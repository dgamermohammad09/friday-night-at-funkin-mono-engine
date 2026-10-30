using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.Colyseus;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Psych Online versus results (online/states/ResultsState.hx): every player of the room with Accuracy / Score + FP / Max
/// Combo / Sick / Good / Bad / Shit / Misses, the winner by the room's win condition (accuracy, score, misses, FP, combo),
/// team averages in Team Mode ("- BEST SOLO -"), ties, and the WINNER / LOSER / TIE judge art (onlineJudges atlas).
/// Numbers come live from the room state (late playerEnded updates show up). A / B = back to the room (rematch);
/// a new "gameStarted" pulls everybody back automatically.
/// </summary>
public class PsoResultsScene : Scene
{
    private float _t;
    private SpriteSheet _judges;
    private Texture2D _desat;
    public override bool NativeResolution => true;

    public override void Load()
    {
        Input.GameplayMode = false;
        Audio.PlayMusic("music/freakyMenu", true);
        _judges = SpriteSheet.Load(Game, "images/psychonline/onlineJudges", sparrowOnly: true);
        _desat = Assets.LoadTexture("menus/main_menu/menuDesat.png"); if (_desat == Assets.Pixel) _desat = null;
        var o = PsychOnline.ComputeOutcome();
        Console.WriteLine($"[PSORESULTS] winCondition={(int)(PsychOnline.State?.Num("winCondition") ?? 0)} team={PsychOnline.State?.Bool("teamMode")} winner={(o.WinnerSid != null ? PsychOnline.NameOf(o.WinnerSid) : "-")} winnerBfSide={o.WinnerBfSide} tie={o.Tie} | "
            + string.Join(" | ", o.Players.Select(p => $"{p.p.Str("name")} acc={PsychOnline.AccuracyPercent(p.p)} score={p.p.Num("score")} miss={p.p.Num("misses")} fp={p.p.Num("songPoints")} combo={p.p.Num("maxCombo")}")));
    }

