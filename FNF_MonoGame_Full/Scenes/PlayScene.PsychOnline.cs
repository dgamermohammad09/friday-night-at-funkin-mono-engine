using System;
using System.Collections.Generic;
using System.Linq;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.Colyseus;
using FNF_MonoGame.Gameplay;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Note = FNF_MonoGame.Engine.PsychEngine.Note;

namespace FNF_MonoGame.Scenes;

// Psych Online match (Funkin-Online-Server room). Ports the multiplayer parts of Psych Online's PlayState.hx:
//  - the chart is the room's Psych chart file (same notes/strumTimes as the PC players, Song.loadRawSong);
//  - "playerReady" once loaded, countdown on "startSong";
//  - own hits:  noteHit [strumTime, noteData, isSustainNote, rating, noteType, index, mustPress] + addScore + addHitJudge
//               (+ one noteHit per sustain piece, Psych splits holds into stepCrochet pieces: generateSong());
//  - own misses: addScore -10, addMiss, noteMiss [strumTime, noteData, isSustainNote] (noteMissCommon);
//  - the other side's notes are hit from its noteHit messages (PlayState.hx onMessage("noteHit"): match lane +
//    isSustainNote + |strumTime - msg| < 1 -> opponentNoteHit); its noteMiss plays the miss animation. Only when
//    nobody plays the other side (solo room) is it auto-played like the CPU;
//  - health = room state health (server: noteHit +-0.023, noteMiss +-0.0475, bf side positive);
//  - song end: updateSongFP, updateMaxCombo, playerEnded; Escape = requestEndSong (LeavePie).
public partial class PlayScene
{
    private bool _mpPso, _psoStarted, _psoEndSent;
    private float _psoReadyResend;
    private double _psoStepMs = 150;   // Conductor.stepCrochet at generateSong (15000 / SONG.bpm)
    private int _psoCombo, _psoMaxCombo, _psoMaxComboSent = -1;
    private sealed class PsoHold { public double T0; public int Lane, N, Next; public bool Must; }
    private readonly List<PsoHold> _psoHolds = new List<PsoHold>();
    private bool _psoRemoteOpp, _psoRemoteBf;          // that strumline is driven by the other player's messages
    private readonly List<(string text, float t)> _psoChat = new List<(string, float)>();
    private readonly string[] _psoSkin = new string[2];
    private int _psoRemoteHits, _psoRemoteMsgs, _psoRemoteUnmatched;

    /// <summary>Right after Chart.Load: replace the notes with the room's Psych chart (exact strumTimes).</summary>
    private void PsoOverrideChart()
    {
        if (MultiplayerSession.Mode != MultiplayerSession.MpMode.Psych || !PsychOnline.Connected || _chart == null) return;
        var st = PsychOnline.State;
        string path = PsychOnline.ChartPath(st.Str("folder"), st.Str("song"), st.Str("modDir"));
        var c = Chart.LoadPsychFile(path, _currentDifficulty);
        if (c == null || c.Notes.Count == 0) { Console.WriteLine("[PSO] chart missing: " + path); return; }
        _chart.Notes.Clear();
        _chart.Notes.AddRange(c.Notes);
        _chart.Speed = c.Speed;
        if (c.BPM > 0) _psoStepMs = 15000.0 / c.BPM;
        Console.WriteLine($"[PSO] chart {path}: {c.Notes.Count} notes, speed {c.Speed}, bpm {c.BPM}");
        PsoApplyRoomStage();
    }

