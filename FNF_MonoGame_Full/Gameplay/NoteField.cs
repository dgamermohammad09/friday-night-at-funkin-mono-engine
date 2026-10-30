using PNote = FNF_MonoGame.Engine.PsychEngine.Note;

namespace FNF_MonoGame.Gameplay;

/// <summary>
/// Manages the note field and note hit detection
/// </summary>
public class NoteField
{
    private readonly List<PNote> _activeNotes = new(128);
    private readonly Chart _chart;
    private readonly Conductor _conductor;
    
    // Timing windows in seconds (matches FNF PBOT1 scoring system)
    public const float KILLER_WINDOW = 0.0125f;  // 12.5ms
    public const float SICK_WINDOW = 0.045f;     // 45ms
    public const float GOOD_WINDOW = 0.090f;     // 90ms
    public const float BAD_WINDOW = 0.135f;      // 135ms
    public const float SHIT_WINDOW = 0.160f;     // 160ms ? same as miss threshold
    public const float MISS_WINDOW = 0.160f;     // 160ms ? PBOT1_MISS_THRESHOLD
    /// <summary>Active hit/miss window (song seconds): PlayScene.ApplyHitWindowPrefs (Psych Safe Frames + playback rate).</summary>
    public static float HitWindowS = 0.160f;
    
    // How far ahead to load notes (seconds)
    private const float SPAWN_DISTANCE = 2.0f;
    /// <summary>Look-ahead (s) for spawning/drawing notes. V-Slice strumline sets Strumline.renderDistanceMs
    /// (FlxG.height / PIXELS_PER_MS / min(scrollSpeed, 1)); legacy renderers keep 2 s.</summary>
    public double RenderAheadSec = SPAWN_DISTANCE;
    
    private int _nextNoteIndex = 0;
    
    // PNote object pool to avoid GC allocations
    private readonly Queue<PNote> _notePool = new(128);
    
    public NoteField(Chart chart, Conductor conductor)
    {
        _chart = chart;
        _conductor = conductor;
        // Pre-allocate pool
        for (int i = 0; i < 64; i++)
            _notePool.Enqueue(new PNote());
    }
    
    private PNote RentNote()
    {
        if (_notePool.Count > 0)
        {
            var note = _notePool.Dequeue();
            note.Reset();
            return note;
        }
        return new PNote();
    }
    
    private void ReturnNote(PNote note)
    {
        _notePool.Enqueue(note);
    }
    
    // order-preserving remove (was O(1) swap-remove)
    private void SwapRemoveAt(int index)
    {
        // Keep spawn (chart) order: the original iterates strumline.notes in spawn order, so for a chord the LAST
        // chart note sets the sing pose (fresh erect 6000 ms LEFT+UP -> singUP). A swap-remove reordered chords.
        _activeNotes.RemoveAt(index);
    }
    
    // Reusable lists to avoid per-frame allocations (reduces GC pressure)
    private readonly List<PNote> _missedNotesBuffer = new();
    private readonly List<PNote> _visibleNotesBuffer = new(64);
    private readonly List<PNote> _opponentNotesBuffer = new();
    private readonly List<PNote> _sustainNotesBuffer = new();
    
    /// <summary>
    /// Update the note field, returns any notes that were missed.
    /// Caller must call ReturnMissedNotes() after processing the returned list.
    /// </summary>
    /// <summary>Bot play: player notes are never flagged missed (PlayScene hits every due note).</summary>
    public bool BotPlay;

    /// <summary>Multiplayer: opponent notes are hit by a player (no auto-hit; misses collected in OpponentMissed).</summary>
    public bool OpponentHuman;
    /// <summary>Psych Online: the boyfriend strumline belongs to the remote player (hit from its messages, never auto-hit;
    /// unhit notes pass silently).</summary>
    public bool RemotePlayer;
    public readonly List<PNote> OpponentMissed = new();

