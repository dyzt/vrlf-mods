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

/// <summary>`vrlf-mods prereqs`: VRLF's Steam install script runs it once, before the first
/// launch. Offers each missing driver and installs the ones the player accepts. Always
/// returns 0: Steam runs the script again on every launch until it exits 0.</summary>
public sealed class Prereqs
{
    /// <summary>A player who launched from the headset cannot see the console; no answer in
    /// this long means no, and VRLF starts.</summary>
    public static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(60);

    private const string PadLine = "ViGEmBus: virtual Xbox pads, for co-op and many emulator profiles.";
    private const string GunLine =
        "Virtual Lightgun: a virtual mouse per gun, for the few games that read each mouse separately.\n" +
        "It trusts a signing certificate made on this PC.";

    private readonly RegistryLoader _loader;
    private readonly Vigem _vigem;
    private readonly VirtualGun _gun;
    private readonly IElevatedRunner _runner;
    private readonly IPrompt _io;
    private bool _timedOut;

    public Prereqs(RegistryLoader loader, Vigem vigem, VirtualGun gun, IElevatedRunner runner, IPrompt io)
    { _loader = loader; _vigem = vigem; _gun = gun; _runner = runner; _io = io; }

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
        if (pad) Show(await Attempt(() => _vigem.InstallAndWait(reg.Vigembus, _runner)));
        if (gun) Show(await Attempt(() => _gun.Install(reg.Virtualgun)));
        await Pause();
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

    private void Show(OpResult r) =>
        _io.Write(r.Ok ? $"{r.Message}\n" : $"{r.Message}. Run vrlf-mods.exe in the VRLF folder to try again.\n");

    private async Task Pause()
    {
        _io.Write("\nPress Enter to start VRLF.");
        await _io.ReadLine(AnswerTimeout);
    }
}
