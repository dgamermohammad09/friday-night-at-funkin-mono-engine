using System;
using System.Collections.Generic;
using System.IO;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Engine;

/// <summary>What <see cref="PreloaderScreen"/> shows in one frame.</summary>
public struct PreloaderView
{
    /// <summary>Left text, first line (FunkinPreloaderState.getProgressLeftText, e.g. "Downloading assets").</summary>
    public string Title;
    /// <summary>Left text, second line (e.g. "2/6 ..." or "Press A to try again").</summary>
    public string StepLine;
    /// <summary>0..1: progress bar pieces + percent text.</summary>
    public float Progress;
    /// <summary>Port-only detail text under the bar (download status / error message; '\n' and long lines wrap).</summary>
    public string Status;
    /// <summary>Display alpha (FunkinPreloader.renderDisplayFadeOut).</summary>
    public float Alpha;
}

/// <summary>1:1 drawing of V-Slice 0.8.1's FunkinPreloader.hx VFD display (positions, sizes, fonts, colour, VFDOverlay
/// shader), in 1280x720 logical units scaled to any destination rectangle. Shared by the Xbox/UWP first-start
/// download screen (Xbox/LoadingScene.cs) and the PC dev preview (FNF_DEV_DLPREVIEW, Scenes/DevPreloaderScene.cs).
/// Fonts: DS-Digital (fonts/DS-DIGIB.TTF, FNF data: on a Store first start it is fetched from the official repo first),
/// Inconsolata Bold/Black + Quantico (OFL, ship in fonts/ui). Missing DS-Digital falls back to Quantico Bold.</summary>
public sealed class PreloaderScreen : IDisposable
{
    public const float BaseW = 1280f, BaseH = 720f;
    private const float BarPadding = 20f;           // BAR_PADDING
    private const float BarHeight = 12f;            // BAR_HEIGHT
    private const int PiecesCount = 16;             // PIECES_COUNT
    public const float FadeTime = 2.5f;             // FADE_TIME
    private const float EllipsisTime = 0.5f;        // ELLIPSIS_TIME
    private static readonly Color BarColor = new Color(0xA4, 0xFF, 0x11);   // Constants.COLOR_PRELOADER_BAR

    private sealed class Face
    {
        public FontSystem Sys;
        public float Upm, Asc, Desc;                // head.unitsPerEm, hhea ascender / descender
        public readonly Dictionary<int, SpriteFontBase> Cache = new();
        public SpriteFontBase Get(float px)
        {
            int key = (int)Math.Round(px * 8f);
            if (!Cache.TryGetValue(key, out var f)) { f = Sys.GetFont(key / 8f); Cache[key] = f; }
            return f;
        }
        /// <summary>OpenFL sizes by em, FontStash by (ascent - descent).</summary>
        public float FsSize(float em) => em * (Asc - Desc) / Upm;
        public float LineHeight(float em) => em * (Asc - Desc) / Upm;
    }

    private readonly GraphicsDevice _gd;
    private Texture2D _px, _vfd;
    private Face _digital, _digitalFallback, _incBold, _incBlack, _quantico;
    private float _elapsed;
    private double _vfdTime;
    private float _s = 1f, _ox, _oy;

    public bool HasDigitalFont => _digital != null;
    public float Elapsed => _elapsed;

    public PreloaderScreen(GraphicsDevice gd)
    {
        _gd = gd;
        _px = new Texture2D(gd, 1, 1);
        _px.SetData(new[] { Color.White });
    }

    /// <summary>Load the fonts that are still missing from the given folders (each folder and its "ui" subfolder).</summary>
    public void LoadFonts(IEnumerable<string> fontDirs)
    {
        var dirs = new List<string>();
        foreach (var d in fontDirs) { if (string.IsNullOrEmpty(d)) continue; dirs.Add(d); dirs.Add(Path.Combine(d, "ui")); }
        _digital ??= LoadFace(dirs, "DS-DIGIB.TTF", "DS-DIGI.TTF");
        _digitalFallback ??= LoadFace(dirs, "Quantico-Bold.ttf");
        _incBold ??= LoadFace(dirs, "Inconsolata-Bold.ttf", "Quantico-Bold.ttf");
        _incBlack ??= LoadFace(dirs, "Inconsolata-Black.ttf", "Inconsolata-Bold.ttf", "Quantico-Bold.ttf");
        _quantico ??= LoadFace(dirs, "Quantico-Regular.ttf", "Quantico-Bold.ttf");
    }

