using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xna.Framework;
using MoonSharp.Interpreter;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Lua tweens/timers created by FunkinLua callbacks: flixel FlxTween (VarTween/ColorTween) + FlxTimer semantics as used by
/// Psych 1.0.4 (FunkinLua.oldTweenFunction / startTween / doTweenColor / noteTweenFunction / runTimer, LuaUtils.getTweenEaseByString /
/// getTweenTypeByString / cancelTween / cancelTimer / formatVariable). Updated once per frame by PlayScene: timers first, then tweens
/// (flixel plugin order).
/// </summary>
public static class LuaTweenManager
{
    private static readonly Dictionary<string, LuaTween> _tweens = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, LuaTimer> _timers = new(StringComparer.Ordinal);

    public static void Update(float deltaTime)
    {
        foreach (var timer in new List<LuaTimer>(_timers.Values))
        {
            if (timer.Cancelled) continue;
            timer.Update(deltaTime);
            if (timer.IsFinished && _timers.TryGetValue(timer.Tag, out var cur) && ReferenceEquals(cur, timer)) _timers.Remove(timer.Tag);
        }
        foreach (var tween in new List<LuaTween>(_tweens.Values))
        {
            if (tween.Cancelled) continue;
            tween.Update(deltaTime);
            if (tween.IsFinished && _tweens.TryGetValue(tween.Tag, out var cur) && ReferenceEquals(cur, tween)) _tweens.Remove(tween.Tag);
        }
    }

    /// <summary>LuaUtils.formatVariable: trim, ' ' -> '_', drop '.'.</summary>
    public static string FormatVariable(string tag) => (tag ?? "").Trim().Replace(' ', '_').Replace(".", "");
    private static string TweenKey(string tag) => tag.StartsWith("tween_") ? tag : "tween_" + FormatVariable(tag);
    private static string TimerKey(string tag) => tag.StartsWith("timer_") ? tag : "timer_" + FormatVariable(tag);

    /// <summary>Single-property tween (doTweenX/Y/Alpha/Angle/Zoom): returns the stored tag 'tween_&lt;tag&gt;'.</summary>
    public static string StartTween(string tag, string property, double toValue, double duration, string ease, string targetPath)
    {
        object target = ResolveTweenTarget(targetPath);
        if (target == null) return null;
        return StartTweenOn(tag, target, new Dictionary<string, double> { [property] = toValue }, duration, EaseByString(ease),
            LuaTween.ONESHOT, 0, 0, null, null, null, targetPath, completeArgsVars: true);
    }

    /// <summary>FlxTween.tween(target, values, duration, options) with the Psych Lua callbacks.
    /// completeArgsVars: onTweenCompleted gets (tag, vars) (doTween*) or only (tag) (noteTween*).</summary>
    public static string StartTweenOn(string tag, object target, Dictionary<string, double> values, double duration, Func<double, double> ease,
        int type, double startDelay, double loopDelay, string onStart, string onUpdate, string onComplete, string vars,
        bool completeArgsVars = true, bool startTweenStyle = false)
    {
        if (target == null) return null;
        string key = tag != null ? TweenKey(tag) : "__anon_" + Guid.NewGuid().ToString("N");
        if (tag != null) CancelTween(tag);
        var tw = new LuaTween
        {
            Tag = key, OriginalTag = tag, Vars = vars, Target = target, Duration = duration, EaseFn = ease ?? (t => t),
            Type = type, StartDelay = startDelay, LoopDelay = loopDelay,
            OnStartFn = onStart, OnUpdateFn = onUpdate, OnCompleteFn = onComplete, CompleteArgsVars = completeArgsVars,
            StartTweenStyle = startTweenStyle, Backward = type == LuaTween.BACKWARD,
        };
        foreach (var kv in values)
        {
            var (obj, field) = ResolveField(target, kv.Key);
            object cur = obj == null ? null : LuaUtils.GetVarInArray(obj, field, false);
            double from = 0; try { from = cur == null ? 0 : Convert.ToDouble(cur, CultureInfo.InvariantCulture); } catch { }
            tw.Props.Add(new LuaTween.Prop { Obj = obj, Field = field, From = from, To = kv.Value });
        }
        _tweens[key] = tw;
        return tag != null ? key : null;
    }

    /// <summary>'scale.x' style field paths (FlxTween VarTween supports dotted fields).</summary>
    private static (object, string) ResolveField(object target, string path)
    {
        var parts = path.Split('.');
        object o = target;
        for (int i = 0; i < parts.Length - 1 && o != null; i++) o = LuaUtils.GetVarInArray(o, parts[i], false);
        return (o, parts[parts.Length - 1]);
    }

