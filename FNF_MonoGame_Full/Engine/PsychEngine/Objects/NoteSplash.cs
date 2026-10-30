using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FNF_MonoGame.Engine.PsychEngine.Objects;

/// <summary>
/// Psych Engine note splash effect shown when hitting a note perfectly.
/// Original: objects/NoteSplash.hx
/// </summary>
public class NoteSplash
{
    public float X { get; set; }
    public float Y { get; set; }
    public bool Visible { get; set; } = true;
    public bool Active { get; set; } = true;
    public float Alpha { get; set; } = 1f;
    public int ZIndex { get; set; }

    public SpriteSheet Sheet { get; private set; }
    public AnimatedSprite AnimatedSprite { get; private set; }
    public string TextureName { get; private set; }

    public bool CopyX { get; set; } = true;
    public bool CopyY { get; set; } = true;
    public bool InEditor { get; set; }

    public int NoteData { get; set; }
    public object BabyArrow { get; set; }

    private static readonly Dictionary<string, NoteSplashConfig> _configs = new(StringComparer.OrdinalIgnoreCase);

    public NoteSplash(float x = 0, float y = 0, int noteData = 0, string texture = null)
    {
        X = x;
        Y = y;
        NoteData = noteData;
        LoadSplash(texture);
    }

    public void LoadSplash(string splash = null)
    {
        splash ??= DefaultNoteSplash + GetSplashSkinPostfix();
        if (ClientPrefs.Data.SplashSkin != null && ClientPrefs.Data.SplashSkin.Length > 0)
            splash = ClientPrefs.Data.SplashSkin;

        string path = PsychPaths.Image(splash);
        if (string.IsNullOrEmpty(path))
        {
            splash = DefaultNoteSplash + GetSplashSkinPostfix();
            path = PsychPaths.Image(splash);
            if (string.IsNullOrEmpty(path))
            {
                splash = DefaultNoteSplash;
                path = PsychPaths.Image(splash);
            }
        }

        TextureName = splash;
        if (!string.IsNullOrEmpty(path))
        {
            string basePath = Path.ChangeExtension(path, null);
            Sheet = SpriteSheet.Load(FNFGame.Instance, basePath);
            AnimatedSprite = Sheet != null ? new AnimatedSprite { Sheet = Sheet } : null;
        }

        LoadConfig(splash, path);
    }

    private void LoadConfig(string splash, string imagePath)
    {
        string configKey = "images/" + splash;
        if (_configs.TryGetValue(configKey, out var cached))
        {
            Config = cached;
            return;
        }

        string jsonPath = imagePath != null ? Path.ChangeExtension(imagePath, ".json") : null;
        if (!string.IsNullOrEmpty(jsonPath) && global::FNF_MonoGame.Engine.FsCI.FileExists(jsonPath))
        {
            try
            {
                var json = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(global::FNF_MonoGame.Engine.FsCI.ReadAllText(jsonPath));
                // TODO: parse full config (scale, allowRGB, allowPixel, rgb, animations)
            }
            catch { }
        }

        // Default splash config with per-note animations
        var config = new NoteSplashConfig { Scale = 1f, AllowRGB = true, AllowPixel = true };
        string[] cols = { "purple", "blue", "green", "red" };
        int maxAnims = 0;
        while (true)
        {
            bool found = true;
            foreach (var col in cols)
            {
                string animName = $"note splash {col} {maxAnims + 1}";
                if (!AnimationExists(animName))
                {
                    found = false;
                    break;
                }
            }
            if (!found) break;
            maxAnims++;
        }
        if (maxAnims == 0) maxAnims = 1;

        for (int animNum = 0; animNum < maxAnims; animNum++)
        {
            for (int i = 0; i < cols.Length; i++)
            {
                int data = i + (animNum * cols.Length);
                string name = animNum > 0 ? cols[i] + (animNum + 1) : cols[i];
                string prefix = $"note splash {cols[i]} {animNum + 1}";
                config.Animations[name] = new NoteSplashAnim
                {
                    Name = name,
                    NoteData = data,
                    Prefix = prefix,
                    Fps = new[] { 22, 26 },
                    Offsets = new[] { 0f, 0f }
                };
            }
        }

        Config = config;
        _configs[configKey] = config;
    }

    private bool AnimationExists(string name)
    {
        return Sheet?.Animations.ContainsKey(name) == true
            || Sheet?.CompositeAnimations.ContainsKey(name) == true
            || Sheet?.RawCompositeData.ContainsKey(name) == true;
    }

    public NoteSplashConfig Config { get; private set; }

    public void SpawnSplashNote(float x, float y, int noteData = 0, object note = null, bool randomize = true)
    {
        X = x;
        Y = y;
        NoteData = noteData;

        if (randomize && Config != null)
        {
            int variants = Config.Animations.Count / 4;
            if (variants > 1)
                NoteData = noteData % 4 + (new Random().Next(0, variants) * 4);
        }

        string anim = PlayDefaultAnim();
        if (!string.IsNullOrEmpty(anim) && Config?.Animations.TryGetValue(anim, out var animCfg) == true && animCfg != null)
        {
            AnimatedSprite?.PlayAnimation(animCfg.Prefix, true, false);
        }

        Alpha = ClientPrefs.Data.SplashAlpha;
        Visible = true;
        Active = true;

        if (AnimatedSprite != null)
        {
            AnimatedSprite.OnFinish = () => { Visible = false; Active = false; };
        }
    }

    public string PlayDefaultAnim()
    {
        string[] cols = { "purple", "blue", "green", "red" };
        int colIndex = NoteData % 4;
        int variant = NoteData / 4;
        string name = variant > 0 ? cols[colIndex] + (variant + 1) : cols[colIndex];

        if (Config?.Animations.ContainsKey(name) == true)
        {
            var anim = Config.Animations[name];
            AnimatedSprite?.PlayAnimation(anim.Prefix, true, false);
            return name;
        }
        return null;
    }

    public void Update(float elapsed)
    {
        if (!Active) return;

        AnimatedSprite?.Update(elapsed);

        if (BabyArrow != null)
        {
            // TODO: track strum note position if CopyX/CopyY
        }
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible || AnimatedSprite == null) return;
        AnimatedSprite.Position = new Vector2(X, Y);
        AnimatedSprite.Tint = Color.White * Alpha;
        AnimatedSprite.Draw(spriteBatch);
    }

    public static string DefaultNoteSplash => "noteSplashes/noteSplashes";

    public static string GetSplashSkinPostfix()
    {
        string skin = "";
        if (!string.IsNullOrEmpty(ClientPrefs.Data.SplashSkin) &&
            ClientPrefs.Data.SplashSkin != ClientPrefs.DefaultData.SplashSkin)
        {
            skin = "-" + ClientPrefs.Data.SplashSkin.Trim().ToLowerInvariant().Replace(' ', '-');
        }
        return skin;
    }
}

public class NoteSplashConfig
{
    public Dictionary<string, NoteSplashAnim> Animations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public float Scale { get; set; } = 1f;
    public bool AllowRGB { get; set; } = true;
    public bool AllowPixel { get; set; } = true;
    public List<int[]> Rgb { get; set; }
}

public class NoteSplashAnim
{
    public string Name { get; set; }
    public int NoteData { get; set; }
    public string Prefix { get; set; }
    public int[] Fps { get; set; }
    public float[] Offsets { get; set; }
    public int[] Indices { get; set; }
}
