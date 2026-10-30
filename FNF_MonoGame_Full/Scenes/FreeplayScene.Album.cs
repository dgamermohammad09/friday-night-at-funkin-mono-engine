using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// AlbumRoll.hx: newAlbumArt = flixel-animate atlas 'freeplay/albumRoll/freeplayAlbum' at (FlxG.width - 360, 220) with the
/// "album art placeholder" symbol graphic replaced by the album art (replaceSymbolGraphic), labels intro (0..19),
/// switch (20..21), idle (22..30, looped after any other label finishes). DifficultyStars at (FlxG.width - 330, 209):
/// atlas 'freeplay/freeplayStars' "diff stars", 100 frames per difficulty step, looping inside its block.
/// albumTitle sparrow at (FlxG.width - 355, 500) + album titleOffsets. playIntro: title/stars appear after 0.75 s.
/// </summary>
public partial class FreeplayScene
{
    private PicoConfirmCard _albumTl, _starsTl;
    private SpriteSheet _albumTlSheet, _starsTlSheet;
    private string _albumLabel = "idle";
    private float _albumLabelTime;
    private float _starsTime;

    private static (int Start, int Dur) AlbumLabelRange(string label) => label switch
    {
        "intro" => (0, 20),
        "switch" => (20, 2),
        _ => (22, 9),
    };

    private static Vector2 AlbumTitleOffsets(string albumId) => VsAlbum(albumId)?.TitleOffsets ?? AlbumTitleOffsetsBase(albumId);
    private static Vector2 AlbumTitleOffsetsBase(string albumId) => (albumId ?? "").ToLowerInvariant() switch
    {
        "expansion1" or "expansion2" => new Vector2(-22, -3),
        "spaghetti" => new Vector2(-35, 0),
        "volume1" => new Vector2(8, 0),
        "volume2" or "volume3" or "volume4" => new Vector2(8, -7),
        _ => Vector2.Zero,
    };

