using System;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine game over substate.
/// Direct port of substates/GameOverSubstate.hx — exposes the static sound
/// and character names used by the original. PlayScene can instantiate this to
/// drive the game over sequence, camera, and retry flow.
/// </summary>
public class GameOverSubstate : MusicBeatSubstate
{
    public static string CharacterName { get; set; } = "bf-dead";
    public static string DeathSoundName { get; set; } = "fnf_loss_sfx";
    public static string LoopSoundName { get; set; } = "gameOver";
    public static string EndSoundName { get; set; } = "gameOverEnd";
    public static float DeathDelay { get; set; }
    public static GameOverSubstate Instance { get; private set; }

    public bool IsConfirming { get; set; }
    public bool IsRetrying { get; set; }
    public float CharX { get; set; }
    public float CharY { get; set; }

    public event Action ConfirmPressed;
    public event Action RestartRequested;
    public event Action ExitRequested;

    public GameOverSubstate()
    {
        Instance = this;
    }

    public static void ResetVariables(PsychSong song = null)
    {
        CharacterName = "bf-dead";
        DeathSoundName = "fnf_loss_sfx";
        LoopSoundName = "gameOver";
        EndSoundName = "gameOverEnd";
        DeathDelay = 0;

        if (song == null) return;
        if (!string.IsNullOrWhiteSpace(song.GameOverChar)) CharacterName = song.GameOverChar;
        if (!string.IsNullOrWhiteSpace(song.GameOverSound)) DeathSoundName = song.GameOverSound;
        if (!string.IsNullOrWhiteSpace(song.GameOverLoop)) LoopSoundName = song.GameOverLoop;
        if (!string.IsNullOrWhiteSpace(song.GameOverEnd)) EndSoundName = song.GameOverEnd;
    }

    public void Confirm()
    {
        if (IsConfirming) return;
        IsConfirming = true;
        ConfirmPressed?.Invoke();
    }

    public void Retry()
    {
        IsRetrying = true;
        RestartRequested?.Invoke();
    }

    public void Exit()
    {
        ExitRequested?.Invoke();
    }
}