    private void PsoOnLoaded()
    {
        _mpActive = true; _mpPso = true;
        _practiceMode = true;   // no game over online: health is the room's tug of war
        bool bf = PsychOnline.PlaysAsBF;
        _mpOppHuman = !bf;
        bool remotePresent = PsychOnline.FirstOnSide(!bf).p != null && Environment.GetEnvironmentVariable("FNF_PSO_LOCALAUTO") != "1";
        _psoRemoteOpp = bf && remotePresent;     // the dad strumline shows the remote player's real hits
        _psoRemoteBf = !bf && remotePresent;     // the boyfriend strumline shows the remote player's real hits
        _noteField.OpponentHuman = _mpOppHuman || _psoRemoteOpp;
        _noteField.RemotePlayer = _psoRemoteBf;
        if (!bf || (MpBotDev && bf)) _botplay = true;    // dad side: the boyfriend strumline is the remote player's (no local input)
        _psoSkin[0] = PsychOnline.SkinName(PsychOnline.FirstOnSide(true).p); _psoSkin[1] = PsychOnline.SkinName(PsychOnline.FirstOnSide(false).p);
        foreach (var n in _chart.Notes) { if (n.IsPlayerNote) _mp[0].Total++; else _mp[1].Total++; }
        var p1 = PsychOnline.FirstOnSide(true); var p2 = PsychOnline.FirstOnSide(false);
        _mp[0].Name = p1.p?.Str("name") ?? "Boyfriend"; _mp[1].Name = p2.p?.Str("name") ?? "Opponent";
        MultiplayerSession.PeerName = bf ? _mp[1].Name : _mp[0].Name;
        _mpWaitStart = true;
        _psoStarted = false;
        PsoSyncInit();
        PsoResolveNoteLooks();
        PsoInitPlayers();
        PsychOnline.Send("status", "In-Game");
        PsychOnline.Send("playerReady");
        Console.WriteLine($"[PSO] loaded: playsAsBF={bf} botplay={_botplay} totals={_mp[0].Total}/{_mp[1].Total}");
    }

    private bool PsoHoldStart()
    {
        PsoUpdate(_lastDelta);
        if (!PsychOnline.Connected) { _mpWaitStart = false; return false; }
        if (!_psoStarted)
        {
            _psoReadyResend += _lastDelta;
            if (_psoReadyResend > 5f) { _psoReadyResend = 0; PsychOnline.Send("playerReady"); }
        }
        return !_psoStarted;
    }

    private static string PsoRating(string r)
    {
        r = (r ?? "").ToUpperInvariant();
        if (r.Contains("SICK") || r.Contains("KILLER") || r.Contains("PERFECT")) return "sick";
        if (r.Contains("GOOD")) return "good";
        if (r.Contains("BAD")) return "bad";
        return "shit";
    }

    private static double PsoMs(double sec) => Math.Round(sec * 1000.0, 3);

    /// <summary>Boyfriend-side hit (OnNoteHit hook).</summary>
    private void PsoHookHit(double timeSec, int lane, double susSec, string rating, int score, bool botplayHit)
    {
        if (!_mpPso || timeSec < 0) return;
        if (!PsychOnline.PlaysAsBF) return;               // our boyfriend strumline is the remote player's
        if (botplayHit && !MpBotDev) return;
        PsoSendHit(timeSec, lane, susSec, rating, score, true);
    }

    private void PsoHookMiss(int lane, double timeSec)
    {
        if (!_mpPso || !PsychOnline.PlaysAsBF) return;
        PsoSendMiss(lane, timeSec, true);
    }

    private void PsoSendHit(double timeSec, int lane, double susSec, string rating, int score, bool mustPress)
    {
        string r = PsoRating(rating);
        PsychOnline.Send("noteHit", new List<object> { PsoMs(timeSec), lane, false, r, "", 0, mustPress });
        PsychOnline.Send("addScore", score);
        PsychOnline.Send("addHitJudge", r);
        _psoCombo++; if (_psoCombo > _psoMaxCombo) _psoMaxCombo = _psoCombo;
        PsoSyncOnHit(lane, r);
        var s = _mp[mustPress ? 0 : 1];
        if (!mustPress) { /* opponent-human stats are counted in MpProcessOpponentHuman */ }
        double susMs = susSec * 1000.0;
        int floorSus = (int)Math.Floor(susMs / _psoStepMs);
        if (floorSus > 0) _psoHolds.Add(new PsoHold { T0 = PsoMs(timeSec), Lane = lane, N = floorSus, Next = 0, Must = mustPress });
    }

    private void PsoSendMiss(int lane, double timeSec, bool mustPress)
    {
        _psoCombo = 0;
        PsoSyncOnMiss(lane);
        PsychOnline.Send("addScore", -10);
        PsychOnline.Send("addMiss");
        if (timeSec >= 0) PsychOnline.Send("noteMiss", new List<object> { PsoMs(timeSec), lane, false });
    }

