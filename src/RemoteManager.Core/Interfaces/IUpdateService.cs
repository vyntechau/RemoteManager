using RemoteManager.Core.Models;

namespace RemoteManager.Core.Interfaces;

public interface IUpdateService
{
    string CurrentVersion { get; }
    Task<UpdateCheckResult> CheckForUpdatesAsync(bool includePrereleases = false, CancellationToken cancellationToken = default);
    Task<string> DownloadAssetAsync(UpdateAssetInfo asset, string destinationDirectory, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
    Task<bool> VerifyChecksumAsync(string localFilePath, string checksumsUrl, CancellationToken cancellationToken = default);
    bool LaunchInstaller(string installerPath);
}
