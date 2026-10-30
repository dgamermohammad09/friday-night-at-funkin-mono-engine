using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.ViewManagement;
using MonoGame.Framework;

namespace FNF_MonoGame;

/// <summary>
/// MonoGame host page for UWP/Xbox. 
/// The SwapChainPanel renders the MonoGame graphics.
/// Uses XamlGame&lt;T&gt;.Create() for proper UWP initialization.
/// </summary>
public sealed partial class GamePage : Page
{
    private XboxGame _game;

    public GamePage()
    {
        InitializeComponent();

        try
        {
            // Force fullscreen on Xbox/UWP — prevents the half-windowed issue
            var view = ApplicationView.GetForCurrentView();
            view.TryEnterFullScreenMode();
            ApplicationView.PreferredLaunchWindowingMode = ApplicationViewWindowingMode.FullScreen;
            view.SetDesiredBoundsMode(ApplicationViewBoundsMode.UseCoreWindow);

            // B on Xbox raises a system "back" request; unhandled at the app's root the console leaves the app
            // (it looks like a crash when backing out of Options). The game handles B itself -> always mark it handled.
            Windows.UI.Core.SystemNavigationManager.GetForCurrentView().BackRequested += (s, e) =>
            {
                e.Handled = true;
                System.Console.WriteLine("[XBOX] BackRequested handled");
            };

            // Hide the mouse cursor (Xbox shows a virtual mouse by default)
            Window.Current.CoreWindow.PointerCursor = null;

            // Standard MonoGame UWP initialization:
            // XamlGame<T>.Create() wires up SwapChainPanel BEFORE Game() constructor,
            // creates the game instance, and calls Run().
            string launchArgs = App.LaunchArgs ?? "";
            _game = XamlGame<XboxGame>.Create(
                launchArgs,
                Windows.UI.Core.CoreWindow.GetForCurrentThread(),
                swapChainPanel);
            XboxSystemKeyboard.Install(swapChainPanel);   // Xbox keyboard for text fields + gamertag as online name
        }
        catch (System.Exception ex)
        {
            var tb = new TextBlock
            {
                Text = $"GAME CRASH:\n{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}",
                Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Red),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20)
            };
            Content = new ScrollViewer { Content = tb };
        }
    }
}
