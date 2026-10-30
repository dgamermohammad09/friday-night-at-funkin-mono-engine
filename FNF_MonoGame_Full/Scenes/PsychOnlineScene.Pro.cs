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
/// Psych Online screens, professional flat redesign (ProUI): lobby HUD over the PSO lobby stage (profile cards above both
/// characters like RoomState's ProfileBox, right info column = song / players / chat, bottom action bar, one hint bar),
/// room settings (RoomSettingsSubstate items incl. Only Network Players, Team Mode, One Lane Mode, stage, song speed, kick),
/// emote pad, "you need a mod" dialog, song select (list + detail pane), front page, rooms / chat / account lists, room-code
/// entry, text entry, connecting, and the network LEADERBOARD + profile (TopPlayerSubstate / ProfileTab).
/// </summary>
public partial class PsychOnlineScene
{
    // ================================================================== lobby HUD
    // lobby HUD: PsychOnlineScene.Theme.cs (FunkTheme)

    private void ProDrawLobbyHints()
    {
        var dock = Dock(); int n = dock.Count; bool readyNow = PsychOnline.Self?.Bool("isReady") ?? false;
        // hint bar: focused action's description on the left
        string tip = _overlay == Overlay.None && _dockSel < n ? dock[_dockSel].Tip.Split('\n').Skip(1).FirstOrDefault() ?? "" : "";
        if (_overlay == Overlay.None && _dockSel < n && dock[_dockSel].Id == "LEAVE") tip = "Hold B / ESC to leave the room.";
        var prompts = new List<ButtonPrompts.Prompt> { ButtonPrompts.Confirm("SELECT"), new(Buttons.X, "R", readyNow ? "UNREADY" : "READY"), new(Buttons.Y, "T", "CHAT"), new(Buttons.B, "ESC", "HOLD TO LEAVE") };
        if (PlayersOverflow) prompts.Insert(1, new(Buttons.RightStick, "PGDN", "PLAYERS"));
        if (_overlay == Overlay.Settings) prompts = new() { ButtonPrompts.UpDown("MOVE"), ButtonPrompts.LeftRight("CHANGE"), ButtonPrompts.Confirm("SELECT"), ButtonPrompts.Back("CLOSE") };
        else if (_overlay == Overlay.Emote) prompts = new() { new(ButtonPrompts.DPadUpDown, "ARROWS", "SING"), ButtonPrompts.Confirm("HEY!"), ButtonPrompts.Back("CLOSE") };
        else if (_overlay == Overlay.ModPrompt) prompts = new();
        ProUI.HintBar(Input, prompts, _statusTimer > 0f ? _status : tip);
    }

    private void ProDrawToasts()
    {
        float y = 92f + 0f;
        if (_overlay != Overlay.None) return;
        foreach (var t in _toasts)
        {
            float a = Math.Min(1f, t.t / 0.4f) * Math.Min(1f, (3.5f - t.t) / 0.12f);
            ProUI.Toast(t.text, FunkUI.H - ProUI.BarH - 118f - (y - 92f), a, t.color);
            y += 40f;
        }
    }

    // ------------------------------------------------------------------ room settings (RoomSettingsSubstate)
    private sealed class SetRow { public string Label, Desc, Value; public bool? On; public bool Allowed = true; public Action Act; public Action<int> Lr; public bool Header; }
    private int _stagePick = -1, _speedPick = -1, _kickPick;

    private List<SetRow> SettingsRows()
    {
        var st = PsychOnline.State; bool perms = PsychOnline.HasPerms, owner = PsychOnline.IsHost;
        bool choose = perms || (st?.Bool("allPlayersChoose") ?? false);
        int wc = Math.Clamp((int)(st?.Num("winCondition") ?? 0), 0, 4);
        void Msg(string m) { PsychOnline.Send(m); }
        var l = new List<SetRow>
        {
            new() { Header = true, Label = "ROOM" },
            new() { Label = "Public Room", Desc = "If enabled, this room will be publicly listed in the FIND tab.", On = !(st?.Bool("isPrivate") ?? true), Allowed = perms, Act = () => Msg("togglePrivate") },
            new() { Label = "Only Network Players", Desc = "If enabled, only players registered to the network can join.", On = st?.Bool("networkOnly") ?? false, Allowed = perms, Act = () => Msg("toggleNetworkOnly") },
            new() { Label = "Anarchy Mode", Desc = "This option gives other players host permissions.", On = st?.Bool("anarchyMode") ?? false, Allowed = perms, Act = () => Msg("anarchyMode") },
            new() { Label = "Let Players Choose", Desc = "Gives other players permission to pick a song and stage.", On = st?.Bool("allPlayersChoose") ?? false, Allowed = perms, Act = () => Msg("togglePlayersCanChoose") },
            new() { Header = true, Label = "MATCH" },
            new() { Label = "Boyfriend Side", Desc = "Play on Boyfriend's side.", On = PsychOnline.SelfBfSide, Act = () => Msg("swapSides") },
            new() { Label = "Team Mode", Desc = "Compete in teams rather than individually! Your performance is averaged with your teammate.", On = st?.Bool("teamMode") ?? false, Allowed = perms, Act = () => Msg("teamMode") },
            new() { Label = "One Lane Mode", Desc = "Everybody plays on the same side!", On = st?.Bool("royalMode") ?? false, Allowed = perms, Act = () => Msg("royalMode") },
        };
        if (st?.Bool("royalMode") ?? false)
            l.Add(new() { Label = "One Lane: Opponent Side", Desc = "Everybody plays on the opponent side when enabled.", On = st?.Bool("royalModeDadSide") ?? false, Allowed = perms, Act = () => Msg("royalModeDadSide") });
        l.Add(new() { Label = "Win Condition", Desc = PsychOnline.WinConditionDesc[wc], Value = PsychOnline.WinConditions[wc], Allowed = perms, Act = () => Msg("nextWinCondition"), Lr = d => Msg("nextWinCondition") });
        if (_speedPick < 0) { float cur = PsychOnline.RoomSongSpeed; _speedPick = System.Array.FindIndex(PsychOnline.SongSpeeds, s => Math.Abs(s - cur) < 0.001f); if (_speedPick < 0) _speedPick = 5; }
        l.Add(new() { Label = "Song Speed", Desc = "Game modifier for everyone in the room (Psych 'songspeed' = playback rate).", Value = PsychOnline.RoomSongSpeed.ToString("0.00") + "x", Allowed = perms,
            Lr = d => { _speedPick = Math.Clamp(_speedPick + d, 0, PsychOnline.SongSpeeds.Length - 1); PsychOnline.SetGameplaySetting("songspeed", (double)PsychOnline.SongSpeeds[_speedPick]); } });
        if (_stagePick < 0) { string cur = st?.Str("stageName") ?? ""; _stagePick = Math.Max(0, System.Array.FindIndex(PsychOnline.Stages, s => s.pso == cur)); }
        bool stageDiff = (PsychOnline.Stages[_stagePick].pso ?? "") != (st?.Str("stageName") ?? "");
        l.Add(new() { Label = "Stage", Desc = "Currently selected: " + PsychOnline.RoomStageTitle() + (stageDiff ? ".  Press A to apply." : ""), Value = PsychOnline.Stages[_stagePick].title, Allowed = choose,
            Lr = d => _stagePick = (_stagePick + d + PsychOnline.Stages.Length) % PsychOnline.Stages.Length, Act = () => PsychOnline.SetStage(PsychOnline.Stages[_stagePick].pso) });
        l.Add(new() { Label = "Hide Girlfriend", Desc = "Hides GF from the stage.", On = st?.Bool("hideGF") ?? false, Allowed = perms, Act = () => Msg("toggleGF") });
        l.Add(new() { Label = "Disable Skins", Desc = "Forbids players from using skins.", On = st?.Bool("disableSkins") ?? false, Allowed = perms, Act = () => Msg("toggleSkins") });
        l.Add(new() { Header = true, Label = "PLAYERS" });
        var others = PsychOnline.AllPlayers().Where(p => p.sid != PsychOnline.SessionId).ToList();
        _kickPick = others.Count == 0 ? 0 : Math.Clamp(_kickPick, 0, others.Count - 1);
        l.Add(new() { Label = "Kick Player", Desc = others.Count == 0 ? "Nobody else is in the room." : "Removes the player from the room (room owner only, /kick <name>).", Value = others.Count == 0 ? "-" : others[_kickPick].p.Str("name"), Allowed = owner && others.Count > 0,
            Lr = d => { if (others.Count > 0) _kickPick = (_kickPick + d + others.Count) % others.Count; },
            Act = () => { if (others.Count > 0) { PsychOnline.Kick(others[_kickPick].p.Str("name")); Toast("Kicked " + others[_kickPick].p.Str("name"), ProUI.Bad); } } });
        l.Add(new() { Label = "Copy Room Code", Desc = PsychOnline.RoomSecret, Act = () => ActivateDock("CODE") });
        l.Add(new() { Label = "Close", Desc = "Back to the lobby.", Act = CloseSettings });
        return l;
    }

