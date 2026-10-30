using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Controller / keyboard button prompts (port-only UI). Shows Xbox glyphs (images/ui/prompts/xbox_*.png) when the
/// last used input device is a controller, otherwise keyboard keycaps with the key name. Switches automatically.
/// Dev hook: FNF_DEV_PAD=1 forces controller glyphs, FNF_DEV_PAD=0 forces keyboard (screenshot checks).
/// </summary>
public static class ButtonPrompts
{
    public struct Prompt
    {
        public Buttons Pad;      // controller button (use DPadUp|DPadDown etc. via special keys below)
        public string Key;       // keyboard label ("ENTER", "ESC", "UPDOWN", "LEFTRIGHT", "ARROWS", ...)
        public string Text;      // action text
        public Prompt(Buttons pad, string key, string text) { Pad = pad; Key = key; Text = text; }
    }

    // Pseudo buttons for d-pad combos (not real Buttons flags combos to keep it simple)
    public const Buttons DPadUpDown = Buttons.DPadUp | Buttons.DPadDown;
    public const Buttons DPadLeftRight = Buttons.DPadLeft | Buttons.DPadRight;
    public const Buttons DPadAll = Buttons.DPadUp | Buttons.DPadDown | Buttons.DPadLeft | Buttons.DPadRight;

    private static readonly string DevPad = Environment.GetEnvironmentVariable("FNF_DEV_PAD");
    /// <summary>FNF_DEV_PAD=1/0 forced glyph mode (read lazily: UWP copies devhooks.txt into the environment late), else null.</summary>
    public static bool? DevPadForced => Environment.GetEnvironmentVariable("FNF_DEV_PAD") switch { "1" => true, "0" => false, _ => null };
    private static readonly Dictionary<string, Texture2D> _tex = new();

    public static bool UseController(InputManager input)
    {
        if (DevPad == "1") return true;
        if (DevPad == "0") return false;
        if (InputManager.IsXboxFamily) return true; // Xbox console: no keyboard -> always controller glyphs
#if WINDOWS_UAP
        // Windows UWP: controller glyphs unless a keyboard was used last (any pad input switches back)
        return input.LastDevice == InputManager.InputDevice.Controller || !input.KeyboardUsedOnce;
#else
        return input.LastDevice == InputManager.InputDevice.Controller;
#endif
    }

    private static Texture2D Tex(AssetManager assets, string name)
    {
        if (_tex.TryGetValue(name, out var t)) return t;
        t = assets.LoadTexture("images/ui/prompts/" + name + ".png");
        if (t == assets.Pixel) t = null;
        _tex[name] = t;
        return t;
    }

    /// <summary>Glyph file name (images/ui/prompts) in the current icon set: xbox_* or ps_* (ControllerIcons:
    /// Options > Button Icons / PlayStation pad detection). ps_ files use the Xbox suffixes (ps_a = Cross, ps_lb = L1,
    /// ps_menu = Options, ps_view = Create, ps_ls = L3 ...). Stick directions: *_ls_u/d/l/r, *_rs_u/d/l/r.</summary>
    public static string PadGlyph(Buttons b) => ControllerIcons.Prefix + PadGlyphSuffix(b);

    public static string PadGlyphSuffix(Buttons b) => b switch
    {
        Buttons.A => "a", Buttons.B => "b", Buttons.X => "x", Buttons.Y => "y",
        Buttons.LeftShoulder => "lb", Buttons.RightShoulder => "rb",
        Buttons.LeftTrigger => "lt", Buttons.RightTrigger => "rt",
        Buttons.Start => "menu", Buttons.Back => "view",
        Buttons.LeftStick => "ls", Buttons.RightStick => "rs",
        Buttons.DPadUp => "dpad_u", Buttons.DPadDown => "dpad_d",
        Buttons.DPadLeft => "dpad_l", Buttons.DPadRight => "dpad_r",
        DPadUpDown => "dpad_ud", DPadLeftRight => "dpad_lr", DPadAll => "dpad_all",
        Buttons.LeftThumbstickUp => "ls_u", Buttons.LeftThumbstickDown => "ls_d",
        Buttons.LeftThumbstickLeft => "ls_l", Buttons.LeftThumbstickRight => "ls_r",
        Buttons.RightThumbstickUp => "rs_u", Buttons.RightThumbstickDown => "rs_d",
        Buttons.RightThumbstickLeft => "rs_l", Buttons.RightThumbstickRight => "rs_r",
        _ => "dpad"
    };

