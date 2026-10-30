using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine ModchartAnimateSprite — sprite used by Lua scripts for FlxAnimate objects.
/// Direct port of psychlua/ModchartAnimateSprite.hx.
/// </summary>
public class ModchartAnimateSprite : ModchartSprite
{
    private readonly Dictionary<string, Vector2> _animOffsets = new(System.StringComparer.OrdinalIgnoreCase);

    public ModchartAnimateSprite(float x = 0, float y = 0) : base(x, y)
    {
        Antialiasing = ClientPrefs.Data.Antialiasing;
    }

    public new void PlayAnim(string name, bool forced = false, bool reverse = false, int startFrame = 0)
    {
        base.PlayAnim(name, forced, reverse, startFrame);
        
        if (_animOffsets.TryGetValue(name, out var offset))
        {
            // Apply offset based on the FlxAnimate logic
            // Assuming base ModchartSprite handles _currentOffset in its Draw function.
            base.AddOffset(name, offset.X, offset.Y);
        }
    }

    public new void AddOffset(string name, float x, float y)
    {
        _animOffsets[name] = new Vector2(x, y);
        base.AddOffset(name, x, y);
    }
}
