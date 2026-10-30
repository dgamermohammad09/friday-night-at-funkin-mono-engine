using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Gameplay;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Game over, ported from funkin/play/GameOverSubState.hx + PlayState.hx (health &lt;= HEALTH_MIN) and the
/// character scripts that customise it (preload/scripts/characters/pico-playable.hxc, pico-dark.hxc,
/// pico-christmas.hxc, pico-holding-nene.hxc, bf-holding-gf.hxc, bf-pixel.hxc, pico-pixel.hxc, songs/2hot.hxc).
///
/// The player character stays in WORLD space at its stage position (the substate adds the same BaseCharacter
/// after resetCharacter()); PlayState is not drawn (persistentDraw = false), an opaque black bg covers the screen,
/// and FlxG.camera follows getMidPointOld(bf) + death.cameraOffsets with lerp DEFAULT_CAMERA_FOLLOW_RATE / 2
/// while the zoom eases to stage.camZoom * death.cameraZoom (smoothLerpPrecision, 0.5 s).
/// </summary>
public partial class PlayScene
{
    // --- camera (FlxG.camera during GameOverSubState) ---
    private float _goCamX, _goCamY, _goTargetX, _goTargetY, _goZoom, _goTargetZoom;
    private bool _goBlueballed, _goFakeout; // GameOverSubState.blueballed / bf.hxc mustNotExit
    private float _goBlazinTimer = -1f; // pico-blazin afterPicoDeathGutPunchIntro
    private bool _goStartedAnim;      // GameOverSubState.hasStartedAnimation
    private bool _goIsEnding;         // isEnding
    private bool _goIsStarting = true;// isStarting (first loop of the death music may be a "Start" variant)
    private bool _goMusicOn;          // gameOverMusic != null && playing (tracked by us)
    private bool _goMusicLooped;
    private float _goMusicT;
    private bool _goPlayedQuote;      // hasPlayedDeathQuote
    private SoundEffectInstance _goQuoteSound;
    private bool _goQuoteFadePending;
    private float _goQuoteTimer = -1f; // pico-holding-nene: FlxTimer 0.58 + 1.5 s before the death quote
    private float _goConfirmTimer = -1f, _goFadeDelay, _goFadeT = -1f;
    private string _goMusicSuffix = "", _goBlueBallSuffix = "";
    private float _goElapsed = -1f;   // seconds since the game over substate opened (dev shots)
    private float _goPreDelay = -1f;  // death.preTransitionDelay countdown
    private bool _goFirstDeathFrameHandled;

    // --- pico-playable / pico-dark / pico-christmas death extras ---
    private AnimatedSprite _goRetry, _goNeneKnife;
    private Vector2 _goRetryPos, _goNenePos;
    private bool _goRetryVisible, _goNeneVisible;
    // --- pico-playable explosion death (2hot can) ---
    private AnimatedSprite _goExplosion;
    private Vector2 _goExplosionPos;
    private float _goExplodeTimer = -1f;
    private SoundEffectInstance _goSinged;

    /// <summary>Seconds since the game over screen opened, -1 when not dead (dev screenshot hook).</summary>
    public float DevDeathElapsed => _gameOver ? _goElapsed : -1f;

    private string GoPlayerId => (_boyfriend?.OriginalName ?? _boyfriend?.Name ?? "bf").ToLowerInvariant();

    private static float? _devForceDeathAt = ParseEnvF("FNF_FORCE_DEATH_AT");
    private static float? _devForceCanMissAt = ParseEnvF("FNF_FORCE_CANMISS_AT");
    private static float? _devForceExplodeAt = ParseEnvF("FNF_FORCE_EXPLODE_AT"); // = 2hot.hxc takeCanDamage() killing Pico
    private static readonly float? _devDeathConfirmAt = ParseEnvF("FNF_DEATH_CONFIRM_AT"); // seconds after the game over opened
    private static float? ParseEnvF(string name)
    {
        var s = Environment.GetEnvironmentVariable(name);
        return float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : null;
    }

