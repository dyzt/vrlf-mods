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

public sealed class FakeElevatedRunner : VrlfMods.IElevatedRunner
{
    private readonly int _exitCode;
    public List<(string Exe, string Args)> Runs { get; } = new();
    public Exception? Throws { get; set; }
    public FakeElevatedRunner(int exitCode) => _exitCode = exitCode;
    public int RunElevated(string exe, string args)
    {
        if (Throws is not null) throw Throws;
        Runs.Add((exe, args));
        return _exitCode;
    }
}

public sealed class FakeVirtualGunState : VrlfMods.IVirtualGunState
{
    public string? Version { get; set; }
    public string? Dir { get; set; }
    public string? InstalledVersion() => Version;
    public string? InstallDir() => Dir;
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

public sealed class FakePatch : VrlfMods.Patches.IReversiblePatch
{
    private readonly VrlfMods.Patches.PatchState _state;
    private readonly bool _revertOk;
    public int Reverts { get; private set; }
    public FakePatch(VrlfMods.Patches.PatchState state, bool revertOk = true, string name = "fake-patch")
    { _state = state; _revertOk = revertOk; Name = name; }
    public string Name { get; }
    public VrlfMods.Patches.PatchState Detect(string p) => _state;
    public VrlfMods.OpResult Apply(string p, string k) => new(true, k, "applied");
    public VrlfMods.OpResult Revert(string p, string k)
    { Reverts++; return new(_revertOk, k, _revertOk ? "reverted" : "no .vrlf-backup found"); }
}
