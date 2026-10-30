using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine ClientPrefs — handles all game settings and save data.
/// Direct port of backend/ClientPrefs.hx.
/// Xbox compatible: uses ApplicationData.LocalFolder on Xbox, AppData on PC.
/// </summary>
public class ClientPrefs
{
    public static SaveVariables Data { get; set; } = new();
    public static SaveVariables DefaultData { get; } = new();

    // Key bindings (simplified for MonoGame — InputManager handles the actual mapping)
    public static Dictionary<string, int[]> KeyBinds { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["note_up"] = new[] { (int)Microsoft.Xna.Framework.Input.Keys.W, (int)Microsoft.Xna.Framework.Input.Keys.Up },
        ["note_left"] = new[] { (int)Microsoft.Xna.Framework.Input.Keys.A, (int)Microsoft.Xna.Framework.Input.Keys.Left },
        ["note_down"] = new[] { (int)Microsoft.Xna.Framework.Input.Keys.S, (int)Microsoft.Xna.Framework.Input.Keys.Down },
        ["note_right"] = new[] { (int)Microsoft.Xna.Framework.Input.Keys.D, (int)Microsoft.Xna.Framework.Input.Keys.Right },
        ["ui_up"] = new[] { (int)Microsoft.Xna.Framework.Input.Keys.W, (int)Microsoft.Xna.Framework.Input.Keys.Up },
        ["ui_left"] = new[] { (int)Microsoft.Xna.Framework.Input.Keys.A, (int)Microsoft.Xna.Framework.Input.Keys.Left },
        ["ui_down"] = new[] { (int)Microsoft.Xna.Framework.Input.Keys.S, (int)Microsoft.Xna.Framework.Input.Keys.Down },
        ["ui_right"] = new[] { (int)Microsoft.Xna.Framework.Input.Keys.D, (int)Microsoft.Xna.Framework.Input.Keys.Right },
        ["accept"] = new[] { (int)Microsoft.Xna.Framework.Input.Keys.Space, (int)Microsoft.Xna.Framework.Input.Keys.Enter },
        ["back"] = new[] { (int)Microsoft.Xna.Framework.Input.Keys.Back, (int)Microsoft.Xna.Framework.Input.Keys.Escape },
        ["pause"] = new[] { (int)Microsoft.Xna.Framework.Input.Keys.Enter, (int)Microsoft.Xna.Framework.Input.Keys.Escape },
        ["reset"] = new[] { (int)Microsoft.Xna.Framework.Input.Keys.R },
    };

    internal static string SavePath
    {
        get
        {
#if XBOX_UWP
            return Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "psychsave.json");
#else
            string dev = Environment.GetEnvironmentVariable("FNF_PSYCHSAVE"); // dev/test runs: private psychsave.json
            if (!string.IsNullOrEmpty(dev)) return dev;
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "PsychEngine");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "psychsave.json");
#endif
        }
    }

    public static void Init()
    {
        Data = new SaveVariables();
        Load();
    }

    public static void Save()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = FNF_MonoGame.Engine.JsonCompat.Serialize(Data); // UWP-safe (see JsonCompat)
            File.WriteAllText(SavePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CLIENT-PREFS] Save failed: {ex.Message}");
        }
    }

    public static void Load()
    {
        Loaded = true;
        try
        {
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(SavePath))
            {
                string json = global::FNF_MonoGame.Engine.FsCI.ReadAllText(SavePath);
                Data = FNF_MonoGame.Engine.JsonCompat.Deserialize<SaveVariables>(json) ?? new SaveVariables();
                Loaded = true;
                Console.WriteLine("[CLIENT-PREFS] Loaded settings.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CLIENT-PREFS] Load failed, using defaults: {ex.Message}");
            Data = new SaveVariables();
        }
    }

    public static bool Loaded { get; private set; }
    /// <summary>Load psychsave.json once (Options / Psych Online / PlayScene read ClientPrefs.Data).</summary>
    public static void EnsureLoaded() { if (!Loaded) { Load(); SyncFromSave(); } }

    /// <summary>Copy the V-Slice settings that Psych also has (Options > Preferences) into ClientPrefs.Data.</summary>
    public static void SyncFromSave()
    {
        var d = FNF_MonoGame.Engine.HighscoreManager.Data;
        if (d == null) return;
        Data.DownScroll = d.Downscroll; Data.MiddleScroll = d.Middlescroll; Data.GhostTapping = d.GhostTapping;
        Data.Flashing = d.FlashingLights; Data.CamZooms = d.CameraZoom; Data.AutoPause = d.AutoPause; Data.ShowFPS = d.FPSCounter;
    }

    /// <summary>HighscoreManager.SavePreferences hook: keep psychsave.json in step with the V-Slice prefs.</summary>
    public static void SaveSynced() { EnsureLoaded(); SyncFromSave(); Save(); }

    public static void Reset()
    {
        Data = new SaveVariables();
        Save();
    }
}

