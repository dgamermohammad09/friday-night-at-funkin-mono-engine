using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.PsychEngine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Options in the in-house Funkin theme (FunkTheme, same look as the Psych Online room / song select / mod browser):
/// black top bar with the bold heading + slanted yellow band, page tabs (LB/RB or Q/E switch pages), the setting list on
/// the left as Freeplay-style capsules (yellow focus + pop, FNF checkbox art, capsule slider bars with Freeplay digital
/// numbers, choice values with &lt; &gt; arrows, keycaps / Xbox glyph chips) and a detail panel on the right (big value,
/// description, previews, the buttons that act on the row). Everything is drawn at the native back-buffer resolution.
/// </summary>
public partial class OptionsScene
{
    private const float ThListX = 40f, ThListW = 770f, ThPanelX = 852f, ThPanelW = 388f, ThBottom = 644f;
    private readonly List<(float x, float y, float w, float h, int id)> _tabRects = new();
    private readonly Dictionary<string, List<PrefItem>> _catSummary = new();

    // ------------------------------------------------------------------ pages (tabs)
    /// <summary>Pages reachable with LB/RB: ids 0-3 = PREFERENCES/GAMEPLAY/VISUALS/GRAPHICS, 4 = PSYCH ONLINE,
    /// 10 = mod page, 40/41 = CONTROLS keyboard/controller, 8 = SAVE DATA.</summary>
    private List<(string label, int id)> PageTabs()
    {
        var l = new List<(string, int)> { ("PREFERENCES", 0), ("GAMEPLAY", 1), ("VISUALS", 2), ("GRAPHICS", 3), ("KEYBOARD", 40), ("CONTROLLER", 41), ("ONLINE", 4) };
        if (QtOptionsPresent) l.Add(("REWIRED", 10));
        l.Add(("SAVE DATA", 8));
        return l;
    }

    private int CurrentPageId() => _inControls ? 40 + _controlTab : _inSaveData ? 8 : _inSubMenu ? _prefPage : -1;

    private void OpenPageId(int id)
    {
        string cat = id switch { 0 => "PREFERENCES", 1 => "GAMEPLAY", 2 => "VISUALS AND UI", 3 => "GRAPHICS", 40 or 41 => "CONTROLS", 4 => "PSYCH ONLINE", 10 => QtCatName, _ => "SAVE DATA" };
        int ci = System.Array.FindIndex(Cats, c => c.name == cat);
        if (ci < 0) return;
        _editing = false; _waitingForKey = _waitingForButton = false;
        _inSubMenu = _inControls = _inSaveData = false;
        _categoryIndex = ci;
        OpenCat(ci);
        if (id >= 40) { _controlTab = id - 40; _controlIndex = 0; _gpBindIndex = 0; _gpBindCol = 0; _kbCol = 0; _controlOnTab = false; }
    }

    /// <summary>LB/RB (Q/E) and clicks on the page tabs. Returns true when the page changed this frame.</summary>
    private bool ThemedPageInput()
    {
        if (!(_inSubMenu || _inControls || _inSaveData) || _editing || _waitingForKey || _waitingForButton) return false;
        int dir = 0;
        if (Input.IsPressed(Keys.Q) || Input.IsGamePadPressed(Buttons.LeftShoulder)) dir = -1;
        else if (Input.IsPressed(Keys.E) || Input.IsGamePadPressed(Buttons.RightShoulder)) dir = 1;
        var tabs = PageTabs();
        if (dir != 0)
        {
            int i = tabs.FindIndex(t => t.id == CurrentPageId());
            OpenPageId(tabs[((i < 0 ? 0 : i) + dir + tabs.Count) % tabs.Count].id);
            Audio.PlaySound("scrollMenu");
            return true;
        }
        if (Input.MouseLeftPressed && !DevNoMouseO)
        {
            var mp = MouseVirtual;
            foreach (var r in _tabRects)
                if (mp.X >= r.x && mp.X <= r.x + r.w && mp.Y >= r.y && mp.Y <= r.y + r.h && r.id != CurrentPageId())
                { OpenPageId(r.id); Audio.PlaySound("scrollMenu"); return true; }
        }
        return false;
    }

    private static (string icon, Color col) CatStyle(string name) => name switch
    {
        "PREFERENCES" => ("bf", FunkTheme.Yellow), "GAMEPLAY" => ("dad", FunkTheme.Green), "VISUALS AND UI" => ("gf", FunkTheme.Pink),
        "GRAPHICS" => ("spooky", FunkTheme.Orange), "CONTROLS" => ("pico", FunkTheme.Cyan), "LAG ADJUSTMENT" => ("mom", FunkTheme.Purple),
        "PSYCH ONLINE" => ("darnell", FunkTheme.Cyan), "MODS" => ("monster", FunkTheme.Green), "SAVE DATA" => ("tankman", FunkTheme.Red),
        QtCatName => ("senpai", FunkTheme.Pink), _ => (null, FunkTheme.Text2),
    };

    private Color ThPageColor => _inControls ? FunkTheme.Cyan : _inSaveData ? FunkTheme.Red : _inSubMenu ? _prefPage switch
    {
        1 => FunkTheme.Green, 2 => FunkTheme.Pink, 10 => FunkTheme.Pink, 3 => FunkTheme.Orange, 4 => FunkTheme.Cyan, _ => FunkTheme.Yellow,
    } : FunkTheme.Purple;

