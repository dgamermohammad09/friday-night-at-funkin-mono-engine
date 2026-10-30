extern alias IRIS;
using System;
using System.Collections.Generic;
using System.IO;
using FNF_MonoGame.Engine.PsychEngine;
using MoonSharp.Interpreter;

namespace FNF_MonoGame.Engine.PureHScript
{
    /// <summary>
    /// The rest of psychlua/HScript.hx preset() (Psych 1.0.4, lines 146-349) that the port lacked: FlxMath, PsychCamera,
    /// Countdown, Achievements, Alphabet, Note, CustomSubstate, File/FileSystem, getModSetting, keyboard/gamepad/key helpers,
    /// createGlobalCallback / createCallback / addHaxeLibrary, parentLua, controls, customSubstate(Name); plus proxy imports
    /// for the backend/objects/psychlua class paths scripts import.
    /// </summary>
    public static class PsychHScriptPresets
    {
        public static void Apply(Action<string, object> set, PsychHScriptEngine engine)
        {
            set("FlxMath", typeof(FNF_MonoGame.Scenes.HxFlxMath));
            set("PsychCamera", typeof(PsychCamera));
            set("Countdown", typeof(HxCountdown));
            set("Achievements", typeof(Achievements));
            set("Alphabet", typeof(FNF_MonoGame.Engine.PsychEngine.Objects.Alphabet));
            set("Note", typeof(Note));
            set("CustomSubstate", typeof(CustomSubstate));
            set("File", typeof(HxFile));
            set("FileSystem", typeof(HxFileSystem));
            set("parentLua", engine.ParentLua);
            set("controls", Controls.Instance);
            set("customSubstate", null);
            set("customSubstateName", CustomSubstate.Name);

            set("getModSetting", new HxFn(a =>
            {
                string tag = HxFn.Arg(a, 0)?.ToString(); string mod = HxFn.Arg(a, 1)?.ToString();
                return LuaUtils.GetModSetting(string.IsNullOrEmpty(mod) ? PsychMods.CurrentModDirectory : mod, tag);
            }));
            // input helpers (ExtraFunctions / HScript.preset): same names as the Lua callbacks
            foreach (var n in new[] { "keyboardJustPressed", "keyboardPressed", "keyboardReleased", "key" + "JustPressed", "keyPressed", "keyReleased",
                                      "anyGamepadJustPressed", "anyGamepadPressed", "anyGamepadReleased", "gamepadJustPressed", "gamepadPressed", "gamepadReleased",
                                      "gamepadAnalogX", "gamepadAnalogY" })
            {
                string name = n;
                set(name, new HxFn(a => CallLuaHelper(name, a)));
            }
            // createGlobalCallback(name, func): every running Lua script + FunkinLua.customFunctions
            set("createGlobalCallback", new HxFn(a =>
            {
                string name = HxFn.Arg(a, 0)?.ToString(); object fn = HxFn.Arg(a, 1);
                if (name != null && fn != null) FunkinLua.RegisterGlobalCallback(name, args => InvokeHx(fn, args));
                return null;
            }));
            // createCallback(name, func, ?funk): only on the parent Lua (runHaxeCode) or the given FunkinLua
            set("createCallback", new HxFn(a =>
            {
                string name = HxFn.Arg(a, 0)?.ToString(); object fn = HxFn.Arg(a, 1);
                var funk = (HxFn.Arg(a, 2) as FunkinLua) ?? (engine.ParentLua as FunkinLua);
                if (funk == null) { Console.WriteLine("[PSYCH-HSCRIPT] createCallback (" + name + "): 3rd argument is null"); return null; }
                funk.AddLocalCallback(name, args => InvokeHx(fn, args));
                return null;
            }));
            set("addHaxeLibrary", new HxFn(a =>
            {
                string lib = HxFn.Arg(a, 0)?.ToString(); string pkg = HxFn.Arg(a, 1)?.ToString() ?? "";
                if (string.IsNullOrEmpty(lib)) return null;
                object cls = ReflectionFunctions.GetClassInstance(pkg.Length > 0 ? pkg + "." + lib : lib) ?? ReflectionFunctions.GetClassInstance(lib);
                if (cls != null) set(lib, cls); else Console.WriteLine("[PSYCH-HSCRIPT] addHaxeLibrary: " + pkg + "." + lib + " is not available in the port");
                return null;
            }));
        }

