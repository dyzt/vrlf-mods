namespace VrlfMods.Tests;

using VrlfMods;

public sealed class FakeHttpFetcher : IHttpFetcher
{
    private readonly Dictionary<string, byte[]?> _map;
    public List<string> Requested { get; } = new();
    public FakeHttpFetcher(Dictionary<string, byte[]?> map) => _map = map;
    public Task<byte[]?> TryGet(string url)
    {
        Requested.Add(url);
        return Task.FromResult(_map.TryGetValue(url, out var v) ? v : null);
    }
}

public sealed class FakeSteamPaths : VrlfMods.ISteamPaths
{
    private readonly string? _root;
    public FakeSteamPaths(string? root) => _root = root;
    public string? SteamRoot() => _root;
}
