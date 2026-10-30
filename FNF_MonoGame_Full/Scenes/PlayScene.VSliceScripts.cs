extern alias IRIS;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.PureHScript;
using FNF_MonoGame.Gameplay;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// V-Slice scripted classes (polymod .hxc from V-Slice mods) inside PlayState: Module / Song / Stage / Character /
/// NoteKind scripts get the PlayState events (onCreate, onSongLoaded, onCountdownStart, onSongStart, onUpdate,
/// onBeatHit, onStepHit, onNoteHit, onNoteMiss, onSongEvent, onSongEnd, onGameOver) like funkin.modding.events.
/// The superclasses and PlayState.instance are C# facades over the engine objects (VSliceFacades below).
/// </summary>
public partial class PlayScene
{
    private readonly List<VScriptInstance> _vsScripts = new();
    private static readonly List<VScriptInstance> s_vsModules = new();
    /// <summary>Characters whose V-Slice script extends 0.8.4 PicoPlayerBaseCharacter (pico-playable behaviour: game over, burps).</summary>
    internal static readonly HashSet<string> VsPicoPlayableIds = new(StringComparer.OrdinalIgnoreCase);
    private static bool s_vsModulesMade;
    private VsPlayState _vsPlayState;
    public static PlayScene VsCurrent;
    private readonly Dictionary<string, string> _vsNoteKindSuffix = new(StringComparer.Ordinal);

    /// <summary>Called once the stage, characters and chart are ready (PlayState.create).</summary>
    private void VSliceScriptsBegin()
    {
        try
        {
            if (!VSliceMods.Any) return;
            Console.WriteLine($"[VSLICE] modPath={_modPath} stage={_chart?.Stage} json={_hasJsonStage} z gf={_gfZIndex} dad={_dadZIndex} bf={_bfZIndex} props={string.Join(",", _staticProps.Select(p => p.Name + ":" + p.ZIndex))} anim={_animatedProps.Count}");
            VSliceScripts.EnsureLoaded();
            if (VSliceScripts.Classes.Count == 0) return;
            VsCurrent = this;
            Character.NoteKindSuffixResolver = k => VsCurrent?.VsNoteKindSuffix(k);
            _vsPlayState = new VsPlayState(this);
            VsRegisterGlobals();
            VSliceScripts.Factory = (cl, a) =>
            {
                string sn = cl.Contains('.') ? cl.Substring(cl.LastIndexOf('.') + 1) : cl;
                var sc = VSliceScripts.Classes.FirstOrDefault(c => c.Name == sn);
                if (sc != null)
                {
                    string b = sc.Extends?.Substring(sc.Extends.LastIndexOf('.') + 1) ?? "";
                    if (b.StartsWith("Scripted")) b = b.Substring(8);
                    VObj self = b is "FunkinSprite" or "FlxSprite" or "Bopper" or "FlxAtlasSprite" ? new VsSprite(this, VObj.D(HxFn.Arg(a, 0)), VObj.D(HxFn.Arg(a, 1))) : new VsBase(b);
                    return VSliceScripts.Instantiate(sc, self, a);
                }
                if (sn is "FunkinSprite" or "FlxSprite" or "Bopper" or "FlxAtlasSprite") return new VsSprite(this, VObj.D(HxFn.Arg(a, 0)), VObj.D(HxFn.Arg(a, 1)));
                if (sn == "AdjustColorShader") return new VsAdjustShader();
                if (sn == "FlxText") return new VsText(this, a); if (sn == "FlxTimer") return new HxFlxTimer();   // new FlxTimer().start(t, cb): real timer (an unknown-class stub ran the callback at once)
                if (sn == "Sequence") { var sq = new VsSequence(HxFn.Arg(a, 0)); VsAddSequence(sq); return sq; }
                if (sn == "Conductor") return new VsMiniConductor();
                return null;
            };
            _vsScripts.Clear();
            if (!s_vsModulesMade)
            {
                s_vsModulesMade = true;
                foreach (var c in VSliceScripts.ClassesExtending("Module"))
                    if (!NativeVsModules.Contains(c.Name)) s_vsModules.Add(VSliceScripts.Instantiate(c, new VsBase("Module")));
                Console.WriteLine($"[VSLICE-SCRIPT] modules: {string.Join(", ", s_vsModules.Select(m => m.Class.Name + "(" + m.Id + ")"))}");
            }
            foreach (var m in s_vsModules) if (!VSliceScripts.Globals.ContainsKey(m.Class.Name)) VSliceScripts.Globals[m.Class.Name] = m;   // static fields of module classes (QtOptionsCategory.SAVE_KEY_*)
            string songId = _chart?.SongName ?? _songName;
            foreach (var c in VSliceScripts.ClassesExtending("Song"))
            {
                var inst = VSliceScripts.Instantiate(c, new VsBase("Song"));
                if ((string.Equals(inst.Id, songId, StringComparison.OrdinalIgnoreCase) || string.Equals(inst.Id, _songName, StringComparison.OrdinalIgnoreCase)) && string.Equals(VsScriptVariation(inst), VsVariation, StringComparison.OrdinalIgnoreCase)) _vsScripts.Add(inst);   // SongRegistry: scriptedSongVariations[id:variation]
            }
            string stageId = _chart?.Stage;
            foreach (var c in VSliceScripts.ClassesExtending("Stage"))
            {
                var inst = VSliceScripts.Instantiate(c, new VsStage(this));
                if (string.Equals(inst.Id, stageId, StringComparison.OrdinalIgnoreCase)) _vsScripts.Add(inst);
            }
            foreach (var (ch, type) in new[] { (_boyfriend, "BF"), (_opponent, "DAD"), (_girlfriend, "GF") })
            {
                if (ch == null) continue;
                foreach (var c in VSliceScripts.ClassesExtending("BaseCharacter", "SparrowCharacter", "MultiSparrowCharacter", "PackerCharacter", "AnimateAtlasCharacter", "MultiAnimateAtlasCharacter", "PicoPlayerBaseCharacter"))
                {
                    var inst = VSliceScripts.Instantiate(c, new VsCharacter(this, ch, type));
                    if (string.Equals(inst.Id, ch.Name, StringComparison.OrdinalIgnoreCase)) { _vsScripts.Add(inst); ((VsCharacter)inst.Self).Script = inst; if ((c.Extends ?? "").EndsWith("PicoPlayerBaseCharacter")) VsPicoPlayableIds.Add(ch.Name); }
                }
            }
            foreach (var c in VSliceScripts.ClassesExtending("NoteKind"))
            {
                var nk = new VsBase("NoteKind");
                var inst = VSliceScripts.Instantiate(c, nk);
                string kind = inst.Id; if (kind == null) continue;
                string suffix = inst.Get("suffix") as string ?? nk.Get("suffix") as string;
                if (!string.IsNullOrEmpty(suffix)) _vsNoteKindSuffix[kind] = suffix;
                _vsScripts.Add(inst);
            }
            Console.WriteLine($"[VSLICE-SCRIPT] active for {songId}: {string.Join(", ", _vsScripts.Select(s => s.Class.Name + "(" + s.Id + ")"))}");
            foreach (var s in _vsScripts) if (s.Self is VsStage) s.Call("buildStage");
            // Stage.addCharacter(character, type) runs for bf/dad/gf while the stage is built (Stage scripts override it, e.g. to set a shader)
            foreach (var s in _vsScripts.ToList()) if (s.Self is VsStage)
                foreach (var t in new[] { "BF", "DAD", "GF" }) if (VsChar(t) is object vc) s.Call("addCharacter", vc, t);
            VsDispatch("onCreate", new VEvent("CREATE"));
            VsDispatch("onSongLoaded", new VEvent("SONG_LOADED"));
            QsSongLoaded();
        }
        catch (Exception ex) { Console.WriteLine($"[VSLICE-SCRIPT] begin failed: {ex}"); }
    }

