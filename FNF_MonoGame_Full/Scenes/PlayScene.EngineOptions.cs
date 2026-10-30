using System;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

// Engine options that touch gameplay (Options > Preferences). Defaults (100 % HUD, 0 % safe area) give exactly the
// original layout: the factor multiplies the camHUD zoom, which flixel applies about the screen centre, so the whole
// HUD (strums, notes, health bar, icons, score) shrinks towards the centre and stays inside a TV's safe area.
public partial class PlayScene
{
    internal static float HudUserScale
    {
        get
        {
            var d = HighscoreManager.Data;
            float s = Math.Clamp(d.HudScale <= 0f ? 1f : d.HudScale, 0.5f, 1f);
            float m = Math.Clamp(d.SafeArea, 0f, 0.1f);
            return s * (1f - 2f * m);
        }
    }

    /// <summary>Psych "Hitsound Volume" (Options > Gameplay): tick on every player hit (PlayState.goodNoteHit, not for botplay).</summary>
    private void EngineHitsound(bool botplayHit)
    {
        float v = FNF_MonoGame.Engine.PsychEngine.ClientPrefs.Data.HitsoundVolume;
        if (botplayHit || v <= 0f) return;
        Audio.PlaySound("psych/hitsound", v);
    }
}
