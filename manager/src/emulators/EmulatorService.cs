namespace VrlfMods;

public record EmulatorOptionStatus(string Id, string Label, string Short, string Version,
    string? InstalledVersion, string? Requires);

/// <param name="Folder">Where options are (or will be) installed: the receipts' folder, else the chosen one.</param>
/// <param name="Suggested">A found main install, offered only while no folder is chosen.</param>
/// <param name="Locked">Something is installed, so the folder cannot change.</param>
public record EmulatorStatus(string Id, string Name, string? Folder, bool FolderChosen, bool FolderExists,
    string? Suggested, bool Locked, List<EmulatorOptionStatus> Options,
    string? Needs, bool NeedsMet, string? Profile, string? Notes);

public record EmulatorListReport(List<EmulatorStatus> Emulators);

/// <summary>What the CLI and TUI call: folder choice, requires, cascade and status over
/// <see cref="EmulatorInstaller"/>.</summary>
public sealed class EmulatorService
{
    private readonly RegistryLoader _loader;
    private readonly EmulatorFolders _folders;
    private readonly EmulatorInstaller _installer;
    private readonly EmulatorReceiptStore _receipts;
    private readonly Vigem _vigem;

    public EmulatorService(RegistryLoader loader, EmulatorFolders folders, EmulatorInstaller installer,
                           EmulatorReceiptStore receipts, Vigem vigem)
    { _loader = loader; _folders = folders; _installer = installer; _receipts = receipts; _vigem = vigem; }

    public List<EmulatorStatus> StatusesFor(ModRegistry reg) => reg.EmulatorList.Select(StatusFor).ToList();

    /// <summary>Where an emulator's options are (or will be) installed: an existing receipt's
    /// folder wins, else the one the user chose. The one rule both <see cref="StatusFor"/> and
    /// <see cref="Install"/> use.</summary>
    string? FolderFor(EmulatorEntry e) => _receipts.ForEmulator(e.Id).FirstOrDefault()?.Folder ?? _folders.Chosen(e);

    public EmulatorStatus StatusFor(EmulatorEntry e)
    {
        var receipts = _receipts.ForEmulator(e.Id);
        var folder = FolderFor(e);
        var options = e.Options.Select(o => new EmulatorOptionStatus(o.Id, o.Label, o.Short ?? o.Label, o.Version,
            receipts.FirstOrDefault(r => r.OptionId == o.Id)?.Version, o.Requires)).ToList();
        bool needsMet = e.Needs switch { null => true, "vigembus" => _vigem.IsInstalled(), _ => false };
        return new EmulatorStatus(e.Id, e.Name, folder, folder is not null,
            folder is not null && Directory.Exists(folder),
            folder is null ? _folders.Suggest(e) : null, receipts.Count > 0,
            options, e.Needs, needsMet, e.Profile, e.Notes);
    }

    public async Task<List<EmulatorStatus>> List()
    {
        var (reg, _) = await _loader.Load();
        return StatusesFor(reg);
    }

    public async Task<EmulatorStatus?> Status(string id)
    {
        var (reg, _) = await _loader.Load();
        var e = reg.FindEmulator(id);
        return e is null ? null : StatusFor(e);
    }

    public async Task<ActionReport> SetFolder(string id, string dir)
    {
        var (emu, fail) = await Find(id, "path");
        if (emu is null) return fail!;
        if (LockedMessage(emu) is { } locked) return Fail("path", locked, emu.Id);
        var check = _folders.Resolve(emu, dir);
        if (!check.Ok) return Fail("path", check.Error!, emu.Id);
        _folders.Choose(emu, check.Folder!);
        return Ok("path", emu.Id, $"{emu.Name}: using {check.Folder}");
    }

    public async Task<ActionReport> UseSuggested(string id)
    {
        var (emu, fail) = await Find(id, "path");
        if (emu is null) return fail!;
        if (LockedMessage(emu) is { } locked) return Fail("path", locked, emu.Id);
        var s = _folders.Suggest(emu);
        return s is null
            ? Fail("path", $"no {emu.Name} settings folder found; choose one", emu.Id)
            : await SetFolder(id, s);
    }

    public async Task<ActionReport> ClearFolder(string id)
    {
        var (emu, fail) = await Find(id, "path");
        if (emu is null) return fail!;
        if (LockedMessage(emu) is { } locked) return Fail("path", locked, emu.Id);
        _folders.Forget(emu);
        return Ok("path", emu.Id, $"{emu.Name}: settings folder forgotten");
    }

    public async Task<ActionReport> Install(string id, string? optionId)
    {
        var (emu, fail) = await Find(id, "install");
        if (emu is null) return fail!;
        var opt = Option(emu, optionId ?? "base");
        if (opt is null) return Fail("install", $"{emu.Id} has no option '{optionId ?? "base"}'", emu.Id);

        var folder = FolderFor(emu);
        if (folder is null) return Fail("install", $"choose {emu.Name}'s settings folder first", emu.Id);
        if (!Directory.Exists(folder))
            return Fail("install", $"{emu.Name}'s settings folder is no longer there: {folder}", emu.Id);
        if (opt.Requires is { } req && _receipts.Load(emu.Id, req) is null)
            return Fail("install", $"install {Option(emu, req)?.Label ?? req} first", emu.Id);

        var res = await _installer.Install(emu, opt, folder);
        return new ActionReport(res.Ok, "install", new() { res });
    }