    /// <summary>Song(id, {variation: X}) params of a scripted song (null/missing = "default").</summary>
    private static string VsScriptVariation(VScriptInstance inst)
    {
        try
        {
            var p = inst.SuperArgs.Length > 1 ? inst.SuperArgs[1] : null;
            if (p == null) return "default";
            object v = p is VObj vo ? vo.Get("variation") : IRIS::Reflect.field(p, "variation");
            return v as string ?? "default";
        }
        catch { return "default"; }
    }

    private IEnumerable<VScriptInstance> VsAll() => s_vsModules.Concat(_vsScripts);

    /// <summary>Send a script event to every active scripted class (modules first, like ModuleHandler).</summary>
    public VEvent VsDispatch(string fn, VEvent ev)
    {
        if (VsCurrent != this || (_vsScripts.Count == 0 && s_vsModules.Count == 0)) return ev;
        foreach (var s in VsAll().ToList())
        {
                        s.Call(fn, ev);
            if (ev.Stopped) break;
        }
        return ev;
    }

    // Hooks used by the dispatchers (PlayScene.BaseStage.cs)
    private void VsUpdate(float elapsed) { if (_vsPlayState != null && !PsychNotes) UpdateHxFlixel(elapsed); /* FlxG.plugins: timers, tweens */ VsElementsUpdate(elapsed); VsSpritesUpdate(elapsed); VsCutsceneTick(elapsed); VsCharAnimHooks(); if (_vsPlayState != null) VsDispatch("onUpdate", new VEvent("UPDATE", ("elapsed", (double)elapsed))); }
    private void VsBeatHit(int beat) { VsElementsBeat(); if (_vsPlayState != null) VsDispatch("onBeatHit", new VEvent("SONG_BEAT_HIT", ("beat", beat), ("step", beat * 4))); }
    private void VsStepHit(int step) { if (_vsPlayState != null) VsDispatch("onStepHit", new VEvent("SONG_STEP_HIT", ("beat", step / 4), ("step", step))); }
    private void VsSongStart() { QsSongStart(); if (_vsPlayState != null) VsDispatch("onSongStart", new VEvent("SONG_START")); }
    private void VsCountdownStart() { if (_vsPlayState != null) VsDispatch("onCountdownStart", new VEvent("COUNTDOWN_START")); }
    private void VsNoteHit(FNF_MonoGame.Engine.PsychEngine.Note note, bool player) { if (_vsPlayState != null) VsDispatch(player ? "onNoteHit" : "onNoteHit", new VEvent("NOTE_HIT", ("note", new VsNote(note, player)), ("isComboBreak", false), ("hitDiff", 0.0), ("judgement", "sick"))); }
    private void VsNoteMiss(FNF_MonoGame.Engine.PsychEngine.Note note) { if (_vsPlayState != null) VsDispatch("onNoteMiss", new VEvent("NOTE_MISS", ("note", new VsNote(note, true)), ("healthChange", -0.0475 * 2))); }
    private void VsSongEvent(GameEvent ev)
    {
        if (_vsPlayState == null || ev == null) return;
        var data = new VObj(); data.Extra["eventKind"] = ev.Name; data.Extra["time"] = ev.Time * 1000.0; object val = ev.Value is string[] sa ? sa.FirstOrDefault() : ev.Value; data.Extra["value"] = val;
        data.Extra["getString"] = new HxFn(a => val?.ToString());
        VsDispatch("onSongEvent", new VEvent("SONG_EVENT", ("eventData", data)));
    }

