using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json.Linq;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Week 6 song scripts + the V-Slice dialogue system, ported 1:1 from the original:
///  - funkin/play/cutscene/dialogue/Conversation.hx, DialogueBox.hx, Speaker.hx, FunkinTypeText.hx (+ flixel-addons FlxTypeText)
///  - data/dialogue/{conversations,boxes,speakers}/*.json
///  - scripts/dialogue/boxes/{roses,thorns}.hxc, scripts/dialogue/conversations/senpai-pico.hxc
///  - scripts/songs/{senpai,senpai-pico,roses,roses-pico,thorns}.hxc (story-only cutscenes; Pico Mix always)
///  - funkin/effects/RetroCameraFade.hx (camera ColorMatrixFilter offsets), FlxTimer/FlxTween semantics.
/// All drawing of the conversation happens on "camCutscene" (overlay above the HUD, zoom 1).
/// </summary>
public partial class PlayScene
{
    // ------------------------------------------------------------------ flixel helpers

    /// <summary>FlxTimer + FlxTimerManager semantics (callbacks fired once per frame, after all timers updated).</summary>
    private sealed class W6Timer
    {
        public double Time; public int Loops; public int Elapsed; public double Counter; public bool Finished; public bool Active = true;
        public Action<W6Timer> Cb;
        public void Reset() { Finished = false; Active = true; Counter = 0; Elapsed = 0; }
    }

    /// <summary>FlxTween (numeric) with optional ease and start delay.</summary>
    private sealed class W6Tween
    {
        public double Duration, Delay, T; public Func<float, float> Ease; public Action<float> Set; public Action Done; public bool Finished;
    }

    private static Func<float, float> W6Stepped(int steps) => t => MathF.Floor(t * steps) / steps;

    private sealed class W6Anim
    {
        public string Name; public List<SpriteFrame> Frames = new(); public float Fps; public bool Loop;
        public int CurFrame; public float Timer; public bool Finished = true; public bool Paused;
        public float FrameDuration => Fps > 0 ? 1f / Fps : 0f;
    }

    /// <summary>Minimal FlxSprite (Sparrow atlas, FlxAnimationController semantics, updateHitbox'd scale).</summary>
    private sealed class W6Sprite
    {
        public SpriteSheet Sheet; public Texture2D Tex;
        public readonly Dictionary<string, W6Anim> Anims = new();
        public W6Anim Cur;
        public float X, Y, Scale = 1f, Alpha = 1f; public bool Visible = true; public Color Tint = Color.White;
        public int FrameWidth0, FrameHeight0; // frameWidth/Height of atlas frame 0 (FlxSprite.frames setter)
        public Action<string> OnFinish;
        public bool FloorPos; // pixelPerfectPosition

        public static W6Sprite Load(Game game, string path)
        {
            var sheet = SpriteSheet.Load(game, path);
            if (sheet == null || sheet.Texture == null) { Console.WriteLine($"[W6] missing atlas {path}"); return null; }
            var s = new W6Sprite { Sheet = sheet, Tex = sheet.Texture };
            var f0 = sheet.Frames.Values.FirstOrDefault();
            if (f0 != null) { s.FrameWidth0 = f0.FrameWidth > 0 ? f0.FrameWidth : f0.SourceRect.Width; s.FrameHeight0 = f0.FrameHeight > 0 ? f0.FrameHeight : f0.SourceRect.Height; }
            return s;
        }

        private static int TrailingNumber(string name, int prefixLen)
        {
            string rest = name.Length > prefixLen ? name.Substring(prefixLen) : "";
            int i = 0; while (i < rest.Length && char.IsDigit(rest[i])) i++;
            return i > 0 && int.TryParse(rest.Substring(0, i), out int n) ? n : 0;
        }

        /// <summary>animation.addByPrefix / addByIndices (FlxAnimationUtil.addAtlasAnimation).</summary>
        public void Add(string name, string prefix, float fps, bool loop, int[] indices = null)
        {
            var a = new W6Anim { Name = name, Fps = fps, Loop = loop };
            if (indices != null && indices.Length > 0)
            {
                foreach (int idx in indices)
                {
                    var f = Sheet.Frames.Values.FirstOrDefault(fr => fr.Name.StartsWith(prefix, StringComparison.Ordinal)
                        && int.TryParse(fr.Name.Substring(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n == idx);
                    if (f != null) a.Frames.Add(f);
                }
            }
            else
            {
                a.Frames = Sheet.Frames.Values.Where(fr => fr.Name.StartsWith(prefix, StringComparison.Ordinal))
                    .OrderBy(fr => TrailingNumber(fr.Name, prefix.Length)).ToList();
            }
            if (a.Frames.Count == 0) { Console.WriteLine($"[W6] no frames for prefix '{prefix}'"); return; }
            Anims[name] = a;
        }

        public bool Has(string name) => name != null && Anims.ContainsKey(name);

        /// <summary>FlxAnimationController.play + FlxAnimation.play.</summary>
        public void Play(string name, bool force = false)
        {
            if (!Anims.TryGetValue(name, out var a)) return;
            if (Cur != null && Cur != a) { Cur.Finished = true; Cur.Paused = true; }
            Cur = a;
            if (!force && !a.Finished) { a.Paused = false; return; }
            a.Paused = false; a.Timer = 0; a.Finished = a.FrameDuration == 0; a.CurFrame = 0;
            if (a.Finished) OnFinish?.Invoke(a.Name);
        }

        public void Update(float dt)
        {
            var a = Cur;
            if (a == null || a.FrameDuration == 0 || a.Finished || a.Paused) return;
            a.Timer += dt;
            while (a.Timer > a.FrameDuration && !a.Finished)
            {
                a.Timer -= a.FrameDuration;
                if (a.Loop && a.CurFrame == a.Frames.Count - 1) a.CurFrame = 0;
                else if (a.CurFrame + 1 > a.Frames.Count - 1)
                {
                    if (!a.Loop) { a.Finished = true; OnFinish?.Invoke(a.Name); }
                }
                else a.CurFrame++;
                if (a.Finished) break;
            }
        }

        public SpriteFrame Frame => Cur != null && Cur.Frames.Count > 0 ? Cur.Frames[Math.Min(Cur.CurFrame, Cur.Frames.Count - 1)] : Sheet?.Frames.Values.FirstOrDefault();

        /// <summary>Draw like flixel after setScale()/updateHitbox(): frame top-left = (x, y) + frame.offset * scale.</summary>
        public void Draw(SpriteBatch sb, float alphaMul)
        {
            if (!Visible || Tex == null) return;
            var f = Frame; if (f == null) return;
            float a = Alpha * alphaMul; if (a <= 0f) return;
            float px = X, py = Y;
            if (FloorPos)
            {
                // pixelPerfectPosition: flixel floors (x - offset) where offset = -(w*s - w)/2, then adds origin*(1-s)
                float offX = -0.5f * (FrameWidth0 * Scale - FrameWidth0), offY = -0.5f * (FrameHeight0 * Scale - FrameHeight0);
                px = MathF.Floor(X - offX) + offX; py = MathF.Floor(Y - offY) + offY;
            }
            var pos = new Vector2(px + f.Offset.X * Scale, py + f.Offset.Y * Scale);
            sb.Draw(f.Texture ?? Tex, pos, f.SourceRect, new Color(Tint, a), 0f, Vector2.Zero, Scale, SpriteEffects.None, 0f);
        }
    }

    // ------------------------------------------------------------------ data

    private sealed class W6AnimData { public string Name, Prefix; public float Fps = 24; public bool Loop; public int[] Indices; public float OffX, OffY; }

    private static List<W6AnimData> W6ParseAnims(JToken arr)
    {
        var list = new List<W6AnimData>();
        if (arr == null) return list;
        foreach (var a in arr)
        {
            var d = new W6AnimData
            {
                Name = (string)a["name"], Prefix = (string)a["prefix"],
                Fps = a["frameRate"] != null ? (float)a["frameRate"] : 24f,
                Loop = a["looped"] != null && (bool)a["looped"],
                Indices = a["frameIndices"]?.Select(x => (int)x).ToArray()
            };
            if (a["offsets"] is JArray o && o.Count >= 2) { d.OffX = (float)o[0]; d.OffY = (float)o[1]; }
            list.Add(d);
        }
        return list;
    }

    private JObject W6Json(string rel)
    {
        string p = Assets.ResolvePath(rel);
        if (p == null || !global::FNF_MonoGame.Engine.FsCI.FileExists(p)) { Console.WriteLine($"[W6] missing {rel}"); return null; }
        return JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(p));
    }

    private static Color W6ParseColor(string s, out float alpha)
    {
        // FlxColor.fromString("#AARRGGBB" | "#RRGGBB")
        alpha = 1f;
        if (string.IsNullOrEmpty(s)) return Color.Black;
        s = s.TrimStart('#');
        uint v = uint.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if (s.Length <= 6) v |= 0xFF000000u;
        alpha = ((v >> 24) & 0xFF) / 255f;
        return new Color((int)((v >> 16) & 0xFF), (int)((v >> 8) & 0xFF), (int)(v & 0xFF));
    }

