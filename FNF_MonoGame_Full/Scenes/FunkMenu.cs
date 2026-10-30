using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Reusable hand-made FNF list screen on top of <see cref="FunkUI"/> (same look as the main menu / multiplayer menus):
/// tinted menuDesat art + boiling marker doodles, tilted bold title, paper strips per row (selected strip = flat accent
/// colour, slapped/wiggled), checkbox / slider / choice values on black tape, section headers on masking tape, help text on
/// a taped notebook note and the black prompt tape. Long lists scroll (camera follows the selection, rows fade at the
/// list edges, scroll doodle-arrows). Logical layout 1280x720 inside the TV safe area; use with NativeResolution scenes or
/// plain 1280x720 scenes (FunkUI.Begin scales by the target width).
/// Used by Options, Psych Online and the GameBanana mod browser.
/// </summary>
public sealed class FunkMenu
{
    public enum Kind { Button, Toggle, Slider, Choice, Header, Info, Cells }

    public sealed class Row
    {
        public string Label, Value, Desc, Art, Badge;
        public Kind Kind = Kind.Button;
        public bool On, Disabled;
        public float Frac = -1f;          // slider fill 0..1
        public Color? Color, BadgeColor;
        public object Tag;
        /// <summary>Kind.Cells: binding cells drawn as keycaps (Pad == null) or Xbox glyphs (+ name).</summary>
        public List<(string Text, Microsoft.Xna.Framework.Input.Buttons? Pad)> Cells;
        public int CellSel = -1; public bool CellWaiting; public float CellW = 150f;
        public bool Selectable => Kind != Kind.Header && Kind != Kind.Info;
    }

    /// <summary>Draw with the in-house Funkin theme (FunkTheme: cut-corner outlined capsules, slanted bands, no tilt /
    /// tape / doodles) instead of the hand-made paper look. Same layout, input and hit-testing.</summary>
    public bool Themed;

    // layout
    public float ListTop = 168f, ListBottom = 572f, RowH = 50f, RowGap = 13f, RowW = 860f, CenterX = 640f;
    public float Spacing => RowH + RowGap;

    // animation state
    public int Sel;
    private object _pageKey;
    public float PageTime { get; private set; }
    public float SelTime { get; private set; } = 10f;
    private int _lastSel = -1;
    private readonly Dictionary<int, float> _hov = new();
    private readonly Dictionary<int, float> _toggleAt = new();
    private float _scroll, _scrollTarget;
    private float _time;
    private Vector3 _tintV = new(-1f, 0f, 0f);
    public Color Tint { get; private set; } = FunkUI.Purple;

    /// <summary>Call every frame with the current page identity (any object/enum). A new page replays the intro.</summary>
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private double _lastClock = -1;

    /// <summary>Call every frame (usually from Draw). The animation clock is the real elapsed time between calls
    /// (clamped to 0.1 s), so intros run at the same speed when the game renders below 60 FPS.</summary>
    public void Update(object pageKey, IList<Row> rows, Color accent, float dtIgnored)
    {
        double now = _clock.Elapsed.TotalSeconds;
        float dt = _lastClock < 0 ? 1f / 60f : (float)Math.Clamp(now - _lastClock, 0.0, 0.1);
        _lastClock = now;
        _time += dt;
        if (!Equals(pageKey, _pageKey))
        {
            _pageKey = pageKey; PageTime = 0f; SelTime = 10f; _lastSel = Sel; _hov.Clear();
            _scroll = _scrollTarget = TargetScroll(rows);
        }
        PageTime += dt; SelTime += dt;
        if (Sel != _lastSel) { _lastSel = Sel; SelTime = 0f; }
        if (rows != null)
            for (int i = 0; i < rows.Count; i++)
            {
                _hov.TryGetValue(i, out float h);
                _hov[i] = FunkUI.Approach(h, i == Sel ? 1f : 0f, 18f, dt);
            }
        _scrollTarget = TargetScroll(rows);
        _scroll = FunkUI.Approach(_scroll, _scrollTarget, 14f, dt);
        var a = accent.ToVector3();
        if (_tintV.X < 0f) _tintV = a;
        _tintV += (a - _tintV) * (1f - MathF.Exp(-10f * dt));
        Tint = new Color(_tintV);
    }

    public void NotifyToggled(int i) => _toggleAt[i] = _time;

    private float ContentHeight(IList<Row> rows) => rows == null || rows.Count == 0 ? 0f : rows.Count * Spacing - RowGap;

    private float TargetScroll(IList<Row> rows)
    {
        float view = ListBottom - ListTop, content = ContentHeight(rows);
        if (content <= view) return -(view - content) * 0f;   // short lists stay at the top
        float selY = Math.Clamp(Sel, 0, rows.Count - 1) * Spacing + RowH / 2f;
        return Math.Clamp(selY - view * 0.45f, 0f, content - view);
    }

    public float RowCenterY(int i) => ListTop + i * Spacing + RowH / 2f - _scroll;

    /// <summary>Move the selection by <paramref name="dir"/>, skipping headers/info rows. Returns true when it moved.</summary>
    public bool Move(IList<Row> rows, int dir)
    {
        if (rows == null || rows.Count == 0) return false;
        int n = rows.Count, s = Math.Clamp(Sel, 0, n - 1);
        for (int k = 0; k < n; k++)
        {
            s = (s + dir + n) % n;
            if (rows[s].Selectable) { Sel = s; return true; }
        }
        return false;
    }

    /// <summary>Make sure the selection sits on a selectable row.</summary>
    public void FixSel(IList<Row> rows)
    {
        if (rows == null || rows.Count == 0) { Sel = 0; return; }
        Sel = Math.Clamp(Sel, 0, rows.Count - 1);
        if (!rows[Sel].Selectable) Move(rows, 1);
    }

