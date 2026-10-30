using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Options > Lag Adjustment: tap-to-the-beat input latency calibration (like the old V-Slice LatencyState): the menu
/// music (freakyMenu, 102 BPM) keeps playing, notes scroll up into a normal strumline on every beat and the player taps
/// any note key / button in time with the MUSIC. Each tap's distance to the nearest beat (song position, the same clock
/// gameplay judges with) is collected; the trimmed average becomes the Note Offset (PlayScene adjustedPos = songPos +
/// offset, so a player who taps D ms late gets offset -D).
/// </summary>
public partial class OptionsScene
{
    private const float CAL_BPM = 102f;                 // freakyMenu
    private const int CAL_MAX_TAPS = 16;
    private const float CAL_WINDOW_MS = 180f;           // taps further than this from a beat are ignored
    private readonly List<float> _calTaps = new();      // ms, + = late
    private readonly List<(float ms, float t)> _calPopups = new();
    private readonly float[] _calConfirmT = { -9f, -9f, -9f, -9f };
    private float _calTime, _calApplied = -9f;
    private SpriteSheet _calNotes, _calReceptors;
    private static readonly string[] CalDirs = { "left", "down", "up", "right" };

    private float CalBeatMs => 60000f / CAL_BPM;

    private void EnterCalibration()
    {
        _inOffset = true;
        _calTaps.Clear(); _calPopups.Clear(); _calApplied = -9f;
        _calNotes ??= SpriteSheet.Load(Game, "game/skins/default/notes");
        _calReceptors ??= SpriteSheet.Load(Game, "game/skins/default/receptors");
        if (!Audio.MusicPlaying) Audio.PlayMusic("music/freakyMenu", true);
        Input.GameplayMode = true;     // controller A/B/X/Y are note buttons here (Back = View/Escape only)
    }

    private void ExitCalibration()
    {
        _inOffset = false;
        Input.GameplayMode = false;
        Audio.PlaySound("cancelMenu");
    }

    /// <summary>Trimmed mean of the collected taps (drops the 2 extremes once there are 8+).</summary>
    private float? CalAverage()
    {
        if (_calTaps.Count < 4) return null;
        var s = _calTaps.OrderBy(v => v).ToList();
        if (s.Count >= 8) { s.RemoveAt(0); s.RemoveAt(s.Count - 1); }
        return s.Average();
    }

    private void UpdateCalibration(float dt)
    {
        _calTime += dt;
        double pos = Audio.MusicPosition;
        float beat = CalBeatMs;

        bool tapped = Input.IsPressed(Keys.Space);
        for (int i = 0; i < 4; i++)
            if (Input.NotePressed[i]) { tapped = true; _calConfirmT[i] = _calTime; }
        if (Input.IsPressed(Keys.Space)) _calConfirmT[CalLaneAt(pos)] = _calTime;
        if (tapped && Audio.MusicPlaying)   // paused music (window lost focus) = no beat to measure against
        {
            double phase = pos % beat;
            float diff = (float)(phase > beat / 2 ? phase - beat : phase);   // + = after the beat (late)
            Console.WriteLine($"[CAL] tap pos={pos:F1} diff={diff:F1} ms");
            if (Math.Abs(diff) <= CAL_WINDOW_MS)
            {
                _calTaps.Add(diff);
                if (_calTaps.Count > CAL_MAX_TAPS) _calTaps.RemoveAt(0);
                _calPopups.Add((diff, _calTime));
            }
        }
        _calPopups.RemoveAll(p => _calTime - p.t > 0.6f);

        bool apply = Input.IsPressed(Keys.Enter) || Input.IsGamePadPressed(Buttons.Start);
        var avg = CalAverage();
        if (apply && avg.HasValue)
        {
            HighscoreManager.Data.GlobalOffset = Math.Clamp(-(int)Math.Round(avg.Value), -500, 500);
            HighscoreManager.SavePreferences();
            Audio.PlaySound("confirmMenu");
            Input.Rumble(0.4f, 0.15f);
            _calTaps.Clear(); _calApplied = _calTime;
        }
        if (Input.IsPressed(Keys.R) || Input.IsGamePadPressed(Buttons.RightStick)) { _calTaps.Clear(); Audio.PlaySound("scrollMenu"); }
        if (Input.CutsceneBackPressed) ExitCalibration(); // pad B too (face buttons are taps here: GameplayMode)
    }

    private int CalLaneAt(double pos) => (int)(Math.Round(pos / CalBeatMs) % 4 + 4) % 4;

