using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// play/components/Subtitles.hx + util/SRTUtil.hx (V-Slice 0.8.1). Preferences.subtitles defaults to true.
/// PlayState.create: new Subtitles(0, 139, downscroll ? TOP : BOTTOM) on camSubtitles (above camCutscene).
/// PlayState.startSong: assignSubtitles('songs/&lt;id&gt;/subtitles/song-lyrics[-&lt;variation&gt;]', FlxG.sound.music).
/// Song scripts assign their own (eggnog-erect santa-emotions, stress-pico end-cutscene-pico).
/// Files: Content/data/subtitles/&lt;song&gt;/&lt;name&gt;.srt (copied from funkin.assets/preload/data/songs/*/subtitles).
/// Look: SubtitlesText = FlxText size 30 'VCR OSD Mono', centred; background = black makeGraphic(ceil(w), ceil(h)), alpha 0.5;
/// group screenCenter(X); y = FlxG.height - height - 139 (bottom) or 139 (top).
/// </summary>
public partial class PlayScene
{
    private sealed class SubEntry { public double Start, End; public string Text; }
    private List<SubEntry> _subData;
    private Func<double> _subTimeMs;       // assignedSound.time (ms)
    private string _subText = "";
    private bool _subVisible;
    private bool _subSongInit;
    private const int SubSize = 30;
    private float _subForceY = -1f;        // script-set offsets[1] with alignment 'bottom' (stress-pico end cutscene)

    private static readonly Regex SubTimeRx = new(@"(\d+):(\d+):(\d+)[,.](\d+)\s*-->\s*(\d+):(\d+):(\d+)[,.](\d+)");

    private static List<SubEntry> SubParse(string s)
    {
        var o = new List<SubEntry>();
        if (s == null) return o;
        s = s.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\uFEFF", "");
        foreach (var block in s.Split(new[] { "\n\n" }, StringSplitOptions.None))
        {
            var b = block.Trim();
            if (b.Length == 0) continue;
            var lines = b.Split('\n');
            int idx = 0;
            if (lines.Length > 0 && Regex.IsMatch(lines[0].Trim(), @"^\d+$")) idx = 1;
            if (idx >= lines.Length) continue;
            var m = SubTimeRx.Match(lines[idx]);
            if (!m.Success) continue;
            double T(int g) => int.Parse(m.Groups[g].Value) * 3600000.0 + int.Parse(m.Groups[g + 1].Value) * 60000.0 + int.Parse(m.Groups[g + 2].Value) * 1000.0 + int.Parse(m.Groups[g + 3].Value);
            var text = string.Join("\n", lines, idx + 1, lines.Length - idx - 1).Replace('{', '<').Replace('}', '>');
            text = Regex.Replace(text, "<[^>]*>", ""); // htmlText: tags are formatting only
            o.Add(new SubEntry { Start = T(1), End = T(5), Text = text });
        }
        return o;
    }

    /// <summary>Subtitles.assignSubtitles(filePath, sound). file = "&lt;song&gt;/&lt;name&gt;".</summary>
    private void SubAssign(string file, Func<double> timeMs)
    {
        _subText = ""; _subVisible = false;
        _subData = null; _subTimeMs = null;
        try
        {
            string p = Assets.ResolvePath($"data/subtitles/{file}.srt") ?? Path.Combine(AppContext.BaseDirectory, "Content", "data", "subtitles", file + ".srt");
            if (!global::FNF_MonoGame.Engine.FsCI.FileExists(p) || timeMs == null) return;
            _subData = SubParse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(p));
            _subTimeMs = timeMs;
            Console.WriteLine($"[SUB] assigned {file} ({_subData.Count} entries)");
        }
        catch (Exception ex) { Console.WriteLine($"[SUB] {file}: {ex.Message}"); }
    }

    /// <summary>PlayState.startSong: song-lyrics[-variation] on the instrumental.</summary>
    private void SubSongInit()
    {
        if (_subSongInit) return;
        _subSongInit = true;
        string s = (_songName ?? "").ToLowerInvariant();
        string id = s, variation = "";
        int us = s.LastIndexOf('_');
        if (us > 0 && (s.EndsWith("_erect") || s.EndsWith("_pico") || s.EndsWith("_bf"))) { id = s.Substring(0, us); variation = s.Substring(us + 1); }
        id = id.Replace('_', '-');
        string file = $"{id}/song-lyrics" + (variation != "" ? "-" + variation : "");
        string p = Assets.ResolvePath($"data/subtitles/{file}.srt") ?? Path.Combine(AppContext.BaseDirectory, "Content", "data", "subtitles", file + ".srt");
        if (global::FNF_MonoGame.Engine.FsCI.FileExists(p)) SubAssign(file, () => _conductor.SongPosition * 1000.0);
    }

    /// <summary>Subtitles.update (camSubtitles object, updated every frame).</summary>
    private void SubUpdate()
    {
        if (!_subSongInit && !_countdownActive && _conductor.SongPosition > 0) SubSongInit();
        if (_subData == null || _subTimeMs == null) return;
        double t = _subTimeMs();
        var cur = new List<string>();
        foreach (var d in _subData)
            if (t >= d.Start && t <= d.End) cur.Insert(0, d.Text); // currentLines.unshift
        if (cur.Count > 0) { _subVisible = true; _subText = string.Join("\n", cur); }
        else { _subVisible = false; _subText = ""; }
    }

    private void SubDraw(SpriteBatch sb)
    {
        if (!_subVisible || string.IsNullOrEmpty(_subText)) return;
        var lines = _subText.Split('\n');
        float adv = MenuUI.FlxTextAdvance(SubSize);
        float lh = MenuUI.FlxTextLineHeight(SubSize);
        int maxLen = 0; foreach (var l in lines) maxLen = Math.Max(maxLen, l.Length);
        float w = maxLen * adv + 4f;                 // FlxText width (2 px gutter each side)
        float h = lines.Length * lh + 4f;            // FlxText height
        float bw = (float)Math.Ceiling(w), bh = (float)Math.Ceiling(h);
        float gw = Math.Max(bw, w), gh = Math.Max(bh, h);
        float x = (float)Math.Round((1280f - gw) / 2f); // screenCenter(X) (+ offsets[0] = 0)
        float y = _subForceY >= 0f ? 720f - gh - _subForceY : _downscroll ? 139f : 720f - gh - 139f;
        sb.End();
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
        sb.Draw(Assets.Pixel, new Rectangle((int)x, (int)y, (int)bw, (int)bh), new Color(Color.Black, 0.5f));
        for (int i = 0; i < lines.Length; i++)
        {
            float lx = x + (w - 4f - lines[i].Length * adv) / 2f; // CENTER alignment inside the autosized field
            MenuUI.FlxText(sb, SubSize, lines[i], lx, y + i * lh, Color.White);
        }
        sb.End();
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
    }
}
