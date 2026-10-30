using System;
using System.Collections.Generic;
using System.Linq;

namespace FNF_MonoGame.Engine.PsychEngine.Options;

/// <summary>
/// Psych Engine visuals settings options substate.
/// Original: options/VisualsSettingsSubState.hx
/// </summary>
public class VisualsSettingsSubState : BaseOptionsMenu
{
    public VisualsSettingsSubState()
    {
        Title = Language.GetPhrase("visuals_menu", "Visuals Settings");
        RpcTitle = "Visuals Settings Menu";

        // Note Skins (STRING option — uses merged list from mods)
        var noteSkins = PsychMods.MergeAllTextsNamed("images/noteSkins/list.txt");
        if (noteSkins != null && noteSkins.Count > 0)
        {
            var skinsList = new List<string> { ClientPrefs.DefaultData.NoteSkin };
            foreach (var skin in noteSkins)
            {
                if (!skinsList.Contains(skin)) skinsList.Add(skin);
            }

            var option = new Option("Note Skins:",
                "Select your prefered Note skin.",
                "NoteSkin", OptionType.String, skinsList.ToArray());
            option.OnChange = OnChangeNoteSkin;
            AddOption(option);
        }

        // Splash Skins (STRING option)
        var splashSkins = PsychMods.MergeAllTextsNamed("images/noteSplashes/list.txt");
        if (splashSkins != null && splashSkins.Count > 0)
        {
            var splashList = new List<string> { ClientPrefs.DefaultData.SplashSkin };
            foreach (var skin in splashSkins)
            {
                if (!splashList.Contains(skin)) splashList.Add(skin);
            }

            var option = new Option("Note Splashes:",
                "Select your prefered Note Splash variation.",
                "SplashSkin", OptionType.String, splashList.ToArray());
            option.OnChange = OnChangeSplashSkin;
            AddOption(option);
        }

        var opt = new Option("Note Splash Opacity",
            "How much transparent should the Note Splashes be.",
            "SplashAlpha", OptionType.Percent);
        opt.ScrollSpeed = 1.6f;
        opt.MinValue = 0.0f;
        opt.MaxValue = 1f;
        opt.ChangeValue = 0.1f;
        opt.Decimals = 1;
        opt.OnChange = PlayNoteSplashes;
        AddOption(opt);

        opt = new Option("Hide HUD",
            "If checked, hides most HUD elements.",
            "HideHud", OptionType.Bool);
        AddOption(opt);

        opt = new Option("Time Bar:",
            "What should the Time Bar display?",
            "TimeBarType", OptionType.String,
            new[] { "Time Left", "Time Elapsed", "Song Name", "Disabled" });
        AddOption(opt);

        opt = new Option("Flashing Lights",
            "Uncheck this if you're sensitive to flashing lights!",
            "Flashing", OptionType.Bool);
        AddOption(opt);

        opt = new Option("Camera Zooms",
            "If unchecked, the camera won't zoom in on a beat hit.",
            "CamZooms", OptionType.Bool);
        AddOption(opt);

        opt = new Option("Score Text Grow on Hit",
            "If unchecked, disables the Score text growing\neverytime you hit a note.",
            "ScoreZoom", OptionType.Bool);
        AddOption(opt);

        opt = new Option("Health Bar Opacity",
            "How much transparent should the health bar and icons be.",
            "HealthBarAlpha", OptionType.Percent);
        opt.ScrollSpeed = 1.6f;
        opt.MinValue = 0.0f;
        opt.MaxValue = 1f;
        opt.ChangeValue = 0.1f;
        opt.Decimals = 1;
        AddOption(opt);

        opt = new Option("FPS Counter",
            "If unchecked, hides FPS Counter.",
            "ShowFPS", OptionType.Bool);
        AddOption(opt);

        opt = new Option("Pause Music:",
            "What song do you prefer for the Pause Screen?",
            "PauseMusic", OptionType.String,
            new[] { "None", "Tea Time", "Breakfast", "Breakfast (Pico)" });
        opt.OnChange = OnChangePauseMusic;
        AddOption(opt);

        opt = new Option("Combo Stacking",
            "If unchecked, Ratings and Combo won't stack, saving on System Memory and making them easier to read",
            "ComboStacking", OptionType.Bool);
        AddOption(opt);
    }

    private void OnChangeNoteSkin()
    {
        // Note skin preview handled by engine note reload
    }

    private void OnChangeSplashSkin()
    {
        // Splash skin preview handled by engine splash reload
    }

    private void PlayNoteSplashes()
    {
        // Play splash animation preview
    }

    private void OnChangePauseMusic()
    {
        // Pause music preview — playback handled by calling state
    }

    public override void Close()
    {
        // Restore menu music if it was changed
        base.Close();
    }
}
