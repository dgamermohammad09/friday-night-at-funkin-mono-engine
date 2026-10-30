using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using FNF_MonoGame.Engine.Colyseus;
using FNF_MonoGame.Engine.PsychEngine;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Psych Online skins (RoomState.LobbyCharacter.loadCharacter + PlayState.initPlayCharacter + GameClient skin options):
/// player.skin = [character, dadSuffix, bfSuffix, modFolder], player.skinURL = URL of the skin's mod.
/// - our own skin (Options > Psych Online > Skin) goes into the join options and answers "requestSkin" with setSkin.
/// - when another player's skin character is missing, its mod is downloaded + installed from skinURL (automatic for
///   trusted domains unless "Disable Automatic Downloads" is on; otherwise the lobby offers GET SKINS) and the lobby /
///   match then load the real character from that mod folder.
/// </summary>
public static partial class PsychOnline
{
    public static string[] OwnSkin
    {
        get
        {
            var dev = Environment.GetEnvironmentVariable("FNF_PSO_SKIN");   // dev: "char|dadSuffix|bfSuffix|modFolder" (doesn't touch the save)
            if (!string.IsNullOrEmpty(dev)) { var d = dev.Split('|'); if (d.Length >= 4) return d; }
            ClientPrefs.EnsureLoaded(); var s = ClientPrefs.Data.CurrentSkin; return s != null && s.Length >= 4 && !string.IsNullOrEmpty(s[0]) ? s : null;
        }
    }
    public static string OwnSkinUrl => OwnSkin == null ? null : NullIfEmpty(ReadModUrl(OwnSkin[3]));
    private static string NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Join options: skin + skinURL (GameClient.getOptions).</summary>
    public static void AddSkinOptions(JObject o)
    {
        var s = OwnSkin;
        if (s == null) return;
        o["skin"] = new JArray(s.Cast<object>().ToArray());
        if (OwnSkinUrl != null) o["skinURL"] = OwnSkinUrl;
    }

    /// <summary>"requestSkin" answer: setSkin [skin, url] or null.</summary>
    public static void SendSkin()
    {
        var s = OwnSkin;
        if (s == null) Send("setSkin", null);
        else Send("setSkin", new List<object> { new List<object>(s), OwnSkinUrl });
    }

    public static List<string> SkinOf(SchemaObject p)
    {
        var a = p?.Array("skin")?.AsStrings();
        if (a == null || a.Count == 0 || string.IsNullOrEmpty(a[0])) return null;
        if (State?.Bool("disableSkins") ?? false) return null;
        while (a.Count < 4) a.Add("");
        return a;
    }

    /// <summary>Psych Character.getCharacterFile lookup: mods/&lt;mod&gt;/characters, mods/characters, then the base game.</summary>
    public static bool CharacterExists(string name, string mod)
    {
        if (string.IsNullOrEmpty(name)) return false;
        string root = AssetManager.ContentRoot;
        if (global::FNF_MonoGame.Engine.FsCI.FileExists(Path.Combine(root, "data", "psych", "characters", name + ".json")) || global::FNF_MonoGame.Engine.FsCI.FileExists(Path.Combine(root, "data", "characters", name + ".json"))) return true;
        string bas = ModsBase;
        if (string.IsNullOrEmpty(bas)) return false;
        if (!string.IsNullOrEmpty(mod) && global::FNF_MonoGame.Engine.FsCI.FileExists(Path.Combine(bas, mod, "characters", name + ".json"))) return true;
        return global::FNF_MonoGame.Engine.FsCI.FileExists(Path.Combine(bas, "characters", name + ".json"));
    }

