using System;

namespace FNF_MonoGame;

/// <summary>
/// Round-half-away-from-zero without MathF/Math.Round(x, MidpointRounding).
/// UWP (.NET Native 2.2 System.Private.CoreLib) implements MathF.Round(float, MidpointRounding.AwayFromZero) via the
/// modff intrinsic, which returns 0 there: the V-Slice health bar was drawn at x=0 (bg) and (0,0) (fill) on Xbox.
/// Pure arithmetic in double is exact for every float input and identical on desktop.
/// </summary>
public static class RoundCompat
{
    public static float AwayFromZero(float x)
    {
        double d = x;
        double r = Math.Floor(Math.Abs(d) + 0.5);
        return (float)(d < 0 ? -r : r);
    }

    public static double AwayFromZero(double x)
    {
        // fraction test (like the BCL's modf path) so 0.49999999999999994 rounds to 0
        double a = Math.Abs(x), f = Math.Floor(a);
        double r = (a - f) >= 0.5 ? f + 1.0 : f;
        return x < 0 ? -r : r;
    }
}
