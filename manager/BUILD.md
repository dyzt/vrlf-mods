# Build notes

## AOT publish path taken: Developer-environment AOT (contingency path 1)

`dotnet publish src/VrlfMods.csproj -c Release -r win-x64 -o out` failed on the first
attempt from a plain shell with:

```
'vswhere.exe' is not recognized as an internal or external command, ...
Generating native code
The filename, directory name, or volume label syntax is incorrect.
...Microsoft.NETCore.Native.targets(370,5): error MSB3073: The command
""'vswhere.exe' is not recognized...;...\link.exe" @"...\link.rsp"" exited with code 123.
```

This is the documented native-link failure mode: the Native AOT toolchain shells out to
`vswhere.exe` to locate the VC toolset, and neither `vswhere.exe` nor the MSVC
`link.exe` environment (`INCLUDE`/`LIB`/`PATH`) were present in the ambient shell.

Fix: re-ran the publish from a Visual Studio 2026 Developer environment. On this
machine the "MSVC 2026" toolchain is actually installed under the Visual Studio
`18` folder (see project CLAUDE.md gotcha), not a folder literally named `2026`:

- `vcvars64.bat`: `C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat`
- `vswhere.exe`: `C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe` (added to `PATH`)

With both on `PATH`/environment (`call vcvars64.bat` + `PATH` augmented with the
Installer directory containing `vswhere.exe`), the same publish command succeeded:

```
Generating native code
VrlfMods -> C:\Users\jamsb\Desktop\Software Projects\vrlf-mods\manager\out\
```

`out/vrlf-mods.exe` is a genuine Native AOT PE32+ executable (~1.2 MB, no bundled
CoreCLR), confirmed to run and print `vrlf-mods (scaffold)`.

No csproj fallback (`PublishAot=false` + single-file self-contained) was needed —
`<PublishAot>true</PublishAot>` in `src/VrlfMods.csproj` is unmodified from the brief.

## NuGet source note

This machine's user-level NuGet config (`%APPDATA%\NuGet\NuGet.Config`) has an empty
`<packageSources>` list (no default feed), so a plain `dotnet test`/`dotnet publish`
fails restore with `NU1100` before any AOT-specific step is reached. Added
`manager/NuGet.config` (scoped to this repo only, `<clear/>` + `nuget.org`) rather than
touching the machine-wide config. This is a prerequisite fix, not part of the AOT
contingency.
