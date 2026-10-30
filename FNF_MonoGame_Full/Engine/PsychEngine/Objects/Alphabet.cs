using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Engine.PsychEngine.Objects;

public enum Alignment
{
    Left,
    Centered,
    Right
}

/// <summary>
/// Psych Engine Alphabet text display.
/// Uses the existing AlphabetFont atlas system for exact original rendering.
/// Original: objects/Alphabet.hx
/// </summary>
public class Alphabet
{
    private string _text = "";
    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value ?? "";
            RebuildLetters();
            OnTextChanged();
        }
    }

    /// <summary>
    /// Hook called after <c>Text</c> is reassigned and <see cref="Letters"/> has been
    /// rebuilt. Used by <c>TypedAlphabet</c> to reset the typewriter reveal state.
    /// </summary>
    protected virtual void OnTextChanged() { }

    public bool Bold { get; set; }
    public bool IsMenuItem { get; set; }
    public int TargetY { get; set; }
    public int ID { get; set; }
    public bool ChangeX { get; set; } = true;
    public bool ChangeY { get; set; } = true;

    private Alignment _alignment;
    public Alignment Alignment
    {
        get => _alignment;
        set
        {
            _alignment = value;
            ApplyAlignment();
        }
    }

    private float _scaleX = 1f;
    public float ScaleX
    {
        get => _scaleX;
        set
        {
            float ratio = value / _scaleX;
            _scaleX = value;
            SoftReloadLetters(ratio, 1f);
        }
    }

    private float _scaleY = 1f;
    public float ScaleY
    {
        get => _scaleY;
        set
        {
            float ratio = value / _scaleY;
            _scaleY = value;
            SoftReloadLetters(1f, ratio);
        }
    }

    public int Rows { get; private set; }
    public Vector2 DistancePerItem { get; set; } = new(20, 120);
    public float DistancePerItemY { get => DistancePerItem.Y; set => DistancePerItem = new Vector2(DistancePerItem.X, value); }
    public Vector2 StartPosition { get; set; }

    public float X { get; set; }
    public float Y { get; set; }
    public float Alpha { get; set; } = 1f;
    public bool Visible { get; set; } = true;
    public float YBobTimer { get; set; }
    public bool YBobbing { get; set; }

    private readonly List<AlphaCharacter> _letters = new();
    private readonly List<float> _rowWidths = new();

    /// <summary>
    /// Internal letter list, exposed to subclasses (e.g. <c>TypedAlphabet</c>) that need
    /// to control per-character visibility for typewriter effects. Matches Psych's
    /// direct <c>letters</c> access on <c>Alphabet.hx</c>.
    /// </summary>
    protected internal List<AlphaCharacter> Letters => _letters;

    /// <summary>
    /// Total layout width of the widest row, in the current scale.
    /// Used by menus (e.g. OptionsState/BaseOptionsMenu) to position selector arrows.
    /// </summary>
    public float Width
    {
        get
        {
            float max = 0f;
            for (int i = 0; i < _rowWidths.Count; i++)
            {
                if (_rowWidths[i] > max) max = _rowWidths[i];
            }
            return max;
        }
    }

    private AlphabetFont Font => Bold ? AlphabetFont.Bold : AlphabetFont.Default;

    public Alphabet(float x, float y, string text = "", bool bold = true)
    {
        X = x;
        Y = y;
        StartPosition = new Vector2(x, y);
        Bold = bold;
        Text = text ?? "";
    }

    public void SetAlignmentFromString(string align)
    {
        Alignment = align?.Trim().ToLowerInvariant() switch
        {
            "right" => Alignment.Right,
            "center" or "centered" => Alignment.Centered,
            _ => Alignment.Left
        };
    }

    public void SetScale(float newX, float? newY = null)
    {
        float lastX = _scaleX;
        float lastY = _scaleY;
        _scaleX = newX;
        _scaleY = newY ?? newX;
        SoftReloadLetters(_scaleX / lastX, _scaleY / lastY);
    }

    public void SetText(string text, bool animate = false)
    {
        Text = text;
    }

    public void ScreenCenter(bool xAxis = true, bool yAxis = true)
    {
        var viewport = FNFGame.Instance.GraphicsDevice.Viewport;
        if (xAxis) X = viewport.Width / 2f - Width / 2f;
        if (yAxis) Y = viewport.Height / 2f - (Rows * 85 * _scaleY) / 2f;
    }

    public void SnapToPosition()
    {
        if (IsMenuItem)
        {
            if (ChangeX) X = TargetY * DistancePerItem.X + StartPosition.X;
            if (ChangeY) Y = TargetY * 1.3f * DistancePerItem.Y + StartPosition.Y;
        }
    }

    public virtual void Update(float elapsed)
    {
        if (IsMenuItem)
        {
            float lerpVal = (float)Math.Exp(-elapsed * 9.6);
            if (ChangeX) X = MathHelper.Lerp(TargetY * DistancePerItem.X + StartPosition.X, X, lerpVal);
            if (ChangeY) Y = MathHelper.Lerp(TargetY * 1.3f * DistancePerItem.Y + StartPosition.Y, Y, lerpVal);
        }

        if (YBobbing)
            YBobTimer += elapsed;
    }

    public virtual void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible || Font?.Texture == null) return;

        var color = Color.White * Alpha;
        float layoutScale = _scaleX;

        if (_letters.Count == 0)
        {
            Font.DrawString(spriteBatch, _text, new Vector2(X, Y), color, layoutScale, YBobbing, YBobTimer);
            return;
        }

        int charIndex = 0;
        foreach (var letter in _letters)
        {
            if (letter.Character == '\n')
            {
                charIndex = 0;
                continue;
            }

            // Per-letter visibility (typewriter effect via TypedAlphabet). Matches Psych's
            // `letters[i].visible = false` mechanism.
            if (!letter.Visible) { charIndex++; continue; }

            float yBob = 0f;
            if (YBobbing && letter.Character != ' ')
                yBob = MathF.Sin((YBobTimer * 3f) + charIndex * 0.5f) * 4f * layoutScale;

            var glyph = Font.GetGlyph(letter.Character);
            if (glyph != null)
            {
                float drawX = X + letter.X + glyph.Offset.X * layoutScale;
                float drawY = Y + letter.Y + yBob + glyph.Offset.Y * layoutScale;
                drawY += (Font.MaxHeight - glyph.FrameHeight) * layoutScale;

                var effects = SpriteEffects.None;
                spriteBatch.Draw(Font.Texture,
                    new Vector2(drawX, drawY),
                    glyph.SourceRect,
                    color,
                    0f,
                    Vector2.Zero,
                    layoutScale,
                    effects,
                    0f);
            }
            else if (letter.Character == ' ')
            {
                // nothing to draw
            }

            if (letter.Character != ' ')
                charIndex++;
        }
    }

    private void RebuildLetters()
    {
        _letters.Clear();
        _rowWidths.Clear();
        Rows = 0;

        string t = _text.Replace("\\n", "\n");
        float xPos = 0;
        float rowWidth = 0;
        int consecutiveSpaces = 0;

        for (int i = 0; i < t.Length; i++)
        {
            char c = t[i];
            if (c == '\n')
            {
                _rowWidths.Add(rowWidth);
                xPos = 0;
                rowWidth = 0;
                Rows++;
                _letters.Add(new AlphaCharacter { Character = '\n', Row = Rows - 1 });
                continue;
            }

            bool space = c == ' ' || (Bold && c == '_');
            if (space)
            {
                consecutiveSpaces++;
                continue;
            }

            if (consecutiveSpaces > 0)
            {
                xPos += 28 * consecutiveSpaces * _scaleX;
                rowWidth = xPos;
                consecutiveSpaces = 0;
            }

            var glyph = Font?.GetGlyph(c);
            float maxHeight = Font?.MaxHeight ?? 40;
            float charWidth = (glyph?.FrameWidth ?? 40) * _scaleX;
            float charHeight = (glyph?.FrameHeight ?? (int)maxHeight) * _scaleY;

            float offX = 2 * _scaleX;
            if (Bold) offX = 0;

            _letters.Add(new AlphaCharacter
            {
                Character = c,
                X = xPos,
                Y = Rows * 85 * _scaleY,
                Row = Rows,
                Width = charWidth,
                Height = charHeight
            });

            xPos += charWidth + offX;
            rowWidth = xPos;
        }

        if (_letters.Count > 0 && _letters[^1].Character != '\n')
            Rows++;

        if (rowWidth > 0)
            _rowWidths.Add(rowWidth);

        ApplyAlignment();
    }

    private void SoftReloadLetters(float ratioX, float ratioY)
    {
        foreach (var letter in _letters)
        {
            letter.X = letter.X * ratioX;
            letter.Y = letter.Y * ratioY;
            letter.Width *= ratioX;
            letter.Height *= ratioY;
        }
        ApplyAlignment();
    }

    private void ApplyAlignment()
    {
        if (_letters.Count == 0) return;

        for (int i = 0; i < _letters.Count; i++)
        {
            var letter = _letters[i];
            if (letter.Character == '\n') continue;

            float rowWidth = letter.Row < _rowWidths.Count ? _rowWidths[letter.Row] : 0;
            float alignOffset = Alignment switch
            {
                Alignment.Centered => rowWidth / 2f,
                Alignment.Right => rowWidth,
                _ => 0
            };

            letter.X -= letter.AlignOffset;
            letter.AlignOffset = alignOffset;
            letter.X += letter.AlignOffset;
        }
    }
}

/// <summary>
/// Single letter/symbol in an Alphabet text.
/// </summary>
public class AlphaCharacter
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float ScaleX { get; set; } = 1f;
    public float ScaleY { get; set; } = 1f;
    public char Character { get; set; } = '?';
    public int Row { get; set; }
    public float RowWidth { get; set; }
    public float AlignOffset { get; set; }
    public float[] LetterOffset { get; set; } = new float[2];

    /// <summary>
    /// Per-letter visibility flag. Used by <c>TypedAlphabet</c> for typewriter reveal.
    /// Matches the <c>visible</c> property of the FlxSprite each Psych letter is backed by.
    /// </summary>
    public bool Visible { get; set; } = true;

    public static bool IsTypeAlphabet(char c)
    {
        int ascii = c;
        return (ascii >= 65 && ascii <= 90)
            || (ascii >= 97 && ascii <= 122)
            || (ascii >= 192 && ascii <= 214)
            || (ascii >= 216 && ascii <= 246)
            || (ascii >= 248 && ascii <= 255);
    }
}
