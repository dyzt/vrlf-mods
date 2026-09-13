using System.IO;
using System.IO.Compression;
using Xunit;

namespace VrlfMods.Tests;

public class VirtualGunTests
{
    static AppPaths TempPaths() =>
        new(Path.Combine(Path.GetTempPath(), "vrlf-vg-" + Guid.NewGuid().ToString("N")));

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

    static VirtualGunInfo Info(byte[] zip) =>
        new("dyzt/vrlf-virtual-gun", "v1.0.0", "vrlf-virtual-gun-1.0.0.zip", Installer.Sha256Hex(zip));

    static (VirtualGun gun, FakeElevatedRunner runner, AppPaths paths) Build(
        byte[]? download, VirtualGunInfo info, int exitCode = 0, FakeVirtualGunState? state = null)
    {
        var paths = TempPaths();
        var http = new FakeHttpFetcher(new() { [VirtualGun.ZipUrl(info)] = download });
        var runner = new FakeElevatedRunner(exitCode);
        var gun = new VirtualGun(state ?? new FakeVirtualGunState(), http, runner, paths);
        return (gun, runner, paths);
    }

    [Fact]
    public void Zip_url_is_the_github_release_asset()
    {
        var info = new VirtualGunInfo("dyzt/vrlf-virtual-gun", "v1.0.0", "vrlf-virtual-gun-1.0.0.zip", "x");
        Assert.Equal("https://github.com/dyzt/vrlf-virtual-gun/releases/download/v1.0.0/vrlf-virtual-gun-1.0.0.zip",
                     VirtualGun.ZipUrl(info));
    }

    [Fact]
    public void Installed_means_the_installer_wrote_a_version()
    {
        var info = Info(ReleaseZip());
        Assert.False(Build(null, info).gun.IsInstalled());
        Assert.True(Build(null, info, state: new FakeVirtualGunState { Version = "1.0.0" }).gun.IsInstalled());
    }

    [Fact]
    public async Task Install_verifies_extracts_and_runs_the_installer_elevated()
    {
        var zip = ReleaseZip();
        var info = Info(zip);
        var (gun, runner, _) = Build(zip, info);
        var r = await gun.Install(info);
        Assert.True(r.Ok, r.Message);
        Assert.Equal("virtualgun", r.GameKey);
        var run = Assert.Single(runner.Runs);
        Assert.Equal(Path.Combine(gun.StagingDir(info), VirtualGun.SetupExe), run.Exe);
        Assert.Equal("install", run.Args);
        Assert.True(File.Exists(Path.Combine(gun.StagingDir(info), "driver", "VRLFVirtualGun.inf")));
    }

    [Fact]
    public async Task Install_refuses_a_hash_mismatch_and_never_runs_anything()
    {
        var zip = ReleaseZip();
        var info = Info(zip) with { Sha256 = "0000" };
        var (gun, runner, _) = Build(zip, info);
        var r = await gun.Install(info);
        Assert.False(r.Ok);
        Assert.Contains("hash", r.Message);
        Assert.Empty(runner.Runs);
    }

    [Fact]
    public async Task Install_reports_a_failed_download()
    {
        var info = Info(ReleaseZip());
        var (gun, runner, _) = Build(null, info);
        var r = await gun.Install(info);
        Assert.False(r.Ok);
        Assert.Contains("download", r.Message);
        Assert.Empty(runner.Runs);
    }

    [Fact]
    public async Task Install_without_a_registry_entry_fails()
    {
        var info = Info(ReleaseZip());
        var (gun, runner, _) = Build(ReleaseZip(), info);
        var r = await gun.Install(null);
        Assert.False(r.Ok);
        Assert.Empty(runner.Runs);
    }

    [Fact]
    public async Task Install_exit_3010_succeeds_and_asks_for_a_restart()
    {
        var zip = ReleaseZip();
        var info = Info(zip);
        var (gun, _, _) = Build(zip, info, exitCode: 3010);
        var r = await gun.Install(info);
        Assert.True(r.Ok);
        Assert.Contains("restart", r.Message);
    }

    [Fact]
    public async Task Install_declined_at_uac_is_a_failure()
    {
        var zip = ReleaseZip();
        var info = Info(zip);
        var (gun, _, _) = Build(zip, info, exitCode: VirtualGun.UacDeclined);
        var r = await gun.Install(info);
        Assert.False(r.Ok);
        Assert.Contains("UAC", r.Message);
    }

    [Fact]
    public async Task Install_failure_points_at_the_setup_log()
    {
        var zip = ReleaseZip();
        var info = Info(zip);
        var (gun, _, _) = Build(zip, info, exitCode: 1);
        var r = await gun.Install(info);
        Assert.False(r.Ok);
        Assert.Contains("setup.log", r.Message);
    }

    [Fact]
    public void Uninstall_runs_the_installed_setup_elevated()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vrlf-vg-inst-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, VirtualGun.SetupExe), "exe");
        var info = Info(ReleaseZip());
        var (gun, runner, _) = Build(null, info, state: new FakeVirtualGunState { Version = "1.0.0", Dir = dir });
        var r = gun.Uninstall();
        Assert.True(r.Ok, r.Message);
        var run = Assert.Single(runner.Runs);
        Assert.Equal(Path.Combine(dir, VirtualGun.SetupExe), run.Exe);
        Assert.Equal("uninstall", run.Args);
    }

    [Fact]
    public void Uninstall_when_not_installed_does_nothing()
    {
        var info = Info(ReleaseZip());
        var (gun, runner, _) = Build(null, info);
        var r = gun.Uninstall();
        Assert.True(r.Ok);
        Assert.Contains("not installed", r.Message);
        Assert.Empty(runner.Runs);
    }

    [Fact]
    public void Uninstall_with_a_missing_uninstaller_says_so()
    {
        var info = Info(ReleaseZip());
        var (gun, runner, _) = Build(null, info, state: new FakeVirtualGunState { Version = "1.0.0", Dir = @"C:\nowhere\vg" });
        var r = gun.Uninstall();
        Assert.False(r.Ok);
        Assert.Contains("uninstaller is missing", r.Message);
        Assert.Empty(runner.Runs);
    }

    [Fact]
    public void Status_reports_an_available_update()
    {
        var info = Info(ReleaseZip()) with { Version = "v1.1.0" };
        var (gun, _, _) = Build(null, info, state: new FakeVirtualGunState { Version = "1.0.0", Dir = "x" });
        Assert.Equal("Virtual Lightgun: installed v1.0.0 (update to v1.1.0)", gun.Status(info).Message);
    }

    [Fact]
    public void Status_not_installed()
    {
        var info = Info(ReleaseZip());
        var (gun, _, _) = Build(null, info);
        Assert.Equal("Virtual Lightgun: not installed", gun.Status(info).Message);
    }
}
