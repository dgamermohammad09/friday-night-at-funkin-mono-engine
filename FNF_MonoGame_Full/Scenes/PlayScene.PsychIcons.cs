using System;
using System.IO;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Psych HealthIcon.changeIcon(char.healthIcon) for Psych-mod songs: name = 'icons/' + icon, else 'icons/icon-' + icon,
/// else 'icons/icon-face'; Paths.image looks in the mod (and mods/) images first, then the shared images.
/// </summary>
public partial class PlayScene
{
    /// <summary>Psych Online skin characters loaded this song (character name -> its mod folder, "" = base game).
    /// Their icons resolve against the skin's own mod, not the song's (PlayState.initPlayCharacter sets
    /// Mods.currentModDirectory to the skin mod).</summary>
    private readonly System.Collections.Generic.Dictionary<string, string> _skinIconMods = new(StringComparer.OrdinalIgnoreCase);

    private void RegisterSkinIconMod(string charName, string mod)
    {
        if (!string.IsNullOrEmpty(charName)) _skinIconMods[charName] = mod ?? "";
    }

    /// <summary>HealthIcon.changeIcon for a skin character: icons/&lt;icon&gt;, icons/icon-&lt;icon&gt;, else icons/icon-face;
    /// each name is looked up in mods/&lt;skinMod&gt;/images, mods/images, then the base game images.
    /// Returns null for base-game (V-Slice) skins without a Psych healthicon (the normal lookup handles them).</summary>
    private Texture2D SkinLoadCharacterIcon(string charName)
    {
        if (string.IsNullOrEmpty(charName) || !_skinIconMods.TryGetValue(charName, out var mod)) return null;
        Gameplay.Character ch = null;
        foreach (var c in new[] { _boyfriend, _opponent })
            if (c != null && (string.Equals(c.Name, charName, StringComparison.OrdinalIgnoreCase) || string.Equals(c.OriginalName, charName, StringComparison.OrdinalIgnoreCase))) { ch = c; break; }
        string icon = ch?.JsonData?.PsychHealthIcon;
        if (string.IsNullOrEmpty(icon))
        {
            if (string.IsNullOrEmpty(mod)) return null;    // base-game skin (pico-playable): V-Slice healthIcon lookup
            icon = charName;
        }
        var roots = new System.Collections.Generic.List<string>();
        string bas = Engine.PsychEngine.PsychMods.ModsBasePath;
        if (!string.IsNullOrEmpty(bas))
        {
            if (!string.IsNullOrEmpty(mod)) roots.Add(Path.Combine(bas, mod));
            roots.Add(bas);
        }
        foreach (var file in new[] { icon, "icon-" + icon })
        {
            foreach (var root in roots)
            {
                string p = Path.Combine(root, "images", "icons", file + ".png");
                if (!global::FNF_MonoGame.Engine.FsCI.FileExists(p)) continue;
                try
                {
                    using var fs = global::FNF_MonoGame.Engine.FsCI.OpenRead(p);
                    Console.WriteLine($"[SKIN-ICON] {charName} ({(mod == "" ? "base" : mod)}): {p}");
                    return Texture2D.FromStream(Game.GraphicsDevice, fs);
                }
                catch (Exception e) { Console.WriteLine($"[SKIN-ICON] {p}: {e.Message}"); }
            }
            var tex = Assets.LoadTexture($"images/icons/{file}.png");
            if (tex != null && tex != Assets.Pixel) { Console.WriteLine($"[SKIN-ICON] {charName}: base images/icons/{file}.png"); return tex; }
        }
        Console.WriteLine($"[SKIN-ICON] {charName}: no icon '{icon}', using icon-face");
        return Assets.LoadTexture("images/icons/icon-face.png");
    }

    private Texture2D PsychLoadCharacterIcon(string charName)
    {
        if (!PsychNotes) return null;
        string icon = null;
        foreach (var c in new[] { _boyfriend, _opponent, _girlfriend })
            if (c != null && string.Equals(c.Name, charName, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(c.JsonData?.PsychHealthIcon))
            { icon = c.JsonData.PsychHealthIcon; break; }
        if (string.IsNullOrEmpty(icon)) return null;
        foreach (var file in new[] { icon, "icon-" + icon })
        {
            foreach (var root in GetPsychModRootPaths())
            {
                string p = Path.Combine(root, "images", "icons", file + ".png");
                if (!global::FNF_MonoGame.Engine.FsCI.FileExists(p)) continue;
                try { using var fs = global::FNF_MonoGame.Engine.FsCI.OpenRead(p); return Texture2D.FromStream(Game.GraphicsDevice, fs); }
                catch (Exception e) { Console.WriteLine($"[PSYCH-ICON] {p}: {e.Message}"); }
            }
            var tex = Assets.LoadTexture($"images/icons/{file}.png");
            if (tex != null && tex != Assets.Pixel) return tex;
        }
        return Assets.LoadTexture("images/icons/icon-face.png");
    }
}
