using Xunit;

namespace VrlfMods.Tests;

public class EmulatorInstallTests
{
    const string SetXInput = """
    { "edits": [ { "file": "emu.ini", "format": "ini", "section": "InputSources", "key": "XInput", "value": "true" } ] }
    """;

    const string Original = "[InputSources]\r\nXInput = false\r\n";

    [Fact]
    public async Task Install_copies_files_applies_edits_and_writes_the_receipt()
    {
        var paths = EmuFixture.TempPaths();
        var folder = EmuFixture.TempFolder();
        EmuFixture.Write(folder, "emu.ini", Original);
        var zip = EmuFixture.Package(SetXInput, ("inputprofiles/VRLF 2P (XInput).ini", "[Pad1]\r\nType = GunCon\r\n"));
        var opt = EmuFixture.Option("base", zip);
        var (inst, receipts, _) = EmuFixture.MakeInstaller(paths, (opt, zip));

        var res = await inst.Install(EmuFixture.Entry(opt), opt, folder);

        Assert.True(res.Ok, res.Message);
        Assert.Equal("testemu/base", res.GameKey);
        Assert.Equal("[InputSources]\r\nXInput = true\r\n", EmuFixture.Read(folder, "emu.ini"));
        Assert.Equal("[Pad1]\r\nType = GunCon\r\n", EmuFixture.Read(folder, "inputprofiles/VRLF 2P (XInput).ini"));
        var r = receipts.Load("testemu", "base")!;
        Assert.Equal("1.0", r.Version);
        Assert.Equal(folder, r.Folder);
        Assert.Single(r.Files);
        Assert.Equal(new[] { "XInput = false" }, r.Edits.Single().Prior.Lines);
        Assert.False(r.Edits.Single().FileCreated);
    }

    [Fact]
    public async Task Install_backs_up_a_file_it_overwrites()
    {
        var paths = EmuFixture.TempPaths();
        var folder = EmuFixture.TempFolder();
        EmuFixture.Write(folder, "emu.ini", Original);
        EmuFixture.Write(folder, "ctrlr/vrlf.cfg", "OLD");
        var zip = EmuFixture.Package(SetXInput, ("ctrlr/vrlf.cfg", "NEW"));
        var opt = EmuFixture.Option("base", zip);
        var (inst, receipts, _) = EmuFixture.MakeInstaller(paths, (opt, zip));

        await inst.Install(EmuFixture.Entry(opt), opt, folder);

        Assert.Equal("NEW", EmuFixture.Read(folder, "ctrlr/vrlf.cfg"));
        Assert.Equal("OLD", File.ReadAllText(Path.Combine(paths.EmuBackupDir("testemu", "base"), "ctrlr", "vrlf.cfg")));
        Assert.Single(receipts.Load("testemu", "base")!.Backups);
    }

    [Fact]
    public async Task Install_refuses_while_the_emulator_runs_and_changes_nothing()
    {
        var paths = EmuFixture.TempPaths();
        var folder = EmuFixture.TempFolder();
        EmuFixture.Write(folder, "emu.ini", Original);
        var zip = EmuFixture.Package(SetXInput);
        var opt = EmuFixture.Option("base", zip);
        var (inst, receipts, probe) = EmuFixture.MakeInstaller(paths, (opt, zip));
        probe.Running.Add("TestEmu-x64-avx2");                      // prefix match, any case

        var res = await inst.Install(EmuFixture.Entry(opt), opt, folder);

        Assert.False(res.Ok);
        Assert.Contains("Close TestEmu first", res.Message);
        Assert.Contains("TestEmu-x64-avx2", res.Message);
        Assert.Equal(Original, EmuFixture.Read(folder, "emu.ini"));
        Assert.Null(receipts.Load("testemu", "base"));
    }

    [Fact]
    public async Task Install_refuses_an_option_that_is_already_installed()
    {
        var paths = EmuFixture.TempPaths();
        var folder = EmuFixture.TempFolder();
        EmuFixture.Write(folder, "emu.ini", Original);
        var zip = EmuFixture.Package(SetXInput);
        var opt = EmuFixture.Option("base", zip);
        var (inst, _, _) = EmuFixture.MakeInstaller(paths, (opt, zip));
        await inst.Install(EmuFixture.Entry(opt), opt, folder);

        var again = await inst.Install(EmuFixture.Entry(opt), opt, folder);

        Assert.False(again.Ok);
        Assert.Contains("already installed", again.Message);
    }

