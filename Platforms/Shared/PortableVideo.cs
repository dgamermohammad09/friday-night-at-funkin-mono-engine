#if DESKTOPGL || FNF_PORTABLE_VIDEO
// Cross-platform replacement for MonoGame's Video / VideoPlayer, which MonoGame DesktopGL (Linux / macOS) does not have.
// Decoding is done by an external `ffmpeg` executable (system package on Linux, Homebrew on macOS, or an ffmpeg
// binary placed next to the game / in $FNF_FFMPEG): one process pipes raw RGBA frames, a second one pipes 16-bit
// stereo PCM into a DynamicSoundEffectInstance. Without ffmpeg the player reports Stopped right away, so cutscenes
// are skipped gracefully (the song / story flow continues exactly like after a finished video).
// Only the API surface used by Engine/VideoCutscenePlayer.cs and Engine/PsychEngine/FlxVideo.cs is implemented.
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;

namespace Microsoft.Xna.Framework.Media
{
    public enum VideoSoundtrackType { Music, Dialog, MusicAndDialog }

    public sealed class Video : IDisposable
    {
        public string FileName { get; }
        public TimeSpan Duration { get; }
        public int Width { get; }
        public int Height { get; }
        public float FramesPerSecond { get; }
        public VideoSoundtrackType VideoSoundtrackType { get; }
        public bool IsDisposed { get; private set; }

        public Video(string fileName, int durationMs, int width, int height, float fps)
        {
            FileName = fileName; Duration = TimeSpan.FromMilliseconds(durationMs);
            Width = width > 0 ? width : 1280; Height = height > 0 ? height : 720; FramesPerSecond = fps > 0 ? fps : 24f;
        }

        // Signature probed by FlxVideo.CreateVideo through reflection (MonoGame's internal Video ctor).
        internal Video(string fileName, GraphicsDevice gd, int durationMs, int width, int height, float fps, VideoSoundtrackType type)
            : this(fileName, durationMs, width, height, fps) { VideoSoundtrackType = type; }

        public void Dispose() => IsDisposed = true;
    }

    public sealed partial class VideoPlayer : IDisposable
    {
        private Video _video;
        private MediaState _state = MediaState.Stopped;
        private Process _vproc, _aproc;
        private Thread _vthread, _athread;
        private volatile bool _quit;
        private readonly Stopwatch _clock = new Stopwatch();
        private byte[] _front, _back;
        private long _frontIndex = -1, _uploadedIndex = -1;
        private readonly object _lock = new object();
        private volatile bool _videoEnded;
        private int _w, _h;
        private Texture2D _tex;
        private DynamicSoundEffectInstance _audio;
        private float _volume = 1f;
        private readonly GraphicsDevice _gd;

        public VideoPlayer() { _gd = FNF_MonoGame.FNFGame.Instance?.GraphicsDevice; }
        public VideoPlayer(GraphicsDevice gd) { _gd = gd; }

        public bool IsLooped { get; set; }
        public bool IsMuted { get; set; }
        public Video Video => _video;
        public MediaState State
        {
            get
            {
                if (_state == MediaState.Playing && _videoEnded && _clock.Elapsed >= (_video?.Duration ?? TimeSpan.Zero))
                {
                    if (IsLooped && _video != null) { var v = _video; Stop(); Play(v); }
                    else Stop();
                }
                return _state;
            }
        }
        public TimeSpan PlayPosition => _clock.Elapsed;
        public float Volume
        {
            get => _volume;
            set { _volume = Math.Clamp(value, 0f, 1f); try { if (_audio != null) _audio.Volume = IsMuted ? 0f : _volume; } catch { } }
        }

        /// <summary>Path of the ffmpeg executable: $FNF_FFMPEG, next to the game, then PATH (+ Homebrew dirs).</summary>
        public static string FindFfmpeg()
        {
            string exe = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
            var env = Environment.GetEnvironmentVariable("FNF_FFMPEG");
            if (env == "none") return null;   // test the no-ffmpeg fallback
            if (!string.IsNullOrEmpty(env) && global::FNF_MonoGame.Engine.FsCI.FileExists(env)) return env;
            var local = Path.Combine(AppContext.BaseDirectory, exe);
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(local)) return local;
            var local2 = Path.Combine(AppContext.BaseDirectory, "ffmpeg", exe);
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(local2)) return local2;
            var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            foreach (var d in dirs) { try { var p = Path.Combine(d.Trim('"'), exe); if (global::FNF_MonoGame.Engine.FsCI.FileExists(p)) return p; } catch { } }
            foreach (var d in new[] { "/opt/homebrew/bin", "/usr/local/bin", "/usr/bin" }) { var p = Path.Combine(d, exe); if (global::FNF_MonoGame.Engine.FsCI.FileExists(p)) return p; }
            return null;
        }

        public void Play(Video video)
        {
            Stop();
            _video = video ?? throw new ArgumentNullException(nameof(video));
            _w = video.Width; _h = video.Height;
#if ANDROID
            // Android: MediaCodec decoder (Platforms/Android/AndroidVideo.cs); on failure the cutscene is skipped
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(video.FileName) && AndroidStart(video)) return;
            Console.WriteLine($"[VIDEO] no Android decoder: skipping '{Path.GetFileName(video.FileName)}'");
            _state = MediaState.Stopped;
            return;
