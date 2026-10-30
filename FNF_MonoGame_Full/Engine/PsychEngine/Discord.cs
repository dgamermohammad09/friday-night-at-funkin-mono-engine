using System;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine-style Discord Rich Presence client.
/// Wraps the existing FNF_MonoGame DiscordRPCManager on desktop and is a no-op on Xbox.
/// Original: backend/Discord.hx
/// </summary>
public static class DiscordClient
{
    public static bool IsInitialized { get; private set; }

    private const string DefaultId = "863222024192262205";
    private static string _clientId = DefaultId;

#if !XBOX_UWP
    private static readonly DiscordRPCManager Manager = new();
#endif

    public static string ClientId
    {
        get => _clientId;
        set
        {
            bool changed = _clientId != value;
            _clientId = value ?? DefaultId;
            if (changed && IsInitialized)
            {
                Shutdown();
                Initialize();
            }
        }
    }

    public static void Check()
    {
#if !XBOX_UWP
        if (ClientPrefs.Data.DiscordRPC) Initialize();
        else if (IsInitialized) Shutdown();
#endif
    }

    public static void Prepare()
    {
#if !XBOX_UWP
        if (!IsInitialized && ClientPrefs.Data.DiscordRPC)
            Initialize();
#endif
    }

    public static void Initialize()
    {
#if !XBOX_UWP
        if (IsInitialized) return;
        Manager.Initialize(_clientId);
        IsInitialized = true;
#endif
    }

    public static void Shutdown()
    {
#if !XBOX_UWP
        IsInitialized = false;
        Manager.Dispose();
#else
        IsInitialized = false;
#endif
    }

    public static void ChangePresence(string details = "In the Menus", string state = null,
        string smallImageKey = null, bool hasStartTimestamp = false, double endTimestamp = 0,
        string largeImageKey = "icon")
    {
#if !XBOX_UWP
        if (!IsInitialized) return;

        double startTimestamp = hasStartTimestamp ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() : 0;
        if (endTimestamp > 0) endTimestamp = startTimestamp + endTimestamp;

        Manager.UpdatePresence(state, details, largeImageKey, smallImageKey);
#endif
    }

    public static void UpdatePresence()
    {
        // Presence is updated through ChangePresence.
    }

    public static void ResetClientId()
    {
        ClientId = DefaultId;
    }

    public static void LoadModRpc()
    {
        var pack = PsychMods.GetPack();
        if (pack != null && pack.discordRPC != null && pack.discordRPC != _clientId)
        {
            ClientId = pack.discordRPC;
        }
    }

    public static void AddLuaCallbacks(MoonSharp.Interpreter.Script lua)
    {
        if (lua == null) return;
        lua.Globals["changeDiscordPresence"] = (Action<string, string, string, bool, double, string>)ChangePresence;
        lua.Globals["changeDiscordClientID"] = (Action<string>)(id => ClientId = id ?? DefaultId);
    }
}
