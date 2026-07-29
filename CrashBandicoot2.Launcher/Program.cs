using CrashBandicoot2.Launcher.Recomp;
using RecompOne.Runtime;
using RecompOne.Runtime.Config;

namespace CrashBandicoot2.Launcher;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            Directory.SetCurrentDirectory(AppPaths.Root);
        }
        catch
        {
            // ignore
        }

        AppPaths.EnsureCreated();

        if (args.Length >= 1 && string.Equals(args[0], "--help", StringComparison.OrdinalIgnoreCase))
        {
            PrintHelp();
            return 0;
        }

        if (args.Length >= 2 && string.Equals(args[0], "--prepare", StringComparison.OrdinalIgnoreCase))
        {
            var cue = Path.GetFullPath(args[1]);
            Console.WriteLine($"[CrashBandicoot2] preparing from {cue}");
            var progress = new Progress<PipelineProgress>(p =>
                Console.WriteLine($"  [{p.Fraction * 100,3:0}%] {p.Stage}: {p.Detail}"));
            var dll = RecompPipeline.EnsureReady(cue, progress);
            Console.WriteLine($"[CrashBandicoot2] ready: {dll}");
            return 0;
        }

        if (args.Length >= 1 && string.Equals(args[0], "--run", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryResolveCue(args, out var cue, out var cueError))
            {
                Console.Error.WriteLine("[CrashBandicoot2] " + cueError);
                PrintHelp();
                return 1;
            }

            try
            {
                ConfigManager.Load();
                ConfigManager.Game.CdPath = cue;
                ConfigManager.SaveGame();
                var dll = RecompPipeline.EnsureReady(cue, new Progress<PipelineProgress>(p =>
                    Console.WriteLine($"  [{p.Fraction * 100,3:0}%] {p.Stage}: {p.Detail}")));
                Console.WriteLine("[CrashBandicoot2] launching " + dll);
                GameLoader.Run(dll, cue);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[CrashBandicoot2] FAIL: " + ex.GetBaseException().Message);
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        if (args.Length >= 1 && string.Equals(args[0], "--smoke", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryResolveCue(args, out var cue, out var cueError))
            {
                Console.Error.WriteLine("[smoke] " + cueError);
                PrintHelp();
                return 1;
            }

            try
            {
                ConfigManager.Load();
                ConfigManager.Game.CdPath = cue;
                ConfigManager.SaveGame();
                var dll = RecompPipeline.EnsureReady(cue);
                Console.WriteLine("[smoke] launching " + dll);
                var t = Task.Run(() => GameLoader.Run(dll, cue));
                if (!t.Wait(TimeSpan.FromSeconds(15)))
                {
                    Console.WriteLine("[smoke] still running after 15s — OK (window likely open)");
                    return 0;
                }
                if (t.IsFaulted)
                    throw t.Exception!.GetBaseException();
                Console.WriteLine("[smoke] Entry.Run returned");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[smoke] FAIL: " + ex.GetBaseException().Message);
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        PrintHelp();
        return args.Length == 0 ? 0 : 1;
    }

    static void PrintHelp()
    {
        Console.WriteLine("Crash Bandicoot 2: Recompiled (CLI)");
        Console.WriteLine("  --prepare <file.cue>   prepare game folder without running");
        Console.WriteLine("  --run <file.cue>       prepare (if needed) and play");
        Console.WriteLine("  --smoke <file.cue>     load prepared game briefly (debug)");
        Console.WriteLine("  --help                 show this help");
        Console.WriteLine();
        Console.WriteLine("Target dump: Crash Bandicoot 2 PAL (SCES-00967 / SCES_009.67).");
        Console.WriteLine("If <file.cue> is omitted for --run/--smoke, uses CdPath from settings.json when set.");
    }

    static bool TryResolveCue(string[] args, out string cue, out string error)
    {
        cue = "";
        error = "";

        if (args.Length >= 2 && !string.IsNullOrWhiteSpace(args[1]))
        {
            cue = Path.GetFullPath(args[1]);
            if (!File.Exists(cue))
            {
                error = $"cue not found: {cue}";
                return false;
            }
            return true;
        }

        try
        {
            ConfigManager.Load();
            var saved = ConfigManager.Game.CdPath;
            if (!string.IsNullOrWhiteSpace(saved) && File.Exists(saved))
            {
                cue = Path.GetFullPath(saved);
                Console.WriteLine($"[CrashBandicoot2] using saved disc: {cue}");
                return true;
            }
        }
        catch
        {
            // fall through
        }

        error = "missing <file.cue> (and no valid CdPath in settings.json)";
        return false;
    }
}
