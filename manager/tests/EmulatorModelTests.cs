using System.Text.Json;
using Xunit;

namespace VrlfMods.Tests;

public class EmulatorModelTests
{
    const string RegJson = """
    { "schema": 1, "vigembus": { "repo": "nefarius/ViGEmBus", "version": "v1.22.0" }, "mods": [],
      "emulators": [
        { "id": "dolphin", "name": "Dolphin", "processes": ["Dolphin"], "exe": ["Dolphin.exe"],
          "locate": ["%APPDATA%/Dolphin Emulator"], "marker": ["Config/Dolphin.ini"],
          "portable": { "flag": ["portable.txt"], "settings": "User" },
          "needs": "vigembus", "profile": "\"Dolphin\" on the Steam Workshop", "notes": "n",
          "options": [
            { "id": "base", "label": "Base config", "short": "Base", "version": "1.0",
              "zip": "emulators/dolphin/dist/dolphin-base-v1.0.zip", "sha256": "aa" },
            { "id": "calibration", "label": "Calibration pack", "short": "Calibration", "requires": "base",
              "version": "1.1", "zip": "z", "sha256": "bb" } ] } ] }
    """;

    [Fact]
    public void Registry_with_emulators_parses_every_field()
    {
        var reg = JsonSerializer.Deserialize(RegJson, VrlfJson.Default.ModRegistry)!;
        var d = reg.FindEmulator("DOLPHIN")!;
        Assert.Equal("Dolphin", d.Name);
        Assert.Equal(new[] { "Dolphin" }, d.Processes);
        Assert.Equal(new[] { "Dolphin.exe" }, d.Exe);
        Assert.Equal("User", d.Portable!.Settings);
        Assert.Equal("vigembus", d.Needs);
        Assert.Equal("base", d.Options[1].Requires);
        Assert.Equal("Calibration", d.Options[1].Short);
        Assert.Null(d.Options[0].Requires);
    }

    [Fact]
    public void Registry_without_emulators_has_an_empty_list()
    {
        var reg = JsonSerializer.Deserialize(
            """{ "schema": 1, "vigembus": { "repo": "r", "version": "v" }, "mods": [] }""",
            VrlfJson.Default.ModRegistry)!;
        Assert.Empty(reg.EmulatorList);
        Assert.Null(reg.FindEmulator("dolphin"));
    }

    [Theory]
    [InlineData("ini", "S", "k", "v", false, null)]
    [InlineData("mame", null, "k", "v", false, null)]
    [InlineData("ini", "S", null, null, true, null)]
    [InlineData("ini", null, "k", "v", false, "need a section")]
    [InlineData("mame", "S", "k", "v", false, "no section")]
    [InlineData("mame", null, null, null, true, "no section")]
    [InlineData("ini", "S", "k", "v", true, "either key + value or replace")]
    [InlineData("ini", "S", null, null, false, "either key + value or replace")]
    [InlineData("ini", "S", "k", null, false, "both key and value")]
    [InlineData("toml", "S", "k", "v", false, "unknown format")]
    [InlineData("ini", "S", "k", "caf\u00e9", false, "plain ASCII")]
    [InlineData("ini", "S", "k", "a\r\nb", false, "plain ASCII")]
    [InlineData("ini", "S", "a=b", "v", false, "'='")]
    [InlineData("ini", "S]", "k", "v", false, "']'")]
    public void EditSpec_Problem_names_what_is_wrong(string format, string? section, string? key, string? value,
                                                    bool replace, string? problem)
    {
        var e = new EditSpec("a.ini", format, section, key, value, replace ? new() { "x = 1" } : null);
        if (problem is null) Assert.Null(e.Problem());
        else Assert.Contains(problem, e.Problem());
    }

    [Theory]
    [InlineData(@"C:\x.ini")]
    [InlineData("../x.ini")]
    [InlineData(@"Config\..\..\x.ini")]
    [InlineData("")]
    public void EditSpec_Problem_rejects_files_outside_the_settings_folder(string file)
        => Assert.NotNull(new EditSpec(file, "ini", "S", "k", "v").Problem());

