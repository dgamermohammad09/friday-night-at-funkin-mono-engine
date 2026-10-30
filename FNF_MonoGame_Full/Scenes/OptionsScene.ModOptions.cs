using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using FNF_MonoGame.Engine;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// V-Slice mod option pages. QT: Rewired (scripts/modules/options/QtOptionCategory.hxc, a 0.8.4 OptionsState module)
/// adds a "Rewired Options" page before LAG ADJUSTMENT with Language / Sawblade Mode / Plushie Button, and a DODGE key
/// row in Controls; values live in Save.modOptions (port: vslice_modoptions.json via VsModOptions).
/// The port shows the page with its own FunkMenu rows; the dodge keyboard key and the port-only pad button are rows of
/// the same page (the mod's Controls-page row would need the 0.8.4 ControlsMenu).
/// </summary>
public partial class OptionsScene
{
    private const string QtCatName = "REWIRED OPTIONS";
    private static bool? s_qtOptions;
    private static bool QtOptionsPresent
    {
        get
        {
            if (s_qtOptions == null)
                try { s_qtOptions = VSliceMods.Any && VSliceScripts.ClassesExtending("Module").Any(c => c.Name == "QtOptionsCategory"); } catch { s_qtOptions = false; }
            return s_qtOptions.Value;
        }
    }

    /// <summary>Categories incl. mod pages (QtOptionsCategory inserts its page before LAG ADJUSTMENT).</summary>
    private static (string name, string art, string desc, Color col)[] Cats
    {
        get
        {
            if (!QtOptionsPresent) return CatsBase;
            var l = CatsBase.ToList();
            int at = l.FindIndex(c => c.name == "LAG ADJUSTMENT"); if (at < 0) at = l.Count - 1;
            l.Insert(at, (QtCatName, "@d_star", "QT: Rewired mod options: language, sawblade mode, dodge key, plushie button.", FunkUI.Pink));
            return l.ToArray();
        }
    }

    /// <summary>Category index in the visible list -> index of the base list (or -1 for a mod page).</summary>
    private static int BaseCatIndex(int i)
    {
        var cats = Cats; if (i < 0 || i >= cats.Length) return i;
        string n = cats[i].name;
        return n == QtCatName ? -1 : System.Array.FindIndex(CatsBase, c => c.name == n);
    }

    private static readonly (string label, int code)[] QtDodgeKeys =
    {
        ("SPACE", 32), ("SHIFT", 16), ("CTRL", 17), ("ALT", 18), ("Q", 81), ("E", 69), ("R", 82), ("F", 70), ("V", 86), ("Z", 90), ("X", 88), ("C", 67), ("TAB", 9),
    };
    private static readonly (string label, string button)[] QtDodgePads = { ("R3", "RightStick"), ("L3", "LeftStick"), ("VIEW", "Back") };

    private List<PrefItem> BuildQtOptionsPage()
    {
        string Get(string k, string def) => VsModOptions.GetValue(k) as string ?? def;
        string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
        return new List<PrefItem>
        {
            new PrefItem { Name = "Language", Desc = "Changes the language used in the mod.", Kind = PrefKind.Choice, Choices = new[] { "English", "Spanish" },
                GetS = () => Cap(Get("qt-rewiredLang", "english")), SetS = v => VsModOptions.SetValue("qt-rewiredLang", v.ToLowerInvariant()) },
            new PrefItem { Name = "Sawblade Mode", Desc = "Changes how the Sawblade Mechanic works.", Kind = PrefKind.Choice, Choices = new[] { "Default", "Instakill", "Disabled" },
                GetS = () => Cap(Get(PlayScene.QsKeyMode, "default")), SetS = v => VsModOptions.SetValue(PlayScene.QsKeyMode, v.ToLowerInvariant()) },
            new PrefItem { Name = "Dodge Key", Desc = "Keyboard key used to dodge the sawblade (QT: Rewired controls: DODGE).", Kind = PrefKind.Choice,
                Choices = QtDodgeKeys.Select(k => k.label).ToArray(),
                GetS = () => { int c = PlayScene.QsDodgeKeys()[0]; var m = QtDodgeKeys.FirstOrDefault(k => k.code == c); return m.label ?? PlayScene.QsKeyName(c).ToUpperInvariant(); },
                SetS = v => { var m = QtDodgeKeys.FirstOrDefault(k => k.label == v); if (m.label != null) VsModOptions.SetValue(PlayScene.QsKeyDodge, new[] { m.code, 0 }); } },
            new PrefItem { Name = "Dodge Button", Desc = "Controller button used to dodge the sawblade (port addition: the mod is keyboard only).", Kind = PrefKind.Choice,
                Choices = QtDodgePads.Select(k => k.label).ToArray(),
                GetS = () => PlayScene.QsPadName(PlayScene.QsPadButton()),
                SetS = v => { var m = QtDodgePads.FirstOrDefault(k => k.label == v); if (m.label != null) VsModOptions.SetValue(PlayScene.QsKeyPad, m.button); } },
            new PrefItem { Name = "Plushie Button", Desc = "When enabled, a button to the QT: Rewired X Makeship campaign is shown in the Main Menu.", Kind = PrefKind.Toggle,
                GetB = () => VsModOptions.GetValue("com.rewired.plush-show-plush-advert") is bool b ? b : true,
                SetB = b => VsModOptions.SetValue("com.rewired.plush-show-plush-advert", b) },
            new PrefItem { Name = "Back", Desc = "", Kind = PrefKind.Back },
        };
    }
}
