using System;
using System.Threading.Tasks;

namespace FNF_MonoGame.Engine;

/// <summary>The platform's own text entry (Xbox: the system on-screen keyboard) for every in-game text field.
/// The host sets <see cref="Provider"/> (Xbox/UWP: GamePage). Scenes call <see cref="Poll"/> at the top of their
/// editor update: Busy = keyboard open (skip the D-pad letter picker), Done = buffer replaced -> commit,
/// Cancelled = close the editor, None = no system keyboard here -> the old in-game editor works as before.</summary>
public static class SystemKeyboard
{
    public enum State { None, Busy, Done, Cancelled }

    /// <summary>(title, current text, max length) -> typed text, or null when cancelled.</summary>
    public static Func<string, string, int, Task<string>> Provider;

    /// <summary>Signed-in console user's name (Xbox gamertag), filled by the host at start-up; null if unknown.</summary>
    public static string AccountName;

    private static Task<string> _task;
    private static object _owner;
    private static int _lastPollMs = int.MinValue / 2;
    private static bool _finished;
    private static bool _hold;
    private static int _holdUntilMs;

    /// <summary>Only on the console (PC test: FNF_DEV_XBOXFAMILY=1 in the UWP app); PC keeps typing straight into the game.</summary>
    public static bool Enabled => Provider != null && InputManager.MouseDisabled;

    /// <summary>InputManager: no new presses while the keyboard is open / right after it closed.</summary>
    public static bool HoldInput => _hold || (_task != null && !_task.IsCompleted);

    internal static void InputReleased(bool anyDown)
    {
        if (_task != null && !_task.IsCompleted) return;
        if (!anyDown && unchecked(Environment.TickCount - _holdUntilMs) >= 0) _hold = false;
    }

    public static State Poll(object owner, string title, ref string buffer, int maxLength = 64)
    {
        if (!Enabled) return State.None;
        int now = Environment.TickCount;
        // A new editing session: another scene/editor, or this editor was closed for a moment.
        if (!ReferenceEquals(owner, _owner) || unchecked(now - _lastPollMs) > 300)
        {
            _owner = owner; _finished = false;
            if (_task != null && _task.IsCompleted) _task = null;
        }
        _lastPollMs = now;
        if (_finished) return State.None;
        if (_task == null)
        {
            try
            {
                _task = Provider(title ?? "", buffer ?? "", maxLength);
                Console.WriteLine($"[OSK] open '{title}' (max {maxLength})");
            }
            catch (Exception e) { Console.WriteLine("[OSK] failed: " + e.Message); _finished = true; return State.None; }
            _hold = true;
            return State.Busy;
        }
        if (!_task.IsCompleted) return State.Busy;
        string r = _task.Status == TaskStatus.RanToCompletion ? _task.Result : null;
        _task = null; _finished = true; _hold = true; _holdUntilMs = now + 250;
        Console.WriteLine(r == null ? "[OSK] cancelled" : $"[OSK] done ({r.Length} chars)");
        if (r == null) return State.Cancelled;
        if (r.Length > maxLength) r = r.Substring(0, maxLength);
        buffer = r;
        return State.Done;
    }
}
