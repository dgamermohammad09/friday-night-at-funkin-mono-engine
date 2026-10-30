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

public class EdAnim
{
    public string Name = "idle", Prefix = "", AnimType = "";
    public float Fps = 24; public bool Loop; public int[] Indices = new int[0];
    public float OffX, OffY;
    public EdAnim Clone() { var a = (EdAnim)MemberwiseClone(); a.Indices = (int[])Indices.Clone(); return a; }
}

/// <summary>Character data merged from Psych (characters/*.json) and V-Slice (data/characters/*.json).</summary>
public class EdChar
{
    public string Id = "newchar", Image = "characters/BOYFRIEND", RenderType = "sparrow", Icon = "bf", StartAnim = "idle", Source = "", SourceFormat = "psych";
    public float Scale = 1, PosX, PosY, CamX, CamY, SingDur = 4, DanceEvery = 1;
    public bool FlipX, NoAA, IsPlayer;
    public int[] HealthRgb = { 161, 161, 161 };
    public List<EdAnim> Anims = new();

    public static EdChar LoadAny(string path)
    {
        var o = JObject.Parse(EdSong.SanitizeJson(global::FNF_MonoGame.Engine.FsCI.ReadAllText(path)));
        var c = new EdChar { Id = Path.GetFileNameWithoutExtension(path), Source = path };
        if (o["image"] != null || o["healthicon"] != null || o["sing_duration"] != null)
        {   // Psych Character.CharacterFile
            c.SourceFormat = "psych";
            c.Image = o.Value<string>("image") ?? c.Image;
            c.Scale = o.Value<float?>("scale") ?? 1;
            c.SingDur = o.Value<float?>("sing_duration") ?? 4;
            c.Icon = o.Value<string>("healthicon") ?? c.Id;
            if (o["position"] is JArray p && p.Count >= 2) { c.PosX = p[0].Value<float>(); c.PosY = p[1].Value<float>(); }
            if (o["camera_position"] is JArray cp && cp.Count >= 2) { c.CamX = cp[0].Value<float>(); c.CamY = cp[1].Value<float>(); }
            c.FlipX = o.Value<bool?>("flip_x") ?? false;
            c.NoAA = o.Value<bool?>("no_antialiasing") ?? false;
            c.IsPlayer = o.Value<bool?>("_editor_isPlayer") ?? false;
            if (o["healthbar_colors"] is JArray hc && hc.Count >= 3) c.HealthRgb = new[] { hc[0].Value<int>(), hc[1].Value<int>(), hc[2].Value<int>() };
            if (o["animations"] is JArray an)
                foreach (var a in an)
                    c.Anims.Add(new EdAnim
                    {
                        Name = a.Value<string>("anim") ?? "idle", Prefix = a.Value<string>("name") ?? "", Fps = a.Value<float?>("fps") ?? 24, Loop = a.Value<bool?>("loop") ?? false,
                        Indices = a["indices"] is JArray ia ? ia.Select(x => x.Value<int>()).ToArray() : new int[0],
                        OffX = a["offsets"] is JArray oa && oa.Count >= 2 ? oa[0].Value<float>() : 0, OffY = a["offsets"] is JArray ob && ob.Count >= 2 ? ob[1].Value<float>() : 0,
                    });
        }
        else
        {   // V-Slice CharacterData
            c.SourceFormat = "vslice";
            c.Image = o.Value<string>("assetPath") ?? c.Image;
            c.RenderType = o.Value<string>("renderType") ?? "sparrow";
            c.Scale = o.Value<float?>("scale") ?? 1;
            c.SingDur = o.Value<float?>("singTime") ?? 8;
            c.DanceEvery = o.Value<float?>("danceEvery") ?? 1;
            c.StartAnim = o.Value<string>("startingAnimation") ?? "idle";
            c.FlipX = o.Value<bool?>("flipX") ?? false;
            c.NoAA = o.Value<bool?>("isPixel") ?? false;
            c.Icon = (o["healthIcon"] as JObject)?.Value<string>("id") ?? c.Id;
            if (o["offsets"] is JArray p && p.Count >= 2) { c.PosX = p[0].Value<float>(); c.PosY = p[1].Value<float>(); }
            if (o["cameraOffsets"] is JArray cp && cp.Count >= 2) { c.CamX = cp[0].Value<float>(); c.CamY = cp[1].Value<float>(); }
            string hb = o.Value<string>("healthBarColor");
            if (!string.IsNullOrEmpty(hb) && hb.TrimStart('#').Length >= 6)
            { var h = hb.TrimStart('#'); c.HealthRgb = new[] { Convert.ToInt32(h[..2], 16), Convert.ToInt32(h[2..4], 16), Convert.ToInt32(h[4..6], 16) }; }
            if (o["animations"] is JArray an)
                foreach (var a in an)
                    c.Anims.Add(new EdAnim
                    {
                        Name = a.Value<string>("name") ?? "idle", Prefix = a.Value<string>("prefix") ?? "", AnimType = a.Value<string>("animType") ?? "", Fps = a.Value<float?>("frameRate") ?? 24, Loop = a.Value<bool?>("looped") ?? false,
                        Indices = a["frameIndices"] is JArray ia ? ia.Select(x => x.Value<int>()).ToArray() : new int[0],
                        OffX = a["offsets"] is JArray oa && oa.Count >= 2 ? oa[0].Value<float>() : 0, OffY = a["offsets"] is JArray ob && ob.Count >= 2 ? ob[1].Value<float>() : 0,
                    });
        }
        return c;
    }

