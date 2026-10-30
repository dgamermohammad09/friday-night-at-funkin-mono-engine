using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using FNF_MonoGame.Engine.PsychEngine;
using PNote = FNF_MonoGame.Engine.PsychEngine.Note;
using ChartNote = FNF_MonoGame.Gameplay.Note;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// PSYCH-FULL audit fixes: Psych Engine 1.0.4 PlayState script contract for Psych-mod songs.
///  - FunkinLua.new preset variables that need the PlayState (songName = SONG.song, scrollSpeed, chartPath, boyfriendName...).
///  - PlayState score state (songScore/songHits/songMisses/combo/totalPlayed/totalNotesHit/ratingsData, ratingPercent/Name/FC)
///    with popUpScore / noteMissCommon / RecalculateRating / updateScore and their script callbacks.
///  - note callbacks with notes.members.indexOf(note), the *Pre variants, onSpawnNote at spawn time, eventEarlyTrigger.
/// Source: states/PlayState.hx (create 395-640, update 1673-1870, popUpScore 2548, noteMissCommon 2894,
/// goodNoteHit 3023, RecalculateRating 3488), psychlua/FunkinLua.hx 60-200.
/// </summary>
public partial class PlayScene
{
    // ---------------------------------------------------------------- score state (PlayState fields)
    private int _psScore, _psHits, _psMisses, _psCombo, _psTotalPlayed;
    private double _psTotalNotesHit, _psRatingPercent;
    private string _psRatingName = "?", _psRatingFC = "";
    private List<Rating> _psRatingsData;
    /// <summary>Set by OnNoteHit right before DispatchGoodNoteHit (botplay hit = cpuControlled path).</summary>
    private bool _psBotHit;
    /// <summary>noteMiss isSustainNote argument (dropped hold = a tail piece miss).</summary>
    private bool _psMissIsSustain;

    /// <summary>PlayState.keyReleased: onKeyReleasePre (Stop skips) then onKeyRelease; nothing in botplay / before the countdown.</summary>
    internal void PsychKeyReleased(int key)
    {
        if (_botplay || !_psychStartedCountdown) return;
        if (CallOnScripts("onKeyReleasePre", key) == FunkinLua.Function_Stop) return;
        CallOnScripts("onKeyRelease", key);
    }

    /// <summary>PlayState.noteMissPress(direction) (ghost tapping off): noteMissCommon with pressMissDamage 0.05,
    /// missnote1-3 at volume 0.1-0.2, stage noteMissPress, callOnScripts('noteMissPress').</summary>
    internal void PsychNoteMissPress(int lane)
    {
        int lastCombo = _psCombo;
        _health = Math.Clamp(_health - 0.05f * PsychHealthLoss, HEALTH_MIN, HEALTH_MAX);
        if (_boyfriend != null) _boyfriend.Miss(lane);
        if (lastCombo > 5 && _girlfriend != null && _girlfriend.HasAnimation("sad")) _girlfriend.PlayAnimationLocked("sad");
        Audio.PlaySound($"missnote{_random.Next(1, 4)}", 0.1f + (float)_random.NextDouble() * 0.1f);
        DispatchNoteMissPress(lane);
    }

    /// <summary>PlayState.ratingStuff (public static, scripts replace it through setPropertyFromClass).</summary>
    public static List<object[]> ratingStuff { get; set; } = DefaultRatingStuff();
    private static List<object[]> DefaultRatingStuff() => new List<object[]> {
        new object[] { "You Suck!", 0.2 }, new object[] { "Shit", 0.4 }, new object[] { "Bad", 0.5 }, new object[] { "Bruh", 0.6 },
        new object[] { "Meh", 0.69 }, new object[] { "Nice", 0.7 }, new object[] { "Good", 0.8 }, new object[] { "Great", 0.9 },
        new object[] { "Sick!", 1.0 }, new object[] { "Perfect!!", 1.0 } };

