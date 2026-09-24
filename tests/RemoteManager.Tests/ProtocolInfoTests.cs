using System.Diagnostics;
using RemoteManager.Core.Models;
using RemoteManager.Protocols.Rdp;
using RemoteManager.Protocols.Ssh;
using RemoteManager.Protocols.Vnc;
using Xunit;

namespace RemoteManager.Tests;

public class ProtocolInfoTests
{
    [Fact]
    public void VncClientInfo_FormattedName_HandlesSpecialIdsAndInstalledStatus()
    {
        var builtIn = new VncClientInfo { Id = "BuiltIn", DisplayName = "Built-in Viewer" };
        var auto = new VncClientInfo { Id = "Auto", DisplayName = "Auto-Detect" };
        var custom = new VncClientInfo { Id = "Custom", DisplayName = "Custom Viewer" };

        var tightVncInstalled = new VncClientInfo
        {
            Id = "TightVNC",
            DisplayName = "TightVNC Viewer",
            IsInstalled = true
        };

        var ultraVncNotInstalled = new VncClientInfo
        {
            Id = "UltraVNC",
            DisplayName = "UltraVNC",
            IsInstalled = false
        };

        Assert.Equal("Built-in VNC Viewer (In-App Tab)", builtIn.FormattedName);
        Assert.Equal("Auto-Detect External Viewer", auto.FormattedName);
        Assert.Equal("Custom External Executable...", custom.FormattedName);
        Assert.Equal("TightVNC Viewer (Installed)", tightVncInstalled.FormattedName);
        Assert.Equal("UltraVNC (Not Detected)", ultraVncNotInstalled.FormattedName);

        // ToString() should return FormattedName
        Assert.Equal(builtIn.FormattedName, builtIn.ToString());
        Assert.Equal(tightVncInstalled.FormattedName, tightVncInstalled.ToString());
    }

    [Fact]
    public void SshClientInfo_FormattedName_DistinguishesInstalledState()
    {
        var installed = new SshClientInfo
        {
            Id = "WindowsTerminal",
            DisplayName = "Windows Terminal",
            IsInstalled = true
        };

        var notInstalled = new SshClientInfo
        {
            Id = "Bitvise",
            DisplayName = "Bitvise SSH Client",
            IsInstalled = false
        };

        Assert.Equal("Windows Terminal (Installed)", installed.FormattedName);
        Assert.Equal("Bitvise SSH Client (Not Detected)", notInstalled.FormattedName);
    }

    [Fact]
    public void VncSessionHandler_DetectAvailableClients_ReturnsAllStandardOptions()
    {
        var clients = VncSessionHandler.DetectAvailableClients();
        Assert.Contains(clients, c => c.Id == "BuiltIn");
        Assert.Contains(clients, c => c.Id == "Auto");
        Assert.Contains(clients, c => c.Id == "Custom");
        Assert.Contains(clients, c => c.Id == "UltraVNC");
        Assert.Contains(clients, c => c.Id == "TightVNC");
        Assert.Contains(clients, c => c.Id == "TigerVNC");
        Assert.Contains(clients, c => c.Id == "RealVNC");
    }

    [Fact]
    public void VncSessionHandler_Launch_ValidationErrors()
    {
        // 1. Empty host
        Assert.Throws<ArgumentException>(() => VncSessionHandler.Launch("", 5900));
        Assert.Throws<ArgumentException>(() => VncSessionHandler.Launch("   ", 5900));

        // 2. Disallowed chars in host
        Assert.Throws<ArgumentException>(() => VncSessionHandler.Launch("10.0.0.1;calc.exe", 5900));
        Assert.Throws<ArgumentException>(() => VncSessionHandler.Launch("host&test", 5900));

        // 3. Custom client with missing executable
        var settingsMissing = new RemoteManager.Core.Models.AppSettings
        {
            VncClientType = "Custom",
            CustomVncClientPath = @"C:\NonExistent\VncViewer.exe"
        };
        Assert.Throws<FileNotFoundException>(() => VncSessionHandler.Launch("10.0.0.1", 5900, "pass", settingsMissing));

        // 4. Custom client with null/empty path
        var settingsNull = new RemoteManager.Core.Models.AppSettings
        {
            VncClientType = "Custom",
            CustomVncClientPath = null
        };
        Assert.Throws<FileNotFoundException>(() => VncSessionHandler.Launch("10.0.0.1", 5900, "pass", settingsNull));
    }

