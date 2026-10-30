using System;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Scenes;

namespace FNF_MonoGame.Gameplay;

/// <summary>
/// A BaseStage that forwards all lifecycle callbacks to a Psych Engine Lua stage script.
/// </summary>
public class LuaStage : BaseStage
{
    private readonly LuaScriptEngine _lua;

    public LuaStage(PlayScene game, string luaPath) : base(game)
    {
        _lua = new LuaScriptEngine(game, luaPath);
        if (!_lua.Load())
        {
            Console.WriteLine($"[LUASTAGE] Could not load Lua stage {luaPath}; callbacks will be skipped.");
        }
    }

    public override void Create()
    {
        base.Create();
        _lua?.Call("onCreate");
    }

    public override void CreatePost()
    {
        base.CreatePost();
        _lua?.Call("onCreatePost");
    }

    public override void Update(float elapsed)
    {
        base.Update(elapsed);
        _lua?.Call("onUpdate", elapsed);
    }

    public override void UpdatePost(float elapsed)
    {
        base.UpdatePost(elapsed);
        _lua?.Call("onUpdatePost", elapsed);
    }

    public override void CountdownTick(Countdown countdown, int num)
    {
        base.CountdownTick(countdown, num);
        _lua?.Call("onStartCountdown", (int)countdown, num);
        _lua?.Call("onCountdownTick", (int)countdown, num);
    }

    public override void StartSong()
    {
        base.StartSong();
        _lua?.Call("onStartSong");
    }

    public override void BeatHit()
    {
        base.BeatHit();
        _lua?.Call("onBeatHit", CurBeat);
    }

    public override void StepHit()
    {
        base.StepHit();
        _lua?.Call("onStepHit", CurStep);
    }

    public override void SectionHit()
    {
        base.SectionHit();
        _lua?.Call("onSectionHit", CurSection);
    }

    public override void GoodNoteHit(FNF_MonoGame.Engine.PsychEngine.Note note)
    {
        base.GoodNoteHit(note);
        _lua?.Call("goodNoteHit", note);
    }

    public override void OpponentNoteHit(FNF_MonoGame.Engine.PsychEngine.Note note)
    {
        base.OpponentNoteHit(note);
        _lua?.Call("opponentNoteHit", note);
    }

    public override void NoteMiss(FNF_MonoGame.Engine.PsychEngine.Note note)
    {
        base.NoteMiss(note);
        _lua?.Call("noteMiss", note);
    }

    public override void NoteMissPress(int direction)
    {
        base.NoteMissPress(direction);
        _lua?.Call("noteMissPress", direction);
    }

    public override void EventCalled(string name, string value1, string value2, float? flValue1, float? flValue2, double time)
    {
        base.EventCalled(name, value1, value2, flValue1, flValue2, time);
        _lua?.Call("onEvent", name, value1 ?? "", value2 ?? "");
    }

    public override void EventPushed(GameEvent eventNote)
    {
        base.EventPushed(eventNote);
        _lua?.Call("onEventPushed", eventNote);
    }
}
