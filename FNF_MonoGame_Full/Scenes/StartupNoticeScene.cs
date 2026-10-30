using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Startup NOTICE card shown once per launch before the Title screen (all platforms) until the player picks
/// DON'T SHOW AGAIN (saved in save_data.json: <see cref="HighscoreManager.SaveData.HideStartupNotice"/>).
/// FunkTheme look (navy backdrop, cut-corner panel, Funkin bold tab, VCR text, capsule buttons).
/// Controls: pad A = focused button (CONTINUE by default), pad Y = DON'T SHOW AGAIN, D-pad / arrows move the focus,
/// Enter / Space activate, mouse hover + click. Dev/automation runs (any FNF_* variable) skip it; FNF_DEV_NOTICE=1 forces it,
/// FNF_DEV_NOTICE=save applies the saved choice in a dev run (persistence test), 0 skips it.
/// </summary>
public sealed class StartupNoticeScene : Scene
{
    private static readonly string[] Paragraphs =
    {
        "FNF Mono Engine is a fan-made port. I am not the original developer of Friday Night Funkin'.",
        "Friday Night Funkin' was created by The Funkin' Crew (ninjamuffin99, PhantomArcade, evilsk8r and Kawai Sprite). All of its art, music and characters belong to them.",
        "Psych Engine is made by Shadow Mario and its contributors. Mods belong to their own creators.",
        "This port is free and is not affiliated with or endorsed by The Funkin' Crew, Newgrounds, Microsoft or Sony.",
        "Ported by Mohammad Aljafari.",
    };

    private readonly Scene _next;
    private float _time, _sel = 10f, _leave = -1f;
    private int _focus;
    private int _choice = -1;
    private Vector2 _lastMouse = new(-1, -1);

    public StartupNoticeScene(Scene next) { _next = next; }

    public override bool NativeResolution => true;

    /// <summary>True when the notice must be shown before the title screen.</summary>
    public static bool ShouldShow
    {
        get
        {
            if (Environment.GetEnvironmentVariable("FNF_DEV_NOTICE") == "1") return true;
            if (Environment.GetEnvironmentVariable("FNF_DEV_NOTICE") == "0") return false;
            // automated test runs keep booting straight into the title screen (FNF_DEV_NOTICE=save: normal saved-choice logic)
            if (SfxPool.IsDevRun && Environment.GetEnvironmentVariable("FNF_DEV_NOTICE") != "save") return false;
            try { return !HighscoreManager.Data.HideStartupNotice; } catch { return true; }
        }
    }

    /// <summary>The notice (then <paramref name="next"/>) or <paramref name="next"/> itself.</summary>
    public static Scene Gate(Scene next) => ShouldShow ? new StartupNoticeScene(next) : next;

    public override void Load() => Console.WriteLine("[NOTICE] startup notice shown");

    // ------------------------------------------------------------------ layout
    private const float CardX = 150f, CardY = 110f, CardW = 980f, CardH = 370f, TextSize = 18f, LineH = 1.4f, ParaGap = 12f;
    private const float BtnY = 532f, BtnH = 56f;
    private static readonly float[] BtnW = { 300f, 380f };
    private const float BtnGap = 40f;
    private static float BtnX(int i) => FunkUI.W / 2f - (BtnW[0] + BtnGap + BtnW[1]) / 2f + (i == 0 ? 0f : BtnW[0] + BtnGap);
    private static readonly string[] BtnText = { "CONTINUE", "DON'T SHOW AGAIN" };

    private int HitTest(Vector2 p)
    {
        if (p.X < 0) return -1;
        for (int i = 0; i < 2; i++)
            if (p.X >= BtnX(i) && p.X <= BtnX(i) + BtnW[i] && p.Y >= BtnY && p.Y <= BtnY + BtnH) return i;
        return -1;
    }

    // ------------------------------------------------------------------ update
    public override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _time += dt; _sel += dt;
        if (_leave >= 0f)
        {
            _leave += dt;
            if (_leave >= 0.45f) { _leave = -2f; Game.Scenes.ChangeScene(_next ?? new TitleScene()); }
            return;
        }
        if (_leave < -1f || _time < 0.35f) return;

        int focus = _focus;
        if (Input.LeftPressed || Input.UpPressed) focus = 0;
        if (Input.RightPressed || Input.DownPressed) focus = 1;
        var mp = MouseVirtual;
        int hover = HitTest(mp);
        bool moved = (mp - _lastMouse).LengthSquared() > 0.5f && _lastMouse.X >= 0;
        _lastMouse = mp;
        if (hover >= 0 && moved) focus = hover;
        if (focus != _focus) { _focus = focus; _sel = 0f; try { Audio.PlaySound("scrollMenu", 0.4f); } catch { } }

