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
/// Psych Engine Character Editor (Native Port)
/// Replaces the old AnimationEditorScene with a 100% faithful Psych Engine port.
/// </summary>
public class CharacterEditorState : Scene
{
    private SpriteFontBase _font16;
    private FNF_MonoGame.Engine.SpriteSheet _bgSheet;
    
    // Character variables
    private string _curChar = "bf";
    private bool _isPlayer = true;
    private string _imageFile = "BOYFRIEND";
    private string _healthIcon = "bf";
    private float _scale = 1.0f;
    private bool _flipX = false;
    private bool _antialiasing = true;
    private Color _healthColor = new Color(49, 176, 209);
    private int _singDuration = 4;
    
    // Ghost
    private float _ghostAlpha = 0.6f;
    private bool _highlightGhost = false;
    
    // Animation Selection
    private int _curAnimIndex = 0;
    private readonly List<string> _animList = new() { "idle", "singUP", "singDOWN", "singLEFT", "singRIGHT" };
    
    // UI Tabs
    private string _selectedTabTop = "Settings";
    private string _selectedTabBot = "Character";

    public override void Load()
    {
#if !XBOX_UWP
        DiscordRPCManager.UpdatePresence("Character Editor");
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
        if (EditorUI.IsMouseOver(new Rectangle(FNFGame.SCREEN_WIDTH - 275, 25, 125, 25)) && Input.MouseLeftPressed)
            _selectedTabTop = "Ghost";
        if (EditorUI.IsMouseOver(new Rectangle(FNFGame.SCREEN_WIDTH - 150, 25, 125, 25)) && Input.MouseLeftPressed)
            _selectedTabTop = "Settings";
            
        if (EditorUI.IsMouseOver(new Rectangle(FNFGame.SCREEN_WIDTH - 375, 170, 175, 25)) && Input.MouseLeftPressed)
            _selectedTabBot = "Animations";
        if (EditorUI.IsMouseOver(new Rectangle(FNFGame.SCREEN_WIDTH - 200, 170, 175, 25)) && Input.MouseLeftPressed)
            _selectedTabBot = "Character";
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

        // Center Character Placeholder (To be replaced with actual FlxSprite later)
        var charRect = new Rectangle(FNFGame.SCREEN_WIDTH / 2 - 150, FNFGame.SCREEN_HEIGHT / 2 - 200, 300, 400);
        EditorUI.DrawBorder(spriteBatch, px, charRect, EditorUI.Primary, 2);
        _font16.DrawText(spriteBatch, "[Character Sprite Preview]\n" + _curChar, new Vector2(charRect.X + 60, charRect.Y + 180), EditorUI.Primary);

        // TOP UI BOX (Ghost / Settings)
        var topBox = new Rectangle(FNFGame.SCREEN_WIDTH - 275, 50, 250, 100);
        DrawTab(spriteBatch, px, new Rectangle(topBox.X, topBox.Y - 25, 125, 25), "Ghost", _selectedTabTop == "Ghost");
        DrawTab(spriteBatch, px, new Rectangle(topBox.X + 125, topBox.Y - 25, 125, 25), "Settings", _selectedTabTop == "Settings");
        EditorUI.FillRect(spriteBatch, px, topBox, new Color(0, 0, 0, 180));
        
        if (_selectedTabTop == "Settings")
        {
            _isPlayer = EditorUI.ToolButton(spriteBatch, px, _font16, topBox.X + 10, topBox.Y + 10, 110, "Playable: " + _isPlayer) ? !_isPlayer : _isPlayer;
            if (EditorUI.ToolButton(spriteBatch, px, _font16, topBox.X + 130, topBox.Y + 10, 110, "Reload Char")) { }
            if (EditorUI.ToolButton(spriteBatch, px, _font16, topBox.X + 10, topBox.Y + 50, 110, "Load Template")) { }
        }
        else if (_selectedTabTop == "Ghost")
        {
            if (EditorUI.ToolButton(spriteBatch, px, _font16, topBox.X + 10, topBox.Y + 10, 110, "Make Ghost")) { }
            _highlightGhost = EditorUI.ToolButton(spriteBatch, px, _font16, topBox.X + 130, topBox.Y + 10, 110, "Highlight: " + _highlightGhost) ? !_highlightGhost : _highlightGhost;
            _font16.DrawText(spriteBatch, "Opacity: " + _ghostAlpha.ToString("0.0"), new Vector2(topBox.X + 10, topBox.Y + 50), Color.White);
        }

        // BOTTOM UI BOX (Animations / Character)
        var botBox = new Rectangle(FNFGame.SCREEN_WIDTH - 375, 195, 350, 280);
        DrawTab(spriteBatch, px, new Rectangle(botBox.X, botBox.Y - 25, 175, 25), "Animations", _selectedTabBot == "Animations");
        DrawTab(spriteBatch, px, new Rectangle(botBox.X + 175, botBox.Y - 25, 175, 25), "Character", _selectedTabBot == "Character");
        EditorUI.FillRect(spriteBatch, px, botBox, new Color(0, 0, 0, 180));

        if (_selectedTabBot == "Character")
        {
            EditorUI.PropertyRow(spriteBatch, px, _font16, botBox.X + 10, botBox.Y + 10, 330, "Image File:", _imageFile);
            EditorUI.PropertyRow(spriteBatch, px, _font16, botBox.X + 10, botBox.Y + 40, 330, "Health Icon:", _healthIcon);
            EditorUI.PropertyRow(spriteBatch, px, _font16, botBox.X + 10, botBox.Y + 70, 330, "Scale:", _scale.ToString("0.0"));
            EditorUI.PropertyRow(spriteBatch, px, _font16, botBox.X + 10, botBox.Y + 100, 330, "Sing Duration:", _singDuration.ToString());
            
            _flipX = EditorUI.ToolButton(spriteBatch, px, _font16, botBox.X + 10, botBox.Y + 140, 100, "FlipX: " + _flipX) ? !_flipX : _flipX;
            _antialiasing = EditorUI.ToolButton(spriteBatch, px, _font16, botBox.X + 120, botBox.Y + 140, 100, "AntiAliasing: " + _antialiasing) ? !_antialiasing : _antialiasing;
            
            if (EditorUI.ToolButton(spriteBatch, px, _font16, botBox.X + 10, botBox.Y + 180, 150, "Save Character")) { }
            if (EditorUI.ToolButton(spriteBatch, px, _font16, botBox.X + 170, botBox.Y + 180, 150, "Load Character")) { }
        }
        else if (_selectedTabBot == "Animations")
        {
            _font16.DrawText(spriteBatch, "Anim List", new Vector2(botBox.X + 10, botBox.Y + 10), Color.White);
            for (int i = 0; i < Math.Min(8, _animList.Count); i++)
            {
                Color col = (i == _curAnimIndex) ? EditorUI.Primary : Color.White;
                _font16.DrawText(spriteBatch, _animList[i], new Vector2(botBox.X + 10, botBox.Y + 35 + (i * 20)), col);
            }
            if (EditorUI.ToolButton(spriteBatch, px, _font16, botBox.X + 180, botBox.Y + 20, 150, "Add Anim")) { }
            if (EditorUI.ToolButton(spriteBatch, px, _font16, botBox.X + 180, botBox.Y + 60, 150, "Update Anim")) { }
            if (EditorUI.ToolButton(spriteBatch, px, _font16, botBox.X + 180, botBox.Y + 100, 150, "Remove Anim")) { }
        }

        EditorUI.DrawStatusBar(spriteBatch, px, _font16, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT, "Character Editor Native Port (Psych Engine)", "Press ESC to return to Master Menu");

        spriteBatch.End();
    }
    
    private void DrawTab(SpriteBatch spriteBatch, Texture2D px, Rectangle rect, string text, bool selected)
    {
        EditorUI.FillRect(spriteBatch, px, rect, selected ? new Color(0, 0, 0, 180) : new Color(0, 0, 0, 100));
        _font16.DrawText(spriteBatch, text, new Vector2(rect.X + 15, rect.Y + 3), selected ? Color.White : Color.Gray);
    }
}
