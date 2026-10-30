using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// ONLINE round 5: mods from inside a Psych Online room.
/// - MODS dock button (RT / G) and the song list's "GET MORE SONGS" row (RT / G) open the GameBanana browser in room mode
///   (ModBrowserScene keeps the Colyseus room polled = heartbeats answered, shows room chat as a small overlay); coming back
///   lands in the SAME room, on the song list at the newly installed mod's first song.
/// - When the host picks a song from a mod we don't have, a one-tap "YOU NEED A MOD" card pops up (A = download with progress,
///   Y = the mod's GameBanana page in the browser / the link page, B = later; the dock keeps a GET MOD button). After the
///   install the chart is checked again (verifyChart) and we send READY as soon as the server says we have the song.
/// Dev hooks: FNF_PSO_AUTOMOD=1 (taps A on the card), FNF_PSO_BROWSE_AT=&lt;s&gt; (open the browser s seconds after a 2nd player
/// joined), FNF_PSO_PICKNEW=&lt;Diff&gt; (on return, pick the new mod's first song at that difficulty).
/// </summary>
public partial class PsychOnlineScene
{
    private string _retMod;            // mod installed in the browser (null = nothing new)
    private bool _retFromBrowser;
    private string _modPromptKey, _modPromptDismissed;
    private float _modPromptT;
    private static bool _devBrowseDone, _devPickNewDone;   // dev hooks fire once per run (a new scene object is made on every return)
    private float _devBrowseTwoT;

    /// <summary>Scene to go back to from the mod browser (the room survives: PsychOnline is static and kept polled).</summary>
    public static PsychOnlineScene BackToRoom(string installedMod) => new PsychOnlineScene { _retMod = installedMod, _retFromBrowser = true };

    private void OpenModBrowser(int modId = 0, string link = null, string why = null)
    {
        Audio.PlaySound("confirmMenu");
        PsychOnline.Send("status", "Browsing mods");
        Game.Scenes.ChangeScene(new ModBrowserScene(null, room: true, openModId: modId, openLink: link, linkWhy: why));
    }

    /// <summary>Called from Load() when we come back from the browser.</summary>
    private void AfterBrowser()
    {
        _retFromBrowser = false;
        if (!PsychOnline.Connected) return;
        bool canPick = PsychOnline.HasPerms || (PsychOnline.State?.Bool("allPlayersChoose") ?? false);
        int first = PsychOnline.FirstSongOfMod(_retMod);
        if (_retMod != null && first >= 0 && canPick)
        {
            _page = Page.Songs; _sel = _songSel = first;
            var e = PsychOnline.Songs()[first];
            _diffSel = Math.Max(0, e.Diffs.IndexOf("Hard") >= 0 ? e.Diffs.IndexOf("Hard") : e.Diffs.Count - 1);
            int n = PsychOnline.SongCountOfMod(_retMod);
            SetStatus(n + " new song" + (n == 1 ? "" : "s") + " from " + _retMod + " - pick one for the room!");
        }
        else if (_retMod != null && first < 0) SetStatus(_retMod + " has no Psych songs the room can play");
        else SetStatus("Back in the room");
    }