    // ------------------------------------------------------------------ drawing
    private void DrawFunk(SpriteBatch sb)
    {
        float dt = 1f / 60f;
        if (_toastT > 0) _toastT -= dt;
        List<FunkMenu.Row> rows; string title, tag = null; int sel; object key;
        if (_inControls)
        {
            rows = ControlRows(out sel); title = "CONTROLS"; key = "ctl" + _controlTab;
            tag = _controlTab == 0 ? "KEYBOARD" : "CONTROLLER";
        }
        else if (_inSaveData) { rows = SaveRows(); sel = _prefIndex; title = "SAVE DATA"; key = "save"; }
        else if (_inSubMenu)
        {
            rows = PrefRows(); sel = _prefIndex; title = PrefPageTitle; key = "pref" + _prefPage;
            tag = _prefPage == 0 ? "ORIGINAL GAME" : _prefPage == 4 ? "PSYCH ONLINE" : _prefPage == 10 ? "QT: REWIRED MOD" : "PSYCH ENGINE";
        }
        else { rows = CatRows(); sel = _categoryIndex; title = "OPTIONS"; key = "cat"; }
        bool paged = _inControls || _inSaveData || _inSubMenu;
        _fm.Sel = sel;
        _fm.CheckboxArt ??= DrawCheckboxArt;
        _fm.RowW = ThListW; _fm.CenterX = ThListX + ThListW / 2f;
        _fm.ListTop = paged ? 146f : 98f; _fm.ListBottom = ThBottom; _fm.RowH = paged ? 50f : 46f; _fm.RowGap = 8f;
        _fm.Update(key, rows, ThPageColor, dt);

        FunkUI.Begin(sb);
        _fm.DrawBackground(null);
        _fm.DrawTitle(title, tag, FunkTheme.Text2);
        _tabRects.Clear();
        if (paged) DrawPageTabs();
        _fm.DrawRows(rows, FunkTheme.Yellow);
        DrawSidePanel(rows, sel, paged);
        if (_inControls && (_waitingForKey || _waitingForButton)) DrawRebindPrompt(rows, sel);
        if (_editing) DrawThemedEditor();
        _fm.DrawToast(_toast, _toastT > 0 ? 3f + _toastT : 0f);
        _fm.DrawPrompts(Input, _editing ? EditorPrompts() : ThemedPrompts());
        sb.End();
    }

    private string PrefPageTitle => _prefPage switch { 1 => "GAMEPLAY", 2 => "VISUALS AND UI", 3 => "GRAPHICS", 4 => "PSYCH ONLINE", 10 => QtCatName, _ => "PREFERENCES" };

    private List<ButtonPrompts.Prompt> EditorPrompts() => new()
    {
        ButtonPrompts.UpDown("LETTER"), new ButtonPrompts.Prompt(Buttons.DPadRight, "TYPE", "ADD"),
        new ButtonPrompts.Prompt(Buttons.X, "BKSP", "DELETE"), ButtonPrompts.Confirm("OK"), ButtonPrompts.Back("CANCEL"),
    };

    private List<ButtonPrompts.Prompt> ThemedPrompts()
    {
        var l = new List<ButtonPrompts.Prompt>();
        bool paged = _inControls || _inSaveData || _inSubMenu;
        if (_inControls)
        {
            if (_waitingForKey || _waitingForButton) { l.Add(ButtonPrompts.Back("CANCEL")); return l; }
            l.Add(ButtonPrompts.UpDown("MOVE"));
            bool cells = _controlTab == 0 ? _controlIndex < 8 : _gpBindIndex < 4;
            if (cells) l.Add(ButtonPrompts.LeftRight("SLOT"));
            l.Add(ButtonPrompts.Confirm(_controlTab == 0 ? (_controlIndex < 8 ? "REBIND" : "SELECT") : (_gpBindIndex < 8 ? "REBIND" : "SELECT")));
        }
        else if (_inSubMenu && _prefItems != null && _prefIndex < _prefItems.Count)
        {
            l.Add(ButtonPrompts.UpDown("MOVE"));
            switch (_prefItems[_prefIndex].Kind)
            {
                case PrefKind.Toggle: l.Add(ButtonPrompts.Confirm("TOGGLE")); break;
                case PrefKind.Percent: case PrefKind.Number: l.Add(ButtonPrompts.LeftRight("CHANGE")); break;
                case PrefKind.Choice: l.Add(ButtonPrompts.LeftRight("CHANGE")); break;
                case PrefKind.Text: l.Add(ButtonPrompts.Confirm("EDIT")); break;
                case PrefKind.Action: l.Add(ButtonPrompts.Confirm("OPEN")); break;
                default: l.Add(ButtonPrompts.Confirm("SELECT")); break;
            }
        }
        else { l.Add(ButtonPrompts.UpDown("MOVE")); l.Add(ButtonPrompts.Confirm(_inSaveData ? "SELECT" : "OPEN")); }
        if (paged) { l.Add(new ButtonPrompts.Prompt(Buttons.LeftShoulder, "Q", "")); l.Add(new ButtonPrompts.Prompt(Buttons.RightShoulder, "E", "PAGE")); }
        l.Add(ButtonPrompts.Back("BACK"));
        return l;
    }

    /// <summary>Page tabs under the top bar: slanted outlined bands, the open page yellow; LB/RB glyphs at the ends.</summary>
    private void DrawPageTabs()
    {
        bool pd = ButtonPrompts.UseController(Input);
        var tabs = PageTabs();
        int cur = CurrentPageId();
        float ts = 16f, gap = 8f, gs = 30f, y = 88f, h = 36f;
        float total = 0f;
        foreach (var t in tabs) total += FunkTheme.TextW(t.label, ts) + 34f + gap;
        float lgw = FunkUI.MeasureGlyph(pd, "Q", gs), rgw = FunkUI.MeasureGlyph(pd, "E", gs);
        float avail = FunkUI.W - 2 * FunkTheme.M - lgw - rgw - 28f;
        if (total > avail) { ts *= avail / total; total = avail; }
        float slide = FunkUI.EaseOutCubic(FunkUI.Clamp01(_fm.PageTime / 0.2f));
        float x = FunkTheme.M;
        x += FunkUI.Glyph(pd, Buttons.LeftShoulder, "Q", x, y + (h - gs) / 2f, gs) + 14f;
        foreach (var t in tabs)
        {
            bool on = t.id == cur;
            float w = FunkTheme.TextW(t.label, ts) + 34f, ty = y + (on ? -2f * slide : 0f);
            FunkTheme.BandOutlined(x, ty, w, h, 10f, on ? FunkTheme.Yellow : FunkTheme.Navy, 3f, on ? 5f : 0f);
            FunkTheme.Txt(t.label, x + w / 2f + 2f, ty + h / 2f - ts * 0.5f, ts, on ? FunkTheme.Ink : FunkTheme.Text2, 0.5f);
            _tabRects.Add((x, ty, w, h, t.id));
            x += w + gap;
        }
        FunkUI.Glyph(pd, Buttons.RightShoulder, "E", x + 6f, y + (h - gs) / 2f, gs);
    }

