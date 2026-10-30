using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// 2hot.hxc darkenStageProps(): 1/24 s after a can is shot every stage member except the characters
/// (bf/dad/gf), the spawned cans and Pico's cock-gun ghost is set to 0xFF111111, 1/24 s later to 0xFF222222,
/// then FlxTween.color(prop, 1.4, 0xFF222222, 0xFFFFFFFF).
/// </summary>
public partial class PlayScene
{
    private float _2hotDarkenT = -1f;   // seconds since the can was shot

    private void Start2hotDarken() => _2hotDarkenT = 0f;

    // darnell.hxc onNoteIncoming: the can sounds are scheduled to start BEFORE the note's strum time
    // (lightcan -65 ms, kickcan -50 ms, kneecan -22 ms) and play whether or not the note is "hit".
    private List<(double Time, string Sound)> _2hotCanSounds;
    private int _2hotCanSoundIdx;
    private void Update2hotCanSounds()
    {
        if (_2hotCanSounds == null)
        {
            _2hotCanSounds = new List<(double, string)>();
            if (_chart?.Notes != null)
                foreach (var n in _chart.Notes)
                {
                    string k = n.Kind?.ToLowerInvariant();
                    if (k == "weekend-1-lightcan") _2hotCanSounds.Add((n.Time - 0.065, "sounds/weekend1/Darnell_Lighter"));
                    else if (k == "weekend-1-kickcan") _2hotCanSounds.Add((n.Time - 0.050, "sounds/weekend1/Kick_Can_UP"));
                    else if (k == "weekend-1-kneecan") _2hotCanSounds.Add((n.Time - 0.022, "sounds/weekend1/Kick_Can_FORWARD"));
                }
            _2hotCanSounds.Sort((a, b) => a.Time.CompareTo(b.Time));
            double now0 = SongPositionSeconds;
            while (_2hotCanSoundIdx < _2hotCanSounds.Count && _2hotCanSounds[_2hotCanSoundIdx].Time < now0 - 0.5) _2hotCanSoundIdx++;
        }
        double now = SongPositionSeconds;
        while (_2hotCanSoundIdx < _2hotCanSounds.Count && now >= _2hotCanSounds[_2hotCanSoundIdx].Time)
        {
            Audio.PlaySound(_2hotCanSounds[_2hotCanSoundIdx].Sound);
            _2hotCanSoundIdx++;
        }
    }

