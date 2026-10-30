using System;
using System.IO;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Sprites;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine Paths — asset path resolution with mod support.
/// Direct port of backend/Paths.hx.
/// Resolves paths checking mods first, then base game assets.
/// Xbox compatible: uses abstract path resolution.
/// </summary>
public static class PsychPaths
{
    private static string _basePath;
    private static string _modsPath;

    /// <summary>
    /// Initialize paths with the base game asset directory.
    /// </summary>
    public static void Init(string basePath, string modsPath = null)
    {
        _basePath = basePath ?? "";
        _modsPath = modsPath ?? Path.Combine(_basePath, "..", "mods");
    }

    /// <summary>
    /// Get the base shared path (e.g. Content/data or assets/base_game).
    /// </summary>
    public static string GetSharedPath(string path = "")
    {
        return string.IsNullOrEmpty(path) ? _basePath : Path.Combine(_basePath, path);
    }

    /// <summary>
    /// Get the mods base path.
    /// </summary>
    public static string Mods(string path = "")
    {
        return string.IsNullOrEmpty(path) ? _modsPath : Path.Combine(_modsPath, path);
    }

    /// <summary>
    /// Format a song/path name to lowercase with spaces as dashes.
    /// Matches Psych Engine Paths.formatToSongPath() exactly.
    /// </summary>
    public static string FormatToSongPath(string path)
    {
        return Difficulty.FormatToSongPath(path);
    }

    /// <summary>
    /// Resolve a full path, checking mods first, then base game.
    /// </summary>
    public static string ResolvePath(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return null;

        // Check current mod directory first
        if (!string.IsNullOrEmpty(PsychMods.CurrentModDirectory))
        {
            string modPath = Path.Combine(_modsPath, PsychMods.CurrentModDirectory, relativePath);
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(modPath) || global::FNF_MonoGame.Engine.FsCI.DirectoryExists(modPath))
                return modPath;
        }

        // Check global mods
        foreach (string globalMod in PsychMods.GetGlobalMods())
        {
            string gPath = Path.Combine(_modsPath, globalMod, relativePath);
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(gPath) || global::FNF_MonoGame.Engine.FsCI.DirectoryExists(gPath))
                return gPath;
        }

        // Check base mods folder
        {
            string baseMod = Path.Combine(_modsPath, relativePath);
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(baseMod) || global::FNF_MonoGame.Engine.FsCI.DirectoryExists(baseMod))
                return baseMod;
        }

        // Check base game assets
        {
            string basePath = Path.Combine(_basePath, relativePath);
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(basePath) || global::FNF_MonoGame.Engine.FsCI.DirectoryExists(basePath))
                return basePath;
        }

        return null;
    }

    /// <summary>
    /// Get path to a JSON file.
    /// </summary>
    public static string Json(string path)
    {
        return ResolvePath(Path.Combine("data", path + ".json"))
            ?? Path.Combine(_basePath, "data", path + ".json");
    }

    /// <summary>
    /// Get path to an image file.
    /// </summary>
    public static string Image(string path)
    {
        return ResolvePath(Path.Combine("images", path + ".png"))
            ?? Path.Combine(_basePath, "images", path + ".png");
    }

    /// <summary>
    /// Get path to a sound file.
    /// </summary>
    public static string Sound(string path)
    {
        return ResolvePath(Path.Combine("sounds", path + ".ogg"))
            ?? Path.Combine(_basePath, "sounds", path + ".ogg");
    }

    /// <summary>
    /// Get path to music.
    /// </summary>
    public static string Music(string path)
    {
        return ResolvePath(Path.Combine("music", path + ".ogg"))
            ?? Path.Combine(_basePath, "music", path + ".ogg");
    }

    /// <summary>
    /// Get path to a song's audio (Inst, Voices).
    /// </summary>
    public static string SongAudio(string songName, string file)
    {
        string formatted = FormatToSongPath(songName);
        return ResolvePath(Path.Combine("songs", formatted, file + ".ogg"))
            ?? Path.Combine(_basePath, "songs", formatted, file + ".ogg");
    }

    /// <summary>
    /// Read text from a file, resolving through mod paths.
    /// </summary>
    public static string GetTextFromFile(string path)
    {
        string resolved = ResolvePath(path);
        if (resolved != null && global::FNF_MonoGame.Engine.FsCI.FileExists(resolved))
            return global::FNF_MonoGame.Engine.FsCI.ReadAllText(resolved);
        return null;
    }

    public static string Font(string font)
    {
        return Image($"fonts/{font}");
    }

    /// <summary>
    /// Load a Sparrow/JSON/XML spritesheet for FlxSprite-style animation.
    /// </summary>
    public static SpriteSheet GetSparrowAtlas(string path)
    {
        string resolved = Image(path);
        if (string.IsNullOrEmpty(resolved)) return null;
        string basePath = Path.ChangeExtension(resolved, null);
        return SpriteSheet.Load(FNFGame.Instance, basePath);
    }

    /// <summary>
    /// Load a Packer .txt spritesheet for FlxSprite-style animation.
    /// </summary>
    public static SpriteSheet GetPackerAtlas(string path)
    {
        string resolved = Image(path);
        if (string.IsNullOrEmpty(resolved)) return null;
        string basePath = Path.ChangeExtension(resolved, null);
        return SpriteSheet.Load(FNFGame.Instance, basePath);
    }

    /// <summary>
    /// Load an Adobe Animate spritemap atlas into a FlxAnimate-style sprite.
    /// </summary>
    public static void LoadAnimateAtlas(Sprite sprite, string path)
    {
        string resolved = Image(path);
        if (string.IsNullOrEmpty(resolved)) return;
        string basePath = Path.ChangeExtension(resolved, null);
        sprite.Frames = SpriteSheet.Load(FNFGame.Instance, basePath);
    }
}
