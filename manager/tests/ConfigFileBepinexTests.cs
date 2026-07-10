using Xunit;

namespace VrlfMods.Tests;

public class ConfigFileBepinexTests
{
    const string Cfg =
        "## Settings file\n\n" +
        "[General]\n\n" +
        "## Log everything.\n# Setting type: Boolean\n# Default value: true\nVerboseLog = false\n\n" +
        "[Aim]\n\n" +
        "## Absolute lightgun aim.\n# Setting type: Boolean\n# Default value: true\nAbsoluteAim = true\n\n" +
        "## P1 scale.\n# Setting type: Single\n# Default value: 1\nAimScale = 1\n";

    [Fact] public void Reads_section_scoped_bool()
    {
        Assert.True(ConfigFile.ParseBoolIn(Cfg, "bepinex", "Aim", "AbsoluteAim"));
        Assert.False(ConfigFile.ParseBoolIn(Cfg, "bepinex", "General", "VerboseLog"));
        Assert.Null(ConfigFile.ParseBoolIn(Cfg, "bepinex", "General", "AbsoluteAim")); // wrong section
    }

    [Fact] public void Set_flips_within_correct_section_preserving_format()
    {
        var outp = ConfigFile.SetBoolIn(Cfg, "bepinex", "Aim", "AbsoluteAim", false);
        Assert.Contains("AbsoluteAim = false", outp);
        Assert.Contains("VerboseLog = false", outp);         // General untouched
        Assert.Contains("AimScale = 1", outp);               // non-bool key untouched
        Assert.Contains("## Absolute lightgun aim.", outp);  // comment intact
    }

    [Fact] public void Help_lifts_the_double_hash_description()
    {
        Assert.Equal("Absolute lightgun aim.", ConfigFile.HelpIn(Cfg, "bepinex", "Aim", "AbsoluteAim"));
    }
}
