using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Gameplay;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine MusicBeatState shim.
/// Direct port of backend/MusicBeatState.hx — tracks curStep, curBeat, curSection
/// and dispatches step/beat/section hit events to registered stages.
/// PlayScene wires this to its Conductor to drive timing.
/// </summary>
public class MusicBeatState
{
    public virtual void Create() { }
    public virtual void Update(float elapsed) { }
    public virtual void Draw(SpriteBatch spriteBatch) { }
    public virtual void Close() { }

    public static void SwitchState(MusicBeatState newState)
    {
        // TODO: wire to FNF_MonoGame scene manager
    }

    public static Dictionary<string, object> Variables { get; } = new(StringComparer.Ordinal);
    public static object TargetInstance { get; set; }

    public static object GetTargetInstance() => TargetInstance;

    public int CurStep { get; private set; }
    public int CurBeat { get; private set; }
    public int CurSection { get; private set; }
    public float CurDecStep { get; private set; }
    public float CurDecBeat { get; private set; }
    public int StepsPerSection { get; set; } = 16;

    public bool ConductorOnHold { get; set; }

    public event Action StepHit;
    public event Action BeatHit;
    public event Action SectionHit;

    private readonly List<BaseStage> _stages = new();

    public virtual void AddStage(BaseStage stage)
    {
        if (stage != null && !_stages.Contains(stage))
            _stages.Add(stage);
    }

    public virtual void RemoveStage(BaseStage stage)
    {
        _stages.Remove(stage);
    }

    public void Update(Conductor conductor)
    {
        if (conductor == null || ConductorOnHold) return;

        float lastStep = CurDecStep;
        CurDecStep = conductor.CurrentStep;
        CurDecBeat = conductor.CurrentBeat;
        int newStep = (int)Math.Floor(CurDecStep);
        int newBeat = (int)Math.Floor(CurDecBeat);

        if (newStep != CurStep)
        {
            CurStep = newStep;
            OnStepHit();
        }
        if (newBeat != CurBeat)
        {
            CurBeat = newBeat;
            OnBeatHit();
            if (CurBeat % StepsPerSection == 0)
            {
                CurSection = CurBeat / StepsPerSection;
                OnSectionHit();
            }
        }
    }

    protected virtual void OnStepHit()
    {
        StepHit?.Invoke();
        foreach (var stage in _stages)
            stage.StepHit();
    }

    protected virtual void OnBeatHit()
    {
        BeatHit?.Invoke();
        foreach (var stage in _stages)
            stage.BeatHit();
    }

    protected virtual void OnSectionHit()
    {
        SectionHit?.Invoke();
        foreach (var stage in _stages)
            stage.SectionHit();
    }
}
