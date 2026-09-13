using System.IO.Compression;
using Xunit;

namespace VrlfMods.Tests;

/// <summary>The composition seam VirtualGunTests.cs doesn't reach: ModManager.VirtualGunMenu
/// picking install vs. status off the loaded registry, and Program dispatching "virtualgun
/// install" through a ModManager wired the way Program.Main actually wires one.</summary>
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
    public async Task Menu_reports_status_without_installing_when_already_current()
    {
        var zip = ReleaseZip();
        var info = Info("v1.0.0", zip);
        var runner = new FakeElevatedRunner(0);
        var (mm, _, _) = DemoManager.Build(DemoManager.TempPaths(), virtualGunInfo: info, virtualGunZip: zip,
            virtualGunState: new FakeVirtualGunState { Version = "1.0.0" }, virtualGunRunner: runner);

        var report = await mm.VirtualGunMenu();

        Assert.True(report.Ok, string.Join("; ", report.Results.Select(r => r.Message)));
        Assert.Empty(runner.Runs);
        Assert.Contains("installed v1.0.0", Assert.Single(report.Results).Message);
    }

    [Fact]
    public async Task Menu_installs_when_not_installed()
    {
        var zip = ReleaseZip();
        var info = Info("v1.0.0", zip);
        var runner = new FakeElevatedRunner(0);
        var (mm, _, _) = DemoManager.Build(DemoManager.TempPaths(), virtualGunInfo: info, virtualGunZip: zip,
            virtualGunState: new FakeVirtualGunState(), virtualGunRunner: runner);

        var report = await mm.VirtualGunMenu();

        Assert.True(report.Ok, string.Join("; ", report.Results.Select(r => r.Message)));
        Assert.Single(runner.Runs);
    }

    [Fact]
    public async Task Menu_installs_when_the_pinned_version_differs_from_installed()
    {
        var zip = ReleaseZip();
        var info = Info("v1.1.0", zip);
        var runner = new FakeElevatedRunner(0);
        var (mm, _, _) = DemoManager.Build(DemoManager.TempPaths(), virtualGunInfo: info, virtualGunZip: zip,
            virtualGunState: new FakeVirtualGunState { Version = "1.0.0" }, virtualGunRunner: runner);

        var report = await mm.VirtualGunMenu();

        Assert.True(report.Ok, string.Join("; ", report.Results.Select(r => r.Message)));
        Assert.Single(runner.Runs);
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
