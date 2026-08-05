using RecompOne.Runtime.Cdrom;
using RecompOne.Runtime.Dispatch;
using RecompOne.Runtime.Hardware;
using RecompOne.Runtime.Host;

namespace RecompOne.Runtime.Memory;

public sealed class PSMemory : IMemory
{
    private readonly byte[] _ram = new byte[Runtime.Mode == RunMode.Devkit ? MemoryMap.DevkitRamSize : MemoryMap.RetailRamSize]; //should i make it able to increase psx mem?
    private readonly byte[] _scratchpad = new byte[MemoryMap.ScratchpadSize];
    private readonly byte[] _hwregs = new byte[MemoryMap.HwRegsSize];
    private readonly byte[] _bios = new byte[MemoryMap.BiosSize];
  
    private readonly Gpu _gpu = new();
    private readonly Spu _spu = new();
    private readonly Mdec _mdec = new();
    private readonly Timers _timers = new();
    private readonly Dma _dma;
    private CdController? _cd;
    private static int _introParentOverwriteLogs;
    private static int _introPointerWriteLogs;
    private static int _introPcWriteLogs;
    private static int _introFieldWriteLogs;
    private static uint _introFt3Base;
    private static int _introFt3PacketCount;
    private static int _introFt3WriteLogs;

    public ReadOnlySpan<byte> Ram => _ram;
    internal byte[] RamBuffer => _ram;
    
    //memory can be frozen for debuging reasons
    private readonly bool[] _frozen = new bool[Runtime.Mode == RunMode.Devkit ? MemoryMap.DevkitRamSize : MemoryMap.RetailRamSize];
    private int _frozenCount;

    public PSMemory()
    {
        _dma = new Dma(this, _gpu, _spu, _mdec, () => Runtime.DispatchIrq(3));
        Runtime.Gpu = _gpu;
        Runtime.Spu = _spu;
        Bios.KromFont.InstallInto(_bios);
    }

    public void SetCd(CdController cd) { _cd = cd; _dma.SetCd(cd); }

    private static bool IsDmaChcr(uint phys) => phys >= 0x1F801080u && phys < 0x1F8010F0u && (phys & 0xFu) == 8u;

    private uint Hw32(uint phys)
    {
        int o = (int)(phys - MemoryMap.HwRegsBase);
        return (uint)(_hwregs[o] | (_hwregs[o + 1] << 8) | (_hwregs[o + 2] << 16) | (_hwregs[o + 3] << 24));
    }

    private void Hw32(uint phys, uint v)
    {
        int o = (int)(phys - MemoryMap.HwRegsBase);
        _hwregs[o] = (byte)v;
        _hwregs[o + 1] = (byte)(v >> 8);
        _hwregs[o + 2] = (byte)(v >> 16);
        _hwregs[o + 3] = (byte)(v >> 24);
    }

    private void TrackWrite(uint phys, int size)
    {
        if (phys < MemoryMap.RamWindow)
        {
            uint off = phys % (uint)_ram.Length;
            Runtime.RamLog.RecordWrite(phys % (uint)_ram.Length, size);
            Dispatcher.NotifyWrite(off);
        }

    }

    private void TrackRead(uint phys, int size)
    {
        if (RamLogger.TrackReads && phys < MemoryMap.RamWindow)
            Runtime.RamLog.RecordRead(phys % (uint)_ram.Length, size);
    }

    private Span<byte> Resolve(uint address, int size)
    {
        uint phys = MemoryMap.ToPhysical(address);

        if (phys < MemoryMap.RamWindow)
            return _ram.AsSpan((int)(phys % (uint)_ram.Length), size);

        if (phys >= MemoryMap.ScratchpadBase && phys < MemoryMap.ScratchpadBase + MemoryMap.ScratchpadSize)
            return _scratchpad.AsSpan((int)(phys - MemoryMap.ScratchpadBase), size);

        if (phys >= MemoryMap.HwRegsBase && phys < MemoryMap.HwRegsBase + MemoryMap.HwRegsSize)
            return _hwregs.AsSpan((int)(phys - MemoryMap.HwRegsBase), size);

        if (phys >= MemoryMap.BiosBase && phys < MemoryMap.BiosBase + MemoryMap.BiosSize)
            return _bios.AsSpan((int)(phys - MemoryMap.BiosBase), size);

        var cpu = Runtime.Cpu;
        if (cpu != null)
            Diagnostics.BootLog.Write($"unmapped Resolve addr=0x{address:X8} size={size} ra=0x{cpu.RA:X8} " +
                $"a0=0x{cpu.A0:X8} a1=0x{cpu.A1:X8} a2=0x{cpu.A2:X8} a3=0x{cpu.A3:X8} " +
                $"s0=0x{cpu.S0:X8} s1=0x{cpu.S1:X8} s2=0x{cpu.S2:X8} s3=0x{cpu.S3:X8} " +
                $"s4=0x{cpu.S4:X8} s5=0x{cpu.S5:X8} s6=0x{cpu.S6:X8} s7=0x{cpu.S7:X8} " +
                $"sp=0x{cpu.SP:X8} fp=0x{cpu.FP:X8} gp=0x{cpu.GP:X8}");
        throw new InvalidOperationException($"unmapped address: 0x{address:X8}");
    }