/// <summary>
/// All saveable game settings — mirrors Psych Engine SaveVariables exactly.
/// </summary>
public class SaveVariables
{
    [JsonPropertyName("downScroll")] public bool DownScroll { get; set; } = false;
    [JsonPropertyName("middleScroll")] public bool MiddleScroll { get; set; } = false;
    [JsonPropertyName("opponentStrums")] public bool OpponentStrums { get; set; } = true;
    [JsonPropertyName("showFPS")] public bool ShowFPS { get; set; } = true;
    [JsonPropertyName("flashing")] public bool Flashing { get; set; } = true;
    [JsonPropertyName("autoPause")] public bool AutoPause { get; set; } = true;
    [JsonPropertyName("antialiasing")] public bool Antialiasing { get; set; } = true;
    [JsonPropertyName("noteSkin")] public string NoteSkin { get; set; } = "Default";
    [JsonPropertyName("splashSkin")] public string SplashSkin { get; set; } = "Psych";
    [JsonPropertyName("splashAlpha")] public float SplashAlpha { get; set; } = 0.6f;
    [JsonPropertyName("lowQuality")] public bool LowQuality { get; set; } = false;
    [JsonPropertyName("shaders")] public bool Shaders { get; set; } = true;
    [JsonPropertyName("cacheOnGPU")] public bool CacheOnGPU { get; set; } = false;
    [JsonPropertyName("framerate")] public int Framerate { get; set; } = 60;
    [JsonPropertyName("camZooms")] public bool CamZooms { get; set; } = true;
    [JsonPropertyName("hideHud")] public bool HideHud { get; set; } = false;
    [JsonPropertyName("noteOffset")] public int NoteOffset { get; set; } = 0;
    [JsonPropertyName("ghostTapping")] public bool GhostTapping { get; set; } = true;
    [JsonPropertyName("timeBarType")] public string TimeBarType { get; set; } = "Time Left";
    [JsonPropertyName("scoreZoom")] public bool ScoreZoom { get; set; } = true;
    [JsonPropertyName("noReset")] public bool NoReset { get; set; } = false;
    [JsonPropertyName("healthBarAlpha")] public float HealthBarAlpha { get; set; } = 1f;
    [JsonPropertyName("hitsoundVolume")] public float HitsoundVolume { get; set; } = 0f;
    [JsonPropertyName("pauseMusic")] public string PauseMusic { get; set; } = "Tea Time";
    [JsonPropertyName("checkForUpdates")] public bool CheckForUpdates { get; set; } = true;
    [JsonPropertyName("comboStacking")] public bool ComboStacking { get; set; } = true;
    [JsonPropertyName("guitarHeroSustains")] public bool GuitarHeroSustains { get; set; } = true;
    // ---- Psych Online (online/ClientPrefs.hx) extras: visuals + gameplay
    [JsonPropertyName("holdSplashAlpha")] public float HoldSplashAlpha { get; set; } = 0.6f;
    [JsonPropertyName("holdAlpha")] public float HoldAlpha { get; set; } = 0.6f;
    [JsonPropertyName("noteUnderlayOpacity")] public float NoteUnderlayOpacity { get; set; } = 0f;
    [JsonPropertyName("camShakes")] public bool CamShakes { get; set; } = true;
    [JsonPropertyName("camAngles")] public bool CamAngles { get; set; } = true;
    [JsonPropertyName("camMovement")] public bool CamMovement { get; set; } = true;
    [JsonPropertyName("colorRating")] public bool ColorRating { get; set; } = false;
    [JsonPropertyName("disableComboRating")] public bool DisableComboRating { get; set; } = false;
    [JsonPropertyName("disableComboCounter")] public bool DisableComboCounter { get; set; } = false;
    [JsonPropertyName("showNoteTiming")] public bool ShowNoteTiming { get; set; } = false;
    [JsonPropertyName("disableStrumMovement")] public bool DisableStrumMovement { get; set; } = false;
    [JsonPropertyName("modchartSkinChanges")] public bool ModchartSkinChanges { get; set; } = false;
    [JsonPropertyName("disableLagDetection")] public bool DisableLagDetection { get; set; } = false;
    [JsonPropertyName("disableAnimAudio")] public bool DisableAnimAudio { get; set; } = false;
    // ---- Psych Online: online options (OnlineOptionsState / GameClient / RoomState)
    [JsonPropertyName("serverAddress")] public string ServerAddress { get; set; } = null;
    [JsonPropertyName("nickname")] public string Nickname { get; set; } = null;
    /// <summary>[character, dadSuffix, bfSuffix, modFolder] like PSO ClientPrefs.currentSkin (null = no skin).</summary>
    [JsonPropertyName("currentSkin")] public string[] CurrentSkin { get; set; } = null;
    [JsonPropertyName("trustedSources")] public List<string> TrustedSources { get; set; } = new() { "https://gamebanana.com/" };
    [JsonPropertyName("disableAutoDownloads")] public bool DisableAutoDownloads { get; set; } = false;
    [JsonPropertyName("notifyOnChatMsg")] public bool NotifyOnChatMsg { get; set; } = false;
    [JsonPropertyName("disableSongComments")] public bool DisableSongComments { get; set; } = false;
    [JsonPropertyName("midSongCommentsOpacity")] public float MidSongCommentsOpacity { get; set; } = 0.5f;
    [JsonPropertyName("nameplateFadeTime")] public float NameplateFadeTime { get; set; } = 10f;
    [JsonPropertyName("showFP")] public bool ShowFP { get; set; } = false;
    [JsonPropertyName("disableReplays")] public bool DisableReplays { get; set; } = false;
    [JsonPropertyName("disableSubmiting")] public bool DisableSubmiting { get; set; } = false;
    [JsonPropertyName("verifySSL")] public bool VerifySSL { get; set; } = false;
    [JsonPropertyName("disablePMs")] public bool DisablePMs { get; set; } = false;
    [JsonPropertyName("disableRoomInvites")] public bool DisableRoomInvites { get; set; } = false;
    [JsonPropertyName("showPing")] public bool ShowPing { get; set; } = true;            // engine: ping next to names in rooms / in-game
    [JsonPropertyName("inGameChat")] public bool InGameChat { get; set; } = true;        // engine: show chat lines during a match
    [JsonPropertyName("discordRPC")] public bool DiscordRPC { get; set; } = true;
    [JsonPropertyName("loadingScreen")] public bool LoadingScreen { get; set; } = true;
    [JsonPropertyName("language")] public string Language { get; set; } = "en-US";

