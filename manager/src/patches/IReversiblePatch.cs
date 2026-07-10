namespace VrlfMods.Patches;

public enum PatchState { Off, On, UnsupportedBuild, Missing }

public interface IReversiblePatch
{
    string Name { get; }
    PatchState Detect(string targetPath);
    OpResult Apply(string targetPath, string gameKey);
    OpResult Revert(string targetPath, string gameKey);
}
