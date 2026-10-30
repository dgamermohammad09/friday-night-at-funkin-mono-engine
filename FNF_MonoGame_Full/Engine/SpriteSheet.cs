using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace FNF_MonoGame.Engine;
/// <summary>
/// Represents a frame in a spritesheet
/// </summary>
public class SpriteFrame
{
    public string Name { get; set; }
    public Rectangle SourceRect { get; set; }
    public Vector2 Offset { get; set; }
    public bool Rotated { get; set; }
    /// <summary>
    /// Original untrimmed frame width (from Sparrow XML frameWidth).
    /// 0 means no trimming info � use SourceRect.Width instead.
    /// </summary>
    public int FrameWidth { get; set; }
    /// <summary>
    /// Original untrimmed frame height (from Sparrow XML frameHeight).
    /// 0 means no trimming info � use SourceRect.Height instead.
    /// </summary>
    public int FrameHeight { get; set; }
    /// <summary>Texture this frame lives in when merged from another atlas (multisparrow / multianimateatlas). Null = sheet texture.</summary>
    public Texture2D Texture { get; set; }

    // flixel-animate element effects inherited from enclosing symbol instances (ColorTransform alphaMultiplier and
    // MovieClip blend). Carried on cached clones so the (frame, matrix) part tuples stay unchanged.
    public float ElemAlpha { get; private set; } = 1f;
    /// <summary>OpenFL BlendMode int: 10 NORMAL (default), 0 ADD, 12 SCREEN, 9 MULTIPLY, ...</summary>
    public int ElemBlend { get; private set; } = 10;
    private SpriteFrame _fxBase;
    private Dictionary<(int, int), SpriteFrame> _fxCache;

    /// <summary>
    /// Returns this part as seen through a symbol instance with alpha multiplier <paramref name="alpha"/> and blend
    /// <paramref name="blend"/>. Blend.resolve: an inner non-NORMAL blend wins over the instance's blend.
    /// </summary>
    /// <summary>flixel-animate mask (layer clipped by a Clipper layer): the masker parts relative to this part.</summary>
    public PartMask Mask { get; private set; }

    /// <summary>This part drawn through a mask (Frame._bakeFrame / FilterRenderer.maskFrame). Uncached clone.</summary>
    public SpriteFrame WithMask(PartMask mask)
    {
        var b = _fxBase ?? this;
        var m = new SpriteFrame
        {
            Name = b.Name, SourceRect = b.SourceRect, Offset = b.Offset, Rotated = b.Rotated,
            FrameWidth = b.FrameWidth, FrameHeight = b.FrameHeight, Texture = b.Texture, Mask = mask
        };
        return m.WithFx(ElemAlpha, ElemBlend);
    }

    public SpriteFrame WithFx(float alpha, int blend)
    {
        float na = ElemAlpha * alpha;
        int nb = ElemBlend != 10 ? ElemBlend : blend;
        if (MathF.Abs(na - ElemAlpha) < 1e-4f && nb == ElemBlend) return this;
        var b = _fxBase ?? this;
        if (MathF.Abs(na - 1f) < 1e-4f && nb == 10) return b;
        var key = ((int)MathF.Round(na * 10000f), nb);
        b._fxCache ??= new Dictionary<(int, int), SpriteFrame>();
        lock (b._fxCache)
        {
            if (b._fxCache.TryGetValue(key, out var c)) return c;
            c = new SpriteFrame
            {
                Name = b.Name, SourceRect = b.SourceRect, Offset = b.Offset, Rotated = b.Rotated,
                FrameWidth = b.FrameWidth, FrameHeight = b.FrameHeight, Texture = b.Texture,
                ElemAlpha = na, ElemBlend = nb, _fxBase = b, Mask = b.Mask
            };
            b._fxCache[key] = c;
            return c;
        }
    }
}
/// <summary>
/// Masker of a masked atlas part. <see cref="Rel"/> = masker parts expressed relative to the masked part's matrix
/// (M_part^-1 * M_masker), which stays valid when the part is lifted into parent symbol space. Parts of one masked
/// layer at one tick share <see cref="Group"/>; they are rendered together, then multiplied by the masker alpha
/// (MaskShader: color *= masker.a), like flixel-animate bakes the masked frame.
/// </summary>
public sealed class PartMask
{
    public object Group;
    public List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)> Rel;
}
/// <summary>
/// Loads and manages Sparrow/Starling XML spritesheets used by FNF
/// Also supports Adobe Animate JSON spritemap format
/// </summary>
public class SpriteSheet
{
    public Texture2D Texture { get; private set; }
    public Dictionary<string, SpriteFrame> Frames { get; } = new();
    public Dictionary<string, List<SpriteFrame>> Animations { get; } = new();
    /// <summary>First SubTexture of the Sparrow XML (FlxSprite.frames setter selects frames[0]).</summary>
    public SpriteFrame FirstFrame { get; set; }
    /// <summary>
    /// Pre-rendered composite animations for spritemap symbols that are composed
    /// of multiple atlas sprites positioned via transforms.
    /// Key = symbol name, Value = list of (texture, sourceRect, origin) per tick.
    /// Origin is the pixel in the texture corresponding to the animation's (0,0) registration point.
    /// </summary>
    public Dictionary<string, List<(Texture2D Tex, Rectangle Rect, Vector2 Origin)>> CompositeAnimations { get; } = new();
    /// <summary>
    /// Raw composite data for runtime compositing (matches original FNF's FlxAnimate approach).
    /// Instead of pre-rendering body parts into atlas textures, stores the raw M3D transform
    /// data per tick so that each body part can be drawn directly at runtime.
    /// Key = animation name (e.g. "Idle", "IdleLeft"), Value = list of ticks, each tick
    /// contains a list of (spriteFrame, affine transform) tuples in draw order.
    /// </summary>
    public Dictionary<string, List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>>> RawCompositeData { get; } = new();
    /// <summary>
    /// Deferred composite data for background-thread loading.
    /// CPU work (CollectPartsWithTransforms) runs on background thread;
    /// GPU work (PreRenderComposite) is deferred to FinalizeComposites on main thread.
    /// </summary>
    public List<(string Name, List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>> TickParts)> PendingComposites { get; } = new();
    /// <summary>
    /// JIT (Just-In-Time) composite data for on-demand rendering.
    /// Stores tick parts + precomputed origin per animation. GPU rendering
    /// is deferred until the animation is first drawn, then the result is
    /// cached in CompositeAnimations for subsequent frames.
    /// </summary>
    public Dictionary<string, JITCompositeInfo> JITComposites { get; } = new();
    /// <summary>
    /// Logical frame size (width/height) for composite animations, based on the FIRST frame
    /// (typically idle) rather than the global padded bounding box. This matches HaxeFlixel's
    /// updateHitbox behavior which uses the current (idle) frame dimensions for positioning.
    /// </summary>
    public Dictionary<string, (int Width, int Height)> CompositeLogicalSizes { get; } = new();
    /// <summary>
    /// Fixed base origin for all composite animations on this sprite sheet.
    /// HaxeFlixel's sprite origin (width/2, height) stays consistent across animations;
    /// using a per-animation bounding-box origin causes the character to shift between animations.
    /// We capture the origin from the first loaded composite (typically idle) and reuse it for all.
    /// </summary>
    public Vector2? CompositeBaseOrigin { get; set; }
    /// <summary>
    /// Base offset (minX, minY) of the first loaded composite frame. Used to keep the
    /// character's feet anchored at the same stage position across frames.
    /// </summary>
    public Vector2 CompositeBaseOffset { get; set; } = Vector2.Zero;
    /// <summary>
    /// Base size (width, height) of the first loaded composite frame.
    /// </summary>
    public (int Width, int Height) CompositeBaseSize { get; set; }
    /// <summary>
    /// FlxAnimate offset: the bounding box (minX, minY) from the idle/first animation frame.
    /// In the original, FlxAnimate.updateHitbox() computes this from getBounds() and stores it as 'offset'.
    /// During draw, FlxSprite subtracts this: drawPoint = screenPos - offset.
    /// We cache this from the first loaded raw composite animation (idle) and reuse for all.
    /// </summary>
    public Vector2 RawCompositeOffset { get; set; } = Vector2.Zero;
    /// <summary>
    /// Per-frame bounding box min offsets for composite animations.
    /// Key = animation name, Value = list of (minX, minY) for each frame.
    /// This matches FlxAnimate.updateHitbox() which updates the offset to the current frame's bounds.
    /// </summary>
    public Dictionary<string, List<Vector2>> CompositeFrameOffsets { get; } = new();
    /// <summary>
    /// Per-frame logical sizes for composite animations.
    /// Key = animation name, Value = list of (width, height) for each frame.
    /// Used to keep the character's feet at the same stage position across frames.
    /// </summary>
    public Dictionary<string, List<(int Width, int Height)>> CompositeFrameSizes { get; } = new();
    /// <summary>Exact (unrounded) per-frame bounds min/max, used for flixel-animate whole-timeline bounds.</summary>
    public Dictionary<string, List<(float MinX, float MinY, float MaxX, float MaxY)>> CompositeFrameExtents { get; } = new();

    /// <summary>
    /// Anchor composite drawing to one animation, the frame the original measures with
    /// updateHitbox() after dance(true). Origin, base offset and base size all come from the same
    /// animation so they share one coordinate space. Mixing them (e.g. a symbol-space anim for the
    /// origin and a label-space anim for frames) drew Dad ~340px up-left and BF off-screen.
    /// </summary>
    /// <summary>Frame labels of the main timeline (flixel-animate addByFrameLabel animations).</summary>
    public HashSet<string> TimelineLabelNames { get; } = new(StringComparer.Ordinal);
    public HashSet<string> SymbolAnimKeys { get; } = new(StringComparer.Ordinal);
    /// <summary>flixel-animate Timeline.getWholeBounds of a library symbol's own timeline (addBySymbol anims).</summary>
    public Func<string, (float X, float Y, float W, float H)?> SymbolBoundsFn { get; set; }
    /// <summary>Union of all main-timeline frames (flixel-animate getWholeBounds) when the atlas uses frame labels.</summary>
    public (float X, float Y, float W, float H)? WholeMainTimelineBounds { get; set; }
    /// <summary>FlxSpriteElement anchors: a song/character script adds a sprite to every frame of a keyword symbol
    /// (Spaghetti SserafimLipSyncSprite via getFramesWithKeyword). Per animation and tick: the composed matrix of each
    /// keyword-symbol instance in timeline space (same space as the composite parts).</summary>
    public Dictionary<string, List<List<(float A, float B, float C, float D, float TX, float TY, int Idx)>>> Anchors { get; } = new();
    /// <summary>Atlas path fragment -> keyword symbol (characters/sserafim-*.hxc onCreate getFramesWithKeyword).</summary>
    public static readonly Dictionary<string, string> AnchorKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        { "characters/sserafim/chaewon", "mouth default" }, { "characters/sserafim/eunchae", "mouth default" },
        { "characters/sserafim/kazuha", "mouth default" }, { "characters/sserafim/sakura", "mouth edit" },
        { "characters/sserafim/yunjin", "mouth yunjin" },
    };
    [ThreadStatic] private static string t_anchorKw;
    [ThreadStatic] private static Dictionary<object, List<List<(float A, float B, float C, float D, float TX, float TY, int Idx)>>> t_anchors;
    private static void AnchorAdd(System.Collections.IList key, int t, (float A, float B, float C, float D, float TX, float TY, int Idx) m)
    {
        if (!t_anchors.TryGetValue(key, out var l))
        {
            l = new List<List<(float A, float B, float C, float D, float TX, float TY, int Idx)>>();
            for (int i = 0; i < key.Count; i++) l.Add(new List<(float A, float B, float C, float D, float TX, float TY, int Idx)>());
            t_anchors[key] = l;
        }
        if (t >= 0 && t < l.Count) l[t].Add(m);
    }
    private static void AnchorCompose(System.Collections.IList dst, int t, System.Collections.IList src, int srcTick, float pa, float pb, float pc, float pd, float ptx, float pty)
    {
        if (t_anchors == null || !t_anchors.TryGetValue(src, out var sl) || srcTick < 0 || srcTick >= sl.Count) return;
        // the nested symbol's parts are appended after the parts already in dst[t]: shift the insertion index
        int baseIdx = t >= 0 && t < dst.Count && dst[t] is System.Collections.ICollection dc ? dc.Count : 0;
        foreach (var a in sl[srcTick])
            AnchorAdd(dst, t, (pa * a.A + pc * a.B, pb * a.A + pd * a.B, pa * a.C + pc * a.D, pb * a.C + pd * a.D, pa * a.TX + pc * a.TY + ptx, pb * a.TX + pd * a.TY + pty, baseIdx + a.Idx));
    }
    /// <summary>Per animation and tick: index of the first composite part drawn ABOVE the FlxSpriteElement
    /// (frame.add(element) appends it to the keyword symbol's frame, so it sits at the mouth layer); -1 = no element.
    /// Composites of these animations are rendered in two pieces (CompositeAnimations = parts below,
    /// CompositeAbove = parts above) so a script sprite can be drawn in between.</summary>
    public Dictionary<string, int[]> AnchorSplit { get; } = new(StringComparer.Ordinal);
    /// <summary>Pre-rendered parts above the FlxSpriteElement insertion point (see AnchorSplit); Tex null = none.</summary>
    public Dictionary<string, List<(Texture2D Tex, Rectangle Rect, Vector2 Origin)>> CompositeAbove { get; } = new(StringComparer.Ordinal);
    /// <summary>Timeline bounds of animations merged from another atlas (each atlas has its own main timeline).</summary>
    public Dictionary<string, (float X, float Y, float W, float H)> MergedTimelineBounds { get; } = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (float X, float Y, float W, float H)> _timelineBoundsCache = new(StringComparer.Ordinal);

    /// <summary>
    /// flixel-animate Timeline._bounds = getWholeBounds(): the union of every frame of the timeline the
    /// animation plays on. Frame-label animations share the main timeline; symbol animations use their
    /// symbol's timeline. FlxAnimate draws every frame with translate(-bounds.x, -bounds.y), and the
    /// sprite's frameWidth/frameHeight are bounds.width/height (FlxAnimateController.updateTimelineBounds).
    /// </summary>
    public (float X, float Y, float W, float H)? GetTimelineBounds(string animKey)
    {
        if (string.IsNullOrEmpty(animKey)) return null;
        if (_timelineBoundsCache.TryGetValue(animKey, out var cached)) return cached;
        int sep0 = animKey.IndexOf("__", StringComparison.Ordinal);
        if (MergedTimelineBounds.TryGetValue(sep0 > 0 ? animKey.Substring(0, sep0) : animKey, out var mb)) return mb;
        int sep = animKey.IndexOf("__", StringComparison.Ordinal);
        string baseKey = sep > 0 ? animKey.Substring(0, sep) : animKey;
        (float X, float Y, float W, float H)? r = null;
        if (SymbolAnimKeys.Contains(baseKey)) { if (!TimelineLabelNames.Contains(baseKey)) { try { r = SymbolBoundsFn?.Invoke(baseKey); } catch { r = null; } } /* label twin (momCar): frames are in main-timeline space */ r ??= UnionOfFrames(baseKey) ?? UnionOfFrames(animKey); } // addBySymbol: own timeline
        else if (TimelineLabelNames.Count > 0 && TimelineLabelNames.Contains(baseKey))
        {
            if (WholeMainTimelineBounds.HasValue) r = WholeMainTimelineBounds;
            else
                foreach (var label in TimelineLabelNames)
                    r = Union(r, UnionOfFrames(label));
        }
        else
        {
            r = UnionOfFrames(baseKey) ?? UnionOfFrames(animKey);
        }
        if (r.HasValue) _timelineBoundsCache[animKey] = r.Value;
        return r;
    }

    private (float X, float Y, float W, float H)? UnionOfFrames(string key)
    {
        if (key != null && CompositeFrameExtents.TryGetValue(key, out var ext) && ext.Count > 0)
        {
            float a0 = float.MaxValue, b0 = float.MaxValue, a1 = float.MinValue, b1 = float.MinValue;
            foreach (var e in ext)
            {
                if (e.MaxX <= e.MinX && e.MaxY <= e.MinY) continue;
                a0 = Math.Min(a0, e.MinX); b0 = Math.Min(b0, e.MinY); a1 = Math.Max(a1, e.MaxX); b1 = Math.Max(b1, e.MaxY);
            }
            if (a0 != float.MaxValue) return (a0, b0, a1 - a0, b1 - b0);
        }
        if (key == null || !CompositeFrameOffsets.TryGetValue(key, out var offs) || offs.Count == 0) return null;
        CompositeFrameSizes.TryGetValue(key, out var sizes);
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (int i = 0; i < offs.Count; i++)
        {
            var (w, h) = sizes != null && i < sizes.Count ? sizes[i] : (0, 0);
            if (w <= 0 && h <= 0) continue;
            minX = Math.Min(minX, offs[i].X); minY = Math.Min(minY, offs[i].Y);
            maxX = Math.Max(maxX, offs[i].X + w); maxY = Math.Max(maxY, offs[i].Y + h);
        }
        if (minX == float.MaxValue) return null;
        return (minX, minY, maxX - minX, maxY - minY);
    }

    private static (float X, float Y, float W, float H)? Union((float X, float Y, float W, float H)? a, (float X, float Y, float W, float H)? b)
    {
        if (!a.HasValue) return b;
        if (!b.HasValue) return a;
        float x = Math.Min(a.Value.X, b.Value.X), y = Math.Min(a.Value.Y, b.Value.Y);
        float r = Math.Max(a.Value.X + a.Value.W, b.Value.X + b.Value.W), btm = Math.Max(a.Value.Y + a.Value.H, b.Value.Y + b.Value.H);
        return (x, y, r - x, btm - y);
    }

    public bool AnchorCompositeTo(string animKey)
    {
        if (string.IsNullOrEmpty(animKey) || !CompositeFrameOffsets.TryGetValue(animKey, out var offs) || offs.Count == 0)
            return false;
        float mx = float.MaxValue, my = float.MaxValue;
        foreach (var o in offs) { mx = Math.Min(mx, o.X); my = Math.Min(my, o.Y); }
        CompositeBaseOrigin = new Vector2(-mx, -my);
        CompositeBaseOffset = offs[0];
        if (CompositeFrameSizes.TryGetValue(animKey, out var sz) && sz.Count > 0)
            CompositeBaseSize = sz[0];
        return true;
    }
    /// <summary>
    /// Stage offset from AN.STI (Stage Instance Transform) in the Animation.json.
    /// Contains the raw (tx, ty) from the STI's M3D matrix � matching FlxAnimate's
    /// applyStageMatrix behavior where offset -= stageMatrix.t.
    /// Applied as: drawPosition = spritePosition + StageOffset.
    /// </summary>
    public Vector2 StageOffset { get; private set; }
    /// <summary>
    /// Custom blend state for rendering non-premultiplied sprites onto a RenderTarget2D.
    /// NonPremultiplied squares alpha (AlphaSourceBlend=SourceAlpha ? outA = srcA*srcA),
    /// causing dark halos when the RT is drawn to screen. This blend state preserves
    /// alpha correctly (AlphaSourceBlend=One ? outA = srcA) while still premultiplying RGB.
    /// The resulting RT has premultiplied RGB and correct alpha � draw with AlphaBlend.
    /// </summary>
    // ADD into a premultiplied composite: rgb += src*a, alpha untouched. Drawing the composite with AlphaBlend
    // (One, InvSrcAlpha) then yields dst + src*a over the scene: exactly OpenFL's ADD.
    private static readonly BlendState _rtCompositeAdd = new BlendState
    {
        ColorSourceBlend = Blend.SourceAlpha, ColorDestinationBlend = Blend.One,
        AlphaSourceBlend = Blend.Zero, AlphaDestinationBlend = Blend.One,
    };
    // SCREEN (approx. in premultiplied space): rgb = src*a + dst*(1 - src), alpha untouched.
    private static readonly BlendState _rtCompositeScreen = new BlendState
    {
        ColorSourceBlend = Blend.SourceAlpha, ColorDestinationBlend = Blend.InverseSourceColor,
        AlphaSourceBlend = Blend.Zero, AlphaDestinationBlend = Blend.One,
    };
    private static BlendState CompositeBlendFor(int openflBlend)
        => openflBlend == 0 ? _rtCompositeAdd : openflBlend == 12 ? _rtCompositeScreen : _rtCompositeBlend;
    private static readonly BlendState _rtCompositeBlend = new BlendState
    {
        ColorSourceBlend = Blend.SourceAlpha,
        ColorDestinationBlend = Blend.InverseSourceAlpha,
        AlphaSourceBlend = Blend.One,
        AlphaDestinationBlend = Blend.InverseSourceAlpha,
    };
    private SpriteSheet() { }
    /// <summary>
    /// Cache the base (idle/first) composite frame offset and size. Called when the first
    /// composite is loaded so Draw can keep the character anchored across frames.
    /// </summary>
    private void TrySetCompositeBase(List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>> tickParts)
    {
        if (tickParts == null || tickParts.Count == 0) return;
        if (CompositeBaseSize.Width > 0) return;
        var (minX, minY, _, _) = AnimatedSprite.ComputeRawBounds(tickParts[0]);
        CompositeBaseOffset = new Vector2(minX, minY);
        CompositeBaseSize = AnimatedSprite.ComputeRawBoundingBox(tickParts[0]);
        if (RawCompositeOffset == Vector2.Zero)
            RawCompositeOffset = CompositeBaseOffset;
    }
    /// <summary>
    /// Premultiply alpha on a texture loaded via Texture2D.FromStream
    /// (which loads non-premultiplied). SpriteBatch.AlphaBlend expects premultiplied.
    /// </summary>
    /// <summary>
    /// Premultiply alpha on a texture. Required for textures drawn with custom
    /// MULTIPLY blend state (DestinationColor) � non-premultiplied textures have
    /// non-zero RGB in transparent pixels which causes bright edge halos.
    /// </summary>
    internal static void PremultiplyAlpha(Texture2D texture)
    {
        SpriteSheetTextureCache.Detach(texture); // pixels are about to change: never share this texture
        if (texture == null) return;
        Color[] data = new Color[texture.Width * texture.Height];
        texture.GetData(data);
        for (int i = 0; i < data.Length; i++)
        {
            var c = data[i];
            if (c.A < 255)
                data[i] = Color.FromNonPremultiplied(c.R, c.G, c.B, c.A);
        }
        texture.SetData(data);
    }
    /// <summary>
    /// Load a spritesheet from PNG + XML or JSON files.
    /// When preRenderComposites is true, multi-part spritemap symbols are pre-rendered
    /// into composite textures (used for the DJ character in Freeplay). This is expensive
    /// so it should only be enabled when the composite rendering is actually needed.
    /// </summary>
    /// <summary>Optional one-shot edit of Animation.json before parsing (used to replay FunkinSprite.scaleElement). Set, Load, then clear.</summary>
    [ThreadStatic] public static Action<JObject> AnimationJsonPatch;
    /// <summary>flixel-animate Layer._loadJson: a mask layer (LT "Clp"/"Clipper") gets visible = false, and a masked layer
    /// (Clpb) whose clipper is not found ABOVE it is hidden too. Hidden layers are neither drawn nor part of the timeline
    /// bounds (Timeline.getBounds skips !layer.visible). The port does not clip masked layers (drawn unmasked).</summary>
    private static void StripMaskLayers(JObject animData)
    {
        void Strip(JToken tl)
        {
            if (tl?["L"] is not JArray layers) return;
            var keep = new JArray();
            var clippers = new Dictionary<string, JToken>(StringComparer.Ordinal);
            foreach (var l in layers)
            {
                string lt = l["LT"]?.ToString();
                string clpb = l["Clpb"]?.ToString();
                string ln = l["LN"]?.ToString() ?? "";
                if (clpb != null) { if (clippers.TryGetValue(clpb, out var cl)) { l["__clipper"] = cl.DeepClone(); keep.Add(l); } continue; }
                if (lt == "Clp" || lt == "Clipper") { clippers[ln] = l; continue; }
                keep.Add(l);
            }
            if (keep.Count != layers.Count) tl["L"] = keep;
        }
        try
        {
            Strip(animData?["AN"]?["TL"]);
            if (animData?["SD"]?["S"] is JArray syms) foreach (var sym in syms) Strip(sym["TL"]);
        }
        catch { }
    }
    internal static readonly bool LoadLog = Environment.GetEnvironmentVariable("FNF_LOADLOG") == "1";
    /// <summary>Device for JIT composites rendered from PlayAnimation (set by every Load).</summary>
    public static GraphicsDevice JitDevice;
    public static SpriteSheet Load(Game game, string basePath, bool preRenderComposites = false, string[] preRenderFilter = null, bool deferComposites = false, bool applyStageInstanceTransform = false, bool applyTRP = false, bool sparrowOnly = false)
    {
        if (game?.GraphicsDevice != null) JitDevice = game.GraphicsDevice;
        var sw = LoadLog ? System.Diagnostics.Stopwatch.StartNew() : null;
        var r = LoadCore(game, basePath, preRenderComposites, preRenderFilter, deferComposites, applyStageInstanceTransform, applyTRP, sparrowOnly);
        if (sw != null) Console.WriteLine($"[SHEETLOAD] {sw.Elapsed.TotalMilliseconds:F0} ms {basePath} prerender={preRenderComposites} defer={deferComposites}");
        return r;
    }
    private static SpriteSheet LoadCore(Game game, string basePath, bool preRenderComposites, string[] preRenderFilter, bool deferComposites, bool applyStageInstanceTransform, bool applyTRP, bool sparrowOnly)
    {
        t_anchorKw = null; t_anchors = null; // FlxSpriteElement anchors (see AnchorKeywords)
        { string bp = (basePath ?? "").Replace('\\', '/'); foreach (var kv in AnchorKeywords) if (bp.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0) { t_anchorKw = kv.Value; t_anchors = new(); break; } }
        var sheet = new SpriteSheet();
        string contentPath = System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot);
        // Use AssetManager for multi-root path resolution (Content + funkin.assets)
