using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Port main menu, hand-made FNF style (FunkUI): the original yellow menu art, a tilted flat-colour slab on the left
/// holding the game's own animated main-menu lettering (buttons / buttons_port atlases, 24 fps) stacked like stickers
/// with alternating tilts, a marker underline scribbled under the selected word, game stickers (transitionSwag) slapped
/// onto a pile on the right for each entry, a taped paper note with the description, the logo bumping to freakyMenu
/// (102 BPM) and marker doodles that "boil" at 8 fps like hand-drawn animation.
/// Rendered at the real back-buffer size (NativeResolution) so it stays crisp on 1440p / 4K TVs.
/// Every destination kept (Freeplay still opens as a substate over this menu). Controller, keyboard and mouse.
/// </summary>
public class MainMenuScene : Scene
{
    private struct Entry
    {
        public string Id, Desc; public string[] Stickers; public Color Color;
        public Entry(string id, string desc, Color color, params string[] stickers) { Id = id; Desc = desc; Color = color; Stickers = stickers; }
    }

    private static readonly Entry[] _entries =
    {
        new("storymode", "Play the weeks in order, cutscenes and all.", FunkUI.Pink, "dad1", "gf1"),
        new("freeplay", "Any song, any difficulty, any character you've got.", FunkUI.Cyan, "bf2", "pico1"),
#if !XBOX_UWP && !ANDROID
        // MainMenuState: hasUpgraded (always on desktop) + FEATURE_OPEN_URL -> 'merch' after freeplay (Referral.doMerchReferral)
        new("merch", "Official Funkin' merch (opens your web browser).", FunkUI.Yellow, "s2:darnell1", "gf1"),
#endif
        new("multiplayer", "Battle a friend - same couch, LAN or Psych Online.", FunkUI.Purple, "bf1", "pico3"),
        new("mods", "Turn your Psych / V-Slice mods on and off.", FunkUI.Green, "s2:cassandra1", "monster1"),
        new("options", "Controls, gameplay, display and sound.", FunkUI.Orange, "gf2", "mom1"),
        new("achievements", "Everything you've unlocked (and what's left).", FunkUI.Blue, "s2:misc1", "s2:darnell1"),
        new("credits", "The crew who made Friday Night Funkin'.", FunkUI.Red, "s2:nene2", "dad3"),
    };

    private static int _rememberedSelectedIndex = 0;
    private int _selectedIndex = 0;

