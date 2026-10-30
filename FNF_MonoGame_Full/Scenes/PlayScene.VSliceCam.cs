extern alias IRIS;
using System;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.PureHScript;

namespace FNF_MonoGame.Scenes;

/// <summary>V-Slice script facade: PlayState camera API used by mod scripts (PlayState.hx 0.8.1).</summary>
public partial class PlayScene
{
    /// <summary>PlayState.tweenCameraZoom(zoom = 1, duration = 0, direct = false, ?ease) (PlayState.hx:3953).
    /// currentCameraZoom = the port's _defaultCamZoom; FlxCamera.defaultZoom = 1.</summary>
    public void VsTweenCameraZoom(float zoom, float duration, bool direct, Func<float, float> ease)
    {
        if (_stageZoom < 0f) _stageZoom = _defaultCamZoom;
        _zoomTweening = false;                                     // cancelCameraZoomTween()
        float target = zoom * (direct ? 1f : _stageZoom);
        if (duration <= 0f) { _defaultCamZoom = target; return; }
        _zoomFrom = _defaultCamZoom; _zoomTo = target; _zoomDur = duration; _zoomT = 0f;
        _zoomEase = ease ?? (t => t);
        _zoomTweening = true;
    }

    // Script-assigned AdjustColorShader instances (prop.shader / character.shader), read live by the draw code.
    private readonly System.Collections.Generic.Dictionary<string, VsAdjustShader> _vsPropShaders = new(StringComparer.Ordinal);
    private readonly VsAdjustShader[] _vsCharShaders = new VsAdjustShader[3];
    public static void VsSetPropShader(string prop, VsAdjustShader sh)
    {
        var ps = VsCurrent; if (ps == null || prop == null) return;
        if (sh == null) ps._vsPropShaders.Remove(prop); else ps._vsPropShaders[prop] = sh;
    }
    public void VsSetCharShader(string type, VsAdjustShader sh)
    {
        int i = type switch { "BF" => 0, "DAD" => 1, "GF" => 2, _ => -1 };
        if (i >= 0) _vsCharShaders[i] = sh;
    }
    private AdjustColorParams? VsPropAdjust(string prop) => prop != null && _vsPropShaders.TryGetValue(prop, out var sh) ? sh.Params : null;
    private AdjustColorParams? VsCharAdjust(int charType) => charType >= 0 && charType < 3 && _vsCharShaders[charType] != null ? _vsCharShaders[charType].Params : null;
    public string VsAnimPropName(int i) => i >= 0 && i < _animatedProps.Count ? _animatedProps[i].Name : null;

    public float VsCamFollowX { get => _camTargetWorldX; set => _camTargetWorldX = value; }
    public float VsCamFollowY { get => _camTargetWorldY; set => _camTargetWorldY = value; }

    /// <summary>Script ease argument (FlxEase.x = Func&lt;double,double&gt; or a script function) as a float ease.</summary>
    public static Func<float, float> VsEase(object e) => e switch
    {
        Func<double, double> f => t => (float)f(t),
        Func<float, float> g => g,
        IRIS::haxe.lang.Function hf => t => (float)VObj.D(IRIS::Reflect.callMethod(null, hf, new IRIS::Array<object>(new object[] { (double)t }))),
        _ => null,
    };
}

/// <summary>PlayState.cameraFollowPoint (FlxObject): x, y, setPosition.</summary>
public sealed class VsCamFollowPoint : VObj
{
    private readonly PlayScene _ps; public VsCamFollowPoint(PlayScene ps) { _ps = ps; }
    public override bool TryGet(string f, out object v)
    {
        switch (f)
        {
            case "x": v = (double)_ps.VsCamFollowX; return true;
            case "y": v = (double)_ps.VsCamFollowY; return true;
            case "setPosition": v = Fn(a => { _ps.VsCamFollowX = (float)D(A(a, 0)); _ps.VsCamFollowY = (float)D(A(a, 1)); return this; }); return true;
        }
        v = null; return false;
    }
    public override bool TrySet(string f, object v)
    {
        switch (f)
        {
            case "x": _ps.VsCamFollowX = (float)D(v); return true;
            case "y": _ps.VsCamFollowY = (float)D(v); return true;
        }
        return false;
    }
}
