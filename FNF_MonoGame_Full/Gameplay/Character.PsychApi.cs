using System;
using System.Globalization;
using Microsoft.Xna.Framework;

namespace FNF_MonoGame.Gameplay;

/// <summary>Psych script API helpers on characters (FlxSprite.setGraphicSize / frames[0] size) and objects/Character.hx
/// public fields read by scripts (healthIcon, healthColorArray, danceEveryNumBeats, isPlayer, ...).</summary>
public partial class Character
{
    /// <summary>flixel frameWidth/frameHeight of the first atlas frame (Psych Character width/height at scale 1).</summary>
    public Vector2 FirstFrameSize()
    {
        var ff = _sprite?.Sheet?.FirstFrame;
        if (ff != null)
            return new Vector2(ff.FrameWidth > 0 ? ff.FrameWidth : ff.SourceRect.Width, ff.FrameHeight > 0 ? ff.FrameHeight : ff.SourceRect.Height);
        return new Vector2(GetFrameWidth(), GetFrameHeight());
    }

    /// <summary>setGraphicSize on a character: the port keeps one uniform scale (x is used).</summary>
    public void SetPsychScale(float sx, float sy) => Scale = sx;

    public string healthIcon { get => JsonData?.PsychHealthIcon ?? OriginalName ?? Name; set { if (JsonData != null) JsonData.PsychHealthIcon = value; } }
    public int[] healthColorArray
    {
        get
        {
            string h = JsonData?.HealthBarColor;
            if (!string.IsNullOrEmpty(h) && h.StartsWith("#") && h.Length >= 7 && int.TryParse(h.Substring(1, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
                return new[] { (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255 };
            return new[] { 255, 0, 0 };
        }
        set { if (value != null && value.Length >= 3 && JsonData != null) JsonData.HealthBarColor = $"#{value[0]:X2}{value[1]:X2}{value[2]:X2}"; }
    }
    /// <summary>Character.recalculateDanceIdle: danceLeft+danceRight -> 1 beat, otherwise 2.</summary>
    private int? _psDanceEvery;
    public bool danceIdle => idleSuffix == "" ? _hasDualDance : (ResolveAnimName("danceLeft" + idleSuffix) != null && ResolveAnimName("danceRight" + idleSuffix) != null);
    public int danceEveryNumBeats { get => _psDanceEvery ?? (danceIdle ? 1 : 2); set { _psDanceEvery = value; _danceInterval = Math.Max(1, value); } }
    /// <summary>PlayState.characterBopper: gf dances every round(gfSpeed * danceEveryNumBeats) beats.</summary>
    public void SetPsychDanceInterval(int beats) => _danceInterval = Math.Max(1, beats);
    public string idleSuffix { get; set; } = "";
    public bool isPlayer => FNF_MonoGame.Scenes.PlayScene.Current != null && ReferenceEquals(FNF_MonoGame.Scenes.PlayScene.Current.boyfriend, this);
    public bool stunned { get; set; }
    /// <summary>Character.addOffset(name, x, y): animOffsets.set(name, [x, y]).</summary>
    public void addOffset(string name, float x = 0, float y = 0)
    {
        if (string.IsNullOrEmpty(name)) return;
        _jsonAnimOffsets ??= new Dictionary<string, Microsoft.Xna.Framework.Vector2>();
        _jsonAnimOffsets[name] = new Microsoft.Xna.Framework.Vector2(x, y);
    }
    /// <summary>Character.specialAnim: the current anim blocks dance() until it finishes (port: AnimLocked).</summary>
    public bool specialAnim { get => AnimLocked; set => AnimLocked = value; }
    public bool skipDance { get; set; }
    public bool hasMissAnimations => HasAnimation("singLEFTmiss") || HasAnimation("singDOWNmiss") || HasAnimation("singUPmiss") || HasAnimation("singRIGHTmiss");
    public string imageFile { get; set; }
    public float jsonScale => JsonData?.Scale ?? 1f;
    public string vocalsFile { get; set; }
    public bool missingCharacter { get; set; }
    public bool debugMode { get; set; }
}
