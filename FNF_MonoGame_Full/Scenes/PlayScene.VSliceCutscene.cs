extern alias IRIS;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json.Linq;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.PureHScript;

namespace FNF_MonoGame.Scenes;

/// <summary>V-Slice script runtime pieces used by mod song cutscenes (QT Rewired blissful.hxc intro):
/// COUNTDOWN_START cancel + PlayState.startCountdown(), camHUD.visible/alpha, camCutscene.fade, FunkinSound.load,
/// funkin.util.Sequence, a script-made Conductor (mapTimeChanges/update/onBeatHit) and Save.instance.modOptions.</summary>
public partial class PlayScene
{
    private bool _vsHoldCountdown;            // onCountdownStart was cancelled: no countdown until startCountdown()
    internal float VsHudAlpha = 1f;           // camHUD.alpha set by scripts
    internal bool VsHudVisible = true;        // camHUD.visible set by scripts
    private readonly List<VsSequence> _vsSequences = new();
    private readonly List<VsSound> _vsSounds = new();
    // camCutscene.fade(color, duration, fadeIn, onComplete, force)
    private Color _vsFadeColor; private float _vsFadeDur, _vsFadeT; private bool _vsFadeIn, _vsFadeActive, _vsFadeHold; private object _vsFadeDone;

    /// <summary>Dispatch COUNTDOWN_START; returns false when a script cancelled it (PlayState.startCountdown).</summary>
    private bool VsCountdownStartChecked()
    {
        if (_vsPlayState == null) return true;
        var ev = VsDispatch("onCountdownStart", new VEvent("COUNTDOWN_START"));
        if (!ev.Cancelled) return true;
        _vsHoldCountdown = true;
        _dialogueActive = true;              // isInCutscene: the song waits
        return false;
    }

    /// <summary>PlayState.instance.startCountdown() from a script.</summary>
    public void VsStartCountdown()
    {
        if (!_vsHoldCountdown) return;
        var ev = VsDispatch("onCountdownStart", new VEvent("COUNTDOWN_START"));
        if (ev.Cancelled) return;
        _vsHoldCountdown = false;
        W6StartCountdown(true);
    }

    /// <summary>Per frame while the countdown is held by a script (characters, props, camera, script timers).</summary>
    private void VsHoldUpdate(float dt)
    {
        W1UpdateBlocked(dt);
        UpdateCameraZoomTween(dt);
        UpdateCameraFollowTween(dt);
        if (!_camFollowTweening)
        {
            float k = 1f - MathF.Pow(1f - 0.04f, dt * 60f);
            _camWorldX += (_camTargetWorldX - _camWorldX) * k;
            _camWorldY += (_camTargetWorldY - _camWorldY) * k;
        }
        VsUpdate(dt);
    }

    /// <summary>Called from VsUpdate: sequences, sounds, the camCutscene fade.</summary>
    private void VsCutsceneTick(float dt)
    {
        foreach (var s in _vsSequences.ToList()) s.Tick(dt);
        foreach (var s in _vsSounds) s.Tick(dt);
        if (_vsFadeActive)
        {
            _vsFadeT += dt;
            if (_vsFadeT >= _vsFadeDur)
            {
                _vsFadeActive = false; _vsFadeHold = !_vsFadeIn;
                var cb = _vsFadeDone; _vsFadeDone = null;
                if (cb != null) try { HxUtil.CallFn(cb); } catch (Exception ex) { Console.WriteLine($"[VSLICE-SCRIPT] fade onComplete: {ex.Message}"); }
            }
        }
    }

    public void VsCameraFade(object[] a)
    {
        long c = a.Length > 0 && a[0] != null ? (long)VObj.D(a[0]) : 0xFF000000L;
        _vsFadeColor = new Color((int)((c >> 16) & 255), (int)((c >> 8) & 255), (int)(c & 255));
        _vsFadeDur = Math.Max(0.0001f, a.Length > 1 ? (float)VObj.D(a[1]) : 1f);
        _vsFadeIn = a.Length > 2 && VObj.B(a[2]);
        _vsFadeDone = a.Length > 3 ? a[3] : null;
        _vsFadeT = 0f; _vsFadeActive = true; _vsFadeHold = false;
    }

