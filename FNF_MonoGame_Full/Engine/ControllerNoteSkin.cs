using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Which controller icon set the game shows (Xbox or PlayStation): Options > Button Icons (Auto / Xbox / PlayStation).
/// Auto = PlayStation icons while a Sony pad (USB/Bluetooth vendor id 054C) is connected on PC; the Xbox console is
/// always Xbox. Detection: WindowsDX = raw-input HID device names (VID_054C, usage page 1 joystick/gamepad);
/// DesktopGL/Android = SDL controller name/GUID; UWP on a PC = Windows.Gaming.Input.RawGameController vendor id.
/// Dev hook FNF_DEV_PADTYPE=ps|xbox forces one set.
/// </summary>
public static class ControllerIcons
{
    public const string Auto = "Auto", Xbox = "Xbox", PlayStation = "PlayStation";
    public static readonly string[] Choices = { Auto, Xbox, PlayStation };
    private static readonly string DevType = Environment.GetEnvironmentVariable("FNF_DEV_PADTYPE");
    private static bool _detected; private static double _nextPoll; private static readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private static bool _logged;

    /// <summary>Options value (Auto / Xbox / PlayStation).</summary>
    public static string Setting
    {
        get { var s = HighscoreManager.Data.ControllerIconSet; return s == Xbox || s == PlayStation ? s : Auto; }
        set => HighscoreManager.Data.ControllerIconSet = value == Xbox || value == PlayStation ? value : Auto;
    }

    /// <summary>True = draw PlayStation icons (notes, prompts, key-binding menu).</summary>
    public static bool UsePlayStation
    {
        get
        {
            if (DevType != null) return DevType.Equals("ps", StringComparison.OrdinalIgnoreCase) || DevType.Equals("playstation", StringComparison.OrdinalIgnoreCase);
            if (InputManager.IsXboxFamily) return false;               // Xbox console: always Xbox buttons
            string s = Setting;
            if (s == Xbox) return false;
            if (s == PlayStation) return true;
            return DetectedPlayStation;
        }
    }

    /// <summary>Polled at most every 2 s (cheap OS query).</summary>
    public static bool DetectedPlayStation
    {
        get
        {
            double now = _clock.Elapsed.TotalSeconds;
            if (now >= _nextPoll)
            {
                _nextPoll = now + 2.0;
                bool d = false;
                try { d = Detect(); } catch (Exception e) { if (!_logged) { _logged = true; Console.WriteLine("[PADTYPE] detect failed: " + e.Message); } }
                if (d != _detected) Console.WriteLine("[PADTYPE] " + (d ? "PlayStation controller connected -> PS icons" : "no PlayStation controller -> Xbox icons"));
                _detected = d;
            }
            return _detected;
        }
    }