    [Fact]
    public void SshSessionHandler_ValidationAndFormatting()
    {
        // 1. Invalid port
        Assert.Throws<ArgumentOutOfRangeException>(() => SshSessionHandler.Launch("10.0.0.1", 70000, "admin"));

        // 2. Invalid host
        Assert.Throws<ArgumentException>(() => SshSessionHandler.Launch("", 22, "admin"));
        Assert.Throws<ArgumentException>(() => SshSessionHandler.Launch("host;rm", 22, "admin"));

        // 3. Invalid username
        Assert.Throws<ArgumentException>(() => SshSessionHandler.Launch("10.0.0.1", 22, "user with space"));
        Assert.Throws<ArgumentException>(() => SshSessionHandler.Launch("10.0.0.1", 22, "user&danger"));

        // 4. FormatArguments template replacement
        var formatted = SshSessionHandler.FormatArguments("{user}@{host}:{port}", "bastion", 2222, "ubuntu");
        Assert.Equal("ubuntu@bastion:2222", formatted);
    }

    [Fact]
    public void RdpIsolatedLauncher_GenerateTempRdpFile_CreatesValidConfig()
    {
        var tempFile = RdpIsolatedLauncher.GenerateTempRdpFile("10.0.0.50:3390", "corpuser", "DOMAIN", fullScreen: true);
        try
        {
            Assert.True(File.Exists(tempFile));
            var content = File.ReadAllText(tempFile);
            Assert.Contains("full address:s:10.0.0.50:3390", content);
            Assert.Contains("username:s:corpuser", content);
            Assert.Contains("domain:s:DOMAIN", content);
            Assert.Contains("screen mode id:i:2", content); // fullscreen
            Assert.Contains("smart sizing:i:1", content);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }

        // Windowed mode
        var tempFileWindowed = RdpIsolatedLauncher.GenerateTempRdpFile("10.0.0.51", null, null, fullScreen: false);
        try
        {
            Assert.True(File.Exists(tempFileWindowed));
            var content = File.ReadAllText(tempFileWindowed);
            Assert.Contains("full address:s:10.0.0.51", content);
            Assert.Contains("screen mode id:i:1", content); // windowed
            Assert.DoesNotContain("username:s:", content);
            Assert.DoesNotContain("domain:s:", content);
        }
        finally
        {
            if (File.Exists(tempFileWindowed)) File.Delete(tempFileWindowed);
        }
    }

    [Fact]
    public void RdpIsolatedLauncher_InjectAndPurgeCredential_ExecutesWithoutThrowing()
    {
        // Safe target string
        RdpIsolatedLauncher.InjectCredential("test-dummy-target-1234", "testuser", "testpassword");
        RdpIsolatedLauncher.PurgeCredential("test-dummy-target-1234");
    }

    [Fact]
    public void SshClientInfo_ToString_ReturnsFormattedName()
    {
        var info = new SshClientInfo { Id = "Auto", DisplayName = "Auto-Detect" };
        Assert.Equal(info.FormattedName, info.ToString());
    }

    [Fact]
    public void SshClientDetector_FindOnPath_FindsCommonSystemExecutable()
    {
        var path = SshClientDetector.FindOnPath("cmd.exe");
        Assert.NotNull(path);
        Assert.True(File.Exists(path));

        var missing = SshClientDetector.FindOnPath("NonExistent_Binary_XYZ_999.exe");
        Assert.Null(missing);
    }

    [Fact]
    public void RdpIsolatedLauncher_Launch_WithProcessLauncher_ExecutesCleanly()
    {
        var prev = RdpIsolatedLauncher.ProcessLauncher;
        try
        {
            ProcessStartInfo? captured = null;
            RdpIsolatedLauncher.ProcessLauncher = psi =>
            {
                captured = psi;
                return new Process();
            };

            var proc = RdpIsolatedLauncher.Launch("10.0.0.1", 3389, "alice", "CORP", "SecretPass1!", fullScreen: true);
            Assert.NotNull(proc);
            Assert.NotNull(captured);
            Assert.Equal("mstsc.exe", captured.FileName);
            Assert.Contains("/f", captured.Arguments);
        }
        finally
        {
            RdpIsolatedLauncher.ProcessLauncher = prev;
        }
    }

