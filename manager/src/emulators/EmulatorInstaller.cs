using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace VrlfMods;

public interface IProcessProbe { IEnumerable<string> RunningProcessNames(); }

public sealed class SystemProcessProbe : IProcessProbe
{
    public IEnumerable<string> RunningProcessNames()
    {
        foreach (var p in Process.GetProcesses())
        {
            string? name = null;
            try { name = p.ProcessName; } catch { /* exited while we looked */ }
            p.Dispose();
            if (name is not null) yield return name;
        }
    }
}

/// <summary>
/// Installs one option of one emulator: copies the package's files (backing up any it
/// overwrites), applies its setting changes while recording what each replaced, and writes a
/// receipt. Uninstall replays the receipt backwards.
/// </summary>
public sealed class EmulatorInstaller
{
    private readonly IHttpFetcher _http;
    private readonly AppPaths _paths;
    private readonly EmulatorReceiptStore _receipts;
    private readonly IProcessProbe _probe;

    public EmulatorInstaller(IHttpFetcher http, AppPaths paths, EmulatorReceiptStore receipts, IProcessProbe probe)
    { _http = http; _paths = paths; _receipts = receipts; _probe = probe; }

    public static string ZipUrl(EmulatorOption o) => $"{RegistryLoader.RawBase}/{o.Zip}";

    static string Key(string emu, string opt) => $"{emu}/{opt}";

    /// <summary>The first running process whose name starts with one of the emulator's prefixes.</summary>
    public string? RunningProcess(EmulatorEntry emu) =>
        _probe.RunningProcessNames().FirstOrDefault(n =>
            emu.Processes.Any(p => n.StartsWith(p, StringComparison.OrdinalIgnoreCase)));

    static string CloseFirst(EmulatorEntry emu, string running) =>
        $"Close {emu.Name} first ({running} is running). It saves its settings on exit and would overwrite these.";

    public async Task<(byte[]? bytes, string? error)> FetchAndVerify(EmulatorOption o)
    {
        var bytes = await _http.TryGet(ZipUrl(o));
        if (bytes is null) return (null, $"download failed for {o.Zip} (network required)");
        var got = Installer.Sha256Hex(bytes);
        if (!string.Equals(got, o.Sha256, StringComparison.OrdinalIgnoreCase))
            return (null, $"sha256 mismatch for {o.Zip}: registry says {o.Sha256}, got {got}");
        return (bytes, null);
    }

    /// <summary>Reads and checks settings.json before anything touches the disk.</summary>
    static (PackageSettings? settings, string? error) ReadSettings(ZipArchive zip)
    {
        var entry = zip.GetEntry("settings.json");
        if (entry is null) return (null, "package has no settings.json");
        PackageSettings? s;
        try
        {
            using var st = entry.Open();
            s = JsonSerializer.Deserialize(st, VrlfJson.Default.PackageSettings);
        }
        catch (Exception ex) { return (null, $"settings.json is unreadable: {ex.Message}"); }
        if (s?.Edits is null) return (null, "settings.json has no edits list");
        foreach (var e in s.Edits)
            if (e.Problem() is { } p) return (null, $"settings.json: {p}");
        return (s, null);
    }

    /// <summary>An entry whose relative name would land outside the settings folder or otherwise
    /// isn't a plain relative path.</summary>
    static string? UnsafeRelPath(string rel)
    {
        var parts = rel.Split('/');
        if (Path.IsPathRooted(rel) || rel.Contains(':') || parts.Contains(".."))
            return $"package file {rel} is not a plain relative path";
        return null;
    }

    /// <summary>A package opened and checked against a settings folder before anything touches
    /// the disk: the zip opens, settings.json parses and every edit passes <see
    /// cref="EditSpec.Problem"/>, and every <c>files/</c> entry resolves to a safe, unique
    /// destination. Used by both <see cref="Install"/> and <see cref="Refresh"/>.</summary>
    sealed class PreparedPackage : IDisposable
    {
        public required ZipArchive Zip { get; init; }
        public required PackageSettings Settings { get; init; }
        public required List<(ZipArchiveEntry Entry, string Rel, string Dest)> Files { get; init; }
        public void Dispose() => Zip.Dispose();
    }

