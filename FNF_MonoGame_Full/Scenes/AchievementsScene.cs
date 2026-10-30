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
/// Achievements menu (port-only feature) in the original game's style: tinted menuDesat background, the main menu's
/// "ACHiEVEMENTS" atlas word as title, a grid of the Psych achievement icons (images/achievements/*.png, locked ones use
/// lockedachievement.png / darkened), overall progress bar, and a details card (name in Alphabet bold, description,
/// per-achievement progress for counters like Roadkill Enthusiast). Controller (D-pad/stick, LB/RB page), keyboard
/// (arrows, PgUp/PgDn) and mouse (hover/click/wheel) navigation.
/// </summary>
public class AchievementsScene : Scene
{
    private sealed class Entry { public string Key; public Achievements.Achievement Data; public Texture2D Icon; public bool Pixel; }
    private List<Entry> _list;
    private int _sel;
    private float _scrollRow, _time, _selPop;
    private Texture2D _desat, _locked;
    private SpriteSheet _words;
    private bool _leaving;

    private const int COLS = 6, CELL = 150, GAP = 34, TOP = 120, ROWH = CELL + GAP;
    private static int GridLeft => (1280 - (COLS * CELL + (COLS - 1) * GAP)) / 2;

    public override void Load()
    {
        Game.Discord?.SetMenuState("Achievements Menu");
        _desat = Assets.LoadTexture("menus/main_menu/menuDesat.png");
        if (_desat == Assets.Pixel) _desat = null;
        _locked = Assets.LoadTexture("images/achievements/lockedachievement.png");
        if (_locked == Assets.Pixel) _locked = null;
        _words = SpriteSheet.Load(Game, "menus/main_menu/buttons_port");
        Achievements.LoadModAchievements();   // Achievements.reloadList(): mod achievements of the currently enabled mods
        // AchievementsMenuState.create: hidden achievements are listed only once unlocked; sorted by ID
        _list = Achievements.AllAchievements.Where(k => !k.Value.Hidden || Achievements.IsUnlocked(k.Key)).OrderBy(k => k.Value.ID).Select(k =>
        {
            var e = new Entry { Key = k.Key, Data = k.Value };
            if (!string.IsNullOrEmpty(k.Value.Mod)) { e.Icon = ModIcon(k.Value.Mod, k.Key, out e.Pixel); return e; }
            var t = Assets.LoadTexture($"images/achievements/{k.Key}.png");
            if (t == Assets.Pixel) { t = Assets.LoadTexture($"images/achievements/{k.Key}-pixel.png"); e.Pixel = t != Assets.Pixel; }
            e.Icon = t == Assets.Pixel ? null : t;
            return e;
        }).ToList();
        if (!Audio.MusicPlaying) Audio.PlayMusic("music/freakyMenu", true);
    }

    /// <summary>Mod achievement icon: Mods.currentModDirectory = achievement.mod; 'achievements/&lt;key&gt;-pixel' if it exists,
    /// else 'achievements/&lt;key&gt;' (mod images, then mods/images, then shared), else 'unknownMod'.</summary>
    private Texture2D ModIcon(string mod, string key, out bool pixel)
    {
        pixel = false;
        string modsBase = PsychMods.ModsBasePath;
        string Find(string k)
        {
            foreach (var root in new[] { string.IsNullOrEmpty(modsBase) ? null : System.IO.Path.Combine(modsBase, mod), modsBase })
            {
                if (string.IsNullOrEmpty(root)) continue;
                string f = System.IO.Path.Combine(root, "images", k + ".png");
                if (global::FNF_MonoGame.Engine.FsCI.FileExists(f)) return f;
            }
            string c = Assets.ResolvePath("images/" + k + ".png");
            return c != null && global::FNF_MonoGame.Engine.FsCI.FileExists(c) ? c : null;
        }
        string path = Find($"achievements/{key}-pixel");
        if (path != null) pixel = true; else path = Find($"achievements/{key}") ?? Find("unknownMod");
        if (path == null) return null;
        try { using var st = global::FNF_MonoGame.Engine.FsCI.OpenRead(path); var t = Texture2D.FromStream(Game.GraphicsDevice, st); _modIcons.Add(t); return t; }
        catch { return null; }
    }
    private readonly List<Texture2D> _modIcons = new();

    public override void Unload() { _words?.Dispose(); foreach (var t in _modIcons) t.Dispose(); }

    private int Rows => (_list.Count + COLS - 1) / COLS;