    /// <summary>Dev hook: FNF_FORCE_DEATH_AT=&lt;song s&gt; sets health to 0; FNF_FORCE_CANMISS_AT=&lt;song s&gt; turns botplay off
    /// and leaves 0.05 health so the next 2hot firegun note is missed (can explosion death).</summary>
    private void DevForceDeathTick()
    {
        if (_gameOver || _conductor == null) return;
        if (_devForceDeathAt is float t && _conductor.SongPosition >= t) { _devForceDeathAt = null; _health = 0f; }
        if (_devForceExplodeAt is float e && _conductor.SongPosition >= e) { _devForceExplodeAt = null; _2hotPicoExplodeDeath = true; _health = 0f; }
        if (_devForceCanMissAt is float c && _conductor.SongPosition >= c) { _devForceCanMissAt = null; _botplay = false; _health = 0.05f; }
    }

    /// <summary>PlayState.update: health &lt;= HEALTH_MIN -> pause audio, deathCounter++, GAME_OVER event, moveToGameOver().</summary>
    private void StartGameOverV()
    {
        _gameOver = true;
        Input.GameplayMode = false; // GameOverSubState: controls.ACCEPT/BACK -> pad A/B must work (face buttons are notes only in gameplay)
        CallOnLuas("onGameOver"); if (_vsPlayState != null) VsDispatch("onGameOver", new VEvent("GAME_OVER")); // V-Slice scripts onGameOver (e.g. QtStageKiller resets its colour shader)
        _gameOverTimer = 0f;
        _gameOverMusicStarted = false;
        _gameOverConfirmed = false;
        _gameOverFadeTimer = 0f;
        _deathCounter++;
        Audio.StopMusic();
        Audio.StopVoices();
        _goPreDelay = _boyfriend?.JsonData?.Death?.PreTransitionDelay ?? 0f;
        if (_goPreDelay <= 0f) OpenGameOverSubState();
    }

    /// <summary>GameOverSubState.new/create + setCameraTarget.</summary>
    private void OpenGameOverSubState()
    {
        _goPreDelay = -1f;
        _goElapsed = 0f;
        _goStartedAnim = false; _goIsEnding = false; _goIsStarting = true; _goMusicOn = false;
        _goPlayedQuote = false; _goQuoteFadePending = false; _goQuoteTimer = -1f;
        _goConfirmTimer = -1f; _goFadeT = -1f; _goFirstDeathFrameHandled = false;
        _goRetry = _goNeneKnife = _goExplosion = null; _goRetryVisible = _goNeneVisible = false; _goExplodeTimer = -1f;

        // Character-script game over suffixes (onCreate of each playable character script; 2hot.hxc takeCanDamage).
        string id = GoPlayerId;
        _goMusicSuffix = _goBlueBallSuffix = "";
        if (id.StartsWith("bf-pixel")) _goMusicSuffix = _goBlueBallSuffix = "-pixel";
        else if (id.StartsWith("pico-pixel")) _goMusicSuffix = _goBlueBallSuffix = "-pixel-pico";
        else if (id == "pico-holding-nene") { _goMusicSuffix = "-pico"; _goBlueBallSuffix = "-pico-and-nene"; }
        else if (id == "pico-blazin") { _goMusicSuffix = "-pico"; _goBlueBallSuffix = "-pico-gutpunch"; }
        else if (id.StartsWith("pico")) _goMusicSuffix = _goBlueBallSuffix = "-pico";
        if (_2hotPicoExplodeDeath) _goMusicSuffix = _goBlueBallSuffix = "-pico-explode";
        if (QsSawDeath) _goMusicSuffix = _goBlueBallSuffix = "-sawblade"; // QT Rewired SawbladeAndDodgeModule.executeInstantKill

        if (_boyfriend != null)
        {
            if (Gameplay.Character.FreeOthersAtGameOver && _boyfriend.HasDeferredDeathAtlases)
            {
                long freed = (_opponent != null && _opponent != _boyfriend ? _opponent.ReleaseCompositeTextures() : 0) + (_girlfriend != null && _girlfriend != _boyfriend ? _girlfriend.ReleaseCompositeTextures() : 0);
                Console.WriteLine($"[MEM] game over: freed {freed / 1048576} MB of opponent/GF composites");
            }
            _boyfriend.LoadDeferredDeathAtlases(); // UWP: death-only atlases are loaded now (Character.Memory.cs)
            _boyfriend.IsDead = true;
            // Death anims come from the character JSON (merged per-animation atlases). Only legacy characters without
            // a JSON "firstDeath" fall back to the old dead_sprites sheets.
            bool jsonDeath = _boyfriend.JsonData?.Animations?.Any(a => a.Name == "firstDeath") == true;
            if (!jsonDeath) _boyfriend.LoadDeathSprites(Game);
            // resetCharacter(): back to the original position, dance(true) (idle), updateHitbox.
            _boyfriend.PlayAnimation(_boyfriend.JsonData?.StartingAnimation ?? "idle", true);
        }

        // FlxG.camera keeps its current scroll/zoom; target = getMidPointOld(bf) + getDeathCameraOffsets().
        _goCamX = _camWorldX; _goCamY = _camWorldY;
        _goZoom = _defaultCamZoom * _cameraBopMultiplier;
        if (_boyfriend != null)
        {
            var mid = _boyfriend.GetMidpoint();
            var off = _boyfriend.JsonData?.Death?.CameraOffsets;
            _goTargetX = mid.X + (off?.Length >= 2 ? off[0] : 0f);
            _goTargetY = mid.Y + (off?.Length >= 2 ? off[1] : 0f);
            if (_2hotPicoExplodeDeath) _goTargetY += 100f; // pico-playable.getDeathCameraOffsets: y + 100 for '-pico-explode'
            if (QsSawDeath && _boyfriend.AnimSuffix == "-saw") { _goTargetX += 71f; _goTargetY += -50f; } // bf-qt.hxc playAnimation('firstDeath-saw')
        }
        else { _goTargetX = _goCamX; _goTargetY = _goCamY; }
        float stageZoom = _stageZoom > 0f ? _stageZoom : _defaultCamZoom;
        _goTargetZoom = stageZoom * (_boyfriend?.JsonData?.Death?.CameraZoom ?? 1f);
        Console.WriteLine($"[GAMEOVER] open player={id} music='{_goMusicSuffix}' blueBall='{_goBlueBallSuffix}' cam=({_goCamX:F1},{_goCamY:F1}) target=({_goTargetX:F1},{_goTargetY:F1}) zoom={_goZoom:F3}->{_goTargetZoom:F3} pos={_boyfriend?.Position}");
    }