    /// <summary>Row under the mouse (logical coords), -1 if none.</summary>
    public int Hit(Vector2 p, IList<Row> rows)
    {
        if (rows == null || p.X < 0) return -1;
        if (p.Y < ListTop - 6f || p.Y > ListBottom + 6f) return -1;
        for (int i = 0; i < rows.Count; i++)
        {
            if (!rows[i].Selectable) continue;
            float cy = RowCenterY(i);
            if (MathF.Abs(p.Y - cy) <= RowH / 2f + RowGap / 2f && MathF.Abs(p.X - CenterX) <= RowW / 2f) return i;
        }
        return -1;
    }

    // ------------------------------------------------------------------------------------------------ drawing
    private static readonly FunkUI.Doodle[] Doodles =
    {
        new("@d_star", 84f, 196f, 44f, Color.White, -12f), new("@d_note", 1210f, 214f, 56f, FunkUI.Ink, 10f),
        new("@d_squiggle", 1176f, 520f, 80f, Color.White, -6f), new("@d_spiral", 72f, 452f, 46f, Color.White, 0f),
        new("@d_heart", 1214f, 372f, 40f, FunkUI.Pink, -14f), new("@d_zigzag", 92f, 330f, 64f, FunkUI.Ink, 4f),
        new("@d_bang", 1150f, 120f, 40f, FunkUI.Ink, 8f), new("@d_star", 700f, 26f, 28f, FunkUI.Ink, 20f),
    };

    public void DrawBackground(Texture2D desat)
    {
        if (Themed) { FunkTheme.Backdrop(_time); return; }
        float bop = FunkUI.Bop;
        FunkUI.Fill(0, 0, FunkUI.W, FunkUI.H, Tint);
        FunkUI.Cover(desat, 1.08f + 0.012f * bop, Vector2.Zero, Tint);
        FunkUI.Push(640f, 380f, FunkUI.Rad(-4f));
        FunkUI.Fill(-200f, 300f, 1700f, 160f, FunkUI.A(Color.Black, 0.18f));
        FunkUI.Pop();
        FunkUI.DrawDoodles(Doodles, _time, bop, FunkUI.Clamp01(PageTime / 0.3f));
    }

    /// <summary>Dim veil (for pages drawn over another scene, e.g. the lobby).</summary>
    public void DrawVeil(float alpha = 0.62f) => FunkUI.Fill(0, 0, FunkUI.W, FunkUI.H, FunkUI.A(Color.Black, alpha));

    /// <summary>Bold title top-left (tilted, bops) + optional paper luggage tag top-right.</summary>
    public void DrawTitle(string title, string tag = null, Color? tagColor = null, string sub = null)
    {
        if (Themed)
        {
            FunkTheme.TopBar(title, _time, sub, FunkUI.Clamp01(PageTime / 0.25f));
            if (!string.IsNullOrEmpty(tag)) FunkTheme.Stamp(tag, FunkUI.W - FunkTheme.M, 20f, tagColor ?? FunkTheme.Text2, 1f, 15f);
            return;
        }
        float ia = FunkUI.EaseOutBack(FunkUI.Clamp01(PageTime / 0.3f), 1.8f);
        float y = 34f - (1f - ia) * 120f;
        float sc = 0.85f, tw = FunkUI.BoldWidth(title, sc);
        float maxW = tag != null ? 760f : 1100f;
        if (tw > maxW) { sc *= maxW / tw; tw = maxW; }
        float cx = FunkUI.SafeX + tw / 2f, cy = y + FunkUI.BoldHeight(sc) / 2f;
        float b = 1f + 0.03f * FunkUI.Bop;
        FunkUI.Push(cx, cy, FunkUI.Rad(-2.5f), b, b);
        FunkUI.Bold(title, FunkUI.SafeX, y, sc, 0f, _time);
        FunkUI.Pop();
        if (!string.IsNullOrEmpty(sub))
        {
            float sw = FunkUI.MeasureVcr(sub, 17f) + 28f, sx = FunkUI.SafeX + 10f, syy = y + FunkUI.BoldHeight(sc) + 6f;
            FunkUI.Push(sx + sw / 2f, syy + 13f, FunkUI.Rad(-1.2f));
            FunkUI.Fill(sx, syy, sw, 27f, FunkUI.Ink);
            FunkUI.Text(sub, sx + 14f, syy + 5f, 17f, Color.White);
            FunkUI.Pop();
        }
        if (!string.IsNullOrEmpty(tag))
        {
            float w = FunkUI.MeasureVcr(tag, 19f) + 36f, tx = FunkUI.W - FunkUI.SafeX - w / 2f, ty = 62f - (1f - ia) * 120f;
            FunkUI.Push(tx, ty, FunkUI.Rad(3f) + FunkUI.Wiggle(PageTime - 0.2f, 22f, 6f) * 0.06f);
            FunkUI.ShadowBlock(tx - w / 2f, ty - 19f, w, 38f, tagColor ?? FunkUI.Paper, 4f, 5f, 6f);
            FunkUI.Text(tag, tx, ty - 10f, 19f, FunkUI.Ink, 0.5f);
            FunkUI.Pop();
        }
    }

    public void DrawRows(IList<Row> rows, Color accent)
    {
        if (rows == null) return;
        for (int i = 0; i < rows.Count; i++) if (i != Sel) DrawRow(rows, i, accent);
        if (Sel >= 0 && Sel < rows.Count) DrawRow(rows, Sel, accent);
        // scroll hints (hand-drawn arrows) when there is more above/below
        float view = ListBottom - ListTop, content = ContentHeight(rows);
        if (content > view + 1f && Themed)
        {   // slim scrollbar on the right of the list
            float sx = CenterX + RowW / 2f + 22f, frac = view / content;
            FunkTheme.Box(sx - 1f, ListTop - 1f, 8f, view + 2f, FunkTheme.Ink);
            FunkTheme.Box(sx, ListTop, 6f, view, FunkTheme.NavyLo);
            FunkTheme.Box(sx, ListTop + view * Math.Clamp(_scroll / content, 0f, 1f - frac), 6f, view * frac, FunkTheme.Yellow);
        }
        else if (content > view + 1f)
        {
            float bob = MathF.Sin(_time * 6f) * 4f;
            if (_scroll > 4f) FunkUI.Sticker("@arrow", CenterX + RowW / 2f + 36f, ListTop + 8f - bob, 34f, -MathHelper.PiOver2);
            if (_scroll < content - view - 4f) FunkUI.Sticker("@arrow", CenterX + RowW / 2f + 36f, ListBottom - 8f + bob, 34f, MathHelper.PiOver2);
        }
        if (rows.Count == 0) return;
    }

