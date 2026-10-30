using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine.PsychEngine.Objects;
using FNF_MonoGame.Gameplay; 

namespace FNF_MonoGame.Engine.PsychEngine.Options;

/// <summary>
/// Psych Engine note offset and combo position calibration state (100% Native C#).
/// Two modes: combo offset (drag rating/combo numbers) and note delay (slider ±500ms).
/// </summary>
public class NoteOffsetState : MusicBeatSubstate
{
    private bool _onComboMenu = true;

    // Note delay
    private float _barPercent;
    private const int DelayMin = -500;
    private const int DelayMax = 500;
    private float _holdTime;

    // Rating/combo display positions  
    private Vector2 _ratingPos;
    private Vector2 _comboNumsPos;

    // Native MonoGame Visuals
    private Texture2D _bgTexture;
    private Texture2D _solidTex;
    private Alphabet _beatText;
    private float _beatAlpha;
    private float _beatY;
    private SpriteSheet _ratingSheet;
    private SpriteSheet _numSheet;

    // Input
    private KeyboardState _prevKb;
    private GamePadState _prevGp;

    public NoteOffsetState()
    {
        Title = "Note Offset";
        RpcTitle = "Delay/Combo Offset Menu";
    }

    public string Title { get; set; }
    public string RpcTitle { get; set; }

    public override void Create()
    {
        base.Create();

#if !XBOX_UWP
        DiscordClient.ChangePresence("Delay/Combo Offset Menu");
#endif

        _solidTex = CreateSolidTexture(Color.White, 1, 1);

        string path = PsychPaths.Image("menuDesat");
        if (!string.IsNullOrEmpty(path))
        {
            string basePath = System.IO.Path.ChangeExtension(path, null);
            var sheet = SpriteSheet.Load(FNFGame.Instance, basePath);
            _bgTexture = sheet?.Texture;
        }
        
        string ratingPath = PsychPaths.Image("sick");
        if (string.IsNullOrEmpty(ratingPath)) ratingPath = PsychPaths.Image("pixelUI/sick-pixel");
        if (!string.IsNullOrEmpty(ratingPath))
        {
             _ratingSheet = SpriteSheet.Load(FNFGame.Instance, System.IO.Path.ChangeExtension(ratingPath, null));
        }

        string numPath = PsychPaths.Image("num0");
        if (string.IsNullOrEmpty(numPath)) numPath = PsychPaths.Image("pixelUI/num0-pixel");
        if (!string.IsNullOrEmpty(numPath))
        {
             _numSheet = SpriteSheet.Load(FNFGame.Instance, System.IO.Path.ChangeExtension(numPath, null));
        }

        _barPercent = ClientPrefs.Data.NoteOffset;
        UpdateNoteDelay();
        RepositionCombo();

        _beatText = new Alphabet(260, 320, Language.GetPhrase("delay_beat_hit", "Beat Hit!"), true);
        _beatAlpha = 0;

        _prevKb = Keyboard.GetState();
        _prevGp = GamePad.GetState(PlayerIndex.One);
    }

    public override void Update(float elapsed)
    {
        base.Update(elapsed);

        var kb = Keyboard.GetState();
        var gp = GamePad.GetState(PlayerIndex.One);

        int addNum = 1;
        bool shiftHeld = kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift) ||
                         gp.Buttons.LeftShoulder == ButtonState.Pressed;
        if (shiftHeld) addNum = _onComboMenu ? 10 : 3;

