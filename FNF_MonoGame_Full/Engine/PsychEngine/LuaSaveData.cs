using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine Lua save data — persistent key-value storage for mod scripts.
/// Direct port of ExtraFunctions.hx save data management.
/// Xbox compatible: uses ApplicationData.LocalFolder on Xbox.
/// </summary>
public class LuaSaveData
{
    public string Name { get; }
    private readonly Dictionary<string, object> _data = new(StringComparer.Ordinal);
    private readonly string _filePath;

    public LuaSaveData(string name, string folder = "psychenginemods")
    {
        Name = name;

#if XBOX_UWP
        string baseDir = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, folder);
#else
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string baseDir = Path.Combine(appData, "PsychEngine", folder);
#endif
        Directory.CreateDirectory(baseDir);
        _filePath = Path.Combine(baseDir, name + ".json");

        // Load existing data
        if (global::FNF_MonoGame.Engine.FsCI.FileExists(_filePath))
        {
            try
            {
                string json = global::FNF_MonoGame.Engine.FsCI.ReadAllText(_filePath);
                _data = FNF_MonoGame.Engine.JsonCompat.Deserialize<Dictionary<string, object>>(json)
                    ?? new Dictionary<string, object>(StringComparer.Ordinal);
            }
            catch { }
        }
    }

    public object GetField(string field, object defaultValue = null)
    {
        return _data.TryGetValue(field, out var val) ? val : defaultValue;
    }

    public void SetField(string field, object value)
    {
        _data[field] = value;
    }

    public bool HasField(string field) => _data.ContainsKey(field);

    public void Flush()
    {
        try
        {
            string json = FNF_MonoGame.Engine.JsonCompat.Serialize(_data);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LUA-SAVE] Flush failed for {Name}: {ex.Message}");
        }
    }

    public void Erase()
    {
        _data.Clear();
        try { if (global::FNF_MonoGame.Engine.FsCI.FileExists(_filePath)) File.Delete(_filePath); } catch { }
    }
}
