using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json.Linq;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Gameplay;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// stages/props/SserafimLipSyncSprite.hxc + characters/sserafim-*.hxc (Spaghetti): every frame of the girl's mouth
/// symbol ("mouth default" / "mouth edit" / "mouth yunjin") gets a FlxSpriteElement holding a lip-sync atlas
/// ('sserafim-lipsync' / '-yunjin', 4103 frames). FlxSpriteElement.applyObjectTransform only takes the parent matrix's
/// translation (point 0,0) and rotation (atan2(b,a)); the lip sprite is drawn unscaled at that point with its own
/// offset/angle per character animation (LIP_SYNC_OFFSETS), origin = Std.int(timeline bounds)/2 (traced 23,17.5),
/// frame = floor(songPosition/1000*24)-1 while the girl's characterType is BF, else frame 0. Drawn with the girl's
/// shader (SserafimShader ignores sprite alpha: chaewon's alpha 0.5 has no effect) on top of the composite.
/// Anchors (mouth instance matrices per tick) come from SpriteSheet.Anchors.
/// </summary>
public partial class PlayScene
{
    private sealed class LipAtlas
    {
        public Texture2D Tex;
        public readonly Dictionary<string, (Rectangle R, bool Rot)> Sprites = new();
        public JToken Main;
        public readonly Dictionary<string, JToken> Symbols = new();
        public readonly Dictionary<int, List<(Rectangle R, bool Rot, Matrix M)>> Cache = new();
        public float BX, BY, BW, BH;   // whole timeline bounds
        public int Frames;
    }
    private LipAtlas _lipDefault, _lipYunjin;
    private bool _lipTried;
    private readonly Dictionary<Character, (string Anim, Vector2 Off, float Ang, int Frame, bool Sing)> _lipState = new();
    private static readonly bool _lipLog = Environment.GetEnvironmentVariable("FNF_LIPLOG") == "1";
    private static readonly bool _lipNoSplit = Environment.GetEnvironmentVariable("FNF_LIP_NOSPLIT") == "1";   // dev: old mouth-on-top drawing

    private static readonly Dictionary<string, Dictionary<string, (float X, float Y, float A)>> LipOffsets = new()
    {
        ["sserafim-yunjin"] = new() { ["idle"] = (8, 6, 23), ["singUP"] = (6, 8, 22), ["singRIGHT"] = (6, 8, 23), ["singDOWN"] = (8, 6, 23), ["singLEFT"] = (6, 8, 23) },
        ["sserafim-kazuha"] = new() { ["idle"] = (5, 4, -13), ["singUP"] = (7, 2, -14), ["singRIGHT"] = (7, 2, -13), ["singDOWN"] = (4, 6, -12), ["singLEFT"] = (5, 4, -14) },
        ["sserafim-chaewon"] = new() { ["idle"] = (41, 3, -166), ["singUP"] = (38, 0, -168), ["singRIGHT"] = (39, 1, -165), ["singDOWN"] = (41, 3, -167), ["singLEFT"] = (40, 2, -165) },
        ["sserafim-eunchae"] = new() { ["idle"] = (43, 6, -168), ["singUP"] = (45, 10, -166), ["singRIGHT"] = (42, 5, -166), ["singDOWN"] = (41, 3, -168), ["singLEFT"] = (43, 6, -169) },
        ["sserafim-sakura"] = new() { ["idle"] = (7, 2, -14), ["singUP"] = (8, 1, -15), ["singRIGHT"] = (7, 2, -15), ["singDOWN"] = (6, 3, -15), ["singLEFT"] = (7, 2, -14),
                                      ["singUP-joint"] = (10, -1, -14), ["singRIGHT-joint"] = (6, 3, -15), ["singDOWN-joint"] = (5, 5, -15), ["singLEFT-joint"] = (7, 2, -16) },
    };

    private static string LipId(Character c) => (c.OriginalName ?? c.Name ?? "").ToLowerInvariant();

