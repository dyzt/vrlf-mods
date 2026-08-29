using System.IO;
using Xunit;

namespace VrlfMods.Tests;

public class GamePathStoreTests
{
    static AppPaths TempPaths() =>
        new(Path.Combine(Path.GetTempPath(), "vrlf-gp-" + Guid.NewGuid().ToString("N")));

    [Fact]
    public void Set_then_Get_round_trips()
    {
        var store = new GamePathStore(TempPaths());
        store.Set(1694600, @"D:\Games\HOTD Remake");
        Assert.Equal(@"D:\Games\HOTD Remake", store.Get(1694600));
    }

    [Fact]
    public void Get_unset_appid_returns_null()
        => Assert.Null(new GamePathStore(TempPaths()).Get(305980));

    [Fact]
    public void Set_persists_across_store_instances()
    {
        var paths = TempPaths();
        new GamePathStore(paths).Set(330370, @"E:\Reload");
        Assert.Equal(@"E:\Reload", new GamePathStore(paths).Get(330370));
    }

    [Fact]
    public void Set_overwrites_an_earlier_path_for_the_same_game()
    {
        var store = new GamePathStore(TempPaths());
        store.Set(1, @"C:\old");
        store.Set(1, @"C:\new");
        Assert.Equal(@"C:\new", store.Get(1));
    }

    [Fact]
    public void Separate_games_keep_separate_paths()
    {
        var store = new GamePathStore(TempPaths());
        store.Set(1, @"C:\one");
        store.Set(2, @"C:\two");
        Assert.Equal(@"C:\one", store.Get(1));
        Assert.Equal(@"C:\two", store.Get(2));
    }

    [Fact]
    public void Clear_removes_only_the_named_game()
    {
        var paths = TempPaths();
        var store = new GamePathStore(paths);
        store.Set(1, @"C:\one");
        store.Set(2, @"C:\two");
        store.Clear(1);
        Assert.Null(new GamePathStore(paths).Get(1));
        Assert.Equal(@"C:\two", new GamePathStore(paths).Get(2));
    }

    [Fact]
    public void Clear_of_an_unset_game_is_a_no_op()
    {
        var store = new GamePathStore(TempPaths());
        store.Clear(999);
        Assert.Null(store.Get(999));
    }

    [Fact]
    public void A_corrupt_store_file_reads_as_empty_rather_than_throwing()
    {
        var paths = TempPaths();
        Directory.CreateDirectory(paths.ModsRoot);
        File.WriteAllText(paths.GamePathsPath, "{ not json at all");
        var store = new GamePathStore(paths);
        Assert.Null(store.Get(1));
        store.Set(1, @"C:\recovered");                       // and it repairs itself on the next write
        Assert.Equal(@"C:\recovered", new GamePathStore(paths).Get(1));
    }

    [Fact]
    public void All_lists_every_stored_override()
    {
        var store = new GamePathStore(TempPaths());
        store.Set(1, @"C:\one");
        store.Set(2, @"C:\two");
        Assert.Equal(2, store.All().Count);
        Assert.Equal(@"C:\one", store.All()["1"]);
    }
}
