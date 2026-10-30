using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine StageData — loads stage definitions from JSON.
/// Direct port of backend/StageData.hx.
/// </summary>
public class StageData
{
    [JsonPropertyName("directory")] public string Directory { get; set; } = "";
    [JsonPropertyName("defaultZoom")] public float DefaultZoom { get; set; } = 0.9f;
    [JsonPropertyName("isPixelStage")] public bool? IsPixelStage { get; set; } = false;
    [JsonPropertyName("stageUI")] public string StageUI { get; set; } = "normal";

    [JsonPropertyName("boyfriend")] public float[] Boyfriend { get; set; } = { 770, 100 };
    [JsonPropertyName("girlfriend")] public float[] Girlfriend { get; set; } = { 400, 130 };
    [JsonPropertyName("opponent")] public float[] Opponent { get; set; } = { 100, 100 };
    [JsonPropertyName("hide_girlfriend")] public bool HideGirlfriend { get; set; } = false;

    [JsonPropertyName("camera_boyfriend")] public float[] CameraBoyfriend { get; set; } = { 0, 0 };
    [JsonPropertyName("camera_opponent")] public float[] CameraOpponent { get; set; } = { 0, 0 };
    [JsonPropertyName("camera_girlfriend")] public float[] CameraGirlfriend { get; set; } = { 0, 0 };
    [JsonPropertyName("camera_speed")] public float? CameraSpeed { get; set; } = 1f;

    [JsonPropertyName("objects")] public List<StageObject> Objects { get; set; }
    [JsonPropertyName("preload")] public StagePreload Preload { get; set; }

    // Force next directory (for mod paths)
    public static string ForceNextDirectory { get; set; } = null;

    public static StageData Dummy() => new();

    /// <summary>
    /// Load a stage JSON file.
    /// </summary>
    public static StageData GetStageFile(string stageName, string basePath)
    {
        try
        {
            // Check mods first, then base game
            string path = Path.Combine(basePath, "stages", stageName + ".json");
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(path))
            {
                string json = global::FNF_MonoGame.Engine.FsCI.ReadAllText(path);
                var data = JsonSerializer.Deserialize<StageData>(json);
                if (data != null) return data;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[STAGE-DATA] Error loading stage '{stageName}': {ex.Message}");
        }
        return Dummy();
    }

    /// <summary>
    /// Load the stage directory for a song.
    /// </summary>
    public static void LoadDirectory(string stageName, string basePath)
    {
        if (string.IsNullOrEmpty(stageName)) stageName = "stage";
        var stageFile = GetStageFile(stageName, basePath);
        ForceNextDirectory = stageFile?.Directory ?? "";
    }

    /// <summary>
    /// Get the vanilla stage name for a vanilla song.
    /// Matches Psych Engine StageData.vanillaSongStage() exactly.
    /// </summary>
    public static string VanillaSongStage(string songName)
    {
        return songName?.ToLowerInvariant() switch
        {
            "spookeez" or "south" or "monster" => "spooky",
            "pico" or "blammed" or "philly" or "philly-nice" => "philly",
            "milf" or "satin-panties" or "high" => "limo",
            "cocoa" or "eggnog" => "mall",
            "winter-horrorland" => "mallEvil",
            "senpai" or "roses" => "school",
            "thorns" => "schoolEvil",
            "ugh" or "guns" or "stress" => "tank",
            _ => "stage"
        };
    }

    /// <summary>
    /// Reserved object names that cannot be used in stage editor.
    /// </summary>
    public static readonly string[] ReservedNames =
        { "gf", "gfGroup", "dad", "dadGroup", "boyfriend", "boyfriendGroup" };
}

public class StageObject
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "sprite"; // sprite, animatedSprite, group
    [JsonPropertyName("image")] public string Image { get; set; } = "";
    [JsonPropertyName("position")] public float[] Position { get; set; } = { 0, 0 };
    [JsonPropertyName("scroll")] public float[] Scroll { get; set; } = { 1, 1 };
    [JsonPropertyName("scale")] public float Scale { get; set; } = 1f;
    [JsonPropertyName("antialiasing")] public bool Antialiasing { get; set; } = true;
    [JsonPropertyName("alpha")] public float Alpha { get; set; } = 1f;
    [JsonPropertyName("color")] public string Color { get; set; } = "FFFFFF";
    [JsonPropertyName("blend")] public string Blend { get; set; } = "";
    [JsonPropertyName("filters")] public int Filters { get; set; } = 3; // LOW_QUALITY | HIGH_QUALITY
    [JsonPropertyName("animations")] public List<StageAnimation> Animations { get; set; }
}

public class StageAnimation
{
    [JsonPropertyName("anim")] public string Anim { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("fps")] public int Fps { get; set; } = 24;
    [JsonPropertyName("loop")] public bool Loop { get; set; } = false;
    [JsonPropertyName("indices")] public int[] Indices { get; set; }
    [JsonPropertyName("offsets")] public float[] Offsets { get; set; } = { 0, 0 };
}

public class StagePreload
{
    [JsonPropertyName("images")] public List<string> Images { get; set; }
    [JsonPropertyName("sounds")] public List<string> Sounds { get; set; }
    [JsonPropertyName("music")] public List<string> Music { get; set; }
}
