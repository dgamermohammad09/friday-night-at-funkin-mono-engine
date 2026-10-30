using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine.PsychEngine.Objects;

namespace FNF_MonoGame.Engine.PsychEngine.Options;

/// <summary>
/// Psych Engine base options menu substate.
/// Original: options/BaseOptionsMenu.hx
/// </summary>
public class BaseOptionsMenu : MusicBeatSubstate
{
    protected Option CurOption => _curSelected >= 0 && _curSelected < _optionsArray.Count ? _optionsArray[_curSelected] : null;
    protected int _curSelected;
    protected readonly List<Option> _optionsArray = new();
    protected readonly List<Alphabet> _grpOptions = new();
    protected readonly List<CheckboxThingie> _checkboxGroup = new();
    protected readonly List<AttachedText> _grpTexts = new();

    public string Title { get; set; } = "Options";
    public string RpcTitle { get; set; } = "Options Menu";

    public Texture2D BgTexture { get; private set; }
    public Texture2D DescBoxTexture { get; private set; }

    private int _nextAccept = 5;
    private float _holdTime;
    private float _holdValue;
    private bool _bindingKey;
    private float _holdingEsc;

    private string _descText = "";
    private Alphabet _titleText;

    // Input state
    private KeyboardState _prevKb;
    private GamePadState _prevGp;

    public Option AddOption(Option option)
    {
        _optionsArray.Add(option);
        return option;
    }

    public override void Create()
    {
        base.Create();

#if !XBOX_UWP
        DiscordClient.ChangePresence(RpcTitle);
#endif

        string path = PsychPaths.Image("menuDesat");
        if (!string.IsNullOrEmpty(path))
        {
            string basePath = System.IO.Path.ChangeExtension(path, null);
            var sheet = SpriteSheet.Load(FNFGame.Instance, basePath);
            BgTexture = sheet?.Texture;
        }

        DescBoxTexture = CreateSolidTexture(Color.Black, 1, 1);

        _titleText = new Alphabet(75, 45, Title, true);

        // Build option items
        for (int i = 0; i < _optionsArray.Count; i++)
        {
            var optionText = new Alphabet(220, 260, _optionsArray[i].Name, false);
            optionText.IsMenuItem = true;
            optionText.TargetY = i;
            _grpOptions.Add(optionText);

            if (_optionsArray[i].Type == OptionType.Bool)
            {
                var checkbox = new CheckboxThingie(optionText.X - 105, optionText.Y,
                    _optionsArray[i].GetValue()?.ToString()?.ToLower() == "true");
                _checkboxGroup.Add(checkbox);
            }
            else
            {
                var valueText = new AttachedText("" + _optionsArray[i].GetValue(), optionText.Width + 60);
                _grpTexts.Add(valueText);
                _optionsArray[i].Child = valueText;
            }
            UpdateTextFrom(_optionsArray[i]);
        }

        ChangeSelection(0);
        ReloadCheckboxes();
        _prevKb = Keyboard.GetState();
        _prevGp = GamePad.GetState(PlayerIndex.One);
    }

