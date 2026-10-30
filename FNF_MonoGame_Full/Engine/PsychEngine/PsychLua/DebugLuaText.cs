using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Engine.PsychEngine.PsychLua;

/// <summary>
/// Psych Engine debug text overlay used by Lua scripts.
/// Original: psychlua/DebugLuaText.hx
/// </summary>
public class DebugLuaText
{
    public float DisableTime { get; set; } = 6f;

    public string Text { get; set; } = "";
    public float X { get; set; } = 10;
    public float Y { get; set; } = 10;
    public int Size { get; set; } = 16;
    public Color Color { get; set; } = Color.White;
    public float BorderSize { get; set; } = 1;
    public Color BorderColor { get; set; } = Color.Black;
    public float Alpha { get; set; } = 1f;
    public bool Visible { get; set; } = true;

    public FontStashSharp.SpriteFontBase Font { get; set; }

    public DebugLuaText()
    {
    }

    public void Update(float elapsed)
    {
        DisableTime -= elapsed;
        if (DisableTime < 0) DisableTime = 0;
        if (DisableTime < 1) Alpha = DisableTime;

        var viewport = FNFGame.Instance.GraphicsDevice.Viewport;
        if (Alpha <= 0 || Y >= viewport.Height)
            Visible = false;
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible || Font == null || Alpha <= 0 || string.IsNullOrEmpty(Text)) return;

        var color = Color * Alpha;
        var pos = new Vector2(X, Y);

        if (BorderSize > 0)
        {
            var border = BorderColor * Alpha;
            for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                    if (x != 0 || y != 0)
                        Font.DrawText(spriteBatch, Text, pos + new Vector2(x, y) * BorderSize, border);
        }

        Font.DrawText(spriteBatch, Text, pos, color);
    }
}
