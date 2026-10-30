using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Engine.PsychEngine.Shaders;

/// <summary>
/// Rain effect shader.
/// Original: shaders/RainShader.hx
/// </summary>
public class RainShader
{
    public Effect ShaderEffect { get; private set; }
    
    private float _time;

    public RainShader(Effect effect = null)
    {
        ShaderEffect = effect;
    }

    public void Update(float elapsedSeconds)
    {
        _time += elapsedSeconds;
        ShaderEffect?.Parameters["uTime"]?.SetValue(_time);
    }
}
