using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes.Editors;

/// <summary>
/// Immediate-mode widget kit for the merged Psych + V-Slice editors, drawn with the hand-made FunkUI look
/// (flat paper slabs, chunky black outlines, hard offset shadows, masking tape, VCR text, Funkin bold titles).
/// Input is latched in <see cref="Frame"/> (scene Update) and consumed by the widgets during Draw, so clicks are
/// never lost when MonoGame runs several updates per draw. Controller: D-pad up/down walks every widget in draw
/// order, A activates, left/right nudges number fields / choices. Text is typed with the keyboard (CapsLock or
/// Shift = upper case) - the scan is per key so FNF_DEVKEYS can type into fields too.
/// </summary>
public static class Ed
{
    // ------------------------------------------------------------------ latched input
    public static Vector2 M;
    public static bool Down, RDown, MDown;
    private static bool _click, _rclick, _release;
    public static bool Click => _click;
    public static bool RClick => _rclick;
    public static bool Release => _release;
    public static int Wheel;
    public static bool Shift, Ctrl;
    public static float Time;
    private static bool _caps;
    private static readonly List<char> _typed = new();
    private static bool _bksp, _enter, _tab;
    private static InputManager _in;

    // ------------------------------------------------------------------ focus / nav
    public static string Focus;           // id of the text field being edited
    private static string _focusBuf;
    public static int Nav;                 // controller-focused widget
    private static int _navCount, _navThis;
    private static bool _navA, _navL, _navR;
    public static bool PadMode;
    public static bool Typing => Focus != null;
    private static readonly List<Rectangle> _uiRects = new(), _uiRectsPrev = new();

    // ------------------------------------------------------------------ toast
    private static string _toast; private static float _toastT; private static Color _toastC;

    public static readonly Color Ink = FunkUI.Ink, Paper = FunkUI.Paper;
    public static readonly Color Yellow = FunkUI.Yellow, Pink = FunkUI.Pink, Cyan = FunkUI.Cyan, Purple = FunkUI.Purple,
        Green = FunkUI.Green, Red = FunkUI.Red, Orange = FunkUI.Orange, Blue = FunkUI.Blue;

    private static readonly Keys[] LetterKeys = BuildLetters();
    private static Keys[] BuildLetters() { var l = new List<Keys>(); for (var k = Keys.A; k <= Keys.Z; k++) l.Add(k); return l.ToArray(); }

