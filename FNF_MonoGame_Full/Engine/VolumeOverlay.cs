using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Volume overlay HUD - exact port of original FNF's FunkinSoundTray.
/// Uses original assets: images/soundtray/volumebox.png, bars_1-10.png
/// Slides from top of screen, shows volume level with bar graphics.
/// </summary>
public class VolumeOverlay
{
    // Original FNF: graphicScale = 0.30
    private const float GRAPHIC_SCALE = 0.30f;

    // Original assets
    private Texture2D _bgTex;
    private Texture2D _backingBarTex;
    private readonly Texture2D[] _barTex = new Texture2D[10];

    // Original FNF tray state
    private float _lerpYPos;
    private float _alphaTarget;
    private float _timer;
    private float _currentY;
    private float _currentAlpha;
    private bool _active;

    // Volume sounds (original: soundtray/Volup, Voldown, VolMAX)
    private const string VOL_UP_SOUND = "soundtray/Volup";
    private const string VOL_DOWN_SOUND = "soundtray/Voldown";
    private const string VOL_MAX_SOUND = "soundtray/VolMAX";

    // Keys (original FNF defaults)
    // VOLUME_UP: PLUS, NUMPADPLUS
    // VOLUME_DOWN: MINUS, NUMPADMINUS
    // VOLUME_MUTE: ZERO, NUMPADZERO

    public void LoadTextures(AssetManager assets)
    {
        _bgTex = assets.LoadTexture("images/soundtray/volumebox.png");
        _backingBarTex = assets.LoadTexture("images/soundtray/bars_10.png");
        for (int i = 0; i < 10; i++)
            _barTex[i] = assets.LoadTexture($"images/soundtray/bars_{i + 1}.png");
    }

    public void Update(float dt, InputManager input, AudioManager audio)
    {
        bool volUp = input.IsPressed(Keys.Add) || input.IsPressed(Keys.OemPlus);
        bool volDown = input.IsPressed(Keys.Subtract) || input.IsPressed(Keys.OemMinus);
        bool volMute = input.IsPressed(Keys.D0) || input.IsPressed(Keys.NumPad0);

        if (volMute)
        {
            audio.Muted = !audio.Muted;
            ShowTray(false, true);
            SaveVolumePreferences(audio);
            return;
        }

        if (volUp || volDown)
        {
            var data = HighscoreManager.Data;
            float delta = volUp ? 0.1f : -0.1f;

            // Unmute when changing volume (original behavior)
            if (audio.Muted) audio.Muted = false;

            float newVol = Math.Clamp(data.MusicVolume + delta, 0f, 1f);
            data.MusicVolume = newVol;
            data.SfxVolume = newVol;
            audio.MusicVolume = newVol;
            audio.SfxVolume = newVol;

            ShowTray(volUp);
            SaveVolumePreferences(audio);
        }

        // Update tray animation
        UpdateTray(dt);
        if (SuppressTrayFrames > 0) SuppressTrayFrames--;
    }

    /// <summary>Kept > 0 by the pause menu while its own Volume row is visible: +/- still change the volume, but the top
    /// sound tray is not shown on top of it (it duplicated the pause row).</summary>
    public static int SuppressTrayFrames;
    public static bool SuppressTray => SuppressTrayFrames > 0;

    private void ShowTray(bool up = false, bool silent = false)
    {
        if (SuppressTray) { _active = false; _currentY = -TrayHeight - 10f; _lerpYPos = -TrayHeight - 10f; _alphaTarget = 0f; _currentAlpha = 0f; GetGlobalVolume(up); if (!silent && !string.IsNullOrEmpty(_pendingSound)) FNFGame.Instance?.Audio?.PlaySound(_pendingSound, 0.3f); _pendingSound = null; return; }
        _timer = 1f;
        _lerpYPos = 10f;
        _active = true;
        _alphaTarget = 1f;

        int globalVol = GetGlobalVolume(up);
        for (int i = 0; i < 10; i++)
            _barVisible[i] = i < globalVol;

        // Play feedback sound (original: volume 0.3)
        if (!silent && !string.IsNullOrEmpty(_pendingSound))
        {
            FNFGame.Instance?.Audio?.PlaySound(_pendingSound, 0.3f);
            _pendingSound = null;
        }
    }

