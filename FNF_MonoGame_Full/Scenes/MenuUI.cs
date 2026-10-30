using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Shared drawing helpers for the port-only menus (Mods, Achievements, Options) so they use the original game's
/// look: menuDesat background tinted like OptionsState/FreeplayState backgrounds, main-menu atlas words as titles,
/// Alphabet bold text, VCR OSD Mono with black outline, black translucent panels with a white border.
/// </summary>
public static class MenuUI
{
    public static readonly Color DefaultTint = new Color(0x92, 0x71, 0xFD);   // FlxColor 0xFF9271FD (options/menu purple)

    /// <summary>menuDesat sized to 1.2x the screen (like MainMenuState's bg) and tinted; slow vertical parallax.</summary>
    public static void DrawBackground(SpriteBatch sb, Texture2D desat, Color tint, float scrollY = 0f)
    {
        if (desat == null) { sb.Draw(FNFGame.Instance.Assets.Pixel, new Rectangle(0, 0, 1280, 720), tint); return; }
        float w = (int)(1280 * 1.2f), scale = w / desat.Width, h = desat.Height * scale;
        sb.Draw(desat, new Vector2((1280 - w) / 2f, (720 - h) / 2f - scrollY * 0.17f), null, tint, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }

    /// <summary>Draw a main-menu atlas word ("mods selected", "achievements idle"...) looping at 24 fps.</summary>
    public static void DrawAtlasWord(SpriteBatch sb, SpriteSheet sheet, string anim, float x, float y, float scale, float time, float alpha = 1f, bool centered = false)
    {
        var frames = sheet?.GetAnimation(anim);
        if (frames == null || frames.Count == 0) return;
        var f = frames[(int)(time * 24f) % frames.Count];
        int fw = f.FrameWidth > 0 ? f.FrameWidth : f.SourceRect.Width, fh = f.FrameHeight > 0 ? f.FrameHeight : f.SourceRect.Height;
        if (centered) { x -= fw * scale / 2f; y -= fh * scale / 2f; }
        sb.Draw(f.Texture ?? sheet.Texture, new Vector2(x + f.Offset.X * scale, y + f.Offset.Y * scale), f.SourceRect, new Color(Color.White, alpha), 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }

    public static float AtlasWordWidth(SpriteSheet sheet, string anim)
    {
        var frames = sheet?.GetAnimation(anim);
        if (frames == null || frames.Count == 0) return 0;
        return frames[0].FrameWidth > 0 ? frames[0].FrameWidth : frames[0].SourceRect.Width;
    }

    /// <summary>Black translucent panel with a white border (FNF prompt/box look).</summary>
    public static void Panel(SpriteBatch sb, Rectangle r, float alpha = 1f, int border = 4, Color? fill = null, Color? edge = null)
    {
        var px = FNFGame.Instance.Assets.Pixel;
        var e = new Color(edge ?? Color.White, alpha);
        sb.Draw(px, new Rectangle(r.X + 6, r.Y + 8, r.Width, r.Height), new Color(Color.Black, 0.35f * alpha));   // drop shadow
        sb.Draw(px, r, e);
        sb.Draw(px, new Rectangle(r.X + border, r.Y + border, r.Width - 2 * border, r.Height - 2 * border), new Color(fill ?? new Color(12, 10, 24), 0.88f * alpha));
    }

    public static void Rect(SpriteBatch sb, Rectangle r, Color c) => sb.Draw(FNFGame.Instance.Assets.Pixel, r, c);

    public static void Text(SpriteBatch sb, int size, string text, float x, float y, Color color, float alpha = 1f, int border = 2, float align = 0f)
    {
        var font = FNFGame.Instance.Assets.GetFont(size);
        if (font == null || string.IsNullOrEmpty(text)) return;
        if (align != 0f) x -= font.MeasureString(text).X * align;
        ButtonPrompts.OutlinedText(sb, font, text, new Vector2(x, y), color, alpha, border);
    }

    public static float TextWidth(int size, string text) => FNFGame.Instance.Assets.GetFont(size)?.MeasureString(text ?? "").X ?? 0;

    /// <summary>Word-wrap text for a VCR font size into lines no wider than <paramref name="width"/>.</summary>
    public static List<string> Wrap(int size, string text, float width)
    {
        var lines = new List<string>();
        var font = FNFGame.Instance.Assets.GetFont(size);
        if (font == null || string.IsNullOrEmpty(text)) return lines;
        foreach (var para in text.Replace("\r", "").Split('\n'))
        {
            string cur = "";
            foreach (var word in para.Split(' '))
            {
                string tryS = cur.Length == 0 ? word : cur + " " + word;
                if (font.MeasureString(tryS).X > width && cur.Length > 0) { lines.Add(cur); cur = word; }
                else cur = tryS;
            }
            lines.Add(cur);
        }
        return lines;
    }

    /// <summary>Alphabet bold string clipped/scaled to fit a max width.</summary>
    public static void Alphabet(SpriteBatch sb, string text, float x, float y, float scale, float maxWidth, Color color)
    {
        var a = AlphabetFont.Bold;
        if (a == null) { Text(sb, (int)(48 * scale), text, x, y, color); return; }
        float w = a.MeasureWidth(text, scale);
        if (maxWidth > 0 && w > maxWidth) scale *= maxWidth / w;
        a.DrawString(sb, text, new Vector2(x, y), color, scale);
    }

    /// <summary>Progress bar with outline.</summary>
    public static void Bar(SpriteBatch sb, Rectangle r, float t, Color fill, float alpha = 1f)
    {
        Rect(sb, new Rectangle(r.X - 3, r.Y - 3, r.Width + 6, r.Height + 6), new Color(Color.Black, alpha));
        Rect(sb, r, new Color(60, 60, 70, (int)(255 * alpha)));
        Rect(sb, new Rectangle(r.X, r.Y, (int)(r.Width * MathHelper.Clamp(t, 0, 1)), r.Height), new Color(fill, alpha));
    }

    public static Texture2D LoadFileTexture(GraphicsDevice gd, string path)
    {
        try { using var fs = global::FNF_MonoGame.Engine.FsCI.OpenRead(path); return Texture2D.FromStream(gd, fs); }
        catch { return null; }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Original OptionsState look (shared by Options pages and the port-only Multiplayer screens)
    // ---------------------------------------------------------------------------------------------------------------
    private static Texture2D _optionsBg;

    /// <summary>OptionsState menuBG with HSVShader(-0.6, 0.9, 3.6) (shaders/hsv.frag) baked on the CPU once.</summary>
    public static Texture2D OptionsBackground()
    {
        if (_optionsBg != null && !_optionsBg.IsDisposed) return _optionsBg;
        var game = FNFGame.Instance;
        var src = game.Assets.LoadTexture("menus/main_menu/background.png");
        if (src == null || src == game.Assets.Pixel) return null;
        var px = new Color[src.Width * src.Height];
        src.GetData(px);
        const float H = -0.6f, S = 0.9f, V = 3.6f;
        for (int i = 0; i < px.Length; i++)
        {
            float r = px[i].R / 255f, g = px[i].G / 255f, b = px[i].B / 255f;
            // rgb2hsv (hsv.frag)
            float kx = 0f, ky = -1f / 3f, kz = 2f / 3f, kw = -1f;
            bool s1 = g >= b; // step(c.b, c.g)
            float p0 = s1 ? g : b, p1 = s1 ? b : g, p2 = s1 ? kx : kw, p3 = s1 ? ky : kz;
            bool s2 = r >= p0; // step(p.x, c.r)
            float q0 = s2 ? r : p0, q1 = p1, q2 = s2 ? p2 : p3, q3 = s2 ? p0 : r;
            float d = q0 - Math.Min(q3, q1), e = 1e-10f;
            float h = Math.Abs(q2 + (q3 - q1) / (6f * d + e)), s = d / (q0 + e), v = q0;
            h *= H; s *= S; v *= V; v *= H * 0.5f + 0.5f;
            // hsv2rgb
            float Ch(float k) { float f = h + k; f -= (float)Math.Floor(f); float p = Math.Abs(f * 6f - 3f); return v * MathHelper.Lerp(1f, MathHelper.Clamp(p - 1f, 0f, 1f), s); }
            px[i] = new Color(MathHelper.Clamp(Ch(1f), 0, 1), MathHelper.Clamp(Ch(2f / 3f), 0, 1), MathHelper.Clamp(Ch(1f / 3f), 0, 1), px[i].A / 255f);
        }
        _optionsBg = new Texture2D(game.GraphicsDevice, src.Width, src.Height);
        _optionsBg.SetData(px);
        return _optionsBg;
    }

    /// <summary>menuBG.setGraphicSize(FlxG.width * 1.1) + updateHitbox + screenCenter, scrollFactor 0.</summary>
    public static void DrawOptionsBackground(SpriteBatch sb)
    {
        var bg = OptionsBackground();
        if (bg == null) { Rect(sb, new Rectangle(0, 0, 1280, 720), new Color(180, 80, 130)); return; }
        float w = (int)(1280 * 1.1f), sc = w / bg.Width, h = bg.Height * sc;
        sb.Draw(bg, new Vector2((int)((1280 - w) / 2f), (int)((720 - h) / 2f)), null, Color.White, 0f, Vector2.Zero, sc, SpriteEffects.None, 0f);
    }

    /// <summary>FlxCamera.follow(target, null, lerp) on the Y axis with a deadzone and minScrollY (flixel updateFollow).</summary>
    public sealed class FollowCam
    {
        public float ScrollY, Lerp = 0.085f, DeadY, DeadH, MinScrollY = float.NegativeInfinity;
        public FollowCam(float deadY, float deadH, float minScrollY, float lerp) { DeadY = deadY; DeadH = deadH; MinScrollY = minScrollY; Lerp = lerp; }
        private float Target(float ty, float th, float cur)
        {
            float t = cur;
            float edge = ty - DeadY; if (t > edge) t = edge;
            edge = ty + th - DeadY - DeadH; if (t < edge) t = edge;
            return Math.Max(t, MinScrollY);
        }
        public void Update(float targetY, float targetH, float dt) { ScrollY += (Target(targetY, targetH, ScrollY) - ScrollY) * (1f - (float)Math.Pow(1f - Lerp, dt * 60f)); }
        public void Snap(float targetY, float targetH) { ScrollY = Target(targetY, targetH, ScrollY); }
    }

    /// <summary>TextMenuList of BOLD AtlasText items, screenCenter(X), y = startY + i * spacing; MenuTypedItem idle alpha 0.6 / selected 1.</summary>
    public static void DrawTextMenu(SpriteBatch sb, IList<string> items, int sel, float startY, float spacing, float scrollY, float time, float alpha = 1f)
    {
        var font = AtlasText.Bold;
        for (int i = 0; i < items.Count; i++)
        {
            float y = startY + i * spacing - scrollY;
            if (y < -120 || y > 760) continue;
            if (font != null) font.DrawCentered(sb, items[i], y, (i == sel ? 1f : 0.6f) * alpha, time);
            else Alphabet(sb, items[i], 640 - 200, y, 1f, 0, Color.White * ((i == sel ? 1f : 0.6f) * alpha));
        }
    }

    /// <summary>PreferencesMenu itemDesc: FlxText(0, 0, 1180, desc, 32) VCR, CENTER, OUTLINE black borderSize 3,
    /// screenCenter() then y += 270; box = black alpha 0.6 at (x-10, y-10, width+20, height+25).</summary>
    public static void DescriptionBox(SpriteBatch sb, string desc)
    {
        if (string.IsNullOrWhiteSpace(desc)) return;
        const int size = 32;
        float adv = FlxTextAdvance(size), lh = FlxTextLineHeight(size);
        int maxChars = (int)((1180 - 4) / adv);
        var lines = new List<string>();
        foreach (var para in desc.Replace("\r", "").Split('\n'))
        {
            string cur = "";
            foreach (var word in para.Split(' '))
            {
                string t = cur.Length == 0 ? word : cur + " " + word;
                if (t.Length > maxChars && cur.Length > 0) { lines.Add(cur); cur = word; } else cur = t;
            }
            lines.Add(cur);
        }
        float h = lines.Count * lh + 4f;                       // FlxText height (2 px gutters)
        float x = (1280 - 1180) / 2f, y = (720 - h) / 2f + 270f;
        Rect(sb, new Rectangle((int)(x - 10), (int)(y - 10), 1180 + 20, (int)(h + 25)), new Color(Color.Black, 0.6f));
        float ly = y;
        foreach (var l in lines)
        {
            float lx = x + (float)Math.Floor((1180 - 4 - l.Length * adv) / 2f);
            for (int d = 1; d <= 3; d++)                          // FlxTextBorderStyle.OUTLINE, borderSize 3 (3 iterations)
                for (int oy = -1; oy <= 1; oy++)
                    for (int ox = -1; ox <= 1; ox++)
                        if (ox != 0 || oy != 0) FlxText(sb, size, l, lx + ox * d, ly + oy * d, Color.Black);
            FlxText(sb, size, l, lx, ly, Color.White);
            ly += lh;
        }
    }

    /// <summary>OpenFL/FlxText rendering of VCR OSD Mono (vcr.ttf: unitsPerEm 2048, ascent 1800, advance 1200): OpenFL
    /// sizes by em, FontStash by ascent -> FontStash size = size*1800/2048; monospace advance size*1200/2048.
    /// x/y = FlxText position (2 px gutter). DY calibrates the glyph top against the original (measured).</summary>
    public static float FlxTextAdvance(int size) => (float)Math.Round(size * 1200f / 2048f);   // OpenFL whole-pixel advance (19 at 32, 9 at 16; measured)
    public static float FlxTextWidth(int size, string text) => (text?.Length ?? 0) * FlxTextAdvance(size) + 4f;
    public static float FlxTextLineHeight(int size) => (float)Math.Floor(size * 1800f / 2048f) + 1f;   // 29 px at 32 (measured on the original)
    public static float FlxTextDY = 0f;
    public static void FlxText(SpriteBatch sb, int size, string text, float x, float y, Color color)
    {
        if (string.IsNullOrEmpty(text)) return;
        var font = FNFGame.Instance.Assets.GetFont((int)Math.Round(size * 1800f / 2048f));
        if (font == null) return;
        float adv = FlxTextAdvance(size);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == ' ') continue;
            font.DrawText(sb, text[i].ToString(), new Vector2((float)Math.Round(x + 2 + i * adv), (float)Math.Round(y + 2 + FlxTextDY)), color);
        }
    }

    /// <summary>Exponential smoothing that is framerate independent (FlxMath.lerp with elapsed*60 adjust).</summary>
    public static float Lerp(float cur, float target, float perFrame, float dt) => cur + (target - cur) * (1f - (float)Math.Pow(1f - perFrame, dt * 60f));
}
