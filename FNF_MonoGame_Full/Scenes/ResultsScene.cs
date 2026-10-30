using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json.Linq;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Scoring rank enum matching original FNF ScoringRank from Scoring.hx.
///   PERFECT_GOLD = all sicks, PERFECT = completion == 1.0, EXCELLENT >= 0.90, GREAT >= 0.80, GOOD >= 0.60, SHIT below.
/// </summary>
public enum ScoringRank
{
    PERFECT_GOLD,
    PERFECT,
    EXCELLENT,
    GREAT,
    GOOD,
    SHIT
}

/// <summary>
/// Results screen: port of V-Slice 0.8.1 funkin.play.ResultState (+ ResultScore/ScoreNum, TallyCounter,
/// ClearPercentCounter, BitmapUtil.createResultsBar, FlxBackdrop, the player data "results" animations of
/// data/players/{bf,pico}.json and the results scripts). Desktop layout (gameNotchSize = gameCutoutSize = 0).
/// Times are seconds since the state was created; every number below comes from the original source.
/// </summary>
public partial class ResultsScene : Scene
{
    // ===================================================================== params
    private readonly int _score, _maxCombo, _misses;
    private readonly string _songName, _difficultyId;
    private readonly bool _isNewHighscore, _isStoryMode;
    private readonly int _tallySick, _tallyGood, _tallyBad, _tallyShit, _tallyMissed, _totalNotesHit, _totalNotes;

    /// <summary>Chart player character id (currentChart.characters.player), e.g. "bf", "pico-playable".</summary>
    public string CharacterId { get; set; }
    /// <summary>Dev hook alias of <see cref="CharacterId"/>.</summary>
    public string DevCharacterId { get => CharacterId; set { if (value != null) CharacterId = value; } }
    /// <summary>"Song by Artist" (or the campaign title in story mode).</summary>
    public string Title { get; set; }
    public bool IsPracticeMode { get; set; }
    public bool IsBotPlayMode { get; set; }
    /// <summary>ResultsParams.prevScoreData: rank saved BEFORE this play (PlayScene saves the score before the results,
    /// so reading the save here returned the new rank and the Freeplay rank reveal never played).</summary>
    public string PrevRankString; public bool HasPrevRank;
    public float DevElapsed => _t;
    public string DevInfo => $"flash={(_bgFlashVisible ? _bgFlashAlpha : -1f):F3} vertY={_rankVertY:F2} rowX0={_rankRowX[0]:F2} " + string.Join(" ", _chars.Where(c => c.atlas != null).Select(c => $"bounds=({c.atlas.A.BoundsMin.X:F3},{c.atlas.A.BoundsMin.Y:F3},{c.atlas.A.FrameW},{c.atlas.A.FrameH}) vis={c.atlas.Visible} anim={c.atlas.Name} f={c.atlas.Index}/{c.atlas.Frames?.Length} tl={c.atlas.TimelineFrame}/{c.atlas.A.FrameCount} fin={c.atlas.Finished} loop={c.atlas.Looped} paused={c.atlas.Paused}"));
    private float _devLogT = -1f;
    private static string DevSpr(Spr x) => x == null ? "null" : $"{(x.Visible ? x.Cur ?? "-" : "hid")}:{x.Frame}";
    public string DevDump => System.FormattableString.Invariant($"t={_t:F3} flash={(_bgFlashVisible ? _bgFlashAlpha : -1f):F3} top={_topBarY:F1} row0={_rankRowX[0]:F3} row1={_rankRowX[1]:F3} vert={(_rankTextShown ? (_rankVertVisible ? 1 : 0) : -1)}:{_rankVertY:F2} song={_songX:F1},{_songY:F2} diff={_diffX:F1},{_diffY:F2} cps={_cpSmallCounter?.X:F1},{_cpSmallCounter?.Y:F2},{_cpSmallCounter?.Visible} cpb={(_cpBig == null ? "null" : $"{_cpBig.Cur}:{_cpBig.Alpha:F3}:{_cpBig.Removed}")} ss={DevSpr(_soundSystem)} res={DevSpr(_resultsAnim)} rat={DevSpr(_ratingsPopin)} sc={DevSpr(_scorePopin)} hs={DevSpr(_highscoreNew)} digits={string.Join(",", _scoreNums.Select(n => DevSpr(n.S)))} tallies={string.Join(",", _tallies.Select(tl => tl.Visible ? ((int)RoundCompat.AwayFromZero(tl.Cur)).ToString() : "h"))} chars={string.Join(";", _chars.Select(c => c.atlas != null ? $"{(c.atlas.Visible ? c.atlas.Name : "hid")}:{c.atlas.Index}" : DevSpr(c.spr)))}");


    public ResultsScene(int score, int maxCombo, int misses, string songName = "")
        : this(score, maxCombo, misses, songName, "normal", false, 0, 0, 0, 0, misses, 0, 0) { }

    public ResultsScene(int score, int maxCombo, int misses, string songName,
        int tallySick, int tallyGood, int tallyBad, int tallyShit, int tallyMissed, int totalNotesHit, int totalNotes)
        : this(score, maxCombo, misses, songName, "normal", false, tallySick, tallyGood, tallyBad, tallyShit, tallyMissed, totalNotesHit, totalNotes) { }

    public ResultsScene(int score, int maxCombo, int misses, string songName, string difficultyId, bool isNewHighscore,
        int tallySick, int tallyGood, int tallyBad, int tallyShit, int tallyMissed, int totalNotesHit, int totalNotes,
        bool isStoryMode = false)
    {
        _score = score; _maxCombo = maxCombo; _misses = misses; _songName = songName ?? "";
        _difficultyId = difficultyId ?? "normal"; _isNewHighscore = isNewHighscore;
        _tallySick = tallySick; _tallyGood = tallyGood; _tallyBad = tallyBad; _tallyShit = tallyShit;
        _tallyMissed = tallyMissed; _totalNotesHit = totalNotesHit; _totalNotes = totalNotes; _isStoryMode = isStoryMode;
    }

    // ===================================================================== timing helpers (FlxTimer / FlxTween)
    private float _t;
    // FlxTimer port: |time| (FlxTimer.start uses Math.abs), counter += elapsed from the frame AFTER creation (timers created
    // in create()/callbacks are first updated by the next FlxTimerManager.update), loops, one onLoopFinished per frame,
    // callbacks after the whole list was updated, in creation order.
    private sealed class FTimer { public float Time, Counter; public int Loops, LoopsCounter, Frame; public bool Finished; public Action<FTimer> Cb; public int LoopsLeft => Loops - LoopsCounter; }
    private readonly List<FTimer> _timers = new();
    private int _frameNo; private float _dt; private bool _inTimers;
    private FTimer StartTimer(float time, Action<FTimer> cb, int loops = 1)
    {
        var tm = new FTimer { Time = Math.Abs(time), Loops = Math.Abs(loops), Cb = cb, Frame = _frameNo };
        _timers.Add(tm);
        return tm;
    }
    private void After(float delay, Action a) => StartTimer(delay, _ => a());

    private sealed class Tween
    {
        public float Start, Dur; public Func<float, float> Ease; public Action<float> Apply; public Action Done; public bool Dead;
    }
    private readonly List<Tween> _tweens = new();
    private Tween TweenF(float dur, Func<float, float> ease, Action<float> apply, float delay = 0f, Action done = null)
    {
        var tw = new Tween { Start = _t + delay - (_inTimers ? _dt : 0f), Dur = dur, Ease = ease ?? Linear, Apply = apply, Done = done };
        _tweens.Add(tw);
        return tw;
    }
    private static float Linear(float x) => x;
    private static float QuartOut(float x) => 1f - MathF.Pow(1f - x, 4f);
    private static float ExpoOut(float x) => x >= 1f ? 1f : 1f - MathF.Pow(2f, -10f * x);
    private static float QuadOut(float x) => -x * (x - 2f);
    private static float QuadIn(float x) => x * x;

    // ===================================================================== sparrow sprite (FlxSprite + FlxAnimation)
    private sealed class Spr
    {
        public SpriteSheet Sheet;
        public readonly Dictionary<string, List<SpriteFrame>> Anims = new();
        public string Cur; public List<SpriteFrame> Frames; public int Frame; public float Timer; public bool Looped, Finished = true;
        public float Fps = 24f;
        public Vector2 Pos; public bool Visible = true; public Color Color = Color.White; public float Alpha = 1f;
        public Action<string> OnFinish;
        public bool White; // PureColor flash shader

