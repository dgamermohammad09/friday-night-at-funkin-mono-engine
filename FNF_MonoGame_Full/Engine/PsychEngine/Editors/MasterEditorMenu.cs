using FontStashSharp;
using FNF_MonoGame.Sprites;
using FNF_MonoGame.Engine.PsychEngine.Objects;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Scenes;
using FNF_MonoGame.Engine.PsychEngine.Objects;

namespace FNF_MonoGame.Engine.PsychEngine.Editors;

/// <summary>
/// Psych Engine master editor menu.
/// Original: states/editors/MasterEditorMenu.hx
/// </summary>
public class MasterEditorMenu : Scene
{
    private readonly string[] _options = new[]
    {
        "Chart Editor",
        "Character Editor",
        "Stage Editor",
        "Week Editor",
        "Menu Character Editor",
        "Dialogue Editor",
        "Dialogue Portrait Editor",
        "Note Splash Editor"
    };

    private readonly List<Alphabet> _grpTexts = new();
    private int _curSelected = 0;
    
    // Mods directory switching (simplified for MonoGame unless Mods class is fully featured)
    private readonly List<string> _directories = new() { null };
    private int _curDirectory = 0;
    private FNF_MonoGame.Engine.SpriteSheet _bgSheet;

    public override void Load()
    {
#if !XBOX_UWP
        DiscordRPCManager.UpdatePresence("Editors Main Menu");
#endif

        string bgPath = PsychPaths.Image("menuDesat");
        if (!string.IsNullOrEmpty(bgPath))
        {
            string basePath = System.IO.Path.ChangeExtension(bgPath, null);
            _bgSheet = SpriteSheet.Load(FNFGame.Instance, basePath);
        }

        for (int i = 0; i < _options.Length; i++)
        {
            var leText = new Alphabet(90, 320, _options[i], true);
            leText.IsMenuItem = true;
            leText.TargetY = i;
            _grpTexts.Add(leText);
            leText.SnapToPosition();
        }

        ChangeSelection(0);
    }

    public override void Unload() { }

    public override void Update(GameTime gameTime)
    {
        float elapsed = (float)gameTime.ElapsedGameTime.TotalSeconds;

        var kbState = Microsoft.Xna.Framework.Input.Keyboard.GetState();
        var gpState = Microsoft.Xna.Framework.Input.GamePad.GetState(Microsoft.Xna.Framework.PlayerIndex.One);

        bool up = kbState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Up) || gpState.DPad.Up == Microsoft.Xna.Framework.Input.ButtonState.Pressed;
        bool down = kbState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Down) || gpState.DPad.Down == Microsoft.Xna.Framework.Input.ButtonState.Pressed;
        bool accept = kbState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Enter) || gpState.Buttons.A == Microsoft.Xna.Framework.Input.ButtonState.Pressed;
        bool back = kbState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Escape) || gpState.Buttons.B == Microsoft.Xna.Framework.Input.ButtonState.Pressed;

        if (up && !_prevUp) ChangeSelection(-1);
        if (down && !_prevDown) ChangeSelection(1);

        _prevUp = up;
        _prevDown = down;

        if (back)
        {
            FNFGame.Instance.Scenes.ChangeScene(new MainMenuScene());
            return;
        }

        if (accept)
        {
            switch (_options[_curSelected])
            {
                case "Chart Editor":
                    FNFGame.Instance.Scenes.ChangeScene(new ChartingState());
                    break;
                case "Character Editor":
                    FNFGame.Instance.Scenes.ChangeScene(new CharacterEditorState());
                    break;
                case "Stage Editor":
                    FNFGame.Instance.Scenes.ChangeScene(new StageEditorState());
                    break;
                case "Week Editor":
                    FNFGame.Instance.Scenes.ChangeScene(new WeekEditorState());
                    break;
                case "Menu Character Editor":
                    FNFGame.Instance.Scenes.ChangeScene(new MenuCharacterEditorState());
                    break;
                case "Dialogue Editor":
                    FNFGame.Instance.Scenes.ChangeScene(new DialogueEditorState());
                    break;
                case "Dialogue Portrait Editor":
                    FNFGame.Instance.Scenes.ChangeScene(new DialogueCharacterEditorState());
                    break;
                case "Note Splash Editor":
                    FNFGame.Instance.Scenes.ChangeScene(new NoteSplashEditorState());
                    break;
            }
            
            if (FNFGame.Instance.Audio != null && FNFGame.Instance.Audio.Music != null)
                FNFGame.Instance.Audio.Music.Volume = 0;
        }

        for (int i = 0; i < _grpTexts.Count; i++)
        {
            var item = _grpTexts[i];
            item.TargetY = i - _curSelected;
            item.Alpha = 0.6f;
            if (item.TargetY == 0)
                item.Alpha = 1f;
            item.Update(elapsed);
        }
    }

    public override void Draw(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
    {
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);

        if (_bgSheet != null && _bgSheet.Texture != null)
        {
            spriteBatch.Draw(_bgSheet.Texture, new Microsoft.Xna.Framework.Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT), new Microsoft.Xna.Framework.Color(0x35, 0x35, 0x35, 0xFF));
        }

        foreach (var text in _grpTexts)
        {
            text.Draw(spriteBatch);
        }
        
        spriteBatch.End();
    }

    private bool _prevUp;
    private bool _prevDown;

    private void ChangeSelection(int change)
    {
        FNFGame.Instance?.Audio?.PlaySound(PsychPaths.Sound("scrollMenu"), 0.4f);
        _curSelected += change;

        if (_curSelected < 0) _curSelected = _options.Length - 1;
        if (_curSelected >= _options.Length) _curSelected = 0;
    }
}
