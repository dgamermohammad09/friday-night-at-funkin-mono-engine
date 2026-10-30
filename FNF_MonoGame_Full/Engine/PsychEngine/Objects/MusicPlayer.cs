using Microsoft.Xna.Framework.Audio;

namespace FNF_MonoGame.Engine.PsychEngine.Objects;

/// <summary>
/// Psych Engine Freeplay music player / jukebox.
/// Original: objects/MusicPlayer.hx
/// </summary>
public class MusicPlayer
{
    public bool IsPlaying { get; private set; }
    public float Volume { get; set; } = 1f;
    public float Pitch { get; set; } = 0f;
    public float CurTime { get; private set; }
    public float Length { get; private set; }

    public string CurrentSong { get; private set; }

    private SoundEffectInstance _instance;

    public void LoadSong(string song)
    {
        CurrentSong = song;
        string path = PsychPaths.Music(song);
        if (global::FNF_MonoGame.Engine.FsCI.FileExists(path))
        {
            var sound = SoundEffect.FromFile(global::FNF_MonoGame.Engine.FsCI.Fix(path));
            _instance = sound.CreateInstance();
            Length = (float)sound.Duration.TotalSeconds;
        }
    }

    public void Play()
    {
        if (_instance != null && !IsPlaying)
        {
            _instance.Play();
            IsPlaying = true;
        }
    }

    public void Pause()
    {
        _instance?.Pause();
        IsPlaying = false;
    }

    public void Stop()
    {
        _instance?.Stop();
        IsPlaying = false;
        CurTime = 0;
    }

    public void Update(float elapsed)
    {
        if (IsPlaying && _instance != null)
        {
            _instance.Volume = Volume;
            _instance.Pitch = Pitch;
            CurTime += elapsed;
            if (_instance.State == SoundState.Stopped)
                IsPlaying = false;
        }
    }

    public void Destroy()
    {
        _instance?.Dispose();
        _instance = null;
    }
}
