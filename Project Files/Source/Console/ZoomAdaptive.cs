using System;

namespace Thetis;

public static class ZoomAdaptive
{
    public const double NARROW_RATIO = 0.03;
    public const double MID_LOW_RATIO = 0.2;
    public const double WIDE_RATIO = 0.9;
    private const float ALPHA_MIN = 0f;
    private const float ALPHA_BASE = 0.002f;
    private const float ALPHA_SR_SCALE = 4E-08f;
    private const float ALPHA_ABSOLUTE_CAP = 0.04f;
    private const int TONEMAP_REINHARD = 1;
    private const int TONEMAP_ACES = 2;

    public static bool Enabled { get; set; } = true;

    public static void ComputeParams(double spanHz, int userToneMap, out int toneMapMode, out float temporalAlpha)
    {
        ComputeParams(spanHz, 0, userToneMap, out toneMapMode, out temporalAlpha);
    }

    public static void ComputeParams(double spanHz, int sampleRateHz, int userToneMap, out int toneMapMode, out float temporalAlpha)
    {
        if (spanHz < 1.0) spanHz = 1.0;
        int num = sampleRateHz > 0 ? sampleRateHz : 192000;
        double num2 = (double)num * 0.03;
        double num3 = (double)num * 0.2;
        double num4 = (double)num * 0.9;
        float num5 = Math.Min(0.04f, 0.002f + (float)num * 4E-08f);
        float num6 = num5 * 0.3f;
        if (spanHz <= num2)
        {
            toneMapMode = 2;
            temporalAlpha = 0f;
        }
        else if (spanHz >= num4)
        {
            toneMapMode = 1;
            temporalAlpha = num5;
        }
        else if (spanHz <= num3)
        {
            double t = (spanHz - num2) / (num3 - num2);
            t = SmoothStepCubic(t);
            toneMapMode = userToneMap;
            temporalAlpha = (float)t * num6;
        }
        else
        {
            double t2 = (spanHz - num3) / (num4 - num3);
            t2 = SmoothStepCubic(t2);
            toneMapMode = userToneMap;
            temporalAlpha = num6 + (float)t2 * (num5 - num6);
        }
    }

    private static double SmoothStepCubic(double t)
    {
        if (t <= 0.0) return 0.0;
        if (t >= 1.0) return 1.0;
        return t * t * (3.0 - 2.0 * t);
    }
}
