using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework.Audio;
using FNF_MonoGame.Engine.PsychEngine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Psych Engine 1.0.4 tagged sounds (psychlua/FunkinLua.hx playSound / pauseSound / resumeSound / stopSound /
/// soundFadeIn / soundFadeOut / soundFadeCancel / get+setSoundVolume / getSoundTime / get+setSoundPitch /
/// luaSoundExists): playSound(sound, volume, tag, loop) keeps the FlxSound under 'sound_' + tag, replacing an older
/// one; on completion onSoundFinished(tag) is called (and the tag removed unless looping).
/// Limitation: SoundEffectInstance cannot seek, so setSoundTime is ignored; getSoundTime is the played time.
/// </summary>
public partial class PlayScene
{
    private sealed class PsychTaggedSound
    {
        public SoundEffectInstance Inst; public string Tag; public bool Loop; public float Volume = 1f; public float Pitch = 1f;
        public bool Paused; public bool Stopped; public double TimeMs;
        public float FadeFrom, FadeTo, FadeDur, FadeT = -1f;
    }
    private readonly Dictionary<string, PsychTaggedSound> _psychSounds = new(StringComparer.Ordinal);

    private static string PsychSoundPath(string sound)
    {
        if (string.IsNullOrEmpty(sound)) return null;
        string mod = PsychMods.ModFolders("sounds/" + sound + ".ogg");
        if (mod != null) return mod;
        string c = System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot, "sounds", sound + ".ogg");
        return global::FNF_MonoGame.Engine.FsCI.FileExists(c) ? c : "sounds/" + sound;
    }

    private void ApplyPsychSoundVolume(PsychTaggedSound s)
    {
        if (s?.Inst == null) return;
        try { s.Inst.Volume = Math.Clamp(Audio.SfxVolume * s.Volume, 0f, 1f); } catch { }
    }

    /// <summary>playSound(sound, volume = 1, tag = null, loop = false)</summary>
    public string PsychPlaySound(string sound, float volume, string tag, bool loop)
    {
        string path = PsychSoundPath(sound);
        if (string.IsNullOrEmpty(tag))
        {
            try { Audio.PlaySound(path, volume); } catch (Exception e) { Console.WriteLine($"[PSYCH-SOUND] {sound}: {e.Message}"); }
            return null;
        }
        PsychStopSound(tag);
        SoundEffectInstance inst = null;
        try { inst = Audio.CreateSoundInstance(path); } catch (Exception e) { Console.WriteLine($"[PSYCH-SOUND] {sound}: {e.Message}"); }
        var s = new PsychTaggedSound { Inst = inst, Tag = tag, Loop = loop, Volume = volume };
        _psychSounds[tag] = s;
        if (inst != null)
        {
            inst.IsLooped = false; // loops are restarted in PsychSoundsUpdate so onSoundFinished fires per loop (FlxSound onComplete)
            ApplyPsychSoundVolume(s);
            inst.Play();
        }
        else s.Stopped = true;
        return "sound_" + tag;
    }

    public void PsychStopSound(string tag)
    {
        if (tag == null || !_psychSounds.TryGetValue(tag, out var s)) return;
        try { s.Inst?.Stop(); s.Inst?.Dispose(); } catch { }
        _psychSounds.Remove(tag);
    }

    public void PsychPauseSound(string tag) { if (tag != null && _psychSounds.TryGetValue(tag, out var s) && s.Inst != null) { s.Paused = true; try { s.Inst.Pause(); } catch { } } }
    public void PsychResumeSound(string tag) { if (tag != null && _psychSounds.TryGetValue(tag, out var s) && s.Inst != null) { s.Paused = false; try { s.Inst.Resume(); } catch { } } }
    public bool PsychSoundExists(string tag) => tag != null && _psychSounds.ContainsKey(tag);
    public float PsychGetSoundVolume(string tag) => tag != null && _psychSounds.TryGetValue(tag, out var s) ? s.Volume : 0f;
    public void PsychSetSoundVolume(string tag, float v) { if (tag != null && _psychSounds.TryGetValue(tag, out var s)) { s.Volume = v; ApplyPsychSoundVolume(s); } }
    public double PsychGetSoundTime(string tag) => tag != null && _psychSounds.TryGetValue(tag, out var s) ? s.TimeMs : 0;
    public float PsychGetSoundPitch(string tag) => tag != null && _psychSounds.TryGetValue(tag, out var s) ? s.Pitch : 1f;
    public void PsychSetSoundPitch(string tag, float p)
    {
        if (tag == null || !_psychSounds.TryGetValue(tag, out var s)) return;
        s.Pitch = p;
        // FlxSound.pitch = playback rate; XNA Pitch = octaves (-1..1)
        try { if (s.Inst != null) s.Inst.Pitch = Math.Clamp((float)(Math.Log(Math.Max(p, 0.0001f)) / Math.Log(2.0)), -1f, 1f); } catch { }
    }
    public void PsychSoundFade(string tag, float duration, float from, float to)
    {
        if (tag == null || !_psychSounds.TryGetValue(tag, out var s)) return;
        s.FadeFrom = from; s.FadeTo = to; s.FadeDur = Math.Max(duration, 0f); s.FadeT = 0f;
        s.Volume = from; ApplyPsychSoundVolume(s);
    }
    public void PsychSoundFadeCancel(string tag) { if (tag != null && _psychSounds.TryGetValue(tag, out var s)) s.FadeT = -1f; }

    /// <summary>Per frame: fades, played time, completion -> onSoundFinished(tag).</summary>
    private void PsychSoundsUpdate(float dt)
    {
        if (_psychSounds.Count == 0) return;
        List<string> finished = null;
        foreach (var kv in _psychSounds)
        {
            var s = kv.Value;
            if (s.Inst == null || s.Paused) continue;
            if (s.FadeT >= 0f)
            {
                s.FadeT += dt;
                float k = s.FadeDur <= 0f ? 1f : Math.Min(1f, s.FadeT / s.FadeDur);
                s.Volume = s.FadeFrom + (s.FadeTo - s.FadeFrom) * k;
                ApplyPsychSoundVolume(s);
                if (k >= 1f) s.FadeT = -1f;
            }
            SoundState st;
            try { st = s.Inst.State; } catch { st = SoundState.Stopped; }
            if (st == SoundState.Playing) s.TimeMs += dt * 1000.0 * s.Pitch;
            else if (st == SoundState.Stopped) (finished ??= new List<string>()).Add(kv.Key);
        }
        if (finished == null) return;
        foreach (var tag in finished)
        {
            if (!_psychSounds.TryGetValue(tag, out var s)) continue;
            if (s.Loop) { s.TimeMs = 0; try { s.Inst.Play(); } catch { } }
            else { try { s.Inst.Dispose(); } catch { } _psychSounds.Remove(tag); }
            CallOnLuas("onSoundFinished", tag);
        }
    }

    private void PsychSoundsDispose()
    {
        foreach (var s in _psychSounds.Values) { try { s.Inst?.Stop(); s.Inst?.Dispose(); } catch { } }
        _psychSounds.Clear();
    }
}
