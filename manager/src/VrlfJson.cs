using System.Text.Json.Serialization;

namespace VrlfMods;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ModRegistry))]
[JsonSerializable(typeof(ConfigManifest))]
[JsonSerializable(typeof(ConfigToggle))]
[JsonSerializable(typeof(Receipt))]
[JsonSerializable(typeof(ListReport))]
[JsonSerializable(typeof(ModStatus))]
[JsonSerializable(typeof(ModConfig))]
[JsonSerializable(typeof(ActionReport))]
[JsonSerializable(typeof(GameStatus))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(EmulatorReceipt))]
[JsonSerializable(typeof(PackageSettings))]
[JsonSerializable(typeof(EmulatorStatus))]
[JsonSerializable(typeof(EmulatorListReport))]
public partial class VrlfJson : JsonSerializerContext { }
