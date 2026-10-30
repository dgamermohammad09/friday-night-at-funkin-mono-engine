using System;
using System.Collections.Generic;
using System.Linq;

namespace FNF_MonoGame.Engine;

/// <summary>
/// V-Slice 0.8.1 unlock flow: Save.hasBeatenLevel / charactersSeen / oldChar (Save.hx), PlayableCharacter.isUnlocked
/// (scripts/players/pico.hxc: Pico = hasBeatenLevel('weekend1')), PlayerRegistry.hasNewCharacter / listNewCharacters /
/// isCharacterSeen (PlayerRegistry.hx:70-147).
/// Port-only rule: a save written before this flow existed that already played as Pico keeps Pico unlocked and seen
/// (no sudden lock, no unlock animation).
/// </summary>
public static class Unlocks
{
    static readonly string[] DefaultDiffs = { "easy", "normal", "hard" };
    static readonly string[] Weekend1Songs = { "darnell", "lit-up", "2hot", "blazin" };

    static HighscoreManager.SaveData D => HighscoreManager.Data;

    /// <summary>
    /// The original's UNLOCK_EVERYTHING define. The released 0.8.1 build has it ON (project.hxp configureFeatureFlags:
    /// UNLOCK_EVERYTHING.apply(this, true); CHANGELOG 0.8.0 "Playable Pico and Weekend 1 songs are now always unlocked"):
    /// hasBeatenLevel = true, every character seen, hasNewCharacter = false -> no locks / unlock animation / unlock dialog.
    /// Dev hook FNF_UNLOCK_EVERYTHING=0 = a -DNO_UNLOCK_EVERYTHING build (the full unlock flow below).
    /// </summary>
    public static bool UnlockEverything => Environment.GetEnvironmentVariable("FNF_UNLOCK_EVERYTHING") != "0";

    /// <summary>Save.hasBeatenLevel: a level score &gt; 0 on any of the difficulties (default easy/normal/hard).</summary>
    public static bool HasBeatenLevel(string levelId, IEnumerable<string> difficulties = null)
    {
        if (UnlockEverything) return true;
        foreach (var d in difficulties ?? DefaultDiffs)
            if (HighscoreManager.GetWeekScore(levelId, d) > 0) return true;
        return false;
    }

    /// <summary>PlayableCharacter.isUnlocked (bf: data unlocked=true, pico: pico.hxc override, mods: data 'unlocked').</summary>
    public static bool IsPlayerUnlocked(string playerId)
    {
        if (string.IsNullOrEmpty(playerId)) return false;
        if (UnlockEverything) return true;
        string id = playerId.ToLowerInvariant();
        if (id == "pico")
        {
            EnsureMigrated();
            return HasBeatenLevel("weekend1") || D.PicoLegacyUnlocked;
        }
        var e = PlayerRegistry.Get(id);
        return e == null ? id == "bf" : e.Unlocked;
    }

    /// <summary>PlayerRegistry.isCharacterSeen.</summary>
    public static bool IsCharacterSeen(string playerId)
    {
        if (UnlockEverything) return true;
        EnsureMigrated();
        return playerId != null && D.CharactersSeen.Contains(playerId);
    }

    /// <summary>Save.addCharacterSeen.</summary>
    public static void AddCharacterSeen(string playerId)
    {
        EnsureMigrated();
        if (playerId == null || D.CharactersSeen.Contains(playerId)) return;
        D.CharactersSeen.Add(playerId);
        Console.WriteLine("[UNLOCK] New list of characters seen: " + string.Join(",", D.CharactersSeen));
        HighscoreManager.Save();
    }

    /// <summary>PlayerRegistry.listNewCharacters: unlocked but not yet seen in Character Select.</summary>
    public static List<string> ListNewCharacters()
    {
        var r = new List<string>();
        if (UnlockEverything) return r;
        EnsureMigrated();
        foreach (var e in PlayerRegistry.All)
        {
            if (!IsPlayerUnlocked(e.Id)) continue;
            if (D.CharactersSeen.Contains(e.Id)) continue;
            r.Add(e.Id);
        }
        return r;
    }

    public static bool HasNewCharacter() => ListNewCharacters().Count > 0;

    /// <summary>Save.oldChar (Character Select intro video seen).</summary>
    public static bool OldChar
    {
        get { EnsureMigrated(); return D.OldChar; }
        set { if (D.OldChar == value) return; D.OldChar = value; HighscoreManager.Save(); }
    }

    /// <summary>
    /// First run with the unlock flow: charactersSeen = ["bf"] (Save.hx default). Saves that already have Pico scores
    /// (any *_pico chart or a Weekend 1 song cleared), a beaten Weekend 1 or Pico selected keep Pico unlocked + seen.
    /// </summary>
    public static void EnsureMigrated()
    {
        var d = D;
        if (d.CharactersSeen != null) return;
        d.CharactersSeen = new List<string> { "bf" };
        bool usedPico = string.Equals(d.SelectedCharacter, "pico", StringComparison.OrdinalIgnoreCase);
        foreach (var kv in d.SongScores)
        {
            if (kv.Value == null || kv.Value.Score <= 0) continue;
            string k = kv.Key.ToLowerInvariant();
            int u = k.LastIndexOf('_');
            string song = u > 0 ? k.Substring(0, u) : k;
            if (song.EndsWith("_pico") || Weekend1Songs.Contains(song)) { usedPico = true; break; }
        }
        bool beaten = false;
        foreach (var dd in DefaultDiffs) if (HighscoreManager.GetWeekScore("weekend1", dd) > 0) beaten = true;
        if (usedPico || beaten)
        {
            d.PicoLegacyUnlocked = usedPico;
            d.CharactersSeen.Add("pico");
            d.OldChar = true; // they already used Character Select: no first-entry intro video
        }
        Console.WriteLine($"[UNLOCK] migrated save: charactersSeen={string.Join(",", d.CharactersSeen)} picoLegacy={d.PicoLegacyUnlocked}");
        HighscoreManager.Save();
    }
}