    private SpriteSheet _buttonsSheet, _portSheet, _logoSheet, _merchSheet;
    private Texture2D _bg, _bgPink;
    /// <summary>Automated screenshot runs ignore the physical mouse (other programs move/click it on the shared PC).</summary>
    private static readonly bool DevNoMouse = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FNF_SHOTS")) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FNF_DEVMOUSE")); // dev: FNF_DEVMOUSE tests the mouse path

    private float _time, _introTime, _selTime = 10f, _scroll;
    private Color _slabOld, _slabNew;
    private Vector2 _lastMouse = new(-1, -1);
    private readonly Random _rng = new();

    private struct Slap { public string Art; public float X, Y, Angle, H, Born; }
    private readonly List<Slap> _pile = new();

    private enum MenuState { Idle, Interacting, Exiting }
    private MenuState _state = MenuState.Idle;
    private float _acceptTime = -1f;
    private int _confirmedIndex;
    private bool _fadeOthers;
    private Scene _pendingScene;
    private bool _callbackDone;


    private const string PORT_TEXT = "Mono Engine v0.2.1";

    public override bool NativeResolution => true;

    /// <summary>Set by FreeplayScene while this menu is drawn behind it (persistentDraw): no prompts there.</summary>
    public bool DrawnAsParent { get; set; }

    public override void Load()
    {
        Game.Discord?.SetMenuState("Main Menu");
        _buttonsSheet = SpriteSheet.Load(Game, "menus/main_menu/buttons");
        _portSheet = SpriteSheet.Load(Game, "menus/main_menu/buttons_port");
        if (System.Array.Exists(_entries, e => e.Id == "merch")) _merchSheet = SpriteSheet.Load(Game, "menus/main_menu/merch"); // original mainmenu/merch atlas
        _logoSheet = SpriteSheet.Load(Game, "menus/title/logo");
        _bg = LoadOpt("menus/main_menu/background.png");
        _bgPink = LoadOpt("menus/main_menu/background_pink.png");
        _selectedIndex = Math.Clamp(_rememberedSelectedIndex, 0, _entries.Length - 1);
        _scroll = _selectedIndex;
        _slabOld = _slabNew = _entries[_selectedIndex].Color;
        SlapStickers(_selectedIndex, true);
        if (!Audio.MusicPlaying)
            Audio.PlayMusic("music/freakyMenu", true);
    }

    private Texture2D LoadOpt(string path)
    {
        var t = Assets.LoadTexture(path);
        return t == null || t == Assets.Pixel ? null : t;
    }

    /// <summary>Select an item without sound/flicker (parent menu created for Freeplay's close()).</summary>
    public void SelectItemSilently(int index)
    {
        _selectedIndex = index; _rememberedSelectedIndex = index; _scroll = index;
        _slabOld = _slabNew = _entries[index].Color;
        _pile.Clear(); SlapStickers(index, true);
        _introTime = 10f; _selTime = 10f;
    }

    /// <summary>Back to idle after the Freeplay substate closed.</summary>
    public void OnSubStateClosed()
    {
        _state = MenuState.Idle; _acceptTime = -1f; _fadeOthers = false; _callbackDone = false;
        _introTime = 0.3f;
    }

    public override void Unload()
    {
        _buttonsSheet?.Dispose();
        _portSheet?.Dispose();
        _merchSheet?.Dispose();
        _logoSheet?.Dispose();
    }

    // ------------------------------------------------------------------------------------------------ layout
    private const float ListX = 296f, ListTop = 96f;
    private static float ListGap => _entries.Length > 7 ? 68f : 78f; // 8 words (desktop merch) still fit above the footer
    private float[] _wy;
    /// <summary>Every entry visible (no scrolling); the selected word pushes its lower neighbours down.</summary>
    private float TargetY(int i) => ListTop + i * ListGap + (i > _selectedIndex ? 36f : i == _selectedIndex ? 18f : 0f);

    private SpriteSheet SheetFor(string id, string kind) => _buttonsSheet?.GetAnimation(id + " " + kind) != null ? _buttonsSheet
        : _merchSheet?.GetAnimation(id + " " + kind) != null ? _merchSheet : _portSheet;

    /// <summary>Hand placement of word i: centre, tilt, scale (no grid: alternating tilt + jitter).</summary>
    private (float x, float y, float angle, float scale, string anim, SpriteSheet sheet) WordPlace(int i)
    {
        bool sel = i == _selectedIndex;
        string anim = _entries[i].Id + (sel ? " selected" : " idle");
        var sheet = SheetFor(_entries[i].Id, sel ? "selected" : "idle");
        var size = FunkUI.WordSize(sheet, anim);
        float max = sel ? 0.62f : 0.44f, fitW = sel ? 490f : 390f;
        float scale = size.X > 0 ? Math.Min(max, fitW / size.X) : max;
        float x = ListX + (FunkUI.Hash(i * 7 + 3) - 0.5f) * 80f + (sel ? 10f : 0f);
        float y = _wy != null ? _wy[i] : TargetY(i);
        float angle = FunkUI.Rad((i % 2 == 0 ? -3.4f : 2.8f) + (FunkUI.Hash(i * 13 + 1) - 0.5f) * 2.4f);
        return (x, y, angle, scale, anim, sheet);
    }

    private int HitTest(Vector2 p)
    {
        if (p.X < 0) return -1;
        for (int i = 0; i < _entries.Length; i++)
        {
            var w = WordPlace(i);
            var s = FunkUI.WordSize(w.sheet, w.anim) * w.scale;
            if (Math.Abs(p.X - w.x) <= s.X / 2f && Math.Abs(p.Y - w.y) <= Math.Max(40f, s.Y / 2f)) return i;
        }
        return -1;
    }

    private void SlapStickers(int index, bool quiet)
    {
        var e = _entries[index];
        float now = _time;
        for (int k = 0; k < e.Stickers.Length; k++)
        {
            float jx = (float)_rng.NextDouble() - 0.5f, jy = (float)_rng.NextDouble() - 0.5f;
            _pile.Add(new Slap
            {
                Art = e.Stickers[k],
                X = 900f + k * 150f + jx * 50f, Y = 372f + (k == 0 ? -18f : 30f) + jy * 40f,
                Angle = FunkUI.Rad((k == 0 ? -9f : 10f) + jx * 14f), H = k == 0 ? 238f : 196f,
                Born = quiet ? now - 1f : now + k * 0.07f,
            });
        }
        while (_pile.Count > 8) _pile.RemoveAt(0);
        if (!quiet) PlayStickerSound();
    }

    private void PlayStickerSound()
    {
        int[] ids = { 1, 2, 3, 4, 5, 7 };
        try { Audio.PlaySound("stickersounds/keys/keyClick" + ids[_rng.Next(ids.Length)], 0.7f); } catch { }
    }

    // ------------------------------------------------------------------------------------------------ update
    public override void Update(GameTime gameTime)
    {
        // Back on the real main menu: any multiplayer session ends (Freeplay keeps a non-updated MainMenu as its parent).
        if (MultiplayerSession.Active && Game.Scenes.CurrentScene == this) MultiplayerSession.Leave();
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _time += dt; _introTime += dt; _selTime += dt;
        _scroll = FunkUI.Approach(_scroll, _selectedIndex, 13f, dt);
        _wy ??= new float[_entries.Length];
        for (int i = 0; i < _wy.Length; i++) _wy[i] = _wy[i] == 0f ? TargetY(i) : FunkUI.Approach(_wy[i], TargetY(i), 22f, dt);

        if (_acceptTime >= 0f)
        {
            _acceptTime += dt;
            if (!_callbackDone && _acceptTime >= 0.6f) { _callbackDone = true; OnItemCallback(_confirmedIndex); }
            if (_pendingScene != null && _acceptTime >= 0.8f) { var s = _pendingScene; _pendingScene = null; Game.Scenes.ChangeScene(s); }
        }
        if (_state != MenuState.Idle) return;
        // Editors hub: 7 = Psych MainMenuState debug_1 (MasterEditorMenu), ~ = V-Slice DEBUG_MENU, R3 on a controller
        if (Input.IsPressed(Microsoft.Xna.Framework.Input.Keys.D7) || Input.IsPressed(Microsoft.Xna.Framework.Input.Keys.OemTilde)
            || Input.IsGamePadPressed(Microsoft.Xna.Framework.Input.Buttons.RightStick))
        { Game.Scenes.ChangeScene(new FNF_MonoGame.Scenes.Editors.EditorsHubScene(this)); return; }

        int n = _entries.Length;
        int newIndex = _selectedIndex;
        bool up = Input.UpPressed || Input.LeftPressed, down = Input.DownPressed || Input.RightPressed;
        // LB / RB belong to the DISCORD / DONATE buttons (UpdateSocial); PageUp/PageDown/Tab still step through the list
        bool prev = Input.IsPressed(Keys.PageUp) || (Input.IsPressed(Keys.Tab) && (Input.IsHeld(Keys.LeftShift) || Input.IsHeld(Keys.RightShift)));
        bool next = Input.IsPressed(Keys.PageDown) || (Input.IsPressed(Keys.Tab) && !(Input.IsHeld(Keys.LeftShift) || Input.IsHeld(Keys.RightShift)));
        if (up != down) newIndex = up ? (_selectedIndex + n - 1) % n : (_selectedIndex + 1) % n;
        else if (prev != next) newIndex = prev ? (_selectedIndex + n - 1) % n : (_selectedIndex + 1) % n;
        if (Input.ScrollDelta != 0) newIndex = ((Input.ScrollDelta > 0 ? _selectedIndex - 1 : _selectedIndex + 1) % n + n) % n;

        var mp = DevNoMouse || _introTime < 0.4f ? new Vector2(-1, -1) : MouseVirtual;
        if (UpdateSocial(mp, dt)) return;
        int hover = _socialHover >= 0 ? -1 : HitTest(mp);
        bool moved = (mp - _lastMouse).LengthSquared() > 0.5f && _lastMouse.X >= 0;
        _lastMouse = mp;
        if (moved && hover >= 0) newIndex = hover;
        bool click = Input.MouseLeftPressed && hover >= 0;
        if (click) newIndex = hover;

        if (newIndex != _selectedIndex) Select(newIndex);
        if (Input.ConfirmPressed || click) { Accept(); return; }

        if (Input.BackPressed)
        {
            _state = MenuState.Exiting;
            _rememberedSelectedIndex = _selectedIndex;
            Audio.PlaySound("cancelMenu");
            Game.Scenes.ChangeScene(new TitleScene());
        }
    }

    private void Select(int index)
    {
        Audio.PlaySound("scrollMenu", 0.4f);
        _slabOld = SlabColorNow();
        _selectedIndex = index; _selTime = 0f;
        _slabNew = _entries[index].Color;
        SlapStickers(index, false);
    }

    private Color SlabColorNow() => _selTime >= SlabWipe ? _slabNew : _slabOld;
    private const float SlabWipe = 0.16f;

    private void Accept()
    {
        _state = MenuState.Interacting;
        _confirmedIndex = _selectedIndex;
        _rememberedSelectedIndex = _selectedIndex;
        _acceptTime = 0f; _callbackDone = false;
        _fadeOthers = _entries[_confirmedIndex].Id != "freeplay";   // Freeplay opens over this menu: keep it whole behind
        Audio.PlaySound("confirmMenu");
        Input.Rumble(0.25f, 0.12f);
    }

    private void OnItemCallback(int index)
    {
        _rememberedSelectedIndex = _selectedIndex;
        string id = _entries[index].Id;
        if (id == "merch")
        {
            // selectMerch: Referral.doMerchReferral() (no Newgrounds login -> URL_MERCH_FALLBACK), uiStateMachine -> Idle
            OpenUrl("https://needlejuicerecords.com/en-ca/pages/friday-night-funkin");
            OnSubStateClosed();
            return;
        }
        if (id == "freeplay")
        {
            // openSubState(new FreeplayState()) - no transition; this menu stays loaded and is drawn behind it
            Game.Scenes.SwitchSceneInstant(new FreeplayScene { ParentMenu = this }, load: true, unloadOld: false);
            return;
        }
        _state = MenuState.Exiting;
        _pendingScene = id switch
        {
            "storymode" => new StoryModeScene(),
            "multiplayer" => new MultiplayerScene(), // Local / Online versus (MultiplayerScene.cs)
            "mods" => new ModsScene(),
            "options" => new OptionsScene(),
            "achievements" => new AchievementsScene(),
            _ => new CreditsScene(),
        };
    }

    private static void OpenUrl(string url)
    {
        if (DevNoMouse) { Console.WriteLine("[MENU] openURL (dev run, not opened) " + url); return; }
#if XBOX_UWP
        // UWP / Xbox: the system browser (Edge on the console) through Launcher, on the UI thread
        try
        {
            var dispatcher = Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher;
            _ = dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
            {
                try { bool ok = await Windows.System.Launcher.LaunchUriAsync(new Uri(url)); Console.WriteLine("[MENU] openURL " + url + " -> " + ok); }
                catch (Exception ex) { Console.WriteLine("[MENU] openURL failed: " + ex.Message); }
            });
        }
        catch (Exception ex) { Console.WriteLine("[MENU] openURL failed: " + ex.Message); }
