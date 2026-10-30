using System.Globalization;
using System.Text;

namespace FNF_MonoGame.Scenes;

// Dev-only: dumps stage props for side-by-side comparison with the original game ([PROPINFO] lines at dev shots).
public partial class PlayScene
{
    public string DevCamZoomNow => $"{_defaultCamZoom * _cameraBopMultiplier} follow={_camTargetWorldX},{_camTargetWorldY} def={_defaultCamZoom} bop={_cameraBopMultiplier}";

    public string DevPropInfo
    {
        get
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            foreach (var p in _staticProps)
                sb.Append(string.Format(ci, "[PROPINFO] S {0} x={1:0.##} y={2:0.##} sf={3:0.##},{4:0.##} sc={5:0.###},{6:0.###} a={7:0.##} vis={8} z={9} blend={10} anim={11}:{12} col={13}\n",
                    p.Name, p.X, p.Y, p.ScrollX, p.ScrollY, p.ScaleX, p.ScaleY, p.Alpha, p.Visible ? 1 : 0, p.ZIndex, p.Blend, p.CurrentAnimation, p.CurrentFrame, p.OverlayColor));
            foreach (var p in _animatedProps)
            {
                var s = p.Sprite;
                sb.Append(string.Format(ci, "[PROPINFO] A {0} x={1:0.##} y={2:0.##} sf={3:0.##},{4:0.##} sc={5:0.###},{6:0.###} a={7:0.##} z={8} blend={9} anim={10}:{11} col={12}\n",
                    p.Name, s?.Position.X, s?.Position.Y, p.ScrollX, p.ScrollY, s?.Scale.X, s?.Scale.Y, p.Alpha, p.ZIndex, p.Blend, s?.CurrentAnimation, s?.FrameIndex, p.Color ?? p.OverlayColor));
            }
            return sb.ToString();
        }
    }
}