    /// <summary>FlxCamera fade overlay (camCutscene is above camHUD).</summary>
    private void VsDrawCutsceneOverlay(SpriteBatch sb)
    {
        VsDrawTexts(sb, true);   // camCutscene members (FlxText) under the camera fade
        float alpha;
        if (_vsFadeActive) { float t = Math.Min(1f, _vsFadeT / _vsFadeDur); alpha = _vsFadeIn ? 1f - t : t; }
        else alpha = _vsFadeHold ? 1f : 0f;
        if (alpha <= 0f) return;
        sb.End();
        sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);
        sb.Draw(Assets.Pixel, new Rectangle(0, 0, 1280, 720), new Color(_vsFadeColor, Math.Clamp(alpha, 0f, 1f)));
        sb.End();
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
    }

    internal VsSound VsLoadSound(string path, bool looped)
    {
        var s = new VsSound(this, path, looped);
        _vsSounds.Add(s);
        return s;
    }
    internal void VsAddSequence(VsSequence s) { _vsSequences.Add(s); }
    internal AudioManagerShim VsAudio => new AudioManagerShim(this);
    internal readonly struct AudioManagerShim
    {
        private readonly PlayScene _ps; public AudioManagerShim(PlayScene ps) { _ps = ps; }
        public SoundEffectInstance Create(string path) { try { return _ps.Audio.CreateSoundInstance(path); } catch { return null; } }
    }

    public Vector2 VsFocusPoint(FNF_MonoGame.Gameplay.Character c) => W1FocusPoint(c);
    public float VsStageZoom => _stageZoom > 0f ? _stageZoom : _defaultCamZoom;
    internal void VsSetHudHidden(bool hidden) { _w6HudHidden = hidden; }

    private void VsStopCutsceneSounds() { foreach (var s in _vsSounds) s.Stop(); _vsSounds.Clear(); _vsSequences.Clear(); }
}

/// <summary>FunkinSound.load(path, looped): play/stop/pause/resume/volume/time (ms, tracked by elapsed time).</summary>
public sealed class VsSound : VObj
{
    private readonly PlayScene _ps; private readonly string _path; private readonly bool _loop;
    private SoundEffectInstance _inst; private double _timeMs; private bool _playing; private float _vol = 1f;
    public VsSound(PlayScene ps, string path, bool loop) { _ps = ps; _path = path; _loop = loop; }
    private static string Norm(string p)
    {
        p = (p ?? "").Replace('\\', '/');
        if (p.Contains(':')) p = p.Substring(p.IndexOf(':') + 1);
        if (p.StartsWith("assets/")) p = p.Substring(7);
        if (!p.EndsWith(".ogg") && !p.EndsWith(".wav")) p += ".ogg";
        return p;
    }
    public void Play()
    {
        _inst ??= _ps.VsAudio.Create(Norm(_path));
        if (_inst == null) { Console.WriteLine($"[VSLICE-SCRIPT] sound not found: {_path}"); return; }
        _inst.IsLooped = _loop; _inst.Volume = Math.Clamp(_vol, 0f, 1f);
        _inst.Stop(); _inst.Play(); _timeMs = 0; _playing = true;
    }
    public void Stop() { _inst?.Stop(); _playing = false; }
    public void Tick(float dt) { if (_playing) _timeMs += dt * 1000.0; if (_inst != null && _playing && _inst.State == SoundState.Stopped && !_loop) _playing = false; }
    public override bool TryGet(string f, out object v)
    {
        switch (f)
        {
            case "play": v = Fn(a => { Play(); return this; }); return true;
            case "stop": case "destroy": v = Fn(a => { Stop(); return null; }); return true;
            case "pause": v = Fn(a => { _inst?.Pause(); _playing = false; return null; }); return true;
            case "resume": v = Fn(a => { _inst?.Resume(); _playing = _inst != null; return null; }); return true;
            case "volume": v = (double)_vol; return true;
            case "time": v = _timeMs; return true;
            case "playing": v = _playing; return true;
        }
        v = null; return false;
    }
    public override bool TrySet(string f, object v)
    {
        switch (f)
        {
            case "volume": _vol = (float)D(v); if (_inst != null) _inst.Volume = Math.Clamp(_vol, 0f, 1f); return true;
        }
        return false;
    }
}

