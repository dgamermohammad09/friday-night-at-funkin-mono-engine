using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Port of funkin/play/cutscene/VideoCutscene.hx + the PlayState parts that drive it:
///  - play(path, type): isInCutscene, camHUD hidden, black screen (camCutscene) under the video, onVideoStarted.
///  - hideVideo / showVideo (2hot ending: the video starts hidden while the in-game part runs).
///  - finishVideo(0.5): video removed, camHUD visible, black screen alpha 1 -> 0 (0.5 s quadInOut), then onVideoEnded and
///    onCutsceneFinish(type): STARTING -> startCountdown(), ENDING -> endSong(true), MIDSONG -> nothing.
///    (The FlxG.camera.zoom tween to stageZoom is overwritten every frame by PlayState.update, so it has no effect.)
///  - PlayState.handleCutsceneKeys: PAUSE opens the pause menu in Cutscene mode (video paused):
///    Resume / Skip Cutscene / Restart Cutscene / Exit to Menu (PauseSubState.PAUSE_MENU_ENTRIES_VIDEO_CUTSCENE).
///  - volume = game volume (hxvlc autoVolumeHandle), also while changed in the pause menu.
/// Dev hooks: FNF_VIDEO_SKIP_AT=&lt;s&gt; finishes the video after s seconds (like the orig test module's finishVideo);
/// FNF_VIDEOLOG=1 logs the video clock each second.
/// </summary>
public partial class PlayScene
{
    public enum VideoCutsceneType { Starting, Midsong, Ending }

    private VideoCutscenePlayer _vid;
    private VideoCutsceneType _vidType;
    private bool _vidBlackVisible;           // VideoCutscene.blackScreen (camCutscene, under the video)
    private float _vidBlackAlpha;
    private float _vidFinishT = -1f;         // >= 0 while the 0.5 s finish transition runs
    private float _vidFinishDur;
    private float _vidClock;
    private Action _vidOnEnded;              // onVideoEnded (addOnce semantics: cleared after dispatch)
    private Action _vidOnFinishOverride;     // replaces onCutsceneFinish(type) when set
    private bool _vidHoldCountdown;          // STARTING video: countdown blocked (_dialogueActive)
    private float _vidLastLog = -1f;

    private static readonly float _vidDevSkipAt = ParseEnvPauseF("FNF_VIDEO_SKIP_AT");
    private static readonly float _devEndSongAt = ParseEnvPauseF("FNF_END_SONG_AT");
    private bool _devEndSongDone;
    // dev: FNF_VIDEO_PAUSE_AT=<video s> opens the pause menu; FNF_VIDEO_PAUSE_CONFIRM_AT=<s since pause> confirms the
    // selected entry (use FNF_DEV_PAUSE_DOWN_AT to move the selection first).
    private static readonly float _vidDevPauseAt = ParseEnvPauseF("FNF_VIDEO_PAUSE_AT");
    private static readonly float _vidDevConfirmAt = ParseEnvPauseF("FNF_VIDEO_PAUSE_CONFIRM_AT");
    private bool _vidDevPauseDone, _vidDevConfirmDone;
    private static readonly bool _vidLog = Environment.GetEnvironmentVariable("FNF_VIDEOLOG") == "1";

    public bool VideoIsPlaying => _vid != null;
    public double DevVideoTime => _vid != null ? _vid.PositionSeconds : -1;

    /// <summary>VideoCutscene.play. onStarted = onVideoStarted listeners, onEnded = onVideoEnded (once).
    /// onFinish replaces the default onCutsceneFinish(type). Returns false when the file is missing / cannot play
    /// (the caller should then continue as if the video ended).</summary>
    private bool VideoPlay(string id, VideoCutsceneType type, Action onStarted = null, Action onEnded = null, Action onFinish = null)
    {
        VideoDestroy();
        var v = VideoCutscenePlayer.Create(Game, Assets, id);
        if (v == null) return false;
        _vidType = type;
        _vidOnEnded = onEnded;
        _vidOnFinishOverride = onFinish;
        // isInCutscene = true; camHUD.visible = false; blackScreen on camCutscene
        _w6HudHidden = true;
        _vidBlackVisible = true; _vidBlackAlpha = 1f;
        _vidFinishT = -1f; _vidClock = 0f; _vidLastLog = -1f;
        if (type == VideoCutsceneType.Starting && !_vidHoldCountdown && !_w1Active)
        {
            _vidHoldCountdown = true;
            _dialogueActive = true; // event.cancel(): CANCEL THE COUNTDOWN
        }
        if (!v.Play(VideoVolume)) { v.Dispose(); _vidBlackVisible = false; return false; }
        _vid = v;
        Console.WriteLine($"[VIDEO] play '{id}' type={type}");
        onStarted?.Invoke(); // onVideoStarted.dispatch()
        return true;
    }

