using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json.Linq;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// funkin.ui.charSelect.Lock port: texture atlas charSelect/lock, the "color" layer of every symbol is replaced by a solid
/// tint (onSymbolCreate: setColorTransform(0,0,0,1, tint.r,g,b, 0)) with the per-slot colour, offset (230,110),
/// antialiasing on. Anims = main-timeline labels idle 0..2 / selected 3..7 / clicked 8..19 at 24 fps, played once
/// (FlxAnimation semantics); CharSelectSubState.updateLockAnims re-plays "idle" every frame on unselected locks, so a finished
/// idle restarts. Placement = flixel-animate: screen = (x - offset) + (p - timeline bounds min); bounds traced from the running
/// original (Lock.timeline._bounds = (-239.15, -103.094, 656 x 349.677), whole main timeline incl. the unlock frames).
/// </summary>
public partial class CharacterSelectScene
{
    private static readonly Color[] LockColors =
    {
        new Color(0x31, 0xF2, 0xA5), new Color(0x20, 0xEC, 0xCD), new Color(0x24, 0xD9, 0xE8),
        new Color(0x20, 0xEC, 0xCD), new Color(0x20, 0xC8, 0xD4), new Color(0x20, 0x9B, 0xDD),
        new Color(0x20, 0x9B, 0xDD), new Color(0x23, 0x62, 0xC9), new Color(0x24, 0x3F, 0xB9)
    };
    private static readonly Vector2 LockBoundsMin = new(-239.15f, -103.094f);
    private static readonly Vector2 LockOffset = new(230f, 110f);
    private static readonly Dictionary<string, (int start, int len)> LockLabels = new()
    {
        ["idle"] = (0, 3), ["selected"] = (3, 5), ["clicked"] = (8, 12), ["unlock"] = (20, 75)
    };

    private sealed class LockTimeline
    {
        public struct Part { public Rectangle Src; public Matrix M; public bool Color; }
        public readonly List<Part>[] Ticks;
        public readonly Texture2D Tex, White;

        public LockTimeline(GraphicsDevice gd, SpriteSheet sheet, string animJson)
        {
            Tex = sheet.Texture;
            var px = new Color[Tex.Width * Tex.Height];
            Tex.GetData(px);
            for (int i = 0; i < px.Length; i++) px[i] = new Color((byte)255, (byte)255, (byte)255, px[i].A);
            White = new Texture2D(gd, Tex.Width, Tex.Height);
            White.SetData(px);

            var root = JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(animJson));
            var syms = new Dictionary<string, JToken>(StringComparer.Ordinal);
            foreach (var s in root["SD"]?["S"] ?? new JArray()) syms[(string)s["SN"]] = s;
            JToken main = root["AN"]["TL"];
            int n = 95; // idle/selected/clicked/unlock (20..94)
            Ticks = new List<Part>[n];
            for (int t = 0; t < n; t++)
            {
                var outp = new List<Part>();
                Walk(main, t, Matrix.Identity, false);
                Ticks[t] = outp;

                void Walk(JToken tl, int tick, Matrix parent, bool colorLayer)
                {
                    var layers = (JArray)tl["L"];
                    for (int li = layers.Count - 1; li >= 0; li--)
                    {
                        var fr = FrameAt(layers[li], tick);
                        if (fr == null) continue;
                        bool isColor = colorLayer || (string)layers[li]["LN"] == "color";
                        int fi = (int)fr["I"];
                        foreach (var e in (JArray)fr["E"])
                        {
                            if (e["ASI"] is JToken asi)
                            {
                                if (!sheet.Frames.TryGetValue((string)asi["N"], out var f)) continue;
                                Matrix local = ReadMx(asi);
                                if (f.Rotated) local = new Matrix(0, -1, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, f.SourceRect.Width, 0, 1) * local;
                                outp.Add(new Part { Src = f.SourceRect, M = local * parent, Color = isColor });
                            }
                            else if (e["SI"] is JToken si && syms.TryGetValue((string)si["SN"], out var sym))
                            {
                                JToken stl = sym["TL"];
                                int dur = Math.Max(1, Duration(stl));
                                int ff = (int?)si["FF"] ?? 0;
                                int ft = ff + (tick - fi);
                                string lp = (string)si["LP"] ?? "LP";
                                if (lp == "PO" || lp == "playonce") ft = Math.Min(ft, dur - 1);
                                else if (lp == "SF" || lp == "singleframe") ft = ff;
                                else ft = ((ft % dur) + dur) % dur;
                                Walk(stl, ft, ReadMx(si) * parent, isColor);
                            }
                        }
                    }
                }
            }
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
        private static Matrix ReadMx(JToken inst)
        {
            var m = inst["MX"];
            if (m != null) return new Matrix((float)m[0], (float)m[1], 0, 0, (float)m[2], (float)m[3], 0, 0, 0, 0, 1, 0, (float)m[4], (float)m[5], 0, 1);
            return Matrix.Identity;
        }
    }

