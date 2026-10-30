using System;
using System.Reflection;

namespace FNF_MonoGame.Engine;

/// <summary>
/// JSON for save files written at runtime (Psych prefs, achievements, highscores, Lua saves, replays).
/// Desktop/Android: System.Text.Json (unchanged file format). UWP/Xbox: .NET Native 2.2 miscompiles System.Text.Json's
/// reflection serializer for generic collections (ArrayConverter&lt;uint[][]&gt; -> access violation in
/// RhpCopyAnyWithWriteBarrier = uncatchable, the app just closes: toggling any Options row on Xbox closed the game,
/// and every replay save at the end of a song did too). There the same names are written/read with Newtonsoft.Json,
/// honouring System.Text.Json's [JsonPropertyName] / [JsonIgnore] so the files stay identical.
/// </summary>
public static class JsonCompat
{
#if WINDOWS_UAP
    private sealed class StjAttributeResolver : Newtonsoft.Json.Serialization.DefaultContractResolver
    {
        protected override Newtonsoft.Json.Serialization.JsonProperty CreateProperty(MemberInfo member, Newtonsoft.Json.MemberSerialization ms)
        {
            var p = base.CreateProperty(member, ms);
            var name = member.GetCustomAttribute<System.Text.Json.Serialization.JsonPropertyNameAttribute>();
            if (name != null) p.PropertyName = name.Name;
            if (member.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() != null) p.Ignored = true;
            if (member is FieldInfo) p.Ignored = true;   // System.Text.Json skips fields by default
            return p;
        }
    }
    private static readonly Newtonsoft.Json.JsonSerializerSettings Settings = new()
    {
        ContractResolver = new StjAttributeResolver(),
        ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace,   // STJ replaces collections, never appends to defaults
        NullValueHandling = Newtonsoft.Json.NullValueHandling.Include,
    };
#endif

    public static string Serialize<T>(T value, bool indented = true)
    {
#if WINDOWS_UAP
        return Newtonsoft.Json.JsonConvert.SerializeObject(value, indented ? Newtonsoft.Json.Formatting.Indented : Newtonsoft.Json.Formatting.None, Settings);
#else
        return System.Text.Json.JsonSerializer.Serialize(value, new System.Text.Json.JsonSerializerOptions { WriteIndented = indented });
#endif
    }

    public static T Deserialize<T>(string json)
    {
#if WINDOWS_UAP
        return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json, Settings);
#else
        return System.Text.Json.JsonSerializer.Deserialize<T>(json);
#endif
    }
}
