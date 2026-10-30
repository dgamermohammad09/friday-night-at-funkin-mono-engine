using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// In-room mod browser, professional flat look (ProUI, same as the Psych Online lobby / song select): header with the room
/// status, segmented category tabs, a clean 3-column grid of mod cards (full-bleed thumbnail, name, author, likes, INSTALLED
/// chip, accent focus frame), flat detail page with facts + file list, install progress, QR link page and one hint bar.
/// Same geometry as the hand-made browser (CardPos / FunkMenu rows), so controller + mouse navigation are unchanged.
/// </summary>
public partial class ModBrowserScene
{
    private void ProDraw(SpriteBatch sb)
    {
        _desat ??= LoadOpt("menus/main_menu/menuDesat.png");
        _fm.Update(_page, null, Accent, 1f / 60f);
        FunkUI.Begin(sb);
        ProUI.Backdrop(_desat);
        bool ok = PsychOnline.Connected;
        string roomTxt = !_room ? "INSTALL ANY MOD" : _roomLost ? "ROOM LOST" : ok ? "ROOM " + (PsychOnline.Room?.RoomId ?? "") + "  -  " + PsychOnline.AllPlayers().Count() + " PLAYERS" : "RECONNECTING";
        string title = _page == Page.Link ? "GET IT HERE" : _page == Page.Detail || _page == Page.Installing ? "MOD DETAILS" : "MOD BROWSER";
        ProUI.Header(title, "GAMEBANANA  -  " + (_search != null ? "SEARCH: " + _search.ToUpperInvariant() : GameBanana.Sorts[_sort].Label.ToUpperInvariant()), roomTxt, !_room ? ProUI.Fg2 : _roomLost ? ProUI.Bad : ok ? ProUI.Good : ProUI.Warn, _time);
        switch (_page)
        {
            case Page.Grid: ProTabs(); ProGrid(); break;
            case Page.Search: ProSearch(); break;
            case Page.Detail: ProDetail(); break;
            case Page.Installing: ProInstalling(); break;
            case Page.Link: ProLink(); break;
        }
        // room chat / notifications: small flat toasts above the hint bar (right)
        float fy = FunkUI.H - ProUI.BarH - 46f;
        for (int i = _feed.Count - 1; i >= 0; i--)
        {
            var (text, ft) = _feed[i];
            float a = Math.Min(1f, ft / 0.6f);
            string s = FunkMenu.Ellipsize(text, 15f, 440f);
            float w = ProUI.Measure(s, 15f) + 30f, x = FunkUI.W - ProUI.M - w;
            FunkTheme.Panel(x, fy, w, 30f, Color.White, a, 9f, 3f, 4f);
            ProUI.Text(s, x + 15f, fy + 8f, 15f, ProUI.A(FunkTheme.Ink, a));
            fy -= 36f;
        }
        ProUI.HintBar(Input, Prompts(), _statusT > 0f ? _status : null);
        sb.End();
    }

    private void ProTabs()
    {
        float x = ProUI.M, y = 124f;
        bool pad = ButtonPrompts.UseController(Input);
        x += FunkUI.Glyph(pad, Buttons.LeftShoulder, "Q", x, y - 1f, 24f) + 14f;
        for (int i = 0; i < GameBanana.Tabs.Length; i++)
        {
            bool on = _search == null && i == _tab;
            string t = GameBanana.Tabs[i].Label.ToUpperInvariant();
            float w = ProUI.Measure(t, 16f) + 40f;
            FunkTheme.BandOutlined(x, y - 4f, w, 32f, 10f, on ? FunkTheme.Yellow : FunkTheme.Navy, 3f, on ? 5f : 0f);
            ProUI.Text(t, x + w / 2f, y + 4f, 16f, on ? FunkTheme.Ink : ProUI.Fg2, 0.5f);
            x += w + 8f;
        }
        FunkUI.Glyph(pad, Buttons.RightShoulder, "E", x + 10f, y - 1f, 24f);
        string count = _total > 0 ? GameBanana.Count(_total) + " MODS" : _listTask != null ? "LOADING..." : "";
        if (count.Length > 0) ProUI.Caption(count, FunkUI.W - ProUI.M, y + 6f, ProUI.Fg2, 13f, 1f);
    }

