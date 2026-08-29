using System.IO;
using System.IO.Compression;
using System.Text;
using Xunit;

namespace VrlfMods.Tests;

/// <summary>The manual game-path override, end to end through ModManager.</summary>
public class ModManagerPathTests
{
    const long Appid = 1;

    static AppPaths TempPaths() =>
        new(Path.Combine(Path.GetTempPath(), "vrlf-mmp-" + Guid.NewGuid().ToString("N")));

    static string RealDir(string? withFile = null)
    {
        var d = Path.Combine(Path.GetTempPath(), "vrlf-mmpdir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        if (withFile is not null) File.WriteAllText(Path.Combine(d, withFile), "x");
        return d;
    }

    static string MissingDir() =>
        Path.Combine(Path.GetTempPath(), "vrlf-mmpgone-" + Guid.NewGuid().ToString("N"));

    static byte[] MakeZip(string name, string content)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        { using var w = new StreamWriter(zip.CreateEntry(name).Open()); w.Write(content); }
        return ms.ToArray();
    }

    // A one-mod manager whose Steam library holds appid 1 at `steamDir` (pass null for
    // "Steam knows nothing about it").
    static (ModManager mm, ReceiptStore receipts, GamePathStore overrides) Build(
        AppPaths paths, string? steamDir = null)
    {
        var zip = MakeZip("m.dll", "M");
        var hash = Installer.Sha256Hex(zip);
        var mod = new ModEntry("demo", "Demo", "1.0", "mods/demo/dist/demo.zip", hash,
            new() { new GameRef(Appid, "Demo Game") }, false, null);
        var regJson =
            "{ \"schema\":1, \"vigembus\":{\"repo\":\"nefarius/ViGEmBus\",\"version\":\"v1.22.0\"}, \"mods\":[" +
            "{ \"id\":\"demo\",\"name\":\"Demo\",\"version\":\"1.0\",\"zip\":\"mods/demo/dist/demo.zip\"," +
            "\"sha256\":\"" + hash + "\",\"games\":[{\"appid\":1,\"name\":\"Demo Game\"}]," +
            "\"requiresVigembusForCoop\":false,\"notes\":null } ] }";
        var http = new FakeHttpFetcher(new()
        {
            [RegistryLoader.RawBase + "/mods.json"] = Encoding.UTF8.GetBytes(regJson),
            [RegistryLoader.ZipUrl(mod)] = zip,
        });
        var receipts = new ReceiptStore(paths);
        var overrides = new GamePathStore(paths);
        // The real SteamLocator only ever returns a directory that exists, so "Steam knows
        // nothing" is modelled by a stub that answers for a different appid entirely.
        var locator = new GameLocator(
            new SteamLocatorStub(steamDir is null ? -1 : Appid, steamDir ?? string.Empty), overrides);
        var mm = new ModManager(new RegistryLoader(http, paths), locator,
            new Installer(http, paths, receipts), receipts,
            new Vigem(new FakeServiceDetector(true), http, new FakeLauncher(), paths));
        return (mm, receipts, overrides);
    }

    static string Messages(ActionReport r) => string.Join(" ", r.Results.Select(x => x.Message));

    [Fact]
    public async Task Status_reports_a_steam_found_game_as_not_manual()
    {
        var (mm, _, _) = Build(TempPaths(), RealDir());
        var g = (await mm.Status("demo"))!.Games.Single();
        Assert.True(g.Detected);
        Assert.False(g.Manual);
    }

    [Fact]
    public async Task Status_reports_an_override_as_manual()
    {
        var paths = TempPaths();
        var manual = RealDir();
        var (mm, _, overrides) = Build(paths, RealDir());
        overrides.Set(Appid, manual);

        var g = (await mm.Status("demo"))!.Games.Single();
        Assert.True(g.Detected);
        Assert.True(g.Manual);
        Assert.Equal(manual, g.Path);
    }

    [Fact]
    public async Task Status_surfaces_a_stale_override_as_undetected_but_still_names_it()
    {
        var paths = TempPaths();
        var gone = MissingDir();
        var (mm, _, overrides) = Build(paths, RealDir());
        overrides.Set(Appid, gone);

        var g = (await mm.Status("demo"))!.Games.Single();
        Assert.False(g.Detected);           // don't pretend, and don't silently use the Steam copy
        Assert.True(g.Manual);
        Assert.Equal(gone, g.Path);         // named, so the user can see what to fix
    }

    [Fact]
    public async Task SetGamePath_persists_the_override()
    {
        var paths = TempPaths();
        var (mm, _, overrides) = Build(paths);
        var dir = RealDir("Game.exe");

        var rep = await mm.SetGamePath("demo", null, dir);
        Assert.True(rep.Ok);
        Assert.Equal(dir, overrides.Get(Appid));
    }

    [Fact]
    public async Task SetGamePath_rejects_a_missing_folder_and_stores_nothing()
    {
        var paths = TempPaths();
        var (mm, _, overrides) = Build(paths);

        var rep = await mm.SetGamePath("demo", null, MissingDir());
        Assert.False(rep.Ok);
        Assert.Null(overrides.Get(Appid));
    }

