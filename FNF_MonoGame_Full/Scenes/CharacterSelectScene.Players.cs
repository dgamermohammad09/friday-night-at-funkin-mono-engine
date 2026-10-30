using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Character Select players from <see cref="PlayerRegistry"/> (CharSelectSubState.loadAvailableCharacters, 0.8.4):
/// base players keep the engine's converted art (menus/character_select/&lt;id&gt;/player|spectator); mod players load
/// charSelect/&lt;id&gt;Chill and charSelect.gf.assetPath from the mod, as an Animate atlas (like the original) or - port
/// extension, the original only takes atlases - as a Sparrow sheet (Playable KB ships kbChill/qtChill as Sparrow).
/// </summary>
public partial class CharacterSelectScene
{
    // Port-only placement of Sparrow chill sprites (they carry no stage matrix): bottom-centre of the idle animation's
    // trimmed bounds goes to the feet point of the base atlas characters (measured on bf/pico/gf/nene at rest).
    static readonly Vector2 SparrowPlayerFeet = new Vector2(970f, 592f);
    static readonly Vector2 SparrowSpectatorFeet = new Vector2(330f, 600f);
    // ...and the idle bounds are scaled down (never up) to fit the area the base characters use, so a large
    // sheet (KB's qtChill: 589x502 seated QT) does not cover the icon grid.
    static readonly Vector2 SparrowPlayerBox = new Vector2(440f, 460f);
    static readonly Vector2 SparrowSpectatorBox = new Vector2(320f, 380f);
    static readonly Dictionary<string, float> _iconNorm = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>loadAvailableCharacters: targetPosition = charSelect.position; while (taken) += 1.</summary>
    private void LoadAvailableCharactersFromRegistry()
    {
        var ids = new List<string>();
        foreach (var e in PlayerRegistry.All)
        {
            int? p = e.CharSelectPosition;
            if (p == null) continue;           // getCharSelectData() == null -> skipped
            if (!Unlocks.IsPlayerUnlocked(e.Id)) Console.WriteLine($"[CharSelect] {e.Id}: locked (shown as a Lock)");
            int pos = p.Value;
            while (_availableChars.ContainsKey(pos)) pos++;
            if (pos < 0 || pos > 8) { Console.WriteLine($"[CharSelect] {e.Id}: no free slot (position {p})"); continue; }
            _availableChars[pos] = e.Id;
            ids.Add(e.Id);
            Console.WriteLine($"[CharSelect] Placing player {e.Id} at position {pos}{(e.IsMod ? " (mod)" : "")}");
        }
        if (ids.Count == 0) { ids.Add("bf"); _availableChars[4] = "bf"; }
        _charIds = ids.ToArray();
    }

    /// <summary>Base players have engine art under menus/character_select/&lt;id&gt;.</summary>
    private static bool HasEngineCharSelectArt(string cid) =>
        global::FNF_MonoGame.Engine.FsCI.DirectoryExists(Path.Combine(AssetManager.ContentRoot, "menus", "character_select", cid, "player"));

