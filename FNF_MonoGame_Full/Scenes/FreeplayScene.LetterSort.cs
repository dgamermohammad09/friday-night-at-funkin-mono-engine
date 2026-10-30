using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json.Linq;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// LetterSort's FreeplayLetter (FunkinSprite.loadTextureAtlas('freeplay/sortedLetters')): plays the "&lt;X&gt; move" symbol
/// (paused on frame 0 unless it is the selected letter, which loops), getScreenPosition -50/-60, flixel-animate symbol
/// timeline bounds (draw = pos - bounds.min + M), scale 0.8 about the frame centre for the outer letters.
/// Spritemap pieces with rotated=true are stored rotated: drawn with -90 degrees (flixel-animate ANGLE_NEG_90).
/// </summary>
public partial class FreeplayScene
{
    private sealed class LetterKey { public int Index, Dur; public float Sx, Tx, Ty; }
    private sealed class LetterSym { public List<LetterKey> Keys = new(); public Rectangle Src; public bool Rot; public Vector2 Inner; public Vector2 BMin, BMax; public int Total; }
    private Dictionary<string, LetterSym> _letterSyms;
    private Texture2D _letterTex;
    private float _letterFps = 24f, _letterAnimTime;

    private void EnsureLetterSyms()
    {
        if (_letterSyms != null) return;
        _letterSyms = new Dictionary<string, LetterSym>(StringComparer.Ordinal);
        try
        {
            string root = System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot, "menus", "freeplay", "sortedLetters");
            var anim = JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(Path.Combine(root, "Animation.json")));
            var map = JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(Path.Combine(root, "spritemap1.json")));
            _letterTex = Assets.LoadTexture("menus/freeplay/sortedLetters/spritemap1.png");
            var rects = new Dictionary<string, (Rectangle r, bool rot)>();
            foreach (var s in map["ATLAS"]["SPRITES"])
            {
                var sp = s["SPRITE"];
                rects[(string)sp["name"]] = (new Rectangle((int)sp["x"], (int)sp["y"], (int)sp["w"], (int)sp["h"]), (bool)sp["rotated"]);
            }
            float? fps = (float?)anim["metadata"]?["framerate"];
            if (fps is > 0) _letterFps = fps.Value;
            var syms = new Dictionary<string, JToken>(StringComparer.Ordinal);
            foreach (var s in anim["SYMBOL_DICTIONARY"]["Symbols"]) syms[(string)s["SYMBOL_name"]] = s;
            foreach (var kv in syms)
            {
                if (!kv.Key.EndsWith(" move", StringComparison.Ordinal)) continue;
                var ls = new LetterSym();
                foreach (var f in kv.Value["TIMELINE"]["LAYERS"][0]["Frames"])
                {
                    var inst = f["elements"]?[0]?["SYMBOL_Instance"];
                    if (inst == null) continue;
                    var m = inst["Matrix3D"];
                    ls.Keys.Add(new LetterKey { Index = (int)f["index"], Dur = (int)f["duration"], Sx = (float)m["m00"], Tx = (float)m["m30"], Ty = (float)m["m31"] });
                    if (ls.Src.Width == 0 && syms.TryGetValue((string)inst["SYMBOL_name"], out var sub))
                    {
                        var a = sub["TIMELINE"]["LAYERS"][0]["Frames"][0]["elements"][0]["ATLAS_SPRITE_instance"];
                        var (r, rot) = rects[(string)a["name"]];
                        ls.Src = r; ls.Rot = rot;
                        ls.Inner = new Vector2((float)a["Matrix3D"]["m30"], (float)a["Matrix3D"]["m31"]);
                    }
                }
                if (ls.Keys.Count == 0 || ls.Src.Width == 0) continue;
                // whole symbol timeline bounds
                float w = ls.Rot ? ls.Src.Height : ls.Src.Width, h = ls.Rot ? ls.Src.Width : ls.Src.Height;
                ls.BMin = new Vector2(float.MaxValue); ls.BMax = new Vector2(float.MinValue);
                foreach (var k in ls.Keys)
                {
                    var p0 = new Vector2(k.Tx, k.Ty) + ls.Inner * k.Sx;
                    var p1 = p0 + new Vector2(w, h) * k.Sx;
                    ls.BMin = Vector2.Min(ls.BMin, p0); ls.BMax = Vector2.Max(ls.BMax, p1);
                    ls.Total = Math.Max(ls.Total, k.Index + k.Dur);
                }
                _letterSyms[kv.Key] = ls;
            }
        }
        catch (Exception ex) { Console.WriteLine("[LetterSort] " + ex.Message); }
    }

    private static string LetterMoveSymbol(string baseName) => baseName switch
    {
        "I L" => "IL move", "s" => "S move", "t" => "T move", _ => baseName + " move"
    };

    /// <summary>Draws one FreeplayLetter; (x, y) = letter.x, letter.y (group x + i*80 + 50, group y + 50).</summary>
    private void DrawLetterGlyph(SpriteBatch sb, string baseName, float x, float y, float scale, Color col, bool playing)
    {
        EnsureLetterSyms();
        if (_letterTex == null || !_letterSyms.TryGetValue(LetterMoveSymbol(baseName), out var ls)) return;
        int frame = playing ? (int)(_letterAnimTime * _letterFps) % Math.Max(1, ls.Total) : 0;
        var key = ls.Keys[0];
        foreach (var k in ls.Keys) if (frame >= k.Index && frame < k.Index + k.Dur) { key = k; break; }
        Vector2 size = ls.BMax - ls.BMin;
        Vector2 origin = size * 0.5f;
        Vector2 screen = new Vector2(x - 50f, y - 60f);                 // getScreenPosition override
        Vector2 local = new Vector2(key.Tx, key.Ty) + ls.Inner * key.Sx - ls.BMin;   // timeline space -> frame space
        Vector2 tl = screen + origin + (local - origin) * scale;
        float s = key.Sx * scale;
        if (!ls.Rot)
            sb.Draw(_letterTex, tl, ls.Src, col, 0f, Vector2.Zero, s, SpriteEffects.None, 0f);
        else  // stored rotated 90 deg clockwise: draw with -90 deg; unrotated size = (h, w)
            sb.Draw(_letterTex, tl + new Vector2(0f, ls.Src.Width * s), ls.Src, col, -MathF.PI / 2f, Vector2.Zero, s, SpriteEffects.None, 0f);
    }
}