    /// <summary>NoteKind suffix for a note kind registered by a ScriptedNoteKind (e.g. "alt" -> sing anims "-alt").</summary>
    public string VsNoteKindSuffix(string kind) => kind != null && _vsNoteKindSuffix.TryGetValue(kind, out var s) ? s : null;

    private void VsRegisterGlobals()
    {
        var g = VSliceScripts.Globals;
        g["PlayState"] = new VsStatic(f => f == "instance" ? _vsPlayState : null);
        g["Conductor"] = new VsStatic(f => f == "instance" ? new VsConductor(this) : null);
        g["FlxG"] = typeof(HxFlxG);
        g["FlxTween"] = typeof(HxFlxTween);
        g["FlxEase"] = typeof(HxFlxEase);
        g["FlxTimer"] = typeof(HxFlxTimer);
        g["FlxColor"] = typeof(HxFlxColor);
        g["StringTools"] = typeof(HxStringTools);
        g["Std"] = new VsStd();
        g["Math"] = typeof(IRIS::Math);
        g["Paths"] = new VsPaths();
        g["FunkinSprite"] = new VsStatic(f => f switch
        {
            "create" => new HxFn(a => { var sp = new VsSprite(this, VObj.D(HxFn.Arg(a, 0)), VObj.D(HxFn.Arg(a, 1))); if (HxFn.Arg(a, 2) is string t) sp.Get("loadTexture"); if (HxFn.Arg(a, 2) is string t2) ((HxFn)sp.Get("loadTexture")).__hx_invokeDynamic(new object[] { t2 }); return sp; }),
            "createTextureAtlas" => new HxFn(a => { var sp = new VsSprite(this, VObj.D(HxFn.Arg(a, 0)), VObj.D(HxFn.Arg(a, 1))); ((HxFn)sp.Get("loadTextureAtlas")).__hx_invokeDynamic(new object[] { HxFn.Arg(a, 2) }); return sp; }),
            "createSparrow" => new HxFn(a => { var sp = new VsSprite(this, VObj.D(HxFn.Arg(a, 0)), VObj.D(HxFn.Arg(a, 1))); ((HxFn)sp.Get("loadSparrow")).__hx_invokeDynamic(new object[] { HxFn.Arg(a, 2) }); return sp; }),
            _ => null,
        });
        g["FlxAnimationUtil"] = new VsStatic(f => f is "addTextureAtlasAnimations" or "addAtlasAnimations" ? new HxFn(a =>
        {
            var target = HxFn.Arg(a, 0) is VScriptInstance si ? si.Self : HxFn.Arg(a, 0) as VObj;
            if (target is VsSprite sp && HxFn.Arg(a, 1) is IRIS::Array<object> arr)
                for (int i = 0; i < arr.length; i++)
                    if (arr.__get(i) is VsJson an)
                    {
                        var addFn = (HxFn)((VObj)sp.Get("animation")).Get("addByFrameLabel");
                        addFn.__hx_invokeDynamic(new object[] { an.Get("name"), an.Get("prefix"), an.Get("frameRate") ?? 24, VObj.B(an.Get("looped")) });
                    }
            return null;
        }) : null);
        g["Bopper"] = g["FunkinSprite"];
        g["Save"] = new VsStatic(f => f == "instance" ? new VsSaveFacade() : null);
        g["FunkinSound"] = new VsStatic(f => f switch
        {
            "load" => new HxFn(a => VsLoadSound(HxFn.Arg(a, 0) as string, a.Length > 1 && VObj.B(a[1]))),
            "playOnce" => new HxFn(a => { var snd = VsLoadSound(HxFn.Arg(a, 0) as string, false); if (a.Length > 1) snd.Set("volume", a[1]); snd.Play(); return snd; }),
            _ => null,
        });
        var fssm = new VsStatic(f => f switch { "wideScale" => new VsPoint(() => 1f, _ => { }, () => 1f, _ => { }), "cutoutSize" => new VsPoint(() => 0f, _ => { }, () => 0f, _ => { }), _ => null });
        g["FullScreenScaleMode"] = fssm; g["FSSM"] = fssm;
        g["CharacterType"] = new VsStatic(f => f is "BF" or "DAD" or "GF" or "OTHER" ? f : null);
        g["Preferences"] = new VsStatic(f => f switch { "downscroll" => (object)_downscroll, "flashingLights" => true, "subtitles" => false, "autoPause" => true, "cameraZoomMode" => 1, "zoomCamera" => true, "naughtyness" => true, "framerate" => 60, "strumlineBackgroundOpacity" => 0.0, _ => null });
        g["PlayStatePlaylist"] = new VsStatic(f => f switch { "isStoryMode" => (object)false, "campaignId" => null, _ => null });
    }
}

