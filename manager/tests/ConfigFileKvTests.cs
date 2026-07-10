using Xunit;

namespace VrlfMods.Tests;

public class ConfigFileKvTests
{
    const string Cfg =
        "# Hide the crosshair.\n" +
        "# Second comment line.\n" +
        "RemoveCrosshair=true\n" +
        "\n" +
        "AimGain=0.4\n" +
        "VerboseLog = false\r\n";

    [Fact] public void Reads_bool_true_and_false()
    {
        Assert.True(ConfigFile.ParseBoolIn(Cfg, "kv", null, "RemoveCrosshair"));
        Assert.False(ConfigFile.ParseBoolIn(Cfg, "kv", null, "VerboseLog"));
        Assert.Null(ConfigFile.ParseBoolIn(Cfg, "kv", null, "Missing"));
    }

    [Fact] public void Set_flips_only_the_target_line_preserving_everything_else()
    {
        var outp = ConfigFile.SetBoolIn(Cfg, "kv", null, "RemoveCrosshair", false);
        Assert.Contains("RemoveCrosshair=false", outp);
        Assert.Contains("# Hide the crosshair.", outp);      // comments intact
        Assert.Contains("AimGain=0.4", outp);                // unrelated key untouched
        Assert.Contains("VerboseLog = false\r\n", outp);     // CRLF + spacing preserved
    }

    [Fact] public void Set_is_idempotent_and_preserves_crlf_on_target()
    {
        var once = ConfigFile.SetBoolIn(Cfg, "kv", null, "VerboseLog", true);
        Assert.Contains("VerboseLog = true\r\n", once);      // spacing + CRLF kept
        var twice = ConfigFile.SetBoolIn(once, "kv", null, "VerboseLog", true);
        Assert.Equal(once, twice);
    }

    [Fact] public void Set_appends_when_key_absent()
    {
        var outp = ConfigFile.SetBoolIn("A=1\n", "kv", null, "NewKey", true);
        Assert.Contains("NewKey=true", outp);
        Assert.Contains("A=1", outp);
    }

    [Fact] public void Help_lifts_contiguous_comment_block_above_key()
    {
        var help = ConfigFile.HelpIn(Cfg, "kv", null, "RemoveCrosshair");
        Assert.Equal("Hide the crosshair. Second comment line.", help);
    }
}
