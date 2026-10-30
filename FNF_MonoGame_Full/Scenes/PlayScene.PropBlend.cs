using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Scenes;

/// <summary>Animated (Sparrow/atlas) stage props honour the stage JSON "blend" like static props
/// (Stage.hx: propSprite.blend = BlendMode.fromString(dataProp.blend)). Base stages have no blended animated
/// props; mod stages do (QT qtStageKiller lightOverlay / tvLights = ADD).</summary>
public partial class PlayScene
{
    private BlendState AnimPropBlendBegin(SpriteBatch sb, string blend)
    {
        if (string.IsNullOrWhiteSpace(blend)) return null;
        var bs = GetStageBlendState(blend);
        if (bs == null) return null;
        sb.End();
        sb.Begin(samplerState: _worldSampler, blendState: bs, transformMatrix: _worldZoomMatrix, rasterizerState: _worldClipRaster);
        return bs;
    }

    private void AnimPropBlendEnd(SpriteBatch sb, BlendState begun)
    {
        if (begun == null) return;
        sb.End();
        sb.Begin(samplerState: _worldSampler, blendState: _worldBlendState, transformMatrix: _worldZoomMatrix, rasterizerState: _worldClipRaster);
    }
}
