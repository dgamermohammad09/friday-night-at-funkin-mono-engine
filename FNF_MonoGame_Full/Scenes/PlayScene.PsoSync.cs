using System;
using System.Collections.Generic;
using System.Linq;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.Colyseus;
using FNF_MonoGame.Gameplay;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace FNF_MonoGame.Scenes;

// Psych Online in-game sync that was missing (PSO-FULL round), ported from states/PlayState.hx:
//  - strumPlay ["pressed"|"static"|"confirm", lane, 0] on key press without a hit / release / hit (keyPressed 5319,
//    keyReleased 5357, goodNoteHit 5732) and the remote strum animations (onMessage "strumPlay" 6659);
//  - charPlay [anim, gfNote] on own hits / misses (5527, 5724), taunts ("taunt" / "taunt-alt", controls.TAUNT 3322) and
//    remote charPlay (6684: gf when message[1], else the sender's character, specialAnim when message[2]);
//  - remote rating popups + per-player combo (popUpScoreOP / getRatingOffset), live score text in PSO's format (6907);
//  - Funkin Points: songDensity (generateSong 2828), updateScore -> "updateSongFP" (2364), final calcFP + FunkinPoints.save
//    ("updateFP") at a finished valid song (4511/4555), "botplay" flag (set_cpuControlled 310);
//  - room gameplay settings (songspeed = playback rate), room stage (setStage), hide GF;
//  - replay recording (ReplayRecorder: [songPosMs, "note_left".., 0 press / 1 release]) + upload when logged in.
public partial class PlayScene
{
    private double _psoDensity, _psoSongPoints, _psoTotalNotesHit;
    private int _psoTotalPlayed, _psoMissCount, _psoFrame;
    private readonly int[] _psoPressFrame = { -9, -9, -9, -9 }, _psoHitFrame = { -9, -9, -9, -9 };
    private bool _psoBotSent;
    private int _psoRxStrum, _psoRxChar, _psoRxPopups;
    private readonly List<(double ms, string id, int move)> _psoInputs = new();
    private static readonly string[] PsoKeyIds = { "note_left", "note_down", "note_up", "note_right" };
    private static readonly string[] PsoSing = { "singLEFT", "singDOWN", "singUP", "singRIGHT" };
    private sealed class PsoPopup { public string Rating; public int Combo; public float X, Y, Vx, Vy, Age; public Color Tint; }
    private readonly List<PsoPopup> _psoPopups = new();
    private readonly Dictionary<string, int> _psoRemoteCombo = new();
    private readonly Dictionary<string, Texture2D> _psoTex = new();
    public static string PsoLastSubmit;   // results screen: replay/score upload outcome

    /// <summary>PsoOverrideChart: room stage (state.stageName) replaces the song's stage.</summary>
    private void PsoApplyRoomStage()
    {
        string st = PsychOnline.RoomStageEngineId();
        if (st != null && _chart != null) { Console.WriteLine($"[PSO] room stage {PsychOnline.State?.Str("stageName")} -> {st} (was {_chart.Stage})"); _chart.Stage = st; }
        float rate = PsychOnline.RoomSongSpeed;
        if (Math.Abs(rate - 1f) > 0.001f) { _practiceRate = rate; Console.WriteLine($"[PSO] room songspeed {rate}x"); }
    }

    /// <summary>PsoOnLoaded: song density, hide GF, botplay flag.</summary>
    private void PsoSyncInit()
    {
        _psoDensity = 0; _psoSongPoints = 0; _psoTotalNotesHit = 0; _psoTotalPlayed = 0; _psoMissCount = 0;
        _psoInputs.Clear(); _psoPopups.Clear(); _psoRemoteCombo.Clear(); _psoBotSent = false; PsoLastSubmit = null;
        bool bf = PsychOnline.PlaysAsBF;
        // generateSong: playingNoteCount counts our notes more than 10 ms after the previous one; density per second / 2
        double last = 0; int count = 0;
        foreach (var n in _chart.Notes.Where(n => n.IsPlayerNote == bf).OrderBy(n => n.Time))
        {
            double t = n.Time * 1000.0;
            if (t - last > 10) count++;
            last = t;
        }
        double instSec = Audio.MusicLength;
        if (instSec <= 0 && _chart.Notes.Count > 0) instSec = _chart.Notes.Max(n => n.Time + n.SustainLength) + 2.0;
        float rate = Math.Max(0.1f, JudgeRate);
        _psoDensity = count == 0 || instSec <= 0 ? 0 : count / (instSec / rate) / 2.0;
        Console.WriteLine($"[PSO] note density score: {_psoDensity:F3} for total of {count} notes (inst {instSec:F1}s), max FP {PsychOnline.CalcFP(1, 0, _psoDensity, count, count)}");
        if ((PsychOnline.State?.Bool("hideGF") ?? false) && _girlfriend != null) { _girlfriend.Visible = false; Console.WriteLine("[PSO] hideGF"); }
        if (MpBotDev && !_psoBotSent) { _psoBotSent = true; PsychOnline.Send("botplay"); }
    }

