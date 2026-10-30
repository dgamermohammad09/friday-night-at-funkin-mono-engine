using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.PsychEngine;
using FNF_MonoGame.Gameplay;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// SKINS menu (Psych Online SkinsState, in the in-house FunkTheme look): two pages switched with LB/RB (Q/E).
///  - CHARACTER: every skin from PsychOnline.AvailableSkins (mod Psych characters + base Pico) and "None" (the song's own
///    character). The selected one dances big on the right (102 BPM, like the lobby); A equips it (saved to
///    psychsave.json, sent to the room when connected) and it replaces your character in your songs and rooms.
///    Left/Right previews the player (BF side) or opponent variant.
///  - NOTES: Arrows / Controller Buttons / the Psych note skins (Chip, Future, mod skins) with a live strum + note
///    preview (RGBPalette shader with your Note Colors); A equips it (= Options > Note Skin).
/// Controller, keyboard and mouse: Up/Down (wheel) move, A/Enter/click equip, B/Esc back.
/// </summary>
public class SkinsScene : Scene
{
    public override bool NativeResolution => true;
    private readonly Func<Scene> _back;
    private int _tab;                       // 0 character, 1 notes
    private List<string[]> _skins;          // [0] = null (None)
    private List<string> _styles;
    private int _sel, _noteSel;
    private float _t, _selT, _equipPop, _scroll;
    private bool _leaving;
    private string _toast; private float _toastT;

    // character preview
    private Character _ch; private string _chKey; private float _chDelay; private bool _chPsych, _chDual;
    private Vector4? _chBox;                 // drawn idle box (x, y, w, h) measured on the first (invisible) draw
    private bool _previewBf = true;
    private bool _previewPs;   // notes page: preview the PlayStation version of a controller style (Left/Right)
    private int _lastBeat = -1;
    private float _chEmote;

    // note preview
    private Effect _rgb; private bool _rgbTried;
    private readonly Dictionary<string, SpriteSheet> _sheets = new(StringComparer.OrdinalIgnoreCase);
    private float _noteT;

    private const float ListX = 40f, ListW = 470f, RowH = 54f, ListTop = 112f;
    private const float PrevX = 540f, PrevY = 100f, PrevW = 700f, PrevH = 512f;

    public SkinsScene(Func<Scene> back = null) { _back = back ?? (() => new OptionsScene()); }

    public override void Load()
    {
        Game.Discord?.SetMenuState("Skins");
        ClientPrefs.EnsureLoaded();
        _skins = new List<string[]> { null };
        _skins.AddRange(PsychOnline.AvailableSkins(refresh: true));
        _styles = NoteStyles.Available();
        var own = ClientPrefs.Data.CurrentSkin;
        _sel = Math.Max(0, _skins.FindIndex(s => Same(s, own)));
        _noteSel = Math.Max(0, _styles.IndexOf(NoteStyles.Current));
        _previewPs = ControllerIcons.UsePlayStation;
        if (int.TryParse(Environment.GetEnvironmentVariable("FNF_SKINS_TAB"), out var tab)) _tab = Math.Clamp(tab, 0, 1);   // dev
        if (!Audio.MusicPlaying) Audio.PlayMusic("music/freakyMenu", true);
        Console.WriteLine($"[SKINS] {_skins.Count - 1} character skins, note styles: {string.Join(", ", _styles)}");
    }

    public override void Unload()
    {
        _ch?.Dispose(); _ch = null;
        foreach (var s in _sheets.Values) s?.Dispose();
        _sheets.Clear();
    }

    private static bool Same(string[] a, string[] b)
    {
        bool an = a == null || a.Length < 4 || string.IsNullOrEmpty(a[0]), bn = b == null || b.Length < 4 || string.IsNullOrEmpty(b[0]);
        if (an || bn) return an && bn;
        return a[0] == b[0] && (a[3] ?? "") == (b[3] ?? "");
    }

    private int Count => _tab == 0 ? _skins.Count : _styles.Count;
    private int Sel { get => _tab == 0 ? _sel : _noteSel; set { if (_tab == 0) _sel = value; else _noteSel = value; } }