    private float EdgeAlpha(float cy)
    {
        if (Themed)
        {   // fade INSIDE the list area so scrolled rows never reach the title band / tabs above it
            float t0 = ListTop + RowH * 0.5f, b0 = ListBottom - RowH * 0.5f;
            if (cy < t0) return FunkUI.Clamp01(1f - (t0 - cy) / (RowH * 0.55f));
            if (cy > b0) return FunkUI.Clamp01(1f - (cy - b0) / (RowH * 0.55f));
            return 1f;
        }
        float top = ListTop + RowH * 0.25f, bot = ListBottom - RowH * 0.25f;
        if (cy < top) return FunkUI.Clamp01(1f - (top - cy) / (RowH * 0.9f));
        if (cy > bot) return FunkUI.Clamp01(1f - (cy - bot) / (RowH * 0.9f));
        return 1f;
    }

    private static float Tilt(int i) => FunkUI.Rad((i % 2 == 0 ? -0.6f : 0.5f) + (FunkUI.Hash(i * 5 + 2) - 0.5f) * 0.4f);
    private static float Jitter(int i) => (FunkUI.Hash(i * 11 + 7) - 0.5f) * 18f;

    private void DrawRow(IList<Row> rows, int i, Color accent)
    {
        var r = rows[i];
        float cy0 = RowCenterY(i);
        float ea = EdgeAlpha(cy0);
        if (ea <= 0.01f) return;
        float intro = FunkUI.EaseOutBack(FunkUI.Clamp01((PageTime - 0.03f * Math.Min(i, 10)) / 0.26f), 1.8f);
        if (intro <= 0f) return;
        _hov.TryGetValue(i, out float h);
        bool sel = i == Sel && r.Selectable;
        float bop = FunkUI.Bop;
        if (Themed) { DrawRowThemed(r, i, sel, cy0, ea, intro, h, accent); return; }

        if (r.Kind == Kind.Header)
        {
            // section title on masking tape (left aligned, tilted)
            float tw = FunkUI.MeasureVcr(r.Label, 20f) + 44f, tx = CenterX - RowW / 2f + 10f, ty = cy0 + (1f - intro) * 40f;
            FunkUI.Push(tx + tw / 2f, ty, FunkUI.Rad(-2f));
            FunkUI.Quad(FunkUI.Art("@d_tape"), tx + tw / 2f, ty, tw + 30f, 40f, 0f, FunkUI.A(r.Color ?? FunkUI.Yellow, 0.95f * ea));
            FunkUI.Text(r.Label, tx + tw / 2f, ty - 10f, 20f, FunkUI.A(FunkUI.Ink, ea), 0.5f);
            FunkUI.Pop();
            if (!string.IsNullOrEmpty(r.Value)) FunkUI.Text(r.Value, tx + tw + 30f, ty - 8f, 16f, FunkUI.A(Color.White, ea), 0f, 2f);
            return;
        }

        var sq = sel ? FunkUI.Squash(SelTime, 0.14f) : Vector2.One;
        float wig = sel ? FunkUI.Wiggle(SelTime, 26f, 7f) * 0.04f : 0f;
        float cx = CenterX + Jitter(i) + 16f * h, cy = cy0 + (1f - intro) * 60f;
        float s = (0.97f + 0.04f * h) * intro;
        FunkUI.Push(cx, cy, Tilt(i) + wig, s * sq.X, s * sq.Y);
        float x = cx - RowW / 2f, y = cy - RowH / 2f;
        var rc = r.Color ?? accent;
        var fill = r.Kind == Kind.Info ? FunkUI.A(FunkUI.Paper, 0.9f) : sel ? rc : FunkUI.Paper;
        if (r.Disabled && !sel) fill = Color.Lerp(FunkUI.Paper, Color.Gray, 0.45f);
        FunkUI.ShadowBlock(x, y, RowW, RowH, fill, 4f, sel ? 8f : 5f, sel ? 9f : 6f, ea);

        float labelX = x + 22f;
        if (!string.IsNullOrEmpty(r.Art))
        {
            float artH = RowH * (r.Art.StartsWith("icon:") ? 1.5f : 1.1f);
            float artA = FunkUI.Rad(-8f + 6f * FunkUI.Hash(i)) + (sel ? FunkUI.Wiggle(SelTime, 30f, 8f) * 0.15f : 0f);
            FunkUI.Sticker(r.Art, x + 6f, cy, artH * (sel ? 1f + 0.08f * bop : 1f), artA, ea);
            labelX = x + 50f;
        }

        // value part (right side)
        float right = x + RowW - 14f, valueLeft = right;
        switch (r.Kind)
        {
            case Kind.Toggle:
            {
                float bs = 34f, bx = right - bs, by = cy - bs / 2f;
                float since = _toggleAt.TryGetValue(i, out var ta) ? _time - ta : 9f;
                var bsq = FunkUI.Squash(since, 0.3f);
                FunkUI.Push(bx + bs / 2f, cy, FunkUI.Rad(-4f) + FunkUI.Wiggle(since, 28f, 8f) * 0.2f, bsq.X, bsq.Y);
                FunkUI.Block(bx, by, bs, bs, r.On ? FunkUI.Green : Color.White, 4f, Color.Black, ea);
                if (r.On)
                {
                    float k = FunkUI.EaseOutCubic(since / 0.12f);
                    var p0 = new Vector2(bx + 7f, by + 18f); var p1 = new Vector2(bx + 15f, by + 27f); var p2 = new Vector2(bx + 30f, by + 4f);
                    var pts = k < 0.5f ? new[] { p0, Vector2.Lerp(p0, p1, k * 2f) } : new[] { p0, p1, Vector2.Lerp(p1, p2, (k - 0.5f) * 2f) };
                    FunkUI.Marker(pts, 6f, FunkUI.A(FunkUI.Ink, ea), i, 0.8f);
                }
                FunkUI.Pop();
                string st = r.On ? "ON" : "OFF";
                float stw = FunkUI.MeasureVcr(st, 18f);
                FunkUI.Text(st, bx - 12f, cy - 9f, 18f, FunkUI.A(FunkUI.Ink, ea), 1f);
                valueLeft = bx - 20f - stw;
                break;
            }
            case Kind.Slider:
            {
                float vs = 19f, vw = FunkUI.MeasureVcr(r.Value ?? "", vs);
                float tapeW = Math.Max(78f, vw + 24f), tx = right - tapeW;
                FunkUI.Fill(tx, cy - 16f, tapeW, 32f, FunkUI.A(FunkUI.Ink, ea));
                FunkUI.Text(r.Value ?? "", tx + tapeW / 2f, cy - 9.5f, vs, FunkUI.A(Color.White, ea), 0.5f);
                float tw = 170f, lx = tx - 22f - tw;
                FunkUI.Line(lx, cy, lx + tw, cy + 1f, 7f, FunkUI.A(FunkUI.Ink, ea));
                float f = FunkUI.Clamp01(r.Frac);
                if (f > 0f) FunkUI.Line(lx, cy, lx + tw * f, cy + f, 4f, FunkUI.A(sel ? Color.White : rc, ea));
                float kx = lx + tw * f;
                FunkUI.Push(kx, cy, FunkUI.Rad(-6f) + (sel ? FunkUI.Wiggle(SelTime, 30f, 8f) * 0.2f : 0f));
                FunkUI.Block(kx - 9f, cy - 13f, 18f, 26f, sel ? FunkUI.Yellow : Color.White, 3f, Color.Black, ea);
                FunkUI.Pop();
                if (sel)
                {
                    float nudge = MathF.Max(0f, MathF.Sin(_time * 7f)) * 3f;
                    FunkUI.Bold("<", lx - 14f - nudge, cy - FunkUI.BoldHeight(0.4f) / 2f, 0.4f, 1f, _time, ea, FunkUI.Yellow);
                }
                valueLeft = lx - 40f;
                break;
            }
            case Kind.Cells:
            {
                if (r.Cells == null) break;
                float cw = r.CellW, gap = 12f;
                float cx0 = right - r.Cells.Count * cw - (r.Cells.Count - 1) * gap;
                for (int c = 0; c < r.Cells.Count; c++)
                {
                    var (text, pad) = r.Cells[c];
                    float bx = cx0 + c * (cw + gap);
                    bool cs = sel && c == r.CellSel;
                    float lift = cs ? 2f + 3f * bop : 0f;
                    if (cs) FunkUI.Block(bx - 2f, cy - 19f - lift, cw + 4f, 38f, r.CellWaiting ? FunkUI.Pink : FunkUI.Yellow, 3f, Color.Black, ea);
                    else FunkUI.Fill(bx, cy - 17f, cw, 34f, FunkUI.A(FunkUI.Ink, 0.85f * ea));
                    if (cs && r.CellWaiting)
                    {
                        float bl = 0.55f + 0.45f * MathF.Sin(_time * 9f);
                        FunkUI.Text("PRESS...", bx + cw / 2f, cy - 9f - lift, 17f, FunkUI.A(FunkUI.Ink, ea * bl), 0.5f);
                        continue;
                    }
                    Color tc = cs ? FunkUI.Ink : Color.White;
                    if (pad.HasValue)
                    {
                        FunkUI.Glyph(true, pad.Value, "", bx + 6f, cy - 14f - lift, 28f, ea);
                        FunkUI.Text(Ellipsize(text, 16f, cw - 44f), bx + 40f, cy - 8f - lift, 16f, FunkUI.A(tc, ea));
                    }
                    else FunkUI.Text(Ellipsize(text, 18f, cw - 12f), bx + cw / 2f, cy - 9f - lift, 18f, FunkUI.A(tc, ea), 0.5f);
                }
                valueLeft = cx0 - 12f;
                break;
            }
            case Kind.Choice:
            case Kind.Button:
            case Kind.Info:
            {
                if (string.IsNullOrEmpty(r.Value)) break;
                float vs = 19f;
                bool arrows = r.Kind == Kind.Choice;
                float aw = arrows ? 26f : 0f;
                float maxVw = RowW * 0.5f;
                string v = Ellipsize(r.Value, vs, maxVw);
                float vw = FunkUI.MeasureVcr(v, vs);
                float boxW = vw + 26f + 2f * aw, bx = right - boxW;
                FunkUI.Fill(bx, cy - 17f, boxW, 34f, FunkUI.A(FunkUI.Ink, ea));
                FunkUI.Text(v, bx + boxW / 2f, cy - 10f, vs, FunkUI.A(Color.White, ea), 0.5f);
                if (arrows)
                {
                    float nudge = sel ? MathF.Max(0f, MathF.Sin(_time * 7f)) * 3f : 0f;
                    FunkUI.Bold("<", bx + 6f - nudge, cy - FunkUI.BoldHeight(0.4f) / 2f, 0.4f, 0f, _time, ea, sel ? FunkUI.Yellow : Color.White);
                    FunkUI.Bold(">", bx + boxW - 6f + nudge, cy - FunkUI.BoldHeight(0.4f) / 2f, 0.4f, 1f, _time, ea, sel ? FunkUI.Yellow : Color.White);
                }
                valueLeft = bx - 12f;
                break;
            }
        }
        if (!string.IsNullOrEmpty(r.Badge))
        {
            float bw = FunkUI.MeasureVcr(r.Badge, 15f) + 18f, bx = valueLeft - bw;
            FunkUI.Push(bx + bw / 2f, cy, FunkUI.Rad(-4f));
            FunkUI.Block(bx, cy - 12f, bw, 24f, r.BadgeColor ?? FunkUI.Yellow, 3f, Color.Black, ea);
            FunkUI.Text(r.Badge, bx + bw / 2f, cy - 7.5f, 15f, FunkUI.A(FunkUI.Ink, ea), 0.5f);
            FunkUI.Pop();
            valueLeft = bx - 10f;
        }

        // label (Funkin bold alphabet; shrinks to fit before the value)
        float avail = valueLeft - labelX - 8f;
        float ls = RowH >= 50f ? 0.5f : 0.44f;
        float lw = FunkUI.BoldWidth(r.Label, ls);
        if (lw > avail && lw > 0) ls *= Math.Max(0.25f, avail / lw);
        Color? lt = sel ? (Color?)null : FunkUI.Ink;
        if (r.Kind == Kind.Info) lt = FunkUI.Ink;
        FunkUI.Bold(r.Label, labelX, cy - FunkUI.BoldHeight(ls) / 2f, ls, 0f, _time, ea * (r.Disabled && !sel ? 0.6f : 1f), lt);
        FunkUI.Pop();
        if (sel) DrawPointer(cx - RowW / 2f * s - 34f, cy, bop);
    }