    private void EnsureAlbumTimelines()
    {
        string root = System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot, "menus", "freeplay");
        if (_albumTl == null)
        {
            string j = Path.Combine(root, "albumRoll", "freeplayAlbum", "Animation.json");
            _albumTlSheet ??= SpriteSheet.Load(Game, "menus/freeplay/albumRoll/freeplayAlbum");
            if (_albumTlSheet?.Texture != null && global::FNF_MonoGame.Engine.FsCI.FileExists(j)) _albumTl = new PicoConfirmCard(_albumTlSheet, j);
        }
        if (_starsTl == null)
        {
            string j = Path.Combine(root, "freeplayStars", "Animation.json");
            _starsTlSheet ??= SpriteSheet.Load(Game, "menus/freeplay/freeplayStars");
            if (_starsTlSheet?.Texture != null && global::FNF_MonoGame.Engine.FsCI.FileExists(j)) _starsTl = new PicoConfirmCard(_starsTlSheet, j);
        }
    }

    /// <summary>newAlbumArt.anim.play(label, true)</summary>
    private void AlbumPlayLabel(string label)
    {
        _albumLabel = label;
        _albumLabelTime = 0f;
    }

    private void AlbumUpdate(float dt)
    {
        if (_albumLabel != "idle")
        {
            _albumLabelTime += dt;
            var (_, dur) = AlbumLabelRange(_albumLabel);
            if (_albumLabelTime * 24f > dur) AlbumPlayLabel("idle");        // onAlbumFinish -> idle, looped
        }
        else _albumLabelTime += dt;

        // playIntro(): FlxTimer 0.75 s -> showTitle(); showStars(); albumTitle.animation.play('switch')
        if (_albumRevealTimer > 0f)
        {
            _albumRevealTimer -= dt;
            if (_albumRevealTimer <= 0f)
            {
                _albumRevealTimer = 0f;
                _albumTitleVisible = true;
                _albumStarsVisible = true;
                if (_albumTitleSprite != null)
                {
                    _albumTitleSprite.OnFinish = () => { _albumTitleSprite.OnFinish = null; PlayAlbumTitleState("idle", loop: true, force: true); };
                    PlayAlbumTitleState("switch", loop: false, force: true);
                }
            }
        }
        _albumTitleSprite?.Update(dt);
        _starsTime += dt;
    }

    private int AlbumCurrentFrame()
    {
        var (start, dur) = AlbumLabelRange(_albumLabel);
        int f = (int)(_albumLabelTime * 24f);
        f = _albumLabel == "idle" ? f % dur : Math.Min(f, dur - 1);
        return start + f;
    }

    private int StarsCurrentFrame()
    {
        // DifficultyStars: difficulty clamp 0..15; curDifficulty = difficulty - 1 (15 = no stars, paused at 1500)
        int d = Math.Clamp(_albumDifficultyRating, 0, 15);
        int cur = d <= 0 ? 15 : d - 1;
        if (cur == 15) return 1500;
        return cur * 100 + ((int)(_starsTime * 24f) % 100);
    }

    /// <summary>Called with an active SpriteBatch (LinearClamp/AlphaBlend); leaves one active on return.</summary>
    private void DrawAlbumRollTl(SpriteBatch sb)
    {
        if (!_introDone || !_albumVisible) return;
        EnsureAlbumTimelines();
        float t = _exitAlbumT;   // exit movers: every album piece tweens to x = FlxG.width (absolute)
        float W = FNFGame.SCREEN_WIDTH;
        sb.End();
        if (_albumTl != null && _albumArtTexture != null && _albumArtTexture != Assets.Pixel)
        {
            _albumTl.Override["0001"] = _albumArtTexture;
            _albumTl.Draw(sb, AlbumCurrentFrame(), new Vector2(W - 360f + 360f * t, 220f + CsDy(-175f)));
        }
        if (_albumStarsVisible && _starsTl != null)
            _starsTl.Draw(sb, StarsCurrentFrame(), new Vector2(W - 330f + 330f * t, 209f + CsDy(-175f)));
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
        if (_albumTitleVisible && _albumTitleSprite?.Sheet?.Texture != null)
        {
            var off = AlbumTitleOffsets(_currentAlbumId);
            float tx = W - 355f + off.X;
            _albumTitleSprite.Position = new Vector2(tx + (W - tx) * t, 500f + off.Y);
            _albumTitleSprite.Scale = Vector2.One;
            _albumTitleSprite.Draw(sb);
        }
    }

    private readonly System.Collections.Generic.Dictionary<string, string> _albumByFolder = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Song.getAlbumId(difficulty, variation): the album of the variation the selection resolves to
    /// (e.g. bopeebo + pico -> songs/bopeebo_pico meta "volume4").</summary>
    private string ResolveAlbumId(FreeplaySong song)
    {
        try
        {
            string diff = Difficulties[Math.Clamp(_difficultyIndex, 0, Difficulties.Length - 1)];
            string folder = ResolveSongNameForSelection(song, diff);
            if (string.IsNullOrEmpty(folder)) return null;
            if (_albumByFolder.TryGetValue(folder, out var cached)) return cached;
            string meta = System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot, "songs", folder, "charts", "meta.json");
            string album = null;
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(meta))
            {
                var m = System.Text.RegularExpressions.Regex.Match(global::FNF_MonoGame.Engine.FsCI.ReadAllText(meta), @"""album""\s*:\s*""([^""]+)""");
                if (m.Success) album = m.Groups[1].Value;
            }
            _albumByFolder[folder] = album;
            return album;
        }
        catch { return null; }
    }

    /// <summary>Variation folder "&lt;song&gt;_&lt;var&gt;" (audio stays in the base folder): play songs/&lt;song&gt;/Inst-&lt;playData.instrumental&gt;.</summary>
    private void TryPlayVariationInst(string folder, int startMs)
    {
        try
        {
            if (Audio.MusicPlaying || string.IsNullOrEmpty(folder)) return;
            string root = System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot, "songs");
            string meta = Path.Combine(root, folder, "charts", "meta.json");
            if (!global::FNF_MonoGame.Engine.FsCI.FileExists(meta)) return;
            var m = System.Text.RegularExpressions.Regex.Match(global::FNF_MonoGame.Engine.FsCI.ReadAllText(meta), @"""instrumental""\s*:\s*""([^""]*)""");
            int us = folder.LastIndexOf('_');
            if (!m.Success || us <= 0) return;
            string baseFolder = folder.Substring(0, us), suffix = m.Groups[1].Value;
            string inst = string.IsNullOrEmpty(suffix) ? "Inst" : "Inst-" + suffix;
            foreach (var b in new[] { baseFolder, baseFolder.Replace('_', '-'), baseFolder.Replace("_", "") })
            {
                if (!global::FNF_MonoGame.Engine.FsCI.FileExists(Path.Combine(root, b, inst + ".ogg"))) continue;
                Audio.PlayMusic($"songs/{b}/{inst}", true, startMs);
                if (Audio.MusicPlaying) return;
            }
        }
        catch { }
    }
}
