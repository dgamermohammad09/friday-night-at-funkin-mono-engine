extern alias IRIS;
using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine.PsychEngine;

namespace FNF_MonoGame.Scenes;

/// <summary>Helpers for values coming from the HScript (Haxe->C#) runtime.</summary>
public static class HxUtil
{
    public static object ArrayFirst(object arr)
    {
        if (arr is IRIS::Array<object> a) return a.length > 0 ? a[0] : null;
        if (arr is System.Collections.IList l) return l.Count > 0 ? l[0] : null;
        return null;
    }

    public static object CallFn(object fn, params object[] args)
    {
        if (fn is IRIS::haxe.lang.Function f) return f.__hx_invokeDynamic(args ?? System.Array.Empty<object>());
        if (fn is Delegate d) return d.DynamicInvoke(args);
        return null;
    }

    /// <summary>Fields of a Haxe anonymous object ({x: 1, alpha: 0}).</summary>
    public static IEnumerable<(string, object)> Fields(object anon)
    {
        if (anon == null) yield break;
        var names = IRIS::Reflect.fields(anon);
        for (int i = 0; i < names.length; i++)
        {
            string n = names[i];
            yield return (n, IRIS::Reflect.field(anon, n));
        }
    }

    public static object Field(object anon, string name) => anon == null ? null : IRIS::Reflect.field(anon, name);

    private static MemberInfo FindMember(System.Type t, string name)
    {
        const BindingFlags bf = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;
        // exact name first (camelCase facades), then case-insensitive
        return (MemberInfo)t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance) ?? t.GetField(name, BindingFlags.Public | BindingFlags.Instance)
            ?? (MemberInfo)t.GetProperty(name, bf) ?? t.GetField(name, bf);
    }

    public static double GetNum(object o, string name)
    {
        if (o is FNF_MonoGame.Engine.VObj vo) { try { return FNF_MonoGame.Engine.VObj.D(vo.Get(name)); } catch { return 0; } }   // V-Slice script facades
        var m = o == null ? null : FindMember(o.GetType(), name);
        object v = m is PropertyInfo p ? p.GetValue(o) : m is FieldInfo f ? f.GetValue(o) : null;
        try { return v == null ? 0 : Convert.ToDouble(v); } catch { return 0; }
    }

    public static void SetNum(object o, string name, double value)
    {
        if (o is FNF_MonoGame.Engine.VObj vo) { vo.Set(name, value); return; }
        var m = o == null ? null : FindMember(o.GetType(), name);
        try
        {
            if (m is PropertyInfo p && p.CanWrite) p.SetValue(o, Conv(value, p.PropertyType));
            else if (m is FieldInfo f) f.SetValue(o, Conv(value, f.FieldType));
        }
        catch { }
    }

    private static object Conv(double v, System.Type t)
    {
        if (t == typeof(float)) return (float)v;
        if (t == typeof(double)) return v;
        if (t == typeof(int)) return (int)v;
        if (t == typeof(object)) return v;
        return Convert.ChangeType(v, Nullable.GetUnderlyingType(t) ?? t);
    }

    public static double ToD(object v, double def = 0) { try { return v == null ? def : Convert.ToDouble(v); } catch { return def; } }
}

/// <summary>FlxPoint view bound to engine fields (sprite.scale).</summary>
public sealed class HxPoint
{
    private readonly Func<float> _gx, _gy; private readonly Action<float> _sx, _sy;
    public HxPoint(Func<float> gx, Action<float> sx, Func<float> gy, Action<float> sy) { _gx = gx; _sx = sx; _gy = gy; _sy = sy; }
    public float x { get => _gx(); set => _sx(value); }
    public float y { get => _gy(); set => _sy(value); }
    public HxPoint set(double x = 0, double y = 0) { _sx((float)x); _sy((float)y); return this; }
}

/// <summary>flixel.FlxCamera created by HScript (extra camera). PlayScene draws its sprites after camGame.</summary>
public sealed class HxFlxCamera
{
    public float x, y, angle, alpha = 1f, zoom = 1f;
    public bool visible = true;
    public object bgColor = 0;
    public HxFlxCamera() { }
    public HxFlxCamera(double x, double y = 0, double w = 0, double h = 0, double z = 0) { this.x = (float)x; this.y = (float)y; if (z > 0) zoom = (float)z; }
    public void flash(object color = null, double duration = 1, object onComplete = null, bool force = false) { }
    public void fade(object color = null, double duration = 1, bool fadeIn = false, object onComplete = null, bool force = false) { }
    public void shake(double intensity = 0.05, double duration = 0.5, object onComplete = null, bool force = true) { }
    /// <summary>FlxCamera.copyFrom copies scroll bounds, follow target/style/lerp/deadzone and scroll - not zoom, angle,
    /// alpha or position, none of which this screen-space shim keeps.</summary>
    public HxFlxCamera copyFrom(object cam) => this;
}