    [Fact]
    public void SettingsEdits_dispatches_each_kind_and_reverts_exactly()
    {
        const string Ini = "[A]\r\nx = 1\r\n";
        var t = SettingsText.Parse(Ini);
        var set = new EditSpec("a.ini", "ini", "A", "x", "2");
        var rep = new EditSpec("a.ini", "ini", "B", Replace: new() { "y = 3" });
        var p1 = SettingsEdits.Apply(t, set);
        var p2 = SettingsEdits.Apply(t, rep);
        Assert.Equal("[A]\r\nx = 2\r\n[B]\r\ny = 3\r\n", t.Render());
        SettingsEdits.Revert(t, rep, p2);
        SettingsEdits.Revert(t, set, p1);
        Assert.Equal(Ini, t.Render());

        var m = SettingsText.Parse("ctrlr none\n");
        var me = new EditSpec("mame.ini", "mame", Key: "ctrlr", Value: "vrlf");
        var p3 = SettingsEdits.Apply(m, me);
        Assert.Equal("ctrlr vrlf\n", m.Render());
        SettingsEdits.Revert(m, me, p3);
        Assert.Equal("ctrlr none\n", m.Render());
    }

    static AppPaths TempPaths() =>
        new(Path.Combine(Path.GetTempPath(), "vrlf-emumodel-" + Guid.NewGuid().ToString("N")));

    static EmulatorReceipt Sample(string emu = "dolphin", string opt = "base") => new(
        emu, opt, "1.0", @"C:\Lightgun\Dolphin\User",
        new() { new InstalledFile("Config/Profiles/Wiimote/VRLF-Dolphin-Base.ini", "ab") },
        new() { new BackupRef("Config/x.ini") },
        new()
        {
            new AppliedEdit(new EditSpec("Config/DSUClient.ini", "ini", "Server", "Enabled", "True"),
                new EditPrior(Lines: new() { "Enabled = False" }), false),
            new AppliedEdit(new EditSpec("Config/WiimoteNew.ini", "ini", "Wiimote1", Replace: new() { "Source = 1" }),
                new EditPrior(Section: new() { new SettingsLine("Source = 0", "\r\n"), new SettingsLine("x", "") }), false),
            new AppliedEdit(new EditSpec("Config/GFX.ini", "ini", "Settings", Replace: new() { "a = 1" }),
                new EditPrior(Section: new(), FinalEolAdded: true), true),
        },
        "2026-09-23T00:00:00.0000000Z");

    static string Json(EmulatorReceipt r) => JsonSerializer.Serialize(r, VrlfJson.Default.EmulatorReceipt);

    [Fact]
    public void Receipt_round_trips_through_the_store_with_every_prior_intact()
    {
        var store = new EmulatorReceiptStore(TempPaths());
        var r = Sample();
        store.Save(r);
        var back = store.Load("dolphin", "base")!;
        Assert.Equal(Json(r), Json(back));
        Assert.Null(back.Edits[1].Prior.Lines);
        Assert.NotNull(back.Edits[2].Prior.Section);             // present but empty is not absent
        Assert.Empty(back.Edits[2].Prior.Section!);
        Assert.Equal("\r\n", back.Edits[1].Prior.Section![0].Eol);
    }

    [Fact]
    public void ForEmulator_and_Delete_work_by_id()
    {
        var store = new EmulatorReceiptStore(TempPaths());
        store.Save(Sample("dolphin", "base"));
        store.Save(Sample("dolphin", "calibration"));
        store.Save(Sample("pcsx2", "base"));
        Assert.Equal(2, store.ForEmulator("dolphin").Count);
        store.Delete("dolphin", "base");
        Assert.Null(store.Load("dolphin", "base"));
        Assert.Single(store.ForEmulator("dolphin"));
    }

    [Fact]
    public void Mod_receipts_never_see_emulator_receipts()
    {
        var paths = TempPaths();
        new EmulatorReceiptStore(paths).Save(Sample());
        Assert.Empty(new ReceiptStore(paths).All());
        Assert.StartsWith(paths.ModsRoot, paths.EmuReceiptPath("dolphin", "base"));
        Assert.NotEqual(paths.ReceiptsDir, Path.GetDirectoryName(paths.EmuReceiptPath("dolphin", "base")));
    }
}
