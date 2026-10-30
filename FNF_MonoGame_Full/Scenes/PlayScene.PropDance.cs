namespace FNF_MonoGame.Scenes;

using System;
using System.Linq;

// Stage prop beat dance, moved out of the Conductor.OnBeat lambda so the countdown beats can use it too:
// V-Slice runs the Conductor during the countdown, so every Bopper prop (Stage props, A-Bot via nene.hxc dance())
// gets beatHit on beats -4..-1. Before, props only started dancing at beat 0 (A-Bot showed its frame-0
// 'non-bumpin' pose = black box + raised screen during the whole countdown).
public partial class PlayScene
{
    private void DanceAnimatedProps(int beat)
    {
        // Animated stage props dance on beat (P1)
        for (int pi = 0; pi < _animatedProps.Count; pi++)
        {
            var prop = _animatedProps[pi];
            if (prop.DanceEvery > 0 && beat % prop.DanceEvery == 0)
            {
                if (string.Equals(prop.Name, "neneSpeakerRig", StringComparison.OrdinalIgnoreCase))
                {
                    if (_abotUseContinuousAnim)
                    {
                        _animatedProps[pi] = prop;
                        continue;
                    }
                    string rigAnim = prop.DanceAnimNames?.Length > 0
                        ? prop.DanceAnimNames[0]
                        : prop.Sprite?.CurrentAnimation;
                    if (!string.IsNullOrEmpty(rigAnim))
                        if (!_abotIsPixel) prop.Sprite?.PlayAnimationFromFrame(rigAnim, _abotRigStartFrame, loop: false, loopFrame: _abotRigStartFrame); // pixel body: synced with Nene in W6AbotPixelUpdate (nene-pixel.hxc dance())
                }
                else
                if (prop.DanceAnimNames != null && prop.DanceAnimNames.Length >= 2)
                {
                    // Alternate between dance animations (danceLeft/danceRight)
                    int idx = prop.DanceToggle ? 1 : 0;
                    string danceAnim = prop.DanceAnimNames[idx];
                    bool danceLooped = prop.AnimLoops != null && prop.AnimLoops.TryGetValue(danceAnim, out bool dl1) ? dl1 : false;
                    if (prop.CanPlayOtherAnims)
                    {
                        prop.Sprite?.PlayAnimation(danceAnim, force: true, loop: danceLooped);
                    }
                    prop.DanceToggle = !prop.DanceToggle;
                }
                else if (prop.DanceAnimNames != null && prop.DanceAnimNames.Length == 1)
                {
                    // Props with a single mapped animation (like guy's "rando"): restart that one on the beat
                    string danceAnim = prop.DanceAnimNames[0];
                    bool danceLooped = prop.AnimLoops != null && prop.AnimLoops.TryGetValue(danceAnim, out bool dl2) ? dl2 : false;
                    if (prop.CanPlayOtherAnims)
                    {
                        if (string.Equals(prop.Name, "guy", StringComparison.OrdinalIgnoreCase) || string.Equals(prop.Name, "rando", StringComparison.OrdinalIgnoreCase))
                            Console.WriteLine($"[STREET-DEBUG] Prop '{prop.Name}' dancing beat={beat} anim={danceAnim} looped={danceLooped}");
                        prop.Sprite?.PlayAnimation(danceAnim, force: true, loop: danceLooped);
                    }
                }
                else if (prop.Sprite?.Sheet != null)
                {
                    // Fallback: restart the currently playing animation, or the first available one
                    string fallbackAnim = prop.Sprite.CurrentAnimation ?? prop.Sprite.Sheet.Animations.Keys.FirstOrDefault();
                    if (fallbackAnim != null && prop.CanPlayOtherAnims)
                    {
                        bool fbLooped = prop.AnimLoops != null && prop.AnimLoops.TryGetValue(fallbackAnim, out bool fbl) ? fbl : false;
                        prop.Sprite.PlayAnimation(fallbackAnim, force: true, loop: fbLooped);
                    }
                }
                prop.LastDanceBeat = beat;
                _animatedProps[pi] = prop;
            }
        }
    }
    /// <summary>The countdown branch of Update returned before the stage props were updated, so every prop
    /// animation was frozen and the A-Bot rig kept its spawn position (screen drawn up-left of Nene, stereoBG
    /// showing as a black box) until the song started; the botplay health bar also stayed half red.</summary>
    private void UpdateDuringCountdown(float dt)
    {
        for (int i = 0; i < _animatedProps.Count; i++)
        {
            var p = _animatedProps[i];
            p.Sprite?.Update(dt);
            if (i >= _animatedProps.Count || _animatedProps[i].Sprite != p.Sprite) { i--; continue; }
            if (!p.CanPlayOtherAnims && p.Sprite != null && p.Sprite.Finished) { p.CanPlayOtherAnims = true; _animatedProps[i] = p; }
        }
        UpdateAbotFallbackAnchors();
        if (UseVSliceHud) HudUpdate(dt);
        if (_vsPlayState != null && !PsychNotes) VsUpdate(dt);   // V-Slice: onUpdate is dispatched during the countdown too
        // FlxCamera.updateFollow runs during the countdown too (PlayState.create -> resetCamera() snaps BEFORE startCountdown,
        // so a follow point set in onCountdownStart, e.g. QT obliterated.hxc, is reached by lerp while counting down).
        if (!_isPsychEngineStage && !PsychNotes && !_camFollowTweening)
        {
            float k = 1f - MathF.Pow(1f - 0.04f, dt * 60f);
            _camWorldX += (_camTargetWorldX - _camWorldX) * k;
            _camWorldY += (_camTargetWorldY - _camWorldY) * k;
        }
    }
}
