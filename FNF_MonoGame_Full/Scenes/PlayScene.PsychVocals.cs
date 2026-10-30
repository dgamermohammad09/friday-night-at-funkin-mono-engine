using System;
using System.Collections.Generic;
using System.IO;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Psych Engine 1.0 split vocals (states/PlayState.hx generateSong 1296-1303):
///   player   = Paths.voices(song, boyfriend.vocalsFile ?? 'Player')  -> songs/&lt;song&gt;/Voices-Player.ogg, fallback Voices.ogg
///   opponent = Paths.voices(song, dad.vocalsFile ?? 'Opponent')      -> songs/&lt;song&gt;/Voices-Opponent.ogg
/// Only mod songs (PsychNotes) get these names; they are tried BEFORE the port's per-character V-Slice names.
/// </summary>
public partial class PlayScene
{
    /// <summary>Psych vocals suffix for a character: its JSON "vocals_file" or the Psych default ('Player'/'Opponent').</summary>
    private string PsychVocalsSuffix(string charId, string fallback)
    {
        if (string.IsNullOrEmpty(charId)) return fallback;
        try
        {
            var roots = new List<string>();
            foreach (var r in GetPsychModRootPaths()) roots.Add(Path.Combine(r, "characters"));
            roots.Add(Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot, "data", "psych", "characters"));
            foreach (var dir in roots)
            {
                string p = Path.Combine(dir, charId + ".json");
                if (!global::FNF_MonoGame.Engine.FsCI.FileExists(p)) continue;
                var j = Newtonsoft.Json.Linq.JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(p));
                string v = (string)j["vocals_file"];
                return string.IsNullOrEmpty(v) ? fallback : v;
            }
        }
        catch (Exception e) { Console.WriteLine($"[PSYCH] vocals_file read failed for {charId}: {e.Message}"); }
        return fallback;
    }

    /// <summary>Puts the Psych 1.0 vocals names first for mod songs.</summary>
    private void PsychVocalsNames(List<string> names, string charId, bool player)
    {
        if (!PsychNotes) return;
        string s = PsychVocalsSuffix(charId, player ? "Player" : "Opponent");
        names.RemoveAll(n => string.Equals(n, s, StringComparison.OrdinalIgnoreCase));
        names.Insert(0, s);
        Console.WriteLine($"[PSYCH] vocals {(player ? "player" : "opponent")}: Voices-{s} first");
    }
}
