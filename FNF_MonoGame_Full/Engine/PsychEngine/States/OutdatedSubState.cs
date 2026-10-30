using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace FNF_MonoGame.Engine.PsychEngine.States;

/// <summary>
/// Psych Engine outdated-version warning substate.
/// Original: substates/OutdatedSubState.hx
/// </summary>
public class OutdatedSubState : MusicBeatSubstate
{
    public static string UpdateVersion { get; set; } = "";

    private bool _leftState;
    private Texture2D _bgTexture;
    private float _bgAlpha;
    private float _textAlpha;

    // Input
    private KeyboardState _prevKb;
    private GamePadState _prevGp;

    public override void Create()
    {
        base.Create();
        _bgTexture = CreateSolidTexture(Color.Black, 1, 1);

        _prevKb = Keyboard.GetState();
        _prevGp = GamePad.GetState(PlayerIndex.One);
    }

    public override void Update(float elapsed)
    {
        base.Update(elapsed);
        if (_leftState) return;

        var kb = Keyboard.GetState();
        var gp = GamePad.GetState(PlayerIndex.One);

        bool accept = (kb.IsKeyDown(Keys.Enter) && !_prevKb.IsKeyDown(Keys.Enter)) ||
                      (gp.Buttons.A == ButtonState.Pressed && _prevGp.Buttons.A != ButtonState.Pressed);
        bool back = (kb.IsKeyDown(Keys.Escape) && !_prevKb.IsKeyDown(Keys.Escape)) ||
                    (gp.Buttons.B == ButtonState.Pressed && _prevGp.Buttons.B != ButtonState.Pressed);

        if (accept)
        {
            _leftState = true;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/ShadowMario/FNF-PsychEngine/releases") { UseShellExecute = true }); } catch { }
        }
        else if (back)
        {
            _leftState = true;
        }

        if (_leftState)
        {
            // TODO: play cancelMenu sound and close via tween
            Close(); // Temporary immediate close
        }

        _prevKb = kb;
        _prevGp = gp;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        base.Draw(spriteBatch);
        var viewport = FNFGame.Instance.GraphicsDevice.Viewport;
        if (_bgTexture != null)
            spriteBatch.Draw(_bgTexture, new Rectangle(0, 0, viewport.Width, viewport.Height), Color.White * _bgAlpha);
        
        // TODO: render outdated warning text
        // "Hey bro, looks like you're running an\noutdated version of Psych Engine Engine (" + MainMenuState.psychEngineVersion + "),\nplease update to " + UpdateVersion + "!\n\nPress ESCAPE to proceed anyway.\n\nThank you for using the Engine!"
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
