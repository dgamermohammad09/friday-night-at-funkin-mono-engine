using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json.Linq;
using FNF_MonoGame.Engine;
using CP = FNF_MonoGame.Engine.PsychEngine.ClientPrefs;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// V-Slice 0.8.1 note-style driven countdown + combo pop-ups (1:1 ports):
///  - NoteStyle.hx buildCountdownSprite / isCountdownSpritePixel / getCountdownSpriteOffsets / getCountdownSoundPath,
///    buildJudgementSprite, buildComboNumSprite (+ "fallback" chain), data/notestyles/&lt;id&gt;.json (base: funkin, pixel; mods too).
///  - Countdown.hx showCountdownGraphic (screenCenter + offsets, alpha 1 -> 0 over one beat, cubeInOut or stepped(8) if pixel)
///    and playCountdownSound (FunkinSound.playOnce(path, COUNTDOWN_VOLUME = 0.6)).
///  - PopUpStuff.hx displayRating / displayCombo (camHUD, flixel motion, alpha tween 0.2 s after beat*0.001 / *0.002 s,
///    stepped(2) ease for pixel sprites, pixelPerfectPosition).
/// Used for V-Slice songs (base + V-Slice mods); Psych songs keep their own popUpScore path.
/// </summary>
public partial class PlayScene
{
    // ------------------------------------------------------------------ note style data
    private sealed class NsSprite
    {
        public Texture2D Tex; public float Scale = 1f; public bool Pixel; public float OffX, OffY;
    }

    private readonly Dictionary<string, JObject> _nsJsonCache = new();
    private readonly Dictionary<string, NsSprite> _nsSpriteCache = new();
    private string _nsStyleId;

    private bool VsNoteStylePopups => !PsychNotes;

    private JObject NsJson(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (_nsJsonCache.TryGetValue(id, out var c)) return c;
        JObject j = VsNoteStyleJson(id);
        if (j == null)
        {
            try
            {
                string p = System.IO.Path.Combine(AssetManager.ContentRoot, "data", "notestyles", id + ".json");
                if (FsCI.FileExists(p)) j = JObject.Parse(FsCI.ReadAllText(p));
            }
            catch (Exception ex) { Console.WriteLine($"[NOTESTYLE] {id}: {ex.Message}"); }
        }
        _nsJsonCache[id] = j;
        return j;
    }

    /// <summary>NoteStyleRegistry.fetchEntry(currentChart.noteStyle) ?? fetchDefault() ("funkin").</summary>
    private string NsStyle()
    {
        if (_nsStyleId != null) return _nsStyleId;
        string id = _chart?.NoteStyle;
        if (string.IsNullOrEmpty(id) || NsJson(id) == null) id = "funkin";
        return _nsStyleId = id;
    }

    /// <summary>Styles in fallback order starting at <paramref name="id"/>.</summary>
    private IEnumerable<JObject> NsChain(string id)
    {
        for (int guard = 0; guard < 8 && !string.IsNullOrEmpty(id); guard++)
        {
            var j = NsJson(id);
            if (j == null) yield break;
            yield return j;
            var fb = j["fallback"];
            id = fb == null || fb.Type == JTokenType.Null ? null : fb.ToString();
        }
    }

    private static JToken NsField(JObject style, string asset, string field)
    {
        var a = style?["assets"]?[asset];
        if (a == null || a.Type == JTokenType.Null) return null;
        var v = a[field];
        return v == null || v.Type == JTokenType.Null ? null : v;
    }

