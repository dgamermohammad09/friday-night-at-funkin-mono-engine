using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Freeplay: read-only Psych Online network leaderboard + song comments for the selected song/difficulty
/// (Engine/PsychOnlineBoards.cs; public server API, no login). A small taped strip under the NPS sticker shows when the
/// selection has a Psych Online chart; L / left-stick click opens a hand-made notebook page (FunkUI) with two taped tabs
/// (TOP SCORES / COMMENTS), ALL TIME / WEEKLY stamp, pages of 15. Freeplay is frozen while the page is open.
/// Dev hooks: FNF_PSOBOARD_OPEN=&lt;s since scene&gt; (auto-open), FNF_PSOBOARD_TAB=1 (comments), FNF_PSOBOARD_CAT=1 (weekly).
/// </summary>
public partial class FreeplayScene
{
    private sealed class BoardSel { public string Id, Reason, Song, Diff, Title, Icon; }
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, BoardSel> _boardIds = new();
    private string _boardKey; private BoardSel _boardSel; private float _boardStripT;
    private bool _boardOpen; private float _boardT, _boardTabT, _boardSelT;
    private int _boardTab, _boardCat, _boardPage, _boardScroll;
    private Task<List<PsychOnlineBoards.TopScore>> _boardTopTask;
    private Task<List<PsychOnlineBoards.Comment>> _boardCmtTask;
    private List<PsychOnlineBoards.TopScore> _boardTop; private List<PsychOnlineBoards.Comment> _boardCmts;
    private string _boardErr; private bool _boardDevOpened;
    private const int BoardRows = 9;

