using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Newtonsoft.Json.Linq;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Main menu > Multiplayer: Local (2 players on one machine) / Online (host or join a relay; PC and Xbox cross-play).
/// Everything is navigable with a controller (D-pad/stick + A/B/X) or the keyboard.
/// Dev hooks: FNF_START_SCENE=multiplayer, FNF_MP_AUTO=host|join|local, FNF_MP_ADDR, FNF_MP_PORT, FNF_MP_SONG=<id>
/// (host / local: skip Freeplay), FNF_MP_DIFF, FNF_MP_BOT=1 (auto-play the local side).
/// </summary>
public class MultiplayerScene : Scene
{
    private enum Page { Root, Local, OnlineChoice, Online, Edit, Connecting, Lobby }
    private Page _page = Page.Root;
    private int _sel;
    private Texture2D _bg;
    private string _status = "";
    private float _statusTimer, _time;
    private Task<bool> _connectTask;
    private bool _connectAsHost;
    // address editor
    private string _editBuf = ""; private int _editField; private int _editCharIdx;
    private const string EditChars = "0123456789.abcdefghijklmnopqrstuvwxyz-:";
    private static string _address = "127.0.0.1";
    private static int _port = MultiplayerSession.DefaultPort;
    private static string _room = "FNF";
    private static bool _autoDone;

    public override void Load()
    {
        Game.Discord?.SetMenuState("Multiplayer");
        Input.GameplayMode = false;
        MultiplayerSession.CharOverride = null;
        if (MultiplayerSession.Mode == MultiplayerSession.MpMode.Online && MultiplayerSession.Net != null && MultiplayerSession.Net.Connected) _page = Page.Lobby;
        else if (MultiplayerSession.Mode == MultiplayerSession.MpMode.Local) _page = Page.Local;
        else { MultiplayerSession.Leave(); DefaultDevices(); }
        var a = Environment.GetEnvironmentVariable("FNF_MP_ADDR"); if (!string.IsNullOrEmpty(a)) _address = a;
        if (int.TryParse(Environment.GetEnvironmentVariable("FNF_MP_PORT"), out var pt)) _port = pt;
        if (!Audio.MusicPlaying) Audio.PlayMusic("music/freakyMenu", true);
    }

    private static void DefaultDevices()
    {
        MultiplayerInput.Poll();
        bool p1 = MultiplayerInput.Connected(MpDevice.Pad1), p2 = MultiplayerInput.Connected(MpDevice.Pad2);
#if XBOX_UWP
        MultiplayerInput.Devices[0] = MpDevice.Pad1; MultiplayerInput.Devices[1] = MpDevice.Pad2;
#else
        if (p1 && p2) { MultiplayerInput.Devices[0] = MpDevice.Pad1; MultiplayerInput.Devices[1] = MpDevice.Pad2; }
        else if (p1) { MultiplayerInput.Devices[0] = MpDevice.KeyboardArrows; MultiplayerInput.Devices[1] = MpDevice.Pad1; }
        else { MultiplayerInput.Devices[0] = MpDevice.KeyboardArrows; MultiplayerInput.Devices[1] = MpDevice.KeyboardWASD; }
#endif
    }

    private List<string> Items()
    {
        switch (_page)
        {
            case Page.Root: return new List<string> { "LOCAL", "ONLINE", "BACK" };
            case Page.OnlineChoice: return new List<string> { "PSYCH ONLINE", "LAN / RELAY", "BACK" };
            case Page.Local: return new List<string> {
                "Player 1 (BF side): < " + MultiplayerInput.Name(MultiplayerInput.Devices[0]) + (MultiplayerInput.Connected(MultiplayerInput.Devices[0]) ? "" : " (not connected)") + " >",
                "Player 2 (Opponent side): < " + MultiplayerInput.Name(MultiplayerInput.Devices[1]) + (MultiplayerInput.Connected(MultiplayerInput.Devices[1]) ? "" : " (not connected)") + " >",
                "Choose song (Freeplay)", "Back" };
            case Page.Online: return new List<string> { "Host game", "Join game", "Address: " + _address, "Port: " + _port, "Room: " + _room, "Back" };
            case Page.Lobby:
                return MultiplayerSession.IsHost
                    ? new List<string> { MultiplayerSession.PeerPresent ? "Choose song (Freeplay)" : "Choose song (waiting for a player...)", "Leave room" }
                    : new List<string> { "Leave room" };
            default: return new List<string>();
        }
    }

    private void SetStatus(string s) { _status = s; _statusTimer = 6f; Console.WriteLine("[MP] " + s); }

    public override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _time += dt; if (_statusTimer > 0) _statusTimer -= dt;
        AnimUpdate(dt);
        MultiplayerInput.Frame++; MultiplayerInput.Poll();
        DevAuto();
        PollNet();
        DevKick();
        if (_page == Page.Connecting) { UpdateConnecting(); if (Input.BackPressed) { MultiplayerSession.Leave(); _page = Page.Online; } return; }
        if (_page == Page.Edit) { UpdateEdit(); return; }

        var items = Items();
        if (items.Count == 0) return;
        if (Input.UpPressed) { _sel = (_sel + items.Count - 1) % items.Count; Audio.PlaySound("scrollMenu"); }
        if (Input.DownPressed) { _sel = (_sel + 1) % items.Count; Audio.PlaySound("scrollMenu"); }
        _sel = Math.Clamp(_sel, 0, items.Count - 1);
        int lr = Input.LeftPressed ? -1 : Input.RightPressed ? 1 : 0;
        // card pages (Root / Online choice): Left/Right switch between the two big cards
        if (IsCardPage && lr != 0) { _sel = _sel >= 2 ? (lr < 0 ? 0 : 1) : (_sel + 1) % 2; Audio.PlaySound("scrollMenu"); lr = 0; }
        // mouse: hover selects (when it moved), click activates; wheel = Left/Right on value rows
        var mp = DevNoMouse ? new Vector2(-1, -1) : MouseVirtual;
        int hit = HitItem(mp);
        bool moved = _lastMouse.X >= 0 && (mp - _lastMouse).LengthSquared() > 0.5f;
        _lastMouse = mp;
        if (moved && hit >= 0 && hit != _sel) { _sel = hit; Audio.PlaySound("scrollMenu"); }
        if (Input.ScrollDelta != 0 && hit >= 0 && hit == _sel && (_page == Page.Local && _sel <= 1 || _page == Page.Online && _sel == 3)) lr = Input.ScrollDelta > 0 ? 1 : -1;
        if (Input.MouseLeftPressed && hit >= 0)
        {
            _sel = hit;
            if (_page == Page.Local && hit <= 1) lr = 1;           // click a player row = next device
            else if (_page == Page.Online && hit == 3) lr = 1;     // click the port row = +1
            else { Confirm(hit); return; }
        }

