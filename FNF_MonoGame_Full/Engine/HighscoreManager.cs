using Newtonsoft.Json;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Persists highscores and song completion data to disk.
/// Matches the original FNF Highscore.hx save structure.
/// </summary>
public static class HighscoreManager
{
    private static readonly string SavePath = GetSavePath();

    private static string GetSavePath()
    {
#if XBOX_UWP
        return Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "save_data.json");
#else
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "save_data.json");
#endif
    }

    private static SaveData _data;

    /// <summary>
    /// Score entry for a song+difficulty combination.
    /// </summary>
    public class ScoreEntry
    {
        public int Score { get; set; }
        public int MaxCombo { get; set; }
        public float ClearPercent { get; set; }
        public string Rank { get; set; }
    }

    public class SaveData
    {
        public Dictionary<string, ScoreEntry> SongScores { get; set; } = new();
        public Dictionary<string, ScoreEntry> WeekScores { get; set; } = new();
        public float MusicVolume { get; set; } = 1f;
        public float SfxVolume { get; set; } = 1f;
        public bool Mute { get; set; }
        public bool Downscroll { get; set; }
        public bool GhostTapping { get; set; } = true;
        public bool FlashingLights { get; set; } = true;
        public bool ControllerVibration { get; set; } = true;
        public int WindowPrefsVersion { get; set; }
        public bool HideStartupNotice { get; set; }          // startup NOTICE card: player picked DON'T SHOW AGAIN (StartupNoticeScene)
        public bool LaunchFullscreen { get; set; }          // PC: start in borderless fullscreen (Options > Launch in Fullscreen)
        public bool VSync { get; set; } = true;             // PC: SynchronizeWithVerticalRetrace
        public int FramerateCap { get; set; } = 60;         // PC: TargetElapsedTime = 1/FPS
        public bool CameraZoom { get; set; } = true;
        public bool Naughtyness { get; set; } = true;
        public bool Middlescroll { get; set; }
        public bool NoteSkinButtons { get; set; }       // gameplay: draw Xbox button icons instead of arrows (Options > Note Skin)
        public bool NoteSkinChosen { get; set; }        // the player picked Note Skin in Options; until then: Xbox = buttons, PC = arrows
        public int NoteSkinButtonStyle { get; set; } = 3; // controller note skin style 1..6 (ControllerNoteSkin; 3 = dark buttons)
        public string ControllerIconSet { get; set; } = "Auto"; // Options > Button Icons: Auto / Xbox / PlayStation (ControllerIcons)
        public bool FPSCounter { get; set; }
        public bool AutoPause { get; set; } = true;
        public int GlobalOffset { get; set; }
        // Engine options (Options > Preferences): defaults keep the original look/behaviour.
        public bool AssetCache { get; set; } = true;        // keep atlas textures between scenes/songs (SpriteSheetTextureCache)
        public float HudScale { get; set; } = 1f;           // gameplay HUD (camHUD) scale, 0.75..1
        public float SafeArea { get; set; } = 0f;           // TV safe-area margin per side (0..0.10 of the screen)
        public string[] UiKeysAccept { get; set; } = { "Enter", "Space" };
        public string[] UiKeysBack { get; set; } = { "Escape", "Back" };
        public string[] UiKeysPause { get; set; } = { "Enter", "Escape" };
        public string[] UiKeysReset { get; set; } = { "R", "None" };
        public string SelectedCharacter { get; set; } = "bf";

        // Psych Engine ClientPrefs additions
        public int NoteOffset { get; set; }            // visual note offset (ms)
        public int RatingOffset { get; set; }          // rating timing offset (ms)
        public int[] ComboOffset { get; set; } = { 0, 0, 0, 0 }; // combo/rating position offsets
        public bool NoReset { get; set; }                // disable R key reset
        public float HealthBarAlpha { get; set; } = 1f; // health bar transparency
        public bool OpponentStrums { get; set; } = true; // show opponent strumline
        public bool ScoreZoom { get; set; } = true;     // score text zoom on hit
        public bool HideHud { get; set; }                // hide gameplay HUD
        public bool LowQuality { get; set; }             // low quality mode
        public bool Shaders { get; set; } = true;       // enable shaders
        public bool Antialiasing { get; set; } = true;  // enable antialiasing
        public string NoteSkin { get; set; } = "Default";
        public string SplashSkin { get; set; } = "Psych";
        public string Language { get; set; } = "en-US";
        public float HitsoundVolume { get; set; }
        public bool GuitarHeroSustains { get; set; } = true;

        // Key bindings (stored as key name strings for serialization)
        public string[] NoteKeysAlt { get; set; } = { "D", "F", "J", "K" };
        public string[] NoteKeysArrow { get; set; } = { "Left", "Down", "Up", "Right" };

        // Controller bindings (stored as Buttons enum name strings)
        public string[] NoteGamepadDPad { get; set; } = { "DPadLeft", "DPadDown", "DPadUp", "DPadRight" };
        public string[] NoteGamepadFace { get; set; } = { "X", "A", "Y", "B" };
        public string[] NoteGamepadTrigger { get; set; } = { "LeftTrigger", "LeftShoulder", "RightShoulder", "RightTrigger" };

        // Controller menu navigation buttons
        public string ConfirmGamepadButton { get; set; } = "A";
        public string CancelGamepadButton { get; set; } = "B";
        public string PauseGamepadButton { get; set; } = "Start";
        public string SwitchCharGamepadButton { get; set; } = "Y";

        // Freeplay favorites
        public HashSet<string> FavoriteSongs { get; set; } = new();

        // Save.data.unlocks (Save.hx): characters whose Character Select unlock animation was seen, intro video seen.
        // null = save written before the unlock flow existed (migrated once by Unlocks.EnsureMigrated).
        public List<string> CharactersSeen { get; set; }
        public bool OldChar { get; set; }
        public bool PicoLegacyUnlocked { get; set; }   // port-only: save already used Pico before the unlock flow existed
    }

    /// <summary>Note Skin in effect: the Options choice, or (never chosen) Controller Buttons on an Xbox console, Arrows elsewhere.</summary>
    public static bool NoteSkinButtonsSelected => !Data.NoteSkinChosen || Data.NoteSkinButtons;
    /// <summary>Gameplay: controller icons only while the player is actually using a controller (last input came from a
    /// pad; always on an Xbox console). Keyboard players see the normal arrows even if a pad is plugged in.
    /// Choosing "Arrows" in Options always shows arrows.</summary>
    public static bool NoteSkinButtonsEffective => NoteSkinButtonsSelected && InputManager.ControllerActive;

    public static SaveData Data
    {
        get
        {
            _data ??= LoadFromDisk();
            return _data;
        }
    }

    private static string MakeKey(string songName, string difficulty)
        => $"{songName.ToLowerInvariant()}_{difficulty.ToLowerInvariant()}";

    public static int GetScore(string songName, string difficulty)
    {
        string key = MakeKey(songName, difficulty);
        return Data.SongScores.TryGetValue(key, out var entry) ? entry.Score : 0;
    }

    public static float GetClearPercent(string songName, string difficulty)
    {
        string key = MakeKey(songName, difficulty);
        return Data.SongScores.TryGetValue(key, out var entry) ? entry.ClearPercent : 0f;
    }

    public static string GetRank(string songName, string difficulty)
    {
        string key = MakeKey(songName, difficulty);
        return Data.SongScores.TryGetValue(key, out var entry) ? entry.Rank : null;
    }

    /// <summary>
    /// Save a score. Returns true if it's a new highscore.
    /// </summary>
    public static bool SaveScore(string songName, string difficulty, int score,
        int maxCombo, float clearPercent, string rank)
    {
        // Save.applySongRank: the high score and the high rank are kept separately - score = max(old, new);
        // tallies (here clear %, rank, max combo) = the old ones if the old rank is better or its completion
        // is higher, else the new ones. (Was: nothing saved unless the score beat the old one, so a better
        // rank / clear % with a lower score was lost.)  Returns isSongHighScore (score only).
        string key = MakeKey(songName, difficulty);
        if (!Data.SongScores.TryGetValue(key, out var existing) || existing == null || RankOrder(existing.Rank) < 0)
        {
            Data.SongScores[key] = new ScoreEntry { Score = score, MaxCombo = maxCombo, ClearPercent = clearPercent, Rank = rank };
            SaveToDisk();
            return true;
        }
        bool isNew = score > existing.Score;
        bool keepOldTallies = RankOrder(existing.Rank) > RankOrder(rank) || existing.ClearPercent > clearPercent;
        var merged = new ScoreEntry
        {
            Score = Math.Max(existing.Score, score),
            MaxCombo = keepOldTallies ? existing.MaxCombo : maxCombo,
            ClearPercent = keepOldTallies ? existing.ClearPercent : clearPercent,
            Rank = keepOldTallies ? existing.Rank : rank
        };
        if (merged.Score != existing.Score || merged.Rank != existing.Rank || merged.ClearPercent != existing.ClearPercent || merged.MaxCombo != existing.MaxCombo)
        {
            Data.SongScores[key] = merged;
            SaveToDisk();
        }
        return isNew;
    }

    /// <summary>Scoring rank as a comparable number (SHIT 0 .. PERFECT_GOLD 5); -1 = none/unknown.</summary>
    public static int RankOrder(string rank) => (rank ?? "").ToUpperInvariant() switch
    {
        "PERFECT_GOLD" => 5, "PERFECT" => 4, "EXCELLENT" => 3, "GREAT" => 2, "GOOD" => 1, "SHIT" => 0, _ => -1
    };

    /// <summary>Scoring.calculateRank from tallies: sick full clear = PERFECT_GOLD, else tallyCompletion thresholds.</summary>
    public static string CalculateRank(int sick, int good, int missed, int totalNotes)
    {
        if (totalNotes <= 0) return null;
        if (sick == totalNotes) return "PERFECT_GOLD";
        float c = Math.Clamp((sick + good - missed) / (float)totalNotes, 0f, 1f);
        return c == 1f ? "PERFECT" : c >= 0.90f ? "EXCELLENT" : c >= 0.80f ? "GREAT" : c >= 0.60f ? "GOOD" : "SHIT";
    }

    public static int GetWeekScore(string weekId, string difficulty)
    {
        string key = MakeKey(weekId, difficulty);
        return Data.WeekScores.TryGetValue(key, out var entry) ? entry.Score : 0;
    }

    public static bool SaveWeekScore(string weekId, string difficulty, int score)
    {
        string key = MakeKey(weekId, difficulty);
        bool isNew = !Data.WeekScores.TryGetValue(key, out var existing) || score > existing.Score;
        if (isNew)
        {
            Data.WeekScores[key] = new ScoreEntry { Score = score };
            SaveToDisk();
        }
        return isNew;
    }

    public static void SavePreferences()
    {
        SaveToDisk();
        try { FNF_MonoGame.Engine.PsychEngine.ClientPrefs.SaveSynced(); } catch { }
    }

    public static void ClearSaveData()
    {
        lock (_writeLock)
        {
            try
            {
                _data = new SaveData();
                string json = JsonConvert.SerializeObject(_data, Formatting.Indented);
                File.WriteAllText(SavePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error clearing save data: {ex.Message}");
            }
        }
    }

    private static SaveData LoadFromDisk()
    {
        try
        {
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(SavePath))
            {
                string json = global::FNF_MonoGame.Engine.FsCI.ReadAllText(SavePath);
                var data = JsonConvert.DeserializeObject<SaveData>(json);
                if (data != null)
                {
                    Console.WriteLine($"Loaded save data: {data.SongScores.Count} song scores");
                    return data;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading save data: {ex.Message}");
        }
        return new SaveData();
    }

    // Lock to prevent concurrent async writes from racing on the same file
    private static readonly object _writeLock = new();
    
    public static void Save() => SaveToDisk();

    private static void SaveToDisk()
    {
        try
        {
            string json = JsonConvert.SerializeObject(_data, Formatting.Indented);
            // Write to disk on a background thread to avoid frame hitches
            string path = SavePath;
            Task.Run(() =>
            {
                lock (_writeLock)
                {
                    try { File.WriteAllText(path, json); }
                    catch (Exception ex) { Console.WriteLine($"Error saving data: {ex.Message}"); }
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error serializing save data: {ex.Message}");
        }
    }
}
