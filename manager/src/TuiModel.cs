namespace VrlfMods;

public enum Screen { List, Mod, VirtualGun, Emulator }
public enum RowKind { Header, Action, Toggle, Separator, Info }
public enum TuiKey { Up, Down, Enter, Back, Quit, Refresh, Other }
public enum ActionKind { None, Open, Back, Quit, Install, Reinstall, Uninstall, Update, Vigem, VirtualGun, Refresh,
                         Toggle, SetPath, ClearPath, GunInstall, GunUninstall,
                         EmuOpen, EmuToggle, EmuSetFolder, EmuUseSuggested, EmuClearFolder, EmuUpdate, EmuReapply }

public record MenuRow(RowKind Kind, string Text, ActionKind Action = ActionKind.None,
    string? ModId = null, string? ToggleKey = null, bool? ToggleOn = null, long? Appid = null,
    bool Selectable = true, string? Help = null, bool Enabled = true);

public record TuiAction(ActionKind Kind, string? ModId = null, string? ToggleKey = null,
    bool ToggleOn = false, long? Appid = null);

public record TuiState(Screen Screen, int Cursor, string? ModId);

public static class TuiModel
{
    public static string RowStatus(ModStatus m)
    {
        if (m.InstalledVersion is null)
        {
            var found = m.Games.Any(g => g.Detected);
            return found ? "○ not installed   (game found)" : "○ not installed   (game not found)";
        }
        if (m.InstalledVersion != m.Version) return $"⚠ update available ({m.InstalledVersion} → {m.Version})";
        var installed = m.Games.Count(g => g.Installed);
        if (m.Games.Count > 1 && installed < m.Games.Count)
            return $"◐ partially installed ({installed}/{m.Games.Count})";
        return $"● installed v{m.InstalledVersion}";
    }

    // Drop the redundant " VRLF Mod" suffix every entry carries — pure repetition in a list
    // already titled "VRLF Mod Manager". Leave any off-convention name untouched.
    public static string DisplayName(string name) =>
        name.EndsWith(" VRLF Mod", StringComparison.Ordinal) ? name[..^" VRLF Mod".Length] : name;

    public static List<MenuRow> ListRows(ListReport r)
    {
        // Pad every name to the widest one (mods + the ViGEmBus row) so all the
        // installed/not-installed labels line up in a single column.
        int width = new[] { "ViGEmBus".Length, "Virtual Lightgun".Length }
            .Concat(r.Mods.Select(m => DisplayName(m.Name).Length))
            .Concat((r.Emulators ?? new()).Select(e => e.Name.Length))
            .Max();

        var rows = new List<MenuRow>();

        // ViGEmBus is now a real selectable row at the top (was a hidden 'V' hotkey).
        // Selecting it installs the driver; when already present it just re-confirms.
        string vstatus = r.VigemInstalled ? "● installed" : "○ not installed";
        rows.Add(new MenuRow(RowKind.Action, $"{"ViGEmBus".PadRight(width)}   {vstatus}",
            ActionKind.Vigem,
            Help: r.VigemInstalled
                ? "Virtual gamepad driver — required for 2-gun co-op. Already installed."
                : "Virtual gamepad driver — required for 2-gun co-op. Enter to install."));

        if (r.VirtualGunAvailable || r.VirtualGunInstalled)
        {
            rows.Add(new MenuRow(RowKind.Action, $"{"Virtual Lightgun".PadRight(width)}   {VirtualGunStatus(r)}",
                ActionKind.VirtualGun,
                Help: "Virtual lightgun driver for Raw Input games (aim_mode hid). Enter to install, update, reinstall or uninstall."));
        }
        rows.Add(new MenuRow(RowKind.Separator, "", Selectable: false));

        foreach (var m in r.Mods)
            rows.Add(new MenuRow(RowKind.Action, $"{DisplayName(m.Name).PadRight(width)}   {RowStatus(m)}",
                ActionKind.Open, ModId: m.Id));

        if (r.Emulators is { Count: > 0 } emus)
        {
            rows.Add(new MenuRow(RowKind.Separator, "", Selectable: false));
            rows.Add(new MenuRow(RowKind.Header, "EMULATORS", Selectable: false));
            foreach (var e in emus)
            {
                var (glyph, text) = EmulatorState(e);
                rows.Add(new MenuRow(RowKind.Action, $"{e.Name.PadRight(width)}   {glyph} {text}",
                    ActionKind.EmuOpen, ModId: e.Id));
            }
        }

        return rows;
    }

    /// <summary>One status for an emulator row: an update beats what is installed, which beats the folder state.</summary>
    public static (string Glyph, string Text) EmulatorState(EmulatorStatus e)
    {
        var installed = e.Options.Where(o => o.InstalledVersion is not null).ToList();
        if (installed.Any(o => o.InstalledVersion != o.Version)) return ("⚠", "update available");
        if (installed.Count > 0)
            return ("●", e.Options.Count == 1
                ? $"installed v{installed[0].InstalledVersion}"
                : string.Join(" + ", installed.Select(o => o.Short)));
        if (!e.FolderChosen) return ("○", "folder not chosen");
        return ("○", "not installed");
    }

