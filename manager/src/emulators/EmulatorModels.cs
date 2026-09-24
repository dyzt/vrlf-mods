namespace VrlfMods;

/// <summary>How a program folder maps to its settings folder: any <see cref="Flag"/> file marks a
/// portable install, whose settings live in <see cref="Settings"/> ("" = the program folder).</summary>
public record PortableRule(List<string> Flag, string Settings = "");

public record EmulatorOption(
    string Id, string Label, string Version, string Zip, string Sha256,
    string? Requires = null, string? Short = null);

/// <param name="Processes">Exe name prefixes that mean the emulator is running.</param>
/// <param name="Exe">Globs that mark the emulator's program folder.</param>
/// <param name="Locate">Candidate settings folders for the suggestion (%APPDATA%, %LOCALAPPDATA%, %DOCUMENTS%).</param>
/// <param name="Marker">Any-of globs, relative to a settings folder, that prove it is one.</param>
public record EmulatorEntry(
    string Id, string Name,
    List<string> Processes, List<string> Exe, List<string> Locate, List<string> Marker,
    List<EmulatorOption> Options,
    PortableRule? Portable = null, string? Needs = null, string? Profile = null, string? Notes = null);

/// <summary>One setting change. Set = <see cref="Key"/> + <see cref="Value"/>; Replace = the
/// section body becomes exactly <see cref="Replace"/>'s lines.</summary>
public record EditSpec(
    string File, string Format, string? Section = null, string? Key = null, string? Value = null,
    List<string>? Replace = null)
{
    public string? Problem()
    {
        if (string.IsNullOrWhiteSpace(File)) return "an edit names no file";
        var parts = File.Replace('\\', '/').Split('/');
        if (Path.IsPathRooted(File) || File.Contains(':') || parts.Contains(".."))
            return $"{File}: must be a relative path inside the settings folder";

        bool isSet = Key is not null || Value is not null;
        bool isReplace = Replace is not null;
        if (isSet == isReplace) return $"{File}: an edit is either key + value or replace";
        if (isSet && (string.IsNullOrEmpty(Key) || Value is null)) return $"{File}: a set edit needs both key and value";

        switch (Format)
        {
            case "ini":
                if (string.IsNullOrEmpty(Section)) return $"{File}: ini edits need a section";
                break;
            case "mame":
                if (Section is not null || isReplace) return $"{File}: mame edits have no section and no replace";
                break;
            case "yaml":
                if (string.IsNullOrEmpty(Section)) return $"{File}: yaml edits need a section";
                if (isReplace) return $"{File}: yaml edits have no replace";
                if (Section.Contains(':') || (Key?.Contains(':') ?? false))
                    return $"{File}: a yaml section or key cannot contain ':'";
                break;
            default:
                return $"{File}: unknown format '{Format}'";
        }

        if (!Plain(File) || !Plain(Section) || !Plain(Key) || !Plain(Value) || (Replace?.Any(l => !Plain(l)) ?? false))
            return $"{File}: edits must be plain ASCII on one line";
        if (Key is not null && Key.Contains('=')) return $"{File}: a key cannot contain '='";
        if (Section is not null && Section.Contains(']')) return $"{File}: a section name cannot contain ']'";
        return null;
    }

    static bool Plain(string? s) => s is null || s.All(c => c == '\t' || (c >= 32 && c < 127));
}

public record PackageSettings(List<EditSpec> Edits);

public record AppliedEdit(EditSpec Edit, EditPrior Prior, bool FileCreated);

public record EmulatorReceipt(
    string EmulatorId, string OptionId, string Version, string Folder,
    List<InstalledFile> Files, List<BackupRef> Backups, List<AppliedEdit> Edits, string InstalledUtc);
