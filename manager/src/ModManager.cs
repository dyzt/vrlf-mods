using System.IO;
using VrlfMods.Patches;

namespace VrlfMods;

public record GameStatus(long Appid, string Name, bool Detected, string? Path, bool Installed,
    string? InstalledVersion, bool Manual = false);
public record ModStatus(string Id, string Name, string Version, string? InstalledVersion, List<GameStatus> Games);
public record ListReport(string RegistrySource, List<ModStatus> Mods, bool VigemInstalled = false,
    bool VirtualGunInstalled = false, bool VirtualGunAvailable = false, string? VirtualGunUpdate = null,
    string? VirtualGunVersion = null);
public record ActionReport(bool Ok, string Command, List<OpResult> Results);

public sealed class ModManager
{
    private readonly RegistryLoader _loader;
    private readonly GameLocator _locator;
    private readonly Installer _installer;
    private readonly ReceiptStore _receipts;
    private readonly Vigem _vigem;
    private readonly ConfigController _config;
    private readonly VirtualGun? _virtualGun;

    public ModManager(RegistryLoader loader, GameLocator locator, Installer installer,
                      ReceiptStore receipts, Vigem vigem, PatchRegistry? patches = null,
                      VirtualGun? virtualGun = null)
    { _loader = loader; _locator = locator; _installer = installer; _receipts = receipts; _vigem = vigem;
      _config = new ConfigController(patches ?? PatchRegistry.Default()); _virtualGun = virtualGun; }

    public async Task<ListReport> List()
    {
        var (reg, source) = await _loader.Load();
        return new ListReport(source, reg.Mods.Select(StatusFor).ToList(), _vigem.IsInstalled(),
            _virtualGun?.IsInstalled() ?? false, reg.Virtualgun is not null,
            _virtualGun?.PendingUpdate(reg.Virtualgun), _virtualGun?.InstalledVersion());
    }

    public async Task<ModStatus?> Status(string id)
    {
        var (reg, _) = await _loader.Load();
        var mod = reg.Find(id);
        return mod is null ? null : StatusFor(mod);
    }

    public async Task<ModConfig?> GetConfig(string id, string gameKey)
    {
        var (reg, _) = await _loader.Load();
        var mod = reg.Find(id);
        if (mod?.Config is null) return null;
        var rcpt = _receipts.Load(id, gameKey);
        if (rcpt is null) return null;
        return _config.Read(mod, gameKey, rcpt.GamePath);
    }

    public async Task<OpResult> SetToggle(string id, string gameKey, string toggleKey, bool on)
    {
        var (reg, _) = await _loader.Load();
        var mod = reg.Find(id);
        if (mod?.Config is null) return new OpResult(false, gameKey, $"{id} has no configurable options");
        var rcpt = _receipts.Load(id, gameKey);
        if (rcpt is null) return new OpResult(false, gameKey, $"{id} is not installed for game {gameKey}");
        return _config.Set(mod, gameKey, rcpt.GamePath, toggleKey, on);
    }

    private ModStatus StatusFor(ModEntry mod)
    {
        string? installedVersion = null;
        var games = new List<GameStatus>();
        foreach (var g in mod.Games)
        {
            var key = AppPaths.GameKeyForAppid(g.Appid);
            var found = _locator.Find(g.Appid);
            var manual = _locator.ManualPath(g.Appid);
            var rcpt = _receipts.Load(mod.Id, key);
            if (rcpt is not null) installedVersion = rcpt.Version;
            // A stale override is reported undetected but still NAMED, so the user can see
            // which folder to fix rather than just being told the game is missing.
            games.Add(new GameStatus(g.Appid, g.Name, found is not null, found?.Path ?? manual,
                rcpt is not null, rcpt?.Version, found?.Manual ?? manual is not null));
        }
        return new ModStatus(mod.Id, mod.Name, mod.Version, installedVersion, games);
    }

