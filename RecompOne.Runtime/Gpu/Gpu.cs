using RecompOne.Runtime.Hle;

namespace RecompOne.Runtime;

//ToDO: HW Renderer must use a separate "view" quad text so widescren patches are possible
// or find a betrter approach, maybe allongate the vram as needed? a dufferent tex seens more ideal?
public sealed partial class Gpu
{
    public const int VramWidth = VramShadow.Width;
    public const int VramHeight = VramShadow.Height;

    public readonly VramShadow Shadow = new();
    public ushort[] Vram => Shadow.Pixels;

    int _drawAreaLeft, _drawAreaTop, _drawAreaRight = VramWidth - 1, _drawAreaBottom = VramHeight - 1;
    int _drawOffsetX, _drawOffsetY;

    int _texPageX, _texPageY;
    int _texDepth;
    int _blendMode;
    bool _dither;
    bool _texDisable;

    int _texWinMaskX, _texWinMaskY, _texWinOffX, _texWinOffY;

    bool _setMask, _checkMask;

    int _dispVramX, _dispVramY;
    int _hRange1 = 0x200, _hRange2 = 0xC00, _vRange1 = 0x10, _vRange2 = 0x100;
    int _hres;
    bool _hres368, _vres480, _pal, _disp24, _interlace, _displayDisabled = true;
    int _dmaDir;

    readonly List<uint> _fifo = new(16);
    int _need;
    bool _polyline;

    bool _loadImage;
    int _loadX, _loadY, _loadW, _loadH, _loadPx;

    bool _readImage;
    int _readX, _readY, _readW, _readH, _readPx;
    uint _gpuRead;

    bool _statField;

    public int DisplayX => _dispVramX;
    public int DisplayY => _dispVramY;
    public bool DisplayEnabled => !_displayDisabled;
    public bool Display24Bit => _disp24;
    public bool Pal => _pal;

    public int DrawAreaLeft => _drawAreaLeft;
    public int DrawAreaTop => _drawAreaTop;
    public int DrawAreaRight => _drawAreaRight;
    public int DrawAreaBottom => _drawAreaBottom;

    /// <summary>Latch the current draw clip (call after DrawOTag completes).</summary>
    public void LatchSoftDrawBuffer()
    {
        int x = _drawAreaLeft;
        int y = _drawAreaTop;
        int w = Math.Max(0, _drawAreaRight - _drawAreaLeft + 1);
        int h = Math.Max(0, _drawAreaBottom - _drawAreaTop + 1);
        int dispH = DisplayHeight;
        if (dispH > 0 && dispH < h) h = dispH;
        if (w <= 0 || h <= 0) return;

        // Probe several regions — Intro mesh sits near screen centre; the top-left
        // 64x64 is often empty while the other double-buffer half still holds title stars.
        int nzDraw = CountNzProbes(x, y, w, h);
        // If this OT submitted polys, always latch the draw clip (even if probes are
        // sparse / dark). Switching to the other half freezes Output on the title snap.
        if (SoftLastPolyAt > 0)
        {
            LatchSoftDisplay(x, y, Math.Min(w, 512), h);
            return;
        }

        int otherX = x >= 512 ? 0 : 512;
        int nzOther = CountNzProbes(otherX, y, 512, h);
        if (nzOther > nzDraw && nzDraw < 8)
            LatchSoftDisplay(otherX, y, 512, h);
        else
            LatchSoftDisplay(x, y, Math.Min(w, 512), h);
    }

    int CountNzProbes(int dx, int dy, int w, int h)
    {
        int pw = Math.Min(64, Math.Max(1, w));
        int ph = Math.Min(64, Math.Max(1, h));
        int nz = CountNz(dx, dy, pw, ph); // top-left
        if (w > pw) nz += CountNz(dx + (w - pw) / 2, dy + Math.Max(0, (h - ph) / 2), pw, ph); // centre
        if (w > pw * 2) nz += CountNz(dx + w - pw, dy + Math.Max(0, (h - ph) / 2), pw, ph); // mid-right
        return nz;
    }

    int CountNz(int dx, int dy, int w, int h)
    {
        int nz = 0;
        var vram = Vram;
        for (int y = 0; y < h; y++)
        {
            int line = ((dy + y) & (VramHeight - 1)) * VramWidth;
            for (int x = 0; x < w; x++)
                if (vram[line + ((dx + x) & (VramWidth - 1))] != 0) nz++;
        }
        return nz;
    }

    // Sticky soft present snap — updated after DrawOTag so Output never shows mid-FillRect VRAM.
    public byte[] SoftSnapRgb { get; private set; } = [];
    public int SoftSnapW { get; private set; }
    public int SoftSnapH { get; private set; }
    public int SoftSnapX { get; private set; }
    public int SoftSnapY { get; private set; }
    public bool SoftSnapValid { get; private set; }
    static int _softLatchLog;

