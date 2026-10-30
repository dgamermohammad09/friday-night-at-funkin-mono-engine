using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FontStashSharp;

namespace FNF_MonoGame.Engine.PsychEngine.Objects;

/// <summary>
/// Psych Engine achievement notification popup.
/// Direct port of objects/AchievementPopup.hx.
///
/// Draws a black card (420x130) with a 100x100 achievement icon on the left
/// and the achievement name/description on the right. Slides down from off-screen
/// using an elastic-out curve, holds for 3 seconds, then linearly slides back up.
///
/// Managed by <see cref="Achievements"/>: <see cref="Achievements.StartPopup"/> creates
/// them, <see cref="Achievements.UpdatePopups"/> ticks them, <see cref="Achievements.DrawPopups"/>
/// renders them. Popups stack vertically — each new popup bumps existing ones down by 150 px.
/// </summary>
public class AchievementPopup
{
    // Card layout (matches Psych exactly)
    private const int CARD_W = 420;
    private const int CARD_H = 130;
    private const int ICON_SIZE = 100;
    private const int ICON_X = 15;
    private const int ICON_Y = 15;
    private const int TEXT_X = ICON_SIZE + ICON_X + 15;   // 130
    private const int TEXT_Y = ICON_Y + 20;               // 35
    private const int TEXT_MAX_W = 270;
    private const int DESC_OFFSET_Y = 30;
    private const int FONT_SIZE = 16;

    public Action OnFinish;

    public float X { get; set; } = 20f;
    public float Y { get; set; }
    /// <summary>Vertical resting position after slide-in. Bumped down by 150 when newer popups spawn.</summary>
    public float IntendedY { get; set; } = 20f;

    public string Name { get; private set; } = "Unknown";
    public string Description { get; private set; } = "Description not found";
    public Texture2D IconTexture { get; private set; }
    private bool _iconIsPixel; // matches hasAntialias=false path in Psych
    public string AchievementId { get; }
    public bool Destroyed { get; private set; }

    private float _lerpTime;
    private float _countedTime;

    public AchievementPopup(string achievementId, Action onFinish = null)
    {
        AchievementId = achievementId;
        OnFinish = onFinish;

        // Resolve achievement metadata + translated strings (matches AchievementPopup.hx L26-63)
        Achievements.Achievement ach = null;
        if (Achievements.AllAchievements.TryGetValue(achievementId, out var a) && a != null)
            ach = a;

        // MODS_ALLOWED path in Psych: switch currentModDirectory to the achievement's mod
        // so `Paths.image()` resolves from that mod. In our port, PsychPaths already
        // walks the enabled-mods list, so we simply resolve normally and let priority
        // rules do the work. (The achievement's `mod` field is preserved for debugging.)
        string image = $"achievements/{achievementId}";

        // Prefer pixel variant if present (matches Psych's fileExists check for `-pixel.png`)
        string pixelPath = PsychPaths.Image($"{image}-pixel");
        string mainPath;
        if (!string.IsNullOrEmpty(pixelPath))
        {
            mainPath = pixelPath;
            _iconIsPixel = true;
        }
        else
        {
            mainPath = PsychPaths.Image(image);
        }

        // Fallback to unknownMod (matches Psych's `if(graphic == null) graphic = Paths.image('unknownMod', false);`)
        if (string.IsNullOrEmpty(mainPath))
            mainPath = PsychPaths.Image("unknownMod");

        if (!string.IsNullOrEmpty(mainPath))
        {
            try
            {
                string basePath = System.IO.Path.ChangeExtension(mainPath, null);
                var sheet = SpriteSheet.Load(FNFGame.Instance, basePath);
                IconTexture = sheet?.Texture;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AchievementPopup] icon load failed for '{achievementId}': {ex.Message}");
            }
        }

        if (ach != null)
        {
            if (!string.IsNullOrEmpty(ach.Name))
                Name = Language.GetPhrase($"achievement_{achievementId}", ach.Name);
            if (!string.IsNullOrEmpty(ach.Description))
                Description = Language.GetPhrase($"description_{achievementId}", ach.Description);
        }

