using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine CoolUtil — utility functions.
/// Direct port of backend/CoolUtil.hx.
/// </summary>
public static class CoolUtil
{
    /// <summary>
    /// Quantize a float to a grid snap.
    /// </summary>
    public static float Quantize(float f, float snap)
    {
        float m = MathF.Round(f * snap);
        return m / snap;
    }

    /// <summary>
    /// Capitalize first letter, lowercase the rest.
    /// </summary>
    public static string Capitalize(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return char.ToUpper(text[0]) + text[1..].ToLower();
    }

    /// <summary>
    /// Read a text file and split into trimmed lines.
    /// </summary>
    public static List<string> CoolTextFile(string path)
    {
        if (!global::FNF_MonoGame.Engine.FsCI.FileExists(path)) return new List<string>();
        return ListFromString(global::FNF_MonoGame.Engine.FsCI.ReadAllText(path));
    }

    /// <summary>
    /// Parse a color from string (hex format).
    /// Supports: "FFFFFF", "#FFFFFF", "0xFFFFFFFF"
    /// </summary>
    public static Color ColorFromString(string color)
    {
        if (string.IsNullOrEmpty(color)) return Color.White;

        color = color.Trim().Replace("\t", "").Replace("\n", "").Replace("\r", "");

        if (color.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            color = color[^6..]; // last 6 chars
        color = color.TrimStart('#');

        if (uint.TryParse(color, System.Globalization.NumberStyles.HexNumber, null, out uint hex))
        {
            byte r = (byte)((hex >> 16) & 0xFF);
            byte g = (byte)((hex >> 8) & 0xFF);
            byte b = (byte)(hex & 0xFF);
            return new Color(r, g, b);
        }
        return Color.White;
    }

    /// <summary>
    /// Split string into trimmed lines.
    /// </summary>
    public static List<string> ListFromString(string str)
    {
        if (string.IsNullOrEmpty(str)) return new List<string>();
        return str.Trim()
            .Split('\n')
            .Select(s => s.Trim())
            .ToList();
    }

    /// <summary>
    /// Floor a decimal to N decimal places.
    /// </summary>
    public static float FloorDecimal(float value, int decimals)
    {
        if (decimals < 1)
            return MathF.Floor(value);
        float pow = MathF.Pow(10, decimals);
        return MathF.Floor(value * pow) / pow;
    }

    /// <summary>
    /// Create an array of integers from min to max (inclusive).
    /// </summary>
    public static List<int> NumberArray(int max, int min = 0)
    {
        var list = new List<int>(max - min + 1);
        for (int i = min; i <= max; i++)
            list.Add(i);
        return list;
    }
}
