using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.PsychEngine;
using FNF_MonoGame.Gameplay;
using FNF_MonoGame.Sprites;
using Note = FNF_MonoGame.Engine.PsychEngine.Note;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Psych Engine BaseStage integration methods for PlayScene.
/// Kept in a separate file to avoid modifying the massive PlayScene.cs directly.
/// </summary>
public partial class PlayScene
{
    #region Stage registration

    public void RegisterStage(BaseStage stage)
    {
        if (stage != null && !_stages.Contains(stage))
            _stages.Add(stage);
    }

    public void UnregisterStage(BaseStage stage)
    {
        if (stage != null)
            _stages.Remove(stage);
    }

    public void CreateDefaultStage()
    {
        // Stage, global (scripts/), song (data/<song>/), note type and event scripts, both .lua
        // and .hx, are all loaded once by LoadPsychLuaScripts() across every enabled mod, in
        // Psych's order. Loading them here as well ran mod scripts twice (LuaStage + FunkinLua).
        CreateBuiltInStage(Chart?.Stage ?? "stage");
    }

    private void CreateBuiltInStage(string stageName)
    {
        // Built-in Psych Engine stage classes (StageWeek1, Limo, Mall, etc.)
        // are excluded from this build until their compile issues are fixed.
        // Mod stages are handled via HScriptStage/LuaStage scripts.
        new BaseStage(this);
    }

    private void LoadModScripts()
    {
        if (string.IsNullOrEmpty(_modPath)) return;

        string scriptsDir = Path.Combine(_modPath, "scripts");
        if (!global::FNF_MonoGame.Engine.FsCI.DirectoryExists(scriptsDir)) return;

        foreach (var hxPath in global::FNF_MonoGame.Engine.FsCI.EnumerateFiles(scriptsDir, "*.hx", SearchOption.AllDirectories))
        {
            new HScriptStage(this, hxPath);
        }

        foreach (var luaPath in global::FNF_MonoGame.Engine.FsCI.EnumerateFiles(scriptsDir, "*.lua", SearchOption.AllDirectories))
        {
            new LuaStage(this, luaPath);
        }
    }

    #endregion

    #region Stage object injection (sprites added by mod stages)

    // Mod stages can add arbitrary objects; for now we track them as variables
    // so scripts can retrieve them by name. Drawable objects are rendered via
    // the existing StaticStageProp path if they are StaticStageProp instances.
    public void AddStageObject(object obj)
    {
        if (obj is StaticStageProp prop)
        {
            if (!_staticProps.Contains(prop))
            {
                _staticProps.Add(prop);
                SortStaticProps();
            }
        }
        else
        {
            Console.WriteLine($"[BASESTAGE] Add: unsupported object type {obj?.GetType()?.Name}");
        }
    }

    public void Add(object obj) => AddStageObject(obj);

    public void RemoveStageObject(object obj)
    {
        if (obj is StaticStageProp prop)
            _staticProps.Remove(prop);
        else if (obj is SpriteGroup group)
        {
            foreach (var member in group.Members)
                _staticProps.Remove(member);
        }
    }

    public void Remove(object obj, bool splice = true) => RemoveStageObject(obj);
    public void AddBehindBF(object obj) => AddStageObject(obj);

    public void InsertStageObject(int position, object obj)
    {
        if (obj is StaticStageProp prop)
        {
            _staticProps.Insert(Math.Clamp(position, 0, _staticProps.Count), prop);
            SortStaticProps();
        }
    }

    public void SortStaticProps()
    {
        var sorted = _staticProps.OrderBy(p => p.ZIndex).ToList();
        _staticProps.Clear();
        _staticProps.AddRange(sorted);
    }

    public Conductor Conductor => _conductor;
    public AssetManager AssetManager => Assets;
    public float CurDecBeat => _conductor?.CurrentBeat ?? 0;
    public float CurDecStep => _conductor?.CurrentStep ?? 0;

    public int GetObjectIndex(Character character)
    {
        if (character == null) return 0;
        // Characters are not in _staticProps; return 0 as a safe default for layering.
        return 0;
    }

    public object GetStageVariable(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (_stageVariables.TryGetValue(name, out var value))
            return value;
        return FNF_MonoGame.Engine.PsychEngine.LuaUtils.GetVarInArray(this, name);
    }

