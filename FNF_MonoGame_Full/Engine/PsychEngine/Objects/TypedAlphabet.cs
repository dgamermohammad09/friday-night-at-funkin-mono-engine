using System;

namespace FNF_MonoGame.Engine.PsychEngine.Objects;

/// <summary>
/// Psych Engine typewriter-style Alphabet. Direct port of objects/TypedAlphabet.hx.
///
/// Reveals letters one-by-one at a fixed interval, playing an optional sound per
/// revealed character. Assigning a new <see cref="Alphabet.Text"/> resets the reveal
/// automatically (matches Psych's <c>set_text</c> override). Consumers can force
/// the entire text to appear immediately via <see cref="FinishText"/>.
///
/// Used by dialogue and cutscene systems; mods author their own typewriter dialogs
/// on top of this class.
/// </summary>
public class TypedAlphabet : Alphabet
{
    /// <summary>Callback invoked once when the last letter is revealed.</summary>
    public Action OnFinish;

    /// <summary>True once every letter has been revealed.</summary>
    public bool FinishedText { get; private set; }

    /// <summary>Delay in seconds between letter reveals. Default 0.05 s (matches Psych).</summary>
    public float Delay { get; set; } = 0.05f;

    /// <summary>Sound path (without extension) played on each reveal. Empty disables sound.</summary>
    public string Sound { get; set; } = "dialogue";

    /// <summary>Playback volume for the reveal sound.</summary>
    public float Volume { get; set; } = 1f;

    private int _curLetter = -1;
    private float _timeToUpdate;

    public TypedAlphabet(float x, float y, string text = "", float delay = 0.05f, bool bold = false)
        : base(x, y, text, bold)
    {
        Delay = delay;
        // Text was set inside the base constructor which fired OnTextChanged -> ResetDialogue
        // via the virtual dispatch. This ensures every letter starts hidden.
    }

    /// <summary>
    /// Reset the typewriter to the start: hide every letter, clear the accumulator.
    /// Called automatically whenever <see cref="Alphabet.Text"/> is reassigned.
    /// </summary>
    public void ResetDialogue()
    {
        _curLetter = -1;
        FinishedText = false;
        _timeToUpdate = 0;
        foreach (var letter in Letters)
            letter.Visible = false;
    }

    /// <summary>Reveal every letter from <see cref="_curLetter"/>+1 up to and including <paramref name="upTo"/>.</summary>
    public void ShowCharacterUpTo(int upTo)
    {
        int start = _curLetter;
        if (start < 0) start = 0;
        int end = Math.Min(upTo, Letters.Count - 1);
        for (int i = start; i <= end; i++)
            Letters[i].Visible = true;
    }

    /// <summary>Immediately reveal every remaining letter, fire the finish callback, and mark complete.</summary>
    public void FinishText()
    {
        if (FinishedText) return;
        if (Letters.Count > 0)
            ShowCharacterUpTo(Letters.Count - 1);
        if (!string.IsNullOrEmpty(Sound))
            PlayRevealSound();
        FinishedText = true;
        _timeToUpdate = 0;
        OnFinish?.Invoke();
    }

    protected override void OnTextChanged()
    {
        base.OnTextChanged();
        ResetDialogue();
    }

    public override void Update(float elapsed)
    {
        if (!FinishedText && Letters.Count > 0)
        {
            bool playedSound = false;
            _timeToUpdate += elapsed;
            while (_timeToUpdate >= Delay)
            {
                ShowCharacterUpTo(_curLetter + 1);
                // Match Psych's stagger: play sound on every reveal for slow typing,
                // or every other letter for fast typing (<= 0.025 s delay).
                if (!playedSound && !string.IsNullOrEmpty(Sound)
                    && (Delay > 0.025f || _curLetter % 2 == 0))
                {
                    PlayRevealSound();
                    playedSound = true;
                }
                _curLetter++;

                if (_curLetter >= Letters.Count - 1)
                {
                    FinishedText = true;
                    OnFinish?.Invoke();
                    _timeToUpdate = 0;
                    break;
                }
                _timeToUpdate = 0;
            }
        }

        base.Update(elapsed);
    }

    private void PlayRevealSound()
    {
        try
        {
            var audio = FNFGame.Instance?.Audio;
            if (audio == null) return;
            // Psych calls Paths.sound(sound); our port's AudioManager takes the logical
            // sound name and resolves via mod-aware PsychPaths. Falling back to PlaySound
            // without volume support keeps behavior consistent across sound backends.
            audio.PlaySound(Sound, Volume);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TypedAlphabet] reveal sound '{Sound}' failed: {ex.Message}");
        }
    }
}
