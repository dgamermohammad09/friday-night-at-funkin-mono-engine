using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// CharSelectSubState unlock flow (V-Slice 0.8.1):
///  - initLocks: an available player not in Save.charactersSeen is shown as a Lock; unlocked ones go to nonLocks.
///  - create: IntroSubState plays videos/introSelect while Save.oldChar is false; on close camera.flash() + CS_Lights
///    (first time only), checkNewChar(), oldChar = true.
///  - checkNewChar: nonLocks -> selectTimer 2 s -> unLock(); else stayFunky + allowInput.
///  - unLock (CharSelectSubState.hx:532): cursor to the slot, CS_select, 0.5 s later Lock 'unlock' (lockedChill 'death' at
///    lock frame 40), CS_unlock vol 0.7; lock finished -> flash(white, 0.1), playerChill 'unlock', gf switch, pixel icon,
///    addCharacterSeen; after 'unlock' -> next unLock or music + input.
///  - curChar "locked": playerChill = lockedChill (slidein -> idle loop), gf hidden, 'static loop' sound (0.6),
///    accept -> 'cannot select Label'.
/// </summary>
public partial class CharacterSelectScene
{
    private readonly HashSet<int> _unseenSlots = new();
    private readonly List<int> _nonLocks = new();
    private const string LockedKey = "__locked", UnlockKey = "__unlock";
    private SoundEffectInstance _staticSnd;
    private bool _lockedShown;

    private VideoCutscenePlayer _introVideo;
    private bool _introVideoActive;
    private float _flashAlpha, _flashDur;
    private float _checkNewCharTimer = -1f;

    private int _unlockStage;          // 0 none, 1 waiting 0.5 s, 2 lock anim, 3 playerChill 'unlock'
    private float _unlockWait;
    private int _unlockIndex = -1;
    private string _unlockChar;
    private bool _chillOutVisible, _unlockChillVisible, _unlockGfVisible, _deathStarted;

    private static float SfxVol => HighscoreManager.Data.Mute ? 0f : HighscoreManager.Data.SfxVolume;

    /// <summary>Slot shows a playable character (available AND seen). Locks otherwise.</summary>
    private bool TryPlayableSlot(int slot, out string charId)
    {
        if (_availableChars.TryGetValue(slot, out charId) && !_unseenSlots.Contains(slot)) return true;
        charId = null;
        return false;
    }
    private bool SlotShowsLock(int slot) => !_availableChars.ContainsKey(slot) || _unseenSlots.Contains(slot);

    /// <summary>CharSelectSubState.initLocks bookkeeping.</summary>
    private void InitUnlockSlots()
    {
        _unseenSlots.Clear(); _nonLocks.Clear();
        for (int i = 0; i < 9; i++)
        {
            if (!_availableChars.TryGetValue(i, out var id)) continue;
            if (Unlocks.IsCharacterSeen(id)) continue;
            _unseenSlots.Add(i);
            if (Unlocks.IsPlayerUnlocked(id)) _nonLocks.Add(i);
        }
        Console.WriteLine($"[CharSelect] unseen slots [{string.Join(",", _unseenSlots)}] nonLocks [{string.Join(",", _nonLocks)}] oldChar={Unlocks.OldChar}");
    }

    private void LoadUnlockSheets()
    {
        try
        {
            var ls = LoadChillSheet("charSelect/lockedChill", SparrowPlayerFeet, SparrowPlayerBox);
            if (ls != null) { _playerSheets[LockedKey] = ls; _playerAnims[LockedKey] = new CharAnim(); }
            if (_nonLocks.Count > 0)
            {
                // playerChill 'unlock' lives only in the original <id>Chill atlas (the engine art has no unlock label)
                string id = _availableChars[_nonLocks[0]];
                var us = LoadChillSheet("charSelect/" + id + "Chill", SparrowPlayerFeet, SparrowPlayerBox);
                if (us != null && us.RawCompositeData.ContainsKey("unlock")) { _playerSheets[UnlockKey] = us; _playerAnims[UnlockKey] = new CharAnim(); }
            }
        }
        catch (Exception ex) { Console.WriteLine($"[CharSelect] unlock sheets: {ex.Message}"); }
    }

    /// <summary>Replaces "start music + allow input" at the end of loading (create + IntroSubState).</summary>
    private void StartCsIntroFlow()
    {
        _allowInput = false;
        LoadUnlockSheets();
        if (!Unlocks.OldChar && Environment.GetEnvironmentVariable("FNF_CS_NOVIDEO") != "1")
        {
            Audio.StopMusic(); // IntroSubState: FlxG.sound.music.destroy()
            _introVideo = VideoCutscenePlayer.Create(Game, Assets, "introSelect");
            if (_introVideo != null && _introVideo.Play(SfxVolForVideo())) { _introVideoActive = true; return; }
            _introVideo?.Dispose(); _introVideo = null;
        }
        OnIntroClosed();
    }

    private static float SfxVolForVideo() => HighscoreManager.Data.Mute ? 0f : HighscoreManager.Data.MusicVolume;

