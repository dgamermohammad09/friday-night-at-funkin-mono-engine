using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// characters/pico-playable.hxc onAnimationFrame: 'cock' frame 3 -> createCasing(): one FlxTypedSpriteGroup at
/// (pico.x + 250, pico.y + 100), zIndex 1000 (created once per song, kept until retry), + a CasingSprite
/// (stages/props/CasingSprite.hxc): Sparrow 'PicoBullet', 'pop' = "Pop0" 24 fps once, 'idle' = "Bullet0" 24 fps loop;
/// at pop frame 40: x += frame.offset.x - 1, y += frame.offset.y + 1, angle 125.1, velocity.x = 20*B, drag.x = A*B,
/// angularVelocity 100, angularDrag = drag.x / velocity.x * 100 (A = random 3..10, B = random 1..2), 'idle'.
/// Motion = FlxObject.updateMotion (half-step velocity integration, FlxVelocity.computeVelocity drag) before
/// updateAnimation. The sprite origin stays the centre of frames[0] (Bullet0000, 24x25) like flixel.
/// </summary>
public partial class PlayScene
{
    private sealed class PicoCasing
    {
        public float X, Y, Angle, Vx, DragX, AngVel, AngDrag, AnimT;
        public int Frame;
        public bool Rolling;
    }
    private readonly List<PicoCasing> _casings = new();
    private bool _casingGroupMade;
    private float _casingGroupX, _casingGroupY;
    private SpriteSheet _casingSheet;
    private StaticStageProp _casingProp;   // casingGroup (its colour = 2hot darkenStageProps tint)
    private List<SpriteFrame> _casingPop, _casingIdle;
    private string _casingLastAnim; private int _casingLastFrame = -1;
    private readonly Random _casingRng = new();
    private static readonly bool _casingLog = Environment.GetEnvironmentVariable("FNF_CASINGLOG") == "1";

    private void CasingCreate()
    {
        if (_boyfriend == null) return;
        if (_casingSheet == null)
        {
            try { _casingSheet = SpriteSheet.Load(Game, "images/weekend1/PicoBullet"); } catch (Exception ex) { Console.WriteLine($"[CASING] {ex.Message}"); }
            if (_casingSheet == null) return;
            _casingPop = _casingSheet.BuildAnimationFromPrefix("Pop0") ?? _casingSheet.BuildAnimationFromPrefix("Pop");
            _casingIdle = _casingSheet.BuildAnimationFromPrefix("Bullet0") ?? _casingSheet.BuildAnimationFromPrefix("Bullet");
            if (_casingPop == null || _casingIdle == null) { Console.WriteLine("[CASING] PicoBullet frames missing"); _casingSheet = null; return; }
        }
        if (!_casingGroupMade)
        {
            _casingGroupMade = true;
            _casingGroupX = _boyfriend.Position.X + 250f;
            _casingGroupY = _boyfriend.Position.Y + 100f;
            var prop = new SsfProp { Name = "picoCasings", ZIndex = 1000, Visible = true, Alpha = 1f, ScaleX = 1f, ScaleY = 1f, ScrollX = 1f, ScrollY = 1f, DrawFn = CasingDraw };
            _casingProp = prop;
            _staticProps.Add(prop);
            _staticProps = _staticProps.OrderBy(p => p.ZIndex).ToList();
        }
        _casings.Add(new PicoCasing { X = _casingGroupX, Y = _casingGroupY });
        if (_casingLog) Console.WriteLine($"[CASING] pop={_casingPop.Count} idle={_casingIdle.Count} keys={string.Join(",", _casingSheet.Frames.Keys.Take(4))} create #{_casings.Count} at {_casingGroupX},{_casingGroupY} song={SongPositionSeconds * 1000:F0}");
    }

    private static float FlxComputeVelocity(float v, float drag, float elapsed)
    {
        if (drag != 0f)
        {
            float d = drag * elapsed;
            if (v - d > 0f) v -= d;
            else if (v + d < 0f) v += d;
            else v = 0f;
        }
        return Math.Clamp(v, -10000f, 10000f);
    }

