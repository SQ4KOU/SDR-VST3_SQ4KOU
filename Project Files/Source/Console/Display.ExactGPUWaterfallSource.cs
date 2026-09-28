using System;
using System.Runtime.InteropServices;

namespace Thetis
{
    partial class Display
    {
        private const int ExactGpuFftSize = 16384;
        private const int ExactGpuIqCapacity = 524288;
        private const int ExactGpuOverlapPercent = 85;

        private static readonly object _exactGpuLock = new object();
        private static readonly bool[] _exactGpuInit = new bool[2];
        private static readonly bool[] _exactGpuIqInit = new bool[2];
        private static readonly float[][] _exactRingI = new float[2][];
        private static readonly float[][] _exactRingQ = new float[2][];
        private static readonly int[] _exactRingHead = new int[2];
        private static readonly int[] _exactRingCount = new int[2];
        private static readonly int[] _exactSampleCredit = new int[2];
        private static readonly bool[] _exactFirstFill = new bool[2];
        private static readonly float[][] _exactReadI = new float[2][];
        private static readonly float[][] _exactReadQ = new float[2][];
        private static readonly float[][] _exactFrameI = new float[2][];
        private static readonly float[][] _exactFrameQ = new float[2][];
        private static readonly float[][] _exactRow = new float[2][];
        private static readonly float[][] _exactRowCopy = new float[2][];
        private static readonly byte[][] _exactColorRow = new byte[2][];
        private static readonly float[][] _exactMedianScratch = new float[2][];
        private static readonly float[] _exactCalOffset = new float[2];
        private static readonly bool[] _exactCalInit = new bool[2];
        private static bool _exactGpuUnavailable;
        private static readonly int[] _exactConfiguredWindow = new int[2] { -1, -1 };
        private static readonly float[] _exactConfiguredKaiser = new float[2] { float.NaN, float.NaN };
        private static readonly int[] _exactConfiguredMagnitude = new int[2] { -1, -1 };
        private static readonly int[] _exactConfiguredLanczos = new int[2] { -1, -1 };
        private static readonly int[] _exactConfiguredResampling = new int[2] { -1, -1 };

        // SDR-VST3 stability path: GPU FFT runs on the native dedicated D3D11 device,
        // while the visible waterfall history remains on the normal Vortice/D2D bitmap.
        // This deliberately avoids wrapping the live Vortice device/context in SharpDX.
        private static bool ExactNativeGPURequested =>
            _gpuWaterfallPipelineEnabled &&
            _waterfallRenderQuality == WaterfallRenderQuality.High &&
            !m_bForceCPURendering && m_eRenderPath == DXRenderPath.Hardware;

        private static class ExactGpuNative
        {
            private const string DllName = "ChannelMaster.dll";

