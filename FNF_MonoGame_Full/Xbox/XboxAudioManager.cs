using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Xna.Framework.Audio;
using NVorbis;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame;

/// <summary>
/// Xbox/UWP-compatible audio manager using NVorbis (pure managed OGG decoder)
/// instead of NAudio (which requires Windows desktop APIs).
/// Provides the same public API as AudioManager so game code works unchanged.
///
/// Song audio = ONE <see cref="SongMixer"/>: the instrumental and every vocal track (Voices, Voices-bf/-dad,
/// Voices-Player/-Opponent) are decoded by one thread and mixed into ONE DynamicSoundEffectInstance, so they can
/// never start, pause, resume, stall or drift apart independently (XBOX-AUDIO: the old code ran one
/// DynamicSoundEffectInstance + streaming thread per track; a thread that got scheduled before its track's Play()
/// saw State == Stopped and exited at once -> that track stayed silent for the whole song, which on the busy Xbox
/// core happened "sometimes" to the instrumental and sometimes to the vocals). Playback starts on the next
/// Update()/MusicPosition read after PlayMusic, so the vocals requested in the same frame are attached first.
/// The song clock follows the audio: buffer underruns are detected and the clock is re-based to the audio.
/// Every start/stop/pause/underrun/resync is logged with [AUDIO].
/// </summary>
public class XboxAudioManager : IDisposable
{
    private readonly AssetManager _assets;

    private SongMixer _mixer;

    // Sound effects cache
    private readonly Dictionary<string, SoundEffect> _soundCache = new();

    // Volume
    private float _musicVolume = 1f;
    private float _sfxVolume = 1f;

    // Extra content roots (for funkin.assets)
    public List<string> ExtraContentRoots { get; } = new();

    // Intro→loop chaining
    private string _pendingLoopPath;
    private Action _onMusicComplete;

    // Music fade-out state
    private float _fadeOutTimer;
    private float _fadeOutDuration;
    private float _fadeOutStartVolume;
    private bool _isFadingOut;
    private Action _fadeOutCallback;

    internal static readonly System.Diagnostics.Stopwatch Wall = System.Diagnostics.Stopwatch.StartNew();
    internal static void Log(string s) { try { Console.WriteLine($"[AUDIO] t={Wall.Elapsed.TotalSeconds:F3} {s}"); } catch { } }

    public XboxAudioManager(AssetManager assets)
    {
        _assets = assets;
    }

    // === Properties matching AudioManager API ===

    public double MusicPosition
    {
        get
        {
            var m = _mixer;
            if (m == null) return 0;
            if (m.StartPending) m.Start();
            return m.ClockMs();
        }
    }

    public double MusicLength
    {
        get
        {
            var m = _mixer;
            if (m == null || !m.HasInst) return 0;
            if (_musicLengthCache >= 0) return _musicLengthCache;
            return _musicLengthCache = m.InstLengthSec();
        }
    }

    public bool MusicPlaying => _mixer != null && _mixer.IsPlaying;
    public bool HasPlayerVoice => PrepareOnly ? _prepFound[1] : (_mixer != null && _mixer.HasKind(1));
    public bool HasOpponentVoice => PrepareOnly ? _prepFound[2] : (_mixer != null && _mixer.HasKind(2));

    // --- Preloading (same API as AudioManager): song readers opened + length scanned before the countdown.
    public bool PrepareOnly;
    private readonly bool[] _prepFound = new bool[3];
    private double _musicLengthCache = -1;
    private readonly Dictionary<string, VorbisReader> _preopened = new Dictionary<string, VorbisReader>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _lengthCache = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    public void BeginPrepare() { PrepareOnly = true; _prepFound[0] = _prepFound[1] = _prepFound[2] = false; }
    public void EndPrepare() { PrepareOnly = false; }
    private void PrepareReader(string path, int kind)
    {
        string fullPath = ResolveAudioPath(path);
        if (fullPath == null || !File.Exists(fullPath)) return;
        _prepFound[kind] = true;
        if (_preopened.ContainsKey(fullPath)) return;
        try
        {
            var r = new VorbisReader(fullPath);
            try { _lengthCache[fullPath] = r.TotalTime.TotalSeconds; r.SeekTo(0); } catch { }
            _preopened[fullPath] = r;
            Console.WriteLine($"[PRELOAD] audio reader ready {Path.GetFileName(fullPath)} kind={kind}");
        }
        catch (Exception e) { Console.WriteLine($"[PRELOAD] audio failed {fullPath}: {e.Message}"); }
    }
    private VorbisReader TakeReader(string fullPath)
    {
        if (_preopened.TryGetValue(fullPath, out var r)) { _preopened.Remove(fullPath); return r; }
        return new VorbisReader(fullPath);
    }
    public void ClearPrepared()
    {
        foreach (var r in _preopened.Values) { try { r.Dispose(); } catch { } }
        _preopened.Clear();
    }

    public float MusicVisualizerLevel { get; private set; }

    public float MusicVolume
    {
        get => _musicVolume;
        set
        {
            _musicVolume = Math.Clamp(value, 0f, 1f);
            if (!_isFadingOut) _mixer?.SetVolume(_musicVolume);
        }
    }

    public float SfxVolume
    {
        get => _sfxVolume;
        set => _sfxVolume = Math.Clamp(value, 0f, 1f);
    }