    /// <summary>Themed mode: draws the real FNF checkbox (checkboxThingie) - (x, y) = box top-left, size, checked,
    /// seconds since it was toggled (replays the tick animation), alpha. Set by the scene (it owns the atlas).</summary>
    public Action<float, float, float, bool, float, float> CheckboxArt;
    public float Time => _time;
    public float ToggleAge(int i) => _toggleAt.TryGetValue(i, out float t) ? _time - t : 99f;

    /// <summary>Themed row: Freeplay-style capsule (focus = yellow fill, slides out + pops), FNF checkbox art, capsule
    /// slider bars + Freeplay digital numbers, choice values with &lt; &gt; arrows, keybind keycaps / Xbox glyph chips,
    /// section headers as slanted stamps.</summary>
    private void DrawRowThemed(Row r, int i, bool sel, float cy0, float ea, float intro, float h, Color accent)
    {
        float cy = cy0 + (1f - intro) * 30f;
        ea *= FunkUI.Clamp01(intro * 1.5f);
        float left = CenterX - RowW / 2f;
        if (r.Kind == Kind.Header)
        {
            float hw = FunkTheme.Stamp(r.Label, left + 4f, cy - 12f, r.Color ?? FunkTheme.Cyan, 0f, 16f, ea);
            float vwh = string.IsNullOrEmpty(r.Value) ? 0f : FunkTheme.TextW(r.Value, 15f) + 16f;
            FunkTheme.Band(left + hw + 6f, cy - 2f, RowW - hw - 10f - vwh, 4f, 3f, FunkTheme.A(r.Color ?? FunkTheme.Cyan, 0.45f * ea));
            if (!string.IsNullOrEmpty(r.Value)) FunkTheme.Txt(r.Value, left + RowW, cy - 8f, 15f, FunkTheme.A(FunkTheme.Text2, ea), 1f);
            return;
        }
        float pop = sel ? MathF.Exp(-SelTime * 9f) : 0f;
        float x = left + 18f * (1f - h) - 10f * pop, w = RowW - 18f * (1f - h);
        float rh = RowH - 4f, y = cy - rh / 2f;
        float s = sel ? 1f + 0.02f * pop + 0.006f * FunkUI.Bop : 1f;
        if (s != 1f) FunkUI.Push(x, cy, 0f, s, s);
        bool quiet = r.Kind == Kind.Info || r.Disabled || r.Color == FunkUI.Paper;   // BACK / EXIT rows are quieter
        var fill = sel ? FunkTheme.Yellow : quiet ? FunkTheme.NavyLo : FunkTheme.Navy;
        FunkTheme.Panel(x, y, w, rh, fill, ea, 11f, 3f, sel ? 7f : 4f);
        if (!sel && r.Color.HasValue && r.Color != FunkUI.Paper) FunkTheme.Box(x, y + 11f, 5f, rh - 11f, FunkTheme.A(r.Color.Value, ea));
        var ink = sel ? FunkTheme.Ink : quiet ? FunkTheme.Text2 : FunkTheme.Text;
        float labelX = x + 22f;
        if (!string.IsNullOrEmpty(r.Art) && r.Art.StartsWith("icon:"))
        {
            float ib = sel ? 1f + 0.08f * FunkUI.Bop : 1f;
            FunkUI.HealthIcon(r.Art.Substring(5), x + 38f, cy - 2f, rh * 1.25f * ib, 0, 0f, ea);
            labelX = x + 78f;
        }
        float right = x + w - 16f, valueLeft = right;
        var plateText = FunkTheme.Text;
        switch (r.Kind)
        {
            case Kind.Toggle:
            {
                const float bs = 24f;
                float bx = right - bs - 4f, by = cy - bs / 2f + 7f;
                if (CheckboxArt != null) CheckboxArt(bx, by, bs, r.On, ToggleAge(i), ea);
                else FunkTheme.Switch(right - 64f, cy - 12f, r.On, !r.Disabled);
                string st = r.On ? "ON" : "OFF";
                FunkTheme.Txt(st, bx - 12f, cy - 8f, 16f, FunkTheme.A(sel ? FunkTheme.Ink : r.On ? FunkTheme.Green : FunkTheme.Text3, ea), 1f);
                valueLeft = bx - 12f - FunkTheme.TextW("OFF", 16f) - 14f;
                break;
            }
            case Kind.Slider:
            {
                string v = r.Value ?? "";
                const float vw = 96f, dh = 24f;
                FunkTheme.Panel(right - vw, cy - 15f, vw, 30f, FunkTheme.Ink, ea, 7f, 0f, 0f);
                float nw = FunkTheme.NumberValue(v, 0, 0, dh, FunkTheme.ScreenText, ea, false);
                FunkTheme.NumberValue(v, right - vw / 2f - nw / 2f, cy, dh, FunkTheme.ScreenText, ea);
                float tw = 190f, lx = right - vw - 30f - tw;
                SliderBar(lx, cy - 8f, tw, 16f, FunkUI.Clamp01(r.Frac), ea);
                var ac = FunkTheme.A(sel ? FunkTheme.Ink : FunkTheme.Text3, ea);
                FunkTheme.Txt("<", lx - 8f, cy - 9f, 18f, ac, 1f); FunkTheme.Txt(">", right - vw - 10f, cy - 9f, 18f, ac, 1f);
                valueLeft = lx - 30f;
                break;
            }
            case Kind.Cells:
            {
                if (r.Cells == null) break;
                float cw = r.CellW, gap = 10f;
                float cx0 = right - r.Cells.Count * cw - (r.Cells.Count - 1) * gap;
                for (int c = 0; c < r.Cells.Count; c++)
                {
                    var (text, pad) = r.Cells[c];
                    float bx = cx0 + c * (cw + gap);
                    bool cs = sel && c == r.CellSel;
                    FunkTheme.Panel(bx, cy - 15f, cw, 30f, cs ? (r.CellWaiting ? FunkTheme.Pink : Color.White) : FunkTheme.Ink, ea, 7f, cs ? 3f : 0f, 0f);
                    if (cs && r.CellWaiting)
                    {
                        float bl = 0.55f + 0.45f * MathF.Sin(_time * 9f);
                        FunkTheme.Txt("PRESS...", bx + cw / 2f, cy - 8f, 16f, FunkTheme.A(FunkTheme.Ink, ea * bl), 0.5f);
                        continue;
                    }
                    var tc = cs ? FunkTheme.Ink : FunkTheme.Text;
                    if (pad.HasValue)
                    {
                        FunkUI.Glyph(true, pad.Value, "", bx + 6f, cy - 12f, 24f, ea);
                        FunkTheme.Txt(Ellipsize(text, 15f, cw - 40f), bx + 36f, cy - 7.5f, 15f, FunkTheme.A(tc, ea));
                    }
                    else FunkTheme.Txt(Ellipsize(text, 16f, cw - 12f), bx + cw / 2f, cy - 8f, 16f, FunkTheme.A(tc, ea), 0.5f);
                }
                valueLeft = cx0 - 12f;
                break;
            }
            default:
            {
                if (string.IsNullOrEmpty(r.Value)) { if (sel) FunkTheme.Txt(">", right - 4f, cy - 9f, 18f, FunkTheme.A(FunkTheme.Ink, ea), 1f); break; }
                bool arrows = r.Kind == Kind.Choice;
                const float dh = 24f;
                bool numeric = arrows && r.Value.Length > 0 && (char.IsDigit(r.Value[0]) || (r.Value[0] == '-' && r.Value.Length > 1 && char.IsDigit(r.Value[1])));
                string v = numeric ? r.Value : Ellipsize(r.Value, 17f, RowW * 0.42f);
                float vw = numeric ? FunkTheme.NumberValue(v, 0, 0, dh, plateText, ea, false) : FunkTheme.TextW(v, 17f), aw = arrows ? 22f : 0f;
                float boxW = Math.Max(arrows ? 110f : 0f, vw + 28f + 2f * aw), bx = right - boxW;
                FunkTheme.Panel(bx, cy - 15f, boxW, 30f, FunkTheme.Ink, ea, 7f, 0f, 0f);
                if (numeric) FunkTheme.NumberValue(v, bx + boxW / 2f - vw / 2f, cy, dh, FunkTheme.ScreenText, ea);
                else FunkTheme.Txt(v, bx + boxW / 2f, cy - 8.5f, 17f, FunkTheme.A(plateText, ea), 0.5f);
                if (arrows)
                {
                    float nudge = sel ? MathF.Max(0f, MathF.Sin(_time * 7f)) * 2f : 0f;
                    var ac = FunkTheme.A(sel ? FunkTheme.Yellow : FunkTheme.Text3, ea);
                    FunkTheme.Txt("<", bx + 8f - nudge, cy - 9f, 18f, ac);
                    FunkTheme.Txt(">", bx + boxW - 8f + nudge, cy - 9f, 18f, ac, 1f);
                }
                valueLeft = bx - 12f;
                break;
            }
        }
        if (!string.IsNullOrEmpty(r.Badge)) valueLeft -= FunkTheme.Stamp(r.Badge, valueLeft, cy - 12f, r.BadgeColor ?? FunkTheme.Cyan, 1f, 14f, ea) + 4f;
        // label: VCR caps, large (the bold alphabet is reserved for headings); shrinks before it would touch the value
        float avail = valueLeft - labelX - 8f, ls = 20f;
        string label = r.Label ?? "";
        while (ls > 15f && FunkTheme.TextW(label, ls) > avail) ls -= 1f;
        FunkTheme.TxtFit(label, labelX, cy - ls * 0.5f, ls, FunkTheme.A(ink, ea), avail);
        if (s != 1f) FunkUI.Pop();
    }

