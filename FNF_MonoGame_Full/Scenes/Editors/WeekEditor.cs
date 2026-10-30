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

/// <summary>
/// Merged week editor: Psych WeekEditorState (weeks/&lt;id&gt;.json: songs + icons + colours, menu characters,
/// background, story name, lock flags, difficulties) with a live Story-menu preview (Psych StoryMenuState layout:
/// yellow band, menu characters, TRACKS list) and the Freeplay list; also writes the V-Slice level
/// (data/levels/&lt;id&gt;.json: name, songs, background, props).
/// </summary>
public class WeekEditor : EditorBase
{
    protected override string EdTitle => "WEEK";
    protected override Color EdColor => Ed.Green;

    private class Song { public string Name = "Test", Icon = "dad"; public int R = 146, G = 113, B = 253; }
    private string _id = "week1", _storyName = "Your New Week", _weekName = "Custom Week", _bg = "stage", _before = "tutorial", _diffs = "", _source = "";
    private readonly string[] _chars = { "dad", "bf", "gf" };
    private bool _startUnlocked = true, _hiddenUntil, _hideStory, _hideFree;
    private List<Song> _songs = new() { new Song { Name = "Bopeebo" } };
    private int _songSel, _tab, _fileSel = -1;
    private float _songScroll, _fileScroll;
    private static readonly string[] Tabs = { "WEEK", "SONGS", "FILE" };
    private List<(string label, string path)> _files = new();
    private readonly Dictionary<string, (SpriteSheet sheet, AnimatedSprite spr, JObject data)> _menuChars = new();
    private string _mod;

    public override void Load()
    {
        RefreshFiles();
        var f = _files.FirstOrDefault(x => x.label.EndsWith(Environment.GetEnvironmentVariable("FNF_EDITOR_WEEK") ?? "\u0000"));
        if (f.path != null) LoadWeek(f.path);
    }

