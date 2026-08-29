using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace VrlfMods;

public static class Output
{
    public static void Json<T>(T report, JsonTypeInfo<T> typeInfo)
        => Console.WriteLine(JsonSerializer.Serialize(report, typeInfo));

    public static void ListText(ListReport r)
    {
        Console.WriteLine($"Registry: {r.RegistrySource}");
        foreach (var m in r.Mods)
        {
            var state = m.InstalledVersion is null ? "not installed"
                       : m.InstalledVersion == m.Version ? $"installed v{m.InstalledVersion}"
                       : $"installed v{m.InstalledVersion} (update to v{m.Version})";
            Console.WriteLine($"  {m.Id,-12} {m.Name}  [{state}]");
            foreach (var g in m.Games)
                Console.WriteLine($"      {(g.Detected ? "✓" : "·")} {g.Name} (appid {g.Appid}){(g.Detected ? "" : " — not found")}");
        }
    }

    /// <summary>Reads back where a game lives and who chose that folder.</summary>
    public static string GamePathText(GameStatus g)
    {
        if (g.Detected)
            return $"{g.Name}: {g.Path}  ({(g.Manual ? "folder you chose" : "found via Steam")})";
        if (g.Manual)
            return $"{g.Name}: {g.Path}  — the folder you chose is no longer there";
        return $"{g.Name}: not found — point at it with:  vrlf-mods path <id> <game dir>";
    }

    public static void ActionText(ActionReport r)
    {
        foreach (var res in r.Results)
            Console.WriteLine($"  [{(res.Ok ? "ok" : "FAIL")}] {res.Message}");
    }

    public static void ConfigText(ModConfig c)
    {
        Console.WriteLine($"{c.ModId} config ({c.Toggles.Count} options):");
        foreach (var t in c.Toggles)
            Console.WriteLine($"  [{(t.On ? "x" : " ")}] {t.Key,-20} {t.Label}{(t.Available ? "" : $"  ({t.Note})")}");
    }
}