    /// <summary>Capsule slider bar: black outlined cut-corner track, yellow fill, white notch at the value.</summary>
    public static void SliderBar(float x, float y, float w, float h, float frac, float alpha = 1f)
    {
        FunkTheme.Panel(x, y, w, h, FunkTheme.NavyLo, alpha, h * 0.4f, 3f, 0f);
        frac = Math.Clamp(frac, 0f, 1f);
        if (frac > 0f) FunkTheme.Shape(x, y, Math.Max(h, w * frac), h, h * 0.4f, FunkTheme.A(FunkTheme.Yellow, alpha));
        float nx = x + Math.Max(h, w * frac);
        FunkTheme.Box(nx - 3f, y - 4f, 6f, h + 8f, FunkTheme.A(FunkTheme.Ink, alpha));
        FunkTheme.Box(nx - 1.5f, y - 2.5f, 3f, h + 5f, FunkTheme.A(Color.White, alpha));
    }

    public static string Ellipsize(string s, float size, float maxW)
    {
        if (string.IsNullOrEmpty(s) || FunkUI.MeasureVcr(s, size) <= maxW) return s;
        int lo = 0, hi = s.Length;
        while (lo < hi) { int m = (lo + hi + 1) / 2; if (FunkUI.MeasureVcr(s.Substring(0, m) + "...", size) <= maxW) lo = m; else hi = m - 1; }
        return s.Substring(0, lo) + "...";
    }