    [Theory]
    [InlineData("""{ "edits": [ { "file": "emu.ini", "format": "mame", "section": "S", "key": "k", "value": "v" } ] }""")]
    [InlineData("""{ "edits": [ { "file": "../escape.ini", "format": "ini", "section": "S", "key": "k", "value": "v" } ] }""")]
    [InlineData("""not json""")]
    public async Task Install_rejects_a_bad_package_before_touching_the_folder(string settings)
    {
        var paths = EmuFixture.TempPaths();
        var folder = EmuFixture.TempFolder();
        EmuFixture.Write(folder, "emu.ini", Original);
        var zip = EmuFixture.Package(settings, ("new.cfg", "X"));
        var opt = EmuFixture.Option("base", zip);
        var (inst, receipts, _) = EmuFixture.MakeInstaller(paths, (opt, zip));

        var res = await inst.Install(EmuFixture.Entry(opt), opt, folder);

        Assert.False(res.Ok);
        Assert.False(File.Exists(EmuFixture.PathOf(folder, "new.cfg")));
        Assert.Equal(Original, EmuFixture.Read(folder, "emu.ini"));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(folder)!, "escape.ini")));
        Assert.Null(receipts.Load("testemu", "base"));
    }

    [Fact]
    public async Task Install_that_fails_part_way_leaves_no_trace()
    {
        var paths = EmuFixture.TempPaths();
        var folder = EmuFixture.TempFolder();
        EmuFixture.Write(folder, "emu.ini", Original);
        EmuFixture.Write(folder, "ctrlr/vrlf.cfg", "OLD");
        Directory.CreateDirectory(EmuFixture.PathOf(folder, "blocked.ini"));   // a folder where a file must go
        var zip = EmuFixture.Package("""
        { "edits": [
          { "file": "emu.ini", "format": "ini", "section": "InputSources", "key": "XInput", "value": "true" },
          { "file": "made.ini", "format": "ini", "section": "A", "key": "k", "value": "v" },
          { "file": "blocked.ini", "format": "ini", "section": "A", "key": "k", "value": "v" } ] }
        """, ("ctrlr/vrlf.cfg", "NEW"), ("inputprofiles/p.ini", "P"));
        var opt = EmuFixture.Option("base", zip);
        var (inst, receipts, _) = EmuFixture.MakeInstaller(paths, (opt, zip));

        var res = await inst.Install(EmuFixture.Entry(opt), opt, folder);

        Assert.False(res.Ok);
        Assert.Contains("nothing changed", res.Message);
        Assert.Equal(Original, EmuFixture.Read(folder, "emu.ini"));
        Assert.Equal("OLD", EmuFixture.Read(folder, "ctrlr/vrlf.cfg"));
        Assert.False(File.Exists(EmuFixture.PathOf(folder, "made.ini")));
        Assert.False(Directory.Exists(EmuFixture.PathOf(folder, "inputprofiles")));
        Assert.Null(receipts.Load("testemu", "base"));
    }

    [Fact]
    public async Task Install_with_a_hash_mismatch_changes_nothing()
    {
        var paths = EmuFixture.TempPaths();
        var folder = EmuFixture.TempFolder();
        EmuFixture.Write(folder, "emu.ini", Original);
        var zip = EmuFixture.Package(SetXInput);
        var opt = EmuFixture.Option("base", zip) with { Sha256 = "00" };
        var (inst, _, _) = EmuFixture.MakeInstaller(paths, (opt, zip));

        var res = await inst.Install(EmuFixture.Entry(opt), opt, folder);

        Assert.False(res.Ok);
        Assert.Contains("sha256 mismatch", res.Message);
        Assert.Equal(Original, EmuFixture.Read(folder, "emu.ini"));
    }

    [Fact]
    public async Task Install_creates_a_missing_settings_file_and_records_that()
    {
        var paths = EmuFixture.TempPaths();
        var folder = EmuFixture.TempFolder();
        var zip = EmuFixture.Package("""
        { "edits": [ { "file": "GameSettings/RGSE8P.ini", "format": "ini", "section": "Controls", "key": "WiimoteSource0", "value": "1" } ] }
        """);
        var opt = EmuFixture.Option("base", zip);
        var (inst, receipts, _) = EmuFixture.MakeInstaller(paths, (opt, zip));

        var res = await inst.Install(EmuFixture.Entry(opt), opt, folder);

        Assert.True(res.Ok, res.Message);
        Assert.Equal("[Controls]\r\nWiimoteSource0 = 1\r\n", EmuFixture.Read(folder, "GameSettings/RGSE8P.ini"));
        Assert.True(receipts.Load("testemu", "base")!.Edits.Single().FileCreated);
    }

    [Fact]
    public async Task Install_handles_spaces_brackets_and_parentheses()
    {
        var paths = EmuFixture.TempPaths();
        var folder = EmuFixture.TempFolder("PCSX2 [Lightgun] (portable)");
        EmuFixture.Write(folder, "inis/PCSX2.ini", Original);
        var zip = EmuFixture.Package("""
        { "edits": [ { "file": "inis/PCSX2.ini", "format": "ini", "section": "InputSources", "key": "XInput", "value": "true" } ] }
        """, ("inputprofiles/VRLF GunCon2 2P (XInput).ini", "[USB1]\r\n"));
        var opt = EmuFixture.Option("base", zip);
        var (inst, _, _) = EmuFixture.MakeInstaller(paths, (opt, zip));

        var res = await inst.Install(EmuFixture.Entry(opt), opt, folder);

        Assert.True(res.Ok, res.Message);
        Assert.True(File.Exists(EmuFixture.PathOf(folder, "inputprofiles/VRLF GunCon2 2P (XInput).ini")));
        Assert.Equal("[InputSources]\r\nXInput = true\r\n", EmuFixture.Read(folder, "inis/PCSX2.ini"));
    }
}
