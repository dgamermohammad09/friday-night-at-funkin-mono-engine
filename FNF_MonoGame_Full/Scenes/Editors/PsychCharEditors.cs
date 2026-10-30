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
using FNF_MonoGame.Engine.PsychEngine;
using FNF_MonoGame.Engine.PsychEngine.Cutscenes;

namespace FNF_MonoGame.Scenes.Editors;

/// <summary>Shared file picker rows for the two Psych character-json editors.</summary>
internal static class EdJsonFiles
{
    /// <summary>[P] psych/&lt;id&gt; (bundled Psych art) + [P] &lt;mod&gt;/&lt;id&gt; for images/&lt;sub&gt;/*.json.</summary>
    public static List<(string label, string path)> Scan(string sub)
    {
        var l = new List<(string, string)>();
        void S(string dir, string tag) { if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(dir)) foreach (var j in global::FNF_MonoGame.Engine.FsCI.GetFiles(dir, "*.json").OrderBy(x => x)) l.Add(($"{tag}{Path.GetFileNameWithoutExtension(j)}", j)); }
        S(Path.Combine(AssetManager.ContentRoot, "images", "psych", sub), "[P] psych/");
        foreach (var m in EdProject.Mods()) S(Path.Combine(EdProject.ModsBase, m, "images", sub), $"[P] {m}/");
        return l;
    }

    /// <summary>Sparrow sheet images/&lt;sub&gt;/&lt;image&gt; next to the json, else in the target mod, else Psych's bundled copy.</summary>
    public static SpriteSheet Sheet(Microsoft.Xna.Framework.Game game, string jsonPath, string sub, string image)
    {
        var dirs = new List<string>();
        if (!string.IsNullOrEmpty(jsonPath)) dirs.Add(Path.GetDirectoryName(jsonPath));
        if (!string.IsNullOrEmpty(EdProject.Root)) dirs.Add(Path.Combine(EdProject.Root, "images", sub));
        dirs.Add(Path.Combine(AssetManager.ContentRoot, "images", "psych", sub));
        foreach (var d in dirs)
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(Path.Combine(d, image + ".png")) && global::FNF_MonoGame.Engine.FsCI.FileExists(Path.Combine(d, image + ".xml")))
                return SpriteSheet.Load((FNFGame)game, Path.Combine(d, image), sparrowOnly: true);
        return null;
    }

    /// <summary>Copies the sheet (png+xml) into mods/&lt;mod&gt;/images/&lt;sub&gt;/ when it lives elsewhere.</summary>
    public static int CopyInto(string root, string jsonPath, string sub, string image)
    {
        int n = 0;
        string dst = Path.Combine(root, "images", sub);
        Directory.CreateDirectory(dst);
        foreach (var d in new[] { string.IsNullOrEmpty(jsonPath) ? null : Path.GetDirectoryName(jsonPath), Path.Combine(AssetManager.ContentRoot, "images", "psych", sub) })
        {
            if (d == null) continue;
            foreach (var ext in new[] { ".png", ".xml" })
            {
                string s = Path.Combine(d, image + ext), t = Path.Combine(dst, image + ext);
                if (global::FNF_MonoGame.Engine.FsCI.FileExists(s) && !global::FNF_MonoGame.Engine.FsCI.FileExists(t)) { File.Copy(s, t); n++; }
            }
            if (n > 0) break;
        }
        return n;
    }
}

/// <summary>
/// Psych MenuCharacterEditorState (states/editors/MenuCharacterEditorState.hx): edits images/menucharacters/&lt;id&gt;.json
/// {image, scale, position, idle_anim, confirm_anim, flipX, antialiasing} on the StoryMenuState layout: yellow band
/// (0,56,1280x386), three slots at x = (FlxG.width*0.25)*(1+i) - 150, y 70 (the other two slots alpha 0.2), position =
/// sprite offset (arrow keys: left/up ADD, like Psych), scale + updateHitbox, SPACE = confirm anim on the BF slot.
/// </summary>
public class MenuCharacterEditor : EditorBase
{
    protected override string EdTitle => "MENU CHARACTER";
    protected override Color EdColor => Ed.Yellow;
    private string _id = "dad", _image = "Menu_Dad", _idle = "M Dad Idle", _confirm = "M Dad Idle", _src;
    private float _scale = 1f, _px, _py;
    private bool _flip, _aa = true;
    private int _slot;                       // 0 opponent, 1 boyfriend, 2 girlfriend (Psych characterTypeRadio)
    private static readonly string[] Slots = { "Opponent", "Boyfriend", "Girlfriend" };
    private static readonly string[] Defaults = { "dad", "bf", "gf" };
    private FlxLiteSprite _spr;
    private readonly FlxLiteSprite[] _others = new FlxLiteSprite[3];
    private List<(string label, string path)> _files = new();
    private int _fileSel = -1; private float _fileScroll;

