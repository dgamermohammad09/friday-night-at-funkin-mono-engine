using System;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Gameplay;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine MusicBeatSubstate shim.
/// Direct port of backend/MusicBeatSubstate.hx — mirrors MusicBeatState timing
/// for menu substates / pause menus. Can be wired to a Conductor or ticked manually.
/// </summary>
public class MusicBeatSubstate
{
    public virtual void Create() { }
    public virtual void Update(float elapsed) { }
    public virtual void Draw(SpriteBatch spriteBatch) { }
    public virtual void Close() { }

    public int CurStep { get; private set; }
    public int CurBeat { get; private set; }
    public int CurSection { get; private set; }
    public float CurDecStep { get; private set; }
    public float CurDecBeat { get; private set; }
    public int StepsPerSection { get; set; } = 16;

    public event Action StepHit;
    public event Action BeatHit;
    public event Action SectionHit;

    public void Update(Conductor conductor)
    {
        if (conductor == null) return;

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
    }

    protected virtual void OnBeatHit()
    {
        BeatHit?.Invoke();
    }

    protected virtual void OnSectionHit()
    {
        SectionHit?.Invoke();
    }
}
