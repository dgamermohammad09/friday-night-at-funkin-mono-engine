using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Non-video parts of the Weekend 1 song-script cutscenes (story mode only, as in the original):
///   songs/darnell.hxc  onCountdownStart -> [darnellCutscene video] -> introCutscene() (Darnell lights/kicks a can, Pico shoots it)
///   songs/2hot.hxc     onSongEnd -> startCutscene() -> [2hotCutscene video starts hidden] -> endCutscene() (camera/zoom
///                      tween, Pico intro1, Darnell pissed) -> after 6 s black bg + showVideo -> song ends when the video ends.
///   songs/blazin.hxc   ending is the blazinCutscene video only (nothing in-game to port).
/// VIDEO HOOKS: the port has no video player yet. <see cref="W1PlayVideo"/> is the single place where a video should be
/// played; it currently completes immediately (calls the onComplete callback), which is what the original does after the video.
/// Dev hook: FNF_W1_STORY=1 treats freeplay as story mode.
/// </summary>
public partial class PlayScene
{
    private bool _w1Active;            // blocks the countdown (_dialogueActive) while the darnell intro runs
    private bool _w1Story;
    private float _w1Clock = -1f;      // seconds since the current cutscene started
    private readonly List<(float T, Action A)> _w1Timers = new();
    private float _w1BlackAlpha;       // bgSprite (black, over the HUD / camCutscene)
    private bool _w1BlackVisible;
    private SoundEffectInstance _w1Music;
    private float _w1MusicClock = -1f; // cutsceneConductor (darnellCanCutscene 168 bpm / 2hot 86.5 bpm)
    private float _w1Bpm;
    private int _w1LastBeat;
    private bool _w1BeatDad;           // darnell intro: dad dances on cutscene beats too
    private AnimatedSprite _w1CutsceneCan, _w1NewCan;
    private int _w1DarkenMaxZ = int.MaxValue;

    // 2hot ending
    private int _w1EndState;           // 0 none, 1 running, 2 done
    private bool _w1EndVideo;          // 2hotCutscene video playing (hidden until 6 s)
    private int _bzEndState;           // blazin.hxc ending video: 0 none, 1 running, 2 done
    private float _w1EndClock;

    public double DevW1Elapsed => _ssfActive ? (_ssfDevEndAt >= 0f ? (_ssfEndT0 >= 0f ? _ssfClock - _ssfEndT0 : -1f) : _ssfClock) : (_w5Clock > 0f ? _w5Clock : _w1Clock);
    private static readonly bool _w1Log = Environment.GetEnvironmentVariable("FNF_W1LOG") == "1";
    private string _w1LastLog = ""; private float _w1LastLogT = -1f;
    private void W1DevLog()
    {
        if (!_w1Log || _w1Clock < 0f || _w1Clock > 14f) return;
        string a = $"bf={_boyfriend?.CurrentAnimation}:{_boyfriend?.SpriteFrameIndex} dad={_opponent?.CurrentAnimation}:{_opponent?.SpriteFrameIndex} gf={_girlfriend?.CurrentAnimation}:{_girlfriend?.SpriteFrameIndex}";
        if (a == _w1LastLog && _w1Clock - _w1LastLogT < 0.25f) return;
        _w1LastLog = a; _w1LastLogT = _w1Clock;
        Console.WriteLine($"W1T t={_w1Clock * 1000:F0} cam={_camWorldX:F1},{_camWorldY:F1} zoom={_defaultCamZoom * _cameraBopMultiplier:F3} hud={(W6HudVisible ? "true" : "false")} black={(_w1BlackVisible ? _w1BlackAlpha : 0f):F2} {a}");
    }

    private bool W1IsStory => _weekSongs != null || Environment.GetEnvironmentVariable("FNF_W1_STORY") == "1";

    private static string W1BaseSong(string songName)
    {
        string s = (songName ?? "").ToLowerInvariant();
        if (s == "darnell") return "darnell";      // story mode plays the default variation only
        if (s == "2hot") return "2hot";
        if (s == "blazin") return "blazin";
        return null;
    }

