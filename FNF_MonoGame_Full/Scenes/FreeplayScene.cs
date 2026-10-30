using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

using FontStashSharp;

using FNF_MonoGame.Engine;
using FNF_MonoGame.Engine.PsychEngine;
using FNF_MonoGame.Gameplay;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System.IO;
using System.Linq;



namespace FNF_MonoGame.Scenes;



/// <summary>

/// Modern FNF FreeplayState — DJ BF on left, song capsules on right,

/// difficulty stamps, pixel icons, score display, album art backing.

/// Matches the modern FNF engine freeplay design.

/// </summary>

/// <summary>
/// Parameters passed from ResultsScene back to FreeplayScene to play the post-song rank animation.
/// </summary>
public class FreeplayResultParams
{
    public string SongId;
    public string DifficultyId;
    public ScoringRank? OldRank;
    public ScoringRank NewRank;
    public bool PlayRankAnim = true;
}

public partial class FreeplayScene : Scene

{

    // -- Post-song result animation (from ResultsScene) --
    // Based on FreeplayState.rankAnimStart / rankDisplayNew / rankAnimSlam.

    private readonly FreeplayResultParams _resultParams;

    private bool _rankAnimActive;
    private float _rankAnimTimer; // elapsed time since rankAnimStart
    private int _rankAnimPhase; // 0=start, 1=display new rank, 2=slam, 3=settle

    private Vector2 _rankAnimCapsuleStart;
    private Vector2 _rankAnimCapsuleTarget;
    private Vector2 _rankAnimCapsulePos;
    private float _rankAnimCapsuleAngle;

    private float _rankAnimRankCameraZoom = 1f;
    private float _rankAnimFunnyCameraZoom = 1f;

    private float _rankAnimRankScale = 1f;
    private float _rankAnimRankAlpha = 1f;
    private bool _rankAnimRankVisible;

    private float _rankAnimBgAlpha;

    private ScoringRank? _rankAnimOldRank;
    private ScoringRank _rankAnimNewRank;

    // Interval shake state for the whole selected capsule (used by rankAnimSlam).
    private Vector2 _rankAnimShakeOffset;
    private float _rankAnimShakeTimer;
    private float _rankAnimShakeInterval;
    private Vector2 _rankAnimShakeAmplitude;
    private float _rankAnimShakeEaseT;
    private Func<float, float> _rankAnimShakeEase;
    private float _rankAnimShakeNextTick;

    // Interval shake state for the capsule body only (used by the initial impact).
    private Vector2 _rankAnimBodyShakeOffset;
    private float _rankAnimBodyShakeTimer;
    private float _rankAnimBodyShakeInterval;
    private Vector2 _rankAnimBodyShakeAmplitude;
    private float _rankAnimBodyShakeEaseT;
    private Func<float, float> _rankAnimBodyShakeEase;
    private float _rankAnimBodyShakeNextTick;

    // Which phase of the rank animation is currently running.
    // 0 = rankAnimStart (0.0s - 0.6s), 1 = rankDisplayNew (0.5s - 0.6s),
    // 2 = impact (0.6s - 0.9s), 3 = pull (0.9s - 1.6s),
    // 4 = rankAnimSlam impact (1.6s - 2.6s).

    private const float RANK_ANIM_CENTER_X = 1280f / 2f - (612f * 0.8f) / 2f;
    private const float RANK_ANIM_CENTER_Y = 720f / 2f - (132f * 0.8f) / 2f;



    // -- Song data --

    private List<FreeplaySong> _allSongs = new(); // master list (unfiltered)

    private List<FreeplaySong> _songs = new(); // active/filtered list used for display

    private List<string> _modContentRoots = new(); // enabled mod roots registered for this scene

    private int _selectedIndex;

    private float _scrollLerp;

    private int _difficultyIndex = 1; // 0=easy, 1=normal, 2=hard, 3=erect, 4=nightmare

    private static readonly string[] Difficulties = { "easy", "normal", "hard", "erect", "nightmare" };



    // -- State --

    private enum Phase { Intro, Idle, Confirmed }

    private Phase _phase = Phase.Intro;

    private float _introTimer;

    private float _confirmTimer;
    private bool _confirmLoadIssued;
    /// <summary>Dev: seconds since confirm (game time), -1 before. Used by FNF_SHOTS_CONFIRMTIME.</summary>
    public float DevConfirmElapsed { get; private set; } = -1f; private float DevSceneElapsedLocal; private bool _fromCharSelectSkip => false; public static float DevSceneElapsed { get; set; } public static bool DevClockStarted;
    public string DevConfirmInfo => $"ex={_exitTimer:F3} jo={_jumpOutBeat} djw={ExitDjWidth()} djx={_exitDjOffset:F1} card={(int)(_picoConfirmTime * 24f)} dj={_djSprite?.CurrentAnimation}@{_djSprite?.FrameIndex} g={_confirmGlowAlpha:F2} g2={_confirmGlow2Alpha:F2} tg={_confirmTextGlowAlpha:F2} ph={_confirmGlowPhase} yeah={_yeahTime:F2} {DevPicoInfo}{DevBfCardInfo} {DevCsInfo}";
    private int _devConfirmFrames;

    private float _songPreviewDelay;

    private bool _songPreviewPlaying;

    private int _lastPreviewIndex = -1;

    private int _displayedScore;

    private float _lerpScore;

    private float _intendedCompletion;

    private float _lerpCompletion;



    // -- Highscore animation timer (original: plays once every 12-60s) --

    private float _highscoreNextPlay;

    private static readonly Random _rng = new();



    // -- Persist selection across visits --

    private static string _rememberedSongId; // FreeplayState.rememberedSongId (set on leave; null = Random)
    private static int _lastSelectedIndex; private static bool _rememberedInit; private readonly int _randomCapsuleRating = new Random().Next(0, 21);

    private static int _lastDifficultyIndex = 1;



    // -- Visual assets --

    private Texture2D _pinkBack;

    private Texture2D _bgImage;

    private Texture2D _bgImageMasked;

    private Texture2D _bgBlackOverlay;

    private Texture2D _bgAngleMask;

    private Texture2D _diffEasy, _diffNormal, _diffHard, _diffErect, _diffNightmare;

    private SpriteSheet _diffNightmareSheet;

    private List<SpriteFrame> _diffNightmareFrames;

    private Texture2D _clearBox;

    private SpriteSheet _capsuleSheet;

    private List<SpriteFrame> _capsuleSelected;

    private List<SpriteFrame> _capsuleUnselected;

    private SpriteSheet _highscoreSheet;

    private AnimatedSprite _highscoreSprite;



    // -- Difficulty selector arrows --

    private SpriteSheet _selectorSheet;

    private AnimatedSprite _selectorLeft;

    private AnimatedSprite _selectorRight;



    // -- Confirm glow effects --

    private Texture2D _confirmGlow;

    private Texture2D _confirmGlow2;

    private Texture2D _confirmTextGlow;

    private Texture2D _cardGlow;

    private Texture2D _separator; // difficulty dot base

    // backcard-pico.hxc confirm(): FlxTween.color(backingImage, ...) schedule (all FlxEase.expoOut)
    private static readonly (float At, Color From, Color To, float Dur)[] PicoBgShots =
    {
        (0f,        new Color(0xFF, 0xFF, 0xFF), new Color(0x8A, 0x8A, 0x8A), 10f / 24f),
        (10f / 24f, new Color(0x34, 0x30, 0x36), new Color(0x69, 0x63, 0x66), 3f / 24f),
        (14f / 24f, new Color(0x27, 0x29, 0x2D), new Color(0x68, 0x6A, 0x6F), 3f / 24f),
        (18f / 24f, new Color(0x2D, 0x28, 0x2D), new Color(0x67, 0x61, 0x64), 3f / 24f),
        (21f / 24f, new Color(0x29, 0x29, 0x2F), new Color(0x62, 0x62, 0x6B), 3f / 24f),
        (24f / 24f, new Color(0x29, 0x23, 0x2C), new Color(0x80, 0x80, 0x80), 3f / 24f),
    };
    private float _picoBgClock = -1f;
    private PicoConfirmCard _picoCard;
    private PicoConfirmCard _yeahCard;   // BackingCard.backingTextYeah (flixel-animate replay)
    private float _yeahTime = -1f;
    private float _picoConfirmTime;
    private float _confirmGlowAlpha;

    private float _confirmGlow2Alpha;

    private float _confirmTextGlowAlpha;

    private float _cardGlowAlpha;

    private float _cardGlowScale = 1f;

    private float _cardGlowDuration = 0.45f; // original: introDone=0.45s, disappear=0.25s

    private int _confirmGlowPhase; // 0=inactive, 1=glow2 fading in, 2=glow flash+textGlow

    private float _confirmGlowTimer;



    // -- Pink color tween on confirm (original: 0xFFD0D5 ? 0x171831 over 0.33s) --

    private Color _pinkColorFrom;

    private Color _pinkColorTo;

    private float _pinkTweenTimer;

    private float _pinkTweenDuration;

    private bool _pinkTweening;



    // -- BG image dimming on confirm --

    private float _bgDimTimer;

    private float _bgDimDuration;

    private Color _bgDimFrom;

    private Color _bgDimTo;

    private bool _bgDimming;

    private Color _bgDimColor = Color.White;

    private int _bgDimPhase; // 0=none, 1=first dim, 2=second dim



    // -- Confirm screen fade (original: funnyCam.fade(BLACK, 0.2s) after startDelay) --

    private bool _confirmFading;

    private float _confirmFadeAlpha;



    // -- Backing text yeah (animated atlas on pink panel) --

    private SpriteSheet _backingTextSheet;

    private AnimatedSprite _backingTextSprite;



    // -- Dot pulse animation --

    private SpriteSheet _dotPulseSheet;

    private List<SpriteFrame> _dotPulseFrames;



    // -- Intro/phase state --

    private bool _introDone; // true after DJ intro finishes

    private float _introRevealTimer;

    private bool _introUiRevealed;

    private Color _pinkColor = new(0xFF, 0xD4, 0xE9); // starts pink, becomes gold

    private float _bgRevealTint; // 0=black, 1=white for BG image color fade

    private bool _orangeBarsVisible; // orange bars hidden until introDone

    private bool _dotsVisible; // dots fade in after introDone



    // -- CharSelectHint alpha pulse (original: hintTimer += elapsed*2, alpha lerp 0.3..0.9) --

    private float _hintAlphaTimer;



    // -- DJ character --

    private SpriteSheet _djSheet;

    private AnimatedSprite _djSprite;

    private string _djIdleAnim;

    private string _djIntroAnim;

    private string _djConfirmAnim;

    private string _djEasterEggAnim;
    private static readonly float DevDjAfkSec = float.TryParse(Environment.GetEnvironmentVariable("FNF_DJ_AFKSEC"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var __afk) ? __afk : 60f; // dev: shorter afk delay

    private string _djTvAnim;

    private string _djFistPumpAnim;

    private string _djLossAnim;

    private string _djCharSelectAnim;

    private string _djNewUnlockAnim;

    private Dictionary<string, Vector2> _djOffsets;

    // -- TV sound effects (matches original BoyfriendFreeplayDJ.hx) --

    private SoundEffectInstance _tvCartoonSound;

    private string[] _tvCartoonFiles;

    private int _tvLastFrame = -1;

    private bool _tvFirstRun = true;

    private bool _tvMusicFading;

    private float _tvMusicFadeTimer;

    private float _tvMusicFadeDuration;

    private float _tvMusicFadeFrom;

    private float _tvMusicFadeTo;

    private float _tvMusicRestoreVolume = 0.8f;



    // -- Pixel icon cache --

    private Dictionary<string, SpriteSheet> _pixelIconSheets = new(StringComparer.OrdinalIgnoreCase);



    // -- Layout constants (1280x720 native, matching original FNF) --

    private const float CAPSULE_SCALE = 0.8f;

    private const float CAPSULE_FRAME_W = 612f;

    private const float CAPSULE_FRAME_H = 132f;

    private const float CAPSULE_SPACING = CAPSULE_FRAME_H * CAPSULE_SCALE + 10f;

    private const float SONGS_X_OFFSET = 0f;

    private const float DJ_POS_MULTI = 0.44f;

    private const float SONGS_POS_MULTI = 0.75f;

    private const float DJ_SCALE = 1f;

    private const float PINK_PANEL_W = 524f;  // pinkBack.png native width — visible on 16:9

    private const float BG_IMAGE_X = PINK_PANEL_W * 0.74f; // backingImage x offset in original

    // Original AngleMask shader: endPosition.x=90, BG image scaled to screen height.

    private const float ANGLE_MASK_BASE_X = 451f;

    private const float ANGLE_MASK_SLOPE = 0.174f;

    private const float ANGLE_MASK_MID_Y = 360f;

    private const float DIFF_X = 90f;

    private const float DIFF_Y = 80f;



    // In this MonoGame port, all Freeplay UI is already authored in the fixed 1280x720 layout space.

    // Keep cutout offset at 0 so we don't double-apply widescreen shift values from HaxeFlixel.

    private float CutoutWidth => 0f;

    private float DiffBaseX => CutoutWidth * DJ_POS_MULTI;

    private float DjBaseX => (CutoutWidth * DJ_POS_MULTI) + 640f;

    private float SongsBaseX => CutoutWidth * SONGS_POS_MULTI;



    // -- Intro animation state --

    private float _pinkSlideX;

    private float _capsuleSlideX;

    private float _overhangY; // overhang slides in from top

    private float _hintSlideY; // charSelectHint slides in from bottom

    private float _hintSlideTweenTimer;

    private float _overhangTweenTimer;

    private float _diffSlideX; // difficulty sprite slides in from left

    private float _diffIntroTweenTimer;

    private bool _capsuleFlicker; // text flickers on confirm

    private float _flickerTimer;

    private float _capsuleBounceTimer; // staggered bounce-in elapsed time

    private const float CAPSULE_BOUNCE_DURATION = 0.6f;



    // -- Score digit atlas --

    private SpriteSheet _digitSheet;

    private static readonly string[] DigitNames = { "ZERO", "ONE", "TWO", "THREE", "FOUR", "FIVE", "SIX", "SEVEN", "EIGHT", "NINE" };



    // -- Clear text font atlas (freeplay-clear) --

    private SpriteSheet _clearFontSheet;



    // -- Capsule detail assets --

    private SpriteSheet _bigNumbersSheet;

    private SpriteSheet _smallNumbersSheet;

    private SpriteSheet _weekTypesSheet;

    private SpriteSheet _newTextSheet;

    private List<SpriteFrame> _newTextFrames;

    private Texture2D _bpmText;

    private Texture2D _difficultyText;



    // -- Letter sort --

    private Texture2D _miniArrow;

    private SpriteSheet _sortedLettersSheet;

    private AnimatedSprite _sortedLettersSprite;

    private int _letterSortIndex = 2; // 0=fav, 1=#, 2=ALL, 3+=A-Z groups

    private static readonly string[] LetterSortLabels = { "#", "fav", "ALL", "A-B", "C-D", "E-H", "I-L", "M-N", "O-R", "S", "T", "U-Z" };



    // LetterSort "wiggle" animation (FNF_Official: doLetterChangeAnims)

    private float _letterWiggleTimer;

    private int _letterWiggleDir;



    // -- Album roll --

    private SpriteSheet _albumRollSheet;

    private AnimatedSprite _albumRollSprite;

    private Texture2D _albumArtTexture;

    private float _albumArtScaleX = 1f;

    private float _albumArtScaleY = 1f;

    private float _albumFlipTimer;

    private Texture2D _albumFlipPendingTexture;

    private bool _albumFlipPending;

    private SpriteSheet _albumTitleSheet; // volume1-text etc.

    private AnimatedSprite _albumTitleSprite;

    private SpriteSheet _freeplayStarsSheet;

    private AnimatedSprite _freeplayStarsSprite;

    private string _currentAlbumId;

    private bool _albumVisible;

    private bool _albumIntroPlayed;

    private int _albumDifficultyRating;

    private float _albumRevealTimer;

    private bool _albumTitleVisible;

    private bool _albumStarsVisible;

    private const float ALBUM_REVEAL_DELAY = 0.75f;



    // -- DJ afk timer --

    private float _djAfkTimer;          // time idling (resets on input / after Easter Egg / after TV)

    private bool _djSawEasterEgg;       // true after idleEasterEgg plays once

    private bool _djEasterEggPlaying;   // true while idleEasterEgg is playing

    private bool _djTvPlaying;          // true while TV/cartoon is playing



    // -- Character select transition --

    private bool _switchingToCharSelect; // true while playing the to-CS animation before changing scene



    // -- Debug animation menu --

    private bool _debugMenuVisible;

    private KeyboardState _prevDebugKb;

    private MouseState _prevMouse;

    private readonly List<DebugMenuItem> _debugMenuItems = new();

    private struct DebugMenuItem

    {

        public string Label;

        public Action Action;

        public Rectangle Bounds;

    }



    // -- Difficulty arrow push (original: scale 0.5, offset.y-=5, reset after 2/24s) --

    private float _leftArrowPush;

    private float _rightArrowPush;

    private const float ARROW_PUSH_DURATION = 2f / 24f; // ~0.083s



    // -- Song preview fade-in/out (FNF_Official: FADE_IN_DURATION=0.5, FADE_OUT_DURATION=0.25, delay=0.25,

    // start volume=0.25, end volume=1.0, fade-out end volume=0.0) --

    private float _previewFadeTimer;

    private bool _previewFadingIn;

    private bool _previewFadingOut;

    private float _previewFadeOutTimer;

    private string _pendingPreviewSong;

    private string _activePreviewSong;

    private int _activePreviewStartMs;

    private int _activePreviewEndMs;

    private const float PREVIEW_FADE_IN_DURATION = 2f;   // playCurSongPreview: FlxG.sound.music.fadeIn(2, 0, previewVolume)

    private const float PREVIEW_FADE_OUT_DURATION = 0.25f;

    private const float PREVIEW_START_VOLUME = 0f;

    private const float PREVIEW_TARGET_VOLUME = 0.7f;   // previewVolume = 0.7 * dj.getMusicPreviewMult() (1)

    private const float PREVIEW_FADE_OUT_END_VOLUME = 0.0f;

    private const float PREVIEW_DELAY = 0.25f;



    // -- Hold/spam navigation (original: first press immediate, 0.9s hold then 0.07s repeat) --

    private float _spamTimer;

    private bool _spamming;



    // -- Difficulty slide transition (old slides out, new slides in) --

    private bool _diffTransitioning;

    private float _diffTransTimer;

    private float _diffOldX;

    private float _diffNewX;

    private float _diffOldTarget;

    private float _diffNewTarget;

    private int _diffOldIndex;

    private Texture2D _diffOldTex;

    private const float DIFF_TRANS_DURATION = 0.2f;



    // -- Difficulty alpha flash (original: arrow moveShitDown whiteShader 1/24s) --

    private float _diffAlphaFlashTimer;



    // -- Difficulty y-offset bounce (original: offset.y -= 5, resets after 1/24s) --

    private float _diffYBounceTimer;



    // -- Exit animation state (per-element exitMovers matching original goBack) --

    private bool _exiting;

    private float _exitTimer;

    private const float EXIT_DURATION = 0.5f; // longest element tween = DJ at 0.5s

    private float _exitDjOffset;       // DJ slides left

    private float _exitPinkOffset;     // pink panel slides left

    private float _exitBgOffset;       // BG/overlay slides right

    private float _exitDiffXOffset;    // difficulty + dots slide left (original: x:-300, speed:0.25)

    private float _exitArrowOffset;    // arrows slide left

    private float _exitScoreOffset;    // score area slides right

    private float _exitLetterYOffset;  // letter sort slides up

    private float _exitCapsuleOffset;  // capsules jump-out right

    private float _exitOverhangYOffset; // overhang slides up (original: y:-164, speed:0.2)

    private float _exitAlbumOffset;    // album roll slides right (original: x:width, speed:0.4)



    // -- Music fade-in after exit (original: fadeIn(4.0, 0.0, 1.0)) --

    private float _menuFadeTimer;

    private bool _menuFading;



    // -- Favorites (persisted via HighscoreManager) --

    private SpriteSheet _favHeartSheet;

    private List<SpriteFrame> _favHeartFrames;



    // -- Background layers (darkback, yellow bg piece) --

    private Texture2D _darkBack;

    private Texture2D _yellowBgPiece;



    // -- Pico freeplay backing card layers --

    private bool _isPicoTheme;

    private Texture2D _picoBlueBar;

    private Texture2D _picoMiddleLoop;

    private Texture2D _picoLowerLoop;

    private Texture2D _picoGlow;

    private SpriteSheet _picoTopLoopSheet;

    private float _picoScrollBackX;

    private float _picoScrollLowerX;

    private float _picoScrollTopX;

    private float _picoScrollMiddleX;

    private float _picoGlowAlpha;

    private float _picoGlowDarkAlpha;

    private Conductor _picoGlowConductor;

    private int _picoGlowLastBeat = -1;

    private float _picoGlowTweenTimer;

    private bool _picoGlowPulsing;

    private string _picoTopLoopAnimName = "base";

    private float _picoTopLoopAnimTime;

    private int _picoTopLoopFrameIndex;

    private int _picoTopLoopFrameWidth;



    // -- Pico confirm atlas (flash/color boxes on confirm) --

    private SpriteSheet _picoConfirmSheet;

    private AnimatedSprite _picoConfirmSprite;

    private bool _picoConfirmVisible;



    private static readonly BlendState LightenBlend = new BlendState

    {

        ColorBlendFunction = BlendFunction.Max,

        ColorSourceBlend = Blend.One,

        ColorDestinationBlend = Blend.One,

        AlphaBlendFunction = BlendFunction.Max,

        AlphaSourceBlend = Blend.One,

        AlphaDestinationBlend = Blend.One

    };



    private static readonly BlendState MultiplyBlend = new BlendState

    {

        ColorBlendFunction = BlendFunction.Add,

        ColorSourceBlend = Blend.DestinationColor,

        ColorDestinationBlend = Blend.InverseSourceAlpha,

        AlphaBlendFunction = BlendFunction.Add,

        AlphaSourceBlend = Blend.One,

        AlphaDestinationBlend = Blend.InverseSourceAlpha

    };



    private static readonly string[] PicoTopLoopAnims =

    {

        "rocket launcher info",

        "rifle info",

        "sniper info",

        "uzi info",

        "base"

    };



    private static readonly string[] CanonicalPicoSongs =

    {

        "bopeebo", "fresh", "dadbattle", "spookeez", "south",

        "pico", "philly-nice", "blammed", "cocoa", "eggnog",

        "senpai", "roses", "ugh", "guns", "stress"

    };



    // -- BF idle scrolling text (BGScrollingText equivalent) --

    private struct ScrollingTextRow

    {

        public string Text;

        public float Y;

        public float Speed; // pixels per second (negative = left, positive = right)

        public Color Color;

        public int FontSize; public bool Bold; // BGScrollingText bold flag

        public float[] Positions; // X positions of each repeated copy

        public float TextWidth;   // measured width of one text copy

    }

    private ScrollingTextRow[] _scrollingTextRows;

    private bool _scrollingTextVisible;



    // -- Rank badges --

    private SpriteSheet _rankBadgesSheet;



    public FreeplayScene()
    {
    }

    public FreeplayScene(FreeplayResultParams resultParams)
    {
        _resultParams = resultParams;
    }

    public override void Load()

    { FNF_MonoGame.Engine.InputManager.DevSceneClock = () => DevSceneElapsed; if (ParentMenu == null) EnsureParentMenu();   // FreeplayState.build(): MainMenuState parent

        _isPicoTheme = GetSelectedCharacterVariationId().Equals("pico", StringComparison.OrdinalIgnoreCase);



        LoadSongList();



        _selectedIndex = Math.Clamp(_lastSelectedIndex, 0, Math.Max(0, _songs.Count - 1));
        // FreeplayState.rememberedSongId starts as 'tutorial' (rememberSelection(); -1 -> 0 = Random)

        _difficultyIndex = Math.Clamp(_lastDifficultyIndex, 0, Difficulties.Length - 1);

        // If returning from a song result, restore the song/difficulty that was just played.
        if (_resultParams != null)
        {
            int resultIdx = _songs.FindIndex(s => s.Name.Equals(_resultParams.SongId, StringComparison.OrdinalIgnoreCase));
            if (resultIdx >= 0) _selectedIndex = resultIdx;
            int diffIdx = System.Array.FindIndex(Difficulties, d => d.Equals(_resultParams.DifficultyId, StringComparison.OrdinalIgnoreCase));
            if (diffIdx >= 0) _difficultyIndex = diffIdx;
        }

        _scrollLerp = _selectedIndex;



        // Add enabled mod roots so mod freeplay assets (backgrounds, icons, etc.) are resolved
        // Use the Psych Engine mod list (mirrors enabled_mods.txt) so the port is the source of truth.
        foreach (var mod in PsychMods.GetAllMods().Where(m => m.Enabled))
        {
            string modPath = PsychMods.GetModDirectory(mod.Name);
            if (string.IsNullOrEmpty(modPath) || !global::FNF_MonoGame.Engine.FsCI.DirectoryExists(modPath)) continue;
            if (_modContentRoots.Contains(modPath, StringComparer.OrdinalIgnoreCase)) continue;
            _modContentRoots.Add(modPath);
            if (!Audio.ExtraContentRoots.Contains(modPath))
                Audio.ExtraContentRoots.Add(modPath);
            Assets.AddExtraRoot(modPath);
        }



        // -- Load textures --

        _pinkBack = Assets.LoadTexture("menus/freeplay/pinkBack.png");

        _bgImage = Assets.LoadTexture(_isPicoTheme

            ? "menus/freeplay/freeplayBGweek1-pico.png"

            : "menus/freeplay/freeplayBGweek1-bf.png");

        _bgImageMasked = CreateMaskedBGImage(Game.GraphicsDevice, _bgImage);

        _bgBlackOverlay = CreateMaskedBlackOverlay(Game.GraphicsDevice, _bgImage);

        _diffEasy = Assets.LoadTexture("menus/freeplay/freeplayeasy.png");

        _diffNormal = Assets.LoadTexture("menus/freeplay/freeplaynormal.png");

        _diffHard = Assets.LoadTexture("menus/freeplay/freeplayhard.png");

        _diffErect = Assets.LoadTexture("menus/freeplay/freeplayerect.png");

        _diffNightmare = Assets.LoadTexture("menus/freeplay/freeplaynightmare.png");

        _diffNightmareSheet = SpriteSheet.Load(Game, "menus/freeplay/freeplaynightmare");

        if (_diffNightmareSheet != null)

            _diffNightmareFrames = _diffNightmareSheet.GetAnimation("idle");

        _clearBox = Assets.LoadTexture("menus/freeplay/clearBox.png");

        _confirmGlow = Assets.LoadTexture("menus/freeplay/confirmGlow.png");

        _confirmGlow2 = Assets.LoadTexture("menus/freeplay/confirmGlow2.png");

        _confirmTextGlow = Assets.LoadTexture("menus/freeplay/glowingText.png");

        _cardGlow = Assets.LoadTexture("menus/freeplay/cardGlow.png");

        _separator = Assets.LoadTexture("menus/freeplay/seperator.png");



        // -- Capsule spritesheet (Sparrow XML) --

        _capsuleSheet = SpriteSheet.Load(Game, _isPicoTheme

            ? "menus/freeplay/freeplayCapsule/capsule/freeplayCapsule_pico"

            : "menus/freeplay/freeplayCapsule/capsule/freeplayCapsule");

        if (_capsuleSheet != null)

        {

            _capsuleSelected = _capsuleSheet.GetAnimation("mp3 capsule w backing");

            _capsuleUnselected = _capsuleSheet.GetAnimation("mp3 capsule w backing NOT SELECTED");

        }



        // -- Highscore label sprite (original: plays once on random 12-50s timer, then 20-60s) --

        _highscoreSheet = SpriteSheet.Load(Game, "menus/freeplay/highscore");

        if (_highscoreSheet != null)

        {

            _highscoreSprite = new AnimatedSprite { Sheet = _highscoreSheet };

            _highscoreSprite.FreezeOnFrame("highscore small instance 1", 0);

            _highscoreNextPlay = _rng.Next(12, 51);

        }



        // -- Difficulty selector arrows (freeplaySelector sparrow atlas) --

        _selectorSheet = SpriteSheet.Load(Game, _isPicoTheme

            ? "menus/freeplay/freeplaySelector/freeplaySelector_pico"

            : "menus/freeplay/freeplaySelector");

        if (_selectorSheet != null)

        {

            _selectorLeft = new AnimatedSprite { Sheet = _selectorSheet };

            _selectorLeft.PlayAnimation("arrow pointer loop", loop: true);

            _selectorRight = new AnimatedSprite { Sheet = _selectorSheet };

            _selectorRight.PlayAnimation("arrow pointer loop", loop: true);

        }



        // -- DJ BF (animateatlas composite) --

        LoadDJ();



        // -- Backing text yeah (animated atlas on pink panel) --

        _backingTextSheet = SpriteSheet.Load(Game, "menus/freeplay/backing-text-yeah");
        {
            string yj = System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot, "menus/freeplay/backing-text-yeah", "Animation.json");
            if (_backingTextSheet?.Texture != null && global::FNF_MonoGame.Engine.FsCI.FileExists(yj)) _yeahCard = new PicoConfirmCard(_backingTextSheet, yj);
        }

        if (_backingTextSheet != null)

        {

            _backingTextSprite = new AnimatedSprite { Sheet = _backingTextSheet, UseTimelineAnchoring = true };

            // Original FNF: backingTextYeah is only animated on confirm (BackingCard.confirm).

            // Idle state shows nothing behind BF.

        }



        // -- DotPulse sparrow atlas for difficulty dot animation --

        _dotPulseSheet = SpriteSheet.Load(Game, "menus/freeplay/dotPulse");

        if (_dotPulseSheet != null)

            _dotPulseFrames = _dotPulseSheet.GetAnimation("pulse");



        // -- Score digit atlas (digital numbers) --

        // Original FreeplayStyle.numbersAsset: digital_numbers (bf) / digital_numbers_pico (pico)
        _digitSheet = SpriteSheet.Load(Game, _isPicoTheme ? "images/digital_numbers_pico" : "images/digital_numbers")
                      ?? SpriteSheet.Load(Game, "images/digital_numbers");



        // -- Clear text font atlas (freeplay-clear) --

        _clearFontSheet = SpriteSheet.Load(Game, "images/fonts/freeplay-clear");



        // -- Capsule detail assets --

        _bigNumbersSheet = SpriteSheet.Load(Game, "menus/freeplay/freeplayCapsule/bignumbers");

        _smallNumbersSheet = SpriteSheet.Load(Game, "menus/freeplay/freeplayCapsule/smallnumbers");

        _weekTypesSheet = SpriteSheet.Load(Game, "menus/freeplay/freeplayCapsule/weektypes");

        _newTextSheet = SpriteSheet.Load(Game, "menus/freeplay/freeplayCapsule/new");

        if (_newTextSheet != null)

            _newTextFrames = _newTextSheet.GetAnimation("NEW notif");

        _bpmText = Assets.LoadTexture("menus/freeplay/freeplayCapsule/bpmtext.png");

        _difficultyText = Assets.LoadTexture("menus/freeplay/freeplayCapsule/difficultytext.png");



        // -- Letter sort (miniArrow + sortedLetters atlas) --

        _miniArrow = Assets.LoadTexture("menus/freeplay/miniArrow.png");

        _sortedLettersSheet = SpriteSheet.Load(Game, "menus/freeplay/sortedLetters",

            preRenderComposites: true);

        if (_sortedLettersSheet != null)

        {

            _sortedLettersSprite = new AnimatedSprite { Sheet = _sortedLettersSheet };

        }



        // -- Album roll (animate atlas) --

        _albumRollSheet = SpriteSheet.Load(Game, "menus/freeplay/albumRoll/freeplayAlbum"); _albumTlSheet = _albumRollSheet;

        if (_albumRollSheet != null)

        {

            _albumRollSprite = new AnimatedSprite { Sheet = _albumRollSheet };

        }

        _albumTitleSheet = SpriteSheet.Load(Game, "menus/freeplay/albumRoll/volume1-text");

        if (_albumTitleSheet != null)

        {

            _albumTitleSprite = new AnimatedSprite { Sheet = _albumTitleSheet };

        }

        _albumArtTexture = Assets.LoadTexture("menus/freeplay/albumRoll/volume1.png");

        _freeplayStarsSheet = SpriteSheet.Load(Game, "menus/freeplay/freeplayStars"); _starsTlSheet = _freeplayStarsSheet;

        if (_freeplayStarsSheet != null)

        {

            _freeplayStarsSprite = new AnimatedSprite { Sheet = _freeplayStarsSheet };

        }



        // -- Fav heart (Sparrow atlas) --

        _favHeartSheet = SpriteSheet.Load(Game, "menus/freeplay/favHeart");

        if (_favHeartSheet != null)

            _favHeartFrames = _favHeartSheet.GetAnimation("fav heart") ?? _favHeartSheet.GetAnimation("heart");



        // -- Background layers --

        _darkBack = Assets.LoadTexture("menus/freeplay/darkback.png");

        _yellowBgPiece = Assets.LoadTexture("menus/freeplay/yellow bg piece.png");



        if (_isPicoTheme)

        {

            _picoBlueBar = Assets.LoadTexture("menus/freeplay/backingCards/pico/blueBar.png");

            _picoMiddleLoop = Assets.LoadTexture("menus/freeplay/backingCards/pico/middleLoop.png");

            _picoLowerLoop = Assets.LoadTexture("menus/freeplay/backingCards/pico/lowerLoop.png");

            _picoGlow = Assets.LoadTexture("menus/freeplay/backingCards/pico/glow.png");

            // Pico glow pulses on every beat of the menu music (original: beatHit() triggers tweens).
            // (PicoCard.beatHit is never called in 0.8.1: no beat-driven glow)

            _picoTopLoopSheet = SpriteSheet.Load(Game, "menus/freeplay/backingCards/pico/topLoop");

            if (_picoTopLoopSheet?.Animations != null)

            {

                if (_picoTopLoopSheet.Animations.TryGetValue("base", out var baseFrames) && baseFrames.Count > 0)

                    _picoTopLoopFrameWidth = baseFrames[0].SourceRect.Width;

                else if (_picoTopLoopSheet.Frames.Count > 0)

                    _picoTopLoopFrameWidth = _picoTopLoopSheet.Frames.Values.First().SourceRect.Width;

            }



            // Pico confirm atlas (flash/color boxes on confirm)

            try { File.Delete(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pico_confirm_debug.log")); } catch { }

            // backcard-pico.hxc: confirmAtlas = FunkinSprite.createTextureAtlas(CUTOUT*0.44 - 120, 55, pico-confirm),
            // replayed part by part like flixel-animate (alpha/blend per element, scaleElement on 7 symbols).
            const string pcPath = "menus/freeplay/backingCards/pico/pico-confirm";
            _picoConfirmSheet = SpriteSheet.Load(Game, pcPath);   // texture + Frames only (no pre-rendered composites)
            string pcJson = System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot, pcPath, "Animation.json");
            if (_picoConfirmSheet?.Texture != null && global::FNF_MonoGame.Engine.FsCI.FileExists(pcJson))
                _picoCard = new PicoConfirmCard(_picoConfirmSheet, pcJson, PicoConfirmCard.PicoScaledSymbols);
            var dumpDir = Environment.GetEnvironmentVariable("FNF_DUMP_PICOCARD");
            if (_picoCard != null && !string.IsNullOrEmpty(dumpDir))
            {
                Directory.CreateDirectory(dumpDir);
                var gd = Game.GraphicsDevice; var rt = new RenderTarget2D(gd, 1280, 720);
                var sbd = new SpriteBatch(gd);
                Console.WriteLine($"[PICOCARD] frames={_picoCard.FrameCount} boundsMin={_picoCard.BoundsMin}");
                foreach (int fr in new[] { 9, 11, 13, 14, 15, 16, 17, 18 })
                {
                    gd.SetRenderTarget(rt); gd.Clear(Color.Magenta);
                    _picoCard.Draw(sbd, fr, new Vector2(CutoutWidth * DJ_POS_MULTI - 120f, 55f));
                    gd.SetRenderTarget(null);
                    using var fs = File.Create(Path.Combine(dumpDir, $"card_{fr:D2}.png")); rt.SaveAsPng(fs, 1280, 720);
                }
                rt.Dispose(); sbd.Dispose();
            }
            _picoConfirmVisible = false;

        }



        // -- BF idle scrolling text init (original: BGScrollingText rows behind DJ) --

        if (!_isPicoTheme)

        {

            InitScrollingText();

        }



        // -- Rank badges --

        _rankBadgesSheet = SpriteSheet.Load(Game, "menus/freeplay/rankbadges");



        // -- Precompute angled mask overlay (runtime-generated) --

        _bgAngleMask?.Dispose();

        _bgAngleMask = CreateFreeplayAngleMask(Game.GraphicsDevice);



        // -- Insert Random capsule at position 0 (original behavior) --

        _allSongs.Insert(0, new FreeplaySong

        {

            Name = null,

            DisplayName = "Random",

            OpponentChar = null,

            WeekId = null,

            Difficulties = new[] { "easy", "normal", "hard", "erect", "nightmare" },

            IsRandom = true

        });



        // -- Set filtered view (default letter filter + selected difficulty) --

        _songs = ApplyDifficultyFilter(_allSongs);
        // Selection after the Random capsule exists (member indices). FreeplayState.rememberedSongId starts as
        // 'tutorial' (rememberSelection: not found -> 0 = Random); returning from results selects the played song.
        if (!_rememberedInit && _resultParams == null)
        {
            _rememberedInit = true;
            int ti = _songs.FindIndex(s => !s.IsRandom && string.Equals(s.Name, Environment.GetEnvironmentVariable("FNF_FREEPLAY_SONG") ?? "tutorial", StringComparison.OrdinalIgnoreCase));
            _selectedIndex = ti >= 0 ? ti : 0;
        }
        else if (_resultParams == null)
        {   // rememberSelection(): back from a song (pause > exit, game over > exit) -> the song played last
            int ri = _rememberedSongId == null ? -1 : _songs.FindIndex(s => !s.IsRandom && string.Equals(s.Name, _rememberedSongId, StringComparison.OrdinalIgnoreCase));
            _selectedIndex = ri >= 0 ? ri : 0;
        }
        if (_resultParams != null)
        {
            int ri = _songs.FindIndex(s => !s.IsRandom && s.Name != null && s.Name.Equals(_resultParams.SongId, StringComparison.OrdinalIgnoreCase));
            if (ri >= 0) _selectedIndex = ri;
            _rememberedInit = true;
        }
        _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, _songs.Count - 1));
        _scrollLerp = _selectedIndex;



