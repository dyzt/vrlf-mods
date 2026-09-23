using Xunit;

namespace VrlfMods.Tests;

public class EmulatorFoldersTests
{
    static string Temp(string name = "root")
    {
        var d = Path.Combine(Path.GetTempPath(), "vrlf-emuf-" + Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(d);
        return d;
    }

    static void Touch(string dir, string rel)
    {
        var p = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "x");
    }

    static EmulatorEntry Pcsx2() => new("pcsx2", "PCSX2",
        new() { "pcsx2" }, new() { "pcsx2*.exe" }, new() { "%DOCUMENTS%/PCSX2" }, new() { "inis/PCSX2.ini" },
        new(), new PortableRule(new() { "portable.ini", "portable.txt" }));

    static EmulatorEntry Dolphin() => new("dolphin", "Dolphin",
        new() { "Dolphin" }, new() { "Dolphin.exe" }, new() { "%APPDATA%/Dolphin Emulator" }, new() { "Config/Dolphin.ini" },
        new(), new PortableRule(new() { "portable.txt" }, "User"));

    static (EmulatorFolders f, GamePathStore store, string docs, string appdata) Build()
    {
        var docs = Temp("Documents");
        var appdata = Temp("AppData");
        var store = new GamePathStore(new AppPaths(Temp("mods")));
        var known = new FakeKnownFolders(new() { ["DOCUMENTS"] = docs, ["APPDATA"] = appdata });
        return (new EmulatorFolders(store, known), store, docs, appdata);
    }

    [Fact]
    public void Expand_resolves_known_tokens_and_rejects_unknown_ones()
    {
        var (f, _, docs, _) = Build();
        Assert.Equal(Path.Combine(docs, "PCSX2"), f.Expand("%DOCUMENTS%/PCSX2"));
        Assert.Null(f.Expand("%NOPE%/PCSX2"));
    }

    [Fact]
    public void Suggest_returns_the_first_candidate_holding_a_marker()
    {
        var (f, _, docs, _) = Build();
        Assert.Null(f.Suggest(Pcsx2()));
        Directory.CreateDirectory(Path.Combine(docs, "PCSX2"));
        Assert.Null(f.Suggest(Pcsx2()));                        // folder without the marker
        Touch(Path.Combine(docs, "PCSX2"), "inis/PCSX2.ini");
        Assert.Equal(Path.Combine(docs, "PCSX2"), f.Suggest(Pcsx2()));
    }

    [Fact]
    public void Suggest_is_never_stored()
    {
        var (f, store, docs, _) = Build();
        Touch(Path.Combine(docs, "PCSX2"), "inis/PCSX2.ini");
        f.Suggest(Pcsx2());
        Assert.Null(f.Chosen(Pcsx2()));
        Assert.Null(store.Get("emu:pcsx2"));
    }

    [Fact]
    public void Resolve_accepts_a_settings_folder_and_strips_quotes()
    {
        var (f, _, _, _) = Build();
        var dir = Temp("PCSX2");
        Touch(dir, "inis/PCSX2.ini");
        var c = f.Resolve(Pcsx2(), "\"" + dir + "\"");
        Assert.True(c.Ok, c.Error);
        Assert.Equal(dir, c.Folder);
    }

    [Fact]
    public void Resolve_maps_a_portable_program_folder_to_its_settings_folder()
    {
        var (f, _, _, _) = Build();
        var prog = Temp("Dolphin-x64");
        Touch(prog, "Dolphin.exe");
        Touch(prog, "portable.txt");
        Touch(prog, "User/Config/Dolphin.ini");
        var c = f.Resolve(Dolphin(), prog);
        Assert.True(c.Ok, c.Error);
        Assert.Equal(Path.Combine(prog, "User"), c.Folder);
    }

    [Fact]
    public void Resolve_asks_for_one_run_when_a_portable_copy_has_no_settings_yet()
    {
        var (f, _, _, _) = Build();
        var prog = Temp("Dolphin-x64");
        Touch(prog, "Dolphin.exe");
        Touch(prog, "portable.txt");
        var c = f.Resolve(Dolphin(), prog);
        Assert.False(c.Ok);
        Assert.Contains("Run Dolphin once", c.Error);
    }

    [Fact]
    public void Resolve_refuses_a_non_portable_program_folder_and_names_where_its_settings_are()
    {
        var (f, _, docs, _) = Build();
        var prog = Temp("PCSX2-prog");
        Touch(prog, "pcsx2-qtx64-avx2.exe");
        var c = f.Resolve(Pcsx2(), prog);
        Assert.False(c.Ok);
        Assert.Contains(Path.Combine(docs, "PCSX2"), c.Error);
        Assert.Contains("portable PCSX2", c.Error);
    }

    [Fact]
    public void Resolve_refuses_an_unrelated_folder_and_a_missing_one()
    {
        var (f, _, _, _) = Build();
        var c = f.Resolve(Pcsx2(), Temp("Photos"));
        Assert.False(c.Ok);
        Assert.Contains("Not a PCSX2 settings folder", c.Error);
        var gone = f.Resolve(Pcsx2(), Path.Combine(Path.GetTempPath(), "vrlf-gone-" + Guid.NewGuid().ToString("N")));
        Assert.False(gone.Ok);
        Assert.Contains("not found", gone.Error);
        Assert.False(f.Resolve(Pcsx2(), "  ").Ok);
    }

    [Fact]
    public void Resolve_handles_spaces_brackets_and_parentheses()
    {
        var (f, _, _, _) = Build();
        var prog = Temp("PCSX2 [Lightgun] (portable)");
        Touch(prog, "pcsx2-qt.exe");
        Touch(prog, "portable.ini");
        Touch(prog, "inis/PCSX2.ini");
        var c = f.Resolve(Pcsx2(), prog);
        Assert.True(c.Ok, c.Error);
        Assert.Equal(prog, c.Folder);
    }

    [Fact]
    public void Choose_and_Forget_use_their_own_key_beside_game_paths()
    {
        var (f, store, _, _) = Build();
        store.Set(330370, @"E:\Reload");
        f.Choose(Pcsx2(), @"D:\PCSX2");
        Assert.Equal(@"D:\PCSX2", f.Chosen(Pcsx2()));
        f.Forget(Pcsx2());
        Assert.Null(f.Chosen(Pcsx2()));
        Assert.Equal(@"E:\Reload", store.Get(330370));
    }

    [Fact]
    public void Choose_stores_under_the_emu_prefixed_key()
    {
        var (f, store, _, _) = Build();
        f.Choose(Pcsx2(), @"D:\PCSX2");
        Assert.Equal(@"D:\PCSX2", store.Get("emu:pcsx2"));
        Assert.Null(store.Get("pcsx2"));   // not the bare id: a future emulator id must not collide with an appid string
    }
}
