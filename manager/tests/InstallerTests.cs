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

    static ModEntry Mod(byte[] zip, string ver = "1.0") =>
        new("demo", "Demo", ver, "mods/demo/dist/demo.zip",
            Installer.Sha256Hex(zip), new() { new GameRef(1, "Demo") }, false, null);

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
}
