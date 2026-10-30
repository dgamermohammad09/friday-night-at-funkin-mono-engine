using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.Colyseus;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Psych Online room HUD in the in-house Funkin theme (FunkTheme): Freeplay-style black top bar with the room code in the
/// bold alphabet, capsule nameplates above every character (side band, health icon, ping, READY stamp, beat bop), a
/// "NOW PLAYING" panel with the real Freeplay capsule + album + difficulty sprite, players list, speech-bubble chat and a
/// bottom row of icon capsules with controller glyph chips. Same actions / controller behaviour as before.
/// </summary>
public partial class PsychOnlineScene
{
    private const float ColX = 872f, ColW = 368f, BarY = 600f, BarBtnH = 52f;
    private int _thFocusPrev = -1; private float _thFocusT = 9f; private float _thLastTime;

    private Rectangle DockRect(int i, int n)
    {
        var d = Dock();
        float total = 0f;
        for (int k = 0; k < d.Count; k++) total += DockW(d[k]) + (k > 0 ? 14f : 0f);
        float x = Math.Max(ProUI.M - 8f, (ColX - 24f - total) > ProUI.M ? ProUI.M - 8f : (FunkUI.W - total) / 2f);
        for (int k = 0; k < i && k < d.Count; k++) x += DockW(d[k]) + 14f;
        return new Rectangle((int)x, (int)BarY + 6, (int)DockW(d[Math.Min(i, d.Count - 1)]), (int)BarBtnH);
    }

    private float DockW(DockBtn b) => Math.Max(104f, FunkTheme.TextW(b.Label, 18f) + 30f + 34f + 8f);

    private static string DockIcon(DockBtn b) => b.Id switch
    {
        "READY" => "play", "SONG" => "song", "CHAT" => "chat", "EMOTE" => "emote", "CODE" => "code", "SETTINGS" => "settings",
        "LEAVE" => "leave", "SKINS" => "code", "MODS" => "@globe", "MOD" => "@d_arrow", _ => null,
    };

    private void ProDrawLobbyHud(SpriteBatch sb)
    {
        float dt = Math.Clamp(_time - _thLastTime, 0f, 0.1f); _thLastTime = _time;
        if (_dockSel != _thFocusPrev) { _thFocusPrev = _dockSel; _thFocusT = 0f; } else _thFocusT += dt;
        ProDrawProfileCards(true);
        ProDrawTopBar();
        float y = ProDrawSongCard();
        y = ProDrawPlayers(y + 22f);
        ProDrawChat(y + 18f);
        ProDrawActionBar();
        ProDrawToasts();
        if (_overlay == Overlay.Settings) ProDrawSettings();
        if (_overlay == Overlay.Emote) ProDrawEmote(sb);
        if (_overlay == Overlay.ModPrompt) ProDrawModPrompt();
        ProDrawLobbyHints();
    }

    private void ProDrawTopBar()
    {
        var st = PsychOnline.State;
        string code = _codeReveal > 0 ? PsychOnline.Room?.RoomId ?? "????" : "????";
        FunkTheme.Box(0, 0, FunkUI.W, FunkTheme.TopH, FunkTheme.Ink);
        FunkTheme.Band(-20f, FunkTheme.TopH, 470f, 8f, -8f, FunkTheme.Yellow);
        FunkTheme.Band(440f, FunkTheme.TopH, 120f, 8f, -8f, FunkTheme.Orange);
        float x = FunkTheme.M;
        x += FunkTheme.Heading("ROOM", x, 12f, 0.5f, _time) + 18f;
        // the code itself in the bold alphabet on a yellow slanted plate
        float cw = FunkTheme.HeadingW(code, 0.5f) + 40f;
        FunkTheme.BandOutlined(x, 10f, cw, 44f, 12f, FunkTheme.NavyHi, 3f);
        FunkTheme.Heading(code, x + 20f, 12f, 0.5f, _time);
        x += cw + 16f;
        bool pad = ButtonPrompts.UseController(Input);
        x += FunkUI.Glyph(pad, Buttons.Back, "C", x, 20f, 26f) + 8f;
        FunkTheme.Txt(_codeReveal > 0 ? "COPIED!" : "SHOW CODE", x, 25f, 16f, _codeReveal > 0 ? FunkTheme.Green : FunkTheme.Text2);
        string srv = (PsychOnline.Server ?? "").Replace("wss://", "").Replace("ws://", "");
        int n = PsychOnline.AllPlayers().Count();
        bool pub = !(st?.Bool("isPrivate") ?? true);
        float rx = FunkUI.W - FunkTheme.M;
        // player count in Freeplay digits
        rx -= FunkTheme.Digits(n.ToString(), rx, 14f, 34f, Color.White, 1f) + 8f;
        rx -= FunkTheme.Txt("PLAYERS", rx, 25f, 16f, FunkTheme.Text2, 1f) + 20f;
        rx -= FunkTheme.Stamp(pub ? "PUBLIC" : "PRIVATE", rx, 19f, pub ? FunkTheme.Green : FunkTheme.Orange, 1f) + 2f;
        if (st?.Bool("anarchyMode") ?? false) rx -= FunkTheme.Stamp("ANARCHY", rx, 19f, FunkTheme.Red, 1f) + 2f;
        if (st?.Bool("teamMode") ?? false) rx -= FunkTheme.Stamp("TEAMS", rx, 19f, FunkTheme.Cyan, 1f) + 2f;
        if (st?.Bool("royalMode") ?? false) rx -= FunkTheme.Stamp("ONE LANE", rx, 19f, FunkTheme.Purple, 1f) + 2f;
        if (rx - x > 200f) FunkTheme.TxtFit(srv, rx - 12f, 25f, 15f, FunkTheme.Text3, rx - x - 30f, 1f);
    }