    private static Face LoadFace(List<string> dirs, params string[] names)
    {
        foreach (var n in names)
            foreach (var d in dirs)
            {
                string p = Path.Combine(d, n);
                try
                {
                    if (!File.Exists(p)) continue;
                    byte[] b = File.ReadAllBytes(p);
                    if (b.Length < 64) continue;
                    var f = new Face { Sys = new FontSystem() };
                    ReadMetrics(b, out f.Upm, out f.Asc, out f.Desc);
                    f.Sys.AddFont(b);
                    return f;
                }
                catch (Exception ex) { Console.WriteLine($"[PRELOADER] font {p}: {ex.Message}"); }
            }
        return null;
    }

    private static int U16(byte[] b, int o) => (b[o] << 8) | b[o + 1];
    private static int S16(byte[] b, int o) => (short)((b[o] << 8) | b[o + 1]);

    private static void ReadMetrics(byte[] b, out float upm, out float asc, out float desc)
    {
        upm = 1000; asc = 800; desc = -200;
        int n = U16(b, 4);
        int head = -1, hhea = -1;
        for (int i = 0; i < n; i++)
        {
            int r = 12 + 16 * i;
            string tag = System.Text.Encoding.ASCII.GetString(b, r, 4);
            int off = (b[r + 8] << 24) | (b[r + 9] << 16) | (b[r + 10] << 8) | b[r + 11];
            if (tag == "head") head = off; else if (tag == "hhea") hhea = off;
        }
        if (head >= 0) upm = U16(b, head + 18);
        if (hhea >= 0) { asc = S16(b, hhea + 4); desc = S16(b, hhea + 6); }
    }

    public void Update(float dt)
    {
        // FunkinPreloader.update: elapsed = seconds since start; vfdShader.update(elapsed * 100) adds it to u_time every frame
        _elapsed += dt;
        _vfdTime += _elapsed * 100.0;
    }

    /// <summary>The cycling "." / ".." / "..." of updateGraphics.</summary>
    public string Ellipsis => new string('.', (int)(_elapsed / EllipsisTime) % 3 + 1);

    /// <summary>MathUtil.easeInOutCirc.</summary>
    public static float EaseInOutCirc(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return x < 0.5f ? (1f - MathF.Sqrt(1f - (2f * x) * (2f * x))) / 2f
                        : (MathF.Sqrt(1f - (-2f * x + 2f) * (-2f * x + 2f)) + 1f) / 2f;
    }

    // ------------------------------------------------------------------ drawing

    public void Draw(SpriteBatch sb, Rectangle dest, in PreloaderView v)
    {
        _s = dest.Width / BaseW;
        float sy = dest.Height / BaseH;
        if (sy < _s) _s = sy;
        _ox = dest.X + (dest.Width - BaseW * _s) / 2f;
        _oy = dest.Y + (dest.Height - BaseH * _s) / 2f;
        EnsureVfd();

        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp);
        sb.Draw(_px, dest, Color.Black);                                    // Constants.COLOR_PRELOADER_BG
        float a = Math.Clamp(v.Alpha, 0f, 1f);
        float w = BaseW, h = BaseH;

        // progressLines: lineStyle(2) drawRect(-2, 0, w + 4, 30) at y = h * 0.67 (only the horizontal edges are on screen)
        float ly = h * 0.67f;
        FillRectF(sb, -3f, ly - 1f, w + 6f, 2f, BarColor * a);
        FillRectF(sb, -3f, ly + 29f, w + 6f, 2f, BarColor * a);

        // progress pieces: drawRoundRect(0, 0, pieceWidth - gap, BAR_HEIGHT, 4, 4), x = i * (width + gap), alpha 0.9 / 0.1
        float pieceW = (w - BarPadding * 2f) / PiecesCount - 8f;
        int toRender = (int)(Math.Clamp(v.Progress, 0f, 1f) * PiecesCount);
        for (int i = 0; i < PiecesCount; i++)
        {
            float pa = v.Alpha < 1f ? a : (i <= toRender ? 0.9f : 0.1f);   // fade-out sets every piece to the fade alpha
            FillRoundRectF(sb, i * (pieceW + 8f), ly + 8f, pieceW, BarHeight, 2f, BarColor * pa);
        }

