using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.IO;
using System.Globalization;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Represents a sprite extracted from a Psych Engine stage Lua/Hx script.
/// </summary>
public class PsychStageSprite
{
    public string Tag { get; set; }
    public string ImagePath { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float ScaleX { get; set; } = 1f;
    public float ScaleY { get; set; } = 1f;
    public float ScrollFactorX { get; set; } = 1f;
    public float ScrollFactorY { get; set; } = 1f;
    public float Alpha { get; set; } = 1f;
    public bool Visible { get; set; } = true;
    public bool InFront { get; set; } = false;
    public bool IsGraphic { get; set; } = false;
    public int GraphicWidth { get; set; }
    public int GraphicHeight { get; set; }
    public string GraphicColor { get; set; }
    public string Camera { get; set; } = "game";
    public int? SetWidth { get; set; }
    public int? SetHeight { get; set; }
}

/// <summary>
/// Parses Psych Engine stage Lua and Haxe scripts to extract sprite definitions
/// for rendering stage backgrounds. This is a static parser — it does not execute
/// the script logic, only extracts makeLuaSprite/FlxSprite calls and their properties.
/// </summary>
public static class PsychStageScriptParser
{
    /// <summary>
    /// Parse a Lua stage script and extract sprite definitions.
    /// </summary>
    public static List<PsychStageSprite> ParseLua(string scriptPath)
    {
        if (!global::FNF_MonoGame.Engine.FsCI.FileExists(scriptPath)) return new List<PsychStageSprite>();

        string content = global::FNF_MonoGame.Engine.FsCI.ReadAllText(scriptPath);
        var sprites = new Dictionary<string, PsychStageSprite>(StringComparer.OrdinalIgnoreCase);
        var addOrder = new List<string>();

        // Extract makeLuaSprite('tag', 'image', x, y)
        var makePattern = new Regex(
            @"makeLuaSprite\s*\(\s*['""](\w+)['""]\s*,\s*(?:['""]([^'""]*)['""]|nil)\s*,\s*(-?[\d.]+)\s*,\s*(-?[\d.]+)\s*\)",
            RegexOptions.IgnoreCase);

        foreach (Match m in makePattern.Matches(content))
        {
            string tag = m.Groups[1].Value;
            string image = m.Groups[2].Value;
            float x = ParseFloat(m.Groups[3].Value);
            float y = ParseFloat(m.Groups[4].Value);

            sprites[tag] = new PsychStageSprite
            {
                Tag = tag,
                ImagePath = string.IsNullOrEmpty(image) ? null : image,
                X = x,
                Y = y
            };
        }

        // Extract makeAnimatedLuaSprite('tag', 'image', x, y)
        var makeAnimPattern = new Regex(
            @"makeAnimatedLuaSprite\s*\(\s*['""](\w+)['""]\s*,\s*['""]([^'""]*)['""]?\s*,\s*(-?[\d.]+)\s*,\s*(-?[\d.]+)\s*\)",
            RegexOptions.IgnoreCase);

        foreach (Match m in makeAnimPattern.Matches(content))
        {
            string tag = m.Groups[1].Value;
            string image = m.Groups[2].Value;
            float x = ParseFloat(m.Groups[3].Value);
            float y = ParseFloat(m.Groups[4].Value);

            if (!sprites.ContainsKey(tag))
            {
                sprites[tag] = new PsychStageSprite
                {
                    Tag = tag,
                    ImagePath = string.IsNullOrEmpty(image) ? null : image,
                    X = x,
                    Y = y
                };
            }
        }

        // Extract makeGraphic('tag', width, height, 'color')
        var graphicPattern = new Regex(
            @"makeGraphic\s*\(\s*['""](\w+)['""]\s*,\s*(.+?)\s*,\s*(.+?)\s*,\s*['""]([0-9a-fA-F]+)['""]",
            RegexOptions.IgnoreCase);

        foreach (Match m in graphicPattern.Matches(content))
        {
            string tag = m.Groups[1].Value;
            if (sprites.TryGetValue(tag, out var sprite))
            {
                sprite.IsGraphic = true;
                sprite.GraphicWidth = (int)EvalSimpleExpr(m.Groups[2].Value);
                sprite.GraphicHeight = (int)EvalSimpleExpr(m.Groups[3].Value);
                sprite.GraphicColor = m.Groups[4].Value;
            }
        }

        // Extract addLuaSprite('tag', true/false)
        var addPattern = new Regex(
            @"addLuaSprite\s*\(\s*['""](\w+)['""]\s*(?:,\s*(true|false))?\s*\)",
            RegexOptions.IgnoreCase);

        foreach (Match m in addPattern.Matches(content))
        {
            string tag = m.Groups[1].Value;
            bool inFront = m.Groups[2].Success && m.Groups[2].Value.Equals("true", StringComparison.OrdinalIgnoreCase);

            if (sprites.TryGetValue(tag, out var sprite))
            {
                sprite.InFront = inFront;
                if (!addOrder.Contains(tag))
                    addOrder.Add(tag);
            }
        }

        // Extract scaleObject('tag', scaleX, scaleY)
        var scalePattern = new Regex(
            @"scaleObject\s*\(\s*['""](\w+)['""]\s*,\s*(-?[\d.]+)\s*,\s*(-?[\d.]+)",
            RegexOptions.IgnoreCase);

        foreach (Match m in scalePattern.Matches(content))
        {
            string tag = m.Groups[1].Value;
            if (sprites.TryGetValue(tag, out var sprite))
            {
                sprite.ScaleX = ParseFloat(m.Groups[2].Value);
                sprite.ScaleY = ParseFloat(m.Groups[3].Value);
            }
        }

        // Extract setGraphicSize('tag', w, h)
        var sizePattern = new Regex(
            @"setGraphicSize\s*\(\s*['""](\w+)['""]\s*,\s*(.+?)\s*,\s*(.+?)\s*(?:,\s*(?:true|false))?\s*\)",
            RegexOptions.IgnoreCase);

        foreach (Match m in sizePattern.Matches(content))
        {
            string tag = m.Groups[1].Value;
            if (sprites.TryGetValue(tag, out var sprite))
            {
                sprite.SetWidth = (int)EvalSimpleExpr(m.Groups[2].Value);
                sprite.SetHeight = (int)EvalSimpleExpr(m.Groups[3].Value);
            }
        }

        // Extract setScrollFactor('tag', scrollX, scrollY)
        var scrollPattern = new Regex(
            @"setScrollFactor\s*\(\s*['""](\w+)['""]\s*,\s*(-?[\d.]+)\s*,\s*(-?[\d.]+)\s*\)",
            RegexOptions.IgnoreCase);

        foreach (Match m in scrollPattern.Matches(content))
        {
            string tag = m.Groups[1].Value;
            if (sprites.TryGetValue(tag, out var sprite))
            {
                sprite.ScrollFactorX = ParseFloat(m.Groups[2].Value);
                sprite.ScrollFactorY = ParseFloat(m.Groups[3].Value);
            }
        }

        // Extract setProperty('tag.alpha', value)
        var alphaPattern = new Regex(
            @"setProperty\s*\(\s*['""](\w+)\.alpha['""]\s*,\s*(-?[\d.]+)\s*\)",
            RegexOptions.IgnoreCase);

        foreach (Match m in alphaPattern.Matches(content))
        {
            string tag = m.Groups[1].Value;
            if (sprites.TryGetValue(tag, out var sprite))
            {
                sprite.Alpha = ParseFloat(m.Groups[2].Value);
            }
        }

        // Extract setProperty('tag.visible', false)
        var visiblePattern = new Regex(
            @"setProperty\s*\(\s*['""](\w+)\.visible['""]\s*,\s*(true|false)\s*\)",
            RegexOptions.IgnoreCase);

        foreach (Match m in visiblePattern.Matches(content))
        {
            string tag = m.Groups[1].Value;
            if (sprites.TryGetValue(tag, out var sprite))
            {
                sprite.Visible = m.Groups[2].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
        }

        // Extract setObjectCamera('tag', 'camera')
        var camPattern = new Regex(
            @"setObjectCamera\s*\(\s*['""](\w+)['""]\s*,\s*['""](\w+)['""]\s*\)",
            RegexOptions.IgnoreCase);

        foreach (Match m in camPattern.Matches(content))
        {
            string tag = m.Groups[1].Value;
            if (sprites.TryGetValue(tag, out var sprite))
            {
                sprite.Camera = m.Groups[2].Value;
            }
        }

        // Build result in add order, only sprites that were actually added
        var result = new List<PsychStageSprite>();
        foreach (var tag in addOrder)
        {
            if (sprites.TryGetValue(tag, out var sprite))
            {
                // Only include game camera sprites with actual images or graphics
                if (sprite.Camera.Equals("game", StringComparison.OrdinalIgnoreCase)
                    && (sprite.ImagePath != null || sprite.IsGraphic))
                {
                    result.Add(sprite);
                }
            }
        }

        Console.WriteLine($"[STAGE-LUA] Parsed {result.Count} sprites from {Path.GetFileName(scriptPath)}");
        return result;
    }

    /// <summary>
    /// Parse a Haxe stage script and extract sprite definitions.
    /// </summary>
    public static List<PsychStageSprite> ParseHaxe(string scriptPath)
    {
        if (!global::FNF_MonoGame.Engine.FsCI.FileExists(scriptPath)) return new List<PsychStageSprite>();

        string content = global::FNF_MonoGame.Engine.FsCI.ReadAllText(scriptPath);
        var sprites = new Dictionary<string, PsychStageSprite>(StringComparer.OrdinalIgnoreCase);
        var addOrder = new List<string>();

        // Resolve variable assignments: var PREFIX:String = 'dragon_ball/';
        var varAssignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var varPattern = new Regex(@"var\s+(\w+)\s*(?::\s*\w+)?\s*=\s*['""]([^'""]*)['""]");
        foreach (Match m in varPattern.Matches(content))
        {
            varAssignments[m.Groups[1].Value] = m.Groups[2].Value;
        }

        // Extract: varName = new FlxSprite(x, y).loadGraphic(Paths.image(...))
        var flxPattern = new Regex(
            @"(\w+)\s*=\s*new\s+FlxSprite\s*\(\s*(-?[\d.]+)\s*,\s*(-?[\d.]+)\s*\)\s*\.loadGraphic\s*\(\s*Paths\.image\s*\(\s*(.+?)\s*\)\s*\)");

        foreach (Match m in flxPattern.Matches(content))
        {
            string varName = m.Groups[1].Value;
            float x = ParseFloat(m.Groups[2].Value);
            float y = ParseFloat(m.Groups[3].Value);
            string imageExpr = m.Groups[4].Value;
            string imagePath = ResolveHaxeStringExpr(imageExpr, varAssignments);

            sprites[varName] = new PsychStageSprite
            {
                Tag = varName,
                ImagePath = imagePath,
                X = x,
                Y = y
            };
        }

        // Extract setGraphicSize: varName.setGraphicSize(w, h)
        var sizePattern = new Regex(@"(\w+)\.setGraphicSize\s*\(\s*(.+?)\s*,\s*(.+?)\s*\)");
        foreach (Match m in sizePattern.Matches(content))
        {
            string varName = m.Groups[1].Value;
            if (sprites.TryGetValue(varName, out var sprite))
            {
                sprite.SetWidth = (int)EvalSimpleExpr(m.Groups[2].Value);
                sprite.SetHeight = (int)EvalSimpleExpr(m.Groups[3].Value);
            }
        }

        // Extract scrollFactor.set: varName.scrollFactor.set(sx, sy)
        var scrollPattern = new Regex(@"(\w+)\.scrollFactor\.set\s*\(\s*(-?[\d.]+)\s*,\s*(-?[\d.]+)\s*\)");
        foreach (Match m in scrollPattern.Matches(content))
        {
            string varName = m.Groups[1].Value;
            if (sprites.TryGetValue(varName, out var sprite))
            {
                sprite.ScrollFactorX = ParseFloat(m.Groups[2].Value);
                sprite.ScrollFactorY = ParseFloat(m.Groups[3].Value);
            }
        }

        // Extract alpha: varName.alpha = value
        var alphaPattern = new Regex(@"(\w+)\.alpha\s*=\s*(-?[\d.]+)");
        foreach (Match m in alphaPattern.Matches(content))
        {
            string varName = m.Groups[1].Value;
            if (sprites.TryGetValue(varName, out var sprite))
            {
                sprite.Alpha = ParseFloat(m.Groups[2].Value);
            }
        }

        // Extract cameras: varName.cameras = [camHUD] / [camOther]
        var camPattern = new Regex(@"(\w+)\.cameras\s*=\s*\[\s*(\w+)\s*\]");
        foreach (Match m in camPattern.Matches(content))
        {
            string varName = m.Groups[1].Value;
            string cam = m.Groups[2].Value;
            if (sprites.TryGetValue(varName, out var sprite))
            {
                sprite.Camera = cam.Contains("HUD", StringComparison.OrdinalIgnoreCase) ? "hud"
                    : cam.Contains("Other", StringComparison.OrdinalIgnoreCase) ? "other"
                    : "game";
            }
        }

        // Extract addBehindGF/addBehindDad/add: addBehindGF(varName) or add(varName)
        var addBehindPattern = new Regex(@"(?:addBehindGF|addBehindDad|addBehindBF|game\.add|add)\s*\(\s*(\w+)\s*\)");
        foreach (Match m in addBehindPattern.Matches(content))
        {
            string varName = m.Groups[1].Value;
            if (sprites.ContainsKey(varName) && !addOrder.Contains(varName))
                addOrder.Add(varName);
        }

        // Build result
        var result = new List<PsychStageSprite>();
        foreach (var tag in addOrder)
        {
            if (sprites.TryGetValue(tag, out var sprite))
            {
                if (sprite.Camera.Equals("game", StringComparison.OrdinalIgnoreCase)
                    && sprite.ImagePath != null)
                {
                    result.Add(sprite);
                }
            }
        }

        Console.WriteLine($"[STAGE-HX] Parsed {result.Count} sprites from {Path.GetFileName(scriptPath)}");
        return result;
    }

    /// <summary>
    /// Resolve a Haxe string concatenation expression, e.g. PREFIX + 'sky'
    /// </summary>
    private static string ResolveHaxeStringExpr(string expr, Dictionary<string, string> vars)
    {
        // Remove outer quotes if it's a simple string
        expr = expr.Trim();

        // Split on + for concatenation
        var parts = expr.Split('+');
        var result = "";
        foreach (var part in parts)
        {
            string p = part.Trim().Trim('\'', '"');
            // Check if it's a variable reference
            if (vars.TryGetValue(p, out var val))
                result += val;
            else
                result += p;
        }
        return result;
    }

    /// <summary>
    /// Evaluate simple arithmetic expressions like "1000 * 2", "screenWidth * 3.5"
    /// </summary>
    private static float EvalSimpleExpr(string expr)
    {
        expr = expr.Trim();

        // Replace known constants
        expr = expr.Replace("screenWidth", "1280", StringComparison.OrdinalIgnoreCase);
        expr = expr.Replace("screenHeight", "720", StringComparison.OrdinalIgnoreCase);
        expr = expr.Replace("FlxG.width", "1280", StringComparison.OrdinalIgnoreCase);
        expr = expr.Replace("FlxG.height", "720", StringComparison.OrdinalIgnoreCase);

        // Handle simple multiplication: "number * number"
        var mulMatch = Regex.Match(expr, @"(-?[\d.]+)\s*\*\s*(-?[\d.]+)");
        if (mulMatch.Success)
        {
            float a = ParseFloat(mulMatch.Groups[1].Value);
            float b = ParseFloat(mulMatch.Groups[2].Value);
            return a * b;
        }

        // Try parse as plain number
        if (float.TryParse(expr, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
            return val;

        return 0f;
    }

    private static float ParseFloat(string s)
    {
        if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
            return val;
        return 0f;
    }
}