    /// <summary>Loads the chill sprites of a registry player without engine art. Returns false for base players.</summary>
    private bool LoadRegistryChill(string cn)
    {
        if (HasEngineCharSelectArt(cn)) return false;
        var e = PlayerRegistry.Get(cn);
        var ps = LoadChillSheet("charSelect/" + cn + "Chill", SparrowPlayerFeet, SparrowPlayerBox);
        if (ps != null)
        {
            _playerSheets[cn] = ps;
            var anim = new CharAnim();
            string enterAnim = FindAnim(ps, "Enter", "slidein");
            string idleAnim = FindAnim(ps, "Idle", "idle");
            if (enterAnim != null && ps.RawCompositeData.ContainsKey(enterAnim))
            {
                anim.Play(ps, enterAnim, false);
                anim.OnFinish = () => { if (idleAnim != null) anim.Play(ps, idleAnim, true); anim.OnFinish = null; };
            }
            else if (idleAnim != null) anim.Play(ps, idleAnim, true);
            _playerAnims[cn] = anim;
        }
        else Console.WriteLine($"[CharSelect] {cn}: charSelect/{cn}Chill missing (no atlas / Sparrow) - player not drawn");

        string gfPath = e?.GfAssetPath;
        if (!string.IsNullOrEmpty(gfPath))
        {
            SpriteSheet ss = null;
            // base GFs referenced by mod players use the engine's converted art
            string baseGf = gfPath.Equals("charSelect/gfChill", StringComparison.OrdinalIgnoreCase) ? "bf"
                          : gfPath.Equals("charSelect/neneChill", StringComparison.OrdinalIgnoreCase) ? "pico" : null;
            if (baseGf != null)
                ss = SpriteSheet.Load(Game, "menus/character_select/" + baseGf + "/spectator", preRenderComposites: true,
                    preRenderFilter: new[] { "Enter", "IdleLeft", "IdleRight", "Confirm", "Cancel", "Exit" }, deferComposites: true, applyStageInstanceTransform: true);
            else
                ss = LoadChillSheet(gfPath, SparrowSpectatorFeet, SparrowSpectatorBox);
            if (ss != null)
            {
                _spectatorSheets[cn] = ss;
                var anim = new CharAnim();
                string specEnter = FindAnim(ss, "Enter", "slidein");
                string specIdle = FindAnim(ss, "IdleLeft", "Idle", "idle");
                bool dual = SheetHasAnim(ss, "IdleLeft");
                if (specEnter != null && ss.RawCompositeData.ContainsKey(specEnter) && specEnter != specIdle)
                {
                    anim.Play(ss, specEnter, false);
                    anim.OnFinish = () => { if (specIdle != null) anim.Play(ss, specIdle, !dual); _gfDanceLeft = true; anim.OnFinish = null; };
                }
                else if (specIdle != null) anim.Play(ss, specIdle, !dual);
                _spectatorAnims[cn] = anim;
            }
            else Console.WriteLine($"[CharSelect] {cn}: gf asset '{gfPath}' missing - spectator hidden");
        }
        return true;
    }

    /// <summary>
    /// Paths.animateAtlas(path) (images/&lt;path&gt;/Animation.json) or a Sparrow sheet (images/&lt;path&gt;/&lt;name&gt;.png+xml or
    /// images/&lt;path&gt;.png+xml). Labels are aliased to the engine's chill names (Enter/Idle/Confirm/Cancel/Exit).
    /// </summary>
    private SpriteSheet LoadChillSheet(string assetPath, Vector2 feet, Vector2 box)
    {
        string img = "images/" + assetPath.Trim('/');
        string dir = Assets.ResolveDirectory(img);
        if (dir != null && global::FNF_MonoGame.Engine.FsCI.FileExists(Path.Combine(dir, "Animation.json")))
        {
            var sheet = SpriteSheet.Load(Game, img, preRenderComposites: true, deferComposites: true, applyStageInstanceTransform: true);
            if (sheet != null) { AliasChillAnims(sheet); Console.WriteLine($"[CharSelect] {assetPath}: atlas [{string.Join(",", sheet.RawCompositeData.Keys)}]"); return sheet; }
        }
        string name = Path.GetFileName(assetPath.TrimEnd('/'));
        foreach (var cand in new[] { img + "/" + name, img })
        {
            if (Assets.ResolvePath(cand + ".xml") == null) continue;
            var sheet = SpriteSheet.Load(Game, cand, sparrowOnly: true);
            if (sheet == null) continue;
            SparrowToRaw(sheet, feet, box);
            Console.WriteLine($"[CharSelect] {assetPath}: Sparrow [{string.Join(",", sheet.RawCompositeData.Keys)}]");
            return sheet;
        }
        return null;
    }

    static readonly (string canon, string[] keys)[] ChillRoles =
    {
        ("Enter", new[] { "slidein", "enter" }),
        ("Exit", new[] { "slideout", "exit" }),
        ("Cancel", new[] { "deselect", "cancel" }),
        ("Confirm", new[] { "select", "confirm" }),
        ("IdleLeft", new[] { "danceleft", "idleleft" }),
        ("IdleRight", new[] { "danceright", "idleright" }),
        ("Idle", new[] { "idle" }),
    };

    /// <summary>Resolve a role to an animation name: exact (case-insensitive), then "ends with", then "contains" -
    /// "Idle Confirm" is a confirm, "Idle Idle" an idle, "deselect" never a select.</summary>
    static string MatchRole(IEnumerable<string> names, string[] keys, HashSet<string> used)
    {
        var list = names.Where(n => !used.Contains(n)).ToList();
        foreach (var k in keys) { var m = list.FirstOrDefault(n => n.Trim().Equals(k, StringComparison.OrdinalIgnoreCase)); if (m != null) return m; }
        foreach (var k in keys) { var m = list.FirstOrDefault(n => n.Trim().EndsWith(k, StringComparison.OrdinalIgnoreCase) && !(k == "select" && n.Trim().EndsWith("deselect", StringComparison.OrdinalIgnoreCase))); if (m != null) return m; }
        foreach (var k in keys) { var m = list.FirstOrDefault(n => n.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0 && !(k == "select" && n.IndexOf("deselect", StringComparison.OrdinalIgnoreCase) >= 0)); if (m != null) return m; }
        return null;
    }

