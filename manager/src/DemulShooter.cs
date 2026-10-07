using System.IO.Compression;
using System.Text.Json;

namespace VrlfMods;

/// <summary>
/// Installs DemulShooter (argonlefou/DemulShooter, always the newest GitHub release) for VRLF's
/// profile Outputs: into %APPDATA%\VRLF\DemulShooter, with its network outputs turned on, and
/// VRLF's settings.cfg ([outputs] demulshooter_path) pointed at it. VRLF reads the game's recoil
/// and hits from it and starts it from the Profile Editor. Not a mod: it lives beside VRLF, not in
/// a game, so it has its own receipt instead of the mod receipts.
/// </summary>
public sealed class DemulShooter
{
    public const string Repo = "argonlefou/DemulShooter";
    public const string LatestUrl = "https://api.github.com/repos/" + Repo + "/releases/latest";
    public const string Exe = "DemulShooter.exe";
    public const string ConfigFile = "config.ini";
    private const string Key = "demulshooter";

    /// <summary>Anything that holds files in the install folder open.</summary>
    public static readonly string[] ProcessNames = { "DemulShooter", "DemulShooterX64", "DemulShooter_GUI" };
    public const string VrlfProcess = "vr_lightgun_framework";

    /// <summary>What VRLF needs in DemulShooter's own config.ini: outputs on, sent over the
    /// network (TCP 8000, which VRLF reads), not as window messages. DemulShooter's defaults are
    /// outputs off, window messages on.</summary>
    public static readonly (string Key, string Value)[] OutputSettings =
    {
        ("OutputEnabled", "True"), ("WM_OutputsEnabled", "False"), ("Net_OutputsEnabled", "True"),
    };

    private readonly IHttpFetcher _http;
    private readonly AppPaths _paths;
    private readonly IProcessProbe _probe;

    public DemulShooter(IHttpFetcher http, AppPaths paths, IProcessProbe probe)
    { _http = http; _paths = paths; _probe = probe; }

    public string InstallDir => _paths.DemulShooterDir;
    private string StagingDir => InstallDir + ".download";
    private string ReceiptPath => Path.Combine(_paths.ModsRoot, "demulshooter.json");

    /// <summary>The installed release's tag, or null: a receipt AND the exe it installed.</summary>
    public string? InstalledVersion()
    {
        if (!File.Exists(Path.Combine(InstallDir, Exe)) || !File.Exists(ReceiptPath)) return null;
        try
        {
            var r = JsonSerializer.Deserialize(File.ReadAllBytes(ReceiptPath), VrlfJson.Default.DictionaryStringString);
            return r is not null && r.TryGetValue("version", out var v) && !string.IsNullOrEmpty(v) ? v : null;
        }
        catch { return null; }
    }

    public bool IsInstalled() => InstalledVersion() is not null;

    /// <summary>Does VRLF's settings.cfg point at this install?</summary>
    public bool VrlfPointsHere() => SamePath(VrlfSettings.GetDemulShooterPath(_paths.SettingsCfgPath), InstallDir);

