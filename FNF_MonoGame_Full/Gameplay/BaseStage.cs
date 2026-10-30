using System;
using Microsoft.Xna.Framework;
using FNF_MonoGame.Scenes;

namespace FNF_MonoGame.Gameplay;

/// <summary>
/// Psych Engine BaseStage port.
/// Mod stages subclass this to override create, update, beat/step/section hits,
/// note callbacks, events, and substate callbacks.
/// </summary>
public class BaseStage
{
    // Reference to the owning PlayScene (Psych Engine's "game")
    protected PlayScene Game { get; }

    public bool OnPlayState => Game != null;

    // Convenience state accessors
    public bool Paused => Game?.Paused ?? false;
    public string SongName => Game?.SongName ?? "";
    public bool IsStoryMode => false; // Not tracked in this MonoGame port yet
    public bool SeenCutscene => false;
    public bool InCutscene
    {
        get => Game?._inCutscene ?? false;
        set { if (Game != null) Game._inCutscene = value; }
    }
    public bool CanPause
    {
        get => Game?._canPause ?? false;
        set { if (Game != null) Game._canPause = value; }
    }

    // Character accessors
    public Character Boyfriend => Game?.Boyfriend;
    public Character Dad => Game?.Opponent;
    public Character Gf => Game?.Girlfriend;

    public float DefaultCamZoom
    {
        get => Game?.DefaultCamZoom ?? 0.9f;
        set { if (Game != null) Game.DefaultCamZoom = value; }
    }

    // Current musical position
    public int CurBeat { get; set; }
    public float CurDecBeat { get; set; }
    public int CurStep { get; set; }
    public float CurDecStep { get; set; }
    public int CurSection { get; set; }

    public BaseStage(PlayScene game)
    {
        Game = game ?? throw new ArgumentNullException(nameof(game));
        Game.RegisterStage(this);
        Create();
    }

    // Main callbacks
    public virtual void Create() { }
    public virtual void CreatePost() { }
    public virtual void Update(float elapsed) { }
    public virtual void UpdatePost(float elapsed) { }
    public virtual void CountdownTick(Countdown countdown, int num) { }
    public virtual void StartSong() { }

    // Timing callbacks
    public virtual void BeatHit() { }
    public virtual void StepHit() { }
    public virtual void SectionHit() { }

    // Substate callbacks
    public virtual void CloseSubState() { }
    public virtual void OpenSubState(object subState) { }

    // Events
    public virtual void EventCalled(string eventName, string value1, string value2, float? flValue1, float? flValue2, double strumTime) { }
    public virtual void EventPushed(GameEvent eventNote) { }
    public virtual void EventPushedUnique(GameEvent eventNote) { }

    // Note callbacks
    public virtual void GoodNoteHit(FNF_MonoGame.Engine.PsychEngine.Note note) { }
    public virtual void OpponentNoteHit(FNF_MonoGame.Engine.PsychEngine.Note note) { }
    public virtual void NoteMiss(FNF_MonoGame.Engine.PsychEngine.Note note) { }
    public virtual void NoteMissPress(int direction) { }

    // Object injection helpers
    public object Add(object obj) { Game?.AddStageObject(obj); return obj; }
    public object Remove(object obj) { Game?.RemoveStageObject(obj); return obj; }
    public object Remove(object obj, bool splice) { Game?.RemoveStageObject(obj); return obj; }
    public object Insert(int position, object obj) { Game?.InsertStageObject(position, obj); return obj; }

    public System.Collections.Generic.List<FNF_MonoGame.Scenes.PlayScene.StaticStageProp> Members =>
        Game?.Stage?.Members ?? new System.Collections.Generic.List<FNF_MonoGame.Scenes.PlayScene.StaticStageProp>();

    public object AddBehindGf(object obj) => Insert(Game.GetObjectIndex(Game.Girlfriend), obj);
    public object AddBehindBf(object obj) => Insert(Game.GetObjectIndex(Game.Boyfriend), obj);
    public object AddBehindBF(object obj) => AddBehindBf(obj);
    public object AddBehindDad(object obj) => Insert(Game.GetObjectIndex(Game.Opponent), obj);

    public object GetStageObject(string name) => Game?.GetStageVariable(name);
    public void SetStageObject(string name, object value) => Game?.SetStageVariable(name, value);

    public void SetStartCallback(Action callback) => Game._startCallback = callback;
    public void SetEndCallback(Action callback) => Game._endCallback = callback;

    public bool StartCountdown() => Game?.StartCountdown() ?? false;
    public void EndSong() => Game?.EndSong();
    public void MoveCameraSection() => Game?.MoveCameraSection();
    public void MoveCamera(bool isDad) => Game?.MoveCamera(isDad);

    public void SetDefaultGf(string name)
    {
        if (Game?.Chart != null && string.IsNullOrEmpty(Game.Chart.GirlfriendCharacter))
            Game.Chart.GirlfriendCharacter = name;
    }

    public object AddBehindGF(object obj) => AddBehindGf(obj);
    public void SetDefaultGF(string name) => SetDefaultGf(name);
}

public enum Countdown
{
    Three,
    Two,
    One,
    Go,
    Start
}
