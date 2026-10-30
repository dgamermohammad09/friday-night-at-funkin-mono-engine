using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Newtonsoft.Json.Linq;
using FNF_MonoGame.Engine;
using PNote = FNF_MonoGame.Engine.PsychEngine.Note;

namespace FNF_MonoGame.Scenes;

// Two-player gameplay (local + online). P1 = boyfriend strumline, P2 = opponent strumline.
//  Local:  P1 input = MultiplayerInput device 0, P2 input = device 1 (opponent notes are not auto-hit).
//  Online: host plays P1 for real (opponent side auto-played locally like the CPU); guest plays P2 for real
//          (its boyfriend side is auto-played). Each side sends its own hit/miss events + score stats; the
//          other machine shows them (miss animations, health, scores). Song start is synchronised (ready/start).
public partial class PlayScene
{
    private sealed class MpStats { public string Name = "P"; public int Score, Misses, Combo, MaxCombo, Sick, Good, Bad, Shit, Hits, Total; }
    private bool _mpActive, _mpLocal, _mpOnline, _mpOppHuman;
    private string _mpParamSong;
    private float _mpPingTimer;
    private readonly MpStats[] _mp = { new MpStats(), new MpStats() };
    private bool _mpWaitStart, _mpLocalReady, _mpPeerReady, _mpEnded;
    private float _mpWaitTimer, _mpStatsTimer;
    private string _mpLastStatsSent = "";
    private string _mpNotice; private float _mpNoticeTimer;
    private static readonly List<PNote> MpNoNotes = new List<PNote>();
    private bool MpActive => _mpActive;
    private bool MpBotDev => Environment.GetEnvironmentVariable("FNF_MP_BOT") == "1";   // dev: auto-play the local human side

    /// <summary>Called at the end of loading (before the countdown).</summary>
    private void MpOnLoaded()
    {
        var mode = MultiplayerSession.Mode;
        if (mode == MultiplayerSession.MpMode.None) return;
        if (mode == MultiplayerSession.MpMode.Psych) { PsoOnLoaded(); return; }
        _mpActive = true;
        _mpLocal = mode == MultiplayerSession.MpMode.Local;
        _mpOnline = mode == MultiplayerSession.MpMode.Online;
        _practiceMode = true;                       // no game over in versus (health is a tug of war)
        _mpOppHuman = _mpLocal || (_mpOnline && !MultiplayerSession.IsHost);
        _noteField.OpponentHuman = _mpOppHuman;
        if (_mpOnline && !MultiplayerSession.IsHost) _botplay = true;   // guest: boyfriend side mirrored from the host
        if (MpBotDev && (_mpLocal || MultiplayerSession.IsHost)) _botplay = true;   // dev: auto-play P1 too
        foreach (var n in _chart.Notes) { if (n.IsPlayerNote) _mp[0].Total++; else _mp[1].Total++; }
        if (_mpLocal) { _mp[0].Name = MultiplayerInput.Name(MultiplayerInput.Devices[0]); _mp[1].Name = MultiplayerInput.Name(MultiplayerInput.Devices[1]); }
        else
        {
            string me = MultiplayerSession.LocalName, peer = MultiplayerSession.PeerName ?? "Opponent";
            _mp[0].Name = MultiplayerSession.IsHost ? me : peer;
            _mp[1].Name = MultiplayerSession.IsHost ? peer : me;
            _mpWaitStart = true; _mpLocalReady = true;
            if (MultiplayerSession.IsHost)   // tell the guest which song to load (same id/difficulty/character variation)
                MultiplayerSession.Send(new JObject { ["t"] = "song", ["song"] = _mpParamSong, ["diff"] = _currentDifficulty,
                    ["char"] = Environment.GetEnvironmentVariable("FNF_FREEPLAY_CHAR") ?? HighscoreManager.Data.SelectedCharacter });
            MultiplayerSession.Send(new JObject { ["t"] = "ready", ["song"] = _songName });
        }
        Console.WriteLine($"[MP] gameplay mode={mode} host={MultiplayerSession.IsHost} oppHuman={_mpOppHuman} botplay={_botplay} totals={_mp[0].Total}/{_mp[1].Total}");
    }

    /// <summary>Online: hold the countdown until both players have loaded. Returns true while waiting.</summary>
    private bool MpHoldStart()
    {
        if (_mpPso) return PsoHoldStart();
        if (!_mpWaitStart) return false;
        _mpWaitTimer += _lastDelta;
        if (MultiplayerSession.IsHost && _mpLocalReady && _mpPeerReady)
        {
            MultiplayerSession.Send(new JObject { ["t"] = "start" });
            _mpWaitStart = false;
            Console.WriteLine("[MP] both ready -> start");
        }
        if (_mpWaitTimer > 3f && MultiplayerSession.Online) // re-announce every 3 s (a message may have been consumed by a menu)
        {
            _mpWaitTimer = 0f;
            MultiplayerSession.Send(new JObject { ["t"] = "ready", ["song"] = _songName, ["again"] = true });
        }
        if (!MultiplayerSession.Online) _mpWaitStart = false;  // connection lost: play alone
        return _mpWaitStart;
    }