    public override void Update(GameTime gameTime)
    {
        _t += (float)gameTime.ElapsedGameTime.TotalSeconds;
        PsychOnline.Poll();
        float exitAfter = float.TryParse(Environment.GetEnvironmentVariable("FNF_MP_EXIT_AFTER_RESULTS"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ea) ? ea : -1f;
        if (exitAfter > 0 && _t > exitAfter) { PsychOnline.Disconnect(); Game.Exit(); return; }
        if ((_t > 0.6f && (Input.ConfirmPressed || Input.BackPressed)) || PsychOnline.GameStartedPending || !PsychOnline.Connected && _t > 2f)
        {
            Audio.PlaySound("confirmMenu");
            Game.Scenes.ChangeScene(new PsychOnlineScene());
        }
    }

    public override void Draw(SpriteBatch sb)
    {
        FunkUI.Begin(sb);
        ProUI.Backdrop(_desat);
        var st = PsychOnline.State;
        var o = PsychOnline.ComputeOutcome();
        string me = PsychOnline.SessionId;
        int wc = Math.Clamp((int)(st?.Num("winCondition") ?? 0), 0, 4);
        bool team = st?.Bool("teamMode") ?? false;
        bool meBf = PsychOnline.SelfBfSide;
        string verdict = o.Tie ? "tie" : team ? (o.WinnerBfSide == meBf ? "weiner" : "loser") : (o.WinnerSid == me ? "weiner" : "loser");
        string song = MultiplayerSession.LastSong ?? st?.Str("song") ?? "";
        ProUI.Header("RESULTS", "ROOM " + (PsychOnline.Room?.RoomId ?? "") + "  -  " + song.ToUpperInvariant() + "  " + PsychOnline.CurrentDiffName().ToUpperInvariant(),
            "WIN CONDITION: " + PsychOnline.WinConditions[wc].ToUpperInvariant() + (team ? "  -  TEAMS" : ""), ProUI.Fg2, _t);

        // verdict (judge art), right of the title
        float ia = FunkUI.EaseOutCubic(_t / 0.35f);
        FunkUI.Push(1000f, 110f, 0f, 0.9f + 0.1f * ia, 0.9f + 0.1f * ia);
        var frames = _judges?.GetAnimation(verdict) ?? _judges?.GetAnimationFuzzy(verdict);
        if (frames != null && frames.Count > 0)
        {
            var f = frames[(int)(_t * 24f) % frames.Count];
            float sc = verdict == "weiner" ? 0.42f : 0.4f, fw = f.FrameWidth > 0 ? f.FrameWidth : f.SourceRect.Width, fh = f.FrameHeight > 0 ? f.FrameHeight : f.SourceRect.Height;
            sb.Draw(f.Texture ?? _judges.Texture, new Vector2(1000f - fw * sc / 2f + f.Offset.X * sc, 110f - fh * sc / 2f + f.Offset.Y * sc), f.SourceRect, new Color(Color.White, ia), 0f, Vector2.Zero, sc, SpriteEffects.None, 0f);
        }
        else ProUI.Title(o.Tie ? "TIE" : verdict == "weiner" ? "YOU WIN" : "YOU LOSE", 1000f, 90f, 0.6f, _t, 0.5f);
        FunkUI.Pop();

        // player cards: one column each (up to 4), opponent side first like the stage
        var players = o.Players.OrderBy(p => p.p.Bool("bfSide") ? 1 : 0).ThenBy(p => p.p.Num("ox")).ToList();
        int n = Math.Max(1, Math.Min(4, players.Count));
        float top = 196f, h = 404f, w = (FunkUI.W - 2 * ProUI.M - (n - 1) * ProUI.G) / n;
        for (int i = 0; i < n && i < players.Count; i++)
        {
            var (sid, p) = players[i];
            float x = ProUI.M + i * (w + ProUI.G);
            bool bf = p.Bool("bfSide"), win = !o.Tie && (team ? o.WinnerBfSide == bf : o.WinnerSid == sid);
            float a = FunkUI.EaseOutCubic((_t - 0.08f * i) / 0.3f);
            float y = top + (1f - a) * 24f;
            ProUI.PanelBox(x, y, w, h, 0.95f * a);
            FunkTheme.Band(x + 18f, y - 8f, w * 0.55f, 10f, 6f, ProUI.A(bf ? ProUI.Bf : ProUI.Opp, a));
            string icon = PsychOnline.SkinOf(p)?[0] ?? "bf";   // LobbyCharacter fallback: bf / bf-opponent (both the bf icon)
            if (FunkUI.Tex("images/icons/icon-" + icon + ".png") == null) icon = "bf";
            FunkUI.HealthIcon(icon, x + 44f, y + 52f, 64f, 0, 0f, a, flip: bf);
            ProUI.TextFit(p.Str("name") ?? "?", x + 86f, y + 26f, 22f, ProUI.A(ProUI.Fg, a), w - 100f);
            float cx = x + 86f;
            cx += ProUI.Chip(bf ? "BOYFRIEND SIDE" : "OPPONENT SIDE", cx, y + 56f, bf ? ProUI.Bf : ProUI.Opp) + 6f;
            if (sid == me) cx += ProUI.Chip("YOU", cx, y + 56f, ProUI.Accent) + 6f;
            if (p.Bool("botplay")) cx += ProUI.Chip("BOTPLAY", cx, y + 56f, ProUI.Bad) + 6f;
            if (team && o.WinnerSid == sid) ProUI.Chip("BEST SOLO", cx, y + 56f, ProUI.Accent, 0f, true);
            ProUI.Rect(x + 16f, y + 96f, w - 32f, 1f, ProUI.Divider);
            double acc = PsychOnline.AccuracyPercent(p);
            ProUI.Caption("ACCURACY", x + 16f, y + 110f);
            float dw = FunkTheme.Digits(acc.ToString("0.00"), x + 16f, y + 132f, 34f, Color.White, 0f, a);
            ProUI.Text("%", x + 22f + dw, y + 140f, 22f, ProUI.A(ProUI.Fg, a));
            ProUI.Chip(PsychOnline.RatingFC(p), x + w - 16f, y + 136f, ProUI.Accent, 1f);
            var rows = new (string k, string v, bool hl)[]
            {
                ("Score", ((long)p.Num("score")).ToString("#,##0"), wc == 1), ("FP", ((int)p.Num("songPoints")).ToString(), wc == 3),
                ("Max Combo", ((int)p.Num("maxCombo")).ToString(), wc == 4), ("Sick", ((int)p.Num("sicks")).ToString(), false),
                ("Good", ((int)p.Num("goods")).ToString(), false), ("Bad", ((int)p.Num("bads")).ToString(), false),
                ("Shit", ((int)p.Num("shits")).ToString(), false), ("Misses", ((int)p.Num("misses")).ToString(), wc == 2),
            };
            float ry = y + 172f;
            foreach (var r in rows)
            {
                ProUI.Text(r.k, x + 16f, ry, 16f, ProUI.A(r.hl ? ProUI.Accent : ProUI.Fg2, a));
                ProUI.Text(r.v, x + w - 16f, ry, 16f, ProUI.A(r.hl ? ProUI.Accent : ProUI.Fg, a), 1f);
                ry += 21f;
            }
            if (win) { FunkTheme.BandOutlined(x + 16f, y + h - 44f, w - 32f, 32f, 10f, FunkTheme.Yellow, 3f, 5f, a); FunkTheme.Heading(team ? "WINNING TEAM" : "WINNER", x + w / 2f, y + h - 44f, 0.38f, _t, 0.5f, a); }
            else if (o.Tie) { FunkTheme.BandOutlined(x + 16f, y + h - 44f, w - 32f, 32f, 10f, FunkTheme.NavyHi, 3f, 0f, a); FunkTheme.Heading("TIE", x + w / 2f, y + h - 44f, 0.38f, _t, 0.5f, a); }
        }
        string left = PlayScene.PsoLastSubmit ?? "";
        ProUI.HintBar(Input, new[] { ButtonPrompts.Confirm("BACK TO ROOM") }, left);
        sb.End();
    }
}
