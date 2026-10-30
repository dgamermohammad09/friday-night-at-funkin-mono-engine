using SysType = System.Type;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MoonSharp.Interpreter;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine Lua utilities.
/// Direct port of psychlua/LuaUtils.hx — helpers for tweens, arrays, mod settings,
/// blend modes, and object lifecycle used by FunkinLua.
/// </summary>
public static class LuaUtils
{
    /// <summary>Parse a Psych/Flixel style key name (e.g. "SPACE", "ENTER", "A") into a MonoGame key.</summary>
    public static bool TryParseKey(string name, out Microsoft.Xna.Framework.Input.Keys key)
    {
        key = Microsoft.Xna.Framework.Input.Keys.None;
        if (string.IsNullOrEmpty(name)) return false;
        string n = name.Trim().ToUpperInvariant();
        switch (n)
        {
            case "SPACE": key = Microsoft.Xna.Framework.Input.Keys.Space; return true;
            case "ENTER" or "RETURN": key = Microsoft.Xna.Framework.Input.Keys.Enter; return true;
            case "ESCAPE" or "ESC": key = Microsoft.Xna.Framework.Input.Keys.Escape; return true;
            case "BACKSPACE": key = Microsoft.Xna.Framework.Input.Keys.Back; return true;
            case "TAB": key = Microsoft.Xna.Framework.Input.Keys.Tab; return true;
            case "SHIFT" or "LSHIFT": key = Microsoft.Xna.Framework.Input.Keys.LeftShift; return true;
            case "RSHIFT": key = Microsoft.Xna.Framework.Input.Keys.RightShift; return true;
            case "CONTROL" or "CTRL" or "LCTRL": key = Microsoft.Xna.Framework.Input.Keys.LeftControl; return true;
            case "RCTRL": key = Microsoft.Xna.Framework.Input.Keys.RightControl; return true;
            case "ALT" or "LALT": key = Microsoft.Xna.Framework.Input.Keys.LeftAlt; return true;
            case "RALT": key = Microsoft.Xna.Framework.Input.Keys.RightAlt; return true;
            case "LEFT": key = Microsoft.Xna.Framework.Input.Keys.Left; return true;
            case "RIGHT": key = Microsoft.Xna.Framework.Input.Keys.Right; return true;
            case "UP": key = Microsoft.Xna.Framework.Input.Keys.Up; return true;
            case "DOWN": key = Microsoft.Xna.Framework.Input.Keys.Down; return true;
            case "ZERO": key = Microsoft.Xna.Framework.Input.Keys.D0; return true;
            case "ONE": key = Microsoft.Xna.Framework.Input.Keys.D1; return true;
            case "TWO": key = Microsoft.Xna.Framework.Input.Keys.D2; return true;
            case "THREE": key = Microsoft.Xna.Framework.Input.Keys.D3; return true;
            case "FOUR": key = Microsoft.Xna.Framework.Input.Keys.D4; return true;
            case "FIVE": key = Microsoft.Xna.Framework.Input.Keys.D5; return true;
            case "SIX": key = Microsoft.Xna.Framework.Input.Keys.D6; return true;
            case "SEVEN": key = Microsoft.Xna.Framework.Input.Keys.D7; return true;
            case "EIGHT": key = Microsoft.Xna.Framework.Input.Keys.D8; return true;
            case "NINE": key = Microsoft.Xna.Framework.Input.Keys.D9; return true;
        }
        if (n.Length == 1 && n[0] >= '0' && n[0] <= '9')
            n = "D" + n;
        return Enum.TryParse(n, true, out key);
    }

