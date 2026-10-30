extern alias IRIS;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json.Linq;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.PureHScript;

namespace FNF_MonoGame.Scenes;

/// <summary>Sprites created by V-Slice mod scripts (FunkinSprite / Bopper / FlxSprite: loadTextureAtlas, loadSparrow,
/// loadTexture, makeSolidColor, animation.addBy*, playAnimation, setAnimationOffsets) and added with
/// PlayState.add / Stage.add. They are drawn with the stage props by zIndex (DrawPropsInRange).</summary>
public partial class PlayScene
{
    private readonly List<VsSprite> _vsSprites = new();

    public void VsAddSprite(VsSprite s) { if (s != null && !_vsSprites.Contains(s)) { s.AddSeq = _vsSprites.Count; _vsSprites.Add(s); Console.WriteLine($"[VSLICE-SCRIPT] add sprite z={s.ZIndex} anims={string.Join(",", s.Anims.Keys)} sheet={(s.Spr?.Sheet != null)} keys={(s.Spr?.Sheet != null ? string.Join(",", s.Spr.Sheet.Animations.Keys) : "")}"); } }
    public void VsRemoveSprite(VsSprite s) { _vsSprites.Remove(s); }

    private void VsSpritesUpdate(float dt) { foreach (var s in _vsSprites.ToList()) s.Update(dt); }

    /// <summary>Draw script sprites with minZ &lt;= zIndex &lt; maxZ (world batch active).</summary>
    private void VsDrawSpritesInRange(SpriteBatch sb, int minZ, int maxZ)
    {
        QsDrawWorld(sb, minZ, maxZ);
        if (_vsSprites.Count == 0) return;
        foreach (var s in _vsSprites.OrderBy(x => x.ZIndex).ThenBy(x => x.AddSeq))
            if (s.ZIndex >= minZ && s.ZIndex < maxZ) s.Draw(sb, Game.GraphicsDevice);
    }
}

/// <summary>JSON data (character _data etc.) as a script object: fields, arrays as Haxe arrays.</summary>
public sealed class VsJson : VObj
{
    private readonly JObject _o; public VsJson(JObject o) { _o = o; }
    public static object Wrap(JToken t) => t switch
    {
        null => null,
        JObject o => new VsJson(o),
        JArray a => new IRIS::Array<object>(a.Select(Wrap).ToArray()),
        JValue v when v.Type == JTokenType.Integer => (object)v.Value<int>(),
        JValue v when v.Type == JTokenType.Float => v.Value<double>(),
        JValue v => v.Value,
        _ => null,
    };
    public override bool TryGet(string f, out object v) { if (_o.TryGetValue(f, out var t)) { v = Wrap(t); return true; } v = null; return false; }
}

public sealed class VsSprite : VObj
{
    public float X, Y, Alpha = 1f, Angle; public bool Visible = true, FlipX, FlipY; public int ZIndex, AddSeq;
    public Vector2 Scale = Vector2.One, Scroll = Vector2.One;
    public AnimatedSprite Spr; public Texture2D Tex; public Color SolidColor = Color.White; public int SolidW, SolidH;
    public readonly Dictionary<string, (string key, bool loop, int fps)> Anims = new();
    public readonly Dictionary<string, Vector2> Offsets = new();
    public string CurAnim; public Vector2 Offset;
    private readonly PlayScene _ps; private readonly Game _game;
    public VsSprite(PlayScene ps, double x = 0, double y = 0) { _ps = ps; _game = ps?.VsGame; X = (float)x; Y = (float)y; }

    void LoadAtlas(string path)
    {
        var sheet = SpriteSheet.Load(_game, "images/" + Strip(path), preRenderComposites: true);   // like atlas characters (Character.LoadAtlasWithSettings)
        if (sheet == null) { Console.WriteLine($"[VSLICE-SCRIPT] sprite atlas not found: {path}"); return; }
        Spr = new AnimatedSprite { Sheet = sheet, UseTimelineAnchoring = true };
    }
    void LoadSparrow(string path)
    {
        var sheet = SpriteSheet.Load(_game, "images/" + Strip(path), sparrowOnly: true);
        if (sheet == null) { Console.WriteLine($"[VSLICE-SCRIPT] sparrow not found: {path}"); return; }
        Spr = new AnimatedSprite { Sheet = sheet };
    }
    static string Strip(string p) { p = p ?? ""; if (p.Contains(':')) p = p.Substring(p.IndexOf(':') + 1); if (p.StartsWith("images/")) p = p.Substring(7); if (p.EndsWith(".png")) p = p.Substring(0, p.Length - 4); return p; }

