using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace WordMaker;

public sealed record UpdateInfo(Version Version, string DownloadUrl, string ReleaseNotes, long SizeBytes);

/// <summary>
/// In-app updates backed by GitHub Releases:
///   1. CheckForUpdateAsync compares the latest release tag against the
///      embedded assembly version.
///   2. DownloadAsync fetches the release's WordMaker.exe asset.
///   3. ApplyUpdate writes a helper script that waits for this app to exit,
///      swaps the exe, and relaunches it (a running exe cannot overwrite
///      itself).
///
/// Publishing an update (developer): bump <Version> in WordMaker.csproj,
/// rebuild, then create a GitHub release tagged e.g. "v1.0.1" with the new
/// WordMaker.exe attached.
/// </summary>
public static class UpdateService
{
    /// <summary>GitHub repository that hosts releases, as "owner/repo".</summary>
    public const string GitHubRepo = "Maryoma-commits/WordMaker";

    /// <summary>The release asset the updater downloads.</summary>
    public const string AssetName = "WordMaker.exe";

    private static readonly HttpClient Http = CreateClient();

    public static Version CurrentVersion =>
        typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WordMaker-Updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.Timeout = TimeSpan.FromMinutes(10);
        return client;
    }

    /// <summary>Returns the newest release if it is newer than this build, else null.</summary>
    public static async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        using var response = await Http.GetAsync(
            $"https://api.github.com/repos/{GitHubRepo}/releases/latest");
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        var tag = root.GetProperty("tag_name").GetString()
            ?? throw new InvalidDataException("Release has no tag name.");
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest))
        {
            throw new InvalidDataException($"Cannot parse a version from release tag '{tag}'.");
        }

        if (latest <= CurrentVersion)
        {
            return null;
        }

        string? url = null;
        long size = 0;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (string.Equals(asset.GetProperty("name").GetString(), AssetName,
                    StringComparison.OrdinalIgnoreCase))
            {
                url = asset.GetProperty("browser_download_url").GetString();
                if (asset.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number)
                {
                    size = s.GetInt64();
                }
                break;
            }
        }
        if (url is null)
        {
            throw new InvalidDataException(
                $"Release '{tag}' does not contain a '{AssetName}' asset.");
        }

        var notes = root.TryGetProperty("body", out var body)
            && body.ValueKind == JsonValueKind.String
                ? body.GetString() ?? string.Empty
                : string.Empty;

        return new UpdateInfo(latest, url, notes, size);
    }

    /// <summary>Downloads the new exe and returns its temp path.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo update, IProgress<int> progress)
    {
        var dir = Path.Combine(Path.GetTempPath(), "WordMaker.update");
        Directory.CreateDirectory(dir);
        var savePath = Path.Combine(dir, AssetName);

        using var response = await Http.GetAsync(update.DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? update.SizeBytes;
        await using var src = await response.Content.ReadAsStreamAsync();
        await using var dst = File.Create(savePath);

        var buffer = new byte[81920];
        long copied = 0;
        int read;
        while ((read = await src.ReadAsync(buffer)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read));
            copied += read;
            if (total > 0)
            {
                progress.Report((int)(100 * copied / total));
            }
        }
        return savePath;
    }

    /// <summary>
    /// Applies the downloaded exe: launches a hidden helper script that waits
    /// for the app to exit, replaces the exe (with retries), and restarts it.
    /// Call this last — the app should exit right after.
    /// </summary>
    public static void ApplyUpdate(string downloadedExePath)
    {
        var target = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot locate the running exe.");

        var dir = Path.Combine(Path.GetTempPath(), "WordMaker.update");
        var scriptPath = Path.Combine(dir, "apply-update.cmd");
        File.WriteAllText(scriptPath, BuildScript(downloadedExePath, target));

        Process.Start(new ProcessStartInfo("cmd.exe", "/c \"\"" + scriptPath + "\"\"")
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }

    private static string BuildScript(string source, string target)
    {
        // Note: written without cmd's parenthesized blocks so no delayed
        // expansion is needed; retries cover antivirus/file-lock races.
        return $@"
@echo off
setlocal
set ""SRC={source}""
set ""DST={target}""
set /a CNT=0

:WAITAPP
tasklist /FI ""IMAGENAME eq WordMaker.exe"" 2>nul | find /I ""WordMaker.exe"" >nul
if not errorlevel 1 goto STILLRUNNING
goto MOVE

:STILLRUNNING
timeout /t 1 /nobreak >nul
set /a CNT+=1
if %CNT% LSS 30 goto WAITAPP
goto END

:MOVE
move /y ""%SRC%"" ""%DST%"" >nul 2>&1
if exist ""%DST%"" goto STARTAPP
timeout /t 1 /nobreak >nul
set /a CNT+=1
if %CNT% LSS 60 goto MOVE
goto END

:STARTAPP
start """" ""%DST%""

:END
del ""%~f0"" >nul 2>&1
exit
";
    }
}
