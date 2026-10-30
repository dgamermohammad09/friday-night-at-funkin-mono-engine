using System;
using Microsoft.Xna.Framework;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// PlayState.tweenCameraToPosition / tweenCameraToFollowPoint / cancelCameraFollowTween (V-Slice 0.8.1, PlayState.hx:3905-3948)
/// and the FocusCameraSongEvent x/y offsets + INSTANT / eased modes (FocusCameraSongEvent.hx:76-160).
/// The port's camera centre is _camWorldX/Y (= FlxG.camera.scroll + W/2), the follow point _camTargetWorldX/Y.
/// While a follow tween runs, the follow lerp is disabled (FlxG.camera.target = null) and the tween drives the camera.
/// </summary>
public partial class PlayScene
{
    private bool _camFollowTweening;
    private float _camTwFromX, _camTwFromY, _camTwToX, _camTwToY, _camTwDur, _camTwT;
    private Func<float, float> _camTwEase;

    /// <summary>PlayState.tweenCameraToPosition(x, y, duration, ease).</summary>
    public void TweenCameraToPosition(float x, float y, float duration, Func<float, float> ease = null)
    {
        _camTargetWorldX = x; // cameraFollowPoint.setPosition(x, y)
        _camTargetWorldY = y;
        TweenCameraToFollowPoint(duration, ease);
    }

    /// <summary>PlayState.tweenCameraToFollowPoint(duration, ease).</summary>
    public void TweenCameraToFollowPoint(float duration, Func<float, float> ease = null)
    {
        _camFollowTweening = false; // cancelCameraFollowTween()
        if (duration <= 0f)
        {
            // resetCamera(false, false): follow + focusOn(cameraFollowPoint) -> snap
            _camWorldX = _camTargetWorldX;
            _camWorldY = _camTargetWorldY;
            return;
        }
        _camTwFromX = _camWorldX; _camTwFromY = _camWorldY;
        _camTwToX = _camTargetWorldX; _camTwToY = _camTargetWorldY;
        _camTwDur = duration; _camTwT = 0f; _camTwEase = ease ?? (t => t);
        _camFollowTweening = true;
    }

    public void CancelCameraFollowTween() => _camFollowTweening = false;

    /// <summary>Steps the follow tween (called every frame from DispatchUpdate, also during cutscenes/countdown).</summary>
    private void UpdateCameraFollowTween(float dt)
    {
        if (!_camFollowTweening) return;
        _camTwT += dt;
        float t = Math.Min(1f, _camTwT / _camTwDur);
        float e = t >= 1f ? 1f : _camTwEase(t); // FlxTween: scale = 1 on completion
        _camWorldX = _camTwFromX + (_camTwToX - _camTwFromX) * e;
        _camWorldY = _camTwFromY + (_camTwToY - _camTwFromY) * e;
        if (t >= 1f)
        {
            _camFollowTweening = false;
            if (_w1Log) Console.WriteLine($"[CAMTW] done at song={SongPositionSeconds:F3} target=({_camTargetWorldX},{_camTargetWorldY}) tweenTo=({_camTwToX},{_camTwToY})");
            // onComplete: resetCamera(false, false) -> follow again and snap to the (current) follow point
            _camWorldX = _camTargetWorldX;
            _camWorldY = _camTargetWorldY;
        }
    }

    /// <summary>FocusCameraSongEvent: posX/posY offsets are added to the character focus point; then the ease mode.
    /// Called after the port's focus code has set _camTargetWorldX/Y to the character's camera focus point.</summary>
    private void ApplyFocusCameraEventExtras(object value, int focusChar)
    {
        if (!_hasJsonStage) return;
        var v = value as Newtonsoft.Json.Linq.JObject;
        if (v == null) return;
        // FocusCameraSongEvent: char -1 = absolute position (x, y); otherwise target += (x, y); no-op if the target char is missing
        if (focusChar == -1) { _camTargetWorldX = ReadFloat(v, "x", 0f); _camTargetWorldY = ReadFloat(v, "y", 0f); }
        else
        {
            if ((focusChar == 0 && _boyfriend == null) || (focusChar == 2 && _girlfriend == null) || (focusChar == 1 && _opponent == null)) return;
            _camTargetWorldX += ReadFloat(v, "x", 0f);
            _camTargetWorldY += ReadFloat(v, "y", 0f);
        }
        string ease = (string)v["ease"] ?? "CLASSIC";
        if (ease == "CLASSIC") { _camFollowTweening = false; return; } // resetCamera(false,false,false) + cancelCameraFollowTween(): follow lerp
        if (ease == "INSTANT") { TweenCameraToPosition(_camTargetWorldX, _camTargetWorldY, 0f); return; }
        string easeDir = (string)v["easeDir"] ?? "In";
        if (ease == "linear" || System.Text.RegularExpressions.Regex.IsMatch(ease, "(In|Out|InOut)$")) easeDir = "";
        float steps = ReadFloat(v, "duration", 4f);
        float durSeconds = (float)(_conductor.Crochet / 4.0) * steps; // Conductor.stepLengthMs * duration / 1000
        TweenCameraToPosition(_camTargetWorldX, _camTargetWorldY, durSeconds, FlxEase(ease + easeDir));
    }
}
