namespace AiKeyLauncher;

/// <summary>Tiny append-only log in %LOCALAPPDATA%\AiKeyLauncher\log.txt so field tests can be diagnosed.</summary>
static class Log
{
    public static string Path => System.IO.Path.Combine(Settings.Dir, "log.txt");
    static readonly object Gate = new();

    public static void Write(string line)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Settings.Dir);
                if (File.Exists(Path) && new FileInfo(Path).Length > 512 * 1024) File.Delete(Path);
                File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {line}{Environment.NewLine}");
            }
        }
        catch { /* never let logging break the app */ }
    }

    public static void Error(string where, Exception ex) => Write($"ERROR in {where}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
}
