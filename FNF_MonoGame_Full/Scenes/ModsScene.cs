using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Mods menu (port-only feature) in the original game's style: tinted menuDesat background (mod colour from pack.json),
/// the main menu's "MODS" atlas word as title, Alphabet bold mod names with icons (pack.png / _polymod_icon.png),
/// the original options checkbox atlas for the enabled state, and a details card with a large icon, author, version,
/// content summary and wrapped description. Full controller + keyboard + mouse support:
///   A/ENTER toggle, LB/RB or Q/E move in load order, X/TAB enable/disable all, LT/DELETE delete (Yes/No confirm), B/ESC back.
/// </summary>
public class ModsScene : Scene
{
    private List<ModInfo> _mods;
    private int _sel;
    private float _selLerp;
    private float _time;
    private Texture2D _desat, _defaultIcon;
    private SpriteSheet _words, _checkbox;
    private readonly Dictionary<ModInfo, Texture2D> _icons = new();
    private readonly Dictionary<ModInfo, float> _checkAnim = new();
    private Color _tint = MenuUI.DefaultTint;
    private bool _changed;
    private bool _leaving;
    private float _cardPop;
    // delete: Yes/No confirm card, then the folder is removed on a background task
    private bool _confirmDelete;
    private int _confirmSel;            // 0 = NO (default), 1 = YES
    private float _confirmT;
    private System.Threading.Tasks.Task<string> _deleteTask;
    private ModInfo _deleteMod;
    private string _notice; private float _noticeT;

    private const float ROW = 118f;

    public override void Load()
    {
        Game.Discord?.SetMenuState("Mods Menu");
        _mods = ModManager.Instance.AllMods;
        _desat = Assets.LoadTexture("menus/main_menu/menuDesat.png");
        if (_desat == Assets.Pixel) _desat = null;
        _defaultIcon = Assets.LoadTexture("images/icons/icon-bf.png");
        if (_defaultIcon == Assets.Pixel) _defaultIcon = null;
        _words = SpriteSheet.Load(Game, "menus/main_menu/buttons_port");
        _checkbox = SpriteSheet.Load(Game, "menus/options/checkbox");
        foreach (var m in _mods)
        {
            _icons[m] = m.IconPath != null ? MenuUI.LoadFileTexture(Game.GraphicsDevice, m.IconPath) : null;
            _checkAnim[m] = m.Enabled ? 10f : -1f;
        }
        if (!Audio.MusicPlaying) Audio.PlayMusic("music/freakyMenu", true);
    }

    public override void Unload()
    {
        foreach (var t in _icons.Values) t?.Dispose();
        _icons.Clear();
        _words?.Dispose();
        _checkbox?.Dispose();
    }

    private Color TargetTint()
    {
        if (_mods.Count == 0) return MenuUI.DefaultTint;
        var c = _mods[_sel].Color;
        return c != null ? new Color(c[0], c[1], c[2]) : MenuUI.DefaultTint;
    }

    public override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _time += dt;
        _selLerp = MenuUI.Lerp(_selLerp, _sel, 0.16f, dt);
        _cardPop = Math.Max(0, _cardPop - dt * 5f);
        var tt = TargetTint();
        float k = 1f - (float)Math.Pow(0.92, dt * 60);
        _tint = new Color((int)MathHelper.Lerp(_tint.R, tt.R, k), (int)MathHelper.Lerp(_tint.G, tt.G, k), (int)MathHelper.Lerp(_tint.B, tt.B, k));
        foreach (var m in _mods) if (_checkAnim.TryGetValue(m, out var ca) && ca >= 0) _checkAnim[m] = ca + dt;
        if (_noticeT > 0) _noticeT -= dt;
        if (_leaving) return;
        if (_deleteTask != null) { if (_deleteTask.IsCompleted) FinishDelete(); return; }
        if (_confirmDelete) { UpdateConfirm(dt); return; }

