using System;
using System.Runtime.CompilerServices;

namespace Thetis
{

public class WaterfallPalette
{
	public struct Stop
	{
		public readonly float Pos;

		public readonly float R;

		public readonly float G;

		public readonly float B;

		public Stop(float pos, float r, float g, float b)
		{
			Pos = pos;
			R = r;
			G = g;
			B = b;
		}

		public Stop(float pos, int r, int g, int b)
		{
			Pos = pos;
			R = r;
			G = g;
			B = b;
		}
	}

	private const int LUT_SIZE = 256;

	private readonly float[] _lut = new float[768];

	private Stop[] _stops;

	private float[] _oklabStops;

	private int _oklabStopCount;

	public static Stop[] ConsoleStops => new Stop[12]
	{
		new Stop(0f, 0, 0, 0),
		new Stop(0.12f, 0, 0, 90),
		new Stop(0.25f, 0, 20, 200),
		new Stop(0.38f, 0, 120, 230),
		new Stop(0.48f, 0, 200, 200),
		new Stop(0.55f, 40, 220, 60),
		new Stop(0.65f, 200, 230, 0),
		new Stop(0.72f, 255, 220, 0),
		new Stop(0.8f, 255, 150, 0),
		new Stop(0.88f, 245, 50, 20),
		new Stop(0.95f, 255, 140, 180),
		new Stop(1f, 255, 255, 255)
	};

	public static Stop[] ThermalStops => new Stop[9]
	{
		new Stop(0f, 0, 0, 0),
		new Stop(0.15f, 20, 0, 50),
		new Stop(0.3f, 55, 10, 100),
		new Stop(0.45f, 110, 10, 120),
		new Stop(0.58f, 175, 30, 90),
		new Stop(0.7f, 220, 70, 30),
		new Stop(0.82f, 250, 140, 0),
		new Stop(0.92f, 255, 215, 60),
		new Stop(1f, 255, 255, 220)
	};

	public static Stop[] DeepBlueStops => new Stop[8]
	{
		new Stop(0f, 0, 0, 0),
		new Stop(0.2f, 0, 10, 40),
		new Stop(0.4f, 0, 40, 120),
		new Stop(0.55f, 0, 110, 200),
		new Stop(0.68f, 0, 190, 220),
		new Stop(0.8f, 120, 235, 200),
		new Stop(0.9f, 220, 250, 180),
		new Stop(1f, 255, 255, 255)
	};

	public static Stop[] EnhancedStops => new Stop[8]
	{
		new Stop(0f, 0, 0, 30),
		new Stop(2f / 9f, 0, 0, 255),
		new Stop(1f / 3f, 0, 255, 255),
		new Stop(4f / 9f, 0, 255, 0),
		new Stop(5f / 9f, 255, 255, 0),
		new Stop(7f / 9f, 255, 0, 0),
		new Stop(8f / 9f, 255, 0, 255),
		new Stop(1f, 192, 124, 255)
	};

	public static Stop[] GrayscaleStops => new Stop[2]
	{
		new Stop(0f, 0, 0, 0),
		new Stop(1f, 255, 255, 255)
	};

	public void Build(Stop[] stops)
	{
		_stops = stops;
		if (stops != null && stops.Length != 0)
		{
			_oklabStopCount = stops.Length;
			_oklabStops = new float[stops.Length * 3];
			for (int i = 0; i < stops.Length; i++)
			{
				SrgbToOklab(stops[i].R / 255f, stops[i].G / 255f, stops[i].B / 255f, out _oklabStops[i * 3], out _oklabStops[i * 3 + 1], out _oklabStops[i * 3 + 2]);
			}
			for (int j = 0; j < 256; j++)
			{
				float pos = (float)j / 255f;
				SampleStopsOklab(stops, _oklabStops, pos, out var L, out var A, out var B);
				OklabToSrgb(L, A, B, out var r, out var g, out var b);
				_lut[j * 3] = r * 255f;
				_lut[j * 3 + 1] = g * 255f;
				_lut[j * 3 + 2] = b * 255f;
			}
		}
	}