    private void DrawCalSprite(SpriteBatch sb, SpriteSheet sheet, string anim, int frame, float cx, float cy, float scale, float alpha = 1f)
    {
        var frames = sheet?.GetAnimation(anim);
        if (frames == null || frames.Count == 0) return;
        var f = frames[Math.Clamp(frame, 0, frames.Count - 1)];
        float fw = f.FrameWidth > 0 ? f.FrameWidth : f.SourceRect.Width, fh = f.FrameHeight > 0 ? f.FrameHeight : f.SourceRect.Height;
        var p = new Vector2(cx - fw * scale / 2f + f.Offset.X * scale, cy - fh * scale / 2f + f.Offset.Y * scale);
        sb.Draw(f.Texture ?? sheet.Texture, p, f.SourceRect, new Color(Color.White, alpha), 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }

    private void DrawCalibration(SpriteBatch sb)
    {
        var bold = AtlasText.Bold; var def = AtlasText.Default;
        double pos = Audio.MusicPosition;
        float beat = CalBeatMs;
        const float s = 0.7f, laneW = 112f, recY = 165f, pxPerMs = 0.45f * 1.3f, fadeFrom = recY + 190f, fadeTo = recY + 120f;
        float x0 = 640f - laneW * 1.5f;

        bold?.DrawCentered(sb, "TAP TO THE BEAT", 22f, 1f, _alphabetBob, 0.8f);

        for (int i = 0; i < 4; i++)
        {
            float since = _calTime - _calConfirmT[i];
            bool confirm = since >= 0f && since < 0.2f;
            if (confirm) DrawCalSprite(sb, _calReceptors, $"{CalDirs[i]} confirm", (int)(since * 24f), x0 + i * laneW, recY, s);
            else DrawCalSprite(sb, _calReceptors, $"{CalDirs[i]} static", 0, x0 + i * laneW, recY, s);
        }

        // notes: one per beat, cycling the lanes, scrolling up into the strumline (reach it exactly on the beat)
        long b0 = (long)Math.Floor(pos / beat) - 1;
        for (long b = b0; b <= b0 + 6; b++)
        {
            double t = b * beat;
            float y = recY + (float)(t - pos) * pxPerMs;
            if (y < recY - 40f || y > fadeFrom) continue;
            int lane = (int)((b % 4 + 4) % 4);
            float a = y <= fadeTo ? 1f : Math.Clamp((fadeFrom - y) / (fadeFrom - fadeTo), 0f, 1f);
            // notes draw over the receptors (like the gameplay strumline) and vanish once they reach them
            if (y >= recY) DrawCalSprite(sb, _calNotes, $"{CalDirs[lane]} note", 0, x0 + lane * laneW, y, s, a);
        }
        // per-tap popups ("+12 MS") rising from the strumline
        foreach (var (ms, t) in _calPopups)
        {
            float k = (_calTime - t) / 0.6f;
            string txt = $"{(ms >= 0 ? "+" : "")}{(int)Math.Round(ms)} MS";
            def?.Draw(sb, txt, 640f + laneW * 2f + 20f, recY - 20f - k * 30f, 1f - k * k, _alphabetBob, 0.6f);
        }

        // timing bar: -150 .. +150 ms, early left / late right, one tick per collected tap, yellow = average
        int barW = 640, barX = 640 - barW / 2, barY = 470, barH = 14;
        MenuUI.Rect(sb, new Rectangle(barX - 4, barY - 4, barW + 8, barH + 8), Color.Black);
        MenuUI.Rect(sb, new Rectangle(barX, barY, barW, barH), new Color(255, 255, 255, 90));
        MenuUI.Rect(sb, new Rectangle(640 - 2, barY - 14, 4, barH + 28), Color.Black);
        MenuUI.Rect(sb, new Rectangle(640 - 1, barY - 12, 2, barH + 24), Color.White);
        float Bx(float ms) => 640f + Math.Clamp(ms, -150f, 150f) / 150f * (barW / 2f);
        for (int i = 0; i < _calTaps.Count; i++)
        {
            float age = (_calTaps.Count - 1 - i) / (float)CAL_MAX_TAPS;
            int x = (int)Bx(_calTaps[i]);
            MenuUI.Rect(sb, new Rectangle(x - 3, barY - 7, 6, barH + 14), Color.Black);
            MenuUI.Rect(sb, new Rectangle(x - 1, barY - 5, 2, barH + 10), new Color(Color.White, 1f - age * 0.7f));
        }
        def?.Draw(sb, "EARLY", barX, barY + 26, 1f, _alphabetBob, 0.5f);
        def?.Draw(sb, "LATE", barX + barW - (def?.Width("LATE", 0.5f) ?? 60), barY + 26, 1f, _alphabetBob, 0.5f);

        var avg = CalAverage();
        var d = HighscoreManager.Data;
        if (avg.HasValue)
        {
            int ax = (int)Bx(avg.Value);
            MenuUI.Rect(sb, new Rectangle(ax - 6, barY - 6, 12, barH + 12), Color.Black);
            MenuUI.Rect(sb, new Rectangle(ax - 4, barY - 4, 8, barH + 8), new Color(0xFA, 0xFD, 0x6D));
            int a = (int)Math.Round(avg.Value);
            string how = a == 0 ? "RIGHT ON THE BEAT" : a > 0 ? $"YOU TAP {a} MS LATE" : $"YOU TAP {-a} MS EARLY";
            def?.DrawCentered(sb, how, 350f, 1f, _alphabetBob, 0.75f);
            def?.DrawCentered(sb, $"PRESS APPLY TO SET THE OFFSET TO {-a} MS", 400f, 1f, _alphabetBob, 0.5f);
        }
        else
        {
            def?.DrawCentered(sb, _calTaps.Count == 0 ? "TAP ANY NOTE KEY ON EVERY BEAT OF THE MUSIC" : $"KEEP TAPPING... {4 - _calTaps.Count}", 380f, 0.85f, _alphabetBob, 0.6f);
        }
        float applied = _calTime - _calApplied;
        float curAlpha = 1f;
        def?.DrawCentered(sb, $"CURRENT NOTE OFFSET {d.GlobalOffset} MS", 540f, curAlpha, _alphabetBob, 0.6f,
            applied < 1f ? Color.Lerp(new Color(0xFA, 0xFD, 0x6D), Color.White, applied) : (Color?)null);
        if (applied < 1f) bold?.DrawCentered(sb, "SAVED", 590f, 1f - applied * applied, _alphabetBob, 0.7f);
    }

    private void AddCalibrationPrompts(List<ButtonPrompts.Prompt> l)
    {
        l.Add(new ButtonPrompts.Prompt(Buttons.A, "SPACE", "TAP"));
        l.Add(new ButtonPrompts.Prompt(Buttons.Start, "ENTER", "APPLY"));
        l.Add(new ButtonPrompts.Prompt(Buttons.RightStick, "R", "RESET"));
    }
}
