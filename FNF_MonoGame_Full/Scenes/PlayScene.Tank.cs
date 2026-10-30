using System;
using System.Collections.Generic;
using Note = FNF_MonoGame.Engine.PsychEngine.Note;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Scenes;

/// <summary>tankmanBattlefield.hxc buildStage: clouds = FlxTiledSprite(tankClouds, 3200, 235, repeatX) at (-1100, 20),
/// scrollFactor 0.25, zIndex 12, velocity.x = 8 (the JSON prop clouds2 does not move).</summary>
public partial class PlayScene
{
    private StaticStageProp[] _tankCloudTiles;
    private float _tankCloudX = -1100f;

    private void TankCloudsUpdate(float dt)
    {
        if (_chart?.Stage != "tankmanBattlefield") return;
        if (_tankCloudTiles == null)
        {
            var tex = LoadStagePropTexture("tankClouds") ?? Assets.LoadTexture("game/stages/tankman/tankClouds.png");
            if (tex == null) { _tankCloudTiles = new StaticStageProp[0]; return; }
            const int W = 3200, H = 235;
            int n = (W + tex.Width - 1) / tex.Width;
            _tankCloudTiles = new StaticStageProp[n];
            for (int i = 0; i < n; i++)
            {
                int w = Math.Min(tex.Width, W - i * tex.Width);
                var p = new StaticStageProp
                {
                    Texture = tex, Name = "tankCloudsTiled" + i, ScaleX = 1f, ScaleY = 1f, Alpha = 1f,
                    ScrollX = 0.25f, ScrollY = 0.25f, ZIndex = 12, Y = 20f,
                    SourceRect = new Rectangle(0, 0, w, Math.Min(H, tex.Height)),
                };
                _tankCloudTiles[i] = p;
                _staticProps.Add(p);
            }
            SortStaticProps();
        }
        _tankCloudX += 8f * dt;
        for (int i = 0; i < _tankCloudTiles.Length; i++)
            _tankCloudTiles[i].X = _tankCloudX + i * (_tankCloudTiles[i].Texture?.Width ?? 0);
    }

    // ------------------------------------------------------------------ TankmanSprite / TankmanSpriteGroup (stress, stress-pico)

    /// <summary>World position of the flixel-animate timeline box of a TankmanSprite. TankmanSprite.new():
    /// setGraphicSize(Std.int(width * 0.4)) + updateHitbox() (offset = -0.5 * (hitbox - frame), origin = frame centre),
    /// then createTankman() overrides scale with 1.10 (erect) / 1.0; flixel scales about the origin, so the box top-left
    /// is x - offset.x + origin.x * (1 - scale) (orig: fw 1254x856, offset 376.5,257.005, origin 627,428).</summary>
    private Vector2 TankmanFlxDrawPos(FNF_MonoGame.Engine.AnimatedSprite spr)
    {
        var tb = spr.Sheet?.GetTimelineBounds(spr.ResolvedAnimName ?? "Running") ?? spr.Sheet?.GetTimelineBounds("Running");
        if (tb == null) return spr.Position;
        float fw = (int)tb.Value.W, fh = (int)tb.Value.H;
        float hw = (int)(fw * 0.4f);                 // setGraphicSize(Std.int(width * 0.4)), height 0 -> scale.y = scale.x
        float s04 = hw / fw, hh = s04 * fh;
        float offX = -0.5f * (hw - fw), offY = -0.5f * (hh - fh);
        float sc = spr.Scale.X;
        return new Vector2(spr.Position.X - offX + fw * 0.5f * (1f - sc), spr.Position.Y - offY + fh * 0.5f * (1f - sc));
    }

    private readonly int[] _tmSlots = { -1, -1, -1, -1 };
    private readonly string[] _tmSlotShot = new string[4];
    private int _tmSlotCount, _tmMarker;
    private double? _tmPosOverrideMs; private float _tmNowMs;

    // TankmanSpriteGroup state that survives a restart (the stage/group object lives on through onSongRetry):
    // tankmanDirs is never cleared by initTimemap (only tankmanTimes is), and group._marker is never reset by clear().
    private static readonly List<bool> s_tmDirs = new();
    private static int s_tmMarker;
    private static string s_tmRetryOf; // set by BuildParams() (restart of the same song)
    private static readonly bool s_tmLog = Environment.GetEnvironmentVariable("FNF_TANKLOG") == "1";

