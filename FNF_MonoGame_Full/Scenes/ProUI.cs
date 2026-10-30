using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Facade used by the Psych Online screens (lobby, song select, room mod browser, front pages, online results). Every
/// call is drawn with the in-house Funkin theme (<see cref="FunkTheme"/>): cut-corner outlined panels with one hard
/// shadow, slanted colour bands, bold Funkin headings, VCR body text, Freeplay palette. Layout constants stay on the
/// 8 px grid so all screens share the same spacing.
/// </summary>
public static class ProUI
{
    public const float M = 48f, G = 24f, BarH = FunkTheme.BottomH;   // margin, gutter, bottom bar height

    public static readonly Color Bg = FunkTheme.Night, Panel = FunkTheme.Navy, PanelHi = FunkTheme.NavyHi, PanelLo = FunkTheme.NavyLo,
        Edge = new(0, 0, 0, 255), Divider = new(255, 255, 255, 40),
        Fg = FunkTheme.Text, Fg2 = FunkTheme.Text2, Fg3 = FunkTheme.Text3,
        Accent = FunkTheme.Yellow, AccentInk = FunkTheme.Ink,
        Bf = FunkTheme.Cyan, Opp = FunkTheme.Purple, Good = FunkTheme.Green, Bad = FunkTheme.Red, Warn = FunkTheme.Orange;

    private static readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    public static float Time => (float)_clock.Elapsed.TotalSeconds;   // heading letter animation clock

    public static Color A(Color c, float a) => new Color(c, Math.Clamp(a, 0f, 1f) * (c.A / 255f));

    // ------------------------------------------------------------------ text
    public static float Text(string s, float x, float y, float size, Color c, float align = 0f) => FunkUI.Text(s, x, y, Math.Max(size, 14f), c, align);
    public static float Measure(string s, float size) => FunkUI.MeasureVcr(s, Math.Max(size, 14f));

    public static float TextFit(string s, float x, float y, float size, Color c, float maxW, float align = 0f)
        => FunkUI.Text(FunkMenu.Ellipsize(s ?? "", Math.Max(size, 14f), maxW), x, y, Math.Max(size, 14f), c, align);

    /// <summary>Small upper-case label (panel captions) - VCR 14, light.</summary>
    public static float Caption(string s, float x, float y, Color? c = null, float size = 14f, float align = 0f)
    {
        if (string.IsNullOrEmpty(s)) return 0f;
        return FunkUI.Text(s.ToUpperInvariant(), x, y, Math.Max(size, 14f), c ?? Fg2, align);
    }

    /// <summary>Heading in the Funkin bold alphabet (animated letters).</summary>
    public static float Title(string s, float x, float y, float scale = 0.6f, float time = 0f, float align = 0f)
        => FunkTheme.Heading(s, x, y, scale, time > 0 ? time : Time, align);

    // ------------------------------------------------------------------ shapes
    public static void Rect(float x, float y, float w, float h, Color c) => FunkUI.Fill(x, y, w, h, c);

    public static void Outline(float x, float y, float w, float h, Color c, float t = 1f)
    {
        FunkUI.Fill(x, y, w, t, c); FunkUI.Fill(x, y + h - t, w, t, c);
        FunkUI.Fill(x, y + t, t, h - 2 * t, c); FunkUI.Fill(x + w - t, y + t, t, h - 2 * t, c);
    }

    /// <summary>Theme panel: cut corners, black outline, hard shadow.</summary>
    public static void PanelBox(float x, float y, float w, float h, float alpha = 0.94f, Color? fill = null)
        => FunkTheme.Panel(x, y, w, h, fill ?? Panel, Math.Min(1f, alpha / 0.94f));

    /// <summary>Panel with a bold heading tab on its top edge. Returns the content top y.</summary>
    public static float Card(float x, float y, float w, float h, string caption, string right = null, Color? rightColor = null, float alpha = 0.94f)
    {
        PanelBox(x, y, w, h, alpha);
        if (string.IsNullOrEmpty(caption)) return y + 16f;
        FunkTheme.Tab(caption.ToUpperInvariant(), x + 10f, y + 8f, Accent, Time, 28f);
        if (!string.IsNullOrEmpty(right)) FunkTheme.Stamp(right, x + w - 14f, y + 10f, rightColor ?? Fg2, 1f, 14f);
        return y + 48f;
    }

