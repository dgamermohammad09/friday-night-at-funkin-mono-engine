using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using FNF_MonoGame.Engine.Colyseus;

namespace FNF_MonoGame.Engine;

// Psych Online extras: song/variation resolution (erect, nightmare, pico mixes, bf mixes, mod songs), the mod downloader
// (port of online/mods/OnlineMods.hx + GameBanana.hx, ZIP archives), chat, skins and the Funkin Network account login
// (online/network/FunkinNetwork.hx: e-mail + one-time code, no browser needed).
public static partial class PsychOnline
{
    // ------------------------------------------------------------------ song resolution
    public sealed class ResolvedSong { public string Song, Diff, Char, ModPath; }

    public static string ModsBase => FNF_MonoGame.Engine.PsychEngine.PsychMods.ModsBasePath;

    /// <summary>Installed mod folder (null when missing). Psych Online's modDir is the folder name under mods/.</summary>
    public static string ModPath(string modDir)
    {
        if (string.IsNullOrWhiteSpace(modDir) || string.IsNullOrEmpty(ModsBase)) return null;
        string p = Path.Combine(ModsBase, modDir);
        return global::FNF_MonoGame.Engine.FsCI.DirectoryExists(p) ? p : null;
    }

    private static bool HasVariationFolder(string id) =>
        global::FNF_MonoGame.Engine.FsCI.DirectoryExists(Path.Combine(ContentDir, "songs", id, "charts"));

    private static string VariationFolder(string baseFolder, string engineBase, string variation)
    {
        foreach (var b in new[] { baseFolder.Replace('-', '_'), engineBase.Replace('-', '_'), engineBase, baseFolder.Replace("-", "") })
            if (HasVariationFolder(b + "_" + variation)) return b + "_" + variation;
        return null;
    }

    /// <summary>
    /// Room song -> what the engine loads. PSO folders: "&lt;song&gt;" (diffs Easy/Normal/Hard + Erect/Nightmare = "-erect" track,
    /// PlayState.isErect), "&lt;song&gt;-pico" (Pico Mix, FreeplayState.getMixSuffix), "&lt;song&gt;-bf" (weekend 1 BF mix), or a mod folder.
    /// The notes always come from the room's Psych chart (PsoOverrideChart); this picks stage/characters/audio.
    /// </summary>
    public static ResolvedSong Resolve(string folder, string song, string modDir)
    {
        string diff = DiffFromSong(folder, song);
        if (!string.IsNullOrEmpty(modDir))
        {
            string mp = ModPath(modDir);
            if (mp == null || !global::FNF_MonoGame.Engine.FsCI.FileExists(ChartPath(folder, song, modDir))) return null;
            return new ResolvedSong { Song = FormatToSongPath(folder), Diff = diff, Char = "bf", ModPath = mp };
        }
        string f = FormatToSongPath(folder);
        if (f == "test") return null;
        string variation = null, baseF = f;
        if (f.EndsWith("-pico")) { variation = "pico"; baseF = f.Substring(0, f.Length - 5); }
        else if (f.EndsWith("-bf")) { variation = "bf"; baseF = f.Substring(0, f.Length - 3); }
        string eng = BaseEngineSong(baseF);
        if (eng == null) return null;
        if (diff == "erect" || diff == "nightmare")
        {
            string v = VariationFolder(baseF, eng, "erect");
            return v == null ? null : new ResolvedSong { Song = v, Diff = diff, Char = "bf" };
        }
        if (variation != null)
        {
            string v = VariationFolder(baseF, eng, variation);
            return v == null ? null : new ResolvedSong { Song = v, Diff = diff, Char = variation };
        }
        return new ResolvedSong { Song = eng, Diff = diff, Char = "bf" };
    }

