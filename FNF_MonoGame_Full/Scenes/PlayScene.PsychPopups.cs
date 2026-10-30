using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using CP = FNF_MonoGame.Engine.PsychEngine.ClientPrefs;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Psych Engine 1.0.4 PlayState.popUpScore 1:1 (Psych songs): rating + optional 'combo' sprite + lpad(combo, 3) digits in
/// comboGroup (camHUD, drawn below uiGroup / noteGroup), Paths.image(uiFolder + name + uiPostfix) art (mod images first,
/// then Psych's shared art in Content/images/psych), setGraphicSize(Std.int(w * 0.7 | 0.5 | pixelZoom ...)) + updateHitbox,
/// flixel motion, FlxTween alpha 0.2 s after crochet*0.001 / *0.002 (playbackRate-scaled). Runs in botplay too
/// (Psych calls popUpScore for cpuControlled hits).
/// </summary>
public partial class PlayScene
{
    private sealed class PsyPop
    {
        public Texture2D Tex; public float X, Y, Scale = 1f, VX, VY, AY, Alpha = 1f, Delay, Fade; public bool Visible, Aa, Done;
        public bool Rating; public string RatingName;
    }
    private readonly List<PsyPop> _psyPops = new(48);
    private readonly Dictionary<string, Texture2D> _psyPopTex = new();
    private bool _psyPopsDrawn;

    /// <summary>PlayState.set_stageUI: uiPrefix / uiPostfix from the stage's stageUI (or isPixelStage).</summary>
    internal static void PsychSetStageUI(string value)
    {
        value = string.IsNullOrWhiteSpace(value) ? "normal" : value.Trim();
        uiPrefix = uiPostfix = "";
        if (value != "normal")
        {
            uiPrefix = value.Split("-pixel")[0].Trim();
            if (value == "pixel" || value.EndsWith("-pixel")) uiPostfix = "-pixel";
        }
        stageUI = value;
    }

    private Texture2D PsyPopTexture(string name)
    {
        if (_psyPopTex.TryGetValue(name, out var t)) return t;
        t = LoadPsychUiTexture(name);
        if (t == Assets.Pixel) { Console.WriteLine($"[PSYCH-POP] images/{name}.png missing"); t = null; }
        _psyPopTex[name] = t;
        return t;
    }

    /// <summary>cachePopUpScore (PlayState.create): preload the rating / digit art for the current stageUI.</summary>
    private void PsyPopPreload()
    {
        _psyPopTex.Clear(); _psyPops.Clear();
        if (!PsychNotes) return;
        string ui = stageUI != "normal" ? uiPrefix + "UI/" : "";
        foreach (var r in new[] { "sick", "good", "bad", "shit" }) PsyPopTexture(ui + r + uiPostfix);
        for (int i = 0; i < 10; i++) PsyPopTexture(ui + "num" + i + uiPostfix);
        Console.WriteLine($"[PSYCH-POP] stageUI={stageUI} prefix='{uiPrefix}' postfix='{uiPostfix}'");
    }

    private static string PsyRatingImage(string rating) => rating switch
    {
        "SICK!!" => "sick", "GOOD!" => "good", "BAD" => "bad", "SHIT" => "shit", _ => "sick"
    };

    /// <summary>FlxSprite.loadGraphic + screenCenter + setGraphicSize(Std.int(width * k)) + updateHitbox.</summary>
    private static float PsyGraphicScale(Texture2D tex, double k)
    {
        int target = (int)(tex.Width * k);
        return tex.Width > 0 ? target / (float)tex.Width : 1f;
    }