    private bool IsPicoPlayableFamily(string id) => id == "pico-playable" || id == "pico-dark" || id == "pico-christmas" || id == "pico" || (id != null && VsPicoPlayableIds.Contains(id)); // + mod chars extending PicoPlayerBaseCharacter (QT pico-qt)

    /// <summary>pico-playable.hxc playAnimation('firstDeath') hook.</summary>
    private void GoPicoFirstDeath(string id)
    {
        if (_2hotPicoExplodeDeath)
        {
            // doExplosionDeath(): resetCameraZoom, cameraFollowPoint.y -= 100, explosion atlas at (x - 1100, y - 1000),
            // pico hidden, 3 s timer -> death music + singed loop.
            _goZoom = _stageZoom > 0f ? _stageZoom : _defaultCamZoom;
            _goTargetY -= 100f;
            var sheet = SpriteSheet.Load(Game, "images/weekend1/characters/picoExplosionDeath", preRenderComposites: true);
            if (sheet != null)
            {
                _goExplosion = new AnimatedSprite { Sheet = sheet, UseTimelineAnchoring = true };
                _goExplosion.PlayAnimation("intro", true, false);
                _goExplosion.OnFinish = () =>
                {
                    if (_goExplosion.CurrentAnimation == "intro") _goExplosion.PlayAnimation("Loop Start", true, true);
                };
                _goExplosionPos = new Vector2(_boyfriend.Position.X - 1100f, _boyfriend.Position.Y - 1000f);
                Console.WriteLine($"[GAMEOVER] explosion anims=[{string.Join(",", sheet.Animations.Keys.Concat(sheet.CompositeAnimations.Keys).Distinct())}]");
            }
            else Console.WriteLine("[GAMEOVER] picoExplosionDeath atlas missing");
            _goExplodeTimer = 3.0f;
            return;
        }
        // createDeathSprites()
        var retry = SpriteSheet.Load(Game, "images/characters/Pico_Death_Retry");
        if (retry != null) _goRetry = new AnimatedSprite { Sheet = retry };
        bool xmas = id == "pico-christmas";
        var nene = SpriteSheet.Load(Game, xmas ? "images/characters/neneChristmas/neneChristmasKnife" : "images/characters/NeneKnifeToss");
        if (nene != null && _girlfriend != null)
        {
            _goNeneKnife = new AnimatedSprite { Sheet = nene };
            _goNeneKnife.PlayAnimation(xmas ? "knife toss xmas" : "knife toss", true, false);
            _goNeneKnife.OnFinish = () => _goNeneVisible = false;
            _goNeneVisible = true;
            // gf.originalPosition + (120, 0) (pico-christmas: y - 200)
            _goNenePos = new Vector2(_girlfriend.Position.X + 120f, _girlfriend.Position.Y + (xmas ? -200f : 0f));
        }
        Console.WriteLine($"[GAMEOVER] pico death sprites retry={(retry != null ? string.Join(",", retry.Animations.Keys) : "MISSING")} nene={(nene != null ? string.Join(",", nene.Animations.Keys) : "MISSING")}");
    }

