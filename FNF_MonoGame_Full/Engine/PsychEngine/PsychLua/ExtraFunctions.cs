using System;
using System.Collections.Generic;
using System.IO;
using MoonSharp.Interpreter;

namespace FNF_MonoGame.Engine.PsychEngine.PsychLua;

/// <summary>
/// Psych Engine ExtraFunctions Lua API.
/// Direct port of psychlua/ExtraFunctions.hx — exposes save-data, file management,
/// and gamepad-wildcard callbacks with the EXACT function names Psych mods expect.
///
/// Keyboard/keybind/random/string helpers are already registered by
/// <see cref="FunkinLua"/>; this file only adds the ones missing there so
/// existing registrations aren't overwritten.
/// </summary>
public static class ExtraFunctions
{
    /// <summary>
    /// Save data registry. Psych stores these under `variables['save_$name']` on
    /// MusicBeatState so multiple scripts on the same state share them. Since
    /// FunkinLua is per-script here, we make the registry global so any script
    /// on the same state can read/write the same named save. Matches Psych behavior.
    /// </summary>
    private static readonly Dictionary<string, LuaSaveData> _saves = new(StringComparer.Ordinal);

    public static void Implement(FunkinLua funk)
    {
        if (funk?.LuaScript == null) return;
        var lua = funk.LuaScript;

        RegisterSaveData(lua);
        RegisterFileManagement(lua);
        RegisterGamepadWildcards(lua);
    }

    // -----------------------------------------------------------------
    //  Save data — matches Psych's initSaveData / flushSaveData /
    //  getDataFromSave / setDataFromSave / eraseSaveData exactly.
    // -----------------------------------------------------------------
    private static void RegisterSaveData(Script lua)
    {
        // initSaveData(name, folder='psychenginemods')
        lua.Globals["initSaveData"] = DynValue.NewCallback((ctx, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String) return DynValue.Nil;
            string name = args[0].String;
            string folder = args.Count >= 2 && args[1].Type == DataType.String
                ? args[1].String : "psychenginemods";
            if (_saves.ContainsKey(name))
            {
                FunkinLua.LuaTrace($"initSaveData: Save file already initialized: {name}", false, false);
                return DynValue.Nil;
            }
            _saves[name] = new LuaSaveData(name, folder);
            return DynValue.Nil;
        });

