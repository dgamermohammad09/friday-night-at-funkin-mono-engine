using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Gameplay;

/// <summary>
/// Dedicated character class for Nene characters (nene, nene-christmas, nene-tankmen, nene-speakers, nene-dark, nene-pixel).
/// </summary>
public class Nene : Character
{
    public Nene(string name, float x, float y) : base(name, x, y)
    {
    }

    public override Vector2 GetCharacterOrigin()
    {
        float num = GetIdleFrameWidth() * Scale;
        float y = GetIdleFrameHeight() * Scale;
        return new Vector2(num / 2f, y);
    }

    public override Vector2 GetMidpoint()
    {
        float num = GetIdleFrameWidth() * Scale / 2f;
        float num2 = GetIdleFrameHeight() * Scale / 2f;
        return new Vector2(Position.X + num, Position.Y + num2);
    }

    public override void ApplyStagePosition(float[] stagePos)
    {
        if (stagePos == null || stagePos.Length < 2 || _sprite?.Sheet == null)
            return;

        string firstAnim = JsonData?.Animations?.Count > 0 ? JsonData.Animations[0].Name : null;
        string animToUse = firstAnim ?? JsonData?.StartingAnimation;
        string resolved = animToUse != null ? ResolveAnimName(animToUse) : null;
        if (resolved != null && _currentAnimation != resolved)
            PlayAnimation(animToUse, force: true);
        else
        {
            string idleName = ResolveAnimName("idle");
            if (idleName != null && _currentAnimation != idleName)
                PlayAnimation(idleName, force: true);
        }

        _sprite.Sheet.AnchorCompositeTo(_sprite.ResolvedAnimName); // anchor to idle, as original updateHitbox()

        float idleFrameWidth = GetIdleFrameWidth();
        float idleFrameHeight = GetIdleFrameHeight();
        float originX = idleFrameWidth * Scale / 2f;
        float originY = idleFrameHeight * Scale;
        float offX = CharOffsets != null && CharOffsets.Length >= 2 ? CharOffsets[0] : 0f;
        float offY = CharOffsets != null && CharOffsets.Length >= 2 ? CharOffsets[1] : 0f;
        Position = new Vector2(stagePos[0] - originX + offX, stagePos[1] - originY + offY);
        if (_sprite != null)
            _sprite.PositioningFrameSize = (Width: (int)idleFrameWidth, Height: (int)idleFrameHeight);
    }

    private float GetIdleFrameWidth()
    {
        if (GetIdleTimelineSize() is { } ts) return ts.W; // flixel-animate timeline bounds
        if (_sprite?.Sheet == null) return 0f;
        if (_sprite.Sheet.CompositeLogicalSizes.Count > 0)
        {
            string firstAnim = JsonData?.Animations?.Count > 0 ? JsonData.Animations[0].Name : null;
            foreach (string name in new[] { firstAnim, JsonData?.StartingAnimation, _currentAnimation, "idle", "danceRight", "danceLeft" })
            {
                if (string.IsNullOrEmpty(name)) continue;
                string resolved = ResolveAnimName(name);
                if (resolved != null && _sprite.Sheet.CompositeLogicalSizes.TryGetValue(resolved, out var size))
                    return size.Width;
            }
            foreach (var kvp in _sprite.Sheet.CompositeLogicalSizes)
                return kvp.Value.Width;
        }
        var rawSize = GetIdleRawSize();
        if (rawSize.Width > 0) return rawSize.Width;
        return _sprite.GetFrameWidth();
    }

    private float GetIdleFrameHeight()
    {
        if (GetIdleTimelineSize() is { } ts) return ts.H; // flixel-animate timeline bounds
        if (_sprite?.Sheet == null) return 0f;
        if (_sprite.Sheet.CompositeLogicalSizes.Count > 0)
        {
            string firstAnim = JsonData?.Animations?.Count > 0 ? JsonData.Animations[0].Name : null;
            foreach (string name in new[] { firstAnim, JsonData?.StartingAnimation, _currentAnimation, "idle", "danceRight", "danceLeft" })
            {
                if (string.IsNullOrEmpty(name)) continue;
                string resolved = ResolveAnimName(name);
                if (resolved != null && _sprite.Sheet.CompositeLogicalSizes.TryGetValue(resolved, out var size))
                    return size.Height;
            }
            foreach (var kvp in _sprite.Sheet.CompositeLogicalSizes)
                return kvp.Value.Height;
        }
        var rawSize = GetIdleRawSize();
        if (rawSize.Height > 0) return rawSize.Height;
        return _sprite.GetFrameHeight();
    }

    private (int Width, int Height) GetIdleRawSize()
    {
        if (_sprite?.Sheet == null || _sprite.Sheet.RawCompositeData.Count == 0)
            return (0, 0);
        foreach (string name in new[] { "idle", "Idle", "nene all", "Nene Idle" })
        {
            string resolved = ResolveAnimName(name);
            if (resolved != null && _sprite.Sheet.RawCompositeData.TryGetValue(resolved, out var rawList) && rawList.Count > 0)
                return AnimatedSprite.ComputeRawBoundingBox(rawList[0]);
        }
        foreach (var kvp in _sprite.Sheet.RawCompositeData)
        {
            if (kvp.Value.Count > 0)
                return AnimatedSprite.ComputeRawBoundingBox(kvp.Value[0]);
        }
        return (0, 0);
    }