    /// <summary>subStateClosed: remove(blackScreen); first time: camera.flash() + CS_Lights; checkNewChar(); oldChar = true.</summary>
    private void OnIntroClosed()
    {
        _introVideoActive = false;
        if (_introVideo != null) { _introVideo.Dispose(); _introVideo = null; }
        if (!Unlocks.OldChar)
        {
            Flash(1f);
            Audio.PlaySound("menus/character_select/CS_Lights", 1f);
        }
        if (_nonLocks.Count > 0) _checkNewCharTimer = 2f; else StartCsMusic();
        Unlocks.OldChar = true;
    }

    private void StartCsMusic()
    {
        Audio.PlayMusic("menus/character_select/character_select", true);
        _allowInput = true;
    }

    private void Flash(float duration) { _flashAlpha = 1f; _flashDur = duration; }

    /// <summary>Per-frame flow update (after UpdateCore).</summary>
    private void UpdateUnlockFlow(float dt)
    {
        if (_flashAlpha > 0f) _flashAlpha = Math.Max(0f, _flashAlpha - dt / Math.Max(0.001f, _flashDur));

        if (_introVideoActive && _introVideo != null)
        {
            _introVideo.Update(SfxVolForVideo());
            if (_introVideo.Ended) OnIntroClosed();
        }
        if (_checkNewCharTimer > 0f)
        {
            _checkNewCharTimer -= dt;
            if (_checkNewCharTimer <= 0f) { _checkNewCharTimer = -1f; UnLock(); }
        }

        // curChar "locked" (set_curChar): lockedChill slides in, static loop plays
        bool lockedNow = _unlockStage == 0 && !_availableChars.ContainsKey(GetCurrentSelected()) | _unseenSlots.Contains(GetCurrentSelected());
        if (_unlockStage == 0) _onLockedSlot = lockedNow;
        if (lockedNow != _lockedShown)
        {
            _lockedShown = lockedNow;
            if (lockedNow) { PlayLockedChill("Enter"); StartStatic(); }
            else StopStatic();
        }

        if (_unlockStage == 1)
        {
            _unlockWait -= dt;
            if (_unlockWait <= 0f) BeginLockUnlock();
        }
        else if (_unlockStage == 2)
        {
            var la = _lockAnims[_unlockIndex];
            if (!_deathStarted && la.Name == "unlock" && la.Frame >= 40)
            {
                _deathStarted = true;
                PlayChill(LockedKey, "death", false);
                if (_playerAnims.TryGetValue(LockedKey, out var da)) da.OnFinish = () => { _chillOutVisible = false; da.OnFinish = null; };
            }
            if (la.Name == "unlock" && la.Finished) LockFinished();
        }
    }

    /// <summary>unLock(): cursor to nonLocks[0], CS_select, selectTimer 0.5 s.</summary>
    private void UnLock()
    {
        if (_nonLocks.Count == 0) { StartCsMusic(); return; }
        int index = _nonLocks[0];
        _nonLocks.RemoveAt(0);
        _pressedSelect = true; _selectTimer = float.MaxValue; // no confirm exit while unlocking
        SetCursorFromIndex(index);
        Audio.PlaySound("menus/character_select/scroll");
        _unlockIndex = index; _unlockChar = _availableChars[index];
        _unlockStage = 1; _unlockWait = 0.5f;
        _onLockedSlot = true;
        if (!_lockedShown) { _lockedShown = true; PlayLockedChill("Enter"); StartStatic(); }
        Console.WriteLine($"[CharSelect] unLock slot {index} ({_unlockChar})");
    }

    private void BeginLockUnlock()
    {
        _unlockStage = 2; _deathStarted = false;
        _lockAnims[_unlockIndex].Play("unlock", true);
        var s = Audio.CreateSoundInstance("menus/character_select/CS_unlock");
        if (s != null) { s.Volume = Math.Clamp(0.7f * SfxVol, 0f, 1f); s.Play(); }
        _unlockChillVisible = false;
        _chillOutVisible = true; // playerChillOut (lockedChill) stays on screen
        _unlockGfVisible = false;
    }

    private void LockFinished()
    {
        string ch = _unlockChar;
        Flash(0.1f);
        _unlockStage = 3;
        _unseenSlots.Remove(_unlockIndex);        // grpIcons: lock replaced by the PixelatedIcon
        _curChar = ch;
        SwitchCharacterSprites(ch);                // nametag.switchChar + gfChill.switchGF
        _unlockGfVisible = true;
        _unlockChillVisible = true;
        bool hasUnlock = _playerSheets.ContainsKey(UnlockKey);
        if (hasUnlock)
        {
            PlayChill(UnlockKey, "unlock", false);
            _playerAnims[UnlockKey].OnFinish = () => { _playerAnims[UnlockKey].OnFinish = null; PlayerUnlockFinished(); };
        }
        Unlocks.AddCharacterSeen(ch);
        Console.WriteLine($"[CharSelect] unlocked {ch}");
        if (_nonLocks.Count == 0)
        {
            // nonLocks empty: pressedSelect = false, staticSound.stop(), playMusic('stayFunky') -> allowInput (right away)
            _pressedSelect = false; _selectTimer = 0f;
            _onLockedSlot = false; _lockedShown = false;
            StopStatic();
            StartCsMusic();
        }
        if (!hasUnlock) PlayerUnlockFinished();
    }