    /// <summary>Parse a Psych/Flixel gamepad button name (e.g. "A", "START", "DPAD_LEFT") into a MonoGame button.</summary>
    public static bool TryParseButton(string name, out Microsoft.Xna.Framework.Input.Buttons button)
    {
        button = 0;
        if (string.IsNullOrEmpty(name)) return false;
        switch (name.Trim().ToUpperInvariant().Replace("_", ""))
        {
            case "A": button = Microsoft.Xna.Framework.Input.Buttons.A; return true;
            case "B": button = Microsoft.Xna.Framework.Input.Buttons.B; return true;
            case "X": button = Microsoft.Xna.Framework.Input.Buttons.X; return true;
            case "Y": button = Microsoft.Xna.Framework.Input.Buttons.Y; return true;
            case "START": button = Microsoft.Xna.Framework.Input.Buttons.Start; return true;
            case "BACK" or "SELECT": button = Microsoft.Xna.Framework.Input.Buttons.Back; return true;
            case "GUIDE": button = Microsoft.Xna.Framework.Input.Buttons.BigButton; return true;
            case "LEFTSHOULDER" or "LB": button = Microsoft.Xna.Framework.Input.Buttons.LeftShoulder; return true;
            case "RIGHTSHOULDER" or "RB": button = Microsoft.Xna.Framework.Input.Buttons.RightShoulder; return true;
            case "LEFTTRIGGER" or "LT": button = Microsoft.Xna.Framework.Input.Buttons.LeftTrigger; return true;
            case "RIGHTTRIGGER" or "RT": button = Microsoft.Xna.Framework.Input.Buttons.RightTrigger; return true;
            case "LEFTSTICKCLICK" or "LEFTSTICK" or "L3": button = Microsoft.Xna.Framework.Input.Buttons.LeftStick; return true;
            case "RIGHTSTICKCLICK" or "RIGHTSTICK" or "R3": button = Microsoft.Xna.Framework.Input.Buttons.RightStick; return true;
            case "DPADUP": button = Microsoft.Xna.Framework.Input.Buttons.DPadUp; return true;
            case "DPADDOWN": button = Microsoft.Xna.Framework.Input.Buttons.DPadDown; return true;
            case "DPADLEFT": button = Microsoft.Xna.Framework.Input.Buttons.DPadLeft; return true;
            case "DPADRIGHT": button = Microsoft.Xna.Framework.Input.Buttons.DPadRight; return true;
            default: return false;
        }
    }

    public static string TweenOptionToString(string option)
    {
        return option?.ToLower() switch
        {
            "Type" => "Type",
            "startdelay" => "startDelay",
            "loopdelay" => "loopDelay",
            "oncomplete" => "onComplete",
            "onstart" => "onStart",
            "onupdate" => "onUpdate",
            "onrepeat" => "onRepeat",
            "options" => "options",
            "ease" => "ease",
            _ => option
        };
    }

    public static object GetVariableFromArray(object parent, string[] path)
    {
        if (parent == null || path == null || path.Length == 0) return null;
        object current = parent;
        foreach (var name in path)
        {
            if (current is IDictionary<string, object> dict)
            {
                if (!dict.TryGetValue(name, out current)) return null;
            }
            else
            {
                var prop = current.GetType().GetProperty(name);
                if (prop == null) return null;
                current = prop.GetValue(current);
            }
        }
        return current;
    }

    public static bool SetVariableFromArray(object parent, string[] path, object value)
    {
        if (parent == null || path == null || path.Length == 0) return false;
        object current = parent;
        for (int i = 0; i < path.Length - 1; i++)
        {
            string name = path[i];
            if (current is IDictionary<string, object> dict)
            {
                if (!dict.TryGetValue(name, out current)) return false;
            }
            else
            {
                var prop = current.GetType().GetProperty(name);
                if (prop == null) return false;
                current = prop.GetValue(current);
            }
            if (current == null) return false;
        }

        string last = path[^1];
        if (current is IDictionary<string, object> d)
        {
            d[last] = value;
            return true;
        }
        else
        {
            var prop = current.GetType().GetProperty(last);
            if (prop == null || !prop.CanWrite) return false;
            try
            {
                prop.SetValue(current, Convert.ChangeType(value, prop.PropertyType));
                return true;
            }
            catch { return false; }
        }
    }

    /// <summary>LuaUtils.getModSetting(saveTag, modName): saved value from FlxG.save.data.modSettings[mod][save]; missing keys
    /// get the default 'value' of mods/&lt;mod&gt;/data/settings.json (an ARRAY of option definitions); keybind/key options
    /// default to {keyboard, gamepad}. No settings.json -> the mod's saved settings are dropped and null is returned.</summary>
    public static object GetModSetting(string modName, string settingName)
    {
        PsychModSaveData.EnsureLoaded();
        string modDir = PsychMods.GetModDirectory(modName);
        string path = modDir == null ? null : Path.Combine(modDir, "data", "settings.json");
        PsychModSaveData.Data.TryGetValue(modName ?? "", out var settings);
        if (path != null && global::FNF_MonoGame.Engine.FsCI.FileExists(path))
        {
            if (settings == null || !settings.ContainsKey(settingName ?? ""))
            {
                settings ??= new Dictionary<string, object>(StringComparer.Ordinal);
                try
                {
                    var arr = Newtonsoft.Json.Linq.JArray.Parse(PsychMods.SanitizeJson(global::FNF_MonoGame.Engine.FsCI.ReadAllText(path)));
                    foreach (var tok in arr)
                    {
                        if (tok is not Newtonsoft.Json.Linq.JObject sub) continue;
                        string save = sub["save"]?.ToString();
                        if (save == null || settings.ContainsKey(save)) continue;
                        string type = sub["type"]?.ToString();
                        if (type != "keybind" && type != "key")
                        {
                            var v = sub["value"];
                            if (v != null && v.Type != Newtonsoft.Json.Linq.JTokenType.Null) settings[save] = JTokenToPlain(v);
                        }
                        else
                            settings[save] = new Dictionary<string, object> { ["keyboard"] = sub["keyboard"]?.ToString() ?? "NONE", ["gamepad"] = sub["gamepad"]?.ToString() ?? "NONE" };
                    }
                    PsychModSaveData.Data[modName] = settings;
                }
                catch (Exception ex) { Console.WriteLine($"[MOD SETTINGS] Mod name: {modName} - An error occurred: {ex.Message}"); }
            }
        }
        else
        {
            PsychModSaveData.Data.Remove(modName ?? "");
            FunkinLua.LuaTrace($"getModSetting: {path ?? ("mods/" + modName + "/data/settings.json")} could not be found!", false, false);
            return null;
        }
        if (settings != null && settingName != null && settings.TryGetValue(settingName, out var val)) return val;
        FunkinLua.LuaTrace($"getModSetting: \"{settingName}\" could not be found inside {modName}'s settings!", true, false); // addTextToDebug RED
        return null;
    }

