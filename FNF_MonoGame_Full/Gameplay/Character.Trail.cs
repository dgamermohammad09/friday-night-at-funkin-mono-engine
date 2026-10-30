using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Gameplay;

/// <summary>
/// flixel-addons FlxTrail / funkin.effects.FunkTrail (spirit.hxc: new FunkTrail(this, null, 4, 24, 0.3, 0.069), zIndex 190).
/// Every `delay` update frames the trail records the target's current frame + draw position (FunkTrail subtracts the
/// animation offset, i.e. it records exactly where the frame was drawn) and redraws its sprites: sprite i shows the
/// i-th most recent record with alpha (alpha - i * diff). The character never moves, so a ghost = the character drawn
/// with the recorded animation/frame/flip.
/// </summary>
public partial class Character
{
    internal float _trailGhostAlpha = -1f;

    private sealed class TrailRecord { public string Anim; public object SpriteState; public bool FlipX; }
    private List<TrailRecord> _trailRecords;
    private int _trailCounter, _trailLength, _trailDelay;
    private float _trailAlpha, _trailDiff;

    public bool HasTrail => _trailRecords != null;

    public void EnableTrail(int length, int delay, float alpha, float diff)
    {
        _trailRecords = new List<TrailRecord>(); _trailLength = length; _trailDelay = delay; _trailAlpha = alpha; _trailDiff = diff; _trailCounter = 0;
    }

    /// <summary>FlxTrail.update (once per game update frame).</summary>
    public void UpdateTrail()
    {
        if (_trailRecords == null || _sprite == null) return;
        _trailCounter++;
        if (_trailCounter < _trailDelay || _trailLength < 1) return;
        _trailCounter = 0;
        _trailRecords.Insert(0, new TrailRecord { Anim = _currentAnimation, SpriteState = _sprite.SaveAnimState(), FlipX = FlipX });
        if (_trailRecords.Count > _trailLength) _trailRecords.RemoveAt(_trailRecords.Count - 1);
    }

    /// <summary>Draw the trail sprites (oldest last in the group order = drawn on top; FlxGroup draws members in order 0..n).</summary>
    public void DrawTrail(SpriteBatch sb, AssetManager assets, float x, float y, float zoom)
    {
        if (_trailRecords == null || _trailRecords.Count == 0 || _sprite == null) return;
        string anim = _currentAnimation; var st = _sprite.SaveAnimState(); bool flip = FlipX;
        for (int i = 0; i < _trailRecords.Count; i++)
        {
            float a = _trailAlpha - i * _trailDiff;   // member alphas set once in increaseLength
            if (a <= 0f) continue;                      // trailSprite.kill()
            var r = _trailRecords[i];
            _currentAnimation = r.Anim; _sprite.RestoreAnimState(r.SpriteState); FlipX = r.FlipX;
            _trailGhostAlpha = a;
            Draw(sb, assets, x, y, zoom);
        }
        _trailGhostAlpha = -1f;
        _currentAnimation = anim; _sprite.RestoreAnimState(st); FlipX = flip;
    }
}