    private static bool IsCd(uint phys) => phys >= 0x1F801800u && phys <= 0x1F801803u;
    private static bool IsSpu(uint phys) => phys >= 0x1F801C00u && phys < 0x1F801E80u;

    public byte ReadU8(uint address)
    {
        uint phys = MemoryMap.ToPhysical(address);
        TrackRead(phys, 1);
        if (_cd != null && IsCd(phys)) return _cd.Read(phys);
        return Resolve(address, 1)[0];
    }

    public ushort ReadU16(uint address)
    {
        uint phys = MemoryMap.ToPhysical(address);
        TrackRead(phys, 2);
        if (_cd != null && IsCd(phys)) return _cd.Read(phys);
        if (IsSpu(phys)) return _spu.ReadReg16(phys);
        if (Timers.InRange(phys) && _timers.TryRead(phys, out uint tv)) return (ushort)tv;
        var s = Resolve(address, 2);
        return (ushort)(s[0] | (s[1] << 8));
    }

    public uint ReadU32(uint address)
    {
        HostWindow.KeepAlive();
        uint phys = MemoryMap.ToPhysical(address);
        TrackRead(phys, 4);
        if (phys == 0x1F801810u) return _gpu.ReadData();
        if (phys == 0x1F801814u) return _gpu.ReadStat();
        if (phys == 0x1F801820u) return _mdec.ReadData();
        if (phys == 0x1F801824u) return _mdec.ReadStatus();
        if (phys == 0x1F8010F4u) return _dma.ReadDicr();
        if (_cd != null && IsCd(phys)) return _cd.Read(phys);
        if (IsSpu(phys)) return (uint)(_spu.ReadReg16(phys) | (_spu.ReadReg16(phys + 2) << 16));
        if (Timers.InRange(phys) && _timers.TryRead(phys, out uint tv)) return tv;
        Span<byte> s;
        try
        {
            s = Resolve(address, 4);
        }
        catch (InvalidOperationException)
        {
            var c = Runtime.Cpu;
            if (c != null)
            {
                Diagnostics.BootLog.Write($"unmapped ReadU32 addr=0x{address:X8} ra=0x{c.RA:X8} " +
                    $"a0=0x{c.A0:X8} a1=0x{c.A1:X8} a2=0x{c.A2:X8} a3=0x{c.A3:X8} " +
                    $"s0=0x{c.S0:X8} s1=0x{c.S1:X8} s2=0x{c.S2:X8} s3=0x{c.S3:X8} " +
                    $"s4=0x{c.S4:X8} s5=0x{c.S5:X8} s6=0x{c.S6:X8} s7=0x{c.S7:X8} " +
                    $"sp=0x{c.SP:X8} fp=0x{c.FP:X8} gp=0x{c.GP:X8}");
                var table = new System.Text.StringBuilder("TABLE 80010590");
                for (uint i = 0; i < 15; i++) table.Append($" {ReadU32(0x80010590u + i * 4u):X8}");
                Diagnostics.BootLog.Write(table.ToString());
            }
            throw;
        }
        return (uint)(s[0] | (s[1] << 8) | (s[2] << 16) | (s[3] << 24));
    }

    public void WriteU8(uint address, byte value)
    {
        uint phys = MemoryMap.ToPhysical(address);
        TrackWrite(phys, 1);
        if (_cd != null && IsCd(phys)) { _cd.Write(phys, value); return; }

        if (_frozenCount > 0 && phys < MemoryMap.RamWindow && _frozen[phys % (uint)_ram.Length]) return;
        Resolve(address, 1)[0] = value;
    }

