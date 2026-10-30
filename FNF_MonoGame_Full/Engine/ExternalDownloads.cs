using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace FNF_MonoGame.Engine;

/// <summary>
/// External download hosts for GameBanana mods whose files live elsewhere (_aAlternateFileSources / links in the
/// description). Resolves a share link to the real file stream:
///   Google Drive  - uc/usercontent download; the "can't scan for viruses" page is a form (id, export, confirm, uuid) -> follow it.
///   MediaFire     - file page -> #downloadButton href (or base64 data-scrambled-url) -> downloadNN.mediafire.com.
///   Dropbox       - same link with dl=1.
///   GitHub        - repo / releases / releases/tag/X -> API release assets (zip/7z/rar); none -> source zip (zipball / codeload).
///   Discord CDN   - direct (signed links expire -> "open in browser").
///   Pixeldrain    - /u/ID -> /api/file/ID?download.
///   anything else - fetched directly; an HTML answer means the host needs a browser (login / captcha / JS).
/// Browser-only hosts (MEGA, YouTube, GameJolt, itch, Discord invites, social pages, MediaFire folders ...) are marked
/// NeedsBrowser so the UI shows "open in browser" (PC) / URL + QR code (Xbox, Android).
/// Self-contained (HttpClient + Newtonsoft) so it can be unit-tested outside the game (scratch/exttest).
/// </summary>
public static class ExternalDownloads
{
    public enum HostKind { Direct, GoogleDrive, MediaFire, Dropbox, GitHub, Discord, Pixeldrain, Browser }

    public sealed class Source
    {
        public string Url, Description, Name, Why;
        public HostKind Kind;
        public long Size;
        public bool NeedsBrowser => Kind == HostKind.Browser;
        public string HostLabel => Kind switch
        {
            HostKind.GoogleDrive => "GOOGLE DRIVE", HostKind.MediaFire => "MEDIAFIRE", HostKind.Dropbox => "DROPBOX", HostKind.GitHub => "GITHUB",
            HostKind.Discord => "DISCORD", HostKind.Pixeldrain => "PIXELDRAIN", HostKind.Direct => "LINK", _ => HostOf(Url).ToUpperInvariant(),
        };
    }

    /// <summary>The host wants a real browser (login, captcha, JS page, expired link...).</summary>
    public sealed class NeedsBrowserException : Exception { public NeedsBrowserException(string m) : base(m) { } }

    public static string HostOf(string url)
    {
        try { var h = new Uri(url).Host.ToLowerInvariant(); return h.StartsWith("www.") ? h.Substring(4) : h; } catch { return url ?? ""; }
    }

    private static readonly string[] BrowserHosts =
    {
        "mega.nz", "mega.io", "youtube.com", "youtu.be", "m.youtube.com", "gamejolt.com", "itch.io", "discord.gg", "discord.com", "twitter.com",
        "x.com", "ko-fi.com", "patreon.com", "carrd.co", "fandom.com", "qiwi.gg", "funkinbackstage.com", "netlify.app", "tiktok.com",
        "instagram.com", "twitch.tv", "reddit.com", "gamebanana.com", "bsky.app", "linktr.ee", "1drv.ms", "onedrive.live.com", "box.com",
    };

