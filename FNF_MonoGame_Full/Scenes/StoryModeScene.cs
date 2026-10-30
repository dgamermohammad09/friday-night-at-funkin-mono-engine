using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Story Mode week-selection ? faithful recreation of FNF StoryMenuState.
/// Yellow banner with animated character props, week title images in
/// the centre, track list bottom-left, difficulty selector bottom-right.
/// </summary>
public partial class StoryModeScene : Scene
{
    // ?? Week data ??
    private readonly List<WeekData> _weeks = new();
    private int _selectedIndex;
    private bool _confirmed;
    private float _confirmTimer;

    // ?? Difficulty ??
    private static readonly string[] DEFAULT_DIFFICULTIES = { "easy", "normal", "hard" };
    private int _difficultyIndex = 1;
    private string[] CurrentDifficulties => _weeks.Count > 0 ? _weeks[_selectedIndex].Difficulties : DEFAULT_DIFFICULTIES;

    // ?? Assets ??
    private readonly Dictionary<string, Texture2D> _weekBanners = new();
    private readonly Dictionary<string, Texture2D> _diffTextures = new();
    private readonly Dictionary<string, SpriteSheet> _propSheets = new();
    private SpriteSheet _arrowsSheet;
    private Texture2D _lockTex;

    // ?? Animation ??
    private int _animFrame;
    private float _animTimer;
    private float _scrollLerp;
    private float _leftArrowTimer;   // >0 = show leftConfirm
    private float _rightArrowTimer;  // >0 = show rightConfirm

    // ?? Prop dance beat tracking (for danceLeft/danceRight alternation) ??
    private int _danceBeat;
    private float _danceBeatTimer;
    private const float DANCE_BPM = 102f; // Original FNF story menu BPM
    // Per-prop resolved animation frames: key = "propName:animName"
    private readonly Dictionary<string, List<SpriteFrame>> _propAnimFrames = new();
    // Per-prop dance state: which dance side each prop is on (true=left)
    private readonly Dictionary<string, bool> _propDanceLeft = new();
    // Per-prop animation start frame (for play-once dance anims)
    private readonly Dictionary<string, int> _propAnimStart = new();
    // Per-prop current animation key (to detect changes)
    private readonly Dictionary<string, string> _propCurrentAnim = new();

    // ?? Constants from original ??
    private const int BANNER_HEIGHT = 400;
    private const int BANNER_TOP = 56;

    public override void Load()
    {
        Game.Discord?.SetStoryModeState();

        LoadWeekData();

        // Load difficulty textures (from story_menu/difficulties)
        var allDiffs = new HashSet<string>(DEFAULT_DIFFICULTIES);
        foreach (var w in _weeks)
            foreach (var d in w.Difficulties)
                allDiffs.Add(d);
        foreach (var d in allDiffs)
            _diffTextures[d] = Assets.LoadTexture($"menus/story_menu/difficulties/{d}.png");

        // Load week title banners
        foreach (var w in _weeks)
            _weekBanners[w.Id] = Assets.LoadTexture($"menus/story_menu/weeks/{w.Id}.png");

        // Load character prop spritesheets for the banner
        // Collect all prop names used across all weeks
        var propNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var w in _weeks)
            foreach (var pn in w.PropNames)
                propNames.Add(pn);
        foreach (var pn in propNames)
        {
            var sheet = SpriteSheet.Load(Game, $"menus/story_menu/characters/{pn}");
            sheet ??= SpriteSheet.Load(Game, $"menus/story_menu/props/{pn}");
            if (sheet != null) _propSheets[pn] = sheet;
        }

        _lockTex = Assets.LoadTexture("menus/story_menu/lock.png");

        // Pre-resolve prop animation frames from level JSON definitions
        ResolvePropAnimations();

        // Load difficulty selector arrows spritesheet
        _arrowsSheet = SpriteSheet.Load(Game, "menus/story_menu/arrows");
        PsychLoadAssets();