    private string _pendingSound;

    private int GetGlobalVolume(bool up)
    {
        var audio = FNFGame.Instance?.Audio;
        float vol = audio?.MusicVolume ?? HighscoreManager.Data.MusicVolume;

        // Original: logToLinear(volume) * 10, rounded
        // Flixel formula: Math.exp(volume * Math.log(10)) / 10
        int globalVolume = (int)Math.Round(Math.Exp(vol * Math.Log(10)) / 10 * 10);

        if (audio?.Muted == true || vol == 0f) globalVolume = 0;

        // Pick sound to play
        if (up)
        {
            _pendingSound = VOL_UP_SOUND;
            if (globalVolume >= 10) _pendingSound = VOL_MAX_SOUND;
        }
        else
        {
            _pendingSound = VOL_DOWN_SOUND;
        }

        return globalVolume;
    }

    private readonly bool[] _barVisible = new bool[10];

    private void UpdateTray(float dt)
    {
        if (!_active) return;

        var audio = FNFGame.Instance?.Audio;
        bool hasVolume = !(audio?.Muted == true || (audio?.MusicVolume ?? 0f) == 0f);

        if (hasVolume)
        {
            if (_timer > 0f)
            {
                _timer -= dt;
            }
            else if (_currentY >= -TrayHeight)
            {
                _lerpYPos = -TrayHeight - 10f;
                _alphaTarget = 0f;
            }

            if (_currentY <= -TrayHeight)
            {
                _active = false;
            }
        }
        else if (!_active)
        {
            // When muted or volume=0, stay visible
            _timer = 1f;
            _lerpYPos = 10f;
            _active = true;
            _alphaTarget = 1f;
        }

        // smoothLerpPrecision(current, target, dt, duration)
        // Original durations: y=0.768, alpha=0.307
        _currentY = SmoothLerpPrecision(_currentY, _lerpYPos, dt, 0.768f);
        _currentAlpha = SmoothLerpPrecision(_currentAlpha, _alphaTarget, dt, 0.307f);
    }

    private float TrayHeight => (_bgTex?.Height ?? 146) * GRAPHIC_SCALE;

    private static float SmoothLerpPrecision(float current, float target, float dt, float duration, float precision = 0.01f)
    {
        if (dt == 0f) return current;
        if (current == target) return target;
        float t = MathF.Pow(precision, dt / duration);
        return target + (current - target) * t;
    }

    private void SaveVolumePreferences(AudioManager audio)
    {
        var data = HighscoreManager.Data;
        data.Mute = audio.Muted;
        data.MusicVolume = audio.MusicVolume;
        data.SfxVolume = audio.SfxVolume;
        HighscoreManager.SavePreferences();
    }

    public void Draw(SpriteBatch sb)
    {
        if (SuppressTray || (!_active && _currentY <= -TrayHeight)) return;
        if (_bgTex == null) return;

        float alpha = _currentAlpha;
        if (alpha <= 0.01f) return;

        float trayW = _bgTex.Width * GRAPHIC_SCALE;
        float trayH = _bgTex.Height * GRAPHIC_SCALE;
        float x = (FNFGame.SCREEN_WIDTH - trayW) / 2f;
        float y = _currentY;

        Color col = Color.White;
        col.A = (byte)(255 * alpha);

        // Background box
        sb.Draw(_bgTex, new Vector2(x, y), null, col, 0f, Vector2.Zero, GRAPHIC_SCALE, SpriteEffects.None, 0f);

        // Backing bar (alpha 0.4 in original)
        Color backCol = col;
        backCol.A = (byte)(col.A * 0.4f);
        float barX = x + 9 * GRAPHIC_SCALE;
        float barY = y + 5 * GRAPHIC_SCALE;
        sb.Draw(_backingBarTex, new Vector2(barX, barY), null, backCol, 0f, Vector2.Zero, GRAPHIC_SCALE, SpriteEffects.None, 0f);

        // Active bars
        for (int i = 0; i < 10; i++)
        {
            if (_barVisible[i])
                sb.Draw(_barTex[i], new Vector2(barX, barY), null, col, 0f, Vector2.Zero, GRAPHIC_SCALE, SpriteEffects.None, 0f);
        }
    }
}
