using System.IO.Compression;
using System.Text.Json;
using Xunit;

namespace VrlfMods.Tests;

/// <summary>Scripted answers; a null answer is a timeout.</summary>
public sealed class FakePrompt : IPrompt
{
    private readonly Queue<string?> _answers;
    public System.Text.StringBuilder Output { get; } = new();
    public int Reads { get; private set; }
    public FakePrompt(params string?[] answers) => _answers = new(answers);
    public void Write(string text) => Output.Append(text);
    public Task<string?> ReadLine(TimeSpan timeout)
    {
        Reads++;
        return Task.FromResult(_answers.Count > 0 ? _answers.Dequeue() : null);
    }
}

public sealed class FakeRestarter : IRestarter
{
    public bool Result { get; set; } = true;
    public int Calls { get; private set; }
    public bool Restart() { Calls++; return Result; }
}

public class PrereqsTests
{
    static readonly VigemInfo PadInfo = new("nefarius/ViGEmBus", "v1.22.0");

    static byte[] ReleaseZip()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        using (var w = new StreamWriter(zip.CreateEntry(VirtualGun.SetupExe).Open())) w.Write("exe");
        return ms.ToArray();
    }

    sealed record Rig(Prereqs Prereqs, FakePrompt Io, FakeElevatedRunner PadRunner,
        FakeElevatedRunner GunRunner, FakeHttpFetcher Http, FakeRestarter Restarter);

    static Rig Build(bool padInstalled, bool gunInstalled, FakePrompt io, bool offerGun = true,
        int padExit = 0, int gunExit = 0)
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "vrlf-pre-" + Guid.NewGuid().ToString("N")));
        var zip = ReleaseZip();
        var gunInfo = new VirtualGunInfo("dyzt/vrlf-virtual-gun", "v1.0.4", "vrlf-virtual-gun-1.0.4.zip",
            Installer.Sha256Hex(zip));
        var reg = new ModRegistry(1, PadInfo, new(), offerGun ? gunInfo : null);
        var http = new FakeHttpFetcher(new()
        {
            [RegistryLoader.RawBase + "/mods.json"] = JsonSerializer.SerializeToUtf8Bytes(reg, VrlfJson.Default.ModRegistry),
            [Vigem.InstallerUrl(PadInfo)] = new byte[] { 1, 2, 3 },
            [VirtualGun.ZipUrl(gunInfo)] = zip,
        });
        var padRunner = new FakeElevatedRunner(padExit);
        var gunRunner = new FakeElevatedRunner(gunExit);
        var vigem = new Vigem(new FakeServiceDetector(padInstalled), http, new FakeLauncher(), paths);
        var gun = new VirtualGun(new FakeVirtualGunState { Version = gunInstalled ? "1.0.4" : null },
            http, gunRunner, paths);
        var restarter = new FakeRestarter();
        return new(new Prereqs(new RegistryLoader(http, paths), vigem, gun, padRunner, io, restarter,
            TimeSpan.FromMilliseconds(1)), io, padRunner, gunRunner, http, restarter);
    }

    [Fact]
    public async Task Nothing_missing_exits_silently()
    {
        var rig = Build(padInstalled: true, gunInstalled: true, new FakePrompt());

        Assert.Equal(0, await rig.Prereqs.Run());
        Assert.Equal("", rig.Io.Output.ToString());
        Assert.Empty(rig.Http.Requested);
    }

    [Fact]
    public async Task Enter_and_yes_install_both_in_turn()
    {
        var rig = Build(padInstalled: false, gunInstalled: false, new FakePrompt("", "y", ""));

        Assert.Equal(0, await rig.Prereqs.Run());
        Assert.EndsWith("ViGEmBus_v1.22.0.exe", Assert.Single(rig.PadRunner.Runs).Exe);
        Assert.Equal("install", Assert.Single(rig.GunRunner.Runs).Args);
        Assert.Equal(3, rig.Io.Reads);   // two questions, then the restart question
        Assert.Contains("Restart now? [y/N]", rig.Io.Output.ToString());
        Assert.Equal(0, rig.Restarter.Calls);   // Enter alone does not restart
    }

    [Fact]
    public async Task Yes_to_restart_restarts_after_an_install()
    {
        var rig = Build(padInstalled: false, gunInstalled: true, new FakePrompt("y", "y"));

        Assert.Equal(0, await rig.Prereqs.Run());
        Assert.Equal(1, rig.Restarter.Calls);
        Assert.Contains("Restarting Windows. Start VRLF again afterwards.", rig.Io.Output.ToString());
    }

    [Fact]
    public async Task No_restart_question_when_nothing_installed()
    {
        var rig = Build(padInstalled: false, gunInstalled: true, new FakePrompt("y", "y"),
            padExit: VirtualGun.UacDeclined);

        Assert.Equal(0, await rig.Prereqs.Run());
        Assert.DoesNotContain("Restart now?", rig.Io.Output.ToString());
        Assert.Contains("Press Enter to start VRLF.", rig.Io.Output.ToString());
        Assert.Equal(0, rig.Restarter.Calls);
    }

    [Fact]
    public async Task A_restart_that_fails_says_so_and_waits()
    {
        var rig = Build(padInstalled: false, gunInstalled: true, new FakePrompt("y", "y", ""));
        rig.Restarter.Result = false;

        Assert.Equal(0, await rig.Prereqs.Run());
        Assert.Contains("Windows did not restart. Restart it before playing.", rig.Io.Output.ToString());
        Assert.Equal(3, rig.Io.Reads);
    }

    [Fact]
    public async Task No_restart_question_after_a_timeout()
    {
        // ViGEmBus accepted, then nobody answers the Virtual Lightgun question: nobody is there
        // to answer a restart either.
        var rig = Build(padInstalled: false, gunInstalled: false, new FakePrompt("y", null));

        Assert.Equal(0, await rig.Prereqs.Run());
        Assert.Single(rig.PadRunner.Runs);
        Assert.DoesNotContain("Restart now?", rig.Io.Output.ToString());
        Assert.Equal(0, rig.Restarter.Calls);
        Assert.Equal(2, rig.Io.Reads);
    }

    [Fact]
    public async Task No_installs_nothing_and_does_not_wait()
    {
        var rig = Build(padInstalled: false, gunInstalled: false, new FakePrompt("n", "no"));

        Assert.Equal(0, await rig.Prereqs.Run());
        Assert.Empty(rig.PadRunner.Runs);
        Assert.Empty(rig.GunRunner.Runs);
        Assert.Equal(2, rig.Io.Reads);
    }

    [Fact]
    public async Task A_timeout_skips_the_rest()
    {
        var rig = Build(padInstalled: false, gunInstalled: false, new FakePrompt(null, "y"));

        Assert.Equal(0, await rig.Prereqs.Run());
        Assert.Empty(rig.PadRunner.Runs);
        Assert.Empty(rig.GunRunner.Runs);
        Assert.Equal(1, rig.Io.Reads);
        Assert.DoesNotContain("Install Virtual Lightgun?", rig.Io.Output.ToString());
    }

    [Fact]
    public async Task Only_the_missing_driver_is_offered()
    {
        var rig = Build(padInstalled: true, gunInstalled: false, new FakePrompt("y", ""));

        Assert.Equal(0, await rig.Prereqs.Run());
        Assert.DoesNotContain("Install ViGEmBus?", rig.Io.Output.ToString());
        Assert.Empty(rig.PadRunner.Runs);
        Assert.Single(rig.GunRunner.Runs);
    }

    [Fact]
    public async Task A_registry_without_the_virtual_gun_offers_only_ViGEmBus()
    {
        var rig = Build(padInstalled: false, gunInstalled: false, new FakePrompt("y", ""), offerGun: false);

        Assert.Equal(0, await rig.Prereqs.Run());
        Assert.DoesNotContain("Install Virtual Lightgun?", rig.Io.Output.ToString());
        Assert.Single(rig.PadRunner.Runs);
    }

    [Fact]
    public async Task A_failed_install_still_exits_0_and_says_how_to_retry()
    {
        var rig = Build(padInstalled: false, gunInstalled: true, new FakePrompt("y", ""),
            padExit: VirtualGun.UacDeclined);

        Assert.Equal(0, await rig.Prereqs.Run());
        var text = rig.Io.Output.ToString();
        Assert.Contains("cancelled at the UAC prompt", text);
        Assert.Contains("Run vrlf-mods.exe in the VRLF folder to try again", text);
    }

    [Fact]
    public async Task Enter_alone_does_not_install_the_Virtual_Lightgun()
    {
        var rig = Build(padInstalled: true, gunInstalled: false, new FakePrompt(""));

        Assert.Equal(0, await rig.Prereqs.Run());
        Assert.Contains("Install Virtual Lightgun? [y/N]", rig.Io.Output.ToString());
        Assert.Empty(rig.GunRunner.Runs);
    }

    [Fact]
    public async Task One_driver_throwing_still_installs_the_other_and_waits()
    {
        var rig = Build(padInstalled: false, gunInstalled: false, new FakePrompt("y", "y", ""));
        rig.PadRunner.Throws = new InvalidOperationException("boom");

        Assert.Equal(0, await rig.Prereqs.Run());
        Assert.Single(rig.GunRunner.Runs);
        Assert.Contains("boom. Run vrlf-mods.exe in the VRLF folder to try again", rig.Io.Output.ToString());
        Assert.Equal(3, rig.Io.Reads);   // the result stays on screen until the restart question
    }

    [Theory]
    [InlineData(0, true, "ViGEmBus installed")]
    [InlineData(3010, true, "restart Windows")]
    [InlineData(VirtualGun.UacDeclined, false, "UAC prompt")]
    [InlineData(1602, false, "install cancelled")]
    [InlineData(1603, false, "exit 1603")]
    public async Task InstallAndWait_reports_the_installer_exit(int exit, bool ok, string message)
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "vrlf-vig-" + Guid.NewGuid().ToString("N")));
        var vig = new Vigem(new FakeServiceDetector(false), new AnyUrlFetcher(new byte[] { 1 }), new FakeLauncher(), paths);

        var res = await vig.InstallAndWait(PadInfo, new FakeElevatedRunner(exit));

        Assert.Equal(ok, res.Ok);
        Assert.Contains(message, res.Message);
    }

    [Fact]
    public async Task InstallAndWait_skips_an_installed_driver()
    {
        var runner = new FakeElevatedRunner(0);
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "vrlf-vig-" + Guid.NewGuid().ToString("N")));
        var vig = new Vigem(new FakeServiceDetector(true), new AnyUrlFetcher(new byte[] { 1 }), new FakeLauncher(), paths);

        Assert.True((await vig.InstallAndWait(PadInfo, runner)).Ok);
        Assert.Empty(runner.Runs);
    }

    sealed class HeldReader : TextReader
    {
        public readonly ManualResetEventSlim Release = new();
        public int Reads;
        public override string? ReadLine() { Interlocked.Increment(ref Reads); Release.Wait(); return "late"; }
    }

    [Fact]
    public async Task ConsolePrompt_times_out_then_hands_the_same_read_to_the_next_ask()
    {
        var orig = Console.In;
        var reader = new HeldReader();
        Console.SetIn(reader);
        try
        {
            var io = new ConsolePrompt();
            Assert.Null(await io.ReadLine(TimeSpan.FromMilliseconds(50)));
            reader.Release.Set();
            Assert.Equal("late", await io.ReadLine(TimeSpan.FromSeconds(5)));
            Assert.Equal(1, reader.Reads);
        }
        finally { reader.Release.Set(); Console.SetIn(orig); }
    }

    [Fact]
    public void Cli_parses_prereqs_and_rejects_arguments()
    {
        Assert.Null(Cli.Parse(new[] { "prereqs" }).Error);
        Assert.Equal("prereqs", Cli.Parse(new[] { "PREREQS" }).Command);
        Assert.NotNull(Cli.Parse(new[] { "prereqs", "now" }).Error);
    }
}
