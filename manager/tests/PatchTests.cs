using System.Security.Cryptography;
using Xunit;
using VrlfMods;
using VrlfMods.Patches;

namespace VrlfMods.Tests;

public class PatchTests
{
    static string Tmp() => Path.Combine(Path.GetTempPath(), "vrlf-patch-" + Guid.NewGuid().ToString("N"));

    [Fact] public void Detect_missing_and_unsupported()
    {
        var p = new BlueEstateCrosshairPatch();
        var path = Tmp();
        Assert.Equal(PatchState.Missing, p.Detect(path));
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        Assert.Equal(PatchState.UnsupportedBuild, p.Detect(path));
        File.Delete(path);
    }

    [Fact] public void Revert_restores_backup()
    {
        var p = new BlueEstateCrosshairPatch();
        var path = Tmp();
        File.WriteAllBytes(path, new byte[] { 9, 9 });
        File.WriteAllBytes(path + ".vrlf-backup", new byte[] { 1, 2, 3 });   // "original"
        var r = p.Revert(path, "k");
        Assert.True(r.Ok, r.Message);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".vrlf-backup"));
        File.Delete(path);
    }

    [Fact] public void Registry_resolves_by_name()
    {
        Assert.NotNull(PatchRegistry.Default().Get("blue-estate-nocrosshair"));
        Assert.Null(PatchRegistry.Default().Get("nope"));
    }

    // GATED golden test: only runs where a real Steam BEGame.upk is present.
    // Set VRLF_BEGAME_UPK to its path, or it uses the H: Steam default. Skips otherwise.
    [Fact] public void Apply_reproduces_community_patch_md5_on_real_upk()
    {
        var real = Environment.GetEnvironmentVariable("VRLF_BEGAME_UPK")
                   ?? @"H:\SteamLibrary\steamapps\common\Blue Estate\BEGame\COOKEDPCCONSOLE\BEGame.upk";
        if (!File.Exists(real)) return;   // skip (not fail) where the game isn't installed
        var work = Tmp();
        File.Copy(real, work, overwrite: true);
        var p = new BlueEstateCrosshairPatch();
        if (p.Detect(work) != PatchState.Off) { File.Delete(work); return; }  // already-patched install
        var applied = p.Apply(work, "k");
        Assert.True(applied.Ok, applied.Message);
        Assert.Equal(PatchState.On, p.Detect(work));
        var md5 = Convert.ToHexString(MD5.HashData(File.ReadAllBytes(work)));
        Assert.Equal("0A63FE0141F94B05186AF8613E6AB52A", md5);
        var reverted = p.Revert(work, "k");
        Assert.True(reverted.Ok, reverted.Message);
        Assert.Equal(PatchState.Off, p.Detect(work));
        File.Delete(work);
    }
}
