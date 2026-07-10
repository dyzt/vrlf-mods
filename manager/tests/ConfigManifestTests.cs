using System.Text.Json;
using Xunit;

namespace VrlfMods.Tests;

public class ConfigManifestTests
{
    const string Sample = """
    {
      "schema": 1,
      "vigembus": { "repo": "nefarius/ViGEmBus", "version": "v1.22.0" },
      "mods": [
        { "id": "reload", "name": "Reload", "version": "1.1.0",
          "zip": "mods/reload/dist/reload-v1.1.0.zip", "sha256": "abc",
          "games": [ { "appid": 330370, "name": "Reload" } ],
          "requiresVigembusForCoop": false, "notes": null,
          "config": {
            "file": "reload_vrlf.cfg", "format": "kv",
            "toggles": [
              { "key": "RemoveCrosshair", "label": "Hide crosshair", "help": "hides it" },
              { "type": "patch", "patch": "blue-estate-nocrosshair",
                "label": "Hide crosshair (patch)", "target": "BEGame/COOKEDPCCONSOLE/BEGame.upk" }
            ]
          }
        },
        { "id": "bbh", "name": "BBH", "version": "1.0.0",
          "zip": "z", "sha256": "d", "games": [ { "appid": 1, "name": "g" } ],
          "requiresVigembusForCoop": false, "notes": null }
      ]
    }
    """;

    [Fact]
    public void Parses_config_block_and_leaves_absent_config_null()
    {
        var reg = JsonSerializer.Deserialize(Sample, VrlfJson.Default.ModRegistry)!;
        var reload = reg.Find("reload")!;
        Assert.NotNull(reload.Config);
        Assert.Equal("reload_vrlf.cfg", reload.Config!.File);
        Assert.Equal("kv", reload.Config.Format);
        Assert.Equal(2, reload.Config.Toggles.Count);
        Assert.Equal("Hide crosshair", reload.Config.Toggles[0].Label);
        Assert.Null(reload.Config.Toggles[0].Type);               // defaults to cfg
        Assert.Equal("patch", reload.Config.Toggles[1].Type);
        Assert.Equal("blue-estate-nocrosshair", reload.Config.Toggles[1].Patch);
        Assert.Null(reg.Find("bbh")!.Config);                      // absent → null
    }
}
