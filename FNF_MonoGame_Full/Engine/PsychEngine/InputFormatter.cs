using Microsoft.Xna.Framework.Input;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Formats keyboard keys and gamepad inputs into short display names.
/// Original: backend/InputFormatter.hx
/// </summary>
public static class InputFormatter
{
    public static string GetKeyName(Keys key)
    {
        switch (key)
        {
            case Keys.Back: return "BckSpc";
            case Keys.LeftControl:
            case Keys.RightControl: return "Ctrl";
            case Keys.LeftAlt:
            case Keys.RightAlt: return "Alt";
            case Keys.CapsLock: return "Caps";
            case Keys.PageUp: return "PgUp";
            case Keys.PageDown: return "PgDown";
            case Keys.D0: return "0";
            case Keys.D1: return "1";
            case Keys.D2: return "2";
            case Keys.D3: return "3";
            case Keys.D4: return "4";
            case Keys.D5: return "5";
            case Keys.D6: return "6";
            case Keys.D7: return "7";
            case Keys.D8: return "8";
            case Keys.D9: return "9";
            case Keys.NumPad0: return "#0";
            case Keys.NumPad1: return "#1";
            case Keys.NumPad2: return "#2";
            case Keys.NumPad3: return "#3";
            case Keys.NumPad4: return "#4";
            case Keys.NumPad5: return "#5";
            case Keys.NumPad6: return "#6";
            case Keys.NumPad7: return "#7";
            case Keys.NumPad8: return "#8";
            case Keys.NumPad9: return "#9";
            case Keys.Multiply: return "#*";
            case Keys.Add: return "#+";
            case Keys.Subtract: return "#-";
            case Keys.Decimal: return "#.";
            case Keys.OemSemicolon: return ";";
            case Keys.OemComma: return ",";
            case Keys.OemPeriod: return ".";
            case Keys.OemTilde: return "`";
            case Keys.OemOpenBrackets: return "[";
            case Keys.OemCloseBrackets: return "]";
            case Keys.OemQuotes: return "'";
            case Keys.PrintScreen: return "PrtScrn";
            case Keys.None: return "---";
            default:
                string label = key.ToString();
                if (label.ToLowerInvariant() == "null") return "---";
                var arr = label.Split('_');
                for (int i = 0; i < arr.Length; i++)
                    arr[i] = CoolUtil.Capitalize(arr[i].ToLowerInvariant());
                return string.Join(' ', arr);
        }
    }

    public static string GetGamepadName(Buttons key)
    {
        // No active-gamepad model detection is available in MonoGame without a connected gamepad,
        // so we default to XInput-style labels.
        switch (key)
        {
            // Analogs
            case Buttons.LeftThumbstickLeft: return "Left";
            case Buttons.LeftThumbstickRight: return "Right";
            case Buttons.LeftThumbstickUp: return "Up";
            case Buttons.LeftThumbstickDown: return "Down";
            case Buttons.LeftStick: return "LS";
            case Buttons.RightThumbstickLeft: return "C. Left";
            case Buttons.RightThumbstickRight: return "C. Right";
            case Buttons.RightThumbstickUp: return "C. Up";
            case Buttons.RightThumbstickDown: return "C. Down";
            case Buttons.RightStick: return "RS";

            // Directional
            case Buttons.DPadLeft: return "D. Left";
            case Buttons.DPadRight: return "D. Right";
            case Buttons.DPadUp: return "D. Up";
            case Buttons.DPadDown: return "D. Down";

            // Top buttons
            case Buttons.LeftShoulder: return "LB";
            case Buttons.RightShoulder: return "RB";
            case Buttons.LeftTrigger: return "LT";
            case Buttons.RightTrigger: return "RT";

            // Buttons
            case Buttons.A: return "A";
            case Buttons.B: return "B";
            case Buttons.X: return "X";
            case Buttons.Y: return "Y";
            case Buttons.Back: return "Back";
            case Buttons.Start: return "Start";

            case Buttons.None: return "---";

            default:
                string label = key.ToString();
                if (label.ToLowerInvariant() == "null") return "---";
                var arr = label.Split('_');
                for (int i = 0; i < arr.Length; i++)
                    arr[i] = CoolUtil.Capitalize(arr[i].ToLowerInvariant());
                return string.Join(' ', arr);
        }
    }
}