    public override void Load()
    {
        _files = EdJsonFiles.Scan("menucharacters");
        string dev = Environment.GetEnvironmentVariable("FNF_EDITOR_MENUCHAR") ?? "] psych/dad";
        var f = _files.FirstOrDefault(x => x.label.EndsWith(dev, StringComparison.OrdinalIgnoreCase));
        if (f.path != null) LoadFile(f.path); else Rebuild();
        for (int i = 0; i < 3; i++) _others[i] = MakeSlot(Defaults[i]);
    }

    /// <summary>MenuCharacter.changeCharacter for the faded default slots.</summary>
    private FlxLiteSprite MakeSlot(string id)
    {
        try
        {
            var p = _files.FirstOrDefault(x => x.label.EndsWith("psych/" + id)).path;
            if (p == null) return null;
            var o = JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(p));
            var s = new FlxLiteSprite();
            s.SetFrames(EdJsonFiles.Sheet(Game, p, "menucharacters", o.Value<string>("image") ?? ""));
            s.AddByPrefix("idle", o.Value<string>("idle_anim") ?? "", 24);
            s.Play("idle");
            float sc = o.Value<float?>("scale") ?? 1f;
            if (sc != 1f) { s.Scale = new Vector2(sc); s.UpdateHitbox(); }
            s.Offset = new Vector2(o["position"]?[0]?.Value<float>() ?? 0f, o["position"]?[1]?.Value<float>() ?? 0f);
            s.FlipX = o.Value<bool?>("flipX") ?? false;
            s.Alpha = 0.2f;
            return s;
        }
        catch { return null; }
    }

    private void LoadFile(string path)
    {
        if (string.IsNullOrEmpty(EdProject.ModName)) EdProject.GuessModFrom(path);
        try
        {
            var o = JObject.Parse(EdSong.SanitizeJson(global::FNF_MonoGame.Engine.FsCI.ReadAllText(path)));
            _id = Path.GetFileNameWithoutExtension(path); _src = path;
            _image = o.Value<string>("image") ?? ""; _idle = o.Value<string>("idle_anim") ?? ""; _confirm = o.Value<string>("confirm_anim") ?? _idle;
            _scale = o.Value<float?>("scale") ?? 1f; _px = o["position"]?[0]?.Value<float>() ?? 0f; _py = o["position"]?[1]?.Value<float>() ?? 0f;
            _flip = o.Value<bool?>("flipX") ?? false; _aa = o.Value<bool?>("antialiasing") ?? true;
            _slot = _id == "bf" ? 1 : _id == "gf" ? 2 : _slot;
            Rebuild();
            Ed.Toast($"Loaded menu character '{_id}'", Ed.Green);
        }
        catch (Exception ex) { Ed.Toast("Load failed: " + ex.Message, Ed.Red); }
    }

    /// <summary>reloadSelectedCharacter: frames, idle (+confirm on the BF slot), flipX, scale + updateHitbox, offset = position.</summary>
    private void Rebuild()
    {
        _spr = new FlxLiteSprite();
        var sh = EdJsonFiles.Sheet(Game, _src, "menucharacters", _image);
        if (sh == null) { Ed.Toast($"No sheet images/menucharacters/{_image}.png/.xml", Ed.Red); _spr = null; return; }
        _spr.SetFrames(sh);
        _spr.AddByPrefix("idle", _idle, 24);
        if (_slot == 1) _spr.AddByPrefix("confirm", _confirm, 24, false);
        _spr.FlipX = _flip;
        _spr.Scale = new Vector2(_scale); _spr.UpdateHitbox();
        _spr.Play("idle");
        UpdateOffset();
    }
    private void UpdateOffset() { if (_spr != null) _spr.Offset = new Vector2(_px, _py); }

    protected override void EdUpdate(float dt)
    {
        _spr?.Update(dt);
        foreach (var o in _others) o?.Update(dt);
        // Psych: LEFT/UP add, RIGHT/DOWN subtract (it is an offset), SHIFT x10
        float m = Ed.Shift ? 10f : 1f;
        if (Ed.Key(Keys.Left)) _px += m; if (Ed.Key(Keys.Right)) _px -= m;
        if (Ed.Key(Keys.Up)) _py += m; if (Ed.Key(Keys.Down)) _py -= m;
        if (Ed.PadWorld) { var d = Ed.PadDir; _px -= d.X; _py -= d.Y; }
        UpdateOffset();
        if ((Ed.Key(Keys.Space) || Ed.Pad(Buttons.X)) && _slot == 1) _spr?.Play("confirm", true);
        if (_spr?.CurAnim != null && _spr.CurAnim.Name == "confirm" && _spr.CurAnim.Finished) _spr.Play("idle", true);
        if (Ed.Pad(Buttons.LeftShoulder)) { _slot = (_slot + 2) % 3; Rebuild(); }
        if (Ed.Pad(Buttons.RightShoulder)) { _slot = (_slot + 1) % 3; Rebuild(); }
        if (Ed.PadWorld && (Ed.LT || Ed.RT)) { _scale = MathF.Round(Math.Max(0.05f, _scale + (Ed.RT ? 0.05f : -0.05f)) * 100f) / 100f; Rebuild(); }
        if (Ed.PadWorld && Ed.Pad(Buttons.A)) { _flip = !_flip; Rebuild(); }
        if (Ed.Key(Keys.F1)) SavePsych();
    }
    protected override void PadSave() => SavePsych();
    protected override List<(Buttons, string)> PadWorldHints() => new()
    {
        (ButtonPrompts.DPadAll, "position"), (Buttons.LeftShoulder, "slot-"), (Buttons.RightShoulder, "slot+"), (Buttons.A, "flip"),
        (Buttons.LeftTrigger, "scale-"), (Buttons.RightTrigger, "scale+"), (Buttons.X, "confirm anim"),
    };

    protected override void DrawWorld(SpriteBatch sb)
    {
        FunkUI.Begin(sb);
        FunkUI.Fill(0, 0, 1280, 720, Color.Black);
        FunkUI.Fill(0, 56, 1280, 386, new Color(0xF9, 0xCF, 0x51));   // StoryMenuState bgYellow
        // Psych txtOffsets + tipText, moved to a taped note beside the panel (Psych's own spots sit under the title/panel)
        FunkUI.Push(1080f, 560f, FunkUI.Rad(2f));
        FunkUI.ShadowBlock(905f, 462f, 350f, 196f, Ed.Yellow, 4f, 6f, 7f);
        FunkUI.Text("OFFSET", 1080f, 474f, 16f, Ed.Ink, 0.5f);
        FunkUI.Text($"[{Ed.F(_px)}, {Ed.F(_py)}]", 1080f, 494f, 32f, Ed.Ink, 0.5f);
        FunkUI.Text("ARROWS / D-pad (tools): offset", 1080f, 546f, 14f, Ed.Ink, 0.5f);
        FunkUI.Text("SHIFT = 10x speed", 1080f, 566f, 14f, Ed.Ink, 0.5f);
        FunkUI.Text("SPACE / X: \"Start Press\" anim", 1080f, 594f, 14f, Ed.Ink, 0.5f);
        FunkUI.Text("(Boyfriend slot only)", 1080f, 614f, 14f, Ed.Ink, 0.5f);
        FunkUI.Pop();
        FunkUI.Tape(1230f, 462f, 60f, FunkUI.Rad(30f), Ed.Pink, 0.85f);
        sb.End();
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, _aa ? SamplerState.LinearClamp : SamplerState.PointClamp);
        for (int i = 0; i < 3; i++)
        {
            var s = i == _slot ? _spr : _others[i];
            if (s == null) continue;
            s.X = 1280f * 0.25f * (1 + i) - 150f; s.Y = 70f;
            s.Draw(sb);
        }
        sb.End();
    }

    protected override void DrawUI()
    {
        float x = 900f, y = 450f, w = 360f, h = 236f;
        Ed.Panel(20f, 450f, 860f, 236f, Ed.Paper, "MENU CHARACTER (images/menucharacters/<id>.json)", Ed.Yellow, 7);
        float cx = 34f, cy = 468f, cw = 410f;
        string s = _id; if (Ed.Field("mcid", "Character id", ref s, cx, cy, cw)) _id = s.Trim(); cy += 30;
        s = _image; if (Ed.Field("mcimg", "Image", ref s, cx, cy, cw)) { _image = s.Trim(); Rebuild(); } cy += 30;
        s = _idle; if (Ed.Field("mcidle", "Idle anim", ref s, cx, cy, cw)) { _idle = s; Rebuild(); } cy += 30;
        s = _confirm; if (Ed.Field("mccf", "Confirm anim", ref s, cx, cy, cw)) { _confirm = s; Rebuild(); } cy += 30;
        if (Ed.Num("mcsc", "Scale", ref _scale, 0.05f, cx, cy, cw)) Rebuild(); cy += 32;
        if (Ed.Toggle("Flip X", ref _flip, cx, cy, 120f)) Rebuild();
        Ed.Toggle("Antialiasing", ref _aa, cx + 130f, cy, 150f);
        float rx = 470f, ry = 468f;
        int ns = Ed.Choice("Slot", Slots, _slot, rx, ry, 390f, 70f); if (ns != _slot) { _slot = ns; Rebuild(); } ry += 32;
        if (Ed.Button("SAVE PSYCH JSON", rx, ry, 190f, 30f, Ed.Purple, false, "mods/<mod>/images/menucharacters/<id>.json + its sheet")) SavePsych();
        if (Ed.Button("RELOAD IMAGE", rx + 200f, ry, 190f, 30f, Ed.Cyan)) Rebuild();
        ry += 38;
        int fs = Ed.List("mcfiles", _files.Select(f => f.label).ToList(), _fileSel, rx, ry, 300f, 104f, ref _fileScroll, Ed.Yellow, 21f);
        if (fs != _fileSel) _fileSel = fs;
        if (Ed.Button("LOAD", rx + 306f, ry, 84f, 28f, Ed.Cyan) && _fileSel >= 0) LoadFile(_files[_fileSel].path);
        if (Ed.Button("RESCAN", rx + 306f, ry + 34f, 84f, 28f, Color.White)) _files = EdJsonFiles.Scan("menucharacters");
        Ed.HelpTape("ARROWS offset (SHIFT x10)   SPACE confirm anim (Boyfriend slot)   F1 save Psych images/menucharacters/<id>.json");
    }

    private void SavePsych()
    {
        try
        {
            var j = new JObject
            {
                ["image"] = _image, ["scale"] = Math.Round(_scale, 3), ["position"] = new JArray(Math.Round(_px, 3), Math.Round(_py, 3)),
                ["idle_anim"] = _idle, ["confirm_anim"] = _confirm, ["flipX"] = _flip, ["antialiasing"] = _aa,
            };
            string root = NeedRoot();
            string p = Path.Combine(root, "images", "menucharacters", _id + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            File.WriteAllText(p, j.ToString(Formatting.Indented));
            int n = EdJsonFiles.CopyInto(root, _src, "menucharacters", _image);
            Ed.Toast("Saved menu character: " + EdProject.Rel(p) + (n > 0 ? $" (+{n} sheet files)" : ""), Ed.Green);
            _files = EdJsonFiles.Scan("menucharacters");
        }
        catch (Exception ex) { Ed.Toast("Save failed: " + ex.Message, Ed.Red); }
    }
}

