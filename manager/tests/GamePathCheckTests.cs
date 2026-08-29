using System.IO;
using Xunit;

namespace VrlfMods.Tests;

public class GamePathCheckTests
{
    static string TempDir(params string[] relFiles)
    {
        var dir = Path.Combine(Path.GetTempPath(), "vrlf-chk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var rel in relFiles)
        {
            var full = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, "x");
        }
        return dir;
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_input_is_rejected(string raw)
    {
        var r = GamePathCheck.Inspect(raw);
        Assert.False(r.Ok);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void Surrounding_quotes_are_stripped()
    {
        // Explorer's "Copy as path" wraps the path in double quotes.
        var dir = TempDir("game.exe");
        var r = GamePathCheck.Inspect($"\"{dir}\"");
        Assert.True(r.Ok);
        Assert.Equal(dir, r.Full);
    }

    [Fact]
    public void Surrounding_whitespace_is_trimmed()
    {
        var dir = TempDir("game.exe");
        Assert.Equal(dir, GamePathCheck.Inspect($"  {dir}  ").Full);
    }

    [Fact]
    public void A_relative_path_is_resolved_to_absolute()
    {
        var r = GamePathCheck.Inspect("some-relative-dir");
        Assert.True(Path.IsPathRooted(r.Full));
    }

    [Fact]
    public void A_missing_directory_is_rejected_and_named()
    {
        var missing = Path.Combine(Path.GetTempPath(), "vrlf-nope-" + Guid.NewGuid().ToString("N"));
        var r = GamePathCheck.Inspect(missing);
        Assert.False(r.Ok);
        Assert.Contains(missing, r.Error);
    }

    [Fact]
    public void A_file_is_rejected_as_not_a_folder()
    {
        var dir = TempDir("game.exe");
        var r = GamePathCheck.Inspect(Path.Combine(dir, "game.exe"));
        Assert.False(r.Ok);
        Assert.Contains("folder", r.Error);
    }

    [Fact]
    public void A_drive_root_is_refused()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var r = GamePathCheck.Inspect(root);
        Assert.False(r.Ok);
        Assert.Contains("drive root", r.Error);
    }

    [Fact]
    public void A_folder_with_a_top_level_exe_passes_clean()
    {
        var r = GamePathCheck.Inspect(TempDir("Reload.exe"));
        Assert.True(r.Ok);
        Assert.Null(r.Warning);
    }

    [Fact]
    public void A_folder_whose_exe_is_nested_passes_clean()
    {
        // The usual Unreal layout: <game>/Binaries/Win64/Game.exe
        var r = GamePathCheck.Inspect(TempDir("Game/Binaries/Win64/Game.exe"));
        Assert.True(r.Ok);
        Assert.Null(r.Warning);
    }

    [Fact]
    public void A_folder_with_no_exe_is_allowed_but_warns()
    {
        var dir = TempDir("readme.txt");
        var r = GamePathCheck.Inspect(dir);
        Assert.True(r.Ok);                      // warn, never block — odd layouts are legitimate
        Assert.NotNull(r.Warning);
        Assert.Contains(dir, r.Warning);
    }

    [Fact]
    public void An_exe_deeper_than_the_probe_still_warns()
    {
        // Documents the bound: the probe stops at 3 levels below the folder so a wrong
        // pick (say a whole library root) can't trigger a full-disk walk.
        var r = GamePathCheck.Inspect(TempDir("a/b/c/d/Game.exe"));
        Assert.True(r.Ok);
        Assert.NotNull(r.Warning);
    }
}
