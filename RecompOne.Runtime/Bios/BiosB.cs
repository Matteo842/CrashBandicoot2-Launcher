using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Bios;

public static class BiosB
{
    static readonly PadReadEvent _padEvent = new();
    struct EvCB { public uint Status, Class, Spec, Mode, Func; }
    const int MaxEvents = 64;
    static readonly EvCB[] _evCBs = new EvCB[MaxEvents];
    struct TCB { public bool Used; }
    const int MaxThreads = 4;
    static readonly TCB[] _tcbs = new TCB[MaxThreads];

    static readonly uint[] _intChain = new uint[4];

    public static uint IntrEnvInInterruptAddr = 0u;

    static uint _padBuf;
    static uint _padBuf1, _padBuf2;
    static int _padSiz1 = 0x22, _padSiz2 = 0x22;
    static bool _padStarted;

    public static void DeliverEvent(uint @class, uint spec)
    {
        for (int i = 0; i < MaxEvents; i++)
            if (_evCBs[i].Status == 2u && _evCBs[i].Class == @class && _evCBs[i].Spec == spec)
                _evCBs[i].Status = 4u;
    }
    
    public static void DeliverEventIntr(CpuContext c, IMemory m, uint @class, uint spec)
    {
        for (int i = 0; i < MaxEvents; i++)
        {
            if (_evCBs[i].Status != 2u || _evCBs[i].Class != @class || _evCBs[i].Spec != spec) continue;
            if ((_evCBs[i].Mode & 0x1000u) != 0 && _evCBs[i].Func != 0u)
            {
                var snap = c.Snapshot();
                RecompOne.Runtime.Dispatch.Dispatcher.Call(c, m, _evCBs[i].Func);
                c.Restore(snap);
            }
            else
            {
                _evCBs[i].Status = 4u;
            }
        }
    }
    
    public static void CardComplete(CpuContext c, IMemory m, uint port)
    {
        var card = (port & 0x10u) != 0 ? Runtime.CardB : Runtime.CardA;
        uint spec = card.Enabled ? 0x0004u : 0x0100u;
        
        DeliverEventIntr(c, m, 0xF4000001u, spec);
        DeliverEventIntr(c, m, 0xF0000011u, spec);
    }

    static void CardRead(CpuContext c, IMemory m)
    {
        var card = (c.A0 & 0x10u) != 0 ? Runtime.CardB : Runtime.CardA;
        if (card.Enabled && c.A2 != 0u)
        {
            Span<byte> f = stackalloc byte[0x80];
            card.FrameRead((int)(c.A1 & 0x3FFu), f);
            for (uint i = 0; i < 0x80u; i++) m.WriteU8(c.A2 + i, f[(int)i]);
        }
        CardComplete(c, m, c.A0);
        c.V0 = 1u;
    }

    static void CardWrite(CpuContext c, IMemory m)
    {
        var card = (c.A0 & 0x10u) != 0 ? Runtime.CardB : Runtime.CardA;
        if (card.Enabled && c.A2 != 0u)
        {
            Span<byte> f = stackalloc byte[0x80];
            for (uint i = 0; i < 0x80u; i++) f[(int)i] = m.ReadU8(c.A2 + i);
            card.FrameWrite((int)(c.A1 & 0x3FFu), f);
        }
        CardComplete(c, m, c.A0);
        c.V0 = 1u;
    }

    public static uint GetFreeEvSlot()
    {
        for (int i = 0; i < MaxEvents; i++) if (_evCBs[i].Status == 0u) return (uint)i;
        return 0xFFFFFFFFu;
    }
    
    static ushort FirePad(IMemory m, int port, ushort buttons)
    {
        if (!Event.HasAnyListeners<PadReadEvent>()) return buttons;
        var e = _padEvent;
        e.Context = Runtime.Cpu!; e.Memory = m;
        e.Port = port; e.Buttons = buttons;
        Event.Dispatch(e);
        return e.Buttons;
    }

    // SCES-00967 game-mode word. While == -1 the main loop skips func_80015A04,
    // so InitPAD raw buffers alone never produce Start/Cross edges for title.
    const uint GameModeAddr = 0x8005F688u; // 0x80060000 - 0x978
    const uint LevelIdAddr = 0x8005F684u;  // 0x80060000 - 0x97C
    const uint WorldAddr = 0x8005F624u;    // 0x80060000 - 0x9DC
    const uint PadFlagsAddr = 0x8006CE2Cu; // 0x80070000 - 0x31D4
    const uint CamModeAddr = 0x8006CE4Cu;  // 0x80070000 - 0x31B4
    const uint LevelInfoPtrAddr = 0x80067834u;
    /// <summary>DrawOTag hold — retail writes -1 to freeze GPU submit until cleared.</summary>
    const uint DrawHoldAddr = 0x80067844u; // 0x80060000 + 0x7844
    /// <summary>Incremented at the start of <c>func_8001658C</c> (GPU submit helper).</summary>
    const uint DrawFrameCounterAddr = 0x8006CF88u; // 0x80070000 - 0x3078

