using System;
using System.Collections.Generic;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine Events system — handles all built-in chart events.
/// Direct port of PlayState.triggerEvent() from states/PlayState.hx.
/// Each event handler is a delegate that receives value1, value2, strumTime.
/// </summary>
public static class PsychEvents
{
    /// <summary>
    /// Represents a chart event note.
    /// </summary>
    public class EventNote
    {
        public string Event { get; set; }
        public string Value1 { get; set; }
        public string Value2 { get; set; }
        public float StrumTime { get; set; }
    }

    public delegate void EventHandler(string value1, string value2, float strumTime, object gameState);

    private static readonly Dictionary<string, EventHandler> _handlers
        = new(StringComparer.OrdinalIgnoreCase);

    static PsychEvents()
    {
        RegisterBuiltInEvents();
    }

    private static void RegisterBuiltInEvents()
    {
        Register("Hey!", HandleHey);
        Register("Set GF Speed", HandleSetGfSpeed);
        Register("Add Camera Zoom", HandleAddCameraZoom);
        Register("Play Animation", HandlePlayAnimation);
        Register("Camera Follow Pos", HandleCameraFollowPos);
        Register("Alt Idle Animation", HandleAltIdleAnimation);
        Register("Screen Shake", HandleScreenShake);
        Register("Change Character", HandleChangeCharacter);
        Register("Change Scroll Speed", HandleChangeScrollSpeed);
    }

    public static void Register(string eventName, EventHandler handler)
    {
        _handlers[eventName] = handler;
    }

    /// <summary>
    /// Trigger a chart event.
    /// </summary>
    public static void Trigger(string eventName, string value1, string value2, float strumTime, object gameState = null)
    {
        if (_handlers.TryGetValue(eventName, out var handler))
        {
            try
            {
                handler(value1, value2, strumTime, gameState);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PSYCH-EVENTS] Error in event '{eventName}': {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine($"[PSYCH-EVENTS] Unknown event: {eventName}");
        }
    }

    /// <summary>
    /// Get the early trigger offset for an event (milliseconds).
    /// </summary>
    public static float GetEarlyTrigger(EventNote evt)
    {
        // Default: no early trigger. Override per-event if needed.
        return evt.Event switch
        {
            "Screen Shake" => 500f,
            _ => 0f
        };
    }

    // ═══════════════════════════════════════════
    // Built-in Event Handlers
    // ═══════════════════════════════════════════

    private static void HandleHey(string value1, string value2, float strumTime, object gameState)
    {
        // 0 = bf, 1 = gf, 2 = both
        int target = 2;
        switch (value1?.ToLowerInvariant()?.Trim())
        {
            case "bf":
            case "boyfriend":
            case "0":
                target = 0;
                break;
            case "gf":
            case "girlfriend":
            case "1":
                target = 1;
                break;
        }

        float duration = ParseFloat(value2, 0.6f);
        if (duration <= 0) duration = 0.6f;

        Console.WriteLine($"[EVENT] Hey! target={target} duration={duration}");
    }

    private static void HandleSetGfSpeed(string value1, string value2, float strumTime, object gameState)
    {
        int speed = (int)ParseFloat(value1, 1);
        if (speed < 1) speed = 1;
        Console.WriteLine($"[EVENT] Set GF Speed: {speed}");
    }

    private static void HandleAddCameraZoom(string value1, string value2, float strumTime, object gameState)
    {
        if (!ClientPrefs.Data.CamZooms) return;
        float gameZoom = ParseFloat(value1, 0.015f);
        float hudZoom = ParseFloat(value2, 0.03f);
        Console.WriteLine($"[EVENT] Add Camera Zoom: game={gameZoom} hud={hudZoom}");
    }

    private static void HandlePlayAnimation(string value1, string value2, float strumTime, object gameState)
    {
        string animName = value1 ?? "";
        int charType = 0; // 0=dad, 1=bf, 2=gf
        switch (value2?.ToLowerInvariant()?.Trim())
        {
            case "bf":
            case "boyfriend":
                charType = 1;
                break;
            case "gf":
            case "girlfriend":
                charType = 2;
                break;
            default:
                charType = (int)ParseFloat(value2, 0);
                break;
        }
        Console.WriteLine($"[EVENT] Play Animation: {animName} on char={charType}");
    }

    private static void HandleCameraFollowPos(string value1, string value2, float strumTime, object gameState)
    {
        float? x = TryParseFloat(value1);
        float? y = TryParseFloat(value2);
        bool forced = x.HasValue || y.HasValue;
        Console.WriteLine($"[EVENT] Camera Follow Pos: forced={forced} x={x} y={y}");
    }

    private static void HandleAltIdleAnimation(string value1, string value2, float strumTime, object gameState)
    {
        int charType = 0;
        switch (value1?.ToLowerInvariant()?.Trim())
        {
            case "gf":
            case "girlfriend":
                charType = 2;
                break;
            case "bf":
            case "boyfriend":
                charType = 1;
                break;
        }
        string suffix = value2 ?? "";
        Console.WriteLine($"[EVENT] Alt Idle Animation: char={charType} suffix={suffix}");
    }

    private static void HandleScreenShake(string value1, string value2, float strumTime, object gameState)
    {
        // value1 = "duration,intensity" for game camera
        // value2 = "duration,intensity" for HUD camera
        ParseShakeValues(value1, out float gameDur, out float gameInt);
        ParseShakeValues(value2, out float hudDur, out float hudInt);
        Console.WriteLine($"[EVENT] Screen Shake: game={gameDur}s/{gameInt} hud={hudDur}s/{hudInt}");
    }

    private static void HandleChangeCharacter(string value1, string value2, float strumTime, object gameState)
    {
        int charType = 0;
        switch (value1?.ToLowerInvariant()?.Trim())
        {
            case "gf":
            case "girlfriend":
                charType = 2;
                break;
            case "dad":
            case "opponent":
                charType = 1;
                break;
            default:
                charType = (int)ParseFloat(value1, 0);
                break;
        }
        Console.WriteLine($"[EVENT] Change Character: type={charType} to={value2}");
    }

    private static void HandleChangeScrollSpeed(string value1, string value2, float strumTime, object gameState)
    {
        float multiplier = ParseFloat(value1, 1f);
        float duration = ParseFloat(value2, 0f);
        Console.WriteLine($"[EVENT] Change Scroll Speed: mult={multiplier} duration={duration}");
    }

    // ═══════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════

    private static float ParseFloat(string str, float defaultVal)
    {
        if (string.IsNullOrEmpty(str)) return defaultVal;
        return float.TryParse(str.Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float val) ? val : defaultVal;
    }

    private static float? TryParseFloat(string str)
    {
        if (string.IsNullOrEmpty(str)) return null;
        return float.TryParse(str.Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float val) ? val : null;
    }

    private static void ParseShakeValues(string str, out float duration, out float intensity)
    {
        duration = 0;
        intensity = 0;
        if (string.IsNullOrEmpty(str)) return;

        string[] parts = str.Split(',');
        if (parts.Length >= 1)
            float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out duration);
        if (parts.Length >= 2)
            float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out intensity);
    }
}