#if XBOX_UWP
        var assets = (game as FNF_MonoGame.XboxGame)?.Assets;
#else
        var assets = (game as FNF_MonoGame.FNFGame)?.Assets;
#endif
        // Try loading spritemap folder format first (for new FNF assets)
        string spritemapFolder = assets?.ResolveDirectory(basePath)
                              ?? (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(Path.Combine(contentPath, basePath)) ? Path.Combine(contentPath, basePath) : null);
        if (spritemapFolder != null)
        {
            string spritemapPng = Path.Combine(spritemapFolder, "spritemap1.png");
            string spritemapJson = Path.Combine(spritemapFolder, "spritemap1.json");
            string animationJson = Path.Combine(spritemapFolder, "Animation.json");
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(spritemapPng) && global::FNF_MonoGame.Engine.FsCI.FileExists(spritemapJson))
            {
                if (AssetManager.AssetLog) Console.WriteLine($"[ASSETLOAD] {spritemapFolder}");
                return LoadFromSpritemap(game, spritemapPng, spritemapJson, animationJson, preRenderComposites, preRenderFilter, deferComposites, applyStageInstanceTransform, applyTRP);
            }
        }
        // Load texture (PNG + XML or JSON)
        string pngPath = assets?.ResolvePath(basePath + ".png")
                      ?? (global::FNF_MonoGame.Engine.FsCI.FileExists(Path.Combine(contentPath, basePath + ".png")) ? Path.Combine(contentPath, basePath + ".png") : null);
        if (pngPath == null)
        {
            return null;
        }
        if (AssetManager.AssetLog) Console.WriteLine($"[ASSETLOAD] {pngPath}");
        sheet.Texture = SpriteSheetTextureCache.Acquire(game.GraphicsDevice, pngPath);
        // Skip PremultiplyAlpha � GetData/SetData causes multi-second freezes on large textures.
        // Use BlendState.NonPremultiplied when drawing instead.
        try
        {
            // Try JSON spritemap first
            string jsonPath = assets?.ResolvePath(basePath + ".json")
                           ?? (global::FNF_MonoGame.Engine.FsCI.FileExists(Path.Combine(contentPath, basePath + ".json")) ? Path.Combine(contentPath, basePath + ".json") : null);
            if (jsonPath != null && !sparrowOnly) // sparrowOnly: Paths.getSparrowAtlas (same-named .json is data, e.g. alphabet.json)
            {
                LoadFromJson(sheet, jsonPath);
                return sheet;
            }
            // Load XML
            string xmlPath = assets?.ResolvePath(basePath + ".xml")
                          ?? (global::FNF_MonoGame.Engine.FsCI.FileExists(Path.Combine(contentPath, basePath + ".xml")) ? Path.Combine(contentPath, basePath + ".xml") : null);
            if (xmlPath != null)
            {
                // Parse Sparrow XML format
                LoadFromXml(sheet, xmlPath);
                return sheet;
            }
            // Try Packer .txt format (used by spirit and some pixel characters)
            string txtPath = assets?.ResolvePath(basePath + ".txt")
                          ?? (global::FNF_MonoGame.Engine.FsCI.FileExists(Path.Combine(contentPath, basePath + ".txt")) ? Path.Combine(contentPath, basePath + ".txt") : null);
            if (txtPath != null)
            {
                LoadFromPacker(sheet, txtPath);
                return sheet;
            }
            // Return sheet with just the full texture as one frame
            sheet.Frames["default"] = new SpriteFrame
            {
                Name = "default",
                SourceRect = new Rectangle(0, 0, sheet.Texture.Width, sheet.Texture.Height),
                Offset = Vector2.Zero
            };
            return sheet;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading spritesheet '{basePath}': {ex.Message}");
            sheet.Dispose();
            return null;
        }
    }
    /// <summary>
    /// Load from Adobe Animate spritemap folder format.
    /// When preRenderComposites is true, composites multi-part symbols into pre-rendered textures.
    /// Otherwise, picks the largest sprite per tick as a simplified fallback.
    /// </summary>
    private static SpriteSheet LoadFromSpritemap(Game game, string pngPath, string spritemapJsonPath, string animationJsonPath, bool preRenderComposites, string[] preRenderFilter = null, bool deferComposites = false, bool applyStageInstanceTransform = false, bool applyTRP = false)
    {
        var sheet = new SpriteSheet();
        if (AssetManager.AssetLog) Console.WriteLine($"[ASSETLOAD] {pngPath}");
        sheet.Texture = SpriteSheetTextureCache.Acquire(game.GraphicsDevice, pngPath);
        // Skip PremultiplyAlpha for spritemap textures � GetData/SetData on 4096�2048
        // textures transfers ~33MB GPU?CPU each way, causing multi-second freezes.
        // Use BlendState.NonPremultiplied when drawing these textures instead.
        try
        {
            // Parse spritemap JSON (atlas frame definitions)
            string spritemapJson = global::FNF_MonoGame.Engine.FsCI.ReadAllText(spritemapJsonPath);
            var spritemapData = JObject.Parse(spritemapJson);
            var sprites = spritemapData["ATLAS"]?["SPRITES"];
            var framesByName = new Dictionary<string, SpriteFrame>();
            if (sprites != null)
            {
                foreach (var spriteToken in sprites)
                {
                    var sprite = spriteToken["SPRITE"];
                    if (sprite == null) continue;
                    string name = sprite["name"]?.ToString() ?? "";
                    int x = sprite["x"]?.Value<int>() ?? 0;
                    int y = sprite["y"]?.Value<int>() ?? 0;
                    int w = sprite["w"]?.Value<int>() ?? 0;
                    int h = sprite["h"]?.Value<int>() ?? 0;
                    bool rotated = sprite["rotated"]?.Value<bool>() ?? false;
                    var frame = new SpriteFrame
                    {
                        Name = name,
                        SourceRect = new Rectangle(x, y, w, h),
                        Offset = Vector2.Zero,
                        Rotated = rotated
                    };
                    sheet.Frames[name] = frame;
                    framesByName[name] = frame;
                }
            }
            // Parse Animation JSON if available
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(animationJsonPath))
            {
                string animJson = global::FNF_MonoGame.Engine.FsCI.ReadAllText(animationJsonPath);
                if (AnimationJsonPatch != null) { var pj = JObject.Parse(animJson); AnimationJsonPatch(pj); animJson = pj.ToString(Newtonsoft.Json.Formatting.None); }
                var animData = JObject.Parse(animJson); StripMaskLayers(animData); // flixel-animate Layer._loadJson: clipper layers are invisible
                // Read AN.STI (Stage Instance Transform) � the authoring-time placement
                // of the root symbol on the Adobe Animate stage. Original FlxAnimate reads
                // stageMatrix.tx/ty and applies: offset -= stageMatrix.t, so drawPos = pos + (tx,ty).
                var stiSi = animData["AN"]?["STI"]?["SI"];
                if (stiSi != null)
                {
                    var stiM3D = stiSi["M3D"];
                    var stiMX = stiSi["MX"];
                    if (stiM3D != null && stiM3D.Count() >= 14)
                        sheet.StageOffset = new Vector2(stiM3D[12].Value<float>(), stiM3D[13].Value<float>());
                    else if (stiMX != null && stiMX.Count() >= 6)
                        sheet.StageOffset = new Vector2(stiMX[4].Value<float>(), stiMX[5].Value<float>());
                }
                var symbolsByName = new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);
                var symbolArray = animData["SD"]?["S"]
                               ?? animData["SYMBOL_DICTIONARY"]?["Symbols"];
                if (symbolArray != null)
                {
                    foreach (var sym in symbolArray)
                    {
                        string sn = (sym["SN"] ?? sym["SYMBOL_name"])?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(sn))
                            symbolsByName[sn] = sym;
                    }
                }
                // Identify top-level animation symbols from the main timeline (AN)
                var topLevelSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var mainTimeline = animData["AN"]?["TL"]?["L"] ?? animData["TL"]?["L"] ?? animData["ANIMATION"]?["TIMELINE"]?["LAYERS"];
                if (mainTimeline != null)
                {
                    foreach (var layer in mainTimeline)
                    {
                        var layerFrames = layer["FR"] ?? layer["Frames"];
                        if (layerFrames == null) continue;
                        foreach (var frameData in layerFrames)
                        {
                            var elements = frameData["E"] ?? frameData["elements"];
                            if (elements == null) continue;
                            foreach (var element in elements)
                            {
                                var si = element["SI"] ?? element["SYMBOL_instance"] ?? element["SYMBOL_Instance"];
                                if (si != null)
                                {
                                    string sn = (si["SN"] ?? si["SYMBOL_name"])?.ToString() ?? "";
                                    if (!string.IsNullOrEmpty(sn))
                                        topLevelSymbols.Add(sn);
                                }
                            }
                        }
                    }
                }
                // If no main timeline found, treat all symbols as top-level
                if (topLevelSymbols.Count == 0)
                {
                    foreach (var k in symbolsByName.Keys)
                        topLevelSymbols.Add(k);
                }
                // Build composite animations only for top-level symbols (performance)
                // When preRenderFilter is set, only pre-render matching symbols
                var symbolFilter = preRenderFilter != null
                    ? new HashSet<string>(preRenderFilter, StringComparer.OrdinalIgnoreCase)
                    : null;
                foreach (var kvp in symbolsByName)
                {
                    if (!topLevelSymbols.Contains(kvp.Key)) continue;
                    // When filter is set, skip non-matching symbols entirely.
                    // This avoids the expensive O(n� � depth) recursive CollectLargestPerTick
                    // for symbols we don't need (body parts, effects, nested components).
                    if (symbolFilter != null && !symbolFilter.Contains(kvp.Key)) continue;
                    int totalDuration = GetSymbolDuration(kvp.Value);
                    if (totalDuration <= 0 || totalDuration > 400) continue;
                    bool shouldComposite = preRenderComposites && (symbolFilter == null || symbolFilter.Contains(kvp.Key));
                    if (shouldComposite)
                    {
                        // Full composite rendering with transforms
                        var tickParts = new List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>>();
                        for (int t = 0; t < totalDuration; t++)
                            tickParts.Add(new List<(SpriteFrame, float, float, float, float, float, float)>());
                        CollectPartsWithTransforms(kvp.Value, symbolsByName, framesByName, tickParts, 1, 0, 0, 1, 0, 0,
                            new HashSet<string>(StringComparer.OrdinalIgnoreCase), applyTRP: false,
                            symbolCache: new Dictionary<string, List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>>>(StringComparer.OrdinalIgnoreCase));
                        bool hasParts = false;
                        foreach (var tick in tickParts)
                            if (tick.Count > 0) { hasParts = true; break; }
                        if (!hasParts) continue;
                        bool multiPart = false;
                        foreach (var tick in tickParts)
                            if (tick.Count > 1) { multiPart = true; break; }
                        // Single-part frames may still have non-identity STI/M3D transforms
                        // (e.g. crowd, bar). Detect and route through composite path to preserve them.
                        bool hasTransforms = false;
                        if (!multiPart)
                        {
                            foreach (var tick in tickParts)
                            {
                                if (tick.Count == 1)
                                {
                                    var (_, a, b, c, d, tx, ty) = tick[0];
                                    if (MathF.Abs(a - 1f) > 0.001f || MathF.Abs(d - 1f) > 0.001f ||
                                        MathF.Abs(b) > 0.001f || MathF.Abs(c) > 0.001f ||
                                        MathF.Abs(tx) > 0.001f || MathF.Abs(ty) > 0.001f)
                                    { hasTransforms = true; break; }
                                }
                            }
                        }
                        if (multiPart || hasTransforms)
                        {
                            if (deferComposites)
                            {
                                // Runtime compositing: store raw tick parts so Draw()
                                // renders each body part directly with M3D transforms.
                                // No GPU work needed � safe for background threads.
                                sheet.RawCompositeData[kvp.Key] = tickParts;
                                // Cache FlxAnimate offset from first loaded animation's first frame
                                // (matches original: updateHitbox() after dance(true) in resetCharacter)
                                if (sheet.RawCompositeOffset == Vector2.Zero && tickParts.Count > 0)
                                {
                                    var (minX, minY, _, _) = AnimatedSprite.ComputeRawBounds(tickParts[0]);
                                    sheet.RawCompositeOffset = new Vector2(minX, minY);
                                }
                                // Store per-frame bounds for updateHitbox-style drawing
                                sheet.CompositeFrameOffsets[kvp.Key] = tickParts.Select(t =>
                                {
                                    var (minX, minY, _, _) = AnimatedSprite.ComputeRawBounds(t);
                                    return new Vector2(minX, minY);
                                }).ToList();
                                sheet.CompositeFrameSizes[kvp.Key] = tickParts.Select(t => AnimatedSprite.ComputeRawBoundingBox(t)).ToList(); sheet.CompositeFrameExtents[kvp.Key] = tickParts.Select(t => { var (ax, ay, bx, by) = AnimatedSprite.ComputeRawBounds(t); return (ax, ay, bx, by); }).ToList();
                                sheet.TrySetCompositeBase(tickParts);
                                // Add placeholder frames so PlayAnimation/FindAnim works
                                var placeholders = new List<SpriteFrame>();
                                for (int pi = 0; pi < tickParts.Count; pi++)
                                    placeholders.Add(new SpriteFrame { Name = $"{kvp.Key}_{pi}", SourceRect = Rectangle.Empty });
                                sheet.Animations[kvp.Key] = placeholders;
                            }
                            else
                            {
                                // Store raw data temporarily so we can recompute RawCompositeOffset from idle later
                                sheet.RawCompositeData[kvp.Key] = tickParts;
                                // Store per-frame bounds for updateHitbox-style drawing
                                sheet.CompositeFrameOffsets[kvp.Key] = tickParts.Select(t =>
                                {
                                    var (minX, minY, _, _) = AnimatedSprite.ComputeRawBounds(t);
                                    return new Vector2(minX, minY);
                                }).ToList();
                                sheet.CompositeFrameSizes[kvp.Key] = tickParts.Select(t => AnimatedSprite.ComputeRawBoundingBox(t)).ToList(); sheet.CompositeFrameExtents[kvp.Key] = tickParts.Select(t => { var (ax, ay, bx, by) = AnimatedSprite.ComputeRawBounds(t); return (ax, ay, bx, by); }).ToList();
                                sheet.TrySetCompositeBase(tickParts);
                                if (LazyCompositeKeep != null && !LazyCompositeKeep.Contains(kvp.Key.Trim()))
                                {
                                    // UWP memory: rendered on first play (AnimatedSprite.EnsureCurrentFrameRendered)
                                    sheet.RawCompositeData.Remove(kvp.Key);
                                    sheet.JITComposites[kvp.Key] = new JITCompositeInfo(tickParts);
                                    sheet.CompositeLogicalSizes[kvp.Key] = AnimatedSprite.ComputeRawBoundingBox(tickParts[0]);
                                    var lazyPh = new List<SpriteFrame>();
                                    for (int pi = 0; pi < tickParts.Count; pi++) lazyPh.Add(new SpriteFrame { Name = $"{kvp.Key}_{pi}", SourceRect = Rectangle.Empty });
                                    sheet.Animations[kvp.Key] = lazyPh;
                                    continue;
                                }
                                var compositeFrames = PreRenderComposite(game.GraphicsDevice, sheet.Texture, tickParts);
                                if (compositeFrames.Count > 0)
                                {
                                    sheet.CompositeAnimations[kvp.Key] = compositeFrames;
                                    sheet.CompositeLogicalSizes[kvp.Key] = AnimatedSprite.ComputeRawBoundingBox(tickParts[0]);
                                    if (!sheet.CompositeBaseOrigin.HasValue)
                                        sheet.CompositeBaseOrigin = CompositeCells.LogicalOrigin(compositeFrames[0]);
                                    var simpleFrames = new List<SpriteFrame>();
                                    for (int i = 0; i < compositeFrames.Count; i++)
                                    {
                                        simpleFrames.Add(new SpriteFrame
                                        {
                                            Name = $"{kvp.Key}_{i}",
                                            SourceRect = compositeFrames[i].Rect,
                                            Offset = Vector2.Zero,
                                            FrameWidth = CompositeCells.LogicalSize(compositeFrames[i]).W,
                                            FrameHeight = CompositeCells.LogicalSize(compositeFrames[i]).H
                                        });
                                    }
                                    sheet.Animations[kvp.Key] = simpleFrames;
                                }
                            }
                        }
                        else
                        {
                            var simpleFrames = new List<SpriteFrame>();
                            foreach (var tick in tickParts)
                            {
                                if (tick.Count > 0)
                                    simpleFrames.Add(tick[0].Frame);
                            }
                            if (simpleFrames.Count > 0)
                                sheet.Animations[kvp.Key] = simpleFrames;
                        }
                    }
                    else
                    {
                        // Simple mode: pick the largest atlas sprite per tick (no pre-rendering)
                        var collected = new List<SpriteFrame>();
                        CollectLargestPerTick(kvp.Value, symbolsByName, framesByName, collected,
                            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                        if (collected.Count > 0)
                            sheet.Animations[kvp.Key] = collected;
                    }
                }
                // Build main timeline composite and split by frame labels (Enter, Idle, Confirm, etc.)
                // This is needed for animateatlas assets like character select where the main timeline
                // defines sub-animations via frame labels, compositing multiple SD symbols together.
                // Always process when mainTimeline exists � even without preRenderComposites, we need
                // to extract single-part frames (e.g. crowd has 0 symbols, all ASI on main timeline).
                if (mainTimeline != null)
                {
                    var labels = new SortedDictionary<int, string>();
                    var labelDur = new Dictionary<int, int>();   // label frame DU (animation = index .. index+DU-1)
                    var labelSeen = new HashSet<string>(StringComparer.Ordinal);
                    int mainDuration = 0;
                    foreach (var layer in mainTimeline)
                    {
                        var layerFrames = layer["FR"] ?? layer["Frames"];
                        if (layerFrames == null) continue;
                        foreach (var fd in layerFrames)
                        {
                            int idx = ReadFrameIndex(fd);
                            int dur = ReadFrameDuration(fd);
                            mainDuration = Math.Max(mainDuration, idx + dur);
                            string label = fd["N"]?.ToString();
                            if (!string.IsNullOrEmpty(label))
                            {
                                label = label.TrimEnd();
                                // flixel-animate findFrameLabelIndices: first layer containing the label wins
                                if (!labelSeen.Contains(label) && labels.TryAdd(idx, label)) { labelDur[idx] = dur; labelSeen.Add(label); }
                            }
                        }
                    }
                    // If no labels, treat the whole timeline as one animation named after the top-level symbol
                    string rootSymbolName = animData["AN"]?["SN"]?.ToString();
                    if (labels.Count == 0 && mainDuration > 0)
                        labels[0] = !string.IsNullOrEmpty(rootSymbolName) ? rootSymbolName : "default";
                    if (labels.Count >= 1 && mainDuration > 0 && mainDuration <= 2000)
                    {
                        var mainAN = animData["AN"];
                        // Build label ranges and compute the merged tick range we actually need
                        var labelList = labels.ToList();
                        var filterSet = preRenderFilter != null
                            ? new HashSet<string>(preRenderFilter, StringComparer.OrdinalIgnoreCase)
                            : null;
                        // Compute merged tick range from filtered labels to avoid processing
                        // unneeded frame entries (e.g. Cancel's 52 ticks when we only need Idle)
                        int tickMin = 0, tickMax = mainDuration;
                        if (filterSet != null)
                        {
                            tickMin = mainDuration;
                            tickMax = 0;
                            for (int i = 0; i < labelList.Count; i++)
                            {
                                string ln = labelList[i].Value;
                                if (!filterSet.Contains(ln)) continue;
                                int sf = labelList[i].Key;
                                int ef = labelDur.TryGetValue(sf, out int lduF) ? Math.Min(mainDuration, sf + lduF) : ((i + 1 < labelList.Count) ? labelList[i + 1].Key : mainDuration);
                                tickMin = Math.Min(tickMin, sf);
                                tickMax = Math.Max(tickMax, ef);
                            }
                            if (tickMin >= tickMax) { tickMin = 0; tickMax = mainDuration; }
                        }
                        var tickParts = new List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>>();
                        for (int t = 0; t < mainDuration; t++)
                            tickParts.Add(new List<(SpriteFrame, float, float, float, float, float, float)>());
                        // STI (Stage Instance Transform) is the authoring-time placement of the
                        // symbol on the Adobe Animate stage. Original FlxAnimate does NOT apply it
                        // during rendering unless applyStageMatrix() is explicitly called.
                        // Instead of processing mainAN (which bakes in SI M3D stage offsets),
                        // iterate the main timeline manually: resolve each SI to its nested symbol
                        // and collect body parts with identity parent (no stage transform).
                        // ASI elements (direct atlas sprites) keep their local transforms.
                        // Pre-rendered composites auto-compensate via bounding box origin;
                        // per-part rendering (characters) needs the clean local-space data.
                        var stlSymbolCache = new Dictionary<string, List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>>>(StringComparer.OrdinalIgnoreCase);
                        var mainLayers = mainTimeline.ToList();
                        for (int mli = mainLayers.Count - 1; mli >= 0; mli--)
                        {
                            var mlFrames = mainLayers[mli]["FR"] ?? mainLayers[mli]["Frames"];
                            if (mlFrames == null) continue;
                            foreach (var mfd in mlFrames)
                            {
                                int mfIdx = ReadFrameIndex(mfd);
                                int mfDur = ReadFrameDuration(mfd);
                                if (mfIdx + mfDur <= tickMin || mfIdx >= tickMax) continue;
                                var mfElements = mfd["E"] ?? mfd["elements"];
                                if (mfElements == null) continue;
                                int mfStart = Math.Max(mfIdx, tickMin);
                                int mfEnd = Math.Min(mfIdx + mfDur, mainDuration);
                                foreach (var mfEl in mfElements)
                                {
                                    // Direct atlas sprite on main timeline � keep local transform
                                    var mfAsi = mfEl["ASI"] ?? mfEl["ATLAS_SPRITE_instance"];
                                    if (mfAsi != null)
                                    {
                                        string fn = (mfAsi["N"] ?? mfAsi["name"])?.ToString() ?? "";
                                        if (framesByName.TryGetValue(fn, out var fr))
                                        {
                                            var mx = ReadTransform(mfAsi, false);
                                            for (int mt = mfStart; mt < mfEnd; mt++)
                                                tickParts[mt].Add((fr, mx.a, mx.b, mx.c, mx.d, mx.tx, mx.ty));
                                        }
                                    }
                                    // Nested symbol instance � resolve to SD symbol, collect body parts in
                                    // local space, then compose with SI's M3D + TRP for correct positioning.
                                    var mfSi = mfEl["SI"] ?? mfEl["SYMBOL_instance"] ?? mfEl["SYMBOL_Instance"];
                                    if (mfSi != null)
                                    {
                                        string nn = (mfSi["SN"] ?? mfSi["SYMBOL_name"])?.ToString() ?? "";
                                        if (!string.IsNullOrEmpty(nn) && symbolsByName.TryGetValue(nn, out var nSym))
                                        {
                                            int nDur = GetSymbolDuration(nSym);
                                            if (nDur <= 0) continue;
                                            if (!stlSymbolCache.TryGetValue(nn, out var localTicks))
                                            {
                                                localTicks = new List<List<(SpriteFrame, float, float, float, float, float, float)>>();
                                                for (int nt = 0; nt < nDur; nt++)
                                                    localTicks.Add(new List<(SpriteFrame, float, float, float, float, float, float)>());
                                                CollectPartsWithTransforms(nSym, symbolsByName, framesByName, localTicks,
                                                    1, 0, 0, 1, 0, 0,
                                                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                                                    applyTRP: applyTRP, symbolCache: stlSymbolCache);
                                                stlSymbolCache[nn] = localTicks;
                                            }
                                            int ff = ReadFirstFrame(mfSi);
                                            // Compose SI's M3D + TRP with cached local parts (matches FlxAnimate).
                                            // On the main timeline, each SI's raw (tx,ty) positions the
                                            // sub-symbol's TRP at that coordinate in parent space. The
                                            // AN.STI raw offset then maps parent space ? screen space.
                                            // Applying TRP here would double-offset: the bounding-box
                                            // composite origin can't compensate because the STI offset
                                            // is additive, not TRP-relative. Result: floor half off-screen.
                                            var si = ReadTransform(mfSi, applyTRP);
                                            var (mfAlpha, mfBlend) = ReadInstanceFx(mfSi);
                                            if (t_anchors != null)
                                                for (int mt = mfStart; mt < mfEnd; mt++)
                                                {
                                                    if (nn.IndexOf(t_anchorKw, StringComparison.OrdinalIgnoreCase) >= 0) AnchorAdd(tickParts, mt, (si.a, si.b, si.c, si.d, si.tx, si.ty, tickParts[mt].Count + localTicks[ResolveNestedTick(mfSi, ff, mt - mfIdx, nDur)].Count));
                                                    else AnchorCompose(tickParts, mt, localTicks, ResolveNestedTick(mfSi, ff, mt - mfIdx, nDur), si.a, si.b, si.c, si.d, si.tx, si.ty);
                                                }
                                            for (int mt = mfStart; mt < mfEnd; mt++)
                                            {
                                                int nTick = ResolveNestedTick(mfSi, ff, mt - mfIdx, nDur);
                                                foreach (var (frame0, ca, cb, cc, cd, ctx, cty) in localTicks[nTick])
                                                {
                                                    var frame = frame0.WithFx(mfAlpha, mfBlend);
                                                    // Compose: world = SI_transform � local_part
                                                    float wa = si.a * ca + si.c * cb;
                                                    float wb = si.b * ca + si.d * cb;
                                                    float wc = si.a * cc + si.c * cd;
                                                    float wd = si.b * cc + si.d * cd;
                                                    float wtx = si.a * ctx + si.c * cty + si.tx;
                                                    float wty = si.b * ctx + si.d * cty + si.ty;
                                                    tickParts[mt].Add((frame, wa, wb, wc, wd, wtx, wty));
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        bool hasParts = false;
                        foreach (var tick in tickParts)
                            if (tick.Count > 0) { hasParts = true; break; }
                        if (hasParts)
                        {
                            // flixel-animate Timeline.getWholeBounds(): union of EVERY main-timeline frame, including
                            // labels that are never played (e.g. picoBlazin 'Low Death Confirm') and unlabeled frames.
                            {
                                { var syms = symbolsByName; var frs = framesByName; sheet.SymbolBoundsFn = nm => syms.TryGetValue(nm, out var sy) ? ComputeFlxAnimateWholeBounds(sy["TL"]?["L"] ?? sy["TIMELINE"]?["LAYERS"], syms, frs) : null; }
                                try { sheet.WholeMainTimelineBounds = ComputeFlxAnimateWholeBounds(mainTimeline, symbolsByName, framesByName); }
                                catch (Exception ex) { Console.WriteLine($"[TL] bounds failed: {ex.Message}"); }
                                if (Environment.GetEnvironmentVariable("FNF_TLLOG") == "1") Console.WriteLine($"[TL] {animationJsonPath} whole={sheet.WholeMainTimelineBounds} labels={labelList.Count} ticks={tickParts.Count}");
                            }
                            for (int i = 0; i < labelList.Count; i++)
                            {
                                int startFrame = labelList[i].Key;
                                int endFrame = labelDur.TryGetValue(startFrame, out int ldu) ? Math.Min(mainDuration, startFrame + ldu) : ((i + 1 < labelList.Count) ? labelList[i + 1].Key : mainDuration);
                                string animName = labelList[i].Value;
                                sheet.TimelineLabelNames.Add(animName);
                                if (t_anchors != null && t_anchors.TryGetValue(tickParts, out var anl)) { int ac = Math.Min(endFrame, anl.Count) - startFrame; if (ac > 0) { sheet.Anchors[animName] = anl.GetRange(startFrame, ac); var spl = new int[ac]; bool anySpl = false; for (int k = 0; k < ac; k++) { spl[k] = -1; foreach (var a in anl[startFrame + k]) spl[k] = Math.Max(spl[k], a.Idx); if (spl[k] >= 0 && spl[k] < tickParts[startFrame + k].Count) anySpl = true; } if (anySpl) sheet.AnchorSplit[animName] = spl; } }
                                if (Environment.GetEnvironmentVariable("FNF_DUMP_PARTS") is string dumpSpec && animationJsonPath.Replace(Path.DirectorySeparatorChar, '/').Contains(dumpSpec.Split('|')[0]) && animName == dumpSpec.Split('|')[1])
                                {
                                    int t0 = labelList[i].Key;
                                    var sbd = new System.Text.StringBuilder();
                                    foreach (var pt in tickParts[t0]) sbd.AppendLine($"{pt.Frame.Name} {pt.A:F4} {pt.B:F4} {pt.C:F4} {pt.D:F4} {pt.TX:F2} {pt.TY:F2} rot={pt.Frame.Rotated} src={pt.Frame.SourceRect.Width}x{pt.Frame.SourceRect.Height}");
                                    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "parts_dump.txt"), sbd.ToString());
                                }
                                int count = endFrame - startFrame;
                                if (count <= 0) continue;
                                // When filter is set, only process matching labels
                                bool shouldProcess = filterSet == null || filterSet.Contains(animName);
                                if (shouldProcess)
                                {
                                    // Pre-render each label's ticks into composite textures.
                                    // Uses the same proven PreRenderComposite path as per-symbol
                                    // composites (DJ, crowd, speakers) � body parts are rendered
                                    // relative to a bounding box with a proper origin, avoiding
                                    // the fragility of absolute M3D screen-position drawing.
                                    var labelTicks = tickParts.GetRange(startFrame, Math.Min(count, tickParts.Count - startFrame));
                                    bool multiPart = false;
                                    foreach (var tick in labelTicks)
                                        if (tick.Count > 1) { multiPart = true; break; }
                                    // Single-part frames may still have non-identity STI/M3D transforms.
                                    bool hasTransforms = false;
                                    if (!multiPart)
                                    {
                                        foreach (var tick in labelTicks)
                                        {
                                            if (tick.Count == 1)
                                            {
                                                var (_, a, b, c, d, tx, ty) = tick[0];
                                                if (MathF.Abs(a - 1f) > 0.001f || MathF.Abs(d - 1f) > 0.001f ||
                                                    MathF.Abs(b) > 0.001f || MathF.Abs(c) > 0.001f ||
                                                    MathF.Abs(tx) > 0.001f || MathF.Abs(ty) > 0.001f)
                                                { hasTransforms = true; break; }
                                            }
                                        }
                                    }
                                    if ((multiPart || hasTransforms) && preRenderComposites)
                                    {
                                        if (deferComposites)
                                        {
                                            // Runtime compositing: store raw tick parts so Draw()
                                            // renders each body part directly with M3D transforms.
                                            // No GPU work needed � safe for background threads.
                                            sheet.RawCompositeData[animName] = labelTicks;
                                            // Cache FlxAnimate offset from first loaded animation
                                            if (sheet.RawCompositeOffset == Vector2.Zero && labelTicks.Count > 0)
                                            {
                                                var (lMinX, lMinY, _, _) = AnimatedSprite.ComputeRawBounds(labelTicks[0]);
                                                sheet.RawCompositeOffset = new Vector2(lMinX, lMinY);
                                            }
                                            // Store per-frame bounds for updateHitbox-style drawing
                                            sheet.CompositeFrameOffsets[animName] = labelTicks.Select(t =>
                                            {
                                                var (lMinX, lMinY, _, _) = AnimatedSprite.ComputeRawBounds(t);
                                                return new Vector2(lMinX, lMinY);
                                            }).ToList();
                                            sheet.CompositeFrameSizes[animName] = labelTicks.Select(t => AnimatedSprite.ComputeRawBoundingBox(t)).ToList(); sheet.CompositeFrameExtents[animName] = labelTicks.Select(t => { var (ax, ay, bx, by) = AnimatedSprite.ComputeRawBounds(t); return (ax, ay, bx, by); }).ToList();
                                            sheet.TrySetCompositeBase(labelTicks);
                                            // Add placeholder frames so PlayAnimation/FindAnim works
                                            var ph = new List<SpriteFrame>();
                                            for (int pi = 0; pi < labelTicks.Count; pi++)
                                                ph.Add(new SpriteFrame { Name = $"{animName}_{pi}", SourceRect = Rectangle.Empty });
                                            if (!sheet.Animations.ContainsKey(animName))
                                                sheet.Animations[animName] = ph;
                                        }
                                        else
                                        {
                                            // Store raw data temporarily so we can recompute RawCompositeOffset from idle later
                                            sheet.RawCompositeData[animName] = labelTicks;
                                            // Store per-frame bounds for updateHitbox-style drawing
                                            sheet.CompositeFrameOffsets[animName] = labelTicks.Select(t =>
                                            {
                                                var (lMinX, lMinY, _, _) = AnimatedSprite.ComputeRawBounds(t);
                                                return new Vector2(lMinX, lMinY);
                                            }).ToList();
                                            sheet.CompositeFrameSizes[animName] = labelTicks.Select(t => AnimatedSprite.ComputeRawBoundingBox(t)).ToList(); sheet.CompositeFrameExtents[animName] = labelTicks.Select(t => { var (ax, ay, bx, by) = AnimatedSprite.ComputeRawBounds(t); return (ax, ay, bx, by); }).ToList();
                                            sheet.TrySetCompositeBase(labelTicks);
                                            sheet.AnchorSplit.TryGetValue(animName, out var lblSplit);
                                            // UWP low-memory: labels no character JSON refers to are rendered on first play (JIT)
                                            bool lazyLbl = LazyCompositeKeep != null && lblSplit == null && !LazyCompositeKeep.Contains(animName.Trim());
                                            if (lazyLbl)
                                            {
                                                sheet.RawCompositeData.Remove(animName);
                                                sheet.JITComposites[animName] = new JITCompositeInfo(labelTicks);
                                                sheet.CompositeLogicalSizes[animName] = AnimatedSprite.ComputeRawBoundingBox(labelTicks[0]);
                                                var lazyPh = new List<SpriteFrame>();
                                                for (int pi = 0; pi < labelTicks.Count; pi++) lazyPh.Add(new SpriteFrame { Name = $"{animName}_{pi}", SourceRect = Rectangle.Empty });
                                                if (!sheet.Animations.ContainsKey(animName)) sheet.Animations[animName] = lazyPh;
                                            }
                                            var compositeFrames = lazyLbl ? new List<(Texture2D Tex, Rectangle Rect, Vector2 Origin)>() : PreRenderComposite(game.GraphicsDevice, sheet.Texture, labelTicks, lblSplit);
                                            if (lblSplit != null) sheet.CompositeAbove[animName] = PreRenderAbove(game.GraphicsDevice, sheet.Texture, labelTicks, lblSplit);
                                            if (compositeFrames.Count > 0)
                                            {
                                                sheet.CompositeAnimations[animName] = compositeFrames;
                                                // Store FIRST frame's bounding box as logical size (idle bounds for updateHitbox)
                                                sheet.CompositeLogicalSizes[animName] = AnimatedSprite.ComputeRawBoundingBox(labelTicks[0]);
                                                var subFrames = new List<SpriteFrame>();
                                                for (int f = 0; f < compositeFrames.Count; f++)
                                                {
                                                    subFrames.Add(new SpriteFrame
                                                    {
                                                        Name = $"{animName}_{f}",
                                                        SourceRect = compositeFrames[f].Rect,
                                                        Offset = Vector2.Zero,
                                                        FrameWidth = CompositeCells.LogicalSize(compositeFrames[f]).W,
                                                        FrameHeight = CompositeCells.LogicalSize(compositeFrames[f]).H
                                                    });
                                                }
                                                if (!sheet.Animations.ContainsKey(animName))
                                                    sheet.Animations[animName] = subFrames;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        // Single-part ticks: use direct sprite frames
                                        var subFrames = new List<SpriteFrame>();
                                        foreach (var tick in labelTicks)
                                        {
                                            if (tick.Count > 0)
                                                subFrames.Add(tick[0].Frame);
                                        }
                                        if (subFrames.Count > 0 && !sheet.Animations.ContainsKey(animName))
                                            sheet.Animations[animName] = subFrames;
                                    }
                                }
                                // Skip non-matching labels entirely when filter is set �
                                // no need to build simple frames for unused animations
                                else if (filterSet == null)
                                {
                                    // No filter: register simple frames for all labels
                                    var subTicks = tickParts.GetRange(startFrame, Math.Min(count, tickParts.Count - startFrame));
                                    var simpleFrames = new List<SpriteFrame>();
                                    foreach (var tick in subTicks)
                                    {
                                        if (tick.Count > 0)
                                            simpleFrames.Add(tick[0].Frame);
                                    }
                                    if (simpleFrames.Count > 0 && !sheet.Animations.ContainsKey(animName))
                                        sheet.Animations[animName] = simpleFrames;
                                }
                            }
                            Console.WriteLine($"  Main timeline split into {labelList.Count} label animations: {string.Join(", ", labelList.Select(l => l.Value))}");
                        }
                    }
                }
                // Safety fallback: if Animation.json was parsed but no animations were created
                // (e.g. no symbols AND main timeline didn't produce anything),
                // create a default animation from all loaded frames.
                if (sheet.Animations.Count == 0 && sheet.Frames.Count > 0)
                    sheet.Animations["default"] = sheet.Frames.Values.ToList();
            }
            else
            {
                sheet.Animations["default"] = sheet.Frames.Values.ToList();
            }
            Console.WriteLine($"Loaded spritemap: {sheet.Frames.Count} frames, {sheet.Animations.Count} animations, {sheet.CompositeAnimations.Count} composite");
            foreach (var anim in sheet.Animations.Keys.Take(15))
            {
                Console.WriteLine($"  Animation: '{anim}' ({sheet.Animations[anim].Count} frames){(sheet.CompositeAnimations.ContainsKey(anim) ? " [composite]" : "")}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading spritemap: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
        // Recompute RawCompositeOffset from the idle animation (or fallback) to match
        // original FlxAnimate updateHitbox() which computes offset from the idle frame.
        string[] preferredAnims = { "idle", "danceRight", "danceLeft", "default" };
        foreach (var animName in preferredAnims)
        {
            if (sheet.RawCompositeData.TryGetValue(animName, out var idleTicks) && idleTicks.Count > 0)
            {
                var (ix, iy, _, _) = AnimatedSprite.ComputeRawBounds(idleTicks[0]);
                sheet.RawCompositeOffset = new Vector2(ix, iy);
                break;
            }
        }
        if (sheet.RawCompositeOffset == Vector2.Zero && sheet.RawCompositeData.Count > 0)
        {
            var firstKvp = sheet.RawCompositeData.First();
            var (fx, fy, _, _) = AnimatedSprite.ComputeRawBounds(firstKvp.Value[0]);
            sheet.RawCompositeOffset = new Vector2(fx, fy);
        }
        // Compute a fixed global base origin from the preferred idle/Freeplay DJ animation.
        // This prevents the anchor from changing when switching animations or when the
        // last processed animation has different bounds than the idle.
        string[] originCandidates = { "idle", "Boyfriend DJ", "Pico DJ", "danceLeft", "danceRight", "default" };
        bool exactOriginFound = false;
        foreach (var animName in originCandidates)
        {
            if (sheet.RawCompositeData.TryGetValue(animName, out var originTicks) && originTicks.Count > 0)
            {
                float gMinX = float.MaxValue, gMinY = float.MaxValue;
                foreach (var tick in originTicks)
                {
                    var (minX, minY, _, _) = AnimatedSprite.ComputeRawBounds(tick);
                    gMinX = Math.Min(gMinX, minX);
                    gMinY = Math.Min(gMinY, minY);
                }
                sheet.CompositeBaseOrigin = new Vector2(-gMinX, -gMinY);
                exactOriginFound = true;
                break;
            }
        }
        // V-Slice 0.8 atlases (bf, tankman, ...) name their main-timeline labels "Idle", "Left", ...
        // (capitalised) and ALSO contain per-symbol animations in symbol-local space. Without a
        // case-insensitive match the anchor fell back to a symbol-space animation, offsetting every
        // label animation (BF was drawn ~1000px off-screen). Anchor origin, base offset and base size
        // to the same idle label so all three share one coordinate space (FlxAnimate updateHitbox
        // after dance(true)).
        if (!exactOriginFound)
        {
            foreach (var cand in new[] { "idle", "danceLeft", "danceRight" })
            {
                var key = sheet.RawCompositeData.Keys.FirstOrDefault(k => string.Equals(k, cand, StringComparison.OrdinalIgnoreCase));
                if (key == null || sheet.RawCompositeData[key].Count == 0) continue;
                var originTicks = sheet.RawCompositeData[key];
                float gMinX = float.MaxValue, gMinY = float.MaxValue;
                foreach (var tick in originTicks)
                {
                    var (minX, minY, _, _) = AnimatedSprite.ComputeRawBounds(tick);
                    gMinX = Math.Min(gMinX, minX);
                    gMinY = Math.Min(gMinY, minY);
                }
                sheet.CompositeBaseOrigin = new Vector2(-gMinX, -gMinY);
                var (bx, by, _, _) = AnimatedSprite.ComputeRawBounds(originTicks[0]);
                sheet.CompositeBaseOffset = new Vector2(bx, by);
                sheet.CompositeBaseSize = AnimatedSprite.ComputeRawBoundingBox(originTicks[0]);
                break;
            }
        }
        // Fallback: if no preferred animation was found, use the first available composite.
        if (sheet.CompositeBaseOrigin == null && sheet.RawCompositeData.Count > 0)
        {
            var firstKvp = sheet.RawCompositeData.First();
            float gMinX = float.MaxValue, gMinY = float.MaxValue;
            foreach (var tick in firstKvp.Value)
            {
                var (minX, minY, _, _) = AnimatedSprite.ComputeRawBounds(tick);
                gMinX = Math.Min(gMinX, minX);
                gMinY = Math.Min(gMinY, minY);
            }
            sheet.CompositeBaseOrigin = new Vector2(-gMinX, -gMinY);
        }
        // For non-deferred paths, CompositeAnimations is already populated.
        // Remove RawCompositeData entries that have matching CompositeAnimations
        // so Draw() uses the pre-rendered path instead of raw compositing.
        foreach (var animName in sheet.CompositeAnimations.Keys.ToArray())
        {
            if (sheet.RawCompositeData.ContainsKey(animName))
                sheet.RawCompositeData.Remove(animName);
        }
        return sheet;
    }
    /// <summary>
    /// Get the total timeline duration of a symbol.
    /// </summary>
    private static int GetSymbolDuration(JToken symbol)
    {
        var layers = symbol["TL"]?["L"] ?? symbol["TIMELINE"]?["LAYERS"];
        if (layers == null) return 0;
        int totalDuration = 0;
        foreach (var layer in layers)
        {
            var layerFrames = layer["FR"] ?? layer["Frames"];
            if (layerFrames == null) continue;
            foreach (var frameData in layerFrames)
            {
                int index = ReadFrameIndex(frameData);
                int dur = ReadFrameDuration(frameData);
                totalDuration = Math.Max(totalDuration, index + dur);
            }
        }
        return totalDuration;
    }
    /// <summary>
    /// Read a 2D affine transform from an element token.
    /// Supports both MX [a,b,c,d,tx,ty] and M3D [16-element 4x4 matrix] formats.
    /// Applies TRP (Transformation Reference Point) adjustment to the translation,
    /// matching FlxAnimate's parseMatrix behavior where TRP defines the pivot point.
    /// </summary>
    private static (float a, float b, float c, float d, float tx, float ty) ReadTransform(JToken elementToken, bool applyTRP = true)
    {
        float a = 1, b = 0, c = 0, d = 1, tx = 0, ty = 0;
        // Try MX first (2x3 matrix: [a, b, c, d, tx, ty])
        var mx = elementToken?["MX"] as JArray;
        if (mx != null && mx.Count >= 6)
        {
            a = mx[0].Value<float>();
            b = mx[1].Value<float>();
            c = mx[2].Value<float>();
            d = mx[3].Value<float>();
            tx = mx[4].Value<float>();
            ty = mx[5].Value<float>();
        }
        else
        {
            // Try M3D (4x4 matrix stored as 16-element flat array)
            // Layout: [a, b, 0, 0, c, d, 0, 0, 0, 0, 1, 0, tx, ty, 0, 1]
            var m3d = elementToken?["M3D"] as JArray;
            if (m3d != null && m3d.Count >= 16)
            {
                a = m3d[0].Value<float>();
                b = m3d[1].Value<float>();
                c = m3d[4].Value<float>();
                d = m3d[5].Value<float>();
                tx = m3d[12].Value<float>();
                ty = m3d[13].Value<float>();
            }
            else
            {
                // Try long-form Matrix3D object (row-major 4x4 with m00..m33 keys)
                // Row 0: [m00=a, m01=b, ...], Row 1: [m10=c, m11=d, ...], Row 3: [m30=tx, m31=ty, ...]
                var m3dObj = elementToken?["Matrix3D"];
                if (m3dObj != null && m3dObj.Type == JTokenType.Object)
                {
                    a = m3dObj["m00"]?.Value<float>() ?? 1;
                    b = m3dObj["m01"]?.Value<float>() ?? 0;
                    c = m3dObj["m10"]?.Value<float>() ?? 0;
                    d = m3dObj["m11"]?.Value<float>() ?? 1;
                    tx = m3dObj["m30"]?.Value<float>() ?? 0;
                    ty = m3dObj["m31"]?.Value<float>() ?? 0;
                }
            }
        }
        // Apply TRP (Transformation Reference Point) — matches FlxAnimate exactly.
        // FlxAnimate calls matrix.translate(-trp.x, -trp.y) AFTER setting a,b,c,d,tx,ty.
        // OpenFL's Matrix.translate is a SIMPLE addition (NOT matrix-multiplied):
        //   tx += dx;  ty += dy;
        // So TRP application is just plain subtraction from tx/ty. This gives proper
        // mirror symmetry for flipped instances (a=-1): both sides land symmetrically
        // around the SI's M3D translation, which is the authoring-intended behavior.
        var trp = applyTRP ? (elementToken?["TRP"] ?? elementToken?["transformationPoint"]) : null;
        if (trp != null)
        {
            float trpX = trp["x"]?.Value<float>() ?? 0;
            float trpY = trp["y"]?.Value<float>() ?? 0;
            tx -= trpX;
            ty -= trpY;
        }
        return (a, b, c, d, tx, ty);
    }

    // ------------------------------------------------------------------------------------------------
    // flixel-animate Timeline.getWholeBounds(includeHiddenLayers=false, includeFilters=true), ported 1:1:
    // every frame's bounds are the union of its layers' frame bounds in timeline-local space; a symbol
    // instance contributes the AABB of its symbol timeline's local bounds transformed by its matrix
    // (AABB-of-AABB, not the tight union of parts), MovieClips are frozen on frame 0 and expanded by
    // their blur/glow filter extents, atlas instances are their (rotated) frame rect transformed.
    // Verified: picoBlazin 1272x945, darnellBlazin 729x643, nene 493x566 (= original traces).
    // ------------------------------------------------------------------------------------------------
    private struct TlRect { public float X, Y, W, H; public bool Empty => W == 0f || H == 0f;
        public TlRect(float x, float y, float w, float h) { X = x; Y = y; W = w; H = h; } }

    private sealed class TlBoundsCtx
    {
        public Dictionary<string, JToken> Symbols;
        public Dictionary<string, SpriteFrame> Frames;
        public readonly Dictionary<string, TlRect> Cache = new(StringComparer.Ordinal);
        public readonly Dictionary<JToken, int> FrameCounts = new();
        public int Depth;
    }

    private static TlRect TlApply(TlRect r, (float a, float b, float c, float d, float tx, float ty) m)
    {
        if (r.Empty) return new TlRect(m.tx, m.ty, 0, 0);
        float l = r.X, t = r.Y, rr = r.X + r.W, bb = r.Y + r.H;
        float x0 = m.a * l + m.c * t, x1 = m.a * rr + m.c * t, x2 = m.a * l + m.c * bb, x3 = m.a * rr + m.c * bb;
        float y0 = m.b * l + m.d * t, y1 = m.b * rr + m.d * t, y2 = m.b * l + m.d * bb, y3 = m.b * rr + m.d * bb;
        float minX = Math.Min(Math.Min(x0, x1), Math.Min(x2, x3)), maxX = Math.Max(Math.Max(x0, x1), Math.Max(x2, x3));
        float minY = Math.Min(Math.Min(y0, y1), Math.Min(y2, y3)), maxY = Math.Max(Math.Max(y0, y1), Math.Max(y2, y3));
        return new TlRect(minX + m.tx, minY + m.ty, maxX - minX, maxY - minY);
    }

    private static TlRect TlExpand(TlRect a, TlRect e)
    {
        float x = Math.Min(a.X, e.X), y = Math.Min(a.Y, e.Y);
        return new TlRect(x, y, Math.Max(a.X + a.W, e.X + e.W) - x, Math.Max(a.Y + a.H, e.Y + e.H) - y);
    }

    private static JToken TlLayers(JToken timelineOwner)
        => timelineOwner?["TL"]?["L"] ?? timelineOwner?["TIMELINE"]?["LAYERS"];

    private static int TlFrameCount(JToken layers, TlBoundsCtx ctx)
    {
        if (layers == null) return 0;
        if (ctx.FrameCounts.TryGetValue(layers, out int n)) return n;
        n = 0;
        foreach (var layer in layers)
        {
            var frs = layer["FR"] ?? layer["Frames"];
            if (frs == null) continue;
            foreach (var fd in frs) n = Math.Max(n, ReadFrameIndex(fd) + ReadFrameDuration(fd));
        }
        ctx.FrameCounts[layers] = n;
        return n;
    }

    private static TlRect TlLocalBounds(JToken layers, int frameIndex, TlBoundsCtx ctx)
    {
        var rect = new TlRect(0, 0, 0, 0);
        if (layers == null || ctx.Depth > 64) return rect;
        bool first = true;
        ctx.Depth++;
        foreach (var layer in layers)
        {
            var frs = layer["FR"] ?? layer["Frames"];
            if (frs == null) continue;
            JToken frame = null; int fIdx = 0;
            foreach (var fd in frs)
            {
                int i0 = ReadFrameIndex(fd), du = ReadFrameDuration(fd);
                if (frameIndex >= i0 && frameIndex < i0 + du) { frame = fd; fIdx = i0; break; }
            }
            var els = frame?["E"] ?? frame?["elements"];
            if (els == null || !els.HasValues) continue;
            TlRect fb = default; bool firstEl = true;
            foreach (var el in els)
            {
                var eb = TlElementBounds(el, frameIndex - fIdx, ctx);
                fb = firstEl ? eb : TlExpand(fb, eb);
                firstEl = false;
            }
            if (fb.Empty) continue;
            var clipper = layer["__clipper"];
            if (clipper != null)
            {
                // Frame.getBounds: masked layer -> Timeline.maskBounds(rect, parentLayer.getBounds(frameIndex))
                var mb = TlLocalBounds(new JArray(clipper), frameIndex, ctx);
                if (!mb.Empty)
                {
                    float x1 = Math.Max(fb.X, mb.X), y1 = Math.Max(fb.Y, mb.Y);
                    float x2 = Math.Min(fb.X + fb.W, mb.X + mb.W), y2 = Math.Min(fb.Y + fb.H, mb.Y + mb.H);
                    fb = (x2 <= x1 || y2 <= y1) ? new TlRect(0, 0, 0, 0) : new TlRect(x1, y1, x2 - x1, y2 - y1);
                    if (fb.Empty) continue;
                }
            }
            rect = first ? fb : TlExpand(rect, fb);
            first = false;
        }
        ctx.Depth--;
        return rect;
    }

    private static TlRect TlElementBounds(JToken el, int rel, TlBoundsCtx ctx)
    {
        var asi = el["ASI"] ?? el["ATLAS_SPRITE_instance"];
        if (asi != null)
        {
            string n = (asi["N"] ?? asi["name"])?.ToString();
            float w = 0, h = 0;
            if (n != null && ctx.Frames.TryGetValue(n, out var f))
            {
                w = f.Rotated ? f.SourceRect.Height : f.SourceRect.Width;
                h = f.Rotated ? f.SourceRect.Width : f.SourceRect.Height;
            }
            return TlApply(new TlRect(0, 0, w, h), ReadTransform(asi, false));
        }
        var si = el["SI"] ?? el["SYMBOL_Instance"];
        if (si == null) return new TlRect(0, 0, 0, 0);
        var m = ReadTransform(si, false);
        string sn = (si["SN"] ?? si["SYMBOL_name"])?.ToString();
        if (sn == null || !ctx.Symbols.TryGetValue(sn, out var sym)) return TlApply(new TlRect(0, 0, 0, 0), m);
        var layers = TlLayers(sym);
        int fc = TlFrameCount(layers, ctx);
        string st = (si["ST"] ?? si["symbolType"])?.ToString() ?? "G";
        bool mc = st == "MC" || st.Equals("movieclip", StringComparison.OrdinalIgnoreCase);
        int fi;
        if (mc) fi = 0;   // MovieClipInstance.getFrameIndex: swfMode false -> 0
        else
        {
            int ff = (si["FF"] ?? si["firstFrame"])?.Value<int>() ?? 0;
            fi = ff + rel;
            string lp = (si["LP"] ?? si["loop"])?.ToString() ?? "LP";
            if (lp == "LP" || lp.Equals("loop", StringComparison.OrdinalIgnoreCase)) fi = fc > 0 ? ((fi % fc) + fc) % fc : 0;
            else if (lp == "PO" || lp.Equals("playonce", StringComparison.OrdinalIgnoreCase)) fi = Math.Min(fi, fc - 1);
            else if (lp == "SF" || lp.Equals("singleframe", StringComparison.OrdinalIgnoreCase)) fi = ff;
        }
        string key = sn + "#" + fi;
        if (!ctx.Cache.TryGetValue(key, out var local)) { local = TlLocalBounds(layers, fi, ctx); ctx.Cache[key] = local; }
        var r = TlApply(local, m);
        var filters = si["F"] ?? si["filters"];
        if (mc && filters is JObject fo)
        {
            float ix = 0, iy = 0, iw = 0, ih = 0;
            foreach (var kv in fo)
            {
                bool blur = kv.Key == "BLF" || kv.Key == "blurFilter";
                bool glow = kv.Key == "GF" || kv.Key == "glowFilter";
                if (!blur && !glow) continue;
                if (glow && ((kv.Value["IN"] ?? kv.Value["inner"])?.Value<bool>() ?? false)) continue;
                float bx = (kv.Value["BLX"] ?? kv.Value["blurX"])?.Value<float>() ?? 0f;
                float by = (kv.Value["BLY"] ?? kv.Value["blurY"])?.Value<float>() ?? 0f;
                float lx = bx > 0 ? MathF.Ceiling(bx) : 0, ly = by > 0 ? MathF.Ceiling(by) : 0;
                ix -= lx; iw += 2 * lx; iy -= ly; ih += 2 * ly;
            }
            r = new TlRect(Math.Min(r.X, r.X + ix), Math.Min(r.Y, r.Y + iy), Math.Max(r.W, r.W + iw), Math.Max(r.H, r.H + ih));
        }
        return r;
    }

    /// <summary>Timeline.getWholeBounds() of a timeline's layers (main timeline or a symbol's).</summary>
    private static (float X, float Y, float W, float H)? ComputeFlxAnimateWholeBounds(JToken layers,
        Dictionary<string, JToken> symbols, Dictionary<string, SpriteFrame> frames)
    {
        var ctx = new TlBoundsCtx { Symbols = symbols, Frames = frames };
        int n = TlFrameCount(layers, ctx);
        TlRect? acc = null;
        for (int i = 0; i < n; i++)
        {
            var fb = TlLocalBounds(layers, i, ctx);
            if (fb.Empty) continue;
            acc = acc.HasValue ? TlExpand(acc.Value, fb) : fb;
        }
        return acc.HasValue ? (acc.Value.X, acc.Value.Y, acc.Value.W, acc.Value.H) : null;
    }

    private static int ReadFrameIndex(JToken frameData)
    {
        return frameData?["I"]?.Value<int>()
            ?? frameData?["index"]?.Value<int>()
            ?? 0;
    }
    private static int ReadFrameDuration(JToken frameData)
    {
        return frameData?["DU"]?.Value<int>()
            ?? frameData?["duration"]?.Value<int>()
            ?? 1;
    }
    /// <summary>
    /// flixel-animate SymbolInstance.getFrameIndex: LOOP wraps, PLAY_ONCE clamps, SINGLE_FRAME holds firstFrame;
    /// MovieClipInstance (ST "MC") with swfMode=false (FunkinSprite default) always shows frame 0.
    /// </summary>
    private static int ResolveNestedTick(JToken si, int firstFrame, int relTick, int duration)
    {
        string st = (si?["ST"] ?? si?["symbolType"])?.ToString();
        if (st == "MC" || st == "movieclip") return 0;
        int f = firstFrame + relTick;
        string lp = (si?["LP"] ?? si?["loop"])?.ToString();
        if (lp == "PO" || lp == "playonce") return Math.Clamp(f, 0, duration - 1);
        if (lp == "SF" || lp == "singleframe") return Math.Clamp(firstFrame, 0, duration - 1);
        return ((f % duration) + duration) % duration;
    }

    /// <summary>Instance ColorTransform alpha multiplier (C.M = CA/AD) and MovieClip blend (B).</summary>
    private static (float Alpha, int Blend) ReadInstanceFx(JToken si)
    {
        float alpha = 1f;
        var c = si?["C"] ?? si?["color"];
        if (c != null)
        {
            string m = (c["M"] ?? c["mode"])?.ToString();
            if (m == "CA" || m == "Alpha" || m == "AD" || m == "Advanced")
                alpha = (c["AM"] ?? c["alphaMultiplier"])?.Value<float>() ?? 1f;
        }
        int blend = 10;
        string st = (si?["ST"] ?? si?["symbolType"])?.ToString();
        if ((st == "MC" || st == "movieclip") && si?["B"] != null)
            blend = si["B"].Value<int>();
        return (alpha, blend);
    }

    private static int ReadFirstFrame(JToken symbolInstance)
    {
        return symbolInstance?["FF"]?.Value<int>()
            ?? symbolInstance?["firstFrame"]?.Value<int>()
            ?? 0;
    }
    /// <summary>
    /// Recursively collect all atlas sprites visible at each tick of a symbol's timeline,
    /// with their accumulated affine transforms (rotation, scale, translation).
    /// tickMin/tickMax limit processing to frame entries overlapping [tickMin, tickMax),
    /// skipping expensive recursive work for ticks we don't need (e.g. Cancel/Exit labels
    /// when we only need Idle). Only used at the top-level call � nested symbols always
    /// process their full range since FF mapping can reference any internal tick.
    /// symbolCache caches each nested symbol's local-space tick parts so symbols referenced
    /// many times (e.g. NENE CS referenced 57� in pico spectator) are computed only once.
    /// </summary>
    private static void CollectPartsWithTransforms(
        JToken symbol,
        Dictionary<string, JToken> allSymbols,
        Dictionary<string, SpriteFrame> framesByName,
        List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>> tickParts,
        float pa, float pb, float pc, float pd, float ptx, float pty,
        HashSet<string> visited,
        bool applyTRP = true,
        int tickMin = 0, int tickMax = int.MaxValue,
        Dictionary<string, List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>>> symbolCache = null)
    {
        string symName = (symbol["SN"] ?? symbol["SYMBOL_name"])?.ToString() ?? "";
        if (!visited.Add(symName)) return;
        var layers = symbol["TL"]?["L"] ?? symbol["TIMELINE"]?["LAYERS"];
        if (layers == null) { visited.Remove(symName); return; }
        int totalDuration = tickParts.Count;
        // Process layers in reverse order (last layer = drawn first/behind)
        var layerList = layers.ToList();
        for (int li = layerList.Count - 1; li >= 0; li--)
        {
            var layer = layerList[li];
            var layerFrames = layer["FR"] ?? layer["Frames"];
            if (layerFrames == null) continue;
            if (layer["__hidden"] != null) continue; // layer.visible = false set by a character script AFTER the bounds were cached (still counted in bounds)
            // Masked layer (Clpb): collect the clipper layer in the same space; parts added below get tagged with it.
            List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>> clipTicks = null;
            int[] layerStart = null;
            if (layer["__clipper"] is JToken clipLayer && !NoMasks)
            {
                clipTicks = new List<List<(SpriteFrame, float, float, float, float, float, float)>>(totalDuration);
                for (int t = 0; t < totalDuration; t++) clipTicks.Add(new List<(SpriteFrame, float, float, float, float, float, float)>());
                var clipSym = new JObject { ["SN"] = "__clip_" + symName + "_" + li, ["TL"] = new JObject { ["L"] = new JArray(clipLayer.DeepClone()) } };
                CollectPartsWithTransforms(clipSym, allSymbols, framesByName, clipTicks, pa, pb, pc, pd, ptx, pty,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase), applyTRP, tickMin, tickMax, null);
                layerStart = new int[totalDuration];
                for (int t = 0; t < totalDuration; t++) layerStart[t] = tickParts[t].Count;
            }
            foreach (var frameData in layerFrames)
            {
                int index = ReadFrameIndex(frameData);
                int duration = ReadFrameDuration(frameData);
                // Skip frame entries entirely outside the needed tick range.
                // This avoids expensive recursive symbol traversal for labels we don't need
                // (e.g. skipping Cancel's 52 ticks when we only need Idle + Confirm).
                if (index + duration <= tickMin || index >= tickMax) continue;
                var elements = frameData["E"] ?? frameData["elements"];
                if (elements == null) continue;
                // Clamp the tick range we actually populate
                int tStart = Math.Max(index, tickMin);
                int tEnd = Math.Min(index + duration, Math.Min(totalDuration, tickMax));
                foreach (var element in elements)
                {
                    // Direct atlas sprite
                    var asi = element["ASI"] ?? element["ATLAS_SPRITE_instance"];
                    if (asi != null)
                    {
                        string frameName = (asi["N"] ?? asi["name"])?.ToString() ?? "";
                        if (framesByName.TryGetValue(frameName, out var frame))
                        {
                            var mx = ReadTransform(asi, applyTRP);
                            // Compose: world = parent � child
                            float wa = pa * mx.a + pc * mx.b;
                            float wb = pb * mx.a + pd * mx.b;
                            float wc = pa * mx.c + pc * mx.d;
                            float wd = pb * mx.c + pd * mx.d;
                            float wtx = pa * mx.tx + pc * mx.ty + ptx;
                            float wty = pb * mx.tx + pd * mx.ty + pty;
                            for (int t = tStart; t < tEnd; t++)
                            {
                                tickParts[t].Add((frame, wa, wb, wc, wd, wtx, wty));
                            }
                        }
                    }
                    // Nested symbol instance
                    var si = element["SI"] ?? element["SYMBOL_instance"] ?? element["SYMBOL_Instance"];
                    if (si != null)
                    {
                        string nestedName = (si["SN"] ?? si["SYMBOL_name"])?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(nestedName) && allSymbols.TryGetValue(nestedName, out var nestedSym))
                        {
                            var mx = ReadTransform(si, applyTRP);
                            // Compose parent � child matrix
                            float ca = pa * mx.a + pc * mx.b;
                            float cb = pb * mx.a + pd * mx.b;
                            float cc = pa * mx.c + pc * mx.d;
                            float cd = pb * mx.c + pd * mx.d;
                            float ctx = pa * mx.tx + pc * mx.ty + ptx;
                            float cty = pb * mx.tx + pd * mx.ty + pty;
                            int nestedDuration = GetSymbolDuration(nestedSym);
                            if (nestedDuration > 0)
                            {
                                // Try to use cached local-space tick parts for this symbol.
                                // Symbols like NENE CS are referenced 57� from different keyframes;
                                // computing them once in local space avoids exponential re-traversal.
                                List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>> localTicks = null;
                                bool cached = symbolCache != null && symbolCache.TryGetValue(nestedName, out localTicks);
                                if (!cached)
                                {
                                    // Compute nested symbol in local space (identity parent matrix)
                                    localTicks = new List<List<(SpriteFrame, float, float, float, float, float, float)>>();
                                    for (int nt = 0; nt < nestedDuration; nt++)
                                        localTicks.Add(new List<(SpriteFrame, float, float, float, float, float, float)>());
                                    // Use a fresh visited set for cache computation to avoid
                                    // contamination from the current recursion context
                                    var cacheVisited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                    CollectPartsWithTransforms(nestedSym, allSymbols, framesByName, localTicks,
                                        1, 0, 0, 1, 0, 0, cacheVisited, applyTRP, symbolCache: symbolCache);
                                    if (symbolCache != null)
                                        symbolCache[nestedName] = localTicks;
                                }
                                // Map cached local parts onto parent ticks, composing parent transform
                            int firstFrame = ReadFirstFrame(si);
                                if (t_anchors != null)
                                {
                                    int ff0 = ReadFirstFrame(si);
                                    for (int t = tStart; t < tEnd; t++)
                                    {
                                        if (nestedName.IndexOf(t_anchorKw, StringComparison.OrdinalIgnoreCase) >= 0) AnchorAdd(tickParts, t, (ca, cb, cc, cd, ctx, cty, tickParts[t].Count + localTicks[ResolveNestedTick(si, ff0, t - index, nestedDuration)].Count));
                                        else AnchorCompose(tickParts, t, localTicks, ResolveNestedTick(si, ff0, t - index, nestedDuration), ca, cb, cc, cd, ctx, cty);
                                    }
                                }
                                var (siAlpha, siBlend) = ReadInstanceFx(si);
                                for (int t = tStart; t < tEnd; t++)
                                {
                                    int nestedTick = ResolveNestedTick(si, firstFrame, t - index, nestedDuration);
                                    var parts = localTicks[nestedTick];
                                    foreach (var (frame0, la, lb, lc, ld, ltx, lty) in parts)
                                    {
                                        var frame = frame0.WithFx(siAlpha, siBlend);
                                        // Compose: world = parent(ca..cty) � local(la..lty)
                                        float wa = ca * la + cc * lb;
                                        float wb = cb * la + cd * lb;
                                        float wc2 = ca * lc + cc * ld;
                                        float wd = cb * lc + cd * ld;
                                        float wtx = ca * ltx + cc * lty + ctx;
                                        float wty = cb * ltx + cd * lty + cty;
                                        tickParts[t].Add((frame, wa, wb, wc2, wd, wtx, wty));
                                    }
                                }
                            }
                        }
                    }
                }
            }
            if (clipTicks != null) TagMasked(tickParts, layerStart, clipTicks);
        }
        visited.Remove(symName);
    }

    /// <summary>Set FNF_NOMASK=1 to draw masked layers unclipped (old behaviour, for comparisons).</summary>
    internal static readonly bool NoMasks = Environment.GetEnvironmentVariable("FNF_NOMASK") == "1";

    /// <summary>Tags the parts a masked layer added at each tick with its masker (relative to each part).
    /// An empty masker frame renders the layer normally (FilterRenderer.maskFrame returns null).</summary>
    private static void TagMasked(List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>> tickParts,
        int[] start, List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>> clipTicks)
    {
        for (int t = 0; t < tickParts.Count && t < start.Length; t++)
        {
            var clip = clipTicks[t];
            if (clip.Count == 0 || tickParts[t].Count <= start[t]) continue;
            var group = new object();
            for (int i = start[t]; i < tickParts[t].Count; i++)
            {
                var (f, a, b, c, d, tx, ty) = tickParts[t][i];
                float det = a * d - b * c;
                if (MathF.Abs(det) < 1e-8f) continue;
                // inverse of [a c tx; b d ty]
                float ia = d / det, ib = -b / det, ic = -c / det, id = a / det;
                float itx = -(ia * tx + ic * ty), ity = -(ib * tx + id * ty);
                var rel = new List<(SpriteFrame, float, float, float, float, float, float)>(clip.Count);
                foreach (var (mf, ma, mb, mc, md, mtx, mty) in clip)
                    rel.Add((mf, ia * ma + ic * mb, ib * ma + id * mb, ia * mc + ic * md, ib * mc + id * md,
                             ia * mtx + ic * mty + itx, ib * mtx + id * mty + ity));
                tickParts[t][i] = (f.WithMask(new PartMask { Group = group, Rel = rel }), a, b, c, d, tx, ty);
            }
        }
    }
    /// <summary>
    /// Pre-render composite frames into a single atlas texture.
    /// All unique frames are tiled into one RenderTarget2D to avoid creating
    /// hundreds of individual RTs (which caused UI freezes during loading).
    /// Applies full affine transforms (rotation, scale, translation) for each body part.
    /// </summary>
    private static List<(Texture2D Tex, Rectangle Rect, Vector2 Origin)> PreRenderComposite(
        GraphicsDevice graphicsDevice,
        Texture2D atlas,
        List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>> tickParts,
        int[] split = null)
    {
        // Same atlas texture (shared via SpriteSheetTextureCache) + same parts = same pixels: reuse the render targets
        // made for the previous song/scene instead of re-rendering (character atlases were ~90% of a song's load time).
        if (!SpriteSheetTextureCache.IsShared(atlas)) return PreRenderCompositeCore(graphicsDevice, atlas, tickParts, split);
        var key = CompositeKey(tickParts, split);
        if (SpriteSheetTextureCache.TryGetComposite(atlas, key, out var hit)) return hit;
        var made = PreRenderCompositeCore(graphicsDevice, atlas, tickParts, split);
        SpriteSheetTextureCache.AddComposite(atlas, key, made);
        return made;
    }
    private static (ulong, ulong) CompositeKey(List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>> tickParts, int[] split)
    {
        ulong h1 = 14695981039346656037UL, h2 = 0x9E3779B97F4A7C15UL;
        void Mix(int v) { unchecked { h1 = (h1 ^ (uint)v) * 1099511628211UL; h2 = (h2 ^ (uint)v) * 0xBF58476D1CE4E5B9UL; h2 ^= h2 >> 31; } }
        void MixF(float f) => Mix(BitConverter.SingleToInt32Bits(f));
        Mix(tickParts.Count);
        foreach (var parts in tickParts)
        {
            Mix(parts.Count);
            foreach (var (frame, a, b, c, d, tx, ty) in parts)
            {
                var r = frame.SourceRect; Mix(r.X); Mix(r.Y); Mix(r.Width); Mix(r.Height);
                Mix(frame.Rotated ? 1 : 0); MixF(frame.ElemAlpha); Mix(frame.ElemBlend);
                MixF(a); MixF(b); MixF(c); MixF(d); MixF(tx); MixF(ty);
            }
        }
        if (split != null) { Mix(split.Length); foreach (var v in split) Mix(v); } else Mix(-7);
        return (h1, h2);
    }
    /// <summary>Set around a Load(): symbol composites NOT in this set are registered as JIT composites (rendered on
    /// first play) instead of being pre-rendered. Used for the Freeplay DJ on UWP/Xbox (afk/tv/fist-pump anims).</summary>
    [ThreadStatic] public static HashSet<string> LazyCompositeKeep;
    private static readonly bool _compLog = Environment.GetEnvironmentVariable("FNF_COMPLOG") == "1";
    private static List<(Texture2D Tex, Rectangle Rect, Vector2 Origin)> PreRenderCompositeCore(
        GraphicsDevice graphicsDevice,
        Texture2D atlas,
        List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>> tickParts,
        int[] split)
    {
        if (TightComposites) return PreRenderCompositeTight(graphicsDevice, atlas, tickParts, split);

        var result = new List<(Texture2D, Rectangle, Vector2)>();
        // Compute a global bounding box across ALL ticks so every frame is
        // rendered with the same origin. This prevents frame-to-frame anchor
        // shifting that causes wobble in composite animations.
        // Must account for rotation/scale by transforming all 4 corners of each sprite.
        float gMinX = float.MaxValue, gMinY = float.MaxValue;
        float gMaxX = float.MinValue, gMaxY = float.MinValue;
        bool anyParts = false;
        foreach (var parts in tickParts)
        {
            foreach (var (frame, a, b, c, d, tx, ty) in parts)
            {
                anyParts = true;
                // For rotated atlas sprites, the stored W/H are swapped.
                // The M3D transform operates on unrotated dimensions.
                float w = frame.Rotated ? frame.SourceRect.Height : frame.SourceRect.Width;
                float h = frame.Rotated ? frame.SourceRect.Width : frame.SourceRect.Height;
                // Transform all 4 corners: (0,0), (w,0), (0,h), (w,h)
                float x0 = tx,             y0 = ty;
                float x1 = a * w + tx,     y1 = b * w + ty;
                float x2 = c * h + tx,     y2 = d * h + ty;
                float x3 = a * w + c * h + tx, y3 = b * w + d * h + ty;
                gMinX = Math.Min(gMinX, Math.Min(Math.Min(x0, x1), Math.Min(x2, x3)));
                gMinY = Math.Min(gMinY, Math.Min(Math.Min(y0, y1), Math.Min(y2, y3)));
                gMaxX = Math.Max(gMaxX, Math.Max(Math.Max(x0, x1), Math.Max(x2, x3)));
                gMaxY = Math.Max(gMaxY, Math.Max(Math.Max(y0, y1), Math.Max(y2, y3)));
            }
        }
        if (!anyParts) return result;
        int gWidth = Math.Clamp((int)MathF.Ceiling(gMaxX - gMinX), 1, 4096);
        int gHeight = Math.Clamp((int)MathF.Ceiling(gMaxY - gMinY), 1, 4096);
        // The animation's (0,0) registration point maps to this pixel in the texture.
        var origin = new Vector2(-gMinX, -gMinY);
        // Build dedup cache keys and identify unique frames
        var tickKeys = new string[tickParts.Count];
        var uniqueMap = new Dictionary<string, int>(); // key -> cell index
        var uniqueTickIdx = new List<int>(); // first tick index for each unique frame
        for (int i = 0; i < tickParts.Count; i++)
        {
            if (tickParts[i].Count == 0) continue;
            // Use the tick index as the key so every frame is preserved.
            // Deduplication compressed the original frame count, breaking frame-based sound triggers
            // (e.g., TV cartoon at frames 80/85) and making some animations look compressed.
            string key = i.ToString();
            tickKeys[i] = key;
            if (!uniqueMap.ContainsKey(key))
            {
                uniqueMap[key] = uniqueMap.Count;
                uniqueTickIdx.Add(i);
            }
        }
        int uniqueCount = uniqueMap.Count;
        if (uniqueCount == 0) return result;
        // Calculate atlas grid layout
        int cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(uniqueCount)));
        int rows = Math.Max(1, (int)Math.Ceiling((double)uniqueCount / cols));
        int atlasW = cols * gWidth;
        int atlasH = rows * gHeight;
        // Constrain to max texture size � maximize cell count within MAX_TEX � MAX_TEX
        const int MAX_TEX = 16384;
        if (atlasW > MAX_TEX || atlasH > MAX_TEX)
        {
            int maxCols = MAX_TEX / Math.Max(1, gWidth);
            int maxRows = MAX_TEX / Math.Max(1, gHeight);
            // Try fitting: constrain cols first, compute rows, then check height
            cols = Math.Min(cols, maxCols);
            rows = (int)Math.Ceiling((double)uniqueCount / Math.Max(1, cols));
            if (rows > maxRows)
            {
                // Need more cols to reduce rows � use as many cols as possible
                rows = maxRows;
                cols = (int)Math.Ceiling((double)uniqueCount / Math.Max(1, maxRows));
                cols = Math.Min(cols, maxCols);
            }
            atlasW = cols * gWidth;
            atlasH = rows * gHeight;
        }
        atlasW = Math.Min(atlasW, MAX_TEX);
        atlasH = Math.Min(atlasH, MAX_TEX);
        int actualCols = Math.Max(1, atlasW / Math.Max(1, gWidth));
        int maxCells = actualCols * (atlasH / Math.Max(1, gHeight));
        // Create ONE atlas RenderTarget2D for all unique frames
        if (_compLog)
        {
            long tight = 0; var seen = new HashSet<string>(); int dups = 0;
            for (int ti = 0; ti < tickParts.Count; ti++)
            {
                var tp = tickParts[ti]; if (tp.Count == 0) continue;
                var sbk = new System.Text.StringBuilder();
                float mnx = float.MaxValue, mny = float.MaxValue, mxx = float.MinValue, mxy = float.MinValue;
                int pe = split != null && ti < split.Length && split[ti] >= 0 ? Math.Min(split[ti], tp.Count) : tp.Count;
                for (int pi = 0; pi < pe; pi++)
                {
                    var (fr, a, b, c, d, tx, ty) = tp[pi];
                    sbk.Append(fr.SourceRect).Append(fr.ElemAlpha).Append(fr.ElemBlend).Append(a).Append(',').Append(b).Append(',').Append(c).Append(',').Append(d).Append(',').Append(tx).Append(',').Append(ty).Append(';');
                    float w = fr.Rotated ? fr.SourceRect.Height : fr.SourceRect.Width, h = fr.Rotated ? fr.SourceRect.Width : fr.SourceRect.Height;
                    float x1 = a * w + tx, y1 = b * w + ty, x2 = c * h + tx, y2 = d * h + ty, x3 = a * w + c * h + tx, y3 = b * w + d * h + ty;
                    mnx = Math.Min(mnx, Math.Min(Math.Min(tx, x1), Math.Min(x2, x3))); mny = Math.Min(mny, Math.Min(Math.Min(ty, y1), Math.Min(y2, y3)));
                    mxx = Math.Max(mxx, Math.Max(Math.Max(tx, x1), Math.Max(x2, x3))); mxy = Math.Max(mxy, Math.Max(Math.Max(ty, y1), Math.Max(y2, y3)));
                }
                if (!seen.Add(sbk.ToString())) { dups++; continue; }
                if (mnx < float.MaxValue) tight += (long)(Math.Ceiling(mxx - mnx) + 2) * (long)(Math.Ceiling(mxy - mny) + 2) * 4;
            }
            Console.WriteLine($"[COMPOSITE] {atlasW}x{atlasH} = {(long)atlasW * atlasH * 4 / 1048576}MB cells={uniqueCount} cell={gWidth}x{gHeight} ticks={tickParts.Count} dups={dups} tightUnique={tight / 1048576}MB");
        }
        bool hasMasks = tickParts.Any(tp => tp.Any(pp => pp.Frame.Mask != null));
        var rt = hasMasks ? new RenderTarget2D(graphicsDevice, atlasW, atlasH, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents)
                          : new RenderTarget2D(graphicsDevice, atlasW, atlasH);
        RenderTarget2D maskTmpA = null, maskTmpB = null;
        if (hasMasks)
        {
            maskTmpA = new RenderTarget2D(graphicsDevice, gWidth, gHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents); // re-bound after the masker pass
            maskTmpB = new RenderTarget2D(graphicsDevice, gWidth, gHeight);
        }
        graphicsDevice.SetRenderTarget(rt);
        graphicsDevice.Clear(Color.Transparent);
        // Use per-body-part Matrix transforms for full affine fidelity (including skew/shear).
        // SpriteBatch.Draw's rotation+scale decomposition loses skew, causing body parts to
        // separate by ~5-15px. Instead, we pass the full 2D affine matrix [a,b,c,d,tx,ty] as
        // SpriteBatch's transformMatrix, which correctly transforms all 4 sprite corners.
        // One Begin/End per body part is fine since PreRenderComposite runs once during loading.
        using var sb = new SpriteBatch(graphicsDevice);
        foreach (var ti in uniqueTickIdx)
        {
            int cellIdx = uniqueMap[tickKeys[ti]];
            if (cellIdx >= maxCells) continue;
            int col = cellIdx % actualCols;
            int row = cellIdx / actualCols;
            float cellX = col * gWidth;
            float cellY = row * gHeight;
            int partEnd = split != null && ti < split.Length && split[ti] >= 0 ? Math.Min(split[ti], tickParts[ti].Count) : tickParts[ti].Count;
            for (int pi = 0; pi < partEnd; pi++)
            {
                if (tickParts[ti][pi].Frame.Mask != null && maskTmpA != null)
                {
                    pi = DrawMaskedRun(graphicsDevice, sb, atlas, tickParts[ti], pi, partEnd, rt, maskTmpA, maskTmpB, gMinX, gMinY, cellX, cellY) - 1;
                    continue;
                }
                var (frame, a, b, c, d, tx, ty) = tickParts[ti][pi];
                float adjustedTx = tx - gMinX + cellX;
                float adjustedTy = ty - gMinY + cellY;
                // Build the full 2D affine transform as a Matrix.
                // For non-rotated sprites: pixel (px,py) ? (a*px + c*py + tx, b*px + d*py + ty)
                // For rotated atlas sprites (stored 90� CW): atlas pixel (ax,ay) maps to
                // original pixel (ay, sourceWidth-ax), so the matrix columns swap/negate.
                Matrix transformMatrix;
                if (frame.Rotated)
                {
                    float sw = frame.SourceRect.Width;
                    transformMatrix = new Matrix(
                        -c, -d, 0, 0,
                         a,  b, 0, 0,
                         0,  0, 1, 0,
                        c * sw + adjustedTx, d * sw + adjustedTy, 0, 1
                    );
                }
                else
                {
                    transformMatrix = new Matrix(
                        a, b, 0, 0,
                        c, d, 0, 0,
                        0, 0, 1, 0,
                        adjustedTx, adjustedTy, 0, 1
                    );
                }
                // CullNone is critical: mirrored parts (a<0, e.g. right-side speakers)
                // produce matrices with negative determinant, reversing triangle winding.
                // SymbolInstance.draw: alphaMultiplier <= 0 -> not drawn (bounds above still include it).
                if (frame.ElemAlpha <= 0f) continue;
                sb.Begin(SpriteSortMode.Deferred, CompositeBlendFor(frame.ElemBlend),
                    rasterizerState: RasterizerState.CullNone,
                    transformMatrix: transformMatrix);
                sb.Draw(atlas, Vector2.Zero, frame.SourceRect, new Color(1f, 1f, 1f, Math.Min(1f, frame.ElemAlpha)));
                sb.End();
            }
        }
        graphicsDevice.SetRenderTarget(null);
        maskTmpA?.Dispose(); maskTmpB?.Dispose();
        // Build result list � map each tick to its atlas cell
        for (int i = 0; i < tickParts.Count; i++)
        {
            if (tickParts[i].Count == 0)
            {
                if (result.Count > 0) result.Add(result[^1]);
                continue;
            }
            int cellIdx = uniqueMap[tickKeys[i]];
            if (cellIdx >= maxCells)
            {
                if (result.Count > 0) result.Add(result[^1]);
                continue;
            }
            int col = cellIdx % actualCols;
            int row = cellIdx / actualCols;
            result.Add((rt, new Rectangle(col * gWidth, row * gHeight, gWidth, gHeight), origin));
        }
        return result;
    }
    // ---- Tight composite packing (memory) ----------------------------------------------------------------------
    /// <summary>
    /// UWP/Xbox (or FNF_TIGHTCOMP=1): pre-rendered composite frames are cropped to each tick's own bounds (+1 px
    /// transparent margin), identical ticks share one cell, and cells are shelf-packed (1 px gaps) into pages of at
    /// most 8192x8192, instead of one global-bounds cell per tick. Every cell's top-left lies a whole number of pixels
    /// from the global cell's top-left, so the rendered pixels are identical; <see cref="CompositeCells"/> keeps the
    /// logical (global) origin/size for consumers that position by the untrimmed cell. Freeplay DJ composites:
    /// 952 MB -> ~430 MB.
    /// </summary>
#if XBOX_UWP
    public static bool TightComposites = Environment.GetEnvironmentVariable("FNF_TIGHTCOMP") != "0";
#else
    public static bool TightComposites = Environment.GetEnvironmentVariable("FNF_TIGHTCOMP") == "1";
#endif

    private static string TickKey(List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)> parts, int partEnd, float gMinX, float gMinY)
    {
        var sb = new System.Text.StringBuilder(partEnd * 48);
        for (int i = 0; i < partEnd; i++)
        {
            var (f, a, b, c, d, tx, ty) = parts[i];
            var r = f.SourceRect;
            sb.Append(r.X).Append(',').Append(r.Y).Append(',').Append(r.Width).Append(',').Append(r.Height).Append(f.Rotated ? 'r' : 'n')
              .Append(BitConverter.SingleToInt32Bits(f.ElemAlpha)).Append(',').Append(f.ElemBlend).Append(',')
              .Append(f.Texture == null ? 0 : f.Texture.GetHashCode()).Append(',')
              .Append(f.Mask == null ? 0 : f.Mask.GetHashCode()).Append(',')
              .Append(BitConverter.SingleToInt32Bits(a)).Append(',').Append(BitConverter.SingleToInt32Bits(b)).Append(',')
              .Append(BitConverter.SingleToInt32Bits(c)).Append(',').Append(BitConverter.SingleToInt32Bits(d)).Append(',')
              .Append(BitConverter.SingleToInt32Bits(tx - gMinX)).Append(',').Append(BitConverter.SingleToInt32Bits(ty - gMinY)).Append(';');
        }
        return sb.ToString();
    }

    private static List<(Texture2D Tex, Rectangle Rect, Vector2 Origin)> PreRenderCompositeTight(
        GraphicsDevice graphicsDevice, Texture2D atlas,
        List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>> tickParts, int[] split)
    {
        var result = new List<(Texture2D, Rectangle, Vector2)>();
        static (float, float, float, float) Ext(SpriteFrame frame, float a, float b, float c, float d, float tx, float ty)
        {
            float w = frame.Rotated ? frame.SourceRect.Height : frame.SourceRect.Width;
            float h = frame.Rotated ? frame.SourceRect.Width : frame.SourceRect.Height;
            float x1 = a * w + tx, y1 = b * w + ty, x2 = c * h + tx, y2 = d * h + ty, x3 = a * w + c * h + tx, y3 = b * w + d * h + ty;
            return (Math.Min(Math.Min(tx, x1), Math.Min(x2, x3)), Math.Min(Math.Min(ty, y1), Math.Min(y2, y3)),
                    Math.Max(Math.Max(tx, x1), Math.Max(x2, x3)), Math.Max(Math.Max(ty, y1), Math.Max(y2, y3)));
        }
        // global bounds exactly like PreRenderCompositeCore (all parts of all ticks)
        float gMinX = float.MaxValue, gMinY = float.MaxValue, gMaxX = float.MinValue, gMaxY = float.MinValue;
        bool any = false;
        foreach (var parts in tickParts)
            foreach (var (frame, a, b, c, d, tx, ty) in parts)
            {
                any = true; var (x0, y0, x1, y1) = Ext(frame, a, b, c, d, tx, ty);
                gMinX = Math.Min(gMinX, x0); gMinY = Math.Min(gMinY, y0); gMaxX = Math.Max(gMaxX, x1); gMaxY = Math.Max(gMaxY, y1);
            }
        if (!any) return result;
        int gWidth = Math.Clamp((int)MathF.Ceiling(gMaxX - gMinX), 1, 4096);
        int gHeight = Math.Clamp((int)MathF.Ceiling(gMaxY - gMinY), 1, 4096);
        var gOrigin = new Vector2(-gMinX, -gMinY);

        // unique cells (k = whole-pixel offset of the cell inside the global cell)
        var cellOfTick = new int[tickParts.Count];
        var cellKX = new List<int>(); var cellKY = new List<int>(); var cellW = new List<int>(); var cellH = new List<int>();
        var cellTick = new List<int>(); var cellEnd = new List<int>();
        var byKey = new Dictionary<string, int>();
        for (int t = 0; t < tickParts.Count; t++)
        {
            cellOfTick[t] = -1;
            var tp = tickParts[t];
            if (tp.Count == 0) continue;
            int partEnd = split != null && t < split.Length && split[t] >= 0 ? Math.Min(split[t], tp.Count) : tp.Count;
            string key = TickKey(tp, partEnd, gMinX, gMinY); // translation-invariant: same pixels wherever the anim sits
            if (byKey.TryGetValue(key, out int ci)) { cellOfTick[t] = ci; continue; }
            float mnx = float.MaxValue, mny = float.MaxValue, mxx = float.MinValue, mxy = float.MinValue;
            for (int pi = 0; pi < partEnd; pi++)
            {
                var (frame, a, b, c, d, tx, ty) = tp[pi];
                var (x0, y0, x1, y1) = Ext(frame, a, b, c, d, tx, ty);
                mnx = Math.Min(mnx, x0); mny = Math.Min(mny, y0); mxx = Math.Max(mxx, x1); mxy = Math.Max(mxy, y1);
            }
            int kx, ky, w, h;
            if (mnx == float.MaxValue) { kx = ky = 0; w = h = 1; }
            else
            {
                kx = Math.Clamp((int)MathF.Floor(mnx - gMinX) - 1, 0, gWidth - 1);
                ky = Math.Clamp((int)MathF.Floor(mny - gMinY) - 1, 0, gHeight - 1);
                w = Math.Clamp((int)MathF.Ceiling(mxx - gMinX) + 1 - kx, 1, gWidth - kx);
                h = Math.Clamp((int)MathF.Ceiling(mxy - gMinY) + 1 - ky, 1, gHeight - ky);
            }
            ci = cellKX.Count; byKey[key] = ci; cellOfTick[t] = ci;
            cellKX.Add(kx); cellKY.Add(ky); cellW.Add(w); cellH.Add(h); cellTick.Add(t); cellEnd.Add(partEnd);
        }
        int nCells = cellKX.Count;
        if (nCells == 0) return result;
        // cells already rendered by another animation of this atlas with the same content relative to its global cell
        // (label anim + symbol anim of the same frames at another instance position: identical pixels)
        var store = _cellStore.GetOrCreateValue(atlas);
        string gKey = $"{gWidth},{gHeight}|";
        var cellKeys = new string[nCells]; var reused = new (Texture2D Tex, Rectangle Rect)?[nCells];
        foreach (var kv in byKey) cellKeys[kv.Value] = gKey + kv.Key;
        int nReused = 0;
        lock (store)
            for (int i = 0; i < nCells; i++)
                if (store.TryGetValue(cellKeys[i], out var hit) && !hit.Tex.IsDisposed) { reused[i] = hit; nReused++; }

        // shelf packing, tallest first, 1 px gaps
        const int PAGE = 8192;
        long area = 0; int maxW = 1;
        for (int i = 0; i < nCells; i++) { area += (long)(cellW[i] + 1) * (cellH[i] + 1); maxW = Math.Max(maxW, cellW[i] + 1); }
        int pageW = Math.Min(PAGE, Math.Max(maxW, (int)Math.Ceiling(Math.Sqrt(area * 1.15))));
        var order = Enumerable.Range(0, nCells).OrderByDescending(i => cellH[i]).ThenBy(i => i).ToList();
        var cellPage = new int[nCells]; var cellX = new int[nCells]; var cellY = new int[nCells];
        var pageSizes = new List<(int W, int H)>();
        int page = 0, penX = 0, penY = 0, shelfH = 0, usedW = 1, usedH = 1;
        order.RemoveAll(i => reused[i] != null);
        foreach (int i in order)
        {
            int w = cellW[i], h = cellH[i];
            if (penX > 0 && penX + w > pageW) { penX = 0; penY += shelfH + 1; shelfH = 0; }
            if (penY > 0 && penY + h > PAGE)
            {
                pageSizes.Add((usedW, usedH)); page++; penX = penY = shelfH = 0; usedW = usedH = 1;
            }
            cellPage[i] = page; cellX[i] = penX; cellY[i] = penY;
            penX += w + 1; shelfH = Math.Max(shelfH, h);
            usedW = Math.Max(usedW, penX - 1); usedH = Math.Max(usedH, penY + h);
        }
        if (order.Count > 0) pageSizes.Add((usedW, usedH));

        bool hasMasks = tickParts.Any(tp => tp.Any(pp => pp.Frame.Mask != null));
        RenderTarget2D maskTmpA = null, maskTmpB = null;
        if (hasMasks)
        {
            maskTmpA = new RenderTarget2D(graphicsDevice, gWidth, gHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            maskTmpB = new RenderTarget2D(graphicsDevice, gWidth, gHeight);
        }
        var pages = new RenderTarget2D[pageSizes.Count];
        using (var sb = new SpriteBatch(graphicsDevice))
        {
            for (int p = 0; p < pages.Length; p++)
            {
                pages[p] = hasMasks
                    ? new RenderTarget2D(graphicsDevice, pageSizes[p].W, pageSizes[p].H, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents)
                    : new RenderTarget2D(graphicsDevice, pageSizes[p].W, pageSizes[p].H);
                graphicsDevice.SetRenderTarget(pages[p]);
                graphicsDevice.Clear(Color.Transparent);
                for (int i = 0; i < nCells; i++)
                {
                    if (reused[i] != null || cellPage[i] != p) continue;
                    var parts = tickParts[cellTick[i]];
                    int partEnd = cellEnd[i];
                    // same as the grid path with the cell placed at (cellX - kx, cellY - ky) in global-cell space
                    float baseX = cellX[i] - cellKX[i], baseY = cellY[i] - cellKY[i];
                    for (int pi = 0; pi < partEnd; pi++)
                    {
                        if (parts[pi].Frame.Mask != null && maskTmpA != null)
                        {
                            pi = DrawMaskedRunCell(graphicsDevice, sb, atlas, parts, pi, partEnd, pages[p], maskTmpA, maskTmpB, gMinX, gMinY,
                                new Rectangle(cellKX[i], cellKY[i], cellW[i], cellH[i]), cellX[i], cellY[i]) - 1;
                            continue;
                        }
                        var (frame, a, b, c, d, tx, ty) = parts[pi];
                        if (frame.ElemAlpha <= 0f) continue;
                        float adjustedTx = tx - gMinX + baseX, adjustedTy = ty - gMinY + baseY;
                        Matrix m = frame.Rotated
                            ? new Matrix(-c, -d, 0, 0, a, b, 0, 0, 0, 0, 1, 0, c * frame.SourceRect.Width + adjustedTx, d * frame.SourceRect.Width + adjustedTy, 0, 1)
                            : new Matrix(a, b, 0, 0, c, d, 0, 0, 0, 0, 1, 0, adjustedTx, adjustedTy, 0, 1);
                        // scissor keeps a part that sticks out of the global cell (4096 clamp) inside this cell, like the grid path's cell
                        sb.Begin(SpriteSortMode.Deferred, CompositeBlendFor(frame.ElemBlend), null, null, _cullNoneScissor, null, m);
                        graphicsDevice.ScissorRectangle = new Rectangle(cellX[i], cellY[i], cellW[i], cellH[i]);
                        sb.Draw(atlas, Vector2.Zero, frame.SourceRect, new Color(1f, 1f, 1f, Math.Min(1f, frame.ElemAlpha)));
                        sb.End();
                    }
                }
            }
        }
        graphicsDevice.SetRenderTarget(null);
        maskTmpA?.Dispose(); maskTmpB?.Dispose();
        for (int t = 0; t < tickParts.Count; t++)
        {
            int ci = cellOfTick[t];
            if (ci < 0) { if (result.Count > 0) result.Add(result[result.Count - 1]); continue; }
            Rectangle rect; Texture2D tex;
            if (reused[ci] is { } ru) { rect = ru.Rect; tex = ru.Tex; }
            else
            {
                rect = new Rectangle(cellX[ci], cellY[ci], cellW[ci], cellH[ci]); tex = pages[cellPage[ci]];
                lock (store) store[cellKeys[ci]] = (tex, rect);
            }
            var org = new Vector2(gOrigin.X - cellKX[ci], gOrigin.Y - cellKY[ci]);
            result.Add((tex, rect, org));
            CompositeCells.Set(tex, rect, org, gOrigin, gWidth, gHeight);
        }
        if (_compVerify) VerifyTight(graphicsDevice, atlas, tickParts, split, result);
        if (_compLog)
        {
            long bytes = 0; foreach (var ps in pageSizes) bytes += (long)ps.W * ps.H * 4;
            var t0 = tickParts.FirstOrDefault(tp => tp.Count > 0); string relk = "";
            if (t0 != null) { var sbr = new System.Text.StringBuilder(); foreach (var pp in t0) sbr.Append(pp.Frame.SourceRect).Append(pp.A).Append(pp.B).Append(pp.C).Append(pp.D).Append(pp.TX - t0[0].TX).Append(pp.TY - t0[0].TY); relk = $"rel={sbr.ToString().GetHashCode():X8} tx0={t0[0].TX:F3},{t0[0].TY:F3} gmin={gMinX:F3},{gMinY:F3} n0={t0.Count}"; }
            Console.WriteLine($"[COMPOSITE-TIGHT] ticks={tickParts.Count} cells={nCells} reused={nReused} {relk} pages={pages.Length} {string.Join("+", pageSizes.Select(ps => ps.W + "x" + ps.H))} = {bytes / 1048576}MB (grid would be {gWidth}x{gHeight} cells)");
        }
        return result;
    }
    private static readonly bool _compVerify = Environment.GetEnvironmentVariable("FNF_COMPVERIFY") == "1";
    public static int VerifyTickMismatches, VerifyTicks;
    /// <summary>Dev check (FNF_COMPVERIFY=1): render the old grid layout too and compare every tick pixel by pixel.</summary>
    private static void VerifyTight(GraphicsDevice gd, Texture2D atlas,
        List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>> tickParts, int[] split,
        List<(Texture2D Tex, Rectangle Rect, Vector2 Origin)> tight)
    {
        TightComposites = false;
        List<(Texture2D Tex, Rectangle Rect, Vector2 Origin)> grid;
        try { grid = PreRenderCompositeCore(gd, atlas, tickParts, split); } finally { TightComposites = true; }
        int bad = 0; long badPx = 0, n4 = 0; int maxD = 0;
        for (int t = 0; t < Math.Min(grid.Count, tight.Count); t++)
        {
            var g = grid[t]; var c = tight[t];
            var gp = new Color[g.Rect.Width * g.Rect.Height];
            g.Tex.GetData(0, g.Rect, gp, 0, gp.Length);
            var cp = new Color[c.Rect.Width * c.Rect.Height];
            c.Tex.GetData(0, c.Rect, cp, 0, cp.Length);
            int ox = (int)MathF.Round(g.Origin.X - c.Origin.X), oy = (int)MathF.Round(g.Origin.Y - c.Origin.Y);
            long n = 0;
            for (int y = 0; y < g.Rect.Height; y++)
                for (int x = 0; x < g.Rect.Width; x++)
                {
                    int tx = x - ox, ty = y - oy;
                    Color want = gp[y * g.Rect.Width + x];
                    Color got = tx >= 0 && ty >= 0 && tx < c.Rect.Width && ty < c.Rect.Height ? cp[ty * c.Rect.Width + tx] : Color.Transparent;
                    if (want != got) { n++; int dd = Math.Max(Math.Max(Math.Abs(want.R - got.R), Math.Abs(want.G - got.G)), Math.Max(Math.Abs(want.B - got.B), Math.Abs(want.A - got.A))); if (dd > maxD) maxD = dd; if (dd > 4) n4++; }
                }
            VerifyTicks++;
            if (n > 0) { bad++; badPx += n; VerifyTickMismatches++; }
        }
        Console.WriteLine($"[COMPVERIFY] ticks={grid.Count}/{tight.Count} mismatchTicks={bad} mismatchPx={badPx} over4={n4} maxDiff={maxD} totals {VerifyTickMismatches}/{VerifyTicks}");
        var seen = new HashSet<Texture2D>(); foreach (var g in grid) if (g.Tex != null && seen.Add(g.Tex)) g.Tex.Dispose();
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Texture2D, Dictionary<string, (Texture2D Tex, Rectangle Rect)>> _cellStore = new();
    private static readonly RasterizerState _cullNoneScissor = new RasterizerState { CullMode = CullMode.None, ScissorTestEnable = true };

    /// <summary>DrawMaskedRun for a tight cell: tmp targets stay global-cell sized; only the cell's region is composited.</summary>
    private static int DrawMaskedRunCell(GraphicsDevice gd, SpriteBatch sb, Texture2D atlas,
        List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)> parts, int start, int end,
        RenderTarget2D rt, RenderTarget2D tmpA, RenderTarget2D tmpB, float gMinX, float gMinY, Rectangle cellInGlobal, int cellX, int cellY)
    {
        // render the run exactly like the grid path (into tmpA/tmpB at global-cell coordinates) ...
        int stop = DrawMaskedRun(gd, sb, atlas, parts, start, end, rt, tmpA, tmpB, gMinX, gMinY, float.NaN, float.NaN);
        // ... then copy the cell's region of the masked result into the page
        gd.SetRenderTarget(rt);
        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        sb.Draw(tmpA, new Vector2(cellX, cellY), cellInGlobal, Color.White);
        sb.End();
        return stop;
    }
    private static Matrix PartMatrix(SpriteFrame frame, float a, float b, float c, float d, float tx, float ty)
        => frame.Rotated
            ? new Matrix(-c, -d, 0, 0, a, b, 0, 0, 0, 0, 1, 0, c * frame.SourceRect.Width + tx, d * frame.SourceRect.Width + ty, 0, 1)
            : new Matrix(a, b, 0, 0, c, d, 0, 0, 0, 0, 1, 0, tx, ty, 0, 1);

    // dst *= src.alpha (premultiplied RT): MaskShader 'color *= maskerColor.a'
    private static readonly BlendState _maskMultiply = new BlendState
    {
        ColorSourceBlend = Blend.Zero, ColorDestinationBlend = Blend.SourceAlpha,
        AlphaSourceBlend = Blend.Zero, AlphaDestinationBlend = Blend.SourceAlpha,
    };

    /// <summary>
    /// Draws the run of parts starting at <paramref name="start"/> that share one masker (same group and same world
    /// masker transform) into tmpA, multiplies tmpA by the masker rendered into tmpB, and composites tmpA into the cell
    /// of <paramref name="rt"/>. Returns the index after the run.
    /// </summary>
    private static int DrawMaskedRun(GraphicsDevice gd, SpriteBatch sb, Texture2D atlas,
        List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)> parts, int start, int end,
        RenderTarget2D rt, RenderTarget2D tmpA, RenderTarget2D tmpB, float gMinX, float gMinY, float cellX, float cellY)
    {
        var first = parts[start];
        var mask = first.Frame.Mask;
        // masker in world space via the first part: M_world_mask = M_part * Rel
        var world = new List<(SpriteFrame, float, float, float, float, float, float)>(mask.Rel.Count);
        foreach (var (mf, ra, rb, rc, rd, rtx, rty) in mask.Rel)
            world.Add((mf, first.A * ra + first.C * rb, first.B * ra + first.D * rb, first.A * rc + first.C * rd, first.B * rc + first.D * rd,
                       first.A * rtx + first.C * rty + first.TX, first.B * rtx + first.D * rty + first.TY));
        int stop = start + 1;
        while (stop < end && parts[stop].Frame.Mask != null && parts[stop].Frame.Mask.Group == mask.Group)
        {
            var p = parts[stop]; var r0 = p.Frame.Mask.Rel[0];
            float wtx = p.A * r0.TX + p.C * r0.TY + p.TX, wty = p.B * r0.TX + p.D * r0.TY + p.TY;
            if (MathF.Abs(wtx - world[0].Item6) > 0.05f || MathF.Abs(wty - world[0].Item7) > 0.05f) break; // another instance
            stop++;
        }
        // masked content
        gd.SetRenderTarget(tmpA);
        gd.Clear(Color.Transparent);
        for (int i = start; i < stop; i++)
        {
            var (f, a, b, c, d, tx, ty) = parts[i];
            if (f.ElemAlpha <= 0f) continue;
            sb.Begin(SpriteSortMode.Deferred, CompositeBlendFor(f.ElemBlend), rasterizerState: RasterizerState.CullNone,
                transformMatrix: PartMatrix(f, a, b, c, d, tx - gMinX, ty - gMinY));
            sb.Draw(f.Texture ?? atlas, Vector2.Zero, f.SourceRect, new Color(1f, 1f, 1f, Math.Min(1f, f.ElemAlpha)));
            sb.End();
        }
        // masker (drawn NORMAL, union of its parts)
        gd.SetRenderTarget(tmpB);
        gd.Clear(Color.Transparent);
        foreach (var (f, a, b, c, d, tx, ty) in world)
        {
            sb.Begin(SpriteSortMode.Deferred, _rtCompositeBlend, rasterizerState: RasterizerState.CullNone,
                transformMatrix: PartMatrix(f, a, b, c, d, tx - gMinX, ty - gMinY));
            sb.Draw(f.Texture ?? atlas, Vector2.Zero, f.SourceRect, Color.White);
            sb.End();
        }
        gd.SetRenderTarget(tmpA);
        sb.Begin(SpriteSortMode.Deferred, _maskMultiply, SamplerState.PointClamp);
        sb.Draw(tmpB, Vector2.Zero, Color.White);
        sb.End();
        // composite (premultiplied over) into the atlas cell (NaN: the tight-cell caller copies its region itself)
        if (float.IsNaN(cellX)) return stop;
        gd.SetRenderTarget(rt);
        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        sb.Draw(tmpA, new Vector2(cellX, cellY), Color.White);
        sb.End();
        return stop;
    }

    /// <summary>Renders tickParts[t][split[t]..] per tick into tightly cropped cells (shelf-packed). Each cell's
    /// top-left lies a whole number of pixels from the base composite's top-left (same sub-pixel phase), so
    /// base + above == the full composite (source-over is associative).</summary>
    private static List<(Texture2D Tex, Rectangle Rect, Vector2 Origin)> PreRenderAbove(
        GraphicsDevice graphicsDevice, Texture2D atlas,
        List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>> tickParts, int[] split)
    {
        var result = new List<(Texture2D Tex, Rectangle Rect, Vector2 Origin)>();
        static (float, float, float, float) Ext(SpriteFrame frame, float a, float b, float c, float d, float tx, float ty)
        {
            float w = frame.Rotated ? frame.SourceRect.Height : frame.SourceRect.Width;
            float h = frame.Rotated ? frame.SourceRect.Width : frame.SourceRect.Height;
            float x1 = a * w + tx, y1 = b * w + ty, x2 = c * h + tx, y2 = d * h + ty, x3 = a * w + c * h + tx, y3 = b * w + d * h + ty;
            return (Math.Min(Math.Min(tx, x1), Math.Min(x2, x3)), Math.Min(Math.Min(ty, y1), Math.Min(y2, y3)),
                    Math.Max(Math.Max(tx, x1), Math.Max(x2, x3)), Math.Max(Math.Max(ty, y1), Math.Max(y2, y3)));
        }
        float gMinX = float.MaxValue, gMinY = float.MaxValue;
        foreach (var parts in tickParts)
            foreach (var (frame, a, b, c, d, tx, ty) in parts)
            { var (x0, y0, _, _) = Ext(frame, a, b, c, d, tx, ty); gMinX = Math.Min(gMinX, x0); gMinY = Math.Min(gMinY, y0); }
        var cells = new (int X, int Y, int W, int H, float MinX, float MinY)[tickParts.Count];
        const int MAXW = 4096, MAXH = 16384;
        int penX = 0, penY = 0, shelfH = 0, usedW = 1, usedH = 1;
        for (int t = 0; t < tickParts.Count; t++)
        {
            cells[t] = (-1, -1, 0, 0, 0, 0);
            if (t >= split.Length || split[t] < 0 || split[t] >= tickParts[t].Count) continue;
            float mnx = float.MaxValue, mny = float.MaxValue, mxx = float.MinValue, mxy = float.MinValue;
            for (int pi = split[t]; pi < tickParts[t].Count; pi++)
            {
                var (frame, a, b, c, d, tx, ty) = tickParts[t][pi];
                var (x0, y0, x1, y1) = Ext(frame, a, b, c, d, tx, ty);
                mnx = Math.Min(mnx, x0); mny = Math.Min(mny, y0); mxx = Math.Max(mxx, x1); mxy = Math.Max(mxy, y1);
            }
            float cx = gMinX + MathF.Floor(mnx - gMinX), cy = gMinY + MathF.Floor(mny - gMinY);
            int w = Math.Clamp((int)MathF.Ceiling(mxx - cx) + 1, 1, MAXW), h = Math.Clamp((int)MathF.Ceiling(mxy - cy) + 1, 1, MAXH);
            if (penX + w > MAXW) { penX = 0; penY += shelfH; shelfH = 0; }
            if (penY + h > MAXH) continue;   // out of space: parts stay hidden for this tick (never seen in practice)
            cells[t] = (penX, penY, w, h, cx, cy);
            penX += w; shelfH = Math.Max(shelfH, h); usedW = Math.Max(usedW, penX); usedH = Math.Max(usedH, penY + h);
        }
        RenderTarget2D rt = null;
        if (cells.Any(cl => cl.X >= 0))
        {
            rt = new RenderTarget2D(graphicsDevice, usedW, usedH);
            graphicsDevice.SetRenderTarget(rt);
            graphicsDevice.Clear(Color.Transparent);
            using var sb = new SpriteBatch(graphicsDevice);
            for (int t = 0; t < tickParts.Count; t++)
            {
                var cl = cells[t];
                if (cl.X < 0) continue;
                for (int pi = split[t]; pi < tickParts[t].Count; pi++)
                {
                    var (frame, a, b, c, d, tx, ty) = tickParts[t][pi];
                    if (frame.ElemAlpha <= 0f) continue;
                    float atx = tx - cl.MinX + cl.X, aty = ty - cl.MinY + cl.Y;
                    Matrix m = frame.Rotated
                        ? new Matrix(-c, -d, 0, 0, a, b, 0, 0, 0, 0, 1, 0, c * frame.SourceRect.Width + atx, d * frame.SourceRect.Width + aty, 0, 1)
                        : new Matrix(a, b, 0, 0, c, d, 0, 0, 0, 0, 1, 0, atx, aty, 0, 1);
                    sb.Begin(SpriteSortMode.Deferred, CompositeBlendFor(frame.ElemBlend), rasterizerState: RasterizerState.CullNone, transformMatrix: m);
                    sb.Draw(atlas, Vector2.Zero, frame.SourceRect, new Color(1f, 1f, 1f, Math.Min(1f, frame.ElemAlpha)));
                    sb.End();
                }
            }
            graphicsDevice.SetRenderTarget(null);
        }
        for (int t = 0; t < tickParts.Count; t++)
        {
            var cl = cells[t];
            result.Add(cl.X < 0 || rt == null ? (null, Rectangle.Empty, Vector2.Zero) : (rt, new Rectangle(cl.X, cl.Y, cl.W, cl.H), new Vector2(-cl.MinX, -cl.MinY)));
        }
        return result;
    }
    /// <summary>
    /// Finalize deferred composite rendering on the main thread.
    /// Called after a background-thread Load(deferComposites: true) completes.
    /// Performs GPU work (RenderTarget2D, SpriteBatch) that requires the main thread.
    /// </summary>
    public void FinalizeComposites(GraphicsDevice graphicsDevice)
    {
        if (PendingComposites.Count == 0) return;
        int count = PendingComposites.Count;
        foreach (var (name, tickParts) in PendingComposites)
        {
            var compositeFrames = PreRenderComposite(graphicsDevice, Texture, tickParts);
            if (compositeFrames.Count > 0)
            {
                CompositeAnimations[name] = compositeFrames;
                CompositeLogicalSizes[name] = AnimatedSprite.ComputeRawBoundingBox(tickParts[0]);
                TrySetCompositeBase(tickParts);
                if (!CompositeBaseOrigin.HasValue)
                    CompositeBaseOrigin = CompositeCells.LogicalOrigin(compositeFrames[0]);
                var simpleFrames = new List<SpriteFrame>();
                for (int i = 0; i < compositeFrames.Count; i++)
                {
                    simpleFrames.Add(new SpriteFrame
                    {
                        Name = $"{name}_{i}",
                        SourceRect = compositeFrames[i].Rect,
                        Offset = Vector2.Zero,
                        FrameWidth = CompositeCells.LogicalSize(compositeFrames[i]).W,
                        FrameHeight = CompositeCells.LogicalSize(compositeFrames[i]).H
                    });
                }
                Animations[name] = simpleFrames;
            }
        }
        PendingComposites.Clear();
        Console.WriteLine($"FinalizeComposites: rendered {count} deferred composites");
    }
    /// <summary>
    /// Convert RawCompositeData to pre-rendered CompositeAnimations on the main thread.
    /// Called after background-thread loading completes (deferComposites: true).
    /// PreRenderComposite uses full affine Matrix transforms for pixel-perfect body part
    /// assembly � no skew/shear loss from rotation+scale decomposition.
    /// </summary>
    public void RenderRawComposites(GraphicsDevice graphicsDevice)
    {
        if (RawCompositeData.Count == 0 || Texture == null) return;
        // Save/restore render target (PreRenderComposite creates its own RT)
        var prevTargets = graphicsDevice.GetRenderTargets();
        var prevRT = prevTargets.Length > 0 ? prevTargets[0].RenderTarget as RenderTarget2D : null;
        int count = 0;
        foreach (var kvp in RawCompositeData)
        {
            string animName = kvp.Key;
            var tickParts = kvp.Value;
            var compositeFrames = PreRenderComposite(graphicsDevice, Texture, tickParts);
            if (compositeFrames.Count > 0)
            {
                CompositeAnimations[animName] = compositeFrames;
                CompositeLogicalSizes[animName] = AnimatedSprite.ComputeRawBoundingBox(tickParts[0]);
                TrySetCompositeBase(tickParts);
                if (!CompositeBaseOrigin.HasValue)
                    CompositeBaseOrigin = CompositeCells.LogicalOrigin(compositeFrames[0]);
                var simpleFrames = new List<SpriteFrame>();
                for (int i = 0; i < compositeFrames.Count; i++)
                {
                    simpleFrames.Add(new SpriteFrame
                    {
                        Name = $"{animName}_{i}",
                        SourceRect = compositeFrames[i].Rect,
                        Offset = Vector2.Zero,
                        FrameWidth = CompositeCells.LogicalSize(compositeFrames[i]).W,
                        FrameHeight = CompositeCells.LogicalSize(compositeFrames[i]).H
                    });
                }
                Animations[animName] = simpleFrames;
                count++;
            }
        }
        RawCompositeData.Clear();
        graphicsDevice.SetRenderTarget(prevRT);
    }
    /// <summary>
    /// Render a JIT (Just-In-Time) composite animation on demand.
    /// Called on the main thread before drawing when the animation hasn't been rendered yet.
    /// Calls PreRenderComposite for this single animation and caches the result.
    /// Saves and restores the current render target since this may be called
    /// while FNFGame.Draw has the scene render target active.
    /// </summary>
    public void RenderJITAnimation(GraphicsDevice graphicsDevice, string animName)
    {
        if (!JITComposites.TryGetValue(animName, out var jit) || jit.Rendered) return;
        var __fl = FrameLog.Begin();
        // Save current render target � PreRenderComposite will switch to its own RT
        // and then set to null. We need to restore the caller's RT (e.g. FNFGame._renderTarget).
        var prevTargets = graphicsDevice.GetRenderTargets();
        var prevRT = prevTargets.Length > 0 ? prevTargets[0].RenderTarget as RenderTarget2D : null;
        var compositeFrames = PreRenderComposite(graphicsDevice, Texture, jit.TickParts);
        // Restore previous render target
        graphicsDevice.SetRenderTarget(prevRT);
        if (compositeFrames.Count > 0)
        {
            CompositeAnimations[animName] = compositeFrames;
            CompositeLogicalSizes[animName] = AnimatedSprite.ComputeRawBoundingBox(jit.TickParts[0]);
            TrySetCompositeBase(jit.TickParts);
            if (!CompositeBaseOrigin.HasValue)
                CompositeBaseOrigin = CompositeCells.LogicalOrigin(compositeFrames[0]);
            var simpleFrames = new List<SpriteFrame>();
            for (int i = 0; i < compositeFrames.Count; i++)
            {
                simpleFrames.Add(new SpriteFrame
                {
                    Name = $"{animName}_{i}",
                    SourceRect = compositeFrames[i].Rect,
                    Offset = Vector2.Zero,
                    FrameWidth = CompositeCells.LogicalSize(compositeFrames[i]).W,
                    FrameHeight = CompositeCells.LogicalSize(compositeFrames[i]).H
                });
            }
            Animations[animName] = simpleFrames;
        }
        jit.Rendered = true;
        Console.WriteLine($"JIT rendered composite: {animName} ({compositeFrames.Count} frames)");
        if (FrameLog.Enabled) FrameLog.Event("jit " + animName, FrameLog.Since(__fl));
    }
    /// <summary>
    /// Simple fallback: recursively collect all unique atlas sprites from a symbol.
    /// </summary>
    private static HashSet<SpriteFrame> CollectAllSprites(
        JToken symbol,
        Dictionary<string, JToken> allSymbols,
        Dictionary<string, SpriteFrame> framesByName,
        HashSet<string> visited)
    {
        var result = new HashSet<SpriteFrame>();
        string symName = (symbol["SN"] ?? symbol["SYMBOL_name"])?.ToString() ?? "";
        if (!visited.Add(symName)) return result;
        var layers = symbol["TL"]?["L"] ?? symbol["TIMELINE"]?["LAYERS"];
        if (layers != null)
        {
            foreach (var layer in layers)
            {
                var layerFrames = layer["FR"] ?? layer["Frames"];
                if (layerFrames == null) continue;
                foreach (var frameData in layerFrames)
                {
                    var elements = frameData["E"] ?? frameData["elements"];
                    if (elements == null) continue;
                    foreach (var element in elements)
                    {
                        var asi = element["ASI"] ?? element["ATLAS_SPRITE_instance"];
                        if (asi != null)
                        {
                            string frameName = (asi["N"] ?? asi["name"])?.ToString() ?? "";
                            if (framesByName.TryGetValue(frameName, out var frame))
                                result.Add(frame);
                        }
                        var si = element["SI"] ?? element["SYMBOL_instance"] ?? element["SYMBOL_Instance"];
                        if (si != null)
                        {
                            string nestedName = (si["SN"] ?? si["SYMBOL_name"])?.ToString() ?? "";
                            if (!string.IsNullOrEmpty(nestedName) && allSymbols.TryGetValue(nestedName, out var nestedSym))
                                foreach (var ns in CollectAllSprites(nestedSym, allSymbols, framesByName, visited))
                                    result.Add(ns);
                        }
                    }
                }
            }
        }
        visited.Remove(symName);
        return result;
    }
    /// <summary>
    /// Simple fallback: for each tick, pick the largest atlas sprite across all layers.
    /// Used when pre-render compositing is disabled.
    /// </summary>
    private static void CollectLargestPerTick(
        JToken symbol,
        Dictionary<string, JToken> allSymbols,
        Dictionary<string, SpriteFrame> framesByName,
        List<SpriteFrame> result,
        HashSet<string> visited)
    {
        string symName = (symbol["SN"] ?? symbol["SYMBOL_name"])?.ToString() ?? "";
        if (!visited.Add(symName)) return;
        var layers = symbol["TL"]?["L"] ?? symbol["TIMELINE"]?["LAYERS"];
        if (layers == null) { visited.Remove(symName); return; }
        int totalDuration = 0;
        foreach (var layer in layers)
        {
            var layerFrames = layer["FR"] ?? layer["Frames"];
            if (layerFrames == null) continue;
            foreach (var frameData in layerFrames)
            {
                int index = ReadFrameIndex(frameData);
                int dur = ReadFrameDuration(frameData);
                totalDuration = Math.Max(totalDuration, index + dur);
            }
        }
        if (totalDuration == 0) { visited.Remove(symName); return; }
        var tickFrames = new SpriteFrame[totalDuration];
        var tickAreas = new int[totalDuration];
        foreach (var layer in layers)
        {
            var layerFrames = layer["FR"] ?? layer["Frames"];
            if (layerFrames == null) continue;
            foreach (var frameData in layerFrames)
            {
                int index = ReadFrameIndex(frameData);
                int duration = ReadFrameDuration(frameData);
                var elements = frameData["E"] ?? frameData["elements"];
                if (elements == null) continue;
                SpriteFrame bestSprite = null;
                int bestArea = 0;
                foreach (var element in elements)
                {
                    var asi = element["ASI"] ?? element["ATLAS_SPRITE_instance"];
                    if (asi != null)
                    {
                        string frameName = (asi["N"] ?? asi["name"])?.ToString() ?? "";
                        if (framesByName.TryGetValue(frameName, out var frame))
                        {
                            int area = frame.SourceRect.Width * frame.SourceRect.Height;
                            if (area > bestArea) { bestArea = area; bestSprite = frame; }
                        }
                    }
                    var si = element["SI"] ?? element["SYMBOL_instance"] ?? element["SYMBOL_Instance"];
                    if (si != null)
                    {
                        string nestedName = (si["SN"] ?? si["SYMBOL_name"])?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(nestedName) && allSymbols.TryGetValue(nestedName, out var nestedSym))
                        {
                            foreach (var ns in CollectAllSprites(nestedSym, allSymbols, framesByName, visited))
                            {
                                int area = ns.SourceRect.Width * ns.SourceRect.Height;
                                if (area > bestArea) { bestArea = area; bestSprite = ns; }
                            }
                        }
                    }
                }
                if (bestSprite != null)
                {
                    for (int t = index; t < index + duration && t < totalDuration; t++)
                    {
                        if (bestArea > tickAreas[t])
                        {
                            tickFrames[t] = bestSprite;
                            tickAreas[t] = bestArea;
                        }
                    }
                }
            }
        }
        for (int t = 0; t < totalDuration; t++)
        {
            if (tickFrames[t] != null)
                result.Add(tickFrames[t]);
        }
        visited.Remove(symName);
    }
    private static void LoadFromJson(SpriteSheet sheet, string jsonPath)
    {
        try
        {
            string json = global::FNF_MonoGame.Engine.FsCI.ReadAllText(jsonPath);
            var data = JObject.Parse(json);
            var sprites = data["ATLAS"]?["SPRITES"];
            if (sprites != null)
            {
                foreach (var spriteToken in sprites)
                {
                    var sprite = spriteToken["SPRITE"];
                    if (sprite == null) continue;
                    string name = sprite["name"]?.ToString() ?? "";
                    int x = sprite["x"]?.Value<int>() ?? 0;
                    int y = sprite["y"]?.Value<int>() ?? 0;
                    int w = sprite["w"]?.Value<int>() ?? 0;
                    int h = sprite["h"]?.Value<int>() ?? 0;
                    var frame = new SpriteFrame
                    {
                        Name = name,
                        SourceRect = new Rectangle(x, y, w, h),
                        Offset = Vector2.Zero
                    };
                    sheet.Frames[name] = frame;
                    string animName = GetAnimationName(name);
                    if (!sheet.Animations.ContainsKey(animName))
                    {
                        sheet.Animations[animName] = new List<SpriteFrame>();
                    }
                    sheet.Animations[animName].Add(frame);
                }
            }
            // Sort frames within each animation by frame number (matches HaxeFlixel addByPrefix)
            SortAnimationFrames(sheet.Animations);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading JSON spritesheet: {ex.Message}");
        }
    }
    private static void LoadFromXml(SpriteSheet sheet, string xmlPath)
    {
        XDocument xml;
        try { xml = XDocument.Load(xmlPath); }
        catch (System.Xml.XmlException)
        {
            // haxe Xml accepts raw '&' / '<' in attribute values (Psych alphabet.xml "& bold", "< normal"): escape names
            string raw = global::FNF_MonoGame.Engine.FsCI.ReadAllText(xmlPath);
            // haxe Xml also ignores text after the root element (Playable KB qtChill.xml ends with "</TextureAtlas>>")
            int rootEnd = raw.LastIndexOf("</TextureAtlas>", StringComparison.Ordinal);
            if (rootEnd >= 0) raw = raw.Substring(0, rootEnd + 15);
            raw = System.Text.RegularExpressions.Regex.Replace(raw, "name=\"([^\"]*)\"", m => "name=\"" + System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;") + "\"");
            xml = XDocument.Parse(raw);
        }
        var root = xml.Root;
        foreach (var subTexture in root.Elements("SubTexture"))
        {
            string name = subTexture.Attribute("name")?.Value ?? "";
            // Strip .png/.xml extensions from frame names (common in Sparrow XML sheets)
            if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                name = name[..^4];
            else if (name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                name = name[..^4];
            int x = int.Parse(subTexture.Attribute("x")?.Value ?? "0");
            int y = int.Parse(subTexture.Attribute("y")?.Value ?? "0");
            int width = int.Parse(subTexture.Attribute("width")?.Value ?? "0");
            int height = int.Parse(subTexture.Attribute("height")?.Value ?? "0");
            // Frame offset and original size (for trimmed sprites)
            int frameX = int.Parse(subTexture.Attribute("frameX")?.Value ?? "0");
            int frameY = int.Parse(subTexture.Attribute("frameY")?.Value ?? "0");
            int frameWidth = int.Parse(subTexture.Attribute("frameWidth")?.Value ?? "0");
            int frameHeight = int.Parse(subTexture.Attribute("frameHeight")?.Value ?? "0");
            var frame = new SpriteFrame
            {
                Name = name,
                SourceRect = new Rectangle(x, y, width, height),
                Offset = new Vector2(-frameX, -frameY),
                Rotated = subTexture.Attribute("rotated")?.Value == "true",
                FrameWidth = frameWidth,
                FrameHeight = frameHeight
            };
            sheet.FirstFrame ??= frame; // flixel: frames setter shows frames[0] -> width/height before any updateHitbox
            sheet.Frames[name] = frame;
            // Group frames by animation name (everything before the frame number)
            string animName = GetAnimationName(name);
            if (!sheet.Animations.ContainsKey(animName))
            {
                sheet.Animations[animName] = new List<SpriteFrame>();
            }
            sheet.Animations[animName].Add(frame);
        }
        // Sort frames within each animation by frame number (matches HaxeFlixel addByPrefix)
        SortAnimationFrames(sheet.Animations);
        Console.WriteLine($"Loaded spritesheet from XML: {sheet.Frames.Count} frames, {sheet.Animations.Count} animations");
        if (sheet.Animations.Count <= 10)
        {
            foreach (var anim in sheet.Animations.Keys)
                Console.WriteLine($"  Animation: '{anim}' ({sheet.Animations[anim].Count} frames)");
        }
    }
    /// <summary>
    /// Load from Packer .txt format: "name = x y w h" per line.
    /// Used by some pixel/retro characters (e.g., spirit).
    /// </summary>
    private static void LoadFromPacker(SpriteSheet sheet, string txtPath)
    {
        var lines = global::FNF_MonoGame.Engine.FsCI.ReadAllLines(txtPath);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            // Format: "name = x y w h" 
            int eqIdx = line.IndexOf('=');
            if (eqIdx < 0) continue;
            string name = line[..eqIdx].Trim();
            string[] parts = line[(eqIdx + 1)..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) continue;
            if (!int.TryParse(parts[0], out int x) || !int.TryParse(parts[1], out int y) ||
                !int.TryParse(parts[2], out int w) || !int.TryParse(parts[3], out int h))
                continue;
            var frame = new SpriteFrame
            {
                Name = name,
                SourceRect = new Rectangle(x, y, w, h),
                Offset = Vector2.Zero
            };
            sheet.Frames[name] = frame;
            // Packer names use underscore separator: "idle spirit_0" -> "idle spirit"
            string animName = name;
            int lastUnderscore = animName.LastIndexOf('_');
            if (lastUnderscore > 0 && lastUnderscore < animName.Length - 1)
            {
                string suffix = animName[(lastUnderscore + 1)..];
                if (int.TryParse(suffix, out _))
                    animName = animName[..lastUnderscore];
            }
            if (!sheet.Animations.ContainsKey(animName))
                sheet.Animations[animName] = new List<SpriteFrame>();
            sheet.Animations[animName].Add(frame);
        }
        // Sort frames within each animation by frame number (matches HaxeFlixel addByPrefix)
        SortAnimationFrames(sheet.Animations);
        Console.WriteLine($"Loaded spritesheet from Packer: {sheet.Frames.Count} frames, {sheet.Animations.Count} animations");
    }
    /// <summary>
    /// Extract animation name from frame name (e.g., "idle0001" -> "idle")
    /// </summary>
    private static string GetAnimationName(string frameName)
    {
        // Remove trailing digits
        int i = frameName.Length - 1;
        while (i >= 0 && char.IsDigit(frameName[i]))
        {
            i--;
        }
        return i >= 0 ? frameName.Substring(0, i + 1) : frameName;
    }
    /// <summary>
    /// Extract the trailing frame number from a frame name (e.g., "idle0003" ? 3).
    /// Used to sort frames within an animation to match HaxeFlixel's addByPrefix behavior.
    /// </summary>
    private static int ExtractFrameNumber(string frameName)
    {
        int i = frameName.Length - 1;
        while (i >= 0 && char.IsDigit(frameName[i])) i--;
        if (i < frameName.Length - 1 && int.TryParse(frameName[(i + 1)..], out int num))
            return num;
        return 0;
    }
    /// <summary>
    /// Sort frames within each animation by their trailing frame number.
    /// Matches HaxeFlixel's addByPrefix behavior which sorts frames by name.
    /// </summary>
    private static void SortAnimationFrames(Dictionary<string, List<SpriteFrame>> animations)
    {
        foreach (var anim in animations.Values)
        {
            anim.Sort((a, b) => ExtractFrameNumber(a.Name).CompareTo(ExtractFrameNumber(b.Name)));
        }
    }
    // Cache for fuzzy animation lookups to avoid repeated linear scans
    private readonly Dictionary<string, List<SpriteFrame>> _fuzzyCache = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Get a specific animation by name (exact, case-insensitive, or partial match)
    /// Returns null if no match found
    /// </summary>
    /// <summary>
    /// Flixel animation.addByPrefix semantics: every frame whose name is the prefix followed only by its
    /// frame number, in frame order. Registers the result under the prefix. Null if no frame matches.
    /// </summary>
    public List<SpriteFrame> BuildAnimationFromPrefix(string prefix)
    {
        if (string.IsNullOrEmpty(prefix) || Frames.Count == 0) return null;
        var list = Frames.Where(kv => kv.Key.Length > prefix.Length && kv.Key.StartsWith(prefix, StringComparison.Ordinal)
                                       && kv.Key.Substring(prefix.Length).All(char.IsDigit))
                         .OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value).ToList();
        if (list.Count == 0) return null;
        Animations[prefix] = list;
        return list;
    }

    public List<SpriteFrame> GetAnimation(string name)
    {
        // Try exact match first
        if (Animations.TryGetValue(name, out var frames))
            return frames;
        // Check fuzzy cache (covers previously resolved case-insensitive lookups)
        if (_fuzzyCache.TryGetValue(name, out var cached))
            return cached;
        // Try case-insensitive match
        foreach (var kvp in Animations)
        {
            if (kvp.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                _fuzzyCache[name] = kvp.Value; // Cache to avoid future linear scans
                return kvp.Value;
            }
        }
        return null;
    }
    /// <summary>
    /// Get animation with fuzzy fallback � always returns something if any animation exists
    /// Used by AnimatedSprite when a guaranteed result is desired
    /// </summary>
    public List<SpriteFrame> GetAnimationFuzzy(string name)
    {
        // Check fuzzy cache first (avoids repeated linear scans)
        if (_fuzzyCache.TryGetValue(name, out var cached))
            return cached;
        var result = GetAnimation(name);
        if (result != null) { _fuzzyCache[name] = result; return result; }
        // Try partial match (animation name contains the search term)
        foreach (var kvp in Animations)
        {
            if (kvp.Key.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                _fuzzyCache[name] = kvp.Value;
                return kvp.Value;
            }
        }
        // Fallback to "default" animation if exists
        if (name != "default" && Animations.TryGetValue("default", out var defaultFrames))
        {
            _fuzzyCache[name] = defaultFrames;
            return defaultFrames;
        }
        // Fallback to first available animation
        if (Animations.Count > 0)
        {
            var first = Animations.Values.First();
            _fuzzyCache[name] = first;
            return first;
        }
        return null;
    }
    /// <summary>
    /// Get a frame by exact name
    /// </summary>
    public SpriteFrame GetFrame(string name)
    {
        return Frames.TryGetValue(name, out var frame) ? frame : null;
    }
    /// <summary>
    /// Dispose all GPU resources (atlas texture + composite textures).
    /// Safe to call multiple times.
    /// </summary>
    /// <summary>Low-memory game over: free the pre-rendered composite render targets (the sprite is no longer drawn).
    /// Shared cache composites become disposed and are re-rendered by the next load that needs them.</summary>
    public long DisposeCompositeTextures()
    {
        long bytes = 0; var seen = new HashSet<Texture2D>();
        foreach (var compList in CompositeAnimations.Values.Concat(CompositeAbove.Values))
            foreach (var (tex, _, _) in compList)
                if (tex != null && !tex.IsDisposed && seen.Add(tex)) { bytes += (long)tex.Width * tex.Height * 4; tex.Dispose(); }
        CompositeAnimations.Clear(); CompositeAbove.Clear();
        return bytes;
    }
    public void Dispose()
    {
        _fuzzyCache.Clear();
        if (Texture != null && !Texture.IsDisposed && !SpriteSheetTextureCache.Release(Texture))
            Texture.Dispose();
        Texture = null;
        var disposed = new HashSet<Texture2D>();
        foreach (var compList in CompositeAnimations.Values.Concat(CompositeAbove.Values))
        {
            foreach (var (tex, _, _) in compList)
            {
                if (tex != null && !tex.IsDisposed && disposed.Add(tex) && !SpriteSheetTextureCache.OwnsComposite(tex))
                    tex.Dispose();
            }
        }
        CompositeAnimations.Clear(); CompositeAbove.Clear();
        CompositeLogicalSizes.Clear();
        CompositeBaseOrigin = null;
        RawCompositeData.Clear();
        JITComposites.Clear();
        Animations.Clear();
        Frames.Clear();
    }
}
/// <summary>
/// Stores JIT composite data for an animation: tick parts and precomputed origin.
/// GPU rendering is deferred until the animation is first drawn.
/// </summary>
public class JITCompositeInfo
{
    public List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>> TickParts;
    public Vector2 Origin;
    public int Width;
    public int Height;
    public bool Rendered;
    public JITCompositeInfo(List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>> tickParts)
    {
        TickParts = tickParts;
        // Precompute global bounding box origin (same logic as PreRenderComposite)
        // so GetCompositeOrigin can return correct values before GPU rendering.
        float gMinX = float.MaxValue, gMinY = float.MaxValue;
        float gMaxX = float.MinValue, gMaxY = float.MinValue;
        foreach (var parts in tickParts)
        {
            foreach (var (frame, a, b, c, d, tx, ty) in parts)
            {
                float w = frame.Rotated ? frame.SourceRect.Height : frame.SourceRect.Width;
                float h = frame.Rotated ? frame.SourceRect.Width : frame.SourceRect.Height;
                float x0 = tx, y0 = ty;
                float x1 = a * w + tx, y1 = b * w + ty;
                float x2 = c * h + tx, y2 = d * h + ty;
                float x3 = a * w + c * h + tx, y3 = b * w + d * h + ty;
                gMinX = Math.Min(gMinX, Math.Min(Math.Min(x0, x1), Math.Min(x2, x3)));
                gMinY = Math.Min(gMinY, Math.Min(Math.Min(y0, y1), Math.Min(y2, y3)));
                gMaxX = Math.Max(gMaxX, Math.Max(Math.Max(x0, x1), Math.Max(x2, x3)));
                gMaxY = Math.Max(gMaxY, Math.Max(Math.Max(y0, y1), Math.Max(y2, y3)));
            }
        }
        if (gMinX < float.MaxValue)
        {
            Origin = new Vector2(-gMinX, -gMinY);
            Width = Math.Clamp((int)MathF.Ceiling(gMaxX - gMinX), 1, 4096);
            Height = Math.Clamp((int)MathF.Ceiling(gMaxY - gMinY), 1, 4096);
        }
        else
        {
            Origin = Vector2.Zero;
            Width = 1;
            Height = 1;
        }
    }
}
/// <summary>
/// Animated sprite that uses a SpriteSheet
/// </summary>
public class AnimatedSprite
{
    public SpriteSheet Sheet { get; set; }
    public Vector2 Position { get; set; }
    public Vector2 Scale { get; set; } = Vector2.One;
    /// <summary>
    /// The frame size (width/height) that the character's Position was originally calculated with.
    /// Drawing shifts each frame by the difference so the feet stay anchored at the stage position.
    /// </summary>
    public (int Width, int Height) PositioningFrameSize { get; set; }
    public float Rotation { get; set; }
    public Color Tint { get; set; } = Color.White;
    public SpriteEffects Effects { get; set; } = SpriteEffects.None;
    private string _currentAnim = "";
    public string ResolvedAnimName => _resolvedAnimName;
    /// <summary>
    /// Characters: draw Animate-atlas composites exactly like flixel-animate (one fixed transform per
    /// timeline, no per-frame re-centering) and report timeline bounds as the frame size.
    /// </summary>
    public bool UseTimelineAnchoring { get; set; }
    /// <summary>For animations split at a FlxSpriteElement (SpriteSheet.AnchorSplit): 0 = draw the parts below and
    /// above the element, 1 = only below, 2 = only above (the caller draws the element in between).</summary>
    public int CompositeLayer { get; set; }
    /// <summary>Rotate about the untrimmed frame centre (FlxSprite default origin) instead of the top-left.</summary>
    public bool RotateAroundCenter { get; set; }
    /// <summary>
    /// Per-frame shader (original DropShadowShader rim light). The caller begins the batch in Immediate mode with
    /// this effect; the sprite sets the frame-dependent uniforms (updateFrameInfo) right before each Draw.
    /// </summary>
    public Effect FrameEffect { get; set; }
    public Texture2D FrameEffectMask { get; set; }