    /// <summary>Per-frame: network messages, local input routing. Called at the start of the gameplay update.</summary>
    private void MpUpdate(float dt)
    {
        if (!_mpActive) return;
        MultiplayerInput.Frame++;
        if (_mpNoticeTimer > 0) _mpNoticeTimer -= dt;
        if (_mpPso) { PsoUpdate(dt); return; }
        if (_mpOnline) MpPoll();
        if (float.TryParse(Environment.GetEnvironmentVariable("FNF_MP_END_AT"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var endAt)
            && !_countdownActive && _conductor.SongPosition > endAt) _forceEndSong = true;   // dev: short test matches
        if (_mpOnline && (_mpPingTimer += dt) > 2f) { _mpPingTimer = 0; MultiplayerSession.Send(new JObject { ["t"] = "ping", ["ts"] = MultiplayerSession.NowMs() }); }
        if (_mpOnline)
        {
            _mpStatsTimer += dt;
            int side = MultiplayerSession.LocalSide;
            var s = side == 0 ? MpP1Live() : _mp[1];
            string key = $"{s.Score}|{s.Misses}|{s.Combo}|{s.Hits}";
            if (key != _mpLastStatsSent || _mpStatsTimer > 2f)
            {
                _mpLastStatsSent = key; _mpStatsTimer = 0;
                MultiplayerSession.Send(MpStatsJson(side, s));
            }
        }
    }

    private MpStats MpP1Live()
    {
        var s = _mp[0];
        if (_mpOnline && !MultiplayerSession.IsHost) return s;     // guest: P1 numbers come from the host
        if (MpBotDev && _botplay) return s;                        // dev auto-play: counted in MpHookP1Hit
        s.Score = _score; s.Misses = _misses; s.Combo = _combo; s.MaxCombo = _maxCombo;
        s.Sick = _tallySick; s.Good = _tallyGood; s.Bad = _tallyBad; s.Shit = _tallyShit; s.Hits = _totalNotesHit;
        return s;
    }

    private static JObject MpStatsJson(int side, MpStats s) => new JObject
    {
        ["t"] = "stats", ["side"] = side, ["score"] = s.Score, ["misses"] = s.Misses, ["combo"] = s.Combo, ["maxCombo"] = s.MaxCombo,
        ["sick"] = s.Sick, ["good"] = s.Good, ["bad"] = s.Bad, ["shit"] = s.Shit, ["hits"] = s.Hits
    };

    private void MpPoll()
    {
        var net = MultiplayerSession.Net;
        if (net == null) return;
        while (net.Inbox.TryDequeue(out var m))
        {
            string t = (string)m["t"];
            switch (t)
            {
                case "ready": _mpPeerReady = true; if (MultiplayerSession.IsHost && !_mpWaitStart && (bool?)m["again"] == true) MultiplayerSession.Send(new JObject { ["t"] = "start" }); break;
                case "start": if (!MultiplayerSession.IsHost) { _mpWaitStart = false; Console.WriteLine("[MP] start received"); } break;
                case "stats":
                {
                    int side = (int?)m["side"] ?? 0;
                    if (side == MultiplayerSession.LocalSide) break;
                    var s = _mp[side];
                    s.Score = (int?)m["score"] ?? 0; s.Misses = (int?)m["misses"] ?? 0; s.Combo = (int?)m["combo"] ?? 0; s.MaxCombo = (int?)m["maxCombo"] ?? 0;
                    s.Sick = (int?)m["sick"] ?? 0; s.Good = (int?)m["good"] ?? 0; s.Bad = (int?)m["bad"] ?? 0; s.Shit = (int?)m["shit"] ?? 0; s.Hits = (int?)m["hits"] ?? 0;
                    break;
                }
                case "ev":
                {
                    int side = (int?)m["side"] ?? 0, lane = (int?)m["lane"] ?? 0;
                    bool miss = (string)m["kind"] == "miss";
                    if (side == MultiplayerSession.LocalSide) break;
                    if (side == 0) { if (miss) { _boyfriend?.Miss(lane); _health -= 0.04f; } else _health += 0.02f; }
                    else { if (miss) { _opponent?.Miss(lane); _health += 0.04f; } else _health -= 0.02f; }
                    _health = Math.Clamp(_health, 0.01f, HEALTH_MAX);
                    break;
                }
                case "leave_match": MpNotice((MultiplayerSession.PeerName ?? "Opponent") + " left the match"); _mpWaitStart = false; break;
                case "peer_left": case "leave": case "disconnected":
                    MultiplayerSession.PeerPresent = false;
                    MpNotice((MultiplayerSession.PeerName ?? "Opponent") + " left the match");
                    _mpWaitStart = false;
                    break;
            }
        }
    }

    private void MpNotice(string s) { _mpNotice = s; _mpNoticeTimer = 4f; Console.WriteLine("[MP] " + s); }

    /// <summary>Before ProcessNoteInput: local versus routes P1's device into the regular input arrays.</summary>
    private void MpBeforeNoteInput()
    {
        if (!_mpActive) return;
        if (_mpLocal)
        {
            MultiplayerInput.UpdateNotes();
            for (int l = 0; l < 4; l++)
            {
                Input.NotePressed[l] = MultiplayerInput.Pressed[0, l];
                Input.NoteHeld[l] = MultiplayerInput.Held[0, l];
                Input.NoteReleased[l] = MultiplayerInput.Released[0, l];
            }
        }
        if (_mpOppHuman) MpProcessOpponentHuman();
    }

    /// <summary>The opponent strumline played by a human (local P2 or the online guest).</summary>
    private void MpProcessOpponentHuman()
    {
        double pos = _conductor.SongPosition + (_globalOffset / 1000.0);
        bool bot = MpBotDev && (_mpLocal || !MultiplayerSession.IsHost);
        for (int lane = 0; lane < 4; lane++)
        {
            bool pressed = _mpLocal ? MultiplayerInput.Pressed[1, lane] : Input.NotePressed[lane];
            PNote note = null;
            if (bot)
            {
                foreach (var n in _noteField.GetAllActiveNotes())
                    if (!n.IsPlayerNote && n.Lane == lane && !n.IsHit && !n.Missed && pos >= n.Time) { note = n; break; }
            }
            else if (pressed) note = _noteField.GetHittableOpponentNote(lane, pos);
            if (note == null) continue;
            float diff = Math.Abs((float)(note.Time - pos));
            string rating = GetRating(bot ? 0f : diff);
            int sc = GetScoreValue(bot ? 0f : diff);
            _opponent.Sing(note.Lane, note.Kind);
            DispatchOpponentNoteHit(note);
            _opponentConfirmTimer[note.Lane] = 0.15f; VsStrumHit(false, note.Lane, note);
            _noteField.RemoveNote(note);
            var s = _mp[1];
            s.Score += sc; s.Hits++; s.Combo++; if (s.Combo > s.MaxCombo) s.MaxCombo = s.Combo;
            string r = rating.ToUpperInvariant();
            if (r.Contains("SICK") || r.Contains("KILLER")) s.Sick++; else if (r.Contains("GOOD")) s.Good++; else if (r.Contains("BAD")) s.Bad++; else s.Shit++;
            _health = Math.Clamp(_health - 0.02f, 0.01f, HEALTH_MAX);
            if (_mpOnline) MultiplayerSession.Send(new JObject { ["t"] = "ev", ["side"] = 1, ["kind"] = "hit", ["lane"] = lane, ["rating"] = rating });
            if (_mpPso) PsoSendHit(note.Time, lane, note.SustainLength, rating, sc, false);
        }
        // opponent notes that left the hit window
        foreach (var n in _noteField.OpponentMissed)
        {
            var s = _mp[1]; s.Misses++; s.Combo = 0;
            _opponent?.Miss(n.Lane);
            _health = Math.Clamp(_health + 0.04f, 0.01f, HEALTH_MAX);
            if (_mpOnline) MultiplayerSession.Send(new JObject { ["t"] = "ev", ["side"] = 1, ["kind"] = "miss", ["lane"] = n.Lane });
            if (_mpPso) PsoSendMiss(n.Lane, n.Time, false);
        }
        _noteField.OpponentMissed.Clear();
    }

    private List<PNote> MpOpponentNotes(List<PNote> cpu) => _mpOppHuman || _psoRemoteOpp ? MpNoNotes : cpu;

    /// <summary>Hook at the start of OnNoteHit (boyfriend side).</summary>
    private void MpHookP1Hit(int lane, string rating, bool botplayHit)
    {
        if (botplayHit && MpBotDev && (_mpLocal || MultiplayerSession.IsHost))
        {   // dev auto-play: count the bot's hits as P1 hits so the versus flow can be tested end to end
            var s = _mp[0]; s.Score += 350; s.Hits++; s.Sick++; s.Combo++; if (s.Combo > s.MaxCombo) s.MaxCombo = s.Combo;
            botplayHit = false;
        }
        if (!_mpOnline || botplayHit || !MultiplayerSession.IsHost) return;
        MultiplayerSession.Send(new JObject { ["t"] = "ev", ["side"] = 0, ["kind"] = "hit", ["lane"] = lane, ["rating"] = rating });
    }

    /// <summary>Hook at the start of OnNoteMiss (boyfriend side).</summary>
    private void MpHookP1Miss(int lane)
    {
        if (!_mpOnline || !MultiplayerSession.IsHost) return;
        MultiplayerSession.Send(new JObject { ["t"] = "ev", ["side"] = 0, ["kind"] = "miss", ["lane"] = lane });
    }

    /// <summary>Online: Escape / Start leaves the match (no pause: the other player keeps playing).</summary>
    private bool MpHandlePause(bool pausePressed, bool enterPressed = false)
    {
        // online: Psych Online PauseSubState ['Resume', 'Exit to lobby'] without pausing (PlayScene.OnlinePause.cs);
        // the press itself used to leave the match at once
        if (_mpPso) return OnlinePauseUpdate(pausePressed, enterPressed, PsoLeaveMatch);
        if (!_mpOnline) return false;
        return OnlinePauseUpdate(pausePressed, enterPressed, () =>
        {
            MultiplayerSession.Send(new JObject { ["t"] = "leave_match" });
            Audio.StopMusic();
            Game.Scenes.ChangeScene(new MultiplayerScene());
        });
    }

    /// <summary>Song finished: show the versus results for both players. Returns true if handled.</summary>
    private bool MpEndSong()
    {
        if (!_mpActive) return false;
        if (_mpEnded) return true;
        _mpEnded = true;
        if (_mpPso) { PsoEndSong(); return true; }
        MpP1Live();
        if (_mpOnline) MultiplayerSession.Send(MpStatsJson(MultiplayerSession.LocalSide, MultiplayerSession.LocalSide == 0 ? _mp[0] : _mp[1]));
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
        Console.WriteLine($"[MP] song end: P1 {res[0].Name} score={res[0].Score} misses={res[0].Misses} | P2 {res[1].Name} score={res[1].Score} misses={res[1].Misses}");
        Audio.StopVoices();
        Game.Scenes.ChangeScene(new MultiplayerResultsScene());
        return true;
    }

    private void MpDrawHud(SpriteBatch sb, float bgX, float bgY, float bgW)
    {
        var p1 = MpP1Live();
        string left = $"{_mp[1].Name}: {_mp[1].Score:#,##0} | Miss {_mp[1].Misses}";
        string right = $"{p1.Name}: {p1.Score:#,##0} | Miss {p1.Misses}";
        if (_mpPso && PsychOnline.State != null)
        {   // Psych Online score text (PlayState 6907): name: score | misses M | acc% - FC | FP | ping
            bool fp = FNF_MonoGame.Engine.PsychEngine.ClientPrefs.Data.ShowFP;
            left = PsoScoreLine(PsychOnline.FirstOnSide(false).p, fp) ?? left; right = PsoScoreLine(PsychOnline.FirstOnSide(true).p, fp) ?? right;
        }
        DrawFlxOutlinedText(sb, 16, left, new Vector2(bgX, bgY + 30), new Color(0xFF, 0x90, 0x90), Color.Black);
        DrawFlxOutlinedText(sb, 16, right, new Vector2(bgX + bgW - 190, bgY + 30), new Color(0x90, 0xFF, 0x90), Color.Black);
        if (_mpPso) PsoDrawHud(sb, bgX, bgY, bgW);
        string center = null;
        if (_mpWaitStart) center = "Waiting for " + (MultiplayerSession.PeerName ?? "the other player") + "...";
        else if (_mpNoticeTimer > 0) center = _mpNotice;
        if (center != null)
        {
            var f = Assets.GetFont(28);
            if (f != null)
            {
                var size = f.MeasureString(center);
                var p = new Vector2((FNFGame.SCREEN_WIDTH - size.X) / 2f, FNFGame.SCREEN_HEIGHT * 0.45f);
                sb.Draw(Assets.Pixel, new Rectangle((int)p.X - 16, (int)p.Y - 8, (int)size.X + 32, (int)size.Y + 16), Color.Black * 0.6f);
                f.DrawText(sb, center, p, Color.White);
            }
        }
        if (_mpOnline && MultiplayerSession.Net != null && MultiplayerSession.Net.PingMs >= 0)
        {
            var f = Assets.GetFont(14);
            f?.DrawText(sb, $"ping {MultiplayerSession.Net.PingMs} ms", new Vector2(8, FNFGame.SCREEN_HEIGHT - 20), Color.White * 0.7f);
        }
    }
}
