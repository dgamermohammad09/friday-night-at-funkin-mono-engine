using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Gameplay;
using FNF_MonoGame.Engine.PsychEngine;

namespace FNF_MonoGame.Scenes;

/// <summary>Psych FlxPoint stand-in bound to engine fields (camFollow.x / camFollow.y).</summary>
public sealed class PsychPointRef
{
    private readonly Func<float> _gx, _gy; private readonly Action<float> _sx, _sy;
    public PsychPointRef(Func<float> gx, Action<float> sx, Func<float> gy, Action<float> sy) { _gx = gx; _sx = sx; _gy = gy; _sy = sy; }
    public float x { get => _gx(); set => _sx(value); }
    public float y { get => _gy(); set => _sy(value); }
    public void set(float x, float y) { _sx(x); _sy(y); }
    public void setPosition(float x, float y) { _sx(x); _sy(y); }
}

/// <summary>
/// Psych HUD object stand-in (healthBar, iconP1/P2, scoreTxt, timeBar, timeTxt). The port draws the
/// V-Slice HUD itself; these carry Psych 1.0.4 default layout values so Lua reads (healthBar.x/y/alpha)
/// return real numbers, and alpha/visible written by scripts are honoured by the HUD draw.
/// </summary>
public sealed class PsychAnimFacade
{
    private readonly Func<string> _name; private readonly Func<int> _frame; private readonly Func<bool> _fin; private readonly Action<string, bool> _play;
    public PsychAnimFacade(Func<string> name, Func<int> frame, Func<bool> fin, Action<string, bool> play) { _name = name; _frame = frame; _fin = fin; _play = play; }
    public PsychAnimFacade curAnim => _name() == null ? null : this;
    public string name => _name();
    public int curFrame => _frame();
    public bool finished => _fin();
    public void play(string anim, bool force = false, bool reversed = false, int frame = 0) => _play?.Invoke(anim, force);
    public bool exists(string anim) => true;
}

public sealed class PsychRect { public float x, y, width, height; }
public sealed class PsychFrameRef { public PsychRect frame = new PsychRect(); }
/// <summary>Bar.leftBar / Bar.rightBar (Psych objects/Bar.hx): makeGraphic(bg.width-6, bg.height-6) at +3,+3.</summary>
public sealed class PsychBarPart
{
    public float x, y, alpha = 1f; public bool visible = true;
    public PsychRect graphic = new PsychRect();
    public PsychFrameRef _frame = new PsychFrameRef();
    public float width => graphic.width; public float height => graphic.height;
}

/// <summary>Live view of a Lua sprite's graphic (FlxGraphic.width/height = texture size).</summary>
public sealed class PsychPropGraphicView
{
    private readonly PlayScene.StaticStageProp _p; public PsychPropGraphicView(PlayScene.StaticStageProp p) { _p = p; }
    public float width => _p.Texture?.Width ?? 0; public float height => _p.Texture?.Height ?? 0;
}
/// <summary>Live view of FlxSprite._frame.frame (source rect); writing width/height crops the drawn frame (health bar fills).</summary>
public sealed class PsychPropFrameRectView
{
    private readonly PlayScene.StaticStageProp _p; public PsychPropFrameRectView(PlayScene.StaticStageProp p) { _p = p; }
    private Rectangle R => _p.SourceRect ?? new Rectangle(0, 0, _p.Texture?.Width ?? 0, _p.Texture?.Height ?? 0);
    public float x { get => R.X; set { var r = R; r.X = (int)value; _p.SourceRect = r; } }
    public float y { get => R.Y; set { var r = R; r.Y = (int)value; _p.SourceRect = r; } }
    public float width { get => R.Width; set { var r = R; r.Width = Math.Max(0, (int)Math.Round(value)); _p.SourceRect = r; } }
    public float height { get => R.Height; set { var r = R; r.Height = Math.Max(0, (int)Math.Round(value)); _p.SourceRect = r; } }
}
public sealed class PsychPropFrameView
{
    public PsychPropFrameRectView frame { get; }
    public PsychPropFrameView(PlayScene.StaticStageProp p) { frame = new PsychPropFrameRectView(p); }
}

