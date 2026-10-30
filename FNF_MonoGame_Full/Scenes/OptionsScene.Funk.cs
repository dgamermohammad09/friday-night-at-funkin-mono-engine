using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.PsychEngine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Options in the hand-made FNF style (FunkMenu: tilted paper strips, checkbox ticks, sliders, binding keycaps / Xbox glyphs,
/// taped help note) + the Psych Engine / Psych Online settings pages:
///   GAMEPLAY  = Psych GameplaySettingsSubState (+ Psych Online extras), bound to ClientPrefs (psychsave.json)
///   VISUALS   = Psych VisualsUISubState (+ PSO camera / rating options)
///   GRAPHICS  = Psych GraphicsSettingsSubState
///   ONLINE    = Psych Online OnlineOptionsState (nickname, server, skin, downloads, chat, network toggles)
/// The V-Slice PREFERENCES page keeps the original items (SaveData). Rendered at native resolution (crisp on TVs),
/// except the latency calibrator (1280x720 strumline art).
/// </summary>
public partial class OptionsScene
{
    public override bool NativeResolution => true;   // every page incl. lag adjustment draws at the back-buffer size

    private readonly FunkMenu _fm = new FunkMenu { Themed = true, ListTop = 100f, ListBottom = 556f, RowH = 46f, RowGap = 8f, RowW = 1000f };   // in-house Funkin theme (FunkTheme)
    private Texture2D _desatF;
    private int _prefPage;                 // 0 V-Slice prefs, 1 gameplay, 2 visuals, 3 graphics, 4 online
    private List<PrefItem> _vsPrefs;
    private Vector2 _lastMouseO = new(-1, -1);
    private static readonly bool DevNoMouseO = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FNF_SHOTS")) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FNF_DEVMOUSE")); // dev: FNF_DEVMOUSE tests the mouse path
    private string _toast; private float _toastT;

    // text editor (nickname / server)
    private bool _editing; private string _editBuf = "", _editQ = "", _editHint = ""; private int _editChar; private Action<string> _editDone;
    private const string EditChars = "abcdefghijklmnopqrstuvwxyz0123456789.:/-_ @!?,'+";

    private static readonly (string name, string art, string desc, Color col)[] CatsBase =
    {
        ("PREFERENCES", "bf1", "The original game's settings: scrolling, ghost tapping, volume, HUD size, display.", FunkUI.Yellow),
        ("GAMEPLAY", "@d_note", "Psych Engine gameplay: opponent notes, hitsounds, rating offset, hit windows, reset button.", FunkUI.Green),
        ("VISUALS AND UI", "gf1", "Psych Engine visuals: note splashes, health bar, time bar, camera, combo popups.", FunkUI.Pink),
        ("GRAPHICS", "@gear", "Low quality, anti-aliasing, shaders, combo stacking.", FunkUI.Orange),
        ("CONTROLS", "@controller", "Rebind keyboard keys and controller buttons.", FunkUI.Cyan),
        ("LAG ADJUSTMENT", "@hourglass", "Tap to the beat to calibrate your audio / input latency.", FunkUI.Purple),
        ("PSYCH ONLINE", "@globe", "Online: nickname, server, your skin, mod downloads, chat and network options.", FunkUI.Cyan),
        ("MODS", "@d_star", "Turn mods on / off, or download new ones from GameBanana.", FunkUI.Green),
        ("SAVE DATA", "@leave", "Clear your scores and progress.", FunkUI.Red),
        ("EXIT", "@back", "Back to the main menu.", FunkUI.Paper),
    };

    private Color PageColor => _inControls ? FunkUI.Cyan : _inSaveData ? FunkUI.Red : _inSubMenu ? _prefPage switch
    {
        1 => FunkUI.Green, 2 => FunkUI.Pink, 10 => FunkUI.Pink, 3 => FunkUI.Orange, 4 => FunkUI.Cyan, _ => FunkUI.Yellow,
    } : FunkUI.Purple;

    private static Color RowColor(Color bg) => bg == FunkUI.Yellow || bg == FunkUI.Orange || bg == FunkUI.Green || bg == FunkUI.Red ? FunkUI.Cyan : FunkUI.Yellow;

    // ------------------------------------------------------------------ Psych pages (ClientPrefs)
    private static SaveVariables P => ClientPrefs.Data;

    private PrefItem PT(string n, string desc, Func<bool> g, Action<bool> s) => new PrefItem { Name = n, Desc = desc, Kind = PrefKind.Toggle, GetB = g, SetB = s, Psych = true };
    private PrefItem PP(string n, string desc, Func<float> g, Action<float> s, float min = 0f, float max = 1f, float step = 0.1f)
        => new PrefItem { Name = n, Desc = desc, Kind = PrefKind.Percent, GetF = g, SetF = s, Min = min, Max = max, Step = step, FastStep = step, Psych = true };
    private PrefItem PN(string n, string desc, Func<float> g, Action<float> s, float min, float max, float step, Func<float, string> fmt, float fast = 0f)
        => new PrefItem { Name = n, Desc = desc, Kind = PrefKind.Number, GetF = g, SetF = s, Min = min, Max = max, Step = step, FastStep = fast > 0 ? fast : step, Fmt = fmt, Psych = true };
    private PrefItem PC(string n, string desc, string[] choices, Func<string> g, Action<string> s)
        => new PrefItem { Name = n, Desc = desc, Kind = PrefKind.Choice, Choices = choices, GetS = g, SetS = s, Psych = true };
    private PrefItem PA(string n, string desc, Action run, Func<string> value = null) => new PrefItem { Name = n, Desc = desc, Kind = PrefKind.Action, Run = run, GetS = value };
    private PrefItem PBack() => new PrefItem { Name = "Back", Desc = "", Kind = PrefKind.Back };

    private List<PrefItem> BuildPsychPage(int page)
    {
        switch (page)
        {
            case 1: return new List<PrefItem>   // GameplaySettingsSubState (Psych 1.0 + Psych Online)
            {
                PT("Opponent Notes", "If unchecked, opponent notes get hidden.", () => P.OpponentStrums, v => { P.OpponentStrums = v; HighscoreManager.Data.OpponentStrums = v; }),
                PT("Disable Reset Button", "If checked, pressing Reset won't do anything.", () => P.NoReset, v => { P.NoReset = v; HighscoreManager.Data.NoReset = v; }),
                PT("Sustains as One Note", "If checked, Hold Notes can't be pressed if you miss, and count as a single Hit/Miss. Uncheck this if you prefer the old Input System.", () => P.GuitarHeroSustains, v => { P.GuitarHeroSustains = v; HighscoreManager.Data.GuitarHeroSustains = v; }),
                PP("Hitsound Volume", "Funny notes does \"Tick!\" when you hit them.", () => P.HitsoundVolume, v => { P.HitsoundVolume = v; HighscoreManager.Data.HitsoundVolume = v; Audio.PlaySound("psych/hitsound", v); }),
                PN("Rating Offset", "Changes how late/early you have to hit for a \"Sick!\". Higher values mean you have to hit later.", () => P.RatingOffset, v => { P.RatingOffset = (int)v; HighscoreManager.Data.RatingOffset = (int)v; }, -30, 30, 1, v => $"{(int)v} MS"),
                PN("Sick! Hit Window", "Changes the amount of time you have for hitting a \"Sick!\" in milliseconds.", () => P.SickWindow, v => P.SickWindow = v, 15, 45, 0.1f, v => $"{v:0.0} MS", 1f),
                PN("Good Hit Window", "Changes the amount of time you have for hitting a \"Good\" in milliseconds.", () => P.GoodWindow, v => P.GoodWindow = v, 15, 90, 0.1f, v => $"{v:0.0} MS", 1f),
                PN("Bad Hit Window", "Changes the amount of time you have for hitting a \"Bad\" in milliseconds.", () => P.BadWindow, v => P.BadWindow = v, 15, 135, 0.1f, v => $"{v:0.0} MS", 1f),
                PN("Safe Frames", "Changes how many frames you have for hitting a note earlier or late.", () => P.SafeFrames, v => P.SafeFrames = v, 2, 10, 0.1f, v => $"{v:0.0}", 0.5f),
                PT("Disable Note Modchart", "If checked, modcharts can't move your notes (Psych Online).", () => P.DisableStrumMovement, v => P.DisableStrumMovement = v),
                PT("Modchart Skin Changes", "If checked, modcharts may change your note skin (Psych Online).", () => P.ModchartSkinChanges, v => P.ModchartSkinChanges = v),
                PT("Disable Lag Detection", "If checked, lag spikes won't pause / resync the song (Psych Online).", () => P.DisableLagDetection, v => P.DisableLagDetection = v),
                PT("Disable Character Sounds", "If checked, characters won't play their animation sounds (Psych Online).", () => P.DisableAnimAudio, v => P.DisableAnimAudio = v),
                PC("Pause Screen Song", "What song do you prefer for the Pause Screen? (Psych songs)", new[] { "None", "Breakfast", "Tea Time" }, () => P.PauseMusic, v => P.PauseMusic = v),
                PT("Check for Updates", "On Release builds, turn this on to check for updates when you start the game.", () => P.CheckForUpdates, v => P.CheckForUpdates = v),
#if !XBOX_UWP
                PT("Discord Rich Presence", "Uncheck this to prevent accidental leaks, it will hide the Application from your \"Playing\" box on Discord.", () => P.DiscordRPC, v => { P.DiscordRPC = v; if (!v) Game.Discord?.ClearPresence(); }),
#endif
                PBack(),
            };
            case 2: return new List<PrefItem>   // VisualsUISubState
            {
                PC("Note Splashes", "Select your prefered Note Splash variation or turn it off.", new[] { "Psych", "Vanilla", "Disabled" }, () => P.SplashSkin, v => { P.SplashSkin = v; HighscoreManager.Data.SplashSkin = v; }),
                PP("Note Splash Opacity", "How much transparent should the Note Splashes be.", () => P.SplashAlpha, v => P.SplashAlpha = v),
                PP("Hold Splash Opacity", "How much transparent should the Note Hold Splashes be (Psych Online).", () => P.HoldSplashAlpha, v => P.HoldSplashAlpha = v),
                PP("Trail Note Opacity", "How much transparent should the hold note trails be (Psych Online).", () => P.HoldAlpha, v => P.HoldAlpha = v),
                PP("Note Underlay Opacity", "If higher than 0%, an underlay will be displayed behind player notes.", () => P.NoteUnderlayOpacity, v => P.NoteUnderlayOpacity = v),
                PT("Hide HUD", "If checked, hides most HUD elements.", () => P.HideHud, v => { P.HideHud = v; HighscoreManager.Data.HideHud = v; }),
                PC("Time Bar", "What should the Time Bar display?", new[] { "Time Left", "Time Elapsed", "Song Name", "Disabled" }, () => P.TimeBarType, v => P.TimeBarType = v),
                PP("Health Bar Opacity", "How much transparent should the health bar and icons be.", () => P.HealthBarAlpha, v => { P.HealthBarAlpha = v; HighscoreManager.Data.HealthBarAlpha = v; }),
                PT("Score Text Zoom on Hit", "If unchecked, disables the Score text zooming everytime you hit a note.", () => P.ScoreZoom, v => { P.ScoreZoom = v; HighscoreManager.Data.ScoreZoom = v; }),
                PT("Camera Shakes", "If unchecked, the camera won't shake (Psych Online).", () => P.CamShakes, v => P.CamShakes = v),
                PT("Camera Tilt", "If unchecked, the camera won't tilt (Psych Online).", () => P.CamAngles, v => P.CamAngles = v),
                PT("Camera Movement", "If unchecked, the camera won't follow the singing character (Psych Online).", () => P.CamMovement, v => P.CamMovement = v),
                PT("Rating Color", "If checked, rating popups are coloured by the rating (Psych Online).", () => P.ColorRating, v => P.ColorRating = v),
                PT("Disable Combo Rating", "If checked, the rating sprite (Sick!, Good...) is not shown (Psych Online).", () => P.DisableComboRating, v => P.DisableComboRating = v),
                PT("Disable Combo Counter", "If checked, the combo numbers are not shown (Psych Online).", () => P.DisableComboCounter, v => P.DisableComboCounter = v),
                PT("Show Note Timing", "If checked, the hit timing in ms is shown next to the rating (Psych Online).", () => P.ShowNoteTiming, v => P.ShowNoteTiming = v),
                PT("Show FPS Counter", "If unchecked, hides the FPS Counter.", () => HighscoreManager.Data.FPSCounter, v => { HighscoreManager.Data.FPSCounter = v; P.ShowFPS = v; }),
                PBack(),
            };
            case 3: return new List<PrefItem>   // GraphicsSettingsSubState
            {
                PT("Low Quality", "If checked, disables some background details, decreases loading times and improves performance.", () => P.LowQuality, v => { P.LowQuality = v; HighscoreManager.Data.LowQuality = v; }),
                PT("Anti-Aliasing", "If unchecked, disables anti-aliasing, increases performance at the cost of sharper visuals.", () => P.Antialiasing, v => { P.Antialiasing = v; HighscoreManager.Data.Antialiasing = v; }),
                PT("Shaders", "If unchecked, disables shaders. It's used for some visual effects, and also CPU intensive for weaker PCs.", () => P.Shaders, v => { P.Shaders = v; HighscoreManager.Data.Shaders = v; }),
                PT("Combo Stacking", "If unchecked, Ratings and Combo won't stack, saving on System Memory and making them easier to read.", () => P.ComboStacking, v => P.ComboStacking = v),
                PT("Cache Assets", "When enabled, textures stay loaded between menus and songs so they load much faster (uses more video memory).", () => HighscoreManager.Data.AssetCache,
                    v => { HighscoreManager.Data.AssetCache = v; if (!v) SpriteSheetTextureCache.Purge(); }),
                PBack(),
            };
            case 4: return new List<PrefItem>   // OnlineOptionsState (Psych Online)
            {
                new PrefItem { Name = "Nickname", Desc = "Set your nickname here! (3 to 14 characters)", Kind = PrefKind.Text, Psych = true,
                    GetS = () => PsychOnline.DefaultNickname(), SetS = v => PsychOnline.SetNickname(v) },
                new PrefItem { Name = "Server Address", Desc = "The server that hosts Game Rooms. Set to empty if you want to use the default server (wss://funkin.sniro.boo).", Kind = PrefKind.Text, Psych = true,
                    GetS = () => string.IsNullOrEmpty(P.ServerAddress) ? "DEFAULT" : P.ServerAddress,
                    SetS = v => { v = v.Trim(); if (v.Length > 0 && !v.Contains("://")) v = "ws://" + v + (v.Contains(":") ? "" : ":2567"); P.ServerAddress = v.Length == 0 || v.Equals("default", StringComparison.OrdinalIgnoreCase) ? null : v; } },
                PC("Skin", "Choose your skin here! You play as it in your songs; other players see it in rooms (and can download it from your mod's URL).", SkinChoices(), () => SkinLabel(P.CurrentSkin), v => P.CurrentSkin = SkinFromLabel(v)),
                PA("Skins Menu", "Preview every character skin and note skin, then pick the one you play with.", () => Game.Scenes.ChangeScene(new SkinsScene(() => new OptionsScene()))),
                PA("Mod Browser", "Download mods (and skins) from GameBanana right here.", () => Game.Scenes.ChangeScene(new ModBrowserScene(new OptionsScene()))),
                PT("Disable Automatic Downloads", "If checked, skins of other players and room mods are only downloaded when you confirm.", () => P.DisableAutoDownloads, v => P.DisableAutoDownloads = v),
                PT("Network Chat Notifications", "If checked, a sound plays and a note pops up for new chat messages.", () => P.NotifyOnChatMsg, v => P.NotifyOnChatMsg = v),
                PT("Show Chat In-Game", "If checked, chat messages are shown at the bottom left during a match.", () => P.InGameChat, v => P.InGameChat = v),
                PT("Show Ping", "If checked, everyone's ping is shown in rooms and under the scores in-game.", () => P.ShowPing, v => P.ShowPing = v),
                PN("Nameplate Fade Time", "After how many seconds player nameplates are hidden in-game (0 = hidden, -1 = never hidden).", () => P.NameplateFadeTime, v => P.NameplateFadeTime = v, -1, 60, 1, v => v < 0 ? "NEVER HIDE" : v == 0 ? "HIDDEN" : $"{(int)v} S"),
                PT("Show Funkin Points Counter", "If checked, your Funkin Points are shown in the menus.", () => P.ShowFP, v => P.ShowFP = v),
                PT("Disable Song Comments", "If checked, song comments from other players are hidden.", () => P.DisableSongComments, v => P.DisableSongComments = v),
                PP("Song Comments Opacity", "How visible song comments are during a song.", () => P.MidSongCommentsOpacity, v => P.MidSongCommentsOpacity = v),
                PT("Disable Recording Replays", "If checked, your plays are not recorded as replays.", () => P.DisableReplays, v => P.DisableReplays = v),
                PT("Disable Leaderboard Submiting", "If checked, your scores are not sent to the network leaderboards.", () => P.DisableSubmiting, v => P.DisableSubmiting = v),
                PT("Mute PM Notifications", "If checked, private messages don't notify you.", () => P.DisablePMs, v => P.DisablePMs = v),
                PT("Mute Room Invites", "If checked, room invites don't notify you.", () => P.DisableRoomInvites, v => P.DisableRoomInvites = v),
                PT("Enable SSL Verification", "If checked, the game will check for valid SSL certificates for downloads and rooms (safer, may fail on some servers).", () => P.VerifySSL, v => P.VerifySSL = v),
                PA("Clear Trusted Domains", "Clear the list of all trusted download domains (GameBanana stays trusted).", () => { P.TrustedSources = new List<string> { "https://gamebanana.com/" }; ClientPrefs.Save(); Toast("Trusted domains cleared"); },
                    () => (P.TrustedSources?.Count ?? 0) + " TRUSTED"),
                PA("Account", "Log in to the Psych Online Network with your e-mail (opens the Psych Online menu).", () => Game.Scenes.ChangeScene(new PsychOnlineScene()),
                    () => PsychOnline.LoggedIn ? (PsychOnline.AuthName ?? "LOGGED IN") : "NOT LOGGED IN"),
                PBack(),
            };
        }
        return null;
    }

    private void Toast(string s) { _toast = s; _toastT = 3f; }

    // ---- skins: characters from the Psych folders of installed mods (+ base Pico), like PSO SkinsState (PsychOnline.Skins.cs)
    private static List<string[]> _skinCache;
    private static List<string[]> Skins() => _skinCache ??= PsychOnline.AvailableSkins(refresh: true);
    private static string SkinLabel(string[] s) => PsychOnline.SkinLabel(s);
    private static string[] SkinChoices() => new[] { "None" }.Concat(Skins().Select(SkinLabel)).ToArray();
    private static string[] SkinFromLabel(string label) => label == "None" ? null : Skins().FirstOrDefault(s => SkinLabel(s) == label);

    // ------------------------------------------------------------------ categories
    // index: 0 prefs, 1 gameplay, 2 visuals, 3 graphics, 4 controls, 5 lag, 6 online, 7 mods, 8 save data, 9 exit
    private void OpenCat(int i)
    {
        _vsPrefs ??= _prefItems;
        i = BaseCatIndex(i);   // mod pages (OptionsScene.ModOptions.cs) shift the visible list
        if (i < 0) { _prefPage = 10; _prefItems = BuildQtOptionsPage(); OpenPrefPage(); return; }
        switch (i)
        {
            case 0: _prefPage = 0; _prefItems = _vsPrefs; OpenPrefPage(); break;
            case 1: case 2: case 3: _prefPage = i; _prefItems = BuildPsychPage(i); if (i == 2) PsychAddVisualExtras(_prefItems); OpenPrefPage(); break;
            case 4: _inControls = true; _controlIndex = 0; _gpBindIndex = 0; _controlOnTab = false; break;
            case 5: EnterCalibration(); break;
            case 6: _prefPage = 4; _skinCache = null; _prefItems = BuildPsychPage(4); OpenPrefPage(); break;
            case 7: Game.Scenes.ChangeScene(new ModsScene()); break;
            case 8: _inSaveData = true; _prefIndex = 0; break;
            default: Game.Scenes.ChangeScene(new MainMenuScene()); break;
        }
    }
    private void OpenPrefPage() { _inSubMenu = true; _prefIndex = 0; _prefToggleTime = new float[_prefItems.Count]; }

    private void UpdateCategories()
    {
        int n = Cats.Length;
        var mp = DevNoMouseO ? new Vector2(-1, -1) : MouseVirtual;
        bool moved = _lastMouseO.X >= 0 && (mp - _lastMouseO).LengthSquared() > 0.5f; _lastMouseO = mp;
        int hit = _fm.Hit(mp, CatRows());
        if (moved && hit >= 0 && hit != _categoryIndex) { _categoryIndex = hit; Audio.PlaySound("scrollMenu"); }
        if (Input.ScrollDelta != 0) { _categoryIndex = Math.Clamp(_categoryIndex + (Input.ScrollDelta > 0 ? -1 : 1), 0, n - 1); Audio.PlaySound("scrollMenu"); }
        if (Input.UpPressed) { _categoryIndex = (_categoryIndex - 1 + n) % n; Audio.PlaySound("scrollMenu"); }
        if (Input.DownPressed) { _categoryIndex = (_categoryIndex + 1) % n; Audio.PlaySound("scrollMenu"); }
        if (Input.ConfirmPressed || (Input.MouseLeftPressed && hit >= 0))
        {
            if (hit >= 0 && Input.MouseLeftPressed) _categoryIndex = hit;
            Audio.PlaySound("confirmMenu");
            OpenCat(_categoryIndex);
            return;
        }
        if (Input.BackPressed) { Audio.PlaySound("cancelMenu"); Game.Scenes.ChangeScene(new MainMenuScene()); }
    }

    /// <summary>Mouse on preference rows: hover selects, click = confirm (toggle / action) or +1 on values, wheel = scroll.</summary>
    private int PrefMouse(out bool click)
    {
        click = false;
        var mp = DevNoMouseO ? new Vector2(-1, -1) : MouseVirtual;
        bool moved = _lastMouseO.X >= 0 && (mp - _lastMouseO).LengthSquared() > 0.5f; _lastMouseO = mp;
        int hit = _fm.Hit(mp, PrefRows());
        if (moved && hit >= 0 && hit != _prefIndex) { _prefIndex = hit; Audio.PlaySound("scrollMenu", 0.4f); }
        if (Input.MouseLeftPressed && hit >= 0) { _prefIndex = hit; click = true; }
        return hit;
    }

    // ------------------------------------------------------------------ text editor
    private void OpenEditor(string q, string hint, string buf, Action<string> done) { _editing = true; _editQ = q; _editHint = hint; _editBuf = buf ?? ""; _editChar = 0; _editDone = done; }

    private void UpdateEditor()
    {
        var skb = SystemKeyboard.Poll(this, _editQ?.TrimEnd(':'), ref _editBuf, 100);
        if (skb == SystemKeyboard.State.Busy) return;
        bool skbOk = skb == SystemKeyboard.State.Done, skbCancel = skb == SystemKeyboard.State.Cancelled;
        bool up = Input.IsPressed(Keys.Up) || Input.IsGamePadPressed(Buttons.DPadUp), down = Input.IsPressed(Keys.Down) || Input.IsGamePadPressed(Buttons.DPadDown);
        if (up || down)
        {
            _editChar = (_editChar + (up ? 1 : EditChars.Length - 1)) % EditChars.Length;
            if (_editBuf.Length == 0) _editBuf = "a";
            _editBuf = _editBuf.Substring(0, _editBuf.Length - 1) + EditChars[_editChar];
        }
        if (Input.IsGamePadPressed(Buttons.DPadRight) || Input.IsPressed(Keys.Right)) _editBuf += EditChars[_editChar];
        if (Input.IsGamePadPressed(Buttons.DPadLeft) || Input.IsGamePadPressed(Buttons.X) || Input.IsPressed(Keys.Back) || Input.IsPressed(Keys.Left))
            if (_editBuf.Length > 0) _editBuf = _editBuf.Substring(0, _editBuf.Length - 1);
        var k = Input.GetAnyKeyPressed();
        if (k.HasValue)
        {
            var key = k.Value; bool shift = Input.IsHeld(Keys.LeftShift) || Input.IsHeld(Keys.RightShift);
            if (key >= Keys.D0 && key <= Keys.D9) _editBuf += (char)('0' + (key - Keys.D0));
            else if (key >= Keys.NumPad0 && key <= Keys.NumPad9) _editBuf += (char)('0' + (key - Keys.NumPad0));
            else if (key == Keys.OemPeriod || key == Keys.Decimal) _editBuf += '.';
            else if (key >= Keys.A && key <= Keys.Z) _editBuf += (char)((shift ? 'A' : 'a') + (key - Keys.A));
            else if (key == Keys.OemMinus) _editBuf += shift ? '_' : '-';
            else if (key == Keys.OemQuestion) _editBuf += '/';
            else if (key == Keys.OemSemicolon) _editBuf += ':';
            else if (key == Keys.Space) _editBuf += ' ';
        }
        if (skbOk || Input.IsPressed(Keys.Enter) || Input.IsGamePadPressed(Buttons.A) || Input.IsGamePadPressed(Buttons.Start))
        { _editing = false; _editDone?.Invoke(_editBuf); ClientPrefs.Save(); Audio.PlaySound("confirmMenu"); }
        else if (skbCancel || Input.IsPressed(Keys.Escape) || Input.IsGamePadPressed(Buttons.B)) { _editing = false; Audio.PlaySound("cancelMenu"); }
    }

    // ------------------------------------------------------------------ rows
    private List<FunkMenu.Row> CatRows()
    {
        var l = new List<FunkMenu.Row>();
        foreach (var c in Cats) { var ic = CatStyle(c.name).icon; l.Add(new FunkMenu.Row { Label = c.name, Art = ic != null ? "icon:" + ic : null, Desc = c.desc, Color = c.name == "EXIT" ? FunkUI.Paper : (Color?)null }); }
        return l;
    }

    private List<FunkMenu.Row> PrefRows()
    {
        var l = new List<FunkMenu.Row>();
        if (_prefItems == null) return l;
        for (int i = 0; i < _prefItems.Count; i++)
        {
            var it = _prefItems[i];
            var r = new FunkMenu.Row { Label = it.Name.ToUpperInvariant(), Desc = it.Desc, Badge = it.PcOnly ? "PC" : null };
            switch (it.Kind)
            {
                case PrefKind.Toggle: r.Kind = FunkMenu.Kind.Toggle; r.On = it.GetB(); break;
                case PrefKind.Percent:
                {
                    float v = it.GetF();
                    r.Kind = FunkMenu.Kind.Slider; r.Value = (int)Math.Round(v * 100) + "%";
                    r.Frac = it.Max > it.Min ? (v - it.Min) / (it.Max - it.Min) : 0f;
                    break;
                }
                case PrefKind.Number:
                {
                    float v = it.GetF();
                    r.Kind = FunkMenu.Kind.Choice; r.Value = it.Fmt != null ? it.Fmt(v) : v.ToString();
                    break;
                }
                case PrefKind.Choice: r.Kind = FunkMenu.Kind.Choice; r.Value = (it.GetS?.Invoke() ?? "").ToUpperInvariant(); break;
                case PrefKind.Text: r.Kind = FunkMenu.Kind.Button; r.Value = it.GetS?.Invoke(); r.Art = "@pencil"; break;
                case PrefKind.Action: r.Kind = FunkMenu.Kind.Button; r.Value = it.GetS?.Invoke(); r.Art = "@join"; break;
                case PrefKind.Back: r.Label = "BACK"; r.Art = "@back"; r.Color = FunkUI.Paper; r.Desc = "Back to the options menu."; break;
            }
            l.Add(r);
        }
        return l;
    }

    private List<FunkMenu.Row> SaveRows() => new()
    {
        new FunkMenu.Row { Label = "CLEAR SAVE DATA", Art = "@d_bang", Desc = "Deletes all your scores, ranks and progress. This can't be undone!", Color = FunkUI.Red },
        new FunkMenu.Row { Label = "EXIT", Art = "@back", Desc = "Back to the options menu.", Color = FunkUI.Paper },
    };

    private List<FunkMenu.Row> ControlRows(out int sel)
    {
        var l = new List<FunkMenu.Row>();
        var input = Input;
        bool waiting = _waitingForKey || _waitingForButton;
        if (_controlTab == 0)
        {
            l.Add(new FunkMenu.Row { Kind = FunkMenu.Kind.Header, Label = "NOTES", Color = FunkUI.Yellow });
            for (int i = 0; i < _controlNames.Length; i++)
            {
                if (i == 4) l.Add(new FunkMenu.Row { Kind = FunkMenu.Kind.Header, Label = "UI", Color = FunkUI.Yellow });
                var r = new FunkMenu.Row { Label = _controlNames[i].ToUpperInvariant(), Tag = i };
                if (i < 8)
                {
                    r.Kind = FunkMenu.Kind.Cells; r.CellW = 170f;
                    r.Cells = new();
                    for (int k = 0; k < 2; k++) r.Cells.Add((KeyName(i < 4 ? (k == 0 ? input.NoteKeysAlt[i] : input.NoteKeysArrow[i]) : KbRow(i)[k]), null));
                    r.CellSel = _kbCol; r.CellWaiting = _waitingForKey && _rebindTarget == i;
                    r.Art = null;
                    r.Desc = i < 4 ? "Left / Right picks the slot, " + (ButtonPrompts.UseController(Input) ? "A" : "Enter") + " rebinds it. A key used by another lane is swapped."
                                   : "Menu / pause keys. Enter and Escape always work too.";
                }
                else { r.Art = i == 8 ? "@d_spiral" : "@back"; r.Color = i == 9 ? FunkUI.Paper : (Color?)null; r.Desc = i == 8 ? "Put every key back to the default." : "Back to the options menu."; }
                l.Add(r);
            }
            sel = l.FindIndex(r => r.Tag is int t && t == _controlIndex);
        }
        else
        {
            string[] nav = { "Confirm", "Cancel", "Pause", "Switch Char" };
            Buttons[] navB = { input.ConfirmButton, input.CancelButton, input.PauseButton, input.SwitchCharButton };
            l.Add(new FunkMenu.Row { Kind = FunkMenu.Kind.Header, Label = "NOTES", Color = FunkUI.Yellow, Value = "D-PAD  /  FACE  /  BUMPER-TRIGGER" });
            for (int r0 = 0; r0 < 10; r0++)
            {
                if (r0 == 4) l.Add(new FunkMenu.Row { Kind = FunkMenu.Kind.Header, Label = "MENU", Color = FunkUI.Yellow });
                var r = new FunkMenu.Row { Tag = r0, Label = (r0 < 4 ? _gpBindNames[r0] : r0 < 8 ? nav[r0 - 4] : r0 == 8 ? "Default Buttons" : "Exit").ToUpperInvariant() };
                if (r0 < 8)
                {
                    r.Kind = FunkMenu.Kind.Cells; r.CellW = 128f; r.Cells = new();
                    if (r0 < 4) foreach (var b in new[] { input.NoteButtons[r0], input.NoteFaceButtons[r0], input.NoteTriggerButtons[r0] }) r.Cells.Add((FormatButtonName(b), b));
                    else r.Cells.Add((FormatButtonName(navB[r0 - 4]), navB[r0 - 4]));
                    r.CellSel = r0 < 4 ? _gpBindCol : 0; r.CellWaiting = _waitingForButton && _gpBindIndex == r0;
                    r.Art = null;
                    r.Desc = r0 < 4 ? "Three buttons per lane. Left / Right picks the slot, A rebinds it." : "Menu buttons.";
                }
                else { r.Art = r0 == 8 ? "@d_spiral" : "@back"; r.Color = r0 == 9 ? FunkUI.Paper : (Color?)null; r.Desc = r0 == 8 ? "Put every button back to the default." : "Back to the options menu."; }
                l.Add(r);
            }
            sel = l.FindIndex(r => r.Tag is int t && t == _gpBindIndex);
        }
        if (waiting) sel = Math.Max(0, sel);
        return l;
    }

    // drawing: OptionsScene.Theme.cs

    private Texture2D LoadOptTex(string path)
    {
        var t = Assets.LoadTexture(path);
        return t == null || t == Assets.Pixel ? null : t;
    }
}
