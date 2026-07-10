using System.Text.Json.Serialization;

namespace VrlfMods;

public record GameRef(long Appid, string Name);
public record VigemInfo(string Repo, string Version);

public record ConfigToggle(
    string Label,
    string? Key = null,
    string? Section = null,
    [property: JsonPropertyName("type")] string? Type = null,   // null/"cfg" | "patch"
    string? Help = null,
    string? Patch = null,
    string? Target = null);

public record ConfigManifest(string? File, string Format, List<ConfigToggle> Toggles);

public record ModEntry(
    string Id, string Name, string Version, string Zip, string Sha256,
    List<GameRef> Games,
    [property: JsonPropertyName("requiresVigembusForCoop")] bool RequiresVigembusForCoop,
    string? Notes,
    ConfigManifest? Config = null);

public record ModRegistry(int Schema, VigemInfo Vigembus, List<ModEntry> Mods)
{
    public ModEntry? Find(string id) =>
        Mods.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
}
