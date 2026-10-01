using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace Thetis;

internal static class BandUiDiagnostics
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OpenHPSDR", "Thetis-x64", "band_ui_diagnostics.log");

    private const long MaxSizeBytes = 4L * 1024L * 1024L;
    private const int MaxQueuedLines = 8192;

    private static readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();
    private static readonly ManualResetEventSlim _wake = new ManualResetEventSlim(false);
    private static readonly object _startLock = new object();

    private static Thread _writerThread;
    private static volatile bool _started;
    private static int _queuedCount;
    private static int _dropped;

    private static long _sequence;
    private static long _activeSequence;
    private static long _startTicks;
    private static long _lastProgressTicks;
    private static long _lastWatchdogReportTicks;
    private static volatile int _active;
    private static volatile int _uiThreadId;
    private static volatile string _stage = "idle";
    private static volatile string _detail = "";

    public static string FilePath => LogPath;
    public static bool IsActive => Volatile.Read(ref _active) != 0;

    public static void Begin(string detail)
    {
        try
        {
            EnsureStarted();
            long now = Stopwatch.GetTimestamp();
            long seq = Interlocked.Increment(ref _sequence);
            Interlocked.Exchange(ref _activeSequence, seq);
            Interlocked.Exchange(ref _startTicks, now);
            Interlocked.Exchange(ref _lastProgressTicks, now);
            Interlocked.Exchange(ref _lastWatchdogReportTicks, 0);
            _uiThreadId = Environment.CurrentManagedThreadId;
            _stage = "BEGIN";
            _detail = detail ?? "";
            Volatile.Write(ref _active, 1);
            Enqueue(Format("BEGIN", $"seq={seq} thread={_uiThreadId} {detail}"));
        }
        catch { }
    }

    public static void Stage(string stage, string detail = null)
    {
        try
        {
            if (!IsActive) return;
            long now = Stopwatch.GetTimestamp();
            _stage = stage ?? "?";
            _detail = detail ?? "";
            Interlocked.Exchange(ref _lastProgressTicks, now);
            long seq = Interlocked.Read(ref _activeSequence);
            Enqueue(Format("STEP", $"seq={seq} stage={_stage}" +
                (string.IsNullOrEmpty(_detail) ? "" : $" detail={_detail}")));
        }
        catch { }
    }

    public static void End(string detail = null)
    {
        try
        {
            if (!IsActive) return;
            long now = Stopwatch.GetTimestamp();
            long start = Interlocked.Read(ref _startTicks);
            double ms = start > 0 ? (now - start) * 1000.0 / Stopwatch.Frequency : 0.0;
            long seq = Interlocked.Read(ref _activeSequence);
            Enqueue(Format("END", $"seq={seq} elapsed={ms:F1}ms" +
                (string.IsNullOrEmpty(detail) ? "" : $" detail={detail}")));
            _stage = "idle";
            _detail = "";
            Volatile.Write(ref _active, 0);
            _wake.Set();
        }
        catch { }
    }

    public static void Exception(string where, Exception ex)
    {
        try
        {
            EnsureStarted();
            Enqueue(Format("EXCEPTION", $"where={where} {ex}"));
        }
        catch { }
    }

    private static string Format(string kind, string message)
    {
        return $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [T{Environment.CurrentManagedThreadId:D2}] [{kind}] {message}";
    }

    private static void EnsureStarted()
    {
        if (_started) return;
        lock (_startLock)
        {
            if (_started) return;
            _started = true;
            _writerThread = new Thread(WriterLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = "BandUiDiagnostics"
            };
            _writerThread.Start();
            Enqueue(Format("SESSION", "Band/UI diagnostics active. File=" + LogPath));
        }
    }

    private static void Enqueue(string line)
    {
        if (Interlocked.Increment(ref _queuedCount) > MaxQueuedLines)
        {
            Interlocked.Decrement(ref _queuedCount);
            Interlocked.Increment(ref _dropped);
            return;
        }
        _queue.Enqueue(line);
        _wake.Set();
    }

    private static void WriterLoop()
    {
        StringBuilder sb = new StringBuilder(32768);
        while (true)
        {
            try
            {
                _wake.Wait(100);
                _wake.Reset();

                while (_queue.TryDequeue(out string line))
                {
                    sb.AppendLine(line);
                    Interlocked.Decrement(ref _queuedCount);
                }

                if (sb.Length > 0)
                {
                    WriteBatch(sb.ToString());
                    sb.Clear();
                }

                int dropped = Interlocked.Exchange(ref _dropped, 0);
                if (dropped > 0)
                    WriteBatch(Format("LOGGER", $"dropped={dropped}") + Environment.NewLine);

                CheckWatchdog();
            }
            catch { }
        }
    }

    private static void CheckWatchdog()
    {
        if (!IsActive) return;

        long now = Stopwatch.GetTimestamp();
        long progress = Interlocked.Read(ref _lastProgressTicks);
        if (progress <= 0) return;

        double stalledMs = (now - progress) * 1000.0 / Stopwatch.Frequency;
        if (stalledMs < 500.0) return;

        long last = Interlocked.Read(ref _lastWatchdogReportTicks);
        if (last != 0 && (now - last) < Stopwatch.Frequency / 2) return;
        Interlocked.Exchange(ref _lastWatchdogReportTicks, now);

        long start = Interlocked.Read(ref _startTicks);
        double totalMs = start > 0 ? (now - start) * 1000.0 / Stopwatch.Frequency : 0.0;
        long seq = Interlocked.Read(ref _activeSequence);

        string line =
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [WATCHDOG] STALL seq={seq}" +
            $" total={totalMs:F0}ms no_progress={stalledMs:F0}ms uiThread={_uiThreadId}" +
            $" stage={_stage}" +
            (string.IsNullOrEmpty(_detail) ? "" : $" detail={_detail}") +
            Environment.NewLine;

        WriteBatch(line);
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
                catch { }
            }

            File.AppendAllText(LogPath, text);
        }
        catch { }
    }
}
