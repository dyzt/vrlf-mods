using System.Text.RegularExpressions;

namespace VrlfMods;

/// <summary>
/// Edits the two-level YAML RPCS3 writes: a top-level <c>Section:</c> line and its
/// <c>  key: value</c> children. A section runs to the next line that starts in column 0, keys
/// match only at the children's indent (deeper blocks are skipped), and values are written as
/// plain scalars. Names match case-insensitively, and undo follows <see cref="IniEditor"/>'s rules.
/// </summary>
public static class YamlEditor
{
    const string DefaultIndent = "  ";

    static bool Starts(string text) => text.Length > 0 && !char.IsWhiteSpace(text[0]) && text[0] != '#';

    static int FindHeader(SettingsText t, string section)
    {
        var rx = new Regex($@"^{Regex.Escape(section)}:\s*$", RegexOptions.IgnoreCase);
        for (int i = 0; i < t.Lines.Count; i++)
            if (rx.IsMatch(t.Lines[i].Text)) return i;
        return -1;
    }

    /// One past the section's last line: the next column-0 line, or the end of the file.
    static int SectionEnd(SettingsText t, int header)
    {
        for (int i = header + 1; i < t.Lines.Count; i++)
            if (Starts(t.Lines[i].Text)) return i;
        return t.Lines.Count;
    }

    /// One past the section's last non-blank line.
    static int BodyEnd(SettingsText t, int header)
    {
        int end = SectionEnd(t, header);
        while (end > header + 1 && t.Lines[end - 1].Text.Trim().Length == 0) end--;
        return end;
    }

    /// The indent of the section's first child, the level its keys live at.
    static string ChildIndent(SettingsText t, int header)
    {
        int end = SectionEnd(t, header);
        for (int i = header + 1; i < end; i++)
        {
            var text = t.Lines[i].Text;
            if (SettingsText.IsBlankOrComment(text)) continue;
            return text[..(text.Length - text.TrimStart().Length)];
        }
        return DefaultIndent;
    }

    static Regex KeyLine(string indent, string key) =>
        new($@"^(?<pre>{Regex.Escape(indent)}{Regex.Escape(key)}:)(?<gap>[ \t]*)(?<val>.*)$", RegexOptions.IgnoreCase);

    static string With(Match m, string value) =>
        m.Groups["pre"].Value + (m.Groups["gap"].Length > 0 ? m.Groups["gap"].Value : " ") + value;

    static List<int> KeyLines(SettingsText t, int header, Regex rx)
    {
        var found = new List<int>();
        int end = SectionEnd(t, header);
        for (int i = header + 1; i < end; i++)
            if (rx.IsMatch(t.Lines[i].Text)) found.Add(i);
        return found;
    }

    public static string? Get(SettingsText t, string section, string key)
    {
        int h = FindHeader(t, section);
        if (h < 0) return null;
        var rx = KeyLine(ChildIndent(t, h), key);
        var idx = KeyLines(t, h, rx);
        return idx.Count == 0 ? null : rx.Match(t.Lines[idx[0]].Text).Groups["val"].Value.Trim();
    }

    public static EditPrior Set(SettingsText t, string section, string key, string value)
    {
        int h = FindHeader(t, section);
        if (h < 0)
        {
            bool fixedEol = t.Append(new[] { $"{section}:", $"{DefaultIndent}{key}: {value}" });
            return new EditPrior(SectionCreated: true, FinalEolAdded: fixedEol);
        }
        var indent = ChildIndent(t, h);
        var rx = KeyLine(indent, key);
        var idx = KeyLines(t, h, rx);
        if (idx.Count == 0) return new EditPrior(FinalEolAdded: Insert(t, h, $"{indent}{key}: {value}"));

        var prior = new List<string>();
        foreach (var i in idx)
        {
            prior.Add(t.Lines[i].Text);
            t.Lines[i] = t.Lines[i] with { Text = With(rx.Match(t.Lines[i].Text), value) };
        }
        return new EditPrior(Lines: prior);
    }

    public static void RevertSet(SettingsText t, string section, string key, EditPrior prior)
    {
        int h = FindHeader(t, section);
        if (prior.Lines is null)
        {
            if (h < 0) return;
            foreach (var i in KeyLines(t, h, KeyLine(ChildIndent(t, h), key)).OrderByDescending(i => i))
                t.Lines.RemoveAt(i);
            if (prior.SectionCreated && SectionIsEmpty(t, h)) t.Lines.RemoveRange(h, SectionEnd(t, h) - h);
            if (prior.FinalEolAdded) t.UndoFinalEol();
            return;
        }
        if (h < 0) { t.Append(new[] { $"{section}:", prior.Lines[^1] }); return; }

        var rx = KeyLine(ChildIndent(t, h), key);
        var idx = KeyLines(t, h, rx);
        if (idx.Count == prior.Lines.Count)
        {
            for (int n = 0; n < idx.Count; n++) t.Lines[idx[n]] = t.Lines[idx[n]] with { Text = prior.Lines[n] };
            return;
        }
        if (idx.Count == 0) { Insert(t, h, prior.Lines[^1]); return; }

        // Rewritten since, with a different number of copies: give every copy the value that was in force.
        var val = rx.Match(prior.Lines[^1]).Groups["val"].Value;
        foreach (var i in idx) t.Lines[i] = t.Lines[i] with { Text = With(rx.Match(t.Lines[i].Text), val) };
    }

    /// <summary>Inserts a line after the section's last non-blank line. Returns true when that
    /// line was the file's last and had no ending, so it was given one.</summary>
    static bool Insert(SettingsText t, int header, string text)
    {
        var eol = t.Eol;
        int at = BodyEnd(t, header);
        bool fixedEol = false;
        if (at == t.Lines.Count && t.Lines[at - 1].Eol.Length == 0)
        {
            t.Lines[at - 1] = t.Lines[at - 1] with { Eol = eol };
            fixedEol = true;
        }
        t.Lines.Insert(at, new SettingsLine(text, eol));
        return fixedEol;
    }

    static bool SectionIsEmpty(SettingsText t, int header)
    {
        int end = SectionEnd(t, header);
        for (int i = header + 1; i < end; i++)
            if (t.Lines[i].Text.Trim().Length > 0) return false;
        return true;
    }
}