        public static void RegisterProxies(Action<string, object> p)
        {
            p("backend.Conductor", typeof(PsychConductor));
            p("backend.CoolUtil", typeof(CoolUtil));
            p("backend.Difficulty", typeof(Difficulty));
            p("backend.Mods", typeof(PsychMods));
            p("backend.Achievements", typeof(Achievements));
            p("backend.PsychCamera", typeof(PsychCamera));
            p("backend.BaseStage.Countdown", typeof(HxCountdown));
            p("objects.Alphabet", typeof(FNF_MonoGame.Engine.PsychEngine.Objects.Alphabet));
            p("objects.Note", typeof(Note));
            p("psychlua.CustomSubstate", typeof(CustomSubstate));
            p("psychlua.LuaUtils", typeof(LuaUtils));
            p("psychlua.FunkinLua", typeof(FunkinLua));
            p("sys.io.File", typeof(HxFile));
            p("sys.FileSystem", typeof(HxFileSystem));
        }

        internal static object InvokeHx(object fn, object[] args)
        {
            try
            {
                if (fn is IRIS::haxe.lang.Function f) return f.__hx_invokeDynamic(args ?? System.Array.Empty<object>());
            }
            catch (Exception ex) { Console.WriteLine("[PSYCH-HSCRIPT] callback error: " + ex.Message); }
            return null;
        }

        /// <summary>Runs the Lua-side implementation of an input helper (one shared definition for Lua and HScript).</summary>
        private static object CallLuaHelper(string name, object[] a)
        {
            var s = FunkinLua.RunningScripts.Count > 0 ? FunkinLua.RunningScripts[0] : null;
            if (s == null || s.Closed) return false;
            var fn = s.LuaScript.Globals.Get(name);
            if (fn.Type != DataType.Function && fn.Type != DataType.ClrFunction) return false;
            try { return s.LuaScript.Call(fn, a ?? System.Array.Empty<object>()).ToObject(); } catch { return false; }
        }
    }

    /// <summary>backend.BaseStage.Countdown (THREE..START = the port's Gameplay.Countdown values).</summary>
    public static class HxCountdown
    {
        public const int THREE = 0, TWO = 1, ONE = 2, GO = 3, START = 4;
    }

    /// <summary>sys.io.File subset.</summary>
    public static class HxFile
    {
        public static string getContent(string path) => global::FNF_MonoGame.Engine.FsCI.FileExists(path) ? global::FNF_MonoGame.Engine.FsCI.ReadAllText(path) : null;
        public static void saveContent(string path, string content) { var d = Path.GetDirectoryName(path); if (!string.IsNullOrEmpty(d)) Directory.CreateDirectory(d); File.WriteAllText(path, content ?? ""); }
        public static byte[] getBytes(string path) => global::FNF_MonoGame.Engine.FsCI.FileExists(path) ? global::FNF_MonoGame.Engine.FsCI.ReadAllBytes(path) : null;
        public static void copy(string src, string dst) => File.Copy(src, dst, true);
    }

    /// <summary>sys.FileSystem subset.</summary>
    public static class HxFileSystem
    {
        public static bool exists(string path) => global::FNF_MonoGame.Engine.FsCI.FileExists(path) || global::FNF_MonoGame.Engine.FsCI.DirectoryExists(path);
        public static bool isDirectory(string path) => global::FNF_MonoGame.Engine.FsCI.DirectoryExists(path);
        public static void createDirectory(string path) => Directory.CreateDirectory(path);
        public static void deleteFile(string path) { if (global::FNF_MonoGame.Engine.FsCI.FileExists(path)) File.Delete(path); }
        public static void deleteDirectory(string path) { if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(path)) Directory.Delete(path); }
        public static string absolutePath(string path) => Path.GetFullPath(path);
        public static IRIS::Array<object> readDirectory(string path)
        {
            var l = new List<object>();
            if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(path)) foreach (var e in Directory.GetFileSystemEntries(path)) l.Add(Path.GetFileName(e));
            return new IRIS::Array<object>(l.ToArray());
        }
    }
}
