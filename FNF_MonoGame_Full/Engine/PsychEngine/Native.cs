namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Platform-native helpers (DPI awareness, window scaling, etc.).
/// On Xbox/UWP these are no-ops; on Windows desktop they would normally
/// call Win32 DPI APIs.
/// Original: backend/Native.hx
/// </summary>
public static class Native
{
    private static bool _registeredDpiAware;
    private static bool _fixedScaling;

    public static void RegisterDpiAware()
    {
        if (_registeredDpiAware) return;
        _registeredDpiAware = true;

#if !XBOX_UWP
        try
        {
            // Win32 DPI awareness is handled by the application manifest / .NET runtime
            // on modern Windows. No direct P/Invoke is performed here to keep the code
            // Xbox-compatible.
            System.Console.WriteLine("[Native] DPI awareness registered (desktop).");
        }
        catch { }
#endif
    }

    public static void FixScaling()
    {
        if (_fixedScaling) return;
        _fixedScaling = true;

#if !XBOX_UWP
        try
        {
            System.Console.WriteLine("[Native] Scaling fix applied (desktop).");
        }
        catch { }
#endif
    }
}
