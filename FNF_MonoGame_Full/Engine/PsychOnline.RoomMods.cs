using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FNF_MonoGame.Engine;

// Psych Online + mod browser glue (ONLINE round 5): keep the room alive while another scene (the GameBanana browser) is open,
// room feed for small overlays, auto-ready after a room mod was installed and verified.
public static partial class PsychOnline
{
    /// <summary>Incremented for every Log line (Log itself is capped at 40 entries, so its Count stops growing).</summary>
    public static int LogSerial;

    /// <summary>In a room, or trying to get back into it (reconnect in progress).</summary>
    public static bool InRoom => Connected || Reconnecting || ReconnectTask != null || Joining;

    /// <summary>Room song for which we should send READY as soon as our chart check passes (set after a room mod install).</summary>
    public static string ReadyAfterVerifyFor;
    public static float ReadyAfterVerifyT;

    /// <summary>
    /// For scenes other than the lobby (mod browser): Poll() answers the server pings (heartbeats) and processes the state; a dropped
    /// socket is reconnected like the lobby does (GameClient.reconnect, the server keeps the seat for 20 s).
    /// Returns false when the room is gone for good.
    /// </summary>
    public static bool KeepAlive()
    {
        Poll();
        if (Room == null && ReconnectTask == null) return false;
        if (Connected)
        {
            if (ReconnectTask != null && ReconnectTask.IsCompleted) ReconnectTask = null;
            return true;
        }
        if (Reconnecting || Joining) return true;
        if (ReconnectTask != null && ReconnectTask.IsCompleted)
        {
            string err = ReconnectTask.Status == TaskStatus.RanToCompletion ? ReconnectTask.Result : "failed";
            ReconnectTask = null;
            if (err != null) { AddLog("Disconnected from the room: " + err); Disconnect(); return false; }
            return true;
        }
        if (CanReconnect) { StartReconnect(); return true; }
        AddLog("Disconnected from the room" + (LastError != null ? ": " + LastError : ""));
        Disconnect();
        return false;
    }

    /// <summary>Songs of one installed mod folder (newly installed from the browser) in the room song list.</summary>
    public static int FirstSongOfMod(string mod)
    {
        if (string.IsNullOrEmpty(mod)) return -1;
        var l = Songs();
        for (int i = 0; i < l.Count; i++) if (string.Equals(l[i].ModDir, mod, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    public static int SongCountOfMod(string mod) => string.IsNullOrEmpty(mod) ? 0 : Songs().Count(e => string.Equals(e.ModDir, mod, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// modURL to send with setSong for an installed mod: the GameBanana direct file link we installed from (pso_url.txt, which the
    /// real Psych Online client can download) when the mod came from a GameBanana page, else mod_url.txt as is.
    /// </summary>
    public static string RoomUrlForMod(string mod, string modUrl)
    {
        try
        {
            if (!string.IsNullOrEmpty(mod) && !string.IsNullOrEmpty(ModsBase) && GameBanana.ModIdFromUrl(modUrl) > 0)
            {
                string p = System.IO.Path.Combine(ModsBase, mod, GameBanana.PsoUrlFile);
                if (global::FNF_MonoGame.Engine.FsCI.FileExists(p)) { string d = global::FNF_MonoGame.Engine.FsCI.ReadAllText(p).Trim(); if (GameBanana.IsGbFileUrl(d)) return d; }
            }
        }
        catch { }
        return modUrl ?? "";
    }

    /// <summary>Name of the player who picked the song (the host) for the mod prompt.</summary>
    public static string HostName()
    {
        string h = State?.Str("host");
        foreach (var (sid, p) in AllPlayers()) if (sid == h) return p.Str("name");
        return "The host";
    }
}
