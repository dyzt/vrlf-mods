using Xunit;

namespace VrlfMods.Tests;

public class EmulatorUninstallTests
{
    // Every edit kind against every awkward file shape at once.
    const string Settings = """
    { "edits": [
      { "file": "emu.ini", "format": "ini", "section": "InputSources", "key": "XInput", "value": "true" },
      { "file": "emu.ini", "format": "ini", "section": "InputSources", "key": "DInput", "value": "false" },
      { "file": "emu.ini", "format": "ini", "section": "Pad1", "replace": [ "Type = GunCon", "Trigger = XInput-0/A" ] },
      { "file": "emu.ini", "format": "ini", "section": "USB1", "replace": [ "Type = guncon2" ] },
      { "file": "lf.ini", "format": "ini", "section": "B", "key": "z", "value": "3" },
      { "file": "bom.ini", "format": "ini", "section": "Server", "key": "Enabled", "value": "True" },
      { "file": "mame.ini", "format": "mame", "key": "lightgun", "value": "1" },
      { "file": "mame.ini", "format": "mame", "key": "ctrlr", "value": "vrlf" },
      { "file": "GameSettings/NEW.ini", "format": "ini", "section": "Controls", "key": "k", "value": "v" } ] }
    """;

    static readonly Dictionary<string, string> Fixture = new()
    {
        ["emu.ini"] = "[UI]\r\nTheme = dark\r\n\r\n[InputSources]\r\nSDL = true\r\nXInput = false\r\n\r\n[Pad1]\r\nType = DualShock2\r\nCross = SDL-0/A\r\n",
        ["lf.ini"] = "[A]\nx = 1\n[B]\ny = 2",
        ["bom.ini"] = "\u00EF\u00BB\u00BF[Server]\r\nEnabled = False\r\n",   // Latin-1 view of a UTF-8 BOM
        ["mame.ini"] = "lightgun 1\nwindow 1\n\nlightgun            0\njoystick_deadzone   0.15",
        ["ctrlr/vrlf.cfg"] = "OLD",
    };

    static async Task<(EmulatorInstaller inst, EmulatorReceiptStore receipts, FakeProcessProbe probe,
        EmulatorEntry emu, EmulatorOption opt, string folder)> Installed(string settings = Settings, string version = "1.0")
    {
        var paths = EmuFixture.TempPaths();
        var folder = EmuFixture.TempFolder();
        foreach (var (rel, content) in Fixture) EmuFixture.Write(folder, rel, content);
        var zip = EmuFixture.Package(settings, ("ctrlr/vrlf.cfg", "NEW"), ("inputprofiles/p.ini", "P"));
        var opt = EmuFixture.Option("base", zip, version);
        var emu = EmuFixture.Entry(opt);
        var (inst, receipts, probe) = EmuFixture.MakeInstaller(paths, (opt, zip));
        var res = await inst.Install(emu, opt, folder);
        Assert.True(res.Ok, res.Message);
        return (inst, receipts, probe, emu, opt, folder);
    }

