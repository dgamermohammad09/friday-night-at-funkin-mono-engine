using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine Song data format.
/// Direct port of backend/Song.hx — SwagSong, SwagSection types.
/// Handles loading and converting Psych Engine chart JSON files.
/// </summary>
public class PsychSong
{
    [JsonPropertyName("song")] public string Song { get; set; } = "";
    [JsonPropertyName("notes")] public List<PsychSection> Notes { get; set; } = new();
    [JsonPropertyName("events")] public List<List<object>> Events { get; set; } = new();
    [JsonPropertyName("bpm")] public float BPM { get; set; } = 100;
    [JsonPropertyName("needsVoices")] public bool NeedsVoices { get; set; } = true;
    [JsonPropertyName("speed")] public float Speed { get; set; } = 1;
    [JsonPropertyName("offset")] public float Offset { get; set; } = 0;

    [JsonPropertyName("player1")] public string Player1 { get; set; } = "bf";
    [JsonPropertyName("player2")] public string Player2 { get; set; } = "dad";
    [JsonPropertyName("gfVersion")] public string GfVersion { get; set; } = "gf";
    [JsonPropertyName("stage")] public string Stage { get; set; } = "stage";
    [JsonPropertyName("format")] public string Format { get; set; } = "psych_v1";

    [JsonPropertyName("gameOverChar")] public string GameOverChar { get; set; }
    [JsonPropertyName("gameOverSound")] public string GameOverSound { get; set; }
    [JsonPropertyName("gameOverLoop")] public string GameOverLoop { get; set; }
    [JsonPropertyName("gameOverEnd")] public string GameOverEnd { get; set; }

    [JsonPropertyName("disableNoteRGB")] public bool DisableNoteRGB { get; set; } = false;
    [JsonPropertyName("arrowSkin")] public string ArrowSkin { get; set; }
    [JsonPropertyName("splashSkin")] public string SplashSkin { get; set; }

    public static string ChartPath { get; set; }
    public static string LoadedSongName { get; set; }

    /// <summary>
    /// Load a song chart from JSON.
    /// </summary>
    public static PsychSong LoadFromJson(string jsonInput, string folder, string basePath)
    {
        if (string.IsNullOrEmpty(folder)) folder = jsonInput;

        string formattedFolder = Difficulty.FormatToSongPath(folder);
        string formattedSong = Difficulty.FormatToSongPath(jsonInput);
        string path = Path.Combine(basePath, "songs", formattedFolder, formattedSong + ".json");

        // Try alternate paths
        if (!global::FNF_MonoGame.Engine.FsCI.FileExists(path))
            path = Path.Combine(basePath, "data", formattedFolder, formattedSong + ".json");

        ChartPath = path;
        LoadedSongName = folder;

        if (!global::FNF_MonoGame.Engine.FsCI.FileExists(path))
        {
            Console.WriteLine($"[PSYCH-SONG] Chart not found: {path}");
            return null;
        }

        try
        {
            string raw = global::FNF_MonoGame.Engine.FsCI.ReadAllText(path);
            return ParseJSON(raw, jsonInput);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PSYCH-SONG] Parse error for {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parse raw JSON into a PsychSong. Handles nested "song" wrapper.
    /// </summary>
    public static PsychSong ParseJSON(string rawData, string nameForError = null)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawData);
            var root = doc.RootElement;

            // Handle nested { "song": { ... } } wrapper
            if (root.TryGetProperty("song", out var songEl) && songEl.ValueKind == JsonValueKind.Object)
            {
                return JsonSerializer.Deserialize<PsychSong>(songEl.GetRawText());
            }

            return JsonSerializer.Deserialize<PsychSong>(rawData);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PSYCH-SONG] JSON parse error ({nameForError}): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Convert old chart format to psych_v1.
    /// Handles player3->gfVersion, events extraction, section beats.
    /// </summary>
    public void Convert()
    {
        if (Format != null && Format.StartsWith("psych_v1")) return;
        Format = "psych_v1_convert";

        // Ensure events list exists
        Events ??= new List<List<object>>();

        // Process sections
        if (Notes == null) return;
        foreach (var section in Notes)
        {
            // Default section beats
            if (section.SectionBeats <= 0 || float.IsNaN(section.SectionBeats))
                section.SectionBeats = 4;

            // Normalize note columns
            if (section.SectionNotes == null) continue;
            foreach (var note in section.SectionNotes)
            {
                if (note.Count < 4) continue;
                int noteData = Convert_ToInt(note[1]);
                bool gottaHitNote = noteData < 4 ? section.MustHitSection : !section.MustHitSection;
                note[1] = (noteData % 4) + (gottaHitNote ? 0 : 4);
            }
        }

        Console.WriteLine("[PSYCH-SONG] Chart converted to psych_v1 format.");
    }

    private static int Convert_ToInt(object val)
    {
        if (val is int i) return i;
        if (val is long l) return (int)l;
        if (val is float f) return (int)f;
        if (val is double d) return (int)d;
        if (val is JsonElement je) return je.GetInt32();
        return int.TryParse(val?.ToString(), out int r) ? r : 0;
    }
}

/// <summary>
/// A section of notes in a Psych Engine chart.
/// </summary>
public class PsychSection
{
    [JsonPropertyName("sectionNotes")] public List<List<object>> SectionNotes { get; set; } = new();
    [JsonPropertyName("sectionBeats")] public float SectionBeats { get; set; } = 4;
    [JsonPropertyName("mustHitSection")] public bool MustHitSection { get; set; } = true;
    [JsonPropertyName("altAnim")] public bool AltAnim { get; set; } = false;
    [JsonPropertyName("gfSection")] public bool GfSection { get; set; } = false;
    [JsonPropertyName("bpm")] public float BPM { get; set; } = 0;
    [JsonPropertyName("changeBPM")] public bool ChangeBPM { get; set; } = false;
}
