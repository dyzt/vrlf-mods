using System.Text;
using Xunit;

namespace VrlfMods.Tests;

public class SettingsTextTests
{
    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("a\r\n")]
    [InlineData("\r\n\r\n")]
    [InlineData("a\nb\r\nc")]
    public void Parse_then_Render_is_identity(string text)
        => Assert.Equal(text, SettingsText.Parse(text).Render());

    [Fact]
    public void Each_line_keeps_its_own_ending()
    {
        var t = SettingsText.Parse("a\nb\r\nc");
        Assert.Equal(new[] { "\n", "\r\n", "" }, t.Lines.Select(l => l.Eol));
    }

    [Theory]
    [InlineData("", "\r\n")]
    [InlineData("a\r\nb\r\n", "\r\n")]
    [InlineData("a\nb\n", "\n")]
    [InlineData("a\r\nb\nc\n", "\n")]
    public void Eol_is_the_majority_ending_and_CRLF_for_an_empty_file(string text, string eol)
        => Assert.Equal(eol, SettingsText.Parse(text).Eol);

    [Fact]
    public void IsBlank_ignores_blank_lines_and_comments()
    {
        Assert.True(SettingsText.Parse("\r\n# note\r\n; other\r\n").IsBlank);
        Assert.False(SettingsText.Parse("# note\r\n[A]\r\n").IsBlank);
    }

    [Fact]
    public void Load_and_Save_keep_the_BOM_and_every_other_byte()
    {
        var path = Path.Combine(Path.GetTempPath(), "vrlf-st-" + Guid.NewGuid().ToString("N") + ".ini");
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.ASCII.GetBytes("[A]\r\n# caf"))
            .Concat(new byte[] { 0xC3, 0xA9 })          // UTF-8 é, must survive untouched
            .Concat(Encoding.ASCII.GetBytes("\r\nx = 1")).ToArray();
        File.WriteAllBytes(path, bytes);

        var t = SettingsText.Load(path);
        Assert.True(t.Bom);
        Assert.Equal("[A]", t.Lines[0].Text);            // BOM is not part of the first line
        t.Save(path);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Load_of_a_missing_file_is_empty()
    {
        var t = SettingsText.Load(Path.Combine(Path.GetTempPath(), "vrlf-none-" + Guid.NewGuid().ToString("N")));
        Assert.Empty(t.Lines);
        Assert.False(t.Bom);
    }

    [Fact]
    public void Parse_detects_a_leading_BOM_character()
    {
        var t = SettingsText.Parse("﻿[A]\r\n");
        Assert.True(t.Bom);
        Assert.Equal("[A]", t.Lines[0].Text);
    }
}