    internal static object JTokenToPlain(Newtonsoft.Json.Linq.JToken v)
    {
        switch (v.Type)
        {
            case Newtonsoft.Json.Linq.JTokenType.Boolean: return v.ToObject<bool>();
            case Newtonsoft.Json.Linq.JTokenType.Integer: return v.ToObject<double>();
            case Newtonsoft.Json.Linq.JTokenType.Float: return v.ToObject<double>();
            case Newtonsoft.Json.Linq.JTokenType.String: return v.ToObject<string>();
            default: return v.ToString();
        }
    }

    public static BlendState BlendModeFromString(string blend)
    {
        return blend?.ToLower() switch
        {
            "add" => BlendState.Additive,
            "alpha" => BlendState.AlphaBlend,
            "multiply" => BlendState.Opaque,
            "screen" => new BlendState
            {
                ColorSourceBlend = Blend.SourceColor,
                AlphaSourceBlend = Blend.SourceColor,
                ColorDestinationBlend = Blend.One,
                AlphaDestinationBlend = Blend.One
            },
            _ => BlendState.AlphaBlend
        };
    }

    public static EaseFunction EaseFromString(string ease)
    {
        return ease?.ToLower() switch
        {
            "linear" => EaseFunction.Linear,
            "quadin" or "easein" or "easeinquad" => EaseFunction.QuadIn,
            "quadout" or "easeout" or "easeoutquad" => EaseFunction.QuadOut,
            "quadinout" or "easeinout" or "easeinoutquad" => EaseFunction.QuadInOut,
            "cubein" or "easeincube" => EaseFunction.CubeIn,
            "cubeout" or "easeoutcube" => EaseFunction.CubeOut,
            "cubeinout" or "easeinoutcube" => EaseFunction.CubeInOut,
            "elasticin" => EaseFunction.ElasticIn,
            "elasticout" => EaseFunction.ElasticOut,
            "elasticinout" => EaseFunction.ElasticInOut,
            "bouncein" => EaseFunction.BounceIn,
            "bounceout" => EaseFunction.BounceOut,
            "bounceinout" => EaseFunction.BounceInOut,
            _ => EaseFunction.Linear
        };
    }

    public static int[] StringToIntArray(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return System.Array.Empty<int>();
        return input.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                    .SelectMany(p =>
                    {
                        if (p.Contains('-'))
                        {
                            var parts = p.Split('-');
                            if (parts.Length == 2 && int.TryParse(parts[0], out int start) && int.TryParse(parts[1], out int end))
                                return Enumerable.Range(start, end - start + 1);
                        }
                        if (int.TryParse(p, out int n)) return new[] { n };
                        return System.Array.Empty<int>();
                    }).ToArray();
    }

