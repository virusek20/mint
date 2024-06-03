using System.Runtime.Serialization;
using System.Text.Json;

namespace MetalMintSolid.Extensions;

public static class DeepCloneExtensions
{
    private static readonly JsonSerializerOptions _options = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters =
        {
            new System.Text.Json.Serialization.JsonStringEnumConverter()
        },
        IncludeFields = true
    };

    public static T CreateDeepCopy<T>(this T obj) where T : class
    {
        var serialized = JsonSerializer.Serialize(obj, _options);
        return JsonSerializer.Deserialize<T>(serialized) ?? throw new SerializationException("Failed to clone object");
    }
}