    /// <summary>Call once per scene Update.</summary>
    public static void Frame(InputManager input, Vector2 mouse, float dt)
    {
        _in = input;
        Time += dt;
        M = mouse;
        bool down = input.MouseLeftHeld, rdown = input.MouseRightHeld;
        _click |= down && !Down;
        _release |= !down && Down;
        _rclick |= rdown && !RDown;
        Down = down; RDown = rdown; MDown = input.MouseMiddleHeld;
        Wheel += input.ScrollDelta;
        Shift = input.IsHeld(Keys.LeftShift) || input.IsHeld(Keys.RightShift);
        Ctrl = input.IsHeld(Keys.LeftControl) || input.IsHeld(Keys.RightControl);
        if (input.IsPressed(Keys.CapsLock)) _caps = !_caps;

        if (Focus != null)
        {
            bool upper = Shift ^ _caps;
            foreach (var k in LetterKeys) if (input.IsPressed(k)) _typed.Add(upper ? (char)('A' + (k - Keys.A)) : (char)('a' + (k - Keys.A)));
            for (int i = 0; i <= 9; i++)
            {
                if (input.IsPressed(Keys.D0 + i)) _typed.Add(Shift && i == 9 ? '(' : Shift && i == 0 ? ')' : (char)('0' + i));
                if (input.IsPressed(Keys.NumPad0 + i)) _typed.Add((char)('0' + i));
            }
            if (input.IsPressed(Keys.Space)) _typed.Add(' ');
            if (input.IsPressed(Keys.OemMinus) || input.IsPressed(Keys.Subtract)) _typed.Add(Shift || _caps ? '_' : '-');   // CapsLock+minus = '_' (lets FNF_DEVKEYS type it)
            if (input.IsPressed(Keys.OemPeriod) || input.IsPressed(Keys.Decimal)) _typed.Add('.');
            if (input.IsPressed(Keys.OemComma)) _typed.Add(',');
            if (input.IsPressed(Keys.OemQuestion) || input.IsPressed(Keys.Divide)) _typed.Add(Shift ? '?' : '/');
            if (input.IsPressed(Keys.OemPlus)) _typed.Add(Shift ? '+' : '=');
            if (input.IsPressed(Keys.OemSemicolon)) _typed.Add(Shift ? ':' : ';');
            if (input.IsPressed(Keys.OemQuotes)) _typed.Add(Shift ? '"' : '\'');
            if (input.IsPressed(Keys.D1) && Shift) { _typed.RemoveAt(_typed.Count - 1); _typed.Add('!'); }
            if (input.IsPressed(Keys.D3) && Shift) { _typed.RemoveAt(_typed.Count - 1); _typed.Add('#'); }
            if (input.IsPressed(Keys.Back)) _bksp = true;
            if (input.IsPressed(Keys.Enter)) _enter = true;
            if (input.IsPressed(Keys.Tab)) _tab = true;
        }
        // controller: D-pad edges with key-repeat (held 0.35 s, then every 0.07 s)
        PadUp = Rep(input, Buttons.DPadUp, 0, dt); PadDown = Rep(input, Buttons.DPadDown, 1, dt);
        PadLeft = Rep(input, Buttons.DPadLeft, 2, dt); PadRight = Rep(input, Buttons.DPadRight, 3, dt);
        LT = input.IsGamePadPressed(Buttons.LeftTrigger); RT = input.IsGamePadPressed(Buttons.RightTrigger);
        LTHeld = input.IsGamePadHeld(Buttons.LeftTrigger); RTHeld = input.IsGamePadHeld(Buttons.RightTrigger);
        if (PadUp || PadDown || PadLeft || PadRight || LT || RT) PadMode = true;
        foreach (var b in AnyPad) if (input.IsGamePadPressed(b)) PadMode = true;
        if (_osk && Focus != null) OskInput(input);
        else if (!PadWorld)
        {
            // controller navigation over the widgets (keyboard arrows stay free for the editors' own tools)
            if (PadDown) Nav++;
            if (PadUp) Nav--;
            _navA |= input.IsGamePadPressed(Buttons.A);
            _navL |= PadLeft;
            _navR |= PadRight;
        }
        if (_navCount > 0) Nav = ((Nav % _navCount) + _navCount) % _navCount;
        if (input.MouseMoved || down) PadMode = false;
        if (_toastT > 0) _toastT -= dt;
    }

    /// <summary>True when the pointer is over any widget/panel drawn last frame (world tools must ignore the click).</summary>
    public static bool OverUI
    {
        get { foreach (var r in _uiRectsPrev) if (r.Contains((int)M.X, (int)M.Y)) return true; return false; }
    }
    /// <summary>Click that landed on the world (not on a panel).</summary>
    public static bool WorldClick => _click && !OverUI;

    /// <summary>Call at the start of the scene Draw (before widgets).</summary>
    public static void BeginWidgets()
    {
        _navThis = 0;
        _uiRects.Clear();
    }

