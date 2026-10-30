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
/// Psych Engine Stage Editor (Native Port)
/// Replaces the old StageEditorScene.
/// </summary>
public class StageEditorState : Scene
{
    private SpriteFontBase _font16;
    private FNF_MonoGame.Engine.SpriteSheet _bgSheet;
    
    // Stage variables
    private string _directory = "stage";
    private float _defaultZoom = 0.9f;
    private bool _isPixelStage = false;
    private float _bfX = 770f, _bfY = 100f;
    private float _gfX = 400f, _gfY = 130f;
    private float _dadX = 100f, _dadY = 100f;
    private float _bfCamX = 0f, _bfCamY = 0f;
    private float _gfCamX = 0f, _gfCamY = 0f;
    private float _dadCamX = 0f, _dadCamY = 0f;
    
    // UI Tabs
    private string _selectedTabTop = "Settings";

    public override void Load()
    {
#if !XBOX_UWP
        DiscordRPCManager.UpdatePresence("Stage Editor");
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
        
        // Handle Tab Switching
        if (EditorUI.IsMouseOver(new Rectangle(FNFGame.SCREEN_WIDTH - 375, 25, 125, 25)) && Input.MouseLeftPressed)
            _selectedTabTop = "Settings";
        if (EditorUI.IsMouseOver(new Rectangle(FNFGame.SCREEN_WIDTH - 250, 25, 125, 25)) && Input.MouseLeftPressed)
            _selectedTabTop = "Positions";
        if (EditorUI.IsMouseOver(new Rectangle(FNFGame.SCREEN_WIDTH - 125, 25, 125, 25)) && Input.MouseLeftPressed)
            _selectedTabTop = "Layers";
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

        // Stage Viewer Placeholder
        var viewRect = new Rectangle(50, 100, FNFGame.SCREEN_WIDTH - 500, 500);
        EditorUI.DrawBorder(spriteBatch, px, viewRect, Color.Cyan, 2);
        EditorUI.FillRect(spriteBatch, px, viewRect, new Color(0, 0, 0, 150));
        _font16.DrawText(spriteBatch, "[Stage Viewer Canvas]", new Vector2(viewRect.X + 300, viewRect.Y + 250), Color.Cyan);

        // UI BOX
        var boxRect = new Rectangle(FNFGame.SCREEN_WIDTH - 375, 50, 375, 300);
        DrawTab(spriteBatch, px, new Rectangle(boxRect.X, boxRect.Y - 25, 125, 25), "Settings", _selectedTabTop == "Settings");
        DrawTab(spriteBatch, px, new Rectangle(boxRect.X + 125, boxRect.Y - 25, 125, 25), "Positions", _selectedTabTop == "Positions");
        DrawTab(spriteBatch, px, new Rectangle(boxRect.X + 250, boxRect.Y - 25, 125, 25), "Layers", _selectedTabTop == "Layers");
        EditorUI.FillRect(spriteBatch, px, boxRect, new Color(0, 0, 0, 180));
        
        if (_selectedTabTop == "Settings")
        {
            EditorUI.PropertyRow(spriteBatch, px, _font16, boxRect.X + 10, boxRect.Y + 10, 350, "Directory:", _directory);
            EditorUI.PropertyRow(spriteBatch, px, _font16, boxRect.X + 10, boxRect.Y + 40, 350, "Default Zoom:", _defaultZoom.ToString("0.0"));
            _isPixelStage = EditorUI.ToolButton(spriteBatch, px, _font16, boxRect.X + 10, boxRect.Y + 70, 200, "Pixel Stage: " + _isPixelStage) ? !_isPixelStage : _isPixelStage;
            
            if (EditorUI.ToolButton(spriteBatch, px, _font16, boxRect.X + 10, boxRect.Y + 110, 170, "Save Stage")) { }
            if (EditorUI.ToolButton(spriteBatch, px, _font16, boxRect.X + 190, boxRect.Y + 110, 170, "Load Stage")) { }
        }
        else if (_selectedTabTop == "Positions")
        {
            _font16.DrawText(spriteBatch, "Boyfriend", new Vector2(boxRect.X + 10, boxRect.Y + 10), Color.Cyan);
            EditorUI.PropertyRow(spriteBatch, px, _font16, boxRect.X + 10, boxRect.Y + 30, 150, "X:", _bfX.ToString());
            EditorUI.PropertyRow(spriteBatch, px, _font16, boxRect.X + 170, boxRect.Y + 30, 150, "Y:", _bfY.ToString());
            
            _font16.DrawText(spriteBatch, "Girlfriend", new Vector2(boxRect.X + 10, boxRect.Y + 60), Color.HotPink);
            EditorUI.PropertyRow(spriteBatch, px, _font16, boxRect.X + 10, boxRect.Y + 80, 150, "X:", _gfX.ToString());
            EditorUI.PropertyRow(spriteBatch, px, _font16, boxRect.X + 170, boxRect.Y + 80, 150, "Y:", _gfY.ToString());
            
            _font16.DrawText(spriteBatch, "Opponent", new Vector2(boxRect.X + 10, boxRect.Y + 110), Color.Red);
            EditorUI.PropertyRow(spriteBatch, px, _font16, boxRect.X + 10, boxRect.Y + 130, 150, "X:", _dadX.ToString());
            EditorUI.PropertyRow(spriteBatch, px, _font16, boxRect.X + 170, boxRect.Y + 130, 150, "Y:", _dadY.ToString());
            
            if (EditorUI.ToolButton(spriteBatch, px, _font16, boxRect.X + 10, boxRect.Y + 170, 330, "Edit Camera Offsets")) { }
        }
        else if (_selectedTabTop == "Layers")
        {
            _font16.DrawText(spriteBatch, "BGSprites / Layers", new Vector2(boxRect.X + 10, boxRect.Y + 10), Color.White);
            if (EditorUI.ToolButton(spriteBatch, px, _font16, boxRect.X + 10, boxRect.Y + 40, 150, "Add Sprite")) { }
            if (EditorUI.ToolButton(spriteBatch, px, _font16, boxRect.X + 170, boxRect.Y + 40, 150, "Remove Sprite")) { }
            if (EditorUI.ToolButton(spriteBatch, px, _font16, boxRect.X + 10, boxRect.Y + 80, 310, "Edit Sprite Properties")) { }
        }

        EditorUI.DrawStatusBar(spriteBatch, px, _font16, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT, "Stage Editor Native Port (Psych Engine)", "Press ESC to return to Master Menu");

        spriteBatch.End();
    }
    
    private void DrawTab(SpriteBatch spriteBatch, Texture2D px, Rectangle rect, string text, bool selected)
    {
        EditorUI.FillRect(spriteBatch, px, rect, selected ? new Color(0, 0, 0, 180) : new Color(0, 0, 0, 100));
        _font16.DrawText(spriteBatch, text, new Vector2(rect.X + 15, rect.Y + 3), selected ? Color.White : Color.Gray);
    }
}
