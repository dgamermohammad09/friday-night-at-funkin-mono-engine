using System;
using System.Collections.Generic;
using System.Linq;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine Difficulty management.
/// Direct port of backend/Difficulty.hx.
/// </summary>
public static class Difficulty
{
    public static readonly string[] DefaultList = { "Easy", "Normal", "Hard" };
    private const string DefaultDifficulty = "Normal";

    public static List<string> List { get; set; } = new(DefaultList);

    /// <summary>
    /// Get the file path suffix for a difficulty.
    /// Normal has no suffix, others get "-DiffName" appended.
    /// </summary>
    public static string GetFilePath(int? num = null)
    {
        int index = num ?? PsychGlobals.StoryDifficulty;
        string filePostfix = index >= 0 && index < List.Count ? List[index] : null;

        if (filePostfix != null && FormatToSongPath(filePostfix) != FormatToSongPath(DefaultDifficulty))
            return "-" + FormatToSongPath(filePostfix);

        return "";
    }

    /// <summary>
    /// Load difficulty list from a WeekData.
    /// </summary>
    public static void LoadFromWeek(WeekData week)
    {
        if (week == null) { ResetList(); return; }

        string diffStr = week.Difficulties;
        if (!string.IsNullOrEmpty(diffStr))
        {
            var diffs = diffStr.Trim().Split(',')
                .Select(d => d.Trim())
                .Where(d => d.Length > 0)
                .ToList();
            if (diffs.Count > 0)
            {
                List = diffs;
                return;
            }
        }
        ResetList();
    }

    public static void ResetList()
    {
        List = new List<string>(DefaultList);
    }

    public static void CopyFrom(IEnumerable<string> diffs)
    {
        List = new List<string>(diffs);
    }

    /// <summary>
    /// Get the display name for a difficulty.
    /// </summary>
    public static string GetString(int? num = null)
    {
        int index = num ?? PsychGlobals.StoryDifficulty;
        return index >= 0 && index < List.Count ? List[index] : DefaultDifficulty;
    }

    public static string GetDefault() => DefaultDifficulty;

    /// <summary>
    /// Format a song/difficulty name to a path-safe string (lowercase, spaces to dashes).
    /// Matches Psych Engine Paths.formatToSongPath().
    /// </summary>
    public static string FormatToSongPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        // Psych Paths.formatToSongPath():
        //   invalidChars = [~&;:<>#\s] replaced with '-'
        //   hideChars    = [.,'"%?!]   removed
        //   then trim + lowercase
        string invalidReplaced = System.Text.RegularExpressions.Regex.Replace(path, @"[~&;:<>#\s]", "-");
        string hidden = System.Text.RegularExpressions.Regex.Replace(invalidReplaced, "[.,'\"%?!]", "");
        return hidden.Trim().ToLowerInvariant();
    }
}
