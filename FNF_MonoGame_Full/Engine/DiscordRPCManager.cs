using System;
using System.IO;
using DiscordRPC;
using DiscordRPC.Message;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Discord Rich Presence manager matching the original Friday Night Funkin' format.
/// Original source: funkin/api/discord/DiscordClient.hx
/// </summary>
public class DiscordRPCManager : IDisposable
{
    private DiscordRpcClient _client;
    private bool _initialized;
    private bool _connectionFailed;
    private float _reconnectTimer;
    private const float RECONNECT_INTERVAL = 10f;
    private DateTime _gameStartTime;
    private string _configClientId;

    // Cached presence data (matching original FNF variables)
    private string _discordRPCAlbum = "";
    private string _discordRPCIcon = "";
    private string _discordRPCState = "";
    private string _discordRPCDetails = "";
    private bool _isPlaying = false;
    private bool _isPaused = false;

    private static readonly string[] CONFIG_PATHS = new[]
    {
        "discord.json",
        Path.Combine("Content", "discord.json"),
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "discord.json"),
        System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot, "discord.json")
    };

    public bool IsInitialized => _initialized;

    public void Initialize(string customClientId = null)
    {
        if (_initialized) return;

        string clientId = customClientId ?? LoadClientIdFromConfig();

        if (string.IsNullOrWhiteSpace(clientId) || clientId == "YOUR_CLIENT_ID_HERE")
        {
            Console.WriteLine("[DiscordRPC] No valid Client ID found. Skipping Discord integration.");
            Console.WriteLine("[DiscordRPC] To enable: Create a Discord app at https://discord.com/developers/applications");
            Console.WriteLine("[DiscordRPC] Then create discord.json next to the exe with: { \"clientId\": \"YOUR_APP_ID\" }");
            return;
        }

        Connect(clientId);
    }

    private string LoadClientIdFromConfig()
    {
        if (!string.IsNullOrWhiteSpace(_configClientId))
            return _configClientId;

        foreach (var path in CONFIG_PATHS)
        {
            if (!global::FNF_MonoGame.Engine.FsCI.FileExists(path)) continue;
            try
            {
                var json = global::FNF_MonoGame.Engine.FsCI.ReadAllText(path);
                var obj = JObject.Parse(json);
                _configClientId = obj["clientId"]?.ToString();
                if (!string.IsNullOrWhiteSpace(_configClientId))
                {
                    Console.WriteLine($"[DiscordRPC] Loaded Client ID from {path}");
                    return _configClientId;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DiscordRPC] Failed to load config from {path}: {ex.Message}");
            }
        }
        return null;
    }

    private void Connect(string clientId)
    {
        try
        {
            _client?.Dispose();
            _client = new DiscordRpcClient(clientId);
            _client.OnReady += OnReady;
            _client.OnConnectionFailed += OnConnectionFailed;
            _client.OnError += OnError;
            _client.OnClose += OnClose;

            _client.Initialize();
            _gameStartTime = DateTime.UtcNow;
            _initialized = true;
            _connectionFailed = false;
            _reconnectTimer = 0;

            Console.WriteLine("[DiscordRPC] Connected successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DiscordRPC] Connection failed: {ex.Message}");
            _initialized = false;
            _connectionFailed = true;
        }
    }

    public void Update(float deltaTime = 0f)
    {
        if (_connectionFailed && !_initialized && deltaTime > 0)
        {
            _reconnectTimer += deltaTime;
            if (_reconnectTimer >= RECONNECT_INTERVAL)
            {
                _reconnectTimer = 0;
                string clientId = _configClientId ?? LoadClientIdFromConfig();
                if (!string.IsNullOrWhiteSpace(clientId))
                    Connect(clientId);
            }
            return;
        }

        if (!_initialized || _client == null) return;

        try
        {
            _client.Invoke();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DiscordRPC] Update error: {ex.Message}");
            _initialized = false;
            _connectionFailed = true;
        }
    }

    /// <summary>
    /// Original FNF: Menus - state = "In the Menus", details = null
    /// </summary>
    public void SetMenuState(string menuName = "Main Menu")
    {
        if (!_initialized) return;
        _isPlaying = false;
        _isPaused = false;
        UpdatePresence("In the Menus", null, "logo", "menu");
    }

    /// <summary>
    /// Original FNF: Freeplay - state = "In the Menus", details = null
    /// </summary>
    public void SetFreeplayState()
    {
        if (!_initialized) return;
        _isPlaying = false;
        _isPaused = false;
        UpdatePresence("In the Menus", null, "logo", "menu");
    }

    /// <summary>
    /// Original FNF: Story Mode menu - state = "In the Menus", details = null
    /// </summary>
    public void SetStoryModeState(string weekName = null)
    {
        if (!_initialized) return;
        _isPlaying = false;
        _isPaused = false;
        UpdatePresence("In the Menus", null, "logo", "menu");
    }

    /// <summary>
    /// Original FNF: Character Select - state = "In the Menus", details = null
    /// </summary>
    public void SetCharacterSelectState()
    {
        if (!_initialized) return;
        _isPlaying = false;
        _isPaused = false;
        UpdatePresence("In the Menus", null, "logo", "menu");
    }

    /// <summary>
    /// Original FNF: Options - state = "In the Menus", details = null
    /// </summary>
    public void SetOptionsState()
    {
        if (!_initialized) return;
        _isPlaying = false;
        _isPaused = false;
        UpdatePresence("In the Menus", null, "logo", "menu");
    }

    /// <summary>
    /// Original FNF: Title Screen - state = "In the Menus", details = null
    /// </summary>
    public void SetTitleState()
    {
        if (!_initialized) return;
        _isPlaying = false;
        _isPaused = false;
        UpdatePresence("In the Menus", null, "logo", "menu");
    }

    /// <summary>
    /// Set up Discord RPC for gameplay. Call when starting a song.
    /// Original FNF sets: discordRPCAlbum = 'album-${currentChart?.album}'
    ///                    discordRPCIcon = 'icon-${currentCharacterData.opponent}'
    /// </summary>
    public void SetGameplayStart(string songName, string difficulty, string album, string opponent)
    {
        _isPlaying = true;
        _isPaused = false;
        _discordRPCAlbum = !string.IsNullOrWhiteSpace(album) ? $"album-{album}" : "album-volume1";
        _discordRPCIcon = !string.IsNullOrWhiteSpace(opponent) ? $"icon-{opponent}" : "";

        if (!_initialized) return;
        UpdateGameplayPresence(songName, difficulty, false, null, false, false);
    }

    /// <summary>
    /// Update presence during gameplay (called each frame).
    /// Original FNF: state = buildDiscordRPCState() = "SongName [Difficulty]"
    ///               details = buildDiscordRPCDetails() = "Story Mode: WeekName" or "Freeplay"
    /// </summary>
    public void UpdateGameplayPresence(string songName, string difficulty, bool isStoryMode, string weekName, bool isPracticeMode = false, bool isBotPlayMode = false)
    {
        if (!_initialized || !_isPlaying) return;

        string state = BuildDiscordRPCState(songName, difficulty);
        string details = BuildDiscordRPCDetails(isStoryMode, weekName, isPracticeMode, isBotPlayMode);

        UpdatePresence(state, details, _discordRPCAlbum, _discordRPCIcon);
    }

    /// <summary>
    /// Called when game is paused.
    /// Original FNF: details = 'Paused - ${buildDiscordRPCDetails()}'
    /// </summary>
    public void SetPaused(string songName, string difficulty, bool isStoryMode, string weekName, bool isPracticeMode = false, bool isBotPlayMode = false)
    {
        if (!_initialized || !_isPlaying) return;
        _isPaused = true;

        string state = BuildDiscordRPCState(songName, difficulty);
        string details = $"Paused - {BuildDiscordRPCDetails(isStoryMode, weekName, isPracticeMode, isBotPlayMode)}";

        UpdatePresence(state, details, _discordRPCAlbum, _discordRPCIcon);
    }

    /// <summary>
    /// Called when game is resumed from pause.
    /// </summary>
    public void SetResumed(string songName, string difficulty, bool isStoryMode, string weekName, bool isPracticeMode = false, bool isBotPlayMode = false)
    {
        if (!_initialized || !_isPlaying) return;
        _isPaused = false;

        string state = BuildDiscordRPCState(songName, difficulty);
        string details = BuildDiscordRPCDetails(isStoryMode, weekName, isPracticeMode, isBotPlayMode);

        UpdatePresence(state, details, _discordRPCAlbum, _discordRPCIcon);
    }

    /// <summary>
    /// Called on Game Over.
    /// Original FNF: details = 'Game Over - ${buildDiscordRPCDetails()}'
    /// </summary>
    public void SetGameOver(string songName, string difficulty, bool isStoryMode, string weekName, bool isPracticeMode = false, bool isBotPlayMode = false)
    {
        if (!_initialized) return;

        string state = BuildDiscordRPCState(songName, difficulty);
        string details = $"Game Over - {BuildDiscordRPCDetails(isStoryMode, weekName, isPracticeMode, isBotPlayMode)}";

        UpdatePresence(state, details, _discordRPCAlbum, _discordRPCIcon);
    }

    /// <summary>
    /// Called on Results screen.
    /// </summary>
    public void SetResultsState(string songName, string difficulty, int score, float accuracy, bool isStoryMode, string weekName)
    {
        if (!_initialized) return;
        _isPlaying = false;

        string state = BuildDiscordRPCState(songName, difficulty);
        string details = isStoryMode ? $"Story Mode: {weekName}" : "Freeplay";

        UpdatePresence(state, $"Results - {details}", _discordRPCAlbum, _discordRPCIcon);
    }

    // ═══════════════════════════════════════════════════════
    //  Original FNF Helper Methods
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Original FNF: buildDiscordRPCState()
    /// Returns: "SongName [Difficulty]"
    /// </summary>
    private string BuildDiscordRPCState(string songName, string difficulty)
    {
        string diff = !string.IsNullOrWhiteSpace(difficulty)
            ? difficulty.Replace("-", " ")
            : "???";
        // Capitalize each word
        var parts = diff.Split(' ');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 0)
                parts[i] = char.ToUpper(parts[i][0]) + parts[i][1..].ToLower();
        }
        diff = string.Join(" ", parts);

        string song = !string.IsNullOrWhiteSpace(songName) ? songName : "???";
        return $"{song} [{diff}]";
    }

    /// <summary>
    /// Original FNF: buildDiscordRPCDetails()
    /// Returns: "Story Mode: WeekName" or "Freeplay" / "Freeplay [Practice]" / "Freeplay [Bot Play]"
    /// </summary>
    private string BuildDiscordRPCDetails(bool isStoryMode, string weekName, bool isPracticeMode = false, bool isBotPlayMode = false)
    {
        if (isStoryMode)
        {
            return !string.IsNullOrWhiteSpace(weekName)
                ? $"Story Mode: {weekName}"
                : "Story Mode";
        }
        else
        {
            if (isPracticeMode)
                return "Freeplay [Practice]";
            if (isBotPlayMode)
                return "Freeplay [Bot Play]";
            return "Freeplay";
        }
    }

    public void UpdatePresence(string state, string details)
        => UpdatePresence(state, details, "logo", "menu");

    public static void UpdatePresence(string state)
        => FNFGame.Instance?.Discord?.UpdatePresence(state, null, "logo", "menu");

    /// <summary>Hide the presence (Options > Gameplay > Discord Rich Presence off).</summary>
    public void ClearPresence() { try { if (_client != null && _initialized) _client.ClearPresence(); } catch { } }

    public void UpdatePresence(string state, string details, string largeImageKey, string smallImageKey)
    {
        if (_client == null || !_initialized) return;
        if (!FNF_MonoGame.Engine.PsychEngine.ClientPrefs.Data.DiscordRPC) { try { _client.ClearPresence(); } catch { } return; }   // Options > Gameplay > Discord Rich Presence

        try
        {
            var presence = new RichPresence
            {
                State = state ?? "",
                Details = details,
                Assets = new Assets
                {
                    LargeImageKey = largeImageKey ?? "album-volume1",
                    LargeImageText = "Friday Night Funkin'",
                    SmallImageKey = smallImageKey ?? ""
                }
            };

            _client.SetPresence(presence);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DiscordRPC] SetPresence error: {ex.Message}");
        }
    }

    private void OnReady(object sender, ReadyMessage msg)
    {
        Console.WriteLine($"[DiscordRPC] Connected to Discord user: {msg.User.Username}#{msg.User.Discriminator}");
    }

    private void OnConnectionFailed(object sender, ConnectionFailedMessage msg)
    {
        Console.WriteLine($"[DiscordRPC] Connection failed: {msg.Type}");
        _initialized = false;
        _connectionFailed = true;
    }

    private void OnClose(object sender, CloseMessage msg)
    {
        Console.WriteLine($"[DiscordRPC] Connection closed: {msg.Reason} ({msg.Code})");
        _initialized = false;
        _connectionFailed = true;
    }

    private void OnError(object sender, ErrorMessage msg)
    {
        Console.WriteLine($"[DiscordRPC] Error: {msg.Message}");
    }

    public void Dispose()
    {
        try
        {
            _client?.Dispose();
            _client = null;
            _initialized = false;
        }
        catch { }
    }
}
