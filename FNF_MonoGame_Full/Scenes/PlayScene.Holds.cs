using System;
using PNote = FNF_MonoGame.Engine.PsychEngine.Note;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Player sustain (hold note) logic ported from V-Slice 0.8.1:
///  - Strumline.updateNotes(): once songPosition &gt; strumTime, a hit hold whose key is not held is DROPPED
///    (missedNote = true, alpha = 0 -> the rest of the trail is hidden). Before strumTime (early hit) nothing
///    is checked; the trail keeps scrolling normally. While held: sustainLength = strumTime + fullSustainLength - songPosition;
///    complete when sustainLength &lt;= 0.
///  - PlayState.processNotes(): while hitNote &amp;&amp; !missedNote &amp;&amp; sustainLength &gt; 0:
///      health += HEALTH_HOLD_BONUS_PER_SECOND * elapsed;  songScore += Std.int(SCORE_HOLD_BONUS_PER_SECOND * elapsed)
///    On drop (missedNote &amp;&amp; !handledMiss), if sustainLength &gt; HOLD_DROP_PENALTY_THRESHOLD_MS (160):
///      score += Std.int(-125 * remainingSec), health += 0 (HEALTH_HOLD_DROP_PENALTY_* are 0 in 0.8.1),
///      combo break (no miss tally), vocals.playerVolume = 0, missnote1-3 at random 0.5-0.6 volume,
///      BF onNoteHoldDrop -> playSingAnimation(dir, miss=true).
///  - A note missed entirely also flags its hold missedNote -> the same drop penalty on top of the miss.
/// </summary>
public partial class PlayScene
{
    /// <summary>Remaining hold length in ms as last updated (holdNote.sustainLength in the original).</summary>
    private readonly double[] _holdRemainingMs = new double[4];

    /// <summary>Dev hook FNF_DEV_TAPHOLD=1: in botplay, every hold note is released right after being hit
    /// (reproduces a player tapping a sustain; used to verify against the original).</summary>
    private static readonly bool DevTapHolds = Environment.GetEnvironmentVariable("FNF_DEV_TAPHOLD") == "1";

    public string DevScoreInfo => $"score={_score} health={_health:F4} combo={_combo} misses={_misses} holding={string.Join(',', _holdingNote)} hud={_hudDbg} hudZoomAdd={_hudZoomAdd:F4} camFollow={_camTargetWorldX:F1},{_camTargetWorldY:F1} dcz={_defaultCamZoom:F4} hudAngle={FNF_MonoGame.Engine.PsychEngine.PsychCamera.HudCam.Angle:F3} hudY={FNF_MonoGame.Engine.PsychEngine.PsychCamera.HudCam.Y:F3} gameAngle={FNF_MonoGame.Engine.PsychEngine.PsychCamera.GameCam.Angle:F3}";

    private const double HOLD_DROP_PENALTY_THRESHOLD_MS = 160.0;
    private const float SCORE_HOLD_BONUS_PER_SECOND = 250f;
    private const float SCORE_HOLD_DROP_PENALTY_PER_SECOND = -125f;

    private void UpdatePlayerHold(int lane, double pos, bool held, bool scoring)
    {
        if (!_holdingNote[lane]) return;
        var note = _holdNoteRef[lane];
        double end = _holdNoteEndTime[lane];
        double strumTime = note?.Time ?? (end - 0.0);

        // Strumline.updateNotes: drop check only once the strum time has passed.
        if (pos > strumTime && !held)
        {
            DropPlayerHold(lane, scoring);
            return;
        }

        if (pos > strumTime)
        {
            _holdRemainingMs[lane] = (end - pos) * 1000.0;
            if (_holdRemainingMs[lane] <= 0)
            {
                // Hold completed.
                _holdingNote[lane] = false;
                _holdCoverTimer[lane] = 0;
                if (note != null) _noteField.RemoveHoldNote(note);
                _holdNoteRef[lane] = null;
                return;
            }
        }

        // PlayState.processNotes: "Make sure the player keeps singing while the note is held by the bot."
        if (_botplay) _boyfriend?.KeepSinging();
        // PlayState.processNotes: bonus every frame while hit && !missed && sustainLength > 0 (also before strumTime).
        if (scoring && !PsychNotes) // Psych has no per-second hold bonus (tail pieces instead)
        {
            _score += (int)(SCORE_HOLD_BONUS_PER_SECOND * _lastDelta);
            _health = Math.Clamp(_health + (6f / 100f * HEALTH_MAX * _lastDelta), HEALTH_MIN, HEALTH_MAX);
        }
    }

    private void DropPlayerHold(int lane, bool scoring)
    {
        var note = _holdNoteRef[lane];
        double remainingMs = _holdRemainingMs[lane];

        _holdingNote[lane] = false;
        _holdCoverTimer[lane] = 0;       // cover killed (no end animation)
        _playerConfirmTimer[lane] = 0f;  // playStatic(direction)
        VsOnHoldDropped(lane);           // cover.kill() (PlayScene.VSliceStrumline.cs)
        if (note != null) _noteField.RemoveHoldNote(note); // holdNote.alpha = 0: rest of the trail is hidden
        _holdNoteRef[lane] = null;

        if (DevTapHolds) Console.WriteLine($"[HOLD] drop lane={lane} t={_conductor.SongPosition:F3} remainingMs={remainingMs:F1} scoring={scoring} score={_score}");
        if (!scoring) return;
        if (PsychDropSustain(lane)) return;
        if (remainingMs > HOLD_DROP_PENALTY_THRESHOLD_MS)
        {
            ApplyHoldDropPenalty(lane, remainingMs, note?.Kind);
        }
    }

    /// <summary>Called for every player note that left the hit window unhit (after OnNoteMiss).</summary>
    private void OnMissedHoldNote(PNote note)
    {
        if (note == null || note.SustainLength <= 0 || _botplay || PsychNotes) return;
        double lenMs = note.SustainLength * 1000.0;
        if (lenMs > HOLD_DROP_PENALTY_THRESHOLD_MS)
            ApplyHoldDropPenalty(note.Lane % 4, lenMs, note.Kind, alreadyMissed: true);
    }

    private void ApplyHoldDropPenalty(int lane, double remainingMs, string kind, bool alreadyMissed = false)
    {
        // applyScore(Std.int(SCORE_HOLD_DROP_PENALTY_PER_SECOND * remainingSec), '', healthChange = 0, isComboBreak = true)
        _score += (int)(SCORE_HOLD_DROP_PENALTY_PER_SECOND * (remainingMs / 1000.0));
        GfComboOnBreak(_combo); NsComboBreak(_combo);   // onNoteHoldDrop GF branch (isComboBreak)
        _combo = 0;
        _lastComboDisplay = 0;
        // event.playSound: vocals.playerVolume = 0; FunkinSound.playOnce(missnote1-3, random 0.5..0.6)
        Audio.SetPlayerVoiceVolume(0f);
        if (!alreadyMissed)
        {
            int missNum = _random.Next(1, 4);
            Audio.PlaySound($"missnote{missNum}", 0.5f + (float)_random.NextDouble() * 0.1f);
            // BaseCharacter.onNoteHoldDrop: playSingAnimation(direction, miss = true)
            if (!Is2hotSpecialNoteKind(kind) && !SsfNoteMiss(lane, kind)) _boyfriend.Miss(lane);
        }
    }
}