    private Vector2 W1FocusPoint(FNF_MonoGame.Gameplay.Character c)
    {
        // Character.cameraFocusPoint (same formula as the port's FocusCamera handler for JSON stages)
        var mid = c.GetMidpoint();
        float ox = c.CameraOffsets != null && c.CameraOffsets.Length >= 2 ? c.CameraOffsets[0] : 0f;
        float oy = c.CameraOffsets != null && c.CameraOffsets.Length >= 2 ? c.CameraOffsets[1] : 0f;
        return new Vector2(mid.X + ox, mid.Y + oy);
    }

    private void W1After(float t, Action a) => _w1Timers.Add((_w1Clock + t, a));

    /// <summary>Fallback when a video file is missing / cannot be played: continue as if it ended.</summary>
    private void W1PlayVideo(string videoId, Action onComplete)
    {
        Console.WriteLine($"[W1] video '{videoId}' could not be played -> continue");
        onComplete?.Invoke();
    }

    /// <summary>darnell.hxc onCountdownStart (called from LoadDialogue = the moment the countdown would start).</summary>
    private bool W1Init()
    {
        bool retry = s_w6RetryOf != null && string.Equals(s_w6RetryOf, _songName, StringComparison.OrdinalIgnoreCase);
        _w1Story = W1IsStory;
        if (W1BaseSong(_songName) != "darnell" || !_w1Story || retry) return false;
        _w1Active = true;
        _dialogueActive = true;     // event.cancel(): CANCEL THE COUNTDOWN
        Console.WriteLine("[W1] darnell intro cutscene");
        // !hasPlayedCutscene: tweenCameraZoom(1.3, 0, true); bgSprite black on camCutscene; startVideo()
        _zoomTweening = false; _defaultCamZoom = 1.3f;
        _w1BlackVisible = true; _w1BlackAlpha = 1f;
        _w6HudHidden = true;
        // VideoCutscene.finishVideo -> (0.5 s) onCutsceneFinish(STARTING) -> startCountdown -> onCountdownStart:
        // hasPlayedCutscene && !hasPlayedInGameCutscene -> camHUD.visible = false; introCutscene()
        Action afterVideo = () => { _w6HudHidden = true; W1DarnellIntro(); };
        if (!VideoPlay("darnellCutscene", VideoCutsceneType.Starting, onFinish: afterVideo))
            W1PlayVideo("darnellCutscene", afterVideo);
        s_w6RetryOf = null;
        return true;
    }

