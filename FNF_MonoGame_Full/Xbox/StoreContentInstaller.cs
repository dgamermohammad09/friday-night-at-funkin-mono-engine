using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace FNF_MonoGame;

/// <summary>Public Microsoft Store build: the package ships only the engine / Psych / Psych Online / our UI files plus
/// Content\_store\manifest.txt. On first start this installs the Friday Night Funkin' game data from The Funkin' Crew's
/// official public GitHub repository (pinned commit) into LocalState\Content:
///   copy the package's Content -> download the official zip -> extract the listed files -> download single files from
///   the official repos -> rebuild the port's repacked sprite sheets from official images (Content\_store\recipes).
/// No FNF art or music is inside the package.</summary>
internal sealed class StoreContentInstaller
{
    private readonly ContentDownloader _ui;
    private readonly string _local, _content, _pkgContent, _work;
    private string _version, _zipUrl, _zipPrefix;
    private readonly List<string[]> _zip = new(), _raw = new(), _recipes = new(), _meta = new(), _grey = new();

    public static string PackageManifest =>
        Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, "Content", "_store", "manifest.txt");
    public static bool IsStoreBuild => File.Exists(PackageManifest);

    public StoreContentInstaller(ContentDownloader ui, string localState)
    {
        _ui = ui; _local = localState;
        _content = Path.Combine(localState, "Content");
        _pkgContent = Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, "Content");
        _work = Path.Combine(localState, "_install");
        foreach (var line in File.ReadAllLines(PackageManifest))
        {
            var f = line.Split('\t');
            if (f.Length < 2) continue;
            switch (f[0])
            {
                case "V": _version = f[1]; break;
                case "U": _zipUrl = f[1]; break;
                case "X": _zipPrefix = f[1]; break;
                case "Z": _zip.Add(f); break;
                case "R": _raw.Add(f); break;
                case "P": _recipes.Add(f); break;
                case "M": _meta.Add(f); break;
                case "G": _grey.Add(f); break;   // G dest zipSrc x0 y0 x1 y1 : crop + greyscale (ITU-R 601 luma, like PIL "L")
            }
        }
    }

    public string Version => _version;
    private string Marker => Path.Combine(_local, "store_content_version.txt");

    public bool Installed()
    {
        try { return File.Exists(Marker) && File.ReadAllText(Marker).Trim() == _version && Directory.Exists(Path.Combine(_content, "data")); }
        catch { return false; }
    }

    private void Report(string status, float progress) { _ui.SetProgress(status, progress); }

    public async Task InstallAsync()
    {
        Console.WriteLine($"[STORE] installing game data {_version}: zip files {_zip.Count}, single files {_raw.Count}, rebuilt images {_recipes.Count}");
        try { File.Delete(Marker); } catch { }
        Directory.CreateDirectory(_work);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
        http.DefaultRequestHeaders.Add("User-Agent", "FNF-MonoGame-Engine");

        // 1. engine / Psych / our UI files that ship inside the package (0 - 2 %)
        Report("Preparing...", 0f);
        await FetchPreloaderFontAsync(http);
        CopyDir(_pkgContent, _content, skip: "_store");

        // 2. official files, one by one from raw.githubusercontent.com (resumable; 6 at a time) (2 - 92 %)
        string commit = _zipPrefix.Substring(_zipPrefix.IndexOf('-') + 1).TrimEnd('/');
        string rawBase = $"https://raw.githubusercontent.com/FunkinCrew/funkin.assets/{commit}/";
        var recipeLines = new Dictionary<string, string[]>();
        var srcFile = new Dictionary<string, string>();               // recipe source ref -> local file
        foreach (var r in _recipes)
        {
            var lines = File.ReadAllLines(Path.Combine(_pkgContent, "_store", "recipes", r[2]));
            recipeLines[r[1]] = lines;
            foreach (var l in lines)
            {
                if (!l.StartsWith("op\t")) continue;
                string src = l.Split('\t')[1];
                if (!srcFile.ContainsKey(src)) srcFile[src] = Path.Combine(_work, "src", srcFile.Count + ".png");
            }
        }
        var jobs = new List<(string Url, string Dest)>();
        foreach (var z in _zip) jobs.Add((rawBase + Uri.EscapeUriString(z[2]), Path.Combine(_content, z[1].Replace('/', '\\'))));
        foreach (var r in _raw) jobs.Add((r[2], Path.Combine(_content, r[1].Replace('/', '\\'))));
        foreach (var g in _grey) jobs.Add((rawBase + Uri.EscapeUriString(g[2]), Path.Combine(_work, "grey", g[1].Replace('/', '_'))));
        foreach (var kv in srcFile)
            jobs.Add((kv.Key.StartsWith("zip:") ? rawBase + Uri.EscapeUriString(kv.Key.Substring(4)) : kv.Key.Substring(4), kv.Value));
        int done = 0;
        using (var gate = new System.Threading.SemaphoreSlim(6))
        {
            var tasks = new List<Task>();
            foreach (var j in jobs)
            {
                await gate.WaitAsync();
                tasks.Add(Task.Run(async () =>
                {
                    try { await DownloadAsync(http, j.Url, j.Dest, null, 0, 0, 0); }
                    finally
                    {
                        int n2 = System.Threading.Interlocked.Increment(ref done);
                        Report($"Downloading game data from The Funkin' Crew's GitHub... {n2} / {jobs.Count} files", 0.02f + 0.90f * n2 / jobs.Count);
                        gate.Release();
                    }
                }));
            }
            await Task.WhenAll(tasks);
        }
        foreach (var g in _grey)
        {
            var im = await LoadAsync(Path.Combine(_work, "grey", g[1].Replace('/', '_')));
            int x0 = int.Parse(g[3]), y0 = int.Parse(g[4]), x1 = int.Parse(g[5]), y1 = int.Parse(g[6]);
            var c = im.Crop(x0, y0, x1 - x0, y1 - y0);
            for (int i = 0; i < c.P.Length; i += 4)
            {
                byte l = (byte)((c.P[i + 2] * 299 + c.P[i + 1] * 587 + c.P[i] * 114) / 1000);
                c.P[i] = l; c.P[i + 1] = l; c.P[i + 2] = l;
            }
            await SaveAsync(c, Path.Combine(_content, g[1].Replace('/', '\\')));
        }

        // 5. rebuild the repacked sprite sheets (92 - 99 %)
        var cache = new Dictionary<string, Img>();
        int n = 0;
        foreach (var kv in recipeLines)
        {
            Report($"Building images... {++n}/{recipeLines.Count}", 0.92f + 0.07f * n / Math.Max(1, recipeLines.Count));
            await BuildAsync(kv.Value, Path.Combine(_content, kv.Key.Replace('/', '\\')), src => srcFile[src], cache);
            if (cache.Count > 12) cache.Clear();
        }
        foreach (var m in _meta)
        {
            string p = Path.Combine(_content, m[1].Replace('/', '\\'));
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            File.WriteAllText(p, m[2]);
        }

        try { Directory.Delete(_work, true); } catch { }
        File.WriteAllText(Marker, _version);
        Console.WriteLine("[STORE] game data installed");
        Report("Game data ready!", 1f);
    }

    // ------------------------------------------------------------------ helpers
    /// <summary>The download screen (FunkinPreloader look) uses DS-Digital, which is FNF data and not in the package:
    /// fetch just that font from the official repo first. Best effort - the screen falls back to Quantico without it.</summary>
    private async Task FetchPreloaderFontAsync(HttpClient http)
    {
        string dest = Path.Combine(_content, "fonts", "DS-DIGIB.TTF");
        if (File.Exists(dest)) return;
        try
        {
            string commit = _zipUrl.Substring(_zipUrl.LastIndexOf('/') + 1);
            string url = $"https://raw.githubusercontent.com/FunkinCrew/funkin.assets/{commit}/fonts/DS-DIGIB.TTF";
            var fetch = http.GetByteArrayAsync(url);
            if (await Task.WhenAny(fetch, Task.Delay(15000)) != fetch) { Console.WriteLine("[STORE] preloader font: timeout"); return; }
            byte[] b = await fetch;
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            File.WriteAllBytes(dest + ".part", b);
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(dest + ".part", dest);
            Console.WriteLine($"[STORE] preloader font DS-DIGIB.TTF ({b.Length} bytes)");
        }
        catch (Exception ex) { Console.WriteLine("[STORE] preloader font: " + ex.Message); }
    }

    private void CopyDir(string src, string dst, string skip)
    {
        foreach (var dir in Directory.GetDirectories(src))
        {
            string name = Path.GetFileName(dir);
            if (string.Equals(name, skip, StringComparison.OrdinalIgnoreCase)) continue;
            CopyDir(dir, Path.Combine(dst, name), null);
        }
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), true);
    }

    private static async Task ExtractAsync(ZipArchiveEntry e, string dest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest));
        using var s = e.Open();
        using var d = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true);
        await s.CopyToAsync(d);
    }

    private async Task DownloadAsync(HttpClient http, string url, string dest, string label, float p0, float p1, long expected)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest));
        try { File.Delete(dest); } catch { }
        long total = expected;
        for (int attempt = 1; ; attempt++)
        {
            long have = File.Exists(dest) ? new FileInfo(dest).Length : 0;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                if (have > 0) req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(have, null);   // resume after a dropped connection
                using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)resp.StatusCode} for {url}");
                bool partial = have > 0 && resp.StatusCode == System.Net.HttpStatusCode.PartialContent;
                if (!partial) have = 0;
                long? len = resp.Content.Headers.ContentLength;
                if (len.HasValue) total = have + len.Value;
                using var s = await resp.Content.ReadAsStreamAsync();
                using var d = new FileStream(dest, partial ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true);
                var buf = new byte[1 << 16]; long got = have; int r; int tick = 0;
                while ((r = await s.ReadAsync(buf, 0, buf.Length)) > 0)
                {
                    await d.WriteAsync(buf, 0, r); got += r;
                    if (label != null && ++tick % 32 == 0)
                    {
                        float f = total > 0 ? Math.Min(1f, got / (float)total) : 0f;
                        Report(total > 0 ? $"{label}... {got >> 20} / {total >> 20} MB" : $"{label}... {got >> 20} MB", p0 + (p1 - p0) * f);
                    }
                }
                if (len.HasValue && got < total) throw new IOException($"connection closed at {got} of {total} bytes");
                return;
            }
            catch (Exception ex) when (attempt < 30)
            {
                Console.WriteLine($"[STORE] download retry {attempt} (resume at {(File.Exists(dest) ? new FileInfo(dest).Length : 0) >> 20} MB): {url} ({ex.Message})");
                if (label != null) Report($"Connection lost, resuming... (try {attempt})", p0);
                await Task.Delay(Math.Min(15000, 2000 * attempt));
            }
        }
    }

    // ------------------------------------------------------------------ image rebuild (straight-alpha BGRA)
    private sealed class Img
    {
        public int W, H; public byte[] P;
        public Img(int w, int h) { W = w; H = h; P = new byte[w * h * 4]; }
        public Img Crop(int x, int y, int w, int h)
        {
            var o = new Img(w, h);
            for (int j = 0; j < h; j++)
            {
                int sy = y + j; if (sy < 0 || sy >= H) continue;
                for (int i = 0; i < w; i++)
                {
                    int sx = x + i; if (sx < 0 || sx >= W) continue;
                    System.Buffer.BlockCopy(P, (sy * W + sx) * 4, o.P, (j * w + i) * 4, 4);
                }
            }
            return o;
        }
        /// <summary>PIL Image.rotate(angle, expand=True) for angle = +90 (counter-clockwise) / -90 (clockwise).</summary>
        public Img Rotate(int angle)
        {
            if (angle == 0) return this;
            var o = new Img(H, W);
            for (int ny = 0; ny < o.H; ny++)
                for (int nx = 0; nx < o.W; nx++)
                {
                    int x, y;
                    if (angle == 90) { x = W - 1 - ny; y = nx; } else { x = ny; y = H - 1 - nx; }
                    System.Buffer.BlockCopy(P, (y * W + x) * 4, o.P, (ny * o.W + nx) * 4, 4);
                }
            return o;
        }
        public void Clean() { for (int i = 0; i < P.Length; i += 4) if (P[i + 3] == 0) { P[i] = 0; P[i + 1] = 0; P[i + 2] = 0; } }
        public void Paste(Img s, int dx, int dy)
        {
            for (int j = 0; j < s.H; j++)
            {
                int y = dy + j; if (y < 0 || y >= H) continue;
                for (int i = 0; i < s.W; i++)
                {
                    int x = dx + i; if (x < 0 || x >= W) continue;
                    System.Buffer.BlockCopy(s.P, (j * s.W + i) * 4, P, (y * W + x) * 4, 4);
                }
            }
        }
    }

    private static async Task<Img> LoadAsync(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        using IRandomAccessStream s = await file.OpenAsync(FileAccessMode.Read);
        var dec = await BitmapDecoder.CreateAsync(s);
        var px = await dec.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        var img = new Img((int)dec.PixelWidth, (int)dec.PixelHeight) { };
        img.P = px.DetachPixelData();
        return img;
    }

    private static async Task SaveAsync(Img img, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(path));
        var file = await folder.CreateFileAsync(Path.GetFileName(path), CreationCollisionOption.ReplaceExisting);
        using IRandomAccessStream s = await file.OpenAsync(FileAccessMode.ReadWrite);
        var enc = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, s);
        enc.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, (uint)img.W, (uint)img.H, 96, 96, img.P);
        await enc.FlushAsync();
    }

    private static async Task BuildAsync(string[] recipe, string dest, Func<string, string> resolve, Dictionary<string, Img> cache)
    {
        Img outImg = null;
        foreach (var line in recipe)
        {
            var f = line.Split('\t');
            if (f[0] == "size") { outImg = new Img(int.Parse(f[1]), int.Parse(f[2])); continue; }
            if (f[0] != "op" || outImg == null) continue;
            int[] v = new int[f.Length - 2];
            for (int i = 2; i < f.Length; i++) v[i - 2] = int.Parse(f[i]);
            // v: sx sy sw sh srot sb0 sb1 sb2 sb3 dx dy dw dh drot db0 db1 db2 db3
            if (!cache.TryGetValue(f[1], out var src)) cache[f[1]] = src = await LoadAsync(resolve(f[1]));
            var up = src.Crop(v[0], v[1], v[2], v[3]).Rotate(v[4]);
            up.Clean();
            var piece = up.Crop(v[5], v[6], v[7] - v[5], v[8] - v[6]);
            int dw = v[11], dh = v[12], drot = v[13];
            var frame = drot != 0 ? new Img(dh, dw) : new Img(dw, dh);
            frame.Paste(piece, v[14], v[15]);
            outImg.Paste(frame.Rotate(-drot), v[9], v[10]);
        }
        if (outImg != null) await SaveAsync(outImg, dest);
    }
}
