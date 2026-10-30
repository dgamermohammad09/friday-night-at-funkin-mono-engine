using System;
using MoonSharp.Interpreter;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine deprecated Lua functions.
/// Direct port of psychlua/DeprecatedFunctions.hx — provides backward-compatible
/// aliases for older mods. These functions are mapped to their modern equivalents.
/// </summary>
public static class DeprecatedFunctions
{
    private static void CallGlobal(Script lua, string name, params object[] args)
    {
        var fn = lua.Globals.Get(name);
        if (fn.Type == DataType.Function)
            lua.Call(fn, args);
    }

    private static DynValue CallGlobalValue(Script lua, string name, params object[] args)
    {
        var fn = lua.Globals.Get(name);
        if (fn.Type == DataType.Function)
            return lua.Call(fn, args);
        return DynValue.Nil;
    }

    public static void Implement(FunkinLua funk)
    {
        var lua = funk.LuaScript;

        lua.Globals["addAnimationByIndicesLoop"] = (Action<string, string, string, string, int, int>)((tag, name, prefix, indices, framerate, loop) =>
        {
            CallGlobal(lua, "addAnimationByIndices", tag, name, prefix, indices, framerate, loop);
        });

        lua.Globals["objectPlayAnimation"] = (Action<string, string, bool, bool>)((tag, anim, forced, reverse) =>
        {
            CallGlobal(lua, "playAnim", tag, anim, forced, reverse, 0);
        });

        lua.Globals["characterPlayAnim"] = (Action<string, string, bool>)((character, anim, forced) =>
        {
            CallGlobal(lua, "playAnim", character, anim, forced, false, 0);
        });

        lua.Globals["luaSpriteMakeGraphic"] = (Action<string, int, int, string>)((tag, width, height, color) =>
        {
            CallGlobal(lua, "makeLuaGraphic", tag, width, height, color);
        });

        lua.Globals["luaSpriteAddAnimationByPrefix"] = (Action<string, string, string, int, bool>)((tag, name, prefix, framerate, loop) =>
        {
            CallGlobal(lua, "addAnimationByPrefix", tag, name, prefix, framerate, loop);
        });

        lua.Globals["luaSpriteAddAnimationByIndices"] = (Action<string, string, string, string, int, bool>)((tag, name, prefix, indices, framerate, loop) =>
        {
            CallGlobal(lua, "addAnimationByIndices", tag, name, prefix, indices, framerate, loop);
        });

        lua.Globals["luaSpritePlayAnimation"] = (Action<string, string, bool, bool, int>)((tag, anim, forced, reverse, startFrame) =>
        {
            CallGlobal(lua, "playAnim", tag, anim, forced, reverse, startFrame);
        });

        lua.Globals["setLuaSpriteCamera"] = (Action<string, string>)((tag, camera) =>
        {
            CallGlobal(lua, "setObjectCamera", tag, camera);
        });

        lua.Globals["setLuaSpriteScrollFactor"] = (Action<string, double, double>)((tag, x, y) =>
        {
            CallGlobal(lua, "setScrollFactor", tag, x, y);
        });

        lua.Globals["scaleLuaSprite"] = (Action<string, double, double>)((tag, x, y) =>
        {
            CallGlobal(lua, "scaleObject", tag, x, y);
        });

        lua.Globals["getPropertyLuaSprite"] = (Func<string, string, DynValue>)((tag, variable) =>
        {
            return CallGlobalValue(lua, "getProperty", $"{tag}.{variable}");
        });
    }
}
