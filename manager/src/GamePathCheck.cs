namespace VrlfMods;

/// <summary>Verdict on a user-supplied game folder. <c>Full</c> is set whenever the input
/// could be resolved at all, so an error message can name the path the user actually meant.</summary>
public record PathCheck(bool Ok, string? Full, string? Error = null, string? Warning = null);

/// <summary>
/// Sanity-checks a manually entered game folder. Deliberately warns rather than blocks on
/// "this doesn't look like a game" — odd install layouts are legitimate and the user is the
/// one who knows where their copy lives.
/// </summary>
public static class GamePathCheck
{
    /// How many levels below the folder to look for an .exe. Bounded so a mis-pick
    /// (a whole library root, say) can't turn into a full-disk walk.
    public const int ExeProbeDepth = 3;

    public static PathCheck Inspect(string? raw)
    {
        var trimmed = (raw ?? string.Empty).Trim().Trim('"').Trim();
        if (trimmed.Length == 0) return new(false, null, "no folder given");

        string full;
        try { full = Path.GetFullPath(trimmed); }
        catch (Exception ex) { return new(false, null, $"not a usable path: {ex.Message}"); }

        if (string.Equals(full, Path.GetPathRoot(full), StringComparison.OrdinalIgnoreCase))
            return new(false, full, $"refusing a drive root ({full}) — point at the game's own folder");

        full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (File.Exists(full))
            return new(false, full, $"that's a file, not a folder: {full}");
        if (!Directory.Exists(full))
            return new(false, full, $"path not found: {full}");

        return HasExe(full, ExeProbeDepth)
            ? new(true, full)
            : new(true, full, null, $"no .exe found under {full} — is this the game folder?");
    }

    private static bool HasExe(string dir, int depth)
    {
        try
        {
            if (Directory.EnumerateFiles(dir, "*.exe").Any()) return true;
            if (depth <= 0) return false;
            foreach (var sub in Directory.EnumerateDirectories(dir))
                if (HasExe(sub, depth - 1)) return true;
        }
        catch { /* unreadable subtree — treat as nothing found rather than failing the check */ }
        return false;
    }
}