    /// <summary>darnell.hxc introCutscene().</summary>
    private void W1DarnellIntro()
    {
        _w1Clock = 0f; _w1Timers.Clear();
        var picoPos = W1FocusPoint(_boyfriend);
        var darnellPos = W1FocusPoint(_opponent);

        _w1Music = Audio.CreateSoundInstance("music/weekend1/darnellCanCutscene/darnellCanCutscene.ogg");
        if (_w1Music != null) { _w1Music.IsLooped = true; _w1Music.Volume = Math.Clamp(Audio.MusicVolume, 0f, 1f); }
        _w1Bpm = 168f; _w1LastBeat = 0; _w1BeatDad = true; _w1MusicClock = -1f;

        var pile = Find2hotSpraycanPile();
        var canSheet = SpriteSheet.Load(Game, "images/weekend1/wked1_cutscene_1_can");
        if (pile != null && canSheet != null)
        {
            // FunkinSprite.createSparrow(.., 'wked1_cutscene_1_can'): x = pile.x + 30, y = pile.y - 320, z = pile.z - 1, hidden
            _w1CutsceneCan = new AnimatedSprite { Sheet = canSheet, Position = new Vector2(pile.X + 30f, pile.Y - 320f), Scale = Vector2.One };
            W1AddProp(_w1CutsceneCan, pile.ZIndex - 1, "w1CutsceneCan", 0f);
        }
        if (pile != null)
        {
            // ScriptedFunkinSprite.init('SpraycanAtlasSprite'): x = pile.x - 430, y = pile.y - 840, z 300, hidden
            var sheet = _2hotCanSheet ??= SpriteSheet.Load(Game, "images/weekend1/spraycanAtlas", preRenderComposites: true);
            if (sheet != null)
            {
                _w1NewCan = new AnimatedSprite { Sheet = sheet, UseTimelineAnchoring = true, Position = new Vector2(pile.X - 430f, pile.Y - 840f), Scale = Vector2.One };
                W1AddProp(_w1NewCan, 300, "w1NewCan", 0f);
            }
        }

        _boyfriend.PlayAnimation("intro1", force: true);

        // camera sets up, pico does his animation showing him pissed
        W1After(0.1f, () =>
        {
            TweenCameraToPosition(picoPos.X + 250f, picoPos.Y, 0f);
            _zoomTweening = false; _defaultCamZoom = 1.3f; // tweenCameraZoom(1.3, 0, true)
        });
        W1After(0.7f, () =>
        {
            _w1Music?.Play(); _w1MusicClock = 0f;
            // FlxTween.tween(bgSprite, {alpha: 0}, 2, {startDelay: 0.3}) -> visible = false
            W1Tween(2f, 0.3f, null, v => _w1BlackAlpha = 1f - v, () => _w1BlackVisible = false);
        });
        const float cutsceneDelay = 2f;
        // move camera out to show everything
        W1After(cutsceneDelay, () =>
        {
            TweenCameraToPosition(darnellPos.X + 100f, darnellPos.Y, 2.5f, FlxEase("quadInOut"));
            W1TweenZoom(0.66f, 2.5f, FlxEase("quadInOut"));
        });
        // darnell lights up a can
        W1After(cutsceneDelay + 3f, () =>
        {
            _opponent.PlayAnimation("lightCan", force: true);
            Audio.PlaySound("sounds/weekend1/Darnell_Lighter", 1f);
        });
        // pico cocks his gun, camera shifts to his side to show this
        W1After(cutsceneDelay + 4f, () =>
        {
            _boyfriend.PlayAnimation("cock", force: true);
            TweenCameraToPosition(darnellPos.X + 180f, darnellPos.Y, 0.4f, FlxEase("backOut"));
            Audio.PlaySound("sounds/weekend1/Gun_Prep", 1f);
        });
        // darnell kicks the can up
        W1After(cutsceneDelay + 4.4f, () =>
        {
            _opponent.PlayAnimation("kickCan", force: true);
            Audio.PlaySound("sounds/weekend1/Kick_Can_UP", 1f);
            W1PlayProp(_w1CutsceneCan, "can kicked up", 1f);
        });
        // darnell knees the can forward
        W1After(cutsceneDelay + 4.9f, () =>
        {
            _opponent.PlayAnimation("kneeCan", force: true);
            Audio.PlaySound("sounds/weekend1/Kick_Can_FORWARD", 1f);
            W1PlayProp(_w1CutsceneCan, "can kick quick", null);
        });
        // pico shoots the can, it explodes
        W1After(cutsceneDelay + 5.1f, () =>
        {
            _boyfriend.PlayAnimationLocked("intro2");
            Audio.PlaySound($"sounds/weekend1/shot{_random.Next(1, 5)}", 1f);
            TweenCameraToPosition(darnellPos.X + 100f, darnellPos.Y, 1f, FlxEase("quadInOut"));
            if (_w1NewCan != null)
            {
                W1PlayProp(_w1NewCan, "Can Shot", 1f);
                var can = _w1NewCan;
                can.OnFinish = () => _animatedProps.RemoveAll(p => p.Sprite == can); // finishCanAnimation('Can Shot') -> kill
            }
            W1SetPropAlpha(_w1CutsceneCan, 0f);
            // new FlxTimer().start(1/24, darkenStageProps) - the port's darken already waits 1/24 s
            _w1DarkenMaxZ = 299; // darnell.hxc: props with zIndex > 299 are not darkened
            Start2hotDarken();
        });
        // darnell laughs
        W1After(cutsceneDelay + 5.9f, () =>
        {
            _opponent.PlayAnimation("laughCutscene", force: true);
            Audio.PlaySound("sounds/weekend1/cutscene/darnell_laugh", 0.6f);
        });
        // nene spits and laughs
        W1After(cutsceneDelay + 6.2f, () =>
        {
            _girlfriend?.PlayAnimation("laughCutscene", force: true);
            Audio.PlaySound("sounds/weekend1/cutscene/nene_laugh", 0.6f);
        });
        // camera returns to normal, cutscene flags set and countdown starts.
        W1After(cutsceneDelay + 8f, () =>
        {
            W1TweenZoom(0.77f, 2f, FlxEase("sineInOut"));
            TweenCameraToPosition(darnellPos.X + 180f, darnellPos.Y, 2f, FlxEase("sineInOut"));
            W1StopMusic();
            _w1Active = false;
            W6StartCountdown(true); // PlayState.startCountdown() (camHUD.visible = true)
        });
    }