        // -- Preload pixel icons for visible songs --

        foreach (var song in _allSongs)

        {

            if (!song.IsRandom)

                EnsurePixelIcon(song.OpponentChar);

        }



        // -- Initialize state --

        _phase = Phase.Intro;

        _introTimer = 0;

        _pinkSlideX = -PINK_PANEL_W; // starts offscreen left, slides to 0

        _capsuleSlideX = FNFGame.SCREEN_WIDTH;

        _overhangY = -164f; // starts offscreen above

        _hintSlideY = 100f; // starts 100px below final position

        _hintSlideTweenTimer = 0f;

        _overhangTweenTimer = 0f;

        _diffSlideX = -300f; // starts offscreen left

        _diffIntroTweenTimer = 0f;

        _capsuleFlicker = false;

        _flickerTimer = 0f;

        _capsuleBounceTimer = 0f;

        _songPreviewDelay = 0;

        _songPreviewPlaying = false;

        _lastPreviewIndex = -1;

        _activePreviewSong = null;

        _activePreviewStartMs = 0;

        _activePreviewEndMs = 0;

        _introDone = false;

        _pinkColor = new Color(0xFF, 0xD4, 0xE9); // starts pink

        _bgRevealTint = 0f; // BG starts black (hidden)

        _orangeBarsVisible = false;

        _confirmGlowAlpha = 0f;

        _confirmGlow2Alpha = 0f;

        _confirmTextGlowAlpha = 0f;

        _confirmGlowPhase = 0;

        _confirmGlowTimer = 0f;

        _cardGlowAlpha = 0f;

        _cardGlowScale = 1f;

        _pinkTweening = false;

        _bgDimming = false;

        _bgDimPhase = 0;

        _bgDimColor = Color.White;
        _picoBgClock = -1f;
        _yeahTime = -1f;

        _dotsVisible = false;

        _albumVisible = false;

        _albumIntroPlayed = false;

        _albumDifficultyRating = 0;

        _albumRevealTimer = 0f;

        _albumTitleVisible = false;

        _albumStarsVisible = false;

        _djAfkTimer = 0f;

        _djSawEasterEgg = false;

        _djEasterEggPlaying = false;

        _djTvPlaying = false;

        _spamTimer = 0f;

        _spamming = false;

        _diffTransitioning = false;

        _exiting = false;

        _exitTimer = 0f;

        _exitDjOffset = 0f;

        _exitPinkOffset = 0f;

        _exitBgOffset = 0f;

        _exitDiffXOffset = 0f;

        _exitArrowOffset = 0f;

        _exitScoreOffset = 0f;

        _exitLetterYOffset = 0f;

        _exitCapsuleOffset = 0f;

        _exitOverhangYOffset = 0f;

        _exitAlbumOffset = 0f;

        _menuFading = false;

        _letterSortIndex = 2; // ALL



        if (_songs.Count > 0)

        {

            UpdateSelection();

            UpdateAlbumForSelection();

            SwitchBackingImage();

        }



        if (!Audio.MusicPlaying)

