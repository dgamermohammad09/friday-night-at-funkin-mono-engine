using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Engine.PsychEngine.Objects;

/// <summary>
/// Psych Engine-style options checkbox sprite.
/// Original: objects/CheckboxThingie.hx
/// </summary>
public class CheckboxThingie
{
    public float X { get; set; }
    public float Y { get; set; }
    public bool Visible { get; set; } = true;
    public float Alpha { get; set; } = 1f;
    public int ID { get; set; }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            if (value)
            {
                if (_currentAnim != "checked" && _currentAnim != "checking")
                    PlayAnim("checking");
            }
            else
            {
                if (_currentAnim != "unchecked" && _currentAnim != "unchecking")
                    PlayAnim("unchecking");
            }
        }
    }

    public bool CopyAlpha { get; set; } = true;
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public object SpriteTracker { get; set; }

    public SpriteSheet Sheet { get; private set; }
    public AnimatedSprite AnimatedSprite { get; private set; }
    public string CurrentAnimation => _currentAnim;

    private bool _checked;
    private string _currentAnim = "";
    private Vector2 _offset = Vector2.Zero;

    public CheckboxThingie(float x = 0, float y = 0, bool checkedValue = false)
    {
        X = x;
        Y = y;
        _checked = checkedValue;
        Load();
        AnimationFinished(checkedValue ? "checking" : "unchecking");
    }

    private void Load()
    {
        string path = PsychPaths.Image("ui/checkbox");
        if (string.IsNullOrEmpty(path)) path = PsychPaths.Image("checkbox");
        if (string.IsNullOrEmpty(path)) path = PsychPaths.Image("checkboxanim");
        if (!string.IsNullOrEmpty(path))
        {
            string basePath = Path.ChangeExtension(path, null);
            Sheet = SpriteSheet.Load(FNFGame.Instance, basePath);
            AnimatedSprite = Sheet != null ? new AnimatedSprite { Sheet = Sheet } : null;
        }
    }

    private void PlayAnim(string name)
    {
        _currentAnim = name;
        AnimatedSprite?.PlayAnimation(name, true, false);
        SetAnimOffset(name);
    }

    private void AnimationFinished(string name)
    {
        switch (name)
        {
            case "checking":
                PlayAnim("checked");
                _offset = new Vector2(3, 12);
                break;
            case "unchecking":
                PlayAnim("unchecked");
                _offset = new Vector2(0, 2);
                break;
        }
    }

    private void SetAnimOffset(string name)
    {
        _offset = name switch
        {
            "checking" => new Vector2(34, 25),
            "unchecking" => new Vector2(25, 28),
            "checked" => new Vector2(3, 12),
            "unchecked" => new Vector2(0, 2),
            _ => Vector2.Zero
        };
    }

    public void Update(float elapsed)
    {
        AnimatedSprite?.Update(elapsed);

        if (AnimatedSprite != null && AnimatedSprite.Finished && (_currentAnim == "checking" || _currentAnim == "unchecking"))
        {
            AnimationFinished(_currentAnim);
        }

        if (SpriteTracker is BGSprite bg)
        {
            X = bg.X - 130 + OffsetX;
            Y = bg.Y + 30 + OffsetY;
            if (CopyAlpha) Alpha = bg.Alpha;
        }
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible || AnimatedSprite == null) return;
        AnimatedSprite.Position = new Vector2(X + _offset.X, Y + _offset.Y);
        AnimatedSprite.Tint = Color.White * Alpha;
        AnimatedSprite.Draw(spriteBatch);
    }
}