    /// <summary>Capsule nameplate above each character (RoomState ProfileBox): side band + health icon, name, status,
    /// ping, READY stamp; HOST / YOU tags on the top edge; bops to the lobby music.</summary>
    private void ProDrawProfileCards(bool hud)
    {
        var st = PsychOnline.State;
        var placed = new List<Rectangle>();
        float bop = FunkTheme.Bop;
        foreach (var lc in _chars.Values.OrderBy(c => c.Bf ? 1 : 0).ThenBy(c => c.Ox))
        {
            if (lc.Leave >= 0f || lc.Ch == null) continue;
            if (!PsychOnline.Players.Items.TryGetValue(lc.Sid, out var po) || po is not SchemaObject p) continue;
            var pos = CharPos(lc);
            var tl = WorldToScreen(pos.X, pos.Y); var br = WorldToScreen(pos.X + lc.Size.X, pos.Y + lc.Size.Y);
            const float w = 272f, h = 80f;
            float x = (tl.X + br.X) / 2f - w / 2f, y = 112f;
            x = Math.Clamp(x, FunkTheme.M, (hud ? ColX - 28f : FunkUI.W - FunkTheme.M) - w);
            var r = new Rectangle((int)x, (int)y, (int)w, (int)h);
            foreach (var q in placed) if (q.Intersects(new Rectangle(r.X - 12, r.Y - 24, r.Width + 24, r.Height + 36))) r.Y = q.Bottom + 34;
            placed.Add(r);
            float a = Math.Min(1f, lc.Appear * 1.4f), slide = FunkUI.EaseOutBack(lc.Appear);
            bool me = lc.Sid == PsychOnline.SessionId, host = lc.Sid == st?.Str("host"), ready = p.Bool("isReady");
            var side = lc.Bf ? FunkTheme.Cyan : FunkTheme.Purple;
            float s = 1f + 0.025f * bop + 0.12f * lc.ReadyPop;
            FunkUI.Push(r.Center.X, r.Bottom, 0f, s, s, 0f, (1f - slide) * -40f);
            FunkTheme.Panel(r.X, r.Y, r.Width, r.Height, FunkTheme.Navy, a);
            // side band with the character's health icon
            FunkTheme.Shape(r.X, r.Y, 64f, r.Height, FunkTheme.Cut, FunkTheme.A(side, a));
            FunkTheme.Tri(r.X + 64f, r.Y, 18f, r.Height, 3, FunkTheme.A(side, a));
            var ic = IconTex(lc.CharName);
            if (ic != null) FunkUI.Quad(ic, r.X + 38f, r.Y + r.Height / 2f, 72f, 72f, 0f, FunkTheme.A(Color.White, a), new Rectangle(0, 0, ic.Width / 2, ic.Height), lc.Bf ? SpriteEffects.FlipHorizontally : SpriteEffects.None);
            float tx = r.X + 90f;
            FunkTheme.TxtFit(p.Str("name") ?? "?", tx, r.Y + 12f, 20f, FunkTheme.A(p.Bool("verified") ? FunkTheme.Yellow : FunkTheme.Text, a), r.Right - tx - 60f);
            int ping = (int)p.Num("ping");
            if (FNF_MonoGame.Engine.PsychEngine.ClientPrefs.Data.ShowPing)
            {
                ProUI.Signal(r.Right - 40f, r.Y + 10f, ping);
                FunkTheme.Txt(ping + "ms", r.Right - 14f, r.Y + 34f, 14f, FunkTheme.A(ProUI.PingColor(ping), a), 1f);
            }
            string status = p.Str("status") ?? "";
            string skin = PsychOnline.SkinName(p);
            if (lc.Fallback && skin != null) status = "skin missing: " + skin;
            FunkTheme.TxtFit(status, tx, r.Y + 37f, 15f, FunkTheme.A(lc.Fallback && skin != null ? FunkTheme.Orange : FunkTheme.Text2, a), r.Right - tx - 60f);
            // READY stamp on the bottom edge
            float sy = r.Bottom - 12f;
            FunkTheme.Stamp(ready ? "READY" : p.Bool("hasSong") ? "NOT READY" : "NO SONG", tx, sy, ready ? FunkTheme.Green : FunkTheme.NavyHi, 0f, 14f, a, ready);
            if (p.Bool("botplay")) FunkTheme.Stamp("BOT", r.Right - 10f, sy, FunkTheme.Red, 1f, 14f, a);
            // tags on the top edge
            float tagX = r.Right - 6f;
            if (host) tagX -= FunkTheme.Stamp("HOST", tagX, r.Y - 14f, FunkTheme.Yellow, 1f, 14f, a);
            if (me) FunkTheme.Stamp("YOU", tagX, r.Y - 14f, FunkTheme.Text, 1f, 14f, a);
            FunkUI.Pop();
        }
    }

