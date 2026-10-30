using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.PsychEngine;
using MoonSharp.Interpreter;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Psych Engine Lua script integration for PlayScene.
/// Mirrors PlayState.hx script startup: loads scripts from the same folders
/// Psych searches (scripts/, data/{song}/, stages/{stage}.lua,
/// custom_notetypes/, custom_events/) and dispatches all gameplay callbacks
/// to every running FunkinLua script.
/// </summary>
public partial class PlayScene
{
    private bool _psychLuaLoaded;

    /// <summary>
    /// Shared sprite pool for Lua and HScript mods (Psych Engine game.modchartSprites).
    /// </summary>
    public Dictionary<string, object> ModchartSprites { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Load every Psych Engine Lua script the original PlayState would run.
    /// Called from Load() after the mod content root is registered.
    /// </summary>
    public void LoadPsychLuaScripts()
    {
        if (_psychLuaLoaded) return;
        _psychLuaLoaded = true;

        // Clean state from a previous song
        FunkinLua.ClearAll();
        ShaderFunctions.ClearAll(); // runtime shaders belong to the song's scripts
        LuaTweenManager.Clear();
        ClearLuaTexts();

        // Register the PlayScene as the global "game" and expose the shared sprite pool
        MusicBeatState.Variables["game"] = this;
        MusicBeatState.Variables["modchartSprites"] = ModchartSprites;

        // game.unspawnNotes: Psych Note views over the chart notes, so onCreate scripts can read
        // noteType and set noAnimation/etc. before the notes spawn (NoteField copies at spawn).
        ApplyPsychNoteTypeDefaults();
        PsychCamera.ResetAll(); ResetHxFlixel();
        if (_conductor != null) { _conductor.CatchUpSkipped = PsychNotes; _conductor.DeferEvents = PsychNotes && Environment.GetEnvironmentVariable("FNF_PSYCH_NODEFER") != "1"; }
        UnspawnNotes = PsychNotes ? PsychBuildUnspawnNotes() : _chart?.Notes != null
            ? _chart.Notes.Select(n => new FNF_MonoGame.Engine.PsychEngine.Note(n)).ToList()
            : new List<FNF_MonoGame.Engine.PsychEngine.Note>();
        // camGame/camHUD zoom are the engine zooms (Psych lerps camGame.zoom back to defaultCamZoom, like the bop decay here)
        PsychCamera.GameCam.ZoomGet = () => _defaultCamZoom * _cameraBopMultiplier;
        PsychCamera.GameCam.ZoomSet = v => _cameraBopMultiplier = _defaultCamZoom > 0f ? v / _defaultCamZoom : 1f;
        PsychCamera.HudCam.ZoomGet = () => 1f + _hudZoomAdd;
        PsychCamera.HudCam.ZoomSet = v => _hudZoomAdd = v - 1f;
        if (PsychNotes && _noteField != null)
        {
            _noteField.OnSpawn = PsychOnNoteSpawned; // PlayState.update: onSpawnNote when a note enters notes
            _noteField.SpawnAheadOverride = () => 2.0 * playbackRate / Math.Min(1.0, Math.Max(0.01, songSpeed)); // spawnTime 2000 ms * playbackRate / songSpeed (< 1)
        }
        PsychObjectsInitMembers(); // PlayState.create 418: stage JSON objects become members before any script
        var searchRoots = GetPsychScriptRoots();
        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Global mod scripts: scripts/*.lua (Psych: "SCRIPTS FOLDER" section)
        foreach (var root in searchRoots)
            LoadLuaScriptsInFolder(Path.Combine(root, "scripts"), loaded);

        // 2. Stage script: stages/{stage}.lua
        string stageName = _chart?.Stage;
        if (!string.IsNullOrEmpty(stageName))
        {
            foreach (var root in searchRoots)
            {
                LoadLuaScript(Path.Combine(root, "stages", stageName + ".lua"), loaded);
                LoadHScript(Path.Combine(root, "stages", stageName + ".hx"), loaded);
            }
        }

        // startCharacterScripts(gf / dad / boyfriend): characters/<name>.lua / .hx (PlayState.create 457-459)
        foreach (var chName in new[] { _girlfriend?.Name, _opponent?.Name, _boyfriend?.Name })
        {
            if (string.IsNullOrEmpty(chName)) continue;
            foreach (var root in searchRoots)
            {
                LoadLuaScript(Path.Combine(root, "characters", chName + ".lua"), loaded);
                LoadHScript(Path.Combine(root, "characters", chName + ".hx"), loaded);
            }
        }

        PsychOrderAddUiGroups(); // PlayState.create 476-520: add(comboGroup/uiGroup/noteGroup), add(camFollow) after the stage scripts

        // generateSong -> makeEvent: onEventPushed(event, value1, value2, strumTime ms) reaches only the scripts loaded so far
        if (_chart?.Events != null)
            foreach (var ev in _chart.Events)
            {
                if (IsPortSyntheticEvent(ev)) continue;
                var (v1, v2) = GetPsychEventValues(ev.Value);
                CallOnScripts("onEventPushed", ev.Name ?? "", v1 ?? "", v2 ?? "", ev.Time * 1000.0);
            }
        // PlayState.create 497: moveCameraSection() -> onMoveCamera for section 0 (stage/global/character scripts only)
        if (PsychNotes && _chart?.Sections != null && _chart.Sections.Count > 0)
        {
            var s0 = _chart.Sections[0];
            // camFollow = camPos (gf graphic midpoint + gf camera offsets), FlxG.camera.snapToTarget(), then moveCameraSection()
            if (_girlfriend != null && PsychCameraTarget(2) is Microsoft.Xna.Framework.Vector2 g0) { _camWorldX = g0.X; _camWorldY = g0.Y; }
            if (PsychCameraTarget(s0.GfSection ? 2 : (s0.MustHitSection ? 0 : 1)) is Microsoft.Xna.Framework.Vector2 t0) { _camTargetWorldX = t0.X; _camTargetWorldY = t0.Y; }
            CallOnScripts("onMoveCamera", s0.GfSection ? "gf" : (s0.MustHitSection ? "boyfriend" : "dad"));
        }

        // 4. Note type scripts: custom_notetypes/{type}.lua
        var noteTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_chart?.Notes != null)
        {
            foreach (var note in _chart.Notes)
                if (!string.IsNullOrEmpty(note.NoteType))
                    noteTypes.Add(note.NoteType);
        }
        foreach (var noteType in noteTypes)
        {
            foreach (var root in searchRoots)
            {
                LoadLuaScript(Path.Combine(root, "custom_notetypes", noteType + ".lua"), loaded);
                LoadHScript(Path.Combine(root, "custom_notetypes", noteType + ".hx"), loaded);
            }
        }

        // 5. Event scripts: custom_events/{event}.lua
        var eventNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_chart?.Events != null)
        {
            foreach (var ev in _chart.Events)
                if (!string.IsNullOrEmpty(ev.Name))
                    eventNames.Add(ev.Name);
        }
        foreach (var eventName in eventNames)
        {
            foreach (var root in searchRoots)
            {
                LoadLuaScript(Path.Combine(root, "custom_events", eventName + ".lua"), loaded);
                LoadHScript(Path.Combine(root, "custom_events", eventName + ".hx"), loaded);
            }
        }

        // 3. Song scripts: data/{song}/*.lua (Psych: "SONG SPECIFIC SCRIPTS")
        string songFolder = Difficulty.FormatToSongPath(_songName ?? "");
        if (songFolder.Length > 0)
        {
            foreach (var root in searchRoots)
                LoadLuaScriptsInFolder(Path.Combine(root, "data", songFolder), loaded);
        }

        if (FunkinLua.RunningScripts.Count > 0)
            Console.WriteLine($"[PSYCH-LUA] {FunkinLua.RunningScripts.Count} Lua script(s) running");

        PsychEventEarlyTriggers(); // PlayState.create 606: event.strumTime -= eventEarlyTrigger(event)

        PreloadPsychChangeCharacters();
        // onSpawnNote is fired when a note actually spawns (NoteField.OnSpawn -> PsychOnNoteSpawned), like PlayState.update
    }

    /// <summary>
    /// Script search roots in Psych priority order: the song's mod first,
    /// then every other enabled mod, then the global mods folder.
    /// </summary>
    private List<string> GetPsychScriptRoots()
    {
        var roots = new List<string>();

        if (!string.IsNullOrEmpty(_modPath) && global::FNF_MonoGame.Engine.FsCI.DirectoryExists(_modPath))
            roots.Add(_modPath);

        // Mods.directoriesWithFile: mods/, the current mod, and enabled mods with "runsGlobally" only.
        foreach (var mod in PsychMods.GetAllMods().Where(m => m.Enabled && m.IsGlobal))
        {
            string modPath = PsychMods.GetModDirectory(mod.Name);
            if (!string.IsNullOrEmpty(modPath) && global::FNF_MonoGame.Engine.FsCI.DirectoryExists(modPath) &&
                !roots.Contains(modPath))
                roots.Add(modPath);
        }

        // Global mods folder (loose files directly in mods/)
        string globalMods = PsychPaths.Mods();
        if (!string.IsNullOrEmpty(globalMods) && global::FNF_MonoGame.Engine.FsCI.DirectoryExists(globalMods) &&
            !roots.Contains(globalMods))
        {
            roots.Add(globalMods);
        }

        return roots;
    }

    private void LoadLuaScriptsInFolder(string folder, HashSet<string> loaded)
    {
        if (!global::FNF_MonoGame.Engine.FsCI.DirectoryExists(folder)) return;
        foreach (var file in global::FNF_MonoGame.Engine.FsCI.GetFiles(folder, "*.lua"))
            LoadLuaScript(file, loaded);
        // Psych 1.0 runs HScript (.hx) files from the same folders as Lua.
        foreach (var file in global::FNF_MonoGame.Engine.FsCI.GetFiles(folder, "*.hx"))
            LoadHScript(file, loaded);
    }

    private void LoadHScript(string path, HashSet<string> loaded)
    {
        if (!global::FNF_MonoGame.Engine.FsCI.FileExists(path) || !loaded.Add(Path.GetFullPath(path))) return;
        try
        {
            new Gameplay.HScriptStage(this, path);
            Console.WriteLine($"[PSYCH-HSCRIPT] Loaded: {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PSYCH-HSCRIPT] Failed to load {path}: {ex}");
        }
    }

    private void LoadLuaScript(string path, HashSet<string> loaded)
    {
        if (!global::FNF_MonoGame.Engine.FsCI.FileExists(path) || !loaded.Add(Path.GetFullPath(path))) return;
        try
        {
            var script = new FunkinLua(this, path);
            if (script.Load()) script.Call("onCreate"); // FunkinLua.new: call('onCreate') right after loading (PlayState create order)
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PSYCH-LUA] Failed to load {path}: {ex}");
        }
    }

    /// <summary>
    /// Call a function on every running Lua script.
    /// Mirrors PlayState.callOnLuas — returns Function_Stop if any script requested it.
    /// </summary>
    public string CallOnLuas(string funcName, params object[] args)
    {
        string returnVal = FunkinLua.Function_Continue;
        var scripts = FunkinLua.RunningScripts;
        for (int i = 0; i < scripts.Count; i++)
        {
            var script = scripts[i];
            if (script.Closed) continue;
            try
            {
                var ret = script.Call(funcName, args);
                if (ret.Type == DataType.String)
                {
                    string s = ret.String;
                    if (s == FunkinLua.Function_Stop || s == FunkinLua.Function_StopAll)
                        returnVal = s;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PSYCH-LUA] {funcName} error in {script.ScriptName}: {ex.Message}");
            }
        }
        return returnVal;
    }

    /// <summary>
    /// Set a variable on every running Lua script (Psych: setOnLuas).
    /// </summary>
    public void SetOnLuas(string name, object value)
    {
        foreach (var script in FunkinLua.RunningScripts)
        {
            if (script.Closed) continue;
            try
            {
                script.LuaScript.Globals[name] = value == null
                    ? DynValue.Nil
                    : DynValue.FromObject(script.LuaScript, value);
            }
            catch { }
        }
    }

    /// <summary>
    /// Shut down all Lua scripts and tweens (called when leaving the scene).
    /// </summary>
    public void ShutdownPsychLua()
    {
        try
        {
            CallOnLuas("onDestroy");
        }
        catch { }
        FunkinLua.ClearAll();
        LuaTweenManager.Clear();
        _psychLuaLoaded = false;
    }

}
