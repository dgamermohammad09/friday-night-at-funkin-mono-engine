using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework.Graphics;
using MoonSharp.Interpreter;
using FNF_MonoGame.Scenes;
using FNF_MonoGame.Gameplay;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Psych Engine Lua script runtime wrapper using MoonSharp.
/// Loads a Lua file and registers callbacks that mirror Psych Engine's mod API.
/// </summary>
public class LuaScriptEngine
{
    private readonly Script _script;
    private readonly string _path;
    private readonly Dictionary<string, DynValue> _callbacks = new(StringComparer.OrdinalIgnoreCase);

    public PlayScene Game { get; }

    public LuaScriptEngine(PlayScene game, string luaPath)
    {
        Game = game ?? throw new ArgumentNullException(nameof(game));
        _path = luaPath;
        _script = new Script(CoreModules.Preset_Default)
        {
            Options = { UseLuaErrorLocations = true }
        };
        RegisterGlobals();
    }

    public bool Load()
    {
        if (!global::FNF_MonoGame.Engine.FsCI.FileExists(_path))
            return false;

        try
        {
            string code = global::FNF_MonoGame.Engine.FsCI.ReadAllText(_path);
            _script.DoString(code, null, _path);
            CollectCallbacks();
            Console.WriteLine($"[LUA] Loaded script: {_path}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LUA] Failed to load {_path}: {ex.Message}");
            return false;
        }
    }

    private void CollectCallbacks()
    {
        string[] names = new[]
        {
            "onCreate", "onCreatePost",
            "onUpdate", "onUpdatePost",
            "onStartCountdown", "onCountdownTick",
            "onStartSong", "onEndSong",
            "onBeatHit", "onStepHit",
            "onEvent", "onEventPushed",
            "goodNoteHit", "opponentNoteHit",
            "noteMiss", "noteMissPress",
            "onPause", "onResume",
            "onGameOver", "onGameOverConfirm"
        };

        foreach (var name in names)
        {
            var val = _script.Globals.Get(name);
            if (val.Type == DataType.Function)
                _callbacks[name] = val;
        }
    }

    public bool HasCallback(string name) => _callbacks.ContainsKey(name);

