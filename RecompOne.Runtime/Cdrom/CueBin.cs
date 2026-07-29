namespace RecompOne.Runtime.Cdrom;

public sealed class CueBin : IDisposable
{
    private record Track(string BinPath, int Number, string Mode, int SectorSize, int DataOffset, long FileOffset, int StartLba);

    private readonly List<Track> _tracks = [];
    private readonly Dictionary<string, FileStream> _files = [];
    private readonly object _ioGate = new();

    private CueBin() {}

    public static CueBin Open(string cuePath)
    {
        var cb = new CueBin();
        cb.Parse(cuePath);
        return cb;
    }

    private void Parse(string cuePath)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(cuePath)) ?? "";
        string? currentFile = null;
        int trackNum = 0;
        string mode = "MODE2/2352";
        long fileBaseSectors = 0;

        foreach (var raw in File.ReadLines(cuePath))
        {
            var line = raw.Trim();
            if (line.StartsWith("FILE ", StringComparison.OrdinalIgnoreCase))
            {
                int a = line.IndexOf('"') + 1;
                int b = line.LastIndexOf('"');
                if (currentFile != null && File.Exists(currentFile))
                    fileBaseSectors += new FileInfo(currentFile).Length / 2352;
                currentFile = Path.Combine(dir, line[a..b]);
            }
            else if (line.StartsWith("TRACK ", StringComparison.OrdinalIgnoreCase))
            {
                var p = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                trackNum = int.Parse(p[1]);
                mode = p[2];
            }
            else if (line.StartsWith("INDEX 01 ", StringComparison.OrdinalIgnoreCase))
            {
                long sectorsWithinFile = MsfToSectors(line[9..].Trim());
                int ss = GetSectorSize(mode);
                int startLba = (int)(fileBaseSectors + sectorsWithinFile);
                _tracks.Add(new Track(currentFile!, trackNum, mode, ss, GetDataOffset(mode), sectorsWithinFile * ss, startLba));
            }
        }
    }

    //crrct trak
    public int FirstTrack => _tracks.Count > 0 ? _tracks.Min(t => t.Number) : 1;
    public int LastTrack => _tracks.Count > 0 ? _tracks.Max(t => t.Number) : 1;
    public bool HasTracks => _tracks.Count > 0;

    public bool TrackStartLba(int track, out int lba)
    {
        var t = _tracks.Find(x => x.Number == track);
        if (t == null) { lba = 0; return false; }
        lba = t.StartLba;
        return true;
    }

    public int LeadoutLba
    {
        get
        {
            long total = 0;
            var seen = new HashSet<string>();
            foreach (var t in _tracks)
                if (seen.Add(t.BinPath) && File.Exists(t.BinPath))
                    total += new FileInfo(t.BinPath).Length / 2352;
            return (int)total;
        }
    }

    public int DataSectors { get { var t = DataTrack(); return DataTrackSectors(t, GetStream(t.BinPath)); } }

    public byte[] ReadSector(int lba) => ReadSectorData(lba, 2048);

    private int _lastOobLba = int.MinValue;

    public byte[] ReadSectorData(int lba, int size)
    {
        var t = DataTrack();
        var stream = GetStream(t.BinPath);
        int offset = t.SectorSize == 2352
            ? size switch { >= 2340 => 12, >= 2329 => 16, _ => 24 }
            : t.DataOffset;
        long pos = t.FileOffset + (long)lba * t.SectorSize + offset;
        var buf = new byte[size];
        if (lba < 0) return buf;
        int want = Math.Min(size, t.SectorSize - offset);
        lock (_ioGate)
        {
            int dataSectors = DataTrackSectors(t, stream);
            if (lba >= dataSectors || pos >= stream.Length)
            {
                if (lba != _lastOobLba)
                {
                    _lastOobLba = lba;
                    Console.WriteLine($"[CueBin] there was read outside data track: lba={lba}, was this intended?");
                }
                return buf;
            }
            int avail = (int)Math.Min(want, stream.Length - pos);
            stream.Seek(pos, SeekOrigin.Begin);
            stream.ReadExactly(buf, 0, avail);
        }
        return buf;
    }

    private Track DataTrack() => _tracks.Find(t => !t.Mode.Equals("AUDIO", StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException("no data track was found in cue sheet");

    private int DataTrackSectors(Track dt, FileStream stream)
    {
        int next = int.MaxValue;
        foreach (var t in _tracks)
            if (t.StartLba > dt.StartLba && t.StartLba < next) next = t.StartLba;
        return next != int.MaxValue ? next - dt.StartLba : (int)((stream.Length - dt.FileOffset) / dt.SectorSize);
    }

    private FileStream GetStream(string path)
    {
        lock (_ioGate)
        {
            if (!_files.TryGetValue(path, out var s))
                _files[path] = s = File.OpenRead(path);
            return s;
        }
    }

    private static long MsfToSectors(string msf)
    {
        var p = msf.Split(':');
        return long.Parse(p[0]) * 60 * 75 + long.Parse(p[1]) * 75 + long.Parse(p[2]);
    }

    private static int GetSectorSize(string mode) => mode switch
    {
        "MODE1/2048" => 2048,
        "MODE2/2336" => 2336,
        _ => 2352,
    };

    private static int GetDataOffset(string mode) => mode switch
    {
        "MODE1/2352" => 16,
        "MODE2/2352" => 24,
        "MODE2/2336" => 8,
        _ => 0,
    };

    public void Dispose()
    {
        foreach (var s in _files.Values) s.Dispose();
        _files.Clear();
    }
}
