using System;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Media;
#if XBOX_UWP
// MonoGame WindowsUniversal VideoPlayer shows no picture (MediaEngine on a device without VideoSupport)
using VideoPlayer = FNF_MonoGame.Engine.UwpVideoPlayer;
#endif

namespace FNF_MonoGame.Engine;

/// <summary>
/// Video cutscene playback for PC (MonoGame WindowsDX: Media Foundation MediaSession + sample grabber) and UWP/Xbox
/// (MonoGame WindowsUniversal: Media Foundation MediaEngine) through MonoGame's own <see cref="VideoPlayer"/>.
/// Videos are H.264 Main + AAC-LC .mp4 files in Content/videos (transcoded from the original HEVC/VP9/.mkv files,
/// see scratch/encode_videos.sh), with a "&lt;id&gt;.mp4.meta" text file "durationMs width height fps".
/// MonoGame only creates <see cref="Video"/> objects through the content pipeline (VideoReader), so a tiny XNB is generated
/// in memory (<see cref="XnbVideoContent"/>) whose RootDirectory is the absolute folder of the .mp4 - works for the app
/// folder and for the Xbox LocalFolder\Content copy alike.
/// </summary>
public sealed class VideoCutscenePlayer : IDisposable
{
    private XnbVideoContent _content;
    private Video _video;
    private Func<(XnbVideoContent, Video)> _reload;
    private VideoPlayer _player;
    private bool _started, _paused;
    private Texture2D _lastTex;
    public readonly float DurationSeconds;
    public bool Visible = true;
    public string Id { get; }
    /// <summary>True once the video reached its end (VideoPlayer stopped by itself).</summary>
    public bool Ended { get; private set; }
    public double PositionSeconds { get { try { return _player != null && _player.State != MediaState.Stopped ? _player.PlayPosition.TotalSeconds : 0; } catch { return 0; } } }

    private VideoCutscenePlayer(string id, XnbVideoContent content, Video video, float durSec)
    {
        Id = id; _content = content; _video = video; DurationSeconds = durSec;
    }

    /// <summary>Paths.videos(id): Content/videos/&lt;id&gt;.mp4. Returns null (and logs) when the file is missing.</summary>
    /// <summary>Duration (mvhd) and size (first tkhd with a non-zero size) of an MP4 file.</summary>
    private static void Mp4Info(string path, ref int durMs, ref int w, ref int h)
    {
        try
        {
            var b = global::FNF_MonoGame.Engine.FsCI.ReadAllBytes(path);
            uint U32(int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);
            ulong U64(int o) => ((ulong)U32(o) << 32) | U32(o + 4);
            bool gotSize = false;
            for (int i = 4; i + 32 < b.Length; i++)
            {
                if (b[i] == 'm' && b[i + 1] == 'v' && b[i + 2] == 'h' && b[i + 3] == 'd' && durMs == 0)
                {
                    int v = b[i + 4];
                    ulong ts = v == 1 ? U32(i + 24) : U32(i + 16);
                    ulong du = v == 1 ? U64(i + 28) : U32(i + 20);
                    if (ts > 0) durMs = (int)(du * 1000UL / ts);
                }
                else if (!gotSize && b[i] == 't' && b[i + 1] == 'k' && b[i + 2] == 'h' && b[i + 3] == 'd')
                {
                    int v = b[i + 4];
                    int o = i + 4 + (v == 1 ? 88 : 76);
                    if (o + 8 <= b.Length)
                    {
                        int tw = (int)(U32(o) >> 16), th = (int)(U32(o + 4) >> 16);
                        if (tw > 0 && th > 0) { w = tw; h = th; gotSize = true; }
                    }
                }
                if (durMs > 0 && gotSize) break;
            }
        }
        catch { }
    }

