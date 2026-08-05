using RecompOne.Runtime.Context;
using RecompOne.Runtime.Diagnostics;
using RecompOne.Runtime.Host;
using RecompOne.Runtime.Memory;
using System.Linq;

namespace RecompOne.Runtime.Sdk;

/// <summary>
/// HLE / mini-interpreter for GOOL opcode-49 native MIPS fragments paged in from NSF.
/// </summary>
public static class LibGool
{
    static int _natLog;
    static int _interpLog;
    static int _introNativeTrace;
    public static int _d4Log;
    public static int _zoneLog;
    public static int _pathHelperLog;
    const int MaxOps = 65536;
    static readonly List<uint> _frameVAs = new();

    /// <summary>Dump+clear the set of interpreted VAs seen since the last dump.</summary>
    public static void DumpFrameVAs()
    {
        lock (_frameVAs)
        {
            if (_frameVAs.Count > 0)
            {
                var msg = "interpVAs " + string.Join(' ', _frameVAs.Select(a => $"0x{a:X8}"));
                Console.WriteLine("[boot] " + msg);
                Diagnostics.BootLog.Write(msg);
                _frameVAs.Clear();
            }
        }
    }

    /// <summary>
    /// Intro NSF <c>S000001C</c> GOOL entry <c>CdahS</c> native @ VA 0x8010DDB0
    /// (kept as explicit HLE; interpreter also covers this VA).
    /// </summary>
    public static void NativeCdahS0(CpuContext c, IMemory m)
    {
        bool log = _natLog < 8;
        if (log)
        {
            BootLog.Write($"GOOL native CdahS@0x8010DDB0 s0=0x{c.S0:X8}");
            if (_natLog == 0)
            {
                // One-shot disasm of the NSF native + the stuck PC at obj+0x14.
                var sb = new System.Text.StringBuilder("CdahS code");
                for (uint i = 0; i < 24; i++)
                    sb.Append($" {m.ReadU32(0x8010DDB0u + i * 4):X8}");
                BootLog.Write(sb.ToString());
                Console.WriteLine("[boot] " + sb);
                uint pc14 = m.ReadU32(c.S0 + 0x14u);
                if (pc14 != 0)
                {
                    var sb2 = new System.Text.StringBuilder($"obj+14 PC=0x{pc14:X8} words");
                    for (uint i = 0; i < 16; i++)
                        sb2.Append($" {m.ReadU32(pc14 + i * 4):X8}");
                    BootLog.Write(sb2.ToString());
                    Console.WriteLine("[boot] " + sb2);
                }
                var sb3 = new System.Text.StringBuilder("CdahS2@0x8010DE20");
                for (uint i = 0; i < 96; i++)
                    sb3.Append($" {m.ReadU32(0x8010DE20u + i * 4):X8}");
                BootLog.Write(sb3.ToString());
                Console.WriteLine("[boot] " + sb3);
                var sb4 = new System.Text.StringBuilder("DCF8 words");
                for (uint i = 0; i < 48; i++)
                    sb4.Append($" {m.ReadU32(0x8010DCF8u + i * 4):X8}");
                BootLog.Write(sb4.ToString());
                Console.WriteLine("[boot] " + sb4);
                var sb5 = new System.Text.StringBuilder("DE14 words");
                for (uint i = 0; i < 16; i++)
                    sb5.Append($" {m.ReadU32(0x8010DE14u + i * 4):X8}");
                BootLog.Write(sb5.ToString());
                Console.WriteLine("[boot] " + sb5);
            }
            _natLog++;
        }
        TryInterpretNative(c, m, 0x8010DDB0u);
        if (log)
            BootLog.Write($"CdahS return s5=0x{c.S5:X8} s6=0x{c.S6:X8} c0=0x{m.ReadU32(c.S0 + 0xC0u):X8}");
    }

