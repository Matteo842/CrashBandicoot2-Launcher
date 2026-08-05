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
        patched = FixIntroSeqJumpTable(patched);
        patched = FixCamInterpJumpTable(patched);
        patched = FixPadModeJumpTable(patched);
        patched = FixLevelAudioJumpTable(patched);
        patched = FixObjectPropertyJumpTable(patched);
        patched = FixResourceInterpolationJumpTable(patched);
        patched = FixGpRelativeMemToGpBase(patched);
        patched = FixGoolAnimJumpTable918(patched);
        patched = FixGoolTransJumpTable938(patched);
        patched = FixGoolTransformPointerGuard(patched);
        patched = FixGoolPathJumpTable8F8(patched);
        patched = FixTexDecompBulkCopy(patched);
        patched = FixTexDecompUseInterpreter(patched);
        patched = FixPolyRasterContinuations(patched);
        patched = FixGoolFallthrough38D94(patched);
        patched = FixGoolFallthroughMidEntries(patched);
        patched = FixGool38FA0Entry(patched);
        patched = FixGoolNativeCdahS0(patched);
        patched = FixGoolNativeClearOTag(patched);
        patched = FixGoolTableRestoreAfter1A040(patched);
        patched = FixGoolTableBefore1C3D4Reentry(patched);
        patched = StripGoolNativeCallReloadPc(patched);
        patched = FixGoolInterpreterFpGuard(patched);
        patched = FixMeshTransformMidEntries(patched);
        patched = FixClipTestEpilogue420B0(patched);
        patched = FixGteLoadMidEntry43310(patched);
        patched = FixMatrixMidEntry44324(patched);
        patched = FixMainLoopCalleeSavedGuard(patched);
        patched = FixObjectTraversalPointerGuard(patched);
        patched = FixGoolObjectPointerGuard(patched);
        patched = FixGoolInterpreterStateGuard(patched);
        patched = FixIntroAssetPollYield(patched);
        patched = FixGoolZeroOpcodeHandler(patched);
        patched = FixGoolIndirectPointerGuard(patched);
        patched = FixObjectHandlerStablePointer(patched);
        patched = FixObjectHandlerClassTypePointerGuard(patched);
        patched = FixObjectOptionalE8PointerGuard(patched);
        patched = FixObjectClassTypePointerGuard(patched);
        patched = FixResourceLookupPointerGuard(patched);
        patched = FixLevelColorPointerGuard(patched);
        patched = FixLevelTableProcessGuard(patched);
        patched = FixLevelAudioTablePointerGuard(patched);
        patched = FixIntroOptionalPacketPointerGuard(patched);
        patched = FixRecursiveWalkerStableArgs(patched);
        patched = FixRecursiveWalkerChildPointerGuard(patched);
        patched = FixOuterWalkerPointerGuards(patched);
        patched = FixObjectTraversalLoopState(patched);
        patched = FixIntroModeReloadGuard(patched);
        // Remaining matrix mid-entries (44514/445CC) still rely on MIPS interp until mapped.
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
    /// Log PsyQ ClearOTag(ot, n) args — Intro's empty OT (nodes=2) may be a bad depth.
    /// </summary>
    static string FixGoolNativeClearOTag(string src)
    {
        if (src.Contains("ClearOTag diag", StringComparison.Ordinal))
            return src;

        const string n2 =
            "    public static void func_8004C72C(CpuContext c, IMemory m)\n    {\n        c.V0 = 0x80060000u;";
        const string r2 =
            "    public static void func_8004C72C(CpuContext c, IMemory m)\n    {\n        // ClearOTag diag\n        RecompOne.Runtime.Diagnostics.BootLog.Write($\"ClearOTag ot=0x{c.A0:X8} n=0x{c.A1:X8}\");\n        c.V0 = 0x80060000u;";
        if (!src.Contains(n2, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: ClearOTag diag inject pattern not found");
            return src;
        }

        src = src.Replace(n2, r2, StringComparison.Ordinal);
        Console.WriteLine("[post-pass] injected ClearOTag arg logging");
        return src;
    }

    /// <summary>
    /// func_8001A040 leaves scratchpad+0x5C = 0x1F800060 (matrix scratch). Retail
    /// restores the GOOL opcode table (0x80060000-0x3854) at the end of func_8001C3D4,
    /// but that is too late: the func_80018F0C path can re-enter GOOL first and then
    /// dispatch through scratchpad garbage (unmapped call 0x800DCF48 on Intro).
    /// </summary>
    static string FixGoolTableRestoreAfter1A040(string src)
    {
        if (src.Contains("Restore the GOOL opcode table BEFORE any path", StringComparison.Ordinal))
            return src;

        const string needle =
            """
                    c.RA = 0x8001C428u;
                    CrashBandicoot2.func_8001A040(c, m);
                    c.V0 = 0x80060000u;
                    c.V0 = m.ReadU32((c.V0 - 0x9DCu));
                    if (c.V0 == 0u) {
                        goto L8001C454;
                    }
            """;

        const string replacement =
            """
                    c.RA = 0x8001C428u;
                    CrashBandicoot2.func_8001A040(c, m);
                    // func_8001A040 leaves scratchpad+0x5C as 0x1F800060 (matrix scratch).
                    // Restore the GOOL opcode table BEFORE any path that can re-enter GOOL
                    // (func_80018F0C → … → func_8003A2AC), otherwise insn dispatch reads
                    // handlers from scratchpad and jumps to garbage (seen: 0x800DCF48).
                    c.V0 = 0x80060000u;
                    c.V0 = c.V0 - 0x3854u;
                    c.At = 0x1F800000u;
                    m.WriteU32((c.At + 0x5Cu), c.V0);
                    c.V0 = 0x80060000u;
                    c.V0 = m.ReadU32((c.V0 - 0x9DCu));
                    if (c.V0 == 0u) {
                        goto L8001C454;
                    }
            """;

        if (!src.Contains(needle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: GOOL table restore after 1A040 pattern not found");
            return src;
        }

        src = src.Replace(needle, replacement, StringComparison.Ordinal);
        Console.WriteLine("[post-pass] applied GOOL table restore after func_8001A040");
        return src;
    }

    /// <summary>
    /// Undo a mistaken post-pass: after Call(S5) at L8003AD6C the retail MIPS does
    /// <c>bne s5, zero, L8003A304</c> with no C0 reload. NSF natives return the next
    /// GOOL PC in S5 (via jalr s5,ra). Reloading obj+0xC0 resumes at the native entry
    /// (C0 was advanced into the blob at fetch) and treats MIPS as bytecode.
    /// </summary>
    static string StripGoolNativeCallReloadPc(string src)
    {
        // Comment was split across lines; match any fragment.
        if (!src.Contains("S5 held the NSF native VA for the call", StringComparison.Ordinal)
            && !src.Contains("PC saved at fetch time (obj+0xC0)", StringComparison.Ordinal))
            return src;

        var nl = src.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = src.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var outLines = new List<string>(lines.Length);
        bool stripReload = false;
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Contains("S5 held the NSF native VA for the call", StringComparison.Ordinal)
                || line.Contains("S-reg restore leave it unsuitable", StringComparison.Ordinal)
                || line.Contains("PC saved at fetch time (obj+0xC0)", StringComparison.Ordinal)
                || (line.Contains("Reload the", StringComparison.Ordinal)
                    && line.Contains("GOOL bytecode PC", StringComparison.Ordinal)))
            {
                stripReload = true;
                continue;
            }

            if (stripReload && line.Contains("c.S5 = m.ReadU32((c.S0 + 0xC0u));", StringComparison.Ordinal))
            {
                stripReload = false;
                continue;
            }

            outLines.Add(line);
        }

        src = string.Join(nl, outLines);
        Console.WriteLine("[post-pass] stripped mistaken GOOL native Call(S5) C0 reload");
        return src;
    }

    /// <summary>
    /// GOOL opcode fetch uses <c>*(FP+0x5C)</c> as the handler table base. Retail keeps
    /// <c>$fp = 0x1F800000</c> for the interpreter; mid-entry MIPS interp and helpers may
    /// clobber FP. Without this guard, dispatch reads handlers from an object and can
    /// <c>Call(0)</c> (seen after CdahS anim setup on Intro).
    /// </summary>
    static string FixGoolInterpreterFpGuard(string src)
    {
        if (src.Contains("Scratchpad $fp must stay 0x1F800000", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string needle =
            "        L8003A304: ;\n" +
            "        c.A1 = m.ReadU32(c.S5);\n" +
            "        c.S5 = c.S5 + 0x4u;\n" +
            "        m.WriteU32((c.S0 + 0xC0u), c.S5);\n" +
            "        c.A0 = m.ReadU32((c.FP + 0x5Cu));";
        const string replacement =
            "        L8003A304: ;\n" +
            "        // Scratchpad $fp must stay 0x1F800000 for opcode-table fetch at +0x5C.\n" +
            "        // Mid-entry MIPS interp / helpers may clobber FP before we resume.\n" +
            "        c.FP = 0x1F800000u;\n" +
            "        c.A1 = m.ReadU32(c.S5);\n" +
            "        c.S5 = c.S5 + 0x4u;\n" +
            "        m.WriteU32((c.S0 + 0xC0u), c.S5);\n" +
            "        c.A0 = m.ReadU32((c.FP + 0x5Cu));";
        if (!src.Contains(needle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: GOOL FP guard pattern not found");
            return src;
        }

        src = src.Replace(needle, replacement, StringComparison.Ordinal);
        Console.WriteLine("[post-pass] applied GOOL interpreter FP scratchpad guard");
        return src;
    }

    /// <summary>
    /// GOOL anim/transform jump table @ 0x80010918 inside func_80038998 (and copies
    /// embedded in func_80037930 / func_80038414). Recompiler emits Call+return, so
    /// mid-entries 0x80038A08 / 38B14 / 38C24 / 38D70 stay dead; MIPS interp then runs
    /// them without the shared C# epilogue path. Convert to switch/goto like other JT fixes.
    /// </summary>
    static string FixGoolAnimJumpTable918(string src)
    {
        if (src.Contains("GOOL anim jump table @ 0x80010918", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);

        const string needle =
            "        c.V0 = m.ReadU32((c.At + 0x918u));\n" +
            "        Dispatcher.Call(c, m, c.V0);\n" +
            "        return;\n" +
            "        c.V0 = m.ReadU32((c.S0 + 0x60u));";

        const string replacement =
            "        c.V0 = m.ReadU32((c.At + 0x918u));\n" +
            "        // SCES-00967: GOOL anim jump table @ 0x80010918 → mid-function cases\n" +
            "        switch (c.V0)\n" +
            "        {\n" +
            "            case 0x80038A08u: goto L80038A08;\n" +
            "            case 0x80038B14u: goto L80038B14;\n" +
            "            case 0x80038C24u: goto L80038C24;\n" +
            "            case 0x80038D70u: goto L80038D70;\n" +
            "            default:\n" +
            "                Dispatcher.Call(c, m, c.V0);\n" +
            "                return;\n" +
            "        }\n" +
            "        L80038A08: ;\n" +
            "        c.V0 = m.ReadU32((c.S0 + 0x60u));";

        if (!src.Contains(needle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: GOOL anim jump table @ 0x918 pattern not found");
            return src;
        }

        src = src.Replace(needle, replacement, StringComparison.Ordinal);

        // Case 1 @ 0x80038B14 — after case 0's goto epilogue.
        src = src.Replace(
            "        goto L80038D78;\n" +
            "        c.A2 = 0x80060000u;\n" +
            "        c.A2 = m.ReadU32((c.A2 - 0x9DCu));",
            "        goto L80038D78;\n" +
            "        L80038B14: ;\n" +
            "        c.A2 = 0x80060000u;\n" +
            "        c.A2 = m.ReadU32((c.A2 - 0x9DCu));",
            StringComparison.Ordinal);

        // Cases 2–5 @ 0x80038C24.
        src = src.Replace(
            "        goto L80038D78;\n" +
            "        c.V0 = c.S1 >> 18;\n" +
            "        c.V1 = c.V0 & 0x0007u;\n" +
            "        c.A3 = c.V1 - 0x4u;",
            "        goto L80038D78;\n" +
            "        L80038C24: ;\n" +
            "        c.V0 = c.S1 >> 18;\n" +
            "        c.V1 = c.V0 & 0x0007u;\n" +
            "        c.A3 = c.V1 - 0x4u;",
            StringComparison.Ordinal);

        // Case 6 @ 0x80038D70.
        src = src.Replace(
            "        goto L80038D78;\n" +
            "        c.A0 = c.S0 + 0u;\n" +
            "        c.RA = 0x80038D78u;\n" +
            "        CrashBandicoot2.func_80029728(c, m);",
            "        goto L80038D78;\n" +
            "        L80038D70: ;\n" +
            "        c.A0 = c.S0 + 0u;\n" +
            "        c.RA = 0x80038D78u;\n" +
            "        CrashBandicoot2.func_80029728(c, m);",
            StringComparison.Ordinal);

        Console.WriteLine("[post-pass] applied GOOL anim jump table fix (@ 0x80010918)");
        return src;
    }

    /// <summary>
    /// GOOL path/transform helper jump table @ 0x800108F8 inside func_80038414 /
    /// func_80037930. Case 0 (0x800384D0) calls pathFollow (func_8001F29C); without this
    /// fix Call+return leaves every case dead and Intro path progress (obj+F4) never moves.
    /// </summary>
    static string FixGoolPathJumpTable8F8(string src)
    {
        if (src.Contains("GOOL path jump table @ 0x800108F8", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);

        const string needle =
            "        c.V0 = m.ReadU32((c.At + 0x8F8u));\n" +
            "        Dispatcher.Call(c, m, c.V0);\n" +
            "        return;\n" +
            "        if (c.S1 == 0u) {\n";

        const string replacement =
            "        c.V0 = m.ReadU32((c.At + 0x8F8u));\n" +
            "        // SCES-00967: GOOL path jump table @ 0x800108F8 → mid-function cases\n" +
            "        switch (c.V0)\n" +
            "        {\n" +
            "            case 0x800384D0u: goto L800384D0;\n" +
            "            case 0x80038828u: goto L80038828;\n" +
            "            case 0x80038534u: goto L80038534;\n" +
            "            case 0x80038660u: goto L80038660;\n" +
            "            case 0x800385C8u: goto L800385C8;\n" +
            "            case 0x80038950u: goto L80038950;\n" +
            "            default:\n" +
            "                Dispatcher.Call(c, m, c.V0);\n" +
            "                return;\n" +
            "        }\n" +
            "        L800384D0: ;\n" +
            "        if (c.S1 == 0u) {\n";

        if (!src.Contains(needle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: GOOL path jump table @ 0x8F8 pattern not found");
            return src;
        }

        src = src.Replace(needle, replacement, StringComparison.Ordinal);

        // Case 2 @ 0x80038534 — after case 0's goto epilogue.
        src = src.Replace(
            "        goto L80038978;\n" +
            "        c.A0 = m.ReadU32((c.S0 + 0x94u));\n" +
            "        c.A0 = c.A0 & 0x0FFFu;\n" +
            "        c.RA = 0x80038540u;\n",
            "        goto L80038978;\n" +
            "        L80038534: ;\n" +
            "        c.A0 = m.ReadU32((c.S0 + 0x94u));\n" +
            "        c.A0 = c.A0 & 0x0FFFu;\n" +
            "        c.RA = 0x80038540u;\n",
            StringComparison.Ordinal);

        // Cases 4/5 @ 0x800385C8.
        src = src.Replace(
            "        goto L80038978;\n" +
            "        c.V0 = m.ReadU32(c.S1);\n" +
            "        m.WriteU32((c.SP + 0x30u), c.V0);\n",
            "        goto L80038978;\n" +
            "        L800385C8: ;\n" +
            "        c.V0 = m.ReadU32(c.S1);\n" +
            "        m.WriteU32((c.SP + 0x30u), c.V0);\n",
            StringComparison.Ordinal);

        // Cases 3/6 @ 0x80038660.
        src = src.Replace(
            "        goto L80038978;\n" +
            "        c.V0 = c.S2 >> 19;\n" +
            "        c.V0 = c.V0 & 0x001Cu;\n",
            "        goto L80038978;\n" +
            "        L80038660: ;\n" +
            "        c.V0 = c.S2 >> 19;\n" +
            "        c.V0 = c.V0 & 0x001Cu;\n",
            StringComparison.Ordinal);

        // Case 1 @ 0x80038828.
        src = src.Replace(
            "        goto L80038978;\n" +
            "        c.A0 = c.S3 + 0u;\n" +
            "        c.V1 = c.S2 >> 15;\n" +
            "        c.V1 = c.V1 & 0x0007u;\n",
            "        goto L80038978;\n" +
            "        L80038828: ;\n" +
            "        c.A0 = c.S3 + 0u;\n" +
            "        c.V1 = c.S2 >> 15;\n" +
            "        c.V1 = c.V1 & 0x0007u;\n",
            StringComparison.Ordinal);

        // Case 7 @ 0x80038950.
        src = src.Replace(
            "        goto L80038978;\n" +
            "        c.A0 = c.S3 + 0u;\n" +
            "        c.V0 = c.S2 >> 15;\n" +
            "        c.V0 = c.V0 & 0x0007u;\n",
            "        goto L80038978;\n" +
            "        L80038950: ;\n" +
            "        c.A0 = c.S3 + 0u;\n" +
            "        c.V0 = c.S2 >> 15;\n" +
            "        c.V0 = c.V0 & 0x0007u;\n",
            StringComparison.Ordinal);

        Console.WriteLine("[post-pass] applied GOOL path jump table fix (@ 0x800108F8)");
        return src;
    }

    /// <summary>
    /// GOOL translate/op helper jump table @ 0x80010938 inside func_80039198 (and copies).
    /// Retail uses <c>jr v0</c> into mid-function cases that share the epilogue at
    /// L8003928C. Call+return skips SP/S0/S1 restore and the next GOOL helper call
    /// then dies on a garbage pointer from func_800370C8.
    /// </summary>
    static string FixGoolTransJumpTable938(string src)
    {
        if (src.Contains("GOOL trans jump table @ 0x80010938", StringComparison.Ordinal)
            && !src.Contains("Dispatcher.Call(c, m, c.V0);\n        return;\n        c.A1 = 0u + 0u;\n        goto L8003920C;", StringComparison.Ordinal)
            && !src.Contains("Dispatcher.Call(c, m, c.V0);\r\n        return;\r\n        c.A1 = 0u + 0u;\r\n        goto L8003920C;", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);

        const string needle =
            "        c.V0 = m.ReadU32((c.At + 0x938u));\n" +
            "        Dispatcher.Call(c, m, c.V0);\n" +
            "        return;\n" +
            "        c.A1 = 0u + 0u;\n" +
            "        goto L8003920C;\n" +
            "        c.A1 = 0u | 0x0001u;\n" +
            "        L8003920C: ;";

        const string replacement =
            "        c.V0 = m.ReadU32((c.At + 0x938u));\n" +
            "        // SCES-00967: GOOL trans jump table @ 0x80010938 -> mid-function cases\n" +
            "        switch (c.V0)\n" +
            "        {\n" +
            "            case 0x80039200u: c.A1 = 0u; goto L8003920C;\n" +
            "            case 0x80039208u: c.A1 = 0x1u; goto L8003920C;\n" +
            "            case 0x8003921Cu: goto L8003921C;\n" +
            "            case 0x8003922Cu: goto L8003922C;\n" +
            "            case 0x8003923Cu: goto L8003923C;\n" +
            "            case 0x8003925Cu: goto L8003925C;\n" +
            "            default:\n" +
            "                Dispatcher.Call(c, m, c.V0);\n" +
            "                return;\n" +
            "        }\n" +
            "        L8003920C: ;";

        int n = 0;
        while (src.Contains(needle, StringComparison.Ordinal))
        {
            src = src.Replace(needle, replacement, StringComparison.Ordinal);
            n++;
        }
        if (n == 0)
        {
            Console.WriteLine("[post-pass] warning: GOOL trans jump table @ 0x938 pattern not found");
            return src;
        }

        const string casesNeedle =
            "        goto L8003928C;\n" +
            "        c.A1 = 0u | 0x0001u;\n" +
            "        c.RA = 0x80039224u;\n" +
            "        CrashBandicoot2.func_800144C4(c, m);\n" +
            "        m.WriteU32((c.S1 + 0xD4u), c.V0);\n" +
            "        goto L8003928C;\n" +
            "        c.A1 = 0u + 0u;\n" +
            "        c.RA = 0x80039234u;\n" +
            "        CrashBandicoot2.func_800144C4(c, m);\n" +
            "        goto L80039244;\n" +
            "        c.RA = 0x80039244u;\n" +
            "        CrashBandicoot2.func_80014670(c, m);\n" +
            "        L80039244: ;";

        const string casesReplacement =
            "        goto L8003928C;\n" +
            "        L8003921C: ;\n" +
            "        c.A1 = 0u | 0x0001u;\n" +
            "        c.RA = 0x80039224u;\n" +
            "        CrashBandicoot2.func_800144C4(c, m);\n" +
            "        m.WriteU32((c.S1 + 0xD4u), c.V0);\n" +
            "        goto L8003928C;\n" +
            "        L8003922C: ;\n" +
            "        c.A1 = 0u + 0u;\n" +
            "        c.RA = 0x80039234u;\n" +
            "        CrashBandicoot2.func_800144C4(c, m);\n" +
            "        goto L80039244;\n" +
            "        L8003923C: ;\n" +
            "        c.RA = 0x80039244u;\n" +
            "        CrashBandicoot2.func_80014670(c, m);\n" +
            "        L80039244: ;";

        while (src.Contains(casesNeedle, StringComparison.Ordinal))
            src = src.Replace(casesNeedle, casesReplacement, StringComparison.Ordinal);

        const string case5Needle =
            "        goto L8003928C;\n" +
            "        c.S0 = m.ReadU32(c.A0);\n" +
            "        c.V0 = m.ReadU32((c.S1 + 0xBCu));";
        const string case5Replacement =
            "        goto L8003928C;\n" +
            "        L8003925C: ;\n" +
            "        c.S0 = m.ReadU32(c.A0);\n" +
            "        c.V0 = m.ReadU32((c.S1 + 0xBCu));";
        // Avoid double-labeling already patched copies.
        int guard = 0;
        while (src.Contains(case5Needle, StringComparison.Ordinal) && guard++ < 16)
        {
            int at = src.IndexOf(case5Needle, StringComparison.Ordinal);
            string prev = src.Substring(Math.Max(0, at - 40), Math.Min(40, at));
            if (prev.Contains("L8003925C", StringComparison.Ordinal))
                break;
            src = src.Substring(0, at) + case5Replacement + src.Substring(at + case5Needle.Length);
        }

        Console.WriteLine($"[post-pass] applied GOOL trans jump table fix (@ 0x80010938) x{n}");
        return src;
    }

    /// <summary>
    /// Rewrite GP-relative <c>m.Read/Write*(c.GP + imm)</c> to use <c>c.GpBase</c>.
    /// Recompiled bodies reuse $gp as scratch; GpBase tracks the last sane PSYQ globals base.
    /// </summary>
    static string FixGpRelativeMemToGpBase(string src)
    {
        if (src.Contains("GP-relative mem → GpBase", StringComparison.Ordinal))
            return src;

        var fixedSrc = System.Text.RegularExpressions.Regex.Replace(
            src,
            @"(m\.\w+\(\(+)c\.GP \+ (0x[0-9A-Fa-f]+u)",
            "$1(c.GpBase + $2)");
        // Upgrade older absolute rewrites from prior post-pass experiments.
        fixedSrc = System.Text.RegularExpressions.Regex.Replace(
            fixedSrc,
            @"\(0x8005F414u \+ (0x[0-9A-Fa-f]+u)\)",
            "(c.GpBase + $1)");
        if (fixedSrc == src)
        {
            Console.WriteLine("[post-pass] warning: no GP-relative mem ops to rewrite");
            return src;
        }

        fixedSrc = fixedSrc.Replace(
            "namespace Recompiled;",
            "namespace Recompiled;\n// GP-relative mem → GpBase",
            StringComparison.Ordinal);
        Console.WriteLine("[post-pass] applied GP-relative mem → GpBase");
        return fixedSrc;
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
        // 0x80038FA0 is handled by FixGool38FA0Entry (separate function, not 37930 fallthrough).
        (string Addr, string Probe)[] entries =
        [
            ("80038414", "c.SP = c.SP - 0x88u;"),
            ("80038998", "c.SP = c.SP - 0x80u;"),
            ("80038EAC", "c.SP = c.SP - 0x20u;"),
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
    /// MSC opcode table dispatches to <c>0x80038FA0</c> (<c>addiu sp,-0x20</c>), but the
    /// recompiler often emits <c>func_80038FA4</c> starting at the next instruction and only
    /// maps +4. Unmapped 38FA0 falls into the NSF MIPS interpreter and breaks anim/path setup.
    /// </summary>
    static string FixGool38FA0Entry(string src)
    {
        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);

        if (!src.Contains("public static void func_80038FA4(", StringComparison.Ordinal))
            return src;

        var changed = false;

        if (!src.Contains("public static void func_80038FA0(", StringComparison.Ordinal))
        {
            const string broken =
                "    public static void func_80038FA4(CpuContext c, IMemory m)\n" +
                "    {\n" +
                "        m.WriteU32((c.SP + 0x10u), c.S0);";
            const string fixedPrologue =
                "    public static void func_80038FA0(CpuContext c, IMemory m) => func_80038FA4(c, m);\n" +
                "    public static void func_80038FA4(CpuContext c, IMemory m)\n" +
                "    {\n" +
                "        // SCES-00967: real entry is 0x80038FA0 (addiu sp,-0x20)\n" +
                "        c.SP = c.SP - 0x20u;\n" +
                "        m.WriteU32((c.SP + 0x10u), c.S0);";
            if (src.Contains(broken, StringComparison.Ordinal))
            {
                src = src.Replace(broken, fixedPrologue, StringComparison.Ordinal);
                changed = true;
            }
        }

        if (!src.Contains("[0x80038FA0u]", StringComparison.Ordinal) &&
            src.Contains("[0x80038FA4u] = CrashBandicoot2.func_80038FA4,", StringComparison.Ordinal))
        {
            src = src.Replace(
                "[0x80038FA4u] = CrashBandicoot2.func_80038FA4,",
                "[0x80038FA0u] = CrashBandicoot2.func_80038FA0,\n            [0x80038FA4u] = CrashBandicoot2.func_80038FA4,",
                StringComparison.Ordinal);
            changed = true;
        }

        if (changed)
            Console.WriteLine("[post-pass] applied GOOL MSC entry fix (0x80038FA0)");
        return src;
    }

    /// <summary>
    /// Intro draw dispatches into mid-entries of mesh/GTE transform <c>func_8003E170</c>
    /// (<c>0x8003E174</c> / <c>0x8003E5F8</c> / <c>0x8003E604</c> / <c>0x8003E784</c>).
    /// Unmapped Call falls into the MIPS interpreter; the second Intro frame times out
    /// at <c>0x8003E5F8</c> (MaxOps) and becomes <c>unmapped call</c>.
    /// </summary>
    static string FixMeshTransformMidEntries(string src)
    {
        if (src.Contains("func_8003E170_entry", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);

        // Alternate entry after the always-taken branch to L8003E4A0.
        if (!src.Contains("L8003E5F8:", StringComparison.Ordinal))
        {
            const string dead =
                "        if ((int)0u >= 0) {\n" +
                "            m.WriteU32((c.V1 + 0x7Cu), c.T9);\n" +
                "            goto L8003E4A0;\n" +
                "        }\n" +
                "        m.WriteU32((c.V1 + 0x7Cu), c.T9);\n" +
                "        m.WriteU32((c.V1 + 0x68u), c.RA);\n" +
                "        c.T8 = m.ReadU32((c.SP + 0x20u));\n" +
                "        c.RA = 0x8003E604u;\n" +
                "        CrashBandicoot2.func_80043960(c, m);\n" +
                "        L8003E604: ;";
            const string labeled =
                "        if ((int)0u >= 0) {\n" +
                "            m.WriteU32((c.V1 + 0x7Cu), c.T9);\n" +
                "            goto L8003E4A0;\n" +
                "        }\n" +
                "        m.WriteU32((c.V1 + 0x7Cu), c.T9);\n" +
                "        L8003E5F8: ;\n" +
                "        m.WriteU32((c.V1 + 0x68u), c.RA);\n" +
                "        c.T8 = m.ReadU32((c.SP + 0x20u));\n" +
                "        c.RA = 0x8003E604u;\n" +
                "        CrashBandicoot2.func_80043960(c, m);\n" +
                "        L8003E604: ;";
            if (!src.Contains(dead, StringComparison.Ordinal))
            {
                Console.WriteLine("[post-pass] warning: mesh mid-entry 0x8003E5F8 pattern not found");
                return src;
            }
            src = src.Replace(dead, labeled, StringComparison.Ordinal);
        }

        // +4 entry used by func_800401AC (RA already spilled).
        if (!src.Contains("L8003E174:", StringComparison.Ordinal))
        {
            const string prol =
                "    public static void func_8003E170(CpuContext c, IMemory m)\n" +
                "    {\n" +
                "        m.WriteU32((c.V1 + 0x68u), c.RA);\n" +
                "        c.T8 = m.ReadU32((c.SP + 0x14u));";
            const string prolLab =
                "    public static void func_8003E170(CpuContext c, IMemory m)\n" +
                "    {\n" +
                "        m.WriteU32((c.V1 + 0x68u), c.RA);\n" +
                "        L8003E174: ;\n" +
                "        c.T8 = m.ReadU32((c.SP + 0x14u));";
            if (!src.Contains(prol, StringComparison.Ordinal))
            {
                Console.WriteLine("[post-pass] warning: mesh mid-entry 0x8003E174 pattern not found");
                return src;
            }
            src = src.Replace(prol, prolLab, StringComparison.Ordinal);
        }

        const string hdr = "    public static void func_8003E170(CpuContext c, IMemory m)\n    {\n";
        var hi = src.IndexOf(hdr, StringComparison.Ordinal);
        if (hi < 0)
        {
            Console.WriteLine("[post-pass] warning: func_8003E170 header not found for mid-entry fix");
            return src;
        }
        var after = hi + hdr.Length;
        var eol = src.IndexOf('\n', after);
        var firstBody = src[after..eol];

        var replacement =
            "    public static void func_8003E170(CpuContext c, IMemory m) => func_8003E170_entry(c, m, 0x8003E170u);\n" +
            "    public static void func_8003E174(CpuContext c, IMemory m) => func_8003E170_gated(c, m, 0x8003E174u);\n" +
            "    public static void func_8003E5F8(CpuContext c, IMemory m) => func_8003E170_gated(c, m, 0x8003E5F8u);\n" +
            "    public static void func_8003E604(CpuContext c, IMemory m) => func_8003E170_gated(c, m, 0x8003E604u);\n" +
            "    public static void func_8003E784(CpuContext c, IMemory m) => func_8003E170_gated(c, m, 0x8003E784u);\n" +
            "    // Call+return mid-entries must not leak S-reg clobbers into func_80011800\n" +
            "    // (S3=-1 mode sentinel / S4=hold). Retail used tail jumps with shared frame.\n" +
            "    static void func_8003E170_gated(CpuContext c, IMemory m, uint entry)\n" +
            "    {\n" +
            "        uint s0 = c.S0, s1 = c.S1, s2 = c.S2, s3 = c.S3, s4 = c.S4;\n" +
            "        uint s5 = c.S5, s6 = c.S6, s7 = c.S7, gp = c.GP, fp = c.FP;\n" +
            "        try { func_8003E170_entry(c, m, entry); }\n" +
            "        finally\n" +
            "        {\n" +
            "            c.S0 = s0; c.S1 = s1; c.S2 = s2; c.S3 = s3; c.S4 = s4;\n" +
            "            c.S5 = s5; c.S6 = s6; c.S7 = s7; c.GP = gp; c.FP = fp;\n" +
            "        }\n" +
            "    }\n" +
            "    static void func_8003E170_entry(CpuContext c, IMemory m, uint entry)\n" +
            "    {\n" +
            "        // SCES-00967: mesh/GTE transform mid-entries (Intro draw)\n" +
            "        switch (entry)\n" +
            "        {\n" +
            "            case 0x8003E174u: goto L8003E174;\n" +
            "            case 0x8003E5F8u: goto L8003E5F8;\n" +
            "            case 0x8003E604u: goto L8003E604;\n" +
            "            case 0x8003E784u: goto L8003E784;\n" +
            "        }\n" +
            firstBody;

        src = string.Concat(src.AsSpan(0, hi), replacement, src.AsSpan(eol));

        src = src.Replace(
            "[0x8003E170u] = CrashBandicoot2.func_8003E170,",
            "[0x8003E170u] = CrashBandicoot2.func_8003E170,\n" +
            "            [0x8003E174u] = CrashBandicoot2.func_8003E174,\n" +
            "            [0x8003E5F8u] = CrashBandicoot2.func_8003E5F8,\n" +
            "            [0x8003E604u] = CrashBandicoot2.func_8003E604,\n" +
            "            [0x8003E784u] = CrashBandicoot2.func_8003E784,",
            StringComparison.Ordinal);

        Console.WriteLine("[post-pass] applied mesh transform mid-entry fix (0x8003E174/3E5F8/3E604/3E784)");
        return src;
    }

    /// <summary>
    /// <c>func_800420B8</c> early-outs do <c>j 0x800420B0</c> (shared epilogue two
    /// instructions before the function). Recompiler emits Call+return → unmapped.
    /// </summary>
    static string FixClipTestEpilogue420B0(string src)
    {
        if (src.Contains("public static void func_800420B0(", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);

        const string hdr =
            "    public static void func_800420B8(CpuContext c, IMemory m)\n    {\n";
        if (!src.Contains(hdr, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: func_800420B8 not found for 420B0 epilogue");
            return src;
        }

        src = src.Replace(
            hdr,
            "    public static void func_800420B0(CpuContext c, IMemory m)\n" +
            "    {\n" +
            "        // SCES-00967: shared clip-test epilogue @ 0x800420B0\n" +
            "        c.RA = m.ReadU32((c.V1 + 0x64u));\n" +
            "        c.T8 = 0x00000000u;\n" +
            "    }\n" +
            hdr,
            StringComparison.Ordinal);

        if (src.Contains("[0x800420B8u] = CrashBandicoot2.func_800420B8,", StringComparison.Ordinal) &&
            !src.Contains("[0x800420B0u]", StringComparison.Ordinal))
        {
            src = src.Replace(
                "[0x800420B8u] = CrashBandicoot2.func_800420B8,",
                "[0x800420B0u] = CrashBandicoot2.func_800420B0,\n            [0x800420B8u] = CrashBandicoot2.func_800420B8,",
                StringComparison.Ordinal);
        }

        Console.WriteLine("[post-pass] applied clip-test epilogue map (0x800420B0)");
        return src;
    }

    /// <summary>
    /// <c>func_80043328</c> tails into mid-entry <c>0x80043310</c> of the GTE matrix
    /// load helper (skip first <c>ctc2</c>). Unmapped Call → MIPS interp.
    /// </summary>
    static string FixGteLoadMidEntry43310(string src)
    {
        if (src.Contains("func_8004330C_entry", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);

        const string body =
            "    public static void func_8004330C(CpuContext c, IMemory m)\n" +
            "    {\n" +
            "        RecompOne.Runtime.Gte.WriteControl(0, c.T0);\n" +
            "        RecompOne.Runtime.Gte.WriteControl(1, c.T1);\n" +
            "        RecompOne.Runtime.Gte.WriteControl(2, c.T2);\n" +
            "        RecompOne.Runtime.Gte.WriteControl(3, c.T3);\n" +
            "        RecompOne.Runtime.Gte.WriteControl(4, c.T4);\n" +
            "        return;\n" +
            "    }";
        const string fixedBody =
            "    public static void func_8004330C(CpuContext c, IMemory m) => func_8004330C_entry(c, m, 0x8004330Cu);\n" +
            "    public static void func_80043310(CpuContext c, IMemory m) => func_8004330C_entry(c, m, 0x80043310u);\n" +
            "    static void func_8004330C_entry(CpuContext c, IMemory m, uint entry)\n" +
            "    {\n" +
            "        // SCES-00967: GTE matrix load mid-entry @ 0x80043310 (skip first ctc2)\n" +
            "        if (entry == 0x80043310u)\n" +
            "            goto L80043310;\n" +
            "        RecompOne.Runtime.Gte.WriteControl(0, c.T0);\n" +
            "        L80043310: ;\n" +
            "        RecompOne.Runtime.Gte.WriteControl(1, c.T1);\n" +
            "        RecompOne.Runtime.Gte.WriteControl(2, c.T2);\n" +
            "        RecompOne.Runtime.Gte.WriteControl(3, c.T3);\n" +
            "        RecompOne.Runtime.Gte.WriteControl(4, c.T4);\n" +
            "        return;\n" +
            "    }";
        if (!src.Contains(body, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: func_8004330C pattern not found for 43310 fix");
            return src;
        }

        src = src.Replace(body, fixedBody, StringComparison.Ordinal);

        if (src.Contains("[0x8004330Cu] = CrashBandicoot2.func_8004330C,", StringComparison.Ordinal) &&
            !src.Contains("[0x80043310u]", StringComparison.Ordinal))
        {
            src = src.Replace(
                "[0x8004330Cu] = CrashBandicoot2.func_8004330C,",
                "[0x8004330Cu] = CrashBandicoot2.func_8004330C,\n            [0x80043310u] = CrashBandicoot2.func_80043310,",
                StringComparison.Ordinal);
        }
        else if (src.Contains("[0x80043328u] = CrashBandicoot2.func_80043328,", StringComparison.Ordinal) &&
                 !src.Contains("[0x80043310u]", StringComparison.Ordinal))
        {
            // 4330C may be inlined/unmapped in some builds; hang the hook next to 43328.
            src = src.Replace(
                "[0x80043328u] = CrashBandicoot2.func_80043328,",
                "[0x80043310u] = CrashBandicoot2.func_80043310,\n            [0x80043328u] = CrashBandicoot2.func_80043328,",
                StringComparison.Ordinal);
        }

        Console.WriteLine("[post-pass] applied GTE load mid-entry fix (0x80043310)");
        return src;
    }

    /// <summary>
    /// Matrix helper mid-entry <c>0x80044324</c> lives inside the mega-fn that also holds
    /// raster fragments (<c>func_80043E0C</c>). Label already exists for internal goto;
    /// map the VA so Intro draw stops interpreting it.
    /// </summary>
    static string FixMatrixMidEntry44324(string src)
    {
        if (src.Contains("func_80043E0C_entry", StringComparison.Ordinal) ||
            src.Contains("[0x80044324u] = CrashBandicoot2.func_80044324,", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);

        if (!src.Contains("L80044324:", StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: L80044324 not found for matrix mid-entry");
            return src;
        }

        const string hdr = "    public static void func_80043E0C(CpuContext c, IMemory m)\n    {\n";
        var hi = src.IndexOf(hdr, StringComparison.Ordinal);
        if (hi < 0)
        {
            Console.WriteLine("[post-pass] warning: func_80043E0C header not found for 44324 fix");
            return src;
        }
        var after = hi + hdr.Length;
        var eol = src.IndexOf('\n', after);
        var firstBody = src[after..eol];

        var replacement =
            "    public static void func_80043E0C(CpuContext c, IMemory m) => func_80043E0C_entry(c, m, 0x80043E0Cu);\n" +
            "    public static void func_80044324(CpuContext c, IMemory m) => func_80043E0C_entry(c, m, 0x80044324u);\n" +
            "    static void func_80043E0C_entry(CpuContext c, IMemory m, uint entry)\n" +
            "    {\n" +
            "        // SCES-00967: matrix mid-entry @ 0x80044324\n" +
            "        if (entry == 0x80044324u)\n" +
            "            goto L80044324;\n" +
            firstBody;

        src = string.Concat(src.AsSpan(0, hi), replacement, src.AsSpan(eol));

        // Prefer hanging the map entry next to 43E0C if present, else 44410.
        if (src.Contains("[0x80043E0Cu] = CrashBandicoot2.func_80043E0C,", StringComparison.Ordinal))
        {
            src = src.Replace(
                "[0x80043E0Cu] = CrashBandicoot2.func_80043E0C,",
                "[0x80043E0Cu] = CrashBandicoot2.func_80043E0C,\n            [0x80044324u] = CrashBandicoot2.func_80044324,",
                StringComparison.Ordinal);
        }
        else if (src.Contains("[0x80044410u] = CrashBandicoot2.func_80044410,", StringComparison.Ordinal))
        {
            src = src.Replace(
                "[0x80044410u] = CrashBandicoot2.func_80044410,",
                "[0x80044324u] = CrashBandicoot2.func_80044324,\n            [0x80044410u] = CrashBandicoot2.func_80044410,",
                StringComparison.Ordinal);
        }
        else
            Console.WriteLine("[post-pass] warning: could not inject 0x80044324 map entry");

        Console.WriteLine("[post-pass] applied matrix mid-entry fix (0x80044324)");
        return src;
    }

    /// <summary>
    /// <c>func_80011800</c> keeps two loop constants in callee-saved registers:
    /// <c>S3=-1</c> (no pending mode) and <c>S4=2</c> (DrawHold during a load).
    /// Intro's mixed recompiled/interpreted draw path currently leaks register writes
    /// from a nested mid-entry, so the next frame sees values such as S3=0x00281215
    /// and S4=0x1F800000. Reassert the values at the loop header, matching the MIPS ABI
    /// contract that callees must preserve them.
    /// </summary>
    static string FixMainLoopCalleeSavedGuard(string src)
    {
        if (src.Contains("Restore func_80011800 callee-saved loop constants", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string needle =
            "        L80011848: ;\n" +
            "        c.V0 = 0x80070000u;";
        const string fix =
            "        L80011848: ;\n" +
            "        // Restore func_80011800 callee-saved loop constants after nested draw calls.\n" +
            "        c.S3 = 0xFFFFFFFFu;\n" +
            "        c.S4 = 0x00000002u;\n" +
            "        c.V0 = 0x80070000u;";
        if (!src.Contains(needle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: main-loop callee-saved guard pattern not found");
            return src;
        }

        src = src.Replace(needle, fix, StringComparison.Ordinal);
        Console.WriteLine("[post-pass] restored func_80011800 S3/S4 loop constants");
        return src;
    }

    /// <summary>
    /// The mixed GOOL/native Intro path can leave a stale child/sibling link in the
    /// recursive object walker. Retail never calls the object handler for a null or
    /// non-KSEG object; reject such nodes before the generated prologue dispatches GOOL.
    /// </summary>
    static string FixObjectTraversalPointerGuard(string src)
    {
        if (src.Contains("Reject invalid recursive object node", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string needle =
            "    public static void func_80018BCC(CpuContext c, IMemory m)\n" +
            "    {\n" +
            "        c.SP = c.SP - 0x28u;";
        const string fix =
            "    public static void func_80018BCC(CpuContext c, IMemory m)\n" +
            "    {\n" +
            "        // Reject invalid recursive object node before invoking its GOOL handler.\n" +
            "        if (c.A0 < 0x80010000u || c.A0 >= 0x80200000u || (c.A0 & 3u) != 0u)\n" +
            "        {\n" +
            "            c.V0 = 0xFFFFFF01u;\n" +
            "            return;\n" +
            "        }\n" +
            "        c.SP = c.SP - 0x28u;";
        if (!src.Contains(needle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: object traversal pointer guard pattern not found");
            return src;
        }

        src = src.Replace(needle, fix, StringComparison.Ordinal);
        Console.WriteLine("[post-pass] applied recursive object pointer guard (func_80018BCC)");
        return src;
    }

    /// <summary>
    /// Do not enter the GOOL bytecode interpreter with a null/stale object pointer.
    /// Returning -255 matches the guest's no-action/error result used by its callers.
    /// </summary>
    static string FixGoolObjectPointerGuard(string src)
    {
        if (src.Contains("Reject invalid GOOL object pointer", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string needle =
            "    public static void func_8003A2AC(CpuContext c, IMemory m)\n" +
            "    {\n" +
            "        goto L8003A2AC;";
        const string fix =
            "    public static void func_8003A2AC(CpuContext c, IMemory m)\n" +
            "    {\n" +
            "        uint stableGoolObject8003A2AC = c.A0;\n" +
            "        // Reject invalid GOOL object pointer instead of decoding low RAM as bytecode.\n" +
            "        if (c.A0 < 0x80010000u || c.A0 >= 0x80200000u || (c.A0 & 3u) != 0u)\n" +
            "        {\n" +
            "            c.V0 = 0xFFFFFF01u;\n" +
            "            return;\n" +
            "        }\n" +
            "        goto L8003A2AC;";
        if (!src.Contains(needle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: GOOL object pointer guard pattern not found");
            return src;
        }

        src = src.Replace(needle, fix, StringComparison.Ordinal);
        Console.WriteLine("[post-pass] applied GOOL object pointer guard (func_8003A2AC)");
        return src;
    }

    /// <summary>
    /// Keep the GOOL object's callee-saved S0 stable across mixed recompiled/native
    /// opcode helpers, and stop an object whose bytecode PC has become non-addressable.
    /// </summary>
    static string FixGoolInterpreterStateGuard(string src)
    {
        if (src.Contains("Stable GOOL object pointer across opcode helpers", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string prologueNeedle =
            "        c.S0 = c.A0 + 0u;\n" +
            "        c.S1 = c.A1 + 0u;\n" +
            "        c.S2 = c.A2 + 0u;\n" +
            "        c.FP = 0x1F800000u;";
        const string prologueFix =
            "        c.S0 = c.A0 + 0u;\n" +
            "        // Stable GOOL object pointer across opcode helpers, outside guest RAM.\n" +
            "        stableGoolObject8003A2AC = c.S0;\n" +
            "        c.S1 = c.A1 + 0u;\n" +
            "        c.S2 = c.A2 + 0u;\n" +
            "        c.FP = 0x1F800000u;";
        if (!src.Contains(prologueNeedle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: GOOL interpreter stable-object prologue not found");
            return src;
        }
        src = src.Replace(prologueNeedle, prologueFix, StringComparison.Ordinal);

        const string fetchNeedle =
            "        L8003A304: ;\n" +
            "        // Scratchpad $fp must stay 0x1F800000 for opcode-table fetch at +0x5C.";
        const string fetchFix =
            "        L8003A304: ;\n" +
            "        c.S0 = stableGoolObject8003A2AC;\n" +
            "        if (c.S5 < 0x80010000u || c.S5 >= 0x80200000u || (c.S5 & 3u) != 0u)\n" +
            "        {\n" +
            "            c.S5 = 0u;\n" +
            "            c.V0 = 0xFFFFFF01u;\n" +
            "            goto L8003A330;\n" +
            "        }\n" +
            "        // Scratchpad $fp must stay 0x1F800000 for opcode-table fetch at +0x5C.";
        if (!src.Contains(fetchNeedle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: GOOL interpreter state-guard fetch point not found");
            return src;
        }
        src = src.Replace(fetchNeedle, fetchFix, StringComparison.Ordinal);
        string[] continuationLabels =
        [
            "L8003A25C", "L8003A260", "L8003A264", "L8003A268", "L8003A26C",
            "L8003A270", "L8003A274", "L8003A278", "L8003A27C", "L8003A280",
            "L8003A284", "L8003A288", "L8003A28C", "L8003A290", "L8003A294",
            "L8003A298", "L8003A29C", "L8003A2A0", "L8003A2A4", "L8003A2A8"
        ];
        foreach (string label in continuationLabels)
        {
            string needle = $"        {label}: ;";
            string fix = needle + "\n        c.S0 = stableGoolObject8003A2AC;";
            src = src.Replace(needle, fix, StringComparison.Ordinal);
        }
        Console.WriteLine("[post-pass] applied GOOL stable-object / bytecode-PC guard");
        return src;
    }

    /// <summary>
    /// The Intro GOOL script polls an asynchronously requested asset at 0x800ECAD0.
    /// On retail hardware the CD interrupt completes the request while the script spins.
    /// Our CD reads complete synchronously, but the resource state machine is advanced by
    /// the outer game loop; yield this one unresolved poll so that loop can run.
    /// </summary>
    static string FixIntroAssetPollYield(string src)
    {
        if (src.Contains("Yield the Intro asset poll to the outer resource pump", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string needle =
            "        L8003A304: ;\n" +
            "        c.S0 = stableGoolObject8003A2AC;\n" +
            "        if (c.S5 < 0x80010000u || c.S5 >= 0x80200000u || (c.S5 & 3u) != 0u)";
        const string replacement =
            "        L8003A304: ;\n" +
            "        c.S0 = stableGoolObject8003A2AC;\n" +
            "        // Yield the Intro asset poll to the outer resource pump.\n" +
            "        // The request is already queued; resuming in the next game tick lets\n" +
            "        // func_80012C88 finalize it without growing the GOOL value stack.\n" +
            "        if (c.S5 == 0x800ECAD0u\n" +
            "            && m.ReadU32((c.S0 + 0x9Cu)) == 0x8006F9E0u\n" +
            "            && (m.ReadU32(0x8006F9E0u) & 1u) != 0u)\n" +
            "        {\n" +
            "            c.V0 = 0u;\n" +
            "            goto L8003A330;\n" +
            "        }\n" +
            "        if (c.S5 < 0x80010000u || c.S5 >= 0x80200000u || (c.S5 & 3u) != 0u)";
        if (!src.Contains(needle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: Intro asset-poll yield pattern not found");
            return src;
        }

        src = src.Replace(needle, replacement, StringComparison.Ordinal);
        Console.WriteLine("[post-pass] applied Intro asset-poll yield");
        return src;
    }

    /// <summary>
    /// GOOL indirect operands can carry engine flags above the 2 MiB RAM address.
    /// Normalize those flagged KSEG0 pointers locally and make invalid reads benign.
    /// </summary>
    static string FixGoolIndirectPointerGuard(string src)
    {
        if (src.Contains("Normalize flagged GOOL indirect pointer", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string readNeedle =
            "        c.V0 = c.V0 + c.V1;\n" +
            "        c.V1 = m.ReadU32(c.V0);\n" +
            "        c.S6 = c.S6 + 0x4u;";
        const string readFix =
            "        c.V0 = c.V0 + c.V1;\n" +
            "        // Normalize flagged GOOL indirect pointer (for example 0x82xxxxxx).\n" +
            "        if ((c.V0 < 0x80010000u || c.V0 >= 0x80200000u) &&\n" +
            "            (c.V0 & 0xFC000000u) == 0x80000000u)\n" +
            "            c.V0 = 0x80000000u | (c.V0 & 0x001FFFFFu);\n" +
            "        c.V1 = c.V0 >= 0x80010000u && c.V0 < 0x80200000u && (c.V0 & 3u) == 0u\n" +
            "            ? m.ReadU32(c.V0) : 0u;\n" +
            "        c.S6 = c.S6 + 0x4u;";
        if (!src.Contains(readNeedle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: GOOL indirect-read pattern not found");
            return src;
        }
        src = src.Replace(readNeedle, readFix, StringComparison.Ordinal);
        Console.WriteLine("[post-pass] applied GOOL indirect pointer normalization");
        return src;
    }

    static string FixGoolZeroOpcodeHandler(string src)
    {
        if (src.Contains("Stop on an empty GOOL opcode slot", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string needle =
            "        c.A3 = m.ReadU32(c.A3);\n" +
            "        switch (c.A3)";
        const string fix =
            "        c.A3 = m.ReadU32(c.A3);\n" +
            "        // Stop on an empty GOOL opcode slot instead of dispatching address zero.\n" +
            "        if (c.A3 == 0u)\n" +
            "        {\n" +
            "            c.V0 = 0xFFFFFF01u;\n" +
            "            goto L8003A330;\n" +
            "        }\n" +
            "        switch (c.A3)";
        if (!src.Contains(needle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: GOOL empty opcode-handler pattern not found");
            return src;
        }
        src = src.Replace(needle, fix, StringComparison.Ordinal);
        Console.WriteLine("[post-pass] applied GOOL empty opcode-handler guard");
        return src;
    }

    static string FixGoolTransformPointerGuard(string src)
    {
        if (src.Contains("Validate GOOL transform descriptor", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string firstNeedle =
            "        c.RA = 0x800391C0u;\n" +
            "        CrashBandicoot2.func_800370C8(c, m);\n" +
            "        c.A0 = c.S1 + 0u;\n" +
            "        c.A1 = c.S0 & 0x0FFFu;\n" +
            "        c.S0 = m.ReadU32(c.V0);";
        const string firstFix =
            "        c.RA = 0x800391C0u;\n" +
            "        CrashBandicoot2.func_800370C8(c, m);\n" +
            "        // Validate GOOL transform descriptor before reading its type.\n" +
            "        if (c.V0 < 0x80010000u || c.V0 >= 0x80200000u || (c.V0 & 3u) != 0u)\n" +
            "        {\n" +
            "            c.V0 = 0xFFFFFF01u;\n" +
            "            goto L8003928C;\n" +
            "        }\n" +
            "        c.A0 = c.S1 + 0u;\n" +
            "        c.A1 = c.S0 & 0x0FFFu;\n" +
            "        c.S0 = m.ReadU32(c.V0);";
        const string secondNeedle =
            "        c.RA = 0x800391D4u;\n" +
            "        CrashBandicoot2.func_800370C8(c, m);\n" +
            "        c.A0 = c.V0 + 0u;";
        const string secondFix =
            "        c.RA = 0x800391D4u;\n" +
            "        CrashBandicoot2.func_800370C8(c, m);\n" +
            "        if (c.V0 < 0x80010000u || c.V0 >= 0x80200000u || (c.V0 & 3u) != 0u)\n" +
            "        {\n" +
            "            c.V0 = 0xFFFFFF01u;\n" +
            "            goto L8003928C;\n" +
            "        }\n" +
            "        c.A0 = c.V0 + 0u;";
        if (!src.Contains(firstNeedle, StringComparison.Ordinal) ||
            !src.Contains(secondNeedle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: GOOL transform descriptor patterns not found");
            return src;
        }
        src = src.Replace(firstNeedle, firstFix, StringComparison.Ordinal);
        src = src.Replace(secondNeedle, secondFix, StringComparison.Ordinal);
        Console.WriteLine("[post-pass] applied GOOL transform descriptor guards");
        return src;
    }

    /// <summary>
    /// <c>func_8001C850</c> keeps its current object in S0. A nested event helper can
    /// leak a small counter into S0. Preserve the object in a managed local (guest
    /// routines may legally overwrite unused words in the emulated stack frame).
    /// </summary>
    static string FixObjectHandlerStablePointer(string src)
    {
        if (src.Contains("Stable func_8001C850 object pointer", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string prologueNeedle =
            "        m.WriteU32((c.SP + 0x30u), c.S0);\n" +
            "        c.S0 = c.A0 + 0u;\n" +
            "        m.WriteU32((c.SP + 0x38u), c.RA);";
        const string prologueFix =
            "        m.WriteU32((c.SP + 0x30u), c.S0);\n" +
            "        c.S0 = c.A0 + 0u;\n" +
            "        // Stable func_8001C850 object pointer outside guest RAM.\n" +
            "        uint stableObject8001C850 = c.S0;\n" +
            "        m.WriteU32((c.SP + 0x38u), c.RA);";
        if (!src.Contains(prologueNeedle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: func_8001C850 stable-object prologue not found");
            return src;
        }
        src = src.Replace(prologueNeedle, prologueFix, StringComparison.Ordinal);

        const string callNeedle =
            "        c.RA = 0x8001CB14u;\n" +
            "        CrashBandicoot2.func_8001C114(c, m);\n" +
            "        c.A0 = c.S0 + 0u;\n" +
            "        c.A1 = c.SP + 0x18u;";
        const string callFix =
            "        c.RA = 0x8001CB14u;\n" +
            "        CrashBandicoot2.func_8001C114(c, m);\n" +
            "        c.S0 = stableObject8001C850;\n" +
            "        c.A0 = c.S0 + 0u;\n" +
            "        c.A1 = c.SP + 0x18u;";
        if (!src.Contains(callNeedle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: func_8001C850 restore before 1D860 not found");
            return src;
        }
        src = src.Replace(callNeedle, callFix, StringComparison.Ordinal);
        Console.WriteLine("[post-pass] stabilized func_8001C850 object pointer before func_8001D860");
        return src;
    }

    static string FixObjectHandlerClassTypePointerGuard(string src)
    {
        if (src.Contains("Validate func_8001C850 class-type pointer", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string firstNeedle =
            "        L8001C8F8: ;\n" +
            "        c.V0 = m.ReadU32((c.S0 + 0xCu));\n" +
            "        c.V0 = m.ReadU32((c.V0 + 0x10u));\n" +
            "        c.V1 = m.ReadU32((c.V0 + 0x4u));";
        const string firstFix =
            "        L8001C8F8: ;\n" +
            "        c.V0 = m.ReadU32((c.S0 + 0xCu));\n" +
            "        c.V0 = m.ReadU32((c.V0 + 0x10u));\n" +
            "        // Validate func_8001C850 class-type pointer before the input test.\n" +
            "        if (c.V0 < 0x80010000u || c.V0 >= 0x80200000u || (c.V0 & 3u) != 0u)\n" +
            "        {\n" +
            "            c.A0 = 0u;\n" +
            "            goto L8001C9CC;\n" +
            "        }\n" +
            "        c.V1 = m.ReadU32((c.V0 + 0x4u));";
        if (!src.Contains(firstNeedle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: first func_8001C850 class-type pattern not found");
            return src;
        }
        src = src.Replace(firstNeedle, firstFix, StringComparison.Ordinal);

        const string secondNeedle =
            "        L8001CBB8: ;\n" +
            "        c.V0 = m.ReadU32((c.S0 + 0xCu));\n" +
            "        c.V0 = m.ReadU32((c.V0 + 0x10u));\n" +
            "        c.V1 = m.ReadU32((c.V0 + 0x4u));";
        const string secondFix =
            "        L8001CBB8: ;\n" +
            "        c.V0 = m.ReadU32((c.S0 + 0xCu));\n" +
            "        c.V0 = m.ReadU32((c.V0 + 0x10u));\n" +
            "        if (c.V0 < 0x80010000u || c.V0 >= 0x80200000u || (c.V0 & 3u) != 0u)\n" +
            "        {\n" +
            "            c.A0 = 0u;\n" +
            "            goto L8001CCDC;\n" +
            "        }\n" +
            "        c.V1 = m.ReadU32((c.V0 + 0x4u));";
        if (!src.Contains(secondNeedle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: second func_8001C850 class-type pattern not found");
            return src;
        }
        src = src.Replace(secondNeedle, secondFix, StringComparison.Ordinal);
        Console.WriteLine("[post-pass] applied func_8001C850 class-type pointer guards");
        return src;
    }

    /// <summary>
    /// func_8001C114 treats object+E8 as an optional structure pointer. Mixed GOOL
    /// state can leave packed flags there; follow the null-field path for non-RAM values.
    /// </summary>
    static string FixObjectOptionalE8PointerGuard(string src)
    {
        if (src.Contains("Validate optional object+E8 pointer", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string needle =
            "        c.A0 = m.ReadU32((c.S0 + 0xE8u));\n" +
            "        if (c.A0 == 0u) {\n" +
            "            c.V0 = 0u | 0x0004u;\n" +
            "            goto L8001C3B8;\n" +
            "        }\n" +
            "        c.V0 = 0u | 0x0004u;\n" +
            "        c.V1 = m.ReadU8(c.A0);";
        const string fix =
            "        c.A0 = m.ReadU32((c.S0 + 0xE8u));\n" +
            "        // Validate optional object+E8 pointer before dereference.\n" +
            "        if (c.A0 == 0u || c.A0 < 0x80010000u || c.A0 >= 0x80200000u) {\n" +
            "            c.V0 = 0u | 0x0004u;\n" +
            "            goto L8001C3B8;\n" +
            "        }\n" +
            "        c.V0 = 0u | 0x0004u;\n" +
            "        c.V1 = m.ReadU8(c.A0);";
        if (!src.Contains(needle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: object+E8 pointer guard point not found");
            return src;
        }
        src = src.Replace(needle, fix, StringComparison.Ordinal);
        Console.WriteLine("[post-pass] applied optional object+E8 pointer guard");
        return src;
    }

    static string FixObjectClassTypePointerGuard(string src)
    {
        if (src.Contains("Validate optional object class-type pointer", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string needle =
            "        c.V0 = m.ReadU32((c.S1 + 0xCu));\n" +
            "        c.V0 = m.ReadU32((c.V0 + 0x10u));\n" +
            "        c.V1 = m.ReadU32(c.V0);\n" +
            "        c.V0 = 0u | 0x0005u;";
        const string fix =
            "        c.V0 = m.ReadU32((c.S1 + 0xCu));\n" +
            "        c.V0 = m.ReadU32((c.V0 + 0x10u));\n" +
            "        // Validate optional object class-type pointer before dereference.\n" +
            "        if (c.V0 < 0x80010000u || c.V0 >= 0x80200000u || (c.V0 & 3u) != 0u)\n" +
            "        {\n" +
            "            m.WriteU32((c.S1 + 0x58u), 0u);\n" +
            "            goto L8001E038;\n" +
            "        }\n" +
            "        c.V1 = m.ReadU32(c.V0);\n" +
            "        c.V0 = 0u | 0x0005u;";
        if (!src.Contains(needle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: object class-type pointer pattern not found");
            return src;
        }
        src = src.Replace(needle, fix, StringComparison.Ordinal);
        Console.WriteLine("[post-pass] applied optional object class-type pointer guard");
        return src;
    }

    static string FixResourceLookupPointerGuard(string src)
    {
        if (src.Contains("Reject invalid resource-table pointer", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string needle =
            "    public static void func_80031DF4(CpuContext c, IMemory m)\n" +
            "    {\n" +
            "        c.SP = c.SP - 0x8u;";
        const string fix =
            "    public static void func_80031DF4(CpuContext c, IMemory m)\n" +
            "    {\n" +
            "        // Reject invalid resource-table pointer; zero means not found.\n" +
            "        if (c.A0 < 0x80010000u || c.A0 >= 0x80200000u || (c.A0 & 3u) != 0u)\n" +
            "        {\n" +
            "            c.V0 = 0u;\n" +
            "            return;\n" +
            "        }\n" +
            "        c.SP = c.SP - 0x8u;";
        if (!src.Contains(needle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: resource lookup pointer pattern not found");
            return src;
        }
        src = src.Replace(needle, fix, StringComparison.Ordinal);
        const string loopNeedle =
            "        L80031E0C: ;\n" +
            "        c.T0 = (uint)(short)m.ReadU16((c.A0 + 0xCu));";
        const string loopFix =
            "        L80031E0C: ;\n" +
            "        if (c.A0 < 0x80010000u || c.A0 >= 0x80200000u || (c.A0 & 3u) != 0u)\n" +
            "            goto L800324CC;\n" +
            "        c.T0 = (uint)(short)m.ReadU16((c.A0 + 0xCu));";
        if (!src.Contains(loopNeedle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: resource lookup chain pattern not found");
            return src;
        }
        src = src.Replace(loopNeedle, loopFix, StringComparison.Ordinal);
        Console.WriteLine("[post-pass] applied resource lookup pointer guard");
        return src;
    }

    static string FixLevelColorPointerGuard(string src)
    {
        if (src.Contains("Validate optional level-color table", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string needle =
            "        c.V0 = m.ReadU32((c.V0 + 0x10u));\n" +
            "        c.A0 = m.ReadU8((c.V0 + 0x2B4u));\n" +
            "        c.A1 = m.ReadU8((c.V0 + 0x2B5u));\n" +
            "        c.A2 = m.ReadU8((c.V0 + 0x2B6u));\n" +
            "        c.RA = 0x80017E5Cu;";
        const string fix =
            "        c.V0 = m.ReadU32((c.V0 + 0x10u));\n" +
            "        // Validate optional level-color table; black is the neutral fallback.\n" +
            "        if (c.V0 >= 0x80010000u && c.V0 < 0x80200000u)\n" +
            "        {\n" +
            "            c.A0 = m.ReadU8((c.V0 + 0x2B4u));\n" +
            "            c.A1 = m.ReadU8((c.V0 + 0x2B5u));\n" +
            "            c.A2 = m.ReadU8((c.V0 + 0x2B6u));\n" +
            "        }\n" +
            "        else\n" +
            "        {\n" +
            "            c.A0 = 0u; c.A1 = 0u; c.A2 = 0u;\n" +
            "        }\n" +
            "        c.RA = 0x80017E5Cu;";
        if (!src.Contains(needle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: level-color pointer pattern not found");
            return src;
        }
        src = src.Replace(needle, fix, StringComparison.Ordinal);
        const string flagNeedle =
            "        c.V0 = m.ReadU32((c.V0 + 0x10u));\n" +
            "        c.V0 = m.ReadU32((c.V0 + 0x29Cu));\n" +
            "        c.V0 = c.V0 & 0x1000u;";
        const string flagFix =
            "        c.V0 = m.ReadU32((c.V0 + 0x10u));\n" +
            "        if (c.V0 < 0x80010000u || c.V0 >= 0x80200000u)\n" +
            "            goto L80017FF0;\n" +
            "        c.V0 = m.ReadU32((c.V0 + 0x29Cu));\n" +
            "        c.V0 = c.V0 & 0x1000u;";
        if (!src.Contains(flagNeedle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: level-color flag pointer pattern not found");
            return src;
        }
        src = src.Replace(flagNeedle, flagFix, StringComparison.Ordinal);
        const string secondFlagNeedle =
            "        c.V0 = m.ReadU32((c.T0 + 0x10u));\n" +
            "        c.V0 = m.ReadU32((c.V0 + 0x29Cu));\n" +
            "        c.V0 = c.V0 & 0x1000u;";
        const string secondFlagFix =
            "        c.V0 = m.ReadU32((c.T0 + 0x10u));\n" +
            "        c.V0 = c.V0 >= 0x80010000u && c.V0 < 0x80200000u\n" +
            "            ? m.ReadU32((c.V0 + 0x29Cu)) : 0u;\n" +
            "        c.V0 = c.V0 & 0x1000u;";
        if (!src.Contains(secondFlagNeedle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: second level-color flag pointer pattern not found");
            return src;
        }
        src = src.Replace(secondFlagNeedle, secondFlagFix, StringComparison.Ordinal);
        Console.WriteLine("[post-pass] applied optional level-color pointer guard");
        return src;
    }

    static string FixLevelTableProcessGuard(string src)
    {
        if (src.Contains("Reject missing level table before processing", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string needle =
            "    public static void func_800183B8(CpuContext c, IMemory m)\n" +
            "    {\n" +
            "        c.SP = c.SP - 0x50u;";
        const string fix =
            "    public static void func_800183B8(CpuContext c, IMemory m)\n" +
            "    {\n" +
            "        // Reject missing level table before processing.\n" +
            "        if (c.A0 < 0x80010000u || c.A0 >= 0x80200000u || (c.A0 & 3u) != 0u)\n" +
            "        {\n" +
            "            c.V0 = 0u;\n" +
            "            return;\n" +
            "        }\n" +
            "        c.SP = c.SP - 0x50u;";
        if (!src.Contains(needle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: level table processor pattern not found");
            return src;
        }
        src = src.Replace(needle, fix, StringComparison.Ordinal);
        Console.WriteLine("[post-pass] applied missing level-table processor guard");
        return src;
    }

    static string FixLevelAudioTablePointerGuard(string src)
    {
        if (src.Contains("Reject missing level-audio table before processing", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string needle =
            "    public static void func_80034204(CpuContext c, IMemory m)\n" +
            "    {\n" +
            "        c.V0 = (uint)(short)m.ReadU16(((c.GpBase + 0x264u)));";
        const string fix =
            "    public static void func_80034204(CpuContext c, IMemory m)\n" +
            "    {\n" +
            "        // Reject missing level-audio table before processing.\n" +
            "        if (c.A0 < 0x80010000u || c.A0 >= 0x80200000u || (c.A0 & 3u) != 0u)\n" +
            "        {\n" +
            "            c.V0 = 0u;\n" +
            "            return;\n" +
            "        }\n" +
            "        c.V0 = (uint)(short)m.ReadU16(((c.GpBase + 0x264u)));";
        if (!src.Contains(needle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: level-audio table guard pattern not found");
            return src;
        }

        src = src.Replace(needle, fix, StringComparison.Ordinal);
        Console.WriteLine("[post-pass] applied missing level-audio table guard");
        return src;
    }

    static string FixIntroOptionalPacketPointerGuard(string src)
    {
        if (src.Contains("Reject missing Intro packet table before dereference", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string firstNeedle =
            "        c.V0 = m.ReadU32((c.V0 + 0x10u));\n" +
            "        c.V0 = m.ReadU32(c.V0);\n" +
            "        if (c.V0 == 0u) {\n" +
            "            goto L80011CC8;\n" +
            "        }";
        const string firstFix =
            "        c.V0 = m.ReadU32((c.V0 + 0x10u));\n" +
            "        // Reject missing Intro packet table before dereference.\n" +
            "        if (c.V0 < 0x80010000u || c.V0 >= 0x80200000u || (c.V0 & 3u) != 0u)\n" +
            "            goto L80011CC8;\n" +
            "        c.V0 = m.ReadU32(c.V0);\n" +
            "        if (c.V0 == 0u) {\n" +
            "            goto L80011CC8;\n" +
            "        }";
        if (!src.Contains(firstNeedle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: first Intro packet-table pattern not found");
            return src;
        }
        src = src.Replace(firstNeedle, firstFix, StringComparison.Ordinal);

        const string secondNeedle =
            "        c.V0 = m.ReadU32((c.V0 + 0x10u));\n" +
            "        c.A2 = m.ReadU32(c.V0);\n" +
            "        c.A0 = 0x1F800000u;";
        const string secondFix =
            "        c.V0 = m.ReadU32((c.V0 + 0x10u));\n" +
            "        if (c.V0 < 0x80010000u || c.V0 >= 0x80200000u || (c.V0 & 3u) != 0u)\n" +
            "            goto L80011CC8;\n" +
            "        c.A2 = m.ReadU32(c.V0);\n" +
            "        c.A0 = 0x1F800000u;";
        if (!src.Contains(secondNeedle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: second Intro packet-table pattern not found");
            return src;
        }
        src = src.Replace(secondNeedle, secondFix, StringComparison.Ordinal);
        Console.WriteLine("[post-pass] applied optional Intro packet-table guards");
        return src;
    }

    /// <summary>
    /// Preserve func_80018BCC's current object, handler and handler argument across the
    /// callback. Mixed GOOL/native handlers can leak S0-S2 and otherwise pass a data
    /// pointer as the handler address on the next recursive child.
    /// </summary>
    static string FixRecursiveWalkerStableArgs(string src)
    {
        if (src.Contains("Stable func_80018BCC recursive arguments in managed locals", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string saveNeedle =
            "        c.S2 = c.A2 + 0u;\n" +
            "        c.A1 = c.S2 + 0u;\n" +
            "        m.WriteU32((c.SP + 0x20u), c.RA);";
        const string saveFix =
            "        c.S2 = c.A2 + 0u;\n" +
            "        // Stable func_80018BCC recursive arguments in managed locals.\n" +
            "        uint stableWalkerObject = c.S0;\n" +
            "        uint stableWalkerHandler = c.S1;\n" +
            "        uint stableWalkerArgument = c.S2;\n" +
            "        uint stableWalkerResult = 0u;\n" +
            "        uint stableWalkerNext = 0u;\n" +
            "        c.A1 = c.S2 + 0u;\n" +
            "        m.WriteU32((c.SP + 0x20u), c.RA);";
        if (!src.Contains(saveNeedle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: func_80018BCC stable-args save point not found");
            return src;
        }
        src = src.Replace(saveNeedle, saveFix, StringComparison.Ordinal);

        const string restoreNeedle =
            "        c.RA = 0x80018BF8u;\n" +
            "        Dispatcher.Call(c, m, c.S1);\n" +
            "        c.S3 = c.V0 + 0u;";
        const string restoreFix =
            "        c.RA = 0x80018BF8u;\n" +
            "        Dispatcher.Call(c, m, c.S1);\n" +
            "        c.S0 = stableWalkerObject;\n" +
            "        c.S1 = stableWalkerHandler;\n" +
            "        c.S2 = stableWalkerArgument;\n" +
            "        stableWalkerResult = c.V0;\n" +
            "        c.S3 = stableWalkerResult;";
        if (!src.Contains(restoreNeedle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: func_80018BCC stable-args restore point not found");
            return src;
        }
        src = src.Replace(restoreNeedle, restoreFix, StringComparison.Ordinal);

        const string recurseNeedle =
            "        c.A1 = c.S1 + 0u;\n" +
            "        c.S0 = m.ReadU32((c.A0 + 0x48u));\n" +
            "        c.A2 = c.S2 + 0u;\n" +
            "        c.RA = 0x80018C44u;\n" +
            "        CrashBandicoot2.func_80018BCC(c, m);\n" +
            "        c.A0 = c.S0 + 0u;";
        const string recurseFix =
            "        c.A1 = stableWalkerHandler;\n" +
            "        stableWalkerNext = m.ReadU32((c.A0 + 0x48u));\n" +
            "        c.S0 = stableWalkerNext;\n" +
            "        c.A2 = stableWalkerArgument;\n" +
            "        c.RA = 0x80018C44u;\n" +
            "        CrashBandicoot2.func_80018BCC(c, m);\n" +
            "        c.S0 = stableWalkerNext;\n" +
            "        c.S1 = stableWalkerHandler;\n" +
            "        c.S2 = stableWalkerArgument;\n" +
            "        c.S3 = stableWalkerResult;\n" +
            "        c.A0 = c.S0 + 0u;";
        if (!src.Contains(recurseNeedle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: func_80018BCC recursive restore point not found");
            return src;
        }
        src = src.Replace(recurseNeedle, recurseFix, StringComparison.Ordinal);
        Console.WriteLine("[post-pass] stabilized func_80018BCC arguments across handler callback");
        return src;
    }

    static string FixRecursiveWalkerChildPointerGuard(string src)
    {
        if (src.Contains("Reject invalid recursive child link", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string needle =
            "        L80018C28: ;\n" +
            "        c.V0 = m.ReadU32(c.A0);";
        const string fix =
            "        L80018C28: ;\n" +
            "        // Reject invalid recursive child link before reading the node.\n" +
            "        if (c.A0 < 0x80010000u || c.A0 >= 0x80200000u || (c.A0 & 3u) != 0u)\n" +
            "        {\n" +
            "            c.V0 = c.S3 + 0u;\n" +
            "            goto L80018C54;\n" +
            "        }\n" +
            "        c.V0 = m.ReadU32(c.A0);";
        if (!src.Contains(needle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: recursive child-link guard pattern not found");
            return src;
        }

        src = src.Replace(needle, fix, StringComparison.Ordinal);
        Console.WriteLine("[post-pass] applied recursive child-link pointer guard");
        return src;
    }

    static string FixOuterWalkerPointerGuards(string src)
    {
        if (src.Contains("Reject invalid func_80018DD0 child-list pointer", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string firstStartNeedle =
            "        L80018E08: ;\n" +
            "        if (c.A0 == 0u) {\n" +
            "            c.V0 = 0xFFFFFF01u;\n" +
            "            goto L80018E30;\n" +
            "        }";
        const string firstStartFix =
            "        L80018E08: ;\n" +
            "        // Reject invalid func_80018DD0 child-list pointer.\n" +
            "        if (c.A0 < 0x80010000u || c.A0 >= 0x80200000u || (c.A0 & 3u) != 0u) {\n" +
            "            c.V0 = 0xFFFFFF01u;\n" +
            "            goto L80018E30;\n" +
            "        }";
        const string firstLoopNeedle =
            "        c.A0 = c.S0 + 0u;\n" +
            "        if (c.A0 != 0u) {\n" +
            "            c.V0 = 0xFFFFFF01u;\n" +
            "            goto L80018E14;\n" +
            "        }";
        const string firstLoopFix =
            "        c.A0 = c.S0 + 0u;\n" +
            "        if (c.A0 >= 0x80010000u && c.A0 < 0x80200000u && (c.A0 & 3u) == 0u) {\n" +
            "            c.V0 = 0xFFFFFF01u;\n" +
            "            goto L80018E14;\n" +
            "        }";
        const string secondStartNeedle =
            "        L80018E94: ;\n" +
            "        if (c.A0 == 0u) {\n" +
            "            c.V0 = 0xFFFFFF01u;\n" +
            "            goto L80018EE4;\n" +
            "        }";
        const string secondStartFix =
            "        L80018E94: ;\n" +
            "        if (c.A0 < 0x80010000u || c.A0 >= 0x80200000u || (c.A0 & 3u) != 0u) {\n" +
            "            c.V0 = 0xFFFFFF01u;\n" +
            "            goto L80018EE4;\n" +
            "        }";
        const string secondLoopNeedle =
            "        L80018EDC: ;\n" +
            "        if (c.A0 != 0u) {\n" +
            "            c.V0 = 0xFFFFFF01u;\n" +
            "            goto L80018EA8;\n" +
            "        }";
        const string secondLoopFix =
            "        L80018EDC: ;\n" +
            "        if (c.A0 >= 0x80010000u && c.A0 < 0x80200000u && (c.A0 & 3u) == 0u) {\n" +
            "            c.V0 = 0xFFFFFF01u;\n" +
            "            goto L80018EA8;\n" +
            "        }";
        if (!src.Contains(firstStartNeedle, StringComparison.Ordinal) ||
            !src.Contains(firstLoopNeedle, StringComparison.Ordinal) ||
            !src.Contains(secondStartNeedle, StringComparison.Ordinal) ||
            !src.Contains(secondLoopNeedle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: outer walker pointer-guard patterns not found");
            return src;
        }

        src = src.Replace(firstStartNeedle, firstStartFix, StringComparison.Ordinal);
        src = src.Replace(firstLoopNeedle, firstLoopFix, StringComparison.Ordinal);
        src = src.Replace(secondStartNeedle, secondStartFix, StringComparison.Ordinal);
        src = src.Replace(secondLoopNeedle, secondLoopFix, StringComparison.Ordinal);
        Console.WriteLine("[post-pass] applied outer object-walker pointer guards");
        return src;
    }

    /// <summary>
    /// func_80018F0C invokes the object walker for eight object groups. The mixed
    /// recompiled/native call chain can leak S0-S4, so keep the dispatch target,
    /// handler, argument, group cursor and loop index in managed locals.
    /// </summary>
    static string FixObjectTraversalLoopState(string src)
    {
        if (src.Contains("Stable func_80018F0C traversal loop state", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string localsNeedle =
            "        c.S0 = 0x80070000u;\n" +
            "        c.S0 = c.S0 - 0x3250u;\n" +
            "        m.WriteU32((c.SP + 0x24u), c.RA);\n" +
            "        L80018F40: ;";
        const string localsFix =
            "        c.S0 = 0x80070000u;\n" +
            "        c.S0 = c.S0 - 0x3250u;\n" +
            "        m.WriteU32((c.SP + 0x24u), c.RA);\n" +
            "        // Stable func_80018F0C traversal loop state in managed locals.\n" +
            "        uint stableTraversalFunction = c.S2;\n" +
            "        uint stableTraversalHandler = c.S3;\n" +
            "        uint stableTraversalArgument = c.S4;\n" +
            "        uint stableTraversalEntry = 0u;\n" +
            "        uint stableTraversalIndex = 0u;\n" +
            "        L80018F40: ;";
        if (!src.Contains(localsNeedle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: func_80018F0C loop locals point not found");
            return src;
        }
        src = src.Replace(localsNeedle, localsFix, StringComparison.Ordinal);

        const string callNeedle =
            "        c.A0 = c.S0 + 0u;\n" +
            "        c.A1 = c.S3 + 0u;\n" +
            "        c.A2 = c.S4 + 0u;\n" +
            "        c.RA = 0x80018F50u;\n" +
            "        Dispatcher.Call(c, m, c.S2);\n" +
            "        c.S1 = c.S1 + 0x1u;";
        const string callFix =
            "        stableTraversalEntry = c.S0;\n" +
            "        stableTraversalIndex = c.S1;\n" +
            "        c.A0 = stableTraversalEntry;\n" +
            "        c.A1 = stableTraversalHandler;\n" +
            "        c.A2 = stableTraversalArgument;\n" +
            "        c.RA = 0x80018F50u;\n" +
            "        Dispatcher.Call(c, m, stableTraversalFunction);\n" +
            "        c.S0 = stableTraversalEntry;\n" +
            "        c.S1 = stableTraversalIndex;\n" +
            "        c.S2 = stableTraversalFunction;\n" +
            "        c.S3 = stableTraversalHandler;\n" +
            "        c.S4 = stableTraversalArgument;\n" +
            "        c.S1 = c.S1 + 0x1u;";
        if (!src.Contains(callNeedle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: func_80018F0C loop restore point not found");
            return src;
        }
        src = src.Replace(callNeedle, callFix, StringComparison.Ordinal);
        Console.WriteLine("[post-pass] stabilized func_80018F0C traversal loop state");
        return src;
    }

    /// <summary>
    /// Intro's steady-state loop can stop pumping host-side async work. Diagnostic
    /// file I/O masked this by yielding naturally; make that scheduling point explicit.
    /// </summary>
    static string FixIntroHostYield(string src)
    {
        if (src.Contains("Yield host work during Intro", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string needle =
            "        c.RA = 0x80011B68u;\n" +
            "        CrashBandicoot2.func_80026F14(c, m);\n" +
            "        L80011B68: ;";
        const string fix =
            "        c.RA = 0x80011B68u;\n" +
            "        CrashBandicoot2.func_80026F14(c, m);\n" +
            "        // Yield host work during Intro; this path otherwise starves async events.\n" +
            "        if (m.ReadU32(0x8005F684u) == 0x1Cu)\n" +
            "            System.Threading.Thread.Sleep(10);\n" +
            "        L80011B68: ;";
        if (!src.Contains(needle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: Intro host-yield point not found");
            return src;
        }
        src = src.Replace(needle, fix, StringComparison.Ordinal);
        Console.WriteLine("[post-pass] applied Intro host yield");
        return src;
    }

    static string InjectIntroMainLoopStepTrace(string src)
    {
        if (src.Contains("intro main step A", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);
        (string needle, string replacement)[] sites =
        [
            ("        c.RA = 0x800119A8u;\n        CrashBandicoot2.func_80011E80(c, m);",
             "        if (m.ReadU32(0x8005F684u) == 0x1Cu) RecompOne.Runtime.Diagnostics.BootLog.Write(\"intro main step L before 11E80\");\n        c.RA = 0x800119A8u;\n        CrashBandicoot2.func_80011E80(c, m);\n        if (m.ReadU32(0x8005F684u) == 0x1Cu) RecompOne.Runtime.Diagnostics.BootLog.Write(\"intro main step M after 11E80\");"),
            ("        c.RA = 0x800119E0u;\n        CrashBandicoot2.func_800300D8(c, m);",
             "        c.RA = 0x800119E0u;\n        CrashBandicoot2.func_800300D8(c, m);\n        if (m.ReadU32(0x8005F684u) == 0x1Cu) RecompOne.Runtime.Diagnostics.BootLog.Write(\"intro main step N after 300D8\");"),
            ("        c.RA = 0x80011A8Cu;\n        CrashBandicoot2.func_8001D360(c, m);",
             "        c.RA = 0x80011A8Cu;\n        CrashBandicoot2.func_8001D360(c, m);\n        if (m.ReadU32(0x8005F684u) == 0x1Cu) RecompOne.Runtime.Diagnostics.BootLog.Write(\"intro main step O after 1D360\");"),
            ("        c.RA = 0x80011B50u;\n        CrashBandicoot2.func_80012C88(c, m);",
             "        c.RA = 0x80011B50u;\n        CrashBandicoot2.func_80012C88(c, m);\n        if (m.ReadU32(0x8005F684u) == 0x1Cu) RecompOne.Runtime.Diagnostics.BootLog.Write(\"intro main step P after 12C88\");"),
            ("        c.RA = 0x80011B68u;\n        CrashBandicoot2.func_80026F14(c, m);",
             "        c.RA = 0x80011B68u;\n        CrashBandicoot2.func_80026F14(c, m);\n        if (m.ReadU32(0x8005F684u) == 0x1Cu) RecompOne.Runtime.Diagnostics.BootLog.Write(\"intro main step Q after 26F14\");"),
            ("        c.RA = 0x80011B70u;\n        CrashBandicoot2.func_80017CE8(c, m);",
             "        c.RA = 0x80011B70u;\n        CrashBandicoot2.func_80017CE8(c, m);\n        if (m.ReadU32(0x8005F684u) == 0x1Cu) RecompOne.Runtime.Diagnostics.BootLog.Write(\"intro main step R after 17CE8\");"),
            ("        c.RA = 0x80011BBCu;\n        CrashBandicoot2.func_8004F1C0(c, m);",
             "        c.RA = 0x80011BBCu;\n        CrashBandicoot2.func_8004F1C0(c, m);\n        if (m.ReadU32(0x8005F684u) == 0x1Cu) RecompOne.Runtime.Diagnostics.BootLog.Write(\"intro main step S after 4F1C0\");"),
            ("        c.RA = 0x80011C90u;\n        CrashBandicoot2.func_8002FE90(c, m);",
             "        c.RA = 0x80011C90u;\n        CrashBandicoot2.func_8002FE90(c, m);\n        if (m.ReadU32(0x8005F684u) == 0x1Cu) RecompOne.Runtime.Diagnostics.BootLog.Write(\"intro main step T after 2FE90\");"),
            ("        c.RA = 0x80011CB8u;\n        CrashBandicoot2.func_800420F4(c, m);",
             "        c.RA = 0x80011CB8u;\n        CrashBandicoot2.func_800420F4(c, m);\n        if (m.ReadU32(0x8005F684u) == 0x1Cu) RecompOne.Runtime.Diagnostics.BootLog.Write(\"intro main step U after 420F4\");"),
            ("        c.RA = 0x80011CDCu;\n        CrashBandicoot2.func_8003BEF4(c, m);",
             "        c.RA = 0x80011CDCu;\n        CrashBandicoot2.func_8003BEF4(c, m);\n        if (m.ReadU32(0x8005F684u) == 0x1Cu) RecompOne.Runtime.Diagnostics.BootLog.Write(\"intro main step V after 3BEF4\");"),
            ("        c.A0 = c.S0 + 0u;\n        c.RA = 0x80026FD4u;\n        CrashBandicoot2.func_80025F8C(c, m);",
             "        c.A0 = c.S0 + 0u;\n        RecompOne.Runtime.Diagnostics.BootLog.Write($\"intro mode W s0=0x{c.S0:X8} flags=0x{m.ReadU32(0x80062D70u):X8} state={(sbyte)m.ReadU8(0x8005BC2Cu)} b88=0x{m.ReadU32(0x80060B88u):X8}\");\n        c.RA = 0x80026FD4u;\n        CrashBandicoot2.func_80025F8C(c, m);\n        RecompOne.Runtime.Diagnostics.BootLog.Write(\"intro mode X after 25F8C\");"),
            ("        c.RA = 0x80026FDCu;\n        CrashBandicoot2.func_80023A6C(c, m);",
             "        c.RA = 0x80026FDCu;\n        CrashBandicoot2.func_80023A6C(c, m);\n        RecompOne.Runtime.Diagnostics.BootLog.Write(\"intro mode Y after 23A6C\");"),
            ("        c.RA = 0x80026FE4u;\n        CrashBandicoot2.func_80026310(c, m);",
             "        c.RA = 0x80026FE4u;\n        CrashBandicoot2.func_80026310(c, m);\n        RecompOne.Runtime.Diagnostics.BootLog.Write(\"intro mode Z after 26310\");"),
            ("        c.V0 = m.ReadU32((c.At + 0x480u));\n        // SCES-00967: game-mode jump table",
             "        c.V0 = m.ReadU32((c.At + 0x480u));\n        RecompOne.Runtime.Diagnostics.BootLog.Write($\"intro mode JT index=0x{c.V1:X8} target=0x{c.V0:X8}\");\n        // SCES-00967: game-mode jump table"),
            ("        c.RA = 0x80011D8Cu;\n        CrashBandicoot2.func_8001C3D4(c, m);",
             "        if (m.ReadU32(0x8005F684u) == 0x1Cu) RecompOne.Runtime.Diagnostics.BootLog.Write(\"intro main step A before 1C3D4\");\n        c.RA = 0x80011D8Cu;\n        CrashBandicoot2.func_8001C3D4(c, m);\n        if (m.ReadU32(0x8005F684u) == 0x1Cu) RecompOne.Runtime.Diagnostics.BootLog.Write(\"intro main step B after 1C3D4\");"),
            ("        c.RA = 0x80011D94u;\n        CrashBandicoot2.func_80011E88(c, m);",
             "        c.RA = 0x80011D94u;\n        CrashBandicoot2.func_80011E88(c, m);\n        if (m.ReadU32(0x8005F684u) == 0x1Cu) RecompOne.Runtime.Diagnostics.BootLog.Write(\"intro main step C after 11E88\");"),
            ("        c.RA = 0x80011DB4u;\n        CrashBandicoot2.func_8003DE2C(c, m);",
             "        c.RA = 0x80011DB4u;\n        CrashBandicoot2.func_8003DE2C(c, m);\n        if (m.ReadU32(0x8005F684u) == 0x1Cu) RecompOne.Runtime.Diagnostics.BootLog.Write(\"intro main step D after 3DE2C\");"),
            ("        c.RA = 0x80011DBCu;\n        CrashBandicoot2.func_8001658C(c, m);",
             "        c.RA = 0x80011DBCu;\n        CrashBandicoot2.func_8001658C(c, m);\n        if (m.ReadU32(0x8005F684u) == 0x1Cu) RecompOne.Runtime.Diagnostics.BootLog.Write(\"intro main step E after 1658C\");"),
            ("        c.RA = 0x80011DC4u;\n        CrashBandicoot2.func_80011E90(c, m);",
             "        if (m.ReadU32(0x8005F684u) == 0x1Cu) RecompOne.Runtime.Diagnostics.BootLog.Write(\"intro main step F before 11E90\");\n        c.RA = 0x80011DC4u;\n        CrashBandicoot2.func_80011E90(c, m);\n        if (m.ReadU32(0x8005F684u) == 0x1Cu) RecompOne.Runtime.Diagnostics.BootLog.Write(\"intro main step G after 11E90\");"),
            ("        c.V0 = m.ReadU32((c.V0 - 0xB98u));\n        if (c.V0 == 0u) {",
             "        c.V0 = m.ReadU32((c.V0 - 0xB98u));\n        if (m.ReadU32(0x8005F684u) == 0x1Cu) RecompOne.Runtime.Diagnostics.BootLog.Write($\"intro main step H tailFlag=0x{c.V0:X8}\");\n        if (c.V0 == 0u) {"),
            ("        c.RA = 0x80011DE8u;\n        CrashBandicoot2.func_8001658C(c, m);",
             "        c.RA = 0x80011DE8u;\n        CrashBandicoot2.func_8001658C(c, m);\n        if (m.ReadU32(0x8005F684u) == 0x1Cu) RecompOne.Runtime.Diagnostics.BootLog.Write(\"intro main step I after tail 1658C-1\");"),
            ("        c.RA = 0x80011DF0u;\n        CrashBandicoot2.func_8001658C(c, m);",
             "        c.RA = 0x80011DF0u;\n        CrashBandicoot2.func_8001658C(c, m);\n        if (m.ReadU32(0x8005F684u) == 0x1Cu) RecompOne.Runtime.Diagnostics.BootLog.Write(\"intro main step J after tail 1658C-2\");"),
            ("        c.RA = 0x80011E00u;\n        CrashBandicoot2.func_80015340(c, m);",
             "        c.RA = 0x80011E00u;\n        CrashBandicoot2.func_80015340(c, m);\n        if (m.ReadU32(0x8005F684u) == 0x1Cu) RecompOne.Runtime.Diagnostics.BootLog.Write(\"intro main step K after tail 15340\");")
        ];
        foreach (var (needle, replacement) in sites)
        {
            if (!src.Contains(needle, StringComparison.Ordinal))
            {
                Console.WriteLine("[post-pass] warning: Intro main-loop trace site not found");
                return src;
            }
            src = src.Replace(needle, replacement, StringComparison.Ordinal);
        }
        Console.WriteLine("[post-pass] injected Intro main-loop step trace");
        return src;
    }

    static string InjectIntroMode2Trace(string src)
    {
        if (src.Contains("intro mode2 after 324E0a", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string startMarker = "    public static void func_80022AD4(CpuContext c, IMemory m)";
        const string endMarker = "    public static void func_80022CD0(CpuContext c, IMemory m)";
        int start = src.IndexOf(startMarker, StringComparison.Ordinal);
        int end = start < 0 ? -1 : src.IndexOf(endMarker, start, StringComparison.Ordinal);
        if (start < 0 || end < 0)
        {
            Console.WriteLine("[post-pass] warning: Intro mode-2 trace function not found");
            return src;
        }

        string block = src[start..end];
        (string ra, string callee, string tag)[] calls =
        [
            ("80022B74", "func_800324E0", "324E0a"),
            ("80022BF8", "func_800244DC", "244DCa"),
            ("80022C10", "func_800269D8", "269D8"),
            ("80022C4C", "func_800324E0", "324E0b"),
            ("80022C60", "func_800244DC", "244DCb"),
            ("80022C78", "func_800232F8", "232F8"),
            ("80022C9C", "func_80020304", "20304"),
            ("80022CA8", "func_800226FC", "226FC"),
            ("80022CB0", "func_80023424", "23424")
        ];
        foreach (var (ra, callee, tag) in calls)
        {
            string needle =
                $"        c.RA = 0x{ra}u;\n" +
                $"        CrashBandicoot2.{callee}(c, m);";
            int pos = block.IndexOf(needle, StringComparison.Ordinal);
            if (pos < 0) continue;
            string replacement =
                $"        RecompOne.Runtime.Diagnostics.BootLog.Write(\"intro mode2 before {tag}\");\n" +
                needle + "\n" +
                $"        RecompOne.Runtime.Diagnostics.BootLog.Write(\"intro mode2 after {tag}\");";
            block = block.Remove(pos, needle.Length).Insert(pos, replacement);
        }
        src = src[..start] + block + src[end..];
        Console.WriteLine("[post-pass] injected Intro mode-2 call trace");
        return src;
    }

    static string Inject20304Trace(string src)
    {
        if (src.Contains("20304 checkpoint entry", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string startMarker = "    public static void func_80020304(CpuContext c, IMemory m)";
        const string endMarker = "    public static void func_80020BF4(CpuContext c, IMemory m)";
        int start = src.IndexOf(startMarker, StringComparison.Ordinal);
        int end = start < 0 ? -1 : src.IndexOf(endMarker, start, StringComparison.Ordinal);
        if (start < 0 || end < 0)
        {
            Console.WriteLine("[post-pass] warning: func_80020304 trace function not found");
            return src;
        }

        string block = src[start..end];
        block = block.Replace(
            "    {\n        c.SP = c.SP - 0xA0u;",
            "    {\n        RecompOne.Runtime.Diagnostics.BootLog.Write($\"20304 checkpoint entry a0=0x{c.A0:X8} a1=0x{c.A1:X8} a2=0x{c.A2:X8} a3=0x{c.A3:X8}\");\n        c.SP = c.SP - 0xA0u;",
            StringComparison.Ordinal);
        string[] labels = ["L80020430", "L80020550", "L800206D8", "L80020790", "L800207D4", "L80020978", "L80020A84", "L80020AF4", "L80020BC0"];
        foreach (string label in labels)
        {
            string needle = $"        {label}: ;";
            string replacement = needle + $"\n        RecompOne.Runtime.Diagnostics.BootLog.Write($\"20304 checkpoint {label} s1=0x{{c.S1:X8}} s2=0x{{c.S2:X8}} s6=0x{{c.S6:X8}} s7=0x{{c.S7:X8}} fp=0x{{c.FP:X8}}\");";
            block = block.Replace(needle, replacement, StringComparison.Ordinal);
        }
        (string ra, string callee, string tag)[] loopCalls =
        [
            ("80020714", "func_80029A74", "20714-29A74"),
            ("8002072C", "func_80021A54", "2072C-21A54"),
            ("8002075C", "func_80029A74", "2075C-29A74"),
            ("80020774", "func_80021A54", "20774-21A54"),
            ("80020788", "func_8001A274", "20788-1A274")
        ];
        foreach (var (ra, callee, tag) in loopCalls)
        {
            string needle =
                $"        c.RA = 0x{ra}u;\n" +
                $"        CrashBandicoot2.{callee}(c, m);";
            string replacement =
                $"        RecompOne.Runtime.Diagnostics.BootLog.Write($\"20304 before {tag} a0=0x{{c.A0:X8}} a1=0x{{c.A1:X8}} a2=0x{{c.A2:X8}}\");\n" +
                needle + "\n" +
                $"        RecompOne.Runtime.Diagnostics.BootLog.Write(\"20304 after {tag}\");";
            block = block.Replace(needle, replacement, StringComparison.Ordinal);
        }
        src = src[..start] + block + src[end..];
        Console.WriteLine("[post-pass] injected func_80020304 checkpoints");
        return src;
    }

    /// <summary>
    /// Robust companion to <see cref="FixGoolTableRestoreAfter1A040"/>. The common
    /// Intro frame path reaches <c>func_80018F0C</c> with scratchpad+0x5C still set to
    /// the matrix workspace (0x1F800060). Restore the opcode table at the call site so
    /// GOOL cannot dispatch a matrix word as a function pointer (seen: 0x800DCF48).
    /// </summary>
    static string FixGoolTableBefore1C3D4Reentry(string src)
    {
        if (src.Contains("Restore GOOL opcode table at 1C3D4 re-entry", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string needle =
            "        c.A2 = c.S0 + 0u;\n" +
            "        c.RA = 0x8001C454u;\n" +
            "        CrashBandicoot2.func_80018F0C(c, m);";
        const string fix =
            "        c.A2 = c.S0 + 0u;\n" +
            "        // Restore GOOL opcode table at 1C3D4 re-entry; 1A040 left matrix scratch here.\n" +
            "        c.V0 = 0x80060000u;\n" +
            "        c.V0 = c.V0 - 0x3854u;\n" +
            "        c.At = 0x1F800000u;\n" +
            "        m.WriteU32((c.At + 0x5Cu), c.V0);\n" +
            "        c.RA = 0x8001C454u;\n" +
            "        CrashBandicoot2.func_80018F0C(c, m);";
        if (!src.Contains(needle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: GOOL table 1C3D4 re-entry pattern not found");
            return src;
        }

        src = src.Replace(needle, fix, StringComparison.Ordinal);
        Console.WriteLine("[post-pass] restored GOOL table before func_80018F0C in func_8001C3D4");
        return src;
    }

    /// <summary>
    /// After Intro draw, mid-entry helpers can clobber <c>func_80011800</c>'s
    /// <c>S3=-1</c> sentinel so <c>mode != S3</c> spuriously re-enters level reload
    /// with <c>A1=mode=-1</c> → heap walk at garbage (<c>unmapped address: 0x3E4F1BDC</c>).
    /// Skip that reload while level is already Intro.
    /// </summary>
    static string FixIntroModeReloadGuard(string src)
    {
        if (src.Contains("Skip bogus Intro level-reload", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);

        const string needle =
            "        c.A0 = 0x80060000u;\n" +
            "        c.A0 = c.A0 + 0x7820u;\n" +
            "        c.A1 = c.S1 + 0u;\n" +
            "        c.RA = 0x80011B18u;\n" +
            "        CrashBandicoot2.func_80014D6C(c, m);";
        const string fix =
            "        c.A0 = 0x80060000u;\n" +
            "        c.A0 = c.A0 + 0x7820u;\n" +
            "        c.A1 = c.S1 + 0u;\n" +
            "        c.RA = 0x80011B18u;\n" +
            "        // Skip bogus Intro level-reload when S3 sentinel was clobbered by draw.\n" +
            "        if (!(c.A1 == 0xFFFFFFFFu && m.ReadU32(0x8005F684u) == 0x1Cu))\n" +
            "            CrashBandicoot2.func_80014D6C(c, m);";
        if (!src.Contains(needle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: Intro mode-reload guard pattern not found");
            return src;
        }

        src = src.Replace(needle, fix, StringComparison.Ordinal);
        Console.WriteLine("[post-pass] applied Intro mode-reload guard (skip A1=-1 while level=Intro)");
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
    /// Intro sequence/event jump table @ 0x80010468 inside func_80026700 (6 entries).
    /// Retail uses a <c>jr</c> into mid-function cases; recomp emitted Dispatcher.Call+return,
    /// which exits early before the intro path stores follow-up state in 0x80060B98/0x80060B9C.
    /// </summary>
    static string FixIntroSeqJumpTable(string src)
    {
        if (src.Contains("L8002678C:", StringComparison.Ordinal))
            return src;

        const string needle =
            """
                    c.V0 = m.ReadU32((c.At + 0x468u));
                    Dispatcher.Call(c, m, c.V0);
                    return;
                    c.A0 = (uint)(sbyte)m.ReadU8((c.S2 + 0x1u));
                    c.V0 = 0x80060000u;
                    c.V0 = m.ReadU32((c.V0 + 0xB94u));
                    if (c.V0 == c.A0) {
                        goto L800269B4;
                    }
            """;

        const string replacement =
            """
                    c.V0 = m.ReadU32((c.At + 0x468u));
                    // SCES-00967: intro sequence jump table @ 0x80010468 → mid-function cases
                    switch (c.V0)
                    {
                        case 0x800267D0u: goto L800267D0;
                        case 0x8002678Cu: goto L8002678C;
                        case 0x800267F0u: goto L800267F0;
                        case 0x8002675Cu: goto L8002675C;
                        case 0x80026990u: goto L80026990;
                        case 0x800267BCu: goto L800267BC;
                        default:
                            Dispatcher.Call(c, m, c.V0);
                            return;
                    }
                    L8002675C: ;
                    c.A0 = (uint)(sbyte)m.ReadU8((c.S2 + 0x1u));
                    c.V0 = 0x80060000u;
                    c.V0 = m.ReadU32((c.V0 + 0xB94u));
                    if (c.V0 == c.A0) {
                        goto L800269B4;
                    }
            """;

        if (!src.Contains(needle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: intro sequence jump table pattern not found");
            return src;
        }

        src = src.Replace(needle, replacement, StringComparison.Ordinal);

        src = ReplaceOnce(src,
            """
                    goto L800269B4;
                    c.A0 = m.ReadU8((c.S2 + 0x1u));
                    c.V0 = 0u | 0x0001u;
                    if (c.S3 != c.V0) {
                        goto L800267AC;
                    }
            """,
            """
                    goto L800269B4;
                    L8002678C: ;
                    c.A0 = m.ReadU8((c.S2 + 0x1u));
                    c.V0 = 0u | 0x0001u;
                    if (c.S3 != c.V0) {
                        goto L800267AC;
                    }
            """);

        src = ReplaceOnce(src,
            """
                    goto L800269B4;
                    c.V0 = 0u | 0x0001u;
                    c.At = 0x80060000u;
                    m.WriteU8((c.At - 0x43CDu), (byte)c.V0);
                    goto L800269B4;
                    c.A0 = 0x80060000u;
                    c.A0 = c.A0 - 0x43D4u;
            """,
            """
                    goto L800269B4;
                    L800267BC: ;
                    c.V0 = 0u | 0x0001u;
                    c.At = 0x80060000u;
                    m.WriteU8((c.At - 0x43CDu), (byte)c.V0);
                    goto L800269B4;
                    L800267D0: ;
                    c.A0 = 0x80060000u;
                    c.A0 = c.A0 - 0x43D4u;
            """);

        src = ReplaceOnce(src,
            """
                    goto L8002680C;
                    c.A0 = 0x80060000u;
                    c.A0 = c.A0 - 0x43D4u;
                    c.V1 = (uint)(sbyte)m.ReadU8(c.A0);
                    c.V0 = 0u | 0x0006u;
                    if (c.V1 == c.V0) {
                        c.V0 = 0u | 0x0006u;
                        goto L800269B4;
                    }
            """,
            """
                    goto L8002680C;
                    L800267F0: ;
                    c.A0 = 0x80060000u;
                    c.A0 = c.A0 - 0x43D4u;
                    c.V1 = (uint)(sbyte)m.ReadU8(c.A0);
                    c.V0 = 0u | 0x0006u;
                    if (c.V1 == c.V0) {
                        c.V0 = 0u | 0x0006u;
                        goto L800269B4;
                    }
            """);

        src = ReplaceOnce(src,
            """
                    goto L800269B4;
                    c.A0 = 0x80060000u;
                    c.A0 = c.A0 - 0x43D4u;
                    c.V1 = (uint)(sbyte)m.ReadU8(c.A0);
                    c.V0 = 0u | 0x0009u;
                    if (c.V1 == c.V0) {
                        c.V0 = 0u | 0x0009u;
                        goto L800269B4;
                    }
            """,
            """
                    goto L800269B4;
                    L80026990: ;
                    c.A0 = 0x80060000u;
                    c.A0 = c.A0 - 0x43D4u;
                    c.V1 = (uint)(sbyte)m.ReadU8(c.A0);
                    c.V0 = 0u | 0x0009u;
                    if (c.V1 == c.V0) {
                        c.V0 = 0u | 0x0009u;
                        goto L800269B4;
                    }
            """);

        Console.WriteLine("[post-pass] applied intro sequence jump table fix (func_80026700)");
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

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);

        const string needleB0 =
            "        c.A3 = (int)c.At < 2 ? 1u : 0u;\n" +
            "        if (c.A3 == 0u) {\n" +
            "            Dispatcher.Call(c, m, 0x8003B910u);\n" +
            "            return;\n" +
            "        }";

        const string fixB0 =
            "        c.A3 = (int)c.At < 2 ? 1u : 0u;\n" +
            "        if (c.A3 == 0u) {\n" +
            "            // Shared bulk-copy tail @ 0x8003B910 lives in func_8003B4E4; branch was\n" +
            "            // emitted as Dispatcher.Call+return (wrong). Inline halfword copy + continue.\n" +
            "            c.V0 = c.V0 + c.At;\n" +
            "            c.V1 = c.V1 + c.At;\n" +
            "            {\n" +
            "                uint n = c.At;\n" +
            "                while (n != 0u)\n" +
            "                {\n" +
            "                    ushort h = m.ReadU16(c.T6);\n" +
            "                    c.T6 = c.T6 + 0x2u;\n" +
            "                    m.WriteU16(c.S1, h);\n" +
            "                    c.S1 = c.S1 + 0x2u;\n" +
            "                    n--;\n" +
            "                }\n" +
            "            }\n" +
            "            goto L8003B38C;\n" +
            "        }";

        const string needleB4 =
            "        c.A3 = (int)c.At < 2 ? 1u : 0u;\n" +
            "        if (c.A3 == 0u) {\n" +
            "            goto L8003B910;\n" +
            "        }";

        const string fixB4 =
            "        c.A3 = (int)c.At < 2 ? 1u : 0u;\n" +
            "        if (c.A3 == 0u) {\n" +
            "            // Bypass broken Duff-device tail @ L8003B910 (jr into mid-function + empty stubs).\n" +
            "            c.V0 = c.V0 + c.At;\n" +
            "            c.V1 = c.V1 + c.At;\n" +
            "            {\n" +
            "                uint n = c.At;\n" +
            "                while (n != 0u)\n" +
            "                {\n" +
            "                    ushort h = m.ReadU16(c.T6);\n" +
            "                    c.T6 = c.T6 + 0x2u;\n" +
            "                    m.WriteU16(c.S1, h);\n" +
            "                    c.S1 = c.S1 + 0x2u;\n" +
            "                    n--;\n" +
            "                }\n" +
            "            }\n" +
            "            goto L8003B710;\n" +
            "        }";

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
    /// Object-property dispatch table @ 0x80010590 inside func_8003322C. The targets
    /// are mid-function cases sharing the function epilogue; treating them as ordinary
    /// calls returns before that epilogue and leaks the 0xC8-byte stack frame.
    /// </summary>
    static string FixObjectPropertyJumpTable(string src)
    {
        if (src.Contains("SCES-00967: object-property jump table", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string needle =
            "        c.V0 = m.ReadU32((c.At + 0x590u));\n" +
            "        Dispatcher.Call(c, m, c.V0);\n" +
            "        return;\n" +
            "        c.V0 = m.ReadU32(c.S0);\n" +
            "        c.V0 = c.V0 & 0x0004u;";
        const string replacement =
            "        c.V0 = m.ReadU32((c.At + 0x590u));\n" +
            "        // SCES-00967: object-property jump table @ 0x80010590 -> mid-function cases\n" +
            "        switch (c.V0)\n" +
            "        {\n" +
            "            case 0x800333ACu: goto L800333AC;\n" +
            "            case 0x80033454u: goto L80033454;\n" +
            "            case 0x80033514u: goto L80033514;\n" +
            "            case 0x8003353Cu: goto L8003353C;\n" +
            "            case 0x800335D4u: goto L800335D4;\n" +
            "            case 0x800335E0u: goto L800335E0;\n" +
            "            case 0x800335ECu: goto L800335EC;\n" +
            "            case 0x80033618u: goto L80033618;\n" +
            "            case 0x80033640u: goto L80033640;\n" +
            "            case 0x80033660u: goto L80033660;\n" +
            "            case 0x80033678u: goto L80033678;\n" +
            "            case 0x8003368Cu: goto L8003368C;\n" +
            "            case 0x800336A0u: goto L800336A0;\n" +
            "            case 0x800336BCu: goto L800336BC;\n" +
            "            case 0x800336E0u: goto L800336E0;\n" +
            "            default:\n" +
            "                Dispatcher.Call(c, m, c.V0);\n" +
            "                return;\n" +
            "        }\n" +
            "        L800333AC: ;\n" +
            "        c.V0 = m.ReadU32(c.S0);\n" +
            "        c.V0 = c.V0 & 0x0004u;";
        if (!src.Contains(needle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: object-property jump table pattern not found");
            return src;
        }
        src = src.Replace(needle, replacement, StringComparison.Ordinal);

        src = ReplaceOnce(src,
            "        c.A0 = c.S0 + 0x20u;\n        goto L800335B0;\n        c.V0 = m.ReadU32(c.S0);\n        c.V0 = c.V0 & 0x0004u;",
            "        c.A0 = c.S0 + 0x20u;\n        goto L800335B0;\n        L80033454: ;\n        c.V0 = m.ReadU32(c.S0);\n        c.V0 = c.V0 & 0x0004u;");
        src = ReplaceOnce(src,
            "        m.WriteU16((c.SP + 0x24u), (ushort)c.V0);\n        goto L800336EC;\n        c.V0 = m.ReadU32(c.S1);\n        c.V1 = m.ReadU32((c.S1 + 0x4u));",
            "        m.WriteU16((c.SP + 0x24u), (ushort)c.V0);\n        goto L800336EC;\n        L80033514: ;\n        c.V0 = m.ReadU32(c.S1);\n        c.V1 = m.ReadU32((c.S1 + 0x4u));");
        src = ReplaceOnce(src,
            "        c.RA = 0x8003353Cu;\n        CrashBandicoot2.func_8001EE74(c, m);\n        c.V0 = m.ReadU32(c.S1);",
            "        c.RA = 0x8003353Cu;\n        CrashBandicoot2.func_8001EE74(c, m);\n        L8003353C: ;\n        c.V0 = m.ReadU32(c.S1);");
        src = ReplaceOnce(src,
            "        m.WriteWordRight((c.SP + 0x18u), c.V1);\n        goto L800336EC;\n        c.V0 = m.ReadU32(c.S1);\n        m.WriteU8((c.S0 + 0x18u), (byte)c.V0);",
            "        m.WriteWordRight((c.SP + 0x18u), c.V1);\n        goto L800336EC;\n        L800335D4: ;\n        c.V0 = m.ReadU32(c.S1);\n        m.WriteU8((c.S0 + 0x18u), (byte)c.V0);");
        src = ReplaceOnce(src,
            "        m.WriteU8((c.S0 + 0x18u), (byte)c.V0);\n        goto L800336EC;\n        c.V0 = m.ReadU32(c.S1);\n        m.WriteU32((c.S0 + 0x8u), c.V0);",
            "        m.WriteU8((c.S0 + 0x18u), (byte)c.V0);\n        goto L800336EC;\n        L800335E0: ;\n        c.V0 = m.ReadU32(c.S1);\n        m.WriteU32((c.S0 + 0x8u), c.V0);");
        src = ReplaceOnce(src,
            "        m.WriteU32((c.S0 + 0x8u), c.V0);\n        goto L800336EC;\n        if (c.S1 == 0u) {",
            "        m.WriteU32((c.S0 + 0x8u), c.V0);\n        goto L800336EC;\n        L800335EC: ;\n        if (c.S1 == 0u) {");
        src = ReplaceOnce(src,
            "        c.V0 = c.V0 < 0x00000001u ? 1u : 0u;\n        goto L80033730;\n        if (c.S1 == 0u) {",
            "        c.V0 = c.V0 < 0x00000001u ? 1u : 0u;\n        goto L80033730;\n        L80033618: ;\n        if (c.S1 == 0u) {");
        src = ReplaceOnce(src,
            "        L8003362C: ;\n        c.V0 = m.ReadU32((c.S0 + 0x10u));\n        c.V1 = c.V1 | 0x347Fu;\n        c.V0 = c.V0 ^ c.V1;\n        c.V0 = 0u < c.V0 ? 1u : 0u;\n        goto L80033730;\n        c.V0 = m.ReadU32(c.S1);",
            "        L8003362C: ;\n        c.V0 = m.ReadU32((c.S0 + 0x10u));\n        c.V1 = c.V1 | 0x347Fu;\n        c.V0 = c.V0 ^ c.V1;\n        c.V0 = 0u < c.V0 ? 1u : 0u;\n        goto L80033730;\n        L80033640: ;\n        c.V0 = m.ReadU32(c.S1);");
        src = ReplaceOnce(src,
            "        goto L800336EC;\n        c.V0 = m.ReadU32(c.S0);\n        c.V1 = m.ReadU32(c.S1);\n        c.V0 = c.V0 | 0x0010u;",
            "        goto L800336EC;\n        L80033660: ;\n        c.V0 = m.ReadU32(c.S0);\n        c.V1 = m.ReadU32(c.S1);\n        c.V0 = c.V0 | 0x0010u;");
        src = ReplaceOnce(src,
            "        m.WriteU16((c.S0 + 0x1Cu), (ushort)c.V1);\n        m.WriteU32(c.S0, c.V0);\n        goto L800336EC;\n        c.V0 = m.ReadU32(c.S0);\n        c.V0 = c.V0 | 0x0200u;",
            "        m.WriteU16((c.S0 + 0x1Cu), (ushort)c.V1);\n        m.WriteU32(c.S0, c.V0);\n        goto L800336EC;\n        L80033678: ;\n        c.V0 = m.ReadU32(c.S0);\n        c.V0 = c.V0 | 0x0200u;");
        src = ReplaceOnce(src,
            "        c.V0 = c.V0 | 0x0200u;\n        m.WriteU32(c.S0, c.V0);\n        goto L800336EC;\n        c.V0 = m.ReadU32(c.S0);\n        c.V1 = 0xFFFFFDFFu;",
            "        c.V0 = c.V0 | 0x0200u;\n        m.WriteU32(c.S0, c.V0);\n        goto L800336EC;\n        L8003368C: ;\n        c.V0 = m.ReadU32(c.S0);\n        c.V1 = 0xFFFFFDFFu;");
        src = ReplaceOnce(src,
            "        c.V0 = c.V0 & c.V1;\n        m.WriteU32(c.S0, c.V0);\n        goto L800336EC;\n        c.A0 = m.ReadU32(c.S0);\n        c.V1 = m.ReadU32(c.S1);\n        c.V0 = 0xFFFFF7FFu;",
            "        c.V0 = c.V0 & c.V1;\n        m.WriteU32(c.S0, c.V0);\n        goto L800336EC;\n        L800336A0: ;\n        c.A0 = m.ReadU32(c.S0);\n        c.V1 = m.ReadU32(c.S1);\n        c.V0 = 0xFFFFF7FFu;");
        src = ReplaceOnce(src,
            "        c.V1 = c.V1 & 0x0800u;\n        goto L800336D4;\n        c.A0 = m.ReadU32(c.S0);\n        c.V1 = m.ReadU32(c.S1);\n        c.V0 = 0xFFFFFBFFu;",
            "        c.V1 = c.V1 & 0x0800u;\n        goto L800336D4;\n        L800336BC: ;\n        c.A0 = m.ReadU32(c.S0);\n        c.V1 = m.ReadU32(c.S1);\n        c.V0 = 0xFFFFFBFFu;");
        src = ReplaceOnce(src,
            "        m.WriteU32(c.S0, c.A0);\n        goto L800336EC;\n        c.V0 = m.ReadU32(c.S1);\n        m.WriteU8((c.S0 + 0x19u), (byte)c.V0);",
            "        m.WriteU32(c.S0, c.A0);\n        goto L800336EC;\n        L800336E0: ;\n        c.V0 = m.ReadU32(c.S1);\n        m.WriteU8((c.S0 + 0x19u), (byte)c.V0);");

        Console.WriteLine("[post-pass] applied object-property jump table fix (func_8003322C)");
        return src;
    }

    /// <summary>
    /// Resource interpolation jump table @ 0x80010544 inside func_80031DF4. Its six
    /// unique targets are cases inside the same function and share the epilogue at
    /// 0x800324D0. Dispatching them as calls returns early and leaks 8 bytes of stack.
    /// </summary>
    static string FixResourceInterpolationJumpTable(string src)
    {
        if (src.Contains("SCES-00967: resource interpolation jump table", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);

        const string dispatchNeedle =
            "        c.V0 = m.ReadU32((c.At + 0x544u));\n" +
            "        Dispatcher.Call(c, m, c.V0);\n" +
            "        return;\n" +
            "        c.V0 = c.A1 + 0u;";
        const string case32190Needle =
            "            goto L8003212C;\n" +
            "        }\n" +
            "        c.A3 = c.A3 + 0x1u;\n" +
            "        c.V0 = c.T9 + 0u;\n" +
            "        goto L800324D0;\n" +
            "        c.V0 = c.A1 + 0u;";
        const string case32204Needle =
            "            goto L800321A0;\n" +
            "        }\n" +
            "        c.A3 = c.A3 + 0x2u;\n" +
            "        c.V0 = c.T9 + 0u;\n" +
            "        goto L800324D0;\n" +
            "        c.V0 = c.A1 + 0u;";
        const string case3225CNeedle =
            "            goto L80032210;\n" +
            "        }\n" +
            "        c.A3 = c.A3 + 0x4u;\n" +
            "        c.V0 = c.T9 + 0u;\n" +
            "        goto L800324D0;\n" +
            "        c.V0 = c.A1 + 0u;";
        const string case32324Needle =
            "            goto L80032274;\n" +
            "        }\n" +
            "        c.T0 = c.T0 + 0x6u;\n" +
            "        c.V0 = c.T9 + 0u;\n" +
            "        goto L800324D0;\n" +
            "        c.V0 = c.A1 + 0u;";

        if (!src.Contains(dispatchNeedle, StringComparison.Ordinal) ||
            !src.Contains(case32190Needle, StringComparison.Ordinal) ||
            !src.Contains(case32204Needle, StringComparison.Ordinal) ||
            !src.Contains(case3225CNeedle, StringComparison.Ordinal) ||
            !src.Contains(case32324Needle, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: resource interpolation jump table pattern not found");
            return src;
        }

        const string dispatchReplacement =
            "        c.V0 = m.ReadU32((c.At + 0x544u));\n" +
            "        // SCES-00967: resource interpolation jump table @ 0x80010544 -> mid-function cases\n" +
            "        switch (c.V0)\n" +
            "        {\n" +
            "            case 0x8003211Cu: goto L8003211C;\n" +
            "            case 0x80032190u: goto L80032190;\n" +
            "            case 0x80032204u: goto L80032204;\n" +
            "            case 0x8003225Cu: goto L8003225C;\n" +
            "            case 0x80032324u: goto L80032324;\n" +
            "            case 0x80032424u: goto L80032424;\n" +
            "            default:\n" +
            "                Dispatcher.Call(c, m, c.V0);\n" +
            "                return;\n" +
            "        }\n" +
            "        L8003211C: ;\n" +
            "        c.V0 = c.A1 + 0u;";
        src = src.Replace(dispatchNeedle, dispatchReplacement, StringComparison.Ordinal);
        src = src.Replace(case32190Needle,
            case32190Needle[..^"        c.V0 = c.A1 + 0u;".Length] +
            "        L80032190: ;\n        c.V0 = c.A1 + 0u;", StringComparison.Ordinal);
        src = src.Replace(case32204Needle,
            case32204Needle[..^"        c.V0 = c.A1 + 0u;".Length] +
            "        L80032204: ;\n        c.V0 = c.A1 + 0u;", StringComparison.Ordinal);
        src = src.Replace(case3225CNeedle,
            case3225CNeedle[..^"        c.V0 = c.A1 + 0u;".Length] +
            "        L8003225C: ;\n        c.V0 = c.A1 + 0u;", StringComparison.Ordinal);
        src = src.Replace(case32324Needle,
            case32324Needle[..^"        c.V0 = c.A1 + 0u;".Length] +
            "        L80032324: ;\n        c.V0 = c.A1 + 0u;", StringComparison.Ordinal);

        Console.WriteLine("[post-pass] applied resource interpolation jump table fix (func_80031DF4)");
        return src;
    }

    /// <summary>
    /// The texture delta decompressors use scratchpad register saves and computed
    /// Duff-device tails. Running their original MIPS avoids C# control-flow splits
    /// that can stop making progress on the second Intro camera block.
    /// </summary>
    static string FixTexDecompUseInterpreter(string src)
    {
        if (src.Contains("MIPS texture decompressor 0x8003B0EC", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string startMarker = "    public static void func_80029A74(CpuContext c, IMemory m)";
        const string endMarker = "    public static void func_80029AF8(CpuContext c, IMemory m)";
        int start = src.IndexOf(startMarker, StringComparison.Ordinal);
        int end = start < 0 ? -1 : src.IndexOf(endMarker, start, StringComparison.Ordinal);
        if (start < 0 || end < 0)
        {
            Console.WriteLine("[post-pass] warning: texture decompressor caller not found");
            return src;
        }

        string block = src[start..end];
        const string callB0 = "        CrashBandicoot2.func_8003B0EC(c, m);";
        const string callB4 = "        CrashBandicoot2.func_8003B4E4(c, m);";
        const string interpB0 =
            "        // MIPS texture decompressor 0x8003B0EC (computed tails do not recompile safely).\n" +
            "        if (!RecompOne.Runtime.Sdk.LibGool.TryInterpretNative(c, m, 0x8003B0ECu))\n" +
            "            throw new InvalidOperationException(\"texture decompressor 0x8003B0EC failed\");";
        const string interpB4 =
            "        // MIPS texture decompressor 0x8003B4E4 (computed tails do not recompile safely).\n" +
            "        if (!RecompOne.Runtime.Sdk.LibGool.TryInterpretNative(c, m, 0x8003B4E4u))\n" +
            "            throw new InvalidOperationException(\"texture decompressor 0x8003B4E4 failed\");";
        if (!block.Contains(callB0, StringComparison.Ordinal) || !block.Contains(callB4, StringComparison.Ordinal))
        {
            Console.WriteLine("[post-pass] warning: texture decompressor direct calls not found");
            return src;
        }
        block = block.Replace(callB0, interpB0, StringComparison.Ordinal);
        block = block.Replace(callB4, interpB4, StringComparison.Ordinal);
        src = src[..start] + block + src[end..];
        Console.WriteLine("[post-pass] routed texture decompressors through MIPS interpreter");
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
        if (src.Contains("SCES-00967: targeted raster continuation 0x80042930", StringComparison.Ordinal))
            return src;

        src = src.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string firstNeedle =
            "        L800427D8: ;\n" +
            "        c.V0 = c.V0 + c.FP;\n" +
            "        Dispatcher.Call(c, m, c.A2);\n" +
            "        return;";
        const string firstFix =
            "        L800427D8: ;\n" +
            "        c.V0 = c.V0 + c.FP;\n" +
            "        // SCES-00967: targeted raster continuation 0x80042930 (first jalr)\n" +
            "        RecompOne.Runtime.Dispatch.RasterContinue.Begin();\n" +
            "        Dispatcher.Call(c, m, c.A2);\n" +
            "        {\n" +
            "            uint _rc = RecompOne.Runtime.Dispatch.RasterContinue.EndTake();\n" +
            "            if (_rc == 0x80042930u)\n" +
            "            {\n" +
            "                c.T9 = 0x09000000u;\n" +
            "                goto L80042BE0;\n" +
            "            }\n" +
            "        }\n" +
            "        return;";
        const string needle =
            "        L80042AA8: ;\n" +
            "        c.V0 = c.V0 + c.FP;\n" +
            "        Dispatcher.Call(c, m, c.A2);\n" +
            "        return;";
        const string fix =
            "        L80042AA8: ;\n" +
            "        c.V0 = c.V0 + c.FP;\n" +
            "        // SCES-00967: targeted raster continuation 0x80042930\n" +
            "        RecompOne.Runtime.Dispatch.RasterContinue.Begin();\n" +
            "        Dispatcher.Call(c, m, c.A2);\n" +
            "        {\n" +
            "            uint _rc = RecompOne.Runtime.Dispatch.RasterContinue.EndTake();\n" +
            "            if (_rc == 0x80042930u)\n" +
            "            {\n" +
            "                c.T9 = 0x09000000u;\n" +
            "                goto L80042BE0;\n" +
            "            }\n" +
            "        }\n" +
            "        return;";
        static string T9Needle(string label) =>
            "        Dispatcher.Call(c, m, c.T9);\n" +
            "        return;\n" +
            $"        {label}: ;";
        static string T9Fix(string label) =>
            "        // SCES-00967: T9 jalr can target raster continuation 0x80042930\n" +
            "        RecompOne.Runtime.Dispatch.RasterContinue.Begin();\n" +
            "        Dispatcher.Call(c, m, c.T9);\n" +
            "        {\n" +
            "            uint _rc = RecompOne.Runtime.Dispatch.RasterContinue.EndTake();\n" +
            "            if (_rc == 0x80042930u)\n" +
            "            {\n" +
            "                c.T9 = 0x09000000u;\n" +
            "                goto L80042BE0;\n" +
            "            }\n" +
            "        }\n" +
            "        return;\n" +
            $"        {label}: ;";
        var t9Labels = new[] { "L800423E4", "L80042428", "L8004254C", "L80042628" };
        bool missingT9Site = false;
        foreach (var label in t9Labels)
            missingT9Site |= !src.Contains(T9Needle(label), StringComparison.Ordinal);
        if (!src.Contains(firstNeedle, StringComparison.Ordinal) ||
            !src.Contains(needle, StringComparison.Ordinal) || missingT9Site)
        {
            Console.WriteLine("[post-pass] warning: targeted raster continuation pattern not found");
            return src;
        }
        src = src.Replace(firstNeedle, firstFix, StringComparison.Ordinal);
        src = src.Replace(needle, fix, StringComparison.Ordinal);
        foreach (var label in t9Labels)
            src = src.Replace(T9Needle(label), T9Fix(label), StringComparison.Ordinal);

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

        const string mapNeedle = "[0x800420F4u] = CrashBandicoot2.func_800420F4,";
        const string mapFix =
            "[0x800420F4u] = CrashBandicoot2.func_800420F4,\n" +
            "            [0x80042628u] = static (c, m) => RecompOne.Runtime.Dispatch.RasterContinue.Jump(0x80042628u),\n" +
            "            [0x800426B8u] = static (c, m) => RecompOne.Runtime.Dispatch.RasterContinue.Jump(0x800426B8u),\n" +
            "            [0x800427E0u] = static (c, m) => RecompOne.Runtime.Dispatch.RasterContinue.Jump(0x800427E0u),\n" +
            "            [0x80042938u] = static (c, m) => RecompOne.Runtime.Dispatch.RasterContinue.Jump(0x80042938u),\n" +
            "            [0x80042AB0u] = static (c, m) => RecompOne.Runtime.Dispatch.RasterContinue.Jump(0x80042AB0u),\n" +
            "            [0x80042BE0u] = static (c, m) => RecompOne.Runtime.Dispatch.RasterContinue.Jump(0x80042BE0u),";
        if (!src.Contains(mapNeedle, StringComparison.Ordinal))
            Console.WriteLine("[post-pass] warning: func_800420F4 dispatcher map entry not found");
        else
            src = src.Replace(mapNeedle, mapFix, StringComparison.Ordinal);

        Console.WriteLine("[post-pass] applied targeted raster continuation (0x800431A0 -> 0x80042930)");
        return src;
    }

    // Kept as reference while the remaining raster mid-entries are validated individually.
    static string FixPolyRasterContinuationsLegacy(string src)
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
            "        c.SP = c.SP + 0x44u;\n" +
            "        return;\n" +
            "        RecompOne.Runtime.Gte.Execute(0x4A280030u);",
            "        c.SP = c.SP + 0x44u;\n" +
            "        return;\n" +
            "        L800426B8: ;\n" +
            "        RecompOne.Runtime.Gte.Execute(0x4A280030u);");

        const string rasterCatch =
            "        {\n" +
            "            uint _rc = RecompOne.Runtime.Dispatch.RasterContinue.EndTake();\n" +
            "            if (_rc != 0u)\n" +
            "            {\n" +
            "                switch (_rc)\n" +
            "                {\n" +
            "                    case 0x80042628u: goto L80042628;\n" +
            "                    case 0x800426B8u: goto L800426B8;\n" +
            "                    case 0x800427E0u: goto L800427E0;\n" +
            "                    case 0x80042930u: c.T9 = 0x09000000u; goto L80042BE0;\n" +
            "                    case 0x80042938u: goto L80042938;\n" +
            "                    case 0x80042AB0u: goto L80042AB0;\n" +
            "                    case 0x80042BE0u: goto L80042BE0;\n" +
            "                    default: throw new InvalidOperationException($\"unhandled raster continue: 0x{_rc:X8}\");\n" +
            "                }\n" +
            "            }\n" +
            "        }\n";

        src = ReplaceOnce(src,
            "        L800427D8: ;\n" +
            "        c.V0 = c.V0 + c.FP;\n" +
            "        Dispatcher.Call(c, m, c.A2);\n" +
            "        return;\n" +
            "        c.T8 = c.S5 << 20;",
            "        L800427D8: ;\n" +
            "        c.V0 = c.V0 + c.FP;\n" +
            "        RecompOne.Runtime.Dispatch.RasterContinue.Begin();\n" +
            "        Dispatcher.Call(c, m, c.A2);\n" +
            rasterCatch +
            "        return;\n" +
            "        L800427E0: ;\n" +
            "        c.T8 = c.S5 << 20;");

        src = ReplaceOnce(src,
            "        if ((int)0u >= 0) {\n" +
            "            c.T9 = 0x09000000u;\n" +
            "            goto L80042BE0;\n" +
            "        }\n" +
            "        c.T9 = 0x09000000u;\n" +
            "        m.WriteU32((c.V1 + 0x1B4u), RecompOne.Runtime.Gte.StoreWord(0));",
            "        if ((int)0u >= 0) {\n" +
            "            c.T9 = 0x09000000u;\n" +
            "            goto L80042BE0;\n" +
            "        }\n" +
            "        L80042938: ;\n" +
            "        c.T9 = 0x09000000u;\n" +
            "        m.WriteU32((c.V1 + 0x1B4u), RecompOne.Runtime.Gte.StoreWord(0));");

        src = ReplaceOnce(src,
            "        L80042AA8: ;\n" +
            "        c.V0 = c.V0 + c.FP;\n" +
            "        Dispatcher.Call(c, m, c.A2);\n" +
            "        return;\n" +
            "        c.T8 = c.S5 << 20;",
            "        L80042AA8: ;\n" +
            "        c.V0 = c.V0 + c.FP;\n" +
            "        RecompOne.Runtime.Dispatch.RasterContinue.Begin();\n" +
            "        Dispatcher.Call(c, m, c.A2);\n" +
            rasterCatch +
            "        return;\n" +
            "        L80042AB0: ;\n" +
            "        c.T8 = c.S5 << 20;");

        // Wrap jalr-via-T9 exits in func_800420F4 (four sites, each followed by a known label).
        // Raw-string indent: content columns 20, closer at 12 → 8 spaces (matches main.cs).
        var t9Sites = new (string Label, string Needle)[]
        {
            ("L800423E4",
            "        Dispatcher.Call(c, m, c.T9);\n" +
            "        return;\n" +
            "        L800423E4: ;"),
            ("L80042428",
            "        Dispatcher.Call(c, m, c.T9);\n" +
            "        return;\n" +
            "        L80042428: ;"),
            ("L8004254C",
            "        Dispatcher.Call(c, m, c.T9);\n" +
            "        return;\n" +
            "        L8004254C: ;"),
            ("L80042628",
            "        Dispatcher.Call(c, m, c.T9);\n" +
            "        return;\n" +
            "        L80042628: ;"),
        };

        string t9Catch =
            "        RecompOne.Runtime.Dispatch.RasterContinue.Begin();\n" +
            "        Dispatcher.Call(c, m, c.T9);\n" +
            rasterCatch +
            "        return;\n";

        int t9Count = 0;
        foreach (var (label, needle) in t9Sites)
        {
            var fix = t9Catch + $"        {label}: ;";
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
                     "0x80042930u", "0x80042938u", "0x80042AB0u", "0x80042BE0u",
                 })
        {
            var callNeedle = $"Dispatcher.Call(c, m, {addr});";
            var jumpFix = $"RecompOne.Runtime.Dispatch.RasterContinue.Jump({addr});";
            if (src.Contains(callNeedle, StringComparison.Ordinal))
                src = src.Replace(callNeedle, jumpFix, StringComparison.Ordinal);
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
