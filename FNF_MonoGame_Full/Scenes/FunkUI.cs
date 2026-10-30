using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FontStashSharp;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Hand-made, FNF-flavoured menu toolkit (main menu + multiplayer menus): flat bold colours, chunky black outlines,
/// tilted / squashed "sticker" elements, the game's own art (main-menu lettering, Funkin bold alphabet, VCR font,
/// transition stickers, health icons) and marker-doodle icons (Content/images/ui/funk, generator scratch/human/doodles.py).
/// Layout is in 1280x720 logical units; Begin() scales the batch by K = targetWidth/1280 so menus rendered at the real
/// back-buffer size (Scene.NativeResolution) stay crisp at 1080p / 1440p / 4K, and still work at K = 1.
/// Every element can be tilted/squashed about its own pivot with Push()/Pop() (restarts the batch with a local matrix).
/// Shapes are anti-aliased with 1-physical-pixel edge ramps (no MSAA needed). No gradients, glows or rounded tiles.
/// </summary>
public static class FunkUI
{
    public const float W = 1280f, H = 720f;
    /// <summary>TV safe area (5 % each side).</summary>
    public const float SafeX = 64f, SafeY = 36f;
    public static float K { get; private set; } = 1f;

    // flat FNF palette
    public static readonly Color Yellow = new(255, 214, 64), Pink = new(255, 92, 168), Cyan = new(49, 210, 247),
        Purple = new(164, 106, 255), Green = new(110, 230, 90), Red = new(255, 70, 70), Orange = new(255, 150, 50),
        Paper = new(255, 248, 226), Ink = new(18, 14, 22), Blue = new(70, 120, 255);

    private static SpriteBatch _sb;
    public static SpriteBatch Batch => _sb;
    private static Matrix _cur = Matrix.Identity;
    private static readonly Stack<Matrix> _stack = new();
    private static Texture2D _px, _rampH, _rampV, _circle;
    private static readonly Dictionary<string, Texture2D> _tex = new();
    private static bool _fontsLoaded;

