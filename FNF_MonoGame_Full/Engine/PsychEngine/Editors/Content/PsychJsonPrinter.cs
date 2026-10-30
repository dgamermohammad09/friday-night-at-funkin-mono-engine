using FontStashSharp;
using FNF_MonoGame.Sprites;
using FNF_MonoGame.Engine.PsychEngine.Objects;
namespace FNF_MonoGame.Engine.PsychEngine.Editors.Content;

/// <summary>
/// Psych Engine JSON printer used by editors.
/// Original: states/editors/content/PsychJsonPrinter.hx
/// </summary>
public static class PsychJsonPrinter
{
    public static string Print(object data)
    {
        return System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }
}