    public JObject ToPsych() => new()
    {
        ["animations"] = new JArray(Anims.Select(a => new JObject { ["offsets"] = new JArray((int)Math.Round(a.OffX), (int)Math.Round(a.OffY)), ["loop"] = a.Loop, ["fps"] = (int)a.Fps, ["anim"] = a.Name, ["indices"] = new JArray(a.Indices), ["name"] = a.Prefix })),
        ["vocals_file"] = "",
        ["no_antialiasing"] = NoAA,
        ["image"] = Image,
        ["position"] = new JArray(PosX, PosY),
        ["healthicon"] = Icon,
        ["flip_x"] = FlipX,
        ["healthbar_colors"] = new JArray(HealthRgb),
        ["camera_position"] = new JArray(CamX, CamY),
        ["sing_duration"] = SingDur,
        ["scale"] = Scale,
        ["_editor_isPlayer"] = IsPlayer,
    };

    public JObject ToVSlice() => new()
    {
        ["version"] = "1.0.1",
        ["name"] = Id,
        ["renderType"] = RenderType,
        ["assetPath"] = Image,
        ["scale"] = Scale,
        ["healthIcon"] = new JObject { ["id"] = Icon, ["flipX"] = false, ["isPixel"] = NoAA, ["offsets"] = new JArray(0, 0), ["scale"] = 1 },
        ["offsets"] = new JArray(PosX, PosY),
        ["cameraOffsets"] = new JArray(CamX, CamY),
        ["isPixel"] = NoAA,
        ["flipX"] = FlipX,
        ["singTime"] = SingDur,
        ["danceEvery"] = DanceEvery,
        ["startingAnimation"] = StartAnim,
        ["healthBarColor"] = $"#{HealthRgb[0]:X2}{HealthRgb[1]:X2}{HealthRgb[2]:X2}",
        ["animations"] = new JArray(Anims.Select(a =>
        {
            var j = new JObject { ["name"] = a.Name, ["prefix"] = a.Prefix, ["offsets"] = new JArray(a.OffX, a.OffY), ["looped"] = a.Loop, ["frameRate"] = (int)a.Fps };
            if (a.Indices.Length > 0) j["frameIndices"] = new JArray(a.Indices);
            if (!string.IsNullOrEmpty(a.AnimType)) j["animType"] = a.AnimType;
            return j;
        })),
    };
}

/// <summary>
/// Merged character editor: Psych CharacterEditorState (anim list, arrow-key offsets, ghost, camera point, health
/// colour, position) + V-Slice DebugBoundingState (offset onion skin, flip, sparrow/atlas sheet). Saves Psych
/// characters/&lt;id&gt;.json and V-Slice data/characters/&lt;id&gt;.json.
/// Keys: W/S anim, SPACE replay, ARROWS offset (SHIFT x10), drag the sprite = offset, right-drag/IJKL = camera,
/// Q/E zoom, G ghost, F flip, F1 save Psych, F2 save V-Slice.
/// </summary>
public class CharacterEditor : EditorBase
{
    protected override string EdTitle => "CHARACTER";
    protected override Color EdColor => Ed.Cyan;

    private EdChar _c = new();
    private SpriteSheet _sheet;
    private AnimatedSprite _spr, _ghostSpr;
    private int _sel;
    private bool _ghost = true, _showCam = true;
    private int _ghostAnim;
    private Vector2 _cam = new(0, 0); private float _zoom = 0.6f;
    private Vector2 _dragStart; private float _dragOx, _dragOy; private bool _dragging, _panning; private Vector2 _panStart, _camStart;
    private List<(string label, string path)> _files = new();
    private int _fileSel = -1; private float _fileScroll, _animScroll;
    private int _tab; private static readonly string[] Tabs = { "ANIMS", "CHARACTER", "FILE" };
    private string _newId = "", _sheetErr = "";
    private List<string> _prefixes = new();
    private float _prefScroll; private int _prefSel = -1;
    // new-anim fields
    private string _aName = "", _aPrefix = "", _aIdx = ""; private float _aFps = 24; private bool _aLoop;

    public override void Load()
    {
        RefreshFiles();
        string dev = Environment.GetEnvironmentVariable("FNF_EDITOR_CHAR");
        var f = _files.FirstOrDefault(x => x.label.EndsWith(string.IsNullOrEmpty(dev) ? "] bf" : dev, StringComparison.OrdinalIgnoreCase));
        if (f.path != null) LoadChar(f.path); else if (_files.Count > 0) LoadChar(_files[0].path);
    }

