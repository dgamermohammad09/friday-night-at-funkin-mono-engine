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

public class DialogueCharacterEditorState : Scene
{
    private SpriteFontBase _font16;
    private FNF_MonoGame.Engine.SpriteSheet _bgSheet;
    
    // UI state
    private string _curAnim = "idle";
    private int _loopFrame = 0;
    private Vector2 _portraitOffset = Vector2.Zero;

    public override void Load()
    {
#if !XBOX_UWP
        DiscordRPCManager.UpdatePresence("Dialogue Portrait Editor");
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
        
        if (_bgSheet != null && _bgSheet.Texture != null)
            spriteBatch.Draw(_bgSheet.Texture, new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT), new Color(0x30, 0x30, 0x30, 0xFF));
        else
            EditorUI.FillRect(spriteBatch, px, new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT), new Color(0x30, 0x30, 0x30, 0xFF));

        // Portrait Preview Placeholder
        var portRect = new Rectangle(FNFGame.SCREEN_WIDTH / 2 - 100, FNFGame.SCREEN_HEIGHT / 2 - 150, 200, 300);
        EditorUI.DrawBorder(spriteBatch, px, portRect, EditorUI.Accent, 2);
        _font16.DrawText(spriteBatch, "Portrait\nPreview", new Vector2(portRect.X + 60, portRect.Y + 130), EditorUI.Accent);

        // Draw Properties Panel (Top Right)
        var propRect = new Rectangle(FNFGame.SCREEN_WIDTH - 300, 20, 280, 260);
        EditorUI.DrawPanel(spriteBatch, px, propRect, "Portrait Properties", _font16);
        
        EditorUI.PropertyRow(spriteBatch, px, _font16, propRect.X + 20, propRect.Y + 40, 240, "Current Anim:", _curAnim);
        EditorUI.PropertyRow(spriteBatch, px, _font16, propRect.X + 20, propRect.Y + 70, 240, "Loop Frame:", _loopFrame.ToString());
        EditorUI.PropertyRow(spriteBatch, px, _font16, propRect.X + 20, propRect.Y + 100, 240, "Offsets:", $"X: {_portraitOffset.X}, Y: {_portraitOffset.Y}");
        
        if (EditorUI.ToolButton(spriteBatch, px, _font16, propRect.X + 20, propRect.Y + 140, 230, "Load Image")) { }
        if (EditorUI.ToolButton(spriteBatch, px, _font16, propRect.X + 20, propRect.Y + 180, 110, "Add Anim")) { }
        if (EditorUI.ToolButton(spriteBatch, px, _font16, propRect.X + 140, propRect.Y + 180, 110, "Remove Anim")) { }
        if (EditorUI.ToolButton(spriteBatch, px, _font16, propRect.X + 20, propRect.Y + 220, 230, "Save Character .json")) { }

        EditorUI.DrawStatusBar(spriteBatch, px, _font16, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT, "Dialogue Portrait Editor Native Port", "Press ESC to exit");

        spriteBatch.End();
    }
}