    /// <summary>NOW PLAYING: album cover, difficulty sprite, speed / chart stamps and the Freeplay capsule with the song
    /// name on its screen. Returns the panel bottom.</summary>
    private float ProDrawSongCard()
    {
        var st = PsychOnline.State; var me = PsychOnline.Self;
        float x = ColX, y = 98f, w = ColW;
        string song = st?.Str("song");
        if (string.IsNullOrEmpty(song))
        {
            float h0 = 128f;
            FunkTheme.Panel(x, y, w, h0, FunkTheme.Navy);
            FunkTheme.Tab("NOW PLAYING", x + 10f, y + 8f, FunkTheme.Yellow, _time, 28f);
            FunkTheme.Txt("No song picked yet", x + 18f, y + 54f, 20f, FunkTheme.Text);
            FunkTheme.Txt(PsychOnline.HasPerms ? "Press SONG to choose one." : "Waiting for the host...", x + 18f, y + 84f, 16f, FunkTheme.Text2);
            return y + h0;
        }
        string folder = st.Str("folder"), mod = st.Str("modDir");
        var card = PsychOnline.CardFor(folder, song, mod);
        string diff = PsychOnline.CurrentDiffName();
        const float art = 92f;
        float h = 48f + art + 12f + FunkTheme.CapsuleH(w - 16f) + 34f;
        FunkTheme.Panel(x, y, w, h, FunkTheme.Navy);
        FunkTheme.Tab("NOW PLAYING", x + 10f, y + 8f, FunkTheme.Yellow, _time, 28f);
        float ay = y + 48f;
        var album = !string.IsNullOrEmpty(card.Album) ? Tex("menus/freeplay/albumRoll/" + card.Album + ".png") : null;
        FunkTheme.Box(x + 14f, ay - 4f, art + 8f, art + 8f, FunkTheme.Ink);
        if (album != null) FunkUI.Quad(album, x + 18f + art / 2f, ay + art / 2f, art, art, 0f, Color.White);
        else
        {
            FunkTheme.Box(x + 18f, ay, art, art, FunkTheme.NavyHi);
            var io = IconTex(card.Opponent);
            if (io != null) FunkUI.Quad(io, x + 18f + art / 2f, ay + art / 2f, 84f, 84f, 0f, Color.White, new Rectangle(0, 0, io.Width / 2, io.Height));
        }
        float tx = x + 18f + art + 18f;
        float dw = FunkTheme.Difficulty(diff, tx + 62f, ay + 18f, 34f);
        if (dw <= 0f) FunkTheme.Stamp(diff ?? "", tx, ay + 6f, DiffColors.TryGetValue(diff ?? "", out var dc) ? dc : FunkTheme.Text);
        float sp = PsychOnline.RoomSongSpeed;
        float sx = tx;
        if (Math.Abs(sp - 1f) > 0.001f) sx += FunkTheme.Stamp(sp.ToString("0.##") + "x SPEED", sx, ay + 46f, FunkTheme.Orange);
        bool has = me?.Bool("hasSong") ?? false;
        FunkTheme.Stamp(has ? "CHART OK" : PsychOnline.RoomNeedsMod ? "MOD NEEDED" : "CHART MISSING", tx, ay + 70f, has ? FunkTheme.Green : FunkTheme.Red, 0f, 14f, 1f, true);
        // the real Freeplay capsule, song name on its screen
        float cy = ay + art + 12f;
        var scr = FunkTheme.Capsule(x + 8f, cy, w - 16f, true);
        string title = card.Title ?? folder;
        float fs = 20f;
        while (fs > 14f && FunkTheme.TextW(title, fs) > scr.Width - 16f) fs -= 1f;
        FunkTheme.TxtFit(title, scr.X + 10f, scr.Center.Y - fs * 0.55f, fs, FunkTheme.ScreenText, scr.Width - 16f);
        string sub = (string.IsNullOrEmpty(card.Artist) ? "" : card.Artist + "  /  ") + (string.IsNullOrEmpty(mod) ? "Base game" : (PsychOnline.PackName(mod) ?? mod)) + "  /  " + PsychOnline.RoomStageTitle();
        FunkTheme.TxtFit(sub, x + 18f, cy + FunkTheme.CapsuleH(w - 16f) + 6f, 15f, FunkTheme.Text2, w - 36f);
        return y + h;
    }

