namespace CrashBandicoot2.Launcher.Recomp;

/// <summary>
/// SCES-00967 first-boot fixes the recompiler does not yet emit.
/// Start with the CD status jump table in func_800473B8 (table @ 0x80010B20).
/// </summary>
public static class PostPassApplier
{
    public static void Apply(string mainCsPath)
    {
        if (!File.Exists(mainCsPath))
            throw new FileNotFoundException("Generated main.cs not found.", mainCsPath);

        var src = File.ReadAllText(mainCsPath);
        var patched = FixCdStatusJumpTable(src);
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
