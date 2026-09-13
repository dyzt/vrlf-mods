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
        Assert.Equal(8, reg.Mods.Count);      // the real embedded catalog
    }

    [Fact]
    public async Task Parses_the_virtualgun_block()
    {
        var paths = TempPaths();
        var url = RegistryLoader.RawBase + "/mods.json";
        var json = Encoding.UTF8.GetBytes("""
            { "schema": 1, "vigembus": {"repo":"nefarius/ViGEmBus","version":"v1"}, "mods": [],
              "virtualgun": {"repo":"dyzt/vrlf-virtual-gun","version":"v1.0.0",
                             "zip":"vrlf-virtual-gun-1.0.0.zip","sha256":"abc"} }
            """);
        var loader = new RegistryLoader(new FakeHttpFetcher(new() { [url] = json }), paths);
        var (reg, _) = await loader.Load();
        Assert.NotNull(reg.Virtualgun);
        Assert.Equal("dyzt/vrlf-virtual-gun", reg.Virtualgun!.Repo);
        Assert.Equal("v1.0.0", reg.Virtualgun.Version);
        Assert.Equal("vrlf-virtual-gun-1.0.0.zip", reg.Virtualgun.Zip);
        Assert.Equal("abc", reg.Virtualgun.Sha256);
    }

    [Fact]
    public async Task Registry_without_virtualgun_still_parses()
    {
        var paths = TempPaths();
        var url = RegistryLoader.RawBase + "/mods.json";
        var loader = new RegistryLoader(new FakeHttpFetcher(new() { [url] = MinimalRegistry("v9") }), paths);
        var (reg, source) = await loader.Load();
        Assert.Equal("network", source);
        Assert.Null(reg.Virtualgun);
    }
}
