using System;
using Microsoft.Xna.Framework;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Original camera zoom/bop song events (funkin/play/event/ZoomCameraSongEvent.hx,
/// SetCameraBopSongEvent.hx, PlayState.tweenCameraZoom / beatHit camera bop).
/// </summary>
public partial class PlayScene
{
    private float _stageZoom = -1f;          // PlayState.stageZoom (stage JSON cameraZoom)
    private float _zoomFrom, _zoomTo, _zoomDur, _zoomT;
    private Func<float, float> _zoomEase;
    private bool _zoomTweening;

    // SetCameraBop: cameraBopIntensity / hudCameraZoomIntensity / cameraZoomRate / cameraZoomRateOffset
    private float _camBopIntensity = 1.015f;         // Constants.DEFAULT_BOP_INTENSITY
    private float _hudBopIntensity = 0.015f * 2f;
    private int _camZoomRate = 4;                    // Constants.DEFAULT_ZOOM_RATE
    private int _camZoomRateOffset = 0;              // Constants.DEFAULT_ZOOM_OFFSET

    private static float ReadFloat(Newtonsoft.Json.Linq.JToken v, string key, float def)
        => v?[key] != null && v[key].Type != Newtonsoft.Json.Linq.JTokenType.Null ? (float)v[key] : def;

    /// <summary>ZoomCameraSongEvent.handleEvent</summary>
    private void HandleZoomCameraEvent(object value)
    {
        if (_stageZoom < 0f) _stageZoom = _defaultCamZoom;
        var v = value as Newtonsoft.Json.Linq.JToken;
        float zoom = ReadFloat(v, "zoom", 1f);                         // DEFAULT_ZOOM
        float duration = ReadFloat(v, "duration", 4f);                 // DEFAULT_DURATION (steps)
        string mode = (string)v?["mode"] ?? "direct";                  // DEFAULT_MODE
        string ease = (string)v?["ease"] ?? "linear";                  // SongEvent.DEFAULT_EASE
        string easeDir = (string)v?["easeDir"] ?? "In";               // SongEvent.DEFAULT_EASE_DIR
        if (ease == "linear" || System.Text.RegularExpressions.Regex.IsMatch(ease, "(In|Out|InOut)$")) easeDir = "";

        float target = zoom * (mode == "direct" ? 1f : _stageZoom);   // tweenCameraZoom: direct ? defaultZoom : stageZoom
        if (ease == "INSTANT")
        {
            _zoomTweening = false;
            _defaultCamZoom = target;
            return;
        }
        float stepSec = (float)(_conductor.Crochet / 4.0);             // Conductor.stepLengthMs / 1000
        _zoomFrom = _defaultCamZoom; _zoomTo = target; _zoomDur = stepSec * duration; _zoomT = 0f;
        _zoomEase = FlxEase(ease + easeDir);
        _zoomTweening = _zoomDur > 0f;
        if (!_zoomTweening) _defaultCamZoom = target;
    }

    /// <summary>SetCameraBopSongEvent.handleEvent</summary>
    private void HandleSetCameraBopEvent(object value)
    {
        var v = value as Newtonsoft.Json.Linq.JToken;
        int rate = v?["rate"] != null ? (int)v["rate"] : 4;
        int offset = v?["offset"] != null ? (int)v["offset"] : 0;
        float intensity = ReadFloat(v, "intensity", 1f);
        _camBopIntensity = (1.015f - 1f) * intensity + 1f;
        _hudBopIntensity = (1.015f - 1f) * intensity * 2f;
        _camZoomRate = rate;
        _camZoomRateOffset = offset;
    }

    /// <summary>PlayState.beatHit camera bop: only while zoom &lt; 135% of default.</summary>
    private bool ShouldCameraBop(int beat)
        => _camZoomRate > 0 && _defaultCamZoom * _cameraBopMultiplier < 1.35f && (beat + _camZoomRateOffset) % _camZoomRate == 0;

    private void UpdateCameraZoomTween(float dt)
    {
        if (!_zoomTweening) return;
        _zoomT += dt;
        float t = Math.Min(1f, _zoomT / _zoomDur);
        _defaultCamZoom = _zoomFrom + (_zoomTo - _zoomFrom) * (t >= 1f ? 1f : _zoomEase(t)); // FlxTween: scale = 1 on completion (expoOut(1) != 1)
        if (t >= 1f) _zoomTweening = false;
    }

    /// <summary>flixel.tweens.FlxEase functions used by chart events.</summary>
    private static Func<float, float> FlxEase(string name)
    {
        const float PI2 = MathF.PI / 2f;
        static float BounceOut(float t)
        {
            if (t < 1 / 2.75f) return 7.5625f * t * t;
            if (t < 2 / 2.75f) { t -= 1.5f / 2.75f; return 7.5625f * t * t + 0.75f; }
            if (t < 2.5f / 2.75f) { t -= 2.25f / 2.75f; return 7.5625f * t * t + 0.9375f; }
            t -= 2.625f / 2.75f; return 7.5625f * t * t + 0.984375f;
        }
        return name switch
        {
            "quadIn" => t => t * t,
            "quadOut" => t => -t * (t - 2),
            "quadInOut" => t => t <= .5f ? t * t * 2 : 1 - (--t) * t * 2,
            "cubeIn" => t => t * t * t,
            "cubeOut" => t => 1 + (--t) * t * t,
            "cubeInOut" => t => t <= .5f ? t * t * t * 4 : 1 + (--t) * t * t * 4,
            "quartIn" => t => t * t * t * t,
            "quartOut" => t => 1 - (t -= 1) * t * t * t,
            "quartInOut" => t => t <= .5f ? t * t * t * t * 8 : (1 - (t = t * 2 - 2) * t * t * t) / 2 + .5f,
            "quintIn" => t => t * t * t * t * t,
            "quintOut" => t => (t = t - 1) * t * t * t * t + 1,
            "quintInOut" => t => ((t *= 2) < 1) ? (t * t * t * t * t) / 2 : ((t -= 2) * t * t * t * t + 2) / 2,
            "sineIn" => t => -MathF.Cos(PI2 * t) + 1,
            "sineOut" => t => MathF.Sin(PI2 * t),
            "sineInOut" => t => -MathF.Cos(MathF.PI * t) / 2 + .5f,
            "expoIn" => t => MathF.Pow(2, 10 * (t - 1)),
            "expoOut" => t => -MathF.Pow(2, -10 * t) + 1,
            "expoInOut" => t => t < .5f ? MathF.Pow(2, 10 * (t * 2 - 1)) / 2 : (-MathF.Pow(2, -10 * (t * 2 - 1)) + 2) / 2,
            "circIn" => t => -(MathF.Sqrt(1 - t * t) - 1),
            "circOut" => t => MathF.Sqrt(1 - (t - 1) * (t - 1)),
            "circInOut" => t => t <= .5f ? (MathF.Sqrt(1 - t * t * 4) - 1) / -2 : (MathF.Sqrt(1 - (t * 2 - 2) * (t * 2 - 2)) + 1) / 2,
            "backIn" => t => t * t * (2.70158f * t - 1.70158f),
            "backOut" => t => 1 - (--t) * t * (-2.70158f * t - 1.70158f),
            "bounceIn" => t => 1 - BounceOut(1 - t),
            "bounceOut" => BounceOut,
            "smoothStepIn" => t => 2 * (t / 2) * (t / 2) * (3 - 2 * (t / 2)),
            "smoothStepOut" => t => 2 * ((t / 2 + .5f) * (t / 2 + .5f) * (3 - 2 * (t / 2 + .5f))) - 1,
            "smoothStepInOut" => t => t * t * (t * -2 + 3),
            _ => t => t,   // linear / unknown
        };
    }
}
