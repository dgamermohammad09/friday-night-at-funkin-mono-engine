using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FontStashSharp;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Port-only modern menu toolkit (new main menu + multiplayer menus). Everything is laid out in the usual 1280x720
/// logical units, but drawn at the current render target's real resolution (Scene.NativeResolution): Begin() puts a
/// scale matrix K = targetWidth/1280 on the batch, text is rasterised at size*K and drawn at 1/K, procedural
/// shapes/icons use mipmapped textures. Result: crisp at 720p, 1080p, 1440p and 4K / TVs, and still correct when the
/// menu is drawn into the 1280x720 target (K = 1, e.g. behind Freeplay).
/// Icons: Content/images/ui/menuicons/*.png (white glyphs, 256 px). Fonts: Content/fonts/ui (Quantico Bold, Montserrat).
/// </summary>
public static class ModernUI
{
    public const float W = 1280f, H = 720f;
    /// <summary>TV safe area (5% each side, Xbox title-safe guidance).</summary>
    public const float SafeX = 64f, SafeY = 36f;
    public static float K { get; private set; } = 1f;

    private static Texture2D _round, _glow, _circle, _vgrad, _hgrad, _px;
    private const int R = 96, RS = 2 * R + 4;   // rounded-rect texture: corner radius 96 px, 4 px straight middle
    private static readonly Dictionary<string, Texture2D> _icons = new();
    private static bool _fontsLoaded;

    // ------------------------------------------------------------------------------------------------ batches
    public static void Begin(SpriteBatch sb, BlendState blend = null)
    {
        var gd = sb.GraphicsDevice;
        K = gd.Viewport.Width / W;
        EnsureTextures(gd);
        sb.Begin(SpriteSortMode.Deferred, blend ?? BlendState.NonPremultiplied, SamplerState.LinearClamp, null, null, null, Matrix.CreateScale(K));
    }

    /// <summary>Round a logical coordinate to a whole physical pixel (crisp edges/text).</summary>
    public static float Snap(float v) => MathF.Round(v * K) / K;

    // ------------------------------------------------------------------------------------------------ textures
    private static void EnsureTextures(GraphicsDevice gd)
    {
        if (_round != null && !_round.IsDisposed) return;
        _px = new Texture2D(gd, 1, 1); _px.SetData(new[] { Color.White });
        _round = MipTex(gd, RS, RS, (x, y) => Coverage(CornerDist(x, y), R));
        _glow = MipTex(gd, RS, RS, (x, y) => { float d = CornerDist(x, y) / R; float a = Math.Clamp(1f - d, 0f, 1f); return a * a * (3 - 2 * a); });
        _circle = MipTex(gd, 256, 256, (x, y) => Coverage(MathF.Sqrt((x + 0.5f - 128) * (x + 0.5f - 128) + (y + 0.5f - 128) * (y + 0.5f - 128)), 127f));
        _vgrad = MipTex(gd, 4, 256, (x, y) => 1f - (y + 0.5f) / 256f);
        _hgrad = MipTex(gd, 256, 4, (x, y) => 1f - (x + 0.5f) / 256f);
    }

