using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Newtonsoft.Json.Linq;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.Colyseus;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Multiplayer > Online > PSYCH ONLINE: play with Psych Online (PC) players on a Funkin-Online-Server
/// (official wss://funkin.sniro.boo or a self-hosted one). Controller-friendly: D-pad/stick + A/B (+X/Y), keyboard works too.
/// Pages: main (create / join by code / public rooms / server / name), room lobby (ready, song, side, privacy, leave),
/// song picker (host), code + text editors. Song start/flow is driven by the room (gameStarted -> PlayScene).
/// Dev hooks: FNF_PSO_AUTO=create|join:<CODE>, FNF_PSO_SERVER=<ws url>, FNF_PSO_NAME, FNF_PSO_SONG=<folder>:<Diff> (host auto-picks),
/// FNF_PSO_READY=1 (auto ready once the song is verified), FNF_MP_BOT=1 (auto-play our side).
/// </summary>
public partial class PsychOnlineScene : Scene
{
    private enum Page { Main, Code, Text, Rooms, Connecting, Room, Songs, Chat, Account, Leaderboard }
    private Page _page = Page.Main;
    private int _sel;
    private float _time, _statusTimer;
    private string _status = "";
    private Task<string> _connect;
    private Task<JArray> _roomsTask;
    private JArray _rooms = new JArray();
    private static string _code = "AAAA";
    private static int _serverIdx;
    private static readonly List<string> _servers = new List<string>(PsychOnline.OfficialServers) { "ws://127.0.0.1:2567" };
    private int _codePos;
    private string _textBuf = ""; private int _textField; private int _textCharIdx;   // 0 = server, 1 = name, 2 = e-mail, 3 = login code, 4 = chat
    private const string TextChars = "abcdefghijklmnopqrstuvwxyz0123456789.:/-_ @!?,'+";
    private static string _email = "";
    private Task<string> _loginTask; private bool _loginIsCode;
    private Page _textReturn = Page.Main;
    private int _songSel, _diffSel = 1;
    private static bool _autoDone, _autoSongSent, _autoSideSent, _autoChatSent;
    private static string _autoReadyFor;
    private bool _starting;

    public override void Load()
    {
        Game.Discord?.SetMenuState("Psych Online");
        Input.GameplayMode = false;
        var sv = Environment.GetEnvironmentVariable("FNF_PSO_SERVER");
        if (!string.IsNullOrEmpty(sv)) { if (!_servers.Contains(sv)) _servers.Add(sv); _serverIdx = _servers.IndexOf(sv); }
        if (PsychOnline.Connected) { _page = Page.Room; _sel = 0; PsychOnline.Send("status", "In the Lobby"); }
        if (_retFromBrowser) AfterBrowser();
        if (!Audio.MusicPlaying) Audio.PlayMusic("music/freakyMenu", true);
        MultiplayerSession.CharOverride = null;
        string lt = Environment.GetEnvironmentVariable("FNF_PSO_LOGINTEST");   // dev: "email" (request a code) or "email|CODE" (verify)
        if (!string.IsNullOrEmpty(lt) && _loginTask == null)
        {
            var parts = lt.Split('|'); _email = parts[0]; _loginIsCode = parts.Length > 1;
            _loginTask = PsychOnline.RequestLoginAsync(_email, _loginIsCode ? parts[1] : null);
        }
    }

    private string Server => _servers[Math.Clamp(_serverIdx, 0, _servers.Count - 1)];
    private void SetStatus(string s) { _status = s; _statusTimer = 6f; Console.WriteLine("[PSO] " + s); }