    public List<Rating> ratingsData => _psRatingsData ??= Rating.LoadDefault();
    public int songHits { get => _psHits; set => _psHits = value; }
    public double totalPlayed { get => _psTotalPlayed; set => _psTotalPlayed = (int)value; }
    public double totalNotesHit { get => _psTotalNotesHit; set => _psTotalNotesHit = value; }
    public double ratingPercent { get => _psRatingPercent; set => _psRatingPercent = value; }
    public string ratingName { get => _psRatingName; set => _psRatingName = value; }
    public string ratingFC { get => _psRatingFC; set => _psRatingFC = value; }
    /// <summary>PlayState.gfSpeed (Set GF Speed event, characterBopper).</summary>
    public int gfSpeed { get => _gfSpeed; set { _gfSpeed = value; PsychApplyGfSpeed(); } }
    internal void PsychApplyGfSpeed() { if (PsychNotes && _girlfriend != null) _girlfriend.SetPsychDanceInterval((int)Math.Round(_gfSpeed * (double)_girlfriend.danceEveryNumBeats, MidpointRounding.AwayFromZero)); }
    public bool showCombo { get; set; } = false;
    public bool showComboNum { get; set; } = true;
    public bool showRating { get; set; } = true;
    public bool skipCountdown { get; set; }
    public bool skipArrowStartTween { get; set; }
    public string introSoundsSuffix { get; set; } = "";
    // PlayState statics (instance getProperty('stageUI') is nil in Psych; use getPropertyFromClass('states.PlayState', ..))
    public static string stageUI { get; set; } = "normal";
    public static string uiPrefix { get; set; } = "";
    public static string uiPostfix { get; set; } = "";
    public static bool isPixelStage => stageUI == "pixel" || stageUI.EndsWith("-pixel");
    /// <summary>PlayState.SONG (static in Psych; getPropertyFromClass('states.PlayState', 'SONG.song')).</summary>
    public PsychSongFacade SONG => new PsychSongFacade(this);
    public sealed class PsychSongFacade
    {
        private readonly PlayScene _p; public PsychSongFacade(PlayScene p) { _p = p; }
        public string song => _p._chart?.DisplayName ?? _p._chart?.SongName ?? "";
        public double bpm => _p._chart?.BPM ?? 100;
        public double speed { get => _p._chart?.Speed ?? 1; set { if (_p._chart != null) _p._chart.Speed = (float)value; } }
        public bool needsVoices => _p._chart?.NeedsVoices ?? true;
        public string player1 => _p._chart?.PlayerCharacter;
        public string player2 => _p._chart?.OpponentCharacter;
        public string gfVersion => _p._chart?.GirlfriendCharacter;
        public string stage => _p._chart?.Stage;
        public string format => "psych_v1";
        public double offset => 0;
        public string arrowSkin => null;
        public string splashSkin => null;
    }
    /// <summary>strumLineNotes / playerStrums / opponentStrums groups (members + length) for getProperty / HScript.</summary>
    public PsychStrumGroup strumLineNotes => new PsychStrumGroup(this, 0, 8);
    public PsychStrumGroup opponentStrums => new PsychStrumGroup(this, 0, 4);
    public PsychStrumGroup playerStrums => new PsychStrumGroup(this, 4, 4);
    public sealed class PsychStrumGroup
    {
        private readonly PlayScene _p; private readonly int _start, _n;
        public PsychStrumGroup(PlayScene p, int start, int n) { _p = p; _start = start; _n = n; }
        public int length => _n;
        public System.Collections.Generic.List<PsychStrumView> members { get { var l = new System.Collections.Generic.List<PsychStrumView>(); for (int i = 0; i < _n; i++) l.Add(new PsychStrumView(_p, _start + i)); return l; } }
    }
    public sealed class PsychStrumView
    {
        private readonly PlayScene _p; private readonly int _i;
        public PsychStrumView(PlayScene p, int i) { _p = p; _i = i; }
        private StrumMod M => _p.StrumMods != null && _i < _p.StrumMods.Length ? _p.StrumMods[_i] : null;
        public float x { get => (M?.OffsetX ?? 0) + _p.PsychStrumBaseX(_i); set { if (M != null) M.OffsetX = value - _p.PsychStrumBaseX(_i); } }
        public float y { get => (M?.OffsetY ?? 0) + _p.PsychStrumBaseY(_i); set { if (M != null) M.OffsetY = value - _p.PsychStrumBaseY(_i); } }
        public float angle { get => M?.Angle ?? 0; set { if (M != null) M.Angle = value; } }
        public float alpha { get => M?.Alpha ?? 1; set { if (M != null) M.Alpha = value; } }
        public bool visible { get => M?.Visible ?? true; set { if (M != null) M.Visible = value; } }
        public float direction { get => M?.Direction ?? 90; set { if (M != null) M.Direction = value; } }
        public int noteData => _i % 4;
        public int player => _i < 4 ? 0 : 1;
        public bool downScroll => ClientPrefs.Data.DownScroll;
    }
    // PlayState.songLength = FlxG.sound.music.length at startSong (inst); FunkinLua.new presets FlxG.sound.music.length = whatever
    // music plays at create (menu music, 0 when none).
    public double songLength => _psySongStarted
        ? (Audio != null && Audio.MusicLength > 0 ? Audio.MusicLength * 1000.0 : (_chart?.SongLength ?? 0) * 1000.0)
        : (Audio != null && Audio.MusicPlaying && Audio.MusicLength > 0 ? Audio.MusicLength * 1000.0 : 0.0);
    public double songPercent { get; private set; }
    public float playbackRate => _playbackRate > 0f ? _playbackRate : 1f;
    public float songSpeed { get => _scrollSpeed > 0f ? _scrollSpeed : (_chart?.Speed ?? 1f); set => _scrollSpeed = value; }
    public float healthGain { get => PsychHealthGain; set => PsychHealthGain = value; }
    public float healthLoss { get => PsychHealthLoss; set => PsychHealthLoss = value; }
    public bool instakillOnMiss => ClientPrefs.Data.Instakill;
    public bool practiceMode { get => ClientPrefs.Data.Practice; set { } }
    public bool guitarHeroSustains => ClientPrefs.Data.GuitarHeroSustains;
    public float BF_X => _psychStageSlot(0, 0); public float BF_Y => _psychStageSlot(0, 1);
    public float DAD_X => _psychStageSlot(1, 0); public float DAD_Y => _psychStageSlot(1, 1);
    public float GF_X => _psychStageSlot(2, 0); public float GF_Y => _psychStageSlot(2, 1);

