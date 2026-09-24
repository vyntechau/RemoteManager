using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using Microsoft.Data.Sqlite;
using RemoteManager.App.ViewModels;
using RemoteManager.Core.Interfaces;
using RemoteManager.Core.Models;
using RemoteManager.Core.Services;
using RemoteManager.Data;
using RemoteManager.Data.Security;
using Wpf.Ui.Controls;
using Xunit;

namespace RemoteManager.Tests;

public class ComprehensiveCoverageTests
{
    #region 1. ExportModels Tests
    [Fact]
    public void ImportResult_SummaryText_CoversAllBranches()
    {
        var failedResult = new ImportResult { Success = false, ErrorMessage = "Database locked" };
        Assert.Equal("Import failed: Database locked", failedResult.SummaryText);

        var failedUnknown = new ImportResult { Success = false, ErrorMessage = null };
        Assert.Equal("Import failed: Unknown error", failedUnknown.SummaryText);

        var successEmpty = new ImportResult { Success = true };
        Assert.Equal("No items were imported.", successEmpty.SummaryText);

        var fullSuccess = new ImportResult
        {
            Success = true,
            ConnectionsImported = 2,
            ConnectionsUpdated = 3,
            ConnectionsSkipped = 1,
            GroupsImported = 4,
            CredentialsImported = 5,
            CredentialsUpdated = 6,
            SettingsImported = true
        };
        var summary = fullSuccess.SummaryText;
        Assert.Contains("2 connections added", summary);
        Assert.Contains("3 connections updated", summary);
        Assert.Contains("1 connections skipped", summary);
        Assert.Contains("4 groups added", summary);
        Assert.Contains("5 credentials added", summary);
        Assert.Contains("6 credentials updated", summary);
        Assert.Contains("Settings restored", summary);
    }
    #endregion