    static ushort _lastLoggedState = 0xFFFF;
    static int _padEdgeLogs;
    static int _titleDiagLogs;
    static bool _drawHoldUnstuckLogged;
    static bool _introModeForcedLogged;
    static bool _introOtResetDone;
    static bool _introZoneInheritLogged;
    static bool _intro18cCleared;
    static bool _introPathF0Restored;
    static bool _introParentPcDumped;
    static bool _introFrameTimerLogged;
    static int _introFrameHle;
    static int _introHoldStuckFrames;

    static void PadRead(IMemory m)
    {
        // Modern InitPAD path: separate 0x22-byte buffers per port.
        // Only refresh raw status here. Title-mode edge synth must run once per
        // game frame (PresentPump) — VSync calls PresentFrame/RefreshPad multiple
        // times per loop and would clear rising-edge taps before GOOL reads them.
        if (_padStarted && (_padBuf1 != 0 || _padBuf2 != 0))
        {
            if (_padBuf1 != 0) WriteInitPadBuf(m, _padBuf1, 0, Hardware.Controller.State,
                Hardware.Controller.RightX, Hardware.Controller.RightY,
                Hardware.Controller.LeftX, Hardware.Controller.LeftY);
            if (_padBuf2 != 0) WriteInitPadBuf(m, _padBuf2, 1, Hardware.Controller.State2,
                Hardware.Controller.RightX2, Hardware.Controller.RightY2,
                Hardware.Controller.LeftX2, Hardware.Controller.LeftY2);
            return;
        }

        // Legacy PAD_init single-buffer path.
        if (_padBuf == 0) return;
        ushort s = Hardware.Controller.State;
        ushort swapped = (ushort)((s >> 8) | (s << 8));
        ushort s2 = Hardware.Controller.State2;
        ushort swapped2 = (ushort)((s2 >> 8) | (s2 << 8));
        swapped = FirePad(m, 0, swapped);
        swapped2 = FirePad(m, 1, swapped2);
        m.WriteU32(_padBuf, ((uint)swapped2 << 16) | swapped);
        m.WriteU8(_padBuf + 4, Hardware.Controller.RightX);
        m.WriteU8(_padBuf + 5, Hardware.Controller.RightY);
        m.WriteU8(_padBuf + 6, Hardware.Controller.LeftX);
        m.WriteU8(_padBuf + 7, Hardware.Controller.LeftY);
    }

    // Crash-format buttons after byte-swap+invert (matches guest pad words).
    const uint CrashStart = 0x0800u;
    const uint CrashCross = 0x0040u;
    // Level IDs: title stays in mode=-1 until GOOL writes a new mode; retail title
    // Start proceeds to Intro. GOOL input/draw on 0x3C is still incomplete, so HLE it.
    const uint LevelTitle = 0x3Cu;
    const uint LevelIntro = 0x1Cu;

    /// <summary>
    /// Once per main-loop PresentPump while mode==-1. Synthesizes digital held/tap
    /// the guest pad processor would write, without re-clearing edges on every VSync.
    /// </summary>
    public static void SynthTitlePadEdges(IMemory m)
    {
        // Runs every PresentPump (even after Intro) so a sticky DrawHold can be cleared.
        if (_padStarted) TryUnstickDrawHold(m);

        if (!_padStarted || m.ReadU32(GameModeAddr) != 0xFFFFFFFFu) return;
        if (_padBuf1 != 0) SynthDigitalPadEdges(m, _padBuf1);
        if (_padBuf2 != 0) SynthDigitalPadEdges(m, _padBuf2);
        LogPadIfChanged(m);
        TryHleTitleStart(m);
    }

