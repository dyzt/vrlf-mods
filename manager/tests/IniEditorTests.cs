using Xunit;

namespace VrlfMods.Tests;

public class IniEditorTests
{
    const string Pcsx2 =
        "[UI]\r\nTheme = dark\r\n\r\n" +
        "[InputSources]\r\nSDL = true\r\nXInput = false\r\n\r\n" +
        "[Pad]\r\nMultitapPort1 = false\r\n";

    static SettingsText T(string s) => SettingsText.Parse(s);

    [Fact]
    public void Set_changes_only_the_value_and_revert_restores_every_byte()
    {
        var t = T(Pcsx2);
        var prior = IniEditor.Set(t, "InputSources", "XInput", "true");
        Assert.Equal(Pcsx2.Replace("XInput = false", "XInput = true"), t.Render());
        Assert.Equal(new[] { "XInput = false" }, prior.Lines);
        IniEditor.RevertSet(t, "InputSources", "XInput", prior);
        Assert.Equal(Pcsx2, t.Render());
    }

    [Fact]
    public void Set_inserts_a_missing_key_before_the_blank_separator()
    {
        var t = T(Pcsx2);
        var prior = IniEditor.Set(t, "InputSources", "DInput", "false");
        Assert.Equal(Pcsx2.Replace("XInput = false\r\n", "XInput = false\r\nDInput = false\r\n"), t.Render());
        Assert.Null(prior.Lines);
        IniEditor.RevertSet(t, "InputSources", "DInput", prior);
        Assert.Equal(Pcsx2, t.Render());
    }

    [Fact]
    public void Set_creates_a_missing_section_at_the_end()
    {
        var t = T(Pcsx2);
        var prior = IniEditor.Set(t, "USB1", "Type", "guncon2");
        Assert.Equal(Pcsx2 + "[USB1]\r\nType = guncon2\r\n", t.Render());
        Assert.True(prior.SectionCreated);
        IniEditor.RevertSet(t, "USB1", "Type", prior);
        Assert.Equal(Pcsx2, t.Render());
    }

    const string NoFinalEol = "[A]\r\nx = 1\r\n[B]\r\ny = 2";

    [Fact]
    public void A_file_without_a_final_newline_round_trips_for_every_edit_kind()
    {
        var t = T(NoFinalEol);
        var p1 = IniEditor.Set(t, "B", "z", "3");
        Assert.Equal("[A]\r\nx = 1\r\n[B]\r\ny = 2\r\nz = 3\r\n", t.Render());
        Assert.True(p1.FinalEolAdded);
        IniEditor.RevertSet(t, "B", "z", p1);
        Assert.Equal(NoFinalEol, t.Render());

        var p2 = IniEditor.Set(t, "C", "k", "v");
        Assert.Equal(NoFinalEol + "\r\n[C]\r\nk = v\r\n", t.Render());
        IniEditor.RevertSet(t, "C", "k", p2);
        Assert.Equal(NoFinalEol, t.Render());

        var p3 = IniEditor.Replace(t, "B", new[] { "q = 9" });
        Assert.Equal("[A]\r\nx = 1\r\n[B]\r\nq = 9\r\n", t.Render());
        IniEditor.RevertReplace(t, "B", p3);
        Assert.Equal(NoFinalEol, t.Render());
    }

    [Fact]
    public void Inserted_lines_use_the_files_majority_ending()
    {
        var t = T("[A]\r\nx = 1\n[B]\ny = 2\n");
        IniEditor.Set(t, "A", "k", "v");
        Assert.Equal("[A]\r\nx = 1\nk = v\n[B]\ny = 2\n", t.Render());
    }

