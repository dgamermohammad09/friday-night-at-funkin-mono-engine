using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine.PsychEngine.Objects;

namespace FNF_MonoGame.Engine.PsychEngine.Options;

/// <summary>
/// Psych Engine options state — main category list.
/// Original: options/OptionsState.hx
/// </summary>
public class OptionsState : MusicBeatState
{
    public static int CurSelected { get; set; }
    public static bool OnPlayState { get; set; }

    private readonly string[] _options = new[]
    {
        "Note Colors",
        "Controls",
        "Adjust Delay and Combo",
        "Graphics",
        "Visuals",
        "Gameplay"
    };

    private readonly List<Alphabet> _grpOptions = new();
    private Alphabet _selectorLeft;
    private Alphabet _selectorRight;

    private Texture2D _bgTexture;
    private MusicBeatSubstate _activeSubstate;

    // Input state
    private KeyboardState _prevKb;
    private GamePadState _prevGp;

    public override void Create()
    {
        base.Create();

#if !XBOX_UWP
        DiscordClient.ChangePresence("Options Menu");
#endif

        string path = PsychPaths.Image("menuDesat");
        if (!string.IsNullOrEmpty(path))
        {
            string basePath = System.IO.Path.ChangeExtension(path, null);
            var sheet = SpriteSheet.Load(FNFGame.Instance, basePath);
            _bgTexture = sheet?.Texture;
        }

        for (int i = 0; i < _options.Length; i++)
        {
            var optionText = new Alphabet(0, 0, Language.GetPhrase($"options_{_options[i]}", _options[i]), true);
            optionText.IsMenuItem = true;
            optionText.TargetY = i;
            _grpOptions.Add(optionText);
        }

        _selectorLeft = new Alphabet(0, 0, ">", true);
        _selectorRight = new Alphabet(0, 0, "<", true);

        ChangeSelection(0);
        ClientPrefs.Save();

        _prevKb = Keyboard.GetState();
        _prevGp = GamePad.GetState(PlayerIndex.One);
    }

    public override void Update(float elapsed)
    {
        base.Update(elapsed);

        // If a substate is open, update it instead
        if (_activeSubstate != null)
        {
            _activeSubstate.Update(elapsed);
            return;
        }

        var kb = Keyboard.GetState();
        var gp = GamePad.GetState(PlayerIndex.One);

        bool upPressed = (kb.IsKeyDown(Keys.Up) && !_prevKb.IsKeyDown(Keys.Up)) ||
                         (gp.DPad.Up == ButtonState.Pressed && _prevGp.DPad.Up != ButtonState.Pressed);
        bool downPressed = (kb.IsKeyDown(Keys.Down) && !_prevKb.IsKeyDown(Keys.Down)) ||
                           (gp.DPad.Down == ButtonState.Pressed && _prevGp.DPad.Down != ButtonState.Pressed);
        bool acceptPressed = (kb.IsKeyDown(Keys.Enter) && !_prevKb.IsKeyDown(Keys.Enter)) ||
                             (gp.Buttons.A == ButtonState.Pressed && _prevGp.Buttons.A != ButtonState.Pressed);
        bool backPressed = (kb.IsKeyDown(Keys.Escape) && !_prevKb.IsKeyDown(Keys.Escape)) ||
                           (gp.Buttons.B == ButtonState.Pressed && _prevGp.Buttons.B != ButtonState.Pressed);

        if (upPressed) ChangeSelection(-1);
        if (downPressed) ChangeSelection(1);

        if (backPressed)
        {
            ClientPrefs.Save();
            // Return to previous state — caller handles navigation
        }
        else if (acceptPressed)
        {
            OpenSelectedSubstate(_options[CurSelected]);
        }

        foreach (var opt in _grpOptions) opt.Update(elapsed);

        _prevKb = kb;
        _prevGp = gp;
    }

    private void OpenSelectedSubstate(string label)
    {
        MusicBeatSubstate substate = label switch
        {
            "Note Colors" => new NotesColorSubState(),
            "Controls" => new ControlsSubState(),
            "Graphics" => new GraphicsSettingsSubState(),
            "Visuals" => new VisualsSettingsSubState(),
            "Gameplay" => new GameplaySettingsSubState(),
            "Adjust Delay and Combo" => new NoteOffsetState(),
            _ => null
        };

        if (substate != null)
        {
            substate.Create();
            _activeSubstate = substate;
        }
    }

    public void CloseSubState()
    {
        _activeSubstate = null;
        ClientPrefs.Save();
#if !XBOX_UWP
        DiscordClient.ChangePresence("Options Menu");
#endif
    }

    private void ChangeSelection(int change = 0)
    {
        CurSelected += change;
        if (CurSelected < 0) CurSelected = _options.Length - 1;
        if (CurSelected >= _options.Length) CurSelected = 0;

        for (int i = 0; i < _grpOptions.Count; i++)
        {
            var item = _grpOptions[i];
            item.TargetY = i - CurSelected;
            item.Alpha = (item.TargetY == 0) ? 1f : 0.6f;

            if (item.TargetY == 0)
            {
                _selectorLeft.X = item.X - 63;
                _selectorLeft.Y = item.Y;
                _selectorRight.X = item.X + item.Width + 15;
                _selectorRight.Y = item.Y;
            }
        }
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        base.Draw(spriteBatch);

        if (_activeSubstate != null)
        {
            _activeSubstate.Draw(spriteBatch);
            return;
        }

        if (_bgTexture != null)
        {
            var viewport = FNFGame.Instance.GraphicsDevice.Viewport;
            spriteBatch.Draw(_bgTexture, new Rectangle(0, 0, viewport.Width, viewport.Height), new Color(0xEA, 0x71, 0xFD));
        }

        foreach (var opt in _grpOptions) opt.Draw(spriteBatch);
        _selectorLeft?.Draw(spriteBatch);
        _selectorRight?.Draw(spriteBatch);
    }

    public void Destroy()
    {
        ClientPrefs.Load();
    }
}