    private void RefreshFiles()
    {
        _files.Clear();
        void Scan(string dir, string tag)
        {
            if (!global::FNF_MonoGame.Engine.FsCI.DirectoryExists(dir)) return;
            foreach (var j in global::FNF_MonoGame.Engine.FsCI.GetFiles(dir, "*.json").OrderBy(x => x)) _files.Add(($"{tag}{Path.GetFileNameWithoutExtension(j)}", j));
        }
        Scan(Path.Combine(AssetManager.ContentRoot, "data", "characters"), "[V] ");
        Scan(Path.Combine(AssetManager.ContentRoot, "data", "psych", "characters"), "[P] psych/");
        foreach (var m in EdProject.Mods())
        {
            Scan(Path.Combine(EdProject.ModsBase, m, "characters"), $"[P] {m}/");
            Scan(Path.Combine(EdProject.ModsBase, m, "data", "characters"), $"[V] {m}/");
        }
    }

    private string ModOf(string path)
    {
        try
        {
            string mb = Path.GetFullPath(EdProject.ModsBase), f = Path.GetFullPath(path);
            if (f.StartsWith(mb, StringComparison.OrdinalIgnoreCase)) return f.Substring(mb.Length).TrimStart('\\', '/').Split('\\', '/')[0];
        }
        catch { }
        return null;
    }

    private void LoadChar(string path)
    {
        if (string.IsNullOrEmpty(EdProject.ModName)) EdProject.GuessModFrom(path); // opening a mod file targets that mod
        try
        {
            _c = EdChar.LoadAny(path);
            string mod = ModOf(path);
            if (mod != null) FNF_MonoGame.Engine.PsychEngine.PsychMods.CurrentModDirectory = mod;
            LoadSheet();
            _sel = 0; PlaySel();
            var ff = _sheet?.FirstFrame;
            _cam = new Vector2(_c.PosX, _c.PosY) + (ff == null ? Vector2.Zero : new Vector2((ff.FrameWidth > 0 ? ff.FrameWidth : ff.SourceRect.Width) * _c.Scale / 2f, (ff.FrameHeight > 0 ? ff.FrameHeight : ff.SourceRect.Height) * _c.Scale / 2f));
            var tb = _sheet?.GetTimelineBounds(_spr?.ResolvedAnimName ?? "");
            if (tb.HasValue) _cam = new Vector2(_c.PosX + tb.Value.W * _c.Scale / 2f, _c.PosY + tb.Value.H * _c.Scale / 2f);
            Ed.Toast($"Loaded {_c.SourceFormat.ToUpperInvariant()} character '{_c.Id}' ({_c.Anims.Count} anims)", Ed.Green);
        }
        catch (Exception ex) { Ed.Toast("Load failed: " + ex.Message, Ed.Red); }
    }

    private void LoadSheet()
    {
        _sheet = null; _sheetErr = "";
        string img = (_c.Image ?? "").Replace('\\', '/');
        int colon = img.IndexOf(':'); if (colon > 0 && colon < img.Length - 1) img = img.Substring(colon + 1);   // V-Slice library prefix "shared:"
        var tries = new List<string> { "images/" + img, "images/psych/" + img, "game/" + img, img };
        string modRoot = string.IsNullOrEmpty(FNF_MonoGame.Engine.PsychEngine.PsychMods.CurrentModDirectory) ? null : Path.Combine(EdProject.ModsBase, FNF_MonoGame.Engine.PsychEngine.PsychMods.CurrentModDirectory);
        if (modRoot != null) tries.Insert(0, Path.Combine(modRoot, "images", img));
        foreach (var t in tries)
        {
            bool atlas = DirHasAtlas(t);
            try { _sheet = SpriteSheet.Load(Game, t, preRenderComposites: atlas); } catch (Exception ex) { _sheetErr = ex.Message; _sheet = null; }
            if (_sheet != null && (_sheet.Frames.Count > 0 || _sheet.Animations.Count > 0)) { _c.RenderType = atlas ? "animateatlas" : _c.RenderType == "animateatlas" ? "sparrow" : _c.RenderType; break; }
            _sheet = null;
        }
        if (_sheet == null) { _sheetErr = "sheet not found: " + img; Ed.Toast(_sheetErr, Ed.Red); }
        _prefixes = _sheet == null ? new List<string>() : _sheet.Frames.Keys.Select(k => k.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9')).Distinct().OrderBy(x => x).ToList();
        if (_sheet != null && _prefixes.Count == 0) _prefixes = _sheet.Animations.Keys.OrderBy(x => x).ToList();
        _spr = new AnimatedSprite { Sheet = _sheet, UseTimelineAnchoring = _c.RenderType == "animateatlas" };
        _ghostSpr = new AnimatedSprite { Sheet = _sheet, UseTimelineAnchoring = _spr.UseTimelineAnchoring };
        foreach (var a in _c.Anims) Register(a);
    }

    private static bool DirHasAtlas(string rel)
    {
        foreach (var root in new[] { AssetManager.ContentRoot, "" })
        {
            try { string d = Path.IsPathRooted(rel) ? rel : Path.Combine(root, rel); if (global::FNF_MonoGame.Engine.FsCI.FileExists(Path.Combine(d, "Animation.json"))) return true; } catch { }
        }
        return false;
    }

    /// <summary>Registers an animation on the sheet under "ed:&lt;name&gt;" (addByPrefix / addByIndices semantics).</summary>
    private bool Register(EdAnim a)
    {
        if (_sheet == null || string.IsNullOrEmpty(a.Prefix)) return false;
        List<SpriteFrame> frames = _sheet.BuildAnimationFromPrefix(a.Prefix) ?? _sheet.GetAnimation(a.Prefix);
        if (frames == null || frames.Count == 0)
        {   // flixel addByPrefix also accepts frames whose name starts with the prefix
            frames = _sheet.Frames.Where(kv => kv.Key.StartsWith(a.Prefix, StringComparison.Ordinal)).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value).ToList();
        }
        if (frames.Count == 0) return false;
        if (a.Indices.Length > 0) frames = a.Indices.Where(i => i >= 0 && i < frames.Count).Select(i => frames[i]).ToList();
        if (frames.Count == 0) return false;
        _sheet.Animations["ed:" + a.Name] = frames;
        if (_sheet.CompositeAnimations.TryGetValue(a.Prefix, out var comp)) _sheet.CompositeAnimations["ed:" + a.Name] = comp;
        return true;
    }