    private string GoDeathQuote()
    {
        string id = GoPlayerId;
        string dad = (_opponent?.OriginalName ?? _opponent?.Name ?? "").ToLowerInvariant();
        // Preferences.naughtyness defaults to true and CENSOR_EXPLETIVES is false on desktop -> no censored lines.
        // bf-holding-gf.hxc / pico-holding-nene.hxc: always; bf.hxc / pico-playable.hxc: only against tankman.
        if (id == "bf-holding-gf") return "week7/sounds/jeffGameover/jeffGameover-" + _random.Next(1, 26);
        if (id == "pico-holding-nene") return "week7/sounds/jeffGameover-pico/jeffGameover-" + _random.Next(1, 11);
        if (dad == "tankman")
        {
            if (id == "bf") return "week7/sounds/jeffGameover/jeffGameover-" + _random.Next(1, 26);
            if (id == "pico-playable") return "week7/sounds/jeffGameover-pico/jeffGameover-" + _random.Next(1, 11);
        }
        return null;
    }

    /// <summary>GameOverSubState.resolveMusicPath: strip suffix segments until a file exists.</summary>
    private string GoResolveMusic(bool starting, bool ending)
    {
        string basePath = "music/gameplay/gameover/gameOver" + (ending ? "End" : starting ? "Start" : "");
        string suffix = _goMusicSuffix;
        while (true)
        {
            foreach (var p in new[] { basePath + suffix, basePath.Replace("music/", "music/weekend1/") + suffix })
                if (GoAudioExists(p)) return p;
            if (suffix.Length == 0) return null;
            var parts = suffix.Split('-'); suffix = string.Join("-", parts.Take(parts.Length - 1));
        }
    }

