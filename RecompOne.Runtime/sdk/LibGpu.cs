using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Hle;
using RecompOne.Runtime.Memory;
using System.Linq;

namespace RecompOne.Runtime.Sdk;

public static class LibGpu
{
    static readonly DrawEnvEvent _drawEnvEvent = new();
    static readonly DispEnvEvent _dispEnvEvent = new();
    static int _drawEnvLog;
    static int _dispEnvLog;
    static int _otCount;

    static int _drawLog;
    static int _drawSkipLog;
    static int _introOtClearLog;
    static int _introPacketLog;

    /// <summary>Project OT clear convention: link forward from the buffer structure's OT head.</summary>
    public static void ClearOTagR(IMemory m, uint ot, uint n)
    {
        if (n == 0) return;
        uint mask = 0x00FFFFFFu;
        uint cur = ot & mask;
        for (uint i = 0; i + 1 < n; i++)
        {
            uint next = (cur + 4u) & mask;
            m.WriteU32(ot + i * 4u, next);
            cur = next;
        }
        m.WriteU32(ot + (n - 1u) * 4u, 0x00FFFFFFu);
    }

    static void EnsureIntroOtCleared(IMemory m, uint otBase)
    {
        // Title calls ClearOTagR every frame via func_80016328; Intro never does, so the
        // OT heads keep leftover self-looping prims (SHORT nodes=2 hdr→self) and submit
        // almost nothing. Clear only the OT just drawn so the other buffer can keep building.
        ClearOTagR(m, otBase, 0x800u);
        if (_introOtClearLog < 8)
        {
            Diagnostics.BootLog.Write($"Intro OT force-clear ot=0x{otBase:X8}");
            _introOtClearLog++;
        }
    }