    private void SetFrameFxCommon(Texture2D tex)
    {
        var p = FrameEffect.Parameters;
        if (FrameEffectMask != null) { p["AltMask"]?.SetValue(FrameEffectMask); }
    }

    /// <summary>Animate-atlas frame: emulate flixel-animate's RenderTexture (size ceil(bounds), origin bounds.min).</summary>
    private void SetFrameFxComposite(Texture2D tex, Rectangle rect, Vector2 item3, (float X, float Y, float W, float H) tb)
    {
        if (FrameEffect == null) return;
        SetFrameFxCommon(tex);
        var p = FrameEffect.Parameters;
        float W = tex.Width, H = tex.Height;
        float ox = rect.X + item3.X, oy = rect.Y + item3.Y;          // texel of animation-space (0,0)
        float rtW = MathF.Ceiling(tb.W), rtH = MathF.Ceiling(tb.H);
        p["uTextureSize"]?.SetValue(new Vector2(rtW, rtH));
        p["uLogicalRect"]?.SetValue(new Vector4((ox + tb.X) / W, (oy + tb.Y) / H, (ox + tb.X + rtW) / W, (oy + tb.Y + rtH) / H));
        p["uValidRect"]?.SetValue(new Vector4(rect.Left / W, rect.Top / H, rect.Right / W, rect.Bottom / H));
        p["uFrameBounds"]?.SetValue(new Vector4(0, 0, tb.W, tb.H));  // animateFrame.uv in PIXELS (upstream quirk)
        p["angOffset"]?.SetValue(0f);
    }

