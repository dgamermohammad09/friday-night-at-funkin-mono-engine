using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Engine.PsychEngine.Shaders;

/// <summary>
/// Blend mode effect shader wrapper.
/// Original: shaders/BlendModeEffect.hx
/// </summary>
public class BlendModeEffect
{
    public Effect ShaderEffect { get; private set; }

    public BlendModeEffect(Effect effect)
    {
        ShaderEffect = effect;
    }
}
