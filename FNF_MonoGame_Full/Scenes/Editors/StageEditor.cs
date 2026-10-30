using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes.Editors;

/// <summary>Loads a character's sheet and draws its idle frame (stage/week previews).</summary>
public class EdCharPreview
{
    public EdChar Data; public SpriteSheet Sheet; public AnimatedSprite Spr; public string Id;
    public float W, H;   // idle frame size * scale

    public static string FindJson(string id, string mod)
    {
        var c = new List<string>();
        if (!string.IsNullOrEmpty(mod)) { c.Add(Path.Combine(EdProject.ModsBase, mod, "characters", id + ".json")); c.Add(Path.Combine(EdProject.ModsBase, mod, "data", "characters", id + ".json")); }
        c.Add(Path.Combine(AssetManager.ContentRoot, "data", "psych", "characters", id + ".json"));
        c.Add(Path.Combine(AssetManager.ContentRoot, "data", "characters", id + ".json"));
        return c.FirstOrDefault(File.Exists);
    }

    public static EdCharPreview Load(Game game, string id, string mod)
    {
        var p = new EdCharPreview { Id = id };
        try
        {
            string j = FindJson(id, mod);
            if (j == null) return p;
            p.Data = EdChar.LoadAny(j);
            string img = p.Data.Image.Replace('\\', '/'); int colon = img.IndexOf(':'); if (colon > 0) img = img.Substring(colon + 1);
            var tries = new List<string>();
            if (!string.IsNullOrEmpty(mod)) tries.Add(Path.Combine(EdProject.ModsBase, mod, "images", img));
            tries.AddRange(new[] { "images/" + img, "images/psych/" + img, "game/" + img });
            foreach (var t in tries)
            {
                bool atlas = global::FNF_MonoGame.Engine.FsCI.FileExists(Path.Combine(Path.IsPathRooted(t) ? t : Path.Combine(AssetManager.ContentRoot, t), "Animation.json"));
                if (!atlas && Path.IsPathRooted(t) && !global::FNF_MonoGame.Engine.FsCI.FileExists(t + ".png")) continue;
                SpriteSheet s = null;
                try { s = SpriteSheet.Load(game, t, preRenderComposites: atlas); } catch { }
                if (s != null && (s.Frames.Count > 0 || s.Animations.Count > 0)) { p.Sheet = s; break; }
            }
            if (p.Sheet == null) return p;
            p.Spr = new AnimatedSprite { Sheet = p.Sheet, UseTimelineAnchoring = p.Data.RenderType.Contains("atlas") };
            var idle = p.Data.Anims.FirstOrDefault(a => a.Name == "idle") ?? p.Data.Anims.FirstOrDefault(a => a.Name == "danceLeft") ?? p.Data.Anims.FirstOrDefault();
            if (idle != null)
            {
                var fr = p.Sheet.BuildAnimationFromPrefix(idle.Prefix) ?? p.Sheet.GetAnimation(idle.Prefix)
                         ?? p.Sheet.Frames.Where(kv => kv.Key.StartsWith(idle.Prefix, StringComparison.Ordinal)).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value).ToList();
                if (fr != null && idle.Indices.Length > 0) fr = idle.Indices.Where(i => i < fr.Count).Select(i => fr[i]).ToList();
                if (fr != null && fr.Count > 0)
                {
                    p.Sheet.Animations["ed:idle"] = fr;
                    if (p.Sheet.CompositeAnimations.TryGetValue(idle.Prefix, out var comp)) p.Sheet.CompositeAnimations["ed:idle"] = comp;
                    p.Spr.FrameRate = idle.Fps; p.Spr.PlayAnimation(CharacterEditor.Key(p.Sheet, new EdAnim { Name = "idle", Prefix = idle.Prefix, Indices = idle.Indices }), true, true);
                }
            }
            var ff = p.Sheet.Animations.TryGetValue("ed:idle", out var idleFr) && idleFr.Count > 0 ? idleFr[0] : p.Sheet.FirstFrame;
            var tb = p.Sheet.GetTimelineBounds(p.Spr.ResolvedAnimName ?? "");
            if (tb.HasValue) { p.W = tb.Value.W * p.Data.Scale; p.H = tb.Value.H * p.Data.Scale; }
            else if (ff != null) { p.W = (ff.FrameWidth > 0 ? ff.FrameWidth : ff.SourceRect.Width) * p.Data.Scale; p.H = (ff.FrameHeight > 0 ? ff.FrameHeight : ff.SourceRect.Height) * p.Data.Scale; }
        }
        catch (Exception ex) { Console.WriteLine("[EDITOR] preview " + id + ": " + ex.Message); }
        return p;
    }

    /// <summary>Draws at Psych semantics: top-left = slot + character position, offsets of the idle anim.</summary>
    public void Draw(SpriteBatch sb, Vector2 screenTopLeft, float zoom, Color tint, bool flipForPlayer)
    {
        if (Spr == null) return;
        var idle = Data.Anims.FirstOrDefault(a => a.Name == "idle") ?? Data.Anims.FirstOrDefault(a => a.Name == "danceLeft") ?? Data.Anims.FirstOrDefault();
        var off = idle == null ? Vector2.Zero : new Vector2(idle.OffX, idle.OffY) * (Data.SourceFormat == "vslice" ? Data.Scale : 1f);
        Spr.Position = screenTopLeft - off * zoom;
        Spr.Scale = new Vector2(Data.Scale * zoom);
        bool flip = Data.FlipX ^ flipForPlayer;
        Spr.Effects = flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
        Spr.Tint = tint;
        Spr.Draw(sb);
    }
}

public class EdProp
{
    public string Name = "prop", Image = "", Type = "sprite", Color = "FFFFFF", FirstAnim = "";
    public float X, Y, ScaleX = 1, ScaleY = 1, ScrollX = 1, ScrollY = 1, Alpha = 1, Angle;
    public bool FlipX, NoAA;
    public JArray Anims;   // Psych animatedSprite animations / V-Slice prop animations (kept verbatim)
    public string CharSlot;   // "bf" / "gf" / "dad" for the character layer entries
    // runtime
    public SpriteSheet Sheet; public Texture2D Tex; public AnimatedSprite Spr; public float W, H;
    public EdProp CloneData() { var p = (EdProp)MemberwiseClone(); p.Anims = (JArray)Anims?.DeepClone(); p.Spr = null; p.Sheet = null; p.Tex = null; return p; }
}