        if (Input.BackPressed)
        {
            _leaving = true;
            Audio.PlaySound("cancelMenu");
            Game.Scenes.ChangeScene(new MainMenuScene());
            return;
        }
        if (Input.IsGamePadPressed(Buttons.Y) || Input.IsPressed(Keys.B) || Input.IsPressed(Keys.F))
        {   // GameBanana mod browser (download + install mods in-game)
            _leaving = true;
            Audio.PlaySound("confirmMenu");
            Game.Scenes.ChangeScene(new ModBrowserScene(new ModsScene()));
            return;
        }
        if (_mods.Count == 0) return;

        int n = _mods.Count;
        bool moveUp = Input.IsGamePadPressed(Buttons.LeftShoulder) || Input.IsPressed(Keys.Q) || Input.IsPressed(Keys.PageUp);
        bool moveDown = Input.IsGamePadPressed(Buttons.RightShoulder) || Input.IsPressed(Keys.E) || Input.IsPressed(Keys.PageDown);
        if (moveUp || moveDown)
        {
            var m = _mods[_sel];
            if (ModManager.Instance.MoveMod(m, moveUp ? -1 : 1))
            {
                _sel = _mods.IndexOf(m); _changed = true;
                Audio.PlaySound("scrollMenu", 0.4f);
            }
            else Audio.PlaySound("cancelMenu", 0.4f);
            return;
        }

        int newSel = _sel;
        if (Input.UpPressed) newSel = (_sel - 1 + n) % n;
        if (Input.DownPressed) newSel = (_sel + 1) % n;
        if (Input.ScrollDelta != 0) newSel = Math.Clamp(_sel + (Input.ScrollDelta > 0 ? -1 : 1), 0, n - 1);
        int hover = HitRow();
        if (Input.MouseLeftPressed && hover >= 0 && hover != _sel) newSel = hover;
        if (newSel != _sel) { _sel = newSel; _cardPop = 1f; Audio.PlaySound("scrollMenu", 0.4f); return; }

