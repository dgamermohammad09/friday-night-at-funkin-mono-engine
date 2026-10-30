using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using CP = FNF_MonoGame.Engine.PsychEngine.ClientPrefs;

namespace FNF_MonoGame.Scenes;

// Rating / combo popup settings (Psych Online PlayState.popUpScore / ClientPrefs):
//  Combo Stacking (Psych + V-Slice PopUpStuff: every hit adds new sprites that keep falling) - default ON = original look,
//  Rating Color (COLOR_SICK 0x6CFD8E, GOOD 0x68D5FD, BAD 0xFCD768, SHIT 0xFC6B68; combo tinted by the worst rating while
//  misses == 0), Disable Combo Rating, Disable Combo Counter, Show Note Timing (FlxText size 20, outline 1, red/lime/cyan,
//  "<ms>ms" = roundDecimal(noteDiffNoAbs / playbackRate, 3), acceleration 600, velocity -150, alpha tween 0.2 s after a crochet).
public partial class PlayScene
{
    private struct StackedPopup
    {
        public string Rating; public float RY, RVel, RAlpha, Timer;
        public int Combo; public float CY, CVel, CAlpha;
    }
    private readonly List<StackedPopup> _stackedPopups = new(24);

    private static readonly Color ColSick = new(0x6C, 0xFD, 0x8E), ColGood = new(0x68, 0xD5, 0xFD),
                                  ColBad = new(0xFC, 0xD7, 0x68), ColShit = new(0xFC, 0x6B, 0x68);

    /// <summary>Before a new rating popup replaces the current one: keep the old one falling (comboStacking).</summary>
    private void PopupPushStack()
    {
        if (!CP.Data.ComboStacking || _ratingTimer <= 0f || string.IsNullOrEmpty(_lastRating)) return;
        if (_stackedPopups.Count >= 24) _stackedPopups.RemoveAt(0);
        _stackedPopups.Add(new StackedPopup
        {
            Rating = _lastRating, RY = _ratingY, RVel = _ratingVelY, RAlpha = _ratingAlpha, Timer = _ratingTimer,
            Combo = _lastComboDisplay, CY = _comboY, CVel = _comboVelY, CAlpha = _comboAlpha,
        });
    }

    private void UpdateStackedPopups(float dt)
    {
        for (int i = _stackedPopups.Count - 1; i >= 0; i--)
        {
            var p = _stackedPopups[i];
            p.Timer -= dt;
            if (p.Timer <= 0f) { _stackedPopups.RemoveAt(i); continue; }
            p.RVel += 550f * dt; p.RY += p.RVel * dt;
            p.CVel += 550f * dt; p.CY += p.CVel * dt;
            if (p.Timer < 0.2f) { p.RAlpha = p.Timer / 0.2f; p.CAlpha = p.Timer / 0.2f; }
            _stackedPopups[i] = p;
        }
        UpdateNoteTimingPopup(dt);
    }

    /// <summary>Draw the stacked (older) popups under the current one, reusing DrawRating with swapped state.</summary>
    private void DrawStackedPopups(SpriteBatch sb)
    {
        if (_stackedPopups.Count == 0) return;
        string r0 = _lastRating; float ry = _ratingY, ra = _ratingAlpha, cy = _comboY, ca = _comboAlpha; int c0 = _lastComboDisplay;
        foreach (var p in _stackedPopups)
        {
            _lastRating = p.Rating; _ratingY = p.RY; _ratingAlpha = p.RAlpha; _comboY = p.CY; _comboAlpha = p.CAlpha; _lastComboDisplay = p.Combo;
            DrawRating(sb);
        }
        _lastRating = r0; _ratingY = ry; _ratingAlpha = ra; _comboY = cy; _comboAlpha = ca; _lastComboDisplay = c0;
    }

    private static bool PopupShowRating => !CP.Data.DisableComboRating;
    private static bool PopupShowCombo => !CP.Data.DisableComboCounter;

    /// <summary>Rating sprite tint (Rating Color option), white by default.</summary>
    private static Color PopupRatingTint(string rating)
    {
        if (!CP.Data.ColorRating) return Color.White;
        return rating switch { "SICK!!" => ColSick, "GOOD!" => ColGood, "BAD" => ColBad, "SHIT" => ColShit, _ => Color.White };
    }

    /// <summary>Combo digits tint: worst rating so far while there are no misses (Rating Color option).</summary>
    private Color PopupComboTint()
    {
        if (!CP.Data.ColorRating || _misses != 0) return Color.White;
        if (_tallyShit > 0) return ColShit;
        if (_tallyBad > 0) return ColBad;
        if (_tallyGood > 0) return ColGood;
        if (_tallySick > 0) return ColSick;
        return Color.White;
    }

    // ---- Show Note Timing ----
    private string _ntText; private Color _ntColor;
    private float _ntX, _ntY, _ntVX, _ntVY, _ntAlpha, _ntDelay, _ntFade = -1f;

    /// <summary>signedMs = strumTime - songPosition + ratingOffset (positive = early), in song ms.</summary>
    private void PopupNoteTiming(string rating, double signedMs)
    {
        if (!CP.Data.ShowNoteTiming || CP.Data.HideHud) return;
        _ntColor = rating switch { "SICK!!" => Color.Cyan, "GOOD!" => new Color(0, 255, 0), _ => Color.Red };
        double v = Math.Round(signedMs / JudgeRate, 3, MidpointRounding.AwayFromZero);
        _ntText = v.ToString("0.###", CultureInfo.InvariantCulture) + "ms";
        float ratingCx = FNFGame.SCREEN_WIDTH * 0.474f + _popUpOffsets.X;
        _ntX = ratingCx + 4f;
        _ntY = (FNFGame.SCREEN_HEIGHT - 22f) / 2f + 60f + 60f + _popUpOffsets.Y;
        _ntVY = -150f; _ntVX = _random.Next(1, 11) * JudgeRate;
        _ntAlpha = 1f; _ntDelay = (float)(60.0 / Math.Max(1.0, _conductor.BPM)); _ntFade = 0f;
    }

    private void UpdateNoteTimingPopup(float dt)
    {
        if (_ntText == null || _ntFade < 0f) return;
        _ntVY += 600f * dt; _ntY += _ntVY * dt; _ntX += _ntVX * dt;
        if (_ntDelay > 0f) { _ntDelay -= dt; return; }
        _ntFade += dt;
        _ntAlpha = Math.Max(0f, 1f - _ntFade / 0.2f);
        if (_ntAlpha <= 0f) _ntFade = -1f;
    }

    private void DrawNoteTimingPopup(SpriteBatch sb)
    {
        if (_ntText == null || _ntFade < 0f || _ntAlpha <= 0f) return;
        DrawPsychText(sb, 20, _ntText, _ntX, _ntY, 0f, _ntColor, 1f, _ntAlpha);
    }
}
