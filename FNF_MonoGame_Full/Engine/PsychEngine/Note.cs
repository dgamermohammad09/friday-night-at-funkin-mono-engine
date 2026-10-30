using System.Collections.Generic;
using Microsoft.Xna.Framework;
using ChartNote = FNF_MonoGame.Gameplay.Note;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Psych Engine gameplay note.
/// Direct port of objects/Note.hx — wraps the chart data (Chart.Note) with runtime
/// state, hit detection flags, RGB shader data, and note-splash configuration.
/// NoteField uses this as the active note type.
/// </summary>
public class Note
{
    public static readonly string[] DefaultNoteTypes = { "", "Alt Animation", "Hey!", "Hurt Note", "GF Sing", "No Animation" };
    public static readonly string[] ColArray = { "purple", "blue", "green", "red" };
    public const int SustainSize = 44;
    public const float SwagWidth = 160 * 0.7f;
    public const string DefaultNoteSkin = "noteSkins/NOTE_assets";

    public ChartNote Data { get; set; }

    public float StrumTime => (float)Data.Time;
    public int NoteData => Data.Lane;
    public bool MustPress => Data.IsPlayerNote;
    public bool MustHitSection => MustPress;
    public float SustainLength { get => (float)Data.SustainLength; set => Data.SustainLength = value; }
    public string NoteType
    {
        get => Data.NoteType;
        set { if (Data.NoteType != value) ApplyBuiltInType(Data, value); Data.NoteType = value; } // Note.set_noteType
    }

    /// <summary>objects/Note.hx set_noteType built-in defaults (Hurt Note colours/splash/gameplay, No Animation, GF Sing).</summary>
    public static void ApplyBuiltInType(ChartNote n, string value)
    {
        n.Rgb = null; n.SplashTexture = null; // defaultRGB(); noteSplashData.texture = SONG.splashSkin
        switch (value)
        {
            case "Hurt Note":
                n.IgnoreNote = n.IsPlayerNote; n.LowPriority = true; n.MissHealth = 0.1f; n.HitCausesMiss = true;
                n.Rgb = new uint[] { 0xFF101010, 0xFFFF0000, 0xFF990022 };
                n.SplashTexture = "noteSplashes/noteSplashes-electric";
                break;
            case "No Animation": n.NoAnimation = true; n.NoMissAnimation = true; break;
            case "GF Sing": n.GfNote = true; break;
        }
    }

    // Backwards-compatible aliases for code that expects Gameplay.Note fields.
    public double Time { get => Data.Time; set => Data.Time = value; }
    public int Lane { get => Data.Lane; set => Data.Lane = value; }
    public bool IsPlayerNote { get => Data.IsPlayerNote; set => Data.IsPlayerNote = value; }
    public bool IsHit { get => Data.IsHit; set => Data.IsHit = value; }
    public bool HoldComplete { get => Data.HoldComplete; set => Data.HoldComplete = value; }
    public string Kind { get => Data.Kind; set => Data.Kind = value; }
    public bool AltAnim { get => Data.AltAnim; set => Data.AltAnim = value; }
    public bool NoAnimation { get => Data.NoAnimation; set => Data.NoAnimation = value; }
    public bool NoMissAnimation { get => Data.NoMissAnimation; set => Data.NoMissAnimation = value; }

