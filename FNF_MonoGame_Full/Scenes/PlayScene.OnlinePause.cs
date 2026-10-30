using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;
using FontStashSharp;

namespace FNF_MonoGame.Scenes;

// Online pause (Psych Online rooms and LAN/online versus). Psych Online PauseSubState (substates/PauseSubState.hx):
// while GameClient.isConnected() the menu is just ['Resume', 'Exit to lobby'] and the song is NOT paused
// (openPauseMenu only muffles the music; the match keeps running). 'Exit to lobby' = GameClient.send("requestEndSong").
// Before this the port sent requestEndSong on the pause press itself, so Start/Esc instantly ended the match
// (user report: "START in a Psych song finishes the game").
public partial class PlayScene
{
    private bool _olPauseOpen;
    private int _olPauseSel;
    private float _olPauseT;
    private static readonly string[] OlPauseItems = { "Resume", "Exit to lobby" };

    /// <summary>Called every frame from MpHandlePause for online matches. Returns true when the frame's input was consumed.</summary>
    /// Frames that consume a press return true (PlayScene skips that one update so the V-Slice pause never opens);
    /// otherwise the match keeps updating underneath (no pause online).
    private bool OnlinePauseUpdate(bool pausePressed, bool enterPressed, Action leave)
    {
        if (!_olPauseOpen)
        {
            if (!pausePressed && !enterPressed) return false;
            _olPauseOpen = true; _olPauseSel = 0; _olPauseT = 0f;
            Console.WriteLine("[PAUSE] online pause opened (match keeps running)");
            ClearNoteInputs();
            return true;
        }
        _olPauseT += 1f / 60f;
        ClearNoteInputs();   // the menu owns the controller while it is open
        if (pausePressed || Input.BackPressed) { _olPauseOpen = false; Audio.PlaySound("cancelMenu"); Console.WriteLine("[PAUSE] online resume"); return true; }
        if (Input.UpPressed || Input.DownPressed) { _olPauseSel = 1 - _olPauseSel; Audio.PlaySound("scrollMenu", 0.4f); }
        bool accept = Input.ConfirmPressed || Input.IsGamePadPressed(Microsoft.Xna.Framework.Input.Buttons.A);
        if (accept && _olPauseT > 0.1f)
        {
            Console.WriteLine($"[PAUSE] online pick {OlPauseItems[_olPauseSel]}");
            _olPauseOpen = false;
            if (_olPauseSel == 1) leave();
            return true;
        }
        return enterPressed;
    }

    private void ClearNoteInputs()
    {
        for (int i = 0; i < 4; i++) { Input.NotePressed[i] = false; Input.NoteHeld[i] = false; Input.NoteReleased[i] = false; }
    }

    private void DrawOnlinePause(SpriteBatch sb)
    {
        if (!_olPauseOpen || sb == null) return;
        sb.Draw(Assets.Pixel, new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT), new Color(Color.Black, 0.6f));
        _pmFont ??= AtlasFontV.Load(Assets, "images/fonts/bold");
        for (int i = 0; i < OlPauseItems.Length; i++)
        {
            float d = (i - _olPauseSel) * 1.3f;   // PauseSubState item layout (remapToRange * 1.3)
            var pos = new Vector2(d * 20f + 90f, d * 120f + 720f * 0.48f);
            var col = new Color(Color.White, i == _olPauseSel ? 1f : 0.6f);
            if (_pmFont != null) _pmFont.Draw(sb, OlPauseItems[i], pos, col, _olPauseT);
            else { var f = Assets.GetFont(28); if (f != null) f.DrawText(sb, OlPauseItems[i], pos, col); }
        }
    }
}
