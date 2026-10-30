using System;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>V-Slice (Polymod) mod support inside PlayState (see Engine/VSliceMods.cs).</summary>
public partial class PlayScene
{
    /// <summary>HealthIcon.loadCharacter for characters whose JSON comes from a V-Slice mod: icons/icon-&lt;healthIcon.id&gt;
    /// (isPixel: icons/icon-&lt;id&gt;-pixel). Base characters keep the engine's own icon lookup.</summary>
    /// <summary>Base V-Slice characters: HealthIcon uses the character JSON healthIcon.id (+ "-pixel" when isPixel), not the
    /// character id (tankman-bloody -> icon-tankman until the SetHealthIcon event; bf-christmas -> icon-bf).</summary>
    private Texture2D JsonHealthIcon(string charName)
    {
        if (string.IsNullOrEmpty(charName) || PsychNotes) return null;
        Gameplay.CharacterJsonData data = null;
        foreach (var c in new[] { _boyfriend, _opponent, _girlfriend })
            if (c != null && (string.Equals(c.OriginalName, charName, StringComparison.OrdinalIgnoreCase) || string.Equals(c.Name, charName, StringComparison.OrdinalIgnoreCase))) { data = c.JsonData; break; }
        string id = data?.VSliceHealthIcon?.Id;
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var file in data.VSliceHealthIcon.IsPixel ? new[] { $"icon-{id}-pixel", $"icon-{id}" } : new[] { $"icon-{id}" })
        {
            var tex = Assets.LoadTexture($"images/icons/{file}.png");
            if (tex != null && tex != Assets.Pixel) return tex;
        }
        return null;
    }

    private Texture2D VSliceLoadCharacterIcon(string charName)
    {
        if (!VSliceMods.Any || string.IsNullOrEmpty(charName)) return null;
        if (VSliceMods.Resolve($"data/characters/{charName}.json") == null) return null;
        Gameplay.CharacterJsonData data = null;
        foreach (var c in new[] { _boyfriend, _opponent, _girlfriend })
            if (c != null && string.Equals(c.Name, charName, StringComparison.OrdinalIgnoreCase)) { data = c.JsonData; break; }
        string id = data?.VSliceHealthIcon?.Id;
        if (string.IsNullOrEmpty(id)) id = charName;
        foreach (var file in data?.VSliceHealthIcon?.IsPixel == true ? new[] { $"icon-{id}-pixel", $"icon-{id}" } : new[] { $"icon-{id}" })
        {
            var tex = Assets.LoadTexture($"images/icons/{file}.png");
            if (tex != null && tex != Assets.Pixel) return tex;
        }
        return null;
    }

    // ---- accessors used by the V-Slice script facades (PlayScene.VSliceScripts.cs)
    public AssetManager VsAssets => Assets;
    internal InputManager VsInput => Input;
    public Microsoft.Xna.Framework.Game VsGame => Game;
    public string VsSongId => _chart?.SongName ?? _songName;
    public string VsVariation => string.IsNullOrEmpty(_selectedCharacterVariation) || _selectedCharacterVariation == "bf" ? "default" : _selectedCharacterVariation;
    public float VsHealth { get => _health; set => _health = Math.Clamp(value, 0f, 2f); }
    public int VsScore => _score;
    public float VsStrumBaseX(int lane, bool player) => VsStrumActive ? VsStrumX(lane, player) : (player ? FNFGame.SCREEN_WIDTH / 2 : 0) + StrumXOffset + lane * NOTE_SPACING;
    public float VsStrumBaseY() => VsStrumActive ? VsStrumY() : _downscroll ? (FNFGame.SCREEN_HEIGHT - STRUMLINE_Y_OFFSET - STRUMLINE_SIZE) : STRUMLINE_Y_OFFSET;
    private readonly System.Collections.Generic.Dictionary<string, VsCharacter> _vsChars = new();
    public object VsChar(string type)
    {
        var ch = type == "BF" ? _boyfriend : type == "DAD" ? _opponent : _girlfriend;
        if (ch == null) return null;
        if (_vsChars.TryGetValue(type, out var f) && f.C == ch) return (object)f.Script ?? f;
        foreach (var s in _vsScripts) if (s.Self is VsCharacter vc && vc.C == ch) { _vsChars[type] = vc; return s; }
        return _vsChars[type] = new VsCharacter(this, ch, type);
    }
    public object VsProp(string name)
    {
        if (name == null) return null;
        foreach (var p in _staticProps) if (string.Equals(p.Name, name, StringComparison.Ordinal)) return new VsProp(p);
        for (int i = 0; i < _animatedProps.Count; i++) if (string.Equals(_animatedProps[i].Name, name, StringComparison.Ordinal)) return new VsAnimProp(this, i);
        return null;
    }
    public Engine.AnimatedSprite VsAnimSprite(int i) => i >= 0 && i < _animatedProps.Count ? _animatedProps[i].Sprite : null;
    public float VsAnimAlpha(int i, float? set)
    {
        if (i < 0 || i >= _animatedProps.Count) return 0f;
        if (set.HasValue) { var p = _animatedProps[i]; p.Alpha = set.Value; _animatedProps[i] = p; }
        return _animatedProps[i].Alpha;
    }
    /// <summary>Character script playAnimation overrides: the engine plays character animations itself, so call the
    /// script's playAnimation whenever the animation (re)starts, with the base call suppressed.</summary>
    private void VsCharAnimHooks()
    {
        foreach (var s in _vsScripts)
        {
            if (s.Self is not VsCharacter vc || !s.HasFunction("playAnimation")) continue;
            string an = vc.C.CurrentAnimation; int fr = vc.C._sprite?.FrameIndex ?? 0;
            if (an != vc.LastAnim || fr < vc.LastFrame)
            {
                vc.SuppressBase = true;
                try { s.Call("playAnimation", an, true, false, false); } finally { vc.SuppressBase = false; }
            }
            vc.LastAnim = an; vc.LastFrame = fr;
        }
    }

    public void VsAdd(string op, object[] a)
    {
        var o = a != null && a.Length > (op == "insert" ? 1 : 0) ? a[op == "insert" ? 1 : 0] : null;
        if (o is VScriptInstance si && si.Self is VsSprite ss) o = ss;
        if (o is VsSprite vs) { if (op == "remove") VsRemoveSprite(vs); else VsAddSprite(vs); return; }
        if (o is VsText vt) { if (op == "remove") VsRemoveText(vt); else VsAddText(vt); return; }
        if (o is VsProp || o is VScriptInstance || o is VObj) return;   // facades are already part of the scene
        if (op == "remove") RemoveStageObject(o); else AddStageObject(o);
    }
}