using System;

namespace FNF_MonoGame.Scenes;

/// <summary>Dev hook FNF_ANIMLOG="<maxSongMs>": logs every change of bf/dad/gf anim:frame with the song position
/// (ms; negative during the countdown = -5 beats + elapsed countdown time, like the V-Slice Conductor).</summary>
public partial class PlayScene
{
    private static readonly float _animLogMax = float.TryParse(Environment.GetEnvironmentVariable("FNF_ANIMLOG"),
        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : -1f;
    private string _animLogLast = "";

    private void AnimLogTick()
    {
        if (_animLogMax <= 0f || _w1Active) return;
        double ms;
        if (_countdownActive)
        {
            if (_dialogueActive) return;
            double beat = _conductor.Crochet;
            ms = (-5 * beat + Math.Min(_countdownStep, 5) * beat + _countdownTimer) * 1000.0;
        }
        else ms = _conductor.SongPosition * 1000.0;
        if (ms > _animLogMax) return;
        string a = $"bf={_boyfriend?.CurrentAnimation}:{_boyfriend?.SpriteFrameIndex} dad={_opponent?.CurrentAnimation}:{_opponent?.SpriteFrameIndex} gf={_girlfriend?.CurrentAnimation}:{_girlfriend?.SpriteFrameIndex}";
        if (a == _animLogLast) return;
        _animLogLast = a;
        Console.WriteLine($"ANIM t={ms:F0} {a} [TS {System.Diagnostics.Stopwatch.GetTimestamp()/ (System.Diagnostics.Stopwatch.Frequency/1000)}]");
    }
}
