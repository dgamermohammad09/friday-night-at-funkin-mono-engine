using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// FreeplayState.capsuleOnConfirm + CapsuleOptionsMenu.hx (0.8.1, not on mobile): when the selected song/difficulty has
/// alt instrumentals (Song.listAltInstrumentalIds = metadata playData.characters.altInstrumentals, filtered by the song
/// scripts: easy/normal/hard drop 'bf' (darnell, lit-up) and drop 'pico' unless Save.hasBeatenSong(id, null, 'pico'),
/// i.e. a saved score of the Pico mix), an instBox opens at capsule targetPos + (175, 115):
/// UI_LEFT = next, UI_RIGHT = previous, ACCEPT = capsuleOnConfirmDefault(cap, instId), BACK = close (open anim reversed).
/// The chosen id goes to PlayState as targetInstrumental (songs/&lt;song&gt;/Inst-&lt;id&gt;). The Random capsule picks a random id.
/// </summary>
public partial class FreeplayScene
{
    private SpriteSheet _instBoxSheet, _instSelSheet;
    private bool _instMenuOpen, _instMenuClosing, _instMenuBusy;
    private List<string> _instIds;
    private int _instIdx;
    private float _instAnimT, _instSelT;
    private Vector2 _instPos;
    private string _instChosen;
    private bool _instForceConfirm;
    private float _instPushL, _instPushR;
    private const float InstBoxW = 329f;

    private static readonly string[] BaseDiffs = { "easy", "normal", "hard" };

    /// <summary>[baseInstrumentalId] + listAltInstrumentalIds, or null when there are no alt instrumentals.</summary>
    private List<string> ListInstrumentalChoices(FreeplaySong song, string diff)
    {
        try
        {
            if (song == null || song.IsRandom) return null;
            string folder = ResolveSongNameForSelection(song, diff);
            if (string.IsNullOrEmpty(folder)) return null;
            string meta = Path.Combine(AssetManager.ContentRoot, "songs", folder, "charts", "meta.json");
            if (!global::FNF_MonoGame.Engine.FsCI.FileExists(meta)) return null;
            string txt = global::FNF_MonoGame.Engine.FsCI.ReadAllText(meta);
            var alt = Regex.Match(txt, "\"altInstrumentals\"\\s*:\\s*\\[([^\\]]*)\\]");
            if (!alt.Success) return null;
            var alts = new List<string>();
            foreach (Match m in Regex.Matches(alt.Groups[1].Value, "\"([^\"]*)\"")) alts.Add(m.Groups[1].Value);
            string d = (diff ?? "normal").ToLowerInvariant();
            if (System.Array.IndexOf(BaseDiffs, d) >= 0)
            {
                alts.Remove("bf"); // darnell.hxc / lit-up.hxc: Pico has no access to the Boyfriend Mix instrumentals
                if (alts.Contains("pico") && !HasBeatenPicoMix(folder)) alts.Remove("pico"); // <song>.hxc hasBeatenPicoMix
            }
            if (alts.Count == 0) return null;
            var bi = Regex.Match(txt, "\"instrumental\"\\s*:\\s*\"([^\"]*)\"");
            var r = new List<string> { bi.Success ? bi.Groups[1].Value : "" };
            r.AddRange(alts);
            return r;
        }
        catch (Exception ex) { Console.WriteLine("[INSTMENU] " + ex.Message); return null; }
    }

    /// <summary>Save.hasBeatenSong(songId, null, 'pico'): any saved score entry for &lt;song&gt;-pico on easy/normal/hard.</summary>
    private static bool HasBeatenPicoMix(string folder)
    {
        if (Environment.GetEnvironmentVariable("FNF_DEV_PICOMIX_BEATEN") == "1") return true;
        var scores = HighscoreManager.Data.SongScores;
        foreach (var b in new[] { folder, folder.Replace('-', '_'), folder.Replace('_', '-') })
            foreach (var d in BaseDiffs)
                if (scores.ContainsKey($"{b}_pico_{d}".ToLowerInvariant())) return true;
        return false;
    }

    /// <summary>capsuleOnConfirm: open the instrumental list instead of confirming (desktop/Xbox; mobile skips it).</summary>
    private bool TryOpenInstMenu(FreeplaySong song)
    {
#if ANDROID
        return false;
#else
        _instChosen = null;
        var ids = ListInstrumentalChoices(song, GetCurrentDifficulty(song));
        if (ids == null) return false;
        _instIds = ids; _instIdx = 0;
        _instMenuOpen = true; _instMenuClosing = false; _instMenuBusy = false; _instAnimT = 0f;
        _instPushL = _instPushR = 0f;
        var cs = Cap(_selectedIndex);
        _instPos = new Vector2(cs.TX + 175f, cs.TY + 115f);
        _instBoxSheet ??= SpriteSheet.Load(Game, "menus/freeplay/instBox/instBox");
        _instSelSheet ??= SpriteSheet.Load(Game, "menus/freeplay/freeplaySelector");
        Console.WriteLine($"[INSTMENU] open [{string.Join(",", ids)}] at {_instPos.X:0.##},{_instPos.Y:0.##}");
        return true;
#endif
    }

    /// <summary>Random capsule: FlxG.random.getObject([base] + alts).</summary>
    private void PickRandomInstrumental(FreeplaySong song)
    {
        var ids = ListInstrumentalChoices(song, GetCurrentDifficulty(song));
        _instChosen = ids == null ? null : ids[_rng.Next(ids.Count)];
    }

