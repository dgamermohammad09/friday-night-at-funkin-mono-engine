using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using FontStashSharp;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// stages/phillyTrainErect.hxc doppleGangerCutscene() (Pico Mix songs: bf = pico-playable on phillyTrainErect, first
/// time only, every mode) + props PicoDopplegangerSprite.hxc / PicoBloodPool.hxc:
///  playerShoots = random.bool(50), explode = random.bool(8); two 'philly/erect/pico_doppleganger' atlases (Player at
///  bf.x-537,bf.y-209 z bf+1; Opponent at dad.x-502,dad.y-209 z player-/+1) playing shoot/explode/cigarette + suffix,
///  blood pool (explode, 11.2 s), cigarette spit (no explode, 11.5 s), camera follow point mid -> cigarette (4 s) ->
///  shooter (6.3 s) -> cigarette (8.75 s), cutscene music (cutscene / cutscene2, 150 bpm conductor: gf dance(true) when
///  finished), gf 'drop70' (explode), 13 s: countdown / death ending (explode + opponent shoots: endSong after 2 s).
///  CUTSCENE_ADVANCE skip until 8.75 s. Uses the W1 cutscene timers (_w1Clock / W1After) + W1UpdateBlocked.
/// Dev hook: FNF_DOPPEL="shoot,explode" (0/1 each) forces the random outcome.
/// </summary>
public partial class PlayScene
{
    private bool _dpActive;                 // countdown blocked
    private bool _dpExplode, _dpPlayerShoots;
    private AnimatedSprite _dpPlayer, _dpOpponent, _dpBlood, _dpCig;
    private bool _dpPlayerVis, _dpOpponentVis, _dpBloodVis, _dpCigVis;
    private SoundEffectInstance _dpMusic;
    private readonly List<SoundEffectInstance> _dpSounds = new();
    private bool _dpSkipped, _dpCanSkip, _dpSkipShown, _dpSkipHidden;
    private float _dpSkipAlpha;
    private float _dpCutFadeA, _dpCutFadeFrom, _dpCutFadeTo, _dpCutFadeT = -1f, _dpCutFadeDur;
    private bool _dpExtendBlood; private float _dpBloodScale = 1f;
    private bool _dpDeathEnd;               // explode + the opponent shot: endSong(true)
    private bool _dpMuteOpponent;           // explode + player shoots: opponent notes cancelled, opponentVolume 0
    private static readonly bool _dpLog = Environment.GetEnvironmentVariable("FNF_DPLOG") == "1";
    private float _dpLogNext;

    private sealed class DpProp : StaticStageProp { }

    /// <summary>LoadDialogue hook (onCountdownStart). Returns true when the countdown is cancelled.</summary>
    private bool DoppelInit()
    {
        if (!string.Equals(_chart?.Stage, "phillyTrainErect", StringComparison.OrdinalIgnoreCase)) return false;
        if (GoPlayerId != "pico-playable" || _boyfriend == null || _opponent == null) return false;
        bool retry = s_w6RetryOf != null && string.Equals(s_w6RetryOf, _songName, StringComparison.OrdinalIgnoreCase);
        if (retry) return false;   // hasPlayedInGameCutscene survives a restart
        _dpActive = true;
        _dialogueActive = true;    // event.cancel()
        _w6HudHidden = true;       // camHUD.visible = false
        DoppelCutscene();
        return true;
    }

