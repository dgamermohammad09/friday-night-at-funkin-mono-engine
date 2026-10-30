using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Modchart support: per-strum offsets driven by Lua noteTween* functions.
/// Strum indexing follows Psych Engine's strumLineNotes: 0-3 opponent, 4-7 player.
/// </summary>
public partial class PlayScene
{
    public class StrumMod
    {
        public float OffsetX;
        public float OffsetY;
        public float Angle;
        public float Alpha = 1f;
        public float Direction = 90f; // Psych default scroll direction
        public bool Visible = true;
    }

    public StrumMod[] StrumMods { get; } = CreateStrumMods();

    private static StrumMod[] CreateStrumMods()
    {
        var arr = new StrumMod[8];
        for (int i = 0; i < arr.Length; i++) arr[i] = new StrumMod();
        return arr;
    }

    /// <summary>Get the strum mod for a lane. isPlayer maps to Psych indices 4-7.</summary>
    public StrumMod GetStrumMod(int lane, bool isPlayer)
        => StrumMods[Math.Clamp(lane, 0, 3) + (isPlayer ? 4 : 0)];

    // ── Modchart tweens ──────────────────────────────────────────────

    private class ModchartTween
    {
        public string Tag;
        public StrumMod Target;
        public string Field;   // x, y, angle, alpha, direction
        public float From;
        public float To;
        public float Duration;
        public float Elapsed;
        public string Ease;
        public Action<string> OnComplete;
    }

    private readonly List<ModchartTween> _modchartTweens = new();

    /// <summary>Psych strum position (StrumNote.playerPosition: STRUM_X 42 + 50 + swagWidth 112 * i (+640 player), y 50 /
    /// downscroll 570). Lua sees absolute x/y like Psych (defaultOpponentStrumX0 = 92); StrumMod keeps the offset from it.</summary>
    public float PsychStrumBaseX(int idx) => PsychNotes ? (idx >= 4 ? 640f : 0f) + 92f + 112f * (idx & 3) : 0f;
    public float PsychStrumBaseY(int idx) => PsychNotes ? (_downscroll ? 720f - 150f : 50f) : 0f;

    /// <summary>PlayState.startCountdown: setOnScripts('defaultPlayerStrumX' + i, playerStrums.members[i].x) ...</summary>
    internal void PsychSetDefaultStrums()
    {
        if (!PsychNotes) return;
        // generateStaticArrows (PlayState 1527): middleScroll -> strumLineX = STRUM_X_MIDDLESCROLL (-278) instead of 42;
        // opponent += 310 (+ FlxG.width/2 + 25 for lanes 2-3), alpha 0.35 (0 without opponentStrums). The base
        // position stays the normal Psych one, so the shift is kept in the strum offset (Lua sees absolute x).
        var prefs = FNF_MonoGame.Engine.PsychEngine.ClientPrefs.Data;
        bool ms = prefs.MiddleScroll;
        for (int i = 0; i < 4; i++)
        {
            var opp = StrumMods[i];
            if (!prefs.OpponentStrums) opp.Alpha = 0f;
            else if (ms) opp.Alpha = 0.35f;
            if (ms)
            {
                StrumMods[i + 4].OffsetX = -278f - 42f;
                opp.OffsetX = -278f - 42f + 310f + (i > 1 ? 640f + 25f : 0f);
            }
        }
        Console.WriteLine($"[PSYCH] generateStaticArrows middleScroll={ms} strum0={PsychStrumBaseX(0) + StrumMods[0].OffsetX} strum4={PsychStrumBaseX(4) + StrumMods[4].OffsetX}"); StrumLockSnapshot();
        for (int i = 0; i < 4; i++)
        {
            SetOnLuas("defaultPlayerStrumX" + i, (double)(PsychStrumBaseX(i + 4) + StrumMods[i + 4].OffsetX));
            SetOnLuas("defaultPlayerStrumY" + i, (double)(PsychStrumBaseY(i + 4) + StrumMods[i + 4].OffsetY));
            SetOnLuas("defaultOpponentStrumX" + i, (double)(PsychStrumBaseX(i) + StrumMods[i].OffsetX));
            SetOnLuas("defaultOpponentStrumY" + i, (double)(PsychStrumBaseY(i) + StrumMods[i].OffsetY));
        }
    }

