using RecompOne.Recompiler.CodeGen;
using RecompOne.Recompiler.Config;
using RecompOne.Runtime.Cdrom;

namespace CrashBandicoot2.Launcher.Recomp;

public static class RecompRunner
{
    public static void Run(string configTemplatePath, string cuePath, string outDir, IProgress<string>? progress = null)
    {
        progress?.Report("Loading recompiler config…");
        if (!File.Exists(configTemplatePath))
            throw new FileNotFoundException("CrashBandicoot2.json not found next to the launcher.", configTemplatePath);

        Directory.CreateDirectory(outDir);
        foreach (var stale in Directory.EnumerateFiles(outDir, "*.cs"))
            File.Delete(stale);

        var config = ConfigLoader.Load(configTemplatePath);
        config.Cue = Path.GetFullPath(cuePath);
        config.Game.Output = Path.GetFullPath(outDir);

        string? Resolve(string? p) =>
            p == null ? null : Path.IsPathRooted(p) ? Path.GetFullPath(p) : Path.GetFullPath(Path.Combine(outDir, p));

        config.Elf = Resolve(config.Elf);
        config.Map = Resolve(config.Map);
        config.FuncMap = Resolve(config.FuncMap);
        foreach (var overlay in config.Overlays)
        {
            overlay.Elf = Resolve(overlay.Elf);
            overlay.Map = Resolve(overlay.Map);
            overlay.FuncMap = Resolve(overlay.FuncMap);
        }

        progress?.Report("Reading disc and recompiling…");
        Directory.CreateDirectory(outDir);
        using var fs = CueFs.Open(Path.GetFullPath(cuePath));
        OverlayWriter.Write(config, fs, Path.GetFullPath(outDir));

        progress?.Report("Applying CB2 compatibility post-pass…");
        PostPassApplier.Apply(Path.Combine(outDir, "main.cs"));

        progress?.Report("Recompilation finished.");
    }
}
