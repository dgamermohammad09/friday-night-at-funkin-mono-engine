using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine ModchartSprite — sprite used by Lua/HScript mod scripts.
/// Direct port of psychlua/ModchartSprite.hx.
/// Supports animation offsets and basic sprite operations.
/// </summary>
public class ModchartSprite : FNF_MonoGame.Scenes.PlayScene.StaticStageProp
{
    public new float X { get => base.X; set => base.X = value; }
    public new float Y { get => base.Y; set => base.Y = value; }
    public new float ScaleX { get => base.ScaleX; set => base.ScaleX = value; }
    public new float ScaleY { get => base.ScaleY; set => base.ScaleY = value; }
    public new float Alpha { get => base.Alpha; set => base.Alpha = value; }
    public new float Angle { get => base.Angle; set => base.Angle = value; }
    public float ScrollFactorX { get => ScrollX; set => ScrollX = value; }
    public float ScrollFactorY { get => ScrollY; set => ScrollY = value; }
    public new bool Visible { get => base.Visible; set => base.Visible = value; }
    public bool Active { get; set; } = true;
    public bool Antialiasing { get; set; } = true;
    public new int ZIndex { get => base.ZIndex; set => base.ZIndex = value; }
    public string BlendMode { get => Blend; set => Blend = value; }
    public new string Camera { get => base.Camera; set => base.Camera = value; }

    public new Texture2D Texture { get => base.Texture; set => base.Texture = value; }
    public SpriteSheet SpriteSheet { get; set; }
    private int? _colorRaw;

    // Animation offsets
    private readonly Dictionary<string, Vector2> _animOffsets = new(System.StringComparer.OrdinalIgnoreCase);
    private Vector2 _currentOffset;

    // Registered animations live on the base StaticStageProp so the shared UpdateAnimation
    // path can advance them.

    // Width/Height
    public int Width => Texture?.Width ?? 0;
    public int Height => Texture?.Height ?? 0;
    public int FrameWidth => Width;
    public int FrameHeight => Height;