    /// <summary>Real V-Slice checkbox (checkboxThingie): box top-left (bx, by), <paramref name="size"/> = box width.
    /// Checked = "Check Box selecting animation" (24 fps, replayed on toggle) at the original offset (17, 70) at 0.7 scale.</summary>
    private void DrawCheckboxArt(float bx, float by, float size, bool on, float age, float alpha)
    {
        var frames = _checkboxSheet?.GetAnimation(on ? "Check Box selecting animation" : "Check Box unselected");
        if (frames == null || frames.Count == 0) { FunkTheme.Switch(bx - 30f, by, on); return; }
        var f = frames[on ? Math.Min(frames.Count - 1, (int)(Math.Max(0f, age) * 24f)) : 0];
        float k = size / 102f;
        float ox = on ? 17f / 0.7f * k : 0f, oy = on ? 70f / 0.7f * k : 0f;
        var tex = FunkUI.Tex("menus/options/checkboxThingie.png") ?? f.Texture ?? _checkboxSheet.Texture;
        FunkUI.Batch.Draw(tex, new Vector2(bx - ox + f.Offset.X * k, by - oy + f.Offset.Y * k), f.SourceRect, new Color(Color.White, alpha), 0f, Vector2.Zero, k, SpriteEffects.None, 0f);
    }

    // ------------------------------------------------------------------ right panel
    private void DrawSidePanel(List<FunkMenu.Row> rows, int sel, bool paged)
    {
        float intro = FunkUI.EaseOutCubic(FunkUI.Clamp01(_fm.PageTime / 0.3f));
        float x = ThPanelX + (1f - intro) * 80f, y = _fm.ListTop, w = ThPanelW, h = ThBottom - y, a = intro;
        FunkTheme.Panel(x, y, w, h, FunkTheme.Navy, a);
        var row = sel >= 0 && sel < rows.Count ? rows[sel] : null;
        if (!paged) DrawCategoryPanel(x, y, w, h, a);
        else if (_inControls) DrawControlPanel(row, x, y, w, h, a);
        else if (_inSaveData) DrawSavePanel(row, x, y, w, h, a);
        else DrawSettingPanel(x, y, w, h, a);
    }

    private void PanelChips(IList<ButtonPrompts.Prompt> items, float x, float y, float a)
    {
        bool pad = ButtonPrompts.UseController(Input);
        foreach (var it in items)
        {
            x += FunkUI.Glyph(pad, it.Pad, it.Key, x, y, 28f, a) + 8f;
            x += FunkTheme.Txt(it.Text, x, y + 6f, 16f, FunkTheme.A(FunkTheme.Text, a)) + 22f;
        }
    }

    private string PrefValueText(PrefItem it)
    {
        try
        {
            switch (it.Kind)
            {
                case PrefKind.Toggle: return it.GetB() ? "ON" : "OFF";
                case PrefKind.Percent: return (int)Math.Round(it.GetF() * 100) + "%";
                case PrefKind.Number: return it.Fmt != null ? it.Fmt(it.GetF()) : it.GetF().ToString();
                case PrefKind.Back: return null;
                default: return (it.GetS?.Invoke() ?? "").ToUpperInvariant();
            }
        }
        catch { return ""; }
    }

    private List<PrefItem> CatItems(string name)
    {
        if (_catSummary.TryGetValue(name, out var l)) return l;
        int bi = System.Array.FindIndex(CatsBase, c => c.name == name);
        l = name == QtCatName ? BuildQtOptionsPage() : bi == 0 ? (_vsPrefs ?? (_inSubMenu ? null : _prefItems)) : bi >= 1 && bi <= 3 ? BuildPsychPage(bi) : bi == 6 ? BuildPsychPage(4) : null;
        if (l != null) l = l.Where(i => i.Kind != PrefKind.Back && !i.Name.StartsWith("Note ", StringComparison.Ordinal) || i.Name == "Note Skin" || i.Name == "Note Offset" || i.Name.StartsWith("Note Splash") || i.Name.StartsWith("Note Underlay")).ToList();
        _catSummary[name] = l;
        return l;
    }

    private void DrawCategoryPanel(float x, float y, float w, float h, float a)
    {
        var cats = Cats;
        if (_categoryIndex < 0 || _categoryIndex >= cats.Length) return;
        var c = cats[_categoryIndex];
        var (icon, col) = CatStyle(c.name);
        FunkTheme.Tab(c.name, x + 14f, y + 14f, col, _fm.Time, 34f, a);
        float ty = y + 66f, tx = x + 22f, tw = w - 44f;
        if (icon != null)
        {
            float b = 1f + 0.07f * FunkUI.Bop;
            FunkUI.HealthIcon(icon, x + 74f, ty + 52f, 104f * b, 0, 0f, a);
            tx = x + 140f; tw = w - 160f;
        }
        FunkUI.Paragraph(c.desc, tx, ty + 6f, tw, 17f, FunkTheme.A(FunkTheme.Text, a));
        float ly = y + 186f;
        FunkTheme.Band(x + 22f, ly - 8f, w - 44f, 3f, 2f, FunkTheme.A(col, 0.6f * a));
        var items = CatItems(c.name);
        int maxLines = (int)((y + h - 20f - ly) / 30f);
        if (items != null)
        {
            int n = 0;
            foreach (var it in items)
            {
                if (n >= maxLines) break;
                string v = PrefValueText(it);
                SummaryLine(it.Name.ToUpperInvariant(), v, x + 22f, ly + n * 30f, w - 44f, a);
                n++;
            }
            if (items.Count > maxLines) FunkTheme.Txt("+" + (items.Count - maxLines) + " MORE", x + w - 22f, y + h - 30f, 15f, FunkTheme.A(FunkTheme.Text3, a), 1f);
            return;
        }
        switch (c.name)
        {
            case "CONTROLS":
                for (int i = 0; i < 4; i++)
                {
                    FunkTheme.Txt(_controlNames[i].ToUpperInvariant(), x + 22f, ly + i * 44f + 8f, 17f, FunkTheme.A(FunkTheme.Text, a));
                    float kx = x + 130f;
                    kx += FunkUI.Key(KeyName(Input.NoteKeysAlt[i]), kx, ly + i * 44f, 32f, a, -1) + 6f;
                    kx += FunkUI.Key(KeyName(Input.NoteKeysArrow[i]), kx, ly + i * 44f, 32f, a, -1) + 14f;
                    FunkUI.Glyph(true, Input.NoteButtons[i], "", kx, ly + i * 44f, 32f, a);
                    FunkUI.Glyph(true, Input.NoteFaceButtons[i], "", kx + 38f, ly + i * 44f, 32f, a);
                }
                break;
            case "LAG ADJUSTMENT":
                FunkTheme.Txt("CURRENT NOTE OFFSET", x + 22f, ly + 4f, 17f, FunkTheme.A(FunkTheme.Text2, a));
                FunkTheme.Panel(x + 22f, ly + 34f, w - 44f, 70f, FunkTheme.Ink, a, 9f, 0f, 0f);
                { string v = HighscoreManager.Data.GlobalOffset + " MS"; float nw = FunkTheme.NumberValue(v, 0, 0, 46f, FunkTheme.ScreenText, a, false); FunkTheme.NumberValue(v, x + w / 2f - nw / 2f, ly + 69f, 46f, FunkTheme.ScreenText, a); }
                FunkUI.Paragraph("Notes scroll to the beat of the menu music: tap along, then apply the measured offset.", x + 22f, ly + 124f, w - 44f, 16f, FunkTheme.A(FunkTheme.Text2, a));
                break;
            case "MODS":
            {
                var mods = PsychMods.GetAllMods();
                SummaryLine("INSTALLED", mods.Count.ToString(), x + 22f, ly, w - 44f, a);
                SummaryLine("ENABLED", mods.Count(m => m.Enabled).ToString(), x + 22f, ly + 30f, w - 44f, a);
                break;
            }
            case "SAVE DATA":
                FunkTheme.Stamp("CAN'T BE UNDONE", x + 22f, ly, FunkTheme.Red, 0f, 15f, a);
                FunkUI.Paragraph("Clearing removes every score, rank and unlock saved by the game.", x + 22f, ly + 40f, w - 44f, 16f, FunkTheme.A(FunkTheme.Text2, a));
                break;
        }
    }