    private void RefreshFiles()
    {
        _files.Clear();
        void Scan(string dir, string tag) { if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(dir)) foreach (var j in global::FNF_MonoGame.Engine.FsCI.GetFiles(dir, "*.json").OrderBy(x => x)) _files.Add(($"{tag}{Path.GetFileNameWithoutExtension(j)}", j)); }
        Scan(Path.Combine(AssetManager.ContentRoot, "data", "levels"), "[V] ");
        foreach (var m in EdProject.Mods()) { Scan(Path.Combine(EdProject.ModsBase, m, "weeks"), $"[P] {m}/"); Scan(Path.Combine(EdProject.ModsBase, m, "data", "levels"), $"[V] {m}/"); }
    }

    private void LoadWeek(string path)
    {
        if (string.IsNullOrEmpty(EdProject.ModName)) EdProject.GuessModFrom(path); // opening a mod file targets that mod
        try
        {
            var o = JObject.Parse(EdSong.SanitizeJson(global::FNF_MonoGame.Engine.FsCI.ReadAllText(path)));
            _id = Path.GetFileNameWithoutExtension(path); _source = path;
            try { string mb = Path.GetFullPath(EdProject.ModsBase), fp = Path.GetFullPath(path); _mod = fp.StartsWith(mb, StringComparison.OrdinalIgnoreCase) ? fp.Substring(mb.Length).TrimStart('\\', '/').Split('\\', '/')[0] : null; } catch { _mod = null; }
            _songs.Clear();
            if (o["songs"] is JArray sa && sa.Count > 0 && sa[0] is JArray)
            {   // Psych WeekFile
                foreach (var s in sa)
                {
                    var c = s[2] as JArray;
                    _songs.Add(new Song { Name = s[0].ToString(), Icon = s[1].ToString(), R = c?[0].Value<int>() ?? 146, G = c?[1].Value<int>() ?? 113, B = c?[2].Value<int>() ?? 253 });
                }
                if (o["weekCharacters"] is JArray wc) for (int i = 0; i < 3 && i < wc.Count; i++) _chars[i] = wc[i].ToString();
                _bg = o.Value<string>("weekBackground") ?? ""; _before = o.Value<string>("weekBefore") ?? "";
                _storyName = o.Value<string>("storyName") ?? ""; _weekName = o.Value<string>("weekName") ?? "";
                _startUnlocked = o.Value<bool?>("startUnlocked") ?? true; _hiddenUntil = o.Value<bool?>("hiddenUntilUnlocked") ?? false;
                _hideStory = o.Value<bool?>("hideStoryMode") ?? false; _hideFree = o.Value<bool?>("hideFreeplay") ?? false;
                _diffs = o.Value<string>("difficulties") ?? "";
            }
            else
            {   // V-Slice LevelData
                _storyName = o.Value<string>("name") ?? _id; _weekName = _storyName;
                if (o["songs"] is JArray ids) foreach (var s in ids) _songs.Add(new Song { Name = s.ToString(), Icon = "dad" });
                if (o["props"] is JArray props)
                    for (int i = 0; i < 3 && i < props.Count; i++)
                    {
                        string ap = props[i].Value<string>("assetPath") ?? "";
                        _chars[i] = Path.GetFileName(ap);
                    }
                // V-Slice prop order is dad, bf, gf like Psych weekCharacters
            }
            _menuChars.Clear();
            Ed.Toast($"Loaded week '{_id}' ({_songs.Count} songs)", Ed.Green);
        }
        catch (Exception ex) { Ed.Toast("Load failed: " + ex.Message, Ed.Red); }
    }

    protected override void EdUpdate(float dt)
    {
        foreach (var m in _menuChars.Values) m.spr?.Update(dt);
        if (Ed.Key(Keys.F1)) SavePsych();
        if (Ed.Key(Keys.F2)) SaveVSlice();
        if (Ed.Pad(Buttons.LeftShoulder)) _tab = (_tab + Tabs.Length - 1) % Tabs.Length;
        if (Ed.Pad(Buttons.RightShoulder)) _tab = (_tab + 1) % Tabs.Length;
        if (Ed.PadWorld && _songs.Count > 0)
        {   // pad tools: D-pad up/down picks the song (freeplay preview), left/right moves it in the list
            if (Ed.PadUp) _songSel = (_songSel + _songs.Count - 1) % _songs.Count;
            if (Ed.PadDown) _songSel = (_songSel + 1) % _songs.Count;
            if (Ed.PadLeft && _songSel > 0) { (_songs[_songSel], _songs[_songSel - 1]) = (_songs[_songSel - 1], _songs[_songSel]); _songSel--; }
            if (Ed.PadRight && _songSel < _songs.Count - 1) { (_songs[_songSel], _songs[_songSel + 1]) = (_songs[_songSel + 1], _songs[_songSel]); _songSel++; }
            if (Ed.Pad(Buttons.A)) { _songs.Insert(_songSel + 1, new Song { Name = "New Song" }); _songSel++; }
        }
    }
    protected override void PadSave() => SavePsych();
    protected override List<(Buttons, string)> PadWorldHints() => new()
    {
        (FNF_MonoGame.Engine.ButtonPrompts.DPadUpDown, "song"), (FNF_MonoGame.Engine.ButtonPrompts.DPadLeftRight, "reorder"), (Buttons.A, "add song"), (Buttons.LeftShoulder, "tab-"), (Buttons.RightShoulder, "tab+"),
    };

    /// <summary>Psych menucharacters/&lt;id&gt;.json (image, scale, position, idle_anim, flipX).</summary>
    private (SpriteSheet sheet, AnimatedSprite spr, JObject data) MenuChar(string id)
    {
        if (string.IsNullOrEmpty(id)) return default;
        if (_menuChars.TryGetValue(id, out var v)) return v;
        v = default;
        try
        {
            var cands = new List<string>();
            foreach (var m in new[] { _mod, EdProject.ModName }.Where(x => !string.IsNullOrEmpty(x))) cands.Add(Path.Combine(EdProject.ModsBase, m, "images", "menucharacters", id + ".json"));
            cands.Add(Path.Combine(AssetManager.ContentRoot, "images", "psych", "menucharacters", id + ".json"));
            string j = cands.FirstOrDefault(File.Exists);
            if (j != null)
            {
                var o = JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(j));
                string img = Path.Combine(Path.GetDirectoryName(j), o.Value<string>("image") ?? id);
                var sh = SpriteSheet.Load(Game, img);
                var spr = new AnimatedSprite { Sheet = sh };
                string idle = o.Value<string>("idle_anim") ?? "idle";
                if (sh != null && sh.BuildAnimationFromPrefix(idle) is { Count: > 0 }) { spr.FrameRate = 24; spr.PlayAnimation(idle, true, true); }
                v = (sh, spr, o);
            }
        }
        catch (Exception ex) { Console.WriteLine("[EDITOR] menu char " + id + ": " + ex.Message); }
        _menuChars[id] = v;
        return v;
    }

    protected override void DrawWorld(SpriteBatch sb)
    {
        // Psych StoryMenuState preview: black top, yellow band (y 56, h 386), characters at (FlxG.width*0.25)*(1+i) - 150, y 70
        FunkUI.Begin(sb);
        FunkUI.Fill(0, 0, 1280, 720, Color.Black);
        FunkUI.Fill(0, 56, 1280, 386, new Color(0xF9, 0xCF, 0x51));
        FunkUI.Text(_weekName.ToUpperInvariant(), 1270f, 64f, 32f, new Color(255, 255, 255, 180), 1f);
        FunkUI.Text("WEEK SCORE: 0", 330f, 14f, 30f, Color.White);
        FunkUI.Text(_storyName ?? "", 1270f, 10f, 32f, new Color(255, 255, 255, 180), 1f);
        // tracks list (Psych txtTracklist colour 0xFFe55777, centred at FlxG.width*0.05+? ) - simplified
        FunkUI.Text("TRACKS", 225f, 500f, 32f, new Color(0xE5, 0x57, 0x77), 0.5f);
        float ty = 540f;
        foreach (var s in _songs) { FunkUI.Text(s.Name.ToUpperInvariant(), 225f, ty, 30f, new Color(0xE5, 0x57, 0x77), 0.5f); ty += 34f; }
        sb.End();
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp);
        for (int i = 0; i < 3; i++)
        {
            var mc = MenuChar(_chars[i]);
            if (mc.spr == null || mc.sheet == null) continue;
            var d = mc.data;
            float sc = d?.Value<float?>("scale") ?? 1f;
            float px = d?["position"]?[0]?.Value<float>() ?? 0, py = d?["position"]?[1]?.Value<float>() ?? 0;
            float bx = 1280f * 0.25f * (1 + i) - 150f, by = 70f;
            mc.spr.Position = new Vector2(bx + px, by + py) * 0.62f + new Vector2(-20, 70);
            mc.spr.Scale = new Vector2(sc * 0.9f * 0.62f);
            mc.spr.Effects = (d?.Value<bool?>("flipX") ?? false) ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            mc.spr.Draw(sb);
        }
        sb.End();
    }

    protected override void DrawUI()
    {
        // freeplay preview strip
        Ed.Panel(400f, 470f, 470f, 210f, new Color(_songs.Count > 0 ? new Color(_songs[Math.Clamp(_songSel, 0, _songs.Count - 1)].R, _songs[Math.Clamp(_songSel, 0, _songs.Count - 1)].G, _songs[Math.Clamp(_songSel, 0, _songs.Count - 1)].B) : Ed.Purple, 255), "FREEPLAY PREVIEW", Ed.Green, 3);
        float fy = 486f;
        for (int i = 0; i < Math.Min(5, _songs.Count); i++)
        {
            var s = _songs[i];
            FunkUI.Bold(s.Name, 420f + i * 12f, fy, 0.45f, 0f, Ed.Time, i == _songSel ? 1f : 0.6f);
            FunkUI.HealthIcon(s.Icon, 440f + i * 12f + FunkUI.BoldWidth(s.Name, 0.45f) + 30f, fy + 18f, 54f, 0, 0f, i == _songSel ? 1f : 0.6f);
            fy += 38f;
        }

        float x = 900f, y = 70f, w = 360f, h = 600f;
        Ed.Panel(x, y, w, h, Ed.Paper, "PSYCH WEEK / V-SLICE LEVEL", Ed.Green, 5);
        for (int i = 0; i < Tabs.Length; i++) if (Ed.Button(Tabs[i], x + 12f + i * 114f, y + 14f, 108f, 26f, Ed.Yellow, _tab == i)) _tab = i;
        float cx = x + 14f, cy = y + 54f, cw = w - 28f;
        if (_tab == 0)
        {
            string s = _id; if (Ed.Field("wid", "Week id", ref s, cx, cy, cw)) _id = s.Trim(); cy += 30;
            s = _storyName; if (Ed.Field("wsn", "Story name", ref s, cx, cy, cw)) _storyName = s; cy += 30;
            s = _weekName; if (Ed.Field("wwn", "Week name", ref s, cx, cy, cw)) _weekName = s; cy += 30;
            for (int i = 0; i < 3; i++)
            {
                string c = _chars[i];
                if (Ed.Field("wc" + i, i == 0 ? "Opponent" : i == 1 ? "Boyfriend" : "Girlfriend", ref c, cx, cy, cw)) { _chars[i] = c.Trim(); }
                cy += 30;
            }
            s = _bg; if (Ed.Field("wbg", "Background", ref s, cx, cy, cw)) _bg = s.Trim(); cy += 30;
            s = _before; if (Ed.Field("wbf", "Week before", ref s, cx, cy, cw)) _before = s.Trim(); cy += 30;
            s = _diffs; if (Ed.Field("wdf", "Difficulties", ref s, cx, cy, cw)) _diffs = s; cy += 34;
            Ed.Toggle("Start unlocked", ref _startUnlocked, cx, cy, 160f); Ed.Toggle("Hidden until unl.", ref _hiddenUntil, cx + 165f, cy, 170f); cy += 28;
            Ed.Toggle("Hide story mode", ref _hideStory, cx, cy, 160f); Ed.Toggle("Hide freeplay", ref _hideFree, cx + 165f, cy, 170f); cy += 28;
        }
        else if (_tab == 1)
        {
            var names = _songs.Select(q => q.Name).ToList();
            _songSel = Ed.List("wsongs", names, _songSel, cx, cy, cw, 170f, ref _songScroll, Ed.Green);
            cy += 178;
            if (Ed.Button("ADD", cx, cy, 80f, 26f, Ed.Green)) { _songs.Add(new Song { Name = "New Song" }); _songSel = _songs.Count - 1; }
            if (Ed.Button("REMOVE", cx + 86f, cy, 90f, 26f, Ed.Red) && _songs.Count > 0) { _songs.RemoveAt(_songSel); _songSel = Math.Max(0, _songSel - 1); }
            if (Ed.Button("UP", cx + 182f, cy, 70f, 26f, Color.White) && _songSel > 0) { (_songs[_songSel], _songs[_songSel - 1]) = (_songs[_songSel - 1], _songs[_songSel]); _songSel--; }
            if (Ed.Button("DOWN", cx + 258f, cy, cw - 258f, 26f, Color.White) && _songSel < _songs.Count - 1) { (_songs[_songSel], _songs[_songSel + 1]) = (_songs[_songSel + 1], _songs[_songSel]); _songSel++; }
            cy += 36;
            if (_songSel >= 0 && _songSel < _songs.Count)
            {
                var so = _songs[_songSel];
                string s = so.Name; if (Ed.Field("sn", "Song", ref s, cx, cy, cw)) so.Name = s; cy += 30;
                s = so.Icon; if (Ed.Field("si", "Icon", ref s, cx, cy, cw)) so.Icon = s.Trim(); cy += 30;
                float r = so.R, g = so.G, b = so.B;
                if (Ed.Num("sr", "Red", ref r, 5f, cx, cy, cw)) so.R = (int)Math.Clamp(r, 0, 255); cy += 30;
                if (Ed.Num("sg", "Green", ref g, 5f, cx, cy, cw)) so.G = (int)Math.Clamp(g, 0, 255); cy += 30;
                if (Ed.Num("sb", "Blue", ref b, 5f, cx, cy, cw)) so.B = (int)Math.Clamp(b, 0, 255); cy += 30;
            }
        }
        else
        {
            int s = Ed.List("wfiles", _files.Select(f => f.label).ToList(), _fileSel, cx, cy, cw, 260f, ref _fileScroll, Ed.Green, 22f);
            if (s != _fileSel) _fileSel = s;
            cy += 268;
            if (Ed.Button("LOAD", cx, cy, 100f, 28f, Ed.Cyan) && _fileSel >= 0) LoadWeek(_files[_fileSel].path);
            if (Ed.Button("RESCAN", cx + 106f, cy, 100f, 28f, Color.White)) RefreshFiles();
            cy += 38;
            if (Ed.Button("SAVE PSYCH", cx, cy, (cw - 6f) / 2f, 30f, Ed.Purple)) SavePsych();
            if (Ed.Button("SAVE V-SLICE", cx + (cw + 6f) / 2f, cy, (cw - 6f) / 2f, 30f, Ed.Orange)) SaveVSlice();
            cy += 40;
            Ed.Label("src: " + EdProject.Rel(_source), cx, cy, 12f);
        }
        Ed.HelpTape("Story menu preview (Psych StoryMenuState) + Freeplay list   F1 save Psych weeks/<id>.json   F2 save V-Slice data/levels/<id>.json");
    }

    private void SavePsych()
    {
        try
        {
            var j = new JObject
            {
                ["songs"] = new JArray(_songs.Select(s => new JArray(s.Name, s.Icon, new JArray(s.R, s.G, s.B)))),
                ["weekCharacters"] = new JArray(_chars),
                ["weekBackground"] = _bg, ["weekBefore"] = _before, ["storyName"] = _storyName, ["weekName"] = _weekName,
                ["freeplayColor"] = new JArray(146, 113, 253),
                ["startUnlocked"] = _startUnlocked, ["hiddenUntilUnlocked"] = _hiddenUntil, ["hideStoryMode"] = _hideStory, ["hideFreeplay"] = _hideFree,
                ["difficulties"] = _diffs,
            };
            string p = Path.Combine(NeedRoot(), "weeks", _id + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            File.WriteAllText(p, j.ToString(Formatting.Indented));
            Ed.Toast("Saved Psych week: " + EdProject.Rel(p), Ed.Green); RefreshFiles();
        }
        catch (Exception ex) { Ed.Toast("Save failed: " + ex.Message, Ed.Red); }
    }

    private void SaveVSlice()
    {
        try
        {
            var props = new JArray();
            float[][] offs = { new float[] { 100, 60 }, new float[] { 150, 80 }, new float[] { 0, 0 } };
            for (int i = 0; i < 3; i++)
                props.Add(new JObject { ["assetPath"] = "storymenu/props/" + _chars[i], ["scale"] = 1.0, ["offsets"] = new JArray(offs[i][0], offs[i][1]), ["animations"] = new JArray(new JObject { ["name"] = "idle", ["prefix"] = "idle", ["frameRate"] = 24 }) });
            var j = new JObject
            {
                ["version"] = "1.0.1", ["name"] = _storyName.ToUpperInvariant(), ["titleAsset"] = "storymenu/titles/" + _id,
                ["props"] = props, ["background"] = "#F9CF51", ["songs"] = new JArray(_songs.Select(s => s.Name.Trim().ToLowerInvariant().Replace(' ', '-'))),
                ["visible"] = !_hideStory,
            };
            string p = Path.Combine(NeedRoot(), "data", "levels", _id + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            File.WriteAllText(p, j.ToString(Formatting.Indented));
            Ed.Toast("Saved V-Slice level: " + EdProject.Rel(p), Ed.Green); RefreshFiles();
        }
        catch (Exception ex) { Ed.Toast("Save failed: " + ex.Message, Ed.Red); }
    }
}
