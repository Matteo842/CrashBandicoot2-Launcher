namespace CrashBandicoot2.Launcher.Recomp;

/// <summary>
/// SCES-00967 first-boot fixes the recompiler does not yet emit.
/// </summary>
public static class PostPassApplier
{
    public static void Apply(string mainCsPath)
    {
        if (!File.Exists(mainCsPath))
            throw new FileNotFoundException("Generated main.cs not found.", mainCsPath);

        var src = File.ReadAllText(mainCsPath);
        var patched = FixCdStatusJumpTable(src);
        patched = FixPrintfJumpTable(patched);
        patched = FixStateJumpTable(patched);
        if (!ReferenceEquals(src, patched) && patched != src)
            File.WriteAllText(mainCsPath, patched);
    }

    /// <summary>
    /// Memory jump table at 0x80010B20 dispatches into mid-function labels inside
    /// func_800473B8. Recompiler emits Dispatcher.Call + return, leaving targets dead.
    /// </summary>
    static string FixCdStatusJumpTable(string src)
    {
        const string needle =
            """
                    c.V0 = m.ReadU32((c.At + 0xB20u));
                    Dispatcher.Call(c, m, c.V0);
                    return;
                    if (c.S1 == 0u) {
                        c.V0 = 0x00000005u;
                        goto L80047670;
                    }
            """;

        const string replacement =
            """
                    c.V0 = m.ReadU32((c.At + 0xB20u));
                    // SCES-00967: CD status jump table @ 0x80010B20 → mid-function cases
                    switch (c.V0)
                    {
                        case 0x80047624u: goto L80047624;
                        case 0x80047724u: goto L80047724;
                        case 0x80047770u: goto L80047770;
                        case 0x800477F4u: goto L800477F4;
                        case 0x80047878u: goto L80047878;
                        default:
                            Dispatcher.Call(c, m, c.V0);
                            return;
                    }
                    L80047624: ;
                    if (c.S1 == 0u) {
                        c.V0 = 0x00000005u;
                        goto L80047670;
                    }
            """;

        if (!src.Contains(needle, StringComparison.Ordinal))
        {
            if (src.Contains("L80047624:", StringComparison.Ordinal))
                return src; // already patched
            Console.WriteLine("[post-pass] warning: CD status jump table pattern not found");
            return src;
        }

        src = src.Replace(needle, replacement, StringComparison.Ordinal);

        // Label other table entry points (were unreachable after return).
        src = ReplaceOnce(src,
            """
                    c.V0 = 0x00000002u;
                    goto L80047928;
                    if (c.S1 == 0u) {
                        c.V0 = 0x00000002u;
                        goto L80047730;
                    }
            """,
            """
                    c.V0 = 0x00000002u;
                    goto L80047928;
                    L80047724: ;
                    if (c.S1 == 0u) {
                        c.V0 = 0x00000002u;
                        goto L80047730;
                    }
            """);

        src = ReplaceOnce(src,
            """
                    L80047768: ;
                    c.V0 = 0x00000002u;
                    goto L80047928;
                    if (c.S1 == 0u) {
                        c.V0 = 0x00000001u;
                        goto L80047794;
                    }
            """,
            """
                    L80047768: ;
                    c.V0 = 0x00000002u;
                    goto L80047928;
                    L80047770: ;
                    if (c.S1 == 0u) {
                        c.V0 = 0x00000001u;
                        goto L80047794;
                    }
            """);

        src = ReplaceOnce(src,
            """
                    goto L80047928;
                    c.A0 = 0x80060000u;
                    c.A0 = c.A0 + 0x100u;
                    c.V0 = 0x00000004u;
            """,
            """
                    goto L80047928;
                    L800477F4: ;
                    c.A0 = 0x80060000u;
                    c.A0 = c.A0 + 0x100u;
                    c.V0 = 0x00000004u;
            """);

        src = ReplaceOnce(src,
            """
                    L80047870: ;
                    c.V0 = 0x00000004u;
                    goto L80047928;
                    c.A0 = 0x80060000u;
                    c.A0 = c.A0 + 0xF0u;
            """,
            """
                    L80047870: ;
                    c.V0 = 0x00000004u;
                    goto L80047928;
                    L80047878: ;
                    c.A0 = 0x80060000u;
                    c.A0 = c.A0 + 0xF0u;
            """);

        Console.WriteLine("[post-pass] applied CD status jump table fix (func_800473B8)");
        return src;
    }

