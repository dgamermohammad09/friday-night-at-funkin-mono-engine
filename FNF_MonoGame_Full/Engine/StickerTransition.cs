using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json.Linq;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Port of funkin.ui.transition.stickers.StickerSubState (V-Slice 0.8.1): the sticker wipe used when leaving
/// Results (non personal-best / story mode) and Pause > Exit to Menu. Stickers live on their own camera above
/// everything (StickerTransitionSprite, addChildBelowMouse 9999), survive the state switch and are removed
/// ("degen") on top of the new state. Driven by SceneManager.Update/Draw.
/// </summary>
public static class StickerTransition
{
    private sealed class Sticker
    {
        public Texture2D Tex; public float X, Y, Angle, Scale = 1f, Timing; public bool Visible;
    }

    private static List<Sticker> _stickers = new();
    private static readonly List<(float at, Action a)> _timers = new();
    private static float _t;
    private static readonly Random Rnd = new();
    private static Action<string> _playSound;

    /// <summary>True while stickers are on screen (regen or degen).</summary>
    public static bool Active { get; private set; }
    /// <summary>StickerSubState.switchingState: last sticker placed, state switch pending (menus ignore input).</summary>
    public static bool SwitchingState { get; private set; }
    /// <summary>Dev: seconds since the last Start() (keeps running after the switch), -1 before any.</summary>
    public static double DevClock => _devStarted ? _devT : -1;
    private static bool _devStarted; private static double _devT;

    private static readonly string[] Sounds = Enumerable.Range(1, 9).Select(i => $"stickersounds/keys/keyClick{i}").ToArray();

