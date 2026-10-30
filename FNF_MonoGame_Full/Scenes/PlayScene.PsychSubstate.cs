using FNF_MonoGame.Engine.PsychEngine;

namespace FNF_MonoGame.Scenes;

/// <summary>psychlua/CustomSubstate.hx openCustomSubstate(name, pauseGame) / closeCustomSubstate: a pausing substate pauses the
/// song (FlxG.sound.music + vocals, PlayState.paused = true); PlayState.closeSubState resumes it and calls onResume.</summary>
public partial class PlayScene
{
    private bool _psySubPaused;

    internal void PsychCustomSubstateOpened(bool pauseGame)
    {
        if (!pauseGame || _psySubPaused) return;
        _psySubPaused = true;
        Audio?.PauseMusic();
    }

    internal void PsychCustomSubstateClosed()
    {
        if (!_psySubPaused) return;
        _psySubPaused = false;
        Audio?.ResumeMusic();
        CallOnScripts("onResume");
    }
}
