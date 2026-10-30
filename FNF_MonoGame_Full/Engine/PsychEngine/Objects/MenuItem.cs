using Microsoft.Xna.Framework;

namespace FNF_MonoGame.Engine.PsychEngine.Objects;

/// <summary>
/// Psych Engine menu item wrapper (typically an Alphabet-based menu entry).
/// Original: objects/MenuItem.hx
/// </summary>
public class MenuItem
{
    public float X { get; set; }
    public float Y { get; set; }
    public int TargetY { get; set; }
    public bool Visible { get; set; } = true;

    public string Text { get; set; }
    public bool IsMenuItem { get; set; } = true;

    private readonly Vector2 _startPosition;

    public MenuItem(float x, float y, string text = "")
    {
        X = x;
        Y = y;
        _startPosition = new Vector2(x, y);
        Text = text;
    }

    public void SnapToPosition()
    {
        X = TargetY * 20 + _startPosition.X;
        Y = TargetY * 1.3f * 120 + _startPosition.Y;
    }

    public void Update(float elapsed)
    {
        if (IsMenuItem)
        {
            float lerpVal = (float)Math.Exp(-elapsed * 9.6);
            X = MathHelper.Lerp(TargetY * 20 + _startPosition.X, X, lerpVal);
            Y = MathHelper.Lerp(TargetY * 1.3f * 120 + _startPosition.Y, Y, lerpVal);
        }
    }
}