    // ------------------------------------------------------------------ Speaker / DialogueBox / text

    private sealed class W6Speaker
    {
        public string Id; public W6Sprite Spr; public float OrigX, OrigY, GlobX, GlobY, AnimOffX, AnimOffY;
        public Dictionary<string, (float, float)> Offsets = new();
        public void ApplyPos() { Spr.X = GlobX + OrigX + AnimOffX; Spr.Y = GlobY + OrigY + AnimOffY; }

        /// <summary>Speaker.playAnimation (with correctAnimationName fallbacks) + applyAnimationOffsets.</summary>
        public void PlayAnimation(string name, bool restart = false)
        {
            string n = name;
            while (n != null && !Spr.Has(n))
            {
                int dash = n.LastIndexOf('-');
                n = dash != -1 ? n.Substring(0, dash) : (n != "idle" ? "idle" : null);
            }
            if (n == null) return;
            Spr.Play(n, restart);
            var o = Offsets.TryGetValue(n, out var oo) ? oo : (0f, 0f);
            AnimOffX = o.Item1; AnimOffY = o.Item2; ApplyPos();
        }
    }

    private W6Speaker W6CreateSpeaker(string id)
    {
        var j = W6Json($"data/dialogue/speakers/{id}.json");
        if (j == null) return null;
        string asset = (string)j["assetPath"];
        var spr = W6Sprite.Load(Game, "week6/images/" + asset);
        if (spr == null) return null;
        var sp = new W6Speaker { Id = id, Spr = spr };
        // loadSpritesheet: screenCenter() at scale 1 (width = frameWidth of frame 0), then globalOffsets, then setScale
        sp.OrigX = (1280f - spr.FrameWidth0) / 2f; sp.OrigY = (720f - spr.FrameHeight0) / 2f;
        if (j["offsets"] is JArray off && off.Count >= 2) { sp.GlobX = (float)off[0]; sp.GlobY = (float)off[1]; }
        spr.Scale = j["scale"] != null ? (float)j["scale"] : 1f;
        foreach (var a in W6ParseAnims(j["animations"]))
        {
            spr.Add(a.Name, a.Prefix, a.Fps, a.Loop, a.Indices);
            sp.Offsets[a.Name] = (a.OffX, a.OffY);
        }
        sp.ApplyPos();
        return sp;
    }

    /// <summary>FunkinTypeText (FlxTypeText + OpenFL TextField layout) rendered with FontStashSharp.</summary>
    private sealed class W6TypeText
    {
        public float X, Y; public int FieldWidth; public Color Color; public Color ShadowColor; public float ShadowAlpha; public int ShadowWidth;
        public SpriteFontBase Font; public bool Visible = true;
        public string Prefix = "", Final = "", Text = "";
        public int Length; public float Timer; public bool Typing; public float Delay = 0.05f;
        public Action Complete; public Action TypeSound; public Action StopSound;

        /// <summary>FunkinTypeText.insertBreakLines: lay out prefix+final with word wrap; keep only final's part with '\n'.</summary>
        private void InsertBreakLines()
        {
            var lines = Layout(Prefix + Final);
            int? prefixLength = Prefix.Length; var split = new StringBuilder();
            foreach (var cur in lines)
            {
                if (prefixLength != null && prefixLength >= cur.Length) prefixLength -= cur.Length;
                else if (prefixLength != null) { split.Append(cur.Substring(prefixLength.Value)); prefixLength = null; }
                else split.Append('\n').Append(cur);
            }
            Final = split.ToString();
        }

        /// <summary>OpenFL TextField word wrap: lines (incl. trailing spaces / '\n') for width fieldWidth - 4 (2px gutters).</summary>
        public List<string> Layout(string s)
        {
            var result = new List<string>(); var paraEnds = new List<int>();
            float maxW = FieldWidth - 4;
            foreach (var para in s.Split('\n'))
            {
                var line = new StringBuilder(); int i = 0;
                var lineStart = result.Count;
                while (i < para.Length)
                {
                    int j = i; while (j < para.Length && para[j] != ' ') j++;           // word
                    int k = j; while (k < para.Length && para[k] == ' ') k++;           // trailing spaces
                    string word = para.Substring(i, j - i), spaces = para.Substring(j, k - j);
                    if (line.Length > 0 && Measure(line.ToString() + word) > maxW)
                    {
                        result.Add(line.ToString()); line.Clear();
                    }
                    line.Append(word).Append(spaces); i = k;
                }
                result.Add(line.ToString());
                paraEnds.Add(result.Count - 1);
            }
            // an explicit '\n' belongs to the line text in OpenFL getLineText (except after the last paragraph)
            for (int n = 0; n < paraEnds.Count - 1; n++) result[paraEnds[n]] += "\n";
            return result;
        }

        public float Measure(string s) => Font == null || s.Length == 0 ? 0 : Font.MeasureString(s).X;

        public void ResetText(string t) { Text = Prefix; Final = t; Typing = false; Length = 0; }

        public void Start()
        {
            Typing = true;
            if (Final.Length == 0) { OnComplete(); return; }
            InsertBreakLines();
        }

        private void OnComplete() { Timer = 0; Typing = false; StopSound?.Invoke(); Complete?.Invoke(); }

        public void Skip() { if (Typing) Length = Final.Length; }

        public void Update(float dt)
        {
            if (Length < Final.Length && Typing) Timer += dt;
            if (Typing && Timer >= Delay)
            {
                Length += (int)(Timer / Delay);
                if (Length > Final.Length) Length = Final.Length;
                Timer %= Delay;
                TypeSound?.Invoke();
            }
            string helper = Prefix + Final.Substring(0, Math.Min(Length, Final.Length));
            if (helper != Text)
            {
                Text = helper;
                if (Length >= Final.Length && Typing) OnComplete();
            }
        }

        public void Draw(SpriteBatch sb, float alpha)
        {
            if (!Visible || Font == null || string.IsNullOrEmpty(Text) || alpha <= 0f) return;
            // OpenFL TextField: 2px gutter; FlxText SHADOW: copies at (borderSize/it*i) for i = it..1, then the text.
            var basePos = new Vector2(X + 2, Y + 2);
            if (ShadowWidth > 0 && ShadowAlpha > 0f)
                for (int i = ShadowWidth; i >= 1; i--)
                    Font.DrawText(sb, Text, basePos + new Vector2(i, i), ShadowColor * (ShadowAlpha * alpha));
            Font.DrawText(sb, Text, basePos, Color * alpha);
        }
    }

    private sealed class W6Box
    {
        public string Id; public W6Sprite Spr; public W6TypeText TextD; public float X, Y;
        public Dictionary<string, (float, float)> Offsets = new();
        public Action TypingComplete;

        /// <summary>DialogueBox.playAnimation (+ correctAnimationName).</summary>
        public void PlayAnimation(string name, bool restart = false)
        {
            string n = name;
            while (n != null && !Spr.Has(n))
            {
                int dash = n.LastIndexOf('-');
                n = dash != -1 ? n.Substring(0, dash) : (n != "idle" ? "idle" : null);
            }
            if (n == null) return;
            Spr.Play(n, restart);
            var o = Offsets.TryGetValue(n, out var oo) ? oo : (0f, 0f);
            Spr.X = X + o.Item1; Spr.Y = Y + o.Item2;
        }
        public bool IsAnimationFinished() => Spr.Cur?.Finished ?? false;
        // DialogueBox.getCurrentAnimation() reads the GROUP's own (empty) animation controller -> always "".
        public string GetCurrentAnimation() => "";
    }

    private SoundEffectInstance _w6TypeSnd;

