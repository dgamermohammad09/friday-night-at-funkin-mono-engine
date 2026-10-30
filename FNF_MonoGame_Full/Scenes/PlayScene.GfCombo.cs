using System;
using Microsoft.Xna.Framework;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Gameplay;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// GF combo / drop count animations (BaseCharacter.onNoteHit / onNoteMiss / onNoteHoldDrop, GF branch) and the
/// nene-tankmen comboHeart (nene-tankmen.hxc: separate Sparrow "characters/Nene_comboHeart", "hearts aflutter" 24 fps
/// no loop, at (gf.x + 174.5, gf.y - 66), zIndex gf + 1, adjustColor hue -40 sat -20 bright -40 contrast -25, shown on
/// playAnimation("combo50") and hidden when it finishes; the atlas copy of the symbol is hidden in Character.AtlasSettings).
/// Events (PlayState.goodNoteHit 3067, onNoteMiss 2821, hold drop 2888):
///   hit:  comboCount = combo + 1 (before applyScore) -> sick/good: playComboAnimation, bad/shit: playComboDropAnimation
///   miss / hold drop: comboCount = combo (before the reset) -> playComboDropAnimation
/// Botplay hits use judgement 'perfect' with comboCount 0 -> nothing plays (no call needed).
/// Dev hook FNF_FORCE_COMBO="songSec:hit|miss:count,..." dispatches a scripted event (same as the original module test).
/// </summary>
public partial class PlayScene
{
    private const string ComboHeartName = "neneComboHeart";
    private Gameplay.Character _comboHeartHooked;
    private (float t, string kind, int count)[] _forcedCombo;
    private int _forcedComboIdx;
    private string _gfComboLogLast;

    /// <summary>HitNoteScriptEvent for a player note (rating = port rating label).</summary>
    private void GfComboOnHit(string rating, int comboCount)
    {
        var gf = _girlfriend;
        if (gf == null) return;
        string r = (rating ?? "").ToUpperInvariant();
        if (r.StartsWith("SICK") || r.StartsWith("GOOD")) gf.PlayComboAnimation(comboCount);
        else gf.PlayComboDropAnimation(comboCount);
    }

    /// <summary>NOTE_MISS / NOTE_HOLD_DROP (isComboBreak) for a player note.</summary>
    private void GfComboOnBreak(int comboCount) => _girlfriend?.PlayComboDropAnimation(comboCount);

    /// <summary>Called every PlayScene update: hooks the heart onto nene-tankmen, runs the dev hook.</summary>
    private void GfComboUpdate()
    {
        var gf = _girlfriend;
        if (gf != null && _comboHeartHooked != gf)
        {
            _comboHeartHooked = gf;
            if (string.Equals(gf.OriginalName ?? gf.Name, "nene-tankmen", StringComparison.OrdinalIgnoreCase))
            {
                StageVisuals.PropShaders[ComboHeartName] = new AdjustColorParams(hue: -40, saturation: -20, brightness: -40, contrast: -25);
                gf.OnCountAnimationPlayed = n => { if (n == "combo50") ShowComboHeart(); };
            }
        }
        if (gf != null && _forcedComboIdx > 0 && Environment.GetEnvironmentVariable("FNF_COMBOLOG") == "1")
        {
            string heart = "";
            foreach (var p in _animatedProps) if (p.Name == ComboHeartName) heart = $" heart f={p.Sprite.FrameIndex} pos={p.Sprite.Position}";
            string st = $"{gf.CurrentAnimation}@{gf.FrameIndexForLog}{heart}";
            if (st != _gfComboLogLast) { _gfComboLogLast = st; Console.WriteLine($"[GFFR] t={SongPositionSeconds:F3} {st}"); }
        }
        if (_forcedCombo == null)
        {
            var env = Environment.GetEnvironmentVariable("FNF_FORCE_COMBO");
            var list = new System.Collections.Generic.List<(float, string, int)>();
            if (!string.IsNullOrEmpty(env))
                foreach (var part in env.Split(','))
                {
                    var f = part.Split(':');
                    if (f.Length == 3 && float.TryParse(f[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float t)
                        && int.TryParse(f[2], out int c)) list.Add((t, f[1], c));
                }
            _forcedCombo = list.ToArray();
        }
        while (_forcedComboIdx < _forcedCombo.Length && SongPositionSeconds >= _forcedCombo[_forcedComboIdx].t)
        {
            var e = _forcedCombo[_forcedComboIdx++];
            Console.WriteLine($"[GFCOMBO] forced {e.kind} {e.count} at song={SongPositionSeconds:F3}");
            if (e.kind == "miss") GfComboOnBreak(e.count); else GfComboOnHit(e.kind == "bad" ? "BAD" : "SICK!!", e.count);
        }
    }

    private void ShowComboHeart()
    {
        var gf = _girlfriend;
        if (gf == null) return;
        _animatedProps.RemoveAll(p => p.Name == ComboHeartName);
        var sheet = SpriteSheet.Load(Game, "images/characters/Nene_comboHeart", sparrowOnly: true);
        if (sheet == null) return;
        var sprite = new AnimatedSprite { Sheet = sheet };
        sprite.Position = new Vector2(gf.Position.X + 174.5f, gf.Position.Y - 66f);
        sprite.Scale = Vector2.One;
        sprite.FrameRate = 24f;
        sprite.PlayAnimation("hearts aflutter", force: true, loop: false);
        sprite.OnFinish = () => _animatedProps.RemoveAll(p => p.Sprite == sprite);   // comboHeart.visible = false
        _animatedProps.Add(new AnimatedStageProp
        {
            Sprite = sprite, ZIndex = _gfZIndex + 1, Alpha = 1f, ScrollX = _gfCharScroll.X, ScrollY = _gfCharScroll.Y,
            DanceEvery = 0, LastDanceBeat = -1, Name = ComboHeartName, CanPlayOtherAnims = true
        });
        _animatedProps = System.Linq.Enumerable.ToList(System.Linq.Enumerable.OrderBy(_animatedProps, p => p.ZIndex));
        if (Environment.GetEnvironmentVariable("FNF_COMBOLOG") == "1")
            Console.WriteLine($"[GFCOMBO] heart ({gf.OriginalName}) at {sprite.Position} z={_gfZIndex + 1}");
    }
}