    // ------------------------------------------------------------------ update
    public override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _t += dt; _selT += dt; _noteT += dt;
        _equipPop = Math.Max(0f, _equipPop - dt * 3f);
        if (_toastT > 0) _toastT -= dt;
        if (_chEmote > 0) { _chEmote -= dt; if (_chEmote <= 0) _ch?.PlayAnimation("idle", true); }
        UpdatePreviewChar(dt);
        if (_leaving) return;

        if (Input.BackPressed) { _leaving = true; Audio.PlaySound("cancelMenu"); Game.Scenes.ChangeScene(_back()); return; }
        if (Input.IsGamePadPressed(Buttons.LeftShoulder) || Input.IsGamePadPressed(Buttons.RightShoulder) || Input.IsPressed(Keys.Q) || Input.IsPressed(Keys.E))
        { _tab = 1 - _tab; _selT = 0f; Audio.PlaySound("scrollMenu"); return; }
        var mp = MouseVirtual;
        for (int i = 0; i < 2; i++)
            if (Input.MouseLeftPressed && TabRect(i).Contains((int)mp.X, (int)mp.Y) && _tab != i) { _tab = i; _selT = 0f; Audio.PlaySound("scrollMenu"); return; }

        int n = Count, sel = Sel;
        if (Input.UpPressed) sel = (sel + n - 1) % n;
        if (Input.DownPressed) sel = (sel + 1) % n;
        if (Input.ScrollDelta != 0 && mp.X < ListX + ListW + 20) sel = Math.Clamp(sel + (Input.ScrollDelta > 0 ? -1 : 1), 0, n - 1);
        int hover = HitRow(mp);
        if (Input.MouseLeftPressed && hover >= 0 && hover != sel) sel = hover;
        if (sel != Sel) { Sel = sel; _selT = 0f; Audio.PlaySound("scrollMenu", 0.5f); return; }

