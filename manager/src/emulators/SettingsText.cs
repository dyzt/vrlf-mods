using System.Text;

namespace VrlfMods;

/// <summary>One line of a settings file and the ending it had.</summary>
public sealed record SettingsLine(string Text, string Eol);

/// <summary>
/// A settings file held as lines. Each line keeps its own ending, a UTF-8 BOM survives, and the
/// bytes are read and written as Latin-1, so an edit that is later undone leaves the file
/// byte-identical whatever its encoding.
/// </summary>
public sealed class SettingsText
{
    public List<SettingsLine> Lines { get; }
    public bool Bom { get; }

    public SettingsText(List<SettingsLine> lines, bool bom) { Lines = lines; Bom = bom; }

    public static SettingsText Parse(string text)
    {
        bool bom = text.Length > 0 && text[0] == '\uFEFF';
        if (bom) text = text[1..];
        var lines = new List<SettingsLine>();
        int i = 0;
        while (i < text.Length)
        {
            int nl = text.IndexOf('\n', i);
            if (nl < 0) { lines.Add(new SettingsLine(text[i..], "")); break; }
            var seg = text[i..nl];
            lines.Add(seg.EndsWith('\r') ? new SettingsLine(seg[..^1], "\r\n") : new SettingsLine(seg, "\n"));
            i = nl + 1;
        }
        return new SettingsText(lines, bom);
    }

    /// <summary>A missing file loads as empty.</summary>
    public static SettingsText Load(string path)
    {
        if (!File.Exists(path)) return new SettingsText(new List<SettingsLine>(), false);
        var bytes = File.ReadAllBytes(path);
        bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        int skip = bom ? 3 : 0;
        var parsed = Parse(Encoding.Latin1.GetString(bytes, skip, bytes.Length - skip));
        return new SettingsText(parsed.Lines, bom);
    }

    public string Render()
    {
        var sb = new StringBuilder();
        foreach (var l in Lines) { sb.Append(l.Text); sb.Append(l.Eol); }
        return sb.ToString();
    }

    public byte[] ToBytes()
    {
        var body = Encoding.Latin1.GetBytes(Render());
        if (!Bom) return body;
        var all = new byte[body.Length + 3];
        all[0] = 0xEF; all[1] = 0xBB; all[2] = 0xBF;
        body.CopyTo(all, 3);
        return all;
    }

    /// <summary>Writes via a temp file then renames over the target, so a failed write never
    /// leaves the settings file half-written.</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".vrlf-tmp";
        try
        {
            File.WriteAllBytes(tmp, ToBytes());
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch { /* best effort */ }
            throw;
        }
    }

    /// <summary>The ending new lines get: the file's most common one, CRLF for an empty file
    /// (every emulator here is a Windows program).</summary>
    public string Eol
    {
        get
        {
            int crlf = Lines.Count(l => l.Eol == "\r\n"), lf = Lines.Count(l => l.Eol == "\n");
            return lf > crlf ? "\n" : "\r\n";
        }
    }

    /// <summary>True when nothing but blank lines and comments is left.</summary>
    public bool IsBlank => Lines.All(l => IsBlankOrComment(l.Text));

    public static bool IsBlankOrComment(string text)
    {
        var t = text.TrimStart();
        return t.Length == 0 || t[0] == '#' || t[0] == ';';
    }

    /// <summary>Appends lines at the end. A last line with no ending is given one first; the
    /// return says so, so the undo can take it away again.</summary>
    internal bool Append(IEnumerable<string> texts)
    {
        var eol = Eol;
        bool fixedEol = false;
        if (Lines.Count > 0 && Lines[^1].Eol.Length == 0)
        {
            Lines[^1] = Lines[^1] with { Eol = eol };
            fixedEol = true;
        }
        foreach (var t in texts) Lines.Add(new SettingsLine(t, eol));
        return fixedEol;
    }

    internal void UndoFinalEol()
    {
        if (Lines.Count > 0) Lines[^1] = Lines[^1] with { Eol = "" };
    }
}

/// <summary>What one edit replaced, enough to undo it exactly.</summary>
/// <param name="Lines">Set: the original text of every line it rewrote; null when the key was absent.</param>
/// <param name="Section">Replace: the section body it replaced; null when the section was absent.</param>
/// <param name="SectionCreated">Set: it created the section it wrote into.</param>
/// <param name="FinalEolAdded">The file's last line had no ending and was given one.</param>
public sealed record EditPrior(
    List<string>? Lines = null,
    List<SettingsLine>? Section = null,
    bool SectionCreated = false,
    bool FinalEolAdded = false);