        // Start off-screen above the game area, matching Psych's `this.y = -130 * lastScale`.
        Y = -CARD_H;
    }

    /// <summary>
    /// Ticks the slide-in / hold / slide-out animation.
    /// Mirrors AchievementPopup.hx `update()` — elastic-out slide-in for 3 s,
    /// then linear slide-out at ~2000 px/s until off-screen.
    /// </summary>
    public void Update(float elapsed)
    {
        if (Destroyed) return;
        // Psych's guard: ignore very long frames (typical during state changes / loading)
        if (elapsed >= 0.5f) return;

        _countedTime += elapsed;
        if (_countedTime < 3f)
        {
            // Slide-in phase: elastic-out interp from y=-130 to y=IntendedY
            _lerpTime = Math.Min(1f, _lerpTime + elapsed);
            float eased = ElasticEaseOut(_lerpTime);
            Y = eased * (IntendedY + CARD_H) - CARD_H;
        }
        else
        {
            // Slide-out phase: linear at 2x screen height per second (Psych: `FlxG.height * 2 * elapsed`)
            Y -= FNFGame.SCREEN_HEIGHT * 2f * elapsed;
            if (Y <= -CARD_H)
                Destroy();
        }
    }

    /// <summary>
    /// Standard elastic-out easing (matches FlxEase.elasticOut used by Psych).
    /// FlxEase's formula: 1 + (2^-10*t) * sin(((t*1) - 0.075) * (2*PI) / 0.3)
    /// </summary>
    private static float ElasticEaseOut(float t)
    {
        if (t <= 0f) return 0f;
        if (t >= 1f) return 1f;
        const float p = 0.3f;
        const float s = p / 4f;
        return (float)(Math.Pow(2f, -10f * t) * Math.Sin((t - s) * (2f * Math.PI) / p) + 1f);
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        if (Destroyed) return;

        var assets = FNFGame.Instance?.Assets;
        var px = assets?.Pixel;
        if (px == null) return;

        int drawX = (int)X;
        int drawY = (int)Y;

        // Card background (Psych uses a rounded 16-px corner black fill; we render as
        // a flat rectangle which is visually near-identical at popup scale.)
        spriteBatch.Draw(px, new Rectangle(drawX, drawY, CARD_W, CARD_H), Color.Black);

        // Icon
        if (IconTexture != null)
        {
            var iconRect = new Rectangle(drawX + ICON_X, drawY + ICON_Y, ICON_SIZE, ICON_SIZE);
            spriteBatch.Draw(IconTexture, iconRect, Color.White);
        }
        else
        {
            // Placeholder if graphic missing (matches Psych fallback branch reaching unknownMod texture)
            var iconRect = new Rectangle(drawX + ICON_X, drawY + ICON_Y, ICON_SIZE, ICON_SIZE);
            spriteBatch.Draw(px, iconRect, new Color(60, 60, 60));
        }

        // Text: name + description via vcr.ttf (loaded by AssetManager at startup)
        var font = assets?.GetFont(FONT_SIZE);
        if (font != null)
        {
            DrawWrappedText(spriteBatch, font, Name,
                new Vector2(drawX + TEXT_X, drawY + TEXT_Y), TEXT_MAX_W, Color.White);
            DrawWrappedText(spriteBatch, font, Description,
                new Vector2(drawX + TEXT_X, drawY + TEXT_Y + DESC_OFFSET_Y), TEXT_MAX_W, Color.White);
        }
    }

    /// <summary>
    /// Draws text word-wrapped to <paramref name="maxWidth"/>. FontStashSharp's DrawText
    /// itself doesn't wrap, so we do a simple greedy word break — matches how FlxText's
    /// `fieldWidth` behaves in Psych for the popup's fixed 270 px column.
    /// </summary>
    private static void DrawWrappedText(SpriteBatch sb, SpriteFontBase font, string text,
        Vector2 position, float maxWidth, Color color)
    {
        if (string.IsNullOrEmpty(text)) return;

        var words = text.Split(' ');
        var line = new System.Text.StringBuilder();
        float y = position.Y;
        float lineHeight = font.LineHeight;

        foreach (var word in words)
        {
            string candidate = line.Length == 0 ? word : line + " " + word;
            var size = font.MeasureString(candidate);
            if (size.X > maxWidth && line.Length > 0)
            {
                font.DrawText(sb, line.ToString(), new Vector2(position.X, y), color);
                y += lineHeight;
                line.Clear();
                line.Append(word);
            }
            else
            {
                if (line.Length > 0) line.Append(' ');
                line.Append(word);
            }
        }
        if (line.Length > 0)
            font.DrawText(sb, line.ToString(), new Vector2(position.X, y), color);
    }

    public void Destroy()
    {
        if (Destroyed) return;
        Destroyed = true;
        OnFinish?.Invoke();
    }
}
