using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FNF_MonoGame.Engine.PsychEngine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Psych 1.0.4 option pages the FunkMenu options did not have yet:
///  - LanguageSubState: languages = data/*.lang in the base folders + every mod (Mods.directoriesWithFile(.., 'data/')),
///    display name = first line when it has no ':', default 'en-US' = Language.defaultLangName; picking one reloads phrases.
///  - NotesColorSubState: ClientPrefs.arrowRGB (per lane fill / outline / shadow) edited as hex values + reset
///    (the port's picker is a text field instead of Psych's colour wheel; the stored data and the note shader use are the same).
/// </summary>
public partial class OptionsScene
{
    private static readonly string[] LaneNames = { "Left", "Down", "Up", "Right" };
    private static readonly string[] PartNames = { "Fill", "Outline", "Shadow" };

    /// <summary>(code, display) for every language file found.</summary>
    internal static List<(string Code, string Name)> PsychLanguages()
    {
        var list = new List<(string, string)> { (ClientPrefs.DefaultData.Language, Language.DefaultLangName) };
        var dirs = new List<string>();
        try
        {
            string mods = PsychPaths.Mods();
            if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(Path.Combine(mods, "data"))) dirs.Add(Path.Combine(mods, "data"));
            foreach (var m in PsychMods.GetAllMods())
            {
                if (!m.Enabled) continue;
                string d = PsychMods.GetModDirectory(m.Name);
                if (d != null) dirs.Add(Path.Combine(d, "data"));
            }
        }
        catch { }
        foreach (var dir in dirs)
        {
            if (!global::FNF_MonoGame.Engine.FsCI.DirectoryExists(dir)) continue;
            foreach (var f in global::FNF_MonoGame.Engine.FsCI.GetFiles(dir, "*.lang"))
            {
                string code = Path.GetFileNameWithoutExtension(f).ToLowerInvariant().Trim();
                if (list.Any(l => string.Equals(l.Item1, code, StringComparison.OrdinalIgnoreCase))) continue;
                string name = code;
                try
                {
                    string txt = global::FNF_MonoGame.Engine.FsCI.ReadAllText(f);
                    int id = txt.IndexOf('\n');
                    string first = (id > 0 ? txt.Substring(0, id) : txt).Trim();
                    if (first.Length > 0 && !first.Contains(":")) name = first;
                }
                catch { }
                list.Add((code, name));
            }
        }
        return list;
    }

    private PrefItem PsychLanguageItem()
    {
        var langs = PsychLanguages();
        return PC("Language", "Choose the language used by mods with translation files (data/<language>.lang).",
            langs.Select(l => l.Name).ToArray(),
            () => (langs.FirstOrDefault(l => string.Equals(l.Code, P.Language, StringComparison.OrdinalIgnoreCase)).Name) ?? Language.DefaultLangName,
            v =>
            {
                var hit = langs.FirstOrDefault(l => l.Name == v);
                P.Language = hit.Code ?? ClientPrefs.DefaultData.Language;
                try { Language.ReloadPhrases(); } catch { }
            });
    }

    /// <summary>VISUALS AND UI page: Language + note colours before 'Back'.</summary>
    private void PsychAddVisualExtras(List<PrefItem> items)
    {
        int at = Math.Max(0, items.Count - 1);
        var extra = new List<PrefItem> { PsychLanguageItem() };
        extra.AddRange(PsychNoteColorItems());
        items.InsertRange(at, extra);
    }

    private static string RgbHex(uint c) => (c & 0xFFFFFF).ToString("X6");

    private IEnumerable<PrefItem> PsychNoteColorItems()
    {
        for (int lane = 0; lane < 4; lane++)
            for (int part = 0; part < 3; part++)
            {
                int l = lane, p = part;
                yield return new PrefItem
                {
                    Name = "Note " + LaneNames[l] + " " + PartNames[p],
                    Desc = "Note colour (hex RRGGBB) used by the Psych note skin RGB shader (NotesColorSubState).",
                    Kind = PrefKind.Text, Psych = true,
                    GetS = () => RgbHex(SafeRgb()[l][p]),
                    SetS = v =>
                    {
                        string h = (v ?? "").Trim().TrimStart('#');
                        if (h.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) h = h.Substring(2);
                        if (h.Length == 8) h = h.Substring(2);
                        if (h.Length != 6 || !uint.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out uint rgb)) return;
                        var a = SafeRgb();
                        a[l][p] = 0xFF000000u | rgb;
                        P.ArrowRGB = a;
                    }
                };
            }
        yield return PA("Reset Note Colors", "Restore the default note colours.", () =>
        {
            P.ArrowRGB = PsychNoteSkin.ArrowRGB.Select(x => (uint[])x.Clone()).ToArray();
        });
    }

    private static uint[][] SafeRgb()
    {
        var a = P.ArrowRGB;
        if (a == null || a.Length < 4 || a.Any(x => x == null || x.Length < 3))
            a = PsychNoteSkin.ArrowRGB.Select(x => (uint[])x.Clone()).ToArray();
        return a;
    }
}
