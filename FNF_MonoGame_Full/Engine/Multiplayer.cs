using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Newtonsoft.Json.Linq;

namespace FNF_MonoGame.Engine;

// ============================================================================================================
// Multiplayer core (local 2-player + online relay). See REPORT.md section F for the design and the reason the
// Psych Online Colyseus protocol is not used: the engine speaks its own tiny "FNFMP" protocol =
//   newline-delimited JSON objects over a plain TCP socket (works on desktop .NET and UWP/.NET Native),
//   routed by a relay (RelayServer) that any instance can host in-process (PC or Xbox) or run headless
//   with  FNF_MonoGame.exe --relay [port].
// ============================================================================================================

public enum MpDevice { KeyboardArrows, KeyboardWASD, Pad1, Pad2, Pad3, Pad4 }

/// <summary>Per-player note input for local multiplayer (each player owns one device).</summary>
public static class MultiplayerInput
{
    public static readonly MpDevice[] Devices = { MpDevice.KeyboardArrows, MpDevice.Pad1 };
    public static readonly bool[,] Pressed = new bool[2, 4], Held = new bool[2, 4], Released = new bool[2, 4];
    private static readonly GamePadState[] _pads = new GamePadState[4], _prevPads = new GamePadState[4];
    private static KeyboardState _kb, _prevKb;
    private static readonly Keys[] Arrows = { Keys.Left, Keys.Down, Keys.Up, Keys.Right };
    private static readonly Keys[] Wasd = { Keys.A, Keys.S, Keys.W, Keys.D };
    private static readonly Buttons[] Dpad = { Buttons.DPadLeft, Buttons.DPadDown, Buttons.DPadUp, Buttons.DPadRight };
    private static readonly Buttons[] Face = { Buttons.X, Buttons.A, Buttons.Y, Buttons.B };
    private static readonly Buttons[] Trig = { Buttons.LeftTrigger, Buttons.LeftShoulder, Buttons.RightShoulder, Buttons.RightTrigger };
    private static int _lastFrame = -1;
    public static int Frame;

    /// <summary>Poll keyboard + 4 pads once per frame (idempotent within a frame).</summary>
    public static void Poll()
    {
        if (_lastFrame == Frame) return;
        _lastFrame = Frame;
        _prevKb = _kb; _kb = Keyboard.GetState();
        for (int i = 0; i < 4; i++) { _prevPads[i] = _pads[i]; _pads[i] = GamePad.GetState((PlayerIndex)i); }
    }

    public static void UpdateNotes()
    {
        Poll();
        for (int p = 0; p < 2; p++)
            for (int l = 0; l < 4; l++)
            {
                bool now = LaneHeld(Devices[p], l, false), prev = LaneHeld(Devices[p], l, true);
                Held[p, l] = now; Pressed[p, l] = now && !prev; Released[p, l] = !now && prev;
            }
    }

    private static bool LaneHeld(MpDevice d, int lane, bool prev)
    {
        var kb = prev ? _prevKb : _kb;
        switch (d)
        {
            case MpDevice.KeyboardArrows: return kb.IsKeyDown(Arrows[lane]);
            case MpDevice.KeyboardWASD: return kb.IsKeyDown(Wasd[lane]);
            default:
                var gp = (prev ? _prevPads : _pads)[(int)d - (int)MpDevice.Pad1];
                if (!gp.IsConnected) return false;
                return gp.IsButtonDown(Dpad[lane]) || gp.IsButtonDown(Face[lane]) || gp.IsButtonDown(Trig[lane]);
        }
    }

    public static bool Connected(MpDevice d) => d <= MpDevice.KeyboardWASD || _pads[(int)d - (int)MpDevice.Pad1].IsConnected;

    public static string Name(MpDevice d) => d switch
    {
        MpDevice.KeyboardArrows => "Keyboard (Arrows)",
        MpDevice.KeyboardWASD => "Keyboard (WASD)",
        _ => $"Controller {(int)d - (int)MpDevice.Pad1 + 1}"
    };

    /// <summary>Pad index whose A (or Start) was just pressed, else -1 (used for "press A to join").</summary>
    public static int PadJustPressed(Buttons b)
    {
        Poll();
        for (int i = 0; i < 4; i++)
            if (_pads[i].IsConnected && _pads[i].IsButtonDown(b) && !_prevPads[i].IsButtonDown(b)) return i;
        return -1;
    }