        if (_tab == 0 && (Input.LeftPressed || Input.RightPressed)) { _previewBf = !_previewBf; _chKey = null; Audio.PlaySound("scrollMenu", 0.5f); }
        // notes page, controller styles: Left/Right previews the Xbox or the PlayStation version
        if (_tab == 1 && NoteStyles.IsButtons(_styles[_noteSel]) && (Input.LeftPressed || Input.RightPressed)) { _previewPs = !_previewPs; Audio.PlaySound("scrollMenu", 0.5f); }
        if (Input.ConfirmPressed || (Input.MouseLeftPressed && hover == Sel)) Equip();
    }

    private void Equip()
    {
        _equipPop = 1f;
        Audio.PlaySound("confirmMenu");
        Input.Rumble(0.2f, 0.08f);
        if (_tab == 0)
        {
            var s = _skins[_sel];
            PsychOnline.SetOwnSkin(s);
            ShowToast(s == null ? "No skin: you play as the song's character" : "Equipped " + PsychOnline.SkinLabel(s));
            if (_ch != null) { foreach (var a in new[] { "hey", "singUP" }) if (HasAnim(a)) { _ch.PlayAnimation(a, true); _chEmote = 0.8f; break; } }
        }
        else
        {
            string v = _styles[_noteSel];
            NoteStyles.Set(v);
            HighscoreManager.SavePreferences();
            ClientPrefs.Save();
            ShowToast("Note skin: " + v);
            _noteT = 0f;
        }
    }

    private void ShowToast(string s) { _toast = s; _toastT = 2.5f; }

    private Rectangle TabRect(int i) => new Rectangle((int)(ListX + i * 200f), 80, 186, 34);

    private float RowY(int i) => ListTop + 52f + i * RowH - _scroll;

    private int HitRow(Vector2 mp)
    {
        if (mp.X < ListX || mp.X > ListX + ListW || mp.Y < ListTop + 44f || mp.Y > FunkUI.H - FunkTheme.BottomH - 10f) return -1;
        for (int i = 0; i < Count; i++) { float y = RowY(i); if (mp.Y >= y && mp.Y < y + RowH - 6f) return i; }
        return -1;
    }

    // ------------------------------------------------------------------ character preview
    private bool HasAnim(string a) => _ch?.JsonData?.Animations?.Any(x => x.Name == a) ?? false;

    private void UpdatePreviewChar(float dt)
    {
        if (_tab != 0) return;
        var s = _skins[_sel];
        string name = s == null ? (_previewBf ? "bf" : "bf-opponent") : s[0] + (_previewBf ? s[2] : s[1]);
        string mod = s?[3] ?? "";
        if (s != null && !PsychOnline.CharacterExists(name, mod)) name = s[0];
        string key = name + "|" + mod + "|" + _previewBf;
        if (key != _chKey)
        {
            if (_chDelay <= 0f) _chDelay = 0.18f;   // don't load every character while scrolling fast
            _chDelay -= dt;
            if (_chDelay > 0f) return;
            _chDelay = 0f; _chKey = key;
            _ch?.Dispose(); _ch = null;
            bool prev = Character.PsychStageActive; string prevMod = PsychMods.CurrentModDirectory;
            try
            {
                PsychMods.CurrentModDirectory = mod;
                bool psych = PsychOnline.IsPsychCharacter(name, mod);
                Character.PsychStageActive = psych;
                var ch = new Character(name, 0, 0) { StageUsesPsychPositioning = psych };
                ch.LoadJsonData(Assets, name);
                ch.SetFlipForRole(_previewBf);
                ch.LoadSprites(Game);
                ch.PlayAnimation("idle", force: true);
                if (!psych) ch.ApplyStagePosition(new[] { 0f, 0f });
                _ch = ch; _chPsych = psych; _chBox = null;
                var anims = ch.JsonData?.Animations;
                _chDual = anims != null && anims.Any(a => a.Name == "danceLeft") && anims.Any(a => a.Name == "danceRight");
                Console.WriteLine($"[SKINS] preview {name} ({(string.IsNullOrEmpty(mod) ? "base" : mod)}) psych={psych}");
            }
            catch (Exception e) { Console.WriteLine("[SKINS] preview " + name + ": " + e.Message); _ch = null; }
            finally { Character.PsychStageActive = prev; PsychMods.CurrentModDirectory = prevMod; }
        }
        if (_ch == null) return;
        int beat = (int)Math.Floor(Audio.MusicPosition / (60000.0 / 102.0));
        if (beat != _lastBeat)
        {
            _lastBeat = beat;
            if (_chEmote <= 0f) { if (_chDual) _ch.Dance(beat); else if (beat % 2 == 0) _ch.PlayAnimation("idle", true); }
        }
        _ch.Update(dt);
    }

    // ------------------------------------------------------------------ drawing
    public override void Draw(SpriteBatch sb)
    {
        FunkUI.Begin(sb);
        FunkTheme.Backdrop(_t);
        FunkTheme.TopBar("SKINS", _t, _tab == 0 ? "CHARACTER" : "NOTES");
        DrawTabs();
        DrawList();
        if (_tab == 0) DrawCharPanel(sb); else DrawNotePanel(sb);
        if (_toastT > 0) FunkTheme.Toast(_toast, FunkUI.H - FunkTheme.BottomH - 58f, Math.Min(1f, _toastT * 3f), FunkTheme.Green);
        var prompts = new List<ButtonPrompts.Prompt> { ButtonPrompts.UpDown("SELECT"), ButtonPrompts.Confirm("EQUIP") };
        if (_tab == 0) prompts.Add(ButtonPrompts.LeftRight(_previewBf ? "OPPONENT VIEW" : "PLAYER VIEW"));
        else if (NoteStyles.IsButtons(_styles[_noteSel])) prompts.Add(ButtonPrompts.LeftRight(_previewPs ? "XBOX VERSION" : "PLAYSTATION VERSION"));
        prompts.Add(new ButtonPrompts.Prompt(Buttons.RightShoulder, "Q/E", _tab == 0 ? "NOTES" : "CHARACTER"));
        prompts.Add(ButtonPrompts.Back("BACK"));
        FunkTheme.BottomBar(Input, prompts);
        sb.End();
    }

    private void DrawTabs()
    {
        bool pad = ButtonPrompts.UseController(Input);
        string[] names = { "CHARACTER", "NOTES" };
        for (int i = 0; i < 2; i++)
        {
            var r = TabRect(i);
            bool on = _tab == i;
            FunkTheme.BandOutlined(r.X, r.Y, r.Width, r.Height, 10f, on ? FunkTheme.Yellow : FunkTheme.Navy, 3f, on ? 4f : 0f);
            FunkTheme.Txt(names[i], r.Center.X, r.Y + 8f, 18f, on ? FunkTheme.Ink : FunkTheme.Text2, 0.5f);
        }
        FunkUI.Glyph(pad, Buttons.LeftShoulder, "Q", TabRect(0).X - 6f, TabRect(0).Y - 14f, 22f);
        FunkUI.Glyph(pad, Buttons.RightShoulder, "E", TabRect(1).Right - 16f, TabRect(1).Y - 14f, 22f);
    }

    private string RowLabel(int i)
    {
        if (_tab == 1) return _styles[i];
        var s = _skins[i];
        return s == null ? "None (song's character)" : s[0] == "pico-playable" ? "Pico" : s[0];
    }

    private string RowSub(int i)
    {
        if (_tab == 1) return _styles[i] == NoteStyles.Arrows ? "The game's own arrows" : NoteStyles.IsButtons(_styles[i]) ? (ControllerIcons.UsePlayStation ? "PlayStation buttons" : "Xbox buttons") + (NoteStyles.ButtonStyle(_styles[i]) == ControllerNoteSkin.DefaultStyle ? " (default)" : "") : "Psych Engine note skin";
        var s = _skins[i];
        return s == null ? "Default" : string.IsNullOrEmpty(s[3]) ? "Base game" : "Mod: " + (PsychOnline.PackName(s[3]) ?? s[3]);
    }

    private bool RowEquipped(int i) => _tab == 0 ? Same(_skins[i], ClientPrefs.Data.CurrentSkin) : _styles[i] == NoteStyles.Current;

    private void DrawList()
    {
        int n = Count, sel = Sel;
        float viewTop = ListTop + 52f, viewBottom = FunkUI.H - FunkTheme.BottomH - 16f;
        float target = Math.Max(0f, Math.Min(sel * RowH - (viewBottom - viewTop) / 2f + RowH / 2f, n * RowH - (viewBottom - viewTop)));
        _scroll = MathHelper.Lerp(_scroll, Math.Max(0f, target), 0.2f);
        FunkTheme.Panel(ListX - 8f, ListTop + 40f, ListW + 16f, viewBottom - ListTop - 30f, FunkTheme.A(FunkTheme.NavyLo, 0.85f), 1f, 12f, 3f, 0f);
        float pop = MathF.Exp(-_selT * 9f);
        for (int i = 0; i < n; i++)
        {
            float y = RowY(i);
            if (y < viewTop - 4f || y + RowH - 6f > viewBottom + 4f) continue;
            bool focus = i == sel;
            float d = Math.Min(4, Math.Abs(i - sel));
            float rx = ListX + 6f + d * 4f + (focus ? -6f - 8f * pop : 0f), rw = ListW - 12f - d * 4f, rh = RowH - 8f;
            float s = focus ? 1f + 0.03f * pop + 0.04f * _equipPop : 1f;
            if (s != 1f) FunkUI.Push(rx, y + rh / 2f, 0f, s, s);
            FunkTheme.Panel(rx, y, rw, rh, focus ? FunkTheme.Yellow : FunkTheme.Navy, 1f, 10f, 3f, focus ? 6f : 4f);
            float lx = rx + 16f;
            if (_tab == 0)
            {
                var ic = IconFor(_skins[i]);
                if (ic != null) FunkUI.Quad(ic, rx + 30f, y + rh / 2f, rh * 1.15f, rh * 1.15f, 0f, Color.White, new Rectangle(0, 0, ic.Width / 2, ic.Height));
                lx += 40f;
            }
            float right = rx + rw - 12f;
            if (RowEquipped(i)) right -= FunkTheme.Stamp("EQUIPPED", right, y + rh / 2f - 11f, FunkTheme.Green, 1f) + 8f;
            FunkTheme.TxtFit(RowLabel(i), lx, y + 5f, 18f, focus ? FunkTheme.Ink : FunkTheme.Text, right - lx - 4f);
            FunkTheme.TxtFit(RowSub(i), lx, y + 26f, 14f, focus ? FunkTheme.A(FunkTheme.Ink, 0.75f) : FunkTheme.Text3, right - lx - 4f);
            if (s != 1f) FunkUI.Pop();
        }
        float view = viewBottom - viewTop, content = n * RowH;
        if (content > view + 1f)
        {
            float sx = ListX + ListW + 2f, frac = view / content;
            FunkTheme.Box(sx, viewTop, 4f, view, FunkTheme.A(FunkTheme.Text3, 0.35f));
            FunkTheme.Box(sx, viewTop + view * Math.Clamp(_scroll / content, 0f, 1f - frac), 4f, view * frac, FunkTheme.Yellow);
        }
    }

    private readonly Dictionary<string, Texture2D> _icons = new();
    private Texture2D IconFor(string[] s)
    {
        string c = s == null ? "bf" : s[0].ToLowerInvariant();
        string mod = s?[3] ?? "";
        string key = mod + "|" + c;
        if (_icons.TryGetValue(key, out var t)) return t;
        t = null;
        // health icon of the character JSON (Psych healthicon) from the mod, else base icons by name
        try
        {
            string bas = PsychMods.ModsBasePath, icon = null;
            if (s != null && !string.IsNullOrEmpty(mod) && !string.IsNullOrEmpty(bas))
            {
                string json = System.IO.Path.Combine(bas, mod, "characters", s[0] + ".json");
                if (FsCI.FileExists(json)) icon = (string)Newtonsoft.Json.Linq.JObject.Parse(FsCI.ReadAllText(json))["healthicon"];
                if (!string.IsNullOrEmpty(icon))
                    foreach (var p in new[] { "images/icons/icon-" + icon + ".png", "images/icons/" + icon + ".png" })
                    {
                        string f = System.IO.Path.Combine(bas, mod, p);
                        if (FsCI.FileExists(f)) { t = MenuUI.LoadFileTexture(Game.GraphicsDevice, f); break; }
                    }
                if (t == null && !string.IsNullOrEmpty(icon)) c = icon.ToLowerInvariant();
            }
        }
        catch { }
        if (t == null)
            foreach (var cand in new[] { c, c.Replace("-playable", ""), c.Split('-')[0], "face" })
            {
                var tx = Assets.LoadTexture("images/icons/icon-" + cand + ".png");
                if (tx != null && tx != Assets.Pixel) { t = tx; break; }
            }
        _icons[key] = t;
        return t;
    }

    private void DrawCharPanel(SpriteBatch sb)
    {
        FunkTheme.Panel(PrevX, PrevY, PrevW, PrevH, FunkTheme.Navy);
        // stage-ish floor + slanted side band (BF cyan / opponent purple)
        var side = _previewBf ? FunkTheme.Cyan : FunkTheme.Purple;
        FunkTheme.Band(PrevX + 20f, PrevY + PrevH - 120f, PrevW - 40f, 70f, 30f, FunkTheme.A(side, 0.25f));
        FunkTheme.Tab(_previewBf ? "PLAYER" : "OPPONENT", PrevX + 12f, PrevY + 12f, side, _t, 30f);
        var s = _skins[_sel];
        string title = s == null ? "NONE" : RowLabel(_sel).ToUpperInvariant();
        FunkTheme.Heading(title, PrevX + PrevW - 20f, PrevY + 14f, 0.42f, _t, 1f);
        FunkTheme.TxtFit(RowSub(_sel), PrevX + PrevW - 20f, PrevY + 54f, 15f, FunkTheme.Text2, PrevW - 200f, 1f);
        if (RowEquipped(_sel)) FunkTheme.Stamp("EQUIPPED", PrevX + PrevW - 20f, PrevY + 80f, FunkTheme.Green, 1f);
        FunkTheme.TxtFit(s == null ? "Each song keeps its own character." : "You play as this character in songs and rooms.", PrevX + 24f, PrevY + PrevH - 36f, 16f, FunkTheme.Text2, PrevW - 48f);
        if (_ch == null) { FunkTheme.Txt(_chKey == null ? "LOADING..." : "NO PREVIEW", PrevX + PrevW / 2f, PrevY + PrevH / 2f, 20f, FunkTheme.Text3, 0.5f); return; }
        // character scaled to fit the panel, feet on the floor band (box = the idle frame as drawn: JSON position /
        // offsets / scale included, measured once with an invisible draw)
        var pos = _ch.Position;
        sb.End();
        if (_chBox == null)
        {
            sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp, null, null, null, Matrix.CreateScale(FunkUI.K));
            _ch.Tint = Color.Transparent;
            _ch.Draw(sb, Assets, pos.X, pos.Y);
            sb.End();
            var sp = _ch._sprite;
            float bw = (sp?.GetFrameWidth() ?? 300) * Math.Abs(_ch.Scale), bh = (sp?.GetFrameHeight() ?? 300) * Math.Abs(_ch.Scale);
            _chBox = new Vector4(sp?.Position.X ?? pos.X, sp?.Position.Y ?? pos.Y, Math.Max(10f, bw), Math.Max(10f, bh));
        }
        var box = _chBox.Value;
        float w = box.Z, h = box.W;
        float k = Math.Min(1f, Math.Min((PrevW - 80f) / w, (PrevH - 170f) / h));
        float cx = PrevX + PrevW / 2f, feetY = PrevY + PrevH - 66f;
        float bop = 1f + 0.015f * FunkTheme.Bop + 0.05f * _equipPop;
        var m = Matrix.CreateTranslation(-(box.X + w / 2f), -(box.Y + h), 0f) * Matrix.CreateScale(k * bop) * Matrix.CreateTranslation(cx, feetY, 0f) * Matrix.CreateScale(FunkUI.K);
        var gd = Game.GraphicsDevice; var oldSc = gd.ScissorRectangle;
        gd.ScissorRectangle = new Rectangle((int)((PrevX + 4f) * FunkUI.K), (int)((PrevY + 4f) * FunkUI.K), (int)((PrevW - 8f) * FunkUI.K), (int)((PrevH - 8f) * FunkUI.K));
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp, null, _scissor, null, m);
        _ch.Tint = Color.White;
        _ch.Draw(sb, Assets, pos.X, pos.Y);
        sb.End();
        gd.ScissorRectangle = oldSc;
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp, null, null, null, Matrix.CreateScale(FunkUI.K));
    }

    private static readonly RasterizerState _scissor = new RasterizerState { ScissorTestEnable = true, CullMode = CullMode.None };

    // ------------------------------------------------------------------ note preview
    private SpriteSheet Sheet(string key)
    {
        if (_sheets.TryGetValue(key, out var s)) return s;
        s = null;
        try
        {
            string mod = PsychMods.ModFolders("images/" + key + ".png");
            if (mod != null) s = SpriteSheet.Load(Game, mod.Substring(0, mod.Length - 4), sparrowOnly: true);
            s ??= SpriteSheet.Load(Game, "images/psych/" + key, sparrowOnly: true);
        }
        catch (Exception e) { Console.WriteLine("[SKINS] note sheet " + key + ": " + e.Message); }
        _sheets[key] = s;
        return s;
    }

    private Effect Rgb
    {
        get
        {
            if (_rgb != null || _rgbTried) return _rgb;
            _rgbTried = true;
            try
            {
                string path = Assets.ResolvePath("shaders/RGBPalette.mgfx") ?? System.IO.Path.Combine(AssetManager.ContentRoot, "shaders", "RGBPalette.mgfx");
                if (FsCI.FileExists(path)) _rgb = new Effect(Game.GraphicsDevice, FsCI.ReadAllBytes(path));
            }
            catch (Exception e) { Console.WriteLine("[SKINS] RGBPalette: " + e.Message); }
            return _rgb;
        }
    }

    private static readonly string[] Dirs = { "LEFT", "DOWN", "UP", "RIGHT" };

    private static SpriteFrame First(SpriteSheet sh, string prefix)
        => sh?.Frames.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal) && kv.Key.Substring(prefix.Length).All(char.IsDigit))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value).FirstOrDefault();

    private static List<SpriteFrame> Frames(SpriteSheet sh, string prefix)
        => sh?.Frames.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal) && kv.Key.Length > prefix.Length && kv.Key.Substring(prefix.Length).All(char.IsDigit))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value).ToList();

    private void DrawFrame(SpriteBatch sb, SpriteSheet sh, SpriteFrame f, float cx, float cy, float scale, uint[] rgb)
    {
        if (f == null) return;
        var fx = rgb != null ? Rgb : null;
        if (fx != null)
        {
            Vector3 C(uint c) => new(((c >> 16) & 0xFF) / 255f, ((c >> 8) & 0xFF) / 255f, (c & 0xFF) / 255f);
            fx.Parameters["r"]?.SetValue(C(rgb[0])); fx.Parameters["g"]?.SetValue(C(rgb[1])); fx.Parameters["b"]?.SetValue(C(rgb[2]));
            fx.Parameters["mult"]?.SetValue(1f);
            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp, null, null, fx, Matrix.CreateScale(FunkUI.K));
        }
        float fw = f.FrameWidth > 0 ? f.FrameWidth : f.SourceRect.Width, fh = f.FrameHeight > 0 ? f.FrameHeight : f.SourceRect.Height;
        var pos = new Vector2(cx - fw * scale / 2f + f.Offset.X * scale, cy - fh * scale / 2f + f.Offset.Y * scale);
        if (!f.Rotated) sb.Draw(f.Texture ?? sh.Texture, pos, f.SourceRect, Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        else sb.Draw(f.Texture ?? sh.Texture, new Vector2(pos.X, pos.Y + f.SourceRect.Width * scale), f.SourceRect, Color.White, -MathF.PI / 2f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        if (fx != null)
        {
            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp, null, null, null, Matrix.CreateScale(FunkUI.K));
        }
    }

    private void DrawNotePanel(SpriteBatch sb)
    {
        FunkTheme.Panel(PrevX, PrevY, PrevW, PrevH, FunkTheme.Navy);
        FunkTheme.Tab("PREVIEW", PrevX + 12f, PrevY + 12f, FunkTheme.Yellow, _t, 30f);
        string v = _styles[_noteSel];
        FunkTheme.Heading(v.ToUpperInvariant(), PrevX + PrevW - 20f, PrevY + 14f, 0.42f, _t, 1f);
        if (RowEquipped(_noteSel)) FunkTheme.Stamp("EQUIPPED", PrevX + PrevW - 20f, PrevY + 58f, FunkTheme.Green, 1f);
        // a strumline (lane 1 pressed, lane 2 confirming in a loop) + notes scrolling up into it
        float lane0 = PrevX + PrevW / 2f - 1.5f * 118f, strumY = PrevY + 150f;
        float loop = _noteT % 2f;
        if (NoteStyles.IsButtons(v))
        {
            // idle / PRESSED (lane 1, button pushed in) / CONFIRM (lane 2, note hit) like the gameplay strums
            int size = 96, style = NoteStyles.ButtonStyle(v);
            for (int l = 0; l < 4; l++)
            {
                float cx = lane0 + l * 118f;
                bool press = l == 1 && loop < 0.4f, confirm = l == 2 && loop >= 1f && loop < 1.25f;
                float st = press ? loop : confirm ? loop - 1f : -1f;
                // hold trail on lane 3 (lane colour, black outline) like gameplay holds
                if (l == 3)
                {
                    float hy0 = strumY + 70f, hy1 = PrevY + PrevH - 120f, hw = size * 0.3f;
                    var lc = ControllerNoteSkin.LaneColor(l);
                    FunkUI.Fill(cx - hw / 2f - 3f, hy0, hw + 6f, hy1 - hy0, new Color(0, 0, 0, 200));
                    FunkUI.Fill(cx - hw / 2f, hy0, hw, hy1 - hy0, new Color(lc.R, lc.G, lc.B, (byte)217));
                }
                ControllerNoteSkin.DrawReceptor(sb, Assets, l, Input.NoteFaceButtons[l], new Vector2(cx, strumY), size, press, confirm, 1f, st, FunkUI.K, style, _previewPs);
                for (int k = 0; k < 2; k++)
                {
                    float ph = (k * 1.1f + l * 0.37f - _noteT * 0.6f) % 2.2f; if (ph < 0) ph += 2.2f; float ny = strumY + 40f + ph * 140f;
                    if (ny < strumY + 70f) continue;
                    if (ny > PrevY + PrevH - 110f) continue;
                    ControllerNoteSkin.DrawNote(sb, Assets, l, Input.NoteFaceButtons[l], new Vector2(cx, ny), size, 1f, 0f, FunkUI.K, style, _previewPs);
                }
            }
        }
        else
        {
            string key = NoteStyles.SheetKey(v) ?? "noteSkins/NOTE_assets";
            var sh = Sheet(key);
            if (sh == null) { FunkTheme.Txt("NOT INSTALLED", PrevX + PrevW / 2f, PrevY + PrevH / 2f, 20f, FunkTheme.Red, 0.5f); return; }
            ClientPrefs.EnsureLoaded();
            var rgbs = ClientPrefs.Data.ArrowRGB;
            const float sc = 0.7f;
            for (int l = 0; l < 4; l++)
            {
                float cx = lane0 + l * 118f;
                uint[] rgb = rgbs != null && rgbs.Length > l ? rgbs[l] : PsychNoteSkin.DefaultRgb(l);
                bool press = l == 1 && loop < 0.4f, confirm = l == 2 && loop >= 1f && loop < 1.25f;
                if (confirm) { var c = Frames(sh, Dirs[l].ToLowerInvariant() + " confirm"); if (c != null && c.Count > 0) DrawFrame(sb, sh, c[Math.Min(c.Count - 1, (int)((loop - 1f) * 24f))], cx, strumY, sc, rgb); }
                else if (press) { var p = Frames(sh, Dirs[l].ToLowerInvariant() + " press"); if (p != null && p.Count > 0) DrawFrame(sb, sh, p[Math.Min(p.Count - 1, (int)(loop * 24f))], cx, strumY, sc, rgb); }
                else DrawFrame(sb, sh, First(sh, "arrow" + Dirs[l]), cx, strumY, sc, null);
                for (int k = 0; k < 2; k++)
                {
                    float ph = (k * 1.1f + l * 0.37f - _noteT * 0.6f) % 2.2f; if (ph < 0) ph += 2.2f; float ny = strumY + 40f + ph * 140f;
                    if (ny < strumY + 70f) continue;
                    if (ny > PrevY + PrevH - 110f) continue;
                    DrawFrame(sb, sh, First(sh, PsychNoteSkin.ColArray[l]), cx, ny, sc, rgb);
                }
            }
        }
        FunkTheme.TxtFit(v == NoteStyles.Arrows ? "The original look (pixel songs keep their pixel arrows)." : NoteStyles.IsButtons(v) ? (_previewPs ? "PLAYSTATION" : "XBOX") + " version - icons follow your controller (Options > Button Icons)." : "Your Note Colors tint this skin. Pixel songs keep their pixel arrows.",
            PrevX + 24f, PrevY + PrevH - 36f, 16f, FunkTheme.Text2, PrevW - 48f);
    }
}
