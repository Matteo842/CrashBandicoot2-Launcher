# Crash Bandicoot 2 Launcher (unofficial)

> **Unofficial fan project.** Not affiliated with, endorsed by, or connected to Sony Interactive Entertainment, Activision, Naughty Dog, or any rights holder of *Crash Bandicoot*.  
> *Crash Bandicoot* and related names/marks belong to their respective owners.

Tools + launcher for a copy of *Crash Bandicoot 2: Cortex Strikes Back* you already own (PS1, PAL, **SCES-00967**).  
Does **not** include the game, disc images, or a ready-made game binary.

Built on [RecompOne](https://github.com/BlackLabelHQ/RecompOne). Early / experimental — first-boot work in progress.

## Requirements

- Windows 10/11 x64 (CLI for now)
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build
- A **legal** dump of *Crash Bandicoot 2* PAL (`SCES_009.67` / SCES-00967) as `.cue` + matching `.bin`

## How to run (dev)

```powershell
dotnet run --project CrashBandicoot2.Launcher -c Release -- --prepare "path\to\game.cue"
dotnet run --project CrashBandicoot2.Launcher -c Release -- --run "path\to\game.cue"
```

First prepare recompiles + compiles into `game\` next to the exe. The disc is still required at runtime.

## Legal

Never commit `.bin` / `.cue` / generated `main.cs`. See `.gitignore`.
