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

        // A prior receipt means this mod is already installed here. Don't re-back-up files we
        // ourselves installed (that would capture our modded file as the "original"), and don't
        // let this call's rollback touch files/backups owned by that earlier install.
        var prior = _receipts.Load(mod.Id, target.Key);
        var priorInstalled = new HashSet<string>(
            prior?.Files.Select(f => f.RelPath) ?? Enumerable.Empty<string>());

        var written = new List<InstalledFile>();
        var newBackups = new List<BackupRef>();   // backups made THIS call — the rollback scope
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
                    && !priorInstalled.Contains(rel))
                {
                    var backupPath = System.IO.Path.Combine(backupDir, rel.Replace('/', System.IO.Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(backupPath)!);
                    File.Copy(dest, backupPath, overwrite: true);
                    newBackups.Add(new BackupRef(rel));
                }

                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, overwrite: true);
                written.Add(new InstalledFile(rel, Sha256HexFile(dest)));
            }

            // Persist the union of earlier originals and this call's new backups, so uninstall
            // can restore every displaced original.
            var allBackups = new List<BackupRef>(prior?.Backups ?? Enumerable.Empty<BackupRef>());
            foreach (var b in newBackups)
                if (!allBackups.Any(x => x.RelPath == b.RelPath))
                    allBackups.Add(b);

            // Receipt written LAST, but INSIDE the try so a Save failure rolls back this call.
            _receipts.Save(new Receipt(mod.Id, mod.Version, target.Key, target.Path,
                written, allBackups, DateTime.UtcNow.ToString("O")));
        }
        catch (Exception ex)
        {
            // Roll back only what THIS call created: delete newly-written files (not ones a prior
            // install already owned), and restore only this call's backups.
            var newWrites = written.Where(w => !priorInstalled.Contains(w.RelPath)).ToList();
            RollBack(target.Path, newWrites, newBackups, mod.Id, target.Key);
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

    public OpResult Uninstall(Receipt r)
    {
        var warnings = new List<string>();
        foreach (var f in r.Files)
        {
            var p = System.IO.Path.Combine(r.GamePath, f.RelPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (!File.Exists(p)) continue;
            if (!string.Equals(Sha256HexFile(p), f.Sha256, StringComparison.OrdinalIgnoreCase))
                warnings.Add(f.RelPath);
            try { File.Delete(p); } catch (Exception ex) { return new OpResult(false, r.GameKey, $"could not delete {f.RelPath}: {ex.Message}"); }
        }

        var backupDir = _paths.BackupDir(r.ModId, r.GameKey);
        foreach (var b in r.Backups)
        {
            var src = System.IO.Path.Combine(backupDir, b.RelPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            var dst = System.IO.Path.Combine(r.GamePath, b.RelPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (!File.Exists(src)) continue;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dst)!);
                File.Copy(src, dst, overwrite: true);
            }
            catch (Exception ex)
            {
                return new OpResult(false, r.GameKey, $"could not restore {b.RelPath}: {ex.Message}");
            }
        }
        if (Directory.Exists(backupDir)) { try { Directory.Delete(backupDir, recursive: true); } catch { } }

        PruneEmptyDirs(r.GamePath, r.Files);
        _receipts.Delete(r.ModId, r.GameKey);

        var msg = $"uninstalled {r.ModId} ({r.Files.Count} files)";
        if (warnings.Count > 0)
            msg += $"; warning: {warnings.Count} file(s) had been modified since install and were removed: {string.Join(", ", warnings)}";
        return new OpResult(true, r.GameKey, msg);
    }

    public async Task<OpResult> Update(ModEntry mod, Receipt current)
    {
        if (string.Equals(mod.Version, current.Version, StringComparison.OrdinalIgnoreCase))
            return new OpResult(true, current.GameKey, $"{mod.Id} is up to date (v{mod.Version})");

        var preserved = new List<string>();
        foreach (var f in current.Files)
        {
            var p = System.IO.Path.Combine(current.GamePath, f.RelPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (File.Exists(p) && !string.Equals(Sha256HexFile(p), f.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                var aside = p + $".bak-{current.Version}";
                try { File.Copy(p, aside, overwrite: true); preserved.Add(f.RelPath + $".bak-{current.Version}"); }
                catch { /* best effort */ }
            }
        }

        var un = Uninstall(current);
        if (!un.Ok) return un;
        var inst = await Install(mod, new GameTarget(current.GameKey, current.GamePath));
        if (!inst.Ok) return inst;

        var msg = $"updated {mod.Id} {current.Version} → {mod.Version}";
        if (preserved.Count > 0)
            msg += $"; kept your modified file(s) as: {string.Join(", ", preserved)}";
        return new OpResult(true, current.GameKey, msg);
    }

    private static void PruneEmptyDirs(string gamePath, List<InstalledFile> files)
    {
        // Deepest-first, so parents empty out after their children are removed.
        var dirs = files
            .Select(f => System.IO.Path.GetDirectoryName(
                System.IO.Path.Combine(gamePath, f.RelPath.Replace('/', System.IO.Path.DirectorySeparatorChar)))
                ?? gamePath)
            .Where(d => d.Length > gamePath.Length)
            .Distinct()
            .OrderByDescending(d => d.Length);
        foreach (var d in dirs)
        {
            var cur = d;
            while (cur.Length > gamePath.Length && Directory.Exists(cur)
                   && !Directory.EnumerateFileSystemEntries(cur).Any())
            {
                try { Directory.Delete(cur); } catch { break; }
                cur = System.IO.Path.GetDirectoryName(cur) ?? gamePath;
            }
        }
    }
}
