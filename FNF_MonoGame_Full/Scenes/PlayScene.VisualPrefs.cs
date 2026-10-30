using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using CP = FNF_MonoGame.Engine.PsychEngine.ClientPrefs;

namespace FNF_MonoGame.Scenes;

// Psych / Psych Online visual + camera prefs (PSO PlayState.update 3345-3372, generateStaticArrows 3013-3060, Note.hx 331,
// Psych 1.0.4 PlayState create 481-547 / doScoreBop 1181). Every default keeps the current (original) look.
public partial class PlayScene
{
    // ---- Camera Movement (PSO: !camMovement -> moveCamera(playsAsBF(), true) every frame = camera stays on GF) ----
    private int CamMovementFocus(int focusChar)
    {
        if (CP.Data.CamMovement) return focusChar;
        return _girlfriend != null ? 2 : 1;
    }

    // ---- Note Underlay (PSO 'All-In-One': black column behind the player strums, alpha = min(1, opacity * strum.alpha) *
    // nearNoteValue; nearNoteValue fades in/out over NEAR_NOTE_DISTANCE 1500 ms when a player note is that close) ----
    private float _nearNoteValue;
    private bool _replayPlaying;
    private void UpdateNoteUnderlay(float dt)
    {
        if (CP.Data.NoteUnderlayOpacity <= 0f || _noteField == null || _conductor == null) return;
        bool near = _noteField.HasPlayerNoteWithin(_conductor.SongPosition, 1.5);
        float step = dt * (1000f / 1500f);
        _nearNoteValue = near ? Math.Min(1f, _nearNoteValue + step) : Math.Max(0f, _nearNoteValue - step);
    }

    private void DrawNoteUnderlay(SpriteBatch sb, int playerStartX, int laneSpacing)
    {
        float op = CP.Data.NoteUnderlayOpacity;
        if (op <= 0f || _replayPlaying) return;
        var m = StrumMods[4];
        if (!m.Visible) return;
        float a = Math.Min(1f, op * m.Alpha) * _nearNoteValue;
        if (a <= 0f) return;
        int x = playerStartX + (int)m.OffsetX;
        sb.Draw(Assets.Pixel, new Rectangle(x, -720, laneSpacing * 4, 720 * 3), new Color(Color.Black, a));
    }

    // ---- Trail Note Opacity / Hold + Note Splash Opacity ----
    /// <summary>Hold trail alpha factor: Psych notes = holdAlpha (Note.hx alpha = multAlpha = holdAlpha, 0.6 default);
    /// V-Slice trails stay opaque at the default and scale relative to it.</summary>
    private float HoldTrailMul => PsychNotes ? Math.Clamp(CP.Data.HoldAlpha, 0f, 1f) : Math.Clamp(CP.Data.HoldAlpha / 0.6f, 0f, 1f);
    /// <summary>V-Slice hold cover alpha factor (PSO holdSplashAlpha, 0.6 = original opaque cover).</summary>
    private static float HoldSplashMul => Math.Clamp(CP.Data.HoldSplashAlpha / 0.6f, 0f, 1f);
    /// <summary>V-Slice note splash alpha factor (Psych splashAlpha, 0.6 = original); Psych notes carry the alpha themselves.</summary>
    private float NoteSplashMul => PsychNotes ? 1f : Math.Clamp(CP.Data.SplashAlpha / 0.6f, 0f, 1f);

    // ---- Disable Note Modchart (PSO StrumNote.forceShow): player strums (and opponent ones with Opponent Notes on) keep
    // their generateStaticArrows position/alpha whatever scripts do ----
    private readonly (float x, float y, float ang, float a, float dir, bool vis)[] _strumLock = MakeStrumLock();
    private static (float, float, float, float, float, bool)[] MakeStrumLock()
    {
        var r = new (float, float, float, float, float, bool)[8];
        for (int i = 0; i < 8; i++) r[i] = (0f, 0f, 0f, 1f, 90f, true);
        return r;
    }
    private void StrumLockSnapshot()
    {
        for (int i = 0; i < 8; i++) { var m = StrumMods[i]; _strumLock[i] = (m.OffsetX, m.OffsetY, m.Angle, m.Alpha, m.Direction, m.Visible); }
    }
    private void ApplyStrumLock()
    {
        if (!CP.Data.DisableStrumMovement || _mpActive) return;
        for (int i = CP.Data.OpponentStrums ? 0 : 4; i < 8; i++)
        {
            var m = StrumMods[i]; var s = _strumLock[i];
            m.OffsetX = s.x; m.OffsetY = s.y; m.Angle = s.ang; m.Alpha = s.a; m.Direction = s.dir; m.Visible = s.vis;
        }
    }

    // ---- Score Text Zoom on Hit (Psych doScoreBop: scale 1.075 -> 1 in 0.2 s on every non-miss player hit) ----
    private float _psychScoreBopT = -1f;
    private void PsychScoreBop() { if (UsePsychHud && CP.Data.ScoreZoom) _psychScoreBopT = 0f; }
    private float PsychScoreScale
    {
        get
        {
            if (_psychScoreBopT < 0f) return 1f;
            float t = Math.Min(1f, _psychScoreBopT / 0.2f);
            return 1.075f + (1f - 1.075f) * t;
        }
    }

    /// <summary>Per frame (PlayScene.Update): prefs that animate.</summary>
    private void UpdateVisualPrefs(float dt)
    {
        UpdateNoteUnderlay(dt);
        if (_psychScoreBopT >= 0f) { _psychScoreBopT += dt; if (_psychScoreBopT >= 0.2f) _psychScoreBopT = -1f; }
        ApplyStrumLock();
    }

    // ---- Time Bar (Psych timeBarType: Time Left / Time Elapsed / Song Name / Disabled) ----
    private static string PsychTimeBarType => CP.Data.TimeBarType ?? "Time Left";
    private void ApplyPsychHudPrefs()
    {
        if (!PsychNotes) return;
        float hba = Math.Clamp(CP.Data.HealthBarAlpha, 0f, 1f);
        _pHealthBar.alpha = hba; _pIconP1.alpha = hba; _pIconP2.alpha = hba;  // PlayState create: alpha = healthBarAlpha
        bool showTime = PsychTimeBarType != "Disabled";
        _pTimeTxt.visible = showTime; _pTimeBar.visible = showTime;
        if (PsychTimeBarType == "Song Name") _pTimeTxt.y += 3f;
    }
    private string PsychTimeText(out int size)
    {
        size = 32;
        string t = PsychTimeBarType;
        if (t == "Song Name") { size = 24; return string.IsNullOrEmpty(_psychSongDisplayName) ? _songName : _psychSongDisplayName; }
        double len = Audio.MusicLength;
        double cur = Math.Max(0, _conductor.SongPosition);
        double calc = t == "Time Elapsed" ? cur : len - cur;
        int secs = Math.Max(0, (int)Math.Floor(calc));
        return $"{secs / 60}:{secs % 60:00}";
    }
    private string _psychSongDisplayName => _chart?.SongName;
}

public partial class PlayScene
{
    /// <summary>Health Bar Opacity (Psych healthBarAlpha) for the V-Slice HUD bar + icons (1 = original).</summary>
    private static float HudBarAlpha => Math.Clamp(CP.Data.HealthBarAlpha, 0f, 1f);
}
