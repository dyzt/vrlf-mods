using System.Text.Json;
using Xunit;

namespace VrlfMods.Tests;

public class EmulatorServiceTests
{
    const string BaseSettings = """{ "edits": [ { "file": "emu.ini", "format": "ini", "section": "A", "key": "base", "value": "1" } ] }""";
    const string AddonSettings = """{ "edits": [ { "file": "emu.ini", "format": "ini", "section": "A", "key": "addon", "value": "1" } ] }""";

    sealed record Rig(EmulatorService Svc, EmulatorReceiptStore Receipts, FakeProcessProbe Probe,
        string Docs, AppPaths Paths, EmulatorEntry Emu);

    static Rig Build(bool vigem = true)
    {
        var paths = EmuFixture.TempPaths();
        var docs = EmuFixture.TempFolder("Documents");
        var zb = EmuFixture.Package(BaseSettings);
        var za = EmuFixture.Package(AddonSettings);
        var ob = EmuFixture.Option("base", zb);
        var oa = EmuFixture.Option("addon", za, requires: "base");
        var emu = EmuFixture.Entry(ob, oa);
        var reg = new ModRegistry(1, new VigemInfo("nefarius/ViGEmBus", "v1.22.0"), new(), null, new() { emu });
        var http = new FakeHttpFetcher(new()
        {
            [RegistryLoader.RawBase + "/mods.json"] = JsonSerializer.SerializeToUtf8Bytes(reg, VrlfJson.Default.ModRegistry),
            [EmulatorInstaller.ZipUrl(ob)] = zb,
            [EmulatorInstaller.ZipUrl(oa)] = za,
        });
        var receipts = new EmulatorReceiptStore(paths);
        var probe = new FakeProcessProbe();
        var svc = new EmulatorService(new RegistryLoader(http, paths),
            new EmulatorFolders(new GamePathStore(paths), new FakeKnownFolders(new() { ["DOCUMENTS"] = docs })),
            new EmulatorInstaller(http, paths, receipts, probe), receipts,
            new Vigem(new FakeServiceDetector(vigem), http, new FakeLauncher(), paths));
        return new Rig(svc, receipts, probe, docs, paths, emu);
    }

    static string SettingsFolder()
    {
        var d = EmuFixture.TempFolder("TestEmu Settings");
        EmuFixture.Write(d, "emu.ini", "[A]\r\nx = 1\r\n");
        return d;
    }

    [Fact]
    public async Task Status_offers_the_main_install_but_does_not_choose_it()
    {
        var rig = Build();
        EmuFixture.Write(Path.Combine(rig.Docs, "TestEmu"), "emu.ini", "[A]\r\n");

        var s = (await rig.Svc.Status("testemu"))!;

        Assert.False(s.FolderChosen);
        Assert.Equal(Path.Combine(rig.Docs, "TestEmu"), s.Suggested);
        Assert.Null(s.Folder);
        Assert.True(s.NeedsMet);
        Assert.All(s.Options, o => Assert.Null(o.InstalledVersion));
        Assert.False((await rig.Svc.Install("testemu", null)).Ok);   // nothing installs until chosen
    }

    [Fact]
    public async Task UseSuggested_chooses_the_found_folder()
    {
        var rig = Build();
        EmuFixture.Write(Path.Combine(rig.Docs, "TestEmu"), "emu.ini", "[A]\r\n");
        Assert.True((await rig.Svc.UseSuggested("testemu")).Ok);
        Assert.Equal(Path.Combine(rig.Docs, "TestEmu"), (await rig.Svc.Status("testemu"))!.Folder);
    }

    [Fact]
    public async Task Install_without_a_folder_says_to_choose_one()
    {
        var res = await Build().Svc.Install("testemu", null);
        Assert.False(res.Ok);
        Assert.Contains("choose TestEmu's settings folder first", res.Results[0].Message);
    }

    [Fact]
    public async Task Install_fails_when_the_chosen_folder_is_gone()
    {
        var rig = Build();
        var dir = SettingsFolder();
        Assert.True((await rig.Svc.SetFolder("testemu", dir)).Ok);
        Directory.Delete(dir, recursive: true);

        var res = await rig.Svc.Install("testemu", null);

        Assert.False(res.Ok);
        Assert.Contains("no longer there", res.Results[0].Message);
    }

    [Fact]
    public async Task Install_defaults_to_base_and_an_addon_needs_its_base()
    {
        var rig = Build();
        await rig.Svc.SetFolder("testemu", SettingsFolder());

        var early = await rig.Svc.Install("testemu", "addon");
        Assert.False(early.Ok);
        Assert.Contains("install base label first", early.Results[0].Message);

        Assert.True((await rig.Svc.Install("testemu", null)).Ok);
        Assert.NotNull(rig.Receipts.Load("testemu", "base"));
        Assert.True((await rig.Svc.Install("testemu", "addon")).Ok);
    }

