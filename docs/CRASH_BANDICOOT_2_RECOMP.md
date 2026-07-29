# Crash Bandicoot 2 on RecompOne — field notes

**Game (current dump):** Crash Bandicoot 2: Cortex Strikes Back (PAL)  
**IDs:** `SCES-00967` / boot `SCES_009.67`  
**Stack:** RecompOne static MIPS→C# + `RecompOne.Runtime`  
**Status:** past CD/GPU init + first disc reads; next crash `unmapped call: 0x800120B0` (alpha jump table `@ 0x80010068` in `func_80012010`)

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
| Black screen (no display) | HLE `SetDispMask` / `PutDispEnv` / `DrawOTag` / … |
| `CdRead: retry...` forever | HLE hi-level `CdRead`/`CdReadSync` + `CdStatus` (no shell-open) |
| `unmapped call: 0x80049A64` | Post-pass: printf format jump table `@ 0x80010C2C` |
| `unmapped call: 0x80056828` | Post-pass: state jump table `@ 0x80011198` |
| `unmapped call: 0x800120B0` | **Next:** alpha jump table `@ 0x80010068` in `func_80012010` |

## Dev tip

Boot breadcrumbs: `CrashBandicoot2.Launcher/bin/Release/net10.0/logs/boot.txt`
