namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// OpenAL Soft configuration loader.
/// On desktop it points OpenALSoft at a custom config file under the executable directory.
/// On Xbox/UWP this is a no-op because OpenALSoft config paths are not applicable.
/// Original: backend/ALSoftConfig.hx
/// </summary>
public static class ALSoftConfig
{
    public static void Apply()
    {
#if !XBOX_UWP
        try
        {
            string origin = System.Reflection.Assembly.GetEntryAssembly()?.Location
                          ?? System.AppDomain.CurrentDomain.BaseDirectory;
            string baseDir = System.IO.Path.GetDirectoryName(
                System.IO.Path.ChangeExtension(origin, null)) ?? "";

            string configPath = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows)
                ? System.IO.Path.Combine(baseDir, "plugins", "alsoft.ini")
                : System.IO.Path.Combine(baseDir, "plugins", "alsoft.conf");

            System.Environment.SetEnvironmentVariable("ALSOFT_CONF", configPath);
        }
        catch { }
#endif
    }
}