    [Fact]
    public async Task Uninstalling_the_base_takes_its_dependants_first()
    {
        var rig = Build();
        var dir = SettingsFolder();
        await rig.Svc.SetFolder("testemu", dir);
        await rig.Svc.Install("testemu", "base");
        await rig.Svc.Install("testemu", "addon");

        var res = await rig.Svc.Uninstall("testemu", "base");

        Assert.True(res.Ok);
        Assert.Equal(new[] { "testemu/addon", "testemu/base" }, res.Results.Select(r => r.GameKey));
        Assert.Empty(rig.Receipts.ForEmulator("testemu"));
        Assert.Equal("[A]\r\nx = 1\r\n", EmuFixture.Read(dir, "emu.ini"));
    }

    [Fact]
    public async Task Cascade_stops_before_the_base_when_a_dependant_fails()
    {
        var rig = Build();
        var dir = SettingsFolder();
        await rig.Svc.SetFolder("testemu", dir);
        await rig.Svc.Install("testemu", "base");
        await rig.Svc.Install("testemu", "addon");
        var before = EmuFixture.Read(dir, "emu.ini");

        ActionReport res;
        using (File.Open(EmuFixture.PathOf(dir, "emu.ini"), FileMode.Open, FileAccess.Read, FileShare.None))
            res = await rig.Svc.Uninstall("testemu", null);

        Assert.False(res.Ok);
        Assert.Single(res.Results);                                  // the base was never attempted
        Assert.NotNull(rig.Receipts.Load("testemu", "base"));
        Assert.NotNull(rig.Receipts.Load("testemu", "addon"));
        Assert.Equal(before, EmuFixture.Read(dir, "emu.ini"));
    }

    [Fact]
    public async Task The_folder_is_locked_while_anything_is_installed()
    {
        var rig = Build();
        var dir = SettingsFolder();
        await rig.Svc.SetFolder("testemu", dir);
        await rig.Svc.Install("testemu", null);

        foreach (var res in new[]
                 {
                     await rig.Svc.SetFolder("testemu", SettingsFolder()),
                     await rig.Svc.ClearFolder("testemu"),
                     await rig.Svc.UseSuggested("testemu"),
                 })
        {
            Assert.False(res.Ok);
            Assert.Contains($"Installed in {dir}. Uninstall to move.", res.Results[0].Message);
        }
        Assert.True((await rig.Svc.Status("testemu"))!.Locked);
    }

    [Fact]
    public async Task Status_reports_installed_versions_and_the_driver()
    {
        var rig = Build(vigem: false);
        await rig.Svc.SetFolder("testemu", SettingsFolder());
        await rig.Svc.Install("testemu", null);

        var s = (await rig.Svc.Status("testemu"))!;

        Assert.Equal("1.0", s.Options.Single(o => o.Id == "base").InstalledVersion);
        Assert.Null(s.Options.Single(o => o.Id == "addon").InstalledVersion);
        Assert.False(s.NeedsMet);
        Assert.Equal("addon short", s.Options.Single(o => o.Id == "addon").Short);
    }

    [Fact]
    public async Task Update_and_reapply_need_something_installed()
    {
        var rig = Build();
        Assert.False((await rig.Svc.Update("testemu")).Ok);
        Assert.False((await rig.Svc.Reapply("testemu")).Ok);
    }

    [Fact]
    public async Task Unknown_ids_fail_cleanly()
    {
        var rig = Build();
        Assert.Null(await rig.Svc.Status("nope"));
        Assert.Contains("unknown emulator id", (await rig.Svc.Install("nope", null)).Results[0].Message);
        Assert.Contains("has no option", (await rig.Svc.Install("testemu", "nope")).Results[0].Message);
    }

    [Fact]
    public async Task ModManager_List_carries_the_emulators()
    {
        var rig = Build();
        var paths = rig.Paths;
        var http = new FakeHttpFetcher(new());
        var mm = new ModManager(new RegistryLoader(http, paths),
            new GameLocator(new SteamLocatorStub(-1, ""), new GamePathStore(paths)),
            new Installer(http, paths, new ReceiptStore(paths)), new ReceiptStore(paths),
            new Vigem(new FakeServiceDetector(true), http, new FakeLauncher(), paths),
            emulators: rig.Svc);

        Assert.NotNull(mm.Emulators);
        Assert.NotNull((await mm.List()).Emulators);
    }
}
