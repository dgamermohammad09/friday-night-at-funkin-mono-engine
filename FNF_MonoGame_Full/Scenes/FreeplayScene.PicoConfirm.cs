using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json.Linq;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

public partial class FreeplayScene
{
    /// <summary>
    /// backcard-pico.hxc confirmAtlas, replayed like flixel-animate (swfMode:false): per-element ColorTransform
    /// multipliers, OpenFL blend ints (0 ADD, 10 NORMAL, 12 SCREEN), loop rules, MovieClips frozen on frame 0,
    /// scaleElement(el, 2, 100) on 7 symbols, and the anchor = UNscaled whole-timeline bounds (flixel-animate
    /// caches Timeline._bounds at load, before the script scales the elements).
    /// Drawn part by part instead of pre-rendered: the scaled cells (~2583x3705 x 31 frames) do not fit an atlas.
    /// </summary>
    /// <remarks>Generic flixel-animate (swfMode:false) timeline replay; also used for backing-text-yeah.</remarks>
    private sealed class PicoConfirmCard
    {
        private struct Part { public SpriteFrame F; public Matrix M; public Vector4 Mul; public int Blend; public string Name; }
        /// <summary>FunkinSprite.replaceSymbolGraphic: atlas sprite name -> replacement texture (drawn whole).</summary>
        public readonly Dictionary<string, Texture2D> Override = new(StringComparer.Ordinal);
        private readonly List<Part>[] _ticks;
        private readonly Texture2D _tex;
        public readonly Vector2 BoundsMin;
        public int FrameCount => _ticks.Length;

        public static readonly HashSet<string> PicoScaledSymbols = new(StringComparer.Ordinal)
            { "backplate flat blue", "pink back", "black flash", "white flash", "blue flash", "blue flash 2", "pink flash" };
        private readonly HashSet<string> ScaledSymbols;

        // OpenFL SCREEN = (ONE, ONE_MINUS_SRC_COLOR) on premultiplied source; tint is premultiplied below.
        private static readonly BlendState ScreenBlend = new()
        {
            ColorSourceBlend = Blend.One, ColorDestinationBlend = Blend.InverseSourceColor,
            AlphaSourceBlend = Blend.One, AlphaDestinationBlend = Blend.InverseSourceAlpha
        };

        public PicoConfirmCard(SpriteSheet sheet, string animationJsonPath, HashSet<string> scaledSymbols = null)
        {
            ScaledSymbols = scaledSymbols ?? new HashSet<string>();
            _tex = sheet.Texture;
            var root = JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(animationJsonPath));
            var syms = new Dictionary<string, JToken>(StringComparer.Ordinal);
            foreach (var s in root["SD"]?["S"] ?? new JArray()) syms[(string)s["SN"]] = s;
            JToken main = root["AN"]["TL"];
            int n = Duration(main);

            float minX = float.MaxValue, minY = float.MaxValue;
            for (int t = 0; t < n; t++)
                foreach (var p in Flatten(main, syms, sheet, t, false))
                {
                    float w = p.F.SourceRect.Width, h = p.F.SourceRect.Height;
                    foreach (var v in new[] { Vector2.Zero, new Vector2(w, 0), new Vector2(0, h), new Vector2(w, h) })
                    {
                        var q = Vector2.Transform(v, p.M);
                        if (q.X < minX) minX = q.X;
                        if (q.Y < minY) minY = q.Y;
                    }
                }
            BoundsMin = new Vector2(minX, minY);

            _ticks = new List<Part>[n];
            for (int t = 0; t < n; t++) _ticks[t] = Flatten(main, syms, sheet, t, true);
        }

        private static int Duration(JToken tl)
        {
            int d = 0;
            foreach (var l in tl["L"]) foreach (var f in l["FR"]) d = Math.Max(d, (int)f["I"] + (int)f["DU"]);
            return d;
        }

        private static JToken FrameAt(JToken layer, int t)
        {
            foreach (var f in layer["FR"]) { int i = (int)f["I"]; if (t >= i && t < i + (int)f["DU"]) return f; }
            return null;
        }

        // Animate [a,b,c,d,tx,ty] -> XNA row-vector matrix (world = local * parent)
        private static Matrix ReadMx(JToken inst)
        {
            var m = inst["MX"];
            if (m != null)
                return new Matrix((float)m[0], (float)m[1], 0, 0, (float)m[2], (float)m[3], 0, 0, 0, 0, 1, 0, (float)m[4], (float)m[5], 0, 1);
            var m3 = inst["M3D"];
            if (m3 is JArray a3 && a3.Count >= 14)
                return new Matrix((float)a3[0], (float)a3[1], 0, 0, (float)a3[4], (float)a3[5], 0, 0, 0, 0, 1, 0, (float)a3[12], (float)a3[13], 0, 1);
            if (m3 is JObject o3)
                return new Matrix((float)o3["m00"], (float)o3["m01"], 0, 0, (float)o3["m10"], (float)o3["m11"], 0, 0, 0, 0, 1, 0, (float)o3["m30"], (float)o3["m31"], 0, 1);
            return Matrix.Identity;
        }