    /// <summary>Sparrow frame: frame.uv on the sheet; rotated frames have angle -90.</summary>
    private void SetFrameFxSparrow(Texture2D tex, SpriteFrame f)
    {
        if (FrameEffect == null) return;
        SetFrameFxCommon(tex);
        var p = FrameEffect.Parameters;
        float W = tex.Width, H = tex.Height; var r = f.SourceRect;
        p["uTextureSize"]?.SetValue(new Vector2(W, H));
        p["uLogicalRect"]?.SetValue(new Vector4(0, 0, 1, 1));
        p["uValidRect"]?.SetValue(new Vector4(0, 0, 1, 1));
        p["uFrameBounds"]?.SetValue(new Vector4(r.Left / W, r.Top / H, r.Right / W, r.Bottom / H));
        p["angOffset"]?.SetValue(f.Rotated ? -MathF.PI / 2f : 0f);
    }
    private string _resolvedAnimName = ""; // Actual key in Animations/CompositeAnimations
    private List<SpriteFrame> _currentFrames;
    private int _currentFrameIndex;
    private float _frameTimer;
    private float _frameRate = 24f; // FPS
    private bool _looping = true;
    private bool _finished;
    private int _loopFrame = 0; // Frame to loop back to when looping
    public string CurrentAnimation => _currentAnim;
    public bool Finished => _finished; public bool Looping => _looping;
    public int FrameIndex => _currentFrameIndex;
    public int FrameCount => _currentFrames?.Count ?? 0;
    public Action OnFinish { get; set; }
    /// <summary>
    /// Animation frame rate in FPS. Original FNF default is 24.
    /// Can be overridden per-animation from character JSON data.
    /// </summary>
    public float FrameRate
    {
        get => _frameRate;
        set => _frameRate = Math.Max(1f, value);
    }
    public void PlayAnimation(string name, bool force = false, bool loop = true)
    {
        _stopAtFrame = -1;
        if (_currentAnim != name || force) _reversed = false;
        if (_currentAnim == name && !force) return;
        // Try exact match first (fast path when name is already resolved)
        List<SpriteFrame> frames = null;
        if (Sheet != null && Sheet.JITComposites.TryGetValue(name, out var jitNow) && !jitNow.Rendered && SpriteSheet.JitDevice != null)
            Sheet.RenderJITAnimation(SpriteSheet.JitDevice, name);   // lazily composited anim: render it now (once)
        if (Sheet != null && Sheet.Animations.TryGetValue(name, out frames) && frames.Count > 0)
        {
            _currentAnim = name;
            _resolvedAnimName = name;
            _currentFrames = frames;
            _currentFrameIndex = 0;
            _frameTimer = 0;
            _looping = loop;
            _finished = false;
            return;
        }
        // Check pre-rendered CompositeAnimations (already GPU-rendered)
        if (Sheet != null && Sheet.CompositeAnimations.TryGetValue(name, out var compFrames) && compFrames.Count > 0)
        {
            _currentAnim = name;
            _resolvedAnimName = name;
            // Create placeholder frames matching composite frame count for Update() advancement
            _currentFrames = CreatePlaceholderFrames(name, compFrames.Count);
            _currentFrameIndex = 0;
            _frameTimer = 0;
            _looping = loop;
            _finished = false;
            return;
        }
        // Check RawCompositeData (runtime compositing � parts drawn individually in Draw)
        if (Sheet != null && Sheet.RawCompositeData.TryGetValue(name, out var rawFrames) && rawFrames.Count > 0)
        {
            _currentAnim = name;
            _resolvedAnimName = name;
            _currentFrames = CreatePlaceholderFrames(name, rawFrames.Count);
            _currentFrameIndex = 0;
            _frameTimer = 0;
            _looping = loop;
            _finished = false;
            return;
        }
        // Check JIT composites (deferred GPU rendering � sets _resolvedAnimName so
        // EnsureCurrentFrameRendered can trigger RenderJITAnimation on the main thread)
        if (Sheet != null && Sheet.JITComposites.TryGetValue(name, out var jit))
        {
            _currentAnim = name;
            _resolvedAnimName = name;
            // Create placeholder frames matching tick count for Update() advancement
            _currentFrames = CreatePlaceholderFrames(name, jit.TickParts.Count);
            _currentFrameIndex = 0;
            _frameTimer = 0;
            _looping = loop;
            _finished = false;
            return;
        }
        // Fallback to fuzzy search in Animations
        frames = Sheet?.GetAnimationFuzzy(name);
        if (frames != null && frames.Count > 0)
        {
            _currentAnim = name;
            // Find the resolved key name
            _resolvedAnimName = name;
            foreach (var kvp in Sheet.Animations)
            {
                if (kvp.Value == frames) { _resolvedAnimName = kvp.Key; break; }
            }
            _currentFrames = frames;
            _currentFrameIndex = 0;
            _frameTimer = 0;
            _looping = loop;
            _finished = false;
        }
    }
    private static List<SpriteFrame> CreatePlaceholderFrames(string name, int count)
    {
        var frames = new List<SpriteFrame>(count);
        for (int i = 0; i < count; i++)
            frames.Add(new SpriteFrame { Name = $"{name}_{i}" });
        return frames;
    }
    public void PlayAnimationFromFrame(string name, int startFrame, bool loop = true, int loopFrame = 0)
    {
        PlayAnimation(name, true, loop);
        if (_currentFrames != null && startFrame < _currentFrames.Count)
            _currentFrameIndex = startFrame;
        _loopFrame = loopFrame;
    }

