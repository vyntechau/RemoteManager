using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RemoteManager.App.ViewModels;
using RemoteManager.Core.Interfaces;
using RemoteManager.Core.Models;
using RemoteManager.Core.Services;

namespace RemoteManager.Tests;

public class GitHubUpdateServiceTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("V2.0.0-beta.1", "2.0.0")]
    [InlineData("1.0.0+build123", "1.0.0")]
    [InlineData("  v3.4.5  ", "3.4.5")]
    [InlineData("", "0.0.0")]
    public void CleanVersionString_NormalizesCorrectly(string input, string expected)
    {
        var actual = GitHubUpdateService.CleanVersionString(input);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("1.1.0", "1.0.0", true)]
    [InlineData("v1.0.1", "1.0.0", true)]
    [InlineData("2.0.0", "1.9.9", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("0.9.9", "1.0.0", false)]
    [InlineData("v1.0.0", "v1.0.0", false)]
    public void IsNewerVersion_ComparesCorrectly(string remote, string local, bool expected)
    {
        var result = GitHubUpdateService.IsNewerVersion(remote, local);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseReleaseElement_ExtractsAllFieldsAndAssets()
    {
        var json = """
        {
            "tag_name": "v1.2.0",
            "name": "RemoteManager v1.2.0",
            "body": "Fixed white screen and added GitHub updater.",
            "html_url": "https://github.com/vyntechau/RemoteManager/releases/tag/v1.2.0",
            "prerelease": false,
            "published_at": "2026-09-14T10:00:00Z",
            "assets": [
                {
                    "name": "RemoteManager-v1.2.0-win-x64-Setup.msi",
                    "browser_download_url": "https://github.com/download/msi",
                    "size": 52428800,
                    "content_type": "application/octet-stream"
                },
                {
                    "name": "RemoteManager-v1.2.0-win-x64-portable.zip",
                    "browser_download_url": "https://github.com/download/zip",
                    "size": 41943040,
                    "content_type": "application/zip"
                },
                {
                    "name": "checksums.txt",
                    "browser_download_url": "https://github.com/download/checksums",
                    "size": 256,
                    "content_type": "text/plain"
                }
            ]
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var release = GitHubUpdateService.ParseReleaseElement(doc.RootElement);

        Assert.Equal("v1.2.0", release.TagName);
        Assert.Equal("1.2.0", release.Version);
        Assert.Equal("RemoteManager v1.2.0", release.Name);
        Assert.Contains("Fixed white screen", release.Body);
        Assert.Equal("https://github.com/vyntechau/RemoteManager/releases/tag/v1.2.0", release.HtmlUrl);
        Assert.False(release.IsPrerelease);
        Assert.Equal(3, release.Assets.Count);

        Assert.NotNull(release.MsiAsset);
        Assert.True(release.MsiAsset!.IsMsi);
        Assert.Equal("RemoteManager-v1.2.0-win-x64-Setup.msi", release.MsiAsset.Name);

        Assert.NotNull(release.ZipAsset);
        Assert.True(release.ZipAsset!.IsZip);

        Assert.NotNull(release.ChecksumsAsset);
        Assert.True(release.ChecksumsAsset!.IsChecksum);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenNewReleaseAvailable_ReturnsAvailable()
    {
        var releaseJson = """
        {
            "tag_name": "v1.1.0",
            "name": "RemoteManager v1.1.0",
            "body": "Bugfixes and new features",
            "html_url": "https://github.com/vyntechau/RemoteManager/releases/tag/v1.1.0",
            "prerelease": false,
            "assets": []
        }
        """;

        var handler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
        });

        var client = new HttpClient(handler);
        var service = new GitHubUpdateService(client, currentVersion: "1.0.0");

        var result = await service.CheckForUpdatesAsync();

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.True(result.IsUpdateAvailable);
        Assert.NotNull(result.LatestRelease);
        Assert.Equal("v1.1.0", result.LatestRelease!.TagName);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenCurrentVersionIsLatest_ReturnsUpToDate()
    {
        var releaseJson = """
        {
            "tag_name": "v1.0.0",
            "name": "RemoteManager v1.0.0",
            "body": "Initial release",
            "html_url": "https://github.com/vyntechau/RemoteManager/releases/tag/v1.0.0",
            "prerelease": false,
            "assets": []
        }
        """;

        var handler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
        });

        var client = new HttpClient(handler);
        var service = new GitHubUpdateService(client, currentVersion: "1.0.0");

        var result = await service.CheckForUpdatesAsync();

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.False(result.IsUpdateAvailable);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_When404_ReturnsUpToDateGracefully()
    {
        var handler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = new HttpClient(handler);
        var service = new GitHubUpdateService(client, currentVersion: "1.0.0");

        var result = await service.CheckForUpdatesAsync();

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.False(result.IsUpdateAvailable);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenNetworkFails_ReturnsFailed()
    {
        var handler = new MockHttpMessageHandler(req => throw new HttpRequestException("Name resolution failure"));
        var client = new HttpClient(handler);
        var service = new GitHubUpdateService(client, currentVersion: "1.0.0");

        var result = await service.CheckForUpdatesAsync();

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("Name resolution failure", result.ErrorMessage);
    }

    [Fact]
    public async Task VerifyChecksumAsync_WithMatchingHash_ReturnsTrue()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var content = "RemoteManager test update package payload";
            await File.WriteAllTextAsync(tempFile, content);

            // Compute expected hash
            using var sha = SHA256.Create();
            var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(content));
            var hashHex = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

            var fileName = Path.GetFileName(tempFile);
            var checksumsTxt = $"{hashHex}  {fileName}\notherhash  otherfile.zip\n";

            var handler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(checksumsTxt, Encoding.UTF8, "text/plain")
            });

            var client = new HttpClient(handler);
            var service = new GitHubUpdateService(client, currentVersion: "1.0.0");

            var verified = await service.VerifyChecksumAsync(tempFile, "https://github.com/checksums.txt");
            Assert.True(verified);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task VerifyChecksumAsync_WithMismatchingHash_ReturnsFalse()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "Corrupted or altered package data");
            var fileName = Path.GetFileName(tempFile);
            var checksumsTxt = $"deadbeef12345678deadbeef12345678deadbeef12345678deadbeef12345678  {fileName}\n";

            var handler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(checksumsTxt, Encoding.UTF8, "text/plain")
            });

            var client = new HttpClient(handler);
            var service = new GitHubUpdateService(client, currentVersion: "1.0.0");

            var verified = await service.VerifyChecksumAsync(tempFile, "https://github.com/checksums.txt");
            Assert.False(verified);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task MainViewModel_CheckForUpdates_SetsStateAndFooterText()
    {
        var db = new TestDatabaseService();
        var crypto = new TestEncryptionService();

        var mockUpdateService = new MockUpdateService
        {
            CheckResult = UpdateCheckResult.Available("1.0.0", new UpdateReleaseInfo
            {
                TagName = "v1.1.0",
                Version = "1.1.0",
                Name = "RemoteManager v1.1.0"
            })
        };

        var vm = new MainViewModel(db, crypto, mockUpdateService);
        vm.SafeDispatch = action => action();

        await vm.CheckForUpdatesAsync();

        Assert.True(vm.IsUpdateAvailable);
        Assert.NotNull(vm.LatestRelease);
        Assert.Equal("v1.1.0", vm.LatestRelease!.TagName);
        Assert.Contains("v1.1.0", vm.FooterUpdateStatusText);
        Assert.Contains("v1.1.0", vm.UpdateStatusTitle);
    }

    private class MockUpdateService : IUpdateService
    {
        public string CurrentVersion => "1.0.0";
        public UpdateCheckResult CheckResult { get; set; } = UpdateCheckResult.UpToDate("1.0.0");

        public Task<UpdateCheckResult> CheckForUpdatesAsync(bool includePrereleases = false, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CheckResult);
        }

        public Task<string> DownloadAssetAsync(UpdateAssetInfo asset, string destinationDirectory, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            progress?.Report(1.0);
            return Task.FromResult(Path.Combine(destinationDirectory, asset.Name));
        }

        public Task<bool> VerifyChecksumAsync(string localFilePath, string checksumsUrl, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public bool LaunchInstaller(string installerPath)
        {
            return true;
        }
    }

    private class TestDatabaseService : IDatabaseService
    {
        public AppSettings Settings { get; set; } = new();
        public Task InitializeAsync() => Task.CompletedTask;
        public Task<List<ConnectionItem>> GetAllConnectionsAsync() => Task.FromResult(new List<ConnectionItem>());
        public Task<ConnectionItem?> GetConnectionByIdAsync(Guid id) => Task.FromResult<ConnectionItem?>(null);
        public Task SaveConnectionAsync(ConnectionItem item) => Task.CompletedTask;
        public Task UpdateConnectionsOrderAsync(IEnumerable<ConnectionItem> connections) => Task.CompletedTask;
        public Task DeleteConnectionAsync(Guid id) => Task.CompletedTask;
        public Task<List<Credential>> GetAllCredentialsAsync() => Task.FromResult(new List<Credential>());
        public Task<Credential?> GetCredentialByIdAsync(Guid id) => Task.FromResult<Credential?>(null);
        public Task SaveCredentialAsync(Credential credential) => Task.CompletedTask;
        public Task DeleteCredentialAsync(Guid id) => Task.CompletedTask;
        public Task<List<ConnectionGroup>> GetAllGroupsAsync() => Task.FromResult(new List<ConnectionGroup>());
        public Task SaveGroupAsync(ConnectionGroup group) => Task.CompletedTask;
        public Task DeleteGroupAsync(Guid id) => Task.CompletedTask;
        public Task<AppSettings> GetSettingsAsync() => Task.FromResult(Settings);
        public Task SaveSettingsAsync(AppSettings settings)
        {
            Settings = settings;
            return Task.CompletedTask;
        }
    }

    private class TestEncryptionService : IEncryptionService
    {
        public string Encrypt(string plainText) => plainText;
        public string Decrypt(string cipherText) => cipherText;
    }
}