/// <summary>
/// Psych DialogueCharacterEditorState: edits images/dialogue/&lt;id&gt;.json {image, dialogue_pos, no_antialiasing, position,
/// scale, animations[{anim, loop_name, loop_offsets, idle_name, idle_offsets}]}. Preview = DialogueBoxPsych placement
/// (setGraphicSize(width * 0.7 * scale), left -60 / right FlxG.width - width - 100 / centre, y 60 + position) with the
/// speech bubble and a typed line; ANIMATIONS tab shows loop (red) and idle (blue) ghosts moved with WASD / arrows.
/// </summary>
public class DialogueCharacterEditor : EditorBase
{
    protected override string EdTitle => "DIALOGUE PORTRAIT";
    protected override Color EdColor => Ed.Orange;
    private sealed class Anim { public string Name = "talk", Loop = "", Idle = ""; public int[] LoopOff = { 0, 0 }, IdleOff = { 0, 0 }; }
    private string _id = "bf", _image = "BF_Dialogue", _pos = "right", _src;
    private bool _noAA;
    private float _scale = 1f, _px, _py;
    private List<Anim> _anims = new();
    private int _cur, _tab, _ghosts;
    private bool _idleShown;
    private float _scroll, _fileScroll; private int _fileSel = -1;
    private SpriteSheet _sheet;
    private FlxLiteSprite _box;
    private DialogueTypedAlphabet _text;
    private List<(string label, string path)> _files = new();
    private static readonly string[] Tabs = { "CHARACTER", "ANIMATIONS", "FILE" };
    private static readonly string[] PosNames = { "left", "center", "right" };
    private const float PS = 0.66f, PX = 22f, PY = 150f;   // preview screen scale/position