    /// <summary>Gamepad name / identifier looks like a Sony pad (SDL names, GUID with vendor 054c).</summary>
    public static bool IsSonyName(string name, string id = null)
    {
        string n = (name ?? "").ToLowerInvariant(), i = (id ?? "").ToLowerInvariant();
        return n.Contains("playstation") || n.Contains("dualsense") || n.Contains("dualshock") || n.Contains("ps4") || n.Contains("ps5") || n.Contains("ps3")
            || n.Contains("sony") || n.Contains("wireless controller") || i.Contains("054c") || i.Contains("4c05");
    }

#if WINDOWS_UAP
    private static bool Detect()
    {
        foreach (var c in Windows.Gaming.Input.RawGameController.RawGameControllers)
            if (c.HardwareVendorId == 0x054C) return true;
        return false;
    }
#elif FNF_CROSSPLATFORM
    private static bool Detect()
    {
        for (int p = 0; p < 4; p++)
        {
            var cap = GamePad.GetCapabilities(p);
            if (cap.IsConnected && IsSonyName(cap.DisplayName, cap.Identifier)) return true;
        }
        return false;
    }
#else
    // WindowsDX: MonoGame reads XInput only (no names). Sony pads (often behind DS4Windows / Steam Input as a virtual
    // Xbox pad) still show up as raw-input HID devices with vendor id 054C.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RAWINPUTDEVICELIST { public IntPtr hDevice; public uint dwType; }
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceList([System.Runtime.InteropServices.Out] RAWINPUTDEVICELIST[] list, ref uint num, uint size);
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern uint GetRawInputDeviceInfo(IntPtr h, uint cmd, System.Text.StringBuilder data, ref uint size);
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, EntryPoint = "GetRawInputDeviceInfoW")]
    private static extern uint GetRawInputDeviceInfoBytes(IntPtr h, uint cmd, byte[] data, ref uint size);
    private const uint RIDI_DEVICENAME = 0x20000007, RIDI_DEVICEINFO = 0x2000000b, RIM_TYPEHID = 2;

    private static bool Detect()
    {
        uint n = 0, sz = (uint)System.Runtime.InteropServices.Marshal.SizeOf<RAWINPUTDEVICELIST>();
        if (GetRawInputDeviceList(null, ref n, sz) != 0 || n == 0) return false;
        var list = new RAWINPUTDEVICELIST[n];
        if (GetRawInputDeviceList(list, ref n, sz) == uint.MaxValue) return false;
        for (int k = 0; k < n; k++)
        {
            if (list[k].dwType != RIM_TYPEHID) continue;
            uint len = 0; GetRawInputDeviceInfo(list[k].hDevice, RIDI_DEVICENAME, null, ref len);
            if (len == 0 || len > 1024) continue;
            var sb = new System.Text.StringBuilder((int)len + 1);
            if (GetRawInputDeviceInfo(list[k].hDevice, RIDI_DEVICENAME, sb, ref len) == uint.MaxValue) continue;
            if (sb.ToString().IndexOf("VID_054C", StringComparison.OrdinalIgnoreCase) < 0) continue;
            // RID_DEVICE_INFO: cbSize, dwType, then hid: dwVendorId, dwProductId, dwVersionNumber, usUsagePage, usUsage
            uint isz = 32; var info = new byte[32]; BitConverter.GetBytes(32u).CopyTo(info, 0);
            if (GetRawInputDeviceInfoBytes(list[k].hDevice, RIDI_DEVICEINFO, info, ref isz) == uint.MaxValue) return true;
            ushort page = BitConverter.ToUInt16(info, 20), usage = BitConverter.ToUInt16(info, 22);
            if (page == 1 && (usage == 4 || usage == 5)) return true;
        }
        return false;
    }
#endif

    /// <summary>Prompt glyph name prefix ("xbox_" / "ps_") for images/ui/prompts.</summary>
    public static string Prefix => UsePlayStation ? "ps_" : "xbox_";

    /// <summary>Short button name in the current set (key-binding cells, help text).</summary>
    public static string ButtonName(Buttons b)
    {
        bool ps = UsePlayStation;
        return b switch
        {
            Buttons.A => ps ? "Cross" : "A", Buttons.B => ps ? "Circle" : "B", Buttons.X => ps ? "Square" : "X", Buttons.Y => ps ? "Triangle" : "Y",
            Buttons.LeftShoulder => ps ? "L1" : "LB", Buttons.RightShoulder => ps ? "R1" : "RB",
            Buttons.LeftTrigger => ps ? "L2" : "LT", Buttons.RightTrigger => ps ? "R2" : "RT",
            Buttons.Start => ps ? "Options" : "Menu", Buttons.Back => ps ? "Create" : "View",
            Buttons.LeftStick => ps ? "L3" : "LS", Buttons.RightStick => ps ? "R3" : "RS",
            Buttons.DPadUp => "D-Up", Buttons.DPadDown => "D-Down", Buttons.DPadLeft => "D-Left", Buttons.DPadRight => "D-Right",
            Buttons.LeftThumbstickUp => "L-Stick Up", Buttons.LeftThumbstickDown => "L-Stick Down",
            Buttons.LeftThumbstickLeft => "L-Stick Left", Buttons.LeftThumbstickRight => "L-Stick Right",
            Buttons.RightThumbstickUp => "R-Stick Up", Buttons.RightThumbstickDown => "R-Stick Down",
            Buttons.RightThumbstickLeft => "R-Stick Left", Buttons.RightThumbstickRight => "R-Stick Right",
            _ => b.ToString()
        };
    }

    // ---- key-binding menu: "you pressed this button" flash -------------------------------------------------------
    private static Buttons _flashBtn; private static double _flashAt = -10;
    /// <summary>Record a button press (Options > Controls shows that glyph pushed in for a moment).</summary>
    public static void Flash(Buttons b) { _flashBtn = b; _flashAt = _clock.Elapsed.TotalSeconds; }
    public static void ClearFlash() => _flashAt = -10;
    /// <summary>The flashed button is still held: keep it pushed in (restarts the 0.18 s hold window).</summary>
    public static void Hold() => _flashAt = _clock.Elapsed.TotalSeconds;
    public static Buttons LastFlash => _flashBtn;
    /// <summary>0..1 how far the glyph of <paramref name="b"/> is pressed in (held 0.18 s, then springs back in 0.12 s).</summary>
    public static float FlashAmount(Buttons b)
    {
        if (b != _flashBtn) return 0f;
        double t = _clock.Elapsed.TotalSeconds - _flashAt;
        if (t < 0 || t > 0.30) return 0f;
        return t < 0.18 ? 1f : (float)(1 - (t - 0.18) / 0.12);
    }
    public static float SinceFlash => (float)(_clock.Elapsed.TotalSeconds - _flashAt);
}

