using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using FNF_MonoGame.Engine.Colyseus;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Psych Online (Snirozu/Funkin-Psych-Online) session on a Funkin-Online-Server "room" (Colyseus 0.17, protocol 11).
/// Mirrors online/GameClient.hx: getOptions() handshake, ping/pong, checkChart -> verifyChart (MD5 of the raw chart),
/// gameStarted -> PlayState, and the in-game messages (noteHit / noteMiss / startSong / endSong ...).
/// Charts: the Psych Online base-game charts are shipped in Content/data/psychonline/&lt;folder&gt;/&lt;song&gt;.json (copied
/// unmodified from the PsychOnline release, so the MD5 matches the PC players' files and the note times are identical).
/// All callbacks run on the game thread (Poll() is called by the active scene every frame).
/// </summary>
public static partial class PsychOnline
{
    public const int Protocol = 11;    // Main.CLIENT_PROTOCOL
    public const string RoomName = "room";
    public static readonly string[] OfficialServers = { "wss://funkin.sniro.boo", "wss://peo-us-east.onrender.com", "wss://gettinfreaky.onrender.com" };

    public static ColyseusRoom Room;
    public static string Server = OfficialServers[0];
    public static string Nickname;
    public static string LastError;
    public static readonly List<string> Log = new List<string>();
    /// <summary>In-game messages (noteHit, noteMiss, startSong, endSong, strumPlay, charPlay) for PlayScene.</summary>
    public static readonly ConcurrentQueue<(string type, object msg)> PlayInbox = new ConcurrentQueue<(string, object)>();
    public static bool GameStartedPending;   // "gameStarted" received: the current scene should start the song
    public static bool EndSongPending;
    public static int StateVersion;

    /// <summary>Joined (JOIN_ROOM handshake done, state received) and the socket is open.</summary>
    public static bool Connected => Room != null && Room.Joined && Room.HasState && !Room.Closed;
    /// <summary>Seat reserved / socket open but the JOIN_ROOM handshake or first state is still pending.</summary>
    public static bool Joining => Room != null && !Room.Closed && !(Room.Joined && Room.HasState);
    public static SchemaObject State => Room?.State;
    public static string SessionId => Room?.SessionId;

    /// <summary>The player's own nickname (Options > Psych Online > Nickname / the online menu NAME row): kept for this
    /// session and written to psychsave.json, so it survives restarts. The Xbox gamertag is only the default while
    /// nothing was saved.</summary>
    public static void SetNickname(string v)
    {
        v = (v ?? "").Trim();
        if (v.Length < 3) return;
        if (v.Length > 14) v = v.Substring(0, 14);
        Nickname = v;
        FNF_MonoGame.Engine.PsychEngine.ClientPrefs.EnsureLoaded();
        FNF_MonoGame.Engine.PsychEngine.ClientPrefs.Data.Nickname = v;
        FNF_MonoGame.Engine.PsychEngine.ClientPrefs.Save();
        Console.WriteLine("[PSO] nickname saved: " + v);
    }

    public static string DefaultNickname()
    {
        string n = Nickname;
        if (string.IsNullOrWhiteSpace(n)) { FNF_MonoGame.Engine.PsychEngine.ClientPrefs.EnsureLoaded(); n = FNF_MonoGame.Engine.PsychEngine.ClientPrefs.Data.Nickname; }
        if (string.IsNullOrWhiteSpace(n)) n = Environment.GetEnvironmentVariable("FNF_PSO_NAME");
        if (string.IsNullOrWhiteSpace(n)) n = SystemKeyboard.AccountName;   // Xbox: the signed-in gamertag
        if (string.IsNullOrWhiteSpace(n)) n = MultiplayerSession.LocalName;
        n = new string(n.Where(c => c != '<' && c != '>' && c != '\r' && c != '\n' && c != '\t').ToArray()).Trim();
        if (n.Length > 14) n = n.Substring(0, 14);
        while (n.Length < 3) n += "_";
        return n;
    }

    // ------------------------------------------------------------------ state helpers
    public static SchemaMap Players => State?.Map("players");
    public static SchemaObject Self => Players != null && SessionId != null && Players.Items.TryGetValue(SessionId, out var p) ? p as SchemaObject : null;
    public static bool IsHost => State != null && State.Str("host") == SessionId;
    public static bool HasPerms => IsHost || (State?.Bool("anarchyMode") ?? false);
    public static bool SelfBfSide => Self?.Bool("bfSide") ?? true;
    /// <summary>PlayState.playsAsBF()</summary>
    public static bool PlaysAsBF => State != null && State.Bool("royalMode") ? !State.Bool("royalModeDadSide") : SelfBfSide;

