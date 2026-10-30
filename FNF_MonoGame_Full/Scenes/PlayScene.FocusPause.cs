using System;

namespace FNF_MonoGame.Scenes;

public partial class PlayScene
{
    private bool _focusPauseRequested;
    private int _focusPauseTick;

    /// <summary>
    /// PlayState.onFocusLost (Preferences.autoPause): opens the pause menu via pause(true) when the game is not paused yet
    /// (FlxG.autoPause is switched off while a song may pause, so the game itself keeps running behind the menu).
    /// Returns true when the PlayScene handles focus loss itself (the caller must then not freeze the game).
    /// Online matches never pause (Psych Online / LAN keep playing).
    /// </summary>
    public bool RequestFocusPause()
    {
        if (_mpPso || _mpOnline) return true;
        if (!_paused && !_gameOver)
        {
            _focusPauseRequested = true;
            _focusPauseTick = Environment.TickCount;
            Console.WriteLine("[FOCUS] lost -> pause requested");
        }
        return true;
    }

    /// <summary>Consumed by the pause check in Update. A request older than 0.5 s (dialogue/video was active) is dropped.</summary>
    private bool ConsumeFocusPause()
    {
        if (!_focusPauseRequested) return false;
        _focusPauseRequested = false;
        return unchecked(Environment.TickCount - _focusPauseTick) < 500 && !_paused && !_gameOver;
    }
}