    private static void SummaryLine(string name, string v, float x, float y, float w, float a)
    {
        Color vc = v == "ON" ? FunkTheme.Green : v == "OFF" ? FunkTheme.Text3 : FunkTheme.Yellow;
        float vw = string.IsNullOrEmpty(v) ? 0f : FunkTheme.TextW(FunkMenu.Ellipsize(v, 16f, w * 0.45f), 16f);
        FunkTheme.TxtFit(name, x, y, 16f, FunkTheme.A(FunkTheme.Text2, a), w - vw - 16f);
        if (!string.IsNullOrEmpty(v)) FunkTheme.Txt(FunkMenu.Ellipsize(v, 16f, w * 0.45f), x + w, y, 16f, FunkTheme.A(vc, a), 1f);
    }

    private void DrawSettingPanel(float x, float y, float w, float h, float a)
    {
        if (_prefItems == null || _prefIndex < 0 || _prefIndex >= _prefItems.Count) return;
        var it = _prefItems[_prefIndex];
        FunkTheme.Tab(_prefPage == 2 ? "VISUALS" : _prefPage == 10 ? "REWIRED" : PrefPageTitle, x + 14f, y + 14f, ThPageColor, _fm.Time, 34f, a);
        float ty = y + 66f;
        string name = it.Kind == PrefKind.Back ? "BACK" : it.Name.ToUpperInvariant();
        var nl = FunkUI.Wrap(name, w - 44f, 22f);
        foreach (var l in nl.Take(2)) { FunkTheme.Txt(l, x + 22f, ty, 22f, FunkTheme.A(FunkTheme.Text, a)); ty += 27f; }
        if (it.PcOnly) { FunkTheme.Stamp("PC ONLY", x + 22f, ty + 2f, FunkTheme.Cyan, 0f, 14f, a); ty += 30f; }
        ty += 10f;
        // big value
        float vcy = ty + 42f, cx = x + w / 2f;
        float sinceChange = 1f - _prefChangeFlash;
        float vpop = 1f + 0.08f * MathF.Max(0f, 1f - sinceChange * 3f);
        string v = PrefValueText(it);
        switch (it.Kind)
        {
            case PrefKind.Toggle:
            {
                bool on = it.GetB();
                float age = _fm.ToggleAge(_prefIndex);
                DrawCheckboxArt(x + 40f, ty + 52f, 56f, on, age, a);
                FunkTheme.Heading(on ? "ON" : "OFF", x + 126f, ty + 56f, 0.62f, _fm.Time, 0f, a);
                vcy = ty + 60f;
                break;
            }
            case PrefKind.Percent:
            case PrefKind.Number:
            case PrefKind.Choice:
            {
                FunkTheme.Panel(x + 22f, vcy - 34f, w - 44f, 68f, FunkTheme.Ink, a, 9f, 0f, 0f);
                bool numeric = !string.IsNullOrEmpty(v) && (char.IsDigit(v[0]) || (v[0] == '-' && v.Length > 1));
                if (numeric)
                {
                    float nw = FunkTheme.NumberValue(v, 0, 0, 44f, FunkTheme.ScreenText, a, false);
                    FunkUI.Push(cx, vcy, 0f, vpop, vpop);
                    FunkTheme.NumberValue(v, cx - nw / 2f, vcy, 44f, FunkTheme.ScreenText, a);
                    FunkUI.Pop();
                }
                else FunkTheme.TxtFit(v, cx, vcy - 12f, 24f, FunkTheme.A(FunkTheme.ScreenText, a), w - 120f, 0.5f);
                float nudge = MathF.Max(0f, MathF.Sin(_fm.Time * 7f)) * 3f;
                FunkTheme.Txt("<", x + 34f - nudge, vcy - 13f, 26f, FunkTheme.A(FunkTheme.Yellow, a));
                FunkTheme.Txt(">", x + w - 34f + nudge, vcy - 13f, 26f, FunkTheme.A(FunkTheme.Yellow, a), 1f);
                if (it.Kind == PrefKind.Percent)
                    FunkMenu.SliderBar(x + 30f, vcy + 46f, w - 60f, 18f, it.Max > it.Min ? (it.GetF() - it.Min) / (it.Max - it.Min) : 0f, a);
                else if (it.Kind == PrefKind.Choice && it.Choices != null && it.Choices.Length > 1 && it.Choices.Length <= 16)
                {
                    int ci = Math.Max(0, System.Array.IndexOf(it.Choices, it.GetS()));
                    float dw = 16f, dg = 8f, dx = cx - (it.Choices.Length * (dw + dg) - dg) / 2f;
                    for (int i = 0; i < it.Choices.Length; i++)
                        FunkTheme.Panel(dx + i * (dw + dg), vcy + 48f, dw, 10f, i == ci ? FunkTheme.Yellow : FunkTheme.NavyLo, a, 3f, 2f, 0f);
                }
                break;
            }
            case PrefKind.Text:
            case PrefKind.Action:
                if (!string.IsNullOrEmpty(v))
                {
                    FunkTheme.Panel(x + 22f, vcy - 26f, w - 44f, 52f, FunkTheme.Ink, a, 9f, 0f, 0f);
                    FunkTheme.TxtFit(it.GetS?.Invoke() ?? v, cx, vcy - 10f, 20f, FunkTheme.A(FunkTheme.ScreenText, a), w - 70f, 0.5f);
                }
                break;
        }
        ty = vcy + 80f;
        // previews
        float previewH = 0f;
        if (it.Name == "HUD Scale" || it.Name == "Safe Area") previewH = 150f;
        int lane = -1;
        if (it.Name.StartsWith("Note ", StringComparison.Ordinal) && it.Kind == PrefKind.Text) lane = System.Array.FindIndex(LaneNames, l => it.Name.StartsWith("Note " + l + " ", StringComparison.Ordinal));
        if (lane >= 0) previewH = 92f;
        bool skinPreview = it.Name == "Note Skin" || it.Name == "Button Icons";
        if (skinPreview) previewH = 104f;
        string desc = it.Kind == PrefKind.Back ? "Back to the options menu." : it.Desc;
        float descMax = y + h - 56f - previewH - ty;
        var dl = FunkUI.Wrap(desc ?? "", w - 44f, 17f);
        int maxL = Math.Max(1, (int)(descMax / 21f));
        if (dl.Count > maxL) { dl = dl.GetRange(0, maxL); dl[maxL - 1] = FunkMenu.Ellipsize(dl[maxL - 1] + " ...", 17f, w - 44f); }
        foreach (var l in dl) { FunkTheme.Txt(l, x + 22f, ty, 17f, FunkTheme.A(FunkTheme.Text2, a)); ty += 21f; }
        float py = y + h - 52f - previewH;
        if (previewH > 0f && lane < 0 && !skinPreview)
        {   // mini screen: the HUD area for the current HUD Scale x Safe Area
            float sw = 230f, sh = sw * 9f / 16f, sx = cx - sw / 2f;
            FunkTheme.Panel(sx, py, sw, sh, FunkTheme.Ink, a, 6f, 3f, 0f);
            float k = PlayScene.HudUserScale, hw = sw * k, hh = sh * k, hx = sx + (sw - hw) / 2f, hy = py + (sh - hh) / 2f;
            var yc = FunkTheme.A(FunkTheme.Yellow, a);
            FunkTheme.Box(hx, hy, hw, 3f, yc); FunkTheme.Box(hx, hy + hh - 3f, hw, 3f, yc); FunkTheme.Box(hx, hy, 3f, hh, yc); FunkTheme.Box(hx + hw - 3f, hy, 3f, hh, yc);
            FunkTheme.Txt("HUD AREA", sx + sw / 2f, py + sh / 2f - 8f, 16f, FunkTheme.A(FunkTheme.Text2, a), 0.5f);
        }
        else if (skinPreview) DrawNoteSkinPreview(cx, py, w - 60f, a);
        else if (lane >= 0)
        {
            var rgb = SafeRgb()[lane];
            float sw = 96f, gap = 12f, sx = cx - (3 * sw + 2 * gap) / 2f;
            for (int p = 0; p < 3; p++)
            {
                uint c = rgb[p];
                var col = new Color((int)((c >> 16) & 0xFF), (int)((c >> 8) & 0xFF), (int)(c & 0xFF));
                FunkTheme.Panel(sx + p * (sw + gap), py, sw, 44f, col, a, 7f, 3f, 0f);
                FunkTheme.Txt(PartNames[p].ToUpperInvariant(), sx + p * (sw + gap) + sw / 2f, py + 54f, 15f, FunkTheme.A(it.Name.EndsWith(PartNames[p]) ? FunkTheme.Yellow : FunkTheme.Text2, a), 0.5f);
            }
        }
        // what the buttons do on this row
        var chips = new List<ButtonPrompts.Prompt>();
        switch (it.Kind)
        {
            case PrefKind.Toggle: chips.Add(ButtonPrompts.Confirm("TOGGLE")); break;
            case PrefKind.Percent: case PrefKind.Number: case PrefKind.Choice: chips.Add(ButtonPrompts.LeftRight("CHANGE")); break;
            case PrefKind.Text: chips.Add(ButtonPrompts.Confirm("EDIT")); break;
            case PrefKind.Action: chips.Add(ButtonPrompts.Confirm("OPEN")); break;
            default: chips.Add(ButtonPrompts.Confirm("BACK")); break;
        }
        PanelChips(chips, x + 22f, y + h - 44f, a);
    }