    /// <summary>
    /// Intro can leave <c>0x80067844</c> sticky so <c>DrawOTag</c> is skipped.
    /// HLE title→Intro also writes mode and the guest path stores hold=2 briefly;
    /// clear any non-zero hold while level is Intro so submit resumes immediately.
    /// </summary>
    static void TryUnstickDrawHold(IMemory m)
    {
        uint level = m.ReadU32(LevelIdAddr);
        uint mode = m.ReadU32(GameModeAddr);
        bool intro = level == LevelIntro || mode == LevelIntro;
        if (!intro)
        {
            _introHoldStuckFrames = 0;
            _introOtResetDone = false;
            _introZoneInheritLogged = false;
            _intro18cCleared = false;
            _introPathF0Restored = false;
            _introParentPcDumped = false;
            _introFrameTimerLogged = false;
            _introFrameHle = 0;
            return;
        }

        // NOTE: do NOT force mode=0x1C here — mode==0x1C means "load intro" to the
        // game state machine; pinning it loops the level load and gates DrawOTag.
        // mode==-1 during the cutscene is correct retail behaviour.

        // One-shot: wipe leftover title OT self-loops when Intro first becomes active.
        // Per-frame ClearOTagR is still missing in Intro; LibGpu also clears after DrawOTag.
        if (!_introOtResetDone && level == LevelIntro)
        {
            _introOtResetDone = true;
            uint a = m.ReadU32(0x800636F0u);
            uint b = m.ReadU32(0x800636ECu);
            if (a != 0) RecompOne.Runtime.Sdk.LibGpu.ClearOTagR(m, a + 0x18u, 0x800u);
            if (b != 0) RecompOne.Runtime.Sdk.LibGpu.ClearOTagR(m, b + 0x18u, 0x800u);
            var msgOt = $"HLE Intro OT reset a=0x{a:X8} b=0x{b:X8}";
            Console.WriteLine("[boot] " + msgOt);
            Diagnostics.BootLog.Write(msgOt);
            RecompOne.Runtime.Sdk.LibGpu.ResetDrawLogBudget();
        }

        // Intro director often keeps zone=0 even though initObject should copy the parent's
        // zone (parent@0x800AD6A4 already has zone 0x800B69E8). Without +18, findZone /
        // path ops that key off current zone no-op. Inherit once when missing.
        {
            const uint cdah = 0x800A1200u;
            if (m.ReadU32(cdah) == 1u && m.ReadU32(cdah + 0x18u) == 0u)
            {
                uint parent = m.ReadU32(cdah + 0x44u);
                uint pz = parent != 0 ? m.ReadU32(parent + 0x18u) : 0u;
                if (pz >= 0x80010000u && pz < 0x80200000u)
                {
                    m.WriteU32(cdah + 0x18u, pz);
                    if (!_introZoneInheritLogged)
                    {
                        _introZoneInheritLogged = true;
                        var msgZ = $"HLE Intro inherit zone +18=0x{pz:X8} from parent 0x{parent:X8}";
                        Console.WriteLine("[boot] " + msgZ);
                        Diagnostics.BootLog.Write(msgZ);
                    }
                }
            }

            // CdahS2 compares parent+13C < director+18C (signed). Idle/setup may leave
            // +18C at sentinel 0x80000001 (negative as signed), so the progress block at
            // DF1C never runs. Once +D4 is bound, widen +18C to a large positive ceiling.
            if (!_intro18cCleared && m.ReadU32(cdah) == 1u && m.ReadU32(cdah + 0x18Cu) == 0x80000001u
                && m.ReadU32(cdah + 0xD4u) != 0u)
            {
                m.WriteU32(cdah + 0x18Cu, 0x7FFFFFFFu);
                _intro18cCleared = true;
                var msg18 = "HLE Intro clear +18C sentinel → 0x7FFFFFFF";
                Console.WriteLine("[boot] " + msg18);
                Diagnostics.BootLog.Write(msg18);
            }

            // Parent path descriptor (+F0) is set at spawn then later cleared while +F8
            // remains; path helpers early-out on F0==0. Restore the spawn pointer once.
            if (!_introPathF0Restored)
            {
                uint parent = m.ReadU32(cdah + 0x44u);
                if (parent != 0 && m.ReadU32(parent + 0xF0u) == 0u && m.ReadU32(parent + 0xF8u) != 0u)
                {
                    const uint spawnPath = 0x800B9174u;
                    m.WriteU32(parent + 0xF0u, spawnPath);
                    _introPathF0Restored = true;
                    var msgF0 = $"HLE Intro restore parent+F0=0x{spawnPath:X8}";
                    Console.WriteLine("[boot] " + msgF0);
                    Diagnostics.BootLog.Write(msgF0);
                }
            }
        }

        uint hold = m.ReadU32(DrawHoldAddr);
        if (hold == 0u)
        {
            _introHoldStuckFrames = 0;
            return;
        }

        _introHoldStuckFrames++;
        m.WriteU32(DrawHoldAddr, 0u);
        if (!_drawHoldUnstuckLogged)
        {
            _drawHoldUnstuckLogged = true;
            var msg = $"HLE clear DrawHold @ 0x80067844 (was 0x{hold:X8}) for Intro DrawOTag";
            Console.WriteLine("[boot] " + msg);
            Diagnostics.BootLog.Write(msg);
        }
        // Allow fresh DrawOTag logs after title spam filled the budget.
        RecompOne.Runtime.Sdk.LibGpu.ResetDrawLogBudget();
    }

    /// <summary>
    /// Advance the display timer used by the GOOL wait stack. Intro stops using the
    /// title PresentPump, so this must be driven by its per-frame DrawOTag instead.
    /// </summary>
    public static void AdvanceIntroFrameTimer(IMemory m)
    {
        if (m.ReadU32(LevelIdAddr) != LevelIntro) return;

        _introFrameHle++;
        uint ticks = (uint)_introFrameHle * 40u;
        uint a = m.ReadU32(0x800636F0u);
        uint b = m.ReadU32(0x800636ECu);
        if (a != 0) m.WriteU32(a + 0xCu, ticks);
        if (b != 0) m.WriteU32(b + 0xCu, ticks);

        if ((!_introFrameTimerLogged && _introFrameHle >= 2) || (_introFrameHle % 30) == 0)
        {
            _introFrameTimerLogged = true;
            var msg = $"HLE Intro advance display+0xC ticks={ticks} (frames≈{_introFrameHle})";
            Console.WriteLine("[boot] " + msg);
            Diagnostics.BootLog.Write(msg);
            if (_introFrameHle <= 180 && (_introFrameHle % 30) == 0)
                LogIntroDirectorState(m);
        }
    }

