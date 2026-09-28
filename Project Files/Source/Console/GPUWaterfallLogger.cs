using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace Thetis;

internal static class GPUWaterfallLogger
{
    private sealed class WorkItem
    {
        public string Line;
    }

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OpenHPSDR", "Thetis-x64", "renderer_diagnostics.log");

    private const long MaxSizeBytes = 8L * 1024L * 1024L;
    private const int MaxQueuedLines = 16384;

    private static readonly ConcurrentQueue<WorkItem> _queue = new ConcurrentQueue<WorkItem>();
    private static readonly ConcurrentDictionary<string, long> _rateTicks = new ConcurrentDictionary<string, long>();
    private static readonly object _startLock = new object();
    private static readonly ManualResetEventSlim _wake = new ManualResetEventSlim(false);

    private static int _queuedCount;
    private static int _droppedLines;
    private static Thread _writerThread;
    private static volatile bool _started;

    private static long _frameSeq;
    private static volatile int _frameInProgress;
    private static long _frameStartTicks;
    private static long _lastFrameEndTicks;
    private static volatile int _frameThreadId;
    private static volatile string _frameStage = "idle";
    private static long _lastWatchdogFrameSeq = -1;
    private static long _lastNoFrameReportTicks;
    private static long _lastStatsTicks;
    private static volatile bool _watchdogArmed;

    public static string FilePath => LogPath;

