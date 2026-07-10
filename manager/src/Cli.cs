namespace VrlfMods;

public record ParsedArgs(string Command, string? Id, long? Appid, string? Path, bool Json, string? Error);

public static class Cli
{
    private static readonly HashSet<string> NeedId = new() { "install", "uninstall", "status" };

    public static ParsedArgs Parse(string[] args)
    {
        if (args.Length == 0) return new("menu", null, null, null, false, null);

        var cmd = args[0].ToLowerInvariant();
        string? id = null, path = null; long? appid = null; bool json = false;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--json": json = true; break;
                case "--game":
                    if (i + 1 >= args.Length) return Err(cmd, "--game needs an appid");
                    if (!long.TryParse(args[++i], out var a)) return Err(cmd, $"--game '{args[i]}' is not a number");
                    appid = a; break;
                case "--path":
                    if (i + 1 >= args.Length) return Err(cmd, "--path needs a directory");
                    path = args[++i]; break;
                default:
                    if (args[i].StartsWith('-')) return Err(cmd, $"unknown option {args[i]}");
                    id ??= args[i]; break;
            }
        }

        var known = new[] { "list", "status", "install", "uninstall", "update", "vigembus", "menu" };
        if (!known.Contains(cmd)) return Err(cmd, $"unknown command '{cmd}'");
        if (NeedId.Contains(cmd) && id is null) return Err(cmd, $"{cmd} needs a mod id");
        return new(cmd, id, appid, path, json, null);
    }

    private static ParsedArgs Err(string cmd, string msg) => new(cmd, null, null, null, false, msg);
}
