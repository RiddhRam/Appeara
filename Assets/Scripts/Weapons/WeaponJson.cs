using Newtonsoft.Json;

/// <summary>Bounded JSON for recursive behavior trees; never instantiate types named by input.</summary>
public static class WeaponJson
{
    private static readonly JsonSerializerSettings Settings=new JsonSerializerSettings {
        MaxDepth=32, TypeNameHandling=TypeNameHandling.None,
        ReferenceLoopHandling=ReferenceLoopHandling.Error
    };
    public static string ToJson(object value) => JsonConvert.SerializeObject(value,Settings);
    public static T FromJson<T>(string json) => JsonConvert.DeserializeObject<T>(json,Settings);
}
