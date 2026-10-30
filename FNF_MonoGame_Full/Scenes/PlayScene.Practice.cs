using System;
using Microsoft.Xna.Framework.Input;
using CP = FNF_MonoGame.Engine.PsychEngine.ClientPrefs;

namespace FNF_MonoGame.Scenes;

// Practice tools (pause menu while Practice Mode is on, Psych PauseSubState 'Skip Time' + Gameplay Changers 'Playback Rate'):
//  Skip Time   - Left/Right moves the target (1 s per press, 10 s/s while held), Accept jumps there (forward or back) and
//                resumes (Psych setSongTime + clearNotesBefore).
//  Playback Rate - 0.5x .. 2x in 0.05 steps; music + vocals change speed AND pitch (FlxSound.pitch), judgement windows scale
//                with it (safeZoneOffset * playbackRate, noteDiff / playbackRate), the scene clock runs at the same rate.
//  Loop Section - OFF / 1..8 sections (4 beats each) starting at the section of the Skip Time target; when the song reaches
//                the end of the loop it jumps back to its start.
// PlayStateParams.PlaybackRate / StartTimestamp (ms) start a song directly at that rate / position.
// Psych Online lag detection (disableLagDetection off): on a >= 0.1 s frame with a player note near, Psych mod songs jump
// back 2 s once per 3 s ("Mod Lag Detected (-2s)").
public partial class PlayScene
{
    private bool _practiceRateApplied;
    private double _skipTarget = -1;
    private int _loopSections;
    private double _loopA = -1, _loopB = -1;
    private float _skipHeld;
    private double _lastLagPos = double.NegativeInfinity;
    private float _lagToastT;

    /// <summary>Scene clock multiplier (Psych FlxG.animationTimeScale + playbackRate-scaled lerps).</summary>
    private float SceneTimeScale => _paused || !_practiceRateApplied ? 1f : JudgeRate;

    /// <summary>StartSongPart2, right after the conductor starts: apply rate and start position.</summary>
    private void PracticeOnSongStart()
    {
        _practiceRateApplied = true;
        if (Math.Abs(JudgeRate - 1f) > 0.001f) Audio.PlaybackRate = JudgeRate;
        if (_startTimestamp > 0) PracticeSeek(_startTimestamp / 1000.0);
    }

    /// <summary>Jump the running song to t (song seconds): audio, conductor, notes, events, holds.</summary>
    private bool _recInvalidBySeek;
    private void PracticeSeek(double t, bool invalidate = true)
    {
        if (_conductor == null || _noteField == null) return;
        double len = Audio.MusicLength;
        if (len > 0) t = Math.Min(t, len - 0.5);
        t = Math.Max(0, t);
        double audioMs = Audio.MusicPosition + (t - _conductor.SongPosition) * 1000.0;
        bool back = t < _conductor.SongPosition;
        Audio.SeekSong(Math.Max(0, audioMs));
        _conductor.SetPosition(t);
        _noteField.SeekTo(t);
        for (int l = 0; l < 4; l++) { _holdingNote[l] = false; _holdNoteRef[l] = null; }
        if (back && _chart?.Events != null)
        {
            _nextEventIndex = 0;
            while (_nextEventIndex < _chart.Events.Count && _chart.Events[_nextEventIndex].Time < t) _nextEventIndex++;
            for (int i = _nextEventIndex; i < _chart.Events.Count; i++) _chart.Events[i].Fired = false;
        }
        _recInvalid = true; if (invalidate) _recInvalidBySeek = true;
        Console.WriteLine($"[PRACTICE] seek -> {t:F2}s (audio {audioMs:F0} ms) back={back}");
    }

    private void PracticeUpdate(float dt)
    {
        if (!_practiceRateApplied || _paused || _conductor == null) return;
        double pos = _conductor.SongPosition;
        if (DevPracLog && (int)pos != _pracLogSec) { _pracLogSec = (int)pos; Console.WriteLine($"[PRACLOG] song={pos:F3} audio={Audio.MusicPosition / 1000.0:F3} rate={Audio.PlaybackRate:F2} wall={System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency:F3} step={_conductor.CurrentStep} dec={Audio.VoicePosition / 1000.0:F3}"); }
        if (_loopSections > 0 && _loopB > _loopA && pos >= _loopB) PracticeSeek(_loopA);
        if (_lagToastT > 0) _lagToastT -= dt;
        // PSO lag detection (Psych mod charts only, never online / botplay test runs)
        if (!CP.Data.DisableLagDetection && !string.IsNullOrEmpty(_modPath) && !_mpActive && !_mpPso && _replay == null
            && _rawDt >= 0.1f && pos > _lastLagPos && Environment.GetEnvironmentVariable("FNF_SHOTS") == null
            && _noteField.HasPlayerNoteWithin(pos, 1.5))
        {
            PracticeSeek(pos - 2.0, invalidate: false);
            _lastLagPos = _conductor.SongPosition + 3.0;
            _lagToastT = 2f;
            Console.WriteLine("[PSO] Mod Lag Detected (-2s)");
        }
    }
    private float _rawDt;
    private int _pracLogSec = -1;
    private static readonly bool DevPracLog = Environment.GetEnvironmentVariable("FNF_PRACLOG") == "1";

