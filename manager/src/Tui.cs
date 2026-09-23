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
            var emu = state.Screen == Screen.Emulator
                ? list.Emulators?.FirstOrDefault(e => e.Id == state.ModId) : null;
            var rows = state.Screen switch
            {
                Screen.VirtualGun => TuiModel.VirtualGunRows(list),
                Screen.Mod when status is not null => TuiModel.ModRows(status, cfg),
                Screen.Emulator when emu is not null => TuiModel.EmulatorRows(emu),
                _ => TuiModel.ListRows(list),
            };
            Render(state, rows, status, emu, list, message);
            message = null;

            var key = MapKey(Console.ReadKey(intercept: true));
            var (next, action) = TuiModel.Reduce(state, key, rows);
            state = next;

            try
            {
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
                    list = await mm.List();   // reflect the new install state in the ViGEmBus row
                    break;
                case ActionKind.VirtualGun:
                    list = await mm.List();   // the screen shows the install state as it is now
                    break;
                case ActionKind.GunInstall:
                    message = await Working(() => mm.VirtualGunInstall());
                    list = await mm.List();
                    state = state with { Cursor = 0 };   // the rows change with the install state
                    break;
                case ActionKind.GunUninstall:
                    message = await Working(() => mm.VirtualGunUninstall());
                    list = await mm.List();
                    state = state with { Cursor = 0 };
                    break;
                case ActionKind.Refresh:
                    message = "refreshed";
                    list = await mm.List();
                    if (state.ModId is not null) cfg = await LoadConfig(mm, list, state.ModId);
                    break;
                case ActionKind.SetPath:
                    message = await PromptForPath(mm, action.ModId!, action.Appid);
                    list = await mm.List();
                    cfg = await LoadConfig(mm, list, action.ModId!);
                    break;
                case ActionKind.ClearPath:
                    message = await Working(() => mm.ClearGamePath(action.ModId!, action.Appid));
                    list = await mm.List();
                    cfg = await LoadConfig(mm, list, action.ModId!);
                    break;
                case ActionKind.Toggle:
                    var gk = GameKeyFor(list, action.ModId!);
                    var res = await mm.SetToggle(action.ModId!, gk, action.ToggleKey!, action.ToggleOn);
                    message = res.Message;
                    cfg = await LoadConfig(mm, list, action.ModId!);
                    break;
                case ActionKind.EmuOpen:
                    list = await mm.List();
                    break;
                case ActionKind.EmuToggle:
                    message = await Working(() => action.ToggleOn
                        ? mm.Emulators.Install(action.ModId!, action.ToggleKey)
                        : mm.Emulators.Uninstall(action.ModId!, action.ToggleKey));
                    list = await mm.List();
                    break;
                case ActionKind.EmuSetFolder:
                    message = await PromptForEmulatorFolder(mm, list, action.ModId!);
                    list = await mm.List();
                    break;
                case ActionKind.EmuUseSuggested:
                    message = await Working(() => mm.Emulators.UseSuggested(action.ModId!));
                    list = await mm.List();
                    state = state with { Cursor = 0 };
                    break;
                case ActionKind.EmuClearFolder:
                    message = await Working(() => mm.Emulators.ClearFolder(action.ModId!));
                    list = await mm.List();
                    state = state with { Cursor = 0 };
                    break;
                case ActionKind.EmuUpdate:
                    message = await Working(() => mm.Emulators.Update(action.ModId!));
                    list = await mm.List();
                    break;
                case ActionKind.EmuReapply:
                    message = await Working(() => mm.Emulators.Reapply(action.ModId!));
                    list = await mm.List();
                    break;
            }
            }
            catch (Exception ex) { message = "error: " + ex.Message; }   // an action must never crash the loop
        }
    }

    // Typed/pasted rather than a folder-browse dialog: no COM interop in an AOT binary,
    // and Explorer's "Copy as path" is one keystroke away.
    static async Task<string> PromptForPath(ModManager mm, string modId, long? appid)
    {
        Console.WriteLine("\n  Type or paste the game's folder, then Enter. Blank cancels.");
        Console.WriteLine("  (In Explorer: Shift+Right-click the folder, \"Copy as path\".)");
        Console.Write("\n  > ");
        var typed = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(typed)) return "cancelled";

        var r = await mm.SetGamePath(modId, appid, typed);
        return string.Join("; ", r.Results.Select(x => x.Message));
    }

    static async Task<string> PromptForEmulatorFolder(ModManager mm, ListReport list, string emuId)
    {
        var st = list.Emulators?.FirstOrDefault(e => e.Id == emuId);
        if (st is { Locked: true }) return $"Installed in {st.Folder}. Uninstall to move.";
        Console.WriteLine("\n  Type or paste the settings folder or the program folder, then Enter. Blank cancels.");
        Console.WriteLine("  (In Explorer: Shift+Right-click the folder, \"Copy as path\".)");
        Console.Write("\n  > ");
        var typed = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(typed)) return "cancelled";
        var r = await mm.Emulators.SetFolder(emuId, typed);
        return string.Join("; ", r.Results.Select(x => x.Message));
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
            'r' => TuiKey.Refresh,
            _ => TuiKey.Other,
        }
    };

    static void Render(TuiState s, List<MenuRow> rows, ModStatus? mod, EmulatorStatus? emu, ListReport list, string? message)
    {
        Console.Clear();
        Console.WriteLine(s.Screen switch
        {
            Screen.VirtualGun => $"  Virtual Lightgun  —  {TuiModel.VirtualGunStatus(list)}\n",
            Screen.Mod when mod is not null => $"  {TuiModel.DisplayName(mod.Name)}  —  {TuiModel.RowStatus(mod)}\n",
            Screen.Emulator when emu is not null => $"  {emu.Name}   {TuiModel.EmulatorState(emu).Glyph} {TuiModel.EmulatorState(emu).Text}\n",
            _ => "  VRLF Mod Manager\n",
        });

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
            ? "\n  ↑/↓ move   Enter select   R refresh   Q quit"
            : "\n  ↑/↓ move   Enter select/toggle   Esc back   Q quit");
    }
}
