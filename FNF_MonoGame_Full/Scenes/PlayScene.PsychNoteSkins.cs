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
/// Psych Engine 1.0.4 per-note skins (objects/Note.hx reloadNote 358-423, loadNoteAnims 433-446):
///  - note.texture = 'X': Paths.getSparrowAtlas(X), anim colArray[noteData] + '0', setGraphicSize(width * 0.7), rgbShader disabled.
///  - rgbShader colours (Hurt Note r/g/b, scripts writing rgbShader.r/g/b): Psych NOTE_assets drawn through the
///    RGBPalette shader (shaders/RGBPalette.hx; Shaders/RGBPalette.fx).
/// Notes without either keep the port's default note art.
/// </summary>
public partial class PlayScene
{
    private readonly Dictionary<string, SpriteSheet> _psychNoteSheets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SpriteFrame> _psychNoteFrameCache = new();
    private Effect _rgbPaletteEffect; private bool _rgbPaletteTried;
    private readonly float[] _psychSplashAlpha = { 1f, 1f, 1f, 1f };

    private Effect RgbPaletteEffect
    {
        get
        {
            if (_rgbPaletteEffect != null || _rgbPaletteTried) return _rgbPaletteEffect;
            _rgbPaletteTried = true;
            try
            {
                string path = Assets.ResolvePath("shaders/RGBPalette.mgfx") ?? System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot, "shaders", "RGBPalette.mgfx");
                if (global::FNF_MonoGame.Engine.FsCI.FileExists(path)) _rgbPaletteEffect = new Effect(Game.GraphicsDevice, global::FNF_MonoGame.Engine.FsCI.ReadAllBytes(path));
            }
            catch (Exception ex) { Console.WriteLine($"[SHADER] RGBPalette load failed: {ex.Message}"); }
            return _rgbPaletteEffect;
        }
    }

    /// <summary>Paths.getSparrowAtlas(key) for note skins: mod images first, then Psych's shared images.</summary>
    private SpriteSheet PsychNoteSheet(string key)
    {
        if (_psychNoteSheets.TryGetValue(key, out var s)) return s;
        s = null;
        try
        {
            string mod = PsychMods.ModFolders("images/" + key + ".png");
            if (mod != null) s = SpriteSheet.Load(Game, mod.Substring(0, mod.Length - 4), sparrowOnly: true);
            s ??= SpriteSheet.Load(Game, "images/psych/" + key, sparrowOnly: true);
        }
        catch (Exception ex) { Console.WriteLine($"[PSYCH-NOTE] skin '{key}': {ex.Message}"); }
        if (s == null) Console.WriteLine($"[PSYCH-NOTE] note skin not found: images/{key}.png");
        _psychNoteSheets[key] = s;
        return s;
    }

    /// <summary>First frame of animation.addByPrefix(prefix) (frames sorted by their trailing number).</summary>
    private SpriteFrame PsychPrefixFrame(string key, SpriteSheet sheet, string prefix)
    {
        string ck = key + "|" + prefix;
        if (_psychNoteFrameCache.TryGetValue(ck, out var f)) return f;
        f = sheet?.Frames.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(kv => { var n = kv.Key; int i = n.Length; while (i > prefix.Length && char.IsDigit(n[i - 1])) i--; return int.TryParse(n.Substring(i), out int v) ? v : 0; })
            .Select(kv => kv.Value).FirstOrDefault();
        _psychNoteFrameCache[ck] = f;
        return f;
    }

    /// <summary>Draws a Psych note head with a custom texture or custom RGB colours. Returns false when the default art applies.</summary>
    private bool PsychDrawNoteHead(SpriteBatch sb, int x, int y, int size, int lane, PNoteV note, float alpha, float angle, float scale)
    {
        if (!PsychNotes || note == null || lane < 0 || lane > 3) return false;
        string tex = note.Texture;
        bool custom = !string.IsNullOrEmpty(tex);
        uint[] rgb = custom ? null : (note.Data.Rgb ?? PsychNoteSkin.DefaultRgb(lane));   // default skin: arrowRGB of the lane
        string key = custom ? tex : UserNoteSkinKey;   // Options > Note Skin (Chip / Future / mod skins)
        var sheet = PsychNoteSheet(key);
        var frame = PsychPrefixFrame(key, sheet, PsychNoteSkin.ColArray[lane] + "0");
        if (frame == null || frame.Rotated) return false;
        var texture = frame.Texture ?? sheet.Texture;
        if (texture == null) return false;
        float s = 0.7f * scale;   // loadNoteAnims: setGraphicSize(width * 0.7)
        int fw = frame.FrameWidth > 0 ? frame.FrameWidth : frame.SourceRect.Width;
        int fh = frame.FrameHeight > 0 ? frame.FrameHeight : frame.SourceRect.Height;
        // updateHitbox after setGraphicSize: the scaled frame's top-left sits at note.x (= strum.x + offsetX);
        // origin = frame centre (rotation about it)
        var origin = new Vector2(fw / 2f + frame.Offset.X * -1f, fh / 2f + frame.Offset.Y * -1f);
        var pos = new Vector2(x + fw * 0.7f / 2f, y + fh * 0.7f / 2f);
        var col = new Color(Color.White, Math.Clamp(alpha, 0f, 1f));
        bool useFx = rgb != null && !note.Data.RgbDisabled && RgbPaletteEffect != null;
        if (useFx)
        {
            var fx = RgbPaletteEffect;
            Vector3 C(uint c) => new(((c >> 16) & 0xFF) / 255f, ((c >> 8) & 0xFF) / 255f, (c & 0xFF) / 255f);
            fx.Parameters["r"]?.SetValue(C(rgb[0]));
            fx.Parameters["g"]?.SetValue(C(rgb[1]));
            fx.Parameters["b"]?.SetValue(C(rgb[2]));
            fx.Parameters["mult"]?.SetValue(1f);
            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp, null, null, fx, _hudBatchMatrix);
        }
        sb.Draw(texture, pos, frame.SourceRect, col, angle, origin, s, SpriteEffects.None, 0f);
        if (useFx)
        {
            sb.End();
            sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied, transformMatrix: _hudBatchMatrix);
        }
        return true;
    }
}
