using System.IO.Compression;
using System.Net.Http;

namespace FNF_MonoGame;

/// <summary>
/// Downloads game Content from GitHub Releases on first launch.
/// Content is extracted to LocalState/Content/ and persisted across launches.
/// 
/// Setup:
/// 1. Run package_content.ps1 to create content.zip from your Content folder.
/// 2. Create a GitHub Release and upload content.zip as an asset.
/// 3. Update CONTENT_URL below with the download URL.
/// </summary>
public class ContentDownloader
{
    private const int MAX_DOWNLOAD_RETRIES = 3;
    private const int HResultTimeout = unchecked((int)0x80072EE2);
    // ===== CONFIGURE THESE =====
    // GitHub Releases URL for the content zip.
    // Format: https://github.com/{owner}/{repo}/releases/download/{tag}/{filename}
    // After uploading, right-click the asset → Copy link address.
    private const string CONTENT_URL = "https://github.com/dgamermohammad0909/friday-night-at-funkin-mono-engine/releases/download/alpha-1/content.zip";
    // Bump this when you upload new content — forces re-download.
    private const string CONTENT_VERSION = "alpha-1";
    // =============================

    private const string VERSION_FILE = "content_version.txt";

    /// <summary>Current status message for the UI.</summary>
    public string Status { get; private set; } = "Checking content...";

    /// <summary>Download/extraction progress from 0.0 to 1.0.</summary>
    public float Progress { get; private set; }

    /// <summary>True when content is ready and the game can proceed.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>True if an error occurred.</summary>
    public bool HasError { get; private set; }

    /// <summary>Error details if HasError is true.</summary>
    public string ErrorMessage { get; private set; }

    private readonly string _localStatePath;
    private readonly string _contentPath;

    public ContentDownloader()
    {
        _localStatePath = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
        _contentPath = Path.Combine(_localStatePath, "Content");
    }

    /// <summary>
    /// Check if content needs to be downloaded.
    /// Returns true if Content folder is missing or version doesn't match.
    /// </summary>
    /// <summary>Progress hook for <see cref="StoreContentInstaller"/>.</summary>
    internal void SetProgress(string status, float progress) { Status = status; Progress = progress; }

    private StoreContentInstaller _store;