    /// <summary>Draw one glyph (controller) or keycap(s) (keyboard) with its top-left at (x,y). Returns the width used.</summary>
    public static int DrawGlyph(SpriteBatch sb, AssetManager assets, bool pad, Buttons button, string key, int x, int y, int size, float alpha = 1f)
    {
        var col = new Color(Color.White, alpha);
        if (pad)
        {
            var t = Tex(assets, PadGlyph(button));
            if (t != null) { sb.Draw(t, new Rectangle(x, y, size, size), col); return size; }
            assets.DrawButtonPrompt(sb, button, x, y, size, alpha);
            return size;
        }
        switch (key)
        {
            case "UPDOWN": { int w = DrawArrowKey(sb, assets, 0, x, y, size, alpha); return w + 2 + DrawArrowKey(sb, assets, 2, x + w + 2, y, size, alpha); }
            case "LEFTRIGHT": { int w = DrawArrowKey(sb, assets, 3, x, y, size, alpha); return w + 2 + DrawArrowKey(sb, assets, 1, x + w + 2, y, size, alpha); }
            case "UP": return DrawArrowKey(sb, assets, 0, x, y, size, alpha);
            case "DOWN": return DrawArrowKey(sb, assets, 2, x, y, size, alpha);
            case "LEFT": return DrawArrowKey(sb, assets, 3, x, y, size, alpha);
            case "RIGHT": return DrawArrowKey(sb, assets, 1, x, y, size, alpha);
        }
        return DrawKeycap(sb, assets, key, x, y, size, alpha);
    }

    private static int DrawArrowKey(SpriteBatch sb, AssetManager assets, int dir, int x, int y, int size, float alpha)
    {
        int w = DrawKeycap(sb, assets, "", x, y, size, alpha);
        var arrow = Tex(assets, "arrow");
        if (arrow != null)
        {
            float s = size * 0.42f / arrow.Width;
            sb.Draw(arrow, new Vector2(x + w / 2f, y + size * 0.44f), null, new Color(Color.White, alpha),
                dir * MathHelper.PiOver2, new Vector2(arrow.Width / 2f, arrow.Height / 2f), s, SpriteEffects.None, 0f);
        }
        return w;
    }

    private static int DrawKeycap(SpriteBatch sb, AssetManager assets, string label, int x, int y, int size, float alpha)
    {
        var font = assets.GetFont(Math.Max(10, (int)(size * (label.Length <= 1 ? 0.55f : 0.42f))));
        float tw = (font != null && label.Length > 0) ? font.MeasureString(label).X : 0;
        int w = Math.Max(size, (int)tw + (int)(size * 0.55f));
        var cap = Tex(assets, "keycap");
        var col = new Color(Color.White, alpha);
        if (cap != null)
        {
            // 9-slice (source 64x64, 16px corners)
            int c = 16, d = Math.Max(4, size / 4);
            Rectangle[] src = { new(0, 0, c, c), new(c, 0, 64 - 2 * c, c), new(64 - c, 0, c, c),
                new(0, c, c, 64 - 2 * c), new(c, c, 64 - 2 * c, 64 - 2 * c), new(64 - c, c, c, 64 - 2 * c),
                new(0, 64 - c, c, c), new(c, 64 - c, 64 - 2 * c, c), new(64 - c, 64 - c, c, c) };
            int[] xs = { x, x + d, x + w - d }, ws = { d, w - 2 * d, d };
            int[] ys = { y, y + d, y + size - d }, hs = { d, size - 2 * d, d };
            for (int r = 0; r < 3; r++) for (int q = 0; q < 3; q++)
                sb.Draw(cap, new Rectangle(xs[q], ys[r], ws[q], hs[r]), src[r * 3 + q], col);
        }
        else sb.Draw(assets.Pixel, new Rectangle(x, y, w, size), new Color(230, 230, 235) * alpha);
        if (font != null && label.Length > 0)
        {
            var ts = font.MeasureString(label);
            font.DrawText(sb, label, new Vector2(x + (w - ts.X) / 2f, y + size * 0.44f - ts.Y / 2f), new Color(new Color(30, 30, 36), alpha));
        }
        return w;
    }