    public bool CanBeHit { get; set; }
    public bool TooLate { get; set; }
    public bool WasGoodHit { get; set; }
    public bool Missed { get; set; }
    public bool IgnoreNote { get => Data.IgnoreNote; set => Data.IgnoreNote = value; }
    public float HitHealth { get => Data.HitHealth; set => Data.HitHealth = value; }
    public float MissHealth { get => Data.MissHealth; set => Data.MissHealth = value; }
    public bool HitCausesMiss { get => Data.HitCausesMiss; set => Data.HitCausesMiss = value; }
    public string Texture { get => Data.Texture; set => Data.Texture = value; }
    /// <summary>The chart note this spawned note was created from (NoteField pool copy).</summary>
    public ChartNote Source { get; set; }
    /// <summary>Lua/HScript: alpha / visible of the spawned note (notes group).</summary>
    public float alpha { get => Data.Alpha; set => Data.Alpha = value; }
    public bool visible { get => Data.Visible; set => Data.Visible = value; }
    public bool HitByOpponent { get; set; }
    public bool NoteWasHit { get; set; }
    public bool Spawned { get; set; }
    public bool BlockHit { get => Data.BlockHit; set => Data.BlockHit = value; }
    public bool IsSustainNote => SustainLength > 0;
    // Lua/HScript exact Psych names (win over the case-insensitive PascalCase lookup): Psych strumTime/sustainLength are
    // in ms, and the port's Note views are always heads (tail pieces are not separate notes) -> isSustainNote = false.
    // Psych sustain tail pieces exist only as script-side views (PlayScene.PsychNoteGroup.cs): PieceIndex k >= 0 =
    // piece at head strumTime + stepCrochet * k (PlayState.generateSong), sharing the head's chart note data.
    public int PieceIndex { get; set; } = -1;
    public double PieceStepMs { get; set; }
    public float strumTime
    {
        get => (float)(Data.Time * 1000.0 + (PieceIndex >= 0 ? PieceStepMs * PieceIndex : 0.0));
        set { if (PieceIndex < 0) Data.Time = value / 1000.0; }
    }
    public float sustainLength { get => PieceIndex >= 0 ? 0f : (float)(Data.SustainLength * 1000.0); set { if (PieceIndex < 0) Data.SustainLength = value / 1000.0; } }
    public bool isSustainNote => PieceIndex >= 0;

    public Note PrevNote { get; set; }
    public Note NextNote { get; set; }
    public Note Parent { get; set; }
    public List<Note> Tail { get; } = new();

    public string AnimSuffix { get; set; } = "";
    public bool GfNote { get; set; }
    public float EarlyHitMult { get; set; } = 1f;
    public float LateHitMult { get; set; } = 1f;
    public float MultSpeed { get; set; } = 1f;
    public bool LowPriority { get => Data.LowPriority; set => Data.LowPriority = value; }
    public string Rating { get; set; }

    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public float OffsetAngle { get; set; }
    public float MultAlpha { get => Data.MultAlpha; set => Data.MultAlpha = value; }

    public bool CopyX { get; set; } = true;
    public bool CopyY { get; set; } = true;
    public bool CopyAngle { get; set; } = true;
    public bool CopyAlpha { get; set; } = true;

    public Dictionary<string, object> ExtraData { get; } = new();

    public bool NoteSplashDisabled { get => Data.NoteSplashDisabled; set => Data.NoteSplashDisabled = value; }
    public string NoteSplashTexture { get => Data.SplashTexture; set => Data.SplashTexture = value; }
    public bool NoteSplashUseRGB { get; set; } = true;
    public float NoteSplashAlpha { get => Data.SplashAlpha; set => Data.SplashAlpha = value; }

    /// <summary>Lua/HScript 'noteSplashData.*' (objects/Note.hx NoteSplashData typedef).</summary>
    public PsychNoteSplashData noteSplashData => new(this);
    /// <summary>Lua/HScript 'rgbShader.*' (RGBShaderReference: r, g, b, enabled).</summary>
    public PsychNoteRgb rgbShader => new(this);

    public Vector2 Position { get; set; }
    public float x { get => Position.X; set => Position = new Vector2(value, Position.Y); }
    public float y { get => Position.Y; set => Position = new Vector2(Position.X, value); }
    public float Angle { get; set; }
    public float Alpha { get; set; } = 1f;
    public float Scale { get; set; } = 1f;
    public Color RgbColor { get; set; } = Color.White;

    public Note()
    {
        Data = new ChartNote();
    }

    public Note(ChartNote data)
    {
        Data = data;
        if (Data.AltAnim && string.IsNullOrEmpty(NoteType))
            AnimSuffix = "-alt";
        if (Data.GfNote)
            GfNote = true;
    }

