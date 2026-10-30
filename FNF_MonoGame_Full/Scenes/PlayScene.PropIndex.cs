using System;

namespace FNF_MonoGame.Scenes;

public partial class PlayScene
{
    /// <summary>
    /// Index of the first static prop with this name (-1 if none). Stage scripts (spooky lightning bgLight/stairsLight,
    /// limo fastCar, philly train/lights) used to cache an index at stage load, but _staticProps is re-sorted afterwards
    /// (A-Bot/Nene props, Stage.refresh), so the cached index pointed at another prop: the lightning faded the A-Bot
    /// stereoBG/eye whites instead of the stairs light (A-Bot screen vanished after a strike, stairs never lit).
    /// </summary>
    private int StaticPropIndexByName(string name)
    {
        var list = _staticProps;
        if (list == null) return -1;
        for (int i = 0; i < list.Count; i++)
            if (string.Equals(list[i].Name, name, StringComparison.Ordinal)) return i;
        return -1;
    }
}
