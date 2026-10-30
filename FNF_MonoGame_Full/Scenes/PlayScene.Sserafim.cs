using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using FontStashSharp;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Gameplay;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// SPAGHETTI (LE SSERAFIM collab) - port of the original scripts:
///   scripts/stages/sserafim.hxc (SserafimStage: girls visibility/singing, SserafimShader darken + truck/pulse lights,
///     dust FlxBackdrops, PerspectiveSprite floor, door kicks, intro cutscene, ending "see you" cards),
///   scripts/stages/props/{PerspectiveSprite,SserafimCutsceneSprite,SserafimGfSprite,SserafimBfSprite}.hxc,
///   scripts/characters/sserafim-*.hxc (sakura joint/bf1/bf2 note kinds, gf "-beautiful" suffix),
///   source/funkin/graphics/shaders/SserafimShader.hx (Shaders/Sserafim.fx).
/// NOT ported: SserafimLipSyncSprite (mouth atlas inserted into the character timelines, 4103 frames) and haptics.
/// </summary>
public partial class PlayScene
{
    private bool _ssfActive;
    private bool _ssfInitDone;
    private static bool _ssfLog = Environment.GetEnvironmentVariable("FNF_SSFLOG") == "1";

    // ---- SserafimShader state (stageShader / characterShader share everything but isCharacter)
    private sealed class SsfShaderState
    {
        public float Darken, PulseStrength, TruckStrength, Hue, Sat, Bright, Contrast;
        public Vector3 PulseColor = Vector3.One;
        public void SetAdjust(float b, float h, float c, float s) { Bright = b; Hue = h; Contrast = c; Sat = s; }
    }
    private readonly SsfShaderState _ssfStage = new(), _ssfChar = new();
    private Effect _ssfEffect;
    private bool _ssfEffectTried;
    private BlendState _ssfScreenBlend;

    // ---- extra characters (OTHER) and girls bookkeeping
    private Character _ssfYunjin, _ssfChaewon, _ssfEunchae;
    private readonly bool[] _ssfSinging = new bool[6];   // yunjin, kazuha(dad), chaewon, eunchae, sakura(bf), gf: true = BF type
    private static readonly bool[] SsfBaseVisible = { true, false, false, false, false, false };
    private static readonly bool[] SsfBaseSinging = { false, false, false, false, false, false };
    private float _ssfYunjinScroll = 0.95f, _ssfChaewonScroll = 0.95f, _ssfEunchaeScroll = 0.97f;

    /// <summary>Stage-JSON characters.*.scroll for the three main characters (Stage.addCharacter scrollFactor).</summary>
    private Vector2 _bfCharScroll = Vector2.One, _dadCharScroll = Vector2.One, _gfCharScroll = Vector2.One;

    // ---- props
    private sealed class SsfProp : StaticStageProp { public Action<SpriteBatch> DrawFn; }
    private StaticStageProp SsfNamed(string n) => _staticProps.FirstOrDefault(p => p.Name == n && p is not SsfProp);
    private sealed class SsfDust { public SsfProp Prop; public Texture2D Tex; public float X, Y, Vx, Scale, Sf, Alpha; public Color Col; public bool Visible = true; }
    private readonly List<SsfDust> _ssfDust = new();
    private Texture2D _ssfFloorTex, _ssfFloorCutTex, _ssfFloorCur;
    private SsfProp _ssfFloorProp;
    private AnimatedSprite _ssfCutSpr, _ssfGfSpr, _ssfBfSpr;
    private bool _ssfCutVis, _ssfGfVis, _ssfBfVis;
    private Texture2D _ssfEnd1, _ssfEnd2;
    private bool _ssfEnd1Vis, _ssfEnd2Vis;

    // ---- lights
    private bool _ssfLightsEnabled;
    private Vector3[] _ssfLightColors = new Vector3[0];
    private float[] _ssfLightDurations = new float[0], _ssfLightIntensities = new float[0];

    // ---- timers / tweens (FlxTimer / FlxTween with cancelTweensOf per target)
    private sealed class SsfTw { public object Target; public float Dur, T; public Func<float, float> Ease; public Action<float> Set; public Action Done; public bool Cancelled; }
    private readonly List<SsfTw> _ssfTweens = new();
    private readonly List<(float T, Action A, bool Cutscene)> _ssfTimers = new();
    private float _ssfClock;
    private void SsfAfter(float t, Action a, bool cutscene = false) => _ssfTimers.Add((_ssfClock + t, a, cutscene));
    private void SsfTween(object target, float dur, Func<float, float> ease, Action<float> set, Action done = null)
    {
        var tw = new SsfTw { Target = target, Dur = dur, Ease = ease ?? (t => t), Set = set, Done = done };
        if (dur <= 0f) { set(1f); done?.Invoke(); return; }
        _ssfTweens.Add(tw);
    }
    private void SsfCancel(object target) { foreach (var t in _ssfTweens) if (ReferenceEquals(t.Target, target)) t.Cancelled = true; }

    // ---- camera overlays: camGame.flash / camGame.fade, camCutscene.fade
    private Color _ssfGameFxColor; private float _ssfGameFxAlpha, _ssfGameFxFrom, _ssfGameFxTo, _ssfGameFxDur, _ssfGameFxT; private bool _ssfGameFxOn;
    private float _ssfCutFadeAlpha, _ssfCutFadeFrom, _ssfCutFadeTo, _ssfCutFadeDur, _ssfCutFadeT; private bool _ssfCutFadeOn;
    private void SsfCamGameFx(Color c, float dur, float from, float to) { _ssfGameFxColor = c; _ssfGameFxFrom = from; _ssfGameFxTo = to; _ssfGameFxDur = dur; _ssfGameFxT = 0f; _ssfGameFxAlpha = from; _ssfGameFxOn = true; }

    // ---- cutscene
    private bool _ssfCutscene;              // countdown blocked by the intro
    private bool _ssfCutsceneSkipped, _ssfCanSkip, _ssfSkipTextShown;
    private float _ssfSkipTextAlpha;
    private SoundEffectInstance _ssfCutsceneSound;
    private bool _ssfIsBeautiful;
    private bool _ssfEnding;
    private bool _ssfHideIconP2;
    private Vector2 _iconP2Offset, _iconP1Offset;   // HealthIcon.iconOffset (healthIcon.offsets / SetHealthIcon)