    /// <summary>Removes one option and everything that needs it (dependants first), or all
    /// options when <paramref name="optionId"/> is null: then first any installed option that
    /// <c>mods.json</c> no longer lists, which nothing else could ever remove. Stops at the first
    /// failure so a base is never removed from under an add-on that is still installed.</summary>
    public async Task<ActionReport> Uninstall(string id, string? optionId)
    {
        var (emu, fail) = await Find(id, "uninstall");
        if (emu is null) return fail!;
        IEnumerable<EmulatorOption> scope = emu.Options;
        var results = new List<OpResult>();
        if (optionId is not null)
        {
            var root = Option(emu, optionId);
            if (root is null) return Fail("uninstall", $"{emu.Id} has no option '{optionId}'", emu.Id);
            scope = emu.Options.Where(o => o.Id == root.Id || DependsOn(emu, o, root.Id));
        }
        else
        {
            foreach (var r in _receipts.ForEmulator(emu.Id).Where(x => Option(emu, x.OptionId) is null))
            {
                var res = _installer.Uninstall(emu, r);
                results.Add(res);
                if (!res.Ok) return new ActionReport(false, "uninstall", results);
            }
        }

        foreach (var o in scope.OrderByDescending(o => Depth(emu, o)))
        {
            var r = _receipts.Load(emu.Id, o.Id);
            if (r is null) continue;
            var res = _installer.Uninstall(emu, r);
            results.Add(res);
            if (!res.Ok) break;
        }
        if (results.Count == 0) return Fail("uninstall", $"nothing from {emu.Name} is installed", emu.Id);
        return new ActionReport(results.All(r => r.Ok), "uninstall", results);
    }

    public Task<ActionReport> Update(string id) => Refresh(id, onlyIfNewer: true, "update");
    public Task<ActionReport> Reapply(string id) => Refresh(id, onlyIfNewer: false, "reapply");

    /// <summary>Bases first, then what builds on them. Stops at the first failure: a base that
    /// failed (possibly after its own uninstall) must not have add-ons reinstalled on top.</summary>
    async Task<ActionReport> Refresh(string id, bool onlyIfNewer, string cmd)
    {
        var (emu, fail) = await Find(id, cmd);
        if (emu is null) return fail!;
        var results = new List<OpResult>();
        foreach (var o in emu.Options.OrderBy(o => Depth(emu, o)))
        {
            var r = _receipts.Load(emu.Id, o.Id);
            if (r is null) continue;
            var res = await _installer.Refresh(emu, o, r, onlyIfNewer);
            results.Add(res);
            if (!res.Ok) break;
        }
        if (results.Count == 0) return Fail(cmd, $"nothing from {emu.Name} is installed", emu.Id);
        return new ActionReport(results.All(r => r.Ok), cmd, results);
    }

    static EmulatorOption? Option(EmulatorEntry e, string id) =>
        e.Options.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Bounded the same way <see cref="Depth"/> is: an entry's own <c>requires</c> chain
    /// is fetched live from <c>mods.json</c> and never validated, so an indirect cycle (A requires
    /// B, B requires A) must not walk forever just because it isn't a direct self-reference.</summary>
    static bool DependsOn(EmulatorEntry e, EmulatorOption o, string target)
    {
        var cur = o;
        for (int steps = 0; cur.Requires is { } req && steps < e.Options.Count; steps++)
        {
            if (string.Equals(req, target, StringComparison.OrdinalIgnoreCase)) return true;
            var next = Option(e, req);
            if (next is null) return false;
            cur = next;
        }
        return false;
    }

    static int Depth(EmulatorEntry e, EmulatorOption o)
    {
        int d = 0;
        for (var cur = o; cur.Requires is { } req && d < e.Options.Count; d++)
        {
            var next = Option(e, req);
            if (next is null) break;
            cur = next;
        }
        return d;
    }

    string? LockedMessage(EmulatorEntry e)
    {
        var r = _receipts.ForEmulator(e.Id).FirstOrDefault();
        return r is null ? null : $"Installed in {r.Folder}. Uninstall to move.";
    }

    async Task<(EmulatorEntry? emu, ActionReport? fail)> Find(string id, string cmd)
    {
        var (reg, _) = await _loader.Load();
        var e = reg.FindEmulator(id);
        return e is null ? (null, Fail(cmd, $"unknown emulator id '{id}'", "")) : (e, null);
    }

    static ActionReport Ok(string cmd, string key, string msg) => new(true, cmd, new() { new OpResult(true, key, msg) });
    static ActionReport Fail(string cmd, string msg, string key) => new(false, cmd, new() { new OpResult(false, key, msg) });
}