    public static IEnumerable<(string sid, SchemaObject p)> AllPlayers()
    {
        var m = Players; if (m == null) yield break;
        foreach (var k in m.Order.ToList()) if (m.Items.TryGetValue(k, out var v) && v is SchemaObject so) yield return (k, so);
    }

    /// <summary>Side a player actually plays: One Lane Mode (royalMode) puts everybody on one side (PlayState.playsAsBF).</summary>
    public static bool EffectiveBfSide(SchemaObject p) => State != null && State.Bool("royalMode") ? !State.Bool("royalModeDadSide") : p?.Bool("bfSide") ?? true;
    /// <summary>First player of a side (bf = true: boyfriend side).</summary>
    public static (string sid, SchemaObject p) FirstOnSide(bool bf)
    {
        foreach (var x in AllPlayers()) if (EffectiveBfSide(x.p) == bf) return x;
        return (null, null);
    }

    // ------------------------------------------------------------------ connect
    public static JObject Options()
    {
        // ClientPrefs.getArrowRGBCompleteMaps(): [ { "4k": [[r,g,b] x4] }, { "4k": pixel colours } ] (FlxColor ints)
        JArray Cols(params uint[][] c) => new JArray(c.Select(x => new JArray(x.Select(v => (int)v))));
        var rgb = new JObject { ["4k"] = Cols(new[] { 0xFFC24B99u, 0xFFFFFFFFu, 0xFF3C1F56u }, new[] { 0xFF00FFFFu, 0xFFFFFFFFu, 0xFF1542B7u },
                                               new[] { 0xFF12FA05u, 0xFFFFFFFFu, 0xFF0A4447u }, new[] { 0xFFF9393Fu, 0xFFFFFFFFu, 0xFF651038u }) };
        var pix = new JObject { ["4k"] = Cols(new[] { 0xFFE276FFu, 0xFFFFF9FFu, 0xFF60008Du }, new[] { 0xFF3DCAFFu, 0xFFF4FFFFu, 0xFF003060u },
                                               new[] { 0xFF71E300u, 0xFFF6FFE6u, 0xFF003100u }, new[] { 0xFFFF884Eu, 0xFFFFFAF5u, 0xFF6C0000u }) };
        return new JObject
        {
            ["name"] = DefaultNickname(),
            ["protocol"] = Protocol,
            ["points"] = FunkinPoints,   // FunkinPoints.funkinPoints
            ["arrowRGB"] = new JArray(rgb, pix),
            ["gameplaySettings"] = new JObject(),
        };
    }

    /// <summary>getOptions(): + networkId/networkToken when logged in and connecting to the network server.</summary>
    private static JObject OptionsFor(string server)
    {
        var o = Options();
        AddSkinOptions(o);
        LoadAuth();
        if (LoggedIn && string.Equals(server?.TrimEnd('/'), NetworkServer, StringComparison.OrdinalIgnoreCase))
        {
            o["networkId"] = AuthId;
            o["networkToken"] = AuthToken;
        }
        return o;
    }

    /// <summary>Create a room (joinCode null) or join one by its 4-letter code. Returns null on success, else the error text.</summary>
    public static async Task<string> ConnectAsync(string server, string joinCode)
    {
        Disconnect();
        Server = server;
        try
        {
            var opts = OptionsFor(server);
            var room = string.IsNullOrEmpty(joinCode)
                ? await ColyseusRoom.Create(server, RoomName, opts).ConfigureAwait(false)
                : await ColyseusRoom.JoinById(server, joinCode.Trim().ToUpperInvariant(), opts).ConfigureAwait(false);
            Attach(room);
            return null;
        }
        catch (ColyseusException e) { LastError = $"{e.Code}: {e.Message}"; }
        catch (Exception e) { LastError = (e.InnerException ?? e).Message; }
        return LastError;
    }

    public static Task<JArray> ListRoomsAsync(string server) => ColyseusRoom.GetAvailableRooms(server, RoomName);

    private static void Attach(ColyseusRoom room, bool keepLog = false)
    {
        Room = room;
        if (!keepLog) Log.Clear();
        GameStartedPending = false; EndSongPending = false;
        while (PlayInbox.TryDequeue(out _)) { }
        MultiplayerSession.Mode = MultiplayerSession.MpMode.Psych;
        room.OnStateChange = () => StateVersion++;
        room.OnError = (c, m) => { LastError = $"{c}: {m}"; AddLog("ERROR " + LastError); };
        room.OnLeave = c => { AddLog("Disconnected (" + c + ")" + (room.LastError != null ? ": " + room.LastError : "")); };
        room.OnMessage = OnMessage;
    }

