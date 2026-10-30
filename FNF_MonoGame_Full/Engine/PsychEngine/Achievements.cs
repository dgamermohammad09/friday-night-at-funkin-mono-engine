using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine.PsychEngine.Objects;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine Achievements system.
/// Direct port of backend/Achievements.hx.
/// Xbox compatible: saves to ApplicationData.LocalFolder on Xbox.
/// </summary>
public static class Achievements
{
    public class Achievement
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("description")] public string Description { get; set; } = "";
        [JsonPropertyName("hidden")] public bool Hidden { get; set; } = false;
        [JsonPropertyName("maxScore")] public float MaxScore { get; set; } = 0;
        [JsonPropertyName("maxDecimals")] public int MaxDecimals { get; set; } = 0;
        [JsonPropertyName("mod")] public string Mod { get; set; } = "";
        [JsonPropertyName("ID")] public int ID { get; set; } = 0;
    }

    public static Dictionary<string, Achievement> AllAchievements { get; } = new(StringComparer.OrdinalIgnoreCase);
    public static Dictionary<string, float> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public static List<string> Unlocked { get; set; } = new();

    private static int _sortID = 0;
    private static bool _initialized = false;

    public static void Init()
    {
        if (_initialized) return;
        _initialized = true;
        _sortID = 0;

        Create("friday_night_play", "Freaky on a Friday Night", "Play on a Friday... Night.", hidden: true);
        Create("week1_nomiss", "She Calls Me Daddy Too", "Beat Week 1 on Hard with no Misses.");
        Create("week2_nomiss", "No More Tricks", "Beat Week 2 on Hard with no Misses.");
        Create("week3_nomiss", "Call Me The Hitman", "Beat Week 3 on Hard with no Misses.");
        Create("week4_nomiss", "Lady Killer", "Beat Week 4 on Hard with no Misses.");
        Create("week5_nomiss", "Missless Christmas", "Beat Week 5 on Hard with no Misses.");
        Create("week6_nomiss", "Highscore!!", "Beat Week 6 on Hard with no Misses.");
        Create("week7_nomiss", "God Effing Damn It!", "Beat Week 7 on Hard with no Misses.");
        Create("weekend1_nomiss", "Just a Friendly Sparring", "Beat Weekend 1 on Hard with no Misses.");
        Create("ur_bad", "What a Funkin' Disaster!", "Complete a Song with a rating lower than 20%.");
        Create("ur_good", "Perfectionist", "Complete a Song with a rating of 100%.");
        Create("roadkill_enthusiast", "Roadkill Enthusiast", "Watch the Henchmen die 50 times.", maxScore: 50);
        Create("oversinging", "Oversinging Much...?", "Sing for 10 seconds without going back to Idle.");
        Create("hype", "Hyperactive", "Finish a Song without going back to Idle.");
        Create("two_keys", "Just the Two of Us", "Finish a Song pressing only two keys.");
        Create("toastie", "Toaster Gamer", "Have you tried to run the game on a toaster?");
        Create("debugger", "Debugger", "Beat the \"Test\" Stage from the Chart Editor.", hidden: true);
        _originalLength = _sortID;
    }

    private static void Create(string id, string name, string description, bool hidden = false, float maxScore = 0, int maxDecimals = 0)
    {
        AllAchievements[id] = new Achievement
        {
            Name = name,
            Description = description,
            Hidden = hidden,
            MaxScore = maxScore,
            MaxDecimals = maxDecimals,
            ID = _sortID++
        };
    }

    public static bool IsUnlocked(string name) => Unlocked.Contains(name);

    /// <summary>
    /// Unlock an achievement. Matches Psych's `Achievements.unlock(name, autoStartPopup=true)`.
    /// When <paramref name="autoStartPopup"/> is true and the unlock actually happened,
    /// a popup is queued automatically.
    /// </summary>
    public static bool Unlock(string name, bool autoStartPopup = true)
    {
        if (Unlocked.Contains(name)) return false;
        if (!AllAchievements.ContainsKey(name)) return false;
        Unlocked.Add(name);
        Save();
        Console.WriteLine($"[ACHIEVEMENTS] Unlocked: {AllAchievements[name].Name}");
        if (autoStartPopup) StartPopup(name);
        return true;
    }

    // ---------------------------------------------------------------------
    //  Popup queue
    //  Direct port of `Achievements._popups`, `showingPopups`, and
    //  `startPopup()` from backend/Achievements.hx. AchievementPopup accesses
    //  this list via friend semantics in Psych; here the manager owns
    //  the lifecycle and popups just flag themselves destroyed.
    // ---------------------------------------------------------------------

    private static readonly List<AchievementPopup> _popups = new();

    /// <summary>True if any achievement popup is currently on screen.</summary>
    public static bool ShowingPopups => _popups.Count > 0;

    /// <summary>
    /// Queue an achievement popup. Matches Psych's `startPopup(achieve, endFunc)`:
    /// existing popups are shifted down by 150 px so the newest sits on top.
    /// </summary>
    public static void StartPopup(string achievement, Action onFinish = null)
    {
        if (string.IsNullOrEmpty(achievement)) return;

        // Bump existing popups down so the new one lands at the top slot.
        // (Matches AchievementPopup.hx L190: `popup.intendedY += 150;`)
        foreach (var p in _popups)
        {
            if (p == null || p.Destroyed) continue;
            p.IntendedY += 150f;
        }

        var popup = new AchievementPopup(achievement, onFinish);
        _popups.Add(popup);
    }

    /// <summary>Tick every live popup and drop destroyed ones.</summary>
    public static void UpdatePopups(float elapsed)
    {
        if (_popups.Count == 0) return;
        for (int i = _popups.Count - 1; i >= 0; i--)
        {
            var p = _popups[i];
            if (p == null) { _popups.RemoveAt(i); continue; }
            p.Update(elapsed);
            if (p.Destroyed) _popups.RemoveAt(i);
        }
    }

    /// <summary>Draw every live popup. Call inside an active <see cref="SpriteBatch"/>.</summary>
    public static void DrawPopups(SpriteBatch spriteBatch)
    {
        if (_popups.Count == 0) return;
        foreach (var p in _popups)
        {
            if (p == null || p.Destroyed) continue;
            p.Draw(spriteBatch);
        }
    }

    public static float GetScore(string name)
    {
        return Variables.TryGetValue(name, out var val) ? val : 0f;
    }

    public static float SetScore(string name, float value, bool saveIfNotUnlocked = true)
    {
        if (!AllAchievements.TryGetValue(name, out var achievement)) return 0f;

        Variables[name] = value;

        if (achievement.MaxScore > 0 && value >= achievement.MaxScore)
        {
            Variables[name] = achievement.MaxScore;
            Unlock(name);
        }

        if (saveIfNotUnlocked) Save();
        return Variables[name];
    }

    public static float AddScore(string name, float value = 1, bool saveIfNotUnlocked = true)
    {
        float current = GetScore(name);
        return SetScore(name, current + value, saveIfNotUnlocked);
    }

    private static string SavePath
    {
        get
        {
#if XBOX_UWP
            return Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "achievements.json");
#else
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "PsychEngine");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "achievements.json");
#endif
        }
    }

    public static void Save()
    {
        try
        {
            var data = new AchievementSaveData { Unlocked = Unlocked, Variables = Variables };
            string json = FNF_MonoGame.Engine.JsonCompat.Serialize(data);
            File.WriteAllText(SavePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ACHIEVEMENTS] Save failed: {ex.Message}");
        }
    }

    public static void Load()
    {
        Init();
        try
        {
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(SavePath))
            {
                string json = global::FNF_MonoGame.Engine.FsCI.ReadAllText(SavePath);
                var data = FNF_MonoGame.Engine.JsonCompat.Deserialize<AchievementSaveData>(json);
                if (data != null)
                {
                    Unlocked = data.Unlocked ?? new List<string>();
                    Variables = data.Variables ?? new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ACHIEVEMENTS] Load failed: {ex.Message}");
        }
    }

    private class AchievementSaveData
    {
        [JsonPropertyName("unlocked")] public List<string> Unlocked { get; set; } = new();
        [JsonPropertyName("variables")] public Dictionary<string, float> Variables { get; set; } = new();
    }

    /// <summary>
    /// Achievements.reloadList (MODS_ALLOWED): drop previously loaded mod achievements, then read
    /// mods/data/achievements.json and mods/&lt;mod&gt;/data/achievements.json for every enabled mod (modsList order).
    /// Each entry needs a "save" key (the achievement id); ids that already exist are skipped; ID continues the
    /// built-in sort order and "mod" remembers the folder (icons resolve from it).
    /// </summary>
    public static void LoadModAchievements()
    {
        Init();
        foreach (var k in AllAchievements.Where(kv => !string.IsNullOrEmpty(kv.Value.Mod) || kv.Value.ID >= _originalLength).Select(kv => kv.Key).ToList())
            AllAchievements.Remove(k);
        _sortID = _originalLength;
        string modsBase = PsychMods.ModsBasePath;
        if (string.IsNullOrEmpty(modsBase)) modsBase = PsychPaths.Mods();
        if (string.IsNullOrEmpty(modsBase)) return;
        LoadAchievementFile(Path.Combine(modsBase, "data", "achievements.json"), null);
        foreach (var mod in PsychMods.GetAllMods().Where(m => m.Enabled))
        {
            string modDir = PsychMods.GetModDirectory(mod.Name);
            if (!string.IsNullOrEmpty(modDir))
                LoadAchievementFile(Path.Combine(modDir, "data", "achievements.json"), mod.Name);
        }
    }

    private static int _originalLength = -1;

    private static void LoadAchievementFile(string path, string mod)
    {
        if (!global::FNF_MonoGame.Engine.FsCI.FileExists(path)) return;
        try
        {
            string raw = global::FNF_MonoGame.Engine.FsCI.ReadAllText(path).Trim();
            if (raw.Length == 0) return;
            var arr = Newtonsoft.Json.Linq.JArray.Parse(PsychMods.SanitizeJson(raw));
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is not Newtonsoft.Json.Linq.JObject a) { Console.WriteLine($"[ACHIEVEMENTS] {mod ?? "None"}: Achievement #{i + 1} is invalid."); continue; }
                string key = (string)a["save"];
                if (key == null || key.Trim().Length < 1) { Console.WriteLine($"[ACHIEVEMENTS] Error on Achievement {(string)a["name"]}: Missing valid \"save\" value."); continue; }
                key = key.Trim();
                if (AllAchievements.ContainsKey(key)) continue;
                AllAchievements[key] = new Achievement
                {
                    Name = (string)a["name"] ?? "",
                    Description = (string)a["description"] ?? "",
                    Hidden = a["hidden"]?.Type == Newtonsoft.Json.Linq.JTokenType.Boolean && (bool)a["hidden"],
                    MaxScore = a["maxScore"] != null && a["maxScore"].Type != Newtonsoft.Json.Linq.JTokenType.Null ? (float)a["maxScore"] : 0,
                    MaxDecimals = a["maxDecimals"] != null && a["maxDecimals"].Type != Newtonsoft.Json.Linq.JTokenType.Null ? (int)a["maxDecimals"] : 0,
                    Mod = mod ?? "",
                    ID = _sortID++
                };
                Console.WriteLine($"[ACHIEVEMENTS] mod achievement '{key}' ({AllAchievements[key].Name}) from {mod ?? "mods/"}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ACHIEVEMENTS] Error loading achievements.json ({mod ?? "None"}): {ex.Message}");
        }
    }
}
