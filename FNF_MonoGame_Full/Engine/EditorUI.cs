using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FontStashSharp;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Professional dark-theme UI toolkit for all editor scenes.
/// All drawing is done with a single pixel texture + SpriteFontBase.
/// </summary>
public static class EditorUI
{
    // ── Theme Colors ──────────────────────────────────────────────
    public static readonly Color BgDark       = new(18, 18, 26);
    public static readonly Color BgPanel      = new(26, 26, 36);
    public static readonly Color BgToolbar    = new(33, 33, 46);
    public static readonly Color BgInput      = new(20, 20, 30);
    public static readonly Color Border       = new(48, 48, 62);
    public static readonly Color BorderLight  = new(70, 70, 90);
    public static readonly Color TextPrimary  = new(232, 232, 240);
    public static readonly Color TextSecondary= new(155, 155, 175);
    public static readonly Color Primary      = Accent;
    public static readonly Color TextDim      = new(105, 105, 125);
    public static readonly Color Accent       = new(96, 152, 232);
    public static readonly Color AccentHover  = new(116, 172, 252);
    public static readonly Color AccentDim    = new(72, 118, 192);
    public static readonly Color Selected     = new(84, 138, 220, 50);
    public static readonly Color Hover        = new(255, 255, 255, 12);
    public static readonly Color Success      = new(82, 205, 180);
    public static readonly Color Warning      = new(232, 196, 128);
    public static readonly Color Error        = new(228, 112, 122);
    public static readonly Color Gold         = new(255, 215, 0);

    // ── State ─────────────────────────────────────────────────────
    private static MouseState _mouse, _prevMouse;
    private static int _hotItem = -1; // hovered item hash
    private static int _activeItem = -1; // clicked/dragging item hash

    public static MouseState Mouse => _mouse;
    public static bool MouseClicked => _mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
    public static bool MouseDown => _mouse.LeftButton == ButtonState.Pressed;
    public static bool MouseReleased => _mouse.LeftButton == ButtonState.Released && _prevMouse.LeftButton == ButtonState.Pressed;
    public static bool RightClicked => _mouse.RightButton == ButtonState.Pressed && _prevMouse.RightButton == ButtonState.Released;
    public static Vector2 MousePos => new(_mouse.X, _mouse.Y);
    public static Vector2 MouseDelta => new(_mouse.X - _prevMouse.X, _mouse.Y - _prevMouse.Y);

    public static void UpdateInput()
    {
        _prevMouse = _mouse;
        _mouse = FNFGame.Instance?.Input?.MouseNow ?? Microsoft.Xna.Framework.Input.Mouse.GetState(); // filtered mouse
    }

    public static bool IsHovered(Rectangle rect) => rect.Contains(_mouse.X, _mouse.Y);
    public static bool IsMouseOver(Rectangle rect) => IsHovered(rect);

    // ── Basic Drawing ─────────────────────────────────────────────

    public static void FillRect(SpriteBatch sb, Texture2D px, Rectangle r, Color c)
        => sb.Draw(px, r, c);

    public static void DrawBorder(SpriteBatch sb, Texture2D px, Rectangle r, Color c, int t = 1)
    {
        sb.Draw(px, new Rectangle(r.X, r.Y, r.Width, t), c);
        sb.Draw(px, new Rectangle(r.X, r.Bottom - t, r.Width, t), c);
        sb.Draw(px, new Rectangle(r.X, r.Y, t, r.Height), c);
        sb.Draw(px, new Rectangle(r.Right - t, r.Y, t, r.Height), c);
    }

    public static void DrawSeparatorH(SpriteBatch sb, Texture2D px, int x, int y, int w)
        => sb.Draw(px, new Rectangle(x, y, w, 1), Border);

    public static void DrawSeparatorV(SpriteBatch sb, Texture2D px, int x, int y, int h)
        => sb.Draw(px, new Rectangle(x, y, 1, h), Border);

    // ── Panel ─────────────────────────────────────────────────────

    public static void DrawPanel(SpriteBatch sb, Texture2D px, Rectangle r, string title = null, SpriteFontBase font = null)
    {
        FillRect(sb, px, r, BgPanel);
        DrawBorder(sb, px, r, Border);
        if (title != null && font != null)
        {
            FillRect(sb, px, new Rectangle(r.X, r.Y, r.Width, 30), BgToolbar);
            sb.Draw(px, new Rectangle(r.X, r.Y + 29, r.Width, 1), AccentDim);
            font.DrawText(sb, title, new Vector2(r.X + 12, r.Y + 7), Accent);
        }
    }

