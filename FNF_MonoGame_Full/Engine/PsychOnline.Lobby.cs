using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using FNF_MonoGame.Engine.Colyseus;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Psych Online lobby helpers (round 3): lobby emotes (charPlay, RoomState.playerAnim), reconnect after a dropped
/// connection (GameClient.reconnect: POST /matchmake/reconnect/{roomId} {reconnectionToken}; the server keeps the
/// seat for 20 s, GameRoom.onLeave allowReconnection), room secret + clipboard (GameClient.getRoomSecret(true)).
/// </summary>
public static partial class PsychOnline
{
    /// <summary>charPlay received in the lobby: (sessionId, animation). RoomState.onMessage("charPlay").</summary>
    public static readonly ConcurrentQueue<(string sid, string anim)> LobbyAnims = new ConcurrentQueue<(string, string)>();

    internal static void QueueLobbyAnim(object msg)
    {
        // server: broadcast("charPlay", [client.sessionId, message]) where message = [anim]
        if (msg is List<object> l && l.Count >= 2 && l[0] is string sid && l[1] is List<object> m && m.Count > 0 && m[0] is string anim)
        {
            LobbyAnims.Enqueue((sid, anim));
            while (LobbyAnims.Count > 16) LobbyAnims.TryDequeue(out _);
        }
    }

    /// <summary>RoomState.playerAnim(anim) for the local player: GameClient.send("charPlay", [anim]).</summary>
    public static void SendEmote(string anim) => Send("charPlay", new List<object> { anim });

    // ------------------------------------------------------------------ reconnect
    public static Task<string> ReconnectTask;
    public static int ReconnectAttempt;
    private static string _lastRoomId, _lastToken, _lastServer;

    /// <summary>True when the room closed without us leaving (network drop) and we still hold a reconnection token.</summary>
    public static bool CanReconnect
    {
        get
        {
            var r = Room;
            return r != null && r.Closed && r.CloseCode != 4000 && r.CloseCode != 1000 && r.CloseCode != 4002
                && !string.IsNullOrEmpty(r.ReconnectionToken) && !string.IsNullOrEmpty(r.RoomId);
        }
    }

    public static bool Reconnecting => ReconnectTask != null && !ReconnectTask.IsCompleted;

    public static void StartReconnect()
    {
        if (Reconnecting || !CanReconnect) return;
        _lastRoomId = Room.RoomId; _lastToken = Room.ReconnectionToken; _lastServer = Server;
        ReconnectAttempt = 0;
        AddLog("Connection lost - reconnecting ...");
        ReconnectTask = ReconnectLoop();
    }

    private static async Task<string> ReconnectLoop()
    {
        string err = null;
        var t0 = DateTime.UtcNow;
        while ((DateTime.UtcNow - t0).TotalSeconds < 18)   // the server keeps the seat for 20 s
        {
            ReconnectAttempt++;
            try
            {
                var room = await ColyseusRoom.Matchmake(_lastServer, "reconnect", _lastRoomId,
                    new JObject { ["reconnectionToken"] = _lastToken }).ConfigureAwait(false);
                Attach(room, keepLog: true);
                AddLog("Reconnected to room " + room.RoomId + " (attempt " + ReconnectAttempt + ")");
                return null;
            }
            catch (ColyseusException e) { err = $"{e.Code}: {e.Message}"; }
            catch (Exception e) { err = (e.InnerException ?? e).Message; }
            Console.WriteLine("[PSO] reconnect attempt " + ReconnectAttempt + " failed: " + err);
            await Task.Delay(1500).ConfigureAwait(false);
        }
        LastError = err ?? "timed out";
        return LastError;
    }

    /// <summary>Dev: drop the socket without LEAVE_ROOM (simulates a network loss) to test reconnects.</summary>
    public static void DebugDrop() { try { Room?.DebugDrop(); } catch { } }

    // ------------------------------------------------------------------ room code
    /// <summary>GameClient.getRoomSecret(true): "CODE;server" - Psych Online's JOIN pastes this from the clipboard.</summary>
    public static string RoomSecret => Room == null ? "" : Room.RoomId + ";" + Server;

    /// <summary>Copy text to the system clipboard (desktop: Win32; Xbox/UWP: DataTransfer). Returns false when unavailable.</summary>
    public static bool CopyToClipboard(string text)
    {
        try
        {
#if WINDOWS_UAP
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
            return true;
#else
            if (!OpenClipboard(IntPtr.Zero)) return false;
            try
            {
                EmptyClipboard();
                IntPtr h = Marshal.StringToHGlobalUni(text);
                if (SetClipboardData(13 /* CF_UNICODETEXT */, h) == IntPtr.Zero) { Marshal.FreeHGlobal(h); return false; }
                return true;
            }
            finally { CloseClipboard(); }
#endif
        }
        catch (Exception e) { Console.WriteLine("[PSO] clipboard: " + e.Message); return false; }
    }

#if !WINDOWS_UAP
    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetClipboardData(uint uFormat, IntPtr data);
#endif

    // ------------------------------------------------------------------ song card data
    public sealed class SongCard { public string Title, Artist, Album, Player, Opponent; }
    private static readonly Dictionary<string, SongCard> _cards = new Dictionary<string, SongCard>();

    /// <summary>Title/artist/album/characters for the room song, from the engine's songs/&lt;variation&gt;/charts/meta.json.</summary>
    public static SongCard CardFor(string folder, string song, string modDir)
    {
        string key = folder + "|" + song + "|" + modDir;
        if (_cards.TryGetValue(key, out var c)) return c;
        c = new SongCard { Title = folder };
        try
        {
            var r = Resolve(folder, song, modDir);
            if (r != null && r.ModPath == null)
            {
                string meta = System.IO.Path.Combine(AssetManager.ContentRoot, "songs", r.Song, "charts", "meta.json");
                if (!global::FNF_MonoGame.Engine.FsCI.FileExists(meta) && r.Song.Contains("_"))
                    meta = System.IO.Path.Combine(AssetManager.ContentRoot, "songs", r.Song.Substring(0, r.Song.LastIndexOf('_')), "charts", "meta.json");
                if (global::FNF_MonoGame.Engine.FsCI.FileExists(meta))
                {
                    var j = JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(meta));
                    c.Title = (string)j["songName"] ?? folder;
                    c.Artist = (string)j["artist"];
                    c.Album = (string)j["playData"]?["album"];
                    c.Player = (string)j["playData"]?["characters"]?["player"];
                    c.Opponent = (string)j["playData"]?["characters"]?["opponent"];
                }
            }
            else if (r != null)
            {
                // Psych mod chart: song.player1 / player2
                var j = JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(ChartPath(folder, song, modDir)));
                var s = j["song"] as JObject ?? j;
                c.Title = (string)s["song"] ?? folder;
                c.Player = (string)s["player1"]; c.Opponent = (string)s["player2"];
            }
        }
        catch (Exception e) { Console.WriteLine("[PSO] song card: " + e.Message); }
        _cards[key] = c;
        return c;
    }
}
