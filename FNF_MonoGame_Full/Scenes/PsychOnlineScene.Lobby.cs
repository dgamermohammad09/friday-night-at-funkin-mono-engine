using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.Colyseus;
using FNF_MonoGame.Gameplay;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Psych Online room lobby (RoomState.hx look): the "lobby" stage (sky/city/window/behindTrain/street, camera zoom 0.65 /
/// scroll 200,120 tweened from 0.5 / 200,130 like funnyMode), every player's character standing and bopping on their side
/// (LobbyCharacter.loadCharacter: skin[0]+suffix, fallback bf / bf-opponent; repos(): x = 200|700 + position[0] -/+ ox*400,
/// y = 120 + position[1]), profile cards above them (ProfileBox: name, ping coloured green->red by ping/400, status, READY).
/// Port additions (controller first): bottom dock of icon buttons with shortcuts, ready badges, song card (album art +
/// health icons), player list with ping, room settings panel, quick emotes (charPlay), room code copy/share, reconnect
/// overlay, join/leave/ready sounds and animations.
/// </summary>
public partial class PsychOnlineScene
{
    // ------------------------------------------------------------------ state
    private enum Overlay { None, Settings, Emote, ModPrompt }
    private Overlay _overlay = Overlay.None;
    private int _dockSel, _setSel;
    private float _camZoom = 0.5f, _camX = 200f, _camY = 130f;
    private float _camFromZoom, _camFromX, _camFromY, _camToZoom = 0.65f, _camToX = 200f, _camToY = 120f, _camT = 1f;
    private int _funnyMode = -1;
    private int _lastBeat = int.MinValue;
    private float _windowAlpha; private Color _windowColor = Color.White; private int _curLight;
    private float _codeReveal;      // RoomState: room code shown for 10 s after ACCEPT, "????" otherwise
    private float _leaveHold;       // LeavePie: hold BACK to leave
    private float _shake; private int _shakeBtn = -1;
    private readonly List<(string text, Color color, float t)> _toasts = new();
    private readonly Dictionary<string, LobbyChar> _chars = new();
    private readonly Dictionary<string, Texture2D> _tex = new();
    private Texture2D _round;       // 64x64 rounded-corner mask (radius 32) for 9-slice boxes
    private float _dockScaleT;
    private bool _lobbyInit;
    private float _roomTime;
    private bool _devDropDone, _devEmoteDone, _devOverlayDone;
    private float _devTwoT;
    private (float t, string name) _devEmoteShot; private int _devEmoteShots;   // dev: FNF_PSO_SHOT_EMOTE=1 -> shot 0.25 s after a received charPlay
    private SpriteSheet _noteSheet, _checkSheet;            // game note arrows (emote pad) + options checkboxThingie (settings)
    private readonly Dictionary<string, float> _emoteFlash = new();
    private static readonly Color BoxColor = new Color(0, 0, 0, 0x8A);   // TEXT_BG_COLOR 0x8A000000

    private sealed class LobbyChar
    {
        public string Sid, Key, CharName;
        public Character Ch;
        public bool Bf, Ready, Psych, Fallback, DualDance;
        public float Ox, Appear, Leave = -1f, Emote, ReadyPop;
        public Vector2 Size, Base;
        public float Hue, BaseOx = float.NaN;
    }

    private Texture2D Tex(string path)
    {
        if (_tex.TryGetValue(path, out var t)) return t;
        t = Assets.LoadTexture(path);
        if (t == Assets.Pixel) t = null;
        _tex[path] = t;
        return t;
    }

    // ------------------------------------------------------------------ lobby lifecycle
    private void LobbyEnter()
    {
        _lobbyInit = true; _roomTime = 0f; _overlay = Overlay.None; _dockSel = 0;
        _camZoom = 0.5f; _camX = 200f; _camY = 130f; _funnyMode = -1;   // cum.scroll.set(200, 130); cum.zoom = 0.5
        foreach (var c in _chars.Values) c.Ch?.Dispose();
        _chars.Clear();
        while (PsychOnline.LobbyAnims.TryDequeue(out _)) { }
        SyncChars(initial: true);
    }

    private static string SkinChar(SchemaObject p, bool bf)
    {
        var a = PsychOnline.SkinOf(p);
        if (a == null) return null;
        return a[0] + a[bf ? 2 : 1];
    }
    private static string SkinMod(SchemaObject p) => PsychOnline.SkinOf(p)?[3] ?? "";

    private static bool CharExists(string name, string mod = null) => PsychOnline.CharacterExists(name, mod);

    private LobbyChar MakeChar(string sid, SchemaObject p)
    {
        bool bf = p.Bool("bfSide");
        string skin = SkinChar(p, bf), skinMod = SkinMod(p);
        bool fallback = !CharExists(skin, skinMod);
        string name = fallback ? (bf ? "bf" : "bf-opponent") : skin;   // LobbyCharacter.loadCharacter fallback
        var lc = new LobbyChar { Sid = sid, Bf = bf, CharName = name, Fallback = fallback && skin != null, Key = name + "|" + bf + "|" + PsychOnline.SkinsVersion };
        lc.Hue = (sid.GetHashCode() & 0x7fffffff) % 360;
        bool prev = Character.PsychStageActive;
        string prevMod = FNF_MonoGame.Engine.PsychEngine.PsychMods.CurrentModDirectory;
        try
        {
            if (!fallback) FNF_MonoGame.Engine.PsychEngine.PsychMods.CurrentModDirectory = skinMod;   // ShitUtil.tempSwitchMod(skin[3])
            Character.PsychStageActive = true;     // Psych's own bf/bf-opponent from Content/data/psych
            var ch = new Character(name, 0, 0) { StageUsesPsychPositioning = true };
            ch.LoadJsonData(Assets, name);
            ch.SetFlipForRole(bf);                 // Psych: flipX = json.flip_x != isPlayer
            ch.LoadSprites(Game);
            ch.PlayAnimation("idle", force: true);
            lc.Ch = ch;
            lc.Psych = ch.PsychCharacterOffset != null;
            var mid = ch.GetMidpoint() - ch.Position;
            lc.Size = new Vector2(Math.Max(10f, mid.X * 2f), Math.Max(10f, mid.Y * 2f));
            var anims = ch.JsonData?.Animations;
            lc.DualDance = anims != null && anims.Any(a => a.Name == "danceLeft") && anims.Any(a => a.Name == "danceRight");
        }
        catch (Exception e) { Console.WriteLine("[PSO] lobby char " + name + ": " + e.Message); }
        finally { Character.PsychStageActive = prev; FNF_MonoGame.Engine.PsychEngine.PsychMods.CurrentModDirectory = prevMod; }
        Console.WriteLine($"[PSO] lobby character {name} for {p.Str("name")} (bf={bf}, skin={skin ?? "-"}, size={lc.Size})");
        return lc;
    }

    /// <summary>Port of RoomState player onAdd/onRemove + listen(bfSide/skin/isReady/ox).</summary>
    private void SyncChars(bool initial = false)
    {
        var seen = new HashSet<string>();
        foreach (var (sid, p) in PsychOnline.AllPlayers())
        {
            seen.Add(sid);
            bool bf = p.Bool("bfSide");
            string key = (CharExists(SkinChar(p, bf), SkinMod(p)) ? SkinChar(p, bf) : (bf ? "bf" : "bf-opponent")) + "|" + bf + "|" + PsychOnline.SkinsVersion;
            if (!_chars.TryGetValue(sid, out var lc) || lc.Leave >= 0f)
            {
                lc = MakeChar(sid, p);
                lc.Ready = p.Bool("isReady");
                lc.Appear = initial ? 1f : 0f;
                _chars[sid] = lc;
                if (!initial && sid != PsychOnline.SessionId)
                {
                    Audio.PlaySound("confirmMenu", 0.6f);
                    Toast(p.Str("name") + " joined", new Color(140, 230, 255));
                }
            }
            else if (lc.Key != key)
            {   // side swap / skin change -> loadCharacter()
                var nc = MakeChar(sid, p); nc.Appear = 1f; nc.Ready = lc.Ready; lc.Ch?.Dispose(); _chars[sid] = nc; lc = nc;
                Audio.PlaySound("scrollMenu");
            }
            lc.Ox = (float)p.Num("ox");
            bool ready = p.Bool("isReady");
            if (ready != lc.Ready)
            {
                lc.Ready = ready;
                if (ready)
                {   // RoomState isReady listener: confirmMenu + playAnim('ready')
                    Audio.PlaySound("confirmMenu", 0.5f);
                    lc.ReadyPop = 1f;
                    if (lc.Ch != null && HasAnim(lc, "ready")) lc.Ch.PlayAnimation("ready", true);
                    else if (lc.Ch != null && HasAnim(lc, "hey")) { lc.Ch.PlayAnimation("hey", true); lc.Emote = 0.9f; }
                    Toast(p.Str("name") + " is READY", new Color(120, 255, 140));
                }
                else if (!(PsychOnline.State?.Bool("isStarted") ?? false)) Audio.PlaySound("cancelMenu");
            }
        }
        foreach (var lc in _chars.Values.ToList())
            if (!seen.Contains(lc.Sid) && lc.Leave < 0f)
            {
                lc.Leave = 0f;
                Audio.PlaySound("cancelMenu", 0.7f);
                Toast("A player left the room", new Color(255, 160, 140));
            }
        // updateCharacters(): funnyMode = max ox -> camera target (set_funnyMode)
        int fm = (int)Math.Max(0, _chars.Values.Where(c => c.Leave < 0f).Select(c => c.Ox).DefaultIfEmpty(0).Max());
        if (fm != _funnyMode)
        {
            _funnyMode = fm;
            (_camToZoom, _camToX, _camToY) = fm switch { 0 => (0.65f, 200f, 120f), 1 => (0.57f, 150f, 120f), 2 => (0.45f, 50f, 180f), _ => (0.34f, 50f, 250f) };
            _camFromZoom = _camZoom; _camFromX = _camX; _camFromY = _camY; _camT = 0f;   // FlxTween 1 s quadOut
        }
    }

    private static bool HasAnim(LobbyChar lc, string anim) => lc.Ch?.JsonData?.Animations?.Any(a => a.Name == anim) ?? false;

    private void Toast(string s, Color c) { _toasts.Add((s, c, 3.5f)); if (_toasts.Count > 4) _toasts.RemoveAt(0); }

    // ------------------------------------------------------------------ dock
    private sealed class DockBtn { public string Id, Icon, Label, Tip; public Buttons Pad; public string Key; public bool Enabled = true; public int IconW, IconH; }

    private string _lastSkinStatus = "";
    private List<DockBtn> Dock()
    {
        var me = PsychOnline.Self; var st = PsychOnline.State;
        bool has = me?.Bool("hasSong") ?? false, ready = me?.Bool("isReady") ?? false;
        bool canPick = PsychOnline.HasPerms || (st?.Bool("allPlayersChoose") ?? false);
        var l = new List<DockBtn>
        {
            new DockBtn { Id = "READY", Icon = "online_play", IconW = 68, IconH = 81, Label = ready ? "UNREADY" : "READY", Pad = Buttons.X, Key = "R", Enabled = has,
                Tip = "- START GAME/READY -\nToggles your READY status.\nThe song starts when everyone is ready." },
            new DockBtn { Id = "SONG", Icon = "online_song", Label = "SONG", Pad = Buttons.RightShoulder, Key = "S", Enabled = canPick,
                Tip = "- SELECT SONG -\nPick the song and difficulty.\n(Players with host permissions only)" },
        };
        if (PsychOnline.RoomNeedsMod)
            l.Add(new DockBtn { Id = "MOD", Icon = "@d_arrow", Label = PsychOnline.ModTask != null && !PsychOnline.ModTask.IsCompleted ? "GETTING..." : "GET MOD", Pad = Buttons.RightStick, Key = "M",
                Tip = "- MOD -\nDownloads the room's mod\n" + (string.IsNullOrEmpty(PsychOnline.RoomModUrl) ? "(the host gave no URL)" : PsychOnline.RoomModUrl) });
        l.Add(new DockBtn { Id = "MODS", Icon = "@globe", Label = "MODS", Pad = Buttons.RightTrigger, Key = "G",
            Tip = "- MOD BROWSER -\nBrowse + install GameBanana mods\nwithout leaving the room.\nTheir songs show up in SONG." });
        var miss = PsychOnline.MissingSkins();
        if (miss.Count > 0)
            l.Add(new DockBtn { Id = "SKINS", Icon = "online_code", Label = PsychOnline.SkinDownloading ? "GETTING..." : "GET SKINS", Pad = Buttons.LeftStick, Key = "K",
                Tip = "- SKINS -\nDownload the custom characters\nof the other players:\n" + string.Join("\n", miss.Select(m => m.player + ": " + m.skin)) });
        l.Add(new DockBtn { Id = "CHAT", Icon = "online_chat", IconW = 85, IconH = 84, Label = "CHAT", Pad = Buttons.Y, Key = "T", Tip = "- CHAT -\nQuick messages or type your own." });
        l.Add(new DockBtn { Id = "EMOTE", Icon = "online_emote", Label = "EMOTE", Pad = Buttons.LeftShoulder, Key = "E", Tip = "- EMOTE -\nMake your character sing or HEY!\nEveryone in the room sees it." });
        l.Add(new DockBtn { Id = "CODE", Icon = "online_code", Label = "CODE", Pad = Buttons.Back, Key = "C", Tip = "- ROOM CODE -\nReveals the code and copies\n\"CODE;server\" to the clipboard\n(PC players paste it in JOIN)." });
        l.Add(new DockBtn { Id = "SETTINGS", Icon = "online_settings", IconW = 96, IconH = 89, Label = "SETTINGS", Pad = Buttons.Start, Key = "SHIFT", Tip = "- SETTINGS -\nRoom settings: public room, side,\nanarchy, win condition, ..." });
        l.Add(new DockBtn { Id = "LEAVE", Icon = "online_leave", Label = "LEAVE", Pad = Buttons.B, Key = "ESC", Tip = "- LEAVE -\nHold to leave the room." });
        return l;
    }

    // ------------------------------------------------------------------ update
    private void UpdateLobby(float dt)
    {
        if (!_lobbyInit) LobbyEnter();
        _roomTime += dt;
        // camera tween (FlxTween quadOut, 1 s)
        if (_camT < 1f)
        {
            _camT = Math.Min(1f, _camT + dt);
            float e = 1f - (1f - _camT) * (1f - _camT);
            _camZoom = MathHelper.Lerp(_camFromZoom, _camToZoom, e); _camX = MathHelper.Lerp(_camFromX, _camToX, e); _camY = MathHelper.Lerp(_camFromY, _camToY, e);
        }
        PsychOnline.PollSkins();   // automatic skin downloads (Options > Psych Online > Disable Automatic Downloads)
        if (PsychOnline.SkinStatus != _lastSkinStatus) { _lastSkinStatus = PsychOnline.SkinStatus; if (!string.IsNullOrEmpty(_lastSkinStatus)) Toast(_lastSkinStatus, new Color(255, 214, 64)); }
        SyncChars();
        if (_devEmoteShot.name != null && (_devEmoteShot.t -= dt) <= 0f) { InputManager.DevShotRequest = _devEmoteShot.name; _devEmoteShot = default; }
        // charPlay from the other players (RoomState.playerAnim(anim, sid))
        while (PsychOnline.LobbyAnims.TryDequeue(out var pa))
            if (Log("[PSO] charPlay " + pa.sid + " " + pa.anim) && _chars.TryGetValue(pa.sid, out var lc) && lc.Ch != null && HasAnim(lc, pa.anim))
            {
                lc.Ch.PlayAnimation(pa.anim, true); lc.Emote = 0.7f;
                if (Environment.GetEnvironmentVariable("FNF_PSO_SHOT_EMOTE") == "1") _devEmoteShot = (0.25f, "emote_" + pa.anim + "_" + (++_devEmoteShots));
                if (pa.anim.EndsWith("miss")) Audio.PlaySound("missnote" + (1 + (int)(_roomTime * 7) % 3), 0.25f);
            }
        // beat: freakyMenu 102 bpm (RoomState.playMusic(false) -> Conductor.bpm = 102)
        int beat = (int)Math.Floor(Audio.MusicPosition / (60000.0 / 102.0));
        if (beat != _lastBeat)
        {
            _lastBeat = beat;
            foreach (var lc in _chars.Values)
            {
                if (lc.Ch == null || lc.Leave >= 0f || lc.Ready || lc.Emote > 0f) continue;   // danceLogic: ready players don't dance
                if (lc.DualDance) lc.Ch.Dance(beat);
                else if (beat % 2 == 0) lc.Ch.PlayAnimation("idle", true);   // Psych danceEveryNumBeats = 2
            }
            if (beat % 4 == 0)
            {   // LobbyStage.beatHit: phillyWindow random light colour
                int[] cols = { 0x31A2FD, 0x31FD8C, 0xFB33F5, 0xFD4531, 0xFBA633 };
                _curLight = (_curLight + 1 + (beat / 4) % 4) % cols.Length;
                _windowColor = new Color((cols[_curLight] >> 16) & 255, (cols[_curLight] >> 8) & 255, cols[_curLight] & 255);
                _windowAlpha = 1f;
            }
        }
        _windowAlpha = Math.Max(0f, _windowAlpha - (60f / 102f) * dt * 1.5f);   // alpha -= crochet/1000 * elapsed * 1.5
        foreach (var lc in _chars.Values.ToList())
        {
            lc.Ch?.Update(dt);
            if (lc.Appear < 1f) lc.Appear = Math.Min(1f, lc.Appear + dt / 0.45f);
            if (lc.Emote > 0f) { lc.Emote -= dt; if (lc.Emote <= 0f && !lc.Ready) lc.Ch?.PlayAnimation("idle", true); }
            if (lc.ReadyPop > 0f) lc.ReadyPop = Math.Max(0f, lc.ReadyPop - dt * 2.5f);
            if (lc.Leave >= 0f) { lc.Leave += dt / 0.4f; if (lc.Leave >= 1f) { lc.Ch?.Dispose(); _chars.Remove(lc.Sid); } }
        }
        for (int i = _toasts.Count - 1; i >= 0; i--) { var t = _toasts[i]; t.t -= dt; if (t.t <= 0) _toasts.RemoveAt(i); else _toasts[i] = t; }
        if (_codeReveal > 0) _codeReveal -= dt;
        if (_shake > 0) _shake -= dt;
        _dockScaleT += dt;
        LobbyDev(); ProLobbyDev(dt);
        UpdateRoomMods(dt);
        UpdateLobbyInput(dt);
    }

    /// <summary>While reconnecting: keep characters animating, no state sync / input.</summary>
    private void UpdateLobbyVisualsOnly(float dt)
    {
        foreach (var lc in _chars.Values) lc.Ch?.Update(dt);
        _windowAlpha = Math.Max(0f, _windowAlpha - (60f / 102f) * dt * 1.5f);
    }

    private void LobbyDev()
    {
        string drop = Environment.GetEnvironmentVariable("FNF_PSO_DROP_AT");   // dev: simulate a network drop at scene time N s
        if (!_devDropDone && float.TryParse(drop, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dAt) && _time > dAt)
        { _devDropDone = true; Console.WriteLine("[PSO] dev: dropping the connection"); PsychOnline.DebugDrop(); }
        string emo = Environment.GetEnvironmentVariable("FNF_PSO_EMOTE");      // dev: "<anim>@<s>" play an emote once, s after a 2nd player is in the room
        if (!_devEmoteDone && !string.IsNullOrEmpty(emo))
        {
            var parts = emo.Split('@');
            float at = parts.Length > 1 && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var a) ? a : 4f;
            if (PsychOnline.AllPlayers().Count() >= 2) _devTwoT += (float)(1.0 / 60.0); else _devTwoT = 0f;
            if (_devTwoT > at) { _devEmoteDone = true; Console.WriteLine("[PSO] dev emote " + parts[0]); DoEmote(parts[0]); }
        }
        string ov = Environment.GetEnvironmentVariable("FNF_PSO_OVERLAY");     // dev: open a panel ("settings@3" / "emote@3" / "dock:<i>@s")
        if (!string.IsNullOrEmpty(ov) )
        {
            var parts = ov.Split('@');
            float at = parts.Length > 1 && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var a) ? a : 3f;
            if (_time > at && !_devOverlayDone)
            {
                _devOverlayDone = true;
                if (parts[0] == "settings") _overlay = Overlay.Settings;
                else if (parts[0] == "emote") _overlay = Overlay.Emote;
                else if (parts[0].StartsWith("dock:") && int.TryParse(parts[0].Substring(5), out var di)) _dockSel = di;
                else if (parts[0] == "code") ActivateDock("CODE");
            }
        }
    }

    private void DoEmote(string anim)
    {
        var me = PsychOnline.SessionId;
        if (me != null && _chars.TryGetValue(me, out var lc) && lc.Ch != null && HasAnim(lc, anim)) { lc.Ch.PlayAnimation(anim, true); lc.Emote = 0.7f; }
        _emoteFlash[anim] = _roomTime;
        PsychOnline.SendEmote(anim);   // GameClient.send("charPlay", [anim])
    }

    private bool PadOrKey(Buttons b, Keys k) => Input.IsGamePadPressed(b) || Input.IsPressed(k);

    private void UpdateLobbyInput(float dt)
    {
        if (PsychOnline.Reconnecting) return;
        var dock = Dock();
        if (_overlay == Overlay.Settings) { UpdateSettings(); return; }
        if (_overlay == Overlay.ModPrompt) { UpdateModPrompt(); return; }
        if (_overlay == Overlay.Emote)
        {
            if (Input.LeftPressed) { DoEmote("singLEFT"); }
            else if (Input.DownPressed) { DoEmote("singDOWN"); }
            else if (Input.UpPressed) { DoEmote("singUP"); }
            else if (Input.RightPressed) { DoEmote("singRIGHT"); }
            else if (Input.ConfirmPressed || Input.IsGamePadPressed(Buttons.Y)) DoEmote("hey");
            else if (Input.BackPressed || Input.IsGamePadPressed(Buttons.LeftShoulder) || Input.IsPressed(Keys.E)) { _overlay = Overlay.None; Audio.PlaySound("cancelMenu"); }
            return;
        }
        // PSO: ALT + note keys = sing animations (keyboard)
        bool alt = Input.IsHeld(Keys.LeftAlt) || Input.IsHeld(Keys.RightAlt);
        if (alt)
        {
            if (Input.IsPressed(Keys.Left)) DoEmote("singLEFT"); if (Input.IsPressed(Keys.Down)) DoEmote("singDOWN");
            if (Input.IsPressed(Keys.Up)) DoEmote("singUP"); if (Input.IsPressed(Keys.Right)) DoEmote("singRIGHT");
            return;
        }
        if (UpdatePlayersScroll(dt)) return;   // PLAYERS panel: RS / PgUp-PgDn / wheel (PsychOnlineScene.Theme.cs)
        int n = dock.Count;
        if (Input.LeftPressed || Input.UpPressed) { _dockSel = (_dockSel + n - 1) % n; Audio.PlaySound("scrollMenu"); }
        if (Input.RightPressed || Input.DownPressed) { _dockSel = (_dockSel + 1) % n; Audio.PlaySound("scrollMenu"); }
        _dockSel = Math.Clamp(_dockSel, 0, n - 1);
        // leave = hold BACK (RoomState LeavePie); a tap only shows the hint
        bool backHeld = Input.IsHeld(Keys.Escape) || Input.IsHeld(Keys.Back) || GamePad.GetState(PlayerIndex.One).IsButtonDown(Buttons.B);
        if (backHeld) { _leaveHold += dt; if (_leaveHold >= 1f) { _leaveHold = 0f; Audio.PlaySound("cancelMenu"); LeaveRoom(); return; } }
        else _leaveHold = Math.Max(0f, _leaveHold - dt * 3f);
        // shortcuts
        bool ctrl = Input.IsHeld(Keys.LeftControl) || Input.IsHeld(Keys.RightControl);
        if (Input.IsGamePadPressed(Buttons.X) || Input.IsPressed(Keys.R)) { ActivateDock("READY"); return; }
        if (Input.IsGamePadPressed(Buttons.Y) || Input.IsPressed(Keys.Tab) || Input.IsPressed(Keys.T)) { ActivateDock("CHAT"); return; }
        if (Input.IsGamePadPressed(Buttons.LeftShoulder) || Input.IsPressed(Keys.E)) { ActivateDock("EMOTE"); return; }
        if (Input.IsGamePadPressed(Buttons.RightShoulder) || (!ctrl && Input.IsPressed(Keys.S) && false)) { ActivateDock("SONG"); return; }
        if (Input.IsGamePadPressed(Buttons.Back) || Input.IsPressed(Keys.C)) { ActivateDock("CODE"); return; }
        if (Input.IsGamePadPressed(Buttons.RightTrigger) || Input.IsPressed(Keys.G)) { ActivateDock("MODS"); return; }
        if (PsychOnline.RoomNeedsMod && (Input.IsGamePadPressed(Buttons.RightStick) || Input.IsPressed(Keys.M))) { ActivateDock("MOD"); return; }
        if (Input.IsGamePadPressed(Buttons.Start) || Input.IsPressed(Keys.LeftShift) || Input.IsPressed(Keys.RightShift)) { ActivateDock("SETTINGS"); return; }
        if (Input.ConfirmPressed) ActivateDock(dock[_dockSel].Id);
    }

    private void Shake(string id) { _shake = 0.3f; _shakeBtn = Dock().FindIndex(b => b.Id == id); Audio.PlaySound("badnoise" + (1 + (int)(_roomTime * 13) % 3), 0.8f); }

    private void ActivateDock(string id)
    {
        var st = PsychOnline.State; var me = PsychOnline.Self;
        switch (id)
        {
            case "READY":
                if (string.IsNullOrEmpty(st?.Str("song"))) { SetStatus("Song isn't selected!"); Shake(id); return; }
                if (me != null && !me.Bool("hasSong")) { SetStatus("You don't have the current song/mod!"); Shake(id); PsychOnline.VerifyChart(); return; }
                PsychOnline.Send("startGame");   // toggles isReady
                break;
            case "SONG":
                if (!(PsychOnline.HasPerms || (st?.Bool("allPlayersChoose") ?? false))) { SetStatus("Only the host can do that!"); Shake(id); return; }
                Audio.PlaySound("confirmMenu"); _page = Page.Songs; _sel = _songSel = 0;
                break;
            case "MOD":
                Audio.PlaySound("confirmMenu"); _overlay = Overlay.ModPrompt; _modPromptT = 0f;   // the one-tap download card
                break;
            case "MODS": OpenModBrowser(); break;
            case "SKINS":
                if (PsychOnline.SkinDownloading) { SetStatus(PsychOnline.SkinStatus); return; }
                Audio.PlaySound("confirmMenu"); PsychOnline.PollSkins(manual: true); SetStatus(PsychOnline.SkinStatus);
                break;
            case "CHAT": Audio.PlaySound("confirmMenu"); _page = Page.Chat; _sel = 0; break;
            case "EMOTE": Audio.PlaySound("scrollMenu"); _overlay = Overlay.Emote; break;
            case "CODE":
            {
                _codeReveal = 10f;
                bool ok = PsychOnline.CopyToClipboard(PsychOnline.RoomSecret);
                Audio.PlaySound("confirmMenu");
                Toast(ok ? "Room code copied! (" + PsychOnline.RoomSecret + ")" : "Room code: " + PsychOnline.Room?.RoomId, new Color(255, 230, 120));
                break;
            }
            case "SETTINGS": Audio.PlaySound("confirmMenu"); _overlay = Overlay.Settings; _setSel = 0; PsychOnline.Send("status", "In the Room Settings"); break;
            case "LEAVE": SetStatus("Hold B / ESC to leave the room"); break;
        }
    }

    private void LeaveRoom()
    {
        PsychOnline.Disconnect(); _page = Page.Main; _sel = 0; _lobbyInit = false;
        foreach (var c in _chars.Values) c.Ch?.Dispose();
        _chars.Clear();
    }

    // ------------------------------------------------------------------ drawing
    private Matrix WorldMatrix(float sf = 1f) =>
        Matrix.CreateTranslation(-_camX * sf - 640f, -_camY * sf - 360f, 0f) * Matrix.CreateScale(_camZoom, _camZoom, 1f) * Matrix.CreateTranslation(640f, 360f, 0f);

    private Vector2 WorldToScreen(float x, float y) => Vector2.Transform(new Vector2(x, y), WorldMatrix());

    /// <summary>Draws frame <paramref name="fi"/> of an atlas animation centred at (cx, cy) (Sparrow trim offsets honoured; Offset = -frameX).</summary>
    private static void DrawFrameCentered(SpriteBatch sb, SpriteSheet sh, string anim, int fi, float cx, float cy, float scale, Color c)
    {
        var frames = sh?.GetAnimation(anim);
        if (frames == null || frames.Count == 0) return;
        var f = frames[Math.Clamp(fi, 0, frames.Count - 1)];
        float fw = f.FrameWidth > 0 ? f.FrameWidth : f.SourceRect.Width, fh = f.FrameHeight > 0 ? f.FrameHeight : f.SourceRect.Height;
        var pos = new Vector2(cx - fw * scale / 2f + f.Offset.X * scale, cy - fh * scale / 2f + f.Offset.Y * scale);
        sb.Draw(f.Texture ?? sh.Texture, pos, f.SourceRect, c, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }

    private static bool Log(string m) { Console.WriteLine(m); return true; }

    private static Color FromHsl(float h, float s, float l)
    {
        float c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs(h / 60f % 2 - 1)), m = l - c / 2;
        (float r, float g, float b) = h < 60 ? (c, x, 0f) : h < 120 ? (x, c, 0f) : h < 180 ? (0f, c, x) : h < 240 ? (0f, x, c) : h < 300 ? (x, 0f, c) : (c, 0f, x);
        return new Color(r + m, g + m, b + m);
    }

    private void DrawLobbyStage(SpriteBatch sb)
    {
        // LobbyStage objects (stages/lobby.json), train excluded like the original
        var objs = new (string img, float x, float y, float sf)[]
        { ("sky", -1585, -526, 1f), ("city", -1399, -100, 0.9f), ("window", -1131, 69, 0.9f), ("behindTrain", -94, -42, 1f), ("street", -1268, -44, 1f) };
        foreach (var o in objs)
        {
            if (o.img == "window" && _windowAlpha <= 0f) continue;
            var t = Tex("images/psychonline/lobby/" + o.img + ".png");
            if (t == null) continue;
            sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied, transformMatrix: WorldMatrix(o.sf));
            sb.Draw(t, new Vector2(o.x, o.y), o.img == "window" ? new Color(_windowColor, _windowAlpha) : Color.White);
            sb.End();
        }
    }

    private Vector2 CharPos(LobbyChar lc)
    {
        // LobbyCharacter.repos(): left x = 200 + position[0] - ox*400, right x = 700 + position[0] + ox*400, y = 120 + position[1]
        var pa = lc.Ch?.PsychCharacterOffset;
        float px = pa != null && pa.Length > 0 ? pa[0] : 0f, py = pa != null && pa.Length > 1 ? pa[1] : 350f;
        float x = lc.Bf ? 700f + px + lc.Ox * 400f : 200f + px - lc.Ox * 400f, y = 120f + py;
        if (!lc.Psych && lc.Ch != null)
        {   // V-Slice character data (Stage.addCharacter: feet = character origin): stand on Psych bf's feet line
            // (Psych bf frame 0 = 415 x 418 at slot + position [0,350])
            if (lc.BaseOx != lc.Ox)
            {
                lc.BaseOx = lc.Ox;
                float fx = (lc.Bf ? 700f + lc.Ox * 400f : 200f - lc.Ox * 400f) + 207f;
                lc.Ch.ApplyStagePosition(new[] { fx, 120f + 350f + 418f });
                lc.Base = lc.Ch.Position;
            }
            x = lc.Base.X; y = lc.Base.Y;
        }
        // join: slide in from outside; leave: slide out + fade
        float slide = lc.Leave >= 0f ? lc.Leave * lc.Leave : (1f - lc.Appear) * (1f - lc.Appear);
        x += (lc.Bf ? 900f : -900f) * slide;
        return new Vector2(x, y);
    }

    private void DrawLobbyChars(SpriteBatch sb)
    {
        var raster = new RasterizerState { CullMode = CullMode.None };
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied, transformMatrix: WorldMatrix(), rasterizerState: raster);
        // charactersLayer sorted by ox (furthest first)
        foreach (var lc in _chars.Values.OrderByDescending(c => c.Ox))
        {
            if (lc.Ch == null) continue;
            var p = CharPos(lc);
            lc.Ch.Position = p;
            float a = lc.Leave >= 0f ? 1f - lc.Leave : Math.Min(1f, lc.Appear * 1.5f);
            lc.Ch.Tint = lc.Fallback ? new Color(255, 255, 255) * a : Color.White * a;
            lc.Ch.Draw(sb, Assets, p.X, p.Y);
        }
        sb.End();
    }

    private static readonly Dictionary<string, Color> DiffColors = new()
    {
        ["Easy"] = new Color(110, 230, 110), ["Normal"] = new Color(255, 230, 90), ["Hard"] = new Color(255, 100, 100),
        ["Erect"] = new Color(200, 120, 255), ["Nightmare"] = new Color(255, 60, 160),
    };

    private Texture2D IconTex(string ch)
    {
        if (string.IsNullOrEmpty(ch)) return null;
        string c = ch.ToLowerInvariant();
        foreach (var cand in new[] { c, c.Replace("-playable", ""), c.Split('-')[0] })
        {
            var t = Tex("images/icons/icon-" + cand + ".png");
            if (t != null) return t;
        }
        return Tex("images/icons/icon-face.png");
    }

    /// <summary>Whole room page: stage + characters (world camera), then the HUD (camHUD).</summary>
    private void DrawLobby(SpriteBatch sb, bool hud = true)
    {
        if (!_lobbyInit && PsychOnline.Connected) LobbyEnter();
        DrawLobbyStage(sb);
        DrawLobbyChars(sb);
        FunkUI.Begin(sb);   // HUD: ProUI flat panels (1280x720 target, K = 1)
        bool hasState = PsychOnline.State != null && PsychOnline.Players != null;   // none while (re)joining
        if (hud && hasState) ProDrawLobbyHud(sb);
        if (PsychOnline.Reconnecting || (_page == Page.Room && !PsychOnline.Connected))
        {
            ProUI.Dim(0.8f);
            ProUI.Title("RECONNECTING" + new string('.', 1 + (int)(_time * 3) % 3), 640f, 280f, 0.5f, _time, 0.5f);
            ProUI.Text("Connection lost - attempt " + Math.Max(1, PsychOnline.ReconnectAttempt) + " (the server keeps your seat for 20 s)", 640f, 350f, 17f, ProUI.Fg, 0.5f);
            ProUI.Text("Hold B / ESC to give up", 640f, 384f, 15f, ProUI.Fg2, 0.5f);
        }
        sb.End();
    }
}
