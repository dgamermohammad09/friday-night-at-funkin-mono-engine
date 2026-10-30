using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Per-capsule SongMenuItem motion (initJumpIn / doLerp / targetPos, changeSelection targets) and the
/// return-from-results rank animation (FreeplayState.rankAnimStart / rankDisplayNew / rankAnimSlam), driven by a
/// minimal FlxTimer/FlxTween emulation with flixel's update order (timers, then tweens, then the state).
/// funnyCam (whole Freeplay, zoom + shake) and rankCamera (rankBg, capsuleToRank, sparks, fade) are rendered
/// through render targets scaled about the screen centre like FlxCamera.zoom.
/// </summary>
public partial class FreeplayScene
{
    // ------------------------------------------------------------------ FlxTimer / FlxTween emulation
    private sealed class FxTimer
    {
        public float Time, Counter; public int Loops, Count; public bool Finished; public Action<FxTimer> Cb;
        public int LoopsLeft => Loops - Count;
    }
    private sealed class FxTween
    {
        public object Target; public Func<float> Get; public Action<float> Set; public float From = float.NaN, To, Dur, T;
        public Func<float, float> Ease; public bool Finished, Cancelled; public Action Done;
    }
    private readonly List<FxTimer> _fxTimers = new();
    private readonly List<FxTween> _fxTweens = new();

    private FxTimer FxStartTimer(float time, Action<FxTimer> cb, int loops = 1)
    {
        var t = new FxTimer { Time = time, Loops = loops, Cb = cb };
        _fxTimers.Add(t);
        return t;
    }

    private FxTween FxTweenTo(object target, Func<float> get, Action<float> set, float to, float dur, Func<float, float> ease, Action done = null)
    {
        var tw = new FxTween { Target = target, Get = get, Set = set, To = to, Dur = dur, Ease = ease, Done = done };
        _fxTweens.Add(tw);
        return tw;
    }

    private void FxCancelTweensOf(object target)
    {
        foreach (var tw in _fxTweens) if (ReferenceEquals(tw.Target, target)) tw.Cancelled = true;
    }

    /// <summary>FlxTimerManager.update then FlxTweenManager.update (plugins run before the state).</summary>
    private void FxPluginsUpdate(float dt)
    {
        List<FxTimer> looped = null;
        for (int i = 0; i < _fxTimers.Count; i++)
        {
            var tm = _fxTimers[i];
            if (tm.Finished) continue;
            int before = tm.Count;
            tm.Counter += dt;
            while (tm.Counter >= tm.Time && !tm.Finished)
            {
                tm.Counter -= tm.Time;
                tm.Count++;
                if (tm.Loops > 0 && tm.Count >= tm.Loops) tm.Finished = true;
                if (tm.Time <= 0f) break;
            }
            if (before != tm.Count) (looped ??= new()).Add(tm);
        }
        if (looped != null) foreach (var tm in looped) tm.Cb?.Invoke(tm);
        _fxTimers.RemoveAll(t => t.Finished);

        List<FxTween> fin = null;
        for (int i = 0; i < _fxTweens.Count; i++)
        {
            var tw = _fxTweens[i];
            if (tw.Cancelled || tw.Finished) continue;
            if (float.IsNaN(tw.From)) tw.From = tw.Get();
            tw.T += dt;
            float s;
            if (tw.T >= tw.Dur) { s = 1f; tw.Finished = true; }
            else s = tw.Ease != null ? tw.Ease(tw.T / tw.Dur) : tw.T / tw.Dur;
            tw.Set(tw.From + (tw.To - tw.From) * s);
            if (tw.Finished) (fin ??= new()).Add(tw);
        }
        if (fin != null) foreach (var tw in fin) tw.Done?.Invoke();
        _fxTweens.RemoveAll(t => t.Finished || t.Cancelled);
    }

    // FlxEase (flixel/tweens/FlxEase.hx)
    private static float FxLinear(float t) => t;
    private static float FxSineIn(float t) => -MathF.Cos(MathF.PI / 2f * t) + 1f;
    private static float FxQuadOut(float t) => -t * (t - 2f);
    private static float FxQuartIn(float t) => t * t * t * t;
    private static float FxExpoIn(float t) => MathF.Pow(2f, 10f * (t - 1f));
    private static float FxExpoOut(float t) => -MathF.Pow(2f, -10f * t) + 1f;
    private static float FxBackIn(float t) => t * t * (2.70158f * t - 1.70158f);
    private static float FxBackOut(float t) { t -= 1f; return t * t * (2.70158f * t + 1.70158f) + 1f; }
    private static float FxBackInOut(float t)
    {
        t *= 2f;
        if (t < 1f) return t * t * (3.5949095f * t - 2.5949095f) / 2f;
        t -= 2f;
        return (t * t * (3.5949095f * t + 2.5949095f) + 2f) / 2f;
    }
    private static float FxElasticOut(float t)
    {
        const float amp = 1f, period = 0.4f;
        return amp * MathF.Pow(2f, -10f * t) * MathF.Sin((t - (period / (2f * MathF.PI) * MathF.Asin(1f / amp))) * (2f * MathF.PI) / period) + 1f;
    }