/// <summary>FlxG.cameras (add / remove / list order).</summary>
public sealed class HxCameraFrontEnd
{
    public static readonly List<HxFlxCamera> Extra = new();
    public object add(object cam, bool defaultDrawTarget = true) { if (cam is HxFlxCamera c && !Extra.Contains(c)) Extra.Add(c); return cam; }
    public object remove(object cam, bool destroy = true) { if (cam is HxFlxCamera c) Extra.Remove(c); return cam; }
    public void setDefaultDrawTarget(object cam, bool value) { }
}

/// <summary>Subset of flixel.FlxG for HScript.</summary>
public static class HxFlxG
{
    public static readonly HxCameraFrontEnd cameras = new();
    public static int width => 1280;
    public static int height => 720;
    public static double elapsed { get; internal set; } = 1.0 / 60.0;
    public static object camera => PsychCamera.GameCam;
    public static object random => HxFlxRandom.Instance;
    public static HxScaleMode scaleMode => HxScaleMode.Instance;
}

public sealed class HxFlxRandom
{
    public static readonly HxFlxRandom Instance = new();
    private readonly Random _r = new();
    public int @int(int min = 0, int max = int.MaxValue) => _r.Next(min, max + 1);
    public double @float(double min = 0, double max = 1) => min + _r.NextDouble() * (max - min);
    public bool @bool(double chance = 50) => _r.NextDouble() * 100 < chance;
}

public static class HxStringTools
{
    public static bool endsWith(string s, string end) => s != null && end != null && s.EndsWith(end, StringComparison.Ordinal);
    public static bool startsWith(string s, string start) => s != null && start != null && s.StartsWith(start, StringComparison.Ordinal);
    public static string replace(string s, string sub, string by) => s?.Replace(sub, by);
    public static string trim(string s) => s?.Trim();
    public static string ltrim(string s) => s?.TrimStart();
    public static string rtrim(string s) => s?.TrimEnd();
    public static bool contains(string s, string v) => s != null && v != null && s.Contains(v);
}