    /// <summary>Lobby tick: auto-open the mod card, auto-ready after an install, dev hooks.</summary>
    private void UpdateRoomMods(float dt)
    {
        var st = PsychOnline.State; var me = PsychOnline.Self;
        bool jobRunning = PsychOnline.ModTask != null && !PsychOnline.ModTask.IsCompleted;
        string key = PsychOnline.RoomNeedsMod ? st?.Str("song") + "|" + st?.Str("modDir") + "|" + PsychOnline.RoomModUrl : null;
        if (key != null && key != _modPromptKey && key != _modPromptDismissed && _overlay == Overlay.None && !(me?.Bool("hasSong") ?? false))
        {   // the host picked a song from a mod we lack -> pop the card once per song/mod
            _modPromptKey = key; _overlay = Overlay.ModPrompt; _modPromptT = 0f; _modFail = null;
            Audio.PlaySound("scrollMenu");
            Console.WriteLine("[PSO] mod prompt: " + key);
        }
        if (_overlay == Overlay.ModPrompt)
        {
            _modPromptT += dt;
            bool justFinished = PsychOnline.ModJob != null && PsychOnline.ModJob.Done && PsychOnline.ModJob.Error == null;
            if (!jobRunning && key == null && (!justFinished || (me?.Bool("hasSong") ?? false) || _modPromptT > 12f)) _overlay = Overlay.None;   // host changed the song / we have it now
        }
        // auto-ready after the mod install: the server sets hasSong once our verifyChart hash matches the host's
        if (PsychOnline.ReadyAfterVerifyFor != null)
        {
            PsychOnline.ReadyAfterVerifyT += dt;
            if (st == null || st.Str("song") != PsychOnline.ReadyAfterVerifyFor) PsychOnline.ReadyAfterVerifyFor = null;
            else if (me != null && me.Bool("hasSong"))
            {
                PsychOnline.ReadyAfterVerifyFor = null;
                if (!me.Bool("isReady") && !st.Bool("isStarted")) PsychOnline.Send("startGame");
                Toast("Mod installed + chart checked - you're READY!", new Color(120, 255, 140));
                Console.WriteLine("[PSO] mod chart verified -> ready");
            }
            else if (PsychOnline.ReadyAfterVerifyT > 10f)
            {
                PsychOnline.ReadyAfterVerifyFor = null;
                Toast("The installed chart doesn't match the host's!", new Color(255, 140, 120));
            }
        }
        // dev hooks
        string ba = Environment.GetEnvironmentVariable("FNF_PSO_BROWSE_AT");
        if (!_devBrowseDone && float.TryParse(ba, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var at))
        {
            if (PsychOnline.AllPlayers().Count() >= 2 || Environment.GetEnvironmentVariable("FNF_PSO_BROWSE_SOLO") == "1") _devBrowseTwoT += dt;
            if (_devBrowseTwoT > at) { _devBrowseDone = true; Console.WriteLine("[PSO] dev: opening the mod browser"); OpenModBrowser(); }
        }
    }

    private string _modFail;

    private void UpdateModPrompt()
    {
        var job = PsychOnline.ModJob;
        bool running = PsychOnline.ModTask != null && !PsychOnline.ModTask.IsCompleted;
        bool devTap = Environment.GetEnvironmentVariable("FNF_PSO_AUTOMOD") == "1" && _modPromptT > 1.2f && !running && job?.ModUrl != PsychOnline.RoomModUrl && _modFail == null;
        if (running)
        {
            if (Input.BackPressed) { job?.Cancel.Cancel(); Audio.PlaySound("cancelMenu"); }
            return;
        }
        if (Input.ConfirmPressed || devTap)
        {
            if (string.IsNullOrEmpty(PsychOnline.RoomModUrl)) { Audio.PlaySound("cancelMenu"); SetStatus("The host didn't give a URL for this mod - ask them to send one"); return; }
            Audio.PlaySound("confirmMenu");
            _modFail = null;
            PsychOnline.StartModDownload(PsychOnline.RoomModUrl);
            SetStatus("Downloading " + (PsychOnline.State?.Str("modDir") ?? "the mod") + " ...");
            return;
        }
        if (Input.IsGamePadPressed(Buttons.Y) || Input.IsPressed(Keys.Y) || Input.IsPressed(Keys.Tab))
        {   // the mod's page: GameBanana mod -> in-game browser detail page; other links -> link page / PC browser
            string url = job?.BrowserUrl ?? PsychOnline.RoomModUrl;
            if (string.IsNullOrEmpty(url)) { Audio.PlaySound("cancelMenu"); return; }
            int id = GameBanana.ModIdFromUrl(url);
            if (id > 0) OpenModBrowser(id);
            else OpenModBrowser(0, url, _modFail ?? "The room's mod is hosted on " + ExternalDownloads.HostOf(url));
            return;
        }
        if (Input.BackPressed)
        {
            Audio.PlaySound("cancelMenu");
            _modPromptDismissed = _modPromptKey; _overlay = Overlay.None;
        }
    }

    /// <summary>Called by UpdateRest when the room mod job finished.</summary>
    private void OnRoomModFinished(string result)
    {
        bool ok = result != null && !result.StartsWith("ERROR");
        if (!ok)
        {
            _modFail = result?.Substring(Math.Min(6, result.Length)).Trim();
            if (_overlay == Overlay.None && PsychOnline.RoomNeedsMod) { _overlay = Overlay.ModPrompt; _modPromptT = 0f; }
            return;
        }
        PsychOnline.ReadyAfterVerifyFor = PsychOnline.State?.Str("song"); PsychOnline.ReadyAfterVerifyT = 0f;
        Toast("Installed " + result + " - checking the chart ...", new Color(255, 230, 120));
    }

    // ------------------------------------------------------------------ drawing: the "you need a mod" card
    private void DrawModPrompt(SpriteBatch sb) => ProDrawModPrompt();
}
