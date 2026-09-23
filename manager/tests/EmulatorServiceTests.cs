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
        var zb = EmuFixture.Package(BaseSettings);
        var za = EmuFixture.Package(AddonSettings);
        var ob = EmuFixture.Option("base", zb);
        var oa = EmuFixture.Option("addon", za, requires: "base");
        return Make(EmuFixture.TempPaths(), EmuFixture.TempFolder("Documents"), vigem, (ob, zb), (oa, za));
    }

    /// <summary>A service whose registry lists exactly these options, each zip served at its own
    /// URL. Pass another rig's paths to see its receipts through a newer registry.</summary>
    static Rig Make(AppPaths paths, string docs, bool vigem, params (EmulatorOption opt, byte[] zip)[] served)
    {
        var emu = EmuFixture.Entry(served.Select(s => s.opt).ToArray());
        var reg = new ModRegistry(1, new VigemInfo("nefarius/ViGEmBus", "v1.22.0"), new(), null, new() { emu });
        var map = new Dictionary<string, byte[]?>
        {
            [RegistryLoader.RawBase + "/mods.json"] = JsonSerializer.SerializeToUtf8Bytes(reg, VrlfJson.Default.ModRegistry),
        };
        foreach (var (o, z) in served) map[EmulatorInstaller.ZipUrl(o)] = z;
        var http = new FakeHttpFetcher(map);
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

    // The menu shows a found main install as the folder, so installing from it takes that folder.
    [Fact]
    public async Task Install_orSuggested_takes_the_main_install_as_the_folder()
    {
        Assert.False((await Build().Svc.Install("testemu", null, orSuggested: true)).Ok);   // nothing found

        var rig = Build();
        var main = Path.Combine(rig.Docs, "TestEmu");
        EmuFixture.Write(main, "emu.ini", "[A]\r\n");

        Assert.True((await rig.Svc.Install("testemu", null, orSuggested: true)).Ok);

        var s = (await rig.Svc.Status("testemu"))!;
        Assert.Equal(main, s.Folder);
        Assert.True(s.Locked);
        Assert.NotNull(rig.Receipts.Load("testemu", "base"));
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

    // Regression: uninstall-all walked only the registry's options, so a receipt for an option
    // later dropped from mods.json could never be removed and kept the folder locked forever.
    [Fact]
    public async Task Uninstall_all_also_removes_options_the_registry_dropped()
    {
        var rig = Build();
        var dir = SettingsFolder();
        await rig.Svc.SetFolder("testemu", dir);
        Assert.True((await rig.Svc.Install("testemu", "base")).Ok);
        // "legacy" was installed while an older mods.json still listed it.
        var zl = EmuFixture.Package("""{ "edits": [ { "file": "emu.ini", "format": "ini", "section": "A", "key": "legacy", "value": "1" } ] }""");
        var legacy = EmuFixture.Option("legacy", zl);
        var (older, _, _) = EmuFixture.MakeInstaller(rig.Paths, (legacy, zl));
        Assert.True((await older.Install(EmuFixture.Entry(legacy), legacy, dir)).Ok);

        var res = await rig.Svc.Uninstall("testemu", null);

        Assert.True(res.Ok, string.Join("; ", res.Results.Select(r => r.Message)));
        Assert.Equal(new[] { "testemu/legacy", "testemu/base" }, res.Results.Select(r => r.GameKey));
        Assert.Contains("uninstalled TestEmu legacy", res.Results[0].Message);
        Assert.Empty(rig.Receipts.ForEmulator("testemu"));
        Assert.Equal("[A]\r\nx = 1\r\n", EmuFixture.Read(dir, "emu.ini"));
        Assert.False((await rig.Svc.Status("testemu"))!.Locked);
    }

    [Fact]
    public async Task Uninstall_all_stops_when_a_dropped_option_fails()
    {
        var rig = Build();
        var dir = SettingsFolder();
        await rig.Svc.SetFolder("testemu", dir);
        Assert.True((await rig.Svc.Install("testemu", "base")).Ok);
        var zl = EmuFixture.Package("""{ "edits": [ { "file": "emu.ini", "format": "ini", "section": "A", "key": "legacy", "value": "1" } ] }""");
        var legacy = EmuFixture.Option("legacy", zl);
        var (older, _, _) = EmuFixture.MakeInstaller(rig.Paths, (legacy, zl));
        Assert.True((await older.Install(EmuFixture.Entry(legacy), legacy, dir)).Ok);

        ActionReport res;
        using (File.Open(EmuFixture.PathOf(dir, "emu.ini"), FileMode.Open, FileAccess.Read, FileShare.None))
            res = await rig.Svc.Uninstall("testemu", null);

        Assert.False(res.Ok);
        Assert.Equal("testemu/legacy", Assert.Single(res.Results).GameKey);
        Assert.NotNull(rig.Receipts.Load("testemu", "base"));
        Assert.NotNull(rig.Receipts.Load("testemu", "legacy"));
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

    /// <summary>Base and add-on installed at v1.0 into a fresh settings folder.</summary>
    static async Task<(Rig rig, string dir)> BothInstalled()
    {
        var rig = Build();
        var dir = SettingsFolder();
        await rig.Svc.SetFolder("testemu", dir);
        Assert.True((await rig.Svc.Install("testemu", "base")).Ok);
        Assert.True((await rig.Svc.Install("testemu", "addon")).Ok);
        return (rig, dir);
    }

    static string ReceiptJson(Rig rig, string opt) =>
        JsonSerializer.Serialize(rig.Receipts.Load("testemu", opt)!, VrlfJson.Default.EmulatorReceipt);

    // Regression: Update kept going after a failure, so an add-on could be reinstalled on top of
    // a base that had just failed (possibly after its own uninstall).
    [Fact]
    public async Task Update_stops_at_the_first_failure()
    {
        var (v1, _) = await BothInstalled();
        var badBase = EmuFixture.Package("""{ "edits": [ { "file": "emu.ini", "format": "toml", "section": "A", "key": "base", "value": "2" } ] }""");
        var addon2 = EmuFixture.Package("""{ "edits": [ { "file": "emu.ini", "format": "ini", "section": "A", "key": "addon", "value": "2" } ] }""");
        var v2 = Make(v1.Paths, v1.Docs, true,
            (EmuFixture.Option("base", badBase, "2.0"), badBase),
            (EmuFixture.Option("addon", addon2, "2.0", requires: "base"), addon2));

        var res = await v2.Svc.Update("testemu");

        Assert.False(res.Ok);
        Assert.Single(res.Results);
        Assert.Equal("testemu/base", res.Results[0].GameKey);
        Assert.Equal("1.0", v2.Receipts.Load("testemu", "addon")!.Version);
    }

    // Update never cascades: refreshing the base is its own uninstall and install, so an add-on
    // on top of it is neither removed nor re-applied.
    [Fact]
    public async Task Update_of_the_base_leaves_the_addon_installed()
    {
        var (v1, dir) = await BothInstalled();
        var addonBefore = ReceiptJson(v1, "addon");
        var base2 = EmuFixture.Package("""{ "edits": [ { "file": "emu.ini", "format": "ini", "section": "A", "key": "base", "value": "2" } ] }""");
        var addon = EmuFixture.Package(AddonSettings);
        var v2 = Make(v1.Paths, v1.Docs, true,
            (EmuFixture.Option("base", base2, "2.0"), base2),
            (EmuFixture.Option("addon", addon, "1.0", requires: "base"), addon));

        var res = await v2.Svc.Update("testemu");

        Assert.True(res.Ok, string.Join("; ", res.Results.Select(r => r.Message)));
        Assert.Equal("2.0", v2.Receipts.Load("testemu", "base")!.Version);
        Assert.Equal(addonBefore, ReceiptJson(v2, "addon"));
        var t = SettingsText.Load(EmuFixture.PathOf(dir, "emu.ini"));
        Assert.Equal("1", IniEditor.Get(t, "A", "addon"));
        Assert.Equal("2", IniEditor.Get(t, "A", "base"));
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
    public async Task Install_names_the_missing_default_option_as_base()
    {
        var paths = EmuFixture.TempPaths();
        var z = EmuFixture.Package("""{ "edits": [] }""");
        var custom = EmuFixture.Option("custom", z);
        var emu = EmuFixture.Entry(custom);
        var reg = new ModRegistry(1, new VigemInfo("nefarius/ViGEmBus", "v1.22.0"), new(), null, new() { emu });
        var http = new FakeHttpFetcher(new()
        {
            [RegistryLoader.RawBase + "/mods.json"] = JsonSerializer.SerializeToUtf8Bytes(reg, VrlfJson.Default.ModRegistry),
        });
        var receipts = new EmulatorReceiptStore(paths);
        var svc = new EmulatorService(new RegistryLoader(http, paths),
            new EmulatorFolders(new GamePathStore(paths), new FakeKnownFolders(new())),
            new EmulatorInstaller(http, paths, receipts, new FakeProcessProbe()), receipts,
            new Vigem(new FakeServiceDetector(true), http, new FakeLauncher(), paths));

        var res = await svc.Install("testemu", null);

        Assert.False(res.Ok);
        Assert.Contains("has no option 'base'", res.Results[0].Message);
    }

    // Regression: DependsOn only stopped on a direct self-reference. "a" and "b" require each
    // other, an indirect (two-hop) cycle unrelated to the option actually targeted ("c"); walking
    // that cycle while scoping the cascade for "c" never reaches "c" and never finds a repeated
    // reference either, so the old code spun forever. This exercise deliberately targets an
    // unrelated option rather than "a" or "b" themselves - within a 2-cycle, asking whether the
    // OTHER member depends on either "a" or "b" always matches on the very first hop (it directly
    // requires that target), so only a target outside the cycle actually walks it far enough to
    // hang the old code.
    [Fact]
    public async Task Uninstall_terminates_on_a_requires_cycle()
    {
        var paths = EmuFixture.TempPaths();
        var zc = EmuFixture.Package("""{ "edits": [] }""");
        var za = EmuFixture.Package("""{ "edits": [] }""");
        var zb = EmuFixture.Package("""{ "edits": [] }""");
        var oc = EmuFixture.Option("c", zc);
        var oa = EmuFixture.Option("a", za, requires: "b");
        var ob = EmuFixture.Option("b", zb, requires: "a");
        var emu = EmuFixture.Entry(oc, oa, ob);
        var reg = new ModRegistry(1, new VigemInfo("nefarius/ViGEmBus", "v1.22.0"), new(), null, new() { emu });
        var http = new FakeHttpFetcher(new()
        {
            [RegistryLoader.RawBase + "/mods.json"] = JsonSerializer.SerializeToUtf8Bytes(reg, VrlfJson.Default.ModRegistry),
        });
        var receipts = new EmulatorReceiptStore(paths);
        var folder = EmuFixture.TempFolder();
        // Installed directly via receipts, bypassing the service's own requires gate on Install.
        foreach (var opt in new[] { oc, oa, ob })
            receipts.Save(new EmulatorReceipt(emu.Id, opt.Id, opt.Version, folder, new(), new(), new(), "2026-01-01T00:00:00Z"));
        var svc = new EmulatorService(new RegistryLoader(http, paths),
            new EmulatorFolders(new GamePathStore(paths), new FakeKnownFolders(new())),
            new EmulatorInstaller(http, paths, receipts, new FakeProcessProbe()), receipts,
            new Vigem(new FakeServiceDetector(true), http, new FakeLauncher(), paths));

        var task = svc.Uninstall("testemu", "c");
        var finished = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(task, finished);
        var res = await task;
        Assert.True(res.Ok, string.Join("; ", res.Results.Select(r => r.Message)));
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

    // Flycast's two guns are Virtual Lightgun lanes, so its status reports that driver, not ViGEmBus.
    [Theory]
    [InlineData("1.0.3", true)]
    [InlineData(null, false)]
    public void A_virtualgun_emulator_reports_the_Virtual_Lightgun(string? version, bool met)
    {
        var paths = EmuFixture.TempPaths();
        var http = new FakeHttpFetcher(new());
        var gun = new VirtualGun(new FakeVirtualGunState { Version = version }, http, new FakeElevatedRunner(0), paths);
        var receipts = new EmulatorReceiptStore(paths);
        var svc = new EmulatorService(new RegistryLoader(http, paths),
            new EmulatorFolders(new GamePathStore(paths), new FakeKnownFolders(new())),
            new EmulatorInstaller(http, paths, receipts, new FakeProcessProbe()), receipts,
            new Vigem(new FakeServiceDetector(true), http, new FakeLauncher(), paths), gun);

        var s = svc.StatusFor(EmuFixture.Entry() with { Needs = "virtualgun" });

        Assert.Equal(met, s.NeedsMet);
    }

    [Fact]
    public void Status_text_names_the_Virtual_Lightgun()
    {
        var s = new EmulatorStatus("flycast", "Flycast", null, false, false, null, false, new(),
            "virtualgun", false, null, null);
        var old = Console.Out;
        var sw = new StringWriter();
        Console.SetOut(sw);
        try { Output.EmulatorStatusText(s); }
        finally { Console.SetOut(old); }
        Assert.Contains("Needs Virtual Lightgun: not installed", sw.ToString());
    }
}
