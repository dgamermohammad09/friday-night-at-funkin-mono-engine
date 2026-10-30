using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>Pause menu volume widget (port addition requested by the user; the original changes volume only via the
/// global +/- sound tray). Bottom-right, under the metadata and above the offset texts:
///   "VOLUME 70%"  [ - ]  |||||||...  [ + ]
/// '-' / '+' are the bold AtlasText glyphs used by the menu entries; bars rise like FunkinSoundTray's.
/// Input: it is a real row of the pause list (UP/DOWN past the last/first entry focuses it): LEFT/RIGHT (keyboard,
/// D-pad, stick) or ACCEPT on the focused '-' / '+'; LB/RB anywhere; mouse click (hold repeats) on a button or a bar.
/// Step 0.1 (FunkinSoundTray), feedback sounds soundtray/Volup|Voldown|VolMAX at 0.3.</summary>
public partial class PlayScene
{
    private const int PmVolBtn = 52, PmVolSegW = 20, PmVolSegGap = 4, PmVolGap = 10, PmVolRight = 1258, PmVolTop = 574;
    private const int PmVolBarW = 10 * PmVolSegW + 9 * PmVolSegGap;
    private static Rectangle PmVolPlusRect => new(PmVolRight - PmVolBtn, PmVolTop, PmVolBtn, PmVolBtn);
    private static Rectangle PmVolBarRect => new(PmVolRight - PmVolBtn - PmVolGap - PmVolBarW, PmVolTop, PmVolBarW, PmVolBtn);
    private static Rectangle PmVolMinusRect => new(PmVolBarRect.X - PmVolGap - PmVolBtn, PmVolTop, PmVolBtn, PmVolBtn);

    private int _pmVolHover;          // -1 minus, +1 plus, 2 bar, 0 none (mouse)
    private int _pmVolHoldDir;        // direction being held (mouse / key / pad) for auto-repeat
    private float _pmVolHoldT;
    private float _pmVolPopMinus = 1f, _pmVolPopPlus = 1f; // press "pop" timers (0 -> 1)
    private bool _pmVolFocus;         // the Volume row is the selected row of the pause list
    private int _pmVolSub = 1;        // focused button of the row: -1 '-', +1 '+'
    private float _pmVolFocusT = 1f;  // seconds since the row got focus (selection pop)

    private void UpdateVolumeWidgetV(float dt)
    {
        _pmVolPopMinus = Math.Min(1f, _pmVolPopMinus + dt / 0.15f);
        _pmVolPopPlus = Math.Min(1f, _pmVolPopPlus + dt / 0.15f);
        _pmVolFocusT += dt;
#if !WINDOWS_UAP
        if (_pmT > 0.8f) VolumeOverlay.SuppressTrayFrames = 3; // the Volume row is on screen: no second tray at the top
#endif

        // mouse
        var mv = MouseVirtual;
        var mp = new Point((int)mv.X, (int)mv.Y);
        bool mouseIn = mv.X >= 0;
        _pmVolHover = !mouseIn ? 0 : PmVolMinusRect.Contains(mp) ? -1 : PmVolPlusRect.Contains(mp) ? 1 : PmVolBarRect.Contains(mp) ? 2 : 0;
        bool mouseHeld = false;
        mouseHeld = Input.MouseLeftHeld; // filtered: controller->pointer emulation is not a mouse

        int press = 0;
        if (Input.MouseLeftPressed && _pmVolHover != 0)
        {
            if (_pmVolHover == 2)
            {   // click on the bar: set the level of that bar
                int seg = Math.Clamp((mp.X - PmVolBarRect.X + PmVolSegGap / 2) / (PmVolSegW + PmVolSegGap), 0, 9);
                float target = (seg + 1) / 10f;
                if (Math.Abs(target - Audio.MusicVolume) < 0.001f) target = seg / 10f; // click the top bar again -> one step down
                ChangeVolumeV(target - Audio.MusicVolume);
            }
            else { press = _pmVolHover; _pmVolSub = press; }
            if (!_pmVolFocus) { _pmVolFocus = true; _pmVolFocusT = 0f; }
        }
        // keyboard / D-pad / stick LEFT-RIGHT, LB/RB
        bool shift = false;
        try { var ks = Keyboard.GetState(); shift = ks.IsKeyDown(Keys.LeftShift) || ks.IsKeyDown(Keys.RightShift); } catch { }
        if (!shift)
        {
            // LB/RB change the volume from anywhere in the pause menu; LEFT/RIGHT only on the focused Volume row
            bool r = Input.IsGamePadPressed(Buttons.RightShoulder) || (_pmVolFocus && Input.RightPressed);
            bool l = Input.IsGamePadPressed(Buttons.LeftShoulder) || (_pmVolFocus && Input.LeftPressed);
            if (r) press = 1;
            if (l) press = press == 1 ? 0 : -1;
            if (press != 0) _pmVolSub = press;
            // ACCEPT (A / ENTER) on the focused row presses the focused button
            if (_pmVolFocus && press == 0 && _pmT > 0.05f && Input.ConfirmPressed) press = _pmVolSub;
        }
        if (DevPauseVolAt >= 0 && !_devPauseVolDone && _pmT >= DevPauseVolAt) { _devPauseVolDone = true; press = DevPauseVolDir; }

        if (press != 0)
        {
            PmVolStep(press);
            _pmVolHoldDir = press; _pmVolHoldT = 0f;
        }
        else if (_pmVolHoldDir != 0)
        {
            bool held = _pmVolHoldDir > 0
                ? (mouseHeld && _pmVolHover == 1) || (_pmVolFocus && (Input.IsHeld(Keys.Right) || Input.IsGamePadHeld(Buttons.DPadRight) || Input.IsGamePadHeld(Buttons.A) || Input.IsHeld(Keys.Enter))) || Input.IsGamePadHeld(Buttons.RightShoulder)
                : (mouseHeld && _pmVolHover == -1) || (_pmVolFocus && (Input.IsHeld(Keys.Left) || Input.IsGamePadHeld(Buttons.DPadLeft) || Input.IsGamePadHeld(Buttons.A) || Input.IsHeld(Keys.Enter))) || Input.IsGamePadHeld(Buttons.LeftShoulder);
            if (!held) _pmVolHoldDir = 0;
            else
            {   // auto-repeat: 0.4 s delay, then every 0.1 s
                float before = _pmVolHoldT;
                _pmVolHoldT += dt;
                if (_pmVolHoldT >= 0.4f && (int)((_pmVolHoldT - 0.4f) / 0.1f) != (int)((before - 0.4f) / 0.1f) || (before < 0.4f && _pmVolHoldT >= 0.4f))
                    PmVolStep(_pmVolHoldDir);
            }
        }
    }

    private static readonly int DevPauseVolDir = Environment.GetEnvironmentVariable("FNF_DEV_PAUSE_VOL_DIR") == "-1" ? -1 : 1;

    private void PmVolStep(int dir)
    {
        if (dir > 0) _pmVolPopPlus = 0f; else _pmVolPopMinus = 0f;
        if ((dir > 0 && Audio.MusicVolume >= 0.999f) || (dir < 0 && Audio.MusicVolume <= 0.001f))
        {   // already at the limit: still give feedback
            Audio.PlaySound(dir > 0 ? "soundtray/VolMAX" : "soundtray/Voldown", 0.3f);
            _pmVolumeFlash = 0f;
            return;
        }
        ChangeVolumeV(dir * 0.1f);
        Console.WriteLine($"[PAUSE] volume -> {Audio.MusicVolume:0.0}");
    }

    private void DrawVolumeWidgetV(SpriteBatch sb)
    {
        float k = QuartOut((_pmT - 0.8f) / 1.8f); // fades/slides in after the metadata (startDelay 0.8, 1.8 s quartOut)
        if (k <= 0f) return;
        int dy = (int)MathF.Round(5f * k) - 5;
        float vol = Audio.MusicVolume;
        int level = (int)Math.Round(vol * 10f);
        bool flash = _pmVolumeFlash < 0.35f;
        // menu-entry alpha rule: the selected row is opaque, the others 0.6
        float rowA = _pmVolFocus ? 1f : 0.6f;
        if (_pmVolFocus) DrawVolFocusV(sb, dy, k);

        // label: VCR 32 (FlxText size 32 like the metadata), right-aligned before the '-' button
        var f32 = Assets.GetFont(28);
        if (f32 != null)
        {
            string label = $"Volume: {(int)Math.Round(vol * 100f)}%";
            var size = f32.MeasureString(label);
            var col = flash ? new Color(255, 255, 120) : Color.White;
            f32.DrawText(sb, label, new Vector2(MathF.Round(PmVolMinusRect.X - 14 - size.X), PmVolTop + dy + 11), new Color(col, k * rowA));
        }

        DrawVolButtonV(sb, PmVolMinusRect, '-', _pmVolHover == -1 || _pmVolHoldDir < 0 || (_pmVolFocus && _pmVolSub < 0), _pmVolPopMinus, dy, k);
        DrawVolButtonV(sb, PmVolPlusRect, '+', _pmVolHover == 1 || _pmVolHoldDir > 0 || (_pmVolFocus && _pmVolSub > 0), _pmVolPopPlus, dy, k);

        // bars (FunkinSoundTray look: rising bars, lit up to the current level)
        var bar = PmVolBarRect;
        for (int i = 0; i < 10; i++)
        {
            int h = 16 + (int)MathF.Round((PmVolBtn - 16) * i / 9f);
            var r = new Rectangle(bar.X + i * (PmVolSegW + PmVolSegGap), bar.Bottom - h + dy, PmVolSegW, h);
            bool on = i < level;
            sb.Draw(Assets.Pixel, new Rectangle(r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4), new Color(Color.Black, 0.6f * k));
            var c = on ? (flash ? new Color(255, 255, 120) : Color.White) : new Color(90, 90, 90);
            sb.Draw(Assets.Pixel, r, new Color(c, (on ? rowA : 0.6f * rowA) * k));
        }
        DrawVolPromptsV(sb, dy, k);
    }

    /// <summary>Selected-row marker: a yellow underline-bracket around the whole row (label + buttons + bars) that pops in.</summary>
    private void DrawVolFocusV(SpriteBatch sb, int dy, float k)
    {
        var f32 = Assets.GetFont(28);
        float labelW = f32 != null ? f32.MeasureString($"Volume: {(int)Math.Round(Audio.MusicVolume * 100f)}%").X : 200f;
        int left = (int)MathF.Round(PmVolMinusRect.X - 14 - labelW) - 12, right = PmVolRight + 8;
        float pop = QuartOut(_pmVolFocusT / 0.25f);
        int top = PmVolTop - 8 + dy, h = PmVolBtn + 16;
        var yel = new Color(new Color(255, 230, 60), k * (0.6f + 0.4f * pop));
        sb.Draw(Assets.Pixel, new Rectangle(left, top, right - left, h), new Color(Color.Black, 0.45f * k));
        int th = 4, arm = 18;
        // corner brackets
        foreach (var (x, y, w, hh) in new[] { (left, top, arm, th), (left, top, th, arm), (right - arm, top, arm, th), (right - th, top, th, arm),
                                             (left, top + h - th, arm, th), (left, top + h - arm, th, arm), (right - arm, top + h - th, arm, th), (right - th, top + h - arm, th, arm) })
            sb.Draw(Assets.Pixel, new Rectangle(x, y, w, hh), yel);
    }

    /// <summary>Controller / keyboard hints for the Volume row (ButtonPrompts glyphs switch with the last used device).</summary>
    private void DrawVolPromptsV(SpriteBatch sb, int dy, float k)
    {
        var items = _pmVolFocus
            ? new[] { ButtonPrompts.LeftRight("VOLUME"), ButtonPrompts.Confirm("PRESS"), ButtonPrompts.UpDown("MENU") }
            : ButtonPrompts.UseController(Input)
                ? new[] { new ButtonPrompts.Prompt(Buttons.LeftShoulder, "LB", "VOL -"), new ButtonPrompts.Prompt(Buttons.RightShoulder, "RB", "VOL +") }
                : null;
        if (items == null) return;
        ButtonPrompts.DrawBar(sb, Assets, Input, items, PmVolRight, PmVolTop - 46 + dy, 1f, 28, k * (_pmVolFocus ? 1f : 0.7f), strip: true);
    }

    private void DrawVolButtonV(SpriteBatch sb, Rectangle r, char glyph, bool focus, float pop, int dy, float k)
    {
        float scale = 1f + 0.12f * (1f - QuartOut(pop)); // press pop
        float a = (focus ? 1f : 0.6f) * k;             // menu-entry alpha rule: selected 1, others 0.6
        var c = new Vector2(r.Center.X, r.Center.Y + dy);
        int w = (int)MathF.Round(r.Width * scale), h = (int)MathF.Round(r.Height * scale);
        var rr = new Rectangle((int)MathF.Round(c.X - w / 2f), (int)MathF.Round(c.Y - h / 2f), w, h);
        sb.Draw(Assets.Pixel, rr, new Color(Color.Black, 0.7f * k));
        const int b = 4;
        var bc = new Color(Color.White, a);
        sb.Draw(Assets.Pixel, new Rectangle(rr.X, rr.Y, rr.Width, b), bc);
        sb.Draw(Assets.Pixel, new Rectangle(rr.X, rr.Bottom - b, rr.Width, b), bc);
        sb.Draw(Assets.Pixel, new Rectangle(rr.X, rr.Y, b, rr.Height), bc);
        sb.Draw(Assets.Pixel, new Rectangle(rr.Right - b, rr.Y, b, rr.Height), bc);

        // glyph: thick white '-' / '+' with a black outline (the bold AtlasText '+' is grey and too thin at this size)
        int len = (int)MathF.Round(26 * scale), th = (int)MathF.Round(8 * scale), o = 3;
        var cx = (int)MathF.Round(c.X); var cy = (int)MathF.Round(c.Y);
        var hRect = new Rectangle(cx - len / 2, cy - th / 2, len, th);
        var vRect = new Rectangle(cx - th / 2, cy - len / 2, th, len);
        var outline = new Color(Color.Black, k);
        sb.Draw(Assets.Pixel, new Rectangle(hRect.X - o, hRect.Y - o, hRect.Width + 2 * o, hRect.Height + 2 * o), outline);
        if (glyph == '+') sb.Draw(Assets.Pixel, new Rectangle(vRect.X - o, vRect.Y - o, vRect.Width + 2 * o, vRect.Height + 2 * o), outline);
        var gc = new Color(Color.White, Math.Max(a, 0.85f * k));
        sb.Draw(Assets.Pixel, hRect, gc);
        if (glyph == '+') sb.Draw(Assets.Pixel, vRect, gc);
    }
}
