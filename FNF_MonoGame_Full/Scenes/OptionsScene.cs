using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.PsychEngine;
using Keys = Microsoft.Xna.Framework.Input.Keys;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Options scene ? matches original FNF OptionsState with Preferences, Controls, Offsets.
/// Uses checkerboard background and AlphabetFont for menu items.
/// </summary>
public partial class OptionsScene : Scene
{
    // Main categories
    private readonly string[] _categories = { "Preferences", "Controls", "Lag Adjustment", "Save Data Options", "Exit" };   // OptionsState item names
    private int _categoryIndex;
    private float _categoryScrollLerp;
    private float _alphabetBob; // global timer for selected-item character Y-bob
    private bool _inSubMenu;

    // Preferences page: data-driven list (original PreferencesMenu order/names + port settings that are wired up:
    // Middlescroll, Ghost Tapping, Note Offset (PlayScene _globalOffset), Controller Vibration, PC display settings).
    private enum PrefKind { Toggle, Percent, Number, Back, Choice, Action, Text }
    private sealed class PrefItem
    {
        public string Name, Desc; public PrefKind Kind; public bool PcOnly;
        public Func<bool> GetB; public Action<bool> SetB;
        public Func<float> GetF; public Action<float> SetF; public float Min, Max, Step, FastStep; public float[] Presets;
        public Func<float, string> Fmt;
        public string[] Choices; public Func<string> GetS; public Action<string> SetS; public Action Run;
        public bool Psych;   // bound to ClientPrefs (psychsave.json)
    }
    private List<PrefItem> _prefItems;
    private float _prefRepeat;
    private float _prefChangeFlash;

    private void BuildPrefs()
    {
        var d = HighscoreManager.Data;
        PrefItem T(string n, string desc, Func<bool> g, Action<bool> st) => new PrefItem { Name = n, Desc = desc, Kind = PrefKind.Toggle, GetB = g, SetB = st };
        _prefItems = new List<PrefItem>
        {
            T("Naughtyness", "When enabled, raunchy content (such as swearing, etc.) is displayed.", () => d.Naughtyness, v => d.Naughtyness = v),
            T("Downscroll", "When enabled, notes move downwards toward the strumline at the bottom of the screen.", () => d.Downscroll, v => d.Downscroll = v),
            T("Middlescroll", "When enabled, your strumline is centred on the screen.", () => d.Middlescroll, v => d.Middlescroll = v),
            new PrefItem { Name = "Note Skin", Desc = "Arrows = the original look. Controller Buttons (+ 5 more Buttons styles) = your notes show the controller buttons (Xbox or PlayStation). Chip / Future (+ mod skins) = Psych Engine note skins. Preview them in Skins.",
                Kind = PrefKind.Choice, Choices = NoteStyles.Available().ToArray(), GetS = () => NoteStyles.Current, SetS = v => NoteStyles.Set(v) },
            new PrefItem { Name = "Button Icons", Desc = "Controller icons on notes, prompts and the controls menu. AUTO = PlayStation icons while a PlayStation controller is connected, Xbox icons otherwise.",
                Kind = PrefKind.Choice, Choices = ControllerIcons.Choices, GetS = () => ControllerIcons.Setting, SetS = v => ControllerIcons.Setting = v },
            T("Ghost Tapping", "When enabled, pressing a direction with no note to hit is not counted as a miss.", () => d.GhostTapping, v => d.GhostTapping = v),
            T("Flashing Lights", "When disabled, flashing effects are dampened. Useful for people with photosensitive epilepsy.", () => d.FlashingLights, v => d.FlashingLights = v),
            T("Camera Zooms", "When enabled, the camera bounces during songs.", () => d.CameraZoom, v => d.CameraZoom = v),
            new PrefItem { Name = "Note Offset", Desc = "Shifts note timing to make up for audio/video latency (ms). Hold to change faster. Use LAG ADJUSTMENT to calibrate.",
                Kind = PrefKind.Number, GetF = () => d.GlobalOffset, SetF = v => d.GlobalOffset = (int)v, Min = -500, Max = 500, Step = 1, FastStep = 5, Fmt = v => $"{(int)v} MS" },
            T("Controller Vibration", "When enabled, the controller rumbles on menu confirms and missed notes.", () => d.ControllerVibration,
                v => { d.ControllerVibration = v; if (v) Input.Rumble(0.5f, 0.25f); }),
            T("FPS Counter", "When enabled, the framerate and memory usage are displayed in the top-left corner.", () => d.FPSCounter, v => d.FPSCounter = v),
            T("Pause on Unfocus", "When enabled, the game automatically pauses when losing focus.", () => d.AutoPause, v => d.AutoPause = v),
            new PrefItem { Name = "HUD Scale", Desc = "Size of the gameplay HUD (notes, health bar, score). Lower it if the HUD feels too big on a large screen.", Kind = PrefKind.Percent,
                GetF = () => d.HudScale <= 0 ? 1f : d.HudScale, SetF = v => d.HudScale = v, Min = 0.75f, Max = 1f, Step = 0.05f, FastStep = 0.05f },
            new PrefItem { Name = "Safe Area", Desc = "Keeps the HUD away from the screen edges, for TVs that cut off the border (overscan). The corners show the HUD area.", Kind = PrefKind.Percent,
                GetF = () => d.SafeArea, SetF = v => d.SafeArea = v, Min = 0f, Max = 0.1f, Step = 0.01f, FastStep = 0.01f },
            new PrefItem { Name = "Music Volume", Desc = "Volume of the menu music and the songs' instrumental/vocals.", Kind = PrefKind.Percent,
                GetF = () => d.MusicVolume, SetF = v => { d.MusicVolume = v; Audio.MusicVolume = v; }, Min = 0, Max = 1, Step = 0.05f, FastStep = 0.05f },
            new PrefItem { Name = "SFX Volume", Desc = "Volume of menu sounds and gameplay sound effects.", Kind = PrefKind.Percent,
                GetF = () => d.SfxVolume, SetF = v => { d.SfxVolume = v; Audio.SfxVolume = v; Audio.PlaySound("scrollMenu"); }, Min = 0, Max = 1, Step = 0.05f, FastStep = 0.05f },
            T("Cache Assets", "When enabled, textures stay loaded between menus and songs so they load much faster (uses more video memory).", () => d.AssetCache,
                v => { d.AssetCache = v; if (!v) SpriteSheetTextureCache.Purge(); }),
#if !XBOX_UWP
            new PrefItem { Name = "Launch in Fullscreen", Desc = "When enabled, the game starts in fullscreen mode (F11 toggles it at any time).", Kind = PrefKind.Toggle, PcOnly = true,
                GetB = () => d.LaunchFullscreen, SetB = v => { d.LaunchFullscreen = v; if (v != Game.IsFullscreenAny) Game.ToggleFullscreen(true); } },
            new PrefItem { Name = "VSync", Desc = "When enabled, the game attempts to match the framerate with your monitor's refresh rate.", Kind = PrefKind.Toggle, PcOnly = true,
                GetB = () => d.VSync, SetB = v => { d.VSync = v; Game.ApplyDisplaySettings(); } },
            new PrefItem { Name = "Framerate", Desc = "The maximum framerate the game runs at. UNLIMITED renders as fast as possible (with VSync on: your monitor's refresh rate).", Kind = PrefKind.Number, PcOnly = true,
                GetF = () => d.FramerateCap <= 0 ? 1000 : d.FramerateCap, SetF = v => { d.FramerateCap = v >= 1000 ? 0 : (int)v; Game.ApplyDisplaySettings(); },
                Presets = new float[] { 60, 120, 144, 240, 1000 }, Fmt = v => v >= 1000 ? "UNLIMITED" : $"{(int)v} FPS" },
#endif
            new PrefItem { Name = "Back", Desc = "", Kind = PrefKind.Back },
        };
        if (InputManager.IsXboxFamily) _prefItems.RemoveAll(p => p.Name == "Button Icons");   // Xbox console: always Xbox icons
    }
    private int _prefIndex;
    private float _prefScrollLerp;