    private W6Box W6CreateBox(string id)
    {
        var j = W6Json($"data/dialogue/boxes/{id}.json");
        if (j == null) return null;
        var spr = W6Sprite.Load(Game, "week6/images/" + (string)j["assetPath"]);
        if (spr == null) return null;
        var b = new W6Box { Id = id, Spr = spr };
        // onCreate: group screenCenter() with no members (width 0) -> (640,360); + globalOffsets
        float gx = 0, gy = 0;
        if (j["offsets"] is JArray off && off.Count >= 2) { gx = (float)off[0]; gy = (float)off[1]; }
        b.X = 640f + gx; b.Y = 360f + gy;
        spr.Scale = j["scale"] != null ? (float)j["scale"] : 1f;
        spr.X = b.X; spr.Y = b.Y;
        foreach (var a in W6ParseAnims(j["animations"]))
        {
            spr.Add(a.Name, a.Prefix, a.Fps, a.Loop, a.Indices);
            b.Offsets[a.Name] = (a.OffX, a.OffY);
        }
        var t = j["text"];
        var td = new W6TypeText
        {
            FieldWidth = t?["width"] != null ? (int)t["width"] : 300,
            Font = W6Font(t?["size"] != null ? (int)t["size"] : 32),
            ShadowWidth = t?["shadowWidth"] != null ? (int)t["shadowWidth"] : 0,
        };
        td.Color = W6ParseColor((string)t?["color"] ?? "#000000", out _);
        td.ShadowColor = W6ParseColor((string)t?["shadowColor"] ?? "#00000000", out td.ShadowAlpha);
        float tox = 0, toy = 0;
        if (t?["offsets"] is JArray to && to.Count >= 2) { tox = (float)to[0]; toy = (float)to[1]; }
        td.X = b.X + tox; td.Y = b.Y + toy;
        // textDisplay.sounds = [FunkinSound.load(Paths.sound('pixelText'), 0.6)]
        _w6TypeSnd ??= Audio.CreateSoundInstance("week6/sounds/pixelText.ogg");
        td.TypeSound = () => { if (_w6TypeSnd == null) return; _w6TypeSnd.Stop(); _w6TypeSnd.Volume = Math.Clamp(0.6f * Audio.SfxVolume, 0f, 1f); _w6TypeSnd.Play(); };
        td.StopSound = () => _w6TypeSnd?.Stop();
        td.Complete = () => b.TypingComplete?.Invoke();
        b.TextD = td;
        return b;
    }

    private static float s_w6FontPxPerEm = -1f;

    /// <summary>
    /// OpenFL/FreeType font size = em size; FontStashSharp (stb ScaleForPixelHeight) size = ascent - descent.
    /// pixel.otf: unitsPerEm 1000, hhea 1125/-250 -> OpenFL size 32 == FontStashSharp size 44 (= the 44 px line height).
    /// </summary>
    private SpriteFontBase W6Font(int size)
    {
        if (s_w6FontPxPerEm < 0f)
        {
            s_w6FontPxPerEm = 1f;
            try
            {
                var d = global::FNF_MonoGame.Engine.FsCI.ReadAllBytes(Assets.ResolvePath("fonts/pixel.otf"));
                int n = (d[4] << 8) | d[5]; int head = -1, hhea = -1;
                for (int i = 0; i < n; i++)
                {
                    int o = 12 + 16 * i; string tag = Encoding.ASCII.GetString(d, o, 4);
                    int off = (d[o + 8] << 24) | (d[o + 9] << 16) | (d[o + 10] << 8) | d[o + 11];
                    if (tag == "head") head = off; else if (tag == "hhea") hhea = off;
                }
                int upem = (d[head + 18] << 8) | d[head + 19];
                short asc = (short)((d[hhea + 4] << 8) | d[hhea + 5]), desc = (short)((d[hhea + 6] << 8) | d[hhea + 7]);
                s_w6FontPxPerEm = (asc - desc) / (float)upem;
            }
            catch (Exception ex) { Console.WriteLine($"[W6] font metrics: {ex.Message}"); }
        }
        int fss = (int)MathF.Round(size * s_w6FontPxPerEm);
        var f = Assets.GetNamedFont("w6pixel", fss);
        if (f == null) { Assets.LoadNamedFont("w6pixel", "fonts/pixel.otf"); f = Assets.GetNamedFont("w6pixel", fss); }
        return f;
    }

    // ------------------------------------------------------------------ Conversation

    private enum W6ConvState { Start, Opening, Speaking, Idle, Ending }

    private sealed class W6Entry { public string Speaker, SpeakerAnim, Box, BoxAnim; public List<string> Text = new(); }

    private sealed class W6Conversation
    {
        public string Id; public W6ConvState State = W6ConvState.Start;
        public int Entry, Line; public List<W6Entry> Data = new();
        public Color BackdropColor; public float BackdropBaseAlpha, BackdropFade; public bool HasBackdrop; public float BackdropAlpha = 1f;
        public string MusicAsset; public float MusicFade; public SoundEffectInstance Music; public float MusicVol;
        public string OutroType; public float OutroFade = 1f;
        public W6Speaker Speaker; public W6Box Box; public float Alpha = 1f;
        public W6Tween OutroTween; public readonly Dictionary<string, W6Speaker> SpeakerCache = new(); public readonly Dictionary<string, W6Box> BoxCache = new();
        public W6Entry Cur => Entry >= 0 && Entry < Data.Count ? Data[Entry] : null;
        public string CurLine => Cur != null && Line < Cur.Text.Count ? Cur.Text[Line] : "";
    }

    private W6Conversation _w6Conv;

    private W6Conversation W6LoadConversation(string id)
    {
        var j = W6Json($"data/dialogue/conversations/{id}.json");
        if (j == null) return null;
        var c = new W6Conversation { Id = id };
        var bd = j["backdrop"];
        if (bd != null && (string)bd["type"] == "solid")
        {
            c.HasBackdrop = true; c.BackdropColor = W6ParseColor((string)bd["color"], out c.BackdropBaseAlpha);
            c.BackdropFade = bd["fadeTime"] != null ? (float)bd["fadeTime"] : 0f;
        }
        var mu = j["music"];
        if (mu != null) { c.MusicAsset = (string)mu["asset"]; c.MusicFade = mu["fadeTime"] != null ? (float)mu["fadeTime"] : 0f; }
        var ou = j["outro"];
        if (ou != null) { c.OutroType = (string)ou["type"]; c.OutroFade = ou["fadeTime"] != null ? (float)ou["fadeTime"] : 1f; }
        foreach (var e in j["dialogue"])
            c.Data.Add(new W6Entry
            {
                Speaker = (string)e["speaker"], SpeakerAnim = (string)e["speakerAnimation"], Box = (string)e["box"], BoxAnim = (string)e["boxAnimation"],
                Text = e["text"]?.Select(x => (string)x).ToList() ?? new List<string>()
            });
        return c;
    }

    /// <summary>PlayState.startConversation + Conversation.onCreate (DIALOGUE_START).</summary>
    private void W6StartConversation(string id)
    {
        _inCutscene = true;
        _w6Conv = W6LoadConversation(id);
        if (_w6Conv == null) { W6OnConversationComplete(); return; }
        _w6Conv.Entry = 0; _w6Conv.Line = 0; _w6Conv.State = W6ConvState.Start;
        W6OnDialogueStart();
    }

    private void W6SetupMusic()
    {
        var c = _w6Conv;
        if (string.IsNullOrEmpty(c.MusicAsset)) return;
        c.Music = Audio.CreateSoundInstance($"week6/music/{c.MusicAsset}.ogg");
        if (c.Music == null) return;
        c.Music.IsLooped = true; c.MusicVol = 0f; W6ApplyMusicVol(); c.Music.Play();
        if (c.MusicFade > 0f)
        {
            var conv = c;
            W6AddTween(c.MusicFade, 0f, null, v => { conv.MusicVol = v; W6ApplyMusicVol(); });
        }
        else { c.MusicVol = 1f; W6ApplyMusicVol(); }
    }

    private void W6ApplyMusicVol()
    {
        var c = _w6Conv; if (c?.Music == null || c.Music.IsDisposed) return;
        c.Music.Volume = Math.Clamp(c.MusicVol * Audio.MusicVolume, 0f, 1f);
    }

    private void W6StopMusic(bool destroy)
    {
        var c = _w6Conv; if (c?.Music == null) return;
        try { c.Music.Stop(); if (destroy) c.Music.Dispose(); } catch { }
        c.Music = null;
    }

    private void W6OnDialogueStart()
    {
        var c = _w6Conv;
        W6SetupMusic();
        // setupBackdrop
        if (c.HasBackdrop)
        {
            if (c.BackdropFade > 0f) { c.BackdropAlpha = 0f; var conv = c; W6AddTween(c.BackdropFade, 0f, W6Stepped(10), v => conv.BackdropAlpha = v); }
            else c.BackdropAlpha = 1f;
        }
        c.State = W6ConvState.Opening;
        W6ShowCurrentDialogueBox();
        W6PlayDialogueBoxAnimation();
    }

    private void W6ShowCurrentDialogueBox()
    {
        var c = _w6Conv; string id = c.Cur?.Box ?? "";
        if (c.Box != null && c.Box.Id == id) return;
        if (!c.BoxCache.TryGetValue(id, out var box)) { box = W6CreateBox(id); if (box == null) return; c.BoxCache[id] = box; }
        box.TextD.Visible = true; box.TextD.Prefix = ""; box.TextD.ResetText("");
        // scripted boxes (roses.hxc / thorns.hxc)
        box.Spr.OnFinish = name =>
        {
            if (name == "sentenceEnd") box.PlayAnimation("idle");
            if (name == "click") box.PlayAnimation("speaking");
        };
        box.TypingComplete = () => { box.PlayAnimation("sentenceEnd"); W6OnTypingComplete(); };
        c.Box = box;
    }

    private void W6PlayDialogueBoxAnimation()
    {
        var c = _w6Conv; if (c.Cur?.BoxAnim == null) return;
        c.Box?.PlayAnimation(c.Cur.BoxAnim);
    }

