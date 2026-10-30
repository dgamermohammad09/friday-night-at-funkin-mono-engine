using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Media;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine FlxVideo for HScript mods. Wraps MonoGame's VideoPlayer to play
/// .mp4 cutscenes from mods (e.g. Silly Billy video sprites).
/// Xbox-compatible: uses only the public Media APIs plus reflection for the
/// internal Video constructor, which is how MonoGame's content pipeline creates
/// Video instances.
/// </summary>
public class FlxVideo : Dictionary<string, object>
{
    private static readonly List<FlxVideo> ActiveVideos = new();

    private readonly VideoPlayer _player;
    private Video _video;
    private bool _disposed;
    private bool _playing;
    private bool _textureReady;
    private readonly List<object> _textureSetupCbs = new();
    private readonly List<object> _endReachedCbs = new();

    public Action<object, object[]> InvokeCallback { get; set; }

    public Texture2D BitmapData
    {
        get => this["bitmapData"] as Texture2D;
        private set => this["bitmapData"] = value;
    }

    public FlxVideo() : base(StringComparer.OrdinalIgnoreCase)
    {
        _player = CreateVideoPlayer();
        SetupDict();
        ActiveVideos.Add(this);
    }

    private static VideoPlayer CreateVideoPlayer()
    {
        var gd = FNFGame.Instance?.GraphicsDevice;
        var ctor = typeof(VideoPlayer).GetConstructor(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, new[] { typeof(GraphicsDevice) }, null);
        if (ctor != null && gd != null)
            return (VideoPlayer)ctor.Invoke(new object[] { gd });
        return new VideoPlayer();
    }

    private void SetupDict()
    {
        this["x"] = 0.0;
        this["y"] = 0.0;
        this["width"] = 0.0;
        this["height"] = 0.0;
        this["alpha"] = 1.0;
        this["angle"] = 0.0;
        this["visible"] = true;
        this["camera"] = "game";
        this["bitmapData"] = null;

        this["play"] = (Func<object[], object>)PlayVideo;
        this["pause"] = (Func<object[], object>)(_ => { Pause(); return null; });
        this["resume"] = (Func<object[], object>)(_ => { Resume(); return null; });
        this["stop"] = (Func<object[], object>)(_ => { Stop(); return null; });
        this["dispose"] = (Func<object[], object>)(_ => { Dispose(); return null; });

        this["onTextureSetup"] = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["add"] = (Func<object[], object>)(a => { if (a.Length > 0) _textureSetupCbs.Add(a[0]); return null; }),
            ["remove"] = (Func<object[], object>)(a => { if (a.Length > 0) _textureSetupCbs.Remove(a[0]); return null; }),
            ["has"] = (Func<object[], object>)(a => a.Length > 0 && _textureSetupCbs.Contains(a[0]))
        };

        this["onEndReached"] = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["add"] = (Func<object[], object>)(a => { if (a.Length > 0) _endReachedCbs.Add(a[0]); return null; }),
            ["remove"] = (Func<object[], object>)(a => { if (a.Length > 0) _endReachedCbs.Remove(a[0]); return null; }),
            ["has"] = (Func<object[], object>)(a => a.Length > 0 && _endReachedCbs.Contains(a[0]))
        };
    }

    private object PlayVideo(object[] args)
    {
        string path = args.Length > 0 ? args[0]?.ToString() : null;
        bool loop = args.Length > 1 && args[1] is bool b && b;

        if (_disposed || string.IsNullOrEmpty(path) || !global::FNF_MonoGame.Engine.FsCI.FileExists(path))
            return null;

        try
        {
            _video = CreateVideo(path);
            if (_video == null) return null;

            _player.IsLooped = loop;
            _player.Play(_video);
            _playing = true;
            _textureReady = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FlxVideo] Failed to play {path}: {ex.Message}");
        }
        return null;
    }

    private static Video CreateVideo(string path)
    {
        var gd = FNFGame.Instance?.GraphicsDevice;
        if (gd == null) return null;

        var type = typeof(Video);
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        // Modern MonoGame signature: (string path, GraphicsDevice gd, int duration, int width, int height, float fps, VideoSoundtrackType type)
        var ctor = type.GetConstructor(flags, null, new[] { typeof(string), typeof(GraphicsDevice), typeof(int), typeof(int), typeof(int), typeof(float), typeof(VideoSoundtrackType) }, null);
        if (ctor != null)
            return (Video)ctor.Invoke(new object[] { path, gd, 0, 1, 1, 30f, VideoSoundtrackType.Music });

        // Alternative: (string path, GraphicsDevice gd, int duration, int width, int height, float fps, VideoSoundtrackType type)
        ctor = type.GetConstructor(flags, null, new[] { typeof(string), typeof(GraphicsDevice), typeof(int), typeof(int), typeof(float), typeof(VideoSoundtrackType) }, null);
        if (ctor != null)
            return (Video)ctor.Invoke(new object[] { path, gd, 0, 1, 1, 30f, VideoSoundtrackType.Music });

        // XNA-style internal constructor: (string fileName, int duration)
        ctor = type.GetConstructor(flags, null, new[] { typeof(string), typeof(int) }, null);
        if (ctor != null)
            return (Video)ctor.Invoke(new object[] { path, 0 });

        return null;
    }

    public void Pause()
    {
        try { _player?.Pause(); } catch { }
    }

    public void Resume()
    {
        try { _player?.Resume(); } catch { }
    }

    public void Stop()
    {
        try { _player?.Stop(); } catch { }
        _playing = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _player?.Stop(); _player?.Dispose(); _video?.Dispose(); } catch { }
        ActiveVideos.Remove(this);
    }

    public void Update()
    {
        if (_disposed || _player == null) return;

        var state = _player.State;
        if (state == MediaState.Playing)
        {
            BitmapData = _player.GetTexture();
            if (BitmapData != null && !_textureReady)
            {
                _textureReady = true;
                this["width"] = BitmapData.Width;
                this["height"] = BitmapData.Height;
                Dispatch(_textureSetupCbs);
            }
        }
        else if (state == MediaState.Stopped && _playing)
        {
            _playing = false;
            Dispatch(_endReachedCbs);
        }
    }

    private void Dispatch(List<object> callbacks)
    {
        var copy = callbacks.ToArray();
        foreach (var cb in copy)
        {
            try { InvokeCallback?.Invoke(cb, null); }
            catch (Exception ex) { Console.WriteLine($"[FlxVideo] Signal callback error: {ex.Message}"); }
        }
    }

    public static void UpdateAll()
    {
        for (int i = ActiveVideos.Count - 1; i >= 0; i--)
            ActiveVideos[i].Update();
    }

    public static void DisposeAll()
    {
        for (int i = ActiveVideos.Count - 1; i >= 0; i--)
            ActiveVideos[i].Dispose();
        ActiveVideos.Clear();
    }

    public static bool IsActive(FlxVideo video) => video != null && ActiveVideos.Contains(video);
    public static void AddActive(FlxVideo video) { if (video != null && !ActiveVideos.Contains(video)) ActiveVideos.Add(video); }
    public static void RemoveActive(FlxVideo video) => ActiveVideos.Remove(video);
}