    /// <summary>Atlas labels/symbols play under their own key (keeps flixel-animate timeline bounds); Sparrow and
    /// index animations use the registered "ed:" alias.</summary>
    public static string Key(SpriteSheet s, EdAnim a)
        => a.Indices.Length == 0 && s != null && (s.CompositeAnimations.ContainsKey(a.Prefix) || s.GetTimelineBounds(a.Prefix).HasValue) && s.Animations.ContainsKey(a.Prefix) ? a.Prefix : "ed:" + a.Name;

    private EdAnim Cur => _sel >= 0 && _sel < _c.Anims.Count ? _c.Anims[_sel] : null;

    private void PlaySel()
    {
        var a = Cur; if (a == null || _spr == null) return;
        if (!_sheet?.Animations.ContainsKey("ed:" + a.Name) ?? true) Register(a);
        if (_sheet == null) return;
        _spr.FrameRate = a.Fps;
        _spr.PlayAnimation(Key(_sheet, a), true, a.Loop);
    }

    protected override void EdUpdate(float dt)
    {
        _spr?.Update(dt);
        int n = _c.Anims.Count;
        if (n > 0)
        {
            if (Ed.Key(Keys.W) || Ed.Pad(Buttons.LeftShoulder)) { _sel = (_sel - 1 + n) % n; PlaySel(); }
            if (Ed.Key(Keys.S) || Ed.Pad(Buttons.RightShoulder)) { _sel = (_sel + 1) % n; PlaySel(); }
        }
        if (Ed.Key(Keys.Space) || Ed.Pad(Buttons.X)) PlaySel();
        var a = Cur;
        if (a != null)
        {
            float m = Ed.Shift ? 10f : 1f;
            if (Ed.Key(Keys.Left)) a.OffX += m;
            if (Ed.Key(Keys.Right)) a.OffX -= m;
            if (Ed.Key(Keys.Up)) a.OffY += m;
            if (Ed.Key(Keys.Down)) a.OffY -= m;
            var rs = new Vector2(Input.GamePadAxisValue("RightX"), Input.GamePadAxisValue("RightY"));
            if (rs.Length() > 0.3f) { a.OffX -= rs.X * dt * 60f; a.OffY += rs.Y * dt * 60f; }
        }
        if (Ed.PadWorld && a != null)
        {   // pad tools: D-pad = offset (Psych arrow keys: left adds x), A = ghost, triggers = zoom
            var pd = Ed.PadDir; a.OffX -= pd.X; a.OffY -= pd.Y;
        }
        if (Ed.PadWorld && Ed.Pad(Buttons.A)) _ghost = !_ghost;
        if (Ed.PadWorld && Ed.LT) _zoom = Math.Max(0.1f, _zoom / 1.2f);
        if (Ed.PadWorld && Ed.RT) _zoom = Math.Min(4f, _zoom * 1.2f);
        if (Ed.Key(Keys.G)) _ghost = !_ghost;
        if (Ed.Key(Keys.F)) _c.FlipX = !_c.FlipX;
        if (Ed.Key(Keys.F1)) SavePsych();
        if (Ed.Key(Keys.F2)) SaveVSlice();
        if (Ed.Key(Keys.Q)) _zoom = Math.Max(0.1f, _zoom / 1.2f);
        if (Ed.Key(Keys.E)) _zoom = Math.Min(4f, _zoom * 1.2f);
        float cs = 500f * dt / _zoom;
        if (Ed.KeyHeld(Keys.J)) _cam.X -= cs; if (Ed.KeyHeld(Keys.L)) _cam.X += cs;
        if (Ed.KeyHeld(Keys.I)) _cam.Y -= cs; if (Ed.KeyHeld(Keys.K)) _cam.Y += cs;
        var ls = new Vector2(Input.GamePadAxisValue("LeftX"), Input.GamePadAxisValue("LeftY"));
        if (ls.Length() > 0.3f && (!Ed.PadMode || Ed.PadWorld)) { _cam.X += ls.X * cs; _cam.Y -= ls.Y * cs; }
        if (Ed.Wheel != 0 && !Ed.OverUI) _zoom = Math.Clamp(_zoom * (Ed.Wheel > 0 ? 1.15f : 1f / 1.15f), 0.1f, 4f);

        // mouse: left-drag the character = move the animation offset (Psych 1.0), right/middle drag = pan
        if (_dragging) { if (!Ed.Down) _dragging = false; else if (a != null) { var d = (Ed.M - _dragStart) / _zoom; a.OffX = MathF.Round(_dragOx - d.X); a.OffY = MathF.Round(_dragOy - d.Y); } }
        else if (Ed.WorldClick && a != null) { _dragging = true; _dragStart = Ed.M; _dragOx = a.OffX; _dragOy = a.OffY; }
        if (_panning) { if (!(Ed.RDown || Ed.MDown)) _panning = false; else _cam = _camStart - (Ed.M - _panStart) / _zoom; }
        else if ((Ed.RClick || Ed.MDown) && !Ed.OverUI) { _panning = true; _panStart = Ed.M; _camStart = _cam; }
    }

