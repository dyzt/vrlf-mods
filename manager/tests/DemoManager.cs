using System.IO;
using System.IO.Compression;
using System.Text;

namespace VrlfMods.Tests;

using VrlfMods;

/// <summary>
/// A ModManager over one fake mod ("demo", appid 1) whose zip drops a single m.dll.
/// Shared by the tests that exercise game-path resolution end to end.
/// </summary>
public static class DemoManager
{
    public const long Appid = 1;
    public const string ModId = "demo";

    public static AppPaths TempPaths() =>
        new(Path.Combine(Path.GetTempPath(), "vrlf-demo-" + Guid.NewGuid().ToString("N")));

    public static string RealDir(string? withFile = null)
    {
        var d = Path.Combine(Path.GetTempPath(), "vrlf-demodir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        if (withFile is not null) File.WriteAllText(Path.Combine(d, withFile), "x");
        return d;
    }

    public static string MissingDir() =>
        Path.Combine(Path.GetTempPath(), "vrlf-demogone-" + Guid.NewGuid().ToString("N"));

    static byte[] MakeZip(string name, string content)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        { using var w = new StreamWriter(zip.CreateEntry(name).Open()); w.Write(content); }
        return ms.ToArray();
    }

    /// <param name="steamDir">Where Steam finds appid 1, or null for "Steam knows nothing".</param>
    /// <param name="virtualGunInfo">Pins a registry "virtualgun" block when given; the registry
    /// carries none by default, matching a fresh vrlf-mods release without support wired in yet.</param>
    /// <param name="virtualGunZip">The release zip served at that pinned version's download URL,
    /// needed only by tests that expect Install to run to completion.</param>
    public static (ModManager mm, ReceiptStore receipts, GamePathStore overrides) Build(
        AppPaths paths, string? steamDir = null,
        VirtualGunInfo? virtualGunInfo = null, byte[]? virtualGunZip = null,
        IVirtualGunState? virtualGunState = null, IElevatedRunner? virtualGunRunner = null)
    {
        var zip = MakeZip("m.dll", "M");
        var hash = Installer.Sha256Hex(zip);
        var mod = new ModEntry(ModId, "Demo", "1.0", "mods/demo/dist/demo.zip", hash,
            new() { new GameRef(Appid, "Demo Game") }, false, null);
        var vgJson = virtualGunInfo is null ? "" :
            ", \"virtualgun\":{\"repo\":\"" + virtualGunInfo.Repo + "\",\"version\":\"" + virtualGunInfo.Version +
            "\",\"zip\":\"" + virtualGunInfo.Zip + "\",\"sha256\":\"" + virtualGunInfo.Sha256 + "\"}";
        var regJson =
            "{ \"schema\":1, \"vigembus\":{\"repo\":\"nefarius/ViGEmBus\",\"version\":\"v1.22.0\"}, \"mods\":[" +
            "{ \"id\":\"demo\",\"name\":\"Demo\",\"version\":\"1.0\",\"zip\":\"mods/demo/dist/demo.zip\"," +
            "\"sha256\":\"" + hash + "\",\"games\":[{\"appid\":1,\"name\":\"Demo Game\"}]," +
            "\"requiresVigembusForCoop\":false,\"notes\":null } ]" + vgJson + " }";
        var httpMap = new Dictionary<string, byte[]?>
        {
            [RegistryLoader.RawBase + "/mods.json"] = Encoding.UTF8.GetBytes(regJson),
            [RegistryLoader.ZipUrl(mod)] = zip,
        };
        if (virtualGunInfo is not null && virtualGunZip is not null)
            httpMap[VirtualGun.ZipUrl(virtualGunInfo)] = virtualGunZip;
        var http = new FakeHttpFetcher(httpMap);
        var receipts = new ReceiptStore(paths);
        var overrides = new GamePathStore(paths);
        // The real SteamLocator only ever returns a directory that exists, so "Steam knows
        // nothing" is modelled by a stub that answers for a different appid entirely.
        var locator = new GameLocator(
            new SteamLocatorStub(steamDir is null ? -1 : Appid, steamDir ?? string.Empty), overrides);
        var virtualGun = new VirtualGun(virtualGunState ?? new FakeVirtualGunState(), http,
            virtualGunRunner ?? new FakeElevatedRunner(0), paths);
        var mm = new ModManager(new RegistryLoader(http, paths), locator,
            new Installer(http, paths, receipts), receipts,
            new Vigem(new FakeServiceDetector(true), http, new FakeLauncher(), paths),
            virtualGun: virtualGun);
        return (mm, receipts, overrides);
    }
}
