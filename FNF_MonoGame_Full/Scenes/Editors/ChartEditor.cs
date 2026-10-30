using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Newtonsoft.Json.Linq;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes.Editors;

/// <summary>
/// Merged chart editor: V-Slice ChartEditorState layout (opponent lanes left, player lanes right, event lane, playhead
/// line, playbar, metronome + hitsounds, waveform) with Psych ChartingState features (sections / mustHitSection,
/// Psych event names + 2 values, note types, BPM changes, difficulty files). Loads/saves Psych JSON and V-Slice
/// chart + metadata JSON.
/// Mouse: click empty cell = place (drag down = sustain), click note = select, right click = delete.
/// Keys: SPACE play, W/S or wheel scroll, A/D section, 1-8 place at playhead, Q/E sustain, DEL delete, U undo,
/// +/- zoom, [ ] snap, M metronome, H hitsounds, F1 save Psych, F2 save V-Slice, ENTER playtest.
/// </summary>
public class ChartEditor : EditorBase
{
    protected override string EdTitle => "CHART EDITOR";
    protected override Color EdColor => Ed.Pink;

    private EdSong _song = new();
    private string _diff = "normal";
    private double _time;          // playhead ms
    private float _zoom = 0.6f;
    private int _snapIdx = 2;
    private static readonly int[] Snaps = { 4, 8, 16, 24, 32, 48, 64 };
    private EdNote _sel; private EdEvent _selEv;
    private EdNote _dragNote; private double _dragStart;
    private bool _playing, _metronome = true, _hitsounds = true, _voicesOn = true;
    private double _playStartWall, _playStartMs;
    private readonly Stack<string> _undo = new();
    private int _tab;
    private static readonly string[] Tabs = { "SONG", "NOTE", "EVENT", "FILE" };
    private string _status = "";

    // audio
    private string _instPath, _voxP, _voxO;
    private double _songLen = 60000;

    // sheets
    private SpriteSheet _notes;
    private readonly SpriteFrame[] _arrow = new SpriteFrame[4];
    private static readonly string[] Dirs = { "left", "down", "up", "right" };
    private static readonly Color[] LaneCol = { new(194, 75, 153), new(0, 255, 255), new(18, 250, 5), new(249, 57, 63) };

    // layout
    private const float CELL = 40f, GX = 120f, STRUM_Y = 190f;
    private float LaneX(int col) => GX + CELL + 10f + col * CELL + (col >= 4 ? 10f : 0f);   // col 0-3 opponent, 4-7 player
    private static int ColOf(int d) => d >= 4 ? d - 4 : 4 + d;
    private static int DOf(int col) => col < 4 ? col + 4 : col - 4;
    private float PxPerBeat => 4f * CELL * _zoom;

    // file list
    private List<(string label, string path)> _files = new();
    private int _fileSel = -1; private float _fileScroll;
    private string _newId = "", _newDiff = "";

    // event editing
    private static readonly string[] EvPresets = { "FocusCamera", "PlayAnimation", "ZoomCamera", "SetCameraBop", "Hey!", "Play Animation", "Add Camera Zoom", "Change Character", "Camera Follow Pos", "Change Scroll Speed", "Set Property" };
    private int _evPreset;
    private string _evV1 = "", _evV2 = "";

    // waveform
    private float[] _wave; private int _wavePerSec = 1000; private string _waveFor;

    public ChartEditor() { }
    public ChartEditor(EdSong s, string diff) { _song = s; _diff = diff; }

    public override void Load()
    {
        Audio.StopMusic();
        _notes = SpriteSheet.Load(Game, "game/skins/default/notes");
        for (int i = 0; i < 4; i++) _arrow[i] = _notes?.BuildAnimationFromPrefix(Dirs[i] + " note")?.FirstOrDefault();
        RefreshFiles();
        string devLoad = Environment.GetEnvironmentVariable("FNF_EDITOR_CHART");
        if (!string.IsNullOrEmpty(devLoad))
        {
            var f = _files.FirstOrDefault(x => x.label.EndsWith(devLoad, StringComparison.OrdinalIgnoreCase) || x.path.EndsWith(devLoad, StringComparison.OrdinalIgnoreCase));
            if (f.path != null) LoadFile(f.path);
        }
        else if (_song.Notes.Count == 0 && _files.Count > 0)
        {
            var bop = _files.FirstOrDefault(x => x.label == "[V] bopeebo");
            LoadFile(bop.path ?? _files[0].path);
        }
        else ResolveAudio();
    }

    public override void Unload() { Audio.StopMusic(); Audio.StopVoices(); Audio.StopOpponentVoices(); }
    protected override void OnExit() { Unload(); }

