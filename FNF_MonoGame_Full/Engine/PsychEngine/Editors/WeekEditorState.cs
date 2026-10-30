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
/// Psych Engine Week Editor (Native Port)
/// Replaces the old StoryModeEditorScene.
/// </summary>
public class WeekEditorState : Scene
{
    private SpriteFontBase _font16;
    private FNF_MonoGame.Engine.SpriteSheet _bgSheet;
    
    // Week variables
    private string _weekName = "Week 1";
    private string _songs = "Bopeebo, Fresh, Dadbattle";
    private string _characters = "dad, bf, gf";
    private string _bgAsset = "stage";
    private string _weekTitle = "Daddy Dearest";
    private Color _freeplayColor = new Color(146, 113, 253);
    
    private bool _hideStoryMode = false;
    private bool _hideFreeplay = false;
    private bool _weekLocked = false;
    
    // UI Tabs
    private string _selectedTab = "Week";

    public override void Load()
    {
#if !XBOX_UWP
        DiscordRPCManager.UpdatePresence("Week Editor");
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
            _selectedTab = "Week";
        if (EditorUI.IsMouseOver(new Rectangle(FNFGame.SCREEN_WIDTH - 250, 25, 125, 25)) && Input.MouseLeftPressed)
            _selectedTab = "Other";
        if (EditorUI.IsMouseOver(new Rectangle(FNFGame.SCREEN_WIDTH - 125, 25, 125, 25)) && Input.MouseLeftPressed)
            _selectedTab = "Save";
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        var px = FNFGame.Instance.Assets.Pixel;

        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);
        
        // Background
        if (_bgSheet != null && _bgSheet.Texture != null)
            spriteBatch.Draw(_bgSheet.Texture, new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT), _freeplayColor);
        else
            EditorUI.FillRect(spriteBatch, px, new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT), _freeplayColor);

        // Story Mode Menu Preview Placeholder
        var previewRect = new Rectangle(50, 100, FNFGame.SCREEN_WIDTH - 500, 400);
        EditorUI.DrawBorder(spriteBatch, px, previewRect, Color.White, 2);
        EditorUI.FillRect(spriteBatch, px, previewRect, new Color(0, 0, 0, 150));
        _font16.DrawText(spriteBatch, "[Story Mode / Freeplay Visual Preview]", new Vector2(previewRect.X + 200, previewRect.Y + 200), Color.White);

        // UI BOX
        var boxRect = new Rectangle(FNFGame.SCREEN_WIDTH - 375, 50, 375, 350);
        DrawTab(spriteBatch, px, new Rectangle(boxRect.X, boxRect.Y - 25, 125, 25), "Week", _selectedTab == "Week");
        DrawTab(spriteBatch, px, new Rectangle(boxRect.X + 125, boxRect.Y - 25, 125, 25), "Other", _selectedTab == "Other");
        DrawTab(spriteBatch, px, new Rectangle(boxRect.X + 250, boxRect.Y - 25, 125, 25), "Save", _selectedTab == "Save");
        EditorUI.FillRect(spriteBatch, px, boxRect, new Color(0, 0, 0, 180));
        
        if (_selectedTab == "Week")
        {
            EditorUI.PropertyRow(spriteBatch, px, _font16, boxRect.X + 10, boxRect.Y + 10, 350, "Songs:", _songs);
            EditorUI.PropertyRow(spriteBatch, px, _font16, boxRect.X + 10, boxRect.Y + 40, 350, "Characters:", _characters);
            EditorUI.PropertyRow(spriteBatch, px, _font16, boxRect.X + 10, boxRect.Y + 70, 350, "BG Asset:", _bgAsset);
            EditorUI.PropertyRow(spriteBatch, px, _font16, boxRect.X + 10, boxRect.Y + 100, 350, "Week Name:", _weekName);
            EditorUI.PropertyRow(spriteBatch, px, _font16, boxRect.X + 10, boxRect.Y + 130, 350, "Week Title:", _weekTitle);
            
            _hideStoryMode = EditorUI.ToolButton(spriteBatch, px, _font16, boxRect.X + 10, boxRect.Y + 170, 170, "Hide Story Mode: " + _hideStoryMode) ? !_hideStoryMode : _hideStoryMode;
            _hideFreeplay = EditorUI.ToolButton(spriteBatch, px, _font16, boxRect.X + 190, boxRect.Y + 170, 170, "Hide Freeplay: " + _hideFreeplay) ? !_hideFreeplay : _hideFreeplay;
            _weekLocked = EditorUI.ToolButton(spriteBatch, px, _font16, boxRect.X + 10, boxRect.Y + 210, 170, "Initially Locked: " + _weekLocked) ? !_weekLocked : _weekLocked;
        }
        else if (_selectedTab == "Other")
        {
            EditorUI.PropertyRow(spriteBatch, px, _font16, boxRect.X + 10, boxRect.Y + 10, 350, "Freeplay Color:", _freeplayColor.R + "," + _freeplayColor.G + "," + _freeplayColor.B);
            
            if (EditorUI.ToolButton(spriteBatch, px, _font16, boxRect.X + 10, boxRect.Y + 50, 150, "Pick Color")) { }
        }
        else if (_selectedTab == "Save")
        {
            if (EditorUI.ToolButton(spriteBatch, px, _font16, boxRect.X + 10, boxRect.Y + 10, 150, "Save Week")) { }
            if (EditorUI.ToolButton(spriteBatch, px, _font16, boxRect.X + 170, boxRect.Y + 10, 150, "Load Week")) { }
        }

        EditorUI.DrawStatusBar(spriteBatch, px, _font16, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT, "Week Editor Native Port (Psych Engine)", "Press ESC to return to Master Menu");

        spriteBatch.End();
    }
    
    private void DrawTab(SpriteBatch spriteBatch, Texture2D px, Rectangle rect, string text, bool selected)
    {
        EditorUI.FillRect(spriteBatch, px, rect, selected ? new Color(0, 0, 0, 180) : new Color(0, 0, 0, 100));
        _font16.DrawText(spriteBatch, text, new Vector2(rect.X + 15, rect.Y + 3), selected ? Color.White : Color.Gray);
    }
}
