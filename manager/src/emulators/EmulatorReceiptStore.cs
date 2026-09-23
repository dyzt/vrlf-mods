using System.Text.Json;

namespace VrlfMods;

/// <summary>Emulator receipts live in their own folder: the mods <see cref="ReceiptStore"/> reads
/// every file in <c>receipts\</c> as a mod receipt.</summary>
public sealed class EmulatorReceiptStore
{
    private readonly AppPaths _paths;
    public EmulatorReceiptStore(AppPaths paths) => _paths = paths;

    public void Save(EmulatorReceipt r)
    {
        Directory.CreateDirectory(_paths.EmuReceiptsDir);
        File.WriteAllText(_paths.EmuReceiptPath(r.EmulatorId, r.OptionId),
            JsonSerializer.Serialize(r, VrlfJson.Default.EmulatorReceipt));
    }

    public EmulatorReceipt? Load(string emu, string opt)
    {
        var p = _paths.EmuReceiptPath(emu, opt);
        if (!File.Exists(p)) return null;
        try { return JsonSerializer.Deserialize(File.ReadAllText(p), VrlfJson.Default.EmulatorReceipt); }
        catch { return null; }
    }

    public List<EmulatorReceipt> ForEmulator(string emu)
    {
        var list = new List<EmulatorReceipt>();
        if (!Directory.Exists(_paths.EmuReceiptsDir)) return list;
        foreach (var f in Directory.EnumerateFiles(_paths.EmuReceiptsDir, "*.json"))
        {
            try
            {
                var r = JsonSerializer.Deserialize(File.ReadAllText(f), VrlfJson.Default.EmulatorReceipt);
                if (r is not null && string.Equals(r.EmulatorId, emu, StringComparison.OrdinalIgnoreCase)) list.Add(r);
            }
            catch { /* skip a corrupt receipt */ }
        }
        return list;
    }

    public void Delete(string emu, string opt)
    {
        var p = _paths.EmuReceiptPath(emu, opt);
        if (File.Exists(p)) File.Delete(p);
    }
}
