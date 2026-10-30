using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Engine.PsychEngine.Cutscenes;

/// <summary>
/// Minimal FlxSprite (flixel 5) used by the Psych cutscene classes: Sparrow frames, addByPrefix animations
/// (FlxAnimation update/finished/reverse rules), setGraphicSize/updateHitbox/centerOffsets and the flixel draw
/// matrix (frame trim offset, flipX inside sourceSize, scale about origin, x - offset). Screen-space (scrollFactor 0).
/// </summary>
public class FlxLiteSprite
{
    public sealed class Anim
    {
        public string Name; public List<SpriteFrame> Frames; public float FrameRate; public bool Looped;
        public int CurFrame; public bool Finished; public bool Reversed; public float Timer;
        public void Reverse() { Reversed = !Reversed; if (Finished) { Finished = false; Timer = 0; } }
        public void Restart() { CurFrame = Reversed ? Frames.Count - 1 : 0; Finished = false; Timer = 0; }
    }

    public SpriteSheet Sheet;
    public Texture2D Solid; public Color SolidColor = Color.White; // makeGraphic
    public float X, Y;
    public Vector2 Offset, Scale = Vector2.One, Origin;
    public float Width, Height;
    public int FrameWidth, FrameHeight;
    public bool FlipX, Visible = true, Alive = true;
    private float _alpha = 1f;
    /// <summary>FlxSprite.set_alpha clamps to 0..1.</summary>
    public float Alpha { get => _alpha; set => _alpha = value < 0f ? 0f : value > 1f ? 1f : value; }
    public Color Color = Color.White;
    public readonly Dictionary<string, Anim> Anims = new(StringComparer.Ordinal);
    public Anim CurAnim;
    private SpriteFrame _frame;

    public FlxLiteSprite(float x = 0, float y = 0) { X = x; Y = y; }

    public static float Clamp01(float a) => a < 0 ? 0 : a > 1 ? 1 : a;

    /// <summary>FlxSprite.frames setter: frame = frames[0], size/origin from it.</summary>
    public void SetFrames(SpriteSheet sheet)
    {
        Sheet = sheet; Anims.Clear(); CurAnim = null;
        _frame = sheet?.FirstFrame ?? sheet?.Frames.Values.FirstOrDefault();
        ResetFrameSize();
        Width = FrameWidth; Height = FrameHeight;
        Origin = new Vector2(FrameWidth * 0.5f, FrameHeight * 0.5f);
    }

    /// <summary>makeGraphic(w, h, color)</summary>
    public void MakeGraphic(int w, int h, Color c)
    {
        Sheet = null; Solid = Assets.Pixel; SolidColor = c; _frame = null;
        FrameWidth = w; FrameHeight = h; Width = w; Height = h; Origin = new Vector2(w * 0.5f, h * 0.5f);
    }

    private static AssetManager Assets => FNFGame.Instance.Assets;

    private void ResetFrameSize()
    {
        if (_frame == null) return;
        FrameWidth = _frame.FrameWidth > 0 ? _frame.FrameWidth : _frame.SourceRect.Width;
        FrameHeight = _frame.FrameHeight > 0 ? _frame.FrameHeight : _frame.SourceRect.Height;
    }

    private static int TrailingNumber(string name, int prefixLen)
    {
        int i = name.Length; while (i > prefixLen && char.IsDigit(name[i - 1])) i--;
        return i < name.Length && int.TryParse(name.Substring(i), out int n) ? n : 0;
    }

