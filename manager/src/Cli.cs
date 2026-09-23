namespace VrlfMods;

public record ParsedArgs(string Command, string? Id, long? Appid, string? Path, bool Json, string? Error,
    string? Sub = null, string? Value = null, bool FlagOn = false, bool Clear = false);

public static class Cli
{
    private static readonly HashSet<string> NeedId = new() { "install", "uninstall", "status", "path" };

    private static readonly HashSet<string> EmuSubs =
        new() { "list", "status", "path", "install", "uninstall", "update", "reapply" };

    private const string EmuUsage =
        "usage: emulator list | status <id> | path <id> [<dir>|--clear] | install <id> [<option>] | uninstall <id> [<option>] | update <id> | reapply <id>";

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

        var known = new[] { "list", "status", "install", "uninstall", "update", "vigembus", "virtualgun", "menu", "config", "path", "emulator" };
        if (!known.Contains(cmd)) return Err(cmd, $"unknown command '{cmd}'");

        string? id = pos.Count > 0 ? pos[0] : null;
        string? sub = null, value = null; bool flagOn = false;

        if (cmd == "emulator")
        {
            if (appid is not null || path is not null)
                return Err(cmd, "--game and --path are not used by emulator commands");
            if (pos.Count == 0) return Err(cmd, EmuUsage);
            var esub = pos[0].ToLowerInvariant();
            if (!EmuSubs.Contains(esub)) return Err(cmd, $"unknown emulator subcommand '{pos[0]}'");
            if (pos.Count > 3) return Err(cmd, EmuUsage);
            string? eid = pos.Count > 1 ? pos[1] : null;
            string? arg = pos.Count > 2 ? pos[2] : null;
            if (esub == "list") { if (eid is not null) return Err(cmd, "emulator list takes no id"); }
            else if (eid is null) return Err(cmd, $"emulator {esub} needs an emulator id");
            if (arg is not null && esub is not ("path" or "install" or "uninstall")) return Err(cmd, EmuUsage);
            if (clear && esub != "path") return Err(cmd, "--clear is only for emulator path");
            if (clear && arg is not null) return Err(cmd, "emulator path <id> --clear takes no folder");
            return new(cmd, eid, null, esub == "path" ? arg : null, json, null,
                Sub: esub, Value: esub == "path" ? null : arg, Clear: clear);
        }

        if (cmd == "virtualgun" && id is not null)
        {
            var lowered = id.ToLowerInvariant();
            if (lowered is not ("install" or "uninstall" or "status"))
                return Err(cmd, "usage: virtualgun [install|uninstall|status]");
            id = lowered;
        }

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
