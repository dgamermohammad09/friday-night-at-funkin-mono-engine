using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MoonSharp.Interpreter;
using SysType = System.Type;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine reflection-based Lua functions.
/// Direct port of psychlua/ReflectionFunctions.hx — exposes dynamic property and
/// method access to Lua mods. Uses a small static registry for Xbox-compatible
/// class lookup instead of full reflection resolution.
/// </summary>
public static class ReflectionFunctions
{
    private static readonly Dictionary<string, object> ClassRegistry = new(StringComparer.OrdinalIgnoreCase);

    static ReflectionFunctions()
    {
        RegisterClass("ClientPrefs", typeof(ClientPrefs));
        RegisterClass("PsychHighscore", typeof(PsychHighscore));
        RegisterClass("PsychGlobals", typeof(PsychGlobals));
        RegisterClass("PsychMods", typeof(PsychMods));
        RegisterClass("PsychPaths", typeof(PsychPaths));
        RegisterClass("CoolUtil", typeof(CoolUtil));
        RegisterClass("Controls", typeof(Controls));
        RegisterClass("PsychCamera", typeof(PsychCamera));
        RegisterClass("MusicBeatState", typeof(MusicBeatState));
        RegisterClass("Difficulty", typeof(Difficulty));

        RegisterClass("backend.ClientPrefs", typeof(ClientPrefs));
        RegisterClass("backend.Conductor", typeof(PsychConductor));
        RegisterClass("Conductor", typeof(PsychConductor));
        RegisterClass("backend.Difficulty", typeof(Difficulty));
        RegisterClass("backend.CoolUtil", typeof(CoolUtil));
        RegisterClass("backend.Paths", typeof(PsychPaths));
        RegisterClass("Paths", typeof(PsychPaths));
        RegisterClass("backend.Mods", typeof(PsychMods));
        RegisterClass("Mods", typeof(PsychMods));
        RegisterClass("flixel.math.FlxMath", typeof(FNF_MonoGame.Scenes.HxFlxMath));
        RegisterClass("FlxMath", typeof(FNF_MonoGame.Scenes.HxFlxMath));
        // flixel classes used through createInstance/callMethodFromClass (e.g. extra FlxCameras + FlxG.cameras.add)
        RegisterClass("flixel.FlxCamera", typeof(FNF_MonoGame.Scenes.HxFlxCamera));
        RegisterClass("FlxCamera", typeof(FNF_MonoGame.Scenes.HxFlxCamera));
        RegisterClass("flixel.FlxSprite", typeof(FNF_MonoGame.Scenes.HxFlxSprite));
        RegisterClass("flixel.FlxG", typeof(FNF_MonoGame.Scenes.HxFlxG));
        RegisterClass("FlxG", typeof(FNF_MonoGame.Scenes.HxFlxG));
        RegisterClass("Main", new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["fpsVar"] = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["currentFPS"] = 60,
                ["visible"] = true
            }
        });
        RegisterClass("openfl.system.System", new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["totalMemory"] = 0
        });
    }

    public static void RegisterClass(string name, object instance)
    {
        ClassRegistry[name] = instance;
    }

    public static object GetClassInstance(string className)
    {
        if (ClassRegistry.TryGetValue(className, out var registered)) return registered;
        return ResolveType(className);
    }

    public static bool SetClassProperty(string className, string variable, object value)
    {
        if (!ClassRegistry.TryGetValue(className, out var cls)) return false;
        var split = variable.Split('.');
        object current = cls;
        for (int i = 0; i < split.Length - 1; i++)
        {
            if (current == null) return false;
            current = LuaUtils.GetVarInArray(current, split[i]);
        }
        LuaUtils.SetVarInArray(current, split[^1], value);
        return true;
    }

    private static bool OptBool(DynValue v, bool defaultValue = false)
    {
        if (v == null || v.Type == DataType.Nil || v.Type == DataType.Void) return defaultValue;
        return v.CastToBool();
    }

    public static void Implement(FunkinLua funk)
    {
        var lua = funk.LuaScript;

        lua.Globals["getProperty"] = (Func<string, DynValue, DynValue>)((variable, allowMaps) =>
        {
            try
            {
                var split = variable.Split('.');
                object result;
                bool allowMapsBool = OptBool(allowMaps);
                if (split.Length > 1)
                {
                    var obj = LuaUtils.GetPropertyLoop(split, true, allowMapsBool);
                    result = LuaUtils.GetVarInArray(obj, split[^1], allowMapsBool);
                }
                else
                {
                    result = LuaUtils.GetVarInArray(MusicBeatState.GetTargetInstance(), variable, allowMapsBool);
                }
                return DynValue.FromObject(lua, result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LUA] getProperty error: {ex.Message}");
                return DynValue.Nil;
            }
        });

        lua.Globals["setProperty"] = (Func<string, DynValue, DynValue, DynValue, DynValue>)((variable, value, allowMaps, allowInstances) =>
        {
            try
            {
                bool allowInstancesBool = OptBool(allowInstances);
                bool allowMapsBool = OptBool(allowMaps);
                object parsed = allowInstancesBool ? ParseInstances(value?.ToObject()) : value?.ToObject();
                var split = variable.Split('.');
                if (split.Length > 1)
                {
                    var obj = LuaUtils.GetPropertyLoop(split, true, allowMapsBool);
                    LuaUtils.SetVarInArray(obj, split[^1], parsed, allowMapsBool);
                }
                else
                {
                    LuaUtils.SetVarInArray(MusicBeatState.GetTargetInstance(), variable, parsed, allowMapsBool);
                }
                return value;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LUA] setProperty error: {ex.Message}");
                return DynValue.Nil;
            }
        });

        lua.Globals["getPropertyFromClass"] = (Func<string, string, DynValue, DynValue>)((className, variable, allowMaps) =>
        {
            try
            {
                if (!ClassRegistry.TryGetValue(className, out var cls))
                {
                    Console.WriteLine($"[LUA] getPropertyFromClass: Class {className} not found");
                    return DynValue.Nil;
                }
                var split = variable.Split('.');
                object result;
                bool allowMapsBool = OptBool(allowMaps);
                if (split.Length > 1)
                {
                    var obj = LuaUtils.GetVarInArray(cls, split[0], allowMapsBool);
                    for (int i = 1; i < split.Length - 1; i++)
                        obj = LuaUtils.GetVarInArray(obj, split[i], allowMapsBool);
                    result = LuaUtils.GetVarInArray(obj, split[^1], allowMapsBool);
                }
                else
                {
                    result = LuaUtils.GetVarInArray(cls, variable, allowMapsBool);
                }
                return DynValue.FromObject(lua, result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LUA] getPropertyFromClass error: {ex.Message}");
                return DynValue.Nil;
            }
        });

        lua.Globals["setPropertyFromClass"] = (Func<string, string, DynValue, DynValue, DynValue, DynValue>)((className, variable, value, allowMaps, allowInstances) =>
        {
            try
            {
                if (!ClassRegistry.TryGetValue(className, out var cls))
                {
                    Console.WriteLine($"[LUA] setPropertyFromClass: Class {className} not found");
                    return DynValue.Nil;
                }
                bool allowInstancesBool = OptBool(allowInstances);
                bool allowMapsBool = OptBool(allowMaps);
                object parsed = allowInstancesBool ? ParseInstances(value?.ToObject()) : value?.ToObject();
                var split = variable.Split('.');
                if (split.Length > 1)
                {
                    var obj = LuaUtils.GetVarInArray(cls, split[0], allowMapsBool);
                    for (int i = 1; i < split.Length - 1; i++)
                        obj = LuaUtils.GetVarInArray(obj, split[i], allowMapsBool);
                    LuaUtils.SetVarInArray(obj, split[^1], parsed, allowMapsBool);
                }
                else
                {
                    LuaUtils.SetVarInArray(cls, variable, parsed, allowMapsBool);
                }
                return value;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LUA] setPropertyFromClass error: {ex.Message}");
                return DynValue.Nil;
            }
        });

        lua.Globals["getPropertyFromGroup"] = (Func<string, int, DynValue, DynValue, DynValue>)((group, index, variable, allowMaps) =>
        {
            try
            {
                if (StrumGroupIndex(group, ref index))
                {
                    var mods = funk.GameScene?.StrumMods;
                    if (mods != null && index >= 0 && index < mods.Length)
                    {
                        var mod = mods[index];
                        var gs = funk.GameScene;
                        return (VarName(variable).ToLowerInvariant()) switch
                        {
                            "x" => DynValue.NewNumber(mod.OffsetX + gs.PsychStrumBaseX(index)), // Psych: absolute strum x
                            "y" => DynValue.NewNumber(mod.OffsetY + gs.PsychStrumBaseY(index)),
                            "angle" => DynValue.NewNumber(mod.Angle),
                            "alpha" => DynValue.NewNumber(mod.Alpha),
                            "direction" => DynValue.NewNumber(mod.Direction),
                            "visible" => DynValue.NewBoolean(mod.Visible),
                            _ => DynValue.Nil
                        };
                    }
                    return DynValue.Nil;
                }
                if (string.Equals(group, "notes", StringComparison.OrdinalIgnoreCase))
                {
                    var members = funk.GameScene?.PsychNotesMembers(); // PlayState.notes = spawned notes, newest first
                    if (members != null && index >= 0 && index < members.Count)
                    {
                        var note = funk.GameScene.PsychGroupLive(members[index]);
                        return DynValue.FromObject(lua, GroupGet(note, VarName(variable), false));
                    }
                    return DynValue.Nil;
                }
                bool allowMapsBool = OptBool(allowMaps);
                string variableName = VarName(variable);
                var split = group.Split('.');
                object realObject = split.Length > 1
                    ? LuaUtils.GetPropertyLoop(split, false, allowMapsBool)
                    : LuaUtils.GetVarInArray(MusicBeatState.GetTargetInstance(), group, allowMapsBool);

                if (realObject is System.Collections.IList list && index >= 0 && index < list.Count)
                {
                    var item = list[index];
                    return DynValue.FromObject(lua, GroupGet(item, variableName, allowMapsBool));
                }
                return DynValue.Nil;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LUA] getPropertyFromGroup error: {ex.Message}");
                return DynValue.Nil;
            }
        });

        lua.Globals["setPropertyFromGroup"] = (Action<string, int, DynValue, DynValue, DynValue, DynValue>)((group, index, variable, value, allowMaps, allowInstances) =>
        {
            try
            {
                if (StrumGroupIndex(group, ref index))
                {
                    var mods = funk.GameScene?.StrumMods;
                    if (mods != null && index >= 0 && index < mods.Length)
                    {
                        var mod = mods[index];
                        var gs = funk.GameScene;
                        switch (VarName(variable).ToLowerInvariant())
                        {
                            case "x": mod.OffsetX = (float)(value?.CastToNumber() ?? 0) - gs.PsychStrumBaseX(index); break;
                            case "y": mod.OffsetY = (float)(value?.CastToNumber() ?? 0) - gs.PsychStrumBaseY(index); break;
                            case "angle": mod.Angle = (float)(value?.CastToNumber() ?? 0); break;
                            case "alpha": mod.Alpha = (float)(value?.CastToNumber() ?? 1); break;
                            case "direction": mod.Direction = (float)(value?.CastToNumber() ?? 90); break;
                            case "visible": mod.Visible = value?.CastToBool() ?? true; break;
                        }
                    }
                    return;
                }
                if (string.Equals(group, "notes", StringComparison.OrdinalIgnoreCase))
                {
                    var members = funk.GameScene?.PsychNotesMembers(); // PlayState.notes = spawned notes, newest first
                    if (members != null && index >= 0 && index < members.Count)
                    {
                        var note = funk.GameScene.PsychGroupLive(members[index]);
                        GroupSet(note, VarName(variable), value?.ToObject(), false);
                    }
                    return;
                }
                bool allowMapsBool = OptBool(allowMaps);
                bool allowInstancesBool = OptBool(allowInstances);
                string variableName = VarName(variable);
                var split = group.Split('.');
                object realObject = split.Length > 1
                    ? LuaUtils.GetPropertyLoop(split, false, allowMapsBool)
                    : LuaUtils.GetVarInArray(MusicBeatState.GetTargetInstance(), group, allowMapsBool);

                object parsed = allowInstancesBool ? ParseInstances(value?.ToObject()) : value?.ToObject();
                if (realObject is System.Collections.IList list && index >= 0 && index < list.Count)
                {
                    var item = list[index];
                    GroupSet(item, variableName, parsed, allowMapsBool);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LUA] setPropertyFromGroup error: {ex.Message}");
            }
        });

        lua.Globals["callMethod"] = (Func<string, Table, DynValue>)((method, args) =>
        {
            try
            {
                var split = method.Split('.');
                object target = MusicBeatState.GetTargetInstance();
                for (int i = 0; i < split.Length - 1; i++)
                    target = LuaUtils.GetVarInArray(target, split[i]);
                return SafeToLua(lua, InvokeMethod(target, split[^1], TableToArgs(args)));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LUA] callMethod error: {ex.Message}");
                return DynValue.Nil;
            }
        });

        lua.Globals["callMethodFromClass"] = (Func<string, string, Table, DynValue>)((className, method, args) =>
        {
            try
            {
                if (!ClassRegistry.TryGetValue(className, out var cls))
                {
                    Console.WriteLine($"[LUA] callMethodFromClass: Class {className} not found");
                    return DynValue.Nil;
                }
                var split = method.Split('.');
                object target = cls;
                for (int i = 0; i < split.Length - 1; i++)
                    target = LuaUtils.GetVarInArray(target, split[i]);
                return SafeToLua(lua, InvokeMethod(target, split[^1], TableToArgs(args)));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LUA] callMethodFromClass error: {ex.Message}");
                return DynValue.Nil;
            }
        });

        lua.Globals["createInstance"] = (Func<string, string, Table, DynValue>)((variableToSave, className, args) =>
        {
            try
            {
                if (className != null && (className == "Character" || className.EndsWith(".Character")) && MusicBeatState.GetTargetInstance() is Scenes.PlayScene ps)
                {
                    var a = TableToArgs(args);
                    float ax = a.Length > 0 ? Convert.ToSingle(a[0]) : 0f, ay = a.Length > 1 ? Convert.ToSingle(a[1]) : 0f;
                    string cname = a.Length > 2 ? a[2]?.ToString() ?? "bf" : "bf";
                    bool player = a.Length > 3 && a[3] is bool pb && pb;
                    MusicBeatState.Variables[variableToSave.Trim()] = ps.CreatePsychExtraCharacter(ax, ay, cname, player);
                    return DynValue.NewBoolean(true);
                }
                if ((className == "flixel.text.FlxText" || className == "FlxText") && MusicBeatState.GetTargetInstance() is Scenes.PlayScene tps)
                {
                    // new FlxText(X = 0, Y = 0, FieldWidth = 0, ?Text, Size = 8): plain FlxText (no border), stored in the variables map
                    var a = TableToArgs(args);
                    float tx = a.Length > 0 ? Convert.ToSingle(a[0]) : 0f, ty = a.Length > 1 ? Convert.ToSingle(a[1]) : 0f;
                    float tw = a.Length > 2 ? Convert.ToSingle(a[2]) : 0f;
                    string tt = a.Length > 3 ? a[3]?.ToString() ?? "" : "";
                    string tag = variableToSave.Trim();
                    tps.MakeLuaText(tag, tt, tw, tx, ty);
                    var lt = tps.GetLuaText(tag);
                    if (lt != null) { lt.Size = a.Length > 4 ? Convert.ToInt32(a[4]) : 8; lt.BorderStyle = "none"; lt.Alignment = "left"; } // (drawn on the HUD layer: LuaText has no camGame path)
                    return DynValue.NewBoolean(true);
                }
                SysType type = ResolveType(className);
                if (type == null)
                {
                    Console.WriteLine($"[LUA] createInstance: Class {className} not found");
                    return DynValue.NewBoolean(false);
                }
                object instance = Activator.CreateInstance(type, TableToArgs(args));
                MusicBeatState.Variables[variableToSave.Trim()] = instance;
                return DynValue.NewBoolean(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LUA] createInstance error: {ex.Message}");
                return DynValue.NewBoolean(false);
            }
        });

        lua.Globals["addInstance"] = (Action<string, DynValue>)((objectName, inFront) =>
        {
            try
            {
                bool inFrontBool = OptBool(inFront);
                if (MusicBeatState.Variables.TryGetValue(objectName.Trim(), out var obj) &&
                    MusicBeatState.GetTargetInstance() is Scenes.PlayScene scene)
                {
                    if (obj is Gameplay.Character ch)
                        scene.AddPsychCharacter(ch, objectName.Trim(), inFrontBool);
                    else if (obj is ModchartSprite spr)
                        scene.AddStageObject(spr.ToStageProp());
                    else if (obj is LuaText lt)
                        scene.AddLuaText(objectName.Trim(), inFrontBool);
                    else
                        scene.AddStageObject(obj);
                }
                else
                {
                    Console.WriteLine($"[LUA] addInstance: Can't add what doesn't exist~ ({objectName})");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LUA] addInstance error: {ex.Message}");
            }
        });
        // ReflectionFunctions.hx instanceArg: a marker string that callMethod/createInstance/setProperty(allowInstances)
        // turn back into the object (port returned the bare name, so callMethod got a string instead of camHUD)
        lua.Globals["instanceArg"] = (Func<string, DynValue>)((instance) => DynValue.NewString("::INSTANCE::" + instance));
    }

    private static object ParseInstances(object value)
    {
        if (value is string str)
        {
            const string instanceStr = "::INSTANCE::";
            if (str.Contains(instanceStr))
            {
                string name = str.Replace(instanceStr, "").Trim();
                if (MusicBeatState.Variables.TryGetValue(name, out var saved))
                    return saved;
                return LuaUtils.GetVarInArray(MusicBeatState.GetTargetInstance(), name);
            }
        }
        return value;
    }

    /// <summary>Convert.toLua: values Lua cannot hold (objects such as cameras returned by FlxG.cameras.add or
    /// setPosition) become nil instead of failing the whole call.</summary>
    private static DynValue SafeToLua(Script lua, object v)
    {
        if (v == null) return DynValue.Nil;
        try { return DynValue.FromObject(lua, v); } catch { return DynValue.Nil; }
    }

    private static object[] TableToArgs(Table args)
    {
        if (args == null) return System.Array.Empty<object>();
        var list = new List<object>();
        foreach (var v in args.Values)
            list.Add(ParseInstances(v.ToObject()));
        return list.ToArray();
    }

    private static SysType ResolveType(string className)
    {
        if (ClassRegistry.TryGetValue(className, out var registered))
        {
            if (registered is SysType t) return t;
            return registered.GetType();
        }
        // Strip Haxe package prefixes (e.g. "objects.Character" -> "Character")
        string shortName = className.Contains('.') ? className[(className.LastIndexOf('.') + 1)..] : className;
        var asm = typeof(ReflectionFunctions).Assembly;
        foreach (var candidateType in asm.GetTypes())
        {
            if (string.Equals(candidateType.Name, shortName, StringComparison.OrdinalIgnoreCase))
                return candidateType;
        }
        return null;
    }

    private static object InvokeMethod(object target, string methodName, object[] args)
    {
        if (target == null) return null;
        SysType type = target as SysType ?? target.GetType();
        object instance = target is SysType ? null : target;

        var candidates = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                             .Where(m => string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase))
                             .ToArray();
        if (candidates.Length == 0)
        {
            Console.WriteLine($"[LUA] callMethod: method {methodName} not found on {type.Name}");
            return null;
        }

        foreach (var method in candidates)
        {
            var pars = method.GetParameters();
            if (args.Length > pars.Length) continue;
            if (args.Length < pars.Length && !pars[args.Length].HasDefaultValue) continue;

            try
            {
                object[] finalArgs = new object[pars.Length];
                for (int i = 0; i < pars.Length; i++)
                {
                    if (i < args.Length)
                        finalArgs[i] = ConvertArg(args[i], pars[i].ParameterType);
                    else
                        finalArgs[i] = pars[i].DefaultValue;
                }
                return method.Invoke(instance, finalArgs);
            }
            catch (Exception)
            {
                // try next overload
            }
        }

        Console.WriteLine($"[LUA] callMethod: no matching overload for {methodName} on {type.Name}");
        return null;
    }

    private static object ConvertArg(object arg, SysType targetType)
    {
        if (arg == null) return null;
        if (targetType.IsInstanceOfType(arg)) return arg;
        try { return Convert.ChangeType(arg, targetType); }
        catch { return arg; }
    }

    /// <summary>Lua argument as a member name. DynValue.ToString() quotes strings ("\"x\""), so every
    /// get/setPropertyFromGroup lookup used to miss (unspawnNotes noteType/texture/strumTime all nil).</summary>
    /// <summary>strumLineNotes (0-3 opponent, 4-7 player), playerStrums (+4), opponentStrums -> StrumMods index.</summary>
    private static bool StrumGroupIndex(string group, ref int index)
    {
        if (string.Equals(group, "strumLineNotes", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(group, "opponentStrums", StringComparison.OrdinalIgnoreCase)) return index >= 0 && index < 4;
        if (string.Equals(group, "playerStrums", StringComparison.OrdinalIgnoreCase)) { if (index < 0 || index >= 4) return false; index += 4; return true; }
        return false;
    }

    private static string VarName(DynValue v)
    {
        if (v == null || v.IsNil()) return "";
        if (v.Type == DataType.String) return v.String;
        if (v.Type == DataType.Number) return ((long)v.Number).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return v.CastToString() ?? "";
    }

    /// <summary>LuaUtils.getGroupStuff: dotted paths walk Reflect.getProperty (noteSplashData.texture, rgbShader.r).</summary>
    private static object GroupGet(object item, string name, bool allowMaps)
    {
        var parts = name.Split('.');
        object o = item;
        for (int i = 0; i < parts.Length && o != null; i++) o = LuaUtils.GetGroupStuff(o, parts[i], allowMaps);
        return o;
    }

    /// <summary>LuaUtils.setGroupStuff with dotted paths.</summary>
    private static void GroupSet(object item, string name, object value, bool allowMaps)
    {
        var parts = name.Split('.');
        object o = item;
        for (int i = 0; i < parts.Length - 1 && o != null; i++) o = LuaUtils.GetGroupStuff(o, parts[i], allowMaps);
        if (o != null) LuaUtils.SetVarInArray(o, parts[parts.Length - 1], value, allowMaps);
    }
}