/// <summary>Class with static fields (PlayState.instance, Preferences.x ...).</summary>
public sealed class VsStatic : VObj
{
    internal static readonly Random Rng = new Random();   // Random.Shared is not available on UWP (.NET Native)
    private readonly Func<string, object> _get;
    public VsStatic(Func<string, object> get) { _get = get; }
    public override bool TryGet(string f, out object v) { v = _get(f); return v != null; }
}

public sealed class VsStd : VObj
{
    public override bool TryGet(string f, out object v)
    {
        switch (f)
        {
            case "int": v = Fn(a => (int)Math.Floor(D(A(a, 0)))); return true;
            case "parseInt": v = Fn(a => int.TryParse(S(A(a, 0)), out var i) ? (object)i : null); return true;
            case "parseFloat": v = Fn(a => D(A(a, 0))); return true;
            case "string": v = Fn(a => A(a, 0)?.ToString() ?? "null"); return true;
            case "random": v = Fn(a => VsStatic.Rng.Next(Math.Max(1, (int)D(A(a, 0))))); return true;
            case "isOfType": case "is": v = Fn(a => A(a, 0) != null); return true;
            case "downcast": v = Fn(a => A(a, 0)); return true;
        }
        v = null; return false;
    }
}

public sealed class VsPaths : VObj
{
    public override bool TryGet(string f, out object v)
    {
        switch (f)
        {
            case "image": case "animateAtlas": v = Fn(a => "images/" + S(A(a, 0))); return true;
            case "sound": v = Fn(a => "sounds/" + S(A(a, 0)) + ".ogg"); return true;
            case "music": v = Fn(a => "music/" + S(A(a, 0)) + ".ogg"); return true;
            case "font": v = Fn(a => "fonts/" + S(A(a, 0))); return true;
            case "videos": case "video": v = Fn(a => "videos/" + S(A(a, 0)) + ".mp4"); return true;
            case "json": v = Fn(a => "data/" + S(A(a, 0)) + ".json"); return true;
            case "file": case "getPath": v = Fn(a => S(A(a, 0))); return true;
        }
        v = null; return false;
    }
}

/// <summary>Superclass facade without engine behaviour (Module, Song, NoteKind ...): base methods are no-ops,
/// fields are free-form (suffix, noteStyleId ...).</summary>
public class VsBase : VObj, IVSuperInit
{
    public readonly string Kind; public object[] Args = System.Array.Empty<object>();
    public VsBase(string kind) { Kind = kind; }
    public virtual void SuperInit(object[] a)
    {
        Args = a;
        if (Kind == "NoteKind") { Extra["noteKind"] = A(a, 0); Extra["description"] = A(a, 1); Extra["noteStyleId"] = A(a, 2); }
        Extra["id"] = A(a, 0);
    }
    public override bool TryGet(string f, out object v)
    {
        if (Extra.TryGetValue(f, out v)) return true;
        if (f.StartsWith("on") || f is "buildStage" or "fetchAssetPaths" or "toString" or "dispatchToCharacters" or "refresh")
        {
            v = f == "fetchAssetPaths" ? Fn(a => new IRIS::Array<object>()) : Fn(a => null);
            return true;
        }
        switch (f)
        {
            case "active": v = true; return true;
            case "priority": v = 1000; return true;
            case "isSongNew": v = Fn(a => false); return true;
            case "listDifficulties": v = Fn(a => new IRIS::Array<object>()); return true;
        }
        return false;
    }
}

public sealed class VsConductor : VObj
{
    private readonly PlayScene _ps; public VsConductor(PlayScene ps) { _ps = ps; }
    public override bool TryGet(string f, out object v)
    {
        var c = _ps.Conductor;
        switch (f)
        {
            case "songPosition": v = (double)(c?.SongPosition ?? 0) * 1000.0; return true;
            case "bpm": v = (double)(c?.BPM ?? 100); return true;
            case "beatLengthMs": v = 60000.0 / Math.Max(1, c?.BPM ?? 100); return true;
            case "mapTimeChanges": case "update": case "forceBPM": v = Fn(a => null); return true;   // the song conductor is driven by the engine
            case "stepLengthMs": v = 15000.0 / Math.Max(1, c?.BPM ?? 100); return true;
            case "currentBeat": v = _ps.CurBeat; return true;
            case "currentStep": v = _ps.CurStep; return true;
            case "currentBeatTime": v = (double)_ps.CurBeat; return true;
            case "currentStepTime": v = (double)_ps.CurStep; return true;
        }
        v = null; return false;
    }
}

