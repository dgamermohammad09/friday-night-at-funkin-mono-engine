namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Global state variables matching Psych Engine's PlayState static fields.
/// These are shared between Lua scripts, HScript, and the game engine.
/// </summary>
public static class PsychGlobals
{
    public const string PsychEngineVersion = "1.0.4"; // MainMenuState.psychEngineVersion: scripts compare it (version >= '1.0.0'); "1.0-monogame" sorted below that

    // Shared random for stage/level scripts
    public static Random Random { get; } = new Random();

    // Story mode state
    public static bool IsStoryMode { get; set; } = false;
    public static int StoryDifficulty { get; set; } = 1; // 0=Easy, 1=Normal, 2=Hard
    public static int StoryWeek { get; set; } = 0;
    public static bool SeenCutscene { get; set; } = false;

    // Death counter (resets per song attempt)
    public static int DeathCounter { get; set; } = 0;

    // Chart scroll speed
    public static float ChartScrollSpeed { get; set; } = 1f;

    // Current song info
    public static string CurrentSong { get; set; } = "";
    public static string CurrentStage { get; set; } = "stage";
    public static float CurrentBPM { get; set; } = 100f;

    // Gameplay modifiers (from ClientPrefs)
    public static float HealthGainMult => ClientPrefs.Data.HealthGain;
    public static float HealthLossMult => ClientPrefs.Data.HealthLoss;
    public static bool GhostTapping => ClientPrefs.Data.GhostTapping;
    public static bool Botplay => ClientPrefs.Data.Botplay;
    public static bool Practice => ClientPrefs.Data.Practice;
    public static bool Instakill => ClientPrefs.Data.Instakill;
    public static bool OpponentPlay => ClientPrefs.Data.OpponentPlay;

    // Animation speed multiplier (used by PsychAnimationController)
    public static float AnimationTimeScale { get; set; } = 1f;
    public static float LastAnimationDelta { get; set; } = 0f;

    public static void Reset()
    {
        IsStoryMode = false;
        StoryDifficulty = 1;
        StoryWeek = 0;
        SeenCutscene = false;
        DeathCounter = 0;
        ChartScrollSpeed = 1f;
        CurrentSong = "";
        CurrentStage = "stage";
        CurrentBPM = 100f;
    }
}
