using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FNF_MonoGame.Engine.PsychEngine;
using Newtonsoft.Json.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Psych Engine 1.0 stage JSON "objects" (backend/StageData.hx addObjectsToState 128-215, PlayState.create 418-429).
/// Each object is a 'sprite' (loadGraphic), 'animatedSprite' (Paths.getAtlas + addByPrefix/addByIndices + addOffset,
/// firstAnimation), 'square' (1x1 white makeGraphic scaled) or a character layer ('gf'/'dad'/'boyfriend'[Group]).
/// Sprites are ModchartSprites at (x,y): scale + updateHitbox only when scale != 1, scrollFactor, color
/// (CoolUtil.colorFromString), alpha, angle, flipX/flipY, antialiasing. The list order IS the draw order: it becomes
/// PlayState.members, so characters can sit between props. Only for Psych mod songs (PsychNotes); the objects are
/// built as ModchartSprites (the Lua-sprite flixel draw rules: offset/origin/angle/straight alpha) and the members list
/// is seeded for setObjectOrder/getObjectOrder. Objects are also published in `variables` (getProperty('name.x')).
/// </summary>
public partial class PlayScene
{
    private List<(string kind, string name)> _psychStageObjects;

    /// <summary>StageData.validateVisibility, including Haxe's dangling else (STORY bit checked first, the FREEPLAY
    /// test only runs inside it): filters 15 = hidden everywhere, exactly like Psych.</summary>
    private static bool PsychValidateVisibility(int filters)
    {
        bool story = FNF_MonoGame.Engine.PsychEngine.PsychGlobals.IsStoryMode;
        if ((filters & 4) == 4)
        {
            if (!story) return false;
            else if ((filters & 8) == 8)
            {
                if (story) return false;
            }
        }
        bool lq = ClientPrefs.Data?.LowQuality ?? false;
        return (lq && (filters & 1) == 1) || (!lq && (filters & 2) == 2);
    }

    private static string PsychColorHex(JToken t)
    {
        string s = t?.Type == JTokenType.String ? ((string)t).Trim() : null;
        if (string.IsNullOrEmpty(s)) return null;
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
        s = s.TrimStart('#');
        if (s.Length == 8) s = s.Substring(2);   // FlxColor 0xAARRGGBB
        if (s.Length != 6) return null;
        return "#" + s.ToUpperInvariant();
    }

    private static float[] PsychPair(JToken t, float def)
    {
        if (t is JArray a && a.Count >= 2) return new[] { a[0].Value<float>(), a[1].Value<float>() };
        return new[] { def, def };
    }

    /// <summary>Raw "objects" of the Psych stage JSON for this song (null when the stage has none / not a Psych mod song).</summary>
    private List<JObject> _psychStageObjectsRaw;

