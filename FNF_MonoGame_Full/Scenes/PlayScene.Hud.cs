using System;
using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FontStashSharp;

namespace FNF_MonoGame.Scenes;

// V-Slice gameplay HUD (PlayState.initHealthBar / updateScoreText / updateHealthBar, HealthIcon.updatePosition/onStepHit).
//  healthBarBG 'healthBar' at y = FlxG.height * (downscroll ? 0.1 : 0.9), screenCenter(X) (float x = 339.5)
//  healthBar = FlxBar(bg.x+4, bg.y+4, RIGHT_TO_LEFT, bg.w-8, bg.h-8, 0..2), createFilledBar(0xFFFF0000, 0xFF66FF33)
//  scoreText: FlxText(bg.x + bg.w - 190, bg.y + 30), vcr.ttf 16, white, OUTLINE black (1 px); 'Score: 1,234' or
//  'Bot Play Enabled'. Bot play: healthLerp = HEALTH_MAX; otherwise healthLerp = lerp(healthLerp, health, 0.15) per frame.
//  Icons: 150 px, y = bar.y - h/2; P1 x = bar.x + bar.w*(1 - v/2) - 26; P2 x = same - (w - 26).
//  Bop every 4 steps: width += 150*0.2, linear tween back to 150 over min(stepMs*0.002, 0.175) s.
public partial class PlayScene
{
    private bool UseVSliceHud => true;
    private string _hudDbg = "";
    private static readonly Color HudBarRed = new Color(0xFF, 0x00, 0x00);
    private static readonly Color HudBarGreen = new Color(0x66, 0xFF, 0x33);
    private float _hudIconW = 150f, _hudIconBopFrom = 150f, _hudIconBopT = -1f, _hudIconBopDur = 0.175f;

    /// <summary>Optional override for the score line (local multiplayer shows both players).</summary>
    protected Func<string> HudScoreTextOverride;

    private void HudIconBop()
    {
        _hudIconBopFrom = _hudIconW + 150f * 0.2f;
        _hudIconW = _hudIconBopFrom;
        _hudIconBopDur = Math.Min((float)_conductor.StepCrochet * 2f, 0.175f);
        _hudIconBopT = 0f;
    }

    private void HudUpdate(float dt)
    {
        if (_botplay && !_mpActive) _healthLerp = HEALTH_MAX;
        else _healthLerp = MathHelper.Lerp(_healthLerp, _health, 1f - MathF.Pow(1f - 0.15f, dt * 60f)); // 0.15 per 60 fps frame
        if (_hudIconBopT >= 0f)
        {
            _hudIconBopT += dt;
            float k = _hudIconBopDur <= 0 ? 1f : Math.Min(1f, _hudIconBopT / _hudIconBopDur);
            _hudIconW = _hudIconBopFrom + (150f - _hudIconBopFrom) * k;
            if (k >= 1f) { _hudIconBopT = -1f; _hudIconW = 150f; }
        }
    }

