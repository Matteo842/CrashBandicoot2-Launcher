using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Sdk;

public static class LibEtc
{
    static int _vcount;
    static long _originMs = Environment.TickCount64;
    static readonly VSyncEvent _vsyncEvent = new();

    // PsyQ LIBETC HSync/VSync counter (SCES-00967 @ 0x8005DF30).
    const uint VBlankCountAddr = 0x8005DF30u;

    // PAL Crash 2: 50 Hz display; NTSC would be 60.
    const double Hz = 50.0;

    static int _vsyncLog;

    public static void VSync(CpuContext c, IMemory m)
    {
        int mode = (int)c.A0;
        if (_vsyncLog < 40)
        {
            Diagnostics.BootLog.Write($"VSync mode={mode} vcount={_vcount}");
            _vsyncLog++;
        }

        // mode < 0 → return current count (PsyQ). Some retail paths busy-poll
        // this until the count changes, so keep it tied to wall-clock vblanks.
        if (mode < 0)
        {
            AdvanceToWallClock(m, present: false);
            c.V0 = (uint)_vcount;
            return;
        }

        // mode == 1 → don't wait, return time since last (approx 0)
        if (mode == 1)
        {
            AdvanceToWallClock(m, present: false);
            c.V0 = 0;
            return;
        }

        // mode == 0 or mode > 1: wait one (or N) vblank(s)
        int frames = mode <= 0 ? 1 : mode;
        for (int i = 0; i < frames; i++)
        {
            Runtime.PresentFrame();
            _vcount++;
            m.WriteU32(VBlankCountAddr, (uint)_vcount);
            _originMs = Environment.TickCount64 - (long)(_vcount * (1000.0 / Hz));

            if (Event.HasAnyListeners<VSyncEvent>())
            {
                var e = _vsyncEvent;
                e.Context = c;
                e.Memory = m;
                e.Frame = _vcount;
                Event.Dispatch(e);
            }
        }

        c.V0 = 0;
    }

    static void AdvanceToWallClock(IMemory m, bool present)
    {
        long now = Environment.TickCount64;
        int target = (int)((now - _originMs) * (Hz / 1000.0));
        if (target <= _vcount) return;

        int delta = Math.Min(target - _vcount, 8); // cap catch-up
        for (int i = 0; i < delta; i++)
        {
            if (present) Runtime.PresentFrame();
            _vcount++;
            m.WriteU32(VBlankCountAddr, (uint)_vcount);
        }
    }

    /// <summary>
    /// SCES-00967 @ 0x8003BBD8 — retail Duff-device halfword copy (src=A0, dst=A1, n=A2 halfwords).
    /// Recompiler emits mid-function jr targets as unmapped Dispatcher.Call.
    /// </summary>
    public static void MemcpyHw(CpuContext c, IMemory m)
    {
        uint src = c.A0;
        uint dst = c.A1;
        uint nBytes = c.A2 << 1;
        for (uint i = 0; i < nBytes; i++)
            m.WriteU8(dst + i, m.ReadU8(src + i));
        c.V0 = dst;
    }
}