    private LipAtlas LipLoad(string dir)
    {
        try
        {
            string root = Assets.ResolvePath($"images/sserafim/{dir}/Animation.json") ?? Path.Combine(AppContext.BaseDirectory, "Content", "images", "sserafim", dir, "Animation.json");
            if (!global::FNF_MonoGame.Engine.FsCI.FileExists(root)) { Console.WriteLine($"[LIP] missing {root}"); return null; }
            string folder = Path.GetDirectoryName(root);
            var la = new LipAtlas();
            var an = JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(root).Replace("\uFEFF", ""));
            var sm = JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(Path.Combine(folder, "spritemap1.json")).Replace("\uFEFF", ""));
            foreach (var sp in sm["ATLAS"]["SPRITES"])
            {
                var s = sp["SPRITE"];
                la.Sprites[(string)s["name"]] = (new Rectangle((int)s["x"], (int)s["y"], (int)s["w"], (int)s["h"]), (bool?)s["rotated"] ?? false);
            }
            la.Tex = Assets.LoadTexture($"images/sserafim/{dir}/spritemap1.png");
            la.Main = an["AN"]["TL"];
            foreach (var sym in an["SD"]?["S"] ?? new JArray()) la.Symbols[(string)sym["SN"]] = sym["TL"];
            la.Frames = LipDuration(la.Main);
            // whole-timeline bounds (Timeline.getWholeBounds)
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int t = 0; t < la.Frames; t++)
                foreach (var (r, rot, m) in LipParts(la, t))
                {
                    float w = rot ? r.Height : r.Width, h = rot ? r.Width : r.Height;
                    foreach (var c in new[] { Vector2.Zero, new Vector2(w, 0), new Vector2(0, h), new Vector2(w, h) })
                    {
                        // m includes the rotated-atlas prefix; transform the unrotated corners
                        var p = Vector2.Transform(c, LipUnrotated(m, rot, r));
                        x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y);
                    }
                }
            la.BX = x0; la.BY = y0; la.BW = x1 - x0; la.BH = y1 - y0;
            Console.WriteLine($"[LIP] {dir} frames={la.Frames} sprites={la.Sprites.Count} bounds={la.BX:F2},{la.BY:F2},{la.BW:F2},{la.BH:F2}");
            return la;
        }
        catch (Exception ex) { Console.WriteLine($"[LIP] load {dir}: {ex.Message}"); return null; }
    }

    // m maps atlas-space pixels; for rotated sprites undo the prefix to get the unrotated-space matrix
    private static Matrix LipUnrotated(Matrix m, bool rot, Rectangle r) => rot ? Matrix.Invert(LipRotPrefix(r)) * m : m;
    private static Matrix LipRotPrefix(Rectangle r) => new Matrix(0, -1, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, r.Width, 0, 1);

    private static int LipDuration(JToken tl)
    {
        int d = 0;
        foreach (var l in tl["L"]) foreach (var f in l["FR"]) d = Math.Max(d, (int)f["I"] + (int)f["DU"]);
        return d;
    }

    private static Matrix LipMatrix(JToken e)
    {
        var m3 = e["M3D"] as JArray;
        if (m3 != null && m3.Count >= 14) return new Matrix((float)m3[0], (float)m3[1], 0, 0, (float)m3[4], (float)m3[5], 0, 0, 0, 0, 1, 0, (float)m3[12], (float)m3[13], 0, 1);
        var mx = e["MX"] as JArray;
        if (mx != null && mx.Count >= 6) return new Matrix((float)mx[0], (float)mx[1], 0, 0, (float)mx[2], (float)mx[3], 0, 0, 0, 0, 1, 0, (float)mx[4], (float)mx[5], 0, 1);
        return Matrix.Identity;
    }

    private List<(Rectangle R, bool Rot, Matrix M)> LipParts(LipAtlas la, int tick)
    {
        if (la.Cache.TryGetValue(tick, out var c)) return c;
        c = new List<(Rectangle, bool, Matrix)>();
        LipCollect(la, la.Main, tick, Matrix.Identity, c, 0);
        la.Cache[tick] = c;
        return c;
    }

    private void LipCollect(LipAtlas la, JToken tl, int tick, Matrix parent, List<(Rectangle, bool, Matrix)> o, int depth)
    {
        if (depth > 8) return;
        var layers = tl["L"].ToList();
        for (int li = layers.Count - 1; li >= 0; li--)   // last layer drawn first
        {
            foreach (var f in layers[li]["FR"])
            {
                int i = (int)f["I"], du = (int)f["DU"];
                if (tick < i || tick >= i + du) continue;
                foreach (var e in f["E"])
                {
                    if (e["ASI"] is JToken asi)
                    {
                        if (la.Sprites.TryGetValue((string)asi["N"], out var sp))
                        {
                            var m = LipMatrix(asi) * parent;
                            o.Add((sp.R, sp.Rot, sp.Rot ? LipRotPrefix(sp.R) * m : m));
                        }
                    }
                    else if (e["SI"] is JToken si && la.Symbols.TryGetValue((string)si["SN"] ?? "", out var stl))
                    {
                        int dur = LipDuration(stl);
                        if (dur <= 0) continue;
                        int ff = (int?)si["FF"] ?? 0, el = tick - i;
                        string lp = (string)si["LP"] ?? "LP";
                        int nt = lp == "SF" ? ff : lp == "PO" ? Math.Min(ff + el, dur - 1) : (ff + el) % dur;
                        LipCollect(la, stl, Math.Clamp(nt, 0, dur - 1), LipMatrix(si) * parent, o, depth + 1);
                    }
                }
                break;
            }
        }
    }

    /// <summary>Per frame (SsfUpdate): SserafimLipSyncSprite.update + playAnimation offsets.</summary>
    private void LipUpdate()
    {
        if (!_lipTried) { _lipTried = true; _lipDefault = LipLoad("sserafim-lipsync"); _lipYunjin = LipLoad("sserafim-lipsync-yunjin"); }
        for (int i = 0; i < 5; i++)
        {
            var c = SsfGirl(i);
            if (c == null || !LipOffsets.TryGetValue(LipId(c), out var map)) continue;
            _lipState.TryGetValue(c, out var st);
            string anim = c.CurrentAnimation;
            if (anim != st.Anim) { st.Anim = anim; if (anim != null && map.TryGetValue(anim, out var d)) { st.Off = new Vector2(d.X, d.Y); st.Ang = d.A; } }
            bool sing = _ssfSinging[i];                               // shouldSing = characterType == BF
            if (st.Sing && !sing) st.Frame = 0;                        // set_shouldSing(false): curFrame = 0
            st.Sing = sing;
            if (sing) st.Frame = Math.Max(0, (int)Math.Floor(SongPositionSeconds * 24.0) - 1);
            _lipState[c] = st;
        }
    }

    /// <summary>True when the girl's current animation is pre-rendered in two pieces around the mouth element
    /// (SpriteSheet.AnchorSplit / CompositeAbove) and the lip sprite will be drawn between them.</summary>
    private bool LipSplitActive(Character c)
    {
        if (_lipNoSplit || !_ssfActive || c?._sprite?.Sheet == null || !_lipState.ContainsKey(c)) return false;
        var la = LipId(c) == "sserafim-yunjin" ? _lipYunjin : _lipDefault;
        string an = c._sprite.ResolvedAnimName;
        return la?.Tex != null && !string.IsNullOrEmpty(an) && c._sprite.Sheet.CompositeAbove.ContainsKey(an);
    }

    /// <summary>Between the parts below and above the mouth layer (DrawStageCharacter).</summary>
    private void LipDraw(SpriteBatch sb, Character c, int charType)
    {
        if (!_ssfActive || c?._sprite?.Sheet == null || !_lipState.TryGetValue(c, out var st)) return;
        var spr = c._sprite;
        var la = LipId(c) == "sserafim-yunjin" ? _lipYunjin : _lipDefault;
        if (la?.Tex == null) return;
        string an = spr.ResolvedAnimName;
        if (string.IsNullOrEmpty(an) || !spr.Sheet.Anchors.TryGetValue(an, out var ticks) || ticks.Count == 0) return;
        var anchors = ticks[Math.Clamp(spr.FrameIndex, 0, ticks.Count - 1)];
        var tb = spr.Sheet.GetTimelineBounds(an);
        if (anchors.Count == 0 || tb == null) return;
        Effect fx = SsfBeginShader(sb, _ssfChar, true) ? SsfEffect : null;   // characterShader uniforms (batch restarted)
        sb.End();
        var parts = LipParts(la, Math.Clamp(st.Frame, 0, la.Frames - 1));
        var origin = new Vector2((int)la.BW / 2f, (int)la.BH / 2f);
        foreach (var a in anchors)
        {
            // FlxSpriteElement: position = parentMatrix(0, 0), angle += atan2(b, a) (no parent scale)
            var p = spr.Position + new Vector2((a.TX - tb.Value.X) * spr.Scale.X, (a.TY - tb.Value.Y) * spr.Scale.Y);
            float theta = MathHelper.ToRadians(st.Ang) + MathF.Atan2(a.B, a.A);
            // FlxAnimate.drawAnimate: translate(-bounds) translate(-origin) rotate(angle) translate(screen + origin - offset)
            var place = Matrix.CreateTranslation(-la.BX - origin.X, -la.BY - origin.Y, 0) * Matrix.CreateRotationZ(theta)
                      * Matrix.CreateTranslation(p.X + origin.X - st.Off.X, p.Y + origin.Y - st.Off.Y, 0);
            if (_lipLog) { var sp = Vector2.Transform(p, _worldZoomMatrix); Console.WriteLine($"[LIPSCR] t={SongPositionSeconds:F3} {LipId(c)} anim={an}:{spr.FrameIndex} screen={sp.X:F0},{sp.Y:F0} split={(LipSplitActive(c) ? 1 : 0)}"); }
            if (_lipLog) Console.WriteLine($"[LIP] {LipId(c)} anim={an}:{spr.FrameIndex} P={p.X:F1},{p.Y:F1} rot={MathHelper.ToDegrees(MathF.Atan2(a.B, a.A)):F1} ang={st.Ang} off={st.Off} f={st.Frame}");
            foreach (var (r, rot, m) in parts)
            {
                sb.Begin(samplerState: _worldSampler, blendState: _worldBlendState, transformMatrix: m * place * _worldZoomMatrix, rasterizerState: _worldClipRaster, effect: fx);
                sb.Draw(la.Tex, Vector2.Zero, r, Color.White);
                sb.End();
            }
        }
        sb.Begin(samplerState: _worldSampler, blendState: _worldBlendState, transformMatrix: _worldZoomMatrix, rasterizerState: _worldClipRaster);
    }
}
