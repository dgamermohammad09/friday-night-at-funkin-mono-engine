using System.Collections.Generic;
using FNF_MonoGame.Engine.PsychEngine;

namespace FNF_MonoGame.Engine.PsychEngine.PsychLua;

/// <summary>
/// Psych Engine text Lua API entry point.
/// Wraps the existing LuaText implementation.
/// Original: psychlua/TextFunctions.hx
/// </summary>
public static class TextFunctions
{
    private static readonly Dictionary<string, LuaText> Texts = new(StringComparer.Ordinal);

    public static void Implement(FunkinLua funk)
    {
        var lua = funk.LuaScript;

        lua.Globals["makeLuaText"] = (System.Action<string, string, int, float, float>)((tag, text, width, x, y) =>
        {
            tag = tag?.Replace(".", "");
            if (Texts.ContainsKey(tag))
                Texts[tag] = null;
            Texts[tag] = new LuaText(tag, text, width, x, y);
        });

        lua.Globals["setTextString"] = (System.Func<string, string, bool>)((tag, text) =>
        {
            if (Texts.TryGetValue(tag, out var t)) { t.Text = text; return true; }
            return false;
        });

        lua.Globals["setTextSize"] = (System.Func<string, int, bool>)((tag, size) =>
        {
            if (Texts.TryGetValue(tag, out var t)) { t.Size = size; return true; }
            return false;
        });

        lua.Globals["setTextColor"] = (System.Func<string, string, bool>)((tag, color) =>
        {
            if (Texts.TryGetValue(tag, out var t)) { t.TextColor = CoolUtil.ColorFromString(color); return true; }
            return false;
        });

        lua.Globals["setTextBorder"] = (System.Func<string, float, string, bool>)((tag, size, color) =>
        {
            if (Texts.TryGetValue(tag, out var t)) { t.BorderSize = size; t.BorderColor = CoolUtil.ColorFromString(color); return true; }
            return false;
        });

        lua.Globals["getTextString"] = (System.Func<string, string>)(tag =>
        {
            if (Texts.TryGetValue(tag, out var t)) return t.Text;
            return null;
        });

        lua.Globals["addLuaText"] = (System.Action<string>)(tag =>
        {
            // Rendering is handled by PlayScene Lua overlay
        });

        lua.Globals["removeLuaText"] = (System.Action<string, bool>)((tag, destroy) =>
        {
            if (destroy && Texts.ContainsKey(tag))
                Texts.Remove(tag);
        });
    }

    public static LuaText GetText(string tag)
    {
        Texts.TryGetValue(tag, out var t);
        return t;
    }
}