    private void W6ShowCurrentSpeaker()
    {
        var c = _w6Conv; string id = c.Cur?.Speaker ?? "";
        if (c.Speaker != null && c.Speaker.Id == id) return;
        c.Speaker = null;
        // SpeakerRegistry caches instances; kill/revive reloads frames + animations (fresh animation state).
        var sp = W6CreateSpeaker(id);
        if (sp == null) return;
        if (c.SpeakerCache.TryGetValue(id, out var old)) { sp.AnimOffX = old.AnimOffX; sp.AnimOffY = old.AnimOffY; sp.ApplyPos(); }
        c.SpeakerCache[id] = sp;
        c.Speaker = sp;
        // scripts/dialogue/conversations/senpai-pico.hxc: stop the music for 'senpai-bwuh', restart it for 'senpai'
        if (c.Id == "senpai-pico")
        {
            if (sp.Id == "senpai-bwuh") W6StopMusic(true);
            else if (sp.Id == "senpai" && c.Music == null) W6SetupMusic();
        }
    }

    private void W6OnTypingComplete()
    {
        var c = _w6Conv; if (c == null) return;
        c.State = W6ConvState.Idle;
    }

    /// <summary>Conversation.advanceConversation (CUTSCENE_ADVANCE).</summary>
    private void W6AdvanceConversation()
    {
        var c = _w6Conv; if (c == null) return;
        switch (c.State)
        {
            case W6ConvState.Start: W6OnDialogueStart(); break;
            case W6ConvState.Opening:
            case W6ConvState.Speaking: c.Box?.TextD.Skip(); break;
            case W6ConvState.Idle: W6OnDialogueLine(); break;
            case W6ConvState.Ending: W6EndOutro(); break;
        }
    }

    private void W6OnDialogueLine()
    {
        var c = _w6Conv;
        // roses.hxc / thorns.hxc onDialogueLine (propagated to the box first)
        if (c.Box != null && (c.Box.Id == "roses" || c.Box.Id == "thorns"))
        {
            c.State = W6ConvState.Opening;
            if (c.Box.Id == "roses") Audio.PlaySound("week6/sounds/textboxClick.ogg", 0.6f);
            // getCurrentAnimation() is always "" on the box group -> neither 'click' nor 'speaking' is played here.
        }
        c.Line += 1;
        if (c.Line >= (c.Cur?.Text.Count ?? 0))
        {
            c.Line = 0; c.Entry += 1;
            if (c.Entry >= c.Data.Count) W6OnDialogueEnd();
            else if (c.State == W6ConvState.Idle)
            {
                W6ShowCurrentDialogueBox(); W6PlayDialogueBoxAnimation(); c.State = W6ConvState.Opening;
            }
        }
        else
        {
            c.State = W6ConvState.Speaking;
            if (c.Box != null) { c.Box.TextD.Prefix = c.Box.TextD.Text; c.Box.TextD.ResetText(c.CurLine); c.Box.TextD.Start(); }
        }
    }

    /// <summary>Pause menu "Skip Dialogue" / Conversation.skipConversation -> DIALOGUE_SKIP -> DIALOGUE_END.</summary>
    private void W6SkipConversation()
    {
        if (_w6Conv == null || _w6Conv.State == W6ConvState.Ending) return;
        W6OnDialogueEnd();
    }

    private void W6OnDialogueEnd()
    {
        var c = _w6Conv;
        // NOTE: the song scripts' `onDialogueEnd()` overrides (Countdown.performCountdown()) never run in the original
        // (verified: song position stays at -5 beats until the outro fade completes). The countdown starts from
        // PlayState.onConversationComplete -> startCountdown() (which also sets camHUD.visible = true).
        // roses.hxc / thorns.hxc onDialogueEnd: hide text, play 'exit'
        if (c.Box != null && (c.Box.Id == "roses" || c.Box.Id == "thorns")) { c.Box.TextD.Visible = false; c.Box.PlayAnimation("exit"); }
        c.State = W6ConvState.Ending;
    }

    private void W6StartOutro()
    {
        var c = _w6Conv;
        if (c.OutroType == "fade")
        {
            var conv = c;
            c.OutroTween = W6AddTween(c.OutroFade, 0f, W6Stepped(8), v => conv.Alpha = 1f - v, () => W6EndOutro());
            if (c.Music != null)
            {
                float start = c.MusicVol;
                W6AddTween(c.OutroFade, 0f, null, v => { conv.MusicVol = start * (1f - v); if (_w6Conv == conv) W6ApplyMusicVol(); });
            }
        }
        else W6EndOutro();
    }

    private void W6EndOutro()
    {
        var c = _w6Conv; if (c == null) return;
        if (c.OutroTween != null) c.OutroTween.Finished = true;
        W6StopMusic(true);
        _w6TypeSnd?.Stop();
        _w6Conv = null;
        W6OnConversationComplete();
    }

    /// <summary>PlayState.onConversationComplete.</summary>
    private void W6OnConversationComplete()
    {
        _inCutscene = false;
        _w6Conv = null;
        if (_dialogueActive) W6StartCountdown(true); // startingSong && !isInCountdown -> startCountdown()
    }

    private void W6UpdateConversation(float dt)
    {
        var c = _w6Conv; if (c == null) return;
        // members (group update): speaker + box sprite + text
        c.Speaker?.Spr.Update(dt);
        if (c.Box != null) { c.Box.Spr.Update(dt); c.Box.TextD.Update(dt); }
        if (_w6Conv != c) return;
        switch (c.State)
        {
            case W6ConvState.Opening:
                if (c.Box != null && (c.Box.IsAnimationFinished() || c.Box.GetCurrentAnimation() != c.Cur?.BoxAnim))
                {
                    c.State = W6ConvState.Speaking;
                    W6ShowCurrentSpeaker();
                    if (c.Cur?.SpeakerAnim != null) c.Speaker?.PlayAnimation(c.Cur.SpeakerAnim);
                    c.Box.TextD.Prefix = ""; c.Box.TextD.ResetText(c.CurLine); c.Box.TextD.Start();
                }
                break;
            case W6ConvState.Ending:
                if (c.OutroTween == null) W6StartOutro();
                break;
        }
    }

    private void W6DrawConversation(SpriteBatch sb)
    {
        var c = _w6Conv; if (c == null) return;
        if (c.HasBackdrop)
        {
            float a = c.BackdropBaseAlpha * c.BackdropAlpha * c.Alpha;
            if (a > 0f) sb.Draw(Assets.Pixel, new Rectangle(0, 0, 1280, 720), new Color(c.BackdropColor, a));
        }
        c.Speaker?.Spr.Draw(sb, c.Alpha);
        c.Box?.Spr.Draw(sb, c.Alpha);
    }

    private void W6DrawConversationText(SpriteBatch sb)
    {
        var c = _w6Conv; if (c?.Box == null) return;
        c.Box.TextD.Draw(sb, c.Alpha);
    }

    // ------------------------------------------------------------------ timers / tweens / camera fades

    private readonly List<W6Timer> _w6Timers = new();
    private readonly List<W6Tween> _w6Tweens = new();

    private W6Timer W6StartTimer(float time, Action<W6Timer> cb, int loops = 1)
    {
        var t = new W6Timer { Time = Math.Abs(time), Loops = loops, Cb = cb };
        _w6Timers.Add(t);
        return t;
    }

    private W6Tween W6AddTween(float duration, float delay, Func<float, float> ease, Action<float> set, Action done = null)
    {
        var t = new W6Tween { Duration = duration, Delay = delay, Ease = ease, Set = set, Done = done };
        _w6Tweens.Add(t);
        return t;
    }

    private void W6UpdateTimersAndTweens(float dt)
    {
        // FlxTimerManager
        var fired = new List<W6Timer>();
        foreach (var t in _w6Timers.ToArray())
        {
            if (!t.Active || t.Finished) continue;
            int before = t.Elapsed;
            t.Counter += dt;
            while (t.Counter >= t.Time && t.Active && !t.Finished)
            {
                t.Counter -= t.Time; t.Elapsed++;
                if (t.Loops > 0 && t.Elapsed >= t.Loops) t.Finished = true;
            }
            if (before != t.Elapsed) fired.Add(t);
        }
        foreach (var t in fired)
        {
            if (t.Finished) _w6Timers.Remove(t);
            t.Cb?.Invoke(t);
            if (!t.Finished && !_w6Timers.Contains(t)) _w6Timers.Add(t);
        }
        // FlxTweenManager
        foreach (var tw in _w6Tweens.ToArray())
        {
            if (tw.Finished) { _w6Tweens.Remove(tw); continue; }
            tw.T += dt;
            if (tw.T < tw.Delay) continue;
            float s = tw.Duration > 0 ? (float)Math.Clamp((tw.T - tw.Delay) / tw.Duration, 0.0, 1.0) : 1f;
            float e = tw.Ease != null ? tw.Ease(s) : s;
            tw.Set?.Invoke(e);
            if (s >= 1f) { tw.Finished = true; _w6Tweens.Remove(tw); tw.Done?.Invoke(); }
        }
    }

