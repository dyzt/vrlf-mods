using Xunit;

namespace VrlfMods.Tests;

/// <summary>How `path &lt;id&gt;` reads back the folder it is using, and who chose it.</summary>
public class OutputGamePathTests
{
    static GameStatus Status(bool detected, string? path, bool manual) =>
        new(1, "Demo Game", detected, path, false, null, manual);

    [Fact]
    public void A_steam_found_folder_says_so()
    {
        var text = Output.GamePathText(Status(true, @"D:\Steam\Demo", manual: false));
        Assert.Contains(@"D:\Steam\Demo", text);
        Assert.Contains("Steam", text);
    }

    [Fact]
    public void A_chosen_folder_is_marked_as_yours()
    {
        var text = Output.GamePathText(Status(true, @"E:\Demo", manual: true));
        Assert.Contains(@"E:\Demo", text);
        Assert.Contains("you chose", text);
    }

    [Fact]
    public void A_chosen_folder_that_has_gone_names_it_and_says_it_is_missing()
    {
        var text = Output.GamePathText(Status(false, @"E:\Gone", manual: true));
        Assert.Contains(@"E:\Gone", text);
        Assert.Contains("no longer", text);
    }

    [Fact]
    public void A_game_that_was_never_found_says_how_to_point_at_it()
    {
        var text = Output.GamePathText(Status(false, null, manual: false));
        Assert.Contains("not found", text);
        Assert.Contains("path", text);          // names the command that fixes it
    }
}