    static void AliasChillAnims(SpriteSheet sheet)
    {
        var used = new HashSet<string>();
        var names = sheet.RawCompositeData.Keys.ToList();
        foreach (var (canon, keys) in ChillRoles)
        {
            if (sheet.RawCompositeData.ContainsKey(canon)) continue;
            var m = MatchRole(names, keys, used);
            if (m == null) continue;
            used.Add(m);
            sheet.RawCompositeData[canon] = sheet.RawCompositeData[m];
        }
    }

    /// <summary>Converts Sparrow animations into single-part RawCompositeData frames so the atlas chill pipeline
    /// (CharAnim + DrawCharacterComposite) draws them unchanged.</summary>
    static void SparrowToRaw(SpriteSheet sheet, Vector2 feet, Vector2 box)
    {
        var used = new HashSet<string>();
        var names = sheet.Animations.Keys.ToList();
        var roles = new Dictionary<string, List<SpriteFrame>>();
        foreach (var (canon, keys) in ChillRoles)
        {
            var m = MatchRole(names, keys, used);
            if (m == null) continue;
            used.Add(m);
            roles[canon] = sheet.Animations[m];
        }
        if (!roles.ContainsKey("Idle") && names.Count > 0) roles["Idle"] = sheet.Animations[names[0]];
        // anchor: union of the idle frames' trimmed bounds in frame space
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (var f in roles["Idle"])
        {
            float w = f.Rotated ? f.SourceRect.Height : f.SourceRect.Width, h = f.Rotated ? f.SourceRect.Width : f.SourceRect.Height;
            minX = Math.Min(minX, f.Offset.X); minY = Math.Min(minY, f.Offset.Y); maxX = Math.Max(maxX, f.Offset.X + w); maxY = Math.Max(maxY, f.Offset.Y + h);
        }
        float sc = Math.Min(1f, Math.Min(box.X / Math.Max(1f, maxX - minX), box.Y / Math.Max(1f, maxY - minY)));
        float ox = feet.X - sc * (minX + maxX) / 2f, oy = feet.Y - sc * maxY;
        foreach (var kv in roles)
        {
            var frames = new List<List<(SpriteFrame Frame, float A, float B, float C, float D, float TX, float TY)>>();
            foreach (var f in kv.Value)
                frames.Add(new List<(SpriteFrame, float, float, float, float, float, float)> { (f, sc, 0f, 0f, sc, ox + sc * f.Offset.X, oy + sc * f.Offset.Y) });
            sheet.RawCompositeData[kv.Key] = frames;
        }
        sheet.Animations.Clear(); // FindAnim must resolve to the RawCompositeData keys
    }

    /// <summary>Pixel icons are 50x50 frames drawn at scale 2 (2.6 selected); HD mod icons (KB: 640x640 frames) are
    /// normalised to the same on-screen size (0.8.x would draw them at 2x their frame size).</summary>
    private float IconScaleNorm(string charId, Vector2 frameSize)
    {
        if (frameSize.X <= 64f) return 1f;
        return 50f / frameSize.X;
    }

    /// <summary>PixelatedIcon plays 'idle'/'confirm'/'confirm-hold': mod sheets may use other case ("Idle", "Confirm").</summary>
    private static void AliasIconAnims(SpriteSheet sheet)
    {
        if (sheet == null) return;
        foreach (var want in new[] { "idle", "confirm", "confirm-hold" })
        {
            if (sheet.Animations.ContainsKey(want)) continue;
            var k = sheet.Animations.Keys.FirstOrDefault(n => n.Equals(want, StringComparison.OrdinalIgnoreCase));
            if (k != null) sheet.Animations[want] = sheet.Animations[k];
        }
        if (!sheet.Animations.ContainsKey("confirm-hold") && sheet.Animations.TryGetValue("confirm", out var c) && c.Count > 0)
            sheet.Animations["confirm-hold"] = new List<SpriteFrame> { c[c.Count - 1] };
        if (!sheet.Animations.ContainsKey("idle") && sheet.Animations.Count > 0)
            sheet.Animations["idle"] = sheet.Animations.Values.First();
    }
}