/// <summary>flixel.tweens.FlxEase (flixel 5 formulas).</summary>
public static class HxFlxEase
{
    private const double PI2 = Math.PI / 2, EL = 2 * Math.PI / .45, B1 = 1 / 2.75, B2 = 2 / 2.75, B3 = 1.5 / 2.75, B4 = 2.5 / 2.75, B5 = 2.25 / 2.75, B6 = 2.625 / 2.75;
    public static Func<double, double> linear => t => t;
    public static Func<double, double> quadIn => t => t * t;
    public static Func<double, double> quadOut => t => -t * (t - 2);
    public static Func<double, double> quadInOut => t => t <= .5 ? t * t * 2 : 1 - (--t) * t * 2;
    public static Func<double, double> cubeIn => t => t * t * t;
    public static Func<double, double> cubeOut => t => 1 + (--t) * t * t;
    public static Func<double, double> cubeInOut => t => t <= .5 ? t * t * t * 4 : 1 + (--t) * t * t * 4;
    public static Func<double, double> quartIn => t => t * t * t * t;
    public static Func<double, double> quartOut => t => 1 - (t -= 1) * t * t * t;
    public static Func<double, double> quartInOut => t => t <= .5 ? t * t * t * t * 8 : (1 - (t = t * 2 - 2) * t * t * t) / 2 + .5;
    public static Func<double, double> quintIn => t => t * t * t * t * t;
    public static Func<double, double> quintOut => t => (t = t - 1) * t * t * t * t + 1;
    public static Func<double, double> quintInOut => t => ((t *= 2) < 1) ? (t * t * t * t * t) / 2 : ((t -= 2) * t * t * t * t + 2) / 2;
    public static Func<double, double> sineIn => t => -Math.Cos(PI2 * t) + 1;
    public static Func<double, double> sineOut => t => Math.Sin(PI2 * t);
    public static Func<double, double> sineInOut => t => -Math.Cos(Math.PI * t) / 2 + .5;
    public static Func<double, double> circIn => t => -(Math.Sqrt(1 - t * t) - 1);
    public static Func<double, double> circOut => t => Math.Sqrt(1 - (t - 1) * (t - 1));
    public static Func<double, double> circInOut => t => t <= .5 ? (Math.Sqrt(1 - t * t * 4) - 1) / -2 : (Math.Sqrt(1 - (t * 2 - 2) * (t * 2 - 2)) + 1) / 2;
    public static Func<double, double> expoIn => t => Math.Pow(2, 10 * (t - 1));
    public static Func<double, double> expoOut => t => -Math.Pow(2, -10 * t) + 1;
    public static Func<double, double> expoInOut => t => t < .5 ? Math.Pow(2, 10 * (t * 2 - 1)) / 2 : (-Math.Pow(2, -10 * (t * 2 - 1)) + 2) / 2;
    public static Func<double, double> backIn => t => t * t * (2.70158 * t - 1.70158);
    public static Func<double, double> backOut => t => 1 - (--t) * (t) * (-2.70158 * t - 1.70158);
    public static Func<double, double> backInOut => t => { t *= 2; if (t < 1) return t * t * (2.70158 * t - 1.70158) / 2; t--; return (1 - (--t) * (t) * (-2.70158 * t - 1.70158)) / 2 + .5; };
    public static Func<double, double> bounceOut => t => t < B1 ? 7.5625 * t * t : t < B2 ? 7.5625 * (t - B3) * (t - B3) + .75 : t < B4 ? 7.5625 * (t - B5) * (t - B5) + .9375 : 7.5625 * (t - B6) * (t - B6) + .984375;
    public static Func<double, double> bounceIn => t => 1 - bounceOut(1 - t);
    public static Func<double, double> bounceInOut => t => t < .5 ? (1 - bounceOut(1 - 2 * t)) / 2 : (1 + bounceOut(2 * t - 1)) / 2;
    public static Func<double, double> elasticIn => t => -(1 * Math.Pow(2, 10 * (t -= 1)) * Math.Sin((t - (0.4 / (2 * Math.PI) * Math.Asin(1 / 1))) * (2 * Math.PI) / 0.4));
    public static Func<double, double> elasticOut => t => (1 * Math.Pow(2, -10 * t) * Math.Sin((t - (0.4 / (2 * Math.PI) * Math.Asin(1 / 1))) * (2 * Math.PI) / 0.4) + 1);
    public static Func<double, double> smoothStepIn => t => 2 * (t / 2) * (t / 2) * (3 - 2 * (t / 2));
    public static Func<double, double> smoothStepOut => t => 2 * ((t / 2 + .5) * (t / 2 + .5) * (3 - 2 * (t / 2 + .5))) - 1;

    public static double Apply(object ease, double t)
    {
        if (ease is Func<double, double> f) return f(t);
        if (ease is IRIS::haxe.lang.Function) return HxUtil.ToD(HxUtil.CallFn(ease, t), t);
        if (ease is string s) { var p = typeof(HxFlxEase).GetProperty(s, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase); if (p?.GetValue(null) is Func<double, double> g) return g(t); }
        return t;
    }
}

/// <summary>flixel.util.FlxTimer (global manager updated by PlayScene every frame, before tweens like flixel's plugin order).</summary>
public sealed class HxFlxTimer
{
    internal static readonly List<HxFlxTimer> Active = new();
    public double time;
    public double timeLeft => Math.Max(0, time - _counter);
    public double elapsedTime => _counter;
    public int loops, elapsedLoops;
    public int loopsLeft => loops <= 0 ? 0 : loops - elapsedLoops;
    public bool active, finished;
    private double _counter;
    private object _cb;
    public HxFlxTimer() { }
    public HxFlxTimer start(double time = 1, object onComplete = null, int loops = 1)
    {
        this.time = Math.Abs(time); _cb = onComplete; this.loops = loops; _counter = 0; elapsedLoops = 0; finished = false; active = true;
        if (!Active.Contains(this)) Active.Add(this);
        return this;
    }
    public HxFlxTimer start(double time, object onComplete) => start(time, onComplete, 1);   // hscript passes only the given args (reflection needs exact arity)
    public HxFlxTimer start(double time) => start(time, null, 1);
    public HxFlxTimer reset(double newTime = -1) { if (newTime < 0) newTime = time; return start(newTime, _cb, loops); }
    public void cancel() { finished = true; active = false; Active.Remove(this); }
    public void destroy() => cancel();

