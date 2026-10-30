using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Engine.PsychEngine.Objects;

/// <summary>
/// Psych Engine story mode menu character sprite.
/// Original: objects/MenuCharacter.hx
/// </summary>
public class MenuCharacter
{
    public float X { get; set; }
    public float Y { get; set; }
    public bool Visible { get; set; } = true;
    public bool Antialiasing { get; set; } = true;

    public string Character { get; private set; }
    public Texture2D Texture { get; private set; }
    public SpriteSheet Sheet { get; private set; }
    public Rectangle SourceRect { get; private set; }

    public bool IsConfirming { get; set; }
    public float Scale { get; set; } = 1f;

    public MenuCharacter(float x, float y, string character = "bf", float scale = 1f)
    {
        X = x;
        Y = y;
        Scale = scale;
        Character = character;
        LoadCharacter(character);
    }

    public void LoadCharacter(string character)
    {
        Character = character;
        string path = PsychPaths.Image($"storymenu/{character}");
        if (string.IsNullOrEmpty(path))
            path = PsychPaths.Image(character);

        if (!string.IsNullOrEmpty(path))
        {
            string basePath = System.IO.Path.ChangeExtension(path, null);
            Sheet = SpriteSheet.Load(FNFGame.Instance, basePath);
            Texture = Sheet?.Texture;
            SourceRect = new Rectangle(0, 0, Texture?.Width ?? 0, Texture?.Height ?? 0);
        }
        Antialiasing = !character.EndsWith("-pixel") && ClientPrefs.Data.Antialiasing;
    }

    public void PlayConfirm() => IsConfirming = true;

    public void Update(float elapsed)
    {
        IsConfirming = false;
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible || Texture == null) return;
        spriteBatch.Draw(Texture, new Vector2(X, Y), SourceRect, Color.White, 0f, Vector2.Zero, Scale, SpriteEffects.None, 0f);
    }
}