    private sealed class LockAnim
    {
        public string Name = "idle"; public int Frame; public float Timer; public bool Finished;
        public void Play(string name, bool force = false)
        {
            // FlxAnimationController.play + FlxAnimation.play: same anim, not forced, not finished -> keep playing
            if (!force && Name == name && !Finished) return;
            Name = name; Frame = 0; Timer = 0f; Finished = false;
        }
        public void Update(float dt)
        {
            if (Finished) return;
            const float dur = 1f / 24f;
            int len = LockLabels[Name].len;
            Timer += dt;
            while (Timer > dur && !Finished)
            {
                Timer -= dur;
                if (Frame + 1 > len - 1) Finished = true; else Frame++;
            }
        }
        public int Tick => LockLabels[Name].start + Math.Clamp(Frame, 0, LockLabels[Name].len - 1);
    }

    private LockTimeline _lockTl;
    private SpriteSheet _lockSheet;
    private readonly LockAnim[] _lockAnims = new LockAnim[9];

    private void LoadLocks()
    {
        try
        {
            string json = Path.Combine(AssetManager.ContentRoot, "menus", "character_select", "lock", "Animation.json");
            _lockSheet = SpriteSheet.Load(Game, "menus/character_select/lock");
            if (_lockSheet?.Texture != null && global::FNF_MonoGame.Engine.FsCI.FileExists(json)) _lockTl = new LockTimeline(Game.GraphicsDevice, _lockSheet, json);
        }
        catch (Exception ex) { Console.WriteLine($"[CHARSEL] lock atlas: {ex.Message}"); _lockTl = null; }
        for (int i = 0; i < 9; i++) _lockAnims[i] = new LockAnim();
    }

    /// <summary>Lock anims advance in super.update (before updateLockAnims).</summary>
    private void UpdateLockSprites(float dt)
    {
        foreach (var a in _lockAnims) a?.Update(dt);
    }

    /// <summary>CharSelectSubState.updateLockAnims, Lock branch (member.ID == 1).</summary>
    private void UpdateLockStates(int sel, bool acceptPressed)
    {
        for (int i = 0; i < 9; i++)
        {
            if (!SlotShowsLock(i) || _lockAnims[i] == null) continue;
            if (_unlockStage >= 2 && i == _unlockIndex) continue; // unlock anim plays out
            var l = _lockAnims[i];
            if (i == sel)
            {
                if (l.Name == "idle") l.Play("selected");
                else if ((l.Name == "selected" || l.Name == "clicked") && acceptPressed) l.Play("clicked", true);
            }
            else l.Play("idle");
        }
    }

    /// <summary>Draws lock i with member position (x, y). Call with NO active SpriteBatch.</summary>
    private bool DrawLock(SpriteBatch sb, int i, float x, float y)
    {
        if (_lockTl == null || _lockAnims[i] == null) return false;
        var parts = _lockTl.Ticks[Math.Clamp(_lockAnims[i].Tick, 0, _lockTl.Ticks.Length - 1)];
        var place = Matrix.CreateTranslation(x - LockOffset.X - LockBoundsMin.X, y - LockOffset.Y - LockBoundsMin.Y, 0f);
        Color tint = LockColors[Math.Clamp(i, 0, LockColors.Length - 1)];
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp, null, RasterizerState.CullNone);
        foreach (var p in parts)
        {
            // decompose the affine part matrix (uniform scale + rotation, no skew in this atlas) into SpriteBatch params
            var m = p.M * place;
            float sx = MathF.Sqrt(m.M11 * m.M11 + m.M12 * m.M12);
            float rot = MathF.Atan2(m.M12, m.M11);
            float cos = MathF.Cos(rot), sin = MathF.Sin(rot);
            float sy = MathF.Abs(cos) > 0.5f ? m.M22 / cos : -m.M21 / sin;
            sb.Draw(p.Color ? _lockTl.White : _lockTl.Tex, new Vector2(m.M41, m.M42), p.Src, p.Color ? tint : Color.White,
                rot, Vector2.Zero, new Vector2(sx, sy), SpriteEffects.None, 0f);
        }
        sb.End();
        return true;
    }
}
