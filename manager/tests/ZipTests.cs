using System.IO;
using Xunit;

namespace VrlfMods.Tests;

public class ZipTests
{
    [Fact]
    public void Normalizes_backslashes_and_leading_slash()
    {
        Assert.Equal("BepInEx/core/x.dll", Zip.NormalizeEntryName(@"BepInEx\core\x.dll"));
        Assert.Equal("a/b", Zip.NormalizeEntryName("/a/b"));
    }

    [Fact]
    public void ResolveDest_returns_full_path_inside_target()
    {
        var target = Path.Combine(Path.GetTempPath(), "game");
        var dest = Zip.ResolveDest(target, @"BepInEx\config\x.cfg");
        Assert.Equal(Path.GetFullPath(Path.Combine(target, "BepInEx", "config", "x.cfg")), dest);
    }

    [Fact]
    public void ResolveDest_rejects_zip_slip()
    {
        var target = Path.Combine(Path.GetTempPath(), "game");
        Assert.Throws<InvalidDataException>(() => Zip.ResolveDest(target, @"..\..\Windows\evil.dll"));
    }

    [Fact]
    public void ResolveDest_rejects_drive_rooted_entry()
    {
        var target = Path.Combine(Path.GetTempPath(), "game");
        Assert.Throws<InvalidDataException>(() => Zip.ResolveDest(target, @"C:\Windows\evil.dll"));
    }

    [Fact]
    public void ResolveDest_rejects_sibling_directory_collision()
    {
        var target = Path.Combine(Path.GetTempPath(), "game");
        Assert.Throws<InvalidDataException>(() => Zip.ResolveDest(target, @"..\gameEVIL\evil.dll"));
    }
}
