using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine Controls bridge.
/// Direct port of backend/Controls.hx, backed by the existing InputManager.
/// </summary>
public class Controls
{
    public static Controls Instance { get; set; } = new();

    public bool ControllerMode { get; set; } = false;

    public bool UI_UP_P => JustPressed("ui_up");
    public bool UI_DOWN_P => JustPressed("ui_down");
    public bool UI_LEFT_P => JustPressed("ui_left");
    public bool UI_RIGHT_P => JustPressed("ui_right");
    public bool NOTE_UP_P => JustPressed("note_up");
    public bool NOTE_DOWN_P => JustPressed("note_down");
    public bool NOTE_LEFT_P => JustPressed("note_left");
    public bool NOTE_RIGHT_P => JustPressed("note_right");

    public bool UI_UP => Pressed("ui_up");
    public bool UI_DOWN => Pressed("ui_down");
    public bool UI_LEFT => Pressed("ui_left");
    public bool UI_RIGHT => Pressed("ui_right");
    public bool NOTE_UP => Pressed("note_up");
    public bool NOTE_DOWN => Pressed("note_down");
    public bool NOTE_LEFT => Pressed("note_left");
    public bool NOTE_RIGHT => Pressed("note_right");

    public bool UI_UP_R => JustReleased("ui_up");
    public bool UI_DOWN_R => JustReleased("ui_down");
    public bool UI_LEFT_R => JustReleased("ui_left");
    public bool UI_RIGHT_R => JustReleased("ui_right");
    public bool NOTE_UP_R => JustReleased("note_up");
    public bool NOTE_DOWN_R => JustReleased("note_down");
    public bool NOTE_LEFT_R => JustReleased("note_left");
    public bool NOTE_RIGHT_R => JustReleased("note_right");

    public bool ACCEPT => JustPressed("accept");
    public bool BACK => JustPressed("back");
    public bool PAUSE => JustPressed("pause");
    public bool RESET => JustPressed("reset");

    public bool JustPressed(string key)
    {
        var input = FNFGame.Instance?.Input;
        if (input == null) return false;
        if (input.GameplayMode)
        {
            switch (key)
            {
                case "note_up": return input.NotePressed[2];
                case "note_down": return input.NotePressed[1];
                case "note_left": return input.NotePressed[0];
                case "note_right": return input.NotePressed[3];
                case "accept": return input.ConfirmPressed;
                case "back": return input.BackPressed;
                case "pause": return input.PauseTogglePressed || input.PauseOpenEnterPressed; // just pressed (was PauseHeld = every frame while held)
                case "reset": return input.IsPressed(Keys.R);
            }
        }
        switch (key)
        {
            case "ui_up": return input.UpPressed;
            case "ui_down": return input.DownPressed;
            case "ui_left": return input.LeftPressed;
            case "ui_right": return input.RightPressed;
            case "note_up": return input.NotePressed[2];
            case "note_down": return input.NotePressed[1];
            case "note_left": return input.NotePressed[0];
            case "note_right": return input.NotePressed[3];
            case "accept": return input.ConfirmPressed;
            case "back": return input.BackPressed;
            case "pause": return input.PauseTogglePressed || input.PauseOpenEnterPressed; // just pressed (was PauseHeld = every frame while held)
            case "reset": return input.IsPressed(Keys.R);
        }
        return false;
    }

    public bool Pressed(string key)
    {
        var input = FNFGame.Instance?.Input;
        if (input == null) return false;
        if (input.GameplayMode)
        {
            switch (key)
            {
                case "note_up": return input.NoteHeld[2];
                case "note_down": return input.NoteHeld[1];
                case "note_left": return input.NoteHeld[0];
                case "note_right": return input.NoteHeld[3];
                case "accept": return input.ConfirmHeld;
                case "back": return input.IsHeld(Keys.Escape) || input.IsHeld(Keys.Back);
                case "pause": return input.PauseHeld;
                case "reset": return input.IsHeld(Keys.R);
            }
        }
        switch (key)
        {
            case "ui_up": return input.UpHeld;
            case "ui_down": return input.DownHeld;
            case "ui_left": return input.IsHeld(Keys.Left) || input.IsHeld(Keys.A);
            case "ui_right": return input.IsHeld(Keys.Right) || input.IsHeld(Keys.D);
            case "note_up": return input.NoteHeld[2];
            case "note_down": return input.NoteHeld[1];
            case "note_left": return input.NoteHeld[0];
            case "note_right": return input.NoteHeld[3];
            case "accept": return input.ConfirmHeld;
            case "back": return input.IsHeld(Keys.Escape) || input.IsHeld(Keys.Back);
            case "pause": return input.PauseHeld;
            case "reset": return input.IsHeld(Keys.R);
        }
        return false;
    }

    public bool JustReleased(string key)
    {
        var input = FNFGame.Instance?.Input;
        if (input == null) return false;
        switch (key)
        {
            case "ui_up": return input.IsReleased(Keys.Up) || input.IsReleased(Keys.W);
            case "ui_down": return input.IsReleased(Keys.Down) || input.IsReleased(Keys.S);
            case "ui_left": return input.IsReleased(Keys.Left) || input.IsReleased(Keys.A);
            case "ui_right": return input.IsReleased(Keys.Right) || input.IsReleased(Keys.D);
            case "note_up": return input.NoteReleased[2];
            case "note_down": return input.NoteReleased[1];
            case "note_left": return input.NoteReleased[0];
            case "note_right": return input.NoteReleased[3];
            case "accept": return input.IsReleased(Keys.Enter) || input.IsReleased(Keys.Space);
            case "back": return input.IsReleased(Keys.Escape) || input.IsReleased(Keys.Back);
            case "pause": return input.IsReleased(Keys.Enter) || input.IsReleased(Keys.Escape);
            case "reset": return input.IsReleased(Keys.R);
        }
        return false;
    }
}
