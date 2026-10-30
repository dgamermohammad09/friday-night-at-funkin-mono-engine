using Microsoft.Xna.Framework;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Gameplay;

public partial class Character
{
    /// <summary>FlxSprite.visible (sserafim.hxc setGirlsVisible). Checked by PlayScene.DrawStageCharacter.</summary>
    public bool Visible = true;
    /// <summary>Suffix appended to every playAnimation name (sserafim-gf.hxc '-beautiful').</summary>
    public string AnimSuffix;

    /// <summary>
    /// Called after every animation change.
    ///
    /// Original (FNF V-Slice BaseCharacter.hx / Stage.hx / Bopper.hx):
    ///   x/y are computed once per placement from the IDLE frame
    ///   (addCharacter -> resetCharacter -> dance(true) -> updateHitbox -> setScale).
    ///   Bopper.playAnimation() only swaps animOffsets; it never calls updateHitbox()
    ///   and never moves x/y. Per-animation alignment comes solely from
    ///   getScreenPosition(): -(animOffsets - globalOffsets) * scale.
    ///
    /// The previous implementation re-anchored the feet here using the CURRENT
    /// animation's frame size, shifting every pose whose frame size differs from
    /// idle and overwriting the idle-anchored positions of Girlfriend/Nene/Otis/
    /// PicoSpeaker. Placement now happens only in ApplyStagePosition().
    /// </summary>
    protected virtual void UpdateHitbox()
    {
    }
}