    /// <summary>Engine song folder with the audio (Content/songs/&lt;id&gt;/Inst.ogg) for a base PSO folder.</summary>
    public static string BaseEngineSong(string folder)
    {
        folder = FormatToSongPath(folder);
        string songs = Path.Combine(ContentDir, "songs");
        foreach (var c in new[] { folder, folder.Replace('-', '_'), folder.Replace("-", "") })
            if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(Path.Combine(songs, c)) && global::FNF_MonoGame.Engine.FsCI.FileExists(Path.Combine(songs, c, "Inst.ogg"))) return c;
        return null;
    }

    private static readonly string[] AllDiffs = { "Easy", "Normal", "Hard", "Erect", "Nightmare" };

    private static List<SongEntry> ScanSongs()
    {
        var list = new List<SongEntry>();
        if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(ChartRoot))
            foreach (var dir in global::FNF_MonoGame.Engine.FsCI.GetDirectories(ChartRoot).OrderBy(d => d))
            {
                string folder = Path.GetFileName(dir);
                var e = new SongEntry { Folder = folder };
                foreach (var d in AllDiffs)
                    if (global::FNF_MonoGame.Engine.FsCI.FileExists(ChartPath(folder, e.FileFor(d))) && Resolve(folder, e.FileFor(d), null) != null) e.Diffs.Add(d);
                if (e.Diffs.Count == 0) continue;
                e.Display = ReadSongName(ChartPath(folder, e.FileFor(e.Diffs.Contains("Normal") ? "Normal" : e.Diffs[0]))) ?? folder;
                if (folder.EndsWith("-pico")) e.Display += " (Pico Mix)";
                else if (folder.EndsWith("-bf")) e.Display += " (BF Mix)";
                list.Add(e);
            }
        // installed Psych mods: data/<song>/<song>[-diff].json (Mods.currentModDirectory = the mod folder)
        try
        {
            if (!string.IsNullOrEmpty(ModsBase) && global::FNF_MonoGame.Engine.FsCI.DirectoryExists(ModsBase))
                foreach (var modDir in global::FNF_MonoGame.Engine.FsCI.GetDirectories(ModsBase).OrderBy(d => d))
                {
                    string mod = Path.GetFileName(modDir);
                    string data = Path.Combine(modDir, "data");
                    if (!global::FNF_MonoGame.Engine.FsCI.DirectoryExists(data)) continue;
                    string url = ReadModUrl(mod);
                    foreach (var sdir in global::FNF_MonoGame.Engine.FsCI.GetDirectories(data).OrderBy(d => d))
                    {
                        string folder = Path.GetFileName(sdir);
                        var e = new SongEntry { Folder = folder, ModDir = mod, ModURL = url ?? "" };
                        foreach (var file in global::FNF_MonoGame.Engine.FsCI.GetFiles(sdir, "*.json"))
                        {
                            string n = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                            string f = FormatToSongPath(folder);
                            if (n == f) e.Diffs.Insert(0, "Normal");
                            else if (n.StartsWith(f + "-") && !n.StartsWith(f + "-events")) e.Diffs.Add(Capitalize(n.Substring(f.Length + 1)));
                        }
                        if (e.Diffs.Count == 0) continue;
                        e.Diffs = e.Diffs.Distinct().OrderBy(d => System.Array.IndexOf(AllDiffs, d) < 0 ? 99 : System.Array.IndexOf(AllDiffs, d)).ToList();
                        e.Display = (ReadSongName(ChartPath(folder, e.FileFor(e.Diffs[0]), mod)) ?? folder) + " [" + (PackName(mod) ?? mod) + "]";
                        list.Add(e);
                    }
                }
        }
        catch (Exception ex) { Console.WriteLine("[PSO] mod song scan failed: " + ex.Message); }
        return list;
    }

    private static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

    private static string ReadSongName(string path)
    {
        try
        {
            var t = JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(path))["song"];   // Psych 0.x: {"song": {"song": name}}, Psych 1.0 unwrapped: {"song": name}
            string n = t is JObject o ? (string)o["song"] : t?.Type == JTokenType.String ? (string)t : null;
            return string.IsNullOrWhiteSpace(n) ? null : n;
        }
        catch { return null; }
    }

    /// <summary>pack.json "name" of an installed mod (the readable name for the song list; folder names can be ids / URLs).</summary>
    public static string PackName(string mod)
    {
        try
        {
            string p = Path.Combine(ModsBase, mod, "pack.json");
            if (!global::FNF_MonoGame.Engine.FsCI.FileExists(p)) return null;
            string n = (string)JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(p))["name"];
            return string.IsNullOrWhiteSpace(n) ? null : n.Trim();
        }
        catch { return null; }
    }

    public static void InvalidateSongs() => _songs = null;

    // ------------------------------------------------------------------ mods (OnlineMods.downloadMod / installMod)
    public static string ReadModUrl(string mod)
    {
        try
        {
            string p = Path.Combine(ModsBase, mod, "mod_url.txt");
            if (!global::FNF_MonoGame.Engine.FsCI.FileExists(p)) return null;
            string s = global::FNF_MonoGame.Engine.FsCI.ReadAllText(p).Trim();
            return s.Length == 0 ? null : s;
        }
        catch { return null; }
    }

    /// <summary>The room song needs a mod that is not installed here.</summary>
    public static bool RoomNeedsMod
    {
        get
        {
            var st = State; if (st == null) return false;
            string md = st.Str("modDir");
            return !string.IsNullOrEmpty(md) && ModPath(md) == null;
        }
    }
    public static string RoomModUrl => State?.Str("modURL");

    public static Task<string> ModTask;      // running download (result = installed mod folder name, or "ERROR: ...")
    public static string ModStatus = "";

    /// <summary>Room mod download (the lobby's one-tap "DOWNLOAD MOD"): GameBanana.InstallFromUrl with the room's modDir.</summary>
    public static GameBanana.Job ModJob;
    public static string ModJobUrl;

    public static void StartModDownload(string url)
    {
        if (ModTask != null && !ModTask.IsCompleted) return;
        string want = State?.Str("modDir");
        var job = GameBanana.InstallFromUrl(url, string.IsNullOrEmpty(want) ? null : want);
        ModJob = job; ModJobUrl = url;
        ModStatus = "Downloading " + url + " ...";
        ModTask = job.Task.ContinueWith(_ => job.Error != null ? "ERROR: " + job.Error : job.Result);
    }

    private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>Downloads + installs a mod URL like Psych Online's OnlineMods.downloadMod (GameBanana page, Google Drive, MediaFire,
    /// Dropbox, Pixeldrain, GitHub or any direct link; ZIP / 7Z / RAR / TAR). Returns the mod folder or "ERROR: ...".</summary>
    public static async Task<string> DownloadAndInstallMod(string url)
    {
        var job = GameBanana.InstallFromUrl(url, null);
        try { await job.Task.ConfigureAwait(false); } catch { }
        ModStatus = job.Status;
        return job.Error != null ? "ERROR: " + job.Error : job.Result;
    }

    private static readonly string[] IgnoreModFolders = { "characters", "custom_events", "custom_notetypes", "data", "songs", "music", "sounds",
        "shaders", "videos", "images", "stages", "weeks", "fonts", "scripts", "achievements", "lumod" };

    /// <summary>FileUtils.formatFile: drop / \ ? * : | " &lt; &gt; and '.'.</summary>
    public static string FormatFile(string s) => new string((s ?? "").Where(c => "/\\?*:|\"<>.".IndexOf(c) < 0).ToArray());

    /// <summary>OnlineMods.installMod (ZIP): find the mod root inside the archive, extract to mods/&lt;name&gt;, normalise the layout.</summary>
    public static string InstallModArchive(string archive, string modUrl, string fallbackName, string packName)
    {
        ModStatus = "Installing ...";
        ZipArchive zip;
        try { zip = ZipFile.OpenRead(archive); }
        catch (Exception e) { return "ERROR: not a ZIP archive (RAR/7z are not supported here): " + e.Message; }
        using (zip)
        {
            string beginFolder = null, modName = null; bool exe = false;
            foreach (var en in zip.Entries)
            {
                string fn = en.FullName.Replace('\\', '/');
                if (fn.EndsWith(".exe")) exe = true;
                if (beginFolder != null) continue;
                var forFiles = new List<string>();
                foreach (var part in fn.Split('/'))
                {
                    if (part == "shared" || part == "mods") break;
                    if (part == "assets" || IgnoreModFolders.Contains(part))
                    {
                        modName = forFiles.Count > 0 ? forFiles[forFiles.Count - 1] : null;
                        if (string.IsNullOrWhiteSpace(modName) || modName == "bin" || modName == "PsychEngine") modName = fallbackName;
                        modName = FormatFile(modName);
                        beginFolder = forFiles.Count > 0 ? string.Join("/", forFiles) + "/" : "";
                        break;
                    }
                    forFiles.Add(part);
                }
            }
            if (beginFolder == null || string.IsNullOrWhiteSpace(modName)) return "ERROR: mod data not found inside the archive";
            string root = Path.Combine(ModsBase, modName);
            if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(root)) Directory.Delete(root, true);
            Directory.CreateDirectory(root);
            string rootFull = Path.GetFullPath(root);
            foreach (var en in zip.Entries)
            {
                string fn = en.FullName.Replace('\\', '/');
                if (!fn.StartsWith(beginFolder) || fn.EndsWith("/")) continue;
                string rel = fn.Substring(beginFolder.Length);
                string dest = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
                if (!dest.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) continue;   // zip-slip guard
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                en.ExtractToFile(dest, true);
            }
            if (exe)   // executable releases: keep only assets/ + mods/
                foreach (var e in Directory.GetFileSystemEntries(root))
                {
                    string n = Path.GetFileName(e);
                    if (n == "assets" || n == "mods") continue;
                    if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(e)) Directory.Delete(e, true); else File.Delete(e);
                }
            string P(string r) => Path.Combine(root, r.Replace('/', Path.DirectorySeparatorChar));
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(P("mods/pack.json"))) File.Delete(P("mods/pack.json"));
            if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(P("mods")))
            {
                foreach (var d in global::FNF_MonoGame.Engine.FsCI.GetDirectories(P("mods")))
                    if (!IgnoreModFolders.Contains(Path.GetFileName(d))) Cut(d, root);
                Cut(P("mods"), root);
            }
            if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(P("assets"))) Cut(P("assets"), root);
            if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(P("shared"))) Cut(P("shared"), root);
            if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(P("data/songData"))) Cut(P("data/songData"), P("data"));
            foreach (var f in new[] { "images/alphabet.png", "images/alphabet.xml", "images/healthBar.png", "images/timeBar.png", "weeks/weekList.txt" })
                if (global::FNF_MonoGame.Engine.FsCI.FileExists(P(f))) File.Delete(P(f));
            if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(P("songs")))   // forever-engine layout: songs/<song>/*.json -> data/<song>/
                foreach (var f in global::FNF_MonoGame.Engine.FsCI.GetFiles(P("songs"), "*.json", SearchOption.AllDirectories))
                {
                    string sd = Path.GetFileName(Path.GetDirectoryName(f));
                    string to = Path.Combine(P("data"), sd, Path.GetFileName(f));
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    if (global::FNF_MonoGame.Engine.FsCI.FileExists(to)) File.Delete(to);
                    File.Move(f, to);
                }
            if (!global::FNF_MonoGame.Engine.FsCI.FileExists(P("pack.json")))
                File.WriteAllText(P("pack.json"), new JObject { ["name"] = packName ?? modName, ["description"] = "", ["runsGlobally"] = false }.ToString());
            File.WriteAllText(P("mod_url.txt"), modUrl ?? "");
            try { FNF_MonoGame.Engine.PsychEngine.PsychMods.ParseList(); } catch { }
            InvalidateSongs();
            ModStatus = "Installed mod: " + modName;
            Console.WriteLine($"[PSO] installed mod '{modName}' from {modUrl} (archive root '{beginFolder}')");
            return modName;
        }
    }

    /// <summary>FileUtils.cut: move the contents of src into dst (merging), then delete src.</summary>
    private static void Cut(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in global::FNF_MonoGame.Engine.FsCI.GetFiles(src))
        {
            string to = Path.Combine(dst, Path.GetFileName(f));
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(to)) File.Delete(to);
            File.Move(f, to);
        }
        foreach (var d in global::FNF_MonoGame.Engine.FsCI.GetDirectories(src)) Cut(d, Path.Combine(dst, Path.GetFileName(d)));
        try { Directory.Delete(src, true); } catch { }
    }

    // ------------------------------------------------------------------ chat + skins
    /// <summary>Lobby/in-game chat ("chat" message; the server broadcasts it back to everyone as a "log" line "name: text").</summary>
    public static void SendChat(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (text.TrimStart().StartsWith("/")) { SendChatOrCommand(text); return; }   // ChatBox: "/x" -> "command"
        if (text.Length > 299) text = text.Substring(0, 299);
        Send("chat", text);
    }
    public static readonly string[] ChatPresets = { "Hi!", "GG!", "Good luck, have fun!", "Ready?", "One more?", "Nice!", "Let's go!", "brb", "Playing from an Xbox!" };
    /// <summary>Log/chat lines received since the last poll (PlayScene shows them during a match).</summary>
    public static readonly ConcurrentQueue<string> ChatFeed = new ConcurrentQueue<string>();

    /// <summary>Player.skin = [character, dadSuffix, bfSuffix, modFolder] (SkinsState / RoomState.hx); null when none.</summary>
    public static string SkinName(SchemaObject p)
    {
        var a = p?.Array("skin")?.AsStrings();
        if (a == null || a.Count == 0 || string.IsNullOrEmpty(a[0])) return null;
        return a.Count > 3 && !string.IsNullOrEmpty(a[3]) ? $"{a[0]} ({a[3]})" : a[0];
    }

    // ------------------------------------------------------------------ Funkin Network account (FunkinNetwork.hx / Auth.hx)
    public static string AuthId, AuthToken, AuthName;
    public static double AuthPoints;
    private static bool _authLoaded;

    private static string AuthFile
    {
        get
        {
#if XBOX_UWP
            return Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "psychonline_auth.json");
#else
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "psychonline_auth.json");
#endif
        }
    }

    public static void LoadAuth()
    {
        if (_authLoaded) return;
        _authLoaded = true;
        try
        {
            if (!global::FNF_MonoGame.Engine.FsCI.FileExists(AuthFile)) return;
            var j = JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(AuthFile));
            AuthId = (string)j["id"]; AuthToken = (string)j["token"]; AuthName = (string)j["name"];
        }
        catch { }
    }

    private static void SaveAuth()
    {
        try
        {
            if (AuthId == null) { if (global::FNF_MonoGame.Engine.FsCI.FileExists(AuthFile)) File.Delete(AuthFile); return; }
            File.WriteAllText(AuthFile, new JObject { ["id"] = AuthId, ["token"] = AuthToken, ["name"] = AuthName }.ToString());
        }
        catch (Exception e) { Console.WriteLine("[PSO] auth save failed: " + e.Message); }
    }

    public static bool LoggedIn => AuthId != null && AuthToken != null;
    /// <summary>GameClient.getOptions only sends the credentials to the network server (default: the first official server).</summary>
    public static string NetworkServer = OfficialServers[0];
    public static string NetworkHttp => ColyseusRoom.HttpBase(NetworkServer);

    /// <summary>FunkinNetwork.requestLogin(email, code): without a code the server mails a one-time code; with it, returns {id, token}.</summary>
    public static async Task<string> RequestLoginAsync(string email, string code)
    {
        try
        {
            var body = new JObject { ["email"] = email?.Trim() };
            if (!string.IsNullOrEmpty(code)) body["code"] = code.Trim().ToUpperInvariant();
            var resp = await _http.PostAsync(NetworkHttp + "/api/auth/login", new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")).ConfigureAwait(false);
            string txt = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            Console.WriteLine($"[PSO] /api/auth/login ({(code == null ? "request code" : "verify")}) -> {(int)resp.StatusCode} {txt.Substring(0, Math.Min(120, txt.Length))}");
            if (!resp.IsSuccessStatusCode) return "ERROR: " + (int)resp.StatusCode + " " + txt;
            if (string.IsNullOrEmpty(code)) return null;
            JObject j;
            try { j = JObject.Parse(txt); } catch { return "ERROR: wrong code or e-mail not registered"; }
            AuthId = (string)j["id"]; AuthToken = (string)j["token"];
            if (AuthId == null || AuthToken == null) return "ERROR: unexpected response";
            SaveAuth();
            return await PingAccountAsync().ConfigureAwait(false);
        }
        catch (Exception e) { return "ERROR: " + (e.InnerException ?? e).Message; }
    }

    /// <summary>FunkinNetwork.ping: GET /api/account/me with Basic id:token.</summary>
    public static async Task<string> PingAccountAsync()
    {
        try
        {
            if (!LoggedIn) return "ERROR: not logged in";
            var req = new HttpRequestMessage(HttpMethod.Get, NetworkHttp + "/api/account/me");
            req.Headers.TryAddWithoutValidation("Authorization", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(AuthId + ":" + AuthToken)));
            var resp = await _http.SendAsync(req).ConfigureAwait(false);
            string txt = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return "ERROR: " + (int)resp.StatusCode + " " + txt;
            var j = JObject.Parse(txt);
            AuthName = (string)j["name"]; AuthPoints = (double?)j["points"] ?? 0;
            SaveAuth();
            return null;
        }
        catch (Exception e) { return "ERROR: " + (e.InnerException ?? e).Message; }
    }

    public static void Logout() { AuthId = AuthToken = AuthName = null; AuthPoints = 0; SaveAuth(); }
}
