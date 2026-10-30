using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;
using PNoteS = FNF_MonoGame.Engine.PsychEngine.Note;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// 1:1 port of the V-Slice 0.8.1 strumline renderer (funkin/play/notes/Strumline.hx, StrumlineNote.hx, NoteSprite,
/// SustainTrail.hx, NoteSplash.hx, NoteHoldCover.hx) for the base note styles "funkin" and "pixel"
/// (assets/preload/data/notestyles/*.json), drawn with flixel's sprite maths (updateHitbox/centerOffsets/origin).
/// Used for every V-Slice song on PC and Xbox, keyboard or controller (not for Psych notes / custom mod note styles).
/// Options > Note Skin = Controller Buttons draws Xbox face-button icons on the player strumline at these same positions.
///   PlayState.initStrumlines: opponent.x = STRUMLINE_X_OFFSET (48), player.x = FlxG.width/2 + 48 = 688,
///   y = STRUMLINE_Y_OFFSET (24), downscroll y = FlxG.height - strumline.height - 24 - offsets[1].
///   Strumline: child.x = x + getXPos(dir) + INITIAL_OFFSET (-0.275*104) + offsets[0], NOTE_SPACING 112.
/// Verified against the running original (cmp/o_* logs: strum x 19.4/131.4/.., 659.4.., y 24 / 535, pixel 47.4 / 56 / 562).
/// </summary>
public partial class PlayScene
{
    private const float VS_STRUMLINE_SIZE = 104f;
    private const float VS_NOTE_SPACING = VS_STRUMLINE_SIZE + 8f;
    private const float VS_INITIAL_OFFSET = -0.275f * VS_STRUMLINE_SIZE;
    private const float VS_NUDGE = 2f;
    private const float VS_X_OFFSET = 48f, VS_Y_OFFSET = 24f;
    private const float VS_PIXELS_PER_MS = 0.45f;
    private const float VS_CONFIRM_HOLD_TIME = 0.15f;

    // ---------------------------------------------------------------- atlas
    private sealed class VsFrame
    {
        public string Name; public Rectangle Src; public bool Rot;
        public float FX, FY;   // position of the trimmed image inside the untrimmed frame (-frameX, -frameY)
        public int FW, FH;     // untrimmed frame size
        public int W, H;       // trimmed (unrotated) size
    }

    private sealed class VsAtlas
    {
        public Texture2D Tex;
        public readonly List<VsFrame> Frames = new();
        private readonly Dictionary<string, List<VsFrame>> _cache = new();

        /// <summary>flixel addByPrefix: frames whose name starts with prefix, ordered by their numeric suffix.</summary>
        public List<VsFrame> Prefix(string p)
        {
            if (_cache.TryGetValue(p, out var l)) return l;
            l = new List<VsFrame>();
            foreach (var f in Frames) if (f.Name.StartsWith(p, StringComparison.Ordinal)) l.Add(f);
            l.Sort((a, b) => Num(a.Name).CompareTo(Num(b.Name)));
            _cache[p] = l;
            return l;
        }
        private static int Num(string n)
        {
            int i = n.Length; while (i > 0 && char.IsDigit(n[i - 1])) i--;
            return i < n.Length && int.TryParse(n.Substring(i), out var v) ? v : 0;
        }
        public VsFrame First => Frames.Count > 0 ? Frames[0] : null;
    }

    private static readonly Regex VsSubTexRx = new("<SubTexture\\s([^>]*?)/?>", RegexOptions.Compiled);
    private static readonly Regex VsAttrRx = new("(\\w+)=\"([^\"]*)\"", RegexOptions.Compiled);

    private VsAtlas VsLoadAtlas(string rel, VsAtlas merge = null)
    {
        var tex = Assets.LoadTexture("images/vslice_notes/" + rel + ".png");
        if (tex == null || tex == Assets.Pixel) return merge;
        string xml = Assets.ResolvePath("images/vslice_notes/" + rel + ".xml");
        var a = merge ?? new VsAtlas { Tex = tex };
        if (xml == null) return a;
        foreach (Match m in VsSubTexRx.Matches(global::FNF_MonoGame.Engine.FsCI.ReadAllText(xml)))
        {
            var d = new Dictionary<string, string>();
            foreach (Match am in VsAttrRx.Matches(m.Groups[1].Value)) d[am.Groups[1].Value] = am.Groups[2].Value;
            int I(string k) => d.TryGetValue(k, out var s) ? (int)float.Parse(s, CultureInfo.InvariantCulture) : 0;
            bool rot = d.TryGetValue("rotated", out var r) && r == "true";
            int w = I("width"), h = I("height");
            var f = new VsFrame
            {
                Name = d.TryGetValue("name", out var n) ? n : "", Rot = rot, W = w, H = h,
                Src = rot ? new Rectangle(I("x"), I("y"), h, w) : new Rectangle(I("x"), I("y"), w, h),
                FX = -I("frameX"), FY = -I("frameY"),
                FW = d.ContainsKey("frameWidth") ? I("frameWidth") : w, FH = d.ContainsKey("frameHeight") ? I("frameHeight") : h,
            };
            a.Frames.Add(f);
            if (merge != null) _vsFrameTex[f] = tex;
        }
        return a;
    }
    private readonly Dictionary<VsFrame, Texture2D> _vsFrameTex = new();

    // ---------------------------------------------------------------- note style data (funkin.json / pixel.json)
    private sealed class VsStyle
    {
        public bool Pixel;
        public VsAtlas Strum; public float StrumScale; public Vector2 StrumOff; public float StrumW, StrumH;
        public string[] StaticP, PressP, ConfirmP, ConfirmHoldP;
        public VsAtlas Notes; public float NoteScale; public string[] NoteP; public float NoteW, NoteH; public int NoteFW0, NoteFH0;
        public Texture2D Hold; public float HoldScale; public Vector2 HoldOff; public float EndOffset, BottomClip;
        public VsAtlas Splash; public Texture2D SplashPremul; public float SplashScale, SplashAlpha; public Vector2 SplashOff; public int SplashFps, SplashVar; public bool SplashScreen;
        public string[][] SplashP;
        public VsAtlas Cover; public float CoverScale; public Vector2 CoverOff; public string[] CoverStartP, CoverHoldP, CoverEndP;
    }

