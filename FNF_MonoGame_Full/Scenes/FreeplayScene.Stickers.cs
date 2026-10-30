namespace FNF_MonoGame.Scenes;

/// <summary>FreeplayState(params, stickers): when opened by the StickerSubState (Results without a new best,
/// Pause > Exit to Menu) the original sets forceSkipIntro = true: no black overlay slide, overhang already at
/// y=-100, capsules forcePosition() (no jump-in), onDJIntroDone() called right away (UI/album/dots shown).</summary>
public partial class FreeplayScene
{
    public bool ForceSkipIntro { get; set; }
    private bool _forceSkipApplied;

    private void ApplyForceSkipIntroOnce()
    {
        if (!ForceSkipIntro || _forceSkipApplied) return;
        _forceSkipApplied = true;
        _overhangY = -100f;
        _pinkSlideX = 0;
        _capsuleSlideX = 0;
        _phase = Phase.Idle;
        if (!_introDone) OnDJIntroDone();
    }
}