    #region 2. SqliteDatabaseService Schema Migration & Error Tests
    [Fact]
    public async Task SqliteDatabaseService_SchemaMigration_CoversAlterColumns()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"Migrate_{Guid.NewGuid():N}.db");
        try
        {
            // Create legacy schema without IsBookmarked and SortOrder
            using (var conn = new SqliteConnection($"Data Source={tempDb}"))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE Groups (Id TEXT PRIMARY KEY, Name TEXT, Description TEXT, SortOrder INTEGER DEFAULT 0, CreatedAt TEXT);
                    CREATE TABLE Credentials (Id TEXT PRIMARY KEY, Title TEXT, Username TEXT, EncryptedPassword TEXT, Domain TEXT, Notes TEXT, CreatedAt TEXT, UpdatedAt TEXT);
                    CREATE TABLE Connections (Id TEXT PRIMARY KEY, Name TEXT, Host TEXT, Port INTEGER, Protocol TEXT, DisplayMode TEXT, GroupId TEXT, CredentialId TEXT, CreatedAt TEXT, UpdatedAt TEXT);
                    CREATE TABLE Settings (Key TEXT PRIMARY KEY, Value TEXT);
                ";
                await cmd.ExecuteNonQueryAsync();
            }

            var db = new SqliteDatabaseService(tempDb);
            await db.InitializeAsync();

            // Verify both columns were added via PRAGMA
            using (var conn = new SqliteConnection($"Data Source={tempDb}"))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA table_info(Connections);";
                using var reader = await cmd.ExecuteReaderAsync();
                var columns = new List<string>();
                while (await reader.ReadAsync())
                {
                    columns.Add(reader.GetString(1));
                }

                Assert.Contains("IsBookmarked", columns);
                Assert.Contains("SortOrder", columns);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(tempDb))
            {
                try { File.Delete(tempDb); } catch { }
            }
        }
    }

    [Fact]
    public async Task SqliteDatabaseService_UpdateConnectionsSortOrder_ThrowsOnError()
    {
        var invalidDb = new SqliteDatabaseService("Data Source=:memory:;");
        // Not initialized, so Connections table doesn't exist
        var items = new List<ConnectionItem>
        {
            new ConnectionItem { Id = Guid.NewGuid(), Name = "Server 1" }
        };

        await Assert.ThrowsAnyAsync<Exception>(() => invalidDb.UpdateConnectionsOrderAsync(items));
    }

    [Fact]
    public async Task SqliteDatabaseService_ClearAllData_ThrowsOnError()
    {
        var invalidDb = new SqliteDatabaseService("Data Source=:memory:;");
        await Assert.ThrowsAnyAsync<Exception>(() => invalidDb.ClearAllDataAsync());
    }
    #endregion

    #region 3. ConnectionCardViewModel Ping Timeout Cancellation
    [Fact]
    public async Task ConnectionCardViewModel_PingAsync_CancellationTimeout_SetsOffline()
    {
        var model = new ConnectionItem
        {
            Name = "Unreachable Test",
            Host = "192.0.2.1", // TEST-NET-1 (non-routable IP)
            Port = 65534
        };
        var card = new ConnectionCardViewModel(model);
        card.PingTimeoutMs = 1; // 1ms timeout guarantees immediate OperationCanceledException

        await card.PingAsync();

        Assert.Equal(ConnectionPingStatus.Offline, card.PingStatus);
        Assert.False(card.IsPinging);
    }
    #endregion

    #region 4. LogsViewModel Background SafeDispatch Test
    [Fact]
    public async Task LogsViewModel_SafeDispatch_ExecutesFromBackgroundThread()
    {
        var vm = new LogsViewModel();
        bool executed = false;

        await Task.Run(() =>
        {
            vm.SafeDispatch(() =>
            {
                executed = true;
            });
        });

        // Give dispatcher time to process if dispatched
        await Task.Delay(50);
        Assert.True(executed);
    }
    #endregion

    #region 5. GitHubUpdateService Comprehensive Tests
    private class SimpleMockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public SimpleMockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    [Fact]
    public async Task GitHubUpdateService_CheckForUpdates_IncludePrereleases_ParsesArray()
    {
        var releaseJson = """
        [
            {
                "tag_name": "v2.0.0-rc.1",
                "name": "RemoteManager v2.0.0 Release Candidate",
                "body": "Preview of 2.0",
                "html_url": "https://github.com/vyntechau/RemoteManager/releases/tag/v2.0.0-rc.1",
                "prerelease": true,
                "assets": []
            }
        ]
        """;

        var handler = new SimpleMockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
        });

        var service = new GitHubUpdateService(new HttpClient(handler), currentVersion: "1.2.0");
        var result = await service.CheckForUpdatesAsync(includePrereleases: true);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("2.0.0", result.LatestRelease!.Version);
    }

    [Fact]
    public async Task GitHubUpdateService_CheckForUpdates_EmptyArrayOrNull_ReturnsUpToDate()
    {
        var handler = new SimpleMockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json")
        });

        var service = new GitHubUpdateService(new HttpClient(handler), currentVersion: "1.2.0");
        var result = await service.CheckForUpdatesAsync(includePrereleases: true);

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
    }

    [Fact]
    public async Task GitHubUpdateService_CheckForUpdates_InvalidJson_ReturnsFailed()
    {
        var handler = new SimpleMockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ not valid json", Encoding.UTF8, "application/json")
        });

        var service = new GitHubUpdateService(new HttpClient(handler), currentVersion: "1.2.0");
        var result = await service.CheckForUpdatesAsync();

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
    }

    [Fact]
    public async Task GitHubUpdateService_DownloadAssetAsync_DownloadsAndReportsProgress()
    {
        var bytes = Encoding.UTF8.GetBytes("Fake MSI installer file content");
        var handler = new SimpleMockHttpMessageHandler(req =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            };
            resp.Content.Headers.ContentLength = bytes.Length;
            return resp;
        });

        var service = new GitHubUpdateService(new HttpClient(handler), currentVersion: "1.2.0");
        var asset = new UpdateAssetInfo
        {
            Name = "RemoteManager-Setup.msi",
            DownloadUrl = "https://github.com/download/msi",
            Size = bytes.Length
        };

        var tempDir = Path.Combine(Path.GetTempPath(), $"Down_{Guid.NewGuid():N}");
        try
        {
            double maxProgress = 0;
            var progress = new Progress<double>(p => { maxProgress = Math.Max(maxProgress, p); });

            var downloadedPath = await service.DownloadAssetAsync(asset, tempDir, progress);

            Assert.True(File.Exists(downloadedPath));
            Assert.Equal(bytes, File.ReadAllBytes(downloadedPath));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task GitHubUpdateService_VerifyChecksumAsync_HandlesScenarios()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"Verify_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempFile, "Hello World");
        var fileName = Path.GetFileName(tempFile);

        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes("Hello World"));
        var hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

        var validChecksums = $"{hash}  {fileName}\n";
        var invalidChecksums = $"1234567890abcdef  {fileName}\n";

        try
        {
            // 1. Nonexistent local file
            var service = new GitHubUpdateService();
            var resNonexistent = await service.VerifyChecksumAsync("C:\\NonExistent_Fake_12345.bin", "http://any.url");
            Assert.False(resNonexistent);

            // 2. Successful checksum match
            var handlerValid = new SimpleMockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(validChecksums)
            });
            var serviceValid = new GitHubUpdateService(new HttpClient(handlerValid));
            var resValid = await serviceValid.VerifyChecksumAsync(tempFile, "http://valid.url");
            Assert.True(resValid);

            // 3. Failed checksum mismatch
            var handlerInvalid = new SimpleMockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(invalidChecksums)
            });
            var serviceInvalid = new GitHubUpdateService(new HttpClient(handlerInvalid));
            var resInvalid = await serviceInvalid.VerifyChecksumAsync(tempFile, "http://invalid.url");
            Assert.False(resInvalid);

            // 4. Empty checksums text
            var handlerEmpty = new SimpleMockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("")
            });
            var serviceEmpty = new GitHubUpdateService(new HttpClient(handlerEmpty));
            var resEmpty = await serviceEmpty.VerifyChecksumAsync(tempFile, "http://empty.url");
            Assert.False(resEmpty);

            // 5. Network exception during fetch
            var handlerError = new SimpleMockHttpMessageHandler(req => throw new HttpRequestException("Network down"));
            var serviceError = new GitHubUpdateService(new HttpClient(handlerError));
            var resError = await serviceError.VerifyChecksumAsync(tempFile, "http://error.url");
            Assert.False(resError);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void GitHubUpdateService_LaunchInstaller_HandlesAllScenarios()
    {
        var prevLauncher = GitHubUpdateService.ProcessLauncher;
        var tempFile = Path.Combine(Path.GetTempPath(), $"Installer_{Guid.NewGuid():N}.msi");
        File.WriteAllText(tempFile, "fake msi");

        try
        {
            var service = new GitHubUpdateService();

            // 1. Nonexistent installer
            Assert.False(service.LaunchInstaller("C:\\nonexistent_installer.msi"));

            // 2. Successful launch
            GitHubUpdateService.ProcessLauncher = psi => new Process();
            Assert.True(service.LaunchInstaller(tempFile));

            // 3. Launcher returns null
            GitHubUpdateService.ProcessLauncher = psi => null;
            Assert.False(service.LaunchInstaller(tempFile));

            // 4. Launcher throws exception
            GitHubUpdateService.ProcessLauncher = psi => throw new InvalidOperationException("Launch failed");
            Assert.False(service.LaunchInstaller(tempFile));
        }
        finally
        {
            GitHubUpdateService.ProcessLauncher = prevLauncher;
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
    #endregion

    #region 6. ExportImportService Comprehensive Coverage
    [Fact]
    public async Task ExportImportService_SelectiveExport_And_FileIO()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"Exp_{Guid.NewGuid():N}.db");
        var db = new SqliteDatabaseService(tempDb);
        await db.InitializeAsync();
        var crypto = new DpapiEncryptionService();
        var service = new ExportImportService(db, crypto);

        var group1 = new ConnectionGroup { Id = Guid.NewGuid(), Name = "Production" };
        var group2 = new ConnectionGroup { Id = Guid.NewGuid(), Name = "Staging" };
        await db.SaveGroupAsync(group1);
        await db.SaveGroupAsync(group2);

        var cred1 = new Credential { Id = Guid.NewGuid(), Title = "Prod Admin", Username = "admin", EncryptedPassword = crypto.Encrypt("secret") };
        var cred2 = new Credential { Id = Guid.NewGuid(), Title = "Staging Admin", Username = "user", EncryptedPassword = crypto.Encrypt("test") };
        await db.SaveCredentialAsync(cred1);
        await db.SaveCredentialAsync(cred2);

        var conn1 = new ConnectionItem { Id = Guid.NewGuid(), Name = "Prod Server", Host = "10.0.0.1", GroupId = group1.Id, CredentialId = cred1.Id };
        var conn2 = new ConnectionItem { Id = Guid.NewGuid(), Name = "Stage Server", Host = "10.0.0.2", GroupId = group2.Id, CredentialId = cred2.Id };
        await db.SaveConnectionAsync(conn1);
        await db.SaveConnectionAsync(conn2);

        // 1. Selective Export with SelectedConnectionIds
        var options = new ExportOptions
        {
            IncludeConnections = true,
            IncludeGroups = true,
            IncludeCredentials = true,
            IncludePasswords = true,
            Passphrase = "Pass",
            SelectedConnectionIds = [conn1.Id]
        };

        var json = await service.ExportToJsonAsync(options);
        Assert.Contains("Prod Server", json);
        Assert.DoesNotContain("Stage Server", json);

        // 2. ExportToJsonFileAsync
        var tempJsonPath = Path.Combine(Path.GetTempPath(), $"Package_{Guid.NewGuid():N}.json");
        try
        {
            await service.ExportToJsonFileAsync(tempJsonPath, options);
            Assert.True(File.Exists(tempJsonPath));
        }
        finally
        {
            if (File.Exists(tempJsonPath)) File.Delete(tempJsonPath);
        }

        // 3. ExportToCsvFileAsync
        var tempCsvPath = Path.Combine(Path.GetTempPath(), $"Export_{Guid.NewGuid():N}.csv");
        try
        {
            await service.ExportToCsvFileAsync(tempCsvPath, [conn1]);
            Assert.True(File.Exists(tempCsvPath));
            var csvContent = File.ReadAllText(tempCsvPath);
            Assert.Contains("Prod Server", csvContent);
        }
        finally
        {
            if (File.Exists(tempCsvPath)) File.Delete(tempCsvPath);
        }

        // 4. ExportToRdpFileAsync
        var tempRdpPath = Path.Combine(Path.GetTempPath(), $"Export_{Guid.NewGuid():N}.rdp");
        try
        {
            await service.ExportToRdpFileAsync(tempRdpPath, conn1, cred1);
            Assert.True(File.Exists(tempRdpPath));
            var rdpContent = File.ReadAllText(tempRdpPath);
            Assert.Contains("full address:s:10.0.0.1:3389", rdpContent);
            Assert.Contains("username:s:admin", rdpContent);
        }
        finally
        {
            if (File.Exists(tempRdpPath)) File.Delete(tempRdpPath);
            SqliteConnection.ClearAllPools();
            if (File.Exists(tempDb)) try { File.Delete(tempDb); } catch { }
        }
    }

    [Fact]
    public async Task ExportImportService_ImportFromJson_ConflictResolutions_And_Validation()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"Imp_{Guid.NewGuid():N}.db");
        var db = new SqliteDatabaseService(tempDb);
        await db.InitializeAsync();
        var crypto = new DpapiEncryptionService();
        var service = new ExportImportService(db, crypto);

        // Empty / whitespace
        var resEmpty = await service.ImportFromJsonAsync("", new ImportOptions());
        Assert.False(resEmpty.Success);

        // Malformed JSON
        var resMalformed = await service.ImportFromJsonAsync("{ not valid json", new ImportOptions());
        Assert.False(resMalformed.Success);

        // Encrypted package without passphrase returns error
        var encryptedPackage = new ExportPackage
        {
            Version = "1.0",
            HasEncryptedCredentials = true,
            EncryptionAlgorithm = "AES-256-GCM-PBKDF2",
            Credentials = [new ExportCredentialItem { Id = Guid.NewGuid(), Title = "Sec", Username = "user", EncryptedPassword = "xyz" }]
        };
        var encJson = JsonSerializer.Serialize(encryptedPackage);
        var resEncrypted = await service.ImportFromJsonAsync(encJson, new ImportOptions { Passphrase = null });
        Assert.False(resEncrypted.Success);
        Assert.Contains("protected with a passphrase", resEncrypted.ErrorMessage);

        // Prepopulate database with an existing group, credential, and connection
        var existingGroup = new ConnectionGroup { Id = Guid.NewGuid(), Name = "Operations" };
        await db.SaveGroupAsync(existingGroup);

        var existingCred = new Credential { Id = Guid.NewGuid(), Title = "Op Admin", Username = "opadmin", Domain = "CORP", EncryptedPassword = crypto.Encrypt("oldpass") };
        await db.SaveCredentialAsync(existingCred);

        var existingConn = new ConnectionItem { Id = Guid.NewGuid(), Name = "Server Alpha", Host = "10.0.0.1", Port = 3389, GroupId = existingGroup.Id, CredentialId = existingCred.Id };
        await db.SaveConnectionAsync(existingConn);

        // Build an import package with same keys but updated properties to test OverwriteExisting
        var package = new ExportPackage
        {
            Version = "1.0",
            ExportedAt = DateTime.UtcNow,
            Groups = [new ConnectionGroup { Id = Guid.NewGuid(), Name = "Operations" }],
            Credentials = [new ExportCredentialItem { Id = Guid.NewGuid(), Title = "Op Admin", Username = "opadmin", Domain = "CORP", EncryptedPassword = crypto.Encrypt("newpassword"), Notes = "Updated notes" }],
            Connections = [new ConnectionItem { Id = Guid.NewGuid(), Name = "Server Alpha", Host = "10.0.0.1", Port = 3389, DisplayMode = DisplayMode.Fullscreen }],
            Settings = new AppSettings { Theme = "Dark", AutoReconnect = true }
        };

        var packageJson = JsonSerializer.Serialize(package);

        // Test OverwriteExisting
        var resOverwrite = await service.ImportFromJsonAsync(packageJson, new ImportOptions
        {
            ConflictResolution = ImportConflictResolution.OverwriteExisting,
            ImportSettings = true
        });

        Assert.True(resOverwrite.Success);
        Assert.Equal(1, resOverwrite.ConnectionsUpdated);
        Assert.Equal(1, resOverwrite.CredentialsUpdated);
        Assert.True(resOverwrite.SettingsImported);

        // Verify updated connection
        var updatedConns = await db.GetAllConnectionsAsync();
        var alphaConn = updatedConns.First(c => c.Name == "Server Alpha");
        Assert.Equal(DisplayMode.Fullscreen, alphaConn.DisplayMode);

        // Verify updated settings
        var settings = await db.GetSettingsAsync();
        Assert.Equal("Dark", settings.Theme);

        // Test CleanAndReplace
        var cleanPackage = new ExportPackage
        {
            Version = "1.0",
            ExportedAt = DateTime.UtcNow,
            Connections = [new ConnectionItem { Id = Guid.NewGuid(), Name = "Brand New Server", Host = "192.168.1.100" }]
        };
        var cleanJson = JsonSerializer.Serialize(cleanPackage);

        var resClean = await service.ImportFromJsonAsync(cleanJson, new ImportOptions
        {
            ConflictResolution = ImportConflictResolution.CleanAndReplace
        });

        Assert.True(resClean.Success);
        var finalConns = await db.GetAllConnectionsAsync();
        Assert.Single(finalConns);
        Assert.Equal("Brand New Server", finalConns[0].Name);
        SqliteConnection.ClearAllPools();
        if (File.Exists(tempDb)) try { File.Delete(tempDb); } catch { }
    }

    [Fact]
    public async Task ExportImportService_CsvImport_EdgeCases_And_AlternativeHeaders()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"Csv_{Guid.NewGuid():N}.db");
        var db = new SqliteDatabaseService(tempDb);
        await db.InitializeAsync();
        var crypto = new DpapiEncryptionService();
        var service = new ExportImportService(db, crypto);

        // 1. Empty CSV
        var emptyRes = await service.ImportFromCsvAsync("");
        Assert.False(emptyRes.Success);

        // 2. Only header row
        var headerOnlyRes = await service.ImportFromCsvAsync("Name,Host,Port");
        Assert.False(headerOnlyRes.Success);

        // 3. Import with quoted values, group, credential, and IsBookmarked
        var customCsv = """
        Name,Host,Protocol,Username,Domain,Port,DisplayMode,Notes,IsBookmarked,Group
        "Remote DB, MySQL",10.0.0.99,SSH,root,CORP,22,Tabbed,"Important DB server",true,DatabaseServers
        """;

        var importRes = await service.ImportFromCsvAsync(customCsv, ImportConflictResolution.MergeAndKeepExisting);
        Assert.True(importRes.Success);
        Assert.Equal(1, importRes.ConnectionsImported);

        var conns = await db.GetAllConnectionsAsync();
        var conn = conns.First();
        Assert.Equal("Remote DB, MySQL", conn.Name);
        Assert.Equal("10.0.0.99", conn.Host);
        Assert.Equal(ProtocolType.SSH, conn.Protocol);
        Assert.True(conn.IsBookmarked);

        // 4. OverwriteExisting via CSV with matching Host, Name, Port
        var overwriteCsv = """
        Name,Host,Protocol,Username,Domain,Port,DisplayMode,Notes,IsBookmarked,Group
        "Remote DB, MySQL",10.0.0.99,SSH,root,CORP,22,Fullscreen,"Important DB server",false,DatabaseServers
        """;

        var overwriteRes = await service.ImportFromCsvAsync(overwriteCsv, ImportConflictResolution.OverwriteExisting);
        Assert.True(overwriteRes.Success);
        Assert.Equal(1, overwriteRes.ConnectionsUpdated);

        var reloaded = (await db.GetAllConnectionsAsync()).First();
        Assert.Equal(DisplayMode.Fullscreen, reloaded.DisplayMode);
        Assert.False(reloaded.IsBookmarked);

        // 5. CleanAndReplace via CSV
        var cleanCsv = """
        Name,Host,Port
        FreshBox,10.0.0.50,3389
        """;
        var cleanRes = await service.ImportFromCsvAsync(cleanCsv, ImportConflictResolution.CleanAndReplace);
        Assert.True(cleanRes.Success);
        var finalConns = await db.GetAllConnectionsAsync();
        Assert.Single(finalConns);
        Assert.Equal("FreshBox", finalConns[0].Name);
        SqliteConnection.ClearAllPools();
        if (File.Exists(tempDb)) try { File.Delete(tempDb); } catch { }
    }

    [Fact]
    public async Task ExportImportService_RdpImport_Files_And_ConflictResolution()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"Rdp_{Guid.NewGuid():N}.db");
        var db = new SqliteDatabaseService(tempDb);
        await db.InitializeAsync();
        var crypto = new DpapiEncryptionService();
        var service = new ExportImportService(db, crypto);

        // Prepopulate with a connection
        var existing = new ConnectionItem { Id = Guid.NewGuid(), Name = "Server1", Host = "10.0.0.1", Port = 3389, DisplayMode = DisplayMode.Tabbed };
        await db.SaveConnectionAsync(existing);

        var rdpContent = """
        full address:s:10.0.0.1:3389
        username:s:Administrator
        domain:s:CORP
        screen mode id:i:2
        """;

        // 1. Import from RDP string content (creates new connection & credential)
        var newConn = await service.ImportFromRdpAsync(rdpContent, "Server1");
        Assert.NotNull(newConn);
        Assert.Equal("10.0.0.1", newConn.Host);
        Assert.Equal(3389, newConn.Port);
        Assert.NotNull(newConn.CredentialId);

        // 2. Import again with same username & domain to test reusing existing credential
        var reuseConn = await service.ImportFromRdpAsync(rdpContent, "Server2");
        Assert.NotNull(reuseConn);
        Assert.Equal(newConn.CredentialId, reuseConn.CredentialId);

        // 3. ImportFromRdpFileAsync with temp file
        var tempRdpFile = Path.Combine(Path.GetTempPath(), $"TestServer_{Guid.NewGuid():N}.rdp");
        try
        {
            File.WriteAllText(tempRdpFile, "full address:s:192.168.1.200:3389\r\nusername:s:user");
            var fileConn = await service.ImportFromRdpFileAsync(tempRdpFile);
            Assert.NotNull(fileConn);
            Assert.Equal("192.168.1.200", fileConn.Host);

            // Test non-existent file throws FileNotFoundException
            await Assert.ThrowsAsync<FileNotFoundException>(() => service.ImportFromRdpFileAsync(tempRdpFile + ".missing"));
        }
        finally
        {
            if (File.Exists(tempRdpFile)) File.Delete(tempRdpFile);
            SqliteConnection.ClearAllPools();
            if (File.Exists(tempDb)) try { File.Delete(tempDb); } catch { }
        }
    }
    #endregion

    #region 7. ConnectionsViewModel Full Coverage Tests
    [Fact]
    public async Task ConnectionsViewModel_FiltersAndSorting_FullCoverage()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"CVM_{Guid.NewGuid():N}.db");
        var db = new SqliteDatabaseService(tempDb);
        await db.InitializeAsync();
        var crypto = new DpapiEncryptionService();
        var mainVM = new MainViewModel(db, crypto);
        var cvm = mainVM.ConnectionsVM;

        var conn1 = new ConnectionItem { Id = Guid.NewGuid(), Name = "Alpha", Host = "10.0.0.1", Port = 3389, Protocol = ProtocolType.RDP, IsBookmarked = true, DisplayMode = DisplayMode.Tabbed };
        var conn2 = new ConnectionItem { Id = Guid.NewGuid(), Name = "Beta", Host = "10.0.0.2", Port = 22, Protocol = ProtocolType.SSH, IsBookmarked = false, DisplayMode = DisplayMode.Fullscreen };
        var conn3 = new ConnectionItem { Id = Guid.NewGuid(), Name = "Gamma", Host = "10.0.0.3", Port = 5900, Protocol = ProtocolType.VNC, IsBookmarked = false, DisplayMode = DisplayMode.ExternalApp };
        var conn4 = new ConnectionItem { Id = Guid.NewGuid(), Name = "Delta", Host = "https://web.internal", Port = 443, Protocol = ProtocolType.Web, IsBookmarked = false, DisplayMode = DisplayMode.Tabbed };

        await db.SaveConnectionAsync(conn1);
        await db.SaveConnectionAsync(conn2);
        await db.SaveConnectionAsync(conn3);
        await db.SaveConnectionAsync(conn4);
        await mainVM.LoadDataAsync();

        // ActiveFilterSummary coverage
        cvm.IsFilterAll = false;
        cvm.IsFilterBookmarked = true;
        Assert.Contains("Bookmarked", cvm.ActiveFilterSummary);

        cvm.IsFilterRdp = true;
        Assert.Contains("RDP", cvm.ActiveFilterSummary);

        cvm.IsFilterSsh = true;
        cvm.IsFilterVnc = true;
        cvm.IsFilterWeb = true;
        cvm.IsFilterOnlineOnly = true;
        Assert.Contains("Filters", cvm.ActiveFilterSummary);

        // Reset to All
        cvm.IsFilterAll = true;
        Assert.Contains("All Servers", cvm.ActiveFilterSummary);

        // Icons and Brushes
        var cols = new[] { "Bookmark", "Status", "Protocol", "DisplayName", "Endpoint", "Credential", "DisplayMode", "Unknown" };
        foreach (var col in cols)
        {
            cvm.CurrentSortColumn = col;
            cvm.IsSortAscending = true;
            _ = cvm.GetColumnSortIcon(col);
            _ = cvm.GetColumnSortIconBrush(col);

            cvm.IsSortAscending = false;
            _ = cvm.GetColumnSortIcon(col);
            _ = cvm.GetColumnSortIconBrush(col);
        }

        _ = cvm.BookmarkSortIcon;
        _ = cvm.BookmarkSortIconBrush;
        _ = cvm.StatusSortIcon;
        _ = cvm.StatusSortIconBrush;
        _ = cvm.ProtocolSortIcon;
        _ = cvm.ProtocolSortIconBrush;
        _ = cvm.NameSortIcon;
        _ = cvm.NameSortIconBrush;
        _ = cvm.EndpointSortIcon;
        _ = cvm.EndpointSortIconBrush;
        _ = cvm.CredentialSortIcon;
        _ = cvm.CredentialSortIconBrush;
        _ = cvm.DisplayModeSortIcon;
        _ = cvm.DisplayModeSortIconBrush;

        // SelectedSortOption coverage
        var sortOptions = new[] { "Custom Order", "Name (A-Z)", "Name (Z-A)", "Protocol", "Host / IP", "Port", "Other" };
        foreach (var opt in sortOptions)
        {
            cvm.CurrentSortColumn = string.Empty;
            cvm.SelectedSortOption = opt;
            cvm.ApplyFilterAndSort();
        }

        // CurrentSortColumn switches
        foreach (var col in new[] { "Bookmark", "Status", "Protocol", "DisplayName", "Endpoint", "Credential", "DisplayMode", "Fallback" })
        {
            cvm.CurrentSortColumn = col;
            cvm.IsSortAscending = true;
            cvm.ApplyFilterAndSort();

            cvm.IsSortAscending = false;
            cvm.ApplyFilterAndSort();
        }

        // Protocol filters
        cvm.SelectedProtocolFilter = "Bookmarked";
        cvm.ApplyFilterAndSort();

        cvm.SelectedProtocolFilter = "RDP";
        cvm.ApplyFilterAndSort();

        cvm.SelectedProtocolFilter = "All";
        cvm.ApplyFilterAndSort();

        // ToggleBookmarkAsync parameter types
        await cvm.ToggleBookmarkAsync(cvm.FilteredCards[0]);
        await cvm.ToggleBookmarkAsync(conn1);
        await cvm.ToggleBookmarkAsync(null);

        // ReorderCardsAsync
        await cvm.ReorderCardsAsync(null!, null!);
        if (cvm.FilteredCards.Count >= 2)
        {
            await cvm.ReorderCardsAsync(cvm.FilteredCards[0], cvm.FilteredCards[1]);
        }
        SqliteConnection.ClearAllPools();
        if (File.Exists(tempDb)) try { File.Delete(tempDb); } catch { }
    }
    #endregion

    #region 8. MainViewModel Coverage Tests
    private class FakeUpdateServiceMock : IUpdateService
    {
        public string CurrentVersion => "1.2.0";
        public UpdateCheckResult CheckResult { get; set; } = UpdateCheckResult.UpToDate("1.2.0");
        public bool LaunchInstallerResult { get; set; } = true;
        public bool VerifyChecksumResult { get; set; } = true;

        public Task<UpdateCheckResult> CheckForUpdatesAsync(bool includePrereleases = false, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CheckResult);
        }

        public bool ReportProgress { get; set; } = false;

        public Task<string> DownloadAssetAsync(UpdateAssetInfo asset, string destinationDirectory, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            if (ReportProgress)
            {
                progress?.Report(0.5);
            }
            return Task.FromResult(Path.Combine(destinationDirectory, asset.Name));
        }

        public Task<bool> VerifyChecksumAsync(string localFilePath, string checksumsUrl, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(VerifyChecksumResult);
        }

        public bool LaunchInstaller(string installerPath) => LaunchInstallerResult;
    }

    [Fact]
    public async Task MainViewModel_FullBranchCoverage()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"MVM_{Guid.NewGuid():N}.db");
        var db = new SqliteDatabaseService(tempDb);
        await db.InitializeAsync();
        var crypto = new DpapiEncryptionService();
        var updateService = new FakeUpdateServiceMock();
        var mainVM = new MainViewModel(db, crypto, updateService);
        mainVM.ShowErrorMessage = (t, m) => { };
        mainVM.ShowWarningMessage = (t, m) => { };
        mainVM.SafeDispatch = action => action();

        // Properties
        Assert.NotNull(mainVM.UpdateService);
        Assert.NotNull(mainVM.ExportImportService);
        Assert.Equal("1.2.0", mainVM.CurrentAppVersion);
        mainVM.RequestNavigateToAbout = () => { };
        mainVM.RequestNavigateToAbout.Invoke();

        var conn1 = new ConnectionItem { Id = Guid.NewGuid(), Name = "First", Host = "10.0.0.1", IsBookmarked = true };
        var conn2 = new ConnectionItem { Id = Guid.NewGuid(), Name = "Second", Host = "10.0.0.2", IsBookmarked = false };
        await db.SaveConnectionAsync(conn1);
        await db.SaveConnectionAsync(conn2);
        await mainVM.LoadDataAsync();

        // ToggleBookmarkAsync with card and null
        var card = new ConnectionCardViewModel(conn2);
        await mainVM.ToggleBookmarkAsync(card);
        await mainVM.ToggleBookmarkAsync(null);

        // ReorderConnectionsAsync edge cases
        await mainVM.ReorderConnectionsAsync(-1, 0);
        await mainVM.ReorderConnectionsAsync(0, 999);
        await mainVM.ReorderConnectionsAsync(0, 0);
        await mainVM.ReorderConnectionsAsync(0, 1);

        // ReorderConnectionItemAsync edge cases
        await mainVM.ReorderConnectionItemAsync(null!, null!);
        await mainVM.ReorderConnectionItemAsync(conn1, conn1);
        await mainVM.ReorderConnectionItemAsync(conn1, conn2);

        // AddConnectionAsync fallbacks
        mainVM.RequestConnectionEditorPage = null;
        mainVM.RequestConnectionEditor = null;
        await mainVM.AddConnectionAsync();

        // EditConnectionAsync fallbacks
        await mainVM.EditConnectionAsync(null);
        await mainVM.EditConnectionAsync(conn1);

        // DeleteConnectionAsync with null
        await mainVM.DeleteConnectionAsync(null);

        // ExportDataAsync parameter types
        mainVM.RequestExportDialog = null;
        await mainVM.ExportDataAsync(card);
        await mainVM.ExportDataAsync(new[] { conn1.Id });

        // ImportDataAsync with null handler
        mainVM.RequestImportDialog = null;
        await mainVM.ImportDataAsync();

        // ExportConnectionToRdpAsync edge cases
        await mainVM.ExportConnectionToRdpAsync(null);

        mainVM.RequestSaveFileDialog = defaultFilename => Task.FromResult<string?>(null);
        await mainVM.ExportConnectionToRdpAsync(conn1);

        mainVM.RequestSaveFileDialog = defaultFilename => Task.FromResult<string?>(Path.Combine(Path.GetTempPath(), "test.rdp"));
        await mainVM.ExportConnectionToRdpAsync(conn1);

        // CheckForUpdatesAsync exception handling
        var throwingUpdateService = new ThrowingUpdateService();
        var mainVMError = new MainViewModel(db, crypto, throwingUpdateService);
        mainVMError.ShowErrorMessage = (t, m) => { };
        mainVMError.ShowWarningMessage = (t, m) => { };
        mainVMError.SafeDispatch = action => action();
        await mainVMError.CheckForUpdatesAsync(silentOnUpToDate: false);
        Assert.Equal("Update check failed", mainVMError.UpdateStatusTitle);

        await mainVMError.CheckForUpdatesAsync(silentOnUpToDate: true);

        // DownloadAndInstallUpdateAsync coverage
        // 1. LatestRelease is null
        mainVM.LatestRelease = null;
        await mainVM.DownloadAndInstallUpdateAsync();

        // 2. MsiAsset is null (calls OpenReleasePage)
        mainVM.LatestRelease = new UpdateReleaseInfo { TagName = "v1.3.0", Version = "1.3.0", HtmlUrl = "https://github.com/vyntechau/RemoteManager" };
        var prevLauncher = MainViewModel.ProcessLauncher;
        try
        {
            MainViewModel.ProcessLauncher = psi => new Process();
            await mainVM.DownloadAndInstallUpdateAsync();

            // 3. MsiAsset present with ChecksumsAsset passing verification
            var releaseWithMsi = new UpdateReleaseInfo
            {
                TagName = "v1.3.0",
                Version = "1.3.0",
                Assets = [
                    new UpdateAssetInfo { Name = "RemoteManager.msi", DownloadUrl = "https://github.com/download/msi" },
                    new UpdateAssetInfo { Name = "checksums.txt", DownloadUrl = "https://github.com/download/checksums" },
                    new UpdateAssetInfo { Name = "RemoteManager-portable.zip", DownloadUrl = "https://github.com/download/zip" }
                ]
            };
            mainVM.LatestRelease = releaseWithMsi;
            // Confirm = false branch
            mainVM.RequestConfirmation = (title, message) => false;
            await mainVM.DownloadAndInstallUpdateAsync();

            // Confirm = true, LaunchInstaller fails branch (covers confirm=true and error message)
            mainVM.RequestConfirmation = (title, message) => true;
            updateService.LaunchInstallerResult = false;
            await mainVM.DownloadAndInstallUpdateAsync();

            // 4. Checksum verification fails
            updateService.VerifyChecksumResult = false;
            await mainVM.DownloadAndInstallUpdateAsync();
            Assert.Equal("Checksum verification failed", mainVM.FooterUpdateStatusText);

            // 5. OpenReleasePage & DownloadPortableZip
            mainVM.OpenReleasePage();
            mainVM.DownloadPortableZip();

            // 6. ProcessLauncher throwing exception in OpenReleasePage & DownloadPortableZip
            MainViewModel.ProcessLauncher = psi => throw new InvalidOperationException("Failed to open");
            mainVM.OpenReleasePage();
            mainVM.DownloadPortableZip();
        }
        finally
        {
            MainViewModel.ProcessLauncher = prevLauncher;
        }

        // SafeDispatch execution test
        bool syncExecuted = false;
        mainVM.SafeDispatch = action => action();
        mainVM.SafeDispatch(() => syncExecuted = true);
        Assert.True(syncExecuted);

        SqliteConnection.ClearAllPools();
        if (File.Exists(tempDb)) try { File.Delete(tempDb); } catch { }
    }

    private class ThrowingUpdateService : IUpdateService
    {
        public string CurrentVersion => "1.2.0";
        public Task<UpdateCheckResult> CheckForUpdatesAsync(bool includePrereleases = false, CancellationToken cancellationToken = default)
            => throw new HttpRequestException("Simulated connection timeout");
        public Task<string> DownloadAssetAsync(UpdateAssetInfo asset, string destinationDirectory, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<bool> VerifyChecksumAsync(string localFilePath, string checksumsUrl, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public bool LaunchInstaller(string installerPath) => false;
    }

    private class ThrowingDownloadService : IUpdateService
    {
        public string CurrentVersion => "1.2.0";
        public Task<UpdateCheckResult> CheckForUpdatesAsync(bool includePrereleases = false, CancellationToken cancellationToken = default)
            => Task.FromResult(UpdateCheckResult.UpToDate("1.2.0"));
        public Task<string> DownloadAssetAsync(UpdateAssetInfo asset, string destinationDirectory, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
            => throw new HttpRequestException("Simulated download failure");
        public Task<bool> VerifyChecksumAsync(string localFilePath, string checksumsUrl, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
        public bool LaunchInstaller(string installerPath) => false;
    }
    #endregion

    #region 9. Final Full Line Coverage Target Tests
    [Fact]
    public void SafeDispatch_WithBackgroundDispatcher_CoversBeginInvoke()
    {
        System.Windows.Threading.Dispatcher? bgDispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            bgDispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            ready.Set();
            System.Windows.Threading.Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        ready.Wait();

        try
        {
            LogsViewModel.DispatcherProvider = () => bgDispatcher;
            var lvm = new LogsViewModel();
            lvm.SafeDispatch(() => { });

            ConnectionsViewModel.DispatcherProvider = () => bgDispatcher;
            var cvm = new ConnectionsViewModel(null!);
            cvm.SafeDispatch(() => { });

            MainViewModel.DispatcherProvider = () => bgDispatcher;
            var mvm = new MainViewModel(null!, null!, new FakeUpdateServiceMock(), null!);
            mvm.SafeDispatch(() => { });
        }
        finally
        {
            LogsViewModel.DispatcherProvider = () => Application.Current?.Dispatcher;
            ConnectionsViewModel.DispatcherProvider = () => Application.Current?.Dispatcher;
            MainViewModel.DispatcherProvider = () => Application.Current?.Dispatcher;
            bgDispatcher?.InvokeShutdown();
            thread.Join(500);
        }
    }

    [Fact]
    public async Task ConnectionsViewModel_RemainingBranches_Covered()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"CVM_Rem_{Guid.NewGuid():N}.db");
        var db = new SqliteDatabaseService(tempDb);
        await db.InitializeAsync();
        var crypto = new DpapiEncryptionService();
        var mainVM = new MainViewModel(db, crypto);
        mainVM.ShowErrorMessage = (t, m) => { };
        mainVM.ShowWarningMessage = (t, m) => { };
        mainVM.SafeDispatch = action => action();
        mainVM.RequestConfirmation = (t, m) => false;
        mainVM.RequestSaveFileDialog = _ => Task.FromResult<string?>(null);
        mainVM.RequestImportDialog = () => Task.FromResult(true);
        var cvm = mainVM.ConnectionsVM;

        var conn = new ConnectionItem { Id = Guid.NewGuid(), Name = "Alpha", Host = "10.0.0.1", Port = 3389 };
        await db.SaveConnectionAsync(conn);
        await mainVM.LoadDataAsync();

        // 1. Headers
        _ = cvm.BookmarkHeader;
        _ = cvm.StatusHeader;
        _ = cvm.CredentialHeader;
        _ = cvm.DisplayModeHeader;

        // 2. Sort column fallbacks
        cvm.SortByColumn("DisplayMode");
        cvm.SortByColumn("Custom");

        // 3. Filter toggles
        cvm.IsFilterSsh = true;
        cvm.IsFilterSsh = false;
        cvm.IsFilterVnc = true;
        cvm.IsFilterVnc = false;
        cvm.IsFilterWeb = true;
        cvm.IsFilterWeb = false;

        // 4. SelectedSortOption switches when CurrentSortColumn is empty
        foreach (var opt in new[] { "Name (A-Z)", "Name (Z-A)", "Protocol", "Host / IP", "Port", "Custom Order" })
        {
            cvm.SelectedSortOption = opt;
            cvm.CurrentSortColumn = string.Empty;
            cvm.ApplyFilterAndSort();
        }

        // 5. Delegation methods with ConnectionItem
        await cvm.DuplicateConnectionAsync(conn);
        await cvm.DeleteConnectionAsync(conn);
        await cvm.ExportConnectionToRdpAsync(conn);
        await cvm.ImportDataAsync();

        SqliteConnection.ClearAllPools();
        if (File.Exists(tempDb)) try { File.Delete(tempDb); } catch { }
    }

    [Fact]
    public async Task MainViewModel_RemainingBranches_Covered()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"MVM_Rem_{Guid.NewGuid():N}.db");
        var db = new SqliteDatabaseService(tempDb);
        await db.InitializeAsync();
        var crypto = new DpapiEncryptionService();
        var updateService = new FakeUpdateServiceMock();
        var mainVM = new MainViewModel(db, crypto, updateService);
        mainVM.ShowErrorMessage = (t, m) => { };
        mainVM.ShowWarningMessage = (t, m) => { };
        mainVM.SafeDispatch = action => action();
        mainVM.RequestConfirmation = (t, m) => false;
        mainVM.RequestConfirmWithNameAsync = (t, m, n, c) => Task.FromResult(false);
        mainVM.RequestSaveFileDialog = _ => Task.FromResult<string?>(null);

        var conn = new ConnectionItem { Id = Guid.NewGuid(), Name = "Server1", Host = "10.0.0.1", Port = 3389 };
        await db.SaveConnectionAsync(conn);
        var card = new ConnectionCardViewModel(conn);

        // 1. DeleteConnectionAsync & ExportConnectionToRdpAsync with ConnectionCardViewModel
        await mainVM.DeleteConnectionAsync(card);
        await mainVM.ExportConnectionToRdpAsync(card);

        // 2. SaveFileDialog branch via SaveFileDialogShower
        var tempExportRdp = Path.Combine(Path.GetTempPath(), $"test_sfd_{Guid.NewGuid():N}.rdp");
        var prevShower = MainViewModel.SaveFileDialogShower;
        try
        {
            MainViewModel.SaveFileDialogShower = sfd => { sfd.FileName = tempExportRdp; return true; };
            mainVM.RequestSaveFileDialog = null;
            await mainVM.ExportConnectionToRdpAsync(conn);
        }
        finally
        {
            MainViewModel.SaveFileDialogShower = prevShower;
            if (File.Exists(tempExportRdp)) try { File.Delete(tempExportRdp); } catch { }
        }

        // 3. Export to RDP exception handling
        mainVM.RequestSaveFileDialog = _ => Task.FromResult<string?>(Path.Combine(Path.GetTempPath(), "InvalidDir*?<>", "export.rdp"));
        await mainVM.ExportConnectionToRdpAsync(conn);

        // 4. Progress reporting in DownloadAndInstallUpdateAsync
        var releaseWithMsi = new UpdateReleaseInfo
        {
            TagName = "v1.3.0",
            Version = "1.3.0",
            Assets = [
                new UpdateAssetInfo { Name = "RemoteManager.msi", DownloadUrl = "https://github.com/download/msi" },
                new UpdateAssetInfo { Name = "checksums.txt", DownloadUrl = "https://github.com/download/checksums" }
            ]
        };
        mainVM.LatestRelease = releaseWithMsi;
        mainVM.RequestConfirmation = (t, m) => false;
        updateService.ReportProgress = true;
        await mainVM.DownloadAndInstallUpdateAsync();
        updateService.ReportProgress = false;

        // 4b. LaunchInstaller succeeds branch with intercepted SafeDispatch (covers lines 1112 and 1114)
        mainVM.RequestConfirmation = (t, m) => true;
        updateService.LaunchInstallerResult = true;
        mainVM.SafeDispatch = action => { /* intercepted to prevent Application.Current.Shutdown */ };
        await mainVM.DownloadAndInstallUpdateAsync();
        mainVM.SafeDispatch = action => action();

        // 5. Download exception handling in DownloadAndInstallUpdateAsync
        var downloadThrowingService = new ThrowingDownloadService();
        var mainVMDownloadError = new MainViewModel(db, crypto, downloadThrowingService);
        mainVMDownloadError.ShowErrorMessage = (t, m) => { };
        mainVMDownloadError.SafeDispatch = action => action();
        mainVMDownloadError.LatestRelease = releaseWithMsi;
        await mainVMDownloadError.DownloadAndInstallUpdateAsync();
        Assert.Equal("Download failed", mainVMDownloadError.FooterUpdateStatusText);

        SqliteConnection.ClearAllPools();
        if (File.Exists(tempDb)) try { File.Delete(tempDb); } catch { }
    }

    [Fact]
    public async Task ExportImportService_RemainingBranches_Covered()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"EIS_Rem_{Guid.NewGuid():N}.db");
        var db = new SqliteDatabaseService(tempDb);
        await db.InitializeAsync();
        var crypto = new DpapiEncryptionService();
        var service = new ExportImportService(db, crypto);

        // 1. Export with corrupted credential (catches decrypt error) & IncludeSettings = true
        var badCred = new Credential { Id = Guid.NewGuid(), Title = "Corrupt", Username = "user", EncryptedPassword = "invalid-base64-not-encrypted" };
        await db.SaveCredentialAsync(badCred);
        var exportOptions = new ExportOptions { IncludeCredentials = true, IncludePasswords = true, IncludeSettings = true, Passphrase = "MySecretPassphrase" };
        var json = await service.ExportToJsonAsync(exportOptions);
        Assert.Contains("Corrupt", json);

        // 2. Import package with missing salt/nonce
        var missingParamsPackage = new ExportPackage
        {
            Version = "1.0",
            HasEncryptedCredentials = true,
            EncryptionAlgorithm = "AES-256-GCM-PBKDF2",
            Credentials = [new ExportCredentialItem { Id = Guid.NewGuid(), Title = "C", Username = "u", EncryptedPassword = "abc" }]
        };
        var resMissing = await service.ImportFromJsonAsync(JsonSerializer.Serialize(missingParamsPackage), new ImportOptions { Passphrase = "pass" });
        Assert.False(resMissing.Success);
        Assert.Contains("Corrupted encryption parameters", resMissing.ErrorMessage);

        // 3. Import package with invalid base64 salt (throws generic Exception during decrypt)
        var invalidSaltPackage = new ExportPackage
        {
            Version = "1.0",
            HasEncryptedCredentials = true,
            EncryptionAlgorithm = "AES-256-GCM-PBKDF2",
            EncryptionSalt = "not-valid-base-64!!",
            EncryptionNonce = "not-valid-base-64!!",
            Credentials = [new ExportCredentialItem { Id = Guid.NewGuid(), Title = "C", Username = "u", EncryptedPassword = "abc" }]
        };
        var resInvalidSalt = await service.ImportFromJsonAsync(JsonSerializer.Serialize(invalidSaltPackage), new ImportOptions { Passphrase = "pass" });
        Assert.False(resInvalidSalt.Success);
        Assert.Contains("Error decrypting credentials", resInvalidSalt.ErrorMessage);

        // 4. OverwriteExisting with matching Group ID & Credential ID
        var grp = new ConnectionGroup { Id = Guid.NewGuid(), Name = "Grp1" };
        await db.SaveGroupAsync(grp);
        var cred = new Credential { Id = Guid.NewGuid(), Title = "Cred1", Username = "user1", EncryptedPassword = crypto.Encrypt("pass1") };
        await db.SaveCredentialAsync(cred);
        var conn = new ConnectionItem { Id = Guid.NewGuid(), Name = "Conn1", Host = "10.0.0.1", Port = 3389, GroupId = grp.Id, CredentialId = cred.Id };
        await db.SaveConnectionAsync(conn);

        var overwritePkg = new ExportPackage
        {
            Version = "1.0",
            Groups = [new ConnectionGroup { Id = grp.Id, Name = "Grp1-Renamed" }],
            Credentials = [new ExportCredentialItem { Id = cred.Id, Title = "Cred1-Renamed", Username = "user1", EncryptedPassword = crypto.Encrypt("newpass") }],
            Connections = [new ConnectionItem { Id = conn.Id, Name = "Conn1", Host = "10.0.0.1", Port = 3389, DisplayMode = DisplayMode.Fullscreen }]
        };
        var resOverwrite = await service.ImportFromJsonAsync(JsonSerializer.Serialize(overwritePkg), new ImportOptions { ConflictResolution = ImportConflictResolution.OverwriteExisting });
        Assert.True(resOverwrite.Success);

        // 5. MergeAndKeepExisting skips existing connection by ID and by HostName
        var skipByIdPkg = new ExportPackage
        {
            Version = "1.0",
            Connections = [new ConnectionItem { Id = conn.Id, Name = "Conn1-DiffName", Host = "10.0.0.99", Port = 3389 }]
        };
        var resSkipId = await service.ImportFromJsonAsync(JsonSerializer.Serialize(skipByIdPkg), new ImportOptions { ConflictResolution = ImportConflictResolution.MergeAndKeepExisting });
        Assert.Equal(1, resSkipId.ConnectionsSkipped);

        var skipByHostPkg = new ExportPackage
        {
            Version = "1.0",
            Connections = [new ConnectionItem { Id = Guid.NewGuid(), Name = "Conn1", Host = "10.0.0.1", Port = 3389 }]
        };
        var resSkipHost = await service.ImportFromJsonAsync(JsonSerializer.Serialize(skipByHostPkg), new ImportOptions { ConflictResolution = ImportConflictResolution.MergeAndKeepExisting });
        Assert.Equal(1, resSkipHost.ConnectionsSkipped);

        // 6. DPAPI password warning on unencrypted package with invalid DPAPI ciphertext
        var warningPkg = new ExportPackage
        {
            Version = "1.0",
            HasEncryptedCredentials = false,
            Credentials = [new ExportCredentialItem { Id = Guid.NewGuid(), Title = "ForeignCred", Username = "foreign", EncryptedPassword = "foreign-machine-dpapi-data" }]
        };
        var resWarning = await service.ImportFromJsonAsync(JsonSerializer.Serialize(warningPkg), new ImportOptions());
        Assert.True(resWarning.Success);
        Assert.NotEmpty(resWarning.Warnings);

        // 7. ImportFromJsonFileAsync missing vs existing
        var missingJson = Path.Combine(Path.GetTempPath(), $"Missing_{Guid.NewGuid():N}.json");
        var resJsonMissing = await service.ImportFromJsonFileAsync(missingJson, new ImportOptions());
        Assert.False(resJsonMissing.Success);

        var tempJson = Path.Combine(Path.GetTempPath(), $"Valid_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(tempJson, JsonSerializer.Serialize(new ExportPackage { Version = "1.0" }));
            var resJsonValid = await service.ImportFromJsonFileAsync(tempJson, new ImportOptions());
            Assert.True(resJsonValid.Success);
        }
        finally
        {
            if (File.Exists(tempJson)) File.Delete(tempJson);
        }

        // 8. ImportFromCsvAsync edge cases:
        // - Protocol = "UNKNOWN" -> defaults to RDP
        // - Missing Port for SSH, VNC, Web, RDP -> defaults to 22, 5900, 443, 3389
        // - DisplayMode = "INVALID" -> defaults to Tabbed
        // - Escaped double quotes: "Server with ""escaped"" name"
        var edgeCsv = """
        Name,Host,Protocol,Port,DisplayMode
        "Server with ""escaped"" quotes",10.1.1.1,UNKNOWN,,INVALID
        SSHServer,10.1.1.2,SSH,,Tabbed
        VNCServer,10.1.1.3,VNC,,Tabbed
        WebServer,10.1.1.4,Web,,Tabbed
        """;
        var resCsvEdge = await service.ImportFromCsvAsync(edgeCsv, ImportConflictResolution.MergeAndKeepExisting);
        Assert.True(resCsvEdge.Success);
        Assert.Equal(4, resCsvEdge.ConnectionsImported);

        // CSV existing connection with MergeAndKeepExisting -> skips
        var resCsvSkip = await service.ImportFromCsvAsync(edgeCsv, ImportConflictResolution.MergeAndKeepExisting);
        Assert.True(resCsvSkip.Success);
        Assert.Equal(4, resCsvSkip.ConnectionsSkipped);

        // 9. ImportFromCsvFileAsync missing vs existing
        var missingCsv = Path.Combine(Path.GetTempPath(), $"Missing_{Guid.NewGuid():N}.csv");
        var resCsvMissing = await service.ImportFromCsvFileAsync(missingCsv);
        Assert.False(resCsvMissing.Success);

        var tempCsv = Path.Combine(Path.GetTempPath(), $"Valid_{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(tempCsv, "Name,Host,Port\nBox,10.2.2.2,3389");
            var resCsvValid = await service.ImportFromCsvFileAsync(tempCsv);
            Assert.True(resCsvValid.Success);
        }
        finally
        {
            if (File.Exists(tempCsv)) File.Delete(tempCsv);
        }

        // 10. ExportToRdpAsync with credential == null and connection.CredentialId.HasValue
        var rdpConn = new ConnectionItem { Id = Guid.NewGuid(), Name = "RdpServer", Host = "10.3.3.3", Port = 3389, CredentialId = cred.Id };
        var rdpText = await service.ExportToRdpAsync(rdpConn, null);
        Assert.Contains("10.3.3.3", rdpText);
        Assert.Contains("user1", rdpText);

        // 11. ImportFromRdpAsync with no port in full address
        var rdpNoPort = "full address:s:myserver.company.local\nusername:s:user1";
        var importedNoPort = await service.ImportFromRdpAsync(rdpNoPort, "NoPortServer");
        Assert.Equal("myserver.company.local", importedNoPort.Host);
        Assert.Equal(3389, importedNoPort.Port);

        SqliteConnection.ClearAllPools();
        if (File.Exists(tempDb)) try { File.Delete(tempDb); } catch { }
    }
    #endregion
}
