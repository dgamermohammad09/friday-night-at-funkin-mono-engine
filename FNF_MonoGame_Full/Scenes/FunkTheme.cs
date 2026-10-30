using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// "Funkin" in-house UI theme for the online screens, built from V-Slice's own UI language:
///  - shapes: cut-corner panels (top-left + bottom-right corners cut 45 deg, like the Freeplay capsules / pause cards),
///    slanted parallelogram bands (Freeplay / Character Select / Results colour bands), thick clean black outlines and
///    ONE hard drop shadow - flat, no gradients, glass or soft glow;
///  - type: page headings in the Funkin bold alphabet (animated letters), numbers in Freeplay's digital_numbers,
///    body text in VCR (the font V-Slice's UI uses), never tiny grey text (min 14 px, light colours);
///  - real art: Freeplay capsule (mp3 capsule w backing), difficulty sprites (menus/freeplay/freeplay&lt;diff&gt;),
///    album covers, Xbox glyphs from ButtonPrompts, Psych Online icons;
///  - palette: Freeplay yellow #FFD863 / orange, BF cyan #31B0D1, opponent purple #AF66CE, dark navy backgrounds;
///  - grid: 8 px unit, 40 px margins; motion: 102 BPM bop, slide-ins, snappy focus scale + outline colour.
/// Coordinates are FunkUI logical 1280x720 units (crisp at any back-buffer size).
/// </summary>
public static class FunkTheme
{
    public const float U = 8f, M = 40f, Outline = 4f, Shadow = 6f, Cut = 14f, TopH = 64f, BottomH = 52f;

    public static readonly Color Night = new(18, 14, 34), Navy = new(34, 27, 62), NavyHi = new(54, 43, 94), NavyLo = new(26, 21, 48),
        Yellow = new(255, 216, 99), Orange = new(255, 154, 61), Cyan = new(49, 176, 209), Purple = new(175, 102, 206),
        Pink = new(255, 120, 190), Green = new(92, 214, 118), Red = new(238, 78, 86), Text = new(255, 250, 238),
        Text2 = new(218, 210, 240), Text3 = new(178, 168, 214), Ink = new(10, 8, 16), Screen = new(8, 14, 28), ScreenText = new(196, 240, 255);

    public static Color A(Color c, float a) => new Color(c, Math.Clamp(a, 0f, 1f) * (c.A / 255f));

    private static SpriteBatch Sb => FunkUI.Batch;
    private static Texture2D _px, _tri;

