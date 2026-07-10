using System.IO;
using Xunit;

namespace VrlfMods.Tests;

public class ReceiptTests
{
    static AppPaths TempPaths() =>
        new(Path.Combine(Path.GetTempPath(), "vrlf-rcpt-" + Guid.NewGuid().ToString("N")));

    [Fact]
    public void Save_then_Load_round_trips()
    {
        var paths = TempPaths();
        var store = new ReceiptStore(paths);
        var r = new Receipt("hotd2", "1.0", "3376690", @"D:\Games\HOTD2",
            new() { new InstalledFile("winhttp.dll", "aa"), new InstalledFile("BepInEx/core/x.dll", "bb") },
            new() { new BackupRef("winhttp.dll") },
            "2026-07-10T00:00:00Z");
        store.Save(r);

        var back = store.Load("hotd2", "3376690")!;
        Assert.Equal("1.0", back.Version);
        Assert.Equal(2, back.Files.Count);
        Assert.Single(back.Backups);
        Assert.Contains(back, store.All());
    }

    [Fact]
    public void Load_missing_returns_null()
        => Assert.Null(new ReceiptStore(TempPaths()).Load("nope", "0"));

    [Fact]
    public void Delete_removes_the_receipt()
    {
        var paths = TempPaths();
        var store = new ReceiptStore(paths);
        store.Save(new Receipt("reload", "1.0", "330370", @"C:\R", new(), new(), "t"));
        store.Delete("reload", "330370");
        Assert.Null(store.Load("reload", "330370"));
    }
}
