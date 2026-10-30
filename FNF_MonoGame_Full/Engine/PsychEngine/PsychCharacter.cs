using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine Character data format.
/// Direct port of objects/Character.hx (CharacterFile and AnimArray typedefs).
/// Handles loading character JSON definitions.
/// </summary>
public class PsychCharacterFile
{
    [JsonPropertyName("animations")] public List<PsychAnimArray> Animations { get; set; } = new();
    [JsonPropertyName("image")] public string Image { get; set; } = "";
    [JsonPropertyName("scale")] public float Scale { get; set; } = 1f;
    [JsonPropertyName("sing_duration")] public float SingDuration { get; set; } = 4f;
    [JsonPropertyName("healthicon")] public string HealthIcon { get; set; } = "face";
    [JsonPropertyName("position")] public float[] Position { get; set; } = { 0, 0 };
    [JsonPropertyName("camera_position")] public float[] CameraPosition { get; set; } = { 0, 0 };
    [JsonPropertyName("flip_x")] public bool FlipX { get; set; } = false;
    [JsonPropertyName("no_antialiasing")] public bool NoAntialiasing { get; set; } = false;
    [JsonPropertyName("healthbar_colors")] public int[] HealthbarColors { get; set; } = { 255, 0, 0 };
    [JsonPropertyName("vocals_file")] public string VocalsFile { get; set; } = "";
    [JsonPropertyName("_editor_isPlayer")] public bool? EditorIsPlayer { get; set; }
}

public class PsychAnimArray
{
    [JsonPropertyName("anim")] public string Anim { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("fps")] public int Fps { get; set; } = 24;
    [JsonPropertyName("loop")] public bool Loop { get; set; } = false;
    [JsonPropertyName("indices")] public int[] Indices { get; set; }
    [JsonPropertyName("offsets")] public int[] Offsets { get; set; } = { 0, 0 };
}

/// <summary>
/// Runtime character state — matches Psych Engine Character class properties.
/// </summary>
public class PsychCharacterState
{
    public const string DEFAULT_CHARACTER = "bf";

    public string CurCharacter { get; set; } = DEFAULT_CHARACTER;
    public bool IsPlayer { get; set; }
    public float HoldTimer { get; set; }
    public float HeyTimer { get; set; }
    public bool SpecialAnim { get; set; }
    public bool Stunned { get; set; }
    public float SingDuration { get; set; } = 4f;
    public string IdleSuffix { get; set; } = "";
    public bool DanceIdle { get; set; }
    public bool SkipDance { get; set; }
    public string HealthIcon { get; set; } = "face";
    public float[] PositionArray { get; set; } = { 0, 0 };
    public float[] CameraPosition { get; set; } = { 0, 0 };
    public int[] HealthColorArray { get; set; } = { 255, 0, 0 };
    public bool MissingCharacter { get; set; }
    public bool HasMissAnimations { get; set; }
    public string VocalsFile { get; set; } = "";
    public string ImageFile { get; set; } = "";
    public float JsonScale { get; set; } = 1f;
    public bool NoAntialiasing { get; set; }
    public bool OriginalFlipX { get; set; }

    public Dictionary<string, float[]> AnimOffsets { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, object> ExtraData { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Load a character from a JSON file.
    /// </summary>
    public static PsychCharacterFile LoadCharacterFile(string characterName, string basePath)
    {
        string path = Path.Combine(basePath, "characters", characterName + ".json");

        // Fallback to default if not found
        if (!global::FNF_MonoGame.Engine.FsCI.FileExists(path))
        {
            Console.WriteLine($"[PSYCH-CHAR] Character '{characterName}' not found, falling back to {DEFAULT_CHARACTER}");
            path = Path.Combine(basePath, "characters", DEFAULT_CHARACTER + ".json");
        }

        if (!global::FNF_MonoGame.Engine.FsCI.FileExists(path))
        {
            Console.WriteLine($"[PSYCH-CHAR] Default character not found at {path}");
            return null;
        }

        try
        {
            string json = global::FNF_MonoGame.Engine.FsCI.ReadAllText(path);
            return JsonSerializer.Deserialize<PsychCharacterFile>(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PSYCH-CHAR] Error loading character {characterName}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Apply a character file to this state.
    /// </summary>
    public void ApplyCharacterFile(PsychCharacterFile file)
    {
        if (file == null) return;

        SingDuration = file.SingDuration;
        HealthIcon = file.HealthIcon ?? "face";
        PositionArray = file.Position ?? new float[] { 0, 0 };
        CameraPosition = file.CameraPosition ?? new float[] { 0, 0 };
        HealthColorArray = file.HealthbarColors ?? new int[] { 255, 0, 0 };
        VocalsFile = file.VocalsFile ?? "";
        ImageFile = file.Image ?? "";
        JsonScale = file.Scale;
        NoAntialiasing = file.NoAntialiasing;
        OriginalFlipX = file.FlipX;

        // Load animation offsets
        AnimOffsets.Clear();
        if (file.Animations != null)
        {
            foreach (var anim in file.Animations)
            {
                if (!string.IsNullOrEmpty(anim.Anim))
                {
                    AnimOffsets[anim.Anim] = anim.Offsets != null
                        ? new float[] { anim.Offsets[0], anim.Offsets.Length > 1 ? anim.Offsets[1] : 0 }
                        : new float[] { 0, 0 };

                    // Detect dance idle
                    if (anim.Anim == "danceLeft" || anim.Anim == "danceRight")
                        DanceIdle = true;

                    // Detect miss animations
                    if (anim.Anim.Contains("miss", StringComparison.OrdinalIgnoreCase))
                        HasMissAnimations = true;
                }
            }
        }
    }

    /// <summary>
    /// Determine dance animation based on beat.
    /// </summary>
    public string GetDanceAnim(int beat)
    {
        if (DanceIdle)
            return (beat % 2 == 0) ? "danceLeft" + IdleSuffix : "danceRight" + IdleSuffix;
        return "idle" + IdleSuffix;
    }

    /// <summary>
    /// Get the sing animation name for a direction.
    /// </summary>
    public string GetSingAnim(int direction, bool isMiss = false)
    {
        string suffix = isMiss && HasMissAnimations ? "miss" : "";
        return direction switch
        {
            0 => "singLEFT" + suffix,
            1 => "singDOWN" + suffix,
            2 => "singUP" + suffix,
            3 => "singRIGHT" + suffix,
            _ => "idle"
        };
    }
}