    // ------------------------------------------------------------------------------------------------ batch / transforms
    public static void Begin(SpriteBatch sb)
    {
        _sb = sb;
        K = sb.GraphicsDevice.Viewport.Width / W;
        EnsureTextures(sb.GraphicsDevice);
        _stack.Clear(); _cur = Matrix.Identity;
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp, null, null, null, Matrix.CreateScale(K));
    }

    private static void Restart()
    {
        _sb.End();
        _sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp, null, null, null, _cur * Matrix.CreateScale(K));
    }

    /// <summary>Local transform about pivot (px, py): scale (sx, sy), then rotate by <paramref name="angle"/> radians,
    /// then move by (dx, dy). Everything drawn until Pop() uses it.</summary>
    public static void Push(float px, float py, float angle, float sx = 1f, float sy = 1f, float dx = 0f, float dy = 0f)
    {
        _stack.Push(_cur);
        _cur = Matrix.CreateTranslation(-px, -py, 0) * Matrix.CreateScale(sx, sy, 1f) * Matrix.CreateRotationZ(angle)
             * Matrix.CreateTranslation(px + dx, py + dy, 0) * _cur;
        Restart();
    }

    public static void Pop()
    {
        if (_stack.Count == 0) return;
        _cur = _stack.Pop();
        Restart();
    }

    /// <summary>Uniform scale of the current local transform (for raster sizes / 1-pixel edges).</summary>
    private static float CurScale => MathF.Sqrt(MathF.Max(1e-6f, MathF.Abs(_cur.M11 * _cur.M22 - _cur.M12 * _cur.M21)));
    /// <summary>One physical pixel in current local units.</summary>
    public static float Px => 1f / (K * CurScale);

    // ------------------------------------------------------------------------------------------------ textures
    private static void EnsureTextures(GraphicsDevice gd)
    {
        if (_px != null && !_px.IsDisposed) return;
        _px = new Texture2D(gd, 1, 1); _px.SetData(new[] { Color.White });
        _rampH = new Texture2D(gd, 2, 1); _rampH.SetData(new[] { new Color(255, 255, 255, 0), Color.White });
        _rampV = new Texture2D(gd, 1, 2); _rampV.SetData(new[] { new Color(255, 255, 255, 0), Color.White });
        const int C = 256;
        var data = new Color[C * C];
        for (int y = 0; y < C; y++)
            for (int x = 0; x < C; x++)
            {
                float d = MathF.Sqrt((x + 0.5f - C / 2f) * (x + 0.5f - C / 2f) + (y + 0.5f - C / 2f) * (y + 0.5f - C / 2f));
                data[y * C + x] = new Color(255, 255, 255, (int)(Math.Clamp(C / 2f - 1f - d + 0.5f, 0f, 1f) * 255f + 0.5f));
            }
        _circle = MipTex(gd, C, C, data);
    }

    /// <summary>Texture with a full mip chain (alpha-weighted box filter) so big art stays smooth when drawn small.</summary>
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

    /// <summary>Mipmapped copy of a content PNG (cached; null when missing). Paths relative to Content.</summary>
    public static Texture2D Tex(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (_tex.TryGetValue(path, out var t) && (t == null || !t.IsDisposed)) return t;
        var assets = FNFGame.Instance.Assets;
        var src = assets.LoadTexture(path);
        if (src == null || src == assets.Pixel) { _tex[path] = null; return null; }
        var data = new Color[src.Width * src.Height];
        src.GetData(data);
        t = MipTex(src.GraphicsDevice, src.Width, src.Height, data);
        _tex[path] = t;
        return t;
    }

    /// <summary>Game sticker (transitionSwag) or doodle icon by short name: "bf1", "pico2", "dad1", "s2:nene1", "@controller".</summary>
    public static Texture2D Art(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (name[0] == '@') return Tex("images/ui/funk/" + name.Substring(1) + ".png");
        if (name.StartsWith("icon:")) return Tex("images/icons/icon-" + name.Substring(5) + ".png");
        string set = "stickers-set-1";
        if (name.StartsWith("s2:")) { set = "stickers-set-2"; name = name.Substring(3); }
        int n = name.Length; while (n > 0 && char.IsDigit(name[n - 1])) n--;
        return Tex($"transitionSwag/{set}/{name.Substring(0, n)}Sticker{name.Substring(n)}.png");
    }

    // ------------------------------------------------------------------------------------------------ shapes
    /// <summary>Texture centred at (cx, cy), drawn <paramref name="w"/> x <paramref name="h"/>, rotated.</summary>
    public static void Quad(Texture2D tex, float cx, float cy, float w, float h, float angle, Color c, Rectangle? src = null, SpriteEffects fx = SpriteEffects.None)
    {
        if (tex == null || w <= 0 || h <= 0 || c.A == 0) return;
        var r = src ?? new Rectangle(0, 0, tex.Width, tex.Height);
        _sb.Draw(tex, new Vector2(cx, cy), r, c, angle, new Vector2(r.Width / 2f, r.Height / 2f), new Vector2(w / r.Width, h / r.Height), fx, 0f);
    }

    /// <summary>Anti-aliased filled rectangle (local units).</summary>
    public static void Fill(float x, float y, float w, float h, Color c)
    {
        if (w <= 0 || h <= 0 || c.A == 0) return;
        float e = Px;
        if (w <= 3 * e || h <= 3 * e) { _sb.Draw(_px, new Vector2(x, y), null, c, 0f, Vector2.Zero, new Vector2(w, h), SpriteEffects.None, 0f); return; }
        _sb.Draw(_px, new Vector2(x + e, y + e), null, c, 0f, Vector2.Zero, new Vector2(w - 2 * e, h - 2 * e), SpriteEffects.None, 0f);
        _sb.Draw(_rampH, new Vector2(x - e, y + e), null, c, 0f, Vector2.Zero, new Vector2(e, h - 2 * e), SpriteEffects.None, 0f);
        _sb.Draw(_rampH, new Vector2(x + w - e, y + e), null, c, 0f, Vector2.Zero, new Vector2(e, h - 2 * e), SpriteEffects.FlipHorizontally, 0f);
        _sb.Draw(_rampV, new Vector2(x + e, y - e), null, c, 0f, Vector2.Zero, new Vector2(w - 2 * e, e), SpriteEffects.None, 0f);
        _sb.Draw(_rampV, new Vector2(x + e, y + h - e), null, c, 0f, Vector2.Zero, new Vector2(w - 2 * e, e), SpriteEffects.FlipVertically, 0f);
    }

    /// <summary>Flat "paper" block: chunky black outline of thickness <paramref name="t"/> around a flat fill.</summary>
    public static void Block(float x, float y, float w, float h, Color fill, float t = 5f, Color? outline = null, float alpha = 1f)
    {
        var o = outline ?? Color.Black;
        Fill(x - t, y - t, w + 2 * t, h + 2 * t, new Color(o, alpha * o.A / 255f));
        Fill(x, y, w, h, new Color(fill, alpha * fill.A / 255f));
    }

    /// <summary>Hard comic shadow (solid, offset - never blurred) + block.</summary>
    public static void ShadowBlock(float x, float y, float w, float h, Color fill, float t = 5f, float sx = 9f, float sy = 10f, float alpha = 1f)
    {
        Fill(x - t + sx, y - t + sy, w + 2 * t, h + 2 * t, new Color(Color.Black, alpha));
        Block(x, y, w, h, fill, t, null, alpha);
    }

    /// <summary>Anti-aliased line with round caps.</summary>
    public static void Line(float x0, float y0, float x1, float y1, float thick, Color c, bool caps = true)
    {
        float dx = x1 - x0, dy = y1 - y0, len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1e-3f || c.A == 0) return;
        float a = MathF.Atan2(dy, dx), e = Px;
        float mx = (x0 + x1) / 2f, my = (y0 + y1) / 2f;
        float nx = -dy / len, ny = dx / len;
        float core = MathF.Max(0f, thick - 2 * e);
        _sb.Draw(_px, new Vector2(mx, my), null, c, a, new Vector2(0.5f, 0.5f), new Vector2(len, core), SpriteEffects.None, 0f);
        float off = thick / 2f;
        _sb.Draw(_rampV, new Vector2(mx - nx * off, my - ny * off), null, c, a, new Vector2(0.5f, 1f), new Vector2(len, e), SpriteEffects.None, 0f);
        _sb.Draw(_rampV, new Vector2(mx + nx * off, my + ny * off), null, c, a, new Vector2(0.5f, 1f), new Vector2(len, e), SpriteEffects.FlipVertically, 0f);
        if (caps) { Disc(x0, y0, thick / 2f, c); Disc(x1, y1, thick / 2f, c); }
    }

    public static void Disc(float cx, float cy, float r, Color c)
        => _sb.Draw(_circle, new Vector2(cx, cy), null, c, 0f, new Vector2(128f, 128f), 2f * r / 254f, SpriteEffects.None, 0f);

    /// <summary>Marker stroke through points with a little hand wobble (deterministic per seed).</summary>
    public static void Marker(IList<Vector2> pts, float thick, Color c, int seed = 1, float wobble = 1.5f, bool closed = false)
    {
        if (pts == null || pts.Count < 2) return;
        var list = new List<Vector2>();
        int n = pts.Count, segs = closed ? n : n - 1;
        for (int i = 0; i < segs; i++)
        {
            var a = pts[i]; var b = pts[(i + 1) % n];
            int steps = Math.Max(1, (int)((b - a).Length() / 18f));
            for (int k = 0; k < steps; k++) list.Add(Vector2.Lerp(a, b, k / (float)steps));
        }
        if (!closed) list.Add(pts[n - 1]); else list.Add(list[0]);
        for (int i = 0; i < list.Count; i++)
        {
            float t = i * 0.37f + seed * 1.7f;
            list[i] += new Vector2(MathF.Sin(t * 1.3f) + MathF.Sin(t * 0.7f + 2f) * 0.6f, MathF.Cos(t * 1.1f + 1f) + MathF.Sin(t * 0.5f) * 0.6f) * wobble * 0.6f;
        }
        // dense discs (no segment quads): overlapping AA rims would show seams where segments meet
        float stepLen = Math.Max(0.6f, thick * 0.22f);
        for (int i = 0; i < list.Count - 1; i++)
        {
            var p0 = list[i]; var p1 = list[i + 1];
            int k = Math.Max(1, (int)((p1 - p0).Length() / stepLen));
            for (int j = 0; j < k; j++) { var q = Vector2.Lerp(p0, p1, j / (float)k); Disc(q.X, q.Y, thick / 2f, c); }
        }
        Disc(list[^1].X, list[^1].Y, thick / 2f, c);
    }

    /// <summary>Hand-drawn rectangle outline (marker) - for doodled frames/underlines.</summary>
    public static void MarkerRect(float x, float y, float w, float h, float thick, Color c, int seed = 1)
        => Marker(new[] { new Vector2(x, y), new Vector2(x + w, y + 1.5f), new Vector2(x + w - 1f, y + h), new Vector2(x + 1f, y + h - 1f) }, thick, c, seed, 1.6f, true);

    /// <summary>Art (sticker / doodle / icon) fitted to height <paramref name="h"/>, centred at (cx, cy).</summary>
    public static float Sticker(string name, float cx, float cy, float h, float angle = 0f, float alpha = 1f, float sx = 1f, float sy = 1f, Color? tint = null)
    {
        var t = Art(name);
        if (t == null || alpha <= 0f) return 0f;
        bool icon = name.StartsWith("icon:");
        var src = icon ? new Rectangle(0, 0, t.Width / 2, t.Height) : new Rectangle(0, 0, t.Width, t.Height);   // health icon: frame 0
        float w = h * src.Width / src.Height;
        Quad(t, cx, cy, w * sx, h * sy, angle, new Color(tint ?? Color.White, alpha), src);
        return w;
    }

    /// <summary>Health icon (icons/icon-*.png, 2 frames: 0 normal, 1 losing).</summary>
    public static void HealthIcon(string id, float cx, float cy, float h, int frame, float angle = 0f, float alpha = 1f, bool flip = false)
    {
        var t = Tex("images/icons/icon-" + id + ".png");
        if (t == null) return;
        int fw = t.Width >= t.Height * 2 ? t.Width / 2 : t.Width;
        var src = new Rectangle(Math.Min(frame, t.Width / fw - 1) * fw, 0, fw, t.Height);
        Quad(t, cx, cy, h * fw / t.Height, h, angle, new Color(Color.White, alpha), src, flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None);
    }

    /// <summary>Masking-tape strip (tinted doodle).</summary>
    public static void Tape(float cx, float cy, float w, float angle, Color c, float alpha = 0.85f)
        => Quad(Art("@d_tape"), cx, cy, w, w * 0.42f, angle, new Color(c, alpha));

    /// <summary>Texture covering the whole screen (aspect fill) with zoom and an offset.</summary>
    public static void Cover(Texture2D tex, float zoom, Vector2 offset, Color c)
    {
        if (tex == null) return;
        float s = Math.Max(W / tex.Width, H / tex.Height) * zoom;
        _sb.Draw(tex, new Vector2(W / 2f + offset.X, H / 2f + offset.Y), null, c, 0f, new Vector2(tex.Width / 2f, tex.Height / 2f), s, SpriteEffects.None, 0f);
    }

    // ------------------------------------------------------------------------------------------------ text
    private static void EnsureFonts()
    {
        if (_fontsLoaded) return;
        _fontsLoaded = true;
        FNFGame.Instance.Assets.LoadNamedFont("funk-vcr", "fonts/vcr.ttf");
    }

    private static SpriteFontBase Vcr(float size, out float rasterScale)
    {
        EnsureFonts();
        rasterScale = K * CurScale;
        int px = Math.Max(6, (int)MathF.Round(size * rasterScale));
        var a = FNFGame.Instance.Assets;
        return a.GetNamedFont("funk-vcr", px) ?? a.GetFont(px);
    }

    public static float MeasureVcr(string text, float size)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        var f = Vcr(size, out float rs);
        return f.MeasureString(text).X / rs;
    }

    /// <summary>VCR text, (x, y) = anchor (align 0 left, 0.5 centre, 1 right; y = top). Optional chunky outline.
    /// Returns the width.</summary>
    public static float Text(string text, float x, float y, float size, Color c, float align = 0f, float outline = 0f, Color? outlineColor = null)
    {
        if (string.IsNullOrEmpty(text) || c.A == 0) return 0f;
        var f = Vcr(size, out float rs);
        float w = f.MeasureString(text).X / rs;
        var pos = new Vector2(x - w * align, y);
        var sc = new Vector2(1f / rs);
        if (outline > 0f)
        {
            var oc = new Color(outlineColor ?? Color.Black, c.A / 255f);
            for (int k = 0; k < 12; k++)
            {
                float a = k * MathF.PI / 6f;
                f.DrawText(_sb, text, pos + new Vector2(MathF.Cos(a), MathF.Sin(a)) * outline, oc, scale: sc);
            }
        }
        f.DrawText(_sb, text, pos, c, scale: sc);
        return w;
    }

    /// <summary>Word-wrapped VCR paragraph; returns the height used.</summary>
    public static float Paragraph(string text, float x, float y, float maxW, float size, Color c, float align = 0f, float lineH = 1.25f)
    {
        float cy = y;
        foreach (var line in Wrap(text, maxW, size)) { Text(line, x, cy, size, c, align); cy += size * lineH; }
        return cy - y;
    }

    public static List<string> Wrap(string text, float maxW, float size)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) return lines;
        string cur = "";
        foreach (var word in text.Split(' '))
        {
            string t = cur.Length == 0 ? word : cur + " " + word;
            if (cur.Length > 0 && MeasureVcr(t, size) > maxW) { lines.Add(cur); cur = word; }
            else cur = t;
        }
        if (cur.Length > 0) lines.Add(cur);
        return lines;
    }

    /// <summary>Funkin bold alphabet (V-Slice AtlasText 'bold', letters boil at 24 fps). (x, y) = anchor, align 0/0.5/1,
    /// y = top. Returns the width.</summary>
    public static float Bold(string text, float x, float y, float scale, float align = 0f, float time = 0f, float alpha = 1f, Color? tint = null)
    {
        var at = AtlasText.Bold;
        if (at == null || string.IsNullOrEmpty(text)) return 0f;
        float w = at.Width(text, scale);
        at.Draw(_sb, text, x - w * align, y, alpha, time, scale, tint);
        return w;
    }
    public static float BoldWidth(string text, float scale) => AtlasText.Bold?.Width(text, scale) ?? 0f;
    public static float BoldHeight(float scale) => (AtlasText.Bold?.MaxHeight ?? 70f) * scale;

    /// <summary>Animated main-menu lettering (buttons atlas), centred at (cx, cy) on the animation's fixed frame box,
    /// looping at 24 fps like AtlasMenuItem. Returns the drawn size.</summary>
    public static Vector2 Word(SpriteSheet sheet, string anim, float cx, float cy, float scale, float time, float alpha = 1f, Color? tint = null)
    {
        var frames = sheet?.GetAnimation(anim);
        if (frames == null || frames.Count == 0 || alpha <= 0f) return Vector2.Zero;
        var f0 = frames[0];
        int fw = f0.FrameWidth > 0 ? f0.FrameWidth : f0.SourceRect.Width, fh = f0.FrameHeight > 0 ? f0.FrameHeight : f0.SourceRect.Height;
        var f = frames[(int)(time * 24f) % frames.Count];
        float x = cx - fw * scale / 2f + f.Offset.X * scale, y = cy - fh * scale / 2f + f.Offset.Y * scale;
        _sb.Draw(f.Texture ?? sheet.Texture, new Vector2(x, y), f.SourceRect, new Color(tint ?? Color.White, alpha), 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        return new Vector2(fw * scale, fh * scale);
    }

    public static Vector2 WordSize(SpriteSheet sheet, string anim)
    {
        var frames = sheet?.GetAnimation(anim);
        if (frames == null || frames.Count == 0) return Vector2.Zero;
        var f0 = frames[0];
        return new Vector2(f0.FrameWidth > 0 ? f0.FrameWidth : f0.SourceRect.Width, f0.FrameHeight > 0 ? f0.FrameHeight : f0.SourceRect.Height);
    }

    // ------------------------------------------------------------------------------------------------ prompts
    /// <summary>Glyph: Xbox button art (controller) or a flat white keycap with a chunky outline (keyboard). Returns width.</summary>
    public static float Glyph(bool pad, Buttons button, string key, float x, float y, float size, float alpha = 1f)
    {
        if (pad)
        {
            var t = Tex("images/ui/prompts/" + ButtonPrompts.PadGlyph(button) + ".png");
            if (t != null)
            {
                // pressed look (Options > Controls: the button you just pressed): pushed in = smaller, darker, nudged down
                float pk = ControllerIcons.FlashAmount(button);
                float gs = size * (1f - 0.14f * pk), dk = 1f - 0.42f * pk;
                Quad(t, x + size / 2f, y + size / 2f + size * 0.05f * pk, gs, gs, 0f, new Color(dk, dk, dk, alpha));
                return size;
            }
        }
        switch (key)
        {
            case "UPDOWN": { float w = Key("", x, y, size, alpha, 0); return w + 4 + Key("", x + w + 4, y, size, alpha, 2); }
            case "LEFTRIGHT": { float w = Key("", x, y, size, alpha, 3); return w + 4 + Key("", x + w + 4, y, size, alpha, 1); }
            case "ARROWS": { float w = Key("", x, y, size, alpha, 3); w += 4 + Key("", x + w + 4, y, size, alpha, 0); w += 4 + Key("", x + w + 4, y, size, alpha, 2); return w + 4 + Key("", x + w + 4, y, size, alpha, 1); }
        }
        return Key(key, x, y, size, alpha, -1);
    }

    /// <summary>Flat keycap; arrowDir 0 up, 1 right, 2 down, 3 left, -1 label only.</summary>
    public static float Key(string label, float x, float y, float size, float alpha, int arrowDir)
    {
        float fs = size * 0.5f;
        float w = Math.Max(size, label.Length > 0 ? MeasureVcr(label, fs) + size * 0.55f : size);
        float t = Math.Max(2f, size * 0.09f);
        Block(x + t, y + t, w - 2 * t, size - 2 * t, Color.White, t, Color.Black, alpha);
        if (arrowDir >= 0)
            Quad(Art("@arrow"), x + w / 2f, y + size / 2f, size * 0.62f, size * 0.62f, (arrowDir - 1) * MathHelper.PiOver2, new Color(Color.White, alpha));
        else if (label.Length > 0)
            Text(label, x + w / 2f, y + size / 2f - fs * 0.5f, fs, new Color(Color.Black, alpha), 0.5f);
        return w;
    }

    public static float MeasureGlyph(bool pad, string key, float size)
    {
        if (pad) return size;
        switch (key)
        {
            case "UPDOWN": case "LEFTRIGHT": return size * 2 + 4;
            case "ARROWS": return size * 4 + 12;
        }
        float fs = size * 0.5f;
        return Math.Max(size, key.Length > 0 ? MeasureVcr(key, fs) + size * 0.55f : size);
    }

    /// <summary>Prompt strip: black tape with glyphs + VCR labels, slightly tilted. (x, y) = right end / top.</summary>
    public static void PromptTape(InputManager input, IList<ButtonPrompts.Prompt> items, float right, float y, float size = 30f, float alpha = 1f, float angle = -0.012f)
    {
        if (items == null || items.Count == 0 || alpha <= 0f) return;
        bool pad = ButtonPrompts.UseController(input);
        float fs = size * 0.62f, gap = size * 0.75f, total = 0;
        foreach (var it in items) total += MeasureGlyph(pad, it.Key, size) + 8 + MeasureVcr(it.Text, fs) + gap;
        total -= gap;
        float left = right - total;
        Push(left + total / 2f, y + size / 2f, angle);
        Fill(left - 18f, y - 10f, total + 36f, size + 20f, new Color(Color.Black, 0.92f * alpha));
        float cx = left;
        foreach (var it in items)
        {
            cx += Glyph(pad, it.Pad, it.Key, cx, y, size, alpha) + 8;
            cx += Text(it.Text, cx, y + size / 2f - fs * 0.5f, fs, new Color(Color.White, alpha)) + gap;
        }
        Pop();
    }

    // ------------------------------------------------------------------------------------------------ timing / maths
    /// <summary>freakyMenu beat (102 BPM). MusicPosition is in ms.</summary>
    public static float Beat => (float)(FNFGame.Instance.Audio.MusicPosition / 1000.0 * 102.0 / 60.0);

    // ------------------------------------------------------------------------------------------------ doodles
    public readonly struct Doodle
    {
        public readonly string Art; public readonly float X, Y, Size, Tilt; public readonly Color Color;
        public Doodle(string art, float x, float y, float size, Color color, float tiltDeg) { Art = art; X = x; Y = y; Size = size; Color = color; Tilt = tiltDeg; }
    }

    /// <summary>Background marker doodles: "boil" at 8 fps (hand-drawn jitter of tilt/position) and hop on the beat.</summary>
    public static void DrawDoodles(IList<Doodle> list, float time, float bop, float alpha = 1f)
    {
        if (list == null || alpha <= 0f) return;
        int boil = (int)(time * 8f) % 3;
        for (int i = 0; i < list.Count; i++)
        {
            var d = list[i];
            float j = Hash(i * 17 + boil * 5) - 0.5f, j2 = Hash(i * 29 + boil * 3 + 1) - 0.5f;
            float hop = (i + BeatIndex) % 2 == 0 ? bop : 0f;
            float s = d.Size * (1f + 0.14f * hop);
            Sticker(d.Art, d.X + j * 2.5f, d.Y + j2 * 2.5f - hop * 6f, s, Rad(d.Tilt + j * 5f), alpha, 1f, 1f, d.Color);
        }
    }
    /// <summary>Snappy beat bop: 1 right on the beat, gone after ~1/4 beat (FNF-style scale bump).</summary>
    public static float Bop { get { float b = Beat; float f = b - MathF.Floor(b); return MathF.Exp(-f * 60f / 102f * 9f); } }
    public static int BeatIndex => (int)MathF.Floor(Beat);

    public static float Clamp01(float t) => Math.Clamp(t, 0f, 1f);
    public static float EaseOutBack(float t, float s = 2.2f) { t = Clamp01(t); float u = t - 1f; return 1f + (s + 1f) * u * u * u + s * u * u; }
    public static float EaseOutCubic(float t) { t = Clamp01(t); float u = 1f - t; return 1f - u * u * u; }
    public static float EaseInBack(float t, float s = 1.9f) { t = Clamp01(t); return (s + 1f) * t * t * t - s * t * t; }
    /// <summary>Damped wiggle (0 at t = 0, decays): sin * exp.</summary>
    public static float Wiggle(float t, float freq = 26f, float decay = 7f) => t < 0f ? 0f : MathF.Sin(t * freq) * MathF.Exp(-t * decay);
    /// <summary>Squash and stretch pair for a slap/pop started <paramref name="t"/> seconds ago.</summary>
    public static Vector2 Squash(float t, float amount = 0.22f)
    {
        if (t < 0f || t > 0.6f) return Vector2.One;
        float e = MathF.Cos(t * 22f) * MathF.Exp(-t * 9f) * amount;
        return new Vector2(1f + e, 1f - e);
    }
    public static float Approach(float v, float target, float speed, float dt) => v + (target - v) * (1f - MathF.Exp(-speed * dt));
    public static Color A(Color c, float alpha) => new Color(c, Clamp01(alpha) * (c.A / 255f));
    public static float Rad(float deg) => deg * MathF.PI / 180f;
    /// <summary>Deterministic pseudo random in [0, 1) for hand-placed jitter.</summary>
    public static float Hash(int i) { float v = MathF.Sin(i * 12.9898f + 78.233f) * 43758.547f; return v - MathF.Floor(v); }
}
