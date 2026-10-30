using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json.Linq;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>Versus results for both players (local or online). A / Enter: back to the multiplayer menu / room.</summary>
public class MultiplayerResultsScene : Scene
{
    private float _t;
    private Texture2D _bg;

    public override void Load()
    {
        Input.GameplayMode = false;
        _bg = Assets.LoadTexture("menus/main_menu/background.png");
        Audio.PlayMusic("music/freakyMenu", true);
        var r = MultiplayerSession.LastResults;
        if (r != null) Console.WriteLine($"[MPRESULTS] {MultiplayerSession.LastSong}: P1 {r[0].Name} {r[0].Score} acc={r[0].Accuracy:F1} miss={r[0].Misses} | P2 {r[1].Name} {r[1].Score} acc={r[1].Accuracy:F1} miss={r[1].Misses}");
    }

    public override void Update(GameTime gameTime)
    {
        _t += (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (MultiplayerSession.Mode == MultiplayerSession.MpMode.Psych) { PsoResultsUpdate(); return; }
        var net = MultiplayerSession.Net;
        if (MultiplayerSession.Online && net != null)
        {
            while (net.Inbox.TryPeek(out var m))
            {
                if (MultiplayerScene.HandleCommon(m, this)) return;
                net.Inbox.TryDequeue(out _);
                if ((string)m["t"] == "stats" && MultiplayerSession.LastResults != null)
                {   // the other player's final numbers (may arrive after we switched scenes)
                    int side = (int?)m["side"] ?? 0;
                    if (side != MultiplayerSession.LocalSide)
                    {
                        var r = MultiplayerSession.LastResults[side];
                        r.Score = (int?)m["score"] ?? r.Score; r.Misses = (int?)m["misses"] ?? r.Misses; r.MaxCombo = (int?)m["maxCombo"] ?? r.MaxCombo;
                        r.Sick = (int?)m["sick"] ?? r.Sick; r.Good = (int?)m["good"] ?? r.Good; r.Bad = (int?)m["bad"] ?? r.Bad; r.Shit = (int?)m["shit"] ?? r.Shit;
                        r.Hits = (int?)m["hits"] ?? r.Hits; r.Accuracy = r.Total > 0 ? Math.Clamp(r.Hits / (float)r.Total, 0f, 1f) * 100f : 0f;
                    }
                }
            }
        }
        float exitAfter = float.TryParse(Environment.GetEnvironmentVariable("FNF_MP_EXIT_AFTER_RESULTS"), out var ea) ? ea : -1f;
        if (exitAfter > 0 && _t > exitAfter) { MultiplayerSession.Leave(); Game.Exit(); return; }
        if (_t > 0.5f && (Input.ConfirmPressed || Input.BackPressed))
        {
            Audio.PlaySound("confirmMenu");
            Game.Scenes.ChangeScene(new MultiplayerScene());
        }
    }

    /// <summary>Psych Online: keep the room alive, refresh final numbers from the room state, back to the room.</summary>
    private void PsoResultsUpdate()
    {
        PsychOnline.Poll();
        var r = MultiplayerSession.LastResults;
        if (r != null && PsychOnline.Connected)
            for (int side = 0; side < 2; side++)
            {
                var p = PsychOnline.FirstOnSide(side == 0).p;
                if (p == null) continue;
                var x = r[side];
                x.Score = (int)p.Num("score"); x.Misses = (int)p.Num("misses"); x.MaxCombo = Math.Max(x.MaxCombo, (int)p.Num("maxCombo"));
                x.Sick = (int)p.Num("sicks"); x.Good = (int)p.Num("goods"); x.Bad = (int)p.Num("bads"); x.Shit = (int)p.Num("shits");
                x.Hits = x.Sick + x.Good + x.Bad + x.Shit; x.Accuracy = x.Total > 0 ? Math.Clamp(x.Hits / (float)x.Total, 0f, 1f) * 100f : 0f;
            }
        float exitAfter = float.TryParse(Environment.GetEnvironmentVariable("FNF_MP_EXIT_AFTER_RESULTS"), out var ea) ? ea : -1f;
        if (exitAfter > 0 && _t > exitAfter) { PsychOnline.Disconnect(); Game.Exit(); return; }
        if ((_t > 0.5f && (Input.ConfirmPressed || Input.BackPressed)) || PsychOnline.GameStartedPending)
        {
            Audio.PlaySound("confirmMenu");
            Game.Scenes.ChangeScene(new PsychOnlineScene());
        }
    }

    private Texture2D _desat;

    public override void Draw(SpriteBatch sb)
    {
        // Original menu look: menuDesat tinted, BOLD AtlasText headers, DEFAULT-font stats, prompt bar.
        if (_desat == null) { _desat = Assets.LoadTexture("menus/main_menu/menuDesat.png"); if (_desat == Assets.Pixel) _desat = null; }
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
        MenuUI.DrawBackground(sb, _desat, MenuUI.DefaultTint);
        var r = MultiplayerSession.LastResults;
        var bold = AtlasText.Bold; var def = AtlasText.Default;
        bold?.DrawCentered(sb, "RESULTS", 30, 1f, _t);
        def?.DrawCentered(sb, MultiplayerSession.LastSong ?? "", 110, 0.8f, _t, 0.6f);
        if (r != null)
        {
            int winner = r[0].Score == r[1].Score ? -1 : (r[0].Score > r[1].Score ? 0 : 1);
            for (int i = 0; i < 2; i++)
            {
                // P2 (opponent side) on the left like the strumlines, P1 (boyfriend side) on the right
                var p = r[i]; float cx = i == 1 ? 340 : 940;
                var panel = new Rectangle((int)cx - 290, 170, 580, 420);
                MenuUI.Rect(sb, panel, new Color(Color.Black, 0.6f));
                string who = (i == 0 ? "P1 " : "P2 ") + (p.Name ?? "");
                var col = i == 0 ? new Color(0x90, 0xFF, 0x90) : new Color(0xFF, 0x90, 0x90);
                if (bold != null) { float sc = Math.Min(0.75f, 540f / Math.Max(1f, bold.Width(who))); bold.Draw(sb, who, cx - bold.Width(who, sc) / 2f, 190, 1f, _t, sc, col); }
                if (def != null)
                {
                    string score = p.Score.ToString("#,##0");
                    def.Draw(sb, score, cx - def.Width(score, 1.1f) / 2f, 260, 1f, _t, 1.1f);
                    string[] lines = {
                        $"Accuracy {p.Accuracy:F2}%", $"Hits {p.Hits}/{p.Total}", $"Misses {p.Misses}  Combo {p.MaxCombo}",
                        $"Sick {p.Sick}  Good {p.Good}", $"Bad {p.Bad}  Shit {p.Shit}" };
                    for (int k = 0; k < lines.Length; k++) def.Draw(sb, lines[k], cx - def.Width(lines[k], 0.5f) / 2f, 350 + k * 40, 0.9f, _t, 0.5f);
                }
                if (winner == i) bold?.Draw(sb, "WINNER!", cx - (bold.Width("WINNER!", 0.8f)) / 2f, 600, 1f, _t, 0.8f, new Color(255, 215, 0));
            }
            if (winner < 0) bold?.DrawCentered(sb, "DRAW", 600, 1f, _t, 0.9f, new Color(255, 215, 0));
        }
        ButtonPrompts.DrawBar(sb, Assets, Input, new[] { ButtonPrompts.Confirm("CONTINUE") }, FNFGame.SCREEN_WIDTH / 2, FNFGame.SCREEN_HEIGHT - 46, 0.5f, 32);
        sb.End();
    }
}