        if (Input.IsGamePadPressed(Buttons.LeftTrigger) || Input.IsPressed(Keys.Delete))
        {   // delete the selected mod (asks first)
            _confirmDelete = true; _confirmSel = 0; _confirmT = 0f;
            Audio.PlaySound("scrollMenu");
            return;
        }
        if (Input.ConfirmPressed || (Input.MouseLeftPressed && hover == _sel))
        {
            var m = _mods[_sel];
            ModManager.Instance.ToggleMod(m);
            _checkAnim[m] = m.Enabled ? 0f : -1f;
            _changed = true;
            Audio.PlaySound(m.Enabled ? "confirmMenu" : "cancelMenu");
            Input.Rumble(0.2f, 0.08f);
        }
        else if (Input.IsGamePadPressed(Buttons.X) || Input.IsPressed(Keys.Tab))
        {
            bool all = _mods.Any(x => !x.Enabled);
            ModManager.Instance.SetAllEnabled(all);
            foreach (var m in _mods) _checkAnim[m] = all ? 0f : -1f;
            _changed = true;
            Audio.PlaySound(all ? "confirmMenu" : "cancelMenu");
        }
    }

    private static Rectangle ConfirmButton(int i) => new Rectangle(i == 1 ? 412 : 668, 438, 200, 64);   // YES left, NO right

    private void UpdateConfirm(float dt)
    {
        _confirmT += dt;
        if (_confirmT < 0.15f) return;   // the press that opened the card must not answer it
        if (Input.LeftPressed || Input.RightPressed) { _confirmSel = 1 - _confirmSel; Audio.PlaySound("scrollMenu", 0.4f); }
        var mp = MouseVirtual;
        for (int i = 0; i < 2; i++)
            if (ConfirmButton(i).Contains((int)mp.X, (int)mp.Y) && (Input.MouseMoved || Input.MouseLeftPressed)) _confirmSel = i;
        bool click = Input.MouseLeftPressed && (ConfirmButton(0).Contains((int)mp.X, (int)mp.Y) || ConfirmButton(1).Contains((int)mp.X, (int)mp.Y));
        bool yes = Input.IsPressed(Keys.Y), no = Input.IsPressed(Keys.N) || Input.BackPressed;
        if (Input.ConfirmPressed || click) { if (_confirmSel == 1) yes = true; else no = true; }
        if (no) { _confirmDelete = false; Audio.PlaySound("cancelMenu"); return; }
        if (!yes) return;
        _confirmDelete = false;
        _deleteMod = _mods[_sel];
        Audio.PlaySound("confirmMenu");
        Input.Rumble(0.3f, 0.12f);
        var m = _deleteMod;
        _deleteTask = System.Threading.Tasks.Task.Run(() => ModManager.Instance.DeleteMod(m));
    }

    private void FinishDelete()
    {
        string err = _deleteTask.Status == System.Threading.Tasks.TaskStatus.RanToCompletion ? _deleteTask.Result : _deleteTask.Exception?.InnerException?.Message ?? "error";
        var m = _deleteMod; _deleteTask = null; _deleteMod = null;
        if (err == null)
        {
            if (_icons.TryGetValue(m, out var t)) { t?.Dispose(); _icons.Remove(m); }
            _checkAnim.Remove(m);
            _sel = Math.Clamp(_sel, 0, Math.Max(0, _mods.Count - 1));
            _selLerp = Math.Min(_selLerp, Math.Max(0, _mods.Count - 1));
            _changed = true; _cardPop = 1f;
            _notice = "Deleted " + m.Name; _noticeT = 3f;
        }
        else { _notice = "Could not delete " + m.Name + ": " + err; _noticeT = 5f; Audio.PlaySound("cancelMenu"); }
    }

    private float RowY(int i) => 380f + (i - _selLerp) * ROW;

    private int HitRow()
    {
        var mp = MouseVirtual;
        if (mp.X < 30 || mp.X > 640 || mp.Y < 112 || mp.Y > 640) return -1;
        for (int i = 0; i < _mods.Count; i++)
        {
            float y = RowY(i);
            if (mp.Y >= y - ROW / 2 && mp.Y < y + ROW / 2) return i;
        }
        return -1;
    }

    public override void Draw(SpriteBatch sb)
    {
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
        MenuUI.DrawBackground(sb, _desat, _tint, _selLerp * 40f);

        if (_mods.Count == 0) DrawEmpty(sb);
        else
        {
            sb.End();
            // list clipped to the area between the title and the prompt bar (scenes draw into the 1280x720 target)
            var gd = Game.GraphicsDevice;
            var old = gd.ScissorRectangle;
            gd.ScissorRectangle = new Rectangle(0, 112, 660, 538);
            sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied, rasterizerState: _scissor);
            for (int i = 0; i < _mods.Count; i++) DrawRow(sb, i);
            sb.End();
            gd.ScissorRectangle = old;
            sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
            DrawCard(sb, _mods[_sel]);
        }

        // title: the main menu's MODS word
        MenuUI.DrawAtlasWord(sb, _words, "mods selected", 34, 10, 0.62f, _time);
        int on = _mods.Count(m => m.Enabled);
        if (_mods.Count > 0) MenuUI.Text(sb, 22, $"{on} / {_mods.Count} ENABLED", 1246, 34, Color.White, 1f, 2, 1f);
        if (_noticeT > 0) MenuUI.Text(sb, 16, _notice.Length > 70 ? _notice[..67] + "..." : _notice, 1246, 70, new Color(255, 230, 120), Math.Min(1f, _noticeT * 2f), 2, 1f);
        else if (_changed) MenuUI.Text(sb, 16, "Load order: top = highest priority. Some mods need a restart.", 1246, 70, new Color(255, 230, 120), 1f, 2, 1f);

        var bar = new List<ButtonPrompts.Prompt> { ButtonPrompts.UpDown("SELECT"), new ButtonPrompts.Prompt(Buttons.Y, "B", "GET MODS") };
        if (_mods.Count > 0)
        {
            bar.Add(ButtonPrompts.Confirm(_mods[_sel].Enabled ? "DISABLE" : "ENABLE"));
            bar.Add(new ButtonPrompts.Prompt(Buttons.LeftShoulder, "Q", "UP"));
            bar.Add(new ButtonPrompts.Prompt(Buttons.RightShoulder, "E", "DOWN"));
            bar.Add(new ButtonPrompts.Prompt(Buttons.X, "TAB", _mods.Any(x => !x.Enabled) ? "ENABLE ALL" : "DISABLE ALL"));
            bar.Add(new ButtonPrompts.Prompt(Buttons.LeftTrigger, "DEL", "DELETE"));
        }
        bar.Add(ButtonPrompts.Back("BACK"));
        if (_confirmDelete || _deleteTask != null) DrawConfirm(sb);
        if (_confirmDelete) bar = new List<ButtonPrompts.Prompt> { ButtonPrompts.LeftRight("CHOOSE"), ButtonPrompts.Confirm("OK"), ButtonPrompts.Back("CANCEL") };
        if (_deleteTask != null) bar = new List<ButtonPrompts.Prompt>();
        ButtonPrompts.DrawBar(sb, Assets, Input, bar, 640, 672, 0.5f, 32);
        sb.End();
    }

    private static readonly RasterizerState _scissor = new RasterizerState { ScissorTestEnable = true, CullMode = CullMode.None };

    private void DrawRow(SpriteBatch sb, int i)
    {
        var m = _mods[i];
        float y = RowY(i);
        if (y < 40 || y > 760) return;
        bool sel = i == _sel;
        float a = sel ? 1f : 0.6f;
        float x = 60 + (sel ? 20f : 0f);
        if (sel) MenuUI.Panel(sb, new Rectangle(36, (int)(y - ROW / 2 + 6), 610, (int)ROW - 12), 0.9f, 3);
        var icon = _icons[m] ?? _defaultIcon;
        if (icon != null)
        {
            var src = icon == _defaultIcon ? new Rectangle(0, 0, Math.Min(150, icon.Width), icon.Height) : icon.Bounds;
            float s = 92f / Math.Max(src.Width, src.Height);
            sb.Draw(icon, new Vector2(x + 46 - src.Width * s / 2, y - src.Height * s / 2), src, new Color(Color.White, a), 0f, Vector2.Zero, s, SpriteEffects.None, 0f);
        }
        MenuUI.Alphabet(sb, m.Name, x + 104, y - 34, 0.72f, 370, new Color(Color.White, a));
        string sub = $"by {m.Author}";
        MenuUI.Text(sb, 16, sub.Length > 44 ? sub[..41] + "..." : sub, x + 106, y + 24, new Color(220, 220, 230), a);
        DrawCheckbox(sb, m, 600, y, a);
    }

    /// <summary>Original options checkbox atlas: "unselected" / "selecting" (play once) / "selected" loop.</summary>
    private void DrawCheckbox(SpriteBatch sb, ModInfo m, float cx, float cy, float alpha)
    {
        float t = _checkAnim[m];
        List<SpriteFrame> frames; int idx;
        if (!m.Enabled || t < 0) { frames = _checkbox?.GetAnimation("unselected"); idx = 0; }
        else
        {
            var sel = _checkbox?.GetAnimation("selecting");
            int f = (int)(t * 24f);
            if (sel != null && f < sel.Count) { frames = sel; idx = f; }
            else { frames = _checkbox?.GetAnimation("selected"); idx = frames == null || frames.Count == 0 ? 0 : (int)(t * 24f) % frames.Count; }
        }
        if (frames == null || frames.Count == 0)
        {
            MenuUI.Rect(sb, new Rectangle((int)cx - 20, (int)cy - 20, 40, 40), new Color(Color.Black, alpha));
            MenuUI.Rect(sb, new Rectangle((int)cx - 16, (int)cy - 16, 32, 32), new Color(m.Enabled ? Color.LimeGreen : Color.DimGray, alpha));
            return;
        }
        var fr = frames[Math.Min(idx, frames.Count - 1)];
        float s = 0.42f;
        int fw = fr.FrameWidth > 0 ? fr.FrameWidth : fr.SourceRect.Width, fh = fr.FrameHeight > 0 ? fr.FrameHeight : fr.SourceRect.Height;
        sb.Draw(fr.Texture ?? _checkbox.Texture, new Vector2(cx - fw * s / 2 + fr.Offset.X * s, cy - fh * s / 2 + fr.Offset.Y * s), fr.SourceRect,
            new Color(Color.White, alpha), 0f, Vector2.Zero, s, SpriteEffects.None, 0f);
    }

    private void DrawCard(SpriteBatch sb, ModInfo m)
    {
        float pop = _cardPop * _cardPop;
        var r = new Rectangle(676, 112 + (int)(pop * 14), 572, 528);
        MenuUI.Panel(sb, r, 1f - pop * 0.3f, 4);
        var icon = _icons[m] ?? _defaultIcon;
        var ir = new Rectangle(r.X + 24, r.Y + 24, 200, 200);
        MenuUI.Rect(sb, new Rectangle(ir.X - 4, ir.Y - 4, ir.Width + 8, ir.Height + 8), Color.White);
        MenuUI.Rect(sb, ir, _tint * 0.85f);
        if (icon != null)
        {
            var src = icon == _defaultIcon ? new Rectangle(0, 0, Math.Min(150, icon.Width), icon.Height) : icon.Bounds;
            float s = Math.Min(ir.Width / (float)src.Width, ir.Height / (float)src.Height);
            float bob = (float)Math.Sin(_time * 2.4f) * 3f;
            sb.Draw(icon, new Vector2(ir.X + (ir.Width - src.Width * s) / 2, ir.Y + (ir.Height - src.Height * s) / 2 + bob), src, Color.White, 0f, Vector2.Zero, s, SpriteEffects.None, 0f);
        }

        int tx = ir.Right + 22, tw = r.Right - 20 - tx;
        int ty = ir.Y;
        foreach (var l in MenuUI.Wrap(26, m.Name, tw).Take(3)) { MenuUI.Text(sb, 26, l, tx, ty, Color.White, 1f, 2); ty += 30; }
        ty += 6;
        foreach (var l in MenuUI.Wrap(18, "by " + m.Author, tw).Take(2)) { MenuUI.Text(sb, 18, l, tx, ty, new Color(255, 230, 140)); ty += 22; }
        ty += 4;
        MenuUI.Text(sb, 16, $"v{m.Version}" + (m.ApiVersion != null ? $"  (API {m.ApiVersion})" : ""), tx, ty, new Color(210, 210, 220)); ty += 22;
        string fmt = m.Format switch { ModFormat.PsychEngine => "Psych Engine mod", ModFormat.FunkinPolymod => "V-Slice (Polymod) mod", _ => "Mod folder" };
        MenuUI.Text(sb, 16, fmt, tx, ty, new Color(210, 210, 220)); ty += 28;
        var badge = new Rectangle(tx, Math.Min(ty, ir.Bottom - 34), 150, 34);
        MenuUI.Rect(sb, badge, Color.Black);
        MenuUI.Rect(sb, new Rectangle(badge.X + 3, badge.Y + 3, badge.Width - 6, badge.Height - 6), m.Enabled ? new Color(40, 170, 60) : new Color(150, 40, 50));
        MenuUI.Text(sb, 20, m.Enabled ? "ENABLED" : "DISABLED", badge.Center.X, badge.Y + 7, Color.White, 1f, 2, 0.5f);
        MenuUI.Text(sb, 16, $"#{_mods.IndexOf(m) + 1} of {_mods.Count}", badge.Right + 12, badge.Y + 9, Color.White);

        var c = m.ContentCounts;
        int cy = ir.Bottom + 22;
        string[] lbl = { "SONGS", "CHARACTERS", "STAGES", "SCRIPTS" };
        int cw = (r.Width - 48) / 4;
        for (int i = 0; i < 4; i++)
        {
            var cell = new Rectangle(r.X + 24 + i * cw, cy, cw - 10, 58);
            MenuUI.Rect(sb, cell, new Color(255, 255, 255, 30));
            MenuUI.Text(sb, 26, c[i].ToString(), cell.Center.X, cell.Y + 4, Color.White, 1f, 2, 0.5f);
            MenuUI.Text(sb, 13, lbl[i], cell.Center.X, cell.Y + 36, new Color(200, 200, 215), 1f, 1, 0.5f);
        }
        int dy = cy + 74;
        var lines = MenuUI.Wrap(18, string.IsNullOrWhiteSpace(m.Description) ? "No description." : m.Description, r.Width - 48);
        foreach (var l in lines.Take(7)) { MenuUI.Text(sb, 18, l, r.X + 24, dy, Color.White, 1f, 1); dy += 23; }
    }

    /// <summary>Yes/No card over the menu (same panel + Alphabet + text look as the rest of the Mods menu).</summary>
    private void DrawConfirm(SpriteBatch sb)
    {
        var m = _deleteTask != null ? _deleteMod : _mods[_sel];
        MenuUI.Rect(sb, new Rectangle(0, 0, 1280, 720), new Color(0, 0, 0, 150));
        float pop = 1f - (float)Math.Pow(1f - Math.Min(1f, _confirmT / 0.18f), 3);
        if (_deleteTask != null) pop = 1f;
        var r = new Rectangle(240, 196 + (int)((1f - pop) * 30), 800, 330);
        MenuUI.Panel(sb, r, 0.97f, 4);
        string title = _deleteTask != null ? "DELETING..." : "DELETE MOD?";
        float tw = AlphabetFont.Bold?.MeasureWidth(title, 0.8f) ?? 400;
        MenuUI.Alphabet(sb, title, 640 - tw / 2, r.Y + 26, 0.8f, 740, Color.White);
        var lines = MenuUI.Wrap(22, m?.Name ?? "", 720).Take(2).ToList();
        int y = r.Y + 112;
        foreach (var l in lines) { MenuUI.Text(sb, 22, l, 640, y, new Color(255, 230, 140), 1f, 2, 0.5f); y += 28; }
        string folder = m?.ModPath != null ? System.IO.Path.GetFileName(m.ModPath.TrimEnd('/', '\\')) : "";
        MenuUI.Text(sb, 16, "The folder mods/" + folder + " will be removed from this device.", 640, y + 4, Color.White, 1f, 2, 0.5f);
        if (_deleteTask != null)
        {
            float t = (_time * 1.6f) % 1f;
            MenuUI.Bar(sb, new Rectangle(440, 452, 400, 18), t, new Color(255, 230, 120));
            return;
        }
        for (int i = 1; i >= 0; i--)
        {
            var b = ConfirmButton(i);
            bool sel = _confirmSel == i;
            if (sel) b.Inflate(6, 4);
            MenuUI.Rect(sb, b, Color.Black);
            MenuUI.Rect(sb, new Rectangle(b.X + 4, b.Y + 4, b.Width - 8, b.Height - 8), i == 1 ? (sel ? new Color(200, 50, 60) : new Color(110, 40, 50)) : (sel ? new Color(40, 170, 60) : new Color(40, 90, 50)));
            string lbl = i == 1 ? "YES" : "NO";
            float lw = AlphabetFont.Bold?.MeasureWidth(lbl, 0.6f) ?? 80;
            MenuUI.Alphabet(sb, lbl, b.Center.X - lw / 2, b.Y + 6, 0.6f, b.Width, sel ? Color.White : new Color(Color.White, 0.6f));
        }
    }

    private void DrawEmpty(SpriteBatch sb)
    {
        var r = new Rectangle(190, 150, 900, 440);
        MenuUI.Panel(sb, r);
        float w = AlphabetFont.Bold?.MeasureWidth("NO MODS FOUND", 0.9f) ?? 400;
        MenuUI.Alphabet(sb, "NO MODS FOUND", 640 - w / 2, r.Y + 36, 0.9f, 820, Color.White);
        if (_defaultIcon != null && _defaultIcon.Width >= 300)
        {
            float bob = (float)Math.Sin(_time * 3f) * 4f;
            sb.Draw(_defaultIcon, new Vector2(640 - 75, r.Y + 120 + bob), new Rectangle(150, 0, 150, Math.Min(150, _defaultIcon.Height)), Color.White);
        }
        var lines = new List<string> { "Put Psych Engine (pack.json) or V-Slice (_polymod_meta.json)", "mods into this folder:" };
        lines.AddRange(MenuUI.Wrap(16, ModManager.Instance.ModsRoot ?? "mods", r.Width - 60));
        int y = r.Y + 290;
        foreach (var l in lines) { MenuUI.Text(sb, 18, l, 640, y, Color.White, 1f, 2, 0.5f); y += 26; }
    }
}
