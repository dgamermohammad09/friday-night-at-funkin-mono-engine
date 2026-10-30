using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Engine.PsychEngine.Objects;

/// <summary>
/// Psych Engine BGSprite — a simple background prop that can be static or animated.
/// Inherits StaticStageProp so it can be added to PlayScene through the mod stage pipeline.
/// Original: objects/BGSprite.hx
/// </summary>
public class BGSprite : FNF_MonoGame.Sprites.Sprite
{
    public new float X { get => base.X; set => base.X = value; }
    public new float Y { get => base.Y; set => base.Y = value; }
    public float ScrollX { get; set; } = 1f;
    public float ScrollY { get; set; } = 1f;
    public new bool Active { get; set; } = true;
    public bool Antialiasing { get; set; } = true;
    public new bool Visible { get => base.Visible; set => base.Visible = value; }
    public bool FlipX { get; set; }
    public bool FlipY { get; set; }
    public Color SpriteColor { get; set; } = Color.White;
    public new float Alpha { get => base.Alpha; set => base.Alpha = value; }
    public new float ScaleX { get => base.ScaleX; set => base.ScaleX = value; }
    public new float ScaleY { get => base.ScaleY; set => base.ScaleY = value; }

    public Vector2 Position { get => new Vector2(X, Y); set { X = value.X; Y = value.Y; } }
    public new Vector2 Scale { get => new Vector2(ScaleX, ScaleY); set { ScaleX = value.X; ScaleY = value.Y; } }
    public new Vector2 ScrollFactor { get => new Vector2(ScrollX, ScrollY); set { ScrollX = value.X; ScrollY = value.Y; } }
    public new Color Color { get => SpriteColor; set => SpriteColor = value; }

    public new Texture2D Texture { get => base.Texture; private set => base.Texture = value; }
    public SpriteSheet Sheet { get; private set; }
    public AnimatedSprite AnimatedSprite { get; private set; }
    public string CurrentAnimation => AnimatedSprite?.CurrentAnimation;

    public BGSpriteAnimation Animation { get; private set; }
    public BGSpriteAnimation Anim { get; private set; }

    public new float Width => base.width;
    public new float Height => base.height;

    // Velocity compatible with FlxSprite for stage movement (field allows .X/.Y mutation)
    public Vector2 Velocity = Vector2.Zero;

    private readonly List<string> _animations = new();
    private string _idleAnim;
    private bool _isAnimated;

    public BGSprite(string image, float x = 0, float y = 0, float scrollX = 1, float scrollY = 1,
        IReadOnlyList<string> animArray = null, bool loop = false)
    {
        X = x;
        Y = y;
        ScrollX = scrollX;
        ScrollY = scrollY;
        Antialiasing = ClientPrefs.Data.Antialiasing;
        Alpha = 1f;
        Animation = new BGSpriteAnimation(this);
        Anim = new BGSpriteAnimation(this);

        if (string.IsNullOrEmpty(image)) return;

        string path = PsychPaths.Image(image);
        if (string.IsNullOrEmpty(path)) return;

        string basePath = Path.ChangeExtension(path, null);
        var sheet = SpriteSheet.Load(FNFGame.Instance, basePath);
        if (sheet == null) return;

        Sheet = sheet;

        if (animArray != null && animArray.Count > 0)
        {
            _isAnimated = true;
            AnimatedSprite = new AnimatedSprite { Sheet = sheet };
            foreach (var anim in animArray)
            {
                _animations.Add(anim);
                if (_idleAnim == null)
                {
                    _idleAnim = anim;
                    AnimatedSprite.PlayAnimation(anim, true, loop);
                }
            }
        }
        else
        {
            Texture = sheet.Texture;
            SourceRect = new Rectangle(0, 0, Texture.Width, Texture.Height);
            Active = false;
        }
    }

