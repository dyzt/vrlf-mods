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

        var bytes = await _http.TryGet(InstallerUrl(info));
        if (bytes is null)
            return new OpResult(false, "vigembus", "could not download the ViGEmBus installer (network required)");

        Directory.CreateDirectory(_paths.ModsRoot);
        var exe = Path.Combine(_paths.ModsRoot, $"ViGEmBus_{info.Version}.exe");
        await File.WriteAllBytesAsync(exe, bytes);
        _launcher.Launch(exe);
        return new OpResult(true, "vigembus", "launched the ViGEmBus installer — follow its prompts (UAC)");
    }
}
