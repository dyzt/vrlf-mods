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

public sealed class FakeServiceDetector : VrlfMods.IServiceDetector
{
    private readonly bool _exists;
    public FakeServiceDetector(bool exists) => _exists = exists;
    public bool ServiceExists(string name) => _exists;
}

public sealed class FakeLauncher : VrlfMods.IProcessLauncher
{
    public string? Launched { get; private set; }
    public void Launch(string path) => Launched = path;
}

public sealed class AnyUrlFetcher : VrlfMods.IHttpFetcher
{
    private readonly byte[]? _bytes;
    public AnyUrlFetcher(byte[]? bytes) => _bytes = bytes;
    public Task<byte[]?> TryGet(string url) => Task.FromResult(_bytes);
}

public sealed class SteamLocatorStub : VrlfMods.SteamLocator
{
    private readonly long _appid; private readonly string _dir;
    public SteamLocatorStub(long appid, string dir) : base(new FakeSteamPaths(null))
    { _appid = appid; _dir = dir; }
    public override string? FindGameDir(long appid) => appid == _appid ? _dir : null;
}
