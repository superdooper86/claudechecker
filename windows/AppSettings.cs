using System;
using System.IO;
using System.Text.Json;

namespace ClaudeCheckerWindows;

public class AppSettings
{
    public string CookieStore     { get; set; } = "";
    public string BurnHistory     { get; set; } = "";
    public string OrgId           { get; set; } = "";
    public int    RefreshInterval { get; set; } = 120;
    public bool   ShowInTaskbar   { get; set; } = true;
    public bool   BetaChannel     { get; set; } = false;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClaudeChecker", "settings.json");

    private static AppSettings? _instance;
    public static AppSettings Default => _instance ??= Load();

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