    // ------------------------------------------------------------------ SongMenuItem motion
    private sealed class CapState
    {
        public float X, Y, TX, TY;
        public bool DoLerp, DoJumpIn;
        public float InTicker; public int InBeat;
        public float SX = 1f, SY = 1f;        // capsule.scale / realScaled (jump-in xFrames)
        public float BodyAngle;                // capsule.capsule.angle (degrees)
        public Vector2 BodyOff;                // IntervalShake on capsule.capsule
        public readonly object BodyKey = new(), TargetKey = new();
    }
    private readonly Dictionary<FreeplaySong, CapState> _caps = new();
    private List<FreeplaySong> _capsList;
    private int _capsLastSel = -1;
    private bool _capsJumpPending;

    private static readonly float[] JumpInXFrames = { 1.7f, 1.8f, 0.85f, 0.85f, 0.97f, 0.97f, 1f };
    private static readonly float[] JumpInXPos = { 0f, 0f, 0.16f, 0.16f, 0.22f, 0.22f, 0.245f };

    private static float CapIntendedX(float index) => (float)(270.0 + 60.0 * FastSin(index));

    /// <summary>FlxMath.fastSin (SongMenuItem.intendedX uses it: intendedX(1) = 320.53, not 320.49).</summary>
    private static double FastSin(double n)
    {
        n *= 0.3183098862;
        if (n > 1) n -= ((int)Math.Ceiling(n) >> 1) << 1;
        else if (n < -1) n += ((int)Math.Ceiling(-n) >> 1) << 1;
        if (n > 0) return n * (3.1 + n * (0.5 + n * (-7.2 + n * 3.6)));
        return n * (3.1 - n * (0.5 + n * (7.2 + n * 3.6)));
    }
    private static float CapIntendedY(float index) => index * (CAPSULE_FRAME_H * CAPSULE_SCALE + 10f) + 120f;

    private CapState Cap(int songIdx)
    {
        var song = _songs[songIdx];
        if (!_caps.TryGetValue(song, out var cs))
        {
            // generateSongList: random y = intendedY(0) + 10; song i of `songs` (index 0 = the null RANDOM entry) y = intendedY(i + 1) + 10
            cs = new CapState { X = FNFGame.SCREEN_WIDTH, Y = CapIntendedY(songIdx == 0 ? 0 : songIdx + 1) + 10f, DoLerp = true };
            cs.TX = cs.X; cs.TY = cs.Y;
            _caps[song] = cs;
        }
        return cs;
    }

    /// <summary>generateSongList: every capsule initPosition(FlxG.width, 0), y = intendedY(member) + 10, initJumpIn(0).</summary>
    private void CapsGenerate(bool jumpIn)
    {
        _caps.Clear();
        _capsList = _songs;
        for (int m = 0; m < _songs.Count; m++)
        {
            var cs = Cap(m);
            cs.DoLerp = false;
            cs.SX = cs.SY = 1f;
            if (!jumpIn) { cs.InBeat = JumpInXFrames.Length; }
        }
        CapsChangeSelection();
        if (jumpIn)
            FxStartTimer(0f, _ => { foreach (var c in _caps.Values) { c.DoJumpIn = true; c.DoLerp = true; c.InBeat = 0; c.InTicker = 0f; } });
        else
            foreach (var c in _caps.Values) { c.X = c.TX; c.Y = c.TY; c.DoLerp = true; }
    }

    /// <summary>FreeplayState.changeSelection: capsule targetPos for every member (index = member + 1).</summary>
    private void CapsChangeSelection()
    {
        _capsLastSel = _selectedIndex;
        for (int m = 0; m < _songs.Count; m++)
        {
            var cs = Cap(m);
            int index = m + 1;
            int capsuleIndex = index - _selectedIndex;
            float yOffset = 0f;
            if (capsuleIndex < 0) yOffset += 50f;
            else if (capsuleIndex > 4) yOffset -= 10f;
            cs.TY = CapIntendedY(capsuleIndex) - yOffset;
            cs.TX = CapIntendedX(capsuleIndex) + SongsBaseX;
            if (index < _selectedIndex) cs.TY -= 100f;
        }
    }