/// <summary>
/// Merged stage editor: Psych 1.0 StageEditorState (objects list with sprites + the 3 character layers, character
/// slots, camera offsets, zoom) and V-Slice stage data (props with zIndex/scroll/scale, characters with feet
/// positions + cameraOffsets, cameraZoom). Saves Psych stages/&lt;id&gt;.json and V-Slice data/stages/&lt;id&gt;.json.
/// Mouse: click = select, drag = move, right-drag = pan, wheel = zoom.  Keys: ARROWS nudge (SHIFT x10),
/// PGUP/PGDN layer, DEL delete, C cycle camera preview, F1 Psych, F2 V-Slice.
/// </summary>
public class StageEditor : EditorBase
{
    protected override string EdTitle => "STAGE";
    protected override Color EdColor => Ed.Yellow;

    private string _id = "newstage", _dir = "", _source = "", _fmt = "psych";
    private float _zoomStage = 0.9f, _camSpeed = 1f;
    private bool _hideGf;
    private readonly Dictionary<string, Vector2> _slot = new() { ["bf"] = new(770, 100), ["gf"] = new(400, 130), ["dad"] = new(100, 100) };
    private readonly Dictionary<string, Vector2> _camOff = new() { ["bf"] = Vector2.Zero, ["gf"] = Vector2.Zero, ["dad"] = Vector2.Zero };
    private readonly Dictionary<string, string> _charId = new() { ["bf"] = "bf", ["gf"] = "gf", ["dad"] = "dad" };
    private readonly Dictionary<string, EdCharPreview> _prev = new();
    private List<EdProp> _layers = new();
    private int _sel = -1;
    private Vector2 _cam = new(640, 360); private float _zoom = 0.5f;
    private bool _dragging, _panning; private Vector2 _dragStart, _dragObj, _panStart, _camStart;
    private int _camPreview = 0; private static readonly string[] CamNames = { "off", "bf", "dad", "gf" };
    private int _tab; private static readonly string[] Tabs = { "OBJECT", "STAGE", "FILE" };
    private List<(string label, string path)> _files = new();
    private int _fileSel = -1; private float _fileScroll, _layerScroll;
    private string _newImage = "", _newId = "";

    public override void Load()
    {
        RefreshFiles();
        string dev = Environment.GetEnvironmentVariable("FNF_EDITOR_STAGE");
        var f = _files.FirstOrDefault(x => x.label.EndsWith(string.IsNullOrEmpty(dev) ? "] mainStage" : dev, StringComparison.OrdinalIgnoreCase));
        if (f.path != null) LoadStage(f.path);
        else NewStage();
    }

