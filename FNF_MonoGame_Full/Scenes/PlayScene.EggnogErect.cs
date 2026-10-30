using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using FontStashSharp;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// songs/eggnog-erect.hxc (V-Slice 0.8.1): onSongEnd (every mode, first time) cancels the end and plays the in-game
/// "Santa dies" cutscene: stage prop 'santa' hidden, SantaDiesSprite (christmas/santa_speaks_assets, 'santa whole scene')
/// at (-1300, 100) zIndex santa-1, DadShootsSprite (christmas/parents_shoot_assets, 'parents whole scene') at (-602, -3.5)
/// zIndex santa-2, both with the santa AdjustColor shader; dad hidden; bf/dad danceEvery 0; camera/zoom tweens;
/// santa_emotion + subtitles, santa_shot_n_falls at 11.375 s, shake at 12.83 s, camHUD fade to black at 15 s,
/// endSong(true) at 16 s. CUTSCENE_ADVANCE: first press shows "Skip [ x ]", a press after 0.5 s skips (camCutscene fade
/// 0.5 s, endSong(true) after 0.5 s). Uses the W1 cutscene timers (_w1Clock / W1After).
/// </summary>
public partial class PlayScene
{
    private int _eggState;                   // 0 none, 1 running, 2 done (endSong(true))
    private AnimatedSprite _eggSanta, _eggParents;
    private SoundEffectInstance _eggSantaSnd, _eggShotSnd;
    private float _eggSantaSndT = -1f;       // santaSound.time (s) for the subtitles
    private bool _eggSkipped, _eggCanSkip, _eggSkipShown;
    private float _eggSkipAlpha;
    private float _eggHudFadeA, _eggHudFadeFrom, _eggHudFadeTo, _eggHudFadeDur, _eggHudFadeT = -1f;
    private float _eggCutFadeA, _eggCutFadeT = -1f;
    private float _eggShakeI, _eggShakeT;
    private readonly Random _eggRng = new();
    private static readonly bool _eggLog = Environment.GetEnvironmentVariable("FNF_EGGLOG") == "1";
    private float _eggLogNext;

    /// <summary>After endSong() the original's music has stopped: Conductor/song events no longer advance.</summary>
    private bool SongEndFrozen => _eggState == 1;

    private bool IsEggnogErect => string.Equals(_songName, "eggnog_erect", StringComparison.OrdinalIgnoreCase)
        || (string.Equals(_songName, "eggnog", StringComparison.OrdinalIgnoreCase) && string.Equals(_chart?.Stage, "mallXmasErect", StringComparison.OrdinalIgnoreCase));

    /// <summary>End-of-song hook. Returns true while the cutscene blocks the end of the song.</summary>
    private bool EggnogErectEndBlocks()
    {
        if (_eggState == 2) return false;
        if (_eggState == 0)
        {
            if (!IsEggnogErect) { _eggState = 2; return false; }
            _eggState = 1;
            EggStartCutscene();
        }
        return _eggState == 1;
    }

    private AnimatedSprite EggAtlas(string path, float x, float y, int z, string name)
    {
        SpriteSheet sheet = null;
        try { sheet = SpriteSheet.Load(Game, path, preRenderComposites: true, deferComposites: true); }
        catch (Exception ex) { Console.WriteLine($"[EGG] atlas {path}: {ex.Message}"); }
        if (sheet == null) return null;
        var spr = new AnimatedSprite { Sheet = sheet, UseTimelineAnchoring = true, Position = new Vector2(x, y), Scale = Vector2.One };
        W1AddProp(spr, z, name, 1f);
        return spr;
    }

    private static void EggPlayMain(AnimatedSprite s, params string[] names)
    {
        if (s?.Sheet == null) return;
        foreach (var n in names)
            if (s.Sheet.Animations.ContainsKey(n) || s.Sheet.CompositeAnimations.ContainsKey(n) || s.Sheet.RawCompositeData.ContainsKey(n))
            { s.PlayAnimation(n, force: true, loop: false); return; }
        var first = s.Sheet.Animations.Keys.Concat(s.Sheet.CompositeAnimations.Keys).FirstOrDefault();
        Console.WriteLine($"[EGG] main anim not found ({string.Join("|", names)}); anims: {string.Join(" | ", s.Sheet.Animations.Keys.Concat(s.Sheet.CompositeAnimations.Keys).Take(10))}");
        if (first != null) s.PlayAnimation(first, force: true, loop: false);
    }

