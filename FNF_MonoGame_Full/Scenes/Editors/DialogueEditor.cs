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
/// Dialogue editor = Psych DialogueEditorState (lines: portrait, expression, text, box state, speed, sound) with a live
/// DialogueBoxPsych-style preview (speech_bubble art, portrait from images/dialogue/&lt;id&gt;.json, typed text).
/// Saves Psych data/&lt;song&gt;/dialogue.json and a V-Slice ConversationData (data/dialogue/conversations/&lt;id&gt;.json).
/// </summary>
public class DialogueEditor : EditorBase
{
    protected override string EdTitle => "DIALOGUE";
    protected override Color EdColor => Ed.Orange;
    private class Line { public string Portrait = "bf", Expression = "talk", Text = "Beep bop!", Box = "normal", Sound = ""; public float Speed = 0.05f; }
    private List<Line> _lines = new() { new Line(), new Line { Portrait = "gf", Text = "Show them what you got!" } };
    private int _sel; private float _typeT, _scroll;
    private string _song = "test"; private bool _dbg;
    /// <summary>Auto-start mode of data/&lt;song&gt;/dialogue_autostart.lua: 0 off, 1 story mode only (Psych example), 2 always.</summary>
    private int _auto = 1; private string _music = "breakfast";
    private SpriteSheet _bubble; private AnimatedSprite _bubbleSpr;
    private readonly Dictionary<string, (JObject data, SpriteSheet sheet, AnimatedSprite spr)> _ports = new();

    public override void Load()
    {
        _bubble = SpriteSheet.Load(Game, "images/psych/speech_bubble");
        _bubbleSpr = new AnimatedSprite { Sheet = _bubble };
        foreach (var a in new[] { "speech bubble normal", "AHH speech bubble" }) _bubble?.BuildAnimationFromPrefix(a);
        string dev = Environment.GetEnvironmentVariable("FNF_EDITOR_DIALOGUE");
        if (!string.IsNullOrEmpty(dev) && global::FNF_MonoGame.Engine.FsCI.FileExists(dev)) LoadFile(dev);
    }

