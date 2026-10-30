using System;
using System.Linq;
using System.Threading.Tasks;
using FNF_MonoGame.Engine;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.ViewManagement.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace FNF_MonoGame;

/// <summary>Xbox/UWP host side of <see cref="SystemKeyboard"/>: a XAML text box over the game that opens the console's
/// own on-screen keyboard, and the signed-in user's gamertag as the default online name.</summary>
internal static class XboxSystemKeyboard
{
    private static Panel _host;
    private static CoreDispatcher _dispatcher;

    public static void Install(Panel host)
    {
        _host = host;
        _dispatcher = host.Dispatcher;
        SystemKeyboard.Provider = Request;
        // Account name only on the console: on a Windows PC reading it pops a "let this app access your account info" prompt.
        bool xbox = false;
        try { xbox = Windows.System.Profile.AnalyticsInfo.VersionInfo.DeviceFamily == "Windows.Xbox"; } catch { }
        if (xbox || Environment.GetEnvironmentVariable("FNF_DEV_GAMERTAG") == "1") _ = LoadGamertagAsync();
        else Console.WriteLine("[XBOX] not a console: online name not taken from the Windows account");
        // Dev: FNF_DEV_OSK=<seconds> opens the keyboard once by itself (test without menu navigation).
        if (double.TryParse(Environment.GetEnvironmentVariable("FNF_DEV_OSK"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double sec))
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(sec));
                Console.WriteLine("[OSK] dev test open");
                string r = await Request("Dev test", "hello", 14);
                Console.WriteLine("[OSK] dev test result: " + (r == null ? "<cancelled>" : "'" + r + "'"));
            });
    }

    private static Task<string> Request(string title, string text, int maxLength)
    {
        var tcs = new TaskCompletionSource<string>();
        _ = _dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            try { Show(title, text, maxLength, tcs); }
            catch (Exception e) { Console.WriteLine("[OSK] show failed: " + e); tcs.TrySetResult(null); }
        });
        return tcs.Task;
    }

    private static void Show(string title, string text, int maxLength, TaskCompletionSource<string> tcs)
    {
        var overlay = new Grid { Background = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)) };
        var card = new StackPanel();
        var frame = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 90, 0, 0), Width = 900, Padding = new Thickness(28), Child = card,
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 24, 56)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 255, 222, 60)), BorderThickness = new Thickness(4),
        };
        card.Children.Add(new TextBlock
        {
            Text = title.ToUpperInvariant(), FontSize = 30, FontWeight = Windows.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White), Margin = new Thickness(0, 0, 0, 14)
        });
        var box = new TextBox
        {
            Text = text ?? "", MaxLength = Math.Max(1, maxLength), FontSize = 30, IsSpellCheckEnabled = false,
            IsTextPredictionEnabled = true, AcceptsReturn = false, SelectionStart = (text ?? "").Length,
        };
        card.Children.Add(box);
        card.Children.Add(new TextBlock
        {
            Text = "Menu / Enter = done      B = cancel", FontSize = 18, Margin = new Thickness(0, 12, 0, 0),
            Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 220))
        });
        overlay.Children.Add(frame);

        bool done = false, shown = false;
        CoreInputView civ = null;
        InputPane pane = null;
        TypedEventHandler<CoreInputView, CoreInputViewHidingEventArgs> civHiding = null;
        TypedEventHandler<CoreInputView, CoreInputViewShowingEventArgs> civShowing = null;
        TypedEventHandler<InputPane, InputPaneVisibilityEventArgs> paneHiding = null, paneShowing = null;

        void Finish(string result)
        {
            if (done) return;
            done = true;
            try
            {
                if (civ != null) { civ.PrimaryViewHiding -= civHiding; civ.PrimaryViewShowing -= civShowing; }
                if (pane != null) { pane.Hiding -= paneHiding; pane.Showing -= paneShowing; }
                _host.Children.Remove(overlay);
            }
            catch { }
            // Close the system keyboard too (Enter in the text box finishes us but leaves the keyboard up).
            try { if (civ != null && ApiInformation.IsMethodPresent("Windows.UI.ViewManagement.Core.CoreInputView", "TryHide")) civ.TryHide(); } catch { }
            try { pane?.TryHide(); } catch { }
            try { box.IsEnabled = false; } catch { }   // drop text focus so the keyboard does not come back
            tcs.TrySetResult(result);
        }

        // Keyboard closed: keep what was typed (B on the system keyboard only closes it), nothing typed = cancel.
        void OnHidden()
        {
            if (!shown) return;
            string t = box.Text ?? "";
            Finish(string.IsNullOrWhiteSpace(t) ? null : t);
        }

        box.KeyDown += (s, e) =>
        {
            if (e.Key == VirtualKey.Enter || e.Key == VirtualKey.GamepadMenu) { e.Handled = true; Finish(box.Text ?? ""); }
            else if (e.Key == VirtualKey.Escape || e.Key == VirtualKey.GamepadB) { e.Handled = true; Finish(null); }
        };

        if (ApiInformation.IsTypePresent("Windows.UI.ViewManagement.Core.CoreInputView"))
        {
            civ = CoreInputView.GetForCurrentView();
            civShowing = (s, e) => shown = true;
            civHiding = (s, e) => { _ = _dispatcher.RunAsync(CoreDispatcherPriority.Low, OnHidden); };
            civ.PrimaryViewShowing += civShowing;
            civ.PrimaryViewHiding += civHiding;
        }
        pane = InputPane.GetForCurrentView();
        paneShowing = (s, e) => shown = true;
        paneHiding = (s, e) => { _ = _dispatcher.RunAsync(CoreDispatcherPriority.Low, OnHidden); };
        pane.Showing += paneShowing;
        pane.Hiding += paneHiding;

        box.Loaded += (s, e) =>
        {
            box.Focus(FocusState.Keyboard);
            box.SelectionStart = box.Text.Length;
            bool ok = false;
            try
            {
                if (civ != null && ApiInformation.IsMethodPresent("Windows.UI.ViewManagement.Core.CoreInputView", "TryShow", 1))
                {
                    // CoreInputViewKind.Gamepad (= 7) is newer than this SDK's enum; the console falls back to its gamepad keyboard anyway.
                    try { ok = civ.TryShow((CoreInputViewKind)7); } catch { }
                    if (!ok) try { ok = civ.TryShow(CoreInputViewKind.Keyboard); } catch { }
                }
            }
            catch { }
            if (!ok) try { ok = pane.TryShow(); } catch { }
            Console.WriteLine($"[OSK] system keyboard requested: {ok}");
        };
        _host.Children.Add(overlay);
    }

    private static async Task LoadGamertagAsync()
    {
        try
        {
            var users = await User.FindAllAsync();
            var user = users.FirstOrDefault(u => u.Type == UserType.LocalUser && u.AuthenticationStatus != UserAuthenticationStatus.Unauthenticated)
                       ?? users.FirstOrDefault();
            if (user == null) { Console.WriteLine("[XBOX] no signed-in user"); return; }
            foreach (var prop in new[] { KnownUserProperties.DisplayName, KnownUserProperties.AccountName, KnownUserProperties.FirstName })
            {
                string v = null;
                try { v = await user.GetPropertyAsync(prop) as string; } catch { }
                v = Clean(v);
                if (!string.IsNullOrEmpty(v)) { SystemKeyboard.AccountName = v; Console.WriteLine($"[XBOX] user name from {prop}: {v}"); return; }
            }
            Console.WriteLine("[XBOX] user has no readable name (userAccountInformation denied?)");
        }
        catch (Exception e) { Console.WriteLine("[XBOX] gamertag lookup failed: " + e.Message); }
    }

    /// <summary>Gamertags can carry a "#1234" suffix and e-mail style account names: keep a short online-safe name.</summary>
    private static string Clean(string v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        int at = v.IndexOf('@'); if (at > 0) v = v.Substring(0, at);
        int hash = v.IndexOf('#'); if (hash > 0) v = v.Substring(0, hash);
        v = new string(v.Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '_' || c == '-' || c == '.').ToArray()).Trim();
        if (v.Length > 14) v = v.Substring(0, 14).Trim();
        return v.Length >= 2 ? v : null;
    }
}
