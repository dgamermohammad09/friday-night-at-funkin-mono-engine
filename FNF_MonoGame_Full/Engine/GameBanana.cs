using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace FNF_MonoGame.Engine;

/// <summary>
/// GameBanana (apiv11 JSON) client for the in-game mod browser: list / search Friday Night Funkin' mods (game 8694),
/// mod files (_aFiles: _sFile, _nFilesize, _sDownloadUrl), thumbnails, and download + install into the mods folder
/// (PC &lt;exe&gt;/mods, Xbox LocalState\mods). Archives: ZIP (System.IO.Compression), 7z and RAR via SharpCompress
/// (managed, works on UWP). Install layout: Psych mod folders go through Psych Online's installer
/// (PsychOnline.InstallModArchive: finds the mod root, flattens mods/assets/shared, writes pack.json + mod_url.txt);
/// V-Slice (Polymod) mods are detected by _polymod_meta.json and extracted as-is. The new mod is enabled afterwards.
/// </summary>
public static class GameBanana
{
    public const int GameId = 8694;
    public const string Api = "https://gamebanana.com/apiv11";

    public sealed class Mod
    {
        public int Id; public string Name, Author, Category, Thumb, ProfileUrl, Version;
        public int Likes, Views, Posts; public long Added, Updated; public bool HasFiles, Nsfw;
    }

    public sealed class FileInfo
    {
        public int Id; public string Name, Url, Description, Analysis; public long Size, Downloads; public bool HasExe;
        /// <summary>Non-null for files hosted outside GameBanana (alternate file sources / description links).</summary>
        public ExternalDownloads.Source External;
        public string Ext => Path.GetExtension(Name ?? "").ToLowerInvariant();
        public bool ArchiveExt => Ext is ".zip" or ".7z" or ".rar" or ".tar" or ".tgz" or ".gz";
        /// <summary>External links: anything the resolver can fetch (the archive type is sniffed after the download).</summary>
        public bool Supported => External != null ? !External.NeedsBrowser : ArchiveExt;
        public bool NeedsBrowser => External != null && External.NeedsBrowser;
    }

    /// <summary>Browse tabs: (label, category id or 0 = whole game, help).</summary>
    public static readonly (string Label, int Cat, string Help)[] Tabs =
    {
        ("PSYCH MODS", 28367, "Psych Engine mod folders - they run in this engine."),
        ("V-SLICE MODS", 29202, "Base game (V-Slice / Polymod) mod folders."),
        ("ALL MOD FOLDERS", 43771, "Every mod folder category (Codename mods may not work)."),
        ("OTHER", 3046, "Other / misc uploads (skins, assets...)."),
    };
    public static readonly (string Label, string Sort)[] Sorts =
    {
        ("POPULAR", "Generic_MostDownloaded"), ("NEWEST", "Generic_Newest"), ("MOST LIKED", "Generic_MostLiked"), ("UPDATED", "Generic_LatestUpdated"),
    };

