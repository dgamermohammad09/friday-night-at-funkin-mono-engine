using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Handles keyboard and gamepad input with press detection.
/// Full Xbox controller support:
///   Notes: DPad or X(Left)/A(Down)/Y(Up)/B(Right) or LT/LB/RB/RT
///   Confirm: A / Start
///   Back: B / Back
///   Navigate: DPad / Left Stick
///   Pause: Start
/// </summary>
public class InputManager
{
    private KeyboardState _currentKeyboard;
    private KeyboardState _previousKeyboard;
    private GamePadState _currentGamePad;
    private GamePadState _previousGamePad;
    private MouseState _currentMouse;
    private MouseState _previousMouse;
    private int _previousScrollValue;
    private int _currentScrollValue;
    
    // Stick deadzone threshold
    private const float STICK_DEADZONE = 0.5f;
    private bool _prevStickUp, _prevStickDown, _prevStickLeft, _prevStickRight;
    
    // Whether a gamepad is connected (used for UI hints)
    public bool GamePadConnected => _currentGamePad.IsConnected;

    // Auto-detect last active input device (switches display between arrows and controller buttons)
    public enum InputDevice { Keyboard, Controller }
    private InputDevice _lastDevice = InputDevice.Keyboard;
    /// <summary>Xbox device family: always Controller (there is no keyboard; the pad also arrives as Gamepad* virtual
    /// keys through CoreWindow, which used to flip this to Keyboard and hid every controller glyph on the console).</summary>
    public InputDevice LastDevice { get => IsXboxFamily ? InputDevice.Controller : _lastDevice; private set { if (_lastDevice != value && System.Environment.GetEnvironmentVariable("FNF_DEVLOG_DEVICE") == "1") System.Console.WriteLine($"[DEVICE] {_lastDevice} -> {value} keys={string.Join(",", _currentKeyboard.GetPressedKeys())}"); _lastDevice = value; s_lastDevice = value; } }
    private static InputDevice s_lastDevice = InputDevice.Keyboard;
    /// <summary>True while the last input came from a controller (always on Xbox consoles).</summary>
    public static bool ControllerActive => IsXboxFamily || s_lastDevice == InputDevice.Controller;
    public static bool IsXboxFamily => MouseDisabled;
    /// <summary>Index of the pad that gave input last (the controller is not always PlayerIndex.One on UWP/Xbox).</summary>
    public int ActivePadIndex { get; private set; }

    // FNF note inputs (Left, Down, Up, Right)
    public bool[] NotePressed { get; } = new bool[4];
    public bool[] NoteHeld { get; } = new bool[4];
    public bool[] NoteReleased { get; } = new bool[4];
    
    // Key bindings for notes (customizable)
    public Keys[] NoteKeysAlt { get; set; } = { Keys.D, Keys.F, Keys.J, Keys.K };
    public Keys[] NoteKeysArrow { get; set; } = { Keys.Left, Keys.Down, Keys.Up, Keys.Right };
    public Buttons[] NoteButtons { get; set; } = { Buttons.DPadLeft, Buttons.DPadDown, Buttons.DPadUp, Buttons.DPadRight };
    // Xbox face button note layout: X=Left, A=Down, Y=Up, B=Right (matches arrow colors)
    public Buttons[] NoteFaceButtons { get; set; } = { Buttons.X, Buttons.A, Buttons.Y, Buttons.B };
    // Xbox trigger/bumper note layout: LT=Left, LB=Down, RB=Up, RT=Right
    public Buttons[] NoteTriggerButtons { get; set; } = { Buttons.LeftTrigger, Buttons.LeftShoulder, Buttons.RightShoulder, Buttons.RightTrigger };

    // Configurable menu navigation buttons (controller)
    public Buttons ConfirmButton { get; set; } = Buttons.A;
    public Buttons CancelButton { get; set; } = Buttons.B;
    public Buttons PauseButton { get; set; } = Buttons.Start;
    public Buttons SwitchCharButton { get; set; } = Buttons.Y;

    // Rebindable keyboard UI keys (Options > Controls > Keyboard), two slots each. Enter (accept) and Escape (back)
    // always keep working as well, so a bad binding can never lock the player out of the menus.
    public static readonly Keys[] DefaultUiAccept = { Keys.Enter, Keys.Space }, DefaultUiBack = { Keys.Escape, Keys.Back },
        DefaultUiPause = { Keys.Enter, Keys.Escape }, DefaultUiReset = { Keys.R, Keys.None };
    public Keys[] UiAccept { get; set; } = (Keys[])DefaultUiAccept.Clone();
    public Keys[] UiBack { get; set; } = (Keys[])DefaultUiBack.Clone();
    public Keys[] UiPause { get; set; } = (Keys[])DefaultUiPause.Clone();
    public Keys[] UiReset { get; set; } = (Keys[])DefaultUiReset.Clone();
    private bool AnyPressed(Keys[] k) { foreach (var x in k) if (x != Keys.None && IsPressed(x)) return true; return false; }
    private bool AnyHeld(Keys[] k) { foreach (var x in k) if (x != Keys.None && IsHeld(x)) return true; return false; }
    /// <summary>Gameplay pause (PlayState PAUSE): Escape-style toggle keys (every Pause binding except Enter) or the pad Pause button.</summary>
    public bool PauseTogglePressed { get { foreach (var x in UiPause) if (x != Keys.None && x != Keys.Enter && IsPressed(x)) return true; return IsPressed(Keys.Escape) && System.Array.IndexOf(UiPause, Keys.Escape) >= 0 || IsGamePadPressed(PauseButton); } }
    /// <summary>Enter opens the pause menu only while it is a Pause binding (default).</summary>
    public bool PauseOpenEnterPressed => System.Array.IndexOf(UiPause, Keys.Enter) >= 0 && IsPressed(Keys.Enter);
    public bool ResetPressed => AnyPressed(UiReset);

