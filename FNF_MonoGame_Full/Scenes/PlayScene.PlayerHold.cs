namespace FNF_MonoGame.Scenes;

/// <summary>
/// BaseCharacter.onUpdate (V-Slice 0.8.1) for characterType == BF:
///   if (justPressedNote()) holdTimer = 0;
///   shouldStopSinging = !isHoldingNote()   (NOTE_LEFT/DOWN/UP/RIGHT held)
/// so the player character keeps its sing pose for as long as a note key is held (long sustains), instead of
/// returning to idle after singTime steps. Controls are read even in botplay (like the original).
/// </summary>
public partial class PlayScene
{
    /// <summary>Dev hook FNF_DEV_HOLDKEYS="from:to" (song s): pretend a note key is held (tests the hold pose in botplay).</summary>
    private static readonly double[] s_devHoldKeys = ParseDevHold(System.Environment.GetEnvironmentVariable("FNF_DEV_HOLDKEYS"));
    private static double[] ParseDevHold(string v)
    {
        var p = (v ?? "").Split(':'); var inv = System.Globalization.CultureInfo.InvariantCulture;
        return p.Length == 2 && double.TryParse(p[0], System.Globalization.NumberStyles.Float, inv, out var a)
            && double.TryParse(p[1], System.Globalization.NumberStyles.Float, inv, out var b) ? new[] { a, b } : null;
    }

    private void SetPlayerHoldFlags()
    {
        bool held = false, pressed = false;
        var inp = Input;
        if (inp != null)
            for (int i = 0; i < 4; i++) { held |= inp.NoteHeld[i]; pressed |= inp.NotePressed[i]; }
        if (s_devHoldKeys != null && _conductor != null && !_countdownActive)
            held |= _conductor.SongPosition >= s_devHoldKeys[0] && _conductor.SongPosition < s_devHoldKeys[1];
        void Apply(Gameplay.Character c, bool isBf)
        {
            if (c == null) return;
            c.IsPlayerType = isBf; c.PlayerHoldingNote = held; c.PlayerJustPressedNote = pressed;
        }
        if (_ssfActive)
        {
            // sserafim.hxc setGirlsSinging: characterType = singing[i] ? BF : DAD
            for (int i = 0; i < 6; i++) Apply(SsfGirl(i), _ssfSinging[i]);
        }
        else Apply(_boyfriend, true);
    }
}
