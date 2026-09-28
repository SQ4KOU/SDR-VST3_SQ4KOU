#pragma once
#ifdef __cplusplus
extern "C" {
#endif

__declspec(dllexport) int __cdecl CM_GPUWaterfallExact_Init(int channel, int fftSize, int displayWidth);
__declspec(dllexport) int __cdecl CM_GPUWaterfallExact_Configure(
    int channel, int windowType, float kaiserBeta, int magnitudeMode,
    int lanczosWindow, int resamplingMode);
__declspec(dllexport) int __cdecl CM_GPUWaterfallExact_Process(
    int channel, int sampleRate, float displayLowHz, float displayHighHz,
    const float* iData, const float* qData, int count, float* outputDb);
__declspec(dllexport) int __cdecl CM_GPUWaterfallExact_RenderRow(
    int channel, float lowThreshold, float highThreshold, unsigned char* outputBGRA, int outputBytes);
__declspec(dllexport) void __cdecl CM_GPUWaterfallExact_Free(int channel);

#ifdef __cplusplus
}
#endif
