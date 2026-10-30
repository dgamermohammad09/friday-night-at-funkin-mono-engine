extern alias IRIS;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Newtonsoft.Json.Linq;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Gameplay;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// QT: Rewired (GameBanana 566597) sawblade / dodge mechanic, a native port of the mod's own scripts:
///   scripts/modules/playstate/dodgemechanic/SawbladeAndDodgeModule.hxc (module), SawBladeSprite.hxc (saw atlas),
///   scripts/events/SawbladeEvent.hxc (song event 'sawKB' -> Sequence of warnings/attacks),
///   scripts/modules/options/QtOptionCategory.hxc (mod options: qt-rewiredSawbladeMode, qt-rewiredDodgeKey).
/// The mod targets V-Slice 0.8.4 (Save.modOptions, FlxText, Sequence, HapticUtil); the reference 0.8.1 throws in
/// onSongLoaded/handleEvent, so the behaviour follows the scripts + 0.8.4 engine semantics:
///  - FlxTimer: a timer is first updated the frame after creation; callbacks run after the timer loop
///    (FlxTimerManager), so timers created in callbacks start next frame. Tweens update after timers.
///  - module 'hitWindow' is never assigned (null/1000 = 0 s timer) -> the bad-hit check runs the frame after the attack.
/// Active only when the mod defines the SawbladeAndDodgeModule class (the scripted module itself is not
/// instantiated by the generic runtime: NativeVsModules).
/// Controller: the mod is keyboard only (FlxG.keys); the port adds a pad binding (modOptions qt-rewiredDodgePad,
/// default RightStick = R3) so the mechanic is playable on Xbox.
/// </summary>
public partial class PlayScene
{
    /// <summary>Scripted module classes replaced by native ports (not instantiated by the V-Slice script runtime).</summary>
    internal static readonly HashSet<string> NativeVsModules = new(StringComparer.Ordinal) { "SawbladeAndDodgeModule" };

    public const string QsKeyMode = "qt-rewiredSawbladeMode", QsKeyDodge = "qt-rewiredDodgeKey", QsKeyPad = "qt-rewiredDodgePad";
    public static readonly int[] QsDefaultKeys = { 32, 0 };

    private sealed class QsTimer { public float Time, Counter; public Action Cb; public bool Done; }
    private sealed class QsTween { public float Dur, T, Delay; public Action<float> Set; public Func<float, float> Ease; public Action Done; public bool Finished; public object Target; }

