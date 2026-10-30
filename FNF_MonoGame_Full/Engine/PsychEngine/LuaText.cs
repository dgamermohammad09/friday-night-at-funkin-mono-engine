using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine LuaText — text object created by Lua scripts via makeLuaText.
/// Direct port of psychlua/TextFunctions.hx text handling.
/// </summary>
public class LuaText
{
    public string Tag { get; set; } = "";
    public string Text { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float FieldWidth { get; set; }
    public float FieldHeight { get; set; }
    public int Size { get; set; } = 16;
    public Color TextColor { get; set; } = Color.White;
    public Color BorderColor { get; set; } = Color.Black;
    public float BorderSize { get; set; } = 2;
    public string BorderStyle { get; set; } = "outline"; // none, outline, shadow
    public string Alignment { get; set; } = "center"; // left, center, right
    public float Alpha { get; set; } = 1f;
    public bool Visible { get; set; } = true;
    public float ScrollFactorX { get; set; } = 0f;
    public float ScrollFactorY { get; set; } = 0f;
    public string Camera { get; set; } = "hud";
    public bool AutoSize { get; set; } = true;
    public string FontName { get; set; } = "vcr";
    public bool Italic { get; set; }

    public FontStashSharp.SpriteFontBase Font { get; set; }

    // Psych Engine property names are lowercase — expose aliases so getProperty/setProperty work.
    public float x { get => X; set => X = value; }
    public float y { get => Y; set => Y = value; }
    public float alpha { get => Alpha; set => Alpha = value; }
    public bool visible { get => Visible; set => Visible = value; }
    public float width => Width;
    public float height => Height;
    public int size { get => Size; set => Size = value; }
    public string text { get => Text; set => Text = value; }
    public string alignment { get => Alignment; set => Alignment = value; }
    public string camera { get => Camera; set => Camera = value; }
    public float borderSize { get => BorderSize; set => BorderSize = value; }
    public string borderStyle { get => BorderStyle; set => BorderStyle = value; }
    public float fieldWidth { get => FieldWidth; set => FieldWidth = value; }
    public float fieldHeight { get => FieldHeight; set => FieldHeight = value; }
    public float scrollX { get => ScrollFactorX; set => ScrollFactorX = value; }
    public float scrollY { get => ScrollFactorY; set => ScrollFactorY = value; }

    public LuaText(string tag, string text, float width, float x, float y)
    {
        Tag = tag;
        Text = text ?? "";
        FieldWidth = width;
        X = x;
        Y = y;
    }

    /// <summary>flixel FlxText: width = fieldWidth (when set) else textWidth + 4; height = lines * lineHeight + 4
    /// (VCR whole-pixel advance round(size*1200/2048), line ceil(size*0.9): 16 -> 19 px like native).</summary>
    public float Width => FieldWidth > 0 ? FieldWidth : MaxLineChars() * (float)System.Math.Round(Size * 1200.0 / 2048.0) + 4f;
    public float Height => (FieldHeight > 0 && !AutoSize) ? FieldHeight : LineCount() * (float)System.Math.Ceiling(Size * 0.9) + 4f;
    private int LineCount() { if (string.IsNullOrEmpty(Text)) return 1; int n = 1; foreach (char c in Text) if (c == '\n') n++; return n; }
    private int MaxLineChars() { int m = 0; foreach (var l in (Text ?? "").Split('\n')) m = System.Math.Max(m, l.Length); return m; }

    public float getWidth() => Width;
    public float getHeight() => Height;

    /// <summary>
    /// Draw the text object. Uses the loaded SpriteFontBase.
    /// </summary>
    public void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible || Alpha <= 0 || Font == null || string.IsNullOrEmpty(Text)) return;

        var pos = new Vector2(X, Y);
        Color color = TextColor * Alpha;

        // Draw border
        if (BorderSize > 0 && BorderStyle != "none")
        {
            var borderColor = BorderColor * Alpha;
            if (BorderStyle == "outline")
            {
                for (int ox = -1; ox <= 1; ox++)
                    for (int oy = -1; oy <= 1; oy++)
                        if (ox != 0 || oy != 0)
                            Font.DrawText(spriteBatch, Text,
                                pos + new Vector2(ox * BorderSize, oy * BorderSize), borderColor);
            }
            else if (BorderStyle == "shadow")
            {
                Font.DrawText(spriteBatch, Text,
                    pos + new Vector2(BorderSize, BorderSize), borderColor);
            }
        }

        // Draw main text
        Font.DrawText(spriteBatch, Text, pos, color);
    }
}
