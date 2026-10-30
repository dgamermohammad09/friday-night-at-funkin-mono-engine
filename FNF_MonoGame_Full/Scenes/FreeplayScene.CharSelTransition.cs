using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// FreeplayState.tryOpenCharSelect / transitionToCharSelect (TAB -> Character Select):
///  - dj.toCharSelect() right away; after the player's charSelect.transitionDelay (bf/pico.json: 0.45 s)
///    transitionToCharSelect(): every exitMoversCharSel member tweens y by its delta over `speed` with FlxEase.backIn
///    (traced on the original: pinkBack -100, dj -175 move; the Pico/BF card layers do NOT move - PicoCard/BoyfriendCard
///    applyExitMovers return early because FreeplayState passes exitMoversCharSel = null at create),
///    funnyCam BlueFade 1 -> 0 over 0.8 s quadIn, music fadeOut(0.9), backingCard.enterCharSel() (Pico backdrop
///    velocities / BF BGScrollingText speeds -> 0 over 0.8 s sineIn), transitionGradient (rankCamera) y 720 -> 0,
///    FlxG.switchState(CharSelect) 0.9 s later.
/// BlueFade: mix((0,0,b,a)*f, tex*f, f) = (r f^2, g f^2, b f, a f) -> one multiply quad over the funnyCam image.
/// </summary>
public partial class FreeplayScene
{
    private float _csT = -1f;                 // seconds since TAB (tryOpenCharSelect); -1 = not opening
    private const float CsTransitionDelay = 0.45f;
    private Texture2D _csGradient;
    private float _csMusicVol0 = -1f;

    private float CsTrans => _csT < 0f ? -1f : _csT - CsTransitionDelay;   // seconds since transitionToCharSelect

    private static float BackIn(float t) => t * t * (2.70158f * t - 1.70158f);
    private static float SineIn(float t) => 1f - MathF.Cos(t * MathF.PI / 2f);
    private static float QuadIn(float t) => t * t;

    /// <summary>exitMoversCharSel y offset (FlxTween y: +dy, speed, backIn) of a member at this moment.</summary>
    private float CsDy(float dy, float speed = 0.8f)
    {
        float t = CsTrans;
        return t <= 0f ? 0f : dy * BackIn(Math.Min(1f, t / speed));
    }

    /// <summary>enterCharSel: velocity/speed tweened to 0 over 0.8 s sineIn.</summary>
    private float CsVelMul => CsTrans <= 0f ? 1f : 1f - SineIn(Math.Min(1f, CsTrans / 0.8f));

    private void CsStart()
    {
        _csT = 0f;
        _csMusicVol0 = -1f;
    }

    /// <summary>Returns true once the scene was switched.</summary>
    private bool CsUpdate(float dt)
    {
        if (_csT < 0f) return false;
        _csT += dt;
        float t = CsTrans;
        if (t >= 0f)
        {
            if (_csMusicVol0 < 0f) _csMusicVol0 = Audio.MusicVolume;
            Audio.MusicVolume = _csMusicVol0 * Math.Max(0f, 1f - t / 0.9f);   // FlxG.sound.music.fadeOut(0.9, 0)
            if (t >= 0.9f)
            {
                _csT = -1f;
                Audio.StopMusic();
                Audio.MusicVolume = HighscoreManager.Data.MusicVolume;
                Game.Scenes.ChangeScene(new CharacterSelectScene());
                return true;
            }
        }
        return false;
    }

    /// <summary>BlueFade on funnyCam + transitionGradient on rankCamera (drawn last inside the funnyCam batch area).</summary>
    private void CsDrawFade(SpriteBatch sb)
    {
        float t = CsTrans;
        if (t <= 0f) return;
        float f = 1f - QuadIn(Math.Min(1f, t / 0.8f));
        sb.End();
        sb.Begin(blendState: CsMultiply, samplerState: SamplerState.PointClamp);
        sb.Draw(Assets.Pixel, new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT), new Color(f * f, f * f, f, 1f));
        sb.End();
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
        _csGradient ??= Assets.LoadTexture("menus/freeplay/transitionGradient.png");
        if (_csGradient != null && _csGradient != Assets.Pixel)
        {
            float y = 720f + CsDy(-720f);   // new FlxSprite(0, 720), scale (1280, 1) + updateHitbox
            sb.Draw(_csGradient, new Vector2(0f, y), null, Color.White, 0f, Vector2.Zero,
                new Vector2(1280f / _csGradient.Width, 1f), SpriteEffects.None, 0f);
        }
    }

    private static readonly BlendState CsMultiply = new BlendState
    {
        ColorSourceBlend = Blend.Zero, ColorDestinationBlend = Blend.SourceColor, ColorBlendFunction = BlendFunction.Add,
        AlphaSourceBlend = Blend.Zero, AlphaDestinationBlend = Blend.One, AlphaBlendFunction = BlendFunction.Add
    };

    private float _csShiftCur;
    /// <summary>Restarts the UI batch translated by the exitMoversCharSel y offset of the group drawn next
    /// (overhang/topLeftCornerText/ostName/charSelectHint -300, grpDifficulties/diffSel/letterSort/score/clearBox -270).</summary>
    private void CsShift(SpriteBatch sb, float dy)
    {
        if (_csT < 0f && _csShiftCur == 0f) return;
        float y = _csT < 0f ? 0f : CsDy(dy);
        _csShiftCur = y;
        sb.End();
        sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied,
            transformMatrix: y == 0f ? (Matrix?)null : Matrix.CreateTranslation(0f, y, 0f));
    }

    /// <summary>DifficultyDot.fadeOut(): alpha -> 0 over 0.25 s quartOut from transitionToCharSelect.</summary>
    private Color CsDotCol(Color c)
    {
        float t = CsTrans;
        if (t <= 0f) return c;
        float k = Math.Min(1f, t / 0.25f);
        float a = 1f - (1f - MathF.Pow(1f - k, 4f));
        return new Color(c.R, c.G, c.B, (byte)(c.A * a));
    }

    private float CsFadeF => CsTrans <= 0f ? 1f : 1f - QuadIn(Math.Min(1f, CsTrans / 0.8f));

    public string DevCsInfo => _csT < 0f ? "" : $"cs={_csT:F3} tr={CsTrans:F3} velMul={CsVelMul:F3} pinkDy={CsDy(-100f):F2}";
}