    public override void Load()
    {
        _files = EdJsonFiles.Scan("dialogue");
        _box = new FlxLiteSprite(70, 370);
        _box.SetFrames(PsychCutsceneAssets.Sparrow("speech_bubble"));
        _box.AddByPrefix("normal", "speech bubble normal", 24);
        _box.AddByPrefix("center-normal", "speech bubble middle", 24);
        _box.Scale = new Vector2(0.9f); _box.UpdateHitbox();
        _text = new DialogueTypedAlphabet(DialogueBoxPsych.DEFAULT_TEXT_X, DialogueBoxPsych.DEFAULT_TEXT_Y, "Beep bop skee dah!");
        _text.SetScale(0.7f);
        string dev = Environment.GetEnvironmentVariable("FNF_EDITOR_DLGCHAR") ?? "] psych/bf";
        var f = _files.FirstOrDefault(x => x.label.EndsWith(dev, StringComparison.OrdinalIgnoreCase));
        if (f.path != null) LoadFile(f.path);
    }

    private void LoadFile(string path)
    {
        if (string.IsNullOrEmpty(EdProject.ModName)) EdProject.GuessModFrom(path);
        try
        {
            var o = JObject.Parse(EdSong.SanitizeJson(global::FNF_MonoGame.Engine.FsCI.ReadAllText(path)));
            _id = Path.GetFileNameWithoutExtension(path); _src = path;
            _image = o.Value<string>("image") ?? ""; _pos = o.Value<string>("dialogue_pos") ?? "left"; _noAA = o.Value<bool?>("no_antialiasing") ?? false;
            _scale = o.Value<float?>("scale") ?? 1f; _px = o["position"]?[0]?.Value<float>() ?? 0f; _py = o["position"]?[1]?.Value<float>() ?? 0f;
            _anims = new List<Anim>();
            if (o["animations"] is JArray a)
                foreach (var e in a.OfType<JObject>())
                    _anims.Add(new Anim
                    {
                        Name = e.Value<string>("anim") ?? "", Loop = e.Value<string>("loop_name") ?? "", Idle = e.Value<string>("idle_name") ?? "",
                        LoopOff = e["loop_offsets"] is JArray lo && lo.Count >= 2 ? new[] { lo[0].Value<int>(), lo[1].Value<int>() } : new[] { 0, 0 },
                        IdleOff = e["idle_offsets"] is JArray io && io.Count >= 2 ? new[] { io[0].Value<int>(), io[1].Value<int>() } : new[] { 0, 0 },
                    });
            _cur = 0;
            ReloadSheet();
            Ed.Toast($"Loaded dialogue portrait '{_id}' ({_anims.Count} anims)", Ed.Green);
        }
        catch (Exception ex) { Ed.Toast("Load failed: " + ex.Message, Ed.Red); }
    }

