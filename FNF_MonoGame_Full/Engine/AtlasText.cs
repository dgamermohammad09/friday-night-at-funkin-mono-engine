using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Port of V-Slice funkin.ui.AtlasText / AtlasChar / AtlasFontData (ui/AtlasText.hx) with the ORIGINAL font atlases
/// (images/fonts/bold + default copied to Content/fonts/vslice). Every char is an animated sprite
/// (animation.addByPrefix(prefix, 24 fps, looped)); x advances by the char's frame width, space = 40 px,
/// y = maxHeight - charHeight (bottom aligned), newline = maxHeight. Chars missing from the atlas fall back to the
/// port's old alphabet_bold glyph (the original would show the atlas' first frame).
/// </summary>
public sealed class AtlasText
{
    private sealed class Frame { public Rectangle Src; public int Fx, Fy, Fw, Fh; }
    private readonly Texture2D _tex;
    private readonly List<(string name, int num, Frame f)> _all = new();
    private readonly Dictionary<string, List<Frame>> _anims = new();
    public float MaxHeight { get; private set; }
    public bool UpperOnly { get; private set; }

    private static AtlasText _bold, _default;
    private static bool _boldTried, _defaultTried;
    public static AtlasText Bold { get { if (!_boldTried) { _boldTried = true; _bold = Load("bold"); } return _bold; } }
    public static AtlasText Default { get { if (!_defaultTried) { _defaultTried = true; _default = Load("default"); } return _default; } }

    private AtlasText(Texture2D tex) { _tex = tex; }

