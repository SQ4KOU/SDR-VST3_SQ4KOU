#pragma once

#ifdef __cplusplus
extern "C" {
#endif

__declspec(dllexport) int __cdecl CM_WaterfallIQ_Init(int channel, int requestedSamples);
__declspec(dllexport) void __cdecl CM_WaterfallIQ_SetEnabled(int channel, int enabled);
__declspec(dllexport) int __cdecl CM_WaterfallIQ_Available(int channel);
__declspec(dllexport) int __cdecl CM_WaterfallIQ_Get(int channel, int requestedSamples, float* iOut, float* qOut);
__declspec(dllexport) void __cdecl CM_WaterfallIQ_Free(int channel);
__declspec(dllexport) unsigned __int64 __cdecl CM_WaterfallIQ_DroppedSamples(int channel);
__declspec(dllexport) void __cdecl CM_WaterfallIQ_ResetDropped(int channel);

/* Internal producer hook. data contains interleaved complex doubles: I,Q,I,Q,... */
void CM_WaterfallIQ_Push(int channel, int nsamples, const double* data);

#ifdef __cplusplus
}
#endif
