using System;

namespace Thetis;

public static class WaterfallEnhancer
{
	public enum ColorDepth
	{
		Bit8,
		Bit10,
		Bit16
	}

	public enum QualityLevel
	{
		Classic,
		Vivid,
		Sharp,
		Ultra
	}

	public enum ToneMapMode
	{
		None,
		Reinhard,
		ACES
	}

	private static float _paletteSharpness = 0f;

	private static float _paletteContrast = 0f;

	private static float _invGamma = 1f;

	private static readonly int[,] BAYER_8X8 = new int[8, 8]
	{
		{ 0, 32, 8, 40, 2, 34, 10, 42 },
		{ 48, 16, 56, 24, 50, 18, 58, 26 },
		{ 12, 44, 4, 36, 14, 46, 6, 38 },
		{ 60, 28, 52, 20, 62, 30, 54, 22 },
		{ 3, 35, 11, 43, 1, 33, 9, 41 },
		{ 51, 19, 59, 27, 49, 17, 57, 25 },
		{ 15, 47, 7, 39, 13, 45, 5, 37 },
		{ 63, 31, 55, 23, 61, 29, 53, 21 }
	};

	public static ColorDepth Depth { get; private set; } = ColorDepth.Bit8;

	public static QualityLevel Quality { get; private set; } = QualityLevel.Classic;

	public static float SaturationBoost { get; private set; } = 0f;

	public static float ContrastBoost { get; private set; } = 0f;

	public static int LUT_SIZE { get; private set; } = 101;

	public static float PaletteSharpness => _paletteSharpness;

	public static float PaletteContrast => _paletteContrast;

	public static ToneMapMode ToneMap { get; private set; } = ToneMapMode.None;

	public static bool DitherEnabled { get; private set; } = false;

	public static float Gamma { get; private set; } = 1f;

	public static int Levels => Depth switch
	{
		ColorDepth.Bit8 => 255, 
		ColorDepth.Bit10 => 1023, 
		ColorDepth.Bit16 => 65535, 
		_ => 255, 
	};

	public static void SetPaletteSharpness(float value)
	{
		_paletteSharpness = ((value < 0f) ? 0f : ((value > 1.5f) ? 1.5f : value));
	}

	public static void SetPaletteContrast(float value)
	{
		_paletteContrast = ((value < 0f) ? 0f : ((value > 1.5f) ? 1.5f : value));
	}

	public static void SetToneMap(ToneMapMode mode)
	{
		ToneMap = mode;
	}

	public static void SetColorDepth(ColorDepth depth)
	{
		Depth = depth;
	}

	public static void SetPaletteResolution(int size)
	{
		if (size == 101 || size == 256 || size == 512 || size == 1024)
		{
			LUT_SIZE = size;
		}
	}

	public static void SetQuality(QualityLevel level)
	{
		Quality = level;
		switch (level)
		{
		default:
			SaturationBoost = 0f;
			ContrastBoost = 0f;
			break;
		case QualityLevel.Vivid:
			SaturationBoost = 0.3f;
			ContrastBoost = 0f;
			break;
		case QualityLevel.Sharp:
			SaturationBoost = 0.3f;
			ContrastBoost = 0.25f;
			break;
		case QualityLevel.Ultra:
			SaturationBoost = 0.4f;
			ContrastBoost = 0.3f;
			break;
		}
	}

	public static void SetDither(bool enabled)
	{
		DitherEnabled = enabled;
	}

	public static void SetGamma(float gamma)
	{
		if (gamma < 0.5f)
		{
			gamma = 0.5f;
		}
		if (gamma > 2f)
		{
			gamma = 2f;
		}
		Gamma = gamma;
		_invGamma = 1f / gamma;
	}

	public static float ApplyGamma(float percent)
	{
		if (percent <= 0f)
		{
			return 0f;
		}
		if (percent >= 1f)
		{
			return 1f;
		}
		if (Gamma == 1f)
		{
			return percent;
		}
		return (float)Math.Pow(percent, _invGamma);
	}

	public static float ApplyQualityPercent(float percent)
	{
		if (ContrastBoost <= 0f)
		{
			return percent;
		}
		if (percent <= 0f)
		{
			return 0f;
		}
		if (percent >= 1f)
		{
			return 1f;
		}
		float num = 0.5f - (float)Math.Cos(Math.PI * (double)percent) * 0.5f;
		return percent + (num - percent) * ContrastBoost;
	}

	public static void ApplySaturationContrast(float[] rowF, int idx)
	{
		if (!(SaturationBoost <= 0f) || !(ContrastBoost <= 0f))
		{
			float num = rowF[idx];
			float num2 = rowF[idx + 1];
			float num3 = rowF[idx + 2];
			if (SaturationBoost > 0f)
			{
				float num4 = 0.299f * num + 0.587f * num2 + 0.114f * num3;
				num = num4 + (num - num4) * (1f + SaturationBoost);
				num2 = num4 + (num2 - num4) * (1f + SaturationBoost);
				num3 = num4 + (num3 - num4) * (1f + SaturationBoost);
			}
			if (ContrastBoost > 0f)
			{
				float num5 = 1f + ContrastBoost;
				num = 128f + (num - 128f) * num5;
				num2 = 128f + (num2 - 128f) * num5;
				num3 = 128f + (num3 - 128f) * num5;
			}
			if (num < 0f)
			{
				num = 0f;
			}
			else if (num > 255f)
			{
				num = 255f;
			}
			if (num2 < 0f)
			{
				num2 = 0f;
			}
			else if (num2 > 255f)
			{
				num2 = 255f;
			}
			if (num3 < 0f)
			{
				num3 = 0f;
			}
			else if (num3 > 255f)
			{
				num3 = 255f;
			}
			rowF[idx] = num;
			rowF[idx + 1] = num2;
			rowF[idx + 2] = num3;
		}
	}

	public static float ApplyDitherFloat(float value, int x, int y)
	{
		if (!DitherEnabled)
		{
			return value;
		}
		int num = BAYER_8X8[y & 7, x & 7];
		float num2 = 255f / (float)(Levels - 1);
		float num3 = ((float)num - 31.5f) / 63f * num2;
		float num4 = value + num3;
		if (num4 < 0f)
		{
			num4 = 0f;
		}
		else if (num4 > 255f)
		{
			num4 = 255f;
		}
		return num4;
	}

	public static float ApplyGammaFloat(float value)
	{
		if (Gamma == 1f)
		{
			return value;
		}
		double x = (double)value / 255.0;
		double num = 255.0 * Math.Pow(x, Gamma);
		if (num < 0.0)
		{
			num = 0.0;
		}
		else if (num > 255.0)
		{
			num = 255.0;
		}
		return (float)num;
	}

	public static float ApplyToneMap(float value)
	{
		return ApplyToneMapMode(value, (int)ToneMap);
	}

	public static float ApplyToneMapMode(float value, int mode)
	{
		if (mode == 0)
		{
			return value;
		}
		float num = value / 255f;
		if (num < 0f)
		{
			num = 0f;
		}
		float num2;
		if (mode == 1)
		{
			num2 = num / (1f + num);
		}
		else
		{
			float num3 = num * (2.51f * num + 0.03f);
			float num4 = num * (2.43f * num + 0.59f) + 0.14f;
			num2 = num3 / num4;
			if (num2 < 0f)
			{
				num2 = 0f;
			}
			else if (num2 > 1f)
			{
				num2 = 1f;
			}
		}
		return num2 * 255f;
	}
}