    static void LogIntroDirectorState(IMemory m)
    {
        const uint cdah = 0x800A1200u;
        uint parent = m.ReadU32(cdah + 0x44u);
        var sb = new System.Text.StringBuilder(
            $"introState f={_introFrameHle} cdah[type={m.ReadU32(cdah):X8} pc={m.ReadU32(cdah + 0xC0u):X8} " +
            $"wait={m.ReadU32(cdah + 0xBCu):X8} state={m.ReadU32(cdah + 0x64u):X8} " +
            $"zone={m.ReadU32(cdah + 0x18u):X8} d4={m.ReadU32(cdah + 0xD4u):X8} " +
            $"f0={m.ReadU32(cdah + 0xF0u):X8} 120={m.ReadU32(cdah + 0x120u):X8} " +
            $"13c={m.ReadU32(cdah + 0x13Cu):X8} 18c={m.ReadU32(cdah + 0x18Cu):X8} parent={parent:X8}]");

        if (parent >= 0x80010000u && parent < 0x80200000u && (parent & 3u) == 0u)
        {
            sb.Append($" parent[type={m.ReadU32(parent):X8} pc={m.ReadU32(parent + 0xC0u):X8} " +
                $"wait={m.ReadU32(parent + 0xBCu):X8} state={m.ReadU32(parent + 0x64u):X8} " +
                $"zone={m.ReadU32(parent + 0x18u):X8} d4={m.ReadU32(parent + 0xD4u):X8} " +
                $"f0={m.ReadU32(parent + 0xF0u):X8} f8={m.ReadU32(parent + 0xF8u):X8} " +
                $"13c={m.ReadU32(parent + 0x13Cu):X8} child={m.ReadU32(parent + 0x4Cu):X8}]");
        }

        sb.Append(" groups");
        for (uint i = 0; i < 8; i++)
        {
            uint group = 0x8006CDB0u + i * 8u;
            sb.Append($" {i}[t={m.ReadU32(group):X8} +4={m.ReadU32(group + 4u):X8} +4c={m.ReadU32(group + 0x4Cu):X8}]");
        }

        var msg = sb.ToString();
        Console.WriteLine("[boot] " + msg);
        Diagnostics.BootLog.Write(msg);
    }

    static void TryHleTitleStart(IMemory m)
    {
        if (m.ReadU32(LevelIdAddr) != LevelTitle) return;
        uint tap = _padBuf1 != 0 ? m.ReadU32(_padBuf1 + 0x24) : 0;
        if ((tap & (CrashStart | CrashCross)) == 0) return;
        ForceTitleStart(m);
    }

    /// <summary>Title → Intro mode poke (pad HLE + headless kick).</summary>
    public static void ForceTitleStart(IMemory m)
    {
        if (m.ReadU32(LevelIdAddr) != LevelTitle) return;
        if (m.ReadU32(GameModeAddr) == LevelIntro) return;
        m.WriteU32(GameModeAddr, LevelIntro);
        // Mode-change path stores hold=2; don't enter Intro with DrawOTag gated.
        m.WriteU32(DrawHoldAddr, 0u);
        RecompOne.Runtime.Sdk.LibGpu.ResetDrawLogBudget();
        Gpu.ResetTriLog();
        // Drop title starfield snap so Output can pick up Intro frames.
        Runtime.Gpu?.InvalidateSoftSnap();
        var msg = $"title HLE Start/Cross -> mode=0x{LevelIntro:X} (Intro)";
        Console.WriteLine("[boot] " + msg);
        Diagnostics.BootLog.Write(msg);
    }