    public static Color ColorFromString(string color)
    {
        // CoolUtil.colorFromString: strip tabs/newlines, '0x...' -> last 6 digits (opaque RGB), then FlxColor.fromString
        // ('#RRGGBB', '0xAARRGGBB', 'RRGGBB' via '#', colour names); unknown -> WHITE. 6-digit values are opaque.
        if (string.IsNullOrWhiteSpace(color)) return Color.White;
        color = color.Replace("\t", "").Replace("\n", "").Replace("\r", "").Trim();
        if (color.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && color.Length >= 6) color = color.Substring(color.Length - 6);
        if (color.StartsWith("#")) color = color.Substring(1);
        if ((color.Length == 6 || color.Length == 8) && uint.TryParse(color, System.Globalization.NumberStyles.HexNumber, null, out uint val))
        {
            byte a = color.Length == 8 ? (byte)((val >> 24) & 0xFF) : (byte)255;
            return new Color((byte)((val >> 16) & 0xFF), (byte)((val >> 8) & 0xFF), (byte)(val & 0xFF), a);
        }
        switch (color.ToLowerInvariant())
        {
            case "black": return new Color(0, 0, 0); case "white": return Color.White; case "gray": return new Color(128, 128, 128);
            case "red": return new Color(255, 0, 0); case "green": return new Color(0, 128, 0); case "lime": return new Color(0, 255, 0);
            case "blue": return new Color(0, 0, 255); case "yellow": return new Color(255, 255, 0); case "orange": return new Color(255, 165, 0);
            case "purple": return new Color(128, 0, 128); case "pink": return new Color(255, 192, 203); case "brown": return new Color(139, 69, 19);
            case "cyan": return new Color(0, 255, 255); case "magenta": return new Color(255, 0, 255);
        }
        return Color.White;
    }

    public static Table MakeLuaTable(Script script, IEnumerable<object> values)
    {
        var table = new Table(script);
        int i = 1;
        foreach (var v in values)
            table[i++] = v;
        return table;
    }

    // ── Reflection helpers used by ReflectionFunctions ──

    public static object GetVarInArray(object instance, string variable, bool allowMaps = false)
    {
        if (string.IsNullOrEmpty(variable)) return null;
        var split = variable.Split('[');
        if (split.Length > 1)
        {
            object target = GetTarget(split[0], instance);
            for (int i = 1; i < split.Length; i++)
            {
                string idx = split[i].Substring(0, split[i].Length - 1);
                if (int.TryParse(idx, out int n) && target is System.Collections.IList list)
                {
                    if (i == split.Length - 1) return list[n];
                    target = list[n];
                }
                else if (target is IDictionary<object, object> dict)
                {
                    if (i == split.Length - 1) return dict[idx];
                    target = dict[idx];
                }
                else return null;
            }
            return target;
        }

        if (allowMaps && IsMap(instance))
        {
            if (instance is IDictionary<string, object> d && d.TryGetValue(variable, out var val)) return val;
            if (instance is IDictionary<object, object> d2 && d2.TryGetValue(variable, out var val2)) return val2;
            return null;
        }

        if (instance != null && MusicBeatState.Variables.TryGetValue(variable, out var stateVar) && instance == MusicBeatState.GetTargetInstance()) return stateVar;

        if (instance is FNF_MonoGame.Scenes.PlayScene.StaticStageProp sp && variable == "animation"
            && MusicBeatState.GetTargetInstance() is FNF_MonoGame.Scenes.PlayScene ps)
            return ps.PropAnimation(sp); // Lua sprite .animation.curAnim.curFrame / .finished
        if (instance is FNF_MonoGame.Scenes.PlayScene.StaticStageProp gp && variable == "graphic") return new FNF_MonoGame.Scenes.PsychPropGraphicView(gp);
        if (instance is FNF_MonoGame.Scenes.PlayScene.StaticStageProp fp && variable == "_frame") return new FNF_MonoGame.Scenes.PsychPropFrameView(fp);
        if (variable == "length") // Haxe Array/String .length
        {
            if (instance is System.Collections.ICollection col) return col.Count;
            if (instance is string str) return str.Length;
        }
        return GetProperty(instance, variable);
    }

    public static object SetVarInArray(object instance, string variable, object value, bool allowMaps = false)
    {
        if (string.IsNullOrEmpty(variable)) return value;
        var split = variable.Split('[');
        if (split.Length > 1)
        {
            object target = GetTarget(split[0], instance);
            for (int i = 1; i < split.Length; i++)
            {
                string idx = split[i].Substring(0, split[i].Length - 1);
                if (i == split.Length - 1)
                {
                    if (int.TryParse(idx, out int n) && target is System.Collections.IList list)
                        list[n] = value;
                    else if (target is IDictionary<object, object> dict)
                        dict[idx] = value;
                    return value;
                }
                else if (int.TryParse(idx, out int n) && target is System.Collections.IList list)
                    target = list[n];
                else if (target is IDictionary<object, object> dict)
                    target = dict[idx];
                else return value;
            }
            return value;
        }

        if (allowMaps && IsMap(instance))
        {
            if (instance is IDictionary<string, object> d) { d[variable] = value; return value; }
            if (instance is IDictionary<object, object> d2) { d2[variable] = value; return value; }
            return value;
        }