    public override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _time += dt;
        _selPop = Math.Max(0, _selPop - dt * 4f);
        int row = _sel / COLS;
        float target = Math.Clamp(row - 1, 0, Math.Max(0, Rows - 2));
        _scrollRow = MenuUI.Lerp(_scrollRow, target, 0.14f, dt);
        if (_leaving) return;
        if (Input.BackPressed)
        {
            _leaving = true;
            Audio.PlaySound("cancelMenu");
            Game.Scenes.ChangeScene(new MainMenuScene());
            return;
        }
        if (_list.Count == 0) return;
        int n = _list.Count, s = _sel;
        if (Input.LeftPressed) s = (s - 1 + n) % n;
        if (Input.RightPressed) s = (s + 1) % n;
        if (Input.UpPressed) s = s - COLS >= 0 ? s - COLS : Math.Min(n - 1, (Rows - 1) * COLS + s % COLS);
        if (Input.DownPressed) s = s + COLS < n ? s + COLS : s % COLS;
        if (Input.IsGamePadPressed(Buttons.LeftShoulder) || Input.IsPressed(Keys.PageUp)) s = Math.Max(0, s - COLS * 2);
        if (Input.IsGamePadPressed(Buttons.RightShoulder) || Input.IsPressed(Keys.PageDown)) s = Math.Min(n - 1, s + COLS * 2);
        if (Input.ScrollDelta != 0) s = Math.Clamp(s + (Input.ScrollDelta > 0 ? -COLS : COLS), 0, n - 1);
        int hover = HitCell();
        if (hover >= 0 && (Input.MouseLeftPressed || Input.MouseMoved)) s = hover;
        if (s != _sel) { _sel = s; _selPop = 1f; Audio.PlaySound("scrollMenu", 0.4f); }
    }

    private Rectangle CellRect(int i)
    {
        int r = i / COLS, c = i % COLS;
        return new Rectangle(GridLeft + c * (CELL + GAP), (int)(TOP + (r - _scrollRow) * ROWH), CELL, CELL);
    }

    private int HitCell()
    {
        var mp = MouseVirtual;
        if (mp.Y < 112 || mp.Y > 500) return -1;
        for (int i = 0; i < _list.Count; i++) if (CellRect(i).Contains(new Point((int)mp.X, (int)mp.Y))) return i;
        return -1;
    }

    public override void Draw(SpriteBatch sb)
    {
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
        MenuUI.DrawBackground(sb, _desat, new Color(0xF0, 0xC0, 0x50), _scrollRow * 60f);   // warm gold tint (trophy feel)
        sb.End();

        var gd = Game.GraphicsDevice;
        var old = gd.ScissorRectangle;
        gd.ScissorRectangle = new Rectangle(0, 104, 1280, 408);
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied, rasterizerState: _scissor);
        for (int i = 0; i < _list.Count; i++) DrawCell(sb, i);
        sb.End();
        gd.ScissorRectangle = old;

        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
        MenuUI.DrawAtlasWord(sb, _words, "achievements selected", 30, 10, 0.5f, _time);
        int unlocked = _list.Count(e => Achievements.IsUnlocked(e.Key));
        float pct = _list.Count == 0 ? 0 : unlocked / (float)_list.Count;
        MenuUI.Text(sb, 22, $"{unlocked} / {_list.Count} UNLOCKED ({(int)Math.Floor(pct * 100)}%)", 1250, 22, Color.White, 1f, 2, 1f);
        MenuUI.Bar(sb, new Rectangle(1250 - 360, 56, 360, 16), pct, new Color(255, 214, 64));
        if (Rows > 2) DrawScrollHint(sb);

        if (_list.Count > 0) DrawCard(sb, _list[_sel]);
        else MenuUI.Text(sb, 24, "No achievements.", 640, 320, Color.White, 1f, 2, 0.5f);

        var bar = new List<ButtonPrompts.Prompt> { new(ButtonPrompts.DPadAll, "ARROWS", "SELECT") };
        if (Rows > 2) { bar.Add(new(Buttons.LeftShoulder, "PGUP", "")); bar.Add(new(Buttons.RightShoulder, "PGDN", "PAGE")); }
        bar.Add(ButtonPrompts.Back("BACK"));
        ButtonPrompts.DrawBar(sb, Assets, Input, bar, 640, 674, 0.5f, 32);
        sb.End();
    }

    private static readonly RasterizerState _scissor = new RasterizerState { ScissorTestEnable = true, CullMode = CullMode.None };

    private void DrawScrollHint(SpriteBatch sb)
    {
        // small page dots on the right edge
        int rows = Rows;
        for (int r = 0; r < rows; r++)
        {
            bool on = r == _sel / COLS;
            MenuUI.Rect(sb, new Rectangle(1238, 150 + r * 22, on ? 14 : 10, on ? 14 : 10), on ? Color.White : new Color(255, 255, 255, 110));
        }
    }

    private void DrawCell(SpriteBatch sb, int i)
    {
        var e = _list[i];
        var r = CellRect(i);
        if (r.Bottom < 90 || r.Y > 520) return;
        bool sel = i == _sel, un = Achievements.IsUnlocked(e.Key);
        float scale = sel ? 1.08f + 0.06f * _selPop * _selPop : 1f;
        int w = (int)(CELL * scale), h = (int)(CELL * scale);
        var rr = new Rectangle(r.Center.X - w / 2, r.Center.Y - h / 2, w, h);
        // frame
        MenuUI.Rect(sb, new Rectangle(rr.X - 5 + 5, rr.Y - 5 + 7, rr.Width + 10, rr.Height + 10), new Color(Color.Black, 0.35f));
        MenuUI.Rect(sb, new Rectangle(rr.X - 5, rr.Y - 5, rr.Width + 10, rr.Height + 10), sel ? Color.White : (un ? new Color(255, 214, 64) : Color.Black));
        MenuUI.Rect(sb, rr, new Color(20, 16, 34));
        // locked: the real picture darkened with a lock badge (hidden ones stay fully locked)
        bool secret = !un && e.Data.Hidden;
        var tex = un ? (e.Icon ?? _locked) : secret ? (_locked ?? e.Icon) : (e.Icon ?? _locked);
        if (tex != null)
        {
            var tint = un || secret ? Color.White : (sel ? new Color(120, 112, 140) : new Color(78, 72, 96));
            sb.Draw(tex, rr, null, tint, 0f, Vector2.Zero, SpriteEffects.None, 0f);
            if (!un && !secret && _locked != null)
            {
                int ls = rr.Width * 2 / 5;
                MenuUI.Rect(sb, new Rectangle(rr.Right - ls - 4, rr.Y + 4, ls, ls), new Color(Color.Black, 0.6f));
                sb.Draw(_locked, new Rectangle(rr.Right - ls - 4, rr.Y + 4, ls, ls), Color.White);
            }
        }
        if (!un && e.Data.MaxScore > 0)
        {
            float p = Achievements.GetScore(e.Key) / e.Data.MaxScore;
            MenuUI.Bar(sb, new Rectangle(rr.X + 10, rr.Bottom - 18, rr.Width - 20, 8), p, new Color(255, 214, 64));
        }
        if (!sel) MenuUI.Rect(sb, rr, new Color(Color.Black, 0.18f));
    }

    private void DrawCard(SpriteBatch sb, Entry e)
    {
        bool un = Achievements.IsUnlocked(e.Key);
        bool secret = !un && e.Data.Hidden;
        var r = new Rectangle(60, 520, 1160, 120);
        MenuUI.Panel(sb, r, 1f, 4);
        var ir = new Rectangle(r.X + 16, r.Y + 14, 92, 92);
        var tex = un ? (e.Icon ?? _locked) : secret ? _locked : (e.Icon ?? _locked);
        MenuUI.Rect(sb, new Rectangle(ir.X - 3, ir.Y - 3, ir.Width + 6, ir.Height + 6), un ? new Color(255, 214, 64) : Color.Gray);
        if (tex != null) sb.Draw(tex, ir, un || secret ? Color.White : new Color(110, 104, 130));
        string name = secret ? "???" : e.Data.Name;
        MenuUI.Alphabet(sb, name, ir.Right + 24, r.Y + 10, 0.62f, 700, un ? Color.White : new Color(190, 190, 200));
        string desc = secret ? "This is a secret achievement. Keep playing to find it!" : e.Data.Description;
        int y = r.Y + 72;
        foreach (var l in MenuUI.Wrap(18, desc, 760).Take(2)) { MenuUI.Text(sb, 18, l, ir.Right + 26, y, Color.White, 1f, 1); y += 22; }
        // status
        var badge = new Rectangle(r.Right - 196, r.Y + 18, 176, 36);
        MenuUI.Rect(sb, badge, Color.Black);
        MenuUI.Rect(sb, new Rectangle(badge.X + 3, badge.Y + 3, badge.Width - 6, badge.Height - 6), un ? new Color(212, 160, 20) : new Color(70, 70, 84));
        MenuUI.Text(sb, 20, un ? "UNLOCKED" : "LOCKED", badge.Center.X, badge.Y + 8, Color.White, 1f, 2, 0.5f);
        if (e.Data.MaxScore > 0)
        {
            float sc = Math.Min(Achievements.GetScore(e.Key), e.Data.MaxScore);
            string fmt = e.Data.MaxDecimals > 0 ? "F" + e.Data.MaxDecimals : "F0";
            MenuUI.Bar(sb, new Rectangle(r.Right - 196, r.Y + 72, 176, 14), sc / e.Data.MaxScore, new Color(255, 214, 64));
            MenuUI.Text(sb, 16, $"{sc.ToString(fmt)} / {e.Data.MaxScore.ToString(fmt)}", r.Right - 108, r.Y + 92, Color.White, 1f, 1, 0.5f);
        }
    }
}
