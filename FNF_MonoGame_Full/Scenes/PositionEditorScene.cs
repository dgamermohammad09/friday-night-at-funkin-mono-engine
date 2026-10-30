using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine;
using FontStashSharp;
using Newtonsoft.Json.Linq;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Position editor — load a stage, select items from the ITEMS LIST on the left,
/// drag or nudge positions. No gameplay, no music, no notes.
/// </summary>
public class PositionEditorScene : Scene
{
    // ── Layout ────────────────────────────────────────────────────
    const int LW  = 260;  // left panel width
    const int RW  = 220;  // right panel width
    const int TH  = 34;   // toolbar height
    const int SH  = 24;   // status bar height
    const int STH = 180;  // stage list height inside left panel

    // ── Data ──────────────────────────────────────────────────────
    private readonly List<string> _stageNames = new();
    private int _stageIndex;
    private JObject _stageData;
    private string _currentStage = "";
    private string _stageFilePath = "";

    private readonly List<EditItem> _items = new();
    private int _selectedItem = -1;
    private bool _dirty;

    // ── Camera ────────────────────────────────────────────────────
    private float _camX, _camY;
    private float _zoom = 1.0f;

    // ── Scroll ────────────────────────────────────────────────────
    private int _stageScroll;
    private int _itemScroll;

    // ── Drag ──────────────────────────────────────────────────────
    private bool _dragging;
    private Vector2 _dragOffset;

    class EditItem
    {
        public string Name;
        public string Type; // "char" or "prop"
        public float X, Y;
        public float OrigX, OrigY;
        public Color MarkerColor;
        public int ZIndex;
        public float Scale = 1f;
    }