    /// <summary>Store first start: the player pressed AGREE AND DOWNLOAD once (LocalState\store_agreement.txt), so a retry
    /// after a failed download or the next launch does not ask again.</summary>
    private static string AgreementFile => Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "store_agreement.txt");
    public static bool AgreementAccepted { get { try { return File.Exists(AgreementFile); } catch { return false; } } }
    public static void AcceptAgreement()
    {
        try { File.WriteAllText(AgreementFile, "agreed " + DateTime.UtcNow.ToString("o") + "\n"); }
        catch (Exception e) { Console.WriteLine("[STORE] agreement marker: " + e.Message); }
        Console.WriteLine("[STORE] download agreement accepted");
    }

    public bool NeedsDownload()
    {
        // Public Store build: FNF game data is installed on first start from the official GitHub.
        if (StoreContentInstaller.IsStoreBuild)
        {
            _store ??= new StoreContentInstaller(this, _localStatePath);
            bool need = !_store.Installed();
            Console.WriteLine($"[STORE] store build {_store.Version}: game data {(need ? "missing -> install" : "installed")}");
            return need;
        }
        // A content.zip / mods.zip uploaded into LocalState (Device Portal > File explorer) is imported first.
        if (File.Exists(LocalContentZip) || File.Exists(LocalModsZip)) return true;
        string versionFile = Path.Combine(_localStatePath, VERSION_FILE);
        // Content uploaded by hand (Device Portal / upload_content_to_xbox.ps1) has no version file:
        // never overwrite it with a download. Same for a package that bundles Content itself.
        if (!File.Exists(versionFile))
        {
            if (Directory.Exists(Path.Combine(_contentPath, "data"))) return false;
            string installed = Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, "Content", "data");
            if (Directory.Exists(installed)) return false;
            return true;
        }

        try
        {
            string currentVersion = File.ReadAllText(versionFile).Trim();
            if (currentVersion != CONTENT_VERSION) return true;
        }
        catch
        {
            return true;
        }

        // Version matches — but does the Content folder actually exist?
        return !Directory.Exists(_contentPath);
    }

    private string LocalContentZip => Path.Combine(_localStatePath, "content.zip");
    private string LocalModsZip => Path.Combine(_localStatePath, "mods.zip");

    /// <summary>Extract a zip uploaded by hand into LocalState (merge: existing files are overwritten, nothing deleted).</summary>
    private async Task ImportZipAsync(string zip, string destRoot, string stripPrefix, string label)
    {
        Directory.CreateDirectory(destRoot);
        using (var fileStream = new FileStream(zip, FileMode.Open, FileAccess.Read))
        using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Read))
        {
            int total = Math.Max(1, archive.Entries.Count), done = 0;
            foreach (var entry in archive.Entries)
            {
                done++;
                if (string.IsNullOrEmpty(entry.Name)) continue;
                string entryPath = entry.FullName.Replace('\\', '/');
                if (stripPrefix != null && entryPath.StartsWith(stripPrefix, StringComparison.OrdinalIgnoreCase))
                    entryPath = entryPath.Substring(stripPrefix.Length);
                if (entryPath.Contains("..")) continue;
                string destPath = Path.Combine(destRoot, entryPath.Replace('/', Path.DirectorySeparatorChar));
                string destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
                using (var entryStream = entry.Open())
                using (var destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write))
                    await entryStream.CopyToAsync(destStream);
                Progress = (float)done / total;
                if (done % 50 == 0) Status = $"Importing {label}... {done}/{total} files";
            }
        }
        try { File.Delete(zip); } catch { }
    }

    /// <summary>
    /// Download content zip from GitHub and extract to LocalState/Content/.
    /// Call from a background thread via Task.Run().
    /// </summary>
    public async Task DownloadContentAsync()
    {
        string zipPath = Path.Combine(_localStatePath, "content_download.zip");

        if (_store != null)
        {
            HasError = false; ErrorMessage = null; IsComplete = false;
            // keep downloading when the window is minimized / the player goes to Home (otherwise the app is suspended)
            var keepRunning = new Windows.ApplicationModel.ExtendedExecution.ExtendedExecutionSession
            {
                Reason = Windows.ApplicationModel.ExtendedExecution.ExtendedExecutionReason.Unspecified,
                Description = "Downloading game data"
            };
            keepRunning.Revoked += (_, e) => Console.WriteLine("[STORE] background download revoked: " + e.Reason);
            try { Console.WriteLine("[STORE] background download: " + await keepRunning.RequestExtensionAsync()); }
            catch (Exception ex) { Console.WriteLine("[STORE] background download request failed: " + ex.Message); }
            try { await _store.InstallAsync(); IsComplete = true; }
            catch (Exception ex)
            {
                Console.WriteLine("[STORE] install failed: " + ex);
                SetError((ex is HttpRequestException || ex is TaskCanceledException || ex is IOException
                    ? "Could not download the game data.\nCheck your internet connection.\n\n" : "Installing the game data failed.\n\n")
                    + ex.GetType().Name + ": " + ex.Message);
            }
            finally { keepRunning.Dispose(); }
            return;
        }
        try
        {
            // Hand-uploaded zips (LocalState\content.zip -> Content, LocalState\mods.zip -> mods) need no internet.
            bool imported = false;
            if (File.Exists(LocalContentZip))
            {
                Status = "Importing content.zip...";
                await ImportZipAsync(LocalContentZip, _contentPath, "Content/", "content");
                try { File.Delete(Path.Combine(_localStatePath, VERSION_FILE)); } catch { } // hand content: never auto-replace
                imported = true;
            }
            if (File.Exists(LocalModsZip))
            {
                Status = "Importing mods.zip...";
                await ImportZipAsync(LocalModsZip, Path.Combine(_localStatePath, "mods"), "mods/", "mods");
                imported = true;
            }
            if (imported && !NeedsDownload())
            {
                Status = "Content ready!";
                Progress = 1f;
                IsComplete = true;
                return;
            }
            // Phase 1: Download the zip (0% → 80%)
            Status = "Connecting to GitHub...";
            Progress = 0f;

            for (int attempt = 1; attempt <= MAX_DOWNLOAD_RETRIES; attempt++)
            {
                try
                {
                    using (var client = new HttpClient())
                    {
                        client.DefaultRequestHeaders.Add("User-Agent", "FNF-MonoGame-Xbox");
                        client.Timeout = TimeSpan.FromHours(2);

                        using var response = await client.GetAsync(CONTENT_URL, HttpCompletionOption.ResponseHeadersRead);

                        if (!response.IsSuccessStatusCode)
                        {
                            SetError($"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}\nURL: {CONTENT_URL}");
                            return;
                        }

                        long? totalBytes = response.Content.Headers.ContentLength;

                        using var stream = await response.Content.ReadAsStreamAsync();
                        using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

                        byte[] buffer = new byte[81920];
                        long downloaded = 0;
                        int bytesRead;

                        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            downloaded += bytesRead;

                            if (totalBytes.HasValue && totalBytes.Value > 0)
                            {
                                Progress = (float)downloaded / totalBytes.Value * 0.8f;
                                float mb = downloaded / (1024f * 1024f);
                                float totalMb = totalBytes.Value / (1024f * 1024f);
                                Status = $"Downloading... {mb:F1} / {totalMb:F1} MB";
                            }
                            else
                            {
                                float mb = downloaded / (1024f * 1024f);
                                Status = $"Downloading... {mb:F1} MB";
                            }
                        }
                    }

                    break;
                }
                catch (Exception ex) when (IsTimeout(ex) && attempt < MAX_DOWNLOAD_RETRIES)
                {
                    Status = $"Download timed out. Retrying {attempt}/{MAX_DOWNLOAD_RETRIES}...";
                    CleanupZip(zipPath);
                    await Task.Delay(1000);
                }
            }

            // Phase 2: Extract the zip (80% → 100%)
            Status = "Extracting content...";
            Progress = 0.8f;

            // Remove old content if present
            if (Directory.Exists(_contentPath))
            {
                try { Directory.Delete(_contentPath, true); }
                catch { /* best effort */ }
            }
            Directory.CreateDirectory(_contentPath);

            using (var fileStream = new FileStream(zipPath, FileMode.Open, FileAccess.Read))
            using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Read))
            {
                int totalEntries = archive.Entries.Count;
                int extracted = 0;

                foreach (var entry in archive.Entries)
                {
                    // Skip directory entries (they have empty Name but non-empty FullName ending with /)
                    if (string.IsNullOrEmpty(entry.Name))
                        continue;

                    // Strip leading "Content/" if the zip was created with the folder included
                    string entryPath = entry.FullName.Replace('\\', '/');
                    if (entryPath.StartsWith("Content/", StringComparison.OrdinalIgnoreCase))
                        entryPath = entryPath.Substring(8);

                    string destPath = Path.Combine(_contentPath, entryPath.Replace('/', Path.DirectorySeparatorChar));
                    string destDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                        Directory.CreateDirectory(destDir);

                    using (var entryStream = entry.Open())
                    using (var destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write))
                    {
                        await entryStream.CopyToAsync(destStream);
                    }

                    extracted++;
                    Progress = 0.8f + (float)extracted / totalEntries * 0.2f;
                    if (extracted % 100 == 0)
                        Status = $"Extracting... {extracted}/{totalEntries} files";
                }

                Status = $"Extracted {extracted} files";
            }

            // Clean up the zip
            try { File.Delete(zipPath); }
            catch { /* ignore */ }

            // Write version marker so we don't re-download next launch
            string versionFile = Path.Combine(_localStatePath, VERSION_FILE);
            File.WriteAllText(versionFile, CONTENT_VERSION);

            Status = "Content ready!";
            Progress = 1f;
            IsComplete = true;
        }
        catch (HttpRequestException ex)
        {
            SetError($"Network error: {ex.Message}\n\nMake sure Xbox is connected to the internet\nand the download URL is correct.");
            CleanupZip(zipPath);
        }
        catch (TaskCanceledException)
        {
            SetError("Download timed out.\nCheck your internet connection and try again.");
            CleanupZip(zipPath);
        }
        catch (Exception ex)
        {
            SetError($"{ex.GetType().Name}: {ex.Message}\nHResult: 0x{ex.HResult:X8}");
            CleanupZip(zipPath);
        }
    }

    private static bool IsTimeout(Exception ex)
    {
        if (ex is TaskCanceledException)
            return true;

        if (ex is IOException && ex.HResult == HResultTimeout)
            return true;

        return ex.HResult == HResultTimeout;
    }

    private void SetError(string message)
    {
        HasError = true;
        ErrorMessage = message;
        Status = "Download failed!";
    }

    private static void CleanupZip(string zipPath)
    {
        try { if (File.Exists(zipPath)) File.Delete(zipPath); }
        catch { /* ignore */ }
    }
}