    private void RefreshFiles()
    {
        _files.Clear();
        void Scan(string dir, string tag) { if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(dir)) foreach (var j in global::FNF_MonoGame.Engine.FsCI.GetFiles(dir, "*.json").OrderBy(x => x)) _files.Add(($"{tag}{Path.GetFileNameWithoutExtension(j)}", j)); }
        Scan(Path.Combine(AssetManager.ContentRoot, "data", "stages"), "[V] ");
        Scan(Path.Combine(AssetManager.ContentRoot, "data", "psych", "stages"), "[P] psych/");
        foreach (var m in EdProject.Mods())
        {
            Scan(Path.Combine(EdProject.ModsBase, m, "stages"), $"[P] {m}/");
            Scan(Path.Combine(EdProject.ModsBase, m, "data", "stages"), $"[V] {m}/");
        }
        _files.RemoveAll(f => f.label.EndsWith("_props"));
    }

    private string ModOf(string path)
    {
        try { string mb = Path.GetFullPath(EdProject.ModsBase), f = Path.GetFullPath(path); if (f.StartsWith(mb, StringComparison.OrdinalIgnoreCase)) return f.Substring(mb.Length).TrimStart('\\', '/').Split('\\', '/')[0]; } catch { }
        return null;
    }
    private string _mod;

    private void NewStage()
    {
        _layers = new List<EdProp> { new() { Name = "gf", CharSlot = "gf", Type = "gf" }, new() { Name = "dad", CharSlot = "dad", Type = "dad" }, new() { Name = "boyfriend", CharSlot = "bf", Type = "boyfriend" } };
        _id = "newstage"; _fmt = "new"; _source = "";
        LoadPreviews();
    }

    private void LoadPreviews()
    {
        foreach (var k in new[] { "bf", "gf", "dad" }) _prev[k] = EdCharPreview.Load(Game, _charId[k], _mod ?? EdProject.ModName);
    }

    private void LoadStage(string path)
    {
        if (string.IsNullOrEmpty(EdProject.ModName)) EdProject.GuessModFrom(path); // opening a mod file targets that mod
        try
        {
            var o = JObject.Parse(EdSong.SanitizeJson(global::FNF_MonoGame.Engine.FsCI.ReadAllText(path)));
            _mod = ModOf(path);
            _id = Path.GetFileNameWithoutExtension(path); _source = path;
            _layers = new List<EdProp>();
            _dir = o.Value<string>("directory") ?? "";
            if (o["props"] != null || o["characters"] is JObject)
            {   // V-Slice StageData
                _fmt = "vslice";
                _zoomStage = o.Value<float?>("cameraZoom") ?? 1f;
                var chars = o["characters"] as JObject;
                var items = new List<(int z, EdProp p)>();
                if (o["props"] is JArray props)
                    foreach (var pr in props)
                    {
                        var p = new EdProp
                        {
                            Name = pr.Value<string>("name") ?? "prop", Image = pr.Value<string>("assetPath") ?? "",
                            X = pr["position"]?[0]?.Value<float>() ?? 0, Y = pr["position"]?[1]?.Value<float>() ?? 0,
                            ScaleX = pr["scale"] is JArray sa ? sa[0].Value<float>() : pr.Value<float?>("scale") ?? 1,
                            ScaleY = pr["scale"] is JArray sb2 ? sb2[1].Value<float>() : pr.Value<float?>("scale") ?? 1,
                            ScrollX = pr["scroll"]?[0]?.Value<float>() ?? 1, ScrollY = pr["scroll"]?[1]?.Value<float>() ?? 1,
                            Alpha = pr.Value<float?>("alpha") ?? 1, FlipX = pr.Value<bool?>("flipX") ?? false, NoAA = pr.Value<bool?>("isPixel") ?? false,
                            Anims = pr["animations"] as JArray, FirstAnim = pr.Value<string>("startingAnimation") ?? "",
                        };
                        if (p.Anims != null && p.Anims.Count > 0) p.Type = "animatedSprite";
                        if (p.Image.StartsWith("#")) { p.Type = "square"; p.Color = p.Image.TrimStart('#'); }
                        items.Add((pr.Value<int?>("zIndex") ?? 0, p));
                    }
                LoadPreviewIds(o);
                foreach (var k in new[] { "bf", "gf", "dad" })
                {
                    var c = chars?[k] as JObject;
                    int z = c?.Value<int?>("zIndex") ?? (k == "bf" ? 300 : k == "dad" ? 200 : 100);
                    items.Add((z, new EdProp { Name = k == "bf" ? "boyfriend" : k, CharSlot = k, Type = k == "bf" ? "boyfriend" : k }));
                }
                LoadPreviews();
                foreach (var k in new[] { "bf", "gf", "dad" })
                {
                    var c = chars?[k] as JObject;
                    var feet = c?["position"] is JArray pa && pa.Count >= 2 ? new Vector2(pa[0].Value<float>(), pa[1].Value<float>()) : _slot[k];
                    var pv = _prev[k];
                    var posOff = pv?.Data == null ? Vector2.Zero : new Vector2(pv.Data.PosX, pv.Data.PosY);
                    // V-Slice: x = feet.x - w/2 + globalOffset ; Psych slot = top-left - character position
                    _slot[k] = feet - new Vector2((pv?.W ?? 0) / 2f, pv?.H ?? 0) + (pv?.Data?.SourceFormat == "vslice" ? posOff : Vector2.Zero) - (pv?.Data?.SourceFormat == "psych" ? posOff : Vector2.Zero);
                    _camOff[k] = c?["cameraOffsets"] is JArray co && co.Count >= 2 ? new Vector2(co[0].Value<float>(), co[1].Value<float>()) : Vector2.Zero;
                }
                _layers = items.OrderBy(i => i.z).Select(i => i.p).ToList();
            }
            else
            {   // Psych StageFile (+ 1.0 objects)
                _fmt = "psych";
                _zoomStage = o.Value<float?>("defaultZoom") ?? 0.9f;
                _camSpeed = o.Value<float?>("camera_speed") ?? 1f;
                _hideGf = o.Value<bool?>("hide_girlfriend") ?? false;
                Vector2 V(string k, Vector2 d) => o[k] is JArray a && a.Count >= 2 ? new Vector2(a[0].Value<float>(), a[1].Value<float>()) : d;
                _slot["bf"] = V("boyfriend", new(770, 100)); _slot["gf"] = V("girlfriend", new(400, 130)); _slot["dad"] = V("opponent", new(100, 100));
                _camOff["bf"] = V("camera_boyfriend", Vector2.Zero); _camOff["gf"] = V("camera_girlfriend", Vector2.Zero); _camOff["dad"] = V("camera_opponent", Vector2.Zero);
                if (o["_editorMeta"] is JObject em) { _charId["bf"] = em.Value<string>("boyfriend") ?? "bf"; _charId["gf"] = em.Value<string>("gf") ?? "gf"; _charId["dad"] = em.Value<string>("dad") ?? "dad"; }
                bool hasChars = false;
                if (o["objects"] is JArray objs)
                    foreach (var ob in objs)
                    {
                        string type = ob.Value<string>("type") ?? "sprite";
                        if (type is "gf" or "gfGroup" or "dad" or "dadGroup" or "boyfriend" or "boyfriendGroup")
                        {
                            string slot = type.StartsWith("gf") ? "gf" : type.StartsWith("dad") ? "dad" : "bf";
                            _layers.Add(new EdProp { Name = slot == "bf" ? "boyfriend" : slot, CharSlot = slot, Type = slot == "bf" ? "boyfriend" : slot }); hasChars = true; continue;
                        }
                        _layers.Add(new EdProp
                        {
                            Type = type, Name = ob.Value<string>("name") ?? "obj", Image = ob.Value<string>("image") ?? "",
                            X = ob.Value<float?>("x") ?? 0, Y = ob.Value<float?>("y") ?? 0,
                            ScaleX = ob["scale"]?[0]?.Value<float>() ?? 1, ScaleY = ob["scale"]?[1]?.Value<float>() ?? 1,
                            ScrollX = ob["scroll"]?[0]?.Value<float>() ?? 1, ScrollY = ob["scroll"]?[1]?.Value<float>() ?? 1,
                            Alpha = ob.Value<float?>("alpha") ?? 1, Angle = ob.Value<float?>("angle") ?? 0, FlipX = ob.Value<bool?>("flipX") ?? false,
                            NoAA = !(ob.Value<bool?>("antialiasing") ?? true), Color = (ob.Value<string>("color") ?? "FFFFFF").TrimStart('#'),
                            Anims = ob["animations"] as JArray, FirstAnim = ob.Value<string>("firstAnimation") ?? "",
                        });
                    }
                if (!hasChars) { _layers.Add(new EdProp { Name = "gf", CharSlot = "gf", Type = "gf" }); _layers.Add(new EdProp { Name = "dad", CharSlot = "dad", Type = "dad" }); _layers.Add(new EdProp { Name = "boyfriend", CharSlot = "bf", Type = "boyfriend" }); }
                // Psych built-in stage 'stage' keeps its art in stage_props.json (V-Slice props), show it too
                string sp = Path.Combine(Path.GetDirectoryName(path) ?? "", _id + "_props.json");
                if (global::FNF_MonoGame.Engine.FsCI.FileExists(sp) && !_layers.Any(l => l.CharSlot == null))
                {
                    var spo = JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(sp));
                    int at = 0;
                    if (spo["props"] is JArray props)
                        foreach (var pr in props)
                            _layers.Insert(at++, new EdProp { Name = pr.Value<string>("name"), Image = pr.Value<string>("assetPath"), X = pr["position"][0].Value<float>(), Y = pr["position"][1].Value<float>(), ScaleX = pr["scale"][0].Value<float>(), ScaleY = pr["scale"][1].Value<float>(), ScrollX = pr["scroll"][0].Value<float>(), ScrollY = pr["scroll"][1].Value<float>() });
                }
                LoadPreviews();
            }
            foreach (var l in _layers) LoadPropArt(l);
            _sel = -1;
            CenterCam("bf");
            Ed.Toast($"Loaded {_fmt.ToUpperInvariant()} stage '{_id}' ({_layers.Count(l => l.CharSlot == null)} props)", Ed.Green);
        }
        catch (Exception ex) { Ed.Toast("Load failed: " + ex.Message, Ed.Red); Console.WriteLine(ex); }
    }

    private void LoadPreviewIds(JObject o) { }

    private void CenterCam(string who)
    {
        var p = _prev.TryGetValue(who, out var pv) ? pv : null;
        _cam = CharTopLeft(who) + new Vector2((p?.W ?? 400) / 2f, (p?.H ?? 400) / 2f);
        _cam.X = (_slot["bf"].X + _slot["dad"].X) / 2f + 200f;
    }

    private Vector2 CharTopLeft(string k)
    {
        var pv = _prev.TryGetValue(k, out var p) ? p : null;
        var off = pv?.Data == null ? Vector2.Zero : new Vector2(pv.Data.PosX, pv.Data.PosY);
        return _slot[k] + (pv?.Data?.SourceFormat == "psych" ? off : Vector2.Zero);
    }

    /// <summary>Finds the prop image: mod images/, Content images/, V-Slice stage folders, Psych base images.</summary>
    private string ResolveImage(string img)
    {
        if (string.IsNullOrEmpty(img) || img.StartsWith("#")) return null;
        img = img.Replace('\\', '/'); int colon = img.IndexOf(':'); if (colon > 0) img = img.Substring(colon + 1);
        var c = new List<string>();
        foreach (var m in new[] { _mod, EdProject.ModName }.Where(x => !string.IsNullOrEmpty(x)).Distinct()) c.Add(Path.Combine(EdProject.ModsBase, m, "images", img));
        string cr = AssetManager.ContentRoot;
        c.Add(Path.Combine(cr, "images", img));
        if (!string.IsNullOrEmpty(_dir)) { c.Add(Path.Combine(cr, "images", _dir, img)); c.Add(Path.Combine(cr, "game", "stages", _dir, img)); }
        c.Add(Path.Combine(cr, "images", "psych", img));
        foreach (var d in StageFolders()) c.Add(Path.Combine(d, img));
        return c.FirstOrDefault(p => global::FNF_MonoGame.Engine.FsCI.FileExists(p + ".png"));
    }
    private static List<string> _stageFolders;
    private static List<string> StageFolders()
    {
        if (_stageFolders != null) return _stageFolders;
        _stageFolders = new List<string>();
        try { string g = Path.Combine(AssetManager.ContentRoot, "game", "stages"); if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(g)) _stageFolders.AddRange(global::FNF_MonoGame.Engine.FsCI.GetDirectories(g)); } catch { }
        return _stageFolders;
    }

    private void LoadPropArt(EdProp p)
    {
        if (p.CharSlot != null || p.Type == "square") return;
        string f = ResolveImage(p.Image);
        p.Sheet = null; p.Tex = null; p.Spr = null; p.W = p.H = 100;
        if (f == null) return;
        try
        {
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(f + ".xml"))
            {
                p.Sheet = SpriteSheet.Load(Game, f);
                p.Spr = new AnimatedSprite { Sheet = p.Sheet };
                string first = null;
                if (p.Anims != null && p.Anims.Count > 0)
                {
                    var a = p.Anims.FirstOrDefault(x => (x.Value<string>("anim") ?? x.Value<string>("name")) == p.FirstAnim) ?? p.Anims[0];
                    first = a.Value<string>("name") is string nm && p.Sheet.BuildAnimationFromPrefix(nm) != null ? nm : a.Value<string>("prefix");
                }
                first ??= p.Sheet.Frames.Keys.OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault()?.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
                if (first != null && p.Sheet.BuildAnimationFromPrefix(first) != null) p.Spr.PlayAnimation(first, true, true);
                var ff = p.Sheet.FirstFrame;
                if (ff != null) { p.W = ff.FrameWidth > 0 ? ff.FrameWidth : ff.SourceRect.Width; p.H = ff.FrameHeight > 0 ? ff.FrameHeight : ff.SourceRect.Height; }
            }
            else
            {
                using var fs = global::FNF_MonoGame.Engine.FsCI.OpenRead(f + ".png");
                p.Tex = Texture2D.FromStream(Game.GraphicsDevice, fs);
                p.W = p.Tex.Width; p.H = p.Tex.Height;
            }
        }
        catch (Exception ex) { Console.WriteLine("[EDITOR] prop art " + p.Image + ": " + ex.Message); }
    }

    // ================================================================== screen maths (flixel camera, zoom about the centre)
    private Vector2 ToScreen(Vector2 world, float sx, float sy)
    {
        var scroll = _cam - new Vector2(640, 360);
        var s = new Vector2(world.X - scroll.X * sx, world.Y - scroll.Y * sy);
        return (s - new Vector2(640, 360)) * _zoom + new Vector2(640, 360);
    }

    private (Vector2 tl, Vector2 size) ScreenRect(EdProp p)
    {
        if (p.CharSlot != null)
        {
            var pv = _prev.TryGetValue(p.CharSlot, out var q) ? q : null;
            var tl = ToScreen(CharTopLeft(p.CharSlot), 1, 1);
            return (tl, new Vector2(pv?.W ?? 300, pv?.H ?? 400) * _zoom);
        }
        return (ToScreen(new Vector2(p.X, p.Y), p.ScrollX, p.ScrollY), new Vector2(p.W * p.ScaleX, p.H * p.ScaleY) * _zoom);
    }

    // ================================================================== update
    protected override void EdUpdate(float dt)
    {
        foreach (var l in _layers) l.Spr?.Update(dt);
        foreach (var pv in _prev.Values) pv.Spr?.Update(dt);
        var sel = _sel >= 0 && _sel < _layers.Count ? _layers[_sel] : null;
        if (sel != null)
        {
            float m = Ed.Shift ? 10f : 1f; Vector2 d = Vector2.Zero;
            if (Ed.Key(Keys.Left)) d.X -= m; if (Ed.Key(Keys.Right)) d.X += m; if (Ed.Key(Keys.Up)) d.Y -= m; if (Ed.Key(Keys.Down)) d.Y += m;
            var rs = new Vector2(Input.GamePadAxisValue("RightX"), -Input.GamePadAxisValue("RightY"));
            if (rs.Length() > 0.3f) d += rs * dt * 300f;
            if (d != Vector2.Zero) Move(sel, d);
            if (Ed.Key(Keys.PageUp) && _sel < _layers.Count - 1) { (_layers[_sel], _layers[_sel + 1]) = (_layers[_sel + 1], _layers[_sel]); _sel++; }
            if (Ed.Key(Keys.PageDown) && _sel > 0) { (_layers[_sel], _layers[_sel - 1]) = (_layers[_sel - 1], _layers[_sel]); _sel--; }
            if (Ed.Key(Keys.Delete) && sel.CharSlot == null) { _layers.RemoveAt(_sel); _sel = -1; }
        }
        if (Ed.PadWorld)
        {   // pad tools: LB/RB pick a layer, D-pad moves it, A raises it, X cycles the camera view, triggers zoom
            if (_layers.Count > 0 && Ed.Pad(Buttons.LeftShoulder)) _sel = _sel <= 0 ? _layers.Count - 1 : _sel - 1;
            if (_layers.Count > 0 && Ed.Pad(Buttons.RightShoulder)) _sel = (_sel + 1) % _layers.Count;
            var psel = _sel >= 0 && _sel < _layers.Count ? _layers[_sel] : null;
            if (psel != null && Ed.PadDir != Vector2.Zero) Move(psel, Ed.PadDir * 5f);
            if (psel != null && Ed.Pad(Buttons.A) && _sel < _layers.Count - 1) { (_layers[_sel], _layers[_sel + 1]) = (_layers[_sel + 1], _layers[_sel]); _sel++; }
            if (Ed.Pad(Buttons.X)) _camPreview = (_camPreview + 1) % CamNames.Length;
            if (Ed.LT) _zoom = Math.Max(0.1f, _zoom / 1.2f);
            if (Ed.RT) _zoom = Math.Min(3f, _zoom * 1.2f);
        }
        if (Ed.Key(Keys.C)) _camPreview = (_camPreview + 1) % CamNames.Length;
        if (Ed.Key(Keys.F1)) SavePsych();
        if (Ed.Key(Keys.F2)) SaveVSlice();
        if (Ed.Key(Keys.Q)) _zoom = Math.Max(0.1f, _zoom / 1.2f);
        if (Ed.Key(Keys.E)) _zoom = Math.Min(3f, _zoom * 1.2f);
        if (Ed.Wheel != 0 && !Ed.OverUI) _zoom = Math.Clamp(_zoom * (Ed.Wheel > 0 ? 1.15f : 1f / 1.15f), 0.1f, 3f);
        float cs = 600f * dt / _zoom;
        if (Ed.KeyHeld(Keys.J)) _cam.X -= cs; if (Ed.KeyHeld(Keys.L)) _cam.X += cs; if (Ed.KeyHeld(Keys.I)) _cam.Y -= cs; if (Ed.KeyHeld(Keys.K)) _cam.Y += cs;
        var ls = new Vector2(Input.GamePadAxisValue("LeftX"), Input.GamePadAxisValue("LeftY"));
        if (ls.Length() > 0.3f && (!Ed.PadMode || Ed.PadWorld)) { _cam.X += ls.X * cs; _cam.Y -= ls.Y * cs; }

        if (_dragging)
        {
            if (!Ed.Down) _dragging = false;
            else if (sel != null)
            {
                var d = (Ed.M - _dragStart) / _zoom;
                if (sel.CharSlot != null) _slot[sel.CharSlot] = new Vector2(MathF.Round(_dragObj.X + d.X), MathF.Round(_dragObj.Y + d.Y));
                else { sel.X = MathF.Round(_dragObj.X + d.X); sel.Y = MathF.Round(_dragObj.Y + d.Y); }
            }
        }
        else if (Ed.WorldClick)
        {
            _sel = -1;
            for (int i = _layers.Count - 1; i >= 0; i--)
            {
                var (tl, size) = ScreenRect(_layers[i]);
                if (Ed.M.X >= tl.X && Ed.M.X < tl.X + size.X && Ed.M.Y >= tl.Y && Ed.M.Y < tl.Y + size.Y) { _sel = i; break; }
            }
            if (_sel >= 0)
            {
                var p = _layers[_sel];
                _dragging = true; _dragStart = Ed.M; _dragObj = p.CharSlot != null ? _slot[p.CharSlot] : new Vector2(p.X, p.Y);
                _tab = 0;
            }
        }
        if (_panning) { if (!(Ed.RDown || Ed.MDown)) _panning = false; else _cam = _camStart - (Ed.M - _panStart) / _zoom; }
        else if ((Ed.RClick || Ed.MDown) && !Ed.OverUI) { _panning = true; _panStart = Ed.M; _camStart = _cam; }
    }

    private void Move(EdProp p, Vector2 d)
    {
        if (p.CharSlot != null) _slot[p.CharSlot] += d; else { p.X += d.X; p.Y += d.Y; }
    }

    // ================================================================== draw
    protected override void DrawWorld(SpriteBatch sb)
    {
        FunkUI.Begin(sb);
        var scroll = _cam - new Vector2(640, 360);
        Backdrop(scroll.X + 640 - 640 / _zoom, scroll.Y + 360 - 360 / _zoom, _zoom, new Color(58, 44, 82), new Color(70, 54, 98));
        sb.End();
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp);
        var px = Assets.Pixel;
        for (int i = 0; i < _layers.Count; i++)
        {
            var p = _layers[i];
            if (p.CharSlot != null)
            {
                if (p.CharSlot == "gf" && _hideGf) continue;
                if (_prev.TryGetValue(p.CharSlot, out var pv) && pv.Spr != null) pv.Draw(sb, ToScreen(CharTopLeft(p.CharSlot), 1, 1), _zoom, Color.White, p.CharSlot == "bf");
                else { var (tl, size) = ScreenRect(p); sb.Draw(px, new Rectangle((int)tl.X, (int)tl.Y, (int)size.X, (int)size.Y), new Color(Ed.Cyan, 0.5f)); }
                continue;
            }
            var (stl, ssz) = ScreenRect(p);
            var tint = p.Type == "square" ? ParseColor(p.Color) * p.Alpha : new Color(ParseColor(p.Color), p.Alpha);
            if (p.Type == "square") { sb.Draw(px, new Rectangle((int)stl.X, (int)stl.Y, (int)ssz.X, (int)ssz.Y), tint); continue; }
            if (p.Spr != null)
            {
                p.Spr.Position = stl; p.Spr.Scale = new Vector2(p.ScaleX, p.ScaleY) * _zoom; p.Spr.Tint = tint;
                p.Spr.Effects = p.FlipX ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                p.Spr.Draw(sb);
            }
            else if (p.Tex != null)
                sb.Draw(p.Tex, stl + ssz / 2f, null, tint, MathHelper.ToRadians(p.Angle), new Vector2(p.Tex.Width / 2f, p.Tex.Height / 2f), new Vector2(p.ScaleX, p.ScaleY) * _zoom, p.FlipX ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
            else sb.Draw(px, new Rectangle((int)stl.X, (int)stl.Y, (int)ssz.X, (int)ssz.Y), new Color(Ed.Red, 0.4f));
        }
        sb.End();
    }

    private static Color ParseColor(string hex)
    {
        hex = (hex ?? "FFFFFF").TrimStart('#');
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex.Substring(2);
        if (hex.Length == 8) hex = hex.Substring(2);
        try { return new Color(Convert.ToInt32(hex[..2], 16), Convert.ToInt32(hex[2..4], 16), Convert.ToInt32(hex[4..6], 16)); } catch { return Color.White; }
    }

    /// <summary>Psych PlayState camera focus: midpoint + (150,-100) dad / (-100,-100) bf / (0,0) gf, + char camera_position + stage camera offsets.</summary>
    private Vector2 CamFocus(string k)
    {
        var pv = _prev.TryGetValue(k, out var p) ? p : null;
        var mid = CharTopLeft(k) + new Vector2((pv?.W ?? 0) / 2f, (pv?.H ?? 0) / 2f);
        var cp = pv?.Data == null ? Vector2.Zero : new Vector2(pv.Data.CamX, pv.Data.CamY);
        return k switch
        {
            "bf" => mid + new Vector2(-100, -100) + new Vector2(-cp.X, cp.Y) + _camOff["bf"],
            "dad" => mid + new Vector2(150, -100) + cp + _camOff["dad"],
            _ => mid + cp + _camOff["gf"],
        };
    }

    protected override void PadSave() => SavePsych();
    protected override List<(Buttons, string)> PadWorldHints() => new()
    {
        (Buttons.LeftShoulder, "prev layer"), (Buttons.RightShoulder, "next layer"), (FNF_MonoGame.Engine.ButtonPrompts.DPadAll, "move"), (Buttons.A, "layer up"),
        (Buttons.X, "camera view"), (Buttons.LeftTrigger, "zoom-"), (Buttons.RightTrigger, "zoom+"), (Buttons.LeftStick, "pan (stick)"),
    };

    protected override void DrawUI()
    {
        // selection + camera preview overlays
        if (_sel >= 0 && _sel < _layers.Count)
        {
            var (tl, sz) = ScreenRect(_layers[_sel]);
            FunkUI.MarkerRect(tl.X, tl.Y, sz.X, sz.Y, 3f, Ed.Pink, 7);
            FunkUI.Text(_layers[_sel].Name, tl.X + 4f, tl.Y - 20f, 15f, Color.White, 0f, 2f);
        }
        foreach (var k in new[] { "bf", "dad", "gf" })
        {
            var f = ToScreen(CamFocus(k), 1, 1);
            var c = k == "bf" ? Ed.Cyan : k == "dad" ? Ed.Purple : Ed.Pink;
            FunkUI.Fill(f.X - 10f, f.Y - 2f, 20f, 4f, c); FunkUI.Fill(f.X - 2f, f.Y - 10f, 4f, 20f, c);
        }
        if (_camPreview > 0)
        {
            var f = ToScreen(CamFocus(CamNames[_camPreview]), 1, 1);
            float vw = 1280f / _zoomStage * _zoom, vh = 720f / _zoomStage * _zoom;
            FunkUI.MarkerRect(f.X - vw / 2f, f.Y - vh / 2f, vw, vh, 3f, Ed.Yellow, 11);
            FunkUI.Text("GAME VIEW (" + CamNames[_camPreview] + ")", f.X - vw / 2f + 6f, f.Y - vh / 2f + 6f, 15f, Ed.Yellow, 0f, 2f);
        }

        // layer list
        Ed.Panel(16f, 84f, 250f, 420f, Ed.Paper, "LAYERS (top = front)", Ed.Yellow, 6);
        var names = new List<string>();
        for (int i = _layers.Count - 1; i >= 0; i--) names.Add((_layers[i].CharSlot != null ? "* " : "") + _layers[i].Name);
        int shown = _sel >= 0 ? _layers.Count - 1 - _sel : -1;
        int ns = Ed.List("layers", names, shown, 28f, 100f, 226f, 310f, ref _layerScroll, Ed.Yellow);
        if (ns != shown && ns >= 0) { _sel = _layers.Count - 1 - ns; _tab = 0; }
        if (Ed.Button("UP", 28f, 420f, 70f, 26f, Color.White) && _sel >= 0 && _sel < _layers.Count - 1) { (_layers[_sel], _layers[_sel + 1]) = (_layers[_sel + 1], _layers[_sel]); _sel++; }
        if (Ed.Button("DOWN", 104f, 420f, 70f, 26f, Color.White) && _sel > 0) { (_layers[_sel], _layers[_sel - 1]) = (_layers[_sel - 1], _layers[_sel]); _sel--; }
        if (Ed.Button("DEL", 180f, 420f, 74f, 26f, Ed.Red) && _sel >= 0 && _layers[_sel].CharSlot == null) { _layers.RemoveAt(_sel); _sel = -1; }
        _camPreview = Ed.Choice("Cam", CamNames, _camPreview, 28f, 458f, 226f, 50f);

        float x = 910f, y = 70f, w = 350f, h = 600f;
        Ed.Panel(x, y, w, h, Ed.Paper, _fmt == "vslice" ? "V-SLICE STAGE" : "PSYCH STAGE", Ed.Yellow, 8);
        for (int i = 0; i < Tabs.Length; i++) if (Ed.Button(Tabs[i], x + 12f + i * 110f, y + 14f, 104f, 26f, Ed.Yellow, _tab == i)) _tab = i;
        float cx = x + 14f, cy = y + 54f, cw = w - 28f;
        if (_tab == 0) ObjTab(cx, cy, cw); else if (_tab == 1) StageTab(cx, cy, cw); else FileTab(cx, cy, cw);
        Ed.Label($"zoom {_zoom:0.00}  cam {(int)_cam.X},{(int)_cam.Y}", 300f, 680f - 6f, 13f, Color.White);
        Ed.HelpTape("click select  drag move  ARROWS nudge (SHIFT x10)  PGUP/PGDN layer  DEL  right-drag/IJKL pan  wheel/QE zoom  C game view  F1 Psych  F2 V-Slice");
    }

    private void ObjTab(float x, float y, float w)
    {
        var p = _sel >= 0 && _sel < _layers.Count ? _layers[_sel] : null;
        if (p == null) { Ed.Label("Click a sprite or a character.", x, y); y += 40; }
        else if (p.CharSlot != null)
        {
            Ed.Label("Character slot: " + p.CharSlot, x, y, 15f, Ed.Pink); y += 24;
            var v = _slot[p.CharSlot]; float sx = v.X, sy = v.Y;
            if (Ed.Num("slx", "Slot X", ref sx, 5f, x, y, w)) _slot[p.CharSlot] = new Vector2(sx, sy); y += 30;
            if (Ed.Num("sly", "Slot Y", ref sy, 5f, x, y, w)) _slot[p.CharSlot] = new Vector2(sx, sy); y += 30;
            var c = _camOff[p.CharSlot]; float cxo = c.X, cyo = c.Y;
            if (Ed.Num("ccx", "Camera X", ref cxo, 5f, x, y, w)) _camOff[p.CharSlot] = new Vector2(cxo, cyo); y += 30;
            if (Ed.Num("ccy", "Camera Y", ref cyo, 5f, x, y, w)) _camOff[p.CharSlot] = new Vector2(cxo, cyo); y += 30;
            string id = _charId[p.CharSlot];
            if (Ed.Field("cid", "Preview char", ref id, x, y, w)) { _charId[p.CharSlot] = id.Trim(); _prev[p.CharSlot] = EdCharPreview.Load(Game, id.Trim(), _mod ?? EdProject.ModName); } y += 30;
            Ed.Label("Psych: slot = top-left, + char 'position'", x, y, 12f); y += 16;
            Ed.Label("V-Slice: saved as feet = slot + (w/2, h)", x, y, 12f); y += 30;
        }
        else
        {
            string s = p.Name; if (Ed.Field("pn", "Name", ref s, x, y, w)) p.Name = s; y += 30;
            s = p.Image; if (Ed.Field("pi", "Image", ref s, x, y, w)) { p.Image = s.Trim(); LoadPropArt(p); } y += 30;
            Ed.Num("px", "X", ref p.X, 5f, x, y, w); y += 30;
            Ed.Num("py", "Y", ref p.Y, 5f, x, y, w); y += 30;
            Ed.Num("psx", "Scale X", ref p.ScaleX, 0.05f, x, y, w); y += 30;
            Ed.Num("psy", "Scale Y", ref p.ScaleY, 0.05f, x, y, w); y += 30;
            Ed.Num("pfx", "Scroll X", ref p.ScrollX, 0.05f, x, y, w); y += 30;
            Ed.Num("pfy", "Scroll Y", ref p.ScrollY, 0.05f, x, y, w); y += 30;
            Ed.Num("pa", "Alpha", ref p.Alpha, 0.05f, x, y, w); y += 30;
            Ed.Num("pan", "Angle", ref p.Angle, 5f, x, y, w); y += 30;
            s = p.Color; if (Ed.Field("pc", "Color", ref s, x, y, w)) p.Color = s.Trim().TrimStart('#'); y += 32;
            Ed.Toggle("Flip X", ref p.FlipX, x, y, 90f); Ed.Toggle("Pixel", ref p.NoAA, x + 100f, y, 90f);
            if (Ed.Button("DUPLICATE", x + w - 110f, y - 2f, 110f, 26f, Ed.Cyan)) { var c = p.CloneData(); c.Name += "2"; c.X += 40; LoadPropArt(c); _layers.Insert(_sel + 1, c); _sel++; }
            y += 34;
        }
        Ed.Label("Add sprite (image path under images/):", x, y, 13f, Ed.Pink); y += 18;
        Ed.Field("ni", "", ref _newImage, x, y, w - 90f, 0f);
        if (Ed.Button("ADD", x + w - 84f, y - 1f, 84f, 28f, Ed.Green))
        {
            var np = new EdProp { Name = Path.GetFileName(_newImage.Trim()), Image = _newImage.Trim(), X = _cam.X - 200, Y = _cam.Y - 150 };
            LoadPropArt(np);
            int at = Math.Max(0, _layers.FindIndex(l => l.CharSlot != null));
            _layers.Insert(at, np); _sel = at;
            Ed.Toast(np.Tex != null || np.Sheet != null ? $"Added '{np.Name}'" : $"Image '{np.Image}' not found (red box)", np.Tex != null || np.Sheet != null ? Ed.Green : Ed.Red);
        }
    }

    private void StageTab(float x, float y, float w)
    {
        string s = _id; if (Ed.Field("sid", "Stage id", ref s, x, y, w)) _id = s.Trim(); y += 30;
        s = _dir; if (Ed.Field("sdir", "Directory", ref s, x, y, w)) _dir = s.Trim(); y += 30;
        Ed.Num("sz", "Zoom", ref _zoomStage, 0.05f, x, y, w); y += 30;
        Ed.Num("scs", "Cam speed", ref _camSpeed, 0.1f, x, y, w); y += 32;
        Ed.Toggle("Hide girlfriend", ref _hideGf, x, y, 200f); y += 34;
        foreach (var k in new[] { "bf", "dad", "gf" })
        {
            string id = _charId[k];
            if (Ed.Field("pc" + k, "Preview " + k, ref id, x, y, w)) { _charId[k] = id.Trim(); _prev[k] = EdCharPreview.Load(Game, id.Trim(), _mod ?? EdProject.ModName); }
            y += 30;
        }
        y += 10;
        Ed.Label("Game view = 1280x720 / zoom around the", x, y, 13f); y += 16;
        Ed.Label("camera focus (C key / Cam picker).", x, y, 13f);
    }

    private void FileTab(float x, float y, float w)
    {
        int s = Ed.List("stages", _files.Select(f => f.label).ToList(), _fileSel, x, y, w, 250f, ref _fileScroll, Ed.Yellow, 22f);
        if (s != _fileSel) _fileSel = s;
        y += 258;
        if (Ed.Button("LOAD", x, y, 100f, 28f, Ed.Cyan) && _fileSel >= 0) LoadStage(_files[_fileSel].path);
        if (Ed.Button("NEW", x + 106f, y, 100f, 28f, Ed.Yellow)) { _mod = EdProject.ModName; NewStage(); }
        if (Ed.Button("RESCAN", x + 212f, y, w - 212f, 28f, Color.White)) RefreshFiles();
        y += 38;
        if (Ed.Button("SAVE PSYCH", x, y, (w - 6f) / 2f, 30f, Ed.Purple)) SavePsych();
        if (Ed.Button("SAVE V-SLICE", x + (w + 6f) / 2f, y, (w - 6f) / 2f, 30f, Ed.Orange)) SaveVSlice();
        y += 40;
        if (Ed.Button("COPY IMAGES INTO MOD", x, y, w, 28f, Ed.Green, false, "Copies every prop image into mods/<mod>/images/ so the stage works on its own")) CopyImages();
        y += 40;
        Ed.Label("src: " + EdProject.Rel(_source), x, y, 12f);
    }

    private void CopyImages()
    {
        try
        {
            string root = NeedRoot(); int n = 0;
            foreach (var p in _layers.Where(l => l.CharSlot == null && l.Type != "square"))
            {
                string f = ResolveImage(p.Image);
                if (f == null) continue;
                string name = Path.GetFileName(f);
                string dst = Path.Combine(root, "images", name);
                if (!string.Equals(Path.GetFullPath(f), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(f + ".png", dst + ".png", true);
                    if (global::FNF_MonoGame.Engine.FsCI.FileExists(f + ".xml")) File.Copy(f + ".xml", dst + ".xml", true);
                }
                p.Image = name; n++;
            }
            _dir = ""; _mod = EdProject.ModName;
            Ed.Toast($"{n} images copied into {EdProject.Rel(Path.Combine(root, "images"))}", Ed.Green);
        }
        catch (Exception ex) { Ed.Toast("Copy failed: " + ex.Message, Ed.Red); }
    }

    private static JArray A(float a, float b) => new(Math.Round(a, 3), Math.Round(b, 3));

    private void SavePsych()
    {
        try
        {
            var objs = new JArray();
            foreach (var p in _layers)
            {
                if (p.CharSlot != null) { objs.Add(new JObject { ["type"] = p.CharSlot == "bf" ? "boyfriend" : p.CharSlot }); continue; }
                var o = new JObject
                {
                    ["type"] = p.Type == "square" ? "square" : p.Anims != null && p.Anims.Count > 0 ? "animatedSprite" : "sprite",
                    ["name"] = p.Name, ["image"] = p.Image, ["x"] = Math.Round(p.X, 3), ["y"] = Math.Round(p.Y, 3),
                    ["scale"] = A(p.ScaleX, p.ScaleY), ["scroll"] = A(p.ScrollX, p.ScrollY), ["color"] = p.Color, ["alpha"] = p.Alpha, ["angle"] = p.Angle,
                    ["flipX"] = p.FlipX, ["flipY"] = false, ["antialiasing"] = !p.NoAA, ["filters"] = 3, // Psych StageEditorState default LOW_QUALITY|HIGH_QUALITY (15 = story+freeplay bits hides it everywhere: validateVisibility dangling else)
                };
                if (p.Anims != null && p.Anims.Count > 0) { o["animations"] = PsychAnims(p.Anims); o["firstAnimation"] = string.IsNullOrEmpty(p.FirstAnim) ? o["animations"][0].Value<string>("anim") : p.FirstAnim; }
                objs.Add(o);
            }
            var j = new JObject
            {
                ["directory"] = _dir, ["defaultZoom"] = Math.Round(_zoomStage, 3), ["isPixelStage"] = false, ["stageUI"] = "normal",
                ["boyfriend"] = A(_slot["bf"].X, _slot["bf"].Y), ["girlfriend"] = A(_slot["gf"].X, _slot["gf"].Y), ["opponent"] = A(_slot["dad"].X, _slot["dad"].Y),
                ["hide_girlfriend"] = _hideGf,
                ["camera_boyfriend"] = A(_camOff["bf"].X, _camOff["bf"].Y), ["camera_opponent"] = A(_camOff["dad"].X, _camOff["dad"].Y), ["camera_girlfriend"] = A(_camOff["gf"].X, _camOff["gf"].Y),
                ["camera_speed"] = _camSpeed,
                ["objects"] = objs,
                ["_editorMeta"] = new JObject { ["gf"] = _charId["gf"], ["dad"] = _charId["dad"], ["boyfriend"] = _charId["bf"] },
            };
            string path = Path.Combine(NeedRoot(), "stages", _id + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, j.ToString(Formatting.Indented));
            Ed.Toast("Saved Psych stage: " + EdProject.Rel(path), Ed.Green); RefreshFiles();
        }
        catch (Exception ex) { Ed.Toast("Save failed: " + ex.Message, Ed.Red); }
    }

    private static JArray PsychAnims(JArray a)
    {
        var o = new JArray();
        foreach (var x in a)
            o.Add(x["anim"] != null ? x.DeepClone() : new JObject { ["anim"] = x.Value<string>("name"), ["name"] = x.Value<string>("prefix"), ["fps"] = x.Value<int?>("frameRate") ?? 24, ["loop"] = x.Value<bool?>("looped") ?? false, ["indices"] = x["frameIndices"]?.DeepClone() ?? new JArray(), ["offsets"] = x["offsets"]?.DeepClone() ?? new JArray(0, 0) });
        return o;
    }
    private static JArray VSliceAnims(JArray a)
    {
        var o = new JArray();
        if (a == null) return o;
        foreach (var x in a)
            o.Add(x["prefix"] != null ? x.DeepClone() : new JObject { ["name"] = x.Value<string>("anim"), ["prefix"] = x.Value<string>("name"), ["frameRate"] = x.Value<int?>("fps") ?? 24, ["looped"] = x.Value<bool?>("loop") ?? false, ["frameIndices"] = x["indices"]?.DeepClone() ?? new JArray(), ["offsets"] = x["offsets"]?.DeepClone() ?? new JArray(0, 0) });
        return o;
    }

    private void SaveVSlice()
    {
        try
        {
            var props = new JArray(); var chars = new JObject();
            for (int i = 0; i < _layers.Count; i++)
            {
                var p = _layers[i]; int z = (i + 1) * 10;
                if (p.CharSlot != null)
                {
                    var pv = _prev.TryGetValue(p.CharSlot, out var q) ? q : null;
                    var feet = CharTopLeft(p.CharSlot) + new Vector2((pv?.W ?? 0) / 2f, pv?.H ?? 0) - (pv?.Data?.SourceFormat == "vslice" ? new Vector2(pv.Data.PosX, pv.Data.PosY) : Vector2.Zero);
                    chars[p.CharSlot] = new JObject { ["zIndex"] = z, ["position"] = A(feet.X, feet.Y), ["cameraOffsets"] = A(_camOff[p.CharSlot].X, _camOff[p.CharSlot].Y) };
                    continue;
                }
                var o = new JObject
                {
                    ["name"] = p.Name, ["assetPath"] = p.Type == "square" ? "#" + p.Color : p.Image, ["position"] = A(p.X, p.Y), ["zIndex"] = z,
                    ["scale"] = A(p.ScaleX, p.ScaleY), ["scroll"] = A(p.ScrollX, p.ScrollY), ["alpha"] = p.Alpha, ["isPixel"] = p.NoAA, ["flipX"] = p.FlipX,
                    ["animType"] = "sparrow", ["danceEvery"] = 0, ["animations"] = VSliceAnims(p.Anims),
                };
                if (!string.IsNullOrEmpty(p.FirstAnim)) o["startingAnimation"] = p.FirstAnim;
                props.Add(o);
            }
            var j = new JObject { ["version"] = "1.0.0", ["name"] = _id, ["cameraZoom"] = Math.Round(_zoomStage, 3), ["directory"] = _dir, ["props"] = props, ["characters"] = chars };
            string path = Path.Combine(NeedRoot(), "data", "stages", _id + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, j.ToString(Formatting.Indented));
            Ed.Toast("Saved V-Slice stage: " + EdProject.Rel(path), Ed.Green); RefreshFiles();
        }
        catch (Exception ex) { Ed.Toast("Save failed: " + ex.Message, Ed.Red); }
    }
}
