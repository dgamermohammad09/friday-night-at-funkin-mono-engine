using System;
using FNF_MonoGame.Gameplay;
using CP = FNF_MonoGame.Engine.PsychEngine.ClientPrefs;

namespace FNF_MonoGame.Scenes;

// Psych Options > Gameplay: Rating Offset, Sick!/Good/Bad Hit Window, Safe Frames (backend/Rating.hx, Conductor.judgeNote,
// PlayState.set_playbackRate: safeZoneOffset = safeFrames / 60 * 1000 * playbackRate; popUpScore: noteDiff = |strumTime -
// songPosition + ratingOffset|, judged as noteDiff / playbackRate). The defaults (45 / 90 / 135 ms, 10 frames, 0 ms) keep the
// original V-Slice PBOT1 windows (45 / 90 / 135 / 160 ms) bit-for-bit.
public partial class PlayScene
{
    /// <summary>Song-time per real second (practice playback rate, 1 = normal).</summary>
    private float _practiceRate = 1f;
    internal float JudgeRate => _practiceRate > 0f ? _practiceRate : 1f;

    private static float PrefSickS => Math.Clamp(CP.Data.SickWindow, 15f, 45f) / 1000f;
    private static float PrefGoodS => Math.Clamp(CP.Data.GoodWindow, 15f, 90f) / 1000f;
    private static float PrefBadS => Math.Clamp(CP.Data.BadWindow, 15f, 135f) / 1000f;
    /// <summary>Hit window (seconds, real time): V-Slice 160 ms at the default 10 safe frames, else Psych safeFrames / 60.</summary>
    private static float PrefHitWindowS => CP.Data.SafeFrames == 10f ? 0.160f : Math.Clamp(CP.Data.SafeFrames, 2f, 10f) / 60f;
    /// <summary>Psych ratingOffset in seconds (positive = hit later).</summary>
    private static double PrefRatingOffsetS => CP.Data.RatingOffset / 1000.0;

    /// <summary>Once per input pass: hit/miss window in SONG time (scaled by the playback rate like Psych).</summary>
    private void ApplyHitWindowPrefs() => NoteField.HitWindowS = PrefHitWindowS * JudgeRate;

    /// <summary>|note - pos + ratingOffset| in real time (song-time difference / playbackRate).</summary>
    private float JudgeDiff(double noteTime, double pos) => (float)Math.Abs(noteTime - pos + PrefRatingOffsetS) / JudgeRate;
}