    public static void Disconnect()
    {
        var r = Room; Room = null;
        try { r?.Leave(true); } catch { }
        if (MultiplayerSession.Mode == MultiplayerSession.MpMode.Psych) MultiplayerSession.Mode = MultiplayerSession.MpMode.None;
    }

    public static void Poll() => Room?.Poll();

    public static void Send(string type, object payload = null) { if (Room != null && Room.Joined && !Room.Closed) Room.Send(type, payload); }

    public static void AddLog(string s)
    {
        Console.WriteLine("[PSO] " + s);
        Log.Add(s);
        LogSerial++;
        if (Log.Count > 40) Log.RemoveAt(0);
    }

    private static void OnMessage(string type, object msg)
    {
        switch (type)
        {
            case "ping": Send("pong"); break;
            case "log":
            {   // formatLog(): JSON {content, hue, date, isPM}
                string s = msg as string;
                try { var j = JObject.Parse(s); s = (string)j["content"]; } catch { }
                AddLog(s);
                ChatFeed.Enqueue(s);
                while (ChatFeed.Count > 20) ChatFeed.TryDequeue(out _);
                break;
            }
            case "alert":
                if (msg is string a) AddLog("! " + a);
                else if (msg is List<object> al && al.Count >= 2) AddLog("! " + al[0] + " " + al[1]);
                break;
            case "checkChart": VerifyChart(); break;
            case "requestSkin": SendSkin(); break;   // GameClient: setSkin [currentSkin, modURL] or null
            case "gameStarted": GameStartedPending = true; EndSongPending = false; while (PlayInbox.TryDequeue(out _)) { } AddLog("Game started: " + State?.Str("song")); break;
            case "endSong": EndSongPending = true; PlayInbox.Enqueue((type, msg)); break;
            case "charPlay": QueueLobbyAnim(msg); PlayInbox.Enqueue((type, msg)); break;
            case "startSong": case "noteHit": case "noteMiss": case "strumPlay": case "custom":
                PlayInbox.Enqueue((type, msg)); break;
        }
    }

    // ------------------------------------------------------------------ charts (Song.loadRawSong + Md5.encode)
    /// <summary>Content folder (same resolution as AssetManager: Xbox = LocalState\Content, else the package/exe folder).</summary>
    public static string ContentDir
    {
        get
        {
#if XBOX_UWP
            string local = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "Content");
            if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(local)) return local;
            return Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, "Content");
#else
            return System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot);