    // ── Toolbar ───────────────────────────────────────────────────

    public static void DrawToolbar(SpriteBatch sb, Texture2D px, Rectangle r)
    {
        FillRect(sb, px, r, BgToolbar);
        sb.Draw(px, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), Border);
    }

    /// <summary>Draw a toolbar button. Returns true if clicked this frame.</summary>
    public static bool ToolButton(SpriteBatch sb, Texture2D px, SpriteFontBase font,
        int x, int y, int w, string label, bool active = false, string tooltip = null)
    {
        int h = 26;
        var r = new Rectangle(x, y, w, h);
        bool hovered = IsHovered(r);
        bool clicked = hovered && MouseClicked;

        Color bg = active ? new Color(96, 152, 232, 30) : (hovered ? new Color(255, 255, 255, 8) : Color.Transparent);
        Color fg = active ? Accent : (hovered ? TextPrimary : TextSecondary);

        if (bg != Color.Transparent)
            FillRect(sb, px, r, bg);
        if (active)
            sb.Draw(px, new Rectangle(r.X, r.Bottom - 2, r.Width, 2), Accent);

        float tw = font.MeasureString(label).X;
        font.DrawText(sb, label, new Vector2(x + (w - tw) / 2, y + 5), fg);

        if (hovered && tooltip != null)
            SetTooltip(tooltip);

        return clicked;
    }

    // ── Status Bar ────────────────────────────────────────────────

    public static void DrawStatusBar(SpriteBatch sb, Texture2D px, SpriteFontBase font,
        int screenW, int screenH, params string[] sections)
    {
        int h = 24;
        var r = new Rectangle(0, screenH - h, screenW, h);
        FillRect(sb, px, r, BgToolbar);
        sb.Draw(px, new Rectangle(0, screenH - h, screenW, 1), new Color(96, 152, 232, 50));

        float x = 10;
        for (int i = 0; i < sections.Length; i++)
        {
            if (i > 0)
            {
                sb.Draw(px, new Rectangle((int)x, screenH - h + 6, 1, h - 12), Border);
                x += 14;
            }
            Color secCol = i == 0 ? TextSecondary : TextDim;
            font.DrawText(sb, sections[i], new Vector2(x, screenH - h + 5), secCol);
            x += font.MeasureString(sections[i]).X + 14;
        }
    }

    // ── List Item ─────────────────────────────────────────────────

    /// <summary>Draw a list row. Returns true if clicked.</summary>
    public static bool ListItem(SpriteBatch sb, Texture2D px, SpriteFontBase font,
        Rectangle r, string text, bool selected, bool enabled = true, string badge = null, Color? badgeColor = null)
    {
        bool hovered = IsHovered(r);
        bool clicked = hovered && MouseClicked && enabled;

        if (selected)
            FillRect(sb, px, r, Selected);
        else if (hovered && enabled)
            FillRect(sb, px, r, Hover);

        if (selected)
            sb.Draw(px, new Rectangle(r.X, r.Y, 3, r.Height), Accent);

        Color fg = selected ? TextPrimary : (enabled ? TextSecondary : TextDim);
        font.DrawText(sb, text, new Vector2(r.X + 10, r.Y + (r.Height - 14) / 2), fg);

        if (badge != null)
        {
            Color bc = badgeColor ?? Accent;
            float bw = font.MeasureString(badge).X + 10;
            var br = new Rectangle(r.Right - (int)bw - 8, r.Y + (r.Height - 18) / 2, (int)bw, 18);
            FillRect(sb, px, br, new Color(bc.R, bc.G, bc.B, (byte)30));
            DrawBorder(sb, px, br, bc * 0.5f);
            font.DrawText(sb, badge, new Vector2(br.X + 5, br.Y + 2), bc);
        }

        return clicked;
    }

    // ── Property Row ──────────────────────────────────────────────

    public static void PropertyRow(SpriteBatch sb, Texture2D px, SpriteFontBase font,
        int x, int y, int w, string label, string value, bool highlight = false)
    {
        if (highlight)
            FillRect(sb, px, new Rectangle(x, y, w, 18), Selected);

        font.DrawText(sb, label, new Vector2(x + 6, y + 2), TextSecondary);
        float valX = x + w * 0.45f;
        font.DrawText(sb, value, new Vector2(valX, y + 2), TextPrimary);
    }

    // ── Toggle / Switch ───────────────────────────────────────────

    public static bool Toggle(SpriteBatch sb, Texture2D px,
        int x, int y, bool value, string label = null, SpriteFontBase font = null)
    {
        int sw = 36, sh = 18;
        var r = new Rectangle(x, y, sw, sh);
        bool clicked = IsHovered(r) && MouseClicked;

        Color track = value ? Accent : new Color(60, 60, 75);
        FillRect(sb, px, r, track);
        DrawBorder(sb, px, r, value ? AccentHover : BorderLight);

        int knobX = value ? r.Right - 16 : r.X + 2;
        FillRect(sb, px, new Rectangle(knobX, y + 2, 14, 14), Color.White);

        if (label != null && font != null)
            font.DrawText(sb, label, new Vector2(x + sw + 8, y + 2), TextPrimary);

        return clicked;
    }

    // ── Slider ────────────────────────────────────────────────────

    public static float Slider(SpriteBatch sb, Texture2D px, SpriteFontBase font,
        int x, int y, int w, float value, string label = null, string format = "F2")
    {
        int h = 16;
        int trackY = y + 4;
        int trackH = 8;

        if (label != null && font != null)
        {
            font.DrawText(sb, label, new Vector2(x, y - 14), TextSecondary);
            font.DrawText(sb, value.ToString(format), new Vector2(x + w - 40, y - 14), TextPrimary);
        }

        // Track
        FillRect(sb, px, new Rectangle(x, trackY, w, trackH), BgInput);
        DrawBorder(sb, px, new Rectangle(x, trackY, w, trackH), Border);

        // Fill
        int fillW = (int)(w * Math.Clamp(value, 0, 1));
        FillRect(sb, px, new Rectangle(x + 1, trackY + 1, fillW - 2, trackH - 2), Accent);

        // Handle
        int handleX = x + fillW - 5;
        FillRect(sb, px, new Rectangle(handleX, y + 1, 10, h), Color.White);

        // Drag interaction
        var trackRect = new Rectangle(x, y, w, h);
        if (IsHovered(trackRect) && MouseDown)
        {
            float newVal = Math.Clamp((_mouse.X - x) / (float)w, 0, 1);
            return newVal;
        }
        return value;
    }

    // ── Tabbar ────────────────────────────────────────────────────

    public static int TabBar(SpriteBatch sb, Texture2D px, SpriteFontBase font,
        int x, int y, int w, string[] tabs, int selected)
    {
        int tabW = w / Math.Max(1, tabs.Length);
        int result = selected;

        for (int i = 0; i < tabs.Length; i++)
        {
            var r = new Rectangle(x + i * tabW, y, tabW, 28);
            bool isSel = (i == selected);
            bool hovered = IsHovered(r);

            FillRect(sb, px, r, isSel ? BgPanel : (hovered ? new Color(40, 40, 56) : BgToolbar));

            if (isSel)
                sb.Draw(px, new Rectangle(r.X, r.Bottom - 2, r.Width, 2), Accent);

            Color fg = isSel ? TextPrimary : TextSecondary;
            float tw = font.MeasureString(tabs[i]).X;
            font.DrawText(sb, tabs[i], new Vector2(r.X + (r.Width - tw) / 2, r.Y + 7), fg);

            if (hovered && MouseClicked) result = i;
        }

        sb.Draw(px, new Rectangle(x, y + 27, w, 1), Border);
        return result;
    }

    // ── Checkerboard (for transparent bg preview) ─────────────────

    public static void DrawCheckerboard(SpriteBatch sb, Texture2D px, Rectangle area, Vector2 offset, float zoom)
    {
        int size = Math.Max(4, (int)(16 * zoom));
        int startX = (int)(offset.X % (size * 2));
        int startY = (int)(offset.Y % (size * 2));

        Color c1 = new(40, 40, 55);
        Color c2 = new(48, 48, 63);

        for (int gx = area.X + startX - size * 2; gx < area.Right; gx += size)
        {
            for (int gy = area.Y + startY - size * 2; gy < area.Bottom; gy += size)
            {
                int cx = (gx - area.X - startX) / size;
                int cy = (gy - area.Y - startY) / size;
                if (gx + size < area.X || gy + size < area.Y) continue;
                Color c = ((cx + cy) % 2 == 0) ? c1 : c2;
                sb.Draw(px, new Rectangle(
                    Math.Max(gx, area.X), Math.Max(gy, area.Y),
                    Math.Min(size, area.Right - Math.Max(gx, area.X)),
                    Math.Min(size, area.Bottom - Math.Max(gy, area.Y))), c);
            }
        }
    }

    // ── Crosshair (origin indicator) ──────────────────────────────

    public static void DrawCrosshair(SpriteBatch sb, Texture2D px, float x, float y, int size, Color c)
    {
        sb.Draw(px, new Rectangle((int)x - size, (int)y, size * 2 + 1, 1), c * 0.6f);
        sb.Draw(px, new Rectangle((int)x, (int)y - size, 1, size * 2 + 1), c * 0.6f);
    }

    // ── Selection Box ─────────────────────────────────────────────

    public static void DrawSelectionBox(SpriteBatch sb, Texture2D px, Rectangle r, Color? color = null)
    {
        var c = color ?? Accent;
        // Outer glow
        DrawBorder(sb, px, new Rectangle(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), c * 0.25f);
        // Main border
        DrawBorder(sb, px, r, c);
        // Corner marks (L-shaped, unobtrusive)
        int m = 8, t = 2;
        sb.Draw(px, new Rectangle(r.X - 1, r.Y - 1, m, t), c);
        sb.Draw(px, new Rectangle(r.X - 1, r.Y - 1, t, m), c);
        sb.Draw(px, new Rectangle(r.Right - m + 1, r.Y - 1, m, t), c);
        sb.Draw(px, new Rectangle(r.Right - t, r.Y - 1, t, m), c);
        sb.Draw(px, new Rectangle(r.X - 1, r.Bottom - t + 1, m, t), c);
        sb.Draw(px, new Rectangle(r.X - 1, r.Bottom - m + 1, t, m), c);
        sb.Draw(px, new Rectangle(r.Right - m + 1, r.Bottom - t + 1, m, t), c);
        sb.Draw(px, new Rectangle(r.Right - t, r.Bottom - m + 1, t, m), c);
    }

    // ── Drag Handle ───────────────────────────────────────────────

    /// <summary>Returns new position if dragged, same position otherwise.</summary>
    public static Vector2 DragHandle(SpriteBatch sb, Texture2D px,
        Vector2 worldPos, Vector2 camOffset, float zoom, int id, bool selected)
    {
        float sx = camOffset.X + worldPos.X * zoom;
        float sy = camOffset.Y + worldPos.Y * zoom;
        int size = selected ? 10 : 7;
        var r = new Rectangle((int)sx - size / 2, (int)sy - size / 2, size, size);

        Color c = selected ? Gold : Accent;
        if (IsHovered(r)) c = AccentHover;
        FillRect(sb, px, r, c);
        if (selected) DrawBorder(sb, px, r, Color.White);

        if (selected && MouseDown && IsHovered(new Rectangle(r.X - 20, r.Y - 20, r.Width + 40, r.Height + 40)))
        {
            var delta = MouseDelta;
            worldPos += delta / zoom;
        }

        return worldPos;
    }

    // ── Scrollbar ─────────────────────────────────────────────────

    public static int DrawScrollbar(SpriteBatch sb, Texture2D px,
        int x, int y, int height, int contentHeight, int scroll, int viewHeight)
    {
        if (contentHeight <= viewHeight) return 0;

        int trackW = 8;
        FillRect(sb, px, new Rectangle(x, y, trackW, height), BgInput);

        float ratio = viewHeight / (float)contentHeight;
        int thumbH = Math.Max(20, (int)(height * ratio));
        float scrollRatio = scroll / (float)(contentHeight - viewHeight);
        int thumbY = y + (int)((height - thumbH) * scrollRatio);

        bool hovered = IsHovered(new Rectangle(x, thumbY, trackW, thumbH));
        FillRect(sb, px, new Rectangle(x, thumbY, trackW, thumbH), hovered ? AccentHover : Accent);

        // Scroll wheel on track area
        var trackRect = new Rectangle(x - 20, y, trackW + 40, height);
        if (IsHovered(trackRect))
        {
            int wheelDelta = _prevMouse.ScrollWheelValue - _mouse.ScrollWheelValue;
            if (wheelDelta != 0)
                return Math.Clamp(scroll + wheelDelta / 4, 0, contentHeight - viewHeight);
        }

        return scroll;
    }

    // ── Timeline scrubber ─────────────────────────────────────────

    public static void DrawTimeline(SpriteBatch sb, Texture2D px, SpriteFontBase font,
        int x, int y, int w, int frameCount, int currentFrame, float frameRate)
    {
        int h = 32;
        FillRect(sb, px, new Rectangle(x, y, w, h), BgInput);
        DrawBorder(sb, px, new Rectangle(x, y, w, h), Border);

        if (frameCount <= 0) return;

        // Frame ticks
        float tickSpacing = w / (float)frameCount;
        for (int i = 0; i <= frameCount; i++)
        {
            int tx = x + (int)(i * tickSpacing);
            bool isMajor = (i % 10 == 0);
            int tickH = isMajor ? 12 : 6;
            sb.Draw(px, new Rectangle(tx, y + h - tickH, 1, tickH), isMajor ? BorderLight : Border);
            if (isMajor && font != null)
                font.DrawText(sb, i.ToString(), new Vector2(tx + 2, y + 2), TextDim);
        }

        // Current frame indicator
        int curX = x + (int)(currentFrame * tickSpacing);
        sb.Draw(px, new Rectangle(curX, y, 2, h), Accent);
        FillRect(sb, px, new Rectangle(curX - 4, y, 10, 8), Accent);

        // Time label
        float seconds = currentFrame / Math.Max(1, frameRate);
        string timeStr = $"{currentFrame}f / {seconds:F2}s";
        font?.DrawText(sb, timeStr, new Vector2(x + w - font.MeasureString(timeStr).X - 6, y + h - 16), TextSecondary);
    }

    // ── Grid (for stage/chart editors) ────────────────────────────

    public static void DrawGrid(SpriteBatch sb, Texture2D px,
        Rectangle area, Vector2 camOffset, float zoom, int gridSize = 100)
    {
        int scaledGrid = Math.Max(4, (int)(gridSize * zoom));

        int ox = (int)(camOffset.X) % scaledGrid;
        int oy = (int)(camOffset.Y) % scaledGrid;
        if (ox < 0) ox += scaledGrid;
        if (oy < 0) oy += scaledGrid;

        for (int gx = area.X + ox; gx < area.Right; gx += scaledGrid)
            sb.Draw(px, new Rectangle(gx, area.Y, 1, area.Height), new Color(50, 50, 65, 80));
        for (int gy = area.Y + oy; gy < area.Bottom; gy += scaledGrid)
            sb.Draw(px, new Rectangle(area.X, gy, area.Width, 1), new Color(50, 50, 65, 80));
    }

    // ── Notification Toast ────────────────────────────────────────

    private static string _toast;
    private static float _toastTimer;

    public static void ShowToast(string message, float duration = 2f)
    {
        _toast = message;
        _toastTimer = duration;
    }

    public static void UpdateToast(float dt) { if (_toastTimer > 0) _toastTimer -= dt; }

    public static void DrawToast(SpriteBatch sb, Texture2D px, SpriteFontBase font, int screenW, int screenH)
    {
        if (_toastTimer <= 0 || _toast == null || font == null) return;
        float alpha = Math.Min(1f, _toastTimer);
        float tw = font.MeasureString(_toast).X;
        int pw = (int)tw + 28;
        int px2 = (screenW - pw) / 2;
        int py = screenH - 64;
        FillRect(sb, px, new Rectangle(px2, py, pw, 30), new Color(32, 32, 44, (int)(230 * alpha)));
        DrawBorder(sb, px, new Rectangle(px2, py, pw, 30), new Color(96, 152, 232, (int)(100 * alpha)));
        sb.Draw(px, new Rectangle(px2, py + 29, pw, 1), new Color(96, 152, 232, (int)(180 * alpha)));
        font.DrawText(sb, _toast, new Vector2(px2 + 14, py + 7), TextPrimary * alpha);
    }

    // ── Icon Button (small square, for list actions like eye toggle) ──
    public static bool IconButton(SpriteBatch sb, Texture2D px, SpriteFontBase font,
        int x, int y, int size, string icon, bool active = false, Color? activeColor = null)
    {
        var r = new Rectangle(x, y, size, size);
        bool hovered = IsHovered(r);
        bool clicked = hovered && MouseClicked;
        Color bg = active ? (activeColor ?? Accent) : (hovered ? new Color(50, 50, 65) : Color.Transparent);
        Color fg = active ? Color.White : (hovered ? TextPrimary : TextDim);
        if (bg != Color.Transparent) FillRect(sb, px, r, bg);
        if (active) DrawBorder(sb, px, r, AccentHover);
        float iw = font.MeasureString(icon).X;
        float ih = font.MeasureString(icon).Y;
        font.DrawText(sb, icon, new Vector2(x + (size - iw) / 2, y + (size - ih) / 2), fg);
        return clicked;
    }

    // ── Property Section Header ──
    public static void PropertyHeader(SpriteBatch sb, Texture2D px, SpriteFontBase font,
        int x, int y, int w, string label)
    {
        FillRect(sb, px, new Rectangle(x, y, w, 24), new Color(35, 35, 48));
        sb.Draw(px, new Rectangle(x, y + 23, w, 1), new Color(96, 152, 232, 40));
        font.DrawText(sb, label.ToUpper(), new Vector2(x + 10, y + 5), new Color(96, 152, 232, 200));
    }

    // ── Horizontal Ruler (for viewport top) ──
    public static void RulerH(SpriteBatch sb, Texture2D px, SpriteFontBase font,
        int x, int y, int w, float worldOffset, float zoom, int step = 100)
    {
        int h = 18;
        FillRect(sb, px, new Rectangle(x, y, w, h), BgToolbar);
        sb.Draw(px, new Rectangle(x, y + h - 1, w, 1), Border);
        // Ticks
        float screenStep = step * zoom;
        float start = worldOffset * zoom;
        float offset = start % screenStep;
        if (offset < 0) offset += screenStep;
        for (float tx = x + offset; tx < x + w; tx += screenStep)
        {
            int ix = (int)tx;
            int worldVal = (int)((ix - x - start) / zoom);
            bool major = (worldVal % (step * 5) == 0);
            int tickH = major ? 10 : 5;
            sb.Draw(px, new Rectangle(ix, y + h - tickH, 1, tickH), major ? TextSecondary : TextDim);
            if (major && font != null && worldVal % 200 == 0)
                font.DrawText(sb, worldVal.ToString(), new Vector2(ix + 2, y + 2), TextDim);
        }
    }

    // ── Vertical Ruler (for viewport left) ──
    public static void RulerV(SpriteBatch sb, Texture2D px, SpriteFontBase font,
        int x, int y, int h, float worldOffset, float zoom, int step = 100)
    {
        int w = 18;
        FillRect(sb, px, new Rectangle(x, y, w, h), BgToolbar);
        sb.Draw(px, new Rectangle(x + w - 1, y, 1, h), Border);
        float screenStep = step * zoom;
        float start = worldOffset * zoom;
        float offset = start % screenStep;
        if (offset < 0) offset += screenStep;
        for (float ty = y + offset; ty < y + h; ty += screenStep)
        {
            int iy = (int)ty;
            int worldVal = (int)((iy - y - start) / zoom);
            bool major = (worldVal % (step * 5) == 0);
            int tickW = major ? 10 : 5;
            sb.Draw(px, new Rectangle(x + w - tickW, iy, tickW, 1), major ? TextSecondary : TextDim);
            if (major && font != null && worldVal % 200 == 0)
                font.DrawText(sb, worldVal.ToString(), new Vector2(x + 2, iy + 2), TextDim);
        }
    }

    // ── World Coordinate Label (follows mouse or fixed position) ──
    public static void WorldLabel(SpriteBatch sb, Texture2D px, SpriteFontBase font,
        int x, int y, string text, Color? bg = null)
    {
        float tw = font.MeasureString(text).X;
        int pw = (int)tw + 8;
        int ph = 18;
        FillRect(sb, px, new Rectangle(x, y, pw, ph), bg ?? new Color(22, 22, 35, 200));
        DrawBorder(sb, px, new Rectangle(x, y, pw, ph), Border);
        font.DrawText(sb, text, new Vector2(x + 4, y + 2), TextPrimary);
    }

    // ── Tooltip (follows mouse) ──
    private static string _tooltip;
    private static float _tooltipTimer;
    public static void SetTooltip(string text) { _tooltip = text; _tooltipTimer = 0.3f; }
    public static void UpdateTooltip(float dt) { if (_tooltipTimer > 0) _tooltipTimer -= dt; }
    public static void DrawTooltip(SpriteBatch sb, Texture2D px, SpriteFontBase font)
    {
        if (_tooltipTimer <= 0 || string.IsNullOrEmpty(_tooltip) || font == null) return;
        float tw = font.MeasureString(_tooltip).X;
        int pw = (int)tw + 14;
        int ph = 22;
        int tx = _mouse.X + 16;
        int ty = _mouse.Y + 16;
        FillRect(sb, px, new Rectangle(tx, ty, pw, ph), new Color(32, 32, 44, 240));
        DrawBorder(sb, px, new Rectangle(tx, ty, pw, ph), new Color(96, 152, 232, 120));
        font.DrawText(sb, _tooltip, new Vector2(tx + 7, ty + 4), TextPrimary);
    }
}