    string FindKey(string prefix)
    {
        if (Spr?.Sheet == null || prefix == null) return null;
        if (Spr.Sheet.Animations.ContainsKey(prefix)) return prefix;
        foreach (var k in Spr.Sheet.Animations.Keys) if (k.StartsWith(prefix, StringComparison.Ordinal)) return k;
        return null;
    }
    void AddAnim(string name, string prefix, bool loop, int fps = 24)
    {
        var key = FindKey(prefix);
        if (key != null) Anims[name] = (key, loop, fps);
    }
    public void Play(string name, bool force)
    {
        if (name == null || Spr == null) return;
        if (!Anims.TryGetValue(name, out var a)) { var k = FindKey(name); if (k == null) return; a = (k, false, 24); }
        if (!force && CurAnim == name) return;
        CurAnim = name; Offset = Offsets.TryGetValue(name, out var o) ? o : Vector2.Zero;
        Spr.PlayAnimation(a.key, true, a.loop);
    }
    public void Update(float dt) => Spr?.Update(dt);
    string DbgInfo()
    {
        if (Spr?.Sheet == null || CurAnim == null || !Anims.TryGetValue(CurAnim, out var a)) return "-";
        string raw = Spr.Sheet.RawCompositeData.TryGetValue(a.key, out var rf) && rf.Count > 0 ? string.Join(";", rf[0].Select(t => $"{t.Frame.Name}@{t.TX:F1},{t.TY:F1}")) : "-";
        return $"key={a.key} flip={FlipX} fr={Spr.FrameIndex} tb={Spr.Sheet.GetTimelineBounds(a.key)} raw={raw} rawKeys={string.Join("|", Spr.Sheet.RawCompositeData.Keys)}";
    }

    static Texture2D s_pixel; string _lastLogAnim = "?";
    public void Draw(SpriteBatch sb, GraphicsDevice gd)
    {
        if (!Visible || Alpha <= 0f) return;
        if (CurAnim != _lastLogAnim && Environment.GetEnvironmentVariable("FNF_VSSPRLOG") == "1") { _lastLogAnim = CurAnim; Console.WriteLine($"[VSSPR] draw {X},{Y} anim={CurAnim} off={Offset} {DbgInfo()}"); }
        // FlxSprite.getScreenPosition (scrollFactor) minus Bopper/FunkinSprite animation offsets; the prop batch only zooms.
        var sp = _ps != null ? _ps.WorldToScreen(X, Y, Scroll.X, Scroll.Y) : new Vector2(X, Y);
        sp -= Offset * Scale;
        var tint = new Color(Color.White, Math.Clamp(Alpha, 0f, 1f));
        var fx = (FlipX ? SpriteEffects.FlipHorizontally : SpriteEffects.None) | (FlipY ? SpriteEffects.FlipVertically : SpriteEffects.None);
        if (Spr != null)
        {
            Spr.Position = sp; Spr.Scale = Scale; Spr.Effects = fx; Spr.Tint = tint; Spr.Rotation = MathHelper.ToRadians(Angle);
            Spr.Draw(sb);
        }
        else if (Tex != null) sb.Draw(Tex, sp, null, tint, MathHelper.ToRadians(Angle), Vector2.Zero, Scale, fx, 0f);
        else if (SolidW > 0)
        {
            if (s_pixel == null) { s_pixel = new Texture2D(gd, 1, 1); s_pixel.SetData(new[] { Color.White }); }
            sb.Draw(s_pixel, new Rectangle((int)sp.X, (int)sp.Y, (int)(SolidW * Scale.X), (int)(SolidH * Scale.Y)), new Color(SolidColor, Math.Clamp(Alpha, 0f, 1f)));
        }
    }