    private float VideoVolume => Math.Clamp(Audio.MusicVolume, 0f, 1f);

    /// <summary>VideoCutscene.hideVideo: video and black screen invisible (video keeps playing, audio audible).</summary>
    private void VideoHide() { if (_vid == null) return; _vid.Visible = false; _vidBlackVisible = false; }
    /// <summary>VideoCutscene.showVideo.</summary>
    private void VideoShow() { if (_vid == null) return; _vid.Visible = true; _vidBlackVisible = false; }

    /// <summary>VideoCutscene.finishVideo(transitionTime).</summary>
    private void VideoFinish(float transition = 0.5f)
    {
        if (_vid == null) return;
        Console.WriteLine($"[VIDEO] finish '{_vid.Id}' at {_vid.PositionSeconds:F2}s (clock {_vidClock:F2}s)");
        _vid.Dispose();
        _vid = null;
        _w6HudHidden = false; // camHUD.visible = true
        _vidFinishDur = Math.Max(0.0001f, transition);
        _vidFinishT = 0f;
    }

    /// <summary>VideoCutscene.destroyVideo (song left / restarted): no callbacks.</summary>
    private void VideoDestroy()
    {
        _vid?.Dispose();
        _vid = null;
        _vidBlackVisible = false; _vidFinishT = -1f;
    }

    private static float VideoQuadInOut(float t) => t < 0.5f ? 2f * t * t : 1f - (float)Math.Pow(-2f * t + 2f, 2) / 2f;

    /// <summary>Per frame from PlayScene.Update (before everything else). Returns true when the rest of the update
    /// must be skipped (pause menu open over the video).</summary>
    private bool VideoUpdate(float dt)
    {
        if (_devEndSongAt >= 0f && !_devEndSongDone && !_countdownActive && _conductor.SongPosition >= _devEndSongAt)
        {
            // dev: end the song now (like the orig test module's ps.endSong(false)); music stopped as at a natural end
            _devEndSongDone = true; _forceEndSong = true;
            Audio.PauseMusic(); Audio.StopVoices();
            Console.WriteLine($"[VIDEO] dev end song at {_conductor.SongPosition:F2}");
        }
        if (_vidFinishT >= 0f)
        {
            _vidFinishT += dt;
            float k = Math.Min(1f, _vidFinishT / _vidFinishDur);
            _vidBlackAlpha = 1f - VideoQuadInOut(k);
            if (k >= 1f)
            {
                _vidFinishT = -1f;
                _vidBlackVisible = false;
                var ended = _vidOnEnded; _vidOnEnded = null;
                ended?.Invoke(); // onVideoEnded.dispatch()
                VideoOnCutsceneFinish();
            }
        }
        if (_vid == null) return false;

        if (_paused)
        {
            UpdatePauseMenuV(dt, false);
            if (_vidDevConfirmAt >= 0f && !_vidDevConfirmDone && _pmT >= _vidDevConfirmAt && _pauseItems != null)
            {
                _vidDevConfirmDone = true;
                string devItem = Environment.GetEnvironmentVariable("FNF_VIDEO_PAUSE_ITEM") ?? _pauseItems[_pauseSelection];
                Console.WriteLine($"[VIDEO] dev confirm '{devItem}'");
                ExecutePauseMenuItem(devItem);
                return true;
            }
            // PAUSE_P again closes the pause menu (same as Resume)
            if (_paused && (Input.IsPressed(Keys.Escape) || Input.IsGamePadPressed(Buttons.Start)))
                ExecutePauseMenuItem("Resume");
            return _paused || _vid == null ? true : true;
        }

        _vidClock += dt;
        _vid.Update(VideoVolume);
        if (_vidLog && _vidClock - _vidLastLog >= 1f) { _vidLastLog = _vidClock; Console.WriteLine($"[VIDEO] t={_vidClock:F2} pos={_vid.PositionSeconds:F2} visible={_vid.Visible}"); }
        if (_vidDevSkipAt >= 0f && _vidClock >= _vidDevSkipAt) { VideoFinish(); return false; }
        if (_vid.Ended) { VideoFinish(); return false; } // bitmap.onEndReached -> finishVideo(0.5)

        // PlayState.handleCutsceneKeys: VideoCutscene.isPlaying() && PAUSE_P -> pause(Cutscene)
        bool pausePressed = Input.IsPressed(Keys.Escape) || Input.IsPressed(Keys.Enter) || Input.IsGamePadPressed(Buttons.Start);
        if (_vidDevPauseAt >= 0f && !_vidDevPauseDone && _vid.PositionSeconds >= _vidDevPauseAt) { _vidDevPauseDone = true; pausePressed = true; }
        if (pausePressed && !_gameOver)
        {
            _vid.Pause(); // openPauseSubState(Cutscene, ..., () -> VideoCutscene.pauseVideo())
            _paused = true;
            Input.GameplayMode = false;
            _pauseSelection = 0; _pauseScrollLerp = 0f; _pauseBgAlpha = 0f; _pauseMetadataAlpha = 0f; _pauseMenuAlpha = 0f; _pauseTimer = 0f;
            _pauseDifficultyMode = false;
            BuildPauseMenuItems();
            PauseOpenedV();
            UpdatePauseMenuV(dt, true);
            return true;
        }
        return false;
    }

