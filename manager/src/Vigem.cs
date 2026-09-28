using System.Diagnostics;
using Microsoft.Win32;

namespace VrlfMods;

public interface IServiceDetector { bool ServiceExists(string name); }

public sealed class RegistryServiceDetector : IServiceDetector
{
    public bool ServiceExists(string name) =>
        Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}") is not null;
}

public interface IProcessLauncher { void Launch(string path); }

public sealed class ProcessLauncher : IProcessLauncher
{
    public void Launch(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
}

public sealed class Vigem
{
    private readonly IServiceDetector _detector;
    private readonly IHttpFetcher _http;
    private readonly IProcessLauncher _launcher;
    private readonly AppPaths _paths;

    public Vigem(IServiceDetector detector, IHttpFetcher http, IProcessLauncher launcher, AppPaths paths)
    { _detector = detector; _http = http; _launcher = launcher; _paths = paths; }

    public bool IsInstalled() => _detector.ServiceExists("ViGEmBus");

    public static string InstallerUrl(VigemInfo info)
    {
        var v = info.Version.TrimStart('v');
        return $"https://github.com/{info.Repo}/releases/download/{info.Version}/ViGEmBus_{v}_x64_x86_arm64.exe";
    }

    public async Task<OpResult> Ensure(VigemInfo info)
    {
        if (IsInstalled())
            return new OpResult(true, "vigembus", "ViGEmBus already installed");

        var exe = await Download(info);
        if (exe is null) return DownloadFailed;
        _launcher.Launch(exe);
        return new OpResult(true, "vigembus", "launched the ViGEmBus installer — follow its prompts (UAC)");
    }

    /// <summary>Ensure, but waits for the installer: `prereqs` runs inside Steam's install
    /// script, and VRLF must not start until the driver is in.</summary>
    public async Task<OpResult> InstallAndWait(VigemInfo info, IElevatedRunner runner)
    {
        if (IsInstalled())
            return new OpResult(true, "vigembus", "ViGEmBus already installed");

        var exe = await Download(info);
        if (exe is null) return DownloadFailed;
        try
        {
            return runner.RunElevated(exe, "") switch
            {
                0 => new OpResult(true, "vigembus", "ViGEmBus installed"),
                3010 => new OpResult(true, "vigembus", "ViGEmBus installed; restart Windows to finish"),
                VirtualGun.UacDeclined => new OpResult(false, "vigembus", "install cancelled at the UAC prompt"),
                1602 => new OpResult(false, "vigembus", "install cancelled"),
                var code => new OpResult(false, "vigembus", $"the ViGEmBus installer failed (exit {code})"),
            };
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return new OpResult(false, "vigembus", $"could not launch the installer: {ex.Message}");
        }
    }

    private static readonly OpResult DownloadFailed =
        new(false, "vigembus", "could not download the ViGEmBus installer (network required)");

    private async Task<string?> Download(VigemInfo info)
    {
        var bytes = await _http.TryGet(InstallerUrl(info));
        if (bytes is null) return null;

        Directory.CreateDirectory(_paths.ModsRoot);
        var exe = Path.Combine(_paths.ModsRoot, $"ViGEmBus_{info.Version}.exe");
        await File.WriteAllBytesAsync(exe, bytes);
        return exe;
    }
}