        // FunkinSprite.scaleElement(el, 2, 100): a += 2; d += 2; tx -= TRP(0)*2 + 100; ty -= 100
        private static Matrix ScaleEl(Matrix m, bool apply)
        {
            if (!apply) return m;
            m.M11 += 2f; m.M22 += 2f; m.M41 -= 100f; m.M42 -= 100f;
            return m;
        }

        private List<Part> Flatten(JToken main, Dictionary<string, JToken> syms, SpriteSheet sheet, int tick, bool scaled)
        {
            var outp = new List<Part>();
            Walk(main, tick, Matrix.Identity, Vector4.One, 10, null);
            return outp;

            void Walk(JToken tl, int t, Matrix parent, Vector4 mul, int blend, string symName)
            {
                var layers = (JArray)tl["L"];
                for (int li = layers.Count - 1; li >= 0; li--)          // JSON layer 0 = top -> drawn last
                {
                    var fr = FrameAt(layers[li], t);
                    if (fr == null) continue;
                    int fi = (int)fr["I"];
                    var els = (JArray)fr["E"];
                    for (int ei = 0; ei < els.Count; ei++)
                    {
                        // getFirstElement(sym) == the first element of the symbol's first frame
                        bool scaleThis = scaled && symName != null && ScaledSymbols.Contains(symName) && li == 0 && fi == 0 && ei == 0;
                        var e = els[ei];
                        Matrix local;
                        if (e["ASI"] is JToken asi)
                        {
                            if (!sheet.Frames.TryGetValue((string)asi["N"], out var f)) continue;
                            local = ScaleEl(ReadMx(asi), scaleThis);
                            if (f.Rotated) // atlas stored rotated 90 deg CW: unrotated (x,y) -> atlas
                                local = new Matrix(0, -1, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, f.SourceRect.Width, 0, 1) * local;
                            outp.Add(new Part { F = f, M = local * parent, Mul = mul, Blend = blend, Name = (string)asi["N"] });
                        }
                        else if (e["SI"] is JToken si && syms.TryGetValue((string)si["SN"], out var sym))
                        {
                            JToken stl = sym["TL"];
                            int dur = Math.Max(1, Duration(stl));
                            int ff = (int?)si["FF"] ?? 0;
                            int ft = ff + (t - fi);
                            string st = (string)si["ST"] ?? "G";
                            string lp = (string)si["LP"] ?? "LP";
                            if (st == "MC" || st == "movieclip") ft = 0;              // swfMode = false
                            else if (lp == "PO" || lp == "playonce") ft = Math.Min(ft, dur - 1);
                            else if (lp == "SF" || lp == "singleframe") ft = ff;
                            else ft = ((ft % dur) + dur) % dur;

                            var m2 = mul;
                            if (si["C"] is JToken c)
                                m2 *= new Vector4((float?)c["RM"] ?? 1f, (float?)c["GM"] ?? 1f, (float?)c["BM"] ?? 1f, (float?)c["AM"] ?? 1f);
                            int b = (int?)si["B"] ?? 10;
                            int nb = b == 10 ? blend : b;                               // Blend.resolve: NORMAL inherits
                            local = ScaleEl(ReadMx(si), scaleThis);
                            Walk(stl, ft, local * parent, m2, nb, (string)si["SN"]);
                        }
                    }
                }
            }
        }

        /// <summary>Call with NO active SpriteBatch. Timeline point p is drawn at pos + (p - bounds.min).</summary>
        public void Draw(SpriteBatch sb, int frame, Vector2 pos)
        {
            var place = Matrix.CreateTranslation(pos.X - BoundsMin.X, pos.Y - BoundsMin.Y, 0f);
            foreach (var p in _ticks[Math.Clamp(frame, 0, _ticks.Length - 1)])
            {
                if (p.Mul.W <= 0f) continue;                       // SymbolInstance.draw: alphaMultiplier <= 0 -> skip
                BlendState bs = p.Blend == 0 ? BlendState.Additive : p.Blend == 12 ? ScreenBlend : BlendState.NonPremultiplied;
                Color tint = p.Blend == 12
                    ? new Color(p.Mul.X * p.Mul.W, p.Mul.Y * p.Mul.W, p.Mul.Z * p.Mul.W, p.Mul.W)
                    : new Color(p.Mul.X, p.Mul.Y, p.Mul.Z, p.Mul.W);
                sb.Begin(SpriteSortMode.Immediate, bs, SamplerState.LinearClamp, null, RasterizerState.CullNone, null, p.M * place);
                if (p.Name != null && Override.TryGetValue(p.Name, out var ov) && ov != null) sb.Draw(ov, Vector2.Zero, tint);
                else sb.Draw(_tex, Vector2.Zero, p.F.SourceRect, tint);
                sb.End();
            }
        }
    }
}
