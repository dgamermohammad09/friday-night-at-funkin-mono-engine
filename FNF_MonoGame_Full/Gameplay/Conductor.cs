namespace FNF_MonoGame.Gameplay;

/// <summary>
/// Handles song timing and beat synchronization
/// Syncs with audio playback position to prevent drift
/// </summary>
public class Conductor
{
    public float BPM { get; private set; }
    public double SongPosition { get; private set; }
    public int CurrentBeat { get; private set; }
    public int CurrentStep { get; private set; }
    public bool Playing { get; private set; }
    
    // Timing values in seconds
    public double Crochet => 60.0 / BPM;           // Beat length
    public double StepCrochet => Crochet / 4.0;    // Step length (1/4 beat)
    
    // Events
    public event Action<int> OnBeat;
    public event Action<int> OnStep;
    /// <summary>Fire stepHit/beatHit for steps skipped by a frame hitch (Psych mods: scripts key on exact
    /// curStep values; native Psych never skips at 60 fps, the port's hitches made VS Majin miss step 405).</summary>
    public bool CatchUpSkipped { get; set; }
    /// <summary>Psych mode: MusicBeatState.update computes curStep from the position of the PREVIOUS frame, so stepHit/beatHit
    /// for a boundary crossed by this frame's advance run in the next frame (after onUpdate). When set, beat/step events
    /// are queued and fired by FlushDeferred().</summary>
    public bool DeferEvents { get; set; }
    private readonly System.Collections.Generic.List<(bool Beat, int V)> _deferred = new System.Collections.Generic.List<(bool, int)>();
    private void FireBeat(int b) { if (DeferEvents) _deferred.Add((true, b)); else OnBeat?.Invoke(b); }
    private void FireStep(int st) { if (DeferEvents) _deferred.Add((false, st)); else OnStep?.Invoke(st); }
    public void FlushDeferred()
    {
        if (_deferred.Count == 0) return;
        var q = _deferred.ToArray(); _deferred.Clear();
        foreach (var e in q) { if (e.Beat) OnBeat?.Invoke(e.V); else OnStep?.Invoke(e.V); }
    }
    public void ClearDeferred() => _deferred.Clear();
    
    private int _lastBeat = -1;
    private int _lastStep = -1;
    
    // Audio sync
    private Func<double> _getAudioPosition;
    private double _offset;
    
    // BPM changes list: (time in seconds, new BPM)
    private List<(double Time, float Bpm)> _bpmChanges = new();
    private int _currentBpmIndex = 0;
    // Accumulated beat/step count at the start of the current BPM section
    private int _sectionStartBeat = 0;
    private int _sectionStartStep = 0;
    private double _sectionStartTime = 0;
    
    public Conductor(float bpm)
    {
        BPM = bpm;
        SongPosition = 0;
        CurrentBeat = 0;
        CurrentStep = 0;
        Playing = false;
    }
    
    /// <summary>
    /// Set a function that returns the current audio playback position in seconds.
    /// Used to sync conductor timing with actual audio.
    /// </summary>
    public void SetAudioSync(Func<double> getAudioPositionMs, double offsetSeconds = 0)
    {
        _getAudioPosition = getAudioPositionMs;
        _offset = offsetSeconds;
    }
    
    public void Start()
    {
        Playing = true;
        SongPosition = 0;
        _lastBeat = -1;
        _lastStep = -1;
    }
    
    public void Stop()
    {
        Playing = false;
    }
    
    public void Pause()
    {
        Playing = false;
    }
    
    public void Resume()
    {
        Playing = true;
    }
    
    public void SetPosition(double position)
    {
        SongPosition = position;
        // Recalculate BPM section for this position (handles forward and backward seeking)
        RecalculateSectionForTime(position);
    }
    
    /// <summary>
    /// Recalculates the current BPM section data for an arbitrary time position.
    /// Handles both forward and backward seeks.
    /// </summary>
    private void RecalculateSectionForTime(double time) => UpdateBeatAndStep(time);

    /// <summary>V-Slice Conductor.update: the current time change is the last one with timeStamp &lt;= songPosition;
    /// stepTime = roundDecimal(beatTime * 4 + (songPosition - timeStamp) / stepLength, 6); beat/step = floor.
    /// Time-change beatTime comes from Conductor.mapTimeChanges (continuous, roundDecimal 4) - a time change that
    /// falls between two beats (e.g. spaghetti b = 28.4272) does NOT restart the beat grid.</summary>
    private void UpdateBeatAndStep(double pos)
    {
        _currentBpmIndex = 0;
        double tcTime = 0, tcBeat = 0;
        if (_bpmChanges.Count > 0)
        {
            BPM = _bpmChanges[0].Bpm;
            tcTime = _bpmChanges[0].Time; tcBeat = 0;
            if (pos > 0)
            {
                for (int i = 1; i < _bpmChanges.Count; i++)
                {
                    if (pos < _bpmChanges[i].Time) break;
                    double prevBpm = _bpmChanges[i - 1].Bpm;
                    double bt = tcBeat + (_bpmChanges[i].Time - _bpmChanges[i - 1].Time) * prevBpm / 60.0;
                    tcBeat = Math.Round(bt * 10000.0, MidpointRounding.AwayFromZero) / 10000.0; // FlxMath.roundDecimal(.., 4)
                    tcTime = _bpmChanges[i].Time;
                    BPM = _bpmChanges[i].Bpm;
                    _currentBpmIndex = i;
                }
            }
        }
        _sectionStartTime = tcTime;
        if (pos > 0)
        {
            double stepTime = tcBeat * 4.0 + (pos - tcTime) / StepCrochet;
            stepTime = Math.Round(stepTime * 1e6, MidpointRounding.AwayFromZero) / 1e6;
            CurrentStep = (int)Math.Floor(stepTime);
            CurrentBeat = (int)Math.Floor(stepTime / 4.0);
            _sectionStartBeat = (int)Math.Floor(tcBeat); _sectionStartStep = (int)Math.Floor(tcBeat * 4.0);
        }
        else
        {
            double elapsed = pos - tcTime;
            CurrentBeat = (int)(elapsed / Crochet);
            CurrentStep = (int)(elapsed / StepCrochet);
        }
    }

