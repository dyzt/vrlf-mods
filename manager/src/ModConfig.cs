using VrlfMods.Patches;

namespace VrlfMods;

public record ToggleState(string Key, string Label, string? Help, bool On, bool Available, string? Note);
public record ModConfig(string ModId, string GameKey, List<ToggleState> Toggles);

public sealed class ConfigController
{
    private readonly PatchRegistry _patches;
    public ConfigController(PatchRegistry patches) => _patches = patches;

    static string Ident(ConfigToggle t) => t.Type == "patch" ? t.Patch! : t.Key!;

    public ModConfig? Read(ModEntry mod, string gameKey, string gameDir)
    {
        if (mod.Config is null) return null;
        var toggles = new List<ToggleState>();
        foreach (var t in mod.Config.Toggles)
            toggles.Add(ReadOne(mod.Config, t, gameDir));
        return new ModConfig(mod.Id, gameKey, toggles);
    }

    ToggleState ReadOne(ConfigManifest cfg, ConfigToggle t, string gameDir)
    {
        if (t.Type == "patch")
        {
            var target = Path.Combine(gameDir, t.Target!.Replace('/', Path.DirectorySeparatorChar));
            var p = _patches.Get(t.Patch!);
            if (p is null) return new ToggleState(Ident(t), t.Label, t.Help, false, false, "unknown patch");
            var st = p.Detect(target);
            var (on, avail, note) = st switch
            {
                PatchState.On => (true, true, (string?)null),
                PatchState.Off => (false, true, null),
                PatchState.UnsupportedBuild => (false, false, "Steam build only"),
                _ => (false, false, "target file not found"),
            };
            return new ToggleState(Ident(t), t.Label, t.Help, on, avail, note);
        }
        var file = Path.Combine(gameDir, cfg.File!.Replace('/', Path.DirectorySeparatorChar));
        var val = ConfigFile.ReadBool(file, cfg.Format, t.Section, t.Key!);
        var help = t.Help ?? ConfigFile.ReadHelp(file, cfg.Format, t.Section, t.Key!);
        return new ToggleState(Ident(t), t.Label, help, val ?? false, File.Exists(file),
            File.Exists(file) ? null : "config not created yet — run the game once");
    }

    public OpResult Set(ModEntry mod, string gameKey, string gameDir, string toggleKey, bool on)
    {
        if (mod.Config is null) return new OpResult(false, gameKey, "no configurable options");
        var t = mod.Config.Toggles.FirstOrDefault(x => string.Equals(Ident(x), toggleKey, StringComparison.OrdinalIgnoreCase));
        if (t is null) return new OpResult(false, gameKey, $"unknown option '{toggleKey}'");

        if (t.Type == "patch")
        {
            var target = Path.Combine(gameDir, t.Target!.Replace('/', Path.DirectorySeparatorChar));
            var p = _patches.Get(t.Patch!);
            if (p is null) return new OpResult(false, gameKey, $"unknown patch '{t.Patch}'");
            try { return on ? p.Apply(target, gameKey) : p.Revert(target, gameKey); }
            catch (Exception ex) { return new OpResult(false, gameKey, $"patch failed: {ex.Message}"); }
        }
        var file = Path.Combine(gameDir, mod.Config.File!.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(file)) return new OpResult(false, gameKey, $"config not found: {mod.Config.File}");
        try { ConfigFile.SetBool(file, mod.Config.Format, t.Section, t.Key!, on); }
        catch (Exception ex) { return new OpResult(false, gameKey, $"write failed: {ex.Message}"); }
        return new OpResult(true, gameKey, $"{t.Label} = {(on ? "on" : "off")} (applies next launch)");
    }

    // Revert every reversible patch this mod declares — used on uninstall so the game
    // returns fully stock. Returns any warnings (e.g. a missing backup) for the caller to
    // surface, rather than silently leaving a patched game file behind.
    public List<string> RevertPatches(ModEntry mod, string gameKey, string gameDir)
    {
        var warnings = new List<string>();
        if (mod.Config is null) return warnings;
        foreach (var t in mod.Config.Toggles.Where(x => x.Type == "patch"))
        {
            var p = _patches.Get(t.Patch!);
            if (p is null) continue;
            var target = Path.Combine(gameDir, t.Target!.Replace('/', Path.DirectorySeparatorChar));
            if (p.Detect(target) != PatchState.On) continue;
            OpResult r;
            try { r = p.Revert(target, gameKey); }
            catch (Exception ex) { r = new OpResult(false, gameKey, ex.Message); }
            if (!r.Ok) warnings.Add(r.Message);
        }
        return warnings;
    }
}
