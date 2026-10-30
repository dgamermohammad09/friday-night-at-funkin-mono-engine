using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine;
using FNF_MonoGame.Gameplay;
using FontStashSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq;

namespace FNF_MonoGame.Scenes;

public class StageEditorScene : Scene
{
    const int LW = 240, RW = 250, TH = 34, SH = 22;

    private readonly List<string> _stageNames = new();
    private int _stageIndex;
    private JObject _stageData;
    private string _currentStage = "";
    private string _stageFilePath = "";

    private readonly List<PropInfo> _props = new();
    private readonly List<CharInfo> _chars = new();
    private int _selectedProp = -1, _selectedChar = -1;
    private bool _dirty;

    private float _camWorldX, _camWorldY;
    private float _zoom = 1.0f;
    private float _defaultCamZoom = 1.0f;

    private int _listScroll, _tab;
    private bool _texturedMode = true;
    private bool _snapToGrid;
    private float _snapSize = 50f;
    private float _nudgeStep = 1f;

    private readonly Dictionary<int, Texture2D> _propTextures = new();
    private readonly Dictionary<int, SpriteSheet> _propSheets = new();
    private readonly Dictionary<int, AnimatedSprite> _propSprites = new();
    private readonly Dictionary<int, AnimatedSprite> _charSprites = new();

    private bool _dragging, _dragCamOffset;
    private Vector2 _dragStart;
    private readonly Stack<UndoAction> _undoStack = new();
    private Vector2 _clipboardPos;

    // Professional UI state
    private string _searchQuery = "";
    private bool _showContextMenu;
    private Vector2 _contextMenuPos;
    private int _contextMenuSelection = -1;
    private Vector2 _mouseWorldPos;
    private readonly float[] _snapPresets = { 1f, 5f, 10f, 25f, 50f, 100f };
    private int _snapPresetIndex = 4;
    private bool _showRulers = true;
    private bool _showAxis = true;
    private bool _showWorldCoords = true;
    private bool _showAllLabels = true;
    private bool _showInfoOverlay = true;
    private bool _showSnapDots = true;
    private float _nudgeRepeatTimer;
    private const float NUDGE_REPEAT_DELAY = 0.4f;
    private const float NUDGE_REPEAT_RATE = 0.05f;
    private string _renameBuffer = "";
    private bool _renaming;

    private readonly List<string> _allCharacterNames = new();
    private AnimatedSprite _abotSprite;
    private bool _showAbot;

    private readonly List<SongVariant> _songVariants = new();
    private int _songVariantIndex;

    class PropInfo { public string Name, AssetPath; public float X, Y, ScaleX = 1, ScaleY = 1; public int ZIndex; public float ScrollX = 1, ScrollY = 1; public bool HasAnims; public bool Locked; }
    class CharInfo { public string Name, CharacterName; public float X, Y, CamX, CamY; public int ZIndex; public float Scale = 1f; public float OffsetX, OffsetY; public float CharCamX, CharCamY; public bool Locked; }
    record UndoAction(string Type, int Index, float OldX, float OldY, float OldCamX = 0, float OldCamY = 0);
    class SongVariant { public string Name, Player, Opponent, Girlfriend; }

    private static readonly Dictionary<string, List<SongVariant>> StageVariants = new()
    {
        ["tankmanBattlefield"] = new() { new() { Name = "Ugh / Guns", Player = "bf", Opponent = "tankman", Girlfriend = "gf-tankmen" }, new() { Name = "Stress", Player = "bf-holding-gf", Opponent = "tankman", Girlfriend = "pico-speaker" } },
        ["tankmanBattlefieldErect"] = new() { new() { Name = "Ugh/Guns (Pico)", Player = "pico-playable", Opponent = "tankman", Girlfriend = "nene-tankmen" }, new() { Name = "Stress (Pico)", Player = "pico-holding-nene", Opponent = "tankman-bloody", Girlfriend = "otis-speaker" } },
    };

