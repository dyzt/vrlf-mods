using Xunit;

namespace VrlfMods.Tests;

public class ArgParseTests
{
    [Fact]
    public void Parses_install_with_appid_and_json()
    {
        var p = Cli.Parse(new[] { "install", "heavy-fire", "--game", "305980", "--json" });
        Assert.Equal("install", p.Command);
        Assert.Equal("heavy-fire", p.Id);
        Assert.Equal(305980, p.Appid);
        Assert.True(p.Json);
        Assert.Null(p.Error);
    }

    [Fact]
    public void Parses_install_with_path()
    {
        var p = Cli.Parse(new[] { "install", "reload", "--path", @"D:\Games\Reload" });
        Assert.Equal(@"D:\Games\Reload", p.Path);
    }

    [Fact]
    public void No_args_is_the_menu_command()
        => Assert.Equal("menu", Cli.Parse(Array.Empty<string>()).Command);

    [Fact]
    public void Install_without_id_is_an_error()
    {
        var p = Cli.Parse(new[] { "install" });
        Assert.NotNull(p.Error);
    }

    [Fact]
    public void Bad_appid_is_an_error()
    {
        var p = Cli.Parse(new[] { "install", "x", "--game", "notanumber" });
        Assert.NotNull(p.Error);
    }
}