    private void UpdateSettings()
    {
        var rows = SettingsRows();
        int Next(int from, int dir) { int s = from; for (int k = 0; k < rows.Count; k++) { s = (s + dir + rows.Count) % rows.Count; if (!rows[s].Header) return s; } return from; }
        if (_setSel < 0 || _setSel >= rows.Count || rows[_setSel].Header) _setSel = Next(Math.Clamp(_setSel, 0, rows.Count - 1), 1);
        if (Input.UpPressed) { _setSel = Next(_setSel, -1); Audio.PlaySound("scrollMenu"); }
        if (Input.DownPressed) { _setSel = Next(_setSel, 1); Audio.PlaySound("scrollMenu"); }
        if (Input.BackPressed || Input.IsPressed(Keys.LeftShift)) { CloseSettings(); return; }
        var r = rows[_setSel];
        int lr = Input.LeftPressed ? -1 : Input.RightPressed ? 1 : 0;
        if (lr != 0 && r.Lr != null)
        {
            if (!r.Allowed) { SetStatus("You don't have a permission to do that."); Audio.PlaySound("cancelMenu"); return; }
            r.Lr(lr); Audio.PlaySound("scrollMenu"); return;
        }
        if (!Input.ConfirmPressed) return;
        if (!r.Allowed || r.Act == null) { SetStatus(r.Act == null ? "Use Left / Right to change this." : "You don't have a permission to do that."); Audio.PlaySound("cancelMenu"); return; }
        Audio.PlaySound("confirmMenu");
        r.Act();
    }

    private void CloseSettings() { _overlay = Overlay.None; _stagePick = -1; _speedPick = -1; Audio.PlaySound("cancelMenu"); PsychOnline.Send("status", "In the Lobby"); }

    private void ProDrawSettings()
    {
        ProUI.Dim(0.7f);
        var rows = SettingsRows();
        const float w = 760f, rowH = 32f;
        float y = 92f, h = FunkUI.H - ProUI.BarH - 16f - y;
        float x = FunkUI.W / 2f - w / 2f;
        int vis = (int)((h - 64f - 84f) / rowH);
        int first = Math.Clamp(_setSel - vis / 2, 0, Math.Max(0, rows.Count - vis));
        ProUI.PanelBox(x, y, w, h, 0.98f);
        FunkTheme.Tab("ROOM SETTINGS", x + 12f, y + 12f, FunkTheme.Yellow, _time, 36f);
        if (!PsychOnline.HasPerms) FunkTheme.Stamp("HOST ONLY OPTIONS LOCKED", x + w - 20f, y + 18f, FunkTheme.Orange, 1f);
        float ry = y + 64f;
        for (int i = first; i < Math.Min(rows.Count, first + vis); i++)
        {
            var r = rows[i];
            if (r.Header) { float hw = FunkTheme.Stamp(r.Label, x + 24f, ry + 6f, FunkTheme.Cyan); FunkTheme.Band(x + 24f + hw + 4f, ry + 15f, w - 72f - hw, 4f, 3f, FunkTheme.A(FunkTheme.Cyan, 0.5f)); ry += rowH; continue; }
            bool sel = i == _setSel;
            if (sel) FunkTheme.Panel(x + 16f, ry, w - 36f, rowH - 2f, FunkTheme.Yellow, 1f, 8f, 3f, 4f);
            var tc = !r.Allowed ? ProUI.Fg3 : sel ? FunkTheme.Ink : ProUI.Fg;
            ProUI.Text(r.Label, x + 32f, ry + 9f, 16f, tc);
            if (!r.Allowed && r.Label != "Kick Player") ProUI.Text("locked", x + 300f, ry + 10f, 13f, ProUI.Fg3);
            if (r.On.HasValue) ProUI.Switch(x + w - 32f - 64f, ry + 3f, r.On.Value, r.Allowed);
            else if (r.Value != null)
            {
                string v = r.Value; float vw = ProUI.Measure(v, 15f);
                float vx = x + w - 32f;
                var vc = sel ? FunkTheme.Ink : r.Allowed ? ProUI.Fg : ProUI.Fg3;
                if (r.Lr != null) { ProUI.Text(">", vx, ry + 9f, 15f, vc, 1f); vx -= 20f; }
                ProUI.TextFit(v, vx, ry + 9f, 15f, vc, 260f, 1f);
                if (r.Lr != null) ProUI.Text("<", vx - Math.Min(vw, 260f) - 10f, ry + 9f, 15f, vc, 1f);
            }
            ry += rowH;
        }
        if (rows.Count > vis)
        {
            float lt = y + 64f, lh = vis * rowH, frac = vis / (float)rows.Count;
            ProUI.Rect(x + w - 8f, lt, 3f, lh, ProUI.A(ProUI.Fg3, 0.35f));
            ProUI.Rect(x + w - 8f, lt + lh * first / rows.Count, 3f, lh * frac, ProUI.Fg2);
        }
        float dy = y + h - 76f;
        ProUI.Rect(x + 24f, dy, w - 48f, 1f, ProUI.Divider);
        if (_setSel >= 0 && _setSel < rows.Count) FunkUI.Paragraph(rows[_setSel].Desc ?? "", x + 24f, dy + 14f, w - 48f, 15f, ProUI.Fg2, 0f, 1.3f);
    }