    private float _psychStageSlot(int who, int axis)
    {
        var c = who == 0 ? _boyfriend : who == 1 ? _opponent : _girlfriend;
        if (c == null) return 0f;
        float pa = c.PsychCharacterOffset != null && c.PsychCharacterOffset.Length > axis ? c.PsychCharacterOffset[axis] : 0f;
        return (axis == 0 ? c.X : c.Y) - pa;
    }

    /// <summary>PlayState.eventNotes: events still to fire (strumTime in ms incl. eventEarlyTrigger).</summary>
    public sealed class PsychEventNote { public double strumTime; public string @event, value1, value2; public Gameplay.GameEvent Src; }
    public List<PsychEventNote> eventNotes
    {
        get
        {
            var l = new List<PsychEventNote>();
            if (_chart?.Events == null) return l;
            foreach (var e in _chart.Events)
            {
                if (e.Fired || IsPortSyntheticEvent(e)) continue;
                var (v1, v2) = GetPsychEventValues(e.Value);
                l.Add(new PsychEventNote { strumTime = e.Time * 1000.0, @event = e.Name, value1 = v1 ?? "", value2 = v2 ?? "", Src = e });
            }
            return l;
        }
    }

    /// <summary>FocusCamera events are generated by the port's chart parser from mustHitSection (camera driving);
    /// Psych has no such chart event, scripts must never see them.</summary>
    internal bool IsPortSyntheticEvent(Gameplay.GameEvent e) => PsychNotes && e != null && e.PortSynthetic;

