using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Gameplay;

/// <summary>
/// Dedicated character class for standard Girlfriend (GF) characters.
/// Handles GF-specific dual-dance logic and proper positioning.
/// </summary>
public class Girlfriend : Character
{
    public Girlfriend(string name, float x, float y) : base(name, x, y)
    {
    }

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
        if (stagePos == null || stagePos.Length < 2) return;
        if (_sprite?.Sheet == null) return;

        // Force starting animation (idle or danceRight) for correct bounds
        string startAnim = JsonData?.StartingAnimation;
        var resolvedStart = startAnim != null ? ResolveAnimName(startAnim) : null;
        if (resolvedStart != null && _currentAnimation != resolvedStart)
        {
            PlayAnimation(startAnim, true);
        }
        else
        {
            var idleName = ResolveAnimName("idle");
            if (idleName != null && _currentAnimation != idleName)
                PlayAnimation(idleName, true);
        }

        // Reuse the idle-anchored, Psych/base-aware logic in the base class.
        base.ApplyStagePosition(stagePos);
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
        foreach (string name in new[] { "idle", "Idle", "GF Dancing Beat", "speakers" })
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