    private void EggStartCutscene()
    {
        Console.WriteLine($"[EGG] eggnog-erect santa cutscene (song {SongPositionSeconds * 1000:F0} ms)");
        // endSong(): music/vocals volume 0
        Audio.PauseMusic(); Audio.StopVoices();
        _w1Clock = 0f; _w1Timers.Clear();

        int santaZ = 210;
        AdjustColorParams? santaShader = GetPropAdjust("santa");
        for (int i = 0; i < _animatedProps.Count; i++)
            if (string.Equals(_animatedProps[i].Name, "santa", StringComparison.OrdinalIgnoreCase))
            { var p = _animatedProps[i]; santaZ = p.ZIndex; p.Alpha = 0f; _animatedProps[i] = p; } // normalSanta.visible = false
        if (santaShader != null) { StageVisuals.PropShaders["eggSantaDead"] = santaShader.Value; StageVisuals.PropShaders["eggParentsShoot"] = santaShader.Value; }

        // SantaDiesSprite x -1300 y 100 zIndex santa-1; DadShootsSprite x -602 y -3.5 zIndex santa-2
        _eggSanta = EggAtlas("game/stages/christmas/santa_speaks_assets", -1300f, 100f, santaZ - 1, "eggSantaDead");
        _eggParents = EggAtlas("game/stages/christmas/parents_shoot_assets", -602f, -3.5f, santaZ - 2, "eggParentsShoot");
        EggPlayMain(_eggSanta, "santa_speaks_assets", "santa whole scene");
        EggPlayMain(_eggParents, "santa speaks assets", "parents whole scene");

        if (_opponent != null) { _opponent.Visible = false; _opponent.NoBeatDance = true; }
        if (_boyfriend != null) _boyfriend.NoBeatDance = true;

        TweenCameraToPosition(-1300f + 1200f, 100f + 300f, 2.8f, FlxEase("expoOut"));
        W1TweenZoom(0.73f, 2f, FlxEase("quadInOut"));

        _eggSantaSnd = Audio.CreateSoundInstance("sounds/week5/santa_emotion.ogg");
        if (_eggSantaSnd != null) { _eggSantaSnd.Volume = Math.Clamp(Audio.SfxVolume, 0f, 1f); _eggSantaSnd.Play(); _eggSantaSndT = 0f; }
        SubAssign("eggnog/santa-emotions", () => _eggSantaSndT < 0f ? 0.0 : _eggSantaSndT * 1000.0);
        _eggShotSnd = Audio.CreateSoundInstance("sounds/week5/santa_shot_n_falls.ogg");

        W1After(2.8f, () =>
        {
            TweenCameraToPosition(-1300f + 1050f, 100f + 300f, 9f, FlxEase("quartInOut"));
            W1TweenZoom(0.79f, 9f, FlxEase("quadInOut"));
        });
        W1After(11.375f, () =>
        {
            if (!_eggSkipped && _eggShotSnd != null) { _eggShotSnd.Volume = Math.Clamp(Audio.SfxVolume, 0f, 1f); _eggShotSnd.Play(); }
        });
        W1After(12.83f, () =>
        {
            _eggShakeI = 0.005f; _eggShakeT = 0.2f;   // camGame.shake(0.005, 0.2)
            TweenCameraToPosition(-1300f + 1060f, 100f + 380f, 5f, FlxEase("expoOut"));
        });
        W1After(15f, () => EggHudFade(0f, 1f, 1f));  // camHUD.fade(black, 1, fadeIn = false)
        W1After(16f, () =>
        {
            if (_eggSkipped) return;
            EggHudFade(1f, 0f, 0.5f);                 // camHUD.fade(black, 0.5, fadeIn = true)
            _eggState = 2;                            // endSong(true)
        });
    }

    private void EggHudFade(float from, float to, float dur)
    {
        _eggHudFadeFrom = from; _eggHudFadeTo = to; _eggHudFadeDur = dur; _eggHudFadeT = 0f; _eggHudFadeA = from;
    }