    public async Task<ActionReport> Install(string id, long? appid, string? path)
    {
        var (reg, _) = await _loader.Load();
        var mod = reg.Find(id);
        if (mod is null) return Fail("install", $"unknown mod id '{id}'");

        var targets = ResolveTargets(mod, appid, path, out var problems);
        if (targets.Count == 0) return new ActionReport(false, "install", problems);

        var results = new List<OpResult>(problems);
        foreach (var t in targets) results.Add(await _installer.Install(mod, t));
        return new ActionReport(results.All(r => r.Ok), "install", results);
    }

    public async Task<ActionReport> Uninstall(string id, long? appid)
    {
        var (reg, _) = await _loader.Load();
        var mod = reg.Find(id);
        if (mod is null) return Fail("uninstall", $"unknown mod id '{id}'");

        var results = new List<OpResult>();
        var keys = appid is null
            ? mod.Games.Select(g => AppPaths.GameKeyForAppid(g.Appid)).ToList()
            : new List<string> { AppPaths.GameKeyForAppid(appid.Value) };
        // Legacy: builds before the manual-path override keyed a --path install as
        // custom-<hash>. Nothing writes those any more, but sweep them so an install
        // made by an older build still uninstalls cleanly.
        keys.AddRange(_receipts.All().Where(r => r.ModId == mod.Id && r.GameKey.StartsWith("custom-"))
                                     .Select(r => r.GameKey));
        foreach (var key in keys.Distinct())
        {
            var rcpt = _receipts.Load(mod.Id, key);
            if (rcpt is null) continue;
            var patchWarnings = _config.RevertPatches(mod, key, rcpt.GamePath);   // return the game fully stock
            var un = _installer.Uninstall(rcpt);
            if (patchWarnings.Count > 0 && un.Ok)
                un = un with { Message = un.Message + "; warning: " + string.Join("; ", patchWarnings) };
            results.Add(un);
        }
        if (results.Count == 0) return Fail("uninstall", $"{mod.Id} is not installed");
        return new ActionReport(results.All(r => r.Ok), "uninstall", results);
    }

    public async Task<ActionReport> Update(string? id)
    {
        var (reg, _) = await _loader.Load();
        var results = new List<OpResult>();
        foreach (var rcpt in _receipts.All())
        {
            if (id is not null && !string.Equals(rcpt.ModId, id, StringComparison.OrdinalIgnoreCase)) continue;
            var mod = reg.Find(rcpt.ModId);
            if (mod is null) continue;
            results.Add(await _installer.Update(mod, rcpt));
        }
        if (results.Count == 0) return Fail("update", id is null ? "nothing installed" : $"{id} is not installed");
        return new ActionReport(results.All(r => r.Ok), "update", results);
    }

    public async Task<ActionReport> EnsureVigem()
    {
        var (reg, _) = await _loader.Load();
        var res = await _vigem.Ensure(reg.Vigembus);
        return new ActionReport(res.Ok, "vigembus", new() { res });
    }

    public async Task<ActionReport> VirtualGunInstall()
    {
        var (reg, _) = await _loader.Load();
        var res = await Gun().Install(reg.Virtualgun);
        return new ActionReport(res.Ok, "virtualgun", new() { res });
    }

    public Task<ActionReport> VirtualGunUninstall()
    {
        var res = Gun().Uninstall();
        return Task.FromResult(new ActionReport(res.Ok, "virtualgun", new() { res }));
    }

    public async Task<ActionReport> VirtualGunStatus()
    {
        var (reg, _) = await _loader.Load();
        var res = Gun().Status(reg.Virtualgun);
        return new ActionReport(res.Ok, "virtualgun", new() { res });
    }

    private VirtualGun Gun() =>
        _virtualGun ?? throw new InvalidOperationException("Virtual Lightgun support is not wired in");

    public async Task<ActionReport> SetGamePath(string modId, long? appid, string dir)
    {
        var (game, err) = await ResolveGame(modId, appid);
        if (game is null) return Fail("path", err!);

        var key = AppPaths.GameKeyForAppid(game.Appid);
        var check = GamePathCheck.Inspect(dir);
        if (!check.Ok) return new ActionReport(false, "path", new() { new OpResult(false, key, check.Error!) });

        _locator.SetManual(game.Appid, check.Full!);
        var msg = $"{game.Name}: using {check.Full}";
        if (check.Warning is not null) msg += "; warning: " + check.Warning;
        return new ActionReport(true, "path", new() { new OpResult(true, key, msg) });
    }