    public override void Update(float elapsed)
    {
        base.Update(elapsed);

        var kb = Keyboard.GetState();
        var gp = GamePad.GetState(PlayerIndex.One);

        if (_bindingKey)
        {
            BindingKeyUpdate(elapsed, kb, gp);
            _prevKb = kb;
            _prevGp = gp;
            return;
        }

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
        bool leftReleased = (!kb.IsKeyDown(Keys.Left) && _prevKb.IsKeyDown(Keys.Left)) ||
                            (gp.DPad.Left != ButtonState.Pressed && _prevGp.DPad.Left == ButtonState.Pressed);
        bool rightReleased = (!kb.IsKeyDown(Keys.Right) && _prevKb.IsKeyDown(Keys.Right)) ||
                             (gp.DPad.Right != ButtonState.Pressed && _prevGp.DPad.Right == ButtonState.Pressed);
        bool acceptPressed = (kb.IsKeyDown(Keys.Enter) && !_prevKb.IsKeyDown(Keys.Enter)) ||
                             (gp.Buttons.A == ButtonState.Pressed && _prevGp.Buttons.A != ButtonState.Pressed);
        bool backPressed = (kb.IsKeyDown(Keys.Escape) && !_prevKb.IsKeyDown(Keys.Escape)) ||
                           (gp.Buttons.B == ButtonState.Pressed && _prevGp.Buttons.B != ButtonState.Pressed);
        bool resetPressed = (kb.IsKeyDown(Keys.R) && !_prevKb.IsKeyDown(Keys.R));

        if (upPressed) ChangeSelection(-1);
        if (downPressed) ChangeSelection(1);

        if (backPressed)
        {
            Close();
            _prevKb = kb;
            _prevGp = gp;
            return;
        }

        if (_nextAccept <= 0 && CurOption != null)
        {
            switch (CurOption.Type)
            {
                case OptionType.Bool:
                    if (acceptPressed)
                    {
                        bool cur = CurOption.GetValue()?.ToString()?.ToLower() == "true";
                        CurOption.SetValue(!cur);
                        CurOption.Change();
                        ReloadCheckboxes();
                    }
                    break;

                case OptionType.Keybind:
                    if (acceptPressed)
                    {
                        _bindingKey = true;
                        _holdingEsc = 0;
                    }
                    break;

                default: // INT, FLOAT, PERCENT, STRING
                    if (leftHeld || rightHeld)
                    {
                        bool pressed = leftPressed || rightPressed;
                        if (_holdTime > 0.5f || pressed)
                        {
                            if (pressed)
                            {
                                if (CurOption.Type != OptionType.String)
                                {
                                    float add = leftPressed ? -ToFloat(CurOption.ChangeValue) : ToFloat(CurOption.ChangeValue);
                                    _holdValue = ToFloat(CurOption.GetValue()) + add;
                                    ClampHoldValue();

                                    if (CurOption.Type == OptionType.Int)
                                        CurOption.SetValue((int)Math.Round(_holdValue));
                                    else
                                        CurOption.SetValue((float)Math.Round(_holdValue, CurOption.Decimals));
                                }
                                else if (CurOption.Options != null && CurOption.Options.Length > 0)
                                {
                                    int num = CurOption.CurOption;
                                    if (leftPressed) num--;
                                    else num++;

                                    if (num < 0) num = CurOption.Options.Length - 1;
                                    else if (num >= CurOption.Options.Length) num = 0;

                                    CurOption.CurOption = num;
                                    CurOption.SetValue(CurOption.Options[num]);
                                }
                                UpdateTextFrom(CurOption);
                                CurOption.Change();
                            }
                            else if (CurOption.Type != OptionType.String)
                            {
                                _holdValue += CurOption.ScrollSpeed * elapsed * (leftHeld ? -1 : 1);
                                ClampHoldValue();

                                if (CurOption.Type == OptionType.Int)
                                    CurOption.SetValue((int)Math.Round(_holdValue));
                                else
                                    CurOption.SetValue((float)Math.Round(_holdValue, CurOption.Decimals));

                                UpdateTextFrom(CurOption);
                                CurOption.Change();
                            }
                        }

                        if (CurOption.Type != OptionType.String)
                            _holdTime += elapsed;
                    }
                    else if (leftReleased || rightReleased)
                    {
                        _holdTime = 0;
                    }
                    break;
            }

            if (resetPressed)
            {
                var opt = CurOption;
                if (opt.Type != OptionType.Keybind)
                {
                    opt.SetValue(opt.DefaultValue);
                    if (opt.Type != OptionType.Bool)
                    {
                        if (opt.Type == OptionType.String && opt.Options != null)
                            opt.CurOption = System.Array.IndexOf(opt.Options, opt.GetValue()?.ToString() ?? "");
                        UpdateTextFrom(opt);
                    }
                }
                else
                {
                    opt.SetValue(opt.DefaultKeys?.Keyboard ?? "NONE");
                    UpdateBind(null, opt);
                }
                opt.Change();
                ReloadCheckboxes();
            }
        }

        if (_nextAccept > 0) _nextAccept--;

        // Update option items
        foreach (var opt in _grpOptions) opt.Update(elapsed);
        foreach (var cb in _checkboxGroup) cb.Update(elapsed);
        foreach (var txt in _grpTexts) txt.Update(elapsed);

        _prevKb = kb;
        _prevGp = gp;
    }

    private void BindingKeyUpdate(float elapsed, KeyboardState kb, GamePadState gp)
    {
        if (kb.IsKeyDown(Keys.Escape) || gp.Buttons.B == ButtonState.Pressed)
        {
            _holdingEsc += elapsed;
            if (_holdingEsc > 0.5f)
            {
                _bindingKey = false;
            }
        }
        else if (kb.IsKeyDown(Keys.Back))
        {
            _holdingEsc += elapsed;
            if (_holdingEsc > 0.5f)
            {
                if (CurOption?.Keys != null) CurOption.Keys.Keyboard = "NONE";
                UpdateBind("NONE");
                _bindingKey = false;
            }
        }
        else
        {
            _holdingEsc = 0;
            // Check for any key press
            foreach (Keys key in Enum.GetValues(typeof(Keys)))
            {
                if (key == Keys.None || key == Keys.Escape || key == Keys.Back) continue;
                if (kb.IsKeyDown(key) && !_prevKb.IsKeyDown(key))
                {
                    string keyName = key.ToString();
                    if (CurOption?.Keys != null) CurOption.Keys.Keyboard = keyName;
                    CurOption?.SetValue(keyName);
                    UpdateBind(keyName);
                    _bindingKey = false;
                    break;
                }
            }
        }
    }