    private AnimatedSprite DpAtlas(string path, float x, float y, int z, string name, Func<bool> vis, bool scaleFn = false)
    {
        SpriteSheet sheet = null;
        try { sheet = SpriteSheet.Load(Game, path, preRenderComposites: true, deferComposites: true); }
        catch (Exception ex) { Console.WriteLine($"[DOPPEL] atlas {path}: {ex.Message}"); }
        if (sheet == null) return null;
        var spr = new AnimatedSprite { Sheet = sheet, UseTimelineAnchoring = true, Position = new Vector2(x, y), Scale = Vector2.One };
        if (_dpLog) Console.WriteLine($"[DOPPEL] {name} bounds={sheet.WholeMainTimelineBounds} anims={string.Join("|", sheet.Animations.Keys.Take(10))}");
        var prop = new SsfProp { Name = name, ZIndex = z, Visible = true, Alpha = 1f, ScaleX = 1f, ScaleY = 1f, ScrollX = 1f, ScrollY = 1f };
        prop.DrawFn = sb =>
        {
            if (!vis() || spr.CurrentAnimation == null) return;
            var keep = spr.Position; var keepScale = spr.Scale;
            var pos = new Vector2(x, y);
            if (scaleFn && _dpBloodScale != 1f)
            {
                // FlxSprite scale about origin (= centre of the frame size = timeline bounds)
                var b = sheet.WholeMainTimelineBounds;
                float w = MathF.Floor(b?.W ?? 0f), h = MathF.Floor(b?.H ?? 0f); // frameWidth = Std.int(bounds)
                pos += new Vector2(w / 2f, h / 2f) * (1f - _dpBloodScale);
                spr.Scale = new Vector2(_dpBloodScale);
            }
            spr.Position = WorldToScreen(pos.X, pos.Y, 1f, 1f);
            bool adj = BeginAdjust(sb, GetPropAdjust("train"));   // colorShader (AdjustColor -26,-16,-5,0)
            spr.Draw(sb);
            EndAdjust(sb, adj);
            spr.Position = keep; spr.Scale = keepScale;
        };
        _staticProps.Add(prop);
        return spr;
    }

    private SoundEffectInstance DpSound(string path)
    {
        var s = Audio.CreateSoundInstance(path);
        if (s == null) return null;
        s.Volume = Math.Clamp(Audio.SfxVolume, 0f, 1f);
        s.Play();
        _dpSounds.Add(s);
        return s;
    }