    /// <summary>stages/&lt;stage&gt;.json with "objects" for a Psych mod song: returns stage data WITHOUT V-Slice props (the
    /// objects are built as Psych sprites in PsychObjectsInitMembers, before the scripts), or null when there are none.</summary>
    private StageJsonData LoadPsychObjectsStage()
    {
        _psychStageObjects = null; _psychStageObjectsRaw = null;
        if (!PsychNotes) return null;
        string stage = _chart?.Stage ?? "stage";
        foreach (var root in GetPsychModRootPaths())
        {
            string path = Path.Combine(root, "stages", stage + ".json");
            if (!global::FNF_MonoGame.Engine.FsCI.FileExists(path)) continue;
            JObject j;
            try { j = JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(path)); }
            catch (Exception e) { Console.WriteLine($"[PSYCH] stage objects: {path}: {e.Message}"); return null; }
            if (!(j["objects"] is JArray objs) || objs.Count == 0) return null;   // StageData.getStageFile: the first stages/<id>.json wins
            _psychStageObjectsRaw = objs.OfType<JObject>().ToList();
            _psychStageHideGf = j.Value<bool?>("hide_girlfriend") ?? false;
            _stageJsonFilePath = path;
            Console.WriteLine($"[PSYCH] stage objects: {path} ({_psychStageObjectsRaw.Count} entries)");
            return new StageJsonData { Name = stage, Directory = j.Value<string>("directory") ?? "", CameraZoom = j.Value<float?>("defaultZoom") ?? 0.9f, Props = new List<StageProp>() };
        }
        return null;
    }
    private bool _psychStageHideGf;

    /// <summary>StageData.addObjectsToState: one ModchartSprite per sprite/animatedSprite/square object.</summary>
    private StaticStageProp PsychBuildStageObject(JObject o, string type)
    {
        float x = o.Value<float?>("x") ?? 0f, y = o.Value<float?>("y") ?? 0f;
        var spr = new ModchartSprite(x, y)
        {
            Name = o.Value<string>("name") ?? "", ScaleX = 1f, ScaleY = 1f, Alpha = 1f, ScrollX = 1f, ScrollY = 1f,
            PsychOffsetX = 0f, PsychOffsetY = 0f,
        };
        Vector2? firstOffset = null;
        if (type == "square")
        {
            var tex = new Texture2D(Game.GraphicsDevice, 1, 1);
            tex.SetData(new[] { Color.White });
            spr.Texture = tex;
            spr.Antialiasing = false;
        }
        else
        {
            string image = o.Value<string>("image") ?? "";
            if (type == "sprite")
            {
                string resolved = Assets.ResolvePath($"images/{image}.png");
                if (resolved != null) { try { spr.Texture = Texture2D.FromFile(Game.GraphicsDevice, global::FNF_MonoGame.Engine.FsCI.Fix(resolved)); } catch (Exception e) { Console.WriteLine($"[PSYCH] stage object {image}: {e.Message}"); } }
                else Console.WriteLine($"[PSYCH] stage object image not found: images/{image}.png");
            }
            else
            {
                var sheet = SpriteSheet.Load(Game, $"images/{image}");
                spr.SpriteSheet = sheet; spr.Sheet = sheet; spr.Texture = sheet?.Texture;
                if (o["animations"] is JArray anims)
                {
                    string fa = o.Value<string>("firstAnimation");
                    foreach (var a in anims.OfType<JObject>())
                    {
                        string an = a.Value<string>("anim"), pre = a.Value<string>("name");
                        int fps = (int)Math.Round(a.Value<float?>("fps") ?? 24f); bool loop = a.Value<bool?>("loop") ?? false;
                        if (a["indices"] is JArray ix && ix.Count > 0) spr.AddAnimationByIndices(an, pre, ix.Select(v => v.Value<int>()).ToArray(), fps, loop);
                        else spr.AddAnimationByPrefix(an, pre, fps, loop);
                        Vector2 off = a["offsets"] is JArray of && of.Count >= 2 ? new Vector2(of[0].Value<float>(), of[1].Value<float>()) : Vector2.Zero;
                        if (a["offsets"] is JArray) spr.AddOffset(an, off.X, off.Y);
                        // `if (curAnim == null || data.firstAnimation == anim.anim) spr.playAnim(anim.anim, true)` -> offset.set(animOffsets)
                        if (firstOffset == null || fa == an) { spr.PlayAnim(an, true); firstOffset = off; }
                    }
                }
            }
            spr.FlipX = o.Value<bool?>("flipX") ?? false;
            spr.FlipY = o.Value<bool?>("flipY") ?? false;
            spr.Antialiasing = (o.Value<bool?>("antialiasing") ?? true) && ClientPrefs.Data.Antialiasing;
        }
        if (firstOffset is Vector2 fo) { spr.PsychOffsetX = fo.X; spr.PsychOffsetY = fo.Y; }
        var sc = PsychPair(o["scale"], 1f);
        if (sc[0] != 1f || sc[1] != 1f) { spr.ScaleX = sc[0]; spr.ScaleY = sc[1]; PsychUpdateHitbox(spr); }
        var sf = PsychPair(o["scroll"], 1f);
        spr.ScrollX = sf[0]; spr.ScrollY = sf[1];
        string col = PsychColorHex(o["color"]);
        if (col != null && col != "#FFFFFF") spr.OverlayColor = ParseStageColor(col);
        if (o["alpha"] != null && o["alpha"].Type != JTokenType.Null) spr.Alpha = o.Value<float>("alpha");
        if (o["angle"] != null && o["angle"].Type != JTokenType.Null) spr.Angle = o.Value<float>("angle");
        return spr;
    }

    /// <summary>PlayState.create 418-429: members = [luaDebugGroup, ...objects in JSON order] (characters only where listed),
    /// then variables.set(name, sprite) for every non-reserved object. Runs before any Lua script.</summary>
    private void PsychObjectsInitMembers()
    {
        if (_psychStageObjectsRaw == null || !PsychOrderActive) return;
        var m = new List<object> { "luaDebugGroup" };
        var added = new HashSet<string>(StringComparer.Ordinal);
        int n = 0;
        foreach (var o in _psychStageObjectsRaw)
        {
            string type = o.Value<string>("type") ?? "";
            switch (type)
            {
                case "gf": case "gfGroup": if (!_psychStageHideGf && added.Add("gf")) m.Add("gfGroup"); continue;
                case "dad": case "dadGroup": if (added.Add("dad")) m.Add("dadGroup"); continue;
                case "boyfriend": case "boyfriendGroup": if (added.Add("boyfriend")) m.Add("boyfriendGroup"); continue;
                case "square": case "sprite": case "animatedSprite": break;
                default: Console.WriteLine($"[Stage .JSON file] Unknown sprite type detected: {type}"); continue;
            }
            if (!PsychValidateVisibility(o.Value<int?>("filters") ?? 3)) continue;
            var spr = PsychBuildStageObject(o, type);
            AddStageObject(spr);
            m.Add(spr); n++;
            string name = spr.Name;
            if (!string.IsNullOrEmpty(name) && !PsychReservedStageNames.Contains(name))
            {
                MusicBeatState.Variables[name] = spr;
                ModchartSprites[name] = spr;
            }
        }
        // A character layer missing from "objects" is never added in Psych; this engine still draws it on top.
        foreach (var g in new[] { "gfGroup", "dadGroup", "boyfriendGroup" })
            if (!m.Contains(g) && !(g == "gfGroup" && _psychStageHideGf)) m.Add(g);   // hide_girlfriend: addObjectsToState(gf = null) -> gfGroup never a member
        _psychMembers = m;
        PsychOrderApply();
        Console.WriteLine($"[PSYCH] stage objects: {n} sprites, members: " + PsychMembersDump());
    }

    private static readonly HashSet<string> PsychReservedStageNames = new() { "gf", "gfGroup", "dad", "dadGroup", "boyfriend", "boyfriendGroup" };

    /// <summary>PsychOrderApply hook for animated stage objects (value-type props in _animatedProps).</summary>
    private void PsychOrderApplyAnim(string marker, int z)
    {
        string name = marker.Substring("psychanim:".Length);
        for (int i = 0; i < _animatedProps.Count; i++)
        {
            if (_animatedProps[i].Name != name) continue;
            var p = _animatedProps[i];
            p.ZIndex = z;
            _animatedProps[i] = p;
        }
    }
}
