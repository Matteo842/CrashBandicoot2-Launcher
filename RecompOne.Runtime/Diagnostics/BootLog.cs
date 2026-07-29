using System.Text;

namespace RecompOne.Runtime.Diagnostics;

/// <summary>Append-only boot trail to logs/boot.txt (survives silent Exit).</summary>
public static class BootLog
{
    static readonly object Gate = new();
    static string? _path;
    static int _n;

    public static void Write(string msg)
    {
        try
        {
            lock (Gate)
            {
                _path ??= Path.Combine(AppPaths.LogsDir, "boot.txt");
                Directory.CreateDirectory(AppPaths.LogsDir);
                if (_n == 0)
                    File.WriteAllText(_path, $"--- boot {DateTime.Now:O} ---{Environment.NewLine}");
                _n++;
                File.AppendAllText(_path, $"{_n:D4} {DateTime.Now:HH:mm:ss.fff} {msg}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch
        {
            // ignore
        }
    }
}