    private void DrawPointer(float x, float y, float bop)
    {
        float nudge = MathF.Sin(_time * 8f) * 4f + bop * 6f;
        FunkUI.Sticker("@arrow", x - 22f + nudge, y, 38f, FunkUI.Rad(-6f), 1f);
    }

    /// <summary>Help text of the selected row on a taped notebook note (bottom left, above the prompt tape).</summary>
    public void DrawHelp(string text, float w = 620f)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (Themed)
        {   // description strip above the bottom bar
            var tl = FunkUI.Wrap(text, FunkUI.W - 2 * FunkTheme.M - 40f, 16f);
            if (tl.Count > 2) { tl = tl.GetRange(0, 2); tl[1] = Ellipsize(tl[1] + " ...", 16f, FunkUI.W - 2 * FunkTheme.M - 40f); }
            float th = 14f + tl.Count * 20f, ty0 = FunkUI.H - FunkTheme.BottomH - 14f - th;
            FunkTheme.Panel(FunkTheme.M, ty0, FunkUI.W - 2 * FunkTheme.M, th, FunkTheme.NavyLo, 1f, 9f, 3f, 0f);
            FunkTheme.Box(FunkTheme.M, ty0, 6f, th, FunkTheme.Yellow);
            float yy = ty0 + 7f;
            foreach (var l in tl) { FunkTheme.Txt(l, FunkTheme.M + 22f, yy, 16f, FunkTheme.Text); yy += 20f; }
            return;
        }
        var lines = FunkUI.Wrap(text, w - 56f, 17f);
        if (lines.Count > 2) { lines = lines.GetRange(0, 2); lines[1] = Ellipsize(lines[1] + " ...", 17f, w - 56f); }
        float h = 14f + lines.Count * 21f;
        float cx = FunkUI.SafeX + w / 2f - 6f, cy = Math.Max(ListBottom + 34f, 606f) + h / 2f;
        var sq = FunkUI.Squash(SelTime, 0.08f);
        FunkUI.Push(cx, cy, FunkUI.Rad(-1.4f) + FunkUI.Wiggle(SelTime, 26f, 9f) * 0.02f, sq.X, sq.Y);
        FunkUI.ShadowBlock(cx - w / 2f, cy - h / 2f, w, h, FunkUI.Paper, 3f, 6f, 7f);
        FunkUI.Fill(cx - w / 2f + 24f, cy - h / 2f, 2f, h, new Color(255, 90, 110, 150));
        float ty = cy - h / 2f + 7f;
        foreach (var l in lines) { FunkUI.Text(l, cx - w / 2f + 36f, ty, 17f, FunkUI.Ink); ty += 21f; }
        FunkUI.Tape(cx - w / 2f + 8f, cy - h / 2f, 64f, FunkUI.Rad(-30f), FunkUI.Pink, 0.9f);
        FunkUI.Pop();
    }

    /// <summary>Yellow slapped status strip under the title, left aligned (replaces the subtitle while shown; fades out
    /// with <paramref name="timer"/>, 6 s total).</summary>
    public void DrawToast(string text, float timer, float y = 114f)
    {
        if (timer <= 0f || string.IsNullOrEmpty(text)) return;
        float a = Math.Min(1f, timer) * Math.Min(1f, (6f - timer) * 6f);
        if (Themed) { FunkTheme.Toast(text, FunkTheme.TopH + 16f, a); return; }
        float slap = FunkUI.EaseOutBack(Math.Min(1f, (6f - timer) / 0.18f), 2.4f);
        string t = Ellipsize(text, 18f, 1080f);
        float tw = FunkUI.MeasureVcr(t, 18f) + 40f, tx = FunkUI.SafeX + 4f;
        FunkUI.Push(tx + tw / 2f, y, FunkUI.Rad(-1.2f), slap, slap);
        FunkUI.Block(tx, y - 15f, tw, 30f, FunkUI.Yellow, 4f, Color.Black, a);
        FunkUI.Text(t, tx + 20f, y - 8.5f, 18f, FunkUI.A(FunkUI.Ink, a));
        FunkUI.Pop();
    }

    public void DrawPrompts(InputManager input, List<ButtonPrompts.Prompt> prompts)
    {
        if (Themed) { FunkTheme.BottomBar(input, prompts); return; }
        FunkUI.PromptTape(input, prompts, FunkUI.W - FunkUI.SafeX + 10f, FunkUI.H - 60f, 28f, FunkUI.Clamp01(PageTime / 0.3f));
    }
    private void UnusedPromptTape(InputManager input, List<ButtonPrompts.Prompt> prompts)
        => FunkUI.PromptTape(input, prompts, FunkUI.W - FunkUI.SafeX + 10f, FunkUI.H - 60f, 28f, FunkUI.Clamp01(PageTime / 0.3f));

    /// <summary>Notebook-page text editor (typing / D-pad keyboard).</summary>
    public void DrawEditor(string question, string buffer, string hint, string charHint = null)
    {
        float ia = FunkUI.EaseOutBack(FunkUI.Clamp01(PageTime / 0.3f), 2f);
        const float w = 820f, h = 280f;
        float cx = FunkUI.W / 2f, cy = 370f + (1f - ia) * 300f;
        FunkUI.Push(cx, cy, FunkUI.Rad(-1.5f));
        float x = cx - w / 2f, y = cy - h / 2f;
        FunkUI.ShadowBlock(x, y, w, h, FunkUI.Paper, 5f, 12f, 14f);
        for (int k = 1; k < 9; k++) FunkUI.Fill(x + 12f, y + k * 31f, w - 24f, 2f, new Color(120, 170, 255, 110));
        FunkUI.Fill(x + 48f, y, 2f, h, new Color(255, 90, 110, 150));
        FunkUI.Text(question, x + 66f, y + 24f, 21f, FunkUI.Ink);
        FunkUI.Block(x + 66f, y + 66f, w - 120f, 76f, Color.White, 4f);
        string shown = Ellipsize(buffer ?? "", 34f, w - 170f);
        if (shown != buffer && buffer != null && buffer.Length > 0)
        {   // keep the END of long text visible
            int st = buffer.Length - 1;
            while (st > 0 && FunkUI.MeasureVcr("..." + buffer.Substring(st - 1), 34f) <= w - 170f) st--;
            shown = "..." + buffer.Substring(st);
        }
        float tw = FunkUI.Text(shown, x + 86f, y + 87f, 34f, FunkUI.Ink);
        if (((int)(_time * 2f)) % 2 == 0) FunkUI.Line(x + 92f + tw, y + 78f, x + 90f + tw, y + 130f, 5f, FunkUI.Pink);
        if (!string.IsNullOrEmpty(charHint))
        {
            FunkUI.Push(x + w - 90f, y + 104f, FunkUI.Rad(5f));
            FunkUI.ShadowBlock(x + w - 116f, y + 80f, 52f, 48f, FunkUI.Yellow, 3f, 4f, 5f);
            FunkUI.Text(charHint, x + w - 90f, y + 92f, 26f, FunkUI.Ink, 0.5f);
            FunkUI.Pop();
        }
        FunkUI.Paragraph(hint ?? "", x + 66f, y + 162f, w - 110f, 17f, FunkUI.Ink, 0f, 1.35f);
        FunkUI.Paragraph("Keyboard: just type.  Controller: Up/Down picks a character, Right adds it, X deletes.", x + 66f, y + h - 42f, w - 110f, 16f, new Color(90, 80, 110), 0f, 1.3f);
        FunkUI.Tape(x + 30f, y + 4f, 100f, FunkUI.Rad(-32f), FunkUI.Cyan, 0.9f);
        FunkUI.Tape(x + w - 30f, y + 4f, 100f, FunkUI.Rad(32f), FunkUI.Cyan, 0.9f);
        FunkUI.Pop();
    }

    /// <summary>Loading animation (BF bopping, three notes hopping) with a caption and a detail tape.</summary>
    public void DrawLoading(string caption, string detail, float progress = -1f)
    {
        float cx = FunkUI.W / 2f, cy = 300f, bop = FunkUI.Bop;
        FunkUI.Sticker("bf1", cx, cy, 190f * (1f + 0.08f * bop), FunkUI.Rad(-6f) + MathF.Sin(_time * 5f) * 0.08f, 1f, 1f + 0.06f * bop, 1f - 0.06f * bop);
        for (int k = 0; k < 3; k++)
        {
            float ph = (_time * 3f - k * 0.33f) % 1f; if (ph < 0) ph += 1f;
            float hop = MathF.Max(0f, MathF.Sin(ph * MathF.PI * 2f)) * 18f;
            FunkUI.Sticker("@d_note", cx - 60f + k * 60f, cy + 140f - hop, 42f, FunkUI.Rad(-8f + k * 8f), 1f, 1f, 1f, k % 2 == 0 ? Color.White : FunkUI.Ink);
        }
        FunkUI.Bold(caption + new string('.', 1 + (int)(_time * 3) % 3), cx, cy + 184f, 0.7f, 0.5f, _time);
        if (progress >= 0f) ProgressBar(cx - 260f, cy + 252f, 520f, progress);
        if (!string.IsNullOrEmpty(detail))
        {
            string d = Ellipsize(detail, 18f, 1000f);
            float lw = FunkUI.MeasureVcr(d, 18f) + 40f, ly = cy + (progress >= 0f ? 300f : 262f);
            FunkUI.Push(cx, ly + 15f, FunkUI.Rad(-1.2f));
            FunkUI.Fill(cx - lw / 2f, ly, lw, 30f, FunkUI.Ink);
            FunkUI.Text(d, cx, ly + 6f, 18f, Color.White, 0.5f);
            FunkUI.Pop();
        }
    }

    /// <summary>Chunky hand-made progress bar: black frame, flat fill with marker hatching, percentage on a sticker.</summary>
    public void ProgressBar(float x, float y, float w, float p)
    {
        p = FunkUI.Clamp01(p);
        FunkUI.Push(x + w / 2f, y + 14f, FunkUI.Rad(-1f));
        FunkUI.ShadowBlock(x, y, w, 28f, Color.White, 4f, 5f, 6f);
        if (p > 0f)
        {
            FunkUI.Fill(x, y, w * p, 28f, FunkUI.Green);
            for (float sx = x + 10f; sx < x + w * p - 4f; sx += 22f) FunkUI.Line(sx, y + 24f, Math.Min(sx + 14f, x + w * p - 2f), y + 4f, 3f, FunkUI.A(Color.White, 0.45f), false);
        }
        string t = (int)(p * 100f) + "%";
        FunkUI.Text(t, x + w / 2f, y + 5f, 18f, FunkUI.Ink, 0.5f);
        FunkUI.Pop();
    }
}