    public void Reset()
    {
        Data.Time = 0;
        Data.Lane = 0;
        Data.SustainLength = 0;
        Data.IsPlayerNote = true;
        Data.IsHit = false;
        Data.HoldComplete = false;
        Data.Kind = null;
        Data.NoteType = null;
        Data.GfNote = false;
        Data.AltAnim = false;
        Data.NoAnimation = false;
        Data.ResetPsychFields();
        Source = null;

        CanBeHit = false;
        TooLate = false;
        WasGoodHit = false;
        Missed = false;
        HitByOpponent = false;
        NoteWasHit = false;
        Spawned = false;
        PrevNote = null;
        NextNote = null;
        Parent = null;
        Tail.Clear();
        AnimSuffix = "";
        GfNote = false;
        EarlyHitMult = 1f;
        LateHitMult = 1f;
        MultSpeed = 1f;
        OffsetX = 0;
        OffsetY = 0;
        OffsetAngle = 0;
        CopyX = true;
        CopyY = true;
        CopyAngle = true;
        CopyAlpha = true;
        ExtraData.Clear();
        NoteSplashTexture = null;
        NoteSplashUseRGB = true;
        NoteSplashAlpha = ClientPrefs.Data.SplashAlpha;
        Position = Vector2.Zero;
        Angle = 0;
        Alpha = 1f;
        Scale = 1f;
        RgbColor = Color.White;
    }
}

/// <summary>Note.noteSplashData view (disabled, texture, a, useRGBShader, r, g, b) over the chart note.</summary>
public sealed class PsychNoteSplashData
{
    private readonly Note _n;
    public PsychNoteSplashData(Note n) { _n = n; }
    public bool disabled { get => _n.NoteSplashDisabled; set => _n.NoteSplashDisabled = value; }
    public string texture { get => _n.NoteSplashTexture; set => _n.NoteSplashTexture = value; }
    public float a { get => _n.NoteSplashAlpha; set => _n.NoteSplashAlpha = value; }
    public bool useRGBShader { get => _n.NoteSplashUseRGB; set => _n.NoteSplashUseRGB = value; }
    public bool useGlobalShader { get; set; }
    public bool antialiasing { get; set; } = true;
    public int r { get; set; } = -1;
    public int g { get; set; } = -1;
    public int b { get; set; } = -1;
}

/// <summary>Note.rgbShader (RGBShaderReference) view: r/g/b colours and enabled, stored on the chart note.</summary>
public sealed class PsychNoteRgb
{
    private readonly Note _n;
    public PsychNoteRgb(Note n) { _n = n; }
    private uint Get(int i) => (_n.Data.Rgb ?? PsychNoteSkin.DefaultRgb(_n.Lane))[i];
    private void Set(int i, double v)
    {
        var a = (uint[])(_n.Data.Rgb ?? PsychNoteSkin.DefaultRgb(_n.Lane)).Clone();
        a[i] = 0xFF000000u | ((uint)(long)v & 0xFFFFFFu);
        _n.Data.Rgb = a;
    }
    // FlxColor is a signed Int in Haxe (0xFF... reads back negative)
    public double r { get => unchecked((int)Get(0)); set => Set(0, value); }
    public double g { get => unchecked((int)Get(1)); set => Set(1, value); }
    public double b { get => unchecked((int)Get(2)); set => Set(2, value); }
    public bool enabled { get => !_n.Data.RgbDisabled; set => _n.Data.RgbDisabled = !value; }
}

/// <summary>Psych 1.0.4 note colours (backend/ClientPrefs.hx arrowRGB defaults).</summary>
public static class PsychNoteSkin
{
    public static readonly uint[][] ArrowRGB =
    {
        new uint[] { 0xFFC24B99, 0xFFFFFFFF, 0xFF3C1F56 },
        new uint[] { 0xFF00FFFF, 0xFFFFFFFF, 0xFF1542B7 },
        new uint[] { 0xFF12FA05, 0xFFFFFFFF, 0xFF0A4447 },
        new uint[] { 0xFFF9393F, 0xFFFFFFFF, 0xFF651038 },
    };
    /// <summary>Note.defaultRGB: ClientPrefs.data.arrowRGB (Options > Note Colors), Psych defaults when unset/invalid.</summary>
    private static bool _rgbLogged;
    public static uint[] DefaultRgb(int lane)
    {
        int l = ((lane % 4) + 4) % 4;
        var a = ClientPrefs.Data?.ArrowRGB;
        if (!_rgbLogged && System.Environment.GetEnvironmentVariable("FNF_RGBLOG") == "1") { _rgbLogged = true; System.Console.WriteLine("[RGBLOG] loaded=" + ClientPrefs.Loaded + " a0=" + (a != null && a.Length > 0 && a[0] != null ? a[0][0].ToString("X8") : "null")); }
        return a != null && a.Length > l && a[l] != null && a[l].Length >= 3 ? a[l] : ArrowRGB[l];
    }
    public static readonly string[] ColArray = { "purple", "blue", "green", "red" };
}
