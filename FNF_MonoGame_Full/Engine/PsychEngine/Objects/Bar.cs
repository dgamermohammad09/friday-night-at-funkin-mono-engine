using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Engine.PsychEngine.Objects;

/// <summary>
/// Psych Engine-style progress bar (health bar, time bar, etc.).
/// Original: objects/Bar.hx
/// </summary>
public class Bar
{
    public Func<float> ValueFunction { get; set; }
    public float Percent { get; private set; }
    public float MinBound { get; set; } = 0f;
    public float MaxBound { get; set; } = 1f;
    public bool LeftToRight { get; set; } = true;
    public float BarCenter { get; private set; }
    public bool Enabled { get; set; } = true;

    public int BarWidth { get; set; } = 1;
    public int BarHeight { get; set; } = 1;
    public Vector2 BarOffset { get; set; } = new(3, 3);

    public Texture2D BgTexture { get; private set; }
    public Texture2D LeftTexture { get; private set; }
    public Texture2D RightTexture { get; private set; }

    public Color LeftColor { get; set; } = Color.White;
    public Color RightColor { get; set; } = Color.Black;

    public float X { get; set; }
    public float Y { get; set; }
    public bool Visible { get; set; } = true;

    private int _bgWidth;
    private int _bgHeight;

    public Bar(float x, float y, string image = "healthBar", Func<float> valueFunction = null, float boundX = 0, float boundY = 1)
    {
        X = x;
        Y = y;
        ValueFunction = valueFunction;
        SetBounds(boundX, boundY);

        string path = PsychPaths.Image(image);
        if (!string.IsNullOrEmpty(path))
        {
            string basePath = System.IO.Path.ChangeExtension(path, null);
            var sheet = SpriteSheet.Load(FNFGame.Instance, basePath);
            BgTexture = sheet?.Texture;
        }

        if (BgTexture != null)
        {
            _bgWidth = BgTexture.Width;
            _bgHeight = BgTexture.Height;
            BarWidth = Math.Max(1, _bgWidth - 6);
            BarHeight = Math.Max(1, _bgHeight - 6);
        }

        LeftTexture = CreateSolidTexture(FNFGame.Instance.GraphicsDevice, LeftColor);
        RightTexture = CreateSolidTexture(FNFGame.Instance.GraphicsDevice, RightColor);

        RegenerateClips();
    }

    public void SetBounds(float min, float max)
    {
        MinBound = min;
        MaxBound = max;
    }

    public void SetColors(Color left, Color right)
    {
        LeftColor = left;
        RightColor = right;
        if (LeftTexture != null) LeftTexture.SetData(new[] { LeftColor });
        if (RightTexture != null) RightTexture.SetData(new[] { RightColor });
    }

    public void Update(float elapsed)
    {
        if (!Enabled) return;
        Percent = ValueFunction != null
            ? Math.Clamp((ValueFunction() - MinBound) / (MaxBound - MinBound), 0f, 1f) * 100f
            : 0f;
    }

    public void UpdateBar()
    {
        if (LeftTexture == null || RightTexture == null) return;
        float leftSize = LeftToRight
            ? MathHelper.Lerp(0, BarWidth, Percent / 100f)
            : MathHelper.Lerp(0, BarWidth, 1 - Percent / 100f);
        BarCenter = X + leftSize + BarOffset.X;
    }

    public void RegenerateClips()
    {
        UpdateBar();
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible) return;
        if (BgTexture != null)
            spriteBatch.Draw(BgTexture, new Vector2(X, Y), Color.White);

        float leftSize = LeftToRight
            ? MathHelper.Lerp(0, BarWidth, Percent / 100f)
            : MathHelper.Lerp(0, BarWidth, 1 - Percent / 100f);

        var leftRect = new Rectangle((int)(X + BarOffset.X), (int)(Y + BarOffset.Y), (int)leftSize, BarHeight);
        var rightRect = new Rectangle((int)(X + BarOffset.X + leftSize), (int)(Y + BarOffset.Y), BarWidth - (int)leftSize, BarHeight);

        if (LeftTexture != null)
            spriteBatch.Draw(LeftTexture, leftRect, Color.White);
        if (RightTexture != null)
            spriteBatch.Draw(RightTexture, rightRect, Color.White);
    }

    private static Texture2D CreateSolidTexture(GraphicsDevice graphics, Color color)
    {
        var tex = new Texture2D(graphics, 1, 1);
        tex.SetData(new[] { color });
        return tex;
    }
}