    /// <summary>Note.followStrumNote: copyAlpha -> note alpha = strum.alpha * multAlpha (Psych notes only).</summary>
    private float PsychStrumAlphaFor(FNF_MonoGame.Engine.PsychEngine.Note note)
    {
        if (!PsychNotes || note == null) return 1f;
        return Math.Clamp(StrumMods[(note.Lane & 3) + (note.IsPlayerNote ? 4 : 0)].Alpha, 0f, 1f);
    }

    public void StartNoteTween(string tag, int noteIndex, string field, float value, float duration, string ease)
    {
        if (noteIndex < 0) return;
        if (StrumMods.Length > 0) noteIndex %= StrumMods.Length; // FunkinLua noteTweenFunction: members[note % length]
        if (field == "x") value -= PsychStrumBaseX(noteIndex);
        else if (field == "y") value -= PsychStrumBaseY(noteIndex);
        CancelModchartTween(tag);
        var target = StrumMods[noteIndex];
        float from = field switch
        {
            "x" => target.OffsetX,
            "y" => target.OffsetY,
            "angle" => target.Angle,
            "alpha" => target.Alpha,
            "direction" => target.Direction,
            _ => 0f
        };
        // Psych noteTweenX tweens to an absolute screen X; our offsets are relative,
        // so x/y values are interpreted as target offsets from the default position.
        if (duration <= 0f)
        {
            ApplyStrumField(target, field, value);
            OnModchartTweenComplete(tag);
            return;
        }
        _modchartTweens.Add(new ModchartTween
        {
            Tag = tag, Target = target, Field = field,
            From = from, To = value, Duration = duration,
            Ease = ease ?? "linear",
            OnComplete = OnModchartTweenComplete
        });
    }

    public void CancelModchartTween(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return;
        _modchartTweens.RemoveAll(t => string.Equals(t.Tag, tag, StringComparison.OrdinalIgnoreCase));
    }

    private void OnModchartTweenComplete(string tag)
    {
        CallOnLuas("onTweenCompleted", tag); // FunkinLua.noteTweenFunction: callOnLuas('onTweenCompleted', [tag])
    }

    private static void ApplyStrumField(StrumMod target, string field, float value)
    {
        switch (field)
        {
            case "x": target.OffsetX = value; break;
            case "y": target.OffsetY = value; break;
            case "angle": target.Angle = value; break;
            case "alpha": target.Alpha = value; break;
            case "direction": target.Direction = value; break;
        }
    }

    public void UpdateModchartTweens(float dt)
    {
        if (_modchartTweens.Count == 0) return;
        // FlxTweenManager order: tweens update (and complete) in creation order; flixel eases (LuaUtils.getTweenEaseByString)
        foreach (var t in _modchartTweens.ToArray())
        {
            if (!_modchartTweens.Contains(t)) continue;
            t.Elapsed += dt;
            float progress = t.Duration > 0f ? Math.Clamp(t.Elapsed / t.Duration, 0f, 1f) : 1f;
            float eased = (float)FNF_MonoGame.Engine.PsychEngine.LuaTweenManager.EaseByString(t.Ease)(progress);
            ApplyStrumField(t.Target, t.Field, t.From + (t.To - t.From) * eased);
            if (progress >= 1f)
            {
                _modchartTweens.Remove(t);
                t.OnComplete?.Invoke(t.Tag);
            }
        }
    }

