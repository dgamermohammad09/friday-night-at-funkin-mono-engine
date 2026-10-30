#if ANDROID
using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using Microsoft.Xna.Framework;

namespace FNF_MonoGame.Mobile;

/// <summary>Android entry point. Game data folder = the app's external files dir
/// (/sdcard/Android/data/com.fnfmonogame.engine/files): Content/, mods/, saves and game_log.txt live there.
/// The ~1.1 GB Content is not inside the APK (too big for a sane APK / Play limits): copy it there once
/// (adb push / USB file transfer) or drop a Content.zip there and the app unpacks it on first start.</summary>
[Activity(Label = "FNF MonoGame", MainLauncher = true, AlwaysRetainTaskState = true, LaunchMode = LaunchMode.SingleInstance,
    ScreenOrientation = ScreenOrientation.SensorLandscape, Theme = "@android:style/Theme.NoTitleBar.Fullscreen",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.UiMode)]
public class MainActivity : AndroidGameActivity
{
    private FNFGame _game;
    private TextView _status;
    public static string DataDir { get; private set; }

    protected override void OnCreate(Bundle bundle)
    {
        base.OnCreate(bundle);
        DataDir = (GetExternalFilesDir(null) ?? FilesDir).AbsolutePath;
        Directory.CreateDirectory(DataDir);
        RedirectLog();
        Console.WriteLine($"[ANDROID] data dir {DataDir}, device {Build.Manufacturer} {Build.Model}, API {(int)Build.VERSION.SdkInt}");

        // Every BaseDirectory-relative path of the engine (Content/, mods/, save_data.json, logs) -> the data dir.
        AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", DataDir.TrimEnd('/') + "/");
        Engine.AssetManager.GameRootOverride = DataDir;
        Engine.AssetManager.ContentRootOverride = Path.Combine(DataDir, "Content");
        Engine.AssetManager.GlShaderDir = ExtractShaders();

        if (ContentOk()) { StartGame(); return; }
        string zip = Path.Combine(DataDir, "Content.zip");
        if (global::FNF_MonoGame.Engine.FsCI.FileExists(zip)) { ShowStatus("Unpacking Content.zip ... (first start only)"); Task.Run(() => Unpack(zip)); return; }
        ShowMissing();
    }

    private const int PickZipRequest = 4711;

    /// <summary>Content counts as installed only after a complete unpack (marker) or a manual copy with the main folders.</summary>
    private static bool ContentOk()
    {
        string c = Path.Combine(DataDir, "Content");
        if (global::FNF_MonoGame.Engine.FsCI.FileExists(Path.Combine(c, ".unpacked_ok"))) return true;
        return global::FNF_MonoGame.Engine.FsCI.DirectoryExists(Path.Combine(c, "data", "characters")) && global::FNF_MonoGame.Engine.FsCI.DirectoryExists(Path.Combine(c, "images"))
            && global::FNF_MonoGame.Engine.FsCI.DirectoryExists(Path.Combine(c, "fonts")) && !global::FNF_MonoGame.Engine.FsCI.FileExists(Path.Combine(c, ".unpacking"));
    }