    public void SetStageVariable(string name, object value)
    {
        if (string.IsNullOrEmpty(name)) return;
        // Try to set a real PlayScene property first (e.g., setPropertyFromClass('PlayState', ...)).
        FNF_MonoGame.Engine.PsychEngine.LuaUtils.SetVarInArray(this, name, value);
        _stageVariables[name] = value;
    }

    public object GetObjectProperty(string tag, string property)
    {
        var obj = GetStageVariable(tag);
        if (obj == null) return null;
        return LuaUtils.GetVarInArray(obj, property);
    }

    public void SetObjectProperty(string tag, string property, object value)
    {
        var obj = GetStageVariable(tag);
        if (obj == null) return;
        LuaUtils.SetVarInArray(obj, property, value);
    }

    #endregion

    #region Lua gameplay helpers

    public void EndSong() => _forceEndSong = true;

    public string CallOnHScript(string funcName, params object[] args)
    {
        string returnVal = FunkinLua.Function_Continue;
        foreach (var stage in _stages)
        {
            if (stage is HScriptStage hss)
            {
                try
                {
                    var ret = hss.Call(funcName, args);
                    if (ret is string s && (s == FunkinLua.Function_Stop || s == FunkinLua.Function_StopAll))
                        returnVal = s;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PSYCH-HSCRIPT] {funcName} error: {ex.Message}");
                }
            }
        }
        return returnVal;
    }

    public string CallOnScripts(string funcName, params object[] args)
    {
        // PlayState.callOnScripts: Lua first; HScript only when no Lua script stopped (excludeValues = [Function_Continue])
        string r = CallOnLuas(funcName, args);
        if (r == FunkinLua.Function_Stop || r == FunkinLua.Function_StopAll) return r;
        string h = CallOnHScript(funcName, args);
        return h == FunkinLua.Function_Stop || h == FunkinLua.Function_StopAll ? h : r;
    }

    public void RestartSong()
    {
        var p = BuildParams();
        p.StartTimestamp = 0f;
        Game.Scenes.ChangeScene(new PlayScene(p));
    }

    public void LoadSong(string name, int difficulty)
    {
        string diffName = Difficulty.GetString(difficulty);
        var p = new PlayStateParams
        {
            SongName = name,
            TargetDifficulty = diffName,
            PracticeMode = _practiceMode,
            BotPlayMode = _botplay,
            TargetVariation = _targetVariation,
            TargetInstrumental = _targetInstrumental
        };
        Game.Scenes.ChangeScene(new PlayScene(p));
    }

    public float GetSongPosition() => (float)(PsychNotes ? PsychSongPosMs() / 1000.0 : (_conductor?.SongPosition ?? 0.0));

    public void SetHealth(float value) => _health = Math.Clamp(value, HEALTH_MIN, HEALTH_MAX);
    public float GetHealth() => _health;

    public void SetBotPlay(bool value) => _botplay = value;
    public bool GetBotPlay() => _botplay;
    public void SetPracticeMode(bool value) => _practiceMode = value;
    public bool GetPracticeMode() => _practiceMode;

    public float GetRatingPercent() => _ratingPercent ?? (_totalNotes > 0 ? (_totalNotesHit / (float)_totalNotes) * 100f : 0f);
    public string GetRatingName()
    {
        if (_ratingName != null) return _ratingName;
        if (_totalNotes == 0) return "?";
        float pct = _totalNotesHit / (float)_totalNotes;
        if (pct >= 1f) return "SS";
        if (pct >= 0.98f) return "S";
        if (pct >= 0.90f) return "A";
        if (pct >= 0.80f) return "B";
        if (pct >= 0.70f) return "C";
        if (pct >= 0.60f) return "D";
        return "F";
    }
    public string GetRatingFC()
    {
        if (_ratingFC != null) return _ratingFC;
        if (_tallyMissed > 0) return "Miss";
        if (_tallyGood > 0 || _tallyBad > 0 || _tallyShit > 0) return "FC";
        if (_tallySick > 0) return "SFC";
        return "N/A";
    }
    public int GetSicks() => _tallySick;
    public int GetGoods() => _tallyGood;
    public int GetBads() => _tallyBad;
    public int GetShits() => _tallyShit;

    public void SetScore(int value) => _score = value;
    public void AddScore(int value) => _score += value;
    public int GetScore() => _score;

    public void SetMisses(int value) => _misses = value;
    public void AddMisses(int value) => _misses += value;
    public int GetMisses() => _misses;

    public void SetCombo(int value) => _combo = value;
    public void AddCombo(int value) => _combo += value;
    public int GetCombo() => _combo;

    public void SetHits(int value) => _totalNotesHit = value;
    public void AddHits(int value) => _totalNotesHit += value;
    public int GetHits() => _totalNotesHit;

    public void AddCharacterToList(string name, string type, float x, float y)
    {
        _characterList ??= new List<Character>();
        var newChar = new Character(name, x, y);
        newChar.LoadJsonData(Assets, name);
        newChar.LoadSprites(Game);
        _characterList.Add(newChar);
    }

    public void CharacterDance(string type)
    {
        Character target = type?.ToLowerInvariant() switch
        {
            "dad" or "opponent" => _opponent,
            "gf" or "girlfriend" => _girlfriend,
            _ => _boyfriend
        };
        target?.PlayAnimation("idle", true);
    }

    public void TriggerEvent(string name, string value1, string value2, float time)
    {
        var ev = new GameEvent { Name = name, Value = new[] { value1 ?? "", value2 ?? "" }, Time = time };
        // FunkinLua triggerEvent -> PlayState.triggerEvent: run the built-in event (Add Camera Zoom, Play Animation,
        // Change Character, ...) then stage eventCalled + onEvent. The port only called onEvent, so scripted events did nothing.
        ExecuteChartEvent(ev);
        DispatchEventCalled(ev, null, null);
    }

    private List<Character> _characterList;

    // Health bar color overrides (set by Lua setHealthBarColors)
    private Color _healthBarPlayerColor;
    private Color _healthBarOpponentColor;
    private bool _healthBarColorOverride;

    // Time bar color overrides (set by Lua setTimeBarColors)
    private Color _timeBarLeftColor = new Color(30, 30, 30);
    private Color _timeBarRightColor = Color.White;
    private bool _timeBarColorOverride;

    // Rating overrides (set by Lua setRatingPercent/Name/FC)
    private float? _ratingPercent;
    private string _ratingName;
    private string _ratingFC;

    public void AddHealth(float amount) => SetHealth(GetHealth() + amount);

    /// <summary>FunkinLua setHealthBarColors(left, right) -> healthBar.setColors: left = opponent side, right = player side;
    /// null/empty keeps that side's current colour (Bar.setColors).</summary>
    public void SetHealthBarColors(string left, string right)
    {
        if (!_healthBarColorOverride)
        {
            _healthBarOpponentColor = PsychCharColor(_opponent);
            _healthBarPlayerColor = PsychCharColor(_boyfriend);
        }
        if (!string.IsNullOrEmpty(left)) _healthBarOpponentColor = LuaUtils.ColorFromString(left);
        if (!string.IsNullOrEmpty(right)) _healthBarPlayerColor = LuaUtils.ColorFromString(right);
        _healthBarColorOverride = true;
    }

    /// <summary>FunkinLua setTimeBarColors(left, right) -> timeBar.setColors (defaults: left WHITE, right BLACK).</summary>
    public void SetTimeBarColors(string left, string right)
    {
        if (!_timeBarColorOverride) { _timeBarLeftColor = Color.White; _timeBarRightColor = Color.Black; }
        if (!string.IsNullOrEmpty(left)) _timeBarLeftColor = LuaUtils.ColorFromString(left);
        if (!string.IsNullOrEmpty(right)) _timeBarRightColor = LuaUtils.ColorFromString(right);
        _timeBarColorOverride = true;
    }

    public void SetRatingPercent(float value) => _ratingPercent = value;
    public void SetRatingName(string value) => _ratingName = value;
    public void SetRatingFC(string value) => _ratingFC = value;

    public void UpdateScoreText() { }

    public void ExitSong()
    {
        if (_weekSongs != null)
            Game.Scenes.ChangeScene(new StoryModeScene());
        else
            Game.Scenes.ChangeScene(new FreeplayScene());
    }

    #endregion

    #region Psych Engine PlayState overrides exposed to stages

    public bool StartCountdown() => PsychCountdownBegin(); // PlayState.startCountdown (PlayScene.PsychDialogue.cs)
    public void MoveCameraSection() { }      // TODO: hook up section-based camera movement
    public void MoveCamera(bool isDad)
    {
        if (isDad && _opponent != null)
        {
            _camTargetWorldX = _opponent.Position.X + _opponent.CameraOffsets[0];
            _camTargetWorldY = _opponent.Position.Y + _opponent.CameraOffsets[1];
        }
        else if (_boyfriend != null)
        {
            _camTargetWorldX = _boyfriend.Position.X + _boyfriend.CameraOffsets[0];
            _camTargetWorldY = _boyfriend.Position.Y + _boyfriend.CameraOffsets[1];
        }
    }

    #endregion

    #region Callback dispatch to all registered stages

    public void DispatchCreatePost()
    {
        PsychCreateRecalculate(); // PlayState.create: RecalculateRating(false, false) between startCallback and onCreatePost
        foreach (var stage in _stages)
            stage.CreatePost();
        CallOnLuas("onCreatePost");
        _psyDecStepLive = true; // scripts created after this (addLuaScript) see the running curDecStep
    }

    public void DispatchUpdate(float elapsed)
    {
        _psyPrevPosSec = _conductor?.SongPosition ?? 0.0; // position before this frame's advance (Psych super.update sees it)
        UpdatePsychExtraCharacters(elapsed);
        Engine.PsychEngine.LuaTweenManager.Update(elapsed);
        PhillyStreetsUpdate(elapsed); // phillyStreets.hxc onUpdate
        BlazinUpdate(elapsed); // phillyBlazin.hxc / blazin.hxc onUpdate
        Weekend1DevTick();
        UpdateCameraZoomTween(elapsed); // ZoomCamera event tween
        UpdateCameraFollowTween(elapsed); // PlayState.tweenCameraToPosition (PlayScene.CamTween.cs)
        foreach (var stage in _stages)
            stage.Update(elapsed);
        CallOnLuas("onUpdate", elapsed);
        if (PsychNotes && _conductor != null && _conductor.DeferEvents) _conductor.FlushDeferred(); // super.update: stepHit/beatHit of the previous frame's advance
        PsychScriptApiUpdate(); // PlayState.update 1692: curDecStep/curDecBeat after onUpdate
        VsUpdate(elapsed);
        PsychSoundsUpdate(elapsed); // tagged Lua sounds: fades + onSoundFinished
    }

    public void DispatchUpdatePost(float elapsed)
    {
        foreach (var stage in _stages)
            stage.UpdatePost(elapsed);
        CallOnLuas("onUpdatePost", elapsed);
    }

    public void DispatchCountdownTick(Gameplay.Countdown countdown, int num)
    {
        foreach (var stage in _stages)
            stage.CountdownTick(countdown, num);
        // Lua onCountdownTick(swagCounter 0..4) is called by the countdown loop itself (PlayScene.cs)
    }

    public void DispatchStartSong()
    {
        _startCallback?.Invoke();
        _psySongStarted = true;
        foreach (var stage in _stages)
            stage.StartSong();
        // PlayState.startSong 1258-1266: songLength = FlxG.sound.music.length; setOnScripts('songLength', songLength)
        if (PsychNotes) { SetOnLuas("songLength", songLength); PsychSetOnHScript("songLength", songLength); }
        CallOnLuas("onSongStart");
        VsSongStart();
    }

    private int _lastDispatchedSection = -1;

    /// <summary>Psych MusicBeatState.curBeat / curStep (HScript resolves curBeat via parentInstance).</summary>
    public int CurBeat { get; private set; }
    public int CurStep { get; private set; }

    public void DispatchBeatHit(int beat)
    {
        if (PsychNotes) PsychGroupSortOnBeat(); // PlayState.beatHit: notes.sort(FlxSort.byY, DESCENDING)
        VsBeatHit(beat);
        CurBeat = beat;
        // Psych: beatHit() runs inside stepHit() on every 4th step, so curStep is already beat*4.
        // (the Lua global curStep is only updated by stepHit AFTER beatHit: onBeatHit still sees the previous step in Psych)
        if (CurStep < beat * 4) { CurStep = beat * 4; if (!PsychNotes) SetOnLuas("curStep", CurStep); }
        if (!PsychNotes) // V-Slice path keeps the beat-time section dispatch
        {
            int section = CalculateCurrentSection(beat);
            if (section != _lastDispatchedSection)
            {
                _lastDispatchedSection = section;
                DispatchSectionHit(section);
            }
        }

        foreach (var stage in _stages)
        {
            stage.CurBeat = beat;
            stage.CurDecBeat = CurDecBeat;
            stage.BeatHit();
        }

        SetOnLuas("curBpm", Conductor?.BPM ?? 0);
        if ((Conductor?.BPM ?? 0) > 0) { SetOnLuas("crochet", 60000.0 / Conductor.BPM); SetOnLuas("stepCrochet", 15000.0 / Conductor.BPM); } // PlayState sectionHit changeBPM
        PhillyStreetsBeatHit(beat); // phillyStreets.hxc onBeatHit
        SetOnLuas("curBeat", beat);
        if (UsePsychHud) PsychHudBeat();
        if (PsychNotes && beat <= 0) return; // Psych MusicBeatState: stepHit (and beatHit inside it) only for curStep > 0
        if (PsychNotes) CallOnLuas("onBeatHit"); else CallOnLuas("onBeatHit", beat); // PlayState.beatHit: no args
    }

    private int CalculateCurrentSection(int beat)
    {
        if (Chart?.Sections == null || Chart.Sections.Count == 0) return 0;
        int accumulatedBeats = 0;
        for (int i = 0; i < Chart.Sections.Count; i++)
        {
            var section = Chart.Sections[i];
            int sectionBeats = section.LengthInSteps / 4;
            if (sectionBeats <= 0) sectionBeats = 4;
            if (accumulatedBeats + sectionBeats > beat)
                return i;
            accumulatedBeats += sectionBeats;
        }
        return Chart.Sections.Count - 1;
    }

    public void DispatchStepHit(int step)
    {
        VsStepHit(step);
        CurStep = step;
        foreach (var stage in _stages)
        {
            stage.CurStep = step;
            stage.CurDecStep = CurDecStep;
            stage.StepHit();
        }

        if (PsychNotes && step <= 0) return; // MusicBeatState.update: if(curStep > 0) stepHit()
        SetOnLuas("curStep", step);
        if (PsychNotes) CallOnLuas("onStepHit"); else CallOnLuas("onStepHit", step); // PlayState.stepHit: no args
        if (PsychNotes) PsychUpdateSection(step);
    }

    /// <summary>Psych MusicBeatState.updateSection (after stepHit): curSection starts at 0 without a hit; every section
    /// whose start step (sum of round(sectionBeats*4)) is reached fires sectionHit in order.</summary>
    private void PsychUpdateSection(int step)
    {
        if (_lastDispatchedSection < 0) _lastDispatchedSection = 0;
        int section = CalculateSectionFromStep(step);
        while (_lastDispatchedSection < section)
        {
            _lastDispatchedSection++;
            DispatchSectionHit(_lastDispatchedSection);
        }
    }

    private int CalculateSectionFromStep(int step)
    {
        if (Chart?.Sections == null || Chart.Sections.Count == 0) return Math.Max(0, step / 16);
        int acc = 0;
        for (int i = 0; i < Chart.Sections.Count; i++)
        {
            int len = Chart.Sections[i].LengthInSteps;
            if (len <= 0) len = 16;
            if (acc + len > step) return i;
            acc += len;
        }
        return Chart.Sections.Count + (step - acc) / 16; // getBeatsOnSection() = 4 past the chart
    }

    public void DispatchSectionHit(int section)
    {
        foreach (var stage in _stages)
        {
            stage.CurSection = section;
            stage.SectionHit();
        }

        CurSection = section;
        // PlayState.sectionHit: moveCameraSection() (-> onMoveCamera) before the zoom bop and onSectionHit
        if (PsychNotes && Chart?.Sections != null && section >= 0 && section < Chart.Sections.Count && !_forceEndSong && !_isCameraOnForcedPos)
        {
            var ms = Chart.Sections[section];
            string who = ms.GfSection ? "gf" : (ms.MustHitSection ? "boyfriend" : "dad");
            if (PsychCameraTarget(ms.GfSection ? 2 : (ms.MustHitSection ? 0 : 1)) is Microsoft.Xna.Framework.Vector2 pt) { _camTargetWorldX = pt.X; _camTargetWorldY = pt.Y; }
            CallOnScripts("onMoveCamera", who);
        }
        PsychSectionZoom();
        SetOnLuas("curSection", section);
        if (Chart?.Sections != null && section >= 0 && section < Chart.Sections.Count)
        {
            var sec = Chart.Sections[section];
            SetOnLuas("mustHitSection", sec?.MustHitSection ?? true);
            SetOnLuas("gfSection", sec?.GfSection ?? false);
            SetOnLuas("altAnim", sec?.AltAnim ?? false);
        }
        if (PsychNotes) CallOnLuas("onSectionHit"); else CallOnLuas("onSectionHit", section); // PlayState.sectionHit: no args
    }

    public void DispatchGoodNoteHit(Note note)
    {
        BlazinNoteHit(note, true); // pico-blazin / darnell-blazin / phillyBlazin onNoteHit
        AbotReactToNoteKind(note?.Kind); // nene/otis onNoteHit + onNoteMiss -> moveByNoteKind
        int psIdx = PsychNotes ? PsychNoteIndex(note) : 0;
        if (PsychNotes) { CallOnLuas("goodNoteHitPre", psIdx, note?.Lane ?? 0, note?.NoteType ?? "", false); PsychScoreOnHit(note, _bzLastRating, _psBotHit); }
        foreach (var stage in _stages)
            stage.GoodNoteHit(note);
        VsNoteHit(note, true);
        CallOnLuas("goodNoteHit", psIdx, note?.Lane ?? 0, note?.NoteType ?? "", false); // Psych: (membersIndex, noteData, noteType, isSustainNote)
        if (PsychNotes) PsychGroupInvalidateHead(note); // goodNoteHit: if(!note.isSustainNote) invalidateNote(note)
    }

    public void DispatchOpponentNoteHit(Note note)
    {
        BlazinNoteHit(note, false);
        AbotReactToNoteKind(note?.Kind); // nene/otis onNoteHit + onNoteMiss -> moveByNoteKind
        if (PsychNotes && !string.Equals(_songName, "tutorial", StringComparison.OrdinalIgnoreCase)) _psychCamZooming = true; // PlayState.opponentNoteHit
        int psIdx = PsychNotes ? PsychNoteIndex(note) : 0;
        if (PsychNotes) CallOnLuas("opponentNoteHitPre", psIdx, note?.Lane ?? 0, note?.NoteType ?? "", false);
        foreach (var stage in _stages)
            stage.OpponentNoteHit(note);
        VsNoteHit(note, false);
        CallOnLuas("opponentNoteHit", psIdx, note?.Lane ?? 0, note?.NoteType ?? "", false); if (PsychNotes) PsychGroupInvalidateHead(note); if (DevMissLog) Console.WriteLine($"[OPPHIT] t={SongPositionSeconds:F3} nt={note?.Time:F3} type={note?.NoteType} health={_health:F4}"); // Psych: (membersIndex, noteData, noteType, isSustainNote)
    }

    public void DispatchNoteMiss(Note note)
    {
        BlazinNoteMiss(note);
        AbotReactToNoteKind(note?.Kind); // nene/otis onNoteHit + onNoteMiss -> moveByNoteKind
        if (PsychNotes) PsychScoreOnMiss(); // noteMissCommon
        foreach (var stage in _stages)
            stage.NoteMiss(note);
        VsNoteMiss(note);
        CallOnLuas("noteMiss", PsychNotes ? PsychNoteIndex(note) : 0, note?.Lane ?? 0, note?.NoteType ?? "", PsychNotes && _psMissIsSustain); // Psych: (membersIndex, noteData, noteType, isSustainNote)
        if (PsychNotes && !_psMissIsSustain) PsychGroupInvalidateHead(note);
    }

    public void DispatchNoteMissPress(int direction)
    {
        if (PsychNotes) PsychScoreOnMiss(); // noteMissCommon(direction)
        foreach (var stage in _stages)
            stage.NoteMissPress(direction);
        CallOnLuas("noteMissPress", direction);
    }

    public void DispatchEventCalled(GameEvent ev, float? flValue1, float? flValue2)
    {
        var (val1, val2) = GetPsychEventValues(ev.Value);
        foreach (var stage in _stages)
            stage.EventCalled(ev.Name, val1, val2, flValue1, flValue2, ev.Time);
        if (IsPortSyntheticEvent(ev)) return; // port-made FocusCamera: not a Psych event
        if (PsychNotes) CallOnScripts("onEvent", ev.Name ?? "", val1 ?? "", val2 ?? "", ev.Time * 1000.0); // PlayState.triggerEvent: strumTime in ms
        else CallOnLuas("onEvent", ev.Name ?? "", val1 ?? "", val2 ?? "", ev.Time);
    }

    public void DispatchEventPushed(GameEvent ev)
    {
        foreach (var stage in _stages)
            stage.EventPushed(ev);
    }

    public void DispatchAllEventsPushed()
    {
        if (Chart?.Events == null) return;
        foreach (var ev in Chart.Events)
            DispatchEventPushed(ev);
    }

    #endregion
}