    private bool VSliceOffsets => _c.SourceFormat == "vslice";

    private Matrix WorldMatrix => Matrix.CreateTranslation(-_cam.X, -_cam.Y, 0) * Matrix.CreateScale(_zoom) * Matrix.CreateTranslation(640 - 150, 380, 0);

    protected override void DrawWorld(SpriteBatch sb)
    {
        FunkUI.Begin(sb);
        Backdrop(_cam.X - (640 - 150) / _zoom, _cam.Y - 380 / _zoom, _zoom, new Color(232, 225, 210), new Color(205, 196, 180));
        sb.End();
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, _c.NoAA ? SamplerState.PointClamp : SamplerState.LinearClamp, null, null, null, WorldMatrix);
        var px = Assets.Pixel;
        // floor + origin cross (character position = Psych position / V-Slice offsets)
        sb.Draw(px, new Rectangle(-3000, 0, 6000, 3), new Color(Ed.Ink, 0.5f));
        sb.Draw(px, new Rectangle(-1, -3000, 2, 6000), new Color(Ed.Ink, 0.25f));
        if (_spr?.Sheet != null)
        {
            var basePos = new Vector2(_c.PosX, _c.PosY);
            float k = VSliceOffsets ? _c.Scale : 1f;
            if (_ghost && _c.Anims.Count > 0)
            {
                var ga = _c.Anims[Math.Clamp(_ghostAnim, 0, _c.Anims.Count - 1)];
                if (_sheet.Animations.ContainsKey("ed:" + ga.Name))
                {
                    if (_ghostSpr.CurrentAnimation != Key(_sheet, ga)) _ghostSpr.PlayAnimation(Key(_sheet, ga), true, false);
                    _ghostSpr.Position = basePos - new Vector2(ga.OffX, ga.OffY) * k;
                    _ghostSpr.Scale = new Vector2(_c.Scale);
                    _ghostSpr.Effects = _c.FlipX ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                    _ghostSpr.Tint = new Color(80, 160, 255, 110);
                    _ghostSpr.Draw(sb);
                }
            }
            var a = Cur;
            if (a != null)
            {
                _spr.Position = basePos - new Vector2(a.OffX, a.OffY) * k;
                _spr.Scale = new Vector2(_c.Scale);
                _spr.Effects = _c.FlipX ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                _spr.Tint = Color.White;
                _spr.Draw(sb);
            }
            // camera point: Psych getMidpoint + camera_position (+150/-100 player, +150/-100 opponent like PlayState)
            if (_showCam)
            {
                float fw = _spr.GetFrameWidth() * _c.Scale, fh = _spr.GetFrameHeight() * _c.Scale;
                var mid = basePos + new Vector2(fw / 2f, fh / 2f);
                var cp = mid + (_c.IsPlayer ? new Vector2(-100 - _c.CamX, -100 + _c.CamY) : new Vector2(150 + _c.CamX, -100 + _c.CamY));
                sb.Draw(px, new Rectangle((int)cp.X - 14, (int)cp.Y - 2, 28, 4), Ed.Pink);
                sb.Draw(px, new Rectangle((int)cp.X - 2, (int)cp.Y - 14, 4, 28), Ed.Pink);
            }
        }
        sb.End();
    }

    protected override void PadSave() => SavePsych();
    protected override List<(Buttons, string)> PadWorldHints() => new()
    {
        (FNF_MonoGame.Engine.ButtonPrompts.DPadAll, "offset"), (Buttons.LeftShoulder, "prev anim"), (Buttons.RightShoulder, "next anim"), (Buttons.X, "replay"),
        (Buttons.A, "ghost"), (Buttons.LeftTrigger, "zoom-"), (Buttons.RightTrigger, "zoom+"), (Buttons.LeftStick, "pan (stick)"),
    };

    protected override void DrawUI()
    {
        var a = Cur;
        // ---- left: animation list (Psych UI box "Animations")
        Ed.Panel(16f, 84f, 270f, 400f, Ed.Paper, "ANIMATIONS", Ed.Cyan, 2);
        var names = _c.Anims.Select(x => $"{x.Name}  [{Ed.F(x.OffX)}, {Ed.F(x.OffY)}]").ToList();
        int s = Ed.List("anims", names, _sel, 28f, 100f, 246f, 300f, ref _animScroll, Ed.Cyan);
        if (s != _sel) { _sel = s; PlaySel(); }
        Ed.Toggle("Ghost", ref _ghost, 28f, 412f, 80f);
        if (_c.Anims.Count > 0) _ghostAnim = Ed.Choice("", _c.Anims.Select(x => x.Name).ToList(), _ghostAnim, 110f, 410f, 164f, 0f);
        Ed.Toggle("Camera point", ref _showCam, 28f, 444f, 160f);
        if (a != null)
        {
            Ed.Label($"frame {(_spr?.FrameIndex ?? 0) + 1}/{_spr?.FrameCount ?? 0}", 190f, 447f, 13f);
        }
        // ---- bottom-left: offset readout
        if (a != null)
        {
            FunkUI.Push(150f, 530f, FunkUI.Rad(-2f));
            FunkUI.ShadowBlock(24f, 506f, 252f, 48f, Ed.Yellow, 4f, 6f, 6f);
            FunkUI.Text($"{a.Name}", 36f, 512f, 16f, Ed.Ink);
            FunkUI.Text($"offset {Ed.F(a.OffX)}, {Ed.F(a.OffY)}", 36f, 532f, 15f, Ed.Ink);
            FunkUI.Pop();
            Ed.AddRect(24f, 506f, 252f, 48f);
        }
        // ---- health bar preview + icon
        FunkUI.Block(30f, 590f, 240f, 18f, new Color(_c.HealthRgb[0], _c.HealthRgb[1], _c.HealthRgb[2]), 4f);
        FunkUI.HealthIcon(_c.Icon, 250f, 598f, 76f, 0);
        Ed.AddRect(30f, 560f, 270f, 80f);

        // ---- right panel
        float x = 900f, y = 70f, w = 360f, h = 600f;
        Ed.Panel(x, y, w, h, Ed.Paper, _c.SourceFormat == "vslice" ? "V-SLICE CHARACTER" : "PSYCH CHARACTER", Ed.Cyan, 4);
        for (int i = 0; i < Tabs.Length; i++) if (Ed.Button(Tabs[i], x + 12f + i * 114f, y + 14f, 108f, 26f, Ed.Yellow, _tab == i)) _tab = i;
        float cy = y + 54f, cx = x + 14f, cw = w - 28f;
        if (_tab == 0) AnimTab(cx, cy, cw);
        else if (_tab == 1) CharTab(cx, cy, cw);
        else FileTab(cx, cy, cw);
        if (!string.IsNullOrEmpty(_sheetErr)) Ed.Label(_sheetErr, 300f, 80f, 14f, Ed.Red);
        Ed.HelpTape("W/S anim  SPACE replay  ARROWS offset (SHIFT x10)  drag sprite = offset  right-drag/IJKL camera  Q/E wheel zoom  G ghost  F flip  F1 save Psych  F2 save V-Slice");
    }

    private void AnimTab(float x, float y, float w)
    {
        var a = Cur;
        if (a != null)
        {
            Ed.Label("Selected animation", x, y, 15f, Ed.Pink); y += 22;
            string s = a.Name; if (Ed.Field("an", "Name", ref s, x, y, w, 80f)) { a.Name = s; Register(a); PlaySel(); } y += 30;
            s = a.Prefix; if (Ed.Field("ap", "Prefix", ref s, x, y, w, 80f)) { a.Prefix = s; Register(a); PlaySel(); } y += 30;
            s = string.Join(",", a.Indices); if (Ed.Field("ai", "Indices", ref s, x, y, w, 80f)) { a.Indices = ParseIdx(s); Register(a); PlaySel(); } y += 30;
            if (Ed.Num("af", "FPS", ref a.Fps, 1f, x, y, w, 80f)) PlaySel(); y += 30;
            if (Ed.Toggle("Loop", ref a.Loop, x, y, 80f)) PlaySel();
            if (Ed.Button("DELETE", x + w - 100f, y - 2f, 100f, 26f, Ed.Red)) { _c.Anims.RemoveAt(_sel); _sel = Math.Max(0, _sel - 1); PlaySel(); return; }
            y += 30;
            Ed.Num("aox", "Offset X", ref a.OffX, 1f, x, y, w, 80f); y += 30;
            Ed.Num("aoy", "Offset Y", ref a.OffY, 1f, x, y, w, 80f); y += 36;
        }
        Ed.Label("Add anim - pick a sheet prefix:", x, y, 14f, Ed.Pink); y += 20;
        int ps = Ed.List("prefixes", _prefixes, _prefSel, x, y, w, 110f, ref _prefScroll, Ed.Yellow, 20f);
        if (ps != _prefSel) { _prefSel = ps; if (ps >= 0) _aPrefix = _prefixes[ps]; }
        y += 116;
        Ed.Field("nan", "Name", ref _aName, x, y, w, 80f); y += 30;
        Ed.Field("nap", "Prefix", ref _aPrefix, x, y, w, 80f); y += 30;
        Ed.Field("nai", "Indices", ref _aIdx, x, y, w - 90f, 80f);
        if (Ed.Button("ADD", x + w - 84f, y - 1f, 84f, 28f, Ed.Green))
        {
            if (string.IsNullOrWhiteSpace(_aName) || string.IsNullOrWhiteSpace(_aPrefix)) Ed.Toast("Name and prefix needed", Ed.Red);
            else
            {
                var na = new EdAnim { Name = _aName.Trim(), Prefix = _aPrefix, Indices = ParseIdx(_aIdx), Fps = _aFps, Loop = _aLoop };
                var old = _c.Anims.FindIndex(q => q.Name == na.Name);
                if (old >= 0) { na.OffX = _c.Anims[old].OffX; na.OffY = _c.Anims[old].OffY; _c.Anims[old] = na; _sel = old; } else { _c.Anims.Add(na); _sel = _c.Anims.Count - 1; }
                if (!Register(na)) Ed.Toast($"No frames for prefix '{na.Prefix}'", Ed.Red); else Ed.Toast($"Animation '{na.Name}' added");
                PlaySel();
            }
        }
    }

    private static int[] ParseIdx(string s)
    {
        var l = new List<int>();
        foreach (var p in (s ?? "").Split(',', ' ')) if (int.TryParse(p.Trim(), out int v)) l.Add(v);
        return l.ToArray();
    }

    private void CharTab(float x, float y, float w)
    {
        string s = _c.Id; if (Ed.Field("cid", "Char id", ref s, x, y, w)) _c.Id = s.Trim(); y += 30;
        s = _c.Image; if (Ed.Field("cimg", "Image", ref s, x, y, w)) { _c.Image = s.Trim(); LoadSheet(); PlaySel(); } y += 30;
        s = _c.Icon; if (Ed.Field("cico", "Health icon", ref s, x, y, w)) _c.Icon = s.Trim(); y += 30;
        Ed.Num("csc", "Scale", ref _c.Scale, 0.05f, x, y, w); y += 30;
        Ed.Num("cpx", "Position X", ref _c.PosX, 5f, x, y, w); y += 30;
        Ed.Num("cpy", "Position Y", ref _c.PosY, 5f, x, y, w); y += 30;
        Ed.Num("ccx", "Camera X", ref _c.CamX, 5f, x, y, w); y += 30;
        Ed.Num("ccy", "Camera Y", ref _c.CamY, 5f, x, y, w); y += 30;
        Ed.Num("csd", "Sing time", ref _c.SingDur, 0.5f, x, y, w); y += 30;
        float r = _c.HealthRgb[0], g = _c.HealthRgb[1], b = _c.HealthRgb[2];
        if (Ed.Num("chr", "Bar red", ref r, 5f, x, y, w)) _c.HealthRgb[0] = (int)Math.Clamp(r, 0, 255); y += 30;
        if (Ed.Num("chg", "Bar green", ref g, 5f, x, y, w)) _c.HealthRgb[1] = (int)Math.Clamp(g, 0, 255); y += 30;
        if (Ed.Num("chb", "Bar blue", ref b, 5f, x, y, w)) _c.HealthRgb[2] = (int)Math.Clamp(b, 0, 255); y += 32;
        Ed.Toggle("Flip X", ref _c.FlipX, x, y, 100f);
        Ed.Toggle("Pixel / no AA", ref _c.NoAA, x + 110f, y, 150f); y += 28;
        Ed.Toggle("Player side (camera)", ref _c.IsPlayer, x, y, 220f);
        if (Ed.Button("ICON COLOUR", x + w - 120f, y - 30f, 120f, 26f, Ed.Orange, false, "Psych 'Get Icon Color': dominant colour of the health icon")) IconColour();
    }

    private void IconColour()
    {
        var t = FunkUI.Tex("images/icons/icon-" + _c.Icon + ".png");
        if (t == null) { Ed.Toast("icon not found", Ed.Red); return; }
        var data = new Color[t.Width * t.Height]; t.GetData(0, null, data, 0, data.Length);
        var counts = new Dictionary<int, int>(); int best = 0, bestN = 0;
        foreach (var c in data)
        {
            if (c.A < 250 || (c.R < 30 && c.G < 30 && c.B < 30)) continue;
            int key = (c.R / 8) << 10 | (c.G / 8) << 5 | (c.B / 8);
            counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
            if (counts[key] > bestN) { bestN = counts[key]; best = key; }
        }
        _c.HealthRgb = new[] { ((best >> 10) & 31) * 8 + 4, ((best >> 5) & 31) * 8 + 4, (best & 31) * 8 + 4 };
        Ed.Toast("Health bar colour from icon");
    }

    private void FileTab(float x, float y, float w)
    {
        int s = Ed.List("chars", _files.Select(f => f.label).ToList(), _fileSel, x, y, w, 250f, ref _fileScroll, Ed.Cyan, 22f);
        if (s != _fileSel) _fileSel = s;
        y += 258;
        if (Ed.Button("LOAD", x, y, 100f, 28f, Ed.Cyan) && _fileSel >= 0) LoadChar(_files[_fileSel].path);
        if (Ed.Button("RESCAN", x + 106f, y, 100f, 28f, Color.White)) RefreshFiles();
        y += 38;
        if (Ed.Button("SAVE PSYCH", x, y, (w - 6f) / 2f, 30f, Ed.Purple)) SavePsych();
        if (Ed.Button("SAVE V-SLICE", x + (w + 6f) / 2f, y, (w - 6f) / 2f, 30f, Ed.Orange)) SaveVSlice();
        y += 40;
        Ed.Field("cnew", "Save as id", ref _newId, x, y, w - 90f, 100f);
        if (Ed.Button("RENAME", x + w - 84f, y - 1f, 84f, 28f, Ed.Yellow) && !string.IsNullOrWhiteSpace(_newId)) { _c.Id = _newId.Trim(); Ed.Toast($"Character id = {_c.Id}"); }
        y += 34;
        if (Ed.Button("COPY SHEET INTO MOD", x, y, w, 28f, Ed.Green, false, "Copies the sprite sheet to mods/<mod>/images/characters/<id> and points Image at it")) CopySheet();
        y += 36;
        Ed.Label("src: " + EdProject.Rel(_c.Source), x, y, 12f);
    }

    private void CopySheet()
    {
        try
        {
            string root = NeedRoot();
            string img = _c.Image.Replace('\\', '/');
            string found = null;
            foreach (var cand in new[] { Path.Combine(AssetManager.ContentRoot, "images", img), Path.Combine(AssetManager.ContentRoot, "images", "psych", img), Path.Combine(AssetManager.ContentRoot, "game", img),
                                         string.IsNullOrEmpty(FNF_MonoGame.Engine.PsychEngine.PsychMods.CurrentModDirectory) ? "" : Path.Combine(EdProject.ModsBase, FNF_MonoGame.Engine.PsychEngine.PsychMods.CurrentModDirectory, "images", img) })
                if (cand.Length > 0 && global::FNF_MonoGame.Engine.FsCI.FileExists(cand + ".png")) { found = cand; break; }
            if (found == null) { Ed.Toast("Only Sparrow png+xml sheets can be copied", Ed.Red); return; }
            string dst = Path.Combine(root, "images", "characters", _c.Id);
            File.Copy(found + ".png", dst + ".png", true);
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(found + ".xml")) File.Copy(found + ".xml", dst + ".xml", true);
            _c.Image = "characters/" + _c.Id;
            FNF_MonoGame.Engine.PsychEngine.PsychMods.CurrentModDirectory = EdProject.ModName;
            Ed.Toast($"Sheet copied to {EdProject.Rel(dst)}.png", Ed.Green);
        }
        catch (Exception ex) { Ed.Toast("Copy failed: " + ex.Message, Ed.Red); }
    }

    private void SavePsych()
    {
        try
        {
            string p = Path.Combine(NeedRoot(), "characters", _c.Id + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            File.WriteAllText(p, _c.ToPsych().ToString(Formatting.Indented));
            Ed.Toast("Saved Psych character: " + EdProject.Rel(p), Ed.Green); RefreshFiles();
        }
        catch (Exception ex) { Ed.Toast("Save failed: " + ex.Message, Ed.Red); }
    }
    private void SaveVSlice()
    {
        try
        {
            string p = Path.Combine(NeedRoot(), "data", "characters", _c.Id + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            File.WriteAllText(p, _c.ToVSlice().ToString(Formatting.Indented));
            Ed.Toast("Saved V-Slice character: " + EdProject.Rel(p), Ed.Green); RefreshFiles();
        }
        catch (Exception ex) { Ed.Toast("Save failed: " + ex.Message, Ed.Red); }
    }
}
