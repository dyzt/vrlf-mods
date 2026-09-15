using System.IO.Compression;
using Xunit;

namespace VrlfMods.Tests;

/// <summary>The composition seam VirtualGunTests.cs doesn't reach: ModManager reporting and
/// installing off the loaded registry, and Program dispatching "virtualgun install" through a
/// ModManager wired the way Program.Main actually wires one.</summary>
public class VirtualGunWiringTests
{
    static Task<int> Run(ModManager mm, params string[] args) => Program.Run(Cli.Parse(args), mm);

    static byte[] ReleaseZip()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var w = new StreamWriter(zip.CreateEntry(VirtualGun.SetupExe).Open())) w.Write("exe");
            using (var w = new StreamWriter(zip.CreateEntry("driver/VRLFVirtualGun.inf").Open())) w.Write("inf");
        }
        return ms.ToArray();
    }

    static VirtualGunInfo Info(string version, byte[] zip) =>
        new("dyzt/vrlf-virtual-gun", version, "vrlf-virtual-gun.zip", Installer.Sha256Hex(zip));

    [Fact]
    public async Task List_reports_the_installed_version_and_a_pending_update()
    {
        var zip = ReleaseZip();
        var (mm, _, _) = DemoManager.Build(DemoManager.TempPaths(), virtualGunInfo: Info("v1.1.0", zip), virtualGunZip: zip,
            virtualGunState: new FakeVirtualGunState { Version = "1.0.0" });

        var list = await mm.List();

        Assert.True(list.VirtualGunInstalled);
        Assert.Equal("1.0.0", list.VirtualGunVersion);
        Assert.Equal("1.1.0", list.VirtualGunUpdate);
    }

    [Fact]
    public async Task Install_runs_the_installer_even_when_already_current()
    {
        // The menu's Reinstall row: repairs a broken driver and re-pins the lane paths.
        var zip = ReleaseZip();
        var info = Info("v1.0.0", zip);
        var runner = new FakeElevatedRunner(0);
        var (mm, _, _) = DemoManager.Build(DemoManager.TempPaths(), virtualGunInfo: info, virtualGunZip: zip,
            virtualGunState: new FakeVirtualGunState { Version = "1.0.0" }, virtualGunRunner: runner);

        var report = await mm.VirtualGunInstall();

        Assert.True(report.Ok, string.Join("; ", report.Results.Select(r => r.Message)));
        Assert.Equal("install", Assert.Single(runner.Runs).Args);
    }

    [Fact]
    public async Task Program_virtualgun_install_fails_cleanly_with_no_virtualgun_block_in_the_registry()
    {
        // DemoManager's registry carries no "virtualgun" entry by default — a fresh vrlf-mods
        // release before this branch merges — but Program.Main always wires a VirtualGun in,
        // so this must fail through VirtualGun.Install's own null check, not an exception.
        var (mm, _, _) = DemoManager.Build(DemoManager.TempPaths());

        Assert.Equal(1, await Run(mm, "virtualgun", "install"));
    }
}