    private bool _qsActive;          // module initialised for this song (mode != disabled)
    private bool _qsAllowed;         // song id starts with obliterated (dodge text)
    private string _qsMode = "default";
    private readonly List<QsTimer> _qsTimers = new();
    private readonly List<QsTween> _qsTweens = new();
    private AnimatedSprite _qsWarn, _qsSaw;
    private string _qsWarnMain, _qsSawMain;
    private Vector2 _qsWarnSize;
    private bool _qsWarnVisible, _qsSawVisible;
    private float _qsSawX, _qsSawY, _qsSawOffY;
    private string _qsSawAnim; private float _qsSawBlankT = -1f;
    private bool _qsSawFinishHook;
    private float _qsDim;            // dimOverlay alpha (used when the stage has no blackScreen prop)
    private string _qsText1, _qsText2; private float _qsText1A, _qsText2A; private float _qsTextY;
    private bool _qsText1Alive, _qsText2Alive;
    private QsTimer _qsSwapTimer;
    // SawbladeEvent state (activeEvent)
    private bool _qsHasEvent; private double _qsHitTime, _qsHitWindow = 160.0; private int _qsAmnt;
    // module state
    private bool _qsEventReady, _qsCanDie = true, _qsBfDodging, _qsBfCanDodge = true; private int _qsAnimSuffix;
    private bool _qsMayPause = true;
    internal bool QsMayPause => !_qsActive || _qsMayPause;
    // camGame flash / shake (FlxCamera.flash / shake)
    private Color _qsFlashColor; private float _qsFlashDur, _qsFlashA;
    private float _qsShakeI, _qsShakeT; private readonly Random _qsRng = new Random();
    // "dodged" popup (PopUpStuff.displayRating + FunkinNoteStyleScriptedExample.buildJudgementSprite)
    private sealed class QsPopup { public Texture2D Tex; public Vector2 C, V; public float Acc = 550f, Scale = 0.75f, ScaleT, A = 1f, Delay, FadeT = -1f; }
    private readonly List<QsPopup> _qsPopups = new();
    private readonly List<SoundEffectInstance> _qsSounds = new();
    internal bool QsSawDeath;
    private static readonly List<double> s_qsDevDodge = (Environment.GetEnvironmentVariable("FNF_QS_DODGE_AT") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(x => double.TryParse(x, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : double.NaN).Where(d => !double.IsNaN(d)).OrderBy(d => d).ToList();
    private static readonly bool s_qsLog = Environment.GetEnvironmentVariable("FNF_QSLOG") == "1";        // GameOverSubState.musicSuffix/blueBallSuffix = '-sawblade'

    internal static bool QsModPresent()
    {
        try { return VSliceScripts.ClassesExtending("Module").Any(c => c.Name == "SawbladeAndDodgeModule"); } catch { return false; }
    }

    // ---------------------------------------------------------------- mod options
    internal static string QsModeOption() => VsModOptions.GetValue(QsKeyMode) as string ?? "default";
    internal static int[] QsDodgeKeys()
    {
        var v = VsModOptions.GetValue(QsKeyDodge);
        var arr = VsArr(v);
        return arr != null && arr.Length > 0 ? arr : QsDefaultKeys;
    }
    private static int[] VsArr(object v)
    {
        if (v == null) return null;
        try
        {
            if (v is System.Collections.IEnumerable e && !(v is string)) return e.Cast<object>().Select(o => (int)VObj.D(o)).ToArray();
            var t = v.GetType();
            var len = t.GetProperty("length")?.GetValue(v) ?? t.GetField("length")?.GetValue(v);
            var get = t.GetMethod("__get");
            if (len != null && get != null) { int n = Convert.ToInt32(len); var r = new int[n]; for (int i = 0; i < n; i++) r[i] = (int)VObj.D(get.Invoke(v, new object[] { i })); return r; }
        }
        catch { }
        return null;
    }
    internal static Buttons QsPadButton() => Enum.TryParse(VsModOptions.GetValue(QsKeyPad) as string ?? "RightStick", out Buttons b) ? b : Buttons.RightStick;

    /// <summary>InputUtil.getKeyName(FlxKey) (FlxKey codes == Windows virtual-key codes == XNA Keys values).</summary>
    internal static string QsKeyName(int code)
    {
        if (code <= 0) return "---";
        if (code >= 65 && code <= 90) return ((char)code).ToString();
        if (code >= 48 && code <= 57) return ((char)code).ToString();
        switch (code)
        {
            case 32: return "Space"; case 13: return "Enter"; case 16: return "Shift"; case 17: return "Ctrl"; case 18: return "Alt";
            case 9: return "Tab"; case 8: return "Backspace"; case 37: return "Left"; case 38: return "Up"; case 39: return "Right"; case 40: return "Down";
            case 186: return ";"; case 187: return "="; case 188: return ","; case 189: return "-"; case 190: return "."; case 191: return "/"; case 192: return "`";
            case 219: return "["; case 220: return "\\"; case 221: return "]"; case 222: return "'";
        }
        if (code >= 112 && code <= 123) return "F" + (code - 111);
        return ((Keys)code).ToString();
    }

    // ---------------------------------------------------------------- lifecycle
    /// <summary>SawbladeAndDodgeModule.onSongLoaded.</summary>
    private void QsSongLoaded()
    {
        _qsActive = false; QsSawDeath = false;
        if (_boyfriend != null && _boyfriend.AnimSuffix == "-saw") _boyfriend.AnimSuffix = null;
        if (!QsModPresent() || _chart == null) return;
        _qsTimers.Clear(); _qsTweens.Clear(); _qsPopups.Clear();
        _qsEventReady = false; _qsCanDie = true; _qsBfDodging = false; _qsBfCanDodge = true; _qsMayPause = true; _qsHasEvent = false;
        string songId = VsSongId ?? "";
        _qsAllowed = songId.StartsWith("obliterated", StringComparison.Ordinal);
        _qsMode = QsModeOption();
        if (_qsMode == "disabled") return;
        _qsText1 = _qsText2 = null; _qsText1Alive = _qsText2Alive = false;
        if (_qsAllowed)
        {
            bool erect = VsVariation == "erect";
            _qsText1 = QsDodgeFormat() + " on third beat to dodge" + (erect ? "..." : "!");
            _qsText1A = 1f; _qsText1Alive = true;
            float h = MenuUI.FlxTextLineHeight(28) + 4f;
            _qsTextY = _downscroll ? 720f - h - 15f : 720f * 0.9f - 90f;   // healthBarBG.y - 90 (healthBarBG.y = FlxG.height * 0.9)
            if (erect)
            {
                _qsText2 = "But watch out, some attacks strike twice!"; _qsText2A = 0f; _qsText2Alive = true;
                _qsSwapTimer = QsStartTimer(5f, () =>
                {
                    if (_qsText1Alive) QsTweenF(2f, 0f, _qsText1A, 0f, a => _qsText1A = a, QsEaseSmoothStepInOut, null, "t1");
                    if (_qsText2Alive) QsTweenF(2f, 0f, _qsText2A, 1f, a => _qsText2A = a, QsEaseSmoothStepInOut, null, "t2");
                    _qsSwapTimer = null;
                });
            }
        }
        QsInitAssets();
        _qsActive = true;
        Console.WriteLine($"[QTSAW] active song={songId} mode={_qsMode} keys={string.Join(",", QsDodgeKeys())} pad={QsPadButton()} text='{_qsText1}' warn={_qsWarn != null} saw={_qsSaw != null}");
    }

    private string QsDodgeFormat()
    {
        if (Input != null && Input.LastDevice == InputManager.InputDevice.Controller) return "Press " + QsPadName(QsPadButton());
        return "Press " + QsKeyName(QsDodgeKeys()[0]).ToUpperInvariant();
    }
    internal static string QsPadName(Buttons b) => b switch
    {
        Buttons.RightStick => "R3", Buttons.LeftStick => "L3", Buttons.Back => "VIEW", Buttons.LeftShoulder => "LB", Buttons.RightShoulder => "RB",
        Buttons.LeftTrigger => "LT", Buttons.RightTrigger => "RT", _ => b.ToString().ToUpperInvariant()
    };

    private void QsInitAssets()
    {
        _qsWarn = QsLoadAtlas("saw_mechanic/warning", out _qsWarnMain);
        if (_qsWarn != null)
        {
            var b = _qsWarn.Sheet.WholeMainTimelineBounds ?? _qsWarn.Sheet.GetTimelineBounds(_qsWarnMain);
            _qsWarnSize = b.HasValue ? new Vector2((int)b.Value.W, (int)b.Value.H) : Vector2.Zero;
        }
        _qsWarnVisible = false;
        _qsSaw = QsLoadAtlas("saw_mechanic/saw_assets", out _qsSawMain);
        _qsSawVisible = false; _qsSawAnim = null; _qsSawOffY = 0f;
        var mid = _opponent != null ? _opponent.GetMidpoint() : Vector2.Zero;   // dad.getGraphicMidpoint()
        _qsSawX = mid.X - 50f; _qsSawY = mid.Y - 275f;
        _qsDim = 0f;
    }

    private AnimatedSprite QsLoadAtlas(string path, out string mainKey)
    {
        mainKey = null;
        try
        {
            var sheet = SpriteSheet.Load(Game, "images/" + path, preRenderComposites: true);
            if (sheet == null) { Console.WriteLine($"[QTSAW] atlas not found: {path}"); return null; }
            // getDefaultSymbol() = the document's main symbol (AN.SN) = the longest timeline in these atlases
            mainKey = sheet.Animations.OrderByDescending(kv => kv.Value?.Count ?? 0).Select(kv => kv.Key).FirstOrDefault();
            Console.WriteLine($"[QTSAW] atlas {path} main={mainKey} keys={string.Join("|", sheet.Animations.Keys)} frames={(mainKey != null ? sheet.Animations[mainKey].Count : 0)}");
            return new AnimatedSprite { Sheet = sheet, UseTimelineAnchoring = true };
        }
        catch (Exception ex) { Console.WriteLine($"[QTSAW] atlas {path}: {ex.Message}"); return null; }
    }

    // ---------------------------------------------------------------- FlxTimer / FlxTween emulation
    private QsTimer QsStartTimer(float time, Action cb) { var t = new QsTimer { Time = Math.Abs(time), Cb = cb }; _qsTimers.Add(t); return t; }
    private void QsTweenF(float dur, float delay, float from, float to, Action<float> set, Func<float, float> ease, Action done, object target)
    {
        if (target != null) foreach (var o in _qsTweens) if (Equals(o.Target, target)) o.Finished = true;   // a new tween on the same field wins
        _qsTweens.Add(new QsTween { Dur = Math.Max(dur, 1e-4f), Delay = delay, Set = v => set(from + (to - from) * v), Ease = ease ?? (x => x), Done = done, Target = target });
    }
    private static float SmoothStepInOut(float t) => t * t * (t * -2f + 3f);   // FlxEase.smoothStepInOut
    private static float QsEaseSmoothStepInOut(float t) => SmoothStepInOut(t);
    private static float QsEaseQuadInOut(float t) => t <= 0.5f ? t * t * 2f : 1f - (--t) * t * 2f;
    private static float QsEaseExpoOut(float t) => t == 1f ? 1f : 1f - (float)Math.Pow(2, -10 * t);

    /// <summary>Per-frame update (before PlayState.update logic): timers, tweens, sprites, input (module onUpdate).</summary>
    private void QsUpdate(float dt)
    {
        if (!_qsActive && _qsPopups.Count == 0 && _qsFlashA <= 0f && _qsShakeT <= 0f) return;
        if (s_qsLog && _qsTimers.Count + _qsTweens.Count > 0) Console.WriteLine($"[QTSAW-TICK] t={SongPositionSeconds * 1000:F0} dt={dt:F4} timers={_qsTimers.Count} tweens={_qsTweens.Count}");
        // FlxTimerManager.update: timers existing at the start of the loop; callbacks after the loop.
        var fired = new List<QsTimer>();
        foreach (var t in _qsTimers.ToList())
        {
            if (t.Done) continue;
            t.Counter += dt;
            if (t.Counter >= t.Time) { t.Done = true; fired.Add(t); }
        }
        _qsTimers.RemoveAll(t => t.Done);
        foreach (var t in fired) t.Cb?.Invoke();
        // FlxTweenManager.update
        foreach (var tw in _qsTweens.ToList())
        {
            if (tw.Finished) continue;
            float tdt = dt;
            if (tw.Delay > 0f) { tw.Delay -= dt; if (tw.Delay > 0f) continue; tdt = -tw.Delay; tw.Delay = 0f; }
            tw.T = Math.Min(tw.Dur, tw.T + tdt);
            tw.Set(tw.Ease(tw.T / tw.Dur));
            if (tw.T >= tw.Dur) { tw.Finished = true; tw.Done?.Invoke(); }
        }
        _qsTweens.RemoveAll(t => t.Finished);

        _qsWarn?.Update(dt);
        if (_qsSaw != null)
        {
            _qsSaw.Update(dt);
            // 'saw anim attack' = main-timeline indices 19..38 but the main timeline only has 34 frames: indices 34..38 draw
            // nothing (no keyframe) and the animation finishes after them (5 frames @ 24 fps).
            if (_qsSawAnim == "saw anim attack" && _qsSaw.Finished && _qsSawBlankT >= 0f) _qsSawBlankT += dt;
            if (_qsSawFinishHook && _qsSawAnim == "saw anim attack" && _qsSaw.Finished && _qsSawBlankT >= 5f / 24f)
            { _qsSawFinishHook = false; QsHideAllVisuals(); }
        }
        for (int i = _qsPopups.Count - 1; i >= 0; i--)
        {
            var p = _qsPopups[i];
            p.V.Y += p.Acc * dt * 0.5f; p.C += p.V * dt; p.V.Y += p.Acc * dt * 0.5f;   // FlxObject.updateMotion (velocity Verlet)
            p.ScaleT = Math.Min(0.5f, p.ScaleT + dt); p.Scale = 0.75f + (0.65f - 0.75f) * QsEaseExpoOut(p.ScaleT / 0.5f);
            if (p.Delay > 0f) p.Delay -= dt;
            else { p.FadeT += dt; p.A = 1f - Math.Clamp(p.FadeT / 0.2f, 0f, 1f); if (p.FadeT >= 0.2f) _qsPopups.RemoveAt(i); }
        }
        if (_qsFlashA > 0f) _qsFlashA = Math.Max(0f, _qsFlashA - dt / Math.Max(_qsFlashDur, 1e-4f));
        if (_qsShakeT > 0f)
        {
            _qsShakeT -= dt;
            if (_qsShakeT <= 0f) _bzShakeOffset = Vector2.Zero;
            else
            {
                float z = _defaultCamZoom * _cameraBopMultiplier;
                _bzShakeOffset = new Vector2(((float)_qsRng.NextDouble() * 2f - 1f) * _qsShakeI * 1280f * z, ((float)_qsRng.NextDouble() * 2f - 1f) * _qsShakeI * 720f * z);
            }
        }
        for (int i = _qsSounds.Count - 1; i >= 0; i--) if (_qsSounds[i] == null || _qsSounds[i].State == SoundState.Stopped) _qsSounds.RemoveAt(i);

        if (!_qsActive || _paused || _gameOver) return;
        // module onUpdate: dodgeBindJustPressed() -> bfDodge()
        if (QsDodgePressed()) QsBfDodge();
    }

    private bool QsDodgePressed()
    {
        if (!_qsHasEvent || Input == null) return false;   // activeEvent == null
        if (s_qsDevDodge.Count > 0 && SongPositionSeconds * 1000.0 >= s_qsDevDodge[0]) { s_qsDevDodge.RemoveAt(0); return true; }   // dev: FNF_QS_DODGE_AT=song ms list
        foreach (var k in QsDodgeKeys()) if (k > 0 && Input.IsPressed((Keys)k)) return true;
        return Input.IsGamePadPressed(QsPadButton());
    }

    /// <summary>SawbladeAndDodgeModule.onSongStart.</summary>
    private void QsSongStart() { if (!_qsActive) return; _qsEventReady = false; _qsCanDie = true; }

    // ---------------------------------------------------------------- SawbladeEvent.handleEvent
    private void QsOnSongEvent(GameEvent ev)
    {
        if (!_qsActive || ev == null || ev.Name != "sawKB") return;
        if (QsModeOption() == "disabled") return;
        _qsMayPause = false;
        float interval = (float)(_conductor?.Crochet ?? 0.5);   // Conductor.instance.beatLengthMs / 1000
        int amnt = 1;
        object nv = QsEventValue(ev.Value, "num_sawblades");
        if (nv is string s) amnt = s == "Double" ? 2 : s == "Triple" ? 3 : s == "Single" ? 1 : (int)VObj.D(s);
        else if (nv != null) amnt = (int)VObj.D(nv);
        _qsAmnt = amnt;
        double evMs = ev.Time * 1000.0;
        _qsHitTime = evMs + interval * 2 + (amnt == 3 ? interval : 0);
        _qsHitWindow = 160.0;   // Scoring.PBOT1_SHIT_THRESHOLD
        _qsHasEvent = true;     // dodgeModule.activeEvent = this
        Console.WriteLine($"[QTSAW] event t={evMs:F0} saws={amnt} interval={interval:F4}");
        double Pos() => SongPositionSeconds * 1000.0;
        var seq = new List<(float t, Action cb)>();
        switch (amnt)
        {
            case 1:
                seq.Add((0f, () => QsExecuteWarning(interval, amnt)));
                seq.Add((interval, () => { QsExecuteAttack(interval); _qsAnimSuffix = 0; _qsHitTime = Pos() + interval * 1000; }));
                seq.Add((interval * 2, () => { _qsMayPause = false; QsPlayWarningStep("attack-1"); _qsEventReady = false; }));
                break;
            case 2:
                seq.Add((0f, () => QsExecuteWarning(interval, amnt)));
                seq.Add((interval, () => { _qsHitTime = Pos() + interval * 1000; QsExecuteAttack(interval); _qsAnimSuffix = 0; }));
                seq.Add((interval * 2, () => { _qsMayPause = false; QsPlayWarningStep("attack-2-1"); QsExecuteAttack(interval); _qsHitTime = Pos() + interval * 1000; _qsAnimSuffix = 1; }));
                seq.Add((interval * 3, () => { _qsMayPause = false; QsPlayWarningStep("attack-2-2", true, 2); _qsEventReady = false; }));
                break;
            case 3:
                seq.Add((0f, () => QsExecuteWarning(interval, amnt)));
                seq.Add((interval * 2, () => { _qsMayPause = false; QsPlayWarningStep("attack-2-1", true, 1); }));
                seq.Add((interval * 3, () => { _qsMayPause = false; QsPlayWarningStep("attack-2-1", true, 1); _qsHitTime = Pos() + interval * 1000; _qsAnimSuffix = 0; QsExecuteAttack(interval); }));
                seq.Add((interval * 4, () => { _qsMayPause = false; QsPlayWarningStep("attack-2-2", true, 2); _qsHitTime = Pos() + interval * 1000; _qsAnimSuffix = 1; QsExecuteAttack(interval); }));
                seq.Add((interval * 5, () => { _qsMayPause = false; _qsHitTime = Pos() + interval * 1000; QsPlayWarningStep("attack-2-2", true, 3); _qsEventReady = false; }));
                break;
            default: Console.WriteLine($"[QTSAW] bad num_sawblades {nv}"); return;   // throw 'what the fuck'
        }
        foreach (var (t, cb) in seq) QsStartTimer(t, cb);   // new Sequence(data): one FlxTimer per entry, created now
    }

    private static object QsEventValue(object value, string key)
    {
        switch (value)
        {
            case JObject jo: { var t = jo[key]; return t == null ? null : t.Type == JTokenType.Integer ? t.Value<int>() : t.Type == JTokenType.Float ? t.Value<double>() : (object)t.ToString(); }
            case JToken jt when jt.Type == JTokenType.Integer: return jt.Value<int>();
            case System.Collections.IDictionary d: return d.Contains(key) ? d[key] : null;
            case VObj vo: return vo.TryGet(key, out var v) ? v : null;
            case string s: return s;
            case null: return null;
            default: return value is int || value is double || value is long ? value : null;
        }
    }

    // ---------------------------------------------------------------- module actions
    private void QsExecuteWarning(float interval, int saws)
    {
        string postfx = saws == 2 ? "-2" : "-1";
        QsDarken();
        QsPlayWarningStep("1-alert" + postfx);
        QsStartTimer(interval, () =>
        {
            if (_qsAmnt > 1) QsPlayWarningStep("2-alert" + postfx, true, 2);
            else QsPlayWarningStep("2-alert" + postfx);
        });
    }

    private StaticStageProp QsBlackScreen() => _staticProps.FirstOrDefault(p => p.Name == "blackScreen");
    private void QsDarken()
    {
        var bs = QsBlackScreen();
        if (bs != null) { float from = bs.Alpha; QsTweenF(0.35f, 0f, from, 0.3f, a => bs.Alpha = a, null, null, "dim"); }
        else { float from = _qsDim; QsTweenF(0.35f, 0f, from, 0.3f, a => _qsDim = a, null, null, "dim"); }
    }
    private void QsUndarken()
    {
        var bs = QsBlackScreen();
        if (bs != null) { float from = bs.Alpha; QsTweenF(0.35f, 0f, from, 0f, a => bs.Alpha = a, QsEaseQuadInOut, null, "dim"); }
        else { float from = _qsDim; QsTweenF(0.35f, 0f, from, 0f, a => _qsDim = a, QsEaseQuadInOut, null, "dim"); }
    }

    private void QsPlayWarningStep(string anim, bool showVfx = true, int suffix = 1)
    {
        if (anim.IndexOf("attack", StringComparison.Ordinal) == -1) QsPlaySound($"sounds/sawblades/alerts/alert-{suffix}.ogg");
        else if (showVfx && suffix == 3) QsPlaySound($"sounds/sawblades/alerts/alert-{suffix}.ogg");
        else QsPlaySound($"sounds/sawblades/attacks/attack-{suffix}.ogg");
        if (_qsWarn != null && showVfx)
        {
            _qsWarnVisible = true;
            string label = anim switch
            {
                "1-alert-1" => "alert 1 beat 1", "2-alert-1" => "alert 1 beat 2", "attack-1" => "alert 1 attack",
                "1-alert-2" => "alert 2 beat 1", "2-alert-2" => "alert 2 beat 2", "attack-2-1" => "alert 2 attack 1", "attack-2-2" => "alert 2 attack 2",
                _ => null
            };
            if (label != null && _qsWarn.Sheet.Animations.ContainsKey(label)) _qsWarn.PlayAnimation(label, true, false);
            else Console.WriteLine($"[QTSAW] warning anim missing: {anim} ({label})");
        }
    }

    private void QsPlaySound(string path)
    {
        try
        {
            var inst = VsAudio.Create(path);
            if (inst == null) { Console.WriteLine($"[QTSAW] sound not found: {path}"); return; }
            inst.Play(); _qsSounds.Add(inst);
        }
        catch (Exception ex) { Console.WriteLine($"[QTSAW] sound {path}: {ex.Message}"); }
    }

    private void QsDoSawAnimation()
    {
        if (_health <= 0f) return;
        if (_qsSaw != null && _qsSawMain != null)
        {
            _qsSaw.PlayRangeThenPause(_qsSawMain, 0, 18);   // addBySymbolIndices('saw anim alert', [0..18])
            _qsSawAnim = "saw anim alert";
        }
        _qsSawOffY = 0f;
        Input?.Rumble(0.6f, 0.2f);                         // HapticUtil.vibrateByPreset(spinVibrationPreset)
        _qsSawFinishHook = true;
        _qsWarnVisible = _qsWarn != null;
        _qsSawVisible = _qsSaw != null;
    }

    private void QsHideAllVisuals()
    {
        if (_health <= 0f) return;
        _qsSawVisible = false; _qsWarnVisible = false;
        QsUndarken();
    }

    private void QsExecuteAttack(float interval)
    {
        if (_health <= 0f) return;
        _qsEventReady = true; _qsBfDodging = false; _qsBfCanDodge = true;
        QsDoSawAnimation();
        QsStartTimer(interval, () =>
        {
            if (_qsSaw != null && _qsSawMain != null) { _qsSaw.PlayAnimationFromFrame(_qsSawMain, 19, false); _qsSawAnim = "saw anim attack"; _qsSawBlankT = 0f; }
            _qsSawOffY = -300f;
            QsShake(0.007f, 0.25f);
            Input?.Rumble(1.0f, 0.3f);
            _girlfriend?.PlayAnimationLocked("dodge");
            QsStartTimer(0f, () =>   // new FlxTimer().start(hitWindow / 1000): module hitWindow is null -> 0 s
            {
                Console.WriteLine($"[QTSAW] attack check t={SongPositionSeconds * 1000:F0} mayPause={_qsMayPause} canDie={_qsCanDie}");
                if (!_qsMayPause) QsOnBadHit();
                _qsMayPause = true;
                _qsEventReady = false;
            });
        });
    }

    private void QsOnGoodHit()
    {
        _health = Math.Min(HEALTH_MAX, _health + 0.20f);
        QsPopUpDodged();
        _qsMayPause = true;
        _qsBfDodging = false; _qsBfCanDodge = true;
        if (!_qsText1Alive) return;
        QsTweenF(1f, 0f, _qsText1A, 0f, a => _qsText1A = a, SmoothStepInOut, () => { _qsText1Alive = false; }, "t1");
        if (_qsText2Alive) QsTweenF(1f, 0f, _qsText2A, 0f, a => _qsText2A = a, SmoothStepInOut, () => { _qsText2Alive = false; }, "t2");
    }

    private void QsOnBadHit()
    {
        if (_botplay || _practiceMode) { QsOnGoodHit(); return; }
        if (!_qsCanDie) { Console.WriteLine("[QTSAW] player cant die rn"); _qsCanDie = true; _qsMayPause = true; return; }
        switch (QsModeOption())
        {
            case "instakill": QsInstantKill(); break;
            case "disabled": return;
            default: QsDamage(); break;
        }
        Input?.Rumble(1.0f, 0.3f);
        _qsMayPause = true;
    }

    private void QsDamage()
    {
        float nh = Math.Max(_health - HEALTH_MAX * 0.5f, HEALTH_MIN);
        _health = nh;
        Console.WriteLine($"[QTSAW] hit: damage -> health={nh:F3}");
        if (nh <= HEALTH_MIN) QsInstantKill();
        else
        {
            QsFlash(new Color(255, 0, 0), 0.3f);
            QsShake(0.008f, 0.3f);
            _boyfriend?.PlayAnimationLocked("sawHit-" + _qsAnimSuffix);
        }
    }

    private void QsInstantKill()
    {
        QsHideAllVisuals();
        if (_boyfriend != null && (_boyfriend.OriginalName == "bf-qt" || _boyfriend.Name == "bf-qt")) _boyfriend.AnimSuffix = "-saw";   // bf-qt setSuffix('-saw')
        QsFlash(new Color(255, 0, 0), 1.0f);
        QsSawDeath = true;   // GameOverSubState.musicSuffix = blueBallSuffix = '-sawblade'
        Console.WriteLine("[QTSAW] instakill");
        _health = -1f;
        // destroyAssets(): texts, sprites, isAllowed = false
        _qsWarn = null; _qsSaw = null; _qsWarnVisible = _qsSawVisible = false; _qsText1Alive = _qsText2Alive = false; _qsActive = false; _qsMayPause = true;
    }

    private void QsBfDodge()
    {
        if (!(_qsBfCanDodge && !_qsBfDodging) || QsModeOption() == "disabled") return;
        // performDodge: bf-qt playAnimation('dodge-N') ignores its suffix and forces canPlayOtherAnims
        if (_qsEventReady && _boyfriend != null)
        {
            string suf = _boyfriend.AnimSuffix; _boyfriend.AnimSuffix = null; _boyfriend.AnimLocked = false;
            _boyfriend.PlayAnimationLocked("dodge-" + _qsAnimSuffix);
            _boyfriend.AnimSuffix = suf;
        }
        // checkHitTiming
        double diff = SongPositionSeconds * 1000.0 - _qsHitTime;
        Console.WriteLine($"[QTSAW] dodge press t={SongPositionSeconds * 1000:F0} hitTime={_qsHitTime:F0} diff={diff:F0} ready={_qsEventReady}");
        if (Math.Abs(diff) <= _qsHitWindow) { _qsCanDie = false; QsOnGoodHit(); }
    }

    private void QsFlash(Color c, float dur) { _qsFlashColor = c; _qsFlashDur = dur; _qsFlashA = 1f; }
    private void QsShake(float intensity, float dur) { if (!FNF_MonoGame.Engine.PsychEngine.ClientPrefs.Data.CamShakes) return; _qsShakeI = intensity; _qsShakeT = dur; }

    /// <summary>playState.popUpScore("dodged"): PopUpStuff.displayRating with the mod's 'funkin' note style override.</summary>
    private void QsPopUpDodged()
    {
        var tex = VsAssets.LoadTexture("images/ui/popup/funkin/dodged.png");
        if (tex == null) { Console.WriteLine("[QTSAW] dodged popup texture missing"); return; }
        var p = new QsPopup { Tex = tex };
        p.C = new Vector2(1280f * 0.474f + _popUpOffsets.X, 720f * 0.45f - 60f + _popUpOffsets.Y);   // x - w/2 + w/2 (updateHitbox at scale .75)
        p.V = new Vector2(-_qsRng.Next(0, 11), -_qsRng.Next(140, 176));
        p.Delay = (float)(_conductor?.Crochet ?? 0.5);
        p.FadeT = 0f;
        _qsPopups.Add(p);
    }

    // ---------------------------------------------------------------- drawing
    /// <summary>World pass: saw sprite (stage.add, zIndex 299, shader = dad.shader).</summary>
    private void QsDrawWorld(SpriteBatch sb, int minZ, int maxZ)
    {
        if (_qsSaw == null || !_qsSawVisible || 299 < minZ || 299 >= maxZ) return;
        if (_qsSawAnim == "saw anim attack" && _qsSaw.Finished) return;   // indices past the timeline end: empty frames
        var sp = WorldToScreen(_qsSawX, _qsSawY - _qsSawOffY, 1f, 1f);
        bool adj = BeginAdjust(sb, GetCharacterAdjust(1));
        _qsSaw.Position = sp; _qsSaw.Scale = Vector2.One; _qsSaw.Tint = Color.White; _qsSaw.Effects = SpriteEffects.None; _qsSaw.Rotation = 0f;
        _qsSaw.Draw(sb);
        EndAdjust(sb, adj);
    }

    /// <summary>camHUD members below the health bar: camGame flash, dimOverlay (z 632), warningVfx (z 633).</summary>
    private void QsDrawHudBack(SpriteBatch sb)
    {
        if (_qsFlashA > 0f) sb.Draw(Assets.Pixel, new Rectangle(-200, -200, 1680, 1120), new Color(_qsFlashColor, _qsFlashA));
        if (!_qsActive) return;
        if (_qsDim > 0f) sb.Draw(Assets.Pixel, new Rectangle(0, 0, 1280, 720), new Color(Color.Black, _qsDim));
        if (_qsWarn != null && _qsWarnVisible)
        {
            _qsWarn.Position = new Vector2((float)Math.Floor((1280f - _qsWarnSize.X) / 2f), (float)Math.Floor((720f - _qsWarnSize.Y) / 2f));   // screenCenter()
            _qsWarn.Scale = Vector2.One; _qsWarn.Tint = Color.White; _qsWarn.Effects = SpriteEffects.None; _qsWarn.Rotation = 0f;
            _qsWarn.Draw(sb);
        }
    }

    /// <summary>camHUD members above the health bar: comboPopUps 'dodged' (z 900), dodgeText (z 999/998).</summary>
    private void QsDrawHudFront(SpriteBatch sb)
    {
        foreach (var p in _qsPopups)
        {
            var o = new Vector2(p.Tex.Width / 2f, p.Tex.Height / 2f);
            sb.Draw(p.Tex, p.C, null, new Color(Color.White, Math.Clamp(p.A, 0f, 1f)), 0f, o, p.Scale, SpriteEffects.None, 0f);
        }
        VsDrawTexts(sb, false);   // script FlxTexts on camHUD
        if (!_qsActive) return;
        QsDrawText(sb, _qsText2, _qsText2Alive ? _qsText2A : 0f);
        QsDrawText(sb, _qsText1, _qsText1Alive ? _qsText1A : 0f);
    }

    private void QsDrawText(SpriteBatch sb, string text, float alpha)
    {
        if (string.IsNullOrEmpty(text) || alpha <= 0.001f) return;
        float w = MenuUI.FlxTextWidth(28, text);
        float x = (float)Math.Floor((1280f - w) / 2f);   // screenCenter()
        var black = new Color(Color.Black, alpha); var red = new Color(new Color(255, 0, 0), alpha);
        for (int oy = -1; oy <= 1; oy++)                  // FlxTextBorderStyle.OUTLINE, borderSize 1
            for (int ox = -1; ox <= 1; ox++)
                if (ox != 0 || oy != 0) MenuUI.FlxText(sb, 28, text, x + ox, _qsTextY + oy, black);
        MenuUI.FlxText(sb, 28, text, x, _qsTextY, red);
    }

    private void QsDrawGameOverFlash(SpriteBatch sb)
    {
        if (_qsFlashA <= 0f) return;
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
        sb.Draw(Assets.Pixel, new Rectangle(0, 0, 1280, 720), new Color(_qsFlashColor, _qsFlashA));
        sb.End();
    }

    /// <summary>Dev info for [SHOTINFO].</summary>
    public string QsDevInfo() => !_qsActive && _qsPopups.Count == 0 ? null
        : $"[QTSAW-STATE] warn={_qsWarnVisible}:{_qsWarn?.CurrentAnimation}:{_qsWarn?.FrameIndex} saw={_qsSawVisible}:{_qsSawAnim}:{_qsSaw?.FrameIndex} offY={_qsSawOffY} sawPos=({_qsSawX:F1},{_qsSawY:F1}) dim={QsBlackScreen()?.Alpha ?? _qsDim:F2} health={_health:F3} mayPause={_qsMayPause} popups={_qsPopups.Count} text1={_qsText1A:F2} bf={_boyfriend?.CurrentAnimation} gf={_girlfriend?.CurrentAnimation}";
}

public partial class PlayScene
{
    /// <summary>PlayState.comboPopUps.offsets (PopUpStuff.offsets), e.g. QtStageKiller.onSongLoaded sets [55, 300].</summary>
    public Vector2 VsPopUpOffsets { get => _popUpOffsets; set => _popUpOffsets = value; }
}

/// <summary>PlayState.instance.comboPopUps facade (offsets only).</summary>
public sealed class VsComboPopUps : VObj
{
    private readonly PlayScene _ps; public VsComboPopUps(PlayScene ps) { _ps = ps; }
    public override bool TryGet(string f, out object v)
    {
        if (f == "offsets") { var o = _ps.VsPopUpOffsets; v = new IRIS::Array<object>(new object[] { (int)o.X, (int)o.Y }); return true; }
        if (f == "zIndex") { v = 900; return true; }
        v = null; return false;
    }
    public override bool TrySet(string f, object v)
    {
        if (f != "offsets") return false;
        var a = v is IRIS::Array<object> arr ? new[] { arr.__get(0), arr.__get(1) } : null;
        if (a != null) _ps.VsPopUpOffsets = new Vector2((float)D(a[0]), (float)D(a[1]));
        Console.WriteLine($"[VSLICE-SCRIPT] comboPopUps.offsets = {_ps.VsPopUpOffsets}");
        return true;
    }
}