    /// <summary>Per frame (not while paused).</summary>
    private void CasingUpdate(float dt)
    {
        // onAnimationFrame('cock', 3)
        if (_boyfriend != null && GoPlayerId == "pico-playable")
        {
            string a = _boyfriend.CurrentAnimation; int f = _boyfriend.SpriteFrameIndex;
            if (a == "cock" && f >= 3 && (_casingLastAnim != "cock" || _casingLastFrame < 3)) CasingCreate(); // onFrameChange fires for every frame passed
            _casingLastAnim = a; _casingLastFrame = f;
        }
        foreach (var c in _casings)
        {
            // FlxObject.updateMotion
            if (c.Rolling)
            {
                float dv = 0.5f * (FlxComputeVelocity(c.AngVel, c.AngDrag, dt) - c.AngVel);
                c.AngVel += dv; c.Angle += c.AngVel * dt; c.AngVel += dv;
                dv = 0.5f * (FlxComputeVelocity(c.Vx, c.DragX, dt) - c.Vx);
                c.Vx += dv; float delta = c.Vx * dt; c.Vx += dv; c.X += delta;
            }
            // updateAnimation (24 fps)
            c.AnimT += dt;
            const float fd = 1f / 24f;
            if (!c.Rolling)
            {
                while (c.AnimT >= fd && c.Frame < _casingPop.Count - 1)
                {
                    c.AnimT -= fd; c.Frame++;
                    if (c.Frame == 40) { CasingStartRoll(c); break; } // onFrameChange('pop', 40)
                }
            }
            else
            {
                while (c.AnimT >= fd) { c.AnimT -= fd; c.Frame = (c.Frame + 1) % _casingIdle.Count; }
            }
        }
    }

    private void CasingStartRoll(PicoCasing c)
    {
        var fr = _casingPop[40];
        c.X = c.X + fr.Offset.X - 1f;
        c.Y = c.Y + fr.Offset.Y + 1f;
        c.Angle = 125.1f;
        float A = 3f + (float)_casingRng.NextDouble() * 7f, B = 1f + (float)_casingRng.NextDouble();
        c.Vx = 20f * B; c.DragX = A * B;
        c.AngVel = 100f; c.AngDrag = c.DragX / c.Vx * 100f;
        c.Rolling = true; c.Frame = 0; c.AnimT = 0f;
        if (_casingLog) Console.WriteLine($"[CASING] roll at {c.X:F1},{c.Y:F1} vx={c.Vx:F2} drag={c.DragX:F2}");
    }

    private void CasingDraw(SpriteBatch sb)
    {
        if (_casingSheet == null) return;
        var tex = _casingSheet.Texture;
        var tint = _casingProp?.OverlayColor ?? Color.White; // FlxSpriteGroup.color (2hot.hxc darkenStageProps darkens every stage member)
        foreach (var c in _casings)
        {
            var fr = c.Rolling ? _casingIdle[c.Frame % _casingIdle.Count] : _casingPop[Math.Min(c.Frame, _casingPop.Count - 1)];
            var t = fr.Texture ?? tex;
            var pos = WorldToScreen(c.X, c.Y, 1f, 1f);
            if (_casingLog) Console.WriteLine($"[CASING] draw song={SongPositionSeconds * 1000:F0} {(c.Rolling ? "idle" : "pop")}:{c.Frame} x={c.X:F1} y={c.Y:F1} a={c.Angle:F1}");
            // flixel: origin = centre of frames[0] (Bullet0000 24x25); rotate the (trimmed) frame about it
            var origin = new Vector2(12f, 12.5f);
            float rot = MathHelper.ToRadians(c.Angle);
            var off = fr.Offset;
            if (fr.Rotated)
            {
                // atlas stores the frame 90 deg CW: unrotated (u,v) = (ay, W - ax)
                float W = fr.SourceRect.Width;
                var o = origin - off;                         // origin in trimmed-frame space
                sb.Draw(t, pos + origin, fr.SourceRect, tint, rot - MathHelper.PiOver2, new Vector2(W - o.Y, o.X), 1f, SpriteEffects.None, 0f);
            }
            else
                sb.Draw(t, pos + origin, fr.SourceRect, tint, rot, origin - off, 1f, SpriteEffects.None, 0f);
        }
    }
}
