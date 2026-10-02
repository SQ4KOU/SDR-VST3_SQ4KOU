# WDSP 2.10 migration

Baseline: TAPR/OpenHPSDR-wdsp commit `b02d5bac675dd2f33ec2bab2b339f79a597c47dd` (Release Version 2.10).

Preserved Thetis/SDR-VST3 patches: analyzer pixel_ref, CBL position, NR3/RNNoise, NR4/specbleach, Q-factor EQ/CFCOMP. PureSignal core is the upstream WDSP 2.10 PS3 implementation.

The hardware-test workflow no longer replaces the freshly built wdsp.dll with the old RedPitaya DLL.