    private static readonly HttpClient _http = MakeClient();
    private static HttpClient MakeClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (FNF-MonoGame mod browser)");
        return h;
    }

    private static async Task<string> GetStringRetry(string url, CancellationToken ct = default)
    {
        Exception last = null;
        for (int i = 0; i < 3; i++)
        {
            try
            {
                using var r = await _http.GetAsync(url, ct).ConfigureAwait(false);
                if ((int)r.StatusCode == 503 || (int)r.StatusCode == 429 || (int)r.StatusCode == 502)
                { last = new HttpRequestException("GameBanana is busy (HTTP " + (int)r.StatusCode + ")"); await Task.Delay(800 * (i + 1), ct).ConfigureAwait(false); continue; }
                r.EnsureSuccessStatusCode();
                return await r.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { last = new TimeoutException("GameBanana didn't answer (timeout)"); }
            catch (HttpRequestException e) { last = e; await Task.Delay(600 * (i + 1), ct).ConfigureAwait(false); }
        }
        throw last ?? new Exception("request failed");
    }

    private static Mod ParseRecord(JObject r)
    {
        var m = new Mod
        {
            Id = (int?)r["_idRow"] ?? 0, Name = (string)r["_sName"] ?? "?", ProfileUrl = (string)r["_sProfileUrl"],
            Author = (string)r["_aSubmitter"]?["_sName"] ?? "?", Category = (string)r["_aRootCategory"]?["_sName"] ?? "",
            Likes = (int?)r["_nLikeCount"] ?? 0, Views = (int?)r["_nViewCount"] ?? 0, Posts = (int?)r["_nPostCount"] ?? 0,
            Added = (long?)r["_tsDateAdded"] ?? 0, Updated = (long?)r["_tsDateUpdated"] ?? 0, HasFiles = (bool?)r["_bHasFiles"] ?? true,
            Version = (string)r["_sVersion"], Nsfw = ((string)r["_sInitialVisibility"] ?? "show") != "show" || ((bool?)r["_bHasContentRatings"] ?? false),
        };
        var img = (r["_aPreviewMedia"]?["_aImages"] as JArray)?.FirstOrDefault() as JObject;
        if (img != null)
        {
            string bas = (string)img["_sBaseUrl"], f = (string)img["_sFile220"] ?? (string)img["_sFile100"] ?? (string)img["_sFile"];
            if (bas != null && f != null) m.Thumb = bas + "/" + f;
        }
        return m;
    }

    /// <summary>Mod list of a category tab (page from 1). Returns (mods, total records).</summary>
    public static async Task<(List<Mod> mods, int total)> ListAsync(int tab, int sort, int page, int perPage = 15, CancellationToken ct = default)
    {
        var (_, cat, _) = Tabs[Math.Clamp(tab, 0, Tabs.Length - 1)];
        string filter = cat > 0 ? "_aFilters%5BGeneric_Category%5D=" + cat : "_aFilters%5BGeneric_Game%5D=" + GameId;
        string url = $"{Api}/Mod/Index?_nPerpage={perPage}&_nPage={page}&{filter}&_sSort={Sorts[Math.Clamp(sort, 0, Sorts.Length - 1)].Sort}";
        var j = JObject.Parse(await GetStringRetry(url, ct).ConfigureAwait(false));
        var l = (j["_aRecords"] as JArray ?? new JArray()).OfType<JObject>().Where(o => (string)o["_sModelName"] == "Mod").Select(ParseRecord).ToList();
        return (l, (int?)j["_aMetadata"]?["_nRecordCount"] ?? l.Count);
    }

    /// <summary>Search FNF mods by name (Util/Search, best match).</summary>
    public static async Task<(List<Mod> mods, int total)> SearchAsync(string text, int page, int perPage = 15, CancellationToken ct = default)
    {
        string url = $"{Api}/Util/Search/Results?_sModelName=Mod&_sOrder=best_match&_idGameRow={GameId}&_sSearchString={Uri.EscapeDataString(text ?? "")}&_nPage={page}&_nPerpage={perPage}";
        var j = JObject.Parse(await GetStringRetry(url, ct).ConfigureAwait(false));
        var l = (j["_aRecords"] as JArray ?? new JArray()).OfType<JObject>().Where(o => (string)o["_sModelName"] == "Mod").Select(ParseRecord).ToList();
        return (l, (int?)j["_aMetadata"]?["_nRecordCount"] ?? l.Count);
    }

    /// <summary>Downloadable files of a mod (ProfilePage-lite: _aFiles + description).</summary>
    public static async Task<(List<FileInfo> files, string text)> FilesAsync(int id, CancellationToken ct = default)
    {
        var j = JObject.Parse(await GetStringRetry($"{Api}/Mod/{id}?_csvProperties=_aFiles,_sText,_nDownloadCount,_sDescription,_aAlternateFileSources,_sName", ct).ConfigureAwait(false));
        LastName = (string)j["_sName"];
        var l = new List<FileInfo>();
        foreach (var f in (j["_aFiles"] as JArray ?? new JArray()).OfType<JObject>())
            l.Add(new FileInfo
            {
                Id = (int?)f["_idRow"] ?? 0, Name = (string)f["_sFile"], Url = (string)f["_sDownloadUrl"], Size = (long?)f["_nFilesize"] ?? 0,
                Downloads = (long?)f["_nDownloadCount"] ?? 0, Description = (string)f["_sDescription"], Analysis = (string)f["_sAvResult"] ?? (string)f["_sAnalysisResult"],
                HasExe = f["_aAnalysisWarnings"]?["contains_exe"] != null,
            });
        // files hosted elsewhere: GameBanana "alternate file sources" first, then download links in the description
        var urls = new List<(string url, string desc)>();
        foreach (var a in (j["_aAlternateFileSources"] as JArray ?? new JArray()).OfType<JObject>())
        {
            string u = (string)a["url"] ?? (string)a["_sUrl"];
            if (!string.IsNullOrWhiteSpace(u)) urls.Add((u.Trim(), (string)a["description"] ?? (string)a["_sDescription"]));
        }
        foreach (var u in ExternalDownloads.LinksInText((string)j["_sText"]).Take(6))
            if (!urls.Any(x => x.url == u)) urls.Add((u, "link from the mod description"));
        int n = 0;
        foreach (var (u, d) in urls.Take(10))
        {
            var src = ExternalDownloads.Make(u, d);
            var list = new List<ExternalDownloads.Source> { src };
            if (src.Kind == ExternalDownloads.HostKind.GitHub)
                try { list = await ExternalDownloads.ExpandGitHubAsync(_http, src, ct).ConfigureAwait(false); } catch (OperationCanceledException) { throw; } catch { }
            foreach (var s in list)
                l.Add(new FileInfo
                {
                    Id = -(++n), Name = s.Name ?? "from " + s.HostLabel.ToLowerInvariant(), Url = s.Url, Size = s.Size, External = s,
                    Description = string.IsNullOrWhiteSpace(s.Description) ? null : s.Description.Trim(),
                });
        }
        string text = (string)j["_sDescription"] ?? "";
        return (l, text);
    }

    /// <summary>_sName of the last FilesAsync call (dev hook FNF_GB_MODID opens a mod without the list).</summary>
    public static string LastName;

    public static async Task<byte[]> GetBytesAsync(string url, CancellationToken ct = default)
    {
        using var r = await _http.GetAsync(url, ct).ConfigureAwait(false);
        r.EnsureSuccessStatusCode();
        return await r.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ download + install
    public sealed class Job
    {
        public string Status = "Starting ...";
        public float Progress = -1f;    // 0..1, -1 = unknown
        public string Result;           // installed mod folder (null while running / failed)
        public string Error;
        public bool Done;
        public string Layout;           // "Psych" / "V-Slice"
        public string BrowserUrl;       // set when the host needs a web browser (login / captcha / expired link)
        public readonly CancellationTokenSource Cancel = new();
        public Task Task;               // finishes when Done is set
        public string ModUrl;           // URL saved in mod_url.txt (what Psych Online rooms send as modURL)
        public string Source;           // what is being installed (mod name / host) for the UI
    }

    public static Job Install(Mod mod, FileInfo file)
    {
        var job = new Job { ModUrl = "https://gamebanana.com/mods/" + mod.Id, Source = mod.Name };
        job.Task = Task.Run(async () =>
        {
            try { job.Result = await DownloadAndInstall(mod, file, job).ConfigureAwait(false); }
            catch (OperationCanceledException) { job.Error = "Cancelled"; }
            catch (ExternalDownloads.NeedsBrowserException e) { job.Error = e.Message; job.BrowserUrl = file.External?.Url ?? file.Url; }
            catch (Exception e) { job.Error = (e.InnerException ?? e).Message; }
            finally { job.Done = true; }
        });
        return job;
    }

    /// <summary>
    /// Sidecar next to mod_url.txt: the GameBanana direct file link (https://gamebanana.com/dl/&lt;fileId&gt;) of the archive we installed.
    /// Rooms get this link as modURL: the real Psych Online client (0.14.x) cannot install from a mod page URL any more (its
    /// GameBanana.downloadMod filters files on the legacy API field _sClamAvResult, which the API now names _sAvResult, so every
    /// file is rejected as "Unsupported file archive type"), but it downloads a direct application/zip link fine.
    /// </summary>
    public const string PsoUrlFile = "pso_url.txt";
    public static bool IsGbFileUrl(string u) => !string.IsNullOrEmpty(u) && System.Text.RegularExpressions.Regex.IsMatch(u, @"^https?://(?:www\.)?gamebanana\.com/dl/\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>GameBanana mod id from a Psych Online mod URL ("https://gamebanana.com/mods/123", also www./http/download forms); 0 = none.</summary>
    public static int ModIdFromUrl(string url)
    {
        var m = System.Text.RegularExpressions.Regex.Match(url ?? "", @"^https?://(?:www\.)?gamebanana\.com/mods/(?:download/)?(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success && int.TryParse(m.Groups[1].Value, out int id) ? id : 0;
    }

    /// <summary>
    /// Downloads + installs a Psych Online room / skin mod URL (OnlineMods.downloadMod): GameBanana mod pages pick the file like
    /// GameBanana.downloadMod (supported archive, AV clean, most downloads; then 7z / external alternates), Google Drive / MediaFire /
    /// Dropbox / Pixeldrain / GitHub links go through ExternalDownloads, any other http(s) link is fetched directly. Archives: ZIP, 7Z,
    /// RAR, TAR, TGZ. <paramref name="wantModDir"/> = the room's modDir: the installed folder is renamed to it when the archive gives
    /// another name (the room only accepts the chart from mods/&lt;modDir&gt;). mod_url.txt keeps the room URL.
    /// </summary>
    public static Job InstallFromUrl(string url, string wantModDir)
    {
        url = (url ?? "").Trim();
        var job = new Job { ModUrl = url, Source = string.IsNullOrEmpty(wantModDir) ? ExternalDownloads.HostOf(url) : wantModDir };
        job.Task = Task.Run(async () =>
        {
            FileInfo file = null;
            try
            {
                if (!(url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
                    throw new Exception("invalid mod URL");
                int gbId = ModIdFromUrl(url);
                Mod mod;
                if (gbId > 0)
                {
                    job.Status = "Asking GameBanana about mod " + gbId + " ...";
                    var (files, _) = await FilesAsync(gbId, job.Cancel.Token).ConfigureAwait(false);
                    mod = new Mod { Id = gbId, Name = LastName ?? ("mod " + gbId) };
                    job.Source = mod.Name;
                    bool Psoish(FileInfo f) => f.External == null && f.Ext is ".zip" or ".rar" or ".tar" or ".tgz" or ".gz";
                    bool Clean(FileInfo f) => string.IsNullOrEmpty(f.Analysis) || f.Analysis.Equals("clean", StringComparison.OrdinalIgnoreCase) || f.Analysis.Equals("ok", StringComparison.OrdinalIgnoreCase);
                    file = files.Where(f => Psoish(f) && Clean(f)).OrderByDescending(f => f.Downloads).FirstOrDefault()
                        ?? files.Where(f => f.External == null && f.Supported).OrderByDescending(f => f.Downloads).FirstOrDefault()
                        ?? files.FirstOrDefault(f => f.External != null && f.Supported);
                    if (file == null)
                    {
                        job.BrowserUrl = files.FirstOrDefault(f => f.NeedsBrowser)?.External?.Url ?? "https://gamebanana.com/mods/" + gbId;
                        throw new ExternalDownloads.NeedsBrowserException("no downloadable ZIP / 7Z / RAR / TAR file on GameBanana");
                    }
                    job.ModUrl = "https://gamebanana.com/mods/" + gbId;   // Psych Online's own format
                }
                else
                {
                    var src = ExternalDownloads.MakeForRoom(url);
                    if (src.NeedsBrowser) { job.BrowserUrl = url; throw new ExternalDownloads.NeedsBrowserException(src.Why ?? "this link needs a web browser"); }
                    file = new FileInfo { Id = -1, Name = src.Name ?? "mod.zip", Url = src.Url, External = src };
                    mod = new Mod { Id = 0, Name = string.IsNullOrEmpty(wantModDir) ? PsychOnline.FormatFile(src.Name != null ? Path.GetFileNameWithoutExtension(src.Name) : ExternalDownloads.HostOf(url)) : wantModDir };
                }
                job.Result = await DownloadAndInstall(mod, file, job, job.ModUrl, wantModDir).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { job.Error = "Cancelled"; }
            catch (ExternalDownloads.NeedsBrowserException e) { job.Error = e.Message; job.BrowserUrl ??= file?.External?.Url ?? url; }
            catch (Exception e) { job.Error = (e.InnerException ?? e).Message; }
            finally { job.Done = true; Console.WriteLine("[GB] room mod " + url + " -> " + (job.Error != null ? "ERROR " + job.Error : job.Result + " (" + job.Layout + ")")); }
        });
        return job;
    }

    /// <summary>Leftovers of crashed/cancelled downloads older than a day (never the ones in use: those are locked or fresh).</summary>
    private static void CleanStaleParts(string dir)
    {
        try
        {
            foreach (var f in Directory.GetFiles(dir, "*.part*"))
                try { if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(f)).TotalHours > 24) File.Delete(f); } catch { }
        }
        catch { }
    }

    private static string ModsBase => PsychEngine.PsychMods.ModsBasePath;

    private static async Task<string> DownloadAndInstall(Mod mod, FileInfo file, Job job, string modUrl = null, string wantModDir = null)
    {
        if (string.IsNullOrEmpty(ModsBase)) throw new Exception("mods folder not available");
        if (file.NeedsBrowser) throw new ExternalDownloads.NeedsBrowserException(file.External.Why ?? "this host needs a web browser");
        if (!file.Supported) throw new Exception(file.Ext + " archives are not supported (zip / 7z / rar only)");
        string dir = Path.Combine(ModsBase, ".downloads");
        Directory.CreateDirectory(dir);
        // Unique per download: a room mod and a skin (or a retry while the old job is still closing) used to share
        // "ext0_1.part" (external links have no GameBanana id) -> "being used by another process" on Xbox.
        string tmp = Path.Combine(dir, (file.Id < 0 ? "ext" + mod.Id + "_" + (-file.Id) : file.Id.ToString()) + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".part");
        CleanStaleParts(dir);
        string ext = file.Ext;
        job.Status = file.External != null ? "Asking " + file.External.HostLabel.ToLowerInvariant() + " ..." : "Downloading " + file.Name;
        using (var resp = file.External != null
            ? await ExternalDownloads.OpenAsync(_http, file.External, job.Cancel.Token).ConfigureAwait(false)
            : await _http.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, job.Cancel.Token).ConfigureAwait(false))
        {
            resp.EnsureSuccessStatusCode();
            string ctype = resp.Content.Headers.ContentType?.MediaType ?? "";
            if (file.External != null && file.External.Kind == ExternalDownloads.HostKind.Direct && ctype.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
                throw new ExternalDownloads.NeedsBrowserException("the link opened a web page, not a file");   // ModDownloader.isMediaTypeAllowed
            if (file.External != null)
            {
                string fn = ExternalDownloads.FileNameOf(resp);
                if (!string.IsNullOrEmpty(fn)) { file.Name = fn; ext = Path.GetExtension(fn).ToLowerInvariant(); }
                Console.WriteLine($"[GB] external {file.External.Kind}: {resp.RequestMessage?.RequestUri?.Host} -> {fn} ({resp.Content.Headers.ContentLength} bytes)");
            }
            long total = resp.Content.Headers.ContentLength ?? file.Size;
            using var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var dst = File.Create(tmp);
            var buf = new byte[131072]; long got = 0; int n;
            while ((n = await src.ReadAsync(buf, 0, buf.Length, job.Cancel.Token).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buf, 0, n, job.Cancel.Token).ConfigureAwait(false);
                got += n;
                job.Progress = total > 0 ? Math.Min(1f, got / (float)total) : -1f;
                job.Status = total > 0 ? $"Downloading  {got / 1048576.0:0.0} / {total / 1048576.0:0.0} MB" : $"Downloading  {got / 1048576.0:0.0} MB";
            }
        }
        job.Progress = -1f; job.Status = "Unpacking ...";
        string zip = tmp;
        try
        {
            var head = new byte[512]; int hn;
            using (var fs = global::FNF_MonoGame.Engine.FsCI.OpenRead(tmp)) hn = fs.Read(head, 0, head.Length);
            string sniff = ExternalDownloads.SniffExt(head, hn);
            if (sniff == null)
                throw new Exception(file.External != null ? "the link didn't give a ZIP / 7Z / RAR / TAR archive (" + (string.IsNullOrEmpty(ext) ? "unknown file" : ext) + ")" : "the download is not a ZIP / 7Z / RAR / TAR archive");
            ext = sniff;
            if (ext != ".zip") { zip = Path.Combine(dir, Path.GetFileNameWithoutExtension(tmp) + ".repack.zip"); Repack(tmp, zip, job); }
            string url = modUrl ?? "https://gamebanana.com/mods/" + mod.Id;
            string name;
            if (IsVSlice(zip, out string root)) { job.Layout = "V-Slice"; name = InstallVSlice(zip, root, mod, url); }
            else
            {
                job.Layout = "Psych";
                // OnlineMods.installMod: archive without a mod root folder -> gbMod._id (else the file name), so a Psych Online player who
                // downloads the same GameBanana page gets the same mods/<folder> = the room's modDir
                // Archives from GameBanana are offered to rooms as their direct file link (see PsoDirectUrl), which Psych Online installs
                // under FileUtils.formatFile(url) when the archive has no root folder - use the same name so both sides match.
                string fallback = !string.IsNullOrEmpty(wantModDir) ? wantModDir
                    : file.External == null && IsGbFileUrl(file.Url) ? PsychOnline.FormatFile(file.Url)
                    : mod.Id > 0 ? mod.Id.ToString() : PsychOnline.FormatFile(mod.Name);
                name = PsychOnline.InstallModArchive(zip, url, fallback, mod.Name);
                if (name != null && name.StartsWith("ERROR")) throw new Exception(name.Substring(6).Trim());
            }
            if (!string.IsNullOrEmpty(wantModDir) && !string.Equals(name, wantModDir, StringComparison.OrdinalIgnoreCase))
            {   // the room asks for mods/<modDir>: use that folder name (Psych Online would install it under the archive's name and fail)
                string from = Path.Combine(ModsBase, name), to = Path.Combine(ModsBase, wantModDir);
                if (!global::FNF_MonoGame.Engine.FsCI.DirectoryExists(to) && global::FNF_MonoGame.Engine.FsCI.DirectoryExists(from))
                {
                    Directory.Move(from, to);
                    try { PsychEngine.PsychMods.ParseList(); } catch { }
                    Console.WriteLine($"[GB] renamed installed mod '{name}' -> '{wantModDir}' (room modDir)");
                    name = wantModDir;
                }
            }
            if (file.External == null && IsGbFileUrl(file.Url))
                try { File.WriteAllText(Path.Combine(ModsBase, name, PsoUrlFile), file.Url.Trim()); } catch { }
            job.Status = "Enabling " + name + " ...";
            EnableInstalled(name);
            job.Status = "Installed " + name + " (" + job.Layout + ")";
            return name;
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
            if (zip != tmp) try { File.Delete(zip); } catch { }
        }
    }

    /// <summary>7z / RAR -> ZIP (SharpCompress, managed) so both installers can read it with System.IO.Compression.</summary>
    private static void Repack(string src, string dstZip, Job job)
    {
        if (ExternalDownloads.SniffExt(ReadHead(src), 512) is ".tgz" or ".tar") { RepackStream(src, dstZip, job); return; }
        using var archive = SharpCompress.Archives.ArchiveFactory.Open(src);
        var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
        long total = Math.Max(1, entries.Sum(e => e.Size)), done = 0;
        using var outZip = ZipFile.Open(dstZip, ZipArchiveMode.Create);
        using var reader = archive.ExtractAllEntries();   // solid 7z: sequential is far faster than random access
        while (reader.MoveToNextEntry())
        {
            job.Cancel.Token.ThrowIfCancellationRequested();
            var e = reader.Entry;
            if (e.IsDirectory || string.IsNullOrEmpty(e.Key)) continue;
            var ze = outZip.CreateEntry(e.Key.Replace('\\', '/'), CompressionLevel.NoCompression);
            using (var os = ze.Open()) reader.WriteEntryTo(os);
            done += e.Size;
            job.Progress = done / (float)total;
            job.Status = $"Unpacking {Path.GetExtension(src).TrimStart('.').ToUpperInvariant()}  {(int)(job.Progress * 100)}%";
        }
    }

    private static byte[] ReadHead(string path)
    {
        var b = new byte[512];
        using (var fs = global::FNF_MonoGame.Engine.FsCI.OpenRead(path)) fs.Read(b, 0, b.Length);
        return b;
    }

    /// <summary>TAR / TAR.GZ (Psych Online ModDownloader accepts x-tar / gzip): forward-only SharpCompress reader.</summary>
    private static void RepackStream(string src, string dstZip, Job job)
    {
        using var fs = global::FNF_MonoGame.Engine.FsCI.OpenRead(src);
        using var reader = SharpCompress.Readers.ReaderFactory.Open(fs);
        using var outZip = ZipFile.Open(dstZip, ZipArchiveMode.Create);
        long total = Math.Max(1, fs.Length);
        while (reader.MoveToNextEntry())
        {
            job.Cancel.Token.ThrowIfCancellationRequested();
            var e = reader.Entry;
            if (e.IsDirectory || string.IsNullOrEmpty(e.Key)) continue;
            var ze = outZip.CreateEntry(e.Key.Replace('\\', '/'), CompressionLevel.NoCompression);
            using (var os = ze.Open()) reader.WriteEntryTo(os);
            job.Progress = Math.Min(1f, fs.Position / (float)total);
            job.Status = $"Unpacking TAR  {(int)(job.Progress * 100)}%";
        }
    }

    /// <summary>V-Slice (Polymod) mod: has _polymod_meta.json; root = its folder inside the archive.</summary>
    private static bool IsVSlice(string zipPath, out string root)
    {
        root = null;
        using var z = ZipFile.OpenRead(zipPath);
        var meta = z.Entries.Select(e => e.FullName.Replace('\\', '/')).Where(n => n.EndsWith("_polymod_meta.json", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(n => n.Count(c => c == '/')).FirstOrDefault();
        if (meta == null) return false;
        root = meta.Substring(0, meta.Length - "_polymod_meta.json".Length);
        return true;
    }

    private static string InstallVSlice(string zipPath, string rootInZip, Mod mod, string url)
    {
        string name = PsychOnline.FormatFile(rootInZip.TrimEnd('/').Split('/').LastOrDefault());
        if (string.IsNullOrWhiteSpace(name)) name = PsychOnline.FormatFile(mod.Name);
        string dest = Path.Combine(ModsBase, name);
        if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(dest)) Directory.Delete(dest, true);
        Directory.CreateDirectory(dest);
        string full = Path.GetFullPath(dest);
        using (var z = ZipFile.OpenRead(zipPath))
            foreach (var e in z.Entries)
            {
                string fn = e.FullName.Replace('\\', '/');
                if (!fn.StartsWith(rootInZip) || fn.EndsWith("/")) continue;
                string to = Path.GetFullPath(Path.Combine(dest, fn.Substring(rootInZip.Length).Replace('/', Path.DirectorySeparatorChar)));
                if (!to.StartsWith(full, StringComparison.OrdinalIgnoreCase)) continue;   // zip-slip guard
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                e.ExtractToFile(to, true);
            }
        File.WriteAllText(Path.Combine(dest, "mod_url.txt"), url);
        return name;
    }

    private static void EnableInstalled(string folder)
    {
        try { PsychEngine.PsychMods.ParseList(); PsychEngine.PsychMods.SetModEnabled(folder, true); PsychEngine.PsychMods.SaveModsList(); } catch { }
        try
        {
            var mm = ModManager.Instance;
            mm.ScanMods();
            var m = mm.AllMods.FirstOrDefault(x => string.Equals(x.Id, folder, StringComparison.OrdinalIgnoreCase));
            if (m != null && !m.Enabled) mm.EnableMod(m);
        }
        catch { }
        PsychOnline.InvalidateSongs();
    }

    /// <summary>Folder names of installed mods (for the "INSTALLED" badge).</summary>
    public static HashSet<string> InstalledUrls()
    {
        var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!string.IsNullOrEmpty(ModsBase) && global::FNF_MonoGame.Engine.FsCI.DirectoryExists(ModsBase))
                foreach (var d in global::FNF_MonoGame.Engine.FsCI.GetDirectories(ModsBase))
                {
                    string f = Path.Combine(d, "mod_url.txt");
                    if (global::FNF_MonoGame.Engine.FsCI.FileExists(f)) s.Add(global::FNF_MonoGame.Engine.FsCI.ReadAllText(f).Trim().TrimEnd('/'));
                }
        }
        catch { }
        return s;
    }

    public static string Size(long b) => b >= 1 << 30 ? $"{b / 1073741824.0:0.0} GB" : b >= 1 << 20 ? $"{b / 1048576.0:0.0} MB" : $"{Math.Max(1, b / 1024)} KB";
    public static string Count(long n) => n >= 1_000_000 ? $"{n / 1_000_000.0:0.#}M" : n >= 1000 ? $"{n / 1000.0:0.#}K" : n.ToString();
}
