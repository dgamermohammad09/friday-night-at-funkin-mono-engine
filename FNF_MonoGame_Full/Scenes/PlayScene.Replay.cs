using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;
using CP = FNF_MonoGame.Engine.PsychEngine.ClientPrefs;

namespace FNF_MonoGame.Scenes;

// Replays: record every note key press/release (with the judged song position) during a Freeplay play and play it back
// later through the normal input path (Input.NotePressed/Held/Released are fed from the file; hits are judged at the
// recorded position, so ratings are identical). Psych Online 'Disable Recording Replays' turns recording off; botplay,
// practice mode, practice seeks and multiplayer are never recorded (PSO: !isInvalidScore()).
public partial class PlayScene
{
    /// <summary>Set before creating a PlayScene to watch that replay instead of playing.</summary>
    public static ReplayData PendingReplay;
    private ReplayData _replay;              // playing back
    private ReplayData _rec;                 // recording
    private bool _replayInitDone, _recInvalid;
    private int _replayIdx;
    private readonly bool[] _replayHeld = new bool[4];
    private readonly double[] _replayLanePos = { double.NaN, double.NaN, double.NaN, double.NaN };
    private readonly double[] _replayRelPos = { double.NaN, double.NaN, double.NaN, double.NaN };
    private bool _replayEndLogged;
    private static float[] s_judgeOverride;  // replay: judgement settings of the recording

    private static readonly bool DevAutoPress = Environment.GetEnvironmentVariable("FNF_DEV_AUTOPRESS") == "1";
    private readonly bool[] _devHeld = new bool[4];
    private readonly double[] _devHoldUntil = new double[4];

    private float[] CurrentJudgeSettings() => new[]
    {
        CP.Data.SickWindow, CP.Data.GoodWindow, CP.Data.BadWindow, CP.Data.SafeFrames, CP.Data.RatingOffset,
        HighscoreManager.Data.GhostTapping ? 1f : 0f, (float)_globalOffset,
    };

    private void ReplayInit()
    {
        _replayInitDone = true;
        if (DevAutoPress) _botplay = false;
        s_judgeOverride = null;
        if (PendingReplay != null)
        {
            _replay = PendingReplay; PendingReplay = null;
            _replayPlaying = true; _botplay = false; _practiceMode = false;
            if (_replay.Judge != null && _replay.Judge.Length >= 7) s_judgeOverride = _replay.Judge;
            Console.WriteLine($"[REPLAY] playing {_replay.FilePath} ({_replay.Inputs?.Count ?? 0} inputs, recorded score {_replay.Score})");
            return;
        }
        if (CP.Data.DisableReplays || _botplay || _practiceMode || _mpActive || _mpPso || _weekId != null) return;
        _rec = new ReplayData
        {
            Song = _songName, SongTitle = _chart?.DisplayName ?? _songName, Difficulty = _currentDifficulty,
            Variation = _targetVariation, Instrumental = _targetInstrumental, ModPath = _modPath, PlaybackRate = JudgeRate,
            Player = CP.Data.Nickname ?? Environment.UserName, Judge = CurrentJudgeSettings(),
        };
    }

    /// <summary>ProcessNoteInput start (before botplay/key handling): feed or record the note keys.</summary>
    private void ReplayFrame(double pos)
    {
        if (!_replayInitDone) ReplayInit();
        if (_replay != null)
        {
            var inp = _replay.Inputs;
            for (int l = 0; l < 4; l++) { Input.NotePressed[l] = false; Input.NoteReleased[l] = false; _replayLanePos[l] = double.NaN; _replayRelPos[l] = double.NaN; }
            while (inp != null && _replayIdx < inp.Count && inp[_replayIdx][0] <= pos)
            {
                var e = inp[_replayIdx++];
                int lane = (int)e[1] & 3;
                if (e[2] >= 0.5) { Input.NotePressed[lane] = true; _replayHeld[lane] = true; _replayLanePos[lane] = e[0]; }
                else { Input.NoteReleased[lane] = true; _replayHeld[lane] = false; _replayRelPos[lane] = e[0]; }
            }
            for (int l = 0; l < 4; l++) Input.NoteHeld[l] = _replayHeld[l];
            return;
        }
        if (DevAutoPress && !_botplay) DevAutoPressFrame(pos);
        if (_rec != null && !_recInvalid && !_botplay)
            for (int l = 0; l < 4; l++)
            {
                if (Input.NoteReleased[l]) _rec.Inputs.Add(new[] { Math.Round(pos, 6), l, 0.0 });
                if (Input.NotePressed[l]) _rec.Inputs.Add(new[] { Math.Round(pos, 6), l, 1.0 });
            }
        if (_botplay || _practiceMode) _recInvalid = true;
    }

    /// <summary>Judged position of a press: the recorded one while replaying.</summary>
    private double ReplayPos(int lane, double pos) => _replay != null && !double.IsNaN(_replayLanePos[lane]) ? _replayLanePos[lane] : pos;

    /// <summary>Hold update position: the recorded release time on a replayed release frame.</summary>
    private double ReplayRelPos(int lane, double pos) => _replay != null && !double.IsNaN(_replayRelPos[lane]) ? _replayRelPos[lane] : pos;