        if (instance is MusicBeatState) { MusicBeatState.Variables[variable] = value; return value; }

        SetProperty(instance, variable, value);
        return value;
    }

    public static object GetPropertyLoop(string[] split, bool targetInstance, bool allowMaps = false)
    {
        object instance = targetInstance ? MusicBeatState.GetTargetInstance() : null;
        object current = GetVarInArray(instance, split[0], allowMaps);
        for (int i = 1; i < split.Length - 1; i++)
            current = GetVarInArray(current, split[i], allowMaps);
        return current;
    }

    public static object GetGroupStuff(object obj, object variable, bool allowMaps = false)
    {
        if (obj == null) return null;
        if (variable is int idx)
        {
            if (obj is System.Collections.IList list) return list[idx];
            return null;
        }
        if (variable is string s) return GetVarInArray(obj, s, allowMaps);
        return null;
    }

    public static bool IsMap(object variable)
    {
        return variable is System.Collections.IDictionary;
    }

    private static object GetTarget(string name, object instance)
    {
        if (MusicBeatState.Variables.TryGetValue(name, out var val)) return val;
        return GetProperty(instance, name);
    }

    private static BindingFlags GetFlags(object instance) =>
        instance is SysType ? BindingFlags.Public | BindingFlags.Static : BindingFlags.Public | BindingFlags.Instance;

    // Psych/Haxe scripts use camelCase names (boyfriend, dad, defaultCamZoom, x) while the C# port
    // exposes PascalCase members (Boyfriend, Dad, DefaultCamZoom, X). Exact match first, then a
    // deterministic case-insensitive fallback, so Psych scripts resolve the same objects.
    private static PropertyInfo FindProp(SysType type, string name, BindingFlags flags)
    {
        var p = SafeGetProperty(type, name, flags);
        if (p != null) return p;
        foreach (var cand in type.GetProperties(flags))
            if (string.Equals(cand.Name, name, StringComparison.OrdinalIgnoreCase) && cand.GetIndexParameters().Length == 0)
                return cand;
        return null;
    }

    private static PropertyInfo SafeGetProperty(SysType type, string name, BindingFlags flags)
    {
        try { return type.GetProperty(name, flags); }
        catch (AmbiguousMatchException)
        {
            foreach (var cand in type.GetProperties(flags))
                if (cand.Name == name && cand.GetIndexParameters().Length == 0) return cand;
            return null;
        }
    }

    private static FieldInfo FindField(SysType type, string name, BindingFlags flags)
    {
        var f = type.GetField(name, flags);
        if (f != null) return f;
        foreach (var cand in type.GetFields(flags))
            if (string.Equals(cand.Name, name, StringComparison.OrdinalIgnoreCase))
                return cand;
        return null;
    }

    private static object GetProperty(object instance, string name)
    {
        if (instance == null) return null;
        SysType type = instance as SysType ?? instance.GetType();
        var flags = GetFlags(instance);
        var prop = FindProp(type, name, flags);
        if (prop != null) return prop.GetValue(instance is SysType ? null : instance);
        var field = FindField(type, name, flags);
        if (field != null) return field.GetValue(instance is SysType ? null : instance);
        return null;
    }

    /// <summary>Convert.ChangeType throws for objects that are not IConvertible even when the member type already
    /// accepts them (object-typed camera/cameras members), which made every object-valued setProperty a silent no-op.</summary>
    private static object ConvertForMember(object value, SysType t)
    {
        if (value == null || t.IsInstanceOfType(value)) return value;
        return Convert.ChangeType(value, t);
    }

    private static void SetProperty(object instance, string name, object value)
    {
        if (instance == null) return;
        SysType type = instance as SysType ?? instance.GetType();
        var flags = GetFlags(instance);
        var prop = FindProp(type, name, flags);
        if (prop != null && prop.CanWrite)
        {
            try { prop.SetValue(instance is SysType ? null : instance, ConvertForMember(value, prop.PropertyType)); } catch { }
            return;
        }
        var field = FindField(type, name, flags);
        if (field != null)
        {
            try { field.SetValue(instance is SysType ? null : instance, ConvertForMember(value, field.FieldType)); } catch { }
        }
    }
}

public enum EaseFunction
{
    Linear,
    QuadIn, QuadOut, QuadInOut,
    CubeIn, CubeOut, CubeInOut,
    ElasticIn, ElasticOut, ElasticInOut,
    BounceIn, BounceOut, BounceInOut
}

public enum TweenType
{
    Persist,
    Looping,
    OneShot,
    PingPong
}