    [Fact]
    public async Task SetGamePath_accepts_a_folder_with_no_exe_but_says_so()
    {
        var paths = TempPaths();
        var (mm, _, overrides) = Build(paths);
        var dir = RealDir("readme.txt");

        var rep = await mm.SetGamePath("demo", null, dir);
        Assert.True(rep.Ok);
        Assert.Equal(dir, overrides.Get(Appid));
        Assert.Contains("no .exe", Messages(rep));
    }

    [Fact]
    public async Task SetGamePath_rejects_an_unknown_mod()
    {
        var (mm, _, _) = Build(TempPaths());
        Assert.False((await mm.SetGamePath("nope", null, RealDir())).Ok);
    }

    [Fact]
    public async Task ClearGamePath_drops_the_override_and_steam_detection_returns()
    {
        var paths = TempPaths();
        var steam = RealDir();
        var (mm, _, overrides) = Build(paths, steam);
        overrides.Set(Appid, RealDir());

        var rep = await mm.ClearGamePath("demo", null);
        Assert.True(rep.Ok);
        Assert.Null(overrides.Get(Appid));

        var g = (await mm.Status("demo"))!.Games.Single();
        Assert.Equal(steam, g.Path);
        Assert.False(g.Manual);
    }

    [Fact]
    public async Task Install_with_no_flags_uses_a_stored_override()
    {
        var paths = TempPaths();
        var manual = RealDir();
        var (mm, receipts, overrides) = Build(paths);          // Steam finds nothing
        overrides.Set(Appid, manual);

        var rep = await mm.Install("demo", null, null);
        Assert.True(rep.Ok);
        Assert.True(File.Exists(Path.Combine(manual, "m.dll")));
        Assert.Equal(manual, receipts.Load("demo", "1")!.GamePath);
    }

    [Fact]
    public async Task Install_with_a_path_keys_the_receipt_by_appid_not_by_a_custom_hash()
    {
        // This is what makes a manual install visible to status, config and update:
        // the game key stays the appid, exactly as a Steam install's does.
        var paths = TempPaths();
        var (mm, receipts, _) = Build(paths);
        var dir = RealDir("Game.exe");

        Assert.True((await mm.Install("demo", null, dir)).Ok);
        Assert.NotNull(receipts.Load("demo", "1"));
        Assert.DoesNotContain(receipts.All(), r => r.GameKey.StartsWith("custom-"));
    }

    [Fact]
    public async Task Install_with_a_path_remembers_it_for_next_time()
    {
        var paths = TempPaths();
        var (mm, _, overrides) = Build(paths);
        var dir = RealDir("Game.exe");

        await mm.Install("demo", null, dir);
        Assert.Equal(dir, overrides.Get(Appid));
    }

    [Fact]
    public async Task Install_with_a_missing_path_fails_and_stores_nothing()
    {
        var paths = TempPaths();
        var (mm, _, overrides) = Build(paths);

        Assert.False((await mm.Install("demo", null, MissingDir())).Ok);
        Assert.Null(overrides.Get(Appid));
    }

    [Fact]
    public async Task A_manual_install_is_reported_as_installed_by_status()
    {
        var paths = TempPaths();
        var (mm, _, _) = Build(paths);                          // Steam finds nothing
        var dir = RealDir("Game.exe");
        await mm.Install("demo", null, dir);

        var st = (await mm.Status("demo"))!;
        Assert.Equal("1.0", st.InstalledVersion);               // was invisible under custom- keys
        Assert.True(st.Games.Single().Installed);
    }

    [Fact]
    public async Task A_manual_install_can_be_uninstalled_without_naming_the_path_again()
    {
        var paths = TempPaths();
        var (mm, receipts, _) = Build(paths);
        var dir = RealDir("Game.exe");
        await mm.Install("demo", null, dir);

        Assert.True((await mm.Uninstall("demo", null)).Ok);
        Assert.Null(receipts.Load("demo", "1"));
        Assert.False(File.Exists(Path.Combine(dir, "m.dll")));
    }

    [Fact]
    public async Task Install_without_a_path_or_an_override_says_how_to_fix_it()
    {
        var (mm, _, _) = Build(TempPaths());                    // Steam finds nothing
        var rep = await mm.Install("demo", null, null);
        Assert.False(rep.Ok);
        Assert.Contains("path", Messages(rep));
    }

    [Fact]
    public async Task Install_when_the_override_has_gone_stale_names_the_missing_folder()
    {
        var paths = TempPaths();
        var gone = MissingDir();
        var (mm, _, overrides) = Build(paths, RealDir());        // Steam DOES have a copy
        overrides.Set(Appid, gone);

        var rep = await mm.Install("demo", null, null);
        Assert.False(rep.Ok);                                    // authoritative: don't use the Steam copy
        Assert.Contains(gone, Messages(rep));
    }
}
