using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine;
using FontStashSharp;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Port of V-Slice 0.8.1 PauseSubState.hx (desktop layout):
///  - pause music  music/breakfast{musicSuffix}: musicSuffix '-pico' when pico-playable/pico-dark/pico-christmas/
///    pico-holding-nene/pico-blazin is loaded, '-pixel' for bf-pixel/pico-pixel (character scripts); starts at a random
///    point in the first half, loops, fadeIn(5 s, 0 -> 0.75).
///  - background black 0 -> 0.6 alpha, 0.8 s quartOut.
///  - metadata FlxTexts (vcr 32, x 20, width 1240, right aligned) song / artist / difficulty / deaths / PRACTICE MODE,
///    then offset texts (vcr 16); each tweens alpha 0->1 and y+5 over 1.8 s quartOut, startDelay 0.1, 0.2, ...
///    Artist line alternates with "Charter: X" (15 s delay, 0.75 s quartOut fades).
///  - menu entries: AtlasText BOLD (animated letters, 24 fps) created at (0, 70*i + 30); changeSelection tweens each to
///    x = (i-cur)*1.3*20 + 90, y = (i-cur)*1.3*120 + 720*0.48 in 0.33 s quartOut; alpha 1 selected / 0.6 others.
///  - NEW (not in the original, user request): volume widget bottom-right ('-' / '+' buttons with the bold AtlasText glyphs
///    and a 10-step bar like FunkinSoundTray): mouse click/hold on the buttons or the bar, LEFT/RIGHT (keys, D-pad, stick),
///    LB/RB; and a Psych-style "Toggle Botplay" entry (Psych PauseSubState: cpuControlled = !cpuControlled, stays open).
/// </summary>
public partial class PlayScene
{
    // ---------- AtlasText (funkin.ui.AtlasText, BOLD font) ----------
    private sealed class AtlasFontV
    {
        public Texture2D Texture;
        public float MaxHeight;
        public readonly Dictionary<string, List<Rectangle>> Anims = new();
        public AtlasFontV Digits;   // digit fallback (practice entries)

