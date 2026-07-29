using RecompOne.Runtime.Cdrom;
var cue = args[0];
using var fs = CueFs.Open(cue);
foreach (var name in fs.ListFiles().OrderBy(x => x))
  if (name.Contains("STR", StringComparison.OrdinalIgnoreCase) ||
      name.Contains("MOV", StringComparison.OrdinalIgnoreCase) ||
      name.Contains("XA", StringComparison.OrdinalIgnoreCase) ||
      name.Contains("FMV", StringComparison.OrdinalIgnoreCase) ||
      name.EndsWith(".STR;1", StringComparison.OrdinalIgnoreCase) ||
      name.Contains("INTRO", StringComparison.OrdinalIgnoreCase) ||
      name.Contains("TITLE", StringComparison.OrdinalIgnoreCase))
    Console.WriteLine(name);
Console.WriteLine("--- sample root ---");
foreach (var name in fs.ListFiles().OrderBy(x => x).Take(40))
  Console.WriteLine(name);
