using System;
using System.Runtime.CompilerServices;

namespace Thetis;

public static class NoiseFloorPro
{
	public enum DetectionMode
	{
		Average,
		Percentile
	}

	public static float Percentile(float[] data, int count, float percentile)
	{
		if (data == null || count <= 0)
		{
			return -200f;
		}
		if (count == 1)
		{
			return data[0];
		}
		float num = percentile / 100f * (float)(count - 1);
		int num2 = (int)Math.Floor(num);
		int num3 = (int)Math.Ceiling(num);
		if (num2 < 0)
		{
			num2 = 0;
		}
		if (num3 > count - 1)
		{
			num3 = count - 1;
		}
		float num4 = Quickselect(data, count, num2);
		if (num2 == num3)
		{
			return num4;
		}
		float num5 = Quickselect(data, count, num3);
		float num6 = num - (float)num2;
		return num4 + (num5 - num4) * num6;
	}

	public static void ComputeLowHigh(float[] data, int count, float lowPct, float highPct, out float lowDbm, out float highDbm)
	{
		lowDbm = Percentile(data, count, lowPct);
		highDbm = Percentile(data, count, highPct);
	}

	private static float Quickselect(float[] data, int count, int k)
	{
		int num = 0;
		int num2 = count - 1;
		while (num < num2)
		{
			int num3 = num + (num2 - num >> 1);
			if (data[num3] < data[num])
			{
				Swap(data, num, num3);
			}
			if (data[num2] < data[num])
			{
				Swap(data, num, num2);
			}
			if (data[num3] < data[num2])
			{
				Swap(data, num3, num2);
			}
			float num4 = data[num2];
			int num5 = num;
			int num6 = num;
			int num7 = num2;
			while (num6 < num7)
			{
				if (data[num6] < num4)
				{
					Swap(data, num5, num6);
					num5++;
					num6++;
				}
				else if (data[num6] > num4)
				{
					num7--;
					Swap(data, num6, num7);
				}
				else
				{
					num6++;
				}
			}
			int num8 = num2 - num7 + 1;
			for (int i = 0; i < num8; i++)
			{
				Swap(data, num5 + i, num7 + i);
			}
			int num9 = num5;
			if (k < num9)
			{
				num2 = num9 - 1;
				continue;
			}
			if (k >= num9 + num8)
			{
				num = num9 + num8;
				continue;
			}
			return num4;
		}
		return data[num];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void Swap(float[] data, int a, int b)
	{
		float num = data[a];
		data[a] = data[b];
		data[b] = num;
	}
}