    private (JObject data, SpriteSheet sheet, AnimatedSprite spr) Portrait(string id)
    {
        if (_ports.TryGetValue(id ?? "", out var v)) return v;
        v = default;
        try
        {
            var c = new List<string>();
            if (!string.IsNullOrEmpty(EdProject.ModName)) c.Add(Path.Combine(EdProject.ModsBase, EdProject.ModName, "images", "dialogue", id + ".json"));
            c.Add(Path.Combine(AssetManager.ContentRoot, "images", "psych", "dialogue", id + ".json"));
            string j = c.FirstOrDefault(File.Exists);
            if (j != null)
            {
                var o = JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(j));
                var sh = SpriteSheet.Load(Game, Path.Combine(Path.GetDirectoryName(j), o.Value<string>("image") ?? id));
                v = (o, sh, new AnimatedSprite { Sheet = sh }); Console.WriteLine($"[EDITOR] portrait {id}: frames {sh?.Frames.Count ?? -1} tex {sh?.Texture?.Width ?? -1}");
            }
        }
        catch (Exception ex) { Console.WriteLine("[EDITOR] portrait " + id + ": " + ex.Message); }
        _ports[id ?? ""] = v;
        return v;
    }

    private void LoadFile(string path)
    {
        try
        {
            var o = JObject.Parse(EdSong.SanitizeJson(global::FNF_MonoGame.Engine.FsCI.ReadAllText(path)));
            _lines.Clear();
            if (o["dialogue"] is JArray d && d.Count > 0 && d[0]["portrait"] != null)
                foreach (var l in d) _lines.Add(new Line { Portrait = l.Value<string>("portrait") ?? "bf", Expression = l.Value<string>("expression") ?? "talk", Text = l.Value<string>("text") ?? "", Box = l.Value<string>("boxState") ?? "normal", Speed = l.Value<float?>("speed") ?? 0.05f, Sound = l.Value<string>("sound") ?? "" });
            else if (o["dialogue"] is JArray vd)   // V-Slice conversation
                foreach (var l in vd) _lines.Add(new Line { Portrait = l.Value<string>("speaker") ?? "bf", Expression = l.Value<string>("speakerAnimation") ?? "talk", Text = string.Join(" ", (l["text"] as JArray)?.Select(t => t.ToString()) ?? new[] { "" }), Box = l.Value<string>("boxAnimation") ?? "normal", Speed = 0.05f / Math.Max(0.1f, l.Value<float?>("speed") ?? 1f) });
            _song = Path.GetFileName(Path.GetDirectoryName(path)); _sel = 0; _typeT = 0;
            if (string.IsNullOrEmpty(EdProject.ModName)) EdProject.GuessModFrom(path);
            ReadAutoStart(Path.GetDirectoryName(path));
            Ed.Toast($"Loaded dialogue ({_lines.Count} lines)", Ed.Green);
        }
        catch (Exception ex) { Ed.Toast("Load failed: " + ex.Message, Ed.Red); }
    }

    protected override void EdUpdate(float dt)
    {
        _typeT += dt;
        _bubbleSpr?.Update(dt);
        if (Ed.Key(Keys.Space) || Ed.Pad(Buttons.X)) _typeT = 0;
        if ((Ed.Key(Keys.W) || Ed.Pad(Buttons.LeftShoulder) || Ed.PadWorld && Ed.PadUp) && _sel > 0) { _sel--; _typeT = 0; }
        if ((Ed.Key(Keys.S) || Ed.Pad(Buttons.RightShoulder) || Ed.PadWorld && Ed.PadDown) && _sel < _lines.Count - 1) { _sel++; _typeT = 0; }
        if (Ed.PadWorld && Ed.Pad(Buttons.A)) { _lines.Insert(_sel + 1, new Line { Portrait = _lines.Count > 0 ? _lines[_sel].Portrait : "bf" }); _sel++; _typeT = 0; }
        if (Ed.PadWorld && (Ed.PadLeft || Ed.PadRight) && _sel < _lines.Count) { var ln = _lines[_sel]; ln.Box = ln.Box == "angry" ? "normal" : "angry"; _typeT = 0; }
        if (Ed.Key(Keys.F1)) SavePsych();
        if (Ed.Key(Keys.F2)) SaveVSlice();
    }

    protected override void DrawWorld(SpriteBatch sb)
    {
        FunkUI.Begin(sb);
        FunkUI.Fill(0, 0, 1280, 720, new Color(0x2B, 0x2B, 0x2B));
        Backdrop(0, 0, 1f, new Color(60, 46, 80), new Color(70, 56, 92));
        sb.End();
        var l = _sel >= 0 && _sel < _lines.Count ? _lines[_sel] : null;
        if (l == null) return;
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp);
        // portrait (DialogueBoxPsych: LEFT_CHAR_X -60, RIGHT_CHAR_X -100, DEFAULT_CHAR_Y 60)
        var p = Portrait(l.Portrait);
        string side = "left";
        if (p.sheet != null)
        {
            side = p.data.Value<string>("dialogue_pos") ?? "left";
            var anim = (p.data["animations"] as JArray)?.FirstOrDefault(a => a.Value<string>("anim") == l.Expression) ?? (p.data["animations"] as JArray)?.FirstOrDefault();
            string pre = anim?.Value<string>("loop_name") ?? anim?.Value<string>("idle_name");
            if (pre != null && p.spr.CurrentAnimation != pre && p.sheet.BuildAnimationFromPrefix(pre) != null) p.spr.PlayAnimation(pre, true, true);
            p.spr.Update(1f / 60f);
            float sc = p.data.Value<float?>("scale") ?? 1f;
            float w = Math.Max(1, p.spr.GetFrameWidth()) * sc;
            float px = p.data["position"]?[0]?.Value<float>() ?? 0, py = p.data["position"]?[1]?.Value<float>() ?? 0;
            float x = side == "right" ? 1280 - w - 100 : side == "center" ? 640 - w / 2 : -60;
            var off = new Vector2(anim?["loop_offsets"]?[0]?.Value<float>() ?? 0, anim?["loop_offsets"]?[1]?.Value<float>() ?? 0);
            p.spr.Position = new Vector2(x + px, 60 + py) - off;
            p.spr.Scale = new Vector2(sc);
            p.spr.Draw(sb);
        }
        // box (x 70, y 370, scale 0.9; flipped when the speaker is on the left)
        if (_bubble != null)
        {
            string ba = l.Box == "angry" ? "AHH speech bubble" : "speech bubble normal";
            if (_bubbleSpr.CurrentAnimation != ba) _bubbleSpr.PlayAnimation(ba, true, true);
            _bubbleSpr.Position = new Vector2(70, 370);
            _bubbleSpr.Scale = new Vector2(0.9f);
            _bubbleSpr.Effects = side == "left" ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            _bubbleSpr.Draw(sb);
        }
        sb.End();
        FunkUI.Begin(sb);
        int chars = (int)(_typeT / Math.Max(0.005f, l.Speed));
        string shown = l.Text.Length <= chars ? l.Text : l.Text.Substring(0, chars);
        FunkUI.Paragraph(shown, 175f, 470f, 760f, 30f, Color.Black, 0f, 1.3f);
        sb.End();
    }

    protected override void DrawUI()
    {
        float x = 900f, y = 70f, w = 360f, h = 600f;
        Ed.Panel(x, y, w, h, Ed.Paper, "PSYCH DIALOGUE", Ed.Orange, 2);
        float cx = x + 14f, cy = y + 16f, cw = w - 28f;
        var names = _lines.Select((q, i) => $"{i + 1}. {q.Portrait}: {q.Text}").ToList();
        int s = Ed.List("lines", names, _sel, cx, cy, cw, 170f, ref _scroll, Ed.Orange);
        if (s != _sel) { _sel = s; _typeT = 0; }
        cy += 178;
        if (Ed.Button("ADD", cx, cy, 80f, 26f, Ed.Green)) { _lines.Insert(_sel + 1, new Line { Portrait = _lines.Count > 0 ? _lines[_sel].Portrait : "bf" }); _sel++; _typeT = 0; }
        if (Ed.Button("REMOVE", cx + 86f, cy, 90f, 26f, Ed.Red) && _lines.Count > 1) { _lines.RemoveAt(_sel); _sel = Math.Max(0, _sel - 1); }
        if (Ed.Button("REPLAY", cx + 182f, cy, cw - 182f, 26f, Ed.Yellow)) _typeT = 0;
        cy += 36;
        var l = _lines[Math.Clamp(_sel, 0, _lines.Count - 1)];
        string t = l.Portrait; if (Ed.Field("dp", "Portrait", ref t, cx, cy, cw)) l.Portrait = t.Trim(); cy += 30;
        t = l.Expression; if (Ed.Field("de", "Expression", ref t, cx, cy, cw)) l.Expression = t.Trim(); cy += 30;
        t = l.Text; if (Ed.Field("dt", "Text", ref t, cx, cy, cw)) { l.Text = t; _typeT = 0; } cy += 30;
        int bi = l.Box == "angry" ? 1 : 0;
        bi = Ed.Choice("Box", new[] { "normal", "angry" }, bi, cx, cy, cw, 104f); l.Box = bi == 1 ? "angry" : "normal"; cy += 30;
        Ed.Num("ds", "Speed", ref l.Speed, 0.01f, cx, cy, cw); cy += 30;
        t = l.Sound; if (Ed.Field("dsnd", "Sound", ref t, cx, cy, cw)) l.Sound = t.Trim(); cy += 34;
        t = _song; if (Ed.Field("dsong", "Song id", ref t, cx, cy, cw)) _song = t.Trim().ToLowerInvariant(); cy += 34;
        _auto = Ed.Choice("Auto-start", new[] { "off", "story", "always" }, _auto, cx, cy, cw, 104f); cy += 30;
        if (_auto > 0) { t = _music; if (Ed.Field("dmus", "Music", ref t, cx, cy, cw)) _music = t.Trim(); }
        cy += 34;
        if (Ed.Button("SAVE PSYCH", cx, cy, (cw - 6f) / 2f, 30f, Ed.Purple, false, "data/<song>/dialogue.json + the auto-start Lua script")) SavePsych();
        if (Ed.Button("SAVE V-SLICE", cx + (cw + 6f) / 2f, cy, (cw - 6f) / 2f, 30f, Ed.Orange)) SaveVSlice();
        cy += 38;
        if (Ed.Button("LOAD data/<song>/dialogue.json", cx, cy, cw, 26f, Ed.Cyan))
        {
            string p = Path.Combine(NeedRoot(), "data", _song, "dialogue.json");
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(p)) LoadFile(p); else Ed.Toast("No " + EdProject.Rel(p), Ed.Red);
        }
        Ed.HelpTape("W/S line  SPACE replay typing  F1 save Psych data/<song>/dialogue.json  F2 save V-Slice conversation");
    }

    private void SavePsych()
    {
        try
        {
            var j = new JObject { ["dialogue"] = new JArray(_lines.Select(l => new JObject { ["portrait"] = l.Portrait, ["expression"] = l.Expression, ["text"] = l.Text, ["boxState"] = l.Box, ["speed"] = Math.Round(l.Speed, 3), ["sound"] = l.Sound })) };
            string p = Path.Combine(NeedRoot(), "data", _song, "dialogue.json");
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            File.WriteAllText(p, j.ToString(Formatting.Indented));
            string lua = WriteAutoStart(Path.GetDirectoryName(p));
            Ed.Toast("Saved Psych dialogue: " + EdProject.Rel(p) + (lua != null ? " + " + Path.GetFileName(lua) : ""), Ed.Green);
        }
        catch (Exception ex) { Ed.Toast("Save failed: " + ex.Message, Ed.Red); }
    }

    private const string AutoLua = "dialogue_autostart.lua";
    protected override void PadSave() => SavePsych();
    protected override List<(Buttons, string)> PadWorldHints() => new()
    {
        (FNF_MonoGame.Engine.ButtonPrompts.DPadUpDown, "line"), (FNF_MonoGame.Engine.ButtonPrompts.DPadLeftRight, "box normal/angry"), (Buttons.A, "add line"), (Buttons.X, "replay text"),
    };

    /// <summary>Psych's standard way to play data/&lt;song&gt;/dialogue.json: a song script that blocks the first countdown
    /// (onStartCountdown -> Function_Stop), waits 0.8 s and calls startDialogue (Psych example_mods dialogue script).
    /// "story" keeps the example's `isStoryMode and not seenCutscene` check, "always" also plays it in Freeplay.</summary>
    private string WriteAutoStart(string dir)
    {
        string p = Path.Combine(dir, AutoLua);
        if (_auto == 0) { if (global::FNF_MonoGame.Engine.FsCI.FileExists(p)) File.Delete(p); return null; }
        string cond = _auto == 1 ? "not allowCountdown and isStoryMode and not seenCutscene" : "not allowCountdown";
        string mus = (_music ?? "").Replace("'", "");
        string lua =
            "-- Made by the Dialogue editor (auto-start: " + (_auto == 1 ? "story mode" : "always") + ")\n" +
            "-- Psych Engine: blocks the first countdown and plays data/<song>/dialogue.json\n" +
            "local allowCountdown = false\n" +
            "function onStartCountdown()\n" +
            "\tif " + cond + " then\n" +
            "\t\tsetProperty('inCutscene', true);\n" +
            "\t\trunTimer('startDialogue', 0.8);\n" +
            "\t\tallowCountdown = true;\n" +
            "\t\treturn Function_Stop;\n" +
            "\tend\n" +
            "\treturn Function_Continue;\n" +
            "end\n\n" +
            "function onTimerCompleted(tag, loops, loopsLeft)\n" +
            "\tif tag == 'startDialogue' then\n" +
            "\t\tstartDialogue('dialogue', '" + mus + "');\n" +
            "\tend\n" +
            "end\n";
        File.WriteAllText(p, lua);
        return p;
    }

    private void ReadAutoStart(string dir)
    {
        try
        {
            string p = Path.Combine(dir, AutoLua);
            if (!global::FNF_MonoGame.Engine.FsCI.FileExists(p)) { _auto = 0; return; }
            string t = global::FNF_MonoGame.Engine.FsCI.ReadAllText(p);
            _auto = t.Contains("isStoryMode") ? 1 : 2;
            var m = System.Text.RegularExpressions.Regex.Match(t, @"startDialogue\('dialogue',\s*'([^']*)'");
            if (m.Success) _music = m.Groups[1].Value;
        }
        catch { }
    }

    private void SaveVSlice()
    {
        try
        {
            var j = new JObject
            {
                ["version"] = "1.0.0",
                ["backdrop"] = new JObject { ["type"] = "solid", ["color"] = "#B3DFD8", ["fadeTime"] = 0.83 },
                ["outro"] = new JObject { ["type"] = "fade", ["fadeTime"] = 1.0 },
                ["music"] = new JObject { ["asset"] = "Lunchbox", ["fadeTime"] = 1.0, ["looped"] = true },
                ["dialogue"] = new JArray(_lines.Select(l => new JObject { ["speaker"] = l.Portrait, ["speakerAnimation"] = l.Expression, ["box"] = "speech-default", ["boxAnimation"] = l.Box == "angry" ? "angry" : "enterRight", ["text"] = new JArray(l.Text), ["speed"] = Math.Round(0.05 / Math.Max(0.005, l.Speed), 2) })),
            };
            string p = Path.Combine(NeedRoot(), "data", "dialogue", "conversations", _song + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            File.WriteAllText(p, j.ToString(Formatting.Indented));
            Ed.Toast("Saved V-Slice conversation: " + EdProject.Rel(p), Ed.Green);
        }
        catch (Exception ex) { Ed.Toast("Save failed: " + ex.Message, Ed.Red); }
    }
}