/// <summary>boyfriendGroup / dadGroup / gfGroup: FlxSpriteGroup at the stage slot holding the character
/// (char.x = group.x + positionArray[0]). Moving the group moves the current character.</summary>
public sealed class PsychCharGroup
{
    private readonly Func<Character> _c;
    public PsychCharGroup(Func<Character> c) { _c = c; }
    private float PA(int i) { var c = _c(); return c?.PsychCharacterOffset != null && c.PsychCharacterOffset.Length > i ? c.PsychCharacterOffset[i] : 0f; }
    public float x { get => (_c()?.X ?? 0f) - PA(0); set { var c = _c(); if (c != null) c.X = value + PA(0); } }
    public float y { get => (_c()?.Y ?? 0f) - PA(1); set { var c = _c(); if (c != null) c.Y = value + PA(1); } }
    public float alpha { get => _c()?.Alpha ?? 1f; set { var c = _c(); if (c != null) c.Alpha = value; } }
    public bool visible { get => (_c()?.Alpha ?? 0f) > 0f; set { var c = _c(); if (c != null && !value) c.Alpha = 0f; } }
    public void setPosition(float nx, float ny) { x = nx; y = ny; }
}

public sealed class PsychHudObject
{
    public string Name;
    public float x, y, width, height, angle;
    public float alpha = 1f;
    public bool visible = true;
    public string text = "";
    public float scaleX = 1f, scaleY = 1f;
    /// <summary>FlxSprite.scale (iconP1.scale.x/y; startTween('iconP1.scale', {x=..})).</summary>
    public HxPoint scale => new HxPoint(() => scaleX, v => scaleX = v, () => scaleY, v => scaleY = v);
    public PsychHudObject(string name, float x, float y, float w, float h) { Name = name; this.x = x; this.y = y; width = w; height = h; }
    public float Opacity => visible ? Math.Clamp(alpha, 0f, 1f) : 0f;
    public PsychHudObject setPosition(double x = 0, double y = 0) { this.x = (float)x; this.y = (float)y; return this; } // FlxObject.setPosition
    /// <summary>FlxSprite.flipX (a Bar group passes it to leftBar/rightBar/bg: each mirrors inside its own frame).</summary>
    public bool flipX;
    /// <summary>FlxSprite.blend as set by setBlendMode / setProperty ("add", "multiply", ...); null = normal.</summary>
    public object blend;
    /// <summary>FlxBasic.camera (camHUD by default; YARARARA moves the bars to camOther).</summary>
    public object camera;
    /// <summary>FlxSprite.offset / origin. null origin = the frame centre (flixel centerOrigin of each Bar child).</summary>
    public float offsetX, offsetY;
    public float? originX, originY;
    /// <summary>objects/Bar.hx is an FlxSpriteGroup: FlxSprite.updateHitbox on the group sets width/height (no-op on a
    /// group), offset = -0.5 * (group span - 0) and centerOrigin() = (0,0) (group frameWidth 0); FlxSpriteGroup passes
    /// offset and origin to every child. Plain HUD sprites keep their own hitbox (not modelled here).</summary>
    public void updateHitbox()
    {
        if (Name != "healthBar" && Name != "timeBar") return;
        offsetX = -0.5f * width; offsetY = -0.5f * height;
        originX = 0f; originY = 0f;
    }
}