    /// <summary>SongMenuItem.update for every capsule (jump-in squash + smoothLerpPrecision towards targetPos).</summary>
    private void CapsUpdate(float dt)
    {
        if (_songs == null) return;
        if (!ReferenceEquals(_capsList, _songs))
        {
            // list regenerated (letter sort / difficulty filter): new capsules jump in, kept ones keep their state
            bool letter = _capsJumpPending;
            _capsJumpPending = false;
            if (letter || _capsList == null) CapsGenerate(true);
            else { _capsList = _songs; CapsChangeSelection(); }
        }
        if (_capsLastSel != _selectedIndex) CapsChangeSelection();
        float a = 1f - MathF.Pow(0.01f, dt / 0.256f), b = 1f - MathF.Pow(0.01f, dt / 0.192f);
        for (int m = 0; m < _songs.Count; m++)
        {
            var cs = Cap(m);
            if (cs.DoJumpIn)
            {
                cs.InTicker += dt;
                if (cs.InTicker >= 1f / 24f && cs.InBeat < JumpInXFrames.Length)
                {
                    cs.InTicker = 0f;
                    cs.SX = JumpInXFrames[cs.InBeat];
                    cs.SY = 1f / JumpInXFrames[cs.InBeat];
                    cs.TX = FNFGame.SCREEN_WIDTH * JumpInXPos[Math.Min(cs.InBeat, JumpInXPos.Length - 1)];
                    cs.InBeat++;
                    // SongMenuItem.index: Random = 1, song = (index in `songs`, whose entry 0 is the null RANDOM) + 1 = member + 1
                    int itemIndex = m + 1;
                    if (cs.TX <= 320f) cs.TX = CapIntendedX(itemIndex - _capsLastSel) + 0f;
                }
                else if (cs.InBeat == JumpInXFrames.Length) cs.DoJumpIn = false;
            }
            if (cs.DoLerp)
            {
                cs.X += (cs.TX - cs.X) * a;
                cs.Y += (cs.TY - cs.Y) * b;
            }
        }
    }

    // ------------------------------------------------------------------ IntervalShake (funkin.effects.IntervalShake)
    private void FxIntervalShake(Func<Vector2> get, Action<Vector2> set, float duration, float interval, float startIntensity, float width,
        Func<float, float> ease, Action done)
    {
        Vector2 initial = get();
        float since = 0f;
        int loops = (int)((double)duration / (double)interval);
        FxStartTimer(interval, tm =>
        {
            since += interval;
            float scale = 1f - ease(since / duration);
            float cur = 0f + (startIntensity - 0f) * scale;
            set(new Vector2(initial.X + RandF(-cur * width, cur * width), initial.Y + RandF(-cur * width, cur * width)));
            if (tm.Loops > 0 && tm.LoopsLeft == 0)
            {
                set(initial);
                done?.Invoke();
            }
        }, loops);
    }

    private static float RandF(float min, float max) => min + (float)_rng.NextDouble() * (max - min);

    // ------------------------------------------------------------------ rank animation state
    private bool _prepForNewRank;          // FreeplayState.prepForNewRank
    private bool _rankBlockInput;          // uiStateMachine Interacting until the slam shake ends
    private bool _rankFx;                  // render through funnyCam / rankCamera targets
    private CapState _rankCap;             // capsuleToRank (on rankCamera while non-null)
    private float _funnyZoom = 1f, _rankZoom = 1f;
    private float _rankBgA;                // rankBg.alpha (colour 0xD3000000)
    private float _rankFadeA, _rankFadeDur; private bool _rankFadeIn;   // rankCamera.fade
    private float _funnyShakeDur, _funnyShakeInt; private Vector2 _funnyShakeOff;
    private float _vignetteA; private Color _vignetteCol = Color.White;
    private bool _fakeRankVisible, _rankingVisible; private float _rankingScale = 0.9f;
    private float _rankT;                  // seconds since rankAnimStart (badge animation clocks)
    private float _rankingAnimStart, _fakeAnimStart; private int _rankStickySel = -1, _rankStickyDiff = -1;
    private bool _sparksVisible; private float _sparksT;
    private ScoringRank? _rankOld; private ScoringRank _rankNew;
    private SpriteSheet _sparksSheet, _sparksAddSheet; private Texture2D _rankVignetteTex;
    private RenderTarget2D _funnyRT, _rankRT;
    private bool _drawingRankCam;
    private enum DjFist { None, Intro, Loop }
    private DjFist _djFist;

    private static Color RankFreeplayColor(ScoringRank r) => r switch
    {
        ScoringRank.SHIT => new Color(0x60, 0x44, 0xFF),
        ScoringRank.GOOD => new Color(0xEF, 0x87, 0x64),
        ScoringRank.GREAT => new Color(0xEA, 0xF6, 0xFF),
        ScoringRank.EXCELLENT => new Color(0xFD, 0xCB, 0x42),
        ScoringRank.PERFECT => new Color(0xFF, 0x58, 0xB4),
        _ => new Color(0xFF, 0xB6, 0x19),
    };

