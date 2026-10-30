using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Engine.PsychEngine.Objects;

/// <summary>
/// Psych Engine health icon component.
/// Original: objects/HealthIcon.hx
/// </summary>
public class HealthIcon
{
    public string Character { get; private set; } = "";
    public bool IsPlayer { get; private set; }
    public bool AutoAdjustOffset { get; set; } = true;
    public bool Antialiasing { get; set; } = true;

    public float X { get; set; }
    public float Y { get; set; }
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public float Alpha { get; set; } = 1f;
    public bool Visible { get; set; } = true;
    public float Scale { get; set; } = 1f;

    public Texture2D Texture { get; private set; }
    public SpriteSheet Sheet { get; private set; }
    public int FrameWidth { get; private set; }
    public int FrameHeight { get; private set; }
    public int FrameCount { get; private set; } = 1;

    public BGSprite SpriteTracker { get; set; }
    public int CurrentFrame { get; set; }

    private Vector2 _iconOffsets = Vector2.Zero;

    public HealthIcon(string character = "face", bool isPlayer = false)
    {
        IsPlayer = isPlayer;
        ChangeIcon(character);
    }

    public void ChangeIcon(string character)
    {
        if (Character == character) return;

        string name = "icons/" + character;
        string path = PsychPaths.Image(name);
        if (string.IsNullOrEmpty(path))
        {
            name = "icons/icon-" + character;
            path = PsychPaths.Image(name);
        }
        if (string.IsNullOrEmpty(path))
        {
            name = "icons/icon-face";
            path = PsychPaths.Image(name);
        }

        if (!string.IsNullOrEmpty(path))
        {
            string basePath = System.IO.Path.ChangeExtension(path, null);
            var sheet = SpriteSheet.Load(FNFGame.Instance, basePath);
            Sheet = sheet;
            Texture = sheet?.Texture;
            if (Texture != null)
            {
                FrameCount = Math.Max(1, (int)Math.Round((double)Texture.Width / Texture.Height));
                FrameWidth = Texture.Width / FrameCount;
                FrameHeight = Texture.Height;
                _iconOffsets.X = (FrameWidth - 150) / FrameCount;
                _iconOffsets.Y = (FrameHeight - 150) / FrameCount;
                UpdateHitbox();
            }
        }

        Character = character;
        Antialiasing = !character.EndsWith("-pixel") && ClientPrefs.Data.Antialiasing;
        CurrentFrame = 0;
    }

    public void Update(float elapsed)
    {
        if (Texture != null && AutoAdjustOffset)
        {
            OffsetX = _iconOffsets.X;
            OffsetY = _iconOffsets.Y;
        }

        if (SpriteTracker != null)
        {
            X = SpriteTracker.X + (SpriteTracker.Texture?.Width ?? 0) + 12;
            Y = SpriteTracker.Y - 30;
        }
    }

    public void UpdateHitbox()
    {
        if (AutoAdjustOffset)
        {
            OffsetX = _iconOffsets.X;
            OffsetY = _iconOffsets.Y;
        }
    }

    public void SetFrameFromHealth(float healthPercent)
    {
        // Player icon: normal when health high, losing when low.
        // Opponent icon: normal when health low, losing when high.
        bool losing = IsPlayer ? healthPercent < 0.2f : healthPercent > 0.8f;
        CurrentFrame = losing && FrameCount > 1 ? 1 : 0;
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible || Texture == null) return;

        var source = new Rectangle(CurrentFrame * FrameWidth, 0, FrameWidth, FrameHeight);
        var origin = new Vector2(OffsetX, OffsetY);
        var pos = new Vector2(X, Y);

        spriteBatch.Draw(
            Texture,
            pos,
            source,
            Color.White * Alpha,
            0f,
            origin,
            Scale,
            SpriteEffects.None,
            0f);
    }

    public string GetCharacter() => Character;
}
