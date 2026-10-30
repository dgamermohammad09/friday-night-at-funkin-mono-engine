using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine.PsychEngine.Objects;
using FNF_MonoGame.Gameplay; // For StrumNote rendering dependencies if needed

namespace FNF_MonoGame.Engine.PsychEngine.Options;

/// <summary>
/// Psych Engine note color customization substate (100% Native C#).
/// Allows changing RGB values for each note column (left, down, up, right)
/// for both regular and pixel note skins, natively in MonoGame.
/// </summary>
public class NotesColorSubState : MusicBeatSubstate
{
    private bool _onModeColumn = true;
    private int _curSelectedMode = 0; // 0=Base, 1=Outline, 2=Shadow
    private int _curSelectedNote = 0; // 0=Left, 1=Down, 2=Up, 3=Right
    private bool _onPixel = false;

    private int _selectedComponent = 0; // 0=R, 1=G, 2=B
    private readonly string[] _modeLabels = { "Base", "Outline", "Shadow" };
    private readonly string[] _noteLabels = { "Left", "Down", "Up", "Right" };
    private readonly Color[] _noteColors = { new(0xC2, 0x4B, 0x99), new(0x00, 0xFF, 0xFF), new(0x12, 0xFA, 0x05), new(0xF9, 0x39, 0x3F) };

    private Texture2D _solidTex;
    private Texture2D _bgTexture;
    
    // Native C# AnimatedSprites for rendering the actual notes
    private AnimatedSprite[] _noteSprites;

    private KeyboardState _prevKb;
    private GamePadState _prevGp;
    private float _holdTime;

    public NotesColorSubState()
    {
        Title = "Note Colors";
        RpcTitle = "Note Colors Menu";
    }

    public string Title { get; set; }
    public string RpcTitle { get; set; }

    public override void Create()
    {
        base.Create();

#if !XBOX_UWP
        DiscordClient.ChangePresence("Note Colors Menu");
#endif

        _solidTex = CreateSolidTexture(Color.White, 1, 1);

        string path = PsychPaths.Image("menuDesat");
        if (!string.IsNullOrEmpty(path))
        {
            string basePath = System.IO.Path.ChangeExtension(path, null);
            var sheet = SpriteSheet.Load(FNFGame.Instance, basePath);
            _bgTexture = sheet?.Texture;
        }

        // Initialize Native C# Note Sprites
        _noteSprites = new AnimatedSprite[4];
        for (int i = 0; i < 4; i++)
        {
            _noteSprites[i] = new AnimatedSprite();
        }
        LoadNoteSkins();

        _prevKb = Keyboard.GetState();
        _prevGp = GamePad.GetState(PlayerIndex.One);
    }

    private void LoadNoteSkins()
    {
        string skin = _onPixel ? "pixelUI/NOTE_assets" : Note.DefaultNoteSkin;
        string postfix = "";
        if (!string.IsNullOrEmpty(ClientPrefs.Data.NoteSkin) && ClientPrefs.Data.NoteSkin != ClientPrefs.DefaultData.NoteSkin)
        {
            postfix = "-" + ClientPrefs.Data.NoteSkin.Trim().ToLowerInvariant().Replace(' ', '-');
        }
        
        string customSkin = skin + postfix;
        string path = PsychPaths.Image(customSkin);
        if (string.IsNullOrEmpty(path)) path = PsychPaths.Image(skin);

        if (!string.IsNullOrEmpty(path))
        {
            string basePath = System.IO.Path.ChangeExtension(path, null);
            var sheet = SpriteSheet.Load(FNFGame.Instance, basePath);
            if (sheet != null)
            {
                string[] dirPrefixes = _onPixel 
                    ? new[] { "left instance", "down instance", "up instance", "right instance" } 
                    : new[] { "arrowLEFT", "arrowDOWN", "arrowUP", "arrowRIGHT" };

                for (int i = 0; i < 4; i++)
                {
                    _noteSprites[i].Sheet = sheet;
                    _noteSprites[i].PlayAnimation(dirPrefixes[i], true, false);
                }
            }
        }
    }