public partial class PlayScene
{
    // PlayState.hx 1.0.4 HUD layout (non-downscroll): healthBar = Bar(0, FlxG.height*0.89) screenCenter(X), 601x19
    private PsychHudObject _pHealthBar, _pIconP1, _pIconP2, _pScoreTxt, _pTimeBar, _pTimeTxt;
    private void EnsurePsychHud()
    {
        if (_pHealthBar != null) return;
        float hbY = _downscroll ? 720f * 0.11f : 720f * 0.89f;
        _pHealthBar = new PsychHudObject("healthBar", (1280f - 601f) / 2f, hbY, 601f, 19f);
        // new HealthIcon(..): x = 0 until PlayState.update -> updateIconsPosition (scripts read 0 in onCreatePost)
        _pIconP1 = new PsychHudObject("iconP1", 0f, hbY - 75f, 150f, 150f);
        _pIconP2 = new PsychHudObject("iconP2", 0f, hbY - 75f, 150f, 150f);
        _pScoreTxt = new PsychHudObject("scoreTxt", 0f, hbY + 40f, 1280f, 20f);
        float ttY = _downscroll ? 720f - 44f : 19f;
        _pTimeTxt = new PsychHudObject("timeTxt", 42f + 640f - 248f, ttY, 400f, 32f);
        _pTimeBar = new PsychHudObject("timeBar", (1280f - 400f) / 2f, ttY + 33f / 4f, 400f, 19f); ApplyPsychHudPrefs(); // timeTxt.y + timeTxt.height/4 (33 px)
    }
    private PsychBarPart _pLeftBar, _pRightBar;
    public PsychBarPart leftBar => _pLeftBar ??= MakeBarPart();
    public PsychBarPart rightBar => _pRightBar ??= MakeBarPart();
    private PsychBarPart MakeBarPart()
    {
        EnsurePsychHud();
        var b = new PsychBarPart { x = _pHealthBar.x + 3f, y = _pHealthBar.y + 3f };
        b.graphic.width = b._frame.frame.width = 595f; b.graphic.height = b._frame.frame.height = 13f;
        return b;
    }
    private PsychCharGroup _pBfGroup, _pDadGroup, _pGfGroup;
    public PsychCharGroup boyfriendGroup => _pBfGroup ??= new PsychCharGroup(() => _boyfriend);
    public PsychCharGroup dadGroup => _pDadGroup ??= new PsychCharGroup(() => _opponent);
    public PsychCharGroup gfGroup => _pGfGroup ??= new PsychCharGroup(() => _girlfriend);
    public string[] singAnimations { get; } = { "singLEFT", "singDOWN", "singUP", "singRIGHT" };
    public PsychHudObject healthBar { get { EnsurePsychHud(); return _pHealthBar; } }
    public PsychHudObject iconP1 { get { EnsurePsychHud(); return _pIconP1; } }
    public PsychHudObject iconP2 { get { EnsurePsychHud(); return _pIconP2; } }
    public PsychHudObject scoreTxt { get { EnsurePsychHud(); return _pScoreTxt; } }
    public PsychHudObject timeBar { get { EnsurePsychHud(); return _pTimeBar; } }
    public PsychHudObject timeTxt { get { EnsurePsychHud(); return _pTimeTxt; } }
    /// <summary>Health bar + icons opacity requested by Psych scripts (1 when untouched).</summary>
    internal float PsychHealthBarOpacity => _pHealthBar == null ? Math.Clamp(FNF_MonoGame.Engine.PsychEngine.ClientPrefs.Data.HealthBarAlpha, 0f, 1f) : _pHealthBar.Opacity;
    internal float PsychIconOpacity(int p) => p == 1 ? (_pIconP1 == null ? Math.Clamp(FNF_MonoGame.Engine.PsychEngine.ClientPrefs.Data.HealthBarAlpha, 0f, 1f) : _pIconP1.Opacity) : (_pIconP2 == null ? Math.Clamp(FNF_MonoGame.Engine.PsychEngine.ClientPrefs.Data.HealthBarAlpha, 0f, 1f) : _pIconP2.Opacity);
    internal float PsychScoreOpacity => _pScoreTxt == null ? 1f : _pScoreTxt.Opacity;

    private PsychPointRef _pCamFollow;
    public PsychPointRef camFollow => _pCamFollow ??= new PsychPointRef(
        () => _camTargetWorldX, v => _camTargetWorldX = v, () => _camTargetWorldY, v => _camTargetWorldY = v);

    public float defaultCamZoom { get => _defaultCamZoom; set { _stageZoom = value; _defaultCamZoom = value; } }
    public float cameraSpeed { get; set; } = 1f;
    public bool isCameraOnForcedPos { get => _isCameraOnForcedPos; set => _isCameraOnForcedPos = value; }
    public int songScore { get => PsychNotes ? _psScore : _score; set { if (PsychNotes) _psScore = value; else _score = value; } }
    public int songMisses { get => PsychNotes ? _psMisses : _misses; set { if (PsychNotes) _psMisses = value; else _misses = value; } }
    public int combo { get => PsychNotes ? _psCombo : _combo; set { if (PsychNotes) _psCombo = value; else _combo = value; } }
    public bool cpuControlled { get => _botplay; set => _botplay = value; }
    public float[] boyfriendCameraOffset { get => _pendingBfCamOffsets ??= new float[2]; set => _pendingBfCamOffsets = value; }
    public float[] opponentCameraOffset { get => _pendingDadCamOffsets ??= new float[2]; set => _pendingDadCamOffsets = value; }
    public float[] girlfriendCameraOffset { get => _pendingGfCamOffsets ??= new float[2]; set => _pendingGfCamOffsets = value; }
    public Character boyfriend => _boyfriend;
    public Character dad => _opponent;
    public Character gf => _girlfriend;