    private void DoppelCutscene()
    {
        _w1Clock = 0f; _w1Timers.Clear();
        var rng = new Random();
        _dpPlayerShoots = rng.NextDouble() * 100.0 < 50.0;   // FlxG.random.bool(50)
        _dpExplode = rng.NextDouble() * 100.0 < 8.0;         // FlxG.random.bool(8)
        var force = Environment.GetEnvironmentVariable("FNF_DOPPEL");
        if (!string.IsNullOrEmpty(force) && force.Contains(','))
        {
            var p = force.Split(',');
            _dpPlayerShoots = p[0].Trim() == "1"; _dpExplode = p[1].Trim() == "1";
        }
        Console.WriteLine($"[DOPPEL] doAnim(Player, {_dpPlayerShoots}, {_dpExplode}) / doAnim(Opponent, {!_dpPlayerShoots}, {_dpExplode})");

        float bfx = _boyfriend.Position.X, bfy = _boyfriend.Position.Y, dx = _opponent.Position.X, dy = _opponent.Position.Y;
        int playerZ = _bfZIndex + 1, oppZ, bloodZ, cigZ;
        Vector2 bloodPos, cigPos, shooterPos, cigCam;
        var bfFocus = W1FocusPoint(_boyfriend); var dadFocus = W1FocusPoint(_opponent);
        bool cigFlip;
        if (_dpPlayerShoots)
        {
            oppZ = playerZ - 1; bloodZ = oppZ - 1; cigZ = _bfZIndex - 2; cigFlip = true;
            bloodPos = new Vector2(dx - 200f, dy + 400f);
            cigPos = new Vector2(bfx - 143.5f, bfy + 210f);
            shooterPos = bfFocus; cigCam = dadFocus;
        }
        else
        {
            oppZ = playerZ + 1; bloodZ = playerZ - 1; cigZ = _dadZIndex - 2; cigFlip = false;
            bloodPos = new Vector2(bfx + 500f, bfy + 400f);
            cigPos = new Vector2(bfx - 478.5f, bfy + 205f);
            cigCam = bfFocus; shooterPos = dadFocus;
        }
        var mid = (shooterPos + cigCam) / 2f;

        // cigarette (Sparrow, no shader)
        try
        {
            var cs = SpriteSheet.Load(Game, "game/stages/philly_train_erect/cigarette");
            if (cs != null)
            {
                _dpCig = new AnimatedSprite { Sheet = cs, Position = cigPos, Scale = Vector2.One, Effects = cigFlip ? SpriteEffects.FlipHorizontally : SpriteEffects.None };
                var cig = _dpCig;
                var prop = new SsfProp { Name = "dpCigarette", ZIndex = cigZ, Visible = true, Alpha = 1f, ScaleX = 1f, ScaleY = 1f, ScrollX = 1f, ScrollY = 1f };
                prop.DrawFn = sb =>
                {
                    if (!_dpCigVis || cig.CurrentAnimation == null) return;
                    var keep = cig.Position; cig.Position = WorldToScreen(cigPos.X, cigPos.Y, 1f, 1f); cig.Draw(sb); cig.Position = keep;
                };
                _staticProps.Add(prop);
            }
        }
        catch (Exception ex) { Console.WriteLine($"[DOPPEL] cigarette: {ex.Message}"); }

        _dpPlayer = DpAtlas("game/stages/philly_train_erect/pico_doppleganger", bfx - 537f, bfy - 209f, playerZ, "dpPlayer", () => _dpPlayerVis);
        _dpOpponent = DpAtlas("game/stages/philly_train_erect/pico_doppleganger", dx - 502f, dy - 209f, oppZ, "dpOpponent", () => _dpOpponentVis);
        _dpBlood = DpAtlas("game/stages/philly_train_erect/bloodPool", bloodPos.X, bloodPos.Y, bloodZ, "dpBloodPool", () => _dpBloodVis, scaleFn: true);
        _staticProps = _staticProps.OrderBy(p => p.ZIndex).ToList();   // currentStage.refresh()
        _dpPlayerVis = _dpOpponentVis = true; _dpBloodVis = false; _dpCigVis = false;

        _boyfriend.Visible = false; _opponent.Visible = false;

        _dpMusic = Audio.CreateSoundInstance(_dpExplode ? "music/week3/cutscene/cutscene.ogg" : "music/week3/cutscene/cutscene2.ogg");
        if (_dpMusic != null) { _dpMusic.IsLooped = true; _dpMusic.Volume = Math.Clamp(Audio.MusicVolume, 0f, 1f); _dpMusic.Play(); }
        _w1Bpm = 150f; _w1LastBeat = 0; _w1BeatDad = false; _w1MusicClock = 0f;   // cutsceneConductor (150 bpm): gf dance

        DpDoAnim(_dpPlayer, "Player", _dpPlayerShoots);
        DpDoAnim(_dpOpponent, "Opponent", !_dpPlayerShoots);

        // resetCamera(false, true) (snap to the current follow point), then cameraFollowPoint = midPoint (follow lerp)
        _camFollowTweening = false; _zoomTweening = false;
        // the follow point is still where PlayState.initCharacters put it ("Camera starts at dad."): no song event ran yet
        _camWorldX = dadFocus.X; _camWorldY = dadFocus.Y;
        _camTargetWorldX = mid.X; _camTargetWorldY = mid.Y;

        W1After(4f, () => { _camTargetWorldX = cigCam.X; _camTargetWorldY = cigCam.Y; });
        W1After(6.3f, () => { _camTargetWorldX = shooterPos.X; _camTargetWorldY = shooterPos.Y; });
        W1After(8.75f, () =>
        {
            _dpSkipped = true; _dpCanSkip = false; _dpSkipHidden = true;   // skipText alpha -> 0 (0.5 s quadIn)
            _camTargetWorldX = cigCam.X; _camTargetWorldY = cigCam.Y;
            if (_dpExplode) _girlfriend?.PlayAnimation("drop70", force: true);
        });
        W1After(11.2f, () =>
        {
            if (_dpExplode && _dpBlood != null)
            {
                DpPlayMain(_dpBlood, "poolAnim");     // animation.play("", true)
                _dpBlood.OnFinish = () => _dpExtendBlood = true;
                _dpBloodVis = true;
            }
        });
        W1After(11.5f, () =>
        {
            if (!_dpExplode && _dpCig != null)
            {
                _dpCigVis = true;
                _dpCig.PlayAnimation(DpFindAnim(_dpCig, "cigarette spit"), force: true, loop: false);
            }
        });
        W1After(13f, () =>
        {
            if (_dpExplode)
            {
                if (_dpPlayerShoots)
                {
                    _dpPlayerVis = false; _boyfriend.Visible = true;
                    _dpMuteOpponent = true;
                    DpEnd(true);
                }
                else
                {
                    _dpOpponentVis = false; _opponent.Visible = true;   // disableKeys
                    _dpDeathEnd = true;
                    W1After(1f, () => DpCutFade(0f, 1f, 1f));          // camCutscene.fade(black, 1)
                    W1After(2f, () => { DpCutFade(1f, 0f, 0.5f); _dpActive = false; _dialogueActive = false; _forceEndSong = true; }); // endSong(true): straight to the end of the song
                }
            }
            else
            {
                _dpPlayerVis = false; _boyfriend.Visible = true;
                _dpOpponentVis = false; _opponent.Visible = true;
                DpEnd(true);
            }
            DpStopMusic();
        });
    }