        int pick = -1;
        if (Input.IsGamePadPressed(Buttons.Y)) pick = 1;
        else if (Input.MouseLeftPressed && hover >= 0) pick = hover;
        else if (Input.ConfirmPressed) pick = _focus;
        if (pick >= 0) Choose(pick);
    }

    private void Choose(int pick)
    {
        _choice = pick; _focus = pick; _sel = 0f; _leave = 0f;
        try { Audio.PlaySound("confirmMenu"); } catch { }
        Input.Rumble(0.2f, 0.1f);
        if (pick == 1)
        {
            try { HighscoreManager.Data.HideStartupNotice = true; HighscoreManager.SavePreferences(); }
            catch (Exception e) { Console.WriteLine("[NOTICE] save failed: " + e.Message); }
        }
        Console.WriteLine("[NOTICE] " + (pick == 1 ? "don't show again" : "continue"));
    }

    // ------------------------------------------------------------------ draw
    public override void Draw(SpriteBatch sb)
    {
        FunkUI.Begin(sb);
        FunkTheme.Backdrop(_time);
        float intro = FunkUI.EaseOutBack(FunkUI.Clamp01(_time / 0.35f), 1.4f);
        float outT = _leave >= 0f ? FunkUI.Clamp01(_leave / 0.45f) : _leave < -1f ? 1f : 0f;
        float alpha = 1f - outT;
        float dy = (1f - intro) * 60f + FunkUI.EaseInBack(outT) * 40f;

        // card
        FunkTheme.Panel(CardX, CardY + dy, CardW, CardH, FunkTheme.Navy, alpha);
        FunkTheme.Band(CardX, CardY + dy + CardH - 10f, 360f, 10f, -10f, FunkTheme.A(FunkTheme.Yellow, alpha));
        FunkTheme.Band(CardX + 350f, CardY + dy + CardH - 10f, 120f, 10f, -10f, FunkTheme.A(FunkTheme.Orange, alpha));
        FunkTheme.Tab("NOTICE", CardX + 28f, CardY + dy - 26f, FunkTheme.Yellow, _time, 52f, alpha);

        float y = CardY + dy + 50f, x = CardX + 44f, w = CardW - 88f;
        for (int p = 0; p < Paragraphs.Length; p++)
        {
            bool last = p == Paragraphs.Length - 1;
            var col = FunkTheme.A(last ? FunkTheme.Yellow : FunkTheme.Text, alpha);
            foreach (var line in FunkUI.Wrap(Paragraphs[p], w, TextSize))
            {
                FunkUI.Text(line, x, y, TextSize, col);
                y += TextSize * LineH;
            }
            y += ParaGap;
        }

        // buttons
        bool pad = ButtonPrompts.UseController(Input);
        for (int i = 0; i < 2; i++)
        {
            bool f = i == _focus;
            float pop = f ? MathF.Max(0f, 1f - _sel / 0.18f) : 0f;
            float bx = BtnX(i), by = BtnY + dy * 1.4f;
            float s = 1f + (f ? 0.05f + 0.06f * pop : 0f);
            bool flash = _choice == i && ((int)(_leave / 0.06f)) % 2 == 1;
            FunkUI.Push(bx + BtnW[i] / 2f, by + BtnH / 2f, 0f, s, s);
            FunkTheme.Panel(bx, by, BtnW[i], BtnH, f ? (flash ? Color.White : FunkTheme.Yellow) : FunkTheme.Navy, alpha, 14f, FunkTheme.Outline, f ? FunkTheme.Shadow + 2f : FunkTheme.Shadow);
            var tc = FunkTheme.A(f ? FunkTheme.Ink : FunkTheme.Text, alpha);
            const float gs = 32f, ts = 20f;
            float tw = FunkUI.MeasureVcr(BtnText[i], ts);
            float total = (pad ? gs + 12f : 0f) + tw, left = bx + BtnW[i] / 2f - total / 2f;
            if (pad) { FunkUI.Glyph(true, i == 0 ? Buttons.A : Buttons.Y, null, left, by + (BtnH - gs) / 2f, gs, alpha); left += gs + 12f; }
            FunkUI.Text(BtnText[i], left, by + BtnH / 2f - ts * 0.5f, ts, tc);
            FunkUI.Pop();
        }

        FunkTheme.BottomBar(Input, Bar);
        sb.End();
    }

    private static readonly ButtonPrompts.Prompt[] Bar =
    {
        ButtonPrompts.LeftRight("CHOOSE"), ButtonPrompts.Confirm("OK"),
    };
}