    public static void LogTitleState(IMemory m)
    {
        if (_titleDiagLogs >= 12) return;
        _titleDiagLogs++;
        uint mode = m.ReadU32(GameModeAddr);
        uint level = m.ReadU32(LevelIdAddr);
        uint world = m.ReadU32(WorldAddr);
        uint flags = m.ReadU32(PadFlagsAddr);
        uint cam = m.ReadU32(CamModeAddr);
        uint hold = m.ReadU32(DrawHoldAddr);
        uint drawFrames = m.ReadU32(DrawFrameCounterAddr);
        uint info = m.ReadU32(LevelInfoPtrAddr);
        uint type = info != 0 ? m.ReadU32(info + 0x8u) : 0;
        uint tap = _padBuf1 != 0 ? m.ReadU32(_padBuf1 + 0x24) : 0;
        uint held = _padBuf1 != 0 ? m.ReadU32(_padBuf1 + 0x28) : 0;
        // 3D-submit gate: flags bit0 and *(**0x80060B64 + 0x10)
        uint objList = 0;
        uint p0 = m.ReadU32(0x80060B64u);
        if (p0 != 0)
        {
            uint p1 = m.ReadU32(p0 + 0x10u);
            if (p1 != 0) objList = m.ReadU32(p1);
        }
        var msg = $"title mode=0x{mode:X8} level=0x{level:X} world=0x{world:X8} flags=0x{flags:X8} cam=0x{cam:X8} hold=0x{hold:X8} drawF={drawFrames} objs=0x{objList:X8} type=0x{type:X} held=0x{held:X4} tap=0x{tap:X4}";
        Console.WriteLine("[boot] " + msg);
        Diagnostics.BootLog.Write(msg);

        // Intro director object (CdahS s0) — watch for frozen state/anim words.
        if (level == LevelIntro)
        {
            const uint cdah = 0x800A1200u;
            var sb = new System.Text.StringBuilder("intro obj@0x800A1200");
            foreach (uint off in new uint[] {
                0x00, 0x14, 0x18, 0x1C, 0x20, 0x24, 0x28, 0x44, 0x48, 0x4C, 0x50,
                0x60, 0x64, 0x68, 0x6C, 0x70, 0xA0, 0xAC, 0xBC, 0xC0, 0xC8, 0xCC,
                0xD4, 0xE8, 0xEC, 0xF4, 0xF8, 0xFC,
                0x118, 0x120, 0x13C, 0x174, 0x18C
            })
                sb.Append($" +{off:X2}=0x{m.ReadU32(cdah + off):X8}");
            uint parent2 = m.ReadU32(cdah + 0x44u);
            if (parent2 != 0)
            {
                sb.Append($" parent@0x{parent2:X8}+00=0x{m.ReadU32(parent2):X8}+14=0x{m.ReadU32(parent2 + 0x14u):X8}+18=0x{m.ReadU32(parent2 + 0x18u):X8}+AC=0x{m.ReadU32(parent2 + 0xACu):X8}+C0=0x{m.ReadU32(parent2 + 0xC0u):X8}+D4=0x{m.ReadU32(parent2 + 0xD4u):X8}+13C=0x{m.ReadU32(parent2 + 0x13Cu):X8}+60=0x{m.ReadU32(parent2 + 0x60u):X8}+64=0x{m.ReadU32(parent2 + 0x64u):X8}+68=0x{m.ReadU32(parent2 + 0x68u):X8}+F0=0x{m.ReadU32(parent2 + 0xF0u):X8}+F4=0x{m.ReadU32(parent2 + 0xF4u):X8}+F8=0x{m.ReadU32(parent2 + 0xF8u):X8}+E8=0x{m.ReadU32(parent2 + 0xE8u):X8}+EC=0x{m.ReadU32(parent2 + 0xECu):X8}");
                if (!_introParentPcDumped)
                {
                    _introParentPcDumped = true;
                    uint ppc = m.ReadU32(parent2 + 0xC0u);
                    uint p14 = m.ReadU32(parent2 + 0x14u);
                    var pb = new System.Text.StringBuilder($"parent PC@0x{ppc:X8} words");
                    if (ppc >= 0x80010000u && ppc < 0x80200000u)
                        for (uint i = 0; i < 16; i++) pb.Append($" {m.ReadU32(ppc + i * 4):X8}");
                    pb.Append($" | +14@0x{p14:X8}");
                    if (p14 >= 0x80010000u && p14 < 0x80200000u)
                        for (uint i = 0; i < 16; i++) pb.Append($" {m.ReadU32(p14 + i * 4):X8}");
                    // Also dump a few words around path descriptor F0 and object wait/event fields.
                    uint f0 = m.ReadU32(parent2 + 0xF0u);
                    pb.Append($" | F0@0x{f0:X8}");
                    if (f0 >= 0x80010000u && f0 < 0x80200000u)
                        for (uint i = 0; i < 8; i++) pb.Append($" {m.ReadU32(f0 + i * 4):X8}");
                    pb.Append($" +10=0x{m.ReadU32(parent2 + 0x10u):X8}+BC=0x{m.ReadU32(parent2 + 0xBCu):X8}+C8=0x{m.ReadU32(parent2 + 0xC8u):X8}+CC=0x{m.ReadU32(parent2 + 0xCCu):X8}+118=0x{m.ReadU32(parent2 + 0x118u):X8}+120=0x{m.ReadU32(parent2 + 0x120u):X8}");
                    uint bc = m.ReadU32(parent2 + 0xBCu);
                    if (bc >= 0x80010004u && bc < 0x80200000u)
                        pb.Append($" wait@BC-4=0x{m.ReadU32(bc - 4u):X8}");
                    pb.Append($" frames=0x{m.ReadU32(0x8006CDFCu):X8}");
                    uint opTab = m.ReadU32(0x1F80005Cu);
                    pb.Append($" opTab=0x{opTab:X8}");
                    if (opTab >= 0x80010000u && opTab < 0x80200000u)
                    {
                        pb.Append(" handlers");
                        foreach (uint op in new uint[] { 0x11, 0x35, 0x39, 0x3B, 0x3C, 0x3F, 0x43, 0x49 })
                            pb.Append($" {op:X2}=0x{m.ReadU32(opTab + op * 4u):X8}");
                    }
                    Diagnostics.BootLog.Write(pb.ToString());
                    Console.WriteLine("[boot] " + pb);
                }
            }
            // func_80028C9C: level struct from *(0x80060B64)+0x10; walks flag words at
            // +0x1B4 and slots at +0x194 (count at +0x190). Match bit2 → candidate for +D4.
            uint lvlRoot = m.ReadU32(0x80060B64u);
            if (lvlRoot != 0)
            {
                uint lvl = m.ReadU32(lvlRoot + 0x10u);
                if (lvl != 0)
                {
                    uint listCount = m.ReadU32(lvl + 0x190u);
                    sb.Append($" lvl@0x{lvl:X8} n={listCount}");
                    uint n = Math.Min(listCount, 8u);
                    for (uint i = 0; i < n; i++)
                    {
                        uint fl = m.ReadU32(lvl + 0x1B4u + i * 4u);
                        uint slot = m.ReadU32(lvl + 0x194u + i * 4u);
                        sb.Append($" [{i}]fl=0x{fl:X8}slot=0x{slot:X8}");
                        // slot may be a handle; only chase +0x14 when it looks like a KSEG object ptr
                        if (slot >= 0x80010000u && slot < 0x80200000u && (slot & 3u) == 0)
                        {
                            // Prefer paged-in object (*slot) when the handle still points at a stub.
                            uint obj = m.ReadU32(slot);
                            if (obj < 0x80010000u || obj >= 0x80200000u || (obj & 3u) != 0)
                                obj = slot;
                            sb.Append($" obj=0x{obj:X8} pos=({m.ReadU32(obj+0x60u):X8},{m.ReadU32(obj+0x64u):X8},{m.ReadU32(obj+0x68u):X8})");
                            uint zone = m.ReadU32(obj + 0x14u);
                            if (zone >= 0x80010000u && zone < 0x80200000u && (zone & 3u) == 0)
                            {
                                sb.Append($"+14=0x{zone:X8}");
                                sb.Append($" aabb=({m.ReadU32(zone):X},{m.ReadU32(zone+4):X},{m.ReadU32(zone+8):X}+{m.ReadU32(zone+0xCu):X},{m.ReadU32(zone+0x10u):X},{m.ReadU32(zone+0x14u):X})");
                                // Zone header neighbors — look for a world origin / matrix near AABB.
                                sb.Append($" zh=({m.ReadU32(zone+0x18u):X},{m.ReadU32(zone+0x1Cu):X},{m.ReadU32(zone+0x20u):X},{m.ReadU32(zone+0x24u):X},{m.ReadU32(zone+0x28u):X},{m.ReadU32(zone+0x2Cu):X})");
                            }
                        }
                    }
                    sb.Append($" ce04n={m.ReadU32(0x8006CE04u)}");
                }
            }
            var msg2 = sb.ToString();
            Console.WriteLine("[boot] " + msg2);
            Diagnostics.BootLog.Write(msg2);
        }
    }