    private VsStyle _vs;
    private bool _vsTried;
    private static readonly bool VsDisabled = Environment.GetEnvironmentVariable("FNF_VS_STRUMS") == "0";

    /// <summary>True when the V-Slice strumline renderer draws this song's notes.</summary>
    private bool VsStrumActive
    {
        get
        {
            if (VsDisabled || PsychNotes || _chart == null) return false; // also with a pad / on Xbox (button icons = VsButtonSkin)
            string st = _chart.NoteStyle;
            if (!(string.IsNullOrEmpty(st) || st == "funkin" || st == "pixel")) return false;
            if (!_vsTried) { _vsTried = true; if (float.TryParse(Environment.GetEnvironmentVariable("FNF_DEV_SCROLLSPEED"), NumberStyles.Float, CultureInfo.InvariantCulture, out var dsp) && dsp > 0) _chart.Speed = dsp; /* dev: render-distance test */ try { _vs = VsBuildStyle(st == "pixel"); } catch (Exception e) { Console.WriteLine("[VSSTRUM] load failed: " + e.Message); _vs = null; } }
            return _vs != null;
        }
    }

    private VsStyle VsBuildStyle(bool pixel)
    {
        var s = new VsStyle { Pixel = pixel };
        string[] dirs = { "Left", "Down", "Up", "Right" };
        if (!pixel)
        {
            s.Strum = VsLoadAtlas("funkin/noteStrumline"); s.StrumScale = 0.7f; s.StrumOff = Vector2.Zero;
            s.StaticP = System.Array.ConvertAll(dirs, d => $"static{d}0"); s.PressP = System.Array.ConvertAll(dirs, d => $"press{d}0");
            s.ConfirmP = System.Array.ConvertAll(dirs, d => $"confirm{d}0"); s.ConfirmHoldP = s.ConfirmP;
            s.Notes = VsLoadAtlas("funkin/notes"); s.NoteScale = 0.7f; s.NoteP = System.Array.ConvertAll(dirs, d => $"note{d}");
            s.Hold = Assets.LoadTexture("images/vslice_notes/funkin/NOTE_hold_assets.png"); s.HoldScale = 0.7f; s.HoldOff = Vector2.Zero;
            s.EndOffset = 0.5f; s.BottomClip = 0.9f;
            s.Splash = VsLoadAtlas("funkin/noteSplashes"); s.SplashScale = 1f; s.SplashAlpha = 0.8f; s.SplashOff = new Vector2(25, -5);
            s.SplashFps = 24; s.SplashVar = 2; s.SplashScreen = false;
            s.SplashP = new[] {
                new[] { "note impact 1 purple0", "note impact 2 purple0" }, new[] { "note impact 1  blue0", "note impact 2 blue0" },
                new[] { "note impact 1 green0", "note impact 2 green0" }, new[] { "note impact 1 red0", "note impact 2 red0" } };
            VsAtlas cov = null;
            foreach (var c in new[] { "Purple", "Blue", "Green", "Red" }) cov = VsLoadAtlas("funkin/holdCover" + c, cov);
            s.Cover = cov; s.CoverScale = 1f; s.CoverOff = Vector2.Zero;
            string[] cols = { "Purple", "Blue", "Green", "Red" };
            s.CoverStartP = System.Array.ConvertAll(cols, c => $"holdCoverStart{c}"); s.CoverHoldP = System.Array.ConvertAll(cols, c => $"holdCover{c}");
            s.CoverEndP = System.Array.ConvertAll(cols, c => $"holdCoverEnd{c}");
        }
        else
        {
            s.Strum = VsLoadAtlas("pixel/arrows-pixels"); s.StrumScale = 6f; s.StrumOff = new Vector2(28, 32);
            s.StaticP = System.Array.ConvertAll(dirs, d => $"static{d}0"); s.PressP = System.Array.ConvertAll(dirs, d => $"pressed{d}0");
            s.ConfirmP = System.Array.ConvertAll(dirs, d => $"confirm{d}0"); s.ConfirmHoldP = s.ConfirmP;
            s.Notes = s.Strum; s.NoteScale = 6f; s.NoteP = System.Array.ConvertAll(dirs, d => $"note{d}");
            s.Hold = Assets.LoadTexture("images/vslice_notes/pixel/arrowEndsNew.png"); s.HoldScale = 6f; s.HoldOff = new Vector2(2, 1);
            s.EndOffset = 1f; s.BottomClip = 1f;
            s.Splash = VsLoadAtlas("pixel/pixelNoteSplash"); s.SplashScale = 4f; s.SplashAlpha = 1f; s.SplashOff = new Vector2(27.5f, 18.5f);
            s.SplashFps = 33; s.SplashVar = 2; s.SplashScreen = true;
            s.SplashPremul = VsPremultiply(s.Splash?.Tex);
            s.SplashP = new[] { new[] { "purple1", "purple2", "purple3" }, new[] { "blue1", "blue2", "blue3" },
                                new[] { "green1", "green2", "green3" }, new[] { "orange1", "orange2", "orange3" } };
            s.Cover = VsLoadAtlas("pixel/pixelNoteHoldCover"); s.CoverScale = 6f; s.CoverOff = new Vector2(29, -4);
            s.CoverStartP = new[] { "loop0000", "loop0000", "loop0000", "loop0000" }; s.CoverHoldP = new[] { "loop", "loop", "loop", "loop" };
            s.CoverEndP = new[] { "explode", "explode", "explode", "explode" };
        }
        if (s.Strum?.First == null || s.Notes?.First == null || s.Hold == null || s.Hold == Assets.Pixel) return null;
        // StrumlineNote.setup: scale.set(s); updateHitbox() while the sprite still shows frames[0] of the atlas.
        s.StrumW = s.Strum.First.FW * s.StrumScale; s.StrumH = s.Strum.First.FH * s.StrumScale;
        s.NoteFW0 = s.Notes.First.FW; s.NoteFH0 = s.Notes.First.FH;
        s.NoteW = s.NoteFW0 * s.NoteScale; s.NoteH = s.NoteFH0 * s.NoteScale;
        Console.WriteLine($"[VSSTRUM] style={(pixel ? "pixel" : "funkin")} strumHitbox={s.StrumW}x{s.StrumH} note={s.NoteW}x{s.NoteH} hold={s.Hold.Width}x{s.Hold.Height}");
        return s;
    }