    public static void DrawOTag(CpuContext c, IMemory m)
    {
        var gpu = Runtime.Gpu;
        if (gpu == null) return;

        // PsyQ ClearOTag links the last slot to this static end-tag. Retail BSS
        // seeds it as 0x04FFFFFF (4 pad words, next=FFFFFF) — that is valid.
        // Only repair when the next-link is no longer a terminator (seen: OT walk
        // escaping into RAM for ~1M nodes / ~65s per Intro frame).
        const uint OtEndTagVa = 0x8005E070u;
        {
            uint endTag = m.ReadU32(OtEndTagVa);
            if ((endTag & 0x00FFFFFFu) != 0x00FFFFFFu)
            {
                Diagnostics.BootLog.Write($"DrawOTag repair end-tag @ 0x{OtEndTagVa:X8} was 0x{endTag:X8}");
                m.WriteU32(OtEndTagVa, 0x04FFFFFFu);
            }
        }

        uint otBase = c.A0;
        uint addr = otBase & 0x1FFFFCu;
        int nodes = 0, words = 0;
        var ops = new int[256];
        uint firstHdr = 0, secondHdr = 0;
        Gpu.SoftCmdIndex = 0;
        Gpu.SoftLastFillAt = -1;
        Gpu.SoftLastPolyAt = -1;
        // Title/Intro OTs are depth ~2048 (double buffers @ 0x8006370C / 0x800657A0).
        const int MaxNodes = 8192;
        const uint OtEndTagBus = OtEndTagVa & 0x1FFFFCu;
        bool runaway = false;
        var visited = new System.Collections.Generic.HashSet<uint>();
        for (int guard = 0; guard < MaxNodes; guard++)
        {
            if (addr == OtEndTagBus)
                break;
            // Guest primitive insertion can form a longer cycle (packet -> OT -> packet).
            // Stop before executing the same packet again; the old direct-self check
            // missed these and replayed corrupt commands until MaxNodes.
            if (!visited.Add(addr))
            {
                runaway = true;
                break;
            }

            uint header = m.ReadU32(addr);
            if (nodes == 0) firstHdr = header;
            else if (nodes == 1) secondHdr = header;
            uint count = header >> 24;
            // Defensive: corrupt RAM as OT can claim absurd packet sizes.
            if (count > 64) count = 0;
            if (count > 0 && _introPacketLog < 8 && (m.ReadU32(addr + 4u) >> 24) == 0x24u)
            {
                var packetWords = string.Join(' ', Enumerable.Range(0, (int)count)
                    .Select(i => $"{m.ReadU32(addr + 4u + (uint)i * 4u):X8}"));
                Diagnostics.BootLog.Write($"Intro GP0 packet addr=0x{addr:X6} count={count} words={packetWords}");
                _introPacketLog++;
            }
            for (uint i = 0; i < count; i++)
            {
                uint w = m.ReadU32(addr + 4u + i * 4u);
                if (i == 0) Gpu.SoftCmdIndex++;
                gpu.WriteGp0(w);
                if (i == 0) ops[w >> 24]++;
                words++;
            }
            nodes++;
            uint next = header & 0xFFFFFFu;
            // Retail terminator is 0x00FFFFFF. next==0 is also invalid (walks from addr 0).
            if (next == 0u || next == 0xFFFFFFu || (next & 0x800000u) != 0) break;
            uint nextAddr = next & 0x1FFFFCu;
            if (nextAddr == addr || nextAddr == OtEndTagBus) break;
            addr = nextAddr;
            if (guard + 1 >= MaxNodes)
                runaway = true;
        }

        if (runaway && _drawLog < 60)
        {
            Diagnostics.BootLog.Write(
                $"DrawOTag RUNAWAY ot=0x{otBase:X8} nodes={nodes} hdr0=0x{firstHdr:X8} hdr1=0x{secondHdr:X8} last=0x{addr:X8}");
            Console.WriteLine($"[boot] DrawOTag RUNAWAY ot=0x{otBase:X8} nodes={nodes}");
        }
        else if (nodes <= 4 && _drawLog < 60)
        {
            Diagnostics.BootLog.Write(
                $"DrawOTag SHORT ot=0x{otBase:X8} nodes={nodes} hdr0=0x{firstHdr:X8} hdr1=0x{secondHdr:X8}");
        }

        GpuHle.Backend?.Flush();
        GpuHle.Backend?.LatchFrame();
        // Sticky soft present: latch the draw FB after OT completes (no mid-FillRect).
        gpu.LatchSoftDrawBuffer();
        // Continuous interp-VA census (survives early window closes).
        if ((++_otCount % 30) == 0) LibGool.DumpFrameVAs();

        // Generous logging through title→intro so we can see submit resume/stall.
        if (_drawLog < 60)
        {
            var top = string.Join(' ',
                Enumerable.Range(0, 256)
                    .Where(i => ops[i] > 0)
                    .OrderByDescending(i => ops[i])
                    .Take(8)
                    .Select(i => $"0x{i:X2}:{ops[i]}"));
            Diagnostics.BootLog.Write($"DrawOTag ot=0x{otBase:X8} nodes={nodes} words={words} ops=[{top}]");
            if (_drawLog < 8 || (_drawLog % 5) == 0 || runaway)
                Console.WriteLine($"[boot] DrawOTag ot=0x{otBase:X8} nodes={nodes} words={words}");
            _drawLog++;
        }

        // Intro skips the title ClearOTagR path — force-clear the drawn OT for next use.
        uint level = m.ReadU32(0x8005F684u);
        if (level == 0x1Cu)
        {
            Bios.BiosB.AdvanceIntroFrameTimer(m);
            EnsureIntroOtCleared(m, otBase);
        }
    }

    public static void ResetDrawLogBudget()
    {
        _drawLog = 0;
        _drawSkipLog = 0;
        _introPacketLog = 0;
        _drawEnvLog = 0;
        _dispEnvLog = 0;
    }

    /// <summary>Diag: guest skipped DrawOTag because hold != 0.</summary>
    public static void NoteDrawHoldSkip(IMemory m, uint hold)
    {
        if (_drawSkipLog >= 8) return;
        _drawSkipLog++;
        var msg = $"DrawOTag SKIP hold=0x{hold:X8}";
        Console.WriteLine("[boot] " + msg);
        Diagnostics.BootLog.Write(msg);
    }

    public static void DrawSync(CpuContext c, IMemory m) => c.V0 = 0;

    /// <summary>
    /// PsyQ SetDispMask — blank (A0==0) or enable (A0!=0) the display via GP1(03h).
    /// Hardware: bit0 0=display on, 1=display off (GPUSTAT.23).
    /// </summary>
    public static void SetDispMask(CpuContext c, IMemory m)
    {
        Diagnostics.BootLog.Write($"SetDispMask a0={c.A0}");
        var gpu = Runtime.Gpu;
        if (gpu != null)
            gpu.WriteGp1(c.A0 != 0 ? 0x03000000u : 0x03000001u);
        c.V0 = c.A0;
    }

    /// <summary>
    /// Stub for PsyQ GPU DMA/IRQ callbacks that ResetGraph installs from GetB0Table()+0x884/+0x894.
    /// Real BIOS ROM is not present under recomp; these are no-ops until proper HLE exists.
    /// </summary>
    public static void GpuBiosCallback(CpuContext c, IMemory m) { }