#endif
        }
    }
    public static string ChartRoot => Path.Combine(ContentDir, "data", "psychonline");

    /// <summary>Paths.formatToSongPath: lowercase, spaces -> '-', strip invalid chars.</summary>
    public static string FormatToSongPath(string s)
    {
        if (s == null) return "";
        var sb = new StringBuilder();
        foreach (char c in s.Replace(' ', '-').ToLowerInvariant())
            if ("~&\\;:<>#/\"'".IndexOf(c) < 0) sb.Append(c);
        return sb.ToString();
    }

    /// <summary>Song.loadRawSong path: mods/&lt;modDir&gt;/data/&lt;folder&gt;/&lt;song&gt;.json (Paths.modFolders) when the room uses a mod, else the shipped base charts.</summary>
    public static string ChartPath(string folder, string song, string modDir = null)
    {
        if (!string.IsNullOrEmpty(modDir) && !string.IsNullOrEmpty(ModsBase))
        {
            string rel = Path.Combine("data", FormatToSongPath(folder), FormatToSongPath(song) + ".json");
            string p = Path.Combine(ModsBase, modDir, rel);
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(p)) return p;
            string g = Path.Combine(ModsBase, rel);
            return global::FNF_MonoGame.Engine.FsCI.FileExists(g) ? g : p;
        }
        return Path.Combine(ChartRoot, FormatToSongPath(folder), FormatToSongPath(song) + ".json");
    }

    /// <summary>Md5.encode(Song.loadRawSong(song, folder)) or null when the chart is not available locally.</summary>
    public static string ChartHash(string folder, string song, string modDir = null)
    {
        try
        {
            string p = ChartPath(folder, song, modDir);
            if (!global::FNF_MonoGame.Engine.FsCI.FileExists(p)) return null;
            var b = global::FNF_MonoGame.Engine.FsCI.ReadAllBytes(p);
            // StringTools.trim: strip leading/trailing chars 9..13 and 32 (bytes = UTF-8, a BOM is NOT whitespace)
            int s = 0, e = b.Length;
            while (s < e && (b[s] == 32 || (b[s] > 8 && b[s] < 14))) s++;
            while (e > s && (b[e - 1] == 32 || (b[e - 1] > 8 && b[e - 1] < 14))) e--;
            return Md5.Hex(b, s, e - s);
        }
        catch (Exception ex) { Console.WriteLine("[PSO] hash failed: " + ex.Message); return null; }
    }

    public static void VerifyChart()
    {
        var st = State; if (st == null) return;
        string song = st.Str("song"), folder = st.Str("folder"), modDir = st.Str("modDir");
        if (string.IsNullOrEmpty(song)) return;
        string hash = ChartHash(folder, song, modDir) ?? "missing";
        Console.WriteLine($"[PSO] verifying song: {song} | {folder} | mod '{modDir}' : {hash}");
        Send("verifyChart", hash);
    }

    /// <summary>One selectable song: a Psych Online chart we also have the audio/stage for (base game or an installed mod).</summary>
    public sealed class SongEntry
    {
        public string Folder, Display, ModDir = "", ModURL = "";
        public List<string> Diffs = new List<string>();      // "Easy","Normal","Hard","Erect","Nightmare"
        public string FileFor(string diff) => diff.Equals("Normal", StringComparison.OrdinalIgnoreCase) ? FormatToSongPath(Folder) : FormatToSongPath(Folder) + "-" + FormatToSongPath(diff);
    }

    private static List<SongEntry> _songs;
    public static List<SongEntry> Songs() => _songs ??= ScanSongs();

    /// <summary>Engine song id (Content/songs/&lt;id&gt;) for a Psych Online folder (base difficulty); null when not available.</summary>
    public static string EngineSongFor(string folder) => Resolve(folder, FormatToSongPath(folder), null)?.Song;

    /// <summary>Difficulty id from the room song name ("bopeebo-hard" -> "hard", "bopeebo" -> "normal").</summary>
    public static string DiffFromSong(string folder, string song)
    {
        folder = FormatToSongPath(folder); song = FormatToSongPath(song);
        if (song.StartsWith(folder + "-")) return song.Substring(folder.Length + 1);
        return "normal";
    }

    /// <summary>Host: FreeplayState "setSong" = [folder, song, diffIndex, md5, modDir, modURL, Difficulty.list].</summary>
    public static bool PickSong(SongEntry e, int diffIdx)
    {
        string diff = e.Diffs[Math.Clamp(diffIdx, 0, e.Diffs.Count - 1)];
        string file = e.FileFor(diff);
        string hash = ChartHash(e.Folder, file, e.ModDir);
        if (hash == null) return false;
        var diffList = e.Diffs.Cast<object>().ToList();
        Send("setSong", new List<object> { FormatToSongPath(e.Folder), file, e.Diffs.IndexOf(diff), hash, e.ModDir ?? "", RoomUrlForMod(e.ModDir, e.ModURL), diffList });
        return true;
    }
}

/// <summary>Small MD5 (RFC 1321) so the UWP build does not depend on System.Security.Cryptography.MD5.</summary>
public static class Md5
{
    private static readonly int[] S = { 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20,
        4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21 };
    private static readonly uint[] K = Enumerable.Range(0, 64).Select(i => (uint)(long)Math.Floor(Math.Abs(Math.Sin(i + 1)) * 4294967296.0)).ToArray();

    public static string Hex(byte[] data, int offset, int count)
    {
        long bitLen = (long)count * 8;
        int padded = ((count + 8) / 64 + 1) * 64;
        var m = new byte[padded];
        System.Array.Copy(data, offset, m, 0, count);
        m[count] = 0x80;
        for (int i = 0; i < 8; i++) m[padded - 8 + i] = (byte)(bitLen >> (8 * i));
        uint a0 = 0x67452301, b0 = 0xefcdab89, c0 = 0x98badcfe, d0 = 0x10325476;
        var w = new uint[16];
        for (int ch = 0; ch < padded; ch += 64)
        {
            for (int i = 0; i < 16; i++) w[i] = BitConverter.ToUInt32(m, ch + i * 4);
            uint A = a0, B = b0, C = c0, D = d0;
            for (int i = 0; i < 64; i++)
            {
                uint F; int g;
                if (i < 16) { F = (B & C) | (~B & D); g = i; }
                else if (i < 32) { F = (D & B) | (~D & C); g = (5 * i + 1) % 16; }
                else if (i < 48) { F = B ^ C ^ D; g = (3 * i + 5) % 16; }
                else { F = C ^ (B | ~D); g = (7 * i) % 16; }
                F = F + A + K[i] + w[g];
                A = D; D = C; C = B;
                B = B + ((F << S[i]) | (F >> (32 - S[i])));
            }
            a0 += A; b0 += B; c0 += C; d0 += D;
        }
        var sb = new StringBuilder(32);
        foreach (var v in new[] { a0, b0, c0, d0 }) for (int i = 0; i < 4; i++) sb.Append(((byte)(v >> (8 * i))).ToString("x2"));
        return sb.ToString();
    }
}
