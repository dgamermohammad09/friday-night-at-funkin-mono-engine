using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FontStashSharp;
using Microsoft.Xna.Framework.Graphics;
using MoonSharp.Interpreter;
using FNF_MonoGame.Engine.PsychEngine;

namespace FNF_MonoGame.Scenes;

public partial class PlayScene
{
    private readonly Dictionary<string, LuaText> _luaTexts = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _luaTextDrawOrder = new();
    private readonly Dictionary<string, FontSystem> _luaFontSystems = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SpriteFontBase> _luaFontCache = new(StringComparer.OrdinalIgnoreCase);

    public void MakeLuaText(string tag, string text, float width, float x, float y)
    {
        if (string.IsNullOrEmpty(tag)) return;
        var luaText = new LuaText(tag, text, width, x, y)
        {
            Font = Assets.GetFont(16)
        };
        _luaTexts[tag] = luaText;
        // TextFunctions makeLuaText only creates the FlxText (camHUD); it is drawn once addLuaText adds it to the state
        // (Majin's "ILLEGAL INSTRUCTION" text showed from the start; it is added at step 2083).
        _luaTextDrawOrder.Remove(tag);
        MusicBeatState.Variables[tag] = luaText;
    }

    public void AddLuaText(string tag, bool front = false)
    {
        if (PsychOrderActive && !string.IsNullOrEmpty(tag) && _luaTexts.TryGetValue(tag, out var pt)) PsychOrderAdd(pt, true); // TextFunctions addLuaText: getTargetInstance().add(text)
        if (string.IsNullOrEmpty(tag) || !_luaTexts.ContainsKey(tag)) return;
        if (!_luaTextDrawOrder.Contains(tag))
            _luaTextDrawOrder.Add(tag);
        if (front)
        {
            _luaTextDrawOrder.Remove(tag);
            _luaTextDrawOrder.Insert(0, tag);
        }
    }

    public void RemoveLuaText(string tag)
    {
        if (_luaTexts.TryGetValue(tag, out var old)) PsychOrderRemove(old);
        _luaTexts.Remove(tag);
        _luaTextDrawOrder.Remove(tag);
        MusicBeatState.Variables.Remove(tag);
    }

    public LuaText GetLuaText(string tag)
    {
        _luaTexts.TryGetValue(tag, out var t);
        return t;
    }

    public void SetTextString(string tag, string text)
    {
        if (_luaTexts.TryGetValue(tag, out var t)) { t.Text = text ?? ""; return; }
        // TextFunctions setTextString: LuaUtils.getObjectDirectly(tag) -> any FlxText (scoreTxt, botplayTxt, timeTxt)
        if (FNF_MonoGame.Engine.PsychEngine.LuaUtils.GetVarInArray(FNF_MonoGame.Engine.PsychEngine.MusicBeatState.GetTargetInstance(), tag, false) is PsychHudObject h) h.text = text ?? "";
    }

    public bool LuaTextItalic(string tag, bool italic)
    {
        if (_luaTexts.TryGetValue(tag, out var t)) { t.Italic = italic; return true; }
        return FNF_MonoGame.Engine.PsychEngine.LuaUtils.GetVarInArray(FNF_MonoGame.Engine.PsychEngine.MusicBeatState.GetTargetInstance(), tag, false) is PsychHudObject;
    }

    public void SetTextSize(string tag, int size)
    {
        if (_luaTexts.TryGetValue(tag, out var t))
        {
            t.Size = Math.Max(1, size);
            t.Font = LoadLuaFont(t.FontName, t.Size);
        }
    }

    public void SetTextFont(string tag, string font)
    {
        if (_luaTexts.TryGetValue(tag, out var t))
        {
            t.FontName = string.IsNullOrEmpty(font) ? "vcr" : font;
            t.Font = LoadLuaFont(t.FontName, t.Size);
        }
    }

    public void SetTextColor(string tag, string color)
    {
        if (_luaTexts.TryGetValue(tag, out var t))
            t.TextColor = FunkinLua.ParseHexColor(color);
    }

    public void SetTextBorder(string tag, double size, DynValue color)
    {
        if (_luaTexts.TryGetValue(tag, out var t))
        {
            t.BorderSize = (float)size;
            if (color != null && !color.IsNil())
            {
                string c = color.CastToString();
                if (!string.IsNullOrEmpty(c))
                    t.BorderColor = FunkinLua.ParseHexColor(c);
            }
        }
    }