    // vcr.ttf: unitsPerEm 2048, ascent 1800, descent 0, advance 1200 (monospace). OpenFL sizes text by em and uses whole-pixel
    // advances (16 px -> 9 px/char); FontStash sizes by ascent-descent, so request size*2048/1800 and place every glyph ourselves.
    public static float FlxTextYOffset = -2f; // measured vs the original: FontStash glyph top is 2 px lower (B of "Bot Play Enabled")
    private void DrawFlxOutlinedText(SpriteBatch sb, int size, string text, Vector2 pos, Color color, Color border)
    {
        var font = Assets.GetFont(int.TryParse(Environment.GetEnvironmentVariable("FNF_HUDFONT"), out var __fs) ? __fs : size);
        if (font == null) return;
        float adv = MathF.Round(size * 1200f / 2048f);
        pos += new Vector2(2, 2 + (float.TryParse(Environment.GetEnvironmentVariable("FNF_HUDFONTY"), out var __fy) ? __fy : FlxTextYOffset));   // FlxText 2 px gutter
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == ' ') continue;
                string ch = text[i].ToString();
                var p = new Vector2(MathF.Round(pos.X + i * adv), MathF.Round(pos.Y));
                if (pass == 0)
                {   // FlxText OUTLINE (borderSize 1, quality 1): 8 copies offset by 1 px
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                            if (dx != 0 || dy != 0) font.DrawText(sb, ch, p + new Vector2(dx, dy), border);
                }
                else font.DrawText(sb, ch, p, color);
            }
    }

    private void DrawHudVSlice(SpriteBatch spriteBatch)
    {
        var bgTex = (_healthBarBG != null && _healthBarBG != Assets.Pixel) ? _healthBarBG : null;
        float bgW = bgTex?.Width ?? 601, bgH = bgTex?.Height ?? 19;
        float bgX = (FNFGame.SCREEN_WIDTH - bgW) / 2f;
        float bgY = _downscroll ? FNFGame.SCREEN_HEIGHT * 0.1f : FNFGame.SCREEN_HEIGHT * 0.9f;
        if (bgTex != null) spriteBatch.Draw(bgTex, new Vector2(RoundCompat.AwayFromZero(bgX), MathF.Round(bgY)), Color.White * HudBarAlpha);
        else spriteBatch.Draw(Assets.Pixel, new Rectangle((int)bgX, (int)bgY, (int)bgW, (int)bgH), Color.Black * HudBarAlpha);

        float barX = bgX + 4, barY = bgY + 4;
        int barW = (int)(bgW - 8), barH = (int)(bgH - 8);
        float value = Math.Clamp(_healthLerp, 0f, HEALTH_MAX);
        int filled = (int)(barW * (value / HEALTH_MAX));           // RIGHT_TO_LEFT: filled part on the right
        int bx = (int)RoundCompat.AwayFromZero(barX), by = (int)RoundCompat.AwayFromZero(barY);
        spriteBatch.Draw(Assets.Pixel, new Rectangle(bx, by, barW - filled, barH), HudBarRed * HudBarAlpha);
        if (filled > 0) spriteBatch.Draw(Assets.Pixel, new Rectangle(bx + barW - filled, by, filled, barH), HudBarGreen * HudBarAlpha);

        // Icons
        _hudDbg = "";
        float pct = 1f - value / HEALTH_MAX;
        int iw = (int)_hudIconW;
        if (!_ssfHideIconP2) DrawHudIcon(spriteBatch, _opponentIcon, barX + barW * pct - (iw - 26) - _iconP2Offset.X, barY - _iconP2Offset.Y, iw, HEALTH_MAX - _health, false); // HealthIcon.iconOffset
        DrawHudIcon(spriteBatch, _playerIcon, barX + barW * pct - 26 - _iconP1Offset.X, barY - _iconP1Offset.Y, iw, _health, true);

        if (_mpActive) { MpDrawHud(spriteBatch, bgX, bgY, bgW); return; }
        string text = HudScoreTextOverride?.Invoke()
            ?? (_botplay ? "Bot Play Enabled" : "Score: " + _score.ToString("#,##0", CultureInfo.InvariantCulture));
        DrawFlxOutlinedText(spriteBatch, 16, text, new Vector2(bgX + bgW - 190, bgY + 30), Color.White, Color.Black);
    }

    private Matrix _hudBatchMatrix = Matrix.Identity;

    private void DrawHudIcon(SpriteBatch sb, Texture2D icon, float x, float barY, int size, float health, bool flip)
    {
        if (icon == null || icon == Assets.Pixel) return;
        int frameW = icon.Height;
        int numFrames = icon.Width / Math.Max(1, frameW);
        int frame = 0;
        if (numFrames >= 2 && health < 0.4f) frame = 1;          // LOSING_THRESHOLD 0.2*2
        else if (numFrames >= 3 && health > 1.6f) frame = 2;     // WINNING_THRESHOLD 0.8*2
        var src = new Rectangle(frame * frameW, 0, frameW, frameW);
        float y = barY - size / 2f;
        _hudDbg += $"{(flip ? "p1" : "p2")}=({x:F1},{y:F1},{size}) ";
        // HealthIcon.loadCharacter: legacy pixel icons (32 px frames, isPixel) use antialiasing = false -> nearest sampling.
        bool pixel = frameW <= 32;
        if (pixel) { sb.End(); sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied, transformMatrix: _hudBatchMatrix); }
        sb.Draw(icon, new Rectangle((int)MathF.Round(x), (int)MathF.Round(y), size, size), src, Color.White * HudBarAlpha, 0f, Vector2.Zero,
            flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
        if (pixel) { sb.End(); sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied, transformMatrix: _hudBatchMatrix); }
    }
}