    /// <summary>LuaUtils.tweenPrepare: the object at the FULL path ('iconP1.scale' -> the scale point; 'boyfriend' -> boyfriend).</summary>
    public static object ResolveTweenTarget(string targetPath)
    {
        if (string.IsNullOrEmpty(targetPath)) return null;
        var split = targetPath.Split('.');
        object parent = LuaUtils.GetPropertyLoop(split, true, false);
        return split.Length > 1 ? LuaUtils.GetVarInArray(parent, split[split.Length - 1], false) : parent;
    }

    /// <summary>doTweenColor(tag, vars, targetColor, duration, ease): FlxTween.color from the current colour (alpha = sprite alpha)
    /// to colorFromString(targetColor); sets both color (RGB) and alpha every frame.</summary>
    public static string StartColorTweenTo(string tag, string targetPath, string toColor, double duration, string ease)
    {
        object target = ResolveTweenTarget(targetPath);
        if (target == null) return null;
        int curRgb = 0xFFFFFF; double curA = 1;
        try { var c = LuaUtils.GetVarInArray(target, "color", false); if (c is Color xc) curRgb = (xc.R << 16) | (xc.G << 8) | xc.B; else if (c != null) curRgb = (int)(Convert.ToInt64(c, CultureInfo.InvariantCulture) & 0xFFFFFF); } catch { }
        try { var a = LuaUtils.GetVarInArray(target, "alpha", false); if (a != null) curA = Convert.ToDouble(a, CultureInfo.InvariantCulture); } catch { }
        var to = LuaUtils.ColorFromString(toColor);
        string key = tag != null ? TweenKey(tag) : "__anon_" + Guid.NewGuid().ToString("N");
        if (tag != null) CancelTween(tag);
        _tweens[key] = new LuaTween
        {
            Tag = key, OriginalTag = tag, Vars = targetPath, Target = target, Duration = duration, EaseFn = EaseByString(ease),
            Type = LuaTween.ONESHOT, IsColorTween = true, CompleteArgsVars = true,
            ColorFromRgb = curRgb, ColorFromA = curA, ColorToRgb = (to.R << 16) | (to.G << 8) | to.B, ColorToA = to.A / 255.0,
        };
        return tag != null ? key : null;
    }

    /// <summary>Legacy entry (from/to colours given): kept for older callers.</summary>
    public static void StartColorTween(string tag, string targetPath, string fromColor, string toColor, double duration, string ease)
    {
        StartColorTweenTo(tag, targetPath, toColor, duration, ease);
        if (tag != null && _tweens.TryGetValue(TweenKey(tag), out var tw))
        {
            var f = LuaUtils.ColorFromString(fromColor);
            tw.ColorFromRgb = (f.R << 16) | (f.G << 8) | f.B; tw.ColorFromA = f.A / 255.0;
        }
    }

    public static void StartSetterTween(string tag, double from, double to, double duration, string ease, Action<double> setter)
    {
        _tweens[tag] = new LuaTween { Tag = tag, Duration = duration, EaseFn = EaseByString(ease), Type = LuaTween.ONESHOT, Setter = setter, SetterFrom = from, SetterTo = to, Silent = true };
    }

    /// <summary>runTimer(tag, time = 1, loops = 1): returns 'timer_&lt;tag&gt;'.</summary>
    public static string StartTimer(string tag, double interval, int loops)
    {
        CancelTimer(tag);
        string key = TimerKey(tag);
        _timers[key] = new LuaTimer { Tag = key, OriginalTag = tag, Interval = Math.Abs(interval), Loops = loops };
        return key;
    }

    public static void CancelTween(string tag)
    {
        if (tag == null) return;
        foreach (var k in new[] { TweenKey(tag), tag })
            if (_tweens.TryGetValue(k, out var t)) { t.Cancelled = true; _tweens.Remove(k); }
    }
    public static void CancelTimer(string tag)
    {
        if (tag == null) return;
        foreach (var k in new[] { TimerKey(tag), tag })
            if (_timers.TryGetValue(k, out var t)) { t.Cancelled = true; _timers.Remove(k); }
    }
    public static bool TweenExists(string tag) => tag != null && (_tweens.ContainsKey(TweenKey(tag)) || _tweens.ContainsKey(tag));
    public static bool TimerExists(string tag) => tag != null && (_timers.ContainsKey(TimerKey(tag)) || _timers.ContainsKey(tag));
    public static void Clear()
    {
        foreach (var t in _tweens.Values) t.Cancelled = true;
        foreach (var t in _timers.Values) t.Cancelled = true;
        _tweens.Clear();
        _timers.Clear();
    }

