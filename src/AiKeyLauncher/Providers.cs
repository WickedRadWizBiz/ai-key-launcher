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

    string? FindShortcut()
    {
        foreach (var folder in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms })
        {
            try
            {
                var dir = Environment.GetFolderPath(folder);
                if (!Directory.Exists(dir)) continue;
                var hit = Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories)
                    .FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), Name, StringComparison.OrdinalIgnoreCase));
                if (hit is not null) return hit;
            }
            catch { /* unreadable subfolder */ }
        }
        return null;
    }

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

    public bool IsInstalled => FindLaunchTarget() is not null || HasPackage() || (UriScheme is not null && SchemeRegistered(UriScheme));

    static bool SchemeRegistered(string scheme)
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(scheme.TrimEnd(':'));
            return key?.GetValue("URL Protocol") is not null;
        }
        catch { return false; }
    }

    /// <summary>Opens the desktop app if present, otherwise the website in the default browser.</summary>
    public void Launch(bool forceWeb = false)
    {
        if (!forceWeb)
        {
            var target = FindLaunchTarget();
            if (target is not null) { Start(target); return; }
            if (Aumid is not null && HasPackage()) { Start("explorer.exe", $"shell:AppsFolder\\{Aumid}"); return; }
            if (UriScheme is not null && SchemeRegistered(UriScheme)) { Start(UriScheme); return; }
        }
        Start(WebUrl);
    }

    static void Start(string file, string? args = null)
    {
        try { Process.Start(new ProcessStartInfo(file, args ?? "") { UseShellExecute = true }); }
        catch (Exception ex) { Debug.WriteLine($"Launch failed for {file}: {ex.Message}"); }
    }
}

static class Providers
{
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