    private Effect SsfEffect
    {
        get
        {
            if (_ssfEffect != null || _ssfEffectTried) return _ssfEffect;
            _ssfEffectTried = true;
            try
            {
                string path = Assets.ResolvePath("shaders/Sserafim.mgfx") ?? System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot, "shaders", "Sserafim.mgfx");
                if (global::FNF_MonoGame.Engine.FsCI.FileExists(path)) _ssfEffect = new Effect(Game.GraphicsDevice, global::FNF_MonoGame.Engine.FsCI.ReadAllBytes(path));
                else Console.WriteLine("[SSF] Sserafim.mgfx not found");
            }
            catch (Exception ex) { Console.WriteLine($"[SSF] shader load failed: {ex.Message}"); }
            return _ssfEffect;
        }
    }

    /// <summary>Called from LoadDialogue (the moment the countdown would start): stage init + onCountdownStart.</summary>
    private bool SsfOnCountdownStart()
    {
        if (!string.Equals(_chart?.Stage, "sserafim", StringComparison.OrdinalIgnoreCase)) return false;
        SsfInit();
        bool retry = s_w6RetryOf != null && string.Equals(s_w6RetryOf, _songName, StringComparison.OrdinalIgnoreCase);
        s_w6RetryOf = null;
        if (retry)
        {
            // hasPlayedCutscene survives onSongRetry -> playCutsceneFromRestart(); the countdown is not cancelled
            SsfPlayCutsceneFromRestart();
            _ssfHideIconP2 = true; // onSongRetry: iconP2.visible = false
            return false;
        }
        // !hasPlayedCutscene: event.cancel(); camHUD.visible = false; setCutsceneVisibility(true); introCutscene()
        _ssfCutscene = true;
        _dialogueActive = true;
        _w6HudHidden = true;
        SsfSetCutsceneVisibility(true);
        SsfIntroCutscene();
        return true;
    }

    private Character SsfMakeChar(string id, float baseX, float baseY, float sf, int z)
    {
        Character c;
        try { c = CreatePsychExtraCharacter(0, 0, id, false); }
        catch (Exception ex) { Console.WriteLine($"[SSF] char {id} failed: {ex.Message}"); return null; }
        var o = c.GetCharacterOrigin();
        c.Position = new Vector2(baseX - o.X, baseY - o.Y);   // setPosition(base - characterOrigin)
        var prop = new SsfProp { Name = id, ZIndex = z, Visible = true, Alpha = 1f, ScaleX = 1f, ScaleY = 1f, ScrollX = sf, ScrollY = sf };
        prop.DrawFn = sb => { if (c.Visible) DrawStageCharacter(sb, 1, c, WorldToScreen(c.Position.X, c.Position.Y, sf, sf)); };
        _staticProps.Add(prop);
        if (_ssfLog) Console.WriteLine($"[SSF] {id} pos={c.Position} origin={o} w={c.Width} h={c.Height}");
        return c;
    }

    private void SsfInit()
    {
        if (_ssfInitDone) return;
        _ssfInitDone = true;
        _ssfActive = true;
        SsfReadCharScroll();
        _ssfScreenBlend = new BlendState
        {
            ColorSourceBlend = Blend.One, ColorDestinationBlend = Blend.InverseSourceColor, ColorBlendFunction = BlendFunction.Add,
            AlphaSourceBlend = Blend.One, AlphaDestinationBlend = Blend.InverseSourceAlpha, AlphaBlendFunction = BlendFunction.Add,
        };
        // addProp(): stageShader on every prop except the lights / solidCover; blends + colour
        foreach (var p in _staticProps)
        {
            switch (p.Name)
            {
                case "truckLight1": p.Blend = null; break;            // blend 12 SCREEN (drawn by SsfDrawStatic)
                case "backLightColor": p.OverlayColor = new Color(0xCC, 0x33, 0x00); break; // blend 12 SCREEN, color 0xFFCC3300
                case "backLightWhite": p.Blend = "add"; break;        // blend 0 = ADD
            }
        }
        // buildStage(): perspective floor (zIndex 11), dust backdrops (zIndex 2000)
        _ssfFloorTex = Assets.LoadTexture("game/stages/sserafim/floor.png");
        _ssfFloorCutTex = Assets.LoadTexture("game/stages/sserafim/cutscene/floor-cutscene.png");
        _ssfFloorCur = _ssfFloorTex;
        _ssfFloorProp = new SsfProp { Name = "perspectiveFloor", ZIndex = 11, Visible = true, Alpha = 1f, ScaleX = 1f, ScaleY = 1f, ScrollX = 1f, ScrollY = 1f, DrawFn = SsfDrawFloor };
        _staticProps.Add(_ssfFloorProp);
        SsfAddDust("dustMid", -650, -200, 1.1f, 1.5f, 0.8f, 350f, new Color(0x98, 0x84, 0x7d));
        SsfAddDust("dustBack", -650, -250, 1.15f, 1.5f, 0.9f, -300f, new Color(0x8b, 0x6c, 0x63));
        SsfAddDust("dustMid", -650, -400, 1.2f, 2f, 0.8f, -200f, new Color(0x6e, 0x64, 0x5c));
        SsfAddDust("dustBack", -650, -1300, 1.25f, 3.5f, 0.9f, -150f, new Color(0x88, 0x6a, 0x60));
        // the three extra girls (CharacterType.OTHER), zIndex 1000
        _ssfYunjin = SsfMakeChar("sserafim-yunjin", -621, 154, _ssfYunjinScroll, 1000);
        _ssfChaewon = SsfMakeChar("sserafim-chaewon", 687, 98, _ssfChaewonScroll, 1000);
        _ssfEunchae = SsfMakeChar("sserafim-eunchae", 770, 675, _ssfEunchaeScroll, 1000);
        SsfSetGirlsVisible(SsfBaseVisible);
        SsfSetGirlsSinging(SsfBaseSinging);
        _ssfLightsEnabled = false;
        if (_girlfriend != null) _girlfriend.Visible = SsfBaseVisible[5];
        // createCutsceneSprites()
        _ssfCutSpr = SsfAtlas("game/stages/sserafim/cutscene/cutsceneMain", -395, 10, 0.94f, 25, () => _ssfCutVis);
        _ssfGfSpr = SsfAtlas("game/stages/sserafim/cutscene/gfGetUp", 655, -104, 0.95f, 25, () => _ssfGfVis);
        _ssfBfSpr = SsfAtlas("game/stages/sserafim/cutscene/bfGetUp", 1220, 531, 0.99f, 305, () => _ssfBfVis);
        _ssfEnd1 = Assets.LoadTexture("game/stages/sserafim/end/end1.png");
        _ssfEnd2 = Assets.LoadTexture("game/stages/sserafim/end/end2.png");
        _ssfStage.SetAdjust(0, 0, 0, 0); _ssfChar.SetAdjust(0, 0, 0, 0);
        _staticProps = _staticProps.OrderBy(p => p.ZIndex).ToList(); // Stage.refresh (stable)
        // onUpdate (first frame): hideOpponentStrumline(); comboPopUps.offsets = [9999, -50]
        for (int i = 0; i < 4; i++) StrumMods[i].Visible = false;
        _popUpOffsets = new Vector2(9999, -50);
        // HealthIcon: healthIcon.id / offsets from the character JSON (kazuha: offsets [0, 5000] = off screen)
        _iconP2Offset = new Vector2(0, 5000);
        var kaz = Assets.LoadTexture("images/icons/icon-kazuha.png"); if (kaz != null && kaz != Assets.Pixel) _opponentIcon = kaz;
        var bfi = Assets.LoadTexture("images/icons/icon-bf.png"); if (bfi != null && bfi != Assets.Pixel) _playerIcon = bfi;
        Console.WriteLine($"[SSF] sserafim stage init: props={_staticProps.Count} shader={(SsfEffect != null)}");
    }

    private void SsfAddDust(string img, float x, float y, float sf, float scale, float alpha, float vx, Color col)
    {
        var tex = Assets.LoadTexture($"game/stages/sserafim/dust/{img}.png");
        if (tex == null || tex == Assets.Pixel) return;
        var d = new SsfDust { Tex = tex, X = x, Y = y, Sf = sf, Scale = scale, Alpha = alpha, Vx = vx, Col = col };
        d.Prop = new SsfProp { Name = "dust" + (_ssfDust.Count + 1), ZIndex = 2000, Visible = true, Alpha = 1f, ScaleX = 1f, ScaleY = 1f, ScrollX = sf, ScrollY = sf };
        d.Prop.DrawFn = sb => SsfDrawDust(sb, d);
        _staticProps.Add(d.Prop);
        _ssfDust.Add(d);
    }

    private AnimatedSprite SsfAtlas(string path, float x, float y, float sf, int z, Func<bool> visible)
    {
        SpriteSheet sheet = null;
        try { sheet = SpriteSheet.Load(Game, path, preRenderComposites: true, deferComposites: true); } catch (Exception ex) { Console.WriteLine($"[SSF] atlas {path}: {ex.Message}"); }
        if (sheet == null) return null;
        var spr = new AnimatedSprite { Sheet = sheet, UseTimelineAnchoring = true, Position = new Vector2(x, y), Scale = Vector2.One };
        var prop = new SsfProp { Name = Path.GetFileName(path), ZIndex = z, Visible = true, Alpha = 1f, ScaleX = 1f, ScaleY = 1f, ScrollX = sf, ScrollY = sf };
        prop.DrawFn = sb =>
        {
            if (!visible() || spr.CurrentAnimation == null) return;
            var keep = spr.Position;
            spr.Position = WorldToScreen(x, y, sf, sf);
            bool b = SsfBeginShader(sb, _ssfChar, true);
            spr.Draw(sb);
            EndAdjust(sb, b);
            spr.Position = keep;
        };
        _staticProps.Add(prop);
        return spr;
    }

    /// <summary>play("") = default symbol = the whole main timeline; the port names it after AN.SN.</summary>
    private static string SsfMainAnim(AnimatedSprite s)
    {
        const string root = "cutsceneMain"; // port names the label-less main timeline after AN.N
        if (s?.Sheet == null) return root;
        if (s.Sheet.Animations.ContainsKey(root) || s.Sheet.CompositeAnimations.ContainsKey(root) || s.Sheet.RawCompositeData.ContainsKey(root)) return root;
        Console.WriteLine("[SSF] cutsceneMain anims: " + string.Join(" | ", s.Sheet.Animations.Keys.Take(12)));
        return root;
    }

    // ------------------------------------------------------------------ girls
    private Character SsfGirl(int i) => i switch { 0 => _ssfYunjin, 1 => _opponent, 2 => _ssfChaewon, 3 => _ssfEunchae, 4 => _boyfriend, 5 => _girlfriend, _ => null };

    private void SsfSetGirlsVisible(bool[] v)
    {
        if (v == null || v.Length < 5) return;
        for (int i = 0; i < 5; i++) { var c = SsfGirl(i); if (c != null) c.Visible = v[i]; }
    }

    private void SsfSetGirlsSinging(bool[] s)
    {
        if (s == null || s.Length < 5) return;
        for (int i = 0; i < 6 && i < s.Length; i++) _ssfSinging[i] = s[i];
        if (s.Length < 6) _ssfSinging[5] = false; // singingArray[5] out of range -> null -> false
    }

    /// <summary>BaseCharacter.onNoteHit dispatched to every stage character (all Spaghetti notes are player notes).
    /// Returns true when handled (replaces the port's _boyfriend.Sing).</summary>
    private bool SsfNoteHit(int lane, string kind)
    {
        if (!_ssfActive) return false;
        for (int i = 0; i < 6; i++)
        {
            var c = SsfGirl(i);
            if (c == null) continue;
            if (i == 4)
            {
                // sserafim-sakura.hxc onNoteHit: note kinds play their suffixed anims regardless of characterType
                switch (kind)
                {
                    case "sakura-joint": c.SingSuffix(lane, "joint"); continue;
                    case "sakura-bf1": c.SingSuffix(lane, "bf1"); continue;
                    case "sakura-bf2": c.SingSuffix(lane, "bf2"); continue;
                }
            }
            if (_ssfSinging[i]) c.Sing(lane, null);
        }
        return true;
    }

    private static readonly string[] SsfDirs = { "LEFT", "DOWN", "UP", "RIGHT" };
    /// <summary>BaseCharacter.onNoteMiss for every stage character + sserafim-sakura.hxc miss overrides.</summary>
    private bool SsfNoteMiss(int lane, string kind)
    {
        if (!_ssfActive) return false;
        for (int i = 0; i < 6; i++)
        {
            var c = SsfGirl(i);
            if (c == null) continue;
            if (i == 4)
            {
                if (kind == "sakura-joint") { c.PlayAnimation($"sing{SsfDirs[lane & 3]}miss-joint", true); continue; }
                // case "sakura-bf1" || "sakura-bf2": hscript evaluates the || first -> matches nothing (falls to super)
            }
            if (_ssfSinging[i]) c.Miss(lane);
        }
        return true;
    }

    // ------------------------------------------------------------------ song events
    private void SsfOnSongEvent(string name, object value)
    {
        if (!_ssfActive || name == null || !name.StartsWith("sserafim")) return;
        var v = value as Newtonsoft.Json.Linq.JToken;
        bool[] BoolArr(string k) => v?[k] is Newtonsoft.Json.Linq.JArray a ? a.Select(x => (bool)x).ToArray() : null;
        float F(string k, float d = 0f) => v?[k] != null && v[k].Type != Newtonsoft.Json.Linq.JTokenType.Null ? (float)v[k] : d;
        bool B(string k) => v?[k] != null && v[k].Type == Newtonsoft.Json.Linq.JTokenType.Boolean && (bool)v[k];
        if (_ssfLog) Console.WriteLine($"[SSF] event {name} {v?.ToString(Newtonsoft.Json.Formatting.None)} at {SongPositionSeconds:F3}");
        switch (name)
        {
            case "sserafimShow": SsfSetGirlsVisible(BoolArr("visible")); break;
            case "sserafimSing": SsfSetGirlsSinging(BoolArr("singing")); break;
            case "sserafimDark": SsfSetDarken(F("amount"), F("duration")); break;
            case "sserafimLights": SsfFlashTruckLights(F("amount"), F("duration")); break;
            case "sserafimCover": SsfSetCover(B("visible")); break;
            case "sserafimFlash": SsfCamGameFx(Color.White, F("duration"), 1f, 0f); break;   // camGame.flash(white, duration)
            case "sserafimPulseLights":
                _ssfLightsEnabled = B("enabled");
                if (v?["colors"] is Newtonsoft.Json.Linq.JArray cols && v["durations"] is Newtonsoft.Json.Linq.JArray durs && v["intensities"] is Newtonsoft.Json.Linq.JArray ints)
                {
                    _ssfLightColors = cols.Select(x => SsfColor((string)x)).ToArray();
                    _ssfLightDurations = durs.Select(x => (float)x).ToArray();
                    _ssfLightIntensities = ints.Select(x => (float)x).ToArray();
                }
                break;
            case "sserafimKick":
                if (B("final"))
                {
                    _ssfYunjin?.PlayAnimation("kick2", true);
                    Audio.PlaySound("sounds/sserafim/doorKick2", 1f);
                    if (_ssfYunjin != null) _ssfYunjin.NoBeatDance = false;   // danceEvery = 1
                    _ssfHideIconP2 = false;                                     // iconP2.visible = true
                    if (_ssfGfSpr != null)
                    {
                        if (_girlfriend != null) _girlfriend.Visible = true;
                        _ssfGfVis = false; _ssfBfVis = false;
                    }
                    _ssfKickDoorPending = true;                                 // truckDoor visible on kick2 frame 23
                    SsfStartClear();
                }
                else
                {
                    _ssfYunjin?.PlayAnimation("kick1", true);
                    Audio.PlaySound("sounds/sserafim/doorKick1", 1f);
                }
                break;
            case "sserafimEnd": SsfEndStuff(); break;
            case "sserafimBeautiful":
                _ssfIsBeautiful = B("beautiful");
                if (_girlfriend != null) _girlfriend.AnimSuffix = _ssfIsBeautiful ? "-beautiful" : null;
                break;
        }
    }
    private bool _ssfKickDoorPending;

    private static Vector3 SsfColor(string s)
    {
        s = (s ?? "").Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
        if (s.StartsWith("#")) s = s.Substring(1);
        uint v = Convert.ToUInt32(s, 16);
        if (s.Length <= 6) v |= 0xFF000000;
        return new Vector3(((v >> 16) & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f);
    }

    private void SsfSetCover(bool visible) { var p = SsfNamed("solidCover"); if (p != null) p.Alpha = visible ? 1f : 0f; }

    private void SsfSetDarken(float amt, float dur)
    {
        SsfCancel(_ssfChar); SsfCancel(_ssfStage);
        float c0 = _ssfChar.Darken, s0 = _ssfStage.Darken;
        var ease = FlxEase("sineInOut");
        SsfTween(_ssfChar, dur, ease, t => _ssfChar.Darken = c0 + (amt - c0) * t);
        SsfTween(_ssfStage, dur, ease, t => _ssfStage.Darken = s0 + (amt - s0) * t);
    }

    private void SsfFlashTruckLights(float amount, float duration)
    {
        var l1 = SsfNamed("truckLight1"); var l2 = SsfNamed("truckLight2");
        if (l1 == null || l2 == null) return;
        SsfCancel(l1); SsfCancel(l2);
        l1.Alpha = amount; l2.Alpha = amount;
        _ssfChar.TruckStrength = _ssfStage.TruckStrength = amount;
        var ease = FlxEase("cubeInOut");
        SsfTween(l1, duration, ease, t => { l1.Alpha = amount * (1f - t); _ssfChar.TruckStrength = _ssfStage.TruckStrength = l1.Alpha; },
            () => { _ssfChar.TruckStrength = _ssfStage.TruckStrength = 0f; });
        SsfTween(l2, duration, ease, t => l2.Alpha = amount * (1f - t));
    }

    private void SsfFlashBackLight(float amount, float duration, Vector3 color)
    {
        var bc = SsfNamed("backLightColor"); var bw = SsfNamed("backLightWhite");
        if (bc == null || bw == null) return;
        SsfCancel(bc); SsfCancel(bw);
        bc.OverlayColor = new Color(color);
        bc.Alpha = amount * 0.8f; bw.Alpha = amount * 0.7f;
        _ssfChar.PulseColor = _ssfStage.PulseColor = color;
        _ssfChar.PulseStrength = _ssfStage.PulseStrength = bc.Alpha;
        float a0 = bc.Alpha, w0 = bw.Alpha;
        var ease = FlxEase("cubeInOut");
        SsfTween(bc, duration, ease, t => { bc.Alpha = a0 * (1f - t); _ssfChar.PulseStrength = _ssfStage.PulseStrength = bc.Alpha; },
            () => { _ssfChar.PulseStrength = _ssfStage.PulseStrength = 0f; });
        SsfTween(bw, duration, ease, t => bw.Alpha = w0 * (1f - t));
    }

    /// <summary>onBeatHit: flash the lights behind the truck.</summary>
    private void SsfOnBeat(int beat)
    {
        if (!_ssfActive) return;
        foreach (var c in new[] { _ssfYunjin, _ssfChaewon, _ssfEunchae }) c?.Dance(beat);
        if (_ssfLightsEnabled && beat >= 0 && _ssfLightIntensities.Length > 0 && _ssfLightDurations.Length > 0 && _ssfLightColors.Length > 0 && _ssfLog && beat > 105 && beat < 113)
            Console.WriteLine($"[SSF] beat {beat} sp={SongPositionSeconds * 1000:F0} int={_ssfLightIntensities[beat % _ssfLightIntensities.Length]} col={_ssfLightColors[beat % _ssfLightColors.Length]}");
        if (_ssfLightsEnabled && beat >= 0 && _ssfLightIntensities.Length > 0 && _ssfLightDurations.Length > 0 && _ssfLightColors.Length > 0)
            SsfFlashBackLight(_ssfLightIntensities[beat % _ssfLightIntensities.Length], _ssfLightDurations[beat % _ssfLightDurations.Length],
                _ssfLightColors[beat % _ssfLightColors.Length]);
    }

    // ------------------------------------------------------------------ dust (resetClear / startClear)
    private void SsfResetClear()
    {
        if (_ssfYunjin != null) { _ssfYunjin.PlayAnimation("doorclosed", true); _ssfYunjin.NoBeatDance = true; } // danceEvery = 0
        var door = SsfNamed("truckDoor"); if (door != null) door.Visible = false;
        _ssfKickDoorPending = false;
        SsfCancel(_ssfStage); SsfCancel(_ssfChar);
        foreach (var d in _ssfDust) SsfCancel(d);
        _ssfStage.SetAdjust(-24, 6, -26, -74);
        _ssfChar.SetAdjust(-24, 6, -26, -74);
        float[] ys = { -400, -450, -600, -1500 };
        float[] vx = { 350, -300, -200, -150 };
        for (int i = 0; i < _ssfDust.Count; i++) { _ssfDust[i].X = -650; _ssfDust[i].Y = ys[i]; _ssfDust[i].Vx = vx[i]; _ssfDust[i].Alpha = 1f; }
    }

    private void SsfStartClear()
    {
        var ease = FlxEase("sineOut");
        SsfTweenAdjust(0, 0, 0, 0, 6f * 4, ease);
        float[] durs = { 5f * 4, 4f * 4, 6f * 4, 4f * 4 };
        float[] dy = { 100, 200, 150, 100 };
        for (int i = 0; i < _ssfDust.Count; i++)
        {
            var d = _ssfDust[i]; float a0 = d.Alpha, y0 = d.Y, v0 = d.Vx, dyi = dy[i];
            SsfTween(d, durs[i], ease, t => { d.Alpha = a0 * (1f - t); d.Y = y0 + dyi * t; });
            SsfTween(d, durs[i], ease, t => d.Vx = v0 * (1f - t));
        }
    }

    /// <summary>FlxTween.tween(stageShader + characterShader, {baseBrightness, baseHue, baseContrast, baseSaturation}).</summary>
    private void SsfTweenAdjust(float b, float h, float c, float s, float dur, Func<float, float> ease)
    {
        foreach (var sh in new[] { _ssfStage, _ssfChar })
        {
            var x = sh; float b0 = x.Bright, h0 = x.Hue, c0 = x.Contrast, s0 = x.Sat;
            SsfTween(x, dur, ease, t => x.SetAdjust(b0 + (b - b0) * t, h0 + (h - h0) * t, c0 + (c - c0) * t, s0 + (s - s0) * t));
        }
    }

    // ------------------------------------------------------------------ cutscene
    private void SsfSetCutsceneVisibility(bool inCutscene)
    {
        if (inCutscene) SsfSetGirlsVisible(new[] { false, false, false, false, false });
        else SsfSetGirlsVisible(SsfBaseVisible);
        void Vis(string n, bool v) { var p = SsfNamed(n); if (p != null) p.Visible = v; }
        Vis("truck", !inCutscene);
        Vis("truckDoor", false);
        Vis("backTables", !inCutscene);
        Vis("backStools", !inCutscene);
        Vis("frontStool", !inCutscene);
        foreach (var d in _ssfDust) d.Visible = !inCutscene;   // hideDust(!inCutscene)
        _ssfFloorCur = inCutscene ? (_ssfFloorCutTex ?? _ssfFloorTex) : _ssfFloorTex;
        Vis("backTablesCutscene", inCutscene);
        Vis("burgerCutscene", inCutscene);
        if (_girlfriend != null) _girlfriend.Visible = false;
        if (_ssfCutSpr != null) _ssfCutVis = inCutscene;
    }

    private void SsfPlayCutsceneFromRestart()
    {
        SsfSetCutsceneVisibility(false);
        SsfResetClear();
        W1TweenZoom(0.55f, 1f, FlxEase("circOut"));
        TweenCameraToPosition(1070, 470, 1f, FlxEase("circOut"));
        _ssfGfVis = _ssfGfSpr != null; _ssfBfVis = _ssfBfSpr != null;
        _ssfGfSpr?.PlayAnimationFromFrame("getup", 23, loop: false);
        _ssfBfSpr?.PlayAnimationFromFrame("getup", 23, loop: false);
    }

    private void SsfIntroCutscene()
    {
        _ssfClock = 0f;
        _ssfCutsceneSkipped = false; _ssfCanSkip = false; _ssfSkipTextShown = false; _ssfSkipTextAlpha = 0f;
        _ssfCutSpr?.PlayAnimation(SsfMainAnim(_ssfCutSpr), true, false);   // doAnim(): animation.play("", true)
        W1TweenZoom(0.5f, 0f, null);                                         // tweenCameraZoom(0.5, 0, true)
        TweenCameraToPosition(660, -200, 0f);
        SsfCamGameFx(Color.Black, 3f, 1f, 0f);                               // camGame.fade(black, 3, fadeIn)
        SsfAfter(20f / 24f, () =>
        {
            _ssfCutsceneSound = Audio.CreateSoundInstance("sounds/sserafim/cutscene/startCutscene.ogg");
            if (_ssfCutsceneSound != null) { _ssfCutsceneSound.Volume = Math.Clamp(Audio.SfxVolume, 0f, 1f); _ssfCutsceneSound.Play(); }
        }, true);
        W1TweenZoom(0.7f, 3f, FlxEase("circOut"));
        TweenCameraToPosition(660, 300, 3f, FlxEase("circOut"));
        // gf taps herself / gf taps bf
        SsfAfter(245f / 24f, () => SsfSetDarken(0.2f, 0.01f), true);
        SsfAfter(251f / 24f, () => SsfSetDarken(0f, 0.8f), true);
        SsfAfter(406f / 24f, () => SsfSetDarken(0.2f, 0.01f), true);
        SsfAfter(411f / 24f, () => SsfSetDarken(0f, 0.8f), true);
        // truck starts getting closer (skipping disabled from here)
        SsfAfter(499f / 24f, () =>
        {
            _ssfCutsceneSkipped = true; _ssfCanSkip = false;
            if (_ssfSkipTextShown) { float a0 = _ssfSkipTextAlpha; SsfTween("skipText", 0.5f, FlxEase("quadIn"), t => _ssfSkipTextAlpha = a0 * (1f - t), () => _ssfSkipTextShown = false); }
            SsfCancel(_ssfStage); SsfCancel(_ssfChar);
            SsfTweenAdjust(55, 0, -30, 0, 49f / 24f, FlxEase("sineOut"));
        }, true);
        // truck gets really close
        SsfAfter(548f / 24f, () =>
        {
            SsfCancel(_ssfStage); SsfCancel(_ssfChar);
            SsfTweenAdjust(66, 10, -17, 0, 15f / 24f, FlxEase("expoIn"));
        }, true);
        // car crash + flash
        SsfAfter(563f / 24f, () =>
        {
            SsfCancel(_ssfStage); SsfCancel(_ssfChar);
            _ssfStage.SetAdjust(0, 0, 0, 0); _ssfChar.SetAdjust(0, 0, 0, 0);
            SsfCamGameFx(Color.White, 30f / 24f, 1f, 0f);                   // camGame.fade(white, 30/24, fadeIn)
            SsfSetCover(true);
            SsfSetCutsceneVisibility(false);
        }, true);
        // fade out from black and add dust
        SsfAfter(650f / 24f, () =>
        {
            W1TweenZoom(0.7f, 0f, null);
            W1TweenZoom(0.55f, 3f, FlxEase("circOut"));
            TweenCameraToPosition(1070, 470, 0f);
            SsfResetClear();
            var cover = SsfNamed("solidCover");
            if (cover != null) SsfTween(cover, 3f, FlxEase("sineOut"), t => cover.Alpha = 1f - t);
            _ssfGfVis = _ssfGfSpr != null; _ssfBfVis = _ssfBfSpr != null;
            _ssfGfSpr?.PlayAnimation("static", true, false);
            _ssfBfSpr?.PlayAnimation("static", true, false);
        }, true);
        SsfAfter(710f / 24f, () =>
        {
            _ssfGfSpr?.PlayAnimation("getup", true, false);
            _ssfBfSpr?.PlayAnimation("getup", true, false);
        }, true);
        // actually start song
        SsfAfter(730f / 24f, SsfCutsceneDone, true);
    }

    private void SsfCutsceneDone()
    {
        _ssfCutscene = false;
        W6StartCountdown(true);
    }

    private void SsfSkipCutscene()
    {
        _ssfCutsceneSkipped = true;
        _ssfCutFadeFrom = 0f; _ssfCutFadeTo = 1f; _ssfCutFadeDur = 0.5f; _ssfCutFadeT = 0f; _ssfCutFadeOn = true; // camCutscene.fade(black, 0.5)
        float v0 = _ssfCutsceneSound?.Volume ?? 0f;
        SsfTween("cutSound", 0.5f, null, t => { if (_ssfCutsceneSound != null) _ssfCutsceneSound.Volume = Math.Max(0f, v0 * (1f - t)); });
        SsfAfter(0.5f, () =>
        {
            _ssfCutFadeFrom = 1f; _ssfCutFadeTo = 0f; _ssfCutFadeDur = 0.5f; _ssfCutFadeT = 0f; _ssfCutFadeOn = true;
            _ssfTimers.RemoveAll(t => t.Cutscene);          // cutsceneTimerManager.clear()
            try { _ssfCutsceneSound?.Stop(); } catch { }
            _ssfSkipTextShown = false;
            SsfCutsceneDone();
            SsfPlayCutsceneFromRestart();
        });
    }

    private static readonly float _ssfDevEndAt = float.TryParse(Environment.GetEnvironmentVariable("FNF_SSF_END_AT"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var __e) ? __e : -1f;
    private float _ssfEndT0 = -1f;   // dev: _ssfClock when endStuff() ran (FNF_SHOTS_W1TIME clock)
    private void SsfEndStuff()
    {
        _ssfEndT0 = _ssfClock;
        _ssfEnding = true;
        Audio.PlaySound("sounds/sserafim/cutscene/end1", 1f);
        SsfAfter(0.05f, () => { _ssfEnd1Vis = true; SsfSetCover(true); _w6HudHidden = true; });
        SsfAfter(4f, () => { _ssfEnd1Vis = false; _ssfEnd2Vis = true; Audio.PlaySound("sounds/sserafim/cutscene/end2", 1f); });
        SsfAfter(8f, () => { _ssfEnd1Vis = false; _ssfEnd2Vis = false; });
        SsfAfter(9f, () => { _ssfEnding = false; _ssfEndNow = true; _forceEndSong = true; }); // PlayState.endSong(true)
    }
    private bool _ssfEndNow;
    /// <summary>Blocks the normal song end while the "see you" ending runs (endSong(true) after 9 s).</summary>
    private bool SsfEndBlocks() => _ssfActive && _ssfEnding && !_ssfEndNow;

    // ------------------------------------------------------------------ update
    /// <summary>Per frame (also while the intro blocks the countdown).</summary>
    private void SsfUpdate(float dt)
    {
        if (!_ssfActive) return;
        _ssfClock += dt;
        for (int i = 0; i < _ssfTimers.Count; i++)
            if (_ssfClock >= _ssfTimers[i].T) { var a = _ssfTimers[i].A; _ssfTimers.RemoveAt(i); i--; a(); }
        for (int i = 0; i < _ssfTweens.Count; i++)
        {
            var tw = _ssfTweens[i];
            if (tw.Cancelled) { _ssfTweens.RemoveAt(i); i--; continue; }
            tw.T += dt;
            float t = Math.Min(1f, tw.T / tw.Dur);
            tw.Set(t >= 1f ? 1f : tw.Ease(t));
            if (t >= 1f) { _ssfTweens.RemoveAt(i); i--; tw.Done?.Invoke(); }
        }
        if (_ssfGameFxOn)
        {
            _ssfGameFxT += dt;
            float t = Math.Min(1f, _ssfGameFxT / Math.Max(0.0001f, _ssfGameFxDur));
            _ssfGameFxAlpha = _ssfGameFxFrom + (_ssfGameFxTo - _ssfGameFxFrom) * t;
            if (t >= 1f) _ssfGameFxOn = _ssfGameFxTo > 0f;   // fade-out stays filled; flash/fade-in ends clear
        }
        if (_ssfCutFadeOn)
        {
            _ssfCutFadeT += dt;
            float t = Math.Min(1f, _ssfCutFadeT / _ssfCutFadeDur);
            _ssfCutFadeAlpha = _ssfCutFadeFrom + (_ssfCutFadeTo - _ssfCutFadeFrom) * t;
            if (t >= 1f && _ssfCutFadeTo <= 0f) _ssfCutFadeOn = false;
        }
        if (_ssfDevEndAt >= 0f && _ssfEndT0 < 0f && !_ssfCutscene && SongPositionSeconds >= _ssfDevEndAt) SsfEndStuff(); // dev: like the orig module's scriptCall('endStuff')
        foreach (var d in _ssfDust) d.X += d.Vx * dt;
        _ssfYunjin?.Update(dt); _ssfChaewon?.Update(dt); _ssfEunchae?.Update(dt);
        _ssfCutSpr?.Update(dt); _ssfGfSpr?.Update(dt); _ssfBfSpr?.Update(dt);
        LipUpdate();
        // kick2: "at this point in the animation, the door is no longer part of her animation" (frame 23)
        if (_ssfKickDoorPending && _ssfYunjin != null && _ssfYunjin.CurrentAnimation == "kick2" && _ssfYunjin.SpriteFrameIndex >= 23)
        {
            var door = SsfNamed("truckDoor"); if (door != null) door.Visible = true;
            _ssfKickDoorPending = false;
        }
        // CUTSCENE_ADVANCE (accept) during the intro: first press shows "Skip [ x ]", a press after 0.5 s skips
        if (_ssfCutscene && !_ssfCutsceneSkipped && Input.CutsceneAdvancePressed)
        {
            if (!_ssfCanSkip)
            {
                if (!_ssfSkipTextShown)
                {
                    _ssfSkipTextShown = true;
                    float a0 = _ssfSkipTextAlpha;
                    SsfTween("skipText", 0.5f, FlxEase("quadOut"), t => _ssfSkipTextAlpha = a0 + (1f - a0) * t);
                    SsfAfter(0.5f, () => _ssfCanSkip = true);
                }
            }
            else SsfSkipCutscene();
        }
        SsfDevLog();
    }

    private float _ssfLogNext = -1f;
    private void SsfDevLog()
    {
        if (!_ssfLog) return;
        if (_ssfLogNext < 0f) _ssfLogNext = 0f;
        if (_ssfClock < _ssfLogNext) return;
        _ssfLogNext += 1f;
        string Ch(Character c) => c == null ? "-" : $"{c.CurrentAnimation}:{c.SpriteFrameIndex} vis={c.Visible} pos={c.Position.X},{c.Position.Y}";
        Console.WriteLine($"[SSF] clock={_ssfClock * 1000:F0} sp={SongPositionSeconds * 1000:F0} cam={_camWorldX:F1},{_camWorldY:F1} zoom={_defaultCamZoom * _cameraBopMultiplier:F3} dark={_ssfStage.Darken:F3} bhcs={_ssfStage.Bright:F2}/{_ssfStage.Hue:F2}/{_ssfStage.Contrast:F2}/{_ssfStage.Sat:F2} truck={_ssfStage.TruckStrength:F3} pulse={_ssfStage.PulseStrength:F3}"
            + $" | yun={Ch(_ssfYunjin)} kaz={Ch(_opponent)} chae={Ch(_ssfChaewon)} eun={Ch(_ssfEunchae)} sak={Ch(_boyfriend)} gf={Ch(_girlfriend)} cut={_ssfCutVis}:{_ssfCutSpr?.FrameIndex} gfs={_ssfGfVis}:{_ssfGfSpr?.CurrentAnimation}:{_ssfGfSpr?.FrameIndex} dust1={(_ssfDust.Count > 0 ? $"{_ssfDust[0].X:F1},{_ssfDust[0].Y:F1},{_ssfDust[0].Alpha:F3}" : "-")}");
    }

    /// <summary>While the intro blocks the countdown: characters animate (bf/dad/gf are hidden).</summary>
    private void SsfUpdateBlocked(float dt)
    {
        _boyfriend?.Update(dt); _opponent?.Update(dt); _girlfriend?.Update(dt);
    }

    // ------------------------------------------------------------------ draw
    private bool SsfBeginShader(SpriteBatch sb, SsfShaderState s, bool isChar)
    {
        if (!_ssfActive) return false;
        var fx = SsfEffect;
        if (fx == null) return false;
        fx.CurrentTechnique = fx.Techniques["Sserafim"];
        fx.Parameters["hue"]?.SetValue(s.Hue);
        fx.Parameters["saturation"]?.SetValue(s.Sat);
        fx.Parameters["brightness"]?.SetValue(s.Bright);
        fx.Parameters["contrast"]?.SetValue(s.Contrast);
        fx.Parameters["darkAmt"]?.SetValue(s.Darken);
        fx.Parameters["lightColor"]?.SetValue(s.PulseColor);
        fx.Parameters["pulseStrength"]?.SetValue(s.PulseStrength);
        fx.Parameters["truckStrength"]?.SetValue(s.TruckStrength);
        fx.Parameters["isChar"]?.SetValue(isChar ? 1f : 0f);
        sb.End();
        sb.Begin(samplerState: _worldSampler, blendState: _worldBlendState, transformMatrix: _worldZoomMatrix, rasterizerState: _worldClipRaster, effect: fx);
        return true;
    }

    /// <summary>Prop draw hook (all static/animated prop paths): stageShader on every prop but the lights / solidCover.</summary>
    private bool SsfBeginProp(SpriteBatch sb, string name)
    {
        if (!_ssfActive || name == null) return false;
        switch (name)
        {
            case "truckLight1": case "truckLight2": case "backLightColor": case "backLightWhite": case "truckTest": case "solidCover":
                return false;
        }
        if (name.StartsWith("dust") || name.StartsWith("sserafim-") || name == "perspectiveFloor"
            || name == "cutsceneMain" || name == "gfGetUp" || name == "bfGetUp") return false; // drawn by their own DrawFn
        return SsfBeginShader(sb, _ssfStage, false);
    }

    /// <summary>Character draw hook: characterShader (isCharacter = true) on all six girls.</summary>
    private bool SsfBeginChar(SpriteBatch sb, Character c) => _ssfActive && c != null && SsfBeginShader(sb, _ssfChar, true);

    /// <summary>Static prop draw override: SCREEN blend lights and straight-alpha normal props (alpha tweens).</summary>
    private bool SsfDrawStatic(SpriteBatch sb, StaticStageProp prop)
    {
        if (prop is SsfProp sp) { if (sp.Visible) sp.DrawFn?.Invoke(sb); return true; } // custom-draw props (any stage)
        if (!_ssfActive) return false;
        if (prop.Texture == null || !prop.Visible) return prop.Texture != null;
        bool screen = prop.Name == "truckLight1" || prop.Name == "backLightColor";
        bool normalAlpha = prop.Name == "truckLight2" || prop.Name == "solidCover";
        if (!screen && !normalAlpha) return false;
        if (prop.Alpha <= 0f) return true;
        var pos = WorldToScreen(prop.X, prop.Y, prop.ScrollX, prop.ScrollY);
        var rgb = prop.OverlayColor ?? Color.White;
        var tint = new Color(rgb.R, rgb.G, rgb.B, (byte)Math.Round(Math.Clamp(prop.Alpha, 0f, 1f) * 255f));
        if (screen)
        {
            var fx = SsfEffect;
            if (fx == null) return false;
            fx.CurrentTechnique = fx.Techniques["Premultiply"];
            sb.End();
            sb.Begin(samplerState: _worldSampler, blendState: _ssfScreenBlend, transformMatrix: _worldZoomMatrix, rasterizerState: _worldClipRaster, effect: fx);
            sb.Draw(prop.Texture, pos, prop.SourceRect, tint, 0f, Vector2.Zero, new Vector2(prop.ScaleX, prop.ScaleY), SpriteEffects.None, 0f);
            sb.End();
            sb.Begin(samplerState: _worldSampler, blendState: _worldBlendState, transformMatrix: _worldZoomMatrix, rasterizerState: _worldClipRaster);
        }
        else
        {
            sb.Draw(prop.Texture, pos, prop.SourceRect, tint, 0f, Vector2.Zero, new Vector2(prop.ScaleX, prop.ScaleY), SpriteEffects.None, 0f);
        }
        return true;
    }

    /// <summary>FlxBackdrop(image, X): tiles repeated along X, scaled about the tile centre, colour + alpha, no shader.</summary>
    private void SsfDrawDust(SpriteBatch sb, SsfDust d)
    {
        if (!d.Visible || d.Alpha <= 0f) return;
        float zoom = Math.Max(0.1f, _defaultCamZoom * _cameraBopMultiplier);
        float visX = d.X + d.Tex.Width / 2f * (1f - d.Scale);
        float visY = d.Y + d.Tex.Height / 2f * (1f - d.Scale);
        float step = d.Tex.Width * d.Scale;
        float viewLeft = (_camWorldX - 640f) * d.Sf + 640f - 640f / zoom;
        float viewRight = (_camWorldX - 640f) * d.Sf + 640f + 640f / zoom;
        float n = MathF.Floor((viewLeft - visX) / step);
        var tint = new Color(d.Col.R, d.Col.G, d.Col.B, (byte)Math.Round(Math.Clamp(d.Alpha, 0f, 1f) * 255f));
        for (float x = visX + n * step; x < viewRight + step; x += step)
        {
            var p = WorldToScreen(x, visY, d.Sf, d.Sf);
            sb.Draw(d.Tex, p, null, tint, 0f, Vector2.Zero, d.Scale, SpriteEffects.None, 0f);
        }
    }

    /// <summary>PerspectiveSprite.updateSkew + FlxSkewedSprite.drawComplex (matrixExposed) with the stageShader.</summary>
    private void SsfDrawFloor(SpriteBatch sb)
    {
        var tex = _ssfFloorCur;
        if (tex == null || tex == Assets.Pixel) return;
        float w = tex.Width, h = tex.Height;
        float scrollX = _camWorldX - 640f, scrollY = _camWorldY - 360f;
        // setPositions(760, 1375, 790, 625); setScrollFactors(1.05, 1.05, 0.93, 0.93); sprite.scrollFactor = 1
        float bx = 760f + scrollX * (1f - 1.05f), by = 1375f + scrollY * (1f - 1.05f);
        float tx = 790f + scrollX * (1f - 0.93f), ty = 625f + scrollY * (1f - 0.93f);
        float distX = tx - bx, distY = ty - (by - h);
        float c = -(distX / h), dd = 1f - (distY / h), mtx = distX / 2f, mty = distY / 2f;
        float sx = bx - w / 2f, sy = by - h;
        _ssfFloorLog = (sx, sy, c, dd, mtx, mty);
        // pixel p -> (p - origin) -> M -> + screen(x,y) + origin, then the camera zoom (world batch matrix)
        var o = WorldToScreen(sx + w / 2f, sy + h / 2f, 1f, 1f);
        var m = Matrix.CreateTranslation(-w / 2f, -h / 2f, 0f)
              * new Matrix(1f, 0f, 0f, 0f, c, dd, 0f, 0f, 0f, 0f, 1f, 0f, mtx, mty, 0f, 1f)
              * Matrix.CreateTranslation(o.X, o.Y, 0f)
              * _worldZoomMatrix;
        var fx = SsfEffect;
        if (fx != null)
        {
            SsfBeginShader(sb, _ssfStage, false);
            sb.End();
            sb.Begin(samplerState: _worldSampler, blendState: _worldBlendState, transformMatrix: m, rasterizerState: _worldClipRaster, effect: fx);
        }
        else
        {
            sb.End();
            sb.Begin(samplerState: _worldSampler, blendState: _worldBlendState, transformMatrix: m, rasterizerState: _worldClipRaster);
        }
        sb.Draw(tex, Vector2.Zero, Color.White);
        sb.End();
        sb.Begin(samplerState: _worldSampler, blendState: _worldBlendState, transformMatrix: _worldZoomMatrix, rasterizerState: _worldClipRaster);
    }
    private (float X, float Y, float C, float D, float TX, float TY) _ssfFloorLog;

    /// <summary>camGame flash/fade (over the world, under the HUD).</summary>
    private void SsfDrawGameOverlay(SpriteBatch sb)
    {
        if (!_ssfActive || !_ssfGameFxOn || _ssfGameFxAlpha <= 0f) return;
        // called between the world pass and the HUD pass (batch not begun here)
        sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);
        sb.Draw(Assets.Pixel, new Rectangle(0, 0, 1280, 720), new Color(_ssfGameFxColor, Math.Clamp(_ssfGameFxAlpha, 0f, 1f)));
        sb.End();
    }

    /// <summary>camCutscene: "see you" cards, skip text, camCutscene fade.</summary>
    private void SsfDrawCutscene(SpriteBatch sb)
    {
        if (!_ssfActive) return;
        sb.End();
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
        if (_ssfEnd1Vis && _ssfEnd1 != null)
        {
            // scale 0.67, updateHitbox, screenCenter
            float w = _ssfEnd1.Width * 0.67f, h = _ssfEnd1.Height * 0.67f;
            sb.Draw(_ssfEnd1, new Vector2((1280 - (int)w) / 2f, (720 - (int)h) / 2f), null, Color.White, 0f, Vector2.Zero, 0.67f, SpriteEffects.None, 0f);
        }
        if (_ssfEnd2Vis && _ssfEnd2 != null)
        {
            float w = (int)(_ssfEnd2.Width * 0.67f), h = (int)(_ssfEnd2.Height * 0.67f);
            sb.Draw(_ssfEnd2, new Vector2(1280 - 40 - w, 720 - 40 - h), null, Color.White, 0f, Vector2.Zero, 0.67f, SpriteEffects.None, 0f);
        }
        if (_ssfSkipTextShown && _ssfSkipTextAlpha > 0f)
        {
            // FlxText(936, 618, 0, 'Skip [ <accept> ]') vcr 40, right, black outline 2
            string txt = "Skip [ " + (Input.LastDevice.ToString() == "Controller" ? "A" : "ENTER") + " ]";
            var font = W6Font(40);
            if (font != null)
            {
                var a = Math.Clamp(_ssfSkipTextAlpha, 0f, 1f);
                for (int ox = -2; ox <= 2; ox += 2) for (int oy = -2; oy <= 2; oy += 2)
                    if (ox != 0 || oy != 0) sb.DrawString(font, txt, new Vector2(936 + ox, 618 + oy), new Color(Color.Black, a));
                sb.DrawString(font, txt, new Vector2(936, 618), new Color(Color.White, a));
            }
        }
        if (_ssfCutFadeOn && _ssfCutFadeAlpha > 0f)
            sb.Draw(Assets.Pixel, new Rectangle(0, 0, 1280, 720), new Color(Color.Black, Math.Clamp(_ssfCutFadeAlpha, 0f, 1f)));
        sb.End();
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
    }

    /// <summary>SetHealthIconSongEvent.handleEvent: iconP1/iconP2.configure({id, offsets: [offsetX, offsetY]}).</summary>
    private void ApplySetHealthIcon(Newtonsoft.Json.Linq.JObject v)
    {
        if (v == null) return;
        int ch = v["char"] != null ? (int)v["char"] : 0;
        string id = (string)v["id"] ?? "face";
        var off = new Vector2(v["offsetX"] != null ? (float)v["offsetX"] : 0f, v["offsetY"] != null ? (float)v["offsetY"] : 0f);
        var tex = Assets.LoadTexture($"images/icons/icon-{id}.png");
        if (tex == null || tex == Assets.Pixel) { Console.WriteLine($"[ICON] SetHealthIcon: no icon-{id}.png (kept)"); return; }
        if (ch == 0) { _playerIcon = tex; _iconP1Offset = off; }
        else if (ch == 1) { _opponentIcon = tex; _iconP2Offset = off; }
    }

    /// <summary>Stage JSON characters.*.scroll (Stage.addCharacter: character.scrollFactor).</summary>
    private void SsfReadCharScroll()
    {
        try
        {
            string path = _stageJsonFilePath;
            if (string.IsNullOrEmpty(path) || !global::FNF_MonoGame.Engine.FsCI.FileExists(path)) path = System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot, "data", "stages", (_chart?.Stage ?? "") + ".json");
            if (!global::FNF_MonoGame.Engine.FsCI.FileExists(path)) return;
            var j = Newtonsoft.Json.Linq.JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(path));
            Vector2 Sc(string k) { var a = j["characters"]?[k]?["scroll"] as Newtonsoft.Json.Linq.JArray; return a != null && a.Count >= 2 ? new Vector2((float)a[0], (float)a[1]) : Vector2.One; }
            _bfCharScroll = Sc("bf"); _dadCharScroll = Sc("dad"); _gfCharScroll = Sc("gf");
        }
        catch (Exception ex) { Console.WriteLine($"[SSF] char scroll: {ex.Message}"); }
    }
}