    public override void Update(float elapsed)
    {
        base.Update(elapsed);

        var kb = Keyboard.GetState();
        var gp = GamePad.GetState(PlayerIndex.One);

        bool upPressed = (kb.IsKeyDown(Keys.Up) && !_prevKb.IsKeyDown(Keys.Up)) ||
                         (gp.DPad.Up == ButtonState.Pressed && _prevGp.DPad.Up != ButtonState.Pressed);
        bool downPressed = (kb.IsKeyDown(Keys.Down) && !_prevKb.IsKeyDown(Keys.Down)) ||
                           (gp.DPad.Down == ButtonState.Pressed && _prevGp.DPad.Down != ButtonState.Pressed);
        bool leftPressed = (kb.IsKeyDown(Keys.Left) && !_prevKb.IsKeyDown(Keys.Left)) ||
                           (gp.DPad.Left == ButtonState.Pressed && _prevGp.DPad.Left != ButtonState.Pressed);
        bool rightPressed = (kb.IsKeyDown(Keys.Right) && !_prevKb.IsKeyDown(Keys.Right)) ||
                            (gp.DPad.Right == ButtonState.Pressed && _prevGp.DPad.Right != ButtonState.Pressed);
        bool leftHeld = kb.IsKeyDown(Keys.Left) || gp.DPad.Left == ButtonState.Pressed;
        bool rightHeld = kb.IsKeyDown(Keys.Right) || gp.DPad.Right == ButtonState.Pressed;
        bool leftReleased = (!kb.IsKeyDown(Keys.Left) && _prevKb.IsKeyDown(Keys.Left));
        bool rightReleased = (!kb.IsKeyDown(Keys.Right) && _prevKb.IsKeyDown(Keys.Right));
        bool acceptPressed = (kb.IsKeyDown(Keys.Enter) && !_prevKb.IsKeyDown(Keys.Enter)) ||
                             (gp.Buttons.A == ButtonState.Pressed && _prevGp.Buttons.A != ButtonState.Pressed);
        bool backPressed = (kb.IsKeyDown(Keys.Escape) && !_prevKb.IsKeyDown(Keys.Escape)) ||
                           (gp.Buttons.B == ButtonState.Pressed && _prevGp.Buttons.B != ButtonState.Pressed);
        bool resetPressed = (kb.IsKeyDown(Keys.R) && !_prevKb.IsKeyDown(Keys.R));
        bool tabPressed = (kb.IsKeyDown(Keys.Tab) && !_prevKb.IsKeyDown(Keys.Tab));

        if (backPressed)
        {
            if (!_onModeColumn)
            {
                _onModeColumn = true;
            }
            else
            {
                ClientPrefs.Save();
                Close();
            }
            _prevKb = kb;
            _prevGp = gp;
            return;
        }

        if (tabPressed)
        {
            _onPixel = !_onPixel;
            LoadNoteSkins();
        }

        if (_onModeColumn)
        {
            if (leftPressed) _curSelectedNote = Wrap(_curSelectedNote - 1, 0, 3);
            if (rightPressed) _curSelectedNote = Wrap(_curSelectedNote + 1, 0, 3);
            if (upPressed) _curSelectedMode = Wrap(_curSelectedMode - 1, 0, 2);
            if (downPressed) _curSelectedMode = Wrap(_curSelectedMode + 1, 0, 2);

            if (acceptPressed)
            {
                _onModeColumn = false;
                _selectedComponent = 0;
            }

            if (resetPressed)
            {
                ResetNoteColors(_curSelectedNote);
            }
        }
        else
        {
            if (upPressed) _selectedComponent = Wrap(_selectedComponent - 1, 0, 2);
            if (downPressed) _selectedComponent = Wrap(_selectedComponent + 1, 0, 2);

            int change = 0;
            if (leftPressed || rightPressed)
            {
                change = leftPressed ? -1 : 1;
                bool shiftHeld = kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift);
                if (shiftHeld) change *= 10;
                _holdTime = 0;
            }
            else if (leftHeld || rightHeld)
            {
                _holdTime += elapsed;
                if (_holdTime > 0.5f)
                    change = (int)(100 * elapsed * (leftHeld ? -1 : 1));
            }
            else if (leftReleased || rightReleased)
            {
                _holdTime = 0;
            }

            if (change != 0) ApplyColorChange(change);
            if (resetPressed) ResetNoteColors(_curSelectedNote);
        }

        foreach (var note in _noteSprites) note.Update(elapsed);