    // Dev hooks for comparing with the original: FNF_SKIP_FIREGUN_AT=<song s> makes botplay leave firegun notes
    // unhit (they are missed when they leave the window); FNF_2HOTLOG="t0,t1" traces anims/frames/cans per frame.
    private static readonly float? _devSkipFiregunAt = ParseEnvF("FNF_SKIP_FIREGUN_AT");
    private static readonly string _dev2hotLog = Environment.GetEnvironmentVariable("FNF_2HOTLOG");
    private bool Dev2hotSkipBotNote(FNF_MonoGame.Engine.PsychEngine.Note note)
        => _devSkipFiregunAt is float t && SongPositionSeconds >= t
           && (note.Kind ?? "").StartsWith("weekend-1-firegun", StringComparison.OrdinalIgnoreCase);
    private void Dev2hotTrace()
    {
        if (string.IsNullOrEmpty(_dev2hotLog)) return;
        var parts = _dev2hotLog.Split(',');
        double t0 = double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), t1 = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
        double ms = SongPositionSeconds * 1000.0;
        if (ms < t0 || ms > t1) return;
        var sb = new System.Text.StringBuilder();
        sb.Append($"FR t={ms:F1} bf={_boyfriend?.CurrentAnimation}:{_boyfriend?.SpriteFrameIndex} dad={_opponent?.CurrentAnimation}:{_opponent?.SpriteFrameIndex} hp={_health:F3} cans={_2hotSpawnedCans.Count}");
        for (int i = 0; i < _2hotSpawnedCans.Count; i++)
        {
            if (_2hotCanStates[i] == CAN_STATE_DEAD) continue;
            var sp = _2hotSpawnedCans[i].Sprite;
            sb.Append($" [{sp?.CurrentAnimation}:{sp?.FrameIndex} st={_2hotCanStates[i]} x={sp?.Position.X} y={sp?.Position.Y}]");
        }
        Console.WriteLine(sb.ToString());
    }

    // pico-playable.hxc onAnimationFinished('shootMISS') with health > 0:
    // FlxFlicker.flicker(pico, 1, 1/30, endVisibility true) then FlxFlicker.flicker(pico, 0.5, 1/60).
    private bool _2hotPreloaded;
    private float _2hotFlickerT = -1f;
    private bool _2hotFlickerArmed = true;
    private void Update2hotFlicker(float dt)
    {
        if (_boyfriend == null) return;
        if (_boyfriend.CurrentAnimation != "shootMISS") _2hotFlickerArmed = true;
        else if (_2hotFlickerArmed && (_boyfriend._sprite?.Finished ?? false) && _health > 0f && !_gameOver)
        { _2hotFlickerArmed = false; _2hotFlickerT = 0f; }
        if (_2hotFlickerT < 0f) return;
        _2hotFlickerT += dt;
        bool visible;
        if (_2hotFlickerT < 1f) visible = ((int)(_2hotFlickerT / (1f / 30f))) % 2 == 0;
        else if (_2hotFlickerT < 1.5f) visible = ((int)((_2hotFlickerT - 1f) / (1f / 60f))) % 2 == 0;
        else { visible = true; _2hotFlickerT = -1f; }
        _boyfriend.Alpha = visible ? 1f : 0f;
    }

    private void Update2hotDarken(float dt)
    {
        Dev2hotTrace();
        Update2hotFlicker(dt);
        // 2hot.hxc / phillyStreets: spraycan + explosion atlases are cached when the song is created, so the first
        // can spawn does not stall (the port used to load them lazily: ~500 ms hitch at the first kickCan).
        if (!_2hotPreloaded && _chart?.Notes != null)
        {
            _2hotPreloaded = true;
            if (_chart.Notes.Exists(n => string.Equals(n.Kind, "weekend-1-kickcan", StringComparison.OrdinalIgnoreCase)))
            {
                _2hotCanSheet ??= Engine.SpriteSheet.Load(Game, "images/weekend1/spraycanAtlas", preRenderComposites: true);
                _2hotExplosionSheet ??= Engine.SpriteSheet.Load(Game, "images/weekend1/spraypaintExplosionEZ");
            }
        }
        if (_conductor != null && !_countdownActive) Update2hotCanSounds();
        if (_2hotDarkenT < 0f) return;
        _2hotDarkenT += dt;
        const float step = 1f / 24f;
        Color? c;
        if (_2hotDarkenT < step) return;                                 // FlxTimer(1/24) before darkenStageProps()
        else if (_2hotDarkenT < 2f * step) c = new Color(0x11, 0x11, 0x11); // color = 0xFF111111
        else
        {
            float t = Math.Clamp((_2hotDarkenT - 2f * step) / 1.4f, 0f, 1f); // FlxTween.color 0x222222 -> white, 1.4 s, linear
            byte v = (byte)MathF.Round(0x22 + (255 - 0x22) * t);
            c = new Color(v, v, v);
            if (t >= 1f) { c = null; _2hotDarkenT = -1f; }
        }
        for (int i = 0; i < _staticProps.Count; i++)
            if (_staticProps[i].ZIndex <= _w1DarkenMaxZ) _staticProps[i].OverlayColor = c; // darnell.hxc: zIndex > 299 excluded
        for (int i = 0; i < _animatedProps.Count; i++)
        {
            var p = _animatedProps[i];
            if (p.Name != null && p.Name.StartsWith("2hotCan", StringComparison.Ordinal)) continue; // cans excluded
            if (p.ZIndex > _w1DarkenMaxZ) continue;
            p.OverlayColor = c;
            _animatedProps[i] = p;
        }
    }
}
