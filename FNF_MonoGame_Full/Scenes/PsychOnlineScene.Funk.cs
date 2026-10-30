using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Newtonsoft.Json.Linq;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Psych Online front page + sub pages in the hand-made FNF style (FunkUI / FunkMenu, same look as the main menu and the
/// multiplayer menus): three tilted posters (CREATE / JOIN / FIND) with game stickers, paper strips for server / name /
/// account, scrolling strip lists for public rooms, songs, chat presets and the account page, a notebook editor and
/// big letter tiles for the room code. Mouse: hover selects, click activates, wheel scrolls / changes values.
/// The lobby itself (Page.Room) keeps the Psych Online room look (PsychOnlineScene.Lobby.cs).
/// </summary>
public partial class PsychOnlineScene
{
    private readonly FunkMenu _fm = new FunkMenu();
    private int _posterCol;
    private float _posterSelTime = 10f; private int _posterLast = -1;
    private readonly float[] _posterHov = new float[3];
    private readonly System.Diagnostics.Stopwatch _posterClock = System.Diagnostics.Stopwatch.StartNew();
    private double _posterLastT;
    private Vector2 _lastMouseF = new(-1, -1);
    private static readonly bool DevNoMouseF = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FNF_SHOTS")) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FNF_DEVMOUSE")); // dev: FNF_DEVMOUSE tests the mouse path

    private bool InRoomNow => PsychOnline.Connected || PsychOnline.Reconnecting || PsychOnline.ReconnectTask != null;
    private bool OverLobby => InRoomNow && (_page == Page.Songs || _page == Page.Chat || (_page == Page.Text && _textField == 4));
    public override bool NativeResolution => _page != Page.Room && !OverLobby;

    private const float PosterW = 336f, PosterH = 226f, PosterGap = 40f, PosterTop = 158f;
    private (float x, float y, float w, float h) PosterRect(int i)
    {
        float x0 = (FunkUI.W - (3 * PosterW + 2 * PosterGap)) / 2f;
        return (x0 + i * (PosterW + PosterGap), PosterTop, PosterW, PosterH);
    }

    private static readonly Color CCreate = FunkUI.Pink, CJoin = FunkUI.Cyan, CFind = FunkUI.Yellow;

    private Color PageAccent => _page switch
    {
        Page.Rooms => FunkUI.Yellow,
        Page.Songs => FunkUI.Green,
        Page.Chat => FunkUI.Cyan,
        Page.Account => FunkUI.Orange,
        Page.Code => FunkUI.Cyan,
        Page.Connecting => FunkUI.Purple,
        _ => FunkUI.Pink,
    };

    /// <summary>Selected-strip colour that stands out on the page tint.</summary>
    public static Color RowAccent(Color bg) => bg == FunkUI.Yellow || bg == FunkUI.Orange || bg == FunkUI.Green ? FunkUI.Cyan : FunkUI.Yellow;

    private static string ServerShort(string s)
    {
        if (string.IsNullOrEmpty(s)) return "?";
        if (PsychOnline.OfficialServers.Contains(s)) return "OFFICIAL SERVER";
        int i = s.IndexOf("://", StringComparison.Ordinal);
        return (i >= 0 ? s.Substring(i + 3) : s).TrimEnd('/');
    }

    // ------------------------------------------------------------------ visual rows (1:1 with Rows())
    private List<FunkMenu.Row> VisualRows(List<(string label, string value)> rows)
    {
        var l = new List<FunkMenu.Row>();
        for (int i = 0; i < rows.Count; i++)
        {
            var (label, value) = rows[i];
            var r = new FunkMenu.Row { Label = label, Value = value };
            switch (_page)
            {
                case Page.Main:
                    if (label == "CREATE ROOM") r.Desc = "Open a new room on the selected server. You are the host: pick the song, stage and room settings.";
                    else if (label == "JOIN ROOM") { r.Value = _code; r.Desc = "Join a friend's room with its 4-letter code (PC players: CODE in the room copies it)."; }
                    else if (label == "FIND ROOMS") { r.Value = null; r.Desc = "Browse the public rooms on this server."; }
                    else if (label == "LEADERBOARD") { r.Value = null; r.Desc = "Top Funkin Network players and their profiles."; }
                    else if (label == "SERVER") { r.Kind = FunkMenu.Kind.Choice; r.Value = ServerShort(Server); r.Desc = "Left / Right switches the server, A types your own (self-hosted Funkin-Online-Server). " + Server; }
                    else if (label == "NAME") { r.Value = PsychOnline.DefaultNickname(); r.Art = "@pencil"; r.Desc = "Your nickname in rooms (3 to 14 characters)."; }
                    else if (label == "SKINS") { r.Value = PsychOnline.SkinLabel(PsychOnline.OwnSkin); r.Art = "@pencil"; r.Desc = "Pick the character you play as (and your note skin). Other players see your skin in rooms."; }
                    else if (label == "ACCOUNT") { r.Value = PsychOnline.LoggedIn ? PsychOnline.AuthName ?? "LOGGED IN" : "NOT LOGGED IN"; r.Art = "@key"; r.Desc = "Optional: log in to the Funkin Network with your e-mail (the server mails you a code)."; }
                    else if (label == "BACK") { r.Value = null; r.Art = "@back"; r.Color = FunkUI.Paper; r.Desc = "Back to the multiplayer menu."; }
                    break;
                case Page.Rooms:
                    if (label == "REFRESH") { r.Art = "@hourglass"; r.Value = _roomsTask != null ? "LOOKING..." : _rooms.Count + " ROOMS"; r.Desc = "Ask the server for the list of public rooms again."; }
                    else if (label == "BACK") { r.Art = "@back"; r.Color = FunkUI.Paper; r.Desc = "Back to the Psych Online menu."; }
                    else if (i < _rooms.Count)
                    {
                        var room = _rooms[i]; var md = room["metadata"] as JObject;
                        string nm = (string)md?["name"];
                        int ping = (int?)md?["ping"] ?? 0;
                        r.Label = string.IsNullOrWhiteSpace(nm) ? label : nm.ToUpperInvariant();
                        r.Value = $"{label}  {(int?)room["clients"] ?? 0}/{(int?)room["maxClients"] ?? 0}";
                        r.Badge = ping + " MS"; r.BadgeColor = ping < 120 ? FunkUI.Green : ping < 250 ? FunkUI.Yellow : FunkUI.Red;
                        r.Art = (i % 3) switch { 0 => "icon:bf", 1 => "icon:dad", _ => "icon:gf" };
                        r.Desc = "Join room " + label + (string.IsNullOrWhiteSpace(nm) ? "" : " (" + nm + ")") + ".";
                    }
                    break;
                case Page.Songs:
                {
                    var songs = PsychOnline.Songs();
                    if (i < songs.Count)
                    {
                        var e = songs[i];
                        if (i == _songSel) { r.Kind = FunkMenu.Kind.Choice; r.Value = e.Diffs[Math.Clamp(_diffSel, 0, e.Diffs.Count - 1)].ToUpperInvariant(); }
                        else r.Value = e.Diffs.Count == 1 ? e.Diffs[0].ToUpperInvariant() : e.Diffs.Count + " DIFFICULTIES";
                        r.Desc = "Pick " + e.Display + " for the room. Left / Right = difficulty." + (string.IsNullOrEmpty(e.ModDir) ? "" : " Mod: " + e.ModDir);
                        if (!string.IsNullOrEmpty(e.ModDir)) { r.Badge = "MOD"; r.BadgeColor = FunkUI.Purple; }
                    }
                    else if (label == "GET MORE SONGS") { r.Art = "@globe"; r.Color = FunkUI.Cyan; r.Value = "MOD BROWSER"; r.Desc = "Browse GameBanana and install any mod without leaving the room - its songs appear right here."; }
                    else { r.Art = "@back"; r.Color = FunkUI.Paper; r.Desc = "Back to the room."; }
                    break;
                }
                case Page.Chat:
                    if (label == "TYPE A MESSAGE") { r.Art = "@pencil"; r.Desc = "Write your own message (keyboard or the D-pad keyboard)."; }
                    else if (label == "BACK") { r.Art = "@back"; r.Color = FunkUI.Paper; r.Desc = "Back to the room."; }
                    else { r.Art = "@mic"; r.Desc = "Send \"" + label.ToLowerInvariant() + "\" to everyone in the room."; }
                    break;
                case Page.Account:
                    if (label == "E-MAIL") { r.Art = "@pencil"; r.Desc = "The e-mail of your Psych Online (Funkin Network) account."; }
                    else if (label == "SEND LOGIN CODE") { r.Value = null; r.Art = "@join"; r.Desc = "The server e-mails you a one-time code."; }
                    else if (label == "ENTER CODE") { r.Art = "@key"; r.Desc = "Type the code from the e-mail to log in."; }
                    else if (label == "LOG OUT") { r.Art = "@leave"; r.Desc = "Forget the saved login."; }
                    else if (label == "BACK") { r.Art = "@back"; r.Color = FunkUI.Paper; r.Desc = "Back to the Psych Online menu."; }
                    break;
            }
            l.Add(r);
        }
        return l;
    }

    // ------------------------------------------------------------------ input helpers (called from UpdateRest)
    /// <summary>Main page: posters (0..2) are a row (Left/Right), strips below (3..). Returns true when it consumed the input.</summary>
    private bool UpdateMainNav(int rowCount)
    {
        if (_page != Page.Main || true) return false;   // Pro layout: one vertical list
        bool onPoster = _sel <= 2;
        if (onPoster) _posterCol = _sel;
        if (Input.LeftPressed && onPoster) { _sel = (_sel + 2) % 3; Audio.PlaySound("scrollMenu"); return true; }
        if (Input.RightPressed && onPoster) { _sel = (_sel + 1) % 3; Audio.PlaySound("scrollMenu"); return true; }
        if (Input.DownPressed)
        {
            _sel = onPoster ? 3 : _sel + 1 >= rowCount ? _posterCol : _sel + 1;
            Audio.PlaySound("scrollMenu"); return true;
        }
        if (Input.UpPressed)
        {
            _sel = onPoster ? rowCount - 1 : _sel == 3 ? _posterCol : _sel - 1;
            Audio.PlaySound("scrollMenu"); return true;
        }
        return false;
    }

    /// <summary>Mouse: hover selects, click confirms, wheel = Left/Right on value rows. Returns lr from the wheel/click.</summary>
    private int UpdateMouse(List<(string label, string value)> rows, out bool clicked)
    {
        clicked = false;
        var mp = DevNoMouseF ? new Vector2(-1, -1) : MouseVirtual;
        bool moved = _lastMouseF.X >= 0 && (mp - _lastMouseF).LengthSquared() > 0.5f;
        _lastMouseF = mp;
        int hit = -1;
        if (_page is Page.Main or Page.Rooms or Page.Songs or Page.Chat or Page.Account) hit = _fm.Hit(mp, VisualRows(rows));
        if (hit < 0 || hit >= rows.Count) return 0;
        if (moved && hit != _sel) { _sel = hit; if (_page == Page.Songs) _songSel = hit; Audio.PlaySound("scrollMenu"); }
        bool valueRow = (_page == Page.Main && hit == 3) || (_page == Page.Songs && hit < PsychOnline.Songs().Count);
        if (Input.ScrollDelta != 0 && valueRow && hit == _sel) return Input.ScrollDelta > 0 ? 1 : -1;
        if (Input.ScrollDelta != 0 && !valueRow && _page != Page.Main)
        { _sel = Math.Clamp(_sel + (Input.ScrollDelta > 0 ? -1 : 1), 0, rows.Count - 1); if (_page == Page.Songs) _songSel = _sel; }
        if (Input.MouseLeftPressed) { _sel = hit; if (_page == Page.Songs) _songSel = hit; clicked = true; }
        return 0;
    }

    // ------------------------------------------------------------------ drawing
    private void DrawFunk(SpriteBatch sb)
    {
        _desat ??= LoadOpt("menus/main_menu/menuDesat.png");
        var rows = Rows();
        bool over = OverLobby;
        if (over) DrawLobby(sb, hud: false);
        var vrows = _page is Page.Main or Page.Rooms or Page.Songs or Page.Chat or Page.Account ? VisualRows(rows) : new List<FunkMenu.Row>();
        float dt = Math.Clamp((float)(_posterClock.Elapsed.TotalSeconds - _posterLastT), 0f, 0.1f); _posterLastT = _posterClock.Elapsed.TotalSeconds;
        ConfigureProList();
        _fm.Sel = _sel; _fm.Update(_page.ToString() + (_page == Page.Text ? _textField.ToString() : ""), vrows, PageAccent, dt);
        FunkUI.Begin(sb);
        ProDrawPages(sb, vrows);
        sb.End();
    }

    private Texture2D LoadOpt(string path)
    {
        var t = Assets.LoadTexture(path);
        return t == null || t == Assets.Pixel ? null : t;
    }
}
