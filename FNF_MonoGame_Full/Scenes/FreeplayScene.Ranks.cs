using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// SongMenuItem.FreeplayRank (ranking + blurredRanking):
///  - sparrow 'freeplay/rankbadges', non-looping 24 fps rank animations, (re)played by set_rank (initData on create,
///    difficulty change, filter change) and ending on the LAST frame (frame 0 is the tiny pop-in frame);
///  - scale 0.9 + updateHitbox at (420, 41): top-left of the scaled frame sits at (420, 41) (the GOOD/GREAT
///    offset.y -= 8 is overwritten by updateHitbox, so it has no effect);
///  - blend ADD, alpha 1 / 0.7 and colour FFFFFF / AAAAAA (selected / not);
///  - blurredRanking: same frame through GaussianBlurShader(1) (blur13 vertical/horizontal mixed 50/50, 2-texel
///    step on the whole atlas), shader ignores alpha/colour, blend ADD.
/// </summary>
public partial class FreeplayScene
{
    private float _rankBadgeAnimTime;   // seconds since the last set_rank (re)play
    private Texture2D _rankBadgesBlurPM; // premultiplied blurred atlas

    private void ReplayRankBadges() => _rankBadgeAnimTime = 0f;

    private static readonly BlendState AddPremultiplied = new BlendState
    {
        ColorSourceBlend = Blend.One, ColorDestinationBlend = Blend.One,
        AlphaSourceBlend = Blend.One, AlphaDestinationBlend = Blend.One
    };

    private Texture2D EnsureRankBlur()
    {
        if (_rankBadgesBlurPM != null || _rankBadgesSheet?.Texture == null) return _rankBadgesBlurPM;
        var src = _rankBadgesSheet.Texture;
        int w = src.Width, h = src.Height;
        var data = new Color[w * h];
        src.GetData(data);
        // premultiply (OpenFL textures are premultiplied)
        var pm = new Vector4[w * h];
        for (int i = 0; i < data.Length; i++)
        {
            float a = data[i].A / 255f;
            pm[i] = new Vector4(data[i].R / 255f * a, data[i].G / 255f * a, data[i].B / 255f * a, a);
        }
        Vector4 Sample(float x, float y) // bilinear, clamp (texel centres at +0.5)
        {
            x -= 0.5f; y -= 0.5f;
            int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
            float fx = x - x0, fy = y - y0;
            Vector4 P(int xx, int yy) => pm[Math.Clamp(yy, 0, h - 1) * w + Math.Clamp(xx, 0, w - 1)];
            return Vector4.Lerp(Vector4.Lerp(P(x0, y0), P(x0 + 1, y0), fx), Vector4.Lerp(P(x0, y0 + 1), P(x0 + 1, y0 + 1), fx), fy);
        }
        float[] off = { 0f, 1.411764705882353f, 3.2941176470588234f, 5.176470588235294f };
        float[] wt = { 0.1964825501511404f, 0.2969069646728344f, 0.09447039785044732f, 0.010381362401148057f };
        var outp = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float cx = x + 0.5f, cy = y + 0.5f;
                Vector4 v = Sample(cx, cy) * wt[0], hz = Sample(cx, cy) * wt[0];
                for (int k = 1; k < 4; k++)
                {
                    float d = off[k] * 2f;   // direction = _amount * 2
                    v += (Sample(cx, cy + d) + Sample(cx, cy - d)) * wt[k];
                    hz += (Sample(cx + d, cy) + Sample(cx - d, cy)) * wt[k];
                }
                var r = Vector4.Lerp(hz, v, 0.5f);
                outp[y * w + x] = new Color(Math.Clamp(r.X, 0, 1), Math.Clamp(r.Y, 0, 1), Math.Clamp(r.Z, 0, 1), Math.Clamp(r.W, 0, 1));
            }
        _rankBadgesBlurPM = new Texture2D(src.GraphicsDevice, w, h);
        _rankBadgesBlurPM.SetData(outp);
        return _rankBadgesBlurPM;
    }

    private static string RankBadgePrefix(string rank) => (rank ?? "").ToUpperInvariant() switch
    {
        "PERFECT_GOLD" or "PERFECT GOLD" or "PERFECTSICK" => "PERFECT rank GOLD",
        "PERFECT" => "PERFECT rank",
        "EXCELLENT" => "EXCELLENT rank",
        "GREAT" => "GREAT rank",
        "GOOD" => "GOOD rank",
        "SHIT" or "LOSS" => "LOSS rank",
        _ => null
    };

    /// <summary>Draws ranking + blurredRanking. Must be called inside an additive (premultiplied ONE,ONE-safe) batch section;
    /// switches batches itself and restores <paramref name="restore"/>.</summary>
    private void DrawRankBadge(SpriteBatch sb, string rank, Vector2 topLeft, float sprScale, float zoom, bool selected,
        float animTime, float? alphaOverride, BlendState restore, SamplerState restoreSampler)
    {
        string prefix = RankBadgePrefix(rank);
        if (prefix == null || _rankBadgesSheet?.Texture == null) return;
        var frames = _rankBadgesSheet.GetAnimation(prefix);
        if (frames == null || frames.Count == 0) return;
        var frame = frames[Math.Clamp((int)(animTime * 24f), 0, frames.Count - 1)];

        float a = alphaOverride ?? (selected ? 1f : 0.7f);
        Color col = selected ? Color.White : new Color(0xAA, 0xAA, 0xAA);
        // scale about the centre origin after updateHitbox: top-left of the scaled frame stays at (x, y) for 0.9,
        // for other scales the centre stays where the 0.9 hitbox centre is.
        float fw = frame.FrameWidth > 0 ? frame.FrameWidth : frame.SourceRect.Width;
        float fh = frame.FrameHeight > 0 ? frame.FrameHeight : frame.SourceRect.Height;
        float scale = sprScale * zoom;
        Vector2 centre = topLeft + new Vector2(fw, fh) * 0.45f * zoom;
        Vector2 drawPos = centre - new Vector2(fw, fh) * 0.5f * scale + frame.Offset * scale;

        sb.End();
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.Additive);
        sb.Draw(_rankBadgesSheet.Texture, drawPos, frame.SourceRect, new Color(col, a), 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        sb.End();
        var blur = EnsureRankBlur();
        if (blur != null)
        {
            sb.Begin(samplerState: SamplerState.LinearClamp, blendState: AddPremultiplied);
            sb.Draw(blur, drawPos, frame.SourceRect, Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            sb.End();
        }
        sb.Begin(samplerState: restoreSampler, blendState: restore);
    }

    private void DisposeRankExtras()
    {
        _rankBadgesBlurPM?.Dispose();
        _rankBadgesBlurPM = null;
    }
}
