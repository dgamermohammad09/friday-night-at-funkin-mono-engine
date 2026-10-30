extern alias IRIS;
using System;
using System.Collections.Generic;
using System.IO;
using IRIS::crowplexus.iris;
using FNF_MonoGame.Engine.PsychEngine;

namespace FNF_MonoGame.Engine.PureHScript
{
    /// <summary>Wraps a C# delegate as a Haxe function value callable from HScript.</summary>
    public sealed class HxFn : IRIS::haxe.lang.VarArgsBase
    {
        private readonly Func<object[], object> _fn;
        public HxFn(Func<object[], object> fn) : base(-1, -1) { _fn = fn; }
        public override object __hx_invokeDynamic(object[] args) => _fn(args ?? System.Array.Empty<object>());
        public static object Arg(object[] a, int i) => a != null && i < a.Length ? a[i] : null;
    }

    /// <summary>Psych HScript extends Iris with origin / filePath / modFolder / parentLua ('this.origin' in scripts).</summary>
    public sealed class PsychIris : Iris
    {
        public string origin, filePath, modFolder; public object parentLua;
        public PsychIris(string code, object config) : base(code, config) { }
        public override object __hx_getField(string field, int hash, bool throwErrors, bool isCheck, bool handleProperties)
        {
            switch (field)
            {
                case "origin": return origin;
                case "filePath": return filePath;
                case "modFolder": return modFolder;
                case "parentLua": return parentLua;
            }
            return base.__hx_getField(field, hash, throwErrors, isCheck, handleProperties);
        }
    }

    /// <summary>
    /// Psych Engine 1.0 HScript (source/psychlua/HScript.hx) on top of the ORIGINAL hscript-iris 1.1.3
    /// interpreter, compiled from Haxe to C# (HScriptIris_CS). Pure managed code, no JIT/Emit, so the
    /// same runtime is used by the desktop and UWP/Xbox builds.
    ///
    /// Construction mirrors Psych: Iris(code, IrisConfig(name, autoRun:false, autoPreset:false)) ->
    /// interp = CustomInterp{ parentInstance = PlayState } -> preset() -> execute().
    /// </summary>
    public class PsychHScriptEngine
    {
        private readonly object _game;
        private readonly string _path;
        private Iris _iris;
        private readonly Dictionary<string, object> _pending = new();

        public string ScriptName => _path;
        /// <summary>HScript.parentLua: the FunkinLua that owns this interpreter (runHaxeCode), null for .hx files.</summary>
        public object ParentLua { get; set; }
        public bool Loaded => _iris != null;

        private static IRIS::haxe.lang.Null<bool> NB(bool v) => new IRIS::haxe.lang.Null<bool>(v, true);

        public PsychHScriptEngine(object game, string path)
        {
            _game = game;
            _path = path;
        }

        /// <summary>Load and run the script file (top-level code runs; functions become callable).</summary>
        public bool Load()
        {
            if (string.IsNullOrEmpty(_path) || !global::FNF_MonoGame.Engine.FsCI.FileExists(_path)) return false;
            return Init(global::FNF_MonoGame.Engine.FsCI.ReadAllText(_path), _path);
        }

        private bool Init(string code, string name)
        {
            try
            {
                var config = new IrisConfig(name, NB(false), NB(false), new IRIS::Array<string>(System.Array.Empty<string>()));
                var pi = new PsychIris(code, config);
                // HScript.origin: the parent Lua's scriptName for runHaxeCode, else the .hx path (game-relative, '/')
                pi.origin = ParentLua is FunkinLua pl ? FNF_MonoGame.Scenes.PlayScene.PsychRelPath(pl.ScriptName) : FNF_MonoGame.Scenes.PlayScene.PsychRelPath(name);
                pi.filePath = pi.origin; pi.parentLua = ParentLua;
                pi.modFolder = PsychModFolderOf(pi.origin);
                _iris = pi;
                var interp = new IRIS::psychlua.CustomInterp();
                interp.set_parentInstance(_game);
                interp.showPosOnLog = false;
                _iris.interp = interp;
                _iris.preset();
                Preset();
                foreach (var kv in _pending) _iris.@set(kv.Key, kv.Value, NB(true));
                _pending.Clear();
                _lastReturn = _iris.execute();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PSYCH-HSCRIPT] Error in {name}: {Describe(ex)}");
                _iris = null;
                return false;
            }
        }