    static void WriteInitPadBuf(IMemory m, uint buf, int port, ushort buttons,
        byte rx, byte ry, byte lx, byte ly)
    {
        ushort b = FirePad(m, port, buttons);
        m.WriteU8(buf + 0, 0x00); // ok
        m.WriteU8(buf + 1, 0x41); // digital / standard
        m.WriteU8(buf + 2, (byte)(b & 0xFF));
        m.WriteU8(buf + 3, (byte)(b >> 8));
        m.WriteU8(buf + 4, rx);
        m.WriteU8(buf + 5, ry);
        m.WriteU8(buf + 6, lx);
        m.WriteU8(buf + 7, ly);
    }

    /// <summary>
    /// Mirror Crash 2 digital pad post-process (byte-swap + invert + rising edge).
    /// Pad struct overlays InitPAD buffer: +0x28 held, +0x2C prev, +0x24 just-pressed.
    /// </summary>
    static void SynthDigitalPadEdges(IMemory m, uint buf)
    {
        ushort raw = m.ReadU16(buf + 2);
        uint swapped = ((uint)(raw & 0xFF) << 8) | (uint)(raw >> 8);
        uint held = (~swapped) & 0xFFFFu;
        uint prev = m.ReadU32(buf + 0x28);
        m.WriteU32(buf + 0x2C, prev);
        m.WriteU32(buf + 0x28, held);
        m.WriteU32(buf + 0x24, held & ~prev & 0xFFFFu);
    }

