using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Engine.PsychEngine.Shaders;

/// <summary>
/// Wiggle effect shader.
/// Original: shaders/WiggleEffect.hx
/// </summary>
public class WiggleEffect
{
    public Effect ShaderEffect { get; private set; }
    
    private float _time;
    private float _amplitude = 0f;
    private float _frequency = 0f;

    public float Amplitude
    {
        get => _amplitude;
        set
        {
            _amplitude = value;
            ShaderEffect?.Parameters["effectAmplitude"]?.SetValue(_amplitude);
        }
    }

    public float Frequency
    {
        get => _frequency;
        set
        {
            _frequency = value;
            ShaderEffect?.Parameters["effectFrequency"]?.SetValue(_frequency);
        }
    }

    public WiggleEffect(Effect effect = null)
    {
        ShaderEffect = effect;
    }

    public void Update(float elapsedSeconds)
    {
        _time += elapsedSeconds;
        ShaderEffect?.Parameters["uTime"]?.SetValue(_time);
    }
}
