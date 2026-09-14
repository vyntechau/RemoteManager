using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using RemoteManager.Core.Interfaces;
using RemoteManager.Core.Logging;
using RemoteManager.Core.Models;

namespace RemoteManager.Core.Services;

public class GitHubUpdateService : IUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly string _owner;
    private readonly string _repo;
    private readonly string _currentVersion;

    public string CurrentVersion => _currentVersion;

    public GitHubUpdateService(HttpClient? httpClient = null, string owner = "vyntechau", string repo = "RemoteManager", string? currentVersion = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "RemoteManager-UpdateService");
        }

        _owner = owner;
        _repo = repo;
        _currentVersion = currentVersion ?? GetAssemblyVersion();
    }

    private static string GetAssemblyVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var ver = asm.GetName().Version;
        if (ver != null)
        {
            return $"{ver.Major}.{ver.Minor}.{Math.Max(0, ver.Build)}";
        }
        return "1.0.0";
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(bool includePrereleases = false, CancellationToken cancellationToken = default)
    {
        try
        {
            LogEngine.Instance.Info("App", $"Checking for updates from GitHub ({_owner}/{_repo}). Current version: v{_currentVersion}");

            // If includePrereleases is true, query /releases list; otherwise query /releases/latest
            string url = includePrereleases
                ? $"https://api.github.com/repos/{_owner}/{_repo}/releases"
                : $"https://api.github.com/repos/{_owner}/{_repo}/releases/latest";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                LogEngine.Instance.Info("App", "No GitHub releases found (404). Application is on the latest build.");
                return UpdateCheckResult.UpToDate(_currentVersion);
            }

            response.EnsureSuccessStatusCode();

            var jsonStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(jsonStream, cancellationToken: cancellationToken);

            UpdateReleaseInfo? release = null;
            if (includePrereleases)
            {
                if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                {
                    // Find the most recent release
                    var firstElem = doc.RootElement.EnumerateArray().FirstOrDefault();
                    if (firstElem.ValueKind == JsonValueKind.Object)
                    {
                        release = ParseReleaseElement(firstElem);
                    }
                }
            }
            else
            {
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    release = ParseReleaseElement(doc.RootElement);
                }
            }

            if (release == null)
            {
                return UpdateCheckResult.UpToDate(_currentVersion);
            }

            bool isNewer = IsNewerVersion(release.Version, _currentVersion);
            if (isNewer)
            {
                LogEngine.Instance.Info("App", $"New update found: v{release.Version} (current: v{_currentVersion})");
                return UpdateCheckResult.Available(_currentVersion, release);
            }

            LogEngine.Instance.Info("App", $"RemoteManager is up to date (current: v{_currentVersion}, latest: v{release.Version})");
            return UpdateCheckResult.UpToDate(_currentVersion, release);
        }
        catch (HttpRequestException ex)
        {
            LogEngine.Instance.Warn("App", $"Update check failed due to network error: {ex.Message}");
            return UpdateCheckResult.Failed(_currentVersion, $"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            LogEngine.Instance.Error("App", "Unexpected error while checking for updates", ex);
            return UpdateCheckResult.Failed(_currentVersion, ex.Message);
        }
    }

    public static UpdateReleaseInfo ParseReleaseElement(JsonElement elem)
    {
        var release = new UpdateReleaseInfo();

        if (elem.TryGetProperty("tag_name", out var tagElem))
        {
            release.TagName = tagElem.GetString() ?? string.Empty;
            release.Version = CleanVersionString(release.TagName);
        }

        if (elem.TryGetProperty("name", out var nameElem))
        {
            release.Name = nameElem.GetString() ?? release.TagName;
        }

        if (elem.TryGetProperty("body", out var bodyElem))
        {
            release.Body = bodyElem.GetString() ?? string.Empty;
        }

        if (elem.TryGetProperty("html_url", out var urlElem))
        {
            release.HtmlUrl = urlElem.GetString() ?? string.Empty;
        }

        if (elem.TryGetProperty("prerelease", out var preElem))
        {
            release.IsPrerelease = preElem.GetBoolean();
        }

        if (elem.TryGetProperty("published_at", out var pubElem) && pubElem.TryGetDateTime(out var pubDate))
        {
            release.PublishedAt = pubDate;
        }

        if (elem.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var assetElem in assetsElem.EnumerateArray())
            {
                var asset = new UpdateAssetInfo();
                if (assetElem.TryGetProperty("name", out var aName)) asset.Name = aName.GetString() ?? string.Empty;
                if (assetElem.TryGetProperty("browser_download_url", out var aUrl)) asset.DownloadUrl = aUrl.GetString() ?? string.Empty;
                if (assetElem.TryGetProperty("size", out var aSize)) asset.Size = aSize.GetInt64();
                if (assetElem.TryGetProperty("content_type", out var aType)) asset.ContentType = aType.GetString();

                release.Assets.Add(asset);
            }
        }

        return release;
    }

    public static string CleanVersionString(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "0.0.0";
        var trimmed = raw.Trim().TrimStart('v', 'V');
        var dashIdx = trimmed.IndexOf('-');
        if (dashIdx > 0)
        {
            trimmed = trimmed[..dashIdx];
        }
        var plusIdx = trimmed.IndexOf('+');
        if (plusIdx > 0)
        {
            trimmed = trimmed[..plusIdx];
        }
        return trimmed;
    }

    public static bool IsNewerVersion(string remoteVersionStr, string localVersionStr)
    {
        var remote = ParseVersion(remoteVersionStr);
        var local = ParseVersion(localVersionStr);

        return remote > local;
    }

    public static Version ParseVersion(string versionStr)
    {
        var clean = CleanVersionString(versionStr);
        var parts = clean.Split('.');
        int major = 0, minor = 0, build = 0, revision = 0;

        if (parts.Length > 0 && int.TryParse(parts[0], out var p0)) major = p0;
        if (parts.Length > 1 && int.TryParse(parts[1], out var p1)) minor = p1;
        if (parts.Length > 2 && int.TryParse(parts[2], out var p2)) build = p2;
        if (parts.Length > 3 && int.TryParse(parts[3], out var p3)) revision = p3;

        return new Version(major, minor, build, revision);
    }

    public async Task<string> DownloadAssetAsync(UpdateAssetInfo asset, string destinationDirectory, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(destinationDirectory);
        var destinationPath = Path.Combine(destinationDirectory, asset.Name);

        LogEngine.Instance.Info("App", $"Starting download of update asset: {asset.Name} from {asset.DownloadUrl}");

        using var response = await _httpClient.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? asset.Size;
        long totalRead = 0;

        using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        int read;

        while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            totalRead += read;

            if (totalBytes > 0 && progress != null)
            {
                progress.Report((double)totalRead / totalBytes);
            }
        }

        progress?.Report(1.0);
        LogEngine.Instance.Info("App", $"Update asset downloaded successfully to: {destinationPath} ({totalRead} bytes)");
        return destinationPath;
    }

    public async Task<bool> VerifyChecksumAsync(string localFilePath, string checksumsUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(localFilePath)) return false;

            LogEngine.Instance.Info("App", $"Fetching checksums file from: {checksumsUrl}");
            var checksumsText = await _httpClient.GetStringAsync(checksumsUrl, cancellationToken);
            if (string.IsNullOrWhiteSpace(checksumsText)) return false;

            using var sha256 = SHA256.Create();
            using var fileStream = File.OpenRead(localFilePath);
            var hashBytes = await sha256.ComputeHashAsync(fileStream, cancellationToken);
            var computedHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

            var fileName = Path.GetFileName(localFilePath);
            var lines = checksumsText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var cleanLine = line.Trim().ToLowerInvariant();
                if (cleanLine.Contains(computedHash) && cleanLine.Contains(fileName.ToLowerInvariant()))
                {
                    LogEngine.Instance.Info("App", $"Checksum verification passed for {fileName} (SHA-256: {computedHash})");
                    return true;
                }
            }

            LogEngine.Instance.Warn("App", $"Checksum verification failed for {fileName}. Expected entry not found in checksums.txt (Calculated: {computedHash})");
            return false;
        }
        catch (Exception ex)
        {
            LogEngine.Instance.Warn("App", $"Error verifying checksum: {ex.Message}");
            return false;
        }
    }

    public bool LaunchInstaller(string installerPath)
    {
        try
        {
            if (!File.Exists(installerPath)) return false;

            LogEngine.Instance.Info("App", $"Launching installer: {installerPath}");
            var psi = new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true
            };

            var proc = Process.Start(psi);
            return proc != null;
        }
        catch (Exception ex)
        {
            LogEngine.Instance.Error("App", $"Failed to launch installer at {installerPath}", ex);
            return false;
        }
    }
}