    /// <summary>Call at the end of the scene Draw: clears the latched edges.</summary>
    public static void EndWidgets()
    {
        _navCount = _navThis;
        _uiRectsPrev.Clear(); _uiRectsPrev.AddRange(_uiRects);
        if (_click && Focus != null && !_focusHitThisFrame && !(Osk && OverOsk())) CommitFocus();
        DrawOsk();
        if (Focus == null) _osk = false;
        _focusHitThisFrame = false;
        _click = _rclick = _release = false; Wheel = 0;
        _typed.Clear(); _bksp = _enter = _tab = false;
        _navA = _navL = _navR = false;
        DrawToast();
    }
    private static bool _focusHitThisFrame;
    private static bool OverOsk() { foreach (var r in _uiRects) if (r.Height > 200 && r.Contains((int)M.X, (int)M.Y)) return true; return false; }
    private static Action<string> _focusCommit;
    private static void CommitFocus() { var c = _focusCommit; var b = _focusBuf; Focus = null; _focusCommit = null; c?.Invoke(b); }

    public static void AddRect(float x, float y, float w, float h) => _uiRects.Add(new Rectangle((int)x - 6, (int)y - 6, (int)w + 12, (int)h + 12));
    public static bool Hover(float x, float y, float w, float h) => M.X >= x && M.X < x + w && M.Y >= y && M.Y < y + h;
    private static bool NavHere(out int idx) { idx = _navThis++; return PadMode && !PadWorld && idx == Nav; }

    // ------------------------------------------------------------------ controller extras
    /// <summary>Y toggles it: the pad drives the editor's world tools (D-pad/A/LB/RB/triggers) instead of walking the widgets.</summary>
    public static bool PadWorld;
    /// <summary>D-pad edges with key-repeat (only meaningful for world tools when <see cref="PadWorld"/>).</summary>
    public static bool PadUp, PadDown, PadLeft, PadRight;
    public static bool LT, RT, LTHeld, RTHeld;
    private static readonly float[] _repT = new float[4];
    private static readonly Buttons[] AnyPad = { Buttons.A, Buttons.B, Buttons.X, Buttons.Y, Buttons.LeftShoulder, Buttons.RightShoulder, Buttons.Start, Buttons.Back, Buttons.LeftStick, Buttons.RightStick };
    private static bool Rep(InputManager input, Buttons b, int i, float dt)
    {
        if (input.IsGamePadPressed(b)) { _repT[i] = 0.35f; return true; }
        if (!input.IsGamePadHeld(b)) { _repT[i] = 0f; return false; }
        _repT[i] -= dt;
        if (_repT[i] <= 0f) { _repT[i] = 0.07f; return true; }
        return false;
    }
    /// <summary>World-tool D-pad as a vector (x: left/right, y: up = -1).</summary>
    public static Vector2 PadDir => new((PadRight ? 1 : 0) - (PadLeft ? 1 : 0), (PadDown ? 1 : 0) - (PadUp ? 1 : 0));
    /// <summary>Pad prompts shown on the help tape while the controller is in use (set by EditorBase before DrawUI).</summary>
    public static List<(Buttons b, string text)> PadHints;
    public static bool ShowPadHints;
    private static AssetManager _assets;
    public static void SetAssets(AssetManager a) => _assets = a;