    public override bool TryGet(string f, out object v)
    {
        switch (f)
        {
            case "x": v = (double)X; return true;
            case "y": v = (double)Y; return true;
            case "alpha": v = (double)Alpha; return true;
            case "visible": v = Visible; return true;
            case "zIndex": v = ZIndex; return true;
            case "flipX": v = FlipX; return true;
            case "angle": v = (double)Angle; return true;
            case "scale": v = new VsPoint(() => Scale.X, x => Scale.X = x, () => Scale.Y, y => Scale.Y = y); return true;
            case "scrollFactor": v = new VsPoint(() => Scroll.X, x => Scroll.X = x, () => Scroll.Y, y => Scroll.Y = y); return true;
            case "offset": v = new VsPoint(() => Offset.X, x => Offset.X = x, () => Offset.Y, y => Offset.Y = y); return true;
            case "loadTextureAtlas": v = Fn(a => { LoadAtlas(S(A(a, 0))); return this; }); return true;
            case "loadSparrow": v = Fn(a => { LoadSparrow(S(A(a, 0))); return this; }); return true;
            case "loadTexture": case "loadGraphic": v = Fn(a => { Tex = _ps?.VsAssets.LoadTexture("images/" + Strip(S(A(a, 0))) + ".png"); return this; }); return true;
            case "makeSolidColor": case "makeGraphic": v = Fn(a => { SolidW = (int)D(A(a, 0)); SolidH = (int)D(A(a, 1)); long c = (long)D(A(a, 2)); SolidColor = new Color((int)((c >> 16) & 255), (int)((c >> 8) & 255), (int)(c & 255)); return this; }); return true;
            case "getDefaultSymbol": v = Fn(a => Spr?.Sheet?.Animations.Keys.FirstOrDefault(k => !Spr.Sheet.SymbolAnimKeys.Contains(k))); return true;
            case "playAnimation": v = Fn(a => { Play(S(A(a, 0)), B(A(a, 1))); return null; }); return true;
            case "setAnimationOffsets": v = Fn(a => { Offsets[S(A(a, 0))] = new Vector2((float)D(A(a, 1)), (float)D(A(a, 2))); return null; }); return true;
            case "setPosition": v = Fn(a => { X = (float)D(A(a, 0)); Y = (float)D(A(a, 1)); return null; }); return true;
            case "animation": v = new VsSpriteAnim(this); return true;
            case "getCurrentAnimation": v = Fn(a => CurAnim); return true;
            case "isAnimationFinished": v = Fn(a => Spr == null || Spr.Finished); return true;
            case "updateHitbox": case "screenCenter": case "dance": case "setGraphicSize": case "antialiasing": v = Fn(a => null); return true;
            case "kill": v = Fn(a => { Visible = false; return null; }); return true;
            case "revive": v = Fn(a => { Visible = true; return null; }); return true;
            case "destroy": v = Fn(a => { _ps?.VsRemoveSprite(this); return null; }); return true;
            case "width": v = (double)(Spr?.Sheet?.Texture?.Width ?? Tex?.Width ?? SolidW); return true;
            case "height": v = (double)(Spr?.Sheet?.Texture?.Height ?? Tex?.Height ?? SolidH); return true;
        }
        v = null; return false;
    }
    public override bool TrySet(string f, object v)
    {
        switch (f)
        {
            case "x": X = (float)D(v); return true;
            case "y": Y = (float)D(v); return true;
            case "alpha": Alpha = (float)D(v); return true;
            case "visible": Visible = B(v); return true;
            case "zIndex": ZIndex = (int)D(v); return true;
            case "flipX": FlipX = B(v); return true;
            case "angle": Angle = (float)D(v); return true;
        }
        return false;
    }

    public sealed class VsSpriteAnim : VObj
    {
        private readonly VsSprite _s; public VsSpriteAnim(VsSprite s) { _s = s; }
        public override bool TryGet(string f, out object v)
        {
            switch (f)
            {
                case "play": v = Fn(a => { _s.Play(S(A(a, 0)), B(A(a, 1))); return null; }); return true;
                case "addByPrefix": case "addByFrameLabel": case "addBySymbol":
                    v = Fn(a => { _s.AddAnim(S(A(a, 0)), S(A(a, 1)), B(A(a, f == "addByPrefix" ? 3 : 3)), (int)(a.Length > 2 ? D(a[2]) : 24)); return null; }); return true;
                case "curAnim": { var c = new VObj(); c.Extra["name"] = _s.CurAnim; c.Extra["finished"] = _s.Spr == null || _s.Spr.Finished; v = c; return true; }
                case "name": v = _s.CurAnim; return true;
                case "exists": v = Fn(a => _s.Anims.ContainsKey(S(A(a, 0)) ?? "")); return true;
            }
            v = null; return false;
        }
    }
}
