using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Read-only Psych Online network boards for a song (Funkin-Online-Server, routes/top.ts + routes/song.ts):
///   GET /api/top/song?song=&lt;id&gt;&amp;strum=2&amp;page=N[&amp;category=week]&amp;keys=4  -> TopScore[] (15 per page)   (Leaderboard.fetchLeaderboard)
///   GET /api/song/comments?id=&lt;id&gt;                                          -> {player, content, at, submitted}[] (FunkinNetwork.fetchSongComments)
/// Both are public (no checkAccess) so they work without logging in. Song id = PlayState.songId / FreeplayState:
/// filterCharacters(SONG.song) + "-" + filterCharacters(Difficulty) + "-" + Md5(trimmed raw chart), filterCharacters keeps [A-Za-z0-9].
/// Writing (score submit + replay upload, posting comments) needs the account token (checkAccess) and is not done here.
/// </summary>
public static class PsychOnlineBoards
{
    public sealed class TopScore
    {
        public int Rank; public string Player, Id, ModUrl; public long Score; public double Accuracy, Points, PlaybackRate;
        public int Misses, Sicks, Goods, Bads, Shits; public DateTime? Submitted;
    }
    public sealed class Comment { public string Player, Content; public double At; public DateTime? Submitted; }

    public static readonly string[] Categories = { null, "week" };
    public static readonly string[] CategoryTitles = { "ALL TIME", "WEEKLY" };