        /// <summary>Psych HScript.preset(): the variables and helpers every Psych .hx script can use.</summary>
        private void Preset()
        {
            void Set(string n, object v) => _iris.@set(n, v, NB(true));

            Set("game", _game);
            Set("this", _iris);
            PsychHScriptPresets.Apply(Set, this);
            Set("buildTarget",
#if XBOX_UWP
                "xbox"
#else
                "windows"
#endif
            );

            Set("Function_Stop", FunkinLua.Function_Stop);
            Set("Function_Continue", FunkinLua.Function_Continue);
            Set("Function_StopLua", FunkinLua.Function_StopLua);
            Set("Function_StopHScript", FunkinLua.Function_StopHScript);
            Set("Function_StopAll", FunkinLua.Function_StopAll);

            // Classes (C# equivalents of the Psych/Flixel classes in HScript.preset)
            Set("FlxSprite", typeof(FNF_MonoGame.Scenes.HxFlxSprite));
            // flixel classes Psych's HScript.preset() exposes (C# shims in Scenes/PlayScene.HxFlixel.cs)
            Set("FlxG", typeof(FNF_MonoGame.Scenes.HxFlxG));
            Set("FlxCamera", typeof(FNF_MonoGame.Scenes.HxFlxCamera));
            Set("FlxTimer", typeof(FNF_MonoGame.Scenes.HxFlxTimer));
            Set("FlxTween", typeof(FNF_MonoGame.Scenes.HxFlxTween));
            Set("FlxEase", typeof(FNF_MonoGame.Scenes.HxFlxEase));
            Set("FlxColor", typeof(FNF_MonoGame.Scenes.HxFlxColor));
            Set("StringTools", typeof(FNF_MonoGame.Scenes.HxStringTools));
            RegisterProxyImports();
            Set("PlayState", typeof(FNF_MonoGame.Scenes.PlayScene));
            Set("Character", typeof(FNF_MonoGame.Gameplay.Character));
            Set("ClientPrefs", typeof(ClientPrefs));
            Set("Paths", typeof(PsychPaths));
            Set("Conductor", typeof(PsychConductor));

            Set("debugPrint", new HxFn(a =>
            {
                Console.WriteLine($"[HSCRIPT-PRINT] {HxFn.Arg(a, 0)}");
                return null;
            }));
            Set("setVar", new HxFn(a =>
            {
                string n = HxFn.Arg(a, 0)?.ToString();
                if (n != null) MusicBeatState.Variables[n] = HxFn.Arg(a, 1);
                return HxFn.Arg(a, 1);
            }));
            Set("getVar", new HxFn(a =>
            {
                string n = HxFn.Arg(a, 0)?.ToString();
                return n != null && MusicBeatState.Variables.TryGetValue(n, out var v) ? v : null;
            }));
            Set("removeVar", new HxFn(a =>
            {
                string n = HxFn.Arg(a, 0)?.ToString();
                return n != null && MusicBeatState.Variables.Remove(n);
            }));

            // FlxState.add/remove/insert (Psych resolves these on the PlayState via parentInstance)
            if (_game is FNF_MonoGame.Scenes.PlayScene ps)
            {
                Set("add", new HxFn(a => { ps.AddStageObject(HxFn.Arg(a, 0)); return HxFn.Arg(a, 0); }));
                Set("remove", new HxFn(a => { ps.RemoveStageObject(HxFn.Arg(a, 0)); return HxFn.Arg(a, 0); }));
                Set("insert", new HxFn(a =>
                {
                    ps.InsertStageObject(Convert.ToInt32(HxFn.Arg(a, 0) ?? 0), HxFn.Arg(a, 1));
                    return HxFn.Arg(a, 1);
                }));
            }
        }