    /// <summary>A person plays our side (not botplay / dev bot): dad side always uses the local input on the opponent strumline.</summary>
    private bool PsoHumanInput => !MpBotDev && (!PsychOnline.PlaysAsBF || !_botplay);
    private Character PsoSelfChar => PsychOnline.PlaysAsBF ? _boyfriend : _opponent;
    private static bool PsoHas(Character c, string anim) => c != null && (c.HasJsonAnimation(anim) || c.HasAnimation(anim));

    /// <summary>Own hit (PsoSendHit, non-sustain): FP counters, charPlay sing, strumPlay confirm.</summary>
    private void PsoSyncOnHit(int lane, string rating)
    {
        _psoHitFrame[lane & 3] = _psoFrame;
        _psoTotalPlayed++;
        _psoTotalNotesHit += rating switch { "sick" => 1.0, "good" => 0.67, "bad" => 0.34, _ => 0.0 };
        PsychOnline.Send("charPlay", new List<object> { PsoSing[lane & 3], false });
        if (PsoHumanInput) PsychOnline.Send("strumPlay", new List<object> { "confirm", lane & 3, 0 });   // PSO: only when !cpuControlled
        PsoUpdateScore();
    }

    private void PsoSyncOnMiss(int lane)
    {
        _psoTotalPlayed++; _psoMissCount++;
        PsychOnline.Send("charPlay", new List<object> { PsoSing[lane & 3] + "miss", false });
        PsoUpdateScore();
    }

    private double PsoRatingPercent => _psoTotalPlayed == 0 ? 0 : Math.Min(1, Math.Max(0, _psoTotalNotesHit / _psoTotalPlayed));

    /// <summary>PlayState.updateScore: FP rounded to 2 decimals, "updateSongFP" (floored) whenever it changes.</summary>
    private void PsoUpdateScore()
    {
        double pts = Math.Round(PsychOnline.FCalcFP(PsoRatingPercent, _psoMissCount, _psoDensity, _psoTotalNotesHit, _psoMaxCombo), 2);
        if (pts != _psoSongPoints) { _psoSongPoints = pts; PsychOnline.Send("updateSongFP", Math.Floor(pts)); }
    }

    /// <summary>Per frame (PsoUpdate): delayed "pressed" (press without a hit), "static" on release, taunts, replay inputs, popups.</summary>
    private void PsoSyncUpdate(float dt)
    {
        _psoFrame++;
        bool human = PsoHumanInput;
        if (human && _psoStarted && !_paused)
        {
            double posMs = _conductor.SongPosition * 1000.0;
            for (int l = 0; l < 4; l++)
            {
                if (Input.NotePressed[l]) { _psoPressFrame[l] = _psoFrame; _psoInputs.Add((Math.Round(posMs, 3), PsoKeyIds[l], 0)); }
                if (Input.NoteReleased[l])
                {
                    _psoInputs.Add((Math.Round(posMs, 3), PsoKeyIds[l], 1));
                    PsychOnline.Send("strumPlay", new List<object> { "static", l, 0 });
                }
                // a press that hit nothing (no own hit within this frame or the next) -> "pressed"
                if (_psoPressFrame[l] == _psoFrame - 1 && _psoHitFrame[l] < _psoPressFrame[l])
                    PsychOnline.Send("strumPlay", new List<object> { "pressed", l, 0 });
            }
            // controls.TAUNT (Space / right stick), "-alt" with ALT
            if (Input.IsPressed(Keys.Space) || Input.IsGamePadPressed(Buttons.RightStick))
            {
                string anim = "taunt" + (Input.IsHeld(Keys.LeftAlt) || Input.IsHeld(Keys.RightAlt) ? "-alt" : "");
                var me = PsoSelfChar;
                if (PsoHas(me, anim)) me.PlayAnimation(anim, true);
                PsychOnline.Send("charPlay", new List<object> { anim });
                Console.WriteLine("[PSO] taunt " + anim);
            }
        }
        for (int i = _psoPopups.Count - 1; i >= 0; i--)
        {
            var p = _psoPopups[i];
            p.Age += dt; p.Vy += 550f * dt; p.Y += p.Vy * dt; p.X += p.Vx * dt;
            float life = 60f / Math.Max(1f, _conductor.BPM) + 0.2f;
            if (p.Age > life) _psoPopups.RemoveAt(i);
        }
    }