    // ------------------------------------------------------------------ on-screen keyboard (text fields picked with the pad)
    private static bool _osk, _oskUpper;
    private static int _oskR, _oskC;
    public static bool Osk => _osk && Focus != null;
    private static readonly string[] OskRows = { "1234567890-", "qwertyuiop_", "asdfghjkl.,", "zxcvbnm/'#!" };
    private static readonly string[] OskActs = { "CAPS", "SPACE", "DEL", "DONE" };
    private static void OskInput(InputManager input)
    {
        int rows = OskRows.Length + 1;
        if (PadUp) _oskR = (_oskR - 1 + rows) % rows;
        if (PadDown) _oskR = (_oskR + 1) % rows;
        int cols = _oskR < OskRows.Length ? OskRows[_oskR].Length : OskActs.Length;
        if (_oskC >= cols) _oskC = cols - 1;
        if (PadLeft) _oskC = (_oskC - 1 + cols) % cols;
        if (PadRight) _oskC = (_oskC + 1) % cols;
        if (input.IsGamePadPressed(Buttons.A)) OskPress(_oskR, _oskC);
        if (input.IsGamePadPressed(Buttons.X)) _bksp = true;
        if (input.IsGamePadPressed(Buttons.Y)) _oskUpper = !_oskUpper;
        if (input.IsGamePadPressed(Buttons.LeftShoulder)) _typed.Add(' ');
        if (input.IsGamePadPressed(Buttons.Start) || input.IsGamePadPressed(Buttons.B)) { _enter = true; _osk = false; }
    }
    private static void OskPress(int r, int c)
    {
        if (r < OskRows.Length)
        {
            char ch = OskRows[r][c];
            if (_oskUpper && char.IsLetter(ch)) ch = char.ToUpperInvariant(ch);
            _typed.Add(ch);
            return;
        }
        switch (c) { case 0: _oskUpper = !_oskUpper; break; case 1: _typed.Add(' '); break; case 2: _bksp = true; break; default: _enter = true; _osk = false; break; }
    }
    /// <summary>Paper keyboard at the bottom of the screen: VCR keycaps, pink cursor, mouse works too.</summary>
    private static void DrawOsk()
    {
        if (!Osk) return;
        const float kw = 46f, kh = 38f, gap = 6f;
        float w = 11 * (kw + gap) + 30f, h = 5 * (kh + gap) + 54f, x = 640f - w / 2f, y = 690f - h;
        FunkUI.Push(640f, y + h / 2f, FunkUI.Rad(-0.8f));
        FunkUI.ShadowBlock(x, y, w, h, Paper, 4f, 8f, 9f);
        FunkUI.Text("TYPE: " + (_focusBuf ?? "") + (((int)(Time * 2.5f)) % 2 == 0 ? "|" : ""), x + 16f, y + 12f, 17f, Ink);
        FunkUI.Tape(x + w - 30f, y + 2f, 70f, FunkUI.Rad(30f), Pink, 0.85f);
        for (int r = 0; r <= OskRows.Length; r++)
        {
            bool act = r == OskRows.Length;
            int cols = act ? OskActs.Length : OskRows[r].Length;
            float cw = act ? (11 * (kw + gap) - gap - (cols - 1) * gap) / cols : kw;
            for (int c = 0; c < cols; c++)
            {
                float kx = x + 15f + c * (cw + gap) + (act ? 0f : r * 6f), ky = y + 42f + r * (kh + gap);
                bool sel = PadMode && r == _oskR && c == _oskC, hov = Hover(kx, ky, cw, kh);
                if (hov && _click) { _oskR = r; _oskC = c; OskPress(r, c); }
                string t = act ? (c == 0 && _oskUpper ? "caps" : OskActs[c]) : (_oskUpper ? OskRows[r][c].ToString().ToUpperInvariant() : OskRows[r][c].ToString());
                var fill = sel ? Yellow : act && c == 3 ? Green : act && c == 2 ? Red : Color.White;
                FunkUI.Fill(kx + 3f, ky + 4f, cw, kh, Color.Black);
                FunkUI.Block(kx, ky - (sel ? 3f : 0f), cw, kh, fill, 3f);
                FunkUI.Text(t, kx + cw / 2f, ky + 11f - (sel ? 3f : 0f), 16f, Ink, 0.5f);
            }
        }
        FunkUI.Pop();
        AddRect(x, y, w, h);
    }

