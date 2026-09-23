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

        if (r.Emulators is { Count: > 0 } emus)
        {
            Console.WriteLine("Emulators:");
            EmulatorListText(emus);
        }
    }

    public static void EmulatorListText(List<EmulatorStatus> all)
    {
        foreach (var e in all)
        {
            Console.WriteLine($"  {e.Id,-12} {e.Name}  [{TuiModel.EmulatorState(e).Text}]");
            Console.WriteLine($"      {FolderLine(e)}");
        }
    }

    public static void EmulatorStatusText(EmulatorStatus e)
    {
        Console.WriteLine($"{e.Name}  [{TuiModel.EmulatorState(e).Text}]");
        Console.WriteLine($"  {FolderLine(e)}");
        foreach (var o in e.Options)
        {
            var state = o.InstalledVersion is null ? $"v{o.Version}"
                : o.InstalledVersion == o.Version ? $"installed v{o.InstalledVersion}"
                : $"installed v{o.InstalledVersion} (update to v{o.Version})";
            var needs = o.Requires is null ? "" : $"  (needs {o.Requires})";
            Console.WriteLine($"  [{(o.InstalledVersion is null ? " " : "x")}] {o.Id,-12} {o.Label}  {state}{needs}");
        }
        var driver = e.Needs switch { "vigembus" => "ViGEmBus", "virtualgun" => "Virtual Lightgun", _ => null };
        if (driver is not null) Console.WriteLine($"  Needs {driver}: {(e.NeedsMet ? "installed" : "not installed")}");
        if (e.Profile is not null) Console.WriteLine($"  VRLF profile: {e.Profile}");
        if (e.Notes is not null) Console.WriteLine($"  {e.Notes}");
    }

    static string FolderLine(EmulatorStatus e)
    {
        if (e.Folder is null)
            return e.Suggested is null
                ? "settings folder: not chosen. Set it with: vrlf-mods emulator path <id> <folder>"
                : $"settings folder: not chosen (found your main install at {e.Suggested})";
        if (!e.FolderExists) return $"settings folder: {e.Folder} (no longer there)";
        return e.Locked ? $"settings folder: {e.Folder} (installed here)" : $"settings folder: {e.Folder}";
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
