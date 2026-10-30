using System;
using System.Collections.Generic;
using PNote = FNF_MonoGame.Engine.PsychEngine.Note;
using ChartNote = FNF_MonoGame.Gameplay.Note;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Psych Engine 1.0.4 note semantics for Psych-mod songs (states/PlayState.hx generateSong / goodNoteHit /
/// opponentNoteHit / noteMissCommon, objects/Note.hx):
///  - per-note hitHealth (default 0.02) / missHealth (0.1; key-press miss = pressMissDamage 0.05),
///    ignoreNote (botplay skips, passing is not a miss), hitCausesMiss (hit = noteMiss), noMissAnimation.
///  - cpuControlled still runs goodNoteHit: health += note.hitHealth * healthGain (score is not added).
///  - sustains are separate notes: round(sustainLength / stepCrochet) tail pieces at strumTime + stepCrochet * k
///    (k = 0..n-1); each piece calls goodNoteHit / opponentNoteHit with isSustainNote = true.
///    guitarHeroSustains (default ON): tail pieces give no health; a released sustain = one miss (rest ignored).
///  - built-in note types (Note.set_noteType): Hurt Note, Alt Animation, No Animation, GF Sing.
/// </summary>
public partial class PlayScene
{
    private static readonly bool DevMissLog = Environment.GetEnvironmentVariable("FNF_MISSLOG") == "1";

    /// <summary>True for songs loaded from a Psych mod folder.</summary>
    private bool PsychNotes => !string.IsNullOrEmpty(_modPath);

    /// <summary>ClientPrefs gameplay settings 'healthgain' / 'healthloss' (default 1).</summary>
    public float PsychHealthGain { get; set; } = 1f;
    public float PsychHealthLoss { get; set; } = 1f;

    private sealed class PsychSusTrack
    {
        public ChartNote Src; public int Lane; public bool Player; public int Next; public int Count;
        public double Start; public double Step; public bool Dropped; public PNote Live;
    }
    private readonly List<PsychSusTrack> _psychSus = new();
    private double _psyPrevPosSec;

    /// <summary>Note.set_noteType defaults for the built-in Psych note types (before onCreate scripts run).</summary>
    private void ApplyPsychNoteTypeDefaults()
    {
        if (!PsychNotes || _chart?.Notes == null) return;
        foreach (var n in _chart.Notes)
        {
            switch (n.NoteType)
            {
                case "Hurt Note":
                    n.IgnoreNote = n.IsPlayerNote; n.LowPriority = true; n.MissHealth = 0.1f; n.HitCausesMiss = true;
                    n.Rgb = new uint[] { 0xFF101010, 0xFFFF0000, 0xFF990022 }; // Note.hx 206-213: rgbShader r/g/b + electric splash
                    n.SplashTexture = "noteSplashes/noteSplashes-electric"; break;
                case "No Animation":
                    n.NoAnimation = true; n.NoMissAnimation = true; break;
                case "GF Sing":
                    n.GfNote = true; break;
            }
        }
    }

    private void PsychBotplayHitHealth(PNote note)
    {
        if (!PsychNotes || note == null) return;
        _health = Math.Clamp(_health + note.HitHealth * PsychHealthGain, HEALTH_MIN, HEALTH_MAX);
    }

    /// <summary>goodNoteHit with hitCausesMiss: counts as noteMiss(note) (Hurt Note: 'hurt' anim). Returns true if handled.</summary>
    private bool PsychHitCausesMiss(int lane, PNote note)
    {
        if (!PsychNotes || note == null || !note.HitCausesMiss) return false;
        _noteField.RemoveNote(note);
        OnNoteMiss(lane, note.Kind, note);
        if (!note.NoMissAnimation && note.NoteType == "Hurt Note" && _boyfriend != null && _boyfriend.HasAnimation("hurt"))
            _boyfriend.PlayAnimationLocked("hurt");
        return true;
    }

    private double PsychStepCrochetAt(double t)
    {
        float bpm = _chart?.BPM ?? 100f;
        if (_chart?.BPMChanges != null)
            foreach (var c in _chart.BPMChanges) { if (c.Time <= t + 1e-6) bpm = c.Bpm; else break; }
        return 60.0 / bpm / 4.0;
    }

    /// <summary>Head of a sustain was hit: schedule its Psych tail pieces.</summary>
    private void PsychStartSustainTrack(PNote note, bool player)
    {
        if (!PsychNotes || note == null || note.SustainLength <= 0) return;
        double step = PsychStepCrochetAt(note.Time);
        int count = (int)Math.Round(note.SustainLength / step, MidpointRounding.AwayFromZero);
        if (count <= 0) return;
        _psychSus.Add(new PsychSusTrack { Src = note.Source, Lane = note.Lane % 4, Player = player, Count = count, Start = note.Time, Step = step, Live = note });
        UpdatePsychSustainPieces();
    }

    /// <summary>Player released the hold (DropPlayerHold): Psych counts the next tail piece as a miss.</summary>
    private bool PsychDropSustain(int lane)
    {
        if (!PsychNotes) return false;
        foreach (var tr in _psychSus)
            if (tr.Player && tr.Lane == lane && !tr.Dropped && tr.Next < tr.Count)
            {
                tr.Dropped = true;
                _psMissIsSustain = true; OnNoteMiss(lane, tr.Live?.Kind ?? "", tr.Live); _psMissIsSustain = false;
            }
        return true;
    }

    /// <summary>Every frame: fire goodNoteHit / opponentNoteHit (isSustainNote = true) for due tail pieces.</summary>
    private void UpdatePsychSustainPieces()
    {
        if (_psychSus.Count == 0) return;
        // Note.update (in super.update, previous frame's songPosition): opponent pieces wasGoodHit at strumTime <= pos;
        // player pieces (earlyHitMult 0) canBeHit only once strumTime < pos.
        double pos = _psyPrevPosSec;
        for (int i = _psychSus.Count - 1; i >= 0; i--)
        {
            var tr = _psychSus[i];
            while (!tr.Dropped && tr.Next < tr.Count && (tr.Player ? pos > tr.Start + tr.Step * tr.Next : pos >= tr.Start + tr.Step * tr.Next))
            {
                tr.Next++;
                string type = tr.Src?.NoteType ?? "";
                int idx = PsychGroupIndexOfPiece(tr.Src, tr.Next - 1);
                if (tr.Player) { CallOnLuas("goodNoteHitPre", idx, tr.Lane, type, true); CallOnLuas("goodNoteHit", idx, tr.Lane, type, true); }
                else if (!(tr.Src?.IgnoreNote ?? false)) { CallOnLuas("opponentNoteHitPre", idx, tr.Lane, type, true); CallOnLuas("opponentNoteHit", idx, tr.Lane, type, true); }
            }
            if (tr.Dropped || tr.Next >= tr.Count) _psychSus.RemoveAt(i);
        }
    }

    /// <summary>Psych PlayState.startVideo: plays mods/&lt;mod&gt;/videos/&lt;name&gt;.mp4; at the end startAndEnd()
    /// (startCountdown, or endSong when the song is ending). Returns false when the file cannot be played.</summary>
    public bool PsychStartVideo(string name, bool canSkip, bool forMidSong)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var type = forMidSong ? VideoCutsceneType.Midsong : (_forceEndSong ? VideoCutsceneType.Ending : VideoCutsceneType.Starting);
        bool hudHidden = _w6HudHidden;
        bool ok = VideoPlay(name, type);
        if (ok)
        {
            // VideoSprite.new: Psych never hides camHUD for a video (unlike V-Slice VideoCutscene); the black cover
            // exists only when !isWaiting (forMidSong = false).
            _w6HudHidden = hudHidden;
            _vidBlackVisible = !forMidSong;
        }
        if (!ok) Console.WriteLine($"[FUNKIN-LUA] startVideo: video file not found or not playable: videos/{name}.mp4 (needs H.264 mp4)");
        return ok;
    }
}
