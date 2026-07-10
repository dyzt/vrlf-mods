using System.IO.Compression;
using System.Security.Cryptography;

namespace VrlfMods;

public record GameTarget(string Key, string Path);
public record OpResult(bool Ok, string GameKey, string Message);

public sealed class Installer
{
    private readonly IHttpFetcher _http;
    private readonly AppPaths _paths;
    private readonly ReceiptStore _receipts;

    public Installer(IHttpFetcher http, AppPaths paths, ReceiptStore receipts)
    { _http = http; _paths = paths; _receipts = receipts; }

    public static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static string Sha256HexFile(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    public async Task<OpResult> Install(ModEntry mod, GameTarget target)
    {
        var bytes = await _http.TryGet(RegistryLoader.ZipUrl(mod));
        if (bytes is null)
            return new OpResult(false, target.Key, $"download failed for {mod.Zip} (network required)");

        if (!string.Equals(Sha256Hex(bytes), mod.Sha256, StringComparison.OrdinalIgnoreCase))
            return new OpResult(false, target.Key,
                $"sha256 mismatch for {mod.Id} — registry says {mod.Sha256}, got {Sha256Hex(bytes)}");

        // Carry forward any prior install's original backups, and never re-back-up a file
        // this same mod already installed (doing so would overwrite the saved original).
        var prior = _receipts.Load(mod.Id, target.Key);
        var priorInstalled = new HashSet<string>(
            prior?.Files.Select(f => f.RelPath) ?? Enumerable.Empty<string>());

        var written = new List<InstalledFile>();
        var backups = new List<BackupRef>(prior?.Backups ?? Enumerable.Empty<BackupRef>());
        var backupDir = _paths.BackupDir(mod.Id, target.Key);

        try
        {
            using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\') || entry.Length == 0
                    && entry.Name.Length == 0)
                    continue;
                var rel = Zip.NormalizeEntryName(entry.FullName);
                if (rel.Length == 0) continue;
                var dest = Zip.ResolveDest(target.Path, entry.FullName);

                if (File.Exists(dest) && !written.Any(w => w.RelPath == rel)
                    && !priorInstalled.Contains(rel) && !backups.Any(b => b.RelPath == rel))
                {
                    var backupPath = System.IO.Path.Combine(backupDir, rel.Replace('/', System.IO.Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(backupPath)!);
                    File.Copy(dest, backupPath, overwrite: true);
                    backups.Add(new BackupRef(rel));
                }

                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, overwrite: true);
                written.Add(new InstalledFile(rel, Sha256HexFile(dest)));
            }

            // Receipt written LAST, but INSIDE the try so a Save failure rolls back
            // instead of stranding the extracted files.
            _receipts.Save(new Receipt(mod.Id, mod.Version, target.Key, target.Path,
                written, backups, DateTime.UtcNow.ToString("O")));
        }
        catch (Exception ex)
        {
            RollBack(target.Path, written, backups, mod.Id, target.Key);
            return new OpResult(false, target.Key, $"install failed: {ex.Message}");
        }

        return new OpResult(true, target.Key, $"installed {mod.Id} v{mod.Version} ({written.Count} files)");
    }

    private void RollBack(string gamePath, List<InstalledFile> written, List<BackupRef> backups,
                          string modId, string gameKey)
    {
        foreach (var f in written)
        {
            var p = System.IO.Path.Combine(gamePath, f.RelPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (File.Exists(p)) { try { File.Delete(p); } catch { } }
        }
        var backupDir = _paths.BackupDir(modId, gameKey);
        foreach (var b in backups)
        {
            var src = System.IO.Path.Combine(backupDir, b.RelPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            var dst = System.IO.Path.Combine(gamePath, b.RelPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (File.Exists(src)) { try { File.Copy(src, dst, overwrite: true); } catch { } }
        }
    }
}