/// <summary>
/// Note splash editor = Psych NoteSplashEditorState: per-direction splash animations (prefix, fps range, offsets,
/// indices) of images/noteSplashes/&lt;skin&gt; with the 4 strums and a looping preview. Saves the Psych JSON.
/// </summary>
public class SplashEditor : EditorBase
{
    protected override string EdTitle => "NOTE SPLASH";
    protected override Color EdColor => Ed.Purple;
    private string _skin = "noteSplashes", _path;
    private JObject _json;
    private SpriteSheet _sheet, _notes;
    private readonly SpriteFrame[] _strum = new SpriteFrame[4];
    private List<string> _keys = new();
    private int _sel; private float _scroll, _t;
    private readonly AnimatedSprite[] _spr = new AnimatedSprite[4];

    public override void Load()
    {
        _notes = SpriteSheet.Load(Game, "game/skins/default/notes");
        string[] dirs = { "left", "down", "up", "right" };
        for (int i = 0; i < 4; i++) _strum[i] = _notes?.BuildAnimationFromPrefix(dirs[i] + " note")?.FirstOrDefault();
        LoadSkin(_skin);
    }

    private void LoadSkin(string skin)
    {
        _skin = skin;
        var c = new List<string>();
        if (!string.IsNullOrEmpty(EdProject.ModName)) c.Add(Path.Combine(EdProject.ModsBase, EdProject.ModName, "images", "noteSplashes", skin));
        c.Add(Path.Combine(AssetManager.ContentRoot, "images", "psych", "noteSplashes", skin));
        string b = c.FirstOrDefault(p => global::FNF_MonoGame.Engine.FsCI.FileExists(p + ".png"));
        if (b == null) { Ed.Toast("Splash skin not found: " + skin, Ed.Red); return; }
        _path = b;
        _sheet = SpriteSheet.Load(Game, b, sparrowOnly: true);
        _json = global::FNF_MonoGame.Engine.FsCI.FileExists(b + ".json") ? JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(b + ".json")) : new JObject { ["animations"] = new JObject(), ["scale"] = 1, ["allowRGB"] = true, ["allowPixel"] = true };
        _keys = (_json["animations"] as JObject)?.Properties().Select(p => p.Name).ToList() ?? new List<string>();
        for (int i = 0; i < 4; i++) _spr[i] = new AnimatedSprite { Sheet = _sheet };
        Ed.Toast($"Loaded splash skin '{skin}' ({_keys.Count} anims, {_sheet?.Frames.Count ?? -1} frames, tex {_sheet?.Texture?.Width ?? -1})", Ed.Green);
    }

    private JObject Anim(int noteData)
    {
        var all = (_json?["animations"] as JObject)?.Properties().Select(p => p.Value as JObject).Where(o => o != null && (o.Value<int?>("noteData") ?? -1) % 4 == noteData).ToList();
        if (all == null || all.Count == 0) return null;
        return all[(int)(_t / 0.6f) % all.Count];
    }

    protected override void EdUpdate(float dt)
    {
        _t += dt;
        for (int i = 0; i < 4; i++)
        {
            var a = Anim(i); if (a == null || _sheet == null) continue;
            string pre = a.Value<string>("prefix");
            if (_spr[i].CurrentAnimation != pre || _spr[i].Finished) { _sheet.BuildAnimationFromPrefix(pre); _spr[i].FrameRate = a["fps"]?[0]?.Value<float>() ?? 24; _spr[i].PlayAnimation(pre, true, false); }
            _spr[i].Update(dt);
        }
        var sel = Sel();
        if (sel != null)
        {
            float m = Ed.Shift ? 10 : 1;
            var off = sel["offsets"] as JArray ?? new JArray(0, 0); sel["offsets"] = off;
            if (Ed.Key(Keys.Left)) off[0] = off[0].Value<float>() + m; if (Ed.Key(Keys.Right)) off[0] = off[0].Value<float>() - m;
            if (Ed.Key(Keys.Up)) off[1] = off[1].Value<float>() + m; if (Ed.Key(Keys.Down)) off[1] = off[1].Value<float>() - m;
        }
        if (Ed.Key(Keys.F1)) Save();
        if (_keys.Count > 0 && (Ed.Pad(Buttons.LeftShoulder))) _sel = (_sel + _keys.Count - 1) % _keys.Count;
        if (_keys.Count > 0 && (Ed.Pad(Buttons.RightShoulder))) _sel = (_sel + 1) % _keys.Count;
        if (Ed.PadWorld && sel != null && Ed.PadDir != Vector2.Zero)
        {   // pad tools: D-pad = offsets of the selected splash anim (Psych arrow keys)
            var off = sel["offsets"] as JArray; var pd = Ed.PadDir;
            off[0] = off[0].Value<float>() - pd.X; off[1] = off[1].Value<float>() - pd.Y;
        }
    }
    protected override void PadSave() => Save();
    protected override List<(Buttons, string)> PadWorldHints() => new()
    {
        (FNF_MonoGame.Engine.ButtonPrompts.DPadAll, "offset"), (Buttons.LeftShoulder, "prev anim"), (Buttons.RightShoulder, "next anim"),
    };
    private JObject Sel() => _sel >= 0 && _sel < _keys.Count ? _json["animations"][_keys[_sel]] as JObject : null;

    protected override void DrawWorld(SpriteBatch sb)
    {
        FunkUI.Begin(sb);
        Backdrop(0, 0, 1f, new Color(44, 30, 70), new Color(56, 40, 86));
        for (int i = 0; i < 4; i++)
        {
            var f = _strum[i]; var tex = f?.Texture ?? _notes?.Texture;
            if (f != null && tex != null) FunkUI.Quad(tex, 200 + i * 112 + 56, 300 + 56, f.SourceRect.Width * 0.7f, f.SourceRect.Height * 0.7f, f.Rotated ? -MathF.PI / 2 : 0f, new Color(Color.White, 0.6f), f.SourceRect);
        }
        sb.End();
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp);
        float scale = _json?.Value<float?>("scale") ?? 1f;
        for (int i = 0; i < 4; i++)
        {
            var a = Anim(i); if (a == null) continue;
            // Psych NoteSplash: x = strum.x + (strum.width - width)/2 ... offsets subtract
            var off = a["offsets"] as JArray;
            _spr[i].Position = new Vector2(200 + i * 112 - 112f * 0.95f, 300 - 112f) - new Vector2(off?[0]?.Value<float>() ?? 0, off?[1]?.Value<float>() ?? 0);   // NoteSplash: strum - swagWidth*(0.95,1) - offsets
            _spr[i].Scale = new Vector2(scale);
            _spr[i].Draw(sb);
        }
        sb.End();
    }

    protected override void DrawUI()
    {
        float x = 900f, y = 70f, w = 360f, h = 600f;
        Ed.Panel(x, y, w, h, Ed.Paper, "PSYCH NOTE SPLASH", Ed.Purple, 4);
        float cx = x + 14f, cy = y + 16f, cw = w - 28f;
        string s = _skin; if (Ed.Field("sk", "Skin", ref s, cx, cy, cw - 70f)) { } if (Ed.Button("LOAD", cx + cw - 64f, cy - 1f, 64f, 28f, Ed.Cyan)) LoadSkin(s.Trim()); _skin = s; cy += 36;
        _sel = Ed.List("sanims", _keys.Select(k => k + "  (note " + (_json["animations"][k]?.Value<int?>("noteData") ?? 0) + ")").ToList(), _sel, cx, cy, cw, 200f, ref _scroll, Ed.Purple);
        cy += 208;
        var a = Sel();
        if (a != null)
        {
            string p = a.Value<string>("prefix") ?? ""; if (Ed.Field("sp", "Prefix", ref p, cx, cy, cw)) a["prefix"] = p; cy += 30;
            float nd = a.Value<float?>("noteData") ?? 0; if (Ed.Num("snd", "Note data", ref nd, 1, cx, cy, cw)) a["noteData"] = (int)nd; cy += 30;
            float f0 = a["fps"]?[0]?.Value<float>() ?? 24, f1 = a["fps"]?[1]?.Value<float>() ?? 24;
            if (Ed.Num("sf0", "FPS min", ref f0, 1, cx, cy, cw)) a["fps"] = new JArray(f0, f1); cy += 30;
            if (Ed.Num("sf1", "FPS max", ref f1, 1, cx, cy, cw)) a["fps"] = new JArray(f0, f1); cy += 30;
            float ox = a["offsets"]?[0]?.Value<float>() ?? 0, oy = a["offsets"]?[1]?.Value<float>() ?? 0;
            if (Ed.Num("sox", "Offset X", ref ox, 1, cx, cy, cw)) a["offsets"] = new JArray(ox, oy); cy += 30;
            if (Ed.Num("soy", "Offset Y", ref oy, 1, cx, cy, cw)) a["offsets"] = new JArray(ox, oy); cy += 34;
        }
        float sc = _json?.Value<float?>("scale") ?? 1f; if (Ed.Num("ssc", "Scale", ref sc, 0.05f, cx, cy, cw)) _json["scale"] = sc; cy += 34;
        if (Ed.Button("SAVE PSYCH SPLASH JSON", cx, cy, cw, 30f, Ed.Purple)) Save();
        Ed.HelpTape("ARROWS offset of the selected anim (SHIFT x10)   F1 save  ->  mods/<mod>/images/noteSplashes/<skin>.json (+ png/xml copy)");
    }

    private void Save()
    {
        try
        {
            string dir = Path.Combine(NeedRoot(), "images", "noteSplashes");
            Directory.CreateDirectory(dir);
            string dst = Path.Combine(dir, _skin);
            if (_path != null && !string.Equals(Path.GetFullPath(_path), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase))
            { File.Copy(_path + ".png", dst + ".png", true); if (global::FNF_MonoGame.Engine.FsCI.FileExists(_path + ".xml")) File.Copy(_path + ".xml", dst + ".xml", true); }
            File.WriteAllText(dst + ".json", _json.ToString(Formatting.Indented));
            Ed.Toast("Saved splash: " + EdProject.Rel(dst + ".json"), Ed.Green);
        }
        catch (Exception ex) { Ed.Toast("Save failed: " + ex.Message, Ed.Red); }
    }
}