    public static float ApplyEase(string ease, float t)
    {
        switch ((ease ?? "linear").ToLowerInvariant())
        {
            case "quadin": return t * t;
            case "quadout": return 1f - (1f - t) * (1f - t);
            case "quadinout": return t < 0.5f ? 2f * t * t : 1f - MathF.Pow(-2f * t + 2f, 2) / 2f;
            case "cubein": return t * t * t;
            case "cubeout": return 1f - MathF.Pow(1f - t, 3);
            case "cubeinout": return t < 0.5f ? 4f * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 3) / 2f;
            case "quartin": return t * t * t * t;
            case "quartout": return 1f - MathF.Pow(1f - t, 4);
            case "quartinout": return t < 0.5f ? 8f * MathF.Pow(t, 4) : 1f - MathF.Pow(-2f * t + 2f, 4) / 2f;
            case "quintin": return MathF.Pow(t, 5);
            case "quintout": return 1f - MathF.Pow(1f - t, 5);
            case "quintinout": return t < 0.5f ? 16f * MathF.Pow(t, 5) : 1f - MathF.Pow(-2f * t + 2f, 5) / 2f;
            case "sinein": return 1f - MathF.Cos(t * MathF.PI / 2f);
            case "sineout": return MathF.Sin(t * MathF.PI / 2f);
            case "sineinout": return -(MathF.Cos(MathF.PI * t) - 1f) / 2f;
            case "expoin": return t <= 0f ? 0f : MathF.Pow(2f, 10f * t - 10f);
            case "expoout": return t >= 1f ? 1f : 1f - MathF.Pow(2f, -10f * t);
            case "expoinout":
                if (t <= 0f) return 0f;
                if (t >= 1f) return 1f;
                return t < 0.5f ? MathF.Pow(2f, 20f * t - 10f) / 2f : (2f - MathF.Pow(2f, -20f * t + 10f)) / 2f;
            case "circin": return 1f - MathF.Sqrt(1f - t * t);
            case "circout": return MathF.Sqrt(1f - (t - 1f) * (t - 1f));
            case "circinout":
                return t < 0.5f
                    ? (1f - MathF.Sqrt(1f - 4f * t * t)) / 2f
                    : (MathF.Sqrt(1f - MathF.Pow(-2f * t + 2f, 2)) + 1f) / 2f;
            case "backin":
            {
                const float c1 = 1.70158f;
                return (c1 + 1f) * t * t * t - c1 * t * t;
            }
            case "backout":
            {
                const float c1 = 1.70158f;
                return 1f + (c1 + 1f) * MathF.Pow(t - 1f, 3) + c1 * MathF.Pow(t - 1f, 2);
            }
            case "backinout":
            {
                const float c2 = 1.70158f * 1.525f;
                return t < 0.5f
                    ? MathF.Pow(2f * t, 2) * ((c2 + 1f) * 2f * t - c2) / 2f
                    : (MathF.Pow(2f * t - 2f, 2) * ((c2 + 1f) * (t * 2f - 2f) + c2) + 2f) / 2f;
            }
            case "elasticin":
            {
                if (t <= 0f) return 0f;
                if (t >= 1f) return 1f;
                const float c4 = 2f * MathF.PI / 3f;
                return -MathF.Pow(2f, 10f * t - 10f) * MathF.Sin((t * 10f - 10.75f) * c4);
            }
            case "elasticout":
            {
                if (t <= 0f) return 0f;
                if (t >= 1f) return 1f;
                const float c4 = 2f * MathF.PI / 3f;
                return MathF.Pow(2f, -10f * t) * MathF.Sin((t * 10f - 0.75f) * c4) + 1f;
            }
            case "bouncein": return 1f - BounceOut(1f - t);
            case "bounceout": return BounceOut(t);
            case "bounceinout":
                return t < 0.5f ? (1f - BounceOut(1f - 2f * t)) / 2f : (1f + BounceOut(2f * t - 1f)) / 2f;
            case "smoothstepin":
            case "smoothstepout":
            case "smoothstepinout": return t * t * (3f - 2f * t);
            default: return t; // linear
        }
    }

    private static float BounceOut(float t)
    {
        const float n1 = 7.5625f;
        const float d1 = 2.75f;
        if (t < 1f / d1) return n1 * t * t;
        if (t < 2f / d1) { t -= 1.5f / d1; return n1 * t * t + 0.75f; }
        if (t < 2.5f / d1) { t -= 2.25f / d1; return n1 * t * t + 0.9375f; }
        t -= 2.625f / d1;
        return n1 * t * t + 0.984375f;
    }

    public void ResetModchart()
    {
        _modchartTweens.Clear();
        for (int i = 0; i < StrumMods.Length; i++)
            StrumMods[i] = new StrumMod();
    }
}