    private static readonly HttpClient _http = MakeClient();
    private static HttpClient MakeClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (FNF-MonoGame)");
        return h;
    }

    public static string FilterCharacters(string s)
    {
        if (s == null) return "";
        var sb = new StringBuilder();
        foreach (char c in s) if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) sb.Append(c);
        return sb.ToString();
    }

    /// <summary>Psych difficulty name as Difficulty.getString gives it (default list Easy/Normal/Hard, PSO extras Erect/Nightmare).</summary>
    public static string PsychDiff(string diff)
    {
        if (string.IsNullOrEmpty(diff)) return "Normal";
        return char.ToUpperInvariant(diff[0]) + diff.Substring(1).ToLowerInvariant();
    }

    /// <summary>Paths.formatToSongPath(song) + Difficulty.getFilePath: "-diff" suffix except for Normal.</summary>
    public static string ChartFile(string song, string diff)
    {
        string d = PsychDiff(diff);
        string s = PsychOnline.FormatToSongPath(song);
        return d == "Normal" ? s : s + "-" + PsychOnline.FormatToSongPath(d);
    }

    /// <summary>
    /// Network song id for a Freeplay selection: engine song folder (bopeebo, bopeebo_pico, bopeebo_erect ...), difficulty
    /// (easy/normal/hard/erect/nightmare) and the Psych mod folder for mod songs. Null (+ reason) when no chart that the
    /// Psych Online players also have exists locally (V-Slice-only songs/mods).
    /// </summary>
    public static string SongId(string engineSong, string diff, string modPath, out string reason, out string chartPath)
    {
        reason = null; chartPath = null;
        if (string.IsNullOrEmpty(engineSong)) { reason = "no song"; return null; }
        var tried = new List<string>();
        if (!string.IsNullOrEmpty(modPath))
        {
            string modDir = Path.GetFileName(modPath.TrimEnd('/', '\\'));
            string p = PsychOnline.ChartPath(engineSong, ChartFile(engineSong, diff), modDir);
            tried.Add(p);
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(p)) chartPath = p;
        }
        else
        {
            string s = engineSong.ToLowerInvariant(), suffix = "";
            foreach (var v in new[] { "_erect", "_pico", "_bf" })
                if (s.EndsWith(v)) { s = s.Substring(0, s.Length - v.Length); if (v != "_erect") suffix = "-" + v.Substring(1); break; }
            foreach (var b in new[] { s, s.Replace('_', '-'), s.Replace("_", "") })
            {
                string folder = b + suffix;
                string p = PsychOnline.ChartPath(folder, ChartFile(folder, diff));
                tried.Add(p);
                if (global::FNF_MonoGame.Engine.FsCI.FileExists(p)) { chartPath = p; break; }
            }
        }
        if (chartPath == null) { reason = "not a Psych Online song"; Console.WriteLine("[PSOBOARD] no chart: " + string.Join(" | ", tried)); return null; }
        try
        {
            var b = global::FNF_MonoGame.Engine.FsCI.ReadAllBytes(chartPath);
            int st = 0, e = b.Length;   // StringTools.trim (same as PsychOnline.ChartHash)
            while (st < e && (b[st] == 32 || (b[st] > 8 && b[st] < 14))) st++;
            while (e > st && (b[e - 1] == 32 || (b[e - 1] > 8 && b[e - 1] < 14))) e--;
            string hash = Md5.Hex(b, st, e - st);
            var j = JObject.Parse(Encoding.UTF8.GetString(b, st, e - st).TrimStart('\uFEFF'));
            string name = j["song"] is JObject o ? (string)o["song"] : (string)j["song"];   // Song.loadFromJson: legacy {song:{song}} or 1.0 unwrapped
            if (string.IsNullOrEmpty(name)) { reason = "chart has no song name"; return null; }
            return FilterCharacters(name) + "-" + FilterCharacters(PsychDiff(diff)) + "-" + FilterCharacters(hash);
        }
        catch (Exception ex) { reason = "chart unreadable: " + ex.Message; return null; }
    }

    private static string Http => PsychOnline.NetworkHttp;

    private static async Task<JArray> GetArray(string path, CancellationToken ct)
    {
        using var r = await _http.GetAsync(Http + path, ct).ConfigureAwait(false);
        if ((int)r.StatusCode == 404) return new JArray();   // song unknown to the server = empty board
        r.EnsureSuccessStatusCode();
        string t = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
        return t.TrimStart().StartsWith("[") ? JArray.Parse(t) : new JArray();
    }

    private static DateTime? Date(JToken t)
    {
        if (t == null || t.Type == JTokenType.Null) return null;
        if (t.Type == JTokenType.Date) return ((DateTime)t).ToUniversalTime();
        return DateTime.TryParse((string)t, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : (DateTime?)null;
    }

    /// <summary>One page (15 entries) of the song leaderboard; strum 2 = player side (1 with opponent play), keys = mania keys (4).</summary>
    public static async Task<List<TopScore>> TopAsync(string songId, int page, int category, int strum = 2, int keys = 4, CancellationToken ct = default)
    {
        string cat = Categories[Math.Clamp(category, 0, Categories.Length - 1)];
        var a = await GetArray("/api/top/song?song=" + Uri.EscapeDataString(songId) + "&strum=" + strum + "&page=" + page + (cat != null ? "&category=" + cat : "") + "&keys=" + keys, ct).ConfigureAwait(false);
        var l = new List<TopScore>();
        int i = 0;
        foreach (var o in a.Children<JObject>())
            l.Add(new TopScore
            {
                Rank = page * 15 + (++i), Player = (string)o["player"] ?? "?", Id = (string)o["id"], ModUrl = (string)o["modURL"],
                Score = (long?)(double?)o["score"] ?? 0, Accuracy = (double?)o["accuracy"] ?? 0, Points = (double?)o["points"] ?? 0,
                Misses = (int?)(double?)o["misses"] ?? 0, Sicks = (int?)(double?)o["sicks"] ?? 0, Goods = (int?)(double?)o["goods"] ?? 0,
                Bads = (int?)(double?)o["bads"] ?? 0, Shits = (int?)(double?)o["shits"] ?? 0, PlaybackRate = (double?)o["playbackRate"] ?? 1,
                Submitted = Date(o["submitted"]),
            });
        return l;
    }

    /// <summary>Song comments (NicommentsView: shown at song time 'at' ms), sorted by time.</summary>
    public static async Task<List<Comment>> CommentsAsync(string songId, CancellationToken ct = default)
    {
        var a = await GetArray("/api/song/comments?id=" + Uri.EscapeDataString(songId), ct).ConfigureAwait(false);
        var l = new List<Comment>();
        foreach (var o in a.Children<JObject>())
            l.Add(new Comment { Player = (string)o["player"] ?? "?", Content = Clean((string)o["content"]), At = (double?)o["at"] ?? 0, Submitted = Date(o["submitted"]) });
        l.Sort((x, y) => x.At.CompareTo(y.At));
        return l;
    }

    /// <summary>VCR has ASCII glyphs only: drop control chars, map the rest to '?' (runs collapsed).</summary>
    public static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            char o = c < 32 ? ' ' : c < 127 ? c : '?';
            if (o == '?' && c >= 127 && sb.Length > 0 && sb[sb.Length - 1] == '?') continue;
            sb.Append(o);
        }
        return sb.ToString().Trim();
    }

    public static string Ago(DateTime? utc)
    {
        if (utc == null) return "";
        var d = DateTime.UtcNow - utc.Value;
        if (d.TotalDays >= 365) return (int)(d.TotalDays / 365) + "Y AGO";
        if (d.TotalDays >= 30) return (int)(d.TotalDays / 30) + "MO AGO";
        if (d.TotalDays >= 1) return (int)d.TotalDays + "D AGO";
        if (d.TotalHours >= 1) return (int)d.TotalHours + "H AGO";
        return Math.Max(1, (int)d.TotalMinutes) + "M AGO";
    }

    public static string SongTime(double ms)
    {
        if (ms < 0) return "INTRO";
        int s = (int)(ms / 1000);
        return (s / 60) + ":" + (s % 60).ToString("00");
    }
}
