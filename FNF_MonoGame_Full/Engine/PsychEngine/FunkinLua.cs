using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.PureHScript;
using FNF_MonoGame.Gameplay;
using FNF_MonoGame.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MoonSharp.Interpreter;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Full Psych Engine FunkinLua runtime — complete Lua scripting API.
/// Direct port of psychlua/FunkinLua.hx.
/// Uses MoonSharp (pure C# Lua interpreter) for Xbox compatibility.
/// </summary>
public class FunkinLua
{
    public const string Function_Stop = "##PSYCHLUA_FUNCTIONSTOP";

    /// <summary>
    /// Psych-parity Lua trace logger. Mirrors <c>FunkinLua.luaTrace()</c> from
    /// psychlua/FunkinLua.hx — mods and psychlua/* helpers call this to surface
    /// warnings/errors. In Psych it can also print to the on-screen debug console;
    /// in this port it routes to stdout with a consistent tag so log tooling can filter.
    /// </summary>
    /// <param name="message">The message to log.</param>
    /// <param name="ignoreCheck">Kept for API compatibility with Psych; unused here.</param>
    /// <param name="deprecated">Kept for API compatibility; deprecation warnings share the same channel.</param>
    public static void LuaTrace(string message, bool ignoreCheck = false, bool deprecated = false)
    {
        string tag = deprecated ? "LUA-DEPRECATED" : "LUA-TRACE";
        Console.WriteLine($"[{tag}] {message}");
    }
    public const string Function_Continue = "##PSYCHLUA_FUNCTIONCONTINUE";
    public const string Function_StopLua = "##PSYCHLUA_FUNCTIONSTOPLUA";
    public const string Function_StopHScript = "##PSYCHLUA_FUNCTIONSTOPHSCRIPT";
    public const string Function_StopAll = "##PSYCHLUA_FUNCTIONSTOPALL";

    private readonly Script _lua;
    private readonly PlayScene _game;
    public Script LuaScript => _lua;
    public PlayScene GameScene => _game;
    public string ScriptName { get; }
    public string ModFolder { get; private set; }
    public bool Closed { get; private set; }

    // All running Lua scripts
    public static List<FunkinLua> RunningScripts { get; } = new();

    // State variables shared between scripts
    private static readonly Dictionary<string, object> _stateVariables = new(StringComparer.Ordinal);

    private readonly PsychHScriptEngine _hscript;

    static FunkinLua()
    {
        // Psych/Haxe callbacks have optional parameters (e.g. addLuaText(tag) without `front`,
        // makeLuaSprite(tag) without x/y). MoonSharp cannot convert a missing (void) or nil argument
        // to a C# value type and throws "cannot convert a void to a clr type System.Boolean",
        // aborting the calling Lua function. Map missing/nil to the C# default, matching Haxe's
        // behaviour for omitted optional arguments.
        var conv = Script.GlobalOptions.CustomConverters;
        foreach (var dt in new[] { DataType.Void, DataType.Nil })
        {
            conv.SetScriptToClrCustomConversion(dt, typeof(bool), _ => false);
            conv.SetScriptToClrCustomConversion(dt, typeof(int), _ => 0);
            conv.SetScriptToClrCustomConversion(dt, typeof(long), _ => 0L);
            conv.SetScriptToClrCustomConversion(dt, typeof(float), _ => 0f);
            conv.SetScriptToClrCustomConversion(dt, typeof(double), _ => 0d);
        }
    }

    public FunkinLua(PlayScene game, string scriptPath)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
        ScriptName = scriptPath?.Trim() ?? "";
        _lua = new Script(CoreModules.Preset_Complete); // Psych: LuaL.openlibs (load/loadstring/dofile/require/io/os like real Lua)
        _hscript = new PsychHScriptEngine(_game, "runHaxeCode");
        _hscript.ParentLua = this; // HScript.parentLua

