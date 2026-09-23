using Xunit;

namespace VrlfMods.Tests;

public class ArgParseEmulatorTests
{
    static ParsedArgs P(params string[] a) => Cli.Parse(a);

    [Fact]
    public void List_parses()
    {
        var p = P("emulator", "list", "--json");
        Assert.Null(p.Error);
        Assert.Equal("emulator", p.Command);
        Assert.Equal("list", p.Sub);
        Assert.True(p.Json);
    }

    [Fact]
    public void Install_takes_an_optional_option()
    {
        var a = P("emulator", "install", "dolphin");
        Assert.Equal(("dolphin", (string?)null), (a.Id, a.Value));
        var b = P("emulator", "install", "dolphin", "calibration");
        Assert.Equal(("dolphin", "calibration"), (b.Id, b.Value));
        Assert.Equal("calibration", P("emulator", "uninstall", "dolphin", "calibration").Value);
    }

    [Fact]
    public void Path_shows_sets_and_clears()
    {
        Assert.Null(P("emulator", "path", "pcsx2").Path);
        var set = P("emulator", "path", "pcsx2", @"D:\PCSX2 [Lightgun]");
        Assert.Equal(@"D:\PCSX2 [Lightgun]", set.Path);
        Assert.Null(set.Value);
        Assert.True(P("emulator", "path", "pcsx2", "--clear").Clear);
    }

    [Theory]
    [InlineData("emulator")]
    [InlineData("emulator", "frobnicate", "dolphin")]
    [InlineData("emulator", "install")]
    [InlineData("emulator", "list", "dolphin")]
    [InlineData("emulator", "install", "dolphin", "--clear")]
    [InlineData("emulator", "path", "pcsx2", "D:\\x", "--clear")]
    [InlineData("emulator", "update", "dolphin", "base")]
    [InlineData("emulator", "install", "dolphin", "base", "extra")]
    [InlineData("emulator", "install", "dolphin", "--game", "1")]
    [InlineData("emulator", "install", "dolphin", "--path", "D:\\x")]
    public void Bad_forms_are_errors(params string[] args)
        => Assert.NotNull(Cli.Parse(args).Error);

    [Fact]
    public void Clear_is_still_rejected_outside_path_commands()
        => Assert.NotNull(P("install", "reload", "--clear").Error);
}