    public double VoicePosition => MusicPosition; // vocals are mixed with the inst: always the same position

    public void Update(float deltaTime = 1f / 60f)
    {
        var m = _mixer;
        if (m != null)
        {
            if (m.StartPending) m.Start();
            m.Watchdog();
            if (m.TakeCompleted())
            {
                var cb = _onMusicComplete;
                _onMusicComplete = null;
                cb?.Invoke(); // intro -> loop chaining (main thread)
            }
        }

        // Tick music fade-out (fades voices alongside music — matches original FNF)
        if (_isFadingOut && _mixer != null)
        {
            _fadeOutTimer += deltaTime;
            float t = Math.Clamp(_fadeOutTimer / _fadeOutDuration, 0f, 1f);
            _mixer.SetVolume(_fadeOutStartVolume * (1f - t));
            if (t >= 1f)
            {
                _isFadingOut = false;
                var fadeCb = _fadeOutCallback;
                _fadeOutCallback = null;
                if (fadeCb != null)
                    fadeCb.Invoke();
                else
                    StopMusic();
            }
        }
    }

    // === Music playback ===

    public void PlayMusic(string path, bool loop = true, int startTimeMs = 0)
    {
        if (PrepareOnly) { PrepareReader(path, 0); return; }
        StopMusic(); _rate = 1.0;

        string fullPath = ResolveAudioPath(path);
        if (fullPath == null || !File.Exists(fullPath)) { Log($"music not found '{path}'"); return; }

        try
        {
            var reader = TakeReader(fullPath);
            _musicLengthCache = _lengthCache.TryGetValue(fullPath, out var __len) ? __len : -1;
            double startSec = Math.Max(0, startTimeMs) / 1000.0;
            var m = new SongMixer(reader.SampleRate, startSec, loop);
            m.SetVolume(_musicVolume);
            m.Add(reader, 0, Path.GetFileName(fullPath));
            _mixer = m;
            Log($"music armed '{path}' rate={reader.SampleRate} ch={reader.Channels} loop={loop} start={startSec:F3}s (starts with all tracks on the next update)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[XboxAudio] Failed to play music '{path}': {ex.Message}");
        }
    }

    public void StopMusic()
    {
        _onMusicComplete = null;
        _pendingLoopPath = null;
        _isFadingOut = false;
        _fadeOutCallback = null;
        var m = _mixer;
        _mixer = null;
        if (m != null) m.Dispose("StopMusic");
    }

    public void PauseMusic() => _mixer?.Pause("PauseMusic");

    public void ResumeMusic() => _mixer?.Resume("ResumeMusic");

    /// <summary>UWP Suspending (Xbox guide / PLM): pause everything; <see cref="OnResume"/> restarts only what this paused.</summary>
    public void OnSuspend() { _mixer?.SuspendPause(); }
    public void OnResume() { _mixer?.SuspendResume(); }

    // === Voice tracks ===

    public void PlayVoices(string path) => AttachVoice(path, 1);
    public void PlayOpponentVoices(string path) => AttachVoice(path, 2);

    private void AttachVoice(string path, int kind)
    {
        if (PrepareOnly) { PrepareReader(path, kind); return; }
        _mixer?.Remove(kind);
        string fullPath = ResolveAudioPath(path);
        if (fullPath == null || !File.Exists(fullPath)) return;
        if (_mixer == null) { Log($"voice '{path}' requested without music - ignored"); return; }
        try
        {
            _mixer.Add(TakeReader(fullPath), kind, Path.GetFileName(fullPath));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[XboxAudio] Failed to play voice '{path}': {ex.Message}");
        }
    }

    public void StopVoices()
    {
        _mixer?.Remove(1);
        _mixer?.Remove(2);
    }

    public void SetPlayerVoiceVolume(float volume) => _mixer?.SetGain(1, Math.Clamp(volume, 0f, 1f));

    private int _resyncCalls;
    /// <summary>Called by PlayScene every 0.5 s. Vocals are mixed with the inst (always in sync); logs a status line every 5 s.</summary>
    public void ResyncVoices()
    {
        if (++_resyncCalls % 10 == 0) _mixer?.LogStatus("status");
    }

    // === Sound effects (decoded once, played on an owned instance pool: Engine/SfxPool.cs) ===
    private readonly FNF_MonoGame.Engine.SfxPool _sfxPool = new FNF_MonoGame.Engine.SfxPool();
    public FNF_MonoGame.Engine.SfxPool SfxPoolStats => _sfxPool;

    /// <summary>Same lookup order as the desktop AudioManager.LoadSoundFromFile: path, path.ogg, sounds/path.ogg, sounds/path.</summary>
    private string ResolveSfxPath(string name)
    {
        string n = name.Replace('\\', '/');
        return ResolveAudioPath(n)
            ?? (n.StartsWith("sounds/") ? null : (_assets.ResolvePath("sounds/" + n + ".ogg") ?? _assets.ResolvePath("sounds/" + n)));
    }