    /// <summary>Glyph + label pairs on the ink help tape (controller prompts).</summary>
    private static void DrawPadHints(float y)
    {
        if (PadHints == null || PadHints.Count == 0 || _assets == null) return;
        const float gs = 22f;
        float total = 0f;
        foreach (var h in PadHints) total += gs + 4f + FunkUI.MeasureVcr(h.text, 13f) + 16f;
        float w = Math.Min(1260f, total + 20f), x = 640f - w / 2f + 10f;
        FunkUI.Push(640f, y + 12f, FunkUI.Rad(0.4f));
        FunkUI.Fill(640f - w / 2f, y, w, 26f, Ink);
        foreach (var h in PadHints)
        {
            var tex = _assets.LoadTexture("images/ui/prompts/" + ButtonPrompts.PadGlyph(h.b) + ".png");
            if (tex != null && tex != _assets.Pixel) FunkUI.Quad(tex, x + gs / 2f, y + 13f, gs, gs * tex.Height / Math.Max(1f, tex.Width), 0f, Color.White);
            x += gs + 4f;
            x += FunkUI.Text(h.text, x, y + 7f, 13f, Color.White) + 16f;
        }
        FunkUI.Pop();
        AddRect(640f - w / 2f, y, w, 26f);
    }

    public static void Toast(string msg, Color? c = null) { _toast = msg; _toastT = 3.2f; _toastC = c ?? Yellow; Console.WriteLine("[EDITOR] " + msg); }

    private static void DrawToast()
    {
        if (_toastT <= 0 || string.IsNullOrEmpty(_toast)) return;
        float a = FunkUI.Clamp01(_toastT / 0.4f), pop = FunkUI.EaseOutBack(FunkUI.Clamp01((3.2f - _toastT) / 0.25f));
        float w = FunkUI.MeasureVcr(_toast, 18f) + 40f, x = 640f - w / 2f, y = 640f;
        FunkUI.Push(640f, y + 18f, FunkUI.Rad(-1.5f), pop, pop);
        FunkUI.ShadowBlock(x, y, w, 36f, FunkUI.A(_toastC, a), 4f, 6f, 6f, a);
        FunkUI.Text(_toast, 640f, y + 8f, 18f, FunkUI.A(Ink, a), 0.5f);
        FunkUI.Pop();
    }

    // ------------------------------------------------------------------ drawing helpers
    public static bool Dark(Color c) => c.R * 0.299f + c.G * 0.587f + c.B * 0.114f < 120f;

    /// <summary>Paper panel with a hard shadow, an ink title tab and a strip of tape.</summary>
    public static void Panel(float x, float y, float w, float h, Color fill, string title = null, Color? tab = null, int seed = 0)
    {
        AddRect(x, y, w, h);
        FunkUI.ShadowBlock(x, y, w, h, fill, 4f, 7f, 8f);
        if (!string.IsNullOrEmpty(title))
        {
            float tw = FunkUI.MeasureVcr(title, 17f) + 22f;
            float tilt = (FunkUI.Hash(seed + title.Length) - 0.5f) * 3f;
            FunkUI.Push(x + 14f + tw / 2f, y - 4f, FunkUI.Rad(-1.5f + tilt));
            FunkUI.Block(x + 14f, y - 17f, tw, 26f, tab ?? Ink, 3f);
            FunkUI.Text(title, x + 14f + tw / 2f, y - 13f, 17f, Dark(tab ?? Ink) ? Color.White : Ink, 0.5f);
            FunkUI.Pop();
        }
        FunkUI.Tape(x + w - 26f, y + 2f, 62f, FunkUI.Rad(28f + (seed % 3) * 4f), Yellow, 0.8f);
    }

    public static bool Button(string label, float x, float y, float w, float h, Color fill, bool on = false, string tip = null)
    {
        AddRect(x, y, w, h);
        bool nav = NavHere(out _);
        bool hover = Hover(x, y, w, h);
        bool hit = (hover && _click && Focus == null) || (nav && _navA);
        if (hover && _click && Focus != null) { CommitFocus(); hit = true; }
        float lift = hover && Down ? 3f : hover || nav ? -2f : 0f;
        var f = on ? Ink : fill;
        FunkUI.Fill(x + 5f, y + 6f, w, h, Color.Black);
        FunkUI.Block(x, y + lift, w, h, f, 3f);
        float ts = Math.Min(16f, h * 0.55f);
        string l = label;
        while (l.Length > 2 && FunkUI.MeasureVcr(l, ts) > w - 8f) l = l.Substring(0, l.Length - 1);
        FunkUI.Text(l, x + w / 2f, y + lift + (h - ts) / 2f, ts, Dark(f) ? Color.White : Ink, 0.5f);
        if (nav) NavMark(x, y + h / 2f);
        if (hover && tip != null) TipText = tip;
        return hit;
    }
    public static string TipText;

