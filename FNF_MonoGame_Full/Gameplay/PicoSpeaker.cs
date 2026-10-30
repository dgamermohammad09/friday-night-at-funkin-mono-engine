using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;
using System.Collections.Generic;
using System.Linq;

namespace FNF_MonoGame.Gameplay;

/// <summary>
/// Dedicated character class for Pico Speaker (Stress song animate atlas character).
/// Separated from generic Character.cs to properly handle Animate Atlas rendering
/// and positioning exactly like original HaxeFlixel AnimateAtlasCharacter + BaseCharacter.
/// </summary>
public class PicoSpeaker : Character
{
    public PicoSpeaker(string name, float x, float y) : base(name, x, y)
    {
    }

    /// <summary>
    /// Original HaxeFlixel BaseCharacter.hx setScale logic:
    ///   var feetPos = feetPosition;
    ///   this.scale.x = scale; this.scale.y = scale;
    ///   this.updateHitbox();
    ///   this.x = feetPos.x - characterOrigin.x + globalOffsets[0];
    ///   this.y = feetPos.y - characterOrigin.y + globalOffsets[1];
    /// </summary>
    public override Vector2 GetCharacterOrigin()
    {
        float w = GetIdleFrameWidth() * Scale;
        float h = GetIdleFrameHeight() * Scale;
        return new Vector2(w / 2f, h);
    }

    public override Vector2 GetMidpoint()
    {
        float halfW = GetIdleFrameWidth() * Scale / 2f;
        float halfH = GetIdleFrameHeight() * Scale / 2f;
        return new Vector2(Position.X + halfW, Position.Y + halfH);
    }

    public override void ApplyStagePosition(float[] stagePos)
    {
        if (stagePos != null && stagePos.Length >= 2 && _sprite?.Sheet != null)
        {
            string text = ResolveAnimName("idle");
            if (text != null && _currentAnimation != text)
            {
                PlayAnimation(text, force: true);
            }
            _sprite.Sheet.AnchorCompositeTo(_sprite.ResolvedAnimName); // anchor to idle, as original updateHitbox()

            float idleFrameWidth = GetIdleFrameWidth();
            float idleFrameHeight = GetIdleFrameHeight();
            float num = idleFrameWidth * Scale / 2f;
            float num2 = idleFrameHeight * Scale;
            float[] charOffsets = CharOffsets;
            float num3 = ((charOffsets != null && charOffsets.Length >= 2) ? CharOffsets[0] : 0f);
            float[] charOffsets2 = CharOffsets;
            float num4 = ((charOffsets2 != null && charOffsets2.Length >= 2) ? CharOffsets[1] : 0f);
            Position = new Vector2(stagePos[0] - num + num3, stagePos[1] - num2 + num4);
        }
    }

    private float GetIdleFrameWidth()
    {
        if (GetIdleTimelineSize() is { } ts) return ts.W; // flixel-animate timeline bounds
        if (_sprite?.Sheet == null) return 0f;
        if (_sprite.Sheet.CompositeLogicalSizes.Count > 0)
        {
            string idleName = ResolveAnimName("idle");
            if (idleName != null && _sprite.Sheet.CompositeLogicalSizes.TryGetValue(idleName, out var idleSize))
                return idleSize.Width;
            foreach (var kvp in _sprite.Sheet.CompositeLogicalSizes)
                return kvp.Value.Width;
        }
        var rawSize = GetIdleRawSize();
        if (rawSize.Width > 0) return rawSize.Width;
        return _sprite.GetFrameWidth();
    }

    private float GetIdleFrameHeight()
    {
        if (GetIdleTimelineSize() is { } ts) return ts.H; // flixel-animate timeline bounds
        if (_sprite?.Sheet == null) return 0f;
        if (_sprite.Sheet.CompositeLogicalSizes.Count > 0)
        {
            string idleName = ResolveAnimName("idle");
            if (idleName != null && _sprite.Sheet.CompositeLogicalSizes.TryGetValue(idleName, out var idleSize))
                return idleSize.Height;
            foreach (var kvp in _sprite.Sheet.CompositeLogicalSizes)
                return kvp.Value.Height;
        }
        var rawSize = GetIdleRawSize();
        if (rawSize.Height > 0) return rawSize.Height;
        return _sprite.GetFrameHeight();
    }

    private (int Width, int Height) GetIdleRawSize()
    {
        if (_sprite?.Sheet == null || _sprite.Sheet.RawCompositeData.Count == 0)
            return (0, 0);
        foreach (string name in new[] { "idle", "Idle", "pico all" })
        {
            string resolved = ResolveAnimName(name);
            if (resolved != null && _sprite.Sheet.RawCompositeData.TryGetValue(resolved, out var rawList) && rawList.Count > 0)
                return AnimatedSprite.ComputeRawBoundingBox(rawList[0]);
        }
        foreach (var kvp in _sprite.Sheet.RawCompositeData)
        {
            if (kvp.Value.Count > 0)
                return AnimatedSprite.ComputeRawBoundingBox(kvp.Value[0]);
        }
        return (0, 0);
    }
}