    public override void Load()
    {
        string root = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content", "data", "stages");
        if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(root))
        {
            foreach (var f in global::FNF_MonoGame.Engine.FsCI.GetFiles(root, "*.json"))
                _stageNames.Add(System.IO.Path.GetFileNameWithoutExtension(f));
            _stageNames.Sort(StringComparer.OrdinalIgnoreCase);
        }
        string charRoot = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content", "data", "characters");
        if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(charRoot))
        {
            foreach (var f in global::FNF_MonoGame.Engine.FsCI.GetFiles(charRoot, "*.json"))
                _allCharacterNames.Add(System.IO.Path.GetFileNameWithoutExtension(f));
            _allCharacterNames.Sort(StringComparer.OrdinalIgnoreCase);
        }
        if (_stageNames.Count > 0) LoadStage(0);
    }

    public override void Unload() { }

    private void LoadStage(int index)
    {
        if (index < 0 || index >= _stageNames.Count) return;
        _stageIndex = index;
        _currentStage = _stageNames[index];
        _props.Clear(); _chars.Clear();
        _propTextures.Clear(); _propSheets.Clear(); _propSprites.Clear(); _charSprites.Clear();
        _selectedProp = -1; _selectedChar = -1;
        _renaming = false;
        _showContextMenu = false;
        _dirty = false; _undoStack.Clear();

        _stageFilePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content", "data", "stages", _currentStage + ".json");
        if (!global::FNF_MonoGame.Engine.FsCI.FileExists(_stageFilePath)) return;
        _stageData = JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(_stageFilePath));

        var propsArr = _stageData["props"] as JArray;
        if (propsArr != null)
        {
            foreach (var p in propsArr)
            {
                var pos = p["position"] as JArray;
                var sc = p["scale"] as JArray;
                var scroll = p["scroll"] as JArray;
                var anims = p["animations"] as JArray;
                _props.Add(new PropInfo
                {
                    Name = p["name"]?.ToString() ?? "?",
                    AssetPath = p["assetPath"]?.ToString() ?? "",
                    X = pos?.Count > 0 ? (float)pos[0] : 0,
                    Y = pos?.Count > 1 ? (float)pos[1] : 0,
                    ScaleX = sc?.Count > 0 ? (float)sc[0] : 1,
                    ScaleY = sc?.Count > 1 ? (float)sc[1] : 1,
                    ZIndex = (int)(p["zIndex"] ?? 0),
                    ScrollX = scroll?.Count > 0 ? (float)scroll[0] : 1,
                    ScrollY = scroll?.Count > 1 ? (float)scroll[1] : 1,
                    HasAnims = anims != null && anims.Count > 0,
                    Locked = (bool?)p["locked"] ?? false
                });
            }
        }

        _songVariants.Clear(); _songVariantIndex = 0;
        if (StageVariants.TryGetValue(_currentStage, out var variants))
            _songVariants.AddRange(variants);

        var charsObj = _stageData["characters"] as JObject;
        if (charsObj != null)
        {
            foreach (var kvp in charsObj)
            {
                var cv = kvp.Value;
                var pos = cv["position"] as JArray;
                var cam = cv["cameraOffsets"] as JArray;
                string slot = kvp.Key;
                string charName = GetVariantCharacter(slot) ?? GetDefaultCharacterForSlot(slot, _currentStage);
                _chars.Add(new CharInfo
                {
                    Name = slot,
                    CharacterName = charName,
                    X = pos?.Count > 0 ? (float)pos[0] : 0,
                    Y = pos?.Count > 1 ? (float)pos[1] : 0,
                    CamX = cam?.Count > 0 ? (float)cam[0] : 0,
                    CamY = cam?.Count > 1 ? (float)cam[1] : 0,
                    ZIndex = (int)(cv["zIndex"] ?? 0),
                    Scale = (float?)cv["scale"] ?? 1f,
                    Locked = (bool?)cv["locked"] ?? false
                });
            }
        }

        _defaultCamZoom = _stageData != null ? (float)(_stageData["cameraZoom"] ?? 1.0) : 1.0f;
        _zoom = _defaultCamZoom;
        _searchQuery = "";

        LoadStageTextures();
        LoadCharacterSprites();
        MatchGameCamera();
    }

    private void MatchGameCamera()
    {
        var dad = _chars.FirstOrDefault(c => c.Name.Equals("dad", StringComparison.OrdinalIgnoreCase));
        if (dad == null) return;
        int dadIdx = _chars.IndexOf(dad);
        float midX = dad.X + dad.OffsetX;
        float midY = dad.Y;
        if (_charSprites.TryGetValue(dadIdx, out var dadSpr) && dadSpr.Sheet != null)
        {
            float scale = dadSpr.Scale.X;
            int fh = dadSpr.GetFrameHeight();
            if (fh <= 0) fh = 300;
            midY = dad.Y + dad.OffsetY - fh * scale / 2f;
        }
        float totalCamX = dad.CamX + dad.CharCamX;
        float totalCamY = dad.CamY + dad.CharCamY;
        _camWorldX = midX + totalCamX;
        _camWorldY = midY + totalCamY;
        _zoom = _defaultCamZoom;
    }

    private Vector2 WorldToScreen(float worldX, float worldY, float scrollX, float scrollY)
    {
        // Exact match to PlayScene camera math:
        // PlayScene: screenPixel = (worldX - (_camWorldX - 640/zoom)*scroll) * zoom
        //          = worldX*zoom - _camWorldX*zoom*scroll + 640*scroll
        float sx = worldX * _zoom - _camWorldX * _zoom * scrollX + 640f * scrollX;
        float sy = worldY * _zoom - _camWorldY * _zoom * scrollY + 360f * scrollY;
        return new Vector2(sx, sy);
    }

    private Vector2 ScreenToWorld(float screenX, float screenY, float scrollX = 1f, float scrollY = 1f)
    {
        // Inverse of WorldToScreen
        float wx = (screenX - 640f * scrollX) / _zoom + _camWorldX * scrollX;
        float wy = (screenY - 360f * scrollY) / _zoom + _camWorldY * scrollY;
        return new Vector2(wx, wy);
    }

    private void ZoomToFit()
    {
        if (_props.Count == 0 && _chars.Count == 0) return;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var p in _props) { minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y); }
        foreach (var c in _chars) { minX = Math.Min(minX, c.X); minY = Math.Min(minY, c.Y); maxX = Math.Max(maxX, c.X); maxY = Math.Max(maxY, c.Y); }
        float margin = 200f;
        minX -= margin; minY -= margin; maxX += margin; maxY += margin;
        float w = maxX - minX; float h = maxY - minY;
        float vpW = FNFGame.SCREEN_WIDTH;
        float vpH = FNFGame.SCREEN_HEIGHT;
        _zoom = Math.Min(vpW / w, vpH / h);
        _zoom = Math.Clamp(_zoom, 0.03f, 3f);
        _camWorldX = (minX + maxX) / 2f;
        _camWorldY = (minY + maxY) / 2f;
        EditorUI.ShowToast($"Zoom to Fit: {_zoom:F2}x");
    }

    private void ZoomToSelected()
    {
        float targetX = 0, targetY = 0;
        if (_selectedProp >= 0) { targetX = _props[_selectedProp].X; targetY = _props[_selectedProp].Y; }
        else if (_selectedChar >= 0) { targetX = _chars[_selectedChar].X; targetY = _chars[_selectedChar].Y; }
        else return;
        _camWorldX = targetX;
        _camWorldY = targetY;
        _zoom = _defaultCamZoom;
        EditorUI.ShowToast($"Zoom to Selected: {_zoom:F2}x");
    }

    private string ResolveStageFolder(string stageName)
    {
        string resolved = stageName switch
        {
            "mainStage" or "stage" => "stage",
            "mainStageErect" or "stageErect" or "stage_erect" => "stage_erect",
            "spookyMansion" or "spookyMansionErect" or "spooky" => "spooky",
            "phillyTrain" or "philly" or "philly-train" or "philly_train_erect" => "philly_train",
            "limoRide" or "limoRideErect" or "limo" or "limo_erect" => "limo",
            "phillyStreets" or "phillyStreetsErect" or "philly_streets" => "philly_streets",
            "mallXmas" or "mallXmasErect" or "mallEvil" => "christmas",
            "school" or "schoolEvil" or "schoolErect" or "schoolEvilErect" or "weeb" => "school",
            "tankmanBattlefield" or "tankmanBattlefieldErect" => "tankman",
            "phillyBlazin" => "philly_blazin",
            _ => stageName
        };
        if (Assets.ResolveDirectory($"game/stages/{resolved}") != null) return resolved;
        if (Assets.ResolveDirectory($"game/stages/{stageName}") != null) return stageName;
        return "stage";
    }

    private string GetDefaultCharacterForSlot(string slot, string stageName)
    {
        if (stageName.Equals("tankmanBattlefield", StringComparison.OrdinalIgnoreCase)
            || stageName.Equals("tankmanBattlefieldErect", StringComparison.OrdinalIgnoreCase))
            return slot switch { "bf" => "bf", "dad" => "tankman", "gf" => "gf-tankmen", _ => slot };
        if (stageName.Equals("phillyBlazin", StringComparison.OrdinalIgnoreCase))
            return slot switch { "bf" => "pico-blazin", "dad" => "darnell-blazin", "gf" => "nene", _ => slot };
        if (stageName.Equals("mainStage", StringComparison.OrdinalIgnoreCase)
            || stageName.Equals("mainStageErect", StringComparison.OrdinalIgnoreCase)
            || stageName.Equals("stage", StringComparison.OrdinalIgnoreCase))
            return slot switch { "bf" => "bf", "dad" => "dad", "gf" => "gf", _ => slot };
        if (stageName.Equals("spookyMansion", StringComparison.OrdinalIgnoreCase)
            || stageName.Equals("spookyMansionErect", StringComparison.OrdinalIgnoreCase))
            return slot switch { "bf" => "bf", "dad" => "spooky", "gf" => "gf", _ => slot };
        if (stageName.Equals("phillyTrain", StringComparison.OrdinalIgnoreCase)
            || stageName.Equals("phillyTrainErect", StringComparison.OrdinalIgnoreCase))
            return slot switch { "bf" => "bf", "dad" => "pico", "gf" => "gf", _ => slot };
        if (stageName.Equals("limoRide", StringComparison.OrdinalIgnoreCase)
            || stageName.Equals("limoRideErect", StringComparison.OrdinalIgnoreCase))
            return slot switch { "bf" => "bf", "dad" => "mom", "gf" => "gf-car", _ => slot };
        if (stageName.Equals("mallXmas", StringComparison.OrdinalIgnoreCase)
            || stageName.Equals("mallXmasErect", StringComparison.OrdinalIgnoreCase)
            || stageName.Equals("mallEvil", StringComparison.OrdinalIgnoreCase))
            return slot switch { "bf" => "pico-christmas", "dad" => "parents-christmas", "gf" => "nene-christmas", _ => slot };
        if (stageName.Equals("school", StringComparison.OrdinalIgnoreCase)
            || stageName.Equals("schoolErect", StringComparison.OrdinalIgnoreCase))
            return slot switch { "bf" => "bf-pixel", "dad" => "senpai", "gf" => "gf-pixel", _ => slot };
        return slot;
    }

    private string GetVariantCharacter(string slot)
    {
        if (_songVariants.Count == 0 || _songVariantIndex >= _songVariants.Count) return null;
        var v = _songVariants[_songVariantIndex];
        return slot.ToLowerInvariant() switch
        {
            "bf" or "player" => v.Player,
            "dad" or "opponent" => v.Opponent,
            "gf" or "girlfriend" => v.Girlfriend,
            _ => null
        };
    }

    private void CycleSongVariant()
    {
        if (_songVariants.Count == 0) return;
        _songVariantIndex = (_songVariantIndex + 1) % _songVariants.Count;
        foreach (var ch in _chars)
        {
            if (ch.Locked) continue;
            string vc = GetVariantCharacter(ch.Name);
            if (!string.IsNullOrEmpty(vc)) { ch.CharacterName = vc; _dirty = true; }
        }
        LoadCharacterSprites();
        MatchGameCamera();
        EditorUI.ShowToast($"Variant: {_songVariants[_songVariantIndex].Name}");
    }

    private void LoadStageTextures()
    {
        var propsArr = _stageData?["props"] as JArray;
        if (propsArr == null) return;
        for (int i = 0; i < _props.Count && i < propsArr.Count; i++)
        {
            var p = _props[i];
            if (string.IsNullOrEmpty(p.AssetPath)) continue;
            if (p.HasAnims)
            {
                var sheet = LoadPropSheet(p.AssetPath);
                if (sheet != null)
                {
                    _propSheets[i] = sheet;
                    var animsArr = propsArr[i]["animations"] as JArray;
                    string startAnim = propsArr[i]["startingAnimation"]?.ToString();
                    RegisterPropAnimations(sheet, animsArr, ref startAnim);
                    var sprite = new AnimatedSprite { Sheet = sheet, Position = new Vector2(p.X, p.Y), Scale = new Vector2(p.ScaleX, p.ScaleY) };
                    if (!string.IsNullOrEmpty(startAnim))
                    {
                        var f = sheet.GetAnimationFuzzy(startAnim);
                        if (f != null && f.Count > 0)
                        {
                            string key = sheet.Animations.FirstOrDefault(k => k.Value == f).Key ?? startAnim;
                            sprite.PlayAnimation(key, loop: true);
                        }
                    }
                    _propSprites[i] = sprite;
                }
            }
            else
            {
                var tex = LoadPropTexture(p.AssetPath);
                if (tex != null) _propTextures[i] = tex;
            }
        }
    }

    private void RegisterPropAnimations(SpriteSheet sheet, JArray animsArr, ref string startAnim)
    {
        if (animsArr == null) return;
        foreach (var a in animsArr)
        {
            string prefix = a["prefix"]?.ToString() ?? a["name"]?.ToString() ?? "";
            string name = a["name"]?.ToString() ?? prefix;
            if (string.IsNullOrEmpty(prefix)) continue;
            var frames = sheet.GetAnimationFuzzy(prefix);
            if (frames == null || frames.Count == 0) continue;
            var idxArr = a["frameIndices"] as JArray;
            if (idxArr != null && idxArr.Count > 0)
            {
                var sub = new List<SpriteFrame>();
                foreach (int idx in idxArr.Select(j => (int)j))
                    if (idx >= 0 && idx < frames.Count) sub.Add(frames[idx]);
                if (sub.Count > 0) sheet.Animations[name] = sub;
            }
            else if (name != prefix && !sheet.Animations.ContainsKey(name))
                sheet.Animations[name] = frames;
            if (string.IsNullOrEmpty(startAnim)) startAnim = name;
        }
    }

    private Texture2D LoadPropTexture(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath) || assetPath.StartsWith("#")) return null;
        string stageFolder = ResolveStageFolder(_currentStage);
        string resolved = assetPath;
        if (assetPath.Contains('/'))
        {
            string[] parts = assetPath.Split('/');
            string prefix = ResolveStageFolder(parts[0]);
            if (prefix == stageFolder) resolved = string.Join('/', parts[1..]);
            else if (parts[0] == "erect") resolved = string.Join('/', parts[1..]);
        }
        var tex = Assets.LoadTexture($"game/stages/{stageFolder}/{resolved}.png");
        if (tex != null && tex != Assets.Pixel) return tex;
        tex = Assets.LoadTexture($"images/{assetPath}.png");
        if (tex != null && tex != Assets.Pixel) return tex;
        return null;
    }

    private SpriteSheet LoadPropSheet(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath) || assetPath.StartsWith("#")) return null;
        string stageFolder = ResolveStageFolder(_currentStage);
        string propPath = assetPath;
        if (assetPath.Contains('/'))
        {
            string[] parts = assetPath.Split('/');
            string prefix = ResolveStageFolder(parts[0]);
            if (prefix == stageFolder) propPath = string.Join('/', parts[1..]);
            else if (parts[0] == "erect") propPath = string.Join('/', parts[1..]);
        }
        var sheet = SpriteSheet.Load(Game, $"game/stages/{stageFolder}/{propPath}");
        if (sheet == null && assetPath.Contains('/'))
        {
            string[] parts = assetPath.Split('/');
            string subFolder = ResolveStageFolder(parts[0]);
            string fileName = parts[^1];
            sheet = SpriteSheet.Load(Game, $"game/stages/{subFolder}/{fileName}");
            if (sheet == null) sheet = SpriteSheet.Load(Game, $"game/stages/{subFolder}/erect/{fileName}");
        }
        if (sheet == null) sheet = SpriteSheet.Load(Game, $"images/{assetPath}");
        return sheet;
    }

    private void LoadCharacterSprites()
    {
        _charSprites.Clear();
        _abotSprite = null;
        _showAbot = false;

        for (int i = 0; i < _chars.Count; i++)
        {
            string charName = _chars[i].CharacterName ?? _chars[i].Name;
            if (string.IsNullOrEmpty(charName)) continue;

            var charJson = Assets.LoadJson<CharacterJsonData>($"data/characters/{charName}");
            if (charJson == null) continue;

            if (charJson.Offsets != null && charJson.Offsets.Length >= 2)
            {
                _chars[i].OffsetX = charJson.Offsets[0];
                _chars[i].OffsetY = charJson.Offsets[1];
            }
            if (charJson.CameraOffsets != null && charJson.CameraOffsets.Length >= 2)
            {
                _chars[i].CharCamX = charJson.CameraOffsets[0];
                _chars[i].CharCamY = charJson.CameraOffsets[1];
            }

            var sheet = LoadCharacterSheet(charName, charJson);
            if (sheet == null) continue;

            if (charJson.Animations != null)
            {
                foreach (var anim in charJson.Animations)
                {
                    string prefix = anim.Prefix ?? anim.Name ?? "";
                    string name = anim.Name ?? prefix;
                    if (string.IsNullOrEmpty(prefix)) continue;
                    var frames = sheet.GetAnimationFuzzy(prefix);
                    if (frames == null || frames.Count == 0) continue;
                    if (anim.FrameIndices != null && anim.FrameIndices.Length > 0)
                    {
                        var sub = new List<SpriteFrame>();
                        foreach (int idx in anim.FrameIndices)
                            if (idx >= 0 && idx < frames.Count) sub.Add(frames[idx]);
                        if (sub.Count > 0) sheet.Animations[name] = sub;
                    }
                    else if (!sheet.Animations.ContainsKey(name))
                        sheet.Animations[name] = frames;
                }
            }

            var sprite = new AnimatedSprite { Sheet = sheet };
            float charJsonScale = charJson.Scale > 0 ? charJson.Scale : 1f;
            float finalScale = charJsonScale * _chars[i].Scale;
            sprite.Scale = new Vector2(finalScale, finalScale);
            sprite.Effects = charJson.FlipX ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

            string startAnim = charJson.StartingAnimation ?? "idle";
            var idleFrames = sheet.GetAnimationFuzzy(startAnim);
            if (idleFrames == null || idleFrames.Count == 0) idleFrames = sheet.GetAnimationFuzzy("idle");
            if (idleFrames == null || idleFrames.Count == 0) idleFrames = sheet.GetAnimationFuzzy("danceRight");
            if (idleFrames != null && idleFrames.Count > 0)
            {
                string key = sheet.Animations.FirstOrDefault(k => k.Value == idleFrames).Key ?? startAnim;
                sprite.PlayAnimation(key, loop: true);
            }
            _charSprites[i] = sprite;
        }

        LoadAbot();
    }

    private SpriteSheet LoadCharacterSheet(string charName, CharacterJsonData charJson)
    {
        bool preferComposite = charJson.RenderType is "animateatlas" or "multianimateatlas";
        var sparrowPaths = new List<string>();
        var spritemapPaths = new List<string>();
        if (!string.IsNullOrEmpty(charJson.AssetPath))
        {
            string ap = charJson.AssetPath;
            if (ap.StartsWith("shared:")) ap = ap["shared:".Length..];
            sparrowPaths.Add($"images/{ap}");
            sparrowPaths.Add($"game/{ap}");
            spritemapPaths.Add($"images/{ap}");
            spritemapPaths.Add($"game/{ap}");
        }
        sparrowPaths.Add($"game/characters/{charName}/sprites");
        sparrowPaths.Add($"game/characters/{charName}");
        sparrowPaths.Add($"images/characters/{charName}");
        spritemapPaths.Add($"game/characters/{charName}/default_spritemap");
        spritemapPaths.Add($"game/characters/{charName}");
        spritemapPaths.Add($"images/characters/{charName}");

        SpriteSheet sheet = null;
        if (preferComposite)
        {
            foreach (var path in spritemapPaths) { sheet = SpriteSheet.Load(Game, path, preRenderComposites: true); if (sheet != null) break; }
            if (sheet == null) foreach (var path in sparrowPaths) { sheet = TryLoadSparrow(path); if (sheet != null) break; }
        }
        else
        {
            foreach (var path in sparrowPaths) { sheet = TryLoadSparrow(path); if (sheet != null) break; }
            if (sheet == null) foreach (var path in spritemapPaths) { sheet = SpriteSheet.Load(Game, path, preRenderComposites: true); if (sheet != null) break; }
        }
        return sheet;
    }

    private SpriteSheet TryLoadSparrow(string basePath)
    {
        string contentPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content");
        string xmlPath = Assets.ResolvePath(basePath + ".xml")
                      ?? (global::FNF_MonoGame.Engine.FsCI.FileExists(System.IO.Path.Combine(contentPath, basePath + ".xml"))
                          ? System.IO.Path.Combine(contentPath, basePath + ".xml") : null);
        if (xmlPath == null) return null;
        return SpriteSheet.Load(Game, basePath, preRenderComposites: false);
    }

    private void LoadAbot()
    {
        var gfCh = _chars.FirstOrDefault(c => c.Name.Equals("gf", StringComparison.OrdinalIgnoreCase));
        if (gfCh == null) return;
        string gfName = gfCh.CharacterName ?? gfCh.Name;
        bool isNene = gfName.Equals("nene", StringComparison.OrdinalIgnoreCase)
            || gfName.Equals("nene-christmas", StringComparison.OrdinalIgnoreCase)
            || gfName.Equals("nene-tankmen", StringComparison.OrdinalIgnoreCase)
            || gfName.Equals("nene-pixel", StringComparison.OrdinalIgnoreCase);
        if (!isNene) return;

        _showAbot = true;
        var sheet = SpriteSheet.Load(Game, "images/characters/abot/abotSystem", preRenderComposites: true);
        if (sheet == null) sheet = SpriteSheet.Load(Game, "game/characters/nene/abot/speaker", preRenderComposites: true);
        if (sheet == null) return;
        string bodyAnim = sheet.Animations.Keys.FirstOrDefault(k => k.Equals("Abot System", StringComparison.OrdinalIgnoreCase))
            ?? sheet.Animations.Keys.FirstOrDefault();
        if (string.IsNullOrEmpty(bodyAnim)) return;
        var sprite = new AnimatedSprite { Sheet = sheet };
        sprite.PlayAnimation(bodyAnim, loop: true);
        _abotSprite = sprite;
    }

    private void SaveStage()
    {
        if (_stageData == null || string.IsNullOrEmpty(_stageFilePath)) return;
        var propsArr = _stageData["props"] as JArray;
        if (propsArr != null)
        {
            for (int i = 0; i < _props.Count; i++)
            {
                var p = _props[i];
                if (i < propsArr.Count)
                {
                    propsArr[i]["position"] = new JArray(Math.Round(p.X, 1), Math.Round(p.Y, 1));
                    propsArr[i]["scale"] = new JArray(Math.Round(p.ScaleX, 2), Math.Round(p.ScaleY, 2));
                    propsArr[i]["zIndex"] = p.ZIndex;
                    propsArr[i]["locked"] = p.Locked;
                }
                else
                {
                    propsArr.Add(new JObject
                    {
                        ["name"] = p.Name,
                        ["assetPath"] = p.AssetPath,
                        ["position"] = new JArray(Math.Round(p.X, 1), Math.Round(p.Y, 1)),
                        ["scale"] = new JArray(Math.Round(p.ScaleX, 2), Math.Round(p.ScaleY, 2)),
                        ["zIndex"] = p.ZIndex,
                        ["scroll"] = new JArray(p.ScrollX, p.ScrollY),
                        ["locked"] = p.Locked
                    });
                }
            }
        }
        var charsObj = _stageData["characters"] as JObject;
        if (charsObj != null)
        {
            foreach (var ch in _chars)
            {
                if (charsObj[ch.Name] is JObject co)
                {
                    co["position"] = new JArray(Math.Round(ch.X, 1), Math.Round(ch.Y, 1));
                    co["cameraOffsets"] = new JArray(Math.Round(ch.CamX, 1), Math.Round(ch.CamY, 1));
                    co["zIndex"] = ch.ZIndex;
                    co["locked"] = ch.Locked;
                    if (Math.Abs(ch.Scale - 1f) > 0.001f) co["scale"] = Math.Round(ch.Scale, 2);
                }
            }
        }
        System.IO.File.WriteAllText(_stageFilePath, _stageData.ToString(Formatting.Indented));
        _dirty = false;
        EditorUI.ShowToast($"Saved {_currentStage}.json");
    }

    private void CycleSelectedCharacter()
    {
        if (_selectedChar < 0 || _selectedChar >= _chars.Count || _allCharacterNames.Count == 0) return;
        if (_chars[_selectedChar].Locked) { EditorUI.ShowToast("Locked"); return; }
        var ch = _chars[_selectedChar];
        int idx = _allCharacterNames.IndexOf(ch.CharacterName ?? ch.Name);
        idx = (idx + 1) % _allCharacterNames.Count;
        ch.CharacterName = _allCharacterNames[idx];
        _dirty = true;
        LoadCharacterSprites();
        EditorUI.ShowToast($"Char: {ch.CharacterName}");
    }

    private void PlayCharacterAnimation(AnimatedSprite sprite, string animName)
    {
        if (sprite?.Sheet?.Animations == null) return;
        if (sprite.Sheet.Animations.ContainsKey(animName)) { sprite.PlayAnimation(animName, loop: true); EditorUI.ShowToast($"Anim: {animName}"); return; }
        var key = sprite.Sheet.Animations.Keys.FirstOrDefault(k => k.Equals(animName, StringComparison.OrdinalIgnoreCase));
        if (key != null) { sprite.PlayAnimation(key, loop: true); EditorUI.ShowToast($"Anim: {key}"); }
        else EditorUI.ShowToast($"Anim '{animName}' not found");
    }

    public override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        EditorUI.UpdateInput();
        EditorUI.UpdateToast(dt);
        EditorUI.UpdateTooltip(dt);

        if (_texturedMode)
        {
            foreach (var kvp in _propSprites) kvp.Value.Update(dt);
            foreach (var kvp in _charSprites) kvp.Value.Update(dt);
        }

        if (Input.IsPressed(Keys.V) && !Input.IsHeld(Keys.LeftControl))
        {
            _texturedMode = !_texturedMode;
            EditorUI.ShowToast(_texturedMode ? "Textured View" : "Wireframe View");
        }

        if (Input.IsPressed(Keys.S) && Input.IsHeld(Keys.LeftControl)) { SaveStage(); return; }
        if (Input.IsPressed(Keys.Z) && Input.IsHeld(Keys.LeftControl) && _undoStack.Count > 0)
        {
            var u = _undoStack.Pop();
            if (u.Type == "prop" && u.Index < _props.Count) { _props[u.Index].X = u.OldX; _props[u.Index].Y = u.OldY; }
            else if (u.Type == "char" && u.Index < _chars.Count) { _chars[u.Index].X = u.OldX; _chars[u.Index].Y = u.OldY; _chars[u.Index].CamX = u.OldCamX; _chars[u.Index].CamY = u.OldCamY; }
            _dirty = true; EditorUI.ShowToast("Undo");
        }

        int rulerSize = 18;
        var vpRect = new Rectangle(LW + rulerSize, TH + rulerSize, FNFGame.SCREEN_WIDTH - LW - RW - rulerSize, FNFGame.SCREEN_HEIGHT - TH - SH - rulerSize);
        float vpCX = LW + rulerSize + vpRect.Width / 2f;
        float vpCY = TH + rulerSize + vpRect.Height / 2f;

        if (EditorUI.IsHovered(vpRect))
        {
            int scroll = Input.ScrollDelta;
            if (scroll != 0)
            {
                float oldZoom = _zoom;
                if (scroll > 0) _zoom = Math.Min(3f, _zoom * 1.1f);
                if (scroll < 0) _zoom = Math.Max(0.03f, _zoom / 1.1f);
                float screenCX = FNFGame.SCREEN_WIDTH / 2f;
                float screenCY = FNFGame.SCREEN_HEIGHT / 2f;
                float mwx = (EditorUI.MousePos.X - screenCX) / oldZoom + _camWorldX;
                float mwy = (EditorUI.MousePos.Y - screenCY) / oldZoom + _camWorldY;
                _camWorldX = mwx - (EditorUI.MousePos.X - screenCX) / _zoom;
                _camWorldY = mwy - (EditorUI.MousePos.Y - screenCY) / _zoom;
            }

            if (EditorUI.Mouse.RightButton == ButtonState.Pressed && !_showContextMenu)
            {
                _camWorldX -= EditorUI.MouseDelta.X / _zoom;
                _camWorldY -= EditorUI.MouseDelta.Y / _zoom;
            }

            if (EditorUI.MouseClicked && !_showContextMenu)
            {
                _dragCamOffset = Input.IsHeld(Keys.LeftShift);
                TrySelect();
                if (_selectedProp >= 0 || _selectedChar >= 0)
                {
                    _dragging = true;
                    _dragStart = EditorUI.MousePos;
                    if (_selectedProp >= 0)
                        _undoStack.Push(new UndoAction("prop", _selectedProp, _props[_selectedProp].X, _props[_selectedProp].Y));
                    else if (_selectedChar >= 0)
                    {
                        var ch = _chars[_selectedChar];
                        _undoStack.Push(new UndoAction("char", _selectedChar, ch.X, ch.Y, ch.CamX, ch.CamY));
                    }
                }
            }

            if (_dragging && EditorUI.MouseDown && !_showContextMenu)
            {
                var delta = EditorUI.MouseDelta / _zoom;
                if (_selectedProp >= 0 && !_props[_selectedProp].Locked)
                {
                    var pDrag = _props[_selectedProp];
                    pDrag.X += delta.X / _zoom;
                    pDrag.Y += delta.Y / _zoom;
                    if (_snapToGrid) { _props[_selectedProp].X = (float)Math.Round(_props[_selectedProp].X / _snapSize) * _snapSize; _props[_selectedProp].Y = (float)Math.Round(_props[_selectedProp].Y / _snapSize) * _snapSize; }
                    _dirty = true;
                }
                else if (_selectedChar >= 0 && !_chars[_selectedChar].Locked)
                {
                    if (_dragCamOffset)
                    {
                        _chars[_selectedChar].CamX += delta.X;
                        _chars[_selectedChar].CamY += delta.Y;
                        if (_snapToGrid) { _chars[_selectedChar].CamX = (float)Math.Round(_chars[_selectedChar].CamX / _snapSize) * _snapSize; _chars[_selectedChar].CamY = (float)Math.Round(_chars[_selectedChar].CamY / _snapSize) * _snapSize; }
                    }
                    else
                    {
                        _chars[_selectedChar].X += delta.X;
                        _chars[_selectedChar].Y += delta.Y;
                        if (_snapToGrid) { _chars[_selectedChar].X = (float)Math.Round(_chars[_selectedChar].X / _snapSize) * _snapSize; _chars[_selectedChar].Y = (float)Math.Round(_chars[_selectedChar].Y / _snapSize) * _snapSize; }
                    }
                    _dirty = true;
                }
            }

            if (EditorUI.MouseReleased) _dragging = false;
        }
        else
        {
            _dragging = false;
        }

        float sd = 0;
        if (Input.IsPressed(Keys.OemPlus) || Input.IsPressed(Keys.Add)) sd = 0.05f;
        if (Input.IsPressed(Keys.OemMinus) || Input.IsPressed(Keys.Subtract)) sd = -0.05f;
        if (sd != 0)
        {
            if (_selectedProp >= 0)
            {
                _props[_selectedProp].ScaleX = Math.Max(0.05f, _props[_selectedProp].ScaleX + sd);
                _props[_selectedProp].ScaleY = Math.Max(0.05f, _props[_selectedProp].ScaleY + sd);
                _dirty = true;
            }
            else if (_selectedChar >= 0)
            {
                _chars[_selectedChar].Scale = Math.Max(0.05f, _chars[_selectedChar].Scale + sd);
                _dirty = true;
                LoadCharacterSprites();
            }
        }

        if (Input.IsPressed(Keys.A) && _selectedChar >= 0 && !Input.IsHeld(Keys.LeftControl) && _charSprites.TryGetValue(_selectedChar, out var csAnim))
        {
            var keys = csAnim.Sheet.Animations.Keys.ToList();
            int idx = keys.IndexOf(csAnim.CurrentAnimation ?? "");
            idx = (idx + 1) % keys.Count;
            string nextAnim = keys[idx];
            csAnim.PlayAnimation(nextAnim, loop: true);
            EditorUI.ShowToast($"Anim: {nextAnim}");
        }

        if (Input.IsPressed(Keys.Tab)) { CycleSongVariant(); }

        if (Input.IsPressed(Keys.PageUp))
        {
            if (_selectedProp >= 0 && !_props[_selectedProp].Locked) { _props[_selectedProp].ZIndex += 10; _dirty = true; }
            if (_selectedChar >= 0 && !_chars[_selectedChar].Locked) { _chars[_selectedChar].ZIndex += 10; _dirty = true; }
        }
        if (Input.IsPressed(Keys.PageDown))
        {
            if (_selectedProp >= 0 && !_props[_selectedProp].Locked) { _props[_selectedProp].ZIndex -= 10; _dirty = true; }
            if (_selectedChar >= 0 && !_chars[_selectedChar].Locked) { _chars[_selectedChar].ZIndex -= 10; _dirty = true; }
        }

        if (Input.IsPressed(Keys.Delete))
        {
            if (_selectedProp >= 0 && !_props[_selectedProp].Locked)
                RemoveSelectedProp();
            else if (_selectedChar >= 0 && !_chars[_selectedChar].Locked)
                RemoveSelectedChar();
        }

        // Duplicate selected
        if (Input.IsPressed(Keys.D) && Input.IsHeld(Keys.LeftControl))
        {
            if (_selectedProp >= 0) DuplicateProp();
            else if (_selectedChar >= 0) DuplicateChar();
        }

        // Lock/Unlock toggle (K key)
        if (Input.IsPressed(Keys.K) && !Input.IsHeld(Keys.LeftControl))
        {
            if (_selectedProp >= 0) { _props[_selectedProp].Locked = !_props[_selectedProp].Locked; EditorUI.ShowToast(_props[_selectedProp].Locked ? "Prop Locked" : "Prop Unlocked"); }
            else if (_selectedChar >= 0) { _chars[_selectedChar].Locked = !_chars[_selectedChar].Locked; EditorUI.ShowToast(_chars[_selectedChar].Locked ? "Char Locked" : "Char Unlocked"); }
        }

        // Rename (F2)
        if (Input.IsPressed(Keys.F2))
        {
            if (_selectedProp >= 0) { _renaming = true; _renameBuffer = _props[_selectedProp].Name; }
            else if (_selectedChar >= 0) { _renaming = true; _renameBuffer = _chars[_selectedChar].Name; }
        }
        if (_renaming)
        {
            // Simple rename input: A-Z, Backspace, Enter to confirm, Esc to cancel
            for (int k = 0; k < 26; k++)
            {
                Keys key = (Keys)((int)Keys.A + k);
                if (Input.IsPressed(key))
                    _renameBuffer += (char)('a' + k);
            }
            for (int k = 0; k < 10; k++)
            {
                Keys key = (Keys)((int)Keys.D0 + k);
                if (Input.IsPressed(key))
                    _renameBuffer += (char)('0' + k);
            }
            if (Input.IsPressed(Keys.Space)) _renameBuffer += " ";
            if (Input.IsPressed(Keys.OemMinus)) _renameBuffer += "-";
            if (Input.IsPressed(Keys.Back) && _renameBuffer.Length > 0)
                _renameBuffer = _renameBuffer.Substring(0, _renameBuffer.Length - 1);
            if (Input.IsPressed(Keys.Enter))
            {
                if (_selectedProp >= 0) _props[_selectedProp].Name = _renameBuffer;
                else if (_selectedChar >= 0)
                {
                    var oldName = _chars[_selectedChar].Name;
                    _chars[_selectedChar].Name = _renameBuffer;
                    var charsObj = _stageData["characters"] as JObject;
                    if (charsObj != null && charsObj[oldName] is JObject oldChar)
                    {
                        charsObj[_renameBuffer] = oldChar;
                        charsObj.Remove(oldName);
                    }
                }
                _renaming = false;
                _dirty = true;
                EditorUI.ShowToast("Renamed");
            }
            if (Input.IsPressed(Keys.Escape))
                _renaming = false;
        }

        // Toggle all labels
        if (Input.IsPressed(Keys.F3)) { _showAllLabels = !_showAllLabels; EditorUI.ShowToast($"All Labels: {(_showAllLabels ? "ON" : "OFF")}"); }
        // Toggle info overlay
        if (Input.IsPressed(Keys.F1)) { _showInfoOverlay = !_showInfoOverlay; EditorUI.ShowToast($"Info Overlay: {(_showInfoOverlay ? "ON" : "OFF")}"); }
        // Toggle snap dots
        if (Input.IsPressed(Keys.F4)) { _showSnapDots = !_showSnapDots; EditorUI.ShowToast($"Snap Dots: {(_showSnapDots ? "ON" : "OFF")}"); }

        if (Input.IsPressed(Keys.C) && _selectedChar >= 0 && !Input.IsHeld(Keys.LeftControl))
        {
            CycleSelectedCharacter();
        }

        if (Input.IsPressed(Keys.Insert))
        {
            if (_tab == 1) AddProp();
            else AddCharSlot();
        }

        // Nudge with repeat
        bool nudgeLeft = Input.IsHeld(Keys.Left);
        bool nudgeRight = Input.IsHeld(Keys.Right);
        bool nudgeUp = Input.IsHeld(Keys.Up);
        bool nudgeDown = Input.IsHeld(Keys.Down);
        bool anyNudge = nudgeLeft || nudgeRight || nudgeUp || nudgeDown;
        bool nudgePressed = Input.IsPressed(Keys.Left) || Input.IsPressed(Keys.Right) || Input.IsPressed(Keys.Up) || Input.IsPressed(Keys.Down);

        if (nudgePressed)
        {
            _nudgeRepeatTimer = NUDGE_REPEAT_DELAY;
            ApplyNudge(nudgeLeft, nudgeRight, nudgeUp, nudgeDown);
        }
        else if (anyNudge && _nudgeRepeatTimer > 0)
        {
            _nudgeRepeatTimer -= dt;
        }
        else if (anyNudge && _nudgeRepeatTimer <= 0)
        {
            _nudgeRepeatTimer = NUDGE_REPEAT_RATE;
            ApplyNudge(nudgeLeft, nudgeRight, nudgeUp, nudgeDown);
        }
        else if (!anyNudge)
        {
            _nudgeRepeatTimer = 0;
        }

        if (Input.IsPressed(Keys.D1)) { _nudgeStep = 1f; EditorUI.ShowToast("Nudge step: 1px"); }
        if (Input.IsPressed(Keys.D2)) { _nudgeStep = 5f; EditorUI.ShowToast("Nudge step: 5px"); }
        if (Input.IsPressed(Keys.D3)) { _nudgeStep = 10f; EditorUI.ShowToast("Nudge step: 10px"); }
        if (Input.IsPressed(Keys.D4)) { _nudgeStep = 50f; EditorUI.ShowToast("Nudge step: 50px"); }
        if (Input.IsPressed(Keys.D5)) { _nudgeStep = 100f; EditorUI.ShowToast("Nudge step: 100px"); }

        if (_selectedChar >= 0 && _charSprites.TryGetValue(_selectedChar, out var animSprite))
        {
            if (Input.IsPressed(Keys.I)) PlayCharacterAnimation(animSprite, "idle");
            if (Input.IsPressed(Keys.L)) PlayCharacterAnimation(animSprite, "singLEFT");
            if (Input.IsPressed(Keys.D) && !Input.IsHeld(Keys.LeftControl)) PlayCharacterAnimation(animSprite, "singDOWN");
            if (Input.IsPressed(Keys.U)) PlayCharacterAnimation(animSprite, "singUP");
            if (Input.IsPressed(Keys.R) && !Input.IsHeld(Keys.LeftControl)) PlayCharacterAnimation(animSprite, "singRIGHT");
            if (Input.IsPressed(Keys.S) && !Input.IsHeld(Keys.LeftControl)) PlayCharacterAnimation(animSprite, "shoot1");
        }

        float panSpeed = 400f / _zoom * dt;
        bool canPan = _selectedChar < 0; // Pan only when no character selected (avoids conflict with anim keys)
        if (canPan)
        {
            if (Input.IsHeld(Keys.A)) _camWorldX -= panSpeed;
            if (Input.IsHeld(Keys.D)) _camWorldX += panSpeed;
            if (Input.IsHeld(Keys.W)) _camWorldY -= panSpeed;
            if (Input.IsHeld(Keys.S)) _camWorldY += panSpeed;
        }
        if (Input.IsPressed(Keys.R) && !Input.IsHeld(Keys.LeftControl) && _selectedChar < 0) { MatchGameCamera(); }

        // Zoom to fit all elements
        if (Input.IsPressed(Keys.Home))
        {
            ZoomToFit();
        }
        // Zoom to selected
        if (Input.IsPressed(Keys.End) && (_selectedProp >= 0 || _selectedChar >= 0))
        {
            ZoomToSelected();
        }

        // Toggle rulers / axis / world coords
        if (Input.IsPressed(Keys.X) && !Input.IsHeld(Keys.LeftControl)) { _showRulers = !_showRulers; EditorUI.ShowToast($"Rulers: {(_showRulers ? "ON" : "OFF")}"); }
        if (Input.IsPressed(Keys.Y) && !Input.IsHeld(Keys.LeftControl)) { _showAxis = !_showAxis; EditorUI.ShowToast($"Axis: {(_showAxis ? "ON" : "OFF")}"); }
        if (Input.IsPressed(Keys.N) && !Input.IsHeld(Keys.LeftControl)) { _showWorldCoords = !_showWorldCoords; EditorUI.ShowToast($"World Coords: {(_showWorldCoords ? "ON" : "OFF")}"); }

        // Cycle snap preset
        if (Input.IsPressed(Keys.G) && !Input.IsHeld(Keys.LeftControl))
        {
            _snapPresetIndex = (_snapPresetIndex + 1) % _snapPresets.Length;
            _snapSize = _snapPresets[_snapPresetIndex];
            _snapToGrid = true;
            EditorUI.ShowToast($"Grid Snap: {_snapSize:F0}px");
        }
        if (Input.IsPressed(Keys.G) && Input.IsHeld(Keys.LeftShift) && !Input.IsHeld(Keys.LeftControl))
        {
            _snapPresetIndex = (_snapPresetIndex - 1 + _snapPresets.Length) % _snapPresets.Length;
            _snapSize = _snapPresets[_snapPresetIndex];
            _snapToGrid = true;
            EditorUI.ShowToast($"Grid Snap: {_snapSize:F0}px");
        }
        if (Input.IsPressed(Keys.G) && Input.IsHeld(Keys.LeftControl))
        {
            _snapToGrid = !_snapToGrid;
            EditorUI.ShowToast($"Grid Snap: {(_snapToGrid ? "ON" : "OFF")}");
        }

        // Context menu
        if (EditorUI.RightClicked && EditorUI.IsHovered(vpRect))
        {
            _showContextMenu = true;
            _contextMenuPos = EditorUI.MousePos;
            _contextMenuSelection = -1;
        }
        if (_showContextMenu && EditorUI.MouseClicked)
        {
            // Only dismiss if clicking outside the menu area (use CLAMPED position to match DrawContextMenu)
            var items = new System.Collections.Generic.List<string>();
            if (_selectedProp >= 0) { items.Add("Copy Position"); items.Add("Remove Prop"); }
            else if (_selectedChar >= 0) { items.Add("Copy Position"); items.Add("Remove Char"); items.Add("Cycle Character"); }
            items.Add("Zoom to Fit");
            items.Add("Reset Camera");
            int mw = 140, mh = 6 + items.Count * 22;
            int mx = (int)_contextMenuPos.X;
            int my = (int)_contextMenuPos.Y;
            if (mx + mw > FNFGame.SCREEN_WIDTH) mx = FNFGame.SCREEN_WIDTH - mw - 4;
            if (my + mh > FNFGame.SCREEN_HEIGHT - 22) my = FNFGame.SCREEN_HEIGHT - mh - 26;
            var menuRect = new Rectangle(mx, my, mw, mh);
            if (!EditorUI.IsHovered(menuRect))
                _showContextMenu = false;
        }

        // Track mouse world position
        _mouseWorldPos = ScreenToWorld(EditorUI.MousePos.X, EditorUI.MousePos.Y);

        if (Input.BackPressed)
        {
            if (_dirty) SaveStage();
            Game.Scenes.ChangeScene(new EditorHubScene());
        }
    }

    private void ApplyNudge(bool left, bool right, bool up, bool down)
    {
        float step = _nudgeStep;
        if (Input.IsHeld(Keys.LeftShift) || Input.IsHeld(Keys.RightShift)) step = 1f;
        if (Input.IsHeld(Keys.LeftControl) || Input.IsHeld(Keys.RightControl)) step *= 5f;

        float ndx = 0f, ndy = 0f;
        if (left) ndx -= step;
        if (right) ndx += step;
        if (up) ndy -= step;
        if (down) ndy += step;

        if (_selectedProp >= 0 && !_props[_selectedProp].Locked)
        {
            _props[_selectedProp].X += ndx;
            _props[_selectedProp].Y += ndy;
            if (_snapToGrid)
            {
                _props[_selectedProp].X = (float)Math.Round(_props[_selectedProp].X / _snapSize) * _snapSize;
                _props[_selectedProp].Y = (float)Math.Round(_props[_selectedProp].Y / _snapSize) * _snapSize;
            }
            _dirty = true;
            EditorUI.ShowToast($"Nudge: ({_props[_selectedProp].X:F1}, {_props[_selectedProp].Y:F1})");
        }
        else if (_selectedChar >= 0 && !_chars[_selectedChar].Locked)
        {
            _chars[_selectedChar].X += ndx;
            _chars[_selectedChar].Y += ndy;
            if (_snapToGrid)
            {
                _chars[_selectedChar].X = (float)Math.Round(_chars[_selectedChar].X / _snapSize) * _snapSize;
                _chars[_selectedChar].Y = (float)Math.Round(_chars[_selectedChar].Y / _snapSize) * _snapSize;
            }
            _dirty = true;
            EditorUI.ShowToast($"Nudge: ({_chars[_selectedChar].X:F1}, {_chars[_selectedChar].Y:F1})");
        }
    }

    private void DuplicateProp()
    {
        if (_selectedProp < 0 || _selectedProp >= _props.Count) return;
        var src = _props[_selectedProp];
        int idx = _props.Count;
        var dup = new PropInfo
        {
            Name = src.Name + "_copy",
            AssetPath = src.AssetPath,
            X = src.X + 20,
            Y = src.Y + 20,
            ScaleX = src.ScaleX,
            ScaleY = src.ScaleY,
            ZIndex = src.ZIndex,
            ScrollX = src.ScrollX,
            ScrollY = src.ScrollY,
            HasAnims = src.HasAnims,
            Locked = src.Locked
        };
        _props.Add(dup);
        var propsArr = _stageData["props"] as JArray ?? new JArray();
        if (_stageData["props"] == null) _stageData["props"] = propsArr;
        var dupJObj = new JObject
        {
            ["name"] = dup.Name,
            ["assetPath"] = dup.AssetPath,
            ["position"] = new JArray(dup.X, dup.Y),
            ["scale"] = new JArray(dup.ScaleX, dup.ScaleY),
            ["zIndex"] = dup.ZIndex,
            ["scroll"] = new JArray(dup.ScrollX, dup.ScrollY)
        };
        if (dup.HasAnims) dupJObj["animations"] = new JArray();
        propsArr.Add(dupJObj);
        _selectedProp = idx;
        _selectedChar = -1;
        _tab = 1;
        _dirty = true;
        LoadStageTextures();
        EditorUI.ShowToast($"Duplicated prop: {dup.Name}");
    }

    private void DuplicateChar()
    {
        if (_selectedChar < 0 || _selectedChar >= _chars.Count) return;
        var src = _chars[_selectedChar];
        int idx = _chars.Count;
        var dup = new CharInfo
        {
            Name = src.Name + "_copy",
            CharacterName = src.CharacterName,
            X = src.X + 20,
            Y = src.Y + 20,
            CamX = src.CamX,
            CamY = src.CamY,
            ZIndex = src.ZIndex,
            Scale = src.Scale,
            OffsetX = src.OffsetX,
            OffsetY = src.OffsetY,
            CharCamX = src.CharCamX,
            CharCamY = src.CharCamY,
            Locked = src.Locked
        };
        _chars.Add(dup);
        var charsObj = _stageData["characters"] as JObject;
        if (charsObj != null)
        {
            var charJObj = new JObject
            {
                ["position"] = new JArray(dup.X, dup.Y),
                ["cameraOffsets"] = new JArray(dup.CamX, dup.CamY),
                ["zIndex"] = dup.ZIndex
            };
            if (Math.Abs(dup.Scale - 1f) > 0.001f) charJObj["scale"] = Math.Round(dup.Scale, 2);
            charsObj[dup.Name] = charJObj;
        }
        _selectedChar = idx;
        _selectedProp = -1;
        _tab = 2;
        _dirty = true;
        LoadCharacterSprites();
        EditorUI.ShowToast($"Duplicated char: {dup.Name}");
    }

    private void TrySelect()
    {
        var mp = EditorUI.MousePos;
        _selectedProp = -1; _selectedChar = -1;

        // Props in reverse order (top-most first)
        for (int i = _props.Count - 1; i >= 0; i--)
        {
            var p = _props[i];
            if (p.Locked) continue;
            var screenPos = WorldToScreen(p.X, p.Y, p.ScrollX, p.ScrollY);
            int pw, ph;
            if (_texturedMode && _propTextures.TryGetValue(i, out var tex))
            { pw = Math.Max(20, (int)(tex.Width * p.ScaleX * _zoom)); ph = Math.Max(20, (int)(tex.Height * p.ScaleY * _zoom)); }
            else if (_texturedMode && _propSprites.TryGetValue(i, out var spr))
            {
                var f = spr.GetCurrentFrame();
                int fw = f != null ? (f.Rotated ? f.SourceRect.Height : f.SourceRect.Width) : 200;
                int fh = f != null ? (f.Rotated ? f.SourceRect.Width : f.SourceRect.Height) : 150;
                pw = Math.Max(20, (int)(fw * p.ScaleX * _zoom)); ph = Math.Max(20, (int)(fh * p.ScaleY * _zoom));
            }
            else
            { pw = Math.Max(20, (int)(200 * p.ScaleX * _zoom)); ph = Math.Max(20, (int)(150 * p.ScaleY * _zoom)); }
            var r = new Rectangle((int)screenPos.X, (int)screenPos.Y, pw, ph);
            if (r.Contains((int)mp.X, (int)mp.Y))
            { _selectedProp = i; _selectedChar = -1; _tab = 1; return; }
        }

        // Characters in reverse order
        for (int i = _chars.Count - 1; i >= 0; i--)
        {
            var ch = _chars[i];
            if (ch.Locked) continue;
            var screenPos = WorldToScreen(ch.X, ch.Y, 1, 1);
            int cw, chh;
            if (_texturedMode && _charSprites.TryGetValue(i, out var cs))
            {
                float scale = cs.Scale.X;
                int fw = cs.GetFrameWidth();
                int fh = cs.GetFrameHeight();
                cw = Math.Max(20, (int)(fw * scale * _zoom)); chh = Math.Max(20, (int)(fh * scale * _zoom));
            }
            else
            { cw = Math.Max(20, (int)(80 * _zoom)); chh = Math.Max(20, (int)(120 * _zoom)); }
            float offX = ch.OffsetX * _zoom;
            float offY = ch.OffsetY * _zoom;
            var r = new Rectangle((int)(screenPos.X - cw / 2 + offX), (int)(screenPos.Y - chh + offY), cw, chh);
            if (r.Contains((int)mp.X, (int)mp.Y))
            { _selectedChar = i; _selectedProp = -1; _tab = 2; return; }
        }
    }

    private void AddProp()
    {
        int idx = _props.Count;
        string name = $"prop{idx}";
        _props.Add(new PropInfo
        {
            Name = name,
            AssetPath = "",
            X = 0, Y = 0,
            ScaleX = 1, ScaleY = 1,
            ZIndex = idx * 10
        });
        var propsArr = _stageData["props"] as JArray ?? new JArray();
        if (_stageData["props"] == null) _stageData["props"] = propsArr;
        propsArr.Add(new JObject
        {
            ["name"] = name,
            ["assetPath"] = "",
            ["position"] = new JArray(0, 0),
            ["scale"] = new JArray(1, 1),
            ["zIndex"] = idx * 10
        });
        _selectedProp = idx;
        _selectedChar = -1;
        _tab = 1;
        _dirty = true;
        EditorUI.ShowToast($"Added prop: {name}");
    }

    private void AddCharSlot()
    {
        int idx = _chars.Count;
        string slotName = $"char{idx}";
        _chars.Add(new CharInfo
        {
            Name = slotName,
            CharacterName = "bf",
            X = 0, Y = 0,
            CamX = 0, CamY = 0,
            ZIndex = 100 + idx * 10
        });
        var charsObj = _stageData["characters"] as JObject;
        if (charsObj != null)
        {
            charsObj[slotName] = new JObject
            {
                ["position"] = new JArray(0, 0),
                ["cameraOffsets"] = new JArray(0, 0),
                ["zIndex"] = 100 + idx * 10
            };
        }
        _selectedChar = idx;
        _selectedProp = -1;
        _tab = 2;
        _dirty = true;
        LoadCharacterSprites();
        EditorUI.ShowToast($"Added character slot: {slotName}");
    }

    private void RemoveSelectedProp()
    {
        if (_selectedProp < 0 || _selectedProp >= _props.Count) return;
        _props.RemoveAt(_selectedProp);
        var propsArr = _stageData["props"] as JArray;
        if (propsArr != null && _selectedProp < propsArr.Count)
            propsArr.RemoveAt(_selectedProp);
        _propTextures.Remove(_selectedProp);
        _propSheets.Remove(_selectedProp);
        _propSprites.Remove(_selectedProp);
        var rebuiltTex = _propTextures.OrderBy(kvp => kvp.Key).ToList();
        _propTextures.Clear();
        foreach (var kvp in rebuiltTex) _propTextures[kvp.Key > _selectedProp ? kvp.Key - 1 : kvp.Key] = kvp.Value;
        var rebuiltSheet = _propSheets.OrderBy(kvp => kvp.Key).ToList();
        _propSheets.Clear();
        foreach (var kvp in rebuiltSheet) _propSheets[kvp.Key > _selectedProp ? kvp.Key - 1 : kvp.Key] = kvp.Value;
        var rebuiltSpr = _propSprites.OrderBy(kvp => kvp.Key).ToList();
        _propSprites.Clear();
        foreach (var kvp in rebuiltSpr) _propSprites[kvp.Key > _selectedProp ? kvp.Key - 1 : kvp.Key] = kvp.Value;
        _selectedProp = -1;
        _dirty = true;
        EditorUI.ShowToast("Prop removed");
    }

    private void RemoveSelectedChar()
    {
        if (_selectedChar < 0 || _selectedChar >= _chars.Count) return;
        string slotName = _chars[_selectedChar].Name;
        _chars.RemoveAt(_selectedChar);
        var charsObj = _stageData["characters"] as JObject;
        charsObj?.Remove(slotName);
        _charSprites.Remove(_selectedChar);
        var rebuilt = _charSprites.OrderBy(kvp => kvp.Key).ToList();
        _charSprites.Clear();
        foreach (var kvp in rebuilt)
        {
            int newIdx = kvp.Key > _selectedChar ? kvp.Key - 1 : kvp.Key;
            _charSprites[newIdx] = kvp.Value;
        }
        _selectedChar = -1;
        _dirty = true;
        EditorUI.ShowToast($"Removed character: {slotName}");
    }

    public override void Draw(SpriteBatch sb)
    {
        var px = Assets.Pixel;
        var font = Assets.GetFont(13);
        var fontSm = Assets.GetFont(11);
        if (font == null) return;
        int W = FNFGame.SCREEN_WIDTH, H = FNFGame.SCREEN_HEIGHT;

        sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend);
        EditorUI.FillRect(sb, px, new Rectangle(0, 0, W, H), EditorUI.BgDark);

        // Toolbar
        EditorUI.DrawToolbar(sb, px, new Rectangle(0, 0, W, TH));
        int tx = LW + 8;
        if (EditorUI.ToolButton(sb, px, font, tx, 5, 50, "Save", false, "Ctrl+S")) { SaveStage(); }
        tx += 54;
        if (EditorUI.ToolButton(sb, px, font, tx, 5, 50, "Undo") && _undoStack.Count > 0)
        {
            var u = _undoStack.Pop();
            if (u.Type == "prop" && u.Index < _props.Count) { _props[u.Index].X = u.OldX; _props[u.Index].Y = u.OldY; }
            else if (u.Type == "char" && u.Index < _chars.Count) { _chars[u.Index].X = u.OldX; _chars[u.Index].Y = u.OldY; _chars[u.Index].CamX = u.OldCamX; _chars[u.Index].CamY = u.OldCamY; }
            _dirty = true;
        }
        tx += 58;
        if (EditorUI.ToolButton(sb, px, font, tx, 5, 70, _texturedMode ? "Textured" : "Wireframe", _texturedMode, "V"))
        {
            _texturedMode = !_texturedMode;
            EditorUI.ShowToast(_texturedMode ? "Textured View" : "Wireframe View");
        }
        tx += 74;
        sb.Draw(px, new Rectangle(tx, 8, 1, 18), EditorUI.Border); tx += 8;

        if (_songVariants.Count > 0)
        {
            string varLabel = _songVariants[_songVariantIndex].Name;
            int varW = Math.Min(140, Math.Max(80, (int)font.MeasureString(varLabel).X + 16));
            if (EditorUI.ToolButton(sb, px, font, tx, 5, varW, varLabel, false, "Tab")) { CycleSongVariant(); }
            tx += varW + 4;
            sb.Draw(px, new Rectangle(tx, 8, 1, 18), EditorUI.Border); tx += 8;
        }

        if (_selectedChar >= 0 && _selectedChar < _chars.Count)
        {
            if (EditorUI.ToolButton(sb, px, font, tx, 5, 70, "Cycle Char", false, "C")) { CycleSelectedCharacter(); }
            tx += 74;
            if (EditorUI.ToolButton(sb, px, font, tx, 5, 50, "Add", false, "Ins")) { AddCharSlot(); }
            tx += 54;
            if (EditorUI.ToolButton(sb, px, font, tx, 5, 60, "Remove", false, "Del")) { RemoveSelectedChar(); }
            tx += 64;
            sb.Draw(px, new Rectangle(tx, 8, 1, 18), EditorUI.Border); tx += 8;
        }
        else if (_selectedProp >= 0 && _selectedProp < _props.Count)
        {
            if (EditorUI.ToolButton(sb, px, font, tx, 5, 50, "Add", false, "Ins")) { AddProp(); }
            tx += 54;
            if (EditorUI.ToolButton(sb, px, font, tx, 5, 60, "Remove", false, "Del")) { RemoveSelectedProp(); }
            tx += 64;
            sb.Draw(px, new Rectangle(tx, 8, 1, 18), EditorUI.Border); tx += 8;
        }

        string info = $"Stage: {_currentStage}  Props: {_props.Count}  Chars: {_chars.Count}";
        font.DrawText(sb, info, new Vector2(tx, 10), EditorUI.TextPrimary);
        if (_dirty)
        {
            float dw = font.MeasureString(info).X;
            font.DrawText(sb, " [MODIFIED]", new Vector2(tx + dw, 10), EditorUI.Warning);
        }

        // Left panel
        var lp = new Rectangle(0, TH, LW, H - TH - SH);
        EditorUI.DrawPanel(sb, px, lp, "Hierarchy", font);
        string[] tabs = { "Stages", "Props", "Chars" };
        _tab = EditorUI.TabBar(sb, px, font, 0, TH + 26, LW, tabs, _tab);
        int listY = TH + 56, listH = H - TH - SH - 56, rowH = 22;

        // Search/filter for Props/Chars
        if (_tab != 0)
        {
            EditorUI.FillRect(sb, px, new Rectangle(4, listY - 22, LW - 12, 18), new Color(35, 35, 50));
            EditorUI.DrawBorder(sb, px, new Rectangle(4, listY - 22, LW - 12, 18), EditorUI.Border);
            string searchLabel = string.IsNullOrEmpty(_searchQuery) ? "Type to filter..." : _searchQuery;
            Color searchCol = string.IsNullOrEmpty(_searchQuery) ? EditorUI.TextDim : EditorUI.TextPrimary;
            fontSm.DrawText(sb, searchLabel, new Vector2(8, listY - 20), searchCol);
            // Simple search input with number keys as first-letter filters
            if (!_renaming)
            {
                for (int k = 0; k < 26; k++)
                {
                    Keys key = (Keys)((int)Keys.A + k);
                    if (Input.IsPressed(key))
                    {
                        char c = (char)('a' + k);
                        _searchQuery += c;
                    }
                }
                if (Input.IsPressed(Keys.Back) && _searchQuery.Length > 0)
                    _searchQuery = _searchQuery.Substring(0, _searchQuery.Length - 1);
                if (Input.IsPressed(Keys.Escape))
                    _searchQuery = "";
            }
        }

        if (_tab == 0)
        {
            int contentH = _stageNames.Count * rowH;
            _listScroll = EditorUI.DrawScrollbar(sb, px, LW - 9, listY, listH, contentH, _listScroll, listH);
            for (int i = 0; i < _stageNames.Count; i++)
            {
                int ry = listY + i * rowH - _listScroll;
                if (ry + rowH < listY || ry > listY + listH) continue;
                if (EditorUI.ListItem(sb, px, fontSm, new Rectangle(0, ry, LW - 10, rowH), _stageNames[i], i == _stageIndex))
                { if (_dirty) SaveStage(); LoadStage(i); }
            }
        }
        else if (_tab == 1)
        {
            var filteredProps = _props.Select((p, i) => (p, i)).Where(x => string.IsNullOrEmpty(_searchQuery) || x.p.Name.ToLower().Contains(_searchQuery.ToLower())).ToList();
            int contentH = filteredProps.Count * rowH;
            _listScroll = EditorUI.DrawScrollbar(sb, px, LW - 9, listY, listH, contentH, _listScroll, listH);
            for (int j = 0; j < filteredProps.Count; j++)
            {
                var (p, i) = filteredProps[j];
                int ry = listY + j * rowH - _listScroll;
                if (ry + rowH < listY || ry > listY + listH) continue;
                string label = p.Name + (p.Locked ? " [L]" : "");
                if (EditorUI.ListItem(sb, px, fontSm, new Rectangle(0, ry, LW - 10, rowH), label, i == _selectedProp,
                    badge: $"z{p.ZIndex}", badgeColor: p.Locked ? EditorUI.Warning : EditorUI.TextDim))
                { if (!p.Locked) { _selectedProp = i; _selectedChar = -1; } }
            }
        }
        else
        {
            var filteredChars = _chars.Select((c, i) => (c, i)).Where(x => string.IsNullOrEmpty(_searchQuery) || x.c.Name.ToLower().Contains(_searchQuery.ToLower()) || (x.c.CharacterName?.ToLower().Contains(_searchQuery.ToLower()) ?? false)).ToList();
            int contentH = filteredChars.Count * rowH;
            _listScroll = EditorUI.DrawScrollbar(sb, px, LW - 9, listY, listH, contentH, _listScroll, listH);
            for (int j = 0; j < filteredChars.Count; j++)
            {
                var (c, i) = filteredChars[j];
                int ry = listY + j * rowH - _listScroll;
                if (ry + rowH < listY || ry > listY + listH) continue;
                string charLabel = c.Name + (c.Locked ? " [L]" : "");
                if (!string.IsNullOrEmpty(c.CharacterName) && c.CharacterName != c.Name)
                    charLabel += $" ({c.CharacterName})";
                if (EditorUI.ListItem(sb, px, fontSm, new Rectangle(0, ry, LW - 10, rowH), charLabel, i == _selectedChar,
                    badge: $"z{c.ZIndex}", badgeColor: c.Locked ? EditorUI.Warning : EditorUI.TextDim))
                { if (!c.Locked) { _selectedChar = i; _selectedProp = -1; } }
            }
        }

        // Center viewport
        DrawViewport(sb, px, font, fontSm, W, H);

        // Rename overlay
        if (_renaming)
        {
            int ry = TH + 4;
            int rx = (W - 300) / 2;
            EditorUI.FillRect(sb, px, new Rectangle(rx, ry, 300, 28), new Color(40, 40, 55, 240));
            EditorUI.DrawBorder(sb, px, new Rectangle(rx, ry, 300, 28), EditorUI.Accent);
            font.DrawText(sb, $"Rename: {_renameBuffer}_", new Vector2(rx + 8, ry + 6), EditorUI.TextPrimary);
        }

        // Right panel
        DrawProperties(sb, px, font, fontSm, W, H);

        // Status bar
        string saveHint = _dirty ? "UNSAVED -- Ctrl+S to save" : "Saved";
        string selName = _selectedProp >= 0 ? _props[_selectedProp].Name : (_selectedChar >= 0 ? _chars[_selectedChar].Name : "None");
        string worldInfo = $"Mouse: ({_mouseWorldPos.X:F0},{_mouseWorldPos.Y:F0})  Sel: {selName}";
        EditorUI.DrawStatusBar(sb, px, fontSm, W, H,
            saveHint, $"Zoom: {_zoom:F2}x  Grid: {_snapSize:F0}px  {worldInfo}",
            "Home=fit  End=sel  X=rulers  Y=axis  N=coords  G=snap(Ctrl+G=off)  K=lock  F2=rename  F3=labels  F1=info  F4=dots  Arrows=nudge  WASD=pan  V=view  Ctrl+Z=undo");
        EditorUI.DrawToast(sb, px, font, W, H);
        EditorUI.DrawTooltip(sb, px, fontSm);
        sb.End();
    }

    private void DrawViewport(SpriteBatch sb, Texture2D px, SpriteFontBase font, SpriteFontBase fontSm, int W, int H)
    {
        int rulerSize = 18;
        var area = new Rectangle(LW + rulerSize, TH + rulerSize, W - LW - RW - rulerSize, H - TH - SH - rulerSize);
        
        // Viewport background with professional border
        EditorUI.FillRect(sb, px, area, new Color(18, 18, 28));
        EditorUI.DrawBorder(sb, px, area, new Color(45, 45, 60), 2);
        
        // Clip all viewport drawing to the viewport area
        var oldScissor = sb.GraphicsDevice.ScissorRectangle;
        var oldRaster = sb.GraphicsDevice.RasterizerState;
        sb.End();
        sb.GraphicsDevice.ScissorRectangle = area;
        sb.GraphicsDevice.RasterizerState = new RasterizerState { ScissorTestEnable = true };
        sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend, rasterizerState: new RasterizerState { ScissorTestEnable = true });

        if (!_texturedMode)
        {
            float gridOffX = area.Width / 2f - _camWorldX * _zoom;
            float gridOffY = area.Height / 2f - _camWorldY * _zoom;
            EditorUI.DrawGrid(sb, px, area, new Vector2(gridOffX, gridOffY), _zoom, 100);
        }

        // Snap grid dots (only if spaced enough to be visible and performant)
        if (_showSnapDots && _snapToGrid && _zoom > 0.05f)
        {
            float step = _snapSize * _zoom;
            if (step >= 5f)
            {
                float offX = (_camWorldX % _snapSize) * _zoom;
                float offY = (_camWorldY % _snapSize) * _zoom;
                float startX = area.X + area.Width / 2f - offX;
                float startY = area.Y + area.Height / 2f - offY;
                // Count before drawing to avoid excessive iterations
                int maxDots = 2000;
                int drawn = 0;
                for (float gx = startX; gx < area.Right && drawn < maxDots; gx += step)
                    for (float gy = startY; gy < area.Bottom && drawn < maxDots; gy += step)
                    { sb.Draw(px, new Rectangle((int)gx, (int)gy, 1, 1), new Color(255, 255, 255, 30)); drawn++; }
                for (float gx = startX - step; gx > area.X && drawn < maxDots; gx -= step)
                    for (float gy = startY; gy < area.Bottom && drawn < maxDots; gy += step)
                    { sb.Draw(px, new Rectangle((int)gx, (int)gy, 1, 1), new Color(255, 255, 255, 30)); drawn++; }
                for (float gx = startX; gx < area.Right && drawn < maxDots; gx += step)
                    for (float gy = startY - step; gy > area.Y && drawn < maxDots; gy -= step)
                    { sb.Draw(px, new Rectangle((int)gx, (int)gy, 1, 1), new Color(255, 255, 255, 30)); drawn++; }
                for (float gx = startX - step; gx > area.X && drawn < maxDots; gx -= step)
                    for (float gy = startY - step; gy > area.Y && drawn < maxDots; gy -= step)
                    { sb.Draw(px, new Rectangle((int)gx, (int)gy, 1, 1), new Color(255, 255, 255, 30)); drawn++; }
            }
        }

        float vpCX = LW + rulerSize + area.Width / 2f;
        float vpCY = TH + rulerSize + area.Height / 2f;

        // Rulers
        if (_showRulers)
        {
            float rulerOffX = -_camWorldX + (area.Width / 2f) / _zoom;
            float rulerOffY = -_camWorldY + (area.Height / 2f) / _zoom;
            EditorUI.RulerH(sb, px, fontSm, LW + rulerSize, TH, area.Width, rulerOffX, _zoom);
            EditorUI.RulerV(sb, px, fontSm, LW, TH + rulerSize, area.Height, rulerOffY, _zoom);
            // Corner square
            EditorUI.FillRect(sb, px, new Rectangle(LW, TH, rulerSize, rulerSize), EditorUI.BgToolbar);
            EditorUI.DrawBorder(sb, px, new Rectangle(LW, TH, rulerSize, rulerSize), EditorUI.Border);
        }

        // Axis lines (world origin)
        if (_showAxis)
        {
            var originScreen = WorldToScreen(0, 0, 1, 1);
            if (originScreen.X >= area.X && originScreen.X <= area.Right)
                sb.Draw(px, new Rectangle((int)originScreen.X, area.Y, 1, area.Height), new Color(224, 108, 117, 120));
            if (originScreen.Y >= area.Y && originScreen.Y <= area.Bottom)
                sb.Draw(px, new Rectangle(area.X, (int)originScreen.Y, area.Width, 1), new Color(78, 201, 176, 120));
        }

        // Build combined draw list sorted by zIndex
        var drawItems = new List<(int ZIndex, string Kind, int Index)>();
        for (int i = 0; i < _props.Count; i++)
            drawItems.Add((_props[i].ZIndex, "prop", i));
        for (int i = 0; i < _chars.Count; i++)
            drawItems.Add((_chars[i].ZIndex, "char", i));
        drawItems.Sort((a, b) => a.ZIndex.CompareTo(b.ZIndex));

        Color[] charColors = { new(78,201,176), new(224,108,117), new(229,192,123) };

        foreach (var (zIdx, kind, idx) in drawItems)
        {
            if (kind == "prop")
                DrawProp(sb, px, fontSm, vpCX, vpCY, idx);
            else
                DrawChar(sb, px, fontSm, vpCX, vpCY, idx, charColors);
        }

        // Draw ABot if gf is a nene variant
        if (_showAbot && _abotSprite != null)
            DrawAbot(sb, px, fontSm, vpCX, vpCY);

        // Camera crosshair at viewport center
        EditorUI.DrawCrosshair(sb, px, vpCX, vpCY, 30, EditorUI.BorderLight);

        // Camera target crosshair
        float camScreenX = vpCX;
        float camScreenY = vpCY;
        EditorUI.DrawCrosshair(sb, px, camScreenX, camScreenY, 20, EditorUI.Gold);
        if (_zoom > 0.1f)
        {
            fontSm.DrawText(sb, "CAM", new Vector2(camScreenX + 4, camScreenY - 14), EditorUI.Gold);
            fontSm.DrawText(sb, $"({_camWorldX:F0},{_camWorldY:F0})", new Vector2(camScreenX + 4, camScreenY + 6), EditorUI.Gold);
        }

        // Info overlay
        if (_showInfoOverlay)
        {
            var infoLines = new List<string>
            {
                $"Stage: {_currentStage}",
                $"Props: {_props.Count}  Chars: {_chars.Count}",
                $"Camera: ({_camWorldX:F0}, {_camWorldY:F0})",
                $"Zoom: {_zoom:F2}x  Grid: {_snapSize:F0}px",
                $"Selected: {(_selectedProp >= 0 ? _props[_selectedProp].Name : (_selectedChar >= 0 ? _chars[_selectedChar].Name : "None"))}"
            };
            int ly = area.Y + area.Height - 4 - infoLines.Count * 14;
            for (int i = 0; i < infoLines.Count; i++)
            {
                float lw = fontSm.MeasureString(infoLines[i]).X;
                EditorUI.FillRect(sb, px, new Rectangle(area.X + 4, ly + i * 14, (int)lw + 6, 14), new Color(0, 0, 0, 120));
                fontSm.DrawText(sb, infoLines[i], new Vector2(area.X + 7, ly + i * 14 + 1), EditorUI.TextDim);
            }
        }

        // World mouse coordinate label
        if (_showWorldCoords && EditorUI.IsHovered(area))
        {
            int mx = (int)EditorUI.MousePos.X + 14;
            int my = (int)EditorUI.MousePos.Y + 14;
            EditorUI.WorldLabel(sb, px, fontSm, mx, my, $"{_mouseWorldPos.X:F1}, {_mouseWorldPos.Y:F1}");
        }

        // Context menu
        if (_showContextMenu)
        {
            DrawContextMenu(sb, px, fontSm);
        }

        string modeLabel = _texturedMode ? "TEXTURED (V)" : "WIREFRAME (V)";
        fontSm.DrawText(sb, modeLabel, new Vector2(area.X + 6, area.Y + 4), EditorUI.TextDim);
        
        // Restore scissor and restart normal batch
        sb.End();
        sb.GraphicsDevice.ScissorRectangle = oldScissor;
        sb.GraphicsDevice.RasterizerState = oldRaster ?? RasterizerState.CullCounterClockwise;
        sb.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend);
    }

    private void DrawContextMenu(SpriteBatch sb, Texture2D px, SpriteFontBase fontSm)
    {
        var items = new List<string>();
        if (_selectedProp >= 0) { items.Add("Copy Position"); items.Add("Remove Prop"); }
        else if (_selectedChar >= 0) { items.Add("Copy Position"); items.Add("Remove Char"); items.Add("Cycle Character"); }
        items.Add("Zoom to Fit");
        items.Add("Reset Camera");

        int mw = 140, mh = 6 + items.Count * 22;
        int mx = (int)_contextMenuPos.X;
        int my = (int)_contextMenuPos.Y;
        if (mx + mw > FNFGame.SCREEN_WIDTH) mx = FNFGame.SCREEN_WIDTH - mw - 4;
        if (my + mh > FNFGame.SCREEN_HEIGHT - 22) my = FNFGame.SCREEN_HEIGHT - mh - 26;

        EditorUI.FillRect(sb, px, new Rectangle(mx, my, mw, mh), new Color(40, 40, 55, 240));
        EditorUI.DrawBorder(sb, px, new Rectangle(mx, my, mw, mh), EditorUI.BorderLight);

        for (int i = 0; i < items.Count; i++)
        {
            int iy = my + 3 + i * 22;
            var r = new Rectangle(mx + 2, iy, mw - 4, 20);
            bool hovered = EditorUI.IsHovered(r);
            if (hovered) EditorUI.FillRect(sb, px, r, EditorUI.AccentDim);
            fontSm.DrawText(sb, items[i], new Vector2(mx + 8, iy + 3), hovered ? Color.White : EditorUI.TextSecondary);
            if (hovered && EditorUI.MouseClicked)
            {
                _showContextMenu = false;
                string cmd = items[i];
                if (cmd == "Copy Position" && _selectedProp >= 0) _clipboardPos = new Vector2(_props[_selectedProp].X, _props[_selectedProp].Y);
                else if (cmd == "Copy Position" && _selectedChar >= 0) _clipboardPos = new Vector2(_chars[_selectedChar].X, _chars[_selectedChar].Y);
                else if (cmd == "Remove Prop" && _selectedProp >= 0) RemoveSelectedProp();
                else if (cmd == "Remove Char" && _selectedChar >= 0) RemoveSelectedChar();
                else if (cmd == "Cycle Character" && _selectedChar >= 0) CycleSelectedCharacter();
                else if (cmd == "Zoom to Fit") ZoomToFit();
                else if (cmd == "Reset Camera") MatchGameCamera();
            }
        }
    }

    private void DrawProp(SpriteBatch sb, Texture2D px, SpriteFontBase fontSm, float vpCX, float vpCY, int i)
    {
        var p = _props[i];
        var screenPos = WorldToScreen(p.X, p.Y, p.ScrollX, p.ScrollY);
        bool sel = (i == _selectedProp);

        if (_texturedMode)
        {
            bool drewTexture = false;
            if (_propSprites.TryGetValue(i, out var animSprite))
            {
                var frame = animSprite.GetCurrentFrame();
                if (frame != null)
                {
                    int fw = frame.Rotated ? frame.SourceRect.Height : frame.SourceRect.Width;
                    int fh = frame.Rotated ? frame.SourceRect.Width : frame.SourceRect.Height;
                    int drawW = Math.Max(1, (int)(fw * p.ScaleX * _zoom));
                    int drawH = Math.Max(1, (int)(fh * p.ScaleY * _zoom));
                    var destRect = new Rectangle((int)screenPos.X, (int)screenPos.Y, drawW, drawH);
                    sb.Draw(animSprite.Sheet.Texture, destRect, frame.SourceRect, Color.White);
                    drewTexture = true;
                    if (sel) EditorUI.DrawSelectionBox(sb, px, destRect, EditorUI.Gold);
                }
            }
            else if (_propTextures.TryGetValue(i, out var tex))
            {
                int drawW = Math.Max(1, (int)(tex.Width * p.ScaleX * _zoom));
                int drawH = Math.Max(1, (int)(tex.Height * p.ScaleY * _zoom));
                var destRect = new Rectangle((int)screenPos.X, (int)screenPos.Y, drawW, drawH);
                sb.Draw(tex, destRect, Color.White);
                drewTexture = true;
                if (sel) EditorUI.DrawSelectionBox(sb, px, destRect, EditorUI.Gold);
            }
            if (!drewTexture)
                DrawPropWireframe(sb, px, fontSm, screenPos.X, screenPos.Y, p, sel);
        }
        else
        {
            DrawPropWireframe(sb, px, fontSm, screenPos.X, screenPos.Y, p, sel);
        }
    }

    private void DrawPropWireframe(SpriteBatch sb, Texture2D px, SpriteFontBase fontSm, float sx, float sy, PropInfo p, bool sel)
    {
        int pw = Math.Max(4, (int)(200 * p.ScaleX * _zoom));
        int ph = Math.Max(4, (int)(150 * p.ScaleY * _zoom));
        Color c = sel ? EditorUI.Accent : new Color(60, 60, 80, 120);
        if (p.Locked) c = new Color(120, 120, 120, 80);
        EditorUI.FillRect(sb, px, new Rectangle((int)sx, (int)sy, pw, ph), c);
        if (sel) EditorUI.DrawSelectionBox(sb, px, new Rectangle((int)sx, (int)sy, pw, ph), p.Locked ? EditorUI.Warning : EditorUI.Gold);
        else EditorUI.DrawBorder(sb, px, new Rectangle((int)sx, (int)sy, pw, ph), p.Locked ? EditorUI.Warning : EditorUI.Border);
        if (p.Locked && _zoom > 0.12f)
            fontSm.DrawText(sb, "[L]", new Vector2(sx + pw - 16, sy + 2), EditorUI.Warning);
        if (_zoom > 0.12f && (_showAllLabels || sel))
            fontSm.DrawText(sb, p.Name, new Vector2(sx + 3, sy + 3), EditorUI.TextPrimary);
    }



    private void DrawChar(SpriteBatch sb, Texture2D px, SpriteFontBase fontSm, float vpCX, float vpCY, int i, Color[] charColors)
    {
        var ch = _chars[i];
        var screenPos = WorldToScreen(ch.X, ch.Y, 1, 1);
        bool sel = (i == _selectedChar);
        Color col = charColors[i % charColors.Length];
        int chh = Math.Max(8, (int)(120 * _zoom));

        if (_texturedMode && _charSprites.TryGetValue(i, out var charSprite) && charSprite.Sheet != null)
        {
            string animName = charSprite.CurrentAnimation;
            float offX = ch.OffsetX * _zoom;
            float offY = ch.OffsetY * _zoom;

            // Runtime compositing (AnimateAtlas with M3D transforms - A-Bot, DJ, etc.)
            if (!string.IsNullOrEmpty(animName) &&
                charSprite.Sheet.RawCompositeData.TryGetValue(animName, out var rawFrames) &&
                rawFrames.Count > 0)
            {
                int idx = charSprite.FrameIndex % rawFrames.Count;
                var parts = rawFrames[idx];
                float charScale = charSprite.Scale.X;
                float effScale = charScale * _zoom;
                float baseX = screenPos.X + offX;
                float baseY = screenPos.Y + offY;
                foreach (var (partFrame, a, b, c_, d, tx, ty) in parts)
                {
                    charSprite.DrawTransformedPart(sb, charSprite.Sheet.Texture, partFrame,
                        a * effScale, b * effScale, c_ * effScale, d * effScale,
                        tx * effScale + baseX, ty * effScale + baseY);
                }
                int fw = charSprite.GetFrameWidth();
                int fh = charSprite.GetFrameHeight();
                int drawW = Math.Max(1, (int)(fw * effScale));
                chh = Math.Max(1, (int)(fh * effScale));
                if (sel)
                {
                    var destRect = new Rectangle((int)(baseX - drawW / 2), (int)(baseY - chh), drawW, chh);
                    EditorUI.DrawSelectionBox(sb, px, destRect, EditorUI.Gold);
                }
                sb.Draw(px, new Rectangle((int)baseX - 3, (int)baseY - 3, 6, 6), col);
            }
            else if (!string.IsNullOrEmpty(animName) &&
                charSprite.Sheet.CompositeAnimations.TryGetValue(animName, out var compFrames) &&
                compFrames.Count > 0)
            {
                int idx = charSprite.FrameIndex % compFrames.Count;
                var (tex, rect, compOrigin) = compFrames[idx];
                float scale = charSprite.Scale.X;
                float effScale = scale * _zoom;
                float charFeetX = screenPos.X + offX;
                float charFeetY = screenPos.Y + offY;
                Vector2 drawPos = new Vector2(charFeetX, charFeetY) - compOrigin * _zoom;
                drawPos.X -= rect.Width * 0.5f * (effScale - _zoom);
                drawPos.Y -= rect.Height * 0.5f * (effScale - _zoom);
                sb.Draw(tex, drawPos, rect, Color.White, 0f, Vector2.Zero,
                    new Vector2(effScale, effScale), charSprite.Effects, 0f);
                chh = (int)(rect.Height * effScale);
                if (sel)
                {
                    int selW = (int)(rect.Width * effScale);
                    int selH = (int)(rect.Height * effScale);
                    EditorUI.DrawSelectionBox(sb, px,
                        new Rectangle((int)drawPos.X, (int)drawPos.Y, selW, selH), EditorUI.Gold);
                }
                sb.Draw(px, new Rectangle((int)(screenPos.X + offX) - 3, (int)(screenPos.Y + offY) - 3, 6, 6), col);
            }
            else
            {
                var frame = charSprite.GetCurrentFrame();
                if (frame != null)
                {
                    float scale = charSprite.Scale.X;
                    int fw = charSprite.GetFrameWidth();
                    int fh = charSprite.GetFrameHeight();
                    int drawW = Math.Max(1, (int)(fw * scale * _zoom));
                    int drawH = Math.Max(1, (int)(fh * scale * _zoom));
                    chh = drawH;
                    var destRect = new Rectangle((int)(screenPos.X - drawW / 2 + offX), (int)(screenPos.Y - drawH + offY), drawW, drawH);
                    sb.Draw(charSprite.Sheet.Texture, destRect, frame.SourceRect, Color.White,
                        0f, Vector2.Zero, charSprite.Effects, 0);
                    if (sel) EditorUI.DrawSelectionBox(sb, px, destRect, EditorUI.Gold);
                    sb.Draw(px, new Rectangle((int)(screenPos.X + offX) - 3, (int)(screenPos.Y + offY) - 3, 6, 6), col);
                }
                else
                {
                    DrawCharWireframe(sb, px, fontSm, screenPos.X, screenPos.Y, ch, sel, col);
                }
            }
        }
        else
        {
            DrawCharWireframe(sb, px, fontSm, screenPos.X, screenPos.Y, ch, sel, col);
        }

        // Label
        if (_zoom > 0.08f && (_showAllLabels || sel))
        {
            string label = ch.Name;
            if (!string.IsNullOrEmpty(ch.CharacterName) && ch.CharacterName != ch.Name)
                label += $" ({ch.CharacterName})";
            var labelSize = fontSm.MeasureString(label);
            float lx = screenPos.X - labelSize.X / 2f + ch.OffsetX * _zoom;
            float ly = (screenPos.Y - chh + ch.OffsetY * _zoom) - labelSize.Y - 2;
            EditorUI.FillRect(sb, px, new Rectangle((int)(lx - 2), (int)ly, (int)labelSize.X + 4, (int)labelSize.Y + 2), new Color(0, 0, 0, 140));
            fontSm.DrawText(sb, label, new Vector2(lx, ly + 1), sel ? EditorUI.Gold : EditorUI.TextDim);
        }

        // Camera offset line
        float feetX = screenPos.X + ch.OffsetX * _zoom;
        float feetY = screenPos.Y + ch.OffsetY * _zoom;
        float camSX = feetX + ch.CamX * _zoom;
        float camSY = feetY + ch.CamY * _zoom;
        sb.Draw(px, new Rectangle((int)Math.Min(feetX, camSX), (int)feetY, Math.Max(2, (int)Math.Abs(camSX - feetX)), 1), col * 0.5f);
        sb.Draw(px, new Rectangle((int)camSX, (int)Math.Min(feetY, camSY), 1, Math.Max(2, (int)Math.Abs(camSY - feetY))), col * 0.5f);
        var camC = sel ? Color.White : col * 0.7f;
        sb.Draw(px, new Rectangle((int)camSX - 5, (int)camSY - 5, 10, 10), camC);
        sb.Draw(px, new Rectangle((int)camSX - 3, (int)camSY - 3, 6, 6), new Color(22, 22, 35));
        sb.Draw(px, new Rectangle((int)camSX - 1, (int)camSY - 1, 2, 2), camC);
    }

    private void DrawAbot(SpriteBatch sb, Texture2D px, SpriteFontBase fontSm, float vpCX, float vpCY)
    {
        var gfCh = _chars.FirstOrDefault(c => c.Name.Equals("gf", StringComparison.OrdinalIgnoreCase));
        if (gfCh == null || !_charSprites.TryGetValue(_chars.IndexOf(gfCh), out var gfSpr) || gfSpr.Sheet == null) return;

        float charScale = gfSpr.Scale.X;
        int gfw = gfSpr.GetFrameWidth();
        int gfh = gfSpr.GetFrameHeight();
        if (gfw <= 0) gfw = 300;
        if (gfh <= 0) gfh = 300;
        float originX = gfw * charScale / 2f;
        float originY = gfh * charScale;

        float cornerX = gfCh.X - originX + gfCh.OffsetX;
        float cornerY = gfCh.Y - originY + gfCh.OffsetY;

        float abotOffX = -95f + gfCh.OffsetX * charScale;
        float abotOffY = 384f + gfCh.OffsetY * charScale;
        if (gfCh.CharacterName != null && gfCh.CharacterName.Contains("christmas", StringComparison.OrdinalIgnoreCase))
        {
            abotOffX = -135f + gfCh.OffsetX * charScale;
            abotOffY = 210f + gfCh.OffsetY * charScale;
        }

        float abotBaseX = cornerX + abotOffX;
        float abotBaseY = cornerY + abotOffY;
        var abotScreen = WorldToScreen(abotBaseX, abotBaseY, 1, 1);

        if (_abotSprite == null || _abotSprite.Sheet == null) return;
        string abotAnim = _abotSprite.CurrentAnimation;

        // Runtime compositing for A-Bot
        if (!string.IsNullOrEmpty(abotAnim) &&
            _abotSprite.Sheet.RawCompositeData.TryGetValue(abotAnim, out var abotRaw) &&
            abotRaw.Count > 0)
        {
            int idx = _abotSprite.FrameIndex % abotRaw.Count;
            var parts = abotRaw[idx];
            float abotScale = _abotSprite.Scale.X;
            float effScale = abotScale * _zoom;
            float baseX = abotScreen.X;
            float baseY = abotScreen.Y;
            foreach (var (partFrame, a, b, c_, d, tx, ty) in parts)
            {
                _abotSprite.DrawTransformedPart(sb, _abotSprite.Sheet.Texture, partFrame,
                    a * effScale, b * effScale, c_ * effScale, d * effScale,
                    tx * effScale + baseX, ty * effScale + baseY);
            }
            if (_zoom > 0.1f)
            {
                int afw = _abotSprite.GetFrameWidth();
                int afh = _abotSprite.GetFrameHeight();
                int adw = Math.Max(1, (int)(afw * effScale));
                int adh = Math.Max(1, (int)(afh * effScale));
                EditorUI.DrawBorder(sb, px, new Rectangle((int)baseX, (int)baseY, adw, adh), new Color(255, 140, 0));
                fontSm.DrawText(sb, "ABOT", new Vector2(baseX + 4, baseY + 4), new Color(255, 140, 0));
            }
        }
        else if (!string.IsNullOrEmpty(abotAnim) &&
            _abotSprite.Sheet.CompositeAnimations.TryGetValue(abotAnim, out var abotComp) &&
            abotComp.Count > 0)
        {
            int idx = _abotSprite.FrameIndex % abotComp.Count;
            var (abotTex, abotRect, abotOrigin) = abotComp[idx];
            Vector2 abotDrawPos = abotScreen;
            sb.Draw(abotTex, abotDrawPos, abotRect, Color.White, 0f, Vector2.Zero, new Vector2(_zoom, _zoom), _abotSprite.Effects, 0f);
            if (_zoom > 0.1f)
            {
                EditorUI.DrawBorder(sb, px, new Rectangle((int)abotDrawPos.X, (int)abotDrawPos.Y,
                    (int)(abotRect.Width * _zoom), (int)(abotRect.Height * _zoom)), new Color(255, 140, 0));
                fontSm.DrawText(sb, "ABOT", new Vector2(abotDrawPos.X + 3, abotDrawPos.Y + 3), new Color(255, 140, 0));
            }
        }
        else
        {
            var aFrame = _abotSprite.GetCurrentFrame();
            if (aFrame != null && _abotSprite.Sheet != null)
            {
                int afw = _abotSprite.GetFrameWidth();
                int afh = _abotSprite.GetFrameHeight();
                int adw = Math.Max(1, (int)(afw * _zoom));
                int adh = Math.Max(1, (int)(afh * _zoom));
                var aRect = new Rectangle((int)abotScreen.X, (int)abotScreen.Y, adw, adh);
                sb.Draw(_abotSprite.Sheet.Texture, aRect, aFrame.SourceRect, Color.White);
                EditorUI.DrawBorder(sb, px, aRect, new Color(255, 140, 0));
                if (_zoom > 0.1f)
                    fontSm.DrawText(sb, "ABOT", new Vector2(abotScreen.X + 3, abotScreen.Y + 3), new Color(255, 140, 0));
            }
            else
            {
                int abw = Math.Max(10, (int)(100 * _zoom));
                int abh = Math.Max(10, (int)(80 * _zoom));
                EditorUI.FillRect(sb, px, new Rectangle((int)abotScreen.X, (int)abotScreen.Y, abw, abh), new Color(255, 140, 0, 60));
                EditorUI.DrawBorder(sb, px, new Rectangle((int)abotScreen.X, (int)abotScreen.Y, abw, abh), new Color(255, 140, 0));
            }
        }
    }

    private void DrawCharWireframe(SpriteBatch sb, Texture2D px, SpriteFontBase fontSm, float sx, float sy, CharInfo ch, bool sel, Color col)
    {
        int cw = Math.Max(8, (int)(80 * _zoom));
        int chh = Math.Max(8, (int)(120 * _zoom));
        float offX = ch.OffsetX * _zoom;
        float offY = ch.OffsetY * _zoom;
        var charRect = new Rectangle((int)(sx - cw / 2 + offX), (int)(sy - chh + offY), cw, chh);
        Color fillCol = ch.Locked ? new Color(120, 120, 120, 80) : (sel ? col : col * 0.4f) * 0.6f;
        Color borderCol = ch.Locked ? EditorUI.Warning : col;
        EditorUI.FillRect(sb, px, charRect, fillCol);
        if (sel) EditorUI.DrawSelectionBox(sb, px, charRect, ch.Locked ? EditorUI.Warning : EditorUI.Gold);
        else EditorUI.DrawBorder(sb, px, charRect, borderCol);
        sb.Draw(px, new Rectangle((int)(sx + offX) - 4, (int)(sy + offY) - 4, 8, 8), borderCol);
        if (_zoom > 0.1f)
        {
            string label = ch.Name.ToUpper() + (ch.Locked ? " [L]" : "");
            fontSm.DrawText(sb, label, new Vector2(charRect.X + 3, charRect.Y + 3), ch.Locked ? EditorUI.Warning : Color.White);
        }
    }

    private void DrawProperties(SpriteBatch sb, Texture2D px, SpriteFontBase font, SpriteFontBase fontSm, int W, int H)
    {
        int rx = W - RW;
        var pp = new Rectangle(rx, TH, RW, H - TH - SH);
        EditorUI.DrawPanel(sb, px, pp, "Properties", font);
        int y = pp.Y + 32;
        int pw = RW - 12;

        EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Stage", _currentStage); y += 18;
        EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Name", _stageData?["name"]?.ToString() ?? "?"); y += 18;
        float cz = _stageData != null ? (float)(_stageData["cameraZoom"] ?? 1.0) : 1.0f;
        EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "CamZoom", $"{cz:F2}"); y += 18;
        EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Dir", _stageData?["directory"]?.ToString() ?? "?"); y += 18;
        if (_songVariants.Count > 0)
        {
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Variant", _songVariants[_songVariantIndex].Name); y += 18;
        }
        sb.Draw(px, new Rectangle(rx + 6, y, pw - 8, 1), EditorUI.Border); y += 8;

        if (_selectedProp >= 0 && _selectedProp < _props.Count)
        {
            var p = _props[_selectedProp];
            EditorUI.PropertyHeader(sb, px, fontSm, rx + 4, y, pw, "Selected Prop"); y += 26;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Name", p.Name, true); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Asset", p.AssetPath); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Position", $"{p.X:F1}, {p.Y:F1}"); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Scale", $"{p.ScaleX:F2} x {p.ScaleY:F2}"); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Z-Index", $"{p.ZIndex}"); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Scroll", $"{p.ScrollX:F1}, {p.ScrollY:F1}"); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Locked", p.Locked ? "YES" : "no"); y += 18;
            y += 8;
            fontSm.DrawText(sb, "Drag to move | +/- scale", new Vector2(rx + 8, y), EditorUI.TextDim); y += 14;
            fontSm.DrawText(sb, "PgUp/Dn zIndex | Del remove", new Vector2(rx + 8, y), EditorUI.TextDim);
        }
        else if (_selectedChar >= 0 && _selectedChar < _chars.Count)
        {
            var ch = _chars[_selectedChar];
            string charName = ch.CharacterName ?? ch.Name;
            EditorUI.PropertyHeader(sb, px, fontSm, rx + 4, y, pw, "Selected Character"); y += 26;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Slot", ch.Name); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Character", charName); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Position", $"{ch.X:F1}, {ch.Y:F1}"); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "CamOffset", $"{ch.CamX:F1}, {ch.CamY:F1}"); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Z-Index", $"{ch.ZIndex}"); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Scale", $"{ch.Scale:F2}"); y += 18;
            EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Locked", ch.Locked ? "YES" : "no"); y += 18;
            if (_charSprites.TryGetValue(_selectedChar, out var spr) && spr.CurrentAnimation != null)
            { EditorUI.PropertyRow(sb, px, fontSm, rx + 4, y, pw, "Anim", spr.CurrentAnimation); y += 18; }
            y += 8;
            EditorUI.PropertyHeader(sb, px, fontSm, rx + 4, y, pw, "Controls"); y += 26;
            fontSm.DrawText(sb, "Arrows = nudge position", new Vector2(rx + 8, y), EditorUI.TextDim); y += 14;
            fontSm.DrawText(sb, $"Step: {_nudgeStep:F0}px (1-5 keys)", new Vector2(rx + 8, y), EditorUI.TextDim); y += 14;
            fontSm.DrawText(sb, "Shift = fine | Ctrl = x5", new Vector2(rx + 8, y), EditorUI.TextDim); y += 14;
            fontSm.DrawText(sb, "I=idle L=left D=down U=up R=right S=shoot", new Vector2(rx + 8, y), EditorUI.TextDim); y += 14;
            fontSm.DrawText(sb, "Tab = cycle song variant", new Vector2(rx + 8, y), EditorUI.TextDim); y += 14;
            fontSm.DrawText(sb, "A = cycle anim | C = cycle char", new Vector2(rx + 8, y), EditorUI.TextDim); y += 14;
            fontSm.DrawText(sb, "Drag = move | Shift+Drag = camOffset", new Vector2(rx + 8, y), EditorUI.TextDim); y += 14;
            fontSm.DrawText(sb, "PgUp/Dn = zIndex | Del = remove | Ins = add", new Vector2(rx + 8, y), EditorUI.TextDim); y += 14;
            fontSm.DrawText(sb, $"G = snap ({_snapSize:F0}px)  Ctrl+G = off  Home=fit  End=sel  WASD=pan", new Vector2(rx + 8, y), EditorUI.TextDim);
        }
        else
        {
            EditorUI.PropertyHeader(sb, px, fontSm, rx + 4, y, pw, "Instructions"); y += 26;
            fontSm.DrawText(sb, "Click a prop or character", new Vector2(rx + 8, y), EditorUI.TextDim); y += 14;
            fontSm.DrawText(sb, "in the viewport to select", new Vector2(rx + 8, y), EditorUI.TextDim); y += 14;
            fontSm.DrawText(sb, "and edit it.", new Vector2(rx + 8, y), EditorUI.TextDim);
        }
    }
}
