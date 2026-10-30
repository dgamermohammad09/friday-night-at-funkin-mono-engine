using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Newtonsoft.Json.Linq;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>Freeplay data from V-Slice (Polymod) mods: AlbumRegistry entries (data/ui/freeplay/albums/&lt;id&gt;.json:
/// albumArtAsset, albumTitleAsset, albumTitleOffsets). Base albums keep the engine's own assets.</summary>
public partial class FreeplayScene
{
    private sealed class VsAlbumInfo { public string Art, Title; public Vector2 TitleOffsets; }
    private static readonly Dictionary<string, VsAlbumInfo> s_vsAlbums = new(StringComparer.OrdinalIgnoreCase);

    private static VsAlbumInfo VsAlbum(string albumId)
    {
        if (string.IsNullOrEmpty(albumId) || !VSliceMods.Any) return null;
        lock (s_vsAlbums)
        {
            if (s_vsAlbums.TryGetValue(albumId, out var c)) return c;
            VsAlbumInfo info = null;
            try
            {
                string p = VSliceMods.Resolve($"data/ui/freeplay/albums/{albumId}.json");
                if (p != null)
                {
                    var j = (JObject)VSliceMods.ParseLenient(global::FNF_MonoGame.Engine.FsCI.ReadAllText(p));
                    var off = j["albumTitleOffsets"] as JArray;
                    info = new VsAlbumInfo
                    {
                        Art = j["albumArtAsset"]?.ToString(),
                        Title = j["albumTitleAsset"]?.ToString(),
                        TitleOffsets = off != null && off.Count >= 2 ? new Vector2(off[0].Value<float>(), off[1].Value<float>()) : Vector2.Zero,
                    };
                }
            }
            catch (Exception ex) { Console.WriteLine($"[VSLICE-MODS] album {albumId}: {ex.Message}"); }
            s_vsAlbums[albumId] = info;
            return info;
        }
    }

    private static string VsAssetPath(string asset) => asset == null ? null : "images/" + (asset.Contains(':') ? asset.Substring(asset.IndexOf(':') + 1) : asset);
}
