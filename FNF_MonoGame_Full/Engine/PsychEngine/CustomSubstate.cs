using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Scenes;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine CustomSubstate — allows Lua/HScript mods to open custom substates.
/// Direct port of psychlua/CustomSubstate.hx.
/// </summary>
public class CustomSubstate
{
    public static string Name { get; set; } = "unnamed";
    public static CustomSubstate Instance { get; private set; }

    public bool IsActive { get; private set; }
    public bool PausesGame { get; }
    /// <summary>closeCustomSubstate: PlayState.closeSubState runs at once (onResume), the substate is destroyed after the
    /// calling callback returns (onCustomSubstateDestroy).</summary>
    public bool PendingClose { get; private set; }

    // Objects added to this substate
    private readonly List<object> _objects = new();

    // Callback dispatcher
    private readonly Action<string, object[]> _callOnScripts;

    public CustomSubstate(string name, bool pauseGame, Action<string, object[]> callOnScripts)
    {
        Name = name;
        PausesGame = pauseGame;
        _callOnScripts = callOnScripts;
    }

    public bool PendingCreate { get; private set; }

    public void Create()
    {
        PendingCreate = false;
        Instance = this;
        IsActive = true;
        _callOnScripts?.Invoke("onCustomSubstateCreate", new object[] { Name });
        _callOnScripts?.Invoke("onCustomSubstateCreatePost", new object[] { Name });
        Console.WriteLine($"[CUSTOM-SUBSTATE] Opened: {Name}");
    }

    public void Update(float elapsed)
    {
        if (!IsActive) return;
        _callOnScripts?.Invoke("onCustomSubstateUpdate", new object[] { Name, elapsed });
        _callOnScripts?.Invoke("onCustomSubstateUpdatePost", new object[] { Name, elapsed });
    }

    public void Close()
    {
        if (!IsActive) return;
        _callOnScripts?.Invoke("onCustomSubstateDestroy", new object[] { Name });
        IsActive = false;
        Instance = null;
        Name = "unnamed";
        Console.WriteLine("[CUSTOM-SUBSTATE] Closed.");
    }

    public void InsertObject(object obj, int pos = -1)
    {
        if (obj == null) return;
        if (pos < 0) _objects.Add(obj);
        else _objects.Insert(Math.Min(pos, _objects.Count), obj);
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        if (!IsActive) return;
        _callOnScripts?.Invoke("onCustomSubstateDraw", new object[] { Name });
        foreach (var obj in _objects)
        {
            if (obj is PlayScene.StaticStageProp prop)
                DrawStaticProp(spriteBatch, prop);
            else if (obj is LuaText txt)
                txt.Draw(spriteBatch);
        }
    }

    private static void DrawStaticProp(SpriteBatch spriteBatch, PlayScene.StaticStageProp prop)
    {
        if (prop?.Texture == null || !prop.Visible) return;
        var pos = new Vector2(prop.X, prop.Y);
        var tint = prop.OverlayColor ?? Color.White;
        tint *= Math.Clamp(prop.Alpha, 0f, 1f);
        var fx = SpriteEffects.None;
        if (prop.FlipX) fx |= SpriteEffects.FlipHorizontally;
        if (prop.FlipY) fx |= SpriteEffects.FlipVertically;
        spriteBatch.Draw(
            prop.Texture,
            pos,
            prop.SourceRect, // current animation frame (was the whole sheet)
            tint,
            MathHelper.ToRadians(prop.Angle),
            Vector2.Zero,
            new Vector2(prop.ScaleX, prop.ScaleY),
            fx,
            0f);
    }

    // Static helpers for Lua/HScript integration
    public static bool OpenCustomSubstate(string name, bool pauseGame, Action<string, object[]> callOnScripts)
    {
        if (Instance != null) return false;
        var sub = new CustomSubstate(name, pauseGame, callOnScripts);
        Instance = sub; sub.PendingCreate = true; // create() runs at the start of the next update
        return true;
    }

    public static bool CloseCustomSubstate()
    {
        if (Instance == null || Instance.PendingClose) return Instance != null;
        Instance.PendingClose = true;
        return true;
    }

    public static bool InsertToCustomSubstate(object tagObject, int pos = -1)
    {
        if (Instance == null || tagObject == null) return false;
        Instance.InsertObject(tagObject, pos);
        return true;
    }
}