    public void InvalidateSoftSnap()
    {
        SoftSnapValid = false;
        SoftSnapW = SoftSnapH = 0;
        _softLatchLog = 0; // allow Intro latch breadcrumbs
    }

    /// <summary>
    /// Copy a VRAM rect into <see cref="SoftSnapRgb"/>. Prefer the draw buffer after
    /// DrawOTag (completed FB). Skip replacing a good snap with an empty one.
    /// </summary>
    public void LatchSoftDisplay(int? srcX = null, int? srcY = null, int? srcW = null, int? srcH = null)
    {
        int dx = srcX ?? _dispVramX;
        int dy = srcY ?? _dispVramY;
        int w = srcW ?? DisplayWidth;
        int h = srcH ?? DisplayHeight;
        if (w <= 0 || h <= 0) return;
        w = Math.Min(w, VramWidth);
        h = Math.Min(h, VramHeight);

        int needed = w * h * 3;
        if (SoftSnapRgb.Length < needed) SoftSnapRgb = new byte[needed];
        // Build into a temp so we can reject empty frames that would blank Output.
        var dest = SoftSnapRgb;
        bool useTemp = SoftSnapValid;
        if (useTemp && (_softLatchScratch == null || _softLatchScratch.Length < needed))
            _softLatchScratch = new byte[needed];
        if (useTemp) dest = _softLatchScratch!;

        var vram = Vram;
        int o = 0;
        int nz = 0;
        if (_disp24)
        {
            for (int y = 0; y < h; y++)
            {
                int lineByte = ((dy + y) * VramWidth + dx) * 2;
                for (int x = 0; x < w; x++)
                {
                    int bo = lineByte + x * 3;
                    byte r = VramByte(vram, bo), g = VramByte(vram, bo + 1), b = VramByte(vram, bo + 2);
                    dest[o++] = r; dest[o++] = g; dest[o++] = b;
                    if (r != 0 || g != 0 || b != 0) nz++;
                }
            }
        }
        else
        {
            for (int y = 0; y < h; y++)
            {
                int line = ((dy + y) & (VramHeight - 1)) * VramWidth;
                for (int x = 0; x < w; x++)
                {
                    ushort px = vram[line + ((dx + x) & (VramWidth - 1))];
                    dest[o++] = (byte)((px & 0x1F) << 3);
                    dest[o++] = (byte)(((px >> 5) & 0x1F) << 3);
                    dest[o++] = (byte)(((px >> 10) & 0x1F) << 3);
                    if (px != 0) nz++;
                }
            }
        }

        // Don't clobber a visible snap with a black/empty latch (PutDispEnv often
        // points at the buffer that has not been drawn yet this frame).
        if (nz == 0)
        {
            if (_softLatchLog < 16)
            {
                _softLatchLog++;
                var skip = SoftSnapValid
                    ? $"softLatch SKIP empty xy={dx},{dy} {w}x{h}"
                    : $"softLatch empty (no snap yet) xy={dx},{dy} {w}x{h}";
                Diagnostics.BootLog.Write(skip);
                Console.WriteLine("[boot] " + skip);
            }
            return;
        }

        if (useTemp)
            Buffer.BlockCopy(dest, 0, SoftSnapRgb, 0, needed);

        SoftSnapW = w;
        SoftSnapH = h;
        SoftSnapX = dx;
        SoftSnapY = dy;
        SoftSnapValid = true;

        if (_softLatchLog < 16)
        {
            _softLatchLog++;
            var msg = $"softLatch xy={dx},{dy} {w}x{h} nz={nz}/{w * h}";
            Diagnostics.BootLog.Write(msg);
            Console.WriteLine("[boot] " + msg);
        }
    }

    byte[]? _softLatchScratch;

    static byte VramByte(ushort[] vram, int byteOffset)
    {
        int hw = (byteOffset >> 1) & (VramWidth * VramHeight - 1);
        ushort v = vram[hw];
        return (byte)((byteOffset & 1) == 0 ? v & 0xFF : v >> 8);
    }

    int CyclesPerPixel => _hres368 ? 7 : _hres switch { 0 => 10, 1 => 8, 2 => 5, _ => 4 };

    public int DisplayWidth
    {
        get
        {
            int w = ((_hRange2 - _hRange1) / CyclesPerPixel + 2) & ~3;
            return Math.Clamp(w, 0, VramWidth);
        }
    }

    public int DisplayHeight
    {
        get
        {
            int lines = _vRange2 - _vRange1;
            if (_vres480) lines <<= 1;
            return Math.Clamp(lines, 0, VramHeight);
        }
    }