    /// <summary>Called first in Update: tracks the selection; returns true while the board page is open (Freeplay frozen).</summary>
    private bool OnlineBoardUpdate(float dt)
    {
        _boardStripT += dt;
        if (!_boardOpen)
        {
            TrackBoardSelection();
            string dev = Environment.GetEnvironmentVariable("FNF_PSOBOARD_OPEN");
            bool devOpen = !_boardDevOpened && dev != null && float.TryParse(dev, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float at) && DevSceneElapsed >= at && _boardSel?.Id != null;
            if (_boardSel?.Id != null && _boardStripT > 0.3f && (devOpen || Input.IsPressed(Keys.L) || Input.IsGamePadPressed(Buttons.LeftStick)))
            {
                _boardDevOpened = true;
                if (devOpen) { int.TryParse(Environment.GetEnvironmentVariable("FNF_PSOBOARD_TAB") ?? "0", out _boardTab); int.TryParse(Environment.GetEnvironmentVariable("FNF_PSOBOARD_CAT") ?? "0", out _boardCat); }
                _boardOpen = true; _boardT = 0f; _boardTabT = 0f; _boardPage = 0; _boardScroll = 0;
                _boardTop = null; _boardCmts = null; _boardErr = null; _boardTopTask = null; _boardCmtTask = null;
                Audio.PlaySound("confirmMenu");
                BoardFetch();
                return true;
            }
            return false;
        }
        _boardT += dt; _boardTabT += dt; _boardSelT += dt; DevSceneElapsed += dt; DevSceneElapsedLocal += dt;
        if (_boardTopTask != null && _boardTopTask.IsCompleted)
        {
            if (_boardTopTask.Status == TaskStatus.RanToCompletion) _boardTop = _boardTopTask.Result;
            else _boardErr = "Couldn't reach the Psych Online server: " + (_boardTopTask.Exception?.InnerException?.Message ?? "error");
            Console.WriteLine($"[PSOBOARD] top {_boardSel?.Id} page {_boardPage} cat {_boardCat}: {(_boardTop != null ? _boardTop.Count + " scores" : _boardErr)}");
            _boardTopTask = null;
        }
        if (_boardCmtTask != null && _boardCmtTask.IsCompleted)
        {
            if (_boardCmtTask.Status == TaskStatus.RanToCompletion) _boardCmts = _boardCmtTask.Result;
            else _boardErr = "Couldn't reach the Psych Online server: " + (_boardCmtTask.Exception?.InnerException?.Message ?? "error");
            Console.WriteLine($"[PSOBOARD] comments {_boardSel?.Id}: {(_boardCmts != null ? _boardCmts.Count + " comments" : _boardErr)}");
            _boardCmtTask = null;
        }
        if (Input.BackPressed || Input.IsPressed(Keys.L) || Input.IsGamePadPressed(Buttons.LeftStick)) { Audio.PlaySound("cancelMenu"); _boardOpen = false; _boardStripT = 0.31f; return true; }
        bool tabL = Input.IsPressed(Keys.Q) || Input.IsGamePadPressed(Buttons.LeftShoulder), tabR = Input.IsPressed(Keys.E) || Input.IsGamePadPressed(Buttons.RightShoulder);
        if (tabL || tabR) { _boardTab ^= 1; _boardTabT = 0f; _boardScroll = 0; Audio.PlaySound("scrollMenu"); BoardFetch(); return true; }
        if (_boardTab == 0 && (Input.IsPressed(Keys.Tab) || Input.IsPressed(Keys.W) || Input.IsGamePadPressed(Buttons.X)))
        { _boardCat ^= 1; _boardPage = 0; _boardScroll = 0; _boardTop = null; _boardTabT = 0f; Audio.PlaySound("scrollMenu"); BoardFetch(true); return true; }
        int count = _boardTab == 0 ? _boardTop?.Count ?? 0 : _boardCmts?.Count ?? 0;
        int maxScroll = Math.Max(0, count - BoardRows);
        int sc = _boardScroll;
        if (Input.UpPressed) sc--;
        if (Input.DownPressed) sc++;
        if (Input.ScrollDelta != 0) sc += Input.ScrollDelta > 0 ? -3 : 3;
        sc = Math.Clamp(sc, 0, maxScroll);
        if (sc != _boardScroll) { _boardScroll = sc; _boardSelT = 0f; Audio.PlaySound("scrollMenu"); }
        if (_boardTab == 0 && _boardTopTask == null)
        {   // pages of 15 (server page size)
            if (Input.RightPressed && (_boardTop?.Count ?? 0) >= 15) { _boardPage++; _boardScroll = 0; _boardTop = null; Audio.PlaySound("scrollMenu"); BoardFetch(true); }
            else if (Input.LeftPressed && _boardPage > 0) { _boardPage--; _boardScroll = 0; _boardTop = null; Audio.PlaySound("scrollMenu"); BoardFetch(true); }
        }
        if (_boardErr != null && Input.ConfirmPressed) { _boardErr = null; Audio.PlaySound("confirmMenu"); BoardFetch(true); }
        return true;
    }

    private void BoardFetch(bool force = false)
    {
        var id = _boardSel?.Id; if (id == null) return;
        if (_boardTab == 0 && (force || _boardTop == null) && _boardTopTask == null) { _boardErr = null; _boardTopTask = PsychOnlineBoards.TopAsync(id, _boardPage, _boardCat); }
        if (_boardTab == 1 && (force || _boardCmts == null) && _boardCmtTask == null) { _boardErr = null; _boardCmtTask = PsychOnlineBoards.CommentsAsync(id); }
    }

