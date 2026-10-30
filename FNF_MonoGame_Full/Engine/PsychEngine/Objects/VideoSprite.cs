using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Engine.PsychEngine.Objects;

/// <summary>
/// Psych Engine video sprite stub.
/// Full video playback requires a MonoGame video implementation (Video / VideoPlayer).
/// Original: objects/VideoSprite.hx
/// </summary>
public class VideoSprite
{
    public float X { get; set; }
    public float Y { get; set; }
    public bool Visible { get; set; } = true;
    public bool IsPlaying { get; private set; }
    public bool IsFinished { get; private set; }
    public bool Antialiasing { get; set; } = true;
    public bool Paused { get; set; }
    public bool AutoPause { get; set; } = true;

    public string VideoPath { get; private set; }
    public Texture2D Texture { get; private set; }

    public VideoSprite(string path)
    {
        VideoPath = path;
    }

    public void Play()
    {
        IsPlaying = true;
    }

    public void Pause()
    {
        Paused = true;
        IsPlaying = false;
    }

    public void Resume()
    {
        Paused = false;
        IsPlaying = true;
    }

    public void Stop()
    {
        IsPlaying = false;
        IsFinished = true;
    }

    public void Update(float elapsed)
    {
        // TODO: advance video frame
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible || Texture == null) return;
        spriteBatch.Draw(Texture, new Vector2(X, Y), Color.White);
    }

    public void Destroy()
    {
        Texture?.Dispose();
        Texture = null;
    }
}