        // flushSaveData(name)
        lua.Globals["flushSaveData"] = DynValue.NewCallback((ctx, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String) return DynValue.Nil;
            string name = args[0].String;
            if (!_saves.TryGetValue(name, out var save))
            {
                FunkinLua.LuaTrace($"flushSaveData: Save file not initialized: {name}", false, false);
                return DynValue.Nil;
            }
            save.Flush();
            return DynValue.Nil;
        });

        // getDataFromSave(name, field, defaultValue=nil)
        lua.Globals["getDataFromSave"] = DynValue.NewCallback((ctx, args) =>
        {
            if (args.Count < 2 || args[0].Type != DataType.String || args[1].Type != DataType.String)
                return DynValue.Nil;
            string name = args[0].String;
            string field = args[1].String;
            DynValue defaultValue = args.Count >= 3 ? args[2] : DynValue.Nil;
            if (!_saves.TryGetValue(name, out var save))
            {
                FunkinLua.LuaTrace($"getDataFromSave: Save file not initialized: {name}", false, false);
                return defaultValue;
            }
            object val = save.GetField(field, null);
            return val == null ? defaultValue : DynValue.FromObject(ctx.GetScript(), val);
        });

        // setDataFromSave(name, field, value)
        lua.Globals["setDataFromSave"] = DynValue.NewCallback((ctx, args) =>
        {
            if (args.Count < 3 || args[0].Type != DataType.String || args[1].Type != DataType.String)
                return DynValue.Nil;
            string name = args[0].String;
            string field = args[1].String;
            if (!_saves.TryGetValue(name, out var save))
            {
                FunkinLua.LuaTrace($"setDataFromSave: Save file not initialized: {name}", false, false);
                return DynValue.Nil;
            }
            save.SetField(field, args[2].ToObject());
            return DynValue.Nil;
        });

        // eraseSaveData(name)
        lua.Globals["eraseSaveData"] = DynValue.NewCallback((ctx, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String) return DynValue.Nil;
            string name = args[0].String;
            if (!_saves.TryGetValue(name, out var save))
            {
                FunkinLua.LuaTrace($"eraseSaveData: Save file not initialized: {name}", false, false);
                return DynValue.Nil;
            }
            save.Erase();
            return DynValue.Nil;
        });
    }

    // -----------------------------------------------------------------
    //  File management — Psych parity:
    //    checkFileExists(filename, absolute=false)
    //    saveFile(path, content, absolute=false)      -> bool
    //    deleteFile(path, ignoreModFolders=false, absolute=false) -> bool
    //  When `absolute` is false, paths route through the mod-aware
    //  PsychPaths.ResolvePath (matching Psych's `Paths.getPath`).
    // -----------------------------------------------------------------
    private static void RegisterFileManagement(Script lua)
    {
        lua.Globals["checkFileExists"] = DynValue.NewCallback((ctx, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String) return DynValue.False;
            string filename = args[0].String;
            bool absolute = args.Count >= 2 && args[1].CastToBool();
            try
            {
                if (absolute) return DynValue.NewBoolean(global::FNF_MonoGame.Engine.FsCI.FileExists(filename));
                string resolved = PsychPaths.ResolvePath(filename);
                return DynValue.NewBoolean(!string.IsNullOrEmpty(resolved) && global::FNF_MonoGame.Engine.FsCI.FileExists(resolved));
            }
            catch { return DynValue.False; }
        });

        lua.Globals["saveFile"] = DynValue.NewCallback((ctx, args) =>
        {
            if (args.Count < 2 || args[0].Type != DataType.String || args[1].Type != DataType.String)
                return DynValue.False;
            string path = args[0].String;
            string content = args[1].String;
            bool absolute = args.Count >= 3 && args[2].CastToBool();
            try
            {
                string finalPath = absolute ? path : PsychPaths.Mods(path);
                string dir = Path.GetDirectoryName(finalPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(finalPath, content);
                return DynValue.True;
            }
            catch (Exception ex)
            {
                FunkinLua.LuaTrace($"saveFile: Error trying to save {path}: {ex.Message}", false, false);
                return DynValue.False;
            }
        });

        lua.Globals["deleteFile"] = DynValue.NewCallback((ctx, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String) return DynValue.False;
            string path = args[0].String;
            bool ignoreModFolders = args.Count >= 2 && args[1].CastToBool();
            bool absolute = args.Count >= 3 && args[2].CastToBool();
            try
            {
                string finalPath = absolute ? path : PsychPaths.ResolvePath(path);
                if (!string.IsNullOrEmpty(finalPath) && global::FNF_MonoGame.Engine.FsCI.FileExists(finalPath))
                {
                    // ignoreModFolders in Psych means: don't resolve into mod folders,
                    // only base game. Our ResolvePath already prefers mods first; when
                    // the flag is set and the resolved path is inside the mods root,
                    // reject the delete to avoid touching mod files by accident.
                    if (ignoreModFolders)
                    {
                        string modsRoot = PsychPaths.Mods();
                        if (!string.IsNullOrEmpty(modsRoot)
                            && finalPath.StartsWith(modsRoot, StringComparison.OrdinalIgnoreCase))
                            return DynValue.False;
                    }
                    File.Delete(finalPath);
                    return DynValue.True;
                }
            }
            catch (Exception ex)
            {
                FunkinLua.LuaTrace($"deleteFile: Error trying to delete {path}: {ex.Message}", false, false);
            }
            return DynValue.False;
        });
    }

    // -----------------------------------------------------------------
    //  Gamepad wildcards — anyGamepad* return true if any connected
    //  controller has that button in the requested state.
    // -----------------------------------------------------------------
    private static void RegisterGamepadWildcards(Script lua)
    {
        lua.Globals["anyGamepadJustPressed"] = (Func<string, bool>)((name) =>
        {
            var input = FNFGame.Instance?.Input;
            if (input == null || !LuaUtils.TryParseButton(name, out var btn)) return false;
            return input.ButtonJustPressed(btn);
        });
        lua.Globals["anyGamepadPressed"] = (Func<string, bool>)((name) =>
        {
            var input = FNFGame.Instance?.Input;
            if (input == null || !LuaUtils.TryParseButton(name, out var btn)) return false;
            return input.ButtonHeld(btn);
        });
        lua.Globals["anyGamepadReleased"] = (Func<string, bool>)((name) =>
        {
            var input = FNFGame.Instance?.Input;
            if (input == null || !LuaUtils.TryParseButton(name, out var btn)) return false;
            return input.ButtonReleased(btn);
        });
    }

    /// <summary>
    /// Helper for other C# systems (e.g. state persistence) to look up a save file
    /// initialized via <c>initSaveData</c>. Returns null if not initialized.
    /// </summary>
    public static LuaSaveData Get(string name)
    {
        return _saves.TryGetValue(name, out var save) ? save : null;
    }
}