    /// <summary>animation.addByPrefix: frames whose name starts with the prefix (case-sensitive), sorted by the trailing number.</summary>
    public void AddByPrefix(string name, string prefix, float frameRate = 24, bool looped = true)
    {
        if (Sheet == null || string.IsNullOrEmpty(prefix)) return;
        var list = Sheet.Frames.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal)).Select(kv => kv.Value).ToList();
        if (list.Count == 0) return;
        list = list.Select((f, i) => (f, i)).OrderBy(t => TrailingNumber(t.f.Name ?? "", prefix.Length)).ThenBy(t => t.i).Select(t => t.f).ToList();
        Anims[name] = new Anim { Name = name, Frames = list, FrameRate = frameRate, Looped = looped };
    }

    /// <summary>animation.play(name, force, reversed, frame)</summary>
    public void Play(string name, bool force = false, bool reversed = false, int frame = 0)
    {
        if (name == null || !Anims.TryGetValue(name, out var a)) return;
        if (!force && CurAnim == a && !a.Finished) return;
        CurAnim = a; a.Reversed = reversed; a.Finished = false; a.Timer = 0;
        a.CurFrame = reversed ? a.Frames.Count - 1 - frame : frame;
        a.CurFrame = Math.Clamp(a.CurFrame, 0, a.Frames.Count - 1);
        SetFrame(a.Frames[a.CurFrame]);
    }

    private void SetFrame(SpriteFrame f) { _frame = f; ResetFrameSize(); }

    /// <summary>FlxAnimation.update: advance while timer > delay; non-looped anims finish when stepping past the end.</summary>
    public virtual void Update(float elapsed)
    {
        var a = CurAnim;
        if (a == null || a.Finished || a.FrameRate <= 0) return;
        float delay = 1f / a.FrameRate;
        a.Timer += elapsed;
        while (a.Timer > delay && !a.Finished)
        {
            a.Timer -= delay;
            if (a.Reversed)
            {
                if (a.CurFrame <= 0) { if (a.Looped) a.CurFrame = a.Frames.Count - 1; else a.Finished = true; }
                else a.CurFrame--;
            }
            else
            {
                if (a.CurFrame >= a.Frames.Count - 1) { if (a.Looped) a.CurFrame = 0; else a.Finished = true; }
                else a.CurFrame++;
            }
        }
        SetFrame(a.Frames[a.CurFrame]);
    }

    public void SetGraphicSize(float w = 0, float h = 0)
    {
        if (w <= 0 && h <= 0) return;
        float nx = w / FrameWidth, ny = h / FrameHeight;
        Scale = new Vector2(w <= 0 ? ny : nx, h <= 0 ? nx : ny);
    }

    public virtual void UpdateHitbox()
    {
        Width = Math.Abs(Scale.X) * FrameWidth; Height = Math.Abs(Scale.Y) * FrameHeight;
        Offset = new Vector2(-0.5f * (Width - FrameWidth), -0.5f * (Height - FrameHeight));
        Origin = new Vector2(FrameWidth * 0.5f, FrameHeight * 0.5f);
    }

    public void CenterOffsets() { Offset = new Vector2((FrameWidth - Width) * 0.5f, (FrameHeight - Height) * 0.5f); }

    /// <summary>flixel drawComplex with scrollFactor 0 on a zoom-1 camera; extraX/Y = parent group offset.</summary>
    public void Draw(SpriteBatch sb, float parentAlpha = 1f, float extraX = 0f, float extraY = 0f)
    {
        float a = Alpha * parentAlpha;
        if (!Visible || !Alive || a <= 0f) return;
        float px = X + extraX - Offset.X, py = Y + extraY - Offset.Y;
        if (Sheet == null)
        {
            if (Solid == null) return;
            float l = px + Origin.X - Origin.X * Scale.X, t = py + Origin.Y - Origin.Y * Scale.Y;
            sb.Draw(Solid, new Vector2(l, t), null, new Color(SolidColor, a), 0f, Vector2.Zero, new Vector2(FrameWidth * Scale.X, FrameHeight * Scale.Y), SpriteEffects.None, 0f);
            return;
        }
        var f = _frame; if (f == null) return;
        var tex = f.Texture ?? Sheet.Texture; if (tex == null) return;
        var src = f.SourceRect;
        int w = f.Rotated ? src.Height : src.Width, h = f.Rotated ? src.Width : src.Height;
        float lx = FlipX ? FrameWidth - f.Offset.X - w : f.Offset.X;
        float left = px + Origin.X + (lx - Origin.X) * Scale.X;
        float top = py + Origin.Y + (f.Offset.Y - Origin.Y) * Scale.Y;
        var col = new Color(Color, a);
        if (!f.Rotated)
            sb.Draw(tex, new Vector2(left, top), src, col, 0f, Vector2.Zero, Scale, FlipX ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
        else
        {
            // Sparrow rotated="true": stored 90 deg clockwise; draw rotated -90 deg about the rect's top-left.
            sb.Draw(tex, new Vector2(left, top + h * Scale.Y), src, col, -MathF.PI / 2f, Vector2.Zero, Scale,
                FlipX ? SpriteEffects.FlipVertically : SpriteEffects.None, 0f);
        }
    }
}
