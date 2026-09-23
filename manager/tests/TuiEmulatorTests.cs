using Xunit;

namespace VrlfMods.Tests;

public class TuiEmulatorTests
{
    static EmulatorOptionStatus Opt(string id, string? inst = null, string ver = "1.0", string? req = null) =>
        new(id, id == "base" ? "Base config" : "Calibration pack", id, ver, inst, req);

    static EmulatorStatus Emu(string? folder = @"D:\Dolphin\User", bool locked = false, string? suggested = null,
        bool exists = true, bool needsMet = true, params EmulatorOptionStatus[] opts) =>
        new("dolphin", "Dolphin", folder, folder is not null, exists, suggested, locked,
            opts.Length > 0 ? opts.ToList() : new() { Opt("base"), Opt("calibration", req: "base") },
            "vigembus", needsMet, "\"Dolphin\" on the Steam Workshop", "Calibration covers 27 USA games.");

    static ListReport List(params EmulatorStatus[] emus) =>
        new("network", new() { new ModStatus("reload", "Reload VRLF Mod", "1.1.0", null, new()) },
            Emulators: emus.ToList());

    [Fact]
    public void The_main_list_gains_an_emulators_section()
    {
        var rows = TuiModel.ListRows(List(Emu(folder: null)));
        var header = rows.FindIndex(r => r.Kind == RowKind.Header && r.Text == "EMULATORS");
        Assert.True(header > 0);
        Assert.Equal(RowKind.Separator, rows[header - 1].Kind);
        var row = rows[header + 1];
        Assert.Equal(ActionKind.EmuOpen, row.Action);
        Assert.Equal("dolphin", row.ModId);
        Assert.Contains("○ folder not chosen", row.Text);
    }

    [Fact]
    public void Without_emulators_the_main_list_is_unchanged()
    {
        var rows = TuiModel.ListRows(new ListReport("network", new()));
        Assert.DoesNotContain(rows, r => r.Text == "EMULATORS");
    }

    [Fact]
    public void Emulator_names_align_with_mod_names()
    {
        var rows = TuiModel.ListRows(List(Emu(folder: null)));
        var mod = rows.First(r => r.Action == ActionKind.Open).Text;
        var emu = rows.First(r => r.Action == ActionKind.EmuOpen).Text;
        Assert.Equal(mod.IndexOf("○"), emu.IndexOf("○"));
    }

    [Fact]
    public void The_folder_row_comes_first_and_a_suggestion_gets_its_own_row()
    {
        var rows = TuiModel.EmulatorRows(Emu(folder: null, suggested: @"C:\Users\x\AppData\Roaming\Dolphin Emulator"));
        Assert.Equal(ActionKind.EmuSetFolder, rows[0].Action);
        Assert.Contains("not chosen", rows[0].Text);
        Assert.Equal(ActionKind.EmuUseSuggested, rows[1].Action);
        Assert.Contains(@"Dolphin Emulator", rows[1].Text);
    }

    [Fact]
    public void Options_are_unavailable_until_a_folder_is_chosen()
    {
        var rows = TuiModel.EmulatorRows(Emu(folder: null));
        Assert.All(rows.Where(r => r.Action == ActionKind.EmuToggle), r => Assert.False(r.Enabled));
    }

    [Fact]
    public void An_addon_is_unavailable_until_its_base_is_installed()
    {
        var rows = TuiModel.EmulatorRows(Emu());
        var cal = rows.Single(r => r.ToggleKey == "calibration");
        Assert.False(cal.Enabled);
        Assert.True(rows.Single(r => r.ToggleKey == "base").Enabled);
    }

    [Fact]
    public void An_installed_option_can_always_be_uninstalled()
    {
        var rows = TuiModel.EmulatorRows(Emu(locked: true, exists: false,
            opts: new[] { Opt("base", "1.0"), Opt("calibration", "1.0", req: "base") }));
        Assert.All(rows.Where(r => r.Action == ActionKind.EmuToggle), r =>
        {
            Assert.True(r.Enabled);
            Assert.True(r.ToggleOn);
        });
    }

