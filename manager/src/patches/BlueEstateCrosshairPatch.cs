using System.Security.Cryptography;

namespace VrlfMods.Patches;

public sealed class BlueEstateCrosshairPatch : IReversiblePatch
{
    public string Name => "blue-estate-nocrosshair";

    const string MD5_ORIGINAL = "FE76FB4294DFB2617D724CE7DD94BE70";
    const string MD5_PATCHED  = "0A63FE0141F94B05186AF8613E6AB52A";
    const string BackupSuffix = ".vrlf-backup";

    static readonly (int off, byte[] repl)[] Runs =
    {
        (0x03766EC, Convert.FromHexString("0000")),
        (0x037673C, Convert.FromHexString("01552b00")),
        (0x0376741, Convert.FromHexString("01552b000001552b00000b0b0b0b0b0b0b0b0b0b0b0b0b")),
    };

    static string Md5(byte[] b) => Convert.ToHexString(MD5.HashData(b));

    public PatchState Detect(string targetPath)
    {
        if (!File.Exists(targetPath)) return PatchState.Missing;
        var h = Md5(File.ReadAllBytes(targetPath));
        if (h == MD5_PATCHED) return PatchState.On;
        if (h == MD5_ORIGINAL) return PatchState.Off;
        return PatchState.UnsupportedBuild;
    }

    public OpResult Apply(string targetPath, string gameKey)
    {
        if (!File.Exists(targetPath)) return new OpResult(false, gameKey, $"not found: {targetPath}");
        var data = File.ReadAllBytes(targetPath);
        var h = Md5(data);
        if (h == MD5_PATCHED) return new OpResult(true, gameKey, "crosshair already hidden");
        if (h != MD5_ORIGINAL)
            return new OpResult(false, gameKey,
                $"unexpected BEGame.upk (MD5 {h}); this patch supports the Steam build only. " +
                "Steam → Verify integrity of game files restores the original.");

        var img = Ue3Package.Decompress(data);
        foreach (var (off, repl) in Runs) Array.Copy(repl, 0, img, off, repl.Length);

        var outHash = Md5(img);
        if (outHash != MD5_PATCHED)
            return new OpResult(false, gameKey, $"patch produced unexpected output ({outHash}); nothing written");

        var backup = targetPath + BackupSuffix;
        if (!File.Exists(backup)) File.Move(targetPath, backup);   // keep the original
        File.WriteAllBytes(targetPath, img);
        return new OpResult(true, gameKey, "crosshair hidden (BEGame.upk patched; original kept as .vrlf-backup)");
    }

    public OpResult Revert(string targetPath, string gameKey)
    {
        var backup = targetPath + BackupSuffix;
        if (File.Exists(backup))
        {
            File.Move(backup, targetPath, overwrite: true);
            return new OpResult(true, gameKey, "crosshair restored (original BEGame.upk)");
        }
        if (Detect(targetPath) == PatchState.Off) return new OpResult(true, gameKey, "already original");
        return new OpResult(false, gameKey,
            "no .vrlf-backup found; restore via Steam → Verify integrity of game files");
    }
}