        // progressLeftText: DS-Digital 32 bold, x = BAR_PADDING, y = progressLines.y - textHeight * 2.5 (textHeight 32)
        var dig = _digital ?? _digitalFallback;
        float leftY = ly - 32f * 2.5f;
        string left = (v.Title ?? "") + (v.StepLine != null ? " \n" + v.StepLine : "");
        DrawTextField(sb, dig, 32f, left, BarPadding, leftY, w - BarPadding * 2f, false, BarColor * a);

        // progressRightText: DS-Digital 16, right-aligned in (BAR_PADDING, h - BAR_PADDING - BAR_HEIGHT - 16 - 4, w - 40)
        string pct = (int)Math.Floor(Math.Clamp(v.Progress, 0f, 1f) * 100f + 1e-4f) + "%";
        DrawTextField(sb, dig, 16f, pct, BarPadding, h - BarPadding - BarHeight - 16f - 4f, w - BarPadding * 2f, true, BarColor * a);

        // rTextGroup at (w * 0.64, progressLeftText.y)
        float gx = w * 0.64f, gy = leftY;
        FillRoundRectF(sb, gx, gy + 40f, 64f, 20f, 2.5f, BarColor * a);
        FillRoundRectF(sb, gx + 70f, gy + 40f, 58f, 20f, 2.5f, BarColor * a);
        FillRoundRectF(sb, gx, gy + 40f, 128f, 20f, 2.5f, BarColor * (0.1f * a));
        DrawTextField(sb, _quantico, 20f, "DSP", gx + 10f, gy + 33f, w, false, Color.Black * a);
        DrawTextField(sb, _quantico, 20f, "FNF", gx + 78f, gy + 33f, w, false, Color.Black * a);
        DrawTextField(sb, _incBlack, 16f, "ENHANCED", gx - 100f, gy + 40f, w, false, BarColor * a);
        DrawTextField(sb, _incBold, 36f, "NATURAL STEREO", gx, gy, w, false, BarColor * a);

        // port-only detail line(s) under the bar, same TextField style (DS-Digital 24)
        if (!string.IsNullOrEmpty(v.Status))
            DrawTextField(sb, dig, 24f, Wrap(dig, 24f, v.Status, w - BarPadding * 2f - 80f, 5), BarPadding, ly + 42f, w - BarPadding * 2f, false, BarColor * a);

