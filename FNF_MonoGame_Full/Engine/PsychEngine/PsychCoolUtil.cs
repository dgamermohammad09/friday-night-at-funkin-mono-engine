using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine utility helpers matching CoolUtil.hx behavior.
/// </summary>
public static class PsychCoolUtil
{
    public static float Lerp(float a, float b, float ratio)
    {
        return a + (b - a) * MathHelper.Clamp(ratio, 0, 1);
    }

    public static double Lerp(double a, double b, double ratio)
    {
        return a + (b - a) * Math.Clamp(ratio, 0, 1);
    }

    public static int[] NumberArray(int max, int min = 0)
    {
        var result = new List<int>();
        int start = min;
        int end = max;
        if (max < min)
        {
            start = max;
            end = min;
        }
        for (int i = start; i < end; i++)
            result.Add(i);
        return result.ToArray();
    }
}
