using System;
using Microsoft.Xna.Framework;
using FNF_MonoGame.Gameplay;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Psych Engine 1.0 PlayState rules that differ from V-Slice, applied only on Psych stages
/// (mods/&lt;mod&gt;/stages/&lt;stage&gt;.json).
/// </summary>
public partial class PlayScene
{
    /// <summary>True while the current song uses a Psych Engine stage JSON.</summary>
    public bool IsPsychStage => _isPsychEngineStage;

    private static float Arr(float[] a, int i) => a != null && a.Length > i ? a[i] : 0f;

    /// <summary>
    /// PlayState.moveCamera / moveCameraToGirlfriend (Psych 1.0.4):
    ///   dad: midpoint + (150, -100) + dad.cameraPosition + opponentCameraOffset
    ///   bf:  midpoint + (-100, -100) + (-bf.cameraPosition.x, bf.cameraPosition.y) + boyfriendCameraOffset
    ///   gf:  midpoint + gf.cameraPosition + girlfriendCameraOffset
    /// </summary>
    internal Vector2? PsychCameraTarget(int focusChar)
    {
        if (!_isPsychEngineStage) return null;
        if (focusChar == 0 && _boyfriend != null)
        {
            var m = _boyfriend.GetMidpoint(); var c = _boyfriend.JsonData?.CameraOffsets;
            return new Vector2(m.X - 100f - Arr(c, 0) + Arr(_pendingBfCamOffsets, 0),
                               m.Y - 100f + Arr(c, 1) + Arr(_pendingBfCamOffsets, 1));
        }
        if (focusChar == 2 && _girlfriend != null)
        {
            var m = _girlfriend.GetMidpoint(); var c = _girlfriend.JsonData?.CameraOffsets;
            return new Vector2(m.X + Arr(c, 0) + Arr(_pendingGfCamOffsets, 0),
                               m.Y + Arr(c, 1) + Arr(_pendingGfCamOffsets, 1));
        }
        if (_opponent != null)
        {
            var m = _opponent.GetMidpoint(); var c = _opponent.JsonData?.CameraOffsets;
            return new Vector2(m.X + 150f + Arr(c, 0) + Arr(_pendingDadCamOffsets, 0),
                               m.Y - 100f + Arr(c, 1) + Arr(_pendingDadCamOffsets, 1));
        }
        return null;
    }

    /// <summary>
    /// PlayState "Change Character" event (Psych 1.0.4 triggerEvent): swap the slot's character, keeping the
    /// group position (new.x = group.x + new.positionArray) and alpha.
    /// </summary>
    internal void PsychChangeCharacter(string target, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;
        newName = newName.Trim();
        int slot = 1;
        switch ((target ?? "").Trim().ToLowerInvariant())
        {
            case "gf": case "girlfriend": case "2": slot = 2; break;
            case "dad": case "opponent": case "1": slot = 1; break;
            case "bf": case "boyfriend": case "0": case "player": slot = 0; break;
            default: slot = int.TryParse(target, out int n) ? n : 0; break;
        }
        var old = slot == 0 ? _boyfriend : slot == 2 ? _girlfriend : _opponent;
        if (old != null && string.Equals(old.CurCharacter, newName, StringComparison.OrdinalIgnoreCase)) return;
        if (slot == 2 && old == null && _psychStageHideGf) return; // Psych 'Change Character' case 2: if(gf != null) only
        try
        {
            Character c = null;
            if (_psychPreloadedChars.TryGetValue(slot + "|" + newName.ToLowerInvariant(), out var pre)) c = pre;
            c ??= BuildPsychCharacter(slot, newName);
            c.PlayAnimation("idle", force: true);
            c.StageUsesPsychPositioning = _isPsychEngineStage;
            float[] slotPos = slot == 0 ? _pendingBfPos : slot == 2 ? _pendingGfPos : _pendingDadPos;
            if (slotPos != null) c.ApplyStagePosition(slotPos);
            if (old != null && _isPsychEngineStage)
            {
                // keep the group position (scripts may have moved boyfriendGroup/dadGroup/gfGroup)
                float gx = old.X - Arr(old.PsychCharacterOffset, 0), gy = old.Y - Arr(old.PsychCharacterOffset, 1);
                c.X = gx + Arr(c.PsychCharacterOffset, 0); c.Y = gy + Arr(c.PsychCharacterOffset, 1);
            }
            float[] stageCam = slot == 0 ? _pendingBfCamOffsets : slot == 2 ? _pendingGfCamOffsets : _pendingDadCamOffsets;
            var cc = c.JsonData?.CameraOffsets;
            c.CameraOffsets = new[] { Arr(cc, 0) + Arr(stageCam, 0), Arr(cc, 1) + Arr(stageCam, 1) };
            if (old != null) c.Alpha = old.Alpha;
            if (slot == 0) _boyfriend = c; else if (slot == 2) _girlfriend = c; else _opponent = c;
            Console.WriteLine($"[PSYCH-EVENT] Change Character: slot={slot} -> {newName} at ({c.X},{c.Y})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PSYCH-EVENT] Change Character failed ({newName}): {ex.Message}");
        }
    }

    /// <summary>Psych addCharacterToList: characters of every "Change Character" event are created at song start
    /// (eventPushed), so the swap does not stall (a 1.3 s load used to skip stepHit 405 in VS Majin).</summary>
    private readonly System.Collections.Generic.Dictionary<string, Character> _psychPreloadedChars = new();

    private Character BuildPsychCharacter(int slot, string newName)
    {
        var c = new Character(ResolveCharacterSprite(newName), 0, 0);
        c.LoadJsonData(Assets, newName);
        c.SetFlipForRole(slot == 0);
        c.StepCrochet = (float)_conductor.StepCrochet;
        c.LoadSprites(Game);
        return c;
    }

    internal void PreloadPsychChangeCharacters()
    {
        if (_chart?.Events == null) return;
        foreach (var ev in _chart.Events)
        {
            if (!string.Equals(ev.Name, "Change Character", StringComparison.OrdinalIgnoreCase)) continue;
            var (v1, v2) = GetPsychEventValues(ev.Value);
            string target = (v1 ?? "").Trim().ToLowerInvariant(), name = (v2 ?? "").Trim();
            if (name.Length == 0) continue;
            int slot = target switch { "gf" or "girlfriend" or "2" => 2, "dad" or "opponent" or "1" => 1, "bf" or "boyfriend" or "0" or "player" => 0, _ => int.TryParse(target, out int n) ? n : 0 };
            string key = slot + "|" + name.ToLowerInvariant();
            if (_psychPreloadedChars.ContainsKey(key)) continue;
            try { _psychPreloadedChars[key] = BuildPsychCharacter(slot, name); }
            catch (Exception ex) { Console.WriteLine($"[PSYCH-EVENT] preload {name} failed: {ex.Message}"); }
        }
    }
}
