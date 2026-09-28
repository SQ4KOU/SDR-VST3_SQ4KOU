using System;
using System.Collections.Generic;
using System.Globalization;

namespace Thetis
{
    partial class Display
    {
        public enum WaterfallRenderQuality { Low, Medium, High }

        private const int GPU_WATERFALL_IQ_CAPACITY = 524288;
        private static bool _gpuWaterfallPipelineEnabled;
        private static int _gpuWaterfallFFTSize = 16384;
        private static int _gpuWaterfallOverlapPercent = 85;
        private static bool _gpuWaterfallAutoOverlap;
        private static GPUWaterfallWindowType _gpuWaterfallWindowType = GPUWaterfallWindowType.Nuttall;
        private static double _gpuWaterfallKaiserBeta = 6.0;
        private static GPUWaterfallMagnitudeMode _gpuWaterfallMagnitudeMode = GPUWaterfallMagnitudeMode.PeakHoldPower;
        private static int _gpuWaterfallLanczosWindow = 3;
        private static GPUWaterfallResamplingMode _gpuWaterfallResamplingMode = GPUWaterfallResamplingMode.Quality;
        private static double[] _gpuLastEffectiveOverlap = new double[2] { -1.0, -1.0 };

        private static WaterfallRenderQuality _waterfallRenderQuality = WaterfallRenderQuality.High;
        private static NoiseFloorPro.DetectionMode _nfMode = NoiseFloorPro.DetectionMode.Average;
        private static float _nfLowPct = 10f;
        private static float _nfHighPct = 99f;
        private static float _wfAgcSmoothing = 0.4f;
        private static float _autoHighMarginDb = 6f;
        private static float _temporalAlpha;
        private static float _autoThresholdFineOffset = -3f;
        private static bool _autoHighEnabledRX1;
        private static bool _autoHighEnabledRX2;
        private static bool _temporalEnabled;
        private static bool _autoThresholdEnabled;
        private static bool _zoomAdaptiveEnabled = true;
        private static bool _autoEnableGPU = true;
        private static bool _gpuEffectsEnabled = true;
        private static float _autoHighRX1 = -40f;
        private static float _autoHighRX2 = -40f;

        private static WaterfallPalette _paletteConsole;
        private static WaterfallPalette _paletteThermal;
        private static WaterfallPalette _paletteDeepBlue;
        private static WaterfallPalette _paletteEnhanced256;
        private static WaterfallPalette _paletteGrayscale256;

        private const string NativeWaterfallSettingsTable = "GPUWaterfallRuntime";
        private static bool _nativeWaterfallSettingsLoaded;

