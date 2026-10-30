using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// In-game GameBanana mod browser (Mods screen / Options > Psych Online > Mod Browser). Hand-made FNF style (FunkUI):
/// taped polaroid cards with the mod thumbnail, name, author and likes on a tilted grid, category tabs (LB/RB, Q/E), sort
/// (X / S), search (Y / F, notebook editor), detail page with the mod's files (paper strips) and a download / install
/// progress screen. Psych Engine and V-Slice layouts are detected and the mod is enabled after the install
/// (Engine/GameBanana.cs). Controller, keyboard and mouse (hover, click, wheel).
/// Dev hooks: FNF_GB_SEARCH=&lt;text&gt; (start with a search), FNF_GB_OPEN=&lt;index&gt; (open card n), FNF_GB_INSTALL=1 (install the first supported file).
/// </summary>
public partial class ModBrowserScene : Scene
{
    public override bool NativeResolution => true;

    private enum Page { Grid, Detail, Installing, Search, Link }
    private Page _page = Page.Grid;
    private readonly Scene _back;
    private readonly FunkMenu _fm = new FunkMenu();
    private readonly FunkMenu _fmFiles = new FunkMenu { ListTop = 448f, ListBottom = 572f, RowH = 42f, RowGap = 10f, RowW = 1090f };
    private Texture2D _desat;
    private float _time, _pageTime, _selTime = 10f;
    private int _tab, _sort, _sel, _lastSel = -1;
    private string _search;     // null = browsing a tab
    private readonly List<GameBanana.Mod> _mods = new();
    private int _total, _nextPage = 1;
    private Task<(List<GameBanana.Mod> mods, int total)> _listTask;
    private string _error;
    private int _gen;           // list generation (ignore stale results)
    private float _scrollY, _scrollTarget;
    private string _status; private float _statusT;
    private HashSet<string> _installed = GameBanana.InstalledUrls();

    // detail
    private GameBanana.Mod _cur;
    private Task<(List<GameBanana.FileInfo> files, string text)> _filesTask;
    private List<GameBanana.FileInfo> _files;
    private string _filesError;
    private int _fileSel;
    private GameBanana.Job _job;
    private float _jobDoneT;

    // "open in browser" page (Xbox / Android: URL + QR code; PC opens the system browser)
    private string _linkUrl, _linkWhy; private QrCode _qr; private Page _linkBack;
    private static readonly bool ForceQr = Environment.GetEnvironmentVariable("FNF_GB_FORCEQR") == "1";

    // search editor
    private string _editBuf = ""; private int _editChar;
    private const string EditChars = "abcdefghijklmnopqrstuvwxyz0123456789 -'!";

    // thumbnails
    private readonly Dictionary<string, Texture2D> _thumbs = new();
    private readonly HashSet<string> _thumbPending = new();
    private readonly ConcurrentQueue<(string url, byte[] data)> _thumbDone = new();
    private int _thumbRunning;

