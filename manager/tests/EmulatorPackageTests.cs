using System.IO.Compression;
using System.Text.Json;
using Xunit;

namespace VrlfMods.Tests;

/// <summary>The real committed packages, installed into realistic settings folders and removed again.</summary>
public class EmulatorPackageTests
{
    static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !(File.Exists(Path.Combine(d.FullName, "mods.json"))
                                  && Directory.Exists(Path.Combine(d.FullName, "emulators"))))
            d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    static ModRegistry Registry() =>
        JsonSerializer.Deserialize(File.ReadAllBytes(Path.Combine(RepoRoot(), "mods.json")), VrlfJson.Default.ModRegistry)!;

    static byte[] Zip(EmulatorOption o) => File.ReadAllBytes(Path.Combine(RepoRoot(), o.Zip.Replace('/', Path.DirectorySeparatorChar)));

    // Settings folders as a user might have them: CRLF, a user's own per-game file, LF + no final
    // newline + a duplicate key for MAME, and an older vrlf.cfg the package replaces.
    static readonly Dictionary<string, Dictionary<string, string>> Fixtures = new()
    {
        ["dolphin"] = new()
        {
            ["Config/Dolphin.ini"] = "[General]\r\nISOPaths = 0\r\n",
            ["Config/DSUClient.ini"] = "[Server]\r\nEnabled = False\r\nEntries = \r\n",
            ["Config/WiimoteNew.ini"] = "[Wiimote1]\r\nDevice = DInput/0/Keyboard Mouse\r\nSource = 0\r\nButtons/A = `Click 0`\r\n[Wiimote2]\r\nSource = 0\r\n[BalanceBoard]\r\nSource = 0\r\n",
            ["Config/GFX.ini"] = "[Settings]\r\nHiresTextures = False\r\nInternalResolution = 3\r\n",
            ["GameSettings/RGSE8P.ini"] = "[Video_Settings]\r\nInternalResolution = 2\r\n",
        },
        ["pcsx2"] = new()
        {
            ["inis/PCSX2.ini"] = "[UI]\r\nSettingsVersion = 1\r\n\r\n[InputSources]\r\nSDL = true\r\nXInput = false\r\n\r\n[Pad]\r\nMultitapPort1 = false\r\n\r\n[Pad1]\r\nType = DualShock2\r\nCross = SDL-0/FaceSouth\r\n\r\n[USB1]\r\nType = None\r\n\r\n[USB2]\r\nType = None\r\n",
        },
        ["duckstation"] = new()
        {
            ["settings.ini"] = "[Main]\r\nSettingsVersion = 3\r\n\r\n[InputSources]\r\nSDL = true\r\nXInput = false\r\n\r\n[Pad1]\r\nType = AnalogController\r\nCross = SDL-0/A\r\n\r\n[Pad2]\r\nType = None\r\n",
        },
        ["mame"] = new()
        {
            ["mame.ini"] = "lightgun 1\nwindow 1\n\n#\n# CORE INPUT OPTIONS\n#\nlightgun            0\njoystick_deadzone   0.15",
            ["mame.exe"] = "MZ",
            ["ctrlr/vrlf.cfg"] = "<old/>",
        },
        ["flycast"] = new()
        {
            ["emu.cfg"] = "[config]\nDynarec.Enabled = yes\n\n[input]\nMouseSensitivity = 100\nRawInput = no\ndevice1 = 0\ndevice1.1 = 1\ndevice2 = 10\nmaple_sdl_mouse = 0\n\n[log]\nLogToFile = no\n",
            ["flycast.exe"] = "MZ",
            ["mappings/SDL_Default Mouse.cfg"] = "[digital]\nbind0 = 1:btn_b\n",
        },
        ["rpcs3"] = new()
        {
            ["config/config.yml"] = "Core:\n  PPU Decoder: Recompiler (LLVM)\nInput/Output:\n  Camera: \"Null\"\n  Camera type: Unknown\n  Keyboard: \"Null\"\n  Move: \"Null\"\n  Mouse: Basic\nLog:\n  Level: Warning\n",
            ["config/input_configs/active_input_configurations.yml"] = "Active Configurations:\n  global: Default\n",
            ["config/input_configs/global/Default.yml"] = "Player 1 Input:\n  Handler: XInput\n",
            ["rpcs3.exe"] = "MZ",
        },
        ["pcsx2x6"] = new()
        {
            ["inis/PCSX2.ini"] = "[UI]\r\nSettingsVersion = 1\r\n\r\n[InputSources]\r\nKeyboard = true\r\nSDL = true\r\n\r\n[Pad]\r\nMultitapPort1 = false\r\n\r\n[Pad1]\r\nType = DualShock2\r\n\r\n[USB1]\r\nType = None\r\n\r\n[USB2]\r\nType = None\r\n\r\n[JVS]\r\nTestMode = false\r\nVideoVoltage = true\r\n",
        },
    };

    [Fact]
    public void Every_emulator_zip_is_committed_and_matches_its_sha256()
    {
        var reg = Registry();
        Assert.Equal(new[] { "dolphin", "pcsx2", "duckstation", "mame", "flycast", "rpcs3", "pcsx2x6" }, reg.EmulatorList.Select(e => e.Id));
        foreach (var o in reg.EmulatorList.SelectMany(e => e.Options))
            Assert.Equal(o.Sha256, Installer.Sha256Hex(Zip(o)));
    }

    [Fact]
    public void The_embedded_registry_carries_the_same_emulators()
    {
        using var s = typeof(ModRegistry).Assembly.GetManifestResourceStream("mods.json")!;
        var embedded = JsonSerializer.Deserialize(s, VrlfJson.Default.ModRegistry)!;
        Assert.Equal(7, embedded.EmulatorList.Count);
    }

    // Dolphin uses Documents\Dolphin Emulator whenever that folder exists, so with both present
    // that is the one it reads and the one to suggest.
    [Fact]
    public void Dolphin_suggests_the_Documents_folder_before_AppData()
    {
        var dolphin = Registry().FindEmulator("dolphin")!;
        var appdata = EmuFixture.TempFolder("AppData");
        var docs = EmuFixture.TempFolder("Documents");
        foreach (var root in new[] { appdata, docs })
            EmuFixture.Write(Path.Combine(root, "Dolphin Emulator"), "Config/Dolphin.ini", "[General]\r\n");
        var folders = new EmulatorFolders(new GamePathStore(EmuFixture.TempPaths()),
            new FakeKnownFolders(new() { ["APPDATA"] = appdata, ["DOCUMENTS"] = docs }));

        Assert.Equal(Path.Combine(docs, "Dolphin Emulator"), folders.Suggest(dolphin));
    }

    // MAME reads only the first mame.ini on its inipath, and the setup edits the one beside mame.exe.
    [Fact]
    public void Mame_notes_say_where_mame_ini_must_live()
        => Assert.EndsWith(" Keep mame.ini next to mame.exe, not in ini/.", Registry().FindEmulator("mame")!.Notes);

    [Theory]
    [InlineData("dolphin")]
    [InlineData("pcsx2")]
    [InlineData("duckstation")]
    [InlineData("mame")]
    [InlineData("flycast")]
    [InlineData("rpcs3")]
    [InlineData("pcsx2x6")]
    public async Task Installs_every_option_then_uninstalls_to_the_original_bytes(string id)
    {
        var emu = Registry().FindEmulator(id)!;
        var paths = EmuFixture.TempPaths();
        var folder = EmuFixture.TempFolder(id + " [Lightgun] (portable)");
        foreach (var (rel, content) in Fixtures[id]) EmuFixture.Write(folder, rel, content);
        var (inst, receipts, _) = EmuFixture.MakeInstaller(paths, emu.Options.Select(o => (o, Zip(o))).ToArray());

        foreach (var o in emu.Options)
        {
            var res = await inst.Install(emu, o, folder);
            Assert.True(res.Ok, res.Message);
        }

        AssertInstalled(id, folder);

        foreach (var o in Enumerable.Reverse(emu.Options))
        {
            var res = inst.Uninstall(emu, receipts.Load(emu.Id, o.Id)!);
            Assert.True(res.Ok, res.Message);
        }

        foreach (var (rel, content) in Fixtures[id]) Assert.Equal(content, EmuFixture.Read(folder, rel));
        var left = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(folder, p).Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal);
        Assert.Equal(Fixtures[id].Keys.OrderBy(p => p, StringComparer.Ordinal), left);
    }

    static SettingsText S(string folder, string rel) => SettingsText.Load(EmuFixture.PathOf(folder, rel));

    // vrlf-virtual-gun pins lane N to VHF instance 2&56524c3N on every PC, and Flycast keys its
    // port assignment on "raw_mouse_" + that device id.
    static string FlycastLane(int lane) =>
        $"maple_raw_mouse_HID_DEVICE_SYSTEM_VHF#2&56524c3{lane}&0&0000#{{378de44c-56ef-11d1-bc8c-00a0c91405dd}}";

    static void AssertInstalled(string id, string folder)
    {
        switch (id)
        {
            case "dolphin":
                var w = S(folder, "Config/WiimoteNew.ini");
                Assert.Equal("XInput/0/Gamepad", IniEditor.Get(w, "Wiimote1", "Device"));
                Assert.Equal("1", IniEditor.Get(w, "Wiimote1", "Source"));
                Assert.NotEqual("`Click 0`", IniEditor.Get(w, "Wiimote1", "Buttons/A"));   // whole section replaced
                Assert.Equal("XInput/1/Gamepad", IniEditor.Get(w, "Wiimote2", "Device"));
                Assert.Equal("0", IniEditor.Get(w, "BalanceBoard", "Source"));
                Assert.Equal("True", IniEditor.Get(S(folder, "Config/DSUClient.ini"), "Server", "Enabled"));
                Assert.Equal("True", IniEditor.Get(S(folder, "Config/GFX.ini"), "Settings", "HiresTextures"));
                var game = S(folder, "GameSettings/RGSE8P.ini");
                Assert.StartsWith("VRLF-", IniEditor.Get(game, "Controls", "WiimoteProfile1"));
                Assert.Equal("2", IniEditor.Get(game, "Video_Settings", "InternalResolution"));
                Assert.True(Directory.EnumerateFiles(EmuFixture.PathOf(folder, "Load/Textures"), "*.png", SearchOption.AllDirectories).Any());
                break;
            case "pcsx2":
                var p = S(folder, "inis/PCSX2.ini");
                Assert.Equal("true", IniEditor.Get(p, "InputSources", "XInput"));
                Assert.Equal("true", IniEditor.Get(p, "InputSources", "SDL"));
                Assert.Equal("8", IniEditor.Get(p, "Pad", "PointerXScale"));
                Assert.Equal("guncon2", IniEditor.Get(p, "USB1", "Type"));
                Assert.Equal("XInput-1/A", IniEditor.Get(p, "USB2", "guncon2_Trigger"));
                Assert.Equal("SDL-0/FaceSouth", IniEditor.Get(p, "Pad1", "Cross"));
                break;
            case "duckstation":
                var d = S(folder, "settings.ini");
                Assert.Equal("false", IniEditor.Get(d, "InputSources", "SDL"));
                Assert.Equal("GunCon", IniEditor.Get(d, "Pad1", "Type"));
                Assert.Equal("XInput-1/A", IniEditor.Get(d, "Pad2", "Trigger"));
                Assert.Null(IniEditor.Get(d, "Pad1", "Cross"));
                break;
            case "mame":
                var m = S(folder, "mame.ini");
                Assert.Equal("vrlf", MameIniEditor.Get(m, "ctrlr"));
                Assert.Equal("1", MameIniEditor.Get(m, "lightgun"));
                Assert.Equal("0", MameIniEditor.Get(m, "joystick_deadzone"));
                Assert.Equal("dinput", MameIniEditor.Get(m, "keyboardprovider"));
                Assert.StartsWith("<?xml", EmuFixture.Read(folder, "ctrlr/vrlf.cfg"));
                break;
            case "flycast":
                var f = S(folder, "emu.cfg");
                Assert.Equal("yes", IniEditor.Get(f, "input", "RawInput"));
                Assert.Equal("7", IniEditor.Get(f, "input", "device1"));
                Assert.Equal("7", IniEditor.Get(f, "input", "device2"));
                Assert.Equal("1", IniEditor.Get(f, "input", "device1.1"));
                Assert.Equal("0", IniEditor.Get(f, "input", FlycastLane(0)));
                Assert.Equal("1", IniEditor.Get(f, "input", FlycastLane(1)));
                Assert.Contains("RawInput = yes\n", EmuFixture.Read(folder, "emu.cfg"));   // Flycast's own spacing
                foreach (var name in new[] { "RAW_HID-compliant mouse [HID_DEVICE_SYSTEM_VHF].cfg",
                                             "RAW_HID-compliant mouse [HID_DEVICE_SYSTEM_VHF]_arcade.cfg" })
                    Assert.Contains("1:reload", EmuFixture.Read(folder, "mappings/" + name));
                Assert.Equal("[digital]\nbind0 = 1:btn_b\n", EmuFixture.Read(folder, "mappings/SDL_Default Mouse.cfg"));
                break;
            case "rpcs3":
                var r = S(folder, "config/config.yml");
                Assert.Equal("Fake", YamlEditor.Get(r, "Input/Output", "Move"));
                Assert.Equal("Fake", YamlEditor.Get(r, "Input/Output", "Camera"));
                Assert.Equal("PS Eye", YamlEditor.Get(r, "Input/Output", "Camera type"));
                Assert.Equal("Basic", YamlEditor.Get(r, "Input/Output", "Mouse"));
                Assert.Equal("VRLF Move 2P",
                    YamlEditor.Get(S(folder, "config/input_configs/active_input_configurations.yml"), "Active Configurations", "global"));
                var input = EmuFixture.Read(folder, "config/input_configs/global/VRLF Move 2P.yml");
                Assert.Contains("Player 7 Input:", input);
                Assert.Contains("Left Pad Squircling Factor: 0", input);
                break;
            case "pcsx2x6":
                var x6 = S(folder, "inis/PCSX2.ini");
                Assert.Equal("true", IniEditor.Get(x6, "InputSources", "XInput"));
                Assert.Equal("true", IniEditor.Get(x6, "InputSources", "SDL"));
                Assert.Equal("8", IniEditor.Get(x6, "Pad", "PointerXScale"));
                Assert.Equal("guncon2", IniEditor.Get(x6, "USB1", "Type"));
                Assert.Equal("XInput-0", IniEditor.Get(x6, "USB1", "guncon2_Pointer"));   // the aim source
                Assert.Equal("XInput-1", IniEditor.Get(x6, "USB2", "guncon2_Pointer"));
                Assert.Equal("XInput-0/Back", IniEditor.Get(x6, "USB1", "guncon2_Select"));   // coin
                Assert.Equal("Keyboard/1", IniEditor.Get(x6, "JVS", "ToggleTestMode"));
                Assert.Equal("Keyboard/5", IniEditor.Get(x6, "JVS", "P1_Down"));
                Assert.Equal("false", IniEditor.Get(x6, "JVS", "TestMode"));
                Assert.Null(IniEditor.Get(x6, "JVS", "SindenBorderThickness"));
                Assert.Equal("DualShock2", IniEditor.Get(x6, "Pad1", "Type"));
                Assert.Contains("guncon2_Pointer = XInput-1", EmuFixture.Read(folder, "inputprofiles/VRLF GunCon2 2P (XInput).ini"));
                break;
        }
    }
}