/// <summary>PlayState.instance.</summary>
public sealed class VsPlayState : VObj
{
    private VsCamFollowPoint _camFollow; private VsCamera _camHud;
    private readonly PlayScene _ps;
    private VsStrumline _opp, _plr; private VsStage _stage;
    public VsPlayState(PlayScene ps) { _ps = ps; }
    public override bool TryGet(string f, out object v)
    {
        switch (f)
        {
            case "currentSong": { var s = new VObj(); s.Extra["id"] = _ps.VsSongId; s.Extra["songName"] = _ps.Chart?.DisplayName ?? _ps.VsSongId; v = s; return true; }
            case "currentStage": v = _stage ??= new VsStage(_ps); return true;
            case "opponentStrumline": v = _opp ??= new VsStrumline(_ps, false); return true;
            case "playerStrumline": v = _plr ??= new VsStrumline(_ps, true); return true;
            case "currentVariation": v = _ps.VsVariation; return true;
            case "currentDifficulty": v = _ps.Chart?.Difficulty ?? "normal"; return true;
            case "health": v = (double)_ps.VsHealth; return true;
            case "songScore": v = _ps.VsScore; return true;
            case "isInCutscene": case "isBotPlayMode": case "isPracticeMode": case "isMinimalMode": case "isChartingMode": case "disableKeys": v = Extra.TryGetValue(f, out var x) ? x : false; return true;
            case "defaultCameraZoom": v = (double)_ps.DefaultCamZoom; return true;
            case "tweenCameraZoom": v = Fn(a => { _ps.VsTweenCameraZoom(a.Length > 0 && a[0] != null ? (float)D(a[0]) : 1f, a.Length > 1 ? (float)D(a[1]) : 0f, a.Length > 2 && B(a[2]), PlayScene.VsEase(A(a, 3))); return null; }); return true;
            case "cameraFollowPoint": v = _camFollow ??= new VsCamFollowPoint(_ps); return true;
            case "startCountdown": v = Fn(a => { _ps.VsStartCountdown(); return null; }); return true;
            case "stageZoom": v = (double)_ps.VsStageZoom; return true;
            case "tweenCameraToPosition": v = Fn(a => { _ps.TweenCameraToPosition((float)D(A(a, 0)), (float)D(A(a, 1)), a.Length > 2 ? (float)D(a[2]) : 0f, PlayScene.VsEase(A(a, 3))); return null; }); return true;
            case "camHUD": v = _camHud ??= new VsCamera(_ps, true); return true;
            case "camCutscene": case "camGame": v = Extra.TryGetValue(f, out var cg) ? cg : (Extra[f] = new VsCamera(_ps, false) { IsCutscene = f == "camCutscene" }); return true;
            case "subtitles": v = Extra.TryGetValue(f, out var sub) ? sub : (Extra[f] = new VsBase("Subtitles")); return true;
            case "comboPopUps": v = Extra.TryGetValue(f, out var cp) ? cp : (Extra[f] = new VsComboPopUps(_ps)); return true;
            case "healthBar": case "healthBarBG": case "scoreText": case "iconP1": case "iconP2":
            case "__camUnused": v = Extra.TryGetValue(f, out var cam) ? cam : (Extra[f] = new VsBase(f)); return true;
            case "controls": v = new VsControls(); return true;
            case "add": case "insert": case "remove": v = Fn(a => { _ps.VsAdd(f, a); return A(a, f == "insert" ? 1 : 0); }); return true;
        }
        v = null; return false;
    }
    public override bool TrySet(string f, object v)
    {
        switch (f)
        {
            case "health": _ps.VsHealth = (float)D(v); return true;
            case "defaultCameraZoom": _ps.DefaultCamZoom = (float)D(v); return true;
            case "justUnpaused": case "mayPauseGame": case "isInCountdown": case "needsReset": Extra[f] = v; return true;
        }
        return false;
    }
}

public sealed class VsControls : VObj
{
    public override bool TryGet(string f, out object v)
    {
        if (f == "getDialogueNameFromToken") { v = new HxFn(a => "ENTER"); return true; }
        var input = PlayScene.VsCurrent?.VsInput;
        if (f is "CUTSCENE_ADVANCE" or "ACCEPT" or "ACCEPT_P") { v = input != null && (input.ConfirmPressed || input.IsGamePadPressed(Microsoft.Xna.Framework.Input.Buttons.A)); return true; }   // Controls.CUTSCENE_ADVANCE = accept (ENTER / pad A)
        if (f is "BACK" or "BACK_P") { v = input != null && input.CutsceneBackPressed; return true; }
        v = false; return f.ToUpperInvariant() == f;
    }
}

public sealed class VsStrumline : VObj
{
    private readonly PlayScene _ps; private readonly bool _player;
    public VsStrumline(PlayScene ps, bool player) { _ps = ps; _player = player; }
    public override bool TryGet(string f, out object v)
    {
        switch (f)
        {
            case "getByIndex": v = Fn(a => new VsStrum(_ps, _player, (int)D(A(a, 0)))); return true;
            case "members": v = new IRIS::Array<object>(Enumerable.Range(0, 4).Select(i => (object)new VsStrum(_ps, _player, i)).ToArray()); return true;
            case "strumlineNotes": v = this; return true;
            case "isPlayer": v = _player; return true;
            case "visible": v = Enumerable.Range(0, 4).Any(i => _ps.GetStrumMod(i, _player).Visible); return true;
            case "alpha": v = (double)_ps.GetStrumMod(0, _player).Alpha; return true;
        }
        v = null; return false;
    }
    public override bool TrySet(string f, object v)
    {
        switch (f)
        {
            case "visible": for (int i = 0; i < 4; i++) _ps.GetStrumMod(i, _player).Visible = B(v); return true;
            case "alpha": for (int i = 0; i < 4; i++) _ps.GetStrumMod(i, _player).Alpha = (float)D(v); return true;
        }
        return false;
    }
}