    // ---- Note Skin preview: the 4 notes of the chosen style (button icons / original arrows / Psych skin) ----
    private readonly Dictionary<string, SpriteSheet> _skinPrevSheets = new();
    private SpriteSheet SkinPrevSheet(string path)
    {
        if (_skinPrevSheets.TryGetValue(path, out var sh)) return sh;
        try { sh = SpriteSheet.Load(Game, path); } catch { sh = null; }
        _skinPrevSheets[path] = sh; return sh;
    }

    /// <summary>Psych note skins are grey RGB-palette art: result = R*colour + G*white + B*outline (Psych default arrowRGB).</summary>
    private static readonly uint[,] PsychRgb = { { 0xC24B99, 0xFFFFFF, 0x3C1F56 }, { 0x00FFFF, 0xFFFFFF, 0x1542B7 }, { 0x12FA05, 0xFFFFFF, 0x0A4447 }, { 0xF9393F, 0xFFFFFF, 0x651038 } };
    private readonly Dictionary<string, Texture2D> _psychTint = new();
    private Texture2D PsychTinted(string skin, int lane, Texture2D src, Rectangle r)
    {
        string key = skin + "|" + lane;
        if (_psychTint.TryGetValue(key, out var t)) return t;
        var px = new Color[r.Width * r.Height]; src.GetData(0, r, px, 0, px.Length);
        float C(uint c, int sh) => ((c >> sh) & 0xFF) / 255f;
        for (int i = 0; i < px.Length; i++)
        {
            var p0 = px[i]; if (p0.A == 0) continue;
            float al = p0.A / 255f, R = p0.R / 255f / al, G = p0.G / 255f / al, B = p0.B / 255f / al;   // premultiplied -> straight
            float o(int sh) => Math.Min(1f, R * C(PsychRgb[lane, 0], sh) + G * C(PsychRgb[lane, 1], sh) + B * C(PsychRgb[lane, 2], sh));
            px[i] = new Color(o(16) * al, o(8) * al, o(0) * al, al);
        }
        t = new Texture2D(Game.GraphicsDevice, r.Width, r.Height); t.SetData(px);
        return _psychTint[key] = t;
    }

