using System.Text;

namespace VrlfMods;

public static class Tui
{
    public static async Task<int> Run(ModManager mm)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { /* console may not allow it */ }

        var list = await mm.List();
        var state = new TuiState(Screen.List, 0, null);
        ModConfig? cfg = null;
        string? message = null;

        while (true)
        {
            var status = state.ModId is null ? null : list.Mods.FirstOrDefault(m => m.Id == state.ModId);
            var rows = state.Screen == Screen.List || status is null
                ? TuiModel.ListRows(list)
                : TuiModel.ModRows(status, cfg);
            Render(state, rows, status, message);
            message = null;

            var key = MapKey(Console.ReadKey(intercept: true));
            var (next, action) = TuiModel.Reduce(state, key, rows);
            state = next;

            switch (action.Kind)
            {
                case ActionKind.Quit:
                    Console.Clear();
                    return 0;
                case ActionKind.Open:
                    cfg = await LoadConfig(mm, list, action.ModId!);
                    break;
                case ActionKind.Back:
                    cfg = null;
                    break;
                case ActionKind.Install:
                case ActionKind.Reinstall:
                    message = await Working(() => mm.Install(action.ModId!, null, null));
                    list = await mm.List();
                    cfg = await LoadConfig(mm, list, action.ModId!);
                    break;
                case ActionKind.Uninstall:
                    message = await Working(() => mm.Uninstall(action.ModId!, null));
                    list = await mm.List();
                    cfg = null;
                    state = new TuiState(Screen.List, 0, null);
                    break;
                case ActionKind.Update:
                    message = await Working(() => mm.Update(action.ModId!));
                    list = await mm.List();
                    cfg = await LoadConfig(mm, list, action.ModId!);
                    break;
                case ActionKind.Vigem:
                    message = await Working(() => mm.EnsureVigem());
                    break;
                case ActionKind.Refresh:
                    message = "refreshed";
                    list = await mm.List();
                    if (state.ModId is not null) cfg = await LoadConfig(mm, list, state.ModId);
                    break;
                case ActionKind.Toggle:
                    var gk = GameKeyFor(list, action.ModId!);
                    var res = await mm.SetToggle(action.ModId!, gk, action.ToggleKey!, action.ToggleOn);
                    message = res.Message;
                    cfg = await LoadConfig(mm, list, action.ModId!);
                    break;
            }
        }
    }

    static async Task<string> Working(Func<Task<ActionReport>> op)
    {
        Console.WriteLine("\n  working…");
        try { var r = await op(); return string.Join("; ", r.Results.Select(x => x.Message)); }
        catch (Exception ex) { return "error: " + ex.Message; }
    }

    static async Task<ModConfig?> LoadConfig(ModManager mm, ListReport list, string modId)
    {
        var m = list.Mods.FirstOrDefault(x => x.Id == modId);
        if (m is null || m.InstalledVersion is null) return null;
        return await mm.GetConfig(modId, GameKeyFor(list, modId));
    }

    static string GameKeyFor(ListReport list, string modId)
    {
        var m = list.Mods.First(x => x.Id == modId);
        var g = m.Games.FirstOrDefault(x => x.Installed) ?? m.Games.First();
        return g.Appid.ToString();
    }

    static TuiKey MapKey(ConsoleKeyInfo k) => k.Key switch
    {
        ConsoleKey.UpArrow => TuiKey.Up,
        ConsoleKey.DownArrow => TuiKey.Down,
        ConsoleKey.Enter => TuiKey.Enter,
        ConsoleKey.Escape or ConsoleKey.Backspace => TuiKey.Back,
        _ => char.ToLowerInvariant(k.KeyChar) switch
        {
            'q' => TuiKey.Quit,
            'v' => TuiKey.Vigem,
            'r' => TuiKey.Refresh,
            _ => TuiKey.Other,
        }
    };

    static void Render(TuiState s, List<MenuRow> rows, ModStatus? mod, string? message)
    {
        Console.Clear();
        Console.WriteLine(s.Screen == Screen.List || mod is null
            ? "  VRLF Mod Manager\n"
            : $"  {mod.Name}  —  {TuiModel.RowStatus(mod)}\n");

        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var marker = (i == s.Cursor && r.Selectable) ? ">" : " ";
            var text = r.Kind == RowKind.Toggle ? $"[{(r.ToggleOn == true ? "x" : " ")}] {r.Text}" : r.Text;
            if (r.Kind == RowKind.Toggle && !r.Enabled) text += "  (unavailable)";
            Console.WriteLine($" {marker} {text}");
        }

        var help = (s.Cursor >= 0 && s.Cursor < rows.Count) ? rows[s.Cursor].Help : null;
        if (help is not null) Console.WriteLine($"\n  {help}");
        if (message is not null) Console.WriteLine($"\n  {message}");

        Console.WriteLine(s.Screen == Screen.List
            ? "\n  ↑/↓ move   Enter open   V ViGEmBus   R refresh   Q quit"
            : "\n  ↑/↓ move   Enter select/toggle   Esc back   Q quit");
    }
}