    // Rating windows (ms)
    [JsonPropertyName("ratingOffset")] public int RatingOffset { get; set; } = 0;
    [JsonPropertyName("sickWindow")] public float SickWindow { get; set; } = 45f;
    [JsonPropertyName("goodWindow")] public float GoodWindow { get; set; } = 90f;
    [JsonPropertyName("badWindow")] public float BadWindow { get; set; } = 135f;
    [JsonPropertyName("safeFrames")] public float SafeFrames { get; set; } = 10f;

    // Combo offset [comboX, comboY, ratingX, ratingY]
    [JsonPropertyName("comboOffset")] public int[] ComboOffset { get; set; } = new[] { 0, 0, 0, 0 };

    // Gameplay settings
    [JsonPropertyName("scrollSpeed")] public float ScrollSpeed { get; set; } = 1f;
    [JsonPropertyName("scrollType")] public string ScrollType { get; set; } = "multiplicative";
    [JsonPropertyName("songSpeed")] public float SongSpeed { get; set; } = 1f;
    [JsonPropertyName("healthGain")] public float HealthGain { get; set; } = 1f;
    [JsonPropertyName("healthLoss")] public float HealthLoss { get; set; } = 1f;
    [JsonPropertyName("instakill")] public bool Instakill { get; set; } = false;
    [JsonPropertyName("practice")] public bool Practice { get; set; } = false;
    [JsonPropertyName("botplay")] public bool Botplay { get; set; } = false;
    [JsonPropertyName("opponentplay")] public bool OpponentPlay { get; set; } = false;

    // Note colors (ARGB hex values) [left, down, up, right] x [body, outline, shadow]
    [JsonPropertyName("arrowRGB")] public uint[][] ArrowRGB { get; set; } = new[]
    {
        new uint[] { 0xFFC24B99, 0xFFFFFFFF, 0xFF3C1F56 },
        new uint[] { 0xFF00FFFF, 0xFFFFFFFF, 0xFF1542B7 },
        new uint[] { 0xFF12FA05, 0xFFFFFFFF, 0xFF0A4447 },
        new uint[] { 0xFFF9393F, 0xFFFFFFFF, 0xFF651038 },
    };

    [JsonPropertyName("arrowRGBPixel")] public uint[][] ArrowRGBPixel { get; set; } = new[]
    {
        new uint[] { 0xFFE276FF, 0xFFFFF9FF, 0xFF60008D },
        new uint[] { 0xFF3DCAFF, 0xFFF4FFFF, 0xFF003060 },
        new uint[] { 0xFF71E300, 0xFFF6FFE6, 0xFF003100 },
        new uint[] { 0xFFFF884E, 0xFFFFFAF5, 0xFF6C0000 },
    };
}