    private SoundEffect LoadSfx(string name)
    {
        if (_soundCache.TryGetValue(name, out var cached) && cached != null && !cached.IsDisposed) return cached;
        string fullPath = ResolveSfxPath(name);
        if (fullPath == null || !File.Exists(fullPath)) return null;
        try
        {
            using var reader = new VorbisReader(fullPath);
            int ch = reader.Channels;
            var all = new List<float>((int)Math.Max(4096, Math.Min(int.MaxValue / 4, reader.TotalSamples * ch)));
            float[] buf = new float[4096 * ch];
            int n;
            while ((n = reader.ReadSamples(buf, 0, buf.Length)) > 0)
                for (int i = 0; i < n; i++) all.Add(buf[i]);
            int frames = all.Count / ch;
            byte[] pcm = new byte[frames * ch * 2];
            for (int i = 0; i < frames * ch; i++)
            {
                short v = (short)(Math.Clamp(all[i], -1f, 1f) * 32767);
                pcm[i * 2] = (byte)(v & 0xFF);
                pcm[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
            }
            if (pcm.Length == 0) return null;
            var sfx = new SoundEffect(pcm, reader.SampleRate, ch == 1 ? AudioChannels.Mono : AudioChannels.Stereo);
            _soundCache[name] = sfx;
            return sfx;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[XboxAudio] Failed to load sound '{name}': {ex.Message}");
            return null;
        }
    }

    public Sound PlaySound(string name, float volume = 1f)
    {
        if (string.IsNullOrEmpty(name)) return null;
        bool wasCached = _soundCache.ContainsKey(name);
        var sfx = LoadSfx(name);
        if (sfx == null)
        {
            Console.WriteLine($"[SFXDBG] '{name}' loaded=False result=NOT_FOUND");
            return null;
        }
        _sfxPool.Play(sfx, name, _sfxVolume * Math.Clamp(volume, 0f, 1f), wasCached);
        return new Sound();
    }

    public Sound PlaySound(string name, float volume, bool looped, bool autoDestroy) => PlaySound(name, volume);

    public void PreloadSound(string name) { try { LoadSfx(name); } catch { } }

    /// <summary>Decode the SFX every menu/gameplay screen uses up front, so the first press never waits on an OGG decode.</summary>
    public void PreloadCommonSfx()
    {
        foreach (var n in FNF_MonoGame.Engine.SfxPool.CommonSfx) PreloadSound(n);
    }

    public bool HasCachedSound(string name) => _soundCache.ContainsKey(name);

    public void LoadSound(string name, string path)
    {
        var s = LoadSfx(path);
        if (s != null) _soundCache[name] = s;
    }

    public SoundEffectInstance CreateSoundInstance(string path)
    {
        try { return LoadSfx(path)?.CreateInstance(); }
        catch { return null; }
    }

    public void PlayMusicWithIntro(string introPath, string loopPath)
    {
        string introFull = ResolveAudioPath(introPath);
        if (introFull != null && File.Exists(introFull))
        {
            PlayMusic(introPath, false);
            _pendingLoopPath = loopPath;
            _onMusicComplete = () =>
            {
                string pending = _pendingLoopPath;
                _pendingLoopPath = null;
                if (pending != null)
                    PlayMusic(pending, true);
            };
        }
        else
        {
            PlayMusic(loopPath, true);
        }
    }

    public void FadeOutMusic(float duration = 0.5f, Action onComplete = null)
    {
        if (_mixer == null || !MusicPlaying)
        {
            onComplete?.Invoke();
            return;
        }
        _fadeOutTimer = 0;
        _fadeOutDuration = Math.Max(0.01f, duration);
        _fadeOutStartVolume = _mixer.Volume;
        _fadeOutCallback = onComplete;
        _isFadingOut = true;
    }

    public void StopOpponentVoices() => _mixer?.Remove(2);

    public void SetOpponentVoiceVolume(float volume) => _mixer?.SetGain(2, Math.Clamp(volume, 0f, 1f));

    // === Path resolution ===

    private string ResolveAudioPath(string path)
    {
        // Try direct path with .ogg extension, then without
        string result = _assets.ResolvePath(path + ".ogg")
            ?? _assets.ResolvePath(path);
        if (result != null) return result;

        // Try tracks/ subfolder (e.g. songs/tutorial/tracks/Inst.ogg)
        string dirPart = Path.GetDirectoryName(path);
        string filePart = Path.GetFileName(path);
        if (dirPart != null)
        {
            string tracksPath = Path.Combine(dirPart, "tracks", filePart);
            result = _assets.ResolvePath(tracksPath + ".ogg")
                ?? _assets.ResolvePath(tracksPath);
        }

        return result;
    }

    public float Volume { get => MusicVolume; set => MusicVolume = value; }
    public XboxAudioManager Music => this;
    public void Stop() => StopMusic();
    public void FadeOut(float duration = 1f, float endVolume = 0f, Action onComplete = null) => FadeOutMusic(duration, onComplete);
    public void FadeIn(float duration = 1f, float endVolume = 1f) { }
    public void FadeIn(float duration, float startVolume, float endVolume) { }

    // ---- Practice: playback rate (speed + pitch) and in-song seek ----
    private double _rate = 1.0;
    public float PlaybackRate
    {
        get => (float)_rate;
        set
        {
            _rate = Math.Clamp((double)value, 0.5, 2.0);
            _mixer?.SetRate(_rate);
        }
    }

    public void SeekSong(double ms) => _mixer?.Seek(ms / 1000.0);

    public void Dispose()
    {
        StopMusic();
        foreach (var sfx in _soundCache.Values)
            sfx?.Dispose();
        _soundCache.Clear();
    }
}

/// <summary>
/// Inst + vocals mixed into one DynamicSoundEffectInstance by one decode thread (see XboxAudioManager).
/// Clock = Stopwatch (smooth) re-based to the audio: consumed frames are counted from PendingBufferCount at every
/// buffer end; when the clock runs ahead of the heard audio by more than 20 ms (underrun / starved thread) it is
/// moved back to the audio and held (MusicPosition never goes backwards); when it lags it jumps forward.
/// </summary>
internal sealed class SongMixer
{
    private sealed class Src
    {
        public VorbisReader R; public int Kind; public string Name; public int Ch, Rate;
        public volatile float Gain = 1f; public bool Ended;
        public float[] Raw = new float[0];
        // resampler state (Rate != output rate): stereo source frames S[0..SLen), read index Idx + fraction Frac
        public float[] S = new float[0]; public int SLen, Idx; public double Frac;
        public long FramesRead; // source position (frames) of the next decoded frame
    }

