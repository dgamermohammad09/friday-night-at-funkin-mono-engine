using Microsoft.Xna.Framework;
using FNF_MonoGame.Gameplay;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Parameters used to initialize <see cref="PlayScene"/>.
/// Matches the original FNF <c>PlayStateParams</c> typedef as closely as possible.
/// </summary>
public class PlayStateParams
{
    /// <summary>
    /// The song to play. If null, <see cref="SongName"/> is used to load the chart internally.
    /// </summary>
    public Chart? TargetSong { get; set; }

    /// <summary>
    /// The song name (used when <see cref="TargetSong"/> is null).
    /// </summary>
    public string SongName { get; set; } = "";

    /// <summary>
    /// The difficulty to play the song on.
    /// Defaults to <c>"normal"</c>.
    /// </summary>
    public string TargetDifficulty { get; set; } = "normal";

    /// <summary>
    /// The variation to play on.
    /// Defaults to <c>"default"</c>.
    /// </summary>
    public string TargetVariation { get; set; } = "default";

    /// <summary>
    /// The instrumental to play with.
    /// Significant if the song supports alternate instrumentals.
    /// </summary>
    public string? TargetInstrumental { get; set; }

    /// <summary>
    /// Whether the song should start in Practice Mode.
    /// Defaults to <c>false</c>.
    /// </summary>
    public bool PracticeMode { get; set; }

    /// <summary>
    /// Whether the song should start in Bot Play Mode.
    /// Defaults to <c>false</c>.
    /// </summary>
    public bool BotPlayMode { get; set; }

    /// <summary>
    /// Whether the results screen should show up before returning to the chart editor.
    /// Defaults to <c>false</c>.
    /// </summary>
    public bool PlaytestResults { get; set; }

    /// <summary>
    /// Whether the song should be in minimal mode.
    /// Defaults to <c>false</c>.
    /// </summary>
    public bool MinimalMode { get; set; }

    /// <summary>
    /// If specified, the game will jump to the given timestamp after the countdown ends.
    /// Defaults to <c>0.0</c>.
    /// </summary>
    public float StartTimestamp { get; set; }

    /// <summary>
    /// If specified, the game will play the song with the given speed.
    /// Defaults to <c>1.0</c> for 100% speed.
    /// </summary>
    public float PlaybackRate { get; set; } = 1.0f;

    /// <summary>
    /// If true, the game will not load the instrumental or vocal tracks,
    /// and they must be loaded externally.
    /// Defaults to <c>false</c>.
    /// </summary>
    public bool OverrideMusic { get; set; }

    /// <summary>
    /// The initial camera follow point.
    /// Used to persist the position of the camera follow between levels.
    /// </summary>
    public Vector2? CameraFollowPoint { get; set; }

    /// <summary>
    /// If non-null, the root path of the mod this song belongs to.
    /// Used to resolve mod-specific assets (charts, audio, characters, stages).
    /// </summary>
    public string? ModPath { get; set; }

    // ── Story Mode parameters (not in original PlayStateParams but needed for parity) ──

    /// <summary>
    /// List of songs in the current week (story mode).
    /// </summary>
    public List<string>? WeekSongs { get; set; }

    /// <summary>
    /// Index of the current song within <see cref="WeekSongs"/>.
    /// </summary>
    public int WeekSongIndex { get; set; }

    /// <summary>
    /// The current week identifier (story mode).
    /// </summary>
    public string? WeekId { get; set; }

    /// <summary>
    /// Accumulated score from previous songs in the week (story mode).
    /// </summary>
    public int AccumulatedScore { get; set; }

    /// <summary>
    /// Convenience factory for freeplay-style quick creation.
    /// </summary>
    public static PlayStateParams ForFreeplay(string songName, string difficulty = "normal", string modPath = null)
    {
        return new PlayStateParams { SongName = songName, TargetDifficulty = difficulty, ModPath = modPath };
    }

    /// <summary>
    /// Convenience factory for story-mode week creation.
    /// </summary>
    public static PlayStateParams ForStoryMode(string songName, string difficulty,
        List<string> weekSongs, int weekSongIndex, string weekId, int accumulatedScore = 0)
    {
        return new PlayStateParams
        {
            SongName = songName,
            TargetDifficulty = difficulty,
            WeekSongs = weekSongs,
            WeekSongIndex = weekSongIndex,
            WeekId = weekId,
            AccumulatedScore = accumulatedScore
        };
    }
}