    private void PlayerUnlockFinished()
    {
        // CharSelectPlayer: 'unlock' finished -> 'idle'
        if (_playerAnims.TryGetValue(_curChar, out var pa) && _playerSheets.TryGetValue(_curChar, out var ps))
        {
            pa.OnFinish = null;
            string idle = FindAnim(ps, "Idle", "idle");
            if (idle != null) pa.Play(ps, idle, true);
        }
        if (_nonLocks.Count == 0)
        {
            _unlockStage = 0; _unlockChillVisible = false; _chillOutVisible = false;
        }
        else { _unlockStage = 0; UnLock(); }
    }

    private void PlayLockedChill(string anim)
    {
        if (!_playerSheets.TryGetValue(LockedKey, out var ls) || !_playerAnims.TryGetValue(LockedKey, out var la)) return;
        string a = FindAnim(ls, anim, anim.ToLowerInvariant());
        if (a == null) return;
        la.OnFinish = null;
        la.Play(ls, a, false);
        // CharSelectPlayer.onFinish: slidein / 'cannot select Label' -> idle (locked idle loops)
        la.OnFinish = () => { string idle = FindAnim(ls, "Idle", "idle"); la.OnFinish = null; if (idle != null) la.Play(ls, idle, true); };
    }

    private void PlayChill(string key, string anim, bool loop)
    {
        if (_playerSheets.TryGetValue(key, out var s) && _playerAnims.TryGetValue(key, out var a)) { a.OnFinish = null; a.Play(s, anim, loop); }
    }

    /// <summary>Accept on a locked slot: playerChill.anim.play("cannot select Label").</summary>
    private void LockedAccept() => PlayLockedChill("cannot select Label");

    private void StartStatic()
    {
        if (_staticSnd == null) { _staticSnd = Audio.CreateSoundInstance("menus/character_select/static loop"); if (_staticSnd != null) _staticSnd.IsLooped = true; }
        if (_staticSnd == null) return;
        _staticSnd.Volume = Math.Clamp(0.6f * SfxVol, 0f, 1f);
        if (_staticSnd.State != SoundState.Playing) _staticSnd.Play();
    }
    private void StopStatic() { try { _staticSnd?.Stop(); } catch { } }

    private void DisposeUnlockFlow()
    {
        StopStatic();
        try { _staticSnd?.Dispose(); } catch { }
        _staticSnd = null;
        _introVideo?.Dispose(); _introVideo = null;
    }

    /// <summary>Player/spectator layer while a lock is shown or an unlock plays. Returns true when it drew the layer.</summary>
    private bool DrawUnlockPlayers(SpriteBatch sb, float scrollX, float scrollY)
    {
        if (_unlockStage >= 2)
        {
            if (_unlockGfVisible)
            {
                var specOff = _spectatorOffsets.GetValueOrDefault(_curChar, Vector2.Zero);
                DrawCharacterComposite(sb, _spectatorAnims, _spectatorSheets, _curChar, specOff.X - scrollX, specOff.Y - scrollY);
            }
            if (_chillOutVisible) DrawCharacterComposite(sb, _playerAnims, _playerSheets, LockedKey, -scrollX, -scrollY);
            if (_unlockChillVisible)
            {
                if (_playerSheets.ContainsKey(UnlockKey) && !(_playerAnims[UnlockKey].Finished))
                    DrawCharacterComposite(sb, _playerAnims, _playerSheets, UnlockKey, -scrollX, -scrollY);
                else
                {
                    var playerOff = _playerOffsets.GetValueOrDefault(_curChar, Vector2.Zero);
                    DrawCharacterComposite(sb, _playerAnims, _playerSheets, _curChar, playerOff.X - scrollX, playerOff.Y - scrollY);
                }
            }
            return true;
        }
        if (_onLockedSlot && _playerSheets.ContainsKey(LockedKey))
        {
            DrawCharacterComposite(sb, _playerAnims, _playerSheets, LockedKey, -scrollX, -scrollY);
            return true;
        }
        return false;
    }

    /// <summary>IntroSubState video (over everything) + FlxG.camera.flash.</summary>
    private void DrawUnlockOverlay(SpriteBatch sb)
    {
        int W = FNFGame.SCREEN_WIDTH, H = FNFGame.SCREEN_HEIGHT;
        if (_introVideoActive && _introVideo != null)
        {
            sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.AlphaBlend);
            sb.Draw(Assets.Pixel, new Rectangle(0, 0, W, H), Color.Black); // blackScreen
            _introVideo.Draw(sb, W, H);
            sb.End();
        }
        if (_flashAlpha > 0f)
        {
            sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);
            sb.Draw(Assets.Pixel, new Rectangle(0, 0, W, H), new Color(Color.White, _flashAlpha));
            sb.End();
        }
    }
}