            [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int CM_WaterfallIQ_Init(int channel, int requestedSamples);
            [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern void CM_WaterfallIQ_SetEnabled(int channel, int enabled);
            [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int CM_WaterfallIQ_Available(int channel);
            [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int CM_WaterfallIQ_Get(int channel, int requestedSamples, [Out] float[] iOut, [Out] float[] qOut);

            [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int CM_GPUWaterfallExact_Init(int channel, int fftSize, int displayWidth);
            [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int CM_GPUWaterfallExact_Configure(
                int channel, int windowType, float kaiserBeta, int magnitudeMode, int lanczosWindow, int resamplingMode);
            [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int CM_GPUWaterfallExact_Process(
                int channel, int sampleRate, float displayLowHz, float displayHighHz,
                float[] iData, float[] qData, int count, [Out] float[] outputDb);

            [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int CM_GPUWaterfallExact_RenderRow(
                int channel, float lowThreshold, float highThreshold,
                [Out] byte[] outputBGRA, int outputBytes);
        }

        private static void ResetExactGPUWaterfallSourceForModeChange(bool enableIQ)
        {
            lock (_exactGpuLock)
            {
                // A transient init failure must not poison the whole session. A mode,
                // FFT or overlap transition is an explicit retry boundary.
                _exactGpuUnavailable = false;

                for (int slot = 0; slot < 2; slot++)
                {
                    _exactRingHead[slot] = 0;
                    _exactRingCount[slot] = 0;
                    _exactSampleCredit[slot] = 0;
                    _exactFirstFill[slot] = false;
                    _exactCalInit[slot] = false;
                    _exactCalOffset[slot] = 0f;
                    _gpuLastEffectiveOverlap[slot] = -1.0;

                    if (_exactGpuIqInit[slot])
                    {
                        try
                        {
                            ExactGpuNative.CM_WaterfallIQ_SetEnabled(slot, enableIQ ? 1 : 0);
                        }
                        catch
                        {
                        }
                    }
                }
            }

            GPUWaterfallLogger.Log("WF-SOURCE",
                "exact source reset enableIQ=" + enableIQ +
                " forceCPU=" + m_bForceCPURendering +
                " pipeline=" + _gpuWaterfallPipelineEnabled);
        }

        private static int CalculateExactGPUWaterfallHop(int rx, int fftSize, int sampleRate, out double effectiveOverlap)
        {
            int overlap = _gpuWaterfallOverlapPercent;
            if (overlap < 0) overlap = 0;
            if (overlap > 95) overlap = 95;

            int hop = Math.Max(1, Math.Min(fftSize,
                (int)Math.Round(fftSize * (1.0 - overlap / 100.0))));

            if (_gpuWaterfallAutoOverlap)
            {
                double fps = Math.Max(1.0, m_nFps);
                int updatePeriod = Math.Max(1, rx == 1 ? waterfall_update_period : rx2_waterfall_update_period);
                double rowsPerSecond = Math.Min(fps / updatePeriod, 30.0);

                // Preserve the original GPU-waterfall 95% maximum-overlap rule.
                double maxRowsAt95Percent = sampleRate / (0.05 * fftSize);
                if (rowsPerSecond > maxRowsAt95Percent)
                    rowsPerSecond = maxRowsAt95Percent;
                if (rowsPerSecond < 1.0)
                    rowsPerSecond = 1.0;

                hop = Math.Max(1, Math.Min(fftSize,
                    (int)Math.Round(sampleRate / rowsPerSecond)));
            }

            effectiveOverlap = 1.0 - (double)hop / fftSize;
            int slot = rx - 1;
            if (slot >= 0 && slot < 2 &&
                Math.Abs(effectiveOverlap - _gpuLastEffectiveOverlap[slot]) > 0.005)
            {
                _gpuLastEffectiveOverlap[slot] = effectiveOverlap;
                GPUWaterfallEffectiveOverlapChanged?.Invoke(rx, effectiveOverlap);
            }

            return hop;
        }

        // 1 = exact GPU FFT row ready; 0 = healthy source waiting for overlap hop;
        // -1 = unavailable (caller keeps the classic CPU waterfall row).
        private static int TryGetExactGpuWaterfallDataRow(
            int rx, int width, float[] reference, int referenceCount,
            out float[] dataRow)
        {
            dataRow = null;
            if (!ExactNativeGPURequested || _exactGpuUnavailable || console == null || !console.PowerOn ||
                width <= 0 || width > 8192)
                return -1;

            int slot = rx - 1;
            if (slot < 0 || slot > 1) return -1;

            int fftSize = _gpuWaterfallFFTSize;
            if (fftSize < 1024) fftSize = ExactGpuFftSize;
            if (fftSize > 262144) fftSize = 262144;

            lock (_exactGpuLock)
            {
                try
                {
                    if (!_exactGpuIqInit[slot])
                    {
                        if (ExactGpuNative.CM_WaterfallIQ_Init(slot, ExactGpuIqCapacity) <= 0)
                            return -1;
                        ExactGpuNative.CM_WaterfallIQ_SetEnabled(slot, 1);
                        _exactGpuIqInit[slot] = true;
                    }

                    bool sizeChanged = !_exactGpuInit[slot] ||
                        _exactRow[slot] == null || _exactRow[slot].Length != width ||
                        _exactFrameI[slot] == null || _exactFrameI[slot].Length != fftSize;

                    if (sizeChanged)
                    {
                        if (ExactGpuNative.CM_GPUWaterfallExact_Init(slot, fftSize, width) == 0)
                        {
                            GPUWaterfallLogger.Log("WF-SOURCE", "RX" + rx + " exact init FAILED fft=" + fftSize + " width=" + width);
                            _exactGpuUnavailable = true;
                            return -1;
                        }
                        GPUWaterfallLogger.Log("WF-SOURCE", "RX" + rx + " exact init fft=" + fftSize + " width=" + width);

                        _exactGpuInit[slot] = true;
                        _exactRingI[slot] = new float[fftSize];
                        _exactRingQ[slot] = new float[fftSize];
                        _exactReadI[slot] = new float[fftSize * 2];
                        _exactReadQ[slot] = new float[fftSize * 2];
                        _exactFrameI[slot] = new float[fftSize];
                        _exactFrameQ[slot] = new float[fftSize];
                        _exactRow[slot] = new float[width];
                        _exactRowCopy[slot] = new float[width];
                        _exactMedianScratch[slot] = new float[Math.Max(width, referenceCount)];
                        _exactRingHead[slot] = 0;
                        _exactRingCount[slot] = 0;
                        _exactSampleCredit[slot] = 0;
                        _exactFirstFill[slot] = false;
                        _exactCalInit[slot] = false;
                        _exactCalOffset[slot] = 0f;
                        _exactConfiguredWindow[slot] = -1;
                    }

                    int window = (int)_gpuWaterfallWindowType;
                    float kaiser = (float)_gpuWaterfallKaiserBeta;
                    int magnitude = (int)_gpuWaterfallMagnitudeMode;
                    int lanczos = _gpuWaterfallLanczosWindow;
                    int resampling = (int)_gpuWaterfallResamplingMode;
                    if (_exactConfiguredWindow[slot] != window ||
                        _exactConfiguredKaiser[slot] != kaiser ||
                        _exactConfiguredMagnitude[slot] != magnitude ||
                        _exactConfiguredLanczos[slot] != lanczos ||
                        _exactConfiguredResampling[slot] != resampling)
                    {
                        if (ExactGpuNative.CM_GPUWaterfallExact_Configure(slot, window, kaiser, magnitude, lanczos, resampling) == 0)
                        {
                            GPUWaterfallLogger.Log("WF-SOURCE", "RX" + rx + " configure FAILED window=" + window +
                                " mag=" + magnitude + " lanczos=" + lanczos + " resampling=" + resampling);
                            return -1;
                        }
                        GPUWaterfallLogger.Log("WF-SOURCE", "RX" + rx + " configure window=" + window +
                            " mag=" + magnitude + " lanczos=" + lanczos + " resampling=" + resampling);
                        _exactConfiguredWindow[slot] = window;
                        _exactConfiguredKaiser[slot] = kaiser;
                        _exactConfiguredMagnitude[slot] = magnitude;
                        _exactConfiguredLanczos[slot] = lanczos;
                        _exactConfiguredResampling[slot] = resampling;
                    }

                    int available = ExactGpuNative.CM_WaterfallIQ_Available(slot);
                    if (available > 0)
                    {
                        int request = Math.Min(available, fftSize * 2);
                        int got = ExactGpuNative.CM_WaterfallIQ_Get(slot, request, _exactReadI[slot], _exactReadQ[slot]);
                        if (got > 0)
                        {
                            int head = _exactRingHead[slot];
                            int count = _exactRingCount[slot];
                            for (int i = 0; i < got; i++)
                            {
                                // ff62e7ad convention: swap I/Q before FFT.
                                _exactRingI[slot][head] = _exactReadQ[slot][i];
                                _exactRingQ[slot][head] = _exactReadI[slot][i];
                                head = (head + 1) % fftSize;
                            }
                            count = Math.Min(count + got, fftSize);
                            _exactRingHead[slot] = head;
                            _exactRingCount[slot] = count;
                            _exactSampleCredit[slot] += got;
                        }
                    }

                    if (_exactRingCount[slot] < fftSize)
                    {
                        GPUWaterfallLogger.LogRateLimited("WF-SOURCE", "fill-rx" + rx, 1000,
                            "RX" + rx + " filling ring count=" + _exactRingCount[slot] + "/" + fftSize +
                            " credit=" + _exactSampleCredit[slot]);
                        return 0;
                    }

                    int sampleRate = cmaster.GetInputRate(0, slot);
                    if (sampleRate <= 0) sampleRate = rx == 1 ? SampleRateRX1 : SampleRateRX2;
                    if (sampleRate <= 0) sampleRate = 192000;

                    int hop = CalculateExactGPUWaterfallHop(rx, fftSize, sampleRate, out double effectiveOverlap);
                    int effectiveOverlapPercent = (int)Math.Round(effectiveOverlap * 100.0);

                    if (!_exactFirstFill[slot])
                    {
                        _exactFirstFill[slot] = true;
                        _exactSampleCredit[slot] = 0;
                    }
                    else
                    {
                        if (_exactSampleCredit[slot] < hop)
                        {
                            GPUWaterfallLogger.LogRateLimited("WF-SOURCE", "hop-rx" + rx, 1000,
                                "RX" + rx + " waiting hop credit=" + _exactSampleCredit[slot] +
                                " hop=" + hop + " overlap=" + effectiveOverlapPercent + "%" +
                                " auto=" + _gpuWaterfallAutoOverlap);
                            return 0;
                        }
                        _exactSampleCredit[slot] -= hop;
                    }
                    int maxCredit = hop * 2;
                    if (_exactSampleCredit[slot] > maxCredit) _exactSampleCredit[slot] = maxCredit;

                    int ringHead = _exactRingHead[slot];
                    for (int i = 0; i < fftSize; i++)
                    {
                        int src = (ringHead + i) % fftSize;
                        _exactFrameI[slot][i] = _exactRingI[slot][src];
                        _exactFrameQ[slot][i] = _exactRingQ[slot][src];
                    }

                    float lowHz = rx == 1 ? RXDisplayLow : RX2DisplayLow;
                    float highHz = rx == 1 ? RXDisplayHigh : RX2DisplayHigh;

                    int result = ExactGpuNative.CM_GPUWaterfallExact_Process(
                        slot, sampleRate, lowHz, highHz,
                        _exactFrameI[slot], _exactFrameQ[slot], fftSize, _exactRow[slot]);
                    if (result != 1)
                    {
                        GPUWaterfallLogger.LogRateLimited("WF-SOURCE", "process-fail-rx" + rx, 1000,
                            "RX" + rx + " process result=" + result + " sr=" + sampleRate +
                            " fft=" + fftSize + " width=" + width);
                        return -1;
                    }
                    GPUWaterfallLogger.LogRateLimited("WF-SOURCE", "ready-rx" + rx, 1000,
                        "RX" + rx + " READY sr=" + sampleRate + " fft=" + fftSize +
                        " width=" + width + " hop=" + hop + " overlap=" + effectiveOverlapPercent + "%" +
                        " auto=" + _gpuWaterfallAutoOverlap);

                    int refCount = Math.Min(referenceCount, reference == null ? 0 : reference.Length);
                    if (refCount > 8)
                    {
                        float cpuMedian = ExactMedian(reference, refCount, slot);
                        float gpuMedian = ExactMedian(_exactRow[slot], width, slot);
                        float target = cpuMedian - gpuMedian;
                        if (!float.IsNaN(target) && !float.IsInfinity(target) && target >= -12f && target <= 7f)
                        {
                            if (!_exactCalInit[slot])
                            {
                                _exactCalOffset[slot] = target;
                                _exactCalInit[slot] = true;
                            }
                            else
                            {
                                float old = _exactCalOffset[slot];
                                float next = old * 0.9f + target * 0.1f;
                                if (next > old + 1f) next = old + 1f;
                                if (next < old - 1f) next = old - 1f;
                                _exactCalOffset[slot] = next;
                            }
                        }
                    }

                    float offset = _exactCalOffset[slot];
                    float rowMin = float.PositiveInfinity;
                    float rowMax = float.NegativeInfinity;
                    int finiteCount = 0;
                    for (int i = 0; i < width; i++)
                    {
                        float v = _exactRow[slot][i] + offset;
                        _exactRowCopy[slot][i] = v;
                        if (!float.IsNaN(v) && !float.IsInfinity(v))
                        {
                            finiteCount++;
                            if (v < rowMin) rowMin = v;
                            if (v > rowMax) rowMax = v;
                        }
                    }

                    GPUWaterfallLogger.LogRateLimited("WF-DATA", "rx" + rx, 1000,
                        "RX" + rx +
                        " finite=" + finiteCount + "/" + width +
                        " min=" + (finiteCount > 0 ? rowMin.ToString("F1") : "NaN") +
                        " max=" + (finiteCount > 0 ? rowMax.ToString("F1") : "NaN") +
                        " span=" + (finiteCount > 0 ? (rowMax - rowMin).ToString("F1") : "NaN") +
                        " cal=" + offset.ToString("F2"));

                    dataRow = _exactRowCopy[slot];
                    return 1;
                }
                catch (Exception ex)
                {
                    GPUWaterfallLogger.Log("WF-SOURCE-EX", ex.ToString());
                    Common.LogString("Exact native GPU waterfall fallback: " + ex.Message);
                    return -1;
                }
            }
        }

        private static float ExactMedian(float[] source, int count, int slot)
        {
            if (source == null || count <= 0) return -200f;
            int n = Math.Min(count, source.Length);
            float[] scratch = _exactMedianScratch[slot];
            if (scratch == null || scratch.Length < n)
            {
                scratch = new float[n];
                _exactMedianScratch[slot] = scratch;
            }
            Array.Copy(source, scratch, n);
            Array.Sort(scratch, 0, n);
            int mid = n / 2;
            return (n & 1) != 0 ? scratch[mid] : (scratch[mid - 1] + scratch[mid]) * 0.5f;
        }
    }
}
