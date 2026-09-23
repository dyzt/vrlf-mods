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
    public string GamePathsPath => Path.Combine(ModsRoot, "game-paths.json");
    public string ReceiptsDir => Path.Combine(ModsRoot, "receipts");
    public string BackupsDir => Path.Combine(ModsRoot, "backups");

    public string ReceiptPath(string modId, string gameKey)
        => Path.Combine(ReceiptsDir, $"{modId}-{gameKey}.json");

    public string BackupDir(string modId, string gameKey)
        => Path.Combine(BackupsDir, modId, gameKey);

    public string EmulatorsRoot => Path.Combine(ModsRoot, "emulators");
    public string EmuReceiptsDir => Path.Combine(EmulatorsRoot, "receipts");

    public string EmuReceiptPath(string emu, string opt)
        => Path.Combine(EmuReceiptsDir, $"{emu}-{opt}.json");

    public string EmuBackupDir(string emu, string opt)
        => Path.Combine(EmulatorsRoot, "backups", emu, opt);

    public static string GameKeyForAppid(long appid) => appid.ToString();
}