    public static VideoCutscenePlayer Create(Game game, AssetManager assets, string id)
    {
        try
        {
            string mp4 = assets?.ResolvePath($"videos/{id}.mp4");
            // Store build: some official cutscenes are Matroska (.mkv) files downloaded as they are.
            if (mp4 == null || !global::FNF_MonoGame.Engine.FsCI.FileExists(mp4)) mp4 = assets?.ResolvePath($"videos/{id}.mkv") ?? mp4;
            if (mp4 == null || !global::FNF_MonoGame.Engine.FsCI.FileExists(mp4)) { Console.WriteLine($"[VIDEO] Video file does not exist: videos/{id}.mp4"); return null; }
            int durMs = 0, w = 1280, h = 720; float fps = 24f;
            string meta = mp4 + ".meta";
            if (!global::FNF_MonoGame.Engine.FsCI.FileExists(meta)) Mp4Info(mp4, ref durMs, ref w, ref h); // mod videos (Psych startVideo) have no .meta
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(meta))
            {
                var p = global::FNF_MonoGame.Engine.FsCI.ReadAllText(meta).Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length >= 4)
                {
                    durMs = int.Parse(p[0], CultureInfo.InvariantCulture); w = int.Parse(p[1], CultureInfo.InvariantCulture);
                    h = int.Parse(p[2], CultureInfo.InvariantCulture); fps = float.Parse(p[3], CultureInfo.InvariantCulture);
                }
            }
            Func<(XnbVideoContent, Video)> load = () =>
            {
#if DESKTOPGL || FNF_PORTABLE_VIDEO
                // DesktopGL (Linux/macOS): no content-pipeline VideoReader; Platforms/Shared/PortableVideo.cs (ffmpeg)
                XnbVideoContent c = null;
                var v = new Video(mp4, durMs, w, h, fps);
#else
                var c = new XnbVideoContent(game.Services, Path.GetDirectoryName(mp4), Path.GetFileName(mp4), durMs, w, h, fps);
                var v = c.Load<Video>(Path.GetFileNameWithoutExtension(mp4));
#endif
#if XBOX_UWP
                UwpVideoPlayer.Register(v, mp4);
#endif
                return (c, v);
            };
            var (content, video) = load();
            Console.WriteLine($"[VIDEO] loaded {id}: {w}x{h} {fps} fps {durMs} ms");
            return new VideoCutscenePlayer(id, content, video, durMs / 1000f) { _reload = load };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VIDEO] failed to load '{id}': {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    public bool Play(float volume)
    {
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                DxResetStaticCallback();
                _player = new VideoPlayer();
                _player.Volume = Math.Clamp(volume, 0f, 1f);
                if (!DxStartWithoutWait(_player, _video)) _player.Play(_video);
                _started = true; _paused = false; Ended = false; _seenPlaying = false; _startClock = 0f;
#if DESKTOPGL || FNF_PORTABLE_VIDEO
                // ffmpeg missing (Android / Linux without ffmpeg): end at once = cutscene skipped, the song flow goes on
                if (_player.State == MediaState.Stopped) { Console.WriteLine($"[VIDEO] no decoder: skipped '{Id}'"); Ended = true; }
#endif
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VIDEO] play attempt {attempt} failed '{Id}': {ex.GetType().Name}: {ex.Message}");
                try { _player?.Dispose(); } catch { }
                _player = null;
            }
        }
        return false;
    }

    // ---- MonoGame 3.8.1 WindowsDX VideoPlayer workarounds (reflection; the fields do not exist on UWP, where the
    // public API is used as is) ----
    //  * VideoPlayer.Play() throws "cannot start video" when the Media Foundation clock is not running after ~0.5 s
    //    (busy machine) and then closes the session: start it with PlatformPlay() and let Update() wait instead.
    //  * The session-event callback is static and bound to the first player's MediaSession: reset it so every new
    //    player (one per video - Stop() closes the session for good) gets its events (OnTopologyReady -> volume).
    private static readonly System.Reflection.BindingFlags NP = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
    private static readonly System.Reflection.BindingFlags NS = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;

    private static void DxResetStaticCallback()
    {
#if XBOX_UWP
        return;
#endif
        try { typeof(VideoPlayer).GetField("_callback", NS)?.SetValue(null, null); } catch { }
    }

    private static bool DxStartWithoutWait(VideoPlayer p, Video v)
    {
#if XBOX_UWP
        return false;
#endif
        try
        {
            var t = typeof(VideoPlayer);
            var fVideo = t.GetField("_currentVideo", NP);
            var fState = t.GetField("_state", NP);
            var mPlay = t.GetMethod("PlatformPlay", NP);
            if (fVideo == null || fState == null || mPlay == null) return false;
            fVideo.SetValue(p, v);
            mPlay.Invoke(p, null);
            fState.SetValue(p, MediaState.Playing);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VIDEO] PlatformPlay failed: {ex.InnerException?.Message ?? ex.Message}");
            return false;
        }
    }

    private bool _seenPlaying;
    private float _startClock;

    /// <summary>Per frame: volume follows the game volume (hxvlc autoVolumeHandle), end detection.</summary>
    public void Update(float volume)
    {
        if (_player == null || !_started || Ended) return;
        try
        {
            float v = Math.Clamp(volume, 0f, 1f);
            if (Math.Abs(_player.Volume - v) > 0.001f) _player.Volume = v;
            if (!_paused)
            {
                var st = _player.State;
                if (st == MediaState.Playing) _seenPlaying = true;
                double pos = PositionSeconds;
                _startClock += 1f / 60f;
                // not started yet (clock not running): wait; give up after 10 s of never playing
                if (!_seenPlaying) { if (_startClock > 10f) { Console.WriteLine("[VIDEO] never started"); Ended = true; } return; }
                if (st == MediaState.Stopped || (DurationSeconds > 0 && pos >= DurationSeconds - 0.001)) Ended = true;
            }
        }
        catch (Exception ex) { Console.WriteLine($"[VIDEO] update failed: {ex.Message}"); Ended = true; }
    }

    public void Pause() { if (_player == null || _paused) return; try { _player.Pause(); } catch { } _paused = true; }
    public void Resume() { if (_player == null || !_paused) return; try { _player.Resume(); } catch { } _paused = false; }

    /// <summary>VideoCutscene.restartVideo: bitmap.time = 0; resume(). On WindowsDX a stopped session closes the
    /// Video's media source for good, so the Video is reloaded and played by a new player.</summary>
    public void Restart(float volume)
    {
        try { _player?.Stop(); } catch { }
        try { _player?.Dispose(); } catch { }
        _player = null; _lastTex = null;
        try { _content?.Unload(); _content?.Dispose(); } catch { }
        try { (_content, _video) = _reload(); } catch (Exception ex) { Console.WriteLine($"[VIDEO] reload failed: {ex.Message}"); Ended = true; return; }
        if (!Play(volume)) Ended = true;
    }

    /// <summary>FlxVideoSprite drawn on camCutscene: scale = min(W/w, H/h), screenCenter().</summary>
    public void Draw(SpriteBatch sb, int screenW, int screenH)
    {
        if (!Visible || _player == null) return;
        Texture2D tex = null;
        try { if (_player.State != MediaState.Stopped) tex = _player.GetTexture(); } catch { tex = null; }
        if (tex != null) _lastTex = tex; else tex = _lastTex;
        if (tex == null || tex.IsDisposed) return;
        float scale = Math.Min(screenW / (float)tex.Width, screenH / (float)tex.Height);
        float w = tex.Width * scale, h = tex.Height * scale;
        sb.Draw(tex, new Rectangle((int)Math.Round((screenW - w) / 2f), (int)Math.Round((screenH - h) / 2f), (int)Math.Round(w), (int)Math.Round(h)), Color.White);
    }

    public void Dispose()
    {
        try { _player?.Stop(); } catch { }
        try { _player?.Dispose(); } catch { }
        _player = null; _lastTex = null;
        try { _content?.Unload(); _content?.Dispose(); } catch { }
    }
}

