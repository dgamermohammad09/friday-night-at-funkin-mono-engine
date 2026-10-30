using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Audio;

namespace FNF_MonoGame.Engine
{
    /// <summary>
    /// Owned SoundEffectInstance pool for one-shot SFX (shared by the desktop AudioManager and the UWP XboxAudioManager).
    /// MonoGame's fire-and-forget SoundEffect.Play() returns false (silently) when its global pool is full and recycles
    /// instances only in FrameworkDispatcher.Update, so bursts (menu scroll spam, hitsounds, countdown + note sounds on the
    /// same frame) could drop sounds. Here every request gets a voice: a stopped instance of the same sound is reused,
    /// a new one is created up to <see cref="MaxPerSound"/> / <see cref="MaxTotal"/>, otherwise the OLDEST instance of
    /// that sound is restarted (like FlxG.sound recycling). Requests are never dropped silently: dev log [SFXDBG]
    /// (FNF_SFXDBG=1) prints name, loaded flag, instance count and the resulting state of every request.
    /// </summary>
    public sealed class SfxPool
    {
        public const int MaxPerSound = 16;
        public const int MaxTotal = 128;

        private sealed class Entry { public SoundEffectInstance Inst; public long Seq; }
        private readonly Dictionary<SoundEffect, List<Entry>> _pools = new Dictionary<SoundEffect, List<Entry>>();
        private int _total;
        private long _seq;

        public static bool Log = Environment.GetEnvironmentVariable("FNF_SFXDBG") == "1";
        public int Requests, Playing, Failed;

        /// <summary>SFX used by menus and every song: decoded at startup (PreloadCommonSfx).</summary>
        public static readonly string[] CommonSfx =
        {
            "scrollMenu", "confirmMenu", "cancelMenu",
            "game/skins/default/countdown/3", "game/skins/default/countdown/2",
            "game/skins/default/countdown/1", "game/skins/default/countdown/go",
            "missnote1", "missnote2", "missnote3", "psych/hitsound",
        };

        public static void RefreshLogFlag() { Log = Environment.GetEnvironmentVariable("FNF_SFXDBG") == "1"; }

        /// <summary>Plays <paramref name="sfx"/> on a pooled instance. Returns the instance (null only if it could not start).</summary>
        public SoundEffectInstance Play(SoundEffect sfx, string name, float volume, bool wasCached)
        {
            Requests++;
            if (sfx == null || sfx.IsDisposed)
            {
                Failed++;
                if (Log) Console.WriteLine($"[SFXDBG] #{Requests} '{name}' loaded=False result=MISSING");
                return null;
            }
            if (!_pools.TryGetValue(sfx, out var list)) { list = new List<Entry>(4); _pools[sfx] = list; }

            Entry e = null;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var x = list[i];
                if (x.Inst == null || x.Inst.IsDisposed) { list.RemoveAt(i); _total--; continue; }
                if (e == null && SafeState(x.Inst) == SoundState.Stopped) e = x;
            }
            string how = "reuse";
            if (e == null && list.Count < MaxPerSound && (_total < MaxTotal || EvictOtherStopped(sfx)))
            {
                SoundEffectInstance inst = null;
                try { inst = sfx.CreateInstance(); } catch (Exception ex) { if (Log) Console.WriteLine($"[SFXDBG] '{name}' CreateInstance failed: {ex.Message}"); }
                if (inst != null) { e = new Entry { Inst = inst }; list.Add(e); _total++; how = "new"; }
            }
            if (e == null && list.Count > 0)
            {
                // all instances of this sound busy (spam): restart the oldest one
                e = list[0];
                for (int i = 1; i < list.Count; i++) if (list[i].Seq < e.Seq) e = list[i];
                how = "steal";
            }
            if (e == null)
            {
                Failed++;
                if (Log) Console.WriteLine($"[SFXDBG] #{Requests} '{name}' loaded={wasCached} inst=0 total={_total} result=NO_VOICE");
                return null;
            }