    private void DrawNoteSkinPreview(float cx, float py, float maxW, float a)
    {
        var sb = FunkUI.Batch; if (sb == null) return;
        string v = NoteStyles.Current ?? NoteStyles.Arrows;
        float size = Math.Min(78f, (maxW - 3 * 12f) / 4f), gap = 12f, x0 = cx - (4 * size + 3 * gap) / 2f + size / 2f, cy = py + size / 2f + 4f;
        FunkTheme.Panel(cx - maxW / 2f, py - 6f, maxW, size + 22f, FunkTheme.Ink, a, 8f, 0f, 0f);
        int style = NoteStyles.ButtonStyle(v);
        var pads = new[] { Microsoft.Xna.Framework.Input.Buttons.X, Microsoft.Xna.Framework.Input.Buttons.A, Microsoft.Xna.Framework.Input.Buttons.Y, Microsoft.Xna.Framework.Input.Buttons.B };
        for (int lane = 0; lane < 4; lane++)
        {
            var c = new Vector2(x0 + lane * (size + gap), cy);
            if (style > 0) { ControllerNoteSkin.DrawNote(sb, Assets, lane, pads[lane], c, size * 0.92f, a, 0f, 1f, style); continue; }
            SpriteSheet sh; string frame;
            if (v == NoteStyles.Arrows) { sh = SkinPrevSheet("game/skins/default/notes"); frame = new[] { "left note", "down note", "up note", "right note" }[lane]; }
            else { sh = SkinPrevSheet("images/psych/noteSkins/NOTE_assets-" + v.ToLowerInvariant().Replace(' ', '_')); frame = new[] { "purple", "blue", "green", "red" }[lane]; }
            var fr = sh?.GetAnimation(frame);
            if (fr == null || fr.Count == 0 || sh.Texture == null) continue;
            var f = fr[0]; var r = f.SourceRect; var tex = sh.Texture;
            if (v != NoteStyles.Arrows) { tex = PsychTinted(v, lane, sh.Texture, r); r = new Rectangle(0, 0, tex.Width, tex.Height); }   // Psych RGB palette (grey art)
            float w = f.Rotated ? r.Height : r.Width, h = f.Rotated ? r.Width : r.Height, k = size / Math.Max(w, h);
            sb.Draw(tex, c, r, Color.White * a, f.Rotated ? -MathF.PI / 2f : 0f, new Vector2(r.Width / 2f, r.Height / 2f), k, SpriteEffects.None, 0f);
        }
    }

    private void DrawControlPanel(FunkMenu.Row row, float x, float y, float w, float h, float a)
    {
        FunkTheme.Tab(_controlTab == 0 ? "KEYBOARD" : "CONTROLLER", x + 14f, y + 14f, FunkTheme.Cyan, _fm.Time, 34f, a);
        if (row == null) return;
        float ty = y + 66f;
        FunkTheme.Txt(row.Label, x + 22f, ty, 22f, FunkTheme.A(FunkTheme.Text, a)); ty += 40f;
        if (row.Cells != null)
        {
            string[] slots = _controlTab == 0 ? new[] { "MAIN", "ALT" } : row.Cells.Count == 3 ? new[] { "D-PAD", "FACE", "SHOULDER" } : new[] { "BUTTON" };
            float cw = (w - 44f - (row.Cells.Count - 1) * 12f) / row.Cells.Count;
            for (int c = 0; c < row.Cells.Count; c++)
            {
                var (text, pad) = row.Cells[c];
                float bx = x + 22f + c * (cw + 12f);
                bool cs = c == row.CellSel;
                FunkTheme.Panel(bx, ty, cw, 74f, cs ? FunkTheme.Yellow : FunkTheme.Ink, a, 8f, 3f, cs ? 5f : 0f);
                if (pad.HasValue) FunkUI.Glyph(true, pad.Value, "", bx + cw / 2f - 21f, ty + 8f, 42f, a);
                else { string kt = text.Length > 9 ? text.Substring(0, 9) : text; FunkUI.Key(kt, bx + cw / 2f - FunkUI.MeasureGlyph(false, kt, 38f) / 2f, ty + 8f, 38f, a, -1); }
                FunkTheme.TxtFit(c < slots.Length ? slots[c] : "", bx + cw / 2f, ty + 54f, 14f, FunkTheme.A(cs ? FunkTheme.Ink : FunkTheme.Text2, a), cw - 8f, 0.5f);
            }
            ty += 96f;
        }
        var dl = FunkUI.Wrap(row.Desc ?? "", w - 44f, 17f);
        foreach (var l in dl.Take(6)) { FunkTheme.Txt(l, x + 22f, ty, 17f, FunkTheme.A(FunkTheme.Text2, a)); ty += 21f; }
        if (_controlTab == 1 && ControllerIcons.SinceFlash < 2.5f)
        {
            // the button you just pressed, in the current icon set (pushed in while held/flashing)
            var lb = ControllerIcons.LastFlash;
            float fa = a * Math.Clamp((2.5f - ControllerIcons.SinceFlash) / 0.4f, 0f, 1f), gy = y + h - 120f;
            FunkTheme.Txt("PRESSED", x + 22f, gy + 20f, 15f, FunkTheme.A(FunkTheme.Text3, fa));
            FunkUI.Glyph(true, lb, "", x + 110f, gy, 56f, fa);
            FunkTheme.Txt(ControllerIcons.ButtonName(lb).ToUpperInvariant(), x + 178f, gy + 18f, 20f, FunkTheme.A(FunkTheme.Text, fa));
        }
        if (_alphabetBob - _bindSavedT < 1.5f) FunkTheme.Stamp("SAVED", x + w - 22f, y + 20f, FunkTheme.Green, 1f, 15f, a);
        var chips = new List<ButtonPrompts.Prompt>();
        if (row.Cells != null) { chips.Add(ButtonPrompts.Confirm("REBIND")); if (row.Cells.Count > 1) chips.Add(ButtonPrompts.LeftRight("SLOT")); }
        else chips.Add(ButtonPrompts.Confirm("SELECT"));
        PanelChips(chips, x + 22f, y + h - 44f, a);
    }