    public static HostKind Classify(string url)
    {
        string h = HostOf(url);
        string path = "";
        try { path = new Uri(url).AbsolutePath; } catch { return HostKind.Browser; }
        if (h == "drive.google.com" || h == "drive.usercontent.google.com" || h == "docs.google.com")
            return GDriveId(url) != null && !path.Contains("/folders/") ? HostKind.GoogleDrive : HostKind.Browser;
        if (h == "mediafire.com" || h.EndsWith(".mediafire.com"))
            return path.StartsWith("/file") || path.StartsWith("/download") || Regex.IsMatch(h, @"^download\d*\.mediafire\.com$") || url.Contains("/?") ? HostKind.MediaFire : HostKind.Browser;
        if (h == "dropbox.com" || h == "dl.dropboxusercontent.com" || h == "dl.dropbox.com") return HostKind.Dropbox;
        if (h == "github.com") return GitHubRepo(url, out _, out _, out _) ? HostKind.GitHub : HostKind.Browser;
        if (h == "objects.githubusercontent.com" || h == "codeload.github.com" || h == "raw.githubusercontent.com") return HostKind.Direct;
        if (h == "cdn.discordapp.com" || h == "media.discordapp.net") return HostKind.Discord;
        if (h == "pixeldrain.com" && (path.StartsWith("/u/") || path.StartsWith("/api/file/"))) return HostKind.Pixeldrain;
        foreach (var b in BrowserHosts) if (h == b || h.EndsWith("." + b)) return HostKind.Browser;
        // generic: only links that look like an archive are tried directly
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext is ".zip" or ".7z" or ".rar" ? HostKind.Direct : HostKind.Browser;
    }

    /// <summary>
    /// Psych Online room / skin mod URL (OnlineMods.downloadMod): Google Drive file and MediaFire file links are scraped, every other
    /// http(s) link is requested directly (ModDownloader, archive content types) - so a generic host without an archive extension is
    /// still tried; only hosts that can never give a file (MEGA, YouTube, folders, ...) go to the browser.
    /// </summary>
    public static Source MakeForRoom(string url)
    {
        var s = Make(url, null);
        if (s.Kind == HostKind.Browser)
        {
            string h = HostOf(s.Url);
            bool never = h.Contains("mega.") || h.Contains("youtu") || h == "discord.gg" || s.Url.Contains("/folders/") || s.Url.Contains("/folder/")
                         || (h == "gamebanana.com" && !s.Url.Contains("/dl/"));
            if (!never) { s.Kind = HostKind.Direct; s.Why = null; s.Name ??= GuessName(s); }
        }
        return s;
    }

    public static Source Make(string url, string desc)
    {
        url = (url ?? "").Trim();
        var s = new Source { Url = url, Description = desc, Kind = Classify(url) };
        s.Name = GuessName(s);
        if (s.NeedsBrowser) s.Why = HostOf(url) + " needs a web browser (login / captcha / not a file link)";
        return s;
    }

    private static string GuessName(Source s)
    {
        try
        {
            var segs = new Uri(s.Url).AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
            if (s.Kind == HostKind.MediaFire && segs.Length >= 3 && segs[0] == "file") return segs[2].Replace('+', ' ');
            var last = segs.LastOrDefault(x => x.Contains('.'));
            if (s.Kind is HostKind.Dropbox or HostKind.Discord or HostKind.Direct && last != null) return last;
        }
        catch { }
        return null;
    }

    /// <summary>http(s) links in a GameBanana description (HTML) that point at a downloadable host.</summary>
    public static List<string> LinksInText(string html)
    {
        var l = new List<string>();
        if (string.IsNullOrEmpty(html)) return l;
        foreach (Match m in Regex.Matches(html, "https?://[^\\s\"'<>()\\[\\]]+", RegexOptions.IgnoreCase))
        {
            string u = System.Net.WebUtility.HtmlDecode(m.Value).TrimEnd('.', ',', ';', '!');
            var k = Classify(u);
            if (k is HostKind.GoogleDrive or HostKind.MediaFire or HostKind.Dropbox or HostKind.Discord or HostKind.Pixeldrain or HostKind.Direct
                || (k == HostKind.GitHub && u.Contains("/releases")))
                if (!l.Contains(u)) l.Add(u);
        }
        return l;
    }

