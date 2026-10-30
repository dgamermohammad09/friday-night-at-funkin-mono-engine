using Microsoft.Xna.Framework;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine strum/receptor note.
/// Direct port of objects/StrumNote.hx — stores lane, player, animation state,
/// RGB colors, and scroll direction. PlayScene can render this directly or wrap
/// its existing receptor sprites.
/// </summary>
public class StrumNote
{
    public int NoteData { get; set; }
    public int Player { get; set; }
    public int ID => NoteData;

    public float ResetAnim { get; set; }
    public float Direction { get; set; } = 90f;
    public bool DownScroll { get; set; }
    public bool SustainReduce { get; set; } = true;
    public bool UseRGBShader { get; set; } = true;
    public string Texture { get; set; }
    public Color[] RgbColors { get; set; } = new Color[3];

    public string CurrentAnimation { get; set; } = "static";
    public bool IsPressed { get; set; }
    public bool IsConfirm { get; set; }

    public Vector2 Position { get; set; }
    public float Angle { get; set; }
    public float Alpha { get; set; } = 1f;
    public float Scale { get; set; } = 1f;

    public StrumNote(int noteData, int player)
    {
        NoteData = noteData;
        Player = player;
    }

    public void PlayAnim(string name, bool force = false)
    {
        CurrentAnimation = name;
        IsPressed = name == "pressed";
        IsConfirm = name == "confirm";
    }

    public void ReloadNote()
    {
        // Texture/animation reload is handled by PlayScene's skin resolver.
    }
}