    private static void NavMark(float x, float cy)
    {
        float b = MathF.Sin(Time * 10f) * 3f;
        FunkUI.Sticker("@arrow", x - 18f + b, cy, 22f, 0f, 1f, 1f, 1f, Pink);
        FunkUI.Fill(x - 26f + b, cy - 2f, 10f, 4f, Pink);
    }

    public static bool Toggle(string label, ref bool v, float x, float y, float w = 160f)
    {
        AddRect(x, y, w, 24f);
        bool nav = NavHere(out _);
        bool hover = Hover(x, y, w, 24f);
        bool hit = (hover && _click) || (nav && _navA);
        if (hit) v = !v;
        FunkUI.Block(x + 2f, y + 2f, 20f, 20f, Color.White, 3f);
        if (v)
        {
            FunkUI.Line(x + 5f, y + 13f, x + 10f, y + 19f, 4f, Pink);
            FunkUI.Line(x + 10f, y + 19f, x + 22f, y + 1f, 4f, Pink);
        }
        FunkUI.Text(label, x + 30f, y + 4f, 15f, Ink);
        if (nav) NavMark(x, y + 12f);
        return hit;
    }

    /// <summary>Single-line text field. Returns true on the frame the edit was committed (Enter / Tab / click away).</summary>
    public static bool Field(string id, string label, ref string value, float x, float y, float w, float labelW = 104f, bool live = true)
    {
        AddRect(x, y, w, 26f);
        bool nav = NavHere(out _);
        float bx = x + labelW, bw = w - labelW;
        if (labelW > 0) { float ls = 14f; while (ls > 9f && FunkUI.MeasureVcr(label, ls) > labelW - 4f) ls -= 1f; FunkUI.Text(label, x, y + 6f, ls, Ink); }
        bool hover = Hover(bx, y, bw, 26f);
        bool committed = false;
        string result = value ?? "";
        if (Focus == id)
        {
            _focusHitThisFrame |= hover && _click;
            foreach (var ch in _typed) _focusBuf += ch;
            if (_bksp && _focusBuf.Length > 0) _focusBuf = _focusBuf.Substring(0, _focusBuf.Length - 1);
            if (_enter || _tab) { result = _focusBuf; Focus = null; _focusCommit = null; committed = true; Console.WriteLine($"[EDITOR] field {id} = '{result}'"); }
        }
        else if ((hover && _click) || (nav && _navA))
        {
            if (Focus != null) CommitFocus();
            Focus = id; _focusBuf = value ?? ""; _focusHitThisFrame = true;
            _osk = nav && _navA; _oskR = 1; _oskC = 0;   // picked with the controller -> on-screen keyboard
            string capturedId = id;
            _focusCommit = s => { _pendingCommits[capturedId] = s; };
        }
        if (_pendingCommits.TryGetValue(id, out var pc)) { result = pc; _pendingCommits.Remove(id); committed = true; }
        bool editing = Focus == id;
        if (editing && live) result = _focusBuf;   // text fields follow the typing (a button pressed right after sees the text)
        FunkUI.Block(bx, y, bw, 26f, editing ? Color.White : new Color(255, 255, 255, 215), 3f);
        string shown = editing ? _focusBuf : result;
        float ts = 15f;
        string vis = shown ?? "";
        while (vis.Length > 0 && FunkUI.MeasureVcr(vis, ts) > bw - 14f) vis = editing ? vis.Substring(1) : vis.Substring(0, vis.Length - 1);
        float tw = FunkUI.Text(vis, bx + 6f, y + 6f, ts, Ink);
        if (editing && ((int)(Time * 2.5f)) % 2 == 0) FunkUI.Fill(bx + 7f + tw, y + 4f, 3f, 18f, Pink);
        if (nav) NavMark(x, y + 13f);
        value = result;
        return committed;
    }
    private static readonly Dictionary<string, string> _pendingCommits = new();

