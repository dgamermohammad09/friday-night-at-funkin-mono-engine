namespace FNF_MonoGame.Engine.PsychEngine.Options;

/// <summary>
/// Psych Engine graphics settings options substate.
/// Original: options/GraphicsSettingsSubState.hx
/// </summary>
public class GraphicsSettingsSubState : BaseOptionsMenu
{
    private int _antialiasingOption;

    public GraphicsSettingsSubState()
    {
        Title = Language.GetPhrase("graphics_menu", "Graphics Settings");
        RpcTitle = "Graphics Settings Menu";

        var option = new Option("Low Quality",
            "If checked, disables some background details,\ndecreases loading times and improves performance.",
            "LowQuality", OptionType.Bool);
        AddOption(option);

        option = new Option("Anti-Aliasing",
            "If unchecked, disables anti-aliasing, increases performance\nat the cost of sharper visuals.",
            "Antialiasing", OptionType.Bool);
        option.OnChange = OnChangeAntiAliasing;
        AddOption(option);
        _antialiasingOption = _optionsArray.Count - 1;

        option = new Option("Shaders",
            "If unchecked, disables shaders.\nIt's used for some visual effects, and also CPU intensive for weaker PCs.",
            "Shaders", OptionType.Bool);
        AddOption(option);

        option = new Option("GPU Caching",
            "If checked, allows the GPU to be used for caching textures, decreasing RAM usage.\nDon't turn this on if you have a shitty Graphics Card.",
            "CacheOnGPU", OptionType.Bool);
        AddOption(option);

        option = new Option("Framerate",
            "Pretty self explanatory, isn't it?",
            "Framerate", OptionType.Int);
        option.MinValue = 60;
        option.MaxValue = 240;
        option.DefaultValue = 60;
        option.DisplayFormat = "%v FPS";
        option.OnChange = OnChangeFramerate;
        AddOption(option);
    }

    private void OnChangeAntiAliasing()
    {
        // Propagate antialiasing change to all sprites (handled by engine rendering)
    }

    private void OnChangeFramerate()
    {
        // MonoGame handles framerate via TargetElapsedTime
        var fps = ClientPrefs.Data.Framerate;
        if (fps > 0)
            FNFGame.Instance.TargetElapsedTime = System.TimeSpan.FromSeconds(1.0 / fps);
    }

    public override void ChangeSelection(int change)
    {
        base.ChangeSelection(change);
    }
}