    /// <summary>Per frame (from the W1/Ssf update hook, also while the end of the song is blocked).</summary>
    private void EggUpdate(float dt)
    {
        SubUpdate();
        if (_eggState == 0) return;
        if (_eggSantaSndT >= 0f)
        {
            if (_eggSantaSnd != null && _eggSantaSnd.State == SoundState.Stopped && _eggSantaSndT > 0.2f) _eggSantaSndT = -1f;
            else _eggSantaSndT += dt;
        }
        if (_eggHudFadeT >= 0f)
        {
            _eggHudFadeT += dt;
            float t = Math.Min(1f, _eggHudFadeT / _eggHudFadeDur);
            _eggHudFadeA = _eggHudFadeFrom + (_eggHudFadeTo - _eggHudFadeFrom) * t;
            if (t >= 1f) _eggHudFadeT = -1f;
        }
        if (_eggCutFadeT >= 0f) { _eggCutFadeT += dt; _eggCutFadeA = Math.Min(1f, _eggCutFadeT / 0.5f); }
        if (_eggShakeT > 0f && !_bzActive)
        {
            _eggShakeT -= dt;
            if (_eggShakeT <= 0f) _bzShakeOffset = Vector2.Zero;
            else
            {
                float z = _defaultCamZoom * _cameraBopMultiplier;
                _bzShakeOffset = new Vector2(((float)_eggRng.NextDouble() * 2f - 1f) * _eggShakeI * 1280f * z,
                                             ((float)_eggRng.NextDouble() * 2f - 1f) * _eggShakeI * 720f * z);
            }
        }
        if (_eggSkipShown && _eggSkipAlpha < 1f && !_eggSkipped) _eggSkipAlpha = Math.Min(1f, _eggSkipAlpha + dt / 0.5f);
        if (_eggSkipped && _eggSkipAlpha > 0f) _eggSkipAlpha = Math.Max(0f, _eggSkipAlpha - dt / 0.5f);
        // CUTSCENE_ADVANCE
        if (_eggState == 1 && !_eggSkipped && Input.CutsceneAdvancePressed)
        {
            if (!_eggCanSkip)
            {
                if (!_eggSkipShown) { _eggSkipShown = true; W1After(0.5f, () => _eggCanSkip = true); }
            }
            else
            {
                _eggSkipped = true;
                _eggCutFadeT = 0f;                        // camCutscene.fade(black, 0.5)
                try { _eggSantaSnd?.Stop(); _eggShotSnd?.Stop(); } catch { }
                _eggSantaSndT = -1f;
                W1After(0.5f, () => _eggState = 2);       // endSong(true)
            }
        }
        if (_eggLog && _w1Clock >= _eggLogNext && _eggState == 1)
        {
            _eggLogNext = (float)Math.Floor(_w1Clock * 2f) / 2f + 0.5f;
            Console.WriteLine($"[EGG] t={_w1Clock * 1000:F0} cam={_camWorldX:F1},{_camWorldY:F1} zoom={_defaultCamZoom * _cameraBopMultiplier:F3} santa={_eggSanta?.CurrentAnimation}:{_eggSanta?.FrameIndex} parents={_eggParents?.FrameIndex} bf={_boyfriend?.CurrentAnimation}:{_boyfriend?.SpriteFrameIndex} gf={_girlfriend?.CurrentAnimation}:{_girlfriend?.SpriteFrameIndex} sub='{_subText}' hudFade={_eggHudFadeA:F2}");
        }
    }

    /// <summary>camHUD fade (over the stage + HUD), camCutscene skip text + fade, camSubtitles.</summary>
    private void EggDraw(SpriteBatch sb)
    {
        if (_eggState != 0)
        {
            sb.End();
            sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
            if (_eggHudFadeA > 0f) sb.Draw(Assets.Pixel, new Rectangle(0, 0, 1280, 720), new Color(Color.Black, Math.Clamp(_eggHudFadeA, 0f, 1f)));
            if (_eggSkipShown && _eggSkipAlpha > 0f)
            {
                string txt = "Skip [ " + (Input.LastDevice.ToString() == "Controller" ? "A" : "ENTER") + " ]";
                var font = W6Font(40);
                if (font != null)
                {
                    var a = Math.Clamp(_eggSkipAlpha, 0f, 1f);
                    for (int ox = -2; ox <= 2; ox += 2) for (int oy = -2; oy <= 2; oy += 2)
                        if (ox != 0 || oy != 0) sb.DrawString(font, txt, new Vector2(936 + ox, 618 + oy), new Color(Color.Black, a));
                    sb.DrawString(font, txt, new Vector2(936, 618), new Color(Color.White, a));
                }
            }
            if (_eggCutFadeA > 0f) sb.Draw(Assets.Pixel, new Rectangle(0, 0, 1280, 720), new Color(Color.Black, Math.Clamp(_eggCutFadeA, 0f, 1f)));
            sb.End();
            sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
        }
        SubDraw(sb);
    }
}