    /// <summary>Number field with -/+ nubs (mouse wheel over it and controller left/right also step).</summary>
    public static bool Num(string id, string label, ref float value, float step, float x, float y, float w, float labelW = 104f, string fmt = "0.###")
    {
        bool changed = false;
        float fw = w - 56f;
        int navIdx = _navThis;
        string s = value.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
        if (Field(id, label, ref s, x, y, fw, labelW, false))
        {
            if (float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) { value = v; changed = true; }
        }
        bool nav = PadMode && Nav == navIdx;
        float mul = Shift ? 10f : 1f;
        if (nav && _navL) { value -= step * mul; changed = true; }
        if (nav && _navR) { value += step * mul; changed = true; }
        if (Hover(x + labelW, y, fw - labelW, 26f) && Wheel != 0 && Focus != id) { value += Math.Sign(Wheel) * step * mul; changed = true; Wheel = 0; }
        if (SmallBtn("-", x + fw + 4f, y)) { value -= step * mul; changed = true; }
        if (SmallBtn("+", x + fw + 30f, y)) { value += step * mul; changed = true; }
        if (changed) value = MathF.Round(value * 1000f) / 1000f;
        return changed;
    }

    private static bool SmallBtn(string t, float x, float y)
    {
        AddRect(x, y, 22f, 26f);
        bool hover = Hover(x, y, 22f, 26f);
        FunkUI.Block(x, y + (hover ? -1f : 0f), 22f, 26f, hover ? Yellow : Color.White, 3f);
        FunkUI.Text(t, x + 11f, y + 5f, 16f, Ink, 0.5f);
        return hover && _click;
    }

    /// <summary>"&lt; value &gt;" picker. Returns the new index.</summary>
    public static int Choice(string label, IList<string> opts, int idx, float x, float y, float w, float labelW = 90f)
    {
        AddRect(x, y, w, 26f);
        bool nav = NavHere(out _);
        if (opts == null || opts.Count == 0) return 0;
        idx = Math.Clamp(idx, 0, opts.Count - 1);
        if (labelW > 0) FunkUI.Text(label, x, y + 5f, 15f, Ink);
        float bx = x + labelW, bw = w - labelW;
        if (SmallBtn("<", bx, y) || (nav && _navL)) idx = (idx - 1 + opts.Count) % opts.Count;
        if (SmallBtn(">", bx + bw - 22f, y) || (nav && (_navR || _navA))) idx = (idx + 1) % opts.Count;
        FunkUI.Block(bx + 26f, y, bw - 52f, 26f, Ink, 3f);
        string t = opts[idx] ?? "";
        while (t.Length > 1 && FunkUI.MeasureVcr(t, 15f) > bw - 60f) t = t.Substring(0, t.Length - 1);
        FunkUI.Text(t, bx + bw / 2f, y + 6f, 15f, Color.White, 0.5f);
        if (nav) NavMark(x, y + 13f);
        return idx;
    }

