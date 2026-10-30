using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FNF_MonoGame.Scenes.Editors;

/// <summary>Chart note in the editor: V-Slice SongNoteData (t ms, d 0-3 player / 4-7 opponent, l ms, k kind).
/// Psych 1.0 "psych_v1" uses the same lane numbering (Song.convert), so lanes map 1:1.</summary>
public class EdNote { public double T; public int D; public double L; public string K = ""; public EdNote Clone() => (EdNote)MemberwiseClone(); }
/// <summary>Chart event: V-Slice SongEventData (t, e, v). Psych events are kept as e = Psych name, v = { v1, v2 }.</summary>
public class EdEvent { public double T; public string E = ""; public JToken V; public EdEvent Clone() => new EdEvent { T = T, E = E, V = V?.DeepClone() }; }
public class EdTimeChange { public double T; public double Bpm; public int N = 4, D = 4; }

/// <summary>
/// One song in the merged chart editor: every difficulty's notes + scroll speed, events, BPM map, metadata.
/// Reads/writes Psych Engine charts (data/&lt;song&gt;/&lt;song&gt;[-diff].json, legacy + psych_v1) and V-Slice charts
/// (&lt;song&gt;-chart.json + &lt;song&gt;-metadata.json, version 2.0.0 / 2.2.4; this engine's charts/chart.json + meta.json too).
/// </summary>
public class EdSong
{
    public string Id = "test", Name = "Test", Artist = "Unknown", Charter = "";
    public string Player = "bf", Opponent = "dad", Gf = "gf", Stage = "mainStage", NoteStyle = "funkin", Album = "volume1";
    public List<EdTimeChange> Bpms = new() { new EdTimeChange { T = 0, Bpm = 100 } };
    public Dictionary<string, List<EdNote>> Notes = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> Speed = new(StringComparer.OrdinalIgnoreCase);
    public List<EdEvent> Events = new();
    public List<string> Difficulties = new() { "easy", "normal", "hard" };
    public bool NeedsVoices = true;
    public string SourceFormat = "new", SourcePath = "";
    // Psych extras kept on round-trip
    public string ArrowSkin = "", SplashSkin = "";
    public double PsychOffset = 0;

    public List<EdNote> NotesFor(string diff)
    {
        if (!Notes.TryGetValue(diff, out var l)) { l = new List<EdNote>(); Notes[diff] = l; }
        return l;
    }
    public double SpeedFor(string diff) => Speed.TryGetValue(diff, out var s) ? s : 1.0;

    // ------------------------------------------------------------------ time maths (V-Slice Conductor / Psych getBPMFromSeconds)
    public double BpmAt(double ms) { double b = Bpms[0].Bpm; foreach (var c in Bpms) if (c.T <= ms + 0.001) b = c.Bpm; return b; }
    public double MsToBeat(double ms)
    {
        double beat = 0; var bp = Bpms;
        for (int i = 0; i < bp.Count; i++)
        {
            double end = i + 1 < bp.Count ? bp[i + 1].T : double.MaxValue;
            double spb = 60000.0 / bp[i].Bpm;
            if (ms < end || i == bp.Count - 1) return beat + (ms - bp[i].T) / spb;
            beat += (end - bp[i].T) / spb;
        }
        return beat;
    }
    public double BeatToMs(double beatTarget)
    {
        double beat = 0; var bp = Bpms;
        for (int i = 0; i < bp.Count; i++)
        {
            double spb = 60000.0 / bp[i].Bpm;
            double len = i + 1 < bp.Count ? (bp[i + 1].T - bp[i].T) / spb : double.MaxValue;
            if (beatTarget < beat + len || i == bp.Count - 1) return bp[i].T + (beatTarget - beat) * spb;
            beat += len;
        }
        return 0;
    }
    public void SortAll()
    {
        Bpms.Sort((a, b) => a.T.CompareTo(b.T));
        if (Bpms.Count == 0 || Bpms[0].T > 0) Bpms.Insert(0, new EdTimeChange { T = 0, Bpm = Bpms.Count > 0 ? Bpms[0].Bpm : 100 });
        foreach (var l in Notes.Values) l.Sort((a, b) => a.T != b.T ? a.T.CompareTo(b.T) : a.D.CompareTo(b.D));
        Events.Sort((a, b) => a.T.CompareTo(b.T));
    }
    public double LastTime()
    {
        double t = 0;
        foreach (var l in Notes.Values) foreach (var n in l) t = Math.Max(t, n.T + n.L);
        foreach (var e in Events) t = Math.Max(t, e.T);
        return t;
    }