    public static void Log(string prefix, string message)
    {
        try
        {
            EnsureStarted();
            Enqueue($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [T{Environment.CurrentManagedThreadId:D2}] [{prefix}] {message}");
        }
        catch
        {
        }
    }

    public static void LogRateLimited(string prefix, string key, int intervalMs, string message)
    {
        try
        {
            long now = Stopwatch.GetTimestamp();
            long minTicks = (long)(Stopwatch.Frequency * (intervalMs / 1000.0));
            string fullKey = prefix + "|" + key;
            while (true)
            {
                if (!_rateTicks.TryGetValue(fullKey, out long old))
                {
                    if (_rateTicks.TryAdd(fullKey, now))
                    {
                        Log(prefix, message);
                        return;
                    }
                    continue;
                }

                if (now - old < minTicks) return;
                if (_rateTicks.TryUpdate(fullKey, now, old))
                {
                    Log(prefix, message);
                    return;
                }
            }
        }
        catch
        {
        }
    }

    public static void FrameEnter(string stage)
    {
        try
        {
            EnsureStarted();
            long seq = Interlocked.Increment(ref _frameSeq);
            _watchdogArmed = true;
            _frameThreadId = Environment.CurrentManagedThreadId;
            _frameStage = stage ?? "enter";
            Interlocked.Exchange(ref _frameStartTicks, Stopwatch.GetTimestamp());
            Volatile.Write(ref _frameInProgress, 1);
            if (seq == 1)
                Log("SESSION", "Renderer diagnostics active. File=" + LogPath);
        }
        catch
        {
        }
    }

    public static void FrameStage(string stage)
    {
        try
        {
            _frameStage = stage ?? "?";
        }
        catch
        {
        }
    }

    public static void FrameEnd(string reason)
    {
        try
        {
            long now = Stopwatch.GetTimestamp();
            long start = Interlocked.Read(ref _frameStartTicks);
            double ms = start > 0 ? (now - start) * 1000.0 / Stopwatch.Frequency : 0.0;
            _frameStage = "idle";
            Volatile.Write(ref _frameInProgress, 0);
            Interlocked.Exchange(ref _lastFrameEndTicks, now);
            if (ms >= 250.0)
                Log("SLOW-FRAME", $"seq={Interlocked.Read(ref _frameSeq)} elapsed={ms:F1}ms reason={reason}");
        }
        catch
        {
        }
    }

    public static void FrameStats(string message)
    {
        try
        {
            long now = Stopwatch.GetTimestamp();
            long old = Interlocked.Read(ref _lastStatsTicks);
            if (old != 0 && (now - old) < Stopwatch.Frequency) return;
            if (Interlocked.CompareExchange(ref _lastStatsTicks, now, old) == old)
                Log("FRAME", message);
        }
        catch
        {
        }
    }

    private static void Enqueue(string line)
    {
        if (Interlocked.Increment(ref _queuedCount) > MaxQueuedLines)
        {
            Interlocked.Decrement(ref _queuedCount);
            Interlocked.Increment(ref _droppedLines);
            return;
        }

        _queue.Enqueue(new WorkItem { Line = line });
        _wake.Set();
    }

    private static void EnsureStarted()
    {
        if (_started) return;

        lock (_startLock)
        {
            if (_started) return;
            _started = true;
            _lastFrameEndTicks = Stopwatch.GetTimestamp();

            _writerThread = new Thread(WriterLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = "RendererDiagnostics"
            };
            _writerThread.Start();

            try
            {
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                    Log("UNHANDLED", e.ExceptionObject == null ? "<null>" : e.ExceptionObject.ToString());
                System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
                    Log("TASK-EX", e.Exception == null ? "<null>" : e.Exception.ToString());
                System.Windows.Forms.Application.ThreadException += (s, e) =>
                    Log("UI-EX", e.Exception == null ? "<null>" : e.Exception.ToString());
                AppDomain.CurrentDomain.ProcessExit += (s, e) =>
                {
                    Log("SESSION", "ProcessExit");
                    Flush(1000);
                };
            }
            catch
            {
            }
        }
    }

    public static void RendererStopped()
    {
        try
        {
            _watchdogArmed = false;
            _frameStage = "stopped";
            Volatile.Write(ref _frameInProgress, 0);
            Interlocked.Exchange(ref _lastNoFrameReportTicks, 0);
        }
        catch
        {
        }
    }

    private static void CheckWatchdog()
    {
        try
        {
            if (!_watchdogArmed) return;

            long now = Stopwatch.GetTimestamp();
            long seq = Interlocked.Read(ref _frameSeq);

            if (Volatile.Read(ref _frameInProgress) != 0)
            {
                long start = Interlocked.Read(ref _frameStartTicks);
                double ms = start > 0 ? (now - start) * 1000.0 / Stopwatch.Frequency : 0.0;
                if (ms >= 1200.0 && seq != Interlocked.Read(ref _lastWatchdogFrameSeq))
                {
                    Interlocked.Exchange(ref _lastWatchdogFrameSeq, seq);
                    WriteBatch($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [WATCHDOG] STALL frame={seq} elapsed={ms:F0}ms thread={_frameThreadId} stage={_frameStage}{Environment.NewLine}");
                }
            }
            else
            {
                long lastEnd = Interlocked.Read(ref _lastFrameEndTicks);
                if (lastEnd > 0)
                {
                    double idleMs = (now - lastEnd) * 1000.0 / Stopwatch.Frequency;
                    long lastReport = Interlocked.Read(ref _lastNoFrameReportTicks);
                    if (idleMs >= 2500.0 &&
                        (lastReport == 0 || (now - lastReport) >= 2 * Stopwatch.Frequency))
                    {
                        Interlocked.Exchange(ref _lastNoFrameReportTicks, now);
                        WriteBatch($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [WATCHDOG] NO-FRAME for {idleMs:F0}ms lastSeq={seq}{Environment.NewLine}");
                    }
                }
            }
        }
        catch
        {
        }
    }

    private static void WriterLoop()
    {
        StringBuilder sb = new StringBuilder(65536);
        while (true)
        {
            try
            {
                _wake.Wait(250);
                _wake.Reset();

                while (_queue.TryDequeue(out WorkItem item))
                {
                    sb.AppendLine(item.Line);
                    Interlocked.Decrement(ref _queuedCount);
                }

                if (sb.Length > 0)
                {
                    WriteBatch(sb.ToString());
                    sb.Clear();
                }

                int dropped = Interlocked.Exchange(ref _droppedLines, 0);
                if (dropped > 0)
                    WriteBatch($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [LOGGER] dropped={dropped}{Environment.NewLine}");

                CheckWatchdog();
            }
            catch
            {
            }
        }
    }

    private static void WriteBatch(string text)
    {
        try
        {
            string dir = Path.GetDirectoryName(LogPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxSizeBytes)
            {
                string bak = LogPath + ".bak";
                try
                {
                    if (File.Exists(bak)) File.Delete(bak);
                    File.Move(LogPath, bak);
                }
                catch
                {
                }
            }

            File.AppendAllText(LogPath, text);
        }
        catch
        {
        }
    }

    public static void Flush(int timeoutMs)
    {
        try
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (Volatile.Read(ref _queuedCount) > 0 && sw.ElapsedMilliseconds < timeoutMs)
            {
                _wake.Set();
                Thread.Sleep(20);
            }
        }
        catch
        {
        }
    }
}