    private static AtlasText Load(string name)
    {
        try
        {
            var assets = FNFGame.Instance.Assets;
            var tex = assets.LoadTexture($"fonts/vslice/{name}.png");
            string xml = assets.ResolvePath($"fonts/vslice/{name}.xml")
                         ?? System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot, "fonts", "vslice", name + ".xml");
            if (tex == null || tex == assets.Pixel || !global::FNF_MonoGame.Engine.FsCI.FileExists(xml)) return null;
            var at = new AtlasText(tex);
            bool up = false, low = false;
            var num = new Regex(@"^(.*?)(\d+)$");
            // regex instead of XDocument: the original default.xml has a raw '&' in a name (invalid XML, flixel parses it anyway)
            foreach (Match st in Regex.Matches(global::FNF_MonoGame.Engine.FsCI.ReadAllText(xml), @"<SubTexture\s([^>]*?)/?>"))
            {
                var attrs = new Dictionary<string, string>();
                foreach (Match am in Regex.Matches(st.Groups[1].Value, @"(\w+)=""([^""]*)""")) attrs[am.Groups[1].Value] = am.Groups[2].Value;
                string n = attrs.TryGetValue("name", out var nn) ? nn.Replace("&amp;", "&").Replace("&quot;", "\"").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&apos;", "'") : "";
                int I(string a, int d) => attrs.TryGetValue(a, out var sv) && int.TryParse(sv, out var v) ? v : d;
                int w = I("width", 0), h = I("height", 0);
                var f = new Frame { Src = new Rectangle(I("x", 0), I("y", 0), w, h), Fx = I("frameX", 0), Fy = I("frameY", 0), Fw = I("frameWidth", w), Fh = I("frameHeight", h) };
                var m = num.Match(n);
                at._all.Add((n, m.Success ? int.Parse(m.Groups[2].Value) : 0, f));
                at.MaxHeight = Math.Max(at.MaxHeight, h);   // frame.frame.height
                if (Regex.IsMatch(n, @"^[A-Z]\d+$")) up = true;
                if (Regex.IsMatch(n, @"^[a-z]\d+$")) low = true;
            }
            at.UpperOnly = up && !low;
            return at;
        }
        catch (Exception e) { Console.WriteLine("[AtlasText] " + e.Message); return null; }
    }

    private static string Prefix(char c) => c switch
    {
        '&' => "-andpersand-", '\'' => "-apostraphie-", '\\' => "-back slash-", ',' => "-comma-", '-' => "-dash-",
        '\u2193' => "-down arrow-", '\u201D' => "-end quote-", '!' => "-exclamation point-", '/' => "-forward slash-",
        '>' => "-greater than-", '\u2665' => "-heart-", '\u2661' => "-heart-", '\u2190' => "-left arrow-", '<' => "-less than-",
        '*' => "-multiply x-", '.' => "-period-", '?' => "-question mark-", '\u2192' => "-right arrow-", '\u201C' => "-start quote-",
        '\u2191' => "-up arrow-", _ => c.ToString()
    };

    /// <summary>flixel addByPrefix: frames whose name starts with the prefix, ordered by their numeric suffix.</summary>
    private List<Frame> Anim(char c)
    {
        string p = Prefix(c);
        if (_anims.TryGetValue(p, out var l)) return l;
        var found = new List<(int, Frame)>();
        foreach (var (name, n, f) in _all) if (name.StartsWith(p, StringComparison.Ordinal)) found.Add((n, f));
        found.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        l = found.ConvertAll(t => t.Item2);
        _anims[p] = l;
        return l;
    }

    private string Cased(string s) => UpperOnly ? s.ToUpperInvariant() : s;

    /// <summary>AtlasText.getWidth() (widest line for multi-line text).</summary>
    public float Width(string text, float scale = 1f)
    {
        float w = 0, best = 0;
        foreach (char c in Cased(text ?? ""))
        {
            if (c == ' ') w += 40;
            else if (c == '\n') { best = Math.Max(best, w); w = 0; }
            else w += CharWidth(c);
        }
        return Math.Max(best, w) * scale;
    }

    private float CharWidth(char c)
    {
        var a = Anim(c);
        if (a.Count > 0) return a[0].Fw;
        var g = AlphabetFont.Bold?.GetGlyph(c);
        return g?.FrameWidth ?? 40;
    }

    /// <summary>Draw like an AtlasText group at (x, y) (group top-left), alpha multiplied, chars animating at 24 fps.</summary>
    public void Draw(SpriteBatch sb, string text, float x, float y, float alpha = 1f, float time = 0f, float scale = 1f, Color? tint = null)
    {
        if (string.IsNullOrEmpty(text) || alpha <= 0f) return;
        var col = new Color(tint ?? Color.White, alpha);
        float xp = 0, yp = 0;
        int tick = (int)(time * 24f);
        foreach (char c in Cased(text))
        {
            if (c == ' ') { xp += 40; continue; }
            if (c == '\n') { xp = 0; yp += MaxHeight; continue; }
            var a = Anim(c);
            if (a.Count == 0)
            {   // missing glyph (e.g. digits in 'bold'): old port alphabet glyph, bottom aligned
                var g = AlphabetFont.Bold?.GetGlyph(c);
                if (g != null && AlphabetFont.Bold.Texture != null)
                {
                    float gy = yp + MaxHeight - g.FrameHeight;
                    sb.Draw(AlphabetFont.Bold.Texture, new Vector2(x + (xp + g.Offset.X) * scale, y + (gy + g.Offset.Y) * scale), g.SourceRect, col, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                    xp += g.FrameWidth;
                }
                else xp += 40;
                continue;
            }
            var f0 = a[0]; var f = a[tick % a.Count];
            float cy = yp + MaxHeight - f0.Fh;           // charSprite.y = yPos + maxHeight - height (height from frame 0)
            sb.Draw(_tex, new Vector2(x + (xp - f.Fx) * scale, y + (cy - f.Fy) * scale), f.Src, col, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            xp += f0.Fw;
        }
    }

    /// <summary>screenCenter(X) helper.</summary>
    public void DrawCentered(SpriteBatch sb, string text, float y, float alpha = 1f, float time = 0f, float scale = 1f, Color? tint = null)
        => Draw(sb, text, (FNFGame.SCREEN_WIDTH - Width(text, scale)) / 2f, y, alpha, time, scale, tint);
}