    public void WriteU16(uint address, ushort value)
    {
        uint phys = MemoryMap.ToPhysical(address);
        TrackWrite(phys, 2);
        if (_cd != null && IsCd(phys)) { _cd.Write(phys, (byte)value); return; }
        if (IsSpu(phys)) { _spu.WriteReg16(phys, value); return; }
        if (_timers.TryWrite(phys, value)) return;
        var s = Resolve(address, 2);

        if (_frozenCount > 0 && phys < MemoryMap.RamWindow)
        {
            uint b = phys % (uint)_ram.Length;
            if(!_frozen[b])   s[0] = (byte)value;
            if(!_frozen[b+1]) s[1] = (byte)(value >> 8);
            return;
        }
        s[0] = (byte)value;
        s[1] = (byte)(value >> 8);
    }

    public void WriteU32(uint address, uint value)
    {
        uint phys = MemoryMap.ToPhysical(address);
        TrackWrite(phys, 4);
        if ((value >> 24) == 0x24u && phys >= 0x00060000u && phys < 0x00100000u &&
            ReadU32(0x8005F684u) == 0x1Cu)
        {
            _introFt3Base = phys - 4u;
            _introFt3PacketCount++;
        }
        if (_introFt3PacketCount <= 4 && _introFt3Base != 0u &&
            phys >= _introFt3Base && phys < _introFt3Base + 0x28u && _introFt3WriteLogs < 80)
        {
            _introFt3WriteLogs++;
            var c = Runtime.Cpu;
            Diagnostics.BootLog.Write(c == null
                ? $"FT3 WRITE pkt={_introFt3PacketCount} base=0x{_introFt3Base:X6} +0x{phys - _introFt3Base:X2}=0x{value:X8}"
                : $"FT3 WRITE pkt={_introFt3PacketCount} base=0x{_introFt3Base:X6} +0x{phys - _introFt3Base:X2}=0x{value:X8} ra=0x{c.RA:X8} a0=0x{c.A0:X8} a1=0x{c.A1:X8} a2=0x{c.A2:X8} a3=0x{c.A3:X8} v0=0x{c.V0:X8} v1=0x{c.V1:X8} t0=0x{c.T0:X8} t1=0x{c.T1:X8} t2=0x{c.T2:X8} t3=0x{c.T3:X8} sp=0x{c.SP:X8}");
        }
        if (phys == 0x000A14FCu && _introPcWriteLogs++ < 80)
        {
            uint old = ReadU32(address);
            var c = Runtime.Cpu;
            Diagnostics.BootLog.Write(c == null
                ? $"INTRO PC WRITE old=0x{old:X8} new=0x{value:X8}"
                : $"INTRO PC WRITE old=0x{old:X8} new=0x{value:X8} ra=0x{c.RA:X8} s5=0x{c.S5:X8} a0=0x{c.A0:X8} a1=0x{c.A1:X8} v0=0x{c.V0:X8}");
        }
        if (phys == 0x000A14D8u && _introFieldWriteLogs++ < 24)
        {
            uint old = ReadU32(address);
            var c = Runtime.Cpu;
            Diagnostics.BootLog.Write(c == null
                ? $"INTRO FIELD9C WRITE old=0x{old:X8} new=0x{value:X8}"
                : $"INTRO FIELD9C WRITE old=0x{old:X8} new=0x{value:X8} ra=0x{c.RA:X8} s5=0x{c.S5:X8} s6=0x{c.S6:X8} a0=0x{c.A0:X8} a1=0x{c.A1:X8} v0=0x{c.V0:X8}");
        }
        bool suspiciousIntroStackWrite = phys == 0x000A14F8u
            && (value < 0x800A1400u || value >= 0x800A1800u);
        if ((suspiciousIntroStackWrite || phys == 0x000A1244u) && _introPointerWriteLogs < 32)
        {
            uint old = ReadU32(address);
            if (old != value)
            {
                _introPointerWriteLogs++;
                var c = Runtime.Cpu;
                Diagnostics.BootLog.Write(c == null
                    ? $"INTRO PTR WRITE addr=0x{address:X8} old=0x{old:X8} new=0x{value:X8}"
                    : $"INTRO PTR WRITE addr=0x{address:X8} old=0x{old:X8} new=0x{value:X8} ra=0x{c.RA:X8} s0=0x{c.S0:X8} s1=0x{c.S1:X8} s2=0x{c.S2:X8} s3=0x{c.S3:X8} s4=0x{c.S4:X8} s5=0x{c.S5:X8} s6=0x{c.S6:X8} a0=0x{c.A0:X8} a1=0x{c.A1:X8} a2=0x{c.A2:X8} v0=0x{c.V0:X8}");
            }
        }
        if (value == 0x01000000u && phys >= 0x000AD6A4u && phys < 0x000AD864u && _introParentOverwriteLogs < 24)
        {
            uint old = ReadU32(address);
            if (old != value)
            {
                if (_introParentOverwriteLogs == 0)
                {
                    var code = new System.Text.StringBuilder("INTRO GOOL @0x800ECAC0");
                    for (uint i = 0; i < 48; i++) code.Append($" {ReadU32(0x800ECAC0u + i * 4u):X8}");
                    Diagnostics.BootLog.Write(code.ToString());
                    uint table = ReadU32(0x1F80005Cu);
                    Diagnostics.BootLog.Write($"INTRO OPTABLE base=0x{table:X8} e4=0x{ReadU32(table + 0xE4u):X8} 100=0x{ReadU32(table + 0x100u):X8} 44=0x{ReadU32(table + 0x44u):X8} 108=0x{ReadU32(table + 0x108u):X8}");
                    var asset = new System.Text.StringBuilder("INTRO ASSET @0x8006F9E0");
                    for (uint i = 0; i < 16; i++) asset.Append($" {ReadU32(0x8006F9E0u + i * 4u):X8}");
                    Diagnostics.BootLog.Write(asset.ToString());
                    var loaded = new System.Text.StringBuilder($"INTRO LOADED count={ReadU32(0x80067848u):X8} records");
                    for (uint i = 0; i < 24; i++) loaded.Append($" {ReadU32(0x8006784Cu + i * 4u):X8}");
                    Diagnostics.BootLog.Write(loaded.ToString());
                    var refs = new System.Text.StringBuilder("INTRO HASH 58000E65 matches");
                    for (uint p = 0; p + 4u <= (uint)_ram.Length; p += 4u)
                    {
                        uint w = (uint)(_ram[p] | (_ram[p + 1] << 8) | (_ram[p + 2] << 16) | (_ram[p + 3] << 24));
                        if (w == 0x58000E65u)
                            refs.Append($" 0x{0x80000000u + p:X8}");
                    }
                    Diagnostics.BootLog.Write(refs.ToString());
                    foreach (uint dumpBase in new[] { 0x80067BE0u, 0x8006F9C0u, 0x800B6200u })
                    {
                        var dump = new System.Text.StringBuilder($"INTRO MEM @0x{dumpBase:X8}");
                        for (uint i = 0; i < 32; i++) dump.Append($" {ReadU32(dumpBase + i * 4u):X8}");
                        Diagnostics.BootLog.Write(dump.ToString());
                    }
                }
                _introParentOverwriteLogs++;
                var c = Runtime.Cpu;
                Diagnostics.BootLog.Write(c == null
                    ? $"PARENT OVERWRITE addr=0x{address:X8} old=0x{old:X8} new=0x{value:X8}"
                    : $"PARENT OVERWRITE addr=0x{address:X8} old=0x{old:X8} new=0x{value:X8} ra=0x{c.RA:X8} pc? s0=0x{c.S0:X8} s1=0x{c.S1:X8} s2=0x{c.S2:X8} s3=0x{c.S3:X8} s4=0x{c.S4:X8} a0=0x{c.A0:X8} a1=0x{c.A1:X8} a2=0x{c.A2:X8}");
            }
        }
        // Intro draw mid-entries clobber func_80011800's S3/S4. That path then stores
        // S4 into DrawHold (0x80067844) and can reload level from a trashed mode word.
        // Scratchpad (0x1F800000) in DrawHold and level=-1 mid-Intro are the crash signature.
        if (address == 0x80067844u && value == 0x1F800000u)
        {
            uint level = ReadU32(0x8005F684u);
            uint mode = ReadU32(0x8005F688u);
            if (level == 0x1Cu || mode == 0x1Cu)
            {
                Diagnostics.BootLog.Write("HLE block DrawHold=scratchpad during Intro → 0");
                value = 0u;
            }
        }
        if (address == 0x8005F684u && value == 0xFFFFFFFFu)
        {
            uint level = ReadU32(0x8005F684u);
            if (level == 0x1Cu)
            {
                Diagnostics.BootLog.Write("HLE block level=-1 during Intro (keep 0x1C)");
                return;
            }
        }
        if (address is 0x8005F688u or 0x8005F684u or 0x80067844u)
        {
            uint old = ReadU32(address);
            var c = Runtime.Cpu;
            if (c != null)
                Diagnostics.BootLog.Write($"watch WriteU32 addr=0x{address:X8} old=0x{old:X8} new=0x{value:X8} ra=0x{c.RA:X8} gp=0x{c.GP:X8} gpbase=0x{c.GpBase:X8} s0=0x{c.S0:X8} s1=0x{c.S1:X8} s2=0x{c.S2:X8} s3=0x{c.S3:X8} s4=0x{c.S4:X8} s5=0x{c.S5:X8}");
            else
                Diagnostics.BootLog.Write($"watch WriteU32 addr=0x{address:X8} old=0x{old:X8} new=0x{value:X8}");
        }
        if (phys == 0x1F801810u) { _gpu.WriteGp0(value); return; }
        if (phys == 0x1F801814u) { _gpu.WriteGp1(value); return; }
        if (phys == 0x1F801820u) { _mdec.Write0(value); return; }
        if (phys == 0x1F801824u) { _mdec.WriteControl(value); return; }
        if (phys == 0x1F8010F4u) { _dma.WriteDicr(value); return; }
        if (IsDmaChcr(phys) && (value & 0x01000000u) != 0)
        {
            Hw32(phys, value & ~0x01000000u);
            _dma.Run((int)((phys - 0x1F801080u) / 0x10u), Hw32(phys - 8u), Hw32(phys - 4u), value);
            return;
        }
        if (_cd != null && IsCd(phys)) { _cd.Write(phys, (byte)value); return; }
        if (IsSpu(phys)) { _spu.WriteReg16(phys, (ushort)value); _spu.WriteReg16(phys + 2, (ushort)(value >> 16)); return; }
        if (_timers.TryWrite(phys, value)) return;
        var s = Resolve(address, 4);
        if (_frozenCount > 0 && phys < MemoryMap.RamWindow)
        {
            uint b = phys % (uint)_ram.Length;
            if(!_frozen[b])   s[0] = (byte)value;
            if(!_frozen[b+1]) s[1] = (byte)(value >> 8);
            if(!_frozen[b+2]) s[2] = (byte)(value >> 16);
            if(!_frozen[b+3]) s[3] = (byte)(value >> 24);
            return;
        }
        s[0] = (byte)value;
        s[1] = (byte)(value >> 8);
        s[2] = (byte)(value >> 16);
        s[3] = (byte)(value >> 24);
    }