    public static string EmulatorFolderText(EmulatorStatus e)
    {
        if (e.Folder is null) return "Settings folder: not chosen";
        if (!e.FolderExists) return $"Settings folder: {e.Folder}  (missing)";
        return $"Settings folder: {e.Folder}";
    }

    /// <summary>The emulator screen: folder first, then one row per option, then the actions.
    /// No help or notes: the rows are the whole screen.</summary>
    public static List<MenuRow> EmulatorRows(EmulatorStatus e)
    {
        var rows = new List<MenuRow>
        {
            new(RowKind.Action, EmulatorFolderText(e), ActionKind.EmuSetFolder, ModId: e.Id),
        };
        if (e.Folder is null && e.Suggested is not null)
            rows.Add(new(RowKind.Action, $"Use your main install: {e.Suggested}", ActionKind.EmuUseSuggested, ModId: e.Id));
        rows.Add(new(RowKind.Separator, "", Selectable: false));

        foreach (var o in e.Options)
        {
            bool installed = o.InstalledVersion is not null;
            var required = o.Requires is null ? null : e.Options.FirstOrDefault(x => x.Id == o.Requires);
            bool reqMet = o.Requires is null || required?.InstalledVersion is not null;
            bool enabled = installed || (e.FolderChosen && e.FolderExists && reqMet);
            rows.Add(new(RowKind.Toggle, $"{o.Label,-28} v{o.InstalledVersion ?? o.Version}", ActionKind.EmuToggle,
                ModId: e.Id, ToggleKey: o.Id, ToggleOn: installed, Enabled: enabled));
        }

        var pending = e.Options.Where(o => o.InstalledVersion is not null && o.InstalledVersion != o.Version).ToList();
        if (pending.Count > 0)
            rows.Add(new(RowKind.Action, "Update → " + string.Join(", ", pending.Select(o => $"{o.Label} v{o.Version}")),
                ActionKind.EmuUpdate, ModId: e.Id));
        if (e.Options.Any(o => o.InstalledVersion is not null))
            rows.Add(new(RowKind.Action, "Re-apply installed options", ActionKind.EmuReapply, ModId: e.Id));
        if (e.Folder is not null && !e.Locked)
            rows.Add(new(RowKind.Action, "Forget this folder", ActionKind.EmuClearFolder, ModId: e.Id));
        return rows;
    }

    public static string VirtualGunStatus(ListReport r)
    {
        if (!r.VirtualGunInstalled) return "○ not installed";
        if (r.VirtualGunUpdate is not null) return $"● update to v{r.VirtualGunUpdate}";
        return r.VirtualGunVersion is null ? "● installed" : $"● installed v{r.VirtualGunVersion}";
    }

    /// <summary>The Virtual Lightgun screen. Install, Update and Reinstall need this release to pin a
    /// driver; Uninstall only needs one installed. Reinstall is hidden while an update is pending,
    /// because it would install the same pinned version the Update row does.</summary>
    public static List<MenuRow> VirtualGunRows(ListReport r)
    {
        const string Admin = " Windows asks for administrator approval.";
        var rows = new List<MenuRow>();
        if (!r.VirtualGunInstalled && r.VirtualGunAvailable)
            rows.Add(new(RowKind.Action, "Install", ActionKind.GunInstall,
                Help: "Installs the driver, so Raw Input games see each VRLF gun as its own mouse." + Admin));
        if (r.VirtualGunInstalled && r.VirtualGunAvailable)
        {
            if (r.VirtualGunUpdate is not null)
                rows.Add(new(RowKind.Action, $"Update → v{r.VirtualGunUpdate}", ActionKind.GunInstall,
                    Help: "Installs the newer driver over this one. Game bindings keep working." + Admin));
            else
                rows.Add(new(RowKind.Action, "Reinstall", ActionKind.GunInstall,
                    Help: "Runs the installer again. Fixes guns a game can't see, or bindings shared from another PC that don't match." + Admin));
        }
        if (r.VirtualGunInstalled)
            rows.Add(new(RowKind.Action, "Uninstall", ActionKind.GunUninstall,
                Help: "Removes the driver and its virtual lightguns. Close VRLF first." + Admin));
        rows.Add(new(RowKind.Separator, "", Selectable: false));
        rows.Add(new(RowKind.Info, "Used by profiles with aim_mode hid, for games that read each mouse separately.", Selectable: false));
        return rows;
    }

    /// <summary>The mod screen's game-folder row: where we will install, and who chose it.</summary>
    public static string GameFolderText(GameStatus g, bool nameTheGame)
    {
        var lead = nameTheGame ? g.Name : "Game folder";
        if (g.Detected)
            return $"{lead}: {g.Path}  ({(g.Manual ? "you chose this" : "found via Steam")})";
        if (g.Manual)
            return $"{lead}: {g.Path}  — no longer there, Enter to fix";
        return $"{lead}: not found — Enter to choose it";
    }

