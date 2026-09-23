namespace VrlfMods;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var p = Cli.Parse(args);
        if (p.Error is not null) { Console.Error.WriteLine("error: " + p.Error); return 1; }

        var paths = AppPaths.Default();
        var http = new HttpFetcher();
        var loader = new RegistryLoader(http, paths);
        var vigem = new Vigem(new RegistryServiceDetector(), http, new ProcessLauncher(), paths);
        var virtualGun = new VirtualGun(new RegistryVirtualGunState(), http, new ElevatedRunner(), paths);
        var emuReceipts = new EmulatorReceiptStore(paths);
        var emulators = new EmulatorService(loader,
            new EmulatorFolders(new GamePathStore(paths), new SystemKnownFolders()),
            new EmulatorInstaller(http, paths, emuReceipts, new SystemProcessProbe()),
            emuReceipts, vigem, virtualGun);
        var mm = new ModManager(
            loader,
            new GameLocator(new SteamLocator(new RegistrySteamPaths()), new GamePathStore(paths)),
            new Installer(http, paths, new ReceiptStore(paths)),
            new ReceiptStore(paths),
            vigem,
            virtualGun: virtualGun,
            emulators: emulators);

        return await Run(p, mm);
    }

    internal static async Task<int> Run(ParsedArgs p, ModManager mm)
    {
        try
        {
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
                    if (r is null)
                    {
                        if (p.Json)
                            Output.Json(new ActionReport(false, "status",
                                new() { new OpResult(false, "", $"unknown mod id '{p.Id}'") }),
                                VrlfJson.Default.ActionReport);
                        else Console.Error.WriteLine($"unknown mod id '{p.Id}'");
                        return 1;
                    }
                    if (p.Json) Output.Json(r, VrlfJson.Default.ModStatus);
                    else Output.ListText(new ListReport("", new() { r }));
                    return 0;
                }
                case "install":   return Report(await mm.Install(p.Id!, p.Appid, p.Path), p.Json);
                case "uninstall": return Report(await mm.Uninstall(p.Id!, p.Appid), p.Json);
                case "update":    return Report(await mm.Update(p.Id), p.Json);
                case "vigembus":  return Report(await mm.EnsureVigem(), p.Json);
                case "virtualgun":
                    return Report(p.Id switch
                    {
                        "install" => await mm.VirtualGunInstall(),
                        "uninstall" => await mm.VirtualGunUninstall(),
                        _ => await mm.VirtualGunStatus(),
                    }, p.Json);
                case "config":    return await Config(p, mm);
                case "path":      return await GamePath(p, mm);
                case "emulator": return await Emulator(p, mm);
                default: Console.Error.WriteLine("error: unknown command"); return 1;
            }
        }
        catch (Exception ex)
        {
            if (p.Json)
                Output.Json(new ActionReport(false, p.Command,
                    new() { new OpResult(false, "", ex.Message) }), VrlfJson.Default.ActionReport);
            else
                Console.Error.WriteLine("error: " + ex.Message);
            return 1;
        }
    }

    // path <id>            — show which folder we are using, and who chose it
    // path <id> <dir>      — point the manager at a game Steam can't find (or a second copy)
    // path <id> --clear    — forget it and go back to Steam detection
    private static async Task<int> GamePath(ParsedArgs p, ModManager mm)
    {
        if (p.Clear) return Report(await mm.ClearGamePath(p.Id!, p.Appid), p.Json);
        if (p.Path is not null) return Report(await mm.SetGamePath(p.Id!, p.Appid, p.Path), p.Json);

        var g = await mm.GamePath(p.Id!, p.Appid);
        if (g is null) return Fail("path", "", $"unknown mod id '{p.Id}'", p.Json);
        if (p.Json) Output.Json(g, VrlfJson.Default.GameStatus);
        else Console.WriteLine(Output.GamePathText(g));
        return 0;
    }

    private static async Task<int> Emulator(ParsedArgs p, ModManager mm)
    {
        var emu = mm.Emulators;
        switch (p.Sub)
        {
            case "list":
            {
                var all = await emu.List();
                if (p.Json) Output.Json(new EmulatorListReport(all), VrlfJson.Default.EmulatorListReport);
                else Output.EmulatorListText(all);
                return 0;
            }
            case "status":
            {
                var s = await emu.Status(p.Id!);
                if (s is null) return Fail("emulator", "", $"unknown emulator id '{p.Id}'", p.Json);
                if (p.Json) Output.Json(s, VrlfJson.Default.EmulatorStatus); else Output.EmulatorStatusText(s);
                return 0;
            }
            case "path":
                if (p.Clear) return Report(await emu.ClearFolder(p.Id!), p.Json);
                if (p.Path is not null) return Report(await emu.SetFolder(p.Id!, p.Path), p.Json);
                goto case "status";
            case "install": return Report(await emu.Install(p.Id!, p.Value), p.Json);
            case "uninstall": return Report(await emu.Uninstall(p.Id!, p.Value), p.Json);
            case "update": return Report(await emu.Update(p.Id!), p.Json);
            case "reapply": return Report(await emu.Reapply(p.Id!), p.Json);
            default: return Fail("emulator", "", "unknown emulator subcommand", p.Json);
        }
    }

    private static int Report(ActionReport r, bool json)
    {
        if (json) Output.Json(r, VrlfJson.Default.ActionReport); else Output.ActionText(r);
        return r.Ok ? 0 : 1;
    }

    private static async Task<int> Config(ParsedArgs p, ModManager mm)
    {
        var (key, err) = await ResolveGameKey(mm, p);
        if (key is null) return Fail("config", key: "", err!, p.Json);

        if (p.Sub == "set")
        {
            var res = await mm.SetToggle(p.Id!, key, p.Value!, p.FlagOn);
            return Report(new ActionReport(res.Ok, "config", new() { res }), p.Json);
        }

        var cfg = await mm.GetConfig(p.Id!, key);
        if (cfg is null)
            return Fail("config", key, $"{p.Id} has no configurable options or is not installed for game {key}", p.Json);

        if (p.Json) Output.Json(cfg, VrlfJson.Default.ModConfig); else Output.ConfigText(cfg);
        return 0;
    }

    // Resolve which installed game's config to act on: explicit --game wins; a mod with a
    // single game (or a single installed game) is unambiguous; otherwise require --game.
    private static async Task<(string? key, string? err)> ResolveGameKey(ModManager mm, ParsedArgs p)
    {
        if (p.Appid is not null) return (p.Appid.Value.ToString(), null);
        var st = await mm.Status(p.Id!);
        if (st is null) return (null, $"unknown mod id '{p.Id}'");
        var installed = st.Games.Where(g => g.Installed).ToList();
        if (installed.Count == 1) return (installed[0].Appid.ToString(), null);
        if (installed.Count == 0)
            return st.Games.Count == 1
                ? (st.Games[0].Appid.ToString(), null)
                : (null, $"{p.Id} is not installed; nothing to configure");
        return (null, $"{p.Id} is installed for multiple games — pass --game <appid>");
    }

    private static int Fail(string cmd, string key, string msg, bool json)
    {
        if (json)
            Output.Json(new ActionReport(false, cmd, new() { new OpResult(false, key, msg) }),
                VrlfJson.Default.ActionReport);
        else Console.Error.WriteLine("error: " + msg);
        return 1;
    }

    private static async Task<int> Menu(ModManager mm)
    {
        // Piped / headless: print the plain list and exit rather than entering the key loop.
        // The TUI needs an interactive console for both ReadKey and screen redraws.
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        { Output.ListText(await mm.List()); return 0; }
        return await Tui.Run(mm);
    }
}
