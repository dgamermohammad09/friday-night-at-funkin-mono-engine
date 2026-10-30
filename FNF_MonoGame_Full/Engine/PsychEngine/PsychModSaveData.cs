using System;
using System.Collections.Generic;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>Psych FlxG.save.data.modSettings (mod folder -> save tag -> value), persisted next to psychsave.json as
/// psych_modsettings.json. Read by getModSetting (Lua/HScript); defaults come from mods/&lt;mod&gt;/data/settings.json.</summary>
public static class PsychModSaveData
{
    public static Dictionary<string, Dictionary<string, object>> Data { get; } = new(StringComparer.OrdinalIgnoreCase);
    private static bool _loaded;
    private static string FilePath => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(ClientPrefs.SavePath) ?? ".", "psych_modsettings.json");

    public static void Save()
    {
        try { System.IO.File.WriteAllText(FilePath, FNF_MonoGame.Engine.JsonCompat.Serialize(Data)); } // UWP-safe writer
        catch (Exception ex) { Console.WriteLine($"[MOD SAVE] Save failed: {ex.Message}"); }
    }

    public static void EnsureLoaded() { if (!_loaded) Load(); }

    public static void Load()
    {
        _loaded = true;
        try
        {
            if (!global::FNF_MonoGame.Engine.FsCI.FileExists(FilePath)) return;
            var root = Newtonsoft.Json.Linq.JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(FilePath));
            foreach (var mod in root.Properties())
            {
                var d = new Dictionary<string, object>(StringComparer.Ordinal);
                if (mod.Value is Newtonsoft.Json.Linq.JObject o)
                    foreach (var p in o.Properties())
                        d[p.Name] = p.Value is Newtonsoft.Json.Linq.JObject kb
                            ? new Dictionary<string, object> { ["keyboard"] = kb["keyboard"]?.ToString() ?? "NONE", ["gamepad"] = kb["gamepad"]?.ToString() ?? "NONE" }
                            : LuaUtils.JTokenToPlain(p.Value);
                Data[mod.Name] = d;
            }
        }
        catch (Exception ex) { Console.WriteLine($"[MOD SAVE] Load failed: {ex.Message}"); }
    }
}