    /// <summary>Remote strumPlay: animate the sender's (other side's) strum.</summary>
    private void PsoRemoteStrum(string sid, List<object> body)
    {
        if (body == null || body.Count < 3 || _vs == null) return;
        bool remoteBf = PsoSideOf(sid);
        if (remoteBf == PsychOnline.PlaysAsBF) return;   // getStrumsFromSID == getPlayerStrums -> ignored
        string anim = body[0]?.ToString(); int lane = (int)MsgPack.Num(body[1]) & 3;
        var s = _vsStrum[lane + (remoteBf ? 4 : 0)];
        if (++_psoRxStrum <= 3 || _psoRxStrum % 50 == 0) Console.WriteLine($"[PSO] remote strumPlay #{_psoRxStrum} {anim} lane {lane} ({(remoteBf ? "bf" : "dad")} strums)");
        switch (anim)
        {
            case "pressed": VsPlay(s, VS_PRESS); s.HoldTimer = -1; break;
            case "static": VsPlay(s, VS_STATIC); s.HoldTimer = -1; break;
            case "confirm": VsPlay(s, VS_CONFIRM); s.HoldTimer = 0; break;
        }
    }

    /// <summary>Remote charPlay: [anim, isGf, special].</summary>
    private void PsoRemoteChar(string sid, List<object> body)
    {
        if (body == null || body.Count < 1 || body[0] == null || sid == PsychOnline.SessionId) return;
        string anim = body[0].ToString();
        if (++_psoRxChar <= 3 || _psoRxChar % 50 == 0 || anim.StartsWith("taunt")) Console.WriteLine($"[PSO] remote charPlay #{_psoRxChar} {anim}");
        bool gf = body.Count > 1 && MsgPack.Bool(body[1]);
        if (gf) { if (PsoHas(_girlfriend, anim)) _girlfriend.PlayAnimation(anim, true); return; }
        bool remoteBf = PsoSideOf(sid);
        var ch = PsoCharOf(sid);                           // characters.get(sid): extra players' own characters
        if (ch == null)
        {
            if (remoteBf == PsychOnline.PlaysAsBF) return;   // a teammate on our main character: it stays ours
            ch = remoteBf ? _boyfriend : _opponent;
        }
        if (PsoHas(ch, anim)) { ch.PlayAnimation(anim, true); if (body.Count > 2 && MsgPack.Bool(body[2])) ch.SpecialAnim = true; }
    }

    /// <summary>Remote noteHit with a rating (not a sustain piece): popUpScoreOP + combo++.</summary>
    private void PsoRemotePopup(string sid, string rating)
    {
        if (string.IsNullOrEmpty(rating)) return;
        _psoRxPopups++;
        _psoRemoteCombo.TryGetValue(sid, out int combo); combo++; _psoRemoteCombo[sid] = combo;
        var prefs = FNF_MonoGame.Engine.PsychEngine.ClientPrefs.Data;
        if (prefs.HideHud) return;
        bool remoteBf = PsoSideOf(sid);
        // getRatingOffset: FlxG.width * (0.4 + (isPlayer == playsAsBF ? 0.15 : -0.1)); screenCenter, x - 40, y - 60
        float x = FNFGame.SCREEN_WIDTH * (0.4f + (remoteBf == PsychOnline.PlaysAsBF ? 0.15f : -0.1f)) - 40f;
        var tint = Color.White;
        if (prefs.ColorRating)
            tint = rating switch { "sick" => new Color(0x6C, 0xFD, 0x8E), "good" => new Color(0x68, 0xD5, 0xFD), "bad" => new Color(0xFC, 0xD7, 0x68), _ => new Color(0xFC, 0x6B, 0x68) };
        if (!prefs.ComboStacking) _psoPopups.RemoveAll(p => p.Tint.PackedValue != 0 && p.Combo >= 0 && p.X == x);
        _psoPopups.Add(new PsoPopup { Rating = rating, Combo = combo, X = x, Y = FNFGame.SCREEN_HEIGHT / 2f - 60f, Vy = -_random.Next(140, 176), Vx = -_random.Next(0, 11), Tint = tint });
    }

    private Texture2D PsoTex(string path)
    {
        if (_psoTex.TryGetValue(path, out var t)) return t;
        t = Assets.LoadTexture(path); if (t == Assets.Pixel) t = null;
        _psoTex[path] = t; return t;
    }