/// <summary>funkin.util.Sequence: callbacks at times (s) after creation; `running = false` stops it.</summary>
public sealed class VsSequence : VObj
{
    private readonly List<(double t, object cb)> _items = new(); private double _t; private int _next; public bool Running = true;
    public VsSequence(object data)
    {
        if (data is IRIS::Array<object> arr)
            for (int i = 0; i < arr.length; i++)
            {
                var e = arr.__get(i);
                _items.Add((VObj.D(HxUtil.Field(e, "time")), HxUtil.Field(e, "callback")));
            }
        _items.Sort((a, b) => a.t.CompareTo(b.t));
    }
    public void Tick(float dt)
    {
        if (!Running) return;
        _t += dt;
        while (_next < _items.Count && _t >= _items[_next].t)
        {
            var cb = _items[_next++].cb;
            try { HxUtil.CallFn(cb); } catch (Exception ex) { Console.WriteLine($"[VSLICE-SCRIPT] Sequence: {ex.Message}"); }
        }
    }
    public override bool TryGet(string f, out object v) { if (f == "running") { v = Running; return true; } v = null; return false; }
    public override bool TrySet(string f, object v) { if (f == "running") { Running = B(v); return true; } return false; }
}

/// <summary>A script-made funkin.Conductor: mapTimeChanges(first bpm), update(timeMs), onBeatHit.add(fn).</summary>
public sealed class VsMiniConductor : VObj
{
    private double _bpm = 100; private int _beat = -1; private readonly List<object> _beatCbs = new();
    public override bool TryGet(string f, out object v)
    {
        switch (f)
        {
            case "mapTimeChanges": v = Fn(a =>
            {
                var tc = A(a, 0);
                object first = tc is IRIS::Array<object> arr && arr.length > 0 ? arr.__get(0) : null;
                var bpm = first == null ? null : (HxUtil.Field(first, "bpm") ?? HxUtil.Field(first, "b"));
                if (bpm != null) _bpm = D(bpm);
                return null;
            }); return true;
            case "update": v = Fn(a =>
            {
                double ms = D(A(a, 0));
                int beat = (int)Math.Floor(ms / (60000.0 / _bpm));
                if (beat > _beat) { _beat = beat; foreach (var cb in _beatCbs.ToList()) try { HxUtil.CallFn(cb); } catch (Exception ex) { Console.WriteLine($"[VSLICE-SCRIPT] Conductor beat: {ex.Message}"); } }
                return null;
            }); return true;
            case "onBeatHit": case "beatHit": { var sig = new VObj(); sig.Extra["add"] = new HxFn(a => { if (A(a, 0) != null) _beatCbs.Add(A(a, 0)); return null; }); sig.Extra["remove"] = new HxFn(a => { _beatCbs.Remove(A(a, 0)); return null; }); v = sig; return true; }
            case "bpm": v = _bpm; return true;
            case "currentBeat": v = _beat; return true;
        }
        v = null; return false;
    }
}

