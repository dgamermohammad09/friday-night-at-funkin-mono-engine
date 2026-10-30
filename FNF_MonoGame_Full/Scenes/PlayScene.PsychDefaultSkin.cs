using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.PsychEngine;
using PNoteV = FNF_MonoGame.Engine.PsychEngine.Note;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Psych Engine 1.0.4 default note skin for Psych-mod songs: strums (objects/StrumNote.hx: 'arrowLEFT' static without
/// RGB, '&lt;dir&gt; press' / '&lt;dir&gt; confirm' with the RGBPalette shader, setGraphicSize(width*0.7), centerOffsets on every
/// anim), sustain pieces/ends ('&lt;col&gt; hold piece' / '&lt;col&gt; hold end', centred on the head) and note splashes
/// (objects/NoteSplash.hx: noteSplashes/noteSplashes(.json) or noteSplashData.texture / SONG.splashSkin, position
/// strum - (swagWidth*0.95, swagWidth), offset (10,10)+anim offsets, random variant + fps 22-26, alpha
/// noteSplashData.a, RGB = arrowRGB of the lane).
/// </summary>
public partial class PlayScene
{
    private static readonly string[] PsychDirs = { "LEFT", "DOWN", "UP", "RIGHT" };
    private const string PsychDefaultSkin = "noteSkins/NOTE_assets";
    private bool _psychStrumLogged;
    private string _userNoteSkinKey;

    /// <summary>Sheet of the player's Note Skin (Options / Skins menu: Psych "Note Skins", e.g. Chip / Future), the default
    /// NOTE_assets when none is picked or the sheet is missing (Note.defaultNoteSkin + getNoteSkinPostfix()).</summary>
    private string UserNoteSkinKey
    {
        get
        {
            if (_userNoteSkinKey != null) return _userNoteSkinKey;
            string k = NoteStyles.UserSheetKey;
            if (k != null && PsychNoteSheet(k) == null) { Console.WriteLine($"[PSYCH-SKIN] note skin '{k}' not installed - default"); k = null; }
            return _userNoteSkinKey = k ?? PsychDefaultSkin;
        }
    }

    private static Vector3 PsychRgbVec(uint c) => new(((c >> 16) & 0xFF) / 255f, ((c >> 8) & 0xFF) / 255f, (c & 0xFF) / 255f);

    /// <summary>Restart the HUD batch with the RGBPalette effect for the given colours (null = plain batch).</summary>
    private bool PsychBeginRgb(SpriteBatch sb, uint[] rgb)
    {
        var fx = rgb != null ? RgbPaletteEffect : null;
        sb.End();
        if (fx != null)
        {
            fx.Parameters["r"]?.SetValue(PsychRgbVec(rgb[0]));
            fx.Parameters["g"]?.SetValue(PsychRgbVec(rgb[1]));
            fx.Parameters["b"]?.SetValue(PsychRgbVec(rgb[2]));
            fx.Parameters["mult"]?.SetValue(1f);
            sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp, null, null, fx, _hudBatchMatrix);
            return true;
        }
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied, transformMatrix: _hudBatchMatrix);
        return false;
    }

    private void PsychEndRgb(SpriteBatch sb, bool used)
    {
        if (!used) return;
        sb.End();
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied, transformMatrix: _hudBatchMatrix);
    }

    private readonly Dictionary<string, List<SpriteFrame>> _psychAnimCache = new();
    private List<SpriteFrame> PsychAnim(string key, string prefix)
    {
        string ck = key + "|" + prefix;
        if (_psychAnimCache.TryGetValue(ck, out var l)) return l;
        var sh = PsychNoteSheet(key);
        l = sh?.Frames.Where(kv => kv.Key.Length > prefix.Length && kv.Key.StartsWith(prefix, StringComparison.Ordinal) && kv.Key.Substring(prefix.Length).All(char.IsDigit))
                .OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value).ToList();
        if (l != null && l.Count == 0) l = null;
        _psychAnimCache[ck] = l;
        return l;
    }

    private static float FW(SpriteFrame f) => f.FrameWidth > 0 ? f.FrameWidth : f.SourceRect.Width;
    private static float FH(SpriteFrame f) => f.FrameHeight > 0 ? f.FrameHeight : f.SourceRect.Height;

    /// <summary>StrumNote draw. x,y = strum position (Psych strum.x/y); returns false to keep the port art.</summary>
    private bool PsychDrawStrum(SpriteBatch sb, int x, int y, int lane, bool isPlayer, bool pressed, bool confirm, int confirmFrame)
    {
        if (!_psychStrumLogged) { _psychStrumLogged = true; Console.WriteLine($"[PSYCH-SKIN] strum psych={PsychNotes} anim={(PsychAnim(PsychDefaultSkin, "arrowLEFT")?.Count ?? -1)} sheet={(PsychNoteSheet(PsychDefaultSkin)?.Frames.Count ?? -1)}"); }
        if (!PsychNotes || lane < 0 || lane > 3) return false;
        if (ButtonsFor(isPlayer)) return PsychDrawButtonStrum(sb, x, y, lane, pressed, confirm);   // Note Skin = Controller Buttons (PlayScene.PsychButtonSkin.cs)
        string key = UserNoteSkinKey;
        var stat = PsychAnim(key, "arrow" + PsychDirs[lane]);
        if (stat == null) return false;
        var sh = PsychNoteSheet(key);
        List<SpriteFrame> frames = stat; string anim = "static";
        if (confirm) { var c = PsychAnim(key, PsychDirs[lane].ToLowerInvariant() + " confirm"); if (c != null) { frames = c; anim = "confirm"; } }
        else if (pressed) { var p = PsychAnim(key, PsychDirs[lane].ToLowerInvariant() + " press"); if (p != null) { frames = p; anim = "press"; } }
        int fi = anim == "static" ? 0 : Math.Clamp(confirmFrame, 0, frames.Count - 1);
        var fr = frames[fi];
        // setGraphicSize(width*0.7) + updateHitbox on the static frame; playAnim -> centerOffsets/centerOrigin:
        // the current frame (scale 0.7) is centred on the static hitbox centre.
        float w = (float)Math.Floor(FW(stat[0]) * 0.7f), h = FH(stat[0]) * (w / FW(stat[0]));
        float s = w / FW(stat[0]);
        float cx = x + w / 2f, cy = y + h / 2f;
        var pos = new Vector2(cx - FW(fr) / 2f * s + fr.Offset.X * s, cy - FH(fr) / 2f * s + fr.Offset.Y * s);
        if (Environment.GetEnvironmentVariable("FNF_SKINLOG") == "1" && lane == 0 && !isPlayer) Console.WriteLine($"[PSYCH-SKIN] strum0 x={x} y={y} pos={pos.X:F2},{pos.Y:F2} s={s:F4} hud={_hudBatchMatrix.M11:F4},{_hudBatchMatrix.M41:F1},{_hudBatchMatrix.M42:F1}");
        bool fx = anim != "static" && PsychBeginRgb(sb, PsychNoteSkin.DefaultRgb(lane));   // rgbShader.enabled = anim != 'static'
        float sa = Math.Clamp(StrumMods[lane + (isPlayer ? 4 : 0)].Alpha, 0f, 1f);   // StrumNote.alpha (Lua / generateStaticArrows)
        sb.Draw(fr.Texture ?? sh.Texture, pos, fr.SourceRect, new Color(Color.White, sa), 0f, Vector2.Zero, s, SpriteEffects.None, 0f);
        PsychEndRgb(sb, fx);
        return true;
    }

    /// <summary>Default-skin sustain body/end ('purple hold piece' / 'purple hold end', NOTE_assets + RGB). The body fills
    /// [top,bottom) at the head centre; the end cap keeps its far edge at the port's end position.</summary>
    private bool PsychDrawSustain(SpriteBatch sb, PNoteV note, int lane, int laneX, int top, int bottom, bool end, SpriteEffects flip, float alpha)
    {
        if (!PsychNotes || lane < 0 || lane > 3 || bottom <= top) return false;
        if (!string.IsNullOrEmpty(note?.Texture)) return false;
        string key = UserNoteSkinKey;
        var piece = PsychAnim(key, PsychNoteSkin.ColArray[lane] + (end ? " hold end" : " hold piece"));
        var head = PsychAnim(key, PsychNoteSkin.ColArray[lane]);
        var endA = PsychAnim(key, PsychNoteSkin.ColArray[lane] + " hold end");
        if (piece == null || head == null || endA == null) return false;
        var fr = piece[0];
        var sh = PsychNoteSheet(key);
        // offsetX = headWidth/2 - endWidth/2 (Note.hx sustain setup), widths after setGraphicSize(*0.7)
        float headW = FW(head[0]) * 0.7f, endW = FW(endA[0]) * 0.7f;
        float left = laneX + headW / 2f - endW / 2f;
        float wDraw = FW(fr) * 0.7f;
        Rectangle dst;
        if (end)
        {
            float eh = FH(fr) * 0.7f;
            bool down = (flip & SpriteEffects.FlipVertically) != 0;
            dst = down ? new Rectangle((int)Math.Round(left), top, (int)Math.Round(wDraw), (int)Math.Round(eh))
                       : new Rectangle((int)Math.Round(left), (int)Math.Round(bottom - eh), (int)Math.Round(wDraw), (int)Math.Round(eh));
        }
        else dst = new Rectangle((int)Math.Round(left), top, (int)Math.Round(wDraw), bottom - top);
        uint[] rgb = note?.Data.Rgb ?? PsychNoteSkin.DefaultRgb(lane);
        bool fx = !(note?.Data.RgbDisabled ?? false) && PsychBeginRgb(sb, rgb);
        sb.Draw(fr.Texture ?? sh.Texture, dst, fr.SourceRect, new Color(Color.White, Math.Clamp(alpha, 0f, 1f)), 0f, Vector2.Zero, flip, 0f);
        PsychEndRgb(sb, fx);
        return true;
    }

    // ---- note splashes ----------------------------------------------------------------------------------------
    private sealed class PsychSplash { public int Lane, Data; public double Start; public float Fps, Alpha; public string Tex; public uint[] Rgb; public bool UseRgb; }
    private readonly List<PsychSplash> _psychSplashes = new();
    private readonly Dictionary<string, (Dictionary<int, (string prefix, float ox, float oy, int f0, int f1)> anims, float scale, bool allowRgb)> _psychSplashCfg = new();
    private readonly Random _psychSplashRng = new();

    private (Dictionary<int, (string prefix, float ox, float oy, int f0, int f1)> anims, float scale, bool allowRgb) PsychSplashConfig(string tex)
    {
        if (_psychSplashCfg.TryGetValue(tex, out var c)) return c;
        var anims = new Dictionary<int, (string, float, float, int, int)>();
        float scale = 1f; bool allowRgb = true;
        try
        {
            string path = PsychMods.ModFolders("images/" + tex + ".json") ?? Assets.ResolvePath("images/psych/" + tex + ".json");
            if (path != null && global::FNF_MonoGame.Engine.FsCI.FileExists(path))
            {
                var j = Newtonsoft.Json.Linq.JObject.Parse(PsychMods.SanitizeJson(global::FNF_MonoGame.Engine.FsCI.ReadAllText(path)));
                if (j["scale"] != null) scale = (float)j["scale"];
                if (j["allowRGB"] != null) allowRgb = (bool)j["allowRGB"];
                if (j["animations"] is Newtonsoft.Json.Linq.JObject an)
                    foreach (var p in an.Properties())
                    {
                        var a = p.Value;
                        int nd = (int)(a["noteData"] ?? 0);
                        var off = a["offsets"] as Newtonsoft.Json.Linq.JArray;
                        var fps = a["fps"] as Newtonsoft.Json.Linq.JArray;
                        anims[nd] = ((string)a["prefix"], off != null && off.Count > 0 ? (float)off[0] : 0, off != null && off.Count > 1 ? (float)off[1] : 0,
                                     fps != null && fps.Count > 0 ? (int)fps[0] : 22, fps != null && fps.Count > 1 ? (int)fps[1] : 26);
                    }
            }
        }
        catch (Exception ex) { Console.WriteLine($"[PSYCH-NOTE] splash config '{tex}': {ex.Message}"); }
        if (anims.Count == 0)   // NoteSplash.createConfig fallback: 'note splash <col> <n>' for every found variant
            for (int v = 1; v <= 2; v++)
                for (int i = 0; i < 4; i++)
                    if (PsychAnim(tex, $"note splash {PsychNoteSkin.ColArray[i]} {v}") != null) anims[i + 4 * (v - 1)] = ($"note splash {PsychNoteSkin.ColArray[i]} {v}", 0, 0, 22, 26);
        c = (anims, scale, allowRgb);
        _psychSplashCfg[tex] = c;
        return c;
    }

    /// <summary>PlayState.spawnNoteSplashOnNote -> NoteSplash.spawnSplashNote.</summary>
    private void PsychSpawnSplash(int lane, PNoteV note)
    {
        if (!PsychNotes || lane < 0 || lane > 3) return;
        if (note != null && note.Data.NoteSplashDisabled) return;
        string tex = note?.Data.SplashTexture;
        if (string.IsNullOrEmpty(tex)) tex = "noteSplashes/noteSplashes";   // (SONG.splashSkin is applied to noteSplashData.texture when notes are created)
        var cfg = PsychSplashConfig(tex);
        int maxAnims = Math.Max(1, cfg.anims.Count / 4);
        int nd = lane;
        if (maxAnims > 1) nd = lane % 4 + _psychSplashRng.Next(0, maxAnims) * 4;
        if (!cfg.anims.ContainsKey(nd)) nd = lane;
        int f0 = cfg.anims.TryGetValue(nd, out var an) ? an.f0 : 22, f1 = cfg.anims.TryGetValue(nd, out an) ? an.f1 : 26;
        _psychSplashes.Add(new PsychSplash
        {
            Lane = lane, Data = nd, Start = _conductor.SongPosition, Fps = _psychSplashRng.Next(Math.Max(0, f0), Math.Max(0, f1) + 1), Alpha = note?.NoteSplashAlpha ?? 0.6f,
            Tex = tex, Rgb = note?.Data.Rgb ?? PsychNoteSkin.DefaultRgb(lane), UseRgb = cfg.allowRgb && (note == null || note.NoteSplashUseRGB)
        });
    }

    /// <summary>Draws (and ages) the Psych splashes; strum positions as DrawNoteSplashes.</summary>
    private void PsychDrawSplashes(SpriteBatch sb, int playerStartX, int strumY)
    {
        for (int i = _psychSplashes.Count - 1; i >= 0; i--)
        {
            var s = _psychSplashes[i];
            var cfg = PsychSplashConfig(s.Tex);
            if (!cfg.anims.TryGetValue(s.Data, out var an)) { _psychSplashes.RemoveAt(i); continue; }
            var frames = PsychAnim(s.Tex, an.prefix);
            int fi = (int)Math.Max(0, (_conductor.SongPosition - s.Start) * s.Fps);   // animation time (song clock)
            if (frames == null || fi >= frames.Count) { _psychSplashes.RemoveAt(i); continue; }
            var fr = frames[fi];
            var sh = PsychNoteSheet(s.Tex);
            var sm = StrumMods[s.Lane + 4];   // NoteSplash.update follows babyArrow.x/y (scripted strum moves included)
            float sx = playerStartX + s.Lane * NOTE_SPACING + sm.OffsetX - NOTE_SPACING * 0.95f, sy = strumY + sm.OffsetY - NOTE_SPACING;
            float ox = 10 + an.ox, oy = 10 + an.oy, sc = cfg.scale;
            // flixel: x - offset + origin*(1-scale) + frameOffset*scale, origin = frame centre (scale 1 in the default config)
            var pos = new Vector2(sx - ox + FW(fr) / 2f * (1 - sc) + fr.Offset.X * sc, sy - oy + FH(fr) / 2f * (1 - sc) + fr.Offset.Y * sc);
            bool fx = s.UseRgb && PsychBeginRgb(sb, s.Rgb);
            sb.Draw(fr.Texture ?? sh.Texture, pos, fr.SourceRect, new Color(Color.White, Math.Clamp(s.Alpha, 0f, 1f)), 0f, Vector2.Zero, sc, SpriteEffects.None, 0f);
            PsychEndRgb(sb, fx);
        }
    }
}
