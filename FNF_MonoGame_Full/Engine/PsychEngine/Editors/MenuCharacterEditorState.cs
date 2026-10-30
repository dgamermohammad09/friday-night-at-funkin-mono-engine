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

/// <summary>
/// Psych Engine Menu Character Editor (Native Port)
/// Replaces the old CharSelectEditorScene.
/// </summary>
public class MenuCharacterEditorState : Scene
{
    private SpriteFontBase _font16;
    private FNF_MonoGame.Engine.SpriteSheet _bgSheet;
    
    // Character variables
    private string _imageFile = "Menu_Dad";
    private float _scale = 1.0f;
    private float _posX = 0f;
    private float _posY = 0f;
    private string _idleAnim = "M Dad Idle";
    private string _confirmAnim = "M Dad CONFIRM";
    private bool _flipX = false;
    
    public override void Load()
    {
#if !XBOX_UWP
        DiscordRPCManager.UpdatePresence("Menu Character Editor");
#endif
        string bgPath = PsychPaths.Image("menuDesat");
        if (!string.IsNullOrEmpty(bgPath))
        {
            string basePath = System.IO.Path.ChangeExtension(bgPath, null);
            _bgSheet = SpriteSheet.Load(FNFGame.Instance, basePath);
        }
        _font16 = FNFGame.Instance.Assets.GetFont(16);
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

        // Center Story Mode Display Placeholder
        var charRect = new Rectangle(FNFGame.SCREEN_WIDTH / 2 - 150, FNFGame.SCREEN_HEIGHT / 2 - 150, 300, 300);
        EditorUI.DrawBorder(spriteBatch, px, charRect, EditorUI.Primary, 2);
        _font16.DrawText(spriteBatch, "[Menu Character Preview]", new Vector2(charRect.X + 60, charRect.Y + 130), EditorUI.Primary);

        // TOP UI BOX (Settings)
        var topBox = new Rectangle(FNFGame.SCREEN_WIDTH - 275, 50, 250, 260);
        EditorUI.DrawPanel(spriteBatch, px, topBox, "Menu Character Settings", _font16);
        
        EditorUI.PropertyRow(spriteBatch, px, _font16, topBox.X + 10, topBox.Y + 40, 230, "Image File:", _imageFile);
        EditorUI.PropertyRow(spriteBatch, px, _font16, topBox.X + 10, topBox.Y + 70, 230, "Idle Anim:", _idleAnim);
        EditorUI.PropertyRow(spriteBatch, px, _font16, topBox.X + 10, topBox.Y + 100, 230, "Confirm Anim:", _confirmAnim);
        EditorUI.PropertyRow(spriteBatch, px, _font16, topBox.X + 10, topBox.Y + 130, 230, "Scale:", _scale.ToString("0.0"));
        EditorUI.PropertyRow(spriteBatch, px, _font16, topBox.X + 10, topBox.Y + 160, 230, "Position X:", _posX.ToString());
        EditorUI.PropertyRow(spriteBatch, px, _font16, topBox.X + 10, topBox.Y + 190, 230, "Position Y:", _posY.ToString());
        
        _flipX = EditorUI.ToolButton(spriteBatch, px, _font16, topBox.X + 10, topBox.Y + 220, 100, "FlipX: " + _flipX) ? !_flipX : _flipX;
        
        if (EditorUI.ToolButton(spriteBatch, px, _font16, topBox.X + 120, topBox.Y + 220, 120, "Save Json")) { }

        EditorUI.DrawStatusBar(spriteBatch, px, _font16, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT, "Menu Character Editor Native Port (Psych Engine)", "Press ESC to return to Master Menu");

        spriteBatch.End();
    }
}
