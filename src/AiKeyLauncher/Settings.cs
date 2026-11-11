using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace AiKeyLauncher;

sealed class Settings
{
    public string DefaultProvider { get; set; } = "claude";
    public bool HoldOpensPicker { get; set; } = true;    // adds ~0.6 s to plain taps when on
    public int HoldDelayMs { get; set; } = 600;
    public bool AlwaysUseWeb { get; set; } = false;

    [JsonIgnore]
    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiKeyLauncher");
    static string FilePath => Path.Combine(Dir, "settings.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? new Settings();
        }
        catch { /* corrupt file: fall back to defaults */ }
        return new Settings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "AiKeyLauncher";

    public static bool StartsWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunName) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(RunName, $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue(RunName, throwOnMissingValue: false);
        }
    }
}