    public static bool KeyJustPressed(Keys k) { Poll(); return _kb.IsKeyDown(k) && !_prevKb.IsKeyDown(k); }
}

/// <summary>Client side of the FNFMP protocol (one JSON object per line over TCP).</summary>
public sealed class NetClient : IDisposable
{
    private TcpClient _tcp;
    private Stream _stream;
    private readonly object _sendLock = new object();
    public readonly ConcurrentQueue<JObject> Inbox = new ConcurrentQueue<JObject>();
    public volatile bool Connected;
    public volatile string Error;
    public int PingMs = -1;

    public async Task<bool> ConnectAsync(string host, int port, string room, string name, int timeoutMs = 5000)
    {
        try
        {
            _tcp = new TcpClient { NoDelay = true };
            var connect = _tcp.ConnectAsync(host, port);
            if (await Task.WhenAny(connect, Task.Delay(timeoutMs)).ConfigureAwait(false) != connect || !_tcp.Connected)
            { Error = $"Could not reach {host}:{port}"; Dispose(); return false; }
            await connect.ConfigureAwait(false);
            _stream = _tcp.GetStream();
            Connected = true;
            _ = Task.Run(ReadLoop);
            Send(new JObject { ["t"] = "hello", ["room"] = room, ["name"] = name, ["ver"] = MultiplayerSession.ProtocolVersion, ["platform"] = MultiplayerSession.Platform });
            return true;
        }
        catch (Exception e) { Error = e.Message; Dispose(); return false; }
    }

    private async Task ReadLoop()
    {
        try
        {
            using var reader = new StreamReader(_stream, new UTF8Encoding(false), false, 4096, leaveOpen: true);
            while (Connected)
            {
                string line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line == null) break;
                if (line.Length == 0) continue;
                try
                {
                    var o = JObject.Parse(line);
                    if ((string)o["t"] == "pong" && o["ts"] != null) { PingMs = (int)(MultiplayerSession.NowMs() - (long)o["ts"]); continue; }
                    Inbox.Enqueue(o);
                }
                catch { }
            }
        }
        catch (Exception e) { if (Connected) Error = e.Message; }
        if (Connected) { Connected = false; Inbox.Enqueue(new JObject { ["t"] = "disconnected" }); }
    }

    public void Send(JObject o)
    {
        if (!Connected || _stream == null) return;
        byte[] data = Encoding.UTF8.GetBytes(o.ToString(Newtonsoft.Json.Formatting.None) + "\n");
        try { lock (_sendLock) { _stream.Write(data, 0, data.Length); _stream.Flush(); } }
        catch (Exception e) { Error = e.Message; Connected = false; Inbox.Enqueue(new JObject { ["t"] = "disconnected" }); }
    }

    public void Dispose()
    {
        Connected = false;
        try { _stream?.Dispose(); } catch { }
        try { _tcp?.Dispose(); } catch { }
        _stream = null; _tcp = null;
    }
}

/// <summary>
/// Minimal room relay. Rooms are keyed by code; max 2 players. The first player in a room is the host.
/// Every message except hello/ping is forwarded verbatim to the other player of the room.
/// </summary>
public sealed class RelayServer : IDisposable
{
    private sealed class Conn { public TcpClient Tcp; public Stream S; public string Room; public string Name; public string Platform; public readonly object L = new object(); }
    private TcpListener _listener;
    private readonly Dictionary<string, List<Conn>> _rooms = new Dictionary<string, List<Conn>>();
    private volatile bool _running;
    public int Port { get; private set; }
    public static Action<string> Log = s => Console.WriteLine("[RELAY] " + s);