    /// <summary>Pause menu entries while a video plays (PAUSE_MENU_ENTRIES_VIDEO_CUTSCENE). Null when no video.</summary>
    private string[] VideoPauseItems() => _vid == null ? null : new[] { "Resume", "Skip Cutscene", "Restart Cutscene", "Exit to Menu" };

    /// <summary>Handles the video pause entries. Returns true when handled.</summary>
    private bool VideoExecutePauseItem(string item)
    {
        if (_vid == null) return false;
        switch (item)
        {
            case "Resume":        // PauseSubState.resume: VideoCutscene.resumeVideo(); close()
                VideoClosePause(); _vid.Resume(); return true;
            case "Skip Cutscene": // VideoCutscene.finishVideo(); close()
                VideoClosePause(); VideoFinish(); return true;
            case "Restart Cutscene": // VideoCutscene.restartVideo(); close()
                VideoClosePause(); _vid.Restart(VideoVolume); _vidClock = 0f; return true;
            case "Exit to Menu":
                VideoDestroy(); return false; // fall through to the normal quitToMenu
        }
        return false;
    }

    private void VideoClosePause()
    {
        _paused = false;
        StopPauseMusicV();
        Input.GameplayMode = true;
    }

    /// <summary>VideoCutscene.onCutsceneFinish(cutsceneType).</summary>
    private void VideoOnCutsceneFinish()
    {
        var over = _vidOnFinishOverride; _vidOnFinishOverride = null;
        if (over != null) { over(); return; }
        switch (_vidType)
        {
            case VideoCutsceneType.Starting:
                if (_vidHoldCountdown) { _vidHoldCountdown = false; W6StartCountdown(true); } // PlayState.startCountdown()
                break;
            case VideoCutsceneType.Ending:
                _forceEndSong = true; // PlayState.endSong(true)
                break;
        }
    }


    /// <summary>Week 7 song scripts onCountdownStart (ugh.hxc / guns.hxc / stress.hxc: story mode only;
    /// stress-pico.hxc: every mode except chart editor). Restarts (onSongRetry) do not replay the video.
    /// Called from LoadDialogue (= the moment the countdown would start).</summary>
    private bool VideoStartInit()
    {
        bool retry = s_w6RetryOf != null && string.Equals(s_w6RetryOf, _songName, StringComparison.OrdinalIgnoreCase);
        if (retry) return false;
        string song = (_songName ?? "").ToLowerInvariant();
        bool story = _weekSongs != null || Environment.GetEnvironmentVariable("FNF_VIDEO_STORY") == "1";
        string id = null;
        bool picoMix = song == "stress_pico" || (song == "stress" && (Chart?.PlayerCharacter ?? "").StartsWith("pico", StringComparison.OrdinalIgnoreCase));
        if (picoMix) id = "stressPicoCutscene"; // stress-pico.hxc (variation 'pico'): no story-mode check
        else if (story && (song == "ugh" || song == "guns" || song == "stress")) id = song + "Cutscene";
        if (id == null) return false;
        _vidHoldCountdown = true;
        _dialogueActive = true; // event.cancel(): CANCEL THE COUNTDOWN!
        if (!VideoPlay(id, VideoCutsceneType.Starting))
        {
            _vidHoldCountdown = false; _dialogueActive = false;
            _w6HudHidden = false;
            return false;
        }
        s_w6RetryOf = null;
        return true;
    }

    /// <summary>Video + black screen on camCutscene (above the HUD, below the pause menu).</summary>
    private void VideoDraw(SpriteBatch sb)
    {
        if (!_vidBlackVisible && (_vid == null || !_vid.Visible)) return;
        if (PsychVideoOnExtraCam) return; // drawn in its script camera layer (PlayScene.PsychVideo.cs)
        sb.End();
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
        if (_vidBlackVisible && _vidBlackAlpha > 0f)
            sb.Draw(Assets.Pixel, new Rectangle(0, 0, 1280, 720), new Color(Color.Black, Math.Clamp(_vidBlackAlpha, 0f, 1f)));
        _vid?.Draw(sb, 1280, 720);
        sb.End();
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
    }
}
