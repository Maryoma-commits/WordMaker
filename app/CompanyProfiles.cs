using System.IO;
using System.Text.Json;

namespace WordMaker;

/// <summary>
/// A saved company profile. Only company-level fields are stored —
/// employee details (name, nationality, passport, occupation) are
/// intentionally never saved.
/// </summary>
public sealed class CompanyProfile
{
    public string CompanyNameEnglish { get; set; } = string.Empty;
    public string CompanyNameArabic { get; set; } = string.Empty;
    public string M700No { get; set; } = string.Empty;
    public string BorderNo { get; set; } = string.Empty;
    public string VisaNo { get; set; } = string.Empty;
}

/// <summary>
/// Persists company profiles to %AppData%\WordMaker\companies.json — proper
/// per-user storage that works even when the exe sits in a read-only folder.
/// Older builds kept the file next to the exe; those are migrated once.
/// </summary>
public static class CompanyProfiles
{
    private static string DefaultPath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WordMaker");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "companies.json");
        }
    }

    private static string LegacyPath
    {
        get
        {
            // Environment.ProcessPath gives the real exe location even for
            // single-file publish (AppContext.BaseDirectory would point at
            // the throwaway extraction directory).
            var dir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            return Path.Combine(dir, "companies.json");
        }
    }

    public static List<CompanyProfile> Load(string? path = null)
    {
        var explicitPath = path is not null;
        path ??= DefaultPath;

        try
        {
            // One-time migration from the old next-to-exe location.
            if (!explicitPath && !File.Exists(path) && File.Exists(LegacyPath))
            {
                var migrated = Load(LegacyPath);
                Save(migrated);
                try
                {
                    File.Move(LegacyPath, LegacyPath + ".migrated");
                }
                catch
                {
                    // Old file stays, but the new store now exists so it
                    // will not be imported twice.
                }
                return migrated;
            }

            if (!File.Exists(path))
            {
                return new List<CompanyProfile>();
            }
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<CompanyProfile>>(json) ?? new List<CompanyProfile>();
        }
        catch
        {
            // Corrupt or unreadable store — start fresh rather than crash.
            return new List<CompanyProfile>();
        }
    }

    public static void Save(List<CompanyProfile> profiles, string? path = null)
    {
        path ??= DefaultPath;
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(profiles, options));
    }
}
