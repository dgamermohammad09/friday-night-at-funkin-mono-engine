using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Engine.PsychEngine.Shaders;

/// <summary>
/// Overlay effect shader wrapper.
/// Original: shaders/OverlayShader.hx
/// </summary>
public class OverlayShader
{
    public Effect ShaderEffect { get; private set; }

    public OverlayShader(Effect effect)
    {
        ShaderEffect = effect;
    }
}