    // ================================================================== files
    private void RefreshFiles()
    {
        _files.Clear();
        try
        {
            string songs = Path.Combine(AssetManager.ContentRoot, "songs");
            if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(songs))
                foreach (var d in global::FNF_MonoGame.Engine.FsCI.GetDirectories(songs).OrderBy(x => x))
                {
                    string c = Path.Combine(d, "charts", "chart.json");
                    if (global::FNF_MonoGame.Engine.FsCI.FileExists(c)) _files.Add(("[V] " + Path.GetFileName(d), c));
                }
            foreach (var mod in EdProject.Mods())
            {
                string data = Path.Combine(EdProject.ModsBase, mod, "data");
                if (!global::FNF_MonoGame.Engine.FsCI.DirectoryExists(data)) continue;
                foreach (var sd in global::FNF_MonoGame.Engine.FsCI.GetDirectories(data))
                {
                    string id = Path.GetFileName(sd);
                    if (id.Equals("songs", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var vd in global::FNF_MonoGame.Engine.FsCI.GetDirectories(sd))
                        {
                            string vid = Path.GetFileName(vd), c = Path.Combine(vd, vid + "-chart.json");
                            if (global::FNF_MonoGame.Engine.FsCI.FileExists(c)) _files.Add(($"[V] {mod}/{vid}", c));
                        }
                        continue;
                    }
                    string main = Path.Combine(sd, id + ".json");
                    if (!global::FNF_MonoGame.Engine.FsCI.FileExists(main)) main = global::FNF_MonoGame.Engine.FsCI.GetFiles(sd, id + "*.json").FirstOrDefault();
                    if (main != null) _files.Add(($"[P] {mod}/{id}", main));
                }
            }
        }
        catch (Exception ex) { Console.WriteLine("[EDITOR] file scan: " + ex.Message); }
    }

    private void LoadFile(string path)
    {
        try
        {
            StopPlay();
            _song = EdSong.Load(path);
            _diff = _song.Difficulties.Contains("normal", StringComparer.OrdinalIgnoreCase) ? "normal" : _song.Difficulties.FirstOrDefault() ?? "normal";
            _time = 0; _sel = null; _selEv = null; _undo.Clear();
            ResolveAudio();
            Ed.Toast($"Loaded {_song.SourceFormat.ToUpperInvariant()} chart '{_song.Id}' ({_song.NotesFor(_diff).Count} notes, {_song.Difficulties.Count} diffs)", Ed.Green);
        }
        catch (Exception ex) { Ed.Toast("Load failed: " + ex.Message, Ed.Red); }
    }

    /// <summary>Inst/Voices: base songs (songs/&lt;id&gt;/Inst, Voices-&lt;char&gt;) or the mod's songs/&lt;id&gt;/ (Psych Inst.ogg, Voices.ogg / Voices-Player/Opponent).</summary>
    private void ResolveAudio()
    {
        _instPath = _voxP = _voxO = null;
        string src = _song.SourcePath ?? "";
        string modRoot = null;
        try
        {
            string mb = Path.GetFullPath(EdProject.ModsBase);
            if (!string.IsNullOrEmpty(src) && Path.GetFullPath(src).StartsWith(mb, StringComparison.OrdinalIgnoreCase))
            {
                string rel = Path.GetFullPath(src).Substring(mb.Length).TrimStart('\\', '/');
                modRoot = Path.Combine(mb, rel.Split('\\', '/')[0]);
            }
        }
        catch { }
        if (modRoot == null && EdProject.Root != null && global::FNF_MonoGame.Engine.FsCI.DirectoryExists(Path.Combine(EdProject.Root, "songs", _song.Id))) modRoot = EdProject.Root;
        if (modRoot != null)
        {
            string sd = Path.Combine(modRoot, "songs", _song.Id);
            string F(string n) => global::FNF_MonoGame.Engine.FsCI.FileExists(Path.Combine(sd, n + ".ogg")) ? Path.Combine(sd, n) : null;
            _instPath = F("Inst");
            _voxP = F("Voices-Player") ?? F("Voices-" + _song.Player) ?? F("Voices");
            _voxO = F("Voices-Opponent") ?? F("Voices-" + _song.Opponent);
        }
        else
        {
            string sd = Path.Combine(AssetManager.ContentRoot, "songs", _song.Id);
            bool E(string n) => global::FNF_MonoGame.Engine.FsCI.FileExists(Path.Combine(sd, n + ".ogg")) || global::FNF_MonoGame.Engine.FsCI.FileExists(Path.Combine(sd, "tracks", n + ".ogg"));
            if (E("Inst")) _instPath = $"songs/{_song.Id}/Inst";
            if (E("Voices-" + _song.Player)) _voxP = $"songs/{_song.Id}/Voices-{_song.Player}";
            else if (E("Voices")) _voxP = $"songs/{_song.Id}/Voices";
            if (E("Voices-" + _song.Opponent)) _voxO = $"songs/{_song.Id}/Voices-{_song.Opponent}";
        }
        _songLen = Math.Max(_song.LastTime() + 4000, 30000);
        StartWaveform();
    }

    private string AbsAudio(string p)
    {
        if (p == null) return null;
        if (Path.IsPathRooted(p)) return global::FNF_MonoGame.Engine.FsCI.FileExists(p + ".ogg") ? p + ".ogg" : p;
        string a = Path.Combine(AssetManager.ContentRoot, p + ".ogg");
        if (global::FNF_MonoGame.Engine.FsCI.FileExists(a)) return a;
        a = Path.Combine(AssetManager.ContentRoot, Path.GetDirectoryName(p) ?? "", "tracks", Path.GetFileName(p) + ".ogg");
        return global::FNF_MonoGame.Engine.FsCI.FileExists(a) ? a : null;
    }

    private void StartWaveform()
    {
        _wave = null;
        string abs = AbsAudio(_instPath);
        _waveFor = abs;
        if (abs == null) return;
#if !FNF_CROSSPLATFORM
        Task.Run(() =>
        {
            try
            {
                using var vr = new NVorbis.VorbisReader(abs);
                int ch = vr.Channels, rate = vr.SampleRate;
                int per = Math.Max(1, rate / _wavePerSec);
                long total = vr.TotalSamples;
                var peaks = new float[(int)(total / per) + 2];
                var buf = new float[per * ch * 64];
                int idx = 0, inBucket = 0; float mx = 0;
                int read;
                while ((read = vr.ReadSamples(buf, 0, buf.Length)) > 0)
                {
                    for (int i = 0; i < read; i += ch)
                    {
                        float v = Math.Abs(buf[i]); if (ch > 1) v = Math.Max(v, Math.Abs(buf[i + 1]));
                        if (v > mx) mx = v;
                        if (++inBucket >= per) { if (idx < peaks.Length) peaks[idx++] = mx; mx = 0; inBucket = 0; }
                    }
                }
                if (_waveFor == abs) { _wave = peaks; _songLen = Math.Max(_songLen, total * 1000.0 / rate); }
            }
            catch (Exception ex) { Console.WriteLine("[EDITOR] waveform: " + ex.Message); }
        });
#endif
    }

    // ================================================================== undo
    private void Snapshot()
    {
        _undo.Push(new JObject { ["n"] = JArray.FromObject(_song.NotesFor(_diff).Select(n => new[] { n.T, n.D, n.L }).ToList()), ["k"] = new JArray(_song.NotesFor(_diff).Select(n => n.K ?? "")), ["e"] = _song.ToVSliceChart()["events"] }.ToString());
        if (_undo.Count > 200) { var keep = _undo.Take(150).Reverse().ToList(); _undo.Clear(); foreach (var k in keep) _undo.Push(k); }
    }
    private void Undo()
    {
        if (_undo.Count == 0) { Ed.Toast("Nothing to undo"); return; }
        var o = JObject.Parse(_undo.Pop());
        var l = _song.NotesFor(_diff); l.Clear();
        var ks = (JArray)o["k"]; int i = 0;
        foreach (var a in (JArray)o["n"]) l.Add(new EdNote { T = a[0].Value<double>(), D = (int)a[1].Value<double>(), L = a[2].Value<double>(), K = ks[i++].ToString() });
        _song.Events.Clear();
        foreach (var e in (JArray)o["e"]) _song.Events.Add(new EdEvent { T = e.Value<double>("t"), E = e.Value<string>("e"), V = e["v"] });
        _sel = null; _selEv = null;
        Audio.PlaySound("sounds/chartingSounds/undo", 0.6f);
        Ed.Toast("Undo");
    }

    // ================================================================== time helpers
    private double SnapMs(double ms)
    {
        double beat = _song.MsToBeat(ms);
        double div = Snaps[_snapIdx] / 4.0;
        return _song.BeatToMs(Math.Round(beat * div) / div);
    }
    private double StepMs => 60000.0 / _song.BpmAt(_time) * 4.0 / Snaps[_snapIdx];
    private float YOf(double ms) => STRUM_Y + (float)((_song.MsToBeat(ms) - _song.MsToBeat(_time)) * PxPerBeat);
    private double MsOfY(float y) => _song.BeatToMs(_song.MsToBeat(_time) + (y - STRUM_Y) / PxPerBeat);

    // ================================================================== playback
    private void StartPlay()
    {
        if (_playing) return;
        _playing = true;
        _playStartMs = _time; _playStartWall = Ed.Time;
        if (_instPath != null)
        {
            Audio.PlayMusic(_instPath, false, (int)Math.Max(0, _time));
            if (_voicesOn && _voxP != null) Audio.PlayVoices(_voxP);
            if (_voicesOn && _voxO != null) Audio.PlayOpponentVoices(_voxO);
            Audio.ResyncVoices();
        }
        _lastBeat = (int)Math.Floor(_song.MsToBeat(_time));
        _lastHitT = _time;
    }
    private void StopPlay()
    {
        if (!_playing) return;
        _playing = false;
        Audio.StopMusic(); Audio.StopVoices(); Audio.StopOpponentVoices();
        _time = Math.Max(0, _time);
    }
    private int _lastBeat; private double _lastHitT;

    // ================================================================== update
    protected override void EdUpdate(float dt)
    {
        if (_playing)
        {
            double prev = _time;
            if (_instPath != null && Audio.MusicPlaying) _time = Audio.MusicPosition;
            else _time = _playStartMs + (Ed.Time - _playStartWall) * 1000.0;
            if (_time < prev - 50 && Audio.MusicPlaying) _time = prev;   // reader jitter
            int beat = (int)Math.Floor(_song.MsToBeat(_time));
            if (beat != _lastBeat && _metronome) Audio.PlaySound(beat % 4 == 0 ? "sounds/chartingSounds/metronome1" : "sounds/chartingSounds/metronome2", 0.6f);
            _lastBeat = beat;
            if (_hitsounds)
                foreach (var n in _song.NotesFor(_diff))
                {
                    if (n.T <= _lastHitT) continue;
                    if (n.T > _time) break;
                    Audio.PlaySound(n.D < 4 ? "sounds/chartingSounds/hitNotePlayer" : "sounds/chartingSounds/hitNoteOpponent", 0.7f);
                }
            _lastHitT = _time;
            if (_time > _songLen + 500 || (_instPath != null && !Audio.MusicPlaying && Ed.Time - _playStartWall > 1.0)) StopPlay();
        }

        if (Ed.Key(Keys.Space) || Ed.Pad(Buttons.X)) { if (_playing) StopPlay(); else StartPlay(); }
        if (Ed.Key(Keys.Enter) || Ed.Pad(Buttons.LeftStick)) { Playtest(); return; }
        if (Ed.Key(Keys.M)) { _metronome = !_metronome; Ed.Toast("Metronome " + (_metronome ? "ON" : "OFF")); }
        if (Ed.Key(Keys.H)) { _hitsounds = !_hitsounds; Ed.Toast("Hitsounds " + (_hitsounds ? "ON" : "OFF")); }
        if (Ed.Key(Keys.U) || (Ed.Ctrl && Ed.Key(Keys.Z))) Undo();
        if (Ed.Key(Keys.F1) || (Ed.Ctrl && Ed.Key(Keys.S))) SavePsych();
        if (Ed.Key(Keys.F2)) SaveVSlice();
        if (Ed.Key(Keys.OemPlus) || Ed.Key(Keys.Add)) _zoom = Math.Min(4f, _zoom * 1.25f);
        if (Ed.Key(Keys.OemMinus) || Ed.Key(Keys.Subtract)) _zoom = Math.Max(0.25f, _zoom / 1.25f);
        if (Ed.Key(Keys.OemCloseBrackets)) _snapIdx = Math.Min(Snaps.Length - 1, _snapIdx + 1);
        if (Ed.Key(Keys.OemOpenBrackets)) _snapIdx = Math.Max(0, _snapIdx - 1);
        if (Ed.Key(Keys.Home)) _time = 0;
        if (Ed.Key(Keys.End)) _time = _song.LastTime();

        if (!_playing)
        {
            double step = StepMs * (Ed.Shift ? 4 : 1);
            if (Ed.Key(Keys.W) || Ed.Key(Keys.Up) || Ed.Pad(Buttons.RightShoulder) && false) _time = SnapMs(_time - step);
            if (Ed.Key(Keys.S) && !Ed.Ctrl || Ed.Key(Keys.Down)) _time = SnapMs(_time + step);
            if (Ed.Key(Keys.A) || Ed.Key(Keys.Left) || Ed.Pad(Buttons.LeftShoulder)) _time = _song.BeatToMs(Math.Max(0, Math.Ceiling(_song.MsToBeat(_time) / 4.0 - 0.001) * 4 - 4));
            if (Ed.Key(Keys.D) || Ed.Key(Keys.Right) || Ed.Pad(Buttons.RightShoulder)) _time = _song.BeatToMs(Math.Floor(_song.MsToBeat(_time) / 4.0 + 0.001) * 4 + 4);
            float rs = Input.GamePadAxisValue("RightY");
            if (Math.Abs(rs) > 0.25f) _time -= rs * dt * 2000.0 / _zoom;
            if (Ed.Wheel != 0 && !Ed.OverUI)
            {
                if (Ed.Ctrl) _zoom = Math.Clamp(_zoom * (Ed.Wheel > 0 ? 1.2f : 1f / 1.2f), 0.25f, 4f);
                else _time = SnapMs(_time - Math.Sign(Ed.Wheel) * step);
            }
            _time = Math.Max(0, _time);
            if (Ed.PadWorld)
            {   // pad tools: D-pad up/down = playhead by snap, left/right = lane cursor, A = note on/off, triggers = sustain
                if (Ed.PadUp) _time = Math.Max(0, SnapMs(_time - StepMs));
                if (Ed.PadDown) _time = SnapMs(_time + StepMs);
                if (Ed.PadLeft) _padLane = (_padLane + 7) % 8;
                if (Ed.PadRight) _padLane = (_padLane + 1) % 8;
                if (Ed.Pad(Buttons.A)) ToggleNoteAt(SnapMs(_time), DOf(_padLane));
            }
            // 1-8: place at playhead (V-Slice keyboard placement) - 1-4 opponent, 5-8 player
            for (int k = 0; k < 8; k++)
                if (Ed.Key(Keys.D1 + k)) ToggleNoteAt(SnapMs(_time), DOf(k));
        }
        if (_sel != null)
        {
            if (Ed.Key(Keys.E) || Ed.PadWorld && Ed.RT) { Snapshot(); _sel.L += StepMs; }
            if (Ed.Key(Keys.Q) || Ed.PadWorld && Ed.LT) { Snapshot(); _sel.L = Math.Max(0, _sel.L - StepMs); }
            if (Ed.Key(Keys.Delete)) { Snapshot(); _song.NotesFor(_diff).Remove(_sel); _sel = null; Audio.PlaySound("sounds/chartingSounds/noteErase", 0.6f); }
        }
        else if (_selEv != null && Ed.Key(Keys.Delete)) { Snapshot(); _song.Events.Remove(_selEv); _selEv = null; }

        GridMouse();
    }

    private int _padLane = 4;
    protected override void PadSave() => SavePsych();
    protected override List<(Buttons, string)> PadWorldHints() => new()
    {
        (FNF_MonoGame.Engine.ButtonPrompts.DPadUpDown, "scroll"), (FNF_MonoGame.Engine.ButtonPrompts.DPadLeftRight, "lane"), (Buttons.A, "note"), (Buttons.LeftTrigger, "sustain-"), (Buttons.RightTrigger, "sustain+"),
        (Buttons.LeftShoulder, "section-"), (Buttons.RightShoulder, "section+"), (Buttons.X, "play"), (Buttons.LeftStick, "playtest"),
    };

    private void ToggleNoteAt(double t, int d)
    {
        var l = _song.NotesFor(_diff);
        var ex = l.FirstOrDefault(n => n.D == d && Math.Abs(n.T - t) < 2);
        Snapshot();
        if (ex != null) { l.Remove(ex); if (_sel == ex) _sel = null; Audio.PlaySound("sounds/chartingSounds/noteErase", 0.6f); }
        else { var n = new EdNote { T = t, D = d, K = _noteKind }; l.Add(n); _sel = n; l.Sort((a, b) => a.T.CompareTo(b.T)); Audio.PlaySound("sounds/chartingSounds/noteLay", 0.6f); }
    }
    private string _noteKind = "";

    private void GridMouse()
    {
        var m = Ed.M;
        if (_dragNote != null)
        {
            if (Ed.Down)
            {
                double end = SnapMs(MsOfY(m.Y));
                _dragNote.L = Math.Max(0, end - _dragNote.T);
            }
            else { if (_dragNote.L > 0) Audio.PlaySound("sounds/chartingSounds/stretchSNAP_UI", 0.5f); _dragNote = null; }
            return;
        }
        if (Ed.OverUI) return;
        float gx0 = GX, gx1 = LaneX(7) + CELL;
        if (m.X < gx0 || m.X >= gx1 || m.Y < 60 || m.Y > 690) return;
        double t = SnapMs(MsOfY(m.Y - CELL / 2f));
        if (t < 0) return;
        if (m.X < GX + CELL)
        {   // event lane
            var ev = _song.Events.FirstOrDefault(e => Math.Abs(YOf(e.T) - (m.Y - CELL / 2f)) < CELL / 2f);
            if (Ed.RClick && ev != null) { Snapshot(); _song.Events.Remove(ev); if (_selEv == ev) _selEv = null; return; }
            if (Ed.Click)
            {
                if (ev != null) { _selEv = ev; _sel = null; _tab = 2; LoadEvFields(ev); }
                else { Snapshot(); var ne = MakeEvent(t); _song.Events.Add(ne); _song.SortAll(); _selEv = ne; _sel = null; _tab = 2; Audio.PlaySound("sounds/chartingSounds/noteLay", 0.6f); }
            }
            return;
        }
        int col = -1;
        for (int c = 0; c < 8; c++) if (m.X >= LaneX(c) && m.X < LaneX(c) + CELL) col = c;
        if (col < 0) return;
        int d = DOf(col);
        var notes = _song.NotesFor(_diff);
        var hit = notes.FirstOrDefault(n => n.D == d && m.Y >= YOf(n.T) - 2 && m.Y <= YOf(n.T) + CELL + 2);
        if (Ed.RClick && hit != null) { Snapshot(); notes.Remove(hit); if (_sel == hit) _sel = null; Audio.PlaySound("sounds/chartingSounds/noteErase", 0.6f); return; }
        if (Ed.Click)
        {
            if (hit != null) { _sel = hit; _selEv = null; _tab = 1; }
            else
            {
                Snapshot();
                var n = new EdNote { T = t, D = d, K = _noteKind };
                notes.Add(n); notes.Sort((a, b) => a.T.CompareTo(b.T));
                _sel = n; _selEv = null; _dragNote = n; _dragStart = t;
                Audio.PlaySound("sounds/chartingSounds/noteLay", 0.6f);
            }
        }
    }

    private EdEvent MakeEvent(double t)
    {
        string name = EvPresets[_evPreset];
        JToken v;
        switch (name)
        {
            case "FocusCamera": v = new JObject { ["char"] = (int)Ed.ParseF(_evV1, 0) }; break;
            case "PlayAnimation": v = new JObject { ["target"] = string.IsNullOrEmpty(_evV2) ? "bf" : _evV2, ["anim"] = string.IsNullOrEmpty(_evV1) ? "hey" : _evV1, ["force"] = true }; break;
            case "ZoomCamera": v = new JObject { ["zoom"] = Ed.ParseF(_evV1, 1.1f), ["duration"] = Ed.ParseF(_evV2, 4f), ["ease"] = "expoOut" }; break;
            case "SetCameraBop": v = new JObject { ["rate"] = (int)Ed.ParseF(_evV1, 4), ["intensity"] = Ed.ParseF(_evV2, 1f) }; break;
            default: v = new JObject { ["v1"] = _evV1 ?? "", ["v2"] = _evV2 ?? "" }; break;
        }
        return new EdEvent { T = t, E = name, V = v };
    }
    private void LoadEvFields(EdEvent e)
    {
        int i = System.Array.IndexOf(EvPresets, e.E); if (i >= 0) _evPreset = i;
        if (e.V is JObject o)
        {
            if (o.ContainsKey("v1")) { _evV1 = o.Value<string>("v1"); _evV2 = o.Value<string>("v2"); }
            else if (e.E == "FocusCamera") { _evV1 = Ed.F(EdSong.FocusValue(o)); _evV2 = ""; }
            else if (e.E == "PlayAnimation") { _evV1 = o.Value<string>("anim"); _evV2 = o.Value<string>("target"); }
            else if (e.E == "ZoomCamera") { _evV1 = Ed.F(o.Value<double?>("zoom") ?? 1); _evV2 = Ed.F(o.Value<double?>("duration") ?? 4); }
            else if (e.E == "SetCameraBop") { _evV1 = Ed.F(o.Value<double?>("rate") ?? 4); _evV2 = Ed.F(o.Value<double?>("intensity") ?? 1); }
            else { _evV1 = o.ToString(Newtonsoft.Json.Formatting.None); _evV2 = ""; }
        }
        else { _evV1 = e.V?.ToString() ?? ""; _evV2 = ""; }
    }

    // ================================================================== save / playtest
    private void SavePsych()
    {
        try { string r = NeedRoot(); var p = _song.SavePsych(r); Ed.Toast($"Saved Psych chart x{p.Count}: {EdProject.Rel(p[0])}", Ed.Green); CopyAudioToMod(r); RefreshFiles(); }
        catch (Exception ex) { Ed.Toast("Save failed: " + ex.Message, Ed.Red); }
    }
    private void SaveVSlice()
    {
        try { string r = NeedRoot(); var p = _song.SaveVSlice(r); Ed.Toast($"Saved V-Slice chart+metadata: {EdProject.Rel(p[0])}", Ed.Green); CopyAudioToMod(r); RefreshFiles(); }
        catch (Exception ex) { Ed.Toast("Save failed: " + ex.Message, Ed.Red); }
    }
    /// <summary>A chart saved into a mod needs its audio there too (Psych songs/&lt;id&gt;/Inst.ogg + Voices.ogg).</summary>
    private void CopyAudioToMod(string root)
    {
        try
        {
            string sd = Path.Combine(root, "songs", _song.Id);
            Directory.CreateDirectory(sd);
            void Cp(string src, string name) { var a = AbsAudio(src); string dst = Path.Combine(sd, name + ".ogg"); if (a != null && !global::FNF_MonoGame.Engine.FsCI.FileExists(dst) && !string.Equals(Path.GetFullPath(a), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase)) File.Copy(a, dst); }
            Cp(_instPath, "Inst");
            // Psych 1.0 names (PlayState.generateSong): split tracks = Voices-Player + Voices-Opponent, one track = Voices
            if (_voxO != null) { Cp(_voxP, "Voices-Player"); Cp(_voxO, "Voices-Opponent"); }
            else Cp(_voxP, "Voices");
        }
        catch (Exception ex) { Console.WriteLine("[EDITOR] audio copy: " + ex.Message); }
    }

    private string _audioFrom = "";
    private void ImportAudio()
    {
        try
        {
            string src = Path.Combine(AssetManager.ContentRoot, "songs", (_audioFrom ?? "").Trim().ToLowerInvariant());
            if (!global::FNF_MonoGame.Engine.FsCI.DirectoryExists(src)) { Ed.Toast("No base song folder " + EdProject.Rel(src), Ed.Red); return; }
            string F(string n) { foreach (var d in new[] { src, Path.Combine(src, "tracks") }) { var p = Path.Combine(d, n + ".ogg"); if (global::FNF_MonoGame.Engine.FsCI.FileExists(p)) return p; } return null; }
            var meta = Path.Combine(src, "charts", "meta.json");
            string pl = "bf", op = "dad";
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(meta)) { var m = JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(meta)); pl = (string)m["playData"]?["characters"]?["player"] ?? pl; op = (string)m["playData"]?["characters"]?["opponent"] ?? op; }
            string dst = Path.Combine(NeedRoot(), "songs", _song.Id);
            Directory.CreateDirectory(dst);
            int n = 0;
            void Cp(string a, string name) { if (a == null) return; string d = Path.Combine(dst, name + ".ogg"); if (!global::FNF_MonoGame.Engine.FsCI.FileExists(d) || new FileInfo(d).Length != new FileInfo(a).Length) File.Copy(a, d, true); n++; }
            Cp(F("Inst"), "Inst");
            string vp = F("Voices-" + pl), vo = F("Voices-" + op), v = F("Voices");
            // Psych 1.0 split vocals: player -> Voices-Player.ogg, opponent -> Voices-Opponent.ogg (one combined track -> Voices.ogg)
            if (vp != null && vo != null) { Cp(vp, "Voices-Player"); Cp(vo, "Voices-Opponent"); }
            else Cp(vp ?? v, "Voices");
            ResolveAudio();
            Ed.Toast($"Imported {n} audio files into {EdProject.Rel(dst)}", Ed.Green);
        }
        catch (Exception ex) { Ed.Toast("Import failed: " + ex.Message, Ed.Red); }
    }

    private void Playtest()
    {
        StopPlay();
        SavePsych();
        string root = EdProject.Root;
        if (root == null) return;
        Game.Scenes.ChangeScene(new PlayScene(new PlayStateParams { SongName = _song.Id, TargetDifficulty = _diff, ModPath = root }));
    }

    // ================================================================== draw
    protected override void DrawWorld(SpriteBatch sb) { }

    protected override void DrawUI()
    {
        if (Ed.PadWorld && !_playing)
        {   // pad lane cursor at the playhead
            float cx = LaneX(_padLane), cy = YOf(SnapMs(_time));
            float b = 2f + MathF.Sin(Ed.Time * 9f) * 1.5f;
            FunkUI.Fill(cx - b, cy - CELL / 2f - b, CELL + 2 * b, 4f, Ed.Pink); FunkUI.Fill(cx - b, cy + CELL / 2f + b - 4f, CELL + 2 * b, 4f, Ed.Pink);
            FunkUI.Fill(cx - b, cy - CELL / 2f - b, 4f, CELL + 2 * b, Ed.Pink); FunkUI.Fill(cx + CELL + b - 4f, cy - CELL / 2f - b, 4f, CELL + 2 * b, Ed.Pink);
        }
        // ---------- grid
        float top = 56f, bot = 690f;
        float gx1 = LaneX(7) + CELL;
        FunkUI.Fill(GX - 4f, top, gx1 - GX + 8f, bot - top, Ed.Ink);
        // checker rows per snap
        double b0 = _song.MsToBeat(MsOfY(top)), b1 = _song.MsToBeat(MsOfY(bot));
        double rowBeats = 4.0 / Snaps[_snapIdx];
        if (PxPerBeat * rowBeats < 6) rowBeats = 0.25;
        long r0 = (long)Math.Floor(Math.Max(0, b0) / rowBeats), r1 = (long)Math.Ceiling(b1 / rowBeats);
        for (long r = r0; r <= r1; r++)
        {
            double bb = r * rowBeats;
            float y = STRUM_Y + (float)((bb - _song.MsToBeat(_time)) * PxPerBeat) + CELL / 2f;
            float h = (float)(rowBeats * PxPerBeat);
            for (int c = -1; c < 8; c++)
            {
                float x = c < 0 ? GX : LaneX(c);
                bool alt = ((r + c + 8) & 1) == 0;
                var col = c < 0 ? (alt ? new Color(90, 70, 120) : new Color(76, 58, 104)) : alt ? new Color(232, 225, 210) : new Color(208, 200, 186);
                float yy = Math.Max(top, y), yh = Math.Min(bot, y + h) - yy;
                if (yh > 0) FunkUI.Fill(x, yy, CELL, yh, col);
            }
        }
        // waveform (inst) behind the player + opponent lanes
        if (_wave != null)
        {
            float cx = (LaneX(3) + CELL + LaneX(4)) / 2f;
            for (float y = top; y < bot; y += 2f)
            {
                double ms = MsOfY(y - CELL / 2f);
                int i0 = (int)(ms * _wavePerSec / 1000.0);
                int i1 = (int)(MsOfY(y + 2f - CELL / 2f) * _wavePerSec / 1000.0);
                if (i0 < 0 || i0 >= _wave.Length) continue;
                float mx = 0; for (int i = i0; i <= Math.Min(i1, _wave.Length - 1); i++) mx = Math.Max(mx, _wave[i]);
                float w = mx * (CELL * 4f);
                FunkUI.Fill(cx - w, y, w * 2f, 2f, new Color(Ed.Purple, 0.35f));
            }
        }
        FunkUI.HealthIcon(_song.Opponent, LaneX(0) + CELL * 2f, 112f, 56f, 0, 0f, 0.5f);
        FunkUI.HealthIcon(_song.Player, LaneX(4) + CELL * 2f, 112f, 56f, 0, 0f, 0.5f, true);
        // beat / measure lines + section labels
        for (long bt = (long)Math.Floor(Math.Max(0, b0)); bt <= (long)Math.Ceiling(b1); bt++)
        {
            float y = STRUM_Y + (float)((bt - _song.MsToBeat(_time)) * PxPerBeat) + CELL / 2f;
            if (y < top || y > bot) continue;
            bool measure = bt % 4 == 0;
            FunkUI.Fill(GX, y - (measure ? 2f : 1f), gx1 - GX, measure ? 4f : 2f, measure ? Ed.Ink : new Color(Ed.Ink, 0.45f));
            if (measure)
            {
                FunkUI.Text((bt / 4).ToString(), GX - 10f, y - 8f, 15f, Color.White, 1f, 2f);
                int f = _song.FocusAt(_song.BeatToMs(bt));
                FunkUI.Sticker("icon:" + (f == 0 ? _song.Player : f == 2 ? _song.Gf : _song.Opponent), GX - 52f, y + 22f, 34f);
            }
        }
        FunkUI.Fill(LaneX(3) + CELL, top, 10f, bot - top, Ed.Ink);
        FunkUI.Fill(GX + CELL, top, 10f, bot - top, Ed.Ink);
        // notes
        foreach (var n in _song.NotesFor(_diff))
        {
            float y = YOf(n.T);
            float ye = n.L > 0 ? YOf(n.T + n.L) : y;
            if (ye + CELL < top || y > bot) continue;
            float x = LaneX(ColOf(n.D));
            if (n.L > 0) { FunkUI.Fill(x + CELL / 2f - 6f, y + CELL / 2f, 12f, ye - y, Color.Black); FunkUI.Fill(x + CELL / 2f - 4f, y + CELL / 2f, 8f, ye - y, LaneCol[n.D % 4]); }
            DrawArrow(n.D % 4, x, y, n == _sel ? 1f : 0.95f);
            if (!string.IsNullOrEmpty(n.K)) FunkUI.Text(n.K.Substring(0, 1).ToUpperInvariant(), x + CELL - 10f, y + 2f, 13f, Color.White, 0f, 2f);
            if (n == _sel) FunkUI.MarkerRect(x - 2f, y - 2f, CELL + 4f, CELL + 4f, 3f, Ed.Pink, 3);
        }
        // events
        foreach (var e in _song.Events)
        {
            float y = YOf(e.T);
            if (y + CELL < top || y > bot) continue;
            FunkUI.Block(GX + 4f, y + 4f, CELL - 8f, CELL - 8f, e.E == "FocusCamera" ? Ed.Cyan : Ed.Yellow, 3f);
            FunkUI.Text(e.E.Substring(0, Math.Min(2, e.E.Length)), GX + CELL / 2f, y + 12f, 13f, Ed.Ink, 0.5f);
            if (e == _selEv) FunkUI.MarkerRect(GX, y, CELL, CELL, 3f, Ed.Pink, 5);
            if (Ed.Hover(GX, y, CELL, CELL)) { Ed.TipText = e.E + " " + (e.V?.ToString(Newtonsoft.Json.Formatting.None) ?? ""); }
        }
        // hover ghost
        if (!Ed.OverUI && _dragNote == null)
        {
            var m = Ed.M;
            for (int c = 0; c < 8; c++)
                if (m.X >= LaneX(c) && m.X < LaneX(c) + CELL && m.Y > top && m.Y < bot)
                {
                    double t = SnapMs(MsOfY(m.Y - CELL / 2f));
                    DrawArrow(DOf(c) % 4, LaneX(c), YOf(t), 0.4f);
                }
        }
        // strumline / playhead
        FunkUI.Fill(GX - 30f, STRUM_Y + CELL / 2f - 2f, gx1 - GX + 60f, 4f, Ed.Red);
        FunkUI.Sticker("@arrow", GX - 40f, STRUM_Y + CELL / 2f, 26f, 0f, 1f, 1f, 1f, Ed.Red);
        for (int c = 0; c < 8; c++) DrawArrow(DOf(c) % 4, LaneX(c), 62f, 0.25f);
        // lane headers
        Ed.AddRect(GX - 4f, top, gx1 - GX + 8f, 0f);

        DrawPlaybar();
        DrawPanel();
        Ed.HelpTape("SPACE play  W/S wheel scroll  A/D section  1-8 place  Q/E sustain  DEL  U undo  +/- zoom  [ ] snap  M metro  H hits  F1 Psych F2 V-Slice  ENTER test");
    }

    private void DrawArrow(int dir, float x, float y, float a)
    {
        var f = _arrow[dir];
        var tex = f?.Texture ?? _notes?.Texture;
        if (f == null || tex == null) { FunkUI.Block(x + 4f, y + 4f, CELL - 8f, CELL - 8f, new Color(LaneCol[dir], a), 3f); return; }
        var r = f.SourceRect;
        float sw = f.Rotated ? r.Height : r.Width, sh = f.Rotated ? r.Width : r.Height;
        float s = (CELL - 2f) / Math.Max(sw, sh);
        FunkUI.Quad(tex, x + CELL / 2f, y + CELL / 2f, r.Width * s, r.Height * s, f.Rotated ? -MathF.PI / 2f : 0f, new Color(Color.White, a), r);
    }

    private void DrawPlaybar()
    {
        float x = 120f, y = 650f;
        // (drawn above the help tape)
        double beat = _song.MsToBeat(_time);
        string t = $"{TimeSpan.FromMilliseconds(Math.Max(0, _time)):m\\:ss\\.ff} / {TimeSpan.FromMilliseconds(_songLen):m\\:ss}   beat {beat:0.00}  step {(int)Math.Floor(beat * 4)}  sec {(int)Math.Floor(beat / 4)}   BPM {Ed.F(_song.BpmAt(_time))}";
        Ed.Panel(x + 360f + 30f, 600f, 470f, 70f, Ed.Paper, null, null, 9);
        FunkUI.Text(t, x + 400f, 608f, 14f, Ed.Ink);
        if (Ed.Button(_playing ? "PAUSE" : "PLAY", x + 400f, 630f, 80f, 28f, Ed.Green)) { if (_playing) StopPlay(); else StartPlay(); }
        if (Ed.Button("|<", x + 488f, 630f, 40f, 28f, Ed.Yellow)) { StopPlay(); _time = 0; }
        Ed.Toggle("metro", ref _metronome, x + 538f, 632f, 86f);
        Ed.Toggle("hits", ref _hitsounds, x + 628f, 632f, 76f);
        if (Ed.Toggle("vox", ref _voicesOn, x + 710f, 632f, 70f) && _playing) { StopPlay(); StartPlay(); }
        // scrub bar
        float sx = x + 400f, sw = 450f, sy = 664f;
        FunkUI.Fill(sx, sy, sw, 4f, Ed.Ink);
        float px = sx + (float)(Math.Clamp(_time / Math.Max(1, _songLen), 0, 1) * sw);
        FunkUI.Disc(px, sy + 2f, 6f, Ed.Pink);
        if (Ed.Hover(sx, sy - 6f, sw, 16f) && Ed.Down) { bool was = _playing; StopPlay(); _time = SnapMs((Ed.M.X - sx) / sw * _songLen); if (was) StartPlay(); }
    }

    private void DrawPanel()
    {
        float x = 850f, y = 70f, w = 410f, h = 518f;
        Ed.Panel(x, y, w, h, Ed.Paper, _song.SourceFormat == "psych" ? "PSYCH CHART" : "V-SLICE CHART", Ed.Pink, 1);
        for (int i = 0; i < Tabs.Length; i++)
            if (Ed.Button(Tabs[i], x + 12f + i * 98f, y + 14f, 92f, 26f, Ed.Yellow, _tab == i)) _tab = i;
        float cy = y + 54f;
        switch (_tab)
        {
            case 0: SongTab(x + 14f, cy, w - 28f); break;
            case 1: NoteTab(x + 14f, cy, w - 28f); break;
            case 2: EventTab(x + 14f, cy, w - 28f); break;
            case 3: FileTab(x + 14f, cy, w - 28f); break;
        }
        // difficulty strip (always visible)
        var diffs = _song.Difficulties;
        int di = Math.Max(0, diffs.FindIndex(d => d.Equals(_diff, StringComparison.OrdinalIgnoreCase)));
        int nd = Ed.Choice("Difficulty", diffs, di, x + 14f, y + h - 40f, w - 28f, 100f);
        if (nd != di) { _diff = diffs[nd]; _sel = null; Ed.Toast($"Difficulty: {_diff} ({_song.NotesFor(_diff).Count} notes)"); }
    }

    private void SongTab(float x, float y, float w)
    {
        string s = _song.Name; if (Ed.Field("sname", "Name", ref s, x, y, w)) _song.Name = s; y += 32;
        s = _song.Id; if (Ed.Field("sid", "Song id", ref s, x, y, w)) { _song.Id = s.Trim().ToLowerInvariant().Replace(' ', '-'); ResolveAudio(); } y += 32;
        s = _song.Artist; if (Ed.Field("sart", "Artist", ref s, x, y, w)) _song.Artist = s; y += 32;
        float bpm = (float)_song.Bpms[0].Bpm;
        if (Ed.Num("sbpm", "Start BPM", ref bpm, 1f, x, y, w)) { if (bpm > 0) _song.Bpms[0].Bpm = bpm; } y += 32;
        float spd = (float)_song.SpeedFor(_diff);
        if (Ed.Num("sspd", "Speed", ref spd, 0.1f, x, y, w)) _song.Speed[_diff] = Math.Max(0.1, spd); y += 32;
        s = _song.Player; if (Ed.Field("sp1", "Player", ref s, x, y, w)) { _song.Player = s; ResolveAudio(); } y += 32;
        s = _song.Opponent; if (Ed.Field("sp2", "Opponent", ref s, x, y, w)) { _song.Opponent = s; ResolveAudio(); } y += 32;
        s = _song.Gf; if (Ed.Field("sgf", "Girlfriend", ref s, x, y, w)) _song.Gf = s; y += 32;
        s = _song.Stage; if (Ed.Field("sstg", "Stage", ref s, x, y, w)) _song.Stage = s; y += 32;
        s = _song.NoteStyle; if (Ed.Field("snst", "Note style", ref s, x, y, w)) _song.NoteStyle = s; y += 32;
        Ed.Toggle("Needs voices", ref _song.NeedsVoices, x, y, 170f);
        if (Ed.Button("BPM CHANGE HERE", x + 180f, y - 2f, w - 180f, 26f, Ed.Orange, false, "Adds a time change at the playhead with the Start BPM field value"))
        {
            double t = SnapMs(_time);
            _song.Bpms.RemoveAll(b => Math.Abs(b.T - t) < 1 && b.T > 0);
            if (t > 0) { _song.Bpms.Add(new EdTimeChange { T = t, Bpm = _bpmNew }); _song.SortAll(); Ed.Toast($"BPM {Ed.F(_bpmNew)} at {t:0} ms"); }
        }
        y += 32;
        Ed.Num("bpmnew", "New BPM", ref _bpmNew, 1f, x, y, w);
    }
    private float _bpmNew = 150f;

    private void NoteTab(float x, float y, float w)
    {
        Ed.Label($"Notes in '{_diff}': {_song.NotesFor(_diff).Count}   snap 1/{Snaps[_snapIdx]}", x, y); y += 24;
        int si = Ed.Choice("Snap", Snaps.Select(v => "1/" + v).ToList(), _snapIdx, x, y, w); _snapIdx = si; y += 32;
        Ed.Field("nkind", "New kind", ref _noteKind, x, y, w, 100f); y += 30;
        Ed.Label("kinds: '' normal, Alt Animation, Hurt Note, GF Sing, No Animation", x, y, 12f); y += 24;
        if (_sel == null) { Ed.Label("Click a note in the grid to edit it.", x, y + 10f); return; }
        float t = (float)_sel.T; if (Ed.Num("nt", "Time ms", ref t, (float)StepMs, x, y, w, 100f)) { Snapshot(); _sel.T = Math.Max(0, t); } y += 32;
        float d = _sel.D; if (Ed.Num("nd", "Lane 0-7", ref d, 1f, x, y, w, 100f)) { Snapshot(); _sel.D = Math.Clamp((int)d, 0, 7); } y += 32;
        float l = (float)_sel.L; if (Ed.Num("nl", "Length ms", ref l, (float)StepMs, x, y, w, 100f)) { Snapshot(); _sel.L = Math.Max(0, l); } y += 32;
        string k = _sel.K; if (Ed.Field("nk", "Kind", ref k, x, y, w, 100f)) { Snapshot(); _sel.K = k; } y += 34;
        Ed.Label(_sel.D < 4 ? "Player note (V-Slice d 0-3 / Psych lane 0-3)" : "Opponent note (V-Slice d 4-7 / Psych lane 4-7)", x, y, 13f); y += 26;
        if (Ed.Button("DELETE NOTE", x, y, 150f, 28f, Ed.Red)) { Snapshot(); _song.NotesFor(_diff).Remove(_sel); _sel = null; }
    }

    private void EventTab(float x, float y, float w)
    {
        _evPreset = Ed.Choice("Event", EvPresets, _evPreset, x, y, w, 70f); y += 32;
        string hint = EvPresets[_evPreset] switch
        {
            "FocusCamera" => "V1: 0 player, 1 opponent, 2 gf (Psych: mustHitSection)",
            "PlayAnimation" => "V1 anim, V2 target bf/dad/gf",
            "ZoomCamera" => "V1 zoom, V2 duration (steps)",
            "SetCameraBop" => "V1 rate (beats), V2 intensity",
            _ => "Psych event: V1 / V2 strings",
        };
        Ed.Label(hint, x, y, 13f); y += 22;
        Ed.Field("ev1", "Value 1", ref _evV1, x, y, w, 70f); y += 32;
        Ed.Field("ev2", "Value 2", ref _evV2, x, y, w, 70f); y += 34;
        if (Ed.Button("ADD AT PLAYHEAD", x, y, 190f, 28f, Ed.Green)) { Snapshot(); var e = MakeEvent(SnapMs(_time)); _song.Events.Add(e); _song.SortAll(); _selEv = e; }
        if (_selEv != null && Ed.Button("UPDATE SELECTED", x + 196f, y, w - 196f, 28f, Ed.Yellow)) { Snapshot(); var e = MakeEvent(_selEv.T); _selEv.E = e.E; _selEv.V = e.V; }
        y += 36;
        var near = _song.Events.Where(e => Math.Abs(e.T - _time) < 8000).Take(9).ToList();
        Ed.Label("Events near playhead:", x, y); y += 20;
        foreach (var e in near)
        {
            bool on = e == _selEv;
            if (Ed.Button($"{e.T:0}ms {e.E} {e.V?.ToString(Newtonsoft.Json.Formatting.None)}", x, y, w, 22f, on ? Ed.Pink : Color.White)) { _selEv = e; LoadEvFields(e); _time = e.T; }
            y += 26;
        }
    }

    private void FileTab(float x, float y, float w)
    {
        int s = Ed.List("files", _files.Select(f => f.label).ToList(), _fileSel, x, y, w, 196f, ref _fileScroll, Ed.Pink, 22f);
        if (s != _fileSel) _fileSel = s;
        y += 204;
        if (Ed.Button("LOAD", x, y, 90f, 28f, Ed.Cyan) && _fileSel >= 0) LoadFile(_files[_fileSel].path);
        if (Ed.Button("RESCAN", x + 96f, y, 90f, 28f, Color.White)) RefreshFiles();
        if (Ed.Button("PLAYTEST", x + 192f, y, w - 192f, 28f, Ed.Green, false, "Saves the Psych chart into the mod and plays it")) Playtest();
        y += 36;
        if (Ed.Button("SAVE PSYCH", x, y, (w - 6f) / 2f, 30f, Ed.Purple)) SavePsych();
        if (Ed.Button("SAVE V-SLICE", x + (w + 6f) / 2f, y, (w - 6f) / 2f, 30f, Ed.Orange)) SaveVSlice();
        y += 40;
        Ed.Field("newid", "New song", ref _newId, x, y, w - 90f, 90f);
        if (Ed.Button("NEW", x + w - 84f, y - 1f, 84f, 28f, Ed.Yellow))
        {
            StopPlay();
            string id = (_newId ?? "").Trim().ToLowerInvariant().Replace(' ', '-');
            if (id.Length == 0) Ed.Toast("Type a song id first", Ed.Red);
            else
            {
                _song = new EdSong { Id = id, Name = _newId.Trim(), Stage = "stage", SourceFormat = "new" };
                foreach (var d in _song.Difficulties) _song.Speed[d] = 1.5;
                _song.Events.Add(new EdEvent { T = 0, E = "FocusCamera", V = new JObject { ["char"] = 1 } });
                _diff = "normal"; _time = 0; _sel = null; ResolveAudio();
                Ed.Toast($"New song '{id}' - put Inst.ogg/Voices.ogg in mods/{EdProject.ModName}/songs/{id}/");
            }
        }
        y += 34;
        Ed.Field("newdiff", "Add diff", ref _newDiff, x, y, w - 90f, 90f);
        if (Ed.Button("ADD", x + w - 84f, y - 1f, 84f, 28f, Ed.Yellow) && !string.IsNullOrWhiteSpace(_newDiff))
        {
            string d = _newDiff.Trim().ToLowerInvariant();
            if (!_song.Difficulties.Contains(d)) { _song.Difficulties.Add(d); _song.Notes[d] = _song.NotesFor(_diff).Select(n => n.Clone()).ToList(); _song.Speed[d] = _song.SpeedFor(_diff); }
            _diff = d; Ed.Toast($"Difficulty '{d}' (copied from current)");
        }
        y += 34;
        Ed.Field("audfrom", "Audio from", ref _audioFrom, x, y, w - 90f, 90f);
        if (Ed.Button("IMPORT", x + w - 84f, y - 1f, 84f, 28f, Ed.Cyan, false, "Copies Inst/Voices of a base song (Content/songs/<id>) into mods/<mod>/songs/<song id>/")) ImportAudio();
        y += 34;
        Ed.Label("src: " + EdProject.Rel(_song.SourcePath), x, y, 12f); y += 16;
        Ed.Label("inst: " + (_instPath == null ? "none" : EdProject.Rel(AbsAudio(_instPath))), x, y, 12f);
    }
}