    // RetroCameraFade: ColorMatrixFilter RGB offset (in 0..255 units, signed) on camGame / camCutscene.
    private float _w6GameOffset, _w6CutOffset;

    private void W6RetroFade(bool game, bool toWhite, bool fromFull, int camSteps, float time)
    {
        // fadeWhite: steps 0..n (n+1 ticks, +V); fadeToBlack: steps 0..n-1 (n ticks, -V)
        // fadeBlack: starts at -255, steps n..0 (n+1 ticks); fadeFromWhite: starts at +255, steps n..1 (n ticks)
        int stepsTotal = camSteps; int steps = fromFull ? camSteps : 0; float sign = toWhite ? 1f : -1f;
        if (fromFull) W6SetCamOffset(game, sign * 255f);
        int loops = fromFull ? (toWhite ? camSteps : camSteps + 1) : (toWhite ? camSteps + 1 : camSteps);
        W6StartTimer(time / stepsTotal, _ =>
        {
            float v = (1f / stepsTotal) * steps; if (steps == stepsTotal) v = 1f;
            W6SetCamOffset(game, sign * v * 255f);
            if (fromFull) steps--; else steps++;
        }, loops);
    }

    private void W6SetCamOffset(bool game, float v) { if (game) _w6GameOffset = v; else _w6CutOffset = v; }

    private static readonly BlendState W6AddBlend = new BlendState
    {
        ColorSourceBlend = Blend.One, ColorDestinationBlend = Blend.One, ColorBlendFunction = BlendFunction.Add,
        AlphaSourceBlend = Blend.Zero, AlphaDestinationBlend = Blend.One, AlphaBlendFunction = BlendFunction.Add
    };
    private static readonly BlendState W6SubBlend = new BlendState
    {
        ColorSourceBlend = Blend.One, ColorDestinationBlend = Blend.One, ColorBlendFunction = BlendFunction.ReverseSubtract,
        AlphaSourceBlend = Blend.Zero, AlphaDestinationBlend = Blend.One, AlphaBlendFunction = BlendFunction.Add
    };

    // Straight-alpha colour blending that leaves the render target's alpha untouched. The game frame is presented with
    // NonPremultiplied (rgb * a), so writing a < 1 into the RT (BlendState.NonPremultiplied does a*a + d*(1-a)) would
    // darken translucent overlays on screen (backdrop 0xBF -> RT alpha 207 -> 19 % darker than the original).
    private static readonly BlendState W6StraightKeepAlpha = new BlendState
    {
        ColorSourceBlend = Blend.SourceAlpha, ColorDestinationBlend = Blend.InverseSourceAlpha, ColorBlendFunction = BlendFunction.Add,
        AlphaSourceBlend = Blend.Zero, AlphaDestinationBlend = Blend.One, AlphaBlendFunction = BlendFunction.Add
    };
    private static readonly BlendState W6PremulKeepAlpha = new BlendState
    {
        ColorSourceBlend = Blend.One, ColorDestinationBlend = Blend.InverseSourceAlpha, ColorBlendFunction = BlendFunction.Add,
        AlphaSourceBlend = Blend.Zero, AlphaDestinationBlend = Blend.One, AlphaBlendFunction = BlendFunction.Add
    };

    private void W6DrawOffsetQuad(SpriteBatch sb, float offset)
    {
        if (Math.Abs(offset) < 0.5f) return;
        float v = Math.Min(1f, Math.Abs(offset) / 255f);
        sb.Begin(samplerState: SamplerState.PointClamp, blendState: offset > 0 ? W6AddBlend : W6SubBlend);
        sb.Draw(Assets.Pixel, new Rectangle(0, 0, 1280, 720), new Color(v, v, v, 1f));
        sb.End();
    }

    // ------------------------------------------------------------------ song scripts

    private bool _w6Active;            // a week 6 song script controls the pre-song flow
    private string _w6SongId, _w6Variation;
    private bool _w6Story;
    private bool _w6HudHidden;         // camHUD.visible = false (thorns.hxc)
    private float _w6Clock = -1f;      // seconds since first update after loading (dev timing)
    private float[] _w6DevAdv; private int _w6DevAdvIdx;
    private static string s_w6RetryOf; // set by BuildParams(): restarts don't replay the cutscene (onSongRetry)

    // camCutscene sprites created by song scripts
    private W6Sprite _w6SenpaiCrazy;
    private bool _w6BlackVisible;       // senpai.hxc black screen (zIndex 1000000)
    private bool _w6RedVisible; private Color _w6RedColor; private float _w6RedAlpha = 1f; // thorns red / roses outro red
    private bool _w6RosesOutro;

    public double DevW6Elapsed => _w6Clock;
    public bool W6HudVisible => !_w6HudHidden && _w6HudAlpha > 0f;

    public string DevW6Info
    {
        get
        {
            var c = _w6Conv;
            var sb = new StringBuilder($"t={_w6Clock:F3} gameOff={_w6GameOffset:F0} cutOff={_w6CutOffset:F0} hud={(W6HudVisible ? 1 : 0)} countdown={(_countdownActive ? 1 : 0)} dlgActive={(_dialogueActive ? 1 : 0)}");
            if (c != null)
            {
                sb.Append($" conv state={c.State} entry={c.Entry} line={c.Line} alpha={c.Alpha:F3} bd={c.BackdropAlpha:F2} mus={c.MusicVol:F3}");
                if (c.Box != null) sb.Append($" box={c.Box.Id} anim={c.Box.Spr.Cur?.Name}:{c.Box.Spr.Cur?.CurFrame}{(c.Box.Spr.Cur?.Finished == true ? "F" : "")} text=\"{c.Box.TextD.Text.Replace("\n", "\\n")}\"");
                if (c.Speaker != null) sb.Append($" spk={c.Speaker.Id} anim={c.Speaker.Spr.Cur?.Name}:{c.Speaker.Spr.Cur?.CurFrame}{(c.Speaker.Spr.Cur?.Finished == true ? "F" : "")} sx={c.Speaker.Spr.X},{c.Speaker.Spr.Y}");
            }
            if (_w6SenpaiCrazy != null) sb.Append($" crazy a={_w6SenpaiCrazy.Alpha:F2} f={_w6SenpaiCrazy.Cur?.CurFrame}");
            if (_w6RedVisible) sb.Append($" red={_w6RedColor} a={_w6RedAlpha:F2}");
            return sb.ToString();
        }
    }

    private static string W6BaseSong(string songName)
    {
        string s = (songName ?? "").ToLowerInvariant();
        foreach (var id in new[] { "senpai", "roses", "thorns" })
            if (s.StartsWith(id)) return id;
        return null;
    }

    /// <summary>
    /// Called from LoadDialogue() (= the moment the countdown would start; Song.onCountdownStart).
    /// Returns true when a week 6 song script handles the start of the song.
    /// </summary>
    private bool W6Init()
    {
        _w6SongId = W6BaseSong(_songName);
        bool retry = s_w6RetryOf != null && string.Equals(s_w6RetryOf, _songName, StringComparison.OrdinalIgnoreCase);
        s_w6RetryOf = null;
        if (_w6SongId == null) return false;
        string player = Chart?.PlayerCharacter ?? "";
        string stage = Chart?.Stage ?? "";
        _w6Variation = player.StartsWith("pico", StringComparison.OrdinalIgnoreCase) ? "pico"
            : stage.EndsWith("Erect", StringComparison.OrdinalIgnoreCase) ? "erect" : "default";
        _w6Story = _weekSongs != null || Environment.GetEnvironmentVariable("FNF_W6_STORY") == "1";
        var adv = Environment.GetEnvironmentVariable("FNF_W6_ADV");
        if (!string.IsNullOrEmpty(adv)) _w6DevAdv = adv.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();

        // Song script selection: <song>-pico.hxc exists for senpai/roses (variation 'pico'); everything else uses the default script.
        bool picoScript = _w6Variation == "pico" && (_w6SongId == "senpai" || _w6SongId == "roses");
        bool hasPlayedCutscene = retry;
        if (!picoScript && !_w6Story) hasPlayedCutscene = true;   // "Skip the cutscene unless we are in Story Mode"
        _w6RosesOutro = _w6SongId == "roses" && !picoScript && _w6Story;
        if (hasPlayedCutscene) return false;

        _w6Active = true;
        _dialogueActive = true;    // blocks the countdown (event.cancel())
        Console.WriteLine($"[W6] cutscene song={_w6SongId} variation={_w6Variation} story={_w6Story} pico={picoScript}");
        switch (_w6SongId)
        {
            case "senpai": W6SenpaiTransition(picoScript ? "senpai-pico" : "senpai"); break;
            case "roses":
                // FunkinSound.playOnce(Paths.sound('ANGRY_TEXT_BOX'), 1.0); startConversation(naughtyness ? 'roses' : 'roses-censored')
                Audio.PlaySound("sounds/ANGRY_TEXT_BOX.ogg", 1f);
                _w6PendingConversation = picoScript ? "roses-pico" : "roses";
                break;
            case "thorns": W6ThornsCutscene(); break;
        }
        return true;
    }