    public uint ReadStat() 
    {
        uint s = 0;
        s |= (uint)((_texPageX / 64) & 0xF);
        s |= (uint)(((_texPageY / 256) & 1) << 4);
        s |= (uint)((_blendMode & 3) << 5);
        s |= (uint)((_texDepth & 3) << 7);
        
        if (_dither) s |= 1u << 9;
        s |= 1u << 10;
        if (_setMask) s |= 1u << 11;
        if (_checkMask) s |= 1u << 12;
        s |= 1u << 13;
        
        if (_texDisable) s |= 1u << 15;
        if (_hres368) s |= 1u << 16;
        
        s |= (uint)((_hres & 3) << 17);
        
        if (_vres480) s |= 1u << 19;
        if (_pal) s |= 1u << 20;
        if (_disp24) s |= 1u << 21;
        if (_interlace) s |= 1u << 22;
        if (_displayDisabled) s |= 1u << 23;
        
        s |= 1u << 26;
        s |= 1u << 27;
        s |= 1u << 28;
        s |= (uint)((_dmaDir & 3) << 29);
        s |= _dmaDir switch { 1 => 1u << 25, 2 => 1u << 28, 3 => 1u << 27, _ => 0u };
        
        _statField = !_statField;
        if (_statField) s |= 1u << 31;
        return s;
    }

    public uint ReadData()
    {
        if (!_readImage) return _gpuRead;
        ushort lo = ReadImageHalfword();
        ushort hi = ReadImageHalfword();
        return (uint)(lo | (hi << 16));
    }

    public void WriteGp0(uint word)
    {
        if (_loadImage) { StoreImageHalfword((ushort)word); StoreImageHalfword((ushort)(word >> 16)); return; }
        if (_polyline)
        {
            if ((word & 0xF000F000u) == 0x50005000u) { _polyline = false; ExecutePolyline(); _fifo.Clear(); }
            else _fifo.Add(word);
            return;
        }

        _fifo.Add(word);
        if (_fifo.Count == 1)
        {
            _need = CommandLength(word);
            if (_need == LenPolyline) { _polyline = true; return; }
            if (_need == LenImageLoad) _need = 3;
        }

        if (_fifo.Count >= _need) { Execute(); if (!_loadImage) _fifo.Clear(); }
    }

    public void WriteGp1(uint word)
    {
        uint op = (word >> 24) & 0xFF;
        uint p = word & 0xFFFFFF;
        switch (op)
        {
            case >= 0x05 and <= 0x08:
                WriteGp1Display(op, p);
                GpuHle.NotifyDisplay(_dispVramX, _dispVramY, DisplayWidth, DisplayHeight);
                return;
            case 0x00: Reset(); break;
            case 0x01: _fifo.Clear(); _polyline = false; _loadImage = false; break;
            case 0x02: break;
            case 0x03: _displayDisabled = (p & 1) != 0; break;
            case 0x04: _dmaDir = (int)(p & 3); break;
            case 0x10: SetGpuInfo(p); break;
        }
    }

    void WriteGp1Display(uint op, uint p)
    {
        switch (op)
        {
            case 0x05: _dispVramX = (int)(p & 0x3FF); _dispVramY = (int)((p >> 10) & 0x1FF); break;
            case 0x06: _hRange1 = (int)(p & 0xFFF); _hRange2 = (int)((p >> 12) & 0xFFF); break;
            case 0x07: _vRange1 = (int)(p & 0x3FF); _vRange2 = (int)((p >> 10) & 0x3FF); break;
            case 0x08:
                _hres = (int)(p & 3);
                _hres368 = (p & 0x40) != 0;
                _vres480 = (p & 4) != 0;
                _pal = (p & 8) != 0;
                _disp24 = (p & 0x10) != 0;
                _interlace = (p & 0x20) != 0;
                break;
        }
    }

    void Reset()
    {
        _fifo.Clear();
        _polyline = _loadImage = _readImage = false;
        _displayDisabled = true;
        _dmaDir = 0;
        _texPageX = _texPageY = _texDepth = _blendMode = 0;
        _dither = _texDisable = false;
        _texWinMaskX = _texWinMaskY = _texWinOffX = _texWinOffY = 0;
        _drawAreaLeft = _drawAreaTop = 0;
        _drawAreaRight = VramWidth - 1;
        _drawAreaBottom = VramHeight - 1;
        _drawOffsetX = _drawOffsetY = 0;
        _setMask = _checkMask = false;
        _dispVramX = _dispVramY = 0;
    }

    void SetGpuInfo(uint p)
    {
        switch (p & 0xFF)
        {
            case 0x03: _gpuRead = (uint)(_drawAreaLeft | (_drawAreaTop << 10)); break;
            case 0x04: _gpuRead = (uint)(_drawAreaRight | (_drawAreaBottom << 10)); break;
            case 0x05: _gpuRead = (uint)((_drawOffsetX & 0x7FF) | ((_drawOffsetY & 0x7FF) << 11)); break;
            default: _gpuRead = 0; break;
        }
    }
}
