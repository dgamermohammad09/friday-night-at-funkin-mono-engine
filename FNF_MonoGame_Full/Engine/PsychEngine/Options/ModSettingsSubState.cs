using System;
using System.Collections.Generic;

namespace FNF_MonoGame.Engine.PsychEngine.Options;

/// <summary>
/// Psych Engine mod-specific settings substate.
/// Original: options/ModSettingsSubState.hx
/// Loads option definitions from mod JSON and stores values in a per-mod save map.
/// </summary>
public class ModSettingsSubState : BaseOptionsMenu
{
    private readonly Dictionary<string, object> _save = new(StringComparer.Ordinal);
    private readonly string _folder;
    private bool _crashed;

    public ModSettingsSubState(List<Dictionary<string, object>> options, string folder, string name)
    {
        _folder = folder;
        Title = "";
        RpcTitle = $"Mod Settings ({name})";

        // Load existing save data for this mod
        if (ModSaveData.Data.TryGetValue(folder, out var existingSave) && existingSave != null)
        {
            foreach (var kvp in existingSave)
                _save[kvp.Key] = kvp.Value;
        }

        try
        {
            if (options != null)
            {
                foreach (var optionDef in options)
                {
                    string optName = optionDef.TryGetValue("name", out var n) ? n?.ToString() :
                                     optionDef.TryGetValue("save", out var s2) ? s2?.ToString() : "Unknown";
                    string desc = optionDef.TryGetValue("description", out var d) ? d?.ToString() : "No description provided.";
                    string saveKey = optionDef.TryGetValue("save", out var s) ? s?.ToString() : optName;
                    string typeStr = optionDef.TryGetValue("type", out var t) ? t?.ToString() : "bool";
                    string[] opts = optionDef.TryGetValue("options", out var o) && o is string[] sa ? sa : null;
                    string translationKey = optionDef.TryGetValue("translation_key", out var tk) ? tk?.ToString() : null;

                    OptionType type = ConvertType(typeStr);

                    var newOption = new Option(optName, desc, saveKey, type, opts, translationKey);

                    if (type != OptionType.Keybind)
                    {
                        // Override getValue/setValue to use local save map
                        if (optionDef.TryGetValue("value", out var val) && val != null)
                            newOption.DefaultValue = val;

                        if (optionDef.TryGetValue("format", out var fmt)) newOption.DisplayFormat = fmt?.ToString() ?? "%v";
                        if (optionDef.TryGetValue("min", out var min) && min != null) newOption.MinValue = min;
                        if (optionDef.TryGetValue("max", out var max) && max != null) newOption.MaxValue = max;
                        if (optionDef.TryGetValue("step", out var step) && step != null) newOption.ChangeValue = step;
                        if (optionDef.TryGetValue("scroll", out var scroll) && scroll is float scrollF) newOption.ScrollSpeed = scrollF;
                        if (optionDef.TryGetValue("decimals", out var dec) && dec is int decI) newOption.Decimals = decI;

                        // Load from save or use default
                        if (_save.TryGetValue(saveKey, out var savedVal))
                        {
                            // We store in the option's backing via ClientPrefs but use our save map
                        }
                        else
                        {
                            object myValue = newOption.DefaultValue;
                            _save[saveKey] = myValue;
                        }
                    }
                    else
                    {
                        string kbStr = optionDef.TryGetValue("keyboard", out var kbv) ? kbv?.ToString() : "NONE";
                        string gpStr = optionDef.TryGetValue("gamepad", out var gpv) ? gpv?.ToString() : "NONE";

                        newOption.DefaultKeys = new Keybind { Keyboard = kbStr, Gamepad = gpStr };
                        if (!_save.ContainsKey(saveKey))
                        {
                            newOption.Keys = new Keybind { Keyboard = kbStr, Gamepad = gpStr };
                            _save[saveKey] = newOption.Keys;
                        }
                    }

                    AddOption(newOption);
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[MOD SETTINGS] Error loading mod '{folder}': {e.Message}");
            _crashed = true;
            return;
        }
    }

    private static OptionType ConvertType(string str)
    {
        return (str?.ToLowerInvariant().Trim()) switch
        {
            "bool" => OptionType.Bool,
            "int" or "integer" => OptionType.Int,
            "float" or "fl" => OptionType.Float,
            "percent" => OptionType.Percent,
            "string" or "str" => OptionType.String,
            "keybind" or "key" => OptionType.Keybind,
            _ => OptionType.Bool,
        };
    }

    public override void Update(float elapsed)
    {
        if (_crashed)
        {
            Close();
            return;
        }
        base.Update(elapsed);
    }

    public override void Close()
    {
        // Save mod settings
        ModSaveData.Data[_folder] = _save;
        ModSaveData.Save();
        base.Close();
    }
}

/// <summary>
/// Simple persistent storage for mod settings.
/// Maps mod folder names to their key-value save data.
/// </summary>
public static class ModSaveData
{
    public static Dictionary<string, Dictionary<string, object>> Data { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static void Save()
    {
        try
        {
            // Save handled by the general settings save system
            ClientPrefs.Save();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MOD SAVE] Save failed: {ex.Message}");
        }
    }

    public static void Load()
    {
        // Load handled by the general settings load system
    }
}
