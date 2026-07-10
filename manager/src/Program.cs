namespace VrlfMods;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var p = Cli.Parse(args);
        if (p.Error is not null) { Console.Error.WriteLine("error: " + p.Error); return 1; }

        var paths = AppPaths.Default();
        var http = new HttpFetcher();
        var mm = new ModManager(
            new RegistryLoader(http, paths),
            new SteamLocator(new RegistrySteamPaths()),
            new Installer(http, paths, new ReceiptStore(paths)),
            new ReceiptStore(paths),
            new Vigem(new RegistryServiceDetector(), http, new ProcessLauncher(), paths));

        switch (p.Command)
        {
            case "menu": return await Menu(mm);
            case "list":
            {
                var r = await mm.List();
                if (p.Json) Output.Json(r, VrlfJson.Default.ListReport); else Output.ListText(r);
                return 0;
            }
            case "status":
            {
                var r = await mm.Status(p.Id!);
                if (r is null) { Console.Error.WriteLine($"unknown mod id '{p.Id}'"); return 1; }
                if (p.Json) Output.Json(r, VrlfJson.Default.ModStatus);
                else Output.ListText(new ListReport("", new() { r }));
                return 0;
            }
            case "install":   return Report(await mm.Install(p.Id!, p.Appid, p.Path), p.Json);
            case "uninstall": return Report(await mm.Uninstall(p.Id!, p.Appid), p.Json);
            case "update":    return Report(await mm.Update(p.Id), p.Json);
            case "vigembus":  return Report(await mm.EnsureVigem(), p.Json);
            default: Console.Error.WriteLine("error: unknown command"); return 1;
        }
    }

    private static int Report(ActionReport r, bool json)
    {
        if (json) Output.Json(r, VrlfJson.Default.ActionReport); else Output.ActionText(r);
        return r.Ok ? 0 : 1;
    }

    private static async Task<int> Menu(ModManager mm)
    {
        var list = await mm.List();
        Output.ListText(list);
        Console.WriteLine();
        Console.WriteLine("Commands: install <id> | uninstall <id> | update | vigembus");
        Console.Write("> ");
        var line = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(line)) return 0;
        return await Main(line.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
