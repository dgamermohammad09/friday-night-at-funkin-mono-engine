using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine.PsychEngine.Objects;

namespace FNF_MonoGame.Engine.PsychEngine.States;

/// <summary>
/// Psych Engine reset-score confirmation substate.
/// Original: substates/ResetScoreSubState.hx
/// </summary>
public class ResetScoreSubState : MusicBeatSubstate
{
    private readonly string _song;
    private readonly int _difficulty;
    private readonly int _week;
    private readonly string _character;

    private Texture2D _bgTexture;
    private float _bgAlpha;
    private bool _onYes;
    private readonly List<Alphabet> _alphabetArray = new();
    private HealthIcon _icon;
    private Alphabet _yesText;
    private Alphabet _noText;

    // Input
    private KeyboardState _prevKb;
    private GamePadState _prevGp;

    public ResetScoreSubState(string song, int difficulty, string character, int week = -1)
    {
        _song = song;
        _difficulty = difficulty;
        _week = week;
        _character = character;
    }

    public override void Create()
    {
        base.Create();
        _bgTexture = CreateSolidTexture(Color.Black, 1, 1);

        string name = _week > -1 ? WeekData.GetWeekName(_week) : _song;
        name += $" ({Difficulty.GetString(_difficulty)})?";

        var text = new Alphabet(0, 180, Language.GetPhrase("reset_score", "Reset the score of"), true);
        text.ScreenCenter(true, false);
        _alphabetArray.Add(text);

        var text2 = new Alphabet(0, text.Y + 90, name, true);
        text2.ScreenCenter(true, false);
        _alphabetArray.Add(text2);

        _yesText = new Alphabet(0, text2.Y + 150, Language.GetPhrase("Yes", "Yes"), true);
        _yesText.ScreenCenter(true, false);
        _yesText.X -= 200;
        _alphabetArray.Add(_yesText);

        _noText = new Alphabet(0, text2.Y + 150, Language.GetPhrase("No", "No"), true);
        _noText.ScreenCenter(true, false);
        _noText.X += 200;
        _alphabetArray.Add(_noText);

        _icon = new HealthIcon(_character);
        _icon.X = text2.X + text2.Width + 30;
        _icon.Y = text2.Y - 30;
        _icon.Alpha = 0;

        foreach (var letter in _alphabetArray)
            letter.Alpha = 0;

        UpdateOptions();

        _prevKb = Keyboard.GetState();
        _prevGp = GamePad.GetState(PlayerIndex.One);
    }

    public override void Update(float elapsed)
    {
        base.Update(elapsed);

        if (_bgAlpha < 0.6f) _bgAlpha += elapsed * 1.5f;
        foreach (var spr in _alphabetArray)
            spr.Alpha += elapsed * 2.5f;
        if (_icon != null)
            _icon.Alpha += elapsed * 2.5f;

        var kb = Keyboard.GetState();
        var gp = GamePad.GetState(PlayerIndex.One);

        bool left = (kb.IsKeyDown(Keys.Left) && !_prevKb.IsKeyDown(Keys.Left)) ||
                    (gp.DPad.Left == ButtonState.Pressed && _prevGp.DPad.Left != ButtonState.Pressed);
        bool right = (kb.IsKeyDown(Keys.Right) && !_prevKb.IsKeyDown(Keys.Right)) ||
                     (gp.DPad.Right == ButtonState.Pressed && _prevGp.DPad.Right != ButtonState.Pressed);
        bool accept = (kb.IsKeyDown(Keys.Enter) && !_prevKb.IsKeyDown(Keys.Enter)) ||
                      (gp.Buttons.A == ButtonState.Pressed && _prevGp.Buttons.A != ButtonState.Pressed);
        bool back = (kb.IsKeyDown(Keys.Escape) && !_prevKb.IsKeyDown(Keys.Escape)) ||
                    (gp.Buttons.B == ButtonState.Pressed && _prevGp.Buttons.B != ButtonState.Pressed);

        if (left || right)
        {
            _onYes = !_onYes;
            UpdateOptions();
        }

        if (back)
        {
            Close();
        }
        else if (accept)
        {
            if (_onYes)
            {
                if (_week == -1)
                    PsychHighscore.ResetSong(_song, _difficulty);
                else
                    PsychHighscore.ResetWeek(WeekData.GetWeekFile(_week), _difficulty);
            }
            Close();
        }

        _prevKb = kb;
        _prevGp = gp;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        base.Draw(spriteBatch);
        var viewport = FNFGame.Instance.GraphicsDevice.Viewport;
        if (_bgTexture != null)
            spriteBatch.Draw(_bgTexture, new Rectangle(0, 0, viewport.Width, viewport.Height), Color.White * _bgAlpha);
        foreach (var letter in _alphabetArray)
            letter.Draw(spriteBatch);
        _icon?.Draw(spriteBatch);
    }

    private void UpdateOptions()
    {
        float[] scales = { 0.75f, 1f };
        float[] alphas = { 0.6f, 1.25f };
        int confirmInt = _onYes ? 1 : 0;

        if (_yesText != null)
        {
            _yesText.Alpha = alphas[confirmInt];
            _yesText.SetScale(scales[confirmInt], scales[confirmInt]);
        }
        if (_noText != null)
        {
            _noText.Alpha = alphas[1 - confirmInt];
            _noText.SetScale(scales[1 - confirmInt], scales[1 - confirmInt]);
        }
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
