using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine.PsychEngine.Objects;

namespace FNF_MonoGame.Engine.PsychEngine.States;

/// <summary>
/// Psych Engine flashing lights warning screen.
/// Original: states/FlashingState.hx
/// </summary>
public class FlashingState : MusicBeatState
{
    public static bool LeftState { get; set; }

    private bool _isYes = true;
    private float _alpha;
    private Texture2D _bgTexture;
    private Texture2D _solidTex;

    // Input
    private KeyboardState _prevKb;
    private GamePadState _prevGp;

    public override void Create()
    {
        base.Create();
        LeftState = false;
        _solidTex = CreateSolidTexture(Color.White, 1, 1);
        _bgTexture = CreateSolidTexture(Color.Black, 1, 1);

        _prevKb = Keyboard.GetState();
        _prevGp = GamePad.GetState(PlayerIndex.One);
    }

    public override void Update(float elapsed)
    {
        base.Update(elapsed);
        if (LeftState) return;

        var kb = Keyboard.GetState();
        var gp = GamePad.GetState(PlayerIndex.One);

        bool uiLeft = (kb.IsKeyDown(Keys.Left) && !_prevKb.IsKeyDown(Keys.Left)) ||
                      (gp.DPad.Left == ButtonState.Pressed && _prevGp.DPad.Left != ButtonState.Pressed);
        bool uiRight = (kb.IsKeyDown(Keys.Right) && !_prevKb.IsKeyDown(Keys.Right)) ||
                       (gp.DPad.Right == ButtonState.Pressed && _prevGp.DPad.Right != ButtonState.Pressed);
        bool accept = (kb.IsKeyDown(Keys.Enter) && !_prevKb.IsKeyDown(Keys.Enter)) ||
                      (gp.Buttons.A == ButtonState.Pressed && _prevGp.Buttons.A != ButtonState.Pressed);
        bool back = (kb.IsKeyDown(Keys.Escape) && !_prevKb.IsKeyDown(Keys.Escape)) ||
                    (gp.Buttons.B == ButtonState.Pressed && _prevGp.Buttons.B != ButtonState.Pressed);

        if (uiLeft || uiRight)
        {
            _isYes = !_isYes;
            UpdateItems();
        }

        if (accept || back)
        {
            LeftState = true;
            if (!back)
            {
                ClientPrefs.Data.Flashing = !_isYes;
                ClientPrefs.Save();
            }
            // TODO: fade out and switch to TitleState via scene manager
        }

        _prevKb = kb;
        _prevGp = gp;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        base.Draw(spriteBatch);
        var viewport = FNFGame.Instance.GraphicsDevice.Viewport;
        if (_bgTexture != null)
            spriteBatch.Draw(_bgTexture, new Rectangle(0, 0, viewport.Width, viewport.Height), Color.White * _alpha);
        
        // Draw the warning text
        // "Hey, watch out!\n\nThis game contains some flashing lights!"
        // "You've been warned!"
        
        // Draw YES/NO buttons
        // Highlights would be updated by UpdateItems() based on _isYes
    }

    private void UpdateItems()
    {
        // yes/no alpha/scale handled during draw
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