    /// <summary>Controller focus: yellow cut-corner frame around the rectangle.</summary>
    public static void Focus(float x, float y, float w, float h, float alpha = 1f) => FunkTheme.FocusFrame(x, y, w, h, alpha);

    /// <summary>Status stamp (slanted band). Returns its width.</summary>
    public static float Chip(string s, float x, float y, Color c, float align = 0f, bool solid = false) => FunkTheme.Stamp(s, x, y, c, align, 14f);

    /// <summary>Capsule button (focus = yellow fill + pop). Optional icon texture on the left.</summary>
    public static void Button(float x, float y, float w, float h, string label, bool focused, bool enabled = true, Microsoft.Xna.Framework.Graphics.Texture2D icon = null, float iconSize = 22f, bool active = false)
    {
        FunkTheme.Button(x, y, w, h, icon == null ? label : "", focused, enabled, null, active, focused ? FunkTheme.Bop : 0f);
        if (icon != null)
        {
            float tw = Measure(label, 18f), cx = x + w / 2f, iw = iconSize * icon.Width / (float)Math.Max(1, icon.Height), total = iw + 10f + tw, left = cx - total / 2f;
            FunkUI.Quad(icon, left + iw / 2f, y + h / 2f, iw, iconSize, 0f, focused ? A(AccentInk, 0.95f) : A(Color.White, enabled ? 0.9f : 0.4f));
            FunkUI.Text(label, left + iw + 10f, y + h / 2f - 9f, 18f, focused ? AccentInk : enabled ? Fg : Fg3);
        }
    }

    public static void Switch(float x, float y, bool on, bool enabled = true) => FunkTheme.Switch(x, y, on, enabled);

    public static void Progress(float x, float y, float w, float p, float h = 6f, Color? c = null) => FunkTheme.Progress(x, y, w, p, Math.Max(h, 10f), c);

    /// <summary>Signal bars for a ping (4 outlined bars).</summary>
    public static void Signal(float x, float y, int ping)
    {
        int bars = ping <= 0 ? 0 : ping < 80 ? 4 : ping < 150 ? 3 : ping < 250 ? 2 : 1;
        var c = PingColor(ping);
        for (int i = 0; i < 4; i++)
        {
            float h = 6 + i * 4f;
            FunkTheme.Box(x + i * 6f - 1f, y + 18f - h - 1f, 6f, h + 2f, FunkTheme.Ink);
            FunkTheme.Box(x + i * 6f, y + 18f - h, 4f, h, i < bars ? c : PanelHi);
        }
    }

    public static Color PingColor(int ping) => ping <= 0 ? Fg3 : ping < 120 ? Good : ping < 250 ? Warn : Bad;

    // ------------------------------------------------------------------ screen furniture
    public static void Dim(float a = 0.72f) => FunkTheme.Box(0, 0, FunkUI.W, FunkUI.H, A(Bg, a));

    /// <summary>Top bar: black strip, bold heading, yellow/orange slanted bands, right info stamp.</summary>
    public static void Header(string title, string kicker = null, string right = null, Color? rightColor = null, float time = 0f)
    {
        FunkTheme.TopBar(title, time > 0 ? time : Time, kicker);
        if (!string.IsNullOrEmpty(right)) FunkTheme.Stamp(right, FunkUI.W - M, 20f, rightColor ?? Fg2, 1f, 14f);
    }

    /// <summary>Bottom bar (same on every online screen).</summary>
    public static void HintBar(InputManager input, IList<ButtonPrompts.Prompt> items, string left = null) => FunkTheme.BottomBar(input, items, left);

    public static void Toast(string s, float y, float alpha, Color? accent = null) => FunkTheme.Toast(s, y, alpha, accent);

    /// <summary>Menu backdrop for the non-lobby pages.</summary>
    public static void Backdrop(Microsoft.Xna.Framework.Graphics.Texture2D desat) => FunkTheme.Backdrop(Time, desat);

    public static Microsoft.Xna.Framework.Graphics.Texture2D Icon(string name) => FunkUI.Tex("images/psychonline/" + name + ".png");
}