        private static bool _proxied;
        /// <summary>import flixel.util.FlxTimer; etc. resolve to the C# shims (Iris.proxyImports).</summary>
        private static void RegisterProxyImports()
        {
            if (_proxied) return; _proxied = true;
            void P(string n, object t) { try { Iris.addProxyImport(n, t); } catch { } }
            P("flixel.FlxSprite", typeof(FNF_MonoGame.Scenes.HxFlxSprite));
            P("flixel.FlxG", typeof(FNF_MonoGame.Scenes.HxFlxG));
            P("flixel.FlxCamera", typeof(FNF_MonoGame.Scenes.HxFlxCamera));
            P("flixel.util.FlxTimer", typeof(FNF_MonoGame.Scenes.HxFlxTimer));
            P("flixel.tweens.FlxTween", typeof(FNF_MonoGame.Scenes.HxFlxTween));
            P("flixel.tweens.FlxEase", typeof(FNF_MonoGame.Scenes.HxFlxEase));
            P("flixel.util.FlxColor", typeof(FNF_MonoGame.Scenes.HxFlxColor));
            P("StringTools", typeof(FNF_MonoGame.Scenes.HxStringTools));
            P("openfl.geom.Matrix", typeof(FNF_MonoGame.Scenes.HxMatrix));
            P("flixel.math.FlxMath", typeof(FNF_MonoGame.Scenes.HxFlxMath));
            P("backend.ClientPrefs", typeof(ClientPrefs));
            P("backend.Paths", typeof(PsychPaths));
            P("states.PlayState", typeof(FNF_MonoGame.Scenes.PlayScene));
            P("objects.Character", typeof(FNF_MonoGame.Gameplay.Character));
            PsychHScriptPresets.RegisterProxies(P);
        }

        public bool Exists(string func) => _iris != null && _iris.exists(func);

        /// <summary>Call a script function if it exists (Psych HScript.call). Returns its return value.</summary>
        public object Call(string func, params object[] args)
        {
            if (_iris == null || string.IsNullOrEmpty(func) || !_iris.exists(func)) return null;
            try
            {
                var arr = new IRIS::Array<object>(NormalizeArgs(args));
                return _iris.call(func, arr)?.returnValue;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PSYCH-HSCRIPT] {func} error in {_path}: {Describe(ex)}");
                return null;
            }
        }

        public object CallHaxeFunction(string func, params object[] args) => Call(func, args);

        /// <summary>Lua runHaxeCode(): first call initialises the shared interpreter, later calls run in it.</summary>
        private object _lastReturn;

        private static string PsychModFolderOf(string rel)
        {
            var parts = (rel ?? "").Split('/');
            return parts.Length > 2 && parts[0] == "mods" ? parts[1] : null;
        }

        public object RunCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            if (_iris == null)
            {
                Init(code, string.IsNullOrEmpty(_path) ? "runHaxeCode" : _path);
                return _lastReturn; // Psych returns hscript.returnValue (last expression)
            }
            try
            {
                var expr = _iris.parser.parseString(code, _path ?? "runHaxeCode");
                return _iris.interp.execute(expr);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PSYCH-HSCRIPT] runHaxeCode error: {Describe(ex)}");
                return null;
            }
        }

        public void SetVariable(string name, object value)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (_iris == null) _pending[name] = value;
            else _iris.@set(name, value, NB(true));
        }

        public object GetVariable(string name) => _iris != null && _iris.exists(name) ? _iris.@get(name) : null;

        public void Destroy()
        {
            try { _iris?.destroy(); } catch { }
            _iris = null;
        }

        // Haxe Float is double; C# float args would not unify with Haxe Float arithmetic.
        private static object[] NormalizeArgs(object[] args)
        {
            if (args == null) return System.Array.Empty<object>();
            var r = new object[args.Length];
            for (int i = 0; i < args.Length; i++)
                r[i] = args[i] is float f ? (double)f : args[i];
            return r;
        }

        private static string Describe(Exception ex)
        {
            if (ex is IRIS::haxe.Exception hx) return hx.get_message() ?? hx.ToString();
            return ex.InnerException != null ? $"{ex.Message} ({ex.InnerException.Message})" : ex.Message;
        }
    }
}
