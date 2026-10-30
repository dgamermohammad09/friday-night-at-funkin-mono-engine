using System;
using System.Collections.Generic;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine Conductor — handles BPM, timing, step/beat tracking.
/// Direct port of backend/Conductor.hx.
/// </summary>
public class PsychConductor
{
    public class BPMChangeEvent
    {
        public int StepTime { get; set; }
        public float SongTime { get; set; }
        public float BPM { get; set; }
        public float StepCrochet { get; set; }
    }

    private float _bpm = 100;
    public float BPM
    {
        get => _bpm;
        set
        {
            _bpm = value;
            Crochet = CalculateCrochet(value);
            StepCrochet = Crochet / 4f;
        }
    }

    public float Crochet { get; private set; } // beat length in ms
    public float StepCrochet { get; private set; } // step length in ms
    public float SongPosition { get; set; }
    public static float crochet { get; set; }
    // backend.Conductor statics read by scripts (getPropertyFromClass('backend.Conductor', 'bpm') / HScript Conductor.bpm)
    public static float bpm { get; set; } = 100f;
    public static float stepCrochet => crochet / 4f;
    public static float offset { get; set; }
    public static float safeZoneOffset => ClientPrefs.Data.SafeFrames / 60f * 1000f;
    public static float songPosition { get; set; }
    public float Offset { get; set; }
    public float SafeZoneOffset { get; set; }

    public List<BPMChangeEvent> BPMChangeMap { get; private set; } = new();

    // Derived timing
    public int CurrentStep => (int)GetStep(SongPosition);
    public int CurrentBeat => (int)(GetStep(SongPosition) / 4);
    public float CurrentDecStep => (float)GetStep(SongPosition);
    public float CurrentDecBeat => (float)(GetStep(SongPosition) / 4);

    public PsychConductor(float bpm = 100)
    {
        BPM = bpm;
    }

    /// <summary>
    /// Judge a note hit timing against rating windows.
    /// </summary>
    public static Rating JudgeNote(List<Rating> ratings, float diff)
    {
        float absDiff = Math.Abs(diff);
        for (int i = 0; i < ratings.Count - 1; i++)
        {
            if (absDiff <= ratings[i].HitWindow)
                return ratings[i];
        }
        return ratings[^1]; // worst rating
    }

    public float GetCrotchetAtTime(float time)
    {
        var lastChange = GetBPMFromSeconds(time);
        return lastChange.StepCrochet * 4;
    }

    public BPMChangeEvent GetBPMFromSeconds(float time)
    {
        var lastChange = new BPMChangeEvent
        {
            StepTime = 0,
            SongTime = 0,
            BPM = _bpm,
            StepCrochet = StepCrochet
        };
        foreach (var change in BPMChangeMap)
        {
            if (time >= change.SongTime)
                lastChange = change;
        }
        return lastChange;
    }

    public BPMChangeEvent GetBPMFromStep(float step)
    {
        var lastChange = new BPMChangeEvent
        {
            StepTime = 0,
            SongTime = 0,
            BPM = _bpm,
            StepCrochet = StepCrochet
        };
        foreach (var change in BPMChangeMap)
        {
            if (change.StepTime <= step)
                lastChange = change;
        }
        return lastChange;
    }

    public float BeatToSeconds(float beat)
    {
        float step = beat * 4;
        var lastChange = GetBPMFromStep(step);
        return lastChange.SongTime + ((step - lastChange.StepTime) / (lastChange.BPM / 60f) / 4f) * 1000f;
    }

    public double GetStep(float time)
    {
        var lastChange = GetBPMFromSeconds(time);
        return lastChange.StepTime + (time - lastChange.SongTime) / lastChange.StepCrochet;
    }

    public double GetStepRounded(float time)
    {
        var lastChange = GetBPMFromSeconds(time);
        return lastChange.StepTime + Math.Floor(time - lastChange.SongTime) / lastChange.StepCrochet;
    }

    public double GetBeat(float time) => GetStep(time) / 4.0;
    public int GetBeatRounded(float time) => (int)Math.Floor(GetStepRounded(time) / 4.0);

    /// <summary>
    /// Build the BPM change map from a Psych Engine song.
    /// </summary>
    public void MapBPMChanges(PsychSong song)
    {
        BPMChangeMap.Clear();

        float curBPM = song.BPM;
        int totalSteps = 0;
        float totalPos = 0;

        for (int i = 0; i < song.Notes.Count; i++)
        {
            var section = song.Notes[i];
            if (section.ChangeBPM && section.BPM != curBPM && section.BPM > 0)
            {
                curBPM = section.BPM;
                BPMChangeMap.Add(new BPMChangeEvent
                {
                    StepTime = totalSteps,
                    SongTime = totalPos,
                    BPM = curBPM,
                    StepCrochet = CalculateCrochet(curBPM) / 4f
                });
            }

            float sectionBeats = section.SectionBeats > 0 ? section.SectionBeats : 4f;
            int deltaSteps = (int)Math.Round(sectionBeats * 4);
            totalSteps += deltaSteps;
            totalPos += (60f / curBPM * 1000f / 4f) * deltaSteps;
        }

        Console.WriteLine($"[CONDUCTOR] BPM map built: {BPMChangeMap.Count} changes");
    }

    public static float CalculateCrochet(float bpm)
    {
        return (60f / bpm) * 1000f;
    }
}
