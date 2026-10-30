using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FNF_MonoGame.Engine.PsychEngine;

namespace FNF_MonoGame.Engine;

/// <summary>
/// Note Skin choices shared by Options > Note Skin and the Skins menu:
///  - "Arrows"             = the game's own arrows (V-Slice funkin / pixel style, Psych default NOTE_assets in Psych songs);
///  - "Controller Buttons" = controller button icons on both strumlines (HighscoreManager.NoteSkinButtons), plus the
///    other approved controller styles ("Buttons: ..." = ControllerNoteSkin.StyleNames, HighscoreManager.NoteSkinButtonStyle);
///    Xbox or PlayStation art follows ControllerIcons.UsePlayStation;
///  - any Psych note skin  = images/psych/noteSkins/NOTE_assets-&lt;name&gt;.png (Psych 1.0: Chip, Future) plus the ones
///    in mods/images/noteSkins and global mods (Psych's Visuals > Note Skins list). Stored like Psych in
///    ClientPrefs.Data.NoteSkin (\"Default\" = no skin), so Psych Online sends it to the other players too.
/// </summary>
public static class NoteStyles
{
    public const string Arrows = "Arrows", Buttons = "Controller Buttons";

    /// <summary>True for any controller-button style name (Controller Buttons + the "Buttons: ..." styles).</summary>
    public static bool IsButtons(string v) => ControllerNoteSkin.StyleOfName(v) > 0;

    /// <summary>Style 1..6 of a choice name (0 = not a controller style).</summary>
    public static int ButtonStyle(string v) => ControllerNoteSkin.StyleOfName(v);

    /// <summary>Choice names: Arrows, Controller Buttons (style 3, default), the other 5 controller styles, then every installed Psych note skin (title case).</summary>
    public static List<string> Available()
    {
        var l = new List<string> { Arrows, Buttons };
        for (int i = 0; i < ControllerNoteSkin.StyleNames.Length; i++) if (i + 1 != ControllerNoteSkin.DefaultStyle) l.Add(ControllerNoteSkin.StyleNames[i]);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "default" };
        void Scan(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !FsCI.DirectoryExists(dir)) return;
                foreach (var f in FsCI.GetFiles(dir, "NOTE_assets-*.png").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    string n = Path.GetFileNameWithoutExtension(f).Substring("NOTE_assets-".Length);
                    if (n.Length == 0 || !seen.Add(n)) continue;
                    if (!FsCI.FileExists(Path.ChangeExtension(f, ".xml"))) continue;
                    l.Add(Title(n));
                }
            }
            catch (Exception e) { Console.WriteLine("[NOTESKIN] scan " + dir + ": " + e.Message); }
        }
        Scan(Path.Combine(AssetManager.ContentRoot, "images", "psych", "noteSkins"));
        string bas = PsychMods.ModsBasePath;
        if (!string.IsNullOrEmpty(bas))
        {
            Scan(Path.Combine(bas, "images", "noteSkins"));
            try { foreach (var g in PsychMods.GetGlobalMods()) Scan(Path.Combine(bas, g, "images", "noteSkins")); } catch { }
        }
        return l;
    }

    /// <summary>"future" / "chip_v2" -> "Future" / "Chip V2" (Psych keeps the list name; the file postfix is lower case with '_').</summary>
    private static string Title(string n) => string.Join(" ", n.Replace('_', ' ').Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));

    /// <summary>Current choice (Options value).</summary>
    public static string Current
    {
        get
        {
            if (HighscoreManager.NoteSkinButtonsSelected) return ControllerNoteSkin.StyleNames[ControllerNoteSkin.Style - 1];
            ClientPrefs.EnsureLoaded();
            string s = ClientPrefs.Data.NoteSkin;
            return string.IsNullOrWhiteSpace(s) || s.Equals("Default", StringComparison.OrdinalIgnoreCase) ? Arrows : s;
        }
    }

    /// <summary>Apply a choice (saved by the caller's SavePreferences / ClientPrefs.Save).</summary>
    public static void Set(string v)
    {
        ClientPrefs.EnsureLoaded();
        var d = HighscoreManager.Data;
        d.NoteSkinChosen = true;
        d.NoteSkinButtons = IsButtons(v);
        if (d.NoteSkinButtons) d.NoteSkinButtonStyle = ButtonStyle(v);
        ClientPrefs.Data.NoteSkin = d.NoteSkinButtons || v == Arrows || string.IsNullOrWhiteSpace(v) ? "Default" : v;
    }

    /// <summary>Psych sheet key of a skin name (noteSkins/NOTE_assets-&lt;postfix&gt;) or null for the default look.</summary>
    public static string SheetKey(string skin)
    {
        if (string.IsNullOrWhiteSpace(skin) || skin.Equals("Default", StringComparison.OrdinalIgnoreCase) || skin == Arrows || IsButtons(skin)) return null;
        return "noteSkins/NOTE_assets-" + skin.Trim().ToLowerInvariant().Replace(' ', '_');
    }

    /// <summary>Sheet key of the player's chosen skin, or null (Arrows / Controller Buttons / not installed).</summary>
    public static string UserSheetKey
    {
        get
        {
            if (HighscoreManager.NoteSkinButtonsSelected) return null;
            ClientPrefs.EnsureLoaded();
            return SheetKey(ClientPrefs.Data.NoteSkin);
        }
    }
}
