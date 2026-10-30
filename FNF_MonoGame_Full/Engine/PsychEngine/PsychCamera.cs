using System;
using Microsoft.Xna.Framework;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine camera shim (camGame / camHUD / camOther, FlxCamera subset).
/// Zoom is bridged to the engine zoom by PlayScene; x/y/angle/alpha/visible are applied by
/// Scenes/PlayScene.PsychCameras.cs when drawing; flash/fade follow flixel 5 FlxCamera.flash/fade/updateFlash/updateFade
/// (flash alpha 1 -> 0 over duration; a finished fade-out keeps its fill).
/// </summary>
public partial class PsychCamera
{
    public static PsychCamera GameCam { get; } = new();
    public static PsychCamera HudCam { get; } = new();
    public static PsychCamera OtherCam { get; } = new();

    public float X { get; set; }
    public float Y { get; set; }
    private float _zoom = 1f;
    public Func<float> ZoomGet { get; set; }
    public Action<float> ZoomSet { get; set; }
    public float Zoom { get => ZoomGet != null ? ZoomGet() : _zoom; set { if (ZoomSet != null) ZoomSet(value); else _zoom = value; } }
    public float zoom { get => Zoom; set => Zoom = value; }
    public bool Visible { get; set; } = true;
    public float Angle { get; set; }
    public float Alpha { get; set; } = 1f;
    public float FollowLerp { get; set; } = 0.04f;
    public float FollowLead { get; set; } = 0f;

    public float TargetX { get; set; }
    public float TargetY { get; set; }
    public bool FollowTarget { get; set; }

    public float ShakeIntensity { get; private set; }
    public float ShakeDuration { get; private set; }
    public Color FlashColor { get; private set; } = Color.White;
    public float FlashDuration { get; private set; }
    public bool FlashForced { get; private set; }
    /// <summary>flixel _fxFlashAlpha (1 at start, 0 when done).</summary>
    public float FlashAlpha { get; private set; }
    public float FadeDuration { get; private set; }
    public Color FadeColor { get; private set; } = Color.Black;
    public bool FadeIn { get; private set; }
    public bool Fading { get; private set; }
    /// <summary>flixel _fxFadeAlpha.</summary>
    public float FadeAlpha { get; private set; }
    private Action _fadeComplete, _flashComplete;

    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;

    public static void ResetAll()
    {
        foreach (var c in new[] { GameCam, HudCam, OtherCam })
        {
            c.X = c.Y = 0; c.Angle = 0; c.Alpha = 1f; c.Visible = true; c._zoom = 1f;
            c.FlashAlpha = 0; c.FadeAlpha = 0; c.FadeDuration = 0; c.Fading = false; c.ShakeDuration = 0; c.ShakeIntensity = 0;
            c._fadeComplete = c._flashComplete = null;
        }
    }

    public void Follow(float x, float y, float lerp = 0.04f) { TargetX = x; TargetY = y; FollowLerp = lerp; FollowTarget = true; }
    public void StopFollow() { FollowTarget = false; }

    public void Update(float deltaTime)
    {
        if (FollowTarget) { X += (TargetX - X) * FollowLerp; Y += (TargetY - Y) * FollowLerp; }
        UpdateFlashSprite();
        if (ShakeDuration > 0) ShakeDuration -= deltaTime; else ShakeIntensity = 0;
        if (FlashAlpha > 0f)
        {
            FlashAlpha -= deltaTime / FlashDuration;
            if (FlashAlpha <= 0f) { FlashAlpha = 0f; var cb = _flashComplete; _flashComplete = null; cb?.Invoke(); }
        }
        if (Fading)
        {
            if (FadeIn)
            {
                FadeAlpha -= deltaTime / FadeDuration;
                if (FadeAlpha <= 0f) { FadeAlpha = 0f; CompleteFade(); }
            }
            else
            {
                FadeAlpha += deltaTime / FadeDuration;
                if (FadeAlpha >= 1f) { FadeAlpha = 1f; CompleteFade(); }
            }
        }
    }

    private void CompleteFade() { Fading = false; var cb = _fadeComplete; _fadeComplete = null; cb?.Invoke(); }

    public void Shake(float intensity, float duration) { if (!ClientPrefs.Data.CamShakes) return; /* PSO Camera Shakes */ ShakeIntensity = intensity; ShakeDuration = duration; }

    public void Flash(Color color, float duration) => Flash(color, duration, null, false);
    public void Flash(Color color, float duration, bool forced) => Flash(color, duration, null, forced);
    public void Flash(Color color, float duration, Action onComplete, bool forced)
    {
        if (!forced && FlashAlpha > 0f) return;
        FlashColor = color; FlashDuration = Math.Max(duration, 0.000001f); FlashForced = forced; FlashAlpha = 1f; _flashComplete = onComplete;
    }

    public void Fade(Color color, float duration, bool fadeIn) => Fade(color, duration, fadeIn, null, false);
    public void Fade(Color color, float duration, bool fadeIn, bool forced) => Fade(color, duration, fadeIn, null, forced);
    public void Fade(Color color, float duration, bool fadeIn, Action onComplete, bool forced)
    {
        if (!forced && Fading) return;
        FadeColor = color; FadeDuration = Math.Max(duration, 0.000001f); FadeIn = fadeIn; Fading = true; _fadeComplete = onComplete;
        FadeAlpha = fadeIn ? 0.999999f : 0.000001f;
    }
    public void StopFX() { FlashAlpha = 0; FadeAlpha = 0; Fading = false; ShakeDuration = 0; }

    public Vector2 GetPosition() => new(X, Y);
}
