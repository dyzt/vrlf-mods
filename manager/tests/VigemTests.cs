using Xunit;

namespace VrlfMods.Tests;

public class VigemTests
{
    static AppPaths TempPaths() =>
        new(Path.Combine(Path.GetTempPath(), "vrlf-vig-" + Guid.NewGuid().ToString("N")));
    static readonly VigemInfo Info = new("nefarius/ViGEmBus", "v1.22.0");

    [Fact]
    public async Task Ensure_noop_when_already_installed()
    {
        var launcher = new FakeLauncher();
        var vig = new Vigem(new FakeServiceDetector(true), new FakeHttpFetcher(new()), launcher, TempPaths());
        var res = await vig.Ensure(Info);
        Assert.True(res.Ok);
        Assert.Contains("already", res.Message);
        Assert.Null(launcher.Launched);
    }

    [Fact]
    public async Task Ensure_downloads_and_launches_when_missing()
    {
        var launcher = new FakeLauncher();
        // Map any URL to fake installer bytes.
        var http = new AnyUrlFetcher(new byte[] { 1, 2, 3 });
        var vig = new Vigem(new FakeServiceDetector(false), http, launcher, TempPaths());
        var res = await vig.Ensure(Info);
        Assert.True(res.Ok, res.Message);
        Assert.NotNull(launcher.Launched);
        Assert.True(File.Exists(launcher.Launched!));
    }

    [Fact]
    public async Task Ensure_fails_when_download_fails()
    {
        var vig = new Vigem(new FakeServiceDetector(false),
            new AnyUrlFetcher(null), new FakeLauncher(), TempPaths());
        var res = await vig.Ensure(Info);
        Assert.False(res.Ok);
    }
}