        bool leftPressed = (kb.IsKeyDown(Keys.Left) && !_prevKb.IsKeyDown(Keys.Left)) ||
                           (gp.DPad.Left == ButtonState.Pressed && _prevGp.DPad.Left != ButtonState.Pressed);
        bool rightPressed = (kb.IsKeyDown(Keys.Right) && !_prevKb.IsKeyDown(Keys.Right)) ||
                            (gp.DPad.Right == ButtonState.Pressed && _prevGp.DPad.Right != ButtonState.Pressed);
        bool upPressed = (kb.IsKeyDown(Keys.Up) && !_prevKb.IsKeyDown(Keys.Up)) ||
                         (gp.DPad.Up == ButtonState.Pressed && _prevGp.DPad.Up != ButtonState.Pressed);
        bool downPressed = (kb.IsKeyDown(Keys.Down) && !_prevKb.IsKeyDown(Keys.Down)) ||
                           (gp.DPad.Down == ButtonState.Pressed && _prevGp.DPad.Down != ButtonState.Pressed);
        bool leftHeld = kb.IsKeyDown(Keys.Left) || gp.DPad.Left == ButtonState.Pressed;
        bool rightHeld = kb.IsKeyDown(Keys.Right) || gp.DPad.Right == ButtonState.Pressed;
        bool leftReleased = (!kb.IsKeyDown(Keys.Left) && _prevKb.IsKeyDown(Keys.Left));
        bool rightReleased = (!kb.IsKeyDown(Keys.Right) && _prevKb.IsKeyDown(Keys.Right));
        bool acceptPressed = (kb.IsKeyDown(Keys.Enter) && !_prevKb.IsKeyDown(Keys.Enter)) ||
                             (gp.Buttons.Start == ButtonState.Pressed && _prevGp.Buttons.Start != ButtonState.Pressed);
        bool backPressed = (kb.IsKeyDown(Keys.Escape) && !_prevKb.IsKeyDown(Keys.Escape)) ||
                           (gp.Buttons.B == ButtonState.Pressed && _prevGp.Buttons.B != ButtonState.Pressed);
        bool resetPressed = (kb.IsKeyDown(Keys.R) && !_prevKb.IsKeyDown(Keys.R));

        if (_onComboMenu)
        {
            if (leftPressed) { ClientPrefs.Data.ComboOffset[0] -= addNum; RepositionCombo(); }
            if (rightPressed) { ClientPrefs.Data.ComboOffset[0] += addNum; RepositionCombo(); }
            if (upPressed) { ClientPrefs.Data.ComboOffset[1] += addNum; RepositionCombo(); }
            if (downPressed) { ClientPrefs.Data.ComboOffset[1] -= addNum; RepositionCombo(); }

            if (kb.IsKeyDown(Keys.A) && !_prevKb.IsKeyDown(Keys.A)) { ClientPrefs.Data.ComboOffset[2] -= addNum; RepositionCombo(); }
            if (kb.IsKeyDown(Keys.D) && !_prevKb.IsKeyDown(Keys.D)) { ClientPrefs.Data.ComboOffset[2] += addNum; RepositionCombo(); }
            if (kb.IsKeyDown(Keys.W) && !_prevKb.IsKeyDown(Keys.W)) { ClientPrefs.Data.ComboOffset[3] += addNum; RepositionCombo(); }
            if (kb.IsKeyDown(Keys.S) && !_prevKb.IsKeyDown(Keys.S)) { ClientPrefs.Data.ComboOffset[3] -= addNum; RepositionCombo(); }

            if (resetPressed)
            {
                for (int i = 0; i < ClientPrefs.Data.ComboOffset.Length; i++)
                    ClientPrefs.Data.ComboOffset[i] = 0;
                RepositionCombo();
            }
        }
        else
        {
            if (leftPressed)
            {
                _barPercent = Math.Max(DelayMin, Math.Min(ClientPrefs.Data.NoteOffset - 1, DelayMax));
                UpdateNoteDelay();
            }
            else if (rightPressed)
            {
                _barPercent = Math.Max(DelayMin, Math.Min(ClientPrefs.Data.NoteOffset + 1, DelayMax));
                UpdateNoteDelay();
            }

            int mult = 1;
            if (leftHeld || rightHeld)
            {
                _holdTime += elapsed;
                if (leftHeld) mult = -1;
            }
            if (leftReleased || rightReleased) _holdTime = 0;

            if (_holdTime > 0.5f)
            {
                _barPercent += 100 * addNum * elapsed * mult;
                _barPercent = Math.Max(DelayMin, Math.Min(_barPercent, DelayMax));
                UpdateNoteDelay();
            }

            if (resetPressed)
            {
                _holdTime = 0;
                _barPercent = 0;
                UpdateNoteDelay();
            }
        }

