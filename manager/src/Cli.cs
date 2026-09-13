namespace VrlfMods;

public record ParsedArgs(string Command, string? Id, long? Appid, string? Path, bool Json, string? Error,
    string? Sub = null, string? Value = null, bool FlagOn = false, bool Clear = false);

public static class Cli
{
    private static readonly HashSet<string> NeedId = new() { "install", "uninstall", "status", "path" };

    public static ParsedArgs Parse(string[] args)
    {
        if (args.Length == 0) return new("menu", null, null, null, false, null);

        var cmd = args[0].ToLowerInvariant();
        string? path = null; long? appid = null; bool json = false; bool clear = false;
        var pos = new List<string>();

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--json": json = true; break;
                case "--clear": clear = true; break;
                case "--game":
                    if (i + 1 >= args.Length) return Err(cmd, "--game needs an appid");
                    if (!long.TryParse(args[++i], out var a)) return Err(cmd, $"--game '{args[i]}' is not a number");
                    appid = a; break;
                case "--path":
                    if (i + 1 >= args.Length) return Err(cmd, "--path needs a directory");
                    path = args[++i]; break;
                default:
                    if (args[i].StartsWith('-')) return Err(cmd, $"unknown option {args[i]}");
                    pos.Add(args[i]); break;
            }
        }

        var known = new[] { "list", "status", "install", "uninstall", "update", "vigembus", "virtualgun", "menu", "config", "path" };
        if (!known.Contains(cmd)) return Err(cmd, $"unknown command '{cmd}'");

        string? id = pos.Count > 0 ? pos[0] : null;
        string? sub = null, value = null; bool flagOn = false;

        if (cmd == "virtualgun" && id is not null && id is not ("install" or "uninstall" or "status"))
            return Err(cmd, "usage: virtualgun [install|uninstall|status]");

        if (cmd == "config")
        {
            if (id is null) return Err(cmd, "config needs a mod id");
            if (pos.Count > 1)
            {
                if (!string.Equals(pos[1], "set", StringComparison.OrdinalIgnoreCase))
                    return Err(cmd, $"unknown config subcommand '{pos[1]}'");
                sub = "set";
                if (pos.Count < 4) return Err(cmd, "usage: config <id> set <option> <on|off>");
                value = pos[2];
                var f = pos[3].ToLowerInvariant();
                if (f != "on" && f != "off") return Err(cmd, "config set needs on or off");
                flagOn = f == "on";
            }
        }

        if (cmd == "path")
        {
            if (id is null) return Err(cmd, "path needs a mod id");
            if (pos.Count > 2) return Err(cmd, "usage: path <id> [<game dir>] [--clear]");
            if (pos.Count == 2)
            {
                if (path is not null) return Err(cmd, "name the folder once, not twice");
                path = pos[1];
            }
            if (clear && path is not null) return Err(cmd, "path <id> --clear takes no folder");
        }
        else if (clear) return Err(cmd, "--clear is only for the path command");

        if (NeedId.Contains(cmd) && id is null) return Err(cmd, $"{cmd} needs a mod id");
        return new(cmd, id, appid, path, json, null, sub, value, flagOn, clear);
    }

    private static ParsedArgs Err(string cmd, string msg) => new(cmd, null, null, null, false, msg);
}
