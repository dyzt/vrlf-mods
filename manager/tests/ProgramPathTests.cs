using Xunit;

namespace VrlfMods.Tests;

/// <summary>The `path` command as dispatched by Program.Run — exit codes and effect.</summary>
public class ProgramPathTests
{
    static Task<int> Run(ModManager mm, params string[] args) => Program.Run(Cli.Parse(args), mm);

    [Fact]
    public async Task Showing_the_folder_succeeds()
    {
        var (mm, _, _) = DemoManager.Build(DemoManager.TempPaths(), DemoManager.RealDir());
        Assert.Equal(0, await Run(mm, "path", "demo"));
    }

    [Fact]
    public async Task Showing_the_folder_of_an_unknown_mod_fails()
    {
        var (mm, _, _) = DemoManager.Build(DemoManager.TempPaths());
        Assert.Equal(1, await Run(mm, "path", "nope"));
    }

    [Fact]
    public async Task Setting_the_folder_stores_it_and_succeeds()
    {
        var paths = DemoManager.TempPaths();
        var (mm, _, overrides) = DemoManager.Build(paths);
        var dir = DemoManager.RealDir("Game.exe");

        Assert.Equal(0, await Run(mm, "path", "demo", dir));
        Assert.Equal(dir, overrides.Get(DemoManager.Appid));
    }

    [Fact]
    public async Task Setting_a_missing_folder_fails_and_stores_nothing()
    {
        var paths = DemoManager.TempPaths();
        var (mm, _, overrides) = DemoManager.Build(paths);

        Assert.Equal(1, await Run(mm, "path", "demo", DemoManager.MissingDir()));
        Assert.Null(overrides.Get(DemoManager.Appid));
    }

    [Fact]
    public async Task Clearing_the_folder_removes_it()
    {
        var paths = DemoManager.TempPaths();
        var (mm, _, overrides) = DemoManager.Build(paths);
        overrides.Set(DemoManager.Appid, DemoManager.RealDir());

        Assert.Equal(0, await Run(mm, "path", "demo", "--clear"));
        Assert.Null(overrides.Get(DemoManager.Appid));
    }

    [Fact]
    public async Task The_json_form_of_show_succeeds()
    {
        var (mm, _, _) = DemoManager.Build(DemoManager.TempPaths(), DemoManager.RealDir());
        Assert.Equal(0, await Run(mm, "path", "demo", "--json"));
    }
}