    // ================================================================== LOAD
    public static EdSong Load(string path)
    {
        var txt = global::FNF_MonoGame.Engine.FsCI.ReadAllText(path);
        var root = JObject.Parse(SanitizeJson(txt));
        string fn = Path.GetFileName(path).ToLowerInvariant();
        // V-Slice chart (has "notes" object keyed by difficulty) or metadata
        if (root["notes"] is JObject || fn.EndsWith("-metadata.json") || fn == "meta.json" || root["playData"] != null)
            return LoadVSlice(path);
        return LoadPsych(path, root);
    }

    public static string SanitizeJson(string s)
    {
        s = s.Trim('\uFEFF', ' ', '\r', '\n', '\t', '\0');
        int i = s.LastIndexOf('}');
        return i >= 0 ? s.Substring(0, i + 1) : s;
    }

    /// <summary>Psych Engine: Song.parseJSON + Song.convert (legacy mustHitSection lanes, events stored as notes with d &lt; 0).</summary>
    public static EdSong LoadPsych(string path, JObject root = null, string diffOverride = null)
    {
        root ??= JObject.Parse(SanitizeJson(global::FNF_MonoGame.Engine.FsCI.ReadAllText(path)));
        var song = root["song"] is JObject so ? so : root;   // 1.0 psych_v1 charts are unwrapped
        var s = new EdSong { SourceFormat = "psych", SourcePath = path };
        string fileNoExt = Path.GetFileNameWithoutExtension(path);
        string folder = Path.GetFileName(Path.GetDirectoryName(path)) ?? fileNoExt;
        s.Id = folder;
        string diff = diffOverride ?? "normal";
        if (diffOverride == null && fileNoExt.StartsWith(folder + "-", StringComparison.OrdinalIgnoreCase)) diff = fileNoExt.Substring(folder.Length + 1);
        s.Name = song.Value<string>("song") ?? folder;
        double bpm = song.Value<double?>("bpm") ?? 100;
        s.Bpms = new List<EdTimeChange> { new EdTimeChange { T = 0, Bpm = bpm } };
        s.Player = song.Value<string>("player1") ?? "bf";
        s.Opponent = song.Value<string>("player2") ?? "dad";
        s.Gf = song.Value<string>("gfVersion") ?? song.Value<string>("player3") ?? "gf";
        s.Stage = song.Value<string>("stage") ?? "stage";
        s.NeedsVoices = song.Value<bool?>("needsVoices") ?? true;
        s.ArrowSkin = song.Value<string>("arrowSkin") ?? "";
        s.SplashSkin = song.Value<string>("splashSkin") ?? "";
        s.Difficulties = new List<string> { diff };
        s.Speed[diff] = song.Value<double?>("speed") ?? 1.0;
        string fmt = song.Value<string>("format") ?? "";
        bool v1 = fmt.StartsWith("psych_v1");
        var notes = s.NotesFor(diff);
        double t = 0, curBpm = bpm;
        int lastFocus = -1;
        if (song["notes"] is JArray secs)
        {
            foreach (var secTok in secs)
            {
                if (secTok is not JObject sec) continue;
                if ((sec.Value<bool?>("changeBPM") ?? false) && sec.Value<double?>("bpm") is double nb && nb > 0 && Math.Abs(nb - curBpm) > 1e-6)
                {
                    curBpm = nb;
                    if (t <= 0.001) s.Bpms[0].Bpm = nb; else s.Bpms.Add(new EdTimeChange { T = t, Bpm = nb });
                }
                bool must = sec.Value<bool?>("mustHitSection") ?? true;
                bool gfSec = sec.Value<bool?>("gfSection") ?? false;
                int focus = gfSec ? 2 : must ? 0 : 1;
                if (focus != lastFocus) { s.Events.Add(new EdEvent { T = t, E = "FocusCamera", V = new JObject { ["char"] = focus } }); lastFocus = focus; }
                double beats = sec.Value<double?>("sectionBeats") ?? (sec.Value<double?>("lengthInSteps") is double st ? st / 4.0 : 4.0);
                if (sec["sectionNotes"] is JArray sn)
                    foreach (var nt in sn)
                    {
                        if (nt is not JArray a || a.Count < 2) continue;
                        double nt0 = a[0].Value<double>();
                        int d = (int)a[1].Value<double>();
                        if (d < 0)
                        {   // legacy event-in-note
                            s.Events.Add(new EdEvent { T = nt0, E = a.Count > 2 ? a[2].ToString() : "", V = new JObject { ["v1"] = a.Count > 3 ? a[3].ToString() : "", ["v2"] = a.Count > 4 ? a[4].ToString() : "" } });
                            continue;
                        }
                        if (!v1)
                        {
                            bool gotta = d % 8 < 4 ? must : !must;
                            d = d % 4 + (gotta ? 0 : 4);
                        }
                        var n = new EdNote { T = nt0, D = d % 8, L = a.Count > 2 && a[2].Type is JTokenType.Integer or JTokenType.Float ? a[2].Value<double>() : 0 };
                        if (a.Count > 3)
                        {
                            if (a[3].Type == JTokenType.String) n.K = a[3].Value<string>();
                            else if (a[3].Type == JTokenType.Integer) n.K = a[3].Value<int>() switch { 1 => "Alt Animation", 2 => "Hey!", 3 => "Hurt Note", 4 => "GF Sing", 5 => "No Animation", _ => "" };
                            else if (a[3].Type == JTokenType.Boolean && a[3].Value<bool>()) n.K = "Alt Animation";
                        }
                        notes.Add(n);
                    }
                t += beats * 60000.0 / curBpm;
            }
        }
        if (song["events"] is JArray evs)
            foreach (var ev in evs)
            {
                if (ev is not JArray ea || ea.Count < 2 || ea[1] is not JArray list) continue;
                double et = ea[0].Value<double>();
                foreach (var e in list)
                    if (e is JArray one && one.Count >= 1)
                        s.Events.Add(new EdEvent { T = et, E = one[0].ToString(), V = new JObject { ["v1"] = one.Count > 1 ? one[1].ToString() : "", ["v2"] = one.Count > 2 ? one[2].ToString() : "" } });
            }
        // events.json next to the chart (Psych loads it in PlayState.generateSong)
        string evPath = Path.Combine(Path.GetDirectoryName(path) ?? "", "events.json");
        if (global::FNF_MonoGame.Engine.FsCI.FileExists(evPath) && !string.Equals(Path.GetFullPath(evPath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
        {
            try { var er = LoadPsych(evPath, null, "x"); foreach (var e in er.Events) if (e.E != "FocusCamera") s.Events.Add(e); } catch { }
        }
        // other difficulties in the same folder
        if (diffOverride == null)
        {
            try
            {
                foreach (var f in global::FNF_MonoGame.Engine.FsCI.GetFiles(Path.GetDirectoryName(path) ?? ".", "*.json"))
                {
                    string n = Path.GetFileNameWithoutExtension(f);
                    if (string.Equals(f, path, StringComparison.OrdinalIgnoreCase) || n.Equals("events", StringComparison.OrdinalIgnoreCase)) continue;
                    string d2 = n.Equals(folder, StringComparison.OrdinalIgnoreCase) ? "normal" : n.StartsWith(folder + "-", StringComparison.OrdinalIgnoreCase) ? n.Substring(folder.Length + 1) : null;
                    if (d2 == null || s.Difficulties.Contains(d2, StringComparer.OrdinalIgnoreCase)) continue;
                    var o = LoadPsych(f, null, d2);
                    s.Notes[d2] = o.NotesFor(d2); s.Speed[d2] = o.SpeedFor(d2); s.Difficulties.Add(d2);
                }
            }
            catch (Exception ex) { Console.WriteLine("[EDITOR] diff scan: " + ex.Message); }
            var order = new[] { "easy", "normal", "hard", "erect", "nightmare" };
            s.Difficulties = s.Difficulties.OrderBy(d => { int i = System.Array.IndexOf(order, d.ToLowerInvariant()); return i < 0 ? 99 : i; }).ToList();
        }
        s.SortAll();
        return s;
    }

    /// <summary>V-Slice: SongMetadata (timeChanges, playData) + SongChartData (scrollSpeed, notes, events per difficulty).</summary>
    public static EdSong LoadVSlice(string path)
    {
        string dir = Path.GetDirectoryName(path) ?? ".";
        string fn = Path.GetFileName(path);
        string chartPath, metaPath;
        if (fn.Equals("chart.json", StringComparison.OrdinalIgnoreCase) || fn.Equals("meta.json", StringComparison.OrdinalIgnoreCase))
        { chartPath = Path.Combine(dir, "chart.json"); metaPath = Path.Combine(dir, "meta.json"); }
        else
        {
            string baseName = fn.Replace("-metadata.json", "").Replace("-chart.json", "");
            chartPath = Path.Combine(dir, baseName + "-chart.json"); metaPath = Path.Combine(dir, baseName + "-metadata.json");
        }
        var s = new EdSong { SourceFormat = "vslice", SourcePath = chartPath };
        s.Id = fn.Equals("chart.json", StringComparison.OrdinalIgnoreCase) || fn.Equals("meta.json", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileName(Path.GetDirectoryName(dir)) : fn.Replace("-metadata.json", "").Replace("-chart.json", "");
        if (global::FNF_MonoGame.Engine.FsCI.FileExists(metaPath))
        {
            var m = JObject.Parse(SanitizeJson(global::FNF_MonoGame.Engine.FsCI.ReadAllText(metaPath)));
            s.Name = m.Value<string>("songName") ?? s.Id;
            s.Artist = m.Value<string>("artist") ?? "Unknown";
            s.Charter = m.Value<string>("charter") ?? "";
            if (m["timeChanges"] is JArray tcs && tcs.Count > 0)
            {
                s.Bpms = new List<EdTimeChange>();
                foreach (var tc in tcs) s.Bpms.Add(new EdTimeChange { T = tc.Value<double?>("t") ?? 0, Bpm = tc.Value<double?>("bpm") ?? 100, N = tc.Value<int?>("n") ?? 4, D = tc.Value<int?>("d") ?? 4 });
            }
            if (m["playData"] is JObject pd)
            {
                if (pd["difficulties"] is JArray da) s.Difficulties = da.Select(x => x.ToString()).ToList();
                if (pd["characters"] is JObject ch)
                {
                    s.Player = ch.Value<string>("player") ?? s.Player; s.Opponent = ch.Value<string>("opponent") ?? s.Opponent; s.Gf = ch.Value<string>("girlfriend") ?? s.Gf;
                }
                s.Stage = pd.Value<string>("stage") ?? s.Stage;
                s.NoteStyle = pd.Value<string>("noteStyle") ?? s.NoteStyle;
                s.Album = pd.Value<string>("album") ?? s.Album;
            }
        }
        if (global::FNF_MonoGame.Engine.FsCI.FileExists(chartPath))
        {
            var c = JObject.Parse(SanitizeJson(global::FNF_MonoGame.Engine.FsCI.ReadAllText(chartPath)));
            if (c["scrollSpeed"] is JObject ss) foreach (var p in ss.Properties()) s.Speed[p.Name] = p.Value.Value<double>();
            if (c["notes"] is JObject nn)
                foreach (var p in nn.Properties())
                {
                    var l = s.NotesFor(p.Name);
                    if (p.Value is JArray arr)
                        foreach (var n in arr)
                            l.Add(new EdNote { T = n.Value<double?>("t") ?? 0, D = n.Value<int?>("d") ?? 0, L = n.Value<double?>("l") ?? 0, K = n.Value<string>("k") ?? "" });
                    if (!s.Difficulties.Contains(p.Name, StringComparer.OrdinalIgnoreCase)) s.Difficulties.Add(p.Name);
                }
            if (c["events"] is JArray ev)
                foreach (var e in ev) s.Events.Add(new EdEvent { T = e.Value<double?>("t") ?? 0, E = e.Value<string>("e") ?? "", V = e["v"]?.DeepClone() });
        }
        s.SortAll();
        return s;
    }

    // ================================================================== SAVE
    private static JToken Num(double v) => Math.Abs(v - Math.Round(v)) < 1e-9 ? new JValue((long)Math.Round(v)) : new JValue(Math.Round(v, 3));

    /// <summary>Focus at time t from FocusCamera events (0 player, 1 opponent, 2 gf). Default opponent like Psych's mustHitSection false.</summary>
    public int FocusAt(double t)
    {
        int f = 1;
        foreach (var e in Events)
        {
            if (e.T > t + 1.0) break;
            if (e.E == "FocusCamera") f = FocusValue(e.V);
        }
        return f;
    }
    public static int FocusValue(JToken v)
    {
        if (v == null) return 0;
        if (v.Type is JTokenType.Integer or JTokenType.Float) return v.Value<int>();
        if (v is JObject o && o["char"] != null) return o["char"].Value<int>();
        if (v.Type == JTokenType.String && int.TryParse(v.ToString(), out int i)) return i;
        return 0;
    }

    /// <summary>Psych 1.0 chart JSON (format psych_v1, unwrapped like Psych 1.0's chart editor saves).</summary>
    public JObject ToPsych(string diff)
    {
        SortAll();
        var notes = NotesFor(diff);
        double end = Math.Max(LastTime() + 1, 1);
        var secs = new JArray();
        double beat = 0; double prevBpm = Bpms[0].Bpm;
        int guard = 0;
        while (BeatToMs(beat) < end && guard++ < 5000)
        {
            double t0 = BeatToMs(beat), t1 = BeatToMs(beat + 4);
            double bpm = BpmAt(t0 + 0.5);
            int focus = FocusAt(t0);
            var sec = new JObject
            {
                ["sectionNotes"] = new JArray(),
                ["sectionBeats"] = 4,
                ["mustHitSection"] = focus == 0,
                ["gfSection"] = focus == 2,
                ["altAnim"] = false,
                ["changeBPM"] = Math.Abs(bpm - prevBpm) > 1e-6,
                ["bpm"] = Num(bpm),
            };
            prevBpm = bpm;
            var sn = (JArray)sec["sectionNotes"];
            foreach (var n in notes)
                if (n.T >= t0 - 0.001 && n.T < t1 - 0.001)
                {
                    var a = new JArray { Num(n.T), n.D, Num(n.L) };
                    if (!string.IsNullOrEmpty(n.K)) a.Add(n.K);
                    sn.Add(a);
                }
            secs.Add(sec);
            beat += 4;
        }
        var evs = new JArray();
        foreach (var g in Events.Where(e => e.E != "FocusCamera").GroupBy(e => e.T))
        {
            var list = new JArray();
            foreach (var e in g)
            {
                var (n, v1, v2) = ToPsychEvent(e);
                list.Add(new JArray { n, v1, v2 });
            }
            evs.Add(new JArray { Num(g.Key), list });
        }
        return new JObject
        {
            ["song"] = Name,
            ["notes"] = secs,
            ["events"] = evs,
            ["bpm"] = Num(Bpms[0].Bpm),
            ["needsVoices"] = NeedsVoices,
            ["speed"] = Num(SpeedFor(diff)),
            ["offset"] = Num(PsychOffset),
            ["player1"] = Player,
            ["player2"] = Opponent,
            ["gfVersion"] = Gf,
            ["stage"] = Stage,
            ["format"] = "psych_v1",
            ["arrowSkin"] = ArrowSkin,
            ["splashSkin"] = SplashSkin,
        };
    }

    /// <summary>V-Slice event -> Psych event (name, value1, value2). Known kinds are translated, others pass through.</summary>
    public static (string, string, string) ToPsychEvent(EdEvent e)
    {
        var v = e.V;
        if (v is JObject o && o.ContainsKey("v1")) return (e.E, o.Value<string>("v1") ?? "", o.Value<string>("v2") ?? "");
        switch (e.E)
        {
            case "PlayAnimation":
                string target = (v as JObject)?.Value<string>("target") ?? "bf";
                target = target switch { "bf" or "boyfriend" or "player" => "BF", "dad" or "opponent" => "Dad", "gf" or "girlfriend" => "GF", _ => target };
                return ("Play Animation", (v as JObject)?.Value<string>("anim") ?? "", target);
            case "ZoomCamera":
                return ("Add Camera Zoom", Ed.F(((v as JObject)?.Value<double?>("zoom") ?? 1.0) - 1.0 > 0 ? 0.015 : 0.015), "0.03");
            case "SetCameraBop":
                return ("Set Camera Bop", Ed.F((v as JObject)?.Value<double?>("rate") ?? 4), Ed.F((v as JObject)?.Value<double?>("intensity") ?? 1));
            default:
                return (e.E, v == null ? "" : v.Type == JTokenType.String ? v.ToString() : v.ToString(Formatting.None), "");
        }
    }

    /// <summary>Psych event -> V-Slice event (only the kinds both engines know are translated).</summary>
    public static EdEvent ToVSliceEvent(EdEvent e)
    {
        if (e.V is not JObject o || !o.ContainsKey("v1")) return e.Clone();
        string v1 = o.Value<string>("v1") ?? "", v2 = o.Value<string>("v2") ?? "";
        switch (e.E)
        {
            case "Play Animation":
                string t = v2.Trim().ToLowerInvariant() switch { "bf" or "boyfriend" or "1" => "bf", "gf" or "girlfriend" or "2" => "gf", _ => "dad" };
                return new EdEvent { T = e.T, E = "PlayAnimation", V = new JObject { ["target"] = t, ["anim"] = v1, ["force"] = true } };
            case "Hey!":
                string who = v1.Trim().ToLowerInvariant() is "gf" or "girlfriend" or "1" ? "gf" : "bf";
                return new EdEvent { T = e.T, E = "PlayAnimation", V = new JObject { ["target"] = who, ["anim"] = who == "gf" ? "cheer" : "hey", ["force"] = true } };
            default: return e.Clone();
        }
    }

    public JObject ToVSliceChart()
    {
        SortAll();
        var speed = new JObject(); var notes = new JObject();
        foreach (var d in Difficulties)
        {
            speed[d] = Num(SpeedFor(d));
            var arr = new JArray();
            foreach (var n in NotesFor(d))
            {
                var o = new JObject { ["t"] = Num(n.T), ["d"] = n.D };
                if (n.L > 0) o["l"] = Num(n.L);
                if (!string.IsNullOrEmpty(n.K)) o["k"] = n.K;
                arr.Add(o);
            }
            notes[d] = arr;
        }
        var evs = new JArray();
        foreach (var e0 in Events)
        {
            var e = ToVSliceEvent(e0);
            evs.Add(new JObject { ["t"] = Num(e.T), ["e"] = e.E, ["v"] = e.V?.DeepClone() ?? new JObject() });
        }
        return new JObject { ["version"] = "2.0.0", ["scrollSpeed"] = speed, ["events"] = evs, ["notes"] = notes, ["generatedBy"] = "FNF MonoGame Editors (merged Psych + V-Slice)" };
    }

    public JObject ToVSliceMeta()
    {
        var tcs = new JArray();
        foreach (var b in Bpms) tcs.Add(new JObject { ["t"] = Num(b.T), ["bpm"] = Num(b.Bpm), ["n"] = b.N, ["d"] = b.D, ["bt"] = new JArray(4, 4, 4, 4) });
        var ratings = new JObject(); foreach (var d in Difficulties) ratings[d] = 1;
        double len = LastTime();
        return new JObject
        {
            ["version"] = "2.2.4",
            ["songName"] = Name,
            ["artist"] = Artist,
            ["charter"] = Charter,
            ["timeFormat"] = "ms",
            ["offsets"] = new JObject { ["instrumental"] = 0 },
            ["timeChanges"] = tcs,
            ["looped"] = false,
            ["playData"] = new JObject
            {
                ["songVariations"] = new JArray(),
                ["difficulties"] = new JArray(Difficulties.ToArray()),
                ["ratings"] = ratings,
                ["characters"] = new JObject { ["player"] = Player, ["girlfriend"] = Gf, ["opponent"] = Opponent, ["instrumental"] = "" },
                ["stage"] = Stage,
                ["noteStyle"] = NoteStyle,
                ["album"] = Album,
                ["previewStart"] = 0,
                ["previewEnd"] = Num(Math.Min(15000, Math.Max(1000, len))),
            },
            ["generatedBy"] = "FNF MonoGame Editors (merged Psych + V-Slice)",
        };
    }

    public static string PsychDiffSuffix(string diff) => diff.Equals("normal", StringComparison.OrdinalIgnoreCase) ? "" : "-" + diff.ToLowerInvariant();

    /// <summary>Writes every difficulty as Psych charts into &lt;modRoot&gt;/data/&lt;id&gt;/. Returns the written paths.</summary>
    public List<string> SavePsych(string modRoot)
    {
        var outp = new List<string>();
        string dir = Path.Combine(modRoot, "data", Id);
        Directory.CreateDirectory(dir);
        foreach (var d in Difficulties)
        {
            string p = Path.Combine(dir, Id + PsychDiffSuffix(d) + ".json");
            File.WriteAllText(p, ToPsych(d).ToString(Formatting.Indented));
            outp.Add(p);
        }
        return outp;
    }

    /// <summary>Writes V-Slice &lt;id&gt;-chart.json + &lt;id&gt;-metadata.json into &lt;modRoot&gt;/data/songs/&lt;id&gt;/ (V-Slice mod layout).</summary>
    public List<string> SaveVSlice(string modRoot)
    {
        string dir = Path.Combine(modRoot, "data", "songs", Id);
        Directory.CreateDirectory(dir);
        string c = Path.Combine(dir, Id + "-chart.json"), m = Path.Combine(dir, Id + "-metadata.json");
        File.WriteAllText(c, ToVSliceChart().ToString(Formatting.Indented));
        File.WriteAllText(m, ToVSliceMeta().ToString(Formatting.Indented));
        return new List<string> { c, m };
    }
}
