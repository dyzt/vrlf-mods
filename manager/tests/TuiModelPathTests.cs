using Xunit;

namespace VrlfMods.Tests;

/// <summary>The mod screen's game-folder row.</summary>
public class TuiModelPathTests
{
    static ModStatus Mod(bool detected, string? path, bool manual, string? installedVersion = null) =>
        new("demo", "Demo VRLF Mod", "1.0", installedVersion,
            new() { new GameStatus(1, "Demo Game", detected, path, installedVersion is not null,
                                   installedVersion, manual) });

    static MenuRow FolderRow(ModStatus m) =>
        TuiModel.ModRows(m, null).Single(r => r.Action == ActionKind.SetPath);

    [Fact]
    public void The_mod_screen_shows_which_folder_it_will_install_into()
    {
        var row = FolderRow(Mod(true, @"D:\Steam\Demo", manual: false));
        Assert.Contains(@"D:\Steam\Demo", row.Text);
        Assert.Contains("Steam", row.Text);
        Assert.True(row.Selectable);
    }

    [Fact]
    public void A_folder_the_user_chose_is_marked_as_theirs()
    {
        var row = FolderRow(Mod(true, @"E:\Demo", manual: true));
        Assert.Contains(@"E:\Demo", row.Text);
        Assert.Contains("you chose", row.Text);
    }

    [Fact]
    public void A_chosen_folder_that_has_gone_is_flagged_rather_than_shown_as_working()
    {
        var row = FolderRow(Mod(false, @"E:\Gone", manual: true));
        Assert.Contains(@"E:\Gone", row.Text);
        Assert.Contains("no longer", row.Text);
    }

    [Fact]
    public void An_undetected_game_invites_the_user_to_choose_a_folder()
    {
        var row = FolderRow(Mod(false, null, manual: false));
        Assert.Contains("not found", row.Text);
    }

    [Fact]
    public void When_the_game_is_not_found_the_folder_row_is_what_the_cursor_lands_on()
    {
        // Nothing else on this screen is actionable, and before the folder row existed the
        // screen offered the user nothing at all.
        var rows = TuiModel.ModRows(Mod(false, null, manual: false), null);
        Assert.Equal(ActionKind.SetPath, rows.First(r => r.Selectable).Action);
    }

    [Fact]
    public void When_the_game_is_found_install_still_comes_first()
    {
        var rows = TuiModel.ModRows(Mod(true, @"D:\Demo", manual: false), null);
        Assert.Equal(ActionKind.Install, rows.First(r => r.Selectable).Action);
    }

    [Fact]
    public void A_chosen_folder_can_be_cleared()
    {
        var rows = TuiModel.ModRows(Mod(true, @"E:\Demo", manual: true), null);
        var clear = rows.Single(r => r.Action == ActionKind.ClearPath);
        Assert.True(clear.Selectable);
        Assert.Equal(1, clear.Appid);
    }

    [Fact]
    public void There_is_nothing_to_clear_when_steam_found_the_game()
    {
        var rows = TuiModel.ModRows(Mod(true, @"D:\Demo", manual: false), null);
        Assert.DoesNotContain(rows, r => r.Action == ActionKind.ClearPath);
    }

    [Fact]
    public void A_stale_chosen_folder_can_still_be_cleared()
    {
        var rows = TuiModel.ModRows(Mod(false, @"E:\Gone", manual: true), null);
        Assert.Contains(rows, r => r.Action == ActionKind.ClearPath);
    }

    [Fact]
    public void The_folder_row_carries_the_game_it_belongs_to()
        => Assert.Equal(1, FolderRow(Mod(true, @"D:\Demo", manual: false)).Appid);

    [Fact]
    public void A_mod_covering_two_games_gets_a_folder_row_each_naming_the_game()
    {
        var m = new ModStatus("hf", "Heavy Fire", "1.0", null, new()
        {
            new GameStatus(305980, "Afghanistan", true, @"D:\A", false, null),
            new GameStatus(385600, "Shattered Spear", false, null, false, null),
        });
        var rows = TuiModel.ModRows(m, null).Where(r => r.Action == ActionKind.SetPath).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Contains("Afghanistan", rows[0].Text);
        Assert.Contains("Shattered Spear", rows[1].Text);
    }

    [Fact]
    public void Enter_on_the_folder_row_asks_to_set_a_path_for_that_game()
    {
        var rows = TuiModel.ModRows(Mod(false, null, manual: false), null);
        var i = rows.FindIndex(r => r.Action == ActionKind.SetPath);
        var (_, action) = TuiModel.Reduce(new TuiState(Screen.Mod, i, "demo"), TuiKey.Enter, rows);
        Assert.Equal(ActionKind.SetPath, action.Kind);
        Assert.Equal("demo", action.ModId);
        Assert.Equal(1, action.Appid);
    }
}
