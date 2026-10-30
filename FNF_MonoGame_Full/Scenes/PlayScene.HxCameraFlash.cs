using System;
using FNF_MonoGame.Scenes;

namespace FNF_MonoGame.Engine.PsychEngine
{
    /// <summary>
    /// flixel 5 FlxCamera display-list fields for HScript (camHUD/camGame/camOther as seen by .hx scripts).
    /// Scripts such as YARARARA's CameraFix.hx read/write `_flashOffset`, `flashSprite` and `canvas.transform.matrix`
    /// to move the camera's rotation into the canvas (rotation/zoom about the camera centre, no rotated clip rect).
    /// The port already renders every Psych camera that way (PlayScene.PsychCameras: zoom + angle about the centre,
    /// then x/y), so these are state-only stand-ins with flixel's values:
    ///  _flashOffset = (width * 0.5 * scaleMode.scale.x * initialZoom, height * 0.5 * ...) (updateFlashOffset),
    ///  flashSprite.x/y = x + _flashOffset.x / y + _flashOffset.y, rotation = angle (updateFlashSpritePosition, set_angle;
    ///  refreshed every frame in Update like FlxCamera.update), scaleX/scaleY = zoom (setScale from set_zoom).
    /// </summary>
    public partial class PsychCamera
    {
        private HxPoint _flashOffsetPt;
        public HxPoint _flashOffset => _flashOffsetPt ??= new HxPoint(() => Width * 0.5f, _ => { }, () => Height * 0.5f, _ => { });
        public HxFlashSprite flashSprite { get; } = new();
        public HxCanvas canvas { get; } = new();
        public float scaleX => Zoom;
        public float scaleY => Zoom;
        public float initialZoom => 1f;

        /// <summary>FlxCamera.updateFlashSpritePosition + set_angle (called from Update every frame).</summary>
        internal void UpdateFlashSprite()
        {
            flashSprite.x = X + Width * 0.5f;
            flashSprite.y = Y + Height * 0.5f;
            flashSprite.rotation = Angle;
        }
    }
}

namespace FNF_MonoGame.Scenes
{
    /// <summary>openfl.display.Sprite subset (FlxCamera.flashSprite).</summary>
    public sealed class HxFlashSprite
    {
        public float x, y, rotation, alpha = 1f, scaleX = 1f, scaleY = 1f;
        public bool visible = true;
        public object filters;
    }

    /// <summary>openfl.display.Sprite subset (FlxCamera.canvas) with its transform.</summary>
    public sealed class HxCanvas
    {
        public float x, y, scaleX = 1f, scaleY = 1f, rotation;
        public bool visible = true;
        public HxTransform transform { get; } = new();
    }

    /// <summary>openfl.geom.Transform subset.</summary>
    public sealed class HxTransform
    {
        public HxMatrix matrix = new();
    }

    /// <summary>openfl.geom.Matrix (a b c d tx ty; translate/scale/rotate post-multiply like OpenFL).</summary>
    public sealed class HxMatrix
    {
        public double a = 1, b, c, d = 1, tx, ty;
        public HxMatrix() { }
        public HxMatrix(double a = 1, double b = 0, double c = 0, double d = 1, double tx = 0, double ty = 0)
        { this.a = a; this.b = b; this.c = c; this.d = d; this.tx = tx; this.ty = ty; }
        public void identity() { a = 1; b = 0; c = 0; d = 1; tx = 0; ty = 0; }
        public void translate(double dx, double dy) { tx += dx; ty += dy; }
        public void scale(double sx, double sy) { a *= sx; b *= sy; c *= sx; d *= sy; tx *= sx; ty *= sy; }
        public void rotate(double theta)
        {
            double cos = Math.Cos(theta), sin = Math.Sin(theta);
            double a1 = a * cos - b * sin, b1 = a * sin + b * cos;
            double c1 = c * cos - d * sin, d1 = c * sin + d * cos;
            double tx1 = tx * cos - ty * sin, ty1 = tx * sin + ty * cos;
            a = a1; b = b1; c = c1; d = d1; tx = tx1; ty = ty1;
        }
        public void concat(HxMatrix m)
        {
            double a1 = a * m.a + b * m.c, b1 = a * m.b + b * m.d;
            double c1 = c * m.a + d * m.c, d1 = c * m.b + d * m.d;
            double tx1 = tx * m.a + ty * m.c + m.tx, ty1 = tx * m.b + ty * m.d + m.ty;
            a = a1; b = b1; c = c1; d = d1; tx = tx1; ty = ty1;
        }
        public HxMatrix clone() => new(a, b, c, d, tx, ty);
        public override string ToString() => $"(a={a}, b={b}, c={c}, d={d}, tx={tx}, ty={ty})";
    }

    /// <summary>FlxG.scaleMode (window 1280x720 = game size: scale 1,1).</summary>
    public sealed class HxScaleMode
    {
        public static readonly HxScaleMode Instance = new();
        public HxPoint scale { get; } = new(() => 1f, _ => { }, () => 1f, _ => { });
    }

    /// <summary>flixel.math.FlxMath subset.</summary>
    public static class HxFlxMath
    {
        public static double lerp(double a, double b, double ratio) => a + ratio * (b - a);
        public static double bound(double value, double? min = null, double? max = null)
        {
            double lower = min.HasValue && value < min.Value ? min.Value : value;
            return max.HasValue && lower > max.Value ? max.Value : lower;
        }
        public static double roundDecimal(double value, int precision)
        {
            double mult = 1; for (int i = 0; i < precision; i++) mult *= 10;
            return Math.Round(value * mult, MidpointRounding.AwayFromZero) / mult;
        }
        public static double remapToRange(double value, double start1, double stop1, double start2, double stop2)
            => start2 + (value - start1) * ((stop2 - start2) / (stop1 - start1));
        public static bool inBounds(double value, double? min, double? max) => (!min.HasValue || value >= min) && (!max.HasValue || value <= max);
        public static int signOf(double f) => f < 0 ? -1 : 1;
        public static int maxInt(int a, int b) => Math.Max(a, b);
        public static int minInt(int a, int b) => Math.Min(a, b);
        public static int absInt(int v) => Math.Abs(v);
        public static bool isOdd(double n) => ((int)n & 1) != 0;
        public static bool isEven(double n) => ((int)n & 1) == 0;
        public static int wrap(int value, int min, int max)
        {
            int range = max - min + 1;
            if (value < min) value += range * ((min - value) / range + 1);
            return min + (value - min) % range;
        }
        public const double MIN_VALUE = 0.0000000001;
        public const double MAX_VALUE = 1.7976931348623157e+308;
        public const double SQUARE_ROOT_OF_TWO = 1.41421356237;
        public const double EPSILON = 0.0000001;
    }
}