    /// <summary>
    /// Sets an animation to a specific frame and freezes it there (no advancement).
    /// Used to show the highscore label immediately while waiting for its random timer.
    /// </summary>
    /// <summary>
    /// flixel-animate anim.play('', true, false, startFrame) followed by anim.pause() once the frame
    /// counter reaches stopFrame (ABot pupils: 0->17 = look left, 17->30 = look back).
    /// </summary>
    public void PlayRangeThenPause(string name, int startFrame, int stopFrame)
    {
        PlayAnimation(name, true, false);
        if (_currentFrames != null && startFrame >= 0 && startFrame < _currentFrames.Count)
            _currentFrameIndex = startFrame;
        _frameTimer = 0f;
        _stopAtFrame = stopFrame;
    }
    private int _stopAtFrame = -1;
    private bool _reversed;
    public bool Reversed => _reversed;
    /// <summary>Play the current animation backwards from its last frame (FlxAnimation.reversed).</summary>
    public void StartReversed()
    {
        _reversed = true; _finished = false; _frameTimer = 0f;
        _currentFrameIndex = Math.Max(0, (_currentFrames?.Count ?? 1) - 1);
    }

    /// <summary>Snapshot / restore of the animation playback state (FlxTrail ghosts draw a recorded frame).</summary>
    public object SaveAnimState() => new object[] { _currentAnim, _resolvedAnimName, _currentFrames, _currentFrameIndex, _frameTimer, _frameRate, _looping, _finished, _loopFrame, _stopAtFrame, _reversed };
    public void RestoreAnimState(object state)
    {
        if (state is not object[] a || a.Length != 11) return;
        _currentAnim = (string)a[0]; _resolvedAnimName = (string)a[1]; _currentFrames = (List<SpriteFrame>)a[2];
        _currentFrameIndex = (int)a[3]; _frameTimer = (float)a[4]; _frameRate = (float)a[5]; _looping = (bool)a[6];
        _finished = (bool)a[7]; _loopFrame = (int)a[8]; _stopAtFrame = (int)a[9]; _reversed = (bool)a[10];
    }