    private string _w6PendingConversation;

    /// <summary>senpai.hxc / senpai-pico.hxc transitionToDialogue.</summary>
    private void W6SenpaiTransition(string conv)
    {
        _w6BlackVisible = true;
        W6StartTimer(0.25f, _ => { _w6BlackVisible = false; W6RetroFade(true, false, true, 12, 2f); });
        W6AddTween(2f, 0.25f, null, null, () => W6StartConversation(conv));
    }

    /// <summary>thorns.hxc startCutscene.</summary>
    private void W6ThornsCutscene()
    {
        _w6HudHidden = true;
        _w6RedVisible = true; _w6RedColor = new Color(0xFF, 0x1B, 0x31); _w6RedAlpha = 1f;
        var sc = W6Sprite.Load(Game, "week6/images/weeb/senpaiCrazy");
        if (sc == null) { W6StartConversation("thorns"); return; }
        sc.Add("idle", "Senpai Pre Explosion instance 1", 24, false);
        // setGraphicSize(int(width * 6)) -> scale 6; updateHitbox; screenCenter; x += width / 5
        sc.Scale = 6f;
        float w = (int)(sc.FrameWidth0 * 6f), h = sc.FrameHeight0 * 6f;
        sc.X = (1280f - w) / 2f + w / 5f; sc.Y = (720f - h) / 2f;
        sc.FloorPos = true; sc.Alpha = 0f;
        // FlxSprite with frames but no animation playing shows frame 0
        sc.Cur = null;
        _w6SenpaiCrazy = sc;
        W6StartTimer(0.3f, timer =>
        {
            sc.Alpha = Math.Clamp(sc.Alpha + 0.15f, 0f, 1f);
            if (sc.Alpha < 1f) { timer.Reset(); return; }
            sc.Alpha = 1f;
            sc.Play("idle");
            var snd = Audio.CreateSoundInstance("week6/sounds/Senpai_Dies.ogg");
            if (snd != null) { snd.Volume = Math.Clamp(Audio.SfxVolume, 0f, 1f); snd.Play(); }
            _w6DiesSound = snd;
            // FunkinSound.playOnce(..., onComplete): fires when the sound finished playing (7.333 s). The XAudio
            // voice state is not a reliable end signal here, so use the decoded length of the ogg.
            float len = W6OggSeconds("week6/sounds/Senpai_Dies.ogg");
            W6StartTimer(len > 0f ? len : 7.3333f, _ => W6OnSenpaiDiesComplete());
            W6StartTimer(3.2f, _ =>
            {
                W6RetroFade(false, true, false, 8, 1.4f);
                W6StartTimer(1.4f, _ =>
                {
                    _w6RedColor = Color.White;
                    _w6SenpaiCrazy = null;
                    _w6CutOffset = 0f; // camCutscene.filters = []
                    W6RetroFade(false, false, false, 8, 0.8f);
                });
            });
        });
    }

    private SoundEffectInstance _w6DiesSound;

    /// <summary>Length of an Ogg Vorbis file = last page granule position / sample rate.</summary>
    private float W6OggSeconds(string rel)
    {
        try
        {
            var d = global::FNF_MonoGame.Engine.FsCI.ReadAllBytes(Assets.ResolvePath(rel));
            int vi = -1; for (int i = 0; i + 7 < d.Length; i++) if (d[i] == 1 && d[i + 1] == (byte)'v' && d[i + 2] == (byte)'o' && d[i + 3] == (byte)'r') { vi = i; break; }
            int sr = BitConverter.ToInt32(d, vi + 12);
            int last = -1; for (int i = d.Length - 4; i >= 0; i--) if (d[i] == (byte)'O' && d[i + 1] == (byte)'g' && d[i + 2] == (byte)'g' && d[i + 3] == (byte)'S') { last = i; break; }
            long gran = BitConverter.ToInt64(d, last + 6);
            return sr > 0 ? (float)(gran / (double)sr) : -1f;
        }
        catch { return -1f; }
    }

    private void W6OnSenpaiDiesComplete()
    {
        _w6RedVisible = false;
        W6RetroFade(false, false, true, 6, 1.4f);
        W6RetroFade(true, false, true, 6, 1.4f);
        W6StartTimer(1.6f, _ => W6StartConversation("thorns"));
    }

    private void W6StartCountdown(bool viaStartCountdown)
    {
        if (!_dialogueActive) return; // already counting down
        if (PsychNotes) { _psychHold = false; _dialogueActive = true; PsychCountdownBegin(); if (viaStartCountdown) _w6HudHidden = false; return; }
        _dialogueActive = false;
        _countdownActive = true;
        _countdownTimer = 0;
        _countdownStep = 0;
        CallOnLuas("onStartCountdown");
        if (viaStartCountdown) _w6HudHidden = false; // PlayState.startCountdown(): camHUD.visible = true
    }

    /// <summary>Per-frame update (runs while the conversation / fades exist, also during the countdown and song).</summary>
    private void W6Update(float dt)
    {
        if (_w6Clock < 0f) _w6Clock = 0f; else _w6Clock += dt;
        W6LogAbot();
        W6AbotPixelUpdate();
        if (!_w6Active) return;
        if (_w6PendingConversation != null) { var p = _w6PendingConversation; _w6PendingConversation = null; W6StartConversation(p); }

        W6UpdateTimersAndTweens(dt);
        _w6SenpaiCrazy?.Update(dt);

        // handleCutsceneKeys: CUTSCENE_ADVANCE advances, (port) back = Pause > Skip Dialogue
        if (_w6Conv != null && Environment.GetEnvironmentVariable("FNF_W6_SKIP") == "1" && _w6Conv.State != W6ConvState.Ending) W6SkipConversation(); // dev: like the orig test module
        if (_w6Conv != null && !_paused)
        {
            if (Input.CutsceneAdvancePressed) W6AdvanceConversation();
            else if (Input.CutsceneBackPressed) W6SkipConversation();
            if (_w6DevAdv != null && _w6DevAdvIdx < _w6DevAdv.Length && _w6Clock >= _w6DevAdv[_w6DevAdvIdx])
            {
                _w6DevAdvIdx++; Console.WriteLine($"[W6] dev advance at {_w6Clock:F3}"); W6AdvanceConversation();
            }
        }
        W6UpdateConversation(dt);

        // roses.hxc onBeatHit: beat 180 in story mode -> red overlay + camHUD fade (1.5 s)
        if (_w6RosesOutro && _conductor != null && _conductor.CurrentBeat >= 180 && !_countdownActive && !_dialogueActive)
        {
            _w6RosesOutro = false;
            _w6RedVisible = true; _w6RedColor = new Color(0xFF, 0x1B, 0x31); _w6RedAlpha = 0f;
            W6AddTween(2f, 0f, null, v => _w6RedAlpha = v);
            W6AddTween(1.5f, 0f, null, v => _w6HudAlpha = 1f - v);
        }
    }

    private float _w6HudAlpha = 1f;

    /// <summary>While the countdown is blocked (dialogue / cutscene) the world keeps animating (no conductor).</summary>
    private void W6UpdateBlocked(float dt)
    {
        _boyfriend?.Update(dt);
        _opponent?.Update(dt);
        _girlfriend?.Update(dt);
        for (int i = 0; i < _animatedProps.Count; i++)
        {
            var p = _animatedProps[i];
            p.Sprite?.Update(dt);
        }
        UpdateAbotFallbackAnchors();
    }

    /// <summary>camGame ColorMatrixFilter (after the world pass, before the HUD).</summary>
    private void W6DrawGameFilter(SpriteBatch sb) { if (_w6Active) W6DrawOffsetQuad(sb, _w6GameOffset); }

    /// <summary>camCutscene layer (above the HUD and countdown, below pause).</summary>
    private void W6DrawCutscene(SpriteBatch sb)
    {
        if (!_w6Active) return;
        bool any = _w6RedVisible || _w6SenpaiCrazy != null || _w6Conv != null || _w6BlackVisible;
        if (!any) return;
        sb.End();
        sb.Begin(samplerState: SamplerState.PointClamp, blendState: W6StraightKeepAlpha);
        if (_w6RedVisible && _w6RedAlpha > 0f) sb.Draw(Assets.Pixel, new Rectangle(0, 0, 1280, 720), new Color(_w6RedColor, _w6RedAlpha));
        _w6SenpaiCrazy?.Draw(sb, 1f);
        W6DrawConversation(sb);
        sb.End();
        if (_w6Conv != null)
        {
            sb.Begin(samplerState: SamplerState.LinearClamp, blendState: W6PremulKeepAlpha);
            W6DrawConversationText(sb);
            sb.End();
        }
        // camCutscene filter: only visible while the layer is opaque (red sprite present)
        if (_w6RedVisible) W6DrawOffsetQuad(sb, _w6CutOffset);
        if (_w6BlackVisible)
        {
            sb.Begin(samplerState: SamplerState.PointClamp, blendState: W6StraightKeepAlpha);
            sb.Draw(Assets.Pixel, new Rectangle(0, 0, 1280, 720), Color.Black);
            sb.End();
        }
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
    }
}

public partial class PlayScene
{
    /// <summary>
    /// Pixel note style (data/notestyles/pixel.json: isPixel = true -> antialiasing = false). Flixel samples those sprites
    /// with NEAREST; drawing them with LinearClamp blurred them and bled neighbouring atlas cells (the packed
    /// game/skins/pixel/arrows.png has no padding) into the stretched sustains / arrow edges -> visible lines.
    /// </summary>
    private bool W6PixelNotes => _chart?.NoteStyle == "pixel" || (_resolvedNoteSkinPath?.EndsWith("/pixel") ?? false);

