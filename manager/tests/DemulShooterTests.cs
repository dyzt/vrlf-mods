using System.IO.Compression;
using System.Text;
using Xunit;

namespace VrlfMods.Tests;

public class DemulShooterTests
{
    const string ZipUrl = "https://github.com/argonlefou/DemulShooter/releases/download/v17.9/DemulShooter_v17.9.zip";

    /// <summary>Temp %APPDATA%\VRLF, holding the mods root.</summary>
    static AppPaths TempPaths() =>
        new(Path.Combine(Path.GetTempPath(), "vrlf-ds-" + Guid.NewGuid().ToString("N"), "VRLF", "mods"));

    static byte[] ReleaseZip(params string[] extra)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in new[] { "DemulShooter.exe", "DemulShooterX64.exe", "MemoryData/lindbergh/hotd4.cfg" }.Concat(extra))
                using (var w = new StreamWriter(zip.CreateEntry(name).Open())) w.Write(name);
        }
        return ms.ToArray();
    }

    static byte[] ReleaseJson(string? sha256, string tag = "v17.9", string asset = "DemulShooter_v17.9.zip") =>
        Encoding.UTF8.GetBytes($$"""
        {"tag_name": "{{tag}}", "assets": [
          {"name": "Source.txt", "browser_download_url": "https://example.invalid/x", "digest": null},
          {"name": "{{asset}}", "browser_download_url": "{{ZipUrl}}"{{(sha256 is null ? "" : $", \"digest\": \"sha256:{sha256}\"")}}}
        ]}
        """);

    static (DemulShooter ds, AppPaths paths, FakeProcessProbe probe) Build(byte[]? zip, bool digest = true)
    {
        var paths = TempPaths();
        var map = new Dictionary<string, byte[]?>
        {
            [DemulShooter.LatestUrl] = ReleaseJson(zip is not null && digest ? Installer.Sha256Hex(zip) : null),
            [ZipUrl] = zip,
        };
        var probe = new FakeProcessProbe();
        return (new DemulShooter(new FakeHttpFetcher(map), paths, probe), paths, probe);
    }

    static void WriteSettings(AppPaths paths, string text)
    {
        Directory.CreateDirectory(paths.VrlfRoot);
        File.WriteAllText(paths.SettingsCfgPath, text);
    }

    [Fact]
    public void Paths_sit_beside_the_mods_root_in_vrlfs_own_folder()
    {
        var paths = new AppPaths(@"C:\Users\x\AppData\Roaming\VRLF\mods");
        Assert.Equal(@"C:\Users\x\AppData\Roaming\VRLF\DemulShooter", paths.DemulShooterDir);
        Assert.Equal(@"C:\Users\x\AppData\Roaming\VRLF\settings.cfg", paths.SettingsCfgPath);
    }

    [Fact]
    public void ParseLatest_takes_the_demulshooter_zip_and_its_digest()
    {
        var got = DemulShooter.ParseLatest(ReleaseJson("abc123"));
        Assert.NotNull(got);
        Assert.Equal("v17.9", got!.Value.Tag);
        Assert.Equal(ZipUrl, got.Value.Url);
        Assert.Equal("abc123", got.Value.Sha256);
        Assert.Null(DemulShooter.ParseLatest(ReleaseJson(null))!.Value.Sha256);
        Assert.Null(DemulShooter.ParseLatest(ReleaseJson("abc", asset: "Something.7z")));
        Assert.Null(DemulShooter.ParseLatest(Encoding.UTF8.GetBytes("not json")));
    }

    [Fact]
    public async Task Install_extracts_turns_outputs_on_and_points_vrlf_at_it()
    {
        var (ds, paths, _) = Build(ReleaseZip());
        WriteSettings(paths, "[display]\n\nspectator=false\n\n[outputs]\n\ndemulshooter_path=\"H:/Utilities/DemulShooter\"\n");

        var r = await ds.Install();

        Assert.True(r.Ok, r.Message);
        Assert.True(File.Exists(Path.Combine(paths.DemulShooterDir, "DemulShooter.exe")));
        Assert.True(File.Exists(Path.Combine(paths.DemulShooterDir, "MemoryData", "lindbergh", "hotd4.cfg")));
        Assert.False(Directory.Exists(paths.DemulShooterDir + ".download"), "no staging left behind");
        var cfg = File.ReadAllText(Path.Combine(paths.DemulShooterDir, "config.ini"));
        Assert.Contains("OutputEnabled = True", cfg);
        Assert.Contains("WM_OutputsEnabled = False", cfg);
        Assert.Contains("Net_OutputsEnabled = True", cfg);
        var settings = File.ReadAllText(paths.SettingsCfgPath);
        Assert.Contains($"demulshooter_path=\"{paths.DemulShooterDir.Replace('\\', '/')}\"", settings);
        Assert.Contains("spectator=false", settings);
        Assert.Contains("was H:/Utilities/DemulShooter", r.Message);
        Assert.Equal("v17.9", ds.InstalledVersion());
        Assert.True(ds.VrlfPointsHere());
    }

    [Fact]
    public async Task An_update_keeps_the_players_config_but_turns_outputs_on()
    {
        var (ds, paths, _) = Build(ReleaseZip());
        Directory.CreateDirectory(paths.DemulShooterDir);
        File.WriteAllText(Path.Combine(paths.DemulShooterDir, "config.ini"),
            ";Output Settings\r\nP1Mode = 2\r\nOutputEnabled = False\r\nNet_OutputsEnabled = False\r\n");
        File.WriteAllText(Path.Combine(paths.DemulShooterDir, "old.dll"), "old");

        var r = await ds.Install();

        Assert.True(r.Ok, r.Message);
        var cfg = File.ReadAllText(Path.Combine(paths.DemulShooterDir, "config.ini"));
        Assert.Contains("P1Mode = 2", cfg);
        Assert.Contains("OutputEnabled = True", cfg);
        Assert.Contains("Net_OutputsEnabled = True", cfg);
        Assert.Contains("WM_OutputsEnabled = False", cfg);
        Assert.False(File.Exists(Path.Combine(paths.DemulShooterDir, "old.dll")), "the old release is replaced, not merged");
    }

    [Fact]
    public async Task A_settings_file_with_no_outputs_section_gets_one_godot_style()
    {
        var (ds, paths, _) = Build(ReleaseZip());
        WriteSettings(paths, "[display]\n\nspectator=false\n");

        Assert.True((await ds.Install()).Ok);

        var settings = File.ReadAllText(paths.SettingsCfgPath);
        Assert.EndsWith($"[outputs]\n\ndemulshooter_path=\"{paths.DemulShooterDir.Replace('\\', '/')}\"\n", settings);
        Assert.StartsWith("[display]\n\nspectator=false\n", settings);
    }

    [Fact]
    public async Task An_outputs_section_without_the_key_gets_it_under_its_header()
    {
        var (ds, paths, _) = Build(ReleaseZip());
        WriteSettings(paths, "[outputs]\n\nother=1\n");

        Assert.True((await ds.Install()).Ok);

        Assert.Equal($"[outputs]\n\ndemulshooter_path=\"{paths.DemulShooterDir.Replace('\\', '/')}\"\nother=1\n",
            File.ReadAllText(paths.SettingsCfgPath));
    }

    [Fact]
    public async Task No_settings_file_means_vrlf_never_ran_and_none_is_made()
    {
        var (ds, paths, _) = Build(ReleaseZip());

        var r = await ds.Install();

        Assert.True(r.Ok, r.Message);
        Assert.False(File.Exists(paths.SettingsCfgPath));
        Assert.Contains("VRLF finds it there", r.Message);
    }

    [Fact]
    public async Task A_hash_mismatch_installs_nothing()
    {
        var zip = ReleaseZip();
        var paths = TempPaths();
        var map = new Dictionary<string, byte[]?>
        {
            [DemulShooter.LatestUrl] = ReleaseJson(new string('0', 64)),
            [ZipUrl] = zip,
        };
        var ds = new DemulShooter(new FakeHttpFetcher(map), paths, new FakeProcessProbe());

        var r = await ds.Install();

        Assert.False(r.Ok);
        Assert.Contains("hash mismatch", r.Message);
        Assert.False(Directory.Exists(paths.DemulShooterDir));
    }

    [Fact]
    public async Task A_release_without_a_digest_still_installs()
    {
        var (ds, _, _) = Build(ReleaseZip(), digest: false);
        Assert.True((await ds.Install()).Ok);
    }

    [Fact]
    public async Task A_zip_that_escapes_its_folder_is_refused()
    {
        var (ds, paths, _) = Build(ReleaseZip("../escape.txt"));

        var r = await ds.Install();

        Assert.False(r.Ok);
        Assert.False(File.Exists(Path.Combine(paths.VrlfRoot, "escape.txt")));
        Assert.False(Directory.Exists(paths.DemulShooterDir + ".download"));
    }

    [Fact]
    public async Task A_running_demulshooter_blocks_install_and_uninstall()
    {
        var (ds, paths, probe) = Build(ReleaseZip());
        Assert.True((await ds.Install()).Ok);
        probe.Running.Add("DemulShooterX64");

        Assert.False((await ds.Install()).Ok);
        Assert.False(ds.Uninstall().Ok);
        Assert.True(File.Exists(Path.Combine(paths.DemulShooterDir, "DemulShooter.exe")));
    }

    [Fact]
    public async Task A_running_vrlf_is_told_to_restart()
    {
        var (ds, _, probe) = Build(ReleaseZip());
        probe.Running.Add("vr_lightgun_framework");
        Assert.Contains("Restart VRLF", (await ds.Install()).Message);
    }

    [Fact]
    public async Task No_network_installs_nothing()
    {
        var ds = new DemulShooter(new FakeHttpFetcher(new()), TempPaths(), new FakeProcessProbe());
        var r = await ds.Install();
        Assert.False(r.Ok);
        Assert.Contains("GitHub", r.Message);
    }

    [Fact]
    public async Task Uninstall_removes_it_and_clears_only_its_own_path()
    {
        var (ds, paths, _) = Build(ReleaseZip());
        WriteSettings(paths, "[outputs]\n\ndemulshooter_path=\"H:/Utilities/DemulShooter\"\n");
        Assert.True((await ds.Install()).Ok);

        var r = ds.Uninstall();

        Assert.True(r.Ok, r.Message);
        Assert.False(Directory.Exists(paths.DemulShooterDir));
        Assert.Null(ds.InstalledVersion());
        Assert.DoesNotContain("demulshooter_path", File.ReadAllText(paths.SettingsCfgPath));
        Assert.Contains("[outputs]", File.ReadAllText(paths.SettingsCfgPath));
    }

    [Fact]
    public async Task Uninstall_leaves_a_path_the_player_pointed_elsewhere()
    {
        var (ds, paths, _) = Build(ReleaseZip());
        Assert.True((await ds.Install()).Ok);
        WriteSettings(paths, "[outputs]\n\ndemulshooter_path=\"H:/Utilities/DemulShooter\"\n");

        Assert.True(ds.Uninstall().Ok);

        Assert.Contains("H:/Utilities/DemulShooter", File.ReadAllText(paths.SettingsCfgPath));
    }

    [Fact]
    public void Uninstall_when_absent_is_a_no_op()
    {
        var ds = new DemulShooter(new FakeHttpFetcher(new()), TempPaths(), new FakeProcessProbe());
        var r = ds.Uninstall();
        Assert.True(r.Ok);
        Assert.Contains("not installed", r.Message);
    }

    [Fact]
    public void Settings_path_round_trips_quotes_backslashes_and_non_ascii()
    {
        var paths = TempPaths();
        WriteSettings(paths, "[outputs]\n\ndemulshooter_path=\"\"\n");
        VrlfSettings.SetDemulShooterPath(paths.SettingsCfgPath, @"C:\Users\Jérôme\VRLF\DemulShooter");
        Assert.Equal("C:/Users/Jérôme/VRLF/DemulShooter", VrlfSettings.GetDemulShooterPath(paths.SettingsCfgPath));
        Assert.Contains("Jérôme", File.ReadAllText(paths.SettingsCfgPath, Encoding.UTF8));
    }

    [Fact]
    public void Cli_parses_the_demulshooter_subcommands()
    {
        Assert.Equal("install", Cli.Parse(new[] { "demulshooter", "install" }).Id);
        Assert.Null(Cli.Parse(new[] { "demulshooter" }).Error);
        Assert.NotNull(Cli.Parse(new[] { "demulshooter", "explode" }).Error);
    }

    [Fact]
    public void The_list_shows_demulshooter_in_the_top_block()
    {
        var rows = TuiModel.ListRows(new ListReport("test", new()));
        var sep = rows.FindIndex(r => r.Kind == RowKind.Separator);
        var ds = rows.FindIndex(r => r.Action == ActionKind.DemulShooter);
        Assert.InRange(ds, 0, sep - 1);
        Assert.Contains("not installed", rows[ds].Text);
    }

    [Fact]
    public void The_screen_offers_install_or_update_and_uninstall()
    {
        var none = TuiModel.DemulShooterRows(new ListReport("test", new()));
        Assert.Contains(none, r => r.Action == ActionKind.DsInstall && r.Text == "Install latest");
        Assert.DoesNotContain(none, r => r.Action == ActionKind.DsUninstall);

        var installed = TuiModel.DemulShooterRows(new ListReport("test", new(),
            DemulShooterInstalled: true, DemulShooterVersion: "v17.9", DemulShooterDir: @"C:\x", DemulShooterLinked: true));
        Assert.Contains(installed, r => r.Action == ActionKind.DsInstall && r.Text == "Update to latest");
        Assert.Contains(installed, r => r.Action == ActionKind.DsUninstall);
        Assert.Equal("● installed v17.9", TuiModel.DemulShooterStatus(new ListReport("test", new(),
            DemulShooterInstalled: true, DemulShooterVersion: "v17.9", DemulShooterLinked: true)));
    }

    [Fact]
    public void The_screen_carries_the_antivirus_note_and_the_credit()
    {
        var rows = TuiModel.DemulShooterRows(new ListReport("test", new()));
        Assert.Contains(rows, r => r.Text.Contains("antivirus") && !r.Selectable);
        Assert.Contains(rows, r => r.Text.Contains("argonlefou") && !r.Selectable);
    }

    [Fact]
    public async Task A_download_that_fails_mentions_antivirus()
    {
        var (ds, _, _) = Build(zip: null);
        var r = await ds.Install();
        Assert.False(r.Ok);
        Assert.Contains("antivirus", r.Message);
    }

    [Fact]
    public void Entering_the_row_opens_its_screen()
    {
        var rows = TuiModel.ListRows(new ListReport("test", new()));
        var at = rows.FindIndex(r => r.Action == ActionKind.DemulShooter);
        var (next, action) = TuiModel.Reduce(new TuiState(Screen.List, at, null), TuiKey.Enter, rows);
        Assert.Equal(Screen.DemulShooter, next.Screen);
        Assert.Equal(ActionKind.DemulShooter, action.Kind);
    }
}