    public void SetTextAlignment(string tag, string alignment)
    {
        if (_luaTexts.TryGetValue(tag, out var t))
            t.Alignment = alignment?.ToLowerInvariant() ?? "center";
    }

    public void SetTextWidth(string tag, float width)
    {
        if (_luaTexts.TryGetValue(tag, out var t))
            t.FieldWidth = width;
    }

    public float GetTextWidth(string tag)
    {
        if (_luaTexts.TryGetValue(tag, out var t))
            return t.Width;
        return 0f;
    }

    public float GetTextHeight(string tag)
    {
        if (_luaTexts.TryGetValue(tag, out var t))
            return t.Height;
        return 0f;
    }

    public void SetTextCamera(string tag, string camera)
    {
        if (_luaTexts.TryGetValue(tag, out var t))
            t.Camera = camera?.ToLowerInvariant() ?? "hud";
    }

    public void SetTextOrder(string tag, int order)
    {
        if (string.IsNullOrEmpty(tag) || !_luaTexts.ContainsKey(tag)) return;
        if (!_luaTextDrawOrder.Contains(tag))
            _luaTextDrawOrder.Add(tag);
        _luaTextDrawOrder.Remove(tag);
        int idx = Math.Clamp(order, 0, _luaTextDrawOrder.Count);
        _luaTextDrawOrder.Insert(idx, tag);
    }

    public int GetTextOrder(string tag)
    {
        return _luaTextDrawOrder.IndexOf(tag);
    }

    private SpriteFontBase LoadLuaFont(string fontName, int size)
    {
        if (string.IsNullOrEmpty(fontName)) fontName = "vcr";
        string cacheKey = $"{fontName}@{size}";
        if (_luaFontCache.TryGetValue(cacheKey, out var cached)) return cached;

        string resolved = null;
        foreach (var candidate in new[] { fontName, $"fonts/{fontName}" })
        {
            resolved = Assets.ResolvePath(candidate);
            if (resolved != null && global::FNF_MonoGame.Engine.FsCI.FileExists(resolved)) break;
            if (!candidate.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) &&
                !candidate.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
            {
                resolved = Assets.ResolvePath(candidate + ".ttf");
                if (resolved != null && global::FNF_MonoGame.Engine.FsCI.FileExists(resolved)) break;
            }
        }

        if (resolved != null)
        {
            if (!_luaFontSystems.TryGetValue(fontName, out var system))
            {
                system = new FontSystem();
                _luaFontSystems[fontName] = system;
            }
            system.AddFont(global::FNF_MonoGame.Engine.FsCI.ReadAllBytes(resolved));
            var font = system.GetFont(size);
            _luaFontCache[cacheKey] = font;
            return font;
        }

        return Assets.GetFont(size);
    }

    public void DrawLuaTexts(SpriteBatch spriteBatch)
    {
        if (PsychOrderActive && _psychMembers != null) return; // drawn in PlayState.members order (PsychDrawHudMembers)
        foreach (var tag in _luaTextDrawOrder)
        {
            if (_luaTexts.TryGetValue(tag, out var t))
                t.Draw(spriteBatch);
        }
    }

    public void ClearLuaTexts()
    {
        foreach (var tag in _luaTexts.Keys.ToArray())
            MusicBeatState.Variables.Remove(tag);
        _luaTexts.Clear();
        _luaTextDrawOrder.Clear();
        _luaFontSystems.Clear();
        _luaFontCache.Clear();
    }

    // Draw Lua-created stage props assigned to the HUD camera (setObjectCamera('hud')).
    public void DrawHudStageProps(SpriteBatch spriteBatch)
    {
        if (PsychOrderActive && _psychMembers != null) { PsychDrawHudMembers(spriteBatch, false); return; }
        foreach (var prop in _staticProps)
        {
            if (IsHudCamera(prop.Camera) && prop.CameraObj == null)
                DrawStaticProp(spriteBatch, prop);
        }
    }

    private static bool IsHudCamera(string camera) =>
        string.Equals(camera, "hud", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(camera, "camhud", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(camera, "other", StringComparison.OrdinalIgnoreCase) || string.Equals(camera, "camother", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(camera, "hxcam", StringComparison.Ordinal);
}
