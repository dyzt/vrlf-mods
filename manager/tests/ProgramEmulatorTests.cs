using System.Text.Json;
using Xunit;

namespace VrlfMods.Tests;

public class ProgramEmulatorTests
{
    static (ModManager mm, string docs) Build()
    {
        var paths = EmuFixture.TempPaths();
        var docs = EmuFixture.TempFolder("Documents");
        var zip = EmuFixture.Package("""{ "edits": [ { "file": "emu.ini", "format": "ini", "section": "A", "key": "k", "value": "v" } ] }""");
        var opt = EmuFixture.Option("base", zip);
        var emu = EmuFixture.Entry(opt);
        var reg = new ModRegistry(1, new VigemInfo("nefarius/ViGEmBus", "v1.22.0"), new(), null, new() { emu });
        var http = new FakeHttpFetcher(new()
        {
            [RegistryLoader.RawBase + "/mods.json"] = JsonSerializer.SerializeToUtf8Bytes(reg, VrlfJson.Default.ModRegistry),
            [EmulatorInstaller.ZipUrl(opt)] = zip,
        });
        var loader = new RegistryLoader(http, paths);
        var vigem = new Vigem(new FakeServiceDetector(true), http, new FakeLauncher(), paths);
        var receipts = new EmulatorReceiptStore(paths);
        var svc = new EmulatorService(loader,
            new EmulatorFolders(new GamePathStore(paths), new FakeKnownFolders(new() { ["DOCUMENTS"] = docs })),
            new EmulatorInstaller(http, paths, receipts, new FakeProcessProbe()), receipts, vigem);
        var mm = new ModManager(loader, new GameLocator(new SteamLocatorStub(-1, ""), new GamePathStore(paths)),
            new Installer(http, paths, new ReceiptStore(paths)), new ReceiptStore(paths), vigem, emulators: svc);
        return (mm, docs);
    }

    static async Task<(int code, string output)> Run(ModManager mm, params string[] args)
    {
        var old = Console.Out;
        var sw = new StringWriter();
        Console.SetOut(sw);
        try { return (await Program.Run(Cli.Parse(args), mm), sw.ToString()); }
        finally { Console.SetOut(old); }
    }

    [Fact]
    public async Task List_json_is_one_object_with_an_emulators_array()
    {
        var (mm, _) = Build();
        var (code, output) = await Run(mm, "emulator", "list", "--json");
        Assert.Equal(0, code);
        using var doc = JsonDocument.Parse(output);
        Assert.Equal("testemu", doc.RootElement.GetProperty("emulators")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Path_then_install_then_uninstall_round_trips_through_the_cli()
    {
        var (mm, _) = Build();
        var dir = EmuFixture.TempFolder("TestEmu");
        EmuFixture.Write(dir, "emu.ini", "[A]\r\nk = old\r\n");

        Assert.Equal(1, (await Run(mm, "emulator", "install", "testemu")).code);   // no folder yet
        Assert.Equal(0, (await Run(mm, "emulator", "path", "testemu", dir)).code);
        Assert.Equal(0, (await Run(mm, "emulator", "install", "testemu")).code);
        Assert.Equal("[A]\r\nk = v\r\n", EmuFixture.Read(dir, "emu.ini"));
        var (code, text) = await Run(mm, "emulator", "status", "testemu");
        Assert.Equal(0, code);
        Assert.Contains("[x] base", text);
        Assert.Equal(0, (await Run(mm, "emulator", "uninstall", "testemu")).code);
        Assert.Equal("[A]\r\nk = old\r\n", EmuFixture.Read(dir, "emu.ini"));
    }

    [Fact]
    public async Task Status_of_an_unknown_emulator_fails()
    {
        var (mm, _) = Build();
        Assert.Equal(1, (await Run(mm, "emulator", "status", "nope")).code);
    }

    // Mutation-check survivor (Task 8 step 7d): "emulator path <id>" with neither a folder nor
    // --clear must fall through to the same status text "emulator status" prints, not just
    // succeed silently — a `return 0;` in place of the `goto case "status";` slipped past every
    // test above because none of them read this specific path-with-no-args form.
    [Fact]
    public async Task Path_with_no_folder_or_clear_shows_status()
    {
        var (mm, _) = Build();
        var dir = EmuFixture.TempFolder("TestEmuPathStatus");
        EmuFixture.Write(dir, "emu.ini", "[A]\r\nk = old\r\n");
        Assert.Equal(0, (await Run(mm, "emulator", "path", "testemu", dir)).code);
        Assert.Equal(0, (await Run(mm, "emulator", "install", "testemu")).code);

        var (code, text) = await Run(mm, "emulator", "path", "testemu");
        Assert.Equal(0, code);
        Assert.Contains("[x] base", text);
    }

    [Fact]
    public void EmulatorState_reads_in_priority_order()
    {
        static EmulatorStatus S(bool chosen, params (string v, string? inst)[] opts) => new("d", "D", chosen ? "f" : null,
            chosen, chosen, null, false,
            opts.Select((o, i) => new EmulatorOptionStatus("o" + i, "Opt " + i, "O" + i, o.v, o.inst, null)).ToList(),
            null, true, null, null);

        Assert.Equal("folder not chosen", TuiModel.EmulatorState(S(false, ("1.0", null))).Text);
        Assert.Equal("not installed", TuiModel.EmulatorState(S(true, ("1.0", null))).Text);
        Assert.Equal("installed v1.0", TuiModel.EmulatorState(S(true, ("1.0", "1.0"))).Text);
        Assert.Equal("O0 + O2", TuiModel.EmulatorState(S(true, ("1.0", "1.0"), ("1.0", null), ("1.0", "1.0"))).Text);
        Assert.Equal("update available", TuiModel.EmulatorState(S(true, ("2.0", "1.0"), ("1.0", "1.0"))).Text);
    }
}
