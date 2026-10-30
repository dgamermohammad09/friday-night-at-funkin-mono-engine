using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine.PsychEngine.Objects;

namespace FNF_MonoGame.Engine.PsychEngine.Options;

/// <summary>
/// Psych Engine gameplay-changers substate (scroll speed, health, botplay, etc.).
/// Original: options/GameplayChangersSubstate.hx
/// Uses GameplayOption (maps to ClientPrefs.Data.gameplaySettings via property reflection).
/// </summary>
public class GameplayChangersSubstate : MusicBeatSubstate
{
    private int _curSelected;
    private readonly List<GameplayOption> _optionsArray = new();
    private readonly List<Alphabet> _grpOptions = new();
    private readonly List<CheckboxThingie> _checkboxGroup = new();
    private readonly List<AttachedText> _grpTexts = new();

    private GameplayOption CurOption => _curSelected >= 0 && _curSelected < _optionsArray.Count ? _optionsArray[_curSelected] : null;

    private int _nextAccept = 5;
    private float _holdTime;
    private float _holdValue;

    private Texture2D _bgTexture;

    // Input state
    private KeyboardState _prevKb;
    private GamePadState _prevGp;

    public GameplayChangersSubstate()
    {
        Title = "Gameplay Changers";
        RpcTitle = "Gameplay Changers Menu";
    }

    public string Title { get; set; }
    public string RpcTitle { get; set; }

    private void GetOptions()
    {
        var scrollType = new GameplayOption("Scroll Type", "scrolltype", OptionType.String, "multiplicative",
            new[] { "multiplicative", "constant" });
        _optionsArray.Add(scrollType);

        var scrollSpeed = new GameplayOption("Scroll Speed", "scrollspeed", OptionType.Float, 1f);
        scrollSpeed.ScrollSpeed = 2.0f;
        scrollSpeed.MinValue = 0.35f;
        scrollSpeed.ChangeValue = 0.05f;
        scrollSpeed.Decimals = 2;
        if ((string)scrollType.GetValue() != "constant")
        {
            scrollSpeed.DisplayFormat = "%vX";
            scrollSpeed.MaxValue = 3f;
        }
        else
        {
            scrollSpeed.DisplayFormat = "%v";
            scrollSpeed.MaxValue = 6f;
        }
        _optionsArray.Add(scrollSpeed);

        var playbackRate = new GameplayOption("Playback Rate", "songspeed", OptionType.Float, 1f);
        playbackRate.ScrollSpeed = 1f;
        playbackRate.MinValue = 0.5f;
        playbackRate.MaxValue = 3.0f;
        playbackRate.ChangeValue = 0.05f;
        playbackRate.DisplayFormat = "%vX";
        playbackRate.Decimals = 2;
        _optionsArray.Add(playbackRate);

        var healthGain = new GameplayOption("Health Gain Multiplier", "healthgain", OptionType.Float, 1f);
        healthGain.ScrollSpeed = 2.5f;
        healthGain.MinValue = 0f;
        healthGain.MaxValue = 5f;
        healthGain.ChangeValue = 0.1f;
        healthGain.DisplayFormat = "%vX";
        _optionsArray.Add(healthGain);

        var healthLoss = new GameplayOption("Health Loss Multiplier", "healthloss", OptionType.Float, 1f);
        healthLoss.ScrollSpeed = 2.5f;
        healthLoss.MinValue = 0.5f;
        healthLoss.MaxValue = 5f;
        healthLoss.ChangeValue = 0.1f;
        healthLoss.DisplayFormat = "%vX";
        _optionsArray.Add(healthLoss);

        _optionsArray.Add(new GameplayOption("Instakill on Miss", "instakill", OptionType.Bool, false));
        _optionsArray.Add(new GameplayOption("Practice Mode", "practice", OptionType.Bool, false));
        _optionsArray.Add(new GameplayOption("Botplay", "botplay", OptionType.Bool, false));
    }

    public GameplayOption GetOptionByName(string name)
    {
        foreach (var opt in _optionsArray)
            if (opt.Name == name) return opt;
        return null;
    }

