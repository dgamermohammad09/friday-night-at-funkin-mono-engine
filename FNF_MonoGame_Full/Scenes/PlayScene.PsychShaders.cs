using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine.PsychEngine;

namespace FNF_MonoGame.Scenes;

/// <summary>Psych setSpriteShader on camGame Lua sprites: the prop is drawn in its own batch with the object's runtime
/// shader (Engine/PsychEngine/RuntimeShader.cs); PtEndLightFx restores the world batch.</summary>
public partial class PlayScene
{
    private bool PsyRuntimeShaderBegin(SpriteBatch sb, StaticStageProp prop)
    {
        if (prop == null || ShaderFunctions.SpriteShaders.Count == 0 || IsHudCamera(prop.Camera)) return false;
        var rs = ShaderFunctions.GetForSprite(prop.Name);
        if (rs?.Effect == null) return false;
        rs.Prepare(prop.Texture);
        sb.End();
        sb.Begin(samplerState: _worldSampler, blendState: _worldBlendState, transformMatrix: _worldZoomMatrix, rasterizerState: _worldClipRaster, effect: rs.Effect);
        return true;
    }
}