    public static void CancelAll() => Clear();

    /// <summary>LuaUtils.getTweenTypeByString.</summary>
    public static int TypeByString(string type)
    {
        switch ((type ?? "").ToLowerInvariant().Trim())
        {
            case "backward": return LuaTween.BACKWARD;
            case "looping": case "loop": return LuaTween.LOOPING;
            case "persist": return LuaTween.PERSIST;
            case "pingpong": return LuaTween.PINGPONG;
        }
        return LuaTween.ONESHOT;
    }

    /// <summary>LuaUtils.getTweenEaseByString (flixel 5 FlxEase formulas).</summary>
    public static Func<double, double> EaseByString(string ease)
    {
        switch ((ease ?? "").ToLowerInvariant().Trim())
        {
            case "backin": return FlxEaseF.backIn; case "backinout": return FlxEaseF.backInOut; case "backout": return FlxEaseF.backOut;
            case "bouncein": return FlxEaseF.bounceIn; case "bounceinout": return FlxEaseF.bounceInOut; case "bounceout": return FlxEaseF.bounceOut;
            case "circin": return FlxEaseF.circIn; case "circinout": return FlxEaseF.circInOut; case "circout": return FlxEaseF.circOut;
            case "cubein": return FlxEaseF.cubeIn; case "cubeinout": return FlxEaseF.cubeInOut; case "cubeout": return FlxEaseF.cubeOut;
            case "elasticin": return FlxEaseF.elasticIn; case "elasticinout": return FlxEaseF.elasticInOut; case "elasticout": return FlxEaseF.elasticOut;
            case "expoin": return FlxEaseF.expoIn; case "expoinout": return FlxEaseF.expoInOut; case "expoout": return FlxEaseF.expoOut;
            case "quadin": return FlxEaseF.quadIn; case "quadinout": return FlxEaseF.quadInOut; case "quadout": return FlxEaseF.quadOut;
            case "quartin": return FlxEaseF.quartIn; case "quartinout": return FlxEaseF.quartInOut; case "quartout": return FlxEaseF.quartOut;
            case "quintin": return FlxEaseF.quintIn; case "quintinout": return FlxEaseF.quintInOut; case "quintout": return FlxEaseF.quintOut;
            case "sinein": return FlxEaseF.sineIn; case "sineinout": return FlxEaseF.sineInOut; case "sineout": return FlxEaseF.sineOut;
            case "smoothstepin": return FlxEaseF.smoothStepIn; case "smoothstepinout": return FlxEaseF.smoothStepInOut; case "smoothstepout": return FlxEaseF.smoothStepOut;
            case "smootherstepin": return FlxEaseF.smootherStepIn; case "smootherstepinout": return FlxEaseF.smootherStepInOut; case "smootherstepout": return FlxEaseF.smootherStepOut;
        }
        return FlxEaseF.linear;
    }

    internal static void CallLua(string func, params object[] args)
    {
        if (string.IsNullOrEmpty(func)) return;
        if (MusicBeatState.TargetInstance is FNF_MonoGame.Scenes.PlayScene ps) ps.CallOnLuas(func, args);
    }
}