/// <summary>
/// The approved controller note skins (notepack/tools/export_ingame.py -> images/ui/noteskins/&lt;xbox|ps&gt;/style&lt;N&gt;):
///  1 Kenney colour, 2 Kenney outline, 3 Xelu dark buttons with coloured letters / PS symbols (default),
///  4 FNF neon orbs, 5 FNF arrow + button badge, 6 dark ring glow. Each lane has note / receptor (idle, grey) /
///  confirm (bright + lane glow) art at 1x (108 px) and 2x. Three strum states like a real button:
///  idle = grey receptor; PRESSED (pressed without a note) = the button pushed into its socket (smaller, darker,
///  nudged down); CONFIRM (note hit) = bright button + lane-colour glow with a short pop.
/// </summary>
public static class ControllerNoteSkin
{
    public const int DefaultStyle = 3;
    /// <summary>Note Skin names, index = style - 1 (style 3 keeps the old "Controller Buttons" name).</summary>
    public static readonly string[] StyleNames =
    {
        "Buttons: Kenney Colour", "Buttons: Kenney Outline", "Controller Buttons",
        "Buttons: Neon Orbs", "Buttons: Arrow Badge", "Buttons: Ring Glow"
    };
    /// <summary>FNF lane colours (purple, blue, green, red) used by holds, glows and splashes.</summary>
    public static readonly Color[] LaneColors = { new Color(194, 75, 153), new Color(0, 255, 255), new Color(18, 250, 5), new Color(249, 57, 63) };
    private static readonly string[] LaneNames = { "left", "down", "up", "right" };
    private const float S0 = 108f;           // 1x art size (the approved previews)
    private static readonly string DevStyle = Environment.GetEnvironmentVariable("FNF_DEV_NOTESTYLE");

    /// <summary>Selected style 1..6 (save NoteSkinButtonStyle; 0/invalid = default 3). Dev FNF_DEV_NOTESTYLE=1..6.</summary>
    public static int Style
    {
        get
        {
            if (DevStyle != null && int.TryParse(DevStyle, out int ds) && ds >= 1 && ds <= 6) return ds;
            int s = HighscoreManager.Data.NoteSkinButtonStyle;
            return s >= 1 && s <= 6 ? s : DefaultStyle;
        }
    }

    public static int StyleOfName(string name)
    {
        int i = System.Array.IndexOf(StyleNames, name);
        return i >= 0 ? i + 1 : 0;
    }

    private static readonly Dictionary<string, Texture2D> _cache = new();
    private static AssetManager _assets;
    private static Texture2D Tex(AssetManager assets, string path)
    {
        if (!ReferenceEquals(_assets, assets)) { _cache.Clear(); _assets = assets; }
        if (_cache.TryGetValue(path, out var t)) return t;
        t = assets.LoadTexture(path);
        if (t == assets.Pixel) t = null;
        _cache[path] = t;
        return t;
    }

    /// <summary>Art lane: styles drawn around the BUTTON (1, 2, 3, 6) follow the bound face button
    /// (X/Square = left art, A/Cross = down, Y/Triangle = up, B/Circle = right); styles 4/5 are lane art.</summary>
    private static int ArtLane(int style, int lane, Buttons btn)
    {
        if (style == 4 || style == 5) return lane;
        return btn switch { Buttons.X => 0, Buttons.A => 1, Buttons.Y => 2, Buttons.B => 3, _ => lane };
    }

