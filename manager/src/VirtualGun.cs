using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
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

    public async Task<OpResult> Install(VirtualGunInfo? info)
    {
        if (info is null)
            return new OpResult(false, Key, "this registry has no virtualgun entry; update vrlf-mods");

        var bytes = await _http.TryGet(ZipUrl(info));
        if (bytes is null)
            return new OpResult(false, Key, "could not download the Virtual Lightgun release (network required)");
        if (!string.Equals(Installer.Sha256Hex(bytes), info.Sha256, StringComparison.OrdinalIgnoreCase))
            return new OpResult(false, Key, "download hash mismatch; refusing to install");

        var dir = StagingDir(info);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        try
        {
            using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                var dest = Zip.ResolveDest(dir, entry.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            return new OpResult(false, Key, $"the release zip is corrupt or unsafe: {ex.Message}");
        }

        var setup = Path.Combine(dir, SetupExe);
        if (!File.Exists(setup))
            return new OpResult(false, Key, "the release zip has no installer");

        try
        {
            return _runner.RunElevated(setup, "install") switch
            {
                0 => new OpResult(true, Key, $"Virtual Lightgun {info.Version} installed"),
                3010 => new OpResult(true, Key, $"Virtual Lightgun {info.Version} installed; restart Windows to finish"),
                UacDeclined => new OpResult(false, Key, "install cancelled at the UAC prompt"),
                var code => new OpResult(false, Key,
                    $"installer failed (exit {code}); see %ProgramData%\\VRLF\\VirtualGun\\setup.log"),
            };
        }
        catch (Win32Exception ex)
        {
            return new OpResult(false, Key, $"could not launch the installer: {ex.Message}");
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

    public OpResult Status(VirtualGunInfo? info)
    {
        var installed = _state.InstalledVersion();
        if (installed is null)
            return new OpResult(true, Key, "Virtual Lightgun: not installed");
        var latest = info?.Version.TrimStart('v');
        return new OpResult(true, Key, latest is null || latest == installed
            ? $"Virtual Lightgun: installed v{installed}"
            : $"Virtual Lightgun: installed v{installed} (update to v{latest})");
    }
}