    public bool Start(int port)
    {
        try
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            Port = port; _running = true;
            _ = Task.Run(AcceptLoop);
            Log($"listening on port {port}");
            return true;
        }
        catch (Exception e) { Log($"start failed: {e.Message}"); return false; }
    }

    private async Task AcceptLoop()
    {
        while (_running)
        {
            TcpClient c;
            try { c = await _listener.AcceptTcpClientAsync().ConfigureAwait(false); }
            catch { if (!_running) break; continue; }
            c.NoDelay = true;
            _ = Task.Run(() => Serve(new Conn { Tcp = c, S = c.GetStream() }));
        }
    }

    private static void Write(Conn c, JObject o)
    {
        byte[] data = Encoding.UTF8.GetBytes(o.ToString(Newtonsoft.Json.Formatting.None) + "\n");
        try { lock (c.L) { c.S.Write(data, 0, data.Length); c.S.Flush(); } } catch { }
    }
    private static void WriteRaw(Conn c, string line)
    {
        byte[] data = Encoding.UTF8.GetBytes(line + "\n");
        try { lock (c.L) { c.S.Write(data, 0, data.Length); c.S.Flush(); } } catch { }
    }

    private async Task Serve(Conn c)
    {
        try
        {
            using var reader = new StreamReader(c.S, new UTF8Encoding(false), false, 4096, leaveOpen: true);
            while (_running)
            {
                string line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line == null) break;
                if (line.Length == 0) continue;
                JObject o;
                try { o = JObject.Parse(line); } catch { continue; }
                string t = (string)o["t"];
                if (t == "ping") { Write(c, new JObject { ["t"] = "pong", ["ts"] = o["ts"] }); continue; }
                if (c.Room == null)
                {
                    if (t != "hello") continue;
                    string room = ((string)o["room"] ?? "FNF").ToUpperInvariant();
                    int ver = (int?)o["ver"] ?? 0;
                    if (ver != MultiplayerSession.ProtocolVersion) { Write(c, new JObject { ["t"] = "error", ["msg"] = $"Protocol mismatch (server {MultiplayerSession.ProtocolVersion}, client {ver})" }); break; }
                    Conn other = null; int count;
                    lock (_rooms)
                    {
                        if (!_rooms.TryGetValue(room, out var list)) _rooms[room] = list = new List<Conn>();
                        if (list.Count >= 2) { Write(c, new JObject { ["t"] = "error", ["msg"] = "Room is full" }); break; }
                        list.Add(c); c.Room = room; c.Name = (string)o["name"] ?? "Player"; c.Platform = (string)o["platform"] ?? "?";
                        count = list.Count;
                        if (list.Count == 2) other = list[0];
                    }
                    Write(c, new JObject { ["t"] = "welcome", ["room"] = room, ["host"] = count == 1, ["peer"] = other?.Name, ["peerPlatform"] = other?.Platform });
                    if (other != null) Write(other, new JObject { ["t"] = "peer_join", ["name"] = c.Name, ["platform"] = c.Platform });
                    Log($"{c.Name} ({c.Platform}) joined room {room} ({count}/2)");
                    continue;
                }
                Conn peer = null;
                lock (_rooms) { if (_rooms.TryGetValue(c.Room, out var list)) foreach (var x in list) if (x != c) peer = x; }
                if (peer != null) WriteRaw(peer, line);
            }
        }
        catch { }
        Drop(c);
    }

    private void Drop(Conn c)
    {
        Conn peer = null;
        lock (_rooms)
        {
            if (c.Room != null && _rooms.TryGetValue(c.Room, out var list))
            {
                list.Remove(c);
                if (list.Count > 0) peer = list[0];
                else _rooms.Remove(c.Room);
            }
        }
        if (peer != null) Write(peer, new JObject { ["t"] = "peer_left", ["name"] = c.Name, ["host"] = true });
        if (c.Room != null) Log($"{c.Name} left room {c.Room}");
        try { c.Tcp.Dispose(); } catch { }
    }

    public void Dispose()
    {
        _running = false;
        try { _listener?.Stop(); } catch { }
        lock (_rooms) { foreach (var l in _rooms.Values) foreach (var c in l) try { c.Tcp.Dispose(); } catch { } _rooms.Clear(); }
    }
}