    /// <summary>Practice seek: drop every spawned note and continue spawning from the first chart note at/after t.</summary>
    public void SeekTo(double t)
    {
        for (int i = 0; i < _activeNotes.Count; i++) ReturnNote(_activeNotes[i]);
        _activeNotes.Clear(); _missedNotesBuffer.Clear();
        _nextNoteIndex = 0;
        while (_nextNoteIndex < _chart.Notes.Count && _chart.Notes[_nextNoteIndex].Time < t) _nextNoteIndex++;
    }

    /// <summary>PSO isPlayNoteNear: an unhit player note within dist seconds ahead.</summary>
    public bool HasPlayerNoteWithin(double t, double dist)
    {
        for (int i = 0; i < _activeNotes.Count; i++) { var n = _activeNotes[i]; if (n.IsPlayerNote && !n.IsHit && !n.Missed && n.Time - t < dist) return true; }
        return false;
    }

    public PNote GetHittableOpponentNote(int lane, double currentTime)
    {
        PNote best = null; double bestDiff = double.MaxValue;
        for (int i = 0; i < _activeNotes.Count; i++)
        {
            var note = _activeNotes[i];
            if (note.Lane == lane && !note.IsPlayerNote && !note.IsHit && !note.Missed)
            {
                double d = Math.Abs(currentTime - note.Time);
                if (d <= HitWindowS && d < bestDiff) { best = note; bestDiff = d; }
            }
        }
        return best;
    }

    public List<PNote> Update(float deltaTime)
    {
        _missedNotesBuffer.Clear();
        double currentTime = _conductor.SongPosition;
        
        // Spawn upcoming notes
        SpawnUpcomingNotes(currentTime);
        
        // Check for missed/expired notes ? iterate backwards for safe swap-removal
        for (int i = _activeNotes.Count - 1; i >= 0; i--)
        {
            var note = _activeNotes[i];
            
            if (note.IsPlayerNote && note.Missed)
            {
                // Original Strumline.updateNotes: a missed note keeps scrolling past the strumline (full alpha,
                // trail unclipped) and is killed once offscreen; its hold trail lives until
                // renderWindowEnd = strumTime + fullSustainLength + HIT_WINDOW_MS + renderDistanceMs / 8.
                double renderDistance = 720.0 / 0.45 / 1000.0 / (ScrollSpeed < 1f ? ScrollSpeed : 1f);
                double headGone = note.Time + (720.0 + 200.0) / (450.0 * ScrollSpeed);
                double trailGone = note.Time + note.SustainLength + MISS_WINDOW + renderDistance / 8.0;
                if (currentTime > Math.Max(headGone, note.SustainLength > 0 ? trailGone : 0))
                {
                    SwapRemoveAt(i);
                    ReturnNote(note);
                }
            }
            else if (note.IsPlayerNote && !note.IsHit)
            {
                double timeDiff = currentTime - note.Time;
                
                if (RemotePlayer)
                {
                    if (timeDiff > HitWindowS) note.Missed = true;   // scrolls away, no local miss
                }
                else if (timeDiff > HitWindowS && note.IgnoreNote)
                {
                    note.Missed = true; // Psych: ignoreNote notes (hurt notes) pass without a miss
                }
                else if (timeDiff > HitWindowS && !BotPlay) // botplay: never miss because of a frame hitch (hit next update)
                {
                    // Keep it in the active list (flagged Missed) so it keeps rendering like the original.
                    note.Missed = true;
                    _missedNotesBuffer.Add(note);
                }
            }
            else if (!note.IsPlayerNote)
            {
                if (OpponentHuman && !note.IsHit && !note.Missed && currentTime - note.Time > HitWindowS)
                {
                    note.Missed = true;          // multiplayer: the opponent strumline is played by a human
                    OpponentMissed.Add(note);
                }
                // Remove old opponent notes after their sustain has ended
                double noteEndTime = note.Time + note.SustainLength;
                if (currentTime - noteEndTime > 0.5 && (note.IsHit || note.Missed || OpponentHuman)) // unhit notes (frame hitch) are still hit by GetOpponentNotes first
                {
                    SwapRemoveAt(i);
                    ReturnNote(note);
                }
            }
        }
        
        return _missedNotesBuffer;
    }
    
