using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Week 5 hard-coded transitions of the original (no .hxc):
///  - PlayState.endSong (PlayState.hx:3587): story mode, song 'eggnog' going to the next song -> black screen over the
///    stage (camHUD hidden, isInCutscene) + Lights_Shut_off; the next song loads when the sound completes.
///  - VanillaCutscenes.playHorrorStartCutscene (PlayState.create for song id 'winter-horrorland', every mode/variation):
///    camHUD hidden, black screen; after 0.1 s black removed, cameraFollowPoint (400, -2050) + resetCamera (snap),
///    FlxG.camera.zoom = 2.5 (overwritten by PlayState.update the same frame), Lights_Turn_On; when it completes
///    camHUD visible with alpha 0 -> 1 over 2 s (quadInOut) and startCountdown().
/// </summary>
public partial class PlayScene
{
    private int _w5EggnogState;           // 0 none, 1 waiting for Lights_Shut_off, 2 done
    private SoundEffectInstance _w5Sound;
    private bool _w5Horror;               // winter horrorland intro running (countdown blocked)
    private int _w5HorrorStage;           // 0 black 0.1 s, 1 waiting for the sound
    private float _w5Clock;
    private float _w5HudAlpha = 1f, _w5HudFadeT = -1f;
    private RenderTarget2D _w5HudRt;
    private bool _w5Logged;
    private static readonly bool _w5Log = Environment.GetEnvironmentVariable("FNF_W5LOG") == "1";
    /// <summary>The render-target composite of camHUD.alpha did not show up in the port's scene target (unresolved);
    /// disabled: the HUD appears at full alpha when the countdown starts.</summary>
    private const bool W5HudFadeEnabled = true;

    private static bool W5IsSong(string songName, string id)
    {
        string s = (songName ?? "").ToLowerInvariant();
        return s == id || s.StartsWith(id + "_");
    }

    /// <summary>Story mode end of 'eggnog' (before loading the next song). Returns true while it blocks.</summary>
    private bool W5EggnogEndBlocks()
    {
        if (_w5EggnogState == 2) return false;
        if (_w5EggnogState == 0)
        {
            if (!W5IsSong(_songName, "eggnog") || _weekSongs == null || _weekSongIndex + 1 >= _weekSongs.Count) { _w5EggnogState = 2; return false; }
            _w5EggnogState = 1;
            Console.WriteLine("[W5] eggnog -> lights shut off");
            _w1BlackVisible = true; _w1BlackAlpha = 1f;   // blackBG (FlxG.width*3 solid black, scrollFactor 0)
            _w6HudHidden = true;                          // camHUD.visible = false
            Audio.StopVoices();
            _w5Sound = Audio.CreateSoundInstance("sounds/week5/Lights_Shut_off.ogg");
            if (_w5Sound == null) { _w5EggnogState = 2; return false; }
            _w5Sound.Volume = Math.Clamp(Audio.SfxVolume, 0f, 1f);
            _w5Sound.Play();
        }
        if (_w5Sound != null && _w5Sound.State != SoundState.Stopped) return true;
        _w5EggnogState = 2;
        return false;
    }

    /// <summary>LoadDialogue hook (the moment PlayState.create would call startCountdown).</summary>
    private bool W5HorrorInit()
    {
        if (!W5IsSong(_songName, "winter-horrorland")) return false;
        bool retry = s_w6RetryOf != null && string.Equals(s_w6RetryOf, _songName, StringComparison.OrdinalIgnoreCase);
        if (retry) return false; // PlayState.create runs once; a restart goes straight to the countdown
        Console.WriteLine("[W5] winter horrorland intro");
        _w5Horror = true; _w5HorrorStage = 0; _w5Clock = 0f;
        _dialogueActive = true;          // isInCutscene: countdown waits
        _w6HudHidden = true;             // camHUD.visible = false
        _w1BlackVisible = true; _w1BlackAlpha = 1f; // blackScreen (zIndex 1000000, scrollFactor 0)
        return true;
    }