        internal static void EnsureNativeGPUWaterfallSettingsLoaded()
        {
            if (_nativeWaterfallSettingsLoaded) return;
            _nativeWaterfallSettingsLoaded = true;

            try
            {
                Dictionary<string, string> d = DB.GetVarsDictionary(NativeWaterfallSettingsTable);
                if (d == null || d.Count == 0) return;

                bool B(string k, bool v) => d.TryGetValue(k, out string s) && bool.TryParse(s, out bool x) ? x : v;
                int I(string k, int v) => d.TryGetValue(k, out string s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ? x : v;
                float F(string k, float v) => d.TryGetValue(k, out string s) && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ? x : v;
                double D(string k, double v) => d.TryGetValue(k, out string s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ? x : v;

                _gpuWaterfallPipelineEnabled = B("PipelineEnabled", _gpuWaterfallPipelineEnabled);
                _gpuWaterfallFFTSize = I("FFTSize", _gpuWaterfallFFTSize);
                _gpuWaterfallOverlapPercent = I("OverlapPercent", _gpuWaterfallOverlapPercent);
                _gpuWaterfallAutoOverlap = B("AutoOverlap", _gpuWaterfallAutoOverlap);
                _gpuWaterfallWindowType = (GPUWaterfallWindowType)I("WindowType", (int)_gpuWaterfallWindowType);
                _gpuWaterfallKaiserBeta = D("KaiserBeta", _gpuWaterfallKaiserBeta);
                _gpuWaterfallMagnitudeMode = (GPUWaterfallMagnitudeMode)I("MagnitudeMode", (int)_gpuWaterfallMagnitudeMode);
                _gpuWaterfallLanczosWindow = I("LanczosWindow", _gpuWaterfallLanczosWindow);
                _gpuWaterfallResamplingMode = (GPUWaterfallResamplingMode)I("ResamplingMode", (int)_gpuWaterfallResamplingMode);
                _waterfallRenderQuality = (WaterfallRenderQuality)I("RenderQuality", (int)_waterfallRenderQuality);

                _nfMode = (NoiseFloorPro.DetectionMode)I("NFMode", (int)_nfMode);
                _nfLowPct = F("NFLowPct", _nfLowPct);
                _nfHighPct = F("NFHighPct", _nfHighPct);
                _wfAgcSmoothing = F("AgcSmoothing", _wfAgcSmoothing);
                _autoHighEnabledRX1 = B("AutoHighRX1", _autoHighEnabledRX1);
                _autoHighEnabledRX2 = B("AutoHighRX2", _autoHighEnabledRX2);
                _autoHighMarginDb = F("AutoHighMarginDb", _autoHighMarginDb);
                _temporalEnabled = B("TemporalEnabled", _temporalEnabled);
                _temporalAlpha = F("TemporalStrength", _temporalAlpha);
                _autoThresholdEnabled = B("AutoThresholdEnabled", _autoThresholdEnabled);
                _autoThresholdFineOffset = F("AutoThresholdFineOffset", _autoThresholdFineOffset);
                _zoomAdaptiveEnabled = B("ZoomAdaptive", _zoomAdaptiveEnabled);
                _autoEnableGPU = B("AutoEnableGPU", _autoEnableGPU);
                _gpuEffectsEnabled = B("GPUEffectsEnabled", _gpuEffectsEnabled);

                WaterfallEnhancer.SetColorDepth((WaterfallEnhancer.ColorDepth)I("ColorDepth", (int)WaterfallEnhancer.Depth));
                WaterfallEnhancer.SetToneMap((WaterfallEnhancer.ToneMapMode)I("ToneMap", (int)WaterfallEnhancer.ToneMap));
                WaterfallEnhancer.SetPaletteSharpness(F("PaletteSharpness", WaterfallEnhancer.PaletteSharpness));
                WaterfallEnhancer.SetPaletteContrast(F("PaletteContrast", WaterfallEnhancer.PaletteContrast));
                WaterfallEnhancer.SetGamma(F("Gamma", WaterfallEnhancer.Gamma));
                WaterfallEnhancer.SetDither(B("Dither", WaterfallEnhancer.DitherEnabled));
                WaterfallEnhancer.SetQuality((WaterfallEnhancer.QualityLevel)I("QualityLevel", (int)WaterfallEnhancer.Quality));

                if (console != null)
                {
                    int rx1 = I("RX1Palette", (int)console.RX1ColourScheme);
                    int rx2 = I("RX2Palette", (int)console.RX2ColourScheme);
                    int tx = I("TXPalette", (int)console.TXColourScheme);
                    if (Enum.IsDefined(typeof(ColorScheme), rx1)) console.RX1ColourScheme = (ColorScheme)rx1;
                    if (Enum.IsDefined(typeof(ColorScheme), rx2)) console.RX2ColourScheme = (ColorScheme)rx2;
                    if (Enum.IsDefined(typeof(ColorScheme), tx)) console.TXColourScheme = (ColorScheme)tx;
                }

                GPUWaterfallLogger.Log("STATE", "Loaded native GPU Waterfall runtime settings.");
            }
            catch (Exception ex)
            {
                GPUWaterfallLogger.Log("STATE-LOAD-FAIL", ex.Message);
            }
        }

        internal static void PersistNativeGPUWaterfallSettings()
        {
            try
            {
                Dictionary<string, string> d = DB.GetVarsDictionary(NativeWaterfallSettingsTable) ?? new Dictionary<string, string>();

                void Put(string k, object v)
                {
                    if (v is IFormattable fmt) d[k] = fmt.ToString(null, CultureInfo.InvariantCulture);
                    else d[k] = v?.ToString() ?? "";
                }

                Put("PipelineEnabled", _gpuWaterfallPipelineEnabled);
                Put("FFTSize", _gpuWaterfallFFTSize);
                Put("OverlapPercent", _gpuWaterfallOverlapPercent);
                Put("AutoOverlap", _gpuWaterfallAutoOverlap);
                Put("WindowType", (int)_gpuWaterfallWindowType);
                Put("KaiserBeta", _gpuWaterfallKaiserBeta);
                Put("MagnitudeMode", (int)_gpuWaterfallMagnitudeMode);
                Put("LanczosWindow", _gpuWaterfallLanczosWindow);
                Put("ResamplingMode", (int)_gpuWaterfallResamplingMode);
                Put("RenderQuality", (int)_waterfallRenderQuality);

                Put("NFMode", (int)_nfMode);
                Put("NFLowPct", _nfLowPct);
                Put("NFHighPct", _nfHighPct);
                Put("AgcSmoothing", _wfAgcSmoothing);
                Put("AutoHighRX1", _autoHighEnabledRX1);
                Put("AutoHighRX2", _autoHighEnabledRX2);
                Put("AutoHighMarginDb", _autoHighMarginDb);
                Put("TemporalEnabled", _temporalEnabled);
                Put("TemporalStrength", _temporalAlpha);
                Put("AutoThresholdEnabled", _autoThresholdEnabled);
                Put("AutoThresholdFineOffset", _autoThresholdFineOffset);
                Put("ZoomAdaptive", _zoomAdaptiveEnabled);
                Put("AutoEnableGPU", _autoEnableGPU);
                Put("GPUEffectsEnabled", _gpuEffectsEnabled);

                Put("ColorDepth", (int)WaterfallEnhancer.Depth);
                Put("ToneMap", (int)WaterfallEnhancer.ToneMap);
                Put("PaletteSharpness", WaterfallEnhancer.PaletteSharpness);
                Put("PaletteContrast", WaterfallEnhancer.PaletteContrast);
                Put("Gamma", WaterfallEnhancer.Gamma);
                Put("Dither", WaterfallEnhancer.DitherEnabled);
                Put("QualityLevel", (int)WaterfallEnhancer.Quality);

                if (console != null)
                {
                    Put("RX1Palette", (int)console.RX1ColourScheme);
                    Put("RX2Palette", (int)console.RX2ColourScheme);
                    Put("TXPalette", (int)console.TXColourScheme);
                }

                DB.SaveVarsDictionary(NativeWaterfallSettingsTable, ref d, true);
                DB.WriteDB();
                _nativeWaterfallSettingsLoaded = true;
            }
            catch (Exception ex)
            {
                GPUWaterfallLogger.Log("STATE-SAVE-FAIL", ex.Message);
            }
        }

        public static event Action<int, double> GPUWaterfallEffectiveOverlapChanged;

        public static bool GPUWaterfallPipelineEnabled
        {
            get => _gpuWaterfallPipelineEnabled;
            set
            {
                if (_gpuWaterfallPipelineEnabled == value) return;
                GPUWaterfallLogger.Log("STATE", "GPUWaterfallPipelineEnabled " + _gpuWaterfallPipelineEnabled + " -> " + value);
                _gpuWaterfallPipelineEnabled = value;
                try
                {
                    for (int ch = 0; ch < 3; ch++)
                    {
                        if (value) ExactGpuNative.CM_WaterfallIQ_Init(ch, GPU_WATERFALL_IQ_CAPACITY);
                        ExactGpuNative.CM_WaterfallIQ_SetEnabled(ch, value && !m_bForceCPURendering ? 1 : 0);
                    }
                }
                catch { }
                ResetExactGPUWaterfallSourceForModeChange(value && !m_bForceCPURendering);
                SetOwns(1, false);
                SetOwns(2, false);
            }
        }

        public static int GPUWaterfallFFTSize
        {
            get => _gpuWaterfallFFTSize;
            set
            {
                int n = Math.Max(1024, Math.Min(262144, value));
                int p = 1; while (p < n && p < 262144) p <<= 1;
                if (_gpuWaterfallFFTSize == p) return;
                _gpuWaterfallFFTSize = p;
                ResetExactGPUWaterfallSourceForModeChange(_gpuWaterfallPipelineEnabled && !m_bForceCPURendering);
            }
        }

        public static int GPUWaterfallOverlapPercent
        {
            get => _gpuWaterfallOverlapPercent;
            set
            {
                int v = Math.Max(0, Math.Min(95, value));
                if (_gpuWaterfallOverlapPercent == v) return;
                _gpuWaterfallOverlapPercent = v;
                ResetExactGPUWaterfallSourceForModeChange(_gpuWaterfallPipelineEnabled && !m_bForceCPURendering);
            }
        }

        public static bool GPUWaterfallAutoOverlap
        {
            get => _gpuWaterfallAutoOverlap;
            set
            {
                if (_gpuWaterfallAutoOverlap == value) return;
                _gpuWaterfallAutoOverlap = value;
                ResetExactGPUWaterfallSourceForModeChange(_gpuWaterfallPipelineEnabled && !m_bForceCPURendering);
            }
        }

        public static GPUWaterfallWindowType GPUWaterfallWindowType { get => _gpuWaterfallWindowType; set => _gpuWaterfallWindowType = value; }
        public static double GPUWaterfallKaiserBeta { get => _gpuWaterfallKaiserBeta; set => _gpuWaterfallKaiserBeta = Math.Max(0.0, Math.Min(20.0, value)); }
        public static GPUWaterfallMagnitudeMode GPUWaterfallMagnitudeMode { get => _gpuWaterfallMagnitudeMode; set => _gpuWaterfallMagnitudeMode = value; }
        public static int GPUWaterfallLanczosWindow
        {
            get => _gpuWaterfallLanczosWindow;
            set { int v=Math.Max(0,Math.Min(4,value)); if(v==1)v=2; _gpuWaterfallLanczosWindow=v; }
        }
        public static GPUWaterfallResamplingMode GPUWaterfallResamplingMode { get => _gpuWaterfallResamplingMode; set => _gpuWaterfallResamplingMode = value; }

        public static WaterfallRenderQuality WaterfallQuality { get => _waterfallRenderQuality; set => _waterfallRenderQuality = value; }
        public static NoiseFloorPro.DetectionMode NFMode { get => _nfMode; set => _nfMode = value; }
        public static float NFLowPct { get => _nfLowPct; set => _nfLowPct = Math.Max(1f, Math.Min(49f, value)); }
        public static float NFHighPct { get => _nfHighPct; set => _nfHighPct = Math.Max(50f, Math.Min(99.9f, value)); }
        public static float WaterfallAgcSmoothing { get => _wfAgcSmoothing; set => _wfAgcSmoothing = Math.Max(0.05f, Math.Min(0.9f, value)); }
        public static bool AutoHighEnabledRX1 { get => _autoHighEnabledRX1; set => _autoHighEnabledRX1 = value; }
        public static bool AutoHighEnabledRX2 { get => _autoHighEnabledRX2; set => _autoHighEnabledRX2 = value; }
        public static float AutoHighMarginDb { get => _autoHighMarginDb; set => _autoHighMarginDb = Math.Max(0f, Math.Min(30f, value)); }
        public static bool TemporalEnabled { get => _temporalEnabled; set => _temporalEnabled = value; }
        public static float TemporalStrength { get => _temporalAlpha; set => _temporalAlpha = Math.Max(0f, Math.Min(0.5f, value)); }
        public static bool AutoThresholdEnabled { get => _autoThresholdEnabled; set => _autoThresholdEnabled = value; }
        public static float AutoThresholdFineOffset { get => _autoThresholdFineOffset; set => _autoThresholdFineOffset = Math.Max(-20f, Math.Min(20f, value)); }
        public static bool ZoomAdaptiveEnabled { get => _zoomAdaptiveEnabled; set => _zoomAdaptiveEnabled = value; }
        public static bool AutoEnableGPU { get => _autoEnableGPU; set => _autoEnableGPU = value; }
        public static bool GPUEffectsEnabled { get => _gpuEffectsEnabled; set => _gpuEffectsEnabled = value; }
        public static string GPUName => _gpu ?? "unknown";
        public static int GPUDetectionLevel => (int)GPUDetector.Level;

        internal static void DetectGPUCapabilitiesFromD2D()
        {
            GPUDetector.Refresh(!m_bForceCPURendering && m_eRenderPath == DXRenderPath.Hardware && _device != null && _bDX2Setup, GPUName);
        }

        internal static void ResetTemporalWaterfallState() { }

        public static void ApplyWaterfallColorDepth(WaterfallEnhancer.ColorDepth depth)
        {
            WaterfallEnhancer.SetColorDepth(depth);
        }

        internal static void NotifyGPUWaterfallColorDepthChanged()
        {
            SetOwns(1, false);
            SetOwns(2, false);
            GPUWaterfallLogger.Log("STATE", "GPU waterfall color depth changed to " + WaterfallEnhancer.Depth);
        }

        private static void LogGPU(string message) => GPUWaterfallLogger.Log("GPU-DISP", message);

        private static WaterfallPalette GetGPUWaterfallPalette(ColorScheme scheme)
        {
            if (scheme == ColorScheme.Console) return _paletteConsole ??= BuildPalette(WaterfallPalette.ConsoleStops);
            if (scheme == ColorScheme.Thermal) return _paletteThermal ??= BuildPalette(WaterfallPalette.ThermalStops);
            if (scheme == ColorScheme.DeepBlue) return _paletteDeepBlue ??= BuildPalette(WaterfallPalette.DeepBlueStops);
            if (scheme == ColorScheme.Enhanced256) return _paletteEnhanced256 ??= BuildPalette(WaterfallPalette.EnhancedStops);
            if (scheme == ColorScheme.Grayscale256) return _paletteGrayscale256 ??= BuildPalette(WaterfallPalette.GrayscaleStops);
            return null;
        }

        private static WaterfallPalette BuildPalette(WaterfallPalette.Stop[] stops)
        {
            var p = new WaterfallPalette();
            p.Build(stops);
            return p;
        }

        private static void ApplyWaterfallProThresholds(int rx, bool localMox, ref float lowThreshold, ref float highThreshold)
        {
            if (localMox) return;
            if (rx == 2)
            {
                if (_autoThresholdEnabled && m_bNoiseFloorGoodRX2)
                {
                    float nf = m_fLerpAverageRX2 + _fNFshiftDBM;
                    float userOffset = Math.Max(0f, Math.Min(10f, rx2_waterfall_low_threshold - nf));
                    float range = Math.Max(10f, rx2_waterfall_high_threshold - rx2_waterfall_low_threshold);
                    lowThreshold = nf + userOffset + _autoThresholdFineOffset;
                    highThreshold = lowThreshold + range;
                }
                else if (_autoHighEnabledRX2)
                {
                    if (_autoHighRX2 <= -39f) _autoHighRX2 = rx2_waterfall_high_threshold;
                    highThreshold = Math.Max(rx2_waterfall_high_threshold, _autoHighRX2 + _autoHighMarginDb);
                }
                return;
            }

            if (_autoThresholdEnabled && m_bNoiseFloorGoodRX1)
            {
                float nf = m_fLerpAverageRX1 + _fNFshiftDBM;
                float userOffset = Math.Max(0f, Math.Min(10f, waterfall_low_threshold - nf));
                float range = Math.Max(10f, waterfall_high_threshold - waterfall_low_threshold);
                lowThreshold = nf + userOffset + _autoThresholdFineOffset;
                highThreshold = lowThreshold + range;
            }
            else if (_autoHighEnabledRX1)
            {
                if (_autoHighRX1 <= -39f) _autoHighRX1 = waterfall_high_threshold;
                highThreshold = Math.Max(waterfall_high_threshold, _autoHighRX1 + _autoHighMarginDb);
            }
        }
    }
}
