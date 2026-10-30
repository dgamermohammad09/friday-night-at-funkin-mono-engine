using Microsoft.Xna.Framework.Audio;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Static audio helper matching Psych Engine's FlxG.sound.play behavior.
/// Delegates to the game's AudioManager.
/// </summary>
public static class Audio
{
    public static Sound PlaySound(string name, float volume = 1f)
    {
        return FNFGame.Instance?.Audio?.PlaySound(name, volume) ?? new Sound();
    }

    public static Sound PlaySound(string name, float volume, bool looped, bool autoDestroy)
    {
        return PlaySound(name, volume);
    }

#if XBOX_UWP
    public static FNF_MonoGame.XboxAudioManager Music => FNFGame.Instance?.Audio;
#else
    public static FNF_MonoGame.Engine.AudioManager Music => FNFGame.Instance?.Audio;
#endif

    public static void PlayMusic(string name, int startTimeMs = 0, bool loop = true)
    {
        FNFGame.Instance?.Audio?.PlayMusic(name, loop, startTimeMs);
    }
}
