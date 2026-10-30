using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Port of funkin.ui.PixelatedIcon (Freeplay capsule + CharSelect grid icon):
/// sparrow atlas freeplay/icons/&lt;id&gt;pixel, antialiasing false, anims idle (idle0, 10 fps loop),
/// confirm (confirm0, 10 fps once -> confirm-hold), confirm-hold (10 fps loop).
/// Also bakes CharSelectSubState.selectedBizz (two openfl DropShadowFilters) on the unscaled frame,
/// like FlxFilteredSprite.filterFrame + FlxAnimateFilterRenderer.applyFilter.
/// </summary>
public sealed class PixelatedIcon
{
    public const string IconDir = "menus/freeplay/icons/";

    public SpriteSheet Sheet;
    public string CurAnim = "idle";
    public int CurFrame;
    public bool Reversed;
    public bool Finished;
    float _timer;
    /// <summary>Extra one-shot finish callback (CharSelect: reversed confirm -> idle, then removeAll).</summary>
    public Action<string> ExtraOnFinish;
    /// <summary>PixelatedIcon.setCharacter onFinish (confirm -> confirm-hold). Removed by CharSelect's removeAll().</summary>
    public bool OwnOnFinish = true;

    /// <summary>PixelatedIcon.setCharacter: longest '-' prefix of the id that has an &lt;id&gt;pixel image, or null (icon invisible).</summary>
    public static string ResolveIconName(string charId)
    {
        if (string.IsNullOrEmpty(charId)) return null;
        string[] parts = charId.Split('-');
        string iconName = "", lastValid = "";
        for (int i = 0; i < parts.Length; i++)
        {
            iconName += parts[i];
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(Path.Combine(AssetManager.ContentRoot, IconDir + iconName + "pixel.png")) || (VSliceMods.Any && VSliceMods.Resolve(IconDir + iconName + "pixel.png") != null)) lastValid = iconName;
            if (i < parts.Length - 1) iconName += "-";
        }
        if (lastValid.Length == 0) return null;
        return lastValid;
    }

    /// <summary>PixelatedIcon origin.x override (setCharacter switch).</summary>
    public static float OriginXFor(string charId) => charId switch
    {
        "parents-christmas" => 140f,
        "sserafim-kazuha" => 195f,
        _ => 100f,
    };

    List<SpriteFrame> Frames(string anim) => Sheet?.GetAnimation(anim);

    /// <summary>FlxAnimationController.play(name, force, reversed).</summary>
    public void Play(string anim, bool force = false, bool reversed = false)
    {
        var fr = Frames(anim);
        if (fr == null || fr.Count == 0) return;
        if (!force && anim == CurAnim && !Finished) { Reversed = reversed; return; }
        CurAnim = anim; Reversed = reversed; Finished = false; _timer = 0;
        CurFrame = reversed ? fr.Count - 1 : 0;
    }

    /// <summary>FlxAnimation.update (frameRate 10).</summary>
    public void Update(float elapsed)
    {
        var fr = Frames(CurAnim);
        if (fr == null || fr.Count == 0 || Finished) return;
        bool looped = CurAnim != "confirm";
        const float dur = 1f / 10f;
        _timer += elapsed;
        while (_timer > dur && !Finished)
        {
            _timer -= dur;
            bool atEnd = Reversed ? CurFrame == 0 : CurFrame == fr.Count - 1;
            if (atEnd)
            {
                if (looped) CurFrame = Reversed ? fr.Count - 1 : 0;
                else { Finished = true; OnFinish(CurAnim); }
            }
            else CurFrame += Reversed ? -1 : 1;
        }
    }

    void OnFinish(string name)
    {
        if (OwnOnFinish && name == "confirm") Play("confirm-hold");
        var extra = ExtraOnFinish;
        if (extra != null) extra(name);
    }

    public SpriteFrame Frame
    {
        get
        {
            var fr = Frames(CurAnim);
            if (fr == null || fr.Count == 0) return null;
            return fr[Math.Clamp(CurFrame, 0, fr.Count - 1)];
        }
    }

    /// <summary>
    /// flixel drawComplex: frame-local point p goes to (x - offset) + origin + (p - origin) * scale.
    /// Draws the frame (or its baked filtered version, which starts 2 px up-left of the trimmed frame).
    /// Caller's batch must use PointClamp (antialiasing = false) + NonPremultiplied.
    /// </summary>
    public void Draw(SpriteBatch sb, float x, float y, Vector2 offset, Vector2 origin, Vector2 scale, Color color, bool filtered = false)
    {
        var f = Frame;
        if (f == null || Sheet == null) return;
        Texture2D tex = f.Texture ?? Sheet.Texture;
        Rectangle? src = f.SourceRect;
        Vector2 local = f.Offset; // trimmed top-left inside the frame (-frameX, -frameY)
        if (filtered)
        {
            var baked = GetBaked(tex, f);
            if (baked != null) { tex = baked; src = null; local -= new Vector2(BakePad, BakePad); }
        }
        var pos = new Vector2(x - offset.X + origin.X + (local.X - origin.X) * scale.X,
                              y - offset.Y + origin.Y + (local.Y - origin.Y) * scale.Y);
        sb.Draw(tex, pos, src, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }

    // ---------------- selectedBizz bake ----------------
    // new DropShadowFilter(0, 0, 0xFFFFFF, 1, 2, 2, 19, 1)  -> white glow, strength 19
    // new DropShadowFilter(5, 45, 0x000000, 1, 2, 2, 1, 1)   -> black shadow at (int(5cos45), int(5sin45)) = (3,3)
    // openfl: blur passes = round(blur*quality/4)+1 = 2 per axis, radius blur*0.5 = 1 px, 7-tap shader
    // (offsets 0.5/0.75/1.0 * r, weights .39894 / .24197 / .05399 / .00443), linear sampling, RGBA8 targets,
    // strength on the last pass, combine = src + glow * (1 - src.a) (premultiplied).
    const int BakePad = 2;
    static readonly Dictionary<(Texture2D, Rectangle), Texture2D> _bakeCache = new();

    static Texture2D GetBaked(Texture2D atlas, SpriteFrame f)
    {
        var key = (atlas, f.SourceRect);
        if (_bakeCache.TryGetValue(key, out var t) && t != null && !t.IsDisposed) return t;
        t = Bake(atlas, f.SourceRect);
        _bakeCache[key] = t;
        return t;
    }

    static float Q(float v) => MathF.Round(Math.Clamp(v, 0f, 1f) * 255f) / 255f;

    static float Sample(float[] a, int w, int h, float x, float y)
    {
        // bilinear, clamp to edge (pixel centres at integer coordinates)
        x = Math.Clamp(x, 0, w - 1); y = Math.Clamp(y, 0, h - 1);
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        int x1 = Math.Min(x0 + 1, w - 1), y1 = Math.Min(y0 + 1, h - 1);
        float fx = x - x0, fy = y - y0;
        float a0 = a[y0 * w + x0] * (1 - fx) + a[y0 * w + x1] * fx;
        float a1 = a[y1 * w + x0] * (1 - fx) + a[y1 * w + x1] * fx;
        return a0 * (1 - fy) + a1 * fy;
    }

    static float[] BlurPass(float[] a, int w, int h, float rx, float ry, float strength)
    {
        var o = new float[a.Length];
        float[] offs = { 1.0f, 0.75f, 0.5f };
        float[] wts = { 0.00443f, 0.05399f, 0.24197f };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float s = a[y * w + x] * 0.39894f;
                for (int k = 0; k < 3; k++)
                {
                    s += Sample(a, w, h, x - rx * offs[k], y - ry * offs[k]) * wts[k];
                    s += Sample(a, w, h, x + rx * offs[k], y + ry * offs[k]) * wts[k];
                }
                o[y * w + x] = Q(s * strength);
            }
        return o;
    }

    /// <summary>One openfl DropShadowFilter (outer, no knockout) on premultiplied rgba planes.</summary>
    static void DropShadow(float[] r, float[] g, float[] b, float[] a, int w, int h, Vector3 color, float strength, int offX, int offY)
    {
        var s = BlurPass(a, w, h, 1, 0, 1);
        s = BlurPass(s, w, h, 1, 0, 1);
        s = BlurPass(s, w, h, 0, 1, 1);
        s = BlurPass(s, w, h, 0, 1, strength);
        for (int y = h - 1; y >= 0; y--)
            for (int x = w - 1; x >= 0; x--)
            {
                int i = y * w + x;
                int sx = x - offX, sy = y - offY;
                float ga = (sx >= 0 && sy >= 0 && sx < w && sy < h) ? s[sy * w + sx] : 0f;
                float inv = 1f - a[i];
                r[i] = Q(r[i] + color.X * ga * inv);
                g[i] = Q(g[i] + color.Y * ga * inv);
                b[i] = Q(b[i] + color.Z * ga * inv);
                a[i] = Q(a[i] + ga * inv);
            }
    }

    static Texture2D Bake(Texture2D atlas, Rectangle src)
    {
        int sw = src.Width, sh = src.Height;
        if (sw <= 0 || sh <= 0) return null;
        var px = new Color[sw * sh];
        atlas.GetData(0, src, px, 0, px.Length);
        int w = sw + BakePad + 8, h = sh + BakePad + 8;
        var r = new float[w * h]; var g = new float[w * h]; var b = new float[w * h]; var a = new float[w * h];
        for (int y = 0; y < sh; y++)
            for (int x = 0; x < sw; x++)
            {
                var c = px[y * sw + x]; // straight alpha
                int i = (y + BakePad) * w + x + BakePad;
                float al = c.A / 255f;
                a[i] = al; r[i] = Q(c.R / 255f * al); g[i] = Q(c.G / 255f * al); b[i] = Q(c.B / 255f * al);
            }
        DropShadow(r, g, b, a, w, h, new Vector3(1, 1, 1), 19f, 0, 0);
        DropShadow(r, g, b, a, w, h, new Vector3(0, 0, 0), 1f, 3, 3);
        var outPx = new Color[w * h];
        for (int i = 0; i < outPx.Length; i++)
        {
            float al = a[i];
            if (al <= 0f) { outPx[i] = Color.Transparent; continue; }
            outPx[i] = new Color(Math.Clamp(r[i] / al, 0, 1), Math.Clamp(g[i] / al, 0, 1), Math.Clamp(b[i] / al, 0, 1), al);
        }
        var tex = new Texture2D(atlas.GraphicsDevice, w, h);
        tex.SetData(outPx);
        return tex;
    }
}
