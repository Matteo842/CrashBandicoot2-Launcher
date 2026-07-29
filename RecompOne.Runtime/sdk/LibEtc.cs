using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Sdk;

public static class LibEtc
{
    static int _vcount;
    static readonly VSyncEvent _vsyncEvent = new();

    // PsyQ / LIBETC vblank counter used by the retail wait helper (SCES-00967).
    const uint VBlankCountAddr = 0x8005DF30u;

    public static void VSync(CpuContext c, IMemory m)
    {
        int mode = (int)c.A0;

        // mode < 0 → return current count (PsyQ)
        if (mode < 0)
        {
            c.V0 = (uint)_vcount;
            return;
        }

        // mode == 1 → don't wait, return time since last (approx 0)
        if (mode == 1)
        {
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
}