    /// <summary>PlayState.notes (spawned notes group): Psych inserts every spawned note at index 0 and sorts by y
    /// (= strumTime descending) on beatHit, so members are newest first.</summary>
    internal List<PNote> PsychNotesMembers()
    {
        if (PsychNotes) return _psyGroup; // PlayScene.PsychNoteGroup.cs (heads + tail pieces, Psych order)
        var src = _noteField?.GetAllActiveNotes();
        if (src == null) return new List<PNote>();
        var l = new List<PNote>(src.Count);
        for (int i = src.Count - 1; i >= 0; i--) l.Add(src[i]);
        l.Sort((a, b) => b.Time.CompareTo(a.Time));
        return l;
    }
    internal int PsychNoteIndex(object note)
    {
        if (note == null) return -1;
        if (PsychNotes && note is PNote pl) return PsychGroupIndexOfHead(pl);
        var m = PsychNotesMembers();
        for (int i = 0; i < m.Count; i++)
            if (ReferenceEquals(m[i], note) || (note is PNote pn && pn.Source != null && ReferenceEquals(m[i].Source, pn.Source))) return i;
        return -1;
    }

    /// <summary>Psych gfGroup.scrollFactor = 0.95 (PlayState.create); dad/bf groups 1.</summary>
    internal Microsoft.Xna.Framework.Vector2 PsychCharScrollFactor(Gameplay.Character c)
        => ReferenceEquals(c, _girlfriend) && PsychNotes ? new Microsoft.Xna.Framework.Vector2(0.95f, 0.95f) : Microsoft.Xna.Framework.Vector2.One;

    /// <summary>Untrimmed size of the frame a Lua sprite shows (flixel frameWidth/frameHeight).</summary>
    internal static (float W, float H) PsychFrameSize(StaticStageProp p) { var f = PsychFrameInfo(p); return (f.W, f.H); }