    /// <summary>Premultiplied-alpha copy of a texture (OpenFL textures are premultiplied; needed for its SCREEN blend).</summary>
    private Texture2D VsPremultiply(Texture2D t)
    {
        if (t == null || t == Assets.Pixel) return null;
        try
        {
            var px = new Color[t.Width * t.Height];
            t.GetData(px);
            for (int i = 0; i < px.Length; i++) { var c = px[i]; px[i] = Color.FromNonPremultiplied(c.R, c.G, c.B, c.A); }
            var r = new Texture2D(t.GraphicsDevice, t.Width, t.Height);
            r.SetData(px);
            return r;
        }
        catch (Exception e) { Console.WriteLine("[VSSTRUM] premultiply failed: " + e.Message); return null; }
    }

    // ---------------------------------------------------------------- layout
    /// <summary>Strumline (group) x/y as in PlayState.initStrumlines.</summary>
    private float VsLineX(bool player)
    {
        if (_middlescroll && player) return FNFGame.SCREEN_WIDTH / 2f - (1.5f * VS_NOTE_SPACING + VS_INITIAL_OFFSET + _vs.StrumOff.X + _vs.StrumW / 2f);
        return player ? FNFGame.SCREEN_WIDTH / 2f + VS_X_OFFSET : VS_X_OFFSET;
    }
    private float VsLineY()
        => _downscroll ? FNFGame.SCREEN_HEIGHT - (_vs.StrumOff.Y + _vs.StrumH) - VS_Y_OFFSET - _vs.StrumOff.Y : VS_Y_OFFSET;

    /// <summary>StrumlineNote.x / y (hitbox top-left).</summary>
    public float VsStrumX(int lane, bool player) => VsLineX(player) + lane * VS_NOTE_SPACING + VS_INITIAL_OFFSET + _vs.StrumOff.X;
    public float VsStrumY() => VsLineY() + _vs.StrumOff.Y;

    // ---------------------------------------------------------------- strum animation state (StrumlineNote)
    private sealed class VsStrumState { public int Anim; public float T; public float HoldTimer = -1; public bool Fresh; public bool Holding; public bool KeyHeld; }
    private readonly VsStrumState[] _vsStrum = { new(), new(), new(), new(), new(), new(), new(), new() };
    private const int VS_STATIC = 0, VS_PRESS = 1, VS_CONFIRM = 2, VS_CONFIRM_HOLD = 3;

    private List<VsFrame> VsStrumFrames(int lane, int anim) => _vs.Strum.Prefix(anim switch
    {
        VS_PRESS => _vs.PressP[lane], VS_CONFIRM => _vs.ConfirmP[lane], VS_CONFIRM_HOLD => _vs.ConfirmHoldP[lane], _ => _vs.StaticP[lane]
    });

    private void VsPlay(VsStrumState s, int anim, bool force = true)
    {
        if (!force && s.Anim == anim) return;
        s.Anim = anim; s.T = 0; s.Fresh = true;
    }

    /// <summary>Strumline.hitNote -> playConfirm (and playNoteHoldCover for hold notes). Called from every note hit.</summary>
    private void VsStrumHit(bool player, int lane, PNoteS note)
    {
        if (_vs == null || lane < 0) return;
        lane %= 4;
        var s = _vsStrum[lane + (player ? 4 : 0)];
        VsPlay(s, VS_CONFIRM);
        bool realPlayer = player && !_botplay; // PlayState: playerStrumline isPlayer = !isBotPlayMode
        s.HoldTimer = realPlayer ? -1 : 0;
        if (note != null && note.SustainLength > 0) VsStartCover(player, lane);
    }

    private bool VsAnimFinished(VsStrumState s, int lane) => s.T * 24f >= VsStrumFrames(lane, s.Anim).Count;

    private void VsTickStrums(float dt)
    {
        double pos = _conductor.SongPosition;
        for (int i = 0; i < 8; i++)
        {
            var s = _vsStrum[i]; int lane = i % 4; bool player = i >= 4; bool realPlayer = player && !_botplay;
            if (!s.Fresh)
            {
                bool wasFinished = VsAnimFinished(s, lane);
                s.T += dt;
                if (!wasFinished && VsAnimFinished(s, lane) && realPlayer && s.Anim == VS_CONFIRM) s.HoldTimer = 0; // onAnimationFinished
                if (s.HoldTimer >= 0)
                {
                    s.HoldTimer += dt;
                    if (s.HoldTimer >= VS_CONFIRM_HOLD_TIME) { s.HoldTimer = -1; VsPlay(s, VS_STATIC); }
                }
            }
            s.Fresh = false;

            // Strumline.updateNotes: hold notes being hit -> holdConfirm; completed -> playPress/playStatic.
            bool holding;
            if (player) holding = _holdingNote[lane] && _holdNoteRef[lane] != null && pos > _holdNoteRef[lane].Time;
            else { var n = _noteField.GetActiveOpponentSustain(lane, pos); holding = n != null && pos > n.Time; }
            if (holding)
            {
                if (s.Anim == VS_CONFIRM_HOLD) { }
                else if (s.Anim == VS_CONFIRM) { if (VsAnimFinished(s, lane)) { s.HoldTimer = -1; VsPlay(s, VS_CONFIRM_HOLD, false); } }
                else VsPlay(s, VS_CONFIRM, false);
            }
            else if (s.Holding)
            {
                if (realPlayer && Input.NoteHeld[lane]) VsPlay(s, VS_PRESS); else VsPlay(s, VS_STATIC);
            }
            s.Holding = holding;
            if (VsTrace == i) { string k = $"{s.Anim}/{(int)(s.T * 24f)}"; if (k != _vsTraceLast) { _vsTraceLast = k; Console.WriteLine($"[VSTRACE] t={pos * 1000:F0} strum{i} anim={s.Anim} f={(int)(s.T * 24f)} holding={holding} timer={s.HoldTimer:F3}"); } }

            if (realPlayer)
            {
                bool held = Input.NoteHeld[lane];
                if (!held && s.KeyHeld) VsPlay(s, VS_STATIC);              // inputReleaseQueue -> playStatic
                if (held && s.Anim == VS_STATIC) VsPlay(s, VS_PRESS);      // isKeyHeld && static -> playPress
                s.KeyHeld = held;
            }
        }
    }

