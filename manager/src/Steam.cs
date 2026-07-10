using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace VrlfMods;

public interface ISteamPaths { string? SteamRoot(); }

public sealed class RegistrySteamPaths : ISteamPaths
{
    public string? SteamRoot()
    {
        var hkcu = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (!string.IsNullOrEmpty(hkcu)) return hkcu.Replace('/', '\\');
        var hklm = Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
        return string.IsNullOrEmpty(hklm) ? null : hklm;
    }
}

public static class SteamVdf
{
    // Matches "path"  "value" pairs (value may contain escaped backslashes).
    private static readonly Regex PathRx =
        new(@"""path""\s*""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex InstallDirRx =
        new(@"""installdir""\s*""([^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyList<string> ParseLibraryFolders(string vdfText) =>
        PathRx.Matches(vdfText).Select(m => Unescape(m.Groups[1].Value)).ToList();

    public static string? ParseInstallDir(string acfText)
    {
        var m = InstallDirRx.Match(acfText);
        return m.Success ? Unescape(m.Groups[1].Value) : null;
    }

    private static string Unescape(string s) => s.Replace("\\\\", "\\");
}

public sealed class SteamLocator
{
    private readonly ISteamPaths _steam;
    public SteamLocator(ISteamPaths steam) => _steam = steam;

    public string? FindGameDir(long appid)
    {
        var root = _steam.SteamRoot();
        if (string.IsNullOrEmpty(root)) return null;

        var libVdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        var libs = new List<string> { root };
        if (File.Exists(libVdf))
            libs.AddRange(SteamVdf.ParseLibraryFolders(File.ReadAllText(libVdf)));

        foreach (var lib in libs.Distinct())
        {
            var acf = Path.Combine(lib, "steamapps", $"appmanifest_{appid}.acf");
            if (!File.Exists(acf)) continue;
            var installDir = SteamVdf.ParseInstallDir(File.ReadAllText(acf));
            if (string.IsNullOrEmpty(installDir)) continue;
            var gameDir = Path.Combine(lib, "steamapps", "common", installDir);
            if (Directory.Exists(gameDir)) return gameDir;
        }
        return null;
    }
}