        _prevKb = kb;
        _prevGp = gp;
    }

    private void ApplyColorChange(int change)
    {
        var dataArray = _onPixel ? ClientPrefs.Data.ArrowRGBPixel : ClientPrefs.Data.ArrowRGB;
        if (_curSelectedNote >= dataArray.Length || _curSelectedMode >= dataArray[_curSelectedNote].Length)
            return;

        uint color = dataArray[_curSelectedNote][_curSelectedMode];
        int a = (int)((color >> 24) & 0xFF);
        int r = (int)((color >> 16) & 0xFF);
        int g = (int)((color >> 8) & 0xFF);
        int b = (int)(color & 0xFF);

        switch (_selectedComponent)
        {
            case 0: r = Math.Clamp(r + change, 0, 255); break;
            case 1: g = Math.Clamp(g + change, 0, 255); break;
            case 2: b = Math.Clamp(b + change, 0, 255); break;
        }

        dataArray[_curSelectedNote][_curSelectedMode] = (uint)((a << 24) | (r << 16) | (g << 8) | b);
    }

    private void ResetNoteColors(int noteIdx)
    {
        uint[][] defaultRGB = {
            new uint[] { 0xFFC24B99, 0xFFFFFFFF, 0xFF3C1F56 },
            new uint[] { 0xFF00FFFF, 0xFFFFFFFF, 0xFF1542B7 },
            new uint[] { 0xFF12FA05, 0xFFFFFFFF, 0xFF0A4447 },
            new uint[] { 0xFFF9393F, 0xFFFFFFFF, 0xFF651038 }
        };
        uint[][] defaultPixel = {
            new uint[] { 0xFFE276FF, 0xFFFFF9FF, 0xFF60008D },
            new uint[] { 0xFF3DCAFF, 0xFFF4FFFF, 0xFF003060 },
            new uint[] { 0xFF71E300, 0xFFF6FFE6, 0xFF003100 },
            new uint[] { 0xFFFF884E, 0xFFFFFAF5, 0xFF6C0000 }
        };

        if (noteIdx >= 0 && noteIdx < 4)
        {
            var src = !_onPixel ? defaultRGB[noteIdx] : defaultPixel[noteIdx];
            var dst = !_onPixel ? ClientPrefs.Data.ArrowRGB[noteIdx] : ClientPrefs.Data.ArrowRGBPixel[noteIdx];
            for (int i = 0; i < 3 && i < src.Length && i < dst.Length; i++)
                dst[i] = src[i];
        }
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        base.Draw(spriteBatch);
        var viewport = FNFGame.Instance.GraphicsDevice.Viewport;

        if (_bgTexture != null)
            spriteBatch.Draw(_bgTexture, new Rectangle(0, 0, viewport.Width, viewport.Height), new Color(0x2D, 0x2D, 0x52));

        var dataArray = _onPixel ? ClientPrefs.Data.ArrowRGBPixel : ClientPrefs.Data.ArrowRGB;
        int noteSpacing = 110;
        int startX = viewport.Width / 2 - (noteSpacing * 2) + 55;
        int noteY = 100;

        // Render Native C# Notes
        for (int i = 0; i < 4; i++)
        {
            int nx = startX + i * noteSpacing;
            float alpha = (i == _curSelectedNote) ? 1f : 0.5f;

            var note = _noteSprites[i];
            note.Position = new Vector2(nx, noteY);
            
            // Apply native RGB tint for preview
            uint c = dataArray[i][0];
            note.Tint = new Color((int)((c >> 16) & 0xFF), (int)((c >> 8) & 0xFF), (int)(c & 0xFF)) * alpha;
            note.Draw(spriteBatch);
        }

        int modeStartY = 240;
        for (int m = 0; m < 3; m++)
        {
            int my = modeStartY + m * 90;
            float alpha = (m == _curSelectedMode && _onModeColumn) ? 1f : 0.5f;
            if (!_onModeColumn) alpha = (m == _curSelectedMode) ? 0.8f : 0.3f;

            if (_curSelectedNote < dataArray.Length && m < dataArray[_curSelectedNote].Length)
            {
                uint argb = dataArray[_curSelectedNote][m];
                var col = new Color((int)((argb >> 16) & 0xFF), (int)((argb >> 8) & 0xFF), (int)(argb & 0xFF));
                spriteBatch.Draw(_solidTex, new Rectangle(viewport.Width / 2 - 200, my, 80, 80), col * alpha);
            }
        }

        if (!_onModeColumn && _curSelectedNote < dataArray.Length && _curSelectedMode < dataArray[_curSelectedNote].Length)
        {
            uint argb = dataArray[_curSelectedNote][_curSelectedMode];
            int r = (int)((argb >> 16) & 0xFF);
            int g = (int)((argb >> 8) & 0xFF);
            int b = (int)(argb & 0xFF);

            int editX = viewport.Width / 2 + 50;
            int editY = 240;
            int[] values = { r, g, b };
            Color[] barColors = { Color.Red, Color.Green, Color.Blue };

            for (int c = 0; c < 3; c++)
            {
                int cy = editY + c * 90;
                float alpha = (c == _selectedComponent) ? 1f : 0.5f;

                spriteBatch.Draw(_solidTex, new Rectangle(editX + 40, cy + 15, 256, 40), Color.DarkGray * alpha);
                spriteBatch.Draw(_solidTex, new Rectangle(editX + 40, cy + 15, values[c], 40), barColors[c] * alpha);
            }

            var previewCol = new Color(r, g, b);
            spriteBatch.Draw(_solidTex, new Rectangle(editX + 40, editY + 280, 100, 100), previewCol);
        }
    }

    private static int Wrap(int value, int min, int max)
    {
        int range = max - min + 1;
        if (range <= 0) return min;
        return ((value - min) % range + range) % range + min;
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