    /// <summary>
    /// Run guest MIPS from RAM until <c>jr ra</c>. Covers GOOL opcode-49 NSF blobs and
    /// unmapped EXE mid-entries (jump-table tails left as dead code by the recompiler).
    /// <c>jal</c> routes through <see cref="Dispatch.Dispatcher.Call"/> so recompiled callees work.
    /// </summary>
    public static bool TryInterpretNative(CpuContext c, IMemory m, uint addr)
    {
        // Main EXE text + NSF page window.
        if (addr < 0x80010000u || addr >= 0x80200000u)
            return false;

        uint first = m.ReadU32(addr);
        // A few legitimate GOOL native blobs begin with one or more NOPs. Only reject
        // a zero entry when the nearby body is empty as well; the interpreter already
        // handles NOP correctly. Keep rejecting the NSF/GOOL marker word.
        bool emptyPrefix = first == 0u;
        if (emptyPrefix)
        {
            for (uint i = 1; i < 8 && emptyPrefix; i++)
                emptyPrefix = m.ReadU32(addr + i * 4u) == 0u;
        }
        if (emptyPrefix || first == 0x49BE0BE0u)
            return false;

        bool traceIntroNative = addr >= 0x800ECA00u && addr < 0x800ECC00u
            && _introNativeTrace++ < 8;
        uint traceStartS5 = c.S5;
        if (traceIntroNative)
        {
            var tb = new System.Text.StringBuilder($"INTRO NATIVE enter addr=0x{addr:X8} ra=0x{c.RA:X8} s5=0x{c.S5:X8} words");
            for (uint i = 0; i < 32; i++) tb.Append($" {m.ReadU32(addr + i * 4u):X8}");
            BootLog.Write(tb.ToString());
        }

        if (_interpLog < 32)
        {
            BootLog.Write($"MIPS interp @ 0x{addr:X8} s0=0x{c.S0:X8}");
            _interpLog++;
        }
        lock (_frameVAs) if (!_frameVAs.Contains(addr)) _frameVAs.Add(addr);

        // NSF / mid-entry fragments run on the live CpuContext. Intermediate recompiled
        // callers only spill the S-regs they themselves use, so unchecked clobbers here
        // leak into grandcallers (seen: main-loop S3/S4 trashed after CdahS → bogus
        // level-reload and DrawHold=0x1F800000). Preserve MIPS callee-saved state.
        // Exception — GOOL opcode-49 natives (jalr s5 from L8003AD6C) intentionally leave:
        //   S5 = next bytecode PC (via jalr s5,ra link past the blob)
        //   S6/S7 = expression-stack pointers (CdahS2 does addiu s6 / sw s6,0xBC(s0))
        // Restoring those resumes inside the native as bytecode or desyncs the GOOL stack.
        // NSF pages are loaded from roughly 0x80080000 upward in this build (the Intro
        // uses a native at 0x800EA344). These calls intentionally return the next GOOL
        // PC in S5; treating them as EXE mid-entries restores the native entry address
        // and makes the interpreter consume MIPS words as GOOL bytecode.
        bool goolNative = c.RA == 0x8003AD74u
            && addr >= 0x80080000u && addr < 0x80200000u;
        // 0x800409F4 is a shared mid-entry of the mesh packet emitter. Unlike a normal
        // MIPS callee it deliberately advances S7 to the next GPU packet before its
        // tail return; restoring S7 makes every triangle overwrite the same packet.
        bool meshPacketEmitter = addr == 0x800409F4u;
        uint saveS0 = c.S0, saveS1 = c.S1, saveS2 = c.S2, saveS3 = c.S3;
        uint saveS4 = c.S4, saveS5 = c.S5, saveS6 = c.S6, saveS7 = c.S7;
        uint saveSP = c.SP, saveRA = c.RA, saveFP = c.FP, saveGP = c.GP;
        uint pc = addr;
        try
        {
        for (int n = 0; n < MaxOps; n++)
        {
            // Interpreted EXE/NSF fragments can busy-wait between PresentFrame calls.
            // Keep the window responsive and let async CD state advance so intro loads
            // do not stall waiting on callbacks that only used to move during VBlank.
            if ((n & 0xFF) == 0)
            {
                HostWindow.KeepAlive();
                LibCd.Tick();
            }

            uint instr = m.ReadU32(pc);
            uint next = pc + 4u;
            uint op = instr >> 26;
            uint rs = (instr >> 21) & 31u;
            uint rt = (instr >> 16) & 31u;
            uint rd = (instr >> 11) & 31u;
            uint sa = (instr >> 6) & 31u;
            uint funct = instr & 0x3Fu;
            int imm = (short)(instr & 0xFFFFu);
            uint uimm = instr & 0xFFFFu;

            void RunDelay()
            {
                uint di = m.ReadU32(next);
                if (!ExecSimple(c, m, di))
                {
                    // Delay-slot branch/jump is rare; try full step via nested handling below.
                    // For now treat unsupported delay as hard fail.
                    throw new InvalidOperationException(
                        $"MIPS interp bad delay 0x{di:X8} @ 0x{next:X8} (entry 0x{addr:X8})");
                }
            }

            // jr rs / jalr rd, rs
            if (op == 0 && funct == 0x08) // jr
            {
                uint target = Get(c, rs);
                RunDelay();
                if (rs == 31) // jr ra → return
                    return true;
                // Tail-ish jr to another routine
                RecompOne.Runtime.Dispatch.Dispatcher.Call(c, m, target);
                return true;
            }
            if (op == 0 && funct == 0x09) // jalr
            {
                uint target = Get(c, rs);
                uint link = next + 4u;
                RunDelay();
                Set(c, rd == 0 ? 31u : rd, link);
                if (rs == 31) // jalr …, ra → return
                    return true;
                RecompOne.Runtime.Dispatch.Dispatcher.Call(c, m, target);
                pc = link;
                continue;
            }

            // j / jal
            if (op == 2 || op == 3)
            {
                uint target = (next & 0xF0000000u) | ((instr & 0x03FFFFFFu) << 2);
                RunDelay();
                if (op == 3)
                {
                    c.RA = next + 4u;
                    RecompOne.Runtime.Dispatch.Dispatcher.Call(c, m, target);
                    pc = next + 4u;
                    continue;
                }

                // j: tail into a known function, else keep interpreting (mid-label)
                if (RecompOne.Runtime.Dispatch.RasterContinue.TryJump(target))
                    return true;
                if (RecompOne.Runtime.Dispatch.Dispatcher.IsMapped(target))
                {
                    RecompOne.Runtime.Dispatch.Dispatcher.Call(c, m, target);
                    return true;
                }
                pc = target;
                continue;
            }

            // branches
            if (op is 1 or 4 or 5 or 6 or 7)
            {
                bool take = op switch
                {
                    4 => Get(c, rs) == Get(c, rt),                          // beq
                    5 => Get(c, rs) != Get(c, rt),                          // bne
                    6 => (int)Get(c, rs) <= 0,                              // blez
                    7 => (int)Get(c, rs) > 0,                               // bgtz
                    1 => rt switch                                          // regimm
                    {
                        0 => (int)Get(c, rs) < 0,                           // bltz
                        1 => (int)Get(c, rs) >= 0,                          // bgez
                        16 => (int)Get(c, rs) < 0,                          // bltzal
                        17 => (int)Get(c, rs) >= 0,                         // bgezal
                        _ => false,
                    },
                    _ => false,
                };
                bool link = op == 1 && (rt == 16 || rt == 17);
                uint dest = next + ((uint)imm << 2);
                RunDelay();
                if (link)
                    c.RA = next + 4u;
                pc = take ? dest : next + 4u;
                continue;
            }

            // COP2 / GTE
            if (op == 0x12)
            {
                if (!ExecCop2(c, instr))
                {
                    BootLog.Write($"MIPS interp unsupported COP2 0x{instr:X8} @ 0x{pc:X8} (entry 0x{addr:X8})");
                    return false;
                }
                pc = next;
                continue;
            }

            // LWC2 / SWC2 — GTE data register <-> memory
            if (op == 0x32) // LWC2 rt, imm(rs)
            {
                uint ea = unchecked(Get(c, rs) + (uint)imm);
                RecompOne.Runtime.Gte.LoadWord((int)rt, m.ReadU32(ea));
                pc = next;
                continue;
            }
            if (op == 0x3A) // SWC2 rt, imm(rs)
            {
                uint ea = unchecked(Get(c, rs) + (uint)imm);
                m.WriteU32(ea, RecompOne.Runtime.Gte.StoreWord((int)rt));
                pc = next;
                continue;
            }

            if (!ExecSimple(c, m, instr))
            {
                BootLog.Write($"MIPS interp unsupported op 0x{instr:X8} @ 0x{pc:X8} (entry 0x{addr:X8})");
                return false;
            }
            pc = next;
        }

        BootLog.Write($"MIPS interp timeout @ 0x{addr:X8}");
        return false;
        }
        catch (Exception ex)
        {
            uint word = 0u;
            try { word = m.ReadU32(pc); } catch { }
            BootLog.Write($"MIPS interp FAIL entry=0x{addr:X8} pc=0x{pc:X8} instr=0x{word:X8} " +
                          $"ra=0x{c.RA:X8} sp=0x{c.SP:X8} {ex.GetType().Name}: {ex.Message}");
            throw;
        }
        finally
        {
            if (traceIntroNative)
                BootLog.Write($"INTRO NATIVE leave addr=0x{addr:X8} startS5=0x{traceStartS5:X8} endS5=0x{c.S5:X8} s6=0x{c.S6:X8} s7=0x{c.S7:X8}");
            c.S0 = saveS0; c.S1 = saveS1; c.S2 = saveS2; c.S3 = saveS3;
            c.S4 = saveS4;
            if (!goolNative)
            {
                c.S5 = saveS5; c.S6 = saveS6;
                if (!meshPacketEmitter)
                    c.S7 = saveS7;
            }
            // Interpreted fragments are entered as MIPS callees. Even if an incomplete
            // path misses its epilogue, never leak its temporary frame/link to the host
            // recompiled caller.
            c.SP = saveSP; c.RA = saveRA; c.FP = saveFP; c.GP = saveGP;
        }
    }