    private static bool GoAudioExists(string p)
    {
        string root = System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot);
        return global::FNF_MonoGame.Engine.FsCI.FileExists(System.IO.Path.Combine(root, p + ".ogg")) || FNF_MonoGame.Engine.VSliceMods.Resolve(p + ".ogg") != null;   // V-Slice mod music (QT '-sawblade')
    }

    /// <summary>GameOverSubState.startDeathMusic(startingVolume, force).</summary>
    private void GoStartDeathMusic(float volume, bool force)
    {
        string path = GoResolveMusic(_goIsStarting, _goIsEnding);
        if (_goIsStarting && path == null) { _goIsStarting = false; path = GoResolveMusic(false, _goIsEnding); }
        if (path == null) { Console.WriteLine("[GAMEOVER] no game over music"); return; }
        if (_goMusicOn && Audio.MusicPlaying && !force) return; // "Music already playing!"
        _goMusicLooped = !(_goIsEnding || _goIsStarting);
        Audio.PlayMusic(path, _goMusicLooped);
        if (Audio.Music != null) Audio.Music.Volume = Math.Clamp(volume, 0f, 1f) * Audio.MusicVolume;
        _goMusicOn = true; _goMusicT = 0f;
        Console.WriteLine($"[GAMEOVER] music {path} vol={volume} looped={_goMusicLooped}");
    }

    private void GoPlayBlueBalledSfx()
    {
        string p = "sounds/gameplay/gameover/fnf_loss_sfx" + _goBlueBallSuffix;
        if (_goBlueBallSuffix == "-pico-explode" || _goBlueBallSuffix == "-pico-gutpunch") p = "sounds/weekend1/gameplay/gameover/fnf_loss_sfx" + _goBlueBallSuffix;
        if (Audio.PlaySound(p) == null) Audio.PlaySound("gameplay/gameover/fnf_loss_sfx");
    }

    private void GoPlayDeathQuote()
    {
        if (_goIsEnding || _boyfriend == null) return;
        string q = GoDeathQuote();
        if (q == null) return;
        _goPlayedQuote = true;
        GoStartDeathMusic(0.2f, false);
        _boyfriend.PlayAnimation("deathLoop", true);
        _goQuoteSound?.Stop();
        _goQuoteSound = Audio.CreateSoundInstance(q);
        if (_goQuoteSound != null) { _goQuoteSound.Volume = Audio.SfxVolume; _goQuoteSound.Play(); _goQuoteFadePending = true; }
    }

    /// <summary>GameOverSubState.update (plus character-script frame/timer hooks).</summary>
    private void UpdateGameOverV(float dt)
    {
        if (_goPreDelay >= 0f)
        {
            // PlayState keeps updating during the pre-transition delay (persistentUpdate is turned off only in the substate).
            _goPreDelay -= dt;
            _boyfriend?.Update(dt);
            if (_goPreDelay <= 0f) OpenGameOverSubState();
            return;
        }
        _gameOverTimer += dt;
        _goElapsed += dt;
        string id = GoPlayerId;

        if (!_goStartedAnim)
        {
            _goStartedAnim = true;
            _goBlueballed = false; _goFakeout = false;
            bool hasFakeout = _boyfriend?.JsonData?.Animations?.Any(a => a.Name == "fakeoutDeath") == true;
            if (_boyfriend != null && hasFakeout && _random.NextDouble() < 1.0 / 4096.0)   // GameOverSubState: FlxG.random.bool(100/4096)
            {
                _goFakeout = true;                                                      // bf.hxc doFakeoutDeath / mustNotExit
                _boyfriend.PlayAnimation("fakeoutDeath", true);
                Audio.PlaySound("sounds/gameplay/gameover/fakeout_death");
            }
            else
            {
                if (_boyfriend != null)
                {
                    if (IsPicoPlayableFamily(id)) GoPicoFirstDeath(id);
                    if (id == "pico-holding-nene") _goQuoteTimer = 0.58f + 1.5f;
                    if (id == "pico-blazin") _goBlazinTimer = 1.25f; // pico-blazin.hxc: FlxTimer(1.25) -> startDeathMusic(1.0) + deathLoop
                    _boyfriend.PlayAnimation("firstDeath", true);
                }
                GoPlayBlueBalledSfx(); _goBlueballed = true;
            }
        }
        if (_goFakeout && _boyfriend != null && _boyfriend.CurrentAnimation == "fakeoutDeath" && (_boyfriend._sprite?.Finished ?? true))
        {
            _goFakeout = false;                                                         // animation.onFinish.addOnce
            _boyfriend.PlayAnimation("firstDeath", true);
            GoPlayBlueBalledSfx(); _goBlueballed = true;
        }

        // FlxG.camera.zoom = smoothLerpPrecision(zoom, target, elapsed, 0.5) (precision 1/100)
        _goZoom = MathHelper.Lerp(_goTargetZoom, _goZoom, MathF.Pow(0.01f, dt / 0.5f));
        // FlxCamera.updateLerp with followLerp = DEFAULT_CAMERA_FOLLOW_RATE / 2 = 0.02
        float lerp = 1f - MathF.Pow(1f - 0.02f, dt * 60f);
        _goCamX += (_goTargetX - _goCamX) * lerp;
        _goCamY += (_goTargetY - _goCamY) * lerp;

        // Input: ACCEPT needs blueballed && !mustNotExit (GameOverSubState.hx:276-278)
        if (!_goIsEnding && _goBlueballed && !_goFakeout)
        {
            if (Input.CutsceneAdvancePressed || (_devDeathConfirmAt is float dc && _goElapsed >= dc)) GoConfirm();
            else if (Input.CutsceneBackPressed)
            {
                _goIsEnding = true;
                Audio.StopMusic(); _goQuoteSound?.Stop(); _goSinged?.Stop();
                CallOnLuas("onGameOverConfirm", false);
                _deathCounter = 0;
                // GameOverSubState.goBack: StickerSubState (chart/player sticker pack) -> StoryMenuState / FreeplayState.build(sticker)
                if (!StickerTransition.Active)
                {
                    bool toStory = _weekId != null;
                    string pack = StickerTransition.ResolvePackId(Assets, _songName, _chart?.PlayerCharacter);
                    StickerTransition.Start(Game.Scenes, Assets, n => Audio.PlaySound(n), pack,
                        () => toStory ? new StoryModeScene() : new FreeplayScene { ForceSkipIntro = true });
                }
                return;
            }
        }

        // Music state
        if (_goMusicOn)
        {
            _goMusicT += dt;
            if (!_goMusicLooped && _goMusicT > 0.25f && !Audio.MusicPlaying)
            {
                _goMusicOn = false;
                if (_goIsStarting && !_goIsEnding) { _goIsStarting = false; GoStartDeathMusic(1f, true); } // onComplete of the Start variant
            }
        }
        if (_goQuoteFadePending && _goQuoteSound != null && _goQuoteSound.State == SoundState.Stopped)
        {
            _goQuoteFadePending = false;
            if (!_goIsEnding && Audio.Music != null) Audio.FadeIn(4f, 0.2f * Audio.MusicVolume, Audio.MusicVolume);
        }

        if (!_goMusicOn && _boyfriend != null && !_goIsEnding)
        {
            bool firstDone = _boyfriend.CurrentAnimation.StartsWith("firstDeath") && (_boyfriend._sprite?.Finished ?? true);
            if (GoDeathQuote() != null)
            {
                if (firstDone && !_goPlayedQuote) GoPlayDeathQuote();
            }
            else if (firstDone)
            {
                GoStartDeathMusic(1f, false);
                _boyfriend.PlayAnimation("deathLoop", true);
            }
        }

        // pico-playable.onAnimationFrame('firstDeath', 35): retry text + early death music + deathLoop
        if (IsPicoPlayableFamily(id) && !_goFirstDeathFrameHandled && _boyfriend != null
            && _boyfriend.CurrentAnimation == "firstDeath" && _boyfriend.SpriteFrameIndex >= 35)
        {
            _goFirstDeathFrameHandled = true;
            if (_goRetry != null)
            {
                _goRetry.PlayAnimation("Retry Text Loop", true, true);
                _goRetryVisible = true;
                _goRetryPos = new Vector2(_boyfriend.Position.X + 195f, _boyfriend.Position.Y - 70f);
            }
            GoStartDeathMusic(1f, false);
            _boyfriend.PlayAnimation("deathLoop", true);
        }
        if (_goBlazinTimer >= 0f)
        {
            _goBlazinTimer -= dt;
            if (_goBlazinTimer < 0f && !_goIsEnding) { GoStartDeathMusic(1f, false); _boyfriend?.PlayAnimation("deathLoop", true); }
        }
        if (_goQuoteTimer >= 0f)
        {
            _goQuoteTimer -= dt;
            if (_goQuoteTimer < 0f && !_goPlayedQuote) GoPlayDeathQuote();
        }
        if (_goExplodeTimer >= 0f)
        {
            _goExplodeTimer -= dt;
            if (_goExplodeTimer < 0f)
            {
                GoStartDeathMusic(1f, false);
                _goSinged = Audio.CreateSoundInstance("sounds/weekend1/singed_loop");
                if (_goSinged != null) { _goSinged.IsLooped = true; _goSinged.Volume = Audio.SfxVolume; _goSinged.Play(); }
            }
        }

        // Confirm -> FADE_TIMER = music length / 7000 -> camera.fade(BLACK, 2) -> reset
        if (_goConfirmTimer >= 0f)
        {
            _goConfirmTimer += dt;
            if (_goFadeT < 0f && _goConfirmTimer >= _goFadeDelay) _goFadeT = 0f;
            if (_goFadeT >= 0f)
            {
                _goFadeT += dt;
                _gameOverFadeTimer = _goFadeT;
                if (_goFadeT >= 2f)
                {
                    _goSinged?.Stop();
                    PlayStateParams p = BuildParams();
                    if (_weekSongs == null) p.WeekSongs = null;
                    Game.Scenes.ChangeScene(new PlayScene(p));
                    return;
                }
            }
        }

        // pico-holding-nene.hxc onAnimationComplete('deathConfirm') -> 'deathConfirm-loop'
        if (id == "pico-holding-nene" && _boyfriend != null && _boyfriend.CurrentAnimation == "deathConfirm" && (_boyfriend._sprite?.Finished ?? false))
            _boyfriend.PlayAnimation("deathConfirm-loop", true);
        _boyfriend?.Update(dt);
        _goRetry?.Update(dt);
        _goNeneKnife?.Update(dt);
        _goExplosion?.Update(dt);
    }

    /// <summary>GameOverSubState.confirmDeath.</summary>
    private void GoConfirm()
    {
        _goIsEnding = true;
        _gameOverConfirmed = true;
        _goPlayedQuote = true;
        _goQuoteSound?.Stop(); _goQuoteSound = null; _goQuoteFadePending = false;
        CallOnLuas("onGameOverConfirm", true);
        GoStartDeathMusic(1f, true);
        string id = GoPlayerId;
        if (_boyfriend != null)
        {
            if (_goExplosion != null)
            {
                _goExplosion.PlayAnimation("Confirm", true, false); // doExplosionConfirm
                _goSinged?.Stop(); _goSinged = null;
            }
            else if (IsPicoPlayableFamily(id) && _goRetry != null)
            {
                // pico-playable: retry text plays 'confirm', moved by (-250, -200); pico's anim is NOT changed.
                _goRetry.PlayAnimation("Retry Text Confirm", true, false); // visibility unchanged (pico-playable.hxc:194-201)
                _goRetryPos += new Vector2(-250f, -200f);
            }
            else _boyfriend.PlayAnimation("deathConfirm", true);
        }
        _goFadeDelay = (float)(Audio.MusicLength * 1000.0 / 7000.0);
        if (QsSawDeath) _goFadeDelay = 3f; // bf-qt.hxc sawblade confirm: FlxTimer(3) -> camera.fade(2 s)
        _goConfirmTimer = 0f;
    }

    private Vector2 GoWorldToScreen(float x, float y) => new Vector2(x - (_goCamX - 640f / _goZoom), y - (_goCamY - 360f / _goZoom));

    /// <summary>Draw the game over substate (PlayState itself is not drawn: persistentDraw = false).</summary>
    private void DrawGameOverV(SpriteBatch spriteBatch)
    {
        if (_goPreDelay >= 0f)
        {
            // Still in PlayState during preTransitionDelay: keep drawing the frozen black + character.
        }
        spriteBatch.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
        spriteBatch.Draw(Assets.Pixel, new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT), Color.Black);
        spriteBatch.End();

        _worldZoomMatrix = Matrix.CreateScale(_goZoom, _goZoom, 1f);
        _worldSampler = SamplerState.LinearClamp;
        _worldBlendState = BlendState.NonPremultiplied;
        _worldClipRaster ??= new RasterizerState { ScissorTestEnable = true, CullMode = CullMode.None }; // flixel never culls: mirrored atlas parts (negative-determinant matrices) must draw
        Game.GraphicsDevice.ScissorRectangle = new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT);
        spriteBatch.Begin(samplerState: _worldSampler, blendState: _worldBlendState, transformMatrix: _worldZoomMatrix, rasterizerState: _worldClipRaster);
        if (_boyfriend != null && _goExplosion == null)
        {
            var p = GoWorldToScreen(_boyfriend.Position.X, _boyfriend.Position.Y);
            bool adj = BeginCharAdjust(spriteBatch, 0);
            _boyfriend.Draw(spriteBatch, Assets, p.X, p.Y, _goZoom);
            if (_goRetry != null && _goRetryVisible)
            {
                // deathSpriteRetry.shader = pico's shader (drawn with the same character adjust)
                _goRetry.Position = GoWorldToScreen(_goRetryPos.X, _goRetryPos.Y);
                _goRetry.Draw(spriteBatch);
            }
            EndAdjust(spriteBatch, adj);
        }
        if (_goNeneKnife != null && _goNeneVisible)
        {
            _goNeneKnife.Position = GoWorldToScreen(_goNenePos.X, _goNenePos.Y);
            _goNeneKnife.Draw(spriteBatch);
        }
        if (_goExplosion != null)
        {
            _goExplosion.Position = GoWorldToScreen(_goExplosionPos.X, _goExplosionPos.Y);
            _goExplosion.Draw(spriteBatch);
        }
        spriteBatch.End();

        if (_goFadeT >= 0f)
        {
            spriteBatch.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
            spriteBatch.Draw(Assets.Pixel, new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT), Color.Black * Math.Clamp(_goFadeT / 2f, 0f, 1f));
            spriteBatch.End();
        }
        QsDrawGameOverFlash(spriteBatch); // FlxG.camera.flash from the QT sawblade instakill persists into the game over
    }
}