    [Fact]
    public void SshSessionHandler_Launch_WithProcessLauncher_ExecutesCleanly()
    {
        var prev = SshSessionHandler.ProcessLauncher;
        try
        {
            ProcessStartInfo? captured = null;
            SshSessionHandler.ProcessLauncher = psi =>
            {
                captured = psi;
                return new Process();
            };

            // 1. Standard auto client
            var proc = SshSessionHandler.Launch("10.0.0.1", 22, "alice");
            Assert.NotNull(proc);
            Assert.NotNull(captured);

            // 2. Custom client
            var tempExe = Path.Combine(Path.GetTempPath(), $"CustomSSH_{Guid.NewGuid():N}.exe");
            File.WriteAllText(tempExe, "dummy");
            try
            {
                var settings = new RemoteManager.Core.Models.AppSettings
                {
                    SshClientType = "Custom",
                    CustomSshClientPath = tempExe,
                    CustomSshClientArgs = "{user}@{host}:{port}"
                };

                var customProc = SshSessionHandler.Launch("10.0.0.1", 2222, "bob", settings);
                Assert.NotNull(customProc);
                Assert.Equal(tempExe, captured.FileName);
                Assert.Equal("bob@10.0.0.1:2222", captured.Arguments);
            }
            finally
            {
                if (File.Exists(tempExe)) File.Delete(tempExe);
            }
        }
        finally
        {
            SshSessionHandler.ProcessLauncher = prev;
        }
    }

    [Fact]
    public void VncSessionHandler_Launch_WithProcessLauncher_ExecutesCustomAndKnown()
    {
        var prev = VncSessionHandler.ProcessLauncher;
        try
        {
            ProcessStartInfo? captured = null;
            VncSessionHandler.ProcessLauncher = psi =>
            {
                captured = psi;
                return new Process();
            };

            // Custom client
            var tempExe = Path.Combine(Path.GetTempPath(), $"CustomVNC_{Guid.NewGuid():N}.exe");
            File.WriteAllText(tempExe, "dummy");
            try
            {
                var settings = new RemoteManager.Core.Models.AppSettings
                {
                    VncClientType = "Custom",
                    CustomVncClientPath = tempExe,
                    CustomVncClientArgs = "{host}:{port} -pass {password}"
                };

                var proc = VncSessionHandler.Launch("10.0.0.1", 5901, "SecretVnc!", settings);
                Assert.NotNull(proc);
                Assert.Equal(tempExe, captured!.FileName);
                Assert.Contains("10.0.0.1:5901", captured.Arguments);
                Assert.Contains("SecretVnc!", captured.Arguments);
            }
            finally
            {
                if (File.Exists(tempExe)) File.Delete(tempExe);
            }
        }
        finally
        {
            VncSessionHandler.ProcessLauncher = prev;
        }
    }

    [Fact]
    public void SshClientDetector_FindOnPath_InvalidChars_CatchesGracefully()
    {
        var found = SshClientDetector.FindOnPath("ssh.exe", "C:\\Windows;Z:\\\0invalid;C:\\Program Files");
        // Should not throw, should handle invalid path character cleanly
    }

    [Fact]
    public void SshSessionHandler_Launch_DetectedClient_And_ExceptionPaths()
    {
        var prev = SshSessionHandler.ProcessLauncher;
        var tempExe = Path.Combine(Path.GetTempPath(), $"MockSsh_{Guid.NewGuid():N}.exe");
        File.WriteAllText(tempExe, "dummy");
        try
        {
            // 1. Detected client with valid executable
            SshSessionHandler.ProcessLauncher = psi => new Process();
            var settings = new AppSettings
            {
                SshClientType = "PuTTY",
                CustomSshClientPath = tempExe,
                CustomSshClientArgs = "{user}@{host}:{port}"
            };

            var proc = SshSessionHandler.Launch("10.0.0.1", 2222, "sshuser", settings);
            Assert.NotNull(proc);

            // 2. ProcessLauncher throws
            SshSessionHandler.ProcessLauncher = psi => throw new InvalidOperationException("Launch failure");
            Assert.Throws<InvalidOperationException>(() => SshSessionHandler.Launch("10.0.0.1", 22, "user", settings));
        }
        finally
        {
            SshSessionHandler.ProcessLauncher = prev;
            if (File.Exists(tempExe)) File.Delete(tempExe);
        }
    }