    /// <summary>FlxTypedGroup.recycle with maxSize 4: below 4 members a new one is created (TankmanSprite.new() picks
    /// 'Shot ' + FlxG.random.int(1, 2) once per object); once full, members[_marker++] is revived and reused whatever
    /// its state (a still-running tankman is taken over by the new one). Then createTankman: goingRight = tankmanDirs.shift().</summary>
    private void StressTankmanSpawn(ref StressTankman tm, int idx)
    {
        int slot;
        if (_tmSlotCount < 4)
        {
            slot = _tmSlotCount++;
            _tmSlotShot[slot] = _random.Next(1, 3) == 1 ? "Shot 1" : "Shot 2";
        }
        else
        {
            slot = _tmMarker++;
            if (_tmMarker >= 4) _tmMarker = 0;
            s_tmMarker = _tmMarker;
            int prev = _tmSlots[slot];
            if (prev >= 0 && prev != idx && prev < _stressTankmen.Count)
            {
                var old = _stressTankmen[prev];
                old.IsDead = true; old.Sprite = null;
                _stressTankmen[prev] = old;
            }
        }
        _tmSlots[slot] = idx;
        if (tm.ShotAnim == null) tm.ShotAnim = _tmSlotShot[slot]; // (FNF_DEV_TANKMEN may fix it)
        if (!tm.DirFixed)
        {
            // tankmanDirs.shift(): an empty Haxe array shifts null -> flipX = !null... = goingRight false (never happens: dirs >= times)
            if (s_tmDirs.Count > 0) { tm.GoingRight = s_tmDirs[0]; s_tmDirs.RemoveAt(0); }
            else tm.GoingRight = false;
        }
        if (s_tmLog)
            Console.WriteLine($"[TANKSPAWN] t={_tmNowMs:0.0} slot={slot} strum={tm.StrumTime:0.0} right={tm.GoingRight} speed={tm.RunSpeed:0.000} endOff={tm.EndingOffset:0.0} shot={tm.ShotAnim}");
    }

    /// <summary>TankmanSpriteGroup.initTimemap for the picospeaker chart of the current variation: every note is a tankman
    /// with FlxG.random.bool(1/16*100); goingRight = note.data not 2/3 (pushed to the never-cleared tankmanDirs).
    /// stress(-pico).hxc reset()s the group TWICE per play: onCountdownStart before the video (then the group spawns
    /// the notes within 3 s of the countdown position -5 beats during the video, shifting their dirs) and again when
    /// the countdown really starts (group.clear() drops those). A restart = onSongRetry + onCountdownStart (no update
    /// in between). Hence the dirs actually used are those of the earlier draw(s), offset by what was spawned.
    /// Dev: FNF_DEV_TANKMEN="strum:R|L:speed:endOff[:shot 1|2];..." replays an exact list (e.g. logged from the original).</summary>
    private void StressTankmenBuild(List<Note> notes)
    {
        notes.Sort((a, b) => a.Time.CompareTo(b.Time));
        bool retry = s_tmRetryOf != null && string.Equals(s_tmRetryOf, _songName, StringComparison.OrdinalIgnoreCase);
        s_tmRetryOf = null;
        if (!retry) { s_tmDirs.Clear(); s_tmMarker = 0; }
        _tmMarker = s_tmMarker;

        string dev = Environment.GetEnvironmentVariable("FNF_DEV_TANKMEN");
        if (!string.IsNullOrEmpty(dev))
        {
            foreach (var e in dev.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var f = e.Split(':');
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                _stressTankmen.Add(new StressTankman
                {
                    StrumTime = float.Parse(f[0], ci), GoingRight = f[1] == "R", DirFixed = true,
                    RunSpeed = float.Parse(f[2], ci), EndingOffset = float.Parse(f[3], ci),
                    ShotAnim = f.Length > 4 ? "Shot " + f[4] : null,
                });
            }
            Console.WriteLine($"[STRESS-INIT] FNF_DEV_TANKMEN: {_stressTankmen.Count} tankmen");
            return;
        }

        List<float> Draw()
        {
            var times = new List<float>();
            foreach (var note in notes)
            {
                if (_random.Next(16) != 0) continue; // FlxG.random.bool(6.25)
                times.Add((float)(note.Time * 1000.0));
                s_tmDirs.Add(!(note.Lane == 2 || note.Lane == 3));
            }
            return times;
        }

        if (!retry)
        {
            // First reset (onCountdownStart that starts the video): spawned at songPosition = -crochet * 5 while the video plays.
            var first = Draw();
            double pos0 = -_conductor.Crochet * 5.0 * 1000.0;
            int spawned = 0;
            foreach (var tt in first) if (tt <= pos0 + 3000.0) spawned++; else break;
            s_tmDirs.RemoveRange(0, Math.Min(spawned, s_tmDirs.Count));
            if (s_tmLog) Console.WriteLine($"[TANKSPAWN] init1 times={first.Count} spawnedDuringVideo={spawned} pos0={pos0:0.0}");
        }
        else Draw(); // onSongRetry reset
        var times2 = Draw(); // onCountdownStart reset
        foreach (var st in times2)
        {
            _stressTankmen.Add(new StressTankman
            {
                StrumTime = st,
                RunSpeed = 0.6f + (float)_random.NextDouble() * 0.4f,     // FlxG.random.float(0.6, 1)
                EndingOffset = 50f + (float)_random.NextDouble() * 150f,  // FlxG.random.float(50, 200)
            });
        }
        if (s_tmLog)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var x in _stressTankmen) if (x.StrumTime <= 62000) sb.Append(' ').Append(Math.Round(x.StrumTime));
            Console.WriteLine($"[TANKSPAWN] init2 retry={retry} times={times2.Count} dirsQueued={s_tmDirs.Count} first60:{sb}");
        }
    }
}