    public override void Create()
    {
        base.Create();

        _bgTexture = CreateSolidTexture(Color.Black, 1, 1);
        GetOptions();

        for (int i = 0; i < _optionsArray.Count; i++)
        {
            var optionText = new Alphabet(150, 360, _optionsArray[i].Name, true);
            optionText.IsMenuItem = true;
            optionText.SetScale(0.8f);
            optionText.TargetY = i;
            _grpOptions.Add(optionText);

            if (_optionsArray[i].Type == OptionType.Bool)
            {
                optionText.X += 60;
                var checkbox = new CheckboxThingie(optionText.X - 105, optionText.Y,
                    _optionsArray[i].GetValue() is true);
                checkbox.ID = i;
                _checkboxGroup.Add(checkbox);
            }
            else
            {
                string valStr = _optionsArray[i].GetValue()?.ToString() ?? "";
                var valueText = new AttachedText(valStr, optionText.Width + 40);
                valueText.ID = i;
                _grpTexts.Add(valueText);
                _optionsArray[i].SetChild(valueText);
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
            ClientPrefs.Save();
            Close();
            _prevKb = kb;
            _prevGp = gp;
            return;
        }

        if (_nextAccept <= 0 && CurOption != null)
        {
            bool usesCheckbox = CurOption.Type == OptionType.Bool;
            if (usesCheckbox)
            {
                if (acceptPressed)
                {
                    CurOption.SetValue(!(CurOption.GetValue() is true));
                    CurOption.Change();
                    ReloadCheckboxes();
                }
            }
            else
            {
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

                                // Handle Scroll Type changing Scroll Speed format
                                if (CurOption.Name == "Scroll Type")
                                {
                                    var speedOpt = GetOptionByName("Scroll Speed");
                                    if (speedOpt != null)
                                    {
                                        if ((string)CurOption.GetValue() == "constant")
                                        {
                                            speedOpt.DisplayFormat = "%v";
                                            speedOpt.MaxValue = 6f;
                                        }
                                        else
                                        {
                                            speedOpt.DisplayFormat = "%vX";
                                            speedOpt.MaxValue = 3f;
                                            if (ToFloat(speedOpt.GetValue()) > 3)
                                                speedOpt.SetValue(3f);
                                        }
                                        UpdateTextFrom(speedOpt);
                                    }
                                }
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
            }

            if (resetPressed)
            {
                for (int i = 0; i < _optionsArray.Count; i++)
                {
                    var leOption = _optionsArray[i];
                    leOption.SetValue(leOption.DefaultValue);
                    if (leOption.Type != OptionType.Bool)
                    {
                        if (leOption.Type == OptionType.String && leOption.Options != null)
                            leOption.CurOption = System.Array.IndexOf(leOption.Options, leOption.GetValue()?.ToString() ?? "");
                        UpdateTextFrom(leOption);
                    }
                    if (leOption.Name == "Scroll Speed")
                    {
                        leOption.DisplayFormat = "%vX";
                        leOption.MaxValue = 3f;
                        if (ToFloat(leOption.GetValue()) > 3)
                            leOption.SetValue(3f);
                        UpdateTextFrom(leOption);
                    }
                    leOption.Change();
                }
                ReloadCheckboxes();
            }
        }

        if (_nextAccept > 0) _nextAccept--;

        foreach (var opt in _grpOptions) opt.Update(elapsed);
        foreach (var cb in _checkboxGroup) cb.Update(elapsed);
        foreach (var txt in _grpTexts) txt.Update(elapsed);

        _prevKb = kb;
        _prevGp = gp;
    }

    private void UpdateTextFrom(GameplayOption option)
    {
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

    private void ChangeSelection(int change = 0)
    {
        if (_optionsArray.Count == 0) return;
        _curSelected += change;
        if (_curSelected < 0) _curSelected = _optionsArray.Count - 1;
        if (_curSelected >= _optionsArray.Count) _curSelected = 0;

        for (int i = 0; i < _grpOptions.Count; i++)
        {
            var item = _grpOptions[i];
            item.TargetY = i - _curSelected;
            item.Alpha = (item.TargetY == 0) ? 1f : 0.6f;
        }
        foreach (var text in _grpTexts)
        {
            text.Alpha = (text.ID == _curSelected) ? 1f : 0.6f;
        }
    }

    private void ReloadCheckboxes()
    {
        foreach (var checkbox in _checkboxGroup)
        {
            if (checkbox.ID >= 0 && checkbox.ID < _optionsArray.Count)
                checkbox.Checked = _optionsArray[checkbox.ID].GetValue() is true;
        }
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        base.Draw(spriteBatch);

        var viewport = FNFGame.Instance.GraphicsDevice.Viewport;
        if (_bgTexture != null)
            spriteBatch.Draw(_bgTexture, new Rectangle(0, 0, viewport.Width, viewport.Height), Color.Black * 0.6f);

        foreach (var opt in _grpOptions) opt.Draw(spriteBatch);
        foreach (var cb in _checkboxGroup) cb.Draw(spriteBatch);
        foreach (var txt in _grpTexts) txt.Draw(spriteBatch);
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

/// <summary>
/// Gameplay option — stores values in ClientPrefs.Data via property name (gameplaySettings map equivalent).
/// Original: inner class GameplayOption in GameplayChangersSubstate.hx
/// </summary>
public class GameplayOption
{
    private AttachedText _child;
    private string _name;
    private string _text;

    public string Name
    {
        get => _name;
        set => _name = Language.GetPhrase($"setting_{value}", value);
    }

    public string Text
    {
        get => _text;
        set
        {
            _text = value;
            if (_child != null)
                _child.Text = Language.GetPhrase($"setting_{_rawName}-{_text}", _text);
        }
    }

    public Action OnChange { get; set; }
    public OptionType Type { get; set; } = OptionType.Bool;

    public float ScrollSpeed { get; set; } = 50f;
    public string Variable { get; set; }
    public object DefaultValue { get; set; }

    public int CurOption { get; set; }
    public string[] Options { get; set; }
    public object ChangeValue { get; set; } = 1;
    public object MinValue { get; set; }
    public object MaxValue { get; set; }
    public int Decimals { get; set; } = 1;
    public int ID { get; set; }

    public string DisplayFormat { get; set; } = "%v";

    private readonly string _rawName;

    // Property name mapping for ClientPrefs.Data gameplay settings
    private static readonly Dictionary<string, string> PropertyMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["scrolltype"] = "ScrollType",
        ["scrollspeed"] = "ScrollSpeed",
        ["songspeed"] = "SongSpeed",
        ["healthgain"] = "HealthGain",
        ["healthloss"] = "HealthLoss",
        ["instakill"] = "Instakill",
        ["practice"] = "Practice",
        ["botplay"] = "Botplay",
    };

    public GameplayOption(string name, string variable, OptionType type, object defaultValue, string[] options = null)
    {
        _rawName = name;
        _name = Language.GetPhrase($"setting_{name}", name);
        Variable = variable;
        Type = type;
        DefaultValue = defaultValue;
        Options = options;

        if (type == OptionType.Percent)
        {
            DisplayFormat = "%v%";
            ChangeValue = 0.01f;
            MinValue = 0f;
            MaxValue = 1f;
            ScrollSpeed = 0.5f;
            Decimals = 2;
        }

        if (GetValue() == null)
            SetValue(defaultValue);

        if (type == OptionType.String && Options != null)
        {
            string val = GetValue()?.ToString() ?? "";
            int idx = System.Array.IndexOf(Options, val);
            if (idx > -1) CurOption = idx;
        }
    }

    public void Change() => OnChange?.Invoke();

    public object GetValue()
    {
        string propName = PropertyMap.TryGetValue(Variable, out var p) ? p : Variable;
        var prop = typeof(SaveVariables).GetProperty(propName, 
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
        return prop?.GetValue(ClientPrefs.Data);
    }

    public void SetValue(object value)
    {
        string propName = PropertyMap.TryGetValue(Variable, out var p) ? p : Variable;
        var prop = typeof(SaveVariables).GetProperty(propName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
        if (prop != null)
        {
            try
            {
                object converted = Convert.ChangeType(value, prop.PropertyType);
                prop.SetValue(ClientPrefs.Data, converted);
            }
            catch
            {
                prop.SetValue(ClientPrefs.Data, value);
            }
        }
    }

    public void SetChild(AttachedText child) => _child = child;
}
