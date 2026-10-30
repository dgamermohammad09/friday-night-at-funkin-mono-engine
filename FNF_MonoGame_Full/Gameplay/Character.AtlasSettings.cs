using System;
using FNF_MonoGame.Engine;
using Newtonsoft.Json.Linq;

namespace FNF_MonoGame.Gameplay;

/// <summary>
/// Per-character AnimateAtlasCharacter.getAtlasSettings() from the original character scripts
/// (assets/preload/scripts/characters/*.hxc). Applied to Animation.json before parsing, so hidden layers are
/// neither drawn nor part of the timeline bounds (flixel-animate Timeline.getBounds skips !layer.visible).
/// </summary>
public partial class Character
{
    private Action<JObject> GetAtlasJsonPatch()
    {
        // nene-tankmen.hxc getAtlasSettings: onSymbolCreate -> symbol "hearts aflutter" has every layer visible = false
        // (the heart is spawned as a separate comboHeart sprite on combo50, PlayScene.GfCombo.cs). Shares the nene atlas.
        if (OriginalName == "nene-tankmen") return HideLibrarySymbols(n => n == "hearts aflutter");
        switch (Name)
        {
            case "monster":
                // monster.hxc: onSymbolCreate -> every layer whose name starts with "HAT" gets visible = false
                // (the shared atlas is also used by monster-christmas, which keeps the hat).
                return HideLibraryLayers(n => n.StartsWith("HAT", StringComparison.Ordinal));
            default:
                return null;
        }
    }

    /// <summary>onSymbolCreate hiding all layers of the library symbols whose name matches.</summary>
    private static Action<JObject> HideLibrarySymbols(Func<string, bool> hide) => data =>
    {
        if (data?["SD"]?["S"] is not JArray syms) return;
        foreach (var sym in syms)
        {
            if (!hide(sym?["SN"]?.ToString() ?? "")) continue;
            if (sym?["TL"]?["L"] is not JArray layers) continue;
            foreach (var l in layers) if (l is JObject lo) lo["__hidden"] = true;
        }
    };

    /// <summary>onSymbolCreate runs for every library SymbolItem (SD.S), not for the main timeline.</summary>
    private static Action<JObject> HideLibraryLayers(Func<string, bool> hide) => data =>
    {
        if (data?["SD"]?["S"] is not JArray syms) return;
        foreach (var sym in syms)
        {
            if (sym?["TL"]?["L"] is not JArray layers) continue;
            // Timeline._loadJson caches the bounds BEFORE onSymbolCreate runs: hidden layers are not drawn but still
            // count in the symbol bounds (monster w 486 h 737 incl. the hat) -> mark, don't remove.
            foreach (var l in layers) if (l is JObject lo && hide(lo["LN"]?.ToString() ?? "")) lo["__hidden"] = true;
        }
    };

    private SpriteSheet LoadAtlasWithSettings(Microsoft.Xna.Framework.Game game, string path)
    {
        var patch = GetAtlasJsonPatch();
        SpriteSheet.LazyCompositeKeep = LazyCompositeKeepSet();   // UWP low memory: unused timeline anims -> JIT (Character.Memory.cs)
        try
        {
            if (patch == null) return SpriteSheet.Load(game, path, preRenderComposites: true);
            SpriteSheet.AnimationJsonPatch = patch;
            try { return SpriteSheet.Load(game, path, preRenderComposites: true); }
            finally { SpriteSheet.AnimationJsonPatch = null; }
        }
        finally { SpriteSheet.LazyCompositeKeep = null; }
    }
}