    // ------------------------------------------------------------------ rows
    private List<(string label, string value)> Rows()
    {
        switch (_page)
        {
            case Page.Main:
                return new() { ("CREATE ROOM", null), ("JOIN ROOM", "code " + _code), ("FIND ROOMS", "public rooms"),
                               ("SERVER", "< " + Server + " >"), ("NAME", PsychOnline.DefaultNickname()), ("SKINS", PsychOnline.SkinLabel(PsychOnline.OwnSkin)),
                               ("ACCOUNT", PsychOnline.LoggedIn ? "logged in as " + (PsychOnline.AuthName ?? "?") : "optional: log in with your e-mail"), ("LEADERBOARD", "top players"), ("BACK", null) };
            case Page.Account:
            {
                var l = new List<(string, string)> { ("E-MAIL", string.IsNullOrEmpty(_email) ? "(none)" : _email), ("SEND LOGIN CODE", "the server e-mails you a one-time code"), ("ENTER CODE", null) };
                if (PsychOnline.LoggedIn) l.Add(("LOG OUT", PsychOnline.AuthName));
                l.Add(("BACK", null));
                return l;
            }
            case Page.Chat:
            {
                var l = new List<(string, string)>();
                foreach (var m in PsychOnline.ChatPresets) l.Add((m.ToUpperInvariant(), null));
                l.Add(("TYPE A MESSAGE", null)); l.Add(("BACK", null));
                return l;
            }
            case Page.Rooms:
            {
                var l = new List<(string, string)>();
                foreach (var r in _rooms)
                {
                    string code = (string)r["roomId"] ?? "?";
                    var md = r["metadata"] as JObject;
                    string nm = (string)md?["name"];
                    l.Add((code, $"{nm ?? ""}  {(int?)r["clients"] ?? 0}/{(int?)r["maxClients"] ?? 0} players  ping {(int?)md?["ping"] ?? 0}"));
                }
                l.Add(("REFRESH", null)); l.Add(("BACK", null));
                return l;
            }
            case Page.Room:
            {
                var st = PsychOnline.State; var me = PsychOnline.Self;
                bool ready = me?.Bool("isReady") ?? false, has = me?.Bool("hasSong") ?? false;
                string song = st?.Str("song");
                var l = new List<(string, string)>
                {
                    ("READY", string.IsNullOrEmpty(song) ? "pick a song first" : !has ? "you don't have this song" : ready ? "ready - waiting for the others" : "not ready"),
                };
                if (PsychOnline.RoomNeedsMod)
                    l.Add(("DOWNLOAD MOD", PsychOnline.ModTask != null && !PsychOnline.ModTask.IsCompleted ? PsychOnline.ModStatus
                        : string.IsNullOrEmpty(PsychOnline.RoomModUrl) ? "the host didn't give a mod URL" : st?.Str("modDir") + " - " + PsychOnline.RoomModUrl));
                if (PsychOnline.HasPerms) l.Add(("SELECT SONG", string.IsNullOrEmpty(song) ? "none" : song));
                l.Add(("SWAP SIDE", PsychOnline.SelfBfSide ? "you play Boyfriend" : "you play the Opponent"));
                if (PsychOnline.IsHost) l.Add(("PRIVATE", (st?.Bool("isPrivate") ?? true) ? "yes (join with the code)" : "no (listed)"));
                l.Add(("CHAT", PsychOnline.Log.Count > 0 ? PsychOnline.Log[PsychOnline.Log.Count - 1] : "say something"));
                l.Add(("LEAVE ROOM", null));
                return l;
            }
            case Page.Songs:
            {
                var songs = PsychOnline.Songs();
                var l = new List<(string, string)>();
                for (int i = 0; i < songs.Count; i++)
                {
                    var e = songs[i];
                    string d = i == _songSel ? "< " + e.Diffs[Math.Clamp(_diffSel, 0, e.Diffs.Count - 1)] + " >" : string.Join(" ", e.Diffs);
                    l.Add((e.Display.ToUpperInvariant(), d));
                }
                l.Add(("GET MORE SONGS", "GameBanana mod browser"));
                l.Add(("BACK", null));
                return l;
            }
            default: return new();
        }
    }

    // ------------------------------------------------------------------ update
    public override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _time += dt; if (_statusTimer > 0) _statusTimer -= dt;
        if (_starting) return;   // scene switch pending
        PsychOnline.Poll();
        DevAuto();