    private void W6PixelHudBegin(SpriteBatch sb, Matrix hudMatrix)
    {
        if (!W6PixelNotes) return;
        sb.End();
        sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied, transformMatrix: hudMatrix);
    }

    private void W6PixelHudEnd(SpriteBatch sb, Matrix hudMatrix)
    {
        if (!W6PixelNotes) return;
        sb.End();
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied, transformMatrix: hudMatrix);
    }
}

public partial class PlayScene
{
    private static readonly bool s_w6AbotLog = Environment.GetEnvironmentVariable("FNF_W6_ABOTLOG") == "1";

    /// <summary>Dev: per-frame state of the pixel A-Bot parts (FNF_W6_ABOTLOG=1).</summary>
    private void W6LogAbot()
    {
        if (!s_w6AbotLog || !_abotIsPixel || _conductor == null) return;
        double sp = _conductor.SongPosition;
        if (sp < 1.9 || sp > 9.0) return;
        var sb = new StringBuilder($"[W6ABOT] song={sp * 1000:F0} gf={_girlfriend?.CurrentAnimation}:{_girlfriend?._sprite?.FrameIndex}");
        foreach (var p in _animatedProps)
        {
            if (p.Name == null || !p.Name.StartsWith("neneSpeaker")) continue;
            var s = p.Sprite;
            sb.Append($" {p.Name.Replace("neneSpeaker", "")}[{s?.ResolvedAnimName}:{s?.FrameIndex}{(s?.Finished == true ? "F" : "")} a={p.Alpha:F2} p={s?.Position}]");
        }
        Console.WriteLine(sb.ToString());
    }
}

/// <summary>
/// nene-pixel.hxc A-Bot: pupils (abotHead toleft/toright on FocusCamera), body dance synced with Nene, and the real
/// ABotVis visualizer (funkin.vis SpectralAnalyzer + grig.audio FFT/FFTVisualization, sys path) fed with the Inst PCM.
/// </summary>
public partial class PlayScene
{
    // ---------------- pupils
    private int _w6PupilState; // 0 = PUPIL_STATE_NORMAL, 1 = PUPIL_STATE_LEFT
    private string _w6HeadAnim; // animation currently playing on abotHead (null = none / finished)

    private AnimatedSprite W6FindPropSprite(string name)
    {
        foreach (var p in _animatedProps) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Sprite;
        return null;
    }

    /// <summary>nene-pixel.hxc onSongEvent(FocusCamera): char 0 -> movePupilsRight, char 1 -> movePupilsLeft.</summary>
    private void W6AbotPixelFocus(int focusChar)
    {
        if (!_abotIsPixel) return;
        var head = W6FindPropSprite("neneSpeakerPixelHead");
        if (head == null) return;
        if (focusChar == 0)
        {
            if (_w6PupilState == 0) return;
            head.PlayAnimation("toright", true, false); _w6HeadAnim = "toright";
        }
        else if (focusChar == 1)
        {
            if (_w6PupilState == 1) return;
            head.PlayAnimation("toleft", true, false); _w6HeadAnim = "toleft";
        }
    }

    private string _w6LastGfAnim; private int _w6LastGfFrame = -1; private bool _w6AbotInit;

    /// <summary>Per-frame pixel A-Bot bookkeeping (called from W6Update).</summary>
    private void W6AbotPixelUpdate()
    {
        if (!_abotIsPixel) return;
        var head = W6FindPropSprite("neneSpeakerPixelHead");
        if (head != null)
        {
            if (!_w6AbotInit)
            {
                // Original: no animation is played on creation -> the sprite shows atlas frame 0 ("toleft0000" = eyes right).
                _w6AbotInit = true; _w6PupilState = 0; _w6HeadAnim = null;
                head.FreezeOnFrame("toleft", 0);
            }
            // abotHead.animation.finishCallback: toleft -> LEFT, toright -> NORMAL
            if (_w6HeadAnim != null && head.Finished)
            {
                _w6PupilState = _w6HeadAnim == "toleft" ? 1 : 0;
                _w6HeadAnim = null;
            }
        }
        // dance(): in STATE_DEFAULT the body plays the same danceLeft/danceRight as Nene (forced restart).
        var g = _girlfriend; var body = W6FindPropSprite("neneSpeakerRig");
        if (g != null && body != null)
        {
            string ga = g.CurrentAnimation; int gfr = g._sprite?.FrameIndex ?? 0;
            bool restarted = (ga == "danceLeft" || ga == "danceRight") && (ga != _w6LastGfAnim || gfr < _w6LastGfFrame);
            if (restarted) body.PlayAnimation(ga, true, false);
            _w6LastGfAnim = ga; _w6LastGfFrame = gfr;
        }
    }

    // ---------------- visualizer
    private short[] _w6VizPcm;      // interleaved 16-bit samples of the Inst (lime AudioBuffer.data is Int16; half the memory of float[])
    private int _w6VizChannels = 2, _w6VizRate = 44100;
    private readonly float[] _w6VizLast = new float[7];
    private readonly double[] _w6Blackman = new double[256];
    private readonly double[] _w6Hamming = new double[256];
    private readonly int[] _w6Rev = new int[256];
    private readonly double[] _w6RootRe = new double[128], _w6RootIm = new double[128];
    private bool _w6FftInit;

