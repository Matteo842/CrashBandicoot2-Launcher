using RecompOne.Recompiler.Disasm;

namespace RecompOne.Recompiler.Analysis;

/// <summary>
/// Static jump tables / function-bound fixes the analyzer cannot recover.
/// </summary>
public static class KnownJumpTables
{
    // Body includes pre-entry trampolines @ 0x8003A25C/0x8003A27C (branched from handlers).
    const uint GoolBodyStart = 0x8003A25Cu;
    const uint GoolStart = 0x8003A2ACu;
    const uint GoolEnd = 0x8003B0ECu;
    const uint GoolJr = 0x8003A328u;

    // All handler targets from the init blob @ 0x8005C7AC (plus dispatch/epilogue).
    static readonly uint[] GoolDispatchHandlers =
    [
        0x8003A304, 0x8003A330,
        0x8003A36C, 0x8003A390, 0x8003A3B4, 0x8003A3DC, 0x8003A404, 0x8003A42C,
        0x8003A44C, 0x8003A474, 0x8003A498, 0x8003A4C0, 0x8003A4E4, 0x8003A50C,
        0x8003A530, 0x8003A558, 0x8003A578, 0x8003A59C, 0x8003A5C0, 0x8003A5EC,
        0x8003A658, 0x8003A674, 0x8003A694, 0x8003A6C4, 0x8003A704, 0x8003A73C,
        0x8003A780, 0x8003A7A8, 0x8003A7C8, 0x8003A7FC, 0x8003A824, 0x8003A880,
        0x8003A8B0, 0x8003A8D8, 0x8003A904, 0x8003A99C, 0x8003AA50, 0x8003AB08,
        0x8003AB34, 0x8003AB60, 0x8003AB90, 0x8003AB9C, 0x8003ABA8, 0x8003ABD8,
        0x8003AC08, 0x8003AC2C, 0x8003ACAC, 0x8003ACDC, 0x8003AD0C, 0x8003AD18,
        0x8003AD24, 0x8003AD30, 0x8003AD3C, 0x8003AD48, 0x8003AD54, 0x8003AD60,
        0x8003AD6C, 0x8003AD84, 0x8003ADFC, 0x8003AE80, 0x8003AF30, 0x8003AF4C,
        0x8003AF7C, 0x8003AFA0, 0x8003AFAC, 0x8003AFD4, 0x8003B000, 0x8003B00C,
        0x8003B018, 0x8003B024, 0x8003B030, 0x8003B03C, 0x8003B070, 0x8003B0BC,
        0x8003B0C8, 0x8003B0D4, 0x8003B0E0,
    ];

    // SCES-00967 GOOL expression switches in func_80037930.
    static readonly uint[] GoolExprOuter =
    [
        0x800379E0, 0x80037AC0, 0x80037B18, 0x80037B50, 0x80037A74, 0x800379E0,
        0x80037B88, 0x80037C00, 0x80037E98, 0x80037C60, 0x80037D98, 0x80037EE4,
        0x800382C4, 0x80038364, 0x800383DC, 0x800383F0,
    ];

    static readonly uint[] GoolExprMid =
    [
        0x80037C88, 0x80037CAC, 0x80037CE4, 0x80037D14, 0x80037CD8, 0x80037D08,
        0x800383F0, 0x800383F0, 0x80037D6C, 0x80037D40, 0x80037F10, 0x80037F4C,
        0x80037F84, 0x80037FAC, 0x80037FC0, 0x80037FD4,
    ];

    static readonly uint[] GoolExprInner =
    [
        0x80037F10, 0x80037F4C, 0x80037F84, 0x80037FAC, 0x80037FC0, 0x80037FD4,
        0x80037FE4, 0x80037FF8, 0x80038008, 0x80038050, 0x8003806C, 0x80038080,
        0x80038090, 0x800380A0, 0x800380B4, 0x800380D0, 0x800380F4, 0x80038110,
        0x800381A4, 0x800383F0, 0x80038134, 0x800382A4,
    ];

    /// <summary>
    /// Call after function discovery, before jump-table analysis / emit.
    /// </summary>
    public static void Prepare(List<MipsFunction> funcs, MipsInstruction[] all)
    {
        MergeRange(funcs, all, GoolStart, GoolBodyStart, GoolEnd, "GOOL interpreter");
    }

    public static void Inject(List<MipsFunction> funcs)
    {
        InjectOne(funcs, GoolStart, GoolJr, GoolDispatchHandlers, "GOOL dispatch");
        InjectOne(funcs, 0x80037930u, 0x800379D8u, GoolExprOuter, "GOOL expr outer");
        InjectOne(funcs, 0x80037930u, 0x80037C80u, GoolExprMid, "GOOL expr mid");
        InjectOne(funcs, 0x80037930u, 0x80037F08u, GoolExprInner, "GOOL expr inner");
    }

    static void MergeRange(List<MipsFunction> funcs, MipsInstruction[] all, uint entry, uint bodyStart, uint end, string label)
    {
        var main = funcs.Find(f => f.Start == entry);
        if (main == null || all.Length == 0) return;

        int si = InstrIndex(all, bodyStart);
        int ei = InstrIndex(all, end);
        if (si < 0 || ei <= si) return;
        if (main.End >= end && main.Instructions.Length > 0 && main.Instructions[0].Vram <= bodyStart) return;

        uint oldEnd = main.End;
        main.End = end;
        main.Instructions = all[si..ei];
        main.JumpTables = [];
        Console.WriteLine($"[Recompiler] extended {label} body 0x{bodyStart:X8}..0x{oldEnd:X8} -> 0x{end:X8} (entry 0x{entry:X8})");
    }

    static void InjectOne(List<MipsFunction> funcs, uint funcStart, uint jrVram, uint[] entries, string label)
    {
        var f = funcs.Find(x => x.Start == funcStart);
        if (f == null) return;
        if (f.JumpTables.Any(j => j.JrVram == jrVram)) return;

        f.JumpTables.Add(new JumpTable { JrVram = jrVram, Entries = entries });
        Console.WriteLine($"[Recompiler] injected {label} jump table @ 0x{jrVram:X8} (func_{funcStart:X8})");
    }

    static int InstrIndex(MipsInstruction[] all, uint vram)
    {
        uint base0 = all[0].Vram;
        if (vram < base0) return -1;
        int i = (int)((vram - base0) / 4);
        return i <= all.Length ? i : -1;
    }
}
