using System;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json.Linq;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>V-Slice mod note styles (data/notestyles/&lt;id&gt;.json, NoteStyle.hx): judgement popups taken from the
/// style's assets.judgementSick/Good/Bad/Shit (with the "fallback" chain). Base styles keep the engine skins.</summary>
public partial class PlayScene
{
    private static JObject VsNoteStyleJson(string id)
    {
        if (string.IsNullOrEmpty(id) || !VSliceMods.Any) return null;
        string p = VSliceMods.Resolve($"data/notestyles/{id}.json");
        if (p == null) return null;
        try { return VSliceMods.ParseLenient(global::FNF_MonoGame.Engine.FsCI.ReadAllText(p)) as JObject; } catch { return null; }
    }

    /// <summary>NoteStyle.fetch*: asset of the style, else of its fallback styles.</summary>
    private static JToken VsNoteStyleAsset(string id, string asset)
    {
        for (int guard = 0; guard < 8 && !string.IsNullOrEmpty(id); guard++)
        {
            var j = VsNoteStyleJson(id);
            if (j == null) return null;
            var a = j["assets"]?[asset];
            if (a != null && a["assetPath"] != null && a["assetPath"].Type != JTokenType.Null) return a;
            id = j["fallback"]?.ToString();
        }
        return null;
    }

    private void VsApplyNoteStyleRatings()
    {
        string style = _chart?.NoteStyle;
        if (string.IsNullOrEmpty(style) || style == "funkin" || style == "pixel" || VsNoteStyleJson(style) == null) return;
        Texture2D Load(string asset, Texture2D cur)
        {
            var a = VsNoteStyleAsset(style, asset);
            string path = a?["assetPath"]?.ToString();
            if (string.IsNullOrEmpty(path)) return cur;
            if (path.Contains(':')) path = path.Substring(path.IndexOf(':') + 1);
            var t = Assets.LoadTexture("images/" + path + ".png");
            Console.WriteLine($"[VSLICE-MODS] noteStyle {style} {asset} -> images/{path}.png {(t == null || t == Assets.Pixel ? "MISSING" : "ok")}");
            return t == null || t == Assets.Pixel ? cur : t;
        }
        _ratingSickTex = Load("judgementSick", _ratingSickTex);
        _ratingGoodTex = Load("judgementGood", _ratingGoodTex);
        _ratingBadTex = Load("judgementBad", _ratingBadTex);
        _ratingShitTex = Load("judgementShit", _ratingShitTex);
    }
}