    // ---------------------------------------------------------------- hold covers (NoteHoldCover)
    private sealed class VsCover { public bool On; public int Anim; public float T; public bool Started; }
    private readonly VsCover[] _vsCover = { new(), new(), new(), new(), new(), new(), new(), new() };
    private readonly bool[] _vsHoldDropped = new bool[4];

    private void VsStartCover(bool player, int lane)
    {
        var c = _vsCover[lane + (player ? 4 : 0)];
        c.On = true; c.Anim = 0; c.T = 0; c.Started = false;
    }

    /// <summary>PlayScene.Holds DropPlayerHold: the cover is killed (no end animation).</summary>
    private void VsOnHoldDropped(int lane) { if (lane >= 0 && lane < 4) _vsHoldDropped[lane] = true; }

    private List<VsFrame> VsCoverFrames(int lane, int anim)
        => _vs.Cover?.Prefix(anim == 0 ? _vs.CoverStartP[lane] : anim == 1 ? _vs.CoverHoldP[lane] : _vs.CoverEndP[lane]);

    private void VsTickCovers(float dt)
    {
        if (_vs.Cover == null) return;
        double pos = _conductor.SongPosition;
        for (int i = 0; i < 8; i++)
        {
            var c = _vsCover[i]; int lane = i % 4; bool player = i >= 4;
            if (!c.On) continue;
            bool pending = player ? _holdingNote[lane] : _noteField.GetActiveOpponentSustain(lane, pos) != null;
            if (c.Started && !pending && c.Anim != 2)
            {
                // Hold finished: player -> playEnd(); opponent / dropped -> kill.
                if (player && !_vsHoldDropped[lane]) { c.Anim = 2; c.T = 0; continue; }
                c.On = false; _vsHoldDropped[lane] = false; continue;
            }
            if (pending) { c.Started = true; _vsHoldDropped[lane] = false; }
            else if (!c.Started) { c.T += dt; if (c.T > 0.25f) c.On = false; continue; }
            c.T += dt;
            var fr = VsCoverFrames(lane, c.Anim);
            int n = fr?.Count ?? 0;
            if (c.Anim == 0 && c.T * 24f >= n) { c.Anim = 1; c.T = 0; }                 // start finished -> playContinue
            else if (c.Anim == 2 && c.T * 24f >= n) { c.On = false; }                     // end finished -> kill
        }
    }

    // ---------------------------------------------------------------- splashes (NoteSplash)
    private sealed class VsSplash { public bool Player; public int Lane; public List<VsFrame> Frames; public float T; public float Fps; }
    private readonly List<VsSplash> _vsSplashes = new();
    private readonly float[] _vsPrevSplashTimer = new float[4];

    private void VsTickSplashes(float dt)
    {
        for (int i = 0; i < 4; i++)
        {
            float cur = _noteSplashTimer[i];
            if (cur > _vsPrevSplashTimer[i] + 0.0001f && _vs.Splash != null) // a new splash was triggered this frame (goodNoteHit sick)
            {
                var opts = _vs.SplashP[i];
                var fr = _vs.Splash.Prefix(opts[_random.Next(0, opts.Length)]);
                if (fr.Count > 0)
                    _vsSplashes.Add(new VsSplash { Player = true, Lane = i, Frames = fr, T = -dt, Fps = _vs.SplashFps + _random.Next(-_vs.SplashVar, _vs.SplashVar + 1) });
            }
            _vsPrevSplashTimer[i] = cur;
        }
        for (int i = _vsSplashes.Count - 1; i >= 0; i--)
        {
            var sp = _vsSplashes[i]; sp.T += dt;
            if (sp.T * sp.Fps >= sp.Frames.Count) _vsSplashes.RemoveAt(i);
        }
    }

