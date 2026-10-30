#if ANDROID || TOUCH_TEST
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Scenes;

namespace FNF_MonoGame.Mobile;

/// <summary>
/// ANDROID-ONLY touch UI layer (compiled only into FNF_Android; the PC / Xbox UI is untouched).
/// It never changes scene code: touches are turned into the keys the engine already understands
/// (InputManager merges <see cref="Apply"/> into its keyboard state each frame).
///  * Song (PlayScene while playing): 4 full-height hitbox lanes (left/down/up/right, like FNF mobile ports; slide between
///    lanes, multi-touch for jumps/holds) + an on-screen PAUSE button at the top centre. Android Back = pause.
///  * Menus / pause / game over: big virtual pad (D-pad bottom-left, A = accept, B = back bottom-right),
///    swipe anywhere = arrows (drag up = next item, drag left = next page), tap anywhere else = accept, Android Back = back.
/// Hidden while a gamepad is the last used device (Android TV / controller players).
/// </summary>
public static class TouchControls
{
    public static bool Enabled = true;

    private enum Mode { Menu, Hitbox }
    private sealed class Touch
    {
        public Vector2 Start, Pos, LastEmit; public double T0; public int Button = -1; public bool Swiped, Pause; public Mode Mode;
    }

    private const float W = 1280f, H = 720f;
    private static Mode _mode = Mode.Menu;
    private static readonly Dictionary<int, Touch> _touches = new();
    private static readonly HashSet<Keys> _held = new(), _pulse = new();
    private static readonly bool[] _laneHeld = new bool[4];
    private static readonly bool[] _btnHeld = new bool[6];
    private static double _clock;
    private static readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();
    private static bool _prevBack, _touchSeen;
    private static float _laneFlash0, _laneFlash1, _laneFlash2, _laneFlash3;

    // virtual pad buttons (logical 1280x720): 0 left, 1 down, 2 up, 3 right, 4 accept (A), 5 back (B)
    private static readonly Vector2[] BtnPos = { new(100, 575), new(195, 660), new(195, 490), new(290, 575), new(1170, 590), new(1040, 650) };
    private static readonly float[] BtnSize = { 104, 104, 104, 104, 132, 100 };
    private static readonly Rectangle PauseRect = new(596, 8, 88, 88);
    private static readonly Color[] LaneColors = { new(194, 75, 153), new(0, 255, 255), new(18, 250, 5), new(249, 57, 63) };

    private static bool PadVisible => Enabled && (_touchSeen || !GamePad.GetState(PlayerIndex.One).IsConnected)
        && FNFGame.Instance?.Input?.LastDevice != InputManager.InputDevice.Controller;

    /// <summary>Called by InputManager.Update (ANDROID only) right after Keyboard.GetState().</summary>
    public static KeyboardState Apply(KeyboardState real, InputManager input)
    {
        if (!Enabled) return real;
        try { Update(input); } catch (Exception ex) { Console.WriteLine($"[TOUCH] {ex.Message}"); }
        if (_held.Count == 0 && _pulse.Count == 0) return real;
        var keys = new List<Keys>(real.GetPressedKeys());
        foreach (var k in _held) if (!keys.Contains(k)) keys.Add(k);
        foreach (var k in _pulse) if (!keys.Contains(k)) keys.Add(k);
        return new KeyboardState(keys.ToArray());
    }

    private static Keys PauseKey(InputManager input)
    {
        foreach (var k in input.UiPause) if (k != Keys.None && k != Keys.Enter) return k;
        return Keys.Escape;
    }

