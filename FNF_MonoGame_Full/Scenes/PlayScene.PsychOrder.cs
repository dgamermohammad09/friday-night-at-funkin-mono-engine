using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine.PsychEngine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Psych PlayState.members draw order for Psych-mod songs (states/PlayState.hx create 398-520, FunkinLua addLuaSprite,
/// addInstance, TextFunctions addLuaText, get/setObjectOrder, LuaUtils.getLowestCharacterGroup).
/// members start as [luaDebugGroup, gfGroup, dadGroup, boyfriendGroup]; after the stage scripts PlayState adds
/// comboGroup, uiGroup, noteGroup and camFollow. addLuaSprite(front)/addInstance(front)/addLuaText append,
/// addLuaSprite/addInstance behind insert at the lowest character group; setObjectOrder = remove + insert
/// (FlxGroup.insert -> Haxe Array.insert). The list index becomes the draw ZIndex (100 + 10*i) of Lua sprites, extra
/// characters and the gf/dad/bf layers, so V-Slice/built-in stage props (z &lt; 100) stay behind like Psych's own
/// stage objects. camHUD members are drawn in the same order: below the HUD when before uiGroup, above it otherwise.
/// setObjectOrder('iconP1', ..) inserts the icon into members (it stays in uiGroup too) -> drawn twice like Psych.
/// </summary>
public partial class PlayScene
{
    private List<object> _psychMembers;
    public bool PsychOrderActive => PsychNotes;

    private List<object> PsychMembers => _psychMembers ??= new List<object> { "luaDebugGroup", "gfGroup", "dadGroup", "boyfriendGroup" };

    private static readonly HashSet<string> PsychOrderMarkers = new()
    {
        "luaDebugGroup", "gfGroup", "dadGroup", "boyfriendGroup", "comboGroup", "uiGroup", "noteGroup", "camFollow", "iconP1", "iconP2"
    };

    /// <summary>PlayState.create: add(comboGroup); add(uiGroup); add(noteGroup); ... add(camFollow) after the stage scripts.</summary>
    public void PsychOrderAddUiGroups()
    {
        if (!PsychOrderActive) return;   // V-Slice/base songs keep their stage JSON zIndex (was renumbered to 110/120/130 whenever mods were scanned)
        var m = PsychMembers;
        foreach (var g in new[] { "comboGroup", "uiGroup", "noteGroup", "camFollow" })
            if (!m.Contains(g)) m.Add(g);
        PsychOrderApply();
    }

    private int PsychLowestCharGroupIndex()
    {
        var m = PsychMembers;
        int pos = m.IndexOf(_girlfriend != null ? "gfGroup" : "boyfriendGroup");
        int np = m.IndexOf("boyfriendGroup"); if (np < pos) pos = np;
        np = m.IndexOf("dadGroup"); if (np < pos) pos = np;
        return pos;
    }

    /// <summary>FlxGroup.insert: no-op when already a member; Haxe Array.insert clamps the position.</summary>
    private static void PsychInsert(List<object> m, int position, object o)
    {
        if (m.Contains(o)) return;
        int pos = position < 0 ? Math.Max(0, m.Count + 1 + position) : Math.Min(position, m.Count);
        m.Insert(pos, o);
    }

    public void PsychOrderAdd(object o, bool front)
    {
        var m = PsychMembers;
        if (m.Contains(o)) return; // FlxGroup.add/insert of a member does nothing
        if (front) m.Add(o);
        else PsychInsert(m, PsychLowestCharGroupIndex(), o);
        PsychOrderApply();
    }

    public void PsychOrderRemove(object o)
    {
        if (_psychMembers != null && _psychMembers.Remove(o)) PsychOrderApply();
    }

    private object PsychOrderResolve(string obj)
    {
        if (string.IsNullOrEmpty(obj)) return null;
        obj = obj.Trim();
        if (PsychOrderMarkers.Contains(obj)) return obj;
        object v = null;
        if (MusicBeatState.Variables.TryGetValue(obj, out var vv)) v = vv;
        else if (ModchartSprites.TryGetValue(obj, out var ms)) v = ms;
        else if (_luaTexts.TryGetValue(obj, out var lt)) v = lt;
        switch (v)
        {
            case StaticStageProp sp: return sp;
            case LuaText t: return t;
            case Gameplay.Character c:
                foreach (var o in _staticProps)
                    if (o is PsychCharacterProp cp && cp.Char == c) return cp;
                return null;
        }
        return null;
    }

    /// <summary>getObjectOrder: members.indexOf (-1 when not a member), or null when the object is unknown here.</summary>
    public int? PsychGetObjectOrder(string obj)
    {
        var k = PsychOrderResolve(obj);
        if (Environment.GetEnvironmentVariable("FNF_PSYORDERLOG") == "1")
            Console.WriteLine("[PSYORDER] " + obj + " members=" + string.Join(",", PsychMembers.ConvertAll(m => m is string s ? s : m is StaticStageProp sp ? "prop:" + sp.Name : m is LuaText lt ? "text:" + lt.Tag : m?.GetType().Name)));
        return k == null ? null : PsychMembers.IndexOf(k);
    }

    public bool PsychSetObjectOrder(string obj, int position)
    {
        var k = PsychOrderResolve(obj);
        if (k == null) return false;
        var m = PsychMembers;
        m.Remove(k);
        PsychInsert(m, position, k);
        PsychOrderApply();
        return true;
    }

    private int _psychUiZ = int.MaxValue;

    private void PsychOrderApply()
    {
        var m = PsychMembers;
        _psychUiZ = int.MaxValue;
        bool anim = false;
        for (int i = 0; i < m.Count; i++)
        {
            int z = 100 + 10 * i;
            switch (m[i])
            {
                case "gfGroup": _gfZIndex = z; break;
                case "dadGroup": _dadZIndex = z; break;
                case "boyfriendGroup": _bfZIndex = z; break;
                case "uiGroup": _psychUiZ = z; break;
                case StaticStageProp sp: sp.ZIndex = z; break;
                case string pa when pa.StartsWith("psychanim:"): PsychOrderApplyAnim(pa, z); anim = true; break;
            }
        }
        PsychApplyGroupChildrenZ();
        SortStaticProps();
        if (anim) _animatedProps = System.Linq.Enumerable.ToList(System.Linq.Enumerable.OrderBy(_animatedProps, p => p.ZIndex));
    }

    /// <summary>For FNF_ORDERLOG: the members list as names.</summary>
    public string PsychMembersDump()
    {
        if (_psychMembers == null) return "";
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < _psychMembers.Count; i++)
        {
            var o = _psychMembers[i];
            string n = o switch { string s => s, StaticStageProp p => p.Name, LuaText t => "text:" + t.Tag, _ => o?.GetType().Name };
            sb.Append(i).Append(':').Append(n).Append(' ');
        }
        return sb.ToString();
    }

    private static bool IsOtherCamera(string camera) =>
        string.Equals(camera, "other", StringComparison.OrdinalIgnoreCase) || string.Equals(camera, "camother", StringComparison.OrdinalIgnoreCase);

    /// <summary>camHUD/camOther members in PlayState.members order. below = members before uiGroup (drawn under the
    /// Psych HUD and notes), otherwise the rest (camHUD first, then camOther, which flixel draws after camHUD).</summary>
    public void PsychDrawHudMembers(SpriteBatch sb, bool below)
    {
        if (!PsychOrderActive || _psychMembers == null) return;
        var m = _psychMembers;
        float pct = Math.Clamp(_health, 0f, 2f) / 2f * 100f;
        for (int pass = 0; pass < (below ? 1 : 2); pass++)
        {
            bool other = pass == 1;
            for (int i = 0; i < m.Count; i++)
            {
                var o = m[i];
                int z = 100 + 10 * i;
                if (below ? z >= _psychUiZ : (!other && z < _psychUiZ)) continue;
                switch (o)
                {
                    case PsychCharacterProp: break; // extra characters draw on camGame
                    case StaticStageProp sp:
                        if (IsHudCamera(sp.Camera) && sp.CameraObj == null && IsOtherCamera(sp.Camera) == other) DrawStaticProp(sb, sp);
                        break;
                    case LuaText t:
                        if (IsOtherCamera(t.Camera) == other) t.Draw(sb);
                        break;
                    case "comboGroup":
                        if (!other) PsyPopsDraw(sb); // PlayState.popUpScore sprites (PlayScene.PsychPopups.cs)
                        break;
                    case "iconP1":
                        if (!other && UsePsychHud) { EnsurePsychHud(); DrawPsychIcon(sb, _playerIcon, _pIconP1, pct < 20f, true); }
                        break;
                    case "iconP2":
                        if (!other && UsePsychHud) { EnsurePsychHud(); DrawPsychIcon(sb, _opponentIcon, _pIconP2, pct > 80f, false); }
                        break;
                }
            }
        }
        if (!below)
        {
            // texts drawn some other way (not tracked as members)
            foreach (var tag in _luaTextDrawOrder)
                if (_luaTexts.TryGetValue(tag, out var t) && !m.Contains(t)) t.Draw(sb);
        }
    }
}