    private static void Ensure()
    {
        if (_tri != null && !_tri.IsDisposed) return;
        var gd = Sb.GraphicsDevice;
        _px = new Texture2D(gd, 1, 1); _px.SetData(new[] { Color.White });
        // right triangle: filled where x + y >= N (right angle at the bottom-right), anti-aliased along the diagonal
        const int N = 128;
        var d = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dist = (x + 0.5f + y + 0.5f - N) / 1.41421356f;     // signed distance to the diagonal (px)
                float a = Math.Clamp(dist + 0.5f, 0f, 1f);
                d[y * N + x] = new Color(255, 255, 255, (int)(a * 255f + 0.5f));
            }
        _tri = FunkUI.MipTex(gd, N, N, d);
    }

    // ------------------------------------------------------------------ primitives
    /// <summary>Solid axis-aligned rectangle (no edge ramps, so neighbouring pieces join without seams).</summary>
    public static void Box(float x, float y, float w, float h, Color c)
    {
        if (w <= 0 || h <= 0 || c.A == 0) return;
        Ensure();
        Sb.Draw(_px, new Vector2(x, y), null, c, 0f, Vector2.Zero, new Vector2(w, h), SpriteEffects.None, 0f);
    }

    /// <summary>Right triangle filling half of the box; <paramref name="corner"/> = the right-angle corner 0 TL, 1 TR, 2 BR, 3 BL.</summary>
    public static void Tri(float x, float y, float w, float h, int corner, Color c)
    {
        if (w <= 0 || h <= 0 || c.A == 0) return;
        Ensure();
        var fx = corner switch { 0 => SpriteEffects.FlipHorizontally | SpriteEffects.FlipVertically, 1 => SpriteEffects.FlipVertically, 3 => SpriteEffects.FlipHorizontally, _ => SpriteEffects.None };
        Sb.Draw(_tri, new Vector2(x, y), null, c, 0f, Vector2.Zero, new Vector2(w / 128f, h / 128f), fx, 0f);
    }

    /// <summary>Cut-corner shape: top-left and bottom-right corners cut by <paramref name="cut"/> (Freeplay capsule silhouette).</summary>
    public static void Shape(float x, float y, float w, float h, float cut, Color c)
    {
        cut = Math.Min(cut, Math.Min(w, h) / 2f);
        if (cut < 0.5f) { Box(x, y, w, h, c); return; }
        Box(x, y + cut, w, h - 2 * cut, c);
        Box(x + cut, y, w - cut, cut, c); Tri(x, y, cut, cut, 2, c);
        Box(x, y + h - cut, w - cut, cut, c); Tri(x + w - cut, y + h - cut, cut, cut, 0, c);
    }

    /// <summary>Panel: hard drop shadow, thick black outline, flat fill, consistent corner cuts.</summary>
    public static void Panel(float x, float y, float w, float h, Color fill, float alpha = 1f, float cut = Cut, float outline = Outline, float shadow = Shadow, Color? edge = null)
    {
        if (alpha <= 0f) return;
        float co = cut + outline * 0.4142f;
        if (shadow > 0) Shape(x - outline + shadow, y - outline + shadow, w + 2 * outline, h + 2 * outline, co, A(Ink, 0.55f * alpha));
        if (outline > 0) Shape(x - outline, y - outline, w + 2 * outline, h + 2 * outline, co, A(edge ?? Ink, alpha));
        Shape(x, y, w, h, cut, A(fill, alpha * fill.A / 255f));
    }

    /// <summary>Slanted band (parallelogram): top edge shifted right by <paramref name="slant"/> (Freeplay/Results colour bands).</summary>
    public static void Band(float x, float y, float w, float h, float slant, Color c)
    {
        if (w <= 0 || h <= 0 || c.A == 0) return;
        float s = Math.Abs(slant);
        if (s < 0.5f) { Box(x, y, w, h, c); return; }
        Box(x + s, y, w - 2 * s, h, c);
        if (slant > 0) { Tri(x, y, s, h, 2, c); Tri(x + w - s, y, s, h, 0, c); }
        else { Tri(x, y, s, h, 1, c); Tri(x + w - s, y, s, h, 3, c); }
    }

    /// <summary>Band with a black outline (and optional hard shadow).</summary>
    public static void BandOutlined(float x, float y, float w, float h, float slant, Color c, float outline = 3f, float shadow = 0f, float alpha = 1f)
    {
        float k = h > 0 ? slant / h : 0f, so = slant + 2 * outline * k;
        if (shadow > 0) Band(x - outline - outline * k + shadow, y - outline + shadow, w + 2 * outline, h + 2 * outline, so, A(Ink, 0.55f * alpha));
        Band(x - outline - outline * k, y - outline, w + 2 * outline, h + 2 * outline, so, A(Ink, alpha));
        Band(x, y, w, h, slant, A(c, alpha * c.A / 255f));
    }

    /// <summary>Focus frame: 3 px outline around a cut-corner rectangle (yellow, or white while pulsing).</summary>
    public static void FocusFrame(float x, float y, float w, float h, float alpha = 1f, Color? c = null, float cut = Cut)
    {
        float t = 3f, g = Outline + 3f;
        var col = A(c ?? Yellow, alpha);
        // draw as a ring: outer shape minus inner shape is not possible in SpriteBatch -> 4 bands + 2 diagonal pieces
        float ox = x - g - t, oy = y - g - t, ow = w + 2 * (g + t), oh = h + 2 * (g + t), oc = cut + (g + t) * 0.4142f;
        Box(ox + oc, oy, ow - oc, t, col);                       // top
        Box(ox, oy + h + 2 * (g + t) - t, ow - oc, t, col);      // bottom
        Box(ox, oy + oc, t, oh - oc, col);                       // left
        Box(ox + ow - t, oy, t, oh - oc, col);                   // right
        FunkUI.Line(ox + t / 2f, oy + oc, ox + oc, oy + t / 2f, t, col, false);
        FunkUI.Line(ox + ow - oc, oy + oh - t / 2f, ox + ow - t / 2f, oy + oh - oc, t, col, false);
    }

    // ------------------------------------------------------------------ type
    public static float TextW(string s, float size) => FunkUI.MeasureVcr(s ?? "", size);
    public static float Txt(string s, float x, float y, float size, Color c, float align = 0f) => FunkUI.Text(s ?? "", x, y, size, c, align);
    public static float TxtFit(string s, float x, float y, float size, Color c, float maxW, float align = 0f)
        => FunkUI.Text(FunkMenu.Ellipsize(s ?? "", size, maxW), x, y, size, c, align);
    /// <summary>VCR text with a 2 px black outline (on top of art).</summary>
    public static float TxtOutlined(string s, float x, float y, float size, Color c, float align = 0f, float outline = 2f)
        => FunkUI.Text(s ?? "", x, y, size, c, align, outline, Ink);

    /// <summary>Heading in the Funkin bold alphabet (animated letters like the menus).</summary>
    public static float Heading(string s, float x, float y, float scale, float time, float align = 0f, float alpha = 1f)
        => FunkUI.Bold(s ?? "", x, y, scale, align, time, alpha);
    public static float HeadingW(string s, float scale) => FunkUI.BoldWidth(s ?? "", scale);

    // digital_numbers (Freeplay score digits)
    private static Texture2D _digTex; private static Dictionary<char, Rectangle> _dig;
    private static readonly string[] DigitNames = { "ZERO", "ONE", "TWO", "THREE", "FOUR", "FIVE", "SIX", "SEVEN", "EIGHT", "NINE" };

    private static Dictionary<string, (Rectangle r, Vector2 off, Point frame)> LoadAtlas(string xmlPath)
    {
        var res = new Dictionary<string, (Rectangle, Vector2, Point)>();
        try
        {
            string txt = global::FNF_MonoGame.Engine.FsCI.ReadAllText(System.IO.Path.Combine(AssetManager.ContentRoot, xmlPath));
            foreach (Match m in Regex.Matches(txt, "<SubTexture\\s+name=\"([^\"]+)\"([^>]*)/>"))
            {
                string attrs = m.Groups[2].Value;
                int I(string n) { var mm = Regex.Match(attrs, n + "=\"(-?\\d+)\""); return mm.Success ? int.Parse(mm.Groups[1].Value) : 0; }
                var r = new Rectangle(I("x"), I("y"), I("width"), I("height"));
                int fw = I("frameWidth"), fh = I("frameHeight");
                res[m.Groups[1].Value] = (r, new Vector2(-I("frameX"), -I("frameY")), new Point(fw > 0 ? fw : r.Width, fh > 0 ? fh : r.Height));
            }
        }
        catch (Exception e) { Console.WriteLine("[THEME] atlas " + xmlPath + ": " + e.Message); }
        return res;
    }

    /// <summary>Number in Freeplay's digital_numbers, <paramref name="h"/> px tall. Non-digits are skipped (gap). Returns width.</summary>
    public static float Digits(string s, float x, float y, float h, Color tint, float align = 0f, float alpha = 1f)
    {
        if (_dig == null)
        {
            _dig = new Dictionary<char, Rectangle>();
            _digTex = FunkUI.Tex("images/digital_numbers.png");
            var at = LoadAtlas("images/digital_numbers.xml");
            for (int i = 0; i < 10; i++) if (at.TryGetValue(DigitNames[i] + " DIGITAL0000", out var e)) _dig[(char)('0' + i)] = e.r;
        }
        if (_digTex == null || string.IsNullOrEmpty(s)) return 0f;
        float k = h / 197f, adv = 112f * k, w = 0f;
        foreach (char ch in s) w += ch == '.' || ch == ':' ? adv * 0.4f : adv;
        float cx = x - w * align;
        foreach (char ch in s)
        {
            if (ch == '.' || ch == ':') { Box(cx + adv * 0.12f, y + h * (ch == '.' ? 0.86f : 0.30f), adv * 0.18f, adv * 0.18f, A(tint, alpha)); if (ch == ':') Box(cx + adv * 0.12f, y + h * 0.70f, adv * 0.18f, adv * 0.18f, A(tint, alpha)); }
            else if (_dig.TryGetValue(ch, out var r))
                Sb.Draw(_digTex, new Vector2(cx + (adv - r.Width * k * 0.8f) / 2f, y + (197 - r.Height) * k), r, A(tint, alpha), 0f, Vector2.Zero, k * 0.8f * new Vector2(1f, 1.25f), SpriteEffects.None, 0f);
            cx += ch == '.' || ch == ':' ? adv * 0.4f : adv;
        }
        return w;
    }

    /// <summary>Width of <see cref="Digits"/> for the same string/height.</summary>
    public static float DigitsW(string s, float h)
    {
        if (string.IsNullOrEmpty(s)) return 0f;
        float adv = 112f * h / 197f, w = 0f;
        foreach (char ch in s) w += ch == '.' || ch == ':' ? adv * 0.4f : adv;
        return w;
    }

    private static readonly Regex NumRx = new Regex(@"^\s*(-?)(\d+(?:\.\d+)?)\s*(.*)$");

    /// <summary>A setting value like "85%", "-12 MS", "60 FPS", "12.5": Freeplay digital numbers + a small VCR unit; any
    /// other text (e.g. "UNLIMITED") in VCR caps. (x, cy) = left edge / vertical centre, <paramref name="h"/> = digit height.
    /// Pass <paramref name="draw"/> = false to measure only. Returns the width.</summary>
    public static float NumberValue(string v, float x, float cy, float h, Color unitColor, float alpha = 1f, bool draw = true)
    {
        if (string.IsNullOrEmpty(v)) return 0f;
        var m = NumRx.Match(v);
        if (!m.Success)
        {
            float ts = Math.Max(14f, h * 0.8f);
            if (draw) Txt(v, x, cy - ts * 0.5f, ts, A(unitColor, alpha));
            return TextW(v, ts);
        }
        float w = 0f, us = Math.Max(14f, h * 0.62f);
        if (m.Groups[1].Value == "-") { if (draw) Box(x, cy - h * 0.06f, h * 0.32f, Math.Max(2f, h * 0.13f), A(new Color(130, 255, 255), alpha)); w += h * 0.32f + h * 0.12f; }
        string num = m.Groups[2].Value;
        if (draw) Digits(num, x + w, cy - h * 0.5f - h * 0.06f, h, Color.White, 0f, alpha);
        w += DigitsW(num, h);
        string unit = m.Groups[3].Value.Trim();
        if (unit.Length > 0) { w += h * 0.18f; if (draw) Txt(unit, x + w, cy + h * 0.5f - us * 0.95f, us, A(unitColor, alpha)); w += TextW(unit, us); }
        return w;
    }

    // ------------------------------------------------------------------ real Freeplay art
    private static Texture2D _capTex; private static Dictionary<string, (Rectangle r, Vector2 off, Point frame)> _cap;

    /// <summary>The Freeplay song capsule (mp3 capsule w backing), <paramref name="w"/> wide at (x, y). Returns the inner
    /// "screen" rectangle where the song name goes.</summary>
    public static Rectangle Capsule(float x, float y, float w, bool selected, float alpha = 1f)
    {
        if (_cap == null) { _capTex = FunkUI.Tex("menus/freeplay/freeplayCapsule/capsule/freeplayCapsule.png"); _cap = LoadAtlas("menus/freeplay/freeplayCapsule/capsule/freeplayCapsule.xml"); }
        float k = w / 612f;
        var screen = new Rectangle((int)(x + 77f * k), (int)(y + 22f * k), (int)(400f * k), (int)(70f * k));
        if (_capTex == null || !_cap.TryGetValue(selected ? "mp3 capsule w backing0000" : "mp3 capsule w backing NOT SELECTED0000", out var e))
        {
            Panel(x, y, w, 132f * k, Navy, alpha);
            Box(screen.X, screen.Y, screen.Width, screen.Height, A(Screen, alpha));
            return screen;
        }
        Sb.Draw(_capTex, new Vector2(x + e.off.X * k, y + e.off.Y * k), e.r, A(Color.White, alpha), 0f, Vector2.Zero, k, SpriteEffects.None, 0f);
        return screen;
    }
    public static float CapsuleH(float w) => 132f * w / 612f;

    /// <summary>Freeplay difficulty sprite (menus/freeplay/freeplay&lt;diff&gt;.png), centred, <paramref name="h"/> tall. Returns width
    /// (0 when the art does not exist -&gt; caller draws a stamp).</summary>
    public static float Difficulty(string diff, float cx, float cy, float h, float alpha = 1f)
    {
        if (string.IsNullOrEmpty(diff)) return 0f;
        var t = FunkUI.Tex("menus/freeplay/freeplay" + diff.Trim().ToLowerInvariant() + ".png");
        if (t == null) return 0f;
        float w = h * t.Width / t.Height;
        FunkUI.Quad(t, cx, cy, w, h, 0f, A(Color.White, alpha));
        return w;
    }

    /// <summary>Psych Online icon (2 frames side by side: idle | selected), centred.</summary>
    public static void Icon(string name, float cx, float cy, float h, bool selected, float alpha = 1f, Color? tint = null)
    {
        if (name.StartsWith("@")) { var at = FunkUI.Art(name); if (at != null) FunkUI.Quad(at, cx, cy, h * at.Width / (float)Math.Max(at.Width, at.Height), h * at.Height / (float)Math.Max(at.Width, at.Height), 0f, A(tint ?? Color.White, alpha)); return; }
        var t = FunkUI.Tex("images/psychonline/online_" + name + ".png");
        if (t == null) return;
        // Psych Online's own sheets hold 2 frames (idle | selected) of a fixed size at the top-left of a POT canvas;
        // the other icons are single 96x96 images
        var (fw, fh) = name switch { "play" => (68, 81), "chat" => (85, 84), "settings" => (96, 89), _ => (t.Width, t.Height) };
        var src = fw == t.Width ? new Rectangle(0, 0, fw, fh) : new Rectangle(selected ? fw : 0, 0, fw, fh);
        float k = h / Math.Max(fw, fh);
        FunkUI.Quad(t, cx, cy, fw * k, fh * k, 0f, A(tint ?? Color.White, alpha), src);
    }

    // ------------------------------------------------------------------ widgets
    /// <summary>Stamp/tag: small slanted band with a black outline and black or white caps text. Returns width.</summary>
    public static float Stamp(string s, float x, float y, Color c, float align = 0f, float size = 14f, float alpha = 1f, bool darkText = true)
    {
        if (string.IsNullOrEmpty(s)) return 0f;
        s = s.ToUpperInvariant();
        float h = size + 10f, sl = h * 0.32f, w = TextW(s, size) + 18f + sl, x0 = x - w * align;
        BandOutlined(x0, y, w, h, sl, c, 2.5f, 0f, alpha);
        Txt(s, x0 + w / 2f, y + 5.5f, size, A(darkText ? Ink : Text, alpha), 0.5f);
        return w + 6f;
    }

    /// <summary>Section tab: slanted coloured band with a bold heading (top-left of a panel).</summary>
    public static float Tab(string s, float x, float y, Color c, float time = 0f, float h = 30f, float alpha = 1f)
    {
        float sc = h / 70f * 0.95f, tw = HeadingW(s, sc), sl = h * 0.35f, w = tw + 28f + sl;
        BandOutlined(x, y, w, h, sl, c, 3f, 0f, alpha);
        Heading(s, x + 14f + sl / 2f, y + h / 2f - FunkUI.BoldHeight(sc) / 2f + 1f, sc, time, 0f, alpha);
        return w;
    }

    /// <summary>Capsule button: cut-corner panel, optional icon + label; focus = yellow fill, dark label, scale pop.</summary>
    public static void Button(float x, float y, float w, float h, string label, bool focused, bool enabled = true, string icon = null,
        bool active = false, float pop = 0f, float alpha = 1f, Color? accent = null)
    {
        float s = 1f + (focused ? 0.04f + 0.05f * pop : 0f);
        float cx = x + w / 2f, cy = y + h / 2f;
        if (s != 1f) FunkUI.Push(cx, cy, 0f, s, s);
        var fill = focused ? (accent ?? Yellow) : active ? NavyHi : Navy;
        Panel(x, y, w, h, enabled || focused ? fill : NavyLo, alpha, Math.Min(Cut, h * 0.3f), Outline, focused ? Shadow + 2f : Shadow);
        if (active && !focused) Box(x, y + h - 5f, w - Math.Min(Cut, h * 0.3f), 5f, A(Green, alpha));
        var tc = focused ? Ink : enabled ? Text : Text3;
        float tw = TextW(label, 18f), ih = h * 0.62f;
        if (icon != null)
        {
            float iw = ih, total = iw + (string.IsNullOrEmpty(label) ? 0 : 8f + tw), left = cx - total / 2f;
            Icon(icon, left + iw / 2f, cy, ih, focused, enabled || focused ? alpha : alpha * 0.5f);
            if (!string.IsNullOrEmpty(label)) Txt(label, left + iw + 8f, cy - 9f, 18f, A(tc, alpha));
        }
        else if (!string.IsNullOrEmpty(label)) Txt(label, cx, cy - 9f, 18f, A(tc, alpha), 0.5f);
        if (s != 1f) FunkUI.Pop();
    }

    /// <summary>ON/OFF switch: outlined capsule, knob block.</summary>
    public static void Switch(float x, float y, bool on, bool enabled = true)
    {
        float w = 64f, h = 24f;
        Panel(x, y, w, h, on ? (enabled ? Green : A(Green, 0.5f)) : NavyLo, 1f, 7f, 3f, 0f);
        Panel(on ? x + w - 20f : x + 2f, y + 2f, 18f, h - 4f, enabled ? Text : Text3, 1f, 5f, 0f, 0f);
        Txt(on ? "ON" : "OFF", on ? x + 7f : x + w - 6f, y + 5f, 14f, on ? Ink : Text2, on ? 0f : 1f);
    }

    public static void Progress(float x, float y, float w, float p, float h = 10f, Color? c = null)
    {
        Panel(x, y, w, h, NavyLo, 1f, h * 0.4f, 2.5f, 0f);
        p = Math.Clamp(p, 0f, 1f);
        if (p > 0) Shape(x, y, Math.Max(h, w * p), h, h * 0.4f, c ?? Yellow);
    }

    // ------------------------------------------------------------------ screen furniture
    /// <summary>Background: night navy with Freeplay-like diagonal bands drifting slowly.</summary>
    public static void Backdrop(float time, Texture2D art = null)
    {
        Box(0, 0, FunkUI.W, FunkUI.H, Night);
        // (menu art is not used: one consistent flat navy backdrop on every online screen)
        float drift = (time * 18f) % 160f;
        for (int i = -2; i < 12; i++)
        {
            float bx = i * 160f + drift - 200f;
            Band(bx, 0, 70f, FunkUI.H, 260f, A(NavyLo, 0.55f));
        }
        Band(-120f, FunkUI.H - 210f, 560f, 210f, -120f, A(Purple, 0.16f));
        Band(FunkUI.W - 420f, 0, 560f, 200f, 120f, A(Cyan, 0.10f));
    }

    /// <summary>Image filling (x, y, w, h) "cover"-cropped, with the top-left corner cut by <paramref name="cut"/> (so it sits
    /// inside a cut-corner panel without covering its corner).</summary>
    public static void CutImage(Texture2D tex, float x, float y, float w, float h, float cut, float alpha = 1f)
    {
        if (tex == null || w <= 0 || h <= 0) return;
        float k = Math.Max(w / tex.Width, h / tex.Height);
        float sx0 = (tex.Width - w / k) / 2f, sy0 = (tex.Height - h / k) / 2f;
        var col = A(Color.White, alpha);
        Rectangle Src(float lx, float ly, float lw, float lh) => new Rectangle((int)(sx0 + lx / k), (int)(sy0 + ly / k), Math.Max(1, (int)(lw / k)), Math.Max(1, (int)(lh / k)));
        Sb.Draw(tex, new Rectangle((int)MathF.Round(x), (int)MathF.Round(y + cut), (int)MathF.Round(w), (int)MathF.Round(h - cut)), Src(0, cut, w, h - cut), col);
        // top strip in slices that follow the 45 deg cut
        const int N = 6;
        for (int i = 0; i < N; i++)
        {
            float y0 = cut * i / N, y1 = cut * (i + 1) / N, x0 = cut - y1;
            Sb.Draw(tex, new Rectangle((int)MathF.Round(x + x0), (int)MathF.Round(y + y0), (int)MathF.Round(w - x0), (int)MathF.Round(y1 - y0) + 1), Src(x0, y0, w - x0, y1 - y0), col);
        }
    }

    /// <summary>Freeplay-style top bar: black strip with the bold heading, a slanted yellow band under it, right-side info.</summary>
    public static void TopBar(string title, float time, string kicker = null, float slide = 1f)
    {
        float y0 = -TopH * (1f - FunkUI.EaseOutCubic(slide));
        Box(0, y0, FunkUI.W, TopH, Ink);
        Band(-20f, y0 + TopH, 520f, 8f, -8f, Yellow);
        Band(500f, y0 + TopH, 140f, 8f, -8f, Orange);
        float sc = 0.6f, tw = HeadingW(title, sc);
        if (tw > 640f) sc *= 640f / tw;
        float x = M;
        x += Heading(title, x, y0 + 9f, sc, time) + 18f;
        if (!string.IsNullOrEmpty(kicker)) Stamp(kicker, x, y0 + 21f, Yellow);
    }

    /// <summary>Bottom bar: black strip, left context text, right Xbox glyph / keycap + label pairs (glyphs follow the device).</summary>
    public static void BottomBar(InputManager input, IList<ButtonPrompts.Prompt> items, string left = null)
    {
        float y = FunkUI.H - BottomH;
        Box(0, y, FunkUI.W, BottomH, Ink);
        Band(FunkUI.W - 380f, y - 6f, 420f, 6f, 6f, Yellow);
        if (!string.IsNullOrEmpty(left)) TxtFit(left, M, y + 17f, 16f, Text2, 560f);
        if (items == null || items.Count == 0) return;
        bool pad = ButtonPrompts.UseController(input);
        const float gs = 28f, gap = 26f;
        float total = 0f;
        foreach (var it in items) total += FunkUI.MeasureGlyph(pad, it.Key, gs) + 8f + TextW(it.Text, 16f) + gap;
        total -= gap;
        float cx = FunkUI.W - M - total;
        foreach (var it in items)
        {
            cx += FunkUI.Glyph(pad, it.Pad, it.Key, cx, y + (BottomH - gs) / 2f, gs) + 8f;
            cx += Txt(it.Text, cx, y + 17f, 16f, Text) + gap;
        }
    }

    /// <summary>Toast: slanted yellow band with dark text, slides down from the top bar.</summary>
    public static void Toast(string s, float y, float alpha, Color? accent = null)
    {
        if (string.IsNullOrEmpty(s) || alpha <= 0f) return;
        s = FunkMenu.Ellipsize(s, 17f, 860f);
        float w = TextW(s, 17f) + 60f, x = FunkUI.W / 2f - w / 2f;
        BandOutlined(x, y, w, 40f, 12f, accent ?? Yellow, 3f, Shadow, alpha);
        Txt(s, FunkUI.W / 2f, y + 11f, 17f, A(Ink, alpha), 0.5f);
    }

    /// <summary>Beat pulse (1 on the beat, decays) of the lobby music, 102 BPM.</summary>
    public static float Bop => FunkUI.Bop;
}