    // FlxCamera.scroll of camGame (port keeps the camera centre in _camWorldX/Y)
    public float PsychCamScrollX { get => _camWorldX - 640f; set => _camWorldX = value + 640f; }
    public float PsychCamScrollY { get => _camWorldY - 360f; set => _camWorldY = value + 360f; }
    public void SetPsychRating(float? percent, string name, string fc)
    {
        if (percent.HasValue) _ratingPercent = percent;
        if (name != null) _ratingName = name;
        if (fc != null) _ratingFC = fc;
    }
    public LuaText GetLuaTextObject(string tag) => tag != null && _luaTexts.TryGetValue(tag, out var t) ? t : null;

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<StaticStageProp, PsychAnimFacade> _propAnims = new();
    public PsychAnimFacade PropAnimation(StaticStageProp p) => _propAnims.GetValue(p, q => new PsychAnimFacade(
        () => q.CurrentAnimation, () => q.CurrentFrame, () => !q.AnimPlaying, null));

    /// <summary>Stage-list entry that draws a Psych extra character (createInstance('x', 'objects.Character', ...)).</summary>
    public sealed class PsychCharacterProp : StaticStageProp { public Character Char; }
    private readonly List<Character> _psychExtraChars = new();

    /// <summary>objects.Character(x, y, character, isPlayer): top-left at (x, y), no stage/position offset.</summary>
    public Character CreatePsychExtraCharacter(float x, float y, string name, bool isPlayer)
    {
        var c = new Character(ResolveCharacterSprite(name), x, y);
        c.LoadJsonData(Assets, name);
        c.SetFlipForRole(isPlayer);
        c.StepCrochet = (float)_conductor.StepCrochet;
        c.LoadSprites(Game);
        c.PlayAnimation("idle", force: true);
        c.Position = new Vector2(x, y);
        return c;
    }

    /// <summary>addInstance(): inFront = on top of everything, otherwise behind the character groups.</summary>
    public void AddPsychCharacter(Character c, string tag, bool inFront)
    {
        if (c == null || _psychExtraChars.Contains(c)) return;
        _psychExtraChars.Add(c);
        var cprop = new PsychCharacterProp { Char = c, Name = tag, Visible = true, Alpha = 1f, ScaleX = 1f, ScaleY = 1f, ScrollX = 1f, ScrollY = 1f, ZIndex = inFront ? 1000 : 0 };
        AddStageObject(cprop);
        if (PsychOrderActive) PsychOrderAdd(cprop, inFront); // FunkinLua addInstance: add(obj) / insert(lowest character group)
    }

    private bool _psychShaking;
    internal void UpdatePsychExtraCharacters(float elapsed)
    {
        foreach (var c in _psychExtraChars) c.Update(elapsed);
        // cameraShake('game', i, d) -> FlxCamera.shake: offset = random(-1,1) * intensity * width * zoom (Blazin's shake path)
        var g = PsychCamera.GameCam;
        if (!_bzActive && g.ShakeDuration > 0f)
        {
            float z = _defaultCamZoom * _cameraBopMultiplier;
            _bzShakeOffset = g.ShakeDuration > 0f
                ? new Vector2(((float)_bzRng.NextDouble() * 2f - 1f) * g.ShakeIntensity * FNFGame.SCREEN_WIDTH * z,
                              ((float)_bzRng.NextDouble() * 2f - 1f) * g.ShakeIntensity * FNFGame.SCREEN_HEIGHT * z)
                : Vector2.Zero;
            _psychShaking = true;
        }
        else if (_psychShaking && !_bzActive) { _bzShakeOffset = Vector2.Zero; _psychShaking = false; }
    }

    /// <summary>Returns true when the prop was a Psych extra character (drawn here).</summary>
    private bool DrawPsychCharacterProp(SpriteBatch sb, StaticStageProp prop)
    {
        if (prop is not PsychCharacterProp cp) return false;
        if (cp.Visible && cp.Char != null && cp.Char.Alpha > 0f)
            DrawStageCharacter(sb, 1, cp.Char, WorldToScreen(cp.Char.Position.X, cp.Char.Position.Y));
        return true;
    }

    /// <summary>Resolve a Psych object tag (Lua sprite/text, game field path like "dad" or "boyfriend").</summary>
    public object ResolvePsychObject(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return null;
        if (ModchartSprites.TryGetValue(tag, out var spr)) return spr;
        if (MusicBeatState.Variables.TryGetValue(tag, out var v) && v != null) return v;
        if (_luaTexts.TryGetValue(tag, out var lt)) return lt;
        var split = tag.Split('.');
        object cur = this;
        foreach (var part in split)
        {
            cur = FNF_MonoGame.Engine.PsychEngine.LuaUtils.GetVarInArray(cur, part);
            if (cur == null) return null;
        }
        return cur;
    }

