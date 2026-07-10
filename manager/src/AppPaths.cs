using System.Security.Cryptography;
using System.Text;

namespace VrlfMods;

public sealed class AppPaths
{
    public string ModsRoot { get; }
    public AppPaths(string modsRoot) => ModsRoot = modsRoot;

    public static AppPaths Default()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return new AppPaths(Path.Combine(appData, "VRLF", "mods"));
    }

    public string RegistryCachePath => Path.Combine(ModsRoot, "registry-cache.json");
    public string ReceiptsDir => Path.Combine(ModsRoot, "receipts");
    public string BackupsDir => Path.Combine(ModsRoot, "backups");

    public string ReceiptPath(string modId, string gameKey)
        => Path.Combine(ReceiptsDir, $"{modId}-{gameKey}.json");

    public string BackupDir(string modId, string gameKey)
        => Path.Combine(BackupsDir, modId, gameKey);

    public static string GameKeyForAppid(long appid) => appid.ToString();

    public static string GameKeyForPath(string path)
    {
        var norm = Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(norm));
        return "custom-" + Convert.ToHexString(hash).ToLowerInvariant()[..8];
    }
}