    // Controls display
    private bool _inControls;
    private bool _inSaveData;
    private int _controlIndex;
    private readonly string[] _controlNames = {
        "Left", "Down", "Up", "Right", "Accept", "Back", "Pause", "Reset", "Default Keys", "Exit"
    };
    private int _kbCol;   // keyboard tab: binding slot (0 = primary, 1 = alternate)
    private float _bindSavedT = -9f;
    private bool _waitingForKey;
    private int _rebindTarget = -1; // index into NoteKeysAlt (0-3)
    private bool _controlOnTab; // true = tab bar focused, Left/Right switches tabs
    private float _selFlash; // selection highlight flash timer

    // Controller bindings tab
    private int _controlTab; // 0=Keyboard, 1=Controller
    private int _gpBindIndex; // selected row in controller bindings
    private int _gpBindCol;   // 0=DPad, 1=Face, 2=Trigger
    private bool _waitingForButton;
    private readonly string[] _gpBindNames = { "Left", "Down", "Up", "Right" };
    private readonly string[] _gpBindSlots = { "D-Pad", "Face", "Bumper/Trigger" };

    // Controller button sprites (Kenney Input Prompts)
    private readonly Dictionary<string, Texture2D> _buttonSprites = new();

    // Offset calibration
    private bool _inOffset;
    private bool _pendingOffsetsMusic;
    private float _offsetBeatTimer;
    private float _offsetBeatBPM = 120f; // calibration BPM
    private float _offsetBeatVisual; // visual indicator pulse (0-1)

    // Checkerboard scroll
    private float _checkerScrollY;

    // Background texture (original uses same menuBG as main menu)
    private Texture2D _bgTex;

    // Spritesheet assets
    private SpriteSheet _checkboxSheet;
    private int _checkAnimFrame;
    private float _checkAnimTimer;

    public override void Load()
    {
        Game.Discord?.SetOptionsState();

        // Load values from save data
        var data = HighscoreManager.Data;

        // Load background (same as main menu)
        _bgTex = Assets.LoadTexture("menus/main_menu/background.png");
        BuildPrefs();
        ClientPrefs.EnsureLoaded();

        // Load checkbox spritesheet for toggle options
        _checkboxSheet = SpriteSheet.Load(Game, "menus/options/checkboxThingie");   // original checkboxThingie atlas
        _prefToggleTime = new float[_prefItems.Count];

        // Load controller button sprites (Kenney Input Prompts)
        string[] spriteNames = {
            "xbox_button_color_a", "xbox_button_color_b", "xbox_button_color_x", "xbox_button_color_y",
            "xbox_dpad_up", "xbox_dpad_down", "xbox_dpad_left", "xbox_dpad_right", "xbox_dpad",
            "xbox_lb", "xbox_rb", "xbox_lt", "xbox_rt",
            "xbox_button_start", "xbox_button_back", "xbox_stick_l"
        };
        foreach (var name in spriteNames)
        {
            var tex = Assets.LoadTexture($"game/ui/controller/{name}.png");
            if (tex != null && tex != Assets.Pixel)
                _buttonSprites[name] = tex;
        }

        if (!Audio.MusicPlaying)
            Audio.PlayMusic("music/freakyMenu", true);
    }

    public override void Unload()
    {
        _checkboxSheet?.Dispose();
        _calNotes?.Dispose(); _calReceptors?.Dispose();
        Input.GameplayMode = false;
        HighscoreManager.SavePreferences();
    }

    public override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _checkerScrollY -= dt * 30f;
        _checkAnimTimer += dt;
        if (_checkAnimTimer >= 1f / 24f) { _checkAnimTimer = 0; _checkAnimFrame++; }
        _selFlash += dt * 6f;
        _alphabetBob += dt;
        _categoryScrollLerp += (_categoryIndex - _categoryScrollLerp) * Math.Min(1f, dt * 14f);
        _catCam.Update(100f + 100f * _categoryIndex, 70f, dt);          // camFocusPoint.y = selected.y
        _prefCam.Update(120f * _prefIndex + 30f, 70f, dt);

