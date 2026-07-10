using Xunit;

namespace VrlfMods.Tests;

public class TuiModelTests
{
    static ModStatus Installed() => new("reload", "Reload", "1.1.0", "1.1.0",
        new() { new GameStatus(330370, "Reload", true, @"C:\g", true, "1.1.0") });
    static ModStatus NotInstalled() => new("hotd2", "HOD2", "1.0", null,
        new() { new GameStatus(1, "HOD2", true, @"C:\g", false, null) });

    [Fact] public void RowStatus_reflects_states()
    {
        Assert.Contains("installed v1.1.0", TuiModel.RowStatus(Installed()));
        Assert.Contains("not installed", TuiModel.RowStatus(NotInstalled()));
        var upd = new ModStatus("m", "M", "2.0", "1.0", new() { new GameStatus(1, "g", true, "p", true, "1.0") });
        Assert.Contains("update", TuiModel.RowStatus(upd));
    }

    [Fact] public void ModRows_has_install_when_not_installed_and_uninstall_when_installed()
    {
        Assert.Contains(TuiModel.ModRows(NotInstalled(), null), r => r.Action == ActionKind.Install);
        Assert.Contains(TuiModel.ModRows(Installed(), null), r => r.Action == ActionKind.Uninstall);
    }

    [Fact] public void ModRows_includes_config_toggle_rows()
    {
        var cfg = new ModConfig("reload", "330370", new() {
            new ToggleState("HideWeapon", "Hide weapon", "help", true, true, null) });
        var rows = TuiModel.ModRows(Installed(), cfg);
        Assert.Contains(rows, r => r.Kind == RowKind.Toggle && r.ToggleKey == "HideWeapon" && r.ToggleOn == true);
    }

    [Fact] public void Reduce_down_moves_over_selectable_rows_only()
    {
        var rows = new List<MenuRow> {
            new(RowKind.Action, "Install", ActionKind.Install),
            new(RowKind.Separator, "", Selectable: false),
            new(RowKind.Action, "Uninstall", ActionKind.Uninstall),
        };
        var s0 = new TuiState(Screen.Mod, 0, "reload");
        var (s1, _) = TuiModel.Reduce(s0, TuiKey.Down, rows);
        Assert.Equal(2, s1.Cursor);   // skipped the separator
    }

    [Fact] public void Reduce_enter_emits_selected_action()
    {
        var rows = new List<MenuRow> { new(RowKind.Action, "Install", ActionKind.Install) };
        var (_, act) = TuiModel.Reduce(new TuiState(Screen.Mod, 0, "reload"), TuiKey.Enter, rows);
        Assert.Equal(ActionKind.Install, act.Kind);
    }

    [Fact] public void Reduce_enter_on_toggle_inverts_state()
    {
        var rows = new List<MenuRow> {
            new(RowKind.Toggle, "Hide", ActionKind.Toggle, ToggleKey: "K", ToggleOn: true) };
        var (_, act) = TuiModel.Reduce(new TuiState(Screen.Mod, 0, "m"), TuiKey.Enter, rows);
        Assert.Equal(ActionKind.Toggle, act.Kind);
        Assert.Equal("K", act.ToggleKey);
        Assert.False(act.ToggleOn);   // was on → toggling requests off
    }

    [Fact] public void Reduce_back_from_list_quits()
    {
        var (_, act) = TuiModel.Reduce(new TuiState(Screen.List, 0, null), TuiKey.Back, new());
        Assert.Equal(ActionKind.Quit, act.Kind);
    }

    [Fact] public void Reduce_back_from_mod_returns_to_list()
    {
        var (s, act) = TuiModel.Reduce(new TuiState(Screen.Mod, 3, "reload"), TuiKey.Back, new());
        Assert.Equal(Screen.List, s.Screen);
        Assert.Equal(ActionKind.Back, act.Kind);
    }

    [Fact] public void DisplayName_strips_vrlf_suffix()
    {
        Assert.Equal("Reload", TuiModel.DisplayName("Reload VRLF Mod"));
        Assert.Equal("Custom Name", TuiModel.DisplayName("Custom Name"));   // off-convention → untouched
    }

    [Fact] public void ListRows_puts_selectable_vigembus_first_reflecting_status()
    {
        var mods = new List<ModStatus> { Installed() };

        var off = TuiModel.ListRows(new ListReport("net", mods, VigemInstalled: false));
        Assert.Equal(ActionKind.Vigem, off[0].Action);
        Assert.True(off[0].Selectable);
        Assert.Contains("ViGEmBus", off[0].Text);
        Assert.Contains("not installed", off[0].Text);
        Assert.Equal(RowKind.Separator, off[1].Kind);   // gap before the mods

        var on = TuiModel.ListRows(new ListReport("net", mods, VigemInstalled: true));
        Assert.Contains("installed", on[0].Text);
        Assert.DoesNotContain("not installed", on[0].Text);
    }

    [Fact] public void ListRows_aligns_status_column_across_rows()
    {
        var mods = new List<ModStatus> {
            new("a", "Short VRLF Mod", "1", "1", new() { new GameStatus(1, "g", true, "p", true, "1") }),
            new("b", "A Much Longer Mod Name VRLF Mod", "1", "1", new() { new GameStatus(2, "g", true, "p", true, "1") }),
        };
        var rows = TuiModel.ListRows(new ListReport("net", mods, false));
        var modRows = rows.Where(r => r.Action == ActionKind.Open).ToList();
        Assert.Equal(2, modRows.Count);
        Assert.DoesNotContain("VRLF Mod", modRows[0].Text);   // suffix stripped
        // status text begins at the same column in every row (padded to one width)
        int c0 = modRows[0].Text.IndexOf(TuiModel.RowStatus(mods[0]), StringComparison.Ordinal);
        int c1 = modRows[1].Text.IndexOf(TuiModel.RowStatus(mods[1]), StringComparison.Ordinal);
        Assert.True(c0 > 0);
        Assert.Equal(c0, c1);
    }
}