    private Vector2 _lastMouse = new(-1, -1);
    private static readonly bool DevNoMouse = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FNF_SHOTS")) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FNF_DEVMOUSE")); // dev: FNF_DEVMOUSE tests the mouse path
    private bool _devOpened, _devInstalled;

    // Psych Online room mode (opened from a room's lobby / song list): the room stays connected (KeepAlive = heartbeats + reconnect),
    // room chat/notifications show as small notes, and leaving returns to the SAME room (song list at the newly installed mod).
    private readonly bool _room;
    private readonly int _openModId;
    private readonly string _openLink, _openLinkWhy;
    private bool _detailOnly, _linkOnly, _roomLost;
    private string _lastInstalled;
    private int _feedSerial = -1;
    private readonly List<(string text, float t)> _feed = new();
    private float _roomT;

    public ModBrowserScene(Scene back = null, bool room = false, int openModId = 0, string openLink = null, string linkWhy = null)
    { _back = back; _room = room; _openModId = openModId; _openLink = openLink; _openLinkWhy = linkWhy; }

    private void ExitToRoom(string installed)
    {
        if (_room) { if (PsychOnline.Connected) PsychOnline.Send("status", "In the Lobby"); Game.Scenes.ChangeScene(PsychOnlineScene.BackToRoom(installed)); }
        else Game.Scenes.ChangeScene(_back ?? new ModsScene());
    }

    /// <summary>Room mode tick: keep the Colyseus room alive, collect chat/log lines, follow a starting match.</summary>
    private void UpdateRoom(float dt)
    {
        _roomT += dt;
        if (!_roomLost && !PsychOnline.KeepAlive()) { _roomLost = true; SetStatus("Lost the connection to the room"); }
        if (_feedSerial < 0) _feedSerial = PsychOnline.LogSerial;
        while (_feedSerial < PsychOnline.LogSerial)
        {
            int back = PsychOnline.LogSerial - _feedSerial;   // lines not shown yet (Log keeps the last 40)
            _feedSerial++;
            if (back <= PsychOnline.Log.Count) _feed.Add((PsychOnline.Log[PsychOnline.Log.Count - back], 7f));
            if (_feed.Count > 4) _feed.RemoveAt(0);
            Audio.PlaySound("scrollMenu", 0.4f);
        }
        for (int i = _feed.Count - 1; i >= 0; i--) { var f = _feed[i]; f.t -= dt; if (f.t <= 0f) _feed.RemoveAt(i); else _feed[i] = f; }
        if (PsychOnline.GameStartedPending) { Console.WriteLine("[GB] room match starting -> back to the room"); ExitToRoom(_lastInstalled); }
    }

    public override void Load()
    {
        Game.Discord?.SetMenuState("Mod Browser");
        Input.GameplayMode = false;
        if (!Audio.MusicPlaying) Audio.PlayMusic("music/freakyMenu", true);
        var s = Environment.GetEnvironmentVariable("FNF_GB_SEARCH");
        if (!string.IsNullOrEmpty(s)) _search = s;
        Refresh();
        if (_room && PsychOnline.Connected) PsychOnline.Send("status", "Browsing mods");
        if (_openModId > 0)
        {   // the room's mod page (lobby card "MOD PAGE")
            _detailOnly = true;
            _cur = new GameBanana.Mod { Id = _openModId, Name = "mod " + _openModId, Author = "?", Category = "" };
            _filesTask = GameBanana.FilesAsync(_openModId);
            Go(Page.Detail);
        }
        else if (_openLink != null) { _linkOnly = true; OpenLink(_openLink, _openLinkWhy); if (_page != Page.Link) _linkOnly = false; }
        if (int.TryParse(Environment.GetEnvironmentVariable("FNF_GB_MODID"), out int devId))
        {   // dev: open one mod page directly (external-host tests)
            _devOpened = true;
            _cur = new GameBanana.Mod { Id = devId, Name = "mod " + devId, Author = "?", Category = "" };
            _files = null; _filesError = null; _fileSel = 0;
            _filesTask = GameBanana.FilesAsync(devId);
            Go(Page.Detail);
        }
    }

    public override void Unload()
    {
        foreach (var t in _thumbs.Values) t?.Dispose();
        _thumbs.Clear();
        _job?.Cancel.Cancel();
    }

    private void SetStatus(string s) { _status = s; _statusT = 6f; Console.WriteLine("[GB] " + s); }

    private void Refresh()
    {
        _gen++; _mods.Clear(); _nextPage = 1; _total = 0; _sel = 0; _error = null; _listTask = null; _scrollY = _scrollTarget = 0;
        LoadMore();
    }

    private void LoadMore()
    {
        if (_listTask != null || (_total > 0 && _mods.Count >= _total)) return;
        int page = _nextPage;
        _listTask = _search != null ? GameBanana.SearchAsync(_search, page) : GameBanana.ListAsync(_tab, _sort, page);
    }

    // ------------------------------------------------------------------ update
    private const int Cols = 3;
    private const float CardW = 352f, CardH = 222f, GapX = 38f, RowStep = 244f, GridTop = 176f, GridBottom = 610f;
    private (float x, float y) CardPos(int i)
    {
        float x0 = (FunkUI.W - (Cols * CardW + (Cols - 1) * GapX)) / 2f;
        return (x0 + (i % Cols) * (CardW + GapX), GridTop + (i / Cols) * RowStep - _scrollY);
    }

    public override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _time += dt; _pageTime += dt; _selTime += dt; if (_statusT > 0) _statusT -= dt;
        if (_room) { UpdateRoom(dt); if (Game.Scenes.CurrentScene != this) return; }
        if (_sel != _lastSel) { _lastSel = _sel; _selTime = 0f; }
        PumpThumbs();
        int gen = _gen;
        if (_listTask != null && _listTask.IsCompleted)
        {
            var t = _listTask; _listTask = null;
            if (t.Status == TaskStatus.RanToCompletion)
            {
                var (mods, total) = t.Result;
                foreach (var m in mods) if (!_mods.Any(x => x.Id == m.Id)) _mods.Add(m);
                _total = mods.Count == 0 ? _mods.Count : total; _nextPage++; _error = null;
                if (_mods.Count == 0) _error = _search != null ? "Nothing found for \"" + _search + "\"." : "No mods in this category.";
            }
            else _error = "Couldn't reach GameBanana: " + (t.Exception?.InnerException?.Message ?? "error") + "  -  press " + (ButtonPrompts.UseController(Input) ? "A" : "ENTER") + " to retry.";
        }
        switch (_page)
        {
            case Page.Grid: UpdateGrid(dt); break;
            case Page.Detail: UpdateDetail(dt); break;
            case Page.Installing: UpdateInstalling(dt); break;
            case Page.Search: UpdateSearch(); break;
            case Page.Link: if (Input.BackPressed || Input.ConfirmPressed || Input.MouseLeftPressed) { Audio.PlaySound("cancelMenu"); if (_linkOnly) { ExitToRoom(null); return; } Go(_linkBack); } break;
        }
        _scrollY = FunkUI.Approach(_scrollY, _scrollTarget, 14f, dt);
    }

    private void Go(Page p) { _page = p; _pageTime = 0f; _selTime = 10f; }

    private void UpdateGrid(float dt)
    {
        int n = _mods.Count;
        // tabs / sort / search
        if (Input.IsPressed(Keys.Q) || Input.IsGamePadPressed(Buttons.LeftShoulder)) { _search = null; _tab = (_tab + GameBanana.Tabs.Length - 1) % GameBanana.Tabs.Length; Audio.PlaySound("scrollMenu"); Refresh(); return; }
        if (Input.IsPressed(Keys.E) || Input.IsGamePadPressed(Buttons.RightShoulder)) { _search = null; _tab = (_tab + 1) % GameBanana.Tabs.Length; Audio.PlaySound("scrollMenu"); Refresh(); return; }
        if (Input.IsPressed(Keys.S) || Input.IsGamePadPressed(Buttons.X)) { _sort = (_sort + 1) % GameBanana.Sorts.Length; if (_search == null) Refresh(); Audio.PlaySound("scrollMenu"); return; }
        if (Input.IsPressed(Keys.F) || Input.IsGamePadPressed(Buttons.Y)) { _editBuf = _search ?? ""; _editChar = 0; Go(Page.Search); Audio.PlaySound("confirmMenu"); return; }
        if (Input.BackPressed)
        {
            Audio.PlaySound("cancelMenu");
            if (_search != null) { _search = null; Refresh(); return; }
            ExitToRoom(_lastInstalled); return;
        }
        if (n == 0)
        {
            if (_error != null && Input.ConfirmPressed && _listTask == null) { Audio.PlaySound("confirmMenu"); Refresh(); }
            return;
        }
        int sel = _sel;
        if (Input.LeftPressed) sel = Math.Max(0, sel - 1);
        if (Input.RightPressed) sel = Math.Min(n - 1, sel + 1);
        if (Input.UpPressed) sel = sel - Cols >= 0 ? sel - Cols : sel;
        if (Input.DownPressed) sel = Math.Min(n - 1, sel + Cols);
        // mouse
        var mp = DevNoMouse ? new Vector2(-1, -1) : MouseVirtual;
        bool moved = _lastMouse.X >= 0 && (mp - _lastMouse).LengthSquared() > 0.5f; _lastMouse = mp;
        int hit = -1;
        if (mp.Y > GridTop - 20f && mp.Y < GridBottom + 20f)
            for (int i = 0; i < n; i++) { var (x, y) = CardPos(i); if (mp.X >= x && mp.X <= x + CardW && mp.Y >= y && mp.Y <= y + CardH) hit = i; }
        if (moved && hit >= 0) sel = hit;
        if (Input.ScrollDelta != 0) sel = Math.Clamp(sel + (Input.ScrollDelta > 0 ? -Cols : Cols), 0, n - 1);
        if (sel != _sel) { _sel = sel; Audio.PlaySound("scrollMenu"); }
        // follow the selection; load the next page near the end
        int row = _sel / Cols, rows = (n + Cols - 1) / Cols;
        _scrollTarget = Math.Max(0f, (row - 1) * RowStep);   // two rows on screen, the selection in the lower one
        _scrollTarget = Math.Min(_scrollTarget, Math.Max(0f, (rows - 2) * RowStep));
        if (row >= rows - 2) LoadMore();
        string dev = Environment.GetEnvironmentVariable("FNF_GB_OPEN");
        if (!_devOpened && dev != null && int.TryParse(dev, out int di) && n > di && _pageTime > 1.5f) { _devOpened = true; _sel = di; OpenDetail(); return; }
        if (Input.ConfirmPressed || (Input.MouseLeftPressed && hit >= 0)) { if (hit >= 0 && Input.MouseLeftPressed) _sel = hit; Audio.PlaySound("confirmMenu"); OpenDetail(); }
    }

    private void OpenDetail()
    {
        _cur = _mods[Math.Clamp(_sel, 0, _mods.Count - 1)];
        _files = null; _filesError = null; _fileSel = 0;
        _filesTask = GameBanana.FilesAsync(_cur.Id);
        Go(Page.Detail);
    }

    private List<FunkMenu.Row> FileRows()
    {
        var l = new List<FunkMenu.Row>();
        if (_files == null) return l;
        foreach (var f in _files)
        {
            if (f.External != null) { l.Add(ExternalRow(f)); continue; }
            string ext = f.Ext.TrimStart('.').ToUpperInvariant();
            l.Add(new FunkMenu.Row
            {
                Label = f.Name ?? "?", Value = GameBanana.Size(f.Size) + "   " + GameBanana.Count(f.Downloads) + " DLS",
                Badge = f.Supported ? (f.HasExe ? ext + " + EXE" : ext) : ext + ": NOT SUPPORTED",
                BadgeColor = f.Supported ? (f.HasExe ? FunkUI.Orange : FunkUI.Green) : FunkUI.Red, Disabled = !f.Supported,
                Art = f.Supported ? "@d_arrow" : "@d_bang",
                Desc = f.Supported ? (f.HasExe ? "Full game build: only its assets / mods folders are kept. " : "") + (string.IsNullOrWhiteSpace(f.Description) ? "Download and install this file." : f.Description)
                                   : "Only ZIP, 7Z and RAR archives can be installed here.",
            });
        }
        return l;
    }

    private FunkMenu.Row ExternalRow(GameBanana.FileInfo f)
    {
        var x = f.External;
        string host = x.HostLabel;
        bool pc = BrowserOnPc;
        return new FunkMenu.Row
        {
            Label = f.Name ?? host, Value = f.Size > 0 ? GameBanana.Size(f.Size) + "   EXTERNAL" : "NOT ON GAMEBANANA",
            Badge = x.NeedsBrowser ? (pc ? host + ": OPEN IN BROWSER" : host + ": SHOW LINK") : host,
            BadgeColor = x.NeedsBrowser ? FunkUI.Orange : FunkUI.Cyan,
            Art = x.NeedsBrowser ? "@d_bang" : "@d_arrow",
            Desc = x.NeedsBrowser
                ? HostWhy(x) + (pc ? " - opens it in your web browser." : " - shows the address + a QR code for your phone.")
                : "Hosted on " + host.ToLowerInvariant() + " (not GameBanana): downloaded and installed here." + (string.IsNullOrWhiteSpace(f.Description) ? "" : "  \"" + f.Description + "\""),
        };
    }

    private static string HostWhy(ExternalDownloads.Source x)
    {
        string h = ExternalDownloads.HostOf(x.Url);
        if (h.Contains("mega")) return "MEGA needs its own web app to decrypt files";
        if (h.Contains("youtu")) return "A YouTube video, not a file";
        if (h.Contains("mediafire") || h.Contains("drive.google")) return "A shared folder, not a single file";
        return "Hosted on " + h + ", which needs a web browser";
    }

    private static bool BrowserOnPc
    {
        get
        {
#if XBOX_UWP || ANDROID
            return false;
#else
            return !ForceQr;
#endif
        }
    }

    /// <summary>PC: system browser + toast. Xbox / Android (or FNF_GB_FORCEQR=1): link page with the URL and a QR code.</summary>
    private void OpenLink(string url, string why)
    {
        Console.WriteLine("[GB] open link: " + url);
        if (BrowserOnPc && ExternalDownloads.TryOpenBrowser(url)) { SetStatus("Opened " + ExternalDownloads.HostOf(url) + " in your web browser"); return; }
        _linkUrl = url; _linkWhy = why; _qr = QrCode.Encode(url); _linkBack = _page == Page.Installing ? Page.Detail : _page;
        if (_page == Page.Installing) _job = null;
        Go(Page.Link);
    }

    private void UpdateDetail(float dt)
    {
        if (_filesTask != null && _filesTask.IsCompleted)
        {
            if (_filesTask.Status == TaskStatus.RanToCompletion) { _files = _filesTask.Result.files; if (_cur.Name == "mod " + _cur.Id && GameBanana.LastName != null) _cur.Name = GameBanana.LastName; if (_files.Count == 0) _filesError = "This mod has no downloadable files (external links only)."; }
            else _filesError = "Couldn't load the files: " + (_filesTask.Exception?.InnerException?.Message ?? "error");
            _filesTask = null;
            _fileSel = Math.Max(0, _files?.FindIndex(f => f.Supported) ?? 0);
        }
        if (Input.BackPressed) { Audio.PlaySound("cancelMenu"); if (_detailOnly) { ExitToRoom(_lastInstalled); return; } Go(Page.Grid); return; }
        var rows = FileRows();
        if (rows.Count > 0)
        {
            if (Input.UpPressed) { _fileSel = (_fileSel + rows.Count - 1) % rows.Count; Audio.PlaySound("scrollMenu"); }
            if (Input.DownPressed) { _fileSel = (_fileSel + 1) % rows.Count; Audio.PlaySound("scrollMenu"); }
            var mp = DevNoMouse ? new Vector2(-1, -1) : MouseVirtual;
            bool moved = _lastMouse.X >= 0 && (mp - _lastMouse).LengthSquared() > 0.5f; _lastMouse = mp;
            int hit = _fmFiles.Hit(mp, rows);
            if (moved && hit >= 0 && hit != _fileSel) { _fileSel = hit; Audio.PlaySound("scrollMenu"); }
            bool devInst = !_devInstalled && Environment.GetEnvironmentVariable("FNF_GB_INSTALL") == "1" && _pageTime > 1.5f;
            if (devInst && int.TryParse(Environment.GetEnvironmentVariable("FNF_GB_FILE"), out int df) && df >= 0 && df < rows.Count) _fileSel = df;
            if (Input.ConfirmPressed || (Input.MouseLeftPressed && hit >= 0) || devInst)
            {
                _devInstalled = true;
                if (hit >= 0 && Input.MouseLeftPressed) _fileSel = hit;
                var f = _files[_fileSel];
                if (f.NeedsBrowser) { Audio.PlaySound("confirmMenu"); OpenLink(f.External.Url, HostWhy(f.External)); return; }
                if (!f.Supported) { Audio.PlaySound("cancelMenu"); SetStatus(f.Ext + " archives can't be installed here (ZIP / 7Z / RAR only)"); return; }
                Audio.PlaySound("confirmMenu");
                _job = GameBanana.Install(_cur, f); _jobDoneT = 0f;
                Go(Page.Installing);
            }
        }
        else if (_filesError != null && Input.ConfirmPressed) { _filesTask = GameBanana.FilesAsync(_cur.Id); _filesError = null; }
    }

    private void UpdateInstalling(float dt)
    {
        if (_job == null) { Go(Page.Detail); return; }
        if (!_job.Done) { if (Input.BackPressed) { _job.Cancel.Cancel(); Audio.PlaySound("cancelMenu"); } return; }
        bool first = _jobDoneT == 0f;
        _jobDoneT += dt;
        if (first)
        {
            if (_job.Error != null) { SetStatus("Install failed: " + _job.Error); Audio.PlaySound("cancelMenu"); }
            else { SetStatus("Installed \"" + _job.Result + "\" (" + _job.Layout + " mod) and turned it on!"); Audio.PlaySound("confirmMenu"); _installed = GameBanana.InstalledUrls(); _lastInstalled = _job.Result; }
        }
        if (_room && _job.Error == null)
        {   // room mode: A = back to the room's song list at this mod, B = keep browsing
            bool devBack = Environment.GetEnvironmentVariable("FNF_PSO_PICKNEW") != null && _jobDoneT > 1.5f;
            if (Input.ConfirmPressed || devBack) { Audio.PlaySound("confirmMenu"); ExitToRoom(_job.Result); return; }
            if (Input.BackPressed) { Audio.PlaySound("cancelMenu"); _job = null; Go(Page.Detail); }
            return;
        }
        bool devLink = _job.BrowserUrl != null && Environment.GetEnvironmentVariable("FNF_GB_INSTALL") == "1" && _jobDoneT > 1.5f && _linkUrl == null;
        if (_job.BrowserUrl != null && (Input.ConfirmPressed || devLink)) { Audio.PlaySound("confirmMenu"); OpenLink(_job.BrowserUrl, _job.Error); return; }
        if (Input.ConfirmPressed || Input.BackPressed || Input.MouseLeftPressed) { Audio.PlaySound("cancelMenu"); _job = null; Go(Page.Detail); }
    }

    private void UpdateSearch()
    {
        var skb = SystemKeyboard.Poll(this, "Search GameBanana for", ref _editBuf, 60);
        if (skb == SystemKeyboard.State.Busy) return;
        bool skbOk = skb == SystemKeyboard.State.Done, skbCancel = skb == SystemKeyboard.State.Cancelled;
        bool up = Input.IsPressed(Keys.Up) || Input.IsGamePadPressed(Buttons.DPadUp), down = Input.IsPressed(Keys.Down) || Input.IsGamePadPressed(Buttons.DPadDown);
        if (up || down)
        {
            _editChar = (_editChar + (up ? 1 : EditChars.Length - 1)) % EditChars.Length;
            if (_editBuf.Length == 0) _editBuf = "a";
            _editBuf = _editBuf.Substring(0, _editBuf.Length - 1) + EditChars[_editChar];
        }
        if (Input.IsGamePadPressed(Buttons.DPadRight) || Input.IsPressed(Keys.Right)) _editBuf += EditChars[_editChar];
        if (Input.IsGamePadPressed(Buttons.DPadLeft) || Input.IsGamePadPressed(Buttons.X) || Input.IsPressed(Keys.Back) || Input.IsPressed(Keys.Left))
            if (_editBuf.Length > 0) _editBuf = _editBuf.Substring(0, _editBuf.Length - 1);
        var k = Input.GetAnyKeyPressed();
        if (k.HasValue)
        {
            var key = k.Value;
            if (key >= Keys.A && key <= Keys.Z) _editBuf += (char)('a' + (key - Keys.A));
            else if (key >= Keys.D0 && key <= Keys.D9) _editBuf += (char)('0' + (key - Keys.D0));
            else if (key == Keys.Space) _editBuf += ' ';
            else if (key == Keys.OemMinus) _editBuf += '-';
        }
        if (skbOk || Input.IsPressed(Keys.Enter) || Input.IsGamePadPressed(Buttons.A) || Input.IsGamePadPressed(Buttons.Start))
        {
            Audio.PlaySound("confirmMenu");
            _search = _editBuf.Trim().Length > 0 ? _editBuf.Trim() : null;
            Go(Page.Grid); Refresh();
        }
        else if (skbCancel || Input.IsPressed(Keys.Escape) || Input.IsGamePadPressed(Buttons.B)) { Audio.PlaySound("cancelMenu"); Go(Page.Grid); }
    }

    // ------------------------------------------------------------------ thumbnails
    private void WantThumb(string url)
    {
        if (string.IsNullOrEmpty(url) || _thumbs.ContainsKey(url) || _thumbPending.Contains(url) || _thumbRunning >= 4) return;
        _thumbPending.Add(url); Interlocked.Increment(ref _thumbRunning);
        Task.Run(async () =>
        {
            byte[] data = null;
            try { data = await GameBanana.GetBytesAsync(url).ConfigureAwait(false); } catch { }
            _thumbDone.Enqueue((url, data));
            Interlocked.Decrement(ref _thumbRunning);
        });
    }

    private void PumpThumbs()
    {
        for (int i = 0; i < 3 && _thumbDone.TryDequeue(out var t); i++)
        {
            _thumbPending.Remove(t.url);
            Texture2D tex = null;
            if (t.data != null) try { using var ms = new MemoryStream(t.data); tex = Texture2D.FromStream(Game.GraphicsDevice, ms); } catch { }
            _thumbs[t.url] = tex;
        }
    }

    private Texture2D Thumb(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (_thumbs.TryGetValue(url, out var t)) return t;
        WantThumb(url);
        return null;
    }

    // ------------------------------------------------------------------ drawing
    private static readonly Color[] TabCols = { FunkUI.Pink, FunkUI.Cyan, FunkUI.Purple, FunkUI.Orange };
    private Color Accent => _search != null ? FunkUI.Green : TabCols[_tab % TabCols.Length];

    private List<ButtonPrompts.Prompt> Prompts()
    {
        var l = new List<ButtonPrompts.Prompt>();
        switch (_page)
        {
            case Page.Grid:
                l.Add(new ButtonPrompts.Prompt(ButtonPrompts.DPadAll, "ARROWS", "PICK"));
                l.Add(ButtonPrompts.Confirm(_mods.Count == 0 && _error != null ? "RETRY" : "OPEN"));
                l.Add(new ButtonPrompts.Prompt(Buttons.X, "S", "SORT"));
                l.Add(new ButtonPrompts.Prompt(Buttons.Y, "F", "SEARCH"));
                l.Add(ButtonPrompts.Back(_search != null ? "CLEAR" : _room ? "BACK TO ROOM" : "BACK"));
                break;
            case Page.Detail:
                l.Add(ButtonPrompts.UpDown("FILE"));
                bool nb = _files != null && _fileSel < _files.Count && _files[_fileSel].NeedsBrowser;
                l.Add(ButtonPrompts.Confirm(nb ? (BrowserOnPc ? "OPEN" : "SHOW LINK") : "INSTALL")); l.Add(ButtonPrompts.Back("BACK"));
                break;
            case Page.Installing:
                if (_room && _job != null && _job.Done && _job.Error == null) { l.Add(ButtonPrompts.Confirm(PsychOnline.Connected ? "BACK TO THE ROOM" : "BACK")); l.Add(ButtonPrompts.Back("KEEP BROWSING")); }
                else if (_job != null && _job.Done && _job.BrowserUrl != null) { l.Add(ButtonPrompts.Confirm(BrowserOnPc ? "OPEN IN BROWSER" : "SHOW LINK")); l.Add(ButtonPrompts.Back("BACK")); }
                else l.Add(_job != null && _job.Done ? ButtonPrompts.Confirm("OK") : ButtonPrompts.Back("CANCEL"));
                break;
            case Page.Link: l.Add(ButtonPrompts.Back("DONE")); break;
            case Page.Search:
                l.Add(ButtonPrompts.UpDown("CHAR")); l.Add(new ButtonPrompts.Prompt(Buttons.DPadRight, "TYPE", "ADD"));
                l.Add(new ButtonPrompts.Prompt(Buttons.X, "BKSP", "DELETE")); l.Add(ButtonPrompts.Confirm("SEARCH")); l.Add(ButtonPrompts.Back("CANCEL"));
                break;
        }
        return l;
    }

    public override void Draw(SpriteBatch sb)
    {
        { ProDraw(sb); return; }   // in-house Funkin theme everywhere (ModBrowserScene.Pro.cs)   // in a Psych Online room: the flat ProUI look (ModBrowserScene.Pro.cs)
        _desat ??= LoadOpt("menus/main_menu/menuDesat.png");
        _fm.Update(_page, null, Accent, 1f / 60f);
        FunkUI.Begin(sb);
        _fm.DrawBackground(_desat);
        string tag = _search != null ? "SEARCH: " + FunkMenu.Ellipsize(_search.ToUpperInvariant(), 19f, 260f) : GameBanana.Sorts[_sort].Label;
        _fm.DrawTitle(_page == Page.Link ? "GET IT HERE" : _page == Page.Detail || _page == Page.Installing ? "MOD INFO" : "MOD BROWSER", "GAMEBANANA  -  " + tag, FunkUI.Yellow);
        switch (_page)
        {
            case Page.Grid: DrawTabs(); DrawGrid(); break;
            case Page.Search:
                _fm.DrawEditor("Search GameBanana for:", _editBuf, "Friday Night Funkin' mods with this name (e.g. tricky, pico, hex).", EditChars[_editChar].ToString());
                break;
            case Page.Detail: DrawDetail(); break;
            case Page.Installing: DrawInstalling(); break;
            case Page.Link: DrawLink(); break;
        }
        if (_room) DrawRoomOverlay();
        _fm.DrawToast(_status, _statusT);
        _fm.DrawPrompts(Input, Prompts());
        sb.End();
    }

    /// <summary>Room mode: small taped room tag (code, players, connection) top-right + the latest chat/notification notes.</summary>
    private void DrawRoomOverlay()
    {
        bool ok = PsychOnline.Connected;
        string code = PsychOnline.Room?.RoomId ?? "----";
        int n = ok ? PsychOnline.AllPlayers().Count() : 0;
        string t = _roomLost ? "ROOM LOST" : ok ? "ROOM " + code + "  -  " + n + " IN" : "RECONNECTING" + new string('.', 1 + (int)(_roomT * 3) % 3);
        float w = FunkUI.MeasureVcr(t, 17f) + 44f, x = FunkUI.W - FunkUI.SafeX - w - 6f, y = 112f;   // under the title's luggage tag
        FunkUI.Push(x + w / 2f, y + 16f, FunkUI.Rad(2.2f));
        FunkUI.ShadowBlock(x, y, w, 32f, _roomLost ? FunkUI.Red : ok ? FunkUI.Green : FunkUI.Orange, 3f, 4f, 5f);
        FunkUI.Disc(x + 16f, y + 16f, 6f, ok && (int)(_roomT * 2) % 2 == 0 ? FunkUI.Ink : FunkUI.A(FunkUI.Ink, 0.35f));   // heartbeat blink
        FunkUI.Text(t, x + 28f, y + 7f, 17f, FunkUI.Ink);
        FunkUI.Pop();
        FunkUI.Tape(x + w - 6f, y - 2f, 46f, FunkUI.Rad(35f), FunkUI.Pink, 0.85f);
        float fy = 520f;
        for (int i = _feed.Count - 1; i >= 0; i--)
        {
            var (text, ft) = _feed[i];
            float a = Math.Min(1f, ft / 0.6f), slap = FunkUI.EaseOutBack(Math.Min(1f, (7f - ft) / 0.2f), 2.4f);
            string s = FunkMenu.Ellipsize(text, 16f, 420f);
            float fw = FunkUI.MeasureVcr(s, 16f) + 26f, fx = FunkUI.W - FunkUI.SafeX - fw;
            FunkUI.Push(fx + fw / 2f, fy + 13f, FunkUI.Rad(i % 2 == 0 ? -1.4f : 1.1f), slap, slap);
            FunkUI.Block(fx, fy, fw, 26f, FunkUI.A(FunkUI.Paper, a), 3f, FunkUI.A(FunkUI.Ink, a));
            FunkUI.Text(s, fx + 13f, fy + 5f, 16f, FunkUI.A(FunkUI.Ink, a));
            FunkUI.Pop();
            fy -= 32f;
        }
    }

    private void DrawTabs()
    {
        float x = FunkUI.SafeX + 4f, y = 136f;
        bool pad = ButtonPrompts.UseController(Input);
        x += FunkUI.Glyph(pad, Buttons.LeftShoulder, "Q", x, y - 15f, 30f) + 10f;
        for (int i = 0; i < GameBanana.Tabs.Length; i++)
        {
            bool on = _search == null && i == _tab;
            string t = GameBanana.Tabs[i].Label;
            float w = FunkUI.MeasureVcr(t, 18f) + 30f, cx = x + w / 2f, cy = y - (on ? 4f : 0f);
            FunkUI.Push(cx, cy, FunkUI.Rad(i % 2 == 0 ? -2f : 1.6f) + (on ? FunkUI.Wiggle(_pageTime, 22f, 7f) * 0.05f : 0f));
            FunkUI.ShadowBlock(cx - w / 2f, cy - 16f, w, 32f, on ? TabCols[i] : Color.Lerp(FunkUI.Paper, Color.Gray, 0.35f), 4f, 4f, 5f);
            FunkUI.Text(t, cx, cy - 9f, 18f, FunkUI.Ink, 0.5f);
            FunkUI.Pop();
            x += w + 16f;
        }
        FunkUI.Glyph(pad, Buttons.RightShoulder, "E", x, y - 15f, 30f);
        string count = _total > 0 ? GameBanana.Count(_total) + " MODS" : _listTask != null ? "LOADING..." : "";
        if (count.Length > 0) FunkUI.Text(count, FunkUI.W - FunkUI.SafeX, y - 9f, 18f, Color.White, 1f, 2f);
    }

    private void DrawGrid()
    {
        int n = _mods.Count;
        if (n == 0)
        {
            if (_error != null) Note(_error, FunkUI.Yellow, 640f, 360f);
            else _fm.DrawLoading("ASKING GAMEBANANA", GameBanana.Tabs[_tab].Help);
            return;
        }
        float bop = FunkUI.Bop;
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < n; i++)
            {
                bool sel = i == _sel;
                if ((pass == 1) != sel) continue;
                var (x, y) = CardPos(i);
                if (y + CardH < GridTop - 60f || y > GridBottom + 40f) continue;
                float ea = y < GridTop - 30f ? FunkUI.Clamp01(1f - (GridTop - 30f - y) / 80f) : y + CardH > GridBottom + 30f ? FunkUI.Clamp01(1f - (y + CardH - GridBottom - 30f) / 80f) : 1f;
                if (ea <= 0.02f) continue;
                DrawCard(_mods[i], i, x, y, sel, ea, bop);
            }
        if (_listTask != null) FunkUI.Text("loading more...", FunkUI.W / 2f, GridBottom + 4f, 17f, Color.White, 0.5f, 2f);
    }

    private void DrawCard(GameBanana.Mod m, int i, float x, float y, bool sel, float ea, float bop)
    {
        float intro = FunkUI.EaseOutBack(FunkUI.Clamp01((_pageTime - 0.03f * (i % 6)) / 0.28f), 1.8f);
        if (intro <= 0f) return;
        float cx = x + CardW / 2f, cy = y + CardH / 2f + (1f - intro) * 80f - (sel ? 6f : 0f);
        float tilt = FunkUI.Rad((FunkUI.Hash(m.Id) - 0.5f) * 4f) + (sel ? FunkUI.Wiggle(_selTime, 24f, 7f) * 0.05f : 0f);
        var sq = sel ? FunkUI.Squash(_selTime, 0.12f) : Vector2.One;
        float s = (sel ? 1.04f + 0.015f * bop : 0.97f) * intro;
        FunkUI.Push(cx, cy, tilt, s * sq.X, s * sq.Y);
        float l = cx - CardW / 2f, t = cy - CardH / 2f;
        FunkUI.ShadowBlock(l, t, CardW, CardH, sel ? Accent : FunkUI.Paper, sel ? 5f : 4f, sel ? 10f : 6f, sel ? 11f : 7f, ea);
        // thumbnail (polaroid photo)
        float px = l + 12f, py = t + 12f, pw = CardW - 24f, ph = 142f;
        FunkUI.Fill(px, py, pw, ph, FunkUI.A(FunkUI.Ink, ea));
        var tex = Thumb(m.Thumb);
        if (tex != null)
        {
            float k = Math.Max(pw / tex.Width, ph / tex.Height);
            int sw = (int)Math.Min(tex.Width, pw / k), sh = (int)Math.Min(tex.Height, ph / k);
            var src = new Rectangle((tex.Width - sw) / 2, (tex.Height - sh) / 2, sw, sh);
            FunkUI.Quad(tex, px + pw / 2f, py + ph / 2f, pw, ph, 0f, new Color(Color.White, ea), src);
        }
        else FunkUI.Sticker("@d_note", px + pw / 2f, py + ph / 2f, 56f, MathF.Sin(_time * 3f + i) * 0.2f, 0.6f * ea);
        // text
        string name = FunkMenu.Ellipsize(m.Name, 18f, CardW - 28f);
        FunkUI.Text(name, l + 14f, t + 162f, 18f, FunkUI.A(FunkUI.Ink, ea));
        string by = FunkMenu.Ellipsize("by " + m.Author, 15f, CardW - 150f);
        FunkUI.Text(by, l + 14f, t + 189f, 15f, FunkUI.A(new Color(70, 60, 90), ea));
        string likes = GameBanana.Count(m.Likes);
        float lw = FunkUI.MeasureVcr(likes, 15f);
        FunkUI.Sticker("@d_heart", l + CardW - 30f - lw, t + 196f, 20f, 0f, ea, 1f, 1f, FunkUI.Pink);
        FunkUI.Text(likes, l + CardW - 14f, t + 189f, 15f, FunkUI.A(FunkUI.Ink, ea), 1f);
        if (_installed.Contains("https://gamebanana.com/mods/" + m.Id))
        {
            FunkUI.Push(l + CardW - 64f, t + 24f, FunkUI.Rad(8f));
            FunkUI.Block(l + CardW - 116f, t + 12f, 104f, 26f, FunkUI.Green, 3f, Color.Black, ea);
            FunkUI.Text("INSTALLED", l + CardW - 64f, t + 17f, 15f, FunkUI.A(FunkUI.Ink, ea), 0.5f);
            FunkUI.Pop();
        }
        FunkUI.Tape(cx, t - 4f, 84f, FunkUI.Rad((FunkUI.Hash(m.Id + 3) - 0.5f) * 14f), Color.White, 0.75f * ea);
        FunkUI.Pop();
    }

    private void Note(string text, Color c, float cx, float cy, float w = 620f)
    {
        var lines = FunkUI.Wrap(text, w - 50f, 20f);
        float h = 36f + lines.Count * 26f;
        FunkUI.Push(cx, cy, FunkUI.Rad(-2f) + FunkUI.Wiggle(_pageTime - 0.1f, 20f, 6f) * 0.05f);
        FunkUI.ShadowBlock(cx - w / 2f, cy - h / 2f, w, h, c, 4f, 8f, 9f);
        float ty = cy - h / 2f + 18f;
        foreach (var l in lines) { FunkUI.Text(l, cx, ty, 20f, FunkUI.Ink, 0.5f); ty += 26f; }
        FunkUI.Tape(cx - w / 2f + 20f, cy - h / 2f, 80f, FunkUI.Rad(-28f), FunkUI.Pink, 0.9f);
        FunkUI.Tape(cx + w / 2f - 20f, cy - h / 2f, 80f, FunkUI.Rad(28f), FunkUI.Pink, 0.9f);
        FunkUI.Pop();
    }

    private void DrawDetail()
    {
        if (_cur == null) return;
        float ia = FunkUI.EaseOutBack(FunkUI.Clamp01(_pageTime / 0.3f), 1.8f);
        // photo (left)
        float pw = 480f, ph = 270f, px = FunkUI.SafeX + 10f, py = 140f + (1f - ia) * 200f;
        FunkUI.Push(px + pw / 2f, py + ph / 2f, FunkUI.Rad(-2.5f));
        FunkUI.ShadowBlock(px - 12f, py - 12f, pw + 24f, ph + 24f, Color.White, 4f, 10f, 12f);
        FunkUI.Fill(px, py, pw, ph, FunkUI.Ink);
        var tex = Thumb(_cur.Thumb);
        if (tex != null)
        {
            float k = Math.Max(pw / tex.Width, ph / tex.Height);
            int sw = (int)Math.Min(tex.Width, pw / k), sh = (int)Math.Min(tex.Height, ph / k);
            FunkUI.Quad(tex, px + pw / 2f, py + ph / 2f, pw, ph, 0f, Color.White, new Rectangle((tex.Width - sw) / 2, (tex.Height - sh) / 2, sw, sh));
        }
        FunkUI.Tape(px + 10f, py - 6f, 90f, FunkUI.Rad(-30f), FunkUI.Yellow, 0.9f);
        FunkUI.Tape(px + pw - 10f, py - 6f, 90f, FunkUI.Rad(30f), FunkUI.Yellow, 0.9f);
        FunkUI.Pop();
        // info card (right)
        float ix = px + pw + 50f, iw = FunkUI.W - FunkUI.SafeX - ix, iy = 140f + (1f - ia) * 260f, ih = 270f;
        FunkUI.Push(ix + iw / 2f, iy + ih / 2f, FunkUI.Rad(1.4f));
        FunkUI.ShadowBlock(ix, iy, iw, ih, FunkUI.Paper, 5f, 10f, 12f);
        for (int k = 1; k < 9; k++) FunkUI.Fill(ix + 10f, iy + k * 30f, iw - 20f, 2f, new Color(120, 170, 255, 90));
        float ty = iy + 16f;
        foreach (var line in FunkUI.Wrap(_cur.Name, iw - 40f, 24f).Take(2)) { FunkUI.Text(line, ix + 20f, ty, 24f, FunkUI.Ink); ty += 30f; }
        FunkUI.Text("by " + FunkMenu.Ellipsize(_cur.Author, 18f, iw - 80f), ix + 20f, ty + 4f, 18f, new Color(70, 60, 90)); ty += 34f;
        FunkUI.Text(FunkMenu.Ellipsize(_cur.Category, 17f, iw - 40f), ix + 20f, ty, 17f, FunkUI.Ink); ty += 34f;
        StatSticker("@d_heart", GameBanana.Count(_cur.Likes) + " likes", ix + 20f, ty, FunkUI.Pink);
        StatSticker("@d_star", GameBanana.Count(_cur.Views) + " views", ix + 20f + iw / 2f - 10f, ty, FunkUI.Yellow); ty += 36f;
        string added = _cur.Added > 0 ? DateTimeOffset.FromUnixTimeSeconds(_cur.Added).ToString("yyyy-MM-dd") : "?";
        FunkUI.Text("added " + added + (string.IsNullOrWhiteSpace(_cur.Version) ? "" : "   v" + _cur.Version), ix + 20f, ty, 16f, new Color(70, 60, 90)); ty += 28f;
        if (_installed.Contains("https://gamebanana.com/mods/" + _cur.Id)) FunkUI.Text("already installed (installing again replaces it)", ix + 20f, ty, 15f, new Color(30, 130, 40));
        FunkUI.Pop();
        // files
        var rows = FileRows();
        if (rows.Count > 0)
        {
            _fmFiles.Sel = _fileSel;
            _fmFiles.Update(_cur.Id, rows, Accent, 1f / 60f);
            _fmFiles.DrawRows(rows, FunkUI.Yellow);
            if (_fileSel < rows.Count) { _fmFiles.DrawHelp(rows[_fileSel].Desc, 760f); }
        }
        else if (_filesError != null) Note(_filesError, FunkUI.Yellow, 640f, 520f, 760f);
        else FunkUI.Text("getting the file list" + new string('.', 1 + (int)(_time * 3) % 3), 640f, 500f, 20f, Color.White, 0.5f, 2f);
    }

    /// <summary>Link page: QR code taped on a paper card (left), the address written on a note (right).</summary>
    private void DrawLink()
    {
        float ia = FunkUI.EaseOutBack(FunkUI.Clamp01(_pageTime / 0.3f), 1.8f);
        float qs = 300f, qx = 166f, qy = 180f + (1f - ia) * 300f;
        FunkUI.Push(qx + qs / 2f, qy + qs / 2f, FunkUI.Rad(-3f));
        FunkUI.ShadowBlock(qx - 38f, qy - 38f, qs + 76f, qs + 76f, Color.White, 5f, 10f, 12f);   // >= 4 modules of quiet zone
        if (_qr != null)
        {
            float m = qs / _qr.Size;
            for (int y = 0; y < _qr.Size; y++)
                for (int x = 0; x < _qr.Size; x++)
                    if (_qr.Modules[y, x]) FunkUI.Fill(qx + x * m, qy + y * m, m + 0.6f, m + 0.6f, Color.Black);
        }
        else FunkUI.Text("address too long for a QR code", qx + qs / 2f, qy + qs / 2f, 16f, FunkUI.Ink, 0.5f);
        FunkUI.Tape(qx - 6f, qy - 14f, 90f, FunkUI.Rad(-35f), FunkUI.Yellow, 0.9f);
        FunkUI.Tape(qx + qs + 6f, qy + qs + 14f, 90f, FunkUI.Rad(-35f), FunkUI.Yellow, 0.9f);
        FunkUI.Pop();
        float nx = 560f, nw = 600f, ny = 160f + (1f - ia) * 360f;
        var lines = HardWrap(_linkUrl ?? "", nw - 50f, 20f);
        var why = FunkUI.Wrap(_linkWhy ?? "", nw - 50f, 18f);
        float nh = 150f + lines.Count * 26f + why.Count * 23f;
        FunkUI.Push(nx + nw / 2f, ny + nh / 2f, FunkUI.Rad(1.5f));
        FunkUI.ShadowBlock(nx, ny, nw, nh, FunkUI.Paper, 5f, 10f, 12f);
        for (int k = 1; k * 26f < nh - 10f; k++) FunkUI.Fill(nx + 10f, ny + 14f + k * 26f, nw - 20f, 2f, new Color(120, 170, 255, 90));
        float ty = ny + 18f;
        FunkUI.Text("SCAN IT WITH YOUR PHONE", nx + 24f, ty, 24f, FunkUI.Ink); ty += 36f;
        FunkUI.Text("or type this address in a browser:", nx + 24f, ty, 17f, new Color(70, 60, 90)); ty += 30f;
        foreach (var l in lines) { FunkUI.Text(l, nx + 24f, ty, 20f, new Color(30, 60, 200)); ty += 26f; }
        ty += 12f;
        foreach (var l in why) { FunkUI.Text(l, nx + 24f, ty, 18f, new Color(170, 60, 40)); ty += 23f; }
        FunkUI.Pop();
        FunkUI.Sticker("@d_arrow", nx - 30f, qy + qs / 2f, 64f, FunkUI.Rad(180f) + FunkUI.Wiggle(_pageTime, 18f, 3f) * 0.2f, 1f, 1f, 1f, FunkUI.Pink);
        FunkUI.Text("then copy the mod folder into  mods/  (or install it on PC and copy it over).", 640f, 600f, 17f, Color.White, 0.5f, 2f);
    }

    /// <summary>Wraps text that has no spaces (URLs) by characters.</summary>
    private static List<string> HardWrap(string text, float maxW, float size)
    {
        var l = new List<string>();
        string cur = "";
        foreach (char c in text)
        {
            if (cur.Length > 0 && FunkUI.MeasureVcr(cur + c, size) > maxW) { l.Add(cur); cur = ""; }
            cur += c;
        }
        if (cur.Length > 0) l.Add(cur);
        return l;
    }

    private void StatSticker(string art, string text, float x, float y, Color c)
    {
        FunkUI.Sticker(art, x + 12f, y + 9f, 26f, FunkUI.Rad(-8f), 1f, 1f, 1f, c);
        FunkUI.Text(text, x + 32f, y, 18f, FunkUI.Ink);
    }

    private void DrawInstalling()
    {
        var j = _job;
        if (j == null) return;
        if (!j.Done) _fm.DrawLoading(j.Progress >= 0f ? "DOWNLOADING" : "INSTALLING", j.Status, Math.Max(0f, j.Progress));
        else if (j.Error != null && j.BrowserUrl != null)
            Note("Couldn't download " + _cur?.Name + " here:  " + j.Error + ".  " + (BrowserOnPc ? "Open the page in your web browser instead?" : "Get it with a phone / browser instead?"), FunkUI.Orange, 640f, 360f, 760f);
        else if (j.Error != null) Note("Couldn't install " + _cur?.Name + ":  " + j.Error, FunkUI.Red, 640f, 360f, 760f);
        else
        {
            float b = FunkUI.Bop;
            FunkUI.Sticker("bf2", 640f, 270f, 190f * (1f + 0.08f * b), FunkUI.Rad(-6f));
            string where = !_room ? "Find its songs in Freeplay / Story Mode!"
                : j.Layout == "V-Slice" ? "Heads up: V-Slice mods can't be played in Psych Online rooms."
                : PsychOnline.SongCountOfMod(j.Result) is int ns && ns > 0 ? ns + " new song" + (ns == 1 ? "" : "s") + " for the room - go back and pick one!"
                : "No Psych songs found in it for the room.";
            string nm = _cur != null && !string.IsNullOrEmpty(_cur.Name) && !_cur.Name.StartsWith("mod ") ? _cur.Name + "\" (mods/" + j.Result + ")" : j.Result + "\"";
            Note("Installed \"" + nm + " as a " + j.Layout + " mod and turned it on. " + where, FunkUI.Paper, 640f, 480f, 760f);
        }
    }

    private Texture2D LoadOpt(string path)
    {
        var t = Assets.LoadTexture(path);
        return t == null || t == Assets.Pixel ? null : t;
    }
}
