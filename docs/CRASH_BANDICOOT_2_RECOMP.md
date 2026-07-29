# Crash Bandicoot 2 on RecompOne — field notes

**Game (current dump):** Crash Bandicoot 2: Cortex Strikes Back (PAL)  
**IDs:** `SCES-00967` / boot `SCES_009.67`  
**Stack:** RecompOne static MIPS→C# + `RecompOne.Runtime`  
**Status:** starfield intro visible; fixed BIOS `InitPAD`/`StartPAD` (try Cross/Start to advance). Next: if still stuck, NSF/GOOL cinematic stream.

Lessons carried over from Crash 1 (SCUS-94900) — see that project's
`docs/CRASH_BANDICOOT_RECOMP.md` for the full write-up.

## Boot checklist (same order as CB1)

1. **Disc / EXE** — confirm `SYSTEM.CNF` → `SCES_009.67`, `PS-X EXE` magic.
2. **Recompile** — `linearSweep` first; empty overlays until we map them.
3. **Pad layout** — verify against *this* build’s `PadUpdate`, not CB1 guesses.
4. **Frame timing** — find real `VSync` vs wait helper; ND titles often pad to ~30 Hz.
5. **Duff / decompressors** — expect mid-function `jr` and split functions → HLE or post-pass.
6. **CD / NSD paging** — disc still required at runtime.

## Known from this dump

| Item | Value |
|------|--------|
| Volume ID | `SCES-00967` |
| BOOT | `cdrom:\SCES_009.67;1` |
| EXE size (ISO) | 327680 bytes |
| Region | PAL (Europe En,Fr,De,Es,It) |

NTSC-U would be `SCUS-94154` / `SCUS_941.54` — not targeted yet.

## Config / HLE

`CrashBandicoot2.json` patches (replace mode) for PsyQ entry points. Important CB2 addresses:

| Address | Role |
|---------|------|
| `8004A71C` | `VSync` → `LibEtc.VSync` (wall-clock vcount for poll-waiters) |
| `8004C1E4` / `8004C240` / `8004C8DC` / `8004C950` / `8004CBA8` | `DrawSync` / `SetDispMask` / `DrawOTag` / `PutDrawEnv` / `PutDispEnv` |
| `80046C2C` / `80046C3C` / `80046C6C` | `CdStatus` / `CdMode` / `CdInit` |
| `80048B90` / `8004920C` / `80049310` | `CdRead` / `CdRead` (hi) / `CdReadSync` |
| `80011E80` | empty stub → `PresentPump` (main-loop frame pump) |
| `8005DF30` | PsyQ vblank counter |

## First-boot fixes landed

| Issue | Fix |
|------|-----|
| `unmapped call: 0x80047624` during CD init | Post-pass: jump table `@ 0x80010B20` in `func_800473B8` |
| `unmapped call: 0x00000884` after ResetGraph | Patch `8004A55C` / `8004A570` → `LibGpu.GpuBiosCallback` |
| Window “Not Responding” | `HostWindow.KeepAlive`; LIBCD HLE; `PresentPump` on `80011E80` |
| `VSync: timeout` / black hang | HLE `VSync` + wall-clock advance on `VSync(-1)` polls |
| Black screen (no display) | `SetDispMask` wrote `GP1(03h)` inverted (1=off); also RT present stale-frame check was too aggressive (`>4`) |
| Stuck on starfield intro | BIOS `InitPAD`/`StartPAD` were no-ops — pad never reached the game |
| `CdRead: retry...` forever | HLE hi-level `CdRead`/`CdReadSync` + `CdStatus` (no shell-open) |
| `unmapped call: 0x80049A64` | Post-pass: printf format jump table `@ 0x80010C2C` |
| `unmapped call: 0x80056828` | Post-pass: state jump table `@ 0x80011198` |
| `unmapped call: 0x800120B0` | Post-pass: alpha jump table `@ 0x80010068` in `func_80012010` |
| `unmapped call: 0x800152B8` | Post-pass: level-param jump table `@ 0x800101B0` in `func_80014D6C` |
| `unmapped call: 0x800133AC` | Post-pass: stream-state jump table `@ 0x80010138` in `func_80013304` |
| `unmapped call: 0x80012748` | Post-pass: entry-type jump table `@ 0x80010120` in `func_800126BC` |
| `unmapped call: 0x800270B8` | Post-pass: game-mode jump table `@ 0x80010480` in `func_80026F14` |
| `unmapped call: 0x8003BC64` | HLE patch `8003BBD8` → `LibEtc.MemcpyHw` (Duff halfword copy) |
| `unmapped call: 0x8003A8B0` | RefineEnd + injected GOOL dispatch jump table in `func_8003A2AC` |
| `unmapped call: 0x80037EE4` | Injected GOOL expr jump tables in `func_80037930` |
| `unmapped call: 0x8003B00C` | Extended GOOL interpreter body + full handler table |
| `unmapped call: 0x8003A27C` / `0x80039470` | Pre-entry trampolines + forced GOOL helper funcs in config |
| `unmapped call: 0x80034818` | Pad-mode jump table @ `0x80010668` in `func_800347D4` |
| `unmapped call: 0x80023F40` | Post-pass: cam-interp jump table `@ 0x80010438` in `func_80023D78` |

## Dev tip

Boot breadcrumbs: `CrashBandicoot2.Launcher/bin/Release/net10.0/logs/boot.txt`
