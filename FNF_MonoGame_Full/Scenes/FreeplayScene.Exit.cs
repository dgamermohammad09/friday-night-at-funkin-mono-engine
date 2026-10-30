using System;
using Microsoft.Xna.Framework;

namespace FNF_MonoGame.Scenes;

/// <summary>FreeplayState.goBack() exit movers that need extra state (see the exit block in Update).</summary>
public partial class FreeplayScene
{
    private float _exitArrowT, _exitScoreT, _exitAlbumT;

    // SongMenuItem: xFrames / xPosOutLerpLol
    private static readonly float[] JumpOutXFrames = { 1.7f, 1.8f, 0.85f, 0.85f, 0.97f, 0.97f, 1f };
    private static readonly float[] JumpOutXPos = { 0.245f, 0.75f, 0.98f, 0.98f, 1.2f };
    private int _jumpOutBeat;      // frameOutTypeBeat (0 = not started)
    private float _jumpOutTicker;  // frameOutTicker
    private bool _jumpingOut;

    private void StartCapsuleJumpOut() { _jumpingOut = true; _jumpOutBeat = 0; _jumpOutTicker = 0f; }

    private void UpdateCapsuleJumpOut(float dt)
    {
        if (!_jumpingOut) return;
        _jumpOutTicker += dt;
        if (_jumpOutTicker >= 1f / 24f && _jumpOutBeat < JumpOutXFrames.Length)
        {
            _jumpOutTicker = 0f;
            _jumpOutBeat++;
        }
    }

    /// <summary>FreeplayDJ width (flixel-animate whole-timeline bounds) for the exit target x = -width * 1.6.</summary>
    private float ExitDjWidth()
    {
        // FreeplayDJ (FlxAtlasSprite) width when the exit mover is registered, measured on the running 0.8.1 build:
        // bf 706 (exit target x -1129.6), pico 647 (target -1035.2). The port's DJ atlas does not expose the
        // flixel-animate whole-timeline width, so use the measured values.
        return _isPicoTheme ? 647f : 706f;
    }
}