    private void ProGrid()
    {
        int n = _mods.Count;
        if (n == 0)
        {
            float cx = FunkUI.W / 2f;
            if (_error != null) { ProUI.Text(_error, cx, 330f, 17f, ProUI.Bad, 0.5f); ProUI.Text("Press A to try again.", cx, 362f, 15f, ProUI.Fg3, 0.5f); }
            else
            {
                ProUI.Text("Loading " + GameBanana.Tabs[_tab].Label + "...", cx, 330f, 17f, ProUI.Fg2, 0.5f);
                float s = (_time * 0.8f) % 1f; ProUI.Rect(cx - 160f, 366f, 320f, 4f, ProUI.PanelHi); ProUI.Rect(cx - 160f + 230f * s, 366f, 90f, 4f, ProUI.Accent);
            }
            return;
        }
        float clipTop = 168f, clipBottom = FunkUI.H - ProUI.BarH - 10f;
        for (int i = 0; i < n; i++)
        {
            var (x, y) = CardPos(i);
            if (y + CardH < clipTop || y > clipBottom) continue;
            float ea = y < clipTop ? FunkUI.Clamp01(1f - (clipTop - y) / 60f) : y + CardH > clipBottom ? FunkUI.Clamp01(1f - (y + CardH - clipBottom) / 60f) : 1f;
            if (ea <= 0.05f) continue;
            ProCard(_mods[i], x, y, i == _sel, ea);
        }
        if (_listTask != null) ProUI.Caption("LOADING MORE", FunkUI.W / 2f, clipBottom - 18f, ProUI.Fg3, 13f, 0.5f);
    }

    private void ProCard(GameBanana.Mod m, float x, float y, bool sel, float ea)
    {
        float pop = sel ? 1.03f + 0.01f * FunkTheme.Bop : 1f;
        if (sel) FunkUI.Push(x + CardW / 2f, y + CardH / 2f, 0f, pop, pop);
        FunkTheme.Panel(x, y, CardW, CardH, sel ? FunkTheme.Yellow : FunkTheme.Navy, ea, FunkTheme.Cut, FunkTheme.Outline, sel ? 9f : FunkTheme.Shadow);
        float ph = 150f;
        FunkTheme.Box(x, y + FunkTheme.Cut, CardW, ph - FunkTheme.Cut, ProUI.A(ProUI.Bg, ea));
        FunkTheme.Box(x + FunkTheme.Cut, y, CardW - FunkTheme.Cut, FunkTheme.Cut, ProUI.A(ProUI.Bg, ea));
        FunkTheme.Tri(x, y, FunkTheme.Cut, FunkTheme.Cut, 2, ProUI.A(ProUI.Bg, ea));
        FunkTheme.CutImage(Thumb(m.Thumb), x, y, CardW, ph, FunkTheme.Cut, ea);
        if (_installed.Contains("https://gamebanana.com/mods/" + m.Id)) ProUI.Chip("INSTALLED", x + CardW - 10f, y + 10f, ProUI.Good, 1f, true);
        FunkTheme.Box(x, y + ph, CardW, 4f, ProUI.A(FunkTheme.Ink, ea));
        ProUI.TextFit(m.Name, x + 14f, y + ph + 14f, 17f, ProUI.A(sel ? FunkTheme.Ink : ProUI.Fg, ea), CardW - 28f);
        ProUI.TextFit("by " + m.Author, x + 14f, y + ph + 40f, 14f, ProUI.A(sel ? FunkTheme.NavyLo : ProUI.Fg2, ea), CardW - 120f);
        ProUI.Text(GameBanana.Count(m.Likes) + " likes", x + CardW - 14f, y + ph + 40f, 14f, ProUI.A(sel ? FunkTheme.NavyLo : ProUI.Fg2, ea), 1f);
        if (sel) FunkUI.Pop();
    }

