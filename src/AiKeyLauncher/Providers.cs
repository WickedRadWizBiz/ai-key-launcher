using System.Diagnostics;
using Microsoft.Win32;

namespace AiKeyLauncher;

/// <summary>An AI assistant the key can open: a desktop app when installed, otherwise its website.</summary>
sealed record AiProvider(
    string Id,
    string Name,
    string WebUrl,
    string InstallUrl,
    string[] ExeCandidates,          // expanded with environment variables
    string? PackageFamilyPrefix,     // MSIX/Store package family name prefix
    string? Aumid,                   // app user model id for shell:AppsFolder launch
    string? UriScheme = null)        // e.g. "ms-copilot:"
{
    public string? FindExe() =>
        ExeCandidates.Select(Environment.ExpandEnvironmentVariables).FirstOrDefault(File.Exists);

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

    public bool IsInstalled => FindExe() is not null || HasPackage();

    /// <summary>Opens the desktop app if present, otherwise the website in the default browser.</summary>
    public void Launch(bool forceWeb = false)
    {
        if (!forceWeb)
        {
            var exe = FindExe();
            if (exe is not null) { Start(exe); return; }
            if (Aumid is not null && HasPackage()) { Start("explorer.exe", $"shell:AppsFolder\\{Aumid}"); return; }
            if (UriScheme is not null) { Start(UriScheme); return; }
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
        new("claude", "Claude",
            "https://claude.ai/new", "https://claude.ai/download",
            [@"%LOCALAPPDATA%\AnthropicClaude\claude.exe", @"%LOCALAPPDATA%\Programs\Claude\Claude.exe"],
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
        new("copilot", "Microsoft Copilot",
            "https://copilot.microsoft.com/", "https://copilot.microsoft.com/",
            [], "Microsoft.Copilot", null, "ms-copilot:"),
    ];

    public static AiProvider Get(string id) => All.FirstOrDefault(p => p.Id == id) ?? All[0];
}
