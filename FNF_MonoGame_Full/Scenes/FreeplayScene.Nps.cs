using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FNF_MonoGame.Gameplay;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Freeplay chart difficulty preview: notes-per-second of the selected song + difficulty (player notes / playable span,
/// and the densest 1-second window), computed in the background from the same chart PlayScene would load and cached.
/// Drawn as a taped marker sticker under the score box (hand-made FunkUI look, VCR digits).
/// </summary>
public partial class FreeplayScene
{
    private sealed class NpsInfo { public double Avg, Peak; public int Notes; public bool Failed; }
    private static readonly ConcurrentDictionary<string, NpsInfo> _npsCache = new();
    private static readonly ConcurrentDictionary<string, byte> _npsPending = new();
    private string _npsKey; private float _npsShowT;
    private FNF_MonoGame.Engine.ReplayData _npsReplay;
    private static FNF_MonoGame.Engine.ReplayData FindReplay(string song, string diff)
    {
        try
        {   // file names are <song>-<difficulty>-<date>.json: only the newest matching file is parsed
            var files = global::FNF_MonoGame.Engine.FsCI.GetFiles(FNF_MonoGame.Engine.ReplayManager.Dir, song + "-" + diff + "-*.json");
            if (files.Length == 0) return null;
            System.Array.Sort(files, StringComparer.Ordinal);
            var r = FNF_MonoGame.Engine.ReplayManager.Load(files[files.Length - 1]);
            if (r != null) r.Inputs = null;
            return r;
        }
        catch { return null; }
    }

    private void UpdateNpsPreview(float dt)
    {
        _npsShowT += dt;
        if (_selectedIndex < 0 || _selectedIndex >= _songs.Count) { _npsKey = null; return; }
        var song = _songs[_selectedIndex];
        if (song == null || song.IsRandom) { _npsKey = null; return; }
        string diff = GetCurrentDifficulty(song);
        string name = ResolveSongNameForSelection(song, diff);
        string key = $"{name}|{diff}|{song.ModPath}";
        if (key != _npsKey) { _npsKey = key; _npsShowT = 0f; _npsReplay = FindReplay(name, diff); }
        // R / right-stick click: watch the newest replay of this song + difficulty
        if (_npsReplay != null && _npsShowT > 0.3f && (Input.IsPressed(Microsoft.Xna.Framework.Input.Keys.R) || Input.IsGamePadPressed(Microsoft.Xna.Framework.Input.Buttons.RightStick)))
        {
            var ps = PlayScene.CreateReplayScene(_npsReplay);
            if (ps != null) { Audio.StopMusic(); Audio.PlaySound("confirmMenu"); Game.Scenes.ChangeScene(ps); return; }
        }
        if (_npsCache.ContainsKey(key) || !_npsPending.TryAdd(key, 0)) return;
        var assets = Assets; string mod = song.ModPath;
        Task.Run(() =>
        {
            var info = new NpsInfo();
            try
            {
                var chart = Chart.Load(name, assets, diff, mod);
                var times = new System.Collections.Generic.List<double>();
                if (chart?.Notes != null) foreach (var n in chart.Notes) if (n.IsPlayerNote) times.Add(n.Time);
                times.Sort();
                info.Notes = times.Count;
                if (times.Count >= 2)
                {
                    double span = Math.Max(1.0, times[times.Count - 1] - times[0]);
                    info.Avg = times.Count / span;
                    int j = 0;
                    for (int i = 0; i < times.Count; i++) { while (times[i] - times[j] >= 1.0) j++; info.Peak = Math.Max(info.Peak, i - j + 1); }
                }
                else info.Failed = times.Count == 0;
            }
            catch (Exception e) { info.Failed = true; Console.WriteLine($"[NPS] {name} {diff}: {e.Message}"); }
            _npsCache[key] = info;
            _npsPending.TryRemove(key, out _);
        });
    }

    private void DrawNpsPreview(SpriteBatch sb)
    {
        if (_npsKey == null || !_npsCache.TryGetValue(_npsKey, out var info) || info.Failed) return;
        var font = Assets.GetFont(18);
        if (font == null) return;
        float pop = Math.Min(1f, _npsShowT / 0.18f);
        float squash = pop < 1f ? 1.25f - 0.25f * pop : 1f;           // slapped on
        pop *= CsFadeF;   // port-only sticker fades with the to-CharSelect BlueFade
        string a = $"NPS {info.Avg:0.0}", b = $"PEAK {info.Peak:0}";
        // marker colour by density: green < 4, yellow < 7, orange < 10, red
        Color mark = info.Avg < 4 ? new Color(0x3E, 0xD8, 0x6A) : info.Avg < 7 ? new Color(0xFF, 0xD2, 0x3F) : info.Avg < 10 ? new Color(0xFF, 0x8A, 0x2E) : new Color(0xFF, 0x3E, 0x5A);
        int w = 230, h = 34;
        var m = Matrix.CreateTranslation(-w / 2f, -h / 2f, 0) * Matrix.CreateScale(squash, 1f / squash, 1f) * Matrix.CreateRotationZ(0.045f)
              * Matrix.CreateTranslation(1110, 228 + CsDy(-270f), 0);   // port-only sticker follows the score group (exitMoversCharSel y -270)
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp, null, null, null, m);
        sb.Draw(Assets.Pixel, new Rectangle(5, 5, w, h), new Color(Color.Black, 0.5f * pop));      // hard shadow
        sb.Draw(Assets.Pixel, new Rectangle(-3, -3, w + 6, h + 6), new Color(Color.Black, pop));    // chunky outline
        sb.Draw(Assets.Pixel, new Rectangle(0, 0, w, h), new Color(Color.White, pop));
        sb.Draw(Assets.Pixel, new Rectangle(0, 0, 12, h), new Color(mark, pop));                     // marker swatch
        sb.Draw(Assets.Pixel, new Rectangle(w - 30, -9, 22, 13), new Color(new Color(0xFF, 0xE0, 0x6B), 0.8f * pop)); // tape
        font.DrawText(sb, a, new Vector2(22, 7), new Color(Color.Black, pop));
        font.DrawText(sb, b, new Vector2(128, 7), new Color(new Color(0x60, 0x20, 0x70), pop));
        if (_npsReplay != null)
        {   // second strip: replay available
            sb.Draw(Assets.Pixel, new Rectangle(23, h + 8, 190, 30), new Color(Color.Black, 0.5f * pop));
            sb.Draw(Assets.Pixel, new Rectangle(15, h + 1, 192, 32), new Color(Color.Black, pop));
            sb.Draw(Assets.Pixel, new Rectangle(18, h + 4, 186, 26), new Color(new Color(0x24, 0x18, 0x2E), pop));
            bool pad = Input.LastDevice == FNF_MonoGame.Engine.InputManager.InputDevice.Controller;
            FNF_MonoGame.Engine.ButtonPrompts.DrawGlyph(sb, Assets, pad, Microsoft.Xna.Framework.Input.Buttons.RightStick, "R", 22, h + 5, 24, pop);
            font.DrawText(sb, "REPLAY " + _npsReplay.Score, new Vector2(54, h + 8), new Color(Color.White, pop));
        }
        sb.End();
    }
}