    /// <summary>
    /// Present + throttle. Used as a frame-pump hook when the game loop does not call VSync.
    /// </summary>
    static int _pumpFrames;

    public static void PresentPump(CpuContext c, IMemory m)
    {
        Runtime.PresentFrame();
        // Title mode skips guest PadUpdate; synth edges once per game frame here
        // (not inside every VSync PresentFrame, which would eat rising edges).
        Bios.BiosB.SynthTitlePadEdges(m);
        // Keep $gp sane — recompiled scratch sometimes clobbers it; title→Intro load
        // paths read through GP+4 and hard-fault on garbage (seen as 0x0970xxxx).
        const uint RetailGp = 0x8005F414u; // 0x80060000 - 0xBEC
        if (c.GP < 0x80010000u || c.GP >= 0x80200000u)
            c.GP = RetailGp;
        // Headless: same HLE as pad Z, after title starfield has been running.
        if (_pumpFrames == 150 && m.ReadU32(0x8005F684u) == 0x3Cu
            && m.ReadU32(0x8005F688u) == 0xFFFFFFFFu)
            Bios.BiosB.ForceTitleStart(m);
        if (_pumpFrames < 360 && (_pumpFrames % 30) == 0)
        {
            Console.WriteLine($"[boot] PresentPump frame={_pumpFrames}");
            Diagnostics.BootLog.Write($"PresentPump frame={_pumpFrames}");
            var gpu = Runtime.Gpu;
            // Sample once on title and again after Intro should be running.
            if (gpu != null && (_pumpFrames == 30 || _pumpFrames == 90 || _pumpFrames == 120 || _pumpFrames == 150
                || _pumpFrames == 180 || _pumpFrames == 210 || _pumpFrames == 240 || _pumpFrames == 270 || _pumpFrames == 300))
            {
                int x0 = gpu.DisplayX, y0 = gpu.DisplayY;
                int w = Math.Min(gpu.DisplayWidth, 64), h = Math.Min(gpu.DisplayHeight, 64);
                int nz = 0, nzHle = 0;
                var vram = gpu.Vram;
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (vram[(y0 + y) * VramShadow.Width + (x0 + x)] != 0) nz++;

                if (Hle.GpuHle.Backend is { Ready: true } be)
                {
                    var buf = new ushort[w * h];
                    be.ReadVram(x0, y0, w, h, buf);
                    for (int i = 0; i < buf.Length; i++)
                        if (buf[i] != 0) nzHle++;

                    // Also sample the opposite double-buffer half.
                    int ox = x0 >= 512 ? 0 : 512;
                    var buf2 = new ushort[w * h];
                    be.ReadVram(ox, y0, w, h, buf2);
                    int nzOther = 0;
                    for (int i = 0; i < buf2.Length; i++)
                        if (buf2[i] != 0) nzOther++;

                    // Center 64x64 — Intro mesh often misses the top-left corner.
                    int dw = Math.Max(gpu.DisplayWidth, 1), dh = Math.Max(gpu.DisplayHeight, 1);
                    int cx = x0 + Math.Max(0, dw / 2 - 32), cy = y0 + Math.Max(0, dh / 2 - 32);
                    int cw = Math.Min(64, dw), ch = Math.Min(64, dh);
                    var bufC = new ushort[cw * ch];
                    be.ReadVram(cx, cy, cw, ch, bufC);
                    int nzCenter = 0;
                    for (int i = 0; i < bufC.Length; i++)
                        if (bufC[i] != 0) nzCenter++;

                    var msg2 = $"fbSample disp={x0},{y0} nz={nzHle} other={ox},{y0} nz={nzOther} center={cx},{cy} nz={nzCenter}/{cw * ch}";
                    Console.WriteLine($"[boot] {msg2}");
                    Diagnostics.BootLog.Write(msg2);
                }

                var msg = $"display on={gpu.DisplayEnabled} xy={x0},{y0} wh={gpu.DisplayWidth}x{gpu.DisplayHeight} shadowNZ={nz} hleNZ={nzHle}/{w * h}";
                Console.WriteLine($"[boot] {msg}");
                Diagnostics.BootLog.Write(msg);

                // Full-framebuffer pixel census on the CPU (soft) VRAM — settles
                // "soft raster draws nothing" vs "present picks the wrong half".
                int dispH2 = Math.Max(gpu.DisplayHeight, 1);
                int nzFull0 = 0, nzFull512 = 0;
                for (int y = 0; y < dispH2; y++)
                {
                    int line = ((y0 + y) & (VramShadow.Height - 1)) * VramShadow.Width;
                    for (int x = 0; x < 512; x++)
                    {
                        if (vram[line + ((x0 >= 512 ? x + 512 : x) & 1023)] != 0) nzFull0++;
                    }
                }
                int oxF = x0 >= 512 ? 0 : 512;
                for (int y = 0; y < dispH2; y++)
                {
                    int line = ((y0 + y) & (VramShadow.Height - 1)) * VramShadow.Width;
                    for (int x = 0; x < 512; x++)
                        if (vram[line + ((oxF + x) & 1023)] != 0) nzFull512++;
                }
                var msgF = $"fbFull disp half@{(x0 >= 512 ? 512 : 0)} nz={nzFull0} other half@{oxF} nz={nzFull512} (of {512 * dispH2})";
                Console.WriteLine($"[boot] {msgF}");
                Diagnostics.BootLog.Write(msgF);
                Gpu.DumpSoftStats();
                Sdk.LibGool.DumpFrameVAs();
                if (_pumpFrames == 30) Gpu.ResetSoftStats(); // measure intro-only interval
                Gpu.LogTriStats();
            }
        }
        _pumpFrames++;
    }