    private void PsoUpdate(float dt)
    {
        PsychOnline.Poll();
        string self = PsychOnline.SessionId;
        int guard = 0;
        while (guard++ < 512 && PsychOnline.PlayInbox.TryDequeue(out var m))
        {
            var arr = m.msg as List<object>;
            string sid = arr != null && arr.Count > 0 ? arr[0] as string : null;
            var body = arr != null && arr.Count > 1 ? arr[1] as List<object> : null;
            switch (m.type)
            {
                case "startSong":
                    if (!_psoStarted) { _psoStarted = true; _mpWaitStart = false; Console.WriteLine("[PSO] startSong"); }
                    break;
                case "noteHit":
                    if (sid == self || body == null || body.Count < 3) break;
                    {
                        bool remoteBf = PsoSideOf(sid);
                        if (body.Count > 3 && !MsgPack.Bool(body[2]) && body[3] != null) PsoRemotePopup(sid, body[3].ToString());
                        if (remoteBf ? _psoRemoteBf : _psoRemoteOpp)
                            PsoRemoteHit(remoteBf, MsgPack.Num(body[0]), (int)MsgPack.Num(body[1]), MsgPack.Bool(body[2]), sid);
                        if (remoteBf != PsychOnline.PlaysAsBF) PsoVocals(sid, 1f);   // getVocalsFromSIDVolume(sid, 1)
                    }
                    break;
                case "noteMiss":
                    if (sid == self || body == null || body.Count < 3) break;
                    {
                        int lane = (int)MsgPack.Num(body[1]);
                        _psoRemoteCombo[sid] = 0;
                        bool remoteBf = PsoSideOf(sid);
                        if (!MsgPack.Bool(body[2])) (PsoCharOf(sid) ?? (remoteBf ? _boyfriend : _opponent))?.Miss(lane);
                        if (remoteBf != PsychOnline.PlaysAsBF) PsoVocals(sid, 0f);   // getVocalsFromSIDVolume(sid, 0)
                        // PlayState: the matching note is killed
                        var n = PsoFindNote(remoteBf, MsgPack.Num(body[0]), lane);
                        if (n != null && !n.IsHit) n.Missed = true;
                    }
                    break;
                case "strumPlay": if (sid != self) PsoRemoteStrum(sid, body); break;
                case "charPlay": if (sid != self) PsoRemoteChar(sid, body); break;
                case "endSong":
                    if (!_mpEnded) { MpNotice("The song was ended"); _forceEndSong = true; }
                    break;
            }
        }
        PsoSyncUpdate(dt);
        PsoPlayersUpdate(dt);
        if (float.TryParse(Environment.GetEnvironmentVariable("FNF_MP_END_AT"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var endAt)
            && !_countdownActive && _conductor.SongPosition > endAt) _forceEndSong = true;   // dev: short test matches
        if (_psoRemoteOpp) _noteField.OpponentMissed.Clear();   // unhit remote notes just scroll away (stats come from the room)
        while (PsychOnline.ChatFeed.TryDequeue(out var line)) { _psoChat.Add((line, 7f)); if (_psoChat.Count > 4) _psoChat.RemoveAt(0); if (FNF_MonoGame.Engine.PsychEngine.ClientPrefs.Data.NotifyOnChatMsg) Audio.PlaySound("scrollMenu", 0.6f); }
        for (int i = _psoChat.Count - 1; i >= 0; i--) { var c = _psoChat[i]; c.t -= dt; if (c.t <= 0) _psoChat.RemoveAt(i); else _psoChat[i] = c; }
        if (!PsychOnline.Connected) return;
        var st = PsychOnline.State;
        // health: room state (bf side = 0..2)
        if (st != null && st.Bool("isStarted") && _psoStarted) _health = Math.Clamp((float)st.Num("health"), 0.01f, HEALTH_MAX);
        // remote side numbers from the room state; ours are live
        PsoStatsFromState(true, 0); PsoStatsFromState(false, 1);
        if (_psoMaxCombo != _psoMaxComboSent && (_psoMaxCombo % 10 == 0 || _psoCombo == 0)) { _psoMaxComboSent = _psoMaxCombo; PsychOnline.Send("updateMaxCombo", _psoMaxCombo); }
        // sustain pieces of our held notes (Psych: daStrumTime + stepCrochet * susNote, susNote = 0..floorSus)
        double posMs = _conductor.SongPosition * 1000.0;
        for (int i = _psoHolds.Count - 1; i >= 0; i--)
        {
            var h = _psoHolds[i];
            bool held = Input.NoteHeld[h.Lane] || (MpBotDev);
            while (h.Next <= h.N)
            {
                double t = Math.Round(h.T0 + _psoStepMs * h.Next, 3);
                if (t > posMs) break;
                if (!held) { h.Next = h.N + 1; break; }
                PsychOnline.Send("noteHit", new List<object> { t, h.Lane, true, null, "", 0, h.Must });
                h.Next++;
            }
            if (h.Next > h.N) _psoHolds.RemoveAt(i);
        }
    }

    private Note PsoFindNote(bool bfSide, double tMs, int lane)
    {
        Note best = null;
        foreach (var n in _noteField.GetAllActiveNotes())
            if (n.IsPlayerNote == bfSide && n.Lane == lane && !n.HoldComplete && Math.Abs(n.Time * 1000.0 - tMs) < 1.0)
            { if (best == null || !n.IsHit) best = n; }
        return best;
    }

    /// <summary>The other player hit a note (their noteHit message): play it on their strumline like Psych Online's opponentNoteHit.</summary>
    private void PsoRemoteHit(bool bfSide, double tMs, int lane, bool sustainPiece, string sid = null)
    {
        if (sustainPiece) return;   // hold pieces: the head hit already holds the whole sustain (singing + strum glow)
        _psoRemoteMsgs++;
        var note = PsoFindNote(bfSide, tMs, lane);
        if (note == null || note.IsHit)
        {
            if (++_psoRemoteUnmatched <= 5)
                Console.WriteLine($"[PSO] remote noteHit not matched: {(bfSide ? "bf" : "dad")} lane {lane} t={tMs} at song {_conductor.SongPosition * 1000.0:0} ms ({(note == null ? "no active note" : "already hit")})");
            return;
        }
        note.Missed = false;
        _psoRemoteHits++;
        double pos = _conductor.SongPosition;
        if (bfSide)
        {
            OnNoteHit(lane, "SICK!!", 500, note, botplayHit: true);
            _noteField.RemoveNote(note);
            if (!note.NoAnimation) (PsoCharOf(sid) ?? _boyfriend).Sing(lane, note.Kind);
            if (note.SustainLength > 0 && note.Time + note.SustainLength > pos)
            {
                _holdingNote[lane] = true;
                _holdNoteEndTime[lane] = note.Time + note.SustainLength;
                _holdNoteRef[lane] = note;
                _holdRemainingMs[lane] = (_holdNoteEndTime[lane] - pos) * 1000.0;
            }
        }
        else
        {
            if (!note.NoAnimation) (PsoCharOf(sid) ?? _opponent).Sing(note.Lane, note.Kind);
            DispatchOpponentNoteHit(note);
            _opponentConfirmTimer[lane] = 0.15f; VsStrumHit(false, lane, note);
            _noteField.RemoveNote(note);
        }
        if (_psoRemoteHits <= 3 || _psoRemoteHits % 25 == 0)
            Console.WriteLine($"[PSO] remote hit #{_psoRemoteHits} {(bfSide ? "bf" : "dad")} lane {lane} t={tMs} at song {pos * 1000.0:0} ms");
    }

    /// <summary>Skin names under the score texts + the room chat/log lines (bottom left), drawn by MpDrawHud.</summary>
    private void PsoDrawHud(SpriteBatch sb, float bgX, float bgY, float bgW)
    {
        PsoDrawPopups(sb);
        if (_psoSkin[1] != null) DrawFlxOutlinedText(sb, 14, "skin: " + _psoSkin[1], new Vector2(bgX, bgY + 50), new Color(0xFF, 0xC0, 0xC0), Color.Black);
        if (_psoSkin[0] != null) DrawFlxOutlinedText(sb, 14, "skin: " + _psoSkin[0], new Vector2(bgX + bgW - 190, bgY + 50), new Color(0xC0, 0xFF, 0xC0), Color.Black);
        var prefs = FNF_MonoGame.Engine.PsychEngine.ClientPrefs.Data;
        if (prefs.ShowPing && PsychOnline.State != null)   // Options > Psych Online > Show Ping
        {
            var (_, pl) = PsychOnline.FirstOnSide(false); var (_, pr) = PsychOnline.FirstOnSide(true);
            if (pl != null) DrawFlxOutlinedText(sb, 14, (int)pl.Num("ping") + "ms", new Vector2(bgX, bgY + 68), Color.White, Color.Black);
            if (pr != null) DrawFlxOutlinedText(sb, 14, (int)pr.Num("ping") + "ms", new Vector2(bgX + bgW - 190, bgY + 68), Color.White, Color.Black);
        }
        if (!prefs.InGameChat) return;   // Options > Psych Online > Show Chat In-Game
        var f = Assets.GetFont(16);
        if (f == null) return;
        float y = FNFGame.SCREEN_HEIGHT - 150;
        foreach (var (text, t) in _psoChat)
        {
            var size = f.MeasureString(text);
            float a = Math.Min(1f, t);
            sb.Draw(Assets.Pixel, new Rectangle(6, (int)y - 2, (int)size.X + 12, (int)size.Y + 4), Color.Black * (0.55f * a));
            f.DrawText(sb, text, new Vector2(12, y), Color.White * a);
            y += size.Y + 6;
        }
    }

    /// <summary>true when the player with this session id plays the boyfriend side.</summary>
    private static bool PsoSideOf(string sid)
    {
        var pl = PsychOnline.Players;
        if (pl != null && sid != null && pl.Items.TryGetValue(sid, out var o) && o is SchemaObject so) return PsychOnline.EffectiveBfSide(so);
        return !PsychOnline.PlaysAsBF;
    }

    private void PsoStatsFromState(bool bfSide, int idx)
    {
        bool mine = PsychOnline.PlaysAsBF == bfSide;
        var s = _mp[idx];
        if (mine)
        {
            if (idx == 0 && !_botplay) { s.Score = _score; s.Misses = _misses; s.Combo = _combo; s.MaxCombo = _maxCombo; s.Sick = _tallySick; s.Good = _tallyGood; s.Bad = _tallyBad; s.Shit = _tallyShit; s.Hits = _totalNotesHit; }
            if (idx == 0 && _botplay || idx == 1) { var p = PsychOnline.Self; if (p != null) PsoCopy(s, p); }
            return;
        }
        var other = PsychOnline.FirstOnSide(bfSide).p;
        if (other != null) PsoCopy(s, other);
    }

    private static void PsoCopy(MpStats s, SchemaObject p)
    {
        s.Name = p.Str("name") ?? s.Name;
        s.Score = (int)p.Num("score"); s.Misses = (int)p.Num("misses"); s.MaxCombo = (int)p.Num("maxCombo");
        s.Sick = (int)p.Num("sicks"); s.Good = (int)p.Num("goods"); s.Bad = (int)p.Num("bads"); s.Shit = (int)p.Num("shits");
        s.Hits = s.Sick + s.Good + s.Bad + s.Shit;
    }

    private void PsoLeaveMatch()
    {
        PsychOnline.Send("requestEndSong");   // LeavePie / PauseSubState: ends the song for the room
        Audio.StopMusic();
        Audio.StopVoices();
        Game.Scenes.ChangeScene(new PsychOnlineScene());
    }

    private void PsoEndSong()
    {
        if (!_psoEndSent)
        {
            _psoEndSent = true;
            PsychOnline.Send("updateSongFP", Math.Floor(_psoSongPoints));
            PsoSyncEnd();
            PsychOnline.Send("updateMaxCombo", _psoMaxCombo);
            PsychOnline.Send("playerEnded");
        }
        PsoStatsFromState(true, 0); PsoStatsFromState(false, 1);
        var res = new MpPlayerResult[2];
        for (int i = 0; i < 2; i++)
        {
            var s = _mp[i];
            res[i] = new MpPlayerResult
            {
                Name = s.Name, Score = s.Score, Misses = s.Misses, Sick = s.Sick, Good = s.Good, Bad = s.Bad, Shit = s.Shit, MaxCombo = s.MaxCombo,
                Hits = s.Hits, Total = s.Total, Accuracy = s.Total > 0 ? Math.Clamp(s.Hits / (float)s.Total, 0f, 1f) * 100f : 0f
            };
        }
        MultiplayerSession.LastResults = res;
        MultiplayerSession.LastSong = _chart?.DisplayName ?? _songName;
        Console.WriteLine($"[PSO] remote noteHit messages: {_psoRemoteMsgs}, shown as hits: {_psoRemoteHits}, unmatched: {_psoRemoteUnmatched}");
        Console.WriteLine($"[PSO] song end: P1 {res[0].Name} score={res[0].Score} misses={res[0].Misses} | P2 {res[1].Name} score={res[1].Score} misses={res[1].Misses}");
        Audio.StopVoices();
        Game.Scenes.ChangeScene(new PsoResultsScene());
    }
}