        if (_page == Page.Local)
        {
            // "Press Y on a controller" assigns it to the highlighted player row
            int pad = MultiplayerInput.PadJustPressed(Buttons.Y);
            if (pad >= 0 && _sel <= 1) { MultiplayerInput.Devices[_sel] = MpDevice.Pad1 + pad; Audio.PlaySound("scrollMenu"); }
            if (lr != 0 && _sel <= 1)
            {
                int n = Enum.GetValues(typeof(MpDevice)).Length;
                MultiplayerInput.Devices[_sel] = (MpDevice)(((int)MultiplayerInput.Devices[_sel] + lr + n) % n);
                Audio.PlaySound("scrollMenu");
            }
        }
        if (_page == Page.Online && lr != 0 && _sel == 3) _port = Math.Clamp(_port + lr, 1024, 65535);

        if (Input.BackPressed) { Back(); return; }
        if (Input.ConfirmPressed) Confirm(_sel);
    }

    private void Back()
    {
        Audio.PlaySound("cancelMenu");
        switch (_page)
        {
            case Page.Root: MultiplayerSession.Leave(); Game.Scenes.ChangeScene(new MainMenuScene()); break;
            case Page.Local: MultiplayerSession.Mode = MultiplayerSession.MpMode.None; _page = Page.Root; _sel = 0; break;
            case Page.Online: _page = Page.OnlineChoice; _sel = 1; break;
            case Page.OnlineChoice: _page = Page.Root; _sel = 1; break;
            case Page.Lobby: MultiplayerSession.Leave(); _page = Page.Online; _sel = 0; break;
        }
    }

    private void Confirm(int i)
    {
        Audio.PlaySound("confirmMenu");
        switch (_page)
        {
            case Page.Root:
                if (i == 0) { _page = Page.Local; _sel = 0; MultiplayerSession.Mode = MultiplayerSession.MpMode.Local; }
                else if (i == 1) { _page = Page.OnlineChoice; _sel = 0; }
                else Back();
                break;
            case Page.OnlineChoice:
                if (i == 0) Game.Scenes.ChangeScene(new PsychOnlineScene());   // Psych Online (Funkin-Online-Server) - PsychOnlineScene.cs
                else if (i == 1) { _page = Page.Online; _sel = 0; }
                else Back();
                break;
            case Page.Local:
                if (i == 2)
                {
                    if (MultiplayerInput.Devices[0] == MultiplayerInput.Devices[1]) { SetStatus("Both players use the same device - pick another one for Player 2"); return; }
                    MultiplayerSession.Mode = MultiplayerSession.MpMode.Local;
                    GoChooseSong();
                }
                else if (i == 3) Back();
                break;
            case Page.Online:
                if (i == 0) StartConnect(true);
                else if (i == 1) StartConnect(false);
                else if (i == 2) { _editField = 0; _editBuf = _address; _editCharIdx = 0; _page = Page.Edit; }
                else if (i == 4) { _editField = 2; _editBuf = _room; _editCharIdx = 0; _page = Page.Edit; }
                else if (i == 5) Back();
                break;
            case Page.Lobby:
                if (MultiplayerSession.IsHost && i == 0)
                {
                    if (!MultiplayerSession.PeerPresent) { SetStatus("Wait for the other player to join first"); return; }
                    GoChooseSong();
                }
                else Back();
                break;
        }
    }

    private void GoChooseSong()
    {
        string devSong = Environment.GetEnvironmentVariable("FNF_MP_SONG");
        if (!string.IsNullOrEmpty(devSong))
        {
            Game.Scenes.ChangeScene(new PlayScene(PlayStateParams.ForFreeplay(devSong, Environment.GetEnvironmentVariable("FNF_MP_DIFF") ?? "normal")));
            return;
        }
        Game.Scenes.ChangeScene(new FreeplayScene());   // character + song chosen in Freeplay; PlayScene reads MultiplayerSession.Mode
    }

    private void StartConnect(bool host)
    {
        _connectAsHost = host;
        string addr = _address;
        if (host)
        {
            if (!MultiplayerSession.HostRelay(_port)) { SetStatus($"Could not open port {_port} (already in use?)"); return; }
            addr = "127.0.0.1";
        }
        MultiplayerSession.Mode = MultiplayerSession.MpMode.Online;
        MultiplayerSession.Room = _room;
        MultiplayerSession.Net = new NetClient();
        _connectTask = MultiplayerSession.Net.ConnectAsync(addr, _port, _room, MultiplayerSession.LocalName);
        _page = Page.Connecting;
        SetStatus(host ? $"Hosting on {MultiplayerSession.LocalIPv4()}:{_port} ..." : $"Connecting to {addr}:{_port} ...");
    }

    private void UpdateConnecting()
    {
        if (_connectTask == null || !_connectTask.IsCompleted) return;
        bool ok = _connectTask.Status == TaskStatus.RanToCompletion && _connectTask.Result;
        _connectTask = null;
        if (!ok) { SetStatus("Connection failed: " + (MultiplayerSession.Net?.Error ?? "?")); MultiplayerSession.Leave(); _page = Page.Online; return; }
        // wait for "welcome" in PollNet
    }

    private void PollNet()
    {
        var net = MultiplayerSession.Net;
        if (net == null || MultiplayerSession.Mode != MultiplayerSession.MpMode.Online) return;
        while (net.Inbox.TryPeek(out var m))
        {
            if (HandleCommon(m, this)) return;   // switched scene: leave the rest of the queue for PlayScene
            net.Inbox.TryDequeue(out _);
            switch ((string)m["t"])
            {
                case "welcome":
                    MultiplayerSession.IsHost = (bool?)m["host"] ?? false;
                    MultiplayerSession.PeerName = (string)m["peer"]; MultiplayerSession.PeerPlatform = (string)m["peerPlatform"];
                    MultiplayerSession.PeerPresent = MultiplayerSession.PeerName != null;
                    _page = Page.Lobby; _sel = 0;
                    SetStatus($"Joined room {(string)m["room"]} as {(MultiplayerSession.IsHost ? "host (Boyfriend side)" : "guest (Opponent side)")}");
                    break;
                case "error": SetStatus("Server: " + (string)m["msg"]); MultiplayerSession.Leave(); _page = Page.Online; return;
            }
        }
    }

    /// <summary>Messages every multiplayer menu reacts to. Returns true when the scene was switched.</summary>
    public static bool HandleCommon(JObject m, Scene from)
    {
        string t = (string)m["t"];
        var game = FNFGame.Instance;
        switch (t)
        {
            case "peer_join":
                MultiplayerSession.PeerPresent = true; MultiplayerSession.PeerName = (string)m["name"]; MultiplayerSession.PeerPlatform = (string)m["platform"];
                Console.WriteLine($"[MP] peer joined: {MultiplayerSession.PeerName} ({MultiplayerSession.PeerPlatform})");
                return false;
            case "peer_left": case "leave": case "disconnected":
                MultiplayerSession.PeerPresent = false;
                if (t == "disconnected") { Console.WriteLine("[MP] disconnected from relay"); }
                if (t == "peer_left" && !MultiplayerSession.IsHost) { MultiplayerSession.IsHost = (bool?)m["host"] ?? true; } // remaining player becomes host
                return false;
            case "song":
                if (MultiplayerSession.IsHost) return false;
                MultiplayerSession.Net.Inbox.TryDequeue(out _);
                MultiplayerSession.CharOverride = (string)m["char"];
                Console.WriteLine($"[MP] host picked {(string)m["song"]} ({(string)m["diff"]}, char {MultiplayerSession.CharOverride})");
                game.Scenes.ChangeScene(new PlayScene(PlayStateParams.ForFreeplay((string)m["song"], (string)m["diff"] ?? "normal")));
                return true;
        }
        return false;
    }

    private void DevAuto()
    {
        if (_autoDone) return;
        string auto = Environment.GetEnvironmentVariable("FNF_MP_AUTO");
        if (string.IsNullOrEmpty(auto)) { _autoDone = true; return; }
        if (_time < 1.0f) return;
        _autoDone = true;
        if (auto == "host") { _page = Page.Online; StartConnect(true); }
        else if (auto == "join") { _page = Page.Online; StartConnect(false); }
        else if (auto == "local") { MultiplayerSession.Mode = MultiplayerSession.MpMode.Local; GoChooseSong(); }
        if (auto == "host") _devHostWaitSong = true;
    }
    private bool _devHostWaitSong;

    private void UpdateEdit()
    {
        var skb = SystemKeyboard.Poll(this, _editField == 0 ? "Server address" : "Room code", ref _editBuf, 64);
        if (skb == SystemKeyboard.State.Busy) return;
        bool skbOk = skb == SystemKeyboard.State.Done, skbCancel = skb == SystemKeyboard.State.Cancelled;
        // Controller: Up/Down = change character, Right = next char, Left/X = delete, A = done, B = cancel. Keyboard typing works too.
        bool up = Input.IsPressed(Keys.Up) || Input.IsGamePadPressed(Buttons.DPadUp), down = Input.IsPressed(Keys.Down) || Input.IsGamePadPressed(Buttons.DPadDown);
        if (up || down)
        {
            _editCharIdx = (_editCharIdx + (up ? 1 : EditChars.Length - 1)) % EditChars.Length;
            if (_editBuf.Length == 0) _editBuf = "0";
            _editBuf = _editBuf.Substring(0, _editBuf.Length - 1) + EditChars[_editCharIdx];
        }
        if (Input.IsGamePadPressed(Buttons.DPadRight) || Input.IsPressed(Keys.Right)) { _editBuf += EditChars[_editCharIdx]; }
        if (Input.IsGamePadPressed(Buttons.DPadLeft) || Input.IsGamePadPressed(Buttons.X) || Input.IsPressed(Keys.Back) || Input.IsPressed(Keys.Left))
            if (_editBuf.Length > 0) _editBuf = _editBuf.Substring(0, _editBuf.Length - 1);
        var k = Input.GetAnyKeyPressed();
        if (k.HasValue)
        {
            var key = k.Value;
            if (key >= Keys.D0 && key <= Keys.D9) _editBuf += (char)('0' + (key - Keys.D0));
            else if (key >= Keys.NumPad0 && key <= Keys.NumPad9) _editBuf += (char)('0' + (key - Keys.NumPad0));
            else if (key == Keys.OemPeriod || key == Keys.Decimal) _editBuf += '.';
            else if (key >= Keys.A && key <= Keys.Z) _editBuf += (char)('a' + (key - Keys.A));
            else if (key == Keys.OemMinus) _editBuf += '-';
        }
        if (skbOk || Input.IsPressed(Keys.Enter) || Input.IsGamePadPressed(Buttons.A) || Input.IsGamePadPressed(Buttons.Start))
        {
            if (_editField == 0) _address = _editBuf.Trim().Length > 0 ? _editBuf.Trim() : _address;
            else if (_editField == 2) _room = _editBuf.Trim().Length > 0 ? _editBuf.Trim().ToUpperInvariant() : _room;
            _page = Page.Online;
        }
        else if (skbCancel || Input.IsPressed(Keys.Escape) || Input.IsGamePadPressed(Buttons.B)) _page = Page.Online;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Drawing: hand-made FNF style (FunkUI, same look as the main menu). Tinted menuDesat art + boiling marker doodles,
    // tilted flat-colour posters with game stickers (Root / Online choice), paper strips with health icons / doodles
    // (settings rows), BF-vs-opponent lobby with the health icons, a paper note for help text, black tape for values,
    // prompts and toasts. Funkin bold alphabet for labels, VCR for small info. Native resolution (crisp on TVs).
    // ---------------------------------------------------------------------------------------------------------------
    public override bool NativeResolution => true;
    private static readonly bool DevNoMouse = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FNF_SHOTS")) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FNF_DEVMOUSE")); // dev: FNF_DEVMOUSE tests the mouse path
    private Vector2 _lastMouse = new(-1, -1);
    private readonly float[] _hov = new float[8];
    private Page _animPage = (Page)(-1);
    private float _pageTime, _selTime = 10f;
    private int _lastSel = -1;
    private Texture2D _desat;
    private SpriteSheet _wordSheet;
    private Color _tint = FunkUI.Purple;
    private Vector3 _tintV = new(-1f, 0f, 0f);

    private static readonly Color CLocal = FunkUI.Orange, COnline = FunkUI.Cyan, CPsych = FunkUI.Pink,
        CLan = FunkUI.Green, CBack = FunkUI.Paper, CSong = FunkUI.Yellow, CHost = FunkUI.Yellow, CP1 = FunkUI.Cyan, CP2 = FunkUI.Purple;

    private bool IsCardPage => _page == Page.Root || _page == Page.OnlineChoice;

    private Color Accent => _page switch
    {
        Page.Local => CLocal,
        Page.Online or Page.Edit or Page.Connecting or Page.Lobby => CLan,
        Page.OnlineChoice => COnline,
        _ => FunkUI.Purple,
    };

    private struct RowDef
    {
        public string Label, Value, Sub; public string[] Art; public Color Color; public bool Arrows; public int Status; // 1 ok, -1 bad
    }

    private List<RowDef> Defs()
    {
        RowDef R(string label, Color c, string sub, string value, bool arrows, int status, params string[] art)
            => new RowDef { Label = label, Color = c, Sub = sub, Value = value, Arrows = arrows, Status = status, Art = art };
        string Dev(int i) => MultiplayerInput.Name(MultiplayerInput.Devices[i]);
        int Ok(int i) => MultiplayerInput.Connected(MultiplayerInput.Devices[i]) ? 1 : -1;
        switch (_page)
        {
            case Page.Root: return new() {
                R("LOCAL", CLocal, "Two players, one screen. Keyboard halves or pads.", null, false, 0, "bf1", "dad2"),
                R("ONLINE", COnline, "Psych Online, or LAN / relay rooms (PC + Xbox).", null, false, 0, "s2:pico1", "@globe"),
                R("BACK", CBack, null, null, false, 0, "@back") };
            case Page.OnlineChoice: return new() {
                R("PSYCH ONLINE", CPsych, "Join Psych Online rooms with PC players.", null, false, 0, "s2:darnell1", "s2:nene1"),
                R("LAN / RELAY", CLan, "FNF MonoGame rooms on your network or a relay.", null, false, 0, "bf3", "@antenna"),
                R("BACK", CBack, null, null, false, 0, "@back") };
            case Page.Local: return new() {
                R("PLAYER 1", CP1, "Boyfriend side. Left / Right picks the device.", Dev(0), true, Ok(0), "icon:bf"),
                R("PLAYER 2", CP2, "Opponent side. Left / Right picks the device.", Dev(1), true, Ok(1), "icon:dad"),
                R("CHOOSE SONG", CSong, "Pick the song and character in Freeplay.", null, false, 0, "@mic"),
                R("BACK", CBack, "Back to the multiplayer menu.", null, false, 0, "@back") };
            case Page.Online: return new() {
                R("HOST GAME", CHost, "Open a room on this machine.", null, false, 0, "@crown"),
                R("JOIN GAME", COnline, "Connect to the address below.", null, false, 0, "@join"),
                R("ADDRESS", CLan, "The host's IP address or name.", _address, false, 0, "@pin"),
                R("PORT", CLan, "Left / Right changes the port.", _port.ToString(), true, 0, "@plug"),
                R("ROOM", CLan, "Both players type the same room code.", _room, false, 0, "@key"),
                R("BACK", CBack, "Back to the online menu.", null, false, 0, "@back") };
            case Page.Lobby:
                return MultiplayerSession.IsHost
                    ? new() { R("CHOOSE SONG", CSong, MultiplayerSession.PeerPresent ? "Pick the song in Freeplay - it starts for both." : "Waiting for a player to join...", null, false, 0, "@mic"),
                              R("LEAVE ROOM", CPsych, "Close the room and go back.", null, false, 0, "@leave") }
                    : new() { R("LEAVE ROOM", CPsych, "The host picks the song - it starts here by itself.", null, false, 0, "@leave") };
            default: return new();
        }
    }

    // ------------------------------------------------------------------ layout (logical 1280x720, inside the TV safe area)
    private const float CardW = 430f, CardH = 330f, CardGap = 56f, CardTop = 196f;
    private (float x, float y, float w, float h) ItemRect(int i, int count)
    {
        if (IsCardPage)
        {
            if (i < 2) { float x0 = (FunkUI.W - (2 * CardW + CardGap)) / 2f; return (x0 + i * (CardW + CardGap), CardTop, CardW, CardH); }
            return (FunkUI.SafeX + 6f, 566f, 200f, 60f);   // BACK tape
        }
        float rh = _page == Page.Online ? 50f : 62f, rg = _page == Page.Online ? 17f : 20f;
        float top = _page == Page.Lobby ? 452f : 172f;
        const float rw = 720f;
        return ((FunkUI.W - rw) / 2f + 20f, top + i * (rh + rg), rw, rh);
    }

    /// <summary>Hand placement: tilt / x jitter per item (no perfect grid).</summary>
    private static float Tilt(int i) => FunkUI.Rad((i % 2 == 0 ? -0.7f : 0.6f) + (FunkUI.Hash(i * 5 + 2) - 0.5f) * 0.5f);
    private static float Jitter(int i) => (FunkUI.Hash(i * 11 + 7) - 0.5f) * 22f;

    private int HitItem(Vector2 p)
    {
        if (p.X < 0 || _page == Page.Edit || _page == Page.Connecting) return -1;
        int n = Items().Count;
        for (int i = 0; i < n; i++)
        {
            var r = ItemRect(i, n);
            if (p.X >= r.x && p.X <= r.x + r.w && p.Y >= r.y && p.Y <= r.y + r.h) return i;
        }
        return -1;
    }

    private void AnimUpdate(float dt)
    {
        if (_page != _animPage) { _animPage = _page; _pageTime = 0f; _selTime = 10f; _lastSel = _sel; for (int i = 0; i < _hov.Length; i++) _hov[i] = i == _sel ? 1f : 0f; }
        _pageTime += dt; _selTime += dt;
        if (_sel != _lastSel) { _lastSel = _sel; _selTime = 0f; }
        for (int i = 0; i < _hov.Length; i++) _hov[i] = FunkUI.Approach(_hov[i], i == _sel ? 1f : 0f, 18f, dt);
        var a = Accent.ToVector3();
        if (_tintV.X < 0f) _tintV = a;
        _tintV += (a - _tintV) * (1f - MathF.Exp(-10f * dt));
        _tint = new Color(_tintV);
    }

    private List<ButtonPrompts.Prompt> Prompts()
    {
        var l = new List<ButtonPrompts.Prompt>();
        switch (_page)
        {
            case Page.Edit:
                l.Add(ButtonPrompts.UpDown("CHAR")); l.Add(new ButtonPrompts.Prompt(Buttons.DPadRight, "TYPE", "ADD"));
                l.Add(new ButtonPrompts.Prompt(Buttons.X, "BKSP", "DELETE")); l.Add(ButtonPrompts.Confirm("OK")); l.Add(ButtonPrompts.Back("CANCEL"));
                return l;
            case Page.Connecting: l.Add(ButtonPrompts.Back("CANCEL")); return l;
        }
        l.Add(IsCardPage ? new ButtonPrompts.Prompt(ButtonPrompts.DPadAll, "ARROWS", "PICK") : ButtonPrompts.UpDown("PICK"));
        if (_page == Page.Local && _sel <= 1) { l.Add(ButtonPrompts.LeftRight("DEVICE")); if (ButtonPrompts.UseController(Input)) l.Add(new ButtonPrompts.Prompt(Buttons.Y, "", "ASSIGN PAD")); }
        else if (_page == Page.Online && _sel == 3) l.Add(ButtonPrompts.LeftRight("CHANGE"));
        else l.Add(ButtonPrompts.Confirm("GO!"));
        l.Add(ButtonPrompts.Back("BACK"));
        return l;
    }

    private static readonly FunkUI.Doodle[] _doodles =
    {
        new("@d_star", 90f, 168f, 46f, Color.White, -12f), new("@d_note", 1206f, 190f, 58f, FunkUI.Ink, 10f),
        new("@d_squiggle", 1150f, 560f, 86f, Color.White, -6f), new("@d_spiral", 70f, 470f, 48f, Color.White, 0f),
        new("@d_heart", 1210f, 380f, 42f, FunkUI.Pink, -14f), new("@d_zigzag", 640f, 640f, 76f, FunkUI.Ink, 4f),
        new("@d_bang", 1220f, 640f, 44f, FunkUI.Ink, 8f), new("@d_star", 600f, 40f, 30f, FunkUI.Ink, 20f),
    };

    public override void Draw(SpriteBatch sb)
    {
        _desat ??= LoadTexOpt("menus/main_menu/menuDesat.png");
        _wordSheet ??= SpriteSheet.Load(Game, "menus/main_menu/buttons_port");
        float bop = FunkUI.Bop;
        FunkUI.Begin(sb);
        FunkUI.Fill(0, 0, FunkUI.W, FunkUI.H, _tint);
        FunkUI.Cover(_desat, 1.08f + 0.012f * bop, Vector2.Zero, _tint);
        // big tilted black stripe behind the content (like Freeplay's angled bars)
        FunkUI.Push(640f, 372f, FunkUI.Rad(-4f));
        FunkUI.Fill(-200f, 300f, 1700f, 150f, FunkUI.A(Color.Black, 0.18f));
        FunkUI.Pop();
        FunkUI.DrawDoodles(_doodles, _time, bop, FunkUI.Clamp01(_pageTime / 0.3f));

        DrawHeader();
        var defs = Defs();
        if (_page == Page.Edit) DrawEdit();
        else if (_page == Page.Connecting) DrawConnecting();
        else
        {
            if (_page == Page.Lobby) DrawLobbyInfo(bop);
            for (int i = 0; i < defs.Count; i++) if (i != _sel) DrawItem(i, defs, bop);
            if (_sel >= 0 && _sel < defs.Count) DrawItem(_sel, defs, bop);
            if (!IsCardPage && _sel >= 0 && _sel < defs.Count) DrawHelpNote(defs[_sel].Sub);
        }

        if (_statusTimer > 0 && !string.IsNullOrEmpty(_status))
        {
            float a = Math.Min(1f, _statusTimer) * Math.Min(1f, (6f - _statusTimer) * 6f);
            float slap = FunkUI.EaseOutBack(Math.Min(1f, (6f - _statusTimer) / 0.18f), 2.4f);
            float tw = FunkUI.MeasureVcr(_status, 19f) + 56f;
            float tx = FunkUI.W / 2f, ty = 150f;
            FunkUI.Push(tx, ty, FunkUI.Rad(-1.5f), slap, slap);
            FunkUI.Block(tx - tw / 2f, ty - 18f, tw, 36f, FunkUI.Yellow, 4f, Color.Black, a);
            FunkUI.Text(_status, tx, ty - 10f, 19f, FunkUI.A(FunkUI.Ink, a), 0.5f);
            FunkUI.Pop();
        }
        FunkUI.PromptTape(Input, Prompts(), FunkUI.W - FunkUI.SafeX + 10f, FunkUI.H - 62f, 30f, FunkUI.Clamp01(_pageTime / 0.3f));
        sb.End();
    }

    private Texture2D LoadTexOpt(string path)
    {
        var t = Assets.LoadTexture(path);
        return t == null || t == Assets.Pixel ? null : t;
    }

    private void DrawHeader()
    {
        float ia = FunkUI.EaseOutBack(FunkUI.Clamp01(_pageTime / 0.3f), 1.8f);
        float y = 40f - (1f - ia) * 120f;
        if (_page == Page.Root && _wordSheet?.GetAnimation("multiplayer selected") != null)
        {
            // the main-menu lettering itself
            var size = FunkUI.WordSize(_wordSheet, "multiplayer selected") * 0.5f;
            float cx = FunkUI.SafeX + size.X / 2f - 6f, cy = y + size.Y / 2f;
            FunkUI.Push(cx, cy, FunkUI.Rad(-3f), 1f + 0.03f * FunkUI.Bop, 1f + 0.03f * FunkUI.Bop);
            FunkUI.Word(_wordSheet, "multiplayer selected", cx, cy, 0.5f, _time);
            FunkUI.Pop();
        }
        else
        {
            string title = _page switch
            {
                Page.Local => "LOCAL VERSUS", Page.OnlineChoice => "ONLINE",
                Page.Lobby => "ROOM " + MultiplayerSession.Room, Page.Connecting => "HANG ON", Page.Edit => _editField == 0 ? "ADDRESS" : "ROOM CODE",
                _ => "LAN / RELAY",
            };
            float sc = 0.9f, w = FunkUI.BoldWidth(title, sc);
            float cx = FunkUI.SafeX + w / 2f, cy = y + FunkUI.BoldHeight(sc) / 2f;
            FunkUI.Push(cx, cy, FunkUI.Rad(-2.5f), 1f + 0.03f * FunkUI.Bop, 1f + 0.03f * FunkUI.Bop);
            FunkUI.Bold(title, FunkUI.SafeX, y, sc, 0f, _time);
            FunkUI.Pop();
        }

        // connection tag (top right): paper luggage tag, tilted
        string chip; Color cc;
        if (MultiplayerSession.Mode == MultiplayerSession.MpMode.Online && MultiplayerSession.Net != null && MultiplayerSession.Net.Connected)
        {
            chip = MultiplayerSession.PeerPresent ? "2/2 PLAYERS" : "1/2 PLAYERS"; cc = MultiplayerSession.PeerPresent ? FunkUI.Green : FunkUI.Yellow;
            if (MultiplayerSession.Net.PingMs >= 0) chip += "  " + MultiplayerSession.Net.PingMs + "ms";
        }
        else if (_page == Page.Local) { chip = "SAME SCREEN"; cc = CLocal; }
        else { chip = "OFFLINE"; cc = FunkUI.Paper; }
        float tw = FunkUI.MeasureVcr(chip, 19f) + 36f, tx = FunkUI.W - FunkUI.SafeX - tw / 2f, ty = 66f - (1f - ia) * 120f;
        FunkUI.Push(tx, ty, FunkUI.Rad(3f) + FunkUI.Wiggle(_pageTime - 0.2f, 22f, 6f) * 0.06f);
        FunkUI.ShadowBlock(tx - tw / 2f, ty - 19f, tw, 38f, cc, 4f, 5f, 6f);
        FunkUI.Text(chip, tx, ty - 10f, 19f, FunkUI.Ink, 0.5f);
        FunkUI.Pop();
    }

    private void DrawItem(int i, List<RowDef> defs, float bop)
    {
        var d = defs[i];
        var (rx, ry, rw, rh) = ItemRect(i, defs.Count);
        float h = i < _hov.Length ? _hov[i] : (i == _sel ? 1f : 0f);
        bool sel = i == _sel;
        float intro = FunkUI.EaseOutBack(FunkUI.Clamp01((_pageTime - 0.04f * i) / 0.28f), 1.8f);
        if (intro <= 0f) return;
        var sq = sel ? FunkUI.Squash(_selTime, 0.16f) : Vector2.One;
        float wig = sel ? FunkUI.Wiggle(_selTime, 26f, 7f) * 0.05f : 0f;
        bool card = IsCardPage && i < 2;
        if (card) { DrawPoster(i, d, rx, ry, rw, rh, h, sel, intro, sq, wig, bop); return; }
        if (IsCardPage) { DrawBackTape(i, d, rx, ry, rw, rh, h, sel, intro, sq, wig); return; }

        // settings row: paper strip, tilted, sticker/icon hanging off the left edge
        float cx = rx + rw / 2f + Jitter(i) + 18f * h, cy = ry + rh / 2f + (1f - intro) * 60f;
        float s = (0.97f + 0.05f * h) * intro;
        FunkUI.Push(cx, cy, Tilt(i) + wig, s * sq.X, s * sq.Y);
        float x = cx - rw / 2f, y = cy - rh / 2f;
        var fill = sel ? d.Color : FunkUI.Paper;
        FunkUI.ShadowBlock(x, y, rw, rh, fill, 4f, sel ? 9f : 5f, sel ? 10f : 6f);
        float artH = rh * (d.Art[0].StartsWith("icon:") ? 1.55f : 1.12f);
        float artA = FunkUI.Rad(-8f + 6f * FunkUI.Hash(i)) + (sel ? FunkUI.Wiggle(_selTime, 30f, 8f) * 0.15f : 0f);
        float artBop = sel ? 1f + 0.08f * bop : 1f;
        FunkUI.Sticker(d.Art[0], x + 4f, cy, artH * artBop, artA);
        float ls = rh >= 60f ? 0.56f : 0.5f;
        FunkUI.Bold(d.Label, x + 58f, cy - FunkUI.BoldHeight(ls) / 2f, ls, 0f, _time, 1f, sel ? (Color?)null : FunkUI.Ink);
        if (d.Value != null)
        {
            float vs = 21f, right = x + rw - 14f;
            float vw = FunkUI.MeasureVcr(d.Value, vs);
            float arrowW = d.Arrows ? 26f : 0f;
            float boxW = vw + 26f + 2f * arrowW, bx = right - boxW;
            FunkUI.Fill(bx, cy - 18f, boxW, 36f, FunkUI.Ink);
            FunkUI.Text(d.Value, bx + boxW / 2f, cy - 10.5f, vs, Color.White, 0.5f);
            if (d.Arrows)
            {
                float nudge = sel ? MathF.Max(0f, MathF.Sin(_time * 7f)) * 3f : 0f;
                FunkUI.Bold("<", bx + 6f - nudge, cy - FunkUI.BoldHeight(0.42f) / 2f, 0.42f, 0f, _time, 1f, sel ? FunkUI.Yellow : Color.White);
                FunkUI.Bold(">", bx + boxW - 6f + nudge, cy - FunkUI.BoldHeight(0.42f) / 2f, 0.42f, 1f, _time, 1f, sel ? FunkUI.Yellow : Color.White);
            }
            if (d.Status != 0)
            {
                string st = d.Status > 0 ? "READY" : "NOT CONNECTED";
                float sw = FunkUI.MeasureVcr(st, 16f) + 20f, sx = bx - 14f - sw;
                FunkUI.Push(sx + sw / 2f, cy, FunkUI.Rad(-4f));
                FunkUI.Block(sx, cy - 13f, sw, 26f, d.Status > 0 ? FunkUI.Green : FunkUI.Red, 3f);
                FunkUI.Text(st, sx + sw / 2f, cy - 8f, 16f, FunkUI.Ink, 0.5f);
                FunkUI.Pop();
            }
        }
        FunkUI.Pop();
        if (sel) DrawPointer(cx - rw / 2f * s - 40f, cy, bop);
    }

    /// <summary>Hand-drawn arrow doodle pointing at the selection, bobbing on the beat.</summary>
    private void DrawPointer(float x, float y, float bop)
    {
        float nudge = MathF.Sin(_time * 8f) * 4f + bop * 6f;
        FunkUI.Sticker("@arrow", x - 26f + nudge, y, 40f, FunkUI.Rad(-6f), 1f);
    }

    private void DrawPoster(int i, RowDef d, float rx, float ry, float rw, float rh, float h, bool sel, float intro, Vector2 sq, float wig, float bop)
    {
        float baseTilt = FunkUI.Rad(i == 0 ? -3.5f : 2.6f);
        float cx = rx + rw / 2f + (i == 0 ? -6f : 8f), cy = ry + rh / 2f + (i == 0 ? 6f : -8f) + (1f - intro) * 260f - 8f * h;
        float s = 0.94f + 0.08f * h;
        if (sel) s *= 1f + 0.02f * bop;
        FunkUI.Push(cx, cy, baseTilt + wig, s * sq.X, s * sq.Y);
        float x = cx - rw / 2f, y = cy - rh / 2f;
        var fill = sel ? d.Color : Color.Lerp(d.Color, Color.Black, 0.3f);
        FunkUI.ShadowBlock(x, y, rw, rh, fill, 6f, 12f, 14f);
        // diagonal marker stripes on the poster (hand-drawn texture)
        for (int k = 0; k < 9; k++)
        {
            float sx0 = x + 20f + k * 52f;
            FunkUI.Line(sx0, y + 14f, sx0 - 30f, y + 44f, 5f, FunkUI.A(Color.White, 0.18f));
        }
        // stickers slapped on the upper half
        for (int k = 0; k < d.Art.Length; k++)
        {
            float age = sel ? _selTime - 0.05f * k : 10f;
            float slam = sel ? 1f + 0.4f * (1f - FunkUI.EaseOutCubic(age / 0.09f)) : 1f;
            var ssq = sel ? FunkUI.Squash(age - 0.09f, 0.2f) : Vector2.One;
            float ax = cx + (k == 0 ? -66f : 78f), ay = y + 98f + (k == 0 ? 0f : 14f);
            float ah = (k == 0 ? 158f : 128f) * slam * (sel ? 1f + 0.05f * bop : 1f);
            float aa = FunkUI.Rad(k == 0 ? -8f : 9f) + (sel ? FunkUI.Wiggle(age - 0.09f, 30f, 8f) * 0.12f : 0f);
            FunkUI.Sticker(d.Art[k], ax, ay, ah, aa, 1f, ssq.X, ssq.Y);
        }
        // label
        float ls = d.Label.Length > 8 ? 0.72f : 0.95f;
        FunkUI.Bold(d.Label, cx, y + 205f - FunkUI.BoldHeight(ls) / 2f, ls, 0.5f, _time);
        // description on a paper strip
        float pw = rw - 44f, ph = 58f, px = cx - pw / 2f, py = y + rh - ph - 18f;
        FunkUI.Push(cx, py + ph / 2f, FunkUI.Rad(i == 0 ? 1.4f : -1.2f));
        FunkUI.Block(px, py, pw, ph, FunkUI.Paper, 3f);
        FunkUI.Paragraph(d.Sub ?? "", px + 14f, py + 8f, pw - 28f, 18f, FunkUI.Ink, 0f, 1.3f);
        FunkUI.Pop();
        FunkUI.Tape(cx, y - 6f, 110f, FunkUI.Rad(i == 0 ? 4f : -5f), Color.White, 0.8f);
        FunkUI.Pop();
    }

    private void DrawBackTape(int i, RowDef d, float rx, float ry, float rw, float rh, float h, bool sel, float intro, Vector2 sq, float wig)
    {
        float cx = rx + rw / 2f + 10f * h, cy = ry + rh / 2f + (1f - intro) * 140f;
        FunkUI.Push(cx, cy, FunkUI.Rad(-3f) + wig, (0.96f + 0.08f * h) * sq.X, (0.96f + 0.08f * h) * sq.Y);
        float x = cx - rw / 2f, y = cy - rh / 2f;
        if (sel) FunkUI.ShadowBlock(x, y, rw, rh, FunkUI.Yellow, 4f, 7f, 8f);
        else FunkUI.Fill(x, y, rw, rh, FunkUI.Ink);
        FunkUI.Sticker(d.Art[0], x + 36f, cy, 40f, FunkUI.Rad(-6f), 1f, 1f, 1f, sel ? FunkUI.Ink : Color.White);
        FunkUI.Bold(d.Label, x + 66f, cy - FunkUI.BoldHeight(0.55f) / 2f, 0.55f, 0f, _time);
        FunkUI.Pop();
    }

    /// <summary>Help text of the selected row on a taped notebook note (bottom left).</summary>
    private void DrawHelpNote(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        const float w = 470f, h = 54f;
        float cx = FunkUI.SafeX + w / 2f - 10f, cy = FunkUI.H - 74f;
        var sq = FunkUI.Squash(_selTime, 0.1f);
        FunkUI.Push(cx, cy, FunkUI.Rad(-1.8f) + FunkUI.Wiggle(_selTime, 26f, 9f) * 0.03f, sq.X, sq.Y);
        FunkUI.ShadowBlock(cx - w / 2f, cy - h / 2f, w, h, FunkUI.Paper, 3f, 6f, 7f);
        FunkUI.Fill(cx - w / 2f + 26f, cy - h / 2f, 2f, h, new Color(255, 90, 110, 150));
        FunkUI.Paragraph(text, cx - w / 2f + 38f, cy - h / 2f + 7f, w - 50f, 18f, FunkUI.Ink, 0f, 1.25f);
        FunkUI.Tape(cx - w / 2f + 8f, cy - h / 2f, 70f, FunkUI.Rad(-30f), FunkUI.Pink, 0.9f);
        FunkUI.Pop();
    }

    private void DrawLobbyInfo(float bop)
    {
        float ia = FunkUI.EaseOutBack(FunkUI.Clamp01(_pageTime / 0.35f), 1.8f);
        for (int s = 0; s < 2; s++)
        {
            bool me = s == 0;
            bool present = me || MultiplayerSession.PeerPresent;
            bool bfSide = me == MultiplayerSession.IsHost;
            float cx = s == 0 ? 400f : 880f, cy = 274f + (1f - ia) * 300f;
            float tilt = FunkUI.Rad(s == 0 ? -4f : 3.5f);
            const float w = 300f, h = 196f;
            FunkUI.Push(cx, cy, tilt);
            FunkUI.ShadowBlock(cx - w / 2f, cy - h / 2f, w, h, present ? (bfSide ? CP1 : CP2) : FunkUI.Paper, 5f, 10f, 12f);
            string icon = present ? (bfSide ? "bf" : "dad") : "face";
            float ib = present ? 1f + 0.07f * bop : 1f;
            FunkUI.HealthIcon(icon, cx, cy - 30f, 150f * ib, 0, FunkUI.Wiggle(_pageTime - 0.2f * s, 20f, 5f) * 0.1f, present ? 1f : 0.5f, !bfSide);
            string name = me ? (MultiplayerSession.LocalName ?? "YOU") + " (YOU)" : (MultiplayerSession.PeerPresent ? MultiplayerSession.PeerName ?? "?" : "WAITING" + new string('.', (int)(_time * 2) % 4));
            string info = present ? ((me ? MultiplayerSession.Platform : MultiplayerSession.PeerPlatform) + " - " + (bfSide ? "BF SIDE" : "OPPONENT")) : "share the room info!";
            FunkUI.Text(name, cx, cy + 44f, 21f, FunkUI.Ink, 0.5f);
            FunkUI.Text(info, cx, cy + 70f, 16f, FunkUI.Ink, 0.5f);
            FunkUI.Pop();
        }
        // VS between them
        float vs = (1f + 0.12f * bop) * ia;
        FunkUI.Push(640f, 280f, FunkUI.Rad(-8f) + FunkUI.Wiggle(_pageTime - 0.3f, 18f, 5f) * 0.2f, vs, vs);
        FunkUI.Bold("VS", 640f, 280f - FunkUI.BoldHeight(1.1f) / 2f, 1.1f, 0.5f, _time, 1f, FunkUI.Yellow);
        FunkUI.Pop();
        string line;
        if (MultiplayerSession.Server != null) line = $"FRIENDS JOIN: {MultiplayerSession.LocalIPv4()}  PORT {MultiplayerSession.Server.Port}  ROOM {MultiplayerSession.Room}";
        else if (!MultiplayerSession.IsHost) line = "THE HOST PICKS THE SONG";
        else line = "ROOM " + MultiplayerSession.Room;
        float lw = FunkUI.MeasureVcr(line, 19f) + 40f;
        FunkUI.Push(640f, 410f, FunkUI.Rad(1.2f));
        FunkUI.Fill(640f - lw / 2f, 394f, lw, 32f, FunkUI.Ink);
        FunkUI.Text(line, 640f, 401f, 19f, Color.White, 0.5f);
        FunkUI.Pop();
    }

    private void DrawEdit()
    {
        float ia = FunkUI.EaseOutBack(FunkUI.Clamp01(_pageTime / 0.3f), 2f);
        const float w = 760f, h = 260f;
        float cx = FunkUI.W / 2f, cy = 350f + (1f - ia) * 300f;
        FunkUI.Push(cx, cy, FunkUI.Rad(-1.5f));
        float x = cx - w / 2f, y = cy - h / 2f;
        FunkUI.ShadowBlock(x, y, w, h, FunkUI.Paper, 5f, 12f, 14f);
        for (int k = 1; k < 8; k++) FunkUI.Fill(x + 12f, y + k * 32f, w - 24f, 2f, new Color(120, 170, 255, 110));
        FunkUI.Fill(x + 48f, y, 2f, h, new Color(255, 90, 110, 150));
        FunkUI.Text(_editField == 0 ? "Type the host's IP address or name:" : "Type a room code (both players use the same one):", x + 66f, y + 26f, 21f, FunkUI.Ink);
        FunkUI.Block(x + 66f, y + 72f, w - 120f, 76f, Color.White, 4f);
        float tw = FunkUI.Text(_editBuf, x + 86f, y + 90f, 40f, FunkUI.Ink);
        if (((int)(_time * 2f)) % 2 == 0) FunkUI.Line(x + 92f + tw, y + 84f, x + 90f + tw, y + 136f, 5f, FunkUI.Pink);
        FunkUI.Paragraph("Keyboard: just type.  Controller: Up/Down picks a character, Right adds it, X deletes.", x + 66f, y + 176f, w - 110f, 17f, FunkUI.Ink, 0f, 1.35f);
        FunkUI.Tape(x + 30f, y + 4f, 100f, FunkUI.Rad(-32f), FunkUI.Cyan, 0.9f);
        FunkUI.Tape(x + w - 30f, y + 4f, 100f, FunkUI.Rad(32f), FunkUI.Cyan, 0.9f);
        FunkUI.Pop();
    }

    private void DrawConnecting()
    {
        float cx = FunkUI.W / 2f, cy = 300f;
        // BF bopping on the beat while three notes hop in turn (hand-made loading animation)
        float bop = FunkUI.Bop;
        FunkUI.Sticker(_connectAsHost ? "bf1" : "bf3", cx, cy, 200f * (1f + 0.08f * bop), FunkUI.Rad(-6f) + MathF.Sin(_time * 5f) * 0.08f, 1f, 1f + 0.06f * bop, 1f - 0.06f * bop);
        for (int k = 0; k < 3; k++)
        {
            float ph = (_time * 3f - k * 0.33f) % 1f; if (ph < 0) ph += 1f;
            float hop = MathF.Max(0f, MathF.Sin(ph * MathF.PI * 2f)) * 18f;
            FunkUI.Sticker("@d_note", cx - 60f + k * 60f, cy + 150f - hop, 44f, FunkUI.Rad(-8f + k * 8f), 1f, 1f, 1f, k % 2 == 0 ? Color.White : FunkUI.Ink);
        }
        FunkUI.Bold((_connectAsHost ? "OPENING ROOM" : "CONNECTING") + new string('.', 1 + (int)(_time * 3) % 3), cx, cy + 196f, 0.75f, 0.5f, _time);
        string line = (_connectAsHost ? "localhost" : _address) + " : " + _port + "   room " + _room;
        float lw = FunkUI.MeasureVcr(line, 19f) + 40f;
        FunkUI.Push(cx, cy + 278f, FunkUI.Rad(-1.2f));
        FunkUI.Fill(cx - lw / 2f, cy + 262f, lw, 32f, FunkUI.Ink);
        FunkUI.Text(line, cx, cy + 269f, 19f, Color.White, 0.5f);
        FunkUI.Pop();
    }

    public override void Unload() { _wordSheet?.Dispose(); _wordSheet = null; }

    /// <summary>Host dev automation: start the dev song once the guest is in the room.</summary>
    public bool DevHostReady => _devHostWaitSong && MultiplayerSession.PeerPresent;
    public void DevKick() { if (DevHostReady) { _devHostWaitSong = false; GoChooseSong(); } }
}
