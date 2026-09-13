using System.IO;
using System.Text;
using System.Text.Json;
using Xunit;

namespace VrlfMods.Tests;

public class RegistryLoaderTests
{
    static AppPaths TempPaths() =>
        new(Path.Combine(Path.GetTempPath(), "vrlf-test-" + Guid.NewGuid().ToString("N")));

    static byte[] MinimalRegistry(string ver) => Encoding.UTF8.GetBytes(
        $$"""{ "schema": 1, "vigembus": {"repo":"nefarius/ViGEmBus","version":"{{ver}}"}, "mods": [] }""");

    [Fact]
    public async Task Network_success_is_used_and_cached()
    {
        var paths = TempPaths();
        var url = RegistryLoader.RawBase + "/mods.json";
        var loader = new RegistryLoader(new FakeHttpFetcher(new() { [url] = MinimalRegistry("v9") }), paths);
        var (reg, source) = await loader.Load();
        Assert.Equal("network", source);
        Assert.Equal("v9", reg.Vigembus.Version);
        Assert.True(File.Exists(paths.RegistryCachePath));   // cached
    }

    [Fact]
    public async Task Falls_back_to_cache_when_offline()
    {
        var paths = TempPaths();
        Directory.CreateDirectory(paths.ModsRoot);
        await File.WriteAllBytesAsync(paths.RegistryCachePath, MinimalRegistry("cachedver"));
        var url = RegistryLoader.RawBase + "/mods.json";
        var loader = new RegistryLoader(new FakeHttpFetcher(new() { [url] = null }), paths);
        var (reg, source) = await loader.Load();
        Assert.Equal("cache", source);
        Assert.Equal("cachedver", reg.Vigembus.Version);
    }

    [Fact]
    public async Task Falls_back_to_embedded_when_offline_and_no_cache()
    {
        var paths = TempPaths();
        var url = RegistryLoader.RawBase + "/mods.json";
        var loader = new RegistryLoader(new FakeHttpFetcher(new() { [url] = null }), paths);
        var (reg, source) = await loader.Load();
        Assert.Equal("embedded", source);
        Assert.Equal(9, reg.Mods.Count);      // the real embedded catalog
    }
}
