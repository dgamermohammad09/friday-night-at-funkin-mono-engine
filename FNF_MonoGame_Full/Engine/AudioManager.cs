using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Diagnostics;

namespace FNF_MonoGame.Engine;

public class AudioManager : IDisposable
{
    private DynamicSoundEffectInstance _currentMusic;
    private WaveStream _vorbisReader;
    private IWaveProvider _waveProvider;
    private Thread _streamThread;
    private int _streamGen; // bumped by StopMusic/new music: an old stream thread that outlived Join(200) must stop, not feed the new track
    private bool _isStreaming;
    private bool _loop;
    
    // Intro?loop music chaining (play intro, then auto-start loop on completion)
    private string _pendingLoopPath;
    private Action _onMusicComplete;
    
    // Thread-safe deferred callback (streaming thread sets this, main thread invokes)
    private volatile Action _pendingCallback;
    
    // High-precision playback timer (replaces VorbisWaveReader.CurrentTime which tracks
    // the READ position, not the actual PLAYBACK position ? causing 200-300ms offset
    // and jitter as the streaming thread reads chunks ahead)
    private readonly Stopwatch _playbackTimer = new();
    private double _playbackOffset; // accumulated time before current play/resume
    private bool _musicPaused;
    
    // Voice track (plays alongside music for FNF vocals)
    private DynamicSoundEffectInstance _voiceTrack;
    private WaveStream _voiceReader;
    private IWaveProvider _voiceProvider;
    private Thread _voiceThread;
    private bool _isVoiceStreaming;
    
    // Opponent voice track (second vocal track)
    private DynamicSoundEffectInstance _opponentVoiceTrack;
    private WaveStream _opponentVoiceReader;
    private IWaveProvider _opponentVoiceProvider;
    private Thread _opponentVoiceThread;
    private bool _isOpponentVoiceStreaming;
    
    private readonly Dictionary<string, SoundEffect> _sounds = new();
    private float _musicVolume = 0.8f;
    private float _sfxVolume = 1.0f;
    private bool _muted;
    
    // Music fade-out state (original: FlxG.sound.music.fadeOut)
    private float _fadeOutTimer;
    private float _fadeOutDuration;
    private float _fadeOutStartVolume;
    private bool _isFadingOut;
    private Action _fadeOutCallback;

    // Music fade-in state
    private float _fadeInTimer;
    private float _fadeInDuration;
    private float _fadeInStartVolume;
    private float _fadeInEndVolume;
    private bool _isFadingIn;

    // Voice resync drift threshold in milliseconds
    // With DynamicSoundEffectInstance streaming, seeking the reader while the
    // streaming thread reads causes race conditions (garbled audio).
    // Use a very high threshold so resync only fires on catastrophic drift.
    private const double VOICE_RESYNC_THRESHOLD_MS = 500.0;
    
    // Lock for voice reader access (resync on main thread vs streaming thread)
    private readonly object _voiceLock = new();
    private readonly object _opponentVoiceLock = new();
    
    // Voice playback timers (same approach as music: Stopwatch tracks actual playback)
    private readonly Stopwatch _voicePlaybackTimer = new();
    private double _voicePlaybackOffset;
    private readonly Stopwatch _opponentVoicePlaybackTimer = new();
    private double _opponentVoicePlaybackOffset;
    
    /// <summary>
    /// Extra content roots for resolving audio paths (e.g. funkin.assets).
    /// Set by FNFGame after AssetManager is initialized.
    /// </summary>
    public List<string> ExtraContentRoots { get; } = new();
    
    public float MusicVolume 
    { 
        get => _musicVolume;
        set 
        { 
            _musicVolume = Math.Clamp(value, 0f, 1f); 
            if (_currentMusic != null && !_isFadingOut) 
                _currentMusic.Volume = _muted ? 0f : _musicVolume; 
        }
    }
    
    public float SfxVolume 
    { 
        get => _sfxVolume; 
        set => _sfxVolume = Math.Clamp(value, 0f, 1f); 
    }
    
    public bool Muted
    {
        get => _muted;
        set
        {
            _muted = value;
            // Apply mute to current music and voices
            float effVol = _muted ? 0f : _musicVolume;
            if (_currentMusic != null && !_isFadingOut)
                _currentMusic.Volume = effVol;
            if (_voiceTrack != null && !_voiceTrack.IsDisposed)
                _voiceTrack.Volume = effVol;
            if (_opponentVoiceTrack != null && !_opponentVoiceTrack.IsDisposed)
                _opponentVoiceTrack.Volume = effVol;
        }
    }
    
    /// <summary>
    /// Actual playback position in milliseconds, tracked via high-precision Stopwatch.
    /// VorbisWaveReader.CurrentTime is the DECODE position (200-300ms ahead of playback
    /// and jumps around as the streaming thread reads), causing arrow jitter.
    /// </summary>
    /// <summary>Dev (FNF_SYNCLOG=1): stream underrun counts [music, voice, opponent voice] = audio fell behind the song clock.</summary>
    public static readonly bool DevSyncLog = System.Environment.GetEnvironmentVariable("FNF_SYNCLOG") == "1";
    public static readonly int[] DevUnderruns = new int[3];
    public double MusicPosition => _musicPaused 
        ? _playbackOffset * 1000.0 
        : (_playbackOffset + _playbackTimer.Elapsed.TotalSeconds * _rate) * 1000.0;
    public bool MusicPlaying => _currentMusic != null && _currentMusic.State == SoundState.Playing;
    public DynamicSoundEffectInstance Music => _currentMusic;
    public float MusicVisualizerLevel => _musicVisualizerLevel;

    private volatile float _musicVisualizerLevel;
    