    private static void Update(InputManager input)
    {
        var game = FNFGame.Instance;
        if (game == null) return;
        _clock = _sw.Elapsed.TotalSeconds;
        _held.Clear(); _pulse.Clear();

        var scene = game.Scenes?.CurrentScene;
        _mode = scene is PlayScene ps && input.GameplayMode && !ps.Paused && !ps.TouchGameOver ? Mode.Hitbox : Mode.Menu;

        // Android Back button (MonoGame reports it as GamePad Back). Menus already treat it as Back; in a song = pause.
        bool back = GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed;
        if (back && !_prevBack && _mode == Mode.Hitbox) _pulse.Add(PauseKey(input));
        _prevBack = back;

        var dest = game.GetScaledDestination();
        var points = new List<(int Id, Vector2 P, bool Released)>();
        foreach (var tl in TouchPanel.GetState())
        {
            if (tl.State == TouchLocationState.Invalid) continue;
            var lp = new Vector2((tl.Position.X - dest.X) * W / Math.Max(1, dest.Width), (tl.Position.Y - dest.Y) * H / Math.Max(1, dest.Height));
            points.Add((tl.Id, lp, tl.State == TouchLocationState.Released));
        }
        DevTouches(points);
        var alive = new HashSet<int>();
        System.Array.Clear(_laneHeld, 0, 4); System.Array.Clear(_btnHeld, 0, _btnHeld.Length);
        foreach (var (id, p, released) in points)
        {
            alive.Add(id);
            _touchSeen = true;
            if (!_touches.TryGetValue(id, out var t))
            {
                if (released) continue;
                t = new Touch { Start = p, Pos = p, LastEmit = p, T0 = _clock, Mode = _mode };
                if (_mode == Mode.Hitbox) t.Pause = PauseRect.Contains((int)p.X, (int)p.Y);
                else t.Button = HitButton(p);
                if (t.Pause) _pulse.Add(PauseKey(input));
                else if (t.Button >= 0) _pulse.Add(ButtonKey(t.Button));
                _touches[id] = t;
            }
            t.Pos = p;
            if (t.Mode != _mode) { if (released) _touches.Remove(id); continue; }   // finger from the previous mode (e.g. the pause tap): ignore
            if (_mode == Mode.Hitbox)
            {
                if (!t.Pause && !released) _laneHeld[Math.Clamp((int)(p.X / (W / 4f)), 0, 3)] = true;
            }
            else if (t.Button >= 0)
            {
                if (!released) { _btnHeld[t.Button] = true; _held.Add(ButtonKey(t.Button)); }
            }
            else
            {
                // swipe: one arrow per 90 px of drag (natural scrolling: drag up = next item / Down)
                var d = p - t.LastEmit;
                if (Math.Abs(d.Y) >= 90f && Math.Abs(d.Y) > Math.Abs(d.X)) { _pulse.Add(d.Y < 0 ? Keys.Down : Keys.Up); t.LastEmit = p; t.Swiped = true; }
                else if (Math.Abs(d.X) >= 110f && Math.Abs(d.X) > Math.Abs(d.Y)) { _pulse.Add(d.X < 0 ? Keys.Right : Keys.Left); t.LastEmit = p; t.Swiped = true; }
                if (released && !t.Swiped && (p - t.Start).Length() < 28f && _clock - t.T0 < 0.45) _pulse.Add(Keys.Enter);
            }
            if (released) _touches.Remove(id);
        }
        // touches that vanished without a Released event
        if (_touches.Count > alive.Count) { var dead = new List<int>(); foreach (var id in _touches.Keys) if (!alive.Contains(id)) dead.Add(id); foreach (var id in dead) _touches.Remove(id); }

        if (_mode == Mode.Hitbox)
            for (int i = 0; i < 4; i++) if (_laneHeld[i]) _held.Add(input.NoteKeysArrow[i]);
    }

