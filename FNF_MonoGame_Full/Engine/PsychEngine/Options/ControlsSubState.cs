using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine.PsychEngine.Objects;

namespace FNF_MonoGame.Engine.PsychEngine.Options;

/// <summary>
/// Psych Engine controls/keybinds options substate.
/// Original: options/ControlsSubState.hx
/// </summary>
public class ControlsSubState : BaseOptionsMenu
{
    private readonly Option _defaultKeysOption;

    public ControlsSubState()
    {
        Title = Language.GetPhrase("controls_menu", "Controls");
        RpcTitle = "Controls Menu";

        // Note keybinds
        AddKeybind("Left", "note_left", "Note Left");
        AddKeybind("Down", "note_down", "Note Down");
        AddKeybind("Up", "note_up", "Note Up");
        AddKeybind("Right", "note_right", "Note Right");

        // UI keybinds
        AddKeybind("UI Left", "ui_left", "UI Left");
        AddKeybind("UI Down", "ui_down", "UI Down");
        AddKeybind("UI Up", "ui_up", "UI Up");
        AddKeybind("UI Right", "ui_right", "UI Right");

        // Other controls
        AddKeybind("Reset", "reset", "Reset");
        AddKeybind("Accept", "accept", "Accept");
        AddKeybind("Back", "back", "Back");
        AddKeybind("Pause", "pause", "Pause");

        _defaultKeysOption = new Option("Reset to Default Keys", "Resets all keybinds to the default layout.", "", OptionType.Bool);
        AddOption(_defaultKeysOption);
    }

    private void AddKeybind(string name, string variable, string displayName)
    {
        var option = new Option(name,
            $"Set the keybind for {displayName}.",
            variable, OptionType.Keybind);
        option.DefaultKeys = new Keybind { Keyboard = "NONE", Gamepad = "NONE" };
        option.Keys = new Keybind { Keyboard = "NONE", Gamepad = "NONE" };
        AddOption(option);
    }
}
