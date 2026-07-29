# Crash Bandicoot 2 on RecompOne — field notes

**Game (current dump):** Crash Bandicoot 2: Cortex Strikes Back (PAL)  
**IDs:** `SCES-00967` / boot `SCES_009.67`  
**Stack:** RecompOne static MIPS→C# + `RecompOne.Runtime`  
**Status:** boots past `CD_init` / `ResetGraph`; hangs on `VSync: timeout` (needs PsyQ VSync HLE addresses, same class of issue as CB1)

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

Start with an empty `functions` / `patches` list. Once addresses are known,
name PsyQ entry points (`VSync`, `DrawSync`, `Cd*`, …) so `SdkPatches` can
redirect them to Runtime HLE — same pattern as CB1’s `CrashBandicoot.json`.

## First-boot fixes landed

| Issue | Fix |
|-------|-----|
| `unmapped call: 0x80047624` during CD init | Post-pass: jump table `@ 0x80010B20` in `func_800473B8` → `switch` + mid-function labels |
| `unmapped call: 0x00000884` after ResetGraph | Patch `8004A55C` / `8004A570` → `LibGpu.GpuBiosCallback` (GetB0Table stubs leave BIOS GPU callbacks unmapped) |
| Window “Not Responding” | Busy-waits never pumped UI → `HostWindow.KeepAlive` from `PSMemory.ReadU32`; LIBCD HLE; frame pump on empty `80011E80`; fixed `CdInit` return (1=OK) |
