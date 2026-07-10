namespace VrlfMods.Patches;

public sealed class PatchRegistry
{
    private readonly Dictionary<string, IReversiblePatch> _map;
    public PatchRegistry(IEnumerable<IReversiblePatch> patches)
        => _map = patches.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
    public IReversiblePatch? Get(string name) => _map.TryGetValue(name, out var p) ? p : null;
    public static PatchRegistry Default() => new(new IReversiblePatch[] { new BlueEstateCrosshairPatch() });
}