    /// <summary>build*Sprite(asset): the first style in the chain whose assets.&lt;asset&gt; exists owns scale; assetPath,
    /// isPixel and offsets fall back further when null. Returns null when there is no graphic (assetPath null).</summary>
    private NsSprite NsBuild(string asset)
    {
        if (_nsSpriteCache.TryGetValue(asset, out var cached)) return cached;
        NsSprite result = null;
        var chain = new List<JObject>(NsChain(NsStyle()));
        int own = chain.FindIndex(j => j["assets"]?[asset] is JObject);
        if (own >= 0)
        {
            string path = null; bool? pixel = null; float[] offs = null;
            for (int i = own; i < chain.Count; i++)
            {
                path ??= NsField(chain[i], asset, "assetPath")?.ToString();
                if (pixel == null && NsField(chain[i], asset, "isPixel") is JToken px) pixel = (bool)px;
                if (offs == null && NsField(chain[i], asset, "offsets") is JArray oa && oa.Count >= 2) offs = new[] { (float)oa[0], (float)oa[1] };
            }
            if (!string.IsNullOrEmpty(path))
            {
                int sep = path.IndexOf(':');
                if (sep >= 0) path = path.Substring(sep + 1);
                var tex = Assets.LoadTexture("images/" + path + ".png");
                if (tex != null && tex != Assets.Pixel)
                {
                    var sc = NsField(chain[own], asset, "scale");
                    result = new NsSprite
                    {
                        Tex = tex, Scale = sc != null ? (float)sc : 1f, Pixel = pixel ?? false,
                        OffX = offs?[0] ?? 0f, OffY = offs?[1] ?? 0f,
                    };
                }
                else Console.WriteLine($"[NOTESTYLE] {NsStyle()} {asset}: images/{path}.png MISSING");
            }
        }
        _nsSpriteCache[asset] = result;
        return result;
    }

    /// <summary>getCountdownSoundPath: assets.&lt;asset&gt;.data.audioPath of the first style that has one -> sounds/&lt;path&gt;.</summary>
    private string NsSoundPath(string asset)
    {
        foreach (var j in NsChain(NsStyle()))
        {
            var p = j["assets"]?[asset]?["data"]?["audioPath"];
            if (p == null || p.Type == JTokenType.Null) continue;
            string s = p.ToString();
            int sep = s.IndexOf(':');
            if (sep >= 0) s = s.Substring(sep + 1);
            return "sounds/" + s;
        }
        return null;
    }

    private static readonly string[] NsCountdownAssets = { "countdownThree", "countdownTwo", "countdownOne", "countdownGo" };

    /// <summary>Load-time: decode the style's countdown sounds and textures (no hitch on the first beat).</summary>
    private void NsPreload()
    {
        _nsStyleId = null; _nsSpriteCache.Clear(); _nsJsonCache.Clear();
        if (!VsNoteStylePopups) return;
        foreach (var a in NsCountdownAssets)
        {
            NsBuild(a);
            string s = NsSoundPath(a);
            if (s != null) { try { Audio.PreloadSound(s); } catch { } }
        }
        foreach (var r in new[] { "judgementSick", "judgementGood", "judgementBad", "judgementShit" }) NsBuild(r);
        for (int d = 0; d < 10; d++) NsBuild("comboNumber" + d);
        Console.WriteLine($"[NOTESTYLE] style={NsStyle()} ready={NsBuild("countdownTwo") != null} sound3={NsSoundPath("countdownThree")}");
    }

    // ------------------------------------------------------------------ countdown (Countdown.hx)
    private sealed class NsCdSprite { public NsSprite S; public float X, Y, Alpha = 1f, T, Dur; public bool Stepped; }
    private readonly List<NsCdSprite> _nsCountdown = new();

    /// <summary>Countdown step tick: step 0 = THREE, 1 = TWO, 2 = ONE, 3 = GO. Returns false if not handled (Psych).</summary>
    private bool NsCountdownStep(int step)
    {
        if (!VsNoteStylePopups || step < 0 || step > 3) return false;
        if (step == 0) NsClear(); // (re)started countdown
        string asset = NsCountdownAssets[step];
        // showCountdownGraphic
        var s = NsBuild(asset);
        if (s != null)
        {
            float w = s.Tex.Width * s.Scale, h = s.Tex.Height * s.Scale;
            _nsCountdown.Add(new NsCdSprite
            {
                S = s, X = (1280f - w) / 2f + s.OffX, Y = (720f - h) / 2f + s.OffY,
                Dur = (float)_conductor.Crochet, Stepped = s.Pixel,
            });
        }
        // playCountdownSound
        string snd = NsSoundPath(asset);
        if (snd != null) Audio.PlaySound(snd, 0.6f);
        return true;
    }