        public void AddByPrefix(string name, string prefix)
        {
            if (Sheet == null) return;
            var list = Sheet.Frames.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(kv => kv.Key.Substring(prefix.Length).Length).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => kv.Value).ToList();
            Anims[name] = list;
        }
        public void Play(string name, bool force = false, int frame = 0)
        {
            if (!Anims.TryGetValue(name, out var f) || f.Count == 0) return;
            if (!force && Cur == name && !Finished) return;
            Cur = name; Frames = f; Timer = 0f; Finished = false;
            Frame = Math.Min(frame, f.Count - 1);
        }
        public void Update(float dt)
        {
            if (Frames == null || Finished) return;
            float dur = 1f / Fps;
            Timer += dt;
            while (Timer > dur && !Finished)
            {
                Timer -= dur;
                if (Looped && Frame == Frames.Count - 1) Frame = 0;
                else if (Frame + 1 > Frames.Count - 1 && !Looped)
                {
                    Finished = true;
                    var cur = Cur; var fl = Frames;
                    OnFinish?.Invoke(cur);
                    if (Frames != fl || !Finished) break; // replayed by the callback
                }
                else Frame++;
            }
        }
        public SpriteFrame CurFrame => Frames != null && Frames.Count > 0 ? Frames[Math.Clamp(Frame, 0, Frames.Count - 1)] : null;
    }

    // ===================================================================== flixel-animate replay (texture atlases)
    private sealed class AtlasAnim
    {
        private struct Part { public SpriteFrame F; public Matrix M; public Vector4 Mul; public int Blend; }
        private readonly List<Part>[] _ticks;
        private readonly Texture2D _tex;
        public readonly Vector2 BoundsMin; public readonly int FrameW, FrameH;
        public int FrameCount => _ticks.Length;
        public float Fps = 24f;
        public readonly Dictionary<string, (int start, int len)> Labels = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (float scale, float offset)> _scaleEls;

        private static readonly BlendState ScreenBlend = new()
        {
            ColorSourceBlend = Blend.One, ColorDestinationBlend = Blend.InverseSourceColor,
            AlphaSourceBlend = Blend.One, AlphaDestinationBlend = Blend.InverseSourceAlpha
        };

        public AtlasAnim(SpriteSheet sheet, string animationJsonPath, Dictionary<string, (float, float)> scaleEls = null)
        {
            _scaleEls = scaleEls;
            _tex = sheet.Texture;
            var root = JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(animationJsonPath));
            Fps = (float?)root["MD"]?["FRT"] ?? 24f;
            var syms = new Dictionary<string, JToken>(StringComparer.Ordinal);
            foreach (var s in root["SD"]?["S"] ?? new JArray()) syms[(string)s["SN"]] = s;
            JToken main = root["AN"]["TL"];
            int n = Duration(main);
            foreach (var l in main["L"])
                foreach (var f in l["FR"])
                    if (f["N"] is JToken nm && !string.IsNullOrEmpty((string)nm) && !Labels.ContainsKey((string)nm))
                        Labels[(string)nm] = ((int)f["I"], (int)f["DU"]);

            // Timeline._bounds = getWholeBounds() at load (before any scaleElement by scripts)
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int t = 0; t < n; t++)
                foreach (var p in Flatten(main, syms, sheet, t, false))
                {
                    float w = p.F.SourceRect.Width, h = p.F.SourceRect.Height;  // p.M already maps the stored (rotated) atlas rect
                    foreach (var v in new[] { Vector2.Zero, new Vector2(w, 0), new Vector2(0, h), new Vector2(w, h) })
                    {
                        var q = Vector2.Transform(v, p.M);
                        minX = Math.Min(minX, q.X); minY = Math.Min(minY, q.Y); maxX = Math.Max(maxX, q.X); maxY = Math.Max(maxY, q.Y);
                    }
                }
            if (minX == float.MaxValue) { minX = minY = 0; maxX = maxY = 1; }
            // prefer the 1:1 port of flixel-animate Timeline.getWholeBounds (AABB-of-AABB + MovieClip filter extents)
            if (sheet.WholeMainTimelineBounds is { } wb) { minX = wb.X; minY = wb.Y; maxX = wb.X + wb.W; maxY = wb.Y + wb.H; }
            BoundsMin = new Vector2(minX, minY);
            FrameW = (int)(maxX - minX); FrameH = (int)(maxY - minY);

            _ticks = new List<Part>[n];
            for (int t = 0; t < n; t++) _ticks[t] = Flatten(main, syms, sheet, t, true);
        }

        private static int Duration(JToken tl)
        {
            int d = 0;
            foreach (var l in tl["L"]) foreach (var f in l["FR"]) d = Math.Max(d, (int)f["I"] + (int)f["DU"]);
            return d;
        }
        private static JToken FrameAt(JToken layer, int t)
        {
            foreach (var f in layer["FR"]) { int i = (int)f["I"]; if (t >= i && t < i + (int)f["DU"]) return f; }
            return null;
        }
        private static Matrix ReadMx(JToken inst)
        {
            var m = inst["MX"];
            if (m != null) return new Matrix((float)m[0], (float)m[1], 0, 0, (float)m[2], (float)m[3], 0, 0, 0, 0, 1, 0, (float)m[4], (float)m[5], 0, 1);
            var m3 = inst["M3D"];
            if (m3 is JArray a3 && a3.Count >= 14)
                return new Matrix((float)a3[0], (float)a3[1], 0, 0, (float)a3[4], (float)a3[5], 0, 0, 0, 0, 1, 0, (float)a3[12], (float)a3[13], 0, 1);
            if (m3 is JObject o3)
                return new Matrix((float)o3["m00"], (float)o3["m01"], 0, 0, (float)o3["m10"], (float)o3["m11"], 0, 0, 0, 0, 1, 0, (float)o3["m30"], (float)o3["m31"], 0, 1);
            return Matrix.Identity;
        }

        private List<Part> Flatten(JToken main, Dictionary<string, JToken> syms, SpriteSheet sheet, int tick, bool scaled)
        {
            var outp = new List<Part>();
            Walk(main, tick, Matrix.Identity, Vector4.One, 10, null);
            return outp;

            void Walk(JToken tl, int t, Matrix parent, Vector4 mul, int blend, string symName)
            {
                var layers = (JArray)tl["L"];
                for (int li = layers.Count - 1; li >= 0; li--)
                {
                    var fr = FrameAt(layers[li], t);
                    if (fr == null) continue;
                    int fi = (int)fr["I"];
                    var els = (JArray)fr["E"];
                    for (int ei = 0; ei < els.Count; ei++)
                    {
                        // FunkinSprite.scaleElement on getFirstElement(symbol): a += s; d += s; tx -= offset (TRP ignored: 0 here)
                        (float s, float o) se = default;
                        bool scaleThis = scaled && symName != null && _scaleEls != null && _scaleEls.TryGetValue(symName, out se) && li == 0 && fi == 0 && ei == 0;
                        var e = els[ei];
                        Matrix local;
                        if (e["ASI"] is JToken asi)
                        {
                            if (!sheet.Frames.TryGetValue((string)asi["N"], out var f)) continue;
                            local = ReadMx(asi);
                            if (scaleThis) { local.M11 += se.s; local.M22 += se.s; local.M41 -= se.o; }
                            if (f.Rotated) local = new Matrix(0, -1, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, f.SourceRect.Width, 0, 1) * local;
                            outp.Add(new Part { F = f, M = local * parent, Mul = mul, Blend = blend });
                        }
                        else if (e["SI"] is JToken si && syms.TryGetValue((string)si["SN"], out var sym))
                        {
                            JToken stl = sym["TL"];
                            int dur = Math.Max(1, Duration(stl));
                            int ff = (int?)si["FF"] ?? 0;
                            int ft = ff + (t - fi);
                            string st = (string)si["ST"] ?? "G";
                            string lp = (string)si["LP"] ?? "LP";
                            if (st == "MC" || st == "movieclip") ft = 0;
                            else if (lp == "PO" || lp == "playonce") ft = Math.Min(ft, dur - 1);
                            else if (lp == "SF" || lp == "singleframe") ft = ff;
                            else ft = ((ft % dur) + dur) % dur;
                            var m2 = mul;
                            if (si["C"] is JToken c)
                                m2 *= new Vector4((float?)c["RM"] ?? 1f, (float?)c["GM"] ?? 1f, (float?)c["BM"] ?? 1f, (float?)c["AM"] ?? 1f);
                            int b = (int?)si["B"] ?? 10;
                            int nb = b == 10 ? blend : b;
                            local = ReadMx(si);
                            if (scaleThis) { local.M11 += se.s; local.M22 += se.s; local.M41 -= se.o; }
                            Walk(stl, ft, local * parent, m2, nb, (string)si["SN"]);
                        }
                    }
                }
            }
        }

        /// <summary>FlxAnimate.drawAnimate: translate(-bounds.min), translate(-origin), scale, translate(pos + origin).
        /// Call with NO active SpriteBatch.</summary>
        private static readonly BlendState AddPremul = new()
        {
            ColorSourceBlend = Blend.One, ColorDestinationBlend = Blend.One, AlphaSourceBlend = Blend.Zero, AlphaDestinationBlend = Blend.One
        };

        public void Draw(SpriteBatch sb, int frame, Vector2 pos, float scale, Func<Texture2D, Texture2D> premul, float alpha = 1f)
        {
            var origin = new Vector2((int)FrameW * 0.5f, (int)FrameH * 0.5f);
            var place = Matrix.CreateTranslation(-BoundsMin.X - origin.X, -BoundsMin.Y - origin.Y, 0f)
                      * Matrix.CreateScale(scale, scale, 1f)
                      * Matrix.CreateTranslation(pos.X + origin.X, pos.Y + origin.Y, 0f);
            foreach (var p in _ticks[Math.Clamp(frame, 0, _ticks.Length - 1)])
            {
                float a = p.Mul.W * alpha;
                if (a <= 0f) continue;
                // premultiplied texture (OpenFL bitmaps are premultiplied; avoids dark fringes on filtered edges)
                BlendState bs = p.Blend == 0 ? AddPremul : p.Blend == 12 ? ScreenBlend : BlendState.AlphaBlend;
                Color tint = new Color(Math.Min(1f, p.Mul.X) * a, Math.Min(1f, p.Mul.Y) * a, Math.Min(1f, p.Mul.Z) * a, a);
                sb.Begin(SpriteSortMode.Immediate, bs, SamplerState.LinearClamp, null, RasterizerState.CullNone, null, p.M * place);
                sb.Draw(premul(p.F.Texture ?? _tex), Vector2.Zero, p.F.SourceRect, tint);
                sb.End();
            }
        }
    }

    /// <summary>FunkinSprite (texture atlas) + FlxAnimateController state.</summary>
    private sealed class AtlasSprite
    {
        public AtlasAnim A; public Vector2 Pos; public float Scale = 1f; public bool Visible; public int Z;
        public int[] Frames; public int Index; public float Timer; public bool Looped, Finished = true, Paused;
        public string Name;
        public Action<string> OnFinish; public Action<string, int> OnFrameChange;
        public void Play(string label, bool force = true, int startFrame = 0)
        {
            int[] fr;
            if (string.IsNullOrEmpty(label)) { fr = Enumerable.Range(0, A.FrameCount).ToArray(); label = ""; }
            else if (A.Labels.TryGetValue(label, out var r)) fr = Enumerable.Range(r.start, r.len).ToArray();
            else return; // "Animation does not exist!"
            if (!force && Name == label && !Finished) { Paused = false; return; }
            Name = label; Frames = fr; Index = Math.Min(startFrame, fr.Length - 1); Timer = 0; Finished = false; Paused = false; Looped = false;
            OnFrameChange?.Invoke(Name, Index);
        }
        public void Update(float dt)
        {
            if (Frames == null || Finished || Paused) return;
            float dur = 1f / A.Fps;
            Timer += dt;
            while (Timer > dur && !Finished)
            {
                Timer -= dur;
                if (Looped && Index == Frames.Length - 1) { Index = 0; OnFrameChange?.Invoke(Name, Index); }
                else if (Index + 1 > Frames.Length - 1 && !Looped)
                {
                    Finished = true;
                    var fr = Frames;
                    OnFinish?.Invoke(Name);
                    if (Frames != fr || !Finished) break;
                }
                else { Index++; OnFrameChange?.Invoke(Name, Index); }
            }
        }
        public int TimelineFrame => Frames == null ? 0 : Frames[Math.Clamp(Index, 0, Frames.Length - 1)];
    }

    // ===================================================================== state
    private ScoringRank _rank;
    private int _clearPercentTarget = 100, _clearPercentLerp;
    private Texture2D _bgTex, _bgFlashTex, _topBarTex, _diffTex, _songFontTex, _rankHorTex, _rankVerTex;
    private float _bgFlashAlpha = 1f; private bool _bgFlashVisible;
    private float _topBarY;
    private int _topBarH;
    private Spr _soundSystem, _resultsAnim, _ratingsPopin, _scorePopin, _highscoreNew;
    private SpriteSheet _tallySheet, _digitSheet, _cpLeft, _cpRight, _cpSmall;
    private readonly Dictionary<Texture2D, Texture2D> _whiteTex = new();
    private Texture2D _cpText, _cpTextSmall;

    // characters (zIndex sorted at draw)
    private readonly List<(int z, int order, AtlasSprite atlas, Spr spr, float scale)> _chars = new();
    private readonly List<(AtlasSprite s, float delay, string startLabel, string sound)> _charAtlasQueue = new();
    private readonly List<(Spr s, float delay)> _charSparrowQueue = new();

    // tallies
    private sealed class Tally { public float X, Y; public float Cur; public int Need; public Color Flavour; public bool Visible; public readonly List<Spr> Digits = new(); }
    private readonly List<Tally> _tallies = new();

    // ResultScore
    private sealed class ScoreNum
    {
        public Spr S; public int Digit = 10, FinalDigit = 10; public bool Glow = true; public float FinalDelay;
    }
    private readonly ScoreNum[] _scoreNums = new ScoreNum[10];
    private int _scoreStart;
    private bool _scoreVisible;
    private static readonly string[] NumToString = { "ZERO", "ONE", "TWO", "THREE", "FOUR", "FIVE", "SIX", "SEVEN", "EIGHT", "NINE", "DISABLED" };

    // ClearPercentCounter
    private sealed class ClearCounter
    {
        public float X, Y; public int Cur; public bool Small; public bool Changed = true; public bool Flash; public float Alpha = 1f;
        public bool Visible = true; public bool Removed; public float LastX, LastY;
        public readonly List<(Spr s, bool variant)> Digits = new(); // members[1..]
    }
    private ClearCounter _cpBig, _cpSmallCounter;
    private bool _cpSmallAdded;

    // rank text
    private bool _rankTextShown;
    private float _rankVertX, _rankVertY, _rankVertVy; private bool _rankVertVisible = true;
    private readonly float[] _rankRowX = new float[12];
    private readonly float[] _rankRowVx = new float[12];

    // song name / difficulty / small clear percent
    private float _diffX, _diffY, _songX, _songY;
    private float _speedX, _speedY;
    private bool _moving;
    private const float SONG_ANGLE = -4.4f;
    private readonly Dictionary<char, Rectangle> _songGlyphs = new();
    private int _songW;

    // music / exit
    private bool _busy; private float _rankBgAlpha;
    private float _musicVol = 1f; private bool _musicFading; private float _musicFadeT;

    private string _playerId = "bf";
    private JObject _playerData;

    // ===================================================================== create()
    public override void Load()
    {
        FNF_MonoGame.Engine.InputManager.DevSceneClock = () => _t;
        _rank = CalculateRank();
        // NOTE: Only divide if totalNotes > 0. Scoring.tallyCompletion = clamp((sick + good - missed) / totalNotes, 0, 1)
        float clearF = _totalNotes == 0 ? 0f : Math.Clamp((_tallySick + _tallyGood - _tallyMissed) / (float)_totalNotes, 0f, 1f) * 100f;
        _clearPercentTarget = (int)Math.Floor(clearF);
        Game.Discord?.SetResultsState(_songName, _difficultyId, _score, _clearPercentTarget, _isStoryMode, null);
        Audio.StopMusic();

        _bgTex = MakeGradient(new Color(0xFE, 0xCC, 0x5C), new Color(0xFD, 0xC0, 0x5C));
        _bgFlashTex = MakeGradient(new Color(0xFF, 0xF1, 0xA6), new Color(0xFF, 0xF1, 0xBE));
        _topBarTex = MakeResultsBar(out _topBarH);
        _topBarY = -_topBarH;

        // soundSystem (-15, -180), visible + play at 8/24
        _soundSystem = LoadSpr("resultScreen/soundSystem", -15, -180, ("idle", "sound system"));
        _soundSystem.Visible = false;
        After(8f / 24f, () => { _soundSystem.Play("idle"); _soundSystem.Visible = true; });

        LoadPlayerAndCharacters();

        _diffTex = Assets.LoadTexture($"resultScreen/diff_{_difficultyId}");
        if (_diffTex == Assets.Pixel) _diffTex = Assets.LoadTexture("resultScreen/diff_normal");
        BuildSongFont();

        // blackTopBar: y -h -> 0, 7/24 quartOut, startDelay 3/24
        TweenF(7f / 24f, QuartOut, k => _topBarY = -_topBarH + _topBarH * k, 3f / 24f);

        _cpSmallCounter = new ClearCounter { X = 1280f / 2f + 300f, Y = 720f / 2f - 100f, Cur = 100, Small = true, Visible = false };
        _cpSmallCounter.Y += _topBarH - 148;

        // speedOfTween = (-cos(angle), -sin(angle)); timerThenSongName(1.0, false)
        float ar = SONG_ANGLE * MathF.PI / 180f;
        _speedX = -1f * MathF.Cos(ar); _speedY = -1f * MathF.Sin(ar);
        TimerThenSongName(1.0f, false);

        _resultsAnim = LoadSpr("resultScreen/results", 1280 - 1480, -10, ("result", "results instance 1"));
        _resultsAnim.Visible = false;
        After(6f / 24f, () => { _resultsAnim.Visible = true; _resultsAnim.Play("result"); });

        _ratingsPopin = LoadSpr("resultScreen/ratingsPopin", -135, 135, ("idle", "Categories"));
        _ratingsPopin.Visible = false;
        After(21f / 24f, () => { _ratingsPopin.Visible = true; _ratingsPopin.Play("idle"); });

        _scorePopin = LoadSpr("resultScreen/scorePopin", -180, 515, ("score", "tally score"));
        _scorePopin.Visible = false;
        After(36f / 24f, () => { _scorePopin.Visible = true; _scorePopin.Play("score"); });

        After(37f / 24f, () => { _scoreVisible = true; AnimateNumbers(); StartRankTallySequence(); });
        After(BFDelay(), AfterRankTallySequence);
        After(FlashDelay(), DisplayRankText);

        _highscoreNew = LoadSpr("resultScreen/highscoreNew", 44, 557, ("new", "highscoreAnim0"));
        _highscoreNew.Visible = false;
        _highscoreNew.OnFinish = _ => _highscoreNew.Play("new", true, 16);
        After(HighscoreDelay(), () => { if (_isNewHighscore) { _highscoreNew.Visible = true; _highscoreNew.Play("new"); } else _highscoreNew.Visible = false; });

        // TallyCounters (hStuf = 50; then 54 for the judgements; extraYOffset 7)
        _tallySheet = SpriteSheet.Load(Game, "resultScreen/tallieNumber");
        int hStuf = 50;
        var totalHit = new Tally { X = 375, Y = hStuf * 3, Need = _totalNotesHit, Flavour = Color.White };
        var maxCombo = new Tally { X = 375, Y = hStuf * 4, Need = _maxCombo, Flavour = Color.White };
        if (_totalNotesHit >= 1000) { totalHit.X -= 30; maxCombo.X -= 30; }
        hStuf += 4; const float ex = 7;
        _tallies.Add(totalHit); _tallies.Add(maxCombo);
        _tallies.Add(new Tally { X = 230, Y = hStuf * 5 + ex, Need = _tallySick, Flavour = new Color(0x89, 0xE5, 0x9E) });
        _tallies.Add(new Tally { X = 210, Y = hStuf * 6 + ex, Need = _tallyGood, Flavour = new Color(0x89, 0xC9, 0xE5) });
        _tallies.Add(new Tally { X = 190, Y = hStuf * 7 + ex, Need = _tallyBad, Flavour = new Color(0xE6, 0xCF, 0x8A) });
        _tallies.Add(new Tally { X = 220, Y = hStuf * 8 + ex, Need = _tallyShit, Flavour = new Color(0xE6, 0x8C, 0x8A) });
        _tallies.Add(new Tally { X = 260, Y = hStuf * 9 + ex, Need = _tallyMissed, Flavour = new Color(0xC6, 0x8A, 0xE6) });
        for (int i = 0; i < _tallies.Count; i++)
        {
            var tl = _tallies[i];
            if (tl.Cur == tl.Need) DrawTallyNumbers(tl); // constructor: if (curNumber == neededNumber) drawNumbers()
            After(0.3f * i + 1.20f, () => { tl.Visible = true; float from = tl.Cur; TweenF(0.5f, QuartOut, k => tl.Cur = from + (tl.Need - from) * k); });
        }

        // ResultScore(35, 305, 10, score): ScoreNum(x + 65 i, y) added to a FlxSpriteGroup at (35, 305) -> (70 + 65 i, 610)
        _digitSheet = SpriteSheet.Load(Game, "resultScreen/score-digital-numbers");
        for (int i = 0; i < 10; i++)
        {
            var s = new Spr { Sheet = _digitSheet, Pos = new Vector2(35 + 65 * i + 35, 305 + 305) };
            for (int d = 0; d < 10; d++) s.AddByPrefix(NumToString[d], NumToString[d] + " DIGITAL");
            s.AddByPrefix("DISABLED", "DISABLED"); s.AddByPrefix("GONE", "GONE");
            _scoreNums[i] = new ScoreNum { S = s };
            s.Play("DISABLED", true);
        }
        SetScoreShit(_score);

        // music at rank.getMusicDelay()
        After(MusicDelay(), StartMusic);

        _cpLeft = SpriteSheet.Load(Game, "resultScreen/clearPercent/clearPercentNumberLeft");
        _cpRight = SpriteSheet.Load(Game, "resultScreen/clearPercent/clearPercentNumberRight");
        _cpSmall = SpriteSheet.Load(Game, "resultScreen/clearPercent/clearPercentNumberSmall");
        _cpText = Assets.LoadTexture("resultScreen/clearPercent/clearPercentText");
        _cpTextSmall = Assets.LoadTexture("resultScreen/clearPercent/clearPercentTextSmall");
        _rankHorTex = Assets.LoadTexture(HorTextAsset());
        _rankVerTex = Assets.LoadTexture(VerTextAsset());

        Audio.PreloadSound("scrollMenu");
        Audio.PreloadSound("confirmMenu");
    }

    private Spr LoadSpr(string path, float x, float y, params (string name, string prefix)[] anims)
    {
        var s = new Spr { Sheet = SpriteSheet.Load(Game, path), Pos = new Vector2(x, y) };
        foreach (var a in anims) s.AddByPrefix(a.name, a.prefix);
        return s;
    }

    // ===================================================================== player data / character animations
    private void LoadPlayerAndCharacters()
    {
        string charId = CharacterId;
        if (string.IsNullOrEmpty(charId))
            charId = (HighscoreManager.Data.SelectedCharacter ?? "bf") == "pico" ? "pico-playable" : "bf";
        // PlayerRegistry.getCharacterOwnerId(characterId) ?? 'bf'
        _playerId = "bf";
        foreach (var p in new[] { "bf", "pico" })
        {
            var jo = LoadJson($"data/players/{p}.json");
            if (jo?["ownedChars"] is JArray owned && owned.Any(o => (string)o == charId)) { _playerId = p; break; }
        }
        _playerData = LoadJson($"data/players/{_playerId}.json");

        string key = _rank switch
        {
            ScoringRank.PERFECT_GOLD => "perfectGold", ScoringRank.PERFECT => "perfect", ScoringRank.EXCELLENT => "excellent",
            ScoringRank.GREAT => "great", ScoringRank.GOOD => "good", _ => "loss"
        };
        var list = _playerData?["results"]?[key] as JArray;
        if (list == null) return;
        bool naughty = true; // Preferences.naughtyness default
        int order = 0;
        foreach (var ad in list)
        {
            string filter = (string)ad["filter"] ?? "both";
            if (filter != "both" && (naughty && filter != "naughty" || !naughty && filter != "safe")) continue;
            string assetPath = (string)ad["assetPath"];
            string script = (string)ad["scriptClass"];
            var offs = ad["offsets"] as JArray;
            float ox = offs != null ? (float)offs[0] : 0f, oy = offs != null ? (float)offs[1] : 0f;
            int z = (int?)ad["zIndex"] ?? 500;
            float scale = (float?)ad["scale"] ?? 1f;
            float delay = (float?)ad["delay"] ?? 0f;
            string path = assetPath != null ? StripLibrary(assetPath) : ScriptAtlasPath(script);
            switch ((string)ad["renderType"])
            {
                case "animateatlas":
                {
                    if (path == null) continue;
                    var anim = LoadAtlas(path, script == "PicoGoodResults"
                        ? new Dictionary<string, (float, float)> { ["white small"] = (10f, 500f), ["blacksmall"] = (15f, 500f) } : null);
                    if (anim == null) continue;
                    var spr = new AtlasSprite { A = anim, Pos = new Vector2(ox, oy), Scale = scale, Z = z, Visible = false };
                    bool looped = (bool?)ad["looped"] ?? true;
                    string loopLabel = (string)ad["loopFrameLabel"];
                    int? loopFrame = (int?)ad["loopFrame"];
                    if (!looped) spr.OnFinish = _ => spr.Paused = true;
                    else if (loopLabel != null) spr.OnFinish = _ => { spr.Play(loopLabel, true); spr.Looped = true; };
                    else if (loopFrame != null) spr.OnFinish = _ => spr.Play("", true, loopFrame.Value);
                    if (script == "PicoGoodResults") HookPicoGood(spr);
                    _chars.Add((z, order++, spr, null, scale));
                    _charAtlasQueue.Add((spr, delay, (string)ad["startFrameLabel"] ?? "", (string)ad["sound"] ?? ""));
                    break;
                }
                case "sparrow":
                {
                    if (path == null) continue;
                    var s = new Spr { Sheet = SpriteSheet.Load(Game, path), Pos = new Vector2(ox, oy), Visible = false };
                    s.AddByPrefix("idle", "");
                    int? loopFrame = (int?)ad["loopFrame"];
                    if (loopFrame != null) s.OnFinish = _ => s.Play("idle", true, loopFrame.Value);
                    _chars.Add((z, order++, null, s, 1f));
                    _charSparrowQueue.Add((s, delay));
                    break;
                }
            }
        }
    }

    /// <summary>PicoGoodResults.hxc: when the default symbol plays, override with "intro" (5 % "intro fat gf",
    /// else 20 % "intro cass"); every other finished anim loops "loop".</summary>
    private void HookPicoGood(AtlasSprite spr)
    {
        var rnd = new Random();
        spr.OnFrameChange = (name, frame) =>
        {
            if (name == "" && spr.Visible)
            {
                if (rnd.NextDouble() * 100 < 5) spr.Play("intro fat gf", true, 0);
                else if (rnd.NextDouble() * 100 < 20) spr.Play("intro cass", true, 0);
                else spr.Play("intro", true, 0);
            }
        };
        spr.OnFinish = name => { if (name == "") return; spr.Play("loop", true); spr.Looped = true; };
    }

    private static string ScriptAtlasPath(string script) => script switch
    {
        "BFBedPerfectResults" => "resultScreen/results-bf/resultsPERFECT/bed",
        "BFShitResults" => "resultScreen/results-bf/resultsSHIT",
        "PicoGoodResults" => "resultScreen/results-pico/resultsGOOD",
        "PicoGreatResults" => "resultScreen/results-pico/resultsGREAT",
        "PicoPerfectResults" => "resultScreen/results-pico/resultsPERFECT",
        _ => null
    };

    private static string StripLibrary(string p) { int i = p.IndexOf(':'); return i >= 0 ? p.Substring(i + 1) : p; }

    private AtlasAnim LoadAtlas(string path, Dictionary<string, (float, float)> scaleEls)
    {
        try
        {
            string dir = Assets.ResolveDirectory(path);
            if (dir == null) return null;
            string json = Path.Combine(dir, "Animation.json");
            if (!global::FNF_MonoGame.Engine.FsCI.FileExists(json)) return null;
            var sheet = SpriteSheet.Load(Game, path);
            if (sheet?.Texture == null) return null;
            return new AtlasAnim(sheet, json, scaleEls);
        }
        catch (Exception ex) { Console.WriteLine($"[RESULTS] atlas {path}: {ex.Message}"); return null; }
    }

    private JObject LoadJson(string rel)
    {
        try { string p = Assets.ResolvePath(rel); return p != null ? JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(p)) : null; }
        catch { return null; }
    }

    // ===================================================================== sequences
    private void StartRankTallySequence()
    {
        _bgFlashVisible = true;
        float a0 = _bgFlashAlpha;
        TweenF(5f / 24f, Linear, k => _bgFlashAlpha = a0 * (1f - k));
        _clearPercentLerp = Math.Max(0, _clearPercentTarget - 36);
        _cpBig = new ClearCounter { X = 1280f / 2f + 190f, Y = 720f / 2f - 70f, Cur = _clearPercentLerp };
        int from = _clearPercentLerp;
        var big = _cpBig;
        TweenF(58f / 24f, QuartOut, k =>
        {
            // FlxTween writes a Float into the Int property (truncated), onUpdate rounds it
            big.Cur = (int)(from + (_clearPercentTarget - from) * k); big.Changed = true;
            if (_clearPercentLerp != big.Cur) { _clearPercentLerp = big.Cur; Audio.PlaySound("scrollMenu"); }
        }, 0f, () =>
        {
            Audio.PlaySound("confirmMenu");
            big.Cur = _clearPercentTarget; big.Changed = true;
            big.Flash = true;
            After(0.4f, () => big.Flash = false);
            After(0.25f, () => TweenF(0.5f, QuartOut, k => big.Alpha = 1f - k, 0.5f, () => big.Removed = true));
        });
    }

    private void DisplayRankText()
    {
        _bgFlashVisible = true;
        _bgFlashAlpha = 1f;
        TweenF(14f / 24f, Linear, k => _bgFlashAlpha = 1f - k);
        _rankTextShown = true;
        _rankVertX = 1280 - 44; _rankVertY = 100; _rankVertVy = 0;
        // FlxFlicker.flicker(rankTextVert, 2/24*3, 2/24, true): toggles every 2/24, ends visible
        _rankVertVisible = true;
        StartTimer(2f / 24f, tm => { _rankVertVisible = !_rankVertVisible; if (tm.Loops > 0 && tm.LoopsLeft == 0) _rankVertVisible = true; }, 3);
        After(30f / 24f, () => _rankVertVy = -80f);
        for (int i = 0; i < 12; i++) { _rankRowX[i] = 1280f / 2f - 320f; _rankRowVx[i] = i % 2 == 0 ? -7f : 7f; }
    }

    private void AfterRankTallySequence()
    {
        // showSmallClearPercent
        _cpSmallAdded = true;
        _cpSmallCounter.Visible = true;
        _cpSmallCounter.Flash = true;
        After(0.4f, () => _cpSmallCounter.Flash = false);
        _cpSmallCounter.Cur = _clearPercentTarget; _cpSmallCounter.Changed = true;
        After(2.5f, () => _moving = true);

        foreach (var q in _charAtlasQueue)
        {
            var qq = q;
            After(qq.delay, () =>
            {
                qq.s.Visible = true;
                qq.s.Play(qq.startLabel, false);
                if (qq.sound != "") Audio.PlaySound(StripLibrary(qq.sound));
            });
        }
        foreach (var q in _charSparrowQueue)
        {
            var qq = q;
            After(qq.delay, () => { qq.s.Visible = true; qq.s.Play("idle", true); });
        }
    }

    private void TimerThenSongName(float timerLength, bool autoScroll)
    {
        _moving = false;
        _diffX = 555;
        float diffYTween = 122;
        int diffH = _diffTex?.Height ?? 58;
        _diffY = -diffH;
        TweenF(0.5f, ExpoOut, k => _diffY = -diffH + (diffYTween + (_topBarH - 148) + diffH) * k, 0.8f);

        _cpSmallCounter.X = _diffX + (_diffTex?.Width ?? 227) + 60;
        float cpH = _cpTextSmall?.Height ?? 33;
        float cpFrom = -cpH;
        _cpSmallCounter.Y = cpFrom; _cpSmallCounter.Changed = true;
        float cpTo = (122 - 5) + (_topBarH - 148);
        TweenF(0.5f, ExpoOut, k => { _cpSmallCounter.Y = cpFrom + (cpTo - cpFrom) * k; _cpSmallCounter.Changed = true; }, 0.85f);

        float songH = 61;
        _songY = -songH;
        float fucked = -(_songW * 0.5f) * MathF.Sin(SONG_ANGLE * MathF.PI / 180f) - 10f;
        float songTo = (diffYTween - 25 - fucked) + (_topBarH - 148);
        TweenF(0.5f, ExpoOut, k => _songY = -songH + (songTo + songH) * k, 0.9f);
        _songX = _cpSmallCounter.X + 94;

        After(timerLength, () =>
        {
            float tx = -1f * MathF.Cos(SONG_ANGLE * MathF.PI / 180f), ty = -1f * MathF.Sin(SONG_ANGLE * MathF.PI / 180f);
            _speedX = 0; _speedY = 0;
            TweenF(0.7f, QuadIn, k => { _speedX = tx * k; _speedY = ty * k; });
            _moving = autoScroll;
        });
    }

    private void StartMusic()
    {
        string path = (string)_playerData?["results"]?["music"]?[RankKey()] ?? "resultsNORMAL";
        string intro = $"music/{path}/{path}-intro";
        string loop = $"music/{path}/{path}";
        if (Assets.ResolvePath(intro + ".ogg") != null) Audio.PlayMusicWithIntro(intro, loop);
        else Audio.PlayMusic(loop, true);
    }

    private string RankKey() => _rank switch
    {
        ScoringRank.PERFECT_GOLD => "PERFECT_GOLD", ScoringRank.PERFECT => "PERFECT", ScoringRank.EXCELLENT => "EXCELLENT",
        ScoringRank.GREAT => "GREAT", ScoringRank.GOOD => "GOOD", _ => "SHIT"
    };

    // ===================================================================== ResultScore / ScoreNum
    private void SetScoreShit(int val)
    {
        int loopNum = 9;
        long dumb = Math.Min((long)val, 9999999999L);
        while (dumb > 0)
        {
            _scoreStart += 1;
            _scoreNums[loopNum].FinalDigit = (int)(dumb % 10);
            _scoreNums[loopNum].S.Play("GONE", true, 0); // set_finalDigit
            dumb /= 10;
            loopNum--;
        }
        while (loopNum > 0) { SetDigit(_scoreNums[loopNum], 10); loopNum--; }
    }

    private void SetDigit(ScoreNum n, int val)
    {
        if (val >= 0 && n.S.Cur != NumToString[val])
        {
            if (n.Glow) { n.S.Play(NumToString[val], true, 0); n.Glow = false; }
            else n.S.Play(NumToString[val], true, 4);
        }
        n.Digit = val;
    }

    private void AnimateNumbers()
    {
        for (int i = 10 - _scoreStart; i < 10; i++)
        {
            int ii = i;
            After((ii - 1) / 24f, () =>
            {
                var n = _scoreNums[ii];
                n.FinalDelay = _scoreStart - (ii - 1);
                n.S.Play(NumToString[n.Digit], true, 0); // playAnim
                // shuffle(): FlxTimer(1/24, shuffleProgress, Std.int((41/24)/(1/24)) = 41 loops)
                StartTimer(1f / 24f, tm =>
                {
                    int td = n.Digit + 1;
                    if (td > 9) td = 0;
                    SetDigit(n, td);
                    if (tm.Loops > 0 && tm.LoopsLeft == 0)
                    {
                        // finishShuffleTween: FlxTween.num(0, finalDigit, 23/24, quadOut) -> digit = floor(x)
                        int fd = n.FinalDigit;
                        TweenF(23f / 24f, QuadOut, k => SetDigit(n, (int)MathF.Floor(fd * k)), 0f,
                            () => After(n.FinalDelay / 24f, () => n.S.Play(n.S.Cur, true, 0)));
                    }
                }, 41);
            });
        }
    }

    // ===================================================================== TallyCounter
    private void DrawTallyNumbers(Tally tl)
    {
        var sep = new List<int>();
        int temp = (int)RoundCompat.AwayFromZero(tl.Cur);
        while (temp != 0) { sep.Add(temp % 10); temp /= 10; }
        if (sep.Count == 0) sep.Add(0);
        sep.Reverse();
        for (int ind = 0; ind < sep.Count; ind++)
        {
            if (ind >= tl.Digits.Count)
            {
                var s = new Spr { Sheet = _tallySheet, Pos = new Vector2(tl.X + ind * 43, tl.Y), Color = tl.Flavour };
                for (int d = 0; d < 10; d++) s.AddByPrefix(d.ToString(), d + " small");
                s.Play(sep[ind].ToString());
                tl.Digits.Add(s);
            }
            else { tl.Digits[ind].Play(sep[ind].ToString()); tl.Digits[ind].Color = tl.Flavour; }
        }
    }

    // ===================================================================== ClearPercentCounter
    private void DrawClearNumbers(ClearCounter c)
    {
        c.Changed = false;
        var sep = new List<int>();
        int temp = c.Cur;
        while (temp > 0) { sep.Add(temp % 10); temp /= 10; }
        if (sep.Count == 0) sep.Add(0);
        sep.Reverse();
        for (int ind = 0; ind < sep.Count; ind++)
        {
            int digitIndex = ind + 1;
            int digitOffset = sep.Count == 1 ? 1 : sep.Count == 3 ? -1 : 0;
            float size = c.Small ? 32 : 72, hOff = c.Small ? -4 : 0;
            float xPos = (digitIndex - 1 + digitOffset) * size + (c.Small ? -24 : 0);
            float yPos = (digitIndex - 1 + digitOffset) * hOff + (c.Small ? 0 : 72);
            // members[0] is the "%" text: a new digit member is created when digitIndex >= members.length
            if (digitIndex >= c.Digits.Count + 1)
            {
                bool variant = sep.Count == 3 ? digitIndex >= 2 : digitIndex >= 1;
                var sheet = c.Small ? _cpSmall : variant ? _cpRight : _cpLeft;
                var s = new Spr { Sheet = sheet, Pos = new Vector2(c.X + xPos, c.Y + yPos) };
                for (int d = 0; d < 10; d++) s.AddByPrefix(d.ToString(), $"number {d} 0");
                s.Play(sep[ind].ToString());
                c.Digits.Add((s, variant));
            }
            else
            {
                var s = c.Digits[digitIndex - 1].s;
                s.Play(sep[ind].ToString());
                s.Pos = new Vector2(xPos + c.X, yPos + c.Y);
                s.Visible = true;
            }
        }
        // members[len+1 ..] hidden (note: index offset as in the original)
        for (int ind = sep.Count + 1; ind < c.Digits.Count + 1; ind++) c.Digits[ind - 1].s.Visible = false;
        c.LastX = c.X; c.LastY = c.Y;
    }

    // ===================================================================== update
    public override void Update(GameTime gameTime)
    {
        float dt = Math.Min((float)gameTime.ElapsedGameTime.TotalSeconds, 0.1f);
        _t += dt; _dt = dt; _frameNo++;

        // timers (FlxTimerManager plugin), then tweens (FlxTweenManager plugin)
        {
            var looped = new List<FTimer>();
            foreach (var tm in _timers.ToList())
            {
                if (tm.Finished || tm.Frame >= _frameNo) continue;
                int before = tm.LoopsCounter;
                tm.Counter += dt;
                while (tm.Counter >= tm.Time && !tm.Finished)
                {
                    tm.Counter -= tm.Time; tm.LoopsCounter++;
                    if (tm.Loops > 0 && tm.LoopsCounter >= tm.Loops) tm.Finished = true;
                }
                if (tm.LoopsCounter != before) looped.Add(tm);
            }
            _inTimers = true;
            foreach (var tm in looped) { if (tm.Finished) _timers.Remove(tm); tm.Cb(tm); }
            _inTimers = false;
        }
        foreach (var tw in _tweens.ToList())
        {
            if (tw.Dead || _t < tw.Start) continue;
            float k = tw.Dur <= 0 ? 1f : Math.Clamp((_t - tw.Start) / tw.Dur, 0f, 1f);
            tw.Apply?.Invoke(tw.Ease(k));
            if (k >= 1f) { tw.Dead = true; tw.Done?.Invoke(); }
        }
        _tweens.RemoveAll(x => x.Dead);

        // movingSongStuff
        if (_moving)
        {
            float sx = _speedX * 60f * dt, sy = _speedY * 60f * dt;
            _songX += sx; _diffX += sx; _cpSmallCounter.X += sx;
            _songY += sy; _diffY += sy; _cpSmallCounter.Y += sy;
            if (_songX + _songW < 100) TimerThenSongName(3.0f, true);
        }

        // sprites
        _soundSystem.Update(dt); _resultsAnim.Update(dt); _ratingsPopin.Update(dt); _scorePopin.Update(dt); _highscoreNew.Update(dt);
        foreach (var n in _scoreNums) n.S.Update(dt);
        foreach (var c in _chars) { c.atlas?.Update(dt); c.spr?.Update(dt); }
        if (int.TryParse(Environment.GetEnvironmentVariable("FNF_RES_FRAME"), out int devFrame))
            foreach (var c in _chars) if (c.atlas != null && c.atlas.Visible && c.atlas.Frames != null) { c.atlas.Index = Math.Min(devFrame, c.atlas.Frames.Length - 1); c.atlas.Paused = true; }
        // dev hook FNF_RES_FRAMESTEP="start,first,last,interval": from <start> s freeze the character atlases on frame first + floor((t-start)/interval)
        { var fsArr = Environment.GetEnvironmentVariable("FNF_RES_FRAMESTEP")?.Split(','); if (fsArr != null && fsArr.Length == 4) { var ci = System.Globalization.CultureInfo.InvariantCulture; float st = float.Parse(fsArr[0], ci), iv = float.Parse(fsArr[3], ci); int f0 = int.Parse(fsArr[1]), f1 = int.Parse(fsArr[2]);
          if (_t >= st) { int fr = Math.Min(f1, f0 + (int)((_t - st) / iv)); foreach (var c in _chars) if (c.atlas != null && c.atlas.Visible && c.atlas.Frames != null) { c.atlas.Index = Math.Min(fr, c.atlas.Frames.Length - 1); c.atlas.Paused = true; } } } }
        if (Environment.GetEnvironmentVariable("FNF_RESLOG") == "2") Console.WriteLine($"[RESFRAME] {DevDump}");
        if (Environment.GetEnvironmentVariable("FNF_RESLOG") == "1" && _t - _devLogT >= 0.1f) { _devLogT = _t; Console.WriteLine($"[RESLOG] t={_t:F3} {DevInfo}"); }
        foreach (var tl in _tallies) { if (tl.Cur < tl.Need) DrawTallyNumbers(tl); foreach (var d in tl.Digits) d.Update(dt); }
        foreach (var c in new[] { _cpBig, _cpSmallCounter })
        {
            if (c == null) continue;
            if (c.Changed) DrawClearNumbers(c);
            else if (c.X != c.LastX || c.Y != c.LastY)
            {
                // FlxSpriteGroup position setter moves every member by the delta
                float dx = c.X - c.LastX, dy = c.Y - c.LastY;
                foreach (var d in c.Digits) d.s.Pos += new Vector2(dx, dy);
                c.LastX = c.X; c.LastY = c.Y;
            }
        }
        if (_rankTextShown)
        {
            _rankVertY += _rankVertVy * dt;
            for (int i = 0; i < 12; i++) _rankRowX[i] += _rankRowVx[i] * dt;
        }

        // music fade on exit (tween volume 0 over 0.8 s)
        if (_musicFading)
        {
            _musicFadeT += dt;
            _musicVol = Math.Max(0f, 1f - _musicFadeT / 0.8f);
            Audio.MusicVolume = HighscoreManager.Data.MusicVolume * _musicVol;
            if (_musicFadeT >= 0.8f) { _musicFading = false; Audio.StopMusic(); }
        }

        if (UpdateReplayPrompt()) return; // R / Y: watch the replay of this play (ResultsScene.Replay.cs)
        if ((Input.ConfirmPressed || Input.IsPressed(Microsoft.Xna.Framework.Input.Keys.Escape)
             || Input.IsGamePadPressed(Microsoft.Xna.Framework.Input.Buttons.Start)) && !_busy)
            BeginExit();
        if (!_busy && _devExitAt >= 0f && _t >= _devExitAt) BeginExit(); // dev hook FNF_RES_EXIT_AT=<s>
    }
    private static readonly float _devExitAt = float.TryParse(Environment.GetEnvironmentVariable("FNF_RES_EXIT_AT"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var __ea) ? __ea : -1f;

    private void BeginExit()
    {
        _busy = true;
        _musicFading = true; _musicFadeT = 0f;

        if (_isStoryMode) { SwitchOut(); return; }
        string oldRankString = HasPrevRank ? PrevRankString : HighscoreManager.GetRank(_songName, _difficultyId);
        ScoringRank? oldRank = null;
        if (!string.IsNullOrEmpty(oldRankString) && Enum.TryParse<ScoringRank>(oldRankString, out var parsed)) oldRank = parsed;
        bool valid = !IsPracticeMode && !IsBotPlayMode;
        // ScoringRank enum here is ordered best-first: "rank > prevRank" == smaller ordinal
        bool personalBest = oldRank == null || (int)_rank < (int)oldRank.Value;
        if (valid && personalBest)
            TweenF(0.5f, ExpoOut, k => _rankBgAlpha = k, 0f, () => SwitchOut(oldRank));
        else SwitchOut(oldRank, false);
    }

    private void SwitchOut(ScoringRank? oldRank = null, bool playRankAnim = true)
    {
        // ResultState.transitionToState: personal best -> FlxG.switchState(FreeplayState.build(fromResults)) right after the
        // rankBg tween (no fade); story mode / no new best -> StickerSubState, then StoryMenuState / FreeplayState(stickers).
        if (!_isStoryMode && !IsBotPlayMode && !IsPracticeMode) // never save botplay/practice clears (PlayState.endSong)
            HighscoreManager.SaveScore(_songName, _difficultyId, _score, _maxCombo, _totalNotes == 0 ? 0f : Math.Clamp((_tallySick + _tallyGood - _tallyMissed) / (float)_totalNotes, 0f, 1f), _rank.ToString());   // exact tallyCompletion (was the floored %)
        // no new best: FreeplayState.build(null, sticker) -> no fromResults params, forceSkipIntro
        Func<Scene> target = _isStoryMode
            ? () => new StoryModeScene()
            : !playRankAnim ? () => new FreeplayScene { ForceSkipIntro = true }
            : () => new FreeplayScene(new FreeplayResultParams
              {
                  SongId = _songName, DifficultyId = _difficultyId, OldRank = oldRank, NewRank = _rank, PlayRankAnim = playRankAnim
              });
        if (!_isStoryMode && playRankAnim)
        {
            Audio.MusicVolume = HighscoreManager.Data.MusicVolume;
            Game.Scenes.SwitchSceneInstant(target());
            return;
        }
        string pack = StickerTransition.ResolvePackId(Assets, _songName, (_playerId == "pico" ? "pico-playable" : "bf"));
        StickerTransition.Start(Game.Scenes, Assets, n => Audio.PlaySound(n), pack, () =>
        {
            Audio.MusicVolume = HighscoreManager.Data.MusicVolume;
            return target();
        });
    }

    public override void Unload()
    {
        Audio.MusicVolume = HighscoreManager.Data.MusicVolume;
        _bgTex?.Dispose(); _bgFlashTex?.Dispose(); _topBarTex?.Dispose();
        foreach (var w in _whiteTex.Values) w.Dispose();
        foreach (var w in _premulTex.Values) w.Dispose();
    }

    // ===================================================================== draw
    public override void Draw(SpriteBatch sb)
    {
        var gd = Game.GraphicsDevice;
        gd.Clear(Color.Magenta);

        // cameraBG
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp);
        sb.Draw(_bgTex, new Rectangle(0, 0, 1280, 720), Color.White);
        sb.End();

        // cameraScroll (1280 x 864, canvas.rotation = -3.8 about the camera's top-left): 12 FlxBackdrop rows (X, spacing 10).
        // Textures FromStream are straight alpha with black transparent texels: filtered (rotated / sub-pixel) draws use
        // premultiplied copies + AlphaBlend like OpenFL, otherwise the edges get dark fringes.
        if (_rankTextShown && _rankHorTex != null)
        {
            var m = Matrix.CreateRotationZ(MathHelper.ToRadians(-3.8f));
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, null, null, null, m);
            var tex = Premul(_rankHorTex);
            float step = _rankHorTex.Width + 10;
            for (int i = 0; i < 12; i++)
            {
                float y = 50 + (135 * i / 2f) + 10;
                float x0 = _rankRowX[i] % step; if (x0 > 0) x0 -= step;
                for (float x = x0; x < 1280; x += step) sb.Draw(tex, new Vector2(x, y), Color.White);
            }
            sb.End();
        }

        // cameraEverything, zIndex order
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp);
        if (_bgFlashVisible && _bgFlashAlpha > 0 && HighscoreManager.Data.FlashingLights)
            sb.Draw(_bgFlashTex, new Rectangle(0, 0, 1280, 720), new Color(Color.White, _bgFlashAlpha));
        sb.End();
        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp);
        if (_cpBig != null && !_cpBig.Removed) DrawClearCounter(sb, _cpBig);
        sb.End();

        // characters (zIndex 499-501) - atlas parts need their own batches
        foreach (var c in _chars.OrderBy(c => c.z).ThenBy(c => c.order))
        {
            if (c.atlas != null) { if (c.atlas.Visible) c.atlas.A.Draw(sb, c.atlas.TimelineFrame, c.atlas.Pos, c.atlas.Scale, Premul); }
            else if (c.spr != null && c.spr.Visible)
            {
                sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp);
                DrawSpr(sb, c.spr);
                sb.End();
            }
        }

        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp);
        // rankTextVert (990): FlxBackdrop(Y, spacingY 30)
        if (_rankTextShown && _rankVertVisible && _rankVerTex != null)
        {
            var tex = Premul(_rankVerTex);
            float step = _rankVerTex.Height + 30;
            float y0 = _rankVertY % step; if (y0 > 0) y0 -= step;
            for (float y = y0; y < 720; y += step) sb.Draw(tex, new Vector2(_rankVertX, y), Color.White);
        }
        // difficulty (LeftMaskShader: hide texels left of screen x 525), songName, clearPercentSmall (1000)
        if (_diffTex != null)
        {
            int cut = (int)Math.Clamp(MathF.Ceiling(525 - _diffX), 0, _diffTex.Width);
            if (cut < _diffTex.Width)
                sb.Draw(Premul(_diffTex), new Vector2(_diffX + cut, _diffY), new Rectangle(cut, 0, _diffTex.Width - cut, _diffTex.Height), Color.White);
        }
        DrawSongName(sb);
        if (_cpSmallAdded) DrawClearCounter(sb, _cpSmallCounter, clipX: 520);
        sb.End();

        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp);
        // blackTopBar (1010)
        sb.Draw(_topBarTex, new Vector2(0, _topBarY), Color.White);
        // soundSystem (1100)
        DrawSpr(sb, _soundSystem);
        // 1200: resultsAnim, ratingsPopin, scorePopin, highscoreNew, ratingGrp, score
        DrawSpr(sb, _resultsAnim); DrawSpr(sb, _ratingsPopin); DrawSpr(sb, _scorePopin); DrawSpr(sb, _highscoreNew);
        foreach (var tl in _tallies) if (tl.Visible) foreach (var d in tl.Digits) DrawSpr(sb, d);
        if (_scoreVisible) foreach (var n in _scoreNums) DrawSpr(sb, n.S);
        if (_rankBgAlpha > 0) sb.Draw(Assets.Pixel, new Rectangle(0, 0, 1280, 720), new Color(Color.Black, _rankBgAlpha));
        sb.End();
        DrawReplayPrompt(sb);
    }

    private void DrawSpr(SpriteBatch sb, Spr s, float groupAlpha = 1f, float clipX = float.NegativeInfinity, bool premul = false)
    {
        if (s == null || !s.Visible) return;
        var f = s.CurFrame;
        if (f == null) return;
        var tex = f.Texture ?? s.Sheet.Texture;
        if (s.White) tex = WhiteOf(tex); else if (premul) tex = Premul(tex);
        var src = f.SourceRect;
        var pos = s.Pos + f.Offset;
        if (clipX > float.NegativeInfinity)
        {
            // clipRect = (max(0, clipX - spr.x), 0, ...) in frame space
            float cutFrame = Math.Max(0, clipX - s.Pos.X);
            float cut = cutFrame - f.Offset.X;
            if (cut > 0)
            {
                int c = (int)MathF.Ceiling(cut);
                if (c >= src.Width) return;
                src = new Rectangle(src.X + c, src.Y, src.Width - c, src.Height);
                pos.X += c;
            }
        }
        var col = s.Color; float a = s.Alpha * groupAlpha;
        a = Math.Clamp(a, 0f, 1f);
        if (premul) sb.Draw(tex, pos, src, new Color(col.R / 255f * a, col.G / 255f * a, col.B / 255f * a, a));
        else sb.Draw(tex, pos, src, new Color(col.R, col.G, col.B, (byte)(a * 255f)));
    }

    private void DrawClearCounter(SpriteBatch sb, ClearCounter c, float clipX = float.NegativeInfinity)
    {
        if (!c.Visible) return;
        var text = c.Small ? _cpTextSmall : _cpText;
        if (text != null)
        {
            float tx = c.X + (c.Small ? 40 : 0), ty = c.Y;
            int cut = clipX > float.NegativeInfinity ? (int)Math.Clamp(MathF.Ceiling(clipX - tx), 0, text.Width) : 0;
            if (cut < text.Width)
                sb.Draw(Premul(text), new Vector2(tx + cut, ty), new Rectangle(cut, 0, text.Width - cut, text.Height), Color.White * c.Alpha);
        }
        foreach (var d in c.Digits) { d.s.White = c.Flash; DrawSpr(sb, d.s, c.Alpha, clipX, premul: true); }
    }

    private void DrawSongName(SpriteBatch sb)
    {
        if (_songFontTex == null || string.IsNullOrEmpty(Title)) return;
        // FlxBitmapText: glyph i at local x = sum(advance + letterSpacing); rotated by angle about its centre;
        // clipRect = (max(0, 520 - x), 0, 1280, h) in local (unrotated) space.
        float rot = MathHelper.ToRadians(SONG_ANGLE);
        var centre = new Vector2(_songX + _songW / 2f, _songY + 61 / 2f);
        float clip = Math.Max(0, 520 - _songX);
        float curX = 0;
        string text = Title;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            bool isSpace = ch == ' ';
            bool has = _songGlyphs.TryGetValue(ch, out var g);
            if (has && !isSpace)
            {
                float gx = curX;
                int cut = (int)Math.Clamp(MathF.Ceiling(clip - gx), 0, 49);
                if (cut < 49)
                {
                    var src = new Rectangle(g.X + cut, g.Y, g.Width - cut, g.Height);
                    var origin = new Vector2(_songW / 2f - (gx + cut), 61 / 2f);
                    sb.Draw(Premul(_songFontTex), centre, src, Color.White, rot, origin, 1f, SpriteEffects.None, 0f);
                }
            }
            if (has || isSpace) curX += 49 - 15;
        }
    }

    private void BuildSongFont()
    {
        _songFontTex = Assets.LoadTexture("resultScreen/tardlingSpritesheet");
        const string letters = "AaBbCcDdEeFfGgHhiIJjKkLlMmNnOoPpQqRrSsTtUuVvWwXxYyZz:1234567890().-";
        if (_songFontTex != null && _songFontTex != Assets.Pixel)
        {
            int cols = _songFontTex.Width / 49;
            for (int i = 0; i < letters.Length; i++)
                _songGlyphs[letters[i]] = new Rectangle((i % cols) * 49, (i / cols) * 61, 49, 61);
        }
        if (string.IsNullOrEmpty(Title)) Title = _songName;
        // getStringWidth: sum(charWidth + letterSpacing) - letterSpacing; unknown chars count 0 + letterSpacing
        float w = 0;
        foreach (char ch in Title) w += (ch == ' ' || _songGlyphs.ContainsKey(ch) ? 49 : 0) - 15;
        if (Title.Length > 0) w += 15;
        _songW = (int)w;
    }

    // ===================================================================== textures
    private Texture2D MakeGradient(Color top, Color bottom)
    {
        var tex = new Texture2D(Game.GraphicsDevice, 1, 720);
        var px = new Color[720];
        for (int y = 0; y < 720; y++) px[y] = Color.Lerp(top, bottom, y / 719f);
        tex.SetData(px);
        return tex;
    }

    /// <summary>BitmapUtil.createResultsBar(): black ceil(1280*1.011) x ceil(w/8.7) rect rotated -3.8 deg, moved -15 px.</summary>
    private Texture2D MakeResultsBar(out int h)
    {
        int w = (int)Math.Ceiling(1280 * 1.011);
        h = (int)Math.Ceiling(w / 8.7);
        var tex = new Texture2D(Game.GraphicsDevice, w, h);
        var px = new Color[w * h];
        double th = -3.8 * Math.PI / 180, c = Math.Cos(th), s = Math.Sin(th);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int hits = 0;
                for (int sy = 0; sy < 4; sy++)
                    for (int sx = 0; sx < 4; sx++)
                    {
                        double X = x + (sx + 0.5) / 4 + 15, Y = y + (sy + 0.5) / 4;
                        double u = X * c + Y * s, v = -X * s + Y * c; // inverse rotation
                        if (u >= 0 && u < w && v >= 0 && v < h) hits++;
                    }
                px[y * w + x] = new Color(0, 0, 0, hits * 255 / 16);
            }
        tex.SetData(px);
        return tex;
    }

    private Texture2D WhiteOf(Texture2D t)
    {
        if (t == null) return null;
        if (_whiteTex.TryGetValue(t, out var w)) return w;
        var px = new Color[t.Width * t.Height];
        t.GetData(px);
        for (int i = 0; i < px.Length; i++) px[i] = new Color(px[i].A, px[i].A, px[i].A, px[i].A); // PureColor: white * alpha (premultiplied)
        w = new Texture2D(Game.GraphicsDevice, t.Width, t.Height);
        w.SetData(px);
        _whiteTex[t] = w;
        return w;
    }

    private readonly Dictionary<Texture2D, Texture2D> _premulTex = new();
    private Texture2D Premul(Texture2D t)
    {
        if (t == null) return null;
        if (_premulTex.TryGetValue(t, out var w)) return w;
        var px = new Color[t.Width * t.Height];
        t.GetData(px);
        for (int i = 0; i < px.Length; i++) { var c = px[i]; px[i] = new Color(c.R * c.A / 255, c.G * c.A / 255, c.B * c.A / 255, c.A); }
        w = new Texture2D(Game.GraphicsDevice, t.Width, t.Height);
        w.SetData(px);
        _premulTex[t] = w;
        return w;
    }

    // ===================================================================== Scoring
    private ScoringRank CalculateRank()
    {
        if (_totalNotes == 0) return ScoringRank.SHIT;
        if (_tallySick == _totalNotes) return ScoringRank.PERFECT_GOLD;
        float completion = Math.Clamp((_tallySick + _tallyGood - _tallyMissed) / (float)_totalNotes, 0f, 1f);
        if (completion == 1f) return ScoringRank.PERFECT;
        if (completion >= 0.90f) return ScoringRank.EXCELLENT;
        if (completion >= 0.80f) return ScoringRank.GREAT;
        if (completion >= 0.60f) return ScoringRank.GOOD;
        return ScoringRank.SHIT;
    }

    private float MusicDelay() => _rank switch
    {
        ScoringRank.PERFECT_GOLD or ScoringRank.PERFECT => 95f / 24f, ScoringRank.EXCELLENT => 0f,
        ScoringRank.GREAT => 5f / 24f, ScoringRank.GOOD => 3f / 24f, _ => 2f / 24f
    };
    private float BFDelay() => _rank switch
    {
        ScoringRank.PERFECT_GOLD or ScoringRank.PERFECT => 95f / 24f, ScoringRank.EXCELLENT => 97f / 24f, _ => 95f / 24f
    };
    private float FlashDelay() => _rank switch
    {
        ScoringRank.PERFECT_GOLD or ScoringRank.PERFECT => 129f / 24f, ScoringRank.EXCELLENT => 122f / 24f,
        ScoringRank.GREAT => 109f / 24f, ScoringRank.GOOD => 107f / 24f, _ => 186f / 24f
    };
    private float HighscoreDelay() => _rank switch
    {
        ScoringRank.PERFECT_GOLD or ScoringRank.PERFECT => 140f / 24f, ScoringRank.EXCELLENT => 140f / 24f,
        ScoringRank.GREAT => 129f / 24f, ScoringRank.GOOD => 127f / 24f, _ => 207f / 24f
    };
    private string HorTextAsset() => _rank switch
    {
        ScoringRank.PERFECT_GOLD or ScoringRank.PERFECT => "resultScreen/rankText/rankScrollPERFECT",
        ScoringRank.EXCELLENT => "resultScreen/rankText/rankScrollEXCELLENT", ScoringRank.GREAT => "resultScreen/rankText/rankScrollGREAT",
        ScoringRank.GOOD => "resultScreen/rankText/rankScrollGOOD", _ => "resultScreen/rankText/rankScrollLOSS"
    };
    private string VerTextAsset() => _rank switch
    {
        ScoringRank.PERFECT_GOLD or ScoringRank.PERFECT => "resultScreen/rankText/rankTextPERFECT",
        ScoringRank.EXCELLENT => "resultScreen/rankText/rankTextEXCELLENT", ScoringRank.GREAT => "resultScreen/rankText/rankTextGREAT",
        ScoringRank.GOOD => "resultScreen/rankText/rankTextGOOD", _ => "resultScreen/rankText/rankTextLOSS"
    };
}