/// <summary>Save.instance.modOptions (V-Slice 0.8.4 Save.modOptions: Map&lt;String, Dynamic&gt;), kept in vslice_modoptions.json.</summary>
public sealed class VsModOptions : VObj
{
    private static Dictionary<string, object> _map;
    private static string FilePath => System.IO.Path.Combine(AssetManager.GameRoot ?? ".", "vslice_modoptions.json");
    private static Dictionary<string, object> Map
    {
        get
        {
            if (_map != null) return _map;
            _map = new Dictionary<string, object>(StringComparer.Ordinal);
            try
            {
                if (global::FNF_MonoGame.Engine.FsCI.FileExists(FilePath))
                    foreach (var p in JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(FilePath)).Properties())
                        _map[p.Name] = p.Value is JArray ja ? new IRIS::Array<object>(ja.Select(x => (object)(x.Type == JTokenType.Integer ? x.Value<int>() : (object)x.ToString())).ToArray()) : p.Value.Type == JTokenType.Integer ? p.Value.Value<int>() : p.Value.Type == JTokenType.Float ? p.Value.Value<double>() : p.Value.Type == JTokenType.Boolean ? p.Value.Value<bool>() : (object)p.Value.ToString();
            }
            catch (Exception ex) { Console.WriteLine($"[VSLICE-MODS] modOptions load: {ex.Message}"); }
            return _map;
        }
    }
    private static void SaveMap()
    {
        try
        {
            var o = new JObject();
            foreach (var kv in Map)
                o[kv.Key] = kv.Value is IRIS::Array<object> arr ? new JArray(Enumerable.Range(0, arr.length).Select(i => JToken.FromObject(arr.__get(i) ?? ""))) : kv.Value == null ? JValue.CreateNull() : JToken.FromObject(kv.Value);
            System.IO.File.WriteAllText(FilePath, o.ToString());
        }
        catch (Exception ex) { Console.WriteLine($"[VSLICE-MODS] modOptions save: {ex.Message}"); }
    }
    public static object GetValue(string k) => k != null && Map.TryGetValue(k, out var v) ? v : null; public static void SetValue(string k, object v) { if (k == null) return; Map[k] = v; SaveMap(); }
    public override bool TryGet(string f, out object v)
    {
        switch (f)
        {
            case "get": v = Fn(a => GetValue(S(A(a, 0)))); return true;
            case "exists": v = Fn(a => S(A(a, 0)) is string k && Map.ContainsKey(k)); return true;
            case "set": v = Fn(a => { if (S(A(a, 0)) is string k) { Map[k] = A(a, 1); SaveMap(); } return null; }); return true;
            case "remove": v = Fn(a => { if (S(A(a, 0)) is string k && Map.Remove(k)) SaveMap(); return null; }); return true;
            case "keys": v = Fn(a => new IRIS::Array<object>(Map.Keys.Cast<object>().ToArray())); return true;
        }
        if (Map.TryGetValue(f, out v)) return true;   // modOptions[key] (Map array access resolved as a field)
        v = null; return false;
    }
}

/// <summary>Save.instance facade (modOptions only).</summary>
public sealed class VsSaveFacade : VObj
{
    public override bool TryGet(string f, out object v)
    {
        switch (f)
        {
            case "modOptions": v = new VsModOptions(); return true;
            case "hasBeatenSong": case "hasBeatenLevel": v = Fn(a => false); return true;
        }
        v = null; return false;
    }
}

/// <summary>FlxCamera facade: camHUD visible/alpha hide the HUD; fade() draws the camCutscene overlay.</summary>
public sealed class VsCamera : VObj
{
    private readonly PlayScene _ps; private readonly bool _hud; private bool _visible = true; private double _alpha = 1;
    public VsCamera(PlayScene ps, bool hud) { _ps = ps; _hud = hud; } public bool IsCutscene { get; set; }
    private void Apply() { if (_hud) _ps.VsSetHudHidden(!_visible || _alpha <= 0.001); }
    public override bool TryGet(string f, out object v)
    {
        switch (f)
        {
            case "visible": v = _visible; return true;
            case "alpha": v = _alpha; return true;
            case "fade": v = Fn(a => { _ps.VsCameraFade(a); return null; }); return true;
            case "shake": case "flash": case "stopFX": v = Fn(a => null); return true;
        }
        v = null; return false;
    }
    public override bool TrySet(string f, object v)
    {
        switch (f)
        {
            case "visible": _visible = B(v); Apply(); return true;
            case "alpha": _alpha = D(v); Apply(); return true;
        }
        return false;
    }
}
