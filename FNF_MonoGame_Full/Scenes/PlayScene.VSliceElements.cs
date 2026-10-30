using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Gameplay;

namespace FNF_MonoGame.Scenes;

/// <summary>FlxSpriteElement added by a V-Slice mod character script into its own atlas timeline
/// (e.g. QT Rewired gf-qt: SpeakerSprite inserted into every "placeholder" symbol frame). The character composite is
/// split at the keyword symbol (SpriteSheet.Anchors/AnchorSplit, as for Spaghetti's lips) and the element atlas is drawn
/// in between at the symbol instance's matrix (FlxSpriteElement: position = parentMatrix(0,0)).</summary>
public partial class PlayScene
{
    private sealed class VsElement { public AnimatedSprite Sprite; public VSliceMods.SpriteElementSpec Spec; public string Anim; }
    private readonly Dictionary<Character, VsElement> _vsElements = new();

    private VsElement VsElementFor(Character c)
    {
        if (c == null || !VSliceMods.Any || VSliceMods.SpriteElements.Count == 0) return null;
        if (_vsElements.TryGetValue(c, out var e)) return e;
        e = null;
        if (VSliceMods.SpriteElements.TryGetValue(c.Name, out var spec))
        {
            var sheet = SpriteSheet.Load(Game, "images/" + spec.AtlasPath, preRenderComposites: true);
            if (sheet != null)
            {
                string anim = spec.AnimOnDance != null && sheet.Animations.ContainsKey(spec.AnimOnDance) ? spec.AnimOnDance : null;
                if (anim == null) foreach (var k in sheet.Animations.Keys) if (!sheet.SymbolAnimKeys.Contains(k)) { anim = k; break; }   // getDefaultSymbol(): the main timeline
                if (anim == null) foreach (var k in sheet.Animations.Keys) { anim = k; break; }
                var spr = new AnimatedSprite { Sheet = sheet, UseTimelineAnchoring = true };
                if (anim != null) spr.PlayAnimation(anim, true, false);
                e = new VsElement { Sprite = spr, Spec = spec, Anim = anim };
                Console.WriteLine($"[VSLICE-MODS] element for {c.Name}: {spec.AtlasPath} anim={anim}");
            }
        }
        _vsElements[c] = e;
        return e;
    }

    private bool VsElementSplit(Character c)
    {
        var e = VsElementFor(c);
        if (e == null || c._sprite?.Sheet == null) return false;
        string an = c._sprite.ResolvedAnimName;
        return !string.IsNullOrEmpty(an) && c._sprite.Sheet.CompositeAbove.ContainsKey(an);
    }

    /// <summary>gf-qt.hxc dance(): boomBoxSpeakers.animation.play("bump", true).</summary>
    private void VsElementsBeat()
    {
        foreach (var kv in _vsElements)
        {
            if (kv.Value?.Sprite == null || kv.Value.Anim == null || kv.Value.Spec.AnimOnDance == null) continue;
            string cur = kv.Key.CurrentAnimation ?? "";
            if (cur.StartsWith("dance") || cur.StartsWith("idle")) kv.Value.Sprite.PlayAnimation(kv.Value.Anim, true, false);
        }
    }

    private void VsElementsUpdate(float dt)
    {
        foreach (var kv in _vsElements) kv.Value?.Sprite?.Update(dt);
    }

    private void VsElementDraw(SpriteBatch sb, Character c)
    {
        var e = VsElementFor(c);
        var spr = c?._sprite;
        if (e == null || spr?.Sheet == null) return;
        string an = spr.ResolvedAnimName;
        if (string.IsNullOrEmpty(an) || !spr.Sheet.Anchors.TryGetValue(an, out var ticks) || ticks.Count == 0) return;
        var anchors = ticks[Math.Clamp(spr.FrameIndex, 0, ticks.Count - 1)];
        var tb = spr.Sheet.GetTimelineBounds(an);
        var etb = e.Anim != null ? e.Sprite.Sheet.GetTimelineBounds(e.Anim) : null;
        if (anchors.Count == 0 || tb == null) return;
        foreach (var a in anchors)
        {
            var p = spr.Position + new Vector2((a.TX - tb.Value.X) * spr.Scale.X, (a.TY - tb.Value.Y) * spr.Scale.Y);
            e.Sprite.Scale = spr.Scale;
            e.Sprite.Position = p + (etb.HasValue ? new Vector2(etb.Value.X * spr.Scale.X, etb.Value.Y * spr.Scale.Y) : Vector2.Zero);
            e.Sprite.Draw(sb);
        }
    }
}