    /// <summary>
    /// libc printf format jump table @ 0x80010C2C inside func_800494EC.
    /// Hits on %X/%x/%s/… were emitted as Dispatcher.Call → unmapped mid-function addrs.
    /// </summary>
    static string FixPrintfJumpTable(string src)
    {
        if (src.Contains("L80049A64:", StringComparison.Ordinal))
            return src; // already patched

        const string needle =
            """
                    c.V0 = m.ReadU32((c.At + 0xC2Cu));
                    Dispatcher.Call(c, m, c.V0);
                    return;
                    c.V0 = m.ReadU32((c.SP + 0x210u));
                    c.V0 = c.V0 | 0x0020u;
                    goto L800497D8;
                    c.V0 = m.ReadU32((c.SP + 0x210u));
                    c.V0 = c.V0 | 0x0040u;
                    goto L800497D8;
                    c.V0 = m.ReadU32((c.SP + 0x210u));
                    c.V0 = c.V0 | 0x0080u;
                    L800497D8: ;
            """;

        const string replacement =
            """
                    c.V0 = m.ReadU32((c.At + 0xC2Cu));
                    // SCES-00967: printf format jump table @ 0x80010C2C → mid-function cases
                    switch (c.V0)
                    {
                        case 0x800497B4u: goto L800497B4;
                        case 0x800497C0u: goto L800497C0;
                        case 0x800497CCu: goto L800497CC;
                        case 0x800497F8u: goto L800497F8;
                        case 0x80049848u: goto L80049848;
                        case 0x80049960u: goto L80049960;
                        case 0x80049A50u: goto L80049A50;
                        case 0x80049A64u: goto L80049A64;
                        case 0x80049A74u: goto L80049A74;
                        case 0x80049B6Cu: goto L80049B6C;
                        case 0x80049B8Cu: goto L80049B8C;
                        case 0x80049C10u: goto L80049C10;
                        case 0x80049C44u: goto L80049C44;
                        default:
                            Dispatcher.Call(c, m, c.V0);
                            return;
                    }
                    L800497B4: ;
                    c.V0 = m.ReadU32((c.SP + 0x210u));
                    c.V0 = c.V0 | 0x0020u;
                    goto L800497D8;
                    L800497C0: ;
                    c.V0 = m.ReadU32((c.SP + 0x210u));
                    c.V0 = c.V0 | 0x0040u;
                    goto L800497D8;
                    L800497CC: ;
                    c.V0 = m.ReadU32((c.SP + 0x210u));
                    c.V0 = c.V0 | 0x0080u;
                    L800497D8: ;
            """;

        if (!src.Contains(needle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: printf jump table pattern not found");
            return src;
        }

        src = src.Replace(needle, replacement, StringComparison.Ordinal);

        // %d / %i
        src = ReplaceOnce(src,
            """
                    c.V1 = c.A1 - 0x4Cu;
                    goto L80049790;
                    c.V1 = m.ReadU32((c.SP + 0x220u));
                    c.V0 = c.V1 + 0x4u;
                    m.WriteU32((c.SP + 0x220u), c.V0);
                    c.A0 = m.ReadU32(c.V1);
                    c.V1 = m.ReadU32((c.SP + 0x210u));
                    c.V0 = c.V1 & 0x0020u;
            """,
            """
                    c.V1 = c.A1 - 0x4Cu;
                    goto L80049790;
                    L800497F8: ;
                    c.V1 = m.ReadU32((c.SP + 0x220u));
                    c.V0 = c.V1 + 0x4u;
                    m.WriteU32((c.SP + 0x220u), c.V0);
                    c.A0 = m.ReadU32(c.V1);
                    c.V1 = m.ReadU32((c.SP + 0x210u));
                    c.V0 = c.V1 & 0x0020u;
            """);

        // %u
        src = ReplaceOnce(src,
            """
                    m.WriteU8((c.SP + 0x211u), (byte)c.S6);
                    goto L80049878;
                    c.V1 = m.ReadU32((c.SP + 0x220u));
                    c.V0 = c.V1 + 0x4u;
                    m.WriteU32((c.SP + 0x220u), c.V0);
                    c.A0 = m.ReadU32(c.V1);
                    c.V0 = m.ReadU32((c.SP + 0x210u));
                    c.V0 = c.V0 & 0x0020u;
            """,
            """
                    m.WriteU8((c.SP + 0x211u), (byte)c.S6);
                    goto L80049878;
                    L80049848: ;
                    c.V1 = m.ReadU32((c.SP + 0x220u));
                    c.V0 = c.V1 + 0x4u;
                    m.WriteU32((c.SP + 0x220u), c.V0);
                    c.A0 = m.ReadU32(c.V1);
                    c.V0 = m.ReadU32((c.SP + 0x210u));
                    c.V0 = c.V0 & 0x0020u;
            """);

        // %o
        src = ReplaceOnce(src,
            """
                    c.S0 = c.S0 + 0x1u;
                    goto L80049C5C;
                    c.V1 = m.ReadU32((c.SP + 0x220u));
                    c.V0 = c.V1 + 0x4u;
                    m.WriteU32((c.SP + 0x220u), c.V0);
                    c.A0 = m.ReadU32(c.V1);
                    c.V1 = m.ReadU32((c.SP + 0x210u));
                    c.V0 = c.V1 & 0x0020u;
                    if (c.V0 == 0u) {
                        c.V0 = c.V1 & 0x0010u;
                        goto L8004998C;
                    }
            """,
            """
                    c.S0 = c.S0 + 0x1u;
                    goto L80049C5C;
                    L80049960: ;
                    c.V1 = m.ReadU32((c.SP + 0x220u));
                    c.V0 = c.V1 + 0x4u;
                    m.WriteU32((c.SP + 0x220u), c.V0);
                    c.A0 = m.ReadU32(c.V1);
                    c.V1 = m.ReadU32((c.SP + 0x210u));
                    c.V0 = c.V1 & 0x0020u;
                    if (c.V0 == 0u) {
                        c.V0 = c.V1 & 0x0010u;
                        goto L8004998C;
                    }
            """);

        // %p / %X / %x
        src = ReplaceOnce(src,
            """
                    c.S1 = c.S1 - 0x1u;
                    c.S1 = c.S1 + 0x1u;
                    goto L80049C5C;
                    c.V1 = m.ReadU32((c.SP + 0x210u));
                    c.V0 = 0x00000008u;
                    m.WriteU32((c.SP + 0x218u), c.V0);
                    c.V1 = c.V1 | 0x0050u;
                    m.WriteU32((c.SP + 0x210u), c.V1);
                    c.A3 = 0x80010000u;
                    c.A3 = c.A3 + 0xC04u;
                    goto L80049A7C;
                    c.A3 = 0x80010000u;
                    c.A3 = c.A3 + 0xC18u;
                    L80049A7C: ;
            """,
            """
                    c.S1 = c.S1 - 0x1u;
                    c.S1 = c.S1 + 0x1u;
                    goto L80049C5C;
                    L80049A50: ;
                    c.V1 = m.ReadU32((c.SP + 0x210u));
                    c.V0 = 0x00000008u;
                    m.WriteU32((c.SP + 0x218u), c.V0);
                    c.V1 = c.V1 | 0x0050u;
                    m.WriteU32((c.SP + 0x210u), c.V1);
                    L80049A64: ;
                    c.A3 = 0x80010000u;
                    c.A3 = c.A3 + 0xC04u;
                    goto L80049A7C;
                    L80049A74: ;
                    c.A3 = 0x80010000u;
                    c.A3 = c.A3 + 0xC18u;
                    L80049A7C: ;
            """);

        // %c
        src = ReplaceOnce(src,
            """
                    m.WriteU8(c.S1, (byte)c.V0);
                    goto L80049C5C;
                    c.V0 = m.ReadU32((c.SP + 0x220u));
                    c.S1 = c.S1 - 0x1u;
                    c.V1 = c.V0 + 0x4u;
                    m.WriteU32((c.SP + 0x220u), c.V1);
                    c.V0 = m.ReadU32(c.V0);
                    c.S0 = 0x00000001u;
                    m.WriteU8(c.S1, (byte)c.V0);
                    goto L80049C5C;
                    c.V0 = m.ReadU32((c.SP + 0x220u));
                    c.V1 = c.V0 + 0x4u;
                    m.WriteU32((c.SP + 0x220u), c.V1);
                    c.V1 = m.ReadU32((c.SP + 0x210u));
                    c.S1 = m.ReadU32(c.V0);
            """,
            """
                    m.WriteU8(c.S1, (byte)c.V0);
                    goto L80049C5C;
                    L80049B6C: ;
                    c.V0 = m.ReadU32((c.SP + 0x220u));
                    c.S1 = c.S1 - 0x1u;
                    c.V1 = c.V0 + 0x4u;
                    m.WriteU32((c.SP + 0x220u), c.V1);
                    c.V0 = m.ReadU32(c.V0);
                    c.S0 = 0x00000001u;
                    m.WriteU8(c.S1, (byte)c.V0);
                    goto L80049C5C;
                    L80049B8C: ;
                    c.V0 = m.ReadU32((c.SP + 0x220u));
                    c.V1 = c.V0 + 0x4u;
                    m.WriteU32((c.SP + 0x220u), c.V1);
                    c.V1 = m.ReadU32((c.SP + 0x210u));
                    c.S1 = m.ReadU32(c.V0);
            """);

        // %n
        src = ReplaceOnce(src,
            """
                    c.S0 = m.ReadU32((c.SP + 0x218u));
                    goto L80049C5C;
                    c.V0 = m.ReadU32((c.SP + 0x220u));
                    c.V1 = c.V0 + 0x4u;
                    m.WriteU32((c.SP + 0x220u), c.V1);
            """,
            """
                    c.S0 = m.ReadU32((c.SP + 0x218u));
                    goto L80049C5C;
                    L80049C10: ;
                    c.V0 = m.ReadU32((c.SP + 0x220u));
                    c.V1 = c.V0 + 0x4u;
                    m.WriteU32((c.SP + 0x220u), c.V1);
            """);

        Console.WriteLine("[post-pass] applied printf jump table fix (func_800494EC)");
        return src;
    }

    /// <summary>
    /// Small state switch jump table @ 0x80011198 inside func_800567A0 (6 cases).
    /// </summary>
    static string FixStateJumpTable(string src)
    {
        if (src.Contains("L80056828:", StringComparison.Ordinal))
            return src;

        const string needle =
            """
                    c.V0 = m.ReadU32((c.At + 0x1198u));
                    Dispatcher.Call(c, m, c.V0);
                    return;
                    c.V1 = 0x00000032u;
                    c.At = 0x80070000u;
                    m.WriteU32((c.At - 0x4BACu), c.V1);
                    c.V0 = 0x00000001u;
                    if (c.A0 == c.V0) {
                        c.V0 = 0x00000005u;
                        goto L80056868;
                    }
                    c.V0 = 0x00000005u;
                    c.At = 0x80060000u;
                    m.WriteU32((c.At - 0xD90u), c.V1);
                    goto L800568F8;
                    c.V0 = 0x0000003Cu;
                    c.At = 0x80070000u;
                    m.WriteU32((c.At - 0x4BACu), c.V0);
                    if (c.A0 != 0u) {
                        goto L80056868;
                    }
                    c.V0 = 0x00000005u;
                    L80056868: ;
                    c.At = 0x80060000u;
                    m.WriteU32((c.At - 0xD90u), c.V0);
                    goto L800568F8;
                    c.V0 = 0x00000078u;
                    c.At = 0x80070000u;
                    m.WriteU32((c.At - 0x4BACu), c.V0);
                    goto L800568F8;
                    c.V0 = 0x000000F0u;
                    c.At = 0x80070000u;
                    m.WriteU32((c.At - 0x4BACu), c.V0);
                    goto L800568F8;
                    if (c.A0 == 0u) {
                        c.V0 = 0x00000001u;
                        goto L800568DC;
                    }
                    c.V0 = 0x00000001u;
                    if (c.A0 == c.V0) {
                        c.V0 = 0x00000032u;
                        goto L800568CC;
                    }
                    c.V0 = 0x00000032u;
                    c.V0 = 0x0000003Cu;
                    goto L800568E0;
                    if (c.A0 == 0u) {
                        c.V0 = 0x00000001u;
                        goto L800568DC;
                    }
            """;

        const string replacement =
            """
                    c.V0 = m.ReadU32((c.At + 0x1198u));
                    // SCES-00967: state jump table @ 0x80011198 → mid-function cases
                    switch (c.V0)
                    {
                        case 0x800568B8u: goto L800568B8;
                        case 0x80056850u: goto L80056850;
                        case 0x8005688Cu: goto L8005688C;
                        case 0x80056878u: goto L80056878;
                        case 0x80056828u: goto L80056828;
                        case 0x800568A0u: goto L800568A0;
                        default:
                            Dispatcher.Call(c, m, c.V0);
                            return;
                    }
                    L80056828: ;
                    c.V1 = 0x00000032u;
                    c.At = 0x80070000u;
                    m.WriteU32((c.At - 0x4BACu), c.V1);
                    c.V0 = 0x00000001u;
                    if (c.A0 == c.V0) {
                        c.V0 = 0x00000005u;
                        goto L80056868;
                    }
                    c.V0 = 0x00000005u;
                    c.At = 0x80060000u;
                    m.WriteU32((c.At - 0xD90u), c.V1);
                    goto L800568F8;
                    L80056850: ;
                    c.V0 = 0x0000003Cu;
                    c.At = 0x80070000u;
                    m.WriteU32((c.At - 0x4BACu), c.V0);
                    if (c.A0 != 0u) {
                        goto L80056868;
                    }
                    c.V0 = 0x00000005u;
                    L80056868: ;
                    c.At = 0x80060000u;
                    m.WriteU32((c.At - 0xD90u), c.V0);
                    goto L800568F8;
                    L80056878: ;
                    c.V0 = 0x00000078u;
                    c.At = 0x80070000u;
                    m.WriteU32((c.At - 0x4BACu), c.V0);
                    goto L800568F8;
                    L8005688C: ;
                    c.V0 = 0x000000F0u;
                    c.At = 0x80070000u;
                    m.WriteU32((c.At - 0x4BACu), c.V0);
                    goto L800568F8;
                    L800568A0: ;
                    if (c.A0 == 0u) {
                        c.V0 = 0x00000001u;
                        goto L800568DC;
                    }
                    c.V0 = 0x00000001u;
                    if (c.A0 == c.V0) {
                        c.V0 = 0x00000032u;
                        goto L800568CC;
                    }
                    c.V0 = 0x00000032u;
                    c.V0 = 0x0000003Cu;
                    goto L800568E0;
                    L800568B8: ;
                    if (c.A0 == 0u) {
                        c.V0 = 0x00000001u;
                        goto L800568DC;
                    }
            """;

        if (!src.Contains(needle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: state jump table pattern not found");
            return src;
        }

        src = src.Replace(needle, replacement, StringComparison.Ordinal);
        Console.WriteLine("[post-pass] applied state jump table fix (func_800567A0)");
        return src;
    }

    static string ReplaceOnce(string src, string oldValue, string newValue)
    {
        var idx = src.IndexOf(oldValue, StringComparison.Ordinal);
        if (idx < 0)
        {
            Console.WriteLine("[post-pass] warning: expected snippet not found");
            return src;
        }
        return string.Concat(src.AsSpan(0, idx), newValue, src.AsSpan(idx + oldValue.Length));
    }
}
