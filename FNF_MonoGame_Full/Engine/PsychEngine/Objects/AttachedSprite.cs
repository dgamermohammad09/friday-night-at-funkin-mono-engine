using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Engine.PsychEngine.Objects;

/// <summary>
/// Psych Engine AttachedSprite — a sprite that follows another sprite.
/// Original: objects/AttachedSprite.hx
/// </summary>
public class AttachedSprite
{
    public ISpriteTracker SprTracker { get; set; }
    public float XAdd { get; set; }
    public float YAdd { get; set; }
    public float AngleAdd { get; set; }
    public float AlphaMult { get; set; } = 1f;
    public bool CopyAngle { get; set; } = true;
    public bool CopyAlpha { get; set; } = true;
    public bool CopyVisible { get; set; }

    public float X { get; set; }
    public float Y { get; set; }
    public float Angle { get; set; }
    public float Alpha { get; set; } = 1f;
    public bool Visible { get; set; } = true;
    public bool Antialiasing { get; set; } = true;

    public Texture2D Texture { get; private set; }
    public SpriteSheet Sheet { get; private set; }
    public Rectangle SourceRect { get; private set; }

    public AttachedSprite(string file = null, string anim = null, string parentFolder = null, bool loop = false)
    {
        Antialiasing = ClientPrefs.Data.Antialiasing;
        if (!string.IsNullOrEmpty(anim))
        {
            string path = PsychPaths.Image(file);
            if (!string.IsNullOrEmpty(path))
            {
                string basePath = System.IO.Path.ChangeExtension(path, null);
                Sheet = SpriteSheet.Load(FNFGame.Instance, basePath);
                Texture = Sheet?.Texture;
            }
        }
        else if (!string.IsNullOrEmpty(file))
        {
            string path = PsychPaths.Image(file);
            if (!string.IsNullOrEmpty(path))
            {
                string basePath = System.IO.Path.ChangeExtension(path, null);
                var sheet = SpriteSheet.Load(FNFGame.Instance, basePath);
                Texture = sheet?.Texture;
                Sheet = sheet;
                SourceRect = new Rectangle(0, 0, Texture?.Width ?? 0, Texture?.Height ?? 0);
            }
        }
    }

    public void Update(float elapsed)
    {
        if (SprTracker != null)
        {
            X = SprTracker.X + XAdd;
            Y = SprTracker.Y + YAdd;
            if (CopyAngle)
                Angle = SprTracker.Angle + AngleAdd;
            if (CopyAlpha)
                Alpha = SprTracker.Alpha * AlphaMult;
            if (CopyVisible)
                Visible = SprTracker.Visible;
        }
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible || Texture == null) return;
        spriteBatch.Draw(Texture, new Vector2(X, Y), SourceRect, Color.White * Alpha, Angle, Vector2.Zero, 1f, SpriteEffects.None, 0f);
    }
}

/// <summary>
/// Minimal tracker interface for AttachedSprite/AttachedText targets.
/// </summary>
public interface ISpriteTracker
{
    float X { get; }
    float Y { get; }
    float Angle { get; }
    float Alpha { get; }
    bool Visible { get; }
}
