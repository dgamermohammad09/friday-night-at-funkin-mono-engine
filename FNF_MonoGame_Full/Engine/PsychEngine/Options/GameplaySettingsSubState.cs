namespace FNF_MonoGame.Engine.PsychEngine.Options;

/// <summary>
/// Psych Engine gameplay settings options substate.
/// Original: options/GameplaySettingsSubState.hx
/// </summary>
public class GameplaySettingsSubState : BaseOptionsMenu
{
    public GameplaySettingsSubState()
    {
        Title = Language.GetPhrase("gameplay_menu", "Gameplay Settings");
        RpcTitle = "Gameplay Settings Menu";

        var option = new Option("Downscroll",
            "If checked, notes go Down instead of Up, simple enough.",
            "DownScroll", OptionType.Bool);
        AddOption(option);

        option = new Option("Middlescroll",
            "If checked, your notes get centered.",
            "MiddleScroll", OptionType.Bool);
        AddOption(option);

        option = new Option("Opponent Notes",
            "If unchecked, opponent notes get hidden.",
            "OpponentStrums", OptionType.Bool);
        AddOption(option);

        option = new Option("Ghost Tapping",
            "If checked, you won't get misses from pressing keys\nwhile there are no notes able to be hit.",
            "GhostTapping", OptionType.Bool);
        AddOption(option);

        option = new Option("Auto Pause",
            "If checked, the game automatically pauses if the screen isn't on focus.",
            "AutoPause", OptionType.Bool);
        option.OnChange = OnChangeAutoPause;
        AddOption(option);

        option = new Option("Disable Reset Button",
            "If checked, pressing Reset won't do anything.",
            "NoReset", OptionType.Bool);
        AddOption(option);

        option = new Option("Sustains as One Note",
            "If checked, Hold Notes can't be pressed if you miss,\nand count as a single Hit/Miss.\nUncheck this if you prefer the old Input System.",
            "GuitarHeroSustains", OptionType.Bool);
        AddOption(option);

        option = new Option("Hitsound Volume",
            "Funny notes does \"Tick!\" when you hit them.",
            "HitsoundVolume", OptionType.Percent);
        option.ScrollSpeed = 1.6f;
        option.MinValue = 0.0f;
        option.MaxValue = 1f;
        option.ChangeValue = 0.1f;
        option.Decimals = 1;
        option.OnChange = OnChangeHitsoundVolume;
        AddOption(option);

        option = new Option("Rating Offset",
            "Changes how late/early you have to hit for a \"Sick!\"\nHigher values mean you have to hit later.",
            "RatingOffset", OptionType.Int);
        option.DisplayFormat = "%vms";
        option.ScrollSpeed = 20;
        option.MinValue = -30;
        option.MaxValue = 30;
        AddOption(option);

        option = new Option("Sick! Hit Window",
            "Changes the amount of time you have\nfor hitting a \"Sick!\" in milliseconds.",
            "SickWindow", OptionType.Float);
        option.DisplayFormat = "%vms";
        option.ScrollSpeed = 15;
        option.MinValue = 15.0f;
        option.MaxValue = 45.0f;
        option.ChangeValue = 0.1f;
        AddOption(option);

        option = new Option("Good Hit Window",
            "Changes the amount of time you have\nfor hitting a \"Good\" in milliseconds.",
            "GoodWindow", OptionType.Float);
        option.DisplayFormat = "%vms";
        option.ScrollSpeed = 30;
        option.MinValue = 15.0f;
        option.MaxValue = 90.0f;
        option.ChangeValue = 0.1f;
        AddOption(option);

        option = new Option("Bad Hit Window",
            "Changes the amount of time you have\nfor hitting a \"Bad\" in milliseconds.",
            "BadWindow", OptionType.Float);
        option.DisplayFormat = "%vms";
        option.ScrollSpeed = 60;
        option.MinValue = 15.0f;
        option.MaxValue = 135.0f;
        option.ChangeValue = 0.1f;
        AddOption(option);

        option = new Option("Safe Frames",
            "Changes how many frames you have for\nhitting a note earlier or late.",
            "SafeFrames", OptionType.Float);
        option.ScrollSpeed = 5;
        option.MinValue = 2f;
        option.MaxValue = 10f;
        option.ChangeValue = 0.1f;
        AddOption(option);
    }

    private void OnChangeHitsoundVolume()
    {
        // Play hitsound at current volume setting
        FNFGame.Instance?.Audio?.PlaySound(PsychPaths.Sound("hitsound"), ClientPrefs.Data.HitsoundVolume);
    }

    private void OnChangeAutoPause()
    {
        // MonoGame handles focus-loss pause via Game.IsActive checks
    }
}