            Audio.PlayMusic("music/freakyMenu", true); RankFxInitOnLoad(); CapsGenerate(true);

    }



    public override void Unload()

    {

        _lastSelectedIndex = _selectedIndex; _rememberedSongId = _selectedIndex >= 0 && _selectedIndex < _songs.Count && !_songs[_selectedIndex].IsRandom ? _songs[_selectedIndex].Name : null;

        _lastDifficultyIndex = _difficultyIndex;

        foreach (var root in _modContentRoots)
        {
            Audio.ExtraContentRoots.Remove(root);
            Assets.RemoveExtraRoot(root);
        }
        _modContentRoots.Clear();

        StopTvCartoon(restoreMusic: false);

        Audio.StopMusic();



        _djSheet?.Dispose();

        _djSheet = null;

        _djSprite = null;

        _capsuleSheet?.Dispose();

        _capsuleSheet = null;

        _highscoreSheet?.Dispose();

        _highscoreSheet = null;

        _selectorSheet?.Dispose();

        _selectorSheet = null;

        _selectorLeft = null;

        _selectorRight = null;

        _diffNightmareSheet?.Dispose();

        _diffNightmareSheet = null;

        _diffNightmareFrames = null;

        _backingTextSheet?.Dispose();

        _backingTextSheet = null;

        _backingTextSprite = null;

        _dotPulseSheet?.Dispose();

        _dotPulseSheet = null;

        _dotPulseFrames = null;

        _digitSheet?.Dispose();

        _digitSheet = null;

        _clearFontSheet?.Dispose();

        _clearFontSheet = null;

        _bigNumbersSheet?.Dispose();

        _bigNumbersSheet = null;

        _smallNumbersSheet?.Dispose();

        _smallNumbersSheet = null;

        _weekTypesSheet?.Dispose();

        _weekTypesSheet = null;

        _newTextSheet?.Dispose();

        _newTextSheet = null;

        _newTextFrames = null;

        _sortedLettersSheet?.Dispose();

        _sortedLettersSheet = null;

        _sortedLettersSprite = null;

        _albumRollSheet?.Dispose();

        _albumRollSheet = null;

        _albumRollSprite = null;

        _albumTitleSheet?.Dispose();

        _albumTitleSheet = null;

        _albumTitleSprite = null;

        _freeplayStarsSheet?.Dispose();

        _freeplayStarsSheet = null;

        _freeplayStarsSprite = null;

        _favHeartSheet?.Dispose();

        _favHeartSheet = null;

        _favHeartFrames = null;

        DisposeRankExtras(); _rankBadgesSheet?.Dispose();

        _rankBadgesSheet = null;

        DisposePicoCardExtras(); _picoTopLoopSheet?.Dispose();

        _picoTopLoopSheet = null;

        _picoConfirmSheet?.Dispose();

        _picoConfirmSheet = null;

        _picoCard = null;

        _picoGlowConductor?.Stop();

        _picoGlowConductor = null;



        foreach (var kvp in _pixelIconSheets)

            kvp.Value?.Dispose();

        _pixelIconSheets.Clear();



        _bgAngleMask?.Dispose();

        _bgAngleMask = null;

        _bgImageMasked?.Dispose();

        _bgImageMasked = null;

        _bgBlackOverlay?.Dispose();

        _bgBlackOverlay = null;

    }



    // -------------------------------------------------------

    //  UPDATE

    // -------------------------------------------------------



    public override void Update(GameTime gameTime)

    { if (OnlineBoardUpdate((float)gameTime.ElapsedGameTime.TotalSeconds)) return; UpdateNpsPreview((float)gameTime.ElapsedGameTime.TotalSeconds); // chart NPS sticker (FreeplayScene.Nps.cs)

        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds; DevSceneElapsed += dt; DevSceneElapsedLocal += dt; DevClockStarted = true; ApplyForceSkipIntroOnce(); PicoCardUpdate(dt); BfCardUpdate(dt); if (CsUpdate(dt)) return; AlbumUpdate(dt); _rankBadgeAnimTime += dt; RankFxUpdate(dt);



        // Smooth scroll

        _scrollLerp = _selectedIndex + (_scrollLerp - _selectedIndex) * MathF.Pow(0.01f, dt / 0.192f);



        // Lerp score display (original: smoothLerpPrecision(lerpScore, intendedScore, dt, 0.2) with precision=1/100)

        {

            float factor = MathF.Pow(0.01f, dt / 0.2f);

            _lerpScore = _displayedScore + (_lerpScore - _displayedScore) * factor;

            if (MathF.Abs(_lerpScore - _displayedScore) <= 1f) _lerpScore = _displayedScore;

        }

        // Lerp completion display (original: smoothLerpPrecision(lerpCompletion, intendedCompletion, dt, 0.5))

        {

            float factor = MathF.Pow(0.01f, dt / 0.5f);

            _lerpCompletion = _intendedCompletion + (_lerpCompletion - _intendedCompletion) * factor;

            if (MathF.Abs(_lerpCompletion - _intendedCompletion) <= 0.01f) _lerpCompletion = _intendedCompletion;

        }



        // DJ animation update

        _djSprite?.Update(dt);

        _highscoreSprite?.Update(dt);

        _selectorLeft?.Update(dt);

        _selectorRight?.Update(dt);

        if (_picoConfirmVisible) _picoConfirmTime += dt;
        if (_yeahTime >= 0f) _yeahTime += dt;
        if (_picoBgClock >= 0f)
        {
            _picoBgClock += dt;
            int k = 0;
            for (int i = 0; i < PicoBgShots.Length; i++) if (_picoBgClock >= PicoBgShots[i].At) k = i;
            var sh = PicoBgShots[k];
            float t = Math.Min(1f, (_picoBgClock - sh.At) / sh.Dur);
            float e = t >= 1f ? 1f : 1f - MathF.Pow(2f, -10f * t);   // FlxEase.expoOut
            _bgDimColor = Color.Lerp(sh.From, sh.To, e);
        }



        // Arrow push timers countdown

        if (_leftArrowPush > 0) _leftArrowPush = Math.Max(0, _leftArrowPush - dt);

        if (_rightArrowPush > 0) _rightArrowPush = Math.Max(0, _rightArrowPush - dt);



        // Difficulty alpha flash countdown (original: 1/24s flash on change)

        if (_diffAlphaFlashTimer > 0) _diffAlphaFlashTimer = Math.Max(0, _diffAlphaFlashTimer - dt);

        if (_diffYBounceTimer > 0) _diffYBounceTimer = Math.Max(0, _diffYBounceTimer - dt);

        if (_letterWiggleTimer > 0) _letterWiggleTimer = Math.Max(0, _letterWiggleTimer - dt);



        // TV music fade ( Boyfriend DJ cartoon lowers menu music to 10% )

        if (_tvMusicFading)

        {

            _tvMusicFadeTimer += dt;

            float t = Math.Clamp(_tvMusicFadeTimer / _tvMusicFadeDuration, 0f, 1f);

            Audio.MusicVolume = MathHelper.Lerp(_tvMusicFadeFrom, _tvMusicFadeTo, t);

            if (t >= 1f) _tvMusicFading = false;

        }



        // Song preview fade-out (FNF_Official: fade out old preview before starting new)

        if (_previewFadingOut)

        {

            _previewFadeOutTimer += dt;

            float t = Math.Min(1f, _previewFadeOutTimer / PREVIEW_FADE_OUT_DURATION);

            Audio.MusicVolume = MathHelper.Lerp(Audio.MusicVolume, PREVIEW_FADE_OUT_END_VOLUME, t);

            if (t >= 1f)

            {

                _previewFadingOut = false;

                Audio.StopMusic();



                var next = _pendingPreviewSong;

                _pendingPreviewSong = null;

                if (next != null)

                    StartPreview(next);

            }

        }



        // Song preview fade-in

        if (_previewFadingIn)

        {

            _previewFadeTimer += dt;

            float t = Math.Min(1f, _previewFadeTimer / PREVIEW_FADE_IN_DURATION);

            Audio.MusicVolume = MathHelper.Lerp(PREVIEW_START_VOLUME, PREVIEW_TARGET_VOLUME, t);

            if (t >= 1f) _previewFadingIn = false;

        }



        // Preview loop segment using authored metadata (previewStart/previewEnd)

        if (_phase == Phase.Idle

            && _songPreviewPlaying

            && !_previewFadingIn

            && !_previewFadingOut

            && !string.IsNullOrEmpty(_activePreviewSong)

            && _activePreviewEndMs > _activePreviewStartMs

            && Audio.MusicPlaying

            && Audio.MusicPosition >= _activePreviewEndMs)

        {

            _pendingPreviewSong = _activePreviewSong;

            BeginPreviewFadeOut();

        }



        // Difficulty slide transition (old slides out, new slides in over 0.2s circInOut)

        if (_diffTransitioning)

        {

            _diffTransTimer += dt;

            float t = Math.Min(1f, _diffTransTimer / DIFF_TRANS_DURATION);

            // circInOut easing

            float eased = t < 0.5f

                ? (1f - MathF.Sqrt(1f - 4f * t * t)) / 2f

                : (MathF.Sqrt(1f - MathF.Pow(-2f * t + 2f, 2f)) + 1f) / 2f;

            _diffOldX = MathHelper.Lerp(_diffSlideX, _diffOldTarget, eased);

            _diffNewX = MathHelper.Lerp(_diffNewX > _diffSlideX ? 500f : -320f, DIFF_X, eased);

            if (t >= 1f)

            {

                _diffTransitioning = false;

                _diffOldTex = null;

                _diffSlideX = DIFF_X;

            }

        }



        // Exit animation — per-element exitMovers (original: goBack with FlxTween expoIn per element)

        if (_exiting)

        {

            _exitTimer += dt;



            // expoIn easing helper: t ? pow(2, 10*(t-1)) for t>0, else 0

            static float ExpoIn(float t) => t <= 0 ? 0 : t >= 1 ? 1 : MathF.Pow(2f, 10f * (t - 1f));



            // DJ: target -dj.width*1.6 (~-1024), speed 0.5s

            float djT = ExpoIn(Math.Min(1f, _exitTimer / 0.5f));

            _exitDjOffset = djT * (-ExitDjWidth() * 1.6f - (CutoutWidth * DJ_POS_MULTI + 640f));



            // Pink panel: target -PINK_PANEL_W, speed 0.4s (original: BackingCard exitMover speed:0.4)

            float pinkT = ExpoIn(Math.Min(1f, _exitTimer / 0.4f));

            _exitPinkOffset = pinkT * -PINK_PANEL_W;



            // BG / overlay: target FlxG.width*1.5, speed 0.4s

            float bgT = ExpoIn(Math.Min(1f, _exitTimer / 0.4f));

            _exitBgOffset = bgT * (FNFGame.SCREEN_WIDTH * 1.5f - BG_IMAGE_X);



            // Difficulty + dots: target x-300, speed 0.25s (original: grpDifficulties exitMover x:-300 speed:0.25)

            float diffT = ExpoIn(Math.Min(1f, _exitTimer / 0.25f));

            _exitDiffXOffset = diffT * (-300f - (DiffBaseX + 90f));



            // Arrows: target -width*2, speed 0.26s

            float arrowT = ExpoIn(Math.Min(1f, _exitTimer / 0.26f));

            _exitArrowT = arrowT;



            // Score/highscore/clearBox: target FlxG.width, speed 0.3s

            float scoreT = ExpoIn(Math.Min(1f, _exitTimer / 0.3f));

            _exitScoreOffset = 0f; _exitScoreT = scoreT;



            // LetterSort: target y-100, speed 0.3s

            float letterT = ExpoIn(Math.Min(1f, _exitTimer / 0.3f));

            _exitLetterYOffset = letterT * (-100f - 75f);



            // Overhang group: target y -164, speed 0.2s (original: y:-overhangStuff.height speed:0.2)

            float ohT = ExpoIn(Math.Min(1f, _exitTimer / 0.2f));

            _exitOverhangYOffset = ohT * -164f;



            // Album roll: target x FlxG.width, speed 0.4s (original: albumRoll exitMover x:FlxG.width speed:0.4)

            float albumT = ExpoIn(Math.Min(1f, _exitTimer / 0.4f));

            _exitAlbumOffset = 0f; _exitAlbumT = albumT;



            // Capsules: slide right (original: doJumpOut, x ? 1.2*screenWidth)

            float capsT = ExpoIn(Math.Min(1f, _exitTimer / 0.35f));

            _exitCapsuleOffset = 0f; UpdateCapsuleJumpOut(dt);



            if (_exitTimer >= EXIT_DURATION)

            {

                _previewFadingIn = false;

                _previewFadingOut = false;

                Audio.MusicVolume = 1f;

                Audio.StopMusic();

                // Restore freakyMenu with fade-in (original: fadeIn(4.0, 0.0, 1.0))

                Audio.PlayMusic("music/freakyMenu", true);
                Audio.FadeIn(4f, 0f, 1f);   // FlxG.sound.music.fadeIn(4.0, 0.0, 1.0)
                // close(): Freeplay is a substate of MainMenuState -> back instantly, no transition / loading
                ReturnToParentMenu();
                return;

            }

        }



        // Music fade-in after returning to main menu (original: fadeIn(4.0, 0.0, 1.0))

        if (_menuFading)

        {

            _menuFadeTimer += dt;

            float t = Math.Min(1f, _menuFadeTimer / 4f);

            Audio.MusicVolume = t;

            if (t >= 1f) _menuFading = false;

        }



        // CardGlow fade out (original: introDone=0.45s sineOut, disappear=0.25s sineOut)

        if (_cardGlowAlpha > 0)

        {

            _cardGlowAlpha = Math.Max(0, _cardGlowAlpha - dt / _cardGlowDuration);

            _cardGlowScale = 1f + (1f - _cardGlowAlpha) * 0.2f; // 1.0 -> 1.2

        }



        // BG image color reveal (original: expoOut from black to white in 0.6s)

        if (_introDone && _bgRevealTint < 1f)

        {

            _bgRevealTint = Math.Min(1f, _bgRevealTint + dt / 0.6f);

        }



        // Confirm glow phased sequence (matches original BackingCard.confirm)

        // Phase 1: confirmGlow2 alpha 0?0.5 over 0.33s

        // Phase 2: confirmGlow2=0.6, confirmGlow=1, confirmTextGlow=1; then glow?0, textGlow?0.4 over 0.5s

        if (_confirmGlowPhase == 1)

        {

            _confirmGlowTimer += dt;

            float t = Math.Min(1f, _confirmGlowTimer / 0.33f);

            _confirmGlow2Alpha = (1f - (1f - t) * (1f - t)) * 0.5f; // FlxEase.quadOut

            if (t >= 1f)

            {

                _confirmGlowPhase = 2;

                _confirmGlowTimer = 0f;

                _confirmGlow2Alpha = 0.6f;

                _confirmGlowAlpha = 1f;

                _confirmTextGlowAlpha = 1f;

                // Start second BG dim phase

                _bgDimPhase = 2;

                _bgDimFrom = new Color(0xCD, 0xCD, 0xCD);

                _bgDimTo = new Color(0x55, 0x55, 0x55);

                _bgDimDuration = 2f;

                _bgDimTimer = 0f;

                _bgDimming = true;

            }

        }

        else if (_confirmGlowPhase == 2)

        {

            _confirmGlowTimer += dt;

            float t = Math.Min(1f, _confirmGlowTimer / 0.5f);

            _confirmGlowAlpha = 1f - t;

            _confirmTextGlowAlpha = 1f - t * 0.6f; // 1.0 ? 0.4

        }



        // Pink color tween (smooth interpolation)

        if (_pinkTweening)

        {

            _pinkTweenTimer += dt;

            float t = Math.Min(1f, _pinkTweenTimer / _pinkTweenDuration);

            // quadOut easing: 1 - (1-t)^2

            float eased = 1f - (1f - t) * (1f - t);

            _pinkColor = Color.Lerp(_pinkColorFrom, _pinkColorTo, eased);

            if (t >= 1f) _pinkTweening = false;

        }



        // BG image dimming on confirm

        if (_bgDimming)

        {

            _bgDimTimer += dt;

            float t = Math.Min(1f, _bgDimTimer / _bgDimDuration);

            float eased = _bgDimPhase == 1 ? t : (1f - MathF.Pow(2f, -10f * t)); // linear for phase1, expoOut for phase2

            _bgDimColor = Color.Lerp(_bgDimFrom, _bgDimTo, eased);

            if (t >= 1f) _bgDimming = false;

        }



        // Highscore animation on random timer (original: 12-50s first, 20-60s repeat)

        if (_highscoreSprite != null && _highscoreSheet != null)

        {

            _highscoreNextPlay -= dt;

            if (_highscoreNextPlay <= 0)

            {

                _highscoreSprite.PlayAnimation("highscore small instance 1", loop: false, force: true);

                _highscoreNextPlay = _rng.Next(20, 61);

            }

        }



        // Overhang slides in from top (original: y=-164 tweens to y=-100 over 0.3s quartOut)

        if (_overhangTweenTimer < 0.3f)

        {

            _overhangTweenTimer = Math.Min(0.3f, _overhangTweenTimer + dt);

            float t = _overhangTweenTimer / 0.3f;

            float eased = 1f - MathF.Pow(1f - t, 4f); // quartOut

            _overhangY = MathHelper.Lerp(-164f, -100f, eased);

        }



        // Capsule bounce-in timer

        if (_capsuleBounceTimer < CAPSULE_BOUNCE_DURATION)

            _capsuleBounceTimer += dt;



        // CharSelectHint slides in from bottom (original: y starts 100px below, tweens over 0.8s quartOut)

        if (_introDone && _hintSlideTweenTimer < 0.8f)

        {

            _hintSlideTweenTimer = Math.Min(0.8f, _hintSlideTweenTimer + dt);

            float t = _hintSlideTweenTimer / 0.8f;

            float eased = 1f - MathF.Pow(1f - t, 4f); // quartOut

            _hintSlideY = MathHelper.Lerp(100f, 0f, eased);

        }



        // CharSelectHint alpha pulse (original: hintTimer += elapsed * 2)

        if (_introDone)

            _hintAlphaTimer += dt * 2f;



        // Intro UI reveal gate (FNF_Official: 1/24s then 1.5/24s)

        if (_introDone && !_introUiRevealed)

        {

            _introRevealTimer += dt;

            if (_introRevealTimer >= (2.5f / 24f))

            {

                _introUiRevealed = true;

                _dotsVisible = true;

            }

        }



        // Difficulty sprite slides in from left on introDone (original: x tweens to 90 over 0.6s quartOut)

        if (_introDone && _diffIntroTweenTimer < 0.6f)

        {

            _diffIntroTweenTimer = Math.Min(0.6f, _diffIntroTweenTimer + dt);

            float t = _diffIntroTweenTimer / 0.6f;

            float eased = 1f - MathF.Pow(1f - t, 4f); // quartOut

            _diffSlideX = MathHelper.Lerp(-300f, DIFF_X, eased);

        }



        // Capsule text flicker on confirm

        if (_capsuleFlicker)

        {

            _flickerTimer += dt;

        }



        switch (_phase)

        {

            case Phase.Intro:

                UpdateIntro(dt);

                break;

            case Phase.Idle:

                UpdateIdle(dt);

                break;

            case Phase.Confirmed:

                UpdateConfirmed(dt);

                break;

        }



        UpdateDebugMenu();

    }



    private void UpdateIntro(float dt)

    {

        _introTimer += dt;



        // Pink panel slides in from left

        float targetPink = 0;

        _pinkSlideX += (targetPink - _pinkSlideX) * Math.Min(1f, dt * 6f);



        // Capsules slide in from right

        float targetCapsuleX = 0;

        _capsuleSlideX += (targetCapsuleX - _capsuleSlideX) * Math.Min(1f, dt * 5f);



        // Transition to idle when DJ intro finishes (OnDJIntroDone sets _introDone)

        // Also use a safety timeout so the user isn't stuck if DJ anim is missing

        if (_introDone || _introTimer > 2.5f)

        {

            _phase = Phase.Idle;

            _pinkSlideX = 0;

            _capsuleSlideX = 0;

            if (!_introDone) OnDJIntroDone(); // safety fallback

        }



        // Back is ignored during the intro: uiStateMachine is EnteringFreeplay (canInteract() == false) until onDJIntroDone.

    }



    private void UpdateIdle(float dt)

    { if (_rankBlockInput) return;   // uiStateMachine Interacting during the rank animation (canInteract() false)

        if (_exiting) return; // block input during exit animation

        if (_switchingToCharSelect) return; if (UpdateInstMenu(dt)) return; // let the to-CS animation play out; CapsuleOptionsMenu owns the input



        if (_songs.Count == 0)

        {

            if (Input.BackPressed)

            {

                Audio.PlaySound("cancelMenu");

                StopTvCartoon(restoreMusic: false);

                _pendingPreviewSong = null;

                _previewFadingIn = false;

                _previewFadingOut = false;

                Audio.MusicVolume = 1f;

                EnsureParentMenu(); ReturnToParentMenu();

                return;

            }

            return;

        }



        // Hold/spam navigation (original: first press immediate, 0.9s hold ? 0.07s repeat)

        bool upHeld = Input.UpHeld;

        bool downHeld = Input.DownHeld;

        if (upHeld || downHeld)

        {

            if (_spamming)

            {

                if (_spamTimer >= 0.07f)

                {

                    _spamTimer = 0;

                    ChangeSelectionNav(upHeld ? -1 : 1);

                }

            }

            else if (_spamTimer >= 0.9f)

            {

                _spamming = true;

                _spamTimer = 0;

            }

            else if (_spamTimer <= 0)

            {

                ChangeSelectionNav(upHeld ? -1 : 1);

            }

            _spamTimer += dt;

        }

        else

        {

            _spamming = false;

            _spamTimer = 0;

        }



        // Mouse wheel scrolling (original: FlxG.mouse.wheel)

        int wheel = Input.ScrollDelta;

        if (wheel != 0)

        {

            int steps = wheel > 0 ? -1 : 1;

            ChangeSelectionNav(steps);

        }



        // Difficulty switch (separate from letter sort controls to avoid double-trigger)

         bool diffLeftPressed = Input.IsPressed(Microsoft.Xna.Framework.Input.Keys.A)

             || Input.IsPressed(Microsoft.Xna.Framework.Input.Keys.Left)

             || Input.IsGamePadPressed(Microsoft.Xna.Framework.Input.Buttons.LeftShoulder)

             || Input.IsGamePadPressed(Microsoft.Xna.Framework.Input.Buttons.LeftTrigger)

             || Input.IsGamePadPressed(Microsoft.Xna.Framework.Input.Buttons.DPadLeft);

         bool diffRightPressed = Input.IsPressed(Microsoft.Xna.Framework.Input.Keys.D)

             || Input.IsPressed(Microsoft.Xna.Framework.Input.Keys.Right)

             || Input.IsGamePadPressed(Microsoft.Xna.Framework.Input.Buttons.RightShoulder)

             || Input.IsGamePadPressed(Microsoft.Xna.Framework.Input.Buttons.RightTrigger)

             || Input.IsGamePadPressed(Microsoft.Xna.Framework.Input.Buttons.DPadRight);

         if (diffLeftPressed)

         {

             ChangeDifficulty(-1);

             ResetDjAfk(returnToIdle: true); // reset AFK on difficulty change

         }

         if (diffRightPressed)

         {

             ChangeDifficulty(1);

             ResetDjAfk(returnToIdle: true); // reset AFK on difficulty change

         }



        // DJ idle Easter Egg / TV cartoon (original: 60s idleEasterEgg, 120s cartoon)

        if (_djSprite != null && _phase == Phase.Idle)

        {

            bool easterEggActive = _djEasterEggPlaying

                || (!string.IsNullOrEmpty(_djEasterEggAnim) && _djSprite.CurrentAnimation == _djEasterEggAnim);

            bool tvActive = _djTvPlaying

                || (!string.IsNullOrEmpty(_djTvAnim) && _djSprite.CurrentAnimation == _djTvAnim);

            bool busy = easterEggActive || tvActive;

            if (!busy)

            {

                _djAfkTimer += dt;



                if (_djAfkTimer >= DevDjAfkSec && !_djSawEasterEgg && !string.IsNullOrEmpty(_djEasterEggAnim))

                {

                    _djEasterEggPlaying = true;

                    _djSawEasterEgg = true;

                    _djAfkTimer = 0f;



                    _djSprite.PlayAnimation(_djEasterEggAnim, loop: false, force: true);

                    _djSprite.OnFinish = () =>

                    {

                        _djSprite.OnFinish = null;

                        _djEasterEggPlaying = false;

                        if (!string.IsNullOrEmpty(_djIdleAnim))

                            _djSprite.PlayAnimation(_djIdleAnim, loop: true, force: true);

                    };

                }

                else if (_djAfkTimer >= 120f && !string.IsNullOrEmpty(_djTvAnim))

                {

                    StartTvAnimation();

                }

            }



            // TV frame-based sound triggers (matches BoyfriendFreeplayDJ.hx)

            if (_djTvPlaying && _djSprite != null && _djSprite.CurrentAnimation == _djTvAnim)

            {

                int frame = _djSprite.FrameIndex;

                if (frame != _tvLastFrame)

                {

                    if (frame == 80) { try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tv_debug2.log"), "TV TRIGGER: remote_click at frame 80" + Environment.NewLine); } catch { } Audio.PlaySound("sounds/remote_click"); }

                    if (frame == 85) { try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tv_debug2.log"), "TV TRIGGER: runTvLogic at frame 85" + Environment.NewLine); } catch { } RunTvLogic(); }

                    _tvLastFrame = frame;

                }

            }



            // When the cartoon audio finishes, restart the TV animation from the channel-change frame

            if (_tvCartoonSound != null && _tvCartoonSound.State == SoundState.Stopped)

            {

                try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tv_debug2.log"), "TV CARTOON SOUND finished, restarting from 60" + Environment.NewLine); } catch { }

                _tvCartoonSound.Dispose();

                _tvCartoonSound = null;

                // Restart the TV animation from the channel-change frame (matches original)

                if (_djSprite != null && !string.IsNullOrEmpty(_djTvAnim))

                    _djSprite.PlayAnimationFromFrame(_djTvAnim, 60, loop: false);

            }

        }



        // Home/End jump navigation (original: changeSelection jumps to first/last)

        if (Input.IsPressed(Microsoft.Xna.Framework.Input.Keys.Home) && _selectedIndex != 0)

        {

            _selectedIndex = 0;

            Audio.PlaySound("scrollMenu", 0.4f);

            OnSelectionChanged();

            ResetDjAfk(returnToIdle: true);

        }

        if (Input.IsPressed(Microsoft.Xna.Framework.Input.Keys.End) && _selectedIndex != _songs.Count - 1)

        {

            _selectedIndex = _songs.Count - 1;

            Audio.PlaySound("scrollMenu", 0.4f);

            OnSelectionChanged();

            ResetDjAfk(returnToIdle: true);

        }



        // Letter sort cycling (FNF_Official: FREEPLAY_LEFT/FREEPLAY_RIGHT)

        // Use dedicated nav inputs so A/D (difficulty) does not also move letter sort.

        bool letterLeftPressed = Input.IsPressed(Microsoft.Xna.Framework.Input.Keys.Q);

        bool letterRightPressed = Input.IsPressed(Microsoft.Xna.Framework.Input.Keys.E);



        if (letterLeftPressed)

        {

            _letterSortIndex = Math.Max(0, _letterSortIndex - 1);

            Audio.PlaySound("scrollMenu", 0.4f);

            ApplyLetterFilter();

            StartLetterWiggle(-1);

            ResetDjAfk(returnToIdle: true); // reset AFK on letter sort change

        }

        if (letterRightPressed)

        {

            _letterSortIndex = Math.Min(LetterSortLabels.Length - 1, _letterSortIndex + 1);

            Audio.PlaySound("scrollMenu", 0.4f);

            ApplyLetterFilter();

            StartLetterWiggle(1);

            ResetDjAfk(returnToIdle: true); // reset AFK on letter sort change

        }



        // TAB / controller SwitchCharButton -> Character Select (original: FREEPLAY_CHAR_SELECT, plays confirmMenu)

        if (Input.SwitchCharPressed && !_diffTransitioning && !_switchingToCharSelect)

        {

            Audio.PlaySound("confirmMenu");

            StopTvCartoon(restoreMusic: false);

            _pendingPreviewSong = null;

            _previewFadingIn = false;

            _previewFadingOut = false;

            Audio.MusicVolume = 1f;

            _switchingToCharSelect = true;

            if (_djSprite != null && !string.IsNullOrEmpty(_djCharSelectAnim))

            {

                _djSprite.OnFinish = null; CsStart(); // tryOpenCharSelect: transitionDelay 0.45 s, then transitionToCharSelect (FreeplayScene.CharSelTransition.cs)

                _djSprite.PlayAnimation(_djCharSelectAnim, loop: false, force: true);

            }

            else

            {

                CsStart();

            }

            return;

        }



        // CTRL ? Toggle favorite (original: controls.FREEPLAY_FAVORITE)

        if (Input.IsPressed(Microsoft.Xna.Framework.Input.Keys.LeftControl)

            || Input.IsPressed(Microsoft.Xna.Framework.Input.Keys.RightControl))

        {

            if (_songs.Count > 0)

            {

                var currentSong = _songs[_selectedIndex];

                if (!currentSong.IsRandom && !string.IsNullOrEmpty(currentSong.Name))

                {

                    var favs = HighscoreManager.Data.FavoriteSongs;

                    bool wasFav = favs.Contains(currentSong.Name);

                    if (wasFav)

                        favs.Remove(currentSong.Name);

                    else

                        favs.Add(currentSong.Name);

                    HighscoreManager.SavePreferences();

                    Audio.PlaySound(wasFav ? "unfav" : "fav");

                    ResetDjAfk(returnToIdle: true); // reset AFK on favorite toggle

                }

            }

        }



        // Confirm

        if ((Input.ConfirmPressed || _instForceConfirm) && !_diffTransitioning)

        {

            if (_songs.Count == 0)

                return;



            // If random capsule selected, pick a random real song

            var currentSong = _songs[_selectedIndex]; bool instWasRandom = currentSong.IsRandom;

            if (currentSong.IsRandom)

            {

                var realSongs = _songs.Where(s => !s.IsRandom).ToList();

                if (realSongs.Count > 0)

                {

                    var picked = realSongs[_rng.Next(realSongs.Count)];

                    _selectedIndex = _songs.IndexOf(picked);

                    currentSong = _songs[_selectedIndex];

                    UpdateSelection();

                    UpdateAlbumForSelection();

                    SwitchBackingImage();

                }

                else

                {

                    Audio.PlaySound("cancelMenu");

                    return;

                }

            }



            if (instWasRandom) PickRandomInstrumental(_songs[_selectedIndex]);
            else if (!_instForceConfirm && TryOpenInstMenu(_songs[_selectedIndex])) return; // capsuleOnConfirm -> openInstrumentalList
            _instForceConfirm = false;
            _pendingPreviewSong = null;

            _previewFadingIn = false;

            _previewFadingOut = false;

            Audio.MusicVolume = 1f;



            _phase = Phase.Confirmed;

            DevConfirmElapsed = 0f;
            _confirmTimer = _isPicoTheme ? 1.45f : 1.0f; // styles/<id>.json startDelay (bf 1.0, pico 1.45)

            _confirmFading = false; _confirmLoadIssued = false;

            _confirmFadeAlpha = 0f;

            Audio.PlaySound("confirmMenu");

            StopTvCartoon(restoreMusic: false);

            if (_djSprite != null && !string.IsNullOrEmpty(_djConfirmAnim))

                _djSprite.PlayAnimation(_djConfirmAnim, force: true, loop: false);



            // Pico confirm atlas (flash/color boxes)

            if (_isPicoTheme && _picoCard != null)
            {
                _picoConfirmVisible = true;   // confirmAtlas.visible = true; anim.play("") (play-once, 24 fps)
                _picoConfirmTime = 0f;
            }



            // Capsule text flicker on confirm (original: songText.flickerText)

            _capsuleFlicker = true;

            _flickerTimer = 0f;



            if (!_isPicoTheme)
            {
            // Original BackingCard.confirm(): pink color tween 0xFFD0D5 -> 0x171831 over 0.33s quadOut

            _pinkColorFrom = new Color(0xFF, 0xD0, 0xD5);

            _pinkColorTo = new Color(0x17, 0x18, 0x31);

            _pinkTweenTimer = 0f;

            _pinkTweenDuration = 0.33f;

            _pinkTweening = true;



            _orangeBarsVisible = false;

            _scrollingTextVisible = false;



            // Start confirm glow sequence (phase 1: glow2 alpha 0->0.5 over 0.33s)

            _confirmGlowAlpha = 0f;

            _confirmGlow2Alpha = 0f;

            _confirmTextGlowAlpha = 0f;

            _confirmGlowPhase = 1;

            _confirmGlowTimer = 0f;



            // BG image dim phase 1: 0xA8A8A8 -> 0x646464 over 0.5s

            _bgDimPhase = 1;

            _bgDimFrom = new Color(0xA8, 0xA8, 0xA8);

            _bgDimTo = new Color(0x64, 0x64, 0x64);

            _bgDimDuration = 0.5f;

            _bgDimTimer = 0f;

            _bgDimming = true;



            // Play backingTextYeah animation on confirm

            // Original FNF: backingTextYeah.anim.play("") ? plays main timeline ("default")

            if (_backingTextSprite != null && _backingTextSheet?.Animations != null

                && _backingTextSheet.Animations.ContainsKey("default"))

            {

                _backingTextSprite.PlayAnimation("default", loop: false, force: true);
            }
            _yeahTime = 0f;   // backingTextYeah.anim.play("")
            {

            }
            }
            else
            {
                // backcard-pico.hxc confirm(): only the atlas + backingImage colour shots
                _picoBgClock = 0f;
                _bgDimColor = Color.White;
            }
            // Hide dots on confirm (original: fadeDots(false))

            _dotsVisible = false;

            return;

        }



        // Back — start exit animation (original: goBack() with exitMovers)

        if (Input.BackPressed && !_diffTransitioning)

        {

            Audio.PlaySound("cancelMenu");

            StopTvCartoon(restoreMusic: false);

            _pendingPreviewSong = null;

            _previewFadingIn = false;

            _previewFadingOut = false;

            Audio.MusicVolume = 1f;

            // Original: backingCard.disappear() — tween pink back to pink, replay cardGlow, hide orange

            _pinkColorFrom = new Color(0xFF, 0xD8, 0x63); // gold

            _pinkColorTo = new Color(0xFF, 0xD0, 0xD5);   // pink

            _pinkTweenTimer = 0f;

            _pinkTweenDuration = 0.25f;

            _pinkTweening = true;

            _orangeBarsVisible = false;

            _cardGlowAlpha = 1f;

            _cardGlowScale = 1f;

            _cardGlowDuration = 0.25f; // original: disappear uses 0.25s sineOut (vs 0.45s introDone)

            _dotsVisible = false;

            _scrollingTextVisible = false;

            _exiting = true; EnsureParentMenu(); _picoCardHidden = true; StartCapsuleJumpOut();

            _exitTimer = 0f;

            return;

        }



        // Song preview with delay (original: 0.25s delay)

        _songPreviewDelay += dt;

        if (_songPreviewDelay >= PREVIEW_DELAY && !_songPreviewPlaying && _lastPreviewIndex != _selectedIndex)

        {

            _lastPreviewIndex = _selectedIndex;

            _songPreviewPlaying = true;

            if (_songs.Count > 0)

                PlaySongPreview(_songs[_selectedIndex].Name);

        }

    }



    private void UpdateDebugMenu()

    {

        var kb = Keyboard.GetState();

        bool toggleF2 = kb.IsKeyDown(Keys.F2) && !_prevDebugKb.IsKeyDown(Keys.F2);

        bool toggleF6 = kb.IsKeyDown(Keys.F6) && !_prevDebugKb.IsKeyDown(Keys.F6);

        if (toggleF2 || toggleF6)

            _debugMenuVisible = !_debugMenuVisible;

        _prevDebugKb = kb;



        var mouse = Mouse.GetState();

        bool clicked = mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton != ButtonState.Pressed;

        var mousePoint = new Point(mouse.X, mouse.Y);



        if (_debugMenuVisible)

        {

            foreach (var item in _debugMenuItems)

            {

                if (item.Bounds.Contains(mousePoint) && clicked)

                {

                    item.Action?.Invoke();

                    break;

                }

            }

        }



        _prevMouse = mouse;

    }



    /// <summary>

    /// Move selection by delta (original: changeSelection). Resets afk timer and plays scroll sound.

    /// </summary>

    private void ChangeSelectionNav(int delta)

    {

        if (_songs.Count == 0) return; // defensive guard against empty list

        _selectedIndex = ((_selectedIndex + delta) % _songs.Count + _songs.Count) % _songs.Count;

        Audio.PlaySound("scrollMenu", 0.4f);

        OnSelectionChanged();

        ResetDjAfk(returnToIdle: true);

    }



    /// <summary>

    /// Change difficulty with slide-in/out animation (original: changeDiff with circInOut tween).

    /// </summary>

    private void ChangeDifficulty(int change)

    { if (change != 0) ReplayRankBadges();   // changeDiff -> initData -> set_rank

        int oldIdx = _difficultyIndex;

        int newIdx = ((_difficultyIndex + change) % Difficulties.Length + Difficulties.Length) % Difficulties.Length;

        if (newIdx == oldIdx)

            return;



        _difficultyIndex = newIdx;



        Audio.PlaySound("scrollMenu", 0.4f);

        ResetDjAfk(returnToIdle: true);



        // Push arrow animation

        if (change < 0) _leftArrowPush = ARROW_PUSH_DURATION;

        else _rightArrowPush = ARROW_PUSH_DURATION;



        // Difficulty alpha flash (original: 1/24s at half alpha)

        _diffAlphaFlashTimer = 1f / 24f;



        // Difficulty y-offset bounce (original: offset.y -= 5, resets after 1/24s)

        _diffYBounceTimer = 1f / 24f;



        // Start difficulty slide transition (old slides out, new slides in)

        _diffOldIndex = oldIdx;

        _diffOldTex = oldIdx switch

        {

            0 => _diffEasy,

            1 => _diffNormal,

            2 => _diffHard,

            3 => _diffErect,

            4 => _diffNightmare,

            _ => _diffNormal

        };

        _diffOldTarget = change > 0 ? -320f : 500f;

        _diffNewX = change > 0 ? 500f : -320f;

        _diffNewTarget = DIFF_X;

        _diffTransTimer = 0f;

        _diffTransitioning = true;

        _diffOldX = _diffSlideX;



        // Switching difficulty changes which songs are available in list.

        ApplyLetterFilter();

    }



    private void UpdateConfirmed(float dt)

    {

        _confirmTimer -= dt;
        DevConfirmElapsed += dt; _devConfirmFrames++;

        if (_confirmTimer <= 0 && !_confirmFading)

        {

            // Original: after startDelay, funnyCam.fade(BLACK, 0.2s) then load PlayState

            _confirmFading = true;

            _confirmFadeAlpha = 0f;

        }

        if (_confirmFading)

        {

            _confirmFadeAlpha = Math.Min(1f, _confirmFadeAlpha + dt / 0.2f);

            if (_confirmFadeAlpha >= 1f && !_confirmLoadIssued)
            {
                _confirmLoadIssued = true;   // LoadingState.loadPlayState runs once (fade onComplete)
                if (_songs.Count > 0)

                {

                    var song = _songs[_selectedIndex];

                    string diff = GetCurrentDifficulty(song);

                    string playSongName = ResolveSongNameForSelection(song, diff);

                    _previewFadingIn = false;

                    _previewFadingOut = false;

                    Audio.MusicVolume = 1f;

                    Audio.StopMusic();

                    var fpParams = PlayStateParams.ForFreeplay(playSongName, diff, song.ModPath); if (_instChosen != null) fpParams.TargetInstrumental = _instChosen; Game.Scenes.ChangeScene(new PlayScene(fpParams));

                }

            }

        }

    }



    // -------------------------------------------------------

    //  DRAW

    // -------------------------------------------------------



    public override void Draw(SpriteBatch spriteBatch)

    {
        RankFxBeginDraw(); bool parentDrawn = DrawParentMenu(spriteBatch);
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);
        if (!parentDrawn)
            spriteBatch.Draw(Assets.Pixel, new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT), new Color(14, 11, 33));



        // 2) Darkback layer behind DJ (original: darkBack below DJ)

        // Original: BitmapUtil.scalePartByWidth(darkback, CUTOUT_WIDTH) ? scales to panel width

        if (_introDone && _darkBack != null && _darkBack != Assets.Pixel && _darkBack.Width > 0)

        {

            float dbScale = PINK_PANEL_W / _darkBack.Width;

            spriteBatch.Draw(_darkBack,

                new Vector2(_pinkSlideX + _exitPinkOffset, 0), null, Color.White * 0.5f,

                0f, Vector2.Zero, dbScale, SpriteEffects.None, 0f);

        }



        // 3.5) Angled mask overlay that defines the diagonal split (approx of AngleMask/blackOverlay)

        DrawBGAngleMask(spriteBatch);



        // 4) Yellow bg piece (original: behind capsules area)

        if (_introDone && _yellowBgPiece != null && _yellowBgPiece != Assets.Pixel)

        {

            spriteBatch.Draw(_yellowBgPiece,

                new Vector2(PINK_PANEL_W + _exitBgOffset, 0), null, Color.White,

                0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);

        }



        // 5) Pink backing card (left panel)

        DrawPinkBack(spriteBatch);



        // 5.5) Pico-specific backing card overlays (guns / RPG theme)

        DrawPicoCardLayers(spriteBatch);



        // 6) Orange accent bar — drawn before text so text renders on top

        DrawOrangeBar(spriteBatch);



        // 5.6) Scrolling text behind DJ (original: BGScrollingText rows). Spans the full

        // screen width; the character BG image drawn in step 7 masks it to the yellow area.

        DrawBfCardTexts(spriteBatch); DrawBfCardGlow(spriteBatch); // backcard-bf.hxc rows + glowDark/glow (FreeplayScene.BfCard.cs)



        // CardGlow (last member of backingCard)
        DrawCardGlow(spriteBatch);
        spriteBatch.End();

        // backingCard confirm members, in BackingCard/backcard-bf.hxc add order: confirmGlow2, confirmGlow,
        // confirmTextGlow, backingTextYeah. backcard-pico.hxc adds none of them (only its confirmAtlas).
        if (!_isPicoTheme)
        {
            DrawConfirmGlow(spriteBatch);
            spriteBatch.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.AlphaBlend);
            DrawBackingTextYeah(spriteBatch);
            spriteBatch.End();
        }
        else
        {
            DrawPicoConfirmAtlas(spriteBatch);
        }

        // dj (FreeplayState.hx:443, added right after backingCard)
        spriteBatch.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.AlphaBlend);
        DrawDJ(spriteBatch);
        spriteBatch.End();

        // backingImage (FreeplayState.hx:490): ABOVE the card and the DJ
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);
        DrawBGImage(spriteBatch);
        spriteBatch.End();

        // albumRoll (FreeplayState.hx:531): above backingImage and the DJ
        spriteBatch.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.AlphaBlend);
        DrawAlbumRollTl(spriteBatch);
        spriteBatch.End();

        // 7) Everything else (capsules, UI)

        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);



        // Song capsules

        if (_songs.Count > 0)

            DrawCapsules(spriteBatch);

        else

            DrawEmptyState(spriteBatch);



        // UI elements only visible after DJ intro (original behavior)

        // During exit, elements animate out via per-element offsets instead of hiding

        if (_introDone && _introUiRevealed)

        {

            // Difficulty selector + arrows + dots

            CsShift(spriteBatch, -270f); DrawDifficulty(spriteBatch);

            DrawDifficultyArrows(spriteBatch);

            CsShift(spriteBatch, 0f); DrawDifficultyDots(spriteBatch);



            // Letter sort bar below difficulty area

            CsShift(spriteBatch, -270f); DrawLetterSort(spriteBatch);

        }



        if (_introDone)

        {



            // Score display (highscore label + score + clearBox + completion)

            CsShift(spriteBatch, -270f); DrawScoreDisplay(spriteBatch); CsShift(spriteBatch, 0f);

        }



        spriteBatch.End();



        // 8) Everything else (capsules, UI)

        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);



        // Top overhang bar (always visible, animates in from top)

        CsShift(spriteBatch, -300f); DrawOverhang(spriteBatch);



        // Character select hint at bottom

        DrawCharSelectHint(spriteBatch); CsShift(spriteBatch, 0f);



        DrawInstMenu(spriteBatch); // CapsuleOptionsMenu (funnyCam, zIndex 10000)



        // Confirm screen fade-to-black overlay (original: funnyCam.fade(BLACK, 0.2s))

        if (_confirmFading && _confirmFadeAlpha > 0)

        {

            spriteBatch.Draw(Assets.Pixel,

                new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT),

                Color.Black * _confirmFadeAlpha);

        }



        CsDrawFade(spriteBatch); // BlueFade + transitionGradient (to Character Select)
        DrawDebugMenu(spriteBatch);



        spriteBatch.End(); RankFxEndDraw(spriteBatch);

        DrawNpsPreview(spriteBatch); DrawOnlineBoardStrip(spriteBatch); DrawOnlineBoard(spriteBatch);
    }



    private void DrawBGImage(SpriteBatch sb)

    {

        var img = _bgImageMasked ?? _bgImage;

        if (img == null || img == Assets.Pixel) return;

        float scale = (float)FNFGame.SCREEN_HEIGHT / img.Height;
        float bgImageX = BG_IMAGE_X + _exitBgOffset;
        if (!_introDone)
        {
            // blackOverlayBullshitLOLXD: starts at x = FlxG.width, tween x -> backingImage.x, 0.7 s quintOut
            // (backingImage itself stays invisible until introDone)
            if (_bgBlackOverlay != null && !_fromCharSelectSkip && !ForceSkipIntro)
            {
                float k = Math.Min(1f, DevSceneElapsedLocal / 0.7f);
                float q = 1f - MathF.Pow(1f - k, 5f);
                float ox = FNFGame.SCREEN_WIDTH + (BG_IMAGE_X - FNFGame.SCREEN_WIDTH) * q;
                sb.Draw(_bgBlackOverlay, new Vector2(ox, 0), null, Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            }
            return;
        }



        // Original: blackOverlayBullshitLOLXD is drawn before backingImage and uses the same

        // AngleMask shader. It darkens the backing card where the BG image will be transparent.

        // Original: blackOverlayBullshitLOLXD.visible = false once the backingImage color tween finishes.

        if (_bgBlackOverlay != null && _bgRevealTint < 1f && !ForceSkipIntro)

        {

            sb.Draw(_bgBlackOverlay,

                new Vector2(bgImageX, CsDy(-100f)), null, Color.White,

                0f, Vector2.Zero, scale, SpriteEffects.None, 0f);

        }



        // Original: color fades from 0xFF000000 to 0xFFFFFFFF (expoOut over 0.6s)

        byte tintVal = (byte)(Math.Min(1f, _bgRevealTint) * 255f);

        var revealColor = new Color(tintVal, tintVal, tintVal);

        // During confirm, BG dims via _bgDimColor

        Color tintColor = _phase == Phase.Confirmed

            ? new Color(

                (byte)(revealColor.R * _bgDimColor.R / 255),

                (byte)(revealColor.G * _bgDimColor.G / 255),

                (byte)(revealColor.B * _bgDimColor.B / 255))

            : revealColor;



        sb.Draw(img,

            new Vector2(bgImageX, CsDy(-100f)), null, tintColor,

            0f, Vector2.Zero, scale, SpriteEffects.None, 0f);

    }



    private void DrawPinkBack(SpriteBatch sb)

    {

        if (PINK_PANEL_W <= 0) return;

        float pinkX = _pinkSlideX + _exitPinkOffset;

        if (_pinkBack == null || _pinkBack == Assets.Pixel)

        {

            sb.Draw(Assets.Pixel,

                new Rectangle((int)pinkX, 0, (int)PINK_PANEL_W, FNFGame.SCREEN_HEIGHT),

                _pinkColor);

            return;

        }

        float scaleX = PINK_PANEL_W / _pinkBack.Width;

        float scaleY = (float)FNFGame.SCREEN_HEIGHT / _pinkBack.Height;

        float s = Math.Max(scaleX, scaleY);

        sb.Draw(_pinkBack,

            new Vector2(pinkX, CsDy(-100f)), null,

            _pinkColor,

            0f, Vector2.Zero, s, SpriteEffects.None, 0f);

    }



    private void DrawBGAngleMask(SpriteBatch sb)

    {

        // Keep disabled: the backing image art already contains the intended split/mask.

        // Drawing an extra runtime mask over-darkens Freeplay and causes the screenshot bug.

        return;

    }



    private Texture2D CreateMaskedBGImage(GraphicsDevice gd, Texture2D source)

    {

        if (source == null || source == Assets.Pixel) return source;

        int w = source.Width;

        int h = source.Height;

        var result = new Texture2D(gd, w, h);

        var data = new Color[w * h];

        source.GetData(data);



        // Original AngleMask shader: diagonal cut from (0,0) to (90, textureHeight).

        // Pixels to the right of the line (texX > texY * 90 / texHeight) are kept;

        // pixels to the left are transparent. This makes the bottom-right (Pico) opaque

        // and the top-left transparent, matching the original.

        for (int y = 0; y < h; y++)

        {

            float cutX = y * 90f / h;

            int row = y * w;

            for (int x = 0; x < w; x++)

            {

                float fade = x < cutX - 4 ? 0f : x > cutX + 4 ? 1f : (x - (cutX - 4)) / 8f;

                fade = Math.Clamp(fade, 0f, 1f);

                var c = data[row + x];

                c.A = (byte)(c.A * fade);

                data[row + x] = c;

            }

        }



        result.SetData(data);

        return result;

    }



    private Texture2D CreateMaskedBlackOverlay(GraphicsDevice gd, Texture2D source)

    {

        if (source == null || source == Assets.Pixel) return null;

        int w = source.Width;

        int h = source.Height;

        var result = new Texture2D(gd, w, h);

        var data = new Color[w * h];



        // Same diagonal mask as the background image, so the black overlay sits

        // exactly where the BG image is opaque (the character / bottom-right area).

        for (int y = 0; y < h; y++)

        {

            float cutX = y * 90f / h;

            int row = y * w;

            for (int x = 0; x < w; x++)

            {

                float fade = x < cutX - 4 ? 0f : x > cutX + 4 ? 1f : (x - (cutX - 4)) / 8f;

                fade = Math.Clamp(fade, 0f, 1f);

                data[row + x] = new Color((byte)0, (byte)0, (byte)0, (byte)(255 * fade));

            }

        }



        result.SetData(data);

        return result;

    }



    private static Texture2D CreateFreeplayAngleMask(GraphicsDevice gd)

    {

        int w = FNFGame.SCREEN_WIDTH;

        int h = FNFGame.SCREEN_HEIGHT;



        var tex = new Texture2D(gd, w, h, false, SurfaceFormat.Color);

        var data = new Color[w * h];



        float baseX = w * 0.55f;

        float slope = 0.35f;

        float midY = h * 0.5f;



        for (int y = 0; y < h; y++)

        {

            float cutX = baseX - (y - midY) * slope;

            int row = y * w;

            for (int x = 0; x < w; x++)

            {

                float a = x < cutX - 2 ? 1f : x > cutX + 2 ? 0f : 1f - (x - (cutX - 2)) / 4f;

                byte alpha = (byte)(Math.Clamp(a, 0f, 1f) * 255f);

                data[row + x] = new Color((byte)0, (byte)0, (byte)0, alpha);

            }

        }



        tex.SetData(data);

        return tex;

    }



    private void DrawEmptyState(SpriteBatch sb)

    {

        var font = Assets.GetFont(24);

        if (font == null) return;



        const string text = "No songs found";

        var sz = font.MeasureString(text);

        float x = (FNFGame.SCREEN_WIDTH - sz.X) / 2f;

        float y = (FNFGame.SCREEN_HEIGHT - sz.Y) / 2f;

        font.DrawText(sb, text, new Vector2(x, y), Color.White);

    }



    private void DrawOrangeBar(SpriteBatch sb)

    {

        if (PINK_PANEL_W <= 0 || !_orangeBarsVisible) return;

        float pinkX = _pinkSlideX + _exitPinkOffset;

        int barY = 440 + (int)MathF.Round(CsDy(-40f)); // exitMoversCharSel orangeBackShit/alsoOrangeLOL y -40

        int barH = 75;



        if (_isPicoTheme)

        {

            // Pico uses blueBar drawn in DrawPicoBackingCardLayers at the correct layer order.

            return;

        }



        // Non-Pico orange accent bar:

        {

            // Original: orangeBackShit at (84, 440) width=pinkBack.width, height=75.

            // The AngleMask diagonal is applied by the black overlay drawn later. Extend the bar

            // to the diagonal cut so it looks correct where the mask is transparent.

            float maskWidth = ANGLE_MASK_BASE_X - (barY - ANGLE_MASK_MID_Y) * ANGLE_MASK_SLOPE;

            maskWidth = Math.Min(maskWidth, PINK_PANEL_W);



            float barX = pinkX + 84f;

            float barW = Math.Min(PINK_PANEL_W - 84f, maskWidth - 84f);

            if (barW > 0)

                sb.Draw(Assets.Pixel,

                    new Rectangle((int)barX, barY, (int)barW, barH),

                    new Color(254, 218, 0));

            // alsoOrangeLOL at (0, 440) width=100

            float alsoW = Math.Min(100f, maskWidth);

            sb.Draw(Assets.Pixel,

                new Rectangle((int)pinkX, barY, (int)alsoW, barH),

                new Color(255, 212, 0));

        }



        // (no character-name label: backcard-bf.hxc orangeBackShit/alsoOrangeLOL carry no text)

    }



    private void DrawPicoBackingCardLayers(SpriteBatch sb)

    {

        if (!_isPicoTheme || !_introDone) return;



        float pinkX = _pinkSlideX + _exitPinkOffset;



        // Original add order: scrollBack, scrollLower, blueBar, scrollTop, scrollMiddle, glowDark, glow.

        // scrollBack (lowerLoop): y=200, flipX, alpha=0.39, velocity=110

        DrawPicoLoopTexture(sb, _picoLowerLoop, pinkX, 200f, _picoScrollBackX, 0.39f, flipX: true);



        // scrollLower (lowerLoop): y=406, velocity=-110

        DrawPicoLoopTexture(sb, _picoLowerLoop, pinkX, 406f, _picoScrollLowerX, 1f, flipX: false);



        // blueBar: y=239, blend=9 (MULTIPLY), alpha=0.4

        DrawPicoBlueBar(sb, pinkX);



        // scrollTop: y=80, velocity=-220

        DrawPicoTopLoop(sb, pinkX, 80f, _picoScrollTopX);



        // scrollMiddle (middleLoop): y=346, velocity=220

        DrawPicoLoopTexture(sb, _picoMiddleLoop, pinkX, 346f, _picoScrollMiddleX, 1f, flipX: false);



        // glowDark: x=-300, y=330, blend=9 (MULTIPLY)

        DrawPicoGlow(sb, pinkX, isGlowDark: true);



        // glow: x=-300, y=330, blend=0 (ADD)

        DrawPicoGlow(sb, pinkX, isGlowDark: false);

    }



    private string FindPicoConfirmAnim(SpriteSheet sheet)

    {

        if (sheet == null) return null;

        foreach (var name in new[] { "pico confirm card", "Pico Back Card Confirm" })

        {

            if (sheet.Animations.ContainsKey(name))

                return name;

        }

        return sheet.Animations.Keys.FirstOrDefault();

    }



    private void DrawPicoConfirmAtlas(SpriteBatch sb)
    {
        if (!_picoConfirmVisible || _picoCard == null) return;
        int frame = Math.Min(_picoCard.FrameCount - 1, (int)(_picoConfirmTime * 24f));   // Animation.json FRT = 24
        _picoCard.Draw(sb, frame, new Vector2(CutoutWidth * DJ_POS_MULTI - 120f, 55f));
    }



    private static void LogPicoConfirm(string msg)

    {

        try

        {

            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pico_confirm_debug.log");

            File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}");

        }

        catch { }

    }



    private SpriteFrame GetPicoTopLoopFrame()

    {

        if (_picoTopLoopSheet == null)

            return null;



        var frames = _picoTopLoopSheet.GetAnimation(_picoTopLoopAnimName);

        if (frames == null || frames.Count == 0)

            frames = _picoTopLoopSheet.GetAnimation("base");

        if (frames == null || frames.Count == 0)

            return null;



        int idx = Math.Clamp(_picoTopLoopFrameIndex, 0, frames.Count - 1);

        return frames[idx];

    }



    private string PickNextPicoTopLoopAnim()

    {

        if (_picoTopLoopFrameWidth <= 0) return "base";



        // Original scrollProgress uses the authored frame width: Math.abs(scrollTop.x % (frameWidth + 20))

        float logicalScrollProgress = Math.Abs(_picoScrollTopX % (_picoTopLoopFrameWidth + 20));

        if (logicalScrollProgress > 500 && logicalScrollProgress < 700)

            return "sniper info";

        if (logicalScrollProgress >= 700 && logicalScrollProgress < 1300)

            return "rifle info";

        if (logicalScrollProgress >= 1450 && logicalScrollProgress < 2000)

            return "rocket launcher info";

        if (logicalScrollProgress >= 0 && logicalScrollProgress < 300)

            return "uzi info";

        return "base";

    }



    private void DrawPicoLoopTexture(SpriteBatch sb, Texture2D texture, float pinkX, float y, float scrollX, float alpha, bool flipX)

    {

        if (texture == null || texture == Assets.Pixel)

            return;



        // Original FlxBackdrop draws at authored pixel size (scale=1.0) and tiles horizontally.

        float drawWidth = texture.Width;



        float wrapped = scrollX % drawWidth;

        if (wrapped < 0) wrapped += drawWidth;



        sb.Draw(texture,

            new Vector2(pinkX + wrapped - drawWidth, y),

            null,

            Color.White * alpha,

            0f,

            Vector2.Zero,

            1f,

            flipX ? SpriteEffects.FlipHorizontally : SpriteEffects.None,

            0f);

        sb.Draw(texture,

            new Vector2(pinkX + wrapped, y),

            null,

            Color.White * alpha,

            0f,

            Vector2.Zero,

            1f,

            flipX ? SpriteEffects.FlipHorizontally : SpriteEffects.None,

            0f);

    }



    private void DrawPicoTopLoop(SpriteBatch sb, float pinkX, float y, float scrollX)

    {

        if (_picoTopLoopSheet?.Texture == null) return;

        var frame = GetPicoTopLoopFrame();

        if (frame == null) return;



        // Original: topLoop frames are played at authored size (scale=1.0).

        float scale = 1f;

        float drawWidth = frame.SourceRect.Width;



        float wrapped = scrollX % drawWidth;

        if (wrapped < 0) wrapped += drawWidth;



        sb.Draw(_picoTopLoopSheet.Texture,

            new Vector2(pinkX + wrapped - drawWidth, y) + frame.Offset,

            frame.SourceRect,

            Color.White,

            0f,

            Vector2.Zero,

            scale,

            SpriteEffects.None,

            0f);

        sb.Draw(_picoTopLoopSheet.Texture,

            new Vector2(pinkX + wrapped, y) + frame.Offset,

            frame.SourceRect,

            Color.White,

            0f,

            Vector2.Zero,

            scale,

            SpriteEffects.None,

            0f);

    }



    private void DrawPicoBlueBar(SpriteBatch sb, float pinkX)

    {

        if (_picoBlueBar == null || _picoBlueBar == Assets.Pixel) return;



        // Original: blend=9 (MULTIPLY), alpha=0.4, position (0, 239), authored 512x256 size.

        sb.End();

        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: MultiplyBlend);



        sb.Draw(_picoBlueBar,

            new Vector2(pinkX, 239f),

            null,

            Color.White * 0.4f,

            0f,

            Vector2.Zero,

            1f,

            SpriteEffects.None,

            0f);



        sb.End();

        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);

    }



    private void DrawPicoGlow(SpriteBatch sb, float pinkX, bool isGlowDark)

    {

        if (_picoGlow == null || _picoGlow == Assets.Pixel) return;



        float alpha = isGlowDark ? _picoGlowDarkAlpha : _picoGlowAlpha;

        if (alpha <= 0) return;



        // Original: glowDark uses blend=9 (MULTIPLY), glow uses blend=0 (ADD).

        sb.End();

        sb.Begin(samplerState: SamplerState.LinearClamp,

            blendState: isGlowDark ? MultiplyBlend : BlendState.Additive);



        // Original position: (CUTOUT_WIDTH * DJ_POS_MULTI) + -300.
        // With cutout at 0, the authored 1280x720 glow position is x = -300.

        float glowX = -300f;

        sb.Draw(_picoGlow,

            new Vector2(glowX, 330f),

            null,

            Color.White * alpha,

            0f,

            Vector2.Zero,

            1f,

            SpriteEffects.None,

            0f);



        sb.End();

        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);

    }



    private void DrawCardGlow(SpriteBatch sb)

    {

        if (_cardGlow == null || _cardGlow == Assets.Pixel || _cardGlowAlpha <= 0) return;

        // Original: cardGlow at (-30, -30), fades alpha 1->0, scale 1->1.2

        float s = _cardGlowScale;

        sb.Draw(_cardGlow,

            new Vector2(-30f, -30f), null,

            Color.White * _cardGlowAlpha,

            0f, Vector2.Zero, s, SpriteEffects.None, 0f);

    }



    private void DrawDJ(SpriteBatch sb)

    {

        if (_djSprite == null) return;

        _djSprite.EnsureCurrentFrameRendered(Game.GraphicsDevice);



        Vector2 off;

        if (_djOffsets != null && _djOffsets.TryGetValue(_djSprite.CurrentAnimation, out off))

        {

            // Use the current animation's explicit offset from the original player data.

        }

        else

        {

            off = _djOffsets[_djIdleAnim];

        }



        // The pre-rendered composite atlas uses CompositeBaseOrigin as the origin for all frames.

        // Setting the sprite position directly to the animation offset places the frame content

        // at the same screen position as the original FNF (position - offset) drawPoint.

        _djSprite.Position = new Vector2(off.X + _exitDjOffset, off.Y + CsDy(-175f)); // exitMoversCharSel dj y -175



        _djSprite.Scale = new Vector2(DJ_SCALE, DJ_SCALE);

        _djSprite.Draw(sb);

    }



    private bool _backingTextLoggedOnce;

    private void DrawBackingTextYeah(SpriteBatch sb)

    {
        // BackingCard.backingTextYeah at (CUTOUT*0.44 - 320, 120): flixel-animate replay (MovieClips frozen on frame 0).
        if (_yeahCard != null)
        {
            if (_yeahTime < 0f || _phase != Phase.Confirmed) return;
            sb.End();
            int fr = Math.Min(_yeahCard.FrameCount - 1, (int)(_yeahTime * 24f));
            _yeahCard.Draw(sb, fr, new Vector2(CutoutWidth * DJ_POS_MULTI - 320f + _exitPinkOffset, 120f));
            sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.AlphaBlend);
            return;
        }

        if (_backingTextSprite == null) return;

        // Only draw when animation is actively playing (confirm state)

        if (string.IsNullOrEmpty(_backingTextSprite.CurrentAnimation)) return;

        _backingTextSprite.EnsureCurrentFrameRendered(Game.GraphicsDevice);



        // Original FNF: backingTextYeah created at x = CUTOUT_WIDTH * DJ_POS_MULTI - 320.

        // CUTOUT_WIDTH ˜ 853 at 1280px wide ? x ˜ 55. FlxAnimate then applies the symbol's

        // internal M3D translations (large negative tx values like -878) which are baked into

        // our PreRenderComposite as origin. AnimatedSprite.Draw subtracts origin from Position,

        // so we add it back to land the visible glyphs at the intended screen position.

        // BackingCard.hx: FunkinSprite.createTextureAtlas((CUTOUT_WIDTH * DJ_POS_MULTI) + -320, 120): the
        // timeline box is drawn at (x, y). CUTOUT_WIDTH is 0 on a 16:9 screen (the old 853 guess put it 375px right).
        Vector2 anchor = new Vector2(CutoutWidth * DJ_POS_MULTI - 320f + _exitPinkOffset, 120f);
        var origin = (Vector2?)null;
        _backingTextSprite.Position = anchor;

        _backingTextSprite.Scale = Vector2.One;



        if (!_backingTextLoggedOnce)

        {

            _backingTextLoggedOnce = true;

            string anim = _backingTextSprite.CurrentAnimation;

            int fi = _backingTextSprite.FrameIndex;

            bool hasCmp = _backingTextSheet?.CompositeAnimations?.ContainsKey(anim) ?? false;

            int cnt = hasCmp ? _backingTextSheet.CompositeAnimations[anim].Count : 0;

            Console.WriteLine($"[BACKING] FIRST DRAW: anim='{anim}' frame={fi} hasComp={hasCmp} cnt={cnt} origin={origin} finalPos={anchor}");

            if (hasCmp && cnt > 0)

            {

                var (tex, rect, co) = _backingTextSheet.CompositeAnimations[anim][fi % cnt];

                Console.WriteLine($"[BACKING] tex={tex.Width}x{tex.Height} disposed={tex.IsDisposed} rect={rect} compOrigin={co}");

            }

        }



        _backingTextSprite.Draw(sb);

    }



    private void InitScrollingText()

    {

        // EXACT values from backcard-bf.hxc (BoyfriendCard scripted backing card):

        //

        // funnyScroll  = new BGScrollingText(0, 220, text1, FlxG.width/2, false, 60); speed=-3.8; color=0xFFFF9963

        // funnyScroll2 = new BGScrollingText(0, 335, text1, FlxG.width/2, false, 60); speed=-3.8; color=0xFFFF9963

        // moreWays     = new BGScrollingText(0, 160, text2, FlxG.width,   true,  43); speed= 6.8; color=0xFFFFF383

        // moreWays2    = new BGScrollingText(0, 397, text2, FlxG.width,   true,  43); speed= 6.8; color=0xFFFFF383

        // txtNuts      = new BGScrollingText(0, 285, text3, FlxG.width/2, true,  43); speed= 3.5; color=white

        // funnyScroll3 = new BGScrollingText(0, 450, text1, FlxG.width/2, false, 60); speed=-3.8; color=0xFFFEA400

        //

        // text1 = "BOYFRIEND" (getFreeplayDJText(1))

        // text2 = "HOT BLOODED IN MORE WAYS THAN ONE" (getFreeplayDJText(2))

        // text3 = "PROTECT YO NUTS" (getFreeplayDJText(3))



        string text1 = DjTextFor(1, _isPicoTheme ? "PICO" : "BOYFRIEND");

        string text2 = DjTextFor(2, _isPicoTheme ? "GOD DAMN HE DOWN ON THE NUT" : "HOT BLOODED IN MORE WAYS THAN ONE");

        string text3 = DjTextFor(3, _isPicoTheme ? "ZEBOIM DAMN IMA NUT" : "PROTECT YO NUTS");



        // Original widthShit values:

        // FlxG.width = 1280 for full-width rows, FlxG.width/2 = 640 for half-width rows

        float fullWidth = 1280f;

        float halfWidth = 640f;



        // Exact colors from source

        Color cFFF383 = new Color(0xFF, 0xF3, 0x83); // moreWays, moreWays2

        Color cFF9963 = new Color(0xFF, 0x99, 0x63); // funnyScroll, funnyScroll2

        Color cFEA400 = new Color(0xFE, 0xA4, 0x00); // funnyScroll3

        Color cWhite  = Color.White;                   // txtNuts (no color set = white)



        // The 5by7 TTF in this port renders bigger than HaxeFlixel's OpenFL rasterizer for

        // the same pt value. Scale the original source sizes (43, 60) down to match visually.

        int sz43 = 43; // BGScrollingText size (em), FontStash size = em * 0.7 (FreeplayScene.BfCard.cs)

        int sz60 = 60;



        // (text, y, widthShit, bold, fontSize, speed, color)

        var rows = new (string text, float y, float widthShit, bool bold, int fontSize, float speed, Color color)[]

        {

            (text2, 160f, fullWidth, true,  sz43, 6.8f,  cFFF383),  // moreWays

            (text1, 220f, halfWidth, false, sz60, -3.8f, cFF9963),  // funnyScroll

            (text3, 285f, halfWidth, true,  sz43, 3.5f,  cWhite),   // txtNuts

            (text1, 335f, halfWidth, false, sz60, -3.8f, cFF9963),  // funnyScroll2

            (text2, 397f, fullWidth, true,  sz43, 6.8f,  cFFF383),  // moreWays2

            (text1, 450f, halfWidth, false, sz60, -3.8f, cFEA400),  // funnyScroll3

        };



        _scrollingTextRows = new ScrollingTextRow[rows.Length];

        for (int i = 0; i < rows.Length; i++)

        {

            var (text, y, wShit, bold, fontSize, speed, color) = rows[i];

            var font = BfCardFont(fontSize, bold);

            float frameWidth = 200f; // fallback

            if (font != null)

            {

                var sz = font.MeasureString(text);

                frameWidth = (float)Math.Ceiling(sz.X) + 4f; // FlxText frameWidth = textWidth + 2 * GUTTER

            }



            // Original: needed = Math.ceil(widthShit / frameWidth) + 1

            int needed = (int)Math.Ceiling(wShit / frameWidth) + 1;



            // Original: positions[i] = (i * frameWidth) + (i * placementOffset)

            // placementOffset = 20

            float placementOffset = 20f;

            float[] positions = new float[needed];

            for (int j = 0; j < needed; j++)

                positions[j] = (j * frameWidth) + (j * placementOffset);



            _scrollingTextRows[i] = new ScrollingTextRow

            {

                Text = text,

                Y = y,

                Speed = speed,

                Color = color,

                FontSize = fontSize,

                Bold = bold,

                Positions = positions,

                TextWidth = frameWidth

            };

        }

    }



    private void UpdateScrollingText(float dt)

    {

        if (_scrollingTextRows == null || !_scrollingTextVisible) return;



        for (int i = 0; i < _scrollingTextRows.Length; i++)

        {

            ref var row = ref _scrollingTextRows[i];

            // In the original, frameWidth is the measured text width (not including spacing).

            float frameWidth = row.TextWidth - 20f;

            const float placementOffset = 20f;



            // Original: txtPosition.x -= 1 * (speed * (elapsed / (1 / 60)))

            float move = row.Speed * (dt / (1f / 60f));



            for (int j = 0; j < row.Positions.Length; j++)

            {

                row.Positions[j] -= move;



                if (row.Speed > 0) // Going left

                {

                    // Original: if (txtPosition.x < -frameWidth)

                    //   txtPosition.x = _textPositions[last].x + frameWidth + placementOffset

                    if (row.Positions[j] < -frameWidth)

                    {

                        float max = float.MinValue;

                        for (int k = 0; k < row.Positions.Length; k++)

                            if (row.Positions[k] > max) max = row.Positions[k];

                        row.Positions[j] = max + frameWidth + placementOffset;

                        SortPositions(row.Positions);

                    }

                }

                else // Going right

                {

                    // Original: if (txtPosition.x > frameWidth * 2)

                    //   txtPosition.x = _textPositions[0].x - frameWidth - placementOffset

                    if (row.Positions[j] > frameWidth * 2f)

                    {

                        float min = float.MaxValue;

                        for (int k = 0; k < row.Positions.Length; k++)

                            if (row.Positions[k] < min) min = row.Positions[k];

                        row.Positions[j] = min - frameWidth - placementOffset;

                        SortPositions(row.Positions);

                    }

                }

            }

        }

    }



    private static void SortPositions(float[] positions)

    {

        System.Array.Sort(positions);

    }



    private void UpdatePicoBackingCardLayers(float dt)

    {

        if (!_isPicoTheme || !_introDone) return;



        _picoScrollBackX += 110f * dt;

        _picoScrollLowerX -= 110f * dt;

        _picoScrollTopX -= 220f * dt;

        _picoScrollMiddleX += 220f * dt;



        // Beat-driven glow pulse: original beatHit() sets glow.alpha=1 -> 0 over 16/24s,
        // glowDark.alpha=0 -> 1 over 18/24s, both with quartOut.
        if (_picoGlowConductor != null)
        {
            _picoGlowConductor.Update(dt);
            if (_picoGlowConductor.CurrentBeat != _picoGlowLastBeat)
            {
                _picoGlowLastBeat = _picoGlowConductor.CurrentBeat;
                _picoGlowPulsing = true;
                _picoGlowTweenTimer = 0f;
                _picoGlowAlpha = 1f;
                _picoGlowDarkAlpha = 0f;
            }
        }

        if (_picoGlowPulsing)
        {
            _picoGlowTweenTimer += dt;
            float glowT = Math.Clamp(_picoGlowTweenTimer / (16f / 24f), 0f, 1f);
            float darkT = Math.Clamp(_picoGlowTweenTimer / (18f / 24f), 0f, 1f);
            float quartOutGlow = 1f - MathF.Pow(1f - glowT, 4f);
            float quartOutDark = 1f - MathF.Pow(1f - darkT, 4f);
            _picoGlowAlpha = 1f - quartOutGlow;
            _picoGlowDarkAlpha = quartOutDark;
            if (glowT >= 1f && darkT >= 1f)
            {
                _picoGlowPulsing = false;
                _picoGlowAlpha = 0f;
                _picoGlowDarkAlpha = 1f;
            }
        }



        // TopLoop scroll-driven animation: 24 fps, non-looping.

        if (_picoTopLoopSheet != null)

        {

            _picoTopLoopAnimTime += dt;

            const int fps = 24;

            var animFrames = _picoTopLoopSheet.GetAnimation(_picoTopLoopAnimName);

            if (animFrames != null && animFrames.Count > 0)

            {

                int newIndex = (int)(_picoTopLoopAnimTime * fps);

                if (newIndex >= animFrames.Count)

                {

                    _picoTopLoopAnimTime = 0f;

                    newIndex = 0;

                    string prevAnim = _picoTopLoopAnimName;

                    _picoTopLoopAnimName = PickNextPicoTopLoopAnim();

                    if (_picoTopLoopAnimName != prevAnim)

                    {

                        // Animation changed; nothing else to log.

                    }

                }

                _picoTopLoopFrameIndex = newIndex;

            }

        }

    }



    private void DrawScrollingText(SpriteBatch sb)

    {

        if (_scrollingTextRows == null || !_scrollingTextVisible || _isPicoTheme) return;



        float pinkX = _pinkSlideX + _exitPinkOffset;



        for (int i = 0; i < _scrollingTextRows.Length; i++)

        {

            ref var row = ref _scrollingTextRows[i];

            var font = Assets.GetNamedFont("5by7", row.FontSize) ?? Assets.GetFont(row.FontSize);

            if (font == null) continue;



            for (int j = 0; j < row.Positions.Length; j++)

            {

                float drawX = pinkX + row.Positions[j];

                if (drawX >= FNFGame.SCREEN_WIDTH) continue;

                if (drawX + row.TextWidth < 0) continue;

                font.DrawText(sb, row.Text, new Vector2(drawX, row.Y), row.Color);

            }

        }

    }



    private void DrawCapsules(SpriteBatch sb)

    {

        if (_capsuleSheet?.Texture == null) return;



        List<Action> additiveItems = new List<Action>();



        int visibleRange = 8;

        for (int offset = -3; offset <= visibleRange; offset++)

        {

            int songIdx = _selectedIndex + offset;

            if (songIdx < 0 || songIdx >= _songs.Count) continue;

            bool isSelected = songIdx == _selectedIndex;

            // funnyCam pass skips capsuleToRank while it is on rankCamera; the rankCamera pass draws only it
            if (_drawingRankCam ? !isSelected : (isSelected && _rankCap != null)) continue;



            // SongMenuItem x/y (per-capsule jump-in + smoothLerp towards targetPos, FreeplayScene.RankAnim.cs)
            var capState = Cap(songIdx);
            float capsuleY = capState.Y; if (Math.Abs(offset) < 6) capsuleY += CsDy(-250f);   // transitionToCharSelect: |index-curSelected|-1 < 5
            float capsuleX = capState.X + _exitCapsuleOffset;



            // Skip off-screen

            if (capsuleY < -CAPSULE_FRAME_H * CAPSULE_SCALE - 20 || capsuleY > FNFGame.SCREEN_HEIGHT + 50) continue;



            var frames = isSelected ? _capsuleSelected : _capsuleUnselected;

            if (frames == null || frames.Count == 0) continue;



            // Use a static capsule frame (original freeplay uses a fixed capsule)

            var frame = frames[0];

            var song = _songs[songIdx];

            float groupAlpha = song.IsRandom ? 0.5f : 1f;   // initRandom(): alpha = 0.5 (whole group)
            float alpha = groupAlpha;

            // Original: initRandom() sets alpha=0.5, hides songText/favIcon/ranking

            if (song.IsRandom)

            {

                /* alpha already 0.5 */

                // initRandom() y = intendedY(0) + 10 is only the start position; the target is the normal formula

            }



            // Original: grayscaleShader.setAmount(isSelected ? 0 : 0.8) — desaturate unselected capsules

            // Simulate 80% grayscale by lerping color toward gray

            Color capsuleTint = new Color(Color.White, groupAlpha);



            // Draw capsule sprite.
            // During the post-song rank animation the selected capsule is centered and zoomed.
            Vector2 pos;
            Vector2 scale;
            float capsuleAngle = 0f;
            float activeCapsuleScale = CAPSULE_SCALE;
            {
                pos = new Vector2(capsuleX, capsuleY);
                scale = new Vector2(CAPSULE_SCALE * capState.SX, CAPSULE_SCALE * capState.SY);
                capsuleAngle = MathHelper.ToRadians(capState.BodyAngle);
                if (_jumpOutBeat > 0)
                {
                    // SongMenuItem.doJumpOut: x = FlxG.width * xPosOutLerpLol[..], capsule.scale = (xF, 1/xF) * realScaled
                    float xf = JumpOutXFrames[_jumpOutBeat - 1];
                    pos.X = FNFGame.SCREEN_WIDTH * JumpOutXPos[Math.Min(_jumpOutBeat - 1, JumpOutXPos.Length - 1)];
                    scale = new Vector2(xf * CAPSULE_SCALE, CAPSULE_SCALE / xf);
                }
            }

            // Children of FlxSpriteGroup are NOT scaled by capsule.scale and stay at

            // their local coords relative to group.x/y (which we treat as `pos`).

            float untrimmedW = frame.FrameWidth > 0 ? frame.FrameWidth : frame.SourceRect.Width;

            float untrimmedH = frame.FrameHeight > 0 ? frame.FrameHeight : frame.SourceRect.Height;

            Vector2 untrimmedOrigin = new Vector2(untrimmedW * 0.5f, untrimmedH * 0.5f);

            // Original: capsule.offset.x = selected ? 0 : -5 (visible shift, capsule only)

            float capsuleOffsetX = isSelected ? 0f : -5f;

            Vector2 capsuleDrawPos = pos

                + untrimmedOrigin * (Vector2.One - scale)

                + frame.Offset * scale

                + new Vector2(capsuleOffsetX, 0f);

            // capsule.capsule: IntervalShake offset + angle about the frame centre (flixel origin)
            capsuleDrawPos += capState.BodyOff;
            Vector2 bodyOrigin = untrimmedOrigin - frame.Offset;
            sb.Draw(_capsuleSheet.Texture,
                capsuleDrawPos + (untrimmedOrigin - frame.Offset) * scale,
                frame.SourceRect, capsuleTint,
                capsuleAngle, bodyOrigin, scale, SpriteEffects.None, 0f);



            if (song.IsRandom)
            {
                // initRandom(): group alpha 0.5 sticks on bpm/difficulty labels + numbers; songText becomes visible
                // again with the jump-in (setVisibleGrp(true)) at alpha 1/0.6; no icon, rank or fav.
                DrawCapsuleText(sb, song, pos, isSelected, isSelected ? 1f : 0.6f, 1f, 0);
                DrawCapsuleDetails(sb, song, pos, 0.5f, 1f);
                continue;
            }



            // All children of the FlxSpriteGroup share `pos` (group origin) as their

            // anchor; capsule scale does NOT cascade to them in original code.

            // During the rank animation the rankCamera zooms the whole group, so children

            // are scaled and positioned around the same origin the capsule body uses.

            Vector2 childAnchor;
            float childZoom;
            childAnchor = pos;
            childZoom = 1f;



            // Draw song name text on capsule (with flicker on confirm)

            // CapsuleText.flickerText(): 19 ticks of 1/24 s alternating (DDDDDD text / glowColor) and (ADD white text+glow);
            // the text is never hidden and keeps blend ADD after tick 2 (never reset).
            int flick = (_capsuleFlicker && isSelected) ? Math.Min(19, (int)(_flickerTimer * 24f)) : 0;
            DrawCapsuleText(sb, song, childAnchor, isSelected, (isSelected ? 1f : 0.6f) * groupAlpha, childZoom, flick);



            // Draw pixel icon on capsule (original: plays 'confirm' anim on song confirm)

            DrawPixelIcon(sb, song, childAnchor, alpha, _phase == Phase.Confirmed && isSelected, childZoom);



            // Draw capsule detail info (BPM, difficulty rating, week type, NEW badge)

            DrawCapsuleDetails(sb, song, childAnchor, alpha, childZoom);



             // Draw fav heart if song is favorited (original: favIcon at x=405 default, x=370 if rank visible)

            if (HighscoreManager.Data.FavoriteSongs.Contains(song.Name) && _favHeartFrames != null && _favHeartFrames.Count > 0

                && _favHeartSheet?.Texture != null)

            {

                int fi = (int)((uint)Environment.TickCount / 83u % (uint)_favHeartFrames.Count);

                var hf = _favHeartFrames[fi];

                string favDiff = GetCurrentDifficulty(song);

                bool hasRank = !string.IsNullOrEmpty(HighscoreManager.GetRank(song.Name, favDiff));

                float favX = hasRank ? 370f : 405f;

                additiveItems.Add(() =>

                {

                    sb.Draw(_favHeartSheet.Texture,

                        childAnchor + new Vector2(favX, 40f) * childZoom + hf.Offset * childZoom,

                        hf.SourceRect, new Color(Color.White, alpha),

                        0f, Vector2.Zero, childZoom, SpriteEffects.None, 0f);

                });

            }



            // Draw rank badge (original: ranking at (555, 40))

            Action drawRank = RankBadgeOverride(sb, childAnchor, isSelected, additiveItems) ? null
                : GetRankBadgeAction(sb, song, childAnchor, isSelected ? 1f : 0.7f, null, null, null, childZoom);

            if (drawRank != null)

                additiveItems.Add(drawRank);

        }



        if (additiveItems.Count > 0)

        {

            sb.End();

            sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.Additive);

            foreach (var action in additiveItems) action();

            sb.End();

            sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.NonPremultiplied);

        }

    }



    private void DrawDebugMenu(SpriteBatch sb)

    {

        if (!_debugMenuVisible) return;



        var font = Assets.GetFont(16);

        if (font == null) return;



        var mouse = Mouse.GetState();

        var mousePoint = new Point(mouse.X, mouse.Y);



        sb.End();

        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.AlphaBlend);



        // Semi-transparent background panel

        int panelH = _debugMenuItems.Count * 30 + 4;

        sb.Draw(Assets.Pixel, new Rectangle(8, 8, 224, panelH), Color.Black * 0.75f);



        foreach (var item in _debugMenuItems)

        {

            bool hover = item.Bounds.Contains(mousePoint);

            Color bg = hover ? Color.DarkSlateGray : Color.DimGray;

            sb.Draw(Assets.Pixel, item.Bounds, bg * 0.85f);

            font.DrawText(sb, item.Label, new Vector2(item.Bounds.X + 6, item.Bounds.Y + 4), Color.White);

        }



        sb.End();

        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);

    }



    private void DrawCapsuleText(SpriteBatch sb, FreeplaySong song, Vector2 capsulePos, bool selected, float alpha, float childZoom = 1f, int flick = 0)

    {

        string displayName = GetCapsuleSongName(song);

        // Original SongMenuItem.hx CapsuleText: position (capsule.width*0.26, 45),

        // font "5by7". 24 looks closest to the original visual size.

        const int FontSize = 24;

        var font = Assets.GetNamedFont("5by7", FontSize) ?? Assets.GetFont(FontSize);

        if (font == null) return;



        float textX = capsulePos.X + 159f * childZoom;

        float textY = capsulePos.Y + 45f * childZoom;

        string clipped = displayName;   // CapsuleText: clipRect(offset.x, 0, clipWidth, h), no ellipsis

        var pos = new Vector2(textX, textY);
        float clipW = CapsuleClipWidth(song) * childZoom, textW = font.MeasureString(displayName).X * childZoom;
        bool clipOn = textW > clipW;
        if (clipOn) { pos.X -= selected ? CapsuleTextScroll(textW - clipW) : 0f; BeginTextClip(sb, textX, textY - 20f * childZoom, clipW, 80f * childZoom); }



        if (selected)

        {

            Color outline = new Color((flick >= 2 && flick % 2 == 0) ? Color.White : new Color(0, 204, 255), alpha);

            for (int dx = -1; dx <= 1; dx++)

            {

                for (int dy = -1; dy <= 1; dy++)

                {

                    if (dx == 0 && dy == 0) continue;

                    font.DrawText(sb, new StringSegment(clipped),

                        pos + new Vector2(dx, dy) * childZoom, outline, 0f, Vector2.Zero, Vector2.One * childZoom);

                }

            }

        }



        font.DrawText(sb, new StringSegment(clipped), pos,

            new Color(flick % 2 == 1 ? new Color(0xDD, 0xDD, 0xDD) : Color.White, alpha), 0f, Vector2.Zero, Vector2.One * childZoom);
        if (clipOn) EndTextClip(sb);

    }



    private void DrawPixelIcon(SpriteBatch sb, FreeplaySong song, Vector2 capsulePos, float alpha, bool isConfirmed, float childZoom = 1f)

    {

        var sheet = EnsurePixelIcon(song.OpponentChar);

        if (sheet?.Texture == null) return;



        // Original: on confirm, pixelIcon.animation.play('confirm'), onFinish ? 'confirm-hold'

        if (isConfirmed)

        {

            var confirmFrames = sheet.GetAnimation("confirm");

            if (confirmFrames != null && confirmFrames.Count > 0)

            {

                int fi = (int)(_flickerTimer * 10f); // PixelatedIcon: addByPrefix('confirm', 'confirm0', 10, false)

                if (fi < confirmFrames.Count)

                {

                    DrawPixelIconFrame(sb, sheet.Texture, confirmFrames[fi], capsulePos, alpha, song.OpponentChar, childZoom);

                    return;

                }

                var holdFrames = sheet.GetAnimation("confirm-hold");

                if (holdFrames != null && holdFrames.Count > 0)

                {

                    int hi = Math.Max(0, (int)((_flickerTimer - confirmFrames.Count / 10f) * 10f)) % holdFrames.Count; // confirm-hold, 10 fps loop

                    DrawPixelIconFrame(sb, sheet.Texture, holdFrames[hi], capsulePos, alpha, song.OpponentChar, childZoom);

                    return;

                }

                DrawPixelIconFrame(sb, sheet.Texture, confirmFrames[^1], capsulePos, alpha, song.OpponentChar, childZoom);

                return;

            }

        }



        var idleFrames = sheet.GetAnimation("idle");

        if (idleFrames == null || idleFrames.Count == 0)

        {

            if (sheet.Frames.Count == 0) return;

            var firstFrame = sheet.Frames.Values.First();

            DrawPixelIconFrame(sb, sheet.Texture, firstFrame, capsulePos, alpha, song.OpponentChar, childZoom);

            return;

        }



        DrawPixelIconFrame(sb, sheet.Texture, idleFrames[0], capsulePos, alpha, song.OpponentChar, childZoom);

    }



    private void DrawPixelIconFrame(SpriteBatch sb, Texture2D tex, SpriteFrame frame,

        Vector2 capsulePos, float alpha, string characterId, float childZoom = 1f)

    {

        // Original SongMenuItem.hx: PixelatedIcon at (160, 35), scale = 2.

        // In HaxeFlixel, the sprite scales around its `origin` while keeping the origin

        // point fixed in space. Default origin.y = height/2; origin.x is overridden

        // per-character (100 default, 140 parents-christmas, 195 sserafim-kazuha).

        // The visible top-left of the scaled sprite ends up at:

        //     drawX = x + originX * (1 - scale) = 160 - originX

        //     drawY = y + originY * (1 - scale) = 35  - originY

        // For 32-tall pixel atlas frames, originY = ~16, so drawY ˜ 19.

        float originX = GetPixelIconOriginX(characterId);

        float originY = (frame.FrameHeight > 0 ? frame.FrameHeight : frame.SourceRect.Height) * 0.5f; // centerOrigin() of the full frame (frames setter)

        const float iconScale = 2f;
        float scaledIconScale = iconScale * childZoom;

        float iconX = capsulePos.X + 160f * childZoom + originX * (1f - scaledIconScale);

        float iconY = capsulePos.Y + 35f * childZoom + originY * (1f - scaledIconScale);



        sb.Draw(tex,

            new Vector2(iconX, iconY) + frame.Offset * scaledIconScale,

            frame.SourceRect, new Color(Color.White, alpha),

            0f, Vector2.Zero, scaledIconScale, SpriteEffects.None, 0f);

    }



    private static float GetPixelIconOriginX(string characterId)

    {

        if (string.Equals(characterId, "parents-christmas", StringComparison.OrdinalIgnoreCase))

            return 140f;



        if (string.Equals(characterId, "sserafim-kazuha", StringComparison.OrdinalIgnoreCase))

            return 195f;



        return 100f;

    }



    private void DrawCapsuleDetails(SpriteBatch sb, FreeplaySong song, Vector2 capsulePos, float alpha, float childZoom = 1f)

    {

        // Original SongMenuItem: all child sprites use absolute local positions within FlxSpriteGroup,

        // NOT affected by the capsule graphic's scale (realScaled=0.8). Draw at 1:1 coords.

        const float detailYOffset = 0f;



        // BPM text label at (144, 87), scale 0.9

        if (_bpmText != null && _bpmText != Assets.Pixel)

        {

            float detailScale = 0.9f * childZoom;

            sb.Draw(_bpmText,

                capsulePos + new Vector2((144f + _bpmText.Width * 0.05f) * childZoom, (87f + _bpmText.Height * 0.05f + detailYOffset) * childZoom),

                null, new Color(Color.White, alpha), 0f, Vector2.Zero,

                detailScale, SpriteEffects.None, 0f);

        }



        // BPM numbers (3 small digits) at (191+i*11, 88.5) — original updateBPM positions

        if (_smallNumbersSheet?.Texture != null)

        {

            int bpm = song.Bpm;

            int d0 = bpm < 100 ? 0 : (bpm / 100) % 10;

            int d1 = bpm < 10 ? 0 : (bpm / 10) % 10;

            int d2 = bpm % 10;



            float shiftX = 191f;

            if (Math.Floor(bpm / 100f) == 1)

                shiftX = 186f;



            float tempShift = (Math.Floor(bpm / 10f) % 10 == 1) ? -4f : 0f;



            DrawSmallNumber(sb, d0, capsulePos + new Vector2(shiftX * childZoom, (88.5f + detailYOffset) * childZoom), alpha, childZoom);

            DrawSmallNumber(sb, d1, capsulePos + new Vector2((shiftX + 11f + tempShift) * childZoom, (88.5f + detailYOffset) * childZoom), alpha, childZoom);

            DrawSmallNumber(sb, d2, capsulePos + new Vector2((shiftX + 22f + tempShift) * childZoom, (88.5f + detailYOffset) * childZoom), alpha, childZoom);

        }



        // Difficulty text label at (414, 87), scale 0.9

        if (_difficultyText != null && _difficultyText != Assets.Pixel)

        {

            float detailScale = 0.9f * childZoom;

            sb.Draw(_difficultyText,

                capsulePos + new Vector2((414f + _difficultyText.Width * 0.05f) * childZoom, (87f + _difficultyText.Height * 0.05f + detailYOffset) * childZoom),

                null, new Color(Color.White, alpha), 0f, Vector2.Zero,

                detailScale, SpriteEffects.None, 0f);

        }



        // Difficulty rating numbers (2 big digits) at (466+i*30, 32)

        if (_bigNumbersSheet?.Texture != null)

        {

            string currentDiff = GetCurrentDifficulty(song);

            int rating = 0;

            if (song.Ratings != null && song.Ratings.TryGetValue(currentDiff, out int r))

                rating = Math.Clamp(r, 0, 20);
            if (song.IsRandom) rating = _randomCapsuleRating;   // SongMenuItem(): updateDifficultyRating(FlxG.random.int(0, 20))

            int tens = rating >= 10 ? rating / 10 : 0;

            int ones = rating % 10;

            DrawBigNumber(sb, tens, capsulePos + new Vector2(466f * childZoom, (32f + detailYOffset) * childZoom), alpha, childZoom);

            DrawBigNumber(sb, ones, capsulePos + new Vector2(496f * childZoom, (32f + detailYOffset) * childZoom), alpha, childZoom);

        }



        // NEW badge at (454, 9) (original: shows when song has no score)

        if (!song.IsRandom && _newTextFrames != null && _newTextFrames.Count > 0 && _newTextSheet?.Texture != null)

        {

            string nd = GetCurrentDifficulty(song);

            int existingScore = HighscoreManager.GetScore(song.Name, nd);

            bool isSpaghetti = string.Equals(song.Name, "spaghetti", StringComparison.OrdinalIgnoreCase);

            if (existingScore <= 0 && isSpaghetti)

            {

                int ni = (int)((uint)Environment.TickCount / 83u % (uint)_newTextFrames.Count);

                var nf = _newTextFrames[ni];

                float newScale = 0.9f * childZoom;

                sb.Draw(_newTextSheet.Texture,

                    capsulePos + new Vector2(454f * childZoom, (9f + detailYOffset) * childZoom) + nf.Offset * newScale,

                    nf.SourceRect, new Color(Color.White, alpha), 0f, Vector2.Zero,

                    newScale, SpriteEffects.None, 0f);

            }

        }



        // Week type (WEEK/WEEKEND) at (291, 87)

        if (_weekTypesSheet?.Texture != null && song.WeekNum != 0)

        {

            string weekAnim = song.WeekNum > 0 ? "WEEK text instance 1" : "WEEKEND text instance 1";

            var weekFrames = _weekTypesSheet.Frames.TryGetValue(weekAnim + "0000", out var wf0) ? new List<SpriteFrame> { wf0 } : _weekTypesSheet.GetAnimation(weekAnim);

            if (weekFrames != null && weekFrames.Count > 0)

            {

                var wf = weekFrames[0];

                float weekScale = 0.9f * childZoom;

                sb.Draw(_weekTypesSheet.Texture,

                    capsulePos + new Vector2((291f + wf.SourceRect.Width * 0.05f) * childZoom, (87f + wf.SourceRect.Height * 0.05f + detailYOffset) * childZoom) + wf.Offset * weekScale,

                    wf.SourceRect, new Color(Color.White, alpha), 0f, Vector2.Zero,

                    weekScale, SpriteEffects.None, 0f);

            }



            // Week number at (355, 88.5) — weekend entries shift x by -35 (original: checkWeek)

            if (_smallNumbersSheet?.Texture != null)

            {

                int wn = Math.Abs(song.WeekNum);

                float weekNumX = 355f;

                if (song.WeekNum < 0) weekNumX += 35f;   // checkWeek: offset.x -= 35 -> drawn 35 px right

                DrawSmallNumber(sb, wn % 10, capsulePos + new Vector2(weekNumX * childZoom, (88.5f + detailYOffset) * childZoom), alpha, childZoom);

            }

        }

    }



    private void DrawSmallNumber(SpriteBatch sb, int digit, Vector2 pos, float alpha, float childZoom = 1f)

    {

        if (_smallNumbersSheet?.Texture == null || digit < 0 || digit > 9) return;

        string frameName = DigitNames[digit] + "0000";

        if (_smallNumbersSheet.Frames.TryGetValue(frameName, out var frame))

        {

            float checkX = digit == 1 ? 4f : (digit == 3 ? 1f : 0f);   // set_digit: offset.x -= 4 / -= 1 -> drawn right
            float numberScale = 0.9f * childZoom;
            sb.Draw(_smallNumbersSheet.Texture,
                pos + new Vector2(checkX * childZoom, 0f) + frame.Offset * numberScale,

                frame.SourceRect, new Color(Color.White, alpha), 0f, Vector2.Zero,

                numberScale, SpriteEffects.None, 0f);

        }

    }



    private void DrawBigNumber(SpriteBatch sb, int digit, Vector2 pos, float alpha, float childZoom = 1f)

    {

        if (_bigNumbersSheet?.Texture == null || digit < 0 || digit > 9) return;

        string frameName = DigitNames[digit] + "0000";

        if (_bigNumbersSheet.Frames.TryGetValue(frameName, out var frame))

        {

            float checkX = digit == 1 ? 4f : (digit == 3 ? 1f : 0f);
            float numberScale = 0.9f * childZoom;
            sb.Draw(_bigNumbersSheet.Texture,
                pos + new Vector2(checkX * childZoom, 0f) + frame.Offset * numberScale,

                frame.SourceRect, new Color(Color.White, alpha), 0f, Vector2.Zero,

                numberScale, SpriteEffects.None, 0f);

        }

    }



    private Action GetRankBadgeAction(SpriteBatch sb, FreeplaySong song, Vector2 capsulePos, float alpha, float? overrideScale = null, float? overrideAlpha = null, ScoringRank? overrideRank = null, float childZoom = 1f)

    {

        if (_rankBadgesSheet?.Texture == null) return null;



        string rank;
        if (overrideRank != null)
            rank = overrideRank.ToString();
        else
        {
            string diff = GetCurrentDifficulty(song);
            rank = HighscoreManager.GetRank(song.Name, diff);
        }

        if (string.IsNullOrEmpty(rank)) return null;



        // Map rank string to atlas animation prefix (original FreeplayRank only has LOSS, no SHIT).

        string rankKey = rank.ToUpperInvariant();

        string prefix = rankKey switch

        {

            "PERFECT_GOLD" or "PERFECT GOLD" => "PERFECT rank GOLD",

            "PERFECT" => "PERFECT rank",

            "EXCELLENT" => "EXCELLENT rank",

            "GREAT" => "GREAT rank",

            "GOOD" => "GOOD rank",

            "SHIT" => "LOSS rank",

            "LOSS" => "LOSS rank",

            _ => null

        };

        if (prefix == null) return null;



        var frames = _rankBadgesSheet.GetAnimation(prefix);

        if (frames == null || frames.Count == 0) return null;



        bool sel = alpha >= 1f;
        float animT = _rankBadgeAnimTime;
        if (false)
            animT = overrideScale != null ? _rankAnimTimer - 0.5f : _rankAnimTimer;   // rankDisplayNew plays at +0.5 s; fakeRanking at start
        Vector2 topLeft = capsulePos + new Vector2(420f, 41f) * childZoom;
        float sprScale = overrideScale ?? 0.9f;
        return () => DrawRankBadge(sb, rank, topLeft, sprScale, childZoom, sel, animT, overrideAlpha, BlendState.Additive, SamplerState.PointClamp);

    }



    private void DrawDifficulty(SpriteBatch sb)

    {

        float y = DIFF_Y;

        // Original: offset.y -= 5 for 1/24s on difficulty change (bounce up)

        float yBounce = _diffYBounceTimer > 0 ? -5f : 0f;



        // During transition: draw old difficulty sliding out

        if (_diffTransitioning)

        {

            DrawDifficultyStamp(sb, _diffOldIndex,

                _diffOldX + DiffBaseX + _exitDiffXOffset,

                y + yBounce,

                1f,

                animateNightmare: false);

        }



        float x = (_diffTransitioning ? _diffNewX : _diffSlideX) + DiffBaseX + _exitDiffXOffset;

        // Original: alpha flashes to 0.5 for 1/24s on difficulty change

        float diffAlpha = _diffAlphaFlashTimer > 0 ? 0.5f : 1f;

        DrawDifficultyStamp(sb, _difficultyIndex, x, y + yBounce, diffAlpha, animateNightmare: true);

    }



    private void DrawDifficultyStamp(SpriteBatch sb, int difficultyIndex, float x, float y, float alpha, bool animateNightmare)

    {

        if (difficultyIndex == 4 && _diffNightmareFrames != null && _diffNightmareFrames.Count > 0 && _diffNightmareSheet?.Texture != null)

        {

            int frameIndex = animateNightmare

                ? (int)((uint)Environment.TickCount / 41u % (uint)_diffNightmareFrames.Count)

                : 0;

            var frame = _diffNightmareFrames[Math.Clamp(frameIndex, 0, _diffNightmareFrames.Count - 1)];

            sb.Draw(_diffNightmareSheet.Texture,

                new Vector2(x, y) + frame.Offset,

                frame.SourceRect,

                Color.White * alpha,

                0f,

                Vector2.Zero,

                1f,

                SpriteEffects.None,

                0f);

            return;

        }



        var tex = difficultyIndex switch

        {

            0 => _diffEasy,

            1 => _diffNormal,

            2 => _diffHard,

            3 => _diffErect,

            4 => _diffNightmare,

            _ => _diffNormal

        };

        if (tex == null || tex == Assets.Pixel)

            return;



        sb.Draw(tex,

            new Vector2(x, y),

            null,

            Color.White * alpha,

            0f,

            Vector2.Zero,

            1f,

            SpriteEffects.None,

            0f);

    }



    private void DrawDifficultyArrows(SpriteBatch sb)

    {

        if (_phase != Phase.Idle && _phase != Phase.Confirmed) return; // arrows stay visible on confirm (only exit movers hide them)

        float y = DIFF_Y - 10f; // original: grpDifficulties.y - 10

        float selW = 49f;   // freeplaySelector frame width
        float ExitSelX(float x0) => x0 + (-selW * 2f - x0) * _exitArrowT;   // absolute target, own speed 0.26 s

        float baseX = DiffBaseX;



        // Left arrow at x=20 (not flipped) — push: scale 0.5, offset.y -= 5

        if (_selectorLeft != null)

        {

            _selectorLeft.EnsureCurrentFrameRendered(Game.GraphicsDevice);

            bool leftPush = _leftArrowPush > 0;

            _selectorLeft.Position = new Vector2(ExitSelX(baseX + 20f), leftPush ? y - 5f : y);

            _selectorLeft.Scale = leftPush ? new Vector2(0.5f) : Vector2.One;

            _selectorLeft.Tint = leftPush ? Color.White : Color.White;

            _selectorLeft.Draw(sb);

        }



        // Right arrow at x=325 (flipped horizontally) — push: scale 0.5, offset.y -= 5

        if (_selectorRight != null)

        {

            _selectorRight.EnsureCurrentFrameRendered(Game.GraphicsDevice);

            bool rightPush = _rightArrowPush > 0;

            _selectorRight.Position = new Vector2(ExitSelX(baseX + 325f), rightPush ? y - 5f : y);

            _selectorRight.Scale = rightPush ? new Vector2(0.5f) : Vector2.One;

            _selectorRight.Effects = SpriteEffects.FlipHorizontally;

            _selectorRight.Draw(sb);

        }

    }



    private void DrawDifficultyDots(SpriteBatch sb)

    {

        if (_separator == null || _separator == Assets.Pixel) return;

        if (!_dotsVisible) return;

        if (_songs.Count == 0) return;



        // Original refreshDots always uses 5 dots (Constants.DEFAULT_DIFFICULTY_LIST_FULL.length)

        int dotCount = Difficulties.Length;

        int selectedDot = Math.Clamp(_difficultyIndex, 0, dotCount - 1);



        // Original refreshDots: distance=30, groupOffset=14.7, shiftAmt=(distance*count)/2

        // difficultyDots.x = 260 - 14.7*(count-1); dot.x = difficultyDots.x + 30*i - shiftAmt


        float distance = 30f;

        float groupOffsetPer = 14.7f;

        float dotsGroupX = 260f - groupOffsetPer * (dotCount - 1);

        float shiftAmt = (distance * dotCount) / 2f;

        float baseY = 170f;

        for (int i = 0; i < dotCount; i++)

        {

            float dotX = DiffBaseX + (dotsGroupX + distance * i) - shiftAmt;   // dots are not exit movers (fadeDots(false))

            bool selected = i == selectedDot;

            // Original: deselected=0xFF484848, selected=0xFFFAFAFA

            Color dotColor = selected ? new Color(0xFA, 0xFA, 0xFA) : new Color(0x48, 0x48, 0x48);

            sb.Draw(_separator,

                new Vector2(dotX, baseY), null, CsDotCol(dotColor),

                0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);



            // Draw dotPulse animation overlay for selected dot

            if (selected && CsTrans < 0f && _dotPulseFrames != null && _dotPulseFrames.Count > 0 && _dotPulseSheet?.Texture != null)

            {

                int frameIdx = (int)((DateTime.Now.Millisecond / 83.3f) % _dotPulseFrames.Count);

                var pFrame = _dotPulseFrames[frameIdx];

                float pulseX = dotX + (_separator.Width / 2f) - (pFrame.SourceRect.Width / 2f);

                float pulseY = baseY + (_separator.Height / 2f) - (pFrame.SourceRect.Height / 2f);

                sb.Draw(_dotPulseSheet.Texture,

                    new Vector2(pulseX, pulseY) + pFrame.Offset, pFrame.SourceRect,

                    dotColor * 0.6f,

                    0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);

            }

        }

    }



    private void DrawConfirmGlow(SpriteBatch sb)
    {
        if (_phase != Phase.Confirmed || _isPicoTheme) return;   // backcard-pico confirm() never calls super.confirm()
        // BackingCard.hx: confirmGlow / confirmGlow2 at (CUTOUT*0.44 - 30, 240); confirmTextGlow at (CUTOUT*0.44 - 8, 115)
        var glowPos = new Vector2(CutoutWidth * DJ_POS_MULTI - 30f, 240f);
        var textGlowPos = new Vector2(CutoutWidth * DJ_POS_MULTI - 8f, 115f);
        // confirmGlow2: default (NORMAL) blend. Straight-alpha texture -> alpha only in A (Color.White * a would be a^2).
        if (_confirmGlow2 != null && _confirmGlow2 != Assets.Pixel && _confirmGlow2Alpha > 0f)
        {
            sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
            sb.Draw(_confirmGlow2, glowPos, new Color(Color.White, _confirmGlow2Alpha));
            sb.End();
        }
        // confirmGlow + confirmTextGlow: blend = 0 (ADD)
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.Additive);
        if (_confirmGlow != null && _confirmGlow != Assets.Pixel && _confirmGlowAlpha > 0f)
            sb.Draw(_confirmGlow, glowPos, new Color(Color.White, _confirmGlowAlpha));
        if (_confirmTextGlow != null && _confirmTextGlow != Assets.Pixel && _confirmTextGlowAlpha > 0f)
            sb.Draw(_confirmTextGlow, textGlowPos, new Color(Color.White, _confirmTextGlowAlpha));
        sb.End();
    }



    private void DrawCharSelectHint(SpriteBatch sb)

    {

        if (!_introDone) return;

        // Original: charSelectHint at center bottom, font "5by7" size 32, color 0xFF5F5F5F

        // Alpha pulses via sine wave (original: targetAmt = (sin(hintTimer)+1)/2, alpha = lerp(0.3, 0.9, targetAmt))

        var font = Assets.GetFont(32);

        if (font == null) return;

        float targetAmt = (MathF.Sin(_hintAlphaTimer) + 1f) / 2f;

        float hintAlpha = MathHelper.Lerp(0.3f, 0.9f, targetAmt);

        var hintCol = new Color(0x5F, 0x5F, 0x5F) * hintAlpha;

        float y = 18f + _hintSlideY + _exitOverhangYOffset * (182f / 164f);   // exit mover: y -> -overhangStuff.height



        // Show controller button sprite when last-used device was a controller, otherwise TAB text

        bool useController = Input.GamePadConnected && Input.LastDevice == InputManager.InputDevice.Controller;

        string spriteName = useController ? OptionsScene.GetButtonSpriteName(Input.SwitchCharButton) : null;

        Texture2D btnTex = spriteName == null ? null : ControllerIcons.UsePlayStation ? Assets.LoadTexture("images/ui/prompts/" + ButtonPrompts.PadGlyph(Input.SwitchCharButton) + ".png") : Assets.LoadTexture($"game/ui/controller/{spriteName}.png");



        if (useController && btnTex != null && btnTex != Assets.Pixel)

        {

            string pre = "Press ";

            string post = " to change characters";

            var preSz = font.MeasureString(pre);

            var postSz = font.MeasureString(post);

            int iconSize = 40;

            int gap = 6;

            float totalW = preSz.X + gap + iconSize + gap + postSz.X;

            // Original FlxText is CENTER-aligned in a field that starts at x=-40 with width SCREEN_WIDTH-16

            float fieldCenter = -40f + (FNFGame.SCREEN_WIDTH - 16f) / 2f;

            float x = fieldCenter - totalW / 2f;

            font.DrawText(sb, pre, new Vector2(x, y), hintCol);

            float iconX = x + preSz.X + gap;

            float iconY = y + (preSz.Y - iconSize) / 2f;

            sb.Draw(btnTex, new Rectangle((int)iconX, (int)iconY, iconSize, iconSize), Color.White * hintAlpha);

            font.DrawText(sb, post, new Vector2(iconX + iconSize + gap, y), hintCol);

        }

        else

        {

            string label = useController ? OptionsScene.FormatButtonName(Input.SwitchCharButton) : "TAB";

            string hint = $"Press [ {label} ] to change characters";

            var sz = font.MeasureString(hint);

            // Original FlxText is CENTER-aligned in a field that starts at x=-40 with width SCREEN_WIDTH-16

            float fieldCenter = -40f + (FNFGame.SCREEN_WIDTH - 16f) / 2f;

            float x = fieldCenter - sz.X / 2f;

            font.DrawText(sb, hint, new Vector2(x, y), hintCol);

        }

    }



    private void DrawLetterSort(SpriteBatch sb)

    {

        // letterSort stays visible while confirming (only the exit movers move it)

        if (_sortedLettersSheet?.Texture == null) return;

        // Original: LetterSort at ((CUTOUT_WIDTH * SONGS_POS_MULTI) + 400, 75)

        float baseX = SongsBaseX + 400f;

        float baseY = 75f + _exitLetterYOffset;

        float slotWidth = 80f;



        float xOffset = GetLetterWiggleOffset();



        // Draw left miniArrow at (-20, 15) in group (flipped)

        if (_miniArrow != null && _miniArrow != Assets.Pixel)

        {

            sb.Draw(_miniArrow,

                new Vector2(baseX - 20f + xOffset, baseY + 15f), null, Color.White,

                0f, Vector2.Zero, 1f, SpriteEffects.FlipHorizontally, 0f);

        }



        // Draw 5 letter slots centered on _letterSortIndex using the atlas (matches FNF_Official)

        for (int i = 0; i < 5; i++)

        {

            int labelIdx = _letterSortIndex - 2 + i;

            if (labelIdx < 0 || labelIdx >= LetterSortLabels.Length) continue;



            float slotX = baseX + 50f + i * slotWidth + xOffset;

            float slotY = baseY;



            float darkness = MathF.Max(MathF.Abs(i - 2) / 6f, 0.01f);
            float scale = i == 2 ? 1f : 0.8f;

            Color col = new Color(1f - darkness, 1f - darkness, 1f - darkness);
            Color sepCol = new Color((1f - darkness) * (1f - darkness), (1f - darkness) * (1f - darkness), (1f - darkness) * (1f - darkness));



            string label = LetterSortLabels[labelIdx];

            string animName = label switch

            {

                "#" => "#",

                "fav" => "fav",

                "ALL" => "ALL",

                "A-B" => "AB", "C-D" => "CD", "E-H" => "EH", "I-L" => "I L", "M-N" => "MN", "O-R" => "OR",
                "S" => "s", "T" => "t", "U-Z" => "UZ",   // sortedLetters atlas symbol names (FreeplayLetter.animLetters)
                _ => label

            };



            DrawLetterGlyph(sb, animName, baseX + 50f + i * slotWidth + xOffset, baseY + 50f, scale, col, i == 2);   // FreeplayLetter (FreeplayScene.LetterSort.cs)

            // Draw separator between slots at (i*80+60, 20) in group (except last)

            if (i < 4 && _separator != null && _separator != Assets.Pixel)

            {

                sb.Draw(_separator,

                    new Vector2(baseX + 60f + i * 80f + xOffset, baseY + 20f), null, sepCol,

                    0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);

            }

        }



        // Draw right miniArrow at (380, 15) in group

        if (_miniArrow != null && _miniArrow != Assets.Pixel)

        {

            sb.Draw(_miniArrow,

                new Vector2(baseX + 380f + xOffset, baseY + 15f), null, Color.White,

                0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);

        }



    }



    private void StartLetterWiggle(int dir)

    {

        _letterWiggleDir = Math.Sign(dir);

        if (_letterWiggleDir == 0) _letterWiggleDir = 1;

        _letterWiggleTimer = 3f / 24f;

    }



    private float GetLetterWiggleOffset()

    {

        if (_letterWiggleTimer <= 0f)

            return 0f;



        float elapsed = (3f / 24f) - _letterWiggleTimer;

        float frame = elapsed * 24f;

        float v = frame switch

        {

            < 1f => -10f,

            < 2f => -22f,

            < 3f => 2f,

            _ => 0f

        };



        return v * _letterWiggleDir;

    }





    private void DrawAlbumRoll(SpriteBatch sb)

    {

        if (!_albumVisible) return;

        // Original: AlbumRoll at (FlxG.width - 360, 220)

        float albumX = FNFGame.SCREEN_WIDTH - 360f + _exitAlbumOffset;

        float albumY = 220f;



        // Album roll placeholder removed: it was a white unused image behind the actual album art.

        if (_albumArtTexture != null && _albumArtTexture != Assets.Pixel)

        {

            // Animate album art switch with a horizontal flip (scale.x 1 -> 0 -> 1).

            var origin = new Vector2(_albumArtTexture.Width / 2f, _albumArtTexture.Height / 2f);

            sb.Draw(_albumArtTexture,

                new Vector2(albumX + origin.X, albumY + origin.Y),

                null, Color.White, 0f, origin,

                new Vector2(_albumArtScaleX, _albumArtScaleY),

                SpriteEffects.None, 0f);

        }



        // Original AlbumRoll.hx:

        //   albumTitle      at (FlxG.width - 355, 500) — sits BELOW the album art

        //   difficultyStars at (FlxG.width - 330, 209) — sits over the top of the art

        if (_albumTitleVisible && _albumTitleSprite?.Sheet?.Texture != null)

        {

            _albumTitleSprite.EnsureCurrentFrameRendered(Game.GraphicsDevice);

            _albumTitleSprite.Position = new Vector2(FNFGame.SCREEN_WIDTH - 355f + _exitAlbumOffset, 500f);

            _albumTitleSprite.Scale = Vector2.One;

            _albumTitleSprite.Draw(sb);

        }



        if (_albumStarsVisible && _freeplayStarsSprite?.Sheet?.Texture != null)

        {

            _freeplayStarsSprite.EnsureCurrentFrameRendered(Game.GraphicsDevice);

            _freeplayStarsSprite.Position = new Vector2(FNFGame.SCREEN_WIDTH - 330f + _exitAlbumOffset, 209f);

            _freeplayStarsSprite.Scale = Vector2.One;

            _freeplayStarsSprite.Draw(sb);

        }

    }



    private void DrawScoreDisplay(SpriteBatch sb)

    {

        // Original: FreeplayScore group is at y=60 and offsets its children by that amount, so rendered score y=120.

        // highscore at (FlxG.width - 420, 70)

        float highscoreX = FNFGame.SCREEN_WIDTH - 420f * (1f - _exitScoreT);

        float highscoreY = 70f;

        float scoreX = FNFGame.SCREEN_WIDTH - 353f * (1f - _exitScoreT);

        float scoreY = 120f;



        if (_highscoreSprite?.Sheet?.Texture != null)

        {

            _highscoreSprite.Position = new Vector2(highscoreX, highscoreY);

            _highscoreSprite.Scale = Vector2.One;

            _highscoreSprite.Draw(sb);

        }



        int displayScore = (int)_lerpScore;

        string scoreStr = displayScore.ToString("D7");



        // Draw score using digit atlas sprites (original FreeplayScore: 7 digits, 45px spacing, 0.4x scale, centerOffsets)

        if (_digitSheet?.Texture != null && _digitSheet.Frames != null)

        {

            float digitScale = 0.4f;

            float digitSpacing = 45f; // original FreeplayScore spacing

            for (int i = 0; i < scoreStr.Length; i++)

            {

                int digit = scoreStr[i] - '0';

                if (digit < 0 || digit > 9) continue;

                string frameName = DigitNames[digit] + " DIGITAL0015"; // final settled frame

                if (_digitSheet.Frames.TryGetValue(frameName, out var frame))

                {

                    // Original FreeplayScore uses setGraphicSize(width*0.4) + centerOffsets:
                    // offset = (frameWidth - width*0.4) / 2, then digit 1 gets offset.x -= 15.

                    // Original ScoreNum: setGraphicSize(Std.int(width * 0.4)) + updateHitbox() puts the untrimmed
                    // frame's top-left exactly at (x, y); the trimmed frame sits at its trim offset * scale.
                    // Digit 1 gets offset.x -= 15 (drawn 15px further right).
                    int fw0 = frame.FrameWidth > 0 ? frame.FrameWidth : frame.SourceRect.Width;
                    digitScale = (int)(fw0 * 0.4f) / (float)fw0;
                    float drawX = scoreX + i * digitSpacing + frame.Offset.X * digitScale + (digit == 1 ? 15f : 0f);
                    float drawY = scoreY + frame.Offset.Y * digitScale;
                    sb.Draw(_digitSheet.Texture,
                        new Vector2(drawX, drawY),

                        frame.SourceRect, Color.White,

                        0f, Vector2.Zero, digitScale, SpriteEffects.None, 0f);

                }

            }

        }

        else

        {

            // Fallback: plain font if digit atlas not loaded

            var font = Assets.GetFont(40);

            if (font != null)

            {

                font.DrawText(sb, scoreStr, new Vector2(scoreX, scoreY + 10f), Color.White);

            }

        }



        // Original: clearBox at (FlxG.width - 115, 65)

        if (_clearBox != null && _clearBox != Assets.Pixel)

        {

            sb.Draw(_clearBox,

                new Vector2(FNFGame.SCREEN_WIDTH - 115f * (1f - _exitScoreT), 65f), null, Color.White,

                0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);

        }



        // Original: txtCompletion at (FlxG.width - 95, 87), shows completion % (uses lerpCompletion)

        float completion = _lerpCompletion;

        // Some save data stored clear percent as 0-100; original expects 0-1. Normalize.

        if (completion > 1f) completion /= 100f;

        int compVal = Math.Min((int)Math.Floor(completion * 100.0 + 1e-4), 100);   // Math.floor(lerpCompletion * 100); epsilon: 0.53f*100f = 52.99998 showed 52

        string compText = compVal > 0 ? compVal.ToString() : "0";



        if (_clearFontSheet?.Texture != null)

        {

            // Draw with the original freeplay-clear bitmap font (no per-digit offset, matches AtlasText left-alignment)

            float drawX = FNFGame.SCREEN_WIDTH - 95f * (1f - _exitScoreT);

            float drawY = 87f;

            for (int i = 0; i < compText.Length; i++)

            {

                int digit = compText[i] - '0';

                if (digit < 0 || digit > 9) continue;

                string frameName = $"{digit}0000";

                if (_clearFontSheet.Frames.TryGetValue(frameName, out var frame))

                {

                    sb.Draw(_clearFontSheet.Texture,

                        new Vector2(drawX, drawY) + frame.Offset,

                        frame.SourceRect, Color.White,

                        0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);

                    drawX += frame.SourceRect.Width;

                }

            }

        }

        else

        {

            var clearFont = Assets.GetFont(32);

            if (clearFont != null)

                clearFont.DrawText(sb, compText, new Vector2(FNFGame.SCREEN_WIDTH - 95f * (1f - _exitScoreT), 87f), Color.White);

        }

    }



    private void DrawOverhang(SpriteBatch sb)

    {

        // Original: 164px sprite at y=-100, so 64px visible at top

        // _overhangY tweens from -164 to -100; visible portion = _overhangY + 164

        float overhangYExit = _overhangY + _exitOverhangYOffset;

        float visibleH = overhangYExit + 164f;

        if (visibleH <= 0) return;

        int overhangH = (int)visibleH;



        var font = Assets.GetFont(40);

        float barY = overhangH - 64f; // text area offset



        if (_introDone && font != null)

        {

            // Draw the arrow BEHIND the black bar area (only shows during exit tween)

            font.DrawText(sb, "<---", new Vector2(8, barY + 8), Color.White);

        }



        // Black overhang bar

        sb.Draw(Assets.Pixel,

            new Rectangle(0, (int)_exitOverhangYOffset, FNFGame.SCREEN_WIDTH, overhangH),

            Color.Black);



        if (font == null) return;



        if (_introDone)

        {

            // freeplayTxtBg (black rect behind FREEPLAY text)

            string freeplayText = "FREEPLAY";

            var textSize = font.MeasureString(freeplayText);

            sb.Draw(Assets.Pixel,

                new Rectangle(0, (int)(barY + _exitOverhangYOffset), (int)(textSize.X + 24), (int)(textSize.Y + 24)),

                Color.Black);

            // FREEPLAY text on top

            font.DrawText(sb, freeplayText, new Vector2(8, barY + 8 + _exitOverhangYOffset), Color.White);



            // ostName right-aligned, 'OFFICIAL OST'

            string ostText = "OFFICIAL OST";

            var ostSize = font.MeasureString(ostText);

            float ostX = FNFGame.SCREEN_WIDTH - 8f - ostSize.X;

            font.DrawText(sb, ostText, new Vector2(ostX, barY + 8 + _exitOverhangYOffset), Color.White);

        }

    }



    // -------------------------------------------------------

    //  HELPERS

    // -------------------------------------------------------



    private void LoadDJ()

    { if (!TryLoadModDJ()) { // mod player DJ (FreeplayScene.Players.cs), else base DJ

        string[] djFilter;

        string sheetPath;

        if (_isPicoTheme)

        {

            djFilter = new[]

            {

                "pico dj intro", "Pico DJ", "Pico DJ confirm",

                "pico cheer", "Pico DJ loss", "Pico DJ afk",

                "Pico DJ to CS"

            };

            sheetPath = "menus/freeplay/freeplay-pico";

        }

        else

        {

            djFilter = new[]

            {

                "boyfriend dj intro", "Boyfriend DJ", "Boyfriend DJ confirm",

                "Boyfriend DJ fist pump", "Boyfriend DJ loss reaction 1", "bf dj afk",

                "Boyfriend DJ watchin tv OG", "Boyfriend DJ to CS",

                "Boyfriend DJ new character "

            };

            sheetPath = "menus/freeplay/freeplay-boyfriend";

        }



        #if XBOX_UWP
        bool djLazy = Environment.GetEnvironmentVariable("FNF_DJ_LAZY") != "0";
#else
        bool djLazy = Environment.GetEnvironmentVariable("FNF_DJ_LAZY") == "1";
#endif
        // Xbox memory: only intro / idle / confirm are pre-rendered now, the rest on first play
        if (djLazy) SpriteSheet.LazyCompositeKeep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { djFilter[0], djFilter[1], djFilter[2] };
        if (djLazy && Environment.GetEnvironmentVariable("FNF_DJ_LAZYALL") == "1") SpriteSheet.LazyCompositeKeep.Remove(djFilter[2]); // dev: confirm via JIT too
        try { _djSheet = SpriteSheet.Load(Game, sheetPath,

            preRenderComposites: true, preRenderFilter: djFilter); }
        finally { SpriteSheet.LazyCompositeKeep = null; }

        if (_djSheet == null) return;



        // Original AnimateAtlasFreeplayDJ (applyStageMatrix=false): each animation is a symbol whose
        // whole-timeline bounds are fixed; point p is drawn at animOffset + (p - bounds.min). Drawing
        // every frame around its own outline made BF jump on the turntables.
        _djSprite = new AnimatedSprite { Sheet = _djSheet, UseTimelineAnchoring = true };



        // Original FNF player-data animation offsets. With applyStageMatrix=false, FlxSprite offset is

        // (position - offset - cutout, position - offset). We replicate the resulting visible position

        // by baking the animation offsets into the sprite position (the CompositeBaseOrigin cancels out).

        _djOffsets = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);

        if (_isPicoTheme)

        {

            _djOffsets["pico dj intro"] = new Vector2(-54.5f, -156f);

            _djOffsets["Pico DJ"] = new Vector2(10f, 273f);

            _djOffsets["Pico DJ afk"] = new Vector2(-119.5f, 134.5f);

            _djOffsets["Pico DJ confirm"] = new Vector2(-54.5f, 284f);

            _djOffsets["pico cheer"] = new Vector2(-14.3f, 61.5f);

            _djOffsets["Pico DJ loss"] = new Vector2(-8f, 224f);

            _djOffsets["Pico DJ to CS"] = new Vector2(-62f, -12f);

        }

        else

        {

            _djOffsets["boyfriend dj intro"] = new Vector2(3.4f, -135.5f);

            _djOffsets["Boyfriend DJ"] = new Vector2(10f, 290f);

            _djOffsets["bf dj afk"] = new Vector2(10f, 127.2f);

            _djOffsets["Boyfriend DJ watchin tv OG"] = new Vector2(-10f, -167.5f);

            _djOffsets["Boyfriend DJ confirm"] = new Vector2(-40f, 289.5f);

            _djOffsets["Boyfriend DJ fist pump"] = new Vector2(10f, 278.3f);

            _djOffsets["Boyfriend DJ loss reaction 1"] = new Vector2(10f, 277.3f);

            _djOffsets["Boyfriend DJ to CS"] = new Vector2(-51.5f, 210.4f);

            _djOffsets["Boyfriend DJ new character "] = new Vector2(-144.5f, 69.2f);

        }



        if (_isPicoTheme)

        {

            _djIntroAnim = FindDJAnim("pico dj intro");

            _djIdleAnim = FindDJAnim("Pico DJ");

            _djConfirmAnim = FindDJAnim("Pico DJ confirm");

            _djEasterEggAnim = FindDJAnim("Pico DJ afk");

            _djTvAnim = null;

            _djFistPumpAnim = FindDJAnim("pico cheer");

            _djLossAnim = FindDJAnim("Pico DJ loss");

            _djCharSelectAnim = FindDJAnim("Pico DJ to CS");

            _djNewUnlockAnim = null;

        }

        else

        {

            _djIntroAnim = FindDJAnim("boyfriend dj intro");

            _djIdleAnim = FindDJAnim("Boyfriend DJ");

            _djConfirmAnim = FindDJAnim("Boyfriend DJ confirm");

            _djEasterEggAnim = FindDJAnim("bf dj afk");

            _djTvAnim = FindDJAnim("Boyfriend DJ watchin tv OG");

            _djFistPumpAnim = FindDJAnim("Boyfriend DJ fist pump");

            _djLossAnim = FindDJAnim("Boyfriend DJ loss reaction 1");

            _djCharSelectAnim = FindDJAnim("Boyfriend DJ to CS");

            _djNewUnlockAnim = FindDJAnim("Boyfriend DJ new character ");

        }



        } // !TryLoadModDJ
        // -- TV sound preload (Boyfriend DJ cartoon / channel switch) --

        if (!_isPicoTheme)

        {

            Audio.PreloadSound("sounds/remote_click");

            Audio.PreloadSound("sounds/tv_on");

            Audio.PreloadSound("sounds/channel_switch");



            string cartoonDir = System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot, "sounds", "cartoons");

            if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(cartoonDir))

                _tvCartoonFiles = global::FNF_MonoGame.Engine.FsCI.GetFiles(cartoonDir, "*.ogg");

            else

                _tvCartoonFiles = System.Array.Empty<string>();

        }



        BuildDebugMenu();



        if (!string.IsNullOrEmpty(_djIntroAnim))

        {

            _djSprite.PlayAnimation(_djIntroAnim, loop: false);

            _djSprite.OnFinish = () =>

            {

                if (!string.IsNullOrEmpty(_djIdleAnim))

                    _djSprite.PlayAnimation(_djIdleAnim, loop: true);

                if (!_introDone) OnDJIntroDone();

            };

        }

        else if (!string.IsNullOrEmpty(_djIdleAnim))

        {

            _djSprite.PlayAnimation(_djIdleAnim, loop: true);

            OnDJIntroDone();

        }

    }



    private void ResetDjAfk(bool returnToIdle = false)

    {

        _djAfkTimer = 0f;

        _djSawEasterEgg = false;

        _djEasterEggPlaying = false;

        _djTvPlaying = false;

        StopTvCartoon(restoreMusic: true);

        if (returnToIdle && _djSprite != null && !string.IsNullOrEmpty(_djIdleAnim))

            _djSprite.PlayAnimation(_djIdleAnim, loop: true, force: true);

    }



    private void StartTvAnimation()

    {

        if (_djSprite == null || string.IsNullOrEmpty(_djTvAnim)) return;



        _djTvPlaying = true;

        _djAfkTimer = 0f;

        _tvFirstRun = true;

        _tvLastFrame = -1;



        _djSprite.OnFinish = () =>

        {

            try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tv_debug2.log"), $"TV OnFinish fired: current={_djSprite?.FrameIndex}, restarting from 60" + Environment.NewLine); } catch { }

            // TV animation is not looping in the original; restart from the channel-change frame

            if (_djSprite != null && !string.IsNullOrEmpty(_djTvAnim))

                _djSprite.PlayAnimationFromFrame(_djTvAnim, 60, loop: false);

        };



        try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tv_debug2.log"), $"TV START: anim={_djTvAnim}, totalFrames={_djSprite.FrameCount}" + Environment.NewLine); } catch { }

        _djSprite.PlayAnimation(_djTvAnim, loop: false, force: true);

    }





    private void StopTvCartoon(bool restoreMusic)

    {

        _tvCartoonSound?.Stop();

        _tvCartoonSound?.Dispose();

        _tvCartoonSound = null;

        _tvFirstRun = true;

        _tvLastFrame = -1;

        if (_djSprite != null)

            _djSprite.OnFinish = null;



        if (restoreMusic)

        {

            _tvMusicFading = true;

            _tvMusicFadeTimer = 0f;

            _tvMusicFadeDuration = 1.0f;

            _tvMusicFadeFrom = Audio.MusicVolume;

            _tvMusicFadeTo = _tvMusicRestoreVolume;

        }

    }



    private void RunTvLogic()

    {

        if (_tvFirstRun)

        {

            Audio.PlaySound("sounds/tv_on");

            _tvFirstRun = false;

        }

        else

        {

            Audio.PlaySound("sounds/channel_switch");

        }

        LoadCartoon();

    }



    private void LoadCartoon()

    {

        _tvCartoonSound?.Stop();

        _tvCartoonSound?.Dispose();

        _tvCartoonSound = null;



        if (_tvCartoonFiles == null || _tvCartoonFiles.Length == 0) return;



        string file = _tvCartoonFiles[_rng.Next(_tvCartoonFiles.Length)];

        string soundName = "sounds/cartoons/" + Path.GetFileName(file);

        _tvCartoonSound = Audio.CreateSoundInstance(soundName);

        if (_tvCartoonSound != null)

        {

            _tvCartoonSound.IsLooped = false;

            _tvCartoonSound.Play();

        }



        // Fade menu music down to 10% over 1 second (matches original BoyfriendFreeplayDJ)

        _tvMusicRestoreVolume = Audio.MusicVolume;

        _tvMusicFading = true;

        _tvMusicFadeTimer = 0f;

        _tvMusicFadeDuration = 1.0f;

        _tvMusicFadeFrom = Audio.MusicVolume;

        _tvMusicFadeTo = 0.1f;

    }



    private void BuildDebugMenu()

    {

        _debugMenuItems.Clear();

        int x = 10, y = 10;

        const int w = 220, h = 26;



        void Add(string label, Action action)

        {

            _debugMenuItems.Add(new DebugMenuItem { Label = label, Action = action, Bounds = new Rectangle(x, y, w, h) });

            y += h + 4;

        }



        void Play(string anim, bool loop)

        {

            if (_djSprite == null || string.IsNullOrEmpty(anim)) return;

            _djSprite.OnFinish = loop || string.IsNullOrEmpty(_djIdleAnim)

                ? (Action)null

                : () => _djSprite.PlayAnimation(_djIdleAnim, loop: true, force: true);

            _djSprite.PlayAnimation(anim, loop: loop, force: true);

        }



        Add("Play Idle", () => Play(_djIdleAnim, true));

        Add("Play Intro", () => Play(_djIntroAnim, false));

        Add("Play Confirm", () => Play(_djConfirmAnim, false));

        Add("Play Fist Pump", () => Play(_djFistPumpAnim, false));

        Add("Play Loss", () => Play(_djLossAnim, false));

        Add("Play Easter Egg", () => Play(_djEasterEggAnim, false));

        Add("Play TV/Cartoon", () => StartTvAnimation());

        Add("Play CharSelect", () => Play(_djCharSelectAnim, false));

        Add("Play NewUnlock", () => Play(_djNewUnlockAnim, true));

        Add("Reset AFK", () => ResetDjAfk(returnToIdle: true));

        Add("Force 60s idle", () => { _djAfkTimer = 59f; _djSawEasterEgg = false; });

        Add("Force 120s idle", () => { _djAfkTimer = 119f; });

    }



    private void OnDJIntroDone()

    { FxStartTimer(1f / 24f, _ => FxStartTimer(1.5f / 24f, __ => CapsChangeSelection()));

        _introDone = true;

        _introRevealTimer = 0f;

        _introUiRevealed = false;

        _hintSlideTweenTimer = 0f;

        // Original: pinkBack.color = 0xFFFFD863 (gold) for BF, 0xFF98A2F3 for Pico

        _pinkColor = _isPicoTheme

            ? new Color(0x98, 0xA2, 0xF3)

            : new Color(0xFF, 0xD8, 0x63);

        // Original: orangeBackShit.visible = true, alsoOrangeLOL.visible = true

        _orangeBarsVisible = true;

        _scrollingTextVisible = true;

        // Original: cardGlow.visible = true, alpha fades to 0 with scale 1.2 (0.45s sineOut)

        _cardGlowAlpha = 1f;

        _cardGlowScale = 1f;

        _cardGlowDuration = 0.45f;

        // Original: backingImage color fades from black to white over 0.6s

        // We'll lerp _bgRevealTint from 0 to 1 in Update

        // Original: fadeDots(true) — show dots after intro (after slight delay)

        _dotsVisible = false;

        // Album roll: visible + play intro

        _albumVisible = true;

        StartAlbumIntro();

        // If returning from a song result, start the rank animation now that the intro is done.
        if (_resultParams != null && _prepForNewRank && _rankCap == null && DevRankElapsed < 0f)   // prepForNewRank && fromResultsParams != null
            StartRankAnimation(_resultParams);

    }



    private static float EaseQuadOut(float t) => 1f - (1f - t) * (1f - t);



    private static float EaseSineIn(float t) => 1f - MathF.Cos(t * MathF.PI / 2f);
    private static float EaseSineOut(float t) => MathF.Sin(t * MathF.PI / 2f);
    private static float EaseBackOut(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float m = t - 1f;
        return 1f + c3 * m * m * m + c1 * m * m;
    }
    private static float EaseBackIn(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return c3 * t * t * t - c1 * t * t;
    }
    private static float EaseBackInOut(float t)
    {
        const float c1 = 1.70158f;
        const float c2 = c1 * 1.525f;
        if (t < 0.5f)
            return (MathF.Pow(2f * t, 2f) * ((c2 + 1f) * 2f * t - c2)) / 2f;
        return (MathF.Pow(2f * t - 2f, 2f) * ((c2 + 1f) * (2f * t - 2f) + c2) + 2f) / 2f;
    }
    private static float EaseExpoOut(float t) => t >= 1f ? 1f : 1f - MathF.Pow(2f, -10f * t);
    private static float EaseExpoIn(float t) => t <= 0f ? 0f : MathF.Pow(2f, 10f * (t - 1f));
    private static float EaseQuartIn(float t) => t * t * t * t;
    private static float EaseQuartOut(float t) => 1f - MathF.Pow(1f - t, 4f);
    private static float EaseElasticOut(float t)
    {
        if (t <= 0f) return 0f;
        if (t >= 1f) return 1f;
        const float c4 = (2f * MathF.PI) / 3f;
        return MathF.Pow(2f, -10f * t) * MathF.Sin((t * 10f - 0.75f) * c4) + 1f;
    }



    private string FindDJAnim(string name)

    {

        if (_djSheet == null) return null;

        if (_djSheet.Animations.ContainsKey(name)) return name;

        foreach (var k in _djSheet.Animations.Keys)

        {

            if (k.Equals(name, StringComparison.OrdinalIgnoreCase))

                return k;

        }

        foreach (var k in _djSheet.Animations.Keys)

        {

            if (k.Contains(name, StringComparison.OrdinalIgnoreCase))

                return k;

        }

        return null;

    }



    private SpriteSheet EnsurePixelIcon(string charName)

    {

        if (string.IsNullOrEmpty(charName)) charName = "dad";



        if (_pixelIconSheets.TryGetValue(charName, out var existing))

            return existing;



        string iconName = PixelatedIcon.ResolveIconName(charName); // PixelatedIcon.setCharacter: longest valid '-' prefix, else invisible
        if (iconName == null) { _pixelIconSheets[charName] = null; return null; }

        var sheet = SpriteSheet.Load(Game, $"menus/freeplay/icons/{iconName}pixel");

        _pixelIconSheets[charName] = sheet;

        return sheet;

    }



    private static string MapCharToPixelIcon(string charName)

    {

        return charName.ToLowerInvariant() switch

        {

            "gf" or "gf-christmas" or "gf-car" or "gf-pixel" or "gf-tankmen" => "gf",

            "dad" or "dad-christmas" => "dad",

            "spooky" or "spooky-dark" => "spooky",

            "pico" or "pico-player" or "pico-blazin" or "pico-christmas" => "pico",

            "mom" or "mom-car" => "mom",

            "parents-christmas" => "parents-christmas",

            "senpai" or "senpai-angry" => "senpai",

            "spirit" => "spirit",

            "tankman" or "tankman-bloody" => "tankman",

            "bf" or "bf-pixel" or "bf-christmas" or "bf-car" or "bf-holding-gf" => "bf",

            "bf-pixel-dead" => "bf",

            "monster" or "monster-christmas" => "monster",

            "darnell" or "darnell-blazin" => "darnell",

            "nene" or "nene-christmas" => "bf",

            "sserafim-kazuha" => "sserafim-kazuha",

            _ => "dad"

        };

    }



    private static string ClipText(SpriteFontBase font, string text, float maxWidth)

    {

        if (font.MeasureString(text).X <= maxWidth) return text;

        for (int len = text.Length - 1; len > 0; len--)

        {

            string sub = text[..len] + "...";

            if (font.MeasureString(sub).X <= maxWidth)

                return sub;

        }

        return text[..1] + "...";

    }



    private void OnSelectionChanged()

    {

        // FNF_Official: fade out preview audio before starting the next preview.

        _pendingPreviewSong = null;

        _activePreviewSong = null;

        _activePreviewStartMs = 0;

        _activePreviewEndMs = 0;

        BeginPreviewFadeOut();

        _songPreviewDelay = 0;

        _songPreviewPlaying = false;

        UpdateSelection();

        UpdateAlbumForSelection();

        SwitchBackingImage();

    }



    private void UpdateAlbumForSelection()

    {

        string albumId = null;

        if (_songs.Count > 0)

        {

            var song = _songs[Math.Clamp(_selectedIndex, 0, _songs.Count - 1)];

            if (song != null && !song.IsRandom && !string.IsNullOrWhiteSpace(song.Album))

                albumId = ResolveAlbumId(song) ?? song.Album;   // getAlbumId(currentDifficulty, currentVariation)

            else if (song != null && !song.IsRandom)

                albumId = "volume1";

        }



        if (albumId == null)

        {

            _currentAlbumId = null;

            _albumVisible = false;

            _albumTitleVisible = false;

            _albumStarsVisible = false;

            _albumArtTexture = null;

            _albumRevealTimer = 0f;

            return;

        }



        _albumVisible = true;



        bool albumChanged = !string.Equals(_currentAlbumId, albumId, StringComparison.OrdinalIgnoreCase);

        if (!albumChanged)

            return;



        _currentAlbumId = albumId;



        _albumTitleSheet?.Dispose();

        var __vsAlb = VsAlbum(albumId); _albumTitleSheet = (__vsAlb?.Title != null ? SpriteSheet.Load(Game, VsAssetPath(__vsAlb.Title)) : null) ?? SpriteSheet.Load(Game, $"menus/freeplay/albumRoll/{albumId}-text")

            ?? SpriteSheet.Load(Game, "menus/freeplay/albumRoll/volume1-text");



        var newAlbumArt = __vsAlb?.Art != null ? Assets.LoadTexture(VsAssetPath(__vsAlb.Art) + ".png") : Assets.LoadTexture($"menus/freeplay/albumRoll/{albumId}.png");

        if (newAlbumArt == null || newAlbumArt == Assets.Pixel)

            newAlbumArt = Assets.LoadTexture("menus/freeplay/albumRoll/volume1.png");



        _albumArtTexture = newAlbumArt;   // replaceSymbolGraphic: immediate swap (no flip in the original)
        _albumFlipPending = false;
        _albumFlipTimer = 0f;



        if (_albumTitleSheet != null)

        {

            _albumTitleSprite = new AnimatedSprite { Sheet = _albumTitleSheet };

            PlayAlbumTitleState("idle", loop: true, force: true);

        }



        RefreshAlbumStarsFrame();



        if (!_introDone)

            return;



        if (!_albumIntroPlayed)

        {

            StartAlbumIntro();

            return;

        }



        PlayAlbumTransition("switch");

    }



    private void StartAlbumIntro()

    {

        if (_albumRollSprite == null)

            return;



        _albumVisible = true;

        _albumIntroPlayed = true;

        _albumTitleVisible = false;

        _albumStarsVisible = false;

        _albumRevealTimer = ALBUM_REVEAL_DELAY;



        _albumRollSprite.OnFinish = () =>

        {

            _albumRollSprite.OnFinish = null;

            PlayAlbumRollState("idle", loop: true, force: true);

        };

        PlayAlbumRollState("intro", loop: false, force: true);

    }



    private void PlayAlbumTransition(string transitionState)

    {

        if (_albumRollSprite == null)

            return;



        _albumRollSprite.OnFinish = () =>

        {

            _albumRollSprite.OnFinish = null;

            PlayAlbumRollState("idle", loop: true, force: true);

        };



        PlayAlbumRollState(transitionState, loop: false, force: true);



        if (_albumTitleSprite != null)

        {

            _albumTitleVisible = true;

            _albumTitleSprite.OnFinish = () =>

            {

                _albumTitleSprite.OnFinish = null;

                PlayAlbumTitleState("idle", loop: true, force: true);

            };

            PlayAlbumTitleState("switch", loop: false, force: true);

        }



        _albumStarsVisible = true;

        RefreshAlbumStarsFrame();

    }



    private void RefreshAlbumStarsFrame()

    { _starsTime = 0f;   // set_curDifficulty replays "diff stars" from curDifficulty*100

        if (_freeplayStarsSprite?.Sheet?.Animations == null)

            return;



        string key = FindAlbumAnimationKey(_freeplayStarsSprite.Sheet, "stars");

        if (key == null)

            return;



        var frames = _freeplayStarsSprite.Sheet.GetAnimation(key);

        if (frames == null || frames.Count == 0)

        {

            _freeplayStarsSprite.PlayAnimation(key, force: true, loop: false);

            return;

        }



        int frame = Math.Clamp((int)MathF.Round((_albumDifficultyRating / 20f) * (frames.Count - 1)), 0, frames.Count - 1);

        _freeplayStarsSprite.PlayAnimationFromFrame(key, frame, loop: false);

    }



    private void UpdateAlbumArtFlip(float dt)

    {

        if (_albumFlipTimer <= 0f)

        {

            _albumArtScaleX = 1f;

            _albumArtScaleY = 1f;

            return;

        }



        _albumFlipTimer -= dt;

        const float halfDuration = 0.15f;

        float t = 1f - (_albumFlipTimer / (halfDuration * 2f));

        if (t < 0.5f)

        {

            _albumArtScaleX = MathF.Cos(t * MathF.PI * 2f) * 0.5f + 0.5f;

        }

        else

        {

            if (_albumFlipPending)

            {

                _albumArtTexture = _albumFlipPendingTexture;

                _albumFlipPending = false;

            }

            _albumArtScaleX = MathF.Cos((t - 0.5f) * MathF.PI * 2f) * 0.5f + 0.5f;

        }



        if (_albumFlipTimer <= 0f)

        {

            _albumArtScaleX = 1f;

            _albumFlipTimer = 0f;

        }

    }



    private void PlayAlbumRollState(string state, bool loop, bool force)

    { AlbumPlayLabel(state);

        if (_albumRollSprite?.Sheet?.Animations == null)

            return;



        string key = FindAlbumAnimationKey(_albumRollSprite.Sheet, state);

        if (key != null)

            _albumRollSprite.PlayAnimation(key, loop: loop, force: force);

    }



    private void PlayAlbumTitleState(string state, bool loop, bool force)

    {

        if (_albumTitleSprite?.Sheet?.Animations == null)

            return;



        string key = FindAlbumAnimationKey(_albumTitleSprite.Sheet, state);

        if (key != null)

            _albumTitleSprite.PlayAnimation(key, loop: loop, force: force);

    }



    private string FindAlbumAnimationKey(SpriteSheet sheet, string state)

    {

        if (sheet?.Animations == null || sheet.Animations.Count == 0)

            return null;



        if (!string.IsNullOrEmpty(_currentAlbumId))

        {

            foreach (var key in sheet.Animations.Keys)

            {

                if (key.Contains(_currentAlbumId, StringComparison.OrdinalIgnoreCase)

                    && key.Contains(state, StringComparison.OrdinalIgnoreCase))

                    return key;

            }

        }



        foreach (var key in sheet.Animations.Keys)

        {

            if (key.Contains(state, StringComparison.OrdinalIgnoreCase))

                return key;

        }



        if (!string.IsNullOrEmpty(_currentAlbumId))

        {

            foreach (var key in sheet.Animations.Keys)

            {

                if (key.Contains(_currentAlbumId, StringComparison.OrdinalIgnoreCase))

                    return key;

            }

        }



        foreach (var key in sheet.Animations.Keys)

            return key;



        return null;

    }



    /// <summary>

    /// Switches the BG image to match the current song's week/character (original: switchBackingImage).

    /// Pattern: freeplayBG{levelId}-{characterId}.png with fallback to freeplayBGweek1-bf.png.

    /// </summary>

    private void SwitchBackingImage()

    {

        if (_songs.Count == 0) return;

        var song = _songs[Math.Clamp(_selectedIndex, 0, _songs.Count - 1)];

        if (song.IsRandom) return;



        string weekId = song.WeekId ?? "week1";

        string selectedVariation = GetSelectedCharacterVariationId();

        string charId = selectedVariation;

        string bgKey = $"menus/freeplay/freeplayBG{weekId}-{charId}.png";

        var newBg = Assets.LoadTexture(bgKey);

        if (newBg != null && newBg != Assets.Pixel)

        {

            _bgImage = newBg;

            _bgImageMasked = CreateMaskedBGImage(Game.GraphicsDevice, _bgImage);

            _bgBlackOverlay = CreateMaskedBlackOverlay(Game.GraphicsDevice, _bgImage);

            return;

        }



        string fallback = selectedVariation.Equals("pico", StringComparison.OrdinalIgnoreCase)

            ? "menus/freeplay/freeplayBGweek1-pico.png"

            : "menus/freeplay/freeplayBGweek1-bf.png";

        newBg = Assets.LoadTexture(fallback);

        if (newBg != null && newBg != Assets.Pixel)

            _bgImage = newBg;

        else

            _bgImage = Assets.LoadTexture("menus/freeplay/freeplayBGweek1-bf.png");

        _bgImageMasked = CreateMaskedBGImage(Game.GraphicsDevice, _bgImage);

        _bgBlackOverlay = CreateMaskedBlackOverlay(Game.GraphicsDevice, _bgImage);

    }



    /// <summary>

    /// Rebuilds the filtered song list based on the current letter sort index.

    /// </summary>

    private void ApplyLetterFilter()

    { _capsJumpPending = true; ReplayRankBadges();   // generateSongList -> initData -> set_rank

        // Preserve selection like FNF_Official: keep current song if still in list (or if it was Random).

        string prevSongName = null;

        if (_songs.Count > 0)

        {

            var prev = _songs[Math.Clamp(_selectedIndex, 0, _songs.Count - 1)];

            if (prev != null && !prev.IsRandom)

                prevSongName = prev.Name;

        }



        string label = LetterSortLabels[_letterSortIndex];



        if (label == "ALL")

        {

            _songs = new List<FreeplaySong>(_allSongs);

        }

        else if (label == "fav")

        {

            var favs = HighscoreManager.Data.FavoriteSongs;

            _songs = _allSongs.Where(s => s.IsRandom || (!string.IsNullOrEmpty(s.Name) && favs.Contains(s.Name))).ToList();

        }

        else if (label == "#")

        {

            _songs = _allSongs.Where(s => s.IsRandom || (!string.IsNullOrEmpty(s.DisplayName) && !char.IsLetter(s.DisplayName[0]))).ToList();

        }

        else

        {

            // Letter range filter (e.g. "A-B", "C-D")

            char lo = label[0];

            char hi = label.Length >= 3 ? label[2] : lo;

            _songs = _allSongs.Where(s =>

            {

                if (s.IsRandom) return true;

                if (string.IsNullOrEmpty(s.DisplayName)) return false;

                char first = char.ToUpperInvariant(s.DisplayName[0]);

                return first >= lo && first <= hi;

            }).ToList();

        }



        _songs = ApplyDifficultyFilter(_songs);



        // Always keep Random capsule at index 0

        var randomCapsule = _songs.FirstOrDefault(s => s.IsRandom);

        if (randomCapsule != null)

        {

            _songs.Remove(randomCapsule);

            _songs.Insert(0, randomCapsule);

        }



        _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, _songs.Count - 1));

        _scrollLerp = _selectedIndex;



        if (prevSongName == null)

        {

            // Random was selected; keep it.

            _selectedIndex = 0;

        }

        else

        {

            int idx = _songs.FindIndex(s => !s.IsRandom && string.Equals(s.Name, prevSongName, StringComparison.OrdinalIgnoreCase));

            if (idx >= 0)

            {

                _selectedIndex = idx;

            }

            else if (_songs.Count > 1)

            {

                // Jump to first real song (avoid landing on Random)

                _selectedIndex = 1;

            }

            else

            {

                _selectedIndex = 0;

            }

        }



        _scrollLerp = _selectedIndex;

        OnSelectionChanged();

    }



    private List<FreeplaySong> ApplyDifficultyFilter(IEnumerable<FreeplaySong> songs)

    {

        string currentDifficulty = Difficulties[Math.Clamp(_difficultyIndex, 0, Difficulties.Length - 1)];

        return songs

            .Where(song => song != null

                && (song.IsRandom || SongMatchesSelectedCharacter(song))

                && (song.IsRandom

                    || SongHasDifficulty(song, currentDifficulty)

                    || HasDifficultyVariant(song, currentDifficulty)))

            .ToList();

    }



    private bool SongMatchesSelectedCharacter(FreeplaySong song)

    {

        if (song == null || song.IsRandom)

            return true;



        string selectedVariation = GetSelectedCharacterVariationId();

        bool isPicoDefaultSong = IsPicoCharacterId(song.PlayerCharacter);



        if (selectedVariation.Equals("pico", StringComparison.OrdinalIgnoreCase))

        {

            return isPicoDefaultSong

                || SongHasCharacterVariation(song, "pico");

        }



        return !isPicoDefaultSong

            || SongHasCharacterVariation(song, "bf");

    }



    private static bool IsPicoCharacterId(string characterId)

        => !string.IsNullOrWhiteSpace(characterId)

            && characterId.Contains("pico", StringComparison.OrdinalIgnoreCase);



    private static string GetSongIdentityKey(string songName)

    {

        if (string.IsNullOrWhiteSpace(songName))

            return string.Empty;



        return songName

            .Replace("-", string.Empty, StringComparison.Ordinal)

            .Replace("_", string.Empty, StringComparison.Ordinal)

            .Trim()

            .ToLowerInvariant();

    }



    private bool SongHasCharacterVariation(FreeplaySong song, string characterVariation)

    {

        if (song == null || string.IsNullOrWhiteSpace(song.Name) || string.IsNullOrWhiteSpace(characterVariation))

            return false;



        string variationId = characterVariation.Trim().ToLowerInvariant();

        if (song.Name.EndsWith($"_{variationId}", StringComparison.OrdinalIgnoreCase))

            return true;



        if (song.SongVariations != null

            && song.SongVariations.Any(v => v.Equals(variationId, StringComparison.OrdinalIgnoreCase)))

            return true;



        foreach (string baseName in GetSongFolderCandidates(song.Name))

        {

            string variantName = $"{baseName}_{variationId}";

            if (Assets.ResolvePath($"songs/{variantName}/charts/meta.json") != null

                || Assets.ResolvePath($"songs/{variantName}/charts/chart.json") != null)

            {

                return true;

            }



            if (HasSongInstVariation(baseName, variationId))

                return true;

        }



        return false;

    }



    private bool HasSongInstVariation(string songName, string variationId)

    {

        if (string.IsNullOrWhiteSpace(songName) || string.IsNullOrWhiteSpace(variationId))

            return false;



        return Assets.ResolvePath($"songs/{songName}/Inst-{variationId}.ogg") != null

            || Assets.ResolvePath($"songs/{songName}/tracks/Inst-{variationId}.ogg") != null;

    }



    private bool HasDifficultyVariant(FreeplaySong song, string difficulty)

    {

        if (song == null)

            return false;



        if (song.SongVariations != null

            && song.SongVariations.Any(v => v.Equals(difficulty, StringComparison.OrdinalIgnoreCase)))

            return true;



        if (difficulty.Equals("nightmare", StringComparison.OrdinalIgnoreCase)

            && song.SongVariations != null

            && song.SongVariations.Any(v => v.Equals("erect", StringComparison.OrdinalIgnoreCase)))

            return true;



        return HasDifficultyVariant(song.Name, difficulty);

    }



    private static bool SongHasDifficulty(FreeplaySong song, string difficulty)

    {

        if (song?.Difficulties == null || song.Difficulties.Length == 0)

            return false;



        return song.Difficulties.Any(d => d.Equals(difficulty, StringComparison.OrdinalIgnoreCase));

    }



    private bool HasDifficultyVariant(string songName, string difficulty)

    {

        if (string.IsNullOrWhiteSpace(songName) || string.IsNullOrWhiteSpace(difficulty))

            return false;



        if (!difficulty.Equals("erect", StringComparison.OrdinalIgnoreCase)

            && !difficulty.Equals("nightmare", StringComparison.OrdinalIgnoreCase))

            return false;



        if (songName.EndsWith($"_{difficulty}", StringComparison.OrdinalIgnoreCase))

            return true;



        if (difficulty.Equals("nightmare", StringComparison.OrdinalIgnoreCase)

            && songName.EndsWith("_erect", StringComparison.OrdinalIgnoreCase))

            return true;



        if (songName.EndsWith("_erect", StringComparison.OrdinalIgnoreCase)

            || songName.EndsWith("_nightmare", StringComparison.OrdinalIgnoreCase))

            return false;



        return TryResolveVariantName(songName, difficulty, out _);

    }



    private string ResolveSongNameForDifficulty(FreeplaySong song, string difficulty)

    {

        if (song == null || string.IsNullOrWhiteSpace(song.Name))

            return song?.Name;



        bool hasAnyVariantSuffix = song.Name.EndsWith("_erect", StringComparison.OrdinalIgnoreCase)

            || song.Name.EndsWith("_nightmare", StringComparison.OrdinalIgnoreCase);



        if (!hasAnyVariantSuffix

            && !song.Name.EndsWith($"_{difficulty}", StringComparison.OrdinalIgnoreCase))

        {

            if (TryResolveVariantName(song.Name, difficulty, out string variantName))

                return variantName;

        }



        return song.Name;

    }



    // SongMenuItem.songText = FreeplaySongData.fullSongName: songName of the difficulty within the variations
    // of the remembered character (e.g. "Bopeebo (Pico Mix)", "Bopeebo Erect"); falls back to the base songName.
    private readonly Dictionary<string, string> _capsuleNameCache = new(StringComparer.OrdinalIgnoreCase);
    private string GetCapsuleSongName(FreeplaySong song)
    {
        string fallback = song.DisplayName ?? song.Name;
        if (song.IsRandom || string.IsNullOrWhiteSpace(song.Name) || !string.IsNullOrEmpty(song.ModPath)) return fallback;
        string diff = GetCurrentDifficulty(song);
        string key = song.Name + "|" + diff + "|" + GetSelectedCharacterVariationId();
        if (_capsuleNameCache.TryGetValue(key, out var cached)) return cached ?? fallback;
        string name = null;
        try
        {
            string folder = ResolveSongNameForSelection(song, diff);
            string meta = Assets.ResolvePath($"songs/{folder}/charts/meta.json");
            if (meta != null && global::FNF_MonoGame.Engine.FsCI.FileExists(meta))
                name = (string)Newtonsoft.Json.Linq.JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(meta))["songName"];
        }
        catch { }
        _capsuleNameCache[key] = string.IsNullOrWhiteSpace(name) ? null : name;
        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }

    private string ResolveSongNameForSelection(FreeplaySong song, string difficulty)

    {

        if (song == null || string.IsNullOrWhiteSpace(song.Name))

            return song?.Name;



        string baseSongName = song.Name;

        string selectedVariation = GetSelectedCharacterVariationId();

        if (TryResolveCharacterVariantName(baseSongName, selectedVariation, out string characterSongName))

        {

            if (TryResolveVariantName(characterSongName, difficulty, out string characterDifficultyVariant))

                return characterDifficultyVariant;



            return characterSongName;

        }



        if (TryResolveVariantName(baseSongName, difficulty, out string difficultyVariant))

            return difficultyVariant;



        return baseSongName;

    }



    private bool TryResolveVariantName(string songName, string difficulty, out string resolvedSongName)

    {

        resolvedSongName = null;

        if (string.IsNullOrWhiteSpace(songName) || string.IsNullOrWhiteSpace(difficulty))

            return false;



        foreach (string difficultyId in GetVariantSearchOrder(difficulty))

        {

            foreach (string baseName in GetSongFolderCandidates(songName))

            {

                string variantName = $"{baseName}_{difficultyId}";

                if (Assets.ResolvePath($"songs/{variantName}/charts/meta.json") != null

                    || Assets.ResolvePath($"songs/{variantName}/tracks/Inst-{difficultyId}.ogg") != null

                    || Assets.ResolvePath($"songs/{variantName}/tracks/Inst.ogg") != null)

                {

                    resolvedSongName = variantName;

                    return true;

                }

            }

        }



        return false;

    }



    private bool TryResolveCharacterVariantName(string songName, string characterVariation, out string resolvedSongName)

    {

        resolvedSongName = null;

        if (string.IsNullOrWhiteSpace(songName) || string.IsNullOrWhiteSpace(characterVariation))

            return false;



        string normalizedVariation = characterVariation.Trim().ToLowerInvariant();

        if (songName.EndsWith($"_{normalizedVariation}", StringComparison.OrdinalIgnoreCase))

        {

            resolvedSongName = songName;

            return true;

        }



        if (songName.EndsWith("_erect", StringComparison.OrdinalIgnoreCase)

            || songName.EndsWith("_nightmare", StringComparison.OrdinalIgnoreCase))

            return false;



        // Pass 1: prefer real character-variant chart folders (e.g. *_pico)

        foreach (string baseName in GetSongFolderCandidates(songName))

        {

            string variantName = $"{baseName}_{normalizedVariation}";

            if (Assets.ResolvePath($"songs/{variantName}/charts/meta.json") != null

                || Assets.ResolvePath($"songs/{variantName}/charts/chart.json") != null)

            {

                resolvedSongName = variantName;

                return true;

            }

        }



        // Pass 2: fallback to base-song instrumental variation (Inst-{variation})

        foreach (string baseName in GetSongFolderCandidates(songName))

        {

            if (HasSongInstVariation(baseName, normalizedVariation))

            {

                resolvedSongName = baseName;

                return true;

            }

        }



        return false;

    }



    private static string GetSelectedCharacterVariationId()

    {

        string selectedCharacter = (Environment.GetEnvironmentVariable("FNF_FREEPLAY_CHAR") ?? HighscoreManager.Data.SelectedCharacter)?.Trim().ToLowerInvariant(); // env = dev test override

        if (string.IsNullOrWhiteSpace(selectedCharacter))

            return "bf"; return FNF_MonoGame.Engine.PlayerRegistry.VariationFamily(selectedCharacter); // mod players (KB owns bf) -> base family

    }



    private static string GetSelectedCharacterDisplayName()

    {

        string variationId = GetSelectedCharacterVariationId();

        return variationId.ToUpperInvariant();

    }



    private static IEnumerable<string> GetVariantSearchOrder(string difficulty)

    {

        if (string.IsNullOrWhiteSpace(difficulty))

            yield break;



        string normalized = difficulty.Trim().ToLowerInvariant();

        yield return normalized;



        if (normalized == "nightmare")

            yield return "erect";

    }



    private static IEnumerable<string> GetSongFolderCandidates(string songName)

    {

        if (string.IsNullOrWhiteSpace(songName))

            yield break;



        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);



        void AddCandidate(string candidate)

        {

            if (!string.IsNullOrWhiteSpace(candidate) && seen.Add(candidate))

            {

            }

        }



        AddCandidate(songName);

        AddCandidate(songName.Replace('-', '_'));

        AddCandidate(songName.Replace('_', '-'));

        AddCandidate(songName.Replace("-", "").Replace("_", ""));



        if (songName.Equals("dadbattle", StringComparison.OrdinalIgnoreCase))

            AddCandidate("dad_battle");



        foreach (var candidate in seen)

            yield return candidate;

    }



    private void UpdateSelection()

    {

        if (_songs.Count == 0) return;

        var song = _songs[_selectedIndex];

        SyncDifficultyToSong(song);

        if (song.IsRandom)

        {

            _displayedScore = 0;

            _intendedCompletion = 0f;

            _albumDifficultyRating = 0;

            RefreshAlbumStarsFrame();

            return;

        }

        string diff = GetCurrentDifficulty(song);

        _displayedScore = HighscoreManager.GetScore(song.Name, diff);

        _intendedCompletion = HighscoreManager.GetClearPercent(song.Name, diff);

        _albumDifficultyRating = 0;

        if (song.Ratings != null && song.Ratings.TryGetValue(diff, out int rating))

            _albumDifficultyRating = Math.Clamp(rating, 0, 20);

        RefreshAlbumStarsFrame();

    }



    private string GetCurrentDifficulty(FreeplaySong song)

    {

        string diff = Difficulties[_difficultyIndex];

        if (song != null && HasDifficultyVariant(song, diff))

            return diff;



        if (song.Difficulties != null && song.Difficulties.Length > 0)

        {

            foreach (string d in song.Difficulties)

            {

                if (d.Equals(diff, StringComparison.OrdinalIgnoreCase))

                    return d;

            }

            return song.Difficulties[0];

        }

        return diff;

    }



    private string[] GetSongDifficulties(FreeplaySong song)

    {

        if (song?.Difficulties != null && song.Difficulties.Length > 0)

            return song.Difficulties;

        return Difficulties;

    }



    private int GetSongDifficultyIndex(FreeplaySong song)

    {

        var songDifficulties = GetSongDifficulties(song);

        string currentDiff = Difficulties[Math.Clamp(_difficultyIndex, 0, Difficulties.Length - 1)];

        int idx = System.Array.FindIndex(songDifficulties, d => d.Equals(currentDiff, StringComparison.OrdinalIgnoreCase));

        return idx >= 0 ? idx : 0;

    }



    private void SyncDifficultyToSong(FreeplaySong song)

    {

        if (song == null || song.IsRandom)

            return;



        string currentDiff = Difficulties[Math.Clamp(_difficultyIndex, 0, Difficulties.Length - 1)];

        if (SongHasDifficulty(song, currentDiff) || HasDifficultyVariant(song, currentDiff))

            return;



        var songDifficulties = GetSongDifficulties(song);

        if (songDifficulties.Length == 0)

            return;



        int idx = GetSongDifficultyIndex(song);

        string resolvedDiff = songDifficulties[Math.Clamp(idx, 0, songDifficulties.Length - 1)];

        int globalIdx = System.Array.FindIndex(Difficulties, d => d.Equals(resolvedDiff, StringComparison.OrdinalIgnoreCase));

        _difficultyIndex = globalIdx >= 0 ? globalIdx : 1;

    }



    private void PlaySongPreview(string songName)

    {

        if (!string.IsNullOrWhiteSpace(songName))

        {

            var selectedSong = _songs.FirstOrDefault(s => !s.IsRandom

                && string.Equals(s.Name, songName, StringComparison.OrdinalIgnoreCase));

            if (selectedSong != null)

            {

                string selectedDiff = Difficulties[Math.Clamp(_difficultyIndex, 0, Difficulties.Length - 1)];

                songName = ResolveSongNameForSelection(selectedSong, selectedDiff);

            }

        }



        // Queue the preview; if music is currently playing, fade it out first.

        if (Audio.MusicPlaying)

        {

            _pendingPreviewSong = songName;

            BeginPreviewFadeOut();

            return;

        }



        StartPreview(songName);

    }



    private void BeginPreviewFadeOut()

    {

        if (!Audio.MusicPlaying)

            return;

        if (_previewFadingOut)

            return;



        _previewFadingIn = false;

        _previewFadingOut = true;

        _previewFadeOutTimer = 0f;

    }



    private void StartPreview(string songName)

    {

        int previewStartMs = 0;

        int previewEndMs = 0;

        string previewDifficulty = Difficulties[Math.Clamp(_difficultyIndex, 0, Difficulties.Length - 1)];

        if (!string.IsNullOrEmpty(songName))

        {

            var previewSong = _songs.FirstOrDefault(s => !s.IsRandom

                && string.Equals(s.Name, songName, StringComparison.OrdinalIgnoreCase));

            if (previewSong != null)

            {

                previewStartMs = Math.Max(0, previewSong.PreviewStartMs);

                previewEndMs = Math.Max(0, previewSong.PreviewEndMs);

            }

        }



        // Random capsule preview

        if (string.IsNullOrEmpty(songName))

        {

            Audio.PlayMusic("music/freeplayRandom", true);

            if (Audio.MusicPlaying)

            {

                Audio.MusicVolume = PREVIEW_START_VOLUME;

                _previewFadeTimer = 0f;

                _previewFadingIn = true;

            }

            _activePreviewSong = null;

            _activePreviewStartMs = 0;

            _activePreviewEndMs = 0;

            return;

        }



        // Song Inst preview (path fallbacks match existing project conventions)

        var instCandidates = new List<string>();

        string previewDiffId = (previewDifficulty ?? "normal").Trim().ToLowerInvariant();

        if (previewDiffId == "nightmare")

        {

            instCandidates.Add("Inst-nightmare");

            instCandidates.Add("Inst-erect");

        }

        else if (previewDiffId == "erect")

        {

            instCandidates.Add("Inst-erect");

        }

        instCandidates.Add("Inst");



        string noSep = songName.Replace("_", "").Replace("-", "");

        string hyphen = songName.Replace('_', '-');

        TryPlayVariationInst(songName, previewStartMs);   // playCurSongPreview: Inst-<getBaseInstrumentalId> from the base song folder
        foreach (string inst in instCandidates.Distinct(StringComparer.OrdinalIgnoreCase))

        {

            if (Audio.MusicPlaying)

                break;



            Audio.PlayMusic($"songs/{songName}/{inst}", true, previewStartMs);

            if (Audio.MusicPlaying)

                break;



            Audio.PlayMusic($"songs/{noSep}/{inst}", true, previewStartMs);

            if (Audio.MusicPlaying)

                break;



            Audio.PlayMusic($"songs/{hyphen}/{inst}", true, previewStartMs);

        }



        if (Audio.MusicPlaying)

        {

            Audio.MusicVolume = PREVIEW_START_VOLUME;

            _previewFadeTimer = 0f;

            _previewFadingIn = true;

            _activePreviewSong = songName;

            _activePreviewStartMs = previewStartMs;

            _activePreviewEndMs = previewEndMs;

        }

    }



    // -------------------------------------------------------

    //  SONG LIST LOADING

    // -------------------------------------------------------



    private static readonly string[] weekOrderAll = { "tutorial", "week1", "week2", "week3", "week4", "week5", "week6", "week7", "weekend1", "sserafim" };
    private void LoadSongList()

    {

        _allSongs.Clear();

        var addedSongKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);



        string contentPath = System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot);

        string levelsDir = Assets.ResolveDirectory("data/levels")

                        ?? Path.Combine(contentPath, "data", "levels");

        if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(levelsDir))

        {

            string[] weekOrder = weekOrderAll; string[] __unused = { "tutorial", "week1", "week2", "week3", "week4",

                                   "week5", "week6", "week7", "weekend1", "sserafim" };

            foreach (string weekName in weekOrder)

            {

                string jsonFile = Assets.ResolvePath($"data/levels/{weekName}.json") ?? Path.Combine(levelsDir, weekName + ".json");   // V-Slice mods may override a base level

                if (!global::FNF_MonoGame.Engine.FsCI.FileExists(jsonFile)) continue;



                try

                {

                    var level = JsonConvert.DeserializeObject<LevelJson>(global::FNF_MonoGame.Engine.FsCI.ReadAllText(jsonFile));

                    if (level?.Songs == null) continue;



                    foreach (string songName in level.Songs)

                    {

                        string songKey = GetSongIdentityKey(songName);

                        if (addedSongKeys.Contains(songKey)) continue;



                        var fSong = BuildSong(songName, weekName, level.Difficulties);

                        if (fSong != null)

                        {

                            _allSongs.Add(fSong);

                            addedSongKeys.Add(songKey);

                        }

                    }

                }

                catch (Exception ex)

                {

                    Console.WriteLine($"Error loading level '{weekName}': {ex.Message}");

                }

            }

        }



        // V-Slice (Polymod) mod levels: LevelRegistry.listSortedLevelIds = base levels, then the rest alphabetically
        foreach (var kv in FNF_MonoGame.Engine.VSliceMods.ListEntries("data/levels", Assets.ResolvePath).OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (weekOrderAll.Contains(kv.Key)) continue;
            try
            {
                var level = JsonConvert.DeserializeObject<LevelJson>(global::FNF_MonoGame.Engine.FsCI.ReadAllText(kv.Value));
                if (level?.Songs == null) continue;
                foreach (string songName in level.Songs)
                {
                    string songKey = GetSongIdentityKey(songName);
                    if (addedSongKeys.Contains(songKey)) continue;
                    var fSong = BuildSong(songName, kv.Key, level.Difficulties);
                    if (fSong != null) { _allSongs.Add(fSong); addedSongKeys.Add(songKey); }
                }
            }
            catch (Exception ex) { Console.WriteLine($"[VSLICE-MODS] level {kv.Key}: {ex.Message}"); }
        }

        AutoIncludeMissingCharacterVariantSongs(addedSongKeys, "pico");

        EnsureCanonicalPicoSongs(addedSongKeys);



        // -- Load songs from enabled mods (Psych Engine format) --

        LoadModSongs(addedSongKeys);



        Console.WriteLine($"Freeplay: loaded {_allSongs.Count} songs ({addedSongKeys.Count} unique)");

        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Freeplay: loaded {_allSongs.Count} songs ({addedSongKeys.Count} unique)");
            foreach (var song in _allSongs)
            {
                string source = string.IsNullOrEmpty(song.ModPath) ? "BASE" : $"MOD: {song.ModPath}";
                sb.AppendLine($"  - {song.DisplayName} ({song.Name}) diffs=[{string.Join(",", song.Difficulties ?? System.Array.Empty<string>())}] {source}");
            }
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "freeplay_debug.txt"), sb.ToString());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MODS] Could not write freeplay debug: {ex.Message}");
        }

    }



    private void EnsureCanonicalPicoSongs(HashSet<string> addedSongKeys)

    {

        if (addedSongKeys == null)

            return;



        foreach (string canonicalSong in CanonicalPicoSongs)

        {

            string songKey = GetSongIdentityKey(canonicalSong);

            if (addedSongKeys.Contains(songKey))

                continue;



            string sourceSongName = null;

            foreach (string candidate in GetSongFolderCandidates(canonicalSong))

            {

                if (Assets.ResolvePath($"songs/{candidate}/charts/meta.json") != null

                    || Assets.ResolvePath($"songs/{candidate}/charts/chart.json") != null)

                {

                    sourceSongName = candidate;

                    break;

                }

            }



            if (sourceSongName == null)

                continue;



            var song = BuildSong(sourceSongName, null, null);

            if (song == null)

                continue;



            song.Name = canonicalSong;

            song.DisplayName = System.Globalization.CultureInfo.CurrentCulture.TextInfo

                .ToTitleCase(canonicalSong.Replace('-', ' ').Replace('_', ' '));

            song.WeekNum = ResolveWeekNum(canonicalSong, song.WeekId);



            _allSongs.Add(song);

            addedSongKeys.Add(songKey);

        }

    }



    private void AutoIncludeMissingCharacterVariantSongs(HashSet<string> addedSongKeys, string characterVariation)

    {

        if (addedSongKeys == null || string.IsNullOrWhiteSpace(characterVariation))

            return;



        string contentPath = System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot);

        string songsDir = Assets.ResolveDirectory("songs")

                         ?? Path.Combine(contentPath, "songs");

        if (!global::FNF_MonoGame.Engine.FsCI.DirectoryExists(songsDir))

            return;



        string variationId = characterVariation.Trim().ToLowerInvariant();

        string variationSuffix = "_" + variationId;



        foreach (string songDir in global::FNF_MonoGame.Engine.FsCI.EnumerateDirectories(songsDir))

        {

            string folderName = Path.GetFileName(songDir);

            if (string.IsNullOrWhiteSpace(folderName)

                || !folderName.EndsWith(variationSuffix, StringComparison.OrdinalIgnoreCase))

                continue;



            string baseSongName = folderName[..^variationSuffix.Length];

            if (string.IsNullOrWhiteSpace(baseSongName))

                continue;



            string songKey = GetSongIdentityKey(baseSongName);

            if (addedSongKeys.Contains(songKey))

                continue;



            var song = BuildSong(folderName, null, null);

            if (song == null)

                continue;



            song.Name = baseSongName;

            song.DisplayName = System.Globalization.CultureInfo.CurrentCulture.TextInfo

                .ToTitleCase(baseSongName.Replace('-', ' ').Replace('_', ' '));

            song.WeekNum = ResolveWeekNum(baseSongName, song.WeekId);



            _allSongs.Add(song);

            addedSongKeys.Add(songKey);

        }

    }



    private FreeplaySong BuildSong(string songName, string weekId, List<string> weekDifficulties)

    {

        string opponentChar = "dad";

        string playerChar = "bf";

        string[] difficulties = null;

        int bpm = 100;

        Dictionary<string, int> ratings = null;

        string album = null;

        int previewStartMs = 0;

        int previewEndMs = 0;

        string[] songVariations = null;



        // Try multiple metadata paths (project layout varies)

        string metaPath = Assets.ResolvePath($"songs/{songName}/charts/meta.json")

                       ?? Assets.ResolvePath($"data/songs/{songName}/metadata.json")

                       ?? Assets.ResolvePath($"data/songs/{songName}/{songName}-metadata.json");

        if (metaPath != null && global::FNF_MonoGame.Engine.FsCI.FileExists(metaPath))

        {

            try

            {

                var meta = JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(metaPath));

                opponentChar = meta["playData"]?["characters"]?["opponent"]?.ToString() ?? "dad";

                playerChar = meta["playData"]?["characters"]?["player"]?.ToString() ?? "bf";

                var diffs = meta["playData"]?["difficulties"];

                if (diffs is JArray da)

                    difficulties = da.Select(d => d.ToString()).ToArray();

                var bpmVal = meta["songData"]?["bpm"] ?? meta["timeChanges"]?[0]?["bpm"];

                if (bpmVal != null)

                    bpm = (int)Math.Round((double)bpmVal);

                // Parse per-difficulty ratings

                var ratingsToken = meta["playData"]?["ratings"];

                if (ratingsToken is JObject ratingsObj)

                    ratings = ratingsObj.Properties().ToDictionary(p => p.Name, p => (int)p.Value);

                // Parse album id

                album = meta["playData"]?["album"]?.ToString();

                // Parse preview start (official metadata key: playData.previewStart)

                var previewStartToken = meta["playData"]?["previewStart"] ?? meta["previewStart"];

                if (previewStartToken != null && int.TryParse(previewStartToken.ToString(), out int parsedPreviewStart))

                    previewStartMs = Math.Max(0, parsedPreviewStart);

                var previewEndToken = meta["playData"]?["previewEnd"] ?? meta["previewEnd"];

                if (previewEndToken != null && int.TryParse(previewEndToken.ToString(), out int parsedPreviewEnd))

                    previewEndMs = Math.Max(0, parsedPreviewEnd);

                var variationsToken = meta["playData"]?["songVariations"] ?? meta["songVariations"];

                if (variationsToken is JArray variationArray)

                    songVariations = variationArray.Select(v => v?.ToString()).Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();

            }

            catch { }

        }



        if (difficulties == null && weekDifficulties != null && weekDifficulties.Count > 0)

            difficulties = weekDifficulties.ToArray();



        difficulties ??= new[] { "easy", "normal", "hard" };



        string displayName = songName.Replace("-", " ").Replace("_", " ");

        displayName = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(displayName);



        int weekNum = ResolveWeekNum(songName, weekId);



        return new FreeplaySong

        {

            Name = songName,

            DisplayName = displayName,

            OpponentChar = opponentChar,

            PlayerCharacter = playerChar,

            WeekId = weekId,

            Difficulties = difficulties,

            Bpm = bpm,

            WeekNum = weekNum,

            Ratings = ratings,

            Album = album,

            PreviewStartMs = previewStartMs,

            PreviewEndMs = previewEndMs,

            SongVariations = songVariations

        };

    }



    private static int ResolveWeekNum(string songName, string weekId)

    {

        // Match original SongMenuItem.checkWeek()

        return songName?.ToLowerInvariant() switch

        {

            "bopeebo" or "fresh" or "dadbattle" => 1,

            "spookeez" or "south" or "monster" => 2,

            "pico" or "philly-nice" or "blammed" => 3,

            "satin-panties" or "high" or "milf" => 4,

            "cocoa" or "eggnog" or "winter-horrorland" => 5,

            "senpai" or "roses" or "thorns" => 6,

            "ugh" or "guns" or "stress" => 7,

            "darnell" or "lit-up" or "2hot" or "blazin" => -1, // weekend

            _ => weekId switch

            {

                "tutorial" => 0,

                "week1" => 1, "week2" => 2, "week3" => 3, "week4" => 4,

                "week5" => 5, "week6" => 6, "week7" => 7,

                "weekend1" => -1,

                _ => 0

            }

        };

    }



    /// <summary>

    /// Load songs from all enabled mods (Psych Engine format).

    /// Psych Engine weeks: weeks/*.json with "songs": [["SongName", "character", [r,g,b]], ...]

    /// Psych Engine song data: data/<songname>/<songname>-<difficulty>.json

    /// </summary>

    private void LoadModSongs(HashSet<string> addedSongKeys)

    {

        var psychMods = PsychMods.GetAllMods().Where(m => m.Enabled).ToList();

        if (psychMods.Count == 0) return;



        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[LoadModSongs] {psychMods.Count} enabled mods");

        foreach (var mod in psychMods)

        {

            string modPath = PsychMods.GetModDirectory(mod.Name);

            if (string.IsNullOrEmpty(modPath)) continue;

            string weeksDir = Path.Combine(modPath, "weeks");

            string dataDir = Path.Combine(modPath, "data");

            sb.AppendLine($"[Mod] {mod.Name} path={modPath} enabled={mod.Enabled}");



            // First, try loading from weeks/*.json (Psych Engine week format)

            if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(weeksDir))

            {

                foreach (var weekFile in StoryModeScene.PsychWeekFiles(weeksDir))   // Psych: weeks/weekList.txt order, then the folder

                {

                    try

                    {

                        var weekJson = JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(weekFile));

                        bool hideFreeplay = weekJson["hideFreeplay"]?.Value<bool>() ?? false;

                        if (hideFreeplay) continue; if (StoryModeScene.PsychWeekLocked(weekJson)) continue;   // FreeplayState: locked weeks are not listed



                        string weekName = Path.GetFileNameWithoutExtension(weekFile);

                        var songsArray = weekJson["songs"] as JArray;

                        if (songsArray == null) continue;

                        // Psych week JSON can define custom difficulties (comma separated)
                        string weekDifficulties = weekJson["difficulties"]?.ToString();



                        foreach (var songEntry in songsArray)

                        {

                            if (songEntry is not JArray songArr || songArr.Count < 2) continue;



                            string songDisplayName = songArr[0]?.ToString();

                            string opponentChar = songArr[1]?.ToString() ?? "dad";

                            if (string.IsNullOrWhiteSpace(songDisplayName)) continue;



                            // Psych Engine: Paths.formatToSongPath(displayName)

                            string songFolderName = FNF_MonoGame.Engine.PsychEngine.Difficulty.FormatToSongPath(songDisplayName);

                            string songKey = songFolderName;



                            if (addedSongKeys.Contains(songKey)) continue;



                            // Week-defined difficulties take priority (Psych: Difficulty.loadFromWeek),
                            // but only keep difficulties that actually have chart files.

                            string[] detectedDiffs = DetectModSongDifficulties(modPath, songFolderName);

                            string[] difficulties;
                            if (!string.IsNullOrWhiteSpace(weekDifficulties))
                            {
                                difficulties = weekDifficulties.Split(',')
                                    .Select(d => d.Trim())
                                    .Where(d => d.Length > 0
                                        && detectedDiffs.Contains(d, StringComparer.OrdinalIgnoreCase))
                                    .ToArray();
                                if (difficulties.Length == 0)
                                    difficulties = detectedDiffs;
                            }
                            else
                                difficulties = detectedDiffs;

                            if (difficulties.Length == 0)
                                continue;



                            var song = new FreeplaySong

                            {

                                Name = songFolderName,

                                DisplayName = songDisplayName,   // Psych FreeplayState shows songs[i][0] as written

                                OpponentChar = opponentChar,

                                PlayerCharacter = "bf",

                                WeekId = weekName,

                                Difficulties = difficulties,

                                Bpm = 100,

                                WeekNum = 0,

                                ModPath = modPath

                            };



                            _allSongs.Add(song);

                            addedSongKeys.Add(songKey);

                            string msg = $"[MODS] Added song: {songDisplayName} ({songFolderName}) diffs=[{string.Join(",", difficulties)}] from {mod.Name}";

                            Console.WriteLine(msg);

                            sb.AppendLine(msg);

                        }

                    }

                    catch (Exception ex)

                    {

                        string err = $"[MODS] Error reading week {weekFile}: {ex.Message}";

                        Console.WriteLine(err);

                        sb.AppendLine(err);

                    }

                }

            }



            if (StoryModeScene.PsychWeekFiles(weeksDir).Count > 0) continue;   // Psych lists only week songs; the data/ scan is a port fallback for mods without weeks
            // Also scan data/ folder directly for any songs not in weeks
            // (Psych Engine: data/{song}/; V-Slice: data/songs/{song}/)

            var dataRoots = new List<string>();

            if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(dataDir))

                dataRoots.Add(dataDir);

            string songsSubDir = Path.Combine(dataDir, "songs");

            if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(songsSubDir))

                dataRoots.Add(songsSubDir);

            foreach (var root in dataRoots)

            {

                foreach (var songDir in global::FNF_MonoGame.Engine.FsCI.GetDirectories(root))

                {

                    string songFolderName = Path.GetFileName(songDir).ToLowerInvariant();

                    if (addedSongKeys.Contains(songFolderName)) continue;



                    // Check if this folder has chart JSON files

                    var chartFiles = global::FNF_MonoGame.Engine.FsCI.GetFiles(songDir, "*.json")

                        .Where(f => !Path.GetFileName(f).Equals("events.json", StringComparison.OrdinalIgnoreCase))

                        .ToArray();

                    if (chartFiles.Length == 0) continue;



                    string[] difficulties = DetectModSongDifficulties(modPath, songFolderName);

                    if (difficulties.Length == 0)
                        continue;



                    string displayName = songFolderName.Replace("-", " ").Replace("_", " ");

                    displayName = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(displayName);



                    // Try to get opponent from chart

                    string opponentChar = "dad";

                    try

                    {

                        var firstChart = JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(chartFiles[0]));

                        opponentChar = firstChart["song"]?["player2"]?.ToString() ?? "dad";

                    }

                    catch { }



                    var song = new FreeplaySong

                    {

                        Name = songFolderName,

                        DisplayName = displayName,

                        OpponentChar = opponentChar,

                        PlayerCharacter = "bf",

                        WeekId = null,

                        Difficulties = difficulties,

                        Bpm = 100,

                        WeekNum = 0,

                        ModPath = modPath

                    };



                    _allSongs.Add(song);

                    addedSongKeys.Add(songFolderName);

                    string msg2 = $"[MODS] Added song (data scan): {displayName} ({songFolderName}) diffs=[{string.Join(",", difficulties)}] from {mod.Name}";

                    Console.WriteLine(msg2);

                    sb.AppendLine(msg2);

                }

            }

        }

    }



    /// <summary>

    /// Detect available difficulties for a mod song by scanning data/<songname>/ and data/songs/<songname>/.

    /// Supports Psych Engine (<songname>.json, <songname>-hard.json) and V-Slice (<songname>-chart.json, <songname>-chart-hard.json).

    /// </summary>

    private static string[] DetectModSongDifficulties(string modPath, string songFolderName)

    {

        var roots = new List<string>();

        string dataDir = Path.Combine(modPath, "data");

        if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(dataDir))

            roots.Add(Path.Combine(dataDir, songFolderName));

        string songsDir = Path.Combine(dataDir, "songs");

        if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(songsDir))

            roots.Add(Path.Combine(songsDir, songFolderName));



        var diffs = new List<string>();

        foreach (var songDataDir in roots)

        {

            if (!global::FNF_MonoGame.Engine.FsCI.DirectoryExists(songDataDir)) continue;

            foreach (var file in global::FNF_MonoGame.Engine.FsCI.GetFiles(songDataDir, "*.json"))

            {

                string fileName = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();

                if (fileName == "events") continue;



                // Psych Engine: <songname>.json, <songname>-hard.json
                // Skip V-Slice metadata files (<songname>-metadata.json / <songname>-metadata-<diff>.json)

                if (fileName == songFolderName)

                {

                    if (!diffs.Contains("normal")) diffs.Add("normal");

                }

                else if (fileName.StartsWith(songFolderName + "-"))

                {

                    string diffName = fileName[(songFolderName.Length + 1)..];

                    if (string.IsNullOrWhiteSpace(diffName)) continue;

                    if (diffName.Equals("metadata", StringComparison.OrdinalIgnoreCase)

                        || diffName.StartsWith("metadata-", StringComparison.OrdinalIgnoreCase))

                        continue;

                    if (diffName.Equals("chart", StringComparison.OrdinalIgnoreCase)

                        || diffName.StartsWith("chart-", StringComparison.OrdinalIgnoreCase))

                        continue;

                    if (!diffs.Contains(diffName))

                        diffs.Add(diffName);

                }



                // V-Slice: <songname>-chart.json, <songname>-chart-hard.json

                string chartPrefix = songFolderName + "-chart";

                if (fileName == chartPrefix)

                {

                    if (!diffs.Contains("normal")) diffs.Add("normal");

                }

                else if (fileName.StartsWith(chartPrefix + "-"))

                {

                    string diffName = fileName[(chartPrefix.Length + 1)..];

                    if (!string.IsNullOrWhiteSpace(diffName) && !diffs.Contains(diffName))

                        diffs.Add(diffName);

                }

            }

        }



        // If no chart files matched the song name, this folder is not a song folder.



        // Sort: easy, normal, hard first, then alphabetical

        var order = new[] { "easy", "normal", "hard" };

        diffs.Sort((a, b) =>

        {

            int ia = System.Array.IndexOf(order, a);

            int ib = System.Array.IndexOf(order, b);

            if (ia >= 0 && ib >= 0) return ia.CompareTo(ib);

            if (ia >= 0) return -1;

            if (ib >= 0) return 1;

            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);

        });



        return diffs.ToArray();

    }

}



/// <summary>

/// Song entry for the freeplay song list.

/// </summary>

public class FreeplaySong

{

    public string Name { get; set; }

    public string DisplayName { get; set; }

    public string OpponentChar { get; set; }

    public string PlayerCharacter { get; set; }

    public string WeekId { get; set; }

    public string[] Difficulties { get; set; }

    public bool IsRandom { get; set; }

    public int Bpm { get; set; }

    public int WeekNum { get; set; } // positive=WEEK, negative=WEEKEND, 0=unknown

    public Dictionary<string, int> Ratings { get; set; } // per-difficulty ratings from metadata

    public string Album { get; set; } // album id (e.g. "volume1")

    public int PreviewStartMs { get; set; } // preview start in milliseconds from metadata

    public int PreviewEndMs { get; set; } // preview end in milliseconds from metadata

    public string[] SongVariations { get; set; } // metadata variants (e.g. erect, pico)

    public string ModPath { get; set; } // non-null if this song comes from a mod

}



