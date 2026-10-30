using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace FNF_MonoGame.Engine.Colyseus;

// Minimal Colyseus 0.17 client (port of @colyseus/sdk Client.ts + Room.ts, shared-types Protocol.ts):
//   1. HTTP POST {http}/matchmake/{create|joinById|join|joinOrCreate}/{room} with the JSON options
//      -> seat reservation { name, sessionId, roomId, processId, publicAddress? } or { code, error }.
//   2. WebSocket {ws}/{processId}/{roomId}?sessionId=..  -> JOIN_ROOM(10) [len token][len serializerId][reflection]
//      -> client acks with [10]; then ROOM_STATE(14) / ROOM_STATE_PATCH(15) (schema) and ROOM_DATA(13) messages.
//   3. room.send(type, payload) = [13][schema string type][msgpack payload].
// Network I/O runs on background tasks; everything user-visible is dispatched from Poll() on the game thread.
// Uses only HttpClient + ClientWebSocket (both available on UWP / Xbox).
public sealed class ColyseusRoom : IDisposable
{
    public const byte JOIN_ROOM = 10, ERROR = 11, LEAVE_ROOM = 12, ROOM_DATA = 13, ROOM_STATE = 14, ROOM_STATE_PATCH = 15, ROOM_DATA_BYTES = 17, PING = 18;

    public string Endpoint, RoomName, RoomId, SessionId, ProcessId, ReconnectionToken, SerializerId;
    public SchemaDecoder Decoder;
    public SchemaObject State => Decoder?.State;
    public bool Joined, Closed, HasState;
    public int CloseCode;
    public string LastError;

    /// <summary>(type, payload) for ROOM_DATA; payload is msgpack-decoded (null/bool/long/double/string/List/Dictionary).</summary>
    public Action<string, object> OnMessage;
    public Action OnStateChange, OnJoin;
    public Action<int, string> OnError;
    public Action<int> OnLeave;
    public static Action<string> Log = s => Console.WriteLine("[COLYSEUS] " + s);

    private ClientWebSocket _ws;
    private readonly ConcurrentQueue<byte[]> _in = new ConcurrentQueue<byte[]>();
    private readonly BlockingCollection<byte[]> _out = new BlockingCollection<byte[]>();
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private int _closeCodePending = -1;

    // ------------------------------------------------------------------ endpoints
    public static string HttpBase(string endpoint)
    {
        endpoint = endpoint.Trim().TrimEnd('/');
        if (endpoint.StartsWith("wss://")) return "https://" + endpoint.Substring(6);
        if (endpoint.StartsWith("ws://")) return "http://" + endpoint.Substring(5);
        if (endpoint.StartsWith("http")) return endpoint;
        return "http://" + endpoint;
    }
    public static string WsBase(string endpoint)
    {
        var h = HttpBase(endpoint);
        return h.StartsWith("https://") ? "wss://" + h.Substring(8) : "ws://" + h.Substring(7);
    }

    private static HttpClient _http;
    private static HttpClient Http => _http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>Matchmaking request + websocket connect. Throws ColyseusException with the server's code/message.</summary>
    public static async Task<ColyseusRoom> Matchmake(string endpoint, string method, string roomNameOrId, JObject options)
    {
        string url = HttpBase(endpoint) + "/matchmake/" + method + "/" + Uri.EscapeDataString(roomNameOrId);
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent((options ?? new JObject()).ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
        };
        req.Headers.Accept.ParseAdd("application/json");
        var resp = await Http.SendAsync(req).ConfigureAwait(false);
        string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        JObject j = null;
        try { j = JObject.Parse(body); } catch { }
        if (!resp.IsSuccessStatusCode || j == null || j["error"] != null || j["sessionId"] == null)
        {
            int code = (int?)j?["code"] ?? (int)resp.StatusCode;
            string msg = (string)j?["error"] ?? (string)j?["message"] ?? body;
            throw new ColyseusException(code, msg);
        }
        var room = new ColyseusRoom
        {
            Endpoint = endpoint,
            RoomName = (string)j["name"],
            RoomId = (string)j["roomId"],
            SessionId = (string)j["sessionId"],
            ProcessId = (string)j["processId"],
        };
        string pub = (string)j["publicAddress"];
        string wsBase = WsBase(endpoint);
        if (!string.IsNullOrEmpty(pub)) wsBase = (wsBase.StartsWith("wss") ? "wss://" : "ws://") + pub;
        string wsUrl = wsBase + "/" + room.ProcessId + "/" + room.RoomId + "?sessionId=" + Uri.EscapeDataString(room.SessionId);
        // Client.createMatchMakeRequest: "forward reconnection token during reconnect methods"
        string rtok = (string)j["reconnectionToken"] ?? (method == "reconnect" ? (string)options?["reconnectionToken"] : null);
        if (rtok != null) wsUrl += "&reconnectionToken=" + Uri.EscapeDataString(rtok);
        await room.Connect(wsUrl).ConfigureAwait(false);
        return room;
    }

    public static Task<ColyseusRoom> Create(string endpoint, string roomName, JObject options) => Matchmake(endpoint, "create", roomName, options);
    public static Task<ColyseusRoom> JoinById(string endpoint, string roomId, JObject options) => Matchmake(endpoint, "joinById", roomId, options);

    /// <summary>GET {http}/rooms/{roomName} (Funkin-Online-Server route used by GameClient.getAvailableRooms): public rooms.</summary>
    public static async Task<JArray> GetAvailableRooms(string endpoint, string roomName = "room")
    {
        var s = await Http.GetStringAsync(HttpBase(endpoint) + "/rooms/" + Uri.EscapeDataString(roomName)).ConfigureAwait(false);
        try { return JArray.Parse(s); } catch { return new JArray(); }
    }