        if (acceptPressed)
        {
            _onComboMenu = !_onComboMenu;
        }

        if (backPressed)
        {
            ClientPrefs.Save();
            Close();
            _prevKb = kb;
            _prevGp = gp;
            return;
        }

        if (_beatAlpha > 0)
        {
            _beatAlpha -= elapsed;
            _beatY -= 150 * elapsed;
        }

        _prevKb = kb;
        _prevGp = gp;
    }

    protected override void OnBeatHit()
    {
        base.OnBeatHit();
        if (!_onComboMenu && CurBeat % 2 == 0)
        {
            _beatAlpha = 1f;
            _beatY = 320;
            // Native Audio beat tick could be played here
        }
    }

    private void RepositionCombo()
    {
        var viewport = FNFGame.Instance.GraphicsDevice.Viewport;
        float cx = viewport.Width * 0.35f;

        _ratingPos = new Vector2(cx - 40 + ClientPrefs.Data.ComboOffset[0],
                                 viewport.Height / 2f - 60 - ClientPrefs.Data.ComboOffset[1]);
        _comboNumsPos = new Vector2(cx - 90 + ClientPrefs.Data.ComboOffset[2],
                                     viewport.Height / 2f + 80 - ClientPrefs.Data.ComboOffset[3]);
    }

    private void UpdateNoteDelay()
    {
        ClientPrefs.Data.NoteOffset = (int)Math.Round(_barPercent);
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        base.Draw(spriteBatch);

        var viewport = FNFGame.Instance.GraphicsDevice.Viewport;

        if (_bgTexture != null)
            spriteBatch.Draw(_bgTexture, new Rectangle(0, 0, viewport.Width, viewport.Height), new Color(0x2D, 0x2D, 0x52));

        if (_onComboMenu)
        {
            // Native render for "sick"
            if (_ratingSheet?.Texture != null)
                spriteBatch.Draw(_ratingSheet.Texture, _ratingPos, Color.White);
            else
                spriteBatch.Draw(_solidTex, new Rectangle((int)_ratingPos.X, (int)_ratingPos.Y, 180, 60), new Color(0x00, 0xFF, 0x00) * 0.7f);

            // Native render for combo number "0"
            if (_numSheet?.Texture != null)
                spriteBatch.Draw(_numSheet.Texture, _comboNumsPos, Color.White);
            else
                spriteBatch.Draw(_solidTex, new Rectangle((int)_comboNumsPos.X, (int)_comboNumsPos.Y, 130, 50), new Color(0xFF, 0xFF, 0x00) * 0.7f);
        }
        else
        {
            int barW = viewport.Width - 200;
            int barH = 20;
            int barX = 100;
            int barY = viewport.Height / 2 + 80;

            spriteBatch.Draw(_solidTex, new Rectangle(barX, barY, barW, barH), Color.DarkGray * 0.8f);

            float pct = (_barPercent - DelayMin) / (float)(DelayMax - DelayMin);
            int fillW = (int)(barW * pct);
            spriteBatch.Draw(_solidTex, new Rectangle(barX, barY, fillW, barH), Color.Lime);

            if (_beatAlpha > 0 && _beatText != null)
            {
                _beatText.Alpha = Math.Max(0, _beatAlpha);
                _beatText.Y = _beatY;
                _beatText.Draw(spriteBatch);
            }
        }

        spriteBatch.Draw(_solidTex, new Rectangle(0, 0, viewport.Width, 40), Color.Black * 0.6f);
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
