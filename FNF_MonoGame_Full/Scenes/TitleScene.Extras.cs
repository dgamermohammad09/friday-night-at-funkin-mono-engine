using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// TitleState.hx (0.8.1) extras:
///  - attractTimer: Constants.TITLE_ATTRACT_DELAY (37.5 s) after startIntro -> music.fadeOut(2) + camera.fade(BLACK, 2)
///    -> AttractState. Cancelled only by moveToMainMenu and startCheat (input does NOT reset it).
///  - swagShader (ColorSwap) on logoBl / gfDance / titleText: UI_LEFT/UI_RIGHT held -> hue -/+ elapsed*0.1.
///  - cheat code LEFT RIGHT LEFT RIGHT UP DOWN UP DOWN (NOTE_* or UI_* presses, after skipIntro):
///    girlfriendsRingtone (fadeIn 4 s 0 -> 1), camera.flash(WHITE, 1), confirmMenu 0.7, hue += 0.125 every even beat.
/// Dev hook FNF_ATTRACT_DELAY=&lt;s&gt; shortens the timer for tests.
/// </summary>
public partial class TitleScene
{
    private const float TitleAttractDelay = 37.5f; // Constants.TITLE_ATTRACT_DELAY
    private float _attractTimer = -1f;
    private float _attractFadeT = -1f;
    private float _hue;               // ColorSwap.uTime
    private bool _cheatActive;
    private int _cheatPos;
    private static Effect s_colorSwap;
    private static bool s_colorSwapTried;

    // FlxDirectionFlags: LEFT 0x0001, RIGHT 0x0010, UP 0x0100, DOWN 0x1000
    private static readonly int[] CheatArray = { 0x0001, 0x0010, 0x0001, 0x0010, 0x0100, 0x1000, 0x0100, 0x1000 };

    private void LoadTitleExtras()
    {
        float delay = TitleAttractDelay;
        var env = Environment.GetEnvironmentVariable("FNF_ATTRACT_DELAY");
        if (!string.IsNullOrEmpty(env) && float.TryParse(env, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var d)) delay = d;
        _attractTimer = delay;
        Console.WriteLine($"[TITLE] Opening Attract state in {delay} seconds...");
    }

    /// <summary>Returns true when the scene was switched (stop updating).</summary>
    private bool UpdateTitleExtras(float dt)
    {
        if (_attractTimer > 0f)
        {
            _attractTimer -= dt;
            if (_attractTimer <= 0f) { _attractTimer = -1f; MoveToAttract(); }
        }
        if (_attractFadeT >= 0f)
        {
            _attractFadeT += dt;
            if (_attractFadeT >= 2f)
            {
                _attractFadeT = -1f;
                Game.Scenes.SwitchSceneInstant(new AttractScene());
                return true;
            }
        }

        if (Input.LeftHeld) _hue -= dt * 0.1f;
        if (Input.RightHeld) _hue += dt * 0.1f;
        if (!_cheatActive && _skippedIntro) CheatCodeShit();
        return false;
    }

    private void MoveToAttract()
    {
        Console.WriteLine("[TITLE] moveToAttract");
        Audio.FadeOutMusic(2f);
        _attractFadeT = 0f; // FlxG.camera.fade(BLACK, 2.0, false, -> AttractState)
    }

    private void CheatCodeShit()
    {
        if (Input.DownPressed) CodePress(0x1000);
        if (Input.UpPressed) CodePress(0x0100);
        if (Input.LeftPressed) CodePress(0x0001);
        if (Input.RightPressed) CodePress(0x0010);
    }

    private void CodePress(int input)
    {
        if (_cheatActive) return;
        if (input == CheatArray[_cheatPos])
        {
            _cheatPos++;
            if (_cheatPos >= CheatArray.Length) StartCheat();
        }
        else _cheatPos = 0;
    }

    private void StartCheat()
    {
        Console.WriteLine("[TITLE] cheat code: girlfriendsRingtone");
        _cheatActive = true;
        // playMusic maps the Conductor to 160 BPM before the track restarts: the OLD music time is first read with the
        // new BPM, so a different (even) beat fires one extra beatHit (orig trace: pos 2489 -> beat 6, hue +0.125, then beat 0).
        double oldPosS = Audio.MusicPosition / 1000.0;
        int oldBeat = (int)(oldPosS / _beatDuration), remapped = (int)(oldPosS / (60.0 / 160.0));
        if (remapped != oldBeat && remapped % 2 == 0) _hue += 0.125f;
        Console.WriteLine($"[TITLE] cheat at pos={oldPosS * 1000:0} beat {oldBeat} -> remapped {remapped} hue={_hue:0.###}");
        Audio.PlayMusic("music/girlfriendsRingtone", true);
        Audio.FadeIn(4f, 0f, Audio.MusicVolume);
        _beatDuration = 60f / 160f; // girlfriendsRingtone-metadata.json bpm 160
        _lastBeat = -1;
        _flashAlpha = 1f; _flashDuration = 1f;
        Audio.PlaySound("confirmMenu", 0.7f);
        _attractTimer = -1f; // attractTimer.cancel()
    }

    private Effect ColorSwapEffect
    {
        get
        {
            if (s_colorSwap != null || s_colorSwapTried) return s_colorSwap;
            s_colorSwapTried = true;
            try
            {
                string path = Assets.ResolvePath("shaders/ColorSwap.mgfx")
                              ?? Path.Combine(AssetManager.ContentRoot, "shaders", "ColorSwap.mgfx");
                if (global::FNF_MonoGame.Engine.FsCI.FileExists(path)) s_colorSwap = new Effect(Game.GraphicsDevice, global::FNF_MonoGame.Engine.FsCI.ReadAllBytes(path));
                else Console.WriteLine("[TITLE] ColorSwap.mgfx not found");
            }
            catch (Exception ex) { Console.WriteLine($"[TITLE] ColorSwap: {ex.Message}"); }
            return s_colorSwap;
        }
    }

    private void BeginColorSwap(SpriteBatch sb)
    {
        var fx = ColorSwapEffect;
        if (fx == null) return;
        sb.End();
        fx.Parameters["uTime"]?.SetValue(_hue);
        sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied, effect: fx);
    }

    private void EndColorSwap(SpriteBatch sb)
    {
        if (ColorSwapEffect == null) return;
        sb.End();
        sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);
    }

    private void DrawAttractFade(SpriteBatch sb)
    {
        if (_attractFadeT < 0f) return;
        float a = Math.Min(1f, _attractFadeT / 2f);
        sb.Draw(Assets.Pixel, new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT), new Color(Color.Black, a));
    }
}