    private void UpdateBind(string text = null, Option option = null)
    {
        option ??= CurOption;
        if (text == null)
        {
            text = option?.GetValue()?.ToString() ?? "NONE";
        }
        if (option?.Child != null)
            option.Child.Text = text;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        base.Draw(spriteBatch);
        if (BgTexture != null)
        {
            var viewport = FNFGame.Instance.GraphicsDevice.Viewport;
            spriteBatch.Draw(BgTexture, new Rectangle(0, 0, viewport.Width, viewport.Height), new Color(0xEA, 0x71, 0xFD));
        }

        _titleText?.Draw(spriteBatch);
        foreach (var option in _grpOptions) option.Draw(spriteBatch);
        foreach (var checkbox in _checkboxGroup) checkbox.Draw(spriteBatch);
        foreach (var text in _grpTexts) text.Draw(spriteBatch);

        // Description box + text
        if (!string.IsNullOrEmpty(_descText) && DescBoxTexture != null)
        {
            var viewport = FNFGame.Instance.GraphicsDevice.Viewport;
            int boxY = viewport.Height - 100;
            spriteBatch.Draw(DescBoxTexture, new Rectangle(40, boxY - 10, viewport.Width - 80, 80), Color.Black * 0.6f);
            // Description text would be drawn with a SpriteFont here
        }
    }

    public virtual void ChangeSelection(int change)
    {
        if (_optionsArray.Count == 0) return;
        _curSelected += change;
        if (_curSelected < 0) _curSelected = _optionsArray.Count - 1;
        if (_curSelected >= _optionsArray.Count) _curSelected = 0;

        _descText = _optionsArray[_curSelected].Description;

        for (int i = 0; i < _grpOptions.Count; i++)
        {
            var item = _grpOptions[i];
            item.TargetY = i - _curSelected;
            item.Alpha = (item.TargetY == 0) ? 1f : 0.6f;
        }

        for (int i = 0; i < _grpTexts.Count; i++)
        {
            var text = _grpTexts[i];
            text.Alpha = (i == _curSelected) ? 1f : 0.6f;
        }
    }

    public void ReloadCheckboxes()
    {
        for (int i = 0; i < _checkboxGroup.Count; i++)
        {
            // Find the option index for this checkbox
            int optIdx = FindBoolOptionIndex(i);
            if (optIdx >= 0 && optIdx < _optionsArray.Count)
                _checkboxGroup[i].Checked = _optionsArray[optIdx].GetValue()?.ToString()?.ToLower() == "true";
        }
    }

    private int FindBoolOptionIndex(int checkboxIndex)
    {
        int count = 0;
        for (int i = 0; i < _optionsArray.Count; i++)
        {
            if (_optionsArray[i].Type == OptionType.Bool)
            {
                if (count == checkboxIndex) return i;
                count++;
            }
        }
        return -1;
    }

    public void UpdateTextFrom(Option option)
    {
        if (option == null) return;

        if (option.Type == OptionType.Keybind)
        {
            UpdateBind(null, option);
            return;
        }

        string text = option.DisplayFormat;
        object val = option.GetValue();
        if (option.Type == OptionType.Percent && val is float fv)
            val = (int)(fv * 100);
        object def = option.DefaultValue;
        text = text.Replace("%v", val?.ToString() ?? "").Replace("%d", def?.ToString() ?? "");
        option.Text = text;
    }

    private void ClampHoldValue()
    {
        if (CurOption.MinValue != null && _holdValue < ToFloat(CurOption.MinValue))
            _holdValue = ToFloat(CurOption.MinValue);
        if (CurOption.MaxValue != null && _holdValue > ToFloat(CurOption.MaxValue))
            _holdValue = ToFloat(CurOption.MaxValue);
    }

    private static float ToFloat(object val)
    {
        if (val is float f) return f;
        if (val is int i) return i;
        if (val is double d) return (float)d;
        if (float.TryParse(val?.ToString(), out float r)) return r;
        return 0f;
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
