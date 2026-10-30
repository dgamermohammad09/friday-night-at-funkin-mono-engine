using System;
using System.Collections.Generic;
using System.Globalization;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Dev hooks used to compare Weekend 1 with the original game (no effect unless the env vars are set):
///   FNF_FORCE_HEALTH="3:0.3,12:1"  from song second 3 hold health at 0.3, from 12 at 1.0 (original botplay never changes health)
///   FNF_NENELOG="t0,t1"            log Nene's anim:frame + knife state changes between song ms t0..t1
/// </summary>
public partial class PlayScene
{
    private static readonly List<(float T, float Hp)> _devHealthPlan = ParseHealthPlan(Environment.GetEnvironmentVariable("FNF_FORCE_HEALTH"));
    private static readonly string _devNeneLog = Environment.GetEnvironmentVariable("FNF_NENELOG");
    private string _devLastNene = "";
    private bool _devHpLogged;

    private static List<(float, float)> ParseHealthPlan(string spec)
    {
        var l = new List<(float, float)>();
        if (string.IsNullOrEmpty(spec)) return l;
        foreach (var part in spec.Split(','))
        {
            var kv = part.Split(':');
            if (kv.Length == 2 && float.TryParse(kv[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var t)
                && float.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var h)) l.Add((t, h));
        }
        l.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return l;
    }

    /// <summary>Called from DispatchUpdate (every frame, also during the countdown).</summary>
    private void Weekend1DevTick()
    {
        if (_devHealthPlan.Count > 0 && _conductor != null && !_gameOver)
        {
            double now = SongPositionSeconds;
            float? hp = null;
            foreach (var (t, h) in _devHealthPlan) if (now >= t) hp = h;
            if (hp.HasValue) _health = hp.Value;
            if (!_devHpLogged) { _devHpLogged = true; Console.WriteLine($"[DEVHP] plan={_devHealthPlan.Count} now={now} hp={hp}"); }
        }
        if (!string.IsNullOrEmpty(_devNeneLog) && _girlfriend != null)
        {
            var p = _devNeneLog.Split(',');
            double ms = SongPositionSeconds * 1000.0;
            if (ms >= double.Parse(p[0], CultureInfo.InvariantCulture) && ms <= double.Parse(p[1], CultureInfo.InvariantCulture))
            {
                string a = $"{_girlfriend.CurrentAnimation}:{_girlfriend.SpriteFrameIndex}";
                if (a != _devLastNene)
                    Console.WriteLine($"NENE t={ms:F1} {a} state={(_girlfriend as FNF_MonoGame.Gameplay.Nene)?.KnifeState} n={_girlfriend.SpriteFrameCount} hp={_health:F3}");
                _devLastNene = a;
            }
        }
    }
}
