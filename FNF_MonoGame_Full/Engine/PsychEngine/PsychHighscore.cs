using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine Highscore system.
/// Direct port of backend/Highscore.hx.
/// Separate from the existing HighscoreManager to handle Psych Engine format.
/// </summary>
public static class PsychHighscore
{
    public static Dictionary<string, int> WeekScores { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public static Dictionary<string, int> SongScores { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public static Dictionary<string, float> SongRatings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static string FormatSong(string song, int diff)
    {
        return Difficulty.FormatToSongPath(song) + Difficulty.GetFilePath(diff);
    }

    public static void ResetSong(string song, int diff = 0)
    {
        string key = FormatSong(song, diff);
        SongScores[key] = 0;
        SongRatings[key] = 0;
        Save();
    }

    public static void ResetWeek(string week, int diff = 0)
    {
        string key = FormatSong(week, diff);
        WeekScores[key] = 0;
        Save();
    }

    public static void SaveScore(string song, int score, int diff = 0, float rating = -1)
    {
        if (string.IsNullOrEmpty(song)) return;
        string key = FormatSong(song, diff);

        if (SongScores.TryGetValue(key, out int existing))
        {
            if (existing < score)
            {
                SongScores[key] = score;
                if (rating >= 0) SongRatings[key] = rating;
            }
        }
        else
        {
            SongScores[key] = score;
            if (rating >= 0) SongRatings[key] = rating;
        }
        Save();
    }

    public static void SaveWeekScore(string week, int score, int diff = 0)
    {
        string key = FormatSong(week, diff);
        if (WeekScores.TryGetValue(key, out int existing))
        {
            if (existing < score) WeekScores[key] = score;
        }
        else WeekScores[key] = score;
        Save();
    }

    public static int GetScore(string song, int diff)
    {
        string key = FormatSong(song, diff);
        return SongScores.TryGetValue(key, out int val) ? val : 0;
    }

    public static float GetRating(string song, int diff)
    {
        string key = FormatSong(song, diff);
        return SongRatings.TryGetValue(key, out float val) ? val : 0f;
    }

    public static int GetWeekScore(string week, int diff)
    {
        string key = FormatSong(week, diff);
        return WeekScores.TryGetValue(key, out int val) ? val : 0;
    }

    private static string SavePath
    {
        get
        {
#if XBOX_UWP
            return Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "highscores.json");
#else
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "PsychEngine");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "highscores.json");
#endif
        }
    }

    public static void Save()
    {
        try
        {
            var data = new HighscoreSaveData
            {
                WeekScores = WeekScores,
                SongScores = SongScores,
                SongRatings = SongRatings
            };
            string json = FNF_MonoGame.Engine.JsonCompat.Serialize(data);
            File.WriteAllText(SavePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HIGHSCORE] Save failed: {ex.Message}");
        }
    }

    public static void Load()
    {
        try
        {
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(SavePath))
            {
                string json = global::FNF_MonoGame.Engine.FsCI.ReadAllText(SavePath);
                var data = FNF_MonoGame.Engine.JsonCompat.Deserialize<HighscoreSaveData>(json);
                if (data != null)
                {
                    WeekScores = data.WeekScores ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    SongScores = data.SongScores ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    SongRatings = data.SongRatings ?? new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
                }
                Console.WriteLine("[HIGHSCORE] Loaded scores.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HIGHSCORE] Load failed: {ex.Message}");
        }
    }

    private class HighscoreSaveData
    {
        [JsonPropertyName("weekScores")] public Dictionary<string, int> WeekScores { get; set; } = new();
        [JsonPropertyName("songScores")] public Dictionary<string, int> SongScores { get; set; } = new();
        [JsonPropertyName("songRatings")] public Dictionary<string, float> SongRatings { get; set; } = new();
    }
}