    /// <summary>FreeplayState.create with fromResults.playRankAnim: rankCamera.fade(black, 0, false) (screen black until rankAnimStart).</summary>
    private void RankFxInitOnLoad()
    {
        if (_resultParams == null || !_resultParams.PlayRankAnim) return;
        _prepForNewRank = true;
        _rankFx = true;
        _rankFadeIn = false; _rankFadeDur = 0.000001f; _rankFadeA = 0.000001f;
        try { _sparksSheet = SpriteSheet.Load(Game, "menus/freeplay/sparks"); } catch { }
        try { _sparksAddSheet = SpriteSheet.Load(Game, "menus/freeplay/sparksadd"); } catch { }
        try { _rankVignetteTex = Assets.LoadTexture("menus/freeplay/rankVignette.png"); } catch { }
        foreach (var s in new[] { "rankinbad", "rankinperfect", "rankinnormal", "loss", "good", "great", "excellent", "perfect" })
            try { Audio.PreloadSound("ranks/" + s); } catch { }
    }

    /// <summary>Called first thing in Update: plugins (timers, tweens), capsules, DJ fist pump state, camera fx.</summary>
    private void RankFxUpdate(float dt)
    {
        FxPluginsUpdate(dt); _letterAnimTime += dt;
        if (_capTextSelIdx != _selectedIndex) { _capTextSelIdx = _selectedIndex; _capTextSelTime = 0f; } else _capTextSelTime += dt;
        if (DevRankElapsed >= 0f) DevRankElapsed += dt;
        if (_prepForNewRank || _rankCap != null) _rankT += dt;
        CapsUpdate(dt);
        if (_sparksVisible)
        {
            _sparksT += dt;
            int n = _sparksSheet?.GetAnimation("sparks")?.Count ?? 5;
            if (_sparksT >= n / 24f) _sparksVisible = false;
        }
        DjFistPumpUpdate();
        // FlxCamera.updateFade / updateShake (rankCamera, funnyCam)
        if (_rankFadeDur > 0f)
        {
            _rankFadeA += (_rankFadeIn ? -dt : dt) / _rankFadeDur;
            if (_rankFadeA <= 0f) { _rankFadeA = 0f; _rankFadeDur = 0f; }
            else if (_rankFadeA >= 1f) { _rankFadeA = 1f; _rankFadeDur = 0f; }
        }
        _funnyShakeOff = Vector2.Zero;
        if (_funnyShakeDur > 0f)
        {
            _funnyShakeDur -= dt;
            if (_funnyShakeDur > 0f)
                _funnyShakeOff = new Vector2(RandF(-1f, 1f) * _funnyShakeInt * FNFGame.SCREEN_WIDTH * _funnyZoom,
                    RandF(-1f, 1f) * _funnyShakeInt * FNFGame.SCREEN_HEIGHT * _funnyZoom);
        }
        if (_rankFx && !_prepForNewRank && _rankCap == null && _funnyShakeDur <= 0f && _vignetteA <= 0f
            && _funnyZoom == 1f && _rankZoom == 1f && _rankFadeA <= 0f && _rankBgA <= 0f)
            _rankFx = false;
    }

    // BaseFreeplayDJ.fistPumpIntro / fistPump (+ AnimateAtlasFreeplayDJ.update FistPumpIntro / FistPump cases)
    private (int introStart, int introEnd, int loopStart, int loopEnd) DjFistFrames(bool bad) => _isPicoTheme
        ? (bad ? (0, 0, 0, -1) : (0, 3, 4, -1))
        : (bad ? (0, 4, 4, -1) : (0, 4, 4, -1));

    private string DjFistAnim(bool bad) => bad ? _djLossAnim : _djFistPumpAnim;

    private void DjPlayFist(bool intro)
    {
        bool bad = _rankNew == ScoringRank.SHIT;
        string anim = DjFistAnim(bad);
        if (_djSprite == null || string.IsNullOrEmpty(anim)) return;
        var f = DjFistFrames(bad);
        _djFist = intro ? DjFist.Intro : DjFist.Loop;
        _djSprite.PlayAnimationFromFrame(anim, intro ? f.introStart : f.loopStart, loop: false);
        _djSprite.OnFinish = () =>
        {
            // onFinishAnim(fistPump / loss): currentState = Idle -> idle (looped)
            _djFist = DjFist.None;
            if (!string.IsNullOrEmpty(_djIdleAnim)) _djSprite.PlayAnimation(_djIdleAnim, force: true, loop: true);
        };
    }