    private void DrawSavePanel(FunkMenu.Row row, float x, float y, float w, float h, float a)
    {
        FunkTheme.Tab("SAVE DATA", x + 14f, y + 14f, FunkTheme.Red, _fm.Time, 34f, a);
        if (row == null) return;
        float ty = y + 66f;
        FunkTheme.Txt(row.Label, x + 22f, ty, 22f, FunkTheme.A(FunkTheme.Text, a)); ty += 38f;
        if (_prefIndex == 0) { FunkTheme.Stamp("CAN'T BE UNDONE", x + 22f, ty, FunkTheme.Red, 0f, 15f, a); ty += 40f; }
        FunkUI.Paragraph(row.Desc ?? "", x + 22f, ty, w - 44f, 17f, FunkTheme.A(FunkTheme.Text2, a));
        PanelChips(new List<ButtonPrompts.Prompt> { ButtonPrompts.Confirm(_prefIndex == 0 ? "CLEAR" : "BACK") }, x + 22f, y + h - 44f, a);
    }

    // ------------------------------------------------------------------ modals
    private void DrawRebindPrompt(List<FunkMenu.Row> rows, int sel)
    {
        FunkTheme.Box(0, 0, FunkUI.W, FunkUI.H - FunkTheme.BottomH, FunkTheme.A(FunkTheme.Ink, 0.62f));
        float w = 620f, h = 230f, x = FunkUI.W / 2f - w / 2f, y = 230f;
        FunkTheme.Panel(x, y, w, h, FunkTheme.Navy);
        FunkTheme.Tab("REBIND", x + 14f, y + 14f, FunkTheme.Pink, _fm.Time, 34f);
        var row = sel >= 0 && sel < rows.Count ? rows[sel] : null;
        string slot = "";
        if (row?.Cells != null && row.Cells.Count > 1)
            slot = _controlTab == 0 ? (row.CellSel == 0 ? "  -  MAIN KEY" : "  -  ALT KEY") : "  -  " + new[] { "D-PAD", "FACE", "SHOULDER" }[Math.Clamp(row.CellSel, 0, 2)] + " SLOT";
        FunkTheme.TxtFit((row?.Label ?? "") + slot, x + w / 2f, y + 74f, 22f, FunkTheme.Text, w - 40f, 0.5f);
        float bl = 1f + 0.04f * MathF.Sin(_fm.Time * 8f);
        FunkUI.Push(x + w / 2f, y + 134f, 0f, bl, bl);
        FunkTheme.Heading(_waitingForKey ? "PRESS A KEY" : "PRESS A BUTTON", x + w / 2f, y + 112f, 0.62f, _fm.Time, 0.5f);
        FunkUI.Pop();
        FunkTheme.Txt(_waitingForKey ? "ESCAPE CANCELS" : "B / ESCAPE CANCELS", x + w / 2f, y + h - 34f, 16f, FunkTheme.Text2, 0.5f);
    }

    private void DrawThemedEditor()
    {
        FunkTheme.Box(0, 0, FunkUI.W, FunkUI.H - FunkTheme.BottomH, FunkTheme.A(FunkTheme.Ink, 0.62f));
        float w = 820f, h = 300f, x = FunkUI.W / 2f - w / 2f, y = 190f;
        FunkTheme.Panel(x, y, w, h, FunkTheme.Navy);
        FunkTheme.Tab((_editQ ?? "").TrimEnd(':').ToUpperInvariant(), x + 14f, y + 14f, FunkTheme.Yellow, _fm.Time, 34f);
        float fy = y + 70f;
        FunkTheme.Panel(x + 24f, fy, w - 150f, 56f, FunkTheme.Ink, 1f, 9f, 3f, 0f);
        bool caret = (_fm.Time * 2f) % 2f < 1.2f;
        FunkTheme.TxtFit(_editBuf + (caret ? "_" : " "), x + 44f, fy + 17f, 22f, FunkTheme.ScreenText, w - 190f);
        // letter picker (D-pad up/down)
        float px = x + w - 110f;
        FunkTheme.Panel(px, fy, 86f, 56f, FunkTheme.Yellow, 1f, 9f, 3f, 4f);
        FunkTheme.Txt(EditChars[_editChar].ToString(), px + 43f, fy + 15f, 26f, FunkTheme.Ink, 0.5f);
        FunkTheme.Txt("LETTER", px + 43f, fy + 64f, 14f, FunkTheme.Text2, 0.5f);
        FunkUI.Paragraph(_editHint ?? "", x + 24f, fy + 96f, w - 48f, 17f, FunkTheme.Text2);
    }