    /// <summary>FlxObject.getMidpoint for any Psych-visible object.</summary>
    public Vector2? PsychMidpoint(string tag, bool graphic = false)
    {
        var o = ResolvePsychObject(tag);
        switch (o)
        {
            case Character c: return graphic ? c.GetGraphicMidpoint() : c.GetMidpoint();
            case StaticStageProp p:
            {
                var (w, h) = PsychPropSize(p);
                return new Vector2(p.X + w / 2f, p.Y + h / 2f);
            }
            case PsychHudObject hud: return new Vector2(hud.x + hud.width / 2f, hud.y + hud.height / 2f);
        }
        return null;
    }

    /// <summary>Untrimmed size + trim offset of the frame currently shown (flixel frameWidth/frame.offset).</summary>
    private static (float W, float H, float OX, float OY) PsychFrameInfo(StaticStageProp p)
    {
        if (p.CurrentAnimation != null && p.Animations != null && p.Animations.TryGetValue(p.CurrentAnimation, out var a)
            && a.FrameSizes.Count > 0 && a.FrameOffsets.Count == a.FrameSizes.Count)
        {
            int i = Math.Clamp(p.CurrentFrame, 0, a.FrameSizes.Count - 1);
            return (a.FrameSizes[i].X, a.FrameSizes[i].Y, a.FrameOffsets[i].X, a.FrameOffsets[i].Y);
        }
        if (p.SourceRect is Rectangle r) return (r.Width, r.Height, 0, 0);
        if (p.Texture != null) return (p.Texture.Width, p.Texture.Height, 0, 0);
        return (0, 0, 0, 0);
    }

    /// <summary>FlxSprite.updateHitbox(): width = frameWidth*scale; offset = -0.5*(width - frameWidth).</summary>
    public static void PsychUpdateHitbox(StaticStageProp p)
    {
        if (!p.PsychOffsetX.HasValue) return;
        var f = PsychFrameInfo(p);
        p.PsychHitW = f.W * Math.Abs(p.ScaleX); p.PsychHitH = f.H * Math.Abs(p.ScaleY);
        p.PsychOffsetX = -0.5f * (p.PsychHitW.Value - f.W); p.PsychOffsetY = -0.5f * (p.PsychHitH.Value - f.H);
    }

    /// <summary>World-space shift from (X,Y) to where the trimmed source rect is drawn (flixel draw rules).</summary>
    internal static float PsychScaleShift(StaticStageProp p, bool xAxis)
    {
        float? off = xAxis ? p.PsychOffsetX : p.PsychOffsetY;
        if (!off.HasValue) return 0f;
        var f = PsychFrameInfo(p);
        float s = xAxis ? p.ScaleX : p.ScaleY;
        float full = xAxis ? f.W : f.H, trim = xAxis ? f.OX : f.OY;
        // flixel flipX mirrors the trimmed rect inside the untrimmed frame: left = frameWidth - frame.offset.x - rectWidth
        if (xAxis && p.FlipX && p.SourceRect is Rectangle sr) trim = full - trim - sr.Width;
        return -off.Value + full / 2f * (1f - s) + trim * s;
    }

    /// <summary>flixel drawComplex pivot: world point (x - offset + origin) and the origin inside the drawn (trimmed, flip-mirrored)
    /// rect, origin = (frameWidth/2, frameHeight/2) (centerOrigin/updateHitbox); scale then angle are applied about it.</summary>
    internal static (float X, float Y, Vector2 Z) PsychPivot(StaticStageProp p)
    {
        var f = PsychFrameInfo(p);
        float rw = p.SourceRect?.Width ?? p.Texture?.Width ?? 0, rh = p.SourceRect?.Height ?? p.Texture?.Height ?? 0;
        float trimX = p.FlipX ? f.W - f.OX - rw : f.OX, trimY = p.FlipY ? f.H - f.OY - rh : f.OY;
        float ox = f.W / 2f, oy = f.H / 2f;
        return (p.X - (p.PsychOffsetX ?? 0f) + ox, p.Y - (p.PsychOffsetY ?? 0f) + oy, new Vector2(ox - trimX, oy - trimY));
    }

    internal static (float W, float H) PsychPropSize(StaticStageProp p)
    {
        var f = PsychFrameInfo(p);
        if (p.PsychOffsetX.HasValue) return (p.PsychHitW ?? f.W, p.PsychHitH ?? f.H);
        return (f.W * Math.Abs(p.ScaleX), f.H * Math.Abs(p.ScaleY));
    }
}
