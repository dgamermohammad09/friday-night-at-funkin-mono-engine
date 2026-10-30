using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine WeekData — loads week definitions for Story Mode.
/// Direct port of backend/WeekData.hx.
/// </summary>
public class WeekData
{
    public static Dictionary<string, WeekData> WeeksLoaded { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public static List<string> WeeksList { get; set; } = new();

    // JSON properties
    [JsonPropertyName("songs")] public List<List<object>> Songs { get; set; } = new();
    [JsonPropertyName("weekCharacters")] public List<string> WeekCharacters { get; set; } = new() { "dad", "bf", "gf" };
    [JsonPropertyName("weekBackground")] public string WeekBackground { get; set; } = "stage";
    [JsonPropertyName("weekBefore")] public string WeekBefore { get; set; } = "";
    [JsonPropertyName("storyName")] public string StoryName { get; set; } = "Your New Week";
    [JsonPropertyName("weekName")] public string WeekName { get; set; } = "Custom Week";
    [JsonPropertyName("startUnlocked")] public bool StartUnlocked { get; set; } = true;
    [JsonPropertyName("hiddenUntilUnlocked")] public bool HiddenUntilUnlocked { get; set; } = false;
    [JsonPropertyName("hideStoryMode")] public bool HideStoryMode { get; set; } = false;
    [JsonPropertyName("hideFreeplay")] public bool HideFreeplay { get; set; } = false;
    [JsonPropertyName("difficulties")] public string Difficulties { get; set; } = "";

    // Runtime properties
    [JsonIgnore] public string FileName { get; set; } = "";
    [JsonIgnore] public string Folder { get; set; } = "";

    /// <summary>
    /// Reload all week files from base game and mods.
    /// </summary>
    public static void ReloadWeekFiles(string basePath, bool? isStoryMode = false)
    {
        WeeksList.Clear();
        WeeksLoaded.Clear();

        // Load weekList.txt (defines the order)
        string weekListPath = Path.Combine(basePath, "weeks", "weekList.txt");
        List<string> weekNames = new();

        if (global::FNF_MonoGame.Engine.FsCI.FileExists(weekListPath))
        {
            string[] lines = global::FNF_MonoGame.Engine.FsCI.ReadAllLines(weekListPath);
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    weekNames.Add(trimmed);
            }
        }

        // Load each week JSON
        foreach (string weekName in weekNames)
        {
            if (WeeksLoaded.ContainsKey(weekName)) continue;

            string jsonPath = Path.Combine(basePath, "weeks", weekName + ".json");
            var week = LoadWeekFile(jsonPath);
            if (week != null)
            {
                week.FileName = weekName;

                // Filter by visibility
                if (isStoryMode == true && week.HideStoryMode) continue;
                if (isStoryMode == false && week.HideFreeplay) continue;

                WeeksLoaded[weekName] = week;
                WeeksList.Add(weekName);
            }
        }

        // Also scan for week JSON files not in weekList.txt
        string weeksDir = Path.Combine(basePath, "weeks");
        if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(weeksDir))
        {
            foreach (string file in global::FNF_MonoGame.Engine.FsCI.GetFiles(weeksDir, "*.json"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (name == "weekList" || WeeksLoaded.ContainsKey(name)) continue;

                var week = LoadWeekFile(file);
                if (week != null)
                {
                    week.FileName = name;
                    if (isStoryMode == true && week.HideStoryMode) continue;
                    if (isStoryMode == false && week.HideFreeplay) continue;

                    WeeksLoaded[name] = week;
                    WeeksList.Add(name);
                }
            }
        }

        Console.WriteLine($"[WEEK-DATA] Loaded {WeeksList.Count} weeks");
    }

    /// <summary>
    /// Also load weeks from mod folders.
    /// </summary>
    public static void LoadModWeeks(string modsBasePath)
    {
        if (!global::FNF_MonoGame.Engine.FsCI.DirectoryExists(modsBasePath)) return;

        foreach (string modDir in global::FNF_MonoGame.Engine.FsCI.GetDirectories(modsBasePath))
        {
            string weeksDir = Path.Combine(modDir, "weeks");
            if (!global::FNF_MonoGame.Engine.FsCI.DirectoryExists(weeksDir)) continue;

            string modName = Path.GetFileName(modDir);
            foreach (string file in global::FNF_MonoGame.Engine.FsCI.GetFiles(weeksDir, "*.json"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (name == "weekList" || WeeksLoaded.ContainsKey(name)) continue;

                var week = LoadWeekFile(file);
                if (week != null)
                {
                    week.FileName = name;
                    week.Folder = modName;
                    WeeksLoaded[name] = week;
                    WeeksList.Add(name);
                }
            }
        }
    }

    public static WeekData LoadWeekFile(string path)
    {
        try
        {
            if (!global::FNF_MonoGame.Engine.FsCI.FileExists(path)) return null;
            string json = global::FNF_MonoGame.Engine.FsCI.ReadAllText(path);
            return JsonSerializer.Deserialize<WeekData>(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WEEK-DATA] Failed to load {path}: {ex.Message}");
            return null;
        }
    }

    public static WeekData GetCurrentWeek()
    {
        if (PsychGlobals.StoryWeek >= 0 && PsychGlobals.StoryWeek < WeeksList.Count)
        {
            string name = WeeksList[PsychGlobals.StoryWeek];
            return WeeksLoaded.TryGetValue(name, out var w) ? w : null;
        }
        return null;
    }

    public static string GetWeekName(int index)
    {
        var week = GetWeekByIndex(index);
        return week?.WeekName ?? "";
    }

    public static string GetWeekFile(int index)
    {
        if (index >= 0 && index < WeeksList.Count)
            return WeeksList[index];
        return "";
    }

    public static WeekData GetWeekByIndex(int index)
    {
        if (index >= 0 && index < WeeksList.Count)
        {
            string name = WeeksList[index];
            return WeeksLoaded.TryGetValue(name, out var w) ? w : null;
        }
        return null;
    }
}