/// <summary>flixel.tweens.FlxEase (flixel 5.x).</summary>
public static class FlxEaseF
{
    private const double PI2 = Math.PI / 2, EL_A = 1, EL_P = 0.4, B1 = 1 / 2.75, B2 = 2 / 2.75, B3 = 1.5 / 2.75, B4 = 2.5 / 2.75, B5 = 2.25 / 2.75, B6 = 2.625 / 2.75;
    public static double linear(double t) => t;
    public static double quadIn(double t) => t * t;
    public static double quadOut(double t) => -t * (t - 2);
    public static double quadInOut(double t) => t <= .5 ? t * t * 2 : 1 - (--t) * t * 2;
    public static double cubeIn(double t) => t * t * t;
    public static double cubeOut(double t) => 1 + (--t) * t * t;
    public static double cubeInOut(double t) => t <= .5 ? t * t * t * 4 : 1 + (--t) * t * t * 4;
    public static double quartIn(double t) => t * t * t * t;
    public static double quartOut(double t) => 1 - (t -= 1) * t * t * t;
    public static double quartInOut(double t) => t <= .5 ? t * t * t * t * 8 : (1 - (t = t * 2 - 2) * t * t * t) / 2 + .5;
    public static double quintIn(double t) => t * t * t * t * t;
    public static double quintOut(double t) => (t = t - 1) * t * t * t * t + 1;
    public static double quintInOut(double t) => ((t *= 2) < 1) ? (t * t * t * t * t) / 2 : ((t -= 2) * t * t * t * t + 2) / 2;
    public static double smoothStepIn(double t) => 2 * smoothStepInOut(t / 2);
    public static double smoothStepOut(double t) => 2 * smoothStepInOut(t / 2 + 0.5) - 1;
    public static double smoothStepInOut(double t) => t * t * (t * -2 + 3);
    public static double smootherStepIn(double t) => 2 * smootherStepInOut(t / 2);
    public static double smootherStepOut(double t) => 2 * smootherStepInOut(t / 2 + 0.5) - 1;
    public static double smootherStepInOut(double t) => t * t * t * (t * (t * 6 - 15) + 10);
    public static double sineIn(double t) => -Math.Cos(PI2 * t) + 1;
    public static double sineOut(double t) => Math.Sin(PI2 * t);
    public static double sineInOut(double t) => -Math.Cos(Math.PI * t) / 2 + .5;
    public static double bounceIn(double t) => 1 - bounceOut(1 - t);
    public static double bounceOut(double t) => t < B1 ? 7.5625 * t * t : t < B2 ? 7.5625 * (t - B3) * (t - B3) + .75 : t < B4 ? 7.5625 * (t - B5) * (t - B5) + .9375 : 7.5625 * (t - B6) * (t - B6) + .984375;
    public static double bounceInOut(double t) => t < .5 ? (1 - bounceOut(1 - 2 * t)) / 2 : (1 + bounceOut(2 * t - 1)) / 2;
    public static double circIn(double t) => -(Math.Sqrt(1 - t * t) - 1);
    public static double circOut(double t) => Math.Sqrt(1 - (t - 1) * (t - 1));
    public static double circInOut(double t) => t <= .5 ? (Math.Sqrt(1 - t * t * 4) - 1) / -2 : (Math.Sqrt(1 - (t * 2 - 2) * (t * 2 - 2)) + 1) / 2;
    public static double expoIn(double t) => Math.Pow(2, 10 * (t - 1));
    public static double expoOut(double t) => -Math.Pow(2, -10 * t) + 1;
    public static double expoInOut(double t) => t < .5 ? Math.Pow(2, 10 * (t * 2 - 1)) / 2 : (-Math.Pow(2, -10 * (t * 2 - 1)) + 2) / 2;
    public static double backIn(double t) => t * t * (2.70158 * t - 1.70158);
    public static double backOut(double t) => 1 - (--t) * (t) * (-2.70158 * t - 1.70158);
    public static double backInOut(double t) { t *= 2; if (t < 1) return t * t * (2.70158 * t - 1.70158) / 2; t--; return (1 - (--t) * (t) * (-2.70158 * t - 1.70158)) / 2 + .5; }
    public static double elasticIn(double t) => -(EL_A * Math.Pow(2, 10 * (t -= 1)) * Math.Sin((t - (EL_P / (2 * Math.PI) * Math.Asin(1 / EL_A))) * (2 * Math.PI) / EL_P));
    public static double elasticOut(double t) => EL_A * Math.Pow(2, -10 * t) * Math.Sin((t - (EL_P / (2 * Math.PI) * Math.Asin(1 / EL_A))) * (2 * Math.PI) / EL_P) + 1;
    public static double elasticInOut(double t)
    {
        if (t < 0.5) return -0.5 * (Math.Pow(2, 10 * (t -= 0.5)) * Math.Sin((t - (EL_P / 4)) * (2 * Math.PI) / EL_P));
        return Math.Pow(2, -10 * (t -= 0.5)) * Math.Sin((t - (EL_P / 4)) * (2 * Math.PI) / EL_P) * 0.5 + 1;
    }
}

/// <summary>flixel FlxTween (VarTween / ColorTween) with FlxTweenType, startDelay/loopDelay and Psych's Lua callbacks.</summary>
public class LuaTween
{
    public const int PERSIST = 1, LOOPING = 2, PINGPONG = 4, ONESHOT = 8, BACKWARD = 16;
    public sealed class Prop { public object Obj; public string Field; public double From, To; }
    public string Tag { get; set; }
    public string OriginalTag, Vars, OnStartFn, OnUpdateFn, OnCompleteFn;
    public bool CompleteArgsVars = true, StartTweenStyle, Silent, Cancelled;
    public object Target { get; set; }
    public readonly List<Prop> Props = new();
    public double Duration { get; set; }
    public Func<double, double> EaseFn = t => t;
    public int Type = ONESHOT;
    public double StartDelay, LoopDelay;
    public bool Backward;
    public bool IsFinished { get; set; }
    public bool IsColorTween;
    public int ColorFromRgb, ColorToRgb; public double ColorFromA = 1, ColorToA = 1;
    public Action<double> Setter; public double SetterFrom, SetterTo;
    private double _sss; private int _executions; private bool _running;