    [Fact]
    public void Update_and_reapply_rows_appear_only_when_they_mean_something()
    {
        Assert.DoesNotContain(TuiModel.EmulatorRows(Emu()), r => r.Action is ActionKind.EmuUpdate or ActionKind.EmuReapply);
        var rows = TuiModel.EmulatorRows(Emu(locked: true, opts: new[] { Opt("base", "1.0", ver: "1.1") }));
        Assert.Contains(rows, r => r.Action == ActionKind.EmuUpdate && r.Text.Contains("Base config v1.1"));
        Assert.Contains(rows, r => r.Action == ActionKind.EmuReapply);
    }

    [Fact]
    public void Forget_is_offered_only_for_a_chosen_folder_with_nothing_installed()
    {
        Assert.Contains(TuiModel.EmulatorRows(Emu()), r => r.Action == ActionKind.EmuClearFolder);
        Assert.DoesNotContain(TuiModel.EmulatorRows(Emu(folder: null)), r => r.Action == ActionKind.EmuClearFolder);
        Assert.DoesNotContain(TuiModel.EmulatorRows(Emu(locked: true, opts: new[] { Opt("base", "1.0") })),
            r => r.Action == ActionKind.EmuClearFolder);
    }

    // The screen is the folder, the options and the actions: no help lines, notes or info rows.
    [Fact]
    public void The_emulator_screen_carries_no_help_or_notes()
    {
        foreach (var e in new[] { Emu(folder: null, suggested: @"C:\D"), Emu(needsMet: false),
                                  Emu(locked: true, opts: new[] { Opt("base", "1.0", ver: "1.1"), Opt("calibration", "1.0", req: "base") }) })
        {
            var rows = TuiModel.EmulatorRows(e);
            Assert.All(rows, r => Assert.Null(r.Help));
            Assert.DoesNotContain(rows, r => r.Kind == RowKind.Info);
        }
        Assert.All(TuiModel.ListRows(List(Emu())).Where(r => r.Action == ActionKind.EmuOpen), r => Assert.Null(r.Help));
    }

    [Theory]
    [InlineData(null, false, false, "Settings folder: not chosen")]
    [InlineData(@"D:\E", true, true, @"Settings folder: D:\E")]
    [InlineData(@"D:\E", false, false, @"Settings folder: D:\E  (missing)")]
    [InlineData(@"D:\E", false, true, @"Settings folder: D:\E")]
    public void Folder_text_reads_the_state(string? folder, bool locked, bool exists, string expected)
        => Assert.Equal(expected, TuiModel.EmulatorFolderText(Emu(folder: folder, locked: locked, exists: exists)));

    [Fact]
    public void Enter_on_an_emulator_opens_its_screen_on_the_folder_row()
    {
        var rows = TuiModel.ListRows(List(Emu()));
        var idx = rows.FindIndex(r => r.Action == ActionKind.EmuOpen);
        var (s, act) = TuiModel.Reduce(new TuiState(Screen.List, idx, null), TuiKey.Enter, rows);
        Assert.Equal(new TuiState(Screen.Emulator, 0, "dolphin"), s);
        Assert.Equal(ActionKind.EmuOpen, act.Kind);
    }

    [Fact]
    public void Enter_on_an_option_asks_for_the_opposite_state()
    {
        var rows = TuiModel.EmulatorRows(Emu(locked: true, opts: new[] { Opt("base", "1.0") }));
        var idx = rows.FindIndex(r => r.ToggleKey == "base");
        var (_, act) = TuiModel.Reduce(new TuiState(Screen.Emulator, idx, "dolphin"), TuiKey.Enter, rows);
        Assert.Equal(ActionKind.EmuToggle, act.Kind);
        Assert.Equal("dolphin", act.ModId);
        Assert.Equal("base", act.ToggleKey);
        Assert.False(act.ToggleOn);
    }

    [Fact]
    public void Back_from_an_emulator_returns_to_the_list()
    {
        var (s, _) = TuiModel.Reduce(new TuiState(Screen.Emulator, 2, "dolphin"), TuiKey.Back, TuiModel.EmulatorRows(Emu()));
        Assert.Equal(Screen.List, s.Screen);
    }
}