    private void DjFistPumpUpdate()
    {
        if (_djFist == DjFist.None || _djSprite == null) return;
        bool bad = _rankNew == ScoringRank.SHIT;
        var f = DjFistFrames(bad);
        int end = _djFist == DjFist.Intro ? f.introEnd : f.loopEnd;
        if (end > -1 && _djSprite.FrameIndex >= end)
            _djSprite.PlayAnimationFromFrame(DjFistAnim(bad), _djFist == DjFist.Intro ? f.introStart : f.loopStart, loop: false);
    }

    /// <summary>FreeplayState.rankAnimStart (FreeplayState.hx:1072).</summary>
    private void StartRankAnimation(FreeplayResultParams result)
    {
        if (_songs.Count == 0 || _selectedIndex < 0 || _selectedIndex >= _songs.Count) return;
        _rankBlockInput = true;
        _rankOld = result.OldRank; _rankNew = result.NewRank;
        _rankT = 0f; DevRankElapsed = 0f;
        var cap = Cap(_selectedIndex);
        _fakeRankVisible = false;
        CapsChangeSelection();
        _lastPreviewIndex = _selectedIndex;   // no automatic preview while prepForNewRank

        DjPlayFist(true);

        _rankFadeIn = true; _rankFadeDur = 0.5f; _rankFadeA = 0.999999f;
        Audio.MusicVolume = 0f;
        _rankBgA = 1f;

        if (_rankOld != null)
        {
            _fakeRankVisible = true; _fakeAnimStart = 0f;   // fakeRanking.rank = oldRank (set_rank replays)
            _sparksVisible = false;
        }
        cap.DoLerp = false;
        _rankingVisible = false;

        _rankZoom = 1.85f;
        FxTweenTo(this, () => _rankZoom, v => _rankZoom = v, 1.8f, 0.6f, FxSineIn);
        _funnyZoom = 1.15f;
        FxTweenTo(this, () => _funnyZoom, v => _funnyZoom = v, 1.1f, 0.6f, FxSineIn);

        _rankCap = cap;
        cap.X = FNFGame.SCREEN_WIDTH / 2f - CAPSULE_FRAME_W / 2f;
        cap.Y = FNFGame.SCREEN_HEIGHT / 2f - CAPSULE_FRAME_H / 2f;

        FxStartTimer(0.5f, _ => RankDisplayNew(cap));

        // onDJIntroDone (after rankAnimStart): albumRoll.skipIntro(); albumRoll.showStars();
        PlayAlbumRollState("switch", loop: false, force: true);
        PlayAlbumTitleState("switch", loop: false, force: true);
        _albumStarsVisible = true;
    }

    private void RankDisplayNew(CapState cap)
    {
        _rankingVisible = true; _rankStickySel = _selectedIndex; _rankStickyDiff = _difficultyIndex;
        _rankingScale = 20f;
        _rankingAnimStart = _rankT;
        FxTweenTo(cap.TargetKey, () => _rankingScale, v => _rankingScale = v, 0.9f, 0.1f, FxLinear);

        FxStartTimer(0.1f, _ =>
        {
            if (_rankOld != null)
            {
                _fakeRankVisible = false;
                _sparksVisible = true; _sparksT = 0f;
            }
            Audio.PlaySound(_rankNew switch
            {
                ScoringRank.SHIT => "ranks/rankinbad",
                ScoringRank.PERFECT or ScoringRank.PERFECT_GOLD => "ranks/rankinperfect",
                _ => "ranks/rankinnormal"
            });
            _rankZoom = 1.3f;
            FxTweenTo(this, () => _rankZoom, v => _rankZoom = v, 1.5f, 0.3f, FxBackInOut);
            cap.X -= 10f;
            cap.Y -= 20f;
            FxTweenTo(this, () => _funnyZoom, v => _funnyZoom = v, 1.05f, 0.3f, FxElasticOut);
            cap.BodyAngle = -3f;
            FxTweenTo(cap.BodyKey, () => cap.BodyAngle, v => cap.BodyAngle = v, 0f, 0.5f, FxBackOut);
            FxIntervalShake(() => cap.BodyOff, v => cap.BodyOff = v, 0.3f, 1f / 30f, 0.1f, CAPSULE_FRAME_W, FxQuadOut, null);
        });

        FxStartTimer(0.4f, _ =>
        {
            FxTweenTo(this, () => _funnyZoom, v => _funnyZoom = v, 1f, 0.8f, FxSineIn);
            FxTweenTo(this, () => _rankZoom, v => _rankZoom = v, 1.2f, 0.8f, FxBackIn);
            float ox = SongsBaseX + 320.488f, oy = 235.6f;
            FxTweenTo(cap, () => cap.X, v => cap.X = v, ox - 7f, 0.8f + 0.5f, FxQuartIn);
            FxTweenTo(cap, () => cap.Y, v => cap.Y = v, oy - 80f, 0.8f + 0.5f, FxQuartIn);
        });

        FxStartTimer(0.6f, _ => RankAnimSlam(cap));
    }

