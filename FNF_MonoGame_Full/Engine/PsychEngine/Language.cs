using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FNF_MonoGame.Engine.PsychEngine;

/// <summary>
/// Translation/Localization helper.
/// Loads .lang files from mods/base game and resolves phrases at runtime.
/// Original: backend/Language.hx
/// </summary>
public static class Language
{
    public static string DefaultLangName = "English (US)";

#if TRANSLATIONS_ALLOWED
    private static readonly Dictionary<string, string> Phrases = new();
    private static readonly Regex HideChars = new("[~&\\\\\\/;:<>#.,'\"%?!]", RegexOptions.Compiled);
#endif

    public static void ReloadPhrases()
    {
#if TRANSLATIONS_ALLOWED
        string langFile = ClientPrefs.Data.language;
        var loadedText = PsychMods.MergeAllTextsNamed($"data/{langFile}.lang");

        Phrases.Clear();
        bool hasPhrases = false;
        for (int i = 0; i < loadedText.Count; i++)
        {
            string phrase = loadedText[i].Trim();
            if (i < 1 && !phrase.Contains(':'))
            {
                Phrases["language_name"] = phrase;
                continue;
            }

            if (phrase.Length < 4 || phrase.StartsWith("//")) continue;

            int n = phrase.IndexOf(':');
            if (n < 0) continue;

            string key = phrase[..n].Trim().ToLowerInvariant();
            string value = phrase[n..];
            n = value.IndexOf('"');
            if (n < 0) continue;

            int lastQuote = value.LastIndexOf('"');
            if (lastQuote <= n) continue;

            string text = value[(n + 1)..lastQuote].Replace("\\n", "\n");
            Phrases[key] = text;
            hasPhrases = true;
        }

        if (!hasPhrases)
            ClientPrefs.Data.language = ClientPrefs.DefaultData.language;
#endif
    }

    public static string GetPhrase(string key, string defaultPhrase = null, List<object> values = null)
    {
#if TRANSLATIONS_ALLOWED
        string str = Phrases.TryGetValue(FormatKey(key), out var phrase) ? phrase : defaultPhrase;
#else
        string str = defaultPhrase;
#endif
        if (string.IsNullOrEmpty(str))
            str = key;

        if (values != null)
        {
            for (int i = 0; i < values.Count; i++)
                str = str.Replace($"{{{i + 1}}}", values[i]?.ToString() ?? "");
        }

        return str;
    }

    public static string GetFileTranslation(string key)
    {
#if TRANSLATIONS_ALLOWED
        string trimmed = key.Trim().ToLowerInvariant();
        if (Phrases.TryGetValue(trimmed, out var translated))
            key = translated;
#endif
        return key;
    }

#if TRANSLATIONS_ALLOWED
    private static string FormatKey(string key)
    {
        return HideChars.Replace(key.Replace(' ', '_'), "").ToLowerInvariant().Trim();
    }
#endif

    public static void AddLuaCallbacks(MoonSharp.Interpreter.Script lua)
    {
        if (lua == null) return;
        lua.Globals["getTranslationPhrase"] = (Func<string, string, List<object>, string>)GetPhrase;
        lua.Globals["getFileTranslation"] = (Func<string, string>)GetFileTranslation;
    }
}