    // ------------------------------------------------------------------ lag adjustment
    /// <summary>Lag adjustment in the theme: the strumline (original note / receptor art) in the left panel, timing bar
    /// under it, current offset + tap stats in Freeplay digits on the right. Native resolution like the other pages.</summary>
    private void DrawCalibrationFunk(SpriteBatch sb)
    {
        _fm.Update("cal", null, FunkTheme.Purple, 1f / 60f);
        FunkUI.Begin(sb);
        FunkTheme.Backdrop(_fm.Time);
        FunkTheme.TopBar("LAG ADJUSTMENT", _fm.Time, "TAP TO THE BEAT", FunkUI.Clamp01(_fm.PageTime / 0.25f));
        float lx = ThListX, ly = 98f, lw = ThListW, lh = ThBottom - ly;
        FunkTheme.Panel(lx, ly, lw, lh, FunkTheme.NavyLo);
        double pos = Audio.MusicPosition;
        float beat = CalBeatMs;
        const float s = 0.7f, laneW = 112f, pxPerMs = 0.45f * 1.3f;
        float cx = lx + lw / 2f, recY = ly + 90f, fadeFrom = recY + 270f, fadeTo = recY + 190f, x0 = cx - laneW * 1.5f;
        for (int i = 0; i < 4; i++)
        {
            float since = _calTime - _calConfirmT[i];
            if (since >= 0f && since < 0.2f) DrawCalSprite(FunkUI.Batch, _calReceptors, $"{CalDirs[i]} confirm", (int)(since * 24f), x0 + i * laneW, recY, s);
            else DrawCalSprite(FunkUI.Batch, _calReceptors, $"{CalDirs[i]} static", 0, x0 + i * laneW, recY, s);
        }
        long b0 = (long)Math.Floor(pos / beat) - 1;
        for (long b = b0; b <= b0 + 6; b++)
        {
            float y = recY + (float)(b * beat - pos) * pxPerMs;
            if (y < recY || y > fadeFrom) continue;
            int lane = (int)((b % 4 + 4) % 4);
            float al = y <= fadeTo ? 1f : Math.Clamp((fadeFrom - y) / (fadeFrom - fadeTo), 0f, 1f);
            DrawCalSprite(FunkUI.Batch, _calNotes, $"{CalDirs[lane]} note", 0, x0 + lane * laneW, y, s, al);
        }
        foreach (var (ms, t) in _calPopups)
        {
            float k = (_calTime - t) / 0.6f;
            string txt = $"{(ms >= 0 ? "+" : "")}{(int)Math.Round(ms)} MS";
            FunkTheme.TxtOutlined(txt, cx + laneW * 2f + 24f, recY - 14f - k * 30f, 20f, FunkTheme.A(FunkTheme.Yellow, 1f - k * k));
        }
        // timing bar: -150 .. +150 ms
        float barW = lw - 140f, barX = cx - barW / 2f, barY = ly + lh - 110f, barH = 18f;
        var avg = CalAverage();
        string how = avg.HasValue
            ? ((int)Math.Round(avg.Value) == 0 ? "RIGHT ON THE BEAT!" : avg.Value > 0 ? $"YOU TAP {(int)Math.Round(avg.Value)} MS LATE" : $"YOU TAP {-(int)Math.Round(avg.Value)} MS EARLY")
            : _calTaps.Count == 0 ? "TAP ANY NOTE KEY ON EVERY BEAT" : $"KEEP TAPPING... {4 - _calTaps.Count} MORE";
        FunkTheme.Txt(how, cx, barY - 62f, 22f, avg.HasValue ? FunkTheme.Yellow : FunkTheme.Text, 0.5f);
        FunkTheme.Panel(barX, barY, barW, barH, FunkTheme.Ink, 1f, 6f, 3f, 0f);
        FunkTheme.Box(cx - 2f, barY - 12f, 4f, barH + 24f, FunkTheme.Text);
        float Bx(float ms) => cx + Math.Clamp(ms, -150f, 150f) / 150f * (barW / 2f);
        for (int i = 0; i < _calTaps.Count; i++)
        {
            float age = (_calTaps.Count - 1 - i) / (float)CAL_MAX_TAPS;
            float tx = Bx(_calTaps[i]);
            FunkTheme.Box(tx - 2f, barY - 6f, 4f, barH + 12f, FunkTheme.A(FunkTheme.Cyan, 1f - age * 0.7f));
        }
        if (avg.HasValue) FunkTheme.Panel(Bx(avg.Value) - 6f, barY - 8f, 12f, barH + 16f, FunkTheme.Yellow, 1f, 3f, 2.5f, 0f);
        FunkTheme.Txt("EARLY", barX, barY + 32f, 16f, FunkTheme.Text2);
        FunkTheme.Txt("LATE", barX + barW, barY + 32f, 16f, FunkTheme.Text2, 1f);

        // right panel
        float px = ThPanelX, pw = ThPanelW;
        FunkTheme.Panel(px, ly, pw, lh, FunkTheme.Navy);
        FunkTheme.Tab("NOTE OFFSET", px + 14f, ly + 14f, FunkTheme.Purple, _fm.Time, 34f);
        var d = HighscoreManager.Data;
        float applied = _calTime - _calApplied;
        FunkTheme.Txt("CURRENT", px + 22f, ly + 68f, 17f, FunkTheme.Text2);
        if (applied < 1.5f) FunkTheme.Stamp("SAVED", px + pw - 22f, ly + 64f, FunkTheme.Green, 1f, 15f);
        FunkTheme.Panel(px + 22f, ly + 96f, pw - 44f, 70f, FunkTheme.Ink, 1f, 9f, 0f, 0f);
        { string v = d.GlobalOffset + " MS"; float nw = FunkTheme.NumberValue(v, 0, 0, 46f, FunkTheme.ScreenText, 1f, false); FunkTheme.NumberValue(v, px + pw / 2f - nw / 2f, ly + 131f, 46f, FunkTheme.ScreenText); }
        FunkTheme.Txt("TAPS", px + 22f, ly + 190f, 17f, FunkTheme.Text2);
        FunkTheme.NumberValue($"{_calTaps.Count} / {CAL_MAX_TAPS}", px + 22f, ly + 236f, 34f, FunkTheme.ScreenText);
        if (avg.HasValue)
        {
            FunkTheme.Txt("NEW OFFSET", px + 22f, ly + 280f, 17f, FunkTheme.Text2);
            FunkTheme.NumberValue($"{Math.Clamp(-(int)Math.Round(avg.Value), -500, 500)} MS", px + 22f, ly + 326f, 34f, FunkTheme.Yellow);
        }
        FunkUI.Paragraph("Tap any note key or button on every beat of the music. After 4 taps, APPLY sets the note offset.", px + 22f, ly + lh - 150f, pw - 44f, 16f, FunkTheme.Text2);
        var chips = new List<ButtonPrompts.Prompt>(); AddCalibrationPrompts(chips);
        PanelChips(chips.Take(2).ToList(), px + 22f, ly + lh - 44f, 1f);
        _fm.DrawPrompts(Input, CurrentPrompts());
        sb.End();
    }
}
