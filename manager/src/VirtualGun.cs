using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace VrlfMods;

public interface IElevatedRunner { int RunElevated(string exe, string args); }

public sealed class ElevatedRunner : IElevatedRunner
{
    public int RunElevated(string exe, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
            })!;
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return VirtualGun.UacDeclined;
        }
    }
}

public interface IVirtualGunState
{
    string? InstalledVersion();
    string? InstallDir();
}

/// <summary>Reads what vrlf-virtual-gun-setup.exe records under HKLM\SOFTWARE\VRLF\VirtualGun.</summary>
public sealed class RegistryVirtualGunState : IVirtualGunState
{
    private const string Key = @"SOFTWARE\VRLF\VirtualGun";

    public string? InstalledVersion() => Read("Version");
    public string? InstallDir() => Read("InstallDir");

    private static string? Read(string name)
    {
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = hive.OpenSubKey(Key);
        var value = key?.GetValue(name) as string;
        return string.IsNullOrEmpty(value) ? null : value;
    }
}

/// <summary>Installs the vrlf-virtual-gun driver from its GitHub release.</summary>
public sealed class VirtualGun
{
    public const string SetupExe = "vrlf-virtual-gun-setup.exe";
    public const int UacDeclined = -1223;
    private const string Key = "virtualgun";

    private readonly IVirtualGunState _state;
    private readonly IHttpFetcher _http;
    private readonly IElevatedRunner _runner;
    private readonly AppPaths _paths;

    public VirtualGun(IVirtualGunState state, IHttpFetcher http, IElevatedRunner runner, AppPaths paths)
    { _state = state; _http = http; _runner = runner; _paths = paths; }

    public static string ZipUrl(VirtualGunInfo info) =>
        $"https://github.com/{info.Repo}/releases/download/{info.Version}/{info.Zip}";

    /// <summary>The installer writes Version last, so its presence means a complete install.</summary>
    public bool IsInstalled() => _state.InstalledVersion() is not null;

    public string StagingDir(VirtualGunInfo info) => Path.Combine(_paths.ModsRoot, "virtualgun", info.Version);

    /// <summary>Version comes from the registry and becomes a folder name — never trust it verbatim.</summary>
    private static bool IsValidVersionFolder(string version) =>
        !version.Contains("..") && version.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    /// <summary>Re-hash on disk right before an elevated launch, closing the window for anything
    /// else with write access to the staging dir to swap the binary after extraction.</summary>
    internal static bool ExtractedInstallerMatches(string path, byte[] expectedSha) =>
        File.Exists(path) && SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(expectedSha);

    public async Task<OpResult> Install(VirtualGunInfo? info)
    {
        if (info is null)
            return new OpResult(false, Key, "this vrlf-mods release does not offer the Virtual Lightgun yet");
        if (!IsValidVersionFolder(info.Version))
            return new OpResult(false, Key, "registry version is not a valid folder name");

        var bytes = await _http.TryGet(ZipUrl(info));
        if (bytes is null)
            return new OpResult(false, Key, "could not download the Virtual Lightgun release (network required)");
        if (!string.Equals(Installer.Sha256Hex(bytes), info.Sha256, StringComparison.OrdinalIgnoreCase))
            return new OpResult(false, Key, "download hash mismatch; refusing to install");

        var dir = StagingDir(info);
        byte[]? setupHash = null;
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            Directory.CreateDirectory(dir);

            using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                var dest = Zip.ResolveDest(dir, entry.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                if (string.Equals(Zip.NormalizeEntryName(entry.FullName), SetupExe, StringComparison.OrdinalIgnoreCase))
                {
                    using var es = entry.Open();
                    using var ms = new MemoryStream();
                    es.CopyTo(ms);
                    var entryBytes = ms.ToArray();
                    setupHash = SHA256.HashData(entryBytes);
                    File.WriteAllBytes(dest, entryBytes);
                }
                else
                {
                    entry.ExtractToFile(dest, overwrite: true);
                }
            }
        }
        catch (Exception ex)
        {
            return new OpResult(false, Key, $"the release zip is corrupt or unsafe: {ex.Message}");
        }

        var setup = Path.Combine(dir, SetupExe);
        if (!File.Exists(setup))
            return new OpResult(false, Key, "the release zip has no installer");
        if (setupHash is null || !ExtractedInstallerMatches(setup, setupHash))
            return new OpResult(false, Key, "the extracted installer changed on disk; refusing to run it");

        try
        {
            var result = _runner.RunElevated(setup, "install") switch
            {
                0 => new OpResult(true, Key, $"Virtual Lightgun {info.Version} installed"),
                3010 => new OpResult(true, Key, $"Virtual Lightgun {info.Version} installed; restart Windows to finish"),
                UacDeclined => new OpResult(false, Key, "install cancelled at the UAC prompt"),
                var code => new OpResult(false, Key,
                    $"installer failed (exit {code}); see %ProgramData%\\VRLF\\VirtualGun\\setup.log"),
            };
            return result;
        }
        catch (Win32Exception ex)
        {
            return new OpResult(false, Key, $"could not launch the installer: {ex.Message}");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public OpResult Uninstall()
    {
        if (!IsInstalled())
            return new OpResult(true, Key, "Virtual Lightgun is not installed");
        var dir = _state.InstallDir();
        var setup = dir is null ? null : Path.Combine(dir, SetupExe);
        if (setup is null || !File.Exists(setup))
            return new OpResult(false, Key, "installed, but its uninstaller is missing; run install again first");
        try
        {
            return _runner.RunElevated(setup, "uninstall") switch
            {
                0 => new OpResult(true, Key, "Virtual Lightgun uninstalled"),
                UacDeclined => new OpResult(false, Key, "uninstall cancelled at the UAC prompt"),
                var code => new OpResult(false, Key,
                    $"uninstaller failed (exit {code}); see %ProgramData%\\VRLF\\VirtualGun\\setup.log"),
            };
        }
        catch (Win32Exception ex)
        {
            return new OpResult(false, Key, $"could not launch the installer: {ex.Message}");
        }
    }

    /// <summary>The pinned registry version, when it differs from what's installed (null if not
    /// installed, or nothing pinned, or already current).</summary>
    public string? PendingUpdate(VirtualGunInfo? info)
    {
        var installed = _state.InstalledVersion()?.TrimStart('v');
        if (installed is null || info is null) return null;
        var latest = info.Version.TrimStart('v');
        return latest == installed ? null : latest;
    }

    public OpResult Status(VirtualGunInfo? info)
    {
        var installed = _state.InstalledVersion()?.TrimStart('v');
        if (installed is null)
            return new OpResult(true, Key, "Virtual Lightgun: not installed");
        var latest = PendingUpdate(info);
        return new OpResult(true, Key, latest is null
            ? $"Virtual Lightgun: installed v{installed}"
            : $"Virtual Lightgun: installed v{installed} (update to v{latest})");
    }
}