    private void ReloadSheet() { _sheet = EdJsonFiles.Sheet(Game, _src, "dialogue", _image); Console.WriteLine($"[EDITOR] dialogue portrait sheet {_image}: {_sheet?.Frames.Count ?? -1} frames"); _sprCache.Clear(); _t = 0f; _text.ResetDialogue(); }

    private readonly Dictionary<string, FlxLiteSprite> _sprCache = new();
    private float _t;

    /// <summary>A DialogueCharacter-like sprite playing one anim (loop or idle) with the json placement applied.</summary>
    private FlxLiteSprite Portrait(Anim a, bool idle)
    {
        if (_sheet == null || a == null) return null;
        string key = (idle ? "i:" : "l:") + a.Name + (idle ? a.Idle : a.Loop);
        if (!_sprCache.TryGetValue(key, out var s))
        {
            s = new FlxLiteSprite();
            s.SetFrames(_sheet);
            s.AddByPrefix("a", idle ? a.Idle : a.Loop, 24, idle);
            s.Play("a", true);
            _sprCache[key] = s;
        }
        // DialogueBoxPsych.spawnCharacters: setGraphicSize(width * DEFAULT_SCALE * scale) from the first frame size
        var first = new FlxLiteSprite(); first.SetFrames(_sheet);
        float w = first.Width * DialogueCharacter.DEFAULT_SCALE * _scale;
        s.SetGraphicSize(w); s.Scale = new Vector2(w / Math.Max(1, first.FrameWidth)); s.UpdateHitbox();
        float x = DialogueBoxPsych.LEFT_CHAR_X, y = DialogueBoxPsych.DEFAULT_CHAR_Y;
        if (_pos == "center") x = 1280f / 2f - s.Width / 2f;
        else if (_pos == "right") x = 1280f - s.Width + DialogueBoxPsych.RIGHT_CHAR_X;
        s.X = x + _px; s.Y = y + _py;
        var off = idle ? a.IdleOff : a.LoopOff;
        s.Offset = new Vector2(off[0], off[1]);
        return s;
    }