    /// <summary>Distance of a texel from the nearest corner centre (0 inside the straight middle bands).</summary>
    private static float CornerDist(int x, int y)
    {
        float fx = x + 0.5f, fy = y + 0.5f;
        float dx = fx < R ? R - fx : fx > RS - R ? fx - (RS - R) : 0f;
        float dy = fy < R ? R - fy : fy > RS - R ? fy - (RS - R) : 0f;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
    private static float Coverage(float d, float radius) => Math.Clamp(radius - d + 0.5f, 0f, 1f);

    private static Texture2D MipTex(GraphicsDevice gd, int w, int h, Func<int, int, float> alpha)
    {
        var data = new Color[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            data[y * w + x] = new Color(255, 255, 255, (int)(Math.Clamp(alpha(x, y), 0f, 1f) * 255f + 0.5f));
        return MipTex(gd, w, h, data);
    }

    /// <summary>Texture with a full mip chain (alpha-weighted box filter) so large art stays smooth when drawn small.</summary>
    public static Texture2D MipTex(GraphicsDevice gd, int w, int h, Color[] data)
    {
        var t = new Texture2D(gd, w, h, true, SurfaceFormat.Color);
        t.SetData(0, null, data, 0, data.Length);
        var cur = data; int cw = w, ch = h;
        for (int l = 1; l < t.LevelCount; l++)
        {
            int nw = Math.Max(1, cw / 2), nh = Math.Max(1, ch / 2);
            var nx = new Color[nw * nh];
            for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                {
                    float r = 0, g = 0, b = 0, a = 0;
                    for (int j = 0; j < 2; j++)
                        for (int i = 0; i < 2; i++)
                        {
                            var c = cur[Math.Min(ch - 1, y * 2 + j) * cw + Math.Min(cw - 1, x * 2 + i)];
                            float ca = c.A; r += c.R * ca; g += c.G * ca; b += c.B * ca; a += ca;
                        }
                    nx[y * nw + x] = a > 0 ? new Color((int)(r / a), (int)(g / a), (int)(b / a), (int)(a / 4f + 0.5f)) : Color.Transparent;
                }
            t.SetData(l, null, nx, 0, nx.Length);
            cur = nx; cw = nw; ch = nh;
        }
        return t;
    }

    /// <summary>Icon from images/ui/menuicons (mipmapped copy, cached). Null when missing.</summary>
    public static Texture2D Icon(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (_icons.TryGetValue(name, out var t) && (t == null || !t.IsDisposed)) return t;
        var assets = FNFGame.Instance.Assets;
        var src = assets.LoadTexture("images/ui/menuicons/" + name + ".png");
        if (src == null || src == assets.Pixel) { _icons[name] = null; return null; }
        var data = new Color[src.Width * src.Height];
        src.GetData(data);
        t = MipTex(src.GraphicsDevice, src.Width, src.Height, data);
        _icons[name] = t;
        return t;
    }

    // ------------------------------------------------------------------------------------------------ shapes
    public static void Blit(SpriteBatch sb, Texture2D tex, Rectangle src, float x, float y, float w, float h, Color c)
    {
        if (w <= 0 || h <= 0) return;
        sb.Draw(tex, new Vector2(x, y), src, c, 0f, Vector2.Zero, new Vector2(w / src.Width, h / src.Height), SpriteEffects.None, 0f);
    }

    public static void Rect(SpriteBatch sb, float x, float y, float w, float h, Color c)
        => sb.Draw(_px, new Vector2(x, y), null, c, 0f, Vector2.Zero, new Vector2(w, h), SpriteEffects.None, 0f);

    /// <summary>Rotated bar centred at (cx, cy).</summary>
    public static void Bar(SpriteBatch sb, float cx, float cy, float w, float h, float angle, Color c)
        => sb.Draw(_px, new Vector2(cx, cy), null, c, angle, new Vector2(0.5f, 0.5f), new Vector2(w, h), SpriteEffects.None, 0f);

    private static void NineSlice(SpriteBatch sb, Texture2D tex, float x, float y, float w, float h, float corner, Color c)
    {
        corner = Math.Min(corner, Math.Min(w, h) / 2f);
        float mw = w - 2 * corner, mh = h - 2 * corner;
        int m = RS - 2 * R;
        Blit(sb, tex, new Rectangle(0, 0, R, R), x, y, corner, corner, c);
        Blit(sb, tex, new Rectangle(R, 0, m, R), x + corner, y, mw, corner, c);
        Blit(sb, tex, new Rectangle(R + m, 0, R, R), x + w - corner, y, corner, corner, c);
        Blit(sb, tex, new Rectangle(0, R, R, m), x, y + corner, corner, mh, c);
        Blit(sb, tex, new Rectangle(R, R, m, m), x + corner, y + corner, mw, mh, c);
        Blit(sb, tex, new Rectangle(R + m, R, R, m), x + w - corner, y + corner, corner, mh, c);
        Blit(sb, tex, new Rectangle(0, R + m, R, R), x, y + h - corner, corner, corner, c);
        Blit(sb, tex, new Rectangle(R, R + m, m, R), x + corner, y + h - corner, mw, corner, c);
        Blit(sb, tex, new Rectangle(R + m, R + m, R, R), x + w - corner, y + h - corner, corner, corner, c);
    }

    public static void Round(SpriteBatch sb, float x, float y, float w, float h, float radius, Color c) => NineSlice(sb, _round, x, y, w, h, radius, c);

    /// <summary>Rounded outline of thickness t (drawn as 4 straight bands + corner arcs approximated by a ring).</summary>
    public static void RoundOutline(SpriteBatch sb, float x, float y, float w, float h, float radius, float t, Color c)
    {
        // outer rounded rect minus inner one is not possible with plain blending: draw the bands + 4 corner pieces
        float r = Math.Min(radius, Math.Min(w, h) / 2f);
        Rect(sb, x + r, y, w - 2 * r, t, c); Rect(sb, x + r, y + h - t, w - 2 * r, t, c);
        Rect(sb, x, y + r, t, h - 2 * r, c); Rect(sb, x + w - t, y + r, t, h - 2 * r, c);
        int steps = Math.Max(6, (int)(r * K / 2f));
        for (int corner = 0; corner < 4; corner++)
        {
            float ccx = corner % 3 == 0 ? x + r : x + w - r, ccy = corner < 2 ? y + r : y + h - r;
            float a0 = corner switch { 0 => MathF.PI, 1 => 1.5f * MathF.PI, 2 => 0f, _ => 0.5f * MathF.PI };
            for (int i = 0; i < steps; i++)
            {
                float a = a0 + (i + 0.5f) / steps * MathF.PI / 2f;
                float seg = (r - t / 2f) * MathF.PI / 2f / steps + 0.6f / K;
                Bar(sb, ccx + MathF.Cos(a) * (r - t / 2f), ccy + MathF.Sin(a) * (r - t / 2f), seg, t, a + MathF.PI / 2f, c);
            }
        }
    }

    /// <summary>Soft glow / shadow around a rect (extends <paramref name="spread"/> beyond it).</summary>
    public static void Glow(SpriteBatch sb, float x, float y, float w, float h, float spread, Color c)
        => NineSlice(sb, _glow, x - spread, y - spread, w + 2 * spread, h + 2 * spread, spread * 2f, c);

    public static void Circle(SpriteBatch sb, float cx, float cy, float r, Color c)
        => sb.Draw(_circle, new Vector2(cx - r, cy - r), null, c, 0f, Vector2.Zero, 2f * r / 256f, SpriteEffects.None, 0f);

    /// <summary>Vertical gradient: <paramref name="top"/> at the top fading into <paramref name="bottom"/>.</summary>
    public static void VGradient(SpriteBatch sb, float x, float y, float w, float h, Color top, Color bottom)
    {
        if (bottom.A > 0) Rect(sb, x, y, w, h, bottom);
        sb.Draw(_vgrad, new Vector2(x, y), null, top, 0f, Vector2.Zero, new Vector2(w / 4f, h / 256f), SpriteEffects.None, 0f);
    }

    /// <summary>Horizontal gradient: <paramref name="left"/> fading into <paramref name="right"/>.</summary>
    public static void HGradient(SpriteBatch sb, float x, float y, float w, float h, Color left, Color right)
    {
        if (right.A > 0) Rect(sb, x, y, w, h, right);
        sb.Draw(_hgrad, new Vector2(x, y), null, left, 0f, Vector2.Zero, new Vector2(w / 256f, h / 4f), SpriteEffects.None, 0f);
    }

    /// <summary>Colour fading out vertically: opaque at the top (<paramref name="opaqueAtBottom"/> false) or at the bottom.</summary>
    public static void VFade(SpriteBatch sb, float x, float y, float w, float h, Color c, bool opaqueAtBottom)
        => sb.Draw(_vgrad, new Vector2(x, y), null, c, 0f, Vector2.Zero, new Vector2(w / 4f, h / 256f), opaqueAtBottom ? SpriteEffects.FlipVertically : SpriteEffects.None, 0f);

    /// <summary>Colour fading out horizontally: opaque at the left (<paramref name="opaqueAtRight"/> false) or at the right.</summary>
    public static void HFade(SpriteBatch sb, float x, float y, float w, float h, Color c, bool opaqueAtRight)
        => sb.Draw(_hgrad, new Vector2(x, y), null, c, 0f, Vector2.Zero, new Vector2(w / 256f, h / 4f), opaqueAtRight ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);

    public static void DrawIcon(SpriteBatch sb, string name, float cx, float cy, float size, Color c, float rotation = 0f)
    {
        var t = Icon(name);
        if (t == null) return;
        sb.Draw(t, new Vector2(cx, cy), null, c, rotation, new Vector2(t.Width / 2f, t.Height / 2f), size / t.Width, SpriteEffects.None, 0f);
    }

    /// <summary>Texture covering the whole screen (aspect fill) with an extra zoom and a parallax offset.</summary>
    public static void Cover(SpriteBatch sb, Texture2D tex, float zoom, Vector2 offset, Color c)
    {
        if (tex == null) return;
        float s = Math.Max(W / tex.Width, H / tex.Height) * zoom;
        sb.Draw(tex, new Vector2(W / 2f + offset.X, H / 2f + offset.Y), null, c, 0f, new Vector2(tex.Width / 2f, tex.Height / 2f), s, SpriteEffects.None, 0f);
    }

    // ------------------------------------------------------------------------------------------------ text
    private static void EnsureFonts()
    {
        if (_fontsLoaded) return;
        _fontsLoaded = true;
        var a = FNFGame.Instance.Assets;
        a.LoadNamedFont("mui-bold", "fonts/ui/Quantico-Bold.ttf");
        a.LoadNamedFont("mui", "fonts/ui/monsterrat.ttf");
    }

    public static SpriteFontBase Font(float size, bool bold)
    {
        EnsureFonts();
        int px = Math.Max(6, (int)MathF.Round(size * K));
        var a = FNFGame.Instance.Assets;
        return a.GetNamedFont(bold ? "mui-bold" : "mui", px) ?? a.GetFont(px);
    }

    public static float Measure(string text, float size, bool bold = true)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        return Font(size, bold).MeasureString(text).X / K;
    }

    /// <summary>Draw text; (x, y) = anchor, <paramref name="align"/> 0 left / 0.5 centre / 1 right, y = top of the line.
    /// Returns the logical width.</summary>
    public static float Text(SpriteBatch sb, string text, float x, float y, float size, Color c, float align = 0f, bool bold = true, float shadow = 0.45f)
    {
        if (string.IsNullOrEmpty(text) || c.A == 0) return 0f;
        var f = Font(size, bold);
        float w = f.MeasureString(text).X / K;
        var pos = new Vector2(Snap(x - w * align), Snap(y));
        var sc = new Vector2(1f / K);
        if (shadow > 0f)
        {
            float o = Math.Max(1f, size / 16f);
            f.DrawText(sb, text, pos + new Vector2(0, o), new Color(Color.Black, shadow * c.A / 255f), scale: sc);
        }
        f.DrawText(sb, text, pos, c, scale: sc);
        return w;
    }

    /// <summary>Word-wrapped text block (left aligned); returns the height used.</summary>
    public static float Paragraph(SpriteBatch sb, string text, float x, float y, float maxW, float size, Color c, bool bold = false, float lineH = 1.3f)
    {
        float cy = y;
        foreach (var line in Wrap(text, maxW, size, bold)) { Text(sb, line, x, cy, size, c, 0f, bold, 0.35f); cy += size * lineH; }
        return cy - y;
    }

    public static List<string> Wrap(string text, float maxW, float size, bool bold)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) return lines;
        string cur = "";
        foreach (var word in text.Split(' '))
        {
            string t = cur.Length == 0 ? word : cur + " " + word;
            if (cur.Length > 0 && Measure(t, size, bold) > maxW) { lines.Add(cur); cur = word; }
            else cur = t;
        }
        if (cur.Length > 0) lines.Add(cur);
        return lines;
    }

    // ------------------------------------------------------------------------------------------------ prompts
    /// <summary>Glyph for a prompt: Xbox button art (controller) or a keycap with the key name (keyboard). Returns width.</summary>
    public static float Glyph(SpriteBatch sb, bool pad, Buttons button, string key, float x, float y, float size, float alpha = 1f)
    {
        var assets = FNFGame.Instance.Assets;
        if (pad)
        {
            var t = assets.LoadTexture("images/ui/prompts/" + ButtonPrompts.PadGlyph(button) + ".png");
            if (t != null && t != assets.Pixel) { sb.Draw(t, new Vector2(x, y), null, new Color(Color.White, alpha), 0f, Vector2.Zero, size / t.Width, SpriteEffects.None, 0f); return size; }
        }
        switch (key)
        {
            case "UPDOWN": { float w = Key(sb, "", x, y, size, alpha, 0); return w + 3 + Key(sb, "", x + w + 3, y, size, alpha, 2); }
            case "LEFTRIGHT": { float w = Key(sb, "", x, y, size, alpha, 3); return w + 3 + Key(sb, "", x + w + 3, y, size, alpha, 1); }
            case "ARROWS": { float w = Key(sb, "", x, y, size, alpha, 3); w += 3 + Key(sb, "", x + w + 3, y, size, alpha, 0); w += 3 + Key(sb, "", x + w + 3, y, size, alpha, 2); return w + 3 + Key(sb, "", x + w + 3, y, size, alpha, 1); }
        }
        return Key(sb, key, x, y, size, alpha, -1);
    }

    /// <summary>Keyboard keycap; arrowDir 0 up, 1 right, 2 down, 3 left, -1 = label only.</summary>
    public static float Key(SpriteBatch sb, string label, float x, float y, float size, float alpha, int arrowDir)
    {
        float fs = size * (label.Length <= 1 ? 0.5f : 0.4f);
        float w = Math.Max(size, label.Length > 0 ? Measure(label, fs) + size * 0.6f : size);
        Round(sb, x, y + size * 0.08f, w, size * 0.92f, size * 0.22f, new Color(new Color(150, 150, 165), alpha));      // key side
        Round(sb, x, y, w, size * 0.86f, size * 0.22f, new Color(new Color(245, 245, 250), alpha));                     // key top
        if (arrowDir >= 0)
            DrawIcon(sb, "arrow", x + w / 2f, y + size * 0.43f, size * 0.5f, new Color(new Color(35, 35, 45), alpha), arrowDir * MathHelper.PiOver2);
        else if (label.Length > 0)
            Text(sb, label, x + w / 2f, y + size * 0.43f - fs * 0.62f, fs, new Color(new Color(35, 35, 45), alpha), 0.5f, true, 0f);
        return w;
    }

    public static float MeasureGlyph(bool pad, string key, float size)
    {
        if (pad) return size;
        switch (key)
        {
            case "UPDOWN": case "LEFTRIGHT": return size * 2 + 3;
            case "ARROWS": return size * 4 + 9;
        }
        float fs = size * (key.Length <= 1 ? 0.5f : 0.4f);
        return Math.Max(size, key.Length > 0 ? Measure(key, fs) + size * 0.6f : size);
    }

    /// <summary>Pill-shaped prompt bar. align 0 = x is the left edge, 0.5 centre, 1 right edge.</summary>
    public static void PromptBar(SpriteBatch sb, InputManager input, IList<ButtonPrompts.Prompt> items, float x, float y, float align, float size = 34f, float alpha = 1f)
    {
        if (items == null || items.Count == 0 || alpha <= 0f) return;
        bool pad = ButtonPrompts.UseController(input);
        float fs = size * 0.52f, gap = size * 0.7f, total = 0;
        foreach (var it in items) total += MeasureGlyph(pad, it.Key, size) + 8 + Measure(it.Text, fs) + gap;
        total -= gap;
        float left = x - total * align;
        Round(sb, left - size * 0.5f, y - size * 0.3f, total + size, size * 1.6f, size * 0.8f, new Color(new Color(10, 8, 22), 0.72f * alpha));
        float cx = left;
        foreach (var it in items)
        {
            cx += Glyph(sb, pad, it.Pad, it.Key, cx, y, size, alpha) + 8;
            cx += Text(sb, it.Text, cx, y + size * 0.5f - fs * 0.62f, fs, new Color(Color.White, alpha), 0f, true, 0.5f) + gap;
        }
    }

    // ------------------------------------------------------------------------------------------------ maths
    public static float Approach(float v, float target, float speed, float dt) => v + (target - v) * (1f - MathF.Exp(-speed * dt));
    public static float EaseOutCubic(float t) { t = Math.Clamp(t, 0f, 1f); float u = 1f - t; return 1f - u * u * u; }
    public static float EaseOutBack(float t) { t = Math.Clamp(t, 0f, 1f); const float c1 = 1.70158f, c3 = c1 + 1f; float u = t - 1f; return 1f + c3 * u * u * u + c1 * u * u; }
    public static Color Mix(Color a, Color b, float t) => Color.Lerp(a, b, Math.Clamp(t, 0f, 1f));
    public static Color A(Color c, float alpha) => new Color(c, Math.Clamp(alpha, 0f, 1f) * (c.A / 255f));

    /// <summary>Menu card body: drop shadow, accent glow (selected), border, glassy/accent body with a soft gradient,
    /// confirm flash. <paramref name="sel"/> 0..1 = selection amount (animated).</summary>
    public static void Card(SpriteBatch sb, float x, float y, float w, float h, float rad, Color accent, float sel, float alpha, float pulse, float flash = 0f)
    {
        Glow(sb, x + 4, y + 12, w - 8, h - 4, 26f, new Color(Color.Black, 0.55f * alpha));
        if (sel > 0.01f) Glow(sb, x, y, w, h, 30f, A(accent, (0.55f + 0.25f * pulse) * sel * alpha));
        float b = 3f + 1f * sel;
        Round(sb, x - b, y - b, w + 2 * b, h + 2 * b, rad + b, new Color(Mix(Color.White, accent, 0.5f * (1f - sel)), (0.18f + 0.82f * sel) * alpha));
        var glass = new Color(24, 20, 48);
        var body = Mix(Mix(glass, accent, 0.22f), Mix(accent, Color.Black, 0.22f), 0.8f * sel);
        Round(sb, x, y, w, h, rad, new Color(body, 0.94f * alpha));
        // soft top sheen + bottom shade (stacked translucent layers = smooth gradient clipped to the rounded shape)
        // (many thin steps: each step changes alpha by ~1%, below visible banding)
        for (int k = 0; k < 20; k++)
        {
            float t0 = 0.40f + k * 0.03f;
            Round(sb, x + 1, y + h * t0, w - 2, h * (1f - t0) - 1, rad - 1, new Color(Color.Black, 0.012f * alpha));
            float t1 = 0.48f - k * 0.024f;
            Round(sb, x + 1, y + 1, w - 2, h * t1, rad - 1, new Color(Color.White, (0.005f + 0.004f * sel) * alpha));
        }
        if (flash > 0f) Round(sb, x, y, w, h, rad, new Color(Color.White, flash * 0.75f * alpha));
    }

    /// <summary>Round icon badge: accent disc with a light ring and a white glyph.</summary>
    public static void IconBadge(SpriteBatch sb, string icon, float cx, float cy, float r, Color accent, float sel, float alpha, float rotation = 0f)
    {
        Circle(sb, cx, cy, r + 3.5f, new Color(Color.White, (0.10f + 0.25f * sel) * alpha));
        Circle(sb, cx, cy, r, new Color(Mix(accent, Color.Black, 0.35f - 0.2f * sel), alpha));
        DrawIcon(sb, icon, cx, cy, r * 1.25f, new Color(Color.White, alpha), rotation);
    }

    /// <summary>Animated menu backdrop shared by the modern menus: blurred, desaturated menu art tinted with the current
    /// accent (smooth cross-fade), slow zoom + parallax, contrast overlays, moving light bands and floating particles.</summary>
    public sealed class Backdrop
    {
        private readonly Texture2D _desat, _plain;
        private Color _tint;
        private float _time;
        public Vector2 Parallax;
        public float Zoom(float pulse) => 1.08f + 0.012f * MathF.Sin(_time * 0.45f) + 0.006f * pulse;

        public Backdrop(Color accent)
        {
            var a = FNFGame.Instance.Assets;
            Texture2D L(string p) { var t = a.LoadTexture(p); return t == null || t == a.Pixel ? null : t; }
            _desat = L("images/ui/menuicons/bg_menuDesat.png") ?? L("menus/main_menu/menuDesat.png");
            _plain = L("images/ui/menuicons/bg_background.png") ?? L("menus/main_menu/background.png");
            _tint = TintFor(accent);
        }
        private static Color TintFor(Color accent) => Mix(accent, Color.White, 0.4f);

        public void Update(float dt, Color accent, Vector2 parallaxTarget)
        {
            _time += dt;
            var tt = TintFor(accent);
            float k = 1f - MathF.Exp(-6f * dt);
            _tint = new Color((int)MathHelper.Lerp(_tint.R, tt.R, k), (int)MathHelper.Lerp(_tint.G, tt.G, k), (int)MathHelper.Lerp(_tint.B, tt.B, k));
            Parallax = new Vector2(Approach(Parallax.X, parallaxTarget.X, 3f, dt), Approach(Parallax.Y, parallaxTarget.Y, 3f, dt));
        }

        /// <summary>Art + contrast overlays.</summary>
        public void DrawBase(SpriteBatch sb, float pulse)
        {
            if (_desat != null) Cover(sb, _desat, Zoom(pulse), Parallax, _tint);
            else Cover(sb, _plain, Zoom(pulse), Parallax, Color.White);
            var deep = new Color(14, 10, 34);
            Rect(sb, 0, 0, W, H, new Color(deep, 0.50f));
            VFade(sb, 0, 0, W, 280, new Color(deep, 0.80f), false);
            VFade(sb, 0, 420, W, 300, new Color(deep, 0.85f), true);
        }

        /// <summary>Light bands + particles (draw after any extra glow).</summary>
        public void DrawFx(SpriteBatch sb, float pulse)
        {
            for (int i = 0; i < 6; i++)
            {
                float speed = 18f + i * 7f;
                float x = ((i * 263f + _time * speed) % 1900f) - 300f;
                Bar(sb, x, 360, 46f + (i % 3) * 38f, 1400f, 0.42f, new Color(Color.White, 0.025f + 0.01f * (i % 2)));
            }
            for (int i = 0; i < 46; i++)
            {
                float seed = i * 12.9898f;
                float fx = Frac(MathF.Sin(seed) * 43758.55f), fy = Frac(MathF.Sin(seed * 1.7f) * 24634.63f);
                float sp = 10f + 26f * Frac(fx * 7.13f);
                float px = fx * W + MathF.Sin(_time * 0.6f + i) * 14f + Parallax.X * 1.6f;
                float py = (fy * (H + 40f) - _time * sp) % (H + 40f);
                if (py < -20f) py += H + 40f;
                float rad = 1.6f + 3.2f * Frac(fy * 3.7f);
                float al = 0.10f + 0.20f * Frac(fx * 5.3f) + 0.15f * pulse;
                Circle(sb, px, py + Parallax.Y * 1.6f, rad * (1f + 0.35f * pulse), new Color(Color.White, al));
            }
        }
        private static float Frac(float v) => v - MathF.Floor(v);
    }

    /// <summary>Beat pulse of the menu music (freakyMenu, 102 BPM): 1 on the beat, decaying to 0.</summary>
    public static float MenuBeat => (float)(FNFGame.Instance.Audio.MusicPosition * 102.0 / 60.0);
    public static float MenuPulse { get { float b = MenuBeat; float f = b - MathF.Floor(b); return MathF.Pow(1f - f, 3f); } }

    /// <summary>Animated FNF atlas lettering (main-menu word art), centred at (cx, cy), looping at 24 fps like AtlasMenuItem.
    /// The centre is taken from the animation's frame box (frameWidth/frameHeight) so trimmed frames never jump.</summary>
    public static void AtlasWord(SpriteBatch sb, SpriteSheet sheet, string anim, float cx, float cy, float scale, float time, float alpha)
    {
        var frames = sheet?.GetAnimation(anim);
        if (frames == null || frames.Count == 0 || alpha <= 0f) return;
        var f = frames[(int)(time * 24f) % frames.Count];
        int fw = f.FrameWidth > 0 ? f.FrameWidth : f.SourceRect.Width, fh = f.FrameHeight > 0 ? f.FrameHeight : f.SourceRect.Height;
        float x = cx - fw * scale / 2f + f.Offset.X * scale, y = cy - fh * scale / 2f + f.Offset.Y * scale;
        sb.Draw(f.Texture ?? sheet.Texture, new Vector2(x, y), f.SourceRect, new Color(Color.White, alpha), 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }
}