    private void W6SetVizSource(string instPath)
    {
        // ABotVis (normal AND pixel A-Bot: nene*.hxc all create ABotVis + initAnalyzer on song start)
        _w6VizPcm = null; System.Array.Clear(_w6VizLast, 0, _w6VizLast.Length);
        if (string.IsNullOrEmpty(instPath) || !HasAbotViz()) return;
        string full = Assets.ResolvePath(instPath) ?? Assets.ResolvePath(instPath + ".ogg");
        if (full == null || !global::FNF_MonoGame.Engine.FsCI.FileExists(full)) { Console.WriteLine($"[W6] viz: inst not found {instPath}"); return; }
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                using var vr = new NVorbis.VorbisReader(full);
                int ch = vr.Channels;
                long total = Math.Max(0, vr.TotalSamples) * ch;
                var pcm = new short[(int)Math.Min(int.MaxValue / 2, total + 8192)];
                var buf = new float[4096 * ch];
                int n, w = 0;
                while ((n = vr.ReadSamples(buf, 0, buf.Length)) > 0)
                {
                    if (w + n > pcm.Length) System.Array.Resize(ref pcm, Math.Max(pcm.Length * 2, w + n));
                    for (int i = 0; i < n; i++) { float f = buf[i]; pcm[w++] = (short)(f >= 1f ? 32767 : f <= -1f ? -32767 : f * 32767f); }
                }
                if (w != pcm.Length) System.Array.Resize(ref pcm, w);
                _w6VizChannels = ch; _w6VizRate = vr.SampleRate;
                _w6VizPcm = pcm;
                Console.WriteLine($"[W6] viz pcm ch={ch} rate={vr.SampleRate} samples={_w6VizPcm.Length} lenMs={_w6VizPcm.Length / (double)ch / vr.SampleRate * 1000.0:F1}");
                W6VizReplay();
            }
            catch (Exception ex) { Console.WriteLine($"[W6] viz decode failed: {ex.Message}"); }
        });
    }

    private void W6InitFft()
    {
        _w6FftInit = true;
        const int n = 256; const double TWO_PI = 6.2831853; // grig.audio FFT.TWO_PI
        for (int i = 0; i < n; i++)
        {
            _w6Blackman[i] = 0.42 - 0.50 * Math.Cos(2 * Math.PI * i / (n - 1)) + 0.08 * Math.Cos(4 * Math.PI * i / (n - 1));
            _w6Hamming[i] = 1 - 0.85 * Math.Cos(i * (TWO_PI / n));
            int x = i, y = 0; for (int b = 0; b < 8; b++) { y = (y << 1) | (x & 1); x >>= 1; }
            _w6Rev[i] = y;
        }
        for (int i = 0; i < n / 2; i++) { _w6RootRe[i] = Math.Cos(i * (TWO_PI / n)); _w6RootIm[i] = Math.Sin(i * (TWO_PI / n)); }
    }

    /// <summary>SpectralAnalyzer.getLevels (sys) -> 7 bar values (0..1, maxDelta 0.1 per call).</summary>
    private bool W6VizLevels(double songMs, float[] levels)
    {
        var pcm = _w6VizPcm; if (pcm == null) return false;
        if (!_w6FftInit) W6InitFft();
        const int n = 256; int ch = _w6VizChannels;
        double lengthMs = pcm.Length / (double)ch / _w6VizRate * 1000.0;
        // currentFrame = int(remap(music.time, 0, music.length, 0, dataLength)) = BYTE offset (16-bit), aligned to 2
        long dataLen = (long)pcm.Length * 2;
        long startByte = (long)(songMs / lengthMs * dataLen);
        if (startByte < 0) { System.Array.Clear(levels, 0, levels.Length); return true; }
        startByte -= startByte % 2;
        long s0 = startByte / 2;
        var mixed = new double[n];
        for (int i = 0; i < n; i++)
        {
            double v = 0;
            for (int c = 0; c < ch; c++) { long idx = s0 + (long)i * ch + c; if (idx < pcm.Length) v += 0.7 * (pcm[idx] / 32767.0); }
            mixed[i] = v * _w6Blackman[i];
        }
        // grig.audio FFT.calcFreq
        var re = new double[n]; var im = new double[n];
        for (int i = 0; i < n; i++) { re[_w6Rev[i]] = mixed[i] * _w6Hamming[i]; im[_w6Rev[i]] = 0; }
        int half = 1, inv = n / 2;
        while (inv > 0)
        {
            for (int g = 0; g < n; g += half << 1)
                for (int b = 0; b < half; b++)
                {
                    int e = g + b, o = g + half + b, r = b * inv;
                    double tr = re[o] * _w6RootRe[r] - im[o] * _w6RootIm[r];
                    double ti = re[o] * _w6RootIm[r] + im[o] * _w6RootRe[r];
                    double er = re[e], ei = im[e];
                    re[e] = er + tr; im[e] = ei + ti; re[o] = er - tr; im[o] = ei - ti;
                }
            half <<= 1; inv >>= 1;
        }
        var freq = new double[256]; // indices >= 128 read 0 (Haxe array out of bounds)
        for (int i = 0; i < n / 2; i++) freq[i] = 2 * Math.Sqrt(re[1 + i] * re[1 + i] + im[1 + i] * im[1 + i]) / n;
        freq[n / 2 - 1] = Math.Sqrt(re[n / 2] * re[n / 2] + im[n / 2] * im[n / 2]) / n;
        // FFTVisualization.makeLogGraph(freq, bands = barCount + 1 = 8, dbRange = 40, intRange = 16)
        const int bands = 8;
        var xs = new double[bands + 1];
        for (int i = 0; i < bands; i++) xs[i] = Math.Pow(256, i / (double)bands) - 0.5;
        xs[bands] = 0.0;
        for (int i = 0; i < 7; i++)
        {
            int a = (int)Math.Ceiling(xs[i]), b = (int)Math.Floor(xs[i + 1]);
            double v = 0;
            if (b < a) v += freq[Math.Max(0, b)] * (xs[i + 1] - xs[i]);
            else
            {
                if (a > 0) v += freq[a - 1] * (a - xs[i]);
                while (a < b) { v += freq[a]; a++; }
                if (b < 256) v += freq[b] * (xs[i + 1] - b);
            }
            v *= bands / 12.0;
            double val = (1 + 20 * Math.Log10(v) / 40) * 16;
            int gi = double.IsNaN(val) || double.IsInfinity(val) ? 0 : (int)Math.Clamp(Math.Truncate(val), 0, 16);
            double value = gi / 16.0;
            double last = _w6VizLast[i];
            value = last + Math.Clamp(value - last, -0.1, 0.1); // maxDelta 0.1
            _w6VizLast[i] = (float)value;
            levels[i] = (float)value;
        }
        return true;
    }

    private readonly float[] _w6Levels = new float[7];
    /// <summary>Dev (FNF_W6_VIZREPLAY=orig stdout): recompute the original's logged W6VIZ level sequence.</summary>
    private void W6VizReplay()
    {
        string f = Environment.GetEnvironmentVariable("FNF_W6_VIZREPLAY");
        if (string.IsNullOrEmpty(f) || !global::FNF_MonoGame.Engine.FsCI.FileExists(f)) return;
        var rows = new List<(double t, float[] l)>();
        foreach (var line in global::FNF_MonoGame.Engine.FsCI.ReadLines(f))
        {
            if (!line.Contains("W6VIZ")) continue;
            var m = System.Text.RegularExpressions.Regex.Match(line, @"mt=([0-9.]+)");
            var ls = System.Text.RegularExpressions.Regex.Matches(line, @"L=([0-9.eE+-]+)\]");
            if (!m.Success || ls.Count != 7) continue;
            var l = new float[7];
            for (int i = 0; i < 7; i++) l[i] = float.Parse(ls[i].Groups[1].Value, CultureInfo.InvariantCulture);
            rows.Add((double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), l));
        }
        if (rows.Count < 2) return;
        for (int i = 0; i < 7; i++) _w6VizLast[i] = rows[0].l[i];
        var outL = new float[7];
        var sb = new StringBuilder();
        for (int k = 1; k < rows.Count; k++)
        {
            W6VizLevels(rows[k - 1].t, outL);
            sb.Append($"[W6REPLAY] t={rows[k - 1].t:F0} port={string.Join(",", System.Array.ConvertAll(outL, v => v.ToString("F3", CultureInfo.InvariantCulture)))} orig={string.Join(",", System.Array.ConvertAll(rows[k].l, v => v.ToString("F3", CultureInfo.InvariantCulture)))}" + Environment.NewLine);
            for (int i = 0; i < 7; i++) _w6VizLast[i] = rows[k].l[i]; // resync slew state to the original
        }
        Console.Write(sb.ToString());
        System.Array.Clear(_w6VizLast, 0, 7);
    }
    private readonly StringBuilder _w6VizLog = new StringBuilder();

    private bool HasAbotViz()
    {
        foreach (var p in _animatedProps) if (p.Name != null && p.Name.StartsWith("neneSpeakerViz", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>ABotVis.drawFFT for the A-Bot (pixel and normal, every frame). Returns true when handled.</summary>
    private bool W6PixelVizUpdate()
    {
        bool songRunning = !_countdownActive && !_dialogueActive && _conductor != null && _conductor.SongPosition >= 0 && Audio.MusicPlaying;
        if (!songRunning || !W6VizLevels(_conductor.SongPosition * 1000.0, _w6Levels))
            System.Array.Clear(_w6Levels, 0, _w6Levels.Length); // analyzer == null -> getDefaultLevels() (all 0)
        _w6VizLog.Clear(); _w6VizLog.Append($"[W6VIZ] song={(_conductor?.SongPosition ?? 0) * 1000:F1} mt={Audio.MusicPosition:F1}");
        for (int pi = 0; pi < _animatedProps.Count; pi++)
        {
            var prop = _animatedProps[pi];
            if (prop.Name == null || !prop.Name.StartsWith("neneSpeakerViz", StringComparison.OrdinalIgnoreCase)) continue;
            if (!int.TryParse(prop.Name.Substring("neneSpeakerViz".Length), out int one) || one < 1 || one > 7) continue;
            int animFrame = Audio.MusicVolume <= 0f ? 0 : (int)Math.Floor(_w6Levels[one - 1] * 6 + 0.5); // (FlxG.sound.volume == 0 || muted) ? 0 : Math.round
            bool visible = animFrame > 0;
            animFrame = Math.Clamp(animFrame - 1, 0, 5);
            animFrame = Math.Abs(animFrame - 5);
            prop.Sprite?.FreezeOnFrame($"neneVizBar{one}", animFrame); // VIZ anim has frameRate 0: never advances on its own
            prop.Alpha = visible ? 1f : 0f;
            if (s_w6AbotLog && _conductor != null && _conductor.SongPosition >= 1.7 && _conductor.SongPosition <= 9.0) _w6VizLog.Append($" [{prop.Sprite?.Position.X},{prop.Sprite?.Position.Y} f={animFrame} v={visible} L={_w6Levels[one - 1]:F4}]");
            _animatedProps[pi] = prop;
        }
        if (s_w6AbotLog && _conductor != null && _conductor.SongPosition >= 1.7 && _conductor.SongPosition <= 9.0) Console.WriteLine(_w6VizLog.ToString());
        return true;
    }
}

public partial class PlayScene
{
    /// <summary>school / schoolErect / schoolEvil / schoolEvilErect: all props and characters are isPixel (nearest sampling).</summary>
    private bool W6PixelWorld => (_chart?.Stage ?? "").StartsWith("school", StringComparison.OrdinalIgnoreCase);
}