    public uint ReadWordLeft(uint current, uint address)
    {
        int shift = (int)((address & 3) * 8);
        uint word = ReadU32(address & ~3u);
        return (current & (0x00FFFFFFu >> shift)) | (word << (24 - shift));
    }

    public uint ReadWordRight(uint current, uint address)
    {
        int shift = (int)((address & 3) * 8);
        uint word = ReadU32(address & ~3u);
        return (current & (0xFFFFFF00u << (24 - shift))) | (word >> shift);
    }

    public void WriteWordLeft(uint address, uint value)
    {
        uint aligned = address & ~3u;
        int shift = (int)((address & 3) * 8);
        uint mem = ReadU32(aligned);
        WriteU32(aligned, (mem & (0xFFFFFF00u << shift)) | (value >> (24 - shift)));
    }

    public void WriteWordRight(uint address, uint value)
    {
        uint aligned = address & ~3u;
        int shift = (int)((address & 3) * 8);
        uint mem = ReadU32(aligned);
        WriteU32(aligned, (mem & (0x00FFFFFFu >> (24 - shift))) | (value << shift));
    }

    public void LoadBytes(uint address, byte[] data)
    {
        for (int i = 0; i < data.Length; i++)
            WriteU8(address + (uint)i, data[i]);
    }

    public void ZeroRange(uint address, uint length)
    {
        for (uint i = 0; i < length; i++)
            WriteU8(address + i, 0);
    }

    public bool IsFrozen(uint off) => _frozenCount > 0 && _frozen[off % (uint)_frozen.Length];

    public void Freeze(uint off, int len)
    {
        for (int i = 0; i < len; i++)
        {
            uint o = (off + (uint)i) % (uint)_frozen.Length;
            if (!_frozen[o])
            {
                _frozen[o] = true;
                _frozenCount++;
            }
        }
    }
    public void Unfreeze(uint off, int len)
    {
        for (int i = 0; i < len; i++)
        {
            uint o = (off + (uint)i) % (uint)_frozen.Length;
            if (_frozen[o])
            {
                _frozen[o] = false;
                _frozenCount--;
            }
        }
    }

    public void ClearFreezes()
    {
        if (_frozenCount == 0) return;
        System.Array.Clear(_frozen, 0, _frozen.Length);
        _frozenCount = 0;
    }

    public void Poke(uint off, byte val) => _ram[off % (uint)_ram.Length] = val;
    
}