    public static void PutDrawEnv(CpuContext c, IMemory m)
    {
        var gpu = Runtime.Gpu;
        if (gpu == null) { c.V0 = c.A0; return; }

        uint env = c.A0;
        short clipX = S16(m, env + 0x00), clipY = S16(m, env + 0x02);
        short clipW = S16(m, env + 0x04), clipH = S16(m, env + 0x06);
        short ofsX = S16(m, env + 0x08), ofsY = S16(m, env + 0x0A);
        short twX = S16(m, env + 0x0C), twY = S16(m, env + 0x0E);
        short twW = S16(m, env + 0x10), twH = S16(m, env + 0x12);
        ushort tpage = m.ReadU16(env + 0x14);
        byte dtd = m.ReadU8(env + 0x16);
        byte dfe = m.ReadU8(env + 0x17);
        byte isbg = m.ReadU8(env + 0x18);
        byte r0 = m.ReadU8(env + 0x19), g0 = m.ReadU8(env + 0x1A), b0 = m.ReadU8(env + 0x1B);

        if (_drawEnvLog < 24)
        {
            Diagnostics.BootLog.Write($"PutDrawEnv clip={clipX},{clipY} {clipW}x{clipH} ofs={ofsX},{ofsY} isbg={isbg} rgb={r0},{g0},{b0}");
            if (_drawEnvLog < 12)
                Console.WriteLine($"[boot] PutDrawEnv clip={clipX},{clipY} ofs={ofsX},{ofsY} isbg={isbg}");
            _drawEnvLog++;
        }

        gpu.WriteGp0(GetCs(clipX, clipY));
        gpu.WriteGp0(GetCe((short)(clipX + clipW - 1), (short)(clipY + clipH - 1)));
        gpu.WriteGp0(GetOfs(ofsX, ofsY));
        gpu.WriteGp0(GetMode(dfe, dtd, tpage));
        gpu.WriteGp0(GetTw(twX, twY, twW, twH));
        gpu.WriteGp0(0xE6000000u);

        if (isbg != 0)
        {
            int margin = GpuHle.WideMargin(clipW);
            int w = Math.Clamp(clipW + margin * 2, 0, VramShadow.Width - 1);
            int h = Math.Clamp((int)clipH, 0, VramShadow.Height - 1);
            int x = clipX - margin - ofsX, y = clipY - ofsY;
            gpu.WriteGp0(0x60000000u | ((uint)b0 << 16) | ((uint)g0 << 8) | r0);
            gpu.WriteGp0(((uint)(ushort)y << 16) | (ushort)x);
            gpu.WriteGp0(((uint)(ushort)h << 16) | (ushort)w);
        }

        if (Event.HasAnyListeners<DrawEnvEvent>())
        {
            var e = _drawEnvEvent;
            e.Context = c; e.Memory = m;
            e.ClipX = clipX; e.ClipY = clipY; e.ClipW = clipW; e.ClipH = clipH;
            e.OfsX = ofsX; e.OfsY = ofsY; e.IsBackground = isbg != 0;
            Event.Dispatch(e);
        }

        c.V0 = c.A0;
    }

