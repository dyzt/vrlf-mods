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
}