    private void W5Update(float dt)
    {
        if (_w5HudFadeT >= 0f)
        {
            _w5HudFadeT += dt;
            float t = Math.Min(1f, _w5HudFadeT / 2f);
            float e = t <= .5f ? t * t * 2 : 1 - (--t) * t * 2;   // FlxEase.quadInOut
            _w5HudAlpha = e;
            if (_w5Log) Console.WriteLine($"[W5] hudA sp={_conductor.SongPosition * 1000:F0} a={e:F4}");
            if (_w5HudFadeT >= 2f) { _w5HudAlpha = 1f; _w5HudFadeT = -1f; }
        }
        if (_w5Clock > 0f && !_w5Horror && _w5Clock < 60f) _w5Clock += dt; // dev clock (FNF_SHOTS_W1TIME) keeps running
        if (!_w5Horror) return;
        _w5Clock += dt;
        if (_w5HorrorStage == 0 && _w5Clock >= 0.1f)
        {
            _w5HorrorStage = 1;
            _w1BlackVisible = false;
            TweenCameraToPosition(400f, -2050f, 0f);   // cameraFollowPoint.setPosition + resetCamera() (snap)
            _w5Sound = Audio.CreateSoundInstance("sounds/week5/Lights_Turn_On.ogg");
            if (_w5Sound != null) { _w5Sound.Volume = Math.Clamp(Audio.SfxVolume, 0f, 1f); _w5Sound.Play(); }
            Console.WriteLine($"[W5] Lights_Turn_On start at clock {_w5Clock:F3} inst={_w5Sound != null}");
        }
        else if (_w5HorrorStage == 1 && (_w5Sound == null || _w5Sound.State == SoundState.Stopped))
        {
            _w5Horror = false;
            Console.WriteLine($"[W5] Lights_Turn_On done at clock {_w5Clock:F3} (state {_w5Sound?.State})");
            _w5HudAlpha = 0f; _w5HudFadeT = 0f;          // camHUD.alpha = 0 -> tween 1 (2 s, quadInOut)
            W6StartCountdown(true);                       // startCountdown(): camHUD.visible = true
        }
    }

    /// <summary>Before the HUD pass: redirect it into a render target while camHUD.alpha &lt; 1.</summary>
    private RenderTargetBinding[] W5HudBegin()
    {
        if (!W5HudFadeEnabled || _w5HudAlpha >= 1f || !W6HudVisible) return null;
        var gd = Game.GraphicsDevice;
        var prev = gd.GetRenderTargets();
        int w = prev.Length > 0 && prev[0].RenderTarget is Texture2D t0 ? t0.Width : gd.PresentationParameters.BackBufferWidth;
        int h = prev.Length > 0 && prev[0].RenderTarget is Texture2D t1 ? t1.Height : gd.PresentationParameters.BackBufferHeight;
        if (_w5HudRt == null || _w5HudRt.Width != w || _w5HudRt.Height != h)
        {
            _w5HudRt?.Dispose();
            _w5HudRt = new RenderTarget2D(gd, w, h, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
        }
        gd.SetRenderTarget(_w5HudRt);
        gd.Clear(Color.Transparent);
        if (!_w5Logged) { _w5Logged = true; Console.WriteLine($"[W5] hud rt prev={prev.Length} {w}x{h} alpha={_w5HudAlpha:F2}"); }
        return prev;
    }

    private void W5HudEnd(SpriteBatch sb, RenderTargetBinding[] prev)
    {
        if (prev == null) return;
        var gd = Game.GraphicsDevice;
        var vp = gd.Viewport;
        if (prev.Length > 0) gd.SetRenderTargets(prev); else gd.SetRenderTarget(null);
        sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend);
        sb.Draw(_w5HudRt, Vector2.Zero, Color.White * Math.Clamp(_w5HudAlpha, 0f, 1f));
        sb.End();
    }
}
