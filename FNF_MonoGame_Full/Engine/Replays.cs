using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Recorded play of one song (Psych Online ReplayRecorder idea): every note key press / release with the judged song
/// position, plus what is needed to start the same chart again and the final result. Stored as JSON in
/// &lt;game&gt;/replays (UWP: LocalState/replays).
/// </summary>
public sealed class ReplayData
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("song")] public string Song { get; set; } = "";
    [JsonPropertyName("songTitle")] public string SongTitle { get; set; } = "";
    [JsonPropertyName("difficulty")] public string Difficulty { get; set; } = "normal";
    [JsonPropertyName("variation")] public string Variation { get; set; } = "default";
    [JsonPropertyName("instrumental")] public string Instrumental { get; set; }
    [JsonPropertyName("modPath")] public string ModPath { get; set; }
    [JsonPropertyName("playbackRate")] public float PlaybackRate { get; set; } = 1f;
    [JsonPropertyName("player")] public string Player { get; set; }
    [JsonPropertyName("date")] public string Date { get; set; } = "";
    [JsonPropertyName("score")] public int Score { get; set; }
    [JsonPropertyName("misses")] public int Misses { get; set; }
    [JsonPropertyName("maxCombo")] public int MaxCombo { get; set; }
    [JsonPropertyName("sicks")] public int Sicks { get; set; }
    [JsonPropertyName("goods")] public int Goods { get; set; }
    [JsonPropertyName("bads")] public int Bads { get; set; }
    [JsonPropertyName("shits")] public int Shits { get; set; }
    [JsonPropertyName("accuracy")] public double Accuracy { get; set; }
    /// <summary>Settings that change judgement, so playback judges exactly like the recording.</summary>
    [JsonPropertyName("judge")] public float[] Judge { get; set; }   // sick, good, bad, safeFrames, ratingOffset, ghostTapping(0/1), globalOffsetMs
    /// <summary>[songTimeSeconds, lane, 1 = press / 0 = release]</summary>
    [JsonPropertyName("inputs")] public List<double[]> Inputs { get; set; } = new();

    [JsonIgnore] public string FilePath { get; set; }
}

public static class ReplayManager
{
    public static string Dir
    {
        get
        {
#if XBOX_UWP
            string d = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "replays");
#else
            string d = Path.Combine(AssetManager.GameRoot, "replays");
#endif
            try { Directory.CreateDirectory(d); } catch { }
            return d;
        }
    }

    /// <summary>Last saved replay (Results screen "watch replay").</summary>
    public static string LastSaved { get; set; }

    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = false };

    public static string Save(ReplayData r)
    {
        try
        {
            string safe = new string(r.Song.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());
            string name = $"{safe}-{r.Difficulty}-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.json";
            string p = Path.Combine(Dir, name);
            File.WriteAllText(p, JsonCompat.Serialize(r, false)); // UWP-safe (see JsonCompat)
            r.FilePath = p; LastSaved = p;
            Console.WriteLine($"[REPLAY] saved {p} ({r.Inputs.Count} inputs, score {r.Score})");
            return p;
        }
        catch (Exception e) { Console.WriteLine($"[REPLAY] save failed: {e.Message}"); return null; }
    }

    public static ReplayData Load(string path)
    {
        try
        {
            var r = JsonCompat.Deserialize<ReplayData>(global::FNF_MonoGame.Engine.FsCI.ReadAllText(path));
            if (r != null) r.FilePath = path;
            return r;
        }
        catch (Exception e) { Console.WriteLine($"[REPLAY] load failed {path}: {e.Message}"); return null; }
    }

    /// <summary>Replays newest first; song filter matches the song id (null = all).</summary>
    public static List<ReplayData> List(string song = null)
    {
        var list = new List<ReplayData>();
        try
        {
            foreach (var f in global::FNF_MonoGame.Engine.FsCI.GetFiles(Dir, "*.json").OrderByDescending(File.GetLastWriteTimeUtc))
            {
                var r = Load(f);
                if (r == null) continue;
                if (song != null && !string.Equals(r.Song, song, StringComparison.OrdinalIgnoreCase)) continue;
                r.Inputs = null; // listing only (loaded again on play)
                list.Add(r);
            }
        }
        catch { }
        return list;
    }
}
