using Xunit;

namespace VrlfMods.Tests;

public class YamlEditorTests
{
    // Shaped like RPCS3's config.yml: LF, two-space children, a same-named key in another
    // section, a key that is a prefix of its neighbour, and a deeper block.
    const string Config =
        "Core:\n  PPU Decoder: Recompiler (LLVM)\n  Move: core\n" +
        "Input/Output:\n  Camera: \"Null\"\n  Camera type: Unknown\n  Keyboard: \"Null\"\n  Move: \"Null\"\n" +
        "Log:\n  Deep:\n    Move: deep\n";

    static SettingsText T(string s) => SettingsText.Parse(s);

    [Fact]
    public void Set_changes_only_that_sections_value_and_revert_restores_every_byte()
    {
        var t = T(Config);
        var prior = YamlEditor.Set(t, "Input/Output", "Move", "Fake");
        Assert.Equal(Config.Replace("  Move: \"Null\"", "  Move: Fake"), t.Render());
        Assert.Equal(new[] { "  Move: \"Null\"" }, prior.Lines);
        YamlEditor.RevertSet(t, "Input/Output", "Move", prior);
        Assert.Equal(Config, t.Render());
    }

    [Fact]
    public void A_key_never_matches_a_longer_key_it_starts()
    {
        var t = T(Config);
        YamlEditor.Set(t, "Input/Output", "Camera", "Fake");
        Assert.Equal(Config.Replace("  Camera: \"Null\"", "  Camera: Fake"), t.Render());
        Assert.Equal("Unknown", YamlEditor.Get(t, "Input/Output", "Camera type"));
    }

    [Fact]
    public void Set_skips_a_deeper_block_and_inserts_at_the_childrens_indent()
    {
        var t = T(Config);
        var prior = YamlEditor.Set(t, "Log", "Move", "x");
        Assert.Equal(Config + "  Move: x\n", t.Render());
        Assert.Null(prior.Lines);
        YamlEditor.RevertSet(t, "Log", "Move", prior);
        Assert.Equal(Config, t.Render());
    }

    [Fact]
    public void Set_inserts_a_missing_key_at_the_end_of_its_section()
    {
        var t = T(Config);
        var prior = YamlEditor.Set(t, "Input/Output", "Mouse", "Basic");
        Assert.Equal(Config.Replace("  Move: \"Null\"\n", "  Move: \"Null\"\n  Mouse: Basic\n"), t.Render());
        YamlEditor.RevertSet(t, "Input/Output", "Mouse", prior);
        Assert.Equal(Config, t.Render());
    }

    [Fact]
    public void Set_creates_a_missing_section_at_the_end()
    {
        var t = T(Config);
        var prior = YamlEditor.Set(t, "Active Configurations", "global", "VRLF Move 2P");
        Assert.Equal(Config + "Active Configurations:\n  global: VRLF Move 2P\n", t.Render());
        Assert.True(prior.SectionCreated);
        YamlEditor.RevertSet(t, "Active Configurations", "global", prior);
        Assert.Equal(Config, t.Render());
    }

    [Fact]
    public void A_file_without_a_final_newline_round_trips()
    {
        const string text = "A:\n  x: 1";
        var t = T(text);
        var prior = YamlEditor.Set(t, "A", "y", "2");
        Assert.Equal("A:\n  x: 1\n  y: 2\n", t.Render());
        Assert.True(prior.FinalEolAdded);
        YamlEditor.RevertSet(t, "A", "y", prior);
        Assert.Equal(text, t.Render());
    }

    // RPCS3 re-sorts and rewrites config.yml on exit, so undo finds the key wherever it now is.
    [Fact]
    public void Revert_finds_the_key_after_the_emulator_rewrote_the_file()
    {
        var t = T(Config);
        var prior = YamlEditor.Set(t, "Input/Output", "Move", "Fake");
        var rewritten = T("Input/Output:\n  Move: Fake\n  Camera: \"Null\"\nCore:\n  Move: core\n");
        YamlEditor.RevertSet(rewritten, "Input/Output", "Move", prior);
        Assert.Equal("Input/Output:\n  Move: \"Null\"\n  Camera: \"Null\"\nCore:\n  Move: core\n", rewritten.Render());
    }

    [Fact]
    public void Get_reads_a_value_or_null()
    {
        var t = T(Config);
        Assert.Equal("Recompiler (LLVM)", YamlEditor.Get(t, "Core", "PPU Decoder"));
        Assert.Null(YamlEditor.Get(t, "Core", "Missing"));
        Assert.Null(YamlEditor.Get(t, "Nope", "Move"));
    }
}