    // ===================================================================================================
    // Knife state machine: port of nene.hxc / nene-dark / nene-christmas / nene-pixel / nene-tankmen
    // (identical in all five). Nene raises her knife when the player's health is low.
    // ===================================================================================================
    private const float VULTURE_THRESHOLD = 0.25f * 2f; // health is 0..2
    private const int STATE_DEFAULT = 0, STATE_PRE_RAISE = 1, STATE_RAISE = 2, STATE_READY = 3, STATE_LOWER = 4;
    private const int MIN_BLINK_DELAY = 3, MAX_BLINK_DELAY = 7;
    private int _knifeState = STATE_DEFAULT;
    public int KnifeState => _knifeState;
    private int _blinkCountdown = MIN_BLINK_DELAY;
    private bool _animationFinished;
    private bool _wasFinished;
    private string _lastAnim;
    private static readonly System.Random _rng = new();

    /// <summary>shouldTransitionState(): disabled in Blazin (player pico-blazin).</summary>
    private static bool ShouldTransitionState()
    {
        var bf = FNF_MonoGame.Scenes.PlayScene.Current?.Boyfriend;
        string id = bf?.CurCharacter ?? bf?.Name;
        return !string.Equals(id, "pico-blazin", System.StringComparison.OrdinalIgnoreCase);
    }

    private static float Health => FNF_MonoGame.Scenes.PlayScene.Current?.PlayerHealth ?? 1f;

    /// <summary>nene.hxc dance(): state-dependent; STATE_DEFAULT is the normal danceLeft/danceRight.</summary>
    protected override bool DanceOverride()
    {
        switch (_knifeState)
        {
            case STATE_DEFAULT:
                return false; // base: alternate danceLeft / danceRight
            case STATE_PRE_RAISE:
                PlayAnimation("danceLeft", false);
                _danceLeft = false; // hasDanced = false
                return true;
            case STATE_READY:
                if (_blinkCountdown == 0)
                {
                    PlayAnimation("idleKnife", false);
                    _blinkCountdown = _rng.Next(MIN_BLINK_DELAY, MAX_BLINK_DELAY + 1); // FlxG.random.int is inclusive
                }
                else
                {
                    _blinkCountdown--;
                }
                return true;
            case STATE_LOWER:
                if (CurrentAnimation != "lowerKnife")
                    PlayAnimation("lowerKnife", false);
                return true;
            default:
                return true; // don't interrupt the current animation
        }
    }

    public override void Update(float deltaTime)
    {
        base.Update(deltaTime);
        if (_sprite == null) return;

        // onAnimationFinished / onAnimationFrame
        string anim = CurrentAnimation;
        bool finished = _sprite.Finished;
        if (anim != _lastAnim) { _wasFinished = false; _lastAnim = anim; }
        if (finished && !_wasFinished
            && (_knifeState == STATE_RAISE || _knifeState == STATE_LOWER))
        {
            _animationFinished = true;
            TransitionState();
        }
        _wasFinished = finished;

        if (_knifeState == STATE_PRE_RAISE && anim == "danceLeft" && _sprite.FrameIndex >= 13)
        {
            _animationFinished = true;
            TransitionState();
            // flixel FlxAnimation.set_curFrame: the frame callback plays 'raiseKnife', which stop()s danceLeft
            // (finished = true); set_curFrame then fires fireFinishCallback('danceLeft') in the same update, and
            // nene.hxc onAnimationFinished (state RAISE) moves straight on to STATE_READY
            // (original trace: state=3 already on raiseKnife frame 0).
            if (_knifeState == STATE_RAISE)
            {
                _animationFinished = true;
                TransitionState();
            }
        }

        // onUpdate(): transitionState() every frame
        if (ShouldTransitionState()) TransitionState();
    }

    private void TransitionState()
    {
        switch (_knifeState)
        {
            case STATE_DEFAULT:
                _knifeState = Health <= VULTURE_THRESHOLD ? STATE_PRE_RAISE : STATE_DEFAULT;
                break;
            case STATE_PRE_RAISE:
                if (Health > VULTURE_THRESHOLD)
                    _knifeState = STATE_DEFAULT;
                else if (_animationFinished)
                {
                    _knifeState = STATE_RAISE;
                    PlayAnimation("raiseKnife", true);
                    _animationFinished = false;
                }
                break;
            case STATE_RAISE:
                if (_animationFinished)
                {
                    _knifeState = STATE_READY;
                    _animationFinished = false;
                }
                break;
            case STATE_READY:
                if (Health > VULTURE_THRESHOLD)
                    _knifeState = STATE_LOWER; // lowerKnife plays on the next beat
                break;
            case STATE_LOWER:
                if (_animationFinished)
                {
                    _knifeState = STATE_DEFAULT;
                    _animationFinished = false;
                }
                break;
            default:
                _knifeState = STATE_DEFAULT;
                break;
        }
    }
}
