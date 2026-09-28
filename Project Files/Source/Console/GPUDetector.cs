using System;

namespace Thetis
{
    // .NET 10/Vortice adapter for the recovered Waterfall UI.
    // It preserves the original GPUDetector API without loading SharpDX.
    internal static class GPUDetector
    {
        internal enum CapabilityLevel
        {
            Level0_CPU = 0,
            Level1_BuiltIn = 1,
            Level2_Custom_Shaders = 2
        }

        internal static CapabilityLevel Level { get; private set; } = CapabilityLevel.Level0_CPU;
        internal static bool HasDeviceContext { get; private set; }
        internal static bool HasBuiltInEffects { get; private set; }
        internal static bool HasCustomShaders { get; private set; }
        internal static string FeaturesList { get; private set; } = "";
        internal static string GPUName { get; private set; } = "unknown";

        internal static void Refresh(bool hardwareReady, string gpuName)
        {
            GPUName = string.IsNullOrWhiteSpace(gpuName) ? "unknown" : gpuName;
            HasDeviceContext = hardwareReady;
            HasBuiltInEffects = hardwareReady;
            HasCustomShaders = hardwareReady;
            Level = hardwareReady ? CapabilityLevel.Level2_Custom_Shaders : CapabilityLevel.Level0_CPU;
            FeaturesList = hardwareReady
                ? "Vortice D3D11, Compute Shader 5.0, native Waterfall history"
                : "CPU/D2D fallback";
        }
    }
}
