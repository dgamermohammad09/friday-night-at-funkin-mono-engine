using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>Results screen: "watch replay" of the play that just ended (R / gamepad Y), saved by PlayScene.ReplaySaveOnEnd.</summary>
public partial class ResultsScene
{
    public string ReplayPath { get; set; }
    private static readonly float _devReplayAt = float.TryParse(Environment.GetEnvironmentVariable("FNF_RES_REPLAY_AT"),
        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : -1f;

    /// <summary>True when the replay was started (scene changed).</summary>
    private bool UpdateReplayPrompt()
    {
        if (_busy || string.IsNullOrEmpty(ReplayPath) || _t < 1f) return false;
        bool go = Input.IsPressed(Keys.R) || Input.IsGamePadPressed(Buttons.Y) || (_devReplayAt >= 0f && _t >= _devReplayAt);
        if (!go) return false;
        var ps = PlayScene.CreateReplayScene(ReplayManager.Load(ReplayPath));
        if (ps == null) return false;
        _busy = true;
        Audio.StopMusic();
        Audio.PlaySound("confirmMenu");
        Game.Scenes.ChangeScene(ps);
        return true;
    }

    /// <summary>Taped label bottom-left (hand-made: black tape strip, slight tilt, hard shadow, VCR text + glyph).</summary>
    private void DrawReplayPrompt(SpriteBatch sb)
    {
        if (string.IsNullOrEmpty(ReplayPath) || _t < 1f) return;
        float a = Math.Min(1f, (_t - 1f) / 0.3f);
        var font = Assets.GetFont(18);
        if (font == null) return;
        const string label = "WATCH REPLAY";
        int w = 64 + (int)font.MeasureString(label).X + 24, h = 40;
        var m = Matrix.CreateTranslation(-w / 2f, -h / 2f, 0) * Matrix.CreateRotationZ(-0.035f) * Matrix.CreateTranslation(40 + w / 2f, 662 + h / 2f, 0);
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp, null, null, null, m);
        sb.Draw(Assets.Pixel, new Rectangle(5, 6, w, h), new Color(Color.Black, 0.45f * a));            // hard shadow
        sb.Draw(Assets.Pixel, new Rectangle(-3, -3, w + 6, h + 6), new Color(Color.White, a));          // paper edge
        sb.Draw(Assets.Pixel, new Rectangle(0, 0, w, h), new Color(new Color(0x24, 0x18, 0x2E), a));
        sb.Draw(Assets.Pixel, new Rectangle(w - 18, -8, 26, 14), new Color(new Color(0xFF, 0xE0, 0x6B), 0.85f * a)); // tape
        bool pad = Input.LastDevice == InputManager.InputDevice.Controller;
        ButtonPrompts.DrawGlyph(sb, Assets, pad, Buttons.Y, "R", 10, 6, 28, a);
        font.DrawText(sb, label, new Vector2(56, 11), new Color(Color.White, a));
        sb.End();
    }
}
