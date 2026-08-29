using System.IO;
using Xunit;

namespace VrlfMods.Tests;

public class GameLocatorTests
{
    static AppPaths TempPaths() =>
        new(Path.Combine(Path.GetTempPath(), "vrlf-loc-" + Guid.NewGuid().ToString("N")));

    static string RealDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "vrlf-locdir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    static GameLocator Locate(AppPaths paths, long steamAppid, string steamDir) =>
        new(new SteamLocatorStub(steamAppid, steamDir), new GamePathStore(paths));

    [Fact]
    public void With_no_override_it_returns_the_steam_dir()
    {
        var steam = RealDir();
        var found = Locate(TempPaths(), 1, steam).Find(1);
        Assert.Equal(steam, found!.Path);
        Assert.False(found.Manual);
    }

    [Fact]
    public void With_no_override_and_no_steam_copy_it_finds_nothing()
        => Assert.Null(Locate(TempPaths(), 1, RealDir()).Find(999));

    [Fact]
    public void An_override_wins_over_the_steam_copy()
    {
        var paths = TempPaths();
        var manual = RealDir();
        new GamePathStore(paths).Set(1, manual);

        var found = Locate(paths, 1, RealDir()).Find(1);
        Assert.Equal(manual, found!.Path);
        Assert.True(found.Manual);
    }

    [Fact]
    public void An_override_locates_a_game_steam_knows_nothing_about()
    {
        var paths = TempPaths();
        var manual = RealDir();
        new GamePathStore(paths).Set(999, manual);

        var found = Locate(paths, 1, RealDir()).Find(999);
        Assert.Equal(manual, found!.Path);
        Assert.True(found.Manual);
    }

    [Fact]
    public void A_stale_override_reports_nothing_found_rather_than_silently_using_steam()
    {
        // An override is authoritative: if the user pointed us somewhere that has since
        // gone, say so, rather than quietly installing into a different copy of the game.
        var paths = TempPaths();
        new GamePathStore(paths).Set(1, Path.Combine(Path.GetTempPath(), "vrlf-gone-" + Guid.NewGuid().ToString("N")));

        Assert.Null(Locate(paths, 1, RealDir()).Find(1));
    }

    [Fact]
    public void ManualPath_reports_the_configured_override_even_when_it_is_stale()
    {
        var paths = TempPaths();
        var gone = Path.Combine(Path.GetTempPath(), "vrlf-gone-" + Guid.NewGuid().ToString("N"));
        new GamePathStore(paths).Set(1, gone);

        Assert.Equal(gone, Locate(paths, 1, RealDir()).ManualPath(1));
    }

    [Fact]
    public void ManualPath_is_null_when_no_override_is_set()
        => Assert.Null(Locate(TempPaths(), 1, RealDir()).ManualPath(1));
}
