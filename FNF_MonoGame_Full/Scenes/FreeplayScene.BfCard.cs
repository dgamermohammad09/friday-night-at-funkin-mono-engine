using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FontStashSharp;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// backcard-bf.hxc (BoyfriendCard) idle layers, ported literally:
///  - six BGScrollingText rows (BGScrollingText.hx): update() runs from the moment the card is created (rows are only
///    hidden until introDone), position step = speed * (elapsed / (1/60)), wrap + in-place sort inside the loop.
///    FlxText metrics: font 5by7 at em size 43 / 60 (FontStash size = em * (ascent-descent)/unitsPerEm = em * 0.7),
///    frameWidth = text width + 4 (2 px gutter each side), glyphs drawn at (x + 2, y + 2).
///  - glowDark (beatglow, MULTIPLY) + glow (beatglow, ADD) at (CUTOUT_WIDTH * DJ_POS_MULTI - 300, 330), alpha 1:
///    BoyfriendCard.beatHit() is never called in 0.8.1 (BackingCard.onBeatHit is empty) - traced on the original
///    (glow.alpha = glowDark.alpha = 1 at every shot).
/// Add order: ... backingTextYeah, moreWays, funnyScroll, txtNuts, funnyScroll2, moreWays2, funnyScroll3, glowDark, glow, cardGlow.
/// </summary>
public partial class FreeplayScene
{
    private const float FiveBySevenEmToFontStash = 0.7f; // 5by7.ttf: hhea ascent 700, descent 0, unitsPerEm 1000
    private Texture2D _bfBeatGlow, _bfBeatGlowPM;

    private SpriteFontBase BfCardFont(int emSize, bool bold) => Assets.GetNamedFontF(bold ? "5by7-bold" : "5by7-regular", emSize * FiveBySevenEmToFontStash)
        ?? Assets.GetNamedFont("5by7", (int)MathF.Round(emSize * FiveBySevenEmToFontStash));

    /// <summary>BGScrollingText.update for every row (called every frame from FreeplayScene.Update, visible or not).</summary>
    private void BfCardUpdate(float dt)
    {
        if (_scrollingTextRows == null || _isPicoTheme) return;
        const float placementOffset = 20f;
        for (int i = 0; i < _scrollingTextRows.Length; i++)
        {
            ref var row = ref _scrollingTextRows[i];
            float frameWidth = row.TextWidth;           // FlxText.frameWidth (gutter included)
            var p = row.Positions;
            for (int j = 0; j < p.Length; j++)          // for (txtPosition in _textPositions): index iterator over the live array
            {
                p[j] -= 1f * (row.Speed * CsVelMul * (dt / (1f / 60f)));   // enterCharSel: speed -> 0 over 0.8 s sineIn
                if (row.Speed > 0f)
                {
                    if (p[j] < -frameWidth)
                    {
                        p[j] = p[p.Length - 1] + frameWidth + placementOffset;
                        System.Array.Sort(p);
                    }
                }
                else
                {
                    if (p[j] > frameWidth * 2f)
                    {
                        p[j] = p[0] - frameWidth - placementOffset;
                        System.Array.Sort(p);
                    }
                }
            }
        }
    }

    private void DrawBfCardTexts(SpriteBatch sb)
    {
        if (_scrollingTextRows == null || !_scrollingTextVisible || _isPicoTheme) return;
        sb.End();   // FontStash glyph atlases are premultiplied: draw them with AlphaBlend (the card batch is NonPremultiplied)
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.AlphaBlend);
        for (int i = 0; i < _scrollingTextRows.Length; i++)
        {
            ref var row = ref _scrollingTextRows[i];
            var font = BfCardFont(row.FontSize, row.Bold);
            if (font == null) continue;
            foreach (float px in row.Positions)
            {
                float x = px + 2f + BfTextDX, y = row.Y + 2f + BfTextDY;   // FlxText gutter
                if (x >= FNFGame.SCREEN_WIDTH || x + row.TextWidth < 0) continue;
                font.DrawText(sb, row.Text, new Vector2(MathF.Round(x), MathF.Round(y)), row.Color);
            }
        }
        sb.End();
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
    }

    // Rasterizer placement difference between OpenFL TextField and FontStash (measured against the original).
    private static readonly float BfTextDX = EnvF("FNF_BFTXT_DX", 0f), BfTextDY = EnvF("FNF_BFTXT_DY", 0f);
    private static float EnvF(string n, float d) => float.TryParse(Environment.GetEnvironmentVariable(n), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : d;

    private void DrawBfCardGlow(SpriteBatch sb)
    {
        if (_isPicoTheme || !_scrollingTextVisible) return;   // glow/glowDark share the rows' visibility (introDone / confirm / disappear)
        _bfBeatGlow ??= Assets.LoadTexture("menus/freeplay/beatglow.png");
        if (_bfBeatGlow == null || _bfBeatGlow == Assets.Pixel) return;
        _bfBeatGlowPM ??= PremultipliedCopy(_bfBeatGlow);
        var pos = new Vector2(CutoutWidth * DJ_POS_MULTI - 300f, 330f);
        sb.End();
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: OpenFlMultiply);
        sb.Draw(_bfBeatGlowPM, pos, Color.White);   // glowDark, MULTIPLY, alpha 1
        sb.End();
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.Additive);
        sb.Draw(_bfBeatGlow, pos, Color.White);     // glow, ADD, alpha 1
        sb.End();
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
    }

    public string DevBfCardInfo => _scrollingTextRows == null || _isPicoTheme ? "" :
        string.Join(" ", System.Array.ConvertAll(_scrollingTextRows, r => $"{r.Text.Substring(0, Math.Min(4, r.Text.Length))}:p0={r.Positions[0]:F2},fw={r.TextWidth}"));
}