    /// <summary>The newest release's tag, zip URL and SHA-256 (null when GitHub gives none),
    /// or null when the JSON has no DemulShooter zip.</summary>
    public static (string Tag, string Url, string? Sha256)? ParseLatest(byte[] json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString();
            if (string.IsNullOrEmpty(tag)) return null;
            foreach (var a in root.GetProperty("assets").EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                if (!name.StartsWith("DemulShooter", StringComparison.OrdinalIgnoreCase)
                    || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                var url = a.GetProperty("browser_download_url").GetString();
                if (string.IsNullOrEmpty(url)) continue;
                string? sha = null;
                if (a.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String
                    && d.GetString() is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    sha = digest["sha256:".Length..];
                return (tag, url, sha);
            }
            return null;
        }
        catch { return null; }
    }

    private bool Running(params string[] names) =>
        _probe.RunningProcessNames().Any(p => names.Any(n => string.Equals(p, n, StringComparison.OrdinalIgnoreCase)));

    public async Task<OpResult> Install()
    {
        if (Running(ProcessNames))
            return new OpResult(false, Key, "DemulShooter is running; close it first (VRLF's Close DemulShooter row does it)");

        var json = await _http.TryGet(LatestUrl);
        if (json is null)
            return new OpResult(false, Key, "could not reach GitHub for the latest DemulShooter (network required)");
        var latest = ParseLatest(json);
        if (latest is null)
            return new OpResult(false, Key, "the latest DemulShooter release has no zip to install");
        var (tag, url, sha) = latest.Value;

        var bytes = await _http.TryGet(url);
        if (bytes is null)
            return new OpResult(false, Key, $"could not download DemulShooter {tag}");
        if (sha is not null && !string.Equals(Installer.Sha256Hex(bytes), sha, StringComparison.OrdinalIgnoreCase))
            return new OpResult(false, Key, "download hash mismatch; refusing to install");

        try
        {
            if (Directory.Exists(StagingDir)) Directory.Delete(StagingDir, recursive: true);
            Directory.CreateDirectory(StagingDir);
            using (var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
            {
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;
                    var dest = Zip.ResolveDest(StagingDir, entry.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    entry.ExtractToFile(dest, overwrite: true);
                }
            }
        }
        catch (Exception ex)
        {
            TryDelete(StagingDir);
            return new OpResult(false, Key, $"the DemulShooter zip is corrupt or unsafe: {ex.Message}");
        }
        if (!File.Exists(Path.Combine(StagingDir, Exe)))
        {
            TryDelete(StagingDir);
            return new OpResult(false, Key, $"DemulShooter {tag} has no {Exe} at the top of its zip; not installed");
        }

        // An update keeps the player's own DemulShooter settings, then makes sure outputs are on.
        var oldConfig = Path.Combine(InstallDir, ConfigFile);
        var newConfig = Path.Combine(StagingDir, ConfigFile);
        try
        {
            if (File.Exists(oldConfig)) File.Copy(oldConfig, newConfig, overwrite: true);
            EnableOutputs(newConfig);
        }
        catch (Exception ex)
        {
            TryDelete(StagingDir);
            return new OpResult(false, Key, $"could not write DemulShooter's config.ini: {ex.Message}");
        }

        try
        {
            if (Directory.Exists(InstallDir)) Directory.Delete(InstallDir, recursive: true);
            Directory.Move(StagingDir, InstallDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(StagingDir);
            return new OpResult(false, Key, $"could not replace {InstallDir} (is something using it?): {ex.Message}");
        }

        Directory.CreateDirectory(_paths.ModsRoot);
        File.WriteAllText(ReceiptPath, JsonSerializer.Serialize(
            new Dictionary<string, string> { ["version"] = tag, ["dir"] = InstallDir },
            VrlfJson.Default.DictionaryStringString));

        var prior = VrlfSettings.SetDemulShooterPath(_paths.SettingsCfgPath, InstallDir);
        var msg = $"DemulShooter {tag} installed in {InstallDir}, network outputs on; " + (prior is null
            ? "VRLF finds it there"
            : "VRLF's settings.cfg points at it");
        if (!string.IsNullOrEmpty(prior) && !SamePath(prior, InstallDir)) msg += $" (was {prior})";
        if (Running(VrlfProcess)) msg += ". Restart VRLF to use it";
        return new OpResult(true, Key, msg);
    }

    public OpResult Uninstall()
    {
        if (!Directory.Exists(InstallDir) && !File.Exists(ReceiptPath))
            return new OpResult(true, Key, "DemulShooter is not installed");
        if (Running(ProcessNames))
            return new OpResult(false, Key, "DemulShooter is running; close it first (VRLF's Close DemulShooter row does it)");
        try
        {
            if (Directory.Exists(InstallDir)) Directory.Delete(InstallDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new OpResult(false, Key, $"could not delete {InstallDir} (is something using it?): {ex.Message}");
        }
        try { File.Delete(ReceiptPath); } catch (IOException) { }
        var cleared = VrlfSettings.ClearDemulShooterPathIf(_paths.SettingsCfgPath, InstallDir);
        return new OpResult(true, Key, cleared
            ? "DemulShooter uninstalled; VRLF's settings.cfg no longer points at it"
            : "DemulShooter uninstalled");
    }

    public OpResult Status()
    {
        var v = InstalledVersion();
        if (v is null) return new OpResult(true, Key, "DemulShooter: not installed");
        return new OpResult(true, Key, VrlfPointsHere()
            ? $"DemulShooter: installed {v} in {InstallDir}"
            : $"DemulShooter: installed {v} in {InstallDir} (VRLF's settings.cfg points elsewhere)");
    }

    /// <summary>Sets each OutputSettings key in DemulShooter's flat <c>Key = Value</c> file, one
    /// line per key, leaving every other line as it was. A missing file starts empty: DemulShooter
    /// fills in its defaults for the rest.</summary>
    public static void EnableOutputs(string path)
    {
        var t = SettingsText.Load(path);
        foreach (var (key, value) in OutputSettings)
        {
            var rx = new System.Text.RegularExpressions.Regex(
                $@"^(?<pre>\s*{System.Text.RegularExpressions.Regex.Escape(key)}\s*=\s*)(?<val>.*)$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            bool found = false;
            for (int i = 0; i < t.Lines.Count; i++)
            {
                if (t.Lines[i].Text.TrimStart().StartsWith(';')) continue;
                var m = rx.Match(t.Lines[i].Text);
                if (!m.Success) continue;
                t.Lines[i] = t.Lines[i] with { Text = m.Groups["pre"].Value + value };
                found = true;
            }
            if (!found) t.Append(new[] { $"{key} = {value}" });
        }
        t.Save(path);
    }

    public static bool SamePath(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        try
        {
            static string N(string p) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p.Replace('/', '\\')));
            return string.Equals(N(a), N(b), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>
/// VRLF's settings.cfg (Godot ConfigFile text): only the one key the manager owns,
/// [outputs] demulshooter_path, as a quoted string with forward slashes. Every other line stays
/// as it was. VRLF rewrites the whole file on its own saves, so a running VRLF must be restarted
/// to see the change; VRLF also finds %APPDATA%\VRLF\DemulShooter by itself when the key is unset.
/// </summary>
public static class VrlfSettings
{
    public const string Section = "outputs";
    public const string PathKey = "demulshooter_path";

    public static string? GetDemulShooterPath(string cfgPath)
    {
        if (!File.Exists(cfgPath)) return null;
        var raw = IniEditor.Get(SettingsText.Load(cfgPath), Section, PathKey);
        return raw is null ? null : Unquote(Utf8(raw));
    }

    /// <summary>Points VRLF at `dir`; returns what it pointed at before ("" when unset), or null
    /// when VRLF has never run here (no settings.cfg): creating one would stand in for VRLF's
    /// first run, and VRLF finds %APPDATA%\VRLF\DemulShooter by itself.</summary>
    public static string? SetDemulShooterPath(string cfgPath, string dir)
    {
        if (!File.Exists(cfgPath)) return null;
        var t = SettingsText.Load(cfgPath);
        var prior = IniEditor.Get(t, Section, PathKey);
        var value = Latin1(Quote(dir.Replace('\\', '/')));
        if (prior is not null)
            IniEditor.Set(t, Section, PathKey, value);   // keeps the line's own `key=` spacing
        else
            InsertGodotStyle(t, $"{PathKey}={value}");
        t.Save(cfgPath);
        return prior is null ? "" : Unquote(Utf8(prior));
    }

    /// <summary>Removes the key when it points at `dir`; true when it did.</summary>
    public static bool ClearDemulShooterPathIf(string cfgPath, string dir)
    {
        if (!DemulShooter.SamePath(GetDemulShooterPath(cfgPath), dir)) return false;
        var t = SettingsText.Load(cfgPath);
        var rx = new System.Text.RegularExpressions.Regex($@"^\s*{PathKey}\s*=",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        bool inSection = false;
        for (int i = 0; i < t.Lines.Count; i++)
        {
            var text = t.Lines[i].Text.Trim();
            if (text.StartsWith('[') && text.EndsWith(']'))
            {
                inSection = string.Equals(text[1..^1], Section, StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (inSection && rx.IsMatch(t.Lines[i].Text)) { t.Lines.RemoveAt(i); i--; }
        }
        t.Save(cfgPath);
        return true;
    }

    /// <summary>A new key the way Godot writes one (`key=value`), under [outputs], creating the
    /// section at the end of the file when there is none.</summary>
    static void InsertGodotStyle(SettingsText t, string line)
    {
        var eol = t.Eol;
        int header = -1;
        for (int i = 0; i < t.Lines.Count; i++)
        {
            var text = t.Lines[i].Text.Trim();
            if (string.Equals(text, $"[{Section}]", StringComparison.OrdinalIgnoreCase)) { header = i; break; }
        }
        if (header < 0)
        {
            var lines = new List<string>();
            if (t.Lines.Count > 0 && t.Lines[^1].Text.Trim().Length > 0) lines.Add("");
            lines.Add($"[{Section}]");
            lines.Add("");
            lines.Add(line);
            t.Append(lines);
            return;
        }
        // After the header's blank line, Godot's own layout: "[outputs]", "", "key=value".
        int at = header + 1;
        while (at < t.Lines.Count && t.Lines[at].Text.Trim().Length == 0) at++;
        if (t.Lines[at - 1].Eol.Length == 0) t.Lines[at - 1] = t.Lines[at - 1] with { Eol = eol };
        t.Lines.Insert(at, new SettingsLine(line, eol));
    }

    static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    static string Unquote(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
            s = s[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
        return s;
    }

    // SettingsText reads and writes bytes as Latin-1; settings.cfg is UTF-8.
    static string Latin1(string utf16) => System.Text.Encoding.Latin1.GetString(System.Text.Encoding.UTF8.GetBytes(utf16));
    static string Utf8(string latin1) => System.Text.Encoding.UTF8.GetString(System.Text.Encoding.Latin1.GetBytes(latin1));
}
