using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI;

namespace FNF_MonoGame;

sealed partial class App : Application
{
    public static string LaunchArgs { get; private set; } = "";

    public App()
    {
        InitializeComponent();
        // Xbox: XAML apps default to "mouse mode" (gamepad drives a pointer and A = left click).
        // That made A click whatever menu row sat under the invisible cursor (e.g. MODS) instead of the
        // highlighted one. Turn it off so the controller is read only as a gamepad.
        RequiresPointerMode = ApplicationRequiresPointerMode.WhenRequested;
        Suspending += OnSuspending;
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        LaunchArgs = args.Arguments ?? "";

        // On Xbox, the PLM requires activation ASAP.
        // GamePage has a dark background set in XAML, so it renders immediately
        // even before MonoGame's DirectX initialization completes.
        if (Window.Current.Content == null)
        {
            var page = new GamePage();
            Window.Current.Content = page;
        }
        Window.Current.Activate();
    }

    private void OnSuspending(object sender, SuspendingEventArgs e)
    {
        var deferral = e.SuspendingOperation.GetDeferral();
        try { UwpLog.Flush(); } catch { }
        deferral.Complete();
    }

    private void OnUnhandledException(object sender, Windows.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        try { FNF_MonoGame.Engine.CrashHandler.Report(e.Exception ?? new System.Exception(e.Message), "xaml-unhandled", XboxGame.Instance?.Scenes?.CurrentScene?.GetType().Name ?? "-"); } catch { }
        try { UwpLog.Flush(); } catch { }
        try
        {
            var tb = new TextBlock
            {
                Text = $"UNHANDLED:\n{e.Exception?.GetType().Name}: {e.Message}\n\n{e.Exception?.StackTrace}",
                Foreground = new SolidColorBrush(Colors.Red),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20)
            };
            Window.Current.Content = new ScrollViewer { Content = tb };
        }
        catch { }
    }
}