    /// <summary>getPixelColor: FlxSprite.pixels.getPixel32(x, y) (ARGB as a signed Int, BLACK when out of range / missing).</summary>
    internal int PsychGetPixel32(string tag, int x, int y)
    {
        const int Black = unchecked((int)0xFF000000);
        object o = ResolvePsychObject(tag);
        if (o is not StaticStageProp p || p.Texture == null) return Black;
        var tex = p.Texture;
        if (x < 0 || y < 0 || x >= tex.Width || y >= tex.Height) return 0;
        try
        {
            var px = new Microsoft.Xna.Framework.Color[1];
            tex.GetData(0, new Microsoft.Xna.Framework.Rectangle(x, y, 1, 1), px, 0, 1);
            var c = px[0];
            return unchecked((int)(((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B));
        }
        catch { return Black; }
    }

    internal void PsychUpdateScoreTextNow() { if (PsychNotes) scoreTxt.text = PsychScoreTextPsych(); }

    /// <summary>addHScript: data/<file>.hx (or the given path) through Mods.modFolders, once.</summary>
    internal bool PsychAddHScript(string file)
    {
        if (string.IsNullOrEmpty(file)) return false;
        string f = file.EndsWith(".hx") ? file : file + ".hx";
        foreach (var root in GetPsychScriptRoots())
        {
            string p = Path.Combine(root, f);
            if (!global::FNF_MonoGame.Engine.FsCI.FileExists(p)) continue;
            foreach (var st in _stages) if (st is Gameplay.HScriptStage hs && string.Equals(Path.GetFullPath(hs.Engine.ScriptName), Path.GetFullPath(p), StringComparison.OrdinalIgnoreCase)) return false;
            var stage = new Gameplay.HScriptStage(this, p);
            return true;
        }
        FunkinLua.LuaTrace("addHScript: The script \"" + file + "\" doesn't exist!", false, false);
        return false;
    }
    internal bool PsychRemoveHScript(string file)
    {
        string f = (file ?? "").EndsWith(".hx") ? file : file + ".hx";
        foreach (var st in _stages.ToArray())
            if (st is Gameplay.HScriptStage hs && hs.Engine.ScriptName.Replace('\\', '/').EndsWith(f.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
            { _stages.Remove(st); return true; }
        return false;
    }
    internal void PsychSetOnHScript(string name, object value)
    {
        if (string.IsNullOrEmpty(name)) return;
        foreach (var st in _stages) if (st is Gameplay.HScriptStage hs) hs.Engine.SetVariable(name, value);
    }

    // ---------------------------------------------------------------- preset variables (FunkinLua.new)
    /// <summary>Called at the end of FunkinLua.SetPresetVariables: values that need the PlayState.</summary>
    internal void PsychPresetVars(Action<string, object> set, string scriptName)
    {
        if (!PsychNotes) return;
        string songDisplay = _chart?.DisplayName ?? _chart?.SongName ?? _songName ?? "";
        set("songName", songDisplay);
        set("songPath", Difficulty.FormatToSongPath(songDisplay));
        set("loadedSongName", _songName ?? "");
        set("loadedSongPath", Difficulty.FormatToSongPath(_songName ?? ""));
        set("chartPath", PsychRelPath(_chart?.PsychChartPath).Replace('/', '\\')); // Song.chartPath: Windows path (backslashes)
        set("bpm", (double)(_chart?.BPM ?? 100f));
        set("scrollSpeed", (double)(_chart?.Speed ?? 1f));
        set("songLength", songLength);
        set("difficultyNameTranslation", Difficulty.GetString());
        set("hasVocals", _chart?.NeedsVoices ?? true);
        set("week", PsychWeekFileName());
        string mod = PsychModFolderName();
        set("currentModDirectory", mod);
        set("modFolder", PsychScriptModFolder(scriptName));
        set("scriptName", PsychRelPath(scriptName));
        set("noteSkinPostfix", PsychSkinPostfix(ClientPrefs.Data.NoteSkin, "Default"));
        set("splashSkinPostfix", PsychSkinPostfix(ClientPrefs.Data.SplashSkin, "Psych"));
        int sec = CurSection;
        var s = _chart?.Sections != null && sec >= 0 && sec < _chart.Sections.Count ? _chart.Sections[sec] : null;
        set("curSection", sec);
        set("curBeat", CurBeat); set("curStep", CurStep);
        set("mustHitSection", s != null && s.MustHitSection);
        set("altAnim", s != null && s.AltAnim);
        set("gfSection", s != null && s.GfSection);
        set("score", _psScore); set("misses", _psMisses); set("hits", _psHits); set("combo", _psCombo);
        set("rating", _psRatingPercent); set("ratingName", _psRatingName); set("ratingFC", _psRatingFC);
        set("totalPlayed", (double)_psTotalPlayed); set("totalNotesHit", _psTotalNotesHit);
        set("botPlay", _botplay);
        set("playbackRate", (double)playbackRate);
        set("healthGainMult", (double)PsychHealthGain); set("healthLossMult", (double)PsychHealthLoss);
        set("boyfriendName", _boyfriend?.Name ?? _chart?.PlayerCharacter ?? "bf");
        set("dadName", _opponent?.Name ?? _chart?.OpponentCharacter ?? "dad");
        set("gfName", _girlfriend?.Name ?? _chart?.GirlfriendCharacter ?? "gf");
        set("defaultBoyfriendX", (double)BF_X); set("defaultBoyfriendY", (double)BF_Y);
        set("defaultOpponentX", (double)DAD_X); set("defaultOpponentY", (double)DAD_Y);
        set("defaultGirlfriendX", (double)GF_X); set("defaultGirlfriendY", (double)GF_Y);
        set("curStage", _chart?.Stage ?? "stage");
        double pds = _psyDecStepLive ? PsychDecStep() : 0.0; // MusicBeatState.curDecStep = 0 until the first update
        set("curDecStep", pds); set("curDecBeat", pds / 4.0);
    }

    private static string PsychSkinPostfix(string skin, string def)
    {
        // Note.getNoteSkinPostfix / NoteSplash.getSplashSkinPostfix: '' for the default skin, else '-' + trim().toLowerCase().replace(' ', '_')
        if (string.IsNullOrEmpty(skin) || skin == def) return "";
        return "-" + skin.Trim().ToLowerInvariant().Replace(' ', '_');
    }

    /// <summary>Psych keeps paths relative to the game folder ('mods/X/data/song/a.lua').</summary>
    internal static string PsychRelPath(string full)
    {
        if (string.IsNullOrEmpty(full)) return "";
        try
        {
            string root = Path.GetFullPath(PsychPaths.Mods());
            string f = Path.GetFullPath(full);
            if (f.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return ("mods/" + f.Substring(root.Length).TrimStart('\\', '/')).Replace('\\', '/');
        }
        catch { }
        return full.Replace('\\', '/');
    }

    private string PsychModFolderName()
    {
        if (string.IsNullOrEmpty(_modPath)) return "";
        return Path.GetFileName(_modPath.TrimEnd('\\', '/'));
    }

    /// <summary>FunkinLua.new: modFolder = myFolder[1] when the script is inside mods/&lt;current or global mod&gt;/.</summary>
    private string PsychScriptModFolder(string scriptName)
    {
        string rel = PsychRelPath(scriptName);
        var parts = rel.Split('/');
        if (parts.Length > 2 && parts[0] == "mods")
        {
            string m = parts[1];
            if (string.Equals(m, PsychModFolderName(), StringComparison.OrdinalIgnoreCase)) return m;
            foreach (var g in PsychMods.GetAllMods())
                if (g.Enabled && g.IsGlobal && string.Equals(g.Name, m, StringComparison.OrdinalIgnoreCase)) return m;
        }
        return null;
    }

    private string PsychWeekFileName()
    {
        try
        {
            if (string.IsNullOrEmpty(_modPath)) return "";
            string wdir = Path.Combine(_modPath, "weeks");
            if (!global::FNF_MonoGame.Engine.FsCI.DirectoryExists(wdir)) return "";
            foreach (var f in global::FNF_MonoGame.Engine.FsCI.GetFiles(wdir, "*.json"))
            {
                string txt = global::FNF_MonoGame.Engine.FsCI.ReadAllText(f);
                if (txt.IndexOf("\"" + (_chart?.SongName ?? _songName) + "\"", StringComparison.OrdinalIgnoreCase) >= 0)
                    return Path.GetFileNameWithoutExtension(f);
            }
        }
        catch { }
        return "";
    }

    /// <summary>Set by DispatchStartSong (PlayState.startSong: startingSong = false).</summary>
    internal bool _psySongStarted;
    private bool _psyDecStepLive;
    /// <summary>Psych Conductor.songPosition (ms): create/startCountdown set -crochet*5 (PlayState 480/983), the countdown
    /// advances it by elapsed (update 1723) until startSong at &gt;= 0; afterwards the song clock. The port's conductor
    /// stays at 0 during the countdown (separate timer), so rebuild it from the countdown timer.</summary>
    internal double PsychSongPosMs()
    {
        double pos = (_conductor?.SongPosition ?? 0.0) * 1000.0;
        if (!PsychNotes || _psySongStarted || _conductor == null) return pos;
        double c = _conductor.Crochet;
        double el = _countdownActive ? (Math.Min(_countdownStep, 5) * c + _countdownTimer) : 0;
        return Math.Min(0.0, (-c * 5 + el) * 1000.0);
    }

    /// <summary>MusicBeatState.updateCurStep: lastChange.stepTime + (songPosition - noteOffset - lastChange.songTime) / stepCrochet.</summary>
    internal double PsychDecStep()
    {
        if (_conductor == null || _chart == null) return 0;
        double posMs = PsychSongPosMs() - ClientPrefs.Data.NoteOffset;
        double stepTime = 0, songTime = 0; float bpm = _chart.BPM > 0 ? _chart.BPM : 100f;
        if (_chart.BPMChanges != null)
            foreach (var c in _chart.BPMChanges)
            {
                double ct = c.Time * 1000.0;
                if (ct > posMs || ct <= 0) { if (ct > posMs) break; continue; }
                stepTime += (ct - songTime) / (15000.0 / bpm);
                songTime = ct; bpm = c.Bpm;
            }
        return stepTime + (posMs - songTime) / (15000.0 / bpm);
    }

    // ---------------------------------------------------------------- per-frame (PlayState.update 1690-1693, 1765-1790)
    internal void PsychScriptApiUpdate()
    {
        if (!PsychNotes) return;
        double ds = PsychDecStep();
        if (_psyDecStepLive) { SetOnLuas("curDecStep", ds); SetOnLuas("curDecBeat", ds / 4.0); }
        double len = songLength;
        if (len > 0) songPercent = Math.Max(0, _conductor.SongPosition * 1000.0 - ClientPrefs.Data.NoteOffset) / len;
        // PlayState.update: timeTxt.text = FlxStringUtil.formatTime(secondsTotal) (not for 'Song Name')
        if (ClientPrefs.Data.TimeBarType != "Song Name" && len > 0)
        {
            double curTime = Math.Max(0, _conductor.SongPosition * 1000.0 - ClientPrefs.Data.NoteOffset);
            double calc = ClientPrefs.Data.TimeBarType == "Time Elapsed" ? curTime : len - curTime;
            int secs = Math.Max(0, (int)Math.Floor(calc / 1000.0));
            timeTxt.text = (secs / 60) + ":" + (secs % 60).ToString("00");
        }
        // backend.Conductor statics for scripts (Conductor.songPosition / bpm / crochet in ms)
        PsychConductor.songPosition = (float)PsychSongPosMs();
        PsychConductor.crochet = (float)(60000.0 / Math.Max(1.0, _conductor.BPM));
        PsychConductor.bpm = (float)_conductor.BPM;
    }

    /// <summary>NoteField spawn hook: callOnLuas('onSpawnNote', [indexOf (0: notes.insert(0,..)), noteData, noteType, isSustainNote, strumTime]).</summary>
    internal void PsychOnNoteSpawned(PNote n)
    {
        if (!PsychNotes || n == null) return;
        PsychGroupSpawn(); // Lua onSpawnNote for the head (and any due tail pieces) from the Psych-side unspawnNotes
        CallOnHScript("onSpawnNote", n);
    }

    // ---------------------------------------------------------------- scoring (popUpScore / noteMissCommon / RecalculateRating)
    /// <summary>goodNoteHit part before the stage/script calls: combo++ and popUpScore for heads.</summary>
    private void PsychScoreOnHit(PNote note, string rating, bool botplay)
    {
        if (!PsychNotes || note == null || note.HitCausesMiss) return;
        _psCombo++; if (_psCombo > 9999) _psCombo = 9999;
        var data = ratingsData;
        int ri = rating switch { "sick" => 0, "good" => 1, "bad" => 2, "shit" => 3, _ => 0 };
        if (ri >= data.Count) ri = data.Count - 1;
        var r = data[ri];
        _psTotalNotesHit += r.RatingMod;
        r.Hits++;
        if (!botplay)
        {
            _psScore += r.Score;
            _psHits++;
            _psTotalPlayed++;
            PsychRecalculateRating(false);
        }
    }

    /// <summary>noteMissCommon bookkeeping (health / anims stay in the engine miss path).</summary>
    private void PsychScoreOnMiss()
    {
        if (!PsychNotes) return;
        _psCombo = 0;
        _psScore -= 10;
        if (!_forceEndSong) _psMisses++;
        _psTotalPlayed++;
        PsychRecalculateRating(true);
    }

    /// <summary>PlayState.RecalculateRating(badHit, scoreBop).</summary>
    internal void PsychRecalculateRating(bool badHit = false, bool scoreBop = true)
    {
        SetOnLuas("score", _psScore); SetOnLuas("misses", _psMisses); SetOnLuas("hits", _psHits); SetOnLuas("combo", _psCombo);
        string ret = CallOnScripts("onRecalculateRating");
        if (ret != FunkinLua.Function_Stop)
        {
            _psRatingName = "?";
            if (_psTotalPlayed != 0)
            {
                _psRatingPercent = Math.Min(1, Math.Max(0, _psTotalNotesHit / _psTotalPlayed));
                var rs = ratingStuff;
                _psRatingName = Convert.ToString(rs[rs.Count - 1][0], CultureInfo.InvariantCulture);
                if (_psRatingPercent < 1)
                    for (int i = 0; i < rs.Count - 1; i++)
                        if (_psRatingPercent < Convert.ToDouble(rs[i][1], CultureInfo.InvariantCulture)) { _psRatingName = Convert.ToString(rs[i][0], CultureInfo.InvariantCulture); break; }
            }
            PsychFullComboFunction();
        }
        SetOnLuas("rating", _psRatingPercent); SetOnLuas("ratingName", _psRatingName); SetOnLuas("ratingFC", _psRatingFC);
        SetOnLuas("totalPlayed", (double)_psTotalPlayed); SetOnLuas("totalNotesHit", _psTotalNotesHit);
        PsychUpdateScore(badHit, scoreBop);
    }

    private void PsychFullComboFunction()
    {
        var d = ratingsData;
        int sicks = d[0].Hits, goods = d[1].Hits, bads = d[2].Hits, shits = d[3].Hits;
        _psRatingFC = "";
        if (_psMisses == 0)
        {
            if (bads > 0 || shits > 0) _psRatingFC = "FC";
            else if (goods > 0) _psRatingFC = "GFC";
            else if (sicks > 0) _psRatingFC = "SFC";
        }
        else
        {
            if (_psMisses < 10) _psRatingFC = "SDCB";
            else _psRatingFC = "Clear";
        }
    }

    /// <summary>PlayState.updateScore(miss, scoreBop): preUpdateScore (Stop skips), updateScoreText, onUpdateScore.</summary>
    internal void PsychUpdateScore(bool miss = false, bool scoreBop = true)
    {
        string ret = CallOnScripts("preUpdateScore", miss);
        if (ret == FunkinLua.Function_Stop) return;
        scoreTxt.text = PsychScoreTextPsych(); _psScoreTextDirty = false; // updateScoreText
        CallOnScripts("onUpdateScore", miss);
    }
    private bool _psScoreTextDirty = true;

    /// <summary>PlayState.updateScoreText().</summary>
    internal string PsychScoreTextPsych()
    {
        string str = _psRatingName;
        if (_psTotalPlayed != 0)
        {
            double percent = Math.Floor(_psRatingPercent * 100 * 100) / 100; // CoolUtil.floorDecimal(x, 2)
            str += " (" + percent.ToString(CultureInfo.InvariantCulture) + "%) - " + _psRatingFC;
        }
        if (!instakillOnMiss) return $"Score: {_psScore} | Misses: {_psMisses} | Rating: {str}";
        return $"Score: {_psScore} | Rating: {str}";
    }

    /// <summary>PlayState.create 612: RecalculateRating(false, false) after startCallback, before onCreatePost.</summary>
    internal void PsychCreateRecalculate()
    {
        if (!PsychNotes) return;
        PsychRecalculateRating(false, false);
    }

    /// <summary>PlayState.create 606-610: eventEarlyTrigger for every event after all scripts were loaded; non-zero return moves the event earlier.</summary>
    internal void PsychEventEarlyTriggers()
    {
        if (!PsychNotes || _chart?.Events == null) return;
        bool moved = false;
        foreach (var ev in _chart.Events)
        {
            if (IsPortSyntheticEvent(ev)) continue;
            var (v1, v2) = GetPsychEventValues(ev.Value);
            double t = ev.Time * 1000.0;
            object r = CallOnScriptsValue("eventEarlyTrigger", ev.Name ?? "", v1 ?? "", v2 ?? "", t);
            double early = 0;
            if (r is double d) early = d; else if (r is float f) early = f; else if (r is int i) early = i;
            else if (r is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var ds)) early = ds;
            if (early == 0 && ev.Name == "Kill Henchmen") early = 280;
            if (early != 0) { ev.Time -= early / 1000.0; moved = true; }
        }
        if (moved) _chart.Events = _chart.Events.OrderBy(e => e.Time).ToList();
    }

    /// <summary>callOnScripts returning the first non-null Lua value (eventEarlyTrigger needs the number).</summary>
    private object CallOnScriptsValue(string func, params object[] args)
    {
        object result = null;
        foreach (var script in FunkinLua.RunningScripts.ToArray())
        {
            if (script.Closed) continue;
            try
            {
                var r = script.Call(func, args);
                if (r != null && r.Type != MoonSharp.Interpreter.DataType.Nil && r.Type != MoonSharp.Interpreter.DataType.Void)
                {
                    object o = r.ToObject();
                    if (o is string s && (s == FunkinLua.Function_Continue)) continue;
                    result ??= o;
                }
            }
            catch (Exception ex) { Console.WriteLine($"[PSYCH-LUA] {func} error in {script.ScriptName}: {ex.Message}"); }
        }
        return result;
    }
}