    internal static void UpdateAll(double dt)
    {
        foreach (var t in Active.ToArray())
        {
            if (!t.active || t.finished) continue;
            t._counter += dt;
            while (t._counter >= t.time && t.active && !t.finished)
            {
                t._counter -= t.time; t.elapsedLoops++;
                if (t.loops > 0 && t.elapsedLoops >= t.loops) { t.finished = true; t.active = false; Active.Remove(t); }
                try { HxUtil.CallFn(t._cb, t); } catch (Exception e) { Console.WriteLine($"[HSCRIPT] FlxTimer callback: {e.Message}"); }
                if (t.time <= 0) break;
            }
        }
    }
    public static void ClearAll() => Active.Clear();
}

/// <summary>flixel.tweens.FlxTween.tween / num / cancelTweensOf (VarTween on numeric fields, ONESHOT/PINGPONG/LOOPING).</summary>
public sealed class HxFlxTween
{
    internal static readonly List<HxFlxTween> Active = new();
    public object target; public double duration, startDelay, loopDelay; public bool active = true, finished, backward;
    public int type = 8; // ONESHOT
    private readonly List<(string name, double from, double to)> _props = new();
    private object _ease, _onComplete, _onUpdate, _onStart, _numCb;
    private double _t; private bool _started; private double _numFrom, _numTo;
    public double percent => duration <= 0 ? 1 : Math.Min(1, _t / duration);
    public double scale { get; private set; }

    public static HxFlxTween tween(object target, object values, double duration = 1, object options = null)
    {
        var tw = new HxFlxTween { target = target, duration = duration };
        tw.ReadOptions(options);
        foreach (var (n, v) in HxUtil.Fields(values)) tw._props.Add((n, double.NaN, HxUtil.ToD(v)));
        Active.Add(tw);
        return tw;
    }

    public static HxFlxTween num(double fromValue, double toValue, double duration = 1, object options = null, object tweenFunction = null)
    {
        var tw = new HxFlxTween { duration = duration, _numFrom = fromValue, _numTo = toValue, _numCb = tweenFunction };
        tw.ReadOptions(options);
        Active.Add(tw);
        return tw;
    }

    public static void cancelTweensOf(object obj, object fieldPaths = null)
    {
        foreach (var t in Active.ToArray()) if (ReferenceEquals(t.target, obj)) t.cancel();
    }

    private void ReadOptions(object o)
    {
        if (o == null) return;
        _ease = HxUtil.Field(o, "ease"); _onComplete = HxUtil.Field(o, "onComplete"); _onUpdate = HxUtil.Field(o, "onUpdate"); _onStart = HxUtil.Field(o, "onStart");
        startDelay = HxUtil.ToD(HxUtil.Field(o, "startDelay")); loopDelay = HxUtil.ToD(HxUtil.Field(o, "loopDelay"));
        var ty = HxUtil.Field(o, "type"); if (ty != null) type = (int)HxUtil.ToD(ty, 8);
    }

    public void cancel() { active = false; finished = true; Active.Remove(this); }
    public void destroy() => cancel();

    private void Apply(double k)
    {
        scale = k;
        if (_numCb != null) { HxUtil.CallFn(_numCb, _numFrom + (_numTo - _numFrom) * k); return; }
        for (int i = 0; i < _props.Count; i++)
        {
            var p = _props[i];
            HxUtil.SetNum(target, p.name, p.from + (p.to - p.from) * k);
        }
    }

