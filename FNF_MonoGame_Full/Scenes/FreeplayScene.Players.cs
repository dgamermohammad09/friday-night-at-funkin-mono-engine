using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Newtonsoft.Json.Linq;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Freeplay for registry players (PlayerRegistry): the selected player's freeplayDJ (atlas or Sparrow, offsets and
/// text1-3 from data/players/&lt;id&gt;.json). If the DJ asset is missing (Playable KB references freeplay/freeplay-sky,
/// which it does not ship) the base DJ of the player's freeplayStyle is used and the fallback is logged.
/// </summary>
public partial class FreeplayScene
{
    static string SelectedPlayerId() =>
        (Environment.GetEnvironmentVariable("FNF_FREEPLAY_CHAR") ?? HighscoreManager.Data.SelectedCharacter ?? "bf").Trim();

    static PlayerEntry SelectedModPlayer()
    {
        var e = PlayerRegistry.Get(SelectedPlayerId());
        return e != null && e.IsMod ? e : null;
    }

    /// <summary>FreeplayDJ.getFreeplayDJText(i) for mod players, else the engine's base text.</summary>
    private static string DjTextFor(int i, string baseText)
    {
        var e = SelectedModPlayer();
        string t = e?.DjText(i);
        return string.IsNullOrEmpty(t) ? baseText : t;
    }

    /// <summary>Loads a mod player's DJ. False = not a mod player, or its asset is missing (caller loads the base DJ).</summary>
    private bool TryLoadModDJ()
    {
        var e = SelectedModPlayer();
        if (e == null) return false;
        var dj = e.FreeplayDJ;
        string asset = (string)dj?["assetPath"];
        if (string.IsNullOrEmpty(asset)) { Console.WriteLine($"[FREEPLAY-DJ] {e.Id}: no freeplayDJ -> base DJ '{e.FreeplayStyle}'"); return false; }
        var anims = new List<(string name, string prefix, Vector2 off)>();
        if (dj["animations"] is JArray ja)
            foreach (var a in ja)
            {
                var o = a["offsets"] as JArray;
                anims.Add(((string)a["name"] ?? "", (string)a["prefix"] ?? "",
                    o != null && o.Count >= 2 ? new Vector2((float)o[0], (float)o[1]) : Vector2.Zero));
            }
        string path = "menus/" + asset.Trim('/'); // mod images/<asset> or base Content/menus/<asset>
        SpriteSheet sheet = null;
        bool atlas = false;
        try
        {
            string dir = Assets.ResolveDirectory(path);
            if (dir != null && global::FNF_MonoGame.Engine.FsCI.FileExists(Path.Combine(dir, "Animation.json")))
            {
                sheet = SpriteSheet.Load(Game, path, preRenderComposites: true, preRenderFilter: anims.Select(x => x.prefix).ToArray());
                atlas = sheet != null;
            }
            else if (Assets.ResolvePath(path + ".xml") != null)
                sheet = SpriteSheet.Load(Game, path, sparrowOnly: true);
        }
        catch (Exception ex) { Console.WriteLine($"[FREEPLAY-DJ] {e.Id}: load '{asset}' failed: {ex.Message}"); sheet = null; }
        if (sheet == null)
        {
            Console.WriteLine($"[FREEPLAY-DJ] {e.Id}: DJ asset '{asset}' missing -> base DJ of freeplayStyle '{e.FreeplayStyle}'");
            return false;
        }
        _djSheet = sheet;
        _djSprite = new AnimatedSprite { Sheet = _djSheet, UseTimelineAnchoring = atlas };
        _djOffsets = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        string Role(string role)
        {
            foreach (var a in anims)
                if (a.name.Equals(role, StringComparison.OrdinalIgnoreCase))
                {
                    string k = FindDJAnim(a.prefix);
                    if (k != null) _djOffsets[k] = a.off;
                    return k;
                }
            return null;
        }
        _djIntroAnim = Role("intro");
        _djIdleAnim = Role("idle");
        _djConfirmAnim = Role("confirm");
        _djEasterEggAnim = Role("idleEasterEgg");
        _djTvAnim = Role("cartoon");
        _djFistPumpAnim = Role("fistPump");
        _djLossAnim = Role("loss");
        _djCharSelectAnim = Role("charSelect");
        _djNewUnlockAnim = Role("newUnlock");
        if (_djIdleAnim == null)
        {
            _djIdleAnim = _djSheet.Animations.Keys.FirstOrDefault();
            if (_djIdleAnim != null && !_djOffsets.ContainsKey(_djIdleAnim)) _djOffsets[_djIdleAnim] = Vector2.Zero;
        }
        if (_djIdleAnim == null)
        {
            Console.WriteLine($"[FREEPLAY-DJ] {e.Id}: '{asset}' has no usable animation -> base DJ");
            _djSheet = null; _djSprite = null; return false;
        }
        Console.WriteLine($"[FREEPLAY-DJ] {e.Id}: {(atlas ? "atlas" : "Sparrow")} '{asset}' idle={_djIdleAnim} intro={_djIntroAnim} confirm={_djConfirmAnim}");
        return true;
    }
}
