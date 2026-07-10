using System;
using System.IO;
using Xunit;

namespace VrlfMods.Tests;

public class SteamTests
{
    const string LibVdf = """
    "libraryfolders"
    {
        "0" { "path" "C:\\Program Files (x86)\\Steam" }
        "1" { "path" "D:\\SteamLibrary" }
    }
    """;

    const string Acf = """
    "AppState"
    {
        "appid" "305980"
        "name"  "Heavy Fire Afghanistan"
        "installdir" "Heavy Fire Afghanistan"
    }
    """;

    [Fact]
    public void ParseLibraryFolders_returns_all_paths()
    {
        var libs = SteamVdf.ParseLibraryFolders(LibVdf);
        Assert.Equal(2, libs.Count);
        Assert.Contains(@"C:\Program Files (x86)\Steam", libs);
        Assert.Contains(@"D:\SteamLibrary", libs);
    }

    [Fact]
    public void ParseInstallDir_reads_the_value()
        => Assert.Equal("Heavy Fire Afghanistan", SteamVdf.ParseInstallDir(Acf));

    [Fact]
    public void FindGameDir_combines_lib_and_installdir()
    {
        // Build a fake Steam tree on disk.
        var root = Path.Combine(Path.GetTempPath(), "steam-" + Guid.NewGuid().ToString("N"));
        var steamApps = Path.Combine(root, "steamapps");
        Directory.CreateDirectory(steamApps);
        File.WriteAllText(Path.Combine(steamApps, "libraryfolders.vdf"),
            $"\"libraryfolders\"{{ \"0\" {{ \"path\" \"{root.Replace("\\","\\\\")}\" }} }}");
        File.WriteAllText(Path.Combine(steamApps, "appmanifest_305980.acf"), Acf);
        var gameDir = Path.Combine(steamApps, "common", "Heavy Fire Afghanistan");
        Directory.CreateDirectory(gameDir);

        var locator = new SteamLocator(new FakeSteamPaths(root));
        Assert.Equal(gameDir, new SteamLocator(new FakeSteamPaths(root)).FindGameDir(305980));
        Assert.Null(locator.FindGameDir(999999));
    }
}
