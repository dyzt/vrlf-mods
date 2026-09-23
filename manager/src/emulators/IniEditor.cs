using System.Text.RegularExpressions;

namespace VrlfMods;

/// <summary>
/// Edits <c>[section]</c> / <c>key = value</c> files (Dolphin, PCSX2, DuckStation) in place and
/// records what each edit replaced. Names match case-insensitively; the first header with a
/// name is the section. Trailing blank lines of a section are separators and never move.
/// </summary>
public static class IniEditor
{
    static readonly Regex Header = new(@"^\s*\[(?<name>[^\]]*)\]\s*$");

    static Regex KeyLine(string key) =>
        new($@"^(?<pre>\s*{Regex.Escape(key)}\s*=\s*)(?<val>.*)$", RegexOptions.IgnoreCase);

    static int FindHeader(SettingsText t, string section)
    {
        for (int i = 0; i < t.Lines.Count; i++)
        {
            var m = Header.Match(t.Lines[i].Text);
            if (m.Success && string.Equals(m.Groups["name"].Value.Trim(), section, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    /// One past the section's last line: the next header, or the end of the file.
    static int SectionEnd(SettingsText t, int header)
    {
        for (int i = header + 1; i < t.Lines.Count; i++)
            if (Header.IsMatch(t.Lines[i].Text)) return i;
        return t.Lines.Count;
    }

    /// One past the section's last non-blank line.
    static int BodyEnd(SettingsText t, int header)
    {
        int end = SectionEnd(t, header);
        while (end > header + 1 && t.Lines[end - 1].Text.Trim().Length == 0) end--;
        return end;
    }

    static List<int> KeyLines(SettingsText t, int header, string key)
    {
        var rx = KeyLine(key);
        var found = new List<int>();
        int end = SectionEnd(t, header);
        for (int i = header + 1; i < end; i++)
            if (!SettingsText.IsBlankOrComment(t.Lines[i].Text) && rx.IsMatch(t.Lines[i].Text))
                found.Add(i);
        return found;
    }

    public static string? Get(SettingsText t, string section, string key)
    {
        int h = FindHeader(t, section);
        if (h < 0) return null;
        var idx = KeyLines(t, h, key);
        return idx.Count == 0 ? null : KeyLine(key).Match(t.Lines[idx[0]].Text).Groups["val"].Value.Trim();
    }

    /// <summary>The section's lines up to its last non-blank one; null when absent.</summary>
    public static List<string>? SectionBody(SettingsText t, string section)
    {
        int h = FindHeader(t, section);
        if (h < 0) return null;
        return t.Lines.GetRange(h + 1, BodyEnd(t, h) - h - 1).Select(l => l.Text).ToList();
    }

    public static EditPrior Set(SettingsText t, string section, string key, string value)
    {
        int h = FindHeader(t, section);
        if (h < 0)
        {
            bool fixedEol = t.Append(new[] { $"[{section}]", $"{key} = {value}" });
            return new EditPrior(SectionCreated: true, FinalEolAdded: fixedEol);
        }
        var idx = KeyLines(t, h, key);
        if (idx.Count == 0) return new EditPrior(FinalEolAdded: Insert(t, h, $"{key} = {value}"));

        var rx = KeyLine(key);
        var prior = new List<string>();
        foreach (var i in idx)
        {
            prior.Add(t.Lines[i].Text);
            t.Lines[i] = t.Lines[i] with { Text = rx.Match(t.Lines[i].Text).Groups["pre"].Value + value };
        }
        return new EditPrior(Lines: prior);
    }

    public static void RevertSet(SettingsText t, string section, string key, EditPrior prior)
    {
        int h = FindHeader(t, section);
        if (prior.Lines is null)
        {
            if (h < 0) return;
            foreach (var i in KeyLines(t, h, key).OrderByDescending(i => i)) t.Lines.RemoveAt(i);
            if (prior.SectionCreated && SectionIsEmpty(t, h)) RemoveSection(t, h);
            if (prior.FinalEolAdded) t.UndoFinalEol();
            return;
        }
        if (h < 0) { t.Append(new[] { $"[{section}]", prior.Lines[^1] }); return; }

        var idx = KeyLines(t, h, key);
        if (idx.Count == prior.Lines.Count)
        {
            for (int n = 0; n < idx.Count; n++) t.Lines[idx[n]] = t.Lines[idx[n]] with { Text = prior.Lines[n] };
            return;
        }
        if (idx.Count == 0) { Insert(t, h, prior.Lines[^1]); return; }

        // Rewritten since, with a different number of copies: give every copy the value that was in force.
        var rx = KeyLine(key);
        var val = rx.Match(prior.Lines[^1]).Groups["val"].Value;
        foreach (var i in idx) t.Lines[i] = t.Lines[i] with { Text = rx.Match(t.Lines[i].Text).Groups["pre"].Value + val };
    }

    public static EditPrior Replace(SettingsText t, string section, IReadOnlyList<string> lines)
    {
        int h = FindHeader(t, section);
        if (h < 0)
        {
            bool appended = t.Append(new[] { $"[{section}]" }.Concat(lines));
            return new EditPrior(SectionCreated: true, FinalEolAdded: appended);
        }
        var eol = t.Eol;
        int end = BodyEnd(t, h);
        var prior = t.Lines.GetRange(h + 1, end - h - 1);
        t.Lines.RemoveRange(h + 1, end - h - 1);
        bool fixedEol = false;
        if (h + 1 == t.Lines.Count && t.Lines[h].Eol.Length == 0 && lines.Count > 0)
        {
            t.Lines[h] = t.Lines[h] with { Eol = eol };
            fixedEol = true;
        }
        t.Lines.InsertRange(h + 1, lines.Select(l => new SettingsLine(l, eol)));
        return new EditPrior(Section: prior, FinalEolAdded: fixedEol);
    }

    public static void RevertReplace(SettingsText t, string section, EditPrior prior)
    {
        int h = FindHeader(t, section);
        if (prior.Section is null)
        {
            if (h < 0) return;
            RemoveSection(t, h);
            if (prior.FinalEolAdded) t.UndoFinalEol();
            return;
        }
        if (h < 0) { t.Append(new[] { $"[{section}]" }); h = t.Lines.Count - 1; }
        int end = BodyEnd(t, h);
        t.Lines.RemoveRange(h + 1, end - h - 1);
        t.Lines.InsertRange(h + 1, prior.Section);
        if (prior.FinalEolAdded) t.Lines[h] = t.Lines[h] with { Eol = "" };
    }

    /// <summary>Inserts a line after the section's last non-blank line. Returns true when that
    /// line was the file's last and had no ending, so it was given one.</summary>
    internal static bool Insert(SettingsText t, int header, string text)
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

    static void RemoveSection(SettingsText t, int header) =>
        t.Lines.RemoveRange(header, SectionEnd(t, header) - header);
}