    /// <summary>PlayState.initPlayCharacter: skin character for the right (bf) or left side, or null for the chart's own.
    /// Solo play (no multiplayer session): your own skin (Skins menu / Options > Psych Online > Skin) replaces the player.</summary>
    public static (string name, string mod)? SkinFor(bool right, string songPlayer)
    {
        if (MultiplayerSession.Mode == MultiplayerSession.MpMode.None)
        {
            if (!right || Environment.GetEnvironmentVariable("FNF_SOLO_SKIN") == "0") return null;
            var own = OwnSkin;
            if (own == null) return null;
            if (!string.IsNullOrEmpty(songPlayer) && songPlayer.StartsWith(own[0], StringComparison.OrdinalIgnoreCase)) return null;
            if (songPlayer != null && songPlayer.EndsWith("-blazin")) return null;
            string on = CharacterExists(own[0] + own[2], own[3]) ? own[0] + own[2] : own[0];
            if (!CharacterExists(on, own[3])) return null;
            return (on, own[3]);
        }
        if (MultiplayerSession.Mode != MultiplayerSession.MpMode.Psych || State == null) return null;
        var (_, p) = FirstOnSide(right);
        var s = SkinOf(p);
        if (s == null) return null;
        if (!string.IsNullOrEmpty(songPlayer) && songPlayer.StartsWith(s[0], StringComparison.OrdinalIgnoreCase)) return null;
        string name = s[0] + s[right ? 2 : 1];
        if (songPlayer != null && songPlayer.EndsWith("-blazin") && !name.EndsWith("-blazin")) return null;
        if (!CharacterExists(name, s[3])) return null;
        return (name, s[3]);
    }

    // ------------------------------------------------------------------ skin list (Skins menu + Options > Psych Online > Skin)
    private static List<string[]> _skinList;
    private static readonly string[] SideSuffixes = { "-player", "-opponent", "-dad", "-bf", "-dead", "-pixel", "-christmas", "-car" };

    /// <summary>Selectable skins like PSO's SkinsState: every character in the Psych <c>characters</c> folder of an
    /// installed mod ([char, dadSuffix, bfSuffix, modFolder]; side variants &lt;name&gt;-opponent/-dad and -player/-bf are
    /// folded into their base), plus the base game's playable Pico. Cached until <paramref name="refresh"/>.</summary>
    public static List<string[]> AvailableSkins(bool refresh = false)
    {
        if (_skinList != null && !refresh) return _skinList;
        var l = new List<string[]>();
        if (global::FNF_MonoGame.Engine.FsCI.FileExists(Path.Combine(AssetManager.ContentRoot, "data", "characters", "pico-playable.json")))
            l.Add(new[] { "pico-playable", "", "", "" });
        try
        {
            string bas = PsychMods.ModsBasePath;
            if (!string.IsNullOrEmpty(bas) && global::FNF_MonoGame.Engine.FsCI.DirectoryExists(bas))
                foreach (var dir in global::FNF_MonoGame.Engine.FsCI.GetDirectories(bas).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    string cdir = Path.Combine(dir, "characters");
                    if (!global::FNF_MonoGame.Engine.FsCI.DirectoryExists(cdir)) continue;
                    var names = global::FNF_MonoGame.Engine.FsCI.GetFiles(cdir, "*.json").Select(f => Path.GetFileNameWithoutExtension(f)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    foreach (var n in names.OrderBy(x => x))
                    {
                        if (SideSuffixes.Any(s => n.EndsWith(s, StringComparison.OrdinalIgnoreCase))) continue;
                        if (!LooksLikeCharacter(Path.Combine(cdir, n + ".json"))) continue;
                        string dad = names.Contains(n + "-opponent") ? "-opponent" : names.Contains(n + "-dad") ? "-dad" : "";
                        string bf = names.Contains(n + "-player") ? "-player" : names.Contains(n + "-bf") ? "-bf" : "";
                        l.Add(new[] { n, dad, bf, Path.GetFileName(dir) });
                    }
                }
        }
        catch (Exception e) { Console.WriteLine("[SKINS] list: " + e.Message); }
        return _skinList = l;
    }

    /// <summary>A Psych character JSON needs an image and animations (skips stray / broken json files).</summary>
    private static bool LooksLikeCharacter(string json)
    {
        try
        {
            var o = JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(json));
            return !string.IsNullOrEmpty((string)o["image"]) && o["animations"] is JArray a && a.Count > 0;
        }
        catch { return false; }
    }

    public static string SkinLabel(string[] s) => s == null || s.Length < 4 || string.IsNullOrEmpty(s[0]) ? "None" : s[0] + (string.IsNullOrEmpty(s[3]) ? "" : " (" + s[3] + ")");

    /// <summary>Equip a skin (null = none): saved to psychsave.json and sent to the room when connected.</summary>
    public static void SetOwnSkin(string[] s)
    {
        ClientPrefs.EnsureLoaded();
        ClientPrefs.Data.CurrentSkin = s == null ? null : (string[])s.Clone();
        ClientPrefs.Save();
        Console.WriteLine("[SKINS] equipped " + SkinLabel(s));
        if (Connected) { try { SendSkin(); } catch { } }
    }