    // players list scrolling (the panel stops above the action bar; RS / PgUp-PgDn / mouse wheel scroll it)
    private int _plScroll, _plVisible = 99, _plCount;
    private Rectangle _plRect;
    private float _plRepeat;
    private const float PlRowH = 30f;

    /// <summary>Dev: FNF_PSO_FAKEPLAYERS=N adds N placeholder rows to the PLAYERS panel (layout / scroll tests).</summary>
    private static readonly int DevFakePlayers = int.TryParse(Environment.GetEnvironmentVariable("FNF_PSO_FAKEPLAYERS"), out var fp) ? fp : 0;

    private bool PlayersOverflow => _plCount > _plVisible;

    /// <summary>Scroll input for the PLAYERS panel: right stick up/down (held = repeat), PageUp/PageDown, mouse wheel over
    /// the panel or a click on its scroll arrows. Returns true when it consumed the input this frame.</summary>
    private bool UpdatePlayersScroll(float dt)
    {
        int max = Math.Max(0, _plCount - _plVisible);
        if (max == 0) { _plScroll = 0; return false; }
        bool rsUp = Input.IsGamePadHeld(Buttons.RightThumbstickUp), rsDown = Input.IsGamePadHeld(Buttons.RightThumbstickDown);
        int dir = 0;
        if (Input.IsGamePadPressed(Buttons.RightThumbstickUp) || Input.IsPressed(Keys.PageUp)) { dir = -1; _plRepeat = 0.35f; }
        else if (Input.IsGamePadPressed(Buttons.RightThumbstickDown) || Input.IsPressed(Keys.PageDown)) { dir = 1; _plRepeat = 0.35f; }
        else if (rsUp || rsDown) { _plRepeat -= dt; if (_plRepeat <= 0f) { dir = rsUp ? -1 : 1; _plRepeat = 0.09f; } }
        var mp = MouseVirtual;
        bool over = _plRect.Contains((int)mp.X, (int)mp.Y);
        if (over && Input.ScrollDelta != 0) dir = Input.ScrollDelta > 0 ? -1 : 1;
        if (over && Input.MouseLeftPressed)
        {
            if (mp.Y < _plRect.Y + 48) dir = -1; else if (mp.Y > _plRect.Bottom - 26) dir = 1;
        }
        if (dir == 0) return false;
        int prev = _plScroll;
        _plScroll = Math.Clamp(_plScroll + dir, 0, max);
        if (_plScroll != prev) Audio.PlaySound("scrollMenu", 0.4f);
        return true;
    }