    // ------------------------------------------------------------------ 2hot ending (2hot.hxc onSongEnd / startCutscene / endCutscene)

    /// <summary>Called when the song would end. Returns true while the ending cutscene still blocks the end of the song.</summary>
    private bool W1EndCutsceneBlocks(float dt)
    {
        if (_w1EndState == 2) return false;
        if (_w1EndState == 0)
        {
            if (W1BaseSong(_songName) != "2hot" || !W1IsStory) { _w1EndState = 2; return false; }
            _w1EndState = 1;
            Console.WriteLine("[W1] 2hot ending cutscene");
            // startCutscene(): camHUD hidden, bf/dad danceEvery = 0, cutsceneConductor 86.5 bpm (gf dances when finished)
            _w6HudHidden = true;
            _boyfriend.NoBeatDance = true;   // danceEvery = 0 (sing hold timer still returns to idle)
            _opponent.NoBeatDance = true;
            if (_girlfriend != null) _girlfriend.ScriptAnimOnly = true;
            _w1Bpm = 86.5f; _w1LastBeat = 0; _w1BeatDad = false; _w1MusicClock = 0f;
            _w1Clock = 0f; _w1Timers.Clear();
            // VideoCutscene.play('2hotCutscene', ENDING) -> onVideoStarted: endCutscene() (hideVideo first; shown at 6 s);
            // the song ends (endSong(true)) after the video's finish transition.
            _w1EndVideo = VideoPlay("2hotCutscene", VideoCutsceneType.Ending,
                onStarted: () => { VideoHide(); W1TwoHotEndCutscene(); },
                onFinish: () => _w1EndState = 2);
            if (!_w1EndVideo) W1TwoHotEndCutscene();
        }
        return _w1EndState == 1;
    }

    /// <summary>blazin.hxc onSongEnd (story mode, first time): black bg over the HUD, VideoCutscene.play('blazinCutscene', ENDING);
    /// the song ends after the video. Returns true while it blocks the end of the song.</summary>
    private bool BlazinEndCutsceneBlocks()
    {
        if (_bzEndState == 2) return false;
        if (_bzEndState == 0)
        {
            if (W1BaseSong(_songName) != "blazin" || !W1IsStory) { _bzEndState = 2; return false; }
            _bzEndState = 1;
            Console.WriteLine("[W1] blazin ending video");
            _w1BlackVisible = true; _w1BlackAlpha = 1f; // bgSprite on camHUD, zIndex 1000000
            if (!VideoPlay("blazinCutscene", VideoCutsceneType.Ending, onFinish: () => _bzEndState = 2)) _bzEndState = 2;
        }
        return _bzEndState == 1;
    }

    // ------------------------------------------------------------------ stress-pico.hxc onSongEnd -> startEndCutscene()
    private Microsoft.Xna.Framework.Audio.SoundEffectInstance _stressEndSnd;

