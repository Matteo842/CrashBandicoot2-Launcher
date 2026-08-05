using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Host;
using RecompOne.Runtime.Memory;
using BiosKernel = RecompOne.Runtime.Bios.Bios;

namespace RecompOne.Runtime.Dispatch;

public static class Dispatcher
{
    static readonly OverlayLoadedEvent _overlayEvent = new();
    static readonly Dictionary<string, IOverlay> _registry = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<int, string> _lbaToName = [];
    static readonly List<string> _active = [];
    static readonly Dictionary<uint, Action<CpuContext, IMemory>> _funcMap = [];
    static readonly List<(uint Lo, uint Hi, Action<CpuContext, IMemory, uint> Fn)> _ranges = [];
    private static IOverlay? _pending;
    public static void Register(string name, IOverlay overlay)
    {
        _registry[name] = overlay;
        if (overlay.LbaStart >= 0) _lbaToName[overlay.LbaStart] = name;
    }

    /// <summary>
    /// Catch mid-function / fallthrough targets inside a recompiled mega-function.
    /// Exact <see cref="_funcMap"/> hits still win; ranges are only tried on miss.
    /// </summary>
    public static void RegisterRange(uint loInclusive, uint hiExclusive, Action<CpuContext, IMemory, uint> fn)
    {
        _ranges.Add((loInclusive, hiExclusive, fn));
    }

    public static void ClearRanges() => _ranges.Clear();

    public static string[] ActiveNames
    {
        get { lock (_active) return _active.ToArray(); }
    }
    
    public static IReadOnlyDictionary<string, IOverlay> Overlays => _registry;

    public static void LoadByLba(int lba)
    {
        if (!_lbaToName.TryGetValue(lba, out var name)) return;
        var overlay = _registry[name];
        if(overlay.Base == 0) {
            Load(name);
            return;
        }

        _pending = overlay;
    }

    public static void NotifyWrite(uint phys)
    {
        var p = _pending;
        if (p == null) return;
        uint start = p.Base & 0x1FFFFFFFu;
        if (phys < start || phys >= start + 0x800u) return;
        _pending = null;
        Load(p.Name);
    }
    public static void ClearPending() => _pending = null;

    public static void Load(string name)
    {
        if (!_registry.TryGetValue(name, out var overlay))
            throw new KeyNotFoundException($"overlay not registered: {name}");

        bool already;
        lock (_active) already = _active.Remove(name);

        if (!already) HandleRegionOverwrites(overlay);

        lock (_active) _active.Add(name);
        foreach (var (addr, fn) in overlay.Functions)
            _funcMap[addr] = fn;

        if (already) return;
        Runtime.OverlayLog.Record(name, OverlayEventKind.Loaded);
        Console.WriteLine($"[Dispatcher] loaded overlay: {name}");

        if (Event.HasAnyListeners<OverlayLoadedEvent>())
        {
            var e = _overlayEvent;
            e.Context = Runtime.Cpu!; e.Memory = Runtime.Mem!;
            e.Name = name;
            Event.Dispatch(e);
        }
    }

    static void HandleRegionOverwrites(IOverlay overlay)
    {
        uint newStart = overlay.Base & 0x1FFFFFFFu;
        uint newEnd = newStart + overlay.Size;
        bool hasRegion = overlay.Base != 0 && overlay.Size != 0;

        List<string>? overwritten = null;
        List<(string Name, int Funcs)>? vramCollisions = null;

        lock (_active)
        {
            foreach (var activeName in _active)
            {
                var other = _registry[activeName];
                bool otherHasRegion = other.Base != 0 && other.Size != 0;

                if (hasRegion && otherHasRegion)
                {
                    uint s = other.Base & 0x1FFFFFFFu;
                    uint e = s + other.Size;

                    if (s < newEnd && e > newStart)
                    {
                        if (s >= newStart && e <= newEnd)
                        {
                            overwritten ??= [];
                            overwritten.Add(activeName);
                        }
                        continue;
                    }
                }

                int shared = CountSharedFunctions(overlay, other);
                if (shared > 0)
                {
                    vramCollisions ??= [];
                    vramCollisions.Add((activeName, shared));
                }
            }

            if (overwritten != null)
                foreach (var d in overwritten) _active.Remove(d);
        }

        if (overwritten != null)
        {
            Rebuild();
            foreach (var d in overwritten)
            {
                Runtime.OverlayLog.Record(d, OverlayEventKind.Overwritten, overlay.Name);
                Console.WriteLine($"[Dispatcher] overlay {d} overwritten by {overlay.Name}");
            }
        }

        if (vramCollisions != null)
        {
            foreach (var (otherName, n) in vramCollisions)
            {
                Runtime.OverlayLog.Record(overlay.Name, OverlayEventKind.VramCollision, $"{otherName} ({n} funcs)");
                Console.WriteLine($"[Dispatcher] overlay {overlay.Name} vvram colision with {otherName}: {n} functions");
            }
        }
    }

