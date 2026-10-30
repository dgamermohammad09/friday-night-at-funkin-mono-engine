using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Logical (untrimmed) cell of a tightly packed composite frame (SpriteSheet.TightComposites): the global-bounds
/// origin and size the frame would have had in the old one-global-cell-per-tick layout. Keyed by (texture, rect, origin) (a cell can be shared by animations placed at different positions),
/// so copied frame lists keep working.
/// </summary>
public static class CompositeCells
{
    public struct Info { public Vector2 GOrigin; public int GW, GH; }
    private static readonly ConditionalWeakTable<Texture2D, Dictionary<(Rectangle, Vector2), Info>> _t = new();

    public static void Set(Texture2D tex, Rectangle rect, Vector2 origin, Vector2 gOrigin, int gw, int gh)
    {
        if (tex == null) return;
        var d = _t.GetOrCreateValue(tex);
        lock (d) d[(rect, origin)] = new Info { GOrigin = gOrigin, GW = gw, GH = gh };
    }

    public static bool TryGet(Texture2D tex, Rectangle rect, Vector2 origin, out Info info)
    {
        info = default;
        if (tex == null || !_t.TryGetValue(tex, out var d)) return false;
        lock (d) return d.TryGetValue((rect, origin), out info);
    }

    /// <summary>The texture was copied (e.g. RenderTarget2D -> Texture2D): carry the cell table over.</summary>
    public static void Copy(Texture2D from, Texture2D to)
    {
        if (from == null || to == null || !_t.TryGetValue(from, out var d)) return;
        var nd = _t.GetOrCreateValue(to);
        lock (d) lock (nd) foreach (var kv in d) nd[kv.Key] = kv.Value;
    }

    /// <summary>Logical origin of a composite frame (global cell origin when trimmed, else the frame's own).</summary>
    public static Vector2 LogicalOrigin((Texture2D Tex, Rectangle Rect, Vector2 Origin) f)
        => TryGet(f.Tex, f.Rect, f.Origin, out var i) ? i.GOrigin : f.Origin;

    /// <summary>Logical size of a composite frame (global cell size when trimmed, else its rect).</summary>
    public static (int W, int H) LogicalSize((Texture2D Tex, Rectangle Rect, Vector2 Origin) f)
        => TryGet(f.Tex, f.Rect, f.Origin, out var i) ? (i.GW, i.GH) : (f.Rect.Width, f.Rect.Height);

    /// <summary>
    /// Top-left of the trimmed rect inside the logical cell, in texture pixels, honouring flips
    /// (SpriteBatch flips a source rect inside its own quad, the old layout flipped the whole cell).
    /// </summary>
    public static Vector2 TrimOffset((Texture2D Tex, Rectangle Rect, Vector2 Origin) f, SpriteEffects fx)
    {
        if (!TryGet(f.Tex, f.Rect, f.Origin, out var i)) return Vector2.Zero;
        float dx = i.GOrigin.X - f.Origin.X, dy = i.GOrigin.Y - f.Origin.Y;
        if ((fx & SpriteEffects.FlipHorizontally) != 0) dx = i.GW - (dx + f.Rect.Width);
        if ((fx & SpriteEffects.FlipVertically) != 0) dy = i.GH - (dy + f.Rect.Height);
        return new Vector2(dx, dy);
    }
}