    /// <summary>True for characters the Psych way (mod characters / data/psych); V-Slice base characters (pico-playable)
    /// keep V-Slice positioning on the stage.</summary>
    public static bool IsPsychCharacter(string name, string mod)
    {
        if (string.IsNullOrEmpty(name)) return true;
        string root = AssetManager.ContentRoot;
        if (string.IsNullOrEmpty(mod) && !global::FNF_MonoGame.Engine.FsCI.FileExists(Path.Combine(root, "data", "psych", "characters", name + ".json"))
            && global::FNF_MonoGame.Engine.FsCI.FileExists(Path.Combine(root, "data", "characters", name + ".json"))) return false;
        return true;
    }

    // ------------------------------------------------------------------ skin downloads
    private static readonly Dictionary<string, Task<string>> _skinJobs = new();
    private static readonly HashSet<string> _skinDone = new();
    private static readonly HashSet<string> _skinLogged = new();
    public static int SkinsVersion { get; private set; }     // bumps when a skin mod got installed (lobby reloads characters)
    public static string SkinStatus = "";

    private static bool Trusted(string url)
    {
        ClientPrefs.EnsureLoaded();
        var list = new List<string>(ClientPrefs.Data.TrustedSources ?? new List<string>());
        var dev = Environment.GetEnvironmentVariable("FNF_PSO_TRUST");   // dev: extra trusted URL prefix (local test server)
        if (!string.IsNullOrEmpty(dev)) list.Add(dev);
        return url.StartsWith("https://gamebanana.com/") || list.Any(t => !string.IsNullOrEmpty(t) && url.StartsWith(t, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Players whose skin is missing here but downloadable: (name, skin, url).</summary>
    public static List<(string player, string skin, string url)> MissingSkins()
    {
        var l = new List<(string, string, string)>();
        foreach (var (sid, p) in AllPlayers())
        {
            if (sid == SessionId) continue;
            var s = SkinOf(p);
            if (s == null) continue;
            bool bf = p.Bool("bfSide");
            if (CharacterExists(s[0] + s[bf ? 2 : 1], s[3]) || CharacterExists(s[0], s[3])) continue;
            string url = NullIfEmpty(p.Str("skinURL"));
            if (_skinLogged.Add(sid + "|" + s[0])) Console.WriteLine($"[PSO] {p.Str("name")} uses skin {string.Join("|", s)} url={url ?? "(none)"}");
            if (url == null || !(url.StartsWith("http://") || url.StartsWith("https://"))) continue;
            l.Add((p.Str("name"), s[0] + (string.IsNullOrEmpty(s[3]) ? "" : " (" + s[3] + ")"), url));
        }
        return l;
    }

    /// <summary>Lobby tick: start automatic downloads (trusted + auto allowed), collect finished ones.</summary>
    public static void PollSkins(bool manual = false)
    {
        ClientPrefs.EnsureLoaded();
        foreach (var (player, skin, url) in MissingSkins())
        {
            if (_skinJobs.ContainsKey(url) || _skinDone.Contains(url)) continue;
            if (!manual && (ClientPrefs.Data.DisableAutoDownloads || !Trusted(url))) continue;
            SkinStatus = "Downloading " + player + "'s skin " + skin + " ...";
            Console.WriteLine($"[PSO] downloading skin {skin} of {player} from {url}");
            _skinJobs[url] = Task.Run(() => DownloadAndInstallMod(url));
        }
        foreach (var kv in _skinJobs.Where(k => k.Value.IsCompleted).ToList())
        {
            _skinJobs.Remove(kv.Key); _skinDone.Add(kv.Key);
            string r = kv.Value.Status == TaskStatus.RanToCompletion ? kv.Value.Result : "ERROR: " + kv.Value.Exception?.InnerException?.Message;
            if (r != null && r.StartsWith("ERROR")) { SkinStatus = "Skin download failed: " + r.Substring(6).Trim(); Console.WriteLine("[PSO] " + SkinStatus); }
            else
            {
                SkinStatus = "Installed skin mod " + r;
                Console.WriteLine("[PSO] installed skin mod '" + r + "' from " + kv.Key);
                try { PsychMods.ParseList(); } catch { }
                SkinsVersion++;
            }
        }
    }

    public static bool SkinDownloading => _skinJobs.Count > 0;
}