    // ------------------------------------------------------------------ emote pad
    private void ProDrawEmote(SpriteBatch sb)
    {
        var dock = Dock(); int ei = Math.Max(0, dock.FindIndex(b => b.Id == "EMOTE"));
        var br = DockRect(ei, dock.Count);
        const float w = 300f, h = 184f;
        float x = Math.Clamp(br.Center.X - w / 2f, ProUI.M, FunkUI.W - ProUI.M - w), y = BarY - 24f - h;
        ProUI.PanelBox(x, y, w, h, 0.98f);
        ProUI.Caption("EMOTE", x + 16f, y + 13f);
        ProUI.Rect(x + 16f, y + 36f, w - 32f, 1f, ProUI.Divider);
        bool pad = ButtonPrompts.UseController(Input);
        _noteSheet ??= SpriteSheet.Load(Game, "game/skins/default/notes");
        float cx = x + 100f, cy = y + 108f;
        var items = new (string dir, int dx, int dy)[] { ("up", 0, -1), ("left", -1, 0), ("right", 1, 0), ("down", 0, 1) };
        foreach (var it in items)
        {
            float since = _emoteFlash.TryGetValue("sing" + it.dir.ToUpperInvariant(), out var ft) ? _roomTime - ft : 9f;
            float pop = since < 0.25f ? 1f + 0.3f * (1f - since / 0.25f) : 1f;
            if (_noteSheet != null) DrawFrameCentered(sb, _noteSheet, it.dir + " note", 0, cx + it.dx * 52f, cy + it.dy * 40f, 0.24f * pop, Color.White);
        }
        float hx = x + 228f;
        float since2 = _emoteFlash.TryGetValue("hey", out var ht) ? _roomTime - ht : 9f;
        ProUI.Button(hx - 44f, cy - 22f, 88f, 44f, "HEY!", since2 < 0.25f);
        FunkUI.Glyph(pad, Buttons.A, "ENTER", hx - 12f, cy + 30f, 24f);
    }

    // ------------------------------------------------------------------ "you need a mod" dialog
    private void ProDrawModPrompt()
    {
        var st = PsychOnline.State;
        var job = PsychOnline.ModJob;
        bool running = PsychOnline.ModTask != null && !PsychOnline.ModTask.IsCompleted;
        bool done = !running && job != null && job.Done && job.Error == null && job.ModUrl == PsychOnline.ModJobUrl && !PsychOnline.RoomNeedsMod;
        ProUI.Dim(0.72f);
        const float w = 640f, h = 300f;
        float x = FunkUI.W / 2f - w / 2f, y = 150f;
        ProUI.PanelBox(x, y, w, h, 0.98f);
        ProUI.Rect(x, y, w, 4f, running ? ProUI.Accent : done ? ProUI.Good : _modFail != null ? ProUI.Bad : ProUI.Opp);
        ProUI.Title(running ? "DOWNLOADING MOD" : done ? "MOD INSTALLED" : _modFail != null ? "DOWNLOAD FAILED" : "MOD REQUIRED", x + 28f, y + 24f, 0.4f, _time);
        string song = st?.Str("song") ?? "?", modDir = st?.Str("modDir") ?? "?", url = PsychOnline.RoomModUrl ?? "";
        float ty = y + 84f;
        ProUI.Caption("SONG", x + 28f, ty); ProUI.TextFit(song, x + 140f, ty - 2f, 16f, ProUI.Fg, w - 170f); ty += 28f;
        ProUI.Caption("MOD", x + 28f, ty); ProUI.TextFit(PsychOnline.PackName(modDir) ?? modDir, x + 140f, ty - 2f, 16f, ProUI.Fg, w - 170f); ty += 28f;
        ProUI.Caption("SOURCE", x + 28f, ty); ProUI.TextFit(string.IsNullOrEmpty(url) ? "(the host gave no download link)" : url.Replace("https://", "").Replace("http://", ""), x + 140f, ty - 2f, 14f, ProUI.Fg2, w - 170f); ty += 40f;
        if (running && job != null)
        {
            float p = job.Progress;
            if (p >= 0f) ProUI.Progress(x + 28f, ty, w - 56f, p, 8f);
            else { float s = (_modPromptT * 0.7f) % 1f; ProUI.Rect(x + 28f, ty, w - 56f, 8f, ProUI.PanelHi); ProUI.Rect(x + 28f + (w - 56f - 120f) * s, ty, 120f, 8f, ProUI.Accent); }
            ProUI.TextFit((p >= 0f ? (int)(p * 100f) + "%   " : "") + (job.Status ?? ""), x + 28f, ty + 18f, 14f, ProUI.Fg2, w - 56f);
        }
        else if (done) ProUI.Text("Checking the chart - you will be set READY automatically.", x + 28f, ty, 15f, ProUI.Good);
        else if (_modFail != null) FunkUI.Paragraph(_modFail, x + 28f, ty, w - 56f, 14f, ProUI.Bad, 0f, 1.3f);
        else ProUI.Text("Download, install and verify the chart in one step.", x + 28f, ty, 15f, ProUI.Fg2);
        // dialog buttons (same style everywhere)
        bool pad = ButtonPrompts.UseController(Input);
        var btns = new List<(Buttons b, string key, string label, bool primary)>();
        if (running) btns.Add((Buttons.B, "ESC", "CANCEL", false));
        else if (!done)
        {
            bool gb = GameBanana.ModIdFromUrl(job?.BrowserUrl ?? url) > 0;
            btns.Add((Buttons.A, "ENTER", _modFail != null ? "TRY AGAIN" : "DOWNLOAD", true));
            btns.Add((Buttons.Y, "Y", gb ? "MOD PAGE" : "OPEN LINK", false));
            btns.Add((Buttons.B, "ESC", "NOT NOW", false));
        }
        float bx = x + w - 28f, by = y + h - 64f;
        for (int i = btns.Count - 1; i >= 0; i--)
        {
            var b = btns[i];
            float bw = ProUI.Measure(b.label, 16f) + 30f + FunkUI.MeasureGlyph(pad, b.key, 22f) + 8f;
            bx -= bw;
            ProUI.Button(bx, by, bw, 40f, "", b.primary);
            float gx = bx + 15f;
            FunkUI.Glyph(pad, b.b, b.key, gx, by + 9f, 22f);
            ProUI.Text(b.label, gx + FunkUI.MeasureGlyph(pad, b.key, 22f) + 8f, by + 12f, 16f, b.primary ? ProUI.AccentInk : ProUI.Fg);
            bx -= 12f;
        }
    }