    [Fact]
    public void VncSessionHandler_PortRange_KnownViewer_And_AutoDetect_Branches()
    {
        var prev = VncSessionHandler.ProcessLauncher;
        var prevKnown = VncSessionHandler.KnownViewers;
        var tempExe = Path.Combine(Path.GetTempPath(), $"MockVncViewer_{Guid.NewGuid():N}.exe");
        File.WriteAllText(tempExe, "dummy");

        try
        {
            ProcessStartInfo? captured = null;
            VncSessionHandler.ProcessLauncher = psi => { captured = psi; return new Process(); };

            // 1. Port > 65535 resets to 5900
            VncSessionHandler.KnownViewers = [("UltraVNC", "UltraVNC", [tempExe], "{host}:{port}")];
            VncSessionHandler.Launch("10.0.0.1", 99999, null, new AppSettings { VncClientType = "UltraVNC" });
            Assert.Contains("10.0.0.1:5900", captured!.Arguments);

            // 2. Specific known viewer found
            VncSessionHandler.Launch("10.0.0.1", 5901, null, new AppSettings { VncClientType = "UltraVNC" });
            Assert.Equal(tempExe, captured.FileName);

            // 3. Auto-detect any installed viewer
            captured = null;
            VncSessionHandler.Launch("10.0.0.1", 5902, null, new AppSettings { VncClientType = "Auto" });
            Assert.NotNull(captured);
            Assert.Equal(tempExe, captured.FileName);

            // 4. Known viewer configured, but file does NOT exist -> falls through to auto-detect
            VncSessionHandler.KnownViewers = [
                ("TightVNC", "TightVNC", [@"Z:\NonExistent\tvn.exe"], "{host}:{port}"),
                ("UltraVNC", "UltraVNC", [tempExe], "{host}:{port}")
            ];
            captured = null;
            VncSessionHandler.Launch("10.0.0.1", 5903, null, new AppSettings { VncClientType = "TightVNC" });
            Assert.NotNull(captured);
            Assert.Equal(tempExe, captured.FileName);

            // 5. No viewer found throws InvalidOperationException
            VncSessionHandler.KnownViewers = [("UltraVNC", "UltraVNC", [@"Z:\NonExistentDir\vnc.exe"], "{host}:{port}")];
            Assert.Throws<InvalidOperationException>(() =>
                VncSessionHandler.Launch("10.0.0.1", 5900, null, new AppSettings { VncClientType = "Auto" }));
        }
        finally
        {
            VncSessionHandler.ProcessLauncher = prev;
            VncSessionHandler.KnownViewers = prevKnown;
            if (File.Exists(tempExe)) File.Delete(tempExe);
        }
    }

    [Fact]
    public void SshSessionHandler_TerminalFallback_And_ExitHandler()
    {
        var prev = SshSessionHandler.ProcessLauncher;
        var prevOverride = SshSessionHandler.WindowsTerminalPathOverride;
        try
        {
            ProcessStartInfo? captured = null;
            SshSessionHandler.ProcessLauncher = psi => { captured = psi; return new Process(); };

            // When Windows Terminal is not found, falls back to cmd.exe
            SshSessionHandler.WindowsTerminalPathOverride = @"Z:\NonExistent\wt.exe";
            SshSessionHandler.Launch("10.0.0.1", 22, "admin", new AppSettings());
            Assert.NotNull(captured);
            Assert.Equal("cmd.exe", captured.FileName);

            // When specific client not found on disk, falls back to auto-detect
            captured = null;
            SshSessionHandler.Launch("10.0.0.1", 22, "admin", new AppSettings { SshClientType = "PuTTY", CustomSshClientPath = @"Z:\NonExistent\putty.exe" });
            Assert.NotNull(captured);
            Assert.Equal("cmd.exe", captured.FileName);

            // Test HandleProcessExit directly
            SshSessionHandler.HandleProcessExit("cmd.exe", 0);
        }
        finally
        {
            SshSessionHandler.ProcessLauncher = prev;
            SshSessionHandler.WindowsTerminalPathOverride = prevOverride;
        }
    }

