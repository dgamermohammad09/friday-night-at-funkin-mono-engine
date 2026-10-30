using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json.Linq;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.PsychEngine;

namespace FNF_MonoGame.Scenes;

/// <summary>Psych Engine mod week (weeks/*.json) shown in Story Mode.</summary>
public partial class WeekData
{
    public bool IsPsych { get; set; }
    public string PsychModDir { get; set; }        // absolute mod folder (null = loose mods/ root)
    public string PsychFile { get; set; }          // WeekData.fileName
    public string[] PsychChars { get; set; } = { "", "", "" };
    public string PsychBackground { get; set; }
    public List<string> PsychSongNames { get; set; } = new();
}

/// <summary>
/// Psych 1.0.4 mod weeks in the Story menu: states/StoryMenuState.hx + objects/MenuCharacter.hx + objects/MenuItem.hx +
/// backend/WeekData.hx (reloadWeekFiles(true)) + backend/Difficulty.hx (loadFromWeek). The weeks are appended after the
/// V-Slice levels; while a Psych week is selected the banner, menu characters, tracklist and difficulty selector use
/// Psych's layout and art (mod images first, then Psych's shared images in Content/images/psych).
/// </summary>
public partial class StoryModeScene
{
    private sealed class PsychMenuChar
    {
        public string Name; public SpriteSheet Sheet; public List<SpriteFrame> Idle, Confirm;
        public float Scale = 1f, OffX, OffY, OriginX, OriginY; public bool FlipX, Visible, Confirming, Missing; public float T;
    }
    private readonly PsychMenuChar[] _psychChars = { new(), new(), new() };
    private readonly Dictionary<string, SpriteSheet> _psychSheets = new();
    private readonly Dictionary<string, Texture2D> _psychTex = new();
    private static string _psychLastDifficultyName = "";
    private SpriteSheet _psychUi;
    private int _psychCharsWeek = -1;

    private WeekData CurWeekOrNull => _weeks.Count > 0 && _selectedIndex < _weeks.Count ? _weeks[_selectedIndex] : null;
    private bool PsychWeekSelected => CurWeekOrNull?.IsPsych == true;

