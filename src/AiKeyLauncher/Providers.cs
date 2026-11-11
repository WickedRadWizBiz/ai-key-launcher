using System.Diagnostics;
using Microsoft.Win32;

namespace AiKeyLauncher;

/// <summary>An AI assistant the key can open: a desktop app when installed, otherwise its website.</summary>
sealed record AiProvider(
    string Id,
    string Name,
    string WebUrl,
    string InstallUrl,
    string[] ExeCandidates,          // expanded with environment variables; last path segment before the file may be a wildcard dir
    string? PackageFamilyPrefix,     // MSIX/Store package family name prefix
    string? Aumid,                   // app user model id for shell:AppsFolder launch
    string? UriScheme = null)        // e.g. "ms-copilot:"
{
    /// <summary>Something ShellExecute can open directly: an exe, or a Start Menu shortcut.</summary>
    public string? FindLaunchTarget()
    {
        foreach (var raw in ExeCandidates)
        {
            var path = Environment.ExpandEnvironmentVariables(raw);
            if (File.Exists(path)) return path;
            // allow one wildcard directory, e.g. ...\Claude\app-*\claude.exe
            var dir = Path.GetDirectoryName(path);
            var file = Path.GetFileName(path);
            if (dir is not null && dir.Contains('*'))
            {
                var parent = Path.GetDirectoryName(dir);
                var pattern = Path.GetFileName(dir);
                if (parent is not null && Directory.Exists(parent))
                {
                    var hit = Directory.GetDirectories(parent, pattern).OrderByDescending(d => d)
                        .Select(d => Path.Combine(d, file)).FirstOrDefault(File.Exists);
                    if (hit is not null) return hit;
                }
            }
        }
        return FindUninstallEntry() ?? FindShortcut();
    }

    string? FindUninstallEntry()
    {
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        foreach (var sub in new[] { @"Software\Microsoft\Windows\CurrentVersion\Uninstall", @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
        {
            try
            {
                using var key = root.OpenSubKey(sub);
                if (key is null) continue;
                foreach (var name in key.GetSubKeyNames())
                {
                    using var app = key.OpenSubKey(name);
                    if (app?.GetValue("DisplayName") is not string dn || !dn.StartsWith(Name, StringComparison.OrdinalIgnoreCase)) continue;
                    if (app.GetValue("DisplayIcon") is string icon)
                    {
                        var exe = icon.Split(',')[0].Trim('"');
                        if (exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(exe)) return exe;
                    }
                }
            }
            catch { /* permissions */ }
        }
        return null;
    }

    string? FindShortcut() =>
        Providers.StartMenuShortcuts().FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), Name, StringComparison.OrdinalIgnoreCase));

    public bool HasPackage()
    {
        if (PackageFamilyPrefix is null) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
            return key?.GetSubKeyNames().Any(n => n.StartsWith(PackageFamilyPrefix, StringComparison.OrdinalIgnoreCase)) == true;
        }
        catch { return false; }
    }

    /// <summary>Full detection (slow: disk and registry). Call off the UI thread; see <see cref="Providers.Refresh"/>.</summary>
    public Detection Detect() => new(FindLaunchTarget(), HasPackage(), UriScheme is not null && SchemeRegistered(UriScheme));

    /// <summary>Cached result of the last <see cref="Providers.Refresh"/>.</summary>
    public Detection Cached => Providers.Cache.TryGetValue(Id, out var d) ? d : new(null, false, false);

    public bool IsInstalled => Cached.Installed;

    static bool SchemeRegistered(string scheme)
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(scheme.TrimEnd(':'));
            return key?.GetValue("URL Protocol") is not null;
        }
        catch { return false; }
    }

    /// <summary>Opens the desktop app if present (per the cache), otherwise the website in the default browser.</summary>
    public void Launch(bool forceWeb = false)
    {
        var d = Cached;
        if (!forceWeb)
        {
            if (d.Target is not null) { Start(d.Target); return; }
            if (Aumid is not null && d.Package) { Start("explorer.exe", $"shell:AppsFolder\\{Aumid}"); return; }
            if (UriScheme is not null && d.Scheme) { Start(UriScheme); return; }
        }
        Start(WebUrl);
    }

    static void Start(string file, string? args = null)
    {
        Log.Write($"launch: {file} {args}");
        try { Process.Start(new ProcessStartInfo(file, args ?? "") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("launch", ex); }
    }
}