    [Fact]
    public void RdpIsolatedLauncher_Inject_Purge_And_ExitedLifecycle()
    {
        var prev = RdpIsolatedLauncher.ProcessLauncher;
        try
        {
            ProcessStartInfo? captured = null;
            RdpIsolatedLauncher.ProcessLauncher = psi => { captured = psi; return new Process(); };

            // Direct inject and purge
            RdpIsolatedLauncher.InjectCredential("10.0.0.1", "admin", "secret");
            Assert.NotNull(captured);
            Assert.Equal("cmdkey.exe", captured.FileName);

            captured = null;
            RdpIsolatedLauncher.PurgeCredential("10.0.0.1");
            Assert.NotNull(captured);
            Assert.Contains("/delete:TERMSRV/10.0.0.1", captured.Arguments);

            // Test CleanupProcessExit directly
            var tempRdp = Path.Combine(Path.GetTempPath(), $"Test_{Guid.NewGuid():N}.rdp");
            File.WriteAllText(tempRdp, "screen mode id:i:2");
            RdpIsolatedLauncher.CleanupProcessExit("10.0.0.1", tempRdp, injected: true, "10.0.0.1");
            Assert.False(File.Exists(tempRdp));

            // Test CleanupProcessExit when file delete throws (locked file)
            var lockedRdp = Path.Combine(Path.GetTempPath(), $"Locked_{Guid.NewGuid():N}.rdp");
            using (var fs = new FileStream(lockedRdp, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                RdpIsolatedLauncher.CleanupProcessExit("10.0.0.1", lockedRdp, injected: false, "10.0.0.1");
            }
            if (File.Exists(lockedRdp)) File.Delete(lockedRdp);

            // Exception handling in cmdkey
            RdpIsolatedLauncher.ProcessLauncher = psi => throw new InvalidOperationException("cmdkey blocked");
            RdpIsolatedLauncher.InjectCredential("10.0.0.1", "admin", "secret");
            RdpIsolatedLauncher.PurgeCredential("10.0.0.1");
        }
        finally
        {
            RdpIsolatedLauncher.ProcessLauncher = prev;
        }
    }

    [Fact]
    public void RdpIsolatedLauncher_GenerateTempRdpFile_EnablesClipboardAndRemoteKeyboardHook()
    {
        var tempFile = RdpIsolatedLauncher.GenerateTempRdpFile("192.168.1.100:3389", "Administrator", "WORKGROUP", fullScreen: false);
        try
        {
            Assert.True(File.Exists(tempFile));
            var content = File.ReadAllText(tempFile);

            // Clipboard redirection virtual channel must be enabled
            Assert.Contains("redirectclipboard:i:1", content);
            // Keyboard hook must be 1 (remote computer) so shortcuts like Ctrl+C, Ctrl+V, Alt+Tab are forwarded
            Assert.Contains("keyboardhook:i:1", content);
            // Drive redirection must be enabled to support copying and pasting files
            Assert.Contains("redirectdrives:i:1", content);
            Assert.Contains("drivestoredirect:s:*", content);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

#pragma warning disable CS0067
    private class DefaultRdpControl : IRdpHostControl
    {
        public event Action? DisconnectRequested;
        public event Action<string, int, int>? Disconnected;
        public event Action? Connected;
        public void Connect(string server, int port, string? username, string? domain, string? password, int width = 1920, int height = 1080) { }
        public void Disconnect() { }
    }
#pragma warning restore CS0067

    [Fact]
    public void IRdpHostControl_DefaultInterfaceMethods_CanBeInvokedWithoutExceptions()
    {
        IRdpHostControl control = new DefaultRdpControl();
        // Invoke default interface methods
        control.FocusRdp();
        control.SendCopy();
        control.SendPaste();
        control.SendCtrlAltDel();
    }
}


