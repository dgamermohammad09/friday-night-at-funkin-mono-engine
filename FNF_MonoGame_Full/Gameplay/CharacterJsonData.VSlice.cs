using Newtonsoft.Json;

namespace FNF_MonoGame.Gameplay;

/// <summary>V-Slice character JSON "healthIcon" block (funkin.data.character.CharacterData.HealthIconData).</summary>
public class VSliceHealthIconData
{
    [JsonProperty("id")] public string Id { get; set; }
    [JsonProperty("scale")] public float? Scale { get; set; }
    [JsonProperty("flipX")] public bool FlipX { get; set; }
    [JsonProperty("isPixel")] public bool IsPixel { get; set; }
    [JsonProperty("offsets")] public float[] Offsets { get; set; }
}

public partial class CharacterJsonData
{
    [JsonProperty("healthIcon")] public VSliceHealthIconData VSliceHealthIcon { get; set; }
}