    internal static void UpdateAll(double dt)
    {
        foreach (var tw in Active.ToArray())
        {
            if (!tw.active) continue;
            tw._t += dt;
            double t = tw._t - tw.startDelay;
            if (t < 0) continue;
            if (!tw._started)
            {
                tw._started = true;
                for (int i = 0; i < tw._props.Count; i++) { var p = tw._props[i]; tw._props[i] = (p.name, HxUtil.GetNum(tw.target, p.name), p.to); }
                if (tw._onStart != null) HxUtil.CallFn(tw._onStart, tw);
            }
            double k = tw.duration <= 0 ? 1 : Math.Min(1, t / tw.duration);
            bool done = k >= 1;
            double e = HxFlxEase.Apply(tw._ease, tw.backward ? 1 - k : k);
            try { tw.Apply(e); if (tw._onUpdate != null) HxUtil.CallFn(tw._onUpdate, tw); }
            catch (Exception ex) { Console.WriteLine($"[HSCRIPT] FlxTween: {ex.Message}"); tw.cancel(); continue; }
            if (done)
            {
                if (tw.type == 2 /*LOOPING*/ || tw.type == 4 /*PINGPONG*/)
                {
                    tw._t = tw.startDelay - tw.loopDelay; if (tw.type == 4) tw.backward = !tw.backward;
                    if (tw._onComplete != null) HxUtil.CallFn(tw._onComplete, tw);
                }
                else
                {
                    tw.finished = true; tw.active = false; Active.Remove(tw);
                    try { if (tw._onComplete != null) HxUtil.CallFn(tw._onComplete, tw); } catch (Exception ex) { Console.WriteLine($"[HSCRIPT] FlxTween onComplete: {ex.Message}"); }
                }
            }
        }
    }
    public static void ClearAll() => Active.Clear();
}

/// <summary>HScript `new FlxSprite(x, y)` (flixel defaults).</summary>
public class HxFlxSprite : ModchartSprite
{
    public HxFlxSprite() : this(0, 0) { }
    public HxFlxSprite(double x, double y = 0) : base((float)x, (float)y)
    {
        ScaleX = ScaleY = 1f; Alpha = 1f; ScrollX = ScrollY = 1f; Visible = true; PsychOffsetX = 0f; PsychOffsetY = 0f;
    }
    public HxFlxSprite(double x, double y, object graphic) : this(x, y) { if (graphic is string s) LoadGraphic(s); }
}