    // Dev hook (tests without a device): FNF_DEVTOUCH="t:x,y[:dur[:x2,y2]];..." = synthetic finger in LOGICAL 1280x720
    // coordinates from t seconds (since the first touch-layer update) for dur seconds (default 0.1), moving to x2,y2.
    private static List<(double T, double Dur, Vector2 A, Vector2 B)> _dev;
    private static double _devStart = -1;
    private static readonly HashSet<int> _devStarted = new();
    private static void DevTouches(List<(int Id, Vector2 P, bool Released)> points)
    {
        if (_dev == null)
        {
            _dev = new();
            var env = Environment.GetEnvironmentVariable("FNF_DEVTOUCH");
            if (!string.IsNullOrEmpty(env))
                foreach (var part in env.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var f = part.Split(':'); var inv = System.Globalization.CultureInfo.InvariantCulture;
                    var a = f[1].Split(','); var A = new Vector2(float.Parse(a[0], inv), float.Parse(a[1], inv));
                    double dur = f.Length > 2 ? double.Parse(f[2], inv) : 0.1;
                    var B = A; if (f.Length > 3) { var b = f[3].Split(','); B = new Vector2(float.Parse(b[0], inv), float.Parse(b[1], inv)); }
                    _dev.Add((double.Parse(f[0], inv), dur, A, B));
                }
        }
        if (_dev.Count == 0) return;
        // same clock as FNF_SHOTS (seconds since process start)
        double now = (DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalSeconds;
        for (int i = 0; i < _dev.Count; i++)
        {
            var d = _dev[i];
            if (now < d.T) continue;
            double k = d.Dur <= 0 ? 1 : Math.Min(1, (now - d.T) / d.Dur);
            bool rel = now >= d.T + d.Dur && _devStarted.Contains(i);
            _devStarted.Add(i);
            points.Add((100000 + i, Vector2.Lerp(d.A, d.B, (float)k), rel));
            if (rel) { Console.WriteLine($"[DEVTOUCH] #{i} released at {now:F2}"); _dev[i] = (double.MaxValue, 0, d.A, d.B); }
        }
    }

    private static Keys ButtonKey(int b) => b switch { 0 => Keys.Left, 1 => Keys.Down, 2 => Keys.Up, 3 => Keys.Right, 4 => Keys.Enter, _ => Keys.Escape };

    private static int HitButton(Vector2 p)
    {
        if (!PadVisible) return -1;
        for (int i = 0; i < BtnPos.Length; i++)
        {
            float r = BtnSize[i] * 0.62f;   // generous hit area (bigger than the drawn button)
            if (Math.Abs(p.X - BtnPos[i].X) <= r && Math.Abs(p.Y - BtnPos[i].Y) <= r) return i;
        }
        return -1;
    }

    // ------------------------------------------------------------------------------------------------ drawing
    private static Texture2D _notes, _receptors;
    private static readonly Rectangle[] NoteSrc = new Rectangle[4], NoteSrcPress = new Rectangle[4];
    private static bool _notesTried;

    /// <summary>V-Slice default note skin (game/skins/default): idle = "<dir> note", pressed = receptor "<dir> confirm".</summary>
    private static void LoadNotes()
    {
        if (_notesTried) return;
        _notesTried = true;
        try
        {
            var assets = FNFGame.Instance.Assets;
            string nx = assets.ResolvePath("game/skins/default/notes.xml"), rx = assets.ResolvePath("game/skins/default/receptors.xml");
            if (nx == null) return;
            string[] dirs = { "left", "down", "up", "right" };
            string nt = global::FNF_MonoGame.Engine.FsCI.ReadAllText(nx), rt = rx != null ? global::FNF_MonoGame.Engine.FsCI.ReadAllText(rx) : "";
            for (int i = 0; i < 4; i++) { NoteSrc[i] = Find(nt, dirs[i] + " note0000"); NoteSrcPress[i] = Find(rt, dirs[i] + " confirm0000"); }
            _notes = FunkUI.Tex("game/skins/default/notes.png");
            _receptors = rx != null ? FunkUI.Tex("game/skins/default/receptors.png") : null;
        }
        catch (Exception ex) { Console.WriteLine($"[TOUCH] note art: {ex.Message}"); }
    }

    private static Rectangle Find(string xml, string name)
    {
        var m = Regex.Match(xml, "name=\"" + Regex.Escape(name) + "\"\\s+x=\"(\\d+)\"\\s+y=\"(\\d+)\"\\s+width=\"(\\d+)\"\\s+height=\"(\\d+)\"");
        return m.Success ? new Rectangle(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value)) : Rectangle.Empty;
    }

