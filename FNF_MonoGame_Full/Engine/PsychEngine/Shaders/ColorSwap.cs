using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Engine.PsychEngine.Shaders;

/// <summary>
/// Color swap shader effect (Hue, Saturation, Lightness).
/// Original: shaders/ColorSwap.hx
/// </summary>
public class ColorSwap
{
    public Effect ShaderEffect { get; private set; }

    private float _hue;
    public float Hue
    {
        get => _hue;
        set
        {
            _hue = value;
            ShaderEffect?.Parameters["uTime"]?.SetValue(_hue);
        }
    }

    private float _saturation;
    public float Saturation
    {
        get => _saturation;
        set
        {
            _saturation = value;
            ShaderEffect?.Parameters["uTime"]?.SetValue(_saturation);
        }
    }

    private float _lightness;
    public float Lightness
    {
        get => _lightness;
        set
        {
            _lightness = value;
            ShaderEffect?.Parameters["uTime"]?.SetValue(_lightness);
        }
    }

    public ColorSwap(Effect effect = null)
    {
        ShaderEffect = effect;
    }
}