    private async Task Connect(string url)
    {
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(new Uri(url), _cts.Token).ConfigureAwait(false);
        _ = Task.Run(ReceiveLoop);
        _ = Task.Run(SendLoop);
    }

    private async Task ReceiveLoop()
    {
        var buf = new byte[64 * 1024];
        var ms = new MemoryStream();
        try
        {
            while (_ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                var r = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), _cts.Token).ConfigureAwait(false);
                if (r.MessageType == WebSocketMessageType.Close)
                {
                    _closeCodePending = (int?)r.CloseStatus ?? 1005;
                    if (!string.IsNullOrEmpty(r.CloseStatusDescription)) LastError = r.CloseStatusDescription;
                    break;
                }
                ms.Write(buf, 0, r.Count);
                if (!r.EndOfMessage) continue;
                _in.Enqueue(ms.ToArray());
                ms.SetLength(0);
            }
        }
        catch (Exception e) { if (!_cts.IsCancellationRequested) { LastError = e.Message; _closeCodePending = 1006; } }
        if (_closeCodePending < 0) _closeCodePending = 1006;
    }

    private async Task SendLoop()
    {
        try
        {
            foreach (var m in _out.GetConsumingEnumerable(_cts.Token))
            {
                if (_ws.State != WebSocketState.Open) break;
                await _ws.SendAsync(new ArraySegment<byte>(m), WebSocketMessageType.Binary, true, _cts.Token).ConfigureAwait(false);
            }
        }
        catch (Exception e) { if (!_cts.IsCancellationRequested) LastError = e.Message; }
    }

    private void Raw(byte[] data) { if (!Closed) _out.Add(data); }

    // ------------------------------------------------------------------ send (Room.send)
    public void Send(string type, object payload = null, bool hasPayload = false)
    {
        var ms = new MemoryStream();
        ms.WriteByte(ROOM_DATA);
        MsgPack.WriteString(ms, type);
        if (payload != null || hasPayload) MsgPack.Write(ms, payload);
        Raw(ms.ToArray());
    }

    public void Leave(bool consented = true)
    {
        if (Closed) return;
        try
        {
            if (consented && _ws != null && _ws.State == WebSocketState.Open)
            {
                Raw(new byte[] { LEAVE_ROOM });
                Thread.Sleep(50);
                _ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).Wait(500);
            }
        }
        catch { }
        Close(4000);
    }

    private void Close(int code)
    {
        if (Closed) return;
        Closed = true; CloseCode = code;
        try { _cts.Cancel(); } catch { }
        try { _ws?.Abort(); } catch { }
        OnLeave?.Invoke(code);
    }

    public void Dispose() => Leave();

    /// <summary>Dev: abort the socket without LEAVE_ROOM (network drop).</summary>
    public void DebugDrop() { try { _ws?.Abort(); } catch { } }

    // ------------------------------------------------------------------ incoming (Room.onMessageCallback)
    /// <summary>Process queued frames; call from the game thread every frame.</summary>
    public void Poll()
    {
        int n = 0;
        while (n++ < 256 && _in.TryDequeue(out var b))
        {
            try { Handle(b); }
            catch (Exception e) { Log("bad frame " + (b.Length > 0 ? b[0] : -1) + ": " + e.Message); }
        }
        if (_in.IsEmpty && _closeCodePending >= 0 && !Closed) Close(_closeCodePending);
    }

    private void Handle(byte[] b)
    {
        if (b.Length == 0) return;
        int o = 1;
        switch (b[0])
        {
            case JOIN_ROOM:
            {
                int l1 = b[o++]; ReconnectionToken = Encoding.UTF8.GetString(b, o, l1); o += l1;
                int l2 = b[o++]; SerializerId = Encoding.UTF8.GetString(b, o, l2); o += l2;
                if (b.Length > o && Decoder == null && SerializerId == "schema")
                    Decoder = SchemaDecoder.FromReflection(b, o);
                Raw(new byte[] { JOIN_ROOM });
                bool first = !Joined;
                Joined = true;
                if (first) OnJoin?.Invoke();
                break;
            }
            case ERROR:
            {
                int code = (int)SchemaDecoder.Number(b, ref o);
                string msg = SchemaDecoder.String(b, ref o);
                LastError = msg;
                Log("error " + code + ": " + msg);
                OnError?.Invoke(code, msg);
                break;
            }
            case LEAVE_ROOM: Leave(); break;
            case ROOM_STATE:
            case ROOM_STATE_PATCH:
                if (Decoder != null) { Decoder.Decode(b, o); HasState = true; OnStateChange?.Invoke(); }
                break;
            case ROOM_DATA:
            {
                string type = SchemaDecoder.StringCheck(b, o) ? SchemaDecoder.String(b, ref o) : SchemaDecoder.Number(b, ref o).ToString(System.Globalization.CultureInfo.InvariantCulture);
                object msg = o < b.Length ? MsgPack.Decode(b, ref o) : null;
                OnMessage?.Invoke(type, msg);
                break;
            }
            case ROOM_DATA_BYTES:
            {
                string type = SchemaDecoder.StringCheck(b, o) ? SchemaDecoder.String(b, ref o) : SchemaDecoder.Number(b, ref o).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var rest = new byte[b.Length - o]; System.Array.Copy(b, o, rest, 0, rest.Length);
                OnMessage?.Invoke(type, rest);
                break;
            }
        }
    }
}

public sealed class ColyseusException : Exception
{
    public readonly int Code;
    public ColyseusException(int code, string msg) : base(msg) { Code = code; }
}
