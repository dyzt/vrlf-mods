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

    public async Task<OpResult> Install(EmulatorEntry emu, EmulatorOption opt, string folder, byte[]? prefetched = null)
    {
        var key = Key(emu.Id, opt.Id);
        if (RunningProcess(emu) is { } running) return new OpResult(false, key, CloseFirst(emu, running));
        if (_receipts.Load(emu.Id, opt.Id) is not null)
            return new OpResult(false, key, $"{opt.Label} is already installed");
        if (!Directory.Exists(folder)) return new OpResult(false, key, $"settings folder not found: {folder}");

        byte[] bytes;
        if (prefetched is not null) bytes = prefetched;
        else
        {
            var (fetched, err) = await FetchAndVerify(opt);
            if (fetched is null) return new OpResult(false, key, err!);
            bytes = fetched;
        }

        ZipArchive zip;
        try { zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read); }
        catch (Exception ex) { return new OpResult(false, key, $"package is not a zip: {ex.Message}"); }
        try { return InstallFrom(zip, emu, opt, folder, key); }
        finally { zip.Dispose(); }
    }

    OpResult InstallFrom(ZipArchive zip, EmulatorEntry emu, EmulatorOption opt, string folder, string key)
    {
        var (settings, problem) = ReadSettings(zip);
        if (settings is null) return new OpResult(false, key, problem!);

        var written = new List<InstalledFile>();
        var backups = new List<BackupRef>();
        var applied = new List<AppliedEdit>();
        var backupDir = _paths.EmuBackupDir(emu.Id, opt.Id);
        try
        {
            foreach (var entry in zip.Entries)
            {
                var name = Zip.NormalizeEntryName(entry.FullName);
                if (!name.StartsWith("files/", StringComparison.Ordinal) || name.EndsWith('/')) continue;
                var rel = name["files/".Length..];
                if (rel.Length == 0) continue;
                var dest = Zip.ResolveDest(folder, rel);
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

            foreach (var edit in settings.Edits)
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
            RevertEdits(folder, applied, warnings: null, bestEffort: true);
            RemoveFiles(folder, written, backups, backupDir, warnings: null, bestEffort: true);
            return new OpResult(false, key, $"install failed, nothing changed: {ex.Message}");
        }
        return new OpResult(true, key, $"installed {emu.Name} {opt.Label} v{opt.Version}");
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

    /// <summary>Deletes the files we installed and puts back the ones they replaced.</summary>
    static void RemoveFiles(string folder, List<InstalledFile> files, List<BackupRef> backups, string backupDir,
                            List<string>? warnings, bool bestEffort)
    {
        foreach (var f in files)
        {
            var p = Path.Combine(folder, f.RelPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(p)) continue;
            if (warnings is not null
                && !string.Equals(Installer.Sha256HexFile(p), f.Sha256, StringComparison.OrdinalIgnoreCase))
                warnings.Add($"{f.RelPath} had been changed since install and was removed");
            Try(bestEffort, () => File.Delete(p));
        }
        foreach (var b in backups)
        {
            var src = Path.Combine(backupDir, b.RelPath.Replace('/', Path.DirectorySeparatorChar));
            var dst = Path.Combine(folder, b.RelPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(src)) continue;
            Try(bestEffort, () =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(src, dst, overwrite: true);
            });
        }
        foreach (var f in files)
            PruneEmptyParents(folder, Path.Combine(folder, f.RelPath.Replace('/', Path.DirectorySeparatorChar)));
        Try(true, () => { if (Directory.Exists(backupDir)) Directory.Delete(backupDir, recursive: true); });
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
