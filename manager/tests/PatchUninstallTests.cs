using System.Text;
using Xunit;
using VrlfMods.Patches;

namespace VrlfMods.Tests;

public class PatchUninstallTests
{
    static AppPaths TempPaths() => new(Path.Combine(Path.GetTempPath(), "vrlf-pu-" + Guid.NewGuid().ToString("N")));

    // A mod whose only config option is a "fake-patch" toggle, marked installed via a receipt.
    static (ModManager mm, ReceiptStore store) Build(AppPaths paths, FakePatch patch)
    {
        var game = Path.Combine(Path.GetTempPath(), "pugame-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(game);
        var regJson = Encoding.UTF8.GetBytes(
            """{ "schema":1, "vigembus":{"repo":"r","version":"v"}, "mods":[ {"id":"demo","name":"Demo","version":"1.0","zip":"z","sha256":"h","games":[{"appid":1,"name":"G"}],"requiresVigembusForCoop":false,"notes":null,"config":{"file":"x.cfg","format":"kv","toggles":[{"type":"patch","patch":"fake-patch","label":"Hide","target":"a/b.bin"}]}} ] }""");
        var http = new FakeHttpFetcher(new() { [RegistryLoader.RawBase + "/mods.json"] = regJson });
        var store = new ReceiptStore(paths);
        store.Save(new Receipt("demo", "1.0", "1", game, new(), new(), "t"));   // installed
        var mm = new ModManager(new RegistryLoader(http, paths), new GameLocator(new SteamLocatorStub(1, game), new GamePathStore(paths)),
            new Installer(http, paths, store), store,
            new Vigem(new FakeServiceDetector(true), http, new FakeLauncher(), paths),
            new PatchRegistry(new IReversiblePatch[] { patch }));
        return (mm, store);
    }

    [Fact] public async Task Uninstall_reverts_an_active_patch()
    {
        var paths = TempPaths();
        var patch = new FakePatch(PatchState.On);
        var (mm, _) = Build(paths, patch);
        var act = await mm.Uninstall("demo", null);
        Assert.True(act.Ok, act.Results[0].Message);
        Assert.True(patch.Reverts >= 1);          // patch reverted so the game returns stock
    }

    [Fact] public async Task Uninstall_surfaces_a_revert_failure_as_a_warning()
    {
        var paths = TempPaths();
        var patch = new FakePatch(PatchState.On, revertOk: false);
        var (mm, _) = Build(paths, patch);
        var act = await mm.Uninstall("demo", null);
        Assert.Contains("warning", act.Results[0].Message);   // not silently left patched
    }

    [Fact] public async Task Uninstall_does_not_revert_when_patch_is_off()
    {
        var paths = TempPaths();
        var patch = new FakePatch(PatchState.Off);
        var (mm, _) = Build(paths, patch);
        await mm.Uninstall("demo", null);
        Assert.Equal(0, patch.Reverts);
    }
}