    // ------------------------------------------------------------------ dev: scripted room actions + state log
    private float _devMsgT; private int _devMsgDone; private string _devStateLog;
    /// <summary>FNF_PSO_DEVMSG="sec:action;..." (seconds after a 2nd player is in the room). action = a bare room message
    /// (teamMode, royalMode, toggleNetworkOnly, nextWinCondition, ...), songspeed=1.2, stage=limo, or a chat line / "/command".
    /// Every change of the room settings is logged as [PSO] room: ... (verification against the real client).</summary>
    private void ProLobbyDev(float dt)
    {
        var st = PsychOnline.State;
        if (st != null)
        {
            var gs = st.Map("gameplaySettings");
            string cur = $"private={st.Bool("isPrivate")} networkOnly={st.Bool("networkOnly")} anarchy={st.Bool("anarchyMode")} team={st.Bool("teamMode")} royal={st.Bool("royalMode")}/{st.Bool("royalModeDadSide")} hideGF={st.Bool("hideGF")} win={st.Num("winCondition")} stage='{st.Str("stageName")}' settings={(gs == null ? "" : string.Join(",", gs.Items.Select(kv => kv.Key + "=" + kv.Value)))} host={PsychOnline.NameOf(st.Str("host"))} players={string.Join(",", PsychOnline.AllPlayers().Select(p => p.p.Str("name") + (p.p.Bool("bfSide") ? "(bf)" : "(dad)")))}";
            if (cur != _devStateLog) { _devStateLog = cur; Console.WriteLine("[PSO] room: " + cur); }
        }
        string spec = Environment.GetEnvironmentVariable("FNF_PSO_DEVMSG");
        if (string.IsNullOrEmpty(spec)) return;
        if (PsychOnline.AllPlayers().Count() >= 2) _devMsgT += dt;
        var items = spec.Split(';');
        while (_devMsgDone < items.Length)
        {
            var it = items[_devMsgDone]; int c = it.IndexOf(':');
            if (c < 0 || !float.TryParse(it.Substring(0, c), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var at) || _devMsgT < at) break;
            _devMsgDone++;
            string a = it.Substring(c + 1);
            Console.WriteLine("[PSO] dev action: " + a);
            if (a.StartsWith("songspeed=")) PsychOnline.SetGameplaySetting("songspeed", double.Parse(a.Substring(10), System.Globalization.CultureInfo.InvariantCulture));
            else if (a.StartsWith("stage=")) PsychOnline.SetStage(a.Substring(6));
            else if (a.StartsWith("/") || a.Contains(" ")) PsychOnline.SendChat(a);
            else if (a == "settings") { _overlay = Overlay.Settings; _setSel = 0; }
            else if (a == "close") _overlay = Overlay.None;
            else PsychOnline.Send(a);
        }
    }

    // ================================================================== pages (front, lists, songs, code, text, connecting, leaderboard)
    private float _proListBottom = 640f;
    private int _thListSel = -1; private float _thListT;

    private void ConfigureProList()
    {
        _fm.CenterX = ProUI.M + 340f; _fm.RowW = 680f; _fm.RowH = 46f; _fm.RowGap = 4f; _fm.ListTop = 112f; _fm.ListBottom = FunkUI.H - ProUI.BarH - 24f;
    }

