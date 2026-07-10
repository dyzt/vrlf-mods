using System.IO;
using System.Text.Json;
using Xunit;

namespace VrlfMods.Tests;

public class RegistryTests
{
    const string Sample = """
    {
      "schema": 1,
      "vigembus": { "repo": "nefarius/ViGEmBus", "version": "v1.22.0" },
      "mods": [
        {
          "id": "heavy-fire", "name": "Heavy Fire VRLF Mod",
          "version": "1.0", "zip": "mods/heavy-fire/dist/heavy-fire-v1.0.zip",
          "sha256": "abc123",
          "games": [
            { "appid": 305980, "name": "Heavy Fire: Afghanistan" },
            { "appid": 385600, "name": "Heavy Fire: Shattered Spear" }
          ],
          "requiresVigembusForCoop": true,
          "notes": "co-op cfg"
        }
      ]
    }
    """;

    [Fact]
    public void Parses_and_finds_mod()
    {
        var reg = JsonSerializer.Deserialize(Sample, VrlfJson.Default.ModRegistry)!;
        Assert.Equal(1, reg.Schema);
        Assert.Equal("v1.22.0", reg.Vigembus.Version);
        var hf = reg.Find("heavy-fire")!;
        Assert.Equal("1.0", hf.Version);
        Assert.Equal(2, hf.Games.Count);
        Assert.Equal(385600, hf.Games[1].Appid);
        Assert.True(hf.RequiresVigembusForCoop);
        Assert.Null(reg.Find("nope"));
    }

    [Fact]
    public void Embedded_catalog_parses_and_has_six_mods()
    {
        using var s = typeof(ModRegistry).Assembly.GetManifestResourceStream("mods.json")!;
        using var r = new StreamReader(s);
        var reg = JsonSerializer.Deserialize(r.ReadToEnd(), VrlfJson.Default.ModRegistry)!;
        Assert.Equal(6, reg.Mods.Count);
        Assert.NotNull(reg.Find("heavy-fire"));
        Assert.NotNull(reg.Find("blue-estate"));
        // Blue Estate's crosshair toggle is a reversible native patch.
        var be = reg.Find("blue-estate")!;
        Assert.Equal("patch", be.Config!.Toggles.Single().Type);
    }
}