    static (PreparedPackage? package, string? error) Prepare(byte[] bytes, string folder)
    {
        ZipArchive zip;
        try { zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read); }
        catch (Exception ex) { return (null, $"package is not a zip: {ex.Message}"); }

        var (settings, problem) = ReadSettings(zip);
        if (settings is null) { zip.Dispose(); return (null, problem); }

        var files = new List<(ZipArchiveEntry Entry, string Rel, string Dest)>();
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            var name = Zip.NormalizeEntryName(entry.FullName);
            if (!name.StartsWith("files/", StringComparison.Ordinal) || name.EndsWith('/')) continue;
            var rel = name["files/".Length..];
            if (rel.Length == 0) continue;

            if (UnsafeRelPath(rel) is { } unsafeMsg) { zip.Dispose(); return (null, unsafeMsg); }

            string dest;
            try { dest = Zip.ResolveDest(folder, rel); }
            catch (InvalidDataException ex) { zip.Dispose(); return (null, ex.Message); }

            if (seen.TryGetValue(dest, out var other))
            {
                zip.Dispose();
                return (null, $"package has two files for the same path: {other} and {rel}");
            }
            seen[dest] = rel;

            files.Add((entry, rel, dest));
        }

        return (new PreparedPackage { Zip = zip, Settings = settings, Files = files }, null);
    }

    public async Task<OpResult> Install(EmulatorEntry emu, EmulatorOption opt, string folder, byte[]? prefetched = null)
    {
        var key = Key(emu.Id, opt.Id);
        if (RunningProcess(emu) is { } running) return new OpResult(false, key, CloseFirst(emu, running));
        // A receipt that no longer parses still means installed: going ahead would record our
        // own settings as the user's originals.
        if (_receipts.Exists(emu.Id, opt.Id))
            return new OpResult(false, key, _receipts.Load(emu.Id, opt.Id) is null
                ? $"{opt.Label} has a damaged receipt at {_paths.EmuReceiptPath(emu.Id, opt.Id)}. Repair or delete it before installing."
                : $"{opt.Label} is already installed");
        // Backups with no receipt are originals a failed install could not put back; a new
        // install would back up over them.
        var backupDir = _paths.EmuBackupDir(emu.Id, opt.Id);
        if (HasEntries(backupDir))
            return new OpResult(false, key,
                $"A previous install left backups in {backupDir}. Put those files back or delete that folder, then install again.");
        if (!Directory.Exists(folder)) return new OpResult(false, key, $"settings folder not found: {folder}");

        byte[] bytes;
        if (prefetched is not null) bytes = prefetched;
        else
        {
            var (fetched, err) = await FetchAndVerify(opt);
            if (fetched is null) return new OpResult(false, key, err!);
            bytes = fetched;
        }

        var (package, error) = Prepare(bytes, folder);
        if (package is null) return new OpResult(false, key, error!);
        try { return InstallFrom(package, emu, opt, folder, key); }
        finally { package.Dispose(); }
    }

    OpResult InstallFrom(PreparedPackage package, EmulatorEntry emu, EmulatorOption opt, string folder, string key)
    {
        var written = new List<InstalledFile>();
        var backups = new List<BackupRef>();
        var applied = new List<AppliedEdit>();
        var backupDir = _paths.EmuBackupDir(emu.Id, opt.Id);
        try
        {
            foreach (var (entry, rel, dest) in package.Files)
            {
                if (File.Exists(dest))
                {
                    var bk = Path.Combine(backupDir, rel.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(bk)!);
                    File.Copy(dest, bk, overwrite: true);
                    backups.Add(new BackupRef(rel));
                }
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, overwrite: true);
                written.Add(new InstalledFile(rel, Installer.Sha256HexFile(dest)));
            }

            foreach (var edit in package.Settings.Edits)
            {
                var path = Zip.ResolveDest(folder, edit.File);
                bool created = !File.Exists(path);
                var text = SettingsText.Load(path);
                var prior = SettingsEdits.Apply(text, edit);
                text.Save(path);
                applied.Add(new AppliedEdit(edit, prior, created));
            }

            _receipts.Save(new EmulatorReceipt(emu.Id, opt.Id, opt.Version, folder,
                written, backups, applied, DateTime.UtcNow.ToString("O")));
        }
        catch (Exception ex)
        {
            return new OpResult(false, key, RollBack(folder, applied, written, backups, backupDir, ex.Message));
        }
        return new OpResult(true, key, $"installed {emu.Name} {opt.Label} v{opt.Version}");
    }

    /// <summary>Undoes a failed install and says how that went. The backups go only when every
    /// original was put back; otherwise they are the user's only copies, and the message says
    /// where. Internal so a test can reach the restore-failure branch (see <see
    /// cref="RemoveFiles"/>).</summary>
    internal static string RollBack(string folder, List<AppliedEdit> applied, List<InstalledFile> written,
                                    List<BackupRef> backups, string backupDir, string error)
    {
        RevertEdits(folder, applied, warnings: null, bestEffort: true);
        var notRestored = RemoveFiles(folder, written, backups, backupDir, warnings: null, bestEffort: true);
        if (notRestored.Count > 0)
            return $"install failed ({error}); could not put back {string.Join(", ", notRestored)}. The originals are kept in {backupDir}.";
        DeleteBackups(backupDir);
        return $"install failed, nothing changed: {error}";
    }

    public OpResult Uninstall(EmulatorEntry emu, EmulatorReceipt r)
    {
        var key = Key(r.EmulatorId, r.OptionId);
        if (RunningProcess(emu) is { } running) return new OpResult(false, key, CloseFirst(emu, running));
        var label = emu.Options.FirstOrDefault(o => o.Id == r.OptionId)?.Label ?? r.OptionId;
        var backupDir = _paths.EmuBackupDir(r.EmulatorId, r.OptionId);

        // A folder on a drive that isn't connected right now is not the same as a deleted folder
        // (portable emulators on USB sticks are common) - refuse instead of treating it as gone.
        var driveRoot = Path.GetPathRoot(r.Folder);
        if (!string.IsNullOrEmpty(driveRoot) && !Directory.Exists(driveRoot))
            return new OpResult(false, key,
                $"the drive for {r.Folder} is not connected. Reconnect it and run uninstall again.");

        // A folder deleted or moved since: restoring backups would recreate it, so put nothing
        // back. The backups stay: a moved folder's user still needs those originals.
        if (!Directory.Exists(r.Folder))
        {
            try { _receipts.Delete(r.EmulatorId, r.OptionId); }
            catch (Exception ex)
            {
                return new OpResult(false, key, $"uninstall stopped part-way ({ex.Message}); fix that and run it again");
            }
            var gone = $"the settings folder {r.Folder} no longer exists, so there was nothing to put back";
            if (HasEntries(backupDir)) gone += $"; the files it had replaced are kept in {backupDir}";
            return new OpResult(true, key, $"uninstalled {emu.Name} {label}; warning: {gone}");
        }

        var warnings = new List<string>();
        List<string> notRestored;
        try
        {
            RevertEdits(r.Folder, r.Edits, warnings, bestEffort: false);
            notRestored = RemoveFiles(r.Folder, r.Files, r.Backups, backupDir, warnings, bestEffort: false);
            _receipts.Delete(r.EmulatorId, r.OptionId);
        }
        catch (Exception ex)
        {
            return new OpResult(false, key, $"uninstall stopped part-way ({ex.Message}); fix that and run it again");
        }
        // Only now: while the receipt exists a re-run may still need these originals.
        if (notRestored.Count == 0) DeleteBackups(backupDir);

        var msg = $"uninstalled {emu.Name} {label}";
        var distinct = warnings.Distinct().ToList();
        if (distinct.Count > 0) msg += "; warning: " + string.Join("; ", distinct);
        return new OpResult(true, key, msg);
    }

    /// <summary>
    /// Update (<paramref name="onlyIfNewer"/>) or re-apply one option: the new package is verified
    /// AND fully opened and checked (so an unreadable package never costs the user their working
    /// install) before the option is uninstalled, which puts the user's originals back, and
    /// installed again, which records those same originals.
    /// </summary>
    public async Task<OpResult> Refresh(EmulatorEntry emu, EmulatorOption opt, EmulatorReceipt current, bool onlyIfNewer)
    {
        var key = Key(emu.Id, opt.Id);
        if (onlyIfNewer && string.Equals(opt.Version, current.Version, StringComparison.OrdinalIgnoreCase))
            return new OpResult(true, key, $"{opt.Label} is up to date (v{opt.Version})");
        if (RunningProcess(emu) is { } running) return new OpResult(false, key, CloseFirst(emu, running));
        // Uninstall would find nothing to put back and drop the receipt and backups, and the
        // install after it would fail: keep what is installed instead.
        if (!Directory.Exists(current.Folder))
            return new OpResult(false, key, $"kept v{current.Version}: the settings folder {current.Folder} is no longer there");

        var (bytes, err) = await FetchAndVerify(opt);
        if (bytes is null) return new OpResult(false, key, $"kept v{current.Version}: {err}");

        var (package, perr) = Prepare(bytes, current.Folder);
        if (package is null) return new OpResult(false, key, $"kept v{current.Version}: {perr}");
        package.Dispose();

        // Anything of ours the user changed since install would otherwise be lost when Uninstall
        // reverts it to our own recorded prior - keep it aside first (mirrors Installer.Update).
        // A copy that fails stops the update: nothing is removed.
        var kept = new List<string>();
        foreach (var f in current.Files)
        {
            var p = SafeResolve(current.Folder, f.RelPath);
            if (p is null) continue;
            try
            {
                if (!File.Exists(p)) continue;
                if (string.Equals(Installer.Sha256HexFile(p), f.Sha256, StringComparison.OrdinalIgnoreCase)) continue;
                File.Copy(p, p + $".bak-{current.Version}", overwrite: true);
                kept.Add(f.RelPath + $".bak-{current.Version}");
            }
            catch (Exception ex)
            {
                return new OpResult(false, key, $"kept v{current.Version}: could not keep your changed {f.RelPath} ({ex.Message})");
            }
        }

        var un = Uninstall(emu, current);
        if (!un.Ok) return un;
        var inst = await Install(emu, opt, current.Folder, bytes);
        if (!inst.Ok)
            return new OpResult(false, key,
                $"removed v{current.Version}, but installing v{opt.Version} failed ({inst.Message}). Run install to try again.");

        var msg = onlyIfNewer
            ? $"updated {emu.Name} {opt.Label} {current.Version} → {opt.Version}"
            : $"re-applied {emu.Name} {opt.Label} v{opt.Version}";
        if (kept.Count > 0) msg += "; kept your changed file(s) as: " + string.Join(", ", kept);
        if (UninstallWarning(un.Message) is { } w) msg += "; warning: " + w;
        return new OpResult(true, key, msg);
    }

    /// <summary>The text after "; warning: " in an <see cref="Uninstall"/> message, or null.</summary>
    static string? UninstallWarning(string uninstallMessage)
    {
        const string marker = "; warning: ";
        var i = uninstallMessage.IndexOf(marker, StringComparison.Ordinal);
        return i < 0 ? null : uninstallMessage[(i + marker.Length)..];
    }

    /// <summary>Undoes edits newest first. A missing settings file is skipped with a warning; a file
    /// we created is deleted once nothing but blanks and comments is left.</summary>
    static void RevertEdits(string folder, List<AppliedEdit> applied, List<string>? warnings, bool bestEffort)
    {
        for (int i = applied.Count - 1; i >= 0; i--)
        {
            var a = applied[i];
            Try(bestEffort, () =>
            {
                var path = Zip.ResolveDest(folder, a.Edit.File);
                if (!File.Exists(path))
                {
                    warnings?.Add($"skipped {a.Edit.File}: it no longer exists");
                    return;
                }
                var text = SettingsText.Load(path);
                SettingsEdits.Revert(text, a.Edit, a.Prior);
                if (a.FileCreated && text.IsBlank)
                {
                    File.Delete(path);
                    PruneEmptyParents(folder, path);
                }
                else text.Save(path);
            });
        }
    }

    /// <summary>A receipt's relative path resolved against a root, or null when a corrupt receipt
    /// would send it outside that root.</summary>
    static string? SafeResolve(string root, string rel)
    {
        try { return Zip.ResolveDest(root, rel); }
        catch (InvalidDataException) { return null; }
    }

    /// <summary>Deletes the files we installed and puts back the ones they replaced. Returns the
    /// relative paths it could not put back; empty means every restore it attempted went
    /// through. It never removes the backup folder, the only copy of a displaced original: the
    /// caller does that once nothing can still need it (for an uninstall, once the receipt is
    /// gone, so a re-run always still has its backups).
    /// Internal (rather than private) so a test can drive the restore-failure branch directly:
    /// reaching it through a full <see cref="Install"/> rollback would need a lock injected
    /// between a successful extraction and the later failure that triggers rollback, which a
    /// synchronous test has no way to do.</summary>
    internal static List<string> RemoveFiles(string folder, List<InstalledFile> files, List<BackupRef> backups,
                            string backupDir, List<string>? warnings, bool bestEffort)
    {
        foreach (var f in files)
        {
            var p = SafeResolve(folder, f.RelPath);
            if (p is null) continue;
            Try(bestEffort, () =>
            {
                if (!File.Exists(p)) return;
                var sha = Installer.Sha256HexFile(p);
                // Already the user's original: an earlier run put it back, then stopped before
                // the receipt went. Deleting it now would lose it.
                if (IsBackedUpOriginal(backupDir, backups, f.RelPath, sha)) return;
                if (warnings is not null && !string.Equals(sha, f.Sha256, StringComparison.OrdinalIgnoreCase))
                    warnings.Add($"{f.RelPath} had been changed since install and was removed");
                File.Delete(p);
            });
        }

        var notRestored = new List<string>();
        foreach (var b in backups)
        {
            var src = SafeResolve(backupDir, b.RelPath);
            var dst = SafeResolve(folder, b.RelPath);
            if (src is null || dst is null) { notRestored.Add(b.RelPath); continue; }
            if (!File.Exists(src)) continue;
            if (!TryRestore(bestEffort, () =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(src, dst, overwrite: true);
            })) notRestored.Add(b.RelPath);
        }

        foreach (var f in files)
            if (SafeResolve(folder, f.RelPath) is { } p) PruneEmptyParents(folder, p);
        return notRestored;
    }

    /// <summary>True when the file at an installed path is byte-for-byte the backup of what it
    /// replaced, i.e. it is the user's original again.</summary>
    static bool IsBackedUpOriginal(string backupDir, List<BackupRef> backups, string rel, string sha)
    {
        if (!backups.Any(b => string.Equals(b.RelPath, rel, StringComparison.OrdinalIgnoreCase))) return false;
        var bk = SafeResolve(backupDir, rel);
        return bk is not null && File.Exists(bk)
            && string.Equals(Installer.Sha256HexFile(bk), sha, StringComparison.OrdinalIgnoreCase);
    }

    static bool HasEntries(string dir) => Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any();

    static void DeleteBackups(string backupDir) =>
        Try(true, () => { if (Directory.Exists(backupDir)) Directory.Delete(backupDir, recursive: true); });

    /// <summary>Like <see cref="Try"/> but reports whether the action actually succeeded, so a
    /// caller can gate a later step on every attempt having gone through.</summary>
    static bool TryRestore(bool swallow, Action a)
    {
        if (!swallow) { a(); return true; }
        try { a(); return true; } catch { return false; }
    }

    /// <summary>Removes empty folders from the file's parent up to, never including, the settings folder.</summary>
    static void PruneEmptyParents(string folder, string path)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        while (dir is not null && dir.Length > root.Length
               && dir.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (!Directory.Exists(dir) || Directory.EnumerateFileSystemEntries(dir).Any()) break;
                Directory.Delete(dir);
            }
            catch { break; }
            dir = Path.GetDirectoryName(dir);
        }
    }

    static void Try(bool swallow, Action a)
    {
        if (!swallow) { a(); return; }
        try { a(); } catch { /* best effort: keep undoing the rest */ }
    }
}
