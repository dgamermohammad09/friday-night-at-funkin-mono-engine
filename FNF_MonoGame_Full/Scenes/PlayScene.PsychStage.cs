using System.Collections.Generic;
using System;
using System.IO;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Psych Engine built-in stage 'stage' for Psych mod songs (PlayState.create: StageData.getStageFile(curStage) +
/// `case 'stage': new StageWeek1()`). Psych's own stage.json (bf 770,100 / gf 400,130 / dad 100,100, zoom 0.9) and
/// StageWeek1 art replace the V-Slice mainStage ONLY for mod songs; base-game (V-Slice) songs never take this path.
/// </summary>
public partial class PlayScene
{
    /// <summary>True for a Psych mod song on the built-in stage 'stage' (a mod's own stages/stage.json still wins).</summary>
    private bool PsychBuiltinStageActive => PsychNotes && (_chart?.Stage ?? "stage") == "stage";

    /// <summary>Roots searched for Psych stages/&lt;name&gt;.json: mod roots first, then Psych's bundled
    /// Content/data/psych (only for the built-in 'stage').</summary>
    private IEnumerable<string> PsychStageJsonRoots()
    {
        foreach (var r in GetPsychModRootPaths()) yield return r;
        if (PsychBuiltinStageActive)
            yield return Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot, "data", "psych");
    }

    /// <summary>StageWeek1.create props (V-Slice prop format) for LoadStageTextures.</summary>
    private StageJsonData LoadPsychBuiltinStageProps()
    {
        if (!PsychBuiltinStageActive) return null;
        var j = Assets.LoadJson<StageJsonData>("data/psych/stages/stage_props");
        if (j != null)
        {
            _stageJsonFilePath = Path.Combine(FNF_MonoGame.Engine.AssetManager.ContentRoot, "data", "psych", "stages", "stage_props.json");
            System.Console.WriteLine("[MODS] Psych built-in stage 'stage' (StageWeek1) props");
        }
        return j;
    }
}