#else
            var ff = FindFfmpeg();
            if (ff == null || !global::FNF_MonoGame.Engine.FsCI.FileExists(video.FileName))
            {
                Console.WriteLine($"[VIDEO] ffmpeg not found ({(ff == null ? "install ffmpeg or set FNF_FFMPEG" : "missing file")}): skipping '{Path.GetFileName(video.FileName)}'");
                _state = MediaState.Stopped;
                return;
            }
            _quit = false; _videoEnded = false; _frontIndex = -1; _uploadedIndex = -1;
            _front = new byte[_w * _h * 4]; _back = new byte[_w * _h * 4];
            try
            {
                _vproc = Start(ff, $"-nostdin -v error -i \"{video.FileName}\" -an -vf scale={_w}:{_h} -pix_fmt rgba -f rawvideo -");
                _aproc = Start(ff, $"-nostdin -v error -i \"{video.FileName}\" -vn -ac 2 -ar 44100 -f s16le -");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VIDEO] ffmpeg start failed: {ex.Message}");
                KillProcs(); _state = MediaState.Stopped; return;
            }
            try
            {
                _audio = new DynamicSoundEffectInstance(44100, AudioChannels.Stereo);
                _audio.Volume = IsMuted ? 0f : _volume;
            }
            catch (Exception ex) { Console.WriteLine($"[VIDEO] audio init failed: {ex.Message}"); _audio = null; }
            _state = MediaState.Playing;
            _clock.Restart();
            _audio?.Play();
            _vthread = new Thread(VideoLoop) { IsBackground = true, Name = "ffmpeg-video" }; _vthread.Start();
            _athread = new Thread(AudioLoop) { IsBackground = true, Name = "ffmpeg-audio" }; _athread.Start();
            Console.WriteLine($"[VIDEO] ffmpeg playback started: {Path.GetFileName(video.FileName)} {_w}x{_h} @{video.FramesPerSecond}");
#endif
        }

        private static Process Start(string ff, string args)
        {
            var psi = new ProcessStartInfo(ff, args)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = false,
                RedirectStandardInput = false, CreateNoWindow = true,
            };
            return Process.Start(psi);
        }

        private void VideoLoop()
        {
            var s = _vproc.StandardOutput.BaseStream;
            int size = _w * _h * 4; long idx = 0; double fps = _video.FramesPerSecond;
            try
            {
                while (!_quit)
                {
                    int got = 0;
                    while (got < size) { int n = s.Read(_back, got, size - got); if (n <= 0) break; got += n; }
                    if (got < size) break;
                    double due = idx / fps;
                    while (!_quit && _clock.Elapsed.TotalSeconds < due) Thread.Sleep(2);
                    lock (_lock) { (_front, _back) = (_back, _front); _frontIndex = idx; }
                    idx++;
                }
            }
            catch (Exception ex) { if (!_quit) Console.WriteLine($"[VIDEO] decode error: {ex.Message}"); }
            _videoEnded = true;
        }

        private void AudioLoop()
        {
            var s = _aproc.StandardOutput.BaseStream;
            var buf = new byte[44100 * 4 / 10];   // 100 ms chunks
            try
            {
                while (!_quit && _audio != null)
                {
                    if (_audio.PendingBufferCount >= 3 || _state == MediaState.Paused) { Thread.Sleep(10); continue; }
                    int got = 0;
                    while (got < buf.Length) { int n = s.Read(buf, got, buf.Length - got); if (n <= 0) break; got += n; }
                    got &= ~3;
                    if (got <= 0) break;
                    _audio.SubmitBuffer(buf, 0, got);
                }
            }
            catch (Exception ex) { if (!_quit) Console.WriteLine($"[VIDEO] audio error: {ex.Message}"); }
        }

        public Texture2D GetTexture()
        {
            var gd = _gd ?? FNF_MonoGame.FNFGame.Instance?.GraphicsDevice;
            if (gd == null || _frontIndex < 0) return _tex;
            if (_tex == null || _tex.IsDisposed) { _tex = new Texture2D(gd, _w, _h, false, SurfaceFormat.Color); _uploadedIndex = -1; }
            lock (_lock)
            {
                if (_frontIndex != _uploadedIndex) { _tex.SetData(_front); _uploadedIndex = _frontIndex; }
            }
            return _tex;
        }

        public void Pause()
        {
            if (_state != MediaState.Playing) return;
            _clock.Stop(); try { _audio?.Pause(); } catch { }
            _state = MediaState.Paused;
        }

        public void Resume()
        {
            if (_state != MediaState.Paused) return;
            _clock.Start(); try { _audio?.Resume(); } catch { }
            _state = MediaState.Playing;
        }

        public void Stop()
        {
            _quit = true;
            KillProcs();
            try { _vthread?.Join(500); } catch { }
            try { _athread?.Join(500); } catch { }
#if ANDROID
            AndroidStop();
#endif
            _vthread = _athread = null;
            try { _audio?.Stop(); _audio?.Dispose(); } catch { }
            _audio = null;
            _clock.Stop();
            _state = MediaState.Stopped;
        }

        private void KillProcs()
        {
            foreach (var p in new[] { _vproc, _aproc })
            {
                try { if (p != null && !p.HasExited) p.Kill(); } catch { }
                try { p?.Dispose(); } catch { }
            }
            _vproc = _aproc = null;
        }

        public void Dispose()
        {
            Stop();
            try { _tex?.Dispose(); } catch { }
            _tex = null;
        }
    }
}
#endif