    /// <summary>Restore every keyboard + controller binding to the defaults.</summary>
    public void ResetKeyboardBindings()
    {
        NoteKeysAlt = new[] { Keys.D, Keys.F, Keys.J, Keys.K }; NoteKeysArrow = new[] { Keys.Left, Keys.Down, Keys.Up, Keys.Right };
        UiAccept = (Keys[])DefaultUiAccept.Clone(); UiBack = (Keys[])DefaultUiBack.Clone();
        UiPause = (Keys[])DefaultUiPause.Clone(); UiReset = (Keys[])DefaultUiReset.Clone();
    }
    public void ResetControllerBindings()
    {
        NoteButtons = new[] { Buttons.DPadLeft, Buttons.DPadDown, Buttons.DPadUp, Buttons.DPadRight };
        NoteFaceButtons = new[] { Buttons.X, Buttons.A, Buttons.Y, Buttons.B };
        NoteTriggerButtons = new[] { Buttons.LeftTrigger, Buttons.LeftShoulder, Buttons.RightShoulder, Buttons.RightTrigger };
        ConfirmButton = Buttons.A; CancelButton = Buttons.B; PauseButton = Buttons.Start; SwitchCharButton = Buttons.Y;
    }

    // When true, face buttons (A/B/X/Y) are used for notes instead of menu navigation
    // This is set to true during gameplay and false in menus
    public bool GameplayMode { get; set; }
    
    // Raw key/button queries used by the Lua scripting API
    public bool KeyJustPressed(Keys key) => _currentKeyboard.IsKeyDown(key) && !_previousKeyboard.IsKeyDown(key);
    public bool KeyHeld(Keys key) => _currentKeyboard.IsKeyDown(key);
    public bool KeyReleased(Keys key) => !_currentKeyboard.IsKeyDown(key) && _previousKeyboard.IsKeyDown(key);
    public bool ButtonJustPressed(Buttons button) => _currentGamePad.IsButtonDown(button) && !_previousGamePad.IsButtonDown(button);
    public bool ButtonHeld(Buttons button) => _currentGamePad.IsButtonDown(button);
    public bool ButtonReleased(Buttons button) => !_currentGamePad.IsButtonDown(button) && _previousGamePad.IsButtonDown(button);
    public float GamePadAxisValue(string axis)
    {
        var sticks = _currentGamePad.ThumbSticks;
        var triggers = _currentGamePad.Triggers;
        return axis?.ToLowerInvariant() switch
        {
            "leftx" or "left_x" => sticks.Left.X,
            "lefty" or "left_y" => sticks.Left.Y,
            "rightx" or "right_x" => sticks.Right.X,
            "righty" or "right_y" => sticks.Right.Y,
            "lefttrigger" or "left_trigger" => triggers.Left,
            "righttrigger" or "right_trigger" => triggers.Right,
            _ => 0f
        };
    }