    static int CountSharedFunctions(IOverlay a, IOverlay b)
    {
        var smaller = a.Functions.Count <= b.Functions.Count ? a : b;
        var larger = ReferenceEquals(smaller, a) ? b : a;

        int n = 0;
        foreach (var addr in smaller.Functions.Keys)
            if (larger.Functions.ContainsKey(addr)) n++;
        return n;
    }

    public static void TryLoad(string name)
    {
        if (_registry.ContainsKey(name))
            Load(name);
    }

    public static void Unload(string name)
    {
        bool removed;
        lock (_active) removed = _active.Remove(name);
        if (!removed) return;
        Rebuild();
        Runtime.OverlayLog.Record(name, OverlayEventKind.Unloaded);
    }

    public static bool IsMapped(uint addr) => _funcMap.ContainsKey(addr);

    static int _pumpCounter;
    static int _introNativeDispatchLogs;
    static int _raster42930DispatchLogs;
    static int _meshPacketDispatchLogs;

    public static void Call(CpuContext c, IMemory m, uint addr)
    {
        // Keep the host window responsive during retail busy-waits (CD/GPU polls).
        if ((++_pumpCounter & 0x3FF) == 0)
            HostWindow.Pump();

        if (addr >= 0x800ECA00u && addr < 0x800ECC00u && _introNativeDispatchLogs++ < 12)
        {
            string target = _funcMap.TryGetValue(addr, out var mapped)
                ? $"mapped={mapped.Method.DeclaringType?.FullName}.{mapped.Method.Name}"
                : "unmapped";
            Diagnostics.BootLog.Write($"INTRO DISPATCH addr=0x{addr:X8} ra=0x{c.RA:X8} s5=0x{c.S5:X8} {target}");
        }
        if (addr == 0x80042930u && _raster42930DispatchLogs++ < 8)
        {
            Diagnostics.BootLog.Write($"RASTER CALL 42930 active={RasterContinue.Active} " +
                $"ra=0x{c.RA:X8} a2=0x{c.A2:X8} sp=0x{c.SP:X8}");
        }
        if (RasterContinue.TryJump(addr)) return;
        if (BiosKernel.TryDispatch(c, m, addr)) return;
        if (!_funcMap.TryGetValue(addr, out var fn))
        {
            for (int i = 0; i < _ranges.Count; i++)
            {
                var (lo, hi, rangeFn) = _ranges[i];
                if (addr >= lo && addr < hi)
                {
                    rangeFn(c, m, addr);
                    return;
                }
            }

            // Unmapped mid-entries (EXE jump-table tails) + GOOL opcode-49 NSF natives.
            if (RecompOne.Runtime.Sdk.LibGool.TryInterpretNative(c, m, addr))
                return;
            uint opTable = m.ReadU32(0x1F80005Cu);
            var diag = $"unmapped call state addr=0x{addr:X8} ra=0x{c.RA:X8} " +
                $"a0=0x{c.A0:X8} a1=0x{c.A1:X8} a2=0x{c.A2:X8} a3=0x{c.A3:X8} " +
                $"s0=0x{c.S0:X8} s1=0x{c.S1:X8} s2=0x{c.S2:X8} s3=0x{c.S3:X8} " +
                $"s4=0x{c.S4:X8} s5=0x{c.S5:X8} s6=0x{c.S6:X8} s7=0x{c.S7:X8} " +
                $"fp=0x{c.FP:X8} gp=0x{c.GP:X8} gpbase=0x{c.GpBase:X8} opTable=0x{opTable:X8}";
            Diagnostics.BootLog.Write(diag);
            Diagnostics.BootLog.Write(Environment.StackTrace);
            throw new InvalidOperationException($"unmapped call: 0x{addr:X8}");
        }
        if (c.RA == 0x8003E6DCu && _meshPacketDispatchLogs++ < 24)
        {
            uint beforeS7 = c.S7;
            uint beforePacket = m.ReadU32(c.V1 + 0x14u);
            Diagnostics.BootLog.Write($"MESH DISPATCH enter target=0x{addr:X8} s7=0x{beforeS7:X8} " +
                $"ctx14=0x{beforePacket:X8} v1=0x{c.V1:X8}");
            fn(c, m);
            Diagnostics.BootLog.Write($"MESH DISPATCH leave target=0x{addr:X8} s7=0x{c.S7:X8} " +
                $"ctx14=0x{m.ReadU32(c.V1 + 0x14u):X8}");
            return;
        }
        fn(c, m);
    }

    static void Rebuild()
    {
        _funcMap.Clear();
        lock (_active)
        {
            foreach (var name in _active)
                foreach (var (addr, fn) in _registry[name].Functions)
                    _funcMap[addr] = fn;
        }
    }
}
