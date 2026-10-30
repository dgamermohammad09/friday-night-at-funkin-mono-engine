using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes.Editors;

/// <summary>
/// Editors hub = Psych's MasterEditorMenu + V-Slice's DebugMenuSubState merged, in the hand-made FunkUI look:
/// tilted paper cards with the game's own stickers/health icons, Funkin bold lettering, 102 BPM bop.
/// Main menu: 7 (Psych debug_1) or ~ (V-Slice DEBUG_MENU) or R3 on a controller; F5 anywhere.
/// </summary>
public class EditorsHubScene : Scene
{
    private struct Card { public string Title, Sub, Art; public Color Col; public Func<Scene> Make; }
    private readonly List<Card> _cards = new();
    private int _sel;
    private float _t, _pop;
    private Texture2D _desat;
    private readonly Scene _back;
    private static readonly FunkUI.Doodle[] Doodles =
    {
        new("@d_note", 90, 600, 70, FunkUI.Pink, -12), new("@d_star", 1190, 120, 60, FunkUI.Yellow, 10),
        new("@d_squiggle", 1180, 610, 80, FunkUI.Cyan, 6), new("@d_spiral", 70, 140, 64, FunkUI.Green, -4),
        new("@pencil", 640, 660, 60, FunkUI.Orange, 20),
    };

    public EditorsHubScene(Scene back = null)
    {
        _back = back;
        _cards.Add(new Card { Title = "CHART", Sub = "notes, events, BPM, playback, waveform", Art = "@d_note", Col = FunkUI.Pink, Make = () => new ChartEditor() });
        _cards.Add(new Card { Title = "CHARACTER", Sub = "anims, offsets, ghost, camera", Art = "icon:bf", Col = FunkUI.Cyan, Make = () => new CharacterEditor() });
        _cards.Add(new Card { Title = "STAGE", Sub = "props, zoom, camera, character spots", Art = "@d_star", Col = FunkUI.Yellow, Make = () => new StageEditor() });
        _cards.Add(new Card { Title = "WEEK", Sub = "story weeks + freeplay lists", Art = "icon:dad", Col = FunkUI.Green, Make = () => new WeekEditor() });
        _cards.Add(new Card { Title = "DIALOGUE", Sub = "Psych dialogue lines + portraits", Art = "@mic", Col = FunkUI.Orange, Make = () => new DialogueEditor() });
        _cards.Add(new Card { Title = "NOTE SPLASH", Sub = "Psych splash anims + offsets", Art = "@d_bang", Col = FunkUI.Purple, Make = () => new SplashEditor() });
        _cards.Add(new Card { Title = "MENU CHARACTER", Sub = "story menu characters (Psych)", Art = "icon:gf", Col = new Color(0xF9, 0xCF, 0x51), Make = () => new MenuCharacterEditor() });
        _cards.Add(new Card { Title = "DIALOGUE PORTRAIT", Sub = "dialogue characters + offsets (Psych)", Art = "@d_spiral", Col = FunkUI.Blue, Make = () => new DialogueCharacterEditor() });
#if !XBOX_UWP   // the legacy debug viewers are desktop-only
        _cards.Add(new Card { Title = "OLD TOOLS", Sub = "legacy viewers (anim, freeplay...)", Art = "@gear", Col = new Color(200, 200, 210), Make = () => new EditorHubScene(new EditorsHubScene()) });
#endif
    }

    public override void Load()
    {
        _desat = Assets.LoadTexture("menus/main_menu/menuDesat.png");
        if (!Audio.MusicPlaying) Audio.PlayMusic("music/freakyMenu/freakyMenu", true);
    }

    // 2 columns x up to 5 rows (9 cards incl. OLD TOOLS)
    private Vector2 CardPos(int i) => new(i % 2 == 0 ? 330f : 950f, 182f + (i / 2) * 100f);
    private const float CW = 560f, CH = 86f;

