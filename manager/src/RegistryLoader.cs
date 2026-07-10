using System.Text.Json;

namespace VrlfMods;

public sealed class RegistryLoader
{
    public const string RawBase = "https://raw.githubusercontent.com/dyzt/vrlf-mods/main";

    private readonly IHttpFetcher _http;
    private readonly AppPaths _paths;

    public RegistryLoader(IHttpFetcher http, AppPaths paths) { _http = http; _paths = paths; }

    public static string ZipUrl(ModEntry mod) => $"{RawBase}/{mod.Zip}";

    public async Task<(ModRegistry reg, string source)> Load()
    {
        var bytes = await _http.TryGet(RawBase + "/mods.json");
        if (bytes is not null && TryParse(bytes, out var net))
        {
            Directory.CreateDirectory(_paths.ModsRoot);
            await File.WriteAllBytesAsync(_paths.RegistryCachePath, bytes);
            return (net!, "network");
        }
        if (File.Exists(_paths.RegistryCachePath)
            && TryParse(await File.ReadAllBytesAsync(_paths.RegistryCachePath), out var cached))
            return (cached!, "cache");

        using var s = typeof(RegistryLoader).Assembly.GetManifestResourceStream("mods.json")!;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return (Parse(ms.ToArray()), "embedded");
    }

    private static bool TryParse(byte[] b, out ModRegistry? reg)
    {
        try { reg = Parse(b); return true; }
        catch { reg = null; return false; }
    }

    private static ModRegistry Parse(byte[] b) =>
        JsonSerializer.Deserialize(b, VrlfJson.Default.ModRegistry)
            ?? throw new InvalidDataException("mods.json parsed to null");
}