    private void TrackBoardSelection()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _songs.Count) { _boardKey = null; _boardSel = null; return; }
        var song = _songs[_selectedIndex];
        if (song == null || song.IsRandom) { _boardKey = null; _boardSel = null; return; }
        string diff = GetCurrentDifficulty(song);
        string name = ResolveSongNameForSelection(song, diff);
        string key = $"{name}|{diff}|{song.ModPath}";
        if (key != _boardKey)
        {
            _boardKey = key; _boardStripT = 0f; _boardSel = null;
            string mod = song.ModPath, title = song.DisplayName ?? song.Name, icon = song.OpponentChar;
            if (!_boardIds.ContainsKey(key))
                Task.Run(() =>
                {
                    var bs = new BoardSel { Song = name, Diff = diff, Title = title, Icon = icon };
                    bs.Id = PsychOnlineBoards.SongId(name, diff, mod, out bs.Reason, out _);
                    _boardIds[key] = bs;
                });
        }
        if (_boardSel == null && _boardIds.TryGetValue(key, out var r)) { _boardSel = r; _boardStripT = 0f; if (r.Id != null) Console.WriteLine("[PSOBOARD] " + key + " -> " + r.Id); }
    }

    // ------------------------------------------------------------------ drawing
    /// <summary>Strip under the NPS / replay stickers (same hand-made style as FreeplayScene.Nps.cs).</summary>
    private void DrawOnlineBoardStrip(SpriteBatch sb)
    {
        if (_boardOpen || _boardSel?.Id == null) return;
        var font = Assets.GetFont(18);
        if (font == null) return;
        float pop = Math.Min(1f, _boardStripT / 0.18f);
        int w = 230, h = 34;
        int y0 = h + 6 + (_npsReplay != null ? 36 : 0);
        var m = Matrix.CreateTranslation(-w / 2f, -h / 2f, 0) * Matrix.CreateRotationZ(0.045f) * Matrix.CreateTranslation(1110, 228, 0);
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp, null, null, null, m);
        sb.Draw(Assets.Pixel, new Rectangle(31, y0 + 7, 184, 30), new Color(Color.Black, 0.5f * pop));
        sb.Draw(Assets.Pixel, new Rectangle(23, y0, 186, 32), new Color(Color.Black, pop));
        sb.Draw(Assets.Pixel, new Rectangle(26, y0 + 3, 180, 26), new Color(new Color(0xFF, 0xD6, 0x40), pop));
        sb.Draw(Assets.Pixel, new Rectangle(26, y0 + 3, 8, 26), new Color(new Color(0xFF, 0x5C, 0xA8), pop));
        bool pad = Input.LastDevice == InputManager.InputDevice.Controller;
        ButtonPrompts.DrawGlyph(sb, Assets, pad, Buttons.LeftStick, "L", 38, y0 + 4, 24, pop);
        font.DrawText(sb, "ONLINE TOP", new Vector2(70, y0 + 7), new Color(Color.Black, pop));
        sb.End();
    }

    private void DrawOnlineBoard(SpriteBatch sb)
    {
        if (!_boardOpen || _boardSel == null) return;
        FunkUI.Begin(sb);
        float a = FunkUI.Clamp01(_boardT / 0.15f);
        FunkUI.Fill(0, 0, FunkUI.W, FunkUI.H, new Color(Color.Black, 0.62f * a));
        float intro = FunkUI.EaseOutBack(FunkUI.Clamp01(_boardT / 0.32f), 1.6f);
        float px = 150f, py = 118f, pw = 980f, ph = 520f;
        float oy = (1f - intro) * 420f;
        // tabs (paper tags taped behind the page's top edge)
        string[] tabs = { "TOP SCORES", "COMMENTS" };
        Color[] tabCol = { FunkUI.Yellow, FunkUI.Pink };
        float tx = px + 40f;
        for (int i = 0; i < 2; i++)
        {
            bool on = i == _boardTab;
            float tw = FunkUI.MeasureVcr(tabs[i], 22f) + 44f, tcx = tx + tw / 2f, tcy = py - 18f + oy - (on ? 10f : 0f);
            FunkUI.Push(tcx, tcy, FunkUI.Rad(i == 0 ? -3f : 2.5f) + (on ? FunkUI.Wiggle(_boardTabT, 22f, 7f) * 0.06f : 0f));
            FunkUI.ShadowBlock(tcx - tw / 2f, tcy - 24f, tw, 50f, on ? tabCol[i] : Color.Lerp(FunkUI.Paper, Color.Gray, 0.4f), 4f, 5f, 6f);
            FunkUI.Text(tabs[i], tcx, tcy - 16f, 22f, FunkUI.Ink, 0.5f);
            FunkUI.Pop();
            tx += tw + 18f;
        }
        bool padNow = ButtonPrompts.UseController(Input);
        FunkUI.Glyph(padNow, Buttons.LeftShoulder, "Q", px + 4f, py - 46f + oy, 28f);
        FunkUI.Glyph(padNow, Buttons.RightShoulder, "E", tx, py - 46f + oy, 28f);
        // notebook page
        FunkUI.Push(px + pw / 2f, py + ph / 2f + oy, FunkUI.Rad(-1.2f));
        FunkUI.ShadowBlock(px, py + oy, pw, ph, FunkUI.Paper, 5f, 12f, 14f);
        for (int k = 0; k < 11; k++) FunkUI.Fill(px + 6f, py + oy + 118f + k * 40f, pw - 12f, 2f, new Color(120, 170, 255, 110));
        FunkUI.Fill(px + 92f, py + oy + 6f, 2f, ph - 12f, new Color(255, 90, 90, 150));
        // header: song title (Funkin bold), difficulty + network label
        string title = (_boardSel.Title ?? _boardSel.Song ?? "").ToUpperInvariant();
        float bs = Math.Min(0.62f, 640f / Math.Max(1f, FunkUI.BoldWidth(title, 1f)));
        FunkUI.Bold(title, px + 112f, py + oy + 18f, bs, 0f, _boardT);
        FunkUI.Text("PSYCH ONLINE NETWORK  -  " + PsychOnlineBoards.PsychDiff(_boardSel.Diff).ToUpperInvariant(), px + 114f, py + oy + 84f, 17f, new Color(80, 60, 110));
        if (!string.IsNullOrEmpty(_boardSel.Icon)) FunkUI.HealthIcon(_boardSel.Icon, px + 48f, py + oy + 52f, 86f, 0, FunkUI.Rad(-8f) + FunkUI.Bop * 0.04f);
        if (_boardTab == 0)
        {   // category stamp (marker box) + page
            string cat = PsychOnlineBoards.CategoryTitles[_boardCat];
            float cw = FunkUI.MeasureVcr(cat, 22f) + 30f, cx = px + pw - 40f - cw;
            FunkUI.Push(cx + cw / 2f, py + oy + 40f, FunkUI.Rad(6f) + FunkUI.Wiggle(_boardTabT, 20f, 6f) * 0.08f);
            FunkUI.MarkerRect(cx, py + oy + 22f, cw, 38f, 4f, _boardCat == 0 ? FunkUI.Red : FunkUI.Blue, 7 + _boardCat);
            FunkUI.Text(cat, cx + cw / 2f, py + oy + 30f, 22f, _boardCat == 0 ? FunkUI.Red : FunkUI.Blue, 0.5f);
            FunkUI.Pop();
            FunkUI.Text("PAGE " + (_boardPage + 1), px + pw - 40f, py + oy + 84f, 17f, new Color(80, 60, 110), 1f);
        }
        float ry = py + oy + 126f;
        string wait = null;
        if (_boardErr != null) BoardNote(_boardErr + "  (" + (padNow ? "A" : "ENTER") + " = retry)", px + pw / 2f, ry + 160f);
        else if (_boardTab == 0)
        {
            if (_boardTop == null) wait = "asking the leaderboard";
            else if (_boardTop.Count == 0) BoardNote(_boardPage > 0 ? "No more scores." : "Nobody has a score on this chart yet" + (_boardCat == 1 ? " this week." : "."), px + pw / 2f, ry + 160f);
            else
            {
                FunkUI.Text("#", px + 60f, ry - 2f, 15f, new Color(110, 90, 140), 0.5f);
                FunkUI.Text("PLAYER", px + 112f, ry - 2f, 15f, new Color(110, 90, 140));
                FunkUI.Text("SCORE", px + 610f, ry - 2f, 15f, new Color(110, 90, 140), 1f);
                FunkUI.Text("ACC", px + 730f, ry - 2f, 15f, new Color(110, 90, 140), 1f);
                FunkUI.Text("MISS", px + 830f, ry - 2f, 15f, new Color(110, 90, 140), 1f);
                ry += 20f;
                for (int i = 0; i < BoardRows && _boardScroll + i < _boardTop.Count; i++)
                {
                    var s = _boardTop[_boardScroll + i];
                    float y = ry + i * 40f;
                    float slide = FunkUI.EaseOutCubic(FunkUI.Clamp01((_boardT - 0.12f - i * 0.03f) / 0.25f));
                    if (slide <= 0f) continue;
                    float dx = (1f - slide) * 60f;
                    Color rc = s.Rank == 1 ? new Color(230, 160, 0) : s.Rank == 2 ? new Color(120, 130, 150) : s.Rank == 3 ? new Color(190, 100, 40) : FunkUI.Ink;
                    if (s.Rank <= 3) FunkUI.Marker(new[] { new Vector2(px + 40f + dx, y + 2f), new Vector2(px + 82f + dx, y + 4f), new Vector2(px + 80f + dx, y + 30f), new Vector2(px + 38f + dx, y + 28f) }, 3f, rc, s.Rank * 13, 2f, true);
                    FunkUI.Text(s.Rank.ToString(), px + 60f + dx, y + 4f, 22f, rc, 0.5f);
                    FunkUI.Text(FunkMenu.Ellipsize(PsychOnlineBoards.Clean(s.Player), 22f, 330f), px + 112f + dx, y + 4f, 22f, FunkUI.Ink);
                    FunkUI.Text(s.Score.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), px + 610f + dx, y + 4f, 22f, FunkUI.Ink, 1f);
                    FunkUI.Text(s.Accuracy.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "%", px + 730f + dx, y + 6f, 18f, new Color(60, 50, 80), 1f);
                    if (s.Misses == 0)
                    {
                        FunkUI.Push(px + 805f + dx, y + 16f, FunkUI.Rad(-7f));
                        FunkUI.Block(px + 782f + dx, y + 4f, 46f, 24f, FunkUI.Green, 3f, Color.Black);
                        FunkUI.Text("FC", px + 805f + dx, y + 8f, 16f, FunkUI.Ink, 0.5f);
                        FunkUI.Pop();
                    }
                    else FunkUI.Text(s.Misses.ToString(), px + 830f + dx, y + 4f, 22f, FunkUI.Red, 1f);
                    string ago = PsychOnlineBoards.Ago(s.Submitted) + (Math.Abs(s.PlaybackRate - 1) > 0.001 ? "  " + s.PlaybackRate.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "X" : "");
                    FunkUI.Text(ago, px + pw - 24f + dx, y + 8f, 14f, new Color(110, 90, 140), 1f);
                }
            }
        }
        else
        {
            if (FNF_MonoGame.Engine.PsychEngine.ClientPrefs.Data.DisableSongComments) BoardNote("Song comments are turned off (Options > Psych Online > Disable Song Comments).", px + pw / 2f, ry + 160f);
            else if (_boardCmts == null) wait = "reading the comments";
            else if (_boardCmts.Count == 0) BoardNote("No comments on this chart yet.", px + pw / 2f, ry + 160f);
            else
            {
                FunkUI.Text(_boardCmts.Count + " COMMENTS  (shown during the song at their time)", px + 112f, ry - 2f, 15f, new Color(110, 90, 140));
                ry += 20f;
                for (int i = 0; i < BoardRows && _boardScroll + i < _boardCmts.Count; i++)
                {
                    var c = _boardCmts[_boardScroll + i];
                    float y = ry + i * 40f;
                    float slide = FunkUI.EaseOutCubic(FunkUI.Clamp01((_boardT - 0.12f - i * 0.03f) / 0.25f));
                    if (slide <= 0f) continue;
                    float dx = (1f - slide) * 60f;
                    FunkUI.Text(PsychOnlineBoards.SongTime(c.At), px + 82f + dx, y + 8f, 16f, new Color(200, 60, 60), 1f);
                    string who = FunkMenu.Ellipsize(PsychOnlineBoards.Clean(c.Player), 20f, 220f) + ":";
                    float ww = FunkUI.Text(who, px + 112f + dx, y + 4f, 20f, new Color(90, 40, 140));
                    FunkUI.Text(FunkMenu.Ellipsize(c.Content, 20f, pw - 150f - ww - 100f), px + 124f + ww + dx, y + 4f, 20f, FunkUI.Ink);
                    FunkUI.Text(PsychOnlineBoards.Ago(c.Submitted), px + pw - 24f + dx, y + 8f, 14f, new Color(110, 90, 140), 1f);
                }
            }
        }
        if (wait != null) FunkUI.Text(wait + new string('.', 1 + (int)(_boardT * 3) % 3), px + pw / 2f, ry + 150f, 22f, new Color(80, 60, 110), 0.5f);
        int total = _boardTab == 0 ? _boardTop?.Count ?? 0 : _boardCmts?.Count ?? 0;
        if (total > BoardRows)
        {   // scroll marker in the margin
            float t0 = py + oy + 150f, th = 360f, k = BoardRows / (float)total;
            FunkUI.Fill(px + pw - 14f, t0, 4f, th, new Color(0, 0, 0, 40));
            FunkUI.Fill(px + pw - 16f, t0 + th * (_boardScroll / (float)total), 8f, Math.Max(18f, th * k), FunkUI.Ink);
        }
        FunkUI.Tape(px + 30f, py + oy + 4f, 96f, FunkUI.Rad(-32f), FunkUI.Cyan, 0.85f);
        FunkUI.Tape(px + pw - 30f, py + oy + ph - 4f, 96f, FunkUI.Rad(-32f), FunkUI.Cyan, 0.85f);
        FunkUI.Pop();
        // prompts
        var pr = new List<ButtonPrompts.Prompt> { ButtonPrompts.UpDown("SCROLL") };
        if (_boardTab == 0) { pr.Add(ButtonPrompts.LeftRight("PAGE")); pr.Add(new ButtonPrompts.Prompt(Buttons.X, "TAB", _boardCat == 0 ? "WEEKLY" : "ALL TIME")); }
        pr.Add(new ButtonPrompts.Prompt(Buttons.LeftShoulder, "Q", "TAB"));
        pr.Add(ButtonPrompts.Back("CLOSE"));
        FunkUI.PromptTape(Input, pr, FunkUI.W - FunkUI.SafeX, 668f + (1f - intro) * 80f);
        sb.End();
    }

    private void BoardNote(string text, float cx, float cy)
    {
        var lines = FunkUI.Wrap(text, 560f, 20f);
        float w = 620f, h = 36f + lines.Count * 26f;
        FunkUI.Push(cx, cy, FunkUI.Rad(2f));
        FunkUI.ShadowBlock(cx - w / 2f, cy - h / 2f, w, h, FunkUI.Yellow, 4f, 7f, 8f);
        float ty = cy - h / 2f + 18f;
        foreach (var l in lines) { FunkUI.Text(l, cx, ty, 20f, FunkUI.Ink, 0.5f); ty += 26f; }
        FunkUI.Tape(cx, cy - h / 2f, 80f, FunkUI.Rad(-4f), FunkUI.Pink, 0.9f);
        FunkUI.Pop();
    }
}