    static void AssertFixtureRestored(string folder)
    {
        foreach (var (rel, content) in Fixture) Assert.Equal(content, EmuFixture.Read(folder, rel));
        var left = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(folder, p).Replace('\\', '/')).OrderBy(p => p);
        Assert.Equal(Fixture.Keys.OrderBy(p => p), left);
        Assert.False(Directory.Exists(EmuFixture.PathOf(folder, "GameSettings")));
        Assert.False(Directory.Exists(EmuFixture.PathOf(folder, "inputprofiles")));
    }

    [Fact]
    public async Task Install_then_uninstall_is_byte_identical()
    {
        var (inst, receipts, _, emu, _, folder) = await Installed();
        var res = inst.Uninstall(emu, receipts.Load("testemu", "base")!);
        Assert.True(res.Ok, res.Message);
        AssertFixtureRestored(folder);
        Assert.Null(receipts.Load("testemu", "base"));
    }

    [Fact]
    public async Task An_unrelated_user_change_survives_and_our_setting_reverts_anyway()
    {
        var (inst, receipts, _, emu, _, folder) = await Installed();
        var t = SettingsText.Load(EmuFixture.PathOf(folder, "emu.ini"));
        IniEditor.Set(t, "UI", "Theme", "light");                  // the user's own change
        IniEditor.Set(t, "InputSources", "XInput", "maybe");       // the user edited OUR setting
        t.Save(EmuFixture.PathOf(folder, "emu.ini"));

        inst.Uninstall(emu, receipts.Load("testemu", "base")!);

        var after = SettingsText.Load(EmuFixture.PathOf(folder, "emu.ini"));
        Assert.Equal("light", IniEditor.Get(after, "UI", "Theme"));
        Assert.Equal("false", IniEditor.Get(after, "InputSources", "XInput"));
    }

    [Fact]
    public async Task Uninstall_after_the_emulator_rewrote_the_file_restores_the_old_values()
    {
        var (inst, receipts, _, emu, _, folder) = await Installed();
        EmuFixture.Write(folder, "emu.ini",
            "[USB1]\r\nType=guncon2\r\n[Pad1]\r\nTrigger=XInput-0/A\r\nType=GunCon\r\n" +
            "[InputSources]\r\nDInput=false\r\nXInput=true\r\nSDL=true\r\n[UI]\r\nTheme=dark\r\n");

        var res = inst.Uninstall(emu, receipts.Load("testemu", "base")!);

        Assert.True(res.Ok, res.Message);
        var t = SettingsText.Load(EmuFixture.PathOf(folder, "emu.ini"));
        Assert.Null(IniEditor.SectionBody(t, "USB1"));
        Assert.Equal(new[] { "Type = DualShock2", "Cross = SDL-0/A" }, IniEditor.SectionBody(t, "Pad1"));
        Assert.Equal("false", IniEditor.Get(t, "InputSources", "XInput"));
        Assert.Null(IniEditor.Get(t, "InputSources", "DInput"));
        Assert.Equal("true", IniEditor.Get(t, "InputSources", "SDL"));
    }

    [Fact]
    public async Task Uninstall_skips_a_deleted_settings_file_with_a_warning()
    {
        var (inst, receipts, _, emu, _, folder) = await Installed();
        File.Delete(EmuFixture.PathOf(folder, "emu.ini"));                 // four edits live in this file

        var res = inst.Uninstall(emu, receipts.Load("testemu", "base")!);

        Assert.True(res.Ok, res.Message);
        Assert.Contains("skipped emu.ini", res.Message);
        Assert.Equal(1, res.Message.Split("skipped emu.ini").Length - 1);  // one warning per file, not per edit
        Assert.False(File.Exists(EmuFixture.PathOf(folder, "emu.ini")));   // never recreated
        Assert.Equal(Fixture["lf.ini"], EmuFixture.Read(folder, "lf.ini")); // the rest still reverted
        Assert.Null(receipts.Load("testemu", "base"));
    }

    [Fact]
    public async Task Uninstall_when_the_folder_is_gone_recreates_nothing()
    {
        var (inst, receipts, _, emu, _, folder) = await Installed();
        Directory.Delete(folder, recursive: true);

        var res = inst.Uninstall(emu, receipts.Load("testemu", "base")!);

        Assert.True(res.Ok, res.Message);
        Assert.Contains("no longer exists", res.Message);
        Assert.False(Directory.Exists(folder));
        Assert.Null(receipts.Load("testemu", "base"));
    }

    // Regression: receipt deletion used to sit outside the try, so an IO error there escaped as
    // an unhandled exception instead of becoming a failed OpResult.
    [Fact]
    public async Task Uninstall_reports_failure_when_the_receipt_cannot_be_deleted()
    {
        var paths = EmuFixture.TempPaths();
        var folder = EmuFixture.TempFolder();
        EmuFixture.Write(folder, "emu.ini", Fixture["emu.ini"]);
        var zip = EmuFixture.Package(V1);
        var opt = EmuFixture.Option("base", zip);
        var emu = EmuFixture.Entry(opt);
        var (inst, receipts, _) = EmuFixture.MakeInstaller(paths, (opt, zip));
        Assert.True((await inst.Install(emu, opt, folder)).Ok);
        var receiptPath = paths.EmuReceiptPath("testemu", "base");
        var r = receipts.Load("testemu", "base")!;

        using var locked = new FileStream(receiptPath, FileMode.Open, FileAccess.Read, FileShare.None);
        var res = inst.Uninstall(emu, r);

        Assert.False(res.Ok);
        Assert.Contains("uninstall stopped part-way", res.Message);
    }

    // Same regression, in the folder-gone branch.
    [Fact]
    public async Task Uninstall_reports_failure_when_the_receipt_cannot_be_deleted_after_the_folder_is_gone()
    {
        var paths = EmuFixture.TempPaths();
        var folder = EmuFixture.TempFolder();
        EmuFixture.Write(folder, "emu.ini", Fixture["emu.ini"]);
        var zip = EmuFixture.Package(V1);
        var opt = EmuFixture.Option("base", zip);
        var emu = EmuFixture.Entry(opt);
        var (inst, receipts, _) = EmuFixture.MakeInstaller(paths, (opt, zip));
        Assert.True((await inst.Install(emu, opt, folder)).Ok);
        var receiptPath = paths.EmuReceiptPath("testemu", "base");
        var r = receipts.Load("testemu", "base")!;
        Directory.Delete(folder, recursive: true);

        using var locked = new FileStream(receiptPath, FileMode.Open, FileAccess.Read, FileShare.None);
        var res = inst.Uninstall(emu, r);

        Assert.False(res.Ok);
        Assert.Contains("uninstall stopped part-way", res.Message);
    }

    // Regression: the backup folder used to go before the receipt did. When the receipt delete
    // then failed, a re-run found the restored original at our path, called it "changed since
    // install", deleted it, and had no backup left to put back.
    [Fact]
    public async Task Uninstall_rerun_after_a_receipt_delete_failure_keeps_the_originals()
    {
        var paths = EmuFixture.TempPaths();
        var folder = EmuFixture.TempFolder();
        EmuFixture.Write(folder, "emu.ini", Fixture["emu.ini"]);
        EmuFixture.Write(folder, "ctrlr/vrlf.cfg", "OLD");
        var zip = EmuFixture.Package(V1, ("ctrlr/vrlf.cfg", "NEW"));
        var opt = EmuFixture.Option("base", zip);
        var emu = EmuFixture.Entry(opt);
        var (inst, receipts, _) = EmuFixture.MakeInstaller(paths, (opt, zip));
        Assert.True((await inst.Install(emu, opt, folder)).Ok);
        var receiptPath = paths.EmuReceiptPath("testemu", "base");
        var backupDir = paths.EmuBackupDir("testemu", "base");
        var r = receipts.Load("testemu", "base")!;

        OpResult first;
        using (new FileStream(receiptPath, FileMode.Open, FileAccess.Read, FileShare.None))
            first = inst.Uninstall(emu, r);
        Assert.False(first.Ok);

        var again = inst.Uninstall(emu, receipts.Load("testemu", "base")!);

        Assert.True(again.Ok, again.Message);
        Assert.DoesNotContain("had been changed", again.Message);
        Assert.Equal("OLD", EmuFixture.Read(folder, "ctrlr/vrlf.cfg"));
        Assert.Equal(Fixture["emu.ini"], EmuFixture.Read(folder, "emu.ini"));
        Assert.False(File.Exists(receiptPath));
        Assert.False(Directory.Exists(backupDir));
    }

    // A backup that cannot be put back (here a damaged receipt path that lands outside the
    // settings folder) must not cost the backup folder: it may hold the user's only original.
    [Fact]
    public async Task Uninstall_keeps_the_backups_when_one_cannot_be_put_back()
    {
        var paths = EmuFixture.TempPaths();
        var folder = EmuFixture.TempFolder();
        EmuFixture.Write(folder, "emu.ini", Fixture["emu.ini"]);
        var zip = EmuFixture.Package(V1);
        var opt = EmuFixture.Option("base", zip);
        var emu = EmuFixture.Entry(opt);
        var (inst, receipts, _) = EmuFixture.MakeInstaller(paths, (opt, zip));
        Assert.True((await inst.Install(emu, opt, folder)).Ok);
        var backupDir = paths.EmuBackupDir("testemu", "base");
        EmuFixture.Write(backupDir, "stray.cfg", "ORIGINAL");
        // Inside the backup folder (it ends in "base"), outside the settings folder.
        var damaged = receipts.Load("testemu", "base")! with { Backups = new() { new BackupRef("../base/stray.cfg") } };

        var res = inst.Uninstall(emu, damaged);

        Assert.True(res.Ok, res.Message);
        Assert.Equal("ORIGINAL", EmuFixture.Read(backupDir, "stray.cfg"));
    }

    [Fact]
    public async Task Uninstall_refuses_while_the_emulator_runs_and_keeps_the_receipt()
    {
        var (inst, receipts, probe, emu, _, folder) = await Installed();
        probe.Running.Add("testemu");

        var res = inst.Uninstall(emu, receipts.Load("testemu", "base")!);

        Assert.False(res.Ok);
        Assert.Contains("Close TestEmu first", res.Message);
        Assert.NotNull(receipts.Load("testemu", "base"));
        Assert.Equal("NEW", EmuFixture.Read(folder, "ctrlr/vrlf.cfg"));
    }

    [Fact]
    public async Task Uninstall_warns_about_an_installed_file_the_user_changed()
    {
        var (inst, receipts, _, emu, _, folder) = await Installed();
        EmuFixture.Write(folder, "inputprofiles/p.ini", "EDITED");

        var res = inst.Uninstall(emu, receipts.Load("testemu", "base")!);

        Assert.True(res.Ok, res.Message);
        Assert.Contains("inputprofiles/p.ini had been changed", res.Message);
        Assert.False(File.Exists(EmuFixture.PathOf(folder, "inputprofiles/p.ini")));
        Assert.Null(receipts.Load("testemu", "base"));
    }

    [Fact]
    public async Task Uninstall_refuses_when_the_drive_is_not_connected()
    {
        var (inst, receipts, _, emu, _, _) = await Installed();
        var used = DriveInfo.GetDrives().Select(d => d.Name[0]).ToHashSet();
        var letter = "ZYXWVUTSRQPONMLKJIHGFEDCBA".First(c => !used.Contains(c));
        var missingFolder = $@"{letter}:\Missing\Settings";
        var moved = receipts.Load("testemu", "base")! with { Folder = missingFolder };
        receipts.Save(moved);

        var res = inst.Uninstall(emu, moved);

        Assert.False(res.Ok);
        Assert.Contains($"{letter}:", res.Message);
        Assert.NotNull(receipts.Load("testemu", "base"));
    }

    // Regression: undoing newest-first matters only once a file is created by two separate
    // edits. Undoing oldest-first instead leaves the second edit believing it never created the
    // file, so its own revert saves an empty stub instead of deleting it.
    [Fact]
    public async Task Uninstall_deletes_a_file_created_by_two_separate_edits()
    {
        var paths = EmuFixture.TempPaths();
        var folder = EmuFixture.TempFolder();
        var zip = EmuFixture.Package("""
        { "edits": [
          { "file": "new.ini", "format": "ini", "section": "A", "key": "k1", "value": "v1" },
          { "file": "new.ini", "format": "ini", "section": "B", "key": "k2", "value": "v2" } ] }
        """);
        var opt = EmuFixture.Option("base", zip);
        var emu = EmuFixture.Entry(opt);
        var (inst, receipts, _) = EmuFixture.MakeInstaller(paths, (opt, zip));
        Assert.True((await inst.Install(emu, opt, folder)).Ok);

        var res = inst.Uninstall(emu, receipts.Load("testemu", "base")!);

        Assert.True(res.Ok, res.Message);
        Assert.False(File.Exists(EmuFixture.PathOf(folder, "new.ini")));
    }

    // Regression: a pre-existing file whose only content was a comment reverts to that same
    // comment-only content. It must never be deleted just because the reverted text looks blank
    // - only a file WE created should ever be removed.
    [Fact]
    public async Task Uninstall_keeps_a_pre_existing_file_that_reverts_to_only_a_comment()
    {
        var paths = EmuFixture.TempPaths();
        var folder = EmuFixture.TempFolder();
        EmuFixture.Write(folder, "comment.ini", "; nothing here\r\n");
        var zip = EmuFixture.Package("""
        { "edits": [ { "file": "comment.ini", "format": "ini", "section": "S", "key": "k", "value": "v" } ] }
        """);
        var opt = EmuFixture.Option("base", zip);
        var emu = EmuFixture.Entry(opt);
        var (inst, receipts, _) = EmuFixture.MakeInstaller(paths, (opt, zip));
        Assert.True((await inst.Install(emu, opt, folder)).Ok);

        var res = inst.Uninstall(emu, receipts.Load("testemu", "base")!);

        Assert.True(res.Ok, res.Message);
        Assert.Equal("; nothing here\r\n", EmuFixture.Read(folder, "comment.ini"));
    }

    const string V1 = """{ "edits": [ { "file": "emu.ini", "format": "ini", "section": "InputSources", "key": "XInput", "value": "true" } ] }""";
    const string V2 = """
    { "edits": [ { "file": "emu.ini", "format": "ini", "section": "InputSources", "key": "XInput", "value": "yes" },
                 { "file": "emu.ini", "format": "ini", "section": "InputSources", "key": "Added", "value": "1" } ] }
    """;

    static async Task<(EmulatorInstaller inst, EmulatorReceiptStore receipts, EmulatorEntry emu,
        EmulatorOption v1, EmulatorOption v2, string folder)> V1Installed(bool v2HashOk = true)
    {
        var paths = EmuFixture.TempPaths();
        var folder = EmuFixture.TempFolder();
        EmuFixture.Write(folder, "emu.ini", Fixture["emu.ini"]);
        var z1 = EmuFixture.Package(V1);
        var z2 = EmuFixture.Package(V2);
        var v1 = EmuFixture.Option("base", z1, "1.0");
        var v2 = EmuFixture.Option("base", z2, "2.0");
        if (!v2HashOk) v2 = v2 with { Sha256 = "00" };
        var emu = EmuFixture.Entry(v2);
        var (inst, receipts, _) = EmuFixture.MakeInstaller(paths, (v1, z1), (v2, z2));
        Assert.True((await inst.Install(emu, v1, folder)).Ok);
        return (inst, receipts, emu, v1, v2, folder);
    }

    [Fact]
    public async Task Update_restores_the_originals_first_then_records_them_again()
    {
        var (inst, receipts, emu, _, v2, folder) = await V1Installed();

        var res = await inst.Refresh(emu, v2, receipts.Load("testemu", "base")!, onlyIfNewer: true);

        Assert.True(res.Ok, res.Message);
        var t = SettingsText.Load(EmuFixture.PathOf(folder, "emu.ini"));
        Assert.Equal("yes", IniEditor.Get(t, "InputSources", "XInput"));
        var r = receipts.Load("testemu", "base")!;
        Assert.Equal("2.0", r.Version);
        Assert.Equal(new[] { "XInput = false" }, r.Edits[0].Prior.Lines);   // the user's, never ours

        inst.Uninstall(emu, r);
        Assert.Equal(Fixture["emu.ini"], EmuFixture.Read(folder, "emu.ini"));
    }

    [Fact]
    public async Task Update_of_an_up_to_date_option_changes_nothing()
    {
        var (inst, receipts, emu, v1, _, folder) = await V1Installed();
        var before = EmuFixture.Read(folder, "emu.ini");

        var res = await inst.Refresh(emu, v1, receipts.Load("testemu", "base")!, onlyIfNewer: true);

        Assert.True(res.Ok);
        Assert.Contains("up to date", res.Message);
        Assert.Equal(before, EmuFixture.Read(folder, "emu.ini"));
    }

    [Fact]
    public async Task Update_checks_the_new_package_before_touching_anything()
    {
        var (inst, receipts, emu, _, v2, folder) = await V1Installed(v2HashOk: false);
        var before = EmuFixture.Read(folder, "emu.ini");

        var res = await inst.Refresh(emu, v2, receipts.Load("testemu", "base")!, onlyIfNewer: true);

        Assert.False(res.Ok);
        Assert.Contains("kept v1.0", res.Message);
        Assert.Equal(before, EmuFixture.Read(folder, "emu.ini"));
        Assert.Equal("1.0", receipts.Load("testemu", "base")!.Version);
    }

    [Fact]
    public async Task Update_with_an_unreadable_package_keeps_the_installed_version()
    {
        var paths = EmuFixture.TempPaths();
        var folder = EmuFixture.TempFolder();
        EmuFixture.Write(folder, "emu.ini", Fixture["emu.ini"]);
        var z1 = EmuFixture.Package(V1);
        var badZ2 = EmuFixture.Package("""
        { "edits": [ { "file": "emu.ini", "format": "toml", "section": "InputSources", "key": "XInput", "value": "yes" } ] }
        """);
        var v1 = EmuFixture.Option("base", z1, "1.0");
        var v2 = EmuFixture.Option("base", badZ2, "2.0");
        var emu = EmuFixture.Entry(v2);
        var (inst, receipts, _) = EmuFixture.MakeInstaller(paths, (v1, z1), (v2, badZ2));
        Assert.True((await inst.Install(emu, v1, folder)).Ok);
        var before = EmuFixture.Read(folder, "emu.ini");

        var res = await inst.Refresh(emu, v2, receipts.Load("testemu", "base")!, onlyIfNewer: true);

        Assert.False(res.Ok);
        Assert.Contains("kept v1.0", res.Message);
        Assert.Equal(before, EmuFixture.Read(folder, "emu.ini"));
        Assert.Equal("1.0", receipts.Load("testemu", "base")!.Version);
    }

    [Fact]
    public async Task Update_keeps_a_changed_installed_file_and_says_so()
    {
        var paths = EmuFixture.TempPaths();
        var folder = EmuFixture.TempFolder();
        var z1 = EmuFixture.Package(V1, ("ctrlr/vrlf.cfg", "V1CONTENT"));
        var z2 = EmuFixture.Package(V2, ("ctrlr/vrlf.cfg", "V2CONTENT"));
        var v1 = EmuFixture.Option("base", z1, "1.0");
        var v2 = EmuFixture.Option("base", z2, "2.0");
        var emu = EmuFixture.Entry(v2);
        var (inst, receipts, _) = EmuFixture.MakeInstaller(paths, (v1, z1), (v2, z2));
        Assert.True((await inst.Install(emu, v1, folder)).Ok);
        EmuFixture.Write(folder, "ctrlr/vrlf.cfg", "USER EDITED");   // changed since install

        var res = await inst.Refresh(emu, v2, receipts.Load("testemu", "base")!, onlyIfNewer: true);

        Assert.True(res.Ok, res.Message);
        Assert.Contains("kept your changed file(s)", res.Message);
        Assert.Contains("ctrlr/vrlf.cfg.bak-1.0", res.Message);
        // The same change also makes the underlying Uninstall warn as it removes the file - that
        // warning must carry through into Refresh's own message, not get swallowed.
        Assert.Contains("; warning: ctrlr/vrlf.cfg had been changed since install and was removed", res.Message);
        Assert.Equal("USER EDITED", EmuFixture.Read(folder, "ctrlr/vrlf.cfg.bak-1.0"));
        Assert.Equal("V2CONTENT", EmuFixture.Read(folder, "ctrlr/vrlf.cfg"));
    }

    [Fact]
    public async Task Update_that_fails_after_removing_says_so()
    {
        var paths = EmuFixture.TempPaths();
        var folder = EmuFixture.TempFolder();
        var z1 = EmuFixture.Package(V1);
        var z2 = EmuFixture.Package("""
        { "edits": [ { "file": "blocked.ini", "format": "ini", "section": "A", "key": "k", "value": "v" } ] }
        """);
        var v1 = EmuFixture.Option("base", z1, "1.0");
        var v2 = EmuFixture.Option("base", z2, "2.0");
        var emu = EmuFixture.Entry(v2);
        var (inst, receipts, _) = EmuFixture.MakeInstaller(paths, (v1, z1), (v2, z2));
        Assert.True((await inst.Install(emu, v1, folder)).Ok);
        Directory.CreateDirectory(EmuFixture.PathOf(folder, "blocked.ini"));  // v2's edit can't land here

        var res = await inst.Refresh(emu, v2, receipts.Load("testemu", "base")!, onlyIfNewer: true);

        Assert.False(res.Ok);
        Assert.Contains("removed v1.0", res.Message);
        Assert.Null(receipts.Load("testemu", "base"));
    }

    [Fact]
    public async Task Reapply_puts_our_value_back_and_still_remembers_the_original()
    {
        var (inst, receipts, emu, v1, _, folder) = await V1Installed();
        var t = SettingsText.Load(EmuFixture.PathOf(folder, "emu.ini"));
        IniEditor.Set(t, "InputSources", "XInput", "maybe");        // changed in the emulator's menus
        t.Save(EmuFixture.PathOf(folder, "emu.ini"));

        var res = await inst.Refresh(emu, v1, receipts.Load("testemu", "base")!, onlyIfNewer: false);

        Assert.True(res.Ok, res.Message);
        Assert.Contains("re-applied", res.Message);
        Assert.Equal("true", IniEditor.Get(SettingsText.Load(EmuFixture.PathOf(folder, "emu.ini")), "InputSources", "XInput"));
        inst.Uninstall(emu, receipts.Load("testemu", "base")!);
        Assert.Equal(Fixture["emu.ini"], EmuFixture.Read(folder, "emu.ini"));
    }
}