/// <summary>StrumlineNote: x/y are absolute (V-Slice) and stored as offsets from the engine's lane position.</summary>
public sealed class VsStrum : VObj
{
    private readonly PlayScene _ps; private readonly bool _player; private readonly int _i;
    public VsStrum(PlayScene ps, bool player, int i) { _ps = ps; _player = player; _i = Math.Clamp(i, 0, 3); }
    PlayScene.StrumMod M => _ps.GetStrumMod(_i, _player);
    public override bool TryGet(string f, out object v)
    {
        switch (f)
        {
            case "x": v = (double)(_ps.VsStrumBaseX(_i, _player) + M.OffsetX); return true;
            case "y": v = (double)(_ps.VsStrumBaseY() + M.OffsetY); return true;
            case "alpha": v = (double)M.Alpha; return true;
            case "visible": v = M.Visible; return true;
            case "angle": v = (double)M.Angle; return true;
        }
        v = null; return false;
    }
    public override bool TrySet(string f, object v)
    {
        switch (f)
        {
            case "x": M.OffsetX = (float)(D(v) - _ps.VsStrumBaseX(_i, _player)); return true;
            case "y": M.OffsetY = (float)(D(v) - _ps.VsStrumBaseY()); return true;
            case "alpha": M.Alpha = (float)D(v); return true;
            case "visible": M.Visible = B(v); return true;
            case "angle": M.Angle = (float)D(v); return true;
        }
        return false;
    }
}

/// <summary>Stage facade (funkin.play.stage.Stage).</summary>
public sealed class VsStage : VsBase
{
    private readonly PlayScene _ps;
    public VsStage(PlayScene ps) : base("Stage") { _ps = ps; }
    public override bool TryGet(string f, out object v)
    {
        switch (f)
        {
            case "getBoyfriend": v = Fn(a => _ps.VsChar("BF")); return true;
            case "getDad": v = Fn(a => _ps.VsChar("DAD")); return true;
            case "getGirlfriend": v = Fn(a => _ps.VsChar("GF")); return true;
            case "getNamedProp": v = Fn(a => _ps.VsProp(S(A(a, 0)))); return true;
            case "getCharacter": v = Fn(a => { string id = S(A(a, 0)); foreach (var t in new[] { "BF", "DAD", "GF" }) if (_ps.VsChar(t) is VObj o && (o.Get("characterId") as string) == id) return o; return null; }); return true;
            case "addCharacter": v = Fn(a => null); return true;   // base Stage.addCharacter: the engine already placed the character
            case "add": case "remove": case "insert": v = Fn(a => { _ps.VsAdd(f, a); return null; }); return true;
            case "stageName": case "id": v = _ps.Chart?.Stage; return true;
            case "camZoom": v = (double)_ps.DefaultCamZoom; return true;
        }
        return base.TryGet(f, out v);
    }
}

/// <summary>Stage prop (StaticStageProp) as a FlxSprite.</summary>
public sealed class VsProp : VObj
{
    public readonly PlayScene.StaticStageProp P;
    public VsProp(PlayScene.StaticStageProp p) { P = p; }
    public override bool TryGet(string f, out object v)
    {
        switch (f)
        {
            case "x": v = (double)P.X; return true;
            case "y": v = (double)P.Y; return true;
            case "alpha": v = (double)P.Alpha; return true;
            case "visible": v = P.Visible; return true;
            case "angle": v = (double)P.Angle; return true;
            case "zIndex": v = P.ZIndex; return true;
            case "flipX": v = P.FlipX; return true;
            case "scale": v = new VsPoint(() => P.ScaleX, x => P.ScaleX = x, () => P.ScaleY, y => P.ScaleY = y); return true;
            case "scrollFactor": v = new VsPoint(() => P.ScrollX, x => P.ScrollX = x, () => P.ScrollY, y => P.ScrollY = y); return true;
            case "animation": v = new VsPropAnim(P); return true;
            case "playAnimation": v = Fn(a => { new VsPropAnim(P).Play(S(A(a, 0)), B(A(a, 1))); return null; }); return true;
            case "color": v = P.OverlayColor.HasValue ? (int)((P.OverlayColor.Value.R << 16) | (P.OverlayColor.Value.G << 8) | P.OverlayColor.Value.B) : 0xFFFFFF; return true;
            case "updateHitbox": case "screenCenter": case "kill": case "revive": case "destroy": v = Fn(a => null); return true;
            case "shader": case "blend": v = Extra.TryGetValue(f, out var x) ? x : null; return true;
        }
        v = null; return false;
    }
    public override bool TrySet(string f, object v)
    {
        switch (f)
        {
            case "shader": Extra["shader"] = v; PlayScene.VsSetPropShader(P.Name, v as VsAdjustShader); return true;
            case "x": P.X = (float)D(v); return true;
            case "y": P.Y = (float)D(v); return true;
            case "alpha": P.Alpha = (float)D(v); return true;
            case "visible": P.Visible = B(v); return true;
            case "angle": P.Angle = (float)D(v); return true;
            case "zIndex": P.ZIndex = (int)D(v); return true;
            case "flipX": P.FlipX = B(v); return true;
            case "color": { long c = (long)D(v); P.OverlayColor = new Color((int)((c >> 16) & 0xFF), (int)((c >> 8) & 0xFF), (int)(c & 0xFF)); return true; }
        }
        return false;
    }
}

