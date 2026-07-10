using System.Text.Json.Serialization;

namespace VrlfMods;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ModRegistry))]
[JsonSerializable(typeof(Receipt))]
public partial class VrlfJson : JsonSerializerContext { }