    public void ChangeBPM(float newBpm)
    {
        BPM = newBpm;
    }
    
    /// <summary>
    /// Set BPM changes from chart data. Each entry is (timeSeconds, newBPM).
    /// </summary>
    public void SetBPMChanges(List<(double Time, float Bpm)> changes)
    {
        _bpmChanges = changes ?? new();
        _currentBpmIndex = 0;
        _sectionStartBeat = 0;
        _sectionStartStep = 0;
        _sectionStartTime = 0;
    }
    
    public void Update(float deltaTime)
    {
        if (!Playing) return;
        
        // Sync with audio playback timer (now Stopwatch-based, smooth and monotonic)
        if (_getAudioPosition != null)
        {
            double audioPos = _getAudioPosition() / 1000.0 + _offset; // Convert ms to seconds
            if (audioPos > 0)
            {
                SongPosition = audioPos;
            }
            else
            {
                SongPosition += deltaTime;
            }
        }
        else
        {
            SongPosition += deltaTime;
        }
        
        // Process BPM changes — accumulate beats at each boundary
        UpdateBeatAndStep(SongPosition);
        if (CurrentBeat != _lastBeat)
        {
            int prevBeat = _lastBeat;
            _lastBeat = CurrentBeat;
            if (CatchUpSkipped && CurrentBeat > prevBeat + 1 && CurrentBeat - prevBeat < 32 && prevBeat >= 0)
                for (int b = prevBeat + 1; b < CurrentBeat; b++) FireBeat(b);
            FireBeat(CurrentBeat);
        }
        
        // Fire step event
        if (CurrentStep != _lastStep)
        {
            int prevStep = _lastStep;
            _lastStep = CurrentStep;
            if (CatchUpSkipped && CurrentStep > prevStep + 1 && CurrentStep - prevStep < 128 && prevStep >= 0)
                for (int st = prevStep + 1; st < CurrentStep; st++) FireStep(st);
            FireStep(CurrentStep);
        }
    }
    
    /// <summary>
    /// Get a value that bounces on beat (for animations).
    /// Uses section-relative time for correct behavior with BPM changes.
    /// </summary>
    public float GetBeatBounce(float intensity = 0.1f)
    {
        double sectionElapsed = SongPosition - _sectionStartTime;
        double beatProgress = (sectionElapsed % Crochet) / Crochet;
        if (beatProgress < 0) beatProgress += 1.0;
        return 1f + (float)(Math.Pow(1 - beatProgress, 2) * intensity);
    }
    
    /// <summary>
    /// Convert a beat number to time in seconds.
    /// Accounts for BPM changes if available.
    /// </summary>
    public double BeatToTime(int beat)
    {
        if (_bpmChanges.Count <= 1)
            return beat * Crochet;
        
        // Walk through BPM sections to find the correct time
        int accumulatedBeats = 0;
        double accumulatedTime = _bpmChanges[0].Time;
        float currentBpm = _bpmChanges[0].Bpm;
        
        for (int i = 1; i < _bpmChanges.Count; i++)
        {
            double sectionCrochet = 60.0 / currentBpm;
            double sectionDuration = _bpmChanges[i].Time - accumulatedTime;
            int sectionBeats = (int)(sectionDuration / sectionCrochet);
            
            if (accumulatedBeats + sectionBeats >= beat)
            {
                // Target beat is in this section
                return accumulatedTime + (beat - accumulatedBeats) * sectionCrochet;
            }
            
            accumulatedBeats += sectionBeats;
            accumulatedTime = _bpmChanges[i].Time;
            currentBpm = _bpmChanges[i].Bpm;
        }
        
        // Target beat is beyond all BPM changes — use final BPM
        double finalCrochet = 60.0 / currentBpm;
        return accumulatedTime + (beat - accumulatedBeats) * finalCrochet;
    }
    
    /// <summary>
    /// Convert time in seconds to beat number.
    /// Accounts for BPM changes if available.
    /// </summary>
    public int TimeToBeat(double time)
    {
        if (_bpmChanges.Count <= 1)
            return (int)(time / Crochet);
        
        int accumulatedBeats = 0;
        double accumulatedTime = _bpmChanges[0].Time;
        float currentBpm = _bpmChanges[0].Bpm;
        
        for (int i = 1; i < _bpmChanges.Count; i++)
        {
            if (_bpmChanges[i].Time > time) break;
            
            double sectionCrochet = 60.0 / currentBpm;
            double sectionDuration = _bpmChanges[i].Time - accumulatedTime;
            accumulatedBeats += (int)(sectionDuration / sectionCrochet);
            accumulatedTime = _bpmChanges[i].Time;
            currentBpm = _bpmChanges[i].Bpm;
        }
        
        double remaining = time - accumulatedTime;
        double finalCrochet = 60.0 / currentBpm;
        return accumulatedBeats + (int)(remaining / finalCrochet);
    }
}