    /// <summary>
    /// Return missed notes to the pool after the caller has finished processing them.
    /// </summary>
    public void ReturnMissedNotes()
    {
        // Missed notes stay in the active list (they scroll offscreen first); Update() recycles them later.
        _missedNotesBuffer.Clear();
    }

    /// <summary>Strumline scroll speed (chart speed); used for the offscreen lifetime of missed notes.</summary>
    public float ScrollSpeed { get; set; } = 1f;
    
    /// <summary>Called for every note when it spawns (Psych onSpawnNote).</summary>
    public System.Action<PNote> OnSpawn;
    /// <summary>Psych spawn window (spawnTime * playbackRate / min(songSpeed,1)); null = port default.</summary>
    public System.Func<double> SpawnAheadOverride;

    private void SpawnUpcomingNotes(double currentTime)
    {
        // Add notes that are coming up
        while (_nextNoteIndex < _chart.Notes.Count)
        {
            var chartNote = _chart.Notes[_nextNoteIndex];
            
            if (chartNote.Time <= currentTime + (SpawnAheadOverride != null ? Math.Max(SpawnAheadOverride(), RenderAheadSec) : Math.Max(SPAWN_DISTANCE, RenderAheadSec)))
            {
                var note = RentNote();
                note.Time = chartNote.Time;
                note.Lane = chartNote.Lane;
                note.SustainLength = (float)chartNote.SustainLength;
                note.IsPlayerNote = chartNote.IsPlayerNote;
                note.GfNote = chartNote.GfNote;
                note.AltAnim = chartNote.AltAnim;
                note.NoAnimation = chartNote.NoAnimation;
                note.Kind = chartNote.Kind;
                note.NoteType = chartNote.NoteType;
                note.Data.CopyPsychFields(chartNote);
                note.Source = chartNote;
                if (chartNote.AltAnim && string.IsNullOrEmpty(chartNote.NoteType))
                    note.AnimSuffix = "-alt";
                _activeNotes.Add(note);
                _nextNoteIndex++;
                OnSpawn?.Invoke(note); // Psych PlayState.update: onSpawnNote
            }
            else
            {
                break;
            }
        }
    }
    
    /// <summary>
    /// Get a hittable note in the given lane, if any.
    /// Returns the closest note to currentTime within the hit window.
    /// </summary>
    public PNote GetHittableNote(int lane, double currentTime)
    {
        int count = _activeNotes.Count;
        PNote best = null;
        double bestDiff = double.MaxValue;
        
        for (int i = 0; i < count; i++)
        {
            var note = _activeNotes[i];
            if (note.Lane == lane && note.IsPlayerNote && !note.IsHit && !note.Missed)
            {
                double timeDiff = Math.Abs(currentTime - note.Time);
                
                if (timeDiff <= HitWindowS && timeDiff < bestDiff)
                {
                    best = note;
                    bestDiff = timeDiff;
                }
            }
        }
        
        return best;
    }
    
    /// <summary>
    /// Get the earliest unhit player note in a lane that is at or past the given time.
    /// Used by botplay to auto-hit notes without the timing window constraint.
    /// </summary>
    public PNote GetBotplayNote(int lane, double currentTime)
    {
        if (RemotePlayer) return null;
        int count = _activeNotes.Count;
        PNote best = null;
        double bestTime = double.MaxValue;
        
        for (int i = 0; i < count; i++)
        {
            var note = _activeNotes[i];
            if (note.Lane == lane && note.IsPlayerNote && !note.IsHit && !note.Missed && !note.IgnoreNote && !note.BlockHit && currentTime >= note.Time)
            {
                if (note.Time < bestTime)
                {
                    best = note;
                    bestTime = note.Time;
                }
            }
        }
        
        return best;
    }
    