#elif ANDROID
        try
        {
            var intent = new Android.Content.Intent(Android.Content.Intent.ActionView, Android.Net.Uri.Parse(url));
            intent.AddFlags(Android.Content.ActivityFlags.NewTask);
            Android.App.Application.Context.StartActivity(intent);
            Console.WriteLine("[MENU] openURL " + url);
        }
        catch (Exception ex) { Console.WriteLine("[MENU] openURL failed: " + ex.Message); }
#else
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); Console.WriteLine("[MENU] openURL " + url); }
        catch (Exception ex) { Console.WriteLine("[MENU] openURL failed: " + ex.Message); }
#endif
    }

    // ------------------------------------------------------------------------------------------------ social buttons
    // Two round buttons bottom-left above the version text (user-approved mockup: 1920x1080 x 40 / 190, y 840, size 150
    // = logical 26.7 / 126.7, 560, 100; circles ~80 px with a gap, centres 69 / 173, y 600). LB / Q = Discord, RB / E = Donate; mouse hover pops, click opens the link.
    private const string DiscordUrl = "https://discord.gg/JBMPPHTVjK", DonateUrl = "https://buymeacoffee.com/dgamermohammad";
    private static readonly Color DiscordBlurple = new(0x58, 0x65, 0xF2), CoffeeYellow = new(0xFF, 0xDD, 0x00);
    private const float SocialR = 38f, SocialY = 600f;
    private static float SocialX(int i) => 69f + i * 104f;
    private int _socialHover = -1, _socialPressed = -1;
    private float _socialPressTime = 10f;
    private readonly float[] _socialScale = { 1f, 1f };

    /// <summary>Hover / click / LB-RB-Q-E. Returns true when a button was activated this frame.</summary>
    private bool UpdateSocial(Vector2 mp, float dt)
    {
        _socialPressTime += dt;
        _socialHover = -1;
        for (int i = 0; i < 2; i++)
            if (mp.X >= 0 && Vector2.Distance(mp, new Vector2(SocialX(i), SocialY)) <= SocialR + 4f) _socialHover = i;
        for (int i = 0; i < 2; i++)
            _socialScale[i] = FunkUI.Approach(_socialScale[i], _socialHover == i ? 1.12f : 1f, 18f, dt);
        int hit = -1;
        if (Input.IsGamePadPressed(Buttons.LeftShoulder) || Input.IsPressed(Keys.Q)) hit = 0;
        else if (Input.IsGamePadPressed(Buttons.RightShoulder) || Input.IsPressed(Keys.E)) hit = 1;
        else if (_socialHover >= 0 && Input.MouseLeftPressed) hit = _socialHover;
        if (hit < 0) return false;
        _socialPressed = hit; _socialPressTime = 0f;
        Audio.PlaySound("confirmMenu");
        Input.Rumble(0.2f, 0.1f);
        OpenUrl(hit == 0 ? DiscordUrl : DonateUrl);
        return true;
    }

    private void DrawSocial()
    {
        if (DrawnAsParent) return;
        float ia = Intro(0.3f);
        if (ia <= 0f) return;
        bool pad = ButtonPrompts.UseController(Input);
        for (int i = 0; i < 2; i++)
        {
            float cx = SocialX(i), cy = SocialY + (1f - FunkUI.EaseOutBack(ia, 1.6f)) * 160f;
            float s = _socialScale[i] * (1f + 0.03f * FunkUI.Bop);
            if (_socialPressed == i) { var sq = FunkUI.Squash(_socialPressTime, 0.25f); s *= sq.X; }
            FunkUI.Push(cx, cy, 0f, s, s);
            FunkUI.Disc(cx + 4f, cy + 5f, SocialR + 4f, FunkUI.A(Color.Black, 0.55f * ia));   // hard shadow
            FunkUI.Disc(cx, cy, SocialR + 4f, FunkUI.A(Color.Black, ia));                      // black outline
            FunkUI.Disc(cx, cy, SocialR, FunkUI.A(i == 0 ? DiscordBlurple : CoffeeYellow, ia));
            var logo = FunkUI.Tex(i == 0 ? "images/ui/social/discord_logo.png" : "images/ui/social/bmc_logo.png");
            float lh = i == 0 ? SocialR * 1.25f : SocialR * 1.2f;
            if (logo != null) FunkUI.Quad(logo, cx, cy, lh, lh, 0f, new Color(Color.White, ia));
            // controller glyph (LB / RB) or keycap (Q / E) on the top-right rim
            float gs = 20f;
            if (pad) FunkUI.Glyph(true, i == 0 ? Buttons.LeftShoulder : Buttons.RightShoulder, null, cx + SocialR * 0.72f - gs / 2f, cy - SocialR - 2f, gs, ia);
            else FunkUI.Glyph(false, Buttons.A, i == 0 ? "Q" : "E", cx + SocialR * 0.72f - gs / 2f, cy - SocialR - 2f, gs, ia);
            FunkUI.Pop();
            FunkUI.Text(i == 0 ? "DISCORD" : "DONATE", cx, SocialY + 60f, 13f, FunkUI.A(Color.White, ia), 0.5f, 1.5f);
        }
    }

    // ------------------------------------------------------------------------------------------------ draw
    public override void Draw(SpriteBatch sb)
    {
        FunkUI.Begin(sb);
        float bop = FunkUI.Bop;
        DrawBackground(bop);
        DrawSlab();
        FunkUI.DrawDoodles(_doodles, _time, bop, Intro(0.05f));
        DrawWords(bop);
        DrawPile();
        DrawLogo();
        DrawNote();
        DrawFooter();
        DrawSocial();
        sb.End();
    }

    private float Intro(float delay, float dur = 0.35f) => FunkUI.Clamp01((_introTime - delay) / dur);

    private void DrawBackground(float bop)
    {
        float zoom = 1.08f + 0.012f * bop;
        var off = new Vector2(0f, (_entries.Length / 2f - _scroll) * 6f);
        bool flick = _acceptTime >= 0f && _acceptTime < 1.1f && HighscoreManager.Data.FlashingLights && ((int)(_acceptTime / 0.15f)) % 2 == 0;
        if (_bg == null) FunkUI.Fill(0, 0, FunkUI.W, FunkUI.H, FunkUI.Yellow);
        FunkUI.Cover(flick && _bgPink != null ? _bgPink : _bg, zoom, off, Color.White);
    }

    /// <summary>Tilted flat-colour slab behind the words; a new colour wipes in from the left on every selection.</summary>
    private void DrawSlab()
    {
        float ia = FunkUI.EaseOutBack(Intro(0f, 0.4f), 1.4f);
        float slide = (1f - ia) * -620f;
        if (_acceptTime >= 0f && _fadeOthers) slide += -760f * FunkUI.EaseInBack(FunkUI.Clamp01((_acceptTime - 0.25f) / 0.4f));
        float wipe = FunkUI.EaseOutCubic(_selTime / SlabWipe);
        FunkUI.Push(260f, 360f, FunkUI.Rad(-5f), 1f, 1f, slide, 0f);
        const float x = -260f, y = -140f, w = 830f, h = 1000f;
        FunkUI.Fill(x + 14f, y, w, h, Color.Black);                     // hard shadow
        FunkUI.Block(x, y, w, h, _slabOld, 7f);
        FunkUI.Fill(x, y, w * Math.Min(1f, wipe), h, _slabNew);
        // marker hatching along the slab edge, like a doodled border
        for (int k = 0; k < 14; k++)
        {
            float yy = y + 160f + k * 52f;
            FunkUI.Line(x + w - 34f, yy, x + w - 14f, yy - 20f, 4f, FunkUI.A(Color.Black, 0.35f));
        }
        FunkUI.Pop();
    }

    private void DrawWords(float bop)
    {
        // unselected first, selected on top
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < _entries.Length; i++)
            {
                bool sel = i == _selectedIndex;
                if (sel != (pass == 1)) continue;
                var p = WordPlace(i);
                if (p.y < -120f || p.y > FunkUI.H + 120f) continue;
                float intro = FunkUI.EaseOutBack(Intro(0.08f + 0.045f * i, 0.32f), 1.8f);
                float dx = (1f - intro) * -520f, alpha = 1f;
                float ang = p.angle, sx = 1f, sy = 1f;
                if (sel)
                {
                    var sq = FunkUI.Squash(_selTime, 0.2f);
                    float slam = 1f + 0.35f * (1f - FunkUI.EaseOutCubic(_selTime / 0.1f));
                    sx = sq.X * slam * (1f + 0.035f * bop); sy = sq.Y * slam * (1f + 0.035f * bop);
                    ang += FunkUI.Wiggle(_selTime, 24f, 6f) * 0.09f;
                }
                if (_acceptTime >= 0f)
                {
                    if (i == _confirmedIndex)
                    {
                        // FlxFlicker 1.1 s / 0.06 (MainMenuState), flashing-lights setting
                        if (HighscoreManager.Data.FlashingLights && ((int)(_acceptTime / 0.06f)) % 2 == 1) alpha = 0f;
                        var sq = FunkUI.Squash(_acceptTime, 0.28f); sx *= sq.X; sy *= sq.Y;
                    }
                    else if (_fadeOthers)
                    {
                        float t = FunkUI.EaseInBack(FunkUI.Clamp01((_acceptTime - 0.03f * i) / 0.35f));
                        dx += -700f * t; ang += -0.5f * t;
                    }
                }
                if (alpha <= 0f) continue;
                FunkUI.Push(p.x, p.y, ang, sx, sy, dx, 0f);
                FunkUI.Word(p.sheet, p.anim, p.x, p.y, p.scale, _time, alpha);
                if (sel && _acceptTime < 0f) DrawScribble(p.x, p.y, FunkUI.WordSize(p.sheet, p.anim) * p.scale);
                FunkUI.Pop();
            }
    }

    /// <summary>Marker underline scribbled in under the selected word (draws on over 0.2 s, boils at 8 fps).</summary>
    private void DrawScribble(float cx, float cy, Vector2 size)
    {
        float prog = FunkUI.EaseOutCubic((_selTime - 0.05f) / 0.22f);
        if (prog <= 0f) return;
        float w = size.X * 0.9f, x0 = cx - w / 2f, y0 = cy + size.Y / 2f - 6f;
        var pts = new List<Vector2>();
        const int steps = 22;
        int upto = Math.Max(2, (int)(steps * prog));
        int seed = _selectedIndex * 31 + (int)(_time * 8f) % 3;
        for (int k = 0; k <= upto; k++)
        {
            float t = k / (float)steps;
            pts.Add(new Vector2(x0 + w * t, y0 + MathF.Sin(t * 9.5f + _selectedIndex) * 3.5f + t * 6f));
        }
        FunkUI.Marker(pts, 9f, Color.Black, seed, 1.4f);
        FunkUI.Marker(pts, 4f, Color.White, seed, 1.4f);
    }

    private void DrawPile()
    {
        float ia = Intro(0.15f);
        if (ia <= 0f) return;
        float leave = _acceptTime >= 0f && _fadeOthers ? FunkUI.EaseInBack(FunkUI.Clamp01((_acceptTime - 0.2f) / 0.4f)) : 0f;
        int topFrom = _pile.Count - _entries[_selectedIndex].Stickers.Length;
        for (int i = 0; i < _pile.Count; i++)
        {
            var s = _pile[i];
            float age = _time - s.Born;
            if (age < 0f) continue;
            // slap: drops in from 1.5x, squashes flat on impact, wiggles; older stickers stay underneath
            float drop = FunkUI.EaseOutCubic(age / 0.09f);
            float sc = 1.5f - 0.5f * drop;
            var sq = FunkUI.Squash(age - 0.09f, 0.18f);
            float ang = s.Angle + FunkUI.Wiggle(age - 0.09f, 30f, 8f) * 0.08f;
            float bopS = i >= topFrom ? 1f + 0.04f * FunkUI.Bop : 1f;
            float alpha = Math.Min(1f, age / 0.05f) * ia;
            FunkUI.Sticker(s.Art, s.X, s.Y + leave * 700f, s.H * sc * bopS, ang + leave * 0.6f, alpha, sq.X, sq.Y);
        }
    }

    private void DrawLogo()
    {
        var frames = _logoSheet?.GetAnimation("logo bumpin");
        if (frames == null || frames.Count == 0) return;
        float beat = FunkUI.Beat, beatFrac = beat - MathF.Floor(beat);
        int fi = Math.Min(frames.Count - 1, (int)(beatFrac * 60f / 102f * 24f));
        var f = frames[fi];
        int fw = f.FrameWidth > 0 ? f.FrameWidth : f.SourceRect.Width, fh = f.FrameHeight > 0 ? f.FrameHeight : f.SourceRect.Height;
        const float s = 0.33f;
        float ia = FunkUI.EaseOutBack(Intro(0.1f, 0.4f), 2f);
        float cx = 1010f, cy = 118f - (1f - ia) * 260f;
        FunkUI.Push(cx, cy, FunkUI.Rad(4f));
        float left = cx - fw * s / 2f + f.Offset.X * s, top = cy - fh * s / 2f + f.Offset.Y * s;
        FunkUI.Quad(f.Texture ?? _logoSheet.Texture, left + f.SourceRect.Width * s / 2f, top + f.SourceRect.Height * s / 2f,
            f.SourceRect.Width * s, f.SourceRect.Height * s, 0f, Color.White, f.SourceRect);
        FunkUI.Pop();
    }

    /// <summary>Paper note with the description in VCR, taped on, re-slapped on every selection.</summary>
    private void DrawNote()
    {
        var e = _entries[_selectedIndex];
        float ia = FunkUI.EaseOutBack(Intro(0.25f, 0.3f), 2f);
        if (ia <= 0f) return;
        float leave = _acceptTime >= 0f && _fadeOthers ? FunkUI.EaseInBack(FunkUI.Clamp01((_acceptTime - 0.1f) / 0.35f)) : 0f;
        var sq = FunkUI.Squash(_selTime, 0.14f);
        const float w = 420f;
        int lines = Math.Max(1, FunkUI.Wrap(e.Desc, w - 64f, 20f).Count);
        float h = 34f + lines * 26.4f;
        float cx = 990f, cy = 566f + (1f - ia) * 200f + leave * 300f;
        float ang = FunkUI.Rad(-2.5f + (FunkUI.Hash(_selectedIndex) - 0.5f) * 3f) + FunkUI.Wiggle(_selTime, 26f, 8f) * 0.05f;
        FunkUI.Push(cx, cy, ang, sq.X, sq.Y);
        FunkUI.ShadowBlock(cx - w / 2f, cy - h / 2f, w, h, FunkUI.Paper, 4f, 7f, 8f);
        // ruled lines + margin like a notebook page
        for (int k = 1; k <= lines; k++) FunkUI.Fill(cx - w / 2f + 10f, cy - h / 2f + 8f + k * 26.4f, w - 20f, 2f, new Color(120, 170, 255, 120));
        FunkUI.Fill(cx - w / 2f + 34f, cy - h / 2f, 2f, h, new Color(255, 90, 110, 150));
        FunkUI.Paragraph(e.Desc, cx - w / 2f + 46f, cy - h / 2f + 13f, w - 64f, 20f, FunkUI.Ink, 0f, 1.32f);
        FunkUI.Tape(cx + w / 2f - 30f, cy - h / 2f - 2f, 92f, FunkUI.Rad(28f), FunkUI.Pink, 0.9f);
        FunkUI.Tape(cx - w / 2f + 26f, cy + h / 2f + 2f, 80f, FunkUI.Rad(-24f), FunkUI.Pink, 0.9f);
        FunkUI.Pop();
    }

    private static readonly ButtonPrompts.Prompt[] _bar =
    {
        ButtonPrompts.UpDown("PICK"), ButtonPrompts.Confirm("GO!"), ButtonPrompts.Back("BACK"),
    };

    private void DrawFooter()
    {
        float ia = Intro(0.3f);
        // version text: VCR with black outline, bottom-left like MainMenuState
        FunkUI.Text(PORT_TEXT, FunkUI.SafeX - 30f, FunkUI.H - 34f, 16f, FunkUI.A(Color.White, ia), 0f, 1.5f);
        if (_state != MenuState.Idle || DrawnAsParent) return;
        FunkUI.PromptTape(Input, _bar, FunkUI.W - FunkUI.SafeX + 10f, FunkUI.H - 62f, 30f, ia);
    }

    // marker doodles scattered on the yellow art (art, x, y, size, colour, base tilt deg)
    private static readonly FunkUI.Doodle[] _doodles =
    {
        new("@d_star", 680f, 70f, 58f, Color.White, -12f), new("@d_note", 1215f, 260f, 64f, FunkUI.Ink, 10f),
        new("@d_squiggle", 760f, 650f, 90f, Color.White, -6f), new("@d_spiral", 1220f, 470f, 54f, Color.White, 0f),
        new("@d_heart", 700f, 300f, 46f, FunkUI.Pink, -14f), new("@d_bang", 1190f, 640f, 50f, FunkUI.Ink, 8f),
        new("@d_zigzag", 1170f, 40f, 80f, FunkUI.Pink, 4f), new("@d_star", 650f, 480f, 34f, FunkUI.Ink, 20f),
    };
}
