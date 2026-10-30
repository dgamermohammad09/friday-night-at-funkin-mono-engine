using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Scenes;

/// <summary>Psych PlayState.videoCutscene (objects/VideoSprite.hx) as seen by scripts: camera/cameras, alpha, visible,
/// pause/resume. VideoSprite takes the LAST camera in FlxG.cameras.list (camOther: above the HUD); a script may move it
/// to another camera (setProperty('videoCutscene.camera', instanceArg('SexCam'), false, true) in YARARARA), which puts
/// the video under camHUD.</summary>
public sealed class PsychVideoFacade
{
    internal PlayScene Owner;
    private object _camera;
    /// <summary>Accepts the camera object or an instanceArg/variable name of it (the reflection path may hand over the
    /// marker string).</summary>
    public object camera
    {
        get => _camera;
        set
        {
            if (value is string str)
            {
                string n = str.Replace("::INSTANCE::", "").Trim();
                if (FNF_MonoGame.Engine.PsychEngine.MusicBeatState.Variables.TryGetValue(n, out var o)) { _camera = o; return; }
            }
            _camera = value;
        }
    }
    public object cameras { get => camera == null ? null : new object[] { camera }; set { if (value is System.Collections.IList l && l.Count > 0) camera = l[0]; } }
    public float alpha = 1f;
    public bool visible = true;
    public void pause() => Owner?.PsychVideoPause(true);
    public void resume() => Owner?.PsychVideoPause(false);
}

public partial class PlayScene
{
    private PsychVideoFacade _psychVideo;
    /// <summary>PlayState.videoCutscene (null when no video was started by a Psych script).</summary>
    public PsychVideoFacade videoCutscene => PsychNotes ? (_psychVideo ??= new PsychVideoFacade { Owner = this }) : null;

    internal void PsychVideoPause(bool pause)
    {
        try { if (pause) _vid?.Pause(); else _vid?.Resume(); } catch (Exception e) { Console.WriteLine($"[VIDEO] pause/resume: {e.Message}"); }
    }

    /// <summary>True when the video belongs to an extra (HScript/Lua-created) camera and must be drawn in that camera's
    /// layer instead of on top of everything.</summary>
    private bool PsychVideoOnExtraCam => _psychVideo != null && _psychVideo.camera is HxFlxCamera && _vid != null;

    private void PsychDrawVideoInCamera(SpriteBatch sb, HxFlxCamera cam)
    {
        if (Environment.GetEnvironmentVariable("FNF_VIDLOG") == "1") Console.WriteLine($"[VIDCAM] draw cam vis={_vid?.Visible}");
        if (_psychVideo == null || !ReferenceEquals(_psychVideo.camera, cam) || _vid == null || !_vid.Visible || !_psychVideo.visible) return;
        _vid.Draw(sb, 1280, 720); // VideoSprite: setGraphicSize(FlxG.width) + screenCenter on that camera
    }
}