    public const int Frames = 1024;          // frames per submitted buffer (~23 ms @ 44.1 kHz)
    private const double SyncTolerance = 0.020;

    private readonly int _outRate;
    private readonly bool _loop;
    private readonly object _lk = new object();
    private readonly List<Src> _srcs = new List<Src>();
    private DynamicSoundEffectInstance _track;
    private Thread _thread;
    private volatile bool _disposed;
    private int _target = 16;                 // buffers kept queued (~370 ms: song start overlaps heavy loading + GC; grows after underruns, max 24)
    private readonly float[] _mix = new float[Frames * 2];
    private readonly float[] _tmp = new float[Frames * 2];
    private readonly byte[] _pcm = new byte[Frames * 4];
    private readonly Queue<int> _queued = new Queue<int>();
    private long _outFrames;                  // output frames mixed since _startSec
    private long _consumed;                   // output frames played (buffer ends seen)
    private double _startSec;
    private volatile bool _instEnded;
    private volatile bool _completedFlag;
    private volatile bool _started;
    public bool StartPending { get; private set; } = true;
    private volatile bool _paused;
    private bool _suspendPaused;
    private float _volume = 1f;
    private double _rate = 1.0;
    // clock
    private readonly System.Diagnostics.Stopwatch _clock = new System.Diagnostics.Stopwatch();
    private double _clockBase;               // song seconds at _clock = 0
    private double _lastReported = double.NegativeInfinity;
    private readonly double[] _drifts = new double[12];
    private int _driftN, _driftI;
    private int _underruns, _resyncs;
    private bool _inUnderrun;
    private double _maxObsDrift;
    // dev: FNF_AUDIO_STALL="songSec:ms[,songSec:ms]" sleeps the mixer thread once (forces an underrun)
    private static readonly List<(double at, int ms)> _devStalls = ParseStalls();
    // stuck-voice detector: the XAudio voice stops consuming buffers (seen after suspend/device changes on consoles)
    // while MonoGame still reports Playing -> no buffer end for StuckSec -> rebuild the voice at the heard position
    private const double StuckSec = 0.5;
    private double _lastDoneAt;               // Wall seconds of the last finished buffer (or start/resume)
    private volatile bool _stuck;
    private int _rebuilds;
    // dev: FNF_AUDIO_FREEZE=<song s> stops the XAudio voice once behind MonoGame's back (simulates a stuck voice)
    private static double _devFreezeAt = ParseFreeze();
    private volatile bool _devFrozen;

    public SongMixer(int outRate, double startSec, bool loop)
    {
        _outRate = Math.Max(8000, outRate);
        _startSec = startSec;
        _clockBase = startSec;
        _loop = loop;
        _track = NewTrack();
    }

    private DynamicSoundEffectInstance NewTrack()
    {
        var t = new DynamicSoundEffectInstance(_outRate, AudioChannels.Stereo);
        t.Volume = _volume;
        try { t.Pitch = (float)Math.Clamp(Math.Log(_rate, 2.0), -1.0, 1.0); } catch { }
        return t;
    }

    public bool HasInst { get { lock (_lk) return _srcs.Exists(s => s.Kind == 0); } }
    public bool HasKind(int k) { lock (_lk) return _srcs.Exists(s => s.Kind == k); }
    public float Volume => _volume;

    public double InstLengthSec()
    {
        lock (_lk)
        {
            var s = _srcs.Find(x => x.Kind == 0);
            try { return s?.R.TotalTime.TotalSeconds ?? 0; } catch { return 0; }
        }
    }

    public bool IsPlaying
    {
        get
        {
            if (_disposed) return false;
            if (StartPending) return true;
            if (_paused) return false;
            if (_instEnded && !_loop) { try { return _track.PendingBufferCount > 0; } catch { return false; } }
            return true;
        }
    }

    public void Add(VorbisReader r, int kind, string name)
    {
        var s = new Src { R = r, Kind = kind, Name = name, Ch = Math.Max(1, r.Channels), Rate = r.SampleRate };
        lock (_lk)
        {
            // line the new track up with the next frame the mixer will produce (sample exact, same buffer)
            double at = _startSec + _outFrames / (double)_outRate;
            long sp = (long)Math.Round(at * s.Rate);
            try
            {
                if (sp >= r.TotalSamples) s.Ended = true;
                else if (sp > 0) r.SeekTo(sp);   // readers are opened/preloaded at sample 0
            }
            catch (Exception e) { XboxAudioManager.Log($"seek failed {name}: {e.Message}"); }
            s.FramesRead = sp;
            _srcs.Add(s);
            XboxAudioManager.Log($"track add {KindName(kind)} '{name}' rate={s.Rate} ch={s.Ch} at={at:F3}s started={_started}");
        }
    }