    private void NsCountdownUpdate(float dt)
    {
        if (_countdownActive && s_nsLog) NsDevTick();
        for (int i = _nsCountdown.Count - 1; i >= 0; i--)
        {
            var c = _nsCountdown[i];
            c.T += dt;
            float t = c.Dur <= 0f ? 1f : Math.Min(1f, c.T / c.Dur);
            float e = c.Stepped ? MathF.Floor(t * 8f) / 8f : (t <= 0.5f ? 4f * t * t * t : 1f + 4f * (t - 1f) * (t - 1f) * (t - 1f));
            c.Alpha = 1f - e;
            if (t >= 1f) _nsCountdown.RemoveAt(i);
        }
    }

    private bool NsCountdownDraw(SpriteBatch sb, float hudAlpha)
    {
        if (!VsNoteStylePopups) return false;
        foreach (var c in _nsCountdown)
        {
            float a = c.Alpha * hudAlpha;
            if (a <= 0f) continue;
            if (c.S.Pixel) { sb.End(); sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied); }
            sb.Draw(c.S.Tex, new Vector2(c.X, c.Y), null, new Color(255, 255, 255, (int)(a * 255f + 0.5f)), 0f, Vector2.Zero, c.S.Scale, SpriteEffects.None, 0f);
            if (c.S.Pixel) { sb.End(); sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied); }
        }
        return true;
    }

    // ------------------------------------------------------------------ pop-ups (PopUpStuff.hx)
    private sealed class NsPopup
    {
        public NsSprite S; public float X, Y, VX, VY, AY, Alpha = 1f, Delay, Fade; public bool Rating; public string RatingName;
    }
    private readonly List<NsPopup> _nsPopups = new(32);

    private static string NsRatingKey(string rating) => rating switch
    {
        "SICK!!" => "sick", "GOOD!" => "good", "BAD" => "bad", "SHIT" => "shit", _ => null
    };

    /// <summary>PlayState.popUpScore: displayRating(daRating) + displayCombo(combo) if combo &gt;= 10.</summary>
    private bool NsPopUpScore(string rating, int combo)
    {
        if (!VsNoteStylePopups) return false;
        string key = NsRatingKey(rating);
        if (key != null && PopupShowRating) NsDisplayRating(key, rating);
        if (combo >= 10 && PopupShowCombo) NsDisplayCombo(combo);
        return true;
    }

    /// <summary>applyScore(isComboBreak): if (tallies.combo &gt;= 10) comboPopUps.displayCombo(0).</summary>
    private void NsComboBreak(int comboBefore)
    {
        if (!VsNoteStylePopups || comboBefore < 10 || !PopupShowCombo) return;
        NsDisplayCombo(0);
    }

    private void NsDisplayRating(string key, string rating)
    {
        var s = NsBuild("judgement" + char.ToUpperInvariant(key[0]) + key.Substring(1));
        if (s == null) return;
        float w = s.Tex.Width * s.Scale, h = s.Tex.Height * s.Scale;
        var p = new NsPopup
        {
            S = s, Rating = true, RatingName = rating,
            X = 1280f * 0.474f - w / 2f + _popUpOffsets.X + s.OffX,
            Y = 720f * 0.45f - 60f - h / 2f + _popUpOffsets.Y + s.OffY,
            AY = 550f,
            Delay = (float)(_conductor.Crochet * 1000.0 * 0.001),
        };
        p.VY -= VsStatic.Rng.Next(140, 176);
        p.VX -= VsStatic.Rng.Next(0, 11);
        _nsPopups.Add(p);
    }

    private readonly int[] _nsDigits = new int[12];

    private void NsDisplayCombo(int combo)
    {
        var digits = _nsDigits; int n = 0;
        int temp = combo;
        while (temp != 0 && n < 12) { digits[n++] = temp % 10; temp /= 10; }
        while (n < 3) digits[n++] = 0;
        int daLoop = 1;
        for (int i = 0; i < n; i++)
        {
            var s = NsBuild("comboNumber" + digits[i]);
            if (s == null) continue;
            var p = new NsPopup
            {
                S = s,
                X = 1280f * 0.507f - 36f * daLoop - 65f + _popUpOffsets.X + s.OffX,
                Y = 720f * 0.44f + _popUpOffsets.Y + s.OffY,
                Delay = (float)(_conductor.Crochet * 1000.0 * 0.002),
            };
            p.AY = VsStatic.Rng.Next(250, 301);
            p.VY -= VsStatic.Rng.Next(130, 151);
            p.VX = (float)(VsStatic.Rng.NextDouble() * 10.0 - 5.0);
            _nsPopups.Add(p);
            daLoop++;
        }
    }

    private void NsPopupsUpdate(float dt)
    {
        NsDevTick();
        for (int i = _nsPopups.Count - 1; i >= 0; i--)
        {
            var p = _nsPopups[i];
            if (s_popFreezeAll) { p.VX = p.VY = p.AY = 0f; }
            // FlxObject.updateMotion: half-step velocity integration (exact for constant acceleration)
            float dvy = 0.5f * p.AY * dt;
            p.VY += dvy; p.Y += p.VY * dt; p.VY += dvy;
            p.X += p.VX * dt;
            // FlxTween alpha 1 -> 0 over 0.2 s after startDelay
            if (p.Delay > 0f) { p.Delay -= dt; if (p.Delay > 0f) continue; dt = -p.Delay; p.Delay = 0f; }
            p.Fade += dt;
            float t = Math.Min(1f, p.Fade / 0.2f);
            float e = p.S.Pixel ? MathF.Floor(t * 2f) / 2f : t;
            p.Alpha = 1f - e;
            if (t >= 1f) _nsPopups.RemoveAt(i);
        }
    }

    private void NsPopupsDraw(SpriteBatch sb, Matrix hudMatrix)
    {
        if (_nsPopups.Count == 0) return;
        bool point = false;
        foreach (var p in _nsPopups)
        {
            if (p.Alpha <= 0f) continue;
            if (p.S.Pixel != point)
            {
                point = p.S.Pixel;
                sb.End();
                sb.Begin(samplerState: point ? SamplerState.PointClamp : SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied, transformMatrix: hudMatrix);
            }
            Color tint = p.Rating ? PopupRatingTint(p.RatingName) : PopupComboTint();
            var col = new Color(tint.R, tint.G, tint.B, (int)(p.Alpha * 255f + 0.5f));
            // pixelPerfectPosition (pixel styles): the draw position is floored
            var pos = p.S.Pixel ? new Vector2(MathF.Floor(p.X), MathF.Floor(p.Y)) : new Vector2(p.X, p.Y);
            sb.Draw(p.S.Tex, pos, null, col, 0f, Vector2.Zero, p.S.Scale, SpriteEffects.None, 0f);
        }
        if (point)
        {
            sb.End();
            sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied, transformMatrix: hudMatrix);
        }
    }

    private void NsClear() { _nsPopups.Clear(); _nsCountdown.Clear(); }

    // ------------------------------------------------------------------ dev hooks
    private static readonly bool s_nsLog = Environment.GetEnvironmentVariable("FNF_NSLOG") == "1";
    private static readonly float s_nsPopAt = float.TryParse(Environment.GetEnvironmentVariable("FNF_DEV_POP_AT"),
        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : float.NaN;
    private bool _nsPopDone;
    private int _nsPopsIdx;
    private static readonly List<(double, int, int)> s_nsPops = ParseDevPops(Environment.GetEnvironmentVariable("FNF_DEV_POPS"));
    private static List<(double, int, int)> ParseDevPops(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        var l = new List<(double, int, int)>();
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var e in s.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = e.Split(':');
            if (p.Length >= 3) l.Add((double.Parse(p[0], ci), int.Parse(p[1], ci), int.Parse(p[2], ci)));
        }
        return l;
    }

    /// <summary>Song position in ms including the countdown (V-Slice Conductor starts at -5 beats).</summary>
    public double DevCountdownSongMs => _conductor == null ? -1e9 : _countdownActive
        ? (-5 * _conductor.Crochet + Math.Min(_countdownStep, 5) * _conductor.Crochet + _countdownTimer) * 1000.0
        : _conductor.SongPosition * 1000.0;

    /// <summary>FNF_DEV_POP_AT=&lt;song s&gt;: displayRating('sick') + displayCombo(123) once (matches the original probe);
    /// FNF_NSLOG=1: per-frame countdown / pop-up sprite dump.</summary>
    private void NsDevTick()
    {
        if (!float.IsNaN(s_nsPopAt) && !_nsPopDone && !_countdownActive && _conductor != null && _conductor.SongPosition >= s_nsPopAt)
        {
            _nsPopDone = true;
            NsDisplayRating("sick", "SICK!!"); NsDisplayCombo(123);
            Console.WriteLine($"POPSPAWN t={DevCountdownSongMs:F1}");
        }
        // FNF_DEV_POPS="ms:rating:combo;.." (rating 0 sick 1 good 2 bad 3 shit, -1 none; combo -1 none): same probe as the
        // original pops.tpl (displayRating(names[r]) + displayCombo(c) at song ms).
        if (s_nsPops != null && !_countdownActive && _conductor != null)
            while (_nsPopsIdx < s_nsPops.Count && _conductor.SongPosition * 1000.0 >= s_nsPops[_nsPopsIdx].Item1)
            {
                var (_, r, c) = s_nsPops[_nsPopsIdx++];
                if (r == -2) { _combo = c; Console.WriteLine($"POPSETCOMBO {c}"); continue; } // r=-2: set the combo counter (setProperty('combo', c))
                string[] keys = { "sick", "good", "bad", "shit" }, names = { "SICK!!", "GOOD!", "BAD", "SHIT" };
                if (VsNoteStylePopups) { if (r >= 0) NsDisplayRating(keys[r], names[r]); if (c >= 0) NsDisplayCombo(c); }
                else PsyPopUpScore(r >= 0 ? names[r] : "SICK!!", Math.Max(0, c));
                if (Environment.GetEnvironmentVariable("FNF_DEV_POPFREEZE") == "1") // no motion: pixel-exact position checks
                {
                    foreach (var p in _nsPopups) { p.VX = p.VY = p.AY = 0f; }
                    foreach (var p in _psyPops) { p.VX = p.VY = p.AY = 0f; }
                }
                Console.WriteLine($"POPSPAWN t={DevCountdownSongMs:F1} r={r} c={c} n={_nsPopups.Count + _psyPops.Count}");
            }
        if (!s_nsLog) return;
        foreach (var c in _nsCountdown)
            Console.WriteLine($"CDSPR t={DevCountdownSongMs:F1} x={c.X:F2} y={c.Y:F2} w={c.S.Tex.Width * c.S.Scale:F1} h={c.S.Tex.Height * c.S.Scale:F1} sc={c.S.Scale} a={c.Alpha:F4} px={c.S.Pixel}");
        foreach (var p in _nsPopups)
            Console.WriteLine($"POPSPR t={DevCountdownSongMs:F1} x={p.X:F2} y={p.Y:F2} w={p.S.Tex.Width * p.S.Scale:F1} h={p.S.Tex.Height * p.S.Scale:F1} sc={p.S.Scale} a={p.Alpha:F4} vx={p.VX:F2} vy={p.VY:F2} ay={p.AY} px={p.S.Pixel}");
    }
}