    /// <summary>Called by FNFGame.Draw (ANDROID only) on the 1280x720 scene target, after the scene and HUD overlays.</summary>
    public static void Draw(SpriteBatch sb)
    {
        if (!Enabled || FNFGame.Instance == null) return;
        if (Environment.GetEnvironmentVariable("FNF_HIDE_TOUCH") == "1") return;
        LoadNotes();
        FunkUI.Begin(sb);
        try
        {
            if (_mode == Mode.Hitbox) DrawHitbox();
            else if (PadVisible) DrawPad();
        }
        finally { sb.End(); }
    }

    private static void DrawHitbox()
    {
        float lw = W / 4f;
        for (int i = 0; i < 4; i++)
        {
            var c = LaneColors[i];
            float x = i * lw;
            if (_laneHeld[i]) FunkUI.Fill(x, 0, lw, H, new Color(c, 0.16f));
            float edge = _laneHeld[i] ? 0.75f : 0.22f;
            FunkUI.Fill(x + 2, 0, 6, H, new Color(c, edge));           // lane borders (Psych mobile hitbox look)
            FunkUI.Fill(x + lw - 8, 0, 6, H, new Color(c, edge));
            FunkUI.Fill(x + 2, H - 10, lw - 4, 8, new Color(c, edge));
        }
        // pause button: chunky FNF block with two bars
        var r = PauseRect;
        FunkUI.ShadowBlock(r.X + 6, r.Y + 6, r.Width - 12, r.Height - 12, FunkUI.Paper, 5f, 6f, 6f, 0.85f);
        FunkUI.Fill(r.X + 28, r.Y + 22, 11, r.Height - 44, new Color(FunkUI.Ink, 0.9f));
        FunkUI.Fill(r.X + r.Width - 39, r.Y + 22, 11, r.Height - 44, new Color(FunkUI.Ink, 0.9f));
    }

    private static void DrawPad()
    {
        for (int i = 0; i < 4; i++)
        {
            bool down = _btnHeld[i] && _receptors != null && NoteSrcPress[i] != Rectangle.Empty;
            var tex = down ? _receptors : _notes;
            var src = down ? NoteSrcPress[i] : NoteSrc[i];
            float s = BtnSize[i] * (down ? 1.25f : 1f) / 157f;   // confirm frames carry their glow around the arrow
            if (tex != null && src != Rectangle.Empty)
                FunkUI.Quad(tex, BtnPos[i].X, BtnPos[i].Y, src.Width * s * (down ? 1f : 1f), src.Height * s, 0f, new Color(Color.White, _btnHeld[i] ? 1f : 0.5f), src);
            else
                FunkUI.ShadowBlock(BtnPos[i].X - BtnSize[i] / 2, BtnPos[i].Y - BtnSize[i] / 2, BtnSize[i], BtnSize[i], LaneColors[i], 5f, 6f, 6f, 0.5f);
        }
        DrawFaceButton(4, FunkUI.Green, "A");
        DrawFaceButton(5, FunkUI.Red, "B");
    }

    private static void DrawFaceButton(int i, Color fill, string label)
    {
        float s = BtnSize[i] * (_btnHeld[i] ? 1.08f : 1f);
        float a = _btnHeld[i] ? 1f : 0.55f;
        FunkUI.ShadowBlock(BtnPos[i].X - s / 2, BtnPos[i].Y - s / 2, s, s, fill, 6f, 8f, 9f, a);
        float scale = s / 120f;
        FunkUI.Bold(label, BtnPos[i].X, BtnPos[i].Y - FunkUI.BoldHeight(scale) / 2f, scale, 0.5f, (float)_clock, a);
    }
}
#endif
