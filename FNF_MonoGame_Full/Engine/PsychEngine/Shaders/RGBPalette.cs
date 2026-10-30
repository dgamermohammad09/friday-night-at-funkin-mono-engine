using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Engine.PsychEngine.Shaders;

/// <summary>
/// RGB Palette shader, heavily used by NoteSplash and StrumNote.
/// Original: shaders/RGBPalette.hx
/// </summary>
public class RGBPalette
{
    public Effect ShaderEffect { get; private set; }

    private Color _r;
    public Color R
    {
        get => _r;
        set
        {
            _r = value;
            ShaderEffect?.Parameters["rColor"]?.SetValue(value.ToVector3());
        }
    }

    private Color _g;
    public Color G
    {
        get => _g;
        set
        {
            _g = value;
            ShaderEffect?.Parameters["gColor"]?.SetValue(value.ToVector3());
        }
    }

    private Color _b;
    public Color B
    {
        get => _b;
        set
        {
            _b = value;
            ShaderEffect?.Parameters["bColor"]?.SetValue(value.ToVector3());
        }
    }

    public RGBPalette(Effect effect = null)
    {
        ShaderEffect = effect;
        R = Color.Red;
        G = Color.Lime;
        B = Color.Blue;
    }
}
