using System.IO.Compression;
using System.Text;

namespace VrlfMods.Tests;

using VrlfMods;

public sealed class FakeProcessProbe : IProcessProbe
{
    public List<string> Running { get; } = new();
    public IEnumerable<string> RunningProcessNames() => Running;
}

/// <summary>Temp folders, packages and a wired installer for the emulator tests. File contents
/// go through Latin-1 so a test string maps to exactly the bytes on disk.</summary>
public static class EmuFixture
{
    public static AppPaths TempPaths() =>
        new(Path.Combine(Path.GetTempPath(), "vrlf-emu-" + Guid.NewGuid().ToString("N")));

    public static string TempFolder(string name = "Emu")
    {
        var d = Path.Combine(Path.GetTempPath(), "vrlf-emudir-" + Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(d);
        return d;
    }

    public static string PathOf(string folder, string rel) =>
        Path.Combine(folder, rel.Replace('/', Path.DirectorySeparatorChar));

    public static void Write(string folder, string rel, string content)
    {
        var p = PathOf(folder, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, Encoding.Latin1.GetBytes(content));
    }

    public static string Read(string folder, string rel) =>
        Encoding.Latin1.GetString(File.ReadAllBytes(PathOf(folder, rel)));

    public static byte[] Package(string settingsJson, params (string name, string content)[] files)
    {
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (n, c) in files)
                using (var s = z.CreateEntry("files/" + n).Open()) s.Write(Encoding.Latin1.GetBytes(c));
            using (var s = z.CreateEntry("settings.json").Open()) s.Write(Encoding.UTF8.GetBytes(settingsJson));
        }
        return ms.ToArray();
    }

    public static EmulatorOption Option(string id, byte[] zip, string version = "1.0", string? requires = null) =>
        new(id, id + " label", version, $"emulators/testemu/dist/testemu-{id}-v{version}.zip",
            Installer.Sha256Hex(zip), requires, id + " short");

    public static EmulatorEntry Entry(params EmulatorOption[] options) => new(
        "testemu", "TestEmu", new() { "testemu" }, new() { "testemu*.exe" },
        new() { "%DOCUMENTS%/TestEmu" }, new() { "emu.ini" }, options.ToList(),
        new PortableRule(new() { "portable.txt" }), "vigembus", "\"Test\" on the Steam Workshop", null);

    public static (EmulatorInstaller inst, EmulatorReceiptStore receipts, FakeProcessProbe probe) MakeInstaller(
        AppPaths paths, params (EmulatorOption opt, byte[] zip)[] served)
    {
        var map = served.ToDictionary(s => EmulatorInstaller.ZipUrl(s.opt), s => (byte[]?)s.zip);
        var receipts = new EmulatorReceiptStore(paths);
        var probe = new FakeProcessProbe();
        return (new EmulatorInstaller(new FakeHttpFetcher(map), paths, receipts, probe), receipts, probe);
    }
}