public sealed class MpPlayerResult
{
    public string Name; public int Score, Misses, Sick, Good, Bad, Shit, MaxCombo, Hits, Total; public float Accuracy;
    public JObject ToJson() => new JObject { ["name"] = Name, ["score"] = Score, ["misses"] = Misses, ["sick"] = Sick, ["good"] = Good, ["bad"] = Bad, ["shit"] = Shit, ["maxCombo"] = MaxCombo, ["hits"] = Hits, ["total"] = Total, ["acc"] = Accuracy };
    public static MpPlayerResult FromJson(JObject o) => new MpPlayerResult
    {
        Name = (string)o["name"], Score = (int?)o["score"] ?? 0, Misses = (int?)o["misses"] ?? 0, Sick = (int?)o["sick"] ?? 0, Good = (int?)o["good"] ?? 0,
        Bad = (int?)o["bad"] ?? 0, Shit = (int?)o["shit"] ?? 0, MaxCombo = (int?)o["maxCombo"] ?? 0, Hits = (int?)o["hits"] ?? 0, Total = (int?)o["total"] ?? 0, Accuracy = (float?)o["acc"] ?? 0
    };
}

/// <summary>Global multiplayer state shared by the menus and PlayScene.</summary>
public static class MultiplayerSession
{
    public enum MpMode { None, Local, Online, Psych }
    public const int ProtocolVersion = 1;
    public const int DefaultPort = 7860;
    public static MpMode Mode = MpMode.None;
    public static bool IsHost;
    public static NetClient Net;
    public static RelayServer Server;
    public static string Room = "FNF";
    public static string PeerName, PeerPlatform;
    public static bool PeerPresent;
    public static string CharOverride;            // online guest: host's selected Freeplay character
    public static MpPlayerResult[] LastResults;   // [0] = P1 (boyfriend side), [1] = P2 (opponent side)
    public static string LastSong;
    public static bool Active => Mode != MpMode.None;
    public static bool Online => Mode == MpMode.Online && Net != null && Net.Connected;
    /// <summary>Online: the host plays the boyfriend strumline (P1), the guest the opponent strumline (P2).</summary>
    public static int LocalSide => IsHost ? 0 : 1;
    private static readonly long _t0 = DateTime.UtcNow.Ticks;
    public static long NowMs() => (DateTime.UtcNow.Ticks - _t0) / TimeSpan.TicksPerMillisecond;

#if XBOX_UWP
    public const string Platform = "Xbox";
#else
    public const string Platform = "PC";
#endif
    private static string _name;
    /// <summary>Own name in LAN / online multiplayer: the saved nickname (Options > Psych Online > Nickname), else the
    /// Xbox gamertag, else a random PC-/Xbox- name.</summary>
    public static string LocalName
    {
        get
        {
            try { FNF_MonoGame.Engine.PsychEngine.ClientPrefs.EnsureLoaded(); var saved = FNF_MonoGame.Engine.PsychEngine.ClientPrefs.Data?.Nickname; if (!string.IsNullOrWhiteSpace(saved)) return saved.Trim(); } catch { }
            return !string.IsNullOrWhiteSpace(SystemKeyboard.AccountName) ? SystemKeyboard.AccountName   // Xbox gamertag
                : _name ??= Platform + "-" + new Random().Next(1000, 9999);
        }
    }

    public static void Send(JObject o) { if (Online) Net.Send(o); }

    public static bool HostRelay(int port)
    {
        if (Server != null) return true;
        Server = new RelayServer();
        if (!Server.Start(port)) { Server = null; return false; }
        return true;
    }

    public static void Leave()
    {
        try { if (Net != null && Net.Connected) Net.Send(new JObject { ["t"] = "leave" }); } catch { }
        Net?.Dispose(); Net = null;
        Server?.Dispose(); Server = null;
        PeerPresent = false; PeerName = null; CharOverride = null;
        Mode = MpMode.None;
    }

    public static string LocalIPv4()
    {
        try
        {
            foreach (var a in Dns.GetHostAddresses(Dns.GetHostName()))
                if (a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)) return a.ToString();
        }
        catch { }
        return "127.0.0.1";
    }

    /// <summary>Headless relay: FNF_MonoGame.exe --relay [port]. Runs until the process is killed.</summary>
    public static void RunHeadlessRelay(int port)
    {
        RelayServer.Log = s => { Console.WriteLine("[RELAY] " + s); };
        var srv = new RelayServer();
        if (!srv.Start(port)) return;
        while (true) Thread.Sleep(1000);
    }
}