    public void Update(float deltaTime)
    {
        if (IsFinished || Cancelled) return;
        _sss += deltaTime;
        double delay = _executions > 0 ? LoopDelay : StartDelay;
        if (_sss < delay) return;
        double scale = Duration > 0 ? Math.Max(_sss - delay, 0) / Duration : 1;
        if (EaseFn != null) scale = EaseFn(Math.Min(scale, 1));
        if (Backward) scale = 1 - scale;
        if (_sss > delay && !_running) { _running = true; if (StartTweenStyle) LuaTweenManager.CallLua(OnStartFn, OriginalTag != null ? Tag : null, Vars); }
        bool done = _sss >= Duration + delay;
        if (done) scale = Backward ? 0 : 1;
        else if (StartTweenStyle) LuaTweenManager.CallLua(OnUpdateFn, OriginalTag != null ? Tag : null, Vars);
        Apply(scale);
        if (!done) return;
        _executions++;
        // FlxTween.finish: onComplete, then type handling
        if (StartTweenStyle) LuaTweenManager.CallLua(OnCompleteFn, OriginalTag != null ? Tag : null, Vars);
        else if (!Silent && OriginalTag != null)
        {
            if (CompleteArgsVars) LuaTweenManager.CallLua("onTweenCompleted", OriginalTag, Vars);
            else LuaTweenManager.CallLua("onTweenCompleted", OriginalTag);
        }
        if (Type == LOOPING || Type == PINGPONG)
        {
            _sss = Duration > 0 ? (_sss - delay) % Duration : 0;
            if (Type == PINGPONG) Backward = !Backward;
            _running = false;
            return;
        }
        IsFinished = true; // ONESHOT / BACKWARD / PERSIST stop updating
    }

    private void Apply(double scale)
    {
        if (Setter != null) { Setter(SetterFrom + (SetterTo - SetterFrom) * scale); return; }
        if (IsColorTween)
        {
            int Ch(int rgb, int sh) => (rgb >> sh) & 255;
            int r = (int)Math.Round(Ch(ColorFromRgb, 16) + (Ch(ColorToRgb, 16) - Ch(ColorFromRgb, 16)) * scale);
            int g = (int)Math.Round(Ch(ColorFromRgb, 8) + (Ch(ColorToRgb, 8) - Ch(ColorFromRgb, 8)) * scale);
            int b = (int)Math.Round(Ch(ColorFromRgb, 0) + (Ch(ColorToRgb, 0) - Ch(ColorFromRgb, 0)) * scale);
            double a = ColorFromA + (ColorToA - ColorFromA) * scale;
            object cur = null; try { cur = LuaUtils.GetVarInArray(Target, "color", false); } catch { }
            if (cur is Color) LuaUtils.SetVarInArray(Target, "color", new Color(r, g, b));
            else LuaUtils.SetVarInArray(Target, "color", (r << 16) | (g << 8) | b);
            LuaUtils.SetVarInArray(Target, "alpha", a);
            return;
        }
        foreach (var p in Props)
            if (p.Obj != null) LuaUtils.SetVarInArray(p.Obj, p.Field, p.From + (p.To - p.From) * scale);
    }
}

/// <summary>flixel FlxTimer: loops (0 = forever); one onTimerCompleted(tag, loops, loopsLeft) per frame.</summary>
public class LuaTimer
{
    public string Tag { get; set; }
    public string OriginalTag;
    public double Interval { get; set; }
    public int Loops { get; set; }
    public double Elapsed { get; set; }
    public int CompletedLoops { get; set; }
    public bool IsFinished { get; set; }
    public bool Cancelled;

    public void Update(float deltaTime)
    {
        if (IsFinished || Cancelled) return;
        Elapsed += deltaTime;
        bool looped = false;
        while (Elapsed >= Interval && !IsFinished)
        {
            Elapsed -= Interval;
            CompletedLoops++;
            looped = true;
            if (Loops > 0 && CompletedLoops >= Loops) IsFinished = true;
            if (Interval <= 0) break;
        }
        if (looped) LuaTweenManager.CallLua("onTimerCompleted", OriginalTag ?? Tag, Loops, Loops - CompletedLoops);
    }
}