/// <summary>What detection found for one provider.</summary>
sealed record Detection(string? Target, bool Package, bool Scheme)
{
    public bool Installed => Target is not null || Package || Scheme;
}

static class Providers
{
    internal static volatile Dictionary<string, Detection> Cache = new();
    static readonly object RefreshGate = new();
    static List<string>? _shortcuts;

    /// <summary>Re-detects every provider on a worker thread; the UI thread only ever reads the cache.</summary>
    public static void Refresh(Action? done = null) => ThreadPool.QueueUserWorkItem(_ =>
    {
        if (!Monitor.TryEnter(RefreshGate)) return; // one at a time
        try
        {
            _shortcuts = null;
            var result = new Dictionary<string, Detection>();
            foreach (var p in All)
            {
                try { result[p.Id] = p.Detect(); }
                catch (Exception ex) { Log.Error($"detect {p.Id}", ex); result[p.Id] = new(null, false, false); }
            }
            Cache = result;
            Log.Write("detect: " + string.Join(", ", result.Select(kv => $"{kv.Key}={(kv.Value.Installed ? kv.Value.Target ?? (kv.Value.Package ? "package" : "scheme") : "web")}")));
        }
        finally { Monitor.Exit(RefreshGate); }
        done?.Invoke();
    });

    internal static List<string> StartMenuShortcuts()
    {
        if (_shortcuts is not null) return _shortcuts;
        var list = new List<string>();
        foreach (var folder in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms })
        {
            try
            {
                var dir = Environment.GetFolderPath(folder);
                if (Directory.Exists(dir))
                    list.AddRange(Directory.EnumerateFiles(dir, "*.lnk", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 4 }));
            }
            catch (Exception ex) { Log.Error("shortcuts", ex); }
        }
        return _shortcuts = list;
    }

    public static readonly AiProvider[] All =
    [
        new("copilot", "Microsoft Copilot",
            "https://copilot.microsoft.com/", "https://copilot.microsoft.com/",
            [], "Microsoft.Copilot", null, "ms-copilot:"),
        new("claude", "Claude",
            "https://claude.ai/new", "https://claude.ai/download",
            [@"%LOCALAPPDATA%\Claude\claude.exe", @"%LOCALAPPDATA%\Claude\app-*\claude.exe",
             @"%LOCALAPPDATA%\AnthropicClaude\claude.exe", @"%LOCALAPPDATA%\AnthropicClaude\app-*\claude.exe",
             @"%LOCALAPPDATA%\Programs\Claude\Claude.exe"],
            "Anthropic.Claude", null),
        new("chatgpt", "ChatGPT",
            "https://chatgpt.com/", "https://openai.com/chatgpt/download/",
            [],
            "OpenAI.ChatGPT-Desktop", "OpenAI.ChatGPT-Desktop_2p2nqsd0c76g0!ChatGPT"),
        new("gemini", "Gemini",
            "https://gemini.google.com/app", "https://gemini.google.com/app",
            [], null, null),
        new("kimi", "Kimi",
            "https://www.kimi.com/", "https://www.kimi.com/download",
            [@"%LOCALAPPDATA%\Programs\Kimi\Kimi.exe", @"%LOCALAPPDATA%\Kimi\Kimi.exe"],
            null, null),
    ];

    public static AiProvider Get(string id) => All.FirstOrDefault(p => p.Id == id) ?? All[0];
}
