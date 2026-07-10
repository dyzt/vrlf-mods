namespace VrlfMods;

public static class Zip
{
    public static string NormalizeEntryName(string name) =>
        name.Replace('\\', '/').TrimStart('/');

    public static bool IsWithin(string targetDirFull, string candidateFull)
    {
        var baseDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetDirFull))
                      + Path.DirectorySeparatorChar;
        var cand = Path.GetFullPath(candidateFull);
        return cand.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase);
    }

    public static string ResolveDest(string targetDir, string entryName)
    {
        var rel = NormalizeEntryName(entryName).Replace('/', Path.DirectorySeparatorChar);
        var dest = Path.GetFullPath(Path.Combine(targetDir, rel));
        if (!IsWithin(targetDir, dest))
            throw new InvalidDataException($"zip entry escapes target: {entryName}");
        return dest;
    }
}
