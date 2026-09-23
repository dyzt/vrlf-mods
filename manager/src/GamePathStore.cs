using System.Text.Json;

namespace VrlfMods;

/// <summary>
/// Where the user has manually told us a game (keyed by Steam appid) or an emulator's settings
/// folder (keyed emu:&lt;id&gt;) lives.
/// Persistence only — it stores what it is given; validation is <see cref="GamePathCheck"/>'s job.
/// </summary>
public sealed class GamePathStore
{
    private readonly AppPaths _paths;
    public GamePathStore(AppPaths paths) => _paths = paths;

    public Dictionary<string, string> All()
    {
        if (!File.Exists(_paths.GamePathsPath)) return new();
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(_paths.GamePathsPath),
                       VrlfJson.Default.DictionaryStringString) ?? new();
        }
        catch { return new(); }   // a corrupt file must not take the whole tool down
    }

    public string? Get(string key) => All().TryGetValue(key, out var p) ? p : null;
    public void Set(string key, string dir) => Write(map => map[key] = dir);
    public void Clear(string key) => Write(map => map.Remove(key));

    public string? Get(long appid) => Get(appid.ToString());
    public void Set(long appid, string dir) => Set(appid.ToString(), dir);
    public void Clear(long appid) => Clear(appid.ToString());

    private void Write(Action<Dictionary<string, string>> mutate)
    {
        var map = All();
        mutate(map);
        Directory.CreateDirectory(_paths.ModsRoot);
        File.WriteAllText(_paths.GamePathsPath,
            JsonSerializer.Serialize(map, VrlfJson.Default.DictionaryStringString));
    }
}