    // ---------------------------------------------------------------- drawing
    /// <summary>flixel FlxSprite.draw of one atlas frame: frame-space point p lands at pos - offset + origin + (p - origin) * scale.</summary>
    private void VsDrawFrame(SpriteBatch sb, Texture2D tex, VsFrame f, float x, float y, float offX, float offY, float orgX, float orgY, float scale, Color col)
    {
        if (f == null) return;
        if (_vsFrameTex.TryGetValue(f, out var t2)) tex = t2;
        float left = x - offX + orgX + (f.FX - orgX) * scale;
        float top = y - offY + orgY + (f.FY - orgY) * scale;
        if (!f.Rot) sb.Draw(tex, new Vector2(left, top), f.Src, col, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        else sb.Draw(tex, new Vector2(left, top + f.H * scale), f.Src, col, -MathF.PI / 2f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }

    /// <summary>Screen rect (left, top, w, h) of the trimmed image VsDrawFrame would draw (unrotated size).</summary>
    private static (float l, float t, float w, float h) VsFrameBox(VsFrame f, float x, float y, float offX, float offY, float orgX, float orgY, float scale)
        => (x - offX + orgX + (f.FX - orgX) * scale, y - offY + orgY + (f.FY - orgY) * scale, f.W * scale, f.H * scale);

    /// <summary>Options > Note Skin = Controller Buttons (port option, default Arrows = original).</summary>
    private bool VsButtonSkin => UseControllerDisplay;
    private int _vsButtonSize;
    /// <summary>Button icon size = the static arrow's drawn size (min of w/h) of the current style.</summary>
    private int VsButtonSize()
    {
        if (_vsButtonSize > 0) return _vsButtonSize;
        var sf = _vs.Strum.Prefix(_vs.StaticP[0]);
        var f = sf.Count > 0 ? sf[0] : _vs.Strum.First;
        return _vsButtonSize = Math.Max(8, (int)MathF.Round(Math.Min(f.W, f.H) * _vs.StrumScale));
    }

    private void VsDrawStrum(SpriteBatch sb, int lane, bool player)
    {
        var mod = StrumMods[lane + (player ? 4 : 0)];
        if (!mod.Visible || mod.Alpha <= 0.01f) return;
        var s = _vsStrum[lane + (player ? 4 : 0)];
        var fr = VsStrumFrames(lane, s.Anim);
        if (fr.Count == 0) return;
        var f = fr[Math.Clamp((int)(s.T * 24f), 0, fr.Count - 1)];
        float x = VsStrumX(lane, player) + mod.OffsetX, y = VsStrumY() + mod.OffsetY;
        // Strumline.fadeInArrow: y -= 10, alpha 0 -> tween {y + 10, alpha 1} over 1 s, circOut, startDelay 0.5 + 0.2 * index.
        float ft = Math.Clamp((_vsIntroT - (0.5f + 0.2f * lane)) / 1f, 0f, 1f);
        float fe = MathF.Sqrt(1f - (ft - 1f) * (ft - 1f));
        y += -10f * (1f - fe);
        float introAlpha = fe;
        if (ButtonsFor(player))
        {
            // Button icon centred on the static arrow's drawn image, sized to it (same box for every anim).
            var sf = VsStrumFrames(lane, VS_STATIC); var f0 = sf.Count > 0 ? sf[0] : f;
            var (bl, bt, bw, bh) = VsFrameBox(f0, x, y, (f0.FW - _vs.StrumW) * 0.5f, (f0.FH - _vs.StrumH) * 0.5f, f0.FW * 0.5f, f0.FH * 0.5f, _vs.StrumScale);
            int size = VsButtonSize();
            // idle (grey) / PRESSED (pushed in) / CONFIRM (bright + lane glow): Engine/ControllerNoteSkin.cs
            bool conf = s.Anim == VS_CONFIRM || s.Anim == VS_CONFIRM_HOLD;
            if (!ControllerNoteSkin.DrawReceptor(sb, Assets, lane, Input.NoteFaceButtons[lane], new Vector2(bl + bw / 2f, bt + bh / 2f), size,
                    s.Anim == VS_PRESS, conf, mod.Alpha * introAlpha, s.Anim == VS_CONFIRM_HOLD ? 1f : s.T, HudArtScale))
                Assets.DrawButtonReceptor(sb, Input.NoteFaceButtons[lane], (int)MathF.Round(bl + bw / 2f - size / 2f), (int)MathF.Round(bt + bh / 2f - size / 2f), size,
                    pressed: s.Anim == VS_PRESS, confirm: conf, alpha: mod.Alpha * introAlpha);
            return;
        }
        // playAnimation -> centerOffsets() + centerOrigin() with the hitbox from setup().
        VsDrawFrame(sb, _vs.Strum.Tex, f, x, y, (f.FW - _vs.StrumW) * 0.5f, (f.FH - _vs.StrumH) * 0.5f, f.FW * 0.5f, f.FH * 0.5f, _vs.StrumScale,
            new Color(Color.White, mod.Alpha * introAlpha));
    }

    /// <summary>Maps texture rows [vA..vB] (in units of the texture height, repeating) onto screen rows [yA..yB].</summary>
    private void VsDrawSustainSpan(SpriteBatch sb, Texture2D tex, int colX, int colW, float sx, float x, double vA, double vB, double yA, double yB, Color col)
    {
        if (Math.Abs(vB - vA) < 1e-9 || Math.Abs(yB - yA) < 0.01) return;
        int texH = tex.Height;
        double k = (yB - yA) / (vB - vA); // screen px per v unit
        double v = vA;
        int guard = 0;
        while (v < vB - 1e-9 && guard++ < 4096)
        {
            double tile = Math.Floor(v + 1e-9);
            double vEnd = Math.Min(vB, tile + 1);
            int r0 = (int)Math.Floor((v - tile) * texH + 1e-6), r1 = (int)Math.Ceiling((vEnd - tile) * texH - 1e-6);
            if (r1 > r0)
            {
                double ya = yA + ((tile + (double)r0 / texH) - vA) * k, yb = yA + ((tile + (double)r1 / texH) - vA) * k;
                float top = (float)Math.Min(ya, yb), hgt = (float)Math.Abs(yb - ya);
                var eff = yb < ya ? SpriteEffects.FlipVertically : SpriteEffects.None;
                sb.Draw(tex, new Vector2(x, top), new Rectangle(colX, r0, colW, r1 - r0), col, 0f, Vector2.Zero,
                    new Vector2(sx, hgt / (r1 - r0)), eff, 0f);
            }
            v = vEnd;
        }
    }

    /// <summary>SustainTrail.updateClipping + draw (drawTriangles with repeat) at hold sprite position (x, y).</summary>
    private void VsDrawSustain(SpriteBatch sb, int lane, float x, float y, double sustainMs, float speed, Color col)
    {
        var tex = _vs.Hold; float zoom = _vs.HoldScale;
        float gw = tex.Width / 8f * zoom;
        double gh = sustainMs * VS_PIXELS_PER_MS * speed;
        double clip = gh;
        if (clip <= 0.1) return;
        double bottomH = tex.Height * zoom * _vs.EndOffset, partH = clip - bottomH;
        int colW = tex.Width / 8, bodyX = colW * 2 * (lane % 4), endX = bodyX + colW;
        float sx = gw / colW;
        float dx = x - _vs.HoldOff.X, dy = y - _vs.HoldOff.Y;
        bool flip = _downscroll;
        double v0y = flip ? clip : gh - clip;
        double v2y = partH > 0 ? (flip ? bottomH : v0y + partH) : v0y;
        double v6y = flip ? tex.Height * (-_vs.BottomClip + _vs.EndOffset) * zoom : gh + tex.Height * (_vs.BottomClip - _vs.EndOffset) * zoom;
        if (partH > 0) VsDrawSustainSpan(sb, tex, bodyX, colW, sx, dx, -partH / tex.Height / zoom, 0, dy + v0y, dy + v2y, col);
        double endV0 = partH > 0 ? 0 : (bottomH - clip) / zoom / tex.Height;
        VsDrawSustainSpan(sb, tex, endX, colW, sx, dx, endV0, _vs.BottomClip, dy + v2y, dy + v6y, col);
    }

    /// <summary>Draws one strumline (Strumline zIndex order: strums 10, holds 20, notes 30, covers 40, splashes 50).</summary>
    private void VsDrawLine(SpriteBatch sb, bool player, List<PNoteS> notes)
    {
        if (PsoDrawPsychLine(sb, player, notes)) return;   // Psych Online: this side's player note skin / arrow colours (PlayScene.PsoPlayers.cs)
        float lineX = VsLineX(player), lineY = VsLineY();
        for (int i = 0; i < 4; i++) VsDrawStrum(sb, i, player);
        if (_countdownActive) { VsDrawCoversAndSplashes(sb, player); return; }

        double posMs = _conductor.SongPosition * 1000.0;
        float speed = _chart.Speed;
        float dirSign = _downscroll ? 1f : -1f;
        // holds
        foreach (var note in notes)
        {
            if (note.IsPlayerNote != player || note.SustainLength <= 0) continue;
            int lane = note.Lane % 4;
            var mod = StrumMods[lane + (player ? 4 : 0)];
            float alpha = (note.Alpha * note.MultAlpha) * PsychStrumAlphaFor(note) * HoldTrailMul;
            if (alpha <= 0) continue;
            double tMs = note.Time * 1000.0, fullMs = note.SustainLength * 1000.0;
            float gw = _vs.Hold.Width / 8f * _vs.HoldScale;
            float hx = lineX + lane * VS_NOTE_SPACING + VS_STRUMLINE_SIZE / 2f - gw / 2f + mod.OffsetX + note.OffsetX;
            double susMs; float hy;
            if (note.IsHit && posMs > tMs)
            {
                susMs = tMs + fullMs - posMs; // being hit: clip it off at the strumline
                if (susMs <= 10) continue;
                double h = susMs * VS_PIXELS_PER_MS * speed;
                hy = (float)(lineY - VS_INITIAL_OFFSET + VS_STRUMLINE_SIZE / 2f - (_downscroll ? h : 0));
            }
            else
            {
                susMs = fullMs;
                double h = susMs * VS_PIXELS_PER_MS * speed;
                double noteY = VS_PIXELS_PER_MS * (posMs - tMs) * speed * dirSign;
                hy = (float)(lineY - VS_INITIAL_OFFSET + noteY - (_downscroll ? h : 0) + VS_STRUMLINE_SIZE / 2f);
            }
            hy += mod.OffsetY + note.OffsetY;
            if (ButtonsFor(player))
            {
                // Solid lane-colour trail (the arrow hold art is coloured per direction, not per button).
                double len = susMs * VS_PIXELS_PER_MS * speed + _vs.Hold.Height * _vs.HoldScale * (_vs.BottomClip - _vs.EndOffset);
                if (len <= 0.1) continue;
                float bw2 = VsButtonSize() * 0.26f, cx = hx + gw / 2f, ol = Math.Max(2f, VsButtonSize() * 0.035f);
                float top = _downscroll ? (float)(hy - _vs.HoldOff.Y + susMs * VS_PIXELS_PER_MS * speed - len) : hy - _vs.HoldOff.Y;
                // lane-colour trail with a black outline (approved preview look)
                sb.Draw(Assets.Pixel, new Rectangle((int)MathF.Round(cx - bw2 / 2f - ol), (int)MathF.Round(top), (int)MathF.Round(bw2 + 2 * ol), (int)MathF.Round((float)len)),
                    new Color(Color.Black, Math.Clamp(alpha * 0.8f, 0f, 1f)));
                sb.Draw(Assets.Pixel, new Rectangle((int)MathF.Round(cx - bw2 / 2f), (int)MathF.Round(top), (int)MathF.Round(bw2), (int)MathF.Round((float)len)),
                    new Color(GetControllerLaneColor(lane), Math.Clamp(alpha * 0.85f, 0f, 1f)));
                continue;
            }
            VsDrawSustain(sb, lane, hx, hy, susMs, speed, new Color(Color.White, Math.Clamp(alpha, 0f, 1f)));
        }
        // notes
        foreach (var note in notes)
        {
            if (note.IsPlayerNote != player || note.IsHit) continue;
            int lane = note.Lane % 4;
            var mod = StrumMods[lane + (player ? 4 : 0)];
            float alpha = (note.Alpha * note.MultAlpha) * PsychStrumAlphaFor(note);
            if (alpha <= 0) continue;
            var fr = _vs.Notes.Prefix(_vs.NoteP[lane]);
            if (fr.Count == 0) continue;
            float nx = lineX + lane * VS_NOTE_SPACING - (_vs.NoteW - VS_STRUMLINE_SIZE) / 2f - VS_NUDGE + mod.OffsetX + note.OffsetX;
            float ny = (float)(lineY - VS_INITIAL_OFFSET + VS_PIXELS_PER_MS * (posMs - note.Time * 1000.0) * speed * dirSign) + mod.OffsetY + note.OffsetY;
            if (ny > FNFGame.SCREEN_HEIGHT + 50 || ny < -_vs.NoteH - 50) continue;
            note.Position = new Vector2(nx, ny);
            if (ButtonsFor(player))
            {
                var (l0, t0, w0, h0) = VsFrameBox(fr[0], nx, ny, -0.5f * (_vs.NoteW - _vs.NoteFW0), -0.5f * (_vs.NoteH - _vs.NoteFH0), _vs.NoteFW0 * 0.5f, _vs.NoteFH0 * 0.5f, _vs.NoteScale);
                if (ControllerNoteSkin.DrawNote(sb, Assets, lane, Input.NoteFaceButtons[lane], new Vector2(l0 + w0 / 2f, t0 + h0 / 2f), VsButtonSize(), alpha, 0f, HudArtScale))
                    continue;
                var bt = Assets.GetButtonSprite(Input.NoteFaceButtons[lane]);
                if (bt != null)
                {
                    var (l, t, w, h) = VsFrameBox(fr[0], nx, ny, -0.5f * (_vs.NoteW - _vs.NoteFW0), -0.5f * (_vs.NoteH - _vs.NoteFH0), _vs.NoteFW0 * 0.5f, _vs.NoteFH0 * 0.5f, _vs.NoteScale);
                    int size = VsButtonSize();
                    sb.Draw(bt, new Rectangle((int)MathF.Round(l + w / 2f - size / 2f), (int)MathF.Round(t + h / 2f - size / 2f), size, size), new Color(Color.White, Math.Clamp(alpha, 0f, 1f)));
                    continue;
                }
            }
            // NoteStyle.buildNoteSprite: scale.set(s); updateHitbox() on frames[0] -> offset = -0.5*(w - fw0), origin = fw0/2.
            VsDrawFrame(sb, _vs.Notes.Tex, fr[0], nx, ny, -0.5f * (_vs.NoteW - _vs.NoteFW0), -0.5f * (_vs.NoteH - _vs.NoteFH0),
                _vs.NoteFW0 * 0.5f, _vs.NoteFH0 * 0.5f, _vs.NoteScale, new Color(Color.White, Math.Clamp(alpha, 0f, 1f)));
        }
        VsDrawCoversAndSplashes(sb, player);
    }

    private void VsDrawCoversAndSplashes(SpriteBatch sb, bool player)
    {
        float lineX = VsLineX(player), lineY = VsLineY();
        if (_vs.Cover?.First != null)
        {
            var f0 = _vs.Cover.First;
            float cw = f0.FW * _vs.CoverScale; // group width = glow.width after updateHitbox (frames[0])
            for (int lane = 0; lane < 4; lane++)
            {
                var c = _vsCover[lane + (player ? 4 : 0)];
                if (!c.On || !c.Started) continue;
                var fr = VsCoverFrames(lane, c.Anim);
                if (fr == null || fr.Count == 0) continue;
                int fi = c.Anim == 1 ? (int)(c.T * 24f) % fr.Count : Math.Min((int)(c.T * 24f), fr.Count - 1);
                float x = lineX + lane * VS_NOTE_SPACING + VS_STRUMLINE_SIZE / 2f - cw / 2f + _vs.CoverOff.X * _vs.CoverScale - 12f;
                float y = lineY + VS_INITIAL_OFFSET + VS_STRUMLINE_SIZE / 2f + _vs.CoverOff.Y * _vs.CoverScale - 96f;
                var mod = StrumMods[lane + (player ? 4 : 0)];
                VsDrawFrame(sb, _vs.Cover.Tex, fr[fi], x + mod.OffsetX, y + mod.OffsetY, -0.5f * (cw - f0.FW), -0.5f * (f0.FH * _vs.CoverScale - f0.FH),
                    f0.FW * 0.5f, f0.FH * 0.5f, _vs.CoverScale, new Color(Color.White, HoldSplashMul));
            }
        }
        if (_vsSplashes.Count > 0 && _vs.Splash?.First != null)
        {
            var f0 = _vs.Splash.First;
            float w = f0.FW * _vs.SplashScale, h = f0.FH * _vs.SplashScale;
            bool screen = _vs.SplashScreen && _vs.SplashPremul != null;
            if (screen) { sb.End(); sb.Begin(samplerState: SamplerState.PointClamp, blendState: VsScreenBlend, transformMatrix: _hudBatchMatrix); }
            foreach (var sp in _vsSplashes)
            {
                if (sp.Player != player || sp.T < 0) continue;
                var f = sp.Frames[Math.Clamp((int)(sp.T * sp.Fps), 0, sp.Frames.Count - 1)];
                float x = lineX + sp.Lane * VS_NOTE_SPACING + VS_INITIAL_OFFSET + _vs.SplashOff.X * _vs.SplashScale;
                float y = lineY - VS_INITIAL_OFFSET + _vs.SplashOff.Y * _vs.SplashScale;
                // NoteSplash.play: offset.set(width * 0.3, height * 0.3); origin stays at frames[0] centre (updateHitbox).
                float sa = Math.Clamp(_vs.SplashAlpha * NoteSplashMul, 0f, 1f);
                VsDrawFrame(sb, screen ? _vs.SplashPremul : _vs.Splash.Tex, f, x, y, w * 0.3f, h * 0.3f, f0.FW * 0.5f, f0.FH * 0.5f, _vs.SplashScale,
                    screen ? Color.White * sa : new Color(Color.White, sa));
            }
            if (screen) { sb.End(); sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied, transformMatrix: _hudBatchMatrix); }
        }
    }

    /// <summary>OpenFL BlendMode.SCREEN: glBlendFunc(ONE, ONE_MINUS_SRC_COLOR) on premultiplied source
    /// (dst' = src + dst * (1 - src)); used with the premultiplied splash copy.</summary>
    private static readonly BlendState VsScreenBlend = new()
    {
        ColorSourceBlend = Blend.One, ColorDestinationBlend = Blend.InverseSourceColor, ColorBlendFunction = BlendFunction.Add,
        AlphaSourceBlend = Blend.One, AlphaDestinationBlend = Blend.InverseSourceColor,
    };

    private static readonly int VsTrace = int.TryParse(Environment.GetEnvironmentVariable("FNF_VSSTRUMTRACE"), out var vt) ? vt : -1;
    private string _vsTraceLast;
    private readonly System.Diagnostics.Stopwatch _vsIntroClock = new();
    private double _vsIntroLast; private float _vsIntroT;
    private static readonly bool VsHudTrace = Environment.GetEnvironmentVariable("FNF_VSHUDTRACE") == "1";
    private float _vsLastTickSongPos = float.NaN;
    private static readonly bool VsLog = Environment.GetEnvironmentVariable("FNF_VSSTRUMLOG") == "1";
    private bool _vsLogged;

    /// <summary>Draw both strumlines (opponent zIndex 1000, player 1001) above the health bar / popups (800-900).</summary>
    private void DrawVsStrumlines(SpriteBatch sb)
    {
        // Advance by the song clock between draws (update ticks can outnumber draws; _lastDelta is one tick only).
        float sp = (float)_conductor.SongPosition;
        float dt = float.IsNaN(_vsLastTickSongPos) ? 0f : sp - _vsLastTickSongPos;
        if (dt < 0f || dt > 0.25f) dt = dt < 0f ? 0f : 0.25f;
        _vsLastTickSongPos = sp;
        // fadeInArrows runs from PlayState.create (real time; FlxG.elapsed is capped at 0.1 s per frame).
        double now = _vsIntroClock.Elapsed.TotalSeconds; if (!_vsIntroClock.IsRunning) { _vsIntroClock.Start(); now = 0; }
        _vsIntroT += (float)Math.Min(0.1, Math.Max(0.0, now - _vsIntroLast)); _vsIntroLast = now;
        if (VsHudTrace && sp > 8f && sp < 14f) Console.WriteLine($"[VSHUD] t={sp * 1000:F0} hud={1f + _hudZoomAdd:F5}");
        VsTickStrums(dt); VsTickCovers(dt); VsTickSplashes(dt);
        // Strumline.renderDistanceMs = FlxG.height / PIXELS_PER_MS / (scrollSpeed < 1 ? scrollSpeed : 1): notes are built
        // (and drawn) once their time is within that distance - slow scroll speeds see further ahead, no pop-in.
        float spd = _chart.Speed > 0 ? _chart.Speed : 1f;
        _noteField.RenderAheadSec = FNFGame.SCREEN_HEIGHT / VS_PIXELS_PER_MS / (spd < 1f ? spd : 1f) / 1000.0;
        var notes = _noteField.GetVisibleNotes(_conductor.SongPosition);
        if (!_middlescroll) VsDrawLine(sb, false, notes);
        VsDrawLine(sb, true, notes);
        if (VsLog && !_vsLogged) { _vsLogged = true; VsLogLayout(); }
    }

    private void VsLogLayout()
    {
        var sbd = new System.Text.StringBuilder("[VSSTRUM]");
        for (int p = 0; p < 2; p++)
            for (int i = 0; i < 4; i++)
                sbd.Append($" {(p == 1 ? 'P' : 'O')}{i}=({VsStrumX(i, p == 1):F2},{VsStrumY():F2} {_vs.StrumW:F1}x{_vs.StrumH:F1} s{_vs.StrumScale})");
        sbd.Append($" lineY={VsLineY():F2} down={_downscroll}");
        Console.WriteLine(sbd.ToString());
    }

    /// <summary>Per-shot dump (FNF_VSSTRUMLOG=1) comparable with the original module's STRUM/NOTE/HOLD lines.</summary>
    public string VsShotInfo()
    {
        if (_vs == null || !VsStrumActive) return "";
        var sbd = new System.Text.StringBuilder();
        string[] an = { "static", "press", "confirm", "confirm-hold" };
        sbd.Append($" intro={_vsIntroT:F3} dev={Input.LastDevice} skin={(VsButtonSkin ? "buttons" : "arrows")} ahead={_noteField.RenderAheadSec:F3}");
        for (int i = 0; i < 8; i++)
        {
            var s = _vsStrum[i];
            sbd.Append($" {(i >= 4 ? 'P' : 'O')}{i % 4}:{an[s.Anim]}/{(int)(s.T * 24f)}@{VsStrumX(i % 4, i >= 4):F1},{VsStrumY():F1}");
        }
        for (int i = 0; i < 8; i++)
        {
            var c = _vsCover[i]; if (!c.On || !c.Started || _vs.Cover?.First == null) continue;
            float cw = _vs.Cover.First.FW * _vs.CoverScale; int lane = i % 4; bool pl = i >= 4;
            float x = VsLineX(pl) + lane * VS_NOTE_SPACING + VS_STRUMLINE_SIZE / 2f - cw / 2f + _vs.CoverOff.X * _vs.CoverScale - 12f;
            float y = VsLineY() + VS_INITIAL_OFFSET + VS_STRUMLINE_SIZE / 2f + _vs.CoverOff.Y * _vs.CoverScale - 96f;
            sbd.Append($" COVER{(pl ? 'P' : 'O')}{lane}:{(c.Anim == 0 ? "start" : c.Anim == 1 ? "hold" : "end")}/{(int)(c.T * 24f)}@{x:F1},{y:F1}");
        }
        foreach (var sp in _vsSplashes) sbd.Append($" SPLASH{sp.Lane}:{(int)(sp.T * sp.Fps)}/{sp.Frames.Count}");
        foreach (var n in _noteField.GetVisibleNotes(_conductor.SongPosition))
            if (!n.IsHit) sbd.Append($" N{(n.IsPlayerNote ? 'P' : 'O')}{n.Lane % 4}t{n.Time * 1000:F0}@{n.Position.X:F2},{n.Position.Y:F2}");
        return sbd.ToString();
    }
}