    // ---- loading ----------------------------------------------------------------------------------------------
    /// <summary>WeekData.reloadWeekFiles(true): mods/ root then every enabled mod (modsList order); weeks/weekList.txt
    /// entries first, then every *.json in weeks/; the first week with a given file name wins; hideStoryMode skipped;
    /// locked + hiddenUntilUnlocked weeks are not listed (StoryMenuState.create).</summary>
    private void LoadPsychWeeks()
    {
        try
        {
            string root = PsychMods.ModsBasePath;
            if (string.IsNullOrEmpty(root) || !global::FNF_MonoGame.Engine.FsCI.DirectoryExists(root)) return;
            var dirs = new List<(string dir, string mod)> { (root, null) };
            foreach (var m in PsychMods.GetAllMods().Where(m => m.Enabled))
            {
                string d = PsychMods.GetModDirectory(m.Name);
                if (d != null) dirs.Add((d, d));
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var w in _weeks) seen.Add(w.Id);
            foreach (var (dir, mod) in dirs)
            {
                string wd = Path.Combine(dir, "weeks");
                foreach (var path in PsychWeekFiles(wd))
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    if (seen.Contains(name)) continue;
                    var w = ParsePsychWeek(path, name, mod);
                    if (w == null) continue;
                    seen.Add(name);
                    if (w == HiddenWeek) continue;
                    _weeks.Add(w);
                    Console.WriteLine($"[PSYCH-WEEK] story week '{name}' ({w.Name}) songs={string.Join(",", w.Songs)} diffs={string.Join(",", w.Difficulties)} locked={w.Locked}");
                }
            }
        }
        catch (Exception ex) { Console.WriteLine($"[PSYCH-WEEK] load error: {ex.Message}"); }
    }

    private static readonly WeekData HiddenWeek = new();

    /// <summary>WeekData.reloadWeekFiles per directory: weeks/weekList.txt entries first, then every *.json of the folder
    /// (duplicates removed). Used by Story Mode and Freeplay.</summary>
    internal static List<string> PsychWeekFiles(string weeksDir)
    {
        var res = new List<string>();
        if (string.IsNullOrEmpty(weeksDir) || !global::FNF_MonoGame.Engine.FsCI.DirectoryExists(weeksDir)) return res;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string listFile = Path.Combine(weeksDir, "weekList.txt");
        if (global::FNF_MonoGame.Engine.FsCI.FileExists(listFile))
            foreach (var l in global::FNF_MonoGame.Engine.FsCI.ReadAllLines(listFile).Select(l => l.Trim()).Where(l => l.Length > 0))
            {
                string p = Path.Combine(weeksDir, l + ".json");
                if (global::FNF_MonoGame.Engine.FsCI.FileExists(p) && seen.Add(p)) res.Add(p);
            }
        foreach (var f in global::FNF_MonoGame.Engine.FsCI.GetFiles(weeksDir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            if (seen.Add(f)) res.Add(f);
        return res;
    }

    /// <summary>StoryMenuState.weekIsLocked on a parsed week JSON.</summary>
    internal static bool PsychWeekLocked(JObject j)
    {
        bool startUnlocked = j["startUnlocked"]?.Type != JTokenType.Boolean || (bool)j["startUnlocked"];
        string before = j["weekBefore"]?.ToString() ?? "";
        return !startUnlocked && before.Length > 0 && !PsychWeekCompleted(before);
    }

    private WeekData ParsePsychWeek(string path, string fileName, string modDir)
    {
        JObject j;
        try { j = JObject.Parse(PsychMods.SanitizeJson(global::FNF_MonoGame.Engine.FsCI.ReadAllText(path))); }
        catch (Exception ex) { Console.WriteLine($"[PSYCH-WEEK] {path}: {ex.Message}"); return null; }
        if (j["hideStoryMode"]?.Type == JTokenType.Boolean && (bool)j["hideStoryMode"]) return HiddenWeek;
        var w = new WeekData { IsPsych = true, PsychModDir = modDir, PsychFile = fileName, Id = fileName };
        if (j["songs"] is JArray songs)
            foreach (var s in songs)
                if (s is JArray sa && sa.Count > 0 && sa[0] != null)
                {
                    string n = sa[0].ToString();
                    w.PsychSongNames.Add(n);
                    w.Songs.Add(Difficulty.FormatToSongPath(n));
                }
        if (j["weekCharacters"] is JArray wc)
            for (int i = 0; i < 3; i++) w.PsychChars[i] = i < wc.Count ? (wc[i]?.ToString() ?? "") : "";
        w.PsychBackground = j["weekBackground"]?.ToString() ?? "";
        w.Name = (j["storyName"]?.ToString() ?? "").ToUpperInvariant();
        w.BackgroundColor = new Color(0xF9, 0xCF, 0x51);
        // Difficulty.loadFromWeek
        string diffStr = j["difficulties"]?.ToString();
        string[] diffs = Difficulty.DefaultList.ToArray();
        if (!string.IsNullOrEmpty(diffStr))
        {
            var l = diffStr.Trim().Split(',').ToList();
            for (int i = l.Count - 1; i > 0; i--) { l[i] = l[i].Trim(); if (l[i].Length < 1) l.RemoveAt(i); }
            if (l.Count > 0 && l[0].Length > 0) diffs = l.ToArray();
        }
        w.Difficulties = diffs;
        // weekIsLocked: !startUnlocked && weekBefore != '' && !weekCompleted(weekBefore)
        bool startUnlocked = j["startUnlocked"]?.Type != JTokenType.Boolean || (bool)j["startUnlocked"];
        string before = j["weekBefore"]?.ToString() ?? "";
        w.Locked = !startUnlocked && before.Length > 0 && !PsychWeekCompleted(before);
        bool hidden = j["hiddenUntilUnlocked"]?.Type == JTokenType.Boolean && (bool)j["hiddenUntilUnlocked"];
        if (w.Locked && hidden) return HiddenWeek;
        return w;
    }

    /// <summary>StoryMenuState.weekCompleted: set when the last song of a story week ends (PlayState.endSong saves the
    /// week score in the port), so any saved week score counts as completed.</summary>
    internal static bool PsychWeekCompleted(string weekFile)
    {
        string p = weekFile.ToLowerInvariant() + "_";
        try { return HighscoreManager.Data.WeekScores.Keys.Any(k => k.StartsWith(p, StringComparison.Ordinal)); } catch { return false; }
    }

    /// <summary>Paths.image(key) for the current week directory: mods/&lt;week mod&gt;/images, mods/images, Psych shared.</summary>
    private string PsychImagePath(WeekData w, string key, string ext = ".png")
    {
        foreach (var root in new[] { w?.PsychModDir, PsychMods.ModsBasePath })
        {
            if (string.IsNullOrEmpty(root)) continue;
            string p = Path.Combine(root, "images", key + ext);
            if (global::FNF_MonoGame.Engine.FsCI.FileExists(p)) return p;
        }
        string c = Assets.ResolvePath("images/psych/" + key + ext);
        return c != null && global::FNF_MonoGame.Engine.FsCI.FileExists(c) ? c : null;
    }

    private Texture2D PsychTexture(WeekData w, string key)
    {
        string p = PsychImagePath(w, key);
        if (p == null) return null;
        if (_psychTex.TryGetValue(p, out var t)) return t;
        try { using var s = global::FNF_MonoGame.Engine.FsCI.OpenRead(p); t = Texture2D.FromStream(Game.GraphicsDevice, s); } catch { t = null; }
        _psychTex[p] = t;
        return t;
    }

    private SpriteSheet PsychSheet(WeekData w, string key)
    {
        string p = PsychImagePath(w, key);
        if (p == null) return null;
        if (_psychSheets.TryGetValue(p, out var s)) return s;
        try { s = SpriteSheet.Load(Game, p.Substring(0, p.Length - 4), sparrowOnly: true); } catch { s = null; }
        _psychSheets[p] = s;
        return s;
    }

    private void PsychLoadAssets()
    {
        foreach (var w in _weeks.Where(w => w.IsPsych))
        {
            // MenuItem: loadGraphic(Paths.image('storymenu/' + weekName))
            var t = PsychTexture(w, "storymenu/" + w.PsychFile);
            _weekBanners[w.Id] = t ?? Assets.Pixel;
        }
        _psychUi = SpriteSheet.Load(Game, "images/psych/campaign_menu_UI_assets", sparrowOnly: true);
    }

    // ---- difficulty list ---------------------------------------------------------------------------------------
    private string[] DiffList => PsychWeekSelected ? CurWeekOrNull.Difficulties : DIFFICULTY_LIST;

    /// <summary>StoryMenuState.changeWeek difficulty part: Difficulty.loadFromWeek, curDifficulty = default ('Normal')
    /// if listed else 0, then the remembered lastDifficultyName if listed.</summary>
    private void PsychOnWeekChanged(WeekData prev)
    {
        var cur = CurWeekOrNull;
        if (cur == null) return;
        if (!cur.IsPsych && prev?.IsPsych != true) return;
        var list = DiffList;
        string last = prev?.IsPsych == true && _difficultyIndex < prev.Difficulties.Length ? prev.Difficulties[_difficultyIndex]
                    : !string.IsNullOrEmpty(_psychLastDifficultyName) ? _psychLastDifficultyName : CurDiff;
        int idx = System.Array.FindIndex(list, d => d.Equals(Difficulty.GetDefault(), StringComparison.OrdinalIgnoreCase));
        if (idx < 0) idx = 0;
        int np = System.Array.FindIndex(list, d => d.Equals(last, StringComparison.OrdinalIgnoreCase));
        if (np > -1) idx = np;
        _difficultyIndex = Math.Clamp(idx, 0, list.Length - 1);
        _diffShown = CurDiff;
    }

    private PlayStateParams PsychStoryParams(WeekData w)
    {
        if (!w.IsPsych) return null;
        string diff = CurDiff;
        _psychLastDifficultyName = diff;
        var p = PlayStateParams.ForStoryMode(w.Songs[0], Difficulty.FormatToSongPath(diff), w.Songs, 0, w.Id, 0);
        p.ModPath = w.PsychModDir;
        return p;
    }

    // ---- menu characters (objects/MenuCharacter.hx) --------------------------------------------------------------
    private void PsychSetupChars(WeekData w)
    {
        for (int i = 0; i < 3; i++)
        {
            var c = _psychChars[i];
            string name = w.PsychChars[i] ?? "";
            if (c.Name == name && c.Sheet != null) continue;   // changeCharacter: same character -> keep anim state
            c.Name = name; c.Visible = name.Length > 0; c.Sheet = null; c.Idle = c.Confirm = null; c.T = 0; c.Confirming = false; c.Missing = false;
            if (!c.Visible) continue;
            string jp = PsychImagePath(w, "menucharacters/" + name, ".json");
            if (jp == null) { c.Visible = false; c.Missing = true; continue; }   // falls back to a character JSON: no menu atlas -> nothing drawn
            try
            {
                var j = JObject.Parse(PsychMods.SanitizeJson(global::FNF_MonoGame.Engine.FsCI.ReadAllText(jp)));
                c.Sheet = PsychSheet(w, "menucharacters/" + (string)j["image"]);
                if (c.Sheet == null) { c.Visible = false; continue; }
                string idle = (string)j["idle_anim"], conf = (string)j["confirm_anim"];
                c.Idle = c.Sheet.BuildAnimationFromPrefix(idle);
                c.Confirm = !string.IsNullOrEmpty(conf) && conf != idle ? c.Sheet.BuildAnimationFromPrefix(conf) : null;
                c.FlipX = j["flipX"]?.Type == JTokenType.Boolean && (bool)j["flipX"];
                c.Scale = j["scale"] != null ? (float)j["scale"] : 1f;
                var pos = j["position"] as JArray;
                c.OffX = pos != null && pos.Count > 0 ? (float)pos[0] : 0; c.OffY = pos != null && pos.Count > 1 ? (float)pos[1] : 0;
                // FlxSprite.frames setter: frame = frames[0] -> origin = frameSize / 2
                var f0 = c.Sheet.Frames.Values.FirstOrDefault();
                float fw = f0 == null ? 0 : (f0.FrameWidth > 0 ? f0.FrameWidth : (f0.Rotated ? f0.SourceRect.Height : f0.SourceRect.Width));
                float fh = f0 == null ? 0 : (f0.FrameHeight > 0 ? f0.FrameHeight : (f0.Rotated ? f0.SourceRect.Width : f0.SourceRect.Height));
                c.OriginX = fw / 2f; c.OriginY = fh / 2f;
            }
            catch (Exception ex) { Console.WriteLine($"[PSYCH-WEEK] menu character '{name}': {ex.Message}"); c.Visible = false; }
        }
    }

    private void PsychUpdate(float dt)
    {
        var w = CurWeekOrNull;
        if (w == null || !w.IsPsych) { _psychCharsWeek = -1; return; }
        if (_psychCharsWeek != _selectedIndex) { PsychSetupChars(w); _psychCharsWeek = _selectedIndex; }
        foreach (var c in _psychChars) c.T += dt;
    }

    private void PsychConfirm()
    {
        if (!PsychWeekSelected) return;
        foreach (var c in _psychChars)
            if (c.Visible && c.Confirm != null && c.Confirm.Count > 0) { c.Confirming = true; c.T = 0; }
    }

    private void PsychDrawChars(SpriteBatch sb)
    {
        for (int i = 0; i < 3; i++)
        {
            var c = _psychChars[i];
            if (!c.Visible || c.Sheet == null) continue;
            var frames = c.Confirming ? c.Confirm : c.Idle;
            if (frames == null || frames.Count == 0) continue;
            int fi = (int)(c.T * 24f);
            fi = c.Confirming ? Math.Min(fi, frames.Count - 1) : fi % frames.Count;   // idle loops, confirm plays once
            var fr = frames[fi];
            // new MenuCharacter((FlxG.width * 0.25) * (1 + char) - 150) ; y += 70 ; offset = position
            float x = FNFGame.SCREEN_WIDTH * 0.25f * (1 + i) - 150f, y = 70f;
            float s = c.Scale;
            float fw = fr.FrameWidth > 0 ? fr.FrameWidth : (fr.Rotated ? fr.SourceRect.Height : fr.SourceRect.Width);
            float offX = fr.Offset.X;
            float srcW = fr.Rotated ? fr.SourceRect.Height : fr.SourceRect.Width;
            if (c.FlipX) offX = fw - srcW - offX;
            float bx = x - c.OffX + c.OriginX * (1f - s), by = y - c.OffY + c.OriginY * (1f - s);
            var tex = fr.Texture ?? c.Sheet.Texture;
            if (fr.Rotated)
                sb.Draw(tex, new Vector2(bx + offX * s, by + fr.Offset.Y * s), fr.SourceRect, Color.White, -MathF.PI / 2f,
                    new Vector2(fr.SourceRect.Width, 0), s, c.FlipX ? SpriteEffects.FlipVertically : SpriteEffects.None, 0f);
            else
                sb.Draw(tex, new Vector2(bx + offX * s, by + fr.Offset.Y * s), fr.SourceRect, Color.White, 0f, Vector2.Zero, s,
                    c.FlipX ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
        }
    }

    /// <summary>bgYellow (0,56, FlxG.width x 386, 0xFFF9CF51) + bgSprite (0,56) 'menubackgrounds/menu_' + weekBackground,
    /// blackBarThingie (0,0, w x 56) above the week titles.</summary>
    private void PsychDrawBanner(SpriteBatch sb, WeekData w)
    {
        sb.Draw(Assets.Pixel, new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, BANNER_TOP), Color.Black);
        sb.Draw(Assets.Pixel, new Rectangle(0, BANNER_TOP, FNFGame.SCREEN_WIDTH, 386), new Color(0xF9, 0xCF, 0x51));
        if (!string.IsNullOrEmpty(w.PsychBackground))
        {
            var bg = PsychTexture(w, "menubackgrounds/menu_" + w.PsychBackground);
            if (bg != null) sb.Draw(bg, new Vector2(0, BANNER_TOP), Color.White);
        }
    }

    /// <summary>tracksSprite (FlxG.width*0.07 + 100 - w/2, 56 + 425) + txtTracklist (VCR 32, CENTER, 0xFFE55777,
    /// screenCenter(X) - FlxG.width*0.35, tracksSprite.y + 60); difficulty arrows (850, 462)/(1226, 462) from
    /// campaign_menu_UI_assets and sprDifficulty 'menudifficulties/&lt;diff&gt;' x = 910 + (308 - w)/3,
    /// y tweened from 462 - h + 50 by +30 with alpha 0 -> 1 in 0.07 s.</summary>
    private void PsychDrawLower(SpriteBatch sb, WeekData w)
    {
        var tracks = Assets.LoadTexture("images/psych/Menu_Tracks.png");
        float ty = BANNER_TOP + 425;
        if (tracks != null && tracks != Assets.Pixel)
            sb.Draw(tracks, new Vector2((float)Math.Floor(FNFGame.SCREEN_WIDTH * 0.07f + 100 - tracks.Width / 2f), ty), Color.White);
        var lines = w.PsychSongNames.Select(n => n.ToUpperInvariant()).ToList();
        lines.Add("");   // text += name + '\n' for every song
        float maxW = 0; foreach (var l in lines) maxW = Math.Max(maxW, MenuUI.FlxTextWidth(32, l) - 4f);
        float x0 = (FNFGame.SCREEN_WIDTH - (maxW + 4f)) / 2f - FNFGame.SCREEN_WIDTH * 0.35f;
        float y = ty + 60;
        var col = new Color(0xE5, 0x57, 0x77);
        foreach (var l in lines)
        {
            if (l.Length > 0) MenuUI.FlxText(sb, 32, l, x0 + (float)Math.Floor((maxW - (MenuUI.FlxTextWidth(32, l) - 4f)) / 2f), y, col);
            y += MenuUI.FlxTextLineHeight(32);
        }
        if (w.Locked) return;   // difficultySelectors.visible = unlocked
        const float lx = 850, ly = BANNER_TOP + 396 + 10;
        if (_psychUi != null)
        {
            var la = _psychUi.BuildAnimationFromPrefix(Input.LeftHeld && !_confirmed ? "arrow push left" : "arrow left");
            var ra = _psychUi.BuildAnimationFromPrefix(Input.RightHeld && !_confirmed ? "arrow push right" : "arrow right");
            if (la?.Count > 0) sb.Draw(la[0].Texture ?? _psychUi.Texture, new Vector2(lx + la[0].Offset.X, ly + la[0].Offset.Y), la[0].SourceRect, Color.White);
            if (ra?.Count > 0) sb.Draw(ra[0].Texture ?? _psychUi.Texture, new Vector2(lx + 376 + ra[0].Offset.X, ly + ra[0].Offset.Y), ra[0].SourceRect, Color.White);
        }
        var dt = PsychTexture(w, "menudifficulties/" + Difficulty.FormatToSongPath(_diffShown ?? CurDiff));
        if (dt == null) return;
        float dx = lx + 60 + (308 - dt.Width) / 3f;
        float t = Math.Min(1f, _diffTweenT / 0.07f);
        float dy = ly - dt.Height + 50 + 30 * t;
        sb.Draw(dt, new Vector2(dx, dy), new Color(Color.White, t));
    }
}