    public static List<MenuRow> ModRows(ModStatus m, ModConfig? cfg)
    {
        var rows = new List<MenuRow>();
        bool installed = m.InstalledVersion is not null;
        bool gameFound = m.Games.Any(g => g.Detected);
        if (!installed && gameFound) rows.Add(new(RowKind.Action, "Install", ActionKind.Install, ModId: m.Id));
        if (installed)
        {
            if (m.InstalledVersion != m.Version) rows.Add(new(RowKind.Action, $"Update → v{m.Version}", ActionKind.Update, ModId: m.Id));
            rows.Add(new(RowKind.Action, "Reinstall", ActionKind.Reinstall, ModId: m.Id));
            rows.Add(new(RowKind.Action, "Uninstall", ActionKind.Uninstall, ModId: m.Id));
        }
        // Where this mod will be installed, and the way out when Steam can't find the game:
        // without this row that screen offers a non-Steam owner nothing at all.
        if (rows.Count > 0) rows.Add(new(RowKind.Separator, "", Selectable: false));
        bool nameTheGame = m.Games.Count > 1;
        foreach (var g in m.Games)
        {
            rows.Add(new(RowKind.Action, GameFolderText(g, nameTheGame), ActionKind.SetPath,
                ModId: m.Id, Appid: g.Appid,
                Help: "Point the manager at your copy of this game — a non-Steam install, or one Steam can't find."));
            if (g.Manual)
                rows.Add(new(RowKind.Action,
                    nameTheGame ? $"Forget the {g.Name} folder" : "Forget this folder",
                    ActionKind.ClearPath, ModId: m.Id, Appid: g.Appid,
                    Help: "Go back to locating this game through Steam."));
        }

        if (cfg is not null && cfg.Toggles.Count > 0)
        {
            rows.Add(new(RowKind.Separator, "", Selectable: false));
            rows.Add(new(RowKind.Header, "CONFIG", Selectable: false));
            foreach (var t in cfg.Toggles)
                rows.Add(new(RowKind.Toggle, t.Label, ActionKind.Toggle, ModId: m.Id,
                    ToggleKey: t.Key, ToggleOn: t.On, Selectable: t.Available, Enabled: t.Available, Help: t.Help ?? t.Note));
        }
        else if (installed)
            rows.Add(new(RowKind.Info, "No configurable options", Selectable: false));
        return rows;
    }

    static int NextSelectable(List<MenuRow> rows, int from, int dir)
    {
        if (rows.Count == 0) return 0;
        // Clamp a stale cursor (rows can shrink after an action) so arrows always recover.
        int start = Math.Clamp(from, 0, rows.Count - 1);
        int i = start;
        for (int step = 0; step < rows.Count; step++)
        {
            i += dir;
            if (i < 0 || i >= rows.Count) return start;   // clamp at ends
            if (rows[i].Selectable) return i;
        }
        return start;
    }

    public static (TuiState, TuiAction) Reduce(TuiState s, TuiKey key, List<MenuRow> rows)
    {
        switch (key)
        {
            case TuiKey.Quit: return (s, new TuiAction(ActionKind.Quit));
            case TuiKey.Refresh: return (s, new TuiAction(ActionKind.Refresh));
            case TuiKey.Up: return (s with { Cursor = NextSelectable(rows, s.Cursor, -1) }, new TuiAction(ActionKind.None));
            case TuiKey.Down: return (s with { Cursor = NextSelectable(rows, s.Cursor, +1) }, new TuiAction(ActionKind.None));
            case TuiKey.Back:
                return s.Screen == Screen.List
                    ? (s, new TuiAction(ActionKind.Quit))
                    : (new TuiState(Screen.List, 0, null), new TuiAction(ActionKind.Back));
            case TuiKey.Enter:
                if (s.Cursor < 0 || s.Cursor >= rows.Count) return (s, new TuiAction(ActionKind.None));
                var row = rows[s.Cursor];
                if (!row.Selectable || !row.Enabled) return (s, new TuiAction(ActionKind.None));
                if (row.Action == ActionKind.Open)
                    return (new TuiState(Screen.Mod, 0, row.ModId), new TuiAction(ActionKind.Open, row.ModId));
                if (row.Action == ActionKind.VirtualGun)
                    return (new TuiState(Screen.VirtualGun, 0, null), new TuiAction(ActionKind.VirtualGun));
                if (row.Action == ActionKind.Toggle)
                    return (s, new TuiAction(ActionKind.Toggle, row.ModId, row.ToggleKey, !(row.ToggleOn ?? false)));
                if (row.Action == ActionKind.EmuOpen)
                    return (new TuiState(Screen.Emulator, 0, row.ModId), new TuiAction(ActionKind.EmuOpen, row.ModId));
                if (row.Action == ActionKind.EmuToggle)
                    return (s, new TuiAction(ActionKind.EmuToggle, row.ModId, row.ToggleKey, !(row.ToggleOn ?? false)));
                return (s, new TuiAction(row.Action, row.ModId, Appid: row.Appid));
            default: return (s, new TuiAction(ActionKind.None));
        }
    }
}