/// <summary>ContentManager that serves a generated XNB for one MonoGame Video (VideoReader layout:
/// string relativePath, int durationMs, int width, int height, float fps, int soundtrackType - each via ReadObject).</summary>
internal sealed class XnbVideoContent : ContentManager
{
    private readonly byte[] _xnb;

    public XnbVideoContent(IServiceProvider services, string absDir, string fileName, int durMs, int w, int h, float fps)
        : base(services, absDir)
    {
        _xnb = Build(fileName, durMs, w, h, fps);
    }

    protected override Stream OpenStream(string assetName) => new MemoryStream(_xnb, false);

    private static byte[] Build(string fileName, int durMs, int w, int h, float fps)
    {
        var body = new MemoryStream();
        using (var bw = new BinaryWriter(body, Encoding.UTF8, true))
        {
            string[] readers =
            {
                "Microsoft.Xna.Framework.Content.VideoReader",   // 1
                "Microsoft.Xna.Framework.Content.StringReader",  // 2
                "Microsoft.Xna.Framework.Content.Int32Reader",   // 3
                "Microsoft.Xna.Framework.Content.SingleReader",  // 4
            };
            Write7(bw, readers.Length);
            foreach (var r in readers) { bw.Write(r); bw.Write(0); }
            Write7(bw, 0);                // shared resources
            Write7(bw, 1);                // primary asset: VideoReader
            Write7(bw, 2); bw.Write(fileName);
            Write7(bw, 3); bw.Write(durMs);
            Write7(bw, 3); bw.Write(w);
            Write7(bw, 3); bw.Write(h);
            Write7(bw, 4); bw.Write(fps);
            Write7(bw, 3); bw.Write(0);   // VideoSoundtrackType.Music
        }
        var outS = new MemoryStream();
        using (var bw = new BinaryWriter(outS, Encoding.UTF8, true))
        {
            bw.Write((byte)'X'); bw.Write((byte)'N'); bw.Write((byte)'B');
            bw.Write((byte)'w'); bw.Write((byte)5); bw.Write((byte)0);
            bw.Write((int)(10 + body.Length));
            bw.Write(body.ToArray());
        }
        return outS.ToArray();
    }

    private static void Write7(BinaryWriter bw, int v)
    {
        uint u = (uint)v;
        while (u >= 0x80) { bw.Write((byte)(u | 0x80)); u >>= 7; }
        bw.Write((byte)u);
    }
}
