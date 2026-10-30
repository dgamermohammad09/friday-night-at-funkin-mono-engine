using System;
using System.Collections.Generic;

namespace FNF_MonoGame.Gameplay;

/// <summary>
/// BaseCharacter combo / drop count animations (BaseCharacter.hx findCountAnimations, playComboAnimation,
/// playComboDropAnimation, onAnimationFinished): a GF-type character plays 'combo&lt;N&gt;' when the player's hit makes
/// the combo exactly N (sick/good), and the highest 'drop&lt;N&gt;' with N &lt;= combo when the combo is broken
/// (bad/shit hit, note miss, hold drop). Both use playAnimation(name, restart = true, ignoreOther = true)
/// (Bopper.canPlayOtherAnims = false until the animation finishes) and dance(true) once they finish.
/// </summary>
public partial class Character
{
    private List<int> _comboNoteCounts, _dropNoteCounts;
    private string _countAnimPlaying;
    public int FrameIndexForLog => _sprite?.FrameIndex ?? -1;

    /// <summary>Fired after playAnimation("combo&lt;N&gt;"/"drop&lt;N&gt;") (character scripts hook it: nene-tankmen combo50 heart).</summary>
    public Action<string> OnCountAnimationPlayed;

    private List<int> FindCountAnimations(string prefix)
    {
        var result = new List<int>();
        var names = JsonData?.Animations;
        if (names == null) return result;
        foreach (var a in names)
        {
            string n = a?.Name;
            if (n == null || !n.StartsWith(prefix, StringComparison.Ordinal)) continue;
            // Std.parseInt(anim.substring(prefix.length)): leading integer (non-numeric suffix -> null)
            string rest = n.Substring(prefix.Length);
            int len = 0;
            while (len < rest.Length && char.IsDigit(rest[len])) len++;
            if (len == 0 || !int.TryParse(rest.Substring(0, len), out int v)) continue;
            if (ResolveAnimName(n) == null) continue;   // animation.getNameList(): only animations that were added
            result.Add(v);
        }
        result.Sort();
        return result;
    }

    public IReadOnlyList<int> ComboNoteCounts => _comboNoteCounts ??= FindCountAnimations("combo");
    public IReadOnlyList<int> DropNoteCounts => _dropNoteCounts ??= FindCountAnimations("drop");

    /// <summary>BaseCharacter.playComboAnimation(comboCount).</summary>
    public bool PlayComboAnimation(int comboCount)
    {
        if (!ComboNoteCounts.Contains(comboCount)) return false;
        return PlayCountAnimation("combo" + comboCount);
    }

    /// <summary>BaseCharacter.playComboDropAnimation(comboCount): highest drop&lt;N&gt; with comboCount &gt;= N.</summary>
    public bool PlayComboDropAnimation(int comboCount)
    {
        string dropAnim = null;
        foreach (int count in DropNoteCounts)
            if (comboCount >= count) dropAnim = "drop" + count;
        return dropAnim != null && PlayCountAnimation(dropAnim);
    }

    private bool PlayCountAnimation(string name)
    {
        // Bopper.playAnimation: while !canPlayOtherAnims only a restart of the same animation may play
        if (AnimLocked && _sprite != null && !_sprite.Finished && CurrentAnimation != name) return false;
        PlayAnimation(name, force: true);
        AnimLocked = true;
        _countAnimPlaying = name;
        if (Environment.GetEnvironmentVariable("FNF_COMBOLOG") == "1")
            Console.WriteLine($"[GFCOMBO] {OriginalName ?? Name} play {name} t={FNF_MonoGame.Scenes.PlayScene.Current?.SongPositionSeconds:F3}");
        OnCountAnimationPlayed?.Invoke(name);
        return true;
    }

    /// <summary>onAnimationFinished: combo*/drop* -> dance(true) (BaseCharacter.dance(true) -> script dance / Bopper.dance()).</summary>
    private void UpdateCountAnimation()
    {
        if (_countAnimPlaying == null) return;
        if (CurrentAnimation != _countAnimPlaying) { _countAnimPlaying = null; return; }
        if (_sprite != null && !_sprite.Finished) return;
        _countAnimPlaying = null;
        AnimLocked = false;
        if (IsDead) return;
        if (!DanceOverride()) PsychDanceNow();
    }
}
