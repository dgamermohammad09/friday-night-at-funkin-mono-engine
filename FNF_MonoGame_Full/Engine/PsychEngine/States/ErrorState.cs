using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace FNF_MonoGame.Engine.PsychEngine.States;

/// <summary>
/// Psych Engine error screen.
/// Original: states/ErrorState.hx
/// Displays an error message with accept/back controls.
/// </summary>
public class ErrorState : MusicBeatState
{
    public Action AcceptCallback;
    public Action BackCallback;
    public string ErrorMsg { get; set; }

    public float ErrorSine { get; private set; }

    private Texture2D _bgTexture;
    private Texture2D _solidTex;
    private float _alpha;

    // Input
    private KeyboardState _prevKb;
    private GamePadState _prevGp;

    public ErrorState(string error, Action accept = null, Action back = null)
    {
        ErrorMsg = error;
        AcceptCallback = accept;
        BackCallback = back;
    }

    public override void Create()
    {
        base.Create();
        _solidTex = CreateSolidTexture(Color.White, 1, 1);

        string path = PsychPaths.Image("menuDesat");
        if (!string.IsNullOrEmpty(path))
        {
            string basePath = System.IO.Path.ChangeExtension(path, null);
            var sheet = SpriteSheet.Load(FNFGame.Instance, basePath);
            _bgTexture = sheet?.Texture;
        }

        _prevKb = Keyboard.GetState();
        _prevGp = GamePad.GetState(PlayerIndex.One);
    }

    public override void Update(float elapsed)
    {
        base.Update(elapsed);
        ErrorSine += 180 * elapsed;
        _alpha = 1f - (float)Math.Sin((Math.PI * ErrorSine) / 180);

        var kb = Keyboard.GetState();
        var gp = GamePad.GetState(PlayerIndex.One);

        bool accept = (kb.IsKeyDown(Keys.Enter) && !_prevKb.IsKeyDown(Keys.Enter)) ||
                      (gp.Buttons.A == ButtonState.Pressed && _prevGp.Buttons.A != ButtonState.Pressed);
        bool back = (kb.IsKeyDown(Keys.Escape) && !_prevKb.IsKeyDown(Keys.Escape)) ||
                    (gp.Buttons.B == ButtonState.Pressed && _prevGp.Buttons.B != ButtonState.Pressed);

        if (accept && AcceptCallback != null)
            AcceptCallback();
        else if (back && BackCallback != null)
            BackCallback();

        _prevKb = kb;
        _prevGp = gp;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        base.Draw(spriteBatch);
        var viewport = FNFGame.Instance.GraphicsDevice.Viewport;

        if (_bgTexture != null)
        {
            spriteBatch.Draw(_bgTexture,
                new Rectangle(0, 0, viewport.Width, viewport.Height),
                Color.Gray);
        }

        // Error overlay background
        if (_solidTex != null)
        {
            spriteBatch.Draw(_solidTex, new Rectangle(0, viewport.Height / 4, viewport.Width, viewport.Height / 2),
                Color.Black * 0.7f);
        }

        // Error text would be drawn with SpriteFont at center
        // Pulsing alpha effect from ErrorSine applied to text color
    }

    private static Texture2D CreateSolidTexture(Color color, int width, int height)
    {
        var tex = new Texture2D(FNFGame.Instance.GraphicsDevice, width, height);
        var data = new Color[width * height];
        for (int i = 0; i < data.Length; i++) data[i] = color;
        tex.SetData(data);
        return tex;
    }
}