    // ------------------------------------------------------------------ GitHub (expanded when the file list loads)
    public static bool GitHubRepo(string url, out string owner, out string repo, out string tag)
    {
        owner = repo = tag = null;
        var m = Regex.Match(url ?? "", @"^https?://(?:www\.)?github\.com/([^/\s]+)/([^/\s#?]+)(/.*)?$", RegexOptions.IgnoreCase);
        if (!m.Success) return false;
        owner = m.Groups[1].Value; repo = m.Groups[2].Value;
        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) repo = repo.Substring(0, repo.Length - 4);
        string rest = m.Groups[3].Value;
        var t = Regex.Match(rest, @"^/releases/(?:tag|download)/([^/?#]+)");
        if (t.Success) tag = Uri.UnescapeDataString(t.Groups[1].Value);
        return !string.IsNullOrEmpty(owner) && !string.IsNullOrEmpty(repo) && owner != "orgs" && owner != "sponsors";
    }

    /// <summary>One Source per release asset (zip/7z/rar), or the source zip when the release/repo has none.</summary>
    public static async Task<List<Source>> ExpandGitHubAsync(HttpClient http, Source s, CancellationToken ct = default)
    {
        var res = new List<Source>();
        if (!GitHubRepo(s.Url, out var owner, out var repo, out var tag)) return new List<Source> { s };
        string api = $"https://api.github.com/repos/{owner}/{repo}/releases/" + (tag != null ? "tags/" + Uri.EscapeDataString(tag) : "latest");
        JObject rel = null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, api);
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var r = await http.SendAsync(req, ct).ConfigureAwait(false);
            if (r.IsSuccessStatusCode) rel = JObject.Parse(await r.Content.ReadAsStringAsync().ConfigureAwait(false));
        }
        catch (OperationCanceledException) { throw; }
        catch { }
        string relName = (string)rel?["tag_name"];
        foreach (var a in (rel?["assets"] as JArray ?? new JArray()).OfType<JObject>())
        {
            string n = (string)a["name"] ?? "";
            string ext = System.IO.Path.GetExtension(n).ToLowerInvariant();
            if (ext is not (".zip" or ".7z" or ".rar")) continue;
            res.Add(new Source { Url = (string)a["browser_download_url"], Name = n, Size = (long?)a["size"] ?? 0, Kind = HostKind.Direct,
                                 Description = $"GitHub {owner}/{repo} release {relName}" + (string.IsNullOrWhiteSpace(s.Description) ? "" : " - " + s.Description) });
        }
        if (res.Count == 0)
        {   // no archive assets: the repo itself (mod folder repos are common for V-Slice mods)
            string zip = rel != null ? (string)rel["zipball_url"] : $"https://codeload.github.com/{owner}/{repo}/zip/HEAD";
            res.Add(new Source { Url = zip, Name = repo + (relName != null ? "-" + relName : "") + ".zip", Kind = HostKind.Direct,
                                 Description = $"GitHub {owner}/{repo} source code" + (relName != null ? " (release " + relName + ")" : " (latest)") });
        }
        return res;
    }

    // ------------------------------------------------------------------ resolve -> file response
    /// <summary>Response whose body is the file (caller disposes). Throws NeedsBrowserException when only a browser can get it.</summary>
    public static async Task<HttpResponseMessage> OpenAsync(HttpClient http, Source s, CancellationToken ct = default)
    {
        switch (s.Kind)
        {
            case HostKind.Browser: throw new NeedsBrowserException(s.Why ?? "this host needs a web browser");
            case HostKind.GoogleDrive: return await OpenGoogleDrive(http, s.Url, ct).ConfigureAwait(false);
            case HostKind.MediaFire: return await OpenMediaFire(http, s.Url, ct).ConfigureAwait(false);
            case HostKind.Dropbox: return await OpenFile(http, DropboxDirect(s.Url), "Dropbox", ct).ConfigureAwait(false);
            case HostKind.Pixeldrain: return await OpenFile(http, PixeldrainDirect(s.Url), "Pixeldrain", ct).ConfigureAwait(false);
            case HostKind.GitHub:
                var l = await ExpandGitHubAsync(http, s, ct).ConfigureAwait(false);
                return await OpenFile(http, l[0].Url, "GitHub", ct).ConfigureAwait(false);
            case HostKind.Discord: return await OpenFile(http, s.Url, "Discord (the link may have expired)", ct).ConfigureAwait(false);
            default: return await OpenFile(http, s.Url, HostOf(s.Url), ct).ConfigureAwait(false);
        }
    }

    public static string DropboxDirect(string url)
    {
        string u = Regex.Replace(url, @"([?&])dl=0", "$1dl=1");
        if (!Regex.IsMatch(u, @"[?&]dl=1")) u += (u.Contains('?') ? "&" : "?") + "dl=1";
        return u;
    }

    public static string PixeldrainDirect(string url)
    {
        var m = Regex.Match(url, @"pixeldrain\.com/(?:u|api/file)/([A-Za-z0-9]+)");
        return m.Success ? "https://pixeldrain.com/api/file/" + m.Groups[1].Value + "?download" : url;
    }

    public static string GDriveId(string url)
    {
        var m = Regex.Match(url ?? "", @"/(?:file/d|uc/d|d)/([A-Za-z0-9_-]{10,})");
        if (m.Success) return m.Groups[1].Value;
        m = Regex.Match(url ?? "", @"[?&]id=([A-Za-z0-9_-]{10,})");
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>GET; an HTML answer (instead of a file) is turned into NeedsBrowserException.</summary>
    private static async Task<HttpResponseMessage> OpenFile(HttpClient http, string url, string what, CancellationToken ct)
    {
        var r = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!r.IsSuccessStatusCode)
        {
            int code = (int)r.StatusCode; r.Dispose();
            if (code == 401 || code == 403 || code == 404 || code == 410) throw new NeedsBrowserException(what + " answered HTTP " + code + " (private, removed or expired link)");
            throw new HttpRequestException(what + " answered HTTP " + code);
        }
        if (IsHtml(r)) { r.Dispose(); throw new NeedsBrowserException(what + " sent a web page instead of the file"); }
        return r;
    }

    private static bool IsHtml(HttpResponseMessage r) =>
        (r.Content.Headers.ContentType?.MediaType ?? "").IndexOf("html", StringComparison.OrdinalIgnoreCase) >= 0;

    private static async Task<HttpResponseMessage> OpenGoogleDrive(HttpClient http, string url, CancellationToken ct)
    {
        string id = GDriveId(url) ?? throw new NeedsBrowserException("not a Google Drive file link");
        string first = "https://drive.usercontent.google.com/download?id=" + id + "&export=download";
        var r = await http.GetAsync(first, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (r.IsSuccessStatusCode && !IsHtml(r)) return r;
        string html = r.IsSuccessStatusCode ? await r.Content.ReadAsStringAsync().ConfigureAwait(false) : "";
        int code = (int)r.StatusCode; r.Dispose();
        // big files: "Google Drive can't scan this file for viruses" -> <form id="download-form" action=...> with hidden inputs
        var form = Regex.Match(html, "<form[^>]*id=\"download-form\"[^>]*action=\"([^\"]+)\"[^>]*>(.*?)</form>", RegexOptions.Singleline);
        if (form.Success)
        {
            var q = new List<string>();
            foreach (Match inp in Regex.Matches(form.Groups[2].Value, "<input[^>]*type=\"hidden\"[^>]*>"))
            {
                var n = Regex.Match(inp.Value, "name=\"([^\"]+)\""); var v = Regex.Match(inp.Value, "value=\"([^\"]*)\"");
                if (n.Success) q.Add(Uri.EscapeDataString(n.Groups[1].Value) + "=" + Uri.EscapeDataString(System.Net.WebUtility.HtmlDecode(v.Success ? v.Groups[1].Value : "")));
            }
            string action = System.Net.WebUtility.HtmlDecode(form.Groups[1].Value);
            return await OpenFile(http, action + (action.Contains('?') ? "&" : "?") + string.Join("&", q), "Google Drive", ct).ConfigureAwait(false);
        }
        var conf = Regex.Match(html, "confirm=([0-9A-Za-z_-]+)");   // old uc page
        if (conf.Success) return await OpenFile(http, first + "&confirm=" + conf.Groups[1].Value, "Google Drive", ct).ConfigureAwait(false);
        if (html.IndexOf("quota", StringComparison.OrdinalIgnoreCase) >= 0) throw new NeedsBrowserException("Google Drive: download quota exceeded for this file, try later in a browser");
        throw new NeedsBrowserException("Google Drive " + (code == 200 ? "wants a sign-in / the file is private" : "answered HTTP " + code));
    }

    private static async Task<HttpResponseMessage> OpenMediaFire(HttpClient http, string url, CancellationToken ct)
    {
        if (Regex.IsMatch(HostOf(url), @"^download\d*\.mediafire\.com$")) return await OpenFile(http, url, "MediaFire", ct).ConfigureAwait(false);
        string html;
        using (var r = await http.GetAsync(url, ct).ConfigureAwait(false))
        {
            if (!r.IsSuccessStatusCode) throw new NeedsBrowserException("MediaFire answered HTTP " + (int)r.StatusCode);
            if (!IsHtml(r))
                throw new NeedsBrowserException("MediaFire sent an unexpected answer");
            html = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        string dl = null;
        var sc = Regex.Match(html, "data-scrambled-url=\"([^\"]+)\"");
        if (sc.Success) try { dl = Encoding.UTF8.GetString(Convert.FromBase64String(sc.Groups[1].Value)); } catch { }
        if (dl == null)
        {
            var m = Regex.Match(html, "href=\"(https?://download\\d*\\.mediafire\\.com/[^\"]+)\"");
            if (m.Success) dl = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value);
        }
        if (dl == null) throw new NeedsBrowserException("MediaFire didn't give a download link (captcha / removed file)");
        return await OpenFile(http, dl, "MediaFire", ct).ConfigureAwait(false);
    }

    /// <summary>File name from Content-Disposition (filename* / filename) or the final URL.</summary>
    public static string FileNameOf(HttpResponseMessage r)
    {
        var cd = r.Content.Headers.ContentDisposition;
        string n = cd?.FileNameStar ?? cd?.FileName;
        if (string.IsNullOrEmpty(n) && r.Content.Headers.TryGetValues("Content-Disposition", out var raw))
        {
            var m = Regex.Match(string.Join(";", raw), "filename\\*?=(?:UTF-8'')?\"?([^\";]+)");
            if (m.Success) n = Uri.UnescapeDataString(m.Groups[1].Value);
        }
        if (string.IsNullOrEmpty(n)) try { n = Uri.UnescapeDataString(r.RequestMessage.RequestUri.AbsolutePath.Split('/').Last()); } catch { }
        return n?.Trim('"', ' ');
    }

    /// <summary>Archive type from the first bytes: ".zip" / ".7z" / ".rar" or null.</summary>
    public static string SniffExt(byte[] h, int n)
    {
        if (n >= 4 && h[0] == 'P' && h[1] == 'K' && (h[2] == 3 || h[2] == 5 || h[2] == 7)) return ".zip";
        if (n >= 6 && h[0] == '7' && h[1] == 'z' && h[2] == 0xBC && h[3] == 0xAF && h[4] == 0x27 && h[5] == 0x1C) return ".7z";
        if (n >= 4 && h[0] == 'R' && h[1] == 'a' && h[2] == 'r' && h[3] == '!') return ".rar";
        if (n >= 3 && h[0] == 0x1F && h[1] == 0x8B && h[2] == 8) return ".tgz";   // gzip (Psych Online accepts TAR / TGZ too)
        if (n >= 262 && h[257] == 'u' && h[258] == 's' && h[259] == 't' && h[260] == 'a' && h[261] == 'r') return ".tar";
        return null;
    }

    // ------------------------------------------------------------------ open in browser
    /// <summary>PC: opens the system browser and returns true. Xbox / Android / no shell: false (show the URL + QR code).</summary>
    public static bool TryOpenBrowser(string url)
    {
#if XBOX_UWP || ANDROID
        return false;
#else
        if (Environment.GetEnvironmentVariable("FNF_NO_BROWSER") == "1") { Console.WriteLine("[EXT] (dev) would open browser: " + url); return true; }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); return true; }
        catch (Exception e) { Console.WriteLine("[EXT] browser failed: " + e.Message); return false; }
#endif
    }
}
