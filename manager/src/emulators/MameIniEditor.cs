using System.Text.RegularExpressions;

namespace VrlfMods;

/// <summary>
/// Edits MAME's <c>key&lt;whitespace&gt;value</c> ini, which has no sections. MAME reads the last
/// copy of a repeated key, so <see cref="Get"/> does too.
/// </summary>
public static class MameIniEditor
{
    static Regex KeyLine(string key) =>
        new($@"^(?<pre>\s*{Regex.Escape(key)})(?<gap>\s+|$)(?<val>.*)$", RegexOptions.IgnoreCase);

    static List<int> KeyLines(SettingsText t, string key)
    {
        var rx = KeyLine(key);
        var found = new List<int>();
        for (int i = 0; i < t.Lines.Count; i++)
            if (!SettingsText.IsBlankOrComment(t.Lines[i].Text) && rx.IsMatch(t.Lines[i].Text)) found.Add(i);
        return found;
    }

    static string With(Match m, string value) =>
        m.Groups["pre"].Value + (m.Groups["gap"].Length > 0 ? m.Groups["gap"].Value : " ") + value;

    public static string? Get(SettingsText t, string key)
    {
        var idx = KeyLines(t, key);
        return idx.Count == 0 ? null : KeyLine(key).Match(t.Lines[idx[^1]].Text).Groups["val"].Value.Trim();
    }

    public static EditPrior Set(SettingsText t, string key, string value)
    {
        var idx = KeyLines(t, key);
        if (idx.Count == 0) return new EditPrior(FinalEolAdded: t.Append(new[] { key.PadRight(25) + " " + value }));

        var rx = KeyLine(key);
        var prior = new List<string>();
        foreach (var i in idx)
        {
            prior.Add(t.Lines[i].Text);
            t.Lines[i] = t.Lines[i] with { Text = With(rx.Match(t.Lines[i].Text), value) };
        }
        return new EditPrior(Lines: prior);
    }

    public static void RevertSet(SettingsText t, string key, EditPrior prior)
    {
        var idx = KeyLines(t, key);
        if (prior.Lines is null)
        {
            if (idx.Count == 0) return;
            foreach (var i in idx.OrderByDescending(i => i)) t.Lines.RemoveAt(i);
            if (prior.FinalEolAdded) t.UndoFinalEol();
            return;
        }
        if (idx.Count == prior.Lines.Count)
        {
            for (int n = 0; n < idx.Count; n++) t.Lines[idx[n]] = t.Lines[idx[n]] with { Text = prior.Lines[n] };
            return;
        }
        if (idx.Count == 0) { t.Append(new[] { prior.Lines[^1] }); return; }

        var rx = KeyLine(key);
        var val = rx.Match(prior.Lines[^1]).Groups["val"].Value;
        foreach (var i in idx) t.Lines[i] = t.Lines[i] with { Text = With(rx.Match(t.Lines[i].Text), val) };
    }
}