/// <summary>flixel.util.FlxColor subset.</summary>
public static class HxFlxColor
{
    public const int WHITE = unchecked((int)0xFFFFFFFF), BLACK = unchecked((int)0xFF000000), RED = unchecked((int)0xFFFF0000), GREEN = unchecked((int)0xFF008000),
        BLUE = unchecked((int)0xFF0000FF), YELLOW = unchecked((int)0xFFFFFF00), CYAN = unchecked((int)0xFF00FFFF), MAGENTA = unchecked((int)0xFFFF00FF),
        PURPLE = unchecked((int)0xFF800080), ORANGE = unchecked((int)0xFFFFA500), PINK = unchecked((int)0xFFFFC0CB), GRAY = unchecked((int)0xFF808080),
        LIME = unchecked((int)0xFF00FF00), TRANSPARENT = 0;
    public static int fromRGB(int r, int g, int b, int a = 255) => unchecked((int)(((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | (uint)b));
    public static int fromString(string s) { var c = LuaUtils.ColorFromString(s); return fromRGB(c.R, c.G, c.B, c.A); }
}

public partial class PlayScene
{
    /// <summary>flixel plugin order: timers, then tweens (FlxG.plugins update before the state).</summary>
    private void UpdateHxFlixel(float dt)
    {
        HxFlxG.elapsed = dt;
        HxFlxTimer.UpdateAll(dt);
        HxFlxTween.UpdateAll(dt);
    }

    private static readonly bool _hxCamDbg = Environment.GetEnvironmentVariable("FNF_HXCAMLOG") == "1";
    private DateTime _hxCamDbgT;
    internal static void ResetHxFlixel() { HxFlxTimer.ClearAll(); HxFlxTween.ClearAll(); HxCameraFrontEnd.Extra.Clear(); }

    /// <summary>Extra HScript cameras, drawn after camGame in FlxG.cameras order (scroll 0: screen-space sprites).</summary>
    private void DrawHxExtraCameras(SpriteBatch sb)
    {
        if (_hxCamDbg && (DateTime.Now - _hxCamDbgT).TotalSeconds > 2)
        {
            _hxCamDbgT = DateTime.Now;
            int n = 0; foreach (var p in _staticProps) if (p.CameraObj != null) n++;
            var bl = _staticProps.Find(p => p.Name == "bgLayer");
            Console.WriteLine($"[HXCAM] video facade={(_psychVideo != null)} cam={_psychVideo?.camera?.GetType().Name} vid={(_vid != null)} vis={_vid?.Visible}");
            Console.WriteLine($"[HXCAM] cams={HxCameraFrontEnd.Extra.Count} props={n} timers={HxFlxTimer.Active.Count} tweens={HxFlxTween.Active.Count} bgLayer={(bl == null ? "none" : $"cam={bl.Camera} obj={bl.CameraObj != null} a={bl.Alpha} vis={bl.Visible} pos={bl.X},{bl.Y} sc={bl.ScaleX}")}");
        }
        if (HxCameraFrontEnd.Extra.Count == 0) return;
        foreach (var cam in HxCameraFrontEnd.Extra)
        {
            if (!cam.visible || cam.alpha <= 0f) continue;
            var m = Matrix.CreateTranslation(-640f, -360f, 0) * Matrix.CreateScale(cam.zoom, cam.zoom, 1f)
                  * Matrix.CreateRotationZ(MathHelper.ToRadians(cam.angle)) * Matrix.CreateTranslation(640f + cam.x, 360f + cam.y, 0);
            bool any = false;
            if (PsychVideoOnExtraCam && ReferenceEquals(_psychVideo.camera, cam)) { sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied, transformMatrix: m); any = true; PsychDrawVideoInCamera(sb, cam); }
            foreach (var p in _staticProps)
            {
                if (!ReferenceEquals(p.CameraObj, cam) || !p.Visible) continue;
                if (!any) { sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied, transformMatrix: m); any = true; }
                float a = p.Alpha; p.Alpha = a * cam.alpha;
                string c = p.Camera; p.Camera = "hud"; // screen-space placement
                if (string.Equals(p.Blend, "overlay", StringComparison.OrdinalIgnoreCase) && !_overlayExperimental) { p.Camera = c; p.Alpha = a; continue; } // TODO: native look not matched yet (see REPORT)
                if (string.Equals(p.Blend, "overlay", StringComparison.OrdinalIgnoreCase) && BeginOverlayBlend(sb, m))
                {
                    string bl = p.Blend; p.Blend = null;
                    DrawStaticProp(sb, p);
                    p.Blend = bl;
                    sb.End();
                    sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied, transformMatrix: m);
                }
                else DrawStaticProp(sb, p);
                p.Camera = c; p.Alpha = a;
            }
            if (any) sb.End();
        }
    }

    private static readonly bool _overlayExperimental = Environment.GetEnvironmentVariable("FNF_PSYCH_OVERLAY") == "1";
    private Effect _overlayFx; private bool _overlayFxTried; private RenderTarget2D _overlayCopy;
    /// <summary>OpenFL BlendMode.OVERLAY: copy the current target, then draw with Shaders/Overlay.fx sampling the copy.
    /// Ends the running batch and begins one with the effect; caller ends it.</summary>
    private bool BeginOverlayBlend(SpriteBatch sb, Matrix m)
    {
        var gd = Game.GraphicsDevice;
        if (!_overlayFxTried)
        {
            _overlayFxTried = true;
            try
            {
                string path = Assets.ResolvePath("shaders/Overlay.mgfx") ?? System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot, "shaders", "Overlay.mgfx");
                if (global::FNF_MonoGame.Engine.FsCI.FileExists(path)) _overlayFx = new Effect(gd, global::FNF_MonoGame.Engine.FsCI.ReadAllBytes(path));
            }
            catch (Exception e) { Console.WriteLine($"[OVERLAY] {e.Message}"); }
        }
        var cur = gd.GetRenderTargets();
        if (_overlayFx == null || cur.Length == 0 || cur[0].RenderTarget is not RenderTarget2D crt) return false;
        sb.End();
        if (_overlayCopy == null || _overlayCopy.Width != crt.Width || _overlayCopy.Height != crt.Height)
        { _overlayCopy?.Dispose(); _overlayCopy = new RenderTarget2D(gd, crt.Width, crt.Height); }
        var vp = gd.Viewport;
        gd.SetRenderTarget(_overlayCopy);
        sb.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp);
        sb.Draw(crt, Vector2.Zero, Color.White);
        sb.End();
        gd.SetRenderTargets(cur);
        gd.Viewport = vp;
        _overlayFx.Parameters["DestTexture"]?.SetValue(_overlayCopy);
        _overlayFx.Parameters["ScreenSize"]?.SetValue(new Vector2(crt.Width, crt.Height));
        sb.Begin(SpriteSortMode.Immediate, BlendState.NonPremultiplied, SamplerState.LinearClamp, null, null, _overlayFx, m);
        return true;
    }
}
