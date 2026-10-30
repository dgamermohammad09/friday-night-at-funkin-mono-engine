using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using FNF_MonoGame.Engine.Colyseus;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Psych Online parts that were missing (PSO-FULL round): Funkin Points (online/FunkinPoints.hx), player accuracy / FC rating
/// (GameClient.getPlayerAccuracyPercent / getPlayerRating), chat commands (ChatBox -> "command"), kick, room stages
/// (SelectStageSubstate -> "setStage"), room gameplay settings (GameClient.getGameplaySetting: songspeed), win conditions
/// (ResultsState), score submit + replay upload (ReplayRecorder.save/upload, Leaderboard.submitScore) and the public
/// network leaderboard / profiles (Leaderboard.fetchPlayerLeaderboard, /api/user/details).
/// </summary>
public static partial class PsychOnline
{
    // ------------------------------------------------------------------ Funkin Points (FunkinPoints.hx)
    private static double _fp = double.NaN;
    private static string FpFile
    {
        get
        {
#if XBOX_UWP
            return Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "psychonline_fp.json");
#else
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "psychonline_fp.json");
#endif
        }
    }

    /// <summary>FlxG.save.data.funkinPointsv4 (offline total; the server replaces it with the account FP when logged in).</summary>
    public static double FunkinPoints
    {
        get
        {
            if (double.IsNaN(_fp))
            {
                _fp = 0;
                try { if (global::FNF_MonoGame.Engine.FsCI.FileExists(FpFile)) _fp = (double?)JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(FpFile))["funkinPointsv4"] ?? 0; } catch { }
            }
            return _fp;
        }
        set
        {
            _fp = value;
            try { File.WriteAllText(FpFile, new JObject { ["funkinPointsv4"] = value }.ToString()); } catch (Exception e) { Console.WriteLine("[PSO] fp save failed: " + e.Message); }
        }
    }

    /// <summary>FunkinPoints.fcalcFP (accuracy 0..1).</summary>
    public static double FCalcFP(double accuracy, double misses, double songDensity, double notesHit, double maxCombo)
    {
        if (accuracy <= 0 || notesHit <= 0) return 0;
        double fp = (1 + songDensity) * (notesHit / 200.0);
        fp *= 1 + maxCombo / 2000.0;
        fp *= Math.Pow(accuracy, 3) / (1 + misses * 0.25);
        return fp;
    }

    /// <summary>FunkinPoints.calcFP: floored.</summary>
    public static double CalcFP(double accuracy, double misses, double songDensity, double notesHit, double maxCombo)
        => Math.Floor(FCalcFP(accuracy, misses, songDensity, notesHit, maxCombo));

    /// <summary>FunkinPoints.save: adds the gained FP to the total and sends "updateFP".</summary>
    public static double SaveFP(double accuracy, double misses, double songDensity, double notesHit, double maxCombo)
    {
        double gained = CalcFP(accuracy, misses, songDensity, notesHit, maxCombo);
        FunkinPoints += gained;
        Send("updateFP", FunkinPoints);
        return gained;
    }

    // ------------------------------------------------------------------ player stats (GameClient)
    /// <summary>Psych Rating.ratingMod: sick 1, good 0.67, bad 0.34, shit 0.</summary>
    public static double AccuracyPercent(SchemaObject p)
    {
        if (p == null) return 0;
        double s = p.Num("sicks"), g = p.Num("goods"), b = p.Num("bads"), sh = p.Num("shits"), m = p.Num("misses");
        double played = s + g + b + sh + m;
        if (played == 0) return 0.0;
        double hit = s * 1.0 + g * 0.67 + b * 0.34 + sh * 0.0;
        return Math.Floor(Math.Min(1, Math.Max(0, hit / played)) * 100 * 100) / 100;   // CoolUtil.floorDecimal(.., 2)
    }

    /// <summary>GameClient.getPlayerRating.</summary>
    public static string RatingFC(SchemaObject p)
    {
        if (p == null) return "Clear";
        double m = p.Num("misses");
        if (m < 1)
        {
            if (p.Num("bads") > 0 || p.Num("shits") > 0) return "FC";
            if (p.Num("goods") > 0) return "GFC";
            if (p.Num("sicks") > 0) return "SFC";
            return "Clear";
        }
        return m < 10 ? "SDCB" : "Clear";
    }

    public static string NameOf(string sid) => Players != null && sid != null && Players.Items.TryGetValue(sid, out var o) && o is SchemaObject p ? p.Str("name") : sid;

    // ------------------------------------------------------------------ chat commands (ChatBox: "/x a b" -> send("command", ["x","a","b"]))
    public static readonly string[] Commands = { "/help", "/roll", "/kick <name>", "/report <reason>", "/resize <cap>" };

    /// <summary>Chat line or command (ChatBox.hx 150-156).</summary>
    public static void SendChatOrCommand(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        text = text.Trim();
        if (text.StartsWith("/")) Send("command", text.Substring(1).Split(' ').ToList<object>());
        else Send("chat", text);
    }

    /// <summary>GameRoom "command" kick (owner only; server: kicks every non-host player whose name matches).</summary>
    public static void Kick(string name) => Send("command", new List<object> { "kick", name ?? "" });

    // ------------------------------------------------------------------ room stage (SelectStageSubstate / GameRoom setStage)
    /// <summary>Psych Online base stages (assets/shared/stages) -> engine V-Slice stage ids.</summary>
    public static readonly (string pso, string engine, string title)[] Stages =
    {
        ("", null, "Default"), ("stage", "mainStage", "Stage"), ("stage-erect", "mainStageErect", "Stage (Erect)"),
        ("spooky", "spookyMansion", "Spooky Mansion"), ("spooky-erect", "spookyMansionErect", "Spooky Mansion (Erect)"),
        ("philly", "phillyTrain", "Philly"), ("philly-erect", "phillyTrainErect", "Philly (Erect)"),
        ("limo", "limoRide", "Limo"), ("limo-erect", "limoRideErect", "Limo (Erect)"),
        ("mall", "mallXmas", "Mall"), ("mall-erect", "mallXmasErect", "Mall (Erect)"), ("mallEvil", "mallEvil", "Evil Mall"),
        ("school", "school", "School"), ("school-erect", "schoolErect", "School (Erect)"), ("schoolEvil", "schoolEvil", "Evil School"),
        ("tank", "tankmanBattlefield", "Tank"), ("tank-erect", "tankmanBattlefieldErect", "Tank (Erect)"),
        ("phillyStreets", "phillyStreets", "Philly Streets"), ("phillyStreets-erect", "phillyStreetsErect", "Philly Streets (Erect)"),
        ("phillyBlazin", "phillyBlazin", "Philly Blazin'"),
    };

    public static void SetStage(string psoName) => Send("setStage", new List<object> { psoName ?? "", "", "" });

    /// <summary>Engine stage id for the room's stageName (null = the song's own stage).</summary>
    public static string RoomStageEngineId()
    {
        string n = State?.Str("stageName");
        if (string.IsNullOrEmpty(n)) return null;
        foreach (var s in Stages) if (string.Equals(s.pso, n, StringComparison.OrdinalIgnoreCase)) return s.engine;
        return null;
    }

    public static string RoomStageTitle()
    {
        string n = State?.Str("stageName");
        if (string.IsNullOrEmpty(n)) return "Default";
        foreach (var s in Stages) if (string.Equals(s.pso, n, StringComparison.OrdinalIgnoreCase)) return s.title;
        return n + (string.IsNullOrEmpty(State?.Str("stageMod")) ? "" : " (" + State.Str("stageMod") + ")");
    }

    // ------------------------------------------------------------------ room gameplay settings (GameClient.getGameplaySetting)
    public static readonly float[] SongSpeeds = { 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1f, 1.1f, 1.2f, 1.25f, 1.3f, 1.4f, 1.5f, 1.75f, 2f, 2.5f, 3f };

    /// <summary>Room 'songspeed' (= Psych playback rate) set by a player with perms; 1 when unset.</summary>
    public static float RoomSongSpeed
    {
        get
        {
            var gs = State?.Map("gameplaySettings");
            if (gs == null || !gs.Items.TryGetValue("songspeed", out var v) || v == null) return 1f;
            return float.TryParse(v.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) && f > 0.1f ? f : 1f;
        }
    }

    public static void SetGameplaySetting(string key, object value) => Send("setGameplaySetting", new List<object> { key, value });

    // ------------------------------------------------------------------ win condition (ResultsState.hx 206-360)
    public static readonly string[] WinConditions = { "Accuracy", "Score", "Misses", "FP", "Max Combo" };
    public static readonly string[] WinConditionDesc =
    {
        "Side with the highest Accuracy wins!", "Side with the highest Score wins!", "Side with the least Misses wins!",
        "Side with the most FP wins!", "Side with the highest Combo wins!",
    };

    public sealed class MatchOutcome
    {
        public string WinnerSid; public bool? WinnerBfSide; public bool Tie;
        public List<(string sid, SchemaObject p)> Players = new();
    }

    private static double WinValue(SchemaObject p, int wc) => wc switch
    {
        0 => AccuracyPercent(p), 1 => p.Num("score"), 2 => p.Num("misses"), 3 => p.Num("songPoints"), _ => p.Num("maxCombo"),
    };

    /// <summary>ResultsState: winner by state.winCondition, team averages in teamMode, ties.</summary>
    public static MatchOutcome ComputeOutcome()
    {
        var o = new MatchOutcome { Players = AllPlayers().ToList() };
        var st = State; if (st == null || o.Players.Count == 0) return o;
        int wc = Math.Clamp((int)st.Num("winCondition"), 0, 4);
        bool team = st.Bool("teamMode");
        double? prev = null;
        var byValue = new Dictionary<double, List<string>>();
        double[] sum = new double[2]; int[] cnt = new int[2];
        foreach (var (sid, p) in o.Players)
        {
            double v = WinValue(p, wc);
            if (team) { sum[p.Bool("bfSide") ? 1 : 0] += v; cnt[p.Bool("bfSide") ? 1 : 0]++; }
            bool better = prev == null || (wc == 2 ? v <= prev.Value : v >= prev.Value);
            if (better)
            {
                prev = v; o.WinnerSid = sid;
                if (!byValue.TryGetValue(v, out var l)) byValue[v] = l = new List<string>();
                l.Add(sid);
            }
        }
        if (prev != null && byValue.TryGetValue(prev.Value, out var best) && best.Count >= 2)
        {
            if (!team) o.Tie = true;
            o.WinnerSid = null;
        }
        if (team)
        {
            double a0 = cnt[0] > 0 ? sum[0] / cnt[0] : double.NaN, a1 = cnt[1] > 0 ? sum[1] / cnt[1] : double.NaN;
            if (a0 == a1) { o.Tie = true; o.WinnerBfSide = null; }
            else o.WinnerBfSide = wc == 2 ? a1 < a0 : a1 > a0;
        }
        return o;
    }

    // ------------------------------------------------------------------ Funkin Network: leaderboard, profiles, score submit
    private static HttpRequestMessage NetReq(HttpMethod m, string path)
    {
        var req = new HttpRequestMessage(m, NetworkHttp + path);
        if (LoggedIn) req.Headers.TryAddWithoutValidation("Authorization", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(AuthId + ":" + AuthToken)));
        return req;
    }

    /// <summary>Leaderboard.fetchPlayerLeaderboard(page, category, 'points4k'): [{player, points4k, profileHue, country, club}].</summary>
    public static async Task<JArray> TopPlayersAsync(int page, string category = null)
    {
        try
        {
            var resp = await _http.SendAsync(NetReq(HttpMethod.Get, "/api/top/players?page=" + page + (category != null ? "&category=" + category : "") + "&sort=points4k")).ConfigureAwait(false);
            string txt = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            Console.WriteLine($"[PSO] /api/top/players page {page} -> {(int)resp.StatusCode} {txt.Length} bytes");
            return resp.IsSuccessStatusCode ? JArray.Parse(txt) : null;
        }
        catch (Exception e) { Console.WriteLine("[PSO] top players failed: " + e.Message); return null; }
    }

    /// <summary>GET /api/user/details?name= (ProfileTab): role, joined, points, avgAccuracy, rank, country, club.</summary>
    public static async Task<JObject> UserDetailsAsync(string name)
    {
        try
        {
            var resp = await _http.SendAsync(NetReq(HttpMethod.Get, "/api/user/details?name=" + Uri.EscapeDataString(name ?? ""))).ConfigureAwait(false);
            string txt = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            Console.WriteLine($"[PSO] /api/user/details {name} -> {(int)resp.StatusCode}");
            return resp.IsSuccessStatusCode ? JObject.Parse(txt) : null;
        }
        catch (Exception e) { Console.WriteLine("[PSO] user details failed: " + e.Message); return null; }
    }

    /// <summary>ReplayRecorder.upload -> Leaderboard.submitScore: POST /api/score/submit (needs login; 3 tries, stop on 429).
    /// Returns (gained points or null, message).</summary>
    public static async Task<(double? gained, string msg)> SubmitScoreAsync(JObject replay)
    {
        if (!LoggedIn) return (null, "not logged in - score kept offline");
        if (FNF_MonoGame.Engine.PsychEngine.ClientPrefs.Data.DisableSubmiting) return (null, "leaderboard submit is off (Options)");
        string body = replay.ToString(Newtonsoft.Json.Formatting.None);
        string last = "";
        for (int tries = 0; tries < 3; tries++)
        {
            try
            {
                await Task.Delay(1000).ConfigureAwait(false);
                var req = NetReq(HttpMethod.Post, "/api/score/submit");
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                var resp = await _http.SendAsync(req).ConfigureAwait(false);
                string txt = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                Console.WriteLine($"[PSO] /api/score/submit try {tries + 1} -> {(int)resp.StatusCode} {txt.Substring(0, Math.Min(160, txt.Length))}");
                last = (int)resp.StatusCode + " " + txt;
                if ((int)resp.StatusCode == 429) break;
                if (!resp.IsSuccessStatusCode) continue;
                var j = JObject.Parse(string.IsNullOrEmpty(txt) ? "{}" : txt);
                return ((double?)j["gained_points"], "Replay uploaded" + (j["climbed_ranks"] != null ? ", climbed " + (int)j["climbed_ranks"] + " ranks" : ""));
            }
            catch (Exception e) { last = e.Message; }
        }
        return (null, "submit failed: " + last);
    }

    /// <summary>ReplayData v4 (ReplayRecorder.hx): inputs = [songPosMs, "note_left"|..., 0 press / 1 release].</summary>
    public static JObject BuildReplay(string song, string difficulty, bool opponentMode, string chartHash, IList<(double ms, string id, int move)> inputs,
        int score, int misses, double accuracy01, int sicks, int goods, int bads, int shits, double points, string modUrl)
    {
        var prefs = FNF_MonoGame.Engine.PsychEngine.ClientPrefs.Data;
        var arr = new JArray();
        foreach (var i in inputs) arr.Add(new JArray(i.ms, i.id, i.move));
        return new JObject
        {
            ["player"] = DefaultNickname(), ["song"] = song, ["difficulty"] = difficulty, ["opponent_mode"] = opponentMode,
            ["beat_time"] = (double)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ["chart_hash"] = chartHash ?? "",
            ["accuracy"] = Math.Floor(accuracy01 * 100 * 100) / 100, ["sicks"] = sicks, ["goods"] = goods, ["bads"] = bads, ["shits"] = shits,
            ["misses"] = misses, ["points"] = points, ["score"] = score, ["inputs"] = arr, ["note_offset"] = prefs.NoteOffset,
            ["gameplay_modifiers"] = new JObject(), ["ghost_tapping"] = prefs.GhostTapping, ["rating_offset"] = prefs.RatingOffset,
            ["safe_frames"] = prefs.SafeFrames, ["version"] = 4, ["mod_url"] = modUrl ?? "", ["keys"] = 4,
        };
    }

    /// <summary>Saves the replay like ReplayRecorder.save ("replays/MyReplay-song-DIFF-date.funkinreplay") and uploads it when
    /// logged in. Accuracy under 5 % is not saved ("git gud").</summary>
    public static async Task<string> SaveAndUploadReplayAsync(JObject replay)
    {
        if ((double)replay["accuracy"] < 5) return "your performance was SHIT (accuracy < 5%) - not saved";
        try
        {
#if XBOX_UWP
            string dir = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "replays");
#else
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "replays");
#endif
            Directory.CreateDirectory(dir);
            string name = "MyReplay-" + replay["song"] + "-" + ((string)replay["difficulty"] ?? "").ToUpperInvariant() + "-" + DateTime.Now.ToString("yyyy-MM-dd_HH'-'mm'-'ss") + ".funkinreplay";
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            File.WriteAllText(Path.Combine(dir, name), replay.ToString(Newtonsoft.Json.Formatting.None));
        }
        catch (Exception e) { Console.WriteLine("[PSO] replay save failed: " + e.Message); }
        var (gained, msg) = await SubmitScoreAsync(replay).ConfigureAwait(false);
        return gained != null ? msg + " (+" + gained + " FP)" : msg;
    }

    /// <summary>Difficulty name of the room song (Difficulty.getString: state.diffList[state.diff]).</summary>
    public static string CurrentDiffName()
    {
        var st = State; if (st == null) return "Normal";
        var diffs = st.Array("diffList")?.AsStrings();
        int di = (int)st.Num("diff");
        return diffs != null && di >= 0 && di < diffs.Count ? diffs[di] : DiffFromSong(st.Str("folder"), st.Str("song")) ?? "Normal";
    }
}