    // ---- pause menu entries ----
    private static readonly string[] PracticeItems = { "Skip Time", "Playback Rate", "Loop Section" };
    private static bool IsPracticeItem(string s) => System.Array.IndexOf(PracticeItems, s) >= 0;

    private string PracticeLabel(string item)
    {
        switch (item)
        {
            case "Skip Time":
                if (_skipTarget < 0) _skipTarget = Math.Max(0, _conductor?.SongPosition ?? 0);
                return $"Skip Time {Fmt(_skipTarget)} of {Fmt(Audio.MusicLength)}";
            case "Playback Rate": return $"Playback Rate {JudgeRate:0.00}x";
            case "Loop Section": return _loopSections <= 0 ? "Loop Section Off" : $"Loop {_loopSections} Section{(_loopSections > 1 ? "s" : "")}";
        }
        return item;
        static string Fmt(double s) { int v = Math.Max(0, (int)s); return $"{v / 60}:{v % 60:00}"; }
    }

    /// <summary>Pause menu: refresh labels and handle Left/Right on a practice entry. True = input consumed.</summary>
    private bool PracticePauseInput(float dt)
    {
        if (_pauseItems == null) return false;
        for (int i = 0; i < _pmEntries.Count && i < _pauseItems.Length; i++)
            if (IsPracticeItem(_pauseItems[i])) _pmEntries[i].Text = PracticeLabel(_pauseItems[i]);
        if (_pmVolFocus || _pauseSelection < 0 || _pauseSelection >= _pauseItems.Length) return false;
        string item = _pauseItems[_pauseSelection];
        if (!IsPracticeItem(item)) return false;
        int dir = (Input.LeftPressed ? -1 : 0) + (Input.RightPressed ? 1 : 0);
        var ks = Keyboard.GetState();
        bool held = ks.IsKeyDown(Keys.Left) || ks.IsKeyDown(Keys.Right);
        int heldDir = ks.IsKeyDown(Keys.Left) ? -1 : ks.IsKeyDown(Keys.Right) ? 1 : 0;
        if (dir != 0) _skipHeld = 0f; else if (held) _skipHeld += dt; else _skipHeld = 0f;
        switch (item)
        {
            case "Skip Time":
                if (dir != 0) { _skipTarget += dir; Audio.PlaySound("scrollMenu", 0.4f); }
                else if (_skipHeld > 0.4f) _skipTarget += heldDir * 10.0 * dt;
                _skipTarget = Math.Clamp(_skipTarget, 0, Math.Max(0, Audio.MusicLength - 1));
                break;
            case "Playback Rate":
                if (dir != 0)
                {
                    _practiceRate = (float)Math.Round(Math.Clamp(_practiceRate + dir * 0.05f, 0.5f, 2f), 2);
                    Audio.PlaybackRate = _practiceRate;
                    Audio.PlaySound("scrollMenu", 0.4f);
                }
                break;
            case "Loop Section":
                if (dir != 0) { _loopSections = Math.Clamp(_loopSections + dir, 0, 8); Audio.PlaySound("scrollMenu", 0.4f); }
                break;
        }
        return dir != 0;
    }

    /// <summary>Accept on a practice entry (ExecutePauseMenuItem).</summary>
    private bool PracticeExecute(string item)
    {
        if (!IsPracticeItem(item)) return false;
        double from = _skipTarget >= 0 ? _skipTarget : _conductor.SongPosition;
        if (item == "Loop Section" && _loopSections > 0)
        {
            double secLen = _conductor.Crochet * 4.0;
            double a = Math.Floor(from / secLen) * secLen;
            _loopA = a; _loopB = a + secLen * _loopSections;
            Console.WriteLine($"[PRACTICE] loop {_loopA:F2}-{_loopB:F2}s");
        }
        else if (item == "Loop Section") { _loopA = _loopB = -1; }
        bool started = _practiceRateApplied && !_countdownActive;
        ExecutePauseMenuItem("Resume");
        if (started && (item == "Skip Time" || (item == "Loop Section" && _loopSections > 0)))
            PracticeSeek(item == "Loop Section" ? _loopA : from);
        _skipTarget = -1;
        return true;
    }

    /// <summary>Dev hooks: FNF_DEV_PRACTICE=1, FNF_DEV_RATE=1.5, FNF_DEV_START_MS=20000, FNF_DEV_LOOP="a,b" (song seconds).</summary>
    private void DevPracticeEnv()
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (Environment.GetEnvironmentVariable("FNF_DEV_PRACTICE") == "1") _practiceMode = true;
        if (float.TryParse(Environment.GetEnvironmentVariable("FNF_DEV_RATE"), System.Globalization.NumberStyles.Float, inv, out float r)) _practiceRate = Math.Clamp(r, 0.5f, 2f);
        if (float.TryParse(Environment.GetEnvironmentVariable("FNF_DEV_START_MS"), System.Globalization.NumberStyles.Float, inv, out float st)) _startTimestamp = st;
        var lp = Environment.GetEnvironmentVariable("FNF_DEV_LOOP")?.Split(',');
        if (lp != null && lp.Length == 2 && double.TryParse(lp[0], System.Globalization.NumberStyles.Float, inv, out double a) && double.TryParse(lp[1], System.Globalization.NumberStyles.Float, inv, out double b))
        { _loopA = a; _loopB = b; _loopSections = 1; }
    }
}
