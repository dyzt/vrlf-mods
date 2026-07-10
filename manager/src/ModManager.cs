using System.IO;
using VrlfMods.Patches;

namespace VrlfMods;

public record GameStatus(long Appid, string Name, bool Detected, string? Path, bool Installed, string? InstalledVersion);
public record ModStatus(string Id, string Name, string Version, string? InstalledVersion, List<GameStatus> Games);
public record ListReport(string RegistrySource, List<ModStatus> Mods);
public record ActionReport(bool Ok, string Command, List<OpResult> Results);

public sealed class ModManager
{
    private readonly RegistryLoader _loader;
    private readonly SteamLocator _steam;
    private readonly Installer _installer;
    private readonly ReceiptStore _receipts;
    private readonly Vigem _vigem;
    private readonly ConfigController _config;

    public ModManager(RegistryLoader loader, SteamLocator steam, Installer installer,
                      ReceiptStore receipts, Vigem vigem, PatchRegistry? patches = null)
    { _loader = loader; _steam = steam; _installer = installer; _receipts = receipts; _vigem = vigem;
      _config = new ConfigController(patches ?? PatchRegistry.Default()); }

    public async Task<ListReport> List()
    {
        var (reg, source) = await _loader.Load();
        return new ListReport(source, reg.Mods.Select(StatusFor).ToList());
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
            var dir = _steam.FindGameDir(g.Appid);
            var rcpt = _receipts.Load(mod.Id, key);
            if (rcpt is not null) installedVersion = rcpt.Version;
            games.Add(new GameStatus(g.Appid, g.Name, dir is not null, dir,
                rcpt is not null, rcpt?.Version));
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
        // Include any custom-path receipts for this mod too.
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

    private List<GameTarget> ResolveTargets(ModEntry mod, long? appid, string? path, out List<OpResult> problems)
    {
        problems = new();
        if (path is not null)
        {
            var full = System.IO.Path.GetFullPath(path);
            if (!Directory.Exists(full))
            {
                problems.Add(new OpResult(false, AppPaths.GameKeyForPath(full), $"path not found: {full}"));
                return new();
            }
            return new() { new GameTarget(AppPaths.GameKeyForPath(full), full) };
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
            var dir = _steam.FindGameDir(g.Appid);
            if (dir is null)
                problems.Add(new OpResult(false, AppPaths.GameKeyForAppid(g.Appid),
                    $"{g.Name} (appid {g.Appid}) not found via Steam — pass --path <game dir> to install manually"));
            else
                targets.Add(new GameTarget(AppPaths.GameKeyForAppid(g.Appid), dir));
        }
        return targets;
    }

    private static ActionReport Fail(string cmd, string msg) =>
        new(false, cmd, new() { new OpResult(false, "", msg) });
}