public sealed class VsPropAnim : VObj
{
    private readonly PlayScene.StaticStageProp _p;
    public VsPropAnim(PlayScene.StaticStageProp p) { _p = p; }
    public void Play(string name, bool force)
    {
        if (name == null || _p.Animations == null || !_p.Animations.ContainsKey(name)) return;
        if (!force && _p.CurrentAnimation == name && _p.AnimPlaying) return;
        _p.CurrentAnimation = name; _p.CurrentFrame = 0; _p.AnimTimer = 0; _p.AnimPlaying = true;
    }
    public override bool TryGet(string f, out object v)
    {
        switch (f)
        {
            case "play": v = Fn(a => { Play(S(A(a, 0)), B(A(a, 1))); return null; }); return true;
            case "curAnim": { var c = new VObj(); c.Extra["name"] = _p.CurrentAnimation; c.Extra["finished"] = !_p.AnimPlaying; c.Extra["curFrame"] = _p.CurrentFrame; v = c; return true; }
            case "name": v = _p.CurrentAnimation; return true;
            case "finished": v = !_p.AnimPlaying; return true;
        }
        v = null; return false;
    }
}

public sealed class VsPoint : VObj
{
    private readonly Func<float> _gx, _gy; private readonly Action<float> _sx, _sy;
    public VsPoint(Func<float> gx, Action<float> sx, Func<float> gy, Action<float> sy) { _gx = gx; _sx = sx; _gy = gy; _sy = sy; }
    public override bool TryGet(string f, out object v)
    {
        switch (f)
        {
            case "x": v = (double)_gx(); return true;
            case "y": v = (double)_gy(); return true;
            case "set": v = Fn(a => { _sx((float)D(A(a, 0))); _sy((float)(a.Length > 1 ? D(a[1]) : D(A(a, 0)))); return this; }); return true;
        }
        v = null; return false;
    }
    public override bool TrySet(string f, object v)
    {
        if (f == "x") { _sx((float)D(v)); return true; }
        if (f == "y") { _sy((float)D(v)); return true; }
        return false;
    }
}

/// <summary>BaseCharacter facade.</summary>
public sealed class VsCharacter : VsBase
{
    private readonly PlayScene _ps; public readonly Character C; public readonly string Type;
    public VScriptInstance Script;
    public bool SuppressBase; public string LastAnim; public int LastFrame;
    private object _data;
    public VsCharacter(PlayScene ps, Character c, string type) : base("Character") { _ps = ps; C = c; Type = type; }
    private object LoadData()
    {
        try
        {
            string p = _ps.VsAssets.ResolvePath($"data/characters/{C.Name}.json");
            return p == null ? null : VsJson.Wrap(VSliceMods.ParseLenient(global::FNF_MonoGame.Engine.FsCI.ReadAllText(p)));
        }
        catch { return null; }
    }
    public override bool TryGet(string f, out object v)
    {
        switch (f)
        {
            case "x": v = (double)C.Position.X; return true;
            case "y": v = (double)C.Position.Y; return true;
            case "alpha": v = (double)C.Alpha; return true;
            case "debug": v = false; return true;
            case "shader": v = Extra.TryGetValue("shader", out var shv) ? shv : null; return true;
            case "getFramesWithKeyword": v = Fn(a => new IRIS::Array<object>());   /* FlxSpriteElement insertion is done natively (VSliceMods.ScanSpriteElements) */ return true;
            case "_data": v = _data ??= LoadData(); return true;
            case "zIndex": v = Extra.TryGetValue("zIndex", out var zi) ? zi : 0; return true;
            case "characterId": v = C.Name; return true;
            case "characterName": v = C.JsonData?.Name ?? C.Name; return true;
            case "characterType": v = Type; return true;
            case "flipX": v = C.FlipX; return true;
            case "getCurrentAnimation": v = Fn(a => C.CurrentAnimation); return true;
            case "playAnimation": v = Fn(a => { if (!SuppressBase) C.PlayAnimation(S(A(a, 0)), B(A(a, 1))); return null; }); return true;
            case "dance": v = Fn(a => { C.PlayAnimation(C.CurrentAnimation?.StartsWith("dance") == true ? (C.CurrentAnimation == "danceLeft" ? "danceRight" : "danceLeft") : "idle", B(A(a, 0))); return null; }); return true;
            case "playSingAnimation": v = Fn(a => { string dir = S(A(a, 0))?.ToUpperInvariant(); string suf = S(A(a, 2)); C.PlayAnimation("sing" + dir + (B(A(a, 1)) ? "miss" : "") + (string.IsNullOrEmpty(suf) ? "" : "-" + suf), true); return null; }); return true;
            case "hasAnimation": v = Fn(a => true); return true;
            case "isAnimationFinished": v = Fn(a => false); return true;
            case "animation": v = new VsCharAnim(C); return true;
            case "cameraFocusPoint": { var p = _ps.VsFocusPoint(C); var cv = new VObj(); cv.Extra["x"] = (double)p.X; cv.Extra["y"] = (double)p.Y; v = cv; return true; }   // midpoint + camera offsets
            case "getGraphicMidpoint": case "getMidpoint": v = Fn(a => { var m = C.GetMidpoint(); var cv = new VObj(); cv.Extra["x"] = (double)m.X; cv.Extra["y"] = (double)m.Y; return cv; }); return true;
        }
        return base.TryGet(f, out v);
    }
    public override bool TrySet(string f, object v)
    {
        switch (f)
        {
            case "x": C.Position = new Vector2((float)D(v), C.Position.Y); return true;
            case "y": C.Position = new Vector2(C.Position.X, (float)D(v)); return true;
            case "alpha": C.Alpha = (float)D(v); return true;
            case "flipX": C.FlipX = B(v); return true;
            case "shader": _ps.VsSetCharShader(Type, v as VsAdjustShader); Extra["shader"] = v; return true;
        }
        return false;
    }
}

