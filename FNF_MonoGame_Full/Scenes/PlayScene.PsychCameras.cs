using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine.PsychEngine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Psych camGame / camHUD / camOther rendering (flixel 5 FlxCamera): x/y move the camera's view on screen,
/// angle rotates the camera's whole render about its centre, alpha fades the camera layer (camGame is drawn over the
/// black stage background -> black overlay of 1-alpha; camHUD is rendered into a target and drawn with alpha),
/// visible=false hides it, flash/fade = full-camera colour fill (FlxCamera.drawFX), shake offsets the view by
/// random(-1,1) * intensity * size * zoom.
/// </summary>
public partial class PlayScene
{
    private RenderTarget2D _psychHudRT;
    private RenderTargetBinding[] _psychHudPrevRT;
    private bool _psychHudLayerActive;
    private readonly Random _psychShakeRng = new();

    private void UpdatePsychCameras(float dt)
    {
        PsychCamera.HudCam.Update(dt);
        PsychCamera.OtherCam.Update(dt);
        if (!(_psychShakingGame)) PsychCamera.GameCam.Update(dt);
    }
    private bool _psychShakingGame => !_bzActive && PsychCamera.GameCam.ShakeDuration > 0f;

    private static Matrix PsychCamTransform(PsychCamera c, Vector2 shake)
    {
        if (c.Angle == 0f && c.X == 0f && c.Y == 0f && shake == Vector2.Zero) return Matrix.Identity;
        return Matrix.CreateTranslation(-640f, -360f, 0f) * Matrix.CreateRotationZ(MathHelper.ToRadians(FNF_MonoGame.Engine.PsychEngine.ClientPrefs.Data.CamAngles ? c.Angle : 0f))
             * Matrix.CreateTranslation(640f + c.X + shake.X, 360f + c.Y + shake.Y, 0f);
    }

    /// <summary>World pass: rotate/move camGame (applied after the zoom about the origin).</summary>
    internal Matrix PsychGameCamMatrix => PsychNotes ? PsychCamTransform(PsychCamera.GameCam, Vector2.Zero) : Matrix.Identity;

    internal Matrix PsychHudCamMatrix
    {
        get
        {
            if (!PsychNotes) return Matrix.Identity;
            var h = PsychCamera.HudCam;
            Vector2 shake = h.ShakeDuration > 0f
                ? new Vector2(((float)_psychShakeRng.NextDouble() * 2f - 1f) * h.ShakeIntensity * 1280f * (1f + _hudZoomAdd),
                              ((float)_psychShakeRng.NextDouble() * 2f - 1f) * h.ShakeIntensity * 720f * (1f + _hudZoomAdd))
                : Vector2.Zero;
            return PsychCamTransform(h, shake);
        }
    }

    internal bool PsychHudVisible => !PsychNotes || (PsychCamera.HudCam.Visible && PsychCamera.HudCam.Alpha > 0f);

    /// <summary>Before the camHUD pass: redirect to an offscreen target when camHUD.alpha &lt; 1.</summary>
    private void BeginPsychHudLayer()
    {
        _psychHudLayerActive = false;
        if (!PsychNotes || PsychCamera.HudCam.Alpha >= 1f) return;
        var gd = Game.GraphicsDevice;
        var pp = gd.PresentationParameters;
        int w = 1280, h = 720;
        var cur = gd.GetRenderTargets();
        if (cur.Length > 0 && cur[0].RenderTarget is RenderTarget2D crt) { w = crt.Width; h = crt.Height; }
        else { w = pp.BackBufferWidth; h = pp.BackBufferHeight; }
        if (_psychHudRT == null || _psychHudRT.Width != w || _psychHudRT.Height != h)
        {
            _psychHudRT?.Dispose();
            _psychHudRT = new RenderTarget2D(gd, w, h, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
        }
        _psychHudPrevRT = cur;
        var vp = gd.Viewport;
        gd.SetRenderTarget(_psychHudRT);
        gd.Viewport = new Viewport(vp.X, vp.Y, vp.Width, vp.Height);
        gd.Clear(Color.Transparent);
        _psychHudLayerActive = true;
    }

    private void EndPsychHudLayer(SpriteBatch sb)
    {
        if (!_psychHudLayerActive) return;
        var gd = Game.GraphicsDevice;
        var vp = gd.Viewport;
        if (_psychHudPrevRT != null && _psychHudPrevRT.Length > 0) gd.SetRenderTargets(_psychHudPrevRT); else gd.SetRenderTarget(null);
        gd.Viewport = vp;
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.AlphaBlend);
        sb.Draw(_psychHudRT, Vector2.Zero, Color.White * Math.Clamp(PsychCamera.HudCam.Alpha, 0f, 1f)); // RT holds premultiplied-like output of NonPremultiplied draws
        sb.End();
        _psychHudLayerActive = false;
    }

    /// <summary>FlxCamera.drawFX: flash then fade fill over the whole camera (screen space).</summary>
    private void DrawPsychCamFx(SpriteBatch sb, PsychCamera c, bool gameLayer)
    {
        if (!PsychNotes) return;
        float dark = gameLayer && c.Visible ? 1f - Math.Clamp(c.Alpha, 0f, 1f) : 0f;
        bool fx = c.FlashAlpha > 0f || c.FadeAlpha > 0f || dark > 0f || (gameLayer && !c.Visible);
        if (!fx) return;
        sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);
        var full = new Rectangle(-64, -64, 1280 + 128, 720 + 128);
        if (gameLayer && !c.Visible) sb.Draw(Assets.Pixel, full, Color.Black);
        else if (dark > 0f) sb.Draw(Assets.Pixel, full, new Color(Color.Black, dark));
        if (c.FlashAlpha > 0f) sb.Draw(Assets.Pixel, full, new Color(c.FlashColor, c.FlashColor.A / 255f * c.FlashAlpha));
        if (c.FadeAlpha > 0f) sb.Draw(Assets.Pixel, full, new Color(c.FadeColor, c.FadeColor.A / 255f * c.FadeAlpha));
        sb.End();
    }

    /// <summary>Psych 'Screen Shake' event: value1 = camGame "duration, intensity", value2 = camHUD.</summary>
    private void PsychScreenShake(string v1, string v2)
    {
        var cams = new[] { PsychCamera.GameCam, PsychCamera.HudCam };
        var vals = new[] { v1, v2 };
        for (int i = 0; i < 2; i++)
        {
            var parts = (vals[i] ?? "").Split(',');
            float dur = 0, inten = 0;
            if (parts.Length > 0) float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out dur);
            if (parts.Length > 1) float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out inten);
            if (float.IsNaN(dur)) dur = 0; if (float.IsNaN(inten)) inten = 0;
            if (dur > 0 && inten != 0) cams[i].Shake(inten, dur);
        }
    }
}