        public static AtlasFontV Load(AssetManager assets, string basePath)
        {
            string xml = assets.ResolvePath(basePath + ".xml");
            var tex = assets.LoadTexture(basePath + ".png");
            if (xml == null || tex == null) return null;
            var f = new AtlasFontV { Texture = tex };
            var frames = new List<(string name, Rectangle r)>();
            foreach (System.Text.RegularExpressions.Match st in System.Text.RegularExpressions.Regex.Matches(global::FNF_MonoGame.Engine.FsCI.ReadAllText(xml), @"<SubTexture\s+name=""(.*?)""\s+x=""(-?\d+)""\s+y=""(-?\d+)""\s+width=""(\d+)""\s+height=""(\d+)"""))
            {   // regex, not XML: some atlases (Psych alphabet) have raw '&' / '<' in names
                var r = new Rectangle(int.Parse(st.Groups[2].Value), int.Parse(st.Groups[3].Value), int.Parse(st.Groups[4].Value), int.Parse(st.Groups[5].Value));
                frames.Add((System.Net.WebUtility.HtmlDecode(st.Groups[1].Value), r));
                f.MaxHeight = Math.Max(f.MaxHeight, r.Height); // maxHeight = max(frame.frame.height)
            }
            // animation.addByPrefix(prefix): frames whose name = prefix + digits, sorted by number
            foreach (var g in frames.GroupBy(fr => fr.name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9')))
                f.Anims[g.Key] = g.OrderBy(fr => fr.name, StringComparer.Ordinal).Select(fr => fr.r).ToList();
            return f;
        }

        public static string Prefix(char c) => c switch
        {
            '&' => "-andpersand-", '\'' => "-apostraphie-", '\\' => "-back slash-", ',' => "-comma-", '-' => "-dash-",
            '\u201D' => "-end quote-", '!' => "-exclamation point-", '/' => "-forward slash-", '>' => "-greater than-",
            '\u2665' => "-heart-", '<' => "-less than-", '*' => "-multiply x-", '.' => "-period-", '?' => "-question mark-",
            '\u201C' => "-start quote-", _ => c.ToString()
        };

        /// <summary>Draw like AtlasText.appendTextCased: letters advance by their (frame 0) width, bottom-aligned to maxHeight.
        /// animTime = seconds since the letters were created (each AtlasChar loops its 24 fps anim).</summary>
        /// <summary>Width of the widest line (same advance rules as Draw).</summary>
        public float MeasureWidth(string text)
        {
            float x = 0, w = 0;
            foreach (char ch0 in text.ToUpperInvariant())
            {
                if (ch0 == ' ') { x += 40; continue; }
                if (ch0 == '\n') { w = Math.Max(w, x); x = 0; continue; }
                if (Anims.TryGetValue(Prefix(ch0), out var fr) && fr.Count > 0) x += fr[0].Width;
                else if (char.IsDigit(ch0) && Digits != null && Digits.Anims.TryGetValue(ch0 + " bold instance ", out var dg) && dg.Count > 0) x += dg[0].Width;
            }
            return Math.Max(w, x);
        }

        public void Draw(SpriteBatch sb, string text, Vector2 pos, Color color, float animTime)
        {
            float x = 0, y = 0;
            int tick = (int)Math.Floor(animTime * 24f);
            foreach (char ch0 in text.ToUpperInvariant())
            {
                if (ch0 == ' ') { x += 40; continue; }
                if (ch0 == '\n') { x = 0; y += MaxHeight; continue; }
                if (char.IsDigit(ch0) && !Anims.ContainsKey(Prefix(ch0)) && Digits != null && Digits.Anims.TryGetValue(ch0 + " bold instance ", out var dg) && dg.Count > 0)
                {   // port addition: the V-Slice bold font has no digits -> Psych Alphabet bold digits (same hand-drawn style)
                    var d0 = dg[0];
                    sb.Draw(Digits.Texture, new Vector2(pos.X + x, pos.Y + y + MaxHeight - d0.Height), dg[tick % dg.Count], color);
                    x += d0.Width; continue;
                }
                if (!Anims.TryGetValue(Prefix(ch0), out var fr) || fr.Count == 0) continue; // no anim -> 0 size
                var first = fr[0];
                var cur = fr[tick % fr.Count];
                sb.Draw(Texture, new Vector2(pos.X + x, pos.Y + y + MaxHeight - first.Height), cur, color);
                x += first.Width;
            }
        }
    }

    private AtlasFontV _pmFont;
    private Vector2 _pmLastMouse = new(-1, -1);

    // ---------- state ----------
    private sealed class PauseEntryV
    {
        public string Text;
        public float X, Y, FromX, FromY, ToX, ToY, TweenT = -1f;
    }
    private readonly List<PauseEntryV> _pmEntries = new();
    private int _pmCurrent;            // currentEntry as last applied by changeSelection
    private bool _pmNeedsRegenerate;
    private float _pmLetterTime;        // AtlasChar animation clock (reset when the entries are rebuilt)
    private float _pmT;                 // seconds since the pause opened

    private DynamicSoundEffectInstance _pmMusic;
    private static readonly Dictionary<string, (byte[] pcm, int rate, int ch)> _pmMusicCache = new();
    private static readonly object _pmMusicLock = new();
    private int _pmMusicPos;            // byte position
    private string _pmMusicKey;

    private float _pmVolumeFlash;       // seconds since the volume was last changed (for the indicator highlight)

    // Dev hooks: FNF_DEV_PAUSE_AT=<song s> opens the pause menu; FNF_DEV_PAUSE_DOWN_AT=<s since pause> presses DOWN once;
    // FNF_DEV_PAUSE_VOL_AT=<s since pause> presses RB once.
    private static readonly float DevPauseAt = ParseEnvPauseF("FNF_DEV_PAUSE_AT");
    private static readonly float DevPauseDownAt = ParseEnvPauseF("FNF_DEV_PAUSE_DOWN_AT");
    private static readonly float DevPauseVolAt = ParseEnvPauseF("FNF_DEV_PAUSE_VOL_AT");
    private bool _devPauseDone, _devPauseDownDone, _devPauseVolDone;
    private int _devPausePickIdx; // FNF_DEV_PAUSE_PICK="1:Toggle Botplay;2:Resume" (times in s since this pause opened)
    private static readonly (float t, string item)[] DevPausePicks = Environment.GetEnvironmentVariable("FNF_DEV_PAUSE_PICK")?.Split(';')
        .Select(ParsePausePick).Where(x => x != null).Select(x => x.Value).ToArray();
    private static (float, string)? ParsePausePick(string v)
    {
        if (string.IsNullOrEmpty(v) || v.IndexOf(':') < 0) return null;
        var t = ParseFloatInv(v.Substring(0, v.IndexOf(':')));
        return t < 0 ? null : (t, v.Substring(v.IndexOf(':') + 1));
    }
    private static float ParseFloatInv(string s) => float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : -1f;
    private bool _botplayToggledV; // botplay switched from the pause menu (Psych: changedDifficulty -> score not saved)
    public float DevPauseElapsed => _paused ? _pmT : -1f;

    private static float ParseEnvPauseF(string name)
    {
        var s = Environment.GetEnvironmentVariable(name);
        return float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : -1f;
    }

    private static float QuartOut(float t) { t = Math.Clamp(t, 0f, 1f); return 1f - MathF.Pow(1f - t, 4f); }

    /// <summary>True for one frame when the dev hook wants the pause menu to open.</summary>
    private bool DevPauseRequested()
    {
        if (DevPauseAt < 0 || _devPauseDone || _paused || _countdownActive) return false;
        if (_conductor.SongPosition < DevPauseAt) return false;
        _devPauseDone = true;
        return true;
    }

    // Called right after the port opened the pause (PlayScene.Update).
    private void PauseOpenedV()
    {
        _pmLastMouse = new Vector2(-2, -2);
        _pmT = 0f;
        _pmAudioLogNext = 0f;
        _devPausePickIdx = Math.Min(_devPausePickIdx, DevPausePicks?.Length ?? 0);
        _pmVolumeFlash = 99f;
        _pmVolFocus = false; _pmVolSub = 1;
        _pmFont ??= AtlasFontV.Load(Assets, "images/fonts/bold");
        if (_pmFont != null && _pmFont.Digits == null && _practiceMode) _pmFont.Digits = AtlasFontV.Load(Assets, "images/psych/alphabet");
        StartPauseMusicV();
        RegeneratePauseEntriesV();
    }

    /// <summary>Called at the end of BuildPauseMenuItems (regenerateMenu: entries rebuilt, selection reset to 0).</summary>
    private void OnPauseItemsRebuilt()
    {
        _pmNeedsRegenerate = true;
        PreloadPauseMusicV();
    }

    private void RegeneratePauseEntriesV()
    {
        _pmNeedsRegenerate = false;
        _pmEntries.Clear();
        if (_pauseItems == null) return;
        for (int i = 0; i < _pauseItems.Length; i++)
            _pmEntries.Add(new PauseEntryV { Text = _pauseItems[i], X = 0, Y = 70 * i + 30 });
        _pmLetterTime = 0f;
        _pauseSelection = 0;
        _pmCurrent = 0;
        _pmVolFocus = false;
        ApplyPauseSelectionV(); // changeSelection(0)
    }

    private void ApplyPauseSelectionV()
    {
        _pmCurrent = _pauseSelection;
        for (int i = 0; i < _pmEntries.Count; i++)
        {
            var e = _pmEntries[i];
            float d = (i - _pmCurrent) * 1.3f; // FlxMath.remapToRange(i - cur, 0, 1, 0, 1.3)
            e.FromX = e.X; e.FromY = e.Y;
            e.ToX = d * 20f + 90f;
            e.ToY = d * 120f + 720f * 0.48f;
            e.TweenT = 0f;
        }
    }

    /// <summary>Per-frame pause update (replaces the old inline block). Returns after handling input.</summary>
    private void UpdatePauseMenuV(float dt, bool justOpened)
    {
        _pmT += dt;
        _pmLetterTime += dt;
        _pmVolumeFlash += dt;
        _pauseTimer = _pmT;
        _pauseBgAlpha = 0.6f * QuartOut(_pmT / 0.8f);
        UpdatePauseMusicV();

        if (_pmNeedsRegenerate) RegeneratePauseEntriesV();
        else if (_pauseSelection != _pmCurrent) ApplyPauseSelectionV();

        foreach (var e in _pmEntries)
        {
            if (e.TweenT < 0) continue;
            e.TweenT += dt;
            float k = QuartOut(e.TweenT / 0.33f);
            e.X = e.FromX + (e.ToX - e.FromX) * k;
            e.Y = e.FromY + (e.ToY - e.FromY) * k;
            if (e.TweenT >= 0.33f) e.TweenT = -1f;
        }

        // Volume widget (port addition): '-' / '+' buttons + 10-step bar (PlayScene.Pause.cs UpdateVolumeWidgetV).
        UpdateVolumeWidgetV(dt);

        if (justOpened || _pauseItems == null || _pauseItems.Length == 0) return;

        // handleModifyingOffsets: SHIFT + UP/DOWN
        KeyboardState ks = Keyboard.GetState();
        // controller (port addition): hold LT + D-pad/stick Up/Down = SHIFT + UP/DOWN
        if (ks.IsKeyDown(Keys.LeftShift) || ks.IsKeyDown(Keys.RightShift) || Input.IsGamePadHeld(Buttons.LeftTrigger))
        {
            if (Input.UpPressed) { _globalOffset = Math.Min(1500, _globalOffset + 1); HighscoreManager.Data.GlobalOffset = _globalOffset; HighscoreManager.SavePreferences(); }
            if (Input.DownPressed) { _globalOffset = Math.Max(-1500, _globalOffset - 1); HighscoreManager.Data.GlobalOffset = _globalOffset; HighscoreManager.SavePreferences(); }
            return;
        }

        // Dev hook: FNF_DEV_PAUSE_PICK="<s since pause>:<entry>" selects that entry (with the scroll) and confirms it.
        if (DevPausePicks != null && _devPausePickIdx < DevPausePicks.Length && _pmT >= DevPausePicks[_devPausePickIdx].t)
        {
            var pk = DevPausePicks[_devPausePickIdx++];
            int idx = System.Array.IndexOf(_pauseItems, pk.item); Console.WriteLine($"[PAUSE] dev pick {pk.item} idx={idx}");
            if (idx >= 0) { _pauseSelection = idx; ApplyPauseSelectionV(); ExecutePauseMenuItem(_pauseItems[idx]); return; }
        }

        // Mouse (port addition, like the volume widget): hovering an entry selects it, a click confirms it.
        if (_pmFont != null && _pmT > 0.2f)
        {
            var mvp = MouseVirtual;
            bool moved = _pmLastMouse.X > -2 && mvp != _pmLastMouse; _pmLastMouse = mvp; // -2 = pause just opened
            int hit = -1;
            if (mvp.X >= 0)
                for (int i = 0; i < _pmEntries.Count; i++)
                {
                    var e = _pmEntries[i];
                    float w = _pmFont.MeasureWidth(e.Text), h = _pmFont.MaxHeight;
                    if (mvp.X >= e.X && mvp.X < e.X + w && mvp.Y >= e.Y && mvp.Y < e.Y + h) { hit = i; break; }
                }
            if (hit >= 0 && (moved || Input.MouseLeftPressed) && (hit != _pauseSelection || _pmVolFocus))
            { _pmVolFocus = false; _pauseSelection = hit; Audio.PlaySound("scrollMenu", 0.4f); ApplyPauseSelectionV(); }
            if (hit >= 0 && Input.MouseLeftPressed && _pmVolHover == 0)
            { Console.WriteLine($"[PAUSE] mouse pick {_pauseItems[hit]}"); ExecutePauseMenuItem(_pauseItems[hit]); return; }
        }

        if (PracticePauseInput(dt)) return; // practice tools: Left/Right on Skip Time / Playback Rate / Loop
        int change = 0;
        if (Input.UpPressed) change -= 1;
        if (Input.DownPressed) change += 1;
        if (DevPauseDownAt >= 0 && !_devPauseDownDone && _pmT >= DevPauseDownAt) { _devPauseDownDone = true; change += 1; }
        if (change != 0)
        {
            // Port addition: the Volume row sits in the selection cycle after the last entry (Xbox players have no mouse):
            // ... last entry -> DOWN -> Volume -> DOWN -> first entry; first entry -> UP -> Volume -> UP -> last entry.
            int n = _pauseItems.Length;
            Console.WriteLine($"[PAUSE] nav change={change} sel={_pauseSelection} volFocus={_pmVolFocus} n={n}");
            if (_pmVolFocus)
            {
                _pmVolFocus = false;
                _pauseSelection = change > 0 ? 0 : n - 1;
                Audio.PlaySound("scrollMenu", 0.4f);
                ApplyPauseSelectionV();
            }
            else if ((change > 0 && _pauseSelection == n - 1) || (change < 0 && _pauseSelection == 0))
            {
                _pmVolFocus = true; _pmVolFocusT = 0f;
                Audio.PlaySound("scrollMenu", 0.4f);
            }
            else
            {
                int prev = _pauseSelection;
                _pauseSelection = ((_pauseSelection + change) % n + n) % n;
                if (_pauseSelection != prev) Audio.PlaySound("scrollMenu", 0.4f);
                ApplyPauseSelectionV();
            }
        }
        if (Input.ConfirmPressed && !_pmVolFocus)
            ExecutePauseMenuItem(_pauseItems[_pauseSelection]);
    }

    private void ChangeVolumeV(float delta)
    {
        var data = HighscoreManager.Data;
        float newVol = (float)Math.Round(Math.Clamp(Audio.MusicVolume + delta, 0f, 1f), 1);
        Audio.MusicVolume = newVol;
        Audio.SfxVolume = newVol;
        data.MusicVolume = newVol;
        data.SfxVolume = newVol;
        HighscoreManager.SavePreferences();
        _pmVolumeFlash = 0f;
        // FunkinSoundTray feedback sounds (volume 0.3)
        string snd = delta > 0 ? (newVol >= 1f ? "soundtray/VolMAX" : "soundtray/Volup") : "soundtray/Voldown";
        Audio.PlaySound(snd, 0.3f);
    }

    // ---------- pause music ----------
    private string PauseMusicSuffixV()
    {
        string Map(string id) => id switch
        {
            "pico-playable" or "pico-dark" or "pico-christmas" or "pico-holding-nene" or "pico-blazin" => "-pico",
            "bf-pixel" or "pico-pixel" => "-pixel",
            _ => null
        };
        return Map(_boyfriend?.Name ?? "") ?? Map(_girlfriend?.Name ?? "") ?? Map(_opponent?.Name ?? "") ?? "";
    }

    private void PreloadPauseMusicV()
    {
        // Psych mod songs get the same pause music (music/breakfast*) as the base game.
        string key = "breakfast" + PauseMusicSuffixV();
        lock (_pmMusicLock) { if (_pmMusicCache.ContainsKey(key)) return; }
        string path = Assets.ResolvePath($"music/{key}/{key}.ogg");
        if (path == null) return;
        Task.Run(() => DecodePauseMusicV(key, path));
    }

    private static void DecodePauseMusicV(string key, string path)
    {
        try
        {
            lock (_pmMusicLock) { if (_pmMusicCache.ContainsKey(key)) return; }
            using var reader = new NVorbis.VorbisReader(path);
            int ch = reader.Channels;
            var samples = new List<float>((int)Math.Max(0, reader.TotalSamples * ch));
            float[] buf = new float[8192 * ch];
            int n;
            while ((n = reader.ReadSamples(buf, 0, buf.Length)) > 0)
                for (int i = 0; i < n; i++) samples.Add(buf[i]);
            byte[] pcm = new byte[samples.Count * 2];
            for (int i = 0; i < samples.Count; i++)
            {
                short s = (short)Math.Clamp(samples[i] * short.MaxValue, short.MinValue, short.MaxValue);
                pcm[i * 2] = (byte)(s & 0xFF);
                pcm[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
            }
            lock (_pmMusicLock) _pmMusicCache[key] = (pcm, reader.SampleRate, ch);
        }
        catch (Exception ex) { Console.WriteLine($"[PAUSE] music decode failed {path}: {ex.Message}"); }
    }

    private void StartPauseMusicV()
    {
        StopPauseMusicV();
        _pmMusicKey = "breakfast" + PauseMusicSuffixV();
        (byte[] pcm, int rate, int ch) m;
        bool have;
        lock (_pmMusicLock) have = _pmMusicCache.TryGetValue(_pmMusicKey, out m);
        if (!have)
        {
            string path = Assets.ResolvePath($"music/{_pmMusicKey}/{_pmMusicKey}.ogg");
            if (path == null) { Console.WriteLine($"[PAUSE] missing music/{_pmMusicKey}"); return; }
            DecodePauseMusicV(_pmMusicKey, path);
            lock (_pmMusicLock) have = _pmMusicCache.TryGetValue(_pmMusicKey, out m);
            if (!have) return;
        }
        try
        {
            _pmMusic = new DynamicSoundEffectInstance(m.rate, m.ch == 1 ? AudioChannels.Mono : AudioChannels.Stereo);
            int frameBytes = 2 * m.ch;
            int lengthMs = (int)((long)m.pcm.Length / frameBytes * 1000L / m.rate);
            int startMs = _random.Next(0, lengthMs / 2 + 1); // FlxG.random.int(0, Std.int(length / 2))
            _pmMusicPos = (int)((long)startMs * m.rate / 1000L) * frameBytes;
            _pmMusic.Volume = 0f;
            FeedPauseMusicV(m);
            _pmMusic.Play();
            Console.WriteLine($"[PAUSE] music {_pmMusicKey} start={startMs}ms len={lengthMs}ms");
        }
        catch (Exception ex) { Console.WriteLine($"[PAUSE] music failed: {ex.Message}"); _pmMusic = null; }
    }

    private void FeedPauseMusicV((byte[] pcm, int rate, int ch) m)
    {
        if (_pmMusic == null) return;
        int chunk = 4096 * 2 * m.ch;
        while (_pmMusic.PendingBufferCount < 4)
        {
            var b = new byte[chunk];
            int filled = 0;
            while (filled < chunk)
            {
                int take = Math.Min(chunk - filled, m.pcm.Length - _pmMusicPos);
                Buffer.BlockCopy(m.pcm, _pmMusicPos, b, filled, take);
                filled += take; _pmMusicPos += take;
                if (_pmMusicPos >= m.pcm.Length) _pmMusicPos = 0; // looped
            }
            _pmMusic.SubmitBuffer(b);
        }
    }

    private void UpdatePauseMusicV()
    {
        if (_pmMusic == null || _pmMusicKey == null) return;
        (byte[] pcm, int rate, int ch) m;
        lock (_pmMusicLock) if (!_pmMusicCache.TryGetValue(_pmMusicKey, out m)) return;
        FeedPauseMusicV(m);
        // fadeIn(MUSIC_FADE_IN_TIME = 5, 0, MUSIC_FINAL_VOLUME = 0.75): linear; times the global volume.
        float vol = 0.75f * Math.Clamp(_pmT / 5f, 0f, 1f);
        _pmMusic.Volume = Math.Clamp(vol * Audio.MusicVolume, 0f, 1f);
        if (PauseAudioLog && _pmT >= _pmAudioLogNext)
        {
            _pmAudioLogNext += 0.5f;
            int frameBytes = 2 * m.ch;
            Console.WriteLine($"[PAUDIO] t={_pmT:0.00} key={_pmMusicKey} state={_pmMusic.State} vol={_pmMusic.Volume:0.000} fade={vol:0.000} master={Audio.MusicVolume:0.00} pending={_pmMusic.PendingBufferCount} posMs={(long)_pmMusicPos / frameBytes * 1000L / m.rate}");
        }
    }
    private static readonly bool PauseAudioLog = Environment.GetEnvironmentVariable("FNF_PAUDIOLOG") == "1";
    private float _pmAudioLogNext;

    private void StopPauseMusicV()
    {
        if (_pmMusic == null) return;
        try { _pmMusic.Stop(); _pmMusic.Dispose(); } catch { }
        _pmMusic = null;
    }

    // ---------- drawing ----------
    private void DrawPauseOverlayV(SpriteBatch sb)
    {
        sb.Draw(Assets.Pixel, new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT), Color.Black * _pauseBgAlpha);

        // metadata (FlxText: x=20, width=1240 (1250 for the offset texts), RIGHT aligned, 2 px gutter)
        // OpenFL sizes are em sizes (vcr.ttf: unitsPerEm 2048, ascent 1800); FontStashSharp sizes are ascent-descent
        // heights -> FlxText size 32 == GetFont(28), size 16 == GetFont(14).
        var f32 = Assets.GetFont(28);
        var f16 = Assets.GetFont(14);
        string song = _chart?.DisplayName ?? _chart?.SongName ?? _songName ?? "";
        string diff = TitleCaseV((_currentDifficulty ?? _chart?.Difficulty ?? "").Replace('-', ' '));

        // artist / charter cycle: startCharterTimer at create (15 s delay, 0.75 s fades), then startArtistTimer, ...
        float cyc = _pmT % 33f;
        bool charter = cyc >= 15.75f && cyc < 32.25f;
        float artistMul = 1f;
        if (_pmT >= 15f)
        {
            if (cyc >= 15f && cyc < 15.75f) artistMul = 1f - QuartOut((cyc - 15f) / 0.75f);
            else if (cyc >= 15.75f && cyc < 16.5f) artistMul = QuartOut((cyc - 15.75f) / 0.75f);
            else if (cyc >= 31.5f && cyc < 32.25f) artistMul = 1f - QuartOut((cyc - 31.5f) / 0.75f);
            else if (cyc >= 32.25f) artistMul = QuartOut((cyc - 32.25f) / 0.75f);
        }
        string artist = charter ? $"Charter: {_chart?.Charter ?? "Unknown"}" : $"Artist: {_chart?.Artist ?? "Unknown"}";

        var lines = new (string text, SpriteFontBase font, float y, float w, bool vis, float mul)[]
        {
            (song, f32, 15, 1240, true, 1f),
            (artist, f32, 47, 1240, true, artistMul),
            ($"Difficulty: {diff}", f32, 79, 1240, true, 1f),
            ($"{_deathCounter} Blue Balls", f32, 111, 1240, true, 1f),
            ("PRACTICE MODE", f32, 143, 1240, _practiceMode, 1f),
            ($"Global Offset: {_globalOffset}ms", f16, 642, 1250, true, 1f),
            ("Hold SHIFT-UP/DOWN,\nto change the offset.", f16, 665, 1250, true, 1f),
        };
        for (int i = 0; i < lines.Length; i++)
        {
            var l = lines[i];
            if (!l.vis || l.font == null) continue;
            float k = QuartOut((_pmT - 0.1f * (i + 1)) / 1.8f); // startDelay 0.1, 0.2, ...
            float a = k * l.mul;
            if (a <= 0f) continue;
            if (i == lines.Length - 1 && ButtonPrompts.UseController(Input)) { DrawOffsetPadHintV(sb, l.font, 20f, l.y + 5f * k, l.w, a); continue; }
            DrawFlxTextRightV(sb, l.font, l.text, 20f, l.y + 5f * k, l.w, new Color(Color.White, a)); // overlay batch is straight-alpha
        }

        DrawVolumeWidgetV(sb); // port addition: '-' / '+' volume buttons

        // menu entries
        if (_pmFont != null)
        {
            for (int i = 0; i < _pmEntries.Count; i++)
            {
                var e = _pmEntries[i];
                if (e.Y > FNFGame.SCREEN_HEIGHT || e.Y < -200) continue;
                float a = i == _pmCurrent && !_pmVolFocus ? 1f : 0.6f;
                _pmFont.Draw(sb, e.Text, new Vector2(e.X, e.Y), new Color(Color.White, a), _pmLetterTime);
            }
        }
    }

    private static string TitleCaseV(string s)
    {
        var parts = s.Split(' ');
        for (int i = 0; i < parts.Length; i++)
            if (parts[i].Length > 0) parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1).ToLowerInvariant();
        return string.Join(" ", parts);
    }