        if (_page == Page.Connecting)
        {
            if (_connect == null && PsychOnline.Joining) { _joinWait += dt; if (_joinWait > 15f) { SetStatus("Couldn't connect: no room state received"); PsychOnline.Disconnect(); _page = Page.Main; } }
            if (_connect == null && PsychOnline.Connected) EnterRoom();
            if (_connect == null && PsychOnline.Room == null) _page = Page.Main;
            if (_connect != null && _connect.IsCompleted)
            {
                string err = _connect.Status == TaskStatus.RanToCompletion ? _connect.Result : (_connect.Exception?.InnerException?.Message ?? "failed");
                _connect = null;
                if (err != null) { SetStatus("Couldn't connect: " + err); _page = Page.Main; }
                else _joinWait = 0f;   // wait for JOIN_ROOM + first state (EnterRoom)
            }
            if (Input.BackPressed) { PsychOnline.Disconnect(); _connect = null; _page = Page.Main; }
            return;
        }
        UpdateRest(dt);
    }

    private float _joinWait;
    private bool _reconnected;
    private static string _autoModFor;
    private void EnterRoom()
    {
        SetStatus("Joined room " + PsychOnline.Room.RoomId); _page = Page.Room; _sel = 0; _lobbyInit = false; PsychOnline.Send("status", "In the Lobby");
        var cf = Environment.GetEnvironmentVariable("FNF_PSO_CODEFILE");   // dev: tell a test peer the room code
        if (!string.IsNullOrEmpty(cf)) try { System.IO.File.WriteAllText(cf, PsychOnline.Room.RoomId); } catch { }
    }

    private void UpdateRest(float dt)
    {
        if (_loginTask != null && _loginTask.IsCompleted)
        {
            string r = _loginTask.Status == TaskStatus.RanToCompletion ? _loginTask.Result : "ERROR: " + _loginTask.Exception?.InnerException?.Message;
            _loginTask = null;
            if (r != null) SetStatus(r);
            else if (_loginIsCode) SetStatus("Logged in as " + PsychOnline.AuthName + " (" + PsychOnline.AuthPoints + " FP)");
            else SetStatus("If this e-mail is registered, a code was sent to it. Choose ENTER CODE.");
        }
        if (PsychOnline.ModTask != null && PsychOnline.ModTask.IsCompleted)
        {
            string r = PsychOnline.ModTask.Status == TaskStatus.RanToCompletion ? PsychOnline.ModTask.Result : "ERROR: " + PsychOnline.ModTask.Exception?.InnerException?.Message;
            PsychOnline.ModTask = null;
            if (r != null && r.StartsWith("ERROR")) SetStatus("Mod download failed: " + r.Substring(6).Trim());
            else
            {
                SetStatus("Installed mod: " + r);
                if (PsychOnline.Connected) { PsychOnline.Send("notifyInstall", PsychOnline.ModJobUrl ?? PsychOnline.RoomModUrl ?? r); PsychOnline.VerifyChart(); }
            }
            OnRoomModFinished(r);   // auto-ready once the chart is verified (or reopen the card with the error)
        }
        if (_roomsTask != null && _roomsTask.IsCompleted)
        {
            _rooms = _roomsTask.Status == TaskStatus.RanToCompletion ? _roomsTask.Result ?? new JArray() : new JArray();
            if (_roomsTask.Status != TaskStatus.RanToCompletion) SetStatus("Couldn't list rooms: " + _roomsTask.Exception?.InnerException?.Message);
            else SetStatus(_rooms.Count + " public room(s)");
            _roomsTask = null;
        }
        if (_page == Page.Room || _page == Page.Songs || _page == Page.Chat || (_page == Page.Text && _textField == 4))
        {
            if (!PsychOnline.Connected)
            {
                // reconnect on drop (GameClient.reconnect): the server keeps the seat for 20 s
                if (!PsychOnline.Reconnecting && PsychOnline.ReconnectTask != null && PsychOnline.ReconnectTask.IsCompleted)
                {
                    string err = PsychOnline.ReconnectTask.Status == TaskStatus.RanToCompletion ? PsychOnline.ReconnectTask.Result : "failed";
                    PsychOnline.ReconnectTask = null;
                    if (err != null) { SetStatus("Disconnected from the room: " + err); PsychOnline.Disconnect(); _page = Page.Main; _sel = 0; _lobbyInit = false; return; }
                    _reconnected = true;
                }
                else if (!PsychOnline.Reconnecting && PsychOnline.CanReconnect) PsychOnline.StartReconnect();
                else if (!PsychOnline.Reconnecting && !PsychOnline.Joining)
                { SetStatus("Disconnected from the room" + (PsychOnline.LastError != null ? ": " + PsychOnline.LastError : "")); PsychOnline.Disconnect(); _page = Page.Main; _sel = 0; _lobbyInit = false; return; }
                bool giveUp = Input.IsHeld(Keys.Escape) || GamePad.GetState(PlayerIndex.One).IsButtonDown(Buttons.B);
                if (giveUp && _time > 0.5f) { _leaveHold += dt; if (_leaveHold > 1f) { _leaveHold = 0f; PsychOnline.ReconnectTask = null; PsychOnline.Disconnect(); _page = Page.Main; _lobbyInit = false; } }
                if (_page == Page.Room) UpdateLobbyVisualsOnly(dt);
                return;
            }
            if (_reconnected || (PsychOnline.ReconnectTask != null && PsychOnline.ReconnectTask.IsCompleted)) { _reconnected = false; PsychOnline.ReconnectTask = null; SetStatus("Reconnected!"); Toast("Reconnected!", new Color(120, 255, 140)); PsychOnline.Send("status", "In the Lobby"); }
            if (PsychOnline.GameStartedPending) { StartMatch(); return; }
            if (_page == Page.Room) { UpdateLobby(dt); return; }
        }
        if (_page == Page.Code) { UpdateCode(); return; }
        if (_page == Page.Text) { UpdateText(); return; }

        if (_page == Page.Leaderboard) { UpdateLeaderboard(); return; }
        var rows = Rows();
        if (rows.Count == 0) return;
        int mlr = UpdateMouse(rows, out bool mclick);
        if (mclick) { if (_page == Page.Main && _sel == 3) mlr = 1; else { Confirm(_sel, rows[_sel].label); return; } }
        if (UpdateMainNav(rows.Count)) { }
        else if (Input.UpPressed) { _sel = (_sel + rows.Count - 1) % rows.Count; Audio.PlaySound("scrollMenu"); if (_page == Page.Songs) _songSel = _sel; }
        else if (Input.DownPressed) { _sel = (_sel + 1) % rows.Count; Audio.PlaySound("scrollMenu"); if (_page == Page.Songs) _songSel = _sel; }
        _sel = Math.Clamp(_sel, 0, rows.Count - 1);
        int lr = Input.LeftPressed ? -1 : Input.RightPressed ? 1 : 0;
        if (_page == Page.Main && _sel <= 2) lr = 0;   // posters: Left/Right handled by UpdateMainNav
        if (mlr != 0) lr = mlr;
        if (lr != 0)
        {
            if (_page == Page.Main && _sel == 3) { _serverIdx = (_serverIdx + lr + _servers.Count) % _servers.Count; Audio.PlaySound("scrollMenu"); }
            if (_page == Page.Songs && _sel < PsychOnline.Songs().Count) { var e = PsychOnline.Songs()[_sel]; _diffSel = (Math.Clamp(_diffSel, 0, e.Diffs.Count - 1) + lr + e.Diffs.Count) % e.Diffs.Count; Audio.PlaySound("scrollMenu"); }
        }
        if (Input.BackPressed) { Back(); return; }
        if (_page == Page.Songs && PsychOnline.Connected && (Input.IsGamePadPressed(Buttons.RightTrigger) || Input.IsPressed(Keys.G))) { OpenModBrowser(); return; }
        DevPickNew();
        if (Input.ConfirmPressed) Confirm(_sel, rows[_sel].label);
    }

    /// <summary>Dev: FNF_PSO_PICKNEW=&lt;Diff&gt; picks the browser-installed mod's first song once we are back on the song list.</summary>
    private void DevPickNew()
    {
        string d = Environment.GetEnvironmentVariable("FNF_PSO_PICKNEW");
        if (_devPickNewDone || string.IsNullOrEmpty(d) || _page != Page.Songs || _retMod == null || _time < 2f) return;
        _devPickNewDone = true;
        int i = PsychOnline.FirstSongOfMod(_retMod);
        if (i < 0) return;
        var e = PsychOnline.Songs()[i];
        int di = e.Diffs.FindIndex(x => x.Equals(d, StringComparison.OrdinalIgnoreCase));
        _sel = _songSel = i; _diffSel = di >= 0 ? di : 0;
        Console.WriteLine($"[PSO] dev: picking new mod song {e.Folder} ({e.Diffs[_diffSel]}) from {e.ModDir}");
        Confirm(_sel, Rows()[_sel].label);
    }

    private void Back()
    {
        Audio.PlaySound("cancelMenu");
        switch (_page)
        {
            case Page.Main: Game.Scenes.ChangeScene(new MultiplayerScene()); break;
            case Page.Rooms: _page = Page.Main; _sel = 2; break;
            case Page.Room: PsychOnline.Disconnect(); _page = Page.Main; _sel = 0; break;
            case Page.Songs: _page = Page.Room; _sel = 0; break;
            case Page.Chat: _page = Page.Room; _sel = 0; break;
            case Page.Account: _page = Page.Main; _sel = 5; break;
        }
    }

    private void Confirm(int i, string label)
    {
        Audio.PlaySound("confirmMenu");
        switch (_page)
        {
            case Page.Main:
                if (label == "CREATE ROOM") StartConnect(null);
                else if (label == "JOIN ROOM") { _page = Page.Code; _codePos = 0; }
                else if (label == "FIND ROOMS") { _page = Page.Rooms; _sel = 0; RefreshRooms(); }
                else if (label == "SERVER") OpenText(0, Server, Page.Main);
                else if (label == "NAME") OpenText(1, PsychOnline.DefaultNickname(), Page.Main);
                else if (label == "SKINS") { Audio.PlaySound("confirmMenu"); Game.Scenes.ChangeScene(new SkinsScene(() => new PsychOnlineScene())); }
                else if (label == "ACCOUNT") { PsychOnline.LoadAuth(); _page = Page.Account; _sel = 0; }
                else if (label == "LEADERBOARD") OpenLeaderboard();
                else Back();
                break;
            case Page.Rooms:
                if (label == "REFRESH") RefreshRooms();
                else if (label == "BACK") Back();
                else { _code = label; StartConnect(label); }
                break;
            case Page.Room:
                if (label == "READY") ToggleReady();
                else if (label == "SELECT SONG") { _page = Page.Songs; _sel = _songSel = 0; }
                else if (label == "SWAP SIDE") PsychOnline.Send("swapSides");
                else if (label == "PRIVATE") PsychOnline.Send("togglePrivate");
                else if (label == "CHAT") { _page = Page.Chat; _sel = 0; }
                else if (label == "DOWNLOAD MOD")
                {
                    if (string.IsNullOrEmpty(PsychOnline.RoomModUrl)) SetStatus("The host didn't specify the URL of this mod");
                    else { PsychOnline.StartModDownload(PsychOnline.RoomModUrl); SetStatus("Downloading the mod ..."); }
                }
                else Back();
                break;
            case Page.Songs:
                if (label == "GET MORE SONGS" && i >= PsychOnline.Songs().Count) { OpenModBrowser(); return; }
                if (label == "BACK" || i >= PsychOnline.Songs().Count) { Back(); return; }
                var e = PsychOnline.Songs()[i];
                if (PsychOnline.PickSong(e, _diffSel)) SetStatus("Picked " + e.Display + " (" + e.Diffs[Math.Clamp(_diffSel, 0, e.Diffs.Count - 1)] + ")");
                else SetStatus("Chart missing for " + e.Display);
                _page = Page.Room; _sel = 0;
                break;
            case Page.Chat:
                if (label == "BACK") { Back(); return; }
                if (label == "TYPE A MESSAGE") { OpenText(4, "", Page.Chat); return; }
                PsychOnline.SendChat(PsychOnline.ChatPresets[Math.Clamp(i, 0, PsychOnline.ChatPresets.Length - 1)]);
                _page = Page.Room; _sel = 0;
                break;
            case Page.Account:
                if (label == "BACK") { Back(); return; }
                if (label == "E-MAIL") OpenText(2, _email, Page.Account);
                else if (label == "ENTER CODE") OpenText(3, "", Page.Account);
                else if (label == "LOG OUT") { PsychOnline.Logout(); SetStatus("Logged out"); _sel = 0; }
                else if (label == "SEND LOGIN CODE")
                {
                    if (!_email.Contains("@")) { SetStatus("Enter your e-mail address first"); return; }
                    if (_loginTask == null) { _loginIsCode = false; _loginTask = PsychOnline.RequestLoginAsync(_email, null); SetStatus("Requesting a login code ..."); }
                }
                break;
        }
    }

    private void OpenText(int field, string buf, Page ret) { _textField = field; _textBuf = buf ?? ""; _textCharIdx = 0; _textReturn = ret; _page = Page.Text; }

    private void ToggleReady()
    {
        var me = PsychOnline.Self;
        if (string.IsNullOrEmpty(PsychOnline.State?.Str("song"))) { SetStatus("The host has to pick a song first"); return; }
        if (me != null && !me.Bool("hasSong")) { SetStatus("You don't have this song/chart (only base-game Psych Online songs are supported)"); PsychOnline.VerifyChart(); return; }
        PsychOnline.Send("startGame");   // toggles isReady; the server starts when everyone is ready
    }

    private void StartConnect(string code)
    {
        PsychOnline.LastError = null;
        _connect = PsychOnline.ConnectAsync(Server, code);
        _page = Page.Connecting;
        SetStatus(code == null ? "Creating a room on " + Server + " ..." : "Joining " + code + " on " + Server + " ...");
    }

    private void RefreshRooms()
    {
        if (_roomsTask != null) return;
        _roomsTask = PsychOnline.ListRoomsAsync(Server);
        SetStatus("Looking for rooms ...");
    }

    private void StartMatch()
    {
        PsychOnline.GameStartedPending = false;
        _starting = true;
        var st = PsychOnline.State;
        string folder = st.Str("folder"), song = st.Str("song"), modDir = st.Str("modDir");
        var r = PsychOnline.Resolve(folder, song, modDir);
        if (r == null) { _starting = false; SetStatus("Song " + song + " is not available here"); PsychOnline.Send("requestEndSong"); return; }
        Console.WriteLine($"[PSO] gameStarted -> {r.Song} ({r.Diff}) char {r.Char} mod '{r.ModPath}', bf={PsychOnline.PlaysAsBF}");
        MultiplayerSession.Mode = MultiplayerSession.MpMode.Psych;
        MultiplayerSession.CharOverride = r.Char;   // variation of the room chart: bf (base/erect/BF mix) or pico (Pico Mix)
        Audio.StopMusic();
        Game.Scenes.ChangeScene(new PlayScene(PlayStateParams.ForFreeplay(r.Song, r.Diff, r.ModPath)));
    }

    private void DevAuto()
    {
        string auto = Environment.GetEnvironmentVariable("FNF_PSO_AUTO");
        if (!_autoDone && !string.IsNullOrEmpty(auto) && _time > 0.8f)
        {
            _autoDone = true;
            if (auto == "create") StartConnect(null);
            else if (auto.StartsWith("join:")) { _code = auto.Substring(5).ToUpperInvariant(); StartConnect(_code); }
        }
        if (!PsychOnline.Connected || _page != Page.Room) return;
        string ds = Environment.GetEnvironmentVariable("FNF_PSO_SONG");
        int.TryParse(Environment.GetEnvironmentVariable("FNF_PSO_MINPLAYERS"), out int devMinPlayers);   // dev: wait for N players before the auto song pick
        if (!_autoSongSent && !string.IsNullOrEmpty(ds) && PsychOnline.HasPerms && PsychOnline.AllPlayers().Count() >= Math.Max(2, devMinPlayers) && _time > 2f)
        {
            _autoSongSent = true;
            var parts = ds.Split(':');
            var e = PsychOnline.Songs().FirstOrDefault(x => x.Folder == parts[0] && (parts.Length < 3 || x.ModDir == parts[2]));
            if (e != null) PsychOnline.PickSong(e, Math.Max(0, e.Diffs.IndexOf(parts.Length > 1 ? parts[1] : "Normal")));
            else SetStatus("dev song not found: " + ds);
        }
        var me = PsychOnline.Self;
        string chat = Environment.GetEnvironmentVariable("FNF_PSO_CHAT");   // dev: send one chat line once the other player is in
        if (!_autoChatSent && !string.IsNullOrEmpty(chat) && PsychOnline.AllPlayers().Count() >= 2 && _time > 3f) { _autoChatSent = true; PsychOnline.SendChat(chat); }
        string side = Environment.GetEnvironmentVariable("FNF_PSO_SIDE");   // dev: bf|dad -> swapSides once
        if (!_autoSideSent && me != null && !string.IsNullOrEmpty(side) && me.Bool("bfSide") != (side == "bf")) { _autoSideSent = true; PsychOnline.Send("swapSides"); }
        // dev: ready once per picked song (startGame toggles isReady)
        if (Environment.GetEnvironmentVariable("FNF_PSO_READY") == "1" && me != null && me.Bool("hasSong") && !me.Bool("isReady")
            && !(PsychOnline.State?.Bool("isStarted") ?? false) && _autoReadyFor != PsychOnline.State?.Str("song"))
        { _autoReadyFor = PsychOnline.State?.Str("song"); PsychOnline.Send("startGame"); }
    }

    // ------------------------------------------------------------------ editors (controller friendly)
    private void UpdateCode()
    {
        // 4-letter room code: Up/Down = letter, Left/Right = position, A/Start = join, B = cancel; keyboard typing works too
        var c = _code.PadRight(4, 'A').ToCharArray();
        if (Input.UpPressed) { c[_codePos] = (char)('A' + (c[_codePos] - 'A' + 1) % 26); Audio.PlaySound("scrollMenu"); }
        if (Input.DownPressed) { c[_codePos] = (char)('A' + (c[_codePos] - 'A' + 25) % 26); Audio.PlaySound("scrollMenu"); }
        if (Input.LeftPressed) _codePos = (_codePos + 3) % 4;
        if (Input.RightPressed) _codePos = (_codePos + 1) % 4;
        var k = Input.GetAnyKeyPressed();
        if (k.HasValue && k.Value >= Keys.A && k.Value <= Keys.Z) { c[_codePos] = (char)('A' + (k.Value - Keys.A)); _codePos = (_codePos + 1) % 4; }
        _code = new string(c);
        if (Input.IsPressed(Keys.Enter) || Input.IsGamePadPressed(Buttons.A) || Input.IsGamePadPressed(Buttons.Start)) { Audio.PlaySound("confirmMenu"); StartConnect(_code); }
        else if (Input.IsPressed(Keys.Escape) || Input.IsGamePadPressed(Buttons.B)) { Audio.PlaySound("cancelMenu"); _page = Page.Main; }
    }

    private void UpdateText()
    {
        var skb = SystemKeyboard.Poll(this, _textField switch { 0 => "Server address", 1 => "Nickname (3 to 14 characters)", 2 => "E-mail", 3 => "Login code", _ => "Chat message" }, ref _textBuf, _textField == 1 ? 14 : 200);
        if (skb == SystemKeyboard.State.Busy) return;
        bool skbOk = skb == SystemKeyboard.State.Done, skbCancel = skb == SystemKeyboard.State.Cancelled;
        bool up = Input.IsPressed(Keys.Up) || Input.IsGamePadPressed(Buttons.DPadUp), down = Input.IsPressed(Keys.Down) || Input.IsGamePadPressed(Buttons.DPadDown);
        if (up || down)
        {
            _textCharIdx = (_textCharIdx + (up ? 1 : TextChars.Length - 1)) % TextChars.Length;
            if (_textBuf.Length == 0) _textBuf = "a";
            _textBuf = _textBuf.Substring(0, _textBuf.Length - 1) + TextChars[_textCharIdx];
        }
        if (Input.IsGamePadPressed(Buttons.DPadRight) || Input.IsPressed(Keys.Right)) _textBuf += TextChars[_textCharIdx];
        if (Input.IsGamePadPressed(Buttons.DPadLeft) || Input.IsGamePadPressed(Buttons.X) || Input.IsPressed(Keys.Back) || Input.IsPressed(Keys.Left))
            if (_textBuf.Length > 0) _textBuf = _textBuf.Substring(0, _textBuf.Length - 1);
        var k = Input.GetAnyKeyPressed();
        if (k.HasValue)
        {
            var key = k.Value; bool shift = Input.IsHeld(Keys.LeftShift) || Input.IsHeld(Keys.RightShift);
            if (shift && key == Keys.D2) _textBuf += '@';
            else if (shift && key == Keys.D1) _textBuf += '!';
            else if (shift && key == Keys.OemQuestion) _textBuf += '?';
            else if (key == Keys.OemComma) _textBuf += ',';
            else if (key == Keys.OemPlus && shift) _textBuf += '+';
            else if (key == Keys.OemQuotes) _textBuf += '\'';
            else if (key >= Keys.D0 && key <= Keys.D9) _textBuf += (char)('0' + (key - Keys.D0));
            else if (key >= Keys.NumPad0 && key <= Keys.NumPad9) _textBuf += (char)('0' + (key - Keys.NumPad0));
            else if (key == Keys.OemPeriod || key == Keys.Decimal) _textBuf += '.';
            else if (key >= Keys.A && key <= Keys.Z) _textBuf += (char)((shift ? 'A' : 'a') + (key - Keys.A));
            else if (key == Keys.OemMinus) _textBuf += shift ? '_' : '-';
            else if (key == Keys.OemQuestion) _textBuf += '/';
            else if (key == Keys.OemSemicolon) _textBuf += ':';
            else if (key == Keys.Space) _textBuf += ' ';
        }
        if (skbOk || Input.IsPressed(Keys.Enter) || Input.IsGamePadPressed(Buttons.A) || Input.IsGamePadPressed(Buttons.Start))
        {
            string v = _textBuf.Trim();
            if (_textField == 0 && v.Length > 0)
            {
                if (!v.Contains("://")) v = "ws://" + v + (v.Contains(":") ? "" : ":2567");
                if (!_servers.Contains(v)) _servers.Add(v);
                _serverIdx = _servers.IndexOf(v);
            }
            else if (_textField == 1 && v.Length >= 3) PsychOnline.SetNickname(v);
            else if (_textField == 2) _email = v;
            else if (_textField == 3 && v.Length > 0 && _loginTask == null) { _loginIsCode = true; _loginTask = PsychOnline.RequestLoginAsync(_email, v); SetStatus("Logging in ..."); }
            else if (_textField == 4 && v.Length > 0) { PsychOnline.SendChat(v); _textReturn = Page.Room; }
            _page = _textReturn; _sel = 0;
        }
        else if (skbCancel || Input.IsPressed(Keys.Escape) || Input.IsGamePadPressed(Buttons.B)) _page = _textReturn;
    }

    // ------------------------------------------------------------------ drawing (same look as MultiplayerScene)
    private Texture2D _desat;
    private readonly MenuUI.FollowCam _cam = new MenuUI.FollowCam(75f, 720f - 150f + 40f - 60f, -75f, 0.085f);
    private const float ROW0 = 200f, ROW_SP = 115f;
    private float RowY(int i) => ROW0 + i * ROW_SP + (_page == Page.Room ? 110f : 0f);

    private List<ButtonPrompts.Prompt> Prompts()
    {
        var l = new List<ButtonPrompts.Prompt>();
        switch (_page)
        {
            case Page.Code:
                l.Add(ButtonPrompts.UpDown("LETTER")); l.Add(ButtonPrompts.LeftRight("MOVE")); l.Add(ButtonPrompts.Confirm("JOIN")); l.Add(ButtonPrompts.Back("CANCEL"));
                return l;
            case Page.Text:
                l.Add(ButtonPrompts.UpDown("CHAR")); l.Add(new ButtonPrompts.Prompt(Buttons.DPadRight, "TYPE", "ADD"));
                l.Add(new ButtonPrompts.Prompt(Buttons.X, "BACKSPACE", "DELETE")); l.Add(ButtonPrompts.Confirm("OK")); l.Add(ButtonPrompts.Back("CANCEL"));
                return l;
            case Page.Connecting: l.Add(ButtonPrompts.Back("CANCEL")); return l;
        }
        l.Add(ButtonPrompts.UpDown("MOVE"));
        if ((_page == Page.Main && _sel == 3) || _page == Page.Songs) l.Add(ButtonPrompts.LeftRight(_page == Page.Songs ? "DIFFICULTY" : "SERVER"));
        l.Add(ButtonPrompts.Confirm("SELECT"));
        if (_page == Page.Songs && PsychOnline.Connected) l.Add(new ButtonPrompts.Prompt(Buttons.RightTrigger, "G", "MORE SONGS"));
        l.Add(ButtonPrompts.Back(_page == Page.Room ? "LEAVE" : "BACK"));
        return l;
    }

    public override void Draw(SpriteBatch sb)
    {
        if (_desat == null) { _desat = Assets.LoadTexture("menus/main_menu/menuDesat.png"); if (_desat == Assets.Pixel) _desat = null; }
        var rows = Rows();
        float target = _page is Page.Code or Page.Text or Page.Connecting ? 0f : rows.Count > 0 ? RowY(Math.Clamp(_sel, 0, rows.Count - 1)) : 0f;
        _cam.Update(target, 70f, 1f / 60f);
        float sy = _cam.ScrollY;
        var bold = AtlasText.Bold; var def = AtlasText.Default;
        if (_page != Page.Room) { DrawFunk(sb); return; }

        bool inRoom = PsychOnline.Connected || PsychOnline.Reconnecting || PsychOnline.ReconnectTask != null;
        if (_page == Page.Room) { DrawLobby(sb); return; }
        bool overLobby = inRoom && (_page == Page.Songs || _page == Page.Chat || (_page == Page.Text && _textField == 4));
        if (overLobby) { DrawLobby(sb, hud: false); }
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
        if (overLobby) sb.Draw(Assets.Pixel, new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT), new Color(0, 0, 0, 165));
        else MenuUI.DrawBackground(sb, _desat, MenuUI.DefaultTint, sy);
        string head = _page switch
        {
            Page.Room => "ROOM " + (PsychOnline.Room?.RoomId ?? ""),
            Page.Songs => "SELECT SONG",
            Page.Rooms => "PUBLIC ROOMS",
            Page.Chat => "CHAT",
            Page.Account => "ACCOUNT",
            _ => "PSYCH ONLINE",
        };
        bold?.DrawCentered(sb, head, 40 - sy, 1f, _time);

        if (_page == Page.Code)
        {
            bold?.DrawCentered(sb, "ROOM CODE", 200, 0.6f, _time);
            string shown = string.Concat(_code.Select((ch, i) => i == _codePos && ((int)(_time * 3) % 2 == 0) ? "_" : ch.ToString()));
            bold?.DrawCentered(sb, shown, 320, 1f, _time);
            MenuUI.Text(sb, 20, "Server: " + Server, 640, 460, Color.White, 1f, 2, 0.5f);
        }
        else if (_page == Page.Text)
        {
            bold?.DrawCentered(sb, _textField switch { 0 => "SERVER ADDRESS", 1 => "NICKNAME", 2 => "E-MAIL", 3 => "LOGIN CODE", _ => "MESSAGE" }, 200, 0.6f, _time);
            string cursor = ((int)(_time * 2) % 2 == 0) ? "_" : " ";
            def?.DrawCentered(sb, _textBuf + cursor, 300, 1f, _time);
            MenuUI.Text(sb, 20, _textField switch { 0 => "e.g. 192.168.1.20:2567 (self-hosted Funkin-Online-Server) or wss://funkin.sniro.boo", 1 => "3 to 14 characters",
                2 => "the e-mail of your Psych Online (Funkin Network) account", 3 => "the code from the e-mail", _ => "chat message (max 300 characters)" },
                640, 420, Color.White, 1f, 2, 0.5f);
        }
        else if (_page == Page.Connecting)
            bold?.DrawCentered(sb, "CONNECTING" + new string('.', 1 + (int)(_time * 3) % 3), 300, 1f, _time);
        else
        {
            if (_page == Page.Room) DrawRoomInfo(sb, 120 - sy);
            if (_page == Page.Main)
                MenuUI.Text(sb, 20, "Play with Psych Online (PC) players - base game songs, protocol " + PsychOnline.Protocol, 640, 120 - sy, new Color(170, 220, 255), 1f, 2, 0.5f);
            for (int i = 0; i < rows.Count; i++)
            {
                float y = RowY(i) - sy;
                if (y < -140 || y > 760) continue;
                float a = i == _sel ? 1f : 0.6f;
                bold?.DrawCentered(sb, rows[i].label, y, a, _time);
                if (rows[i].value != null && def != null) def.DrawCentered(sb, rows[i].value, y + (bold?.MaxHeight ?? 70) - 2, a, _time, 0.55f);
            }
        }
        if (_statusTimer > 0) MenuUI.Text(sb, 22, _status, 640, FNFGame.SCREEN_HEIGHT - 100, new Color(255, 190, 90), Math.Min(1f, _statusTimer), 2, 0.5f);
        ButtonPrompts.DrawBar(sb, Assets, Input, Prompts(), FNFGame.SCREEN_WIDTH / 2, FNFGame.SCREEN_HEIGHT - 46, 0.5f, 32);
        sb.End();
    }

    private void DrawRoomInfo(SpriteBatch sb, float y)
    {
        var st = PsychOnline.State;
        if (st == null) return;
        string song = st.Str("song");
        MenuUI.Text(sb, 22, $"Code {PsychOnline.Room?.RoomId}  -  {PsychOnline.Server}  -  song: {(string.IsNullOrEmpty(song) ? "(none)" : song)}", 640, y, new Color(170, 220, 255), 1f, 2, 0.5f);
        y += 32;
        foreach (var (sid, p) in PsychOnline.AllPlayers())
        {
            bool me = sid == PsychOnline.SessionId;
            string line = $"{(p.Bool("bfSide") ? "BF " : "DAD")}  {p.Str("name")}{(me ? " (you)" : "")}{(sid == st.Str("host") ? " [host]" : "")}  "
                        + $"{(p.Bool("isReady") ? "READY" : "not ready")}  {(p.Bool("hasSong") ? "has song" : "no song")}  {(int)p.Num("ping")} ms  {p.Str("status")}"
                        + (PsychOnline.SkinName(p) is string sk ? "  skin: " + sk : "") + (p.Bool("verified") ? "  [network]" : "");
            MenuUI.Text(sb, 20, line, 640, y, p.Bool("isReady") ? new Color(150, 255, 150) : Color.White, 1f, 2, 0.5f);
            y += 26;
        }
        var log = PsychOnline.Log;
        for (int i = Math.Max(0, log.Count - 4); i < log.Count; i++) { MenuUI.Text(sb, 18, log[i], 640, y, new Color(255, 230, 160), 0.9f, 2, 0.5f); y += 22; }
    }

    public override void Unload() { }
}
