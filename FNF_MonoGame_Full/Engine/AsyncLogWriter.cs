using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace FNF_MonoGame.Engine;

/// <summary>
/// game_log.txt writer that never blocks the game thread on disk I/O: Console output is queued and written by a
/// background thread (flushed every 100 ms). The old AutoFlush StreamWriter did a synchronous file write for every
/// Console.WriteLine - dozens per second during busy songs (stress tankmen / muzzle flashes / speaker logs), which
/// showed up as 100-300 ms frame stalls on a busy or nearly full disk. Flush() drains synchronously (crash / exit).
/// </summary>
public sealed class AsyncLogWriter : TextWriter
{
    private StreamWriter _w;
    /// <summary>Optional size cap (chars written since the file was opened; 0 = unlimited, desktop default). When it is
    /// exceeded <see cref="Rotate"/> is called with the current writer and must return the new one (UWP keeps 3 files).</summary>
    public long MaxChars;
    public Func<StreamWriter, StreamWriter> Rotate;
    private long _chars;
    private readonly ConcurrentQueue<string> _q = new();
    private readonly AutoResetEvent _signal = new(false);
    private readonly object _io = new();
    [ThreadStatic] private static StringBuilder _line;

    public AsyncLogWriter(StreamWriter w)
    {
        _w = w; _w.AutoFlush = false;
        var t = new Thread(Pump) { IsBackground = true, Name = "log-writer" };
        try { t.Priority = ThreadPriority.BelowNormal; } catch { }
        t.Start();
        AppDomain.CurrentDomain.ProcessExit += (s, e) => Flush();
        AppDomain.CurrentDomain.UnhandledException += (s, e) => Flush();
    }

    public override Encoding Encoding => _w.Encoding;

    public override void Write(char value)
    {
        var sb = _line ??= new StringBuilder(256);
        sb.Append(value);
        if (value == '\n') { _q.Enqueue(sb.ToString()); sb.Clear(); }
    }
    public override void Write(string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        var sb = _line ??= new StringBuilder(256);
        if (sb.Length == 0 && value.EndsWith("\n")) { _q.Enqueue(value); return; }
        sb.Append(value);
        if (value.EndsWith("\n")) { _q.Enqueue(sb.ToString()); sb.Clear(); }
    }
    public override void WriteLine(string value)
    {
        var sb = _line;
        if (sb != null && sb.Length > 0) { sb.Append(value).Append(Environment.NewLine); _q.Enqueue(sb.ToString()); sb.Clear(); }
        else _q.Enqueue((value ?? "") + Environment.NewLine);
    }
    public override void WriteLine() => WriteLine("");

    private void Drain()
    {
        lock (_io)
        {
            bool any = false;
            while (_q.TryDequeue(out var s))
            {
                _w.Write(s); any = true; _chars += s.Length;
                if (MaxChars > 0 && _chars > MaxChars && Rotate != null)
                {
                    try { _w.Flush(); var nw = Rotate(_w); if (nw != null) { _w = nw; _w.AutoFlush = false; } } catch { }
                    _chars = 0;
                }
            }
            if (any) _w.Flush();
        }
    }

    private void Pump()
    {
        while (true)
        {
            _signal.WaitOne(100);
            try { Drain(); } catch { }
        }
    }

    public override void Flush() { try { Drain(); } catch { } }
}