    public void SetGraphicSize(int? width = null, int? height = null)
    {
        if (Texture == null) return;

        float originalWidth = Texture.Width;
        float originalHeight = Texture.Height;

        float newScaleX = width.HasValue ? width.Value / originalWidth : 1f;
        float newScaleY = height.HasValue ? height.Value / originalHeight : 1f;

        if (width.HasValue && !height.HasValue)
            newScaleY = newScaleX;
        else if (height.HasValue && !width.HasValue)
            newScaleX = newScaleY;

        ScaleX = newScaleX;
        ScaleY = newScaleY;
    }

    public void UpdateHitbox() { }

    public void MakeGraphic(int width, int height, Color color)
    {
        var gd = FNFGame.Instance.GraphicsDevice;
        var tex = new Texture2D(gd, width, height);
        var data = new Color[width * height];
        for (int i = 0; i < data.Length; i++) data[i] = color;
        tex.SetData(data);
        Texture = tex;
        SourceRect = new Rectangle(0, 0, width, height);
        _isAnimated = false;
        Active = false;
    }

    public virtual void Dance(bool forcePlay = false)
    {
        if (!string.IsNullOrEmpty(_idleAnim) && AnimatedSprite != null)
            AnimatedSprite.PlayAnimation(_idleAnim, forcePlay, AnimatedSprite.CurrentAnimation == _idleAnim || forcePlay);
    }

    public override void UpdateAnimation(float dt)
    {
        if (_isAnimated && Active)
            AnimatedSprite?.Update(dt);
        base.UpdateAnimation(dt);
    }

    public virtual void Update(float elapsed)
    {
        UpdateAnimation(elapsed);

        if (Velocity != Vector2.Zero)
        {
            X += Velocity.X * elapsed;
            Y += Velocity.Y * elapsed;
        }
    }

    public virtual void Kill()
    {
        Active = false;
        Visible = false;
    }
}

public class BGSpriteAnimation
{
    private readonly BGSprite _sprite;
    private readonly Dictionary<string, string> _prefixMap = new();

    public BGSpriteAnimation(BGSprite sprite)
    {
        _sprite = sprite;
        CurAnim = new BGSpriteAnimState(sprite);
    }

    public BGSpriteAnimState CurAnim { get; }
    public int Length => CurAnim.Length;
    public int CurFrame { get => CurAnim.CurFrame; set => CurAnim.CurFrame = value; }

    public event Action<string> OnComplete;
    public Action<string> FinishCallback;

    public void AddByPrefix(string name, string prefix, int framerate, bool looped)
    {
        _prefixMap[name] = prefix;
        if (_sprite.AnimatedSprite != null)
        {
            // Ensure the prefix can be resolved later
            _sprite.AnimatedSprite.PlayAnimation(prefix, true, looped);
            _sprite.AnimatedSprite.FrameRate = framerate;
        }
    }

    public void AddByIndices(string name, string prefix, int[] indices, string postfix, int framerate, bool looped)
    {
        // BGSprite uses exact animation names; fallback to prefix
        _prefixMap[name] = prefix;
    }

    public void Play(string name, bool restart = false, bool reversed = false, int frame = 0)
    {
        if (_sprite.AnimatedSprite == null) return;
        string anim = _prefixMap.TryGetValue(name, out var prefix) ? prefix : name;
        _sprite.AnimatedSprite.PlayAnimation(anim, restart, _sprite.AnimatedSprite.CurrentAnimation == anim || restart);
    }

    public void Finish()
    {
        if (_sprite.AnimatedSprite != null)
            _sprite.AnimatedSprite.Update(float.MaxValue);
    }

    internal void InvokeComplete(string name)
    {
        OnComplete?.Invoke(name);
        FinishCallback?.Invoke(name);
    }
}

public class BGSpriteAnimState
{
    private readonly BGSprite _sprite;

    public BGSpriteAnimState(BGSprite sprite)
    {
        _sprite = sprite;
    }

    public string Name => _sprite.AnimatedSprite?.CurrentAnimation ?? "";
    public bool Finished => _sprite.AnimatedSprite?.Finished ?? false;
    public int Length => _sprite.AnimatedSprite?.FrameCount ?? 0;

    public int CurFrame
    {
        get => _sprite.AnimatedSprite?.FrameIndex ?? 0;
        set { }
    }

    public IReadOnlyList<Rectangle> Frames => null;
}