        RunningScripts.Add(this);
        RegisterAllCallbacks();
        SetPresetVariables();
    }

    public bool Load()
    {
        if (!global::FNF_MonoGame.Engine.FsCI.FileExists(ScriptName))
        {
            Console.WriteLine($"[FUNKIN-LUA] Script not found: {ScriptName}");
            return false;
        }

        try
        {
            string code = global::FNF_MonoGame.Engine.FsCI.ReadAllText(ScriptName);
            _lua.DoString(code, null, System.IO.Path.GetFileName(ScriptName));
            Console.WriteLine($"[FUNKIN-LUA] Loaded: {ScriptName}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FUNKIN-LUA] Error in {ScriptName}: {(ex as InterpreterException)?.DecoratedMessage ?? ex.Message}");
            return false;
        }
    }

    public DynValue Call(string funcName, params object[] args)
    {
        if (Closed) return DynValue.Nil;
        try
        {
            var func = _lua.Globals.Get(funcName);
            if (func.Type == DataType.Function)
                return args == null || args.Length == 0 ? _lua.Call(func) : _lua.Call(func, args);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FUNKIN-LUA] {funcName} error in {ScriptName}: {(ex as InterpreterException)?.DecoratedMessage ?? ex.Message}");
        }
        return DynValue.Nil;
    }

    /// <summary>FunkinLua.addLocalCallback (HScript createCallback): a callback only this script sees.</summary>
    public void AddLocalCallback(string name, Func<object[], object> callback)
    {
        if (string.IsNullOrEmpty(name) || callback == null || Closed) return;
        _lua.Globals[name] = DynValue.NewCallback((ctx, args) =>
        {
            var list = new object[args.Count];
            for (int i = 0; i < args.Count; i++) list[i] = args[i].ToObject();
            var r = callback(list);
            return r != null ? DynValue.FromObject(_lua, r) : DynValue.Nil;
        });
    }

    public bool HasCallback(string funcName)
    {
        if (Closed) return false;
        var func = _lua.Globals.Get(funcName);
        return func.Type == DataType.Function;
    }

    public void Stop()
    {
        Closed = true;
        RunningScripts.Remove(this);
    }

    public static void ClearAll()
    {
        foreach (var script in RunningScripts.ToArray())
            script.Stop();
        RunningScripts.Clear();
        _stateVariables.Clear();
        _globalCallbacks.Clear();
    }

    private static readonly Dictionary<string, Func<object[], object>> _globalCallbacks = new(StringComparer.OrdinalIgnoreCase);

    public static void RegisterGlobalCallback(string name, Func<object[], object> callback)
    {
        if (string.IsNullOrEmpty(name) || callback == null) return;
        _globalCallbacks[name] = callback;
        foreach (var script in RunningScripts)
        {
            if (script.Closed) continue;
            script._lua.Globals[name] = DynValue.NewCallback((ctx, args) =>
            {
                var argList = new List<object>(args.Count);
                for (int i = 0; i < args.Count; i++)
                    argList.Add(args[i].ToObject());
                var result = callback(argList.ToArray());
                return result != null ? DynValue.FromObject(script._lua, result) : DynValue.Nil;
            });
        }
    }

    private void RegisterPendingGlobalCallbacks()
    {
        foreach (var kvp in _globalCallbacks)
        {
            string name = kvp.Key;
            var callback = kvp.Value;
            _lua.Globals[name] = DynValue.NewCallback((ctx, args) =>
            {
                var argList = new List<object>(args.Count);
                for (int i = 0; i < args.Count; i++)
                    argList.Add(args[i].ToObject());
                var result = callback(argList.ToArray());
                return result != null ? DynValue.FromObject(_lua, result) : DynValue.Nil;
            });
        }
    }

    private void Set(string name, object value)
    {
        if (value == null)
        {
            _lua.Globals[name] = DynValue.Nil;
            return;
        }
        try
        {
            _lua.Globals[name] = DynValue.FromObject(_lua, value);
        }
        catch (ScriptRuntimeException)
        {
            // MoonSharp only marshals CLR objects whose type is registered as UserData.
            // Unregistered types (PlayScene, sprite maps, note lists...) used to throw here and
            // abort the whole script during SetPresetVariables(), so NO Psych Lua script could load.
            try
            {
                if (!UserData.IsTypeRegistered(value.GetType()))
                    UserData.RegisterType(value.GetType());
                _lua.Globals[name] = DynValue.FromObject(_lua, value);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FUNKIN-LUA] Global '{name}' ({value.GetType().Name}) not exposed to Lua: {ex.Message}");
            }
        }
    }

    private void SetPresetVariables()
    {
        // Function return constants
        Set("Function_StopLua", Function_StopLua);
        Set("Function_StopHScript", Function_StopHScript);
        Set("Function_StopAll", Function_StopAll);
        Set("Function_Stop", Function_Stop);
        Set("Function_Continue", Function_Continue);
        Set("luaDebugMode", false);
        Set("luaDeprecatedWarnings", true);
        Set("version", PsychGlobals.PsychEngineVersion);
        Set("modFolder", ModFolder);

        // Song/Week info
        Set("curBpm", PsychGlobals.CurrentBPM);
        Set("bpm", PsychGlobals.CurrentBPM);
        // FunkinLua.new: crochet / stepCrochet (ms) of the current BPM (scripts time tweens with stepCrochet * 0.001)
        Set("crochet", PsychGlobals.CurrentBPM > 0 ? 60000.0 / PsychGlobals.CurrentBPM : 0.0);
        Set("stepCrochet", PsychGlobals.CurrentBPM > 0 ? 15000.0 / PsychGlobals.CurrentBPM : 0.0);
        Set("scrollSpeed", PsychGlobals.ChartScrollSpeed);
        Set("songName", PsychGlobals.CurrentSong);
        Set("songPath", Difficulty.FormatToSongPath(PsychGlobals.CurrentSong));
        Set("startedCountdown", false);
        Set("curStage", PsychGlobals.CurrentStage);

        Set("isStoryMode", PsychGlobals.IsStoryMode);
        Set("difficulty", PsychGlobals.StoryDifficulty);
        Set("difficultyName", Difficulty.GetString());
        Set("difficultyPath", Difficulty.GetFilePath());
        Set("weekRaw", PsychGlobals.StoryWeek);
        Set("seenCutscene", PsychGlobals.SeenCutscene);

        // Screen
        Set("screenWidth", 1280);
        Set("screenHeight", 720);

        // PlayState variables
        Set("curSection", 0);
        Set("curBeat", 0);
        Set("curStep", 0);
        Set("curDecBeat", 0.0);
        Set("curDecStep", 0.0);

        Set("score", 0);
        Set("misses", 0);
        Set("hits", 0);
        Set("combo", 0);
        Set("deaths", PsychGlobals.DeathCounter);
        Set("rating", 0.0);
        Set("ratingName", "?");
        Set("ratingFC", "");

        Set("inGameOver", false);
        Set("mustHitSection", false);
        Set("altAnim", false);
        Set("gfSection", false);

        Set("healthGainMult", ClientPrefs.Data.HealthGain);
        Set("healthLossMult", ClientPrefs.Data.HealthLoss);
        Set("playbackRate", 1.0);
        Set("guitarHeroSustains", ClientPrefs.Data.GuitarHeroSustains);
        Set("instakillOnMiss", ClientPrefs.Data.Instakill);
        Set("botPlay", ClientPrefs.Data.Botplay);
        Set("practice", ClientPrefs.Data.Practice);

        // Default strum positions
        for (int i = 0; i < 4; i++)
        {
            Set("defaultPlayerStrumX" + i, 0);
            Set("defaultPlayerStrumY" + i, 0);
            Set("defaultOpponentStrumX" + i, 0);
            Set("defaultOpponentStrumY" + i, 0);
        }

        // Character positions
        Set("defaultBoyfriendX", 770.0);
        Set("defaultBoyfriendY", 100.0);
        Set("defaultOpponentX", 100.0);
        Set("defaultOpponentY", 100.0);
        Set("defaultGirlfriendX", 400.0);
        Set("defaultGirlfriendY", 130.0);

        // Settings
        Set("downscroll", ClientPrefs.Data.DownScroll);
        Set("middlescroll", ClientPrefs.Data.MiddleScroll);
        Set("framerate", ClientPrefs.Data.Framerate);
        Set("ghostTapping", ClientPrefs.Data.GhostTapping);
        Set("hideHud", ClientPrefs.Data.HideHud);
        Set("timeBarType", ClientPrefs.Data.TimeBarType);
        Set("scoreZoom", ClientPrefs.Data.ScoreZoom);
        Set("cameraZoomOnBeat", ClientPrefs.Data.CamZooms);
        Set("flashingLights", ClientPrefs.Data.Flashing);
        Set("noteOffset", ClientPrefs.Data.NoteOffset);
        Set("healthBarAlpha", ClientPrefs.Data.HealthBarAlpha);
        Set("noResetButton", ClientPrefs.Data.NoReset);
        Set("lowQuality", ClientPrefs.Data.LowQuality);
        Set("shadersEnabled", ClientPrefs.Data.Shaders);
        Set("scriptName", ScriptName);

        Set("noteSkin", ClientPrefs.Data.NoteSkin);
        Set("splashSkin", ClientPrefs.Data.SplashSkin);
        Set("splashAlpha", ClientPrefs.Data.SplashAlpha);

        Set("buildTarget", "windows");

        // Psych Engine exposes the PlayState as "game" and its modchartSprites map
        Set("game", _game);
        Set("modchartSprites", _game.ModchartSprites);
        Set("notes", _game.Notes);
        Set("sustains", _game.Sustains);
        _game.PsychPresetVars(Set, ScriptName); // FunkinLua.new values that need the PlayState (PlayScene.PsychScriptApi.cs)
    }

    private void RegisterAllCallbacks()
    {
        // ═══════════════════════════════════════════
        // Variables
        // ═══════════════════════════════════════════
        _lua.Globals["setVar"] = (Func<string, DynValue, DynValue>)((name, value) =>
        {
            _stateVariables[name] = value?.ToObject();
            return value;
        });
        _lua.Globals["getVar"] = (Func<string, DynValue>)((name) =>
        {
            _stateVariables.TryGetValue(name, out var val);
            return val != null ? DynValue.FromObject(_lua, val) : DynValue.Nil;
        });

        // ═══════════════════════════════════════════
        // Debug
        // ═══════════════════════════════════════════
        _lua.Globals["debugPrint"] = (Action<DynValue, DynValue>)((text, color) =>
        {
            Console.WriteLine($"[LUA-PRINT] {text?.CastToString() ?? "nil"}");
        });
        _lua.Globals["close"] = (Action)(() => Stop());

        // ═══════════════════════════════════════════
        // Sprites
        // ═══════════════════════════════════════════
        RegisterSpriteCallbacks();

        // ═══════════════════════════════════════════
        // Text
        // ═══════════════════════════════════════════
        RegisterTextCallbacks();

        // ═══════════════════════════════════════════
        // Tweens & Timers
        // ═══════════════════════════════════════════
        RegisterTweenCallbacks();

        // ═══════════════════════════════════════════
        // Sound
        // ═══════════════════════════════════════════
        RegisterSoundCallbacks();

        // ═══════════════════════════════════════════
        // Properties (getProperty / setProperty)
        // ═══════════════════════════════════════════
        RegisterPropertyCallbacks();

        // ═══════════════════════════════════════════
        // Camera
        // ═══════════════════════════════════════════
        RegisterCameraCallbacks();

        // ═══════════════════════════════════════════
        // Generic object properties
        // ═══════════════════════════════════════════
        RegisterObjectCallbacks();

        // ═══════════════════════════════════════════
        // Input
        // ═══════════════════════════════════════════
        RegisterInputCallbacks();

        // ═══════════════════════════════════════════
        // Utility / Math
        // ═══════════════════════════════════════════
        RegisterUtilCallbacks();

        // ═══════════════════════════════════════════
        // Modchart (noteTween*, group/class properties, precache)
        // ═══════════════════════════════════════════
        RegisterModchartCallbacks();

        // ═══════════════════════════════════════════
        // Custom substates
        // ═══════════════════════════════════════════
        RegisterSubstateCallbacks();

        // ═══════════════════════════════════════════
        // Achievements
        // ═══════════════════════════════════════════
        RegisterAchievementCallbacks();

        // ═══════════════════════════════════════════
        // Script management
        // ═══════════════════════════════════════════
        RegisterScriptManagementCallbacks();

        // ═══════════════════════════════════════════
        // Gameplay state helpers
        // ═══════════════════════════════════════════
        RegisterGameplayCallbacks();

        // ═══════════════════════════════════════════
        // Psych Engine reflection / deprecated / shader / animate modules
        // ═══════════════════════════════════════════
        ReflectionFunctions.Implement(this);
        DeprecatedFunctions.Implement(this);
        FlxAnimateFunctions.Implement(this);
        ShaderFunctions.Implement(this);
        // ExtraFunctions: Psych-parity save-data + file mgmt + gamepad wildcards
        // (initSaveData / flushSaveData / getDataFromSave / setDataFromSave /
        //  eraseSaveData / checkFileExists / saveFile / deleteFile / anyGamepad*).
        // Must be after Reflection/Deprecated so its names take priority.
        PsychLua.ExtraFunctions.Implement(this); PsychLua.MissingFunctions.Implement(this);
        RegisterPendingGlobalCallbacks();
    }

    private void RegisterSpriteCallbacks()
    {
        var sprites = _game.ModchartSprites;

        _lua.Globals["makeLuaSprite"] = (Action<string, string, double, double>)((tag, image, x, y) =>
        {
            if (string.IsNullOrEmpty(tag)) return;
            // Psych Engine creates the sprite even when the image is missing;
            // the script can call makeGraphic later to fill it.
            Texture2D tex = null;
            if (!string.IsNullOrEmpty(image))
            {
                string fullPath = $"images/{image}";
                string resolved = _game.AssetManager?.ResolvePath(fullPath + ".png");
                if (resolved == null)
                {
                    Console.WriteLine($"[FUNKIN-LUA] makeLuaSprite: image not found: {fullPath} (creating empty sprite)");
                }
                else
                {
                    try
                    {
                        tex = Texture2D.FromFile(FNFGame.Instance.GraphicsDevice, global::FNF_MonoGame.Engine.FsCI.Fix(resolved));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[FUNKIN-LUA] makeLuaSprite failed: {ex.Message}");
                    }
                }
            }
            sprites[tag] = new PlayScene.StaticStageProp
            {
                Name = tag, Texture = tex, X = (float)x, Y = (float)y,
                ScaleX = 1f, ScaleY = 1f, Alpha = 1f,
                ScrollX = 1f, ScrollY = 1f, ZIndex = 0, Blend = null,
                PsychOffsetX = 0f, PsychOffsetY = 0f
            };
            MusicBeatState.Variables[tag] = sprites[tag];
        });

        _lua.Globals["makeAnimatedLuaSprite"] = (Action<string, string, double, double>)((tag, image, x, y) =>
        {
            if (string.IsNullOrEmpty(tag)) return;
            SpriteSheet sheet = null;
            string fullPath = $"images/{image}";
            try
            {
                sheet = SpriteSheet.Load(FNFGame.Instance, fullPath);
                if (sheet == null)
                    Console.WriteLine($"[FUNKIN-LUA] makeAnimatedLuaSprite: image not found: {fullPath}.png (creating empty sprite)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FUNKIN-LUA] makeAnimatedLuaSprite failed: {ex.Message}");
            }

            var sprite = new ModchartSprite((float)x, (float)y)
            {
                Name = tag,
                SpriteSheet = sheet,
                Sheet = sheet,
                Texture = sheet?.Texture,
                ScaleX = 1f, ScaleY = 1f, Alpha = 1f,
                ScrollX = 1f, ScrollY = 1f, ZIndex = 0, Blend = null,
                PsychOffsetX = 0f, PsychOffsetY = 0f
            };
            sprites[tag] = sprite;
            MusicBeatState.Variables[tag] = sprite;
        });

        _lua.Globals["addAnimationByPrefix"] = (Action<string, string, string, int, bool>)((tag, name, prefix, framerate, loop) =>
        {
            if (sprites.TryGetValue(tag, out var obj) && obj is ModchartSprite sprite)
                sprite.AddAnimationByPrefix(name, prefix, framerate, loop);
            else if (obj is PlayScene.StaticStageProp prop && prop.Sheet != null)
                AddStaticPropAnimationByPrefix(prop, name, prefix, framerate, loop);
        });

        _lua.Globals["addAnimationByIndices"] = (Action<string, string, string, string, int, bool>)((tag, name, prefix, indices, framerate, loop) =>
        {
            var arr = LuaUtils.StringToIntArray(indices);
            if (sprites.TryGetValue(tag, out var obj) && obj is ModchartSprite sprite)
                sprite.AddAnimationByIndices(name, prefix, arr, framerate, loop);
            else if (obj is PlayScene.StaticStageProp prop && prop.Sheet != null)
                AddStaticPropAnimationByIndices(prop, name, prefix, arr, framerate, loop);
        });

        _lua.Globals["playAnim"] = (Action<string, string, bool, bool, int>)((tag, name, forced, reverse, startFrame) =>
        {
            if (sprites.TryGetValue(tag, out var obj) && obj is ModchartSprite sprite)
            {
                sprite.PlayAnim(name, forced, reverse, startFrame);
                if (sprite.PsychAnimOffsets != null && name != null && sprite.PsychAnimOffsets.TryGetValue(name, out var mo)) { sprite.PsychOffsetX = mo.X; sprite.PsychOffsetY = mo.Y; } // ModchartSprite.playAnim
            }
            else if (obj is PlayScene.StaticStageProp prop)
            {
                PlayStaticPropAnimation(prop, name, forced, reverse, startFrame);
                if (prop.PsychAnimOffsets != null && name != null && prop.PsychAnimOffsets.TryGetValue(name, out var ao)) { prop.PsychOffsetX = ao.X; prop.PsychOffsetY = ao.Y; } // ModchartSprite.playAnim
            }
            else if (_game?.ResolvePsychObject(tag) is Gameplay.Character ch) ch.PlayAnimation(name, forced); // playAnim('dad', ...)
        });

        _lua.Globals["makeGraphic"] = (Action<string, int, int, string>)((tag, width, height, color) =>
        {
            if (string.IsNullOrEmpty(tag)) return;
            if (!sprites.TryGetValue(tag, out var propObj) || propObj is not PlayScene.StaticStageProp prop) return;
            try
            {
                var tex = new Texture2D(FNFGame.Instance.GraphicsDevice, width, height);
                var data = new Color[width * height];
                Color c = ParseHexColor(color);
                for (int i = 0; i < data.Length; i++) data[i] = c;
                tex.SetData(data);
                prop.Texture = tex;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FUNKIN-LUA] makeGraphic failed: {ex.Message}");
            }
        });
        _lua.Globals["makeLuaGraphic"] = _lua.Globals["makeGraphic"];

        _lua.Globals["addLuaSprite"] = (Action<string, bool>)((tag, front) =>
        {
            if (sprites.TryGetValue(tag, out var propObj) && propObj is PlayScene.StaticStageProp prop)
            {
                if (_game?.PsychOrderActive == true) _game.PsychOrderAdd(prop, front); // PlayState.members order
                else if (front) prop.ZIndex = 1000;
                _game?.AddStageObject(prop);
            }
        });

        _lua.Globals["removeLuaSprite"] = (Action<string, bool>)((tag, destroy) =>
        {
            if (sprites.TryGetValue(tag, out var propObj) && propObj is PlayScene.StaticStageProp prop)
            {
                _game?.RemoveStageObject(prop);
                _game?.PsychOrderRemove(prop);
                sprites.Remove(tag);
                MusicBeatState.Variables.Remove(tag);
            }
        });

        // FunkinLua setGraphicSize(obj, x, y = 0, updateHitbox = true) -> flixel FlxSprite.setGraphicSize: scale = size / frameSize,
        // a 0 side keeps the aspect (scale.y = scale.x or scale.x = scale.y), then updateHitbox.
        _lua.Globals["setGraphicSize"] = (Action<string, double, DynValue, DynValue>)((tag, width, heightV, upd) =>
        {
            double height = heightV != null && heightV.Type == DataType.Number ? heightV.Number : 0;
            bool doHitbox = upd == null || upd.IsNil() || upd.CastToBool();
            object o = sprites.TryGetValue(tag, out var po) ? po : _game?.ResolvePsychObject(tag);
            if (o is PlayScene.StaticStageProp prop)
            {
                var (fw, fh) = PlayScene.PsychFrameSize(prop);
                if (width <= 0 && height <= 0) return;
                double sx = fw > 0 ? width / fw : 0, sy = fh > 0 ? height / fh : 0;
                prop.ScaleX = (float)sx; prop.ScaleY = (float)sy;
                if (width <= 0) prop.ScaleX = (float)sy; else if (height <= 0) prop.ScaleY = (float)sx;
                if (doHitbox) PlayScene.PsychUpdateHitbox(prop);
            }
            else if (o is Character ch)
            {
                var ff = ch.FirstFrameSize();
                if (width <= 0 && height <= 0) return;
                double sx = ff.X > 0 ? width / ff.X : 0, sy = ff.Y > 0 ? height / ff.Y : 0;
                float nx = (float)sx, ny = (float)sy;
                if (width <= 0) nx = ny; else if (height <= 0) ny = nx;
                ch.SetPsychScale(nx, ny);
            }
        });

        _lua.Globals["scaleObject"] = (Action<string, double, double>)((tag, scaleX, scaleY) =>
        {
            if (sprites.TryGetValue(tag, out var propObj) && propObj is PlayScene.StaticStageProp prop)
            {
                prop.ScaleX = (float)scaleX;
                prop.ScaleY = (float)scaleY;
            }
        });

        _lua.Globals["setScrollFactor"] = (Action<string, double, double>)((tag, scrollX, scrollY) =>
        {
            if (sprites.TryGetValue(tag, out var propObj) && propObj is PlayScene.StaticStageProp prop)
            {
                prop.ScrollX = (float)scrollX;
                prop.ScrollY = (float)scrollY;
            }
        });

        _lua.Globals["setObjectCamera"] = (Action<string, string>)((tag, cam) =>
        {
            if (string.IsNullOrEmpty(tag)) return;
            if (sprites.TryGetValue(tag, out var propObj) && propObj is PlayScene.StaticStageProp prop)
                prop.Camera = cam?.ToLowerInvariant() ?? "game";
            else
                _game?.SetTextCamera(tag, cam);
        });

        _lua.Globals["screenCenter"] = (Action<string, string>)((tag, axis) =>
        {
            // FlxObject.screenCenter uses the hitbox (width/height; 0 for a sprite with no graphic yet)
            if (_game?.ResolvePsychObject(tag) is PlayScene.StaticStageProp prop)
            {
                var (w, h) = PlayScene.PsychPropSize(prop);
                if (string.IsNullOrEmpty(axis) || axis.ToUpper().Contains("X"))
                    prop.X = (1280 - w) / 2f;
                if (string.IsNullOrEmpty(axis) || axis.ToUpper().Contains("Y"))
                    prop.Y = (720 - h) / 2f;
            }
        });

        _lua.Globals["setBlendMode"] = (Action<string, string>)((tag, blend) =>
        {
            if (sprites.TryGetValue(tag, out var propObj) && propObj is PlayScene.StaticStageProp prop)
                prop.Blend = blend;
            else if (_game?.ResolvePsychObject(tag) is PsychHudObject hud)
                hud.blend = blend; // healthBar / timeBar (FlxSpriteGroup.blend -> children)
        });

        _lua.Globals["setObjectOrder"] = (Action<string, int>)((obj, pos) =>
        {
            if (_game?.PsychOrderActive == true && _game.PsychSetObjectOrder(obj, pos)) return;
            if (sprites.TryGetValue(obj, out var propObj) && propObj is PlayScene.StaticStageProp prop)
            {
                prop.ZIndex = pos;
                _game?.SortStaticProps();
            }
            else
            {
                _game?.SetTextOrder(obj, pos);
            }
        });
        _lua.Globals["getObjectOrder"] = (Func<string, int>)((obj) =>
        {
            if (_game?.PsychOrderActive == true && _game.PsychGetObjectOrder(obj) is int po) return po;
            if (sprites.TryGetValue(obj, out var propObj) && propObj is PlayScene.StaticStageProp prop) return prop.ZIndex;
            return _game?.GetTextOrder(obj) ?? 0;
        });

        _lua.Globals["updateHitbox"] = (Action<string>)((tag) =>
        {
            // Psych: sprite.updateHitbox() — our props derive size from Texture * scale,
            // so recalculation is implicit. Reset offsets to match Psych behavior.
            if (sprites.TryGetValue(tag, out var propObj) && propObj is PlayScene.StaticStageProp prop && prop.Texture != null)
            {
                PlayScene.PsychUpdateHitbox(prop);
            }
        });

        // FunkinLua.hx 701-704: FlxColor.fromString(...) -> Int (ARGB, 6 hex digits = opaque); a Color struct
        // could not be stored in Lua ("cannot convert clr type Color" aborted setProperty('x.color', getColorFromHex(..))).
        _lua.Globals["getColorFromHex"] = (Func<string, DynValue>)((color) => FlxColorFromString("#" + color));
        _lua.Globals["FlxColor"] = (Func<string, DynValue>)((color) => FlxColorFromString(color));
        _lua.Globals["getColorFromName"] = (Func<string, DynValue>)((color) => FlxColorFromString(color));
        _lua.Globals["getColorFromString"] = (Func<string, DynValue>)((color) => FlxColorFromString(color));
    }

    /// <summary>flixel FlxColor.fromString: '0x'/'#' + 6 (opaque) or 8 (ARGB) hex digits, or a colour name; null otherwise.</summary>
    public static DynValue FlxColorFromString(string str)
    {
        if (str == null) return DynValue.Nil;
        string s = str.Trim();
        string hex = null;
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = s.Substring(2);
        else if (s.StartsWith("#")) hex = s.Substring(1);
        if (hex != null)
        {
            if ((hex.Length == 6 || hex.Length == 8) && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint v))
                return DynValue.NewNumber(unchecked((int)(hex.Length == 6 ? 0xFF000000u | v : v)));
            return DynValue.Nil;
        }
        switch (s.ToLowerInvariant())
        {
            case "transparent": return DynValue.NewNumber(0);
            case "white": return DynValue.NewNumber(unchecked((int)0xFFFFFFFFu));
            case "gray": return DynValue.NewNumber(unchecked((int)0xFF808080u));
            case "black": return DynValue.NewNumber(unchecked((int)0xFF000000u));
            case "green": return DynValue.NewNumber(unchecked((int)0xFF008000u));
            case "lime": return DynValue.NewNumber(unchecked((int)0xFF00FF00u));
            case "yellow": return DynValue.NewNumber(unchecked((int)0xFFFFFF00u));
            case "orange": return DynValue.NewNumber(unchecked((int)0xFFFFA500u));
            case "red": return DynValue.NewNumber(unchecked((int)0xFFFF0000u));
            case "purple": return DynValue.NewNumber(unchecked((int)0xFF800080u));
            case "blue": return DynValue.NewNumber(unchecked((int)0xFF0000FFu));
            case "brown": return DynValue.NewNumber(unchecked((int)0xFF8B4513u));
            case "pink": return DynValue.NewNumber(unchecked((int)0xFFFFC0CBu));
            case "magenta": return DynValue.NewNumber(unchecked((int)0xFFFF00FFu));
            case "cyan": return DynValue.NewNumber(unchecked((int)0xFF00FFFFu));
        }
        return DynValue.Nil;
    }

    private void RegisterSubstateCallbacks()
    {
        _lua.Globals["openCustomSubstate"] = DynValue.NewCallback((ctx, args) =>
        {
            if (args.Count == 0 || args[0].Type != DataType.String) return DynValue.NewBoolean(false);
            string name = args[0].String;
            bool pauseGame = args.Count > 1 && args[1].CastToBool();
            // CustomSubstate.openCustomSubstate: pause the song first, then openSubState -> create (callOnScripts: Lua + HScript)
            _game?.PsychCustomSubstateOpened(pauseGame);
            CustomSubstate.OpenCustomSubstate(name, pauseGame, (func, a) => _game?.CallOnScripts(func, a));
            return DynValue.Nil; // Psych's openCustomSubstate returns nothing
        });

        _lua.Globals["closeCustomSubstate"] = (Func<bool>)(() =>
        {
            if (CustomSubstate.Instance == null) return false;
            _game?.PsychCustomSubstateClosed(); // PlayState.closeSubState: resume + onResume (before the destroy)
            return CustomSubstate.CloseCustomSubstate();
        });

        _lua.Globals["insertToCustomSubstate"] = DynValue.NewCallback((ctx, args) =>
        {
            if (args.Count == 0 || args[0].Type != DataType.String) return DynValue.Nil;
            string tag = args[0].String;
            int pos = args.Count > 1 && args[1].Type == DataType.Number ? (int)args[1].Number : -1;
            if (_game.ModchartSprites.TryGetValue(tag, out var obj) && obj is PlayScene.StaticStageProp prop)
                return DynValue.NewBoolean(CustomSubstate.InsertToCustomSubstate(prop, pos));
            if (_game?.GetLuaText(tag) is LuaText txt)
                return DynValue.NewBoolean(CustomSubstate.InsertToCustomSubstate(txt, pos));
            if (MusicBeatState.Variables.TryGetValue(tag, out var v) && (v is PlayScene.StaticStageProp || v is LuaText))
                return DynValue.NewBoolean(CustomSubstate.InsertToCustomSubstate(v, pos));
            return DynValue.NewBoolean(false);
        });
    }

    private void RegisterAchievementCallbacks()
    {
        _lua.Globals["unlockAchievement"] = (Func<string, bool>)((name) =>
            !string.IsNullOrEmpty(name) && Achievements.Unlock(name));

        _lua.Globals["isAchievementUnlocked"] = (Func<string, bool>)((name) =>
            !string.IsNullOrEmpty(name) && Achievements.IsUnlocked(name));

        _lua.Globals["getAchievementScore"] = (Func<string, float>)((name) =>
            string.IsNullOrEmpty(name) ? 0f : Achievements.GetScore(name));

        _lua.Globals["setAchievementScore"] = DynValue.NewCallback((ctx, args) =>
        {
            if (args.Count < 2 || args[0].Type != DataType.String || args[1].Type != DataType.Number)
                return DynValue.NewNumber(0);
            string name = args[0].String;
            float value = (float)args[1].Number;
            bool save = args.Count <= 2 || args[2].CastToBool();
            return DynValue.NewNumber(Achievements.SetScore(name, value, save));
        });

        _lua.Globals["addAchievementScore"] = DynValue.NewCallback((ctx, args) =>
        {
            if (args.Count < 2 || args[0].Type != DataType.String || args[1].Type != DataType.Number)
                return DynValue.NewNumber(0);
            string name = args[0].String;
            float value = (float)args[1].Number;
            bool save = args.Count <= 2 || args[2].CastToBool();
            return DynValue.NewNumber(Achievements.AddScore(name, value, save));
        });
    }

    private void RegisterTweenCallbacks()
    {
        // FunkinLua.oldTweenFunction: (tag, vars, value, duration, ease = 'linear') -> 'tween_<tag>'; onTweenCompleted(tag, vars)
        Func<string, string, double, double, string, string> T(string field) => (tag, vars, value, duration, ease) =>
        {
            string r = LuaTweenManager.StartTween(tag, field, value, duration, ease, vars);
            if (r == null && LuaTweenManager.ResolveTweenTarget(vars) == null) LuaTrace("doTween: Couldnt find object: " + vars, false, false);
            return r;
        };
        _lua.Globals["doTweenX"] = T("x");
        _lua.Globals["doTweenY"] = T("y");
        _lua.Globals["doTweenAlpha"] = T("alpha");
        _lua.Globals["doTweenAngle"] = T("angle");
        _lua.Globals["doTweenZoom"] = (Func<string, string, double, double, string, string>)((tag, camera, value, duration, ease) =>
        {
            switch ((camera ?? "").ToLowerInvariant())
            {
                case "camgame": case "game": camera = "camGame"; break;
                case "camhud": case "hud": camera = "camHUD"; break;
                case "camother": case "other": camera = "camOther"; break;
                default: if (!(LuaTweenManager.ResolveTweenTarget(camera) is PsychCamera)) camera = "camGame"; break;
            }
            return LuaTweenManager.StartTween(tag, "zoom", value, duration, ease, camera);
        });
        // doTweenColor(tag, vars, targetColor, duration, ease): FlxTween.color from the current colour + alpha
        _lua.Globals["doTweenColor"] = (Func<string, string, string, double, string, string>)((tag, vars, color, duration, ease) =>
            LuaTweenManager.StartColorTweenTo(tag, vars, color, duration, ease));

        _lua.Globals["cancelTween"] = (Action<string>)((tag) => LuaTweenManager.CancelTween(tag));
        // FunkinLua runTimer(tag, time = 1, loops = 1): an omitted loops must stay 1 (0 = infinite); returns 'timer_<tag>'
        _lua.Globals["runTimer"] = (Func<string, DynValue, DynValue, string>)((tag, time, loops) => LuaTweenManager.StartTimer(tag,
            time != null && time.Type == DataType.Number ? time.Number : 1.0,
            loops != null && loops.Type == DataType.Number ? (int)loops.Number : 1));
        _lua.Globals["cancelTimer"] = (Action<string>)((tag) => LuaTweenManager.CancelTimer(tag));
    }

    private void RegisterSoundCallbacks()
    {
        _lua.Globals["playSound"] = (Action<string, double, string>)((path, volume, tag) =>
        {
            FNFGame.Instance?.Audio?.PlaySound(path, (float)(volume > 0 ? volume : 1.0));
        });

        _lua.Globals["playMusic"] = (Action<string, double, bool>)((path, volume, loop) =>
        {
            FNFGame.Instance?.Audio?.PlayMusic(path, loop);
        });

        _lua.Globals["soundFadeIn"] = (Action<string, double, double, double>)
            ((tag, duration, fromVol, toVol) =>
            {
                var audio = FNFGame.Instance?.Audio;
                if (audio == null) return;
                // Empty tag = current music (matches Psych behavior)
                audio.MusicVolume = (float)fromVol;
                LuaTweenManager.StartSetterTween($"__soundfade_{tag}", fromVol, toVol, duration, "linear",
                    (v) => audio.MusicVolume = (float)v);
            });
        _lua.Globals["soundFadeOut"] = (Action<string, double, double>)
            ((tag, duration, toVol) =>
            {
                var audio = FNFGame.Instance?.Audio;
                if (audio == null) return;
                if (toVol <= 0)
                {
                    audio.FadeOutMusic((float)duration);
                }
                else
                {
                    LuaTweenManager.StartSetterTween($"__soundfade_{tag}", audio.MusicVolume, toVol, duration, "linear",
                        (v) => audio.MusicVolume = (float)v);
                }
            });
    }

    private static void AddStaticPropAnimationByPrefix(PlayScene.StaticStageProp prop, string name, string prefix, int framerate, bool loop)
    {
        if (prop.Sheet == null) return;
        prop.Animations ??= new Dictionary<string, ModchartAnimation>(System.StringComparer.OrdinalIgnoreCase);
        var anim = new ModchartAnimation
        {
            Name = name,
            Prefix = prefix,
            Framerate = framerate,
            Loop = loop
        };
        var frames = prop.Sheet.Frames
            .Where(f => f.Key.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
            .Select(f => f.Value)
            .OrderBy(f => ExtractFrameNumber(f.Name))
            .ToList();
        foreach (var frame in frames)
        {
            anim.FrameRects.Add(frame.SourceRect);
            anim.FrameOffsets.Add(frame.Offset);
            anim.FrameSizes.Add(new Point(frame.FrameWidth > 0 ? frame.FrameWidth : frame.SourceRect.Width, frame.FrameHeight > 0 ? frame.FrameHeight : frame.SourceRect.Height));
        }
        prop.Animations[name] = anim;
        // FunkinLua addAnimationByPrefix/Indices: play the first animation added (obj.animation.curAnim == null)
        if (prop.CurrentAnimation == null && anim.FrameRects.Count > 0) { prop.CurrentAnimation = name; prop.CurrentFrame = 0; prop.AnimTimer = 0f; prop.AnimPlaying = true; prop.AnimFramerate = framerate; prop.AnimLoop = loop; prop.SourceRect = anim.FrameRects[0]; }
    }

    private static void AddStaticPropAnimationByIndices(PlayScene.StaticStageProp prop, string name, string prefix, int[] indices, int framerate, bool loop)
    {
        if (prop.Sheet == null || indices == null) return;
        prop.Animations ??= new Dictionary<string, ModchartAnimation>(System.StringComparer.OrdinalIgnoreCase);
        var anim = new ModchartAnimation
        {
            Name = name,
            Prefix = prefix,
            Indices = indices,
            Framerate = framerate,
            Loop = loop
        };
        foreach (int index in indices)
        {
            var frame = FindFrame(prop.Sheet, prefix, index);
            if (frame != null)
            {
                anim.FrameRects.Add(frame.SourceRect);
                anim.FrameOffsets.Add(frame.Offset);
                anim.FrameSizes.Add(new Point(frame.FrameWidth > 0 ? frame.FrameWidth : frame.SourceRect.Width, frame.FrameHeight > 0 ? frame.FrameHeight : frame.SourceRect.Height));
            }
        }
        prop.Animations[name] = anim;
        // FunkinLua addAnimationByPrefix/Indices: play the first animation added (obj.animation.curAnim == null)
        if (prop.CurrentAnimation == null && anim.FrameRects.Count > 0) { prop.CurrentAnimation = name; prop.CurrentFrame = 0; prop.AnimTimer = 0f; prop.AnimPlaying = true; prop.AnimFramerate = framerate; prop.AnimLoop = loop; prop.SourceRect = anim.FrameRects[0]; }
    }

    private static void PlayStaticPropAnimation(PlayScene.StaticStageProp prop, string name, bool forced, bool reverse, int startFrame)
    {
        if (prop.Animations == null || !prop.Animations.TryGetValue(name, out var anim) || anim.FrameRects.Count == 0)
            return;
        if (!forced && prop.CurrentAnimation == name && prop.AnimPlaying)
            return;

        prop.CurrentAnimation = name;
        prop.AnimFramerate = anim.Framerate;
        prop.AnimLoop = anim.Loop;
        prop.AnimTimer = 0f;
        prop.AnimPlaying = true;
        prop.CurrentFrame = reverse ? Math.Max(0, anim.FrameRects.Count - 1 - startFrame) : Math.Max(0, startFrame);
        prop.SourceRect = anim.FrameRects[prop.CurrentFrame];
        prop.Texture = prop.Sheet?.Texture;
    }

    private static SpriteFrame FindFrame(SpriteSheet sheet, string prefix, int index)
    {
        foreach (string candidate in new[] { $"{prefix}{index}", $"{prefix}{index:D4}", $"{prefix}{index:D3}", $"{prefix}{index:D2}", $"{prefix}{index:D1}" })
        {
            if (sheet.Frames.TryGetValue(candidate, out var frame))
                return frame;
            foreach (var kvp in sheet.Frames)
            {
                if (kvp.Key.Equals(candidate, System.StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }
        }
        return null;
    }

    private static int ExtractFrameNumber(string frameName)
    {
        if (string.IsNullOrEmpty(frameName)) return 0;
        var match = Regex.Match(frameName, @"(\d+)$");
        return match.Success && int.TryParse(match.Value, out var n) ? n : 0;
    }

    private void RegisterPropertyCallbacks()
    {
        _lua.Globals["getProperty"] = (Func<string, DynValue>)((name) =>
        {
            var val = _game?.GetStageVariable(name);
            return val != null ? DynValue.FromObject(_lua, val) : DynValue.Nil;
        });

        _lua.Globals["setProperty"] = (Action<string, DynValue>)((name, value) =>
        {
            _game?.SetStageVariable(name, value?.ToObject());
        });
    }

    private static PsychCamera GetCamera(string name)
    {
        return name?.ToLower() switch
        {
            "camhud" or "hud" => PsychCamera.HudCam,
            "camother" or "other" => PsychCamera.OtherCam,
            _ => PsychCamera.GameCam
        };
    }

    private void RegisterCameraCallbacks()
    {
        _lua.Globals["cameraSetTarget"] = (Action<string>)((target) =>
        {
            // Psych: moveCamera(target == 'dad') — flip section focus on the PlayScene
            bool isDad = target != null && (target.ToLower() == "dad" || target.ToLower() == "opponent");
            _game?.SetStageVariable("cameraTarget", isDad ? "dad" : "boyfriend");
        });

        _lua.Globals["cameraShake"] = (Action<string, double, double>)((cam, intensity, duration) =>
            GetCamera(cam).Shake((float)intensity, (float)duration));

        _lua.Globals["cameraFlash"] = (Action<string, string, double, bool>)((cam, color, duration, forced) =>
            GetCamera(cam).Flash(LuaUtils.ColorFromString(color), (float)duration, forced));

        _lua.Globals["cameraFade"] = (Action<string, string, double, bool>)((cam, color, duration, forced) =>
            GetCamera(cam).Fade(LuaUtils.ColorFromString(color), (float)duration, false, forced));

        _lua.Globals["setCameraZoom"] = (Action<string, double>)((cam, zoom) => GetCamera(cam).Zoom = (float)zoom);
        _lua.Globals["getCameraZoom"] = (Func<string, double>)((cam) => GetCamera(cam).Zoom);
        _lua.Globals["cameraGetZoom"] = (Func<string, double>)((cam) => GetCamera(cam).Zoom);
        _lua.Globals["cameraSetZoom"] = (Action<string, double>)((cam, zoom) => GetCamera(cam).Zoom = (float)zoom);
        _lua.Globals["addCameraZoom"] = (Action<string, double>)((cam, zoom) => GetCamera(cam).Zoom += (float)zoom);

        _lua.Globals["setCameraAngle"] = (Action<string, double>)((cam, angle) => GetCamera(cam).Angle = (float)angle);
        _lua.Globals["getCameraAngle"] = (Func<string, double>)((cam) => GetCamera(cam).Angle);
        _lua.Globals["setCameraAlpha"] = (Action<string, double>)((cam, alpha) => GetCamera(cam).Alpha = (float)alpha);
        _lua.Globals["getCameraAlpha"] = (Func<string, double>)((cam) => GetCamera(cam).Alpha);
        _lua.Globals["setCameraX"] = (Action<string, double>)((cam, x) => GetCamera(cam).X = (float)x);
        _lua.Globals["getCameraX"] = (Func<string, double>)((cam) => GetCamera(cam).X);
        _lua.Globals["setCameraY"] = (Action<string, double>)((cam, y) => GetCamera(cam).Y = (float)y);
        _lua.Globals["getCameraY"] = (Func<string, double>)((cam) => GetCamera(cam).Y);
    }

    private void RegisterObjectCallbacks()
    {
        _lua.Globals["setObjectX"] = (Action<string, double>)((tag, x) => _game?.SetObjectProperty(tag, "x", (float)x));
        _lua.Globals["getObjectX"] = (Func<string, double>)((tag) => (double)(_game?.GetObjectProperty(tag, "x") ?? 0.0));
        _lua.Globals["setObjectY"] = (Action<string, double>)((tag, y) => _game?.SetObjectProperty(tag, "y", (float)y));
        _lua.Globals["getObjectY"] = (Func<string, double>)((tag) => (double)(_game?.GetObjectProperty(tag, "y") ?? 0.0));

        _lua.Globals["setObjectAngle"] = (Action<string, double>)((tag, angle) => _game?.SetObjectProperty(tag, "angle", (float)angle));
        _lua.Globals["getObjectAngle"] = (Func<string, double>)((tag) => (double)(_game?.GetObjectProperty(tag, "angle") ?? 0.0));
        _lua.Globals["setObjectAlpha"] = (Action<string, double>)((tag, alpha) => _game?.SetObjectProperty(tag, "alpha", (float)alpha));
        _lua.Globals["getObjectAlpha"] = (Func<string, double>)((tag) => (double)(_game?.GetObjectProperty(tag, "alpha") ?? 1.0));

        _lua.Globals["setObjectScaleX"] = (Action<string, double>)((tag, sx) => _game?.SetObjectProperty(tag, "scaleX", (float)sx));
        _lua.Globals["getObjectScaleX"] = (Func<string, double>)((tag) => (double)(_game?.GetObjectProperty(tag, "scaleX") ?? 1.0));
        _lua.Globals["setObjectScaleY"] = (Action<string, double>)((tag, sy) => _game?.SetObjectProperty(tag, "scaleY", (float)sy));
        _lua.Globals["getObjectScaleY"] = (Func<string, double>)((tag) => (double)(_game?.GetObjectProperty(tag, "scaleY") ?? 1.0));
        // Psych: scaleObject(obj, x, y, ?updateHitbox = true)
        _lua.Globals["scaleObject"] = (Action<string, double, double, DynValue>)((tag, sx, sy, upd) =>
        {
            bool updateHitbox = upd == null || upd.IsNil() || upd.CastToBool();
            if (_game?.ResolvePsychObject(tag) is PlayScene.StaticStageProp prop)
            {
                prop.ScaleX = (float)sx; prop.ScaleY = (float)sy;
                if (updateHitbox) PlayScene.PsychUpdateHitbox(prop);
                return;
            }
            if (_game?.ResolvePsychObject(tag) is PsychHudObject hud)
            {
                hud.scaleX = (float)sx; hud.scaleY = (float)sy;
                if (updateHitbox) hud.updateHitbox();
                return;
            }
            _game?.SetObjectProperty(tag, "scaleX", (float)sx);
            _game?.SetObjectProperty(tag, "scaleY", (float)sy);
        });

        _lua.Globals["setObjectColor"] = (Action<string, string>)((tag, color) => _game?.SetObjectProperty(tag, "Color", LuaUtils.ColorFromString(color)));
        _lua.Globals["getObjectColor"] = (Func<string, string>)((tag) => _game?.GetObjectProperty(tag, "Color")?.ToString() ?? "FFFFFF");
        _lua.Globals["setObjectBlendMode"] = (Action<string, string>)((tag, blend) => _game?.SetObjectProperty(tag, "Blend", blend));
        _lua.Globals["getObjectBlendMode"] = (Func<string, string>)((tag) => _game?.GetObjectProperty(tag, "Blend")?.ToString() ?? "");
        _lua.Globals["setObjectVisible"] = (Action<string, bool>)((tag, visible) => _game?.SetObjectProperty(tag, "visible", visible));
        _lua.Globals["getObjectVisible"] = (Func<string, bool>)((tag) => (bool)(_game?.GetObjectProperty(tag, "visible") ?? true));
        _lua.Globals["setObjectScrollFactor"] = (Action<string, double, double>)((tag, sx, sy) =>
        {
            _game?.SetObjectProperty(tag, "scrollX", (float)sx);
            _game?.SetObjectProperty(tag, "scrollY", (float)sy);
        });
        _lua.Globals["getObjectScrollFactor"] = (Func<string, Table>)((tag) =>
        {
            var table = new Table(_lua);
            table[1] = (double)(_game?.GetObjectProperty(tag, "scrollX") ?? 1.0);
            table[2] = (double)(_game?.GetObjectProperty(tag, "scrollY") ?? 1.0);
            return table;
        });
        _lua.Globals["setObjectCamera"] = (Action<string, string>)((tag, cam) => _game?.SetObjectProperty(tag, "camera", cam));
        _lua.Globals["getObjectCamera"] = (Func<string, string>)((tag) => _game?.GetObjectProperty(tag, "camera")?.ToString() ?? "game");
    }

    private void RegisterInputCallbacks()
    {
        _lua.Globals["keyJustPressed"] = (Func<string, bool>)((name) =>
        {
            var input = FNFGame.Instance?.Input;
            if (input == null) return false;
            return name?.ToLower() switch
            {
                "left" => input.NotePressed[0],
                "down" => input.NotePressed[1],
                "up" => input.NotePressed[2],
                "right" => input.NotePressed[3],
                _ => FNF_MonoGame.Engine.PsychEngine.Controls.Instance.JustPressed((name ?? "").ToLowerInvariant().Trim()) // ExtraFunctions.hx default: controls.justPressed(name)
            };
        });
        _lua.Globals["keyPressed"] = (Func<string, bool>)((name) =>
        {
            var input = FNFGame.Instance?.Input;
            if (input == null) return false;
            return name?.ToLower() switch
            {
                "left" => input.NoteHeld[0],
                "down" => input.NoteHeld[1],
                "up" => input.NoteHeld[2],
                "right" => input.NoteHeld[3],
                _ => FNF_MonoGame.Engine.PsychEngine.Controls.Instance.Pressed((name ?? "").ToLowerInvariant().Trim()) // ExtraFunctions.hx default: controls.pressed(name)
            };
        });
        _lua.Globals["keyReleased"] = (Func<string, bool>)((name) =>
        {
            var input = FNFGame.Instance?.Input;
            if (input == null) return false;
            return name?.ToLower() switch
            {
                "left" => input.NoteReleased[0],
                "down" => input.NoteReleased[1],
                "up" => input.NoteReleased[2],
                "right" => input.NoteReleased[3],
                _ => FNF_MonoGame.Engine.PsychEngine.Controls.Instance.JustReleased((name ?? "").ToLowerInvariant().Trim()) // ExtraFunctions.hx default: controls.justReleased(name)
            };
        });

        _lua.Globals["keyboardJustPressed"] = (Func<string, bool>)((key) =>
        {
            var input = FNFGame.Instance?.Input;
            return input != null && LuaUtils.TryParseKey(key, out var k) && input.KeyJustPressed(k);
        });
        _lua.Globals["keyboardPressed"] = (Func<string, bool>)((key) =>
        {
            var input = FNFGame.Instance?.Input;
            return input != null && LuaUtils.TryParseKey(key, out var k) && input.KeyHeld(k);
        });
        _lua.Globals["keyboardReleased"] = (Func<string, bool>)((key) =>
        {
            var input = FNFGame.Instance?.Input;
            return input != null && LuaUtils.TryParseKey(key, out var k) && input.KeyReleased(k);
        });
        _lua.Globals["gamepadJustPressed"] = (Func<int, string, bool>)((id, key) =>
        {
            var input = FNFGame.Instance?.Input;
            return input != null && LuaUtils.TryParseButton(key, out var b) && input.ButtonJustPressed(b);
        });
        _lua.Globals["gamepadPressed"] = (Func<int, string, bool>)((id, key) =>
        {
            var input = FNFGame.Instance?.Input;
            return input != null && LuaUtils.TryParseButton(key, out var b) && input.ButtonHeld(b);
        });
        _lua.Globals["gamepadReleased"] = (Func<int, string, bool>)((id, key) =>
        {
            var input = FNFGame.Instance?.Input;
            return input != null && LuaUtils.TryParseButton(key, out var b) && input.ButtonReleased(b);
        });
        _lua.Globals["gamepadAnalogX"] = (Func<int, double>)((id) =>
            FNFGame.Instance?.Input?.GamePadAxisValue("leftx") ?? 0.0);
        _lua.Globals["gamepadAnalogY"] = (Func<int, double>)((id) =>
            FNFGame.Instance?.Input?.GamePadAxisValue("lefty") ?? 0.0);

        _lua.Globals["mouseClicked"] = (Func<string, bool>)((button) => PsychMouse(button, 1)); // FlxG.mouse.justPressed
        _lua.Globals["mousePressed"] = (Func<string, bool>)((button) => PsychMouse(button, 0));  // FlxG.mouse.pressed
        _lua.Globals["mouseReleased"] = (Func<string, bool>)((button) => PsychMouse(button, 2)); // FlxG.mouse.justReleased
        _lua.Globals["getMouseX"] = (Func<string, double>)((cam) => FNFGame.Instance?.Input?.MouseNow.X ?? 0);
        _lua.Globals["getMouseY"] = (Func<string, double>)((cam) => FNFGame.Instance?.Input?.MouseNow.Y ?? 0);
    }

    /// <summary>Psych mouse callbacks on the filtered mouse (controller pointer emulation removed). mode 0 held, 1 just pressed, 2 just released.</summary>
    private static bool PsychMouse(string button, int mode)
    {
        var input = FNFGame.Instance?.Input;
        if (input == null) return false;
        Microsoft.Xna.Framework.Input.ButtonState Pick(Microsoft.Xna.Framework.Input.MouseState m) => (button ?? "").ToLowerInvariant() switch
        { "right" => m.RightButton, "middle" => m.MiddleButton, _ => m.LeftButton };
        bool now = Pick(input.MouseNow) == Microsoft.Xna.Framework.Input.ButtonState.Pressed;
        bool prev = Pick(input.MousePrev) == Microsoft.Xna.Framework.Input.ButtonState.Pressed;
        return mode == 0 ? now : mode == 1 ? now && !prev : !now && prev;
    }

    private void RegisterModchartCallbacks()
    {
        // noteTween* — tweens strum note offsets (strumLineNotes indexing: 0-3 opponent, 4-7 player)
        _lua.Globals["noteTweenX"] = (Action<string, int, double, double, string>)((tag, note, value, duration, ease) =>
            _game?.StartNoteTween(tag, note, "x", (float)value, (float)duration, ease));
        _lua.Globals["noteTweenY"] = (Action<string, int, double, double, string>)((tag, note, value, duration, ease) =>
            _game?.StartNoteTween(tag, note, "y", (float)value, (float)duration, ease));
        _lua.Globals["noteTweenAngle"] = (Action<string, int, double, double, string>)((tag, note, value, duration, ease) =>
            _game?.StartNoteTween(tag, note, "angle", (float)value, (float)duration, ease));
        _lua.Globals["noteTweenAlpha"] = (Action<string, int, double, double, string>)((tag, note, value, duration, ease) =>
            _game?.StartNoteTween(tag, note, "alpha", (float)value, (float)duration, ease));
        _lua.Globals["noteTweenDirection"] = (Action<string, int, double, double, string>)((tag, note, value, duration, ease) =>
            _game?.StartNoteTween(tag, note, "direction", (float)value, (float)duration, ease));

        _lua.Globals["cancelTween"] = (Action<string>)((tag) =>
        {
            _game?.CancelModchartTween(tag);
            LuaTweenManager.CancelTween(tag);
        });

        // getPropertyFromGroup / setPropertyFromGroup / *FromClass are provided by
        // ReflectionFunctions.Implement (with strumLineNotes handled there).

        // Precaching — load assets ahead of time so mid-song loads don't hitch
        _lua.Globals["precacheImage"] = (Action<string>)((image) =>
        {
            try
            {
                string resolved = _game?.AssetManager?.ResolvePath($"images/{image}.png");
                if (resolved != null)
                    Texture2D.FromFile(FNFGame.Instance.GraphicsDevice, global::FNF_MonoGame.Engine.FsCI.Fix(resolved))?.Dispose();
            }
            catch (Exception ex) { Console.WriteLine($"[FUNKIN-LUA] precacheImage failed: {ex.Message}"); }
        });
        _lua.Globals["precacheSound"] = (Action<string>)((sound) =>
        {
            _game?.AssetManager?.ResolvePath($"sounds/{sound}.ogg");
        });
        _lua.Globals["precacheMusic"] = (Action<string>)((music) =>
        {
            _game?.AssetManager?.ResolvePath($"music/{music}.ogg");
        });
    }

    private void RegisterUtilCallbacks()
    {
        _lua.Globals["getRandomInt"] = (Func<int, int, int>)((min, max) =>
            new Random().Next(min, max + 1));
        _lua.Globals["getRandomFloat"] = (Func<double, double, double>)((min, max) =>
            min + new Random().NextDouble() * (max - min));
        _lua.Globals["getRandomBool"] = (Func<double, bool>)((chance) =>
            new Random().NextDouble() < chance / 100.0);

        _lua.Globals["stringStartsWith"] = (Func<string, string, bool>)((str, start) =>
            str?.StartsWith(start) ?? false);
        _lua.Globals["stringEndsWith"] = (Func<string, string, bool>)((str, end) =>
            str?.EndsWith(end) ?? false);
        _lua.Globals["stringSplit"] = (Func<string, string, Table>)((str, delimiter) =>
        {
            var parts = str?.Split(delimiter) ?? System.Array.Empty<string>();
            var table = new Table(_lua);
            for (int i = 0; i < parts.Length; i++)
                table[i + 1] = parts[i];
            return table;
        });
        _lua.Globals["stringTrim"] = (Func<string, string>)((str) => str?.Trim() ?? "");

        _lua.Globals["getTextFromFile"] = (Func<string, string>)((path) =>
        {
            string resolved = _game?.AssetManager?.ResolvePath(path);
            if (resolved != null && global::FNF_MonoGame.Engine.FsCI.FileExists(resolved))
                return global::FNF_MonoGame.Engine.FsCI.ReadAllText(resolved);
            return null;
        });

        _lua.Globals["directoryFileList"] = (Func<string, Table>)((folder) =>
        {
            var table = new Table(_lua);
            // ExtraFunctions directoryFileList(folder): FileSystem.readDirectory(absolutePath(folder)) relative to the game folder
            string resolved = global::FNF_MonoGame.Engine.FsCI.DirectoryExists(folder ?? "") ? folder : _game?.AssetManager?.ResolvePath(folder);
            if (resolved != null && global::FNF_MonoGame.Engine.FsCI.DirectoryExists(resolved))
            {
                int i = 1;
                foreach (var f in Directory.GetFileSystemEntries(resolved)) // readDirectory lists files AND folders
                    table[i++] = Path.GetFileName(f);
            }
            return table;
        });

        _lua.Globals["getSongPosition"] = (Func<double>)(() =>
            (_game?.Conductor?.SongPosition ?? 0.0) * 1000.0); // Conductor.songPosition is in ms in Psych

        // Psych: runHaxeCode(code, ?varsToBring, ?funcToRun, ?funcArgs) -> returnValue of the code (or of funcToRun)
        _lua.Globals["runHaxeCode"] = (Func<string, DynValue, DynValue, DynValue, DynValue>)((code, vars, funcToRun, funcArgs) =>
        {
            object ret = null;
            try
            {
                if (vars != null && vars.Type == DataType.Table)
                    foreach (var kv in vars.Table.Pairs) _hscript.SetVariable(kv.Key.CastToString(), kv.Value.ToObject());
                ret = _hscript.RunCode(code);
                if (funcToRun != null && funcToRun.Type == DataType.String)
                {
                    var fa = new List<object>();
                    if (funcArgs != null && funcArgs.Type == DataType.Table) foreach (var v in funcArgs.Table.Values) fa.Add(v.ToObject());
                    ret = _hscript.CallHaxeFunction(funcToRun.String, fa.ToArray());
                }
            }
            catch (Exception ex) { Console.WriteLine($"[FUNKIN-LUA] runHaxeCode error: {ex.Message}"); }
            if (ret == null || !(ret is string || ret is bool || ret is int || ret is long || ret is float || ret is double)) return DynValue.Nil; // LuaUtils.isLuaSupported
            return DynValue.FromObject(_lua, ret);
        });

        _lua.Globals["runHaxeFunction"] = DynValue.NewCallback((ctx, args) =>
        {
            if (args.Count == 0 || args[0].Type != DataType.String) return DynValue.Nil;
            string func = args[0].String;
            object[] hargs;
            if (args.Count == 2 && args[1].Type == DataType.Table)
            {
                var table = args[1].Table;
                var list = new List<object>();
                foreach (var v in table.Values)
                    list.Add(v.ToObject());
                hargs = list.ToArray();
            }
            else
            {
                hargs = new object[args.Count - 1];
                for (int i = 1; i < args.Count; i++)
                    hargs[i - 1] = args[i].ToObject();
            }
            try { return DynValue.FromObject(ctx.GetScript(), _hscript.CallHaxeFunction(func, hargs)); }
            catch (Exception ex) { Console.WriteLine($"[FUNKIN-LUA] runHaxeFunction error: {ex.Message}"); return DynValue.Nil; }
        });

        _lua.Globals["addHaxeLibrary"] = (Action<string, string>)((className, libName) =>
        {
            if (string.IsNullOrEmpty(className)) return;
            string fullName = string.IsNullOrEmpty(libName) ? className : $"{libName}.{className}";
            object imported = ReflectionFunctions.GetClassInstance(fullName)
                ?? ReflectionFunctions.GetClassInstance(className)
                ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            _hscript.SetVariable(className, imported);
        });

        _lua.Globals["version"] = "0.6.3";
        _lua.Globals["_VERSION"] = "Lua 5.1"; // linc_luajit / Lua 5.1 (MoonSharp reports its own name)
        _lua.Globals["playbackRate"] = 1.0;
        _lua.Globals["mustHitSection"] = false;
        _lua.Globals["gfSection"] = false;
    }

    private void RegisterScriptManagementCallbacks()
    {
        _lua.Globals["getRunningScripts"] = (Func<Table>)(() =>
        {
            var table = new Table(_lua);
            for (int i = 0; i < RunningScripts.Count; i++)
                table[i + 1] = PlayScene.PsychRelPath(RunningScripts[i].ScriptName); // Psych scriptName: 'mods/<mod>/...'.
            return table;
        });

        _lua.Globals["isRunning"] = (Func<string, bool>)((scriptFile) =>
        {
            if (string.IsNullOrEmpty(scriptFile)) return false;
            string want = scriptFile.Replace('\\', '/');
            string resolved = ResolveLuaPath(scriptFile);
            foreach (var s in RunningScripts)
                if (s.ScriptName.Equals(scriptFile, StringComparison.OrdinalIgnoreCase)
                    || PlayScene.PsychRelPath(s.ScriptName).Equals(want, StringComparison.OrdinalIgnoreCase)
                    || (resolved != null && s.ScriptName.Equals(resolved, StringComparison.OrdinalIgnoreCase)))
                    return true;
            return false;
        });

        _lua.Globals["addLuaScript"] = (Action<string>)((luaFile) =>
        {
            string resolved = ResolveLuaPath(luaFile);
            if (resolved != null && global::FNF_MonoGame.Engine.FsCI.FileExists(resolved))
            {
                var newScript = new FunkinLua(_game, resolved);
                newScript.Load();
            }
        });

        _lua.Globals["removeLuaScript"] = (Action<string>)((luaFile) =>
        {
            string resolved = ResolveLuaPath(luaFile);
            if (resolved == null) return;
            foreach (var s in RunningScripts.ToArray())
                if (s.ScriptName.Equals(resolved, StringComparison.OrdinalIgnoreCase))
                    s.Stop();
        });

        _lua.Globals["callScript"] = DynValue.NewCallback((ctx, args) =>
        {
            if (args.Count < 2 || args[0].Type != DataType.String || args[1].Type != DataType.String)
                return DynValue.Nil;
            string luaFile = args[0].String;
            string funcName = args[1].String;
            string resolved = ResolveLuaPath(luaFile);
            if (resolved == null) return DynValue.Nil;
            object[] callArgs = new object[args.Count - 2];
            for (int i = 2; i < args.Count; i++) callArgs[i - 2] = args[i].ToObject();
            foreach (var s in RunningScripts)
            {
                if (s.Closed || !s.ScriptName.Equals(resolved, StringComparison.OrdinalIgnoreCase)) continue;
                try { return s.Call(funcName, callArgs); }
                catch (Exception ex) { Console.WriteLine($"[FUNKIN-LUA] callScript error: {ex.Message}"); }
            }
            return DynValue.Nil;
        });

        _lua.Globals["callOnLuas"] = DynValue.NewCallback((ctx, args) =>
        {
            if (args.Count == 0 || args[0].Type != DataType.String) return DynValue.NewString(Function_Continue);
            string funcName = args[0].String;
            object[] callArgs = new object[args.Count - 1];
            for (int i = 1; i < args.Count; i++) callArgs[i - 1] = args[i].ToObject();
            return DynValue.NewString(_game?.CallOnLuas(funcName, callArgs) ?? Function_Continue);
        });

        _lua.Globals["callOnScripts"] = DynValue.NewCallback((ctx, args) =>
        {
            if (args.Count == 0 || args[0].Type != DataType.String) return DynValue.NewString(Function_Continue);
            string funcName = args[0].String;
            object[] callArgs = new object[args.Count - 1];
            for (int i = 1; i < args.Count; i++) callArgs[i - 1] = args[i].ToObject();
            return DynValue.NewString(_game?.CallOnScripts(funcName, callArgs) ?? Function_Continue);
        });

        _lua.Globals["callOnHScript"] = DynValue.NewCallback((ctx, args) =>
        {
            if (args.Count == 0 || args[0].Type != DataType.String) return DynValue.NewString(Function_Continue);
            string funcName = args[0].String;
            object[] callArgs = new object[args.Count - 1];
            for (int i = 1; i < args.Count; i++) callArgs[i - 1] = args[i].ToObject();
            return DynValue.NewString(_game?.CallOnHScript(funcName, callArgs) ?? Function_Continue);
        });

        _lua.Globals["loadSong"] = (Action<string, int>)((name, diff) => _game?.LoadSong(name, diff));
        _lua.Globals["endSong"] = (Action)(() => _game?.EndSong());
        _lua.Globals["restartSong"] = (Action)(() => _game?.RestartSong());
        _lua.Globals["startCountdown"] = (Func<bool>)(() => _game?.StartCountdown() ?? false);
        _lua.Globals["getSongPosition"] = (Func<double>)(() => (_game?.GetSongPosition() ?? 0.0) * 1000.0); // Psych: ms (port Conductor: seconds)

        _lua.Globals["setOnLuas"] = (Action<string, DynValue>)((name, value) =>
        {
            try { _game?.SetOnLuas(name, value?.ToObject()); }
            catch (Exception ex) { Console.WriteLine($"[FUNKIN-LUA] setOnLuas error: {ex.Message}"); }
        });

        _lua.Globals["setOnScripts"] = (Action<string, DynValue>)((name, value) =>
        {
            try { _game?.SetOnLuas(name, value?.ToObject()); }
            catch (Exception ex) { Console.WriteLine($"[FUNKIN-LUA] setOnScripts error: {ex.Message}"); }
        });
    }

    private string ResolveLuaPath(string luaFile)
    {
        if (string.IsNullOrEmpty(luaFile)) return null;

        // Try direct path
        if (global::FNF_MonoGame.Engine.FsCI.FileExists(luaFile)) return luaFile;

        // Try relative to script location
        string dir = Path.GetDirectoryName(ScriptName);
        if (dir != null)
        {
            string relative = Path.Combine(dir, luaFile);
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(relative)) return relative;
            if (!luaFile.EndsWith(".lua") && global::FNF_MonoGame.Engine.FsCI.FileExists(relative + ".lua"))
                return relative + ".lua";
        }

        // Try mods path
        string modsPath = _game?.AssetManager?.ResolvePath(luaFile);
        return modsPath;
    }

    public static Color ParseHexColor(string color)
    {
        if (string.IsNullOrEmpty(color)) return Color.White;
        string hex = color.TrimStart('#');
        if (uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint v))
        {
            return v switch
            {
                <= 0xFFF => new Color((int)((v >> 8) & 0xF) * 17, (int)((v >> 4) & 0xF) * 17, (int)(v & 0xF) * 17),
                <= 0xFFFF => new Color((int)((v >> 8) & 0xFF), (int)(v & 0xFF), 0),
                <= 0xFFFFFF => new Color((int)((v >> 16) & 0xFF), (int)((v >> 8) & 0xFF), (int)(v & 0xFF)),
                _ => new Color((int)((v >> 24) & 0xFF), (int)((v >> 16) & 0xFF), (int)((v >> 8) & 0xFF), (int)(v & 0xFF))
            };
        }
        return Color.White;
    }

    private void RegisterTextCallbacks()
    {
        _lua.Globals["makeLuaText"] = (Action<string, string, double, double, double>)((tag, text, width, x, y) =>
        {
            if (string.IsNullOrEmpty(tag)) return;
            _game?.MakeLuaText(tag, text ?? "", (float)width, (float)x, (float)y);
        });

        _lua.Globals["addLuaText"] = (Action<string, bool>)((tag, front) =>
        {
            _game?.AddLuaText(tag, front);
        });

        _lua.Globals["removeLuaText"] = (Action<string>)((tag) =>
        {
            _game?.RemoveLuaText(tag);
        });

        _lua.Globals["setTextString"] = (Action<string, string>)((tag, text) =>
        {
            _game?.SetTextString(tag, text ?? "");
        });

        _lua.Globals["setTextSize"] = (Action<string, int>)((tag, size) =>
        {
            _game?.SetTextSize(tag, size);
        });

        _lua.Globals["setTextFont"] = (Action<string, string>)((tag, font) =>
        {
            _game?.SetTextFont(tag, font);
        });

        _lua.Globals["setTextColor"] = (Action<string, string>)((tag, color) =>
        {
            _game?.SetTextColor(tag, color);
        });

        _lua.Globals["setTextBorder"] = (Action<string, double, DynValue>)((tag, size, color) =>
        {
            _game?.SetTextBorder(tag, size, color);
        });

        _lua.Globals["setTextAlignment"] = (Action<string, string>)((tag, alignment) =>
        {
            _game?.SetTextAlignment(tag, alignment);
        });

        _lua.Globals["setTextWidth"] = (Action<string, double>)((tag, width) =>
        {
            _game?.SetTextWidth(tag, (float)width);
        });

        _lua.Globals["getTextWidth"] = (Func<string, double>)((tag) =>
        {
            return _game?.GetTextWidth(tag) ?? 0.0;
        });

        _lua.Globals["getTextHeight"] = (Func<string, double>)((tag) =>
        {
            return _game?.GetTextHeight(tag) ?? 0.0;
        });
    }

    private void RegisterGameplayCallbacks()
    {
        _lua.Globals["setHealth"] = (Action<double>)((value) => _game?.SetHealth((float)value));
        _lua.Globals["getHealth"] = (Func<double>)(() => _game?.GetHealth() ?? 0.0);

        _lua.Globals["setBotPlay"] = (Action<bool>)((value) => _game?.SetBotPlay(value));
        _lua.Globals["getBotPlay"] = (Func<bool>)(() => _game?.GetBotPlay() ?? false);
        _lua.Globals["setPracticeMode"] = (Action<bool>)((value) => _game?.SetPracticeMode(value));
        _lua.Globals["getPracticeMode"] = (Func<bool>)(() => _game?.GetPracticeMode() ?? false);

        _lua.Globals["getRatingPercent"] = (Func<double>)(() => _game?.GetRatingPercent() ?? 0.0);
        _lua.Globals["getRatingName"] = (Func<string>)(() => _game?.GetRatingName() ?? "?");
        _lua.Globals["getRatingFC"] = (Func<string>)(() => _game?.GetRatingFC() ?? "N/A");
        _lua.Globals["getSicks"] = (Func<int>)(() => _game?.GetSicks() ?? 0);
        _lua.Globals["getGoods"] = (Func<int>)(() => _game?.GetGoods() ?? 0);
        _lua.Globals["getBads"] = (Func<int>)(() => _game?.GetBads() ?? 0);
        _lua.Globals["getShits"] = (Func<int>)(() => _game?.GetShits() ?? 0);

        _lua.Globals["setScore"] = (Action<int>)((value) => _game?.SetScore(value));
        _lua.Globals["addScore"] = (Action<int>)((value) => _game?.AddScore(value));
        _lua.Globals["getScore"] = (Func<int>)(() => _game?.GetScore() ?? 0);
        _lua.Globals["setMisses"] = (Action<int>)((value) => _game?.SetMisses(value));
        _lua.Globals["addMisses"] = (Action<int>)((value) => _game?.AddMisses(value));
        _lua.Globals["getMisses"] = (Func<int>)(() => _game?.GetMisses() ?? 0);
        _lua.Globals["setCombo"] = (Action<int>)((value) => _game?.SetCombo(value));
        _lua.Globals["addCombo"] = (Action<int>)((value) => _game?.AddCombo(value));
        _lua.Globals["getCombo"] = (Func<int>)(() => _game?.GetCombo() ?? 0);
        _lua.Globals["setHits"] = (Action<int>)((value) => _game?.SetHits(value));
        _lua.Globals["addHits"] = (Action<int>)((value) => _game?.AddHits(value));
        _lua.Globals["getHits"] = (Func<int>)(() => _game?.GetHits() ?? 0);

        _lua.Globals["addCharacterToList"] = (Action<string, string, double, double>)((name, type, x, y) =>
            _game?.AddCharacterToList(name, type, (float)x, (float)y));
        _lua.Globals["characterDance"] = (Action<string>)((type) => _game?.CharacterDance(type));
        _lua.Globals["characterPlayAnim"] = (Action<string, string, bool>)((type, anim, forced) =>
        {
            var ch = GetCharacterByType(type);
            ch?.PlayAnimation(anim, forced);
        });
        _lua.Globals["setCharacterAlpha"] = (Action<string, double>)((type, alpha) =>
        {
            var ch = GetCharacterByType(type);
            if (ch != null) ch.Alpha = (float)alpha;
        });
        _lua.Globals["getCharacterAlpha"] = (Func<string, double>)((type) => (double)(GetCharacterByType(type)?.Alpha ?? 1.0));
        _lua.Globals["setCharacterColor"] = (Action<string, string>)((type, color) =>
        {
            var ch = GetCharacterByType(type);
            if (ch != null) ch.Tint = LuaUtils.ColorFromString(color);
        });
        _lua.Globals["getCharacterColor"] = (Func<string, string>)((type) => GetCharacterByType(type)?.Tint.ToString() ?? "FFFFFF");

        // FunkinLua get/setCharacterX/Y act on the character GROUPS (dadGroup / gfGroup / boyfriendGroup)
        PsychCharGroup Grp(string type)
        {
            switch ((type ?? "").ToLowerInvariant())
            {
                case "dad": case "opponent": return _game.dadGroup;
                case "gf": case "girlfriend": return _game.gfGroup;
                default: return _game.boyfriendGroup;
            }
        }
        _lua.Globals["getCharacterX"] = (Func<string, double>)((type) => Grp(type).x);
        _lua.Globals["setCharacterX"] = (Action<string, double>)((type, x) => Grp(type).x = (float)x);
        _lua.Globals["getCharacterY"] = (Func<string, double>)((type) => Grp(type).y);
        _lua.Globals["setCharacterY"] = (Action<string, double>)((type, y) => Grp(type).y = (float)y);

        _lua.Globals["triggerEvent"] = (Action<string, DynValue, DynValue, DynValue>)((name, value1, value2, time) =>   // values are Dynamic in Psych (Std.string); a bool arg made MoonSharp abort the callback
            _game?.TriggerEvent(name, LuaArgString(value1), LuaArgString(value2), time != null && time.Type == DataType.Number ? (float)time.Number : 0f));
    }

    private static string LuaArgString(DynValue v) => v == null || v.IsNil() ? "" : v.Type == DataType.String ? v.String : v.Type == DataType.Boolean ? (v.Boolean ? "true" : "false") : v.CastToString() ?? "";

    private Character GetCharacterByType(string type)
    {
        return type?.ToLowerInvariant() switch
        {
            "dad" or "opponent" => _game?.Opponent,
            "gf" or "girlfriend" => _game?.Girlfriend,
            _ => _game?.Boyfriend
        };
    }
}