    /// <summary>Stress (Pico Mix) ending: camHUD hidden, camera to tankman focus +320/-70 (2.8 s expoOut), zoom 0.65 direct
    /// (2 s expoOut), dad 'stressPicoEnding' + erect/endCutscene sound (+ end-cutscene-pico subtitles, offsets [0,42] bottom),
    /// 176/24 s bf 'laughEnd', 270/24 s camera to -370 (2 s quadInOut) + black bgSprite alpha 0->1 (2 s), 320/24 s endSong(true).
    /// Not in minimal mode; plays once per song end. Returns true while it blocks the end of the song.</summary>
    private bool StressPicoEndBlocks()
    {
        if (!_isStressPico || _stressEndCutsceneTimer >= 2f) return false;
        if (_stressEndCutsceneTimer < 0f)
        {
            _stressEndCutsceneTimer = 0f;
            Console.WriteLine($"[STRESS-END] start (song {SongPositionSeconds * 1000:F0} ms)");
            var tankmanPos = _opponent != null ? W1FocusPoint(_opponent) : new Vector2(_camWorldX, _camWorldY);
            _w1Clock = 0f; _w1Timers.Clear();
            _w6HudHidden = true;                                   // camHUD.visible = false
            _w1BlackVisible = true; _w1BlackAlpha = 0f;            // bgSprite (camCutscene, alpha 0)
            TweenCameraToPosition(tankmanPos.X + 320f, tankmanPos.Y - 70f, 2.8f, FlxEase("expoOut"));
            VsTweenCameraZoom(0.65f, 2f, true, FlxEase("expoOut"));
            if (_opponent != null) { _opponent.NoBeatDance = true; _opponent.PlayAnimation("stressPicoEnding", force: true); }
            if (_boyfriend != null) _boyfriend.NoBeatDance = true;
            _stressEndSnd = Audio.CreateSoundInstance("week7/sounds/erect/endCutscene.ogg");
            if (_stressEndSnd != null) { _stressEndSnd.Volume = Math.Clamp(Audio.SfxVolume, 0f, 1f); _stressEndSnd.Play(); }
            else Console.WriteLine("[STRESS-END] endCutscene sound missing");
            float sndStart = _w1Clock;
            _subForceY = 42f;                                      // subtitles.offsets = [0, 42]; alignment = 'bottom'
            SubAssign("stress/end-cutscene-pico", () => (_w1Clock - sndStart) * 1000.0);
            W1After(176f / 24f, () => _boyfriend?.PlayAnimation("laughEnd", force: true));
            W1After(270f / 24f, () =>
            {
                TweenCameraToPosition(tankmanPos.X + 320f, tankmanPos.Y - 370f, 2f, FlxEase("quadInOut"));
                W1Tween(2f, 0f, null, v => _w1BlackAlpha = v);    // FlxTween.tween(bgSprite, {alpha: 1}, 2)
            });
            W1After(320f / 24f, () =>
            {
                _stressEndCutsceneTimer = 2f;                     // endSong(true)
                try { _stressEndSnd?.Stop(); } catch { }
                Console.WriteLine("[STRESS-END] endSong");
            });
        }
        return _stressEndCutsceneTimer < 2f;
    }

    private void W1TwoHotEndCutscene()
    {
        // Nene plays 'danceLeft' so that she syncs with the video: hasDanced = false; dance(true)
        if (_girlfriend != null) { _girlfriend.PlayAnimation("danceLeft", force: true); _girlfriend.SetHasDanced(true); }
        W1After(1f, () =>
        {
            TweenCameraToPosition(1539f, 833.5f, 2f, FlxEase("quadInOut"));
            W1TweenZoom(0.69f, 2f, FlxEase("quadInOut"));
        });
        W1After(2f, () => _boyfriend.PlayAnimationLocked("intro1"));
        W1After(2.5f, () => _opponent.PlayAnimationLocked("pissed"));
        W1After(6f, () =>
        {
            // black bg over the HUD, VideoCutscene.showVideo(); the song ends when the video ends
            _w1BlackVisible = true; _w1BlackAlpha = 1f;
            if (_w1EndVideo) VideoShow();
            else W1PlayVideo("2hotCutscene", () => _w1EndState = 2);
        });
    }

    // ------------------------------------------------------------------ helpers / update / draw

    private sealed class W1Tw { public float Dur, Delay, T; public Func<float, float> Ease; public Action<float> Set; public Action Done; }
    private readonly List<W1Tw> _w1Tweens = new();

    private void W1Tween(float dur, float delay, Func<float, float> ease, Action<float> set, Action done = null)
        => _w1Tweens.Add(new W1Tw { Dur = dur, Delay = delay, Ease = ease ?? (t => t), Set = set, Done = done });

    /// <summary>PlayState.tweenCameraZoom(zoom, duration, direct = true, ease).</summary>
    private void W1TweenZoom(float zoom, float duration, Func<float, float> ease)
    {
        _zoomFrom = _defaultCamZoom; _zoomTo = zoom; _zoomDur = duration; _zoomT = 0f; _zoomEase = ease;
        _zoomTweening = duration > 0f;
        if (!_zoomTweening) _defaultCamZoom = zoom;
    }