    // Dev hook for automated visual checks: FNF_CONFIRM_AT=<seconds since launch> injects ONE confirm press.
    private static readonly System.Diagnostics.Stopwatch _devClock = System.Diagnostics.Stopwatch.StartNew();
    private static float? _devConfirmAt = float.TryParse(System.Environment.GetEnvironmentVariable("FNF_CONFIRM_AT"),
        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : null;
    private bool _devConfirmFrame;
    // FNF_BACK_AT=<s> injects ONE back press. With FNF_DEV_SCENECLOCK=1 both times count from DevSceneClock (set by the scene).
    private static float? _devBackAt = float.TryParse(System.Environment.GetEnvironmentVariable("FNF_BACK_AT"),
        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tb) ? tb : null;
    public static System.Func<double> DevSceneClock;
    private bool _devBackFrame;
    /// <summary>Clock of the dev screenshot harness (set by FNFGame) so injected inputs and FNF_SHOTS share one time base.</summary>
    public static System.Func<double> DevShotClock;
    /// <summary>Dev: name of a one-off screenshot the desktop host saves at the next frame (FNF_SHOT_DIR/&lt;name&gt;.png).</summary>
    public static string DevShotRequest;
    private static double DevNow() => System.Environment.GetEnvironmentVariable("FNF_DEV_SCENECLOCK") == "1" ? (DevSceneClock?.Invoke() ?? -1) : _devClock.Elapsed.TotalSeconds;
    private static double DevKeysNow() => DevShotClock?.Invoke() ?? _devClock.Elapsed.TotalSeconds;
    private bool DevConfirmDue() => _devConfirmFrame;
    // FNF_DEVKEYS="t:Key,t:Key" injects one-frame key presses (Keys enum names) at t seconds (wall clock / DevSceneClock).
    private static readonly System.Collections.Generic.List<(double t, Keys k)> _devKeys = ParseDevKeys();
    private readonly System.Collections.Generic.HashSet<Keys> _devKeysFrame = new();
    private static System.Collections.Generic.List<(double, Keys)> ParseDevKeys()
    {
        var l = new System.Collections.Generic.List<(double, Keys)>();
        var v = System.Environment.GetEnvironmentVariable("FNF_DEVKEYS");
        if (string.IsNullOrEmpty(v)) return l;
        foreach (var part in v.Split(','))
        {
            var kv = part.Split(':');
            if (kv.Length == 2 && double.TryParse(kv[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var t)
                && System.Enum.TryParse<Keys>(kv[1].Trim(), true, out var k)) l.Add((t, k));
        }
        l.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return l;
    }
    /// <summary>True once any keyboard key has been pressed this session (Xbox prompts default to the controller).</summary>
    public bool KeyboardUsedOnce { get; private set; }

    // FNF_DEVPAD="t:Button[+Button],..." -> one frame with those GamePad buttons down (Buttons enum names, DPadUp.. included),
    // FNF_DEVMOUSE="t:x:y[:up],..." -> pointer moved to window pixel (x,y) and left button down for one frame (":up" = move only).
    // The pointer then stays at the last injected position. Same time base as FNF_DEVKEYS.
    private static readonly System.Collections.Generic.List<(double t, string v)> _devPad = ParseDevList("FNF_DEVPAD");
    private static readonly System.Collections.Generic.List<(double t, string v)> _devMouse = ParseDevList("FNF_DEVMOUSE");
    private static Point? _devMousePos;
    private static GamePadState _devPadHold; private static double _devPadHoldUntil = -1; private static bool _devPadPrevFrame;
    private static System.Collections.Generic.List<(double, string)> ParseDevList(string name)
    {
        var l = new System.Collections.Generic.List<(double, string)>();
        var v = System.Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(v)) return l;
        foreach (var part in v.Split(','))
        {
            int i = part.IndexOf(':');
            if (i > 0 && double.TryParse(part.Substring(0, i), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var t))
                l.Add((t, part.Substring(i + 1).Trim()));
        }
        l.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return l;
    }
    private void DevInjectPadMouse()
    {
        if (_devPad.Count == 0 && _devMouse.Count == 0 && _devMousePos == null && _devPadHoldUntil < 0) return;
        double now = DevKeysNow();
        bool devPrev = _devPadPrevFrame; _devPadPrevFrame = false;
        bool due = _devPad.Count > 0 && now >= _devPad[0].t;
        // a new entry right after a held one (or after a hitch that swallowed the release) first gets one released frame,
        // otherwise the two presses merge into one hold and the second press is lost
        if (due && devPrev) { _devPadHoldUntil = -1; due = false; }
        if (_devPadHoldUntil >= 0) { if (now < _devPadHoldUntil) { _currentGamePad = _devPadHold; _devPadPrevFrame = true; } else _devPadHoldUntil = -1; }
        if (due)
        {
            Buttons b = 0; bool du = false, dd = false, dl = false, dr = false;
            string pv = _devPad[0].v; double hold = 0; int ti = pv.IndexOf('~');   // "Start~0.15" = held 0.15 s (real button press)
            if (ti > 0) { double.TryParse(pv.Substring(ti + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out hold); pv = pv.Substring(0, ti); }
            foreach (var n in pv.Split('+'))
                if (System.Enum.TryParse<Buttons>(n.Trim(), true, out var bb))
                {
                    if (bb == Buttons.DPadUp) du = true; else if (bb == Buttons.DPadDown) dd = true;
                    else if (bb == Buttons.DPadLeft) dl = true; else if (bb == Buttons.DPadRight) dr = true; else b |= bb;
                }
            ButtonState S(bool x) => x ? ButtonState.Pressed : ButtonState.Released;
            _currentGamePad = new GamePadState(new GamePadThumbSticks(), new GamePadTriggers(), new GamePadButtons(b), new GamePadDPad(S(du), S(dd), S(dl), S(dr)));
            if (hold > 0) { _devPadHold = _currentGamePad; _devPadHoldUntil = now + hold; }
            _devPadPrevFrame = true;
            System.Console.WriteLine($"[DEVPAD] {_devPad[0].v} t={now:F3}");
            LastDevice = InputDevice.Controller;   // simulated pad counts as "using a controller"
            _devPad.RemoveAt(0);
        }
        bool down = false;
        if (_devMouse.Count > 0 && now >= _devMouse[0].t)
        {
            var f = _devMouse[0].v.Split(':');
            _devMousePos = new Point(int.Parse(f[0]), int.Parse(f[1]));
            down = !(f.Length > 2 && (f[2] == "up" || f[2] == "release" || f[2] == "wheel"));
            // editor dev hooks: ":down" keeps the button held (drag) until a later ":release"; ":wheel:N" scrolls
            if (f.Length > 2 && f[2] == "down") _devMouseHeld = true;
            if (f.Length > 2 && f[2] == "release") _devMouseHeld = false;
            if (f.Length > 3 && f[2] == "wheel") _devWheel += int.Parse(f[3]) * 120;
            System.Console.WriteLine($"[DEVMOUSE] {_devMouse[0].v} t={now:F3}");
            _devMouse.RemoveAt(0);
        }
        if (_devMousePos is Point mp)
            _currentMouse = new MouseState(mp.X, mp.Y, _currentMouse.ScrollWheelValue + _devWheel, down || _devMouseHeld ? ButtonState.Pressed : ButtonState.Released,
                ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
    }
    /// <summary>True when FNF_DEVMOUSE currently holds the pointer (scenes reading Mouse.GetState() directly should use Input).</summary>
    public static bool DevMouseActive => _devMousePos != null;
    private static bool _devMouseHeld;
    private static int _devWheel;
    /// <summary>Filtered mouse (controller emulation removed, parked on Xbox). Use instead of Mouse.GetState().</summary>
    public MouseState MouseNow => _currentMouse;
    public MouseState MousePrev => _previousMouse;
    public bool MouseLeftHeld => _currentMouse.LeftButton == ButtonState.Pressed;
    public bool MouseRightHeld => _currentMouse.RightButton == ButtonState.Pressed;
    public bool MouseMiddleHeld => _currentMouse.MiddleButton == ButtonState.Pressed;

    private static readonly bool _devNoKeyboard = System.Environment.GetEnvironmentVariable("FNF_DEV_NOKEYBOARD") == "1";

    public void Update()
    {
        _devConfirmFrame = false;
        _devBackFrame = false; if (_devBackAt.HasValue && DevNow() >= _devBackAt.Value) { _devBackFrame = true; _devBackAt = null; System.Console.WriteLine($"[DEVBACK] wall={_devClock.Elapsed.TotalSeconds:F3}"); }
        if (_devConfirmAt.HasValue && DevNow() >= _devConfirmAt.Value) { _devConfirmFrame = true; _devConfirmAt = null; System.Console.WriteLine($"[DEVCONFIRM] wall={_devClock.Elapsed.TotalSeconds:F3}"); }
        { double now = _devClock.Elapsed.TotalSeconds; UpdateRumble((float)(now - _lastRumbleT)); _lastRumbleT = now; }
        _devKeysFrame.Clear();
        if (_devKeys.Count > 0 && DevKeysNow() >= _devKeys[0].t)   // at most one injected key per frame (sorted by time)
        { _devKeysFrame.Add(_devKeys[0].k); System.Console.WriteLine($"[DEVKEY] {_devKeys[0].k} t={DevKeysNow():F3}"); _devKeys.RemoveAt(0); }
        _previousKeyboard = _currentKeyboard;
        _previousGamePad = _currentGamePad;
        _previousMouse = _currentMouse;
        _currentKeyboard = Keyboard.GetState();
        if (_devNoKeyboard) _currentKeyboard = default;   // dev FNF_DEV_NOKEYBOARD=1: automated runs ignore real keys (other tools on a shared PC type into the foreground window)
#if ANDROID || TOUCH_TEST
        _currentKeyboard = FNF_MonoGame.Mobile.TouchControls.Apply(_currentKeyboard, this);   // Android touch UI layer
#endif
        _currentGamePad = ReadActiveGamePad();
        _currentMouse = Mouse.GetState();
        DevInjectPadMouse(); // FNF_DEVPAD / FNF_DEVMOUSE dev hooks
        FilterControllerMouse(); // controller->pointer emulation (Xbox mouse mode, Steam desktop layout) is not a mouse
        _previousScrollValue = _currentScrollValue;
        _currentScrollValue = _currentMouse.ScrollWheelValue;
        
        // Track left stick for press detection (digital from analog)
        bool stickUp = _currentGamePad.ThumbSticks.Left.Y > STICK_DEADZONE;
        bool stickDown = _currentGamePad.ThumbSticks.Left.Y < -STICK_DEADZONE;
        bool stickLeft = _currentGamePad.ThumbSticks.Left.X < -STICK_DEADZONE;
        bool stickRight = _currentGamePad.ThumbSticks.Left.X > STICK_DEADZONE;
        
        // Update note inputs
        for (int i = 0; i < 4; i++)
        {
            bool keyHeld = _currentKeyboard.IsKeyDown(NoteKeysAlt[i]) || 
                          _currentKeyboard.IsKeyDown(NoteKeysArrow[i]) ||
                          _currentGamePad.IsButtonDown(NoteButtons[i]) ||
                          _currentGamePad.IsButtonDown(NoteTriggerButtons[i]);
            
            // Face buttons only count as note inputs during gameplay
            if (GameplayMode)
                keyHeld = keyHeld || _currentGamePad.IsButtonDown(NoteFaceButtons[i]);
            
            bool prevHeld = _previousKeyboard.IsKeyDown(NoteKeysAlt[i]) || 
                           _previousKeyboard.IsKeyDown(NoteKeysArrow[i]) ||
                           _previousGamePad.IsButtonDown(NoteButtons[i]) ||
                           _previousGamePad.IsButtonDown(NoteTriggerButtons[i]);
            
            if (GameplayMode)
                prevHeld = prevHeld || _previousGamePad.IsButtonDown(NoteFaceButtons[i]);
            
            NoteHeld[i] = keyHeld;
            NotePressed[i] = keyHeld && !prevHeld;
            NoteReleased[i] = !keyHeld && prevHeld;
        }
        
        // Auto-detect which input device was used last
        // Check if any keyboard key was pressed this frame
#if WINDOWS_UAP
        _currentKeyboard = StripGamepadVirtualKeys(_currentKeyboard);
#endif
        if (_currentKeyboard.GetPressedKeyCount() > 0) KeyboardUsedOnce = true;
        if (_currentKeyboard.GetPressedKeyCount() > 0 && _previousKeyboard.GetPressedKeyCount() == 0)
            LastDevice = InputDevice.Keyboard;
        else if (_currentKeyboard.GetPressedKeyCount() > _previousKeyboard.GetPressedKeyCount())
            LastDevice = InputDevice.Keyboard;
        // Check if any gamepad button was pressed this frame
        if (_currentGamePad.IsConnected)
        {
            bool anyGpNow = _currentGamePad.IsButtonDown(Buttons.A) || _currentGamePad.IsButtonDown(Buttons.B) ||
                _currentGamePad.IsButtonDown(Buttons.X) || _currentGamePad.IsButtonDown(Buttons.Y) ||
                _currentGamePad.IsButtonDown(Buttons.DPadUp) || _currentGamePad.IsButtonDown(Buttons.DPadDown) ||
                _currentGamePad.IsButtonDown(Buttons.DPadLeft) || _currentGamePad.IsButtonDown(Buttons.DPadRight) ||
                _currentGamePad.IsButtonDown(Buttons.LeftShoulder) || _currentGamePad.IsButtonDown(Buttons.RightShoulder) ||
                _currentGamePad.IsButtonDown(Buttons.LeftTrigger) || _currentGamePad.IsButtonDown(Buttons.RightTrigger) ||
                _currentGamePad.IsButtonDown(Buttons.Start) || _currentGamePad.IsButtonDown(Buttons.Back) ||
                stickUp || stickDown || stickLeft || stickRight;
            bool anyGpPrev = _previousGamePad.IsButtonDown(Buttons.A) || _previousGamePad.IsButtonDown(Buttons.B) ||
                _previousGamePad.IsButtonDown(Buttons.X) || _previousGamePad.IsButtonDown(Buttons.Y) ||
                _previousGamePad.IsButtonDown(Buttons.DPadUp) || _previousGamePad.IsButtonDown(Buttons.DPadDown) ||
                _previousGamePad.IsButtonDown(Buttons.DPadLeft) || _previousGamePad.IsButtonDown(Buttons.DPadRight) ||
                _previousGamePad.IsButtonDown(Buttons.LeftShoulder) || _previousGamePad.IsButtonDown(Buttons.RightShoulder) ||
                _previousGamePad.IsButtonDown(Buttons.LeftTrigger) || _previousGamePad.IsButtonDown(Buttons.RightTrigger) ||
                _previousGamePad.IsButtonDown(Buttons.Start) || _previousGamePad.IsButtonDown(Buttons.Back) ||
                _prevStickUp || _prevStickDown || _prevStickLeft || _prevStickRight;
            if (anyGpNow && !anyGpPrev)
                LastDevice = InputDevice.Controller;
        }

        // System keyboard (Xbox): while it is open, and until every button is released after it closes, the game sees no
        // new presses - the A/B/Menu that typed or closed the keyboard must not also click the menu behind it.
        if (SystemKeyboard.HoldInput)
        {
            bool anyDown = _currentKeyboard.GetPressedKeyCount() > 0 || stickUp || stickDown || stickLeft || stickRight;
            if (_currentGamePad.IsConnected)
                foreach (var b in HoldButtons) if (_currentGamePad.IsButtonDown(b)) { anyDown = true; break; }
            _previousKeyboard = _currentKeyboard; _previousGamePad = _currentGamePad;
            _prevStickUp = stickUp; _prevStickDown = stickDown; _prevStickLeft = stickLeft; _prevStickRight = stickRight;
            SystemKeyboard.InputReleased(anyDown);
            return;
        }

        // Save stick state for next frame
        _prevStickUp = stickUp;
        _prevStickDown = stickDown;
        _prevStickLeft = stickLeft;
        _prevStickRight = stickRight;
    }
    
    // ---- any connected pad (XBOX-INPUT 2) ------------------------------------------------------------------------
    // MonoGame maps UWP Windows.Gaming.Input.Gamepad.Gamepads[i] to PlayerIndex i; on a console the controller in
    // use is not always index 0 (second pad, reconnect, the pad that signed in). Poll all four and follow the one
    // that had input last (first connected one by default).
    private static bool AnyPadInput(GamePadState s)
    {
        if (!s.IsConnected) return false;
        if (s.Buttons.A == ButtonState.Pressed || s.Buttons.B == ButtonState.Pressed || s.Buttons.X == ButtonState.Pressed || s.Buttons.Y == ButtonState.Pressed
            || s.Buttons.Start == ButtonState.Pressed || s.Buttons.Back == ButtonState.Pressed
            || s.Buttons.LeftShoulder == ButtonState.Pressed || s.Buttons.RightShoulder == ButtonState.Pressed
            || s.DPad.Up == ButtonState.Pressed || s.DPad.Down == ButtonState.Pressed || s.DPad.Left == ButtonState.Pressed || s.DPad.Right == ButtonState.Pressed)
            return true;
        return s.Triggers.Left > 0.5f || s.Triggers.Right > 0.5f || s.ThumbSticks.Left.LengthSquared() > 0.25f;
    }

    private readonly GamePadState[] _padStates = new GamePadState[4];
    private GamePadState ReadActiveGamePad()
    {
        int firstConnected = -1;
        for (int i = 0; i < 4; i++)
        {
            try { _padStates[i] = GamePad.GetState((PlayerIndex)i); } catch { _padStates[i] = default; }
            if (!_padStates[i].IsConnected) continue;
            if (firstConnected < 0) firstConnected = i;
            if (i != ActivePadIndex && AnyPadInput(_padStates[i]) && !AnyPadInput(_padStates[ActivePadIndex]))
            {
                System.Console.WriteLine($"[INPUT] active controller -> pad {i}");
                ActivePadIndex = i;
            }
        }
        if (!_padStates[ActivePadIndex].IsConnected && firstConnected >= 0 && firstConnected != ActivePadIndex)
        {
            System.Console.WriteLine($"[INPUT] active controller -> pad {firstConnected} (pad {ActivePadIndex} not connected)");
            ActivePadIndex = firstConnected;
        }
        return _padStates[ActivePadIndex];
    }

#if WINDOWS_UAP
    // UWP delivers controller buttons ALSO as key events (VirtualKey.GamepadA..GamepadRightThumbstickLeft = 195-218,
    // Navigation* = 136-143). They are not keyboard input: drop them so a pad press never counts as "keyboard used".
    private static KeyboardState StripGamepadVirtualKeys(KeyboardState ks)
    {
        if (ks.GetPressedKeyCount() == 0) return ks;
        var keys = ks.GetPressedKeys();
        bool any = false;
        foreach (var k in keys) { int v = (int)k; if ((v >= 195 && v <= 218) || (v >= 136 && v <= 143)) { any = true; break; } }
        if (!any) return ks;
        var kept = new System.Collections.Generic.List<Keys>(keys.Length);
        foreach (var k in keys) { int v = (int)k; if (!((v >= 195 && v <= 218) || (v >= 136 && v <= 143))) kept.Add(k); }
        return new KeyboardState(kept.ToArray(), ks.CapsLock, ks.NumLock);
    }
#endif

    // ---- controller vs mouse --------------------------------------------------------------------------------
    // On Xbox a XAML app runs in "mouse mode" by default: the stick drives an invisible pointer and A is ALSO a
    // left click at that pointer, so A clicked whatever menu row sat under it (main menu -> MODS) instead of the
    // highlighted one. Steam's desktop controller layout does the same on Windows. Rules:
    //  * Xbox device family: the mouse is ignored completely (parked off-screen, no buttons, no wheel).
    //  * Windows (desktop + UWP): a mouse button/wheel event that starts while a gamepad button is down or within
    //    0.25 s of one is controller emulation -> swallowed until that button is released. Hover still needs real
    //    pointer movement (scenes compare positions; the parked/unchanged pointer never hovers).
    // Dev: FNF_DEV_XBOXFAMILY=1 simulates the Xbox rule on the PC.
    public static readonly bool MouseDisabled = DetectXboxFamily();
    private static bool DetectXboxFamily()
    {
        try
        {
            if (System.Environment.GetEnvironmentVariable("FNF_DEV_XBOXFAMILY") == "1") return true;
#if WINDOWS_UAP
            return Windows.System.Profile.AnalyticsInfo.VersionInfo.DeviceFamily == "Windows.Xbox";
#else
            return false;
#endif
        }
        catch { return false; }
    }
    private static readonly bool _mouseLog = System.Environment.GetEnvironmentVariable("FNF_MOUSELOG") == "1";
    private double _lastPadButtonT = -10;
    private ButtonState _rawPrevL, _rawPrevR, _rawPrevM;
    private bool _swallowL, _swallowR, _swallowM;
    private int _scrollBase; // wheel value with emulated (swallowed) scrolling removed
    private int _rawPrevScroll;
    private static bool AnyPadButtonDown(GamePadState g)
    {
        if (!g.IsConnected && g.Buttons.A == ButtonState.Released && g.Buttons.Start == ButtonState.Released) return false;
        var b = g.Buttons;
        return b.A == ButtonState.Pressed || b.B == ButtonState.Pressed || b.X == ButtonState.Pressed || b.Y == ButtonState.Pressed
            || b.Start == ButtonState.Pressed || b.Back == ButtonState.Pressed || b.LeftShoulder == ButtonState.Pressed
            || b.RightShoulder == ButtonState.Pressed || b.LeftStick == ButtonState.Pressed || b.RightStick == ButtonState.Pressed
            || g.Triggers.Left > 0.3f || g.Triggers.Right > 0.3f;
    }
    private void FilterControllerMouse()
    {
        double now = _devClock.Elapsed.TotalSeconds;
        if (AnyPadButtonDown(_currentGamePad)) _lastPadButtonT = now;
        if (MouseDisabled)
        {
            _currentMouse = new MouseState(-100000, -100000, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released,
                ButtonState.Released, ButtonState.Released);
            return;
        }
        bool padRecent = now - _lastPadButtonT < 0.25;
        var m = _currentMouse;
        if (_mouseLog && m.Position != _previousMouse.Position) System.Console.WriteLine($"[MOUSE] {_previousMouse.Position} -> {m.Position} t={now:F2}");
        ButtonState Filter(ButtonState raw, ref ButtonState rawPrev, ref bool swallow, string name)
        {
            if (raw == ButtonState.Pressed && rawPrev == ButtonState.Released && padRecent)
            { swallow = true; System.Console.WriteLine($"[INPUT] {name} click ignored (controller emulation)"); }
            if (raw == ButtonState.Released) swallow = false;
            rawPrev = raw;
            return swallow ? ButtonState.Released : raw;
        }
        var l = Filter(m.LeftButton, ref _rawPrevL, ref _swallowL, "left");
        var r = Filter(m.RightButton, ref _rawPrevR, ref _swallowR, "right");
        var mid = Filter(m.MiddleButton, ref _rawPrevM, ref _swallowM, "middle");
        int rawScroll = m.ScrollWheelValue;
        if (padRecent) _scrollBase += rawScroll - _rawPrevScroll;   // drop wheel ticks sent by the controller
        _rawPrevScroll = rawScroll;
        if (l != m.LeftButton || r != m.RightButton || mid != m.MiddleButton || _scrollBase != 0)
            _currentMouse = new MouseState(m.X, m.Y, rawScroll - _scrollBase, l, mid, r, m.XButton1, m.XButton2);
    }

    /// <summary>Raw mouse position in back-buffer pixels.</summary>
    public Point RawMousePosition => _currentMouse.Position;
    public bool MouseMoved => !MouseDisabled && _currentMouse.Position != _previousMouse.Position;

    // Controller vibration (Options > Controller vibration). Stops after the given duration.
    private float _rumbleLeft;
    private double _lastRumbleT;
    public void Rumble(float strength, float seconds)
    {
        if (!HighscoreManager.Data.ControllerVibration || !_currentGamePad.IsConnected) return;
        try { GamePad.SetVibration((PlayerIndex)ActivePadIndex, strength, strength); _rumbleLeft = seconds; } catch { }
    }
    public void UpdateRumble(float dt)
    {
        if (_rumbleLeft <= 0) return;
        _rumbleLeft -= dt;
        if (_rumbleLeft <= 0) try { GamePad.SetVibration((PlayerIndex)ActivePadIndex, 0f, 0f); } catch { }
    }

    public bool IsPressed(Keys key)
    {
        if (_devKeysFrame.Count > 0 && _devKeysFrame.Contains(key)) return true;
        return _currentKeyboard.IsKeyDown(key) && !_previousKeyboard.IsKeyDown(key);
    }
    
    public bool IsHeld(Keys key)
    {
        return _currentKeyboard.IsKeyDown(key) || (_devKeysFrame.Count > 0 && _devKeysFrame.Contains(key));   // FNF_DEVKEYS = one-frame hold too
    }
    
    public bool IsReleased(Keys key)
    {
        return !_currentKeyboard.IsKeyDown(key) && _previousKeyboard.IsKeyDown(key);
    }
    
    private static readonly Buttons[] HoldButtons = { Buttons.A, Buttons.B, Buttons.X, Buttons.Y, Buttons.Start, Buttons.Back, Buttons.LeftShoulder, Buttons.RightShoulder,
        Buttons.LeftTrigger, Buttons.RightTrigger, Buttons.DPadUp, Buttons.DPadDown, Buttons.DPadLeft, Buttons.DPadRight, Buttons.LeftStick, Buttons.RightStick };

    public bool IsGamePadPressed(Buttons button)
    {
        return _currentGamePad.IsButtonDown(button) && !_previousGamePad.IsButtonDown(button);
    }
    
    public bool IsGamePadHeld(Buttons button)
    {
        return _currentGamePad.IsButtonDown(button);
    }
    
    // Left stick press detection (digital from analog with deadzone)
    private bool StickUpPressed => _currentGamePad.ThumbSticks.Left.Y > STICK_DEADZONE && !_prevStickUp;
    private bool StickDownPressed => _currentGamePad.ThumbSticks.Left.Y < -STICK_DEADZONE && !_prevStickDown;
    private bool StickLeftPressed => _currentGamePad.ThumbSticks.Left.X < -STICK_DEADZONE && !_prevStickLeft;
    private bool StickRightPressed => _currentGamePad.ThumbSticks.Left.X > STICK_DEADZONE && !_prevStickRight;
    
    /// <summary>
    /// Mouse scroll wheel delta this frame (positive = scroll up, negative = scroll down).
    /// </summary>
    public int ScrollDelta => _currentScrollValue - _previousScrollValue;
    public bool MouseLeftPressed => _currentMouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
    
    // Menu navigation: Keyboard + DPad + Left Stick
    public bool ConfirmPressed => DevConfirmDue() || IsPressed(Keys.Enter) || AnyPressed(UiAccept) 
        || (!GameplayMode && IsGamePadPressed(ConfirmButton)) || IsGamePadPressed(Buttons.Start);
    /// <summary>Cutscene/dialogue/game-over controls inside a song (GameplayMode on): A/B count even though they are note buttons
    /// in gameplay (V-Slice CUTSCENE_ADVANCE/ACCEPT = pad A, BACK = pad B).</summary>
    public bool CutsceneAdvancePressed => ConfirmPressed || IsGamePadPressed(ConfirmButton);
    public bool CutsceneBackPressed => BackPressed || IsGamePadPressed(CancelButton);
    public bool BackPressed => _devBackFrame || IsPressed(Keys.Escape) || AnyPressed(UiBack) 
        || (!GameplayMode && IsGamePadPressed(CancelButton)) || IsGamePadPressed(Buttons.Back);
    /// <summary>
    /// Confirm button held (keyboard or controller).
    /// </summary>
    public bool ConfirmHeld => IsHeld(Keys.Enter) || AnyHeld(UiAccept)
        || (!GameplayMode && IsGamePadHeld(ConfirmButton)) || IsGamePadHeld(Buttons.Start);
    /// <summary>
    /// Pause button held (keyboard or controller).
    /// </summary>
    public bool PauseHeld => IsHeld(Keys.LeftShift) || IsHeld(Keys.RightShift)
        || IsGamePadHeld(PauseButton);
    public bool UpPressed => IsPressed(Keys.Up) || IsPressed(Keys.W) 
        || IsGamePadPressed(Buttons.DPadUp) || StickUpPressed;
    public bool DownPressed => IsPressed(Keys.Down) || IsPressed(Keys.S) 
        || IsGamePadPressed(Buttons.DPadDown) || StickDownPressed;
    public bool LeftPressed => IsPressed(Keys.Left) || IsPressed(Keys.A) 
        || IsGamePadPressed(Buttons.DPadLeft) || StickLeftPressed;
    public bool RightPressed => IsPressed(Keys.Right) || IsPressed(Keys.D) 
        || IsGamePadPressed(Buttons.DPadRight) || StickRightPressed;

    // Menu navigation held state (for hold-repeat in menus)
    public bool UpHeld => IsHeld(Keys.Up) || IsHeld(Keys.W)
        || IsGamePadHeld(Buttons.DPadUp) || _currentGamePad.ThumbSticks.Left.Y > STICK_DEADZONE;
    public bool DownHeld => IsHeld(Keys.Down) || IsHeld(Keys.S)
        || IsGamePadHeld(Buttons.DPadDown) || _currentGamePad.ThumbSticks.Left.Y < -STICK_DEADZONE;
    public bool LeftHeld => IsHeld(Keys.Left) || IsHeld(Keys.A)
        || IsGamePadHeld(Buttons.DPadLeft) || _currentGamePad.ThumbSticks.Left.X < -STICK_DEADZONE;
    public bool RightHeld => IsHeld(Keys.Right) || IsHeld(Keys.D)
        || IsGamePadHeld(Buttons.DPadRight) || _currentGamePad.ThumbSticks.Left.X > STICK_DEADZONE;

    // Switch character (Freeplay -> Character Select). Tab on keyboard, configurable button on controller.
    public bool SwitchCharPressed => IsPressed(Keys.Tab)
        || (!GameplayMode && IsGamePadPressed(SwitchCharButton));

    /// <summary>
    /// Returns the first key that was just pressed this frame, or null if none.
    /// Used for key rebinding capture.
    /// </summary>
    public Keys? GetAnyKeyPressed()
    {
        foreach (var dk in _devKeysFrame) if (dk != Keys.Escape) return dk;   // FNF_DEVKEYS (rebind tests)
        var pressed = _currentKeyboard.GetPressedKeys();
        foreach (var key in pressed)
        {
            if (!_previousKeyboard.IsKeyDown(key)
                && key != Keys.None && key != Keys.Escape)
                return key;
        }
        return null;
    }

    /// <summary>
    /// Returns the first gamepad button that was just pressed this frame, or null if none.
    /// Used for controller rebinding capture. Excludes Start/Back (reserved for navigation).
    /// </summary>
    /// <summary>
    /// FlxG.keys.pressed.ANY minus the volume keys (AttractState skip). Port: any gamepad button counts too (Xbox).
    /// </summary>
    public bool AnyInputHeld
    {
        get
        {
            if (_devKeysFrame.Count > 0) return true;
            foreach (var k in _currentKeyboard.GetPressedKeys())
                if (k != Keys.None && k != Keys.OemMinus && k != Keys.OemPlus && k != Keys.D0 && k != Keys.NumPad0
                    && k != Keys.Add && k != Keys.Subtract) return true;
            foreach (var btn in s_anyPadButtons) if (_currentGamePad.IsButtonDown(btn)) return true;
            return false;
        }
    }
    private static readonly Buttons[] s_anyPadButtons = {
        Buttons.A, Buttons.B, Buttons.X, Buttons.Y, Buttons.Start, Buttons.Back,
        Buttons.DPadUp, Buttons.DPadDown, Buttons.DPadLeft, Buttons.DPadRight,
        Buttons.LeftShoulder, Buttons.RightShoulder, Buttons.LeftTrigger, Buttons.RightTrigger,
        Buttons.LeftStick, Buttons.RightStick };

    public Buttons? GetAnyButtonPressed()
    {
        Buttons[] candidates = {
            Buttons.A, Buttons.B, Buttons.X, Buttons.Y,
            Buttons.DPadUp, Buttons.DPadDown, Buttons.DPadLeft, Buttons.DPadRight,
            Buttons.LeftShoulder, Buttons.RightShoulder,
            Buttons.LeftTrigger, Buttons.RightTrigger,
            Buttons.LeftStick, Buttons.RightStick,
            Buttons.Start, Buttons.Back,
            Buttons.LeftThumbstickUp, Buttons.LeftThumbstickDown, Buttons.LeftThumbstickLeft, Buttons.LeftThumbstickRight,
            Buttons.RightThumbstickUp, Buttons.RightThumbstickDown, Buttons.RightThumbstickLeft, Buttons.RightThumbstickRight
        };
        foreach (var btn in candidates)
        {
            if (_currentGamePad.IsButtonDown(btn) && !_previousGamePad.IsButtonDown(btn))
                return btn;
        }
        return null;
    }
    
    /// <summary>
    /// Load key bindings from save data.
    /// </summary>
    public void LoadBindings()
    {
        var data = HighscoreManager.Data;
        if (data.NoteKeysAlt != null && data.NoteKeysAlt.Length == 4)
        {
            for (int i = 0; i < 4; i++)
            {
                if (System.Enum.TryParse<Keys>(data.NoteKeysAlt[i], out var k))
                    NoteKeysAlt[i] = k;
            }
        }
        if (data.NoteKeysArrow != null && data.NoteKeysArrow.Length == 4)
        {
            for (int i = 0; i < 4; i++)
            {
                if (System.Enum.TryParse<Keys>(data.NoteKeysArrow[i], out var k))
                    NoteKeysArrow[i] = k;
            }
        }
        if (data.NoteGamepadDPad != null && data.NoteGamepadDPad.Length == 4)
        {
            for (int i = 0; i < 4; i++)
            {
                if (System.Enum.TryParse<Buttons>(data.NoteGamepadDPad[i], out var b))
                    NoteButtons[i] = b;
            }
        }
        if (data.NoteGamepadFace != null && data.NoteGamepadFace.Length == 4)
        {
            for (int i = 0; i < 4; i++)
            {
                if (System.Enum.TryParse<Buttons>(data.NoteGamepadFace[i], out var b))
                    NoteFaceButtons[i] = b;
            }
        }
        if (data.NoteGamepadTrigger != null && data.NoteGamepadTrigger.Length == 4)
        {
            for (int i = 0; i < 4; i++)
            {
                if (System.Enum.TryParse<Buttons>(data.NoteGamepadTrigger[i], out var b))
                    NoteTriggerButtons[i] = b;
            }
        }
        if (!string.IsNullOrEmpty(data.ConfirmGamepadButton) &&
            System.Enum.TryParse<Buttons>(data.ConfirmGamepadButton, out var cb))
            ConfirmButton = cb;
        if (!string.IsNullOrEmpty(data.CancelGamepadButton) &&
            System.Enum.TryParse<Buttons>(data.CancelGamepadButton, out var xb))
            CancelButton = xb;
        if (!string.IsNullOrEmpty(data.PauseGamepadButton) &&
            System.Enum.TryParse<Buttons>(data.PauseGamepadButton, out var pb))
            PauseButton = pb;
        if (!string.IsNullOrEmpty(data.SwitchCharGamepadButton) &&
            System.Enum.TryParse<Buttons>(data.SwitchCharGamepadButton, out var sb))
            SwitchCharButton = sb;
        Keys[] Ui(string[] src, Keys[] def)
        {
            var r = (Keys[])def.Clone();
            if (src != null) for (int i = 0; i < r.Length && i < src.Length; i++) if (System.Enum.TryParse<Keys>(src[i], out var k)) r[i] = k;
            return r;
        }
        UiAccept = Ui(data.UiKeysAccept, DefaultUiAccept); UiBack = Ui(data.UiKeysBack, DefaultUiBack);
        UiPause = Ui(data.UiKeysPause, DefaultUiPause); UiReset = Ui(data.UiKeysReset, DefaultUiReset);
    }
    
    /// <summary>
    /// Save current key bindings to save data.
    /// </summary>
    public void SaveBindings()
    {
        var data = HighscoreManager.Data;
        data.NoteKeysAlt = new string[4];
        data.NoteKeysArrow = new string[4];
        data.NoteGamepadDPad = new string[4];
        data.NoteGamepadFace = new string[4];
        data.NoteGamepadTrigger = new string[4];
        for (int i = 0; i < 4; i++)
        {
            data.NoteKeysAlt[i] = NoteKeysAlt[i].ToString();
            data.NoteKeysArrow[i] = NoteKeysArrow[i].ToString();
            data.NoteGamepadDPad[i] = NoteButtons[i].ToString();
            data.NoteGamepadFace[i] = NoteFaceButtons[i].ToString();
            data.NoteGamepadTrigger[i] = NoteTriggerButtons[i].ToString();
        }
        data.ConfirmGamepadButton = ConfirmButton.ToString();
        data.CancelGamepadButton = CancelButton.ToString();
        data.PauseGamepadButton = PauseButton.ToString();
        data.SwitchCharGamepadButton = SwitchCharButton.ToString();
        string[] U(Keys[] k) => System.Array.ConvertAll(k, x => x.ToString());
        data.UiKeysAccept = U(UiAccept); data.UiKeysBack = U(UiBack); data.UiKeysPause = U(UiPause); data.UiKeysReset = U(UiReset);
        HighscoreManager.SavePreferences();
    }
}
