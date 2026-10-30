using System;

namespace FNF_MonoGame.Scenes;

/// <summary>
/// Whether the song/stage gets a Girlfriend at all (no default GF for mods that do not use her).
///  - Psych (PlayState.create): `if (!stageData.hide_girlfriend) { if (gfVersion empty) gfVersion = 'gf'; gf = new Character(...) }`
///    -> hide_girlfriend:true = no gf object at all (gf == null; scripts' gf.* calls do nothing, 'Change Character' on gf is skipped).
///    An empty/missing gfVersion still gives 'gf' (Song.hx convert + PlayState fix "for the Chart Editor").
///  - V-Slice (PlayState.initCharacters): `fetchCharacter(currentCharacterData.girlfriend)`; SongCharacterData.girlfriend
///    defaults to '' and '' (or an unknown id) gives no GF.
/// </summary>
public partial class PlayScene
{
    private bool GfSuppressedBySongOrStage(string gfName)
    {
        if (PsychNotes && _psychStageHideGf)
        {
            Console.WriteLine($"[GF] stage '{_chart?.Stage}' has hide_girlfriend: true -> no girlfriend");
            return true;
        }
        if (string.IsNullOrWhiteSpace(gfName))
        {
            Console.WriteLine("[GF] chart girlfriend is empty -> no girlfriend");
            return true;
        }
        return false;
    }
}
