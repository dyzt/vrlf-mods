using System.Text;
using System.Text.RegularExpressions;

namespace VrlfMods;

public static class ConfigFile
{
    // ---- bool parsing ----
    public static bool? AsBool(string v)
    {
        v = v.Trim();
        if (v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1"
            || v.Equals("on", StringComparison.OrdinalIgnoreCase)
            || v.Equals("yes", StringComparison.OrdinalIgnoreCase)) return true;
        if (v.Equals("false", StringComparison.OrdinalIgnoreCase) || v == "0"
            || v.Equals("off", StringComparison.OrdinalIgnoreCase)
            || v.Equals("no", StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }

    static bool Bepinex(string format) => format == "bepinex";

    // Match "  key = value" (optionally section-scoped for bepinex). Returns the value.
    public static bool? ParseBoolIn(string text, string format, string? section, string key)
    {
        string? cur = null;
        foreach (var (body, _) in SplitKeepEol(text))
        {
            var sec = SectionHeader(body);
            if (Bepinex(format) && sec is not null) { cur = sec; continue; }
            bool inSection = !Bepinex(format) || string.Equals(cur, section, StringComparison.OrdinalIgnoreCase);
            if (!inSection) continue;
            var m = KeyLine(body, key);
            if (m.Success) return AsBool(m.Groups["val"].Value);
        }
        return null;
    }

    public static string SetBoolIn(string text, string format, string? section, string key, bool value)
    {
        var raw = value ? "true" : "false";
        var lines = SplitKeepEol(text);
        string? curSection = null;
        int sectionEndIdx = -1;          // last line index within the target section
        for (int i = 0; i < lines.Count; i++)
        {
            var body = lines[i].body;
            var sec = SectionHeader(body);
            if (Bepinex(format) && sec is not null) { curSection = sec; continue; }
            bool inSection = !Bepinex(format) || string.Equals(curSection, section, StringComparison.OrdinalIgnoreCase);
            if (inSection) sectionEndIdx = i;
            if (!inSection) continue;
            var m = KeyLine(body, key);
            if (m.Success)
            {
                lines[i] = (m.Groups["pre"].Value + raw, lines[i].eol);
                return Join(lines);
            }
        }
        // absent → insert at end of section (bepinex) or end of file (kv)
        var insert = $"{key}={raw}";
        if (Bepinex(format) && sectionEndIdx >= 0)
            lines.Insert(sectionEndIdx + 1, (insert, "\n"));
        else
        {
            if (lines.Count > 0 && lines[^1].eol.Length == 0) lines[^1] = (lines[^1].body, "\n");
            lines.Add((insert, "\n"));
        }
        return Join(lines);
    }

    public static string? HelpIn(string text, string format, string? section, string key)
    {
        var lines = SplitKeepEol(text);
        string? curSection = null;
        for (int i = 0; i < lines.Count; i++)
        {
            var body = lines[i].body;
            var sec = SectionHeader(body);
            if (Bepinex(format) && sec is not null) { curSection = sec; continue; }
            bool inSection = !Bepinex(format) || string.Equals(curSection, section, StringComparison.OrdinalIgnoreCase);
            if (!inSection) continue;
            if (!KeyLine(body, key).Success) continue;
            // Walk upward collecting the contiguous comment block.
            var comments = new List<string>();
            for (int j = i - 1; j >= 0; j--)
            {
                var t = lines[j].body.TrimStart();
                if (t.StartsWith("##")) comments.Insert(0, t[2..].Trim());
                else if (Bepinex(format) && t.StartsWith("#")) continue;    // skip BepInEx metadata (# Setting type / # Default value)
                else if (!Bepinex(format) && t.StartsWith("#")) comments.Insert(0, t[1..].Trim());
                else if (t.Length == 0 && comments.Count == 0) continue;   // skip blanks just above
                else break;
            }
            return comments.Count > 0 ? string.Join(" ", comments).Trim() : null;
        }
        return null;
    }

    // ---- file wrappers ----
    public static bool? ReadBool(string path, string format, string? section, string key)
        => File.Exists(path) ? ParseBoolIn(File.ReadAllText(path), format, section, key) : null;

    public static void SetBool(string path, string format, string? section, string key, bool value)
        => File.WriteAllText(path, SetBoolIn(File.ReadAllText(path), format, section, key, value));

    public static string? ReadHelp(string path, string format, string? section, string key)
        => File.Exists(path) ? HelpIn(File.ReadAllText(path), format, section, key) : null;

    // ---- helpers ----
    static Match KeyLine(string line, string key) => Regex.Match(
        line, $@"^(?<pre>\s*{Regex.Escape(key)}\s*=\s*)(?<val>[^\r\n]*)$");

    static string? SectionHeader(string line)
    {
        var m = Regex.Match(line.Trim(), @"^\[(?<s>.+)\]$");
        return m.Success ? m.Groups["s"].Value : null;
    }

    static List<(string body, string eol)> SplitKeepEol(string text)
    {
        var result = new List<(string, string)>();
        int i = 0;
        while (i < text.Length)
        {
            int nl = text.IndexOf('\n', i);
            if (nl < 0) { result.Add((text[i..], "")); break; }
            var seg = text[i..nl];
            if (seg.EndsWith('\r')) result.Add((seg[..^1], "\r\n"));
            else result.Add((seg, "\n"));
            i = nl + 1;
        }
        return result;
    }

    static string Join(List<(string body, string eol)> lines)
    {
        var sb = new StringBuilder();
        foreach (var (body, eol) in lines) { sb.Append(body); sb.Append(eol); }
        return sb.ToString();
    }
}