    /// <summary>Scrolling list of paper rows. Returns the clicked/selected index (or the old one).</summary>
    public static int List(string id, IList<string> items, int sel, float x, float y, float w, float h, ref float scroll, Color? accent = null, float rowH = 24f)
    {
        AddRect(x, y, w, h);
        FunkUI.Block(x, y, w, h, new Color(255, 255, 255, 230), 3f);
        int n = items?.Count ?? 0;
        float maxScroll = Math.Max(0f, n * rowH - h);
        if (Hover(x, y, w, h) && Wheel != 0) { scroll -= Math.Sign(Wheel) * rowH * 3f; Wheel = 0; }
        bool nav = NavHere(out _);
        if (nav && (_navL || _navR)) { sel = Math.Clamp(sel + (_navR ? 1 : -1), 0, Math.Max(0, n - 1)); scroll = Math.Clamp(sel * rowH - h / 2f, 0, maxScroll); }
        scroll = Math.Clamp(scroll, 0f, maxScroll);
        int first = (int)(scroll / rowH);
        for (int i = first; i < n; i++)
        {
            float ry = y + i * rowH - scroll;
            if (ry > y + h - rowH + 1f) break;
            bool hov = Hover(x, ry, w, rowH);
            if (i == sel) FunkUI.Fill(x + 2f, ry, w - 4f, rowH, accent ?? Pink);
            else if (hov) FunkUI.Fill(x + 2f, ry, w - 4f, rowH, new Color(Yellow, 0.6f));
            string t = items[i] ?? "";
            while (t.Length > 1 && FunkUI.MeasureVcr(t, 14f) > w - 12f) t = t.Substring(0, t.Length - 1);
            FunkUI.Text(t, x + 6f, ry + (rowH - 14f) / 2f, 14f, i == sel && Dark(accent ?? Pink) ? Color.White : Ink);
            if (hov && _click) sel = i;
        }
        if (maxScroll > 0)
        {
            float bh = Math.Max(20f, h * h / (n * rowH));
            FunkUI.Fill(x + w - 6f, y + (h - bh) * scroll / maxScroll, 4f, bh, Ink);
        }
        if (nav) NavMark(x, y + 12f);
        return sel;
    }

    public static void Label(string t, float x, float y, float size = 15f, Color? c = null, float align = 0f) => FunkUI.Text(t, x, y, size, c ?? Ink, align);

    /// <summary>Bottom help tape (key hints).</summary>
    public static void HelpTape(string text)
    {
        if (ShowPadHints && PadHints != null && PadHints.Count > 0) { DrawPadHints(694f); return; }
        float ts = 14f; while (ts > 9f && FunkUI.MeasureVcr(text, ts) > 1220f) ts -= 0.5f;
        float w = Math.Min(1250f, FunkUI.MeasureVcr(text, ts) + 26f);
        FunkUI.Push(640f, 706f, FunkUI.Rad(-0.4f));
        FunkUI.Fill(640f - w / 2f, 694f, w, 24f, Ink);
        FunkUI.Text(text, 640f, 699f + (14f - ts) / 2f, ts, Color.White, 0.5f);
        FunkUI.Pop();
        AddRect(640f - w / 2f, 694f, w, 24f);
    }

    /// <summary>Tilted bold title in the top-left corner with a stamp-like colour slab behind it.</summary>
    public static void Title(string title, Color slab, float x = 18f, float y = 10f, float scale = 0.5f)
    {
        float tw = FunkUI.BoldWidth(title, scale), th = FunkUI.BoldHeight(scale);
        FunkUI.Push(x + tw / 2f, y + th / 2f, FunkUI.Rad(-2f));
        FunkUI.ShadowBlock(x - 8f, y - 2f, tw + 16f, th + 4f, slab, 4f, 6f, 6f);
        FunkUI.Bold(title, x, y, scale, 0f, Time);
        FunkUI.Pop();
        AddRect(x - 8f, y - 2f, tw + 16f, th + 4f);
    }

    // ------------------------------------------------------------------ keyboard helpers for the editors
    public static bool Key(Keys k) => !Typing && _in != null && _in.IsPressed(k);
    public static bool KeyHeld(Keys k) => !Typing && _in != null && _in.IsHeld(k);
    public static bool Pad(Buttons b) => _in != null && _in.IsGamePadPressed(b);
    public static bool PadHeld(Buttons b) => _in != null && _in.IsGamePadHeld(b);

    /// <summary>Formats a float for JSON-friendly display.</summary>
    public static string F(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    public static float ParseF(string s, float def = 0f) => float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : def;
}