    private float ProDrawPlayers(float y)
    {
        var st = PsychOnline.State;
        var players = PsychOnline.AllPlayers().Select(t => (t.sid, t.p, fake: (string)null)).ToList();
        for (int i = 0; i < DevFakePlayers; i++) players.Add(("fake" + i, null, "Player" + (i + 2)));
        // the panel never runs into the action bar: rows that don't fit scroll
        float maxH = BarY - 16f - y;
        int fit = Math.Max(1, (int)((maxH - 56f) / PlRowH));
        int count = Math.Max(1, players.Count);
        bool scroll = count > fit;
        int vis = scroll ? fit : count;
        _plCount = players.Count; _plVisible = vis;
        _plScroll = Math.Clamp(_plScroll, 0, Math.Max(0, players.Count - vis));
        float h = 48f + vis * PlRowH + 8f + (scroll ? 18f : 0f);
        _plRect = new Rectangle((int)ColX, (int)y, (int)ColW, (int)h);
        FunkTheme.Panel(ColX, y, ColW, h, FunkTheme.Navy);
        FunkTheme.Tab("PLAYERS", ColX + 10f, y + 8f, FunkTheme.Yellow, _time, 28f);
        if (st?.Bool("networkOnly") ?? false) FunkTheme.Stamp("NETWORK ONLY", ColX + ColW - 14f, y + 10f, FunkTheme.Cyan, 1f);
        else if (scroll) FunkTheme.Txt((_plScroll + 1) + "-" + (_plScroll + vis) + " OF " + players.Count, ColX + ColW - 14f, y + 16f, 14f, FunkTheme.Text2, 1f);
        float ry = y + 48f;
        float right = ColX + ColW - (scroll ? 30f : 14f);
        for (int i = _plScroll; i < Math.Min(players.Count, _plScroll + vis); i++)
        {
            var (sid, p, fake) = players[i];
            bool bf = p?.Bool("bfSide") ?? (i % 2 == 0), ready = p?.Bool("isReady") ?? false;
            bool hasSong = p?.Bool("hasSong") ?? false;
            FunkTheme.Band(ColX + 14f, ry + 3f, 12f, 20f, 4f, bf ? FunkTheme.Cyan : FunkTheme.Purple);
            float nw = FunkTheme.TxtFit(p?.Str("name") ?? fake ?? "?", ColX + 34f, ry + 5f, 16f, sid == PsychOnline.SessionId ? FunkTheme.Yellow : FunkTheme.Text, 150f);
            if (sid == st?.Str("host")) FunkTheme.Txt("HOST", ColX + 42f + nw, ry + 6f, 14f, FunkTheme.Text3);
            FunkTheme.Txt(ready ? "READY" : hasSong ? "NOT READY" : "NO SONG", right - 68f, ry + 6f, 14f, ready ? FunkTheme.Green : FunkTheme.Text3, 1f);
            int ping = (int)(p?.Num("ping") ?? 0);
            FunkTheme.Txt(ping + "ms", right, ry + 6f, 14f, ProUI.PingColor(ping), 1f);
            ry += PlRowH;
        }
        if (scroll)
        {
            // scroll track on the right edge + arrows, controller / key hint under the rows
            float tx = ColX + ColW - 22f, ty = y + 50f, th = vis * PlRowH - 4f;
            FunkTheme.Box(tx, ty, 8f, th, FunkTheme.NavyLo);
            float kh = Math.Max(18f, th * vis / players.Count);
            float ky = ty + (th - kh) * (_plScroll / (float)Math.Max(1, players.Count - vis));
            FunkTheme.Box(tx, ky, 8f, kh, FunkTheme.Yellow);
            if (_plScroll > 0) { FunkTheme.Tri(tx - 2f, ty - 12f, 6f, 8f, 2, FunkTheme.Yellow); FunkTheme.Tri(tx + 4f, ty - 12f, 6f, 8f, 3, FunkTheme.Yellow); }
            if (_plScroll < players.Count - vis) { FunkTheme.Tri(tx - 2f, ty + th + 4f, 6f, 8f, 1, FunkTheme.Yellow); FunkTheme.Tri(tx + 4f, ty + th + 4f, 6f, 8f, 0, FunkTheme.Yellow); }
            bool pad = ButtonPrompts.UseController(Input);
            float hx = ColX + 34f, hy = y + h - 26f;
            hx += FunkUI.Glyph(pad, Buttons.RightStick, "PGUP", hx, hy, 20f) + 6f;
            if (!pad) hx += FunkUI.Glyph(false, Buttons.RightStick, "PGDN", hx, hy, 20f) + 6f;
            FunkTheme.Txt("SCROLL", hx, hy + 3f, 14f, FunkTheme.Text2);
        }
        return y + h;
    }