    [Fact]
    public void Keys_with_symbols_and_spaces_match_only_their_own_line()
    {
        const string W = "[Wiimote1]\r\nButtons/- = `Back`\r\nButtons/+ = `Start`\r\nIR/Total Yaw = 21.\r\n";
        var t = T(W);
        Assert.Equal("21.", IniEditor.Get(t, "Wiimote1", "IR/Total Yaw"));
        var p = IniEditor.Set(t, "Wiimote1", "Buttons/+", "`Button X`");
        Assert.Equal(W.Replace("Buttons/+ = `Start`", "Buttons/+ = `Button X`"), t.Render());
        Assert.Equal("`Back`", IniEditor.Get(t, "Wiimote1", "Buttons/-"));
        IniEditor.RevertSet(t, "Wiimote1", "Buttons/+", p);
        Assert.Equal(W, t.Render());
    }

    [Fact]
    public void Every_duplicate_occurrence_is_set_and_each_is_restored()
    {
        const string D = "[A]\r\nk = 1\r\nk  =  2\r\n";
        var t = T(D);
        var p = IniEditor.Set(t, "A", "k", "9");
        Assert.Equal("[A]\r\nk = 9\r\nk  =  9\r\n", t.Render());
        IniEditor.RevertSet(t, "A", "k", p);
        Assert.Equal(D, t.Render());
    }

    [Fact]
    public void Comment_lines_are_never_matched()
    {
        var t = T("[InputSources]\r\n# XInput = true\r\nXInput = false\r\n");
        IniEditor.Set(t, "InputSources", "XInput", "true");
        Assert.Equal("[InputSources]\r\n# XInput = true\r\nXInput = true\r\n", t.Render());
    }

    [Fact]
    public void Section_and_key_names_match_case_insensitively()
    {
        var t = T("[inputsources]\r\nxinput = false\r\n");
        IniEditor.Set(t, "InputSources", "XInput", "true");
        Assert.Equal("[inputsources]\r\nxinput = true\r\n", t.Render());
    }

    const string Duck = "[Pad1]\r\nType = AnalogController\r\nCross = SDL-0/A\r\n\r\n[Pad2]\r\nType = None\r\n";

    [Fact]
    public void Replace_swaps_the_body_keeps_the_separator_and_reverts_exactly()
    {
        var t = T(Duck);
        var p = IniEditor.Replace(t, "Pad1", new[] { "Type = GunCon", "Trigger = XInput-0/A" });
        Assert.Equal("[Pad1]\r\nType = GunCon\r\nTrigger = XInput-0/A\r\n\r\n[Pad2]\r\nType = None\r\n", t.Render());
        Assert.Equal(new[] { "Type = AnalogController", "Cross = SDL-0/A" }, p.Section!.Select(l => l.Text));
        IniEditor.RevertReplace(t, "Pad1", p);
        Assert.Equal(Duck, t.Render());
    }

    [Fact]
    public void Replace_of_a_missing_section_appends_it_and_revert_removes_it()
    {
        var t = T(Duck);
        var p = IniEditor.Replace(t, "Pad3", new[] { "Type = GunCon" });
        Assert.Equal(Duck + "[Pad3]\r\nType = GunCon\r\n", t.Render());
        Assert.Null(p.Section);
        IniEditor.RevertReplace(t, "Pad3", p);
        Assert.Equal(Duck, t.Render());
    }

    [Fact]
    public void Replace_of_an_empty_last_header_without_newline_reverts_exactly()
    {
        const string S = "[A]\r\nx = 1\r\n[B]";
        var t = T(S);
        var p = IniEditor.Replace(t, "B", new[] { "y = 2" });
        Assert.Equal("[A]\r\nx = 1\r\n[B]\r\ny = 2\r\n", t.Render());
        Assert.Empty(p.Section!);
        IniEditor.RevertReplace(t, "B", p);
        Assert.Equal(S, t.Render());
    }

