using System.ComponentModel;
using System.Diagnostics;

namespace VrlfMods;

/// <summary>Console in and out for `prereqs`. ReadLine returns null once the wait runs out or
/// input has ended.</summary>
public interface IPrompt
{
    void Write(string text);
    Task<string?> ReadLine(TimeSpan timeout);
}

public sealed class ConsolePrompt : IPrompt
{
    // A read that timed out is still blocked on the console; the next ReadLine waits on it
    // rather than starting a second one.
    private Task<string?>? _pending;

    public void Write(string text) => Console.Write(text);

    public async Task<string?> ReadLine(TimeSpan timeout)
    {
        _pending ??= Task.Run(Console.ReadLine);
        if (await Task.WhenAny(_pending, Task.Delay(timeout)) != _pending) return null;
        var line = _pending.Result;
        _pending = null;
        return line;
    }
}

/// <summary>Starts a Windows restart; false if it could not be started.</summary>
public interface IRestarter { bool Restart(); }

public sealed class WindowsRestarter : IRestarter
{
    // No /f, so an app holding unsaved work can still stop it. A /t above 0 would imply /f.
    public bool Restart()
    {
        try
        {
            var exe = Path.Combine(Environment.SystemDirectory, "shutdown.exe");
            using var p = Process.Start(new ProcessStartInfo(exe, "/r /t 0")
                { UseShellExecute = false, CreateNoWindow = true })!;
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch (Win32Exception) { return false; }
    }
}

/// <summary>`vrlf-mods prereqs`: VRLF's Steam install script runs it once, before the first
/// launch. Offers each missing driver and installs the ones the player accepts. Always
/// returns 0: Steam runs the script again on every launch until it exits 0.</summary>
public sealed class Prereqs
{
    /// <summary>A player who launched from the headset cannot see the console; no answer in
    /// this long means no, and VRLF starts.</summary>
    public static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Once a restart is started, how long to keep Steam from launching VRLF into it.
    /// The restart normally ends this process first; if an app cancels it, VRLF starts after.</summary>
    public static readonly TimeSpan RestartWait = TimeSpan.FromSeconds(60);

    private const string PadLine = "ViGEmBus: virtual Xbox pads, for co-op and many emulator profiles.";
    private const string GunLine =
        "Virtual Lightgun: a virtual mouse per gun, for the few games that read each mouse separately.\n" +
        "It trusts a signing certificate made on this PC.";

    private readonly RegistryLoader _loader;
    private readonly Vigem _vigem;
    private readonly VirtualGun _gun;
    private readonly IElevatedRunner _runner;
    private readonly IPrompt _io;
    private readonly IRestarter _restarter;
    private readonly TimeSpan _restartWait;
    private bool _timedOut;

    public Prereqs(RegistryLoader loader, Vigem vigem, VirtualGun gun, IElevatedRunner runner, IPrompt io,
        IRestarter restarter, TimeSpan restartWait)
    {
        _loader = loader; _vigem = vigem; _gun = gun; _runner = runner; _io = io;
        _restarter = restarter; _restartWait = restartWait;
    }

    public async Task<int> Run()
    {
        try { await Offer(); }
        catch (Exception ex)
        {
            _io.Write($"error: {ex.Message}\n");
            await Pause();
        }
        return 0;
    }

    private async Task Offer()
    {
        bool needPad = !_vigem.IsInstalled();
        bool needGun = !_gun.IsInstalled();
        if (!needPad && !needGun) return;

        var (reg, _) = await _loader.Load();
        needGun = needGun && reg.Virtualgun is not null;
        if (!needPad && !needGun) return;

        _io.Write("VRLF drivers\n\n" +
                  "Some VRLF profiles need these. vrlf-mods.exe in the VRLF folder installs them later too.\n" +
                  $"No answer in {AnswerTimeout.TotalSeconds:0} seconds skips them.\n");
        bool pad = needPad && await Ask(PadLine, "Install ViGEmBus?", enterIsYes: true);
        // Enter alone does not trust a root certificate.
        bool gun = needGun && await Ask(GunLine, "Install Virtual Lightgun?", enterIsYes: false);
        if (!pad && !gun) return;

        _io.Write("\n");
        bool installed = false;
        if (pad) installed |= Show(await Attempt(() => _vigem.InstallAndWait(reg.Vigembus, _runner)));
        if (gun) installed |= Show(await Attempt(() => _gun.Install(reg.Virtualgun)));
        if (installed) await OfferRestart();
        else await Pause();
    }

    private async Task OfferRestart()
    {
        // Enter alone does not restart: the player may have unsaved work open.
        if (!await Ask("Restart Windows to finish setting up the drivers.", "Restart now?", enterIsYes: false))
            return;
        if (!_restarter.Restart())
        {
            _io.Write("Windows did not restart. Restart it before playing.\n");
            await Pause();
            return;
        }
        _io.Write("Restarting Windows. Start VRLF again afterwards.\n");
        await Task.Delay(_restartWait);
    }

    private async Task<bool> Ask(string about, string question, bool enterIsYes)
    {
        if (_timedOut) return false;
        _io.Write($"\n{about}\n{question} {(enterIsYes ? "[Y/n]" : "[y/N]")} ");
        var answer = await _io.ReadLine(AnswerTimeout);
        if (answer is null) { _timedOut = true; _io.Write("\n"); return false; }
        return answer.Trim().ToLowerInvariant() switch
        {
            "" => enterIsYes,
            "y" or "yes" => true,
            _ => false,
        };
    }

    /// <summary>One driver's failure must not skip the other.</summary>
    private static async Task<OpResult> Attempt(Func<Task<OpResult>> install)
    {
        try { return await install(); }
        catch (Exception ex) { return new OpResult(false, "", ex.Message); }
    }

    private bool Show(OpResult r)
    {
        _io.Write(r.Ok ? $"{r.Message}\n" : $"{r.Message}. Run vrlf-mods.exe in the VRLF folder to try again.\n");
        return r.Ok;
    }

    private async Task Pause()
    {
        _io.Write("\nPress Enter to start VRLF.");
        await _io.ReadLine(AnswerTimeout);
    }
}
