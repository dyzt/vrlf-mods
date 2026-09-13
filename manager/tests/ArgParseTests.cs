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

    [Fact]
    public void Parses_config_list_and_set()
    {
        var a = Cli.Parse(new[] { "config", "reload" });
        Assert.Equal("config", a.Command);
        Assert.Equal("reload", a.Id);
        Assert.Null(a.Sub);

        var b = Cli.Parse(new[] { "config", "reload", "set", "HideWeapon", "off", "--game", "330370" });
        Assert.Equal("set", b.Sub);
        Assert.Equal("HideWeapon", b.Value);
        Assert.False(b.FlagOn);
        Assert.Equal(330370, b.Appid);
    }

    [Fact]
    public void Config_set_requires_on_or_off()
        => Assert.NotNull(Cli.Parse(new[] { "config", "reload", "set", "K", "maybe" }).Error);

    [Fact]
    public void Config_without_id_is_an_error()
        => Assert.NotNull(Cli.Parse(new[] { "config" }).Error);

    [Theory]
    [InlineData("install")]
    [InlineData("uninstall")]
    [InlineData("status")]
    public void Parses_virtualgun_subcommands(string sub)
    {
        var p = Cli.Parse(new[] { "virtualgun", sub, "--json" });
        Assert.Null(p.Error);
        Assert.Equal("virtualgun", p.Command);
        Assert.Equal(sub, p.Id);
        Assert.True(p.Json);
    }

    [Fact]
    public void Virtualgun_without_a_subcommand_means_status()
    {
        var p = Cli.Parse(new[] { "virtualgun" });
        Assert.Null(p.Error);
        Assert.Null(p.Id);
    }

    [Fact]
    public void Virtualgun_unknown_subcommand_is_an_error()
        => Assert.NotNull(Cli.Parse(new[] { "virtualgun", "reboot" }).Error);
}