    /// <summary>Sticker pack id for a song (meta playData.stickerPack) falling back to the player's pack
    /// (data/players/{bf,pico}.json stickerPack) and Constants.DEFAULT_STICKER_PACK ("default").</summary>
    public static string ResolvePackId(AssetManager assets, string songFolder, string playerCharacterId)
    {
        try
        {
            if (!string.IsNullOrEmpty(songFolder))
            {
                string p = assets.ResolvePath($"songs/{songFolder}/charts/meta.json");
                if (p != null && global::FNF_MonoGame.Engine.FsCI.FileExists(p) && (string)JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(p))["playData"]?["stickerPack"] is string sp) return sp;
            }
            foreach (var owner in new[] { "bf", "pico" })
            {
                string p = assets.ResolvePath($"data/players/{owner}.json");
                if (p == null || !global::FNF_MonoGame.Engine.FsCI.FileExists(p)) continue;
                var jo = JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(p));
                if (jo["ownedChars"] is JArray owned && owned.Any(o => (string)o == playerCharacterId)) return (string)jo["stickerPack"];
            }
            string bf = assets.ResolvePath("data/players/bf.json");
            if (bf != null && global::FNF_MonoGame.Engine.FsCI.FileExists(bf)) return (string)JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(bf))["stickerPack"];
        }
        catch { }
        return "default";
    }

    private static List<string> LoadPack(AssetManager assets, string packId)
    {
        foreach (var id in new[] { packId, "default" })
        {
            try
            {
                string p = assets.ResolvePath($"data/stickerpacks/{id ?? "default"}.json");
                if (p != null && global::FNF_MonoGame.Engine.FsCI.FileExists(p))
                {
                    var list = (JObject.Parse(global::FNF_MonoGame.Engine.FsCI.ReadAllText(p))["stickers"] as JArray)?.Select(x => (string)x).ToList();
                    if (list != null && list.Count > 0) return list;
                }
            }
            catch { }
        }
        return new List<string>();
    }

    /// <summary>StickerSubState.new + regenStickers(): cover the screen, then switch to <paramref name="target"/>
    /// (no fade: skipNextTransIn/Out) and remove the stickers over the new scene (degenStickers).</summary>
    public static void Start(SceneManager scenes, AssetManager assets, Action<string> playSound, string packId, Func<Scene> target)
    {
        _playSound = playSound;
        _devStarted = true; _devT = 0;
        if (System.Environment.GetEnvironmentVariable("FNF_STICKERLOG") == "1") System.Console.WriteLine($"[STICKER] start pack={packId}");
        _timers.Clear(); _t = 0f;
        _stickers = new List<Sticker>();
        Active = true; SwitchingState = false;
        var pack = LoadPack(assets, packId);
        if (pack.Count == 0) { Active = false; scenes.SwitchSceneInstant(target()); return; }
        Texture2D Pick() => assets.LoadTexture(pack[Rnd.Next(pack.Count)]);

        float xPos = -100, yPos = -100;
        while (xPos <= 1280)
        {
            var s = new Sticker { Tex = Pick(), X = xPos, Y = yPos };
            xPos += s.Tex.Width * 0.5f;
            if (xPos >= 1280 && yPos <= 720) { xPos = -100; yPos += 70f + (float)Rnd.NextDouble() * 50f; }
            s.Angle = Rnd.Next(-60, 71);
            _stickers.Add(s);
        }
        // FlxG.random.shuffle
        for (int i = _stickers.Count - 1; i > 0; i--) { int j = Rnd.Next(i + 1); (_stickers[i], _stickers[j]) = (_stickers[j], _stickers[i]); }
        var last = new Sticker { Tex = Pick(), Angle = 0 };
        last.X = (1280 - last.Tex.Width) / 2f; last.Y = (720 - last.Tex.Height) / 2f; // screenCenter()
        _stickers.Add(last);

        int n = _stickers.Count;
        for (int ind = 0; ind < n; ind++)
        {
            var sticker = _stickers[ind]; int i = ind;
            sticker.Timing = 0.9f * i / n; // FlxMath.remapToRange(ind, 0, length, 0, 0.9)
            After(sticker.Timing, () =>
            {
                sticker.Visible = true;
                PlayRandomSound();
                int frameTimer = i == n - 1 ? 2 : Rnd.Next(0, 3);
                After(frameTimer / 24f, () =>
                {
                    sticker.Scale = 0.97f + (float)Rnd.NextDouble() * 0.05f;
                    if (i == n - 1)
                    {
                        SwitchingState = true;
                        if (System.Environment.GetEnvironmentVariable("FNF_STICKERLOG") == "1") System.Console.WriteLine($"[STICKER] switch t={_t:F3} n={n}");
                        scenes.SwitchSceneInstant(target());
                        SwitchingState = false;
                        Degen();
                    }
                });
            });
        }
        // grpStickers.sort(byValues timing) - already in timing order
    }

    /// <summary>degenStickers(): hide each sticker at its timing (with a key click); close after the last.</summary>
    private static void Degen()
    {
        _timers.Clear(); _t = 0f;
        int n = _stickers.Count;
        if (n == 0) { Active = false; return; }
        for (int ind = 0; ind < n; ind++)
        {
            var sticker = _stickers[ind]; int i = ind;
            After(sticker.Timing, () =>
            {
                sticker.Visible = false;
                PlayRandomSound();
                if (i == n - 1) { if (System.Environment.GetEnvironmentVariable("FNF_STICKERLOG") == "1") System.Console.WriteLine($"[STICKER] done t={_t:F3}"); Active = false; _stickers = new List<Sticker>(); }
            });
        }
    }

    private static void PlayRandomSound()
    {
        try { _playSound?.Invoke(Sounds[Rnd.Next(Sounds.Length)]); } catch { }
    }

    private static void After(float delay, Action a) => _timers.Add((_t + delay, a));

    public static void Update(float dt)
    {
        if (_devStarted) _devT += Math.Min(dt, 0.1f);
        if (!Active) return;
        _t += Math.Min(dt, 0.1f);
        for (int guard = 0; guard < 8; guard++)
        {
            var due = _timers.Where(x => x.at <= _t).OrderBy(x => x.at).ToList();
            if (due.Count == 0) break;
            _timers.RemoveAll(x => x.at <= _t);
            foreach (var d in due) { d.a(); if (!Active) return; }
        }
    }

    public static void Draw(SpriteBatch sb)
    {
        if (!Active || _stickers.Count == 0) return;
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp);
        foreach (var s in _stickers)
        {
            if (!s.Visible || s.Tex == null) continue;
            // FlxSprite: origin = frame centre, angle/scale about the origin, x/y = top-left (offset 0)
            var origin = new Vector2(s.Tex.Width / 2f, s.Tex.Height / 2f);
            sb.Draw(s.Tex, new Vector2(s.X, s.Y) + origin, null, Color.White, MathHelper.ToRadians(s.Angle), origin, s.Scale, SpriteEffects.None, 0f);
        }
        sb.End();
    }
}
