using System.IO;
using Xunit;

namespace VrlfMods.Tests;

public class AppPathsTests
{
    [Fact]
    public void ReceiptPath_combines_id_and_key()
    {
        var p = new AppPaths(@"C:\state");
        Assert.Equal(Path.Combine(@"C:\state", "receipts", "hotd2-3376690.json"),
                     p.ReceiptPath("hotd2", "3376690"));
    }

    [Fact]
    public void GameKeyForAppid_is_the_number()
        => Assert.Equal("305980", AppPaths.GameKeyForAppid(305980));

    [Fact]
    public void GameKeyForPath_is_stable_and_prefixed()
    {
        var a = AppPaths.GameKeyForPath(@"C:\Games\Reload\");
        var b = AppPaths.GameKeyForPath(@"c:/games/reload");
        Assert.StartsWith("custom-", a);
        Assert.Equal(15, a.Length);            // "custom-" + 8 hex
        Assert.Equal(a, b);                    // normalization makes these equal
    }

    [Fact]
    public void GameKeyForPath_resolves_relative_to_absolute()
    {
        var rel = "SomeGameDir";
        var abs = Path.GetFullPath(rel);
        Assert.Equal(AppPaths.GameKeyForPath(abs), AppPaths.GameKeyForPath(rel));
    }
}