    static void LogPadIfChanged(IMemory m)
    {
        ushort s = Hardware.Controller.State;
        if (s == _lastLoggedState) return;
        _lastLoggedState = s;
        if (_padEdgeLogs >= 40) return;
        _padEdgeLogs++;
        uint mode = m.ReadU32(GameModeAddr);
        uint tap = _padBuf1 != 0 ? m.ReadU32(_padBuf1 + 0x24) : 0;
        uint held = _padBuf1 != 0 ? m.ReadU32(_padBuf1 + 0x28) : 0;
        byte st = _padBuf1 != 0 ? m.ReadU8(_padBuf1) : (byte)0xFF;
        byte id = _padBuf1 != 0 ? m.ReadU8(_padBuf1 + 1) : (byte)0xFF;
        ushort raw = _padBuf1 != 0 ? m.ReadU16(_padBuf1 + 2) : (ushort)0;
        var msg = $"pad state=0x{s:X4} raw={st:X2}/{id:X2}/0x{raw:X4} held=0x{held:X4} tap=0x{tap:X4} mode=0x{mode:X8}";
        Console.WriteLine("[boot] " + msg);
        Diagnostics.BootLog.Write(msg);
    }

    public static void RefreshPad(IMemory m) => PadRead(m);
    public static void Dispatch(CpuContext c, IMemory m, uint fn)
    {
        Log.Bios($"B({fn:X2}) {BiosNames.B(fn)}");
        switch (fn)
        {
            case 0x00: c.V0 = 0u; break;
            case 0x01: break;
            case 0x02: c.V0 = 0u; break;
            case 0x03: c.V0 = 0u; break;
            case 0x04: break;
            case 0x05: break;
            case 0x06: break;
            case 0x07: DeliverEvent(c.A0, c.A1); break;
            case 0x08: c.V0 = OpenEvent(c.A0, c.A1, c.A2, c.A3); break;
            case 0x09: CloseEvent(c.A0); c.V0 = 1u; break;
            case 0x0A: c.V0 = WaitEvent(c.A0); break;
            case 0x0B: c.V0 = TestEvent(c.A0); break;
            case 0x0C: EnableEvent(c.A0); c.V0 = 1u; break;
            case 0x0D: DisableEvent(c.A0); c.V0 = 1u; break;
            case 0x0E: c.V0 = OpenTh(c.A0, c.A1, c.A2); break;
            case 0x0F: CloseTh(c.A0); c.V0 = 1u; break;
            case 0x10: break;
            case 0x11: break;
            case 0x12: // InitPAD(buf1, siz1, buf2, siz2)
            {
                _padBuf1 = c.A0;
                _padSiz1 = (int)c.A1;
                _padBuf2 = c.A2;
                _padSiz2 = (int)c.A3;
                if (_padBuf1 != 0 && _padSiz1 > 0)
                    for (int i = 0; i < _padSiz1; i++) m.WriteU8(_padBuf1 + (uint)i, 0xFF);
                if (_padBuf2 != 0 && _padSiz2 > 0)
                    for (int i = 0; i < _padSiz2; i++) m.WriteU8(_padBuf2 + (uint)i, 0xFF);
                Diagnostics.BootLog.Write($"InitPAD buf1=0x{_padBuf1:X8}/{_padSiz1} buf2=0x{_padBuf2:X8}/{_padSiz2}");
                c.V0 = 1u;
                break;
            }
            case 0x13: // StartPAD
                _padStarted = true;
                PadRead(m);
                Diagnostics.BootLog.Write("StartPAD");
                c.V0 = 1u;
                break;
            case 0x14: // StopPAD
                _padStarted = false;
                c.V0 = 1u;
                break;
            case 0x15: _padBuf = c.A1; break;
            case 0x16: PadRead(m); break;
            case 0x17: break;
            case 0x18: IntrEnvInInterruptAddr = 0u; break;
            case 0x19: IntrEnvInInterruptAddr = c.A0 != 0u ? c.A0 - 0x36u : 0u; break;
            case 0x1A: break;
            case 0x1B: break;
            case 0x1C: break;
            case 0x1D: break;
            case 0x1E: break;
            case 0x1F: break;
            case 0x20: UnDeliverEvent(c.A0, c.A1); break;
            case 0x2B: break;
            case 0x2C: break;
            case 0x2D: break;
            case 0x2E: break;
            case 0x2F: c.V0 = 0u; break;
            case 0x30: c.V0 = 0u; break;
            case 0x31: c.V0 = 0u; break;
            case 0x32: BiosA.Dispatch(c, m, 0x00); break;
            case 0x33: BiosA.Dispatch(c, m, 0x01); break;
            case 0x34: BiosA.Dispatch(c, m, 0x02); break;
            case 0x35: BiosA.Dispatch(c, m, 0x03); break;
            case 0x36: BiosA.Dispatch(c, m, 0x04); break;
            case 0x37: BiosA.Dispatch(c, m, 0x05); break;
            case 0x38: BiosA.Dispatch(c, m, 0x06); break;
            case 0x39: c.V0 = c.A0 <= 2u ? 2u : 0u; break;
            case 0x3A: c.V0 = 0xFFFFFFFFu; break;
            case 0x3B: Console.Write((char)(c.A0 & 0xFF)); c.V0 = c.A0; break;
            case 0x3C: c.V0 = 0xFFFFFFFFu; break;
            case 0x3D: Console.Write((char)(c.A0 & 0xFF)); c.V0 = c.A0; break;
            case 0x3E: c.V0 = 0u; break;
            case 0x3F: Console.Write(Bios.ReadString(m, c.A0)); c.V0 = c.A0; break;
            case 0x40: c.V0 = 1u; break;
            case 0x41: c.V0 = BiosA.CardFormat(m, c.A0); break;
            case 0x42: c.V0 = BiosA.FirstFile(m, c.A0, c.A1); break;
            case 0x43: c.V0 = BiosA.NextFile(m, c.A0); break;
            case 0x44: c.V0 = 0u; break;
            case 0x45: c.V0 = BiosA.CardDelete(m, c.A0); break;
            case 0x46: c.V0 = 0u; break;
            case 0x47: c.V0 = GetFreeEvSlot(); break;
            case 0x48: c.V0 = 0xFFFFFFFFu; break;
            case 0x49: break;
            case 0x4A: c.V0 = 1u; break;
            case 0x4B: c.V0 = 1u; break;
            case 0x4C: c.V0 = 1u; break;
            case 0x4D: break;
            case 0x4E: CardWrite(c, m); break;
            case 0x4F: CardRead(c, m); break;
            case 0x50: break;
            case 0x51: c.V0 = KromFont.Krom2RawAdd(c.A0); break;
            case 0x53: c.V0 = KromFont.Krom2Offset(c.A0); break;
            case 0x54: c.V0 = BiosA.LastErrno; break;
            case 0x55: c.V0 = 0u; break;
            case 0x56: c.V0 = 0u; break;
            case 0x57: c.V0 = 0u; break;
            case 0x58: break;
            case 0x59: c.V0 = BiosA.TestDevice(m, c.A0); break;
            case 0x5B: c.V0 = 0u; break;
            case 0x5C: c.V0 = 0u; break;
            case 0x5D: break;
            default: break;
        }
    }

