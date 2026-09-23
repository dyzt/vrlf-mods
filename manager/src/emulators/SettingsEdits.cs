namespace VrlfMods;

/// <summary>Routes an <see cref="EditSpec"/> to the editor for its format.</summary>
public static class SettingsEdits
{
    public static EditPrior Apply(SettingsText t, EditSpec e)
    {
        if (e.Format == "mame") return MameIniEditor.Set(t, e.Key!, e.Value!);
        if (e.Replace is not null) return IniEditor.Replace(t, e.Section!, e.Replace);
        return IniEditor.Set(t, e.Section!, e.Key!, e.Value!);
    }

    public static void Revert(SettingsText t, EditSpec e, EditPrior prior)
    {
        if (e.Format == "mame") MameIniEditor.RevertSet(t, e.Key!, prior);
        else if (e.Replace is not null) IniEditor.RevertReplace(t, e.Section!, prior);
        else IniEditor.RevertSet(t, e.Section!, e.Key!, prior);
    }
}
