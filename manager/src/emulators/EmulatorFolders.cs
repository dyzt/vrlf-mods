using System.Text.RegularExpressions;

namespace VrlfMods;

/// <summary>Resolves the folder tokens the registry's <c>locate</c> entries use. Documents goes
/// through the shell, not <c>%USERPROFILE%</c>, because OneDrive can move it.</summary>
public interface IKnownFolders { string? Resolve(string token); }

public sealed class SystemKnownFolders : IKnownFolders
{
    public string? Resolve(string token) => token.ToUpperInvariant() switch
    {
        "APPDATA" => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LOCALAPPDATA" => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DOCUMENTS" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        _ => null,
    };
}

/// <summary>Verdict on a folder the user typed. <c>Folder</c> is the settings folder to use when Ok.</summary>
public record FolderCheck(bool Ok, string? Folder, string? Error = null);

/// <summary>
/// Which settings folder an emulator's options install into. The user always chooses; a found
/// main install is only ever a suggestion and is never stored until they confirm it.
/// </summary>
public sealed class EmulatorFolders
{
    private readonly GamePathStore _store;
    private readonly IKnownFolders _known;

    public EmulatorFolders(GamePathStore store, IKnownFolders known) { _store = store; _known = known; }

    static string StoreKey(EmulatorEntry e) => "emu:" + e.Id;

    /// <summary>"%DOCUMENTS%/PCSX2" to a full path; null when the token is unknown.</summary>
    public string? Expand(string pattern)
    {
        var m = Regex.Match(pattern, @"^%(?<t>[A-Za-z]+)%(?<rest>.*)$");
        if (!m.Success) return Path.GetFullPath(pattern);
        var root = _known.Resolve(m.Groups["t"].Value);
        if (string.IsNullOrEmpty(root)) return null;
        var rest = m.Groups["rest"].Value.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(root, rest));
    }

    public static bool HasMarker(EmulatorEntry e, string folder) => e.Marker.Any(m => Matches(folder, m));

    /// A glob like "inis/PCSX2.ini" or "mame*.exe", relative to the folder.
    static bool Matches(string folder, string pattern)
    {
        var rel = pattern.Replace('/', Path.DirectorySeparatorChar);
        var dir = Path.Combine(folder, Path.GetDirectoryName(rel) ?? "");
        try { return Directory.Exists(dir) && Directory.EnumerateFiles(dir, Path.GetFileName(rel)).Any(); }
        catch { return false; }
    }

    public string? Suggest(EmulatorEntry e) =>
        e.Locate.Select(Expand).FirstOrDefault(p => p is not null && Directory.Exists(p) && HasMarker(e, p));

    public string? Chosen(EmulatorEntry e) => _store.Get(StoreKey(e));
    public void Choose(EmulatorEntry e, string folder) => _store.Set(StoreKey(e), folder);
    public void Forget(EmulatorEntry e) => _store.Clear(StoreKey(e));

    /// <summary>
    /// A typed or pasted folder: a settings folder is used as is; a portable program folder maps
    /// to its settings folder; a shared (non-portable) program folder is refused with where its
    /// settings really are.
    /// </summary>
    public FolderCheck Resolve(EmulatorEntry e, string? raw)
    {
        var trimmed = (raw ?? "").Trim().Trim('"').Trim();
        if (trimmed.Length == 0) return new(false, null, "no folder given");
        string full;
        try { full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(trimmed)); }
        catch (Exception ex) { return new(false, null, $"not a usable path: {ex.Message}"); }
        if (!Directory.Exists(full)) return new(false, full, $"folder not found: {full}");

        if (HasMarker(e, full)) return new(true, full);

        if (e.Portable is { } port && port.Flag.Any(f => File.Exists(Path.Combine(full, f))))
        {
            var settings = port.Settings.Length == 0 ? full : Path.Combine(full, port.Settings);
            return Directory.Exists(settings) && HasMarker(e, settings)
                ? new(true, settings)
                : new(false, full, $"Not a {e.Name} settings folder yet. Run {e.Name} once, then choose its folder.");
        }

        if (e.Exe.Any(x => Matches(full, x)))
        {
            var home = e.Locate.Select(Expand).FirstOrDefault(p => p is not null);
            return new(false, full, home is null
                ? $"This {e.Name} is not portable, so its settings live elsewhere. For a separate lightgun setup, use a portable {e.Name}."
                : $"This {e.Name} keeps its settings in {home}, which your other {e.Name} copies share. For a separate lightgun setup, use a portable {e.Name}.");
        }

        return new(false, full, $"Not a {e.Name} settings folder. Run {e.Name} once, then choose its folder.");
    }
}
