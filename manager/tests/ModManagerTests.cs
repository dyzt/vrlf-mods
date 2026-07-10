using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Xunit;

namespace VrlfMods.Tests;

public class ModManagerTests
{
    static AppPaths TempPaths() =>
        new(Path.Combine(Path.GetTempPath(), "vrlf-mm-" + Guid.NewGuid().ToString("N")));

    static byte[] Registry(string zipUrlHashPairsJsonMods) => Encoding.UTF8.GetBytes(
        $$"""{ "schema":1, "vigembus":{"repo":"nefarius/ViGEmBus","version":"v1.22.0"}, "mods":[ {{zipUrlHashPairsJsonMods}} ] }""");

    static byte[] MakeZip(string name, string content)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        { using var w = new StreamWriter(zip.CreateEntry(name).Open()); w.Write(content); }
        return ms.ToArray();
    }

    // Assemble a ModManager whose registry names one mod, backed by a fake game dir + fake http.
    static (ModManager mm, string game, ReceiptStore store, string modId) Build(AppPaths paths, out FakeHttpFetcher http)
    {
        var game = Path.Combine(Path.GetTempPath(), "mmgame-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(game);
        var zip = MakeZip("m.dll", "M");
        var hash = Installer.Sha256Hex(zip);
        var modJson = $$"""
          { "id":"demo","name":"Demo","version":"1.0","zip":"mods/demo/dist/demo.zip",
            "sha256":"{{hash}}","games":[{"appid":1,"name":"Demo Game"}],"requiresVigembusForCoop":false,"notes":null }
        """;
        var reg = Registry(modJson);
        var f = new FakeHttpFetcher(new()
        {
            [RegistryLoader.RawBase + "/mods.json"] = reg,
            [RegistryLoader.ZipUrl(new ModEntry("demo","Demo","1.0","mods/demo/dist/demo.zip",hash,new(){new GameRef(1,"Demo Game")},false,null))] = zip,
        });
        http = f;
        var loader = new RegistryLoader(f, paths);
        var store = new ReceiptStore(paths);
        // Steam locator that resolves appid 1 → our fake game dir:
        var steam = new SteamLocatorStub(1, game);
        var installer = new Installer(f, paths, store);
        var vigem = new Vigem(new FakeServiceDetector(true), f, new FakeLauncher(), paths);
        return (new ModManager(loader, steam, installer, store, vigem), game, store, "demo");
    }

    [Fact]
    public async Task List_reports_detected_but_not_installed()
    {
        var paths = TempPaths();
        var (mm, _, _, _) = Build(paths, out _);
        var rep = await mm.List();
        var demo = rep.Mods.Single();
        Assert.Equal("1.0", demo.Version);
        Assert.Null(demo.InstalledVersion);
        Assert.True(demo.Games.Single().Detected);
    }

    [Fact]
    public async Task Install_then_List_shows_installed_version()
    {
        var paths = TempPaths();
        var (mm, game, _, _) = Build(paths, out _);
        var act = await mm.Install("demo", null, null);
        Assert.True(act.Ok, act.Results[0].Message);
        Assert.True(File.Exists(Path.Combine(game, "m.dll")));
        var rep = await mm.List();
        Assert.Equal("1.0", rep.Mods.Single().InstalledVersion);
    }

    [Fact]
    public async Task Install_unknown_id_fails()
    {
        var paths = TempPaths();
        var (mm, _, _, _) = Build(paths, out _);
        var act = await mm.Install("nope", null, null);
        Assert.False(act.Ok);
    }

    [Fact]
    public async Task Facade_uninstall_after_install_removes_files_and_receipt()
    {
        var paths = TempPaths();
        var (mm, game, store, _) = Build(paths, out _);
        await mm.Install("demo", null, null);
        var act = await mm.Uninstall("demo", null);
        Assert.True(act.Ok, act.Results.Count > 0 ? act.Results[0].Message : "");
        Assert.False(File.Exists(Path.Combine(game, "m.dll")));
        Assert.Null(store.Load("demo", "1"));
    }

    [Fact]
    public async Task Facade_status_reflects_installed_version()
    {
        var paths = TempPaths();
        var (mm, _, _, _) = Build(paths, out _);
        await mm.Install("demo", null, null);
        var st = await mm.Status("demo");
        Assert.NotNull(st);
        Assert.Equal("1.0", st!.InstalledVersion);
    }

    [Fact]
    public async Task Facade_update_is_noop_when_registry_matches_installed()
    {
        var paths = TempPaths();
        var (mm, _, _, _) = Build(paths, out _);
        await mm.Install("demo", null, null);
        var act = await mm.Update("demo");
        Assert.True(act.Ok);
        Assert.Contains(act.Results, r => r.Message.Contains("up to date"));
    }

    [Fact]
    public async Task Dispatch_list_json_emits_single_object_and_exit_0()
    {
        var paths = TempPaths();
        var (mm, _, _, _) = Build(paths, out _);
        var orig = Console.Out;
        var sw = new StringWriter();
        Console.SetOut(sw);
        int code;
        try { code = await Program.Run(Cli.Parse(new[] { "list", "--json" }), mm); }
        finally { Console.SetOut(orig); }
        Assert.Equal(0, code);
        using var doc = JsonDocument.Parse(sw.ToString());        // one parseable object
        Assert.True(doc.RootElement.TryGetProperty("registrySource", out _));
    }

    [Fact]
    public async Task Menu_falls_back_to_list_when_output_redirected()
    {
        var paths = TempPaths();
        var (mm, _, _, _) = Build(paths, out _);
        var orig = Console.Out; var sw = new StringWriter(); Console.SetOut(sw);
        int code;
        try { code = await Program.Run(Cli.Parse(new[] { "menu" }), mm); }
        finally { Console.SetOut(orig); }
        Assert.Equal(0, code);
        Assert.Contains("Registry:", sw.ToString());     // printed the list, did not block on ReadKey
    }

    [Fact]
    public async Task Dispatch_status_unknown_json_returns_1_with_error_object()
    {
        var paths = TempPaths();
        var (mm, _, _, _) = Build(paths, out _);
        var orig = Console.Out;
        var sw = new StringWriter();
        Console.SetOut(sw);
        int code;
        try { code = await Program.Run(Cli.Parse(new[] { "status", "nope", "--json" }), mm); }
        finally { Console.SetOut(orig); }
        Assert.Equal(1, code);
        using var doc = JsonDocument.Parse(sw.ToString());
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
    }
}
