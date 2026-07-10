using System.Linq;
using System.Text.Json;

namespace VrlfMods;

public record InstalledFile(string RelPath, string Sha256);
public record BackupRef(string RelPath);
public record Receipt(
    string ModId, string Version, string GameKey, string GamePath,
    List<InstalledFile> Files, List<BackupRef> Backups, string InstalledUtc)
{
    // List<T> has no value equality, so the compiler-synthesized record Equals()
    // would compare Files/Backups by reference — always false across two separate
    // deserializations (e.g. Load() vs. All()). Compare by sequence instead so two
    // Receipts loaded from the same JSON are considered equal.
    public virtual bool Equals(Receipt? other) =>
        other is not null &&
        ModId == other.ModId && Version == other.Version && GameKey == other.GameKey &&
        GamePath == other.GamePath && InstalledUtc == other.InstalledUtc &&
        Files.SequenceEqual(other.Files) && Backups.SequenceEqual(other.Backups);

    public override int GetHashCode() =>
        HashCode.Combine(ModId, Version, GameKey, GamePath, InstalledUtc);
}

public sealed class ReceiptStore
{
    private readonly AppPaths _paths;
    public ReceiptStore(AppPaths paths) => _paths = paths;

    public void Save(Receipt r)
    {
        Directory.CreateDirectory(_paths.ReceiptsDir);
        var json = JsonSerializer.Serialize(r, VrlfJson.Default.Receipt);
        File.WriteAllText(_paths.ReceiptPath(r.ModId, r.GameKey), json);
    }

    public Receipt? Load(string modId, string gameKey)
    {
        var p = _paths.ReceiptPath(modId, gameKey);
        if (!File.Exists(p)) return null;
        try { return JsonSerializer.Deserialize(File.ReadAllText(p), VrlfJson.Default.Receipt); }
        catch { return null; }
    }

    public List<Receipt> All()
    {
        var list = new List<Receipt>();
        if (!Directory.Exists(_paths.ReceiptsDir)) return list;
        foreach (var f in Directory.EnumerateFiles(_paths.ReceiptsDir, "*.json"))
        {
            try
            {
                var r = JsonSerializer.Deserialize(File.ReadAllText(f), VrlfJson.Default.Receipt);
                if (r is not null) list.Add(r);
            }
            catch { /* skip corrupt receipt */ }
        }
        return list;
    }

    public void Delete(string modId, string gameKey)
    {
        var p = _paths.ReceiptPath(modId, gameKey);
        if (File.Exists(p)) File.Delete(p);
    }
}