    public void FreezeOnFrame(string name, int frameIndex)
    {
        PlayAnimation(name, true, false);
        if (_currentFrames != null && frameIndex >= 0 && frameIndex < _currentFrames.Count)
        {
            _currentFrameIndex = frameIndex;
            _finished = true;
            _frameTimer = 0f;
        }
    }

    public void Update(float deltaTime)
    {
        if (_currentFrames == null || _currentFrames.Count == 0 || _finished)
            return;
        if (_reversed)
        {
            // flixel FlxAnimation reversed: step backwards; looped wraps to the last frame
            _frameTimer += deltaTime;
            float rd = 1f / _frameRate;
            while (_frameTimer >= rd)
            {
                _frameTimer -= rd;
                _currentFrameIndex--;
                if (_currentFrameIndex < 0)
                {
                    if (_looping) _currentFrameIndex = _currentFrames.Count - 1;
                    else { _currentFrameIndex = 0; _finished = true; _frameTimer = 0; OnFinish?.Invoke(); break; }
                }
            }
            return;
        }
        _frameTimer += deltaTime;
        float frameDuration = 1f / _frameRate;
        while (_frameTimer >= frameDuration)
        {
            _frameTimer -= frameDuration;
            _currentFrameIndex++;
            if (_stopAtFrame >= 0 && _currentFrameIndex >= _stopAtFrame)
            {
                _currentFrameIndex = Math.Min(_stopAtFrame, (_currentFrames?.Count ?? 1) - 1);
                _stopAtFrame = -1; _finished = true; _frameTimer = 0f;
                break;
            }
            if (_currentFrameIndex >= _currentFrames.Count)
            {
                if (_looping)
                {
                    _currentFrameIndex = Math.Min(_loopFrame, _currentFrames.Count - 1);
                }
                else
                {
                    _currentFrameIndex = _currentFrames.Count - 1;
                    _finished = true;
                    _frameTimer = 0; // Clear accumulated time before callback
                    OnFinish?.Invoke();
                    break; // Exit loop � callback may have reset animation state
                }
            }
        }
    }
    public string LastDebug = "";
    internal static readonly bool DebugDraw = Environment.GetEnvironmentVariable("FNF_CAMLOG") == "1";
    public void Draw(SpriteBatch spriteBatch)
    {
        if (Sheet == null || _currentFrames == null || _currentFrames.Count == 0)
            return;

        if (!string.IsNullOrEmpty(_resolvedAnimName) &&
            Sheet.CompositeAnimations.TryGetValue(_resolvedAnimName, out var compFrames) &&
            compFrames.Count > 0)
        {
            int num = _currentFrameIndex % compFrames.Count;
            (Texture2D, Rectangle, Vector2) tuple = compFrames[num];
            Texture2D item = tuple.Item1;
            Rectangle item2 = tuple.Item2;
            Vector2 item3 = tuple.Item3;
            Vector2 vector = Sheet.CompositeBaseOrigin ?? CompositeCells.LogicalOrigin(tuple);
            List<Vector2> value2;
            Vector2 vector2 = ((Sheet.CompositeFrameOffsets.TryGetValue(_resolvedAnimName, out value2) && num < value2.Count) ? value2[num] : Sheet.RawCompositeOffset);
            Vector2 zero = Vector2.Zero;
            if (Sheet.CompositeFrameSizes.TryGetValue(_resolvedAnimName, out List<(int, int)> value3) && value2 != null && value3.Count > 0 && num < value3.Count)
            {
                (int, int) tuple2 = value3[num];
                int item4 = tuple2.Item1;
                int item5 = tuple2.Item2;
                Vector2 vector3 = value2[num];
                bool flag = Sheet.CompositeBaseSize.Width > 0;
                int num2;
                int num3;
                if (flag)
                {
                    (num2, num3) = Sheet.CompositeBaseSize;
                }
                else
                {
                    int item6 = value3[0].Item1;
                    int item7 = value3[0].Item2;
                    num3 = item7;
                    num2 = item6;
                }
                Vector2 vector4 = (flag ? Sheet.CompositeBaseOffset : value2[0]);
                zero.X = vector4.X + (float)num2 * 0.5f - (vector3.X + (float)item4 * 0.5f);
                zero.Y = vector4.Y + (float)num3 - (vector3.Y + (float)item5);
            }
            Vector2 position = Position + zero - vector - vector2;
            if (SpriteSheet.TightComposites && !UseTimelineAnchoring)
            {
                // trimmed cell: place it where it sat inside the untrimmed cell (rotation about the cell top-left)
                var tro = CompositeCells.TrimOffset(tuple, Effects) * Scale;
                if (Rotation != 0f) { float cs = MathF.Cos(Rotation), sn = MathF.Sin(Rotation); tro = new Vector2(tro.X * cs - tro.Y * sn, tro.X * sn + tro.Y * cs); }
                position += tro;
            }
            if (UseTimelineAnchoring && Sheet.GetTimelineBounds(_resolvedAnimName) is { } tb)
            {
                // flixel-animate: point p of the timeline is drawn at screenPos + (p - bounds.min) * scale
                // (mirrored inside the bounds when flipped). The pre-rendered texture's top-left is at
                // -item3 in timeline space, so it never moves between frames.
                float u = -item3.X - tb.X, v = -item3.Y - tb.Y;
                if ((Effects & SpriteEffects.FlipHorizontally) != 0) u = tb.W - (u + item2.Width);
                if ((Effects & SpriteEffects.FlipVertically) != 0) v = tb.H - (v + item2.Height);
                position = Position + new Vector2(u * Scale.X, v * Scale.Y);
                if (DebugDraw) LastDebug = $"TL anim={_resolvedAnimName} boxTopLeft={Position} texTopLeft={position} item3={item3} tb={tb}";
            }
            if (DebugDraw) LastDebug = $"composite anim={_resolvedAnimName} final={position} base={Sheet.CompositeBaseOrigin} item3={item3} off={vector2} zero={zero} src={item2} tex={item.Width}x{item.Height}";
            SpriteEffects effects = Effects;
            if (FrameEffect != null)
            {
                var fxb = Sheet.GetTimelineBounds(_resolvedAnimName) ?? LogicalBounds(tuple);
                SetFrameFxComposite(item, item2, item3, fxb);
            }
            if (CompositeLayer != 2) spriteBatch.Draw(item, position, item2, Tint, Rotation, Vector2.Zero, Scale, effects, 0f);
            if (CompositeLayer != 1 && Sheet.CompositeAbove.TryGetValue(_resolvedAnimName, out var aboveFrames) && aboveFrames.Count > 0)
            {
                var ab = aboveFrames[num % aboveFrames.Count];
                if (ab.Tex != null)
                {
                    Vector2 apos = position + (item3 - ab.Origin) * Scale;
                    if (UseTimelineAnchoring && Sheet.GetTimelineBounds(_resolvedAnimName) is { } atb)
                    {
                        float u = -ab.Origin.X - atb.X, v = -ab.Origin.Y - atb.Y;
                        if ((Effects & SpriteEffects.FlipHorizontally) != 0) u = atb.W - (u + ab.Rect.Width);
                        if ((Effects & SpriteEffects.FlipVertically) != 0) v = atb.H - (v + ab.Rect.Height);
                        apos = Position + new Vector2(u * Scale.X, v * Scale.Y);
                    }
                    if (FrameEffect != null)
                        SetFrameFxComposite(ab.Tex, ab.Rect, ab.Origin, Sheet.GetTimelineBounds(_resolvedAnimName) ?? LogicalBounds(tuple));
                    spriteBatch.Draw(ab.Tex, apos, ab.Rect, Tint, Rotation, Vector2.Zero, Scale, effects, 0f);
                }
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(_resolvedAnimName) &&
                Sheet.RawCompositeData.TryGetValue(_resolvedAnimName, out var rawFrames) &&
                rawFrames.Count > 0)
            {
                if (Sheet.Texture == null)
                    return;

                int num4 = _currentFrameIndex % rawFrames.Count;
                List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)> list = rawFrames[num4];
                float x = Scale.X;
                float y = Scale.Y;
                List<Vector2> value5;
                Vector2 vector5 = ((Sheet.CompositeFrameOffsets.TryGetValue(_resolvedAnimName, out value5) && num4 < value5.Count) ? value5[num4] : Sheet.RawCompositeOffset);
                Vector2 zero2 = Vector2.Zero;
                if (Sheet.CompositeFrameSizes.TryGetValue(_resolvedAnimName, out List<(int, int)> value6) && value5 != null && value6.Count > 0 && num4 < value6.Count)
                {
                    (int, int) tuple4 = value6[0];
                    int item8 = tuple4.Item1;
                    int item9 = tuple4.Item2;
                    Vector2 vector6 = value5[0];
                    (int, int) tuple5 = value6[num4];
                    int item10 = tuple5.Item1;
                    int item11 = tuple5.Item2;
                    Vector2 vector7 = value5[num4];
                    zero2.X = vector6.X + (float)item8 * 0.5f - (vector7.X + (float)item10 * 0.5f);
                    zero2.Y = vector6.Y + (float)item9 - (vector7.Y + (float)item11);
                }
                float num5 = Position.X + zero2.X - vector5.X * x;
                float num6 = Position.Y + zero2.Y - vector5.Y * y;
                if (UseTimelineAnchoring && Sheet.GetTimelineBounds(_resolvedAnimName) is { } rtb)
                {
                    num5 = Position.X - rtb.X * x;   // flixel-animate translate(-bounds.x, -bounds.y)
                    num6 = Position.Y - rtb.Y * y;
                }
                foreach (var (frame, num7, num8, num9, num10, num11, num12) in list)
                {
                    DrawTransformedPart(spriteBatch, frame.Texture ?? Sheet.Texture, frame, num7 * x, num8 * y, num9 * x, num10 * y, num11 * x + num5, num12 * y + num6);
                }
                return;
            }

            if (Sheet.Texture == null)
                return;

            SpriteFrame spriteFrame = _currentFrames[_currentFrameIndex];
            Vector2 vector8 = spriteFrame.Offset;
            if (spriteFrame.Rotated)
            {
                bool flag2 = (Effects & SpriteEffects.FlipHorizontally) != 0;
                if (flag2 && spriteFrame.FrameWidth > 0)
                {
                    vector8 = new Vector2((float)(spriteFrame.FrameWidth - spriteFrame.SourceRect.Height) - vector8.X, vector8.Y);
                }
                Vector2 position2 = Position + vector8 * Scale;
                Vector2 origin = new Vector2(spriteFrame.SourceRect.Width, 0f);
                float rotation = Rotation - (float)Math.PI / 2f;
                SpriteEffects effects2 = SpriteEffects.None;
                if (flag2)
                {
                    effects2 = SpriteEffects.FlipVertically;
                }
                SetFrameFxSparrow(spriteFrame.Texture ?? Sheet.Texture, spriteFrame);
                spriteBatch.Draw(spriteFrame.Texture ?? Sheet.Texture, position2, spriteFrame.SourceRect, Tint, rotation, origin, Scale, effects2, 0f);
            }
            else
            {
                if ((Effects & SpriteEffects.FlipHorizontally) != SpriteEffects.None && spriteFrame.FrameWidth > 0)
                {
                    vector8 = new Vector2((float)(spriteFrame.FrameWidth - spriteFrame.SourceRect.Width) - vector8.X, vector8.Y);
                }
                if (RotateAroundCenter && Rotation != 0f)
                {
                    var c = new Vector2((spriteFrame.FrameWidth > 0 ? spriteFrame.FrameWidth : spriteFrame.SourceRect.Width) / 2f,
                                        (spriteFrame.FrameHeight > 0 ? spriteFrame.FrameHeight : spriteFrame.SourceRect.Height) / 2f);
                    SetFrameFxSparrow(spriteFrame.Texture ?? Sheet.Texture, spriteFrame);
                    spriteBatch.Draw(spriteFrame.Texture ?? Sheet.Texture, Position + c * Scale, spriteFrame.SourceRect, Tint, Rotation, c - vector8, Scale, Effects, 0f);
                }
                else
                {
                Vector2 position3 = Position + vector8 * Scale;
                SetFrameFxSparrow(spriteFrame.Texture ?? Sheet.Texture, spriteFrame);
                spriteBatch.Draw(spriteFrame.Texture ?? Sheet.Texture, position3, spriteFrame.SourceRect, Tint, Rotation, Vector2.Zero, Scale, Effects, 0f);
                }
            }
        }
    }
    public SpriteFrame GetCurrentFrame()
    {
        if (_currentFrames == null || _currentFrames.Count == 0)
            return null;
        return _currentFrames[_currentFrameIndex];
    }
    /// <summary>
    /// Get the composite registration point (origin) for the current animation frame.
    /// Returns null if the current animation is not a composite (animateatlas) animation.
    /// The origin represents the animation's (0,0) registration point within the composite texture,
    /// which for FNF characters is typically at the character's feet/anchor position.
    /// </summary>
    private static (float X, float Y, float W, float H) LogicalBounds((Texture2D Tex, Rectangle Rect, Vector2 Origin) f)
    {
        var o = CompositeCells.LogicalOrigin(f); var sz = CompositeCells.LogicalSize(f);
        return (-o.X, -o.Y, sz.W, sz.H);
    }
    public Vector2? GetCompositeOrigin()
    {
        if (Sheet == null || string.IsNullOrEmpty(_resolvedAnimName)) return null;
        // Use the fixed base origin (from the first loaded composite, typically idle)
        // to keep the registration point consistent across all animations.
        if (Sheet.CompositeBaseOrigin.HasValue)
            return Sheet.CompositeBaseOrigin.Value;
        if (Sheet.CompositeAnimations.TryGetValue(_resolvedAnimName, out var compFrames) && compFrames.Count > 0)
        {
            int idx = _currentFrameIndex % compFrames.Count;
            return CompositeCells.LogicalOrigin(compFrames[idx]);
        }
        // JIT fallback: use precomputed origin before GPU rendering has occurred
        if (Sheet.JITComposites.TryGetValue(_resolvedAnimName, out var jit))
            return jit.Origin;
        return null;
    }
    /// <summary>
    /// Ensure the current animation's composite frame is GPU-rendered and cached.
    /// Must be called on the main thread BEFORE SpriteBatch.Begin/Draw.
    /// For JIT composites, this triggers PreRenderComposite on first access.
    /// </summary>
    public void EnsureCurrentFrameRendered(GraphicsDevice gd)
    {
        if (Sheet == null || string.IsNullOrEmpty(_resolvedAnimName)) return;
        if (!Sheet.JITComposites.TryGetValue(_resolvedAnimName, out var jit) || jit.Rendered) return;
        Sheet.RenderJITAnimation(gd, _resolvedAnimName);
        // Refresh _currentFrames since RenderJITAnimation updates Animations
        if (Sheet.Animations.TryGetValue(_resolvedAnimName, out var newFrames))
            _currentFrames = newFrames;
    }
    /// <summary>
    /// Get the width of the current animation frame in pixels.
    /// Handles composites, JIT, runtime, and normal Sparrow frames properly.
    /// </summary>
    public int GetFrameWidth()
    {
        return GetCurrentFrameSize().Width;
    }
    /// <summary>
    /// Get the height of the current animation frame in pixels.
    /// Uses logical frame size (FrameHeight), NOT trimmed source size, matching Flixel.
    /// </summary>
    public int GetFrameHeight()
    {
        return GetCurrentFrameSize().Height;
    }
    /// <summary>
    /// Get the logical size of the currently displayed frame.
    /// For composites, returns the per-frame size if available; otherwise falls back to the first frame logical size.
    /// </summary>
    public (int Width, int Height) GetCurrentFrameSize()
    {
        if (Sheet == null || string.IsNullOrEmpty(_resolvedAnimName)) return (0, 0);
        if (UseTimelineAnchoring && Sheet.GetTimelineBounds(_resolvedAnimName) is { } fb)
            return ((int)fb.W, (int)fb.H); // FlxFrame frameWidth/Height = Std.int(sourceSize)
        if (Sheet.CompositeFrameSizes.TryGetValue(_resolvedAnimName, out var sizes) && _currentFrameIndex >= 0 && _currentFrameIndex < sizes.Count)
            return sizes[_currentFrameIndex];
        if (Sheet.CompositeLogicalSizes.TryGetValue(_resolvedAnimName, out var logicalSize))
            return logicalSize;
        var frame = GetCurrentFrame();
        if (frame != null)
        {
            // FlxAtlasFrames.fromSparrow: frameWidth/frameHeight are the UNROTATED source size; only the atlas
            // rect (width/height) is stored rotated. Swapping both turned Pico's 452x475 idle into 475x452.
            if (frame.FrameWidth > 0 && frame.FrameHeight > 0) return (frame.FrameWidth, frame.FrameHeight);
            int fw = frame.SourceRect.Width, fh = frame.SourceRect.Height;
            return frame.Rotated ? (fh, fw) : (fw, fh);
        }
        return (0, 0);
    }
    /// <summary>
    /// Returns the fixed composite base origin (from idle/first animation).
    /// Using a fixed origin prevents position jumps when animations change.
    /// Returns Vector2.Zero if not using CompositeAnimations.
    /// </summary>
    public Vector2 GetCurrentCompOrigin()
    {
        if (Sheet != null)
        {
            if (Sheet.CompositeBaseOrigin.HasValue)
                return Sheet.CompositeBaseOrigin.Value;
            if (!string.IsNullOrEmpty(_resolvedAnimName) &&
                Sheet.CompositeAnimations.TryGetValue(_resolvedAnimName, out var compFrames) &&
                compFrames.Count > 0)
            {
                return CompositeCells.LogicalOrigin(compFrames[0]);
            }
        }
        return Vector2.Zero;
    }
    public static (int Width, int Height) ComputeRawBoundingBox(List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)> parts)
    {
        var (minX, minY, maxX, maxY) = ComputeRawBounds(parts);
        return ((int)MathF.Ceiling(maxX - minX), (int)MathF.Ceiling(maxY - minY));
    }
    /// <summary>
    /// Compute the raw bounding box min/max for a frame's parts.
    /// Returns (minX, minY, maxX, maxY) - matching FlxAnimate's getBounds().
    /// The (minX, minY) is what FlxAnimate uses as 'offset' in updateHitbox().
    /// </summary>
    public static (float MinX, float MinY, float MaxX, float MaxY) ComputeRawBounds(List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)> parts)
    {
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        foreach (var (frame, a, b, c, d, tx, ty) in parts)
        {
            float w = frame.Rotated ? frame.SourceRect.Height : frame.SourceRect.Width;
            float h = frame.Rotated ? frame.SourceRect.Width : frame.SourceRect.Height;
            float x0 = tx, y0 = ty;
            float x1 = a * w + tx, y1 = b * w + ty;
            float x2 = c * h + tx, y2 = d * h + ty;
            float x3 = a * w + c * h + tx, y3 = b * w + d * h + ty;
            minX = Math.Min(minX, Math.Min(Math.Min(x0, x1), Math.Min(x2, x3)));
            minY = Math.Min(minY, Math.Min(Math.Min(y0, y1), Math.Min(y2, y3)));
            maxX = Math.Max(maxX, Math.Max(Math.Max(x0, x1), Math.Max(x2, x3)));
            maxY = Math.Max(maxY, Math.Max(Math.Max(y0, y1), Math.Max(y2, y3)));
        }
        return (minX, minY, maxX, maxY);
    }
    /// <summary>
    /// Check if the current animation uses runtime compositing (RawCompositeData).
    /// When true, Position acts as an offset to the absolute M3D screen positions.
    /// </summary>
    public bool IsRuntimeComposite()
    {
        if (Sheet == null || string.IsNullOrEmpty(_resolvedAnimName)) return false;
        return Sheet.RawCompositeData.ContainsKey(_resolvedAnimName);
    }
    /// <summary>
    /// Draw a single body part with full affine transform decomposition.
    /// Handles atlas-rotated sprites, flips, rotation, and scale.
    /// </summary>
    internal void DrawTransformedPart(SpriteBatch spriteBatch, Texture2D atlas, SpriteFrame frame,
        float a, float b, float c, float d, float tx, float ty)
    {
        float det = a * d - b * c;
        float rotation;
        float scaleX, scaleY;
        var effects = SpriteEffects.None;
        float drawX = tx;
        float drawY = ty;
        Vector2 drawOrigin = Vector2.Zero;
        // Rotated atlas sprites are stored 90� CW. Need -90� correction.
        float rotOffset = frame.Rotated ? -MathF.PI / 2f : 0f;
        if (frame.Rotated)
            drawOrigin = new Vector2(frame.SourceRect.Width, 0);
        float unrotW = frame.Rotated ? frame.SourceRect.Height : frame.SourceRect.Width;
        float unrotH = frame.Rotated ? frame.SourceRect.Width : frame.SourceRect.Height;
        // Fast path: no M3D rotation/skew (just scale + optional flip)
        if (MathF.Abs(b) < 0.001f && MathF.Abs(c) < 0.001f)
        {
            rotation = rotOffset;
            scaleX = MathF.Abs(a);
            scaleY = MathF.Abs(d);
            if (a < 0)
            {
                if (frame.Rotated)
                    effects |= SpriteEffects.FlipVertically;
                else
                    effects |= SpriteEffects.FlipHorizontally;
                drawX = a * unrotW + tx;
            }
            if (d < 0)
            {
                if (frame.Rotated)
                    effects |= SpriteEffects.FlipHorizontally;
                else
                    effects |= SpriteEffects.FlipVertically;
                drawY = d * unrotH + ty;
            }
        }
        else
        {
            // General rotation + scale decomposition
            rotation = MathF.Atan2(b, a) + rotOffset;
            scaleX = MathF.Sqrt(a * a + b * b);
            scaleY = MathF.Sqrt(c * c + d * d);
            if (det < 0) scaleY = -scaleY;
        }
        spriteBatch.Draw(atlas,
            new Vector2(drawX, drawY),
            frame.SourceRect,
            Tint,
            rotation,
            drawOrigin,
            new Vector2(scaleX, scaleY),
            effects,
            0f);
    }
}