        // vfdBitmap + VFDOverlay shader over the whole display (black, alpha from the shader)
        float flicker = (float)Math.Sin(_vfdTime) * 0.05f + 1f;
        sb.Draw(_vfd, new Rectangle((int)Math.Round(_ox), (int)Math.Round(_oy), (int)Math.Round(BaseW * _s), (int)Math.Round(BaseH * _s)),
            Color.White * (1f + (flicker - 1f) * 0.6f));
        sb.End();
    }

    /// <summary>OpenFL TextField: 2 px gutter, lines (ascent + descent) apart, LEFT or RIGHT aligned in the field width.</summary>
    private void DrawTextField(SpriteBatch sb, Face face, float em, string text, float x, float y, float width, bool right, Color c)
    {
        if (face == null || string.IsNullOrEmpty(text)) return;
        var font = face.Get(face.FsSize(em) * _s);
        float lh = face.LineHeight(em);
        string[] lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Length == 0) continue;
            float lx = x + 2f;
            if (right) lx = x + width - 2f - font.MeasureString(line.TrimEnd()).X / _s;
            // OpenFL/FreeType snaps the baseline down to the next whole pixel and the pen 1 px right of FontStash's
            // (measured on the original: every text sat 1 px right / 1-2 px lower)
            float asc = face.Asc / face.Upm * em;
            float baseY = (float)Math.Ceiling((_oy + (y + 2f + i * lh + asc) * _s) - 0.001f);
            float px = (float)Math.Round(_ox + (lx + 1f) * _s), py = baseY - (float)Math.Round(asc * _s);
            font.DrawText(sb, line, new Vector2(px, py), c);
        }
    }

    /// <summary>Word-wrap to the field width (logical px), at most maxLines lines (cached: the status changes rarely).</summary>
    private string _wrapIn, _wrapOut; private float _wrapScale; private Face _wrapFace;
    private string Wrap(Face face, float em, string text, float width, int maxLines)
    {
        if (face == null) return text;
        if (text == _wrapIn && _wrapScale == _s && _wrapFace == face) return _wrapOut;
        _wrapIn = text; _wrapScale = _s; _wrapFace = face;
        var font = face.Get(face.FsSize(em) * _s);
        var outLines = new List<string>();
        foreach (var para in text.Replace("\r", "").Split('\n'))
        {
            string cur = "";
            foreach (var word in para.Split(' '))
            {
                string t = cur.Length == 0 ? word : cur + " " + word;
                if (cur.Length > 0 && font.MeasureString(t).X / _s > width) { outLines.Add(cur); cur = word; }
                else cur = t;
            }
            outLines.Add(cur);
        }
        while (outLines.Count > 0 && outLines[outLines.Count - 1].Length == 0) outLines.RemoveAt(outLines.Count - 1);
        if (outLines.Count > maxLines) outLines.RemoveRange(maxLines, outLines.Count - maxLines);
        return _wrapOut = string.Join("\n", outLines);
    }

    /// <summary>Anti-aliased (coverage) rectangle in logical units; colour is premultiplied.</summary>
    private void FillRectF(SpriteBatch sb, float x, float y, float w, float h, Color c)
    {
        float x0 = _ox + x * _s, y0 = _oy + y * _s, x1 = _ox + (x + w) * _s, y1 = _oy + (y + h) * _s;
        int iy0 = (int)Math.Floor(y0), iy1 = (int)Math.Ceiling(y1);
        for (int row = iy0; row < iy1; row++)
        {
            float cov = Math.Min(y1, row + 1) - Math.Max(y0, row);
            if (cov > 0f) Span(sb, x0, x1, row, c * cov);
        }
    }

    /// <summary>drawRoundRect(x, y, w, h, 2r, 2r) with coverage AA, per device row.</summary>
    private void FillRoundRectF(SpriteBatch sb, float x, float y, float w, float h, float r, Color c)
    {
        float x0 = _ox + x * _s, y0 = _oy + y * _s, x1 = _ox + (x + w) * _s, y1 = _oy + (y + h) * _s, rr = r * _s;
        int iy0 = (int)Math.Floor(y0), iy1 = (int)Math.Ceiling(y1);
        for (int row = iy0; row < iy1; row++)
        {
            float cov = Math.Min(y1, row + 1) - Math.Max(y0, row);
            if (cov <= 0f) continue;
            float mid = Math.Clamp(row + 0.5f, y0, y1);
            float d = Math.Min(mid - y0, y1 - mid);              // distance to the nearest horizontal edge
            float inset = 0f;
            if (d < rr) { float k = rr - d; inset = rr - MathF.Sqrt(Math.Max(0f, rr * rr - k * k)); }
            Span(sb, x0 + inset, x1 - inset, row, c * cov);
        }
    }

    /// <summary>One device row from fx0 to fx1 with fractional end pixels.</summary>
    private void Span(SpriteBatch sb, float fx0, float fx1, int row, Color c)
    {
        if (fx1 <= fx0) return;
        int ix0 = (int)Math.Ceiling(fx0), ix1 = (int)Math.Floor(fx1);
        if (ix1 < ix0)
        {   // inside a single pixel
            sb.Draw(_px, new Rectangle(ix1, row, 1, 1), c * (fx1 - fx0));
            return;
        }
        if (ix1 > ix0) sb.Draw(_px, new Rectangle(ix0, row, ix1 - ix0, 1), c);
        if (fx0 < ix0) sb.Draw(_px, new Rectangle(ix0 - 1, row, 1, 1), c * (ix0 - fx0));
        if (fx1 > ix1) sb.Draw(_px, new Rectangle(ix1, row, 1, 1), c * (fx1 - ix1));
    }

    /// <summary>CPU bake of VFDOverlay.hx (hex grid outline + scanlines + noise) at u_time flicker 1.0.</summary>
    private void EnsureVfd()
    {
        int tw = (int)Math.Round(BaseW * _s), th = (int)Math.Round(BaseH * _s);
        tw = Math.Clamp(tw, 320, 1920); th = Math.Clamp(th, 180, 1080);   // resolution independent pattern: 1080p bake is enough on 4K
        if (_vfd != null && _vfd.Width == tw && _vfd.Height == th) return;
        _vfd?.Dispose();
        var data = new Color[tw * th];
        const float sX = 1f, sY = 1.7320508f;
        float nx = 1f / MathF.Sqrt(1f + 3f), ny = 1.7320508f / MathF.Sqrt(1f + 3f);
        for (int py = 0; py < th; py++)
        {
            float v = (py + 0.5f) / th;
            float uy = -100f * v;
            float sinshit = SmoothStep(-3f, 1f, MathF.Sin(uy * 3f));
            for (int pxi = 0; pxi < tw; pxi++)
            {
                float u = (pxi + 0.5f) / tw;
                float ux = -100f * u;
                float hcx = MathF.Floor(ux / sX) + 0.5f, hcy = MathF.Floor(uy / sY) + 0.5f;
                float hcz = MathF.Floor((ux - 0.5f) / sX) + 0.5f, hcw = MathF.Floor((uy - 1f) / sY) + 0.5f;
                float o1x = ux - hcx * sX, o1y = uy - hcy * sY;
                float o2x = ux - (hcz + 0.5f) * sX, o2y = uy - (hcw + 0.5f) * sY;
                float hx, hy;
                if (o1x * o1x + o1y * o1y < o2x * o2x + o2y * o2y) { hx = o1x; hy = o1y; } else { hx = o2x; hy = o2y; }
                float ax = MathF.Abs(hx), ay = MathF.Abs(hy);
                float edgeDist = Math.Max(ax * nx + ay * ny, ax);
                float outline = SmoothStep(0.5f - 0.11f, 0.5f, edgeDist);
                float colorMix = 0.3f * outline;
                float alpha = colorMix + (sinshit - colorMix) * 0.5f;             // mix(col, (0,0,0,sinshit), 0.5 * flicker)
                float dot = ux * 12.9898f + uy * 78.233f;
                float specs = Fract(MathF.Sin(dot) * 43758.5453f);
                alpha = alpha * 0.9f + specs * 0.1f;                               // mix(col, noise, 0.1)
                byte ab = (byte)Math.Clamp((int)(alpha * 255f + 0.5f), 0, 255);
                data[py * tw + pxi] = new Color((byte)0, (byte)0, (byte)0, ab);   // premultiplied black
            }
        }
        _vfd = new Texture2D(_gd, tw, th);
        _vfd.SetData(data);
    }

    private static float Fract(float x) => x - MathF.Floor(x);
    private static float SmoothStep(float e0, float e1, float x)
    {
        float t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    // ------------------------------------------------------------------ store first-start agreement
    public const string AgreementTitle = "BEFORE YOU DOWNLOAD";
    public const string AgreementBody = "FNF Mono Engine does not include Friday Night Funkin's art and music. To play, the game will download the official game files from The Funkin' Crew's public GitHub page (github.com/FunkinCrew/funkin.assets). The download is about 1.3 GB and uses about 1.3 GB of storage.";
    public const string AgreementLead = "By pressing AGREE you confirm that:";
    public static readonly string[] AgreementBullets =
    {
        "these files belong to The Funkin' Crew Inc. and are covered by their license;",
        "you will use them only to play this game, and will not sell them or share them on their own.",
    };
    public static readonly string[] AgreementButtons = { "AGREE AND DOWNLOAD", "EXIT" };
    private const float AgrX = 80f, AgrW = BaseW - 160f, AgrBtnY = 574f, AgrBtnH = 58f, AgrBtnGap = 40f;
    private static readonly float[] AgrBtnW = { 420f, 220f };
    private Texture2D[] _glyphs;
    private readonly Dictionary<string, string> _wrapCache = new();

    /// <summary>Xbox A / B glyph art (images/ui/prompts, shipped in the Store package) from the first folder that has it.</summary>
    public void LoadGlyphs(IEnumerable<string> promptDirs)
    {
        if (_glyphs != null) return;
        var g = new Texture2D[2];
        string[] names = { "xbox_a.png", "xbox_b.png" };
        foreach (var d in promptDirs)
            for (int i = 0; i < 2; i++)
            {
                if (g[i] != null || string.IsNullOrEmpty(d)) continue;
                try
                {
                    string p = Path.Combine(d, names[i]);
                    if (!File.Exists(p)) continue;
                    using (var s = File.OpenRead(p)) g[i] = Texture2D.FromStream(_gd, s);
                    // straight PNG -> premultiplied (this screen draws with BlendState.AlphaBlend)
                    var px = new Color[g[i].Width * g[i].Height];
                    g[i].GetData(px);
                    for (int k = 0; k < px.Length; k++) { var c = px[k]; px[k] = new Color(c.R * c.A / 255, c.G * c.A / 255, c.B * c.A / 255, (int)c.A); }
                    g[i].SetData(px);
                }
                catch (Exception ex) { Console.WriteLine("[PRELOADER] glyph " + names[i] + ": " + ex.Message); }
            }
        _glyphs = g;
    }

    private static float AgrBtnX(int i) => BaseW / 2f - (AgrBtnW[0] + AgrBtnGap + AgrBtnW[1]) / 2f + (i == 0 ? 0f : AgrBtnW[0] + AgrBtnGap);

    /// <summary>Button <paramref name="i"/> (0 AGREE, 1 EXIT) in device pixels for the destination rectangle (mouse hit test).</summary>
    public Rectangle AgreementButtonRect(int i, Rectangle dest)
    {
        float s = Math.Min(dest.Width / BaseW, dest.Height / BaseH);
        float ox = dest.X + (dest.Width - BaseW * s) / 2f, oy = dest.Y + (dest.Height - BaseH * s) / 2f;
        return new Rectangle((int)(ox + AgrBtnX(i) * s), (int)(oy + AgrBtnY * s), (int)(AgrBtnW[i] * s), (int)(AgrBtnH * s));
    }

    /// <summary>Store first-start agreement in the same VFD display look (black, #A4FF11, DS-Digital title (Quantico Bold
    /// until DS-Digital is fetched), Inconsolata body, rounded green bars, VFD overlay). <paramref name="focus"/> = focused
    /// button, <paramref name="pad"/> = Xbox glyphs instead of keycaps.</summary>
    public void DrawAgreement(SpriteBatch sb, Rectangle dest, int focus, bool pad, float alpha = 1f)
    {
        _s = Math.Min(dest.Width / BaseW, dest.Height / BaseH);
        _ox = dest.X + (dest.Width - BaseW * _s) / 2f;
        _oy = dest.Y + (dest.Height - BaseH * _s) / 2f;
        EnsureVfd();
        float a = Math.Clamp(alpha, 0f, 1f);
        var green = BarColor * a;

        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp);
        sb.Draw(_px, dest, Color.Black);

        // title (progressLeftText style) + a progressLines-style rule under it
        var dig = _digital ?? _digitalFallback;
        DrawTextField(sb, dig, 48f, AgreementTitle, AgrX - 2f, 50f, AgrW, false, green);
        FillRectF(sb, -3f, 122f, BaseW + 6f, 2f, green);

        // body (Inconsolata Bold 26: TV distance)
        const float em = 26f;
        var body = _incBold ?? _quantico;
        float lh = body != null ? body.LineHeight(em) : 26f;
        float y = 150f;
        string para = WrapCached(body, em, AgreementBody, AgrW);
        DrawTextField(sb, body, em, para, AgrX - 2f, y, AgrW, false, green);
        y += lh * CountLines(para) + 24f;
        DrawTextField(sb, _incBlack ?? body, em, AgreementLead, AgrX - 2f, y, AgrW, false, green);
        y += lh + 10f;
        foreach (var b in AgreementBullets)
        {
            string bt = WrapCached(body, em, b, AgrW - 34f);
            FillRoundRectF(sb, AgrX + 8f, y + lh * 0.5f - 3f, 8f, 8f, 4f, green);   // bullet
            DrawTextField(sb, body, em, bt, AgrX + 32f, y, AgrW - 34f, false, green);
            y += lh * CountLines(bt) + 10f;
        }

        // button row between two progressLines-style rules
        FillRectF(sb, -3f, AgrBtnY - 22f, BaseW + 6f, 2f, green);
        FillRectF(sb, -3f, AgrBtnY + AgrBtnH + 20f, BaseW + 6f, 2f, green);
        for (int i = 0; i < 2; i++)
        {
            bool f = i == focus;
            float bx = AgrBtnX(i), by = AgrBtnY, bw = AgrBtnW[i], bh = AgrBtnH;
            FillRoundRectF(sb, bx, by, bw, bh, 4f, green);
            if (!f) FillRoundRectF(sb, bx + 3f, by + 3f, bw - 6f, bh - 6f, 2.5f, Color.Black * a);
            var tc = f ? Color.Black * a : green;
            var bf = _incBlack ?? body;
            float tw = bf != null ? bf.Get(bf.FsSize(24f) * _s).MeasureString(AgreementButtons[i]).X / _s : 0f;
            const float gs = 34f;
            float kw = pad ? gs : KeycapWidth(i == 0 ? "ENTER" : "ESC");
            float total = kw + 12f + tw, left = bx + bw / 2f - total / 2f;
            if (pad && _glyphs != null && _glyphs[i] != null)
                sb.Draw(_glyphs[i], new Rectangle((int)Math.Round(_ox + left * _s), (int)Math.Round(_oy + (by + (bh - gs) / 2f) * _s), (int)Math.Round(gs * _s), (int)Math.Round(gs * _s)), Color.White * a);
            else DrawKeycap(sb, i == 0 ? "ENTER" : "ESC", left, by + (bh - 30f) / 2f, f, a);
            float th = bf != null ? bf.LineHeight(24f) : 28f;
            DrawTextField(sb, bf, 24f, AgreementButtons[i], left + kw + 12f - 3f, by + (bh - th) / 2f - 2f, BaseW, false, tc);
        }

        float flicker = (float)Math.Sin(_vfdTime) * 0.05f + 1f;
        sb.Draw(_vfd, new Rectangle((int)Math.Round(_ox), (int)Math.Round(_oy), (int)Math.Round(BaseW * _s), (int)Math.Round(BaseH * _s)),
            Color.White * (1f + (flicker - 1f) * 0.6f));
        sb.End();
    }

    private float KeycapWidth(string label)
    {
        var f = _incBlack ?? _incBold ?? _quantico;
        float tw = f != null ? f.Get(f.FsSize(15f) * _s).MeasureString(label).X / _s : label.Length * 9f;
        return Math.Max(30f, tw + 16f);
    }

    /// <summary>Keyboard key as a small rounded VFD box (green when the button is idle, black on the lit focused button).</summary>
    private void DrawKeycap(SpriteBatch sb, string label, float x, float y, bool onLit, float a)
    {
        float w = KeycapWidth(label), h = 30f;
        var edge = onLit ? Color.Black * a : BarColor * a;
        FillRoundRectF(sb, x, y, w, h, 4f, edge);
        FillRoundRectF(sb, x + 2f, y + 2f, w - 4f, h - 4f, 3f, onLit ? BarColor * a : Color.Black * a);
        var f = _incBlack ?? _incBold ?? _quantico;
        if (f == null) return;
        float tw = f.Get(f.FsSize(15f) * _s).MeasureString(label).X / _s;
        DrawTextField(sb, f, 15f, label, x + (w - tw) / 2f - 3f, y + (h - f.LineHeight(15f)) / 2f - 2f, BaseW, false, edge);
    }

    private static int CountLines(string s) => string.IsNullOrEmpty(s) ? 0 : s.Split('\n').Length;

    private string WrapCached(Face face, float em, string text, float width)
    {
        if (face == null) return text;
        string key = text + "|" + em + "|" + width + "|" + _s + "|" + face.GetHashCode();
        if (_wrapCache.TryGetValue(key, out var r)) return r;
        var font = face.Get(face.FsSize(em) * _s);
        var lines = new List<string>();
        string cur = "";
        foreach (var word in text.Split(' '))
        {
            string t = cur.Length == 0 ? word : cur + " " + word;
            if (cur.Length > 0 && font.MeasureString(t).X / _s > width) { lines.Add(cur); cur = word; }
            else cur = t;
        }
        if (cur.Length > 0) lines.Add(cur);
        if (_wrapCache.Count > 64) _wrapCache.Clear();
        return _wrapCache[key] = string.Join("\n", lines);
    }

    public void Dispose()
    {
        _px?.Dispose(); _px = null;
        _vfd?.Dispose(); _vfd = null;
        if (_glyphs != null) foreach (var g in _glyphs) g?.Dispose();
        _glyphs = null;
    }
}