    public ModchartSprite(float x = 0, float y = 0)
    {
        X = x;
        Y = y;
        Antialiasing = ClientPrefs.Data.Antialiasing;
        Animations = new Dictionary<string, ModchartAnimation>(System.StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>HScript `new FlxSprite()`: flixel defaults (scale 1, alpha 1, scrollFactor 1, offset 0 -> scaled about the centre).</summary>
    public static ModchartSprite CreateForHScript(float x = 0, float y = 0)
    {
        var s = new ModchartSprite(x, y);
        s.ScaleX = s.ScaleY = 1f; s.Alpha = 1f; s.ScrollX = s.ScrollY = 1f; s.Visible = true;
        s.PsychOffsetX = 0f; s.PsychOffsetY = 0f;
        return s;
    }

    private FNF_MonoGame.Scenes.HxPoint _scalePt;
    /// <summary>FlxSprite.scale (FlxPoint): x/y bound to ScaleX/ScaleY.</summary>
    public FNF_MonoGame.Scenes.HxPoint scale => _scalePt ??= new FNF_MonoGame.Scenes.HxPoint(() => ScaleX, v => ScaleX = v, () => ScaleY, v => ScaleY = v);

    /// <summary>FlxSprite.color as FlxColor int (0xAARRGGBB); alpha byte ignored like flixel's colour transform.</summary>
    public new object color
    {
        // flixel: `color(default, set):FlxColor = 0xffffff`; set_color stores the value as given (getProperty returns it)
        get => _colorRaw ?? (OverlayColor is Microsoft.Xna.Framework.Color c ? ((c.R << 16) | (c.G << 8) | c.B) : 0xFFFFFF);
        set
        {
            if (value is Microsoft.Xna.Framework.Color col) { OverlayColor = col; _colorRaw = null; return; }
            long v = System.Convert.ToInt64(value);
            _colorRaw = unchecked((int)v);
            OverlayColor = new Microsoft.Xna.Framework.Color((int)((v >> 16) & 255), (int)((v >> 8) & 255), (int)(v & 255));
        }
    }

    public void PlayAnim(string name, bool forced = false, bool reverse = false, int startFrame = 0)
    {
        if (!Animations.TryGetValue(name, out var anim) || anim.FrameRects.Count == 0)
            return;
        if (!forced && base.CurrentAnimation == name && base.AnimPlaying)
            return;

        base.CurrentAnimation = name;
        base.AnimFramerate = anim.Framerate;
        base.AnimLoop = anim.Loop;
        base.AnimTimer = 0f;
        base.AnimPlaying = true;
        base.CurrentFrame = reverse ? Math.Max(0, anim.FrameRects.Count - 1 - startFrame) : Math.Max(0, startFrame);
        base.SourceRect = anim.FrameRects[base.CurrentFrame];
        base.Texture = SpriteSheet?.Texture;

        if (_animOffsets.TryGetValue(name, out var offset))
            _currentOffset = offset;
        else
            _currentOffset = Vector2.Zero;
    }

    /// <summary>
    /// Register an animation by frame-name prefix (FlxSprite.animation.addByPrefix).
    /// </summary>
    public void AddAnimationByPrefix(string name, string prefix, int framerate = 24, bool loop = true)
    {
        var anim = new ModchartAnimation
        {
            Name = name,
            Prefix = prefix,
            Framerate = framerate,
            Loop = loop
        };

        if (SpriteSheet?.Frames != null)
        {
            var frames = SpriteSheet.Frames
                .Where(f => f.Key.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Value)
                .OrderBy(f => ExtractFrameNumber(f.Name))
                .ToList();
            foreach (var frame in frames)
            {
                anim.FrameRects.Add(frame.SourceRect);
                anim.FrameOffsets.Add(frame.Offset);
                anim.FrameSizes.Add(new Point(frame.FrameWidth > 0 ? frame.FrameWidth : frame.SourceRect.Width, frame.FrameHeight > 0 ? frame.FrameHeight : frame.SourceRect.Height));
            }
        }

        Animations[name] = anim;
        if (base.CurrentAnimation == null && anim.FrameRects.Count > 0) PlayAnim(name, true); // Psych plays the first animation added
    }

    /// <summary>
    /// Register an animation by explicit frame indices (FlxSprite.animation.addByIndices).
    /// </summary>
    public void AddAnimationByIndices(string name, string prefix, int[] indices, int framerate = 24, bool loop = false)
    {
        var anim = new ModchartAnimation
        {
            Name = name,
            Prefix = prefix,
            Indices = indices,
            Framerate = framerate,
            Loop = loop
        };

        if (SpriteSheet?.Frames != null && indices != null)
        {
            foreach (int index in indices)
            {
                SpriteFrame frame = FindFrame(SpriteSheet, prefix, index);
                if (frame != null)
                {
                    anim.FrameRects.Add(frame.SourceRect);
                    anim.FrameOffsets.Add(frame.Offset);
                    anim.FrameSizes.Add(new Point(frame.FrameWidth > 0 ? frame.FrameWidth : frame.SourceRect.Width, frame.FrameHeight > 0 ? frame.FrameHeight : frame.SourceRect.Height));
                }
            }
        }

        Animations[name] = anim;
        if (base.CurrentAnimation == null && anim.FrameRects.Count > 0) PlayAnim(name, true); // Psych plays the first animation added
    }

    private static SpriteFrame FindFrame(SpriteSheet sheet, string prefix, int index)
    {
        // Try the most common Sparrow/Starling naming patterns (prefix0000, prefix001, etc.)
        foreach (string candidate in new[] { $"{prefix}{index}", $"{prefix}{index:D4}", $"{prefix}{index:D3}", $"{prefix}{index:D2}", $"{prefix}{index:D1}" })
        {
            if (sheet.Frames.TryGetValue(candidate, out var frame))
                return frame;
            foreach (var kvp in sheet.Frames)
            {
                if (kvp.Key.Equals(candidate, System.StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }
        }
        return null;
    }

    private static int ExtractFrameNumber(string frameName)
    {
        if (string.IsNullOrEmpty(frameName)) return 0;
        var match = Regex.Match(frameName, @"(\d+)$");
        return match.Success && int.TryParse(match.Value, out var n) ? n : 0;
    }

    public void AddOffset(string name, float x, float y)
    {
        _animOffsets[name] = new Vector2(x, y);
    }

    /// <summary>
    /// Load a static graphic (single image, no animation).
    /// </summary>
    public ModchartSprite LoadGraphic(Texture2D texture)
    {
        Texture = texture;
        return this;
    }

    /// <summary>
    /// Make a solid colored rectangle.
    /// </summary>
    public ModchartSprite MakeGraphic(int width, int height, Color color)
    {
        var tex = new Texture2D(FNFGame.Instance.GraphicsDevice, width, height);
        var data = new Color[width * height];
        for (int i = 0; i < data.Length; i++)
            data[i] = color;
        tex.SetData(data);
        Texture = tex;
        return this;
    }

    /// <summary>
    /// Set the graphic size (adjusts scale to match).
    /// </summary>
    public void SetGraphicSize(int width = 0, int height = 0)
    {
        if (Texture == null) return;
        if (width > 0) ScaleX = (float)width / Texture.Width;
        if (height > 0) ScaleY = (float)height / Texture.Height;
        if (width > 0 && height <= 0) ScaleY = ScaleX;
        if (height > 0 && width <= 0) ScaleX = ScaleY;
    }

    /// <summary>
    /// Screen center the sprite.
    /// </summary>
    public void ScreenCenter(string axis = "XY")
    {
        if (Texture == null) return;
        if (axis.Contains('X'))
            X = (1280 - Texture.Width * ScaleX) / 2f;
        if (axis.Contains('Y'))
            Y = (720 - Texture.Height * ScaleY) / 2f;
    }

    /// <summary>
    /// Update hitbox (recalculate after scale change).
    /// </summary>
    public void UpdateHitbox()
    {
        // No-op for now — hitbox is computed from Texture size * scale
    }

    /// <summary>
    /// Draw the sprite.
    /// </summary>
    public void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible || Texture == null || Alpha <= 0) return;

        spriteBatch.Draw(
            Texture,
            new Vector2(X - _currentOffset.X, Y - _currentOffset.Y),
            null,
            Color.White * Alpha,
            MathHelper.ToRadians(Angle),
            Vector2.Zero,
            new Vector2(ScaleX, ScaleY),
            SpriteEffects.None,
            0f
        );
    }

    /// <summary>
    /// Convert to a StaticStageProp for integration with the existing stage system.
    /// Since ModchartSprite is now a StaticStageProp, this returns the live object.
    /// </summary>
    public FNF_MonoGame.Scenes.PlayScene.StaticStageProp ToStageProp() => this;

    public new string Name { get => base.Name; set => base.Name = value; }

    public void SetPosition(float x, float y) { X = x; Y = y; }
    public void Destroy() { /* scripted destroy is a no-op for now */ }
    public void Kill() { /* scripted kill is a no-op for now */ }
    public void Revive() { /* scripted revive is a no-op for now */ }

    /// <summary>
    /// Load a static graphic from a file path (HScript loadGraphic).
    /// </summary>
    public ModchartSprite LoadGraphic(string path)
    {
        if (string.IsNullOrEmpty(path)) return this;
        string resolved = PsychPaths.ResolvePath(path) ?? path;
        if (global::FNF_MonoGame.Engine.FsCI.FileExists(resolved))
            Texture = FNFGame.Instance?.Assets?.LoadTexture(resolved);
        return this;
    }

    /// <summary>
    /// MakeGraphic overload accepting a hex color string (HScript FlxColor constants).
    /// </summary>
    public ModchartSprite MakeGraphic(int width, int height, string color)
    {
        return MakeGraphic(width, height, ParseHexColor(color));
    }

    private static Color ParseHexColor(object color)
    {
        if (color is Color c) return c;
        if (color is uint u) return new Color((byte)((u >> 16) & 0xFF), (byte)((u >> 8) & 0xFF), (byte)(u & 0xFF), (byte)((u >> 24) & 0xFF));
        if (color is string s)
        {
            s = s.Trim().Replace("#", "").Replace("0x", "");
            if (uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var hex))
                return new Color((byte)((hex >> 16) & 0xFF), (byte)((hex >> 8) & 0xFF), (byte)(hex & 0xFF), (byte)((hex >> 24) & 0xFF));
        }
        return Color.White;
    }
}

/// <summary>
/// Animation definition registered from Lua (prefix/indices/framerate/loop).
/// </summary>
public class ModchartAnimation
{
    public string Name { get; set; }
    public string Prefix { get; set; }
    public int[] Indices { get; set; }
    public int Framerate { get; set; } = 24;
    public bool Loop { get; set; }

    public List<Rectangle> FrameRects { get; } = new();
    public List<Vector2> FrameOffsets { get; } = new();
    /// <summary>Untrimmed Sparrow frame size (frameWidth/frameHeight) per frame.</summary>
    public List<Point> FrameSizes { get; } = new();
    public float FrameDuration => Framerate > 0 ? 1f / Framerate : 0f;
}
