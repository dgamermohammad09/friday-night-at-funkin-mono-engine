#if XBOX_UWP
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Media;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.MediaFoundation;
using SharpDX.Mathematics.Interop;
using XnaTexture = Microsoft.Xna.Framework.Graphics.Texture2D;

namespace FNF_MonoGame.Engine;

/// <summary>
/// UWP/Xbox replacement for MonoGame 3.8.1 WindowsUniversal's <see cref="VideoPlayer"/>, which plays the audio but
/// never shows a picture: it runs the Media Foundation MediaEngine on MonoGame's own D3D11 device, which is created
/// WITHOUT D3D11_CREATE_DEVICE_VIDEO_SUPPORT, so DXVA decoding into that device fails (black video).
/// This player gives the MediaEngine its own hardware D3D11 device with VideoSupport (frame-server mode, BGRA output),
/// copies every new frame through a staging texture to the CPU and uploads it into a MonoGame Texture2D.
/// Same surface as the members of VideoPlayer that <see cref="VideoCutscenePlayer"/> uses.
/// </summary>
public sealed class UwpVideoPlayer : IDisposable
{
    private static readonly Dictionary<Video, string> Paths = new();
    private static bool _mfStarted;

    /// <summary>Remember the absolute .mp4 path of a Video (MonoGame keeps it internal).</summary>
    public static void Register(Video video, string path) { lock (Paths) Paths[video] = path; }

    private Device _dev;
    private DXGIDeviceManager _mgr;
    private MediaEngine _engine;
    private SharpDX.Direct3D11.Texture2D _target, _staging;
    private XnaTexture _tex;
    private byte[] _buf;
    private int _w, _h;
    private volatile MediaState _state = MediaState.Stopped;
    private float _volume = 1f;
    private int _fails;
    private long _lastPts = long.MinValue;

    public MediaState State => _state;

    public TimeSpan PlayPosition
    {
        get { try { return _engine != null ? TimeSpan.FromSeconds(_engine.CurrentTime) : TimeSpan.Zero; } catch { return TimeSpan.Zero; } }
    }

    public float Volume
    {
        get => _volume;
        set { _volume = value; try { if (_engine != null) _engine.Volume = value; } catch { } }
    }

    public void Play(Video video)
    {
        string path;
        lock (Paths) if (!Paths.TryGetValue(video, out path)) throw new InvalidOperationException("video path not registered");
        if (!_mfStarted) { MediaManager.Startup(); _mfStarted = true; }

        _dev = new Device(DriverType.Hardware, DeviceCreationFlags.VideoSupport | DeviceCreationFlags.BgraSupport);
        using (var mt = _dev.QueryInterface<DeviceMultithread>()) mt.SetMultithreadProtected(true);
        _mgr = new DXGIDeviceManager();
        _mgr.ResetDevice(_dev);
        using (var factory = new MediaEngineClassFactory())
        using (var attr = new MediaEngineAttributes(0) { VideoOutputFormat = (int)SharpDX.DXGI.Format.B8G8R8A8_UNorm, DxgiManager = _mgr })
            _engine = new MediaEngine(factory, attr, MediaEngineCreateFlags.None, OnEngineEvent);
        _engine.Volume = _volume;
        _engine.Source = path;
        _engine.Play();
        _state = MediaState.Playing;
    }

    private void OnEngineEvent(MediaEngineEvent ev, long param1, int param2)
    {
        if (ev == MediaEngineEvent.Ended) _state = MediaState.Stopped;
        else if (ev == MediaEngineEvent.Error)
        {
            Console.WriteLine($"[VIDEO] MediaEngine error {param1} hr=0x{param2:X8}");
            _state = MediaState.Stopped;
        }
    }

    /// <summary>Latest decoded frame (the previous one when no new frame is ready), or null before the first frame.</summary>
    public XnaTexture GetTexture()
    {
        if (_engine == null) return _tex;
        string step = "tick";
        try
        {
            if (!_engine.HasVideo()) return _tex;
            // OnVideoStreamTick: S_OK = new frame, S_FALSE = none. SharpDX's RawBool mapping of that HRESULT is not
            // reliable (MonoGame's own player relies on it and never shows a frame), so decide by the time stamp.
            _engine.OnVideoStreamTick(out long pts);
            if (pts == _lastPts) return _tex;
            step = "size"; _engine.GetNativeVideoSize(out int w, out int h);
            if (w <= 0 || h <= 0) return _tex;
            if (_target == null || w != _w || h != _h)
            {
                _target?.Dispose(); _staging?.Dispose();
                _w = w; _h = h;
                var desc = new Texture2DDescription
                {
                    Width = w, Height = h, MipLevels = 1, ArraySize = 1, Format = SharpDX.DXGI.Format.B8G8R8A8_UNorm,
                    SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0), Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource, CpuAccessFlags = CpuAccessFlags.None,
                };
                _target = new SharpDX.Direct3D11.Texture2D(_dev, desc);
                desc.Usage = ResourceUsage.Staging; desc.BindFlags = BindFlags.None; desc.CpuAccessFlags = CpuAccessFlags.Read;
                _staging = new SharpDX.Direct3D11.Texture2D(_dev, desc);
                _buf = new byte[w * h * 4];
            }
            step = "transfer"; _engine.TransferVideoFrame(_target, null, new RawRectangle(0, 0, w, h), null);
            var ctx = _dev.ImmediateContext;
            step = "copy"; ctx.CopyResource(_target, _staging);
            step = "map"; var box = ctx.MapSubresource(_staging, 0, MapMode.Read, MapFlags.None);
            try
            {
                int row = w * 4;
                for (int y = 0; y < h; y++)
                    Marshal.Copy(box.DataPointer + y * box.RowPitch, _buf, y * row, row);
            }
            finally { ctx.UnmapSubresource(_staging, 0); }
            _lastPts = pts;
            for (int i = 3; i < _buf.Length; i += 4) _buf[i] = 255; // video frames are opaque
            step = "upload"; var gd = XboxGame.Instance.GraphicsDevice;
            if (_tex == null || _tex.Width != w || _tex.Height != h)
            {
                _tex?.Dispose();
                _tex = new XnaTexture(gd, w, h, false, Microsoft.Xna.Framework.Graphics.SurfaceFormat.Bgra32);
            }
            _tex.SetData(_buf);
        }
        catch (Exception ex) { if (_fails++ < 5) Console.WriteLine($"[VIDEO] frame copy failed at {step} ({_w}x{_h}): {ex.GetType().Name}: {ex.Message}"); }
        return _tex;
    }

    public void Pause() { try { _engine?.Pause(); } catch { } if (_state == MediaState.Playing) _state = MediaState.Paused; }
    public void Resume() { try { _engine?.Play(); } catch { } if (_state == MediaState.Paused) _state = MediaState.Playing; }
    public void Stop() { try { _engine?.Pause(); } catch { } _state = MediaState.Stopped; }

    public void Dispose()
    {
        _state = MediaState.Stopped;
        try { _engine?.Shutdown(); } catch { }
        try { _engine?.Dispose(); } catch { }
        _engine = null;
        try { _target?.Dispose(); _staging?.Dispose(); } catch { }
        _target = _staging = null;
        try { _mgr?.Dispose(); } catch { }
        try { _dev?.Dispose(); } catch { }
        _mgr = null; _dev = null;
        try { _tex?.Dispose(); } catch { }
        _tex = null;
    }
}
#endif