            SoundState st = SoundState.Stopped;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    var inst = e.Inst;
                    if (SafeState(inst) != SoundState.Stopped) inst.Stop();
                    inst.IsLooped = false;
                    inst.Volume = Math.Max(0f, Math.Min(1f, volume));
                    inst.Pitch = 0f; inst.Pan = 0f;
                    inst.Play();
                    st = SafeState(inst);
                }
                catch (Exception ex)
                {
                    if (Log) Console.WriteLine($"[SFXDBG] '{name}' Play threw {ex.GetType().Name}: {ex.Message}");
                    st = SoundState.Stopped;
                }
                if (st == SoundState.Playing) break;
                // broken voice: replace it with a fresh instance and retry once
                try { e.Inst.Dispose(); } catch { }
                try { e.Inst = sfx.CreateInstance(); } catch { e.Inst = null; }
                if (e.Inst == null) break;
                how += "+retry";
            }
            e.Seq = ++_seq;
            if (st == SoundState.Playing) Playing++; else Failed++;
            if (Log) Console.WriteLine($"[SFXDBG] #{Requests} '{name}' loaded={wasCached} vol={volume:F2} inst={list.Count} total={_total} {how} state={st}");
            return st == SoundState.Playing ? e.Inst : null;
        }

        private bool EvictOtherStopped(SoundEffect keep)
        {
            foreach (var kv in _pools)
            {
                if (ReferenceEquals(kv.Key, keep)) continue;
                var l = kv.Value;
                for (int i = 0; i < l.Count; i++)
                {
                    if (l[i].Inst == null || l[i].Inst.IsDisposed || SafeState(l[i].Inst) == SoundState.Stopped)
                    {
                        try { l[i].Inst?.Dispose(); } catch { }
                        l.RemoveAt(i); _total--;
                        return true;
                    }
                }
            }
            return false;
        }

        private static SoundState SafeState(SoundEffectInstance i)
        {
            try { return i.IsDisposed ? SoundState.Stopped : i.State; } catch { return SoundState.Stopped; }
        }

        private readonly List<SoundEffectInstance> _focusPaused = new List<SoundEffectInstance>();

        /// <summary>FlxG.autoPause (focus lost outside a song): pause every playing one-shot, resumed by ResumeAll.</summary>
        public void PauseAll()
        {
            foreach (var l in _pools.Values)
                foreach (var e in l)
                    if (e.Inst != null && !e.Inst.IsDisposed && SafeState(e.Inst) == SoundState.Playing)
                    {
                        try { e.Inst.Pause(); _focusPaused.Add(e.Inst); } catch { }
                    }
        }

        public void ResumeAll()
        {
            foreach (var i in _focusPaused) { try { if (!i.IsDisposed && i.State == SoundState.Paused) i.Resume(); } catch { } }
            _focusPaused.Clear();
        }

        /// <summary>
        /// Dev/test runs (any FNF_* variable except FNF_WINDOWED/FNF_LOG) never auto-pause on focus loss: test windows
        /// run in the background on this shared PC.
        /// </summary>
        public static bool IsDevRun
        {
            get
            {
                try
                {
                    foreach (System.Collections.DictionaryEntry kv in Environment.GetEnvironmentVariables())
                    {
                        string k = kv.Key as string;
                        if (k != null && k.StartsWith("FNF_", StringComparison.OrdinalIgnoreCase)
                            && !k.Equals("FNF_WINDOWED", StringComparison.OrdinalIgnoreCase) && !k.Equals("FNF_LOG", StringComparison.OrdinalIgnoreCase)
                            && !k.Equals("FNF_FOCUSTEST", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
                catch { }
                return false;
            }
        }

        public void StopAll()
        {
            foreach (var l in _pools.Values) foreach (var e in l) { try { e.Inst?.Stop(); } catch { } }
        }

        /// <summary>Drop pooled instances of a sound that is about to be disposed.</summary>
        public void Forget(SoundEffect sfx)
        {
            if (sfx == null || !_pools.TryGetValue(sfx, out var l)) return;
            foreach (var e in l) { try { e.Inst?.Dispose(); } catch { } }
            _total -= l.Count;
            _pools.Remove(sfx);
        }

        public void Dispose()
        {
            foreach (var l in _pools.Values) foreach (var e in l) { try { e.Inst?.Dispose(); } catch { } }
            _pools.Clear(); _total = 0;
        }
    }
}
