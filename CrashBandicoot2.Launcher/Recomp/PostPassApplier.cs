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
        patched = FixAlphaJumpTable(patched);
        patched = FixLevelParamJumpTable(patched);
        patched = FixStreamStateJumpTable(patched);
        patched = FixEntryTypeJumpTable(patched);
        patched = FixGameModeJumpTable(patched);
        patched = FixCamInterpJumpTable(patched);
        patched = FixPadModeJumpTable(patched);
        patched = FixLevelAudioJumpTable(patched);
        patched = FixTexDecompBulkCopy(patched);
        patched = FixPolyRasterContinuations(patched);
        patched = FixGoolFallthrough38D94(patched);
        patched = FixGoolFallthroughMidEntries(patched);
        patched = FixGoolNativeCdahS0(patched);
        // Matrix mid-entries (444CC/44514/445CC/44324) are applied in-tree on main.cs for
        // Pipeline 30+; fold into a dedicated fixer before the next full MIPS recomp.
        if (!ReferenceEquals(src, patched) && patched != src)
            File.WriteAllText(mainCsPath, patched);

        // Entry.cs: prefer MIPS interp for unmapped mid-entries (C# labels often VA-skewed).
        // Do not RegisterRange over the GOOL helper mega-fn.
    }

    /// <summary>
    /// Intro NSF GOOL opcode-49 native at VA 0x8010DDB0 (CdahS). Not in main EXE —
    /// register HLE so Dispatcher.Call(S5) resolves after NSF page-in.
    /// </summary>
    static string FixGoolNativeCdahS0(string src)
    {
        if (src.Contains("[0x8010DDB0u]", StringComparison.Ordinal))
            return src;

        const string needle = "[0x80038D94u] = CrashBandicoot2.func_80038D94,";
        const string alt = "[0x80037930u] = CrashBandicoot2.func_80037930,";
        const string entry = "[0x8010DDB0u] = RecompOne.Runtime.Sdk.LibGool.NativeCdahS0,";

        if (src.Contains(needle, StringComparison.Ordinal))
            src = src.Replace(needle, needle + "\n            " + entry, StringComparison.Ordinal);
        else if (src.Contains(alt, StringComparison.Ordinal))
            src = src.Replace(alt, alt + "\n            " + entry, StringComparison.Ordinal);
        else
        {
            Console.WriteLine("[post-pass] warning: could not inject GOOL native 0x8010DDB0 map entry");
            return src;
        }

        Console.WriteLine("[post-pass] registered GOOL native HLE @ 0x8010DDB0 (CdahS)");
        return src;
    }

    /// <summary>
    /// After func_80037930's epilogue @ L80038D78, a helper was left as dead fallthrough.
    /// Callers hit 0x80038D94 unmapped. Expose as alternate entry.
    /// </summary>
    static string FixGoolFallthrough38D94(string src)
    {
        if (src.Contains("func_80037930_entry", StringComparison.Ordinal) ||
            src.Contains("[0x80038D94u]", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);

        src = ReplaceOnce(src,
            """
                    c.SP = c.SP + 0x80u;
                    return;
                    c.SP = c.SP - 0x48u;
                    m.WriteU32((c.SP + 0x20u), c.S0);
            """,
            """
                    c.SP = c.SP + 0x80u;
                    return;
                    L80038D94: ;
                    c.SP = c.SP - 0x48u;
                    m.WriteU32((c.SP + 0x20u), c.S0);
            """);

        const string hdr = "    public static void func_80037930(CpuContext c, IMemory m)\n    {\n";
        var hi = src.IndexOf(hdr, StringComparison.Ordinal);
        if (hi < 0)
        {
            Console.WriteLine("[post-pass] warning: func_80037930 header not found for 38D94 fix");
            return src;
        }
        var after = hi + hdr.Length;
        var eol = src.IndexOf('\n', after);
        var firstBody = src[after..eol];

        var replacement =
            "    public static void func_80037930(CpuContext c, IMemory m) => func_80037930_entry(c, m, 0x80037930u);\n" +
            "    public static void func_80038D94(CpuContext c, IMemory m) => func_80037930_entry(c, m, 0x80038D94u);\n" +
            "    static void func_80037930_entry(CpuContext c, IMemory m, uint entry)\n" +
            "    {\n" +
            "        // SCES-00967: GOOL helper mid-entry (fallthrough after epilogue)\n" +
            "        switch (entry)\n" +
            "        {\n" +
            "            case 0x80038D94u: goto L80038D94;\n" +
            "        }\n" +
            firstBody;

        src = string.Concat(src.AsSpan(0, hi), replacement, src.AsSpan(eol));

        src = src.Replace(
            "[0x80037930u] = CrashBandicoot2.func_80037930,",
            "[0x80037930u] = CrashBandicoot2.func_80037930,\n            [0x80038D94u] = CrashBandicoot2.func_80038D94,",
            StringComparison.Ordinal);

        Console.WriteLine("[post-pass] applied GOOL fallthrough mid-entry fix (0x80038D94)");
        return src;
    }

    /// <summary>
    /// Extra stacked helpers between func_80037930 epilogues and func_800390AC.
    /// Requires existing func_80037930_entry (from FixGoolFallthrough38D94 / prior pipeline).
    /// </summary>
    static string FixGoolFallthroughMidEntries(string src)
    {
        if (!src.Contains("func_80037930_entry", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);

        // (label, fallthrough probe just after an epilogue return)
        (string Addr, string Probe)[] entries =
        [
            ("80038414", "c.SP = c.SP - 0x88u;"),
            ("80038998", "c.SP = c.SP - 0x80u;"),
            ("80038EAC", "c.SP = c.SP - 0x20u;"),
            ("80038FA4", "c.SP = c.SP - 0x20u;"),
        ];

        var added = 0;
        foreach (var (addr, probe) in entries)
        {
            var lab = $"L{addr}";
            var fn = $"func_{addr}";
            var hex = $"0x{addr}";

            if (!src.Contains($"{lab}:", StringComparison.Ordinal))
            {
                // Prefer the first unlabeled fallthrough matching the probe after a return.
                var marked = $"        return;\n        {lab}: ;\n        {probe}";
                var plain = $"        return;\n        {probe}";
                if (src.Contains(plain, StringComparison.Ordinal) &&
                    !src.Contains(marked, StringComparison.Ordinal))
                {
                    src = ReplaceOnce(src, plain, marked);
                }
            }

            if (!src.Contains($"public static void {fn}(", StringComparison.Ordinal) &&
                src.Contains("public static void func_80038D94(", StringComparison.Ordinal))
            {
                src = src.Replace(
                    "    public static void func_80038D94(CpuContext c, IMemory m) => func_80037930_entry(c, m, 0x80038D94u);\n",
                    $"    public static void func_80038D94(CpuContext c, IMemory m) => func_80037930_entry(c, m, 0x80038D94u);\n" +
                    $"    public static void {fn}(CpuContext c, IMemory m) => func_80037930_entry(c, m, {hex}u);\n",
                    StringComparison.Ordinal);
            }

            if (!src.Contains($"case {hex}u:", StringComparison.Ordinal) &&
                src.Contains("case 0x80038D94u:", StringComparison.Ordinal))
            {
                src = src.Replace(
                    "            case 0x80038D94u: goto L80038D94;\n",
                    $"            case 0x80038D94u: goto L80038D94;\n            case {hex}u: goto {lab};\n",
                    StringComparison.Ordinal);
            }

            if (!src.Contains($"[{hex}u]", StringComparison.Ordinal) &&
                src.Contains("[0x80038D94u] = CrashBandicoot2.func_80038D94,", StringComparison.Ordinal))
            {
                src = src.Replace(
                    "[0x80038D94u] = CrashBandicoot2.func_80038D94,",
                    $"[0x80038D94u] = CrashBandicoot2.func_80038D94,\n            [{hex}u] = CrashBandicoot2.{fn},",
                    StringComparison.Ordinal);
                added++;
            }
            else if (!src.Contains($"[{hex}u]", StringComparison.Ordinal) &&
                     src.Contains("[0x80037930u] = CrashBandicoot2.func_80037930,", StringComparison.Ordinal))
            {
                src = src.Replace(
                    "[0x80037930u] = CrashBandicoot2.func_80037930,",
                    $"[0x80037930u] = CrashBandicoot2.func_80037930,\n            [{hex}u] = CrashBandicoot2.{fn},",
                    StringComparison.Ordinal);
                added++;
            }
        }

        if (added > 0)
            Console.WriteLine($"[post-pass] applied GOOL fallthrough mid-entries (+{added} map hooks)");
        return src;
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

    /// <summary>
    /// Alphabetical char→code jump table @ 0x80010068 inside func_80012010 (A–Z).
    /// Used while parsing CD paths after the first sector reads (e.g. letter 'N').
    /// </summary>
    static string FixAlphaJumpTable(string src)
    {
        if (src.Contains("L800120B0:", StringComparison.Ordinal))
            return src;

        const string needle =
            """
                    c.V0 = m.ReadU32((c.At + 0x68u));
                    Dispatcher.Call(c, m, c.V0);
                    return;
                    c.V0 = 0u | 0x0001u;
                    goto L800120E4;
                    c.V0 = 0u | 0x0004u;
                    goto L800120E4;
                    c.V0 = 0u | 0x0002u;
                    goto L800120E4;
                    c.V0 = 0u | 0x0003u;
                    goto L800120E4;
                    c.V0 = 0u | 0x0005u;
                    goto L800120E4;
                    c.V0 = 0u | 0x0006u;
                    goto L800120E4;
                    c.V0 = 0u | 0x0007u;
                    goto L800120E4;
                    c.V0 = 0u | 0x000Bu;
                    goto L800120E4;
                    c.V0 = 0u | 0x000Cu;
                    goto L800120E4;
                    c.V0 = 0u | 0x0014u;
                    goto L800120E4;
                    c.V0 = 0u | 0x000Du;
                    goto L800120E4;
                    c.V0 = 0u | 0x0010u;
                    goto L800120E4;
                    c.V0 = 0u | 0x000Eu;
                    goto L800120E4;
                    c.V0 = 0u | 0x0011u;
                    goto L800120E4;
                    c.V0 = 0u | 0x0012u;
                    goto L800120E4;
                    c.V0 = 0u | 0x0013u;
                    goto L800120E4;
                    c.V0 = 0u | 0x0015u;
                    goto L800120E4;
                    c.V0 = 0u | 0x000Fu;
                    goto L800120E4;
                    L800120E0: ;
                    c.V0 = 0u + 0u;
                    L800120E4: ;
            """;

        const string replacement =
            """
                    c.V0 = m.ReadU32((c.At + 0x68u));
                    // SCES-00967: alpha jump table @ 0x80010068 → mid-function cases
                    switch (c.V0)
                    {
                        case 0x80012050u: goto L80012050;
                        case 0x80012058u: goto L80012058;
                        case 0x80012060u: goto L80012060;
                        case 0x80012068u: goto L80012068;
                        case 0x80012070u: goto L80012070;
                        case 0x80012078u: goto L80012078;
                        case 0x80012080u: goto L80012080;
                        case 0x80012088u: goto L80012088;
                        case 0x80012090u: goto L80012090;
                        case 0x80012098u: goto L80012098;
                        case 0x800120A0u: goto L800120A0;
                        case 0x800120A8u: goto L800120A8;
                        case 0x800120B0u: goto L800120B0;
                        case 0x800120B8u: goto L800120B8;
                        case 0x800120C0u: goto L800120C0;
                        case 0x800120C8u: goto L800120C8;
                        case 0x800120D0u: goto L800120D0;
                        case 0x800120D8u: goto L800120D8;
                        case 0x800120E0u: goto L800120E0;
                        default:
                            Dispatcher.Call(c, m, c.V0);
                            return;
                    }
                    L80012050: ;
                    c.V0 = 0u | 0x0001u;
                    goto L800120E4;
                    L80012058: ;
                    c.V0 = 0u | 0x0004u;
                    goto L800120E4;
                    L80012060: ;
                    c.V0 = 0u | 0x0002u;
                    goto L800120E4;
                    L80012068: ;
                    c.V0 = 0u | 0x0003u;
                    goto L800120E4;
                    L80012070: ;
                    c.V0 = 0u | 0x0005u;
                    goto L800120E4;
                    L80012078: ;
                    c.V0 = 0u | 0x0006u;
                    goto L800120E4;
                    L80012080: ;
                    c.V0 = 0u | 0x0007u;
                    goto L800120E4;
                    L80012088: ;
                    c.V0 = 0u | 0x000Bu;
                    goto L800120E4;
                    L80012090: ;
                    c.V0 = 0u | 0x000Cu;
                    goto L800120E4;
                    L80012098: ;
                    c.V0 = 0u | 0x0014u;
                    goto L800120E4;
                    L800120A0: ;
                    c.V0 = 0u | 0x000Du;
                    goto L800120E4;
                    L800120A8: ;
                    c.V0 = 0u | 0x0010u;
                    goto L800120E4;
                    L800120B0: ;
                    c.V0 = 0u | 0x000Eu;
                    goto L800120E4;
                    L800120B8: ;
                    c.V0 = 0u | 0x0011u;
                    goto L800120E4;
                    L800120C0: ;
                    c.V0 = 0u | 0x0012u;
                    goto L800120E4;
                    L800120C8: ;
                    c.V0 = 0u | 0x0013u;
                    goto L800120E4;
                    L800120D0: ;
                    c.V0 = 0u | 0x0015u;
                    goto L800120E4;
                    L800120D8: ;
                    c.V0 = 0u | 0x000Fu;
                    goto L800120E4;
                    L800120E0: ;
                    c.V0 = 0u + 0u;
                    L800120E4: ;
            """;

        if (!src.Contains(needle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: alpha jump table pattern not found");
            return src;
        }

        src = src.Replace(needle, replacement, StringComparison.Ordinal);
        Console.WriteLine("[post-pass] applied alpha jump table fix (func_80012010)");
        return src;
    }

    /// <summary>
    /// Level/mode parameter jump table @ 0x800101B0 inside func_80014D6C
    /// (59 entries on S5-2; only 4 distinct mid-function targets).
    /// </summary>
    static string FixLevelParamJumpTable(string src)
    {
        if (src.Contains("L800152B8:", StringComparison.Ordinal))
            return src;

        const string needle =
            """
                    c.V0 = m.ReadU32((c.At + 0x1B0u));
                    Dispatcher.Call(c, m, c.V0);
                    return;
                    c.S1 = 0u | 0x8000u;
                    c.S0 = 0u | 0x0002u;
                    goto L800152C8;
                    c.S1 = 0x00010000u;
                    c.S0 = 0u | 0x0002u;
                    goto L800152C8;
                    c.S1 = 0u | 0xEA60u;
                    c.S0 = 0u | 0x0001u;
                    c.A2 = 0x00030000u;
                    c.A2 = c.A2 | 0xEE40u;
                    L800152C8: ;
            """;

        const string replacement =
            """
                    c.V0 = m.ReadU32((c.At + 0x1B0u));
                    // SCES-00967: level-param jump table @ 0x800101B0 → mid-function cases
                    switch (c.V0)
                    {
                        case 0x800152A0u: goto L800152A0;
                        case 0x800152ACu: goto L800152AC;
                        case 0x800152B8u: goto L800152B8;
                        case 0x800152C8u: goto L800152C8;
                        default:
                            Dispatcher.Call(c, m, c.V0);
                            return;
                    }
                    L800152A0: ;
                    c.S1 = 0u | 0x8000u;
                    c.S0 = 0u | 0x0002u;
                    goto L800152C8;
                    L800152AC: ;
                    c.S1 = 0x00010000u;
                    c.S0 = 0u | 0x0002u;
                    goto L800152C8;
                    L800152B8: ;
                    c.S1 = 0u | 0xEA60u;
                    c.S0 = 0u | 0x0001u;
                    c.A2 = 0x00030000u;
                    c.A2 = c.A2 | 0xEE40u;
                    L800152C8: ;
            """;

        if (!src.Contains(needle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: level-param jump table pattern not found");
            return src;
        }

        src = src.Replace(needle, replacement, StringComparison.Ordinal);
        Console.WriteLine("[post-pass] applied level-param jump table fix (func_80014D6C)");
        return src;
    }

    /// <summary>
    /// Stream/object state jump table @ 0x80010138 inside func_80013304
    /// (30 entries on (halfword-2); 8 distinct mid-function targets).
    /// </summary>
    static string FixStreamStateJumpTable(string src)
    {
        if (src.Contains("L800133AC:", StringComparison.Ordinal))
            return src;

        const string needle =
            """
                    c.V0 = m.ReadU32((c.At + 0x138u));
                    Dispatcher.Call(c, m, c.V0);
                    return;
                    c.V0 = m.ReadU32((c.S0 + 0x10u));
                    c.V1 = m.ReadU16(c.V0);
                    c.V0 = 0u | 0x8765u;
                    if (c.V1 == c.V0) {
                        goto L800137AC;
                    }
            """;

        const string replacement =
            """
                    c.V0 = m.ReadU32((c.At + 0x138u));
                    // SCES-00967: stream-state jump table @ 0x80010138 → mid-function cases
                    switch (c.V0)
                    {
                        case 0x8001335Cu: goto L8001335C;
                        case 0x800133ACu: goto L800133AC;
                        case 0x80013440u: goto L80013440;
                        case 0x8001350Cu: goto L8001350C;
                        case 0x80013560u: goto L80013560;
                        case 0x80013724u: goto L80013724;
                        case 0x800137A0u: goto L800137A0;
                        case 0x800137ACu: goto L800137AC;
                        default:
                            Dispatcher.Call(c, m, c.V0);
                            return;
                    }
                    L8001335C: ;
                    c.V0 = m.ReadU32((c.S0 + 0x10u));
                    c.V1 = m.ReadU16(c.V0);
                    c.V0 = 0u | 0x8765u;
                    if (c.V1 == c.V0) {
                        goto L800137AC;
                    }
            """;

        if (!src.Contains(needle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: stream-state jump table pattern not found");
            return src;
        }

        src = src.Replace(needle, replacement, StringComparison.Ordinal);

        src = ReplaceOnce(src,
            """
                    goto L80013324;
                    c.S3 = 0x80070000u;
                    c.S3 = c.S3 - 0x7DB4u;
                    c.V0 = m.ReadU32(c.S3);
            """,
            """
                    goto L80013324;
                    L800133AC: ;
                    c.S3 = 0x80070000u;
                    c.S3 = c.S3 - 0x7DB4u;
                    c.V0 = m.ReadU32(c.S3);
            """);

        src = ReplaceOnce(src,
            """
                    c.S0 = c.S1 + 0u;
                    goto L80013324;
                    c.V0 = m.ReadU32((c.GP + 0x28u));
                    if (c.V0 == 0u) {
                        goto L800134EC;
                    }
            """,
            """
                    c.S0 = c.S1 + 0u;
                    goto L80013324;
                    L80013440: ;
                    c.V0 = m.ReadU32((c.GP + 0x28u));
                    if (c.V0 == 0u) {
                        goto L800134EC;
                    }
            """);

        src = ReplaceOnce(src,
            """
                    goto L800137AC;
                    c.V0 = 0u | 0x0001u;
                    m.WriteU16((c.S0 + 0x4u), (ushort)c.V0);
                    m.WriteU8((c.S0 + 0xFu), (byte)0u);
                    L800137AC: ;
            """,
            """
                    goto L800137AC;
                    L800137A0: ;
                    c.V0 = 0u | 0x0001u;
                    m.WriteU16((c.S0 + 0x4u), (ushort)c.V0);
                    m.WriteU8((c.S0 + 0xFu), (byte)0u);
                    L800137AC: ;
            """);

        Console.WriteLine("[post-pass] applied stream-state jump table fix (func_80013304)");
        return src;
    }

    /// <summary>
    /// NSD entry-type jump table @ 0x80010120 inside func_800126BC (6 entries, 4 targets).
    /// </summary>
    static string FixEntryTypeJumpTable(string src)
    {
        if (src.Contains("L80012748:", StringComparison.Ordinal))
            return src;

        const string needle =
            """
                    c.V0 = m.ReadU32((c.At + 0x120u));
                    Dispatcher.Call(c, m, c.V0);
                    return;
                    c.A1 = m.ReadU32(c.S1);
                    c.V0 = m.ReadU32((c.A1 + 0x10u));
                    c.V0 = m.ReadU32((c.V0 + 0x4u));
                    c.V1 = 0x80060000u;
                    c.V1 = m.ReadU32((c.V1 + 0x7828u));
                    c.A0 = m.ReadU32((c.A1 + 0x8u));
            """;

        const string replacement =
            """
                    c.V0 = m.ReadU32((c.At + 0x120u));
                    // SCES-00967: entry-type jump table @ 0x80010120 → mid-function cases
                    switch (c.V0)
                    {
                        case 0x80012748u: goto L80012748;
                        case 0x8001282Cu: goto L8001282C;
                        case 0x80012940u: goto L80012940;
                        case 0x80012AB0u: goto L80012AB0;
                        default:
                            Dispatcher.Call(c, m, c.V0);
                            return;
                    }
                    L80012748: ;
                    c.A1 = m.ReadU32(c.S1);
                    c.V0 = m.ReadU32((c.A1 + 0x10u));
                    c.V0 = m.ReadU32((c.V0 + 0x4u));
                    c.V1 = 0x80060000u;
                    c.V1 = m.ReadU32((c.V1 + 0x7828u));
                    c.A0 = m.ReadU32((c.A1 + 0x8u));
            """;

        if (!src.Contains(needle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: entry-type jump table pattern not found");
            return src;
        }

        src = src.Replace(needle, replacement, StringComparison.Ordinal);

        src = ReplaceOnce(src,
            """
                    goto L80012AB0;
                    c.A0 = m.ReadU32((c.S1 + 0x18u));
                    c.RA = 0x80012838u;
                    CrashBandicoot2.func_8001398C(c, m);
            """,
            """
                    goto L80012AB0;
                    L8001282C: ;
                    c.A0 = m.ReadU32((c.S1 + 0x18u));
                    c.RA = 0x80012838u;
                    CrashBandicoot2.func_8001398C(c, m);
            """);

        src = ReplaceOnce(src,
            """
                    goto L80012ABC;
                    c.A1 = m.ReadU32(c.S1);
                    c.V0 = m.ReadU32((c.A1 + 0x10u));
                    c.V0 = m.ReadU32((c.V0 + 0x4u));
                    c.V1 = 0x80060000u;
                    c.V1 = m.ReadU32((c.V1 + 0x7828u));
                    c.S4 = m.ReadU32((c.S1 + 0x24u));
            """,
            """
                    goto L80012ABC;
                    L80012940: ;
                    c.A1 = m.ReadU32(c.S1);
                    c.V0 = m.ReadU32((c.A1 + 0x10u));
                    c.V0 = m.ReadU32((c.V0 + 0x4u));
                    c.V1 = 0x80060000u;
                    c.V1 = m.ReadU32((c.V1 + 0x7828u));
                    c.S4 = m.ReadU32((c.S1 + 0x24u));
            """);

        Console.WriteLine("[post-pass] applied entry-type jump table fix (func_800126BC)");
        return src;
    }

    /// <summary>
    /// Game-mode dispatch jump table @ 0x80010480 inside func_80026F14 (11 entries).
    /// Hit after PresentPump / first large NSD page-ins.
    /// </summary>
    static string FixGameModeJumpTable(string src)
    {
        if (src.Contains("L800270B8:", StringComparison.Ordinal))
            return src;

        const string needle =
            """
                    c.V0 = m.ReadU32((c.At + 0x480u));
                    Dispatcher.Call(c, m, c.V0);
                    return;
                    c.A0 = c.S0 + 0u;
                    c.A1 = c.S1 + 0u;
                    c.RA = 0x800270C4u;
                    CrashBandicoot2.func_80022978(c, m);
                    c.V0 = 0u | 0x0001u;
                    goto L80027150;
                    c.A0 = c.S0 + 0u;
                    c.RA = 0x800270D4u;
                    CrashBandicoot2.func_80022AD4(c, m);
                    c.V0 = 0u | 0x0001u;
                    goto L80027150;
                    c.A0 = c.S0 + 0u;
                    c.A1 = c.S1 + 0u;
                    c.RA = 0x800270E8u;
                    CrashBandicoot2.func_80021C64(c, m);
                    c.V0 = 0u | 0x0001u;
                    goto L80027150;
                    c.A0 = c.S0 + 0u;
                    c.A1 = c.S1 + 0u;
                    c.RA = 0x800270FCu;
                    CrashBandicoot2.func_800221A8(c, m);
                    c.V0 = 0u | 0x0001u;
                    goto L80027150;
                    c.A0 = c.S0 + 0u;
                    c.A1 = c.S1 + 0u;
                    c.RA = 0x80027110u;
                    CrashBandicoot2.func_80022570(c, m);
                    c.V0 = 0u | 0x0001u;
                    goto L80027150;
                    c.A0 = c.S0 + 0u;
                    c.A1 = c.S1 + 0u;
                    c.RA = 0x80027124u;
                    CrashBandicoot2.func_8002281C(c, m);
                    c.V0 = 0u | 0x0001u;
                    goto L80027150;
                    L8002712C: ;
            """;

        const string replacement =
            """
                    c.V0 = m.ReadU32((c.At + 0x480u));
                    // SCES-00967: game-mode jump table @ 0x80010480 → mid-function cases
                    switch (c.V0)
                    {
                        case 0x800270B8u: goto L800270B8;
                        case 0x800270CCu: goto L800270CC;
                        case 0x800270DCu: goto L800270DC;
                        case 0x800270F0u: goto L800270F0;
                        case 0x80027104u: goto L80027104;
                        case 0x80027118u: goto L80027118;
                        case 0x8002712Cu: goto L8002712C;
                        default:
                            Dispatcher.Call(c, m, c.V0);
                            return;
                    }
                    L800270B8: ;
                    c.A0 = c.S0 + 0u;
                    c.A1 = c.S1 + 0u;
                    c.RA = 0x800270C4u;
                    CrashBandicoot2.func_80022978(c, m);
                    c.V0 = 0u | 0x0001u;
                    goto L80027150;
                    L800270CC: ;
                    c.A0 = c.S0 + 0u;
                    c.RA = 0x800270D4u;
                    CrashBandicoot2.func_80022AD4(c, m);
                    c.V0 = 0u | 0x0001u;
                    goto L80027150;
                    L800270DC: ;
                    c.A0 = c.S0 + 0u;
                    c.A1 = c.S1 + 0u;
                    c.RA = 0x800270E8u;
                    CrashBandicoot2.func_80021C64(c, m);
                    c.V0 = 0u | 0x0001u;
                    goto L80027150;
                    L800270F0: ;
                    c.A0 = c.S0 + 0u;
                    c.A1 = c.S1 + 0u;
                    c.RA = 0x800270FCu;
                    CrashBandicoot2.func_800221A8(c, m);
                    c.V0 = 0u | 0x0001u;
                    goto L80027150;
                    L80027104: ;
                    c.A0 = c.S0 + 0u;
                    c.A1 = c.S1 + 0u;
                    c.RA = 0x80027110u;
                    CrashBandicoot2.func_80022570(c, m);
                    c.V0 = 0u | 0x0001u;
                    goto L80027150;
                    L80027118: ;
                    c.A0 = c.S0 + 0u;
                    c.A1 = c.S1 + 0u;
                    c.RA = 0x80027124u;
                    CrashBandicoot2.func_8002281C(c, m);
                    c.V0 = 0u | 0x0001u;
                    goto L80027150;
                    L8002712C: ;
            """;

        if (!src.Contains(needle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: game-mode jump table pattern not found");
            return src;
        }

        src = src.Replace(needle, replacement, StringComparison.Ordinal);
        Console.WriteLine("[post-pass] applied game-mode jump table fix (func_80026F14)");
        return src;
    }

    /// <summary>
    /// Camera/interp mode jump table @ 0x80010438 inside func_80023D78
    /// (11 entries, only 2 distinct targets: interpolate vs raw).
    /// </summary>
    static string FixCamInterpJumpTable(string src)
    {
        if (src.Contains("L80023F40:", StringComparison.Ordinal))
            return src;

        const string needle =
            """
                    c.V0 = m.ReadU32((c.At + 0x438u));
                    Dispatcher.Call(c, m, c.V0);
                    return;
                    c.V0 = (uint)(short)m.ReadU16(c.A0);
                    c.V1 = (uint)(short)m.ReadU16((c.A0 + 0x2u));
                    c.A1 = m.ReadU32((c.S2 + 0xCu));
                    c.A3 = c.V0 << 8;
                    if (c.A1 == 0u) {
                        c.A2 = c.V1 << 8;
                        goto L80023FBC;
                    }
            """;

        const string replacement =
            """
                    c.V0 = m.ReadU32((c.At + 0x438u));
                    // SCES-00967: cam-interp jump table @ 0x80010438 → mid-function cases
                    switch (c.V0)
                    {
                        case 0x80023F40u: goto L80023F40;
                        case 0x80023FACu: goto L80023FAC;
                        default:
                            Dispatcher.Call(c, m, c.V0);
                            return;
                    }
                    L80023F40: ;
                    c.V0 = (uint)(short)m.ReadU16(c.A0);
                    c.V1 = (uint)(short)m.ReadU16((c.A0 + 0x2u));
                    c.A1 = m.ReadU32((c.S2 + 0xCu));
                    c.A3 = c.V0 << 8;
                    if (c.A1 == 0u) {
                        c.A2 = c.V1 << 8;
                        goto L80023FBC;
                    }
            """;

        if (!src.Contains(needle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: cam-interp jump table pattern not found");
            return src;
        }

        src = src.Replace(needle, replacement, StringComparison.Ordinal);
        Console.WriteLine("[post-pass] applied cam-interp jump table fix (func_80023D78)");
        return src;
    }

    /// <summary>
    /// Pad/input mode jump table @ 0x80010668 inside func_800347D4 (5 entries).
    /// </summary>
    static string FixPadModeJumpTable(string src)
    {
        if (src.Contains("L80034818:", StringComparison.Ordinal))
            return src;

        const string needle =
            """
                    c.V0 = m.ReadU32((c.At + 0x668u));
                    Dispatcher.Call(c, m, c.V0);
                    return;
                    c.A0 = (uint)(short)m.ReadU16((c.GP + 0x268u));
                    c.A1 = (uint)(short)m.ReadU16(c.S0);
                    c.A2 = 0u | 0x0001u;
                    c.A3 = c.A3 << 16;
                    c.A3 = (uint)((int)c.A3 >> 16);
                    c.V0 = 0u | 0x0001u;
                    m.WriteU32((c.S0 + 0x4u), c.V0);
                    c.RA = 0x80034838u;
                    CrashBandicoot2.func_80054BD0(c, m);
            """;

        const string replacement =
            """
                    c.V0 = m.ReadU32((c.At + 0x668u));
                    // SCES-00967: pad-mode jump table @ 0x80010668 → mid-function cases
                    switch (c.V0)
                    {
                        case 0x80034818u: goto L80034818;
                        case 0x80034840u: goto L80034840;
                        case 0x8003485Cu: goto L8003485C;
                        case 0x80034874u: goto L80034874;
                        case 0x80034890u: goto L80034890;
                        default:
                            Dispatcher.Call(c, m, c.V0);
                            return;
                    }
                    L80034818: ;
                    c.A0 = (uint)(short)m.ReadU16((c.GP + 0x268u));
                    c.A1 = (uint)(short)m.ReadU16(c.S0);
                    c.A2 = 0u | 0x0001u;
                    c.A3 = c.A3 << 16;
                    c.A3 = (uint)((int)c.A3 >> 16);
                    c.V0 = 0u | 0x0001u;
                    m.WriteU32((c.S0 + 0x4u), c.V0);
                    c.RA = 0x80034838u;
                    CrashBandicoot2.func_80054BD0(c, m);
            """;

        if (!src.Contains(needle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: pad-mode jump table pattern not found");
            return src;
        }

        src = src.Replace(needle, replacement, StringComparison.Ordinal);

        src = ReplaceOnce(src,
            """
                    goto L800348D4;
                    c.A0 = (uint)(short)m.ReadU16((c.GP + 0x268u));
                    c.A1 = (uint)(short)m.ReadU16(c.S0);
                    c.V0 = 0u | 0x0002u;
                    m.WriteU32((c.S0 + 0x4u), c.V0);
                    c.RA = 0x80034854u;
                    CrashBandicoot2.func_8005659C(c, m);
            """,
            """
                    goto L800348D4;
                    L80034840: ;
                    c.A0 = (uint)(short)m.ReadU16((c.GP + 0x268u));
                    c.A1 = (uint)(short)m.ReadU16(c.S0);
                    c.V0 = 0u | 0x0002u;
                    m.WriteU32((c.S0 + 0x4u), c.V0);
                    c.RA = 0x80034854u;
                    CrashBandicoot2.func_8005659C(c, m);
            """);

        src = ReplaceOnce(src,
            """
                    goto L800348D4;
                    c.A0 = (uint)(short)m.ReadU16((c.GP + 0x268u));
                    c.A1 = (uint)(short)m.ReadU16(c.S0);
                    m.WriteU32((c.S0 + 0x4u), 0u);
                    c.RA = 0x8003486Cu;
                    CrashBandicoot2.func_80054B6C(c, m);
            """,
            """
                    goto L800348D4;
                    L8003485C: ;
                    c.A0 = (uint)(short)m.ReadU16((c.GP + 0x268u));
                    c.A1 = (uint)(short)m.ReadU16(c.S0);
                    m.WriteU32((c.S0 + 0x4u), 0u);
                    c.RA = 0x8003486Cu;
                    CrashBandicoot2.func_80054B6C(c, m);
            """);

        src = ReplaceOnce(src,
            """
                    goto L800348D4;
                    c.A0 = (uint)(short)m.ReadU16((c.GP + 0x268u));
                    c.A1 = (uint)(short)m.ReadU16(c.S0);
                    c.V0 = 0u | 0x0001u;
                    m.WriteU32((c.S0 + 0x4u), c.V0);
                    c.RA = 0x80034888u;
                    CrashBandicoot2.func_80054CCC(c, m);
            """,
            """
                    goto L800348D4;
                    L80034874: ;
                    c.A0 = (uint)(short)m.ReadU16((c.GP + 0x268u));
                    c.A1 = (uint)(short)m.ReadU16(c.S0);
                    c.V0 = 0u | 0x0001u;
                    m.WriteU32((c.S0 + 0x4u), c.V0);
                    c.RA = 0x80034888u;
                    CrashBandicoot2.func_80054CCC(c, m);
            """);

        src = ReplaceOnce(src,
            """
                    goto L800348D4;
                    c.V1 = (uint)(short)m.ReadU16((c.GP + 0x17Cu));
                    c.S1 = 0u + 0u;
                    c.V0 = (uint)((int)c.A3 >> 16);
                    m.WriteU16((c.S0 + 0x10u), (ushort)c.V0);
            """,
            """
                    goto L800348D4;
                    L80034890: ;
                    c.V1 = (uint)(short)m.ReadU16((c.GP + 0x17Cu));
                    c.S1 = 0u + 0u;
                    c.V0 = (uint)((int)c.A3 >> 16);
                    m.WriteU16((c.S0 + 0x10u), (ushort)c.V0);
            """);

        Console.WriteLine("[post-pass] applied pad-mode jump table fix (func_800347D4)");
        return src;
    }

    /// <summary>
    /// Per-level audio/id jump table @ 0x800105D0 inside func_80034034 (0x26 entries,
    /// 4 unique mid-function targets). Hit when leaving title into Intro (0x1C).
    /// </summary>
    static string FixLevelAudioJumpTable(string src)
    {
        if (src.Contains("L800340B8:", StringComparison.Ordinal))
            return src;

        const string needle =
            """
                    c.V0 = m.ReadU32((c.At + 0x5D0u));
                    Dispatcher.Call(c, m, c.V0);
                    return;
                    c.V0 = 0u | 0x0012u;
                    goto L800340D4;
                    c.V0 = 0u | 0x0011u;
                    goto L800340D4;
                    c.V0 = 0u | 0x000Cu;
                    goto L800340D4;
                    L800340D0: ;
                    c.V0 = 0u | 0x0010u;
            """;

        const string replacement =
            """
                    c.V0 = m.ReadU32((c.At + 0x5D0u));
                    // SCES-00967: level-audio jump table @ 0x800105D0 → mid-function cases
                    switch (c.V0)
                    {
                        case 0x800340B8u: goto L800340B8;
                        case 0x800340C0u: goto L800340C0;
                        case 0x800340C8u: goto L800340C8;
                        case 0x800340D0u: goto L800340D0;
                        default:
                            Dispatcher.Call(c, m, c.V0);
                            return;
                    }
                    L800340B8: ;
                    c.V0 = 0u | 0x0012u;
                    goto L800340D4;
                    L800340C0: ;
                    c.V0 = 0u | 0x0011u;
                    goto L800340D4;
                    L800340C8: ;
                    c.V0 = 0u | 0x000Cu;
                    goto L800340D4;
                    L800340D0: ;
                    c.V0 = 0u | 0x0010u;
            """;

        if (!src.Contains(needle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: level-audio jump table pattern not found");
            return src;
        }

        src = src.Replace(needle, replacement, StringComparison.Ordinal);
        Console.WriteLine("[post-pass] applied level-audio jump table fix (func_80034034)");
        return src;
    }

    /// <summary>
    /// Texture decompressors func_8003B0EC / func_8003B4E4 share a Duff-device bulk-copy
    /// tail @ 0x8003B910. Cross-function branches become Dispatcher.Call+return; the
    /// unrolled jr targets are empty stubs. Replace with a plain halfword copy + continue.
    /// </summary>
    static string FixTexDecompBulkCopy(string src)
    {
        if (src.Contains("Shared bulk-copy tail @ 0x8003B910", StringComparison.Ordinal) ||
            src.Contains("Bypass broken Duff-device tail @ L8003B910", StringComparison.Ordinal))
            return src;

        const string needleB0 =
            """
                    c.A3 = (int)c.At < 2 ? 1u : 0u;
                    if (c.A3 == 0u) {
                        Dispatcher.Call(c, m, 0x8003B910u);
                        return;
                    }
            """;

        const string fixB0 =
            """
                    c.A3 = (int)c.At < 2 ? 1u : 0u;
                    if (c.A3 == 0u) {
                        // Shared bulk-copy tail @ 0x8003B910 lives in func_8003B4E4; branch was
                        // emitted as Dispatcher.Call+return (wrong). Inline halfword copy + continue.
                        c.V0 = c.V0 + c.At;
                        c.V1 = c.V1 + c.At;
                        {
                            uint n = c.At;
                            while (n != 0u)
                            {
                                ushort h = m.ReadU16(c.T6);
                                c.T6 = c.T6 + 0x2u;
                                m.WriteU16(c.S1, h);
                                c.S1 = c.S1 + 0x2u;
                                n--;
                            }
                        }
                        goto L8003B38C;
                    }
            """;

        const string needleB4 =
            """
                    c.A3 = (int)c.At < 2 ? 1u : 0u;
                    if (c.A3 == 0u) {
                        goto L8003B910;
                    }
            """;

        const string fixB4 =
            """
                    c.A3 = (int)c.At < 2 ? 1u : 0u;
                    if (c.A3 == 0u) {
                        // Bypass broken Duff-device tail @ L8003B910 (jr into mid-function + empty stubs).
                        c.V0 = c.V0 + c.At;
                        c.V1 = c.V1 + c.At;
                        {
                            uint n = c.At;
                            while (n != 0u)
                            {
                                ushort h = m.ReadU16(c.T6);
                                c.T6 = c.T6 + 0x2u;
                                m.WriteU16(c.S1, h);
                                c.S1 = c.S1 + 0x2u;
                                n--;
                            }
                        }
                        goto L8003B710;
                    }
            """;

        bool any = false;
        if (src.Contains(needleB0, StringComparison.Ordinal))
        {
            src = src.Replace(needleB0, fixB0, StringComparison.Ordinal);
            any = true;
        }
        else
            Console.WriteLine("[post-pass] warning: tex-decomp B0EC bulk-copy pattern not found");

        if (src.Contains(needleB4, StringComparison.Ordinal))
        {
            src = src.Replace(needleB4, fixB4, StringComparison.Ordinal);
            any = true;
        }
        else
            Console.WriteLine("[post-pass] warning: tex-decomp B4E4 bulk-copy pattern not found");

        if (any)
            Console.WriteLine("[post-pass] applied tex-decomp bulk-copy fix (func_8003B0EC/B4E4)");
        return src;
    }

    /// <summary>
    /// Poly rasterizer <c>func_800420F4</c> was split: mode fragments (42D50, 43E0C, …)
    /// do MIPS <c>j</c> into mid-labels (42938 / 42AB0 / 426B8 / 427E0 / …). Those were
    /// emitted as <c>Dispatcher.Call</c>+return → unmapped. Convert fragment jumps into
    /// <see cref="RecompOne.Runtime.Dispatch.RasterContinue"/> tokens and catch them at
    /// the parent's jalr sites so control resumes via goto (flat stack).
    /// </summary>
    static string FixPolyRasterContinuations(string src)
    {
        if (src.Contains("// SCES-00967: poly-raster continuations", StringComparison.Ordinal))
            return src;

        // Generated main.cs may be CRLF on Windows; normalize so raw-string needles match.
        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);

        // Mid-entry labels inside func_800420F4 (were fallthrough after jalr/jr).
        // Marker must stay — used as idempotency guard.
        src = src.Replace(
            "    public static void func_800420F4(CpuContext c, IMemory m)\n    {",
            "    public static void func_800420F4(CpuContext c, IMemory m)\n    {\n        // SCES-00967: poly-raster continuations",
            StringComparison.Ordinal);

        src = ReplaceOnce(src,
            """
                    c.SP = c.SP + 0x44u;
                    return;
                    RecompOne.Runtime.Gte.Execute(0x4A280030u);
            """,
            """
                    c.SP = c.SP + 0x44u;
                    return;
                    L800426B8: ;
                    RecompOne.Runtime.Gte.Execute(0x4A280030u);
            """);

        src = ReplaceOnce(src,
            """
                    L800427D8: ;
                    c.V0 = c.V0 + c.FP;
                    Dispatcher.Call(c, m, c.A2);
                    return;
                    c.T8 = c.S5 << 20;
            """,
            """
                    L800427D8: ;
                    c.V0 = c.V0 + c.FP;
                    Dispatcher.Call(c, m, c.A2);
                    {
                        uint _rc = RecompOne.Runtime.Dispatch.RasterContinue.Take();
                        if (_rc != 0u)
                        {
                            switch (_rc)
                            {
                                case 0x80042628u: goto L80042628;
                                case 0x800426B8u: goto L800426B8;
                                case 0x800427E0u: goto L800427E0;
                                case 0x80042938u: goto L80042938;
                                case 0x80042AB0u: goto L80042AB0;
                                case 0x80042BE0u: goto L80042BE0;
                                default: throw new InvalidOperationException($"unhandled raster continue: 0x{_rc:X8}");
                            }
                        }
                    }
                    return;
                    L800427E0: ;
                    c.T8 = c.S5 << 20;
            """);

        src = ReplaceOnce(src,
            """
                    if ((int)0u >= 0) {
                        c.T9 = 0x09000000u;
                        goto L80042BE0;
                    }
                    c.T9 = 0x09000000u;
                    m.WriteU32((c.V1 + 0x1B4u), RecompOne.Runtime.Gte.StoreWord(0));
            """,
            """
                    if ((int)0u >= 0) {
                        c.T9 = 0x09000000u;
                        goto L80042BE0;
                    }
                    L80042938: ;
                    c.T9 = 0x09000000u;
                    m.WriteU32((c.V1 + 0x1B4u), RecompOne.Runtime.Gte.StoreWord(0));
            """);

        src = ReplaceOnce(src,
            """
                    L80042AA8: ;
                    c.V0 = c.V0 + c.FP;
                    Dispatcher.Call(c, m, c.A2);
                    return;
                    c.T8 = c.S5 << 20;
            """,
            """
                    L80042AA8: ;
                    c.V0 = c.V0 + c.FP;
                    Dispatcher.Call(c, m, c.A2);
                    {
                        uint _rc = RecompOne.Runtime.Dispatch.RasterContinue.Take();
                        if (_rc != 0u)
                        {
                            switch (_rc)
                            {
                                case 0x80042628u: goto L80042628;
                                case 0x800426B8u: goto L800426B8;
                                case 0x800427E0u: goto L800427E0;
                                case 0x80042938u: goto L80042938;
                                case 0x80042AB0u: goto L80042AB0;
                                case 0x80042BE0u: goto L80042BE0;
                                default: throw new InvalidOperationException($"unhandled raster continue: 0x{_rc:X8}");
                            }
                        }
                    }
                    return;
                    L80042AB0: ;
                    c.T8 = c.S5 << 20;
            """);

        // Wrap jalr-via-T9 exits in func_800420F4 (four sites, each followed by a known label).
        // Raw-string indent: content columns 20, closer at 12 → 8 spaces (matches main.cs).
        var t9Sites = new (string Label, string Needle)[]
        {
            ("L800423E4",
            """
                    Dispatcher.Call(c, m, c.T9);
                    return;
                    L800423E4: ;
            """),
            ("L80042428",
            """
                    Dispatcher.Call(c, m, c.T9);
                    return;
                    L80042428: ;
            """),
            ("L8004254C",
            """
                    Dispatcher.Call(c, m, c.T9);
                    return;
                    L8004254C: ;
            """),
            ("L80042628",
            """
                    Dispatcher.Call(c, m, c.T9);
                    return;
                    L80042628: ;
            """),
        };

        const string t9Catch =
            """
                    Dispatcher.Call(c, m, c.T9);
                    {
                        uint _rc = RecompOne.Runtime.Dispatch.RasterContinue.Take();
                        if (_rc != 0u)
                        {
                            switch (_rc)
                            {
                                case 0x80042628u: goto L80042628;
                                case 0x800426B8u: goto L800426B8;
                                case 0x800427E0u: goto L800427E0;
                                case 0x80042938u: goto L80042938;
                                case 0x80042AB0u: goto L80042AB0;
                                case 0x80042BE0u: goto L80042BE0;
                                default: throw new InvalidOperationException($"unhandled raster continue: 0x{_rc:X8}");
                            }
                        }
                    }
                    return;
            """;

        int t9Count = 0;
        foreach (var (label, needle) in t9Sites)
        {
            var fix = t9Catch + $"        {label}: ;\n";
            if (!src.Contains(needle, StringComparison.Ordinal))
            {
                Console.WriteLine($"[post-pass] warning: raster T9 jalr site before {label} not found");
                continue;
            }
            src = src.Replace(needle, fix, StringComparison.Ordinal);
            t9Count++;
        }

        // Fragment j-targets → RasterContinue.Jump (same pattern everywhere).
        foreach (var addr in new[]
                 {
                     "0x80042628u", "0x800426B8u", "0x800427E0u",
                     "0x80042938u", "0x80042AB0u", "0x80042BE0u",
                 })
        {
            var callNeedle = $"Dispatcher.Call(c, m, {addr});";
            var jumpFix = $"RecompOne.Runtime.Dispatch.RasterContinue.Jump({addr});";
            if (src.Contains(callNeedle, StringComparison.Ordinal))
                src = src.Replace(callNeedle, jumpFix, StringComparison.Ordinal);
        }

        // jalr/indirect Call to mid-labels must resolve in the overlay map: stubs only
        // set the continue token so the parent's Take() can goto the real label.
        if (!src.Contains("[0x80042938u]", StringComparison.Ordinal))
        {
            const string mapNeedle = "[0x800420F4u] = CrashBandicoot2.func_800420F4,";
            const string mapFix =
                """
                [0x800420F4u] = CrashBandicoot2.func_800420F4,
                            [0x80042628u] = static (c, m) => RecompOne.Runtime.Dispatch.RasterContinue.Jump(0x80042628u),
                            [0x800426B8u] = static (c, m) => RecompOne.Runtime.Dispatch.RasterContinue.Jump(0x800426B8u),
                            [0x800427E0u] = static (c, m) => RecompOne.Runtime.Dispatch.RasterContinue.Jump(0x800427E0u),
                            [0x80042938u] = static (c, m) => RecompOne.Runtime.Dispatch.RasterContinue.Jump(0x80042938u),
                            [0x80042AB0u] = static (c, m) => RecompOne.Runtime.Dispatch.RasterContinue.Jump(0x80042AB0u),
                            [0x80042BE0u] = static (c, m) => RecompOne.Runtime.Dispatch.RasterContinue.Jump(0x80042BE0u),
                """;
            if (!src.Contains(mapNeedle, StringComparison.Ordinal))
                Console.WriteLine("[post-pass] warning: func_800420F4 dispatcher map entry not found");
            else
                src = src.Replace(mapNeedle, mapFix, StringComparison.Ordinal);
        }

        Console.WriteLine($"[post-pass] applied poly-raster continuation fix (func_800420F4 mid-entries; {t9Count} T9 jalr sites)");
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
