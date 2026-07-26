using System.Text.Json;

namespace NexKVM;

/// <summary>User settings persisted between runs (%LOCALAPPDATA%\NexKVM\settings.json).</summary>
internal sealed class Settings
{
    public string Role { get; set; } = "host";      // "host" | "receiver"
    public string TargetIp { get; set; } = "";
    public int Port { get; set; } = 24800;
    public string Side { get; set; } = "Left";        // Left | Right
    public bool Clipboard { get; set; } = true;

    private static string PathFor()
    {
        string dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexKVM");
        Directory.CreateDirectory(dir);
        return System.IO.Path.Combine(dir, "settings.json");
    }

    public static Settings Load()
    {
        try
        {
            string p = PathFor();
            if (File.Exists(p))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(p)) ?? new Settings();
        }
        catch { /* ignore, use defaults */ }
        return new Settings();
    }

    public void Save()
    {
        try { File.WriteAllText(PathFor(), JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch { /* ignore */ }
    }
}