    private void RankAnimSlam(CapState cap)
    {
        FxTweenTo(this, () => _rankBgA, v => _rankBgA = v, 0f, 0.5f, FxExpoIn);
        Audio.PlaySound(_rankNew switch
        {
            ScoringRank.SHIT => "ranks/loss",
            ScoringRank.GOOD => "ranks/good",
            ScoringRank.GREAT => "ranks/great",
            ScoringRank.EXCELLENT => "ranks/excellent",
            ScoringRank.PERFECT or ScoringRank.PERFECT_GOLD => "ranks/perfect",
            _ => "ranks/loss"
        });
        float ox = SongsBaseX + 320.488f, oy = 235.6f;
        FxTweenTo(cap.TargetKey, () => cap.TX, v => cap.TX = v, ox, 0.5f, FxExpoOut);
        FxTweenTo(cap.TargetKey, () => cap.TY, v => cap.TY = v, oy, 0.5f, FxExpoOut);

        FxStartTimer(0.5f, _ =>
        {
            _funnyShakeInt = 0.0045f; _funnyShakeDur = 0.35f;
            DjPlayFist(false);
            _rankZoom = 0.8f;
            _funnyZoom = 0.8f;
            FxTweenTo(this, () => _rankZoom, v => _rankZoom = v, 1f, 1f, FxElasticOut);
            FxTweenTo(this, () => _funnyZoom, v => _funnyZoom = v, 1f, 0.8f, FxElasticOut);

            int sel = _selectedIndex;
            for (int index = 0; index < _songs.Count; index++)
            {
                float dist = Math.Abs(index - sel) - 1;
                if (dist >= 5) continue;
                var c = Cap(index);
                if (index == sel)
                {
                    FxCancelTweensOf(c);
                    // capsule.fadeAnim(): evilTrail.color = ranking.rank colour (the SAVED rank refreshed by changeSelection)
                    var song = _songs[index];
                    string saved = song.IsRandom ? null : HighscoreManager.GetRank(song.Name, GetCurrentDifficulty(song));
                    if (saved != null && saved.Equals("LOSS", StringComparison.OrdinalIgnoreCase)) saved = "SHIT";
                    _vignetteCol = RankFreeplayColor(Enum.TryParse<ScoringRank>(saved?.Replace(' ', '_'), true, out var sr) ? sr : _rankNew);
                    _vignetteA = 1f;
                    FxTweenTo(_rankVignetteTex ?? (object)"vig", () => _vignetteA, v => _vignetteA = v, 0f, 0.6f, FxExpoOut);
                    c.DoLerp = false;
                    c.X = ox; c.Y = oy;
                    FxIntervalShake(() => new Vector2(c.X, c.Y), v => { c.X = v.X; c.Y = v.Y; }, 0.6f, 1f / 24f, 0.12f, CAPSULE_FRAME_W, FxQuadOut, () =>
                    {
                        c.DoLerp = true;
                        _rankCap = null;           // capsule.cameras = [funnyCam]
                        _rankBlockInput = false;   // uiStateMachine.transition(Idle)
                        RankPlayPreview();
                    });
                }
                else
                {
                    float d = dist;
                    FxStartTimer(d / 20f, __ =>
                    {
                        c.DoLerp = false;
                        c.BodyAngle = RandF(-10f + d * 2f, 10f - d * 2f);
                        FxTweenTo(c.BodyKey, () => c.BodyAngle, v => c.BodyAngle = v, 0f, 0.5f, FxBackOut);
                        FxIntervalShake(() => new Vector2(c.X, c.Y), v => { c.X = v.X; c.Y = v.Y; }, 0.6f, 1f / 24f, 0.12f / (d + 1f), CAPSULE_FRAME_W, FxQuadOut,
                            () => c.DoLerp = true);
                    });
                }
            }
        });

        FxStartTimer(2f, _ => _prepForNewRank = false);
    }

    /// <summary>playCurSongPreview(capsule): stop the current music, start the preview with its 0 -> volume fade-in.</summary>
    private void RankPlayPreview()
    {
        if (_songs.Count == 0) return;
        try
        {
            Audio.StopMusic();
            _pendingPreviewSong = null; _previewFadingOut = false;
            _lastPreviewIndex = _selectedIndex;
            _songPreviewPlaying = true;
            var s = _songs[_selectedIndex];
            PlaySongPreview(s.IsRandom ? null : s.Name);
        }
        catch { }
    }