    public static void PutDispEnv(CpuContext c, IMemory m)
    {
        var gpu = Runtime.Gpu;
        if (gpu == null) { c.V0 = c.A0; return; }

        uint env = c.A0;
        short dispX = S16(m, env + 0x00), dispY = S16(m, env + 0x02);
        short dispW = S16(m, env + 0x04), dispH = S16(m, env + 0x06);
        short scrX = S16(m, env + 0x08), scrY = S16(m, env + 0x0A);
        short scrW = S16(m, env + 0x0C), scrH = S16(m, env + 0x0E);
        byte isinter = m.ReadU8(env + 0x10);
        byte isrgb24 = m.ReadU8(env + 0x11);
        bool pal = gpu.Pal;

        if (_dispEnvLog < 24)
        {
            Diagnostics.BootLog.Write($"PutDispEnv disp={dispX},{dispY} {dispW}x{dispH} scr={scrX},{scrY} {scrW}x{scrH} inter={isinter} rgb24={isrgb24}");
            if (_dispEnvLog < 12)
                Console.WriteLine($"[boot] PutDispEnv disp={dispX},{dispY} {dispW}x{dispH}");
            _dispEnvLog++;
        }

        gpu.WriteGp1(0x05000000u | (((uint)dispY & 0x3FF) << 10) | ((uint)dispX & 0x3FF));

        int hStart = scrX * 10 + 0x260;
        int vStart = scrY + (pal ? 0x13 : 0x10);
        int hEnd = hStart + (scrW != 0 ? scrW * 10 : 2560);
        int vEnd = vStart + (scrH != 0 ? scrH : 240);
        hStart = Math.Clamp(hStart, 500, 3290);
        hEnd = Math.Clamp(hEnd, hStart + 0x50, 3290);
        vStart = Math.Clamp(vStart, 0x10, pal ? 310 : 256);
        vEnd = Math.Clamp(vEnd, vStart + 2, pal ? 312 : 258);
        gpu.WriteGp1(0x06000000u | (((uint)hEnd & 0xFFF) << 12) | ((uint)hStart & 0xFFF));
        gpu.WriteGp1(0x07000000u | (((uint)vEnd & 0x3FF) << 10) | ((uint)vStart & 0x3FF));

        uint mode = 0x08000000u;
        if (pal) mode |= 0x8;
        if (isrgb24 != 0) mode |= 0x10;
        if (isinter != 0) mode |= 0x20;
        if (dispW <= 280) { }
        else if (dispW <= 352) mode |= 1;
        else if (dispW <= 400) mode |= 0x40;
        else if (dispW <= 560) mode |= 2;
        else mode |= 3;
        if (dispH > (pal ? 288 : 256)) mode |= 0x24;
        gpu.WriteGp1(mode);

        GpuHle.NotifyDisplay(dispX, dispY, dispW, dispH);

        if (Event.HasAnyListeners<DispEnvEvent>())
        {
            var e = _dispEnvEvent;
            e.Context = c; e.Memory = m;
            e.X = dispX; e.Y = dispY; e.W = dispW; e.H = dispH;
            Event.Dispatch(e);
        }

        c.V0 = c.A0;
    }

    static short S16(IMemory m, uint addr) => (short)m.ReadU16(addr);

    static uint GetCs(short x, short y)
    {
        x = short.Clamp(x, 0, VramShadow.Width - 1);
        y = short.Clamp(y, 0, VramShadow.Height - 1);
        return 0xE3000000u | (((uint)y & 0x3FF) << 10) | ((uint)x & 0x3FF);
    }

    static uint GetCe(short x, short y)
    {
        x = short.Clamp(x, 0, VramShadow.Width - 1);
        y = short.Clamp(y, 0, VramShadow.Height - 1);
        return 0xE4000000u | (((uint)y & 0x3FF) << 10) | ((uint)x & 0x3FF);
    }

    static uint GetOfs(short x, short y)
        => 0xE5000000u | (((uint)y & 0x7FF) << 11) | ((uint)x & 0x7FF);

    static uint GetMode(int dfe, int dtd, ushort tpage)
        => (dtd != 0 ? 0xE1000200u : 0xE1000000u) | (dfe != 0 ? 0x400u : 0u) | ((uint)tpage & 0x9FF);

    static uint GetTw(short x, short y, short w, short h)
    {
        uint c0 = ((uint)x & 0xFF) >> 3;
        uint c1 = ((uint)y & 0xFF) >> 3;
        uint c2 = ((uint)(-w) & 0xFF) >> 3;
        uint c3 = ((uint)(-h) & 0xFF) >> 3;
        return 0xE2000000u | (c1 << 15) | (c0 << 10) | (c3 << 5) | c2;
    }
}