    public override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _t += dt; _pop = Math.Max(0f, _pop - dt * 4f);
        Ed.Frame(Input, MouseVirtual, dt);
        int n = _cards.Count, old = _sel;
        if (Input.UpPressed) _sel = (_sel - 2 + n + (n % 2 == 1 && _sel < 2 ? 1 : 0)) % n;
        if (Input.DownPressed) _sel = (_sel + 2) % n;
        if (Input.LeftPressed || Input.RightPressed) _sel = (_sel % 2 == 0) ? Math.Min(n - 1, _sel + 1) : _sel - 1;
        var m = MouseVirtual;
        for (int i = 0; i < n; i++)
        {
            var p = CardPos(i);
            if (m.X >= p.X - CW / 2 && m.X < p.X + CW / 2 && m.Y >= p.Y - CH / 2 && m.Y < p.Y + CH / 2)
            {
                if (Input.MouseMoved) _sel = i;
                if (Input.MouseLeftPressed) { _sel = i; Go(); return; }
            }
        }
        if (_sel != old) { _pop = 1f; Audio.PlaySound("scrollMenu", 0.5f); }
        if (Input.ConfirmPressed) { Go(); return; }
        if (Input.BackPressed) { Audio.PlaySound("cancelMenu"); Game.Scenes.ChangeScene(_back is EditorsHubScene or null or EditorBase ? new MainMenuScene() : _back); }
        // dev hook: FNF_EDITOR_OPEN=<index> opens a card right away (automated tests)
        if (_devOpen >= 0 && _t > 0.3f) { _sel = Math.Min(_devOpen, n - 1); _devOpen = -1; Go(); }
    }
    private static int _devOpen = int.TryParse(Environment.GetEnvironmentVariable("FNF_EDITOR_OPEN"), out var d) ? d : -1;

    private static readonly List<(Buttons, string)> HubHints = new()
    {
        (FNF_MonoGame.Engine.ButtonPrompts.DPadAll, "pick editor"), (Buttons.A, "open"), (Buttons.B, "back"),
    };

    private void Go()
    {
        Audio.PlaySound("confirmMenu");
        Game.Scenes.ChangeScene(_cards[_sel].Make());
    }

    public override void Draw(SpriteBatch sb)
    {
        FunkUI.Begin(sb);
        Ed.BeginWidgets();
        float bop = FunkUI.Bop;
        var tint = new Color(150, 90, 230);
        FunkUI.Fill(0, 0, 1280, 720, tint);
        FunkUI.Cover(_desat, 1.08f + 0.012f * bop, Vector2.Zero, tint);
        FunkUI.Push(640f, 380f, FunkUI.Rad(-4f));
        FunkUI.Fill(-200f, 300f, 1700f, 160f, FunkUI.A(Color.Black, 0.18f));
        FunkUI.Pop();
        FunkUI.DrawDoodles(Doodles, _t, bop);

        // title
        float sc = 0.95f, tw = FunkUI.BoldWidth("EDITORS", sc);
        float b = 1f + 0.03f * bop;
        FunkUI.Push(640f, 70f, FunkUI.Rad(-2.5f), b, b);
        FunkUI.ShadowBlock(640f - tw / 2f - 18f, 30f, tw + 36f, 84f, FunkUI.Yellow, 5f, 9f, 10f);
        FunkUI.Bold("EDITORS", 640f, 36f, sc, 0.5f, _t);
        FunkUI.Pop();
        FunkUI.Push(1010f, 112f, FunkUI.Rad(4f));
        FunkUI.Fill(870f, 98f, 280f, 28f, FunkUI.Ink);
        FunkUI.Text("PSYCH + V-SLICE TOOLS", 1010f, 104f, 17f, Color.White, 0.5f);
        FunkUI.Pop();

        for (int i = 0; i < _cards.Count; i++)
        {
            var c = _cards[i]; var p = CardPos(i);
            bool on = i == _sel;
            float tilt = (FunkUI.Hash(i * 7 + 3) - 0.5f) * 4f + (on ? FunkUI.Wiggle(1f - _pop, 22f, 6f) * 3f : 0f);
            float s = on ? 1.04f + 0.02f * bop : 0.97f;
            FunkUI.Push(p.X, p.Y, FunkUI.Rad(tilt), s, s);
            FunkUI.ShadowBlock(p.X - CW / 2, p.Y - CH / 2, CW, CH, on ? c.Col : FunkUI.Paper, 5f, on ? 11f : 7f, on ? 12f : 8f);
            if (c.Art.StartsWith("icon:")) FunkUI.HealthIcon(c.Art.Substring(5), p.X - CW / 2 + 58f, p.Y, 80f, 0);
            else FunkUI.Sticker(c.Art, p.X - CW / 2 + 58f, p.Y, 62f, FunkUI.Rad(-8f), 1f, 1f, 1f, on ? FunkUI.Ink : c.Col);
            FunkUI.Bold(c.Title, p.X - CW / 2 + 112f, p.Y - 38f, 0.47f, 0f, _t, 1f);
            FunkUI.Text(c.Sub, p.X - CW / 2 + 114f, p.Y + 16f, 15f, FunkUI.Ink);
            if (on) FunkUI.Tape(p.X + CW / 2 - 20f, p.Y - CH / 2 + 4f, 70f, FunkUI.Rad(32f), FunkUI.Pink);
            FunkUI.Pop();
        }
        Ed.SetAssets(Assets);
        Ed.ShowPadHints = FNF_MonoGame.Engine.ButtonPrompts.UseController(Input) || Ed.PadMode;
        Ed.PadHints = HubHints;
        Ed.HelpTape("ARROWS/MOUSE pick   ENTER/A open   ESC/B back   (main menu: 7 or ~ or R3 opens this)");
        Ed.EndWidgets();
        sb.End();
    }
}