    public async Task<ActionReport> ClearGamePath(string modId, long? appid)
    {
        var (game, err) = await ResolveGame(modId, appid);
        if (game is null) return Fail("path", err!);

        _locator.ClearManual(game.Appid);
        return new ActionReport(true, "path", new() { new OpResult(true,
            AppPaths.GameKeyForAppid(game.Appid),
            $"{game.Name}: manual folder cleared — back to Steam detection") });
    }

    /// <summary>Where we currently believe one of a mod's games lives, and who said so.</summary>
    public async Task<GameStatus?> GamePath(string modId, long? appid)
    {
        var (game, _) = await ResolveGame(modId, appid);
        if (game is null) return null;
        var st = await Status(modId);
        return st?.Games.FirstOrDefault(g => g.Appid == game.Appid);
    }

    private async Task<(GameRef? game, string? error)> ResolveGame(string modId, long? appid)
    {
        var (reg, _) = await _loader.Load();
        var mod = reg.Find(modId);
        if (mod is null) return (null, $"unknown mod id '{modId}'");
        return GameFor(mod, appid);
    }

    // Which of a mod's games are we acting on? Every mod ships exactly one today, so --game
    // is only needed if that ever stops being true.
    private static (GameRef? game, string? error) GameFor(ModEntry mod, long? appid)
    {
        if (mod.Games.Count == 0) return (null, $"{mod.Id} names no games");
        if (appid is null)
            return mod.Games.Count == 1
                ? (mod.Games[0], null)
                : (null, $"{mod.Id} covers several games — pass --game <appid>");
        var g = mod.Games.FirstOrDefault(x => x.Appid == appid.Value);
        return g is null ? (null, $"appid {appid.Value} is not a listed game for {mod.Id}") : (g, null);
    }

    private List<GameTarget> ResolveTargets(ModEntry mod, long? appid, string? path, out List<OpResult> problems)
    {
        problems = new();
        if (path is not null)
        {
            var (g, gerr) = GameFor(mod, appid);
            if (g is null) { problems.Add(new OpResult(false, "", gerr!)); return new(); }

            var key = AppPaths.GameKeyForAppid(g.Appid);
            var check = GamePathCheck.Inspect(path);
            if (!check.Ok) { problems.Add(new OpResult(false, key, check.Error!)); return new(); }

            // Remember it. The install then keys by APPID like any other, which is what keeps
            // a manually-located game visible to status, config, update and uninstall.
            _locator.SetManual(g.Appid, check.Full!);
            if (check.Warning is not null)
                problems.Add(new OpResult(true, key, "warning: " + check.Warning));
            return new() { new GameTarget(key, check.Full!) };
        }

        var games = appid is null ? mod.Games : mod.Games.Where(g => g.Appid == appid.Value).ToList();
        if (appid is not null && games.Count == 0)
        {
            problems.Add(new OpResult(false, AppPaths.GameKeyForAppid(appid.Value),
                $"appid {appid.Value} is not a listed game for {mod.Id}"));
            return new();
        }
        var targets = new List<GameTarget>();
        foreach (var g in games)
        {
            var key = AppPaths.GameKeyForAppid(g.Appid);
            var found = _locator.Find(g.Appid);
            if (found is not null) { targets.Add(new GameTarget(key, found.Path)); continue; }

            var manual = _locator.ManualPath(g.Appid);
            problems.Add(new OpResult(false, key, manual is not null
                ? $"{g.Name}: the folder you chose is no longer there: {manual} — set it again with --path <game dir>"
                : $"{g.Name} (appid {g.Appid}) not found via Steam — pass --path <game dir> to install manually"));
        }
        return targets;
    }

    private static ActionReport Fail(string cmd, string msg) =>
        new(false, cmd, new() { new OpResult(false, "", msg) });
}