    static bool ExecCop2(CpuContext c, uint instr)
    {
        // COP2 move / GTE command
        uint rs = (instr >> 21) & 31u;
        uint rt = (instr >> 16) & 31u;
        uint rd = (instr >> 11) & 31u;

        if (((instr >> 25) & 1u) != 0)
        {
            RecompOne.Runtime.Gte.Execute(instr);
            return true;
        }

        switch (rs)
        {
            case 0: // MFC2
                Set(c, rt, RecompOne.Runtime.Gte.Read((int)rd));
                return true;
            case 2: // CFC2
                Set(c, rt, RecompOne.Runtime.Gte.ReadControl((int)rd));
                return true;
            case 4: // MTC2
                RecompOne.Runtime.Gte.Write((int)rd, Get(c, rt));
                return true;
            case 6: // CTC2
                RecompOne.Runtime.Gte.WriteControl((int)rd, Get(c, rt));
                return true;
            default:
                return false;
        }
    }

    static bool ExecSimple(CpuContext c, IMemory m, uint instr)
    {
        if (instr == 0) return true; // nop

        uint op = instr >> 26;
        uint rs = (instr >> 21) & 31u;
        uint rt = (instr >> 16) & 31u;
        uint rd = (instr >> 11) & 31u;
        uint sa = (instr >> 6) & 31u;
        uint funct = instr & 0x3Fu;
        int imm = (short)(instr & 0xFFFFu);
        uint uimm = instr & 0xFFFFu;

        if (op == 0)
        {
            switch (funct)
            {
                case 0x00: Set(c, rd, Get(c, rt) << (int)sa); return true;                 // sll
                case 0x02: Set(c, rd, Get(c, rt) >> (int)sa); return true;                 // srl
                case 0x03: Set(c, rd, (uint)((int)Get(c, rt) >> (int)sa)); return true;    // sra
                case 0x04: Set(c, rd, Get(c, rt) << (int)(Get(c, rs) & 31)); return true;  // sllv
                case 0x06: Set(c, rd, Get(c, rt) >> (int)(Get(c, rs) & 31)); return true;  // srlv
                case 0x07: Set(c, rd, (uint)((int)Get(c, rt) >> (int)(Get(c, rs) & 31))); return true; // srav
                case 0x10: Set(c, rd, c.HI); return true;                                  // mfhi
                case 0x11: c.HI = Get(c, rs); return true;                                 // mthi
                case 0x12: Set(c, rd, c.LO); return true;                                  // mflo
                case 0x13: c.LO = Get(c, rs); return true;                                 // mtlo
                case 0x18: // mult
                {
                    long r = (long)(int)Get(c, rs) * (int)Get(c, rt);
                    c.LO = (uint)r; c.HI = (uint)(r >> 32); return true;
                }
                case 0x19: // multu
                {
                    ulong r = (ulong)Get(c, rs) * Get(c, rt);
                    c.LO = (uint)r; c.HI = (uint)(r >> 32); return true;
                }
                case 0x1A: // div
                {
                    int a = (int)Get(c, rs), b = (int)Get(c, rt);
                    if (b != 0) { c.LO = (uint)(a / b); c.HI = (uint)(a % b); }
                    return true;
                }
                case 0x1B: // divu
                {
                    uint a = Get(c, rs), b = Get(c, rt);
                    if (b != 0) { c.LO = a / b; c.HI = a % b; }
                    return true;
                }
                case 0x20: // add
                case 0x21: Set(c, rd, unchecked(Get(c, rs) + Get(c, rt))); return true;   // addu
                case 0x22: // sub
                case 0x23: Set(c, rd, unchecked(Get(c, rs) - Get(c, rt))); return true;   // subu
                case 0x24: Set(c, rd, Get(c, rs) & Get(c, rt)); return true;               // and
                case 0x25: Set(c, rd, Get(c, rs) | Get(c, rt)); return true;               // or
                case 0x26: Set(c, rd, Get(c, rs) ^ Get(c, rt)); return true;               // xor
                case 0x27: Set(c, rd, ~(Get(c, rs) | Get(c, rt))); return true;            // nor
                case 0x2A: Set(c, rd, (int)Get(c, rs) < (int)Get(c, rt) ? 1u : 0u); return true; // slt
                case 0x2B: Set(c, rd, Get(c, rs) < Get(c, rt) ? 1u : 0u); return true;     // sltu
                default: return false;
            }
        }

        switch (op)
        {
            case 0x08: // addi
            case 0x09: Set(c, rt, unchecked(Get(c, rs) + (uint)imm)); return true;         // addiu
            case 0x0A: Set(c, rt, (int)Get(c, rs) < imm ? 1u : 0u); return true;           // slti
            case 0x0B: Set(c, rt, Get(c, rs) < (uint)imm ? 1u : 0u); return true;          // sltiu (sign-ext imm as unsigned compare per MIPS)
            case 0x0C: Set(c, rt, Get(c, rs) & uimm); return true;                         // andi
            case 0x0D: Set(c, rt, Get(c, rs) | uimm); return true;                         // ori
            case 0x0E: Set(c, rt, Get(c, rs) ^ uimm); return true;                         // xori
            case 0x0F: Set(c, rt, uimm << 16); return true;                                // lui
            case 0x20: Set(c, rt, (uint)(sbyte)m.ReadU8(unchecked(Get(c, rs) + (uint)imm))); return true; // lb
            case 0x21: Set(c, rt, (uint)(short)m.ReadU16(unchecked(Get(c, rs) + (uint)imm))); return true; // lh
            case 0x22: // lwl
            {
                uint ea = unchecked(Get(c, rs) + (uint)imm);
                uint aligned = ea & ~3u;
                int shift = (int)((3u - (ea & 3u)) * 8);
                uint mem = m.ReadU32(aligned);
                uint cur = Get(c, rt);
                uint mask = 0xFFFFFFFFu << shift;
                Set(c, rt, (cur & ~mask) | (mem << shift));
                return true;
            }
            case 0x23: Set(c, rt, m.ReadU32(unchecked(Get(c, rs) + (uint)imm))); return true; // lw
            case 0x24: Set(c, rt, m.ReadU8(unchecked(Get(c, rs) + (uint)imm))); return true;  // lbu
            case 0x25: Set(c, rt, m.ReadU16(unchecked(Get(c, rs) + (uint)imm))); return true; // lhu
            case 0x26: // lwr
            {
                uint ea = unchecked(Get(c, rs) + (uint)imm);
                int shift = (int)((ea & 3u) * 8);
                uint mem = m.ReadU32(ea & ~3u);
                uint cur = Get(c, rt);
                uint mask = 0xFFFFFFFFu >> shift;
                Set(c, rt, (cur & ~mask) | (mem >> shift));
                return true;
            }
            case 0x28: m.WriteU8(unchecked(Get(c, rs) + (uint)imm), (byte)Get(c, rt)); return true; // sb
            case 0x29: m.WriteU16(unchecked(Get(c, rs) + (uint)imm), (ushort)Get(c, rt)); return true; // sh
            case 0x2A: // swl
            {
                uint ea = unchecked(Get(c, rs) + (uint)imm);
                uint aligned = ea & ~3u;
                int n = (int)(ea & 3u);
                uint val = Get(c, rt);
                for (int i = 0; i <= n; i++)
                    m.WriteU8(aligned + (uint)i, (byte)(val >> ((3 - n + i) * 8)));
                return true;
            }
            case 0x2B: m.WriteU32(unchecked(Get(c, rs) + (uint)imm), Get(c, rt)); return true; // sw
            case 0x2E: // swr
            {
                uint ea = unchecked(Get(c, rs) + (uint)imm);
                uint aligned = ea & ~3u;
                int n = (int)(ea & 3u);
                uint val = Get(c, rt);
                for (int i = n; i < 4; i++)
                    m.WriteU8(aligned + (uint)i, (byte)(val >> ((i - n) * 8)));
                return true;
            }
            default: return false;
        }
    }

