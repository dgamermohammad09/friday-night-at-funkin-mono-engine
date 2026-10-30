using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Engine.PsychEngine.Shaders;

/// <summary>
/// Error Handled Shader wrapper for safe shader fallback.
/// Original: shaders/ErrorHandledShader.hx
/// </summary>
public class ErrorHandledShader
{
    public Effect ShaderEffect { get; private set; }

    public ErrorHandledShader(Effect effect)
    {
        ShaderEffect = effect;
    }
}