    /// <summary>PlayState.popUpScore (Psych 1.0.4) after the judgement: builds rating / combo / number sprites.</summary>
    private void PsyPopUpScore(string rating, int combo)
    {
        if (!PsychNotes) return;
        var prefs = CP.Data;
        if (!prefs.ComboStacking && _psyPops.Count > 0) _psyPops.Clear();
        float rate = JudgeRate, rate2 = rate * rate;
        var rng = VsStatic.Rng;
        float placement = 1280f * 0.35f;
        bool pixel = isPixelStage;
        string uiFolder = ""; bool aa = prefs.Antialiasing;
        if (stageUI != "normal") { uiFolder = uiPrefix + "UI/"; aa = !pixel; }
        int[] off = prefs.ComboOffset != null && prefs.ComboOffset.Length >= 4 ? prefs.ComboOffset : new[] { 0, 0, 0, 0 };
        float crochetMs = (float)(_conductor.Crochet * 1000.0);
        const float daPixelZoom = 6f;

        PsyPop rs = null;
        var rt = PsyPopTexture(uiFolder + PsyRatingImage(rating) + uiPostfix);
        if (rt != null)
        {
            rs = new PsyPop { Tex = rt, Rating = true, RatingName = rating, Aa = aa };
            rs.Y = (720f - rt.Height) / 2f;           // screenCenter()
            rs.X = placement - 40f;
            rs.Y -= 60f;
            rs.AY = 550f * rate2;
            rs.VY -= rng.Next(140, 176) * rate;
            rs.VX -= rng.Next(0, 11) * rate;
            rs.Visible = !prefs.HideHud && showRating && PopupShowRating; // + port option Disable Combo Rating
            rs.X += off[0];
            rs.Y -= off[1];
            rs.Scale = PsyGraphicScale(rt, pixel ? 6.0 * 0.85 : 0.7);
            rs.Delay = crochetMs * 0.001f / rate;
            _psyPops.Add(rs);                          // comboGroup.add(rating)
        }

        PsyPop cs = null;
        var ct = PsyPopTexture(uiFolder + "combo" + uiPostfix);
        if (ct != null)
        {
            cs = new PsyPop { Tex = ct, Aa = aa };
            cs.Y = (720f - ct.Height) / 2f;
            cs.X = placement;
            cs.AY = rng.Next(200, 301) * rate2;
            cs.VY -= rng.Next(140, 161) * rate;
            cs.Visible = !prefs.HideHud && showCombo;
            cs.X += off[0];
            cs.Y -= off[1];
            cs.Y += 60f;
            cs.VX += rng.Next(1, 11) * rate;
            cs.Scale = PsyGraphicScale(ct, pixel ? daPixelZoom * 0.85 : 0.7);
            cs.Delay = crochetMs * 0.002f / rate;
            if (showCombo) _psyPops.Add(cs);
        }

        string sep = combo.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(3, '0');
        float xThing = 0f;
        for (int i = 0; i < sep.Length; i++)
        {
            int d = sep[i] - '0';
            if (d < 0 || d > 9) continue;
            var nt = PsyPopTexture(uiFolder + "num" + d + uiPostfix);
            if (nt == null) continue;
            var ns = new PsyPop { Tex = nt, Aa = aa };
            ns.X = (1280f - nt.Width) / 2f; ns.Y = (720f - nt.Height) / 2f;
            ns.X = placement + 43f * i - 90f + off[2];
            ns.Y += 80f - off[3];
            ns.Scale = PsyGraphicScale(nt, pixel ? daPixelZoom : 0.5);
            ns.AY = rng.Next(200, 301) * rate2;
            ns.VY -= rng.Next(140, 161) * rate;
            ns.VX = (float)(rng.NextDouble() * 10.0 - 5.0) * rate;
            ns.Visible = !prefs.HideHud && PopupShowCombo; // + port option Disable Combo Counter
            ns.Delay = crochetMs * 0.002f / rate;
            if (showComboNum) _psyPops.Add(ns);
            if (ns.X > xThing) xThing = ns.X;
        }
        if (cs != null) cs.X = xThing + 50f;
        if (s_psyPopLog)
            foreach (var p in _psyPops)
                if (p.Fade == 0f && p.Alpha == 1f && p.Delay > 0f)
                    Console.WriteLine($"PSYPOP spawn x={p.X:F3} y={p.Y:F3} w={p.Tex.Width * p.Scale:F2} h={p.Tex.Height * p.Scale:F2} fw={p.Tex.Width} sc={p.Scale:F5} vx={p.VX:F2} vy={p.VY:F2} ay={p.AY} delay={p.Delay:F3} vis={p.Visible} aa={p.Aa}");
    }
    /// <summary>FNF_DEV_POPFREEZE=2: every popup stays at its spawn position (pixel-exact checks vs a frozen original).</summary>
    internal static readonly bool s_popFreezeAll = Environment.GetEnvironmentVariable("FNF_DEV_POPFREEZE") == "2";
    private static readonly bool s_psyPopLog = Environment.GetEnvironmentVariable("FNF_NSLOG") == "1";

    private void PsyPopsUpdate(float dt)
    {
        for (int i = _psyPops.Count - 1; i >= 0; i--)
        {
            var p = _psyPops[i];
            if (s_popFreezeAll) { p.VX = p.VY = p.AY = 0f; }
            float dvy = 0.5f * p.AY * dt;               // FlxObject.updateMotion
            p.VY += dvy; p.Y += p.VY * dt; p.VY += dvy;
            p.X += p.VX * dt;
            float ft = dt;
            if (p.Delay > 0f) { p.Delay -= ft; if (p.Delay > 0f) continue; ft = -p.Delay; p.Delay = 0f; }
            p.Fade += ft;
            float t = Math.Min(1f, p.Fade / (0.2f / JudgeRate));
            p.Alpha = 1f - t;
            if (t >= 1f) _psyPops.RemoveAt(i);      // numScore/comboSpr.destroy(); rating is alpha 0 until then
        }
    }

    /// <summary>comboGroup draw (camHUD). Called from the Psych member order (comboGroup slot) or before the HUD.</summary>
    private void PsyPopsDraw(SpriteBatch sb)
    {
        _psyPopsDrawn = true;
        if (_psyPops.Count == 0) return;
        var m = _hudBatchMatrix;
        bool point = false;
        foreach (var p in _psyPops)
        {
            if (!p.Visible || p.Alpha <= 0f) continue;
            if (!p.Aa != point)
            {
                point = !p.Aa;
                sb.End();
                sb.Begin(samplerState: point ? SamplerState.PointClamp : SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied, transformMatrix: m);
            }
            Color tint = p.Rating ? PopupRatingTint(p.RatingName) : PopupComboTint();
            sb.Draw(p.Tex, new Vector2(p.X, p.Y), null, new Color(tint.R, tint.G, tint.B, (int)(p.Alpha * 255f + 0.5f)), 0f, Vector2.Zero, p.Scale, SpriteEffects.None, 0f);
        }
        if (point)
        {
            sb.End();
            sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied, transformMatrix: m);
        }
    }
}