    // ── Load ──────────────────────────────────────────────────────
    public override void Load()
    {
        string root = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content", "data", "stages");
        if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(root))
        {
            foreach (var f in global::FNF_MonoGame.Engine.FsCI.GetFiles(root, "*.json"))
                _stageNames.Add(System.IO.Path.GetFileNameWithoutExtension(f));
            _stageNames.Sort(StringComparer.OrdinalIgnoreCase);
        }
        if (_stageNames.Count > 0) LoadStage(0);
    }

    public override void Unload() { }

    // ── LoadStage ─────────────────────────────────────────────────
    private void LoadStage(int index)
    {
        if (index < 0 || index >= _stageNames.Count) return;
        _stageIndex = index;
        _currentStage = _stageNames[index];
        _items.Clear();
        _selectedItem = -1;
        _dirty = false;
        _itemScroll = 0;

        _stageFilePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content", "data", "stages", _currentStage + ".json");
        if (!global::FNF_MonoGame.Engine.FsCI.FileExists(_stageFilePath)) return;
        _stageData = JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(_stageFilePath));

        // Props
        if (_stageData["props"] is JArray propsArr)
        {
            foreach (var p in propsArr)
            {
                var pos = p["position"] as JArray;
                float x = pos?.Count > 0 ? (float)pos[0] : 0;
                float y = pos?.Count > 1 ? (float)pos[1] : 0;
                _items.Add(new EditItem
                {
                    Name = p["name"]?.ToString() ?? "prop",
                    Type = "prop",
                    X = x, Y = y, OrigX = x, OrigY = y,
                    MarkerColor = new Color(80, 160, 240),
                    ZIndex = (int)(p["zIndex"] ?? 0),
                    Scale = 1f
                });
            }
        }

        // Characters
        if (_stageData["characters"] is JObject charsObj)
        {
            foreach (var kvp in charsObj)
            {
                var cv = kvp.Value;
                var pos = cv["position"] as JArray;
                float x = pos?.Count > 0 ? (float)pos[0] : 0;
                float y = pos?.Count > 1 ? (float)pos[1] : 0;
                Color col = kvp.Key.Equals("bf", StringComparison.OrdinalIgnoreCase)   ? new Color(0, 220, 100)
                          : kvp.Key.Equals("dad", StringComparison.OrdinalIgnoreCase)  ? new Color(240, 80, 80)
                          : kvp.Key.Equals("gf", StringComparison.OrdinalIgnoreCase)   ? new Color(240, 200, 60)
                          : new Color(200, 160, 255);
                _items.Add(new EditItem
                {
                    Name = kvp.Key,
                    Type = "char",
                    X = x, Y = y, OrigX = x, OrigY = y,
                    MarkerColor = col,
                    ZIndex = (int)(cv["zIndex"] ?? 0),
                    Scale = (float?)cv["scale"] ?? 1f
                });
            }
        }

        // Sort by Z-index so draw order makes sense
        _items.Sort((a, b) => a.ZIndex.CompareTo(b.ZIndex));

        ZoomToFitAll();
    }

    // ── Zoom to fit ALL items ─────────────────────────────────────
    private void ZoomToFitAll()
    {
        if (_items.Count == 0) { _camX = 0; _camY = 0; _zoom = 0.5f; return; }

        float minX = _items.Min(i => i.X);
        float maxX = _items.Max(i => i.X);
        float minY = _items.Min(i => i.Y);
        float maxY = _items.Max(i => i.Y);

        float margin = 200f;
        float worldW = (maxX - minX) + margin * 2;
        float worldH = (maxY - minY) + margin * 2;

        int vpW = FNFGame.SCREEN_WIDTH - LW - RW;
        int vpH = FNFGame.SCREEN_HEIGHT - TH - SH;

        float zx = worldW > 0 ? vpW / worldW : 1f;
        float zy = worldH > 0 ? vpH / worldH : 1f;
        _zoom = Math.Clamp(Math.Min(zx, zy), 0.05f, 3f);

        _camX = (minX + maxX) / 2f;
        _camY = (minY + maxY) / 2f;
    }

    // ── Coordinate helpers ────────────────────────────────────────
    // Viewport center in screen coords
    private float VpCX => LW + (FNFGame.SCREEN_WIDTH - LW - RW) / 2f;
    private float VpCY => TH + (FNFGame.SCREEN_HEIGHT - TH - SH) / 2f;

    private Vector2 WorldToScreen(float wx, float wy)
        => new(VpCX + (wx - _camX) * _zoom, VpCY + (wy - _camY) * _zoom);

    private Vector2 ScreenToWorld(float sx, float sy)
        => new(_camX + (sx - VpCX) / _zoom, _camY + (sy - VpCY) / _zoom);

    // ── Save ──────────────────────────────────────────────────────
    private void SaveStage()
    {
        if (_stageData == null || string.IsNullOrEmpty(_stageFilePath)) return;

        if (_stageData["props"] is JArray propsArr)
        {
            int pi = 0;
            foreach (var item in _items)
            {
                if (item.Type != "prop") continue;
                if (pi < propsArr.Count)
                    propsArr[pi]["position"] = new JArray(Math.Round(item.X, 1), Math.Round(item.Y, 1));
                pi++;
            }
        }
        if (_stageData["characters"] is JObject charsObj)
        {
            foreach (var item in _items)
            {
                if (item.Type != "char") continue;
                if (charsObj[item.Name] is JObject co)
                    co["position"] = new JArray(Math.Round(item.X, 1), Math.Round(item.Y, 1));
            }
        }
        System.IO.File.WriteAllText(_stageFilePath, _stageData.ToString(Newtonsoft.Json.Formatting.Indented));
        _dirty = false;
        EditorUI.ShowToast($"Saved {_currentStage}.json");
    }

    // ── Center camera on selected item ───────────────────────────
    private void JumpToSelected()
    {
        if (_selectedItem < 0 || _selectedItem >= _items.Count) return;
        _camX = _items[_selectedItem].X;
        _camY = _items[_selectedItem].Y;
    }

    // ── Update ────────────────────────────────────────────────────
    public override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        EditorUI.UpdateInput();
        EditorUI.UpdateToast(dt);

        int W = FNFGame.SCREEN_WIDTH, H = FNFGame.SCREEN_HEIGHT;
        var vpRect = new Rectangle(LW, TH, W - LW - RW, H - TH - SH);

        // Stage list area: clicks
        var stageListRect = new Rectangle(0, TH + 30, LW, STH);
        if (EditorUI.MouseClicked && stageListRect.Contains(EditorUI.MousePos))
        {
            int rowH = 20;
            for (int i = 0; i < _stageNames.Count; i++)
            {
                int ry = stageListRect.Y + (i - _stageScroll) * rowH;
                if (ry < stageListRect.Y || ry + rowH > stageListRect.Bottom) continue;
                if (EditorUI.MousePos.Y >= ry && EditorUI.MousePos.Y < ry + rowH)
                {
                    LoadStage(i);
                    break;
                }
            }
        }
        // Stage list scroll wheel
        if (EditorUI.IsHovered(stageListRect))
        {
            int sw = Input.ScrollDelta;
            if (sw != 0) _stageScroll = Math.Clamp(_stageScroll - sw / 120, 0, Math.Max(0, _stageNames.Count - 8));
        }

        // Items list area: clicks
        var itemListRect = new Rectangle(0, TH + STH + 64, LW, H - TH - STH - 64 - SH);
        if (EditorUI.MouseClicked && itemListRect.Contains(EditorUI.MousePos))
        {
            int rowH = 22;
            for (int i = 0; i < _items.Count; i++)
            {
                int ry = itemListRect.Y + (i - _itemScroll) * rowH;
                if (ry < itemListRect.Y || ry + rowH > itemListRect.Bottom) continue;
                if (EditorUI.MousePos.Y >= ry && EditorUI.MousePos.Y < ry + rowH)
                {
                    _selectedItem = i;
                    JumpToSelected();
                    break;
                }
            }
        }
        // Items list scroll
        if (EditorUI.IsHovered(itemListRect))
        {
            int sw = Input.ScrollDelta;
            if (sw != 0) _itemScroll = Math.Clamp(_itemScroll - sw / 120, 0, Math.Max(0, _items.Count - 10));
        }

        // Keyboard: stage navigation
        if (!Input.IsHeld(Keys.LeftControl))
        {
            if (Input.IsPressed(Keys.PageUp))   LoadStage((_stageIndex - 1 + _stageNames.Count) % _stageNames.Count);
            if (Input.IsPressed(Keys.PageDown)) LoadStage((_stageIndex + 1) % _stageNames.Count);
        }

        // Save
        if (Input.IsPressed(Keys.S) && Input.IsHeld(Keys.LeftControl)) { SaveStage(); return; }

        // Reset
        if (Input.IsPressed(Keys.R) && !Input.IsHeld(Keys.LeftControl) && _selectedItem >= 0)
        {
            var it = _items[_selectedItem];
            it.X = it.OrigX; it.Y = it.OrigY;
            _dirty = true;
            EditorUI.ShowToast($"Reset {it.Name}");
        }
        if (Input.IsPressed(Keys.R) && Input.IsHeld(Keys.LeftControl))
        {
            foreach (var it in _items) { it.X = it.OrigX; it.Y = it.OrigY; }
            _dirty = true; EditorUI.ShowToast("Reset ALL");
        }

        // F key = zoom-to-fit
        if (Input.IsPressed(Keys.F)) ZoomToFitAll();
        // G key = jump to selected
        if (Input.IsPressed(Keys.G) && _selectedItem >= 0) JumpToSelected();

        // Viewport interactions
        if (EditorUI.IsHovered(vpRect))
        {
            // Scroll wheel zoom
            int scroll = Input.ScrollDelta;
            if (scroll != 0)
            {
                float oldZoom = _zoom;
                if (scroll > 0) _zoom = Math.Min(4f, _zoom * 1.15f);
                if (scroll < 0) _zoom = Math.Max(0.05f, _zoom / 1.15f);
                var mwp = ScreenToWorld(EditorUI.MousePos.X, EditorUI.MousePos.Y);
                _camX = mwp.X - (EditorUI.MousePos.X - VpCX) / _zoom;
                _camY = mwp.Y - (EditorUI.MousePos.Y - VpCY) / _zoom;
            }

            // Left click: hit test items
            if (EditorUI.MouseClicked && !_dragging)
            {
                bool hit = false;
                const float HIT_RADIUS = 20f; // fixed pixel radius, independent of zoom
                for (int i = _items.Count - 1; i >= 0; i--)
                {
                    var sp = WorldToScreen(_items[i].X, _items[i].Y);
                    if (Math.Abs(EditorUI.MousePos.X - sp.X) < HIT_RADIUS &&
                        Math.Abs(EditorUI.MousePos.Y - sp.Y) < HIT_RADIUS)
                    {
                        _selectedItem = i;
                        var mwp = ScreenToWorld(EditorUI.MousePos.X, EditorUI.MousePos.Y);
                        _dragOffset = new Vector2(_items[i].X - mwp.X, _items[i].Y - mwp.Y);
                        _dragging = true;
                        hit = true;
                        break;
                    }
                }
                if (!hit) { _selectedItem = -1; }
            }
        }

        // Drag move
        if (_dragging && EditorUI.MouseDown && _selectedItem >= 0)
        {
            var mwp = ScreenToWorld(EditorUI.MousePos.X, EditorUI.MousePos.Y);
            var it = _items[_selectedItem];
            it.X = mwp.X + _dragOffset.X;
            it.Y = mwp.Y + _dragOffset.Y;
            _dirty = true;
        }
        if (EditorUI.MouseReleased) _dragging = false;

        // Right drag = pan
        if (EditorUI.Mouse.RightButton == ButtonState.Pressed)
        {
            _camX -= EditorUI.MouseDelta.X / _zoom;
            _camY -= EditorUI.MouseDelta.Y / _zoom;
        }

        // Arrow key nudge selected item
        if (_selectedItem >= 0 && !Input.IsHeld(Keys.LeftControl))
        {
            float step = Input.IsHeld(Keys.LeftShift) ? 10f : 1f;
            bool moved = false;
            if (Input.IsPressed(Keys.Left))  { _items[_selectedItem].X -= step; moved = true; }
            if (Input.IsPressed(Keys.Right)) { _items[_selectedItem].X += step; moved = true; }
            if (Input.IsPressed(Keys.Up))    { _items[_selectedItem].Y -= step; moved = true; }
            if (Input.IsPressed(Keys.Down))  { _items[_selectedItem].Y += step; moved = true; }
            if (moved) _dirty = true;
        }

        // Tab cycles items
        if (Input.IsPressed(Keys.Tab) && _items.Count > 0)
        {
            _selectedItem = (_selectedItem + 1) % _items.Count;
            JumpToSelected();
            // Scroll item list so selection is visible
            _itemScroll = Math.Clamp(_selectedItem - 4, 0, Math.Max(0, _items.Count - 10));
        }
        if (Input.IsPressed(Keys.Escape)) _selectedItem = -1;
        if (Input.BackPressed) Game.Scenes.ChangeScene(new EditorHubScene());
    }

    // ── Draw ──────────────────────────────────────────────────────
    public override void Draw(SpriteBatch sb)
    {
        var px = Assets.Pixel;
        var font   = Assets.GetFont(14);
        var fontSm = Assets.GetFont(12);
        int W = FNFGame.SCREEN_WIDTH, H = FNFGame.SCREEN_HEIGHT;
        var vp = new Rectangle(LW, TH, W - LW - RW, H - TH - SH);

        sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);

        // Full background
        EditorUI.FillRect(sb, px, new Rectangle(0, 0, W, H), EditorUI.BgDark);

        // Viewport background + grid
        EditorUI.FillRect(sb, px, vp, new Color(22, 22, 32));
        EditorUI.DrawGrid(sb, px, vp, new Vector2(VpCX - _camX * _zoom, VpCY - _camY * _zoom), _zoom, 100);
        EditorUI.DrawBorder(sb, px, vp, EditorUI.Border);

        // Origin crosshair
        var orig = WorldToScreen(0, 0);
        if (orig.X > vp.X && orig.X < vp.Right && orig.Y > vp.Y && orig.Y < vp.Bottom)
            EditorUI.DrawCrosshair(sb, px, orig.X, orig.Y, 30, EditorUI.Border);

        // ── Draw items ──
        const int MARKER_HALF = 18; // fixed screen-space half-size
        foreach (var item in _items)
        {
            var sp = WorldToScreen(item.X, item.Y);
            // Skip if way off viewport
            if (sp.X < vp.X - 60 || sp.X > vp.Right + 60 || sp.Y < vp.Y - 60 || sp.Y > vp.Bottom + 60)
                continue;

            int idx = _items.IndexOf(item);
            bool sel = (idx == _selectedItem);
            Color c = item.MarkerColor;

            int ix = (int)sp.X, iy = (int)sp.Y;

            // Shadow
            sb.Draw(px, new Rectangle(ix - MARKER_HALF + 2, iy - MARKER_HALF + 2, MARKER_HALF * 2, MARKER_HALF * 2), new Color(0, 0, 0, 80));
            // Filled body
            sb.Draw(px, new Rectangle(ix - MARKER_HALF, iy - MARKER_HALF, MARKER_HALF * 2, MARKER_HALF * 2), sel ? Color.White : c * 0.75f);
            // Border
            EditorUI.DrawBorder(sb, px, new Rectangle(ix - MARKER_HALF, iy - MARKER_HALF, MARKER_HALF * 2, MARKER_HALF * 2), sel ? Color.White : c);

            // Character icon inside marker
            if (item.Type == "char" && fontSm != null)
            {
                string icon = item.Name.Length > 0 ? item.Name[..1].ToUpper() : "?";
                float iw = fontSm.MeasureString(icon).X;
                fontSm.DrawText(sb, icon, new Vector2(ix - iw / 2f, iy - 7f), sel ? EditorUI.BgDark : Color.White);
            }

            // Label above marker
            if (fontSm != null)
            {
                string lbl = item.Name;
                float lw = fontSm.MeasureString(lbl).X;
                // Drop shadow
                fontSm.DrawText(sb, lbl, new Vector2(ix - lw / 2f + 1, iy - MARKER_HALF - 15f + 1), Color.Black * 0.8f);
                fontSm.DrawText(sb, lbl, new Vector2(ix - lw / 2f, iy - MARKER_HALF - 15f), sel ? Color.White : c);
            }

            // Position below marker (only when selected)
            if (sel && fontSm != null)
            {
                string pos = $"({item.X:F1}, {item.Y:F1})";
                float pw = fontSm.MeasureString(pos).X;
                fontSm.DrawText(sb, pos, new Vector2(ix - pw / 2f + 1, iy + MARKER_HALF + 4f + 1), Color.Black * 0.8f);
                fontSm.DrawText(sb, pos, new Vector2(ix - pw / 2f, iy + MARKER_HALF + 4f), EditorUI.Warning);
            }

            // Selection ring
            if (sel)
                EditorUI.DrawBorder(sb, px, new Rectangle(ix - MARKER_HALF - 3, iy - MARKER_HALF - 3, MARKER_HALF * 2 + 6, MARKER_HALF * 2 + 6), EditorUI.Accent);
        }

        sb.End();

        // ── UI Panels ──────────────────────────────────────────────
        sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend);

        // Toolbar
        EditorUI.DrawToolbar(sb, px, new Rectangle(0, 0, W, TH));
        if (font != null)
        {
            string title = $"Position Editor  —  {_currentStage}" + (_dirty ? "  *" : "");
            font.DrawText(sb, title, new Vector2(12, 8), EditorUI.TextPrimary);
        }
        if (fontSm != null)
        {
            string hint = "Tab=next  |  Arrows=nudge  |  Shift=10px  |  F=fit  |  G=goto  |  R=reset  |  Ctrl+S=save  |  Esc=back";
            float hw = fontSm.MeasureString(hint).X;
            fontSm.DrawText(sb, hint, new Vector2(W - hw - 8, 10), EditorUI.TextDim);
        }

        // ── Left panel ──────────────────────────────────────────────
        EditorUI.FillRect(sb, px, new Rectangle(0, TH, LW, H - TH - SH), EditorUI.BgPanel);
        EditorUI.DrawBorder(sb, px, new Rectangle(0, TH, LW, H - TH - SH), EditorUI.Border);

        // Stage list section header
        EditorUI.PropertyHeader(sb, px, fontSm, 0, TH, LW, "Stages  (click to load)");
        int stageRowH = 20;
        var stageArea = new Rectangle(0, TH + 28, LW, STH);
        EditorUI.FillRect(sb, px, stageArea, EditorUI.BgDark);
        for (int i = 0; i < _stageNames.Count; i++)
        {
            int ry = stageArea.Y + (i - _stageScroll) * stageRowH;
            if (ry < stageArea.Y || ry + stageRowH > stageArea.Bottom) continue;
            var rr = new Rectangle(2, ry, LW - 4, stageRowH);
            bool sel = (i == _stageIndex);
            if (sel) { EditorUI.FillRect(sb, px, rr, EditorUI.Selected); sb.Draw(px, new Rectangle(rr.X, rr.Y, 3, rr.Height), EditorUI.Accent); }
            else if (EditorUI.IsHovered(rr)) EditorUI.FillRect(sb, px, rr, EditorUI.Hover);
            fontSm?.DrawText(sb, _stageNames[i], new Vector2(rr.X + 8, rr.Y + 3), sel ? EditorUI.TextPrimary : EditorUI.TextSecondary);
        }

        // Items list section header
        int itemHeaderY = TH + STH + 34;
        EditorUI.PropertyHeader(sb, px, fontSm, 0, itemHeaderY, LW, $"Items  ({_items.Count})  — click to select");
        int itemRowH = 22;
        var itemArea = new Rectangle(0, itemHeaderY + 24, LW, H - itemHeaderY - 24 - SH);
        EditorUI.FillRect(sb, px, itemArea, EditorUI.BgDark);
        for (int i = 0; i < _items.Count; i++)
        {
            int ry = itemArea.Y + (i - _itemScroll) * itemRowH;
            if (ry < itemArea.Y || ry + itemRowH > itemArea.Bottom) continue;
            var rr = new Rectangle(2, ry, LW - 4, itemRowH);
            bool sel = (i == _selectedItem);
            if (sel) { EditorUI.FillRect(sb, px, rr, EditorUI.Selected); sb.Draw(px, new Rectangle(rr.X, rr.Y, 3, rr.Height), _items[i].MarkerColor); }
            else if (EditorUI.IsHovered(rr)) EditorUI.FillRect(sb, px, rr, EditorUI.Hover);

            // Color dot
            sb.Draw(px, new Rectangle(rr.X + 6, rr.Y + 7, 8, 8), _items[i].MarkerColor);
            // Type tag
            Color typeCol = _items[i].Type == "char" ? EditorUI.Success : EditorUI.Accent;
            fontSm?.DrawText(sb, _items[i].Type == "char" ? "C" : "P", new Vector2(rr.X + 18, rr.Y + 4), typeCol);
            // Name
            fontSm?.DrawText(sb, _items[i].Name, new Vector2(rr.X + 30, rr.Y + 4), sel ? EditorUI.TextPrimary : EditorUI.TextSecondary);
            // X/Y
            fontSm?.DrawText(sb, $"{_items[i].X:F0},{_items[i].Y:F0}", new Vector2(rr.X + 150, rr.Y + 4), sel ? EditorUI.Warning : EditorUI.TextDim);
        }

        // ── Right panel ─────────────────────────────────────────────
        EditorUI.DrawPanel(sb, px, new Rectangle(W - RW, TH, RW, H - TH - SH), "Properties", font);
        if (_selectedItem >= 0 && _selectedItem < _items.Count)
        {
            var it = _items[_selectedItem];
            int py = TH + 38;
            int px2 = W - RW + 10;
            int pw = RW - 20;

            // Color swatch
            sb.Draw(px, new Rectangle(px2, py, pw, 4), it.MarkerColor); py += 12;

            EditorUI.PropertyRow(sb, px, fontSm, px2, py, pw, "Name",    it.Name);         py += 22;
            EditorUI.PropertyRow(sb, px, fontSm, px2, py, pw, "Type",    it.Type);         py += 22;
            EditorUI.PropertyRow(sb, px, fontSm, px2, py, pw, "X",       $"{it.X:F2}",   true); py += 22;
            EditorUI.PropertyRow(sb, px, fontSm, px2, py, pw, "Y",       $"{it.Y:F2}",   true); py += 22;
            EditorUI.PropertyRow(sb, px, fontSm, px2, py, pw, "Z-Index", it.ZIndex.ToString()); py += 22;
            EditorUI.PropertyRow(sb, px, fontSm, px2, py, pw, "Scale",   $"{it.Scale:F2}"); py += 22;
            EditorUI.PropertyRow(sb, px, fontSm, px2, py, pw, "Orig X",  $"{it.OrigX:F1}"); py += 22;
            EditorUI.PropertyRow(sb, px, fontSm, px2, py, pw, "Orig Y",  $"{it.OrigY:F1}"); py += 22;

            py += 12;
            EditorUI.PropertyHeader(sb, px, fontSm, px2, py, pw, "Controls"); py += 28;
            fontSm?.DrawText(sb, "Arrows = nudge 1px",   new Vector2(px2, py), EditorUI.TextDim); py += 16;
            fontSm?.DrawText(sb, "Shift+Arrows = 10px",  new Vector2(px2, py), EditorUI.TextDim); py += 16;
            fontSm?.DrawText(sb, "Drag = move freely",   new Vector2(px2, py), EditorUI.TextDim); py += 16;
            fontSm?.DrawText(sb, "Tab = next item",       new Vector2(px2, py), EditorUI.TextDim); py += 16;
            fontSm?.DrawText(sb, "G = jump camera here", new Vector2(px2, py), EditorUI.TextDim); py += 16;
            fontSm?.DrawText(sb, "F = fit all in view",   new Vector2(px2, py), EditorUI.TextDim); py += 16;
            fontSm?.DrawText(sb, "R = reset to original",new Vector2(px2, py), EditorUI.TextDim); py += 16;
            fontSm?.DrawText(sb, "Ctrl+S = save",         new Vector2(px2, py), EditorUI.TextDim); py += 16;
        }
        else
        {
            fontSm?.DrawText(sb, "← Click an item\nin the list to\nselect it.", new Vector2(W - RW + 10, TH + 44), EditorUI.TextSecondary);
            fontSm?.DrawText(sb, "F = fit all items\nin viewport", new Vector2(W - RW + 10, TH + 110), EditorUI.TextDim);
        }

        // Status bar
        string status = _dirty ? "* UNSAVED — Ctrl+S to save" : "Saved";
        string selStr = _selectedItem >= 0 ? $"[{_selectedItem}] {_items[_selectedItem].Name}  ({_items[_selectedItem].X:F1},{_items[_selectedItem].Y:F1})" : "Nothing selected";
        EditorUI.DrawStatusBar(sb, px, fontSm, W, H,
            $"Stage: {_stageIndex + 1}/{_stageNames.Count}  |  {_items.Count} items",
            selStr,
            status,
            $"Zoom: {_zoom:F2}x");
        EditorUI.DrawToast(sb, px, font, W, H);
        sb.End();
    }
}
