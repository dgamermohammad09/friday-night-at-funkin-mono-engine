using System;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.PureHScript;
using FNF_MonoGame.Scenes;

namespace FNF_MonoGame.Gameplay;

/// <summary>
/// Runs one Psych Engine HScript (.hx) file (global script, stage script, song script, note type or
/// event script) and forwards gameplay callbacks to it with Psych 1.0's names and arguments
/// (PlayState.callOnScripts / callOnHScript).
/// </summary>
public class HScriptStage : BaseStage
{
    private readonly PsychHScriptEngine _hscript;

    public HScriptStage(PlayScene game, string hxPath) : base(game)
    {
        _hscript = new PsychHScriptEngine(game, hxPath);
        if (!_hscript.Load())
        {
            Console.WriteLine($"[HSCRIPTSTAGE] Could not load HScript {hxPath}; callbacks will be skipped.");
            return;
        }
        // Psych PlayState.initHScript(): onCreate is called right after the script is created.
        _hscript.Call("onCreate");
    }

    public PsychHScriptEngine Engine => _hscript;

    public override void CreatePost()
    {
        base.CreatePost();
        _hscript?.Call("onCreatePost");
    }

    public override void Update(float elapsed)
    {
        base.Update(elapsed);
        _hscript?.Call("onUpdate", elapsed);
    }

    public override void UpdatePost(float elapsed)
    {
        base.UpdatePost(elapsed);
        _hscript?.Call("onUpdatePost", elapsed);
    }

    public override void CountdownTick(Countdown countdown, int num)
    {
        base.CountdownTick(countdown, num);
        _hscript?.Call("onCountdownTick", (int)countdown, num);
    }

    public override void StartSong()
    {
        base.StartSong();
        _hscript?.Call("onSongStart");
    }

    public override void BeatHit()
    {
        base.BeatHit();
        _hscript?.Call("onBeatHit");
    }

    public override void StepHit()
    {
        base.StepHit();
        _hscript?.Call("onStepHit");
    }

    public override void SectionHit()
    {
        base.SectionHit();
        _hscript?.Call("onSectionHit");
    }

    public object Call(string funcName, params object[] args) => _hscript?.CallHaxeFunction(funcName, args);

    public override void GoodNoteHit(FNF_MonoGame.Engine.PsychEngine.Note note)
    {
        base.GoodNoteHit(note);
        _hscript?.Call("goodNoteHit", note);
    }

    public override void OpponentNoteHit(FNF_MonoGame.Engine.PsychEngine.Note note)
    {
        base.OpponentNoteHit(note);
        _hscript?.Call("opponentNoteHit", note);
    }

    public override void NoteMiss(FNF_MonoGame.Engine.PsychEngine.Note note)
    {
        base.NoteMiss(note);
        _hscript?.Call("noteMiss", note);
    }

    public override void NoteMissPress(int direction)
    {
        base.NoteMissPress(direction);
        _hscript?.Call("noteMissPress", direction);
    }

    public override void EventCalled(string name, string value1, string value2, float? flValue1, float? flValue2, double time)
    {
        base.EventCalled(name, value1, value2, flValue1, flValue2, time);
        _hscript?.Call("onEvent", name, value1 ?? "", value2 ?? "", time);
    }

    public override void EventPushed(GameEvent eventNote)
    {
        base.EventPushed(eventNote);
        _hscript?.Call("onEventPushed", eventNote?.Name ?? "", eventNote?.Value?.ToString() ?? "", "", eventNote?.Time ?? 0d);
    }
}
