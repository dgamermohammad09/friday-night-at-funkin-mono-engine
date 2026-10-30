using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// ui/title/AttractState.hx (0.8.1): after TITLE_ATTRACT_DELAY on the title screen, play the next trailer
/// (riftCollabTrailer -> mobileRelease -> boyfriendEverywhere, static index), then go back to the TitleState.
/// Holding any key for HOLD_TIME (1.5 s) skips: FlxPieDial(radius 40, 45 frames, CIRCLE, clockwise, inner 20) at
/// (FlxG.width - 1.5*w, FlxG.height - 1.5*h), alpha = remap(amount, 0.025..1 -> 0..1). Port: any gamepad button counts.
/// </summary>
public class AttractScene : Scene
{
    private static readonly string[] VideoIds = { "riftCollabTrailer", "mobileRelease", "boyfriendEverywhere" };
    private static int s_nextVideo;
    private const float HoldTime = 1.5f;
    private const int PieRadius = 40, PieInner = 20, PieFrames = 45;

    private VideoCutscenePlayer _vid;
    private bool _ended;
    private float _holdDelta, _pieScale = 1f, _pieAmount;
    private Texture2D _pieTex;
    private int _pieFrame = -1;
    private Color[] _pieRing;

    private float Vol => HighscoreManager.Data.Mute ? 0f : HighscoreManager.Data.MusicVolume;

    public override void Load()
    {
        Audio.StopMusic(); // FlxG.sound.music.destroy()
        string id = VideoIds[s_nextVideo];
        s_nextVideo = (s_nextVideo + 1) % VideoIds.Length;
        Console.WriteLine($"[ATTRACT] Playing video {id}");
        _vid = VideoCutscenePlayer.Create(Game, Assets, id);
        if (_vid == null || !_vid.Play(Vol)) { Console.WriteLine("[ATTRACT] video failed"); _vid?.Dispose(); _vid = null; }
        BuildPieRing();
    }

    public override void Unload()
    {
        _vid?.Dispose(); _vid = null;
        _pieTex?.Dispose(); _pieTex = null;
    }

    public override void Update(GameTime gameTime)
    {
        if (_ended) return;
        float e = (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (_vid == null) { End(); return; }
        _vid.Update(Vol);
        if (_vid.Ended) { End(); return; }

        if (Input.AnyInputHeld)
        {
            _holdDelta = Math.Clamp(_holdDelta + e, 0f, HoldTime);
            _pieScale = MathHelper.Lerp(_pieScale, 1.3f, MathF.Exp(-e * 140f));
        }
        else
        {
            _holdDelta = MathHelper.Lerp(_holdDelta, -0.1f, Math.Clamp(e * 3f, 0f, 1f));
            _holdDelta = Math.Clamp(_holdDelta, 0f, HoldTime);
            _pieScale = MathHelper.Lerp(_pieScale, 1f, MathF.Exp(-e * 160f));
        }
        _pieAmount = Math.Min(1f, Math.Max(0f, (_holdDelta / HoldTime) * 1.025f));
        if (_pieAmount >= 1f) End();
    }

    /// <summary>onAttractEnd: stop the video, switch straight to the TitleState.</summary>
    private void End()
    {
        if (_ended) return;
        _ended = true;
        Console.WriteLine("[ATTRACT] end -> Title");
        Game.Scenes.SwitchSceneInstant(new TitleScene());
    }

    public override void Draw(SpriteBatch sb)
    {
        int W = FNFGame.SCREEN_WIDTH, H = FNFGame.SCREEN_HEIGHT;
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.AlphaBlend);
        sb.Draw(Assets.Pixel, new Rectangle(0, 0, W, H), Color.Black);
        _vid?.Draw(sb, W, H);
        sb.End();

        // FlxPieDial: hidden while amount * frames < 1; frame = int(amount * frames) (amount 1 -> frame 0 = full ring)
        float alpha = (_pieAmount - 0.025f) / (1f - 0.025f);
        if (_pieAmount * PieFrames < 1f || alpha <= 0f) return;
        int frame = _pieAmount >= 1f ? 0 : (int)(_pieAmount * PieFrames);
        UpdatePieTexture(frame);
        float size = PieRadius * 2;
        float x = W - size * 1.5f, y = H - size * 1.5f;
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
        sb.Draw(_pieTex, new Vector2(x + size / 2f, y + size / 2f), null, new Color(Color.White, Math.Min(1f, alpha)),
            0f, new Vector2(size / 2f, size / 2f), _pieScale, SpriteEffects.None, 0f);
        sb.End();
    }

    /// <summary>White ring (outer radius 40, inner 20, smoothed edge) like FlxPieDialUtils.drawShape CIRCLE.</summary>
    private void BuildPieRing()
    {
        int S = PieRadius * 2;
        _pieRing = new Color[S * S];
        for (int py = 0; py < S; py++)
            for (int px = 0; px < S; px++)
            {
                float dx = px + 0.5f - PieRadius, dy = py + 0.5f - PieRadius;
                float r = MathF.Sqrt(dx * dx + dy * dy);
                float a = Math.Clamp(PieRadius - r + 0.5f, 0f, 1f) * Math.Clamp(r - PieInner + 0.5f, 0f, 1f);
                _pieRing[py * S + px] = new Color(255, 255, 255, (int)(a * 255f + 0.5f));
            }
        _pieTex = new Texture2D(Game.GraphicsDevice, S, S);
    }

    /// <summary>Frame i shows the clockwise sweep of i * 360/45 degrees from 12 o'clock (frame 0 = full).</summary>
    private void UpdatePieTexture(int frame)
    {
        if (frame == _pieFrame || _pieTex == null) return;
        _pieFrame = frame;
        int S = PieRadius * 2;
        var data = new Color[S * S];
        float sweep = frame == 0 ? 360f : frame * 360f / PieFrames;
        for (int py = 0; py < S; py++)
            for (int px = 0; px < S; px++)
            {
                float dx = px + 0.5f - PieRadius, dy = py + 0.5f - PieRadius;
                float ang = MathF.Atan2(dx, -dy) * 180f / MathF.PI; // 0 at top, clockwise
                if (ang < 0f) ang += 360f;
                data[py * S + px] = ang <= sweep ? _pieRing[py * S + px] : Color.Transparent;
            }
        _pieTex.SetData(data);
    }
}
