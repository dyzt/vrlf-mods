using System.Text;
using System.Text.Json;
using Xunit;

namespace VrlfMods.Tests;

public class ConfigControllerTests
{
    static AppPaths TempPaths() => new(Path.Combine(Path.GetTempPath(), "vrlf-cc-" + Guid.NewGuid().ToString("N")));

    static (ModManager mm, string game, ReceiptStore store) BuildInstalled(AppPaths paths)
    {
        var game = Path.Combine(Path.GetTempPath(), "ccgame-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "demo.cfg"), "# help text\nHideThing=true\n");

        var regJson = Encoding.UTF8.GetBytes(
            """{ "schema":1, "vigembus":{"repo":"r","version":"v"}, "mods":[ {"id":"demo","name":"Demo","version":"1.0","zip":"z","sha256":"h","games":[{"appid":1,"name":"Demo Game"}],"requiresVigembusForCoop":false,"notes":null, "config":{"file":"demo.cfg","format":"kv","toggles":[{"key":"HideThing","label":"Hide thing"}]}} ] }""");
        var http = new FakeHttpFetcher(new() { [RegistryLoader.RawBase + "/mods.json"] = regJson });
        var store = new ReceiptStore(paths);
        store.Save(new Receipt("demo", "1.0", "1", game, new(), new(), "t"));   // mark installed
        var mm = new ModManager(new RegistryLoader(http, paths), new GameLocator(new SteamLocatorStub(1, game), new GamePathStore(paths)),
            new Installer(http, paths, store), store, new Vigem(new FakeServiceDetector(true), http, new FakeLauncher(), paths));
        return (mm, game, store);
    }

    [Fact] public async Task GetConfig_reads_current_toggle_state()
    {
        var paths = TempPaths();
        var (mm, _, _) = BuildInstalled(paths);
        var cfg = await mm.GetConfig("demo", "1");
        Assert.NotNull(cfg);
        var t = cfg!.Toggles.Single();
        Assert.Equal("HideThing", t.Key);
        Assert.True(t.On);
        Assert.True(t.Available);
    }

    [Fact] public async Task SetToggle_writes_the_cfg()
    {
        var paths = TempPaths();
        var (mm, game, _) = BuildInstalled(paths);
        var r = await mm.SetToggle("demo", "1", "HideThing", false);
        Assert.True(r.Ok, r.Message);
        Assert.Contains("HideThing=false", File.ReadAllText(Path.Combine(game, "demo.cfg")));
    }

    [Fact] public async Task GetConfig_null_when_not_installed()
    {
        var paths = TempPaths();
        var (mm, _, store) = BuildInstalled(paths);
        store.Delete("demo", "1");
        Assert.Null(await mm.GetConfig("demo", "1"));
    }

    [Fact] public async Task Dispatch_config_json_emits_toggles_object()
    {
        var paths = TempPaths();
        var (mm, _, _) = BuildInstalled(paths);
        var orig = Console.Out; var sw = new StringWriter(); Console.SetOut(sw);
        int code;
        try { code = await Program.Run(Cli.Parse(new[] { "config", "demo", "--json" }), mm); }
        finally { Console.SetOut(orig); }
        Assert.Equal(0, code);
        using var doc = JsonDocument.Parse(sw.ToString());
        Assert.True(doc.RootElement.TryGetProperty("toggles", out var toggles));
        Assert.Equal(1, toggles.GetArrayLength());
    }

    [Fact] public async Task Dispatch_config_set_flips_and_exits_0()
    {
        var paths = TempPaths();
        var (mm, game, _) = BuildInstalled(paths);
        var orig = Console.Out; var sw = new StringWriter(); Console.SetOut(sw);
        int code;
        try { code = await Program.Run(Cli.Parse(new[] { "config", "demo", "set", "HideThing", "off" }), mm); }
        finally { Console.SetOut(orig); }
        Assert.Equal(0, code);
        Assert.Contains("HideThing=false", File.ReadAllText(Path.Combine(game, "demo.cfg")));
    }
}
