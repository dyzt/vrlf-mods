namespace VrlfMods;

/// <summary>Where a game was found, and whether the user told us or Steam did.</summary>
public record GameDir(string Path, bool Manual);

/// <summary>
/// Answers "where does this game live?" — a manual override first, Steam's own library second.
/// </summary>
public class GameLocator
{
    private readonly SteamLocator _steam;
    private readonly GamePathStore _manual;

    public GameLocator(SteamLocator steam, GamePathStore manual)
    { _steam = steam; _manual = manual; }

    /// <summary>The override the user configured for this game, whether or not it still exists.</summary>
    public string? ManualPath(long appid) => _manual.Get(appid);

    public virtual GameDir? Find(long appid)
    {
        var manual = ManualPath(appid);
        if (manual is not null)
            // An override is authoritative. If it has gone stale we report nothing found so the
            // user is told, rather than silently falling back to a different copy of the game.
            return Directory.Exists(manual) ? new GameDir(manual, true) : null;

        var steam = _steam.FindGameDir(appid);
        return steam is null ? null : new GameDir(steam, false);
    }
}
