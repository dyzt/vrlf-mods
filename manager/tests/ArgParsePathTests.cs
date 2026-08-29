using Xunit;

namespace VrlfMods.Tests;

/// <summary>The `path` verb: show, set, clear.</summary>
public class ArgParsePathTests
{
    [Fact]
    public void Path_with_only_an_id_shows_the_current_folder()
    {
        var p = Cli.Parse(new[] { "path", "reload" });
        Assert.Equal("path", p.Command);
        Assert.Equal("reload", p.Id);
        Assert.Null(p.Path);
        Assert.False(p.Clear);
        Assert.Null(p.Error);
    }

    [Fact]
    public void Path_with_a_directory_sets_it()
    {
        var p = Cli.Parse(new[] { "path", "reload", @"D:\Games\Reload" });
        Assert.Equal(@"D:\Games\Reload", p.Path);
        Assert.False(p.Clear);
        Assert.Null(p.Error);
    }

    [Fact]
    public void Path_takes_the_directory_as_a_flag_too()
    {
        var p = Cli.Parse(new[] { "path", "reload", "--path", @"D:\Games\Reload" });
        Assert.Equal(@"D:\Games\Reload", p.Path);
        Assert.Null(p.Error);
    }

    [Fact]
    public void Path_with_clear_clears_it()
    {
        var p = Cli.Parse(new[] { "path", "reload", "--clear" });
        Assert.True(p.Clear);
        Assert.Null(p.Path);
        Assert.Null(p.Error);
    }

    [Fact]
    public void Path_accepts_an_explicit_game()
    {
        var p = Cli.Parse(new[] { "path", "heavy-fire-afghanistan", "--game", "305980", @"D:\HF" });
        Assert.Equal(305980, p.Appid);
        Assert.Equal(@"D:\HF", p.Path);
    }

    [Fact]
    public void Path_without_an_id_is_an_error()
        => Assert.NotNull(Cli.Parse(new[] { "path" }).Error);

    [Fact]
    public void Path_cannot_both_set_and_clear()
        => Assert.NotNull(Cli.Parse(new[] { "path", "reload", @"D:\x", "--clear" }).Error);

    [Fact]
    public void Path_rejects_a_second_directory()
        => Assert.NotNull(Cli.Parse(new[] { "path", "reload", @"D:\x", @"D:\y" }).Error);

    [Fact]
    public void Clear_is_only_meaningful_for_the_path_command()
        => Assert.NotNull(Cli.Parse(new[] { "install", "reload", "--clear" }).Error);
}
