using System;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine BaseStage — abstract base for all gameplay stages.
/// Direct port of backend/BaseStage.hx.
/// Defines the full lifecycle callback API that stages can override.
/// </summary>
public enum Countdown
{
    Three = 0,
    Two = 1,
    One = 2,
    Go = 3,
    Start = 4
}

public abstract class PsychBaseStage
{
    // Timing state
    public int CurBeat { get; set; }
    public float CurDecBeat { get; set; }
    public int CurStep { get; set; }
    public float CurDecStep { get; set; }
    public int CurSection { get; set; }

    // ═══════════════════════════════════════════
    // Lifecycle Callbacks
    // ═══════════════════════════════════════════

    /// <summary>Called once when the stage is first created.</summary>
    public virtual void Create() { }

    /// <summary>Called after all stage objects and characters are set up.</summary>
    public virtual void CreatePost() { }

    /// <summary>Called every frame.</summary>
    public virtual void Update(float elapsed) { }

    /// <summary>Called every frame after Update.</summary>
    public virtual void UpdatePost(float elapsed) { }

    /// <summary>Called on each countdown tick (3, 2, 1, GO).</summary>
    public virtual void CountdownTick(Countdown count, int num) { }

    /// <summary>Called when the song starts playing.</summary>
    public virtual void StartSong() { }

    /// <summary>Called when the song ends.</summary>
    public virtual void EndSong() { }

    // ═══════════════════════════════════════════
    // Step / Beat / Section hits
    // ═══════════════════════════════════════════

    /// <summary>Called on every beat (quarter note).</summary>
    public virtual void BeatHit() { }

    /// <summary>Called on every step (16th note).</summary>
    public virtual void StepHit() { }

    /// <summary>Called on every section change.</summary>
    public virtual void SectionHit() { }

    // ═══════════════════════════════════════════
    // Substates
    // ═══════════════════════════════════════════

    /// <summary>Called when a substate is closed (e.g., unpausing).</summary>
    public virtual void CloseSubState() { }

    /// <summary>Called when a substate is opened (e.g., pausing).</summary>
    public virtual void OpenSubState() { }

    // ═══════════════════════════════════════════
    // Events
    // ═══════════════════════════════════════════

    /// <summary>Called when a chart event is triggered during gameplay.</summary>
    public virtual void EventCalled(string eventName, string value1, string value2,
        float? flValue1, float? flValue2, float strumTime) { }

    /// <summary>Called once per event type during song load (for precaching).</summary>
    public virtual void EventPushed(PsychEvents.EventNote evt) { }

    /// <summary>Called for every event during song load.</summary>
    public virtual void EventPushedUnique(PsychEvents.EventNote evt) { }

    // ═══════════════════════════════════════════
    // Note Hit / Miss
    // ═══════════════════════════════════════════

    /// <summary>Called when the player hits a note.</summary>
    public virtual void GoodNoteHit(object note) { }

    /// <summary>Called when the opponent hits a note.</summary>
    public virtual void OpponentNoteHit(object note) { }

    /// <summary>Called when the player misses a note.</summary>
    public virtual void NoteMiss(object note) { }

    /// <summary>Called when the player presses a key with no note to hit.</summary>
    public virtual void NoteMissPress(int direction) { }

    // ═══════════════════════════════════════════
    // Game Over
    // ═══════════════════════════════════════════

    /// <summary>Called when game over starts.</summary>
    public virtual void OnGameOver() { }

    /// <summary>Called when game over is confirmed (retry/exit).</summary>
    public virtual void OnGameOverConfirm(bool retry) { }

    // ═══════════════════════════════════════════
    // Pause
    // ═══════════════════════════════════════════

    /// <summary>Called when the game is paused.</summary>
    public virtual void OnPause() { }

    /// <summary>Called when the game is resumed from pause.</summary>
    public virtual void OnResume() { }

    // ═══════════════════════════════════════════
    // Cleanup
    // ═══════════════════════════════════════════

    /// <summary>Called when the stage is destroyed.</summary>
    public virtual void OnDestroy() { }
}
