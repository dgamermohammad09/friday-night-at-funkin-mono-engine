using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine.PsychEngine.Objects;

namespace FNF_MonoGame.Engine.PsychEngine.Options;

/// <summary>
/// Psych Engine language selection substate.
/// Direct port of options/LanguageSubState.hx — scans data/*.lang files from base +
/// all enabled mods, presents them as an Alphabet list, and applies the selection
/// via <see cref="ClientPrefs"/> + <see cref="Language.ReloadPhrases"/>.
///
/// Guarded by TRANSLATIONS_ALLOWED (matches Psych's #if guard). When translations are
/// disabled, the substate immediately closes.
/// </summary>
public class LanguageSubState : MusicBeatSubstate
{
    private readonly List<Alphabet> _grpLanguages = new();
    private readonly List<string> _languages = new();
    private readonly Dictionary<string, string> _displayLanguages = new();
    private int _curSelected;
    private bool _changedLanguage;

    private Texture2D _bgTexture;
    private KeyboardState _prevKb;
    private GamePadState _prevGp;

    public event Action Closed;

    public override void Create()
    {
        base.Create();

#if !TRANSLATIONS_ALLOWED
        // Match Psych's #if guard — nothing to configure.
        Close();
        return;
#endif

        // Background: menuDesat tinted magenta 0xFFea71fd (matches Psych).
        string path = PsychPaths.Image("menuDesat");
        if (!string.IsNullOrEmpty(path))
        {
            try
            {
                string basePath = Path.ChangeExtension(path, null);
                var sheet = SpriteSheet.Load(FNFGame.Instance, basePath);
                _bgTexture = sheet?.Texture;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LanguageSubState] menuDesat load failed: {ex.Message}");
            }
        }

        BuildLanguageList();

        // Resolve current selection (fallback to defaultData.language if not found — matches Psych L67-73)
        _curSelected = _languages.IndexOf(ClientPrefs.Data.Language);
        if (_curSelected < 0)
        {
            ClientPrefs.Data.Language = ClientPrefs.DefaultData.Language;
            _curSelected = Math.Max(0, _languages.IndexOf(ClientPrefs.Data.Language));
        }

        // Build Alphabet items (matches Psych L75-93)
        for (int i = 0; i < _languages.Count; i++)
        {
            string lang = _languages[i];
            string display = _displayLanguages.TryGetValue(lang, out var d) ? d : lang;

            var text = new Alphabet(0, 300, display, true)
            {
                IsMenuItem = true,
                TargetY = i,
                ChangeX = false,
                DistancePerItem = new Vector2(20, 100)
            };
            // Center horizontally
            text.X = (FNFGame.SCREEN_WIDTH - text.Width) / 2f;

            // If fewer than 7 languages, lock Y at screen-center offset (matches Psych L85-90)
            if (_languages.Count < 7)
            {
                text.ChangeY = false;
                text.Y = (FNFGame.SCREEN_HEIGHT - 100) / 2f + (100 * (i - _languages.Count / 2f)) + 45f;
            }

            _grpLanguages.Add(text);
        }

        ChangeSelected(0);