    // ------------------------------------------------------------------ drawing
    /// <summary>Rank badges of the capsule on rankCamera (fakeRanking = old rank, ranking = new rank popping in at scale 20).</summary>
    private bool RankBadgeOverride(SpriteBatch sb, Vector2 anchor, bool isSelected, List<Action> additive)
    {
        if (!isSelected) return false;
        if (_rankCap == null)
        {
            if (!_rankingVisible || _rankStickySel != _selectedIndex || _rankStickyDiff != _difficultyIndex) { _rankingVisible = false; return false; }
        }
        Vector2 topLeft = anchor + new Vector2(420f, 41f);
        if (_fakeRankVisible && _rankOld != null)
        {
            string r = _rankOld.Value.ToString(); float at = _rankT - _fakeAnimStart;
            additive.Add(() => DrawRankBadge(sb, r, topLeft, 0.9f, 1f, true, at, null, BlendState.Additive, SamplerState.PointClamp));
        }
        if (_rankingVisible)
        {
            string r = _rankNew.ToString(); float at = _rankT - _rankingAnimStart; float sc = _rankingScale;
            additive.Add(() => DrawRankBadge(sb, r, topLeft, sc, 1f, true, at, null, BlendState.Additive, SamplerState.PointClamp));
        }
        return true;
    }

    private RenderTargetBinding[] _rankPrevTargets;

    /// <summary>Called at the start of Draw: when the rank fx are active, the whole Freeplay (funnyCam) renders into a target.</summary>
    private void RankFxBeginDraw()
    {
        if (!_rankFx) return;
        var gd = Game.GraphicsDevice;
        _funnyRT ??= new RenderTarget2D(gd, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT);
        _rankRT ??= new RenderTarget2D(gd, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT);
        _rankPrevTargets = gd.GetRenderTargets();
        gd.SetRenderTarget(_funnyRT);
        gd.Clear(Color.Black);
    }

    private static Matrix CamZoom(float zoom, Vector2 shake) =>
        Matrix.CreateTranslation(-FNFGame.SCREEN_WIDTH / 2f, -FNFGame.SCREEN_HEIGHT / 2f, 0f) * Matrix.CreateScale(zoom, zoom, 1f)
        * Matrix.CreateTranslation(FNFGame.SCREEN_WIDTH / 2f + shake.X, FNFGame.SCREEN_HEIGHT / 2f + shake.Y, 0f);

    /// <summary>Called at the end of Draw (no batch open): rankVignette (funnyCam), then composite funnyCam and rankCamera.</summary>
    private void RankFxEndDraw(SpriteBatch sb)
    {
        if (!_rankFx) return;
        var gd = Game.GraphicsDevice;
        // rankVignette: on funnyCam, scale 2 + updateHitbox at (0,0), blend ADD, colour = trail colour
        if (_vignetteA > 0f && _rankVignetteTex != null)
        {
            sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.Additive);
            sb.Draw(_rankVignetteTex, Vector2.Zero, null, new Color(_vignetteCol, _vignetteA), 0f, Vector2.Zero, 2f, SpriteEffects.None, 0f);
            sb.End();
        }