	private static void SrgbToOklab(float r, float g, float b, out float L, out float A, out float B)
	{
		r = SrgbToLinear(r);
		g = SrgbToLinear(g);
		b = SrgbToLinear(b);
		float num = 0.41222146f * r + 0.53633255f * g + 0.051445995f * b;
		float num2 = 0.2119035f * r + 0.6806995f * g + 0.10739696f * b;
		float num3 = 0.08830246f * r + 0.28171885f * g + 0.6299787f * b;
		num = (((float)Math.Sign(num) * Math.Abs(num) < 1E-06f) ? 0f : Cbrt(num));
		num2 = (((float)Math.Sign(num2) * Math.Abs(num2) < 1E-06f) ? 0f : Cbrt(num2));
		num3 = (((float)Math.Sign(num3) * Math.Abs(num3) < 1E-06f) ? 0f : Cbrt(num3));
		L = 0.21045426f * num + 0.7936178f * num2 - 0.004072047f * num3;
		A = 1.9779985f * num - 2.4285922f * num2 + 0.4505937f * num3;
		B = 0.025904037f * num + 0.78277177f * num2 - 0.80867577f * num3;
	}

	private static void OklabToSrgb(float L, float A, float B, out float r, out float g, out float b)
	{
		float num = L + 0.39633778f * A + 0.21580376f * B;
		float num2 = L - 0.105561346f * A - 0.06385417f * B;
		float num3 = L - 0.08948418f * A - 1.2914855f * B;
		num = num * num * num;
		num2 = num2 * num2 * num2;
		num3 = num3 * num3 * num3;
		r = 4.0767417f * num - 3.3077116f * num2 + 0.23096994f * num3;
		g = -1.268438f * num + 2.6097574f * num2 - 0.34131938f * num3;
		b = -0.0041960864f * num - 0.7034186f * num2 + 1.7076147f * num3;
		r = LinearToSrgb(Clamp01(r));
		g = LinearToSrgb(Clamp01(g));
		b = LinearToSrgb(Clamp01(b));
	}

	private static void SampleStopsOklab(Stop[] stops, float[] oklabStops, float pos, out float L, out float A, out float B)
	{
		if (pos <= stops[0].Pos)
		{
			L = oklabStops[0];
			A = oklabStops[1];
			B = oklabStops[2];
			return;
		}
		int num = stops.Length - 1;
		if (pos >= stops[num].Pos)
		{
			int num2 = num * 3;
			L = oklabStops[num2];
			A = oklabStops[num2 + 1];
			B = oklabStops[num2 + 2];
			return;
		}
		for (int i = 0; i < num; i++)
		{
			if (pos >= stops[i].Pos && pos <= stops[i + 1].Pos)
			{
				float num3 = stops[i + 1].Pos - stops[i].Pos;
				float num4 = ((num3 > 0f) ? ((pos - stops[i].Pos) / num3) : 0f);
				int num5 = i * 3;
				int num6 = (i + 1) * 3;
				L = oklabStops[num5] + (oklabStops[num6] - oklabStops[num5]) * num4;
				A = oklabStops[num5 + 1] + (oklabStops[num6 + 1] - oklabStops[num5 + 1]) * num4;
				B = oklabStops[num5 + 2] + (oklabStops[num6 + 2] - oklabStops[num5 + 2]) * num4;
				return;
			}
		}
		int num7 = num * 3;
		L = oklabStops[num7];
		A = oklabStops[num7 + 1];
		B = oklabStops[num7 + 2];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static float SrgbToLinear(float v)
	{
		if (v <= 0.04045f)
		{
			return v / 12.92f;
		}
		return (float)Math.Pow(((double)v + 0.055) / 1.055, 2.4);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static float LinearToSrgb(float v)
	{
		if (v <= 0.0031308f)
		{
			return v * 12.92f;
		}
		return 1.055f * (float)Math.Pow(v, 5.0 / 12.0) - 0.055f;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static float Clamp01(float v)
	{
		if (!(v < 0f))
		{
			if (!(v > 1f))
			{
				return v;
			}
			return 1f;
		}
		return 0f;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static float Cbrt(float v)
	{
		if (v >= 0f)
		{
			return (float)Math.Pow(v, 1.0 / 3.0);
		}
		return 0f - (float)Math.Pow(0f - v, 1.0 / 3.0);
	}

	public void Sample(float percent, out float r, out float g, out float b)
	{
		if (float.IsNaN(percent) || percent <= 0f)
		{
			percent = 0f;
		}
		else if (percent >= 1f)
		{
			percent = 1f;
		}
		int num = (int)(percent * 255f) * 3;
		r = _lut[num];
		g = _lut[num + 1];
		b = _lut[num + 2];
	}
}

}