    private Anim Cur => _cur >= 0 && _cur < _anims.Count ? _anims[_cur] : null;

    protected override void EdUpdate(float dt)
    {
        _t += dt;
        foreach (var s in _sprCache.Values) s.Update(dt);
        _text.Update(dt);
        var a = Cur;
        // loop anim plays while the text types, then the idle anim (DialogueBoxPsych: finished text -> idle)
        _idleShown = _text.FinishedText;
        if (Ed.Key(Keys.Space) || Ed.Pad(Buttons.X)) { _text.ResetDialogue(); foreach (var s in _sprCache.Values) s.Play("a", true); }
        int n = _anims.Count;
        if (n > 0 && _tab == 0 && (Ed.Key(Keys.W) || Ed.Pad(Buttons.LeftShoulder))) { _cur = (_cur + n - 1) % n; _text.ResetDialogue(); }
        if (n > 0 && _tab == 0 && (Ed.Key(Keys.S) || Ed.Pad(Buttons.RightShoulder))) { _cur = (_cur + 1) % n; _text.ResetDialogue(); }
        if (_tab == 1 && n > 0 && Ed.Pad(Buttons.LeftShoulder)) _cur = (_cur + n - 1) % n;
        if (_tab == 1 && n > 0 && Ed.Pad(Buttons.RightShoulder)) _cur = (_cur + 1) % n;
        if (_tab == 1 && a != null)
        {   // Psych: WASD = loop offsets (red), arrows = idle offsets (blue); offsets are subtracted (left adds)
            int m = Ed.Shift ? 10 : 1;
            if (Ed.Key(Keys.A)) a.LoopOff[0] += m; if (Ed.Key(Keys.D)) a.LoopOff[0] -= m;
            if (Ed.Key(Keys.W)) a.LoopOff[1] += m; if (Ed.Key(Keys.S)) a.LoopOff[1] -= m;
            if (Ed.Key(Keys.Left)) a.IdleOff[0] += m; if (Ed.Key(Keys.Right)) a.IdleOff[0] -= m;
            if (Ed.Key(Keys.Up)) a.IdleOff[1] += m; if (Ed.Key(Keys.Down)) a.IdleOff[1] -= m;
            if (Ed.PadWorld)
            {   // pad: D-pad moves the idle ghost, D-pad with LT held moves the loop ghost
                var d = Ed.PadDir;
                var o = Ed.LTHeld ? a.LoopOff : a.IdleOff;
                o[0] -= (int)d.X; o[1] -= (int)d.Y;
            }
            if (Ed.Key(Keys.H) || Ed.PadWorld && Ed.Pad(Buttons.A)) _ghosts = (_ghosts + 1) % 3;
        }
        if (_tab == 0 && Ed.PadWorld)
        {
            var d = Ed.PadDir; _px += d.X; _py += d.Y;
            if (Ed.Pad(Buttons.A)) { _pos = PosNames[(System.Array.IndexOf(PosNames, _pos) + 1) % 3]; }
            if (Ed.LT || Ed.RT) _scale = MathF.Round(Math.Max(0.05f, _scale + (Ed.RT ? 0.05f : -0.05f)) * 100f) / 100f;
        }
        if (Ed.Pad(Buttons.RightStick)) _tab = (_tab + 1) % Tabs.Length;
        if (Ed.Key(Keys.F1)) SavePsych();
    }
    protected override void PadSave() => SavePsych();
    protected override List<(Buttons, string)> PadWorldHints() => _tab == 1
        ? new() { (ButtonPrompts.DPadAll, "idle offset"), (Buttons.LeftTrigger, "+D-pad: loop offset"), (Buttons.A, "ghosts"), (Buttons.LeftShoulder, "anim-"), (Buttons.RightShoulder, "anim+"), (Buttons.RightStick, "tab") }
        : new() { (ButtonPrompts.DPadAll, "position"), (Buttons.A, "left/center/right"), (Buttons.LeftTrigger, "scale-"), (Buttons.RightTrigger, "scale+"), (Buttons.LeftShoulder, "anim-"), (Buttons.RightShoulder, "anim+"), (Buttons.X, "retype"), (Buttons.RightStick, "tab") };