    private void ProDetail()
    {
        if (_cur == null) return;
        float px = ProUI.M, py = 116f, pw = 480f, ph = 270f;
        FunkTheme.Panel(px, py, pw, ph, ProUI.Bg);
        FunkTheme.CutImage(Thumb(_cur.Thumb), px, py, pw - FunkTheme.Cut, ph, FunkTheme.Cut);
        FunkTheme.Box(px + pw - FunkTheme.Cut, py, FunkTheme.Cut, ph - FunkTheme.Cut, ProUI.Bg);
        float ix = px + pw + ProUI.G, iw = FunkUI.W - ProUI.M - ix;
        float top = ProUI.Card(ix, py, iw, ph, "MOD");
        var lines = FunkUI.Wrap(_cur.Name, iw - 32f, 22f).Take(2).ToList();
        foreach (var l in lines) { ProUI.Text(l, ix + 16f, top, 22f, ProUI.Fg); top += 28f; }
        ProUI.TextFit("by " + _cur.Author, ix + 16f, top + 2f, 15f, ProUI.Fg2, iw - 32f); top += 30f;
        string added = _cur.Added > 0 ? DateTimeOffset.FromUnixTimeSeconds(_cur.Added).ToString("yyyy-MM-dd") : "?";
        void Fact(string k, string v) { ProUI.Caption(k, ix + 16f, top + 3f); ProUI.TextFit(v, ix + iw - 16f, top, 15f, ProUI.Fg, iw - 160f, 1f); top += 28f; }
        Fact("Category", _cur.Category ?? "-");
        Fact("Likes / Views", GameBanana.Count(_cur.Likes) + " / " + GameBanana.Count(_cur.Views));
        Fact("Added", added + (string.IsNullOrWhiteSpace(_cur.Version) ? "" : "   v" + _cur.Version));
        if (_installed.Contains("https://gamebanana.com/mods/" + _cur.Id)) ProUI.Chip("INSTALLED - INSTALLING AGAIN REPLACES IT", ix + 16f, py + ph - 36f, ProUI.Good);
        // files
        var rows = FileRows();
        float fy = py + ph + 24f;
        FunkTheme.Tab("FILES", ProUI.M, fy - 6f, FunkTheme.Cyan, _time, 28f);
        _fmFiles.CenterX = FunkUI.W / 2f; _fmFiles.RowW = FunkUI.W - 2 * ProUI.M; _fmFiles.ListTop = fy + 34f; _fmFiles.ListBottom = FunkUI.H - ProUI.BarH - 60f; _fmFiles.RowH = 40f; _fmFiles.RowGap = 4f;
        if (rows.Count > 0)
        {
            _fmFiles.Sel = _fileSel;
            _fmFiles.Update(_cur.Id, rows, Accent, 1f / 60f);
            float x = ProUI.M, w = _fmFiles.RowW;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i]; float cy = _fmFiles.RowCenterY(i), y = cy - _fmFiles.RowH / 2f;
                if (y < _fmFiles.ListTop - 2f || y + _fmFiles.RowH > _fmFiles.ListBottom + 2f) continue;
                bool sel = i == _fileSel;
                FunkTheme.Panel(x + (sel ? 0f : 8f), y + 2f, w - (sel ? 0f : 16f), _fmFiles.RowH - 4f, sel ? FunkTheme.Yellow : FunkTheme.Navy, 1f, 9f, 3f, sel ? 6f : 3f);
                float right = x + w - 18f;
                if (!string.IsNullOrEmpty(r.Value)) { right -= ProUI.Text(r.Value, right, cy - 8f, 15f, sel ? FunkTheme.Ink : ProUI.Fg2, 1f) + 14f; }
                if (!string.IsNullOrEmpty(r.Badge)) right -= ProUI.Chip(r.Badge, right, cy - 12f, r.Disabled ? ProUI.Bad : r.Badge.Contains("EXE") ? ProUI.Warn : ProUI.Good, 1f) + 12f;
                ProUI.TextFit(r.Label, x + 22f, cy - 9f, 17f, r.Disabled ? ProUI.Fg3 : sel ? FunkTheme.Ink : ProUI.Fg, right - x - 30f);
            }
            if (_fileSel < rows.Count) ProUI.TextFit(rows[_fileSel].Desc ?? "", ProUI.M, FunkUI.H - ProUI.BarH - 40f, 15f, ProUI.Fg2, FunkUI.W - 2 * ProUI.M);
        }
        else if (_filesError != null) ProUI.Text(_filesError, ProUI.M, fy + 30f, 16f, ProUI.Bad);
        else ProUI.Text("Getting the file list" + new string('.', 1 + (int)(_time * 3) % 3), ProUI.M, fy + 30f, 16f, ProUI.Fg2);
    }

    private void ProInstalling()
    {
        var j = _job;
        if (j == null) return;
        const float w = 720f, h = 230f;
        float x = FunkUI.W / 2f - w / 2f, y = 220f;
        ProUI.PanelBox(x, y, w, h, 0.97f);
        bool okDone = j.Done && j.Error == null;
        FunkTheme.Tab(!j.Done ? (j.Progress >= 0f ? "DOWNLOADING" : "INSTALLING") : okDone ? "INSTALLED" : "FAILED", x + 14f, y + 14f, !j.Done ? FunkTheme.Yellow : okDone ? FunkTheme.Green : FunkTheme.Red, _time, 40f);
        ProUI.TextFit(_cur?.Name ?? j.Result ?? "", x + 28f, y + 80f, 17f, ProUI.Fg, w - 56f);
        if (!j.Done)
        {
            if (j.Progress >= 0f) ProUI.Progress(x + 28f, y + 120f, w - 56f, j.Progress, 8f);
            else { float s = (_time * 0.8f) % 1f; ProUI.Progress(x + 28f, y + 120f, w - 56f, 0f); FunkTheme.Shape(x + 28f + (w - 56f - 120f) * s, y + 120f, 120f, 10f, 4f, FunkTheme.Yellow); }
            ProUI.TextFit((j.Progress >= 0f ? (int)(j.Progress * 100f) + "%   " : "") + (j.Status ?? ""), x + 28f, y + 142f, 14f, ProUI.Fg2, w - 56f);
        }
        else if (j.Error != null)
            FunkUI.Paragraph((j.BrowserUrl != null ? "Couldn't download it here: " + j.Error + ". " + (BrowserOnPc ? "Open the page in your web browser instead?" : "Get it with a phone / browser instead?") : "Couldn't install it: " + j.Error), x + 28f, y + 116f, w - 56f, 15f, ProUI.Bad, 0f, 1.35f);
        else
        {
            string where = j.Layout == "V-Slice" ? "Heads up: V-Slice mods can't be played in Psych Online rooms."
                : PsychOnline.SongCountOfMod(j.Result) is int ns && ns > 0 ? ns + " new song" + (ns == 1 ? "" : "s") + " for the room - go back and pick one."
                : "No Psych songs found in it for the room.";
            FunkUI.Paragraph("Installed as a " + j.Layout + " mod (mods/" + j.Result + ") and enabled. " + where, x + 28f, y + 116f, w - 56f, 15f, ProUI.Fg2, 0f, 1.35f);
        }
    }

    private void ProLink()
    {
        float qs = 300f, qx = ProUI.M + 40f, qy = 150f;
        ProUI.Rect(qx - 30f, qy - 30f, qs + 60f, qs + 60f, Color.White);
        if (_qr != null)
        {
            float m = qs / _qr.Size;
            for (int y = 0; y < _qr.Size; y++)
                for (int x = 0; x < _qr.Size; x++)
                    if (_qr.Modules[y, x]) FunkUI.Fill(qx + x * m, qy + y * m, m + 0.6f, m + 0.6f, Color.Black);
        }
        float nx = qx + qs + 70f, nw = FunkUI.W - ProUI.M - nx;
        float top = ProUI.Card(nx, qy - 30f, nw, qs + 60f, "OPEN ON ANOTHER DEVICE");
        ProUI.Text("Scan the code with your phone, or type this address:", nx + 16f, top, 15f, ProUI.Fg2); top += 32f;
        foreach (var l in HardWrap(_linkUrl ?? "", nw - 32f, 17f)) { ProUI.Text(l, nx + 16f, top, 17f, ProUI.Bf); top += 24f; }
        top += 14f;
        FunkUI.Paragraph(_linkWhy ?? "", nx + 16f, top, nw - 32f, 15f, ProUI.Warn, 0f, 1.35f);
        ProUI.Text("Then copy the mod folder into  mods/  (or install it on PC and copy it over).", nx + 16f, qy + qs + 2f, 14f, ProUI.Fg3);
    }

    private void ProSearch()
    {
        const float w = 820f, h = 230f;
        float x = FunkUI.W / 2f - w / 2f, y = 190f;
        ProUI.PanelBox(x, y, w, h, 0.97f);
        ProUI.Caption("SEARCH GAMEBANANA", x + 28f, y + 24f, ProUI.Accent);
        ProUI.Rect(x + 28f, y + 52f, w - 56f - 70f, 56f, ProUI.Bg); ProUI.Outline(x + 28f, y + 52f, w - 56f - 70f, 56f, ProUI.A(ProUI.Accent, 0.7f));
        string shown = _editBuf ?? "";
        while (shown.Length > 0 && ProUI.Measure(shown, 26f) > w - 56f - 70f - 32f) shown = shown.Substring(1);
        float tw = ProUI.Text(shown, x + 44f, y + 66f, 26f, ProUI.Fg);
        if (((int)(_time * 2f)) % 2 == 0) ProUI.Rect(x + 46f + tw, y + 64f, 2f, 32f, ProUI.Accent);
        float cx = x + w - 28f - 56f;
        ProUI.Rect(cx, y + 52f, 56f, 56f, ProUI.PanelHi);
        ProUI.Text(EditChars[_editChar].ToString(), cx + 28f, y + 66f, 26f, ProUI.Accent, 0.5f);
        ProUI.Text("Friday Night Funkin' mods with this name (e.g. tricky, pico, hex).", x + 28f, y + 128f, 15f, ProUI.Fg2);
        FunkUI.Paragraph("Keyboard: just type.  Controller: Up / Down picks a character, Right adds it, X deletes, A searches.", x + 28f, y + h - 54f, w - 56f, 14f, ProUI.Fg3, 0f, 1.3f);
    }
}