    public void Remove(int kind)
    {
        lock (_lk)
        {
            for (int i = _srcs.Count - 1; i >= 0; i--)
            {
                if (_srcs[i].Kind != kind) continue;
                XboxAudioManager.Log($"track remove {KindName(kind)} '{_srcs[i].Name}'");
                try { _srcs[i].R.Dispose(); } catch { }
                _srcs.RemoveAt(i);
            }
        }
    }

    public void SetGain(int kind, float g) { lock (_lk) foreach (var s in _srcs) if (s.Kind == kind) s.Gain = g; }
    public void SetVolume(float v) { _volume = v; try { if (!_disposed) _track.Volume = v; } catch { } }

    public void SetRate(double rate)
    {
        lock (_lk)
        {
            RebaseClock();
            _rate = rate;
            try { _track.Pitch = (float)Math.Clamp(Math.Log(_rate, 2.0), -1.0, 1.0); } catch { }
            _driftN = 0; _driftI = 0;
        }
    }

    // ---- clock ----
    private double RawClock() => _clockBase + _clock.Elapsed.TotalSeconds * _rate;
    private void RebaseClock() { _clockBase = RawClock(); if (_clock.IsRunning) _clock.Restart(); else _clock.Reset(); }
    public double ClockMs()
    {
        double c = RawClock();
        if (c < _lastReported) c = _lastReported; // never backwards (clock moved back to a stalled audio = hold)
        _lastReported = c;
        return c * 1000.0;
    }

    // ---- start / thread ----
    public void Start()
    {
        if (!StartPending || _disposed) return;
        StartPending = false;
        lock (_lk)
        {
            try
            {
                for (int i = 0; i < 8; i++) MixOne();
                _track.Play();
                _clock.Restart();
                _started = true;
                _lastDoneAt = XboxAudioManager.Wall.Elapsed.TotalSeconds;
            }
            catch (Exception e) { XboxAudioManager.Log("start failed: " + e.Message); }
        }
        LogStatus("START");
        _thread = new Thread(Pump) { IsBackground = true, Name = "song-audio" };
        try { _thread.Priority = ThreadPriority.AboveNormal; } catch { }
        _thread.Start();
    }