    /// <summary>Controller version of "Hold SHIFT-UP/DOWN, to change the offset.": LT glyph + D-pad Up/Down glyph
    /// (same FlxText right alignment/lines as the keyboard text).</summary>
    private void DrawOffsetPadHintV(SpriteBatch sb, SpriteFontBase font, float x, float y, float width, float a)
    {
        var col = new Color(Color.White, a);
        float right = x + width - 2f, lineY = y + 1f;
        const int g = 17;
        string t1 = " UP/DOWN,"; var w1 = font.MeasureString(t1).X;
        font.DrawText(sb, t1, new Vector2(MathF.Round(right - w1), MathF.Round(lineY)), col);
        float gx = right - w1 - g;
        ButtonPrompts.DrawGlyph(sb, Assets, true, ButtonPrompts.DPadUpDown, "", (int)gx, (int)lineY - 3, g, a);
        string plus = " + "; var wp = font.MeasureString(plus).X; gx -= wp;
        font.DrawText(sb, plus, new Vector2(MathF.Round(gx), MathF.Round(lineY)), col);
        gx -= g;
        ButtonPrompts.DrawGlyph(sb, Assets, true, Buttons.LeftTrigger, "", (int)gx, (int)lineY - 3, g, a);
        string hold = "Hold "; var wh = font.MeasureString(hold).X;
        font.DrawText(sb, hold, new Vector2(MathF.Round(gx - wh), MathF.Round(lineY)), col);
        string t2 = "to change the offset."; var w2 = font.MeasureString(t2).X;
        font.DrawText(sb, t2, new Vector2(MathF.Round(right - w2), MathF.Round(lineY + 15f)), col);
    }

    /// <summary>FlxText with alignment RIGHT: each line right-aligned inside [x+2, x+width-2]. Vertical placement and
    /// line height measured against the running original (OpenFL text: 32px -> glyph tops 1 px above our ascent
    /// placement, 16px -> 1 px; 16px line height 15 px).</summary>
    private void DrawFlxTextRightV(SpriteBatch sb, SpriteFontBase font, string text, float x, float y, float width, Color color)
    {
        bool small = font == Assets.GetFont(14);
        float lineY = y + 2f + (small ? -1f : -1f);
        foreach (var line in text.Split('\n'))
        {
            var size = font.MeasureString(line);
            font.DrawText(sb, line, new Vector2(MathF.Round(x + width - 2f - size.X), MathF.Round(lineY)), color);
            lineY += small ? 15f : font.LineHeight;
        }
    }
}
