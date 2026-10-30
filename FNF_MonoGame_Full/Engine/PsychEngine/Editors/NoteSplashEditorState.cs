using FontStashSharp;
using FNF_MonoGame.Sprites;
using FNF_MonoGame.Engine.PsychEngine.Objects;
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Scenes;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.PsychEngine.Objects;

namespace FNF_MonoGame.Engine.PsychEngine.Editors;

public class NoteSplashEditorState : Scene
{
    private SpriteFontBase _font16;
    private FNF_MonoGame.Engine.SpriteSheet _bgSheet;
    
    private NoteSplash _splash;
    private float _timer = 0f;
    private int _curNoteData = 0;
    
    // UI state
    private bool _allowRGB = true;
    private bool _allowPixel = true;
    private int _rgbMode = 0; // 0=Red, 1=Green, 2=Blue

    public override void Load()
    {
#if !XBOX_UWP
        DiscordRPCManager.UpdatePresence("Note Splash Editor");
#endif

        string bgPath = PsychPaths.Image("menuDesat");
        if (!string.IsNullOrEmpty(bgPath))
        {
            string basePath = System.IO.Path.ChangeExtension(bgPath, null);
            _bgSheet = SpriteSheet.Load(FNFGame.Instance, basePath);
        }

        _font16 = FNFGame.Instance.Assets.GetFont(16);
        
        _splash = new NoteSplash(FNFGame.SCREEN_WIDTH / 2, FNFGame.SCREEN_HEIGHT / 2);
    }

    public override void Unload() { }

    public override void Update(GameTime gameTime)
    {
        EditorUI.UpdateInput();

        if (Input.BackPressed)
        {
            FNFGame.Instance.Scenes.ChangeScene(new MasterEditorMenu());
            return;
        }
        
        float elapsed = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _splash?.Update(elapsed);
        
        _timer += elapsed;
        if (_timer > 1.5f)
        {
            _timer = 0f;
            _curNoteData = (_curNoteData + 1) % 4;
            _splash?.SpawnSplashNote(FNFGame.SCREEN_WIDTH / 2 - 50, FNFGame.SCREEN_HEIGHT / 2 - 50, _curNoteData);
        }
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        var px = FNFGame.Instance.Assets.Pixel;

        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);

        // Background
        if (_bgSheet != null && _bgSheet.Texture != null)
            spriteBatch.Draw(_bgSheet.Texture, new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT), new Color(0x30, 0x30, 0x30, 0xFF));
        else
            EditorUI.FillRect(spriteBatch, px, new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT), new Color(0x30, 0x30, 0x30, 0xFF));

        // Splash Render
        _splash?.Draw(spriteBatch);

        // Draw Properties Panel (Top Right)
        var propRect = new Rectangle(FNFGame.SCREEN_WIDTH - 300, 20, 280, 200);
        EditorUI.DrawPanel(spriteBatch, px, propRect, "Properties", _font16);
        
        if (EditorUI.ToolButton(spriteBatch, px, _font16, propRect.X + 20, propRect.Y + 40, 120, "Reload Image"))
        {
            _splash.LoadSplash();
        }
        
        if (EditorUI.Toggle(spriteBatch, px, propRect.X + 20, propRect.Y + 80, _allowRGB)) _allowRGB = !_allowRGB;
        _font16.DrawText(spriteBatch, "Allow RGB?", new Vector2(propRect.X + 60, propRect.Y + 82), EditorUI.TextPrimary);

        if (EditorUI.Toggle(spriteBatch, px, propRect.X + 20, propRect.Y + 110, _allowPixel)) _allowPixel = !_allowPixel;
        _font16.DrawText(spriteBatch, "Allow Pixel?", new Vector2(propRect.X + 60, propRect.Y + 112), EditorUI.TextPrimary);

        if (EditorUI.ToolButton(spriteBatch, px, _font16, propRect.X + 20, propRect.Y + 150, 100, "Save config"))
        {
            // Serialize
        }

        // Draw Shader Panel (Below Properties)
        var shaderRect = new Rectangle(FNFGame.SCREEN_WIDTH - 300, 240, 280, 180);
        EditorUI.DrawPanel(spriteBatch, px, shaderRect, "Shader", _font16);
        
        _font16.DrawText(spriteBatch, "Replacing Color:", new Vector2(shaderRect.X + 20, shaderRect.Y + 40), EditorUI.TextSecondary);
        if (EditorUI.ToolButton(spriteBatch, px, _font16, shaderRect.X + 20, shaderRect.Y + 70, 70, "Red", _rgbMode == 0)) _rgbMode = 0;
        if (EditorUI.ToolButton(spriteBatch, px, _font16, shaderRect.X + 100, shaderRect.Y + 70, 70, "Green", _rgbMode == 1)) _rgbMode = 1;
        if (EditorUI.ToolButton(spriteBatch, px, _font16, shaderRect.X + 180, shaderRect.Y + 70, 70, "Blue", _rgbMode == 2)) _rgbMode = 2;

        EditorUI.DrawStatusBar(spriteBatch, px, _font16, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT, "Note Splash Editor Native Port", "Press ESC to exit");

        spriteBatch.End();
    }
}