    /// <summary>Android 11+ file managers can't write into Android/data, so let the user pick Content.zip
    /// (Downloads, SD card, ...) with the system file picker and copy it in ourselves.</summary>
    private void ShowMissing()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetGravity(GravityFlags.Center);
        root.SetBackgroundColor(global::Android.Graphics.Color.Black);
        root.SetPadding(48, 48, 48, 48);
        _status = new TextView(this)
        {
            Text = "Game files needed (first start only).\n\nDownload Content.zip, then tap the button and choose it.\n" +
                   "The game copies and unpacks it (about 1.3 GB, a few minutes).",
            TextSize = 18f, Gravity = GravityFlags.Center
        };
        _status.SetTextColor(global::Android.Graphics.Color.White);
        var btn = new Button(this) { Text = "Choose Content.zip", TextSize = 22f };
        btn.SetPadding(64, 32, 64, 32);
        btn.Click += (_, _) =>
        {
            var it = new global::Android.Content.Intent(global::Android.Content.Intent.ActionOpenDocument);
            it.AddCategory(global::Android.Content.Intent.CategoryOpenable);
            it.SetType("*/*");
            it.PutExtra(global::Android.Content.Intent.ExtraMimeTypes, new[] { "application/zip", "application/x-zip-compressed", "application/octet-stream" });
            StartActivityForResult(it, PickZipRequest);
        };
        var lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { TopMargin = 48, Gravity = GravityFlags.Center };
        root.AddView(_status);
        root.AddView(btn, lp);
        var hint = new TextView(this) { Text = "Or copy the Content folder to:\n" + DataDir, TextSize = 13f, Gravity = GravityFlags.Center };
        hint.SetTextColor(global::Android.Graphics.Color.Gray);
        var lp2 = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { TopMargin = 48, Gravity = GravityFlags.Center };
        root.AddView(hint, lp2);
        SetContentView(root);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, global::Android.Content.Intent data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != PickZipRequest || resultCode != Result.Ok || data?.Data == null) return;
        var uri = data.Data;
        ShowStatus("Copying Content.zip ...");
        Task.Run(() =>
        {
            string zip = Path.Combine(DataDir, "Content.zip");
            try
            {
                long total = 0;
                try { using var fd = ContentResolver.OpenFileDescriptor(uri, "r"); total = fd?.StatSize ?? 0; } catch { }
                using (var src = ContentResolver.OpenInputStream(uri))
                using (var dst = File.Create(zip))
                {
                    var buf = new byte[1 << 20]; long done = 0; int r, tick = 0;
                    while ((r = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        dst.Write(buf, 0, r); done += r;
                        if (++tick % 16 == 0) { long d = done; RunOnUiThread(() => _status.Text = total > 0 ? $"Copying Content.zip ... {d * 100 / total}%" : $"Copying Content.zip ... {d >> 20} MB"); }
                    }
                }
                RunOnUiThread(() => _status.Text = "Unpacking Content.zip ...");
                Unpack(zip);
            }
            catch (Exception ex)
            {
                try { File.Delete(zip); } catch { }
                RunOnUiThread(() => { ShowMissing(); _status.Text = "Copy failed: " + ex.Message + "\n\nTry again."; });
            }
        });
    }

    private void Unpack(string zip)
    {
        try
        {
            using (var z = ZipFile.OpenRead(zip))
            {
                int n = 0, total = z.Entries.Count;
                bool rooted = total > 0 && z.Entries[0].FullName.StartsWith("Content/", StringComparison.OrdinalIgnoreCase);
                string dest = rooted ? DataDir : Path.Combine(DataDir, "Content");
                Directory.CreateDirectory(Path.Combine(DataDir, "Content"));
                File.WriteAllText(Path.Combine(DataDir, "Content", ".unpacking"), "1");
                foreach (var e in z.Entries)
                {
                    string p = Path.GetFullPath(Path.Combine(dest, e.FullName));
                    if (!p.StartsWith(DataDir)) continue;
                    if (e.FullName.EndsWith("/")) Directory.CreateDirectory(p);
                    else { Directory.CreateDirectory(Path.GetDirectoryName(p)); e.ExtractToFile(p, true); }
                    if (++n % 200 == 0) RunOnUiThread(() => _status.Text = $"Unpacking Content.zip ... {n * 100 / Math.Max(1, total)}%");
                }
            }
            File.Delete(Path.Combine(DataDir, "Content", ".unpacking"));
            File.WriteAllText(Path.Combine(DataDir, "Content", ".unpacked_ok"), DateTime.Now.ToString("o"));
            File.Delete(zip);
            RunOnUiThread(StartGame);
        }
        catch (Exception ex) { RunOnUiThread(() => _status.Text = "Unpacking failed: " + ex.Message + "\n\nFree some storage (about 3 GB needed) and start the app again."); }
    }

    private void ShowStatus(string text)
    {
        _status = new TextView(this) { Text = text, TextSize = 18f, Gravity = GravityFlags.Center };
        _status.SetTextColor(global::Android.Graphics.Color.White);
        _status.SetBackgroundColor(global::Android.Graphics.Color.Black);
        _status.SetPadding(48, 48, 48, 48);
        SetContentView(_status);
    }

    private void StartGame()
    {
        _game = new FNFGame();
        var view = (View)_game.Services.GetService(typeof(View));
        SetContentView(view);
        HideSystemUi(view);
        _game.Run();
    }

    private static void HideSystemUi(View v)
    {
#pragma warning disable CS0618
        v.SystemUiVisibility = (StatusBarVisibility)(SystemUiFlags.Fullscreen | SystemUiFlags.HideNavigation | SystemUiFlags.ImmersiveSticky | SystemUiFlags.LayoutStable);
#pragma warning restore CS0618
    }

    private string ExtractShaders()
    {
        string dir = Path.Combine(FilesDir.AbsolutePath, "shaders_gl");
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var name in Assets.List("shaders_gl") ?? System.Array.Empty<string>())
            {
                using var src = Assets.Open("shaders_gl/" + name);
                using var dst = File.Create(Path.Combine(dir, name));
                src.CopyTo(dst);
            }
        }
        catch (Exception ex) { Console.WriteLine($"[ANDROID] shader extract failed: {ex.Message}"); }
        return dir;
    }

    private void RedirectLog()
    {
        try
        {
            var w = new StreamWriter(Path.Combine(DataDir, "game_log.txt")) { AutoFlush = true };
            Console.SetOut(w); Console.SetError(w);
            Console.WriteLine($"[LOG] Game started at {DateTime.Now}");
        }
        catch { }
    }
}
#endif
