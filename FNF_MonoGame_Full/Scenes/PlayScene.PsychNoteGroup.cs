using System;
using System.Collections.Generic;
using System.Linq;
using PNote = FNF_MonoGame.Engine.PsychEngine.Note;
using ChartNote = FNF_MonoGame.Gameplay.Note;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// PlayState.unspawnNotes / PlayState.notes as Psych scripts see them. The port's NoteField keeps one note per chart
/// note (sustains drawn from the head), Psych has a separate Note per sustain tail piece:
///  - generateSong: head + round(sustainLength / stepCrochet) pieces at strumTime + stepCrochet * k, sorted by time.
///  - update: while unspawnNotes[0].strumTime - songPosition &lt; spawnTime -> notes.insert(0, note), onSpawnNote(index 0, ...).
///  - goodNoteHit/opponentNoteHit: callbacks get notes.members.indexOf(note); heads are invalidated afterwards, tail pieces
///    stay until songPosition - strumTime &gt; noteKillOffset (= max(stepCrochet, 350 / songSpeed * playbackRate)).
///  - beatHit: notes.sort(FlxSort.byY, DESCENDING) (upscroll; later notes first).
/// Views share the chart note data, so property writes from scripts reach the real notes.
/// </summary>
public partial class PlayScene
{
    private List<PNote> _psyGroup = new List<PNote>();
    private Dictionary<ChartNote, PNote> _psyHeadView = new Dictionary<ChartNote, PNote>();
    /// <summary>Lua getProperty('notes.length') / HScript game.notes (exact camelCase wins over 'Notes').</summary>
    public System.Collections.IList notes => PsychNotes ? (System.Collections.IList)_psyGroup : (Notes ?? new PNote[0]);
    /// <summary>HScript game.unspawnNotes.</summary>
    public List<PNote> unspawnNotes => UnspawnNotes;

    /// <summary>PlayState.generateSong: unspawnNotes with heads + tail pieces.</summary>
    private List<PNote> PsychBuildUnspawnNotes()
    {
        var l = new List<PNote>();
        _psyGroup = new List<PNote>();
        _psyHeadView = new Dictionary<ChartNote, PNote>();
        if (_chart?.Notes == null) return l;
        foreach (var n in _chart.Notes)
        {
            var head = new PNote(n);
            l.Add(head);
            _psyHeadView[n] = head;
            if (!PsychNotes || n.SustainLength <= 0) continue;
            double step = PsychStepCrochetAt(n.Time);
            int count = (int)Math.Floor(n.SustainLength / step + 0.5); // Haxe Math.round
            for (int k = 0; k < count; k++)
            {
                var piece = new PNote(n) { PieceIndex = k, PieceStepMs = step * 1000.0, Parent = head };
                head.Tail.Add(piece);
                l.Add(piece);
            }
        }
        // ArraySort (merge sort, stable) by strumTime
        return l.Select((x, i) => (x, i)).OrderBy(t => t.x.strumTime).ThenBy(t => t.i).Select(t => t.x).ToList();
    }

    private double PsychSpawnTimeMs() => 2000.0 * playbackRate / Math.Min(1.0, Math.Max(0.01, songSpeed));
    private double PsychNoteKillOffsetMs()
    {
        double stepMs = _conductor != null ? _conductor.Crochet * 1000.0 / 4.0 : 100.0;
        return Math.Max(stepMs, 350.0 / Math.Max(0.01, songSpeed) * playbackRate);
    }

    /// <summary>PlayState.update spawn loop (after the position advance): fires Lua onSpawnNote for heads and pieces.</summary>
    internal void PsychGroupSpawn()
    {
        var un = UnspawnNotes;
        if (!PsychNotes || un == null || un.Count == 0) return;
        double pos = PsychSongPosMs();
        double time = PsychSpawnTimeMs();
        if (un[0] != null && un[0].MultSpeed < 1f) time /= un[0].MultSpeed;
        int guard = 0;
        while (un.Count > 0 && guard++ < 4096)
        {
            var n = un[0];
            if (n == null) { un.RemoveAt(0); continue; }
            if (n.strumTime - pos >= time) break;
            _psyGroup.Insert(0, n);
            n.Spawned = true;
            CallOnLuas("onSpawnNote", 0, n.Lane % 4, n.NoteType ?? "", n.isSustainNote, (double)n.strumTime);
            un.RemoveAt(0);
        }
    }

    /// <summary>Kill extremely late notes (PlayState.update notes loop).</summary>
    private void PsychGroupKill()
    {
        if (_psyGroup.Count == 0) return;
        double pos = PsychSongPosMs();
        double kill = PsychNoteKillOffsetMs();
        _psyGroup.RemoveAll(n => pos - n.strumTime > kill);
    }

    /// <summary>beatHit: notes.sort(FlxSort.byY, DESCENDING) (upscroll: y grows with strumTime; a piece sits below its head).</summary>
    internal void PsychGroupSortOnBeat()
    {
        if (_psyGroup.Count < 2) return;
        var arr = _psyGroup.ToArray();
        _psyGroup = arr.Select((x, i) => (x, i))
            .OrderByDescending(t => t.x.strumTime + (t.x.isSustainNote ? 0.001 : 0.0))
            .ThenBy(t => t.i).Select(t => t.x).ToList();
    }

    /// <summary>Index of the head view of a live NoteField note in PlayState.notes.members (-1 if not in the group).</summary>
    private int PsychGroupIndexOfHead(PNote live)
    {
        if (live == null) return -1;
        var src = live.Source ?? live.Data;
        for (int i = 0; i < _psyGroup.Count; i++)
        {
            var v = _psyGroup[i];
            if (v.PieceIndex < 0 && (ReferenceEquals(v.Data, src) || ReferenceEquals(v, live))) return i;
        }
        return -1;
    }

    /// <summary>Index of tail piece k of a chart note.</summary>
    private int PsychGroupIndexOfPiece(ChartNote src, int k)
    {
        for (int i = 0; i < _psyGroup.Count; i++)
        {
            var v = _psyGroup[i];
            if (v.PieceIndex == k && ReferenceEquals(v.Data, src)) return i;
        }
        return -1;
    }

    /// <summary>invalidateNote(head) after goodNoteHit / opponentNoteHit / noteMiss of a head.</summary>
    private void PsychGroupInvalidateHead(PNote live)
    {
        int i = PsychGroupIndexOfHead(live);
        if (i >= 0) _psyGroup.RemoveAt(i);
    }

    /// <summary>The live NoteField note for a group view (HScript onSpawnNote / getPropertyFromGroup 'notes' writes).</summary>
    internal PNote PsychGroupLive(PNote view)
    {
        if (view == null) return null;
        var src = _noteField?.GetAllActiveNotes();
        if (src != null && view.PieceIndex < 0)
            foreach (var n in src) if (ReferenceEquals(n.Source, view.Data)) return n;
        return view;
    }

    /// <summary>Per frame, after the NoteField update: spawn, then kill.</summary>
    internal void PsychGroupUpdate()
    {
        if (!PsychNotes) return;
        PsychGroupSpawn();
        PsychGroupKill();
    }
}