    /// <summary>Generic list: rows at FunkMenu's (scrolling) positions so mouse hit-testing stays the same.</summary>
    private void ProDrawList(List<FunkMenu.Row> rows, int sel)
    {
        float x0 = _fm.CenterX - _fm.RowW / 2f, w0 = _fm.RowW;
        _proListBottom = Math.Min(_fm.ListBottom, _fm.ListTop + Math.Max(4, rows.Count) * _fm.Spacing);
        if (sel != _thListSel) { _thListSel = sel; _thListT = 0f; }
        _thListT += 1f / 60f;
        float pop = MathF.Exp(-_thListT * 9f);
        var songs = _page == Page.Songs ? PsychOnline.Songs() : null;
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            float cy = _fm.RowCenterY(i), y = cy - _fm.RowH / 2f + 3f, rh = _fm.RowH - 6f;
            if (y < _fm.ListTop - 2f || y + rh > _fm.ListBottom + 2f) continue;
            bool focus = i == sel;
            // Freeplay-style capsules: the focused one slides out and pops, the others step back with distance
            float d = Math.Min(4, Math.Abs(i - sel));
            float rx = x0 + 12f + d * 6f + (focus ? -12f - 10f * pop : 0f), rw = w0 - 24f - d * 6f;
            float s = focus ? 1f + 0.03f * pop : 1f;
            if (s != 1f) FunkUI.Push(rx, cy, 0f, s, s);
            FunkTheme.Panel(rx, y, rw, rh, focus ? FunkTheme.Yellow : r.Disabled ? FunkTheme.NavyLo : FunkTheme.Navy, 1f, 10f, 3f, focus ? 6f : 4f);
            float lx = rx + 20f;
            if (songs != null && i < songs.Count)
            {   // health icon of the song's opponent at the capsule's left edge (Freeplay)
                var ic = IconTex(PsychOnline.CardFor(songs[i].Folder, songs[i].FileFor(songs[i].Diffs.FirstOrDefault() ?? "Normal"), songs[i].ModDir).Opponent);
                if (ic != null) FunkUI.Quad(ic, rx + 4f, cy, rh * 1.25f, rh * 1.25f, 0f, Color.White, new Rectangle(0, 0, ic.Width / 2, ic.Height));
                lx += 26f;
            }
            var lc = r.Disabled ? ProUI.Fg3 : focus ? FunkTheme.Ink : ProUI.Fg;
            float right = rx + rw - 16f;
            if (r.Kind == FunkMenu.Kind.Toggle) { ProUI.Switch(right - 64f, cy - 12f, r.On); right -= 78f; }
            else if (!string.IsNullOrEmpty(r.Value))
            {
                bool arrows = r.Kind == FunkMenu.Kind.Choice;
                var vc = focus ? FunkTheme.Ink : ProUI.Fg2;
                if (arrows) { ProUI.Text(">", right, cy - 8f, 16f, vc, 1f); right -= 22f; }
                float vw = Math.Min(ProUI.Measure(r.Value, 16f), rw * 0.45f);
                ProUI.TextFit(r.Value, right, cy - 8f, 16f, vc, rw * 0.45f, 1f);
                right -= vw + 10f;
                if (arrows) { ProUI.Text("<", right, cy - 8f, 16f, vc, 1f); right -= 22f; }
            }
            if (!string.IsNullOrEmpty(r.Badge)) right -= ProUI.Chip(r.Badge, right, cy - 12f, r.BadgeColor ?? ProUI.Fg2, 1f) + 10f;
            ProUI.TextFit(r.Label, lx, cy - 10f, 20f, lc, right - lx - 12f);
            if (s != 1f) FunkUI.Pop();
        }
        float x = x0, w = w0;
        float view = _fm.ListBottom - _fm.ListTop, content = rows.Count * _fm.Spacing;
        if (content > view + 1f)
        {   // scrollbar
            float sx = x + w + 2f, frac = view / content;
            float first = _fm.ListTop - (_fm.RowCenterY(0) - _fm.RowH / 2f);
            ProUI.Rect(sx, _fm.ListTop, 3f, view, ProUI.A(ProUI.Fg3, 0.35f));
            ProUI.Rect(sx, _fm.ListTop + view * Math.Clamp(first / content, 0f, 1f - frac), 3f, view * frac, ProUI.Fg2);
        }
    }

    /// <summary>Right info pane for list pages.</summary>
    private void ProSidePanel(string caption, string title, string body, IList<(string k, string v)> facts = null)
    {
        float x = ProUI.M + 680f + ProUI.G + 16f, w = FunkUI.W - ProUI.M - x, y = _fm.ListTop - 8f, h = _proListBottom - _fm.ListTop + 16f;
        float top = ProUI.Card(x, y, w, h, caption);
        if (!string.IsNullOrEmpty(title)) { ProUI.TextFit(title, x + 16f, top + 4f, 22f, ProUI.Fg, w - 32f); top += 40f; }
        if (!string.IsNullOrEmpty(body)) top += FunkUI.Paragraph(body, x + 16f, top, w - 32f, 15f, ProUI.Fg2, 0f, 1.35f) + 16f;
        if (facts != null)
            foreach (var (k, v) in facts)
            {
                ProUI.Caption(k, x + 16f, top + 3f); ProUI.TextFit(v ?? "-", x + w - 16f, top, 15f, ProUI.Fg, w - 150f, 1f);
                top += 30f; ProUI.Rect(x + 16f, top - 8f, w - 32f, 1f, ProUI.Divider);
            }
    }

    private void ProDrawPages(SpriteBatch sb, List<FunkMenu.Row> vrows)
    {
        bool over = OverLobby;
        if (over) ProUI.Dim(0.84f); else ProUI.Backdrop(_desat);
        string title = _page switch
        {
            Page.Songs => "SELECT SONG", Page.Rooms => "FIND ROOMS", Page.Chat => "CHAT", Page.Account => "ACCOUNT", Page.Leaderboard => "LEADERBOARD",
            Page.Code => "JOIN ROOM", Page.Connecting => "CONNECTING",
            Page.Text => _textField switch { 0 => "SERVER", 1 => "NICKNAME", 2 => "E-MAIL", 3 => "LOGIN CODE", _ => "MESSAGE" },
            _ => "PSYCH ONLINE",
        };
        string kicker = over ? "ROOM " + (PsychOnline.Room?.RoomId ?? "") : "MULTIPLAYER";
        string right = over ? (PsychOnline.AllPlayers().Count() + " PLAYERS") : ServerShort(Server);
        ProUI.Header(title, kicker, right, over ? ProUI.Fg2 : PsychOnline.OfficialServers.Contains(Server) ? ProUI.Fg2 : ProUI.Warn, _time);
        string hintLeft = _statusTimer > 0f ? _status : null;
        switch (_page)
        {
            case Page.Main:
            {
                ProDrawList(vrows, _sel);
                var r = _sel >= 0 && _sel < vrows.Count ? vrows[_sel] : null;
                PsychOnline.LoadAuth();
                ProSidePanel("ABOUT", r?.Label, r?.Desc, new List<(string, string)>
                {
                    ("Server", ServerShort(Server)), ("Protocol", PsychOnline.Protocol.ToString()), ("Nickname", PsychOnline.DefaultNickname()),
                    ("Account", PsychOnline.LoggedIn ? PsychOnline.AuthName ?? "logged in" : "not logged in"), ("Funkin Points", ((long)PsychOnline.FunkinPoints).ToString("#,##0") + " FP"),
                });
                break;
            }
            case Page.Rooms:
            {
                ProDrawList(vrows, _sel);
                var r = _sel >= 0 && _sel < vrows.Count ? vrows[_sel] : null;
                string body = _rooms.Count == 0 ? (_roomsTask != null ? "Looking for rooms..." : "No public rooms right now. Create one, or join with a code.") : r?.Desc;
                ProSidePanel("ROOM", _rooms.Count > 0 && _sel < _rooms.Count ? r?.Label : null, body,
                    _sel < _rooms.Count ? new List<(string, string)> { ("Code", (string)_rooms[_sel]["roomId"]), ("Players", r?.Value), ("Ping", r?.Badge) } : null);
                break;
            }
            case Page.Chat:
            case Page.Account:
            {
                ProDrawList(vrows, _sel);
                var r = _sel >= 0 && _sel < vrows.Count ? vrows[_sel] : null;
                if (_page == Page.Chat)
                {
                    var log = PsychOnline.Log; var last = log.Skip(Math.Max(0, log.Count - 8)).ToList();
                    ProSidePanel("RECENT", null, last.Count == 0 ? "No messages yet." : string.Join("  /  ", last), new List<(string, string)> { ("Commands", "/help /roll /kick") });
                }
                else ProSidePanel("FUNKIN NETWORK", r?.Label, r?.Desc, new List<(string, string)> { ("E-mail", string.IsNullOrEmpty(_email) ? "-" : _email), ("Status", PsychOnline.LoggedIn ? "logged in as " + PsychOnline.AuthName : "not logged in") });
                break;
            }
            case Page.Songs: ProDrawSongs(vrows); break;
            case Page.Leaderboard: ProDrawLeaderboard(); break;
            case Page.Code: ProDrawCode(); break;
            case Page.Text: ProDrawText(); break;
            case Page.Connecting: ProDrawConnecting(); break;
        }
        if (_page is Page.Main or Page.Rooms or Page.Chat or Page.Account && hintLeft == null && _sel >= 0 && _sel < vrows.Count) hintLeft = null;
        ProUI.HintBar(Input, _page == Page.Leaderboard ? LeaderboardPrompts() : Prompts(), hintLeft);
    }

    // ------------------------------------------------------------------ song select: list + detail pane
    private void ProDrawSongs(List<FunkMenu.Row> vrows)
    {
        var songs = PsychOnline.Songs();
        ProDrawList(vrows, _sel);
        float x = ProUI.M + 680f + ProUI.G + 16f, w = FunkUI.W - ProUI.M - x, y = _fm.ListTop - 8f, h = _fm.ListBottom - _fm.ListTop + 16f;
        ProUI.PanelBox(x, y, w, h, 0.95f);
        if (_sel >= songs.Count)
        {
            float t = ProUI.Card(x, y, w, h, _sel == songs.Count ? "MOD BROWSER" : "BACK");
            FunkUI.Paragraph(_sel == songs.Count ? "Browse GameBanana and install any mod without leaving the room. Its songs appear in this list." : "Back to the room.", x + 16f, t + 4f, w - 32f, 15f, ProUI.Fg2, 0f, 1.35f);
            return;
        }
        var e = songs[Math.Clamp(_sel, 0, songs.Count - 1)];
        int di = Math.Clamp(_diffSel, 0, e.Diffs.Count - 1);
        string diff = e.Diffs[di];
        var card = PsychOnline.CardFor(e.Folder, e.FileFor(diff), e.ModDir);
        float art = w - 32f > 200f ? 200f : w - 32f;
        var album = !string.IsNullOrEmpty(card.Album) ? Tex("menus/freeplay/albumRoll/" + card.Album + ".png") : null;
        float ay = y + 20f;
        FunkTheme.Box(x + 12f, ay - 4f, art + 8f, art + 8f, FunkTheme.Ink);
        if (album != null) FunkUI.Quad(album, x + 16f + art / 2f, ay + art / 2f, art, art, 0f, Color.White);
        else
        {
            ProUI.Rect(x + 16f, ay, art, art, ProUI.PanelHi);
            var io = IconTex(card.Opponent ?? "face");
            if (io != null) FunkUI.Quad(io, x + 16f + art / 2f, ay + art / 2f, 150f, 150f, 0f, Color.White, new Rectangle(0, 0, io.Width / 2, io.Height));
        }
        float tx = x + 16f + art + 20f, tw = x + w - 16f - tx;
        if (tw < 120f) { tx = x + 16f; tw = w - 32f; ay += art + 16f; }
        FunkTheme.Stamp(string.IsNullOrEmpty(e.ModDir) ? "BASE GAME" : "MOD", tx, ay, string.IsNullOrEmpty(e.ModDir) ? FunkTheme.Cyan : FunkTheme.Purple);
        float ty = ay + 34f;
        var titleLines = FunkUI.Wrap(card.Title ?? e.Display, tw, 24f).Take(2).ToList();
        foreach (var l in titleLines) { ProUI.Text(l, tx, ty, 24f, ProUI.Fg); ty += 30f; }
        if (!string.IsNullOrEmpty(card.Artist)) { ProUI.TextFit(card.Artist, tx, ty, 14f, ProUI.Fg2, tw); ty += 24f; }
        if (!string.IsNullOrEmpty(e.ModDir)) { ProUI.TextFit(PsychOnline.PackName(e.ModDir) ?? e.ModDir, tx, ty, 14f, ProUI.Fg2, tw); ty += 24f; }
        // health icons opponent vs player
        var io2 = IconTex(card.Opponent); var ip = IconTex(card.Player);
        if (io2 != null) FunkUI.Quad(io2, tx + 30f, ty + 34f, 60f, 60f, 0f, Color.White, new Rectangle(0, 0, io2.Width / 2, io2.Height));
        ProUI.Text("VS", tx + 76f, ty + 26f, 15f, ProUI.Fg3, 0.5f);
        if (ip != null) FunkUI.Quad(ip, tx + 122f, ty + 34f, 60f, 60f, 0f, Color.White, new Rectangle(0, 0, ip.Width / 2, ip.Height), SpriteEffects.FlipHorizontally);
        // difficulty selector
        float dy = Math.Max(ty + 80f, y + 20f + art + 24f);
        // the selected difficulty as Freeplay's own difficulty sprite, the list as stamps
        bool hasArt = FunkTheme.Difficulty(diff, x + w / 2f, dy + 30f, 56f) > 0f;
        if (!hasArt) FunkTheme.Heading(diff.ToUpperInvariant(), x + w / 2f, dy + 6f, 0.6f, _time, 0.5f);
        if (e.Diffs.Count > 1) { ProUI.Text("<", x + 24f, dy + 22f, 20f, FunkTheme.Yellow); ProUI.Text(">", x + w - 24f, dy + 22f, 20f, FunkTheme.Yellow, 1f); }
        dy += 72f;
        float dx = x + 16f;
        for (int k = 0; k < e.Diffs.Count; k++)
        {
            string d = e.Diffs[k].ToUpperInvariant();
            float bw = FunkTheme.TextW(d, 14f) + 34f;
            if (dx + bw > x + w - 16f) { dx = x + 16f; dy += 34f; }
            bool on = k == di;
            var dc = DiffColors.TryGetValue(e.Diffs[k], out var c) ? c : ProUI.Fg;
            dx += FunkTheme.Stamp(d, dx, dy, on ? dc : FunkTheme.NavyHi, 0f, 14f, 1f, on);
        }
        dy += 48f;
        string cur = PsychOnline.State?.Str("song");
        bool isCur = !string.IsNullOrEmpty(cur) && PsychOnline.State?.Str("folder") == e.Folder;
        if (isCur) ProUI.Chip("CURRENT ROOM SONG", x + 16f, dy, ProUI.Good);
        if (dy + 40f < y + h) ProUI.TextFit("Every player needs this chart to play.", x + 16f, y + h - 34f, 14f, ProUI.Fg3, w - 32f);
    }

    // ------------------------------------------------------------------ code entry / text entry / connecting
    private void ProDrawCode()
    {
        var c = _code.PadRight(4, 'A');
        const float tw = 120f, th = 150f, gap = 20f;
        float x0 = (FunkUI.W - (4 * tw + 3 * gap)) / 2f, ty = 220f;
        ProUI.Text("Enter the 4-letter room code", FunkUI.W / 2f, 160f, 18f, ProUI.Fg2, 0.5f);
        for (int i = 0; i < 4; i++)
        {
            bool sel = i == _codePos;
            float x = x0 + i * (tw + gap);
            ProUI.PanelBox(x, ty, tw, th, 0.96f, sel ? ProUI.PanelHi : ProUI.Panel);
            if (sel) ProUI.Focus(x, ty, tw, th);
            ProUI.Title(c[i].ToString(), x + tw / 2f, ty + th / 2f - FunkUI.BoldHeight(1.1f) / 2f, 1.1f, _time, 0.5f);
            if (sel)
            {   // flat chevrons above / below the focused letter
                float mx = x + tw / 2f;
                FunkUI.Line(mx - 10f, ty - 12f, mx, ty - 22f, 3f, ProUI.Accent); FunkUI.Line(mx, ty - 22f, mx + 10f, ty - 12f, 3f, ProUI.Accent);
                FunkUI.Line(mx - 10f, ty + th + 12f, mx, ty + th + 22f, 3f, ProUI.Accent); FunkUI.Line(mx, ty + th + 22f, mx + 10f, ty + th + 12f, 3f, ProUI.Accent);
            }
        }
        ProUI.Text("Server: " + Server, FunkUI.W / 2f, ty + th + 60f, 15f, ProUI.Fg3, 0.5f);
        ProUI.Text("Up / Down changes the letter, Left / Right moves, or just type the code.", FunkUI.W / 2f, ty + th + 88f, 15f, ProUI.Fg3, 0.5f);
    }

    private void ProDrawText()
    {
        string q = _textField switch { 0 => "Server address", 1 => "Your nickname", 2 => "Your e-mail", 3 => "Code from the e-mail", _ => "Message" };
        string hint = _textField switch
        {
            0 => "e.g. 192.168.1.20:2567 (self-hosted Funkin-Online-Server) or wss://funkin.sniro.boo", 1 => "3 to 14 characters.",
            2 => "The e-mail of your Psych Online (Funkin Network) account.", 3 => "The one-time code the server e-mailed you.",
            _ => "Chat message (max 300 characters). Commands: /help /roll /kick <name> /report <reason>",
        };
        const float w = 820f, h = 260f;
        float x = FunkUI.W / 2f - w / 2f, y = 170f;
        ProUI.PanelBox(x, y, w, h, 0.97f);
        ProUI.Caption(q, x + 28f, y + 24f, ProUI.Accent);
        ProUI.Rect(x + 28f, y + 52f, w - 56f - 70f, 56f, ProUI.Bg);
        ProUI.Outline(x + 28f, y + 52f, w - 56f - 70f, 56f, ProUI.A(ProUI.Accent, 0.7f));
        string buf = _textBuf ?? "";
        float maxW = w - 56f - 70f - 32f;
        string shown = buf;
        while (shown.Length > 0 && ProUI.Measure(shown, 26f) > maxW) shown = shown.Substring(1);
        float tw = ProUI.Text(shown, x + 44f, y + 66f, 26f, ProUI.Fg);
        if (((int)(_time * 2f)) % 2 == 0) ProUI.Rect(x + 46f + tw, y + 64f, 2f, 32f, ProUI.Accent);
        float cx = x + w - 28f - 56f;
        ProUI.Rect(cx, y + 52f, 56f, 56f, ProUI.PanelHi);
        ProUI.Text(TextChars[_textCharIdx].ToString(), cx + 28f, y + 66f, 26f, ProUI.Accent, 0.5f);
        FunkUI.Paragraph(hint, x + 28f, y + 128f, w - 56f, 15f, ProUI.Fg2, 0f, 1.35f);
        FunkUI.Paragraph("Keyboard: just type.  Controller: Up / Down picks a character (box on the right), Right adds it, X deletes, A confirms.", x + 28f, y + h - 54f, w - 56f, 14f, ProUI.Fg3, 0f, 1.3f);
    }

    private void ProDrawConnecting()
    {
        float cx = FunkUI.W / 2f, y = 300f;
        ProUI.Text("Connecting to " + ServerShort(Server) + (string.IsNullOrEmpty(_code) ? "" : "  -  room " + _code), cx, y, 18f, ProUI.Fg, 0.5f);
        float w = 360f, s = (_time * 0.8f) % 1f;
        ProUI.Rect(cx - w / 2f, y + 40f, w, 4f, ProUI.PanelHi);
        ProUI.Rect(cx - w / 2f + (w - 90f) * s, y + 40f, 90f, 4f, ProUI.Accent);
    }

    // ------------------------------------------------------------------ network leaderboard (TopPlayerSubstate) + profile
    private JArray _top; private Task<JArray> _topTask; private int _topPage, _topSel;
    private JObject _profile; private Task<JObject> _profileTask; private string _profileName;

    private void OpenLeaderboard() { _page = Page.Leaderboard; _topSel = 0; _topPage = 0; _top = null; _profile = null; _profileName = null; _topTask = PsychOnline.TopPlayersAsync(0); }

    private List<ButtonPrompts.Prompt> LeaderboardPrompts() => new()
    { ButtonPrompts.UpDown("MOVE"), ButtonPrompts.LeftRight("PAGE"), ButtonPrompts.Confirm("PROFILE"), ButtonPrompts.Back("BACK") };

    private void UpdateLeaderboard()
    {
        if (_topTask != null && _topTask.IsCompleted) { _top = _topTask.Status == TaskStatus.RanToCompletion ? _topTask.Result : null; _topTask = null; if (_top == null) SetStatus("Couldn't load the leaderboard (network offline?)"); }
        if (_profileTask != null && _profileTask.IsCompleted) { _profile = _profileTask.Status == TaskStatus.RanToCompletion ? _profileTask.Result : null; _profileTask = null; }
        int n = _top?.Count ?? 0;
        if (Input.BackPressed) { Audio.PlaySound("cancelMenu"); _page = Page.Main; _sel = 6; return; }
        if (n > 0 && Input.UpPressed) { _topSel = (_topSel + n - 1) % n; Audio.PlaySound("scrollMenu"); }
        if (n > 0 && Input.DownPressed) { _topSel = (_topSel + 1) % n; Audio.PlaySound("scrollMenu"); }
        int lr = Input.LeftPressed ? -1 : Input.RightPressed ? 1 : 0;
        if (lr != 0 && _topTask == null && _topPage + lr >= 0) { _topPage += lr; _topSel = 0; _topTask = PsychOnline.TopPlayersAsync(_topPage); Audio.PlaySound("scrollMenu"); }
        if (Input.ConfirmPressed && n > 0 && _profileTask == null)
        {
            _profileName = (string)_top[Math.Clamp(_topSel, 0, n - 1)]["player"];
            _profile = null; _profileTask = PsychOnline.UserDetailsAsync(_profileName); Audio.PlaySound("confirmMenu");
        }
    }

    private void ProDrawLeaderboard()
    {
        float x = ProUI.M, w = 680f, y = 104f, h = FunkUI.H - ProUI.BarH - 24f - y;
        float top = ProUI.Card(x, y, w, h, "TOP PLAYERS  -  ALL TIME  -  4K", "PAGE " + (_topPage + 1), ProUI.Fg2);
        if (_topTask != null) ProUI.Text("Loading...", x + 16f, top + 6f, 16f, ProUI.Fg2);
        else if (_top == null || _top.Count == 0) ProUI.Text("Nothing here.", x + 16f, top + 6f, 16f, ProUI.Fg3);
        else
        {
            const float rh = 30f;
            int vis = (int)((y + h - top - 8f) / rh);
            int first = Math.Clamp(_topSel - vis / 2, 0, Math.Max(0, _top.Count - vis));
            for (int i = first; i < Math.Min(_top.Count, first + vis); i++)
            {
                var e = _top[i];
                float ry = top + (i - first) * rh;
                bool sel = i == _topSel;
                if (sel) { ProUI.Rect(x + 8f, ry - 3f, w - 16f, rh - 2f, ProUI.PanelHi); ProUI.Rect(x + 8f, ry - 3f, 3f, rh - 2f, ProUI.Accent); }
                int rank = _topPage * 15 + i + 1;
                ProUI.Text(rank.ToString(), x + 50f, ry + 3f, 15f, rank <= 3 ? ProUI.Accent : ProUI.Fg3, 1f);
                float hue = (float?)e["profileHue"] ?? 250f;
                ProUI.Rect(x + 62f, ry + 5f, 12f, 12f, FromHsl(hue, 0.55f, 0.55f));
                ProUI.TextFit((string)e["player"] ?? "?", x + 84f, ry + 3f, 16f, sel ? ProUI.Fg : ProUI.Fg2, 260f);
                string club = (string)e["club"]; string ctry = (string)e["country"];
                ProUI.Text((club != null ? "[" + club + "] " : "") + (ctry ?? ""), x + 380f, ry + 4f, 13f, ProUI.Fg3);
                ProUI.Text(((long?)e["points4k"] ?? 0).ToString("#,##0") + " FP", x + w - 16f, ry + 3f, 15f, ProUI.Fg, 1f);
            }
        }
        float px = x + w + ProUI.G + 16f, pw = FunkUI.W - ProUI.M - px;
        float pt = ProUI.Card(px, y, pw, h, "PROFILE");
        if (_profileName == null) { FunkUI.Paragraph("Select a player and press A to see their profile.", px + 16f, pt + 4f, pw - 32f, 15f, ProUI.Fg2, 0f, 1.35f); return; }
        ProUI.TextFit(_profileName, px + 16f, pt + 4f, 22f, ProUI.Fg, pw - 32f);
        if (_profileTask != null) { ProUI.Text("Loading...", px + 16f, pt + 44f, 15f, ProUI.Fg2); return; }
        if (_profile == null) { ProUI.Text("Profile unavailable.", px + 16f, pt + 44f, 15f, ProUI.Fg3); return; }
        float fy = pt + 50f;
        void Fact(string k, string v) { ProUI.Caption(k, px + 16f, fy + 3f); ProUI.TextFit(v ?? "-", px + pw - 16f, fy, 15f, ProUI.Fg, pw - 150f, 1f); fy += 30f; ProUI.Rect(px + 16f, fy - 8f, pw - 32f, 1f, ProUI.Divider); }
        Fact("Rank", "#" + (int?)_profile["rank"]);
        Fact("Points", ((long?)_profile["points"] ?? 0).ToString("#,##0") + " FP");
        Fact("Avg. accuracy", (((double?)_profile["avgAccuracy"] ?? 0) * 100).ToString("0.00") + "%");
        Fact("Role", (string)_profile["role"]);
        Fact("Country", (string)_profile["country"]);
        Fact("Club", (string)_profile["club"]);
        string joined = (string)_profile["joined"];
        Fact("Joined", joined != null && joined.Length >= 10 ? joined.Substring(0, 10) : joined);
    }
}