        // rankCamera content (capsuleToRank + sparks) at zoom 1
        gd.SetRenderTarget(_rankRT);
        gd.Clear(Color.Transparent);
        if (_rankCap != null && _songs.Count > 0)
        {
            sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);
            _drawingRankCam = true;
            try { DrawCapsules(sb); } finally { _drawingRankCam = false; }
            sb.End();
        }
        if (_sparksVisible)
        {
            sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.Additive);
            DrawSparks(sb, _sparksSheet, "sparks", new Vector2(517f, 134f), Color.White);
            DrawSparks(sb, _sparksAddSheet, "sparks add", new Vector2(498f, 116f), _rankOld != null ? RankFreeplayColor(_rankOld.Value) : Color.White);
            sb.End();
        }

        if (_rankPrevTargets != null && _rankPrevTargets.Length > 0) gd.SetRenderTargets(_rankPrevTargets); else gd.SetRenderTarget(null);
        gd.Clear(Color.Black);
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.Opaque, transformMatrix: CamZoom(_funnyZoom, _funnyShakeOff));
        sb.Draw(_funnyRT, Vector2.Zero, Color.White);
        sb.End();
        // rankCamera: rankBg (makeSolidColor 0xD3000000), capsule/sparks, then the camera fade
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.AlphaBlend, transformMatrix: CamZoom(_rankZoom, Vector2.Zero));
        if (_rankBgA > 0f)
            sb.Draw(Assets.Pixel, new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT), Color.Black * (0xD3 / 255f * _rankBgA));
        sb.Draw(_rankRT, Vector2.Zero, Color.White);
        sb.End();
        if (_rankFadeA > 0f)
        {
            sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend);
            sb.Draw(Assets.Pixel, new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT), Color.Black * _rankFadeA);
            sb.End();
        }
    }

    private void DrawSparks(SpriteBatch sb, SpriteSheet sheet, string anim, Vector2 pos, Color col)
    {
        var frames = sheet?.GetAnimation(anim);
        if (frames == null || frames.Count == 0 || sheet.Texture == null) return;
        var f = frames[Math.Clamp((int)(_sparksT * 24f), 0, frames.Count - 1)];
        float fw = f.FrameWidth > 0 ? f.FrameWidth : f.SourceRect.Width, fh = f.FrameHeight > 0 ? f.FrameHeight : f.SourceRect.Height;
        // scale 0.5 about the frame centre (no updateHitbox)
        Vector2 centre = pos + new Vector2(fw, fh) * 0.5f;
        sb.Draw(sheet.Texture, centre + (f.Offset - new Vector2(fw, fh) * 0.5f) * 0.5f, f.SourceRect, col, 0f, Vector2.Zero, 0.5f, SpriteEffects.None, 0f);
    }

    public float DevRankElapsed { get; private set; } = -1f;
    private string DevCap(int m) => m < _songs.Count ? $"c{m}={Cap(m).X:F2},{Cap(m).Y:F2},{Cap(m).SX * CAPSULE_SCALE:F3} " : "";
    public string DevRankInfo => _caps.Count == 0 ? "" : DevCap(0) + DevCap(1) + DevCap(2) + DevCap(4) +
        $"rk t={_rankT:F3} rz={_rankZoom:F4} fz={_funnyZoom:F4} bg={_rankBgA:F3} fade={_rankFadeA:F3} vig={_vignetteA:F3} fake={_fakeRankVisible} rank={_rankingVisible}/{_rankingScale:F2} sparks={_sparksVisible} " +
        (_selectedIndex >= 0 && _selectedIndex < _songs.Count ? $"cap={Cap(_selectedIndex).X:F2},{Cap(_selectedIndex).Y:F2} ang={Cap(_selectedIndex).BodyAngle:F2} body={Cap(_selectedIndex).BodyOff.X:F1},{Cap(_selectedIndex).BodyOff.Y:F1} " : "") +
        (_selectedIndex + 2 < _songs.Count ? $"n2={Cap(_selectedIndex + 2).X:F2},{Cap(_selectedIndex + 2).Y:F2},{Cap(_selectedIndex + 2).BodyAngle:F3} " : "") +
        $"dj={_djSprite?.CurrentAnimation}@{_djSprite?.FrameIndex} vol={Audio.MusicVolume:F3}";

    // ------------------------------------------------------------------ CapsuleText clip / scroll
    private int _capTextSelIdx = -1; private float _capTextSelTime;
    private static readonly RasterizerState ScissorRaster = new() { CullMode = CullMode.None, ScissorTestEnable = true };

    /// <summary>SongMenuItem.checkClip: 290, 245 with a rank, 210 with a rank and the favourite heart.</summary>
    private float CapsuleClipWidth(FreeplaySong song)
    {
        int t = 0;
        if (!song.IsRandom && !string.IsNullOrEmpty(HighscoreManager.GetRank(song.Name, GetCurrentDifficulty(song)))) t++;
        if (!song.IsRandom && HighscoreManager.Data.FavoriteSongs.Contains(song.Name)) t++;
        return t == 2 ? 210f : t == 1 ? 245f : 290f;
    }

    /// <summary>CapsuleText.initMove (selected + tooLong): wait 0.6 s, sineInOut 2 s right, 0.3 s, 2 s left, 0.3 s, ...;
    /// resetText on selection change and on confirm (flickerText).</summary>
    private float CapsuleTextScroll(float dist)
    {
        if (_phase == Phase.Confirmed) return 0f;
        float t = _capTextSelTime - 0.6f;
        if (t <= 0f) return 0f;
        float u = t % 4.6f;
        static float SineInOut(float x) => -MathF.Cos(MathF.PI * x) / 2f + 0.5f;
        if (u < 2f) return dist * SineInOut(u / 2f);
        if (u < 2.3f) return dist;
        if (u < 4.3f) return dist * (1f - SineInOut((u - 2.3f) / 2f));
        return 0f;
    }

    private void BeginTextClip(SpriteBatch sb, float x, float y, float w, float h)
    {
        sb.End();
        Game.GraphicsDevice.ScissorRectangle = new Rectangle((int)MathF.Round(x), (int)MathF.Floor(y), (int)MathF.Round(w), (int)MathF.Ceiling(h));
        sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied, rasterizerState: ScissorRaster);
    }

    private void EndTextClip(SpriteBatch sb)
    {
        sb.End();
        sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);
    }
}