    // Regression: the restored body's last line had no ending (it was the file's last), so a
    // section added after it since was glued on: "Type = None[Achievements]", and that header's
    // keys then belonged to our section.
    [Fact]
    public void RevertReplace_keeps_a_section_added_after_a_last_section_without_newline()
    {
        var t = T("[A]\r\nx = 1\r\n[USB2]\r\nType = None");
        var p = IniEditor.Replace(t, "USB2", new[] { "Type = guncon2" });
        t.Lines.Add(new SettingsLine("[Achievements]", "\r\n"));      // the emulator adds a section
        t.Lines.Add(new SettingsLine("Enabled = true", "\r\n"));

        IniEditor.RevertReplace(t, "USB2", p);

        var saved = T(t.Render());                                     // what the file on disk reads as
        Assert.Equal(new[] { "Type = None" }, IniEditor.SectionBody(saved, "USB2"));
        Assert.Equal("true", IniEditor.Get(saved, "Achievements", "Enabled"));
        Assert.Contains("Type = None\r\n[Achievements]", t.Render());
    }

    // Same regression through the header: undoing the ending given to an empty last header
    // glued the next section on: "[USB1][B]".
    [Fact]
    public void RevertReplace_keeps_a_section_added_after_an_empty_last_header()
    {
        var t = T("[A]\r\nx = 1\r\n[USB1]");
        var p = IniEditor.Replace(t, "USB1", new[] { "Type = guncon2" });
        t.Lines.Add(new SettingsLine("[B]", "\r\n"));
        t.Lines.Add(new SettingsLine("k = v", "\r\n"));

        IniEditor.RevertReplace(t, "USB1", p);

        var saved = T(t.Render());                                     // what the file on disk reads as
        Assert.Equal(Array.Empty<string>(), IniEditor.SectionBody(saved, "USB1"));
        Assert.Equal("v", IniEditor.Get(saved, "B", "k"));
        Assert.Contains("[USB1]\r\n[B]", t.Render());
    }

    [Fact]
    public void Revert_finds_our_settings_after_the_emulator_rewrote_the_file()
    {
        var t = T(Pcsx2);
        var pX = IniEditor.Set(t, "InputSources", "XInput", "true");       // existed as false
        var pY = IniEditor.Set(t, "Pad", "PointerXScale", "8");           // absent before
        var pU = IniEditor.Replace(t, "USB1", new[] { "Type = guncon2" }); // absent section

        // The emulator saves on exit: sections reordered, spacing normalised.
        var rewritten = T(
            "[USB1]\r\nType=guncon2\r\n[Pad]\r\nPointerXScale=8\r\nMultitapPort1=false\r\n" +
            "[InputSources]\r\nXInput=true\r\nSDL=true\r\n[UI]\r\nTheme=dark\r\n");
        IniEditor.RevertReplace(rewritten, "USB1", pU);
        IniEditor.RevertSet(rewritten, "Pad", "PointerXScale", pY);
        IniEditor.RevertSet(rewritten, "InputSources", "XInput", pX);

        Assert.Null(IniEditor.SectionBody(rewritten, "USB1"));
        Assert.Null(IniEditor.Get(rewritten, "Pad", "PointerXScale"));
        Assert.Equal("false", IniEditor.Get(rewritten, "InputSources", "XInput"));
        Assert.Equal("true", IniEditor.Get(rewritten, "InputSources", "SDL"));
        Assert.Equal("false", IniEditor.Get(rewritten, "Pad", "MultitapPort1"));
        Assert.Equal("dark", IniEditor.Get(rewritten, "UI", "Theme"));
    }

    [Fact]
    public void Revert_puts_a_key_back_that_was_deleted_since()
    {
        var t = T(Pcsx2);
        var p = IniEditor.Set(t, "InputSources", "XInput", "true");
        var gone = T(Pcsx2.Replace("XInput = false\r\n", ""));
        IniEditor.RevertSet(gone, "InputSources", "XInput", p);
        Assert.Equal("false", IniEditor.Get(gone, "InputSources", "XInput"));
    }

    [Fact]
    public void Get_and_SectionBody_report_absence_as_null()
    {
        var t = T(Pcsx2);
        Assert.Null(IniEditor.Get(t, "Nope", "x"));
        Assert.Null(IniEditor.Get(t, "UI", "Nope"));
        Assert.Null(IniEditor.SectionBody(t, "Nope"));
        Assert.Equal(new[] { "SDL = true", "XInput = false" }, IniEditor.SectionBody(t, "InputSources"));
    }
}