    private void DpDoAnim(AnimatedSprite s, string suffix, bool shoot)
    {
        if (s == null) return;
        W1After(0.3f, () => DpSound("sounds/week3/cutscene/picoGasp.ogg"));
        if (shoot)
        {
            s.PlayAnimation(DpFindAnim(s, "shoot" + suffix), force: true, loop: false);
            W1After(6.29f, () => DpSound("sounds/week3/cutscene/picoShoot.ogg"));
            W1After(10.33f, () => DpSound("sounds/week3/cutscene/picoSpin.ogg"));
        }
        else if (_dpExplode)
        {
            s.PlayAnimation(DpFindAnim(s, "explode" + suffix), force: true, loop: false);
            // startLoop(): animation.play("loop") (no such animation: keeps the finished explode anim) + looped = true
            W1After(3.7f, () => DpSound("sounds/week3/cutscene/picoCigarette2.ogg"));
            W1After(8.75f, () => DpSound("sounds/week3/cutscene/picoExplode.ogg"));
        }
        else
        {
            s.PlayAnimation(DpFindAnim(s, "cigarette" + suffix), force: true, loop: false);
            W1After(3.7f, () => DpSound("sounds/week3/cutscene/picoCigarette.ogg"));
        }
    }

    private static string DpFindAnim(AnimatedSprite s, string name)
    {
        if (s?.Sheet == null) return name;
        if (s.Sheet.Animations.ContainsKey(name) || s.Sheet.CompositeAnimations.ContainsKey(name) || s.Sheet.RawCompositeData.ContainsKey(name)) return name;
        var k = s.Sheet.Animations.Keys.Concat(s.Sheet.CompositeAnimations.Keys).FirstOrDefault(n => n.StartsWith(name, StringComparison.Ordinal));
        if (k == null) Console.WriteLine($"[DOPPEL] anim '{name}' not found: {string.Join("|", s.Sheet.Animations.Keys.Take(12))}");
        return k ?? name;
    }

    private static void DpPlayMain(AnimatedSprite s, string name) => s?.PlayAnimation(DpFindAnim(s, name), force: true, loop: false);

    private void DpCutFade(float from, float to, float dur) { _dpCutFadeFrom = from; _dpCutFadeTo = to; _dpCutFadeDur = dur; _dpCutFadeT = 0f; _dpCutFadeA = from; }

    private void DpStopMusic()
    {
        try { _dpMusic?.Stop(); _dpMusic?.Dispose(); } catch { }
        _dpMusic = null; _w1MusicClock = -1f;
    }

    /// <summary>startCountdown() (isInCutscene false, mayPauseGame true).</summary>
    private void DpEnd(bool countdown)
    {
        _dpActive = false;
        if (countdown) W6StartCountdown(true);
    }

    private void DpSkip()
    {
        _dpSkipped = true; _dpExplode = false;
        DpCutFade(0f, 1f, 0.5f);                    // camCutscene.fade(black, 0.5)
        float v0 = _dpMusic?.Volume ?? 0f; float t0 = _w1Clock;
        W1Tween(0.5f, 0f, null, v => { if (_dpMusic != null) _dpMusic.Volume = Math.Max(0f, v0 * (1f - v)); }); // fadeOut(0.5, 0)
        _w1Timers.Add((_w1Clock + 0.5f, () =>
        {
            DpCutFade(1f, 0f, 0.5f);
            _w1Timers.Clear();                      // cutsceneTimerManager.clear()
            foreach (var s in _dpSounds) { try { s.Stop(); } catch { } }   // cancelSounds()
            DpStopMusic();
            _dpSkipShown = false;
            _dpPlayerVis = false; _dpOpponentVis = false;
            _boyfriend.Visible = true; _opponent.Visible = true;
            DpEnd(true);
        }));
    }