        if (!Audio.MusicPlaying)
            Audio.PlayMusic("music/freakyMenu", true);
    }

    private void LoadWeekData()
    {
        string levelsDir = Assets.ResolveDirectory("data/levels")
                        ?? System.IO.Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot, "data", "levels");
        if (!global::FNF_MonoGame.Engine.FsCI.DirectoryExists(levelsDir)) return;

        string[] order = { "tutorial", "week1", "week2", "week3", "week4", "week5", "week6", "week7", "weekend1", "sserafim" };

        foreach (var wid in order)
        {
            string jp = Path.Combine(levelsDir, wid + ".json");
            if (!global::FNF_MonoGame.Engine.FsCI.FileExists(jp)) continue;
            try
            {
                var d = JsonConvert.DeserializeObject<LevelJson>(global::FNF_MonoGame.Engine.FsCI.ReadAllText(jp));
                if (d == null) continue;
                _weeks.Add(new WeekData
                {
                    Id = wid,
                    Name = d.Name ?? wid.ToUpper(),
                    Songs = d.Songs ?? new List<string>(),
                    BackgroundColor = ParseHex(d.Background ?? "#F9CF51"),
                    PropNames = ExtractPropNames(d),
                    PropData = ExtractPropData(d),
                    Difficulties = d.Difficulties != null && d.Difficulties.Count > 0
                        ? d.Difficulties.ToArray()
                        : new[] { "easy", "normal", "hard" }
                });
            }
            catch (Exception ex) { Console.WriteLine($"Week load error {wid}: {ex.Message}"); }
        }

        if (_weeks.Count == 0)
            _weeks.Add(new WeekData
            {
                Id = "tutorial", Name = "TEACHING TIME",
                Songs = new() { "tutorial" },
                BackgroundColor = new Color(249, 207, 81),
                PropNames = new() { "gf", "bf" }
            });
        
        // All weeks unlocked (no progression lock)
        for (int i = 0; i < _weeks.Count; i++)
        {
            _weeks[i].Locked = false;
        }
        LoadPsychWeeks();   // Psych mod weeks (StoryModeScene.Psych.cs)
    }

    private List<string> ExtractPropNames(LevelJson lj)
    {
        var result = new List<string>();
        if (lj.Props != null)
        {
            foreach (var p in lj.Props)
            {
                string ap = p.AssetPath ?? "";
                string name = ap.Contains('/') ? ap[(ap.LastIndexOf('/') + 1)..] : ap;
                if (!string.IsNullOrEmpty(name)) result.Add(name);
            }
        }
        return result;
    }

    private List<LevelPropJson> ExtractPropData(LevelJson lj)
    {
        return lj.Props ?? new List<LevelPropJson>();
    }

    private Color ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length >= 6)
        {
            int r = Convert.ToInt32(hex[..2], 16);
            int g = Convert.ToInt32(hex[2..4], 16);
            int b = Convert.ToInt32(hex[4..6], 16);
            return new Color(r, g, b);
        }
        return new Color(249, 207, 81);
    }

    public override void Unload()
    {
        foreach (var sheet in _propSheets.Values)
            sheet?.Dispose();
        _propSheets.Clear();
        _arrowsSheet?.Dispose();
    }

    // ===============================================================================================================
    // StoryMenuState (ui/story/StoryMenuState.hx + LevelTitle.hx) port: selection logic, title list, difficulty
    // sprite tween, background colour tween, score lerp, confirm flash + camera fade, draw order by zIndex.
    // ===============================================================================================================
    private static readonly string[] DIFFICULTY_LIST = { "easy", "normal", "hard" };   // Constants.DEFAULT_DIFFICULTY_LIST
    private static string _rememberedLevelId, _rememberedDifficulty = "normal";
    private float[] _titleY, _titleTargetY;
    private Color _bgColor, _bgFrom, _bgTo; private float _bgTweenT = -1f;
    private float _highScoreLerp = 12345678f; private int _highScore;
    private float _diffTweenT = 1f; private string _diffShown = "normal";
    private bool _flashing; private float _flashTick; private bool _flashCyan;
    private float _fadeT = -1f;
    private float _time;
    private readonly Dictionary<string, SpriteSheet> _diffAtlases = new();
    private readonly Dictionary<string, string> _songNames = new();
    private bool _inited;

    private string CurDiff => DiffList[Math.Clamp(_difficultyIndex, 0, DiffList.Length - 1)];

    private void InitState()
    {
        _inited = true;
        int n = _weeks.Count;
        _titleY = new float[n]; _titleTargetY = new float[n];
        if (_rememberedLevelId != null) { int i = _weeks.FindIndex(w => w.Id == _rememberedLevelId); if (i >= 0) _selectedIndex = i; }
        _difficultyIndex = Math.Max(0, System.Array.IndexOf(DIFFICULTY_LIST, _rememberedDifficulty ?? "normal"));
        for (int i = 0; i < n; i++) { _titleY[i] = BANNER_TOP + BANNER_HEIGHT + 10; _titleTargetY[i] = (TitleH(i) + 20) * i; }
        _bgColor = _weeks.Count > 0 ? _weeks[_selectedIndex].BackgroundColor : new Color(0xF9, 0xCF, 0x51);
        PsychOnWeekChanged(null);
        _diffShown = CurDiff; _diffTweenT = 0f;
        RepositionTitles();
        UpdateHighScore();
    }

    private int TitleH(int i) => _weekBanners.TryGetValue(_weeks[i].Id, out var t) && t != null && t != Assets.Pixel ? t.Height : 70;

    private void RepositionTitles()
    {
        int cur = _selectedIndex;
        _titleTargetY[cur] = 480;
        for (int i = cur - 1; i >= 0; i--) _titleTargetY[i] = _titleTargetY[i + 1] - Math.Max(TitleH(i) + 20, 125);
        for (int i = cur + 1; i < _weeks.Count; i++) _titleTargetY[i] = _titleTargetY[i - 1] + (TitleH(i - 1) + 20);
    }

    private void UpdateHighScore() => _highScore = HighscoreManager.GetWeekScore(_weeks[_selectedIndex].Id, CurDiff);

    private void ChangeLevel(int change)
    {
        int prev = _selectedIndex, cur = prev + change;
        if (cur < 0) cur = _weeks.Count - 1;
        if (cur >= _weeks.Count) cur = 0;
        var prevColor = _weeks[prev].BackgroundColor;
        _selectedIndex = cur; _rememberedLevelId = _weeks[cur].Id; PsychOnWeekChanged(_weeks[prev]);
        if (cur != prev) Audio.PlaySound("scrollMenu", 0.4f);
        RepositionTitles();
        UpdateHighScore();
        // updateBackground: simple colours -> FlxTween.color(0.9 s, quartOut) from the CURRENT colour
        if (_weeks[cur].BackgroundColor != prevColor) { _bgFrom = _bgColor; _bgTo = _weeks[cur].BackgroundColor; _bgTweenT = 0f; }
    }

    private void ChangeDifficulty(int change)
    {
        int cur = _difficultyIndex + change;
        if (cur < 0) cur = DiffList.Length - 1;
        if (cur >= DiffList.Length) cur = 0;
        bool changed = cur != _difficultyIndex;
        _difficultyIndex = cur; _rememberedDifficulty = CurDiff;
        if (changed) { _diffShown = CurDiff; _diffTweenT = 0f; Audio.PlaySound("scrollMenu", 0.4f); }
        UpdateHighScore();
    }

    public override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (!_inited && _weeks.Count > 0) InitState();
        _time += dt;
        _animTimer += dt;
        if (_animTimer >= 1f / 24f) { _animTimer = 0; _animFrame++; }
        _danceBeatTimer += dt;
        float beatLen = 60f / DANCE_BPM;
        if (_danceBeatTimer >= beatLen) { _danceBeatTimer -= beatLen; _danceBeat++; }
        if (_weeks.Count == 0)
        {
            if (Input.BackPressed) { Audio.PlaySound("cancelMenu"); Game.Scenes.ChangeScene(new MainMenuScene()); }
            return;
        }

        // highScoreLerp = snap(smoothLerpPrecision(lerp, highScore, elapsed, 0.307), highScore, 1)
        _highScoreLerp = _highScore + (_highScoreLerp - _highScore) * (float)Math.Pow(0.01, dt / 0.307);
        if (Math.Abs(_highScoreLerp - _highScore) < 1f) _highScoreLerp = _highScore;
        _highScoreLerp = (int)_highScoreLerp;
        // LevelTitle.update: y = smoothLerpPrecision(y, targetY, elapsed, 0.451); flashing at 20 fps
        float k = (float)Math.Pow(0.01, dt / 0.451);
        for (int i = 0; i < _weeks.Count; i++) _titleY[i] = _titleTargetY[i] + (_titleY[i] - _titleTargetY[i]) * k;
        if (_flashing) { _flashTick += dt; if (_flashTick >= 1f / 20f) { _flashTick %= 1f / 20f; _flashCyan = !_flashCyan; } }
        if (_bgTweenT >= 0f)
        {
            _bgTweenT += dt;
            float t = Math.Min(1f, _bgTweenT / 0.9f), e = 1f - (float)Math.Pow(1f - t, 4);   // quartOut
            _bgColor = Color.Lerp(_bgFrom, _bgTo, e);
            if (t >= 1f) _bgTweenT = -1f;
        }
        if (_diffTweenT < 1f) _diffTweenT += dt;
        UpdateProps(dt);

        if (_confirmed)
        {
            _confirmTimer -= dt;
            if (_confirmTimer <= 0 && _fadeT < 0f) _fadeT = 0f;          // FlxTimer(1) -> camera.fade(BLACK, 0.2)
            if (_fadeT >= 0f)
            {
                _fadeT += dt;
                if (_fadeT >= 0.2f && _fadeT < 10f)
                {
                    _fadeT = 10f;
                    var w = _weeks[_selectedIndex];
                    if (w.Songs.Count > 0)
                    {
                        Audio.StopMusic(); PlayScene.ResetStoryCampaign(w.Name); // StoryMenuState: talliesLevel reset + campaignTitle
                        Game.Scenes.ChangeScene(new PlayScene(PsychStoryParams(w) ?? PlayStateParams.ForStoryMode(w.Songs[0], CurDiff, w.Songs, 0, w.Id, 0)));
                    }
                }
            }
            return;
        }

        if (Input.UpPressed) { ChangeLevel(-1); ChangeDifficulty(0); }
        if (Input.DownPressed) { ChangeLevel(1); ChangeDifficulty(0); }
        if (Input.ScrollDelta != 0) ChangeLevel(Input.ScrollDelta > 0 ? -1 : 1);
        if (Input.RightPressed) ChangeDifficulty(1);
        if (Input.LeftPressed) ChangeDifficulty(-1);

        if (Input.ConfirmPressed)
        {
            if (_weeks[_selectedIndex].Locked) Audio.PlaySound("cancelMenu");
            else
            {
                Audio.PlaySound("confirmMenu");
                Input.Rumble(0.4f, 0.15f);
                _confirmed = true; _confirmTimer = 1f; _flashing = true; PropsConfirm();
            }
        }
        if (Input.BackPressed && !_confirmed) { Audio.PlaySound("cancelMenu"); Game.Scenes.ChangeScene(new MainMenuScene()); }
    }

    public override void Draw(SpriteBatch sb)
    {
        if (!_inited && _weeks.Count > 0) InitState();
        sb.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.NonPremultiplied);
        sb.Draw(Assets.Pixel, new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT), Color.Black);   // bgColor
        if (_weeks.Count == 0) { sb.End(); return; }
        var cw = _weeks[_selectedIndex];

        // zIndex 0: tracklistText, arrows, difficultySprite (added without zIndex)
        if (cw.IsPsych) PsychDrawLower(sb, cw); else { DrawTrackList(sb, cw); DrawDifficulty(sb); }
        // zIndex 15: levelTitles
        DrawWeekTitles(sb);
        // zIndex 99: black (0,0, w, 400 + 56); zIndex 100: levelBackground (0, 56, w x 400, colour)
        if (cw.IsPsych) PsychDrawBanner(sb, cw); else {
        sb.Draw(Assets.Pixel, new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, BANNER_HEIGHT + BANNER_TOP), Color.Black);
        sb.Draw(Assets.Pixel, new Rectangle(0, BANNER_TOP, FNFGame.SCREEN_WIDTH, BANNER_HEIGHT), _bgColor); }
        // zIndex 1000: props, scoreText, levelTitleText
        if (cw.IsPsych) PsychDrawChars(sb); else DrawProps(sb, cw);
        // scoreText FlxText(10, 10, 'LEVEL SCORE: n', VCR 32); levelTitleText VCR 32 RIGHT, alpha 0.7, x = w - (width + 10)
        MenuUI.FlxText(sb, 32, $"{(cw.IsPsych ? "WEEK" : "LEVEL")} SCORE: {(int)Math.Round(_highScoreLerp)}", 10, 10, Color.White);
        string title = cw.Name ?? "";
        MenuUI.FlxText(sb, 32, title, FNFGame.SCREEN_WIDTH - (MenuUI.FlxTextWidth(32, title) + 10), 10, new Color(Color.White, 0.7f));
        if (!_confirmed)
            ButtonPrompts.DrawBar(sb, Assets, Input, new[] { ButtonPrompts.UpDown("WEEK"), ButtonPrompts.LeftRight("DIFFICULTY"), ButtonPrompts.Confirm("PLAY"), ButtonPrompts.Back("BACK") },
                FNFGame.SCREEN_WIDTH - 16, FNFGame.SCREEN_HEIGHT - 40, 1f, 26, 0.85f);
        if (_fadeT >= 0f) sb.Draw(Assets.Pixel, new Rectangle(0, 0, FNFGame.SCREEN_WIDTH, FNFGame.SCREEN_HEIGHT), new Color(Color.Black, Math.Min(1f, _fadeT / 0.2f)));
        sb.End();
    }

    private const float TEXT_DY = 0f;

    // ---- LevelProp (ui/story/LevelProp.hx, Level.buildProps) + Bopper dance logic -------------------------------
    // x = offsets[0] + FlxG.width * 0.25 * index, y = offsets[1] (screen space), scale about the first frame's centre,
    // animation offsets subtracted, flipX/alpha from the data. Bopper: dance() on every beat % danceEvery (default 1);
    // dance steps alternate danceLeft/danceRight, otherwise 'idle' is replayed when finished; first dance() is paused
    // until the first beat; playConfirm() = 'confirm' forced and nothing else may play afterwards.
    private sealed class PropState { public string Anim; public float T; public bool Paused, HasDanced, Locked; }
    private readonly Dictionary<string, PropState> _propState = new();
    private int _lastBeat = -1;
    private const float MENU_BPM = 102f;   // music/freakyMenu metadata (timeChanges bpm 102)

    private PropState GetPropState(WeekData w, int i)
    {
        string key = w.Id + ":" + i;
        if (!_propState.TryGetValue(key, out var st))
        {
            st = new PropState();
            _propState[key] = st;
            var pd = i < w.PropData.Count ? w.PropData[i] : null;
            if (pd?.StartingAnimation is { Length: > 0 } sa && HasPropAnim(w.PropNames[i], sa)) { st.Anim = sa; st.T = 0; }
            else { PropDance(w, i, st, true); st.Paused = true; }
        }
        return st;
    }

    private bool HasPropAnim(string pName, string anim) => _propAnimFrames.ContainsKey($"{pName}:{anim}");

    private void PropDance(WeekData w, int i, PropState st, bool force = false)
    {
        if (st.Locked) return;
        string p = w.PropNames[i];
        if (HasPropAnim(p, "danceLeft") && HasPropAnim(p, "danceRight"))
        {
            st.Anim = st.HasDanced ? "danceRight" : "danceLeft"; st.HasDanced = !st.HasDanced; st.T = 0; st.Paused = false;
        }
        else
        {
            bool finished = st.Anim != "idle" || PropFinished(w, i, st);
            if (force || finished) { st.Anim = "idle"; st.T = 0; }
            st.Paused = false;
        }
    }

    private List<SpriteFrame> PropFrames(string pName, string anim)
    {
        if (anim != null && _propAnimFrames.TryGetValue($"{pName}:{anim}", out var f)) return f;
        return _propSheets.TryGetValue(pName, out var sh) && sh.Animations.TryGetValue(anim ?? "idle", out var g) ? g : null;
    }

    private LevelPropAnimJson PropAnimData(WeekData w, int i, string anim)
        => i < w.PropData.Count ? w.PropData[i].Animations?.FirstOrDefault(a => a.Name == anim) : null;

    private bool PropFinished(WeekData w, int i, PropState st)
    {
        var fr = PropFrames(w.PropNames[i], st.Anim);
        var ad = PropAnimData(w, i, st.Anim);
        if (fr == null || ad?.Looped == true) return false;
        return (int)(st.T * (ad?.FrameRate ?? 24)) >= fr.Count;
    }

    private void UpdateProps(float dt)
    {
        PsychUpdate(dt);
        var w = _weeks[_selectedIndex];
        double pos = Audio.MusicPosition;
        int beat = (int)Math.Floor(pos * MENU_BPM / 60.0);
        bool beatHit = beat != _lastBeat;
        _lastBeat = beat;
        for (int i = 0; i < w.PropNames.Count; i++)
        {
            var st = GetPropState(w, i);
            if (!st.Paused) st.T += dt;
            if (beatHit)
            {
                int every = i < w.PropData.Count && w.PropData[i].DanceEvery.HasValue ? w.PropData[i].DanceEvery.Value : 1;
                if (every > 0 && beat % every == 0) PropDance(w, i, st);
            }
        }
    }

    private void PropsConfirm()
    {
        PsychConfirm();
        var w = _weeks[_selectedIndex];
        for (int i = 0; i < w.PropNames.Count; i++)
        {
            if (!HasPropAnim(w.PropNames[i], "confirm")) continue;
            var st = GetPropState(w, i);
            st.Anim = "confirm"; st.T = 0; st.Paused = false; st.Locked = true;
        }
    }

    private void DrawProps(SpriteBatch sb, WeekData w)
    {
        for (int i = 0; i < w.PropNames.Count; i++)
        {
            string pName = w.PropNames[i];
            if (!_propSheets.TryGetValue(pName, out var sheet)) continue;
            var pd = i < w.PropData.Count ? w.PropData[i] : null;
            var st = GetPropState(w, i);
            var frames = PropFrames(pName, st.Anim) ?? PropFrames(pName, "idle") ?? sheet.Animations.Values.FirstOrDefault();
            if (frames == null || frames.Count == 0) continue;
            var ad = PropAnimData(w, i, st.Anim);
            int fi = (int)(st.T * (ad?.FrameRate ?? 24));
            fi = ad?.Looped == true ? fi % frames.Count : Math.Min(fi, frames.Count - 1);
            var frame = frames[fi];
            // FlxSprite origin = first atlas frame's size / 2 (set when the frames were assigned)
            var first = sheet.Frames.Count > 0 ? sheet.Frames.Values.First() : frames[0];
            float fw0 = first.FrameWidth > 0 ? first.FrameWidth : first.SourceRect.Width;
            float fh0 = first.FrameHeight > 0 ? first.FrameHeight : first.SourceRect.Height;
            float scale = pd != null && pd.Scale > 0 ? pd.Scale : 1f;
            float ox = pd?.Offsets?.Count >= 2 ? pd.Offsets[0] : 0, oy = pd?.Offsets?.Count >= 2 ? pd.Offsets[1] : 0;
            float ax = ad?.Offsets?.Count >= 2 ? ad.Offsets[0] : 0, ay = ad?.Offsets?.Count >= 2 ? ad.Offsets[1] : 0;
            float x = ox + FNFGame.SCREEN_WIDTH * 0.25f * i, y = oy;
            bool flip = pd?.FlipX == true;
            // flixel: screen = x - offset + origin - origin*scale + frameOffset*scale (flipX mirrors the frame offset)
            float fw = frame.FrameWidth > 0 ? frame.FrameWidth : frame.SourceRect.Width;
            float offX = frame.Offset.X;
            if (flip) offX = fw - (frame.Rotated ? frame.SourceRect.Height : frame.SourceRect.Width) - offX;
            float bx = x - ax + fw0 / 2f * (1f - scale), by = y - ay + fh0 / 2f * (1f - scale);
            var col = new Color(Color.White, pd?.Alpha ?? 1f);
            var tex = frame.Texture ?? sheet.Texture;
            if (frame.Rotated)
                sb.Draw(tex, new Vector2(bx + offX * scale, by + frame.Offset.Y * scale), frame.SourceRect, col, -MathF.PI / 2f,
                    new Vector2(frame.SourceRect.Width, 0), scale, flip ? SpriteEffects.FlipVertically : SpriteEffects.None, 0f);
            else
                sb.Draw(tex, new Vector2(bx + offX * scale, by + frame.Offset.Y * scale), frame.SourceRect, col, 0f, Vector2.Zero, scale,
                    flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
        }
    }

    private void DrawPropsOld(SpriteBatch sb, WeekData w)
    {
        // Original FNF StoryMenuState positioning:
        // x = (FlxG.width * 0.25) * (1 + i) - 150 + offsets[0]
        // y = 70 + offsets[1]
        // scale applied from JSON directly (setGraphicSize)
        int propCount = w.PropNames.Count;
        if (propCount == 0) return;

        for (int i = 0; i < propCount; i++)
        {
            string pName = w.PropNames[i];
            if (!_propSheets.TryGetValue(pName, out var sheet)) continue;

            // Determine which animation to play from the level JSON prop data
            List<SpriteFrame> frames = null;
            bool useConfirm = _confirmed && pName.Equals("bf", StringComparison.OrdinalIgnoreCase);
            bool isDance = false;
            string resolvedAnimKey = null;

            if (useConfirm)
            {
                _propAnimFrames.TryGetValue($"{pName}:confirm", out frames);
                resolvedAnimKey = $"{pName}:confirm";
            }

            if (frames == null && i < w.PropData.Count)
            {
                var pd = w.PropData[i];
                bool hasDance = _propAnimFrames.ContainsKey($"{pName}:danceLeft")
                             && _propAnimFrames.ContainsKey($"{pName}:danceRight");

                if (hasDance)
                {
                    isDance = true;
                    // Alternate dance on beat (original: danceEvery beats)
                    int interval = pd.DanceEvery > 0 ? pd.DanceEvery.Value : 2;
                    if (_danceBeat % interval == 0)
                    {
                        string key = $"{pName}:danceState";
                        if (!_propDanceLeft.ContainsKey(key)) _propDanceLeft[key] = true;
                        // Toggle on new beats
                        int expectedBeat = _danceBeat / interval;
                        bool left = expectedBeat % 2 == 0;
                        _propDanceLeft[key] = left;
                    }
                    string dKey = $"{pName}:danceState";
                    bool danceLeft = _propDanceLeft.ContainsKey(dKey) && _propDanceLeft[dKey];
                    resolvedAnimKey = danceLeft ? $"{pName}:danceLeft" : $"{pName}:danceRight";
                    _propAnimFrames.TryGetValue(resolvedAnimKey, out frames);
                }
                else
                {
                    resolvedAnimKey = $"{pName}:idle";
                    _propAnimFrames.TryGetValue(resolvedAnimKey, out frames);
                }
            }

            // Fallback: use spritesheet "idle" or "confirm" directly
            if (frames == null)
            {
                if (useConfirm)
                    sheet.Animations.TryGetValue("confirm", out frames);
                if (frames == null)
                    sheet.Animations.TryGetValue("idle", out frames);
                frames ??= sheet.Animations.Values.FirstOrDefault();
                resolvedAnimKey ??= $"{pName}:fallback";
            }
            if (frames == null || frames.Count == 0) continue;

            // Track animation changes: reset start frame when animation switches
            // (e.g., danceLeft ? danceRight, or idle ? confirm)
            string propStateKey = $"{pName}:{i}";
            if (!_propCurrentAnim.TryGetValue(propStateKey, out var prevAnimKey) || prevAnimKey != resolvedAnimKey)
            {
                _propCurrentAnim[propStateKey] = resolvedAnimKey;
                _propAnimStart[propStateKey] = _animFrame;
            }

            // Calculate frame index:
            // Dance anims: play once and hold on last frame (matches original FNF)
            // Idle/other anims: loop continuously
            int animProgress = _animFrame - _propAnimStart.GetValueOrDefault(propStateKey);
            SpriteFrame frame;
            if (isDance)
                frame = frames[Math.Min(animProgress, frames.Count - 1)];
            else
                frame = frames[animProgress % frames.Count];

            float scale = 1.0f;
            float offsetX = 0, offsetY = 0;
            if (i < w.PropData.Count)
            {
                var pd = w.PropData[i];
                if (pd.Scale > 0) scale = pd.Scale;
                if (pd.Offsets != null && pd.Offsets.Count >= 2)
                {
                    offsetX = pd.Offsets[0];
                    offsetY = pd.Offsets[1];
                }
            }

            // Use logical (untrimmed) frame size when available (Sparrow atlas trimming)
            // For rotated frames without FrameWidth/Height, swap source rect dimensions
            int logicalW = frame.FrameWidth > 0 ? frame.FrameWidth
                : (frame.Rotated ? frame.SourceRect.Height : frame.SourceRect.Width);
            int logicalH = frame.FrameHeight > 0 ? frame.FrameHeight
                : (frame.Rotated ? frame.SourceRect.Width : frame.SourceRect.Height);

            // Apply JSON scale to logical frame dimensions
            float finalScale = scale;

            // Safety clamp: if prop is taller than the available area (banner + overflow
            // into gray zone), scale it down proportionally. Original FNF allows props to
            // extend below the banner into the gray area, so we use a generous limit.
            float maxHeight = BANNER_HEIGHT * 1.3f;
            float scaledH = logicalH * finalScale;
            if (scaledH > maxHeight)
                finalScale *= maxHeight / scaledH;

            // Original FNF positioning formula:
            // x = (screenWidth * 0.25) * (1 + propIndex) - 150 + offsets[0]
            // y = 70 + offsets[1]   (~BANNER_TOP + 14)
            // Offsets are raw pixel values, NOT scaled by fitScale.
            int x = (int)((FNFGame.SCREEN_WIDTH * 0.25f) * (1 + i) - 150f + offsetX);
            int y = (int)(BANNER_TOP + 14 + offsetY);

            // Original FNF: BF faces left (toward opponent). Spritesheet has BF facing right, so flip.
            var effects = pName.Equals("bf", StringComparison.OrdinalIgnoreCase)
                ? SpriteEffects.FlipHorizontally
                : SpriteEffects.None;

            if (frame.Rotated)
            {
                // Rotated Sparrow frames: stored 90? clockwise in atlas.
                // Draw with -90? rotation, origin at (sourceWidth, 0) to unrotate.
                Vector2 offset = frame.Offset;
                bool wantFlipH = (effects & SpriteEffects.FlipHorizontally) != 0;
                if (wantFlipH && frame.FrameWidth > 0)
                    offset = new Vector2(frame.FrameWidth - frame.SourceRect.Height - offset.X, offset.Y);

                float drawX = x + offset.X * finalScale;
                float drawY = y + offset.Y * finalScale;
                var rotOrigin = new Vector2(frame.SourceRect.Width, 0);
                var rotEffects = SpriteEffects.None;
                if (wantFlipH)
                    rotEffects = SpriteEffects.FlipVertically;

                sb.Draw(sheet.Texture,
                    new Vector2(drawX, drawY),
                    frame.SourceRect,
                    Color.White,
                    -MathF.PI / 2f,
                    rotOrigin,
                    new Vector2(finalScale, finalScale),
                    rotEffects,
                    0f);
            }
            else
            {
                // Visible content rect (trimmed) scaled proportionally
                int drawW = (int)(frame.SourceRect.Width * finalScale);
                int drawH = (int)(frame.SourceRect.Height * finalScale);

                // Apply frame offset (trimmed whitespace) so the visible content
                // is positioned correctly within the logical frame area.
                Vector2 offset = frame.Offset;
                // Mirror X offset when flipped horizontally (matches AnimatedSprite.Draw)
                if ((effects & SpriteEffects.FlipHorizontally) != 0 && frame.FrameWidth > 0)
                    offset = new Vector2(frame.FrameWidth - frame.SourceRect.Width - offset.X, offset.Y);

                int drawX = x + (int)(offset.X * finalScale);
                int drawY = y + (int)(offset.Y * finalScale);

                sb.Draw(sheet.Texture, new Rectangle(drawX, drawY, drawW, drawH), frame.SourceRect, Color.White, 0f, Vector2.Zero, effects, 0f);
            }
        }
    }

    /// <summary>
    /// Pre-resolve prop animation frame lists from level JSON animation definitions.
    /// Creates sub-frame lists keyed as "propName:animName" using frameIndices when specified.
    /// </summary>
    private void ResolvePropAnimations()
    {
        _propAnimFrames.Clear();
        foreach (var w in _weeks)
        {
            for (int i = 0; i < w.PropNames.Count && i < w.PropData.Count; i++)
            {
                string pName = w.PropNames[i];
                var pd = w.PropData[i];
                if (pd.Animations == null || pd.Animations.Count == 0) continue;
                if (!_propSheets.TryGetValue(pName, out var sheet)) continue;

                foreach (var anim in pd.Animations)
                {
                    if (string.IsNullOrEmpty(anim.Name)) continue;
                    string key = $"{pName}:{anim.Name}";
                    if (_propAnimFrames.ContainsKey(key)) continue;

                    // Find parent animation in spritesheet by prefix
                    List<SpriteFrame> parentFrames = null;
                    if (!string.IsNullOrEmpty(anim.Prefix))
                        parentFrames = sheet.GetAnimationFuzzy(anim.Prefix);
                    parentFrames ??= sheet.Animations.Values.FirstOrDefault();
                    if (parentFrames == null || parentFrames.Count == 0) continue;

                    if (anim.FrameIndices != null && anim.FrameIndices.Length > 0)
                    {
                        // Build sub-animation from specific frame indices
                        var subFrames = new List<SpriteFrame>();
                        foreach (int idx in anim.FrameIndices)
                        {
                            if (idx >= 0 && idx < parentFrames.Count)
                                subFrames.Add(parentFrames[idx]);
                        }
                        if (subFrames.Count > 0)
                            _propAnimFrames[key] = subFrames;
                    }
                    else
                    {
                        _propAnimFrames[key] = parentFrames;
                    }
                }
            }
        }
    }

    private void DrawWeekTitles(SpriteBatch sb)
    {
        // LevelTitle: title graphic screenCenter(X) at the lerped y, alpha 1 selected / 0.6 others; flashing
        // selected title toggles colour WHITE <-> 0xFF33FFFF at 20 fps after confirm.
        for (int i = 0; i < _weeks.Count; i++)
        {
            float y = _titleY[i];
            if (y > FNFGame.SCREEN_HEIGHT || y < -200) continue;
            bool sel = i == _selectedIndex;
            var col = (sel && _flashing && _flashCyan) ? new Color(0x33, 0xFF, 0xFF) : Color.White;
            col = new Color(col, sel ? 1f : 0.6f);
            if (_weekBanners.TryGetValue(_weeks[i].Id, out var banner) && banner != null && banner != Assets.Pixel)
            {
                float x = (int)((FNFGame.SCREEN_WIDTH - banner.Width) / 2f);
                sb.Draw(banner, new Vector2(x, (float)Math.Floor(y)), col);
            }
            else if (!_weeks[i].IsPsych) AtlasText.Bold?.DrawCentered(sb, _weeks[i].Name, y, col.A / 255f, _time);
        }
    }

    private string SongDisplayName(string id)
    {
        if (_songNames.TryGetValue(id, out var n)) return n;
        n = FormatSongName(id);
        try
        {
            string meta = Assets.ResolvePath($"songs/{id}/charts/meta.json");
            if (meta != null && global::FNF_MonoGame.Engine.FsCI.FileExists(meta))
            {
                var jo = Newtonsoft.Json.Linq.JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(meta));
                n = (string)jo["songName"] ?? n;
            }
        }
        catch { }
        _songNames[id] = n;
        return n;
    }

    private void DrawTrackList(SpriteBatch sb, WeekData w)
    {
        // tracklistText: FlxText(0, 400 + 100, 0, "TRACKS\n\n" + songNames.join("\n"), VCR 32), CENTER, colour 0xFFE55777,
        // screenCenter(X) then x -= FlxG.width * 0.33.
        var lines = new List<string> { "TRACKS", "" };
        foreach (var s in w.Songs) lines.Add(SongDisplayName(s));
        float maxW = 0; foreach (var l in lines) maxW = Math.Max(maxW, MenuUI.FlxTextWidth(32, l) - 4f);
        float x0 = (FNFGame.SCREEN_WIDTH - (maxW + 4f)) / 2f - FNFGame.SCREEN_WIDTH * 0.33f;
        var col = new Color(0xE5, 0x57, 0x77);
        float y = BANNER_HEIGHT + 100;
        foreach (var l in lines)
        {   // CENTER alignment inside the text field (OpenFL rounds the line offset)
            MenuUI.FlxText(sb, 32, l, x0 + (float)Math.Floor((maxW - (MenuUI.FlxTextWidth(32, l) - 4f)) / 2f), y, col);
            y += MenuUI.FlxTextLineHeight(32);
        }
    }
    private const float TRACK_LINE_H = 32f;

    private string FormatSongName(string n)
    {
        var w = n.Replace("-", " ").Replace("_", " ").Split(' ');
        return string.Join(" ", w.Select(x => x.Length > 0 ? char.ToUpper(x[0]) + (x.Length > 1 ? x[1..] : "") : ""));
    }

    private SpriteFrame DiffFrame(string diff, out Texture2D tex)
    {
        tex = null;
        if (!_diffAtlases.TryGetValue(diff, out var sh))
        {
            sh = Assets.ResolvePath($"menus/story_menu/difficulties/{diff}.xml") != null ? SpriteSheet.Load(Game, $"menus/story_menu/difficulties/{diff}") : null;
            _diffAtlases[diff] = sh;
        }
        if (sh != null)
        {
            var fr = sh.GetAnimation("idle");
            if (fr != null && fr.Count > 0)
            {
                var f = fr[HighscoreManager.Data.FlashingLights ? (int)(_time * 24f) % fr.Count : 0];
                tex = f.Texture ?? sh.Texture; return f;
            }
        }
        return null;
    }

    private void DrawDifficulty(SpriteBatch sb)
    {
        // leftDifficultyArrow (FlxG.width - 410, 480) 'leftIdle0'/'leftConfirm0' (press while UI_LEFT held),
        // rightDifficultyArrow (FlxG.width - 35, 480); difficultySprite x = left.x + left.width + 10 + (normal.w - w)/2,
        // y tweened from left.y - 15 to left.y + 10 - (h - normal.h)/2 and alpha 0 -> 1 in 0.07 s (linear).
        if (_arrowsSheet == null) return;
        var lf = _arrowsSheet.GetAnimation(Input.LeftHeld && !_confirmed ? "leftConfirm" : "leftIdle") ?? _arrowsSheet.GetAnimation("leftIdle");
        var rf = _arrowsSheet.GetAnimation(Input.RightHeld && !_confirmed ? "rightConfirm" : "rightIdle") ?? _arrowsSheet.GetAnimation("rightIdle");
        var li = _arrowsSheet.GetAnimation("leftIdle");
        float lx = FNFGame.SCREEN_WIDTH - 410, ly = 480, rx = FNFGame.SCREEN_WIDTH - 35;
        int lw = li != null && li.Count > 0 ? (li[0].FrameWidth > 0 ? li[0].FrameWidth : li[0].SourceRect.Width) : 49;
        void DrawF(SpriteFrame f, float x, float y) { if (f != null) sb.Draw(f.Texture ?? _arrowsSheet.Texture, new Vector2(x + f.Offset.X, y + f.Offset.Y), f.SourceRect, Color.White); }
        if (lf != null && lf.Count > 0) DrawF(lf[0], lx, ly);
        if (rf != null && rf.Count > 0) DrawF(rf[0], rx, ly);

        string diff = _diffShown;
        var nTex = _diffTextures.TryGetValue("normal", out var nt) ? nt : null;
        float nW = nTex?.Width ?? 0, nH = nTex?.Height ?? 0;
        var af = DiffFrame(diff, out var atex);
        Texture2D tex = null; float dw, dh;
        if (af != null) { dw = af.FrameWidth > 0 ? af.FrameWidth : af.SourceRect.Width; dh = af.FrameHeight > 0 ? af.FrameHeight : af.SourceRect.Height; }
        else
        {
            if (!_diffTextures.TryGetValue(diff, out tex) || tex == null || tex == Assets.Pixel) tex = Assets.LoadTexture($"menus/story_menu/difficulties/{diff}.png");
            if (tex == null || tex == Assets.Pixel) return;
            dw = tex.Width; dh = tex.Height;
        }
        float t = Math.Min(1f, _diffTweenT / 0.07f);
        float dx = lx + lw + 10 + (nW - dw) / 2f;
        float y0 = ly - 15, y1 = ly + 10 - (dh - nH) / 2f;
        float dy = y0 + (y1 - y0) * t;
        var col = new Color(Color.White, t);
        if (af != null) sb.Draw(atex, new Vector2(dx + af.Offset.X, dy + af.Offset.Y), af.SourceRect, col);
        else sb.Draw(tex, new Vector2(dx, dy), col);
    }
}

