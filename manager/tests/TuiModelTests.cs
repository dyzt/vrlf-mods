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
}
