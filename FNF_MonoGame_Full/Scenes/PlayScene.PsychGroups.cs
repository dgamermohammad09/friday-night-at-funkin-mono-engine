using System;
using System.Collections.Generic;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// ReflectionFunctions addToGroup / removeFromGroup and DeprecatedFunctions updateHitboxFromGroup (Psych 1.0.4) for the
/// PlayState groups a stage/song script can reach: members (the state itself), gfGroup / dadGroup / boyfriendGroup
/// (FlxSpriteGroup: preAdd offsets the sprite by the group position, multiplies alpha and copies scrollFactor, and the
/// member draws with the group, after the character).
/// </summary>
public partial class PlayScene
{
    private readonly Dictionary<string, List<object>> _psychGroupChildren = new(StringComparer.Ordinal);

    private PsychCharGroup PsychCharGroupByName(string g) => g switch
    {
        "gfGroup" => gfGroup, "dadGroup" => dadGroup, "boyfriendGroup" => boyfriendGroup, _ => null
    };

    internal string PsychAddToGroup(string group, string tag, int index)
    {
        var o = PsychOrderResolve(tag);
        if (o == null || o is string) return $"addToGroup: Object {tag} is not valid!";
        group = (group ?? "").Trim();
        if (group == "members")
        {
            var m = PsychMembers;
            if (m.Contains(o)) return null;
            if (index < 0) m.Add(o); else PsychInsert(m, index, o);
            PsychOrderApply();
            return null;
        }
        var cg = PsychCharGroupByName(group);
        if (cg == null) return $"addToGroup: Group/Array {group} is not valid!";
        if (!_psychGroupChildren.TryGetValue(group, out var kids)) _psychGroupChildren[group] = kids = new List<object>();
        if (kids.Contains(o)) return null;
        // FlxTypedSpriteGroup.preAdd: sprite.x += x; sprite.y += y; sprite.alpha *= alpha; scrollFactor.copyFrom(scrollFactor)
        if (o is StaticStageProp sp)
        {
            sp.X += cg.x; sp.Y += cg.y; sp.Alpha *= cg.alpha;
            sp.ScrollX = 1f; sp.ScrollY = 1f; // the group's own scrollFactor is (1, 1) (gf's 0.95 is on the Character)
        }
        // FlxGroup.add does not remove it from PlayState.members: getObjectOrder still finds it there (native: same index)
        if (index < 0) kids.Add(o); else PsychInsert(kids, index, o);
        PsychOrderApply();
        return null;
    }

    internal string PsychRemoveFromGroup(string group, int index, string tag)
    {
        group = (group ?? "").Trim();
        List<object> list = group == "members" ? PsychMembers : (_psychGroupChildren.TryGetValue(group, out var k) ? k : null);
        if (list == null) return PsychCharGroupByName(group) != null ? null : $"removeFromGroup: Group/Array {group} is not valid!";
        object o = null;
        if (tag != null) { o = PsychOrderResolve(tag); if (o == null || o is string) return $"removeFromGroup: Object {tag} is not valid!"; }
        else if (index >= 0 && index < list.Count) o = list[index];
        if (o == null) return null;
        list.Remove(o);
        // FlxSpriteGroup.onMemberRemove: sprite.x -= x; sprite.y -= y
        if (group != "members" && o is StaticStageProp sp && PsychCharGroupByName(group) is PsychCharGroup cg) { sp.X -= cg.x; sp.Y -= cg.y; }
        PsychOrderApply();
        return null;
    }

    internal void PsychUpdateHitboxFromGroup(string group, int index)
    {
        List<object> list = group == "members" ? PsychMembers : (_psychGroupChildren.TryGetValue(group ?? "", out var k) ? k : null);
        if (list != null && index >= 0 && index < list.Count && list[index] is StaticStageProp sp && sp.Texture != null) PsychUpdateHitbox(sp);
    }

    /// <summary>FunkinLua addOffset: Character.addOffset / ModchartSprite.addOffset (animOffsets[anim] = [x, y]).</summary>
    internal bool PsychAddOffset(string obj, string anim, float x, float y)
    {
        var o = ResolvePsychObject(obj);
        if (o is Gameplay.Character c) { c.addOffset(anim, x, y); return true; }
        if (o is StaticStageProp sp)
        {
            sp.PsychAnimOffsets ??= new Dictionary<string, Microsoft.Xna.Framework.Vector2>(StringComparer.Ordinal);
            sp.PsychAnimOffsets[anim ?? ""] = new Microsoft.Xna.Framework.Vector2(x, y);
            return true;
        }
        return false;
    }

    /// <summary>FunkinLua addAnimation: animation.add(name, frames, framerate, loop) - frames index the sheet's frame list;
    /// the first animation added starts playing (LuaUtils.addAnimByIndices: obj.animation.curAnim == null).</summary>
    internal bool PsychAddAnimationFrames(string obj, string name, int[] frames, float fps, bool loop)
    {
        if (ResolvePsychObject(obj) is not StaticStageProp prop || prop.Sheet == null || frames == null) return false;
        var all = new List<FNF_MonoGame.Engine.SpriteFrame>(prop.Sheet.Frames.Values);
        prop.Animations ??= new Dictionary<string, FNF_MonoGame.Engine.PsychEngine.ModchartAnimation>(StringComparer.OrdinalIgnoreCase);
        var anim = new FNF_MonoGame.Engine.PsychEngine.ModchartAnimation { Name = name, Prefix = null, Indices = frames, Framerate = (int)Math.Round(fps), Loop = loop };
        foreach (int i in frames)
        {
            if (i < 0 || i >= all.Count) continue;
            var f = all[i];
            anim.FrameRects.Add(f.SourceRect);
            anim.FrameOffsets.Add(f.Offset);
            anim.FrameSizes.Add(new Microsoft.Xna.Framework.Point(f.FrameWidth > 0 ? f.FrameWidth : f.SourceRect.Width, f.FrameHeight > 0 ? f.FrameHeight : f.SourceRect.Height));
        }
        prop.Animations[name] = anim;
        if (prop.CurrentAnimation == null && anim.FrameRects.Count > 0)
        { prop.CurrentAnimation = name; prop.CurrentFrame = 0; prop.AnimTimer = 0f; prop.AnimPlaying = true; prop.AnimFramerate = anim.Framerate; prop.AnimLoop = loop; prop.SourceRect = anim.FrameRects[0]; }
        return true;
    }

    /// <summary>Called from PsychOrderApply: group members draw right after their character (z + 1 + i).</summary>
    private void PsychApplyGroupChildrenZ()
    {
        foreach (var kv in _psychGroupChildren)
        {
            // still in members too (FlxSprite drawn twice in Psych): the group copy draws over the member copy -> use the higher z
            int baseZ = kv.Key == "gfGroup" ? _gfZIndex : kv.Key == "dadGroup" ? _dadZIndex : _bfZIndex;
            for (int i = 0; i < kv.Value.Count; i++)
                if (kv.Value[i] is StaticStageProp sp) sp.ZIndex = Math.Max(sp.ZIndex, baseZ + 1 + Math.Min(i, 8));
        }
    }
}