// ?? JSON models ??
public class LevelJson
{
    [JsonProperty("name")] public string Name { get; set; }
    [JsonProperty("songs")] public List<string> Songs { get; set; }
    [JsonProperty("background")] public string Background { get; set; }
    [JsonProperty("titleAsset")] public string TitleAsset { get; set; }
    [JsonProperty("props")] public List<LevelPropJson> Props { get; set; }
    [JsonProperty("difficulties")] public List<string> Difficulties { get; set; }
}

public class LevelPropJson
{
    [JsonProperty("assetPath")] public string AssetPath { get; set; }
    [JsonProperty("scale")] public float Scale { get; set; } = 1f;
    [JsonProperty("offsets")] public List<float> Offsets { get; set; }
    [JsonProperty("animations")] public List<LevelPropAnimJson> Animations { get; set; }
    [JsonProperty("danceEvery")] public int? DanceEvery { get; set; }
    [JsonProperty("startingAnimation")] public string StartingAnimation { get; set; }
    [JsonProperty("flipX")] public bool FlipX { get; set; }
    [JsonProperty("isPixel")] public bool IsPixel { get; set; }
    [JsonProperty("alpha")] public float Alpha { get; set; } = 1f;
}

public class LevelPropAnimJson
{
    [JsonProperty("name")] public string Name { get; set; }
    [JsonProperty("prefix")] public string Prefix { get; set; }
    [JsonProperty("frameRate")] public int FrameRate { get; set; } = 24;
    [JsonProperty("frameIndices")] public int[] FrameIndices { get; set; }
    [JsonProperty("offsets")] public List<float> Offsets { get; set; }
    [JsonProperty("looped")] public bool Looped { get; set; }
}

public partial class WeekData
{
    public string Id { get; set; }
    public string Name { get; set; }
    public List<string> Songs { get; set; } = new();
    public Color BackgroundColor { get; set; }
    public List<string> PropNames { get; set; } = new();
    public List<LevelPropJson> PropData { get; set; } = new();
    public string[] Difficulties { get; set; } = { "easy", "normal", "hard" };
    public bool Locked { get; set; }
}