    static uint Get(CpuContext c, uint reg) => reg switch
    {
        0 => 0,
        2 => c.V0, 3 => c.V1,
        4 => c.A0, 5 => c.A1, 6 => c.A2, 7 => c.A3,
        8 => c.T0, 9 => c.T1, 10 => c.T2, 11 => c.T3, 12 => c.T4, 13 => c.T5, 14 => c.T6, 15 => c.T7,
        16 => c.S0, 17 => c.S1, 18 => c.S2, 19 => c.S3, 20 => c.S4, 21 => c.S5, 22 => c.S6, 23 => c.S7,
        24 => c.T8, 25 => c.T9,
        28 => c.GP, 29 => c.SP, 30 => c.FP, 31 => c.RA,
        _ => 0,
    };

    static void Set(CpuContext c, uint reg, uint value)
    {
        switch (reg)
        {
            case 0: break;
            case 2: c.V0 = value; break; case 3: c.V1 = value; break;
            case 4: c.A0 = value; break; case 5: c.A1 = value; break;
            case 6: c.A2 = value; break; case 7: c.A3 = value; break;
            case 8: c.T0 = value; break; case 9: c.T1 = value; break;
            case 10: c.T2 = value; break; case 11: c.T3 = value; break;
            case 12: c.T4 = value; break; case 13: c.T5 = value; break;
            case 14: c.T6 = value; break; case 15: c.T7 = value; break;
            case 16: c.S0 = value; break; case 17: c.S1 = value; break;
            case 18: c.S2 = value; break; case 19: c.S3 = value; break;
            case 20: c.S4 = value; break; case 21: c.S5 = value; break;
            case 22: c.S6 = value; break; case 23: c.S7 = value; break;
            case 24: c.T8 = value; break; case 25: c.T9 = value; break;
            case 28: c.GP = value; break; case 29: c.SP = value; break;
            case 30: c.FP = value; break; case 31: c.RA = value; break;
        }
    }
}