    /// <summary>kind = note / receptor / confirm; picks the 2x file when drawn bigger than ~1.25x of the 1x art.</summary>
    public static Texture2D Get(AssetManager assets, string kind, int lane, Buttons btn, float drawnPx, int style = 0, bool? ps = null)
    {
        if (style <= 0) style = Style;
        bool p = ps ?? ControllerIcons.UsePlayStation;
        int al = ArtLane(style, lane & 3, btn);
        string basePath = "images/ui/noteskins/" + (p ? "ps" : "xbox") + "/style" + style + "/" + kind + "_" + LaneNames[al];
        Texture2D t = null;
        if (drawnPx > S0 * 1.25f) t = Tex(assets, basePath + "_2x.png");
        return t ?? Tex(assets, basePath + ".png");
    }

    /// <summary>Draws texture <paramref name="t"/> whose note box (S0*k) maps to <paramref name="size"/>, centred on c.</summary>
    private static void DrawArt(SpriteBatch sb, Texture2D t, int k, Vector2 c, float size, Color col, float rot = 0f)
    {
        float s = size / (S0 * k);
        sb.Draw(t, c, null, col, rot, new Vector2(t.Width / 2f, t.Height / 2f), s, SpriteEffects.None, 0f);
    }

    /// <summary>1x / 2x art: note/receptor 108 vs 216 px; confirm (1.12x + 42 px glow pad per side) 204 vs 408 px.</summary>
    private static int K(Texture2D t, bool confirm = false) => t.Width > (confirm ? 300 : S0 * 1.5f) ? 2 : 1;

    /// <summary>Note head centred on <paramref name="center"/>, <paramref name="size"/> = note box (button) size.</summary>
    public static bool DrawNote(SpriteBatch sb, AssetManager assets, int lane, Buttons btn, Vector2 center, float size, float alpha,
        float angle = 0f, float screenScale = 1f, int style = 0, bool? ps = null)
    {
        var t = Get(assets, "note", lane, btn, size * screenScale, style, ps);
        if (t == null) return false;
        DrawArt(sb, t, K(t), center, size, new Color(Color.White, Math.Clamp(alpha, 0f, 1f)), angle);
        return true;
    }

    /// <summary>
    /// Strum (receptor). <paramref name="stateT"/> = seconds since the press/confirm started (-1 = unknown: no pop).
    /// </summary>
    public static bool DrawReceptor(SpriteBatch sb, AssetManager assets, int lane, Buttons btn, Vector2 center, float size,
        bool pressed, bool confirm, float alpha, float stateT = -1f, float screenScale = 1f, int style = 0, bool? ps = null)
    {
        alpha = Math.Clamp(alpha, 0f, 1f);
        float px = size * screenScale;
        var rec = Get(assets, "receptor", lane, btn, px, style, ps);
        if (rec == null) return false;
        if (confirm)
        {
            var cf = Get(assets, "confirm", lane, btn, px, style, ps);
            if (cf == null) return false;
            // pop: 1.10 -> 1.0 over 0.1 s (the confirm art is already 1.12x with its glow)
            float pop = stateT >= 0f && stateT < 0.1f ? 1f + 0.10f * (1f - stateT / 0.1f) : 1f;
            DrawArt(sb, cf, K(cf, true), center, size * pop, new Color(Color.White, alpha));
            return true;
        }
        if (pressed)
        {
            // pushed in: the socket (grey receptor) stays, the button sinks: 88 % size, darker, 3 % lower
            DrawArt(sb, rec, K(rec), center, size, new Color(Color.White, alpha * 0.85f));
            var nt = Get(assets, "note", lane, btn, px, style, ps);
            if (nt == null) return true;
            float k = stateT >= 0f && stateT < 0.05f ? stateT / 0.05f : 1f;      // 50 ms push-in
            float sc = 1f - 0.12f * k;
            float dark = 1f - 0.40f * k;
            DrawArt(sb, nt, K(nt), center + new Vector2(0f, size * 0.03f * k), size * sc,
                new Color(dark, dark, dark, alpha));          // textures are straight alpha (NonPremultiplied)
            return true;
        }
        DrawArt(sb, rec, K(rec), center, size, new Color(Color.White, alpha));
        return true;
    }

    /// <summary>Lane colour (holds / splashes / covers).</summary>
    public static Color LaneColor(int lane) => LaneColors[((lane % 4) + 4) % 4];
}