/// <summary>funkin.graphics.shaders.AdjustColorShader (hue/saturation/brightness/contrast), read live at draw time.</summary>
public sealed class VsAdjustShader : VObj
{
    public double Hue, Saturation, Brightness, Contrast;
    public PlayScene.AdjustColorParams Params => new((float)Hue, (float)Saturation, (float)Brightness, (float)Contrast);
    public override bool TryGet(string f, out object v)
    {
        switch (f)
        {
            case "hue": v = Hue; return true;
            case "saturation": v = Saturation; return true;
            case "brightness": v = Brightness; return true;
            case "contrast": v = Contrast; return true;
            case "setAdjustColor": v = Fn(a => { Brightness = D(A(a, 0)); Hue = D(A(a, 1)); Contrast = D(A(a, 2)); Saturation = D(A(a, 3)); return null; }); return true;
        }
        v = null; return false;
    }
    public override bool TrySet(string f, object v)
    {
        switch (f)
        {
            case "hue": Hue = D(v); return true;
            case "saturation": Saturation = D(v); return true;
            case "brightness": Brightness = D(v); return true;
            case "contrast": Contrast = D(v); return true;
        }
        return false;
    }
}

public sealed class VsCharAnim : VObj
{
    private readonly Character _c; public VsCharAnim(Character c) { _c = c; }
    public override bool TryGet(string f, out object v)
    {
        switch (f)
        {
            case "play": v = Fn(a => { _c.PlayAnimation(S(A(a, 0)), B(A(a, 1))); return null; }); return true;
            case "curAnim": { var c = new VObj(); c.Extra["name"] = _c.CurrentAnimation; c.Extra["finished"] = false; v = c; return true; }
            case "name": v = _c.CurrentAnimation; return true;
        }
        v = null; return false;
    }
}

/// <summary>funkin.play.notes.NoteSprite as seen from a HitNoteScriptEvent.</summary>
public sealed class VsNote : VObj
{
    private readonly FNF_MonoGame.Engine.PsychEngine.Note _n; private readonly bool _player;
    public VsNote(FNF_MonoGame.Engine.PsychEngine.Note n, bool player) { _n = n; _player = player; }
    public override bool TryGet(string f, out object v)
    {
        switch (f)
        {
            case "kind": case "noteKind": v = _n?.Kind; return true;
            case "direction": v = _n?.NoteData ?? 0; return true;
            case "strumTime": v = (double)(_n?.StrumTime ?? 0) * 1000.0; return true;
            case "mustHitNote": v = _player; return true;
            case "noteData":
            {
                var d = new VObj(); int lane = _n?.NoteData ?? 0;
                d.Extra["kind"] = _n?.Kind; d.Extra["time"] = (double)(_n?.StrumTime ?? 0) * 1000.0; d.Extra["data"] = lane + (_player ? 0 : 4);
                d.Extra["getDirection"] = new HxFn(a => lane);
                d.Extra["getDirectionName"] = new HxFn(a => new[] { "Left", "Down", "Up", "Right" }[lane & 3]);
                d.Extra["getMustHitNote"] = new HxFn(a => _player);
                v = d; return true;
            }
        }
        v = null; return false;
    }
}

/// <summary>Animated stage prop (PlayScene.AnimatedStageProp struct, accessed by index).</summary>
public sealed class VsAnimProp : VObj
{
    private readonly PlayScene _ps; private readonly int _i;
    public VsAnimProp(PlayScene ps, int i) { _ps = ps; _i = i; }
    public override bool TryGet(string f, out object v)
    {
        var sp = _ps.VsAnimSprite(_i);
        switch (f)
        {
            case "x": v = (double)(sp?.Position.X ?? 0); return true;
            case "y": v = (double)(sp?.Position.Y ?? 0); return true;
            case "alpha": v = (double)_ps.VsAnimAlpha(_i, null); return true;
            case "animation": { var o = new VObj(); o.Extra["play"] = new HxFn(a => { sp?.PlayAnimation(S(A(a, 0)), B(A(a, 1))); return null; }); var c = new VObj(); c.Extra["name"] = sp?.CurrentAnimation; o.Extra["curAnim"] = c; o.Extra["name"] = sp?.CurrentAnimation; v = o; return true; }
            case "playAnimation": v = Fn(a => { sp?.PlayAnimation(S(A(a, 0)), B(A(a, 1))); return null; }); return true;
            case "scale": v = new VsPoint(() => sp?.Scale.X ?? 1, x => { if (sp != null) sp.Scale = new Vector2(x, sp.Scale.Y); }, () => sp?.Scale.Y ?? 1, y => { if (sp != null) sp.Scale = new Vector2(sp.Scale.X, y); }); return true;
        }
        v = null; return false;
    }
    public override bool TrySet(string f, object v)
    {
        var sp = _ps.VsAnimSprite(_i);
        switch (f)
        {
            case "shader": Extra["shader"] = v; PlayScene.VsSetPropShader(_ps.VsAnimPropName(_i), v as VsAdjustShader); return true;
            case "x": if (sp != null) sp.Position = new Vector2((float)D(v), sp.Position.Y); return true;
            case "y": if (sp != null) sp.Position = new Vector2(sp.Position.X, (float)D(v)); return true;
            case "alpha": _ps.VsAnimAlpha(_i, (float)D(v)); return true;
            case "visible": _ps.VsAnimAlpha(_i, B(v) ? 1f : 0f); return true;
        }
        return false;
    }
}