    /// <summary>
    /// Call from the main game Update loop to process deferred audio callbacks
    /// (e.g., intro?loop chaining) on the main thread.
    /// </summary>
    public void Update(float deltaTime = 1f / 60f)
    {
        var cb = _pendingCallback;
        if (cb != null)
        {
            _pendingCallback = null;
            cb.Invoke();
        }

        // Tick music fade-in
        if (_isFadingIn && _currentMusic != null)
        {
            _fadeInTimer += deltaTime;
            float t = Math.Clamp(_fadeInTimer / _fadeInDuration, 0f, 1f);
            float fadeVolume = _muted ? 0f : MathHelper.Lerp(_fadeInStartVolume, _fadeInEndVolume, t);
            _currentMusic.Volume = fadeVolume;
            if (_voiceTrack != null && !_voiceTrack.IsDisposed)
                _voiceTrack.Volume = fadeVolume;
            if (_opponentVoiceTrack != null && !_opponentVoiceTrack.IsDisposed)
                _opponentVoiceTrack.Volume = fadeVolume;
            if (t >= 1f)
                _isFadingIn = false;
        }

        // Tick music fade-out (fades voices alongside music ? matches original FNF)
        if (_isFadingOut && _currentMusic != null)
        {
            _fadeOutTimer += deltaTime;
            float t = Math.Clamp(_fadeOutTimer / _fadeOutDuration, 0f, 1f);
            float fadeVolume = _muted ? 0f : _fadeOutStartVolume * (1f - t);
            _currentMusic.Volume = fadeVolume;
            // Fade voice tracks in sync
            if (_voiceTrack != null && !_voiceTrack.IsDisposed)
                _voiceTrack.Volume = fadeVolume;
            if (_opponentVoiceTrack != null && !_opponentVoiceTrack.IsDisposed)
                _opponentVoiceTrack.Volume = fadeVolume;
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
    
    /// <summary>
    /// Set player voice volume (0 = muted, 1 = full). Matches original vocals.playerVolume.
    /// </summary>
    public bool HasPlayerVoice => PrepareOnly ? _prepFound[1] : (_voiceTrack != null && !_voiceTrack.IsDisposed);
    public bool HasOpponentVoice => PrepareOnly ? _prepFound[2] : (_opponentVoiceTrack != null && !_opponentVoiceTrack.IsDisposed);

    public void FadeIn(float duration = 1f, float endVolume = 1f)
    {
        if (_currentMusic == null) return;
        FadeIn(duration, _currentMusic.Volume, endVolume);
    }

    public void FadeIn(float duration, float startVolume, float endVolume)
    {
        if (_currentMusic == null) return;
        _fadeInDuration = Math.Max(duration, 0.001f);
        _fadeInTimer = 0f;
        _fadeInStartVolume = Math.Clamp(startVolume, 0f, 1f);
        _fadeInEndVolume = Math.Clamp(endVolume, 0f, 1f);
        _isFadingIn = true;
    }
    
    public void SetPlayerVoiceVolume(float volume)
    {
        if (_voiceTrack != null)
            _voiceTrack.Volume = _muted ? 0f : Math.Clamp(volume, 0f, 1f) * _musicVolume;
    }
    
    /// <summary>
    /// Set opponent voice volume.
    /// </summary>
    public void SetOpponentVoiceVolume(float volume)
    {
        if (_opponentVoiceTrack != null)
            _opponentVoiceTrack.Volume = _muted ? 0f : Math.Clamp(volume, 0f, 1f) * _musicVolume;
    }
    
    /// <summary>
    /// Play an intro music file (non-looping), then automatically start the loop music on completion.
    /// If the intro file doesn't exist, falls back to playing the loop directly.
    /// </summary>
    public void PlayMusicWithIntro(string introPath, string loopPath)
    {
        string contentPath = System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot);
        string introOgg = ResolveAudioPath(contentPath, introPath);
        
        if (introOgg != null && global::FNF_MonoGame.Engine.FsCI.FileExists(introOgg))
        {
            // Play intro (non-looping), chain to loop on completion
            // Note: PlayMusic calls StopMusic which clears _onMusicComplete,
            // so we set the callback AFTER PlayMusic
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
            // No intro file, just play the loop directly
            PlayMusic(loopPath, true);
        }
    }
    
    public void PlayMusic(string path, bool loop = true, int startTimeMs = 0)
    {
        if (PrepareOnly) { PrepareReader(path, 0); return; }
        StopMusic(); _rate = 1.0; // a new track always starts at normal speed (PlayScene re-applies its practice rate)
        
        string contentPath = System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot);
        string oggPath = ResolveAudioPath(contentPath, path);
        
        // Also try tracks/ subfolder (e.g. songs/tutorial/tracks/Inst.ogg)
        if (oggPath == null)
        {
            string dirPart = Path.GetDirectoryName(path);
            string filePart = Path.GetFileName(path);
            if (dirPart != null)
            {
                oggPath = ResolveAudioPath(contentPath, Path.Combine(dirPart, "tracks", filePart));
            }
        }
        
        if (oggPath == null || !global::FNF_MonoGame.Engine.FsCI.FileExists(oggPath))
        {
            Console.WriteLine($"Audio not found: {oggPath}");
            return;
        }
        
        Console.WriteLine($"Playing audio: {oggPath}");
        
        try
        {
            _vorbisReader = CreateWaveReader(oggPath);
            _musicLengthCache = _lengthCache.TryGetValue(oggPath, out var __len) ? __len : -1;
            _loop = loop;

            double playbackStartSeconds = 0;
            if (startTimeMs > 0)
            {
                try
                {
                    var startTime = TimeSpan.FromMilliseconds(startTimeMs);
                    if (startTime < _vorbisReader.TotalTime)
                    {
                        _vorbisReader.CurrentTime = startTime;
                        playbackStartSeconds = startTime.TotalSeconds;
                    }
                }
                catch (Exception seekEx)
                {
                    Console.WriteLine($"Audio seek failed for '{path}': {seekEx.Message}");
                }
            }
            
            // Convert VorbisWaveReader IEEE Float output to 16-bit PCM for MonoGame
            var sampleProvider = _vorbisReader.ToSampleProvider();
            
            // Resample to 44100Hz if needed ? DynamicSoundEffectInstance may reject
            // non-standard rates on some audio drivers
            int targetRate = _vorbisReader.WaveFormat.SampleRate;
            int[] supportedRates = { 8000, 11025, 16000, 22050, 24000, 32000, 44100, 48000 };
            if (System.Array.IndexOf(supportedRates, targetRate) < 0)
            {
                Console.WriteLine($"Audio: Resampling from {targetRate}Hz to 44100Hz");
                var resampled = new WdlResamplingSampleProvider(sampleProvider, 44100);
                _waveProvider = new SampleToWaveProvider16(resampled);
                targetRate = 44100;
            }
            else
            {
                _waveProvider = new SampleToWaveProvider16(sampleProvider);
            }
            
            int audioChannels = _vorbisReader.WaveFormat.Channels;
            // MonoGame only supports Mono or Stereo
            if (audioChannels > 2) audioChannels = 2;
            
            // Create dynamic sound effect for streaming
            _currentMusic = new DynamicSoundEffectInstance(
                targetRate,
                audioChannels == 1 ? AudioChannels.Mono : AudioChannels.Stereo
            );
            
            _currentMusic.Volume = _muted ? 0f : _musicVolume;
            
            // Pre-fill buffers before starting playback to prevent "Queue empty" crashes
            // DynamicSoundEffectInstance throws if its internal queue drains between frames
            int sampleRate = _vorbisReader.WaveFormat.SampleRate;
            int channels = _vorbisReader.WaveFormat.Channels;
            int bufSize = sampleRate * channels * 2 / 10; // 100ms
            byte[] preBuf = new byte[bufSize];
            for (int i = 0; i < 3; i++)
            {
                int read = _waveProvider.Read(preBuf, 0, preBuf.Length);
                if (read > 0)
                    _currentMusic.SubmitBuffer(preBuf, 0, read);
            }
            
            // Start streaming thread
            _isStreaming = true;
            int gen = System.Threading.Interlocked.Increment(ref _streamGen);
            _streamThread = new Thread(() => StreamAudio(gen));
            _streamThread.IsBackground = true;
            _streamThread.Start();
            
            // Start playback timer ? this is our source of truth for song position
            _playbackOffset = playbackStartSeconds;
            _musicPaused = false;
            _playbackTimer.Restart();
            
            _currentMusic.Play();
            Console.WriteLine($"Music started! Sample rate: {_vorbisReader.WaveFormat.SampleRate}, Channels: {_vorbisReader.WaveFormat.Channels}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error playing audio: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
    
    private void StreamAudio(int gen)
    {
        // Buffer size: 100ms of 16-bit PCM audio data
        int sampleRate = _vorbisReader.WaveFormat.SampleRate;
        int channels = _vorbisReader.WaveFormat.Channels;
        int bytesPerSample = 2; // 16-bit = 2 bytes
        int bufferSize = sampleRate * channels * bytesPerSample / 10; // 100ms
        byte[] buffer = new byte[bufferSize];
        
        while (_isStreaming && _waveProvider != null && gen == _streamGen)
        {
            try
            {
                if (_currentMusic == null || _currentMusic.IsDisposed)
                    break;

                // Don't advance the stream while paused: reading would push _vorbisReader
                // forward and newly submitted buffers can cause DynamicSoundEffectInstance
                // to auto-resume playback on some platforms (the "music keeps playing during
                // pause" bug). Just idle until resumed.
                if (_musicPaused)
                {
                    Thread.Sleep(10);
                    continue;
                }

                // Submit more data when buffer is running low
                if (DevSyncLog && _currentMusic.PendingBufferCount == 0 && _currentMusic.State == SoundState.Playing) DevUnderruns[0]++;
                while (_currentMusic.PendingBufferCount < 3 && _isStreaming && !_musicPaused && gen == _streamGen)
                {
                    int bytesRead = _waveProvider.Read(buffer, 0, buffer.Length);
                    
                    if (bytesRead == 0)
                    {
                        if (_loop)
                        {
                            _vorbisReader.Position = 0;
                            continue;
                        }
                        else
                        {
                            _isStreaming = false;
                            // Defer callback to main thread (audio objects must be created on main thread)
                            var onComplete = _onMusicComplete;
                            _onMusicComplete = null;
                            if (onComplete != null)
                                _pendingCallback = onComplete;
                            break;
                        }
                    }

                    UpdateMusicVisualizerLevel(buffer, bytesRead);
                    
                    _currentMusic.SubmitBuffer(buffer, 0, bytesRead);
                }
                
                Thread.Sleep(10);
            }
            catch (Exception ex)
            {
                if (gen == _streamGen) Console.WriteLine($"Audio streaming error: {ex.Message}"); // else: track was replaced/stopped under us
                break;
            }
        }
    }
    
    public void Stop() => StopMusic();

    public void StopMusic() 
    { 
        _isStreaming = false;
        System.Threading.Interlocked.Increment(ref _streamGen);
        _onMusicComplete = null;
        _pendingCallback = null;
        _pendingLoopPath = null;
        _isFadingOut = false;
        _fadeOutCallback = null;
        _playbackTimer.Stop();
        _musicPaused = false;
        _streamThread?.Join(200);
        _streamThread = null;
        
        _currentMusic?.Stop();
        _currentMusic?.Dispose();
        _currentMusic = null;
        
        _waveProvider = null;
        _vorbisReader?.Dispose();
        _vorbisReader = null;
        _musicVisualizerLevel = 0f;
        
        StopVoices();
    }

    private void UpdateMusicVisualizerLevel(byte[] buffer, int bytesRead)
    {
        if (buffer == null || bytesRead <= 1)
        {
            _musicVisualizerLevel *= 0.9f;
            return;
        }

        int samples = bytesRead / 2;
        if (samples <= 0)
        {
            _musicVisualizerLevel *= 0.9f;
            return;
        }

        double sumSquares = 0;
        for (int i = 0; i + 1 < bytesRead; i += 2)
        {
            short pcm = (short)(buffer[i] | (buffer[i + 1] << 8));
            float v = pcm / 32768f;
            sumSquares += v * v;
        }

        float rms = (float)Math.Sqrt(sumSquares / samples);
        float normalized = Math.Clamp((rms - 0.005f) * 10f, 0f, 1f);

        float current = _musicVisualizerLevel;
        float attack = 0.45f;
        float release = 0.12f;
        float t = normalized > current ? attack : release;
        _musicVisualizerLevel = current + (normalized - current) * t;
    }
    
    public void StopVoices()
    {
        _isVoiceStreaming = false;
        _voiceThread?.Join(200);
        _voiceThread = null;
        
        _voiceTrack?.Stop();
        _voiceTrack?.Dispose();
        _voiceTrack = null;
        _voicePlaybackTimer.Stop();
        _voicePlaybackOffset = 0;
        
        _voiceProvider = null;
        _voiceReader?.Dispose();
        _voiceReader = null;
        
        StopOpponentVoices();
    }
    
    public void StopOpponentVoices()
    {
        _isOpponentVoiceStreaming = false;
        _opponentVoiceThread?.Join(200);
        _opponentVoiceThread = null;
        
        _opponentVoiceTrack?.Stop();
        _opponentVoiceTrack?.Dispose();
        _opponentVoiceTrack = null;
        _opponentVoicePlaybackTimer.Stop();
        _opponentVoicePlaybackOffset = 0;
        
        _opponentVoiceProvider = null;
        _opponentVoiceReader?.Dispose();
        _opponentVoiceReader = null;
    }
    
    public void PauseMusic() 
    { 
        // Save accumulated playback time and stop the timer
        _playbackOffset += _playbackTimer.Elapsed.TotalSeconds * _rate;
        _playbackTimer.Stop();
        _musicPaused = true;
        
        // Save voice playback offsets too
        _voicePlaybackOffset += _voicePlaybackTimer.Elapsed.TotalSeconds * _rate; // seconds (was ms: every pause/resume forced a vocal seek, drift ~11e6 ms)
        _voicePlaybackTimer.Stop();
        _opponentVoicePlaybackOffset += _opponentVoicePlaybackTimer.Elapsed.TotalSeconds * _rate;
        _opponentVoicePlaybackTimer.Stop();
        
        _currentMusic?.Pause();
        _voiceTrack?.Pause();
        _opponentVoiceTrack?.Pause();
    }
    
    public void ResumeMusic() 
    { 
        // Restart timer from zero (offset already saved)
        _musicPaused = false;
        _playbackTimer.Restart();
        
        // Restart voice timers from zero (offsets already saved)
        _voicePlaybackTimer.Restart();
        _opponentVoicePlaybackTimer.Restart();
        
        _currentMusic?.Resume();
        _voiceTrack?.Resume();
        _opponentVoiceTrack?.Resume();
    }
    
    /// <summary>
    /// Get the decode-side position of the voice reader in milliseconds.
    /// Used for resync checks (original FNF: resyncVocals).
    /// Returns -1 if no voice track is active.
    /// </summary>
    public double VoicePosition
    {
        get
        {
            if (_voiceReader == null) return -1;
            try { return _voiceReader.CurrentTime.TotalMilliseconds; }
            catch { return -1; }
        }
    }
    
    /// <summary>
    /// Get the total duration of the current music track in seconds.
    /// Returns 0 if no music is loaded.
    /// </summary>
    public double MusicLength
    {
        get
        {
            if (_vorbisReader == null) return 0;
            if (_musicLengthCache >= 0) return _musicLengthCache;
            try { return _musicLengthCache = _vorbisReader.TotalTime.TotalSeconds; }
            catch { return 0; }
        }
    }
    
    /// <summary>
    /// Resync voice tracks to match the music playback position.
    /// Voice tracks start at the same time as music and stream at the same rate,
    /// so they stay naturally in sync. Only seek on catastrophic drift (>500ms)
    /// which indicates a pause/resume desync. Uses locks to prevent race conditions
    /// with the streaming threads.
    /// </summary>
    public void ResyncVoices()
    {
        // Voices are started simultaneously with music and stream at native rate.
        // DynamicSoundEffectInstance handles timing internally.
        // Seeking the WaveStream reader while the streaming thread reads causes
        // garbled audio, so we only resync on catastrophic drift (>500ms).
        if (!MusicPlaying) return;

        double musicPosMs = MusicPosition;

        // Resync player voice
        if (_voiceTrack != null && !_voiceTrack.IsDisposed && _voiceReader != null)
        {
            double voicePosMs = (_voicePlaybackOffset + _voicePlaybackTimer.Elapsed.TotalSeconds * _rate) * 1000.0;
            double drift = Math.Abs(voicePosMs - musicPosMs);

            if (drift > VOICE_RESYNC_THRESHOLD_MS)
            {
                Console.WriteLine($"Voice resync: drift={drift:F1}ms, seeking to {musicPosMs:F1}ms");
                try
                {
                    lock (_voiceLock)
                    {
                        var targetTime = TimeSpan.FromMilliseconds(musicPosMs);
                        if (targetTime >= TimeSpan.Zero && targetTime < _voiceReader.TotalTime)
                        {
                            _voiceReader.CurrentTime = targetTime;
                            _voicePlaybackOffset = musicPosMs / 1000.0;
                            _voicePlaybackTimer.Restart();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Voice resync error: {ex.Message}");
                }
            }
        }

        // Resync opponent voice
        if (_opponentVoiceTrack != null && !_opponentVoiceTrack.IsDisposed && _opponentVoiceReader != null)
        {
            double oppVoicePosMs = (_opponentVoicePlaybackOffset + _opponentVoicePlaybackTimer.Elapsed.TotalSeconds * _rate) * 1000.0;
            double drift = Math.Abs(oppVoicePosMs - musicPosMs);

            if (drift > VOICE_RESYNC_THRESHOLD_MS)
            {
                Console.WriteLine($"Opponent voice resync: drift={drift:F1}ms, seeking to {musicPosMs:F1}ms");
                try
                {
                    lock (_opponentVoiceLock)
                    {
                        var targetTime = TimeSpan.FromMilliseconds(musicPosMs);
                        if (targetTime >= TimeSpan.Zero && targetTime < _opponentVoiceReader.TotalTime)
                        {
                            _opponentVoiceReader.CurrentTime = targetTime;
                            _opponentVoicePlaybackOffset = musicPosMs / 1000.0;
                            _opponentVoicePlaybackTimer.Restart();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Opponent voice resync error: {ex.Message}");
                }
            }
        }
    }
    
    /// <summary>
    /// Fade out the current music over the given duration (seconds).
    /// Original FNF: FlxG.sound.music.fadeOut(duration). Calls onComplete when done.
    /// </summary>
    public void FadeOutMusic(float duration = 0.5f, Action onComplete = null)
    {
        if (_currentMusic == null || !MusicPlaying)
        {
            onComplete?.Invoke();
            return;
        }
        _fadeOutTimer = 0;
        _fadeOutDuration = Math.Max(0.01f, duration);
        _fadeOutStartVolume = _currentMusic.Volume;
        _fadeOutCallback = onComplete;
        _isFadingOut = true;
    }
    
    /// <summary>
    /// Play a voice track alongside the current music (for FNF vocals)
    /// </summary>
    public void PlayVoices(string path)
    {
        if (PrepareOnly) { PrepareReader(path, 1); return; }
        StopVoices();
        
        string contentPath = System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot);
        string oggPath = ResolveAudioPath(contentPath, path);
        
        // Also try tracks/ subfolder
        if (oggPath == null)
        {
            string dirPart = Path.GetDirectoryName(path);
            string filePart = Path.GetFileName(path);
            if (dirPart != null)
            {
                oggPath = ResolveAudioPath(contentPath, Path.Combine(dirPart, "tracks", filePart));
            }
        }
        
        if (oggPath == null || !global::FNF_MonoGame.Engine.FsCI.FileExists(oggPath))
        {
            Console.WriteLine($"Voice track not found: {path}");
            return;
        }
        
        try
        {
            _voiceReader = CreateWaveReader(oggPath);
            var sampleProvider = _voiceReader.ToSampleProvider();
            int voiceRate = _voiceReader.WaveFormat.SampleRate;
            int[] supportedRates = { 8000, 11025, 16000, 22050, 24000, 32000, 44100, 48000 };
            if (System.Array.IndexOf(supportedRates, voiceRate) < 0)
            {
                var resampled = new WdlResamplingSampleProvider(sampleProvider, 44100);
                _voiceProvider = new SampleToWaveProvider16(resampled);
                voiceRate = 44100;
            }
            else
            {
                _voiceProvider = new SampleToWaveProvider16(sampleProvider);
            }
            
            int vCh = Math.Min(_voiceReader.WaveFormat.Channels, 2);
            _voiceTrack = new DynamicSoundEffectInstance(
                voiceRate,
                vCh == 1 ? AudioChannels.Mono : AudioChannels.Stereo
            );
            
            _voiceTrack.Volume = _muted ? 0f : _musicVolume;
            
            // Pre-fill buffers to prevent Queue empty crash
            int vrSampleRate = _voiceReader.WaveFormat.SampleRate;
            int vrChannels = _voiceReader.WaveFormat.Channels;
            int vrBufSize = vrSampleRate * vrChannels * 2 / 10;
            byte[] vrBuf = new byte[vrBufSize];
            for (int i = 0; i < 3; i++)
            {
                int read = _voiceProvider.Read(vrBuf, 0, vrBuf.Length);
                if (read > 0) _voiceTrack.SubmitBuffer(vrBuf, 0, read);
            }
            
            _isVoiceStreaming = true;
            _voiceThread = new Thread(StreamVoice);
            _voiceThread.IsBackground = true;
            _voiceThread.Start();
            
            _voiceTrack.Play();
            _voicePlaybackOffset = 0;
            _voicePlaybackTimer.Restart();
            Console.WriteLine($"Voice track started: {oggPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error playing voice track: {ex.Message}");
        }
    }
    
    private void StreamVoice()
    {
        int sampleRate = _voiceReader.WaveFormat.SampleRate;
        int channels = _voiceReader.WaveFormat.Channels;
        int bufferSize = sampleRate * channels * 2 / 10; // 100ms of 16-bit PCM
        byte[] buffer = new byte[bufferSize];
        
        while (_isVoiceStreaming && _voiceProvider != null)
        {
            try
            {
                if (_voiceTrack == null || _voiceTrack.IsDisposed)
                    break;

                // Mirror the main music streamer: don't advance the voice reader while paused.
                if (_musicPaused)
                {
                    Thread.Sleep(10);
                    continue;
                }

                if (DevSyncLog && _voiceTrack.PendingBufferCount == 0 && _voiceTrack.State == SoundState.Playing) DevUnderruns[1]++;

                while (_voiceTrack.PendingBufferCount < 3 && _isVoiceStreaming && !_musicPaused)
                {
                    int bytesRead;
                    lock (_voiceLock)
                    {
                        bytesRead = _voiceProvider.Read(buffer, 0, buffer.Length);
                    }
                    if (bytesRead == 0)
                    {
                        _isVoiceStreaming = false;
                        break;
                    }
                    _voiceTrack.SubmitBuffer(buffer, 0, bytesRead);
                }
                
                Thread.Sleep(10);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Voice streaming error: {ex.Message}");
                break;
            }
        }
    }
    
    /// <summary>
    /// Play opponent voice track alongside the current music (for FNF opponent vocals)
    /// </summary>
    public void PlayOpponentVoices(string path)
    {
        if (PrepareOnly) { PrepareReader(path, 2); return; }
        StopOpponentVoices();
        
        string contentPath = System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot);
        string oggPath = ResolveAudioPath(contentPath, path);
        
        // Also try tracks/ subfolder
        if (oggPath == null)
        {
            string dirPart = Path.GetDirectoryName(path);
            string filePart = Path.GetFileName(path);
            if (dirPart != null)
            {
                oggPath = ResolveAudioPath(contentPath, Path.Combine(dirPart, "tracks", filePart));
            }
        }
        
        if (oggPath == null || !global::FNF_MonoGame.Engine.FsCI.FileExists(oggPath))
        {
            Console.WriteLine($"Opponent voice track not found: {path}");
            return;
        }
        
        try
        {
            _opponentVoiceReader = CreateWaveReader(oggPath);
            var sampleProvider = _opponentVoiceReader.ToSampleProvider();
            int ovRate = _opponentVoiceReader.WaveFormat.SampleRate;
            int[] supportedRates = { 8000, 11025, 16000, 22050, 24000, 32000, 44100, 48000 };
            if (System.Array.IndexOf(supportedRates, ovRate) < 0)
            {
                var resampled = new WdlResamplingSampleProvider(sampleProvider, 44100);
                _opponentVoiceProvider = new SampleToWaveProvider16(resampled);
                ovRate = 44100;
            }
            else
            {
                _opponentVoiceProvider = new SampleToWaveProvider16(sampleProvider);
            }
            
            int ovCh = Math.Min(_opponentVoiceReader.WaveFormat.Channels, 2);
            _opponentVoiceTrack = new DynamicSoundEffectInstance(
                ovRate,
                ovCh == 1 ? AudioChannels.Mono : AudioChannels.Stereo
            );
            
            _opponentVoiceTrack.Volume = _muted ? 0f : _musicVolume;
            
            // Pre-fill buffers to prevent Queue empty crash
            int ovSampleRate = _opponentVoiceReader.WaveFormat.SampleRate;
            int ovChannels = _opponentVoiceReader.WaveFormat.Channels;
            int ovBufSize = ovSampleRate * ovChannels * 2 / 10;
            byte[] ovBuf = new byte[ovBufSize];
            for (int i = 0; i < 3; i++)
            {
                int read = _opponentVoiceProvider.Read(ovBuf, 0, ovBuf.Length);
                if (read > 0) _opponentVoiceTrack.SubmitBuffer(ovBuf, 0, read);
            }
            
            _isOpponentVoiceStreaming = true;
            _opponentVoiceThread = new Thread(StreamOpponentVoice);
            _opponentVoiceThread.IsBackground = true;
            _opponentVoiceThread.Start();
            
            _opponentVoiceTrack.Play();
            _opponentVoicePlaybackOffset = 0;
            _opponentVoicePlaybackTimer.Restart();
            Console.WriteLine($"Opponent voice track started: {oggPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error playing opponent voice track: {ex.Message}");
        }
    }
    
    private void StreamOpponentVoice()
    {
        int sampleRate = _opponentVoiceReader.WaveFormat.SampleRate;
        int channels = _opponentVoiceReader.WaveFormat.Channels;
        int bufferSize = sampleRate * channels * 2 / 10; // 100ms of 16-bit PCM
        byte[] buffer = new byte[bufferSize];
        
        while (_isOpponentVoiceStreaming && _opponentVoiceProvider != null)
        {
            try
            {
                if (_opponentVoiceTrack == null || _opponentVoiceTrack.IsDisposed)
                    break;

                // Mirror the main music streamer: don't advance the opponent voice reader while paused.
                if (_musicPaused)
                {
                    Thread.Sleep(10);
                    continue;
                }

                if (DevSyncLog && _opponentVoiceTrack.PendingBufferCount == 0 && _opponentVoiceTrack.State == SoundState.Playing) DevUnderruns[2]++;

                while (_opponentVoiceTrack.PendingBufferCount < 3 && _isOpponentVoiceStreaming && !_musicPaused)
                {
                    int bytesRead;
                    lock (_opponentVoiceLock)
                    {
                        bytesRead = _opponentVoiceProvider.Read(buffer, 0, buffer.Length);
                    }
                    if (bytesRead == 0)
                    {
                        _isOpponentVoiceStreaming = false;
                        break;
                    }
                    _opponentVoiceTrack.SubmitBuffer(buffer, 0, bytesRead);
                }
                
                Thread.Sleep(10);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Opponent voice streaming error: {ex.Message}");
                break;
            }
        }
    }
    
    /// <summary>
    /// Create the appropriate WaveStream reader based on file extension.
    /// OGG uses VorbisWaveReader, everything else uses AudioFileReader.
    /// </summary>
    // --- Preloading (the original loads every song track before the countdown; opening an OGG reader and
    // scanning its length took 0.5-3 s at song start here). PrepareOnly: Play* calls only open the readers.
    public bool PrepareOnly;
    private readonly bool[] _prepFound = new bool[3];
    private double _musicLengthCache = -1;
    private readonly Dictionary<string, WaveStream> _preopened = new Dictionary<string, WaveStream>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _lengthCache = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

    public void BeginPrepare() { PrepareOnly = true; _prepFound[0] = _prepFound[1] = _prepFound[2] = false; }
    public void EndPrepare() { PrepareOnly = false; }

    private void PrepareReader(string path, int kind)
    {
        string contentPath = System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot);
        string oggPath = ResolveAudioPath(contentPath, path);
        if (oggPath == null)
        {
            string dirPart = Path.GetDirectoryName(path);
            string filePart = Path.GetFileName(path);
            if (dirPart != null) oggPath = ResolveAudioPath(contentPath, Path.Combine(dirPart, "tracks", filePart));
        }
        if (oggPath == null || !global::FNF_MonoGame.Engine.FsCI.FileExists(oggPath)) return;
        _prepFound[kind] = true;
        if (_preopened.ContainsKey(oggPath)) return;
        try
        {
            var r = CreateWaveReaderRaw(oggPath);
            try { _lengthCache[oggPath] = r.TotalTime.TotalSeconds; r.Position = 0; } catch { }
            _preopened[oggPath] = r;
            Console.WriteLine($"[PRELOAD] audio reader ready: {oggPath} len={(_lengthCache.TryGetValue(oggPath, out var l) ? l : -1):F2}s");
        }
        catch (Exception e) { Console.WriteLine($"[PRELOAD] audio failed {oggPath}: {e.Message}"); }
    }

    /// <summary>Dispose readers that were preloaded but never played (song left before it started).</summary>
    public void ClearPrepared()
    {
        foreach (var r in _preopened.Values) { try { r.Dispose(); } catch { } }
        _preopened.Clear();
    }

    private WaveStream CreateWaveReader(string filePath)
    {
        if (_preopened.TryGetValue(filePath, out var pre)) { _preopened.Remove(filePath); return pre; }
        return CreateWaveReaderRaw(filePath);
    }

    private static WaveStream CreateWaveReaderRaw(string filePath)
    {
        if (filePath.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
            return new VorbisWaveReader(filePath);
#if FNF_CROSSPLATFORM
        return new WaveFileReader(filePath);   // NAudio.Core only (AudioFileReader needs Windows codecs)
#else
        return new AudioFileReader(filePath);
#endif
    }
    
    /// <summary>
    /// Resolve an audio path trying .ogg and subfolder formats
    /// </summary>
    private string ResolveAudioPath(string contentPath, string path)
    {
        if (FNF_MonoGame.Engine.VSliceMods.Any) { string vs = FNF_MonoGame.Engine.VSliceMods.Resolve(path.EndsWith(".ogg") ? path : path + ".ogg") ?? FNF_MonoGame.Engine.VSliceMods.Resolve(path); if (vs != null && global::FNF_MonoGame.Engine.FsCI.FileExists(vs)) return vs; }
        // Try in contentPath first, then extra roots
        string[] roots = new string[1 + ExtraContentRoots.Count];
        roots[0] = contentPath;
        for (int i = 0; i < ExtraContentRoots.Count; i++)
            roots[i + 1] = ExtraContentRoots[i];
        
        foreach (var root in roots)
        {
            string norm = path.Replace('\\', '/');
            
            // Map game paths to funkin.assets structure for extra roots
            string[] searchPaths;
            if (root == contentPath)
            {
                // Content directory: just use the path directly
                searchPaths = new[] { norm };
            }
            else if (norm.StartsWith("songs/"))
            {
                // songs/{folder}/{file} stays the same in funkin.assets
                searchPaths = new[] { norm };
            }
            else if (norm.StartsWith("game/"))
            {
                // game/characters/... ? shared/images/characters/...
                string mapped = norm.StartsWith("game/characters/")
                    ? "shared/images/characters/" + norm["game/characters/".Length..]
                    : norm;
                searchPaths = new[] { norm, mapped };
            }
            else
            {
                // Map Content-relative paths to funkin.assets structure
                var paths = new List<string> { norm };
                
                if (norm.StartsWith("music/"))
                {
                    // music/freakyMenu ? preload/music/freakyMenu, shared/music/freakyMenu
                    paths.Add("preload/" + norm);
                    paths.Add("shared/" + norm);
                }
                else if (norm.StartsWith("sounds/"))
                {
                    paths.Add("preload/" + norm);
                    paths.Add("shared/" + norm);
                }
                else
                {
                    // Unknown prefix ? try various mapped locations
                    paths.Add("shared/music/" + norm);
                    paths.Add("preload/music/" + norm);
                    paths.Add("shared/sounds/" + norm);
                    paths.Add("preload/sounds/" + norm);
                }
                searchPaths = paths.ToArray();
            }
            
            foreach (var sp in searchPaths)
            {
                string tryPath = Path.Combine(root, sp + ".ogg");
                if (global::FNF_MonoGame.Engine.FsCI.FileExists(tryPath)) return tryPath;
                
                tryPath = Path.Combine(root, sp);
                if (global::FNF_MonoGame.Engine.FsCI.FileExists(tryPath)) return tryPath;
                
                string folderName = Path.GetFileName(sp);
                tryPath = Path.Combine(root, sp, folderName + ".ogg");
                if (global::FNF_MonoGame.Engine.FsCI.FileExists(tryPath)) return tryPath;
            }
        }
        
        return null;
    }
    
    public Sound PlaySound(string name, float volume = 1f)
    {
        if (string.IsNullOrEmpty(name)) return null;
        // Try to load on-demand if not cached (menu/gameplay SFX are preloaded by PreloadCommonSfx at startup)
        bool wasCached = _sounds.TryGetValue(name, out var s);
        if (!wasCached)
        {
            s = LoadSoundFromFile(name);
            if (s == null)
            {
                if (FNF_MonoGame.Engine.SfxPool.Log) Console.WriteLine($"[SFXDBG] '{name}' loaded=False result=NOT_FOUND");
                return null;
            }
        }
        float vol = _muted ? 0f : (_sfxVolume * volume);
        if (_legacySfx)
        {
            bool ok = false;
            try { ok = s.Play(vol, 0f, 0f); } catch (Exception ex) { Console.WriteLine($"[SFXDBG] legacy '{name}' threw {ex.Message}"); }
            if (FNF_MonoGame.Engine.SfxPool.Log) Console.WriteLine($"[SFXDBG] legacy '{name}' loaded={wasCached} vol={vol:F2} result={(ok ? "Playing" : "DROPPED")}");
            return new Sound();
        }
        _sfxPool.Play(s, name, vol, wasCached);
        return new Sound();
    }
    private readonly FNF_MonoGame.Engine.SfxPool _sfxPool = new FNF_MonoGame.Engine.SfxPool();
    private static readonly bool _legacySfx = Environment.GetEnvironmentVariable("FNF_SFXLEGACY") == "1";
    public FNF_MonoGame.Engine.SfxPool SfxPoolStats => _sfxPool;

    /// <summary>Decode the SFX every menu/gameplay screen uses up front, so the first press never waits on an OGG decode.</summary>
    public void PreloadCommonSfx()
    {
        foreach (var n in FNF_MonoGame.Engine.SfxPool.CommonSfx) { try { PreloadSound(n); } catch { } }
    }

    public Sound PlaySound(string name, float volume, bool looped, bool autoDestroy) => PlaySound(name, volume);
    
    /// <summary>
    /// Pre-load a sound effect so it's cached and ready for instant playback.
    /// Call during scene Load() to avoid on-demand OGG decoding hitches during gameplay.
    /// </summary>
    public void PreloadSound(string name)
    {
        if (!_sounds.ContainsKey(name))
        {
            LoadSoundFromFile(name);
        }
    }
    
    /// <summary>
    /// Check if a sound is loaded/cached (used for fallback logic).
    /// </summary>
    public bool HasCachedSound(string name) => _sounds.ContainsKey(name);
    
    public void LoadSound(string name, string path) 
    { 
        LoadSoundFromFile(path, name);
    }

    /// <summary>
    /// Create a MonoGame SoundEffectInstance for a Content-relative OGG path.
    /// The sound is cached like PlaySound/PreloadSound. Returns null if the file is missing.
    /// </summary>
    public SoundEffectInstance CreateSoundInstance(string path)
    {
        if (!_sounds.TryGetValue(path, out var sound))
        {
            sound = LoadSoundFromFile(path);
            if (sound == null) return null;
        }
        return sound.CreateInstance();
    }
    
    /// <summary>
    /// Load an OGG/WAV sound effect file into a MonoGame SoundEffect using NAudio
    /// </summary>
    private SoundEffect LoadSoundFromFile(string path, string cacheKey = null)
    {
        cacheKey ??= path;
        if (_sounds.TryGetValue(cacheKey, out var cached))
            return cached;
        if (FrameLog.Enabled) FrameLog.Event("snd " + path, 0);
        
        string contentPath = System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot);
        string fullPath = null;
        if (FNF_MonoGame.Engine.VSliceMods.Any) { fullPath = FNF_MonoGame.Engine.VSliceMods.Resolve(path.EndsWith(".ogg") ? path : path + ".ogg") ?? FNF_MonoGame.Engine.VSliceMods.Resolve("sounds/" + path + ".ogg"); }
        
        // Build list of roots to search
        string[] roots = new string[1 + ExtraContentRoots.Count];
        roots[0] = contentPath;
        for (int i = 0; i < ExtraContentRoots.Count; i++)
            roots[i + 1] = ExtraContentRoots[i];
        
        if (fullPath == null) foreach (var root in roots)
        {
            // Try exact path first (with .ogg)
            string tryPath = Path.Combine(root, path);
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(tryPath)) { fullPath = tryPath; break; }
            
            tryPath = Path.Combine(root, path + ".ogg");
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(tryPath)) { fullPath = tryPath; break; }
            
            tryPath = Path.Combine(root, "sounds", path + ".ogg");
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(tryPath)) { fullPath = tryPath; break; }
            
            tryPath = Path.Combine(root, "sounds", path);
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(tryPath)) { fullPath = tryPath; break; }
            
            tryPath = Path.Combine(root, path + ".wav");
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(tryPath)) { fullPath = tryPath; break; }
            
            // funkin.assets: shared/sounds/ and preload/sounds/
            if (root != contentPath)
            {
                tryPath = Path.Combine(root, "shared", "sounds", path + ".ogg");
                if (global::FNF_MonoGame.Engine.FsCI.FileExists(tryPath)) { fullPath = tryPath; break; }
                tryPath = Path.Combine(root, "preload", "sounds", path + ".ogg");
                if (global::FNF_MonoGame.Engine.FsCI.FileExists(tryPath)) { fullPath = tryPath; break; }
            }
        }
        