    private void PsoDrawPopups(SpriteBatch sb)
    {
        float life0 = 60f / Math.Max(1f, _conductor.BPM);
        foreach (var p in _psoPopups)
        {
            float a = p.Age < life0 ? 1f : Math.Max(0f, 1f - (p.Age - life0) / 0.2f);
            var t = PsoTex("game/skins/default/ratings/" + p.Rating + ".png");
            if (t != null) sb.Draw(t, new Vector2(p.X, p.Y), null, new Color(p.Tint, a), 0f, Vector2.Zero, 0.7f * 0.75f, SpriteEffects.None, 0f);
            if (p.Combo < 10) continue;
            string digits = p.Combo.ToString("000");
            for (int i = 0; i < digits.Length; i++)
            {
                var d = PsoTex("game/skins/default/comboNumbers/num" + digits[i] + ".png");
                if (d != null) sb.Draw(d, new Vector2(p.X + 43f * i * 0.75f - 10f, p.Y + 90f), null, Color.White * a, 0f, Vector2.Zero, 0.5f * 0.75f, SpriteEffects.None, 0f);
            }
        }
    }

    /// <summary>PSO score line for a player (PlayState 6907): "name: score | n M | acc% - FC | xFP | ping".</summary>
    private static string PsoScoreLine(SchemaObject p, bool showFp)
    {
        if (p == null) return null;
        return $"{p.Str("name")}: {(long)p.Num("score"):#,##0} | {(int)p.Num("misses")} M | {PsychOnline.AccuracyPercent(p)}% - {PsychOnline.RatingFC(p)}"
             + (showFp ? $" | {(int)p.Num("songPoints")}FP" : "") + $" | {(int)p.Num("ping")}ms" + (p.Bool("botplay") ? " [BOT]" : "");
    }

    /// <summary>End of the match (PsoEndSong): final FP, offline FP save + replay save/upload for a finished valid song.</summary>
    private void PsoSyncEnd()
    {
        double fp = PsychOnline.CalcFP(PsoRatingPercent, _psoMissCount, _psoDensity, _psoTotalNotesHit, _psoMaxCombo);
        bool finished = Audio.MusicLength > 0 && _conductor.SongPosition >= Audio.MusicLength - 3.0;
        bool valid = !MpBotDev && !_psoBotSent && finished && Environment.GetEnvironmentVariable("FNF_MP_END_AT") == null;
        Console.WriteLine($"[PSO] end: FP {fp} (acc {PsoRatingPercent * 100:F2}% misses {_psoMissCount} hit {_psoTotalNotesHit:F2} maxCombo {_psoMaxCombo}) finished={finished} valid={valid} inputs={_psoInputs.Count} | received strumPlay {_psoRxStrum}, charPlay {_psoRxChar}, rating popups {_psoRxPopups}");
        if (Environment.GetEnvironmentVariable("FNF_PSO_FORCE_SUBMIT") == "1") valid = true;   // dev: exercise the save/upload path
        if (!valid) { PsoLastSubmit = finished ? "FP not saved (botplay)" : "FP not saved (song not finished)"; return; }
        double gained = PsychOnline.SaveFP(PsoRatingPercent, _psoMissCount, _psoDensity, _psoTotalNotesHit, _psoMaxCombo);
        PsoLastSubmit = "+" + gained + " FP (total " + PsychOnline.FunkinPoints + ")";
        if (FNF_MonoGame.Engine.PsychEngine.ClientPrefs.Data.DisableReplays) return;
        var st = PsychOnline.State; var me = PsychOnline.Self;
        string hash = PsychOnline.ChartHash(st?.Str("folder"), st?.Str("song"), st?.Str("modDir"));   // Md5.encode(RAW_SONG)
        var replay = PsychOnline.BuildReplay(_chart?.DisplayName ?? st?.Str("folder"), PsychOnline.CurrentDiffName(), !PsychOnline.PlaysAsBF, hash, _psoInputs,
            (int)(me?.Num("score") ?? _score), _psoMissCount, PsoRatingPercent, (int)(me?.Num("sicks") ?? 0), (int)(me?.Num("goods") ?? 0),
            (int)(me?.Num("bads") ?? 0), (int)(me?.Num("shits") ?? 0), fp, st?.Str("modURL"));
        var task = PsychOnline.SaveAndUploadReplayAsync(replay);
        task.ContinueWith(t => { PsoLastSubmit += " - " + (t.Status == System.Threading.Tasks.TaskStatus.RanToCompletion ? t.Result : "upload error"); Console.WriteLine("[PSO] replay: " + PsoLastSubmit); });
    }
}