    /// <summary>Chat log as speech bubbles (newest at the bottom), in the space left in the right column.</summary>
    private void ProDrawChat(float y)
    {
        float bottom = BarY - 18f;
        if (bottom - y < 50f) return;
        var log = PsychOnline.Log;
        float bw = ColW - 24f, by = bottom;
        if (log.Count == 0)
        {
            FunkTheme.Panel(ColX + 12f, bottom - 40f, bw, 34f, FunkTheme.NavyLo, 0.9f, 10f, 3f, 0f);
            FunkTheme.Txt("No messages yet - press CHAT to say hi!", ColX + 24f, bottom - 31f, 15f, FunkTheme.Text2);
            return;
        }
        for (int i = log.Count - 1; i >= 0; i--)
        {
            var lines = FunkUI.Wrap(log[i], bw - 24f, 15f);
            float bh = lines.Count * 19f + 14f;
            if (by - bh - 10f < y) break;
            by -= bh + 10f;
            bool sys = !log[i].Contains(':');
            bool newest = i == log.Count - 1;
            var fill = sys ? FunkTheme.NavyLo : Color.White;
            FunkTheme.Panel(ColX + 12f, by, bw, bh, fill, newest ? 1f : 0.92f, 10f, 3f, newest ? 4f : 0f);
            if (!sys) FunkTheme.Tri(ColX + 22f, by + bh + 2f, 12f, 9f, 0, FunkTheme.Ink);   // bubble tail
            float ly = by + 7f;
            foreach (var l in lines) { FunkTheme.Txt(l, ColX + 24f, ly, 15f, sys ? FunkTheme.Text2 : FunkTheme.Ink); ly += 19f; }
        }
    }

    /// <summary>Bottom row: icon + label capsules, controller glyph chip on each button's corner, focus pop.</summary>
    private void ProDrawActionBar()
    {
        var dock = Dock(); int n = dock.Count;
        bool pad = ButtonPrompts.UseController(Input);
        FunkTheme.Band(-30f, BarY - 6f, FunkUI.W + 60f, FunkUI.H - FunkTheme.BottomH - BarY + 6f, 0f, FunkTheme.A(FunkTheme.Night, 0.78f));
        bool readyNow = PsychOnline.Self?.Bool("isReady") ?? false;
        float pop = MathF.Exp(-_thFocusT * 10f);
        for (int i = 0; i < n; i++)
        {
            var b = dock[i]; var r = DockRect(i, n);
            float sx = _shake > 0 && _shakeBtn == i ? (float)Math.Sin(_shake * 120f) * 5f * (_shake / 0.3f) : 0f;
            bool sel = i == _dockSel && _overlay == Overlay.None;
            bool active = b.Id == "READY" && readyNow;
            var accent = b.Id == "LEAVE" ? FunkTheme.Red : b.Id == "READY" ? FunkTheme.Green : FunkTheme.Yellow;
            float s = sel ? 1.06f + 0.10f * pop + 0.02f * FunkTheme.Bop : 1f;
            FunkUI.Push(r.Center.X + sx, r.Center.Y, 0f, s, s);
            float bx = r.X + sx;
            FunkTheme.Panel(bx, r.Y, r.Width, r.Height, sel ? accent : b.Enabled ? (active ? FunkTheme.NavyHi : FunkTheme.Navy) : FunkTheme.NavyLo, 1f, 12f, FunkTheme.Outline, sel ? 8f : FunkTheme.Shadow);
            if (active && !sel) FunkTheme.Box(bx, r.Bottom - 6f, r.Width - 12f, 6f, FunkTheme.Green);
            string icon = DockIcon(b);
            float lx = bx + 14f;
            if (icon != null) { FunkTheme.Icon(icon == "play" && readyNow ? "ready" : icon, lx + 15f, r.Y + r.Height / 2f, 34f, sel, b.Enabled || sel ? 1f : 0.45f); lx += 38f; }
            else lx += 6f;
            FunkTheme.Txt(b.Label, lx, r.Y + r.Height / 2f - 9f, 18f, sel ? FunkTheme.Ink : b.Enabled ? FunkTheme.Text : FunkTheme.Text3);
            // glyph chip on the top-left corner (Xbox art on a pad, keycap on a keyboard)
            FunkUI.Glyph(pad, b.Pad, b.Key, bx - 8f, r.Y - 14f, 24f, b.Enabled || sel ? 1f : 0.6f);
            if (b.Id == "LEAVE" && _leaveHold > 0f) FunkTheme.Box(bx, r.Bottom - 6f, (r.Width - 12f) * Math.Min(1f, _leaveHold), 6f, FunkTheme.Red);
            FunkUI.Pop();
        }
    }
}