        if (fullPath == null)
            return null;
        
        try
        {
            // Use NAudio to decode audio to PCM
            // VorbisWaveReader handles .ogg, AudioFileReader handles .wav
            WaveStream reader;
            if (fullPath.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
                reader = new VorbisWaveReader(fullPath);
            else
#if FNF_CROSSPLATFORM
                reader = new WaveFileReader(fullPath);
#else
                reader = new AudioFileReader(fullPath);
#endif
            
            using (reader)
            {
                var sampleProvider = reader.ToSampleProvider();
                var pcmProvider = new SampleToWaveProvider16(sampleProvider);
                
                // Read all PCM data
                using var ms = new MemoryStream();
                byte[] buffer = new byte[4096];
                int bytesRead;
                while ((bytesRead = pcmProvider.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ms.Write(buffer, 0, bytesRead);
                }
                
                byte[] pcmData = ms.ToArray();
                var sfx = new SoundEffect(pcmData, reader.WaveFormat.SampleRate, 
                    reader.WaveFormat.Channels == 1 ? AudioChannels.Mono : AudioChannels.Stereo);
                
                _sounds[cacheKey] = sfx;
                return sfx;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading sound {path}: {ex.Message}");
            return null;
        }
    }
    
    // ---- Practice: playback rate (speed + pitch, XAudio2 frequency ratio) and in-song seek ----
    private double _rate = 1.0;
    public float PlaybackRate
    {
        get => (float)_rate;
        set
        {
            double v = Math.Clamp((double)value, 0.5, 2.0);
            if (!_musicPaused)
            {
                _playbackOffset += _playbackTimer.Elapsed.TotalSeconds * _rate; _playbackTimer.Restart();
                _voicePlaybackOffset += _voicePlaybackTimer.Elapsed.TotalSeconds * _rate; _voicePlaybackTimer.Restart();
                _opponentVoicePlaybackOffset += _opponentVoicePlaybackTimer.Elapsed.TotalSeconds * _rate; _opponentVoicePlaybackTimer.Restart();
            }
            _rate = v;
            ApplyRatePitch();
        }
    }
    private void ApplyRatePitch()
    {
        float p = (float)Math.Clamp(Math.Log(_rate, 2.0), -1.0, 1.0);
        try { if (_currentMusic != null && !_currentMusic.IsDisposed) _currentMusic.Pitch = p; } catch { }
        try { if (_voiceTrack != null && !_voiceTrack.IsDisposed) _voiceTrack.Pitch = p; } catch { }
        try { if (_opponentVoiceTrack != null && !_opponentVoiceTrack.IsDisposed) _opponentVoiceTrack.Pitch = p; } catch { }
    }

    /// <summary>Jump the song (music + both vocal tracks) to ms; queued audio is flushed.</summary>
    public void SeekSong(double ms)
    {
        if (_vorbisReader == null) return;
        bool wasPaused = _musicPaused;
        _musicPaused = true;          // stream threads idle
        Thread.Sleep(25);
        var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        try
        {
            _currentMusic?.Stop();
            if (t < _vorbisReader.TotalTime) _vorbisReader.CurrentTime = t;
            lock (_voiceLock) { _voiceTrack?.Stop(); if (_voiceReader != null && t < _voiceReader.TotalTime) _voiceReader.CurrentTime = t; }
            lock (_opponentVoiceLock) { _opponentVoiceTrack?.Stop(); if (_opponentVoiceReader != null && t < _opponentVoiceReader.TotalTime) _opponentVoiceReader.CurrentTime = t; }
        }
        catch (Exception e) { Console.WriteLine("[AUDIO] seek failed: " + e.Message); }
        _playbackOffset = t.TotalSeconds; _voicePlaybackOffset = t.TotalSeconds; _opponentVoicePlaybackOffset = t.TotalSeconds;
        if (wasPaused) { _playbackTimer.Reset(); return; }
        _musicPaused = false;
        _playbackTimer.Restart(); _voicePlaybackTimer.Restart(); _opponentVoicePlaybackTimer.Restart();
        ApplyRatePitch();
        try { _currentMusic?.Play(); _voiceTrack?.Play(); _opponentVoiceTrack?.Play(); } catch (Exception e) { Console.WriteLine("[AUDIO] seek play: " + e.Message); }
    }

    public void Dispose() 
    { 
        StopMusic();
        
        foreach (var s in _sounds.Values) 
            s?.Dispose(); 
        _sounds.Clear();
    }
}