    public void Call(string name, params object[] args)
    {
        if (!_callbacks.TryGetValue(name, out var fn)) return;
        try
        {
            var luaArgs = new DynValue[args.Length];
            for (int i = 0; i < args.Length; i++)
                luaArgs[i] = MoonSharpValue(args[i]);
            _script.Call(fn, luaArgs);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LUA] Callback '{name}' error in {_path}: {ex.Message}");
        }
    }

    public bool? CallBool(string name, params object[] args)
    {
        if (!_callbacks.TryGetValue(name, out var fn)) return null;
        try
        {
            var luaArgs = new DynValue[args.Length];
            for (int i = 0; i < args.Length; i++)
                luaArgs[i] = MoonSharpValue(args[i]);
            var result = _script.Call(fn, luaArgs);
            return result.Boolean;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LUA] Callback '{name}' error in {_path}: {ex.Message}");
            return null;
        }
    }

    private DynValue MoonSharpValue(object value)
    {
        if (value == null) return DynValue.Nil;
        if (value is DynValue dv) return dv;
        if (value is Note note)
        {
            var table = new Table(_script);
            table["time"] = note.Time;
            table["lane"] = note.Lane;
            table["sustainLength"] = note.SustainLength;
            table["isPlayerNote"] = note.IsPlayerNote;
            table["noteType"] = note.NoteType ?? "";
            table["kind"] = note.Kind ?? "";
            table["altAnim"] = note.AltAnim;
            table["gfNote"] = note.GfNote;
            table["noAnimation"] = note.NoAnimation;
            return DynValue.NewTable(table);
        }
        if (value is GameEvent ev)
        {
            var table = new Table(_script);
            table["time"] = ev.Time;
            table["name"] = ev.Name ?? "";
            table["value"] = ev.Value;
            return DynValue.NewTable(table);
        }
        return DynValue.FromObject(_script, value);
    }

    private void RegisterGlobals()
    {
        // Common Psych Engine Lua helper functions
        _script.Globals["getProperty"] = (Func<string, object>)GetProperty;
        _script.Globals["setProperty"] = (Action<string, object>)SetProperty;
        _script.Globals["getPropertyFromClass"] = (Func<string, string, object>)GetPropertyFromClass;
        _script.Globals["setPropertyFromClass"] = (Action<string, string, object>)SetPropertyFromClass;
        _script.Globals["makeLuaSprite"] = (Func<string, string, float, float, bool>)MakeLuaSprite;
        _script.Globals["makeAnimatedLuaSprite"] = (Func<string, string, float, float, bool>)MakeLuaSprite;
        _script.Globals["addLuaSprite"] = (Action<string, bool>)AddLuaSprite;
        _script.Globals["scaleObject"] = (Action<string, float, float>)ScaleObject;
        _script.Globals["setScrollFactor"] = (Action<string, float, float>)SetScrollFactor;
        _script.Globals["setObjectCamera"] = (Action<string, string>)SetObjectCamera;
        _script.Globals["playSound"] = (Action<string, float, float>)PlaySound;
        _script.Globals["debugPrint"] = (Action<string>)DebugPrint;
        _script.Globals["luaPrint"] = (Action<string>)DebugPrint;
    }

    private object GetProperty(string name)
    {
        if (TryGetSpriteProperty(name, out var sprite, out var prop))
        {
            return prop?.ToLowerInvariant() switch
            {
                "x" => sprite.X,
                "y" => sprite.Y,
                "alpha" => sprite.Alpha,
                "scalex" => sprite.ScaleX,
                "scaley" => sprite.ScaleY,
                _ => null
            };
        }
        return Game?.GetStageVariable(name);
    }

    private void SetProperty(string name, object value)
    {
        if (TryGetSpriteProperty(name, out var sprite, out var prop))
        {
            float f = Convert.ToSingle(value);
            switch (prop?.ToLowerInvariant())
            {
                case "x": sprite.X = f; break;
                case "y": sprite.Y = f; break;
                case "alpha": sprite.Alpha = f; break;
                case "scalex": sprite.ScaleX = f; break;
                case "scaley": sprite.ScaleY = f; break;
            }
            return;
        }
        Game?.SetStageVariable(name, value);
    }

    private bool TryGetSpriteProperty(string name, out PlayScene.StaticStageProp sprite, out string property)
    {
        sprite = null;
        property = null;
        if (string.IsNullOrEmpty(name)) return false;

        int dot = name.IndexOf('.');
        if (dot <= 0 || dot >= name.Length - 1) return false;

        string tag = name.Substring(0, dot);
        property = name.Substring(dot + 1);
        return _luaSprites.TryGetValue(tag, out sprite);
    }

    private object GetPropertyFromClass(string className, string propertyName)
    {
        // Minimal: only support a few well-known PlayState properties
        if (className.Equals("PlayState", StringComparison.OrdinalIgnoreCase))
        {
            return propertyName.ToLowerInvariant() switch
            {
                "songPosition" => Game?.Conductor?.SongPosition ?? 0.0,
                "curBeat" => Game?.Conductor?.CurrentBeat ?? 0,
                "curStep" => Game?.Conductor?.CurrentStep ?? 0,
                "bpm" => Game?.Conductor?.BPM ?? 0f,
                _ => null
            };
        }
        return null;
    }

    private void SetPropertyFromClass(string className, string propertyName, object value)
    {
        // Not implemented for now
    }

    private readonly Dictionary<string, PlayScene.StaticStageProp> _luaSprites = new(StringComparer.OrdinalIgnoreCase);

    private bool MakeLuaSprite(string tag, string imagePath, float x, float y)
    {
        if (string.IsNullOrEmpty(tag) || Game?.AssetManager == null) return false;

        string fullPath = $"images/{imagePath}";
        string resolved = Game.AssetManager.ResolvePath(fullPath + ".png");
        if (resolved == null)
        {
            Console.WriteLine($"[LUA] makeLuaSprite image not found: {fullPath}");
            return false;
        }

        try
        {
            var tex = Texture2D.FromFile(FNFGame.Instance.GraphicsDevice, global::FNF_MonoGame.Engine.FsCI.Fix(resolved));
            _luaSprites[tag] = new PlayScene.StaticStageProp
            {
                Name = tag,
                Texture = tex,
                X = x,
                Y = y,
                ScaleX = 1f,
                ScaleY = 1f,
                Alpha = 1f,
                ScrollX = 1f,
                ScrollY = 1f,
                ZIndex = 0,
                Blend = null
            };
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LUA] makeLuaSprite failed for {tag}: {ex.Message}");
            return false;
        }
    }

    private void AddLuaSprite(string tag, bool inFront)
    {
        if (_luaSprites.TryGetValue(tag, out var prop))
            Game?.AddStageObject(prop);
    }

    private void ScaleObject(string tag, float scaleX, float scaleY)
    {
        if (_luaSprites.TryGetValue(tag, out var prop))
        {
            prop.ScaleX = scaleX;
            prop.ScaleY = scaleY;
        }
    }

    private void SetScrollFactor(string tag, float scrollX, float scrollY)
    {
        if (_luaSprites.TryGetValue(tag, out var prop))
        {
            prop.ScrollX = scrollX;
            prop.ScrollY = scrollY;
        }
    }

    private void SetObjectCamera(string tag, string camera)
    {
        // Currently only game camera is supported; other cameras ignored.
    }

    private void PlaySound(string path, float volume, float pitch)
    {
        FNFGame.Instance?.Audio?.PlaySound(path, volume);
    }

    private void DebugPrint(string message)
    {
        Console.WriteLine($"[LUA-PRINT] {message}");
    }
}
