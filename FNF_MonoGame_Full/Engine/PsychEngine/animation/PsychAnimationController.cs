namespace FNF_MonoGame.Engine.PsychEngine.animation;

/// <summary>
/// Animation controller that respects the global animation time scale.
/// Adapts backend/animation/PsychAnimationController.hx for MonoGame.
/// </summary>
public class PsychAnimationController
{
    public bool FollowGlobalSpeed { get; set; } = true;
    public float TimeScale { get; set; } = 1f;

    public void Update(float elapsed)
    {
        float speed = TimeScale;
        if (FollowGlobalSpeed)
            speed *= PsychGlobals.AnimationTimeScale;

        // Apply scaled elapsed time to any tracked animation.
        // Concrete sprite animation is driven by the owning AnimatedSprite;
        // this controller just supplies the effective delta.
        PsychGlobals.LastAnimationDelta = elapsed * speed;
    }
}