    private void W1AddProp(AnimatedSprite sprite, int z, string name, float alpha)
    {
        var prop = new AnimatedStageProp { Sprite = sprite, ZIndex = z, Alpha = alpha, ScrollX = 1f, ScrollY = 1f, Name = name };
        _animatedProps.Add(prop);
        _animatedProps = _animatedProps.OrderBy(p => p.ZIndex).ToList();
    }

    private void W1SetPropAlpha(AnimatedSprite sprite, float alpha)
    {
        if (sprite == null) return;
        for (int i = 0; i < _animatedProps.Count; i++)
            if (_animatedProps[i].Sprite == sprite) { var p = _animatedProps[i]; p.Alpha = alpha; _animatedProps[i] = p; }
    }

    private void W1PlayProp(AnimatedSprite sprite, string anim, float? alpha)
    {
        if (sprite == null) return;
        sprite.PlayAnimation(anim, force: true, loop: false);
        if (alpha.HasValue) W1SetPropAlpha(sprite, alpha.Value);
    }

    private void W1StopMusic()
    {
        try { _w1Music?.Stop(); _w1Music?.Dispose(); } catch { }
        _w1Music = null;
        _w1MusicClock = -1f;
    }

    /// <summary>Per frame (always, also during the countdown and the song): FlxTimers, tweens, cutscene conductor.</summary>
    private void W1CutsceneUpdate(float dt)
    {
        if (_w1Clock < 0f) return;
        _w1Clock += dt;
        W1DevLog();
        for (int i = 0; i < _w1Timers.Count; i++)
        {
            if (_w1Clock >= _w1Timers[i].T)
            {
                var a = _w1Timers[i].A;
                _w1Timers.RemoveAt(i); i--;
                a();
            }
        }
        for (int i = 0; i < _w1Tweens.Count; i++)
        {
            var tw = _w1Tweens[i];
            float d = dt;
            if (tw.Delay > 0f) { tw.Delay -= d; if (tw.Delay > 0f) continue; d = -tw.Delay; tw.Delay = 0f; }
            tw.T += d;
            float t = Math.Min(1f, tw.T / tw.Dur);
            tw.Set?.Invoke(t >= 1f ? 1f : tw.Ease(t)); // FlxTween: scale = 1 on completion
            if (t >= 1f) { _w1Tweens.RemoveAt(i); i--; tw.Done?.Invoke(); }
        }
        // cutsceneConductor.onBeatHit
        if (_w1MusicClock >= 0f && _w1Bpm > 0f)
        {
            _w1MusicClock += dt;
            int beat = (int)Math.Floor(_w1MusicClock / (60f / _w1Bpm));
            if (beat != _w1LastBeat)
            {
                _w1LastBeat = beat;
                W1OnCutsceneBeat(beat);
            }
        }
    }

    private void W1OnCutsceneBeat(int beat)
    {
        int b = (100000 + beat) * 840; // forced dance (dance(true)); unique + divisible by any dance interval <= 8
        if (_w1BeatDad && _opponent != null && (_opponent._sprite?.Finished ?? true) && _opponent.CurrentAnimation != "lightCan")
            _opponent.Dance(b);
        if (_girlfriend != null && (_girlfriend._sprite?.Finished ?? true))
        {
            bool s = _girlfriend.ScriptAnimOnly;
            _girlfriend.ScriptAnimOnly = false;
            _girlfriend.Dance(b);
            _girlfriend.ScriptAnimOnly = s;
        }
    }

    /// <summary>While the darnell intro blocks the countdown: characters + stage props keep animating.</summary>
    private void W1UpdateBlocked(float dt)
    {
        _boyfriend?.Update(dt);
        _opponent?.Update(dt);
        _girlfriend?.Update(dt);
        for (int i = 0; i < _animatedProps.Count; i++) _animatedProps[i].Sprite?.Update(dt);
        UpdateAbotFallbackAnchors();
    }

    /// <summary>bgSprite (black) on camCutscene / camHUD.</summary>
    private void W1DrawCutscene(SpriteBatch sb)
    {
        if (!_w1BlackVisible || _w1BlackAlpha <= 0f) return;
        sb.End();
        sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);
        sb.Draw(Assets.Pixel, new Rectangle(0, 0, 1280, 720), new Color(Color.Black, Math.Clamp(_w1BlackAlpha, 0f, 1f)));
        sb.End();
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
    }
}
