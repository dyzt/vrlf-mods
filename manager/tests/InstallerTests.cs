using System.IO.Compression;
using System.Text;
using Xunit;

namespace VrlfMods.Tests;

public class InstallerTests
{
    static AppPaths TempPaths() =>
        new(Path.Combine(Path.GetTempPath(), "vrlf-inst-" + Guid.NewGuid().ToString("N")));

    static string TempGameDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "vrlf-game-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    // Build a zip whose entries use backslash separators (as the BBH zip does).
    static byte[] MakeZip(params (string name, string content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, content) in entries)
            {
                var e = zip.CreateEntry(name);
                using var w = new StreamWriter(e.Open());
                w.Write(content);
            }
        return ms.ToArray();
    }

    static ModEntry Mod(byte[] zip, string ver = "1.0", string? configFile = null) =>
        new("demo", "Demo", ver, "mods/demo/dist/demo.zip",
            Installer.Sha256Hex(zip), new() { new GameRef(1, "Demo") }, false, null,
            configFile is null ? null : new ConfigManifest(configFile, "kv", new()));

    static Installer For(ModEntry mod, byte[] zip, AppPaths paths, ReceiptStore store) =>
        new(new FakeHttpFetcher(new() { [RegistryLoader.ZipUrl(mod)] = zip }), paths, store);

    const string Cfg = "BepInEx/plugins/demo.cfg";
    static string CfgPath(string game) => Path.Combine(game, "BepInEx", "plugins", "demo.cfg");

    [Fact]
    public async Task Install_extracts_files_and_writes_receipt()
    {
        var paths = TempPaths();
        var game = TempGameDir();
        var zip = MakeZip((@"winhttp.dll", "DLL"), (@"BepInEx\config\x.cfg", "CFG"));
        var mod = Mod(zip);
        var http = new FakeHttpFetcher(new() { [RegistryLoader.ZipUrl(mod)] = zip });
        var installer = new Installer(http, paths, new ReceiptStore(paths));

        var res = await installer.Install(mod, new GameTarget("1", game));

        Assert.True(res.Ok, res.Message);
        Assert.Equal("DLL", File.ReadAllText(Path.Combine(game, "winhttp.dll")));
        Assert.Equal("CFG", File.ReadAllText(Path.Combine(game, "BepInEx", "config", "x.cfg")));
        var rcpt = new ReceiptStore(paths).Load("demo", "1")!;
        Assert.Equal(2, rcpt.Files.Count);
    }

    [Fact]
    public async Task Install_backs_up_a_preexisting_file()
    {
        var paths = TempPaths();
        var game = TempGameDir();
        File.WriteAllText(Path.Combine(game, "winhttp.dll"), "ORIGINAL");
        var zip = MakeZip((@"winhttp.dll", "MODDED"));
        var mod = Mod(zip);
        var http = new FakeHttpFetcher(new() { [RegistryLoader.ZipUrl(mod)] = zip });
        var installer = new Installer(http, paths, new ReceiptStore(paths));

        await installer.Install(mod, new GameTarget("1", game));

        Assert.Equal("MODDED", File.ReadAllText(Path.Combine(game, "winhttp.dll")));
        var backup = Path.Combine(paths.BackupDir("demo", "1"), "winhttp.dll");
        Assert.Equal("ORIGINAL", File.ReadAllText(backup));
        var rcpt = new ReceiptStore(paths).Load("demo", "1")!;
        Assert.Single(rcpt.Backups);
    }

    [Fact]
    public async Task Sha256_mismatch_aborts_with_no_files_written()
    {
        var paths = TempPaths();
        var game = TempGameDir();
        var zip = MakeZip((@"winhttp.dll", "DLL"));
        var mod = new ModEntry("demo", "Demo", "1.0", "mods/demo/dist/demo.zip",
            "deadbeef" /* wrong hash */, new() { new GameRef(1, "Demo") }, false, null);
        var http = new FakeHttpFetcher(new() { [RegistryLoader.ZipUrl(mod)] = zip });
        var installer = new Installer(http, paths, new ReceiptStore(paths));

        var res = await installer.Install(mod, new GameTarget("1", game));

        Assert.False(res.Ok);
        Assert.Contains("sha256", res.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(game, "winhttp.dll")));
        Assert.Null(new ReceiptStore(paths).Load("demo", "1"));
    }

    [Fact]
    public async Task Zip_slip_entry_aborts_and_rolls_back()
    {
        var paths = TempPaths();
        var game = TempGameDir();
        var zip = MakeZip((@"good.dll", "OK"), (@"..\evil.dll", "PWN"));
        var mod = Mod(zip);
        var http = new FakeHttpFetcher(new() { [RegistryLoader.ZipUrl(mod)] = zip });
        var installer = new Installer(http, paths, new ReceiptStore(paths));

        var res = await installer.Install(mod, new GameTarget("1", game));

        Assert.False(res.Ok);
        Assert.False(File.Exists(Path.Combine(game, "good.dll")));           // rolled back
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(game)!, "evil.dll")));
        Assert.Null(new ReceiptStore(paths).Load("demo", "1"));
    }

    [Fact]
    public async Task Repeat_install_preserves_the_original_backup()
    {
        var paths = TempPaths();
        var game = TempGameDir();
        File.WriteAllText(Path.Combine(game, "winhttp.dll"), "ORIGINAL");
        var zip = MakeZip((@"winhttp.dll", "MODDED"));
        var mod = Mod(zip);
        var http = new FakeHttpFetcher(new() { [RegistryLoader.ZipUrl(mod)] = zip });
        var store = new ReceiptStore(paths);
        var installer = new Installer(http, paths, store);

        await installer.Install(mod, new GameTarget("1", game));   // first
        await installer.Install(mod, new GameTarget("1", game));   // repeat, no uninstall

        var backup = Path.Combine(paths.BackupDir("demo", "1"), "winhttp.dll");
        Assert.Equal("ORIGINAL", File.ReadAllText(backup));        // NOT overwritten with MODDED
        Assert.Single(store.Load("demo", "1")!.Backups);           // still references the original
    }

    [Fact]
    public async Task Receipt_save_failure_rolls_back_extracted_files()
    {
        var paths = TempPaths();
        var game = TempGameDir();
        var zip = MakeZip((@"a.dll", "A"));
        var mod = Mod(zip);
        var http = new FakeHttpFetcher(new() { [RegistryLoader.ZipUrl(mod)] = zip });
        var store = new ReceiptStore(paths);
        // Force ReceiptStore.Save to throw: pre-create a DIRECTORY where the receipt file must go.
        Directory.CreateDirectory(paths.ReceiptPath("demo", "1"));
        var installer = new Installer(http, paths, store);

        var res = await installer.Install(mod, new GameTarget("1", game));

        Assert.False(res.Ok);
        Assert.False(File.Exists(Path.Combine(game, "a.dll")));    // rolled back
    }

    [Fact]
    public async Task Failed_repeat_install_does_not_revert_or_lose_the_original()
    {
        var paths = TempPaths();
        var game = TempGameDir();
        File.WriteAllText(Path.Combine(game, "winhttp.dll"), "ORIGINAL");
        var v1 = MakeZip((@"winhttp.dll", "MODDED"));
        var mod1 = Mod(v1);
        var store = new ReceiptStore(paths);
        await new Installer(new FakeHttpFetcher(new() { [RegistryLoader.ZipUrl(mod1)] = v1 }), paths, store)
            .Install(mod1, new GameTarget("1", game));                     // install #1 ok

        // install #2: re-writes winhttp then hits a zip-slip entry → throws mid-extract
        var v2 = MakeZip((@"winhttp.dll", "MODDED2"), (@"..\evil.dll", "PWN"));
        var mod2 = Mod(v2);
        var res = await new Installer(new FakeHttpFetcher(new() { [RegistryLoader.ZipUrl(mod2)] = v2 }), paths, store)
            .Install(mod2, new GameTarget("1", game));

        Assert.False(res.Ok);
        Assert.Equal("ORIGINAL", File.ReadAllText(Path.Combine(paths.BackupDir("demo", "1"), "winhttp.dll"))); // original safe
        Assert.NotEqual("ORIGINAL", File.ReadAllText(Path.Combine(game, "winhttp.dll")));                       // prior install NOT reverted to vanilla
    }

    [Fact]
    public async Task Uninstall_removes_files_and_restores_backup()
    {
        var paths = TempPaths();
        var game = TempGameDir();
        File.WriteAllText(Path.Combine(game, "winhttp.dll"), "ORIGINAL");
        var zip = MakeZip((@"winhttp.dll", "MODDED"), (@"BepInEx\core\x.dll", "CORE"));
        var mod = Mod(zip);
        var http = new FakeHttpFetcher(new() { [RegistryLoader.ZipUrl(mod)] = zip });
        var store = new ReceiptStore(paths);
        var installer = new Installer(http, paths, store);
        await installer.Install(mod, new GameTarget("1", game));

        var res = installer.Uninstall(store.Load("demo", "1")!);

        Assert.True(res.Ok, res.Message);
        Assert.Equal("ORIGINAL", File.ReadAllText(Path.Combine(game, "winhttp.dll"))); // restored
        Assert.False(Directory.Exists(Path.Combine(game, "BepInEx")));                 // pruned
        Assert.Null(store.Load("demo", "1"));                                          // receipt gone
    }

    [Fact]
    public async Task Uninstall_warns_on_user_modified_file_but_still_removes()
    {
        var paths = TempPaths();
        var game = TempGameDir();
        var zip = MakeZip((@"cfg.ini", "DEFAULT"));
        var mod = Mod(zip);
        var http = new FakeHttpFetcher(new() { [RegistryLoader.ZipUrl(mod)] = zip });
        var store = new ReceiptStore(paths);
        var installer = new Installer(http, paths, store);
        await installer.Install(mod, new GameTarget("1", game));
        File.WriteAllText(Path.Combine(game, "cfg.ini"), "USER EDITED");   // change after install

        var res = installer.Uninstall(store.Load("demo", "1")!);

        Assert.True(res.Ok);
        Assert.Contains("modified", res.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(game, "cfg.ini")));
    }

    [Fact]
    public async Task Update_noop_when_version_matches()
    {
        var paths = TempPaths();
        var game = TempGameDir();
        var zip = MakeZip((@"a.dll", "A"));
        var mod = Mod(zip, "1.0");
        var http = new FakeHttpFetcher(new() { [RegistryLoader.ZipUrl(mod)] = zip });
        var store = new ReceiptStore(paths);
        var installer = new Installer(http, paths, store);
        await installer.Install(mod, new GameTarget("1", game));

        var res = await installer.Update(mod, store.Load("demo", "1")!);
        Assert.True(res.Ok);
        Assert.Contains("up to date", res.Message);
    }

    [Fact]
    public async Task Update_reinstalls_and_preserves_user_edits()
    {
        var paths = TempPaths();
        var game = TempGameDir();
        var v1 = MakeZip((@"a.dll", "A1"), (@"cfg.ini", "DEF"));
        var mod1 = Mod(v1, "1.0");
        var http1 = new FakeHttpFetcher(new() { [RegistryLoader.ZipUrl(mod1)] = v1 });
        var store = new ReceiptStore(paths);
        await new Installer(http1, paths, store).Install(mod1, new GameTarget("1", game));
        File.WriteAllText(Path.Combine(game, "cfg.ini"), "USER");          // user edit

        var v2 = MakeZip((@"a.dll", "A2"), (@"cfg.ini", "DEF"));
        var mod2 = Mod(v2, "2.0");
        var http2 = new FakeHttpFetcher(new() { [RegistryLoader.ZipUrl(mod2)] = v2 });
        var installer2 = new Installer(http2, paths, store);

        var res = await installer2.Update(mod2, store.Load("demo", "1")!);

        Assert.True(res.Ok, res.Message);
        Assert.Equal("A2", File.ReadAllText(Path.Combine(game, "a.dll")));  // upgraded
        Assert.True(File.Exists(Path.Combine(game, "cfg.ini.bak-1.0")));    // user edit preserved
        Assert.Contains("cfg.ini.bak-1.0", res.Message);
        Assert.Equal("2.0", store.Load("demo", "1")!.Version);             // receipt bumped
    }

    // ---- the mod's config file (mods.json config.file) is seeded, never overwritten ----

    [Fact]
    public async Task Install_seeds_the_config_when_it_is_absent()
    {
        var paths = TempPaths();
        var game = TempGameDir();
        var zip = MakeZip((@"a.dll", "A"), (@"BepInEx\plugins\demo.cfg", "DEFAULT"));
        var mod = Mod(zip, configFile: Cfg);
        var store = new ReceiptStore(paths);

        var res = await For(mod, zip, paths, store).Install(mod, new GameTarget("1", game));

        Assert.True(res.Ok, res.Message);
        Assert.Equal("DEFAULT", File.ReadAllText(CfgPath(game)));
        Assert.DoesNotContain("kept your settings", res.Message);
    }

    [Fact]
    public async Task Reinstall_keeps_the_players_config()
    {
        var paths = TempPaths();
        var game = TempGameDir();
        var zip = MakeZip((@"a.dll", "A"), (@"BepInEx\plugins\demo.cfg", "DEFAULT"));
        var mod = Mod(zip, configFile: Cfg);
        var store = new ReceiptStore(paths);
        var installer = For(mod, zip, paths, store);
        await installer.Install(mod, new GameTarget("1", game));
        File.WriteAllText(CfgPath(game), "PLAYER");
        File.WriteAllText(Path.Combine(game, "a.dll"), "DAMAGED");

        var res = await installer.Install(mod, new GameTarget("1", game));

        Assert.True(res.Ok, res.Message);
        Assert.Equal("PLAYER", File.ReadAllText(CfgPath(game)));            // settings survive
        Assert.Equal("A", File.ReadAllText(Path.Combine(game, "a.dll")));   // everything else repaired
        Assert.Contains("kept your settings", res.Message);
    }

    [Fact]
    public async Task Install_keeps_a_config_the_plugin_already_created_and_uninstall_removes_it()
    {
        var paths = TempPaths();
        var game = TempGameDir();
        Directory.CreateDirectory(Path.GetDirectoryName(CfgPath(game))!);
        File.WriteAllText(CfgPath(game), "PLUGIN MADE");
        var zip = MakeZip((@"a.dll", "A"), (@"BepInEx\plugins\demo.cfg", "DEFAULT"));
        var mod = Mod(zip, configFile: Cfg);
        var store = new ReceiptStore(paths);
        var installer = For(mod, zip, paths, store);

        await installer.Install(mod, new GameTarget("1", game));

        Assert.Equal("PLUGIN MADE", File.ReadAllText(CfgPath(game)));
        var rcpt = store.Load("demo", "1")!;
        Assert.Empty(rcpt.Backups);                                          // nothing to restore over it
        Assert.Contains(rcpt.Files, f => f.RelPath == Cfg);                  // but it is the mod's file
        File.WriteAllText(CfgPath(game), "PLAYER");

        var un = installer.Uninstall(rcpt, Installer.ConfigRel(mod));

        Assert.True(un.Ok, un.Message);
        Assert.False(File.Exists(CfgPath(game)));
        Assert.DoesNotContain("modified", un.Message);                       // a changed cfg is expected
    }

    [Fact]
    public async Task Failed_install_leaves_a_kept_config_alone()
    {
        var paths = TempPaths();
        var game = TempGameDir();
        Directory.CreateDirectory(Path.GetDirectoryName(CfgPath(game))!);
        File.WriteAllText(CfgPath(game), "PLAYER");
        var zip = MakeZip((@"BepInEx\plugins\demo.cfg", "DEFAULT"), (@"..\evil.dll", "PWN"));
        var mod = Mod(zip, configFile: Cfg);

        var res = await For(mod, zip, paths, new ReceiptStore(paths)).Install(mod, new GameTarget("1", game));

        Assert.False(res.Ok);
        Assert.Equal("PLAYER", File.ReadAllText(CfgPath(game)));             // rollback never deletes it
    }

    [Fact]
    public async Task Update_keeps_the_config_in_place()
    {
        var paths = TempPaths();
        var game = TempGameDir();
        var store = new ReceiptStore(paths);
        var v1 = MakeZip((@"a.dll", "A1"), (@"BepInEx\plugins\demo.cfg", "DEFAULT1"));
        var mod1 = Mod(v1, "1.0", Cfg);
        await For(mod1, v1, paths, store).Install(mod1, new GameTarget("1", game));
        File.WriteAllText(CfgPath(game), "PLAYER");

        var v2 = MakeZip((@"a.dll", "A2"), (@"BepInEx\plugins\demo.cfg", "DEFAULT2"));
        var mod2 = Mod(v2, "2.0", Cfg);
        var res = await For(mod2, v2, paths, store).Update(mod2, store.Load("demo", "1")!);

        Assert.True(res.Ok, res.Message);
        Assert.Equal("A2", File.ReadAllText(Path.Combine(game, "a.dll")));
        Assert.Equal("PLAYER", File.ReadAllText(CfgPath(game)));
        Assert.False(File.Exists(CfgPath(game) + ".bak-1.0"));
        Assert.Contains("kept your settings", res.Message);
        Assert.Contains(store.Load("demo", "1")!.Files, f => f.RelPath == Cfg);
    }

    [Fact]
    public async Task Update_seeds_the_config_when_the_player_has_none()
    {
        var paths = TempPaths();
        var game = TempGameDir();
        var store = new ReceiptStore(paths);
        var v1 = MakeZip((@"a.dll", "A1"));                                   // shipped no cfg
        var mod1 = Mod(v1, "1.0", Cfg);
        await For(mod1, v1, paths, store).Install(mod1, new GameTarget("1", game));

        var v2 = MakeZip((@"a.dll", "A2"), (@"BepInEx\plugins\demo.cfg", "DEFAULT2"));
        var mod2 = Mod(v2, "2.0", Cfg);
        var res = await For(mod2, v2, paths, store).Update(mod2, store.Load("demo", "1")!);

        Assert.True(res.Ok, res.Message);
        Assert.Equal("DEFAULT2", File.ReadAllText(CfgPath(game)));
    }

    [Fact]
    public async Task Update_aborts_and_keeps_old_version_when_new_zip_unavailable()
    {
        var paths = TempPaths();
        var game = TempGameDir();
        var v1 = MakeZip((@"a.dll", "A1"));
        var mod1 = Mod(v1, "1.0");
        var store = new ReceiptStore(paths);
        await new Installer(new FakeHttpFetcher(new() { [RegistryLoader.ZipUrl(mod1)] = v1 }), paths, store)
            .Install(mod1, new GameTarget("1", game));

        // v2 in the registry, but the fetcher has NO mapping for its zip → download fails
        var v2 = MakeZip((@"a.dll", "A2"));
        var mod2 = Mod(v2, "2.0");
        var res = await new Installer(new FakeHttpFetcher(new()), paths, store)
            .Update(mod2, store.Load("demo", "1")!);

        Assert.False(res.Ok);
        Assert.Equal("A1", File.ReadAllText(Path.Combine(game, "a.dll")));  // old version intact
        Assert.Equal("1.0", store.Load("demo", "1")!.Version);             // receipt unchanged
    }
}