    /// <summary>Per frame from the cutscene hook (not paused).</summary>
    private void DoppelUpdate(float dt)
    {
        if (_dpPlayer == null) return;
        _dpPlayer?.Update(dt); _dpOpponent?.Update(dt); _dpCig?.Update(dt);
        if (_dpBloodVis) _dpBlood?.Update(dt);
        if (_dpExtendBlood) _dpBloodScale += 0.02f * dt;
        if (_dpCutFadeT >= 0f)
        {
            _dpCutFadeT += dt;
            float t = Math.Min(1f, _dpCutFadeT / _dpCutFadeDur);
            _dpCutFadeA = _dpCutFadeFrom + (_dpCutFadeTo - _dpCutFadeFrom) * t;
            if (t >= 1f) _dpCutFadeT = -1f;
        }
        if (_dpSkipShown && !_dpSkipHidden && _dpSkipAlpha < 1f) _dpSkipAlpha = Math.Min(1f, _dpSkipAlpha + dt / 0.5f);
        if (_dpSkipHidden && _dpSkipAlpha > 0f) _dpSkipAlpha = Math.Max(0f, _dpSkipAlpha - dt / 0.5f);
        if (_dpMuteOpponent) Audio.SetOpponentVoiceVolume(0f);
        // CUTSCENE_ADVANCE
        if (_dpActive && !_dpSkipped && Input.CutsceneAdvancePressed)
        {
            if (!_dpCanSkip) { if (!_dpSkipShown) { _dpSkipShown = true; W1After(0.5f, () => _dpCanSkip = true); } }
            else DpSkip();
        }
        if (_dpLog && _dpActive && _w1Clock >= _dpLogNext)
        {
            _dpLogNext = (float)Math.Floor(_w1Clock * 2f) / 2f + 0.5f;
            Console.WriteLine($"[DOPPEL] t={_w1Clock * 1000:F0} cam={_camWorldX:F1},{_camWorldY:F1} target={_camTargetWorldX:F1},{_camTargetWorldY:F1} player={_dpPlayer?.CurrentAnimation}:{_dpPlayer?.FrameIndex} opp={_dpOpponent?.CurrentAnimation}:{_dpOpponent?.FrameIndex} blood={_dpBloodVis}:{_dpBlood?.FrameIndex}:{_dpBloodScale:F3} cig={_dpCigVis}:{_dpCig?.FrameIndex} gf={_girlfriend?.CurrentAnimation}:{_girlfriend?.SpriteFrameIndex}");
        }
    }

    /// <summary>While the cutscene blocks the countdown: characters/props animate, camera follow lerp.</summary>
    private void DoppelUpdateBlocked(float dt)
    {
        W1UpdateBlocked(dt);
        float k = 1f - MathF.Pow(1f - 0.04f, dt * 60f);   // FlxCamera follow lerp (DEFAULT_CAMERA_FOLLOW_RATE)
        _camWorldX += (_camTargetWorldX - _camWorldX) * k;
        _camWorldY += (_camTargetWorldY - _camWorldY) * k;
    }

    private void DoppelDraw(SpriteBatch sb)
    {
        if (_dpPlayer == null) return;
        if (!(_dpSkipShown && _dpSkipAlpha > 0f) && _dpCutFadeA <= 0f) return;
        sb.End();
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
        if (_dpSkipShown && _dpSkipAlpha > 0f)
        {
            string txt = "Skip [ " + (Input.LastDevice.ToString() == "Controller" ? "A" : "ENTER") + " ]";
            var font = W6Font(40);
            if (font != null)
            {
                var a = Math.Clamp(_dpSkipAlpha, 0f, 1f);
                for (int ox = -2; ox <= 2; ox += 2) for (int oy = -2; oy <= 2; oy += 2)
                    if (ox != 0 || oy != 0) sb.DrawString(font, txt, new Vector2(936 + ox, 618 + oy), new Color(Color.Black, a));
                sb.DrawString(font, txt, new Vector2(936, 618), new Color(Color.White, a));
            }
        }
        if (_dpCutFadeA > 0f) sb.Draw(Assets.Pixel, new Rectangle(0, 0, 1280, 720), new Color(Color.Black, Math.Clamp(_dpCutFadeA, 0f, 1f)));
        sb.End();
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
    }
}