        if (_inControls)
        {
            // controls page: the glyph of every pad button you press is drawn pushed in for a moment (cells + panel)
            var flashBtn = Input.GetAnyButtonPressed();
            if (flashBtn.HasValue) ControllerIcons.Flash(flashBtn.Value);
            else if (ControllerIcons.SinceFlash < 30f && Input.IsGamePadHeld(ControllerIcons.LastFlash)) ControllerIcons.Hold();   // stays pushed in while held
        }
        if (!_inOffset && ThemedPageInput()) return;   // LB/RB (Q/E) + tab clicks switch pages
        if (_inControls)
        {
            UpdateControls();
            return;
        }
        if (_inOffset)
        {
            UpdateCalibration(dt);
            return;
        }
        if (_inSaveData)
        {
            UpdateSaveData();
            return;
        }
        if (_inSubMenu)
        {
            if (_editing) { UpdateEditor(); return; }
            UpdatePreferences(dt);
            return;
        }
        UpdateCategories();
    }

    private void UpdateCategoriesOld()
    {

        // Main category navigation
        if (Input.UpPressed)
        {
            _categoryIndex = (_categoryIndex - 1 + _categories.Length) % _categories.Length;
            Audio.PlaySound("scrollMenu");
        }
        if (Input.DownPressed)
        {
            _categoryIndex = (_categoryIndex + 1) % _categories.Length;
            Audio.PlaySound("scrollMenu");
        }
        if (Input.ConfirmPressed)
        {
            Audio.PlaySound("confirmMenu");
            switch (_categoryIndex)
            {
                case 0: _inSubMenu = true; _prefIndex = 0; break;
                case 1: _inControls = true; _controlIndex = 0; break;
                case 2:
                    EnterCalibration();
                    break;
                case 3:
                    _inSaveData = true; _prefIndex = 0;
                    break;
                case 4:
                    Game.Scenes.ChangeScene(new MainMenuScene());
                    break;
            }
        }
        if (Input.BackPressed)
        {
            Audio.PlaySound("cancelMenu");
            Game.Scenes.ChangeScene(new MainMenuScene());
        }
    }

    private void UpdatePreferences(float dt)
    {
        _prefScrollLerp = MenuUI.Lerp(_prefScrollLerp, _prefIndex, 0.16f, dt);
        _prefChangeFlash = Math.Max(0, _prefChangeFlash - dt * 4f);
        int n = _prefItems.Count;
        if (Input.UpPressed) { _prefIndex = (_prefIndex - 1 + n) % n; Audio.PlaySound("scrollMenu", 0.4f); }
        if (Input.DownPressed) { _prefIndex = (_prefIndex + 1) % n; Audio.PlaySound("scrollMenu", 0.4f); }
        if (Input.ScrollDelta != 0) { _prefIndex = Math.Clamp(_prefIndex + (Input.ScrollDelta > 0 ? -1 : 1), 0, n - 1); Audio.PlaySound("scrollMenu", 0.4f); }

        PrefMouse(out bool mclick);
        var it = _prefItems[_prefIndex];
        bool changed = false;
        bool confirm = Input.ConfirmPressed || mclick;
        int dir = Input.LeftPressed ? -1 : Input.RightPressed ? 1 : 0;
        if (mclick && (it.Kind == PrefKind.Number || it.Kind == PrefKind.Percent || it.Kind == PrefKind.Choice)) dir = 1;
        if (dir == 0 && Input.ConfirmPressed && it.Kind == PrefKind.Choice) dir = 1;   // A cycles a choice too (controller-first)
        // hold-to-repeat for number/percent items (0.4 s delay, then every 0.05 s)
        if (dir == 0 && (Input.LeftHeld || Input.RightHeld) && (it.Kind == PrefKind.Number || it.Kind == PrefKind.Percent))
        {
            _prefRepeat += dt;
            if (_prefRepeat > 0.4f) { _prefRepeat -= 0.05f; dir = Input.LeftHeld ? -1 : 1; }
        }
        else if (dir != 0) _prefRepeat = 0f;
        if (!Input.LeftHeld && !Input.RightHeld) _prefRepeat = 0f;

        switch (it.Kind)
        {
            case PrefKind.Toggle:
                if (confirm || dir != 0) { it.SetB(!it.GetB()); _fm.NotifyToggled(_prefIndex); changed = true; Audio.PlaySound("scrollMenu"); if (_prefToggleTime != null) _prefToggleTime[_prefIndex] = _alphabetBob; }
                break;
            case PrefKind.Percent:
            case PrefKind.Number:
                if (dir != 0)
                {
                    float v = it.GetF(), nv;
                    if (it.Presets != null)
                    {
                        int idx = System.Array.FindIndex(it.Presets, p => p >= v - 0.01f);
                        if (idx < 0) idx = it.Presets.Length - 1;
                        if (dir < 0 && it.Presets[idx] >= v - 0.01f) idx--; else if (dir > 0 && it.Presets[idx] <= v + 0.01f) idx++;
                        nv = it.Presets[Math.Clamp(idx, 0, it.Presets.Length - 1)];
                    }
                    else
                    {
                        float step = _prefRepeat > 1.2f ? it.FastStep : it.Step;
                        nv = Math.Clamp((float)Math.Round((v + dir * step) / it.Step) * it.Step, it.Min, it.Max);
                    }
                    if (Math.Abs(nv - v) > 0.0001f) { it.SetF(nv); changed = true; if (it.Kind == PrefKind.Number) Audio.PlaySound("scrollMenu", 0.3f); }
                }
                break;
            case PrefKind.Choice:
                if (dir != 0 && it.Choices != null && it.Choices.Length > 0)
                {
                    int ci = Math.Max(0, System.Array.IndexOf(it.Choices, it.GetS()));
                    it.SetS(it.Choices[(ci + dir + it.Choices.Length) % it.Choices.Length]); changed = true; Audio.PlaySound("scrollMenu");
                }
                break;
            case PrefKind.Action:
                if (confirm) { Audio.PlaySound("confirmMenu"); it.Run?.Invoke(); }
                break;
            case PrefKind.Text:
                if (confirm) { Audio.PlaySound("confirmMenu"); var t = it; OpenEditor(it.Name + ":", it.Desc, it.GetS?.Invoke() == "DEFAULT" ? "" : it.GetS?.Invoke(), v => t.SetS?.Invoke(v)); }
                break;
            case PrefKind.Back:
                if (confirm) { _inSubMenu = false; Audio.PlaySound("cancelMenu"); }
                break;
        }
        if (changed) { _prefChangeFlash = 1f; HighscoreManager.SavePreferences(); if (it.Psych) ClientPrefs.Save(); }

        if (Input.BackPressed)
        {
            _inSubMenu = false;
            Audio.PlaySound("cancelMenu");
        }
    }

    private void UpdateControls()
    {
        // Key capture mode: wait for a key press and assign it
        if (_waitingForKey)
        {
            if (Input.BackPressed)
            {
                _waitingForKey = false;
                return;
            }
            var key = Input.GetAnyKeyPressed();
            if (key.HasValue && _rebindTarget >= 0 && _rebindTarget < 8)
            {
                AssignKey(_rebindTarget, _kbCol, key.Value);
                Input.SaveBindings();
                Audio.PlaySound("confirmMenu");
                _bindSavedT = _alphabetBob;
                _waitingForKey = false;
            }
            return;
        }

        // Button capture mode: wait for a gamepad button press
        if (_waitingForButton)
        {
            if (Input.BackPressed)
            {
                _waitingForButton = false;
                return;
            }
            var btn = Input.GetAnyButtonPressed();
            if (btn.HasValue)
            {
                if (_gpBindIndex >= 0 && _gpBindIndex < 4)
                {
                    // the button already drives another lane: give that lane this slot's old button (swap)
                    var slots = new[] { Input.NoteButtons, Input.NoteFaceButtons, Input.NoteTriggerButtons };
                    var old = slots[_gpBindCol][_gpBindIndex];
                    foreach (var arr in slots) for (int l = 0; l < 4; l++) if (arr[l] == btn.Value && !(arr == slots[_gpBindCol] && l == _gpBindIndex)) arr[l] = old;
                    switch (_gpBindCol)
                    {
                        case 0: Input.NoteButtons[_gpBindIndex] = btn.Value; break;
                        case 1: Input.NoteFaceButtons[_gpBindIndex] = btn.Value; break;
                        case 2: Input.NoteTriggerButtons[_gpBindIndex] = btn.Value; break;
                    }
                }
                else if (_gpBindIndex == 4)
                {
                    Input.ConfirmButton = btn.Value;
                }
                else if (_gpBindIndex == 5)
                {
                    Input.CancelButton = btn.Value;
                }
                else if (_gpBindIndex == 6)
                {
                    Input.PauseButton = btn.Value;
                }
                else if (_gpBindIndex == 7)
                {
                    Input.SwitchCharButton = btn.Value;
                }
                Input.SaveBindings();
                Audio.PlaySound("confirmMenu");
                _bindSavedT = _alphabetBob;
                _waitingForButton = false;
            }
            return;
        }

        // Tab switching: Q/E or LB/RB (always available)
        if (Input.IsPressed(Microsoft.Xna.Framework.Input.Keys.Q) ||
            Input.IsGamePadPressed(Microsoft.Xna.Framework.Input.Buttons.LeftShoulder))
        {
            _controlTab = (_controlTab - 1 + 2) % 2;
            _controlIndex = 0; _gpBindIndex = 0; _gpBindCol = 0;
            Audio.PlaySound("scrollMenu");
            return;
        }
        if (Input.IsPressed(Microsoft.Xna.Framework.Input.Keys.E) ||
            Input.IsGamePadPressed(Microsoft.Xna.Framework.Input.Buttons.RightShoulder))
        {
            _controlTab = (_controlTab + 1) % 2;
            _controlIndex = 0; _gpBindIndex = 0; _gpBindCol = 0;
            Audio.PlaySound("scrollMenu");
            return;
        }

        // Tab bar focused: Left/Right switches tabs, Down enters bindings
        if (_controlOnTab)
        {
            if (Input.LeftPressed || Input.RightPressed)
            {
                _controlTab = (_controlTab + 1) % 2;
                _controlIndex = 0; _gpBindIndex = 0; _gpBindCol = 0;
                Audio.PlaySound("scrollMenu");
                return;
            }
            if (Input.DownPressed || Input.ConfirmPressed)
            {
                _controlOnTab = false;
                Audio.PlaySound("scrollMenu");
                return;
            }
            if (Input.BackPressed)
            {
                _inControls = false;
                Audio.PlaySound("cancelMenu");
            }
            return;
        }

        if (_controlTab == 0)
        {
            // Keyboard bindings tab
            if (Input.UpPressed)
            {
                _controlIndex = (_controlIndex - 1 + _controlNames.Length) % _controlNames.Length;
                Audio.PlaySound("scrollMenu");
            }
            if (Input.DownPressed)
            {
                _controlIndex = (_controlIndex + 1) % _controlNames.Length;
                Audio.PlaySound("scrollMenu");
            }
            if ((Input.LeftPressed || Input.RightPressed) && _controlIndex < 8)
            {
                _kbCol = 1 - _kbCol;
                Audio.PlaySound("scrollMenu");
            }
            if (Input.ConfirmPressed && _controlIndex < 8)
            {
                _waitingForKey = true;
                _rebindTarget = _controlIndex;
                Audio.PlaySound("scrollMenu");
                return;
            }
            if (Input.ConfirmPressed && _controlIndex == 8)
            {
                Input.ResetKeyboardBindings(); Input.SaveBindings();
                Audio.PlaySound("confirmMenu"); _bindSavedT = _alphabetBob;
                return;
            }
            if (Input.BackPressed || (Input.ConfirmPressed && _controlIndex == _controlNames.Length - 1))
            {
                _inControls = false;
                Audio.PlaySound("cancelMenu");
            }
        }
        else
        {
            // Controller bindings tab (4 notes + confirm + cancel + pause + switch-char + defaults + back = 10 rows)
            int totalRows = 10;
            if (Input.UpPressed)
            {
                _gpBindIndex = (_gpBindIndex - 1 + totalRows) % totalRows;
                Audio.PlaySound("scrollMenu");
            }
            if (Input.DownPressed)
            {
                _gpBindIndex = (_gpBindIndex + 1) % totalRows;
                Audio.PlaySound("scrollMenu");
            }
            if (Input.LeftPressed && _gpBindIndex < 4)
            {
                _gpBindCol = (_gpBindCol - 1 + 3) % 3;
                Audio.PlaySound("scrollMenu");
            }
            if (Input.RightPressed && _gpBindIndex < 4)
            {
                _gpBindCol = (_gpBindCol + 1) % 3;
                Audio.PlaySound("scrollMenu");
            }
            if (Input.ConfirmPressed && _gpBindIndex < 8)
            {
                _waitingForButton = true;
                Audio.PlaySound("scrollMenu");
                return;
            }
            if (Input.ConfirmPressed && _gpBindIndex == 8)
            {
                Input.ResetControllerBindings(); Input.SaveBindings();
                Audio.PlaySound("confirmMenu"); _bindSavedT = _alphabetBob;
                return;
            }
            if (Input.BackPressed || (Input.ConfirmPressed && _gpBindIndex == 9))
            {
                _inControls = false;
                Audio.PlaySound("cancelMenu");
            }
        }
    }

    private void UpdateOffset(float dt)
    {
        var data = HighscoreManager.Data;
        // Shift+Left/Right for ?5ms jumps, plain Left/Right for ?1ms
        bool shiftHeld = Input.IsHeld(Microsoft.Xna.Framework.Input.Keys.LeftShift) ||
                         Input.IsHeld(Microsoft.Xna.Framework.Input.Keys.RightShift);
        if (Input.LeftPressed)
        {
            data.GlobalOffset -= shiftHeld ? 5 : 1;
            HighscoreManager.SavePreferences();
        }
        if (Input.RightPressed)
        {
            data.GlobalOffset += shiftHeld ? 5 : 1;
            HighscoreManager.SavePreferences();
        }
        
        // Visual beat indicator for calibration
        float beatInterval = 60f / _offsetBeatBPM;
        _offsetBeatTimer += dt;
        if (_offsetBeatTimer >= beatInterval)
        {
            _offsetBeatTimer -= beatInterval;
            _offsetBeatVisual = 1f;
            Audio.PlaySound("scrollMenu", 0.5f);
        }
        _offsetBeatVisual = Math.Max(0, _offsetBeatVisual - dt * 4f);
        
        if (Input.BackPressed)
        {
            _inOffset = false;
            _offsetBeatTimer = 0;
            Audio.PlaySound("cancelMenu");
        }
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (_inOffset) DrawCalibrationFunk(spriteBatch); else DrawFunk(spriteBatch);
        return;
        spriteBatch.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);

        // Background: OptionsState menuBG + HSVShader(-0.6, 0.9, 3.6), setGraphicSize(FlxG.width * 1.1) + screenCenter, scrollFactor 0
        MenuUI.DrawOptionsBackground(spriteBatch);

        if (_inControls)
            DrawControlsMenu(spriteBatch);
        else if (_inOffset)
            DrawCalibration(spriteBatch);
        else if (_inSaveData)
            DrawSaveDataMenu(spriteBatch);
        else if (_inSubMenu)
            DrawPreferencesMenu(spriteBatch);
        else
            DrawCategoryMenu(spriteBatch);

        // Button prompts (controller glyphs or keyboard keys, switching on the last used device)
        ButtonPrompts.DrawBar(spriteBatch, Assets, Input, CurrentPrompts(), FNFGame.SCREEN_WIDTH / 2, FNFGame.SCREEN_HEIGHT - 46, 0.5f, 32);

        spriteBatch.End();
    }

    private List<ButtonPrompts.Prompt> CurrentPrompts()
    {
        var l = new List<ButtonPrompts.Prompt>();
        if (_inControls)
        {
            if (_controlOnTab) { l.Add(ButtonPrompts.LeftRight("TAB")); l.Add(ButtonPrompts.Confirm("OPEN")); }
            else
            {
                l.Add(ButtonPrompts.UpDown("SELECT"));
                l.Add(ButtonPrompts.LeftRight("SLOT"));
                l.Add(ButtonPrompts.Confirm("REBIND"));
                l.Add(new ButtonPrompts.Prompt(Microsoft.Xna.Framework.Input.Buttons.LeftShoulder, "Q", ""));
                l.Add(new ButtonPrompts.Prompt(Microsoft.Xna.Framework.Input.Buttons.RightShoulder, "E", "TAB"));
            }
        }
        else if (_inSubMenu)
        {
            l.Add(ButtonPrompts.UpDown("SELECT"));
            var it = _prefItems[_prefIndex];
            if (it.Kind == PrefKind.Toggle) l.Add(ButtonPrompts.Confirm("TOGGLE"));
            else if (it.Kind == PrefKind.Back) l.Add(ButtonPrompts.Confirm("SELECT"));
            else l.Add(ButtonPrompts.LeftRight("ADJUST"));
        }
        else if (_inOffset) { AddCalibrationPrompts(l); l.Add(new ButtonPrompts.Prompt(Microsoft.Xna.Framework.Input.Buttons.Back, "ESC", "BACK")); return l; }
        else { l.Add(ButtonPrompts.UpDown("SELECT")); l.Add(ButtonPrompts.Confirm("SELECT")); }
        l.Add(ButtonPrompts.Back("BACK"));
        return l;
    }

    // OptionsMenu camera: follow(camFocusPoint 140x70, lerp 0.085), deadzone (0, 75, w, h - 150 + 40), minScrollY -75.
    private readonly MenuUI.FollowCam _catCam = new MenuUI.FollowCam(75f, 720f - 150f + 40f, -75f, 0.085f);
    // PreferencesMenu camera: margin 160 -> deadzone (0, 160, w, h - 320), minScrollY 0, lerp 0.085.
    private readonly MenuUI.FollowCam _prefCam = new MenuUI.FollowCam(160f, 720f - 320f, 0f, 0.085f);
    private float[] _prefToggleTime;

    private void DrawCategoryMenu(SpriteBatch sb)
    {
        // OptionsMenu.createItem: TextMenuItem(0, 100 + i * 100, name, BOLD).screenCenter(X); idle alpha 0.6, selected 1.
        var names = new List<string>();
        foreach (var c in _categories) names.Add(c.ToUpperInvariant());
        MenuUI.DrawTextMenu(sb, names, _categoryIndex, 100f, 100f, _catCam.ScrollY, _alphabetBob);
    }

    private void DrawPreferencesMenu(SpriteBatch sb)
    {
        // PreferencesMenu: checkbox at (0, 120 i) scale 0.7, label BOLD at y = 120 i + 30, x = 150 selected / 120 idle
        // (+ lefthand value width - 75 for number items, value in DEFAULT font at x 15), description box on the HUD camera.
        var bold = AtlasText.Bold; var def = AtlasText.Default;
        float sy = _prefCam.ScrollY;
        for (int i = 0; i < _prefItems.Count; i++)
        {
            var it = _prefItems[i];
            bool sel = i == _prefIndex;
            float y = 120f * i + 30f - sy;
            if (y < -150 || y > 760) continue;
            float alpha = sel ? 1f : 0.6f;
            float labelX = sel ? 150f : 120f;
            switch (it.Kind)
            {
                case PrefKind.Toggle: DrawPrefCheckbox(sb, it.GetB(), 0f, 120f * i - sy, i); break;
                case PrefKind.Percent:
                case PrefKind.Number:
                {
                    string txt = it.Kind == PrefKind.Percent ? $"{(int)Math.Round(it.GetF() * 100)}%" : (it.Fmt != null ? it.Fmt(it.GetF()) : it.GetF().ToString());
                    float tw = def?.Width(txt) ?? 100;
                    def?.Draw(sb, txt, 15f, y, 1f, _alphabetBob);
                    labelX += tw - 75f;
                    break;
                }
            }
            string label = it.Name;
            if (bold != null) bold.Draw(sb, label, labelX, y, alpha, _alphabetBob);
            if (it.PcOnly) MenuUI.Text(sb, 16, "PC", labelX + (bold?.Width(label) ?? 200) + 12, y + 8, new Color(255, 220, 120), alpha, 1);
        }
        string selName = _prefItems[_prefIndex].Name;
        if (selName == "HUD Scale" || selName == "Safe Area") DrawHudAreaPreview(sb);
        MenuUI.DescriptionBox(sb, _prefItems[_prefIndex].Desc);
    }

    /// <summary>Corner brackets around the area the gameplay HUD will cover (HUD Scale x Safe Area, centred).</summary>
    private void DrawHudAreaPreview(SpriteBatch sb)
    {
        float k = PlayScene.HudUserScale;
        int w = (int)(1280 * k), h = (int)(720 * k), x = (1280 - w) / 2, y = (720 - h) / 2, len = 70, t = 6;
        float pulse = 0.75f + 0.25f * MathF.Sin(_alphabetBob * 5f);
        var col = new Color(0xFA, 0xFD, 0x6D);   // ControlsMenu device-bar yellow
        foreach (var (cx, cy, dx, dy) in new[] { (x, y, 1, 1), (x + w, y, -1, 1), (x, y + h, 1, -1), (x + w, y + h, -1, -1) })
        {
            for (int pass = 0; pass < 2; pass++)
            {
                int o = pass == 0 ? 3 : 0; var c = pass == 0 ? Color.Black : Color.Lerp(Color.White, col, pulse);
                var hx = new Rectangle(dx > 0 ? cx - o : cx - len - o, dy > 0 ? cy - o : cy - t - o, len + 2 * o, t + 2 * o);
                var vy = new Rectangle(dx > 0 ? cx - o : cx - t - o, dy > 0 ? cy - o : cy - len - o, t + 2 * o, len + 2 * o);
                MenuUI.Rect(sb, hx, c); MenuUI.Rect(sb, vy, c);
            }
        }
    }

    private void DrawArrows(SpriteBatch sb, float cx, float cy, float half)
    {
        float bob = (float)Math.Sin(_alphabetBob * 6f) * 3f;
        MenuUI.Text(sb, 30, "<", cx - half - bob, cy - 17, Color.White, 1f, 2, 0.5f);
        MenuUI.Text(sb, 30, ">", cx + half + bob, cy - 17, Color.White, 1f, 2, 0.5f);
    }

    /// <summary>CheckboxPreferenceItem: checkboxThingie atlas, setGraphicSize(140 * 0.7) (scale 0.7, origin 70,112.5 from the
    /// first atlas frame), 'static' = "Check Box unselected" offset (0,0); 'checked' = "Check Box selecting animation"
    /// (24 fps, not looped, replayed on toggle) offset (17, 70).</summary>
    private void DrawPrefCheckbox(SpriteBatch sb, bool on, float x, float y, int idx)
    {
        if (_checkboxSheet == null) return;
        var frames = _checkboxSheet.GetAnimation(on ? "Check Box selecting animation" : "Check Box unselected");
        if (frames == null || frames.Count == 0) return;
        int fi = 0;
        if (on)
        {
            float t = (_prefToggleTime != null && idx < _prefToggleTime.Length) ? _alphabetBob - _prefToggleTime[idx] : 99f;
            fi = Math.Min(frames.Count - 1, (int)(t * 24f));
        }
        var f = frames[fi];
        const float s = 0.7f;
        float ox = on ? 17f : 0f, oy = on ? 70f : 0f;
        float bx = x - ox + 70f * (1f - s), by = y - oy + 112.5f * (1f - s);
        sb.Draw(f.Texture ?? _checkboxSheet.Texture, new Vector2(bx + f.Offset.X * s, by + f.Offset.Y * s), f.SourceRect, Color.White, 0f, Vector2.Zero, s, SpriteEffects.None, 0f);
    }

    private void DrawPreferenceDescription(SpriteBatch sb) => MenuUI.DescriptionBox(sb, _prefItems[_prefIndex].Desc);

    private void UpdateSaveData()
    {
        int total = 2;
        if (Input.UpPressed)
        {
            _prefIndex = (_prefIndex - 1 + total) % total;
            Audio.PlaySound("scrollMenu");
        }
        if (Input.DownPressed)
        {
            _prefIndex = (_prefIndex + 1) % total;
            Audio.PlaySound("scrollMenu");
        }

        if (Input.ConfirmPressed)
        {
            if (_prefIndex == 0)
            {
                HighscoreManager.ClearSaveData();
                Audio.PlaySound("confirmMenu");
            }
            else
            {
                _inSaveData = false;
                Audio.PlaySound("cancelMenu");
            }
        }

        if (Input.BackPressed)
        {
            _inSaveData = false;
            Audio.PlaySound("cancelMenu");
        }
    }

    private void DrawSaveDataMenu(SpriteBatch sb)
    {
        // SaveDataMenu.createItem: TextMenuItem(0, 100 + i * 100, name, BOLD).screenCenter(X)
        MenuUI.DrawTextMenu(sb, new[] { "CLEAR SAVE DATA", "EXIT" }, _prefIndex, 100f, 100f, 0f, _alphabetBob);
    }

    // ControlsMenu camera: follow(camFollow 70x70, lerp 0.06), margin 100 -> deadzone (0, 100, w, h - 200), minScrollY 0.
    private readonly MenuUI.FollowCam _ctlCam = new MenuUI.FollowCam(100f, 720f - 200f, 0f, 0.06f);
    private const float CTL_SPACER = 70f, CTL_LABEL_X = 50f;

    /// <summary>Row y (menu space) of control row r: devices bar -> start 120, header + 70 spacing like ControlsMenu.</summary>
    private static float CtlRowY(int r, int firstGroupRows)
    {
        float y = 120f + CTL_SPACER;                                  // "NOTES" header at 120
        if (r < firstGroupRows) return y + r * CTL_SPACER;
        return y + firstGroupRows * CTL_SPACER + CTL_SPACER * 2 + (r - firstGroupRows) * CTL_SPACER;  // blank + "UI" header
    }

    private void DrawControlsMenu(SpriteBatch sb)
    {
        // ControlsMenu look: yellow device bar (0xFAFD6D, 100 px) with BOLD "KEYBOARD"/"GAMEPAD" items (TextMenuList
        // Horizontal: x = w/2 - width - 30 / w/2 + 30), BOLD headers centred, BOLD labels at x 50 (alpha 0.6),
        // InputItems in the DEFAULT font (selected alpha 1, others 0.6), camera following the selected row.
        int selRow = _controlTab == 0 ? _controlIndex : _gpBindIndex;
        float targetY = _controlOnTab ? 0f : CtlRowY(selRow, 4);
        _ctlCam.Update(targetY, 70f, 1f / 60f);
        float sy = _ctlCam.ScrollY;
        var bold = AtlasText.Bold;
        MenuUI.Rect(sb, new Rectangle(0, (int)(-sy), FNFGame.SCREEN_WIDTH, 100), new Color(0xFA, 0xFD, 0x6D));
        if (bold != null)
        {
            string k = "KEYBOARD", g = "GAMEPAD";
            float ky = (100 - bold.MaxHeight) / 2f - sy;
            bool tabSel = _controlOnTab;
            bold.Draw(sb, k, 640 - bold.Width(k) - 30, ky, _controlTab == 0 ? (tabSel ? 1f : 0.85f) : 0.6f, _alphabetBob);
            bold.Draw(sb, g, 640 + 30, ky, _controlTab == 1 ? (tabSel ? 1f : 0.85f) : 0.6f, _alphabetBob);
        }
        bold?.DrawCentered(sb, "NOTES", 120f - sy, 1f, _alphabetBob);
        bold?.DrawCentered(sb, _controlTab == 0 ? "UI" : "MENU", CtlRowY(4, 4) - CTL_SPACER - sy, 1f, _alphabetBob);
        if (_controlTab == 0) DrawKeyboardBindings(sb, FNFGame.Instance.Input, sy);
        else DrawControllerBindings(sb, FNFGame.Instance.Input, sy);

    }

    private void DrawCtlLabel(SpriteBatch sb, string text, float y, bool sel)
        => AtlasText.Bold?.Draw(sb, text, CTL_LABEL_X + (sel ? 0f : 0f), y, sel ? 1f : 0.6f, _alphabetBob);

    private void DrawCtlValue(SpriteBatch sb, string text, float x, float y, bool sel, bool waiting)
    {
        var def = AtlasText.Default;
        if (def == null) return;
        if (waiting) { float f = 0.5f + 0.5f * MathF.Sin(_selFlash * 4f); def.Draw(sb, "PRESS ANY KEY", x, y, f, _alphabetBob, 0.7f); return; }
        // just (re)bound: quick squash-and-stretch slap on the selected cell (bindings save immediately)
        float since = _alphabetBob - _bindSavedT, sc = 0.8f;
        if (sel && since >= 0f && since < 0.3f) sc *= 1f + 0.25f * MathF.Sin(since / 0.3f * MathF.PI);
        def.Draw(sb, text, x, y + (0.8f - sc) * 30f, sel ? 1f : 0.6f, _alphabetBob, sc);
    }

    private Keys[] KbRow(int r) => r switch
    {
        4 => Input.UiAccept, 5 => Input.UiBack, 6 => Input.UiPause, 7 => Input.UiReset, _ => null
    };

    /// <summary>Bind slot (row, col). Note lanes: a key already used by another lane is swapped onto this slot's old key.</summary>
    private void AssignKey(int row, int col, Keys key)
    {
        if (row < 4)
        {
            var cols = new[] { Input.NoteKeysAlt, Input.NoteKeysArrow };
            var old = cols[col][row];
            for (int c = 0; c < 2; c++) for (int l = 0; l < 4; l++)
                if (cols[c][l] == key && !(c == col && l == row)) cols[c][l] = old;
            cols[col][row] = key;
        }
        else
        {
            var k = KbRow(row);
            if (k[1 - col] == key) k[1 - col] = k[col];   // same key in both slots: swap instead of duplicating
            k[col] = key;
        }
    }

    internal static string KeyName(Keys k) => k switch
    {
        Keys.None => "---", Keys.Escape => "ESC", Keys.Back => "BACKSPACE", Keys.Enter => "ENTER", Keys.Space => "SPACE",
        Keys.LeftShift => "L SHIFT", Keys.RightShift => "R SHIFT", Keys.LeftControl => "L CTRL", Keys.RightControl => "R CTRL",
        Keys.LeftAlt => "L ALT", Keys.RightAlt => "R ALT", Keys.OemComma => ",", Keys.OemPeriod => ".", Keys.OemQuestion => "/",
        Keys.OemSemicolon => ";", Keys.OemQuotes => "'", Keys.OemOpenBrackets => "[", Keys.OemCloseBrackets => "]",
        Keys.OemMinus => "-", Keys.OemPlus => "=", Keys.OemTilde => "~", Keys.OemPipe => "BACKSLASH",
        >= Keys.D0 and <= Keys.D9 => ((int)(k - Keys.D0)).ToString(),
        >= Keys.NumPad0 and <= Keys.NumPad9 => "NUM " + (int)(k - Keys.NumPad0),
        _ => k.ToString().ToUpperInvariant()
    };

    private void DrawKeyboardBindings(SpriteBatch sb, InputManager input, float sy)
    {
        for (int i = 0; i < _controlNames.Length; i++)
        {
            bool sel = i == _controlIndex && !_controlOnTab;
            float y = CtlRowY(i, 4) - sy;
            if (y < -80 || y > 760) continue;
            if (i >= 8) { AtlasText.Bold?.DrawCentered(sb, _controlNames[i].ToUpperInvariant(), y, sel ? 1f : 0.6f, _alphabetBob); continue; }
            DrawCtlLabel(sb, _controlNames[i].ToUpperInvariant(), y, sel);
            for (int k = 0; k < 2; k++)
            {
                var key = i < 4 ? (k == 0 ? input.NoteKeysAlt[i] : input.NoteKeysArrow[i]) : KbRow(i)[k];
                bool cellSel = sel && k == _kbCol;
                DrawCtlValue(sb, KeyName(key), CTL_LABEL_X + 560 + k * 320, y + 6, cellSel, _waitingForKey && i == _rebindTarget && k == _kbCol);
            }
        }
    }

    private void DrawControllerBindings(SpriteBatch sb, InputManager input, float sy)
    {
        string[] nav = { "Confirm", "Cancel", "Pause", "Switch Char" };
        Microsoft.Xna.Framework.Input.Buttons[] navB = { input.ConfirmButton, input.CancelButton, input.PauseButton, input.SwitchCharButton };
        for (int r = 0; r < 10; r++)
        {
            bool rowSel = r == _gpBindIndex && !_controlOnTab;
            float y = CtlRowY(r, 4) - sy;
            if (y < -80 || y > 760) continue;
            if (r >= 8) { AtlasText.Bold?.DrawCentered(sb, r == 8 ? "DEFAULT BUTTONS" : "EXIT", y, rowSel ? 1f : 0.6f, _alphabetBob); continue; }
            DrawCtlLabel(sb, (r < 4 ? _gpBindNames[r] : nav[r - 4]).ToUpperInvariant(), y, rowSel);
            int cols = r < 4 ? 3 : 1;
            for (int c = 0; c < cols; c++)
            {
                var btn = r < 4 ? (c == 0 ? input.NoteButtons[r] : c == 1 ? input.NoteFaceButtons[r] : input.NoteTriggerButtons[r]) : navB[r - 4];
                bool cellSel = rowSel && (r >= 4 || c == _gpBindCol);
                float x = 560 + c * 230;
                if (_waitingForButton && cellSel) { DrawCtlValue(sb, "", x, y, true, true); continue; }
                int gx = ButtonPrompts.DrawGlyph(sb, Assets, true, btn, "", (int)x, (int)y + 4, 56, cellSel ? 1f : 0.6f);
                AtlasText.Default?.Draw(sb, FormatButtonName(btn), x + 64, y + 8, cellSel ? 1f : 0.6f, _alphabetBob, 0.6f);
            }
        }
    }

    private void DrawButtonCell(SpriteBatch sb, Texture2D btnTex, string fallbackText, int x, int y, bool highlighted, Color color)
    {
        int boxW = 80;
        int boxH = 36;
        int by = y;

        // Box background
        Color bg = highlighted ? new Color(40, 50, 70, 200) : new Color(25, 25, 35, 120);
        sb.Draw(Assets.Pixel, new Rectangle(x, by, boxW, boxH), bg);
        // Box border
        Color border = highlighted ? color : Color.Gray * 0.2f;
        sb.Draw(Assets.Pixel, new Rectangle(x, by, boxW, 1), border);
        sb.Draw(Assets.Pixel, new Rectangle(x, by + boxH - 1, boxW, 1), border);
        sb.Draw(Assets.Pixel, new Rectangle(x, by, 1, boxH), border);
        sb.Draw(Assets.Pixel, new Rectangle(x + boxW - 1, by, 1, boxH), border);

        if (btnTex != null)
        {
            int sprSize = highlighted ? 32 : 28;
            int sx = x + (boxW - sprSize) / 2;
            int sy = by + (boxH - sprSize) / 2;
            sb.Draw(btnTex, new Rectangle(sx, sy, sprSize, sprSize), Color.White * (highlighted ? 1f : 0.7f));
        }
        else
        {
            var font = Assets.GetFont(highlighted ? 16 : 13);
            if (font != null)
            {
                var sz = font.MeasureString(fallbackText);
                Color tc = highlighted ? Color.White : Color.Gray * 0.8f;
                font.DrawText(sb, fallbackText, new Vector2(x + (boxW - sz.X) / 2, by + (boxH - sz.Y) / 2), tc);
            }
        }
    }

    /// <summary>
    /// Map a Buttons enum value to a Kenney sprite filename (without extension).
    /// </summary>
    internal static string GetButtonSpriteName(Microsoft.Xna.Framework.Input.Buttons btn)
    {
        return btn switch
        {
            Microsoft.Xna.Framework.Input.Buttons.A => "xbox_button_color_a",
            Microsoft.Xna.Framework.Input.Buttons.B => "xbox_button_color_b",
            Microsoft.Xna.Framework.Input.Buttons.X => "xbox_button_color_x",
            Microsoft.Xna.Framework.Input.Buttons.Y => "xbox_button_color_y",
            Microsoft.Xna.Framework.Input.Buttons.DPadUp => "xbox_dpad_up",
            Microsoft.Xna.Framework.Input.Buttons.DPadDown => "xbox_dpad_down",
            Microsoft.Xna.Framework.Input.Buttons.DPadLeft => "xbox_dpad_left",
            Microsoft.Xna.Framework.Input.Buttons.DPadRight => "xbox_dpad_right",
            Microsoft.Xna.Framework.Input.Buttons.LeftShoulder => "xbox_lb",
            Microsoft.Xna.Framework.Input.Buttons.RightShoulder => "xbox_rb",
            Microsoft.Xna.Framework.Input.Buttons.LeftTrigger => "xbox_lt",
            Microsoft.Xna.Framework.Input.Buttons.RightTrigger => "xbox_rt",
            Microsoft.Xna.Framework.Input.Buttons.Start => "xbox_button_start",
            Microsoft.Xna.Framework.Input.Buttons.Back => "xbox_button_back",
            Microsoft.Xna.Framework.Input.Buttons.LeftStick => "xbox_stick_l",
            _ => null
        };
    }

    internal static string FormatButtonName(Microsoft.Xna.Framework.Input.Buttons btn) => ControllerIcons.ButtonName(btn);   // Xbox or PlayStation names

    internal static string FormatButtonNameXbox(Microsoft.Xna.Framework.Input.Buttons btn)
    {
        return btn switch
        {
            Microsoft.Xna.Framework.Input.Buttons.LeftShoulder => "LB",
            Microsoft.Xna.Framework.Input.Buttons.RightShoulder => "RB",
            Microsoft.Xna.Framework.Input.Buttons.LeftTrigger => "LT",
            Microsoft.Xna.Framework.Input.Buttons.RightTrigger => "RT",
            Microsoft.Xna.Framework.Input.Buttons.DPadUp => "D-Up",
            Microsoft.Xna.Framework.Input.Buttons.DPadDown => "D-Down",
            Microsoft.Xna.Framework.Input.Buttons.DPadLeft => "D-Left",
            Microsoft.Xna.Framework.Input.Buttons.DPadRight => "D-Right",
            Microsoft.Xna.Framework.Input.Buttons.LeftStick => "L-Stick",
            Microsoft.Xna.Framework.Input.Buttons.RightStick => "R-Stick",
            _ => btn.ToString()
        };
    }

    private void DrawOffsetMenu(SpriteBatch sb)
    {
        // Port calibrator drawn in the OptionsState style: BOLD AtlasText items centred (100 + i * 100), DEFAULT font info.
        var data = HighscoreManager.Data;
        var bold = AtlasText.Bold; var def = AtlasText.Default;
        bold?.DrawCentered(sb, "GLOBAL OFFSET", 100f, 0.6f, _alphabetBob);
        bold?.DrawCentered(sb, $"< {data.GlobalOffset} MS >", 200f, 1f, _alphabetBob);
        string desc = data.GlobalOffset == 0 ? "Offset is neutral"
            : data.GlobalOffset > 0 ? $"Audio plays {data.GlobalOffset}ms later" : $"Audio plays {Math.Abs(data.GlobalOffset)}ms earlier";
        def?.DrawCentered(sb, desc, 300f, 0.8f, _alphabetBob, 0.6f);

        // beat ticker (120 BPM): the cursor should cross the centre line on every tick sound
        int barX = FNFGame.SCREEN_WIDTH / 2 - 250, barY = 400, barW = 500, barH = 26;
        MenuUI.Rect(sb, new Rectangle(barX - 4, barY - 4, barW + 8, barH + 8), Color.Black);
        MenuUI.Rect(sb, new Rectangle(barX, barY, barW, barH), new Color(255, 255, 255, 70));
        int centerLineX = barX + barW / 2;
        MenuUI.Rect(sb, new Rectangle(centerLineX - 2, barY - 10, 4, barH + 20), Color.White);
        float progress = _offsetBeatTimer / (60f / _offsetBeatBPM);
        int tickerX = barX + (int)(progress * barW);
        MenuUI.Rect(sb, new Rectangle(tickerX - 6, barY - 6, 12, barH + 12), Color.Black);
        MenuUI.Rect(sb, new Rectangle(tickerX - 4, barY - 4, 8, barH + 8), Color.Lerp(new Color(255, 220, 120), Color.White, _offsetBeatVisual));
        def?.DrawCentered(sb, "Visual beat calibration (120 BPM)", 450f, 0.8f, _alphabetBob, 0.5f);
    }

    private void DrawMenuItem(SpriteBatch sb, string text, int y, bool selected, Color color)
    {
        var alphabetFont = AlphabetFont.Bold;
        if (alphabetFont != null)
        {
            float scale = selected ? 0.6f : 0.5f;
            alphabetFont.DrawStringCentered(sb, text.ToUpper(), y, color, scale);
        }
        else
        {
            var font = Assets.GetFont(selected ? 32 : 26);
            if (font != null)
            {
                var sz = font.MeasureString(text);
                font.DrawText(sb, text, new Vector2((FNFGame.SCREEN_WIDTH - sz.X) / 2, y), color);
            }
        }
    }

    private void DrawCheckerboard(SpriteBatch sb)
    {
        int tileSize = 80;
        int offY = ((int)_checkerScrollY % (tileSize * 2) + tileSize * 2) % (tileSize * 2);
        for (int y = -tileSize * 2 + offY; y < FNFGame.SCREEN_HEIGHT + tileSize; y += tileSize)
        {
            for (int x = 0; x < FNFGame.SCREEN_WIDTH + tileSize; x += tileSize)
            {
                int row = (y + tileSize * 2) / tileSize;
                int col = x / tileSize;
                Color c = ((row + col) % 2 == 0) ? new Color(50, 25, 70) : new Color(70, 40, 95);
                sb.Draw(Assets.Pixel, new Rectangle(x, y, tileSize, tileSize), c);
            }
        }
    }
}