        _prevKb = Keyboard.GetState();
        _prevGp = GamePad.GetState(PlayerIndex.One);
    }

    private void BuildLanguageList()
    {
        // The default language always sits at index 0 (matches Psych L23-24).
        _languages.Add(ClientPrefs.DefaultData.Language);
        _displayLanguages[ClientPrefs.DefaultData.Language] = Language.DefaultLangName;

        // Scan every data/ directory (base + each enabled mod) for *.lang files.
        var dataDirs = new List<string>();
        string baseDataDir = PsychPaths.GetSharedPath("data");
        if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(baseDataDir)) dataDirs.Add(baseDataDir);

        foreach (var mod in PsychMods.GetAllMods().Where(m => m.Enabled))
        {
            string modDir = PsychMods.GetModDirectory(mod.Name);
            if (string.IsNullOrEmpty(modDir)) continue;
            string modDataDir = Path.Combine(modDir, "data");
            if (global::FNF_MonoGame.Engine.FsCI.DirectoryExists(modDataDir)) dataDirs.Add(modDataDir);
        }

        foreach (var dir in dataDirs)
        {
            string[] langFiles;
            try { langFiles = global::FNF_MonoGame.Engine.FsCI.GetFiles(dir, "*.lang"); }
            catch { continue; }

            foreach (var filePath in langFiles)
            {
                string langId = Path.GetFileNameWithoutExtension(filePath).Trim();
                if (string.IsNullOrEmpty(langId)) continue;
                if (!_languages.Contains(langId)) _languages.Add(langId);

                if (!_displayLanguages.ContainsKey(langId))
                {
                    // Extract display name from the first line of the .lang file, matches Psych L45-51.
                    // The first non-empty line without ':' is treated as the human-readable name.
                    try
                    {
                        string txt = global::FNF_MonoGame.Engine.FsCI.ReadAllText(filePath);
                        int nl = txt.IndexOf('\n');
                        if (nl > 0)
                        {
                            string name = txt[..nl].Trim();
                            if (!name.Contains(':')) _displayLanguages[langId] = name;
                        }
                        else if (txt.Trim().Length > 0 && !txt.Contains(':'))
                        {
                            _displayLanguages[langId] = txt.Trim();
                        }
                    }
                    catch { /* ignore malformed lang files */ }
                }
            }
        }

        // Sort by display name (case-insensitive, matches Psych L57-64).
        _languages.Sort((a, b) =>
        {
            string da = (_displayLanguages.TryGetValue(a, out var av) ? av : a).ToLowerInvariant();
            string db = (_displayLanguages.TryGetValue(b, out var bv) ? bv : b).ToLowerInvariant();
            return string.Compare(da, db, StringComparison.Ordinal);
        });
    }

    public override void Update(float elapsed)
    {
        base.Update(elapsed);

        var kb = Keyboard.GetState();
        var gp = GamePad.GetState(PlayerIndex.One);

        int mult = kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift) ? 4 : 1;

        bool upPressed = (kb.IsKeyDown(Keys.Up) && !_prevKb.IsKeyDown(Keys.Up)) ||
                         (gp.DPad.Up == ButtonState.Pressed && _prevGp.DPad.Up != ButtonState.Pressed);
        bool downPressed = (kb.IsKeyDown(Keys.Down) && !_prevKb.IsKeyDown(Keys.Down)) ||
                           (gp.DPad.Down == ButtonState.Pressed && _prevGp.DPad.Down != ButtonState.Pressed);
        bool acceptPressed = (kb.IsKeyDown(Keys.Enter) && !_prevKb.IsKeyDown(Keys.Enter)) ||
                             (gp.Buttons.A == ButtonState.Pressed && _prevGp.Buttons.A != ButtonState.Pressed);
        bool backPressed = (kb.IsKeyDown(Keys.Escape) && !_prevKb.IsKeyDown(Keys.Escape)) ||
                           (gp.Buttons.B == ButtonState.Pressed && _prevGp.Buttons.B != ButtonState.Pressed);

        if (upPressed) ChangeSelected(-mult);
        if (downPressed) ChangeSelected(mult);

        if (backPressed)
        {
            // In Psych, if the language was changed the entire state is reset so text re-renders.
            // In our port there is no "state reset" primitive on a substate; callers reload their
            // own text via GetPhrase, so a plain Close() is sufficient. `changedLanguage` is
            // exposed via <see cref="LanguageChanged"/> so PlayScene/menus can refresh themselves.
            Close();
        }
        else if (acceptPressed)
        {
            ClientPrefs.Data.Language = _languages[_curSelected];
            ClientPrefs.Save();
            Language.ReloadPhrases();
            _changedLanguage = true;
        }

        foreach (var lang in _grpLanguages) lang.Update(elapsed);

        _prevKb = kb;
        _prevGp = gp;
    }

    private void ChangeSelected(int change)
    {
        if (_grpLanguages.Count == 0) return;
        _curSelected += change;
        while (_curSelected < 0) _curSelected += _languages.Count;
        while (_curSelected >= _languages.Count) _curSelected -= _languages.Count;

        for (int i = 0; i < _grpLanguages.Count; i++)
        {
            var lang = _grpLanguages[i];
            lang.TargetY = i - _curSelected;
            lang.Alpha = (i == _curSelected) ? 1f : 0.6f;
        }
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        base.Draw(spriteBatch);
        if (_bgTexture != null)
        {
            var viewport = FNFGame.Instance.GraphicsDevice.Viewport;
            spriteBatch.Draw(_bgTexture, new Rectangle(0, 0, viewport.Width, viewport.Height),
                new Color(0xEA, 0x71, 0xFD));
        }

        foreach (var lang in _grpLanguages) lang.Draw(spriteBatch);
    }

    public override void Close()
    {
        base.Close();
        Closed?.Invoke();
    }

    /// <summary>True if the user changed the language and confirmed with ACCEPT.</summary>
    public bool LanguageChanged => _changedLanguage;
}
