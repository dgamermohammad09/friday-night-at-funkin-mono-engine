using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;
using MoonSharp.Interpreter;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine FlxAnimate Lua functions.
/// Direct port of psychlua/FlxAnimateFunctions.hx — creates and manipulates
/// FlxAnimate-style sprites from Lua scripts. On Xbox, this uses the same
/// MonoGame SpriteSheet/AnimatedSprite path as the rest of the engine.
/// </summary>
public static class FlxAnimateFunctions
{
    private static readonly Dictionary<string, ModchartSprite> _animateSprites = new(StringComparer.OrdinalIgnoreCase);

    public static void Implement(FunkinLua funk)
    {
        var lua = funk.LuaScript;
        var game = funk.GameScene;

        lua.Globals["makeFlxAnimateSprite"] = (Action<string, string, double, double, bool>)((tag, spritePath, x, y, loadAtlas) =>
        {
            if (string.IsNullOrEmpty(tag)) return;
            var sprite = new ModchartSprite((float)x, (float)y)
            {
                Texture = FNFGame.Instance?.Assets?.LoadTexture(spritePath)
            };
            _animateSprites[tag] = sprite;
        });

        lua.Globals["loadAnimateAtlas"] = (Func<string, string, bool>)((tag, atlas) =>
        {
            if (!_animateSprites.TryGetValue(tag, out var sprite))
            {
                Console.WriteLine($"[LUA] loadAnimateAtlas: sprite {tag} not found");
                return false;
            }
            try
            {
                var sheet = SpriteSheet.Load(FNFGame.Instance, $"images/{atlas}")
                         ?? SpriteSheet.Load(FNFGame.Instance, atlas);
                if (sheet == null)
                {
                    Console.WriteLine($"[LUA] loadAnimateAtlas: atlas not found: {atlas}");
                    return false;
                }
                sprite.SpriteSheet = sheet;
                sprite.Texture = sheet.Texture;
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LUA] loadAnimateAtlas error: {ex.Message}");
                return false;
            }
        });

        lua.Globals["addAnimationBySymbol"] = (Func<string, string, string, int, bool, bool>)((tag, name, symbol, framerate, loop) =>
        {
            if (!_animateSprites.TryGetValue(tag, out var sprite))
                return false;
            sprite.AddAnimationByPrefix(name, symbol, framerate, loop);
            if (sprite.CurrentAnimation == null)
                sprite.PlayAnim(name);
            return true;
        });

        lua.Globals["addAnimationBySymbolIndices"] = (Func<string, string, string, string, int, bool, bool>)((tag, name, symbol, indices, framerate, loop) =>
        {
            if (!_animateSprites.TryGetValue(tag, out var sprite))
                return false;
            sprite.AddAnimationByIndices(name, symbol, LuaUtils.StringToIntArray(indices), framerate, loop);
            if (sprite.CurrentAnimation == null)
                sprite.PlayAnim(name);
            return true;
        });

        lua.Globals["playAnimateAnim"] = (Action<string, string, bool>)((tag, name, forced) =>
        {
            if (_animateSprites.TryGetValue(tag, out var sprite))
                sprite.PlayAnim(name, forced);
        });

        lua.Globals["addAnimateSprite"] = (Action<string, bool>)((tag, front) =>
        {
            if (_animateSprites.TryGetValue(tag, out var sprite))
            {
                var prop = sprite.ToStageProp();
                if (front) prop.ZIndex = 1000;
                game?.AddStageObject(prop);
            }
        });

        lua.Globals["removeAnimateSprite"] = (Action<string>)((tag) =>
        {
            _animateSprites.Remove(tag);
        });
    }

    public static ModchartSprite GetSprite(string tag)
        => tag != null && _animateSprites.TryGetValue(tag, out var s) ? s : null;

    public static void ClearAll() => _animateSprites.Clear();
}