    /// <summary>Dev hook FNF_DEV_AUTOPRESS=1: a fake human pressing the real keys -30..+30 ms around each note.</summary>
    private double[] _devPauseWin;
    private void DevAutoPressFrame(double pos)
    {
        // FNF_DEV_AUTOPRESS_PAUSE="from:to" (song s): the fake human stops pressing in that window (real misses / combo break)
        if (_devPauseWin == null) { var w = (Environment.GetEnvironmentVariable("FNF_DEV_AUTOPRESS_PAUSE") ?? "").Split(':'); _devPauseWin = w.Length == 2 && double.TryParse(w[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var a) && double.TryParse(w[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var b) ? new[] { a, b } : new double[0]; }
        if (_devPauseWin.Length == 2 && pos >= _devPauseWin[0] && pos < _devPauseWin[1])
        { for (int l = 0; l < 4; l++) { Input.NotePressed[l] = false; Input.NoteReleased[l] = _devHeld[l]; _devHeld[l] = false; Input.NoteHeld[l] = false; } return; }
        for (int l = 0; l < 4; l++)
        {
            bool realHeld = Input.NoteHeld[l], realPressed = Input.NotePressed[l];   // real keys / FNF_DEVPAD still count (ghost presses)
            Input.NotePressed[l] = false; Input.NoteReleased[l] = false;
            var note = _noteField.GetBotplayNote(l, pos + 0.2);
            double j = note == null ? 0 : ((((long)Math.Round(note.Time * 1000) * 7919) % 61) - 30) / 1000.0;
            bool due = note != null && pos >= note.Time + j;
            if (_devHeld[l] && (pos >= _devHoldUntil[l] || due))
            {
                _devHeld[l] = false; Input.NoteReleased[l] = true;
            }
            else if (due && !_devHeld[l])
            {
                _devHeld[l] = true; Input.NotePressed[l] = true;
                _devHoldUntil[l] = note.Time + Math.Max(note.SustainLength, 0.06);
            }
            Input.NoteHeld[l] = _devHeld[l] || realHeld;
            if (realPressed && !_devHeld[l]) Input.NotePressed[l] = true;
        }
    }

    /// <summary>CreateResultsScene: save the recording of a finished, valid play.</summary>
    private string ReplaySaveOnEnd()
    {
        if (_replay != null) { if (!_replayEndLogged) Console.WriteLine($"[REPLAY] playback finished: score {_score} misses {_misses} sick {_tallySick} good {_tallyGood} bad {_tallyBad} shit {_tallyShit} (recorded score {_replay.Score} misses {_replay.Misses} sick {_replay.Sicks} good {_replay.Goods} bad {_replay.Bads} shit {_replay.Shits})"); _replayEndLogged = true; return _replay.FilePath; }
        if (_rec == null || _recInvalid || _rec.Inputs.Count == 0) return null;
        _rec.Score = _score; _rec.Misses = _misses; _rec.MaxCombo = _maxCombo;
        _rec.Sicks = _tallySick; _rec.Goods = _tallyGood; _rec.Bads = _tallyBad; _rec.Shits = _tallyShit;
        int played = _tallySick + _tallyGood + _tallyBad + _tallyShit + _misses;
        _rec.Accuracy = played == 0 ? 0 : Math.Round(Math.Clamp((_tallySick + _tallyGood * 0.67 + _tallyBad * 0.34) / played, 0, 1) * 100, 2);
        _rec.Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        string p = ReplayManager.Save(_rec);
        _rec = null;
        return p;
    }

    /// <summary>Start watching a replay (Results / Freeplay replay list).</summary>
    public static PlayScene CreateReplayScene(ReplayData r)
    {
        if (r == null) return null;
        if (r.Inputs == null && r.FilePath != null) r = ReplayManager.Load(r.FilePath);
        if (r?.Inputs == null) return null;
        PendingReplay = r;
        return new PlayScene(new PlayStateParams
        {
            SongName = r.Song, TargetDifficulty = r.Difficulty, TargetVariation = r.Variation ?? "default",
            TargetInstrumental = r.Instrumental, ModPath = r.ModPath, PlaybackRate = r.PlaybackRate <= 0 ? 1f : r.PlaybackRate,
        });
    }

    private float _replayBadgeT;
    private void DrawReplayBadge(SpriteBatch sb)
    {
        if (_replay == null) return;
        _replayBadgeT += 1f / 60f;
        float a = 1f - MathF.Abs(MathF.Sin(_replayBadgeT * 1.6f)) * 0.6f;
        string who = string.IsNullOrEmpty(_replay.Player) ? "" : " - " + _replay.Player.ToUpperInvariant();
        DrawPsychText(sb, 32, "REPLAY" + who, 0f, _downscroll ? 720f * 0.11f + 60f : 720f * 0.89f - 90f, 1280f, Color.White, 1.25f, a);
        DrawPsychText(sb, 16, $"{_replay.Date}  RECORDED SCORE {_replay.Score}  MISSES {_replay.Misses}  {_replay.Accuracy:0.##}%", 0f,
            _downscroll ? 720f * 0.11f + 96f : 720f * 0.89f - 54f, 1280f, Color.White, 1f, a * 0.85f);
    }
}
