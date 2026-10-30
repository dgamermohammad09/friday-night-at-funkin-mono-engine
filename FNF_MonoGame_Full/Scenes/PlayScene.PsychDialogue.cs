using System;
using System.Globalization;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FNF_MonoGame.Engine.PsychEngine;
using FNF_MonoGame.Engine.PsychEngine.Cutscenes;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Psych Engine 1.0.4 countdown gating + DialogueBoxPsych (states/PlayState.hx startCountdown 956-1000,
/// startDialogue 899-926, startNextDialogue/skipDialogue 1225-1232; psychlua/FunkinLua.hx startDialogue 1242-1282).
/// onStartCountdown returning Function_Stop holds the song before the countdown until a script (or the dialogue's
/// finishThing) calls startCountdown() again.
/// </summary>
public partial class PlayScene
{
    private bool _psychHold;               // onStartCountdown returned Function_Stop: no countdown yet (inCutscene)
    private bool _psychStartedCountdown;   // PlayState.startedCountdown
    private DialogueBoxPsych _psychDialogue;
    private int _psychDialogueCount;
    private float _psychDlgClock = -1f;    // dev clock: seconds since the dialogue box was created
    private float[] _devDlgAcceptAt;
    private int _devDlgAcceptIdx;

    /// <summary>FNF_SHOTS_DIALOGUETIME=1 clock (seconds since startDialogue), -1 when no dialogue ran yet.</summary>
    public float DevDialogueElapsed => _psychDlgClock;
    public string DevDialogueInfo => _psychDialogue == null ? "none" : $"t={_psychDlgClock:F3}\n" + _psychDialogue.DevInfo();

    /// <summary>PlayState.startCountdown(). Returns true when the countdown actually started.</summary>
    private bool PsychCountdownBegin()
    {
        if (!PsychNotes)
        {
            CallOnLuas("onStartCountdown"); SetOnLuas("startedCountdown", true); CallOnLuas("onCountdownStarted");
            return true;
        }
        if (_psychStartedCountdown) { CallOnScripts("onStartCountdown"); return false; }
        PsychGlobals.SeenCutscene = true;
        string ret = CallOnLuas("onStartCountdown");
        if (ret != FunkinLua.Function_Stop && ret != FunkinLua.Function_StopAll)
            ret = CallOnHScript("onStartCountdown");
        if (ret == FunkinLua.Function_Stop || ret == FunkinLua.Function_StopAll)
        {
            _countdownActive = false;
            _dialogueActive = true;   // blocks the song (inCutscene)
            _psychHold = true;
            return false;
        }
        _psychHold = false;
        _dialogueActive = false;
        _countdownActive = true;
        _countdownTimer = 0;
        _countdownStep = 0;
        PsychSetDefaultStrums(); // generateStaticArrows + setOnScripts('default*StrumX/Y' + i, ...)
        _psychStartedCountdown = true;
        SetOnLuas("startedCountdown", true);
        CallOnScripts("onCountdownStarted");
        // startCountdown 998: moveCameraSection() (curSection 0) -> onMoveCamera after onCountdownStarted (song scripts see this one)
        if (_chart?.Sections != null && _chart.Sections.Count > 0)
        {
            int sec = Math.Max(0, Math.Min(CurSection, _chart.Sections.Count - 1));
            var s0 = _chart.Sections[sec];
            if (PsychCameraTarget(s0.GfSection ? 2 : (s0.MustHitSection ? 0 : 1)) is Microsoft.Xna.Framework.Vector2 t0) { _camTargetWorldX = t0.X; _camTargetWorldY = t0.Y; }
            CallOnScripts("onMoveCamera", s0.GfSection ? "gf" : (s0.MustHitSection ? "boyfriend" : "dad"));
        }
        return true;
    }

    /// <summary>Lua startDialogue(dialogueFile, music): data/&lt;song&gt;/&lt;file&gt;.json from the mod folders.</summary>
    public bool PsychLuaStartDialogue(string dialogueFile, string music)
    {
        string songPath = Difficulty.FormatToSongPath(_songName ?? "");
        string rel = "data/" + songPath + "/" + dialogueFile + ".json";
        string path = PsychMods.ModFolders(rel);
        Console.WriteLine($"[FUNKIN-LUA] startDialogue: Trying to load dialogue: {rel} -> {path ?? "(missing)"}");
        if (path != null)
        {
            PsychDialogueFile f;
            try { f = PsychDialogueFile.Parse(path); }
            catch (Exception e) { Console.WriteLine($"[FUNKIN-LUA] startDialogue: {e.Message}"); f = null; }
            if (f != null && f.dialogue.Count > 0)
            {
                PsychStartDialogue(f, string.IsNullOrEmpty(music) ? null : music);
                return true;
            }
            Console.WriteLine("[FUNKIN-LUA] startDialogue: Your dialogue file is badly formatted!");
        }
        else
        {
            Console.WriteLine("[FUNKIN-LUA] startDialogue: Dialogue file not found");
            if (_forceEndSong) EndSong(); else PsychCountdownBegin();
        }
        return false;
    }

    /// <summary>PlayState.startDialogue(dialogueFile, song)</summary>
    public void PsychStartDialogue(PsychDialogueFile file, string song = null)
    {
        if (_psychDialogue != null) return;
        if (file == null || file.dialogue.Count == 0) { PsychCountdownBegin(); return; } // startAndEnd()
        bool ending = _forceEndSong;
        _psychDialogue = new DialogueBoxPsych(file, song);
        _psychDlgClock = 0f;
        _psychDialogue.finishThing = () =>
        {
            _psychDialogue = null;
            Console.WriteLine($"[DLG] finished t={_psychDlgClock:F3}");
            if (ending) EndSong(); else PsychCountdownBegin();
        };
        _psychDialogue.nextDialogueThing = () => { _psychDialogueCount++; CallOnScripts("onNextDialogue", _psychDialogueCount); };
        _psychDialogue.skipDialogueThing = () => CallOnScripts("onSkipDialogue", _psychDialogueCount);
        if (!_psychStartedCountdown) { _psychHold = true; _dialogueActive = true; _countdownActive = false; }
    }

    /// <summary>Update while the countdown is held (before startCountdown): characters animate, dialogue runs.</summary>
    private void PsychHoldUpdate(float dt)
    {
        W1UpdateBlocked(dt);
        UpdatePsychHoldDialogue(dt);
        SetOnLuas("botPlay", _botplay);
        DispatchUpdatePost(dt); // PlayState.update keeps running scripts' onUpdatePost while in a cutscene
    }

    private void UpdatePsychHoldDialogue(float dt)
    {
        if (_psychDialogue == null) return;
        _psychDlgClock += dt;
        bool devAccept = false;
        _devDlgAcceptAt ??= (Environment.GetEnvironmentVariable("FNF_DLG_ACCEPT_AT") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        if (_devDlgAcceptIdx < _devDlgAcceptAt.Length && _psychDlgClock >= _devDlgAcceptAt[_devDlgAcceptIdx])
        { devAccept = true; _devDlgAcceptIdx++; Console.WriteLine($"[DLG] accept t={_psychDlgClock:F3}"); }
        // Psych Controls ACCEPT (Enter/Space, pad A/Start) and BACK (Backspace/Escape, pad B/Back)
        bool accept = devAccept || Input.IsPressed(Keys.Enter) || Input.IsPressed(Keys.Space)
            || Input.IsGamePadPressed(Buttons.A) || Input.IsGamePadPressed(Buttons.Start);
        bool back = Input.IsPressed(Keys.Escape) || Input.IsPressed(Keys.Back)
            || Input.IsGamePadPressed(Buttons.B) || Input.IsGamePadPressed(Buttons.Back);
        _psychDialogue.Update(dt, accept, back);
    }

    /// <summary>DialogueBoxPsych on camHUD (screen space).</summary>
    private void DrawPsychDialogue(SpriteBatch sb)
    {
        _psychDialogue?.Draw(sb, DrawPsychTextRight);
    }

    private static bool _nokiaLoaded;
    /// <summary>FlxText(x, y, fieldWidth, text, 16) RIGHT aligned, flixel default font (nokiafc22), OUTLINE_FAST black border 2.</summary>
    private void DrawPsychTextRight(SpriteBatch sb, string text, float x, float y, float boxW)
    {
        const int size = 16; const float borderSize = 2f;
        if (!_nokiaLoaded) { _nokiaLoaded = true; try { Assets.LoadNamedFont("nokia", "fonts/nokiafc22.ttf"); } catch { } }
        var font = Assets.GetNamedFont("nokia", size) ?? Assets.GetFont(size);
        if (font == null || string.IsNullOrEmpty(text)) return;
        float textW = font.MeasureString(text).X;
        if (text == "Press BACK to Skip" && FNF_MonoGame.Engine.ButtonPrompts.UseController(Input))
        {
            // controller in use (port addition): "Press [B] to Skip" with the B glyph in place of the BACK keybind name
            const int g = 20;
            string pre = "Press ", post = " to Skip";
            float wPre = font.MeasureString(pre).X, wPost = font.MeasureString(post).X;
            float right = MathF.Round(x + boxW - 2f), ty = MathF.Round(y + 2f);
            float px = right - wPost - g - wPre;
            foreach (var (str, sx) in new[] { (pre, px), (post, right - wPost) })
            {
                foreach (var d in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
                    font.DrawText(sb, str, new Vector2(sx, ty) + d * borderSize, Color.Black);
                font.DrawText(sb, str, new Vector2(sx, ty), Color.White);
            }
            FNF_MonoGame.Engine.ButtonPrompts.DrawGlyph(sb, Assets, true, Buttons.B, "", (int)(px + wPre), (int)ty - 2, g);
            return;
        }
        var p = new Vector2(MathF.Round(x + boxW - 2f - textW), MathF.Round(y + 2f));
        // OUTLINE_FAST: 4 diagonal copies at +-borderSize, then the fill
        foreach (var d in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
            font.DrawText(sb, text, p + d * borderSize, Color.Black);
        font.DrawText(sb, text, p, Color.White);
    }
}