    /// <summary>Text with a black outline (FlxTextBorderStyle.OUTLINE look).</summary>
    public static void OutlinedText(SpriteBatch sb, FontStashSharp.SpriteFontBase font, string text, Vector2 pos, Color color, float alpha = 1f, int border = 2)
    {
        var blk = new Color(Color.Black, alpha);
        for (int dx = -border; dx <= border; dx += border)
            for (int dy = -border; dy <= border; dy += border)
                if (dx != 0 || dy != 0) font.DrawText(sb, text, pos + new Vector2(dx, dy), blk);
        font.DrawText(sb, text, pos, new Color(color, alpha));
    }

    /// <summary>Measure a prompt bar's width.</summary>
    public static int Measure(AssetManager assets, InputManager input, IList<Prompt> items, int size = 36)
    {
        bool pad = UseController(input);
        var font = assets.GetFont((int)(size * 0.62f));
        int w = 0;
        foreach (var it in items)
        {
            int gw = pad ? size : MeasureKey(assets, it.Key, size);
            w += gw + 8 + (int)font.MeasureString(it.Text).X + 26;
        }
        return Math.Max(0, w - 26);
    }

    private static int MeasureKey(AssetManager assets, string key, int size)
    {
        if (key == "UPDOWN" || key == "LEFTRIGHT") return size * 2 + 2;
        if (key == "UP" || key == "DOWN" || key == "LEFT" || key == "RIGHT") return size;
        var font = assets.GetFont(Math.Max(10, (int)(size * (key.Length <= 1 ? 0.55f : 0.42f))));
        return Math.Max(size, (int)font.MeasureString(key).X + (int)(size * 0.55f));
    }

    /// <summary>
    /// Draw a row of prompts. align: 0=left (x is left edge), 1=right (x is right edge), 0.5=centred.
    /// When <paramref name="strip"/> is set, a translucent black strip is drawn behind the row.
    /// </summary>
    public static void DrawBar(SpriteBatch sb, AssetManager assets, InputManager input, IList<Prompt> items,
        int x, int y, float align = 1f, int size = 36, float alpha = 1f, bool strip = true)
    {
        if (items == null || items.Count == 0) return;
        bool pad = UseController(input);
        var font = assets.GetFont((int)(size * 0.62f));
        int total = Measure(assets, input, items, size);
        int left = (int)(x - total * align);
        if (strip)
            sb.Draw(assets.Pixel, new Rectangle(left - 14, y - 6, total + 28, size + 12), new Color(Color.Black, 0.55f * alpha));
        int cx = left;
        foreach (var it in items)
        {
            int gw = DrawGlyph(sb, assets, pad, it.Pad, it.Key, cx, y, size, alpha);
            cx += gw + 8;
            var ts = font.MeasureString(it.Text);
            OutlinedText(sb, font, it.Text, new Vector2(cx, y + (size - ts.Y) / 2f + 1), Color.White, alpha);
            cx += (int)ts.X + 26;
        }
    }

    // Common prompts
    public static Prompt Confirm(string text = "SELECT") => new(Buttons.A, "ENTER", text);
    public static Prompt Back(string text = "BACK") => new(Buttons.B, "ESC", text);
    public static Prompt UpDown(string text = "MOVE") => new(DPadUpDown, "UPDOWN", text);
    public static Prompt LeftRight(string text = "CHANGE") => new(DPadLeftRight, "LEFTRIGHT", text);
}
