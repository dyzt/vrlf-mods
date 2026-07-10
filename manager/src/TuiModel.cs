namespace VrlfMods;

public enum Screen { List, Mod }
public enum RowKind { Header, Action, Toggle, Separator, Info }
public enum TuiKey { Up, Down, Enter, Back, Quit, Vigem, Refresh, Other }
public enum ActionKind { None, Open, Back, Quit, Install, Reinstall, Uninstall, Update, Vigem, Refresh, Toggle }

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

    public static List<MenuRow> ListRows(ListReport r) =>
        r.Mods.Select(m => new MenuRow(RowKind.Action, $"{m.Name,-30} {RowStatus(m)}",
            ActionKind.Open, ModId: m.Id)).ToList();

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
            case TuiKey.Vigem when s.Screen == Screen.List: return (s, new TuiAction(ActionKind.Vigem));
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
                if (row.Action == ActionKind.Toggle)
                    return (s, new TuiAction(ActionKind.Toggle, row.ModId, row.ToggleKey, !(row.ToggleOn ?? false)));
                return (s, new TuiAction(row.Action, row.ModId, Appid: row.Appid));
            default: return (s, new TuiAction(ActionKind.None));
        }
    }
}