    protected override void DrawWorld(SpriteBatch sb)
    {
        FunkUI.Begin(sb);
        Backdrop(0, 0, 1f, new Color(58, 40, 70), new Color(70, 50, 84));
        // the 1280x720 game screen, shrunk into the space left of the panel (Psych's portraits sit on the far right)
        FunkUI.ShadowBlock(PX - 4f, PY - 4f, 1280f * PS + 8f, 720f * PS + 8f, new Color(24, 16, 30), 4f, 7f, 8f);
        sb.End();
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, _noAA ? SamplerState.PointClamp : SamplerState.LinearClamp,
            null, null, null, Matrix.CreateScale(PS) * Matrix.CreateTranslation(PX, PY, 0f));
        var a = Cur;
        if (_tab == 1 && a != null)
        {   // ghosts: loop (red) + idle (blue)
            var gl = Portrait(a, false); var gi = Portrait(a, true);
            if (gl != null && _ghosts != 1) { gl.Color = new Color(255, 120, 120); gl.Alpha = _ghosts == 2 ? 1f : 0.6f; gl.Draw(sb); }
            if (gi != null && _ghosts != 2) { gi.Color = new Color(120, 160, 255); gi.Alpha = _ghosts == 1 ? 1f : 0.6f; gi.Draw(sb); }
        }
        else if (a != null)
        {
            var p = Portrait(a, _idleShown || string.IsNullOrEmpty(a.Loop) || a.Loop == a.Idle);
            if (p != null) { p.Color = Color.White; p.Alpha = 1f; p.Draw(sb); }
        }
        if (_tab != 1 && _box != null)
        {
            _box.Play(_pos == "center" ? "center-normal" : "normal");
            _box.FlipX = _pos == "left";
            _box.Draw(sb);
            _text.Draw(sb);
        }
        sb.End();
        FunkUI.Begin(sb);
        if (_tab == 1 && a != null)
        {
            FunkUI.Text($"Loop: [{a.LoopOff[0]}, {a.LoopOff[1]}]", 24f, 80f, 24f, new Color(255, 140, 140));
            FunkUI.Text($"Idle: [{a.IdleOff[0]}, {a.IdleOff[1]}]", 24f, 110f, 24f, new Color(140, 180, 255));
        }
        else if (a != null)
            FunkUI.Text($"Animation: {a.Name} ({_cur + 1} / {_anims.Count}) - W/S or LB/RB to scroll", 24f, 100f, 18f, Color.White);
        sb.End();
    }

    protected override void DrawUI()
    {
        float x = 900f, y = 70f, w = 360f, h = 600f;
        Ed.Panel(x, y, w, h, Ed.Paper, "PSYCH DIALOGUE CHARACTER", Ed.Orange, 9);
        for (int i = 0; i < Tabs.Length; i++) if (Ed.Button(Tabs[i], x + 12f + i * 114f, y + 14f, 108f, 26f, Ed.Yellow, _tab == i)) _tab = i;
        float cx = x + 14f, cy = y + 54f, cw = w - 28f;
        var a = Cur;
        if (_tab == 0)
        {
            string s = _id; if (Ed.Field("dcid", "Character id", ref s, cx, cy, cw)) _id = s.Trim(); cy += 30;
            s = _image; if (Ed.Field("dcimg", "Image", ref s, cx, cy, cw)) { _image = s.Trim(); ReloadSheet(); } cy += 30;
            int pi = Math.Max(0, System.Array.IndexOf(PosNames, _pos));
            pi = Ed.Choice("Position", PosNames, pi, cx, cy, cw, 104f); _pos = PosNames[pi]; cy += 30;
            Ed.Num("dcx", "Offset X", ref _px, 1f, cx, cy, cw); cy += 30;
            Ed.Num("dcy", "Offset Y", ref _py, 1f, cx, cy, cw); cy += 30;
            Ed.Num("dcs", "Scale", ref _scale, 0.05f, cx, cy, cw); cy += 32;
            bool aa = !_noAA; if (Ed.Toggle("Antialiasing", ref aa, cx, cy, 160f)) _noAA = !aa; cy += 34;
            if (Ed.Button("RETYPE TEXT", cx, cy, cw, 28f, Ed.Yellow)) { _text.ResetDialogue(); foreach (var sp in _sprCache.Values) sp.Play("a", true); }
        }
        else if (_tab == 1)
        {
            int sel = Ed.List("dcanims", _anims.Select(q => q.Name).ToList(), _cur, cx, cy, cw, 150f, ref _scroll, Ed.Orange);
            if (sel != _cur) _cur = sel;
            cy += 158;
            if (Ed.Button("ADD", cx, cy, 100f, 26f, Ed.Green)) { _anims.Add(new Anim { Name = "anim" + (_anims.Count + 1), Loop = a?.Loop ?? "", Idle = a?.Idle ?? "" }); _cur = _anims.Count - 1; }
            if (Ed.Button("REMOVE", cx + 106f, cy, 100f, 26f, Ed.Red) && a != null) { _anims.Remove(a); _cur = Math.Max(0, _cur - 1); _sprCache.Clear(); }
            cy += 36;
            if (a != null)
            {
                string s = a.Name; if (Ed.Field("dcan", "Name", ref s, cx, cy, cw)) a.Name = s.Trim(); cy += 30;
                s = a.Loop; if (Ed.Field("dcal", "Loop prefix", ref s, cx, cy, cw)) { a.Loop = s; _sprCache.Clear(); } cy += 30;
                s = a.Idle; if (Ed.Field("dcai", "Idle prefix", ref s, cx, cy, cw)) { a.Idle = s; _sprCache.Clear(); } cy += 34;
                Ed.Label("WASD: loop offset (red)", cx, cy, 13f); cy += 18;
                Ed.Label("ARROWS: idle offset (blue)", cx, cy, 13f); cy += 18;
                Ed.Label("H ghosts   SHIFT x10", cx, cy, 13f);
            }
        }
        else
        {
            int fs = Ed.List("dcfiles", _files.Select(f => f.label).ToList(), _fileSel, cx, cy, cw, 300f, ref _fileScroll, Ed.Orange, 22f);
            if (fs != _fileSel) _fileSel = fs;
            cy += 308;
            if (Ed.Button("LOAD", cx, cy, 100f, 28f, Ed.Cyan) && _fileSel >= 0) LoadFile(_files[_fileSel].path);
            if (Ed.Button("RESCAN", cx + 106f, cy, 100f, 28f, Color.White)) _files = EdJsonFiles.Scan("dialogue");
            cy += 40;
            if (Ed.Button("SAVE PSYCH JSON", cx, cy, cw, 30f, Ed.Purple, false, "mods/<mod>/images/dialogue/<id>.json + its sheet")) SavePsych();
            cy += 40;
            Ed.Label("src: " + EdProject.Rel(_src), cx, cy, 12f);
        }
        Ed.HelpTape("CHARACTER: W/S anim  SPACE retype   ANIMATIONS: WASD loop / ARROWS idle offsets, H ghosts   F1 save images/dialogue/<id>.json");
    }

    private void SavePsych()
    {
        try
        {
            var j = new JObject
            {
                ["image"] = _image, ["dialogue_pos"] = _pos, ["no_antialiasing"] = _noAA,
                ["position"] = new JArray(Math.Round(_px, 3), Math.Round(_py, 3)), ["scale"] = Math.Round(_scale, 3),
                ["animations"] = new JArray(_anims.Select(a => new JObject
                {
                    ["anim"] = a.Name, ["loop_name"] = a.Loop, ["loop_offsets"] = new JArray(a.LoopOff[0], a.LoopOff[1]),
                    ["idle_name"] = a.Idle, ["idle_offsets"] = new JArray(a.IdleOff[0], a.IdleOff[1]),
                })),
            };
            string root = NeedRoot();
            string p = Path.Combine(root, "images", "dialogue", _id + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            File.WriteAllText(p, j.ToString(Formatting.Indented));
            int n = EdJsonFiles.CopyInto(root, _src, "dialogue", _image);
            Ed.Toast("Saved dialogue portrait: " + EdProject.Rel(p) + (n > 0 ? $" (+{n} sheet files)" : ""), Ed.Green);
            _files = EdJsonFiles.Scan("dialogue");
        }
        catch (Exception ex) { Ed.Toast("Save failed: " + ex.Message, Ed.Red); }
    }
}
