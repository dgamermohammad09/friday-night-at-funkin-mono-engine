using System;
using System.Collections.Generic;
using System.IO;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine NoteTypesConfig — custom note type property overrides.
/// Direct port of backend/NoteTypesConfig.hx.
/// Reads custom_notetypes/{name}.txt to override note properties.
/// </summary>
public static class NoteTypesConfig
{
    public class NoteTypeProperty
    {
        public string[] PropertyPath { get; set; }
        public object Value { get; set; }
    }

    private static readonly Dictionary<string, List<NoteTypeProperty>> _cache
        = new(StringComparer.OrdinalIgnoreCase);

    public static void Clear() => _cache.Clear();

    /// <summary>
    /// Load note type data from custom_notetypes/{name}.txt.
    /// Format: property.path = value (one per line)
    /// </summary>
    public static List<NoteTypeProperty> LoadNoteTypeData(string name, string basePath)
    {
        if (_cache.TryGetValue(name, out var cached))
            return cached;

        string filePath = Path.Combine(basePath, "custom_notetypes", name + ".txt");
        if (!global::FNF_MonoGame.Engine.FsCI.FileExists(filePath))
        {
            _cache[name] = null;
            return null;
        }

        try
        {
            string content = global::FNF_MonoGame.Engine.FsCI.ReadAllText(filePath);
            if (!content.Contains(':') && !content.Contains('='))
            {
                _cache[name] = null;
                return null;
            }

            var parsed = new List<NoteTypeProperty>();
            string[] lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("//")) continue;

                int sep = line.IndexOf(':');
                if (sep < 0) sep = line.IndexOf('=');
                if (sep < 0) continue;

                string propStr = line[..sep].Trim();
                string valStr = line[(sep + 1)..].Trim();

                string[] pathParts = propStr.Split('.');
                for (int i = 0; i < pathParts.Length; i++)
                    pathParts[i] = pathParts[i].Trim();

                parsed.Add(new NoteTypeProperty
                {
                    PropertyPath = pathParts,
                    Value = InterpretValue(valStr)
                });
            }

            _cache[name] = parsed;
            return parsed;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NOTE-TYPES] Failed to parse {name}: {ex.Message}");
            _cache[name] = null;
            return null;
        }
    }

    /// <summary>
    /// Apply note type properties to a note's property dictionary.
    /// </summary>
    public static void ApplyNoteTypeData(Dictionary<string, object> noteProps, string typeName, string basePath)
    {
        var data = LoadNoteTypeData(typeName, basePath);
        if (data == null || data.Count == 0) return;

        foreach (var prop in data)
        {
            if (prop.PropertyPath.Length == 1)
            {
                noteProps[prop.PropertyPath[0]] = prop.Value;
            }
            else if (prop.PropertyPath.Length == 2)
            {
                // Handle nested property like "animation.speed"
                string key = string.Join(".", prop.PropertyPath);
                noteProps[key] = prop.Value;
            }
        }
    }

    private static object InterpretValue(string str)
    {
        if (string.IsNullOrEmpty(str)) return null;

        // Bool
        if (str.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (str.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        if (str.Equals("null", StringComparison.OrdinalIgnoreCase)) return null;

        // Int
        if (int.TryParse(str, out int intVal)) return intVal;

        // Float
        if (float.TryParse(str, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float floatVal))
            return floatVal;

        // String (strip quotes if present)
        if (str.Length >= 2 && str[0] == '"' && str[^1] == '"')
            return str[1..^1];
        if (str.Length >= 2 && str[0] == '\'' && str[^1] == '\'')
            return str[1..^1];

        return str;
    }
}