    /// <summary>CapsuleOptionsMenu.update. Returns true while the menu owns the input (uiStateMachine Interacting).</summary>
    private bool UpdateInstMenu(float dt)
    {
        if (!_instMenuOpen) return false;
        _instAnimT += dt; _instSelT += dt;
        if (_instPushL > 0f) _instPushL -= dt;
        if (_instPushR > 0f) _instPushR -= dt;
        if (_instMenuClosing)
        {
            if (_instAnimT >= 9f / 24f) { _instMenuOpen = false; _instMenuClosing = false; Console.WriteLine("[INSTMENU] closed"); }
            return true;
        }
        if (_instMenuBusy) return true;
        if (Input.BackPressed)
        {
            _instMenuBusy = true; _instMenuClosing = true; _instAnimT = 0f;
            return true;
        }
        int n = _instIds.Count;
        if (Input.LeftPressed) { _instIdx = (_instIdx + 1) % n; _instPushL = 2f / 24f; }
        if (Input.RightPressed) { _instIdx = (_instIdx - 1 + n) % n; _instPushR = 2f / 24f; }
        if (Input.ConfirmPressed)
        {
            _instMenuBusy = true;
            _instChosen = _instIds[_instIdx] ?? "";
            Console.WriteLine($"[INSTMENU] confirm '{_instChosen}'");
            _instForceConfirm = true; // capsuleOnConfirmDefault(cap, targetInstId) via the normal confirm path
            return false;
        }
        return true;
    }

    private static string TitleCase(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var parts = s.Split(' ');
        for (int i = 0; i < parts.Length; i++)
            if (parts[i].Length > 0) parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1).ToLowerInvariant();
        return string.Join(" ", parts);
    }

    private void DrawInstMenu(SpriteBatch sb)
    {
        if (!_instMenuOpen || _instBoxSheet == null) return;
        float x = _instPos.X, y = _instPos.Y;
        // capsuleMenuBG: 'open' (9 frames, 24 fps) then 'idle'; close = 'open' reversed
        var open = _instBoxSheet.GetAnimation("open");
        var idle = _instBoxSheet.GetAnimation("idle");
        int fi = (int)(_instAnimT * 24f);
        SpriteFrame bg = null;
        if (_instMenuClosing) { if (open != null && open.Count > 0) bg = open[Math.Max(0, open.Count - 1 - fi)]; }
        else if (open != null && fi < open.Count) bg = open[fi];
        else if (idle != null && idle.Count > 0) bg = idle[0];
        if (bg != null && Environment.GetEnvironmentVariable("FNF_INSTLOG") == "1") Console.WriteLine($"[INSTMENU] bg {bg.Name} src={bg.SourceRect} off={bg.Offset} tex={(bg.Texture ?? _instBoxSheet.Texture)?.Width}x{(bg.Texture ?? _instBoxSheet.Texture)?.Height} rot={bg.Rotated}");
        if (bg != null)
            sb.Draw(bg.Texture ?? _instBoxSheet.Texture, new Vector2(x + bg.Offset.X, y + bg.Offset.Y), bg.SourceRect, Color.White);

        // InstrumentalSelector arrows: scale 0.6 + updateHitbox (top-left at x,y), PureColor white while pushed
        if (_instSelSheet != null)
        {
            var frames = _instSelSheet.GetAnimation("arrow pointer loop");
            if (frames != null && frames.Count > 0)
            {
                var f = frames[(int)(_instSelT * 24f) % frames.Count];
                float fw = f.FrameWidth > 0 ? f.FrameWidth : f.SourceRect.Width;
                const float PAD = 4f;
                DrawInstArrow(sb, f, x + PAD, y + 30f, false, _instPushL > 0f);
                DrawInstArrow(sb, f, x + InstBoxW - fw * 0.6f - PAD, y + 30f, true, _instPushR > 0f);
            }
        }

        string cur = TitleCase(_instIds[_instIdx]);
        if (cur == "") cur = "Default";
        DrawCenteredFlxText(sb, 24, "INSTRUMENTAL", x, y + 5f);
        DrawCenteredFlxText(sb, 40, cur, x, y + 36f);
    }

    private void DrawCenteredFlxText(SpriteBatch sb, int size, string text, float x, float y)
    {
        float w = MenuUI.FlxTextWidth(size, text);
        MenuUI.FlxText(sb, size, text, (float)Math.Round(x + (InstBoxW - w) / 2f), y, Color.White);
    }

    private void DrawInstArrow(SpriteBatch sb, SpriteFrame f, float x, float y, bool flip, bool pushed)
    {
        // moveShitDown: offset.y -= 5, scale 0.5*0.6 (no updateHitbox until the timer ends), white
        float baseS = 0.6f, s = pushed ? 0.3f : baseS;
        float fw = f.FrameWidth > 0 ? f.FrameWidth : f.SourceRect.Width, fh = f.FrameHeight > 0 ? f.FrameHeight : f.SourceRect.Height;
        // flixel draw with origin at the frame centre: top-left = x - offset + origin*(1 - scale); offset from updateHitbox at 0.6
        float offX = -0.5f * (fw * baseS - fw), offY = -0.5f * (fh * baseS - fh) - (pushed ? 5f : 0f);
        float tlX = x - offX + fw / 2f * (1f - s), tlY = y - offY + fh / 2f * (1f - s);
        float ox = flip ? fw - f.Offset.X - f.SourceRect.Width : f.Offset.X;
        var tex = f.Texture ?? _instSelSheet.Texture;
        // (PureColor white flash for the 2/24 s push is not reproduced: normal colours)
        sb.Draw(tex, new Vector2(tlX + ox * s, tlY + f.Offset.Y * s), f.SourceRect, Color.White, 0f, Vector2.Zero, s,
            flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
    }
}
