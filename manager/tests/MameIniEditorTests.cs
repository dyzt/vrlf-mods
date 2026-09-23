using Xunit;

namespace VrlfMods.Tests;

public class MameIniEditorTests
{
    // Shaped like James's live mame.ini: LF, a duplicate key, no final newline.
    const string Mame =
        "lightgun 1\nwindow 1\n\n#\n# CORE INPUT OPTIONS\n#\n" +
        "lightgun            0\nlightgun_device     mouse\njoystick_deadzone   0.15";

    static SettingsText T(string s) => SettingsText.Parse(s);

    [Fact]
    public void Set_keeps_the_padding_and_revert_restores_every_byte()
    {
        var t = T(Mame);
        var p = MameIniEditor.Set(t, "joystick_deadzone", "0");
        Assert.EndsWith("\njoystick_deadzone   0", t.Render());
        MameIniEditor.RevertSet(t, "joystick_deadzone", p);
        Assert.Equal(Mame, t.Render());
    }

    [Fact]
    public void A_key_never_matches_a_longer_key_with_the_same_start()
    {
        var t = T(Mame);
        var p = MameIniEditor.Set(t, "lightgun", "1");
        Assert.Contains("lightgun_device     mouse", t.Render());
        Assert.Equal("mouse", MameIniEditor.Get(t, "lightgun_device"));
        MameIniEditor.RevertSet(t, "lightgun", p);
        Assert.Equal(Mame, t.Render());
    }

    [Fact]
    public void Every_duplicate_is_set_and_Get_reads_the_last()
    {
        var t = T(Mame);
        Assert.Equal("0", MameIniEditor.Get(t, "lightgun"));
        var p = MameIniEditor.Set(t, "lightgun", "1");
        Assert.StartsWith("lightgun 1\n", t.Render());
        Assert.Contains("\nlightgun            1\n", t.Render());
        Assert.Equal(new[] { "lightgun 1", "lightgun            0" }, p.Lines);
    }

    [Fact]
    public void A_missing_key_is_appended_padded_and_revert_removes_it_exactly()
    {
        var t = T(Mame);
        var p = MameIniEditor.Set(t, "ctrlr", "vrlf");
        Assert.Equal(Mame + "\n" + "ctrlr".PadRight(25) + " vrlf\n", t.Render());
        Assert.True(p.FinalEolAdded);
        MameIniEditor.RevertSet(t, "ctrlr", p);
        Assert.Equal(Mame, t.Render());
    }

    [Fact]
    public void Comment_lines_are_never_matched()
    {
        var t = T("# lightgun 5\nlightgun 0\n");
        MameIniEditor.Set(t, "lightgun", "1");
        Assert.Equal("# lightgun 5\nlightgun 1\n", t.Render());
    }

    [Fact]
    public void Revert_after_a_rewrite_uses_the_value_that_was_in_force()
    {
        var t = T(Mame);
        var p = MameIniEditor.Set(t, "lightgun", "1");
        var rewritten = T("lightgun 1\nwindow 1\n");             // one copy left
        MameIniEditor.RevertSet(rewritten, "lightgun", p);
        Assert.Equal("0", MameIniEditor.Get(rewritten, "lightgun"));
    }

    [Fact]
    public void Get_of_a_missing_key_is_null()
        => Assert.Null(MameIniEditor.Get(T(Mame), "ctrlr"));
}
