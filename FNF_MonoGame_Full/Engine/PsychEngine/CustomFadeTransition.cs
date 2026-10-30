using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine-style fade transition.
/// Adapts the original FlxGradient transition to MonoGame by rendering a black bar
/// and a gradient texture that sweeps across the screen.
/// Original: backend/CustomFadeTransition.hx
/// </summary>
public class CustomFadeTransition : MusicBeatSubstate
{
    public static System.Action FinishCallback;

    private readonly bool _isTransIn;
    private readonly float _duration;

    private Texture2D _gradientTexture;
    private Texture2D _blackTexture;
    private float _gradientY;
    private float _targetPos;
    private float _height;

    public CustomFadeTransition(float duration, bool isTransIn)
    {
        _duration = duration;
        _isTransIn = isTransIn;
    }

    public override void Create()
    {
        base.Create();

        var graphics = FNFGame.Instance.GraphicsDevice;
        int width = graphics.Viewport.Width;
        int height = graphics.Viewport.Height;
        _height = height;
        _targetPos = height + 50;

        _blackTexture = new Texture2D(graphics, 1, 1);
        _blackTexture.SetData(new[] { Color.Black });

        _gradientTexture = CreateGradientTexture(graphics, 1, height, _isTransIn);

        if (_isTransIn)
            _gradientY = 0;
        else
            _gradientY = -height;
    }

    public override void Update(float elapsed)
    {
        base.Update(elapsed);

        if (_duration > 0)
            _gradientY += (_height + _targetPos) * elapsed / _duration;
        else
            _gradientY += _targetPos * elapsed;

        if (_gradientY >= _targetPos)
            Close();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        base.Draw(spriteBatch);
        if (_blackTexture == null || _gradientTexture == null) return;

        int width = FNFGame.Instance.GraphicsDevice.Viewport.Width;
        int height = FNFGame.Instance.GraphicsDevice.Viewport.Height;

        float blackY = _isTransIn ? _gradientY + _gradientTexture.Height : _gradientY - height - 400;
        spriteBatch.Draw(_blackTexture, new Rectangle(0, (int)blackY, width, height + 400), Color.White);
        spriteBatch.Draw(_gradientTexture, new Rectangle(0, (int)_gradientY, width, _gradientTexture.Height), Color.White);
    }

    public override void Close()
    {
        base.Close();
        FinishCallback?.Invoke();
        FinishCallback = null;

        _blackTexture?.Dispose();
        _gradientTexture?.Dispose();
        _blackTexture = null;
        _gradientTexture = null;
    }

    private static Texture2D CreateGradientTexture(GraphicsDevice graphics, int width, int height, bool transIn)
    {
        var texture = new Texture2D(graphics, width, height);
        var data = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            float t = y / (float)height;
            byte a = transIn ? (byte)(255 * t) : (byte)(255 * (1 - t));
            for (int x = 0; x < width; x++)
                data[y * width + x] = new Color((byte)0, (byte)0, (byte)0, a);
        }
        texture.SetData(data);
        return texture;
    }
}