    /// <summary>
    /// Mark a note as hit. For hold notes, keep in active list so sustain renders.
    /// For non-hold notes, remove from list.
    /// </summary>
    public void RemoveNote(PNote note)
    {
        note.IsHit = true;
        if (note.SustainLength <= 0)
        {
            int idx = _activeNotes.IndexOf(note);
            if (idx >= 0)
            {
                SwapRemoveAt(idx);
                ReturnNote(note);
            }
        }
        // Hold notes stay in _activeNotes so the sustain tail keeps rendering
    }
    
    /// <summary>
    /// Fully remove a hold note when the hold is complete or released.
    /// </summary>
    public void RemoveHoldNote(PNote note)
    {
        note.IsHit = true;
        note.HoldComplete = true;
        int idx = _activeNotes.IndexOf(note);
        if (idx >= 0)
        {
            SwapRemoveAt(idx);
            ReturnNote(note);
        }
    }
    
    /// <summary>
    /// Get all visible notes for rendering
    /// </summary>
    public List<PNote> GetVisibleNotes(double currentTime)
    {
        _visibleNotesBuffer.Clear();
        int count = _activeNotes.Count;
        double visibleStart = currentTime - 0.1;
        double visibleEnd = currentTime + RenderAheadSec;
        
        for (int i = 0; i < count; i++)
        {
            var note = _activeNotes[i];
            
            // Skip completed hold notes
            if (note.HoldComplete) continue;
            
            // Skip hit non-hold notes (hold notes stay visible for sustain rendering)
            if (note.IsHit && note.SustainLength <= 0) continue;
            
            // Only show notes approaching or at the strumline (not past it)
            // For hold notes that are hit, show them as long as the sustain is active
            if (note.IsHit && note.SustainLength > 0)
            {
                // Show hold note if sustain end is still in the future
                double sustainEnd = note.Time + note.SustainLength;
                if (sustainEnd >= visibleStart)
                {
                    _visibleNotesBuffer.Add(note);
                }
            }
            else if (note.Missed)
            {
                _visibleNotesBuffer.Add(note); // scrolls past the strumline until recycled in Update()
            }
            else if (note.Time >= visibleStart && note.Time <= visibleEnd)
            {
                _visibleNotesBuffer.Add(note);
            }
        }
        return _visibleNotesBuffer;
    }
    
    /// <summary>
    /// Get opponent notes that should trigger animations
    /// </summary>
    public List<PNote> GetOpponentNotes(double currentTime)
    {
        _opponentNotesBuffer.Clear();
        int count = _activeNotes.Count;
        for (int i = 0; i < count; i++)
        {
            var note = _activeNotes[i];
            if (!note.IsPlayerNote && !note.IsHit && currentTime >= note.Time)
            {
                _opponentNotesBuffer.Add(note);
            }
        }
        return _opponentNotesBuffer;
    }
    
    /// <summary>
    /// Get an active opponent sustain note in the given lane if the sustain is still playing.
    /// Used to keep opponent singing during hold notes (original FNF behavior).
    /// </summary>
    public PNote GetActiveOpponentSustain(int lane, double currentTime)
    {
        int count = _activeNotes.Count;
        for (int i = 0; i < count; i++)
        {
            var note = _activeNotes[i];
            if (!note.IsPlayerNote && note.IsHit && !note.HoldComplete
                && note.Lane == lane && note.SustainLength > 0
                && currentTime <= note.Time + note.SustainLength)
            {
                return note;
            }
        }
        return null;
    }
    
    public void Reset()
    {
        _activeNotes.Clear();
        _nextNoteIndex = 0;
    }

    /// <summary>
    /// Returns all currently spawned notes. Do not modify the list.
    /// </summary>
    public List<PNote> GetAllActiveNotes() => _activeNotes;

    /// <summary>
    /// Returns all currently spawned sustain notes. Do not modify the list.
    /// </summary>
    public List<PNote> GetAllActiveSustains()
    {
        _sustainNotesBuffer.Clear();
        int count = _activeNotes.Count;
        for (int i = 0; i < count; i++)
        {
            var note = _activeNotes[i];
            if (note.SustainLength > 0)
                _sustainNotesBuffer.Add(note);
        }
        return _sustainNotesBuffer;
    }
}
