using System;
using System.Reflection;
using FNF_MonoGame.Engine.PsychEngine.Objects;

namespace FNF_MonoGame.Engine.PsychEngine.Options;

public enum OptionType
{
    Bool,
    Int,
    Float,
    Percent,
    String,
    Keybind
}

public class Keybind
{
    public string Keyboard { get; set; } = "NONE";
    public string Gamepad { get; set; } = "NONE";
}

/// <summary>
/// Psych Engine options menu item definition.
/// Original: options/Option.hx
/// </summary>
public class Option
{
    public AttachedText Child { get; set; }

    private string _name;
    private string _translationKey;
    private string _text;

    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            if (_translationKey == null) _translationKey = value;
            _name = Language.GetPhrase($"setting_{_translationKey}", value);
        }
    }

    public string Text
    {
        get => _text;
        set
        {
            _text = value;
            if (Child != null)
                Child.Text = Language.GetPhrase($"setting_{_translationKey}-{GetValue()}", _text);
        }
    }

    public Action OnChange { get; set; }
    public OptionType Type { get; set; } = OptionType.Bool;

    public float ScrollSpeed { get; set; } = 50f;
    public string Variable { get; set; }
    public object DefaultValue { get; set; }

    public int CurOption { get; set; }
    public string[] Options { get; set; }
    public object ChangeValue { get; set; } = 1;
    public object MinValue { get; set; }
    public object MaxValue { get; set; }
    public int Decimals { get; set; } = 1;

    public string DisplayFormat { get; set; } = "%v";
    public string Description { get; set; } = "";

    public Keybind DefaultKeys { get; set; }
    public Keybind Keys { get; set; }

    public Option(string name, string description, string variable, OptionType type = OptionType.Bool, string[] options = null, string translation = null)
    {
        _name = name;
        _translationKey = translation ?? _name;
        Name = Language.GetPhrase($"setting_{_translationKey}", name);
        Description = Language.GetPhrase($"description_{_translationKey}", description);
        Variable = variable;
        Type = type;
        Options = options;

        if (Type != OptionType.Keybind)
            DefaultValue = GetProperty(ClientPrefs.DefaultData, variable);

        switch (Type)
        {
            case OptionType.Bool:
                DefaultValue ??= false;
                break;
            case OptionType.Int:
            case OptionType.Float:
                DefaultValue ??= 0;
                break;
            case OptionType.Percent:
                DefaultValue ??= 1f;
                DisplayFormat = "%v%";
                ChangeValue = 0.01f;
                MinValue = 0f;
                MaxValue = 1f;
                ScrollSpeed = 0.5f;
                Decimals = 2;
                break;
            case OptionType.String:
                if (Options?.Length > 0)
                    DefaultValue = Options[0];
                DefaultValue ??= "";
                break;
            case OptionType.Keybind:
                DefaultValue = "";
                DefaultKeys = new Keybind();
                Keys = new Keybind();
                break;
        }

        try
        {
            if (GetValue() == null)
                SetValue(DefaultValue);

            if (Type == OptionType.String && GetValue() is string s && Options != null)
            {
                int idx = System.Array.IndexOf(Options, s);
                if (idx > -1) CurOption = idx;
            }
        }
        catch { }
    }

    public void Change()
    {
        OnChange?.Invoke();
    }

    public object GetValue()
    {
        var value = GetProperty(ClientPrefs.Data, Variable);
        if (Type == OptionType.Keybind && value is Keybind k)
        {
            // TODO: check controller mode
            return k.Keyboard;
        }
        return value;
    }

    public object SetValue(object value)
    {
        if (Type == OptionType.Keybind && GetProperty(ClientPrefs.Data, Variable) is Keybind k)
        {
            // TODO: check controller mode
            k.Keyboard = value as string;
            return value;
        }
        SetProperty(ClientPrefs.Data, Variable, value);
        return value;
    }

    private static object GetProperty(object target, string name)
    {
        if (target == null || string.IsNullOrEmpty(name)) return null;
        var prop = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        return prop?.GetValue(target);
    }

    private static void SetProperty(object target, string name, object value)
    {
        if (target == null || string.IsNullOrEmpty(name)) return;
        var prop = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        prop?.SetValue(target, value);
    }
}