    static uint OpenEvent(uint @class, uint spec, uint mode, uint func)
    {
        for (int i = 0; i < MaxEvents; i++)
        {
            if (_evCBs[i].Status == 0u)
            {
                _evCBs[i] = new EvCB { Status = 1u, Class = @class, Spec = spec, Mode = mode, Func = func };
                return 0xF0000000u | (uint)i;
            }
        }
        return 0xFFFFFFFFu;
    }

    static int EvSlot(uint ev)
    {
        int i = (int)(ev & 0xFFu);
        return i < MaxEvents ? i : -1;
    }

    static void CloseEvent(uint ev)
    {
        int s = EvSlot(ev);
        if (s >= 0) _evCBs[s] = default;
    }

    static uint WaitEvent(uint ev)
    {
        int s = EvSlot(ev);
        if (s >= 0 && _evCBs[s].Status == 4u) _evCBs[s].Status = 2u;
        return 1u;
    }

    static uint TestEvent(uint ev)
    {
        int s = EvSlot(ev);
        if (s >= 0 && _evCBs[s].Status == 4u) { _evCBs[s].Status = 2u; return 1u; }
        return 0u;
    }

    static void EnableEvent(uint ev)
    {
        int s = EvSlot(ev);
        if (s >= 0) _evCBs[s].Status = 2u;
    }

    static void DisableEvent(uint ev)
    {
        int s = EvSlot(ev);
        if (s >= 0 && _evCBs[s].Status != 0u) _evCBs[s].Status = 1u;
    }

    static void UnDeliverEvent(uint @class, uint spec)
    {
        for (int i = 0; i < MaxEvents; i++)
            if (_evCBs[i].Status == 4u && _evCBs[i].Class == @class && _evCBs[i].Spec == spec)
                _evCBs[i].Status = 2u;
    }
    static uint OpenTh(uint pc, uint spFp, uint gp)
    {
        for (int i = 0; i < MaxThreads; i++)
            if (!_tcbs[i].Used) { _tcbs[i] = new TCB { Used = true }; return 0xFF000000u | (uint)i; }
        return 0xFFFFFFFFu;
    }
    static void CloseTh(uint handle)
    {
        int i = (int)(handle & 0xFFu);
        if (i < MaxThreads) _tcbs[i] = default;
    }

    public static void SysEnqIntRP(CpuContext c, IMemory m)
    {
        uint priority = c.A0 & 3u;
        uint struc = c.A1;
        c.V0 = _intChain[priority];
        m.WriteU32(struc, _intChain[priority]);
        _intChain[priority] = struc;
    }
    public static void SysDeqIntRP(CpuContext c, IMemory m)
    {
        uint priority = c.A0 & 3u;
        uint struc = c.A1;
        if (_intChain[priority] == struc)
        {
            _intChain[priority] = m.ReadU32(struc);
            c.V0 = 1u;
            return;
        }
        uint cur = _intChain[priority];
        while (cur != 0u)
        {
            uint next = m.ReadU32(cur);
            if (next == struc) { m.WriteU32(cur, m.ReadU32(struc)); c.V0 = 1u; return; }
            cur = next;
        }
        c.V0 = 0u;
    }
}
