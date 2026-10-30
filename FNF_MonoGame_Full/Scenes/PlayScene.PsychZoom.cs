using System;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Psych Engine 1.0.4 camera bop rules for Psych-mod songs (states/PlayState.hx):
///  camZooming starts false and becomes true on the first opponentNoteHit (not on 'tutorial', line 2983);
///  sectionHit (3264): if camZooming &amp;&amp; FlxG.camera.zoom &lt; 1.35 &amp;&amp; camZooms: camera.zoom += 0.015*camZoomingMult,
///  camHUD.zoom += 0.03*camZoomingMult; update (1756): zoom/hud lerp back only while camZooming, exp(-elapsed*3.125*camZoomingDecay).
///  There is no per-beat bop (the V-Slice beat bop is skipped for Psych songs).
/// </summary>
public partial class PlayScene
{
    private bool _psychCamZooming;
    public float camZoomingMult { get; set; } = 1f;
    public float camZoomingDecay { get; set; } = 1f;

    private void PsychSectionZoom()
    {
        if (!PsychNotes || !_psychCamZooming || !_cameraZoomEnabled) return;
        if (_defaultCamZoom * _cameraBopMultiplier >= 1.35f) return;
        _cameraBopMultiplier += _defaultCamZoom > 0f ? 0.015f * camZoomingMult / _defaultCamZoom : 0.015f * camZoomingMult;
        _hudZoomAdd += 0.03f * camZoomingMult;
    }
}
