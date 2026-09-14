namespace RemoteManager.Core.Models;

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    Failed
}

public class UpdateAssetInfo
{
    public string Name { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public long Size { get; set; }
    public string? ContentType { get; set; }

    public bool IsMsi => Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);
    public bool IsZip => Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    public bool IsChecksum => Name.Equals("checksums.txt", StringComparison.OrdinalIgnoreCase);
}

public class UpdateReleaseInfo
{
    public string TagName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string HtmlUrl { get; set; } = string.Empty;
    public DateTime? PublishedAt { get; set; }
    public bool IsPrerelease { get; set; }
    public List<UpdateAssetInfo> Assets { get; set; } = new();

    public UpdateAssetInfo? MsiAsset => Assets.FirstOrDefault(a => a.IsMsi);
    public UpdateAssetInfo? ZipAsset => Assets.FirstOrDefault(a => a.IsZip);
    public UpdateAssetInfo? ChecksumsAsset => Assets.FirstOrDefault(a => a.IsChecksum);
}

public class UpdateCheckResult
{
    public UpdateCheckStatus Status { get; set; }
    public string CurrentVersion { get; set; } = string.Empty;
    public UpdateReleaseInfo? LatestRelease { get; set; }
    public string? ErrorMessage { get; set; }

    public bool IsUpdateAvailable => Status == UpdateCheckStatus.UpdateAvailable;

    public static UpdateCheckResult UpToDate(string currentVersion, UpdateReleaseInfo? latestRelease = null) =>
        new() { Status = UpdateCheckStatus.UpToDate, CurrentVersion = currentVersion, LatestRelease = latestRelease };

    public static UpdateCheckResult Available(string currentVersion, UpdateReleaseInfo latestRelease) =>
        new() { Status = UpdateCheckStatus.UpdateAvailable, CurrentVersion = currentVersion, LatestRelease = latestRelease };

    public static UpdateCheckResult Failed(string currentVersion, string errorMessage) =>
        new() { Status = UpdateCheckStatus.Failed, CurrentVersion = currentVersion, ErrorMessage = errorMessage };
}