    private void Pump()
    {
        try
        {
            while (!_disposed)
            {
                DevStall();
                DevFreeze();
                bool full = true;
                lock (_lk)
                {
                    if (_disposed) break;
                    Account();
                    int pending = _track.PendingBufferCount;
                    if (!_instEnded && pending < _target) { MixOne(); full = pending + 1 >= _target; }
                }
                if (full) Thread.Sleep(_instEnded ? 20 : 4);
            }
        }
        catch (Exception e)
        {
            if (!_disposed) XboxAudioManager.Log($"mixer thread error: {e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>Count finished buffers; detect underruns; re-base the clock to the audio. Called with _lk held.</summary>
    private void Account()
    {
        int pending = _track.PendingBufferCount;
        int done = 0;
        while (_queued.Count > pending) { _consumed += _queued.Dequeue(); done++; }
        double wallNow = XboxAudioManager.Wall.Elapsed.TotalSeconds;
        if (done > 0) _lastDoneAt = wallNow;
        if (!_started || _paused) return;
        if (!_stuck && pending > 0 && wallNow - _lastDoneAt > StuckSec)
        {
            _stuck = true;
            string st; try { st = _track.State.ToString(); } catch { st = "?"; }
            XboxAudioManager.Log($"STUCK no buffer finished for {(wallNow - _lastDoneAt) * 1000:F0} ms (pending={pending} state={st}) audio={AudioSec():F3}s clock={RawClock():F3}s -> rebuild voice");
        }
        if (pending == 0 && !_instEnded)
        {
            if (!_inUnderrun)
            {
                _inUnderrun = true; _underruns++;
                _target = Math.Min(24, _target + 4);
                XboxAudioManager.Log($"UNDERRUN #{_underruns} audio={AudioSec():F3}s clock={RawClock():F3}s -> queue target {_target} buffers");
            }
        }
        else if (_inUnderrun && pending > 0) { _inUnderrun = false; XboxAudioManager.Log($"underrun recovered audio={AudioSec():F3}s pending={pending} tracks={Names()}"); }
        if (done == 0 || _instEnded) return;
        double drift = RawClock() - AudioSec();   // >= true drift (a buffer end is seen a little late)
        _drifts[_driftI] = drift; _driftI = (_driftI + 1) % _drifts.Length; if (_driftN < _drifts.Length) _driftN++;
        if (Math.Abs(drift) > Math.Abs(_maxObsDrift)) _maxObsDrift = drift;
        if (_driftN < 4 && drift > -SyncTolerance) return;
        double min = double.MaxValue;
        for (int i = 0; i < _driftN; i++) min = Math.Min(min, _drifts[i]);
        if ((_driftN >= 4 && min > SyncTolerance) || drift < -SyncTolerance)
        {
            double corr = drift < -SyncTolerance ? drift : min;
            _clockBase -= corr; _resyncs++;
            XboxAudioManager.Log($"RESYNC #{_resyncs} clock {(corr > 0 ? "ahead of" : "behind")} audio by {Math.Abs(corr) * 1000:F1} ms -> clock={RawClock():F3}s audio={AudioSec():F3}s tracks={Names()}");
            _driftN = 0; _driftI = 0;
        }
    }

    private double AudioSec() => _startSec + _consumed / (double)_outRate;

    /// <summary>Decode + mix one buffer from every track and submit it. Called with _lk held.</summary>
    private void MixOne()
    {
        if (_disposed || _instEnded) return;
        Array.Clear(_mix, 0, _mix.Length);
        int instN = -1, maxN = 0;
        foreach (var s in _srcs)
        {
            if (s.Ended) { if (s.Kind == 0) instN = 0; continue; }
            int n = 0;
            try
            {
                n = ReadStereo(s, _tmp, Frames, 0);
                if (n < Frames && s.Kind == 0 && _loop)
                {
                    s.R.SeekTo(0); s.FramesRead = 0; s.SLen = 0; s.Idx = 0; s.Frac = 0;
                    n += ReadStereo(s, _tmp, Frames - n, n * 2);
                }
            }
            catch (Exception e) { XboxAudioManager.Log($"decode error {KindName(s.Kind)} '{s.Name}': {e.Message} -> track ended"); }
            if (n < Frames) s.Ended = true;
            float g = s.Gain;
            for (int i = 0; i < n * 2; i++) _mix[i] += _tmp[i] * g;
            if (s.Kind == 0) instN = n;
            if (n > maxN) maxN = n;
        }
        int produced = instN >= 0 ? instN : maxN;
        bool end = instN >= 0 ? instN < Frames : maxN < Frames;
        if (produced > 0)
        {
            for (int i = 0; i < produced * 2; i++)
            {
                float v = _mix[i];
                if (v > 1f) v = 1f; else if (v < -1f) v = -1f;
                short sv = (short)(v * 32767f);
                _pcm[i * 2] = (byte)(sv & 0xFF);
                _pcm[i * 2 + 1] = (byte)((sv >> 8) & 0xFF);
            }
            _track.SubmitBuffer(_pcm, 0, produced * 4);
            _queued.Enqueue(produced);
            _outFrames += produced;
        }
        if (end)
        {
            _instEnded = true;
            _completedFlag = true;
            XboxAudioManager.Log($"END of music at {(_startSec + _outFrames / (double)_outRate):F3}s (underruns={_underruns} resyncs={_resyncs})");
        }
    }

    /// <summary>Read up to <paramref name="frames"/> stereo frames at the output rate into dst (from dstOff floats).</summary>
    private int ReadStereo(Src s, float[] dst, int frames, int dstOff)
    {
        if (s.Rate == _outRate)
        {
            int need = frames * s.Ch;
            if (s.Raw.Length < need) s.Raw = new float[need];
            int got = 0;
            while (got < need)
            {
                int r = s.R.ReadSamples(s.Raw, got, need - got);
                if (r <= 0) break;
                got += r;
            }
            int f = got / s.Ch;
            if (s.Ch == 1) for (int i = 0; i < f; i++) { float v = s.Raw[i]; dst[dstOff + i * 2] = v; dst[dstOff + i * 2 + 1] = v; }
            else for (int i = 0; i < f; i++) { dst[dstOff + i * 2] = s.Raw[i * s.Ch]; dst[dstOff + i * 2 + 1] = s.Raw[i * s.Ch + 1]; }
            s.FramesRead += f;
            return f;
        }
        // linear resampler (mods with 48 kHz vocals over a 44.1 kHz inst etc.)
        double step = s.Rate / (double)_outRate;
        int outN = 0;
        while (outN < frames)
        {
            if (s.Idx + 1 >= s.SLen)
            {
                if (!Refill(s)) break;
                continue;
            }
            float t = (float)s.Frac;
            int a = s.Idx * 2, b = a + 2;
            dst[dstOff + outN * 2] = s.S[a] + (s.S[b] - s.S[a]) * t;
            dst[dstOff + outN * 2 + 1] = s.S[a + 1] + (s.S[b + 1] - s.S[a + 1]) * t;
            outN++;
            s.Frac += step;
            while (s.Frac >= 1.0) { s.Frac -= 1.0; s.Idx++; }
        }
        return outN;
    }

    private bool Refill(Src s)
    {
        // keep the frame at Idx (interpolation start), append newly decoded frames after it
        int keep = s.SLen > 0 && s.Idx < s.SLen ? 1 : 0;
        const int chunk = 2048;
        float k0 = 0, k1 = 0;
        if (keep == 1) { k0 = s.S[s.Idx * 2]; k1 = s.S[s.Idx * 2 + 1]; }
        if (s.S.Length < (chunk + 1) * 2) s.S = new float[(chunk + 1) * 2];
        if (keep == 1) { s.S[0] = k0; s.S[1] = k1; }
        int need = chunk * s.Ch;
        if (s.Raw.Length < need) s.Raw = new float[need];
        int got = 0;
        while (got < need) { int r = s.R.ReadSamples(s.Raw, got, need - got); if (r <= 0) break; got += r; }
        int f = got / s.Ch;
        if (f == 0) return false;
        for (int i = 0; i < f; i++)
        {
            int o = (keep + i) * 2;
            if (s.Ch == 1) { s.S[o] = s.S[o + 1] = s.Raw[i]; }
            else { s.S[o] = s.Raw[i * s.Ch]; s.S[o + 1] = s.Raw[i * s.Ch + 1]; }
        }
        s.SLen = keep + f; s.Idx = 0;
        s.FramesRead += f;
        return true;
    }

    // ---- pause / resume / suspend / watchdog ----
    public void Pause(string why)
    {
        if (_disposed) return;
        if (StartPending) Start();
        lock (_lk)
        {
            if (_suspendPaused) { _suspendPaused = false; XboxAudioManager.Log($"PAUSE ({why}) while suspend-paused -> stays paused until ResumeMusic"); return; }
            if (_paused) return;
            RebaseClock(); _clock.Stop();
            _paused = true;
            try { _track.Pause(); } catch { }
            XboxAudioManager.Log($"PAUSE ({why}) at {RawClock():F3}s tracks={Names()}");
        }
    }

    public void Resume(string why)
    {
        if (_disposed) return;
        lock (_lk)
        {
            _suspendPaused = false;
            if (!_paused) return;
            _paused = false;
            ResumeTrack();
            _clock.Restart();
            _driftN = 0; _driftI = 0; _lastDoneAt = XboxAudioManager.Wall.Elapsed.TotalSeconds;
            XboxAudioManager.Log($"RESUME ({why}) at {RawClock():F3}s state={_track.State} pending={_track.PendingBufferCount} tracks={Names()}");
        }
    }

    private void ResumeTrack()
    {
        try
        {
            if (_track.State == SoundState.Paused) _track.Resume();
            else if (_track.State == SoundState.Stopped) _track.Play();
        }
        catch (Exception e) { XboxAudioManager.Log("resume failed: " + e.Message); }
    }

    public void SuspendPause()
    {
        if (_disposed || StartPending) return;
        lock (_lk)
        {
            if (_paused) { XboxAudioManager.Log("SUSPEND: already paused"); return; }
            RebaseClock(); _clock.Stop();
            _paused = true; _suspendPaused = true;
            try { _track.Pause(); } catch { }
            XboxAudioManager.Log($"SUSPEND pause at {RawClock():F3}s tracks={Names()}");
        }
    }

    public void SuspendResume()
    {
        if (_disposed) return;
        lock (_lk)
        {
            if (!_suspendPaused) { XboxAudioManager.Log("SUSPEND resume: not paused by suspend -> unchanged"); return; }
            _suspendPaused = false; _paused = false;
            ResumeTrack();
            _clock.Restart();
            _driftN = 0; _driftI = 0; _lastDoneAt = XboxAudioManager.Wall.Elapsed.TotalSeconds;
            XboxAudioManager.Log($"SUSPEND resume at {RawClock():F3}s state={_track.State} tracks={Names()}");
        }
    }

    private double _lastWatch;
    /// <summary>Main thread, every frame (checks 4x/s): a track that should play but is not (XAudio voice stopped
    /// after suspend / device change) is restarted; a dead mixer thread is restarted.</summary>
    public void Watchdog()
    {
        if (_disposed || !_started) return;
        if (_stuck)
        {
            _stuck = false;
            if (!_paused && !_instEnded) { _rebuilds++; Rebuild("stuck voice"); }
            return;
        }
        double now = XboxAudioManager.Wall.Elapsed.TotalSeconds;
        if (now - _lastWatch < 0.25) return;
        _lastWatch = now;
        lock (_lk)
        {
            if (_paused || _instEnded || _disposed || _devFrozen) return;
            SoundState st;
            try { st = _track.State; } catch { return; }
            if (st != SoundState.Playing)
            {
                XboxAudioManager.Log($"WATCHDOG track state={st} while it should play -> restart");
                ResumeTrack();
            }
        }
        if (_thread != null && !_thread.IsAlive && !_disposed && !_instEnded)
        {
            XboxAudioManager.Log("WATCHDOG mixer thread died -> restart");
            _thread = new Thread(Pump) { IsBackground = true, Name = "song-audio" };
            try { _thread.Priority = ThreadPriority.AboveNormal; } catch { }
            _thread.Start();
        }
    }

    public bool TakeCompleted()
    {
        if (!_completedFlag) return false;
        // fire once the last buffers were heard (FlxSound.onComplete)
        try { if (_track.PendingBufferCount > 0) return false; } catch { }
        _completedFlag = false;
        return true;
    }

    public void Seek(double sec) => SeekCore(sec, "SEEK");

    /// <summary>New XAudio voice, all tracks re-positioned to the position that was actually heard (main thread).</summary>
    private void Rebuild(string why)
    {
        double at;
        lock (_lk) at = AudioSec();
        _devFrozen = false;
        SeekCore(at, $"REBUILD #{_rebuilds} ({why})");
    }

    private void SeekCore(double sec, string tag)
    {
        if (_disposed) return;
        if (StartPending) Start();
        lock (_lk)
        {
            try { _track.Stop(); _track.Dispose(); } catch { }
            _track = NewTrack();
            _queued.Clear(); _consumed = 0; _outFrames = 0;
            _startSec = Math.Max(0, sec);
            _instEnded = false; _completedFlag = false; _inUnderrun = false;
            foreach (var s in _srcs)
            {
                long sp = (long)Math.Round(_startSec * s.Rate);
                try { if (sp < s.R.TotalSamples) { s.R.SeekTo(sp); s.Ended = false; } else s.Ended = true; } catch (Exception e) { XboxAudioManager.Log("seek failed: " + e.Message); }
                s.FramesRead = sp; s.SLen = 0; s.Idx = 0; s.Frac = 0;
            }
            _clockBase = _startSec; _driftN = 0; _driftI = 0; _stuck = false;
            if (tag == "SEEK") _lastReported = double.NegativeInfinity; // a rebuild never moves the song clock backwards (holds)
            _lastDoneAt = XboxAudioManager.Wall.Elapsed.TotalSeconds;
            for (int i = 0; i < 3; i++) MixOne();
            if (!_paused) { try { _track.Play(); } catch { } _clock.Restart(); }
            else _clock.Reset();
            XboxAudioManager.Log($"{tag} to {_startSec:F3}s paused={_paused} state={_track.State} tracks={Names()}");
        }
        if (_thread == null || !_thread.IsAlive)
        {
            _thread = new Thread(Pump) { IsBackground = true, Name = "song-audio" };
            try { _thread.Priority = ThreadPriority.AboveNormal; } catch { }
            _thread.Start();
        }
    }

    public void Dispose(string why)
    {
        if (_disposed) return;
        lock (_lk)
        {
            _disposed = true;
            XboxAudioManager.Log($"STOP ({why}) at {RawClock():F3}s audio={AudioSec():F3}s tracks={Names()} underruns={_underruns} resyncs={_resyncs} rebuilds={_rebuilds} maxObsDrift={_maxObsDrift * 1000:F1}ms");
            try { _track.Stop(); } catch { }
            try { _track.Dispose(); } catch { }
            foreach (var s in _srcs) { try { s.R.Dispose(); } catch { } }
            _srcs.Clear();
        }
    }

    public void LogStatus(string what)
    {
        lock (_lk)
        {
            if (_disposed) return;
            string st; int pend;
            try { st = _track.State.ToString(); pend = _track.PendingBufferCount; } catch { st = "?"; pend = -1; }
            var sb = new System.Text.StringBuilder();
            double instPos = double.NaN;
            foreach (var s in _srcs)
            {
                double p = s.FramesRead / (double)s.Rate;  // decoded position of this track
                if (s.Kind == 0) instPos = p;
                sb.Append($" {KindName(s.Kind)}={(s.Ended ? "Ended" : st)}@{p:F3}");
            }
            string sync = "";
            if (!double.IsNaN(instPos))
                foreach (var s in _srcs) if (s.Kind != 0 && !s.Ended) sync += $" d_{KindName(s.Kind)}={(s.FramesRead / (double)s.Rate - instPos) * 1000:F1}ms";
            XboxAudioManager.Log($"{what} clock={RawClock():F3}s audio={AudioSec():F3}s drift={(RawClock() - AudioSec()) * 1000:F1}ms state={st} pending={pend}/{_target}{sb}{sync} underruns={_underruns} resyncs={_resyncs}");
        }
    }

    private string Names()
    {
        var parts = new List<string>();
        foreach (var s in _srcs) parts.Add(KindName(s.Kind) + (s.Ended ? "(ended)" : ""));
        return parts.Count == 0 ? "none" : string.Join("+", parts);
    }

    private static string KindName(int k) => k == 0 ? "inst" : k == 1 ? "voicesP" : "voicesO";

    private void DevStall()
    {
        if (_devStalls.Count == 0 || !_started || _paused) return;
        double now = RawClock();
        for (int i = 0; i < _devStalls.Count; i++)
        {
            if (now < _devStalls[i].at) continue;
            int ms = _devStalls[i].ms;
            _devStalls.RemoveAt(i);
            XboxAudioManager.Log($"dev stall {ms} ms at {now:F3}s (FNF_AUDIO_STALL)");
            Thread.Sleep(ms);
            return;
        }
    }

    private void DevFreeze()
    {
        if (_devFreezeAt < 0 || !_started || _paused) return;
        if (RawClock() < _devFreezeAt) return;
        _devFreezeAt = -1;
        lock (_lk)
        {
            _devFrozen = true;
            try { _track.Pause(); } catch { }   // MonoGame state = Paused, Watchdog state check skipped while frozen
            XboxAudioManager.Log($"dev freeze: voice stopped at {RawClock():F3}s (FNF_AUDIO_FREEZE)");
        }
    }

    private static double ParseFreeze()
    {
        try
        {
            string v = Environment.GetEnvironmentVariable("FNF_AUDIO_FREEZE");
            if (!string.IsNullOrEmpty(v) && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) return d;
        }
        catch { }
        return -1;
    }

    private static List<(double at, int ms)> ParseStalls()
    {
        var l = new List<(double at, int ms)>();
        try
        {
            string v = Environment.GetEnvironmentVariable("FNF_AUDIO_STALL");
            if (string.IsNullOrEmpty(v)) return l;
            foreach (var p in v.Split(','))
            {
                var a = p.Split(':');
                if (a.Length == 2 && double.TryParse(a[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var at) && int.TryParse(a[1], out var ms))
                    l.Add((at, ms));
            }
        }
        catch { }
        return l;
    }
}
