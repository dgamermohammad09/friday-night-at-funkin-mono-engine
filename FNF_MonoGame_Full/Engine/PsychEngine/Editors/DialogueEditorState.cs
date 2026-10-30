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

public class DialogueEditorState : Scene
{
    private SpriteFontBase _font16;
    private FNF_MonoGame.Engine.SpriteSheet _bgSheet;
    
    // UI state
    private string _curCharacter = "bf";
    private string _curBoxType = "normal";

    public override void Load()
    {
#if !XBOX_UWP
        DiscordRPCManager.UpdatePresence("Dialogue Editor");
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

        // Center Box Placeholder
        var boxRect = new Rectangle(FNFGame.SCREEN_WIDTH / 2 - 300, FNFGame.SCREEN_HEIGHT - 250, 600, 200);
        EditorUI.DrawBorder(spriteBatch, px, boxRect, EditorUI.Accent, 2);
        EditorUI.FillRect(spriteBatch, px, boxRect, new Color(0, 0, 0, 150));
        _font16.DrawText(spriteBatch, "[Dialogue Box Preview]", new Vector2(boxRect.X + 220, boxRect.Y + 80), EditorUI.TextSecondary);

        // Portrait Placeholder
        var portRect = new Rectangle(boxRect.X + 50, boxRect.Y - 200, 150, 200);
        EditorUI.DrawBorder(spriteBatch, px, portRect, EditorUI.Success, 2);
        _font16.DrawText(spriteBatch, "Portrait:\n" + _curCharacter, new Vector2(portRect.X + 10, portRect.Y + 80), EditorUI.Success);

        // Draw Properties Panel (Top Right)
        var propRect = new Rectangle(FNFGame.SCREEN_WIDTH - 300, 20, 280, 260);
        EditorUI.DrawPanel(spriteBatch, px, propRect, "Dialogue Properties", _font16);
        
        EditorUI.PropertyRow(spriteBatch, px, _font16, propRect.X + 20, propRect.Y + 40, 240, "Character:", _curCharacter);
        EditorUI.PropertyRow(spriteBatch, px, _font16, propRect.X + 20, propRect.Y + 70, 240, "Box Type:", _curBoxType);
        
        if (EditorUI.ToolButton(spriteBatch, px, _font16, propRect.X + 20, propRect.Y + 110, 110, "Load Character")) { }
        if (EditorUI.ToolButton(spriteBatch, px, _font16, propRect.X + 140, propRect.Y + 110, 110, "Change Box")) { }
        
        if (EditorUI.ToolButton(spriteBatch, px, _font16, propRect.X + 20, propRect.Y + 160, 230, "Add Dialogue Line")) { }
        if (EditorUI.ToolButton(spriteBatch, px, _font16, propRect.X + 20, propRect.Y + 200, 230, "Save Dialogue .json")) { }

        EditorUI.DrawStatusBar(spriteBatch, px, _font16, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT, "Dialogue Editor Native Port", "Press ESC to exit");

        spriteBatch.End();
    }
}
