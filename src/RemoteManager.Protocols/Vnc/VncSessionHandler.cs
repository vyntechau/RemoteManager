using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using RemoteManager.Core.Models;
using RemoteManager.Core.Logging;

namespace RemoteManager.Protocols.Vnc;

public static class VncSessionHandler
{
    internal static Func<ProcessStartInfo, Process?> ProcessLauncher { get; set; } = psi => Process.Start(psi);

    internal static (string Id, string DisplayName, string[] Paths, string DefaultArgs)[] KnownViewers =
    [
        ("UltraVNC", "UltraVNC", [
            @"C:\Program Files\uvnc bvba\UltraVNC\vncviewer.exe",
            @"C:\Program Files (x86)\uvnc bvba\UltraVNC\vncviewer.exe"
        ], "{host}:{port}"),
        ("TightVNC", "TightVNC", [
            @"C:\Program Files\TightVNC\tvnviewer.exe",
            @"C:\Program Files (x86)\TightVNC\tvnviewer.exe"
        ], "{host}:{port}"),
        ("TigerVNC", "TigerVNC", [
            @"C:\Program Files\TigerVNC\vncviewer.exe",
            @"C:\Program Files (x86)\TigerVNC\vncviewer.exe"
        ], "{host}:{port}"),
        ("RealVNC", "RealVNC Viewer", [
            @"C:\Program Files\RealVNC\VNC Viewer\vncviewer.exe",
            @"C:\Program Files (x86)\RealVNC\VNC Viewer\vncviewer.exe"
        ], "{host}:{port}")
    ];

    public static List<VncClientInfo> DetectAvailableClients()
    {
        var list = new List<VncClientInfo>
        {
            new()
            {
                Id = "BuiltIn",
                DisplayName = "Built-in VNC Viewer",
                IsInstalled = true,
                DefaultArgsTemplate = string.Empty
            },
            new()
            {
                Id = "Auto",
                DisplayName = "Auto-Detect External",
                IsInstalled = true,
                DefaultArgsTemplate = "{host}:{port}"
            }
        };

        foreach (var (id, displayName, paths, defaultArgs) in KnownViewers)
        {
            var found = paths.FirstOrDefault(File.Exists);
            list.Add(new VncClientInfo
            {
                Id = id,
                DisplayName = displayName,
                ExecutablePath = found,
                IsInstalled = found != null,
                DefaultArgsTemplate = defaultArgs
            });
        }

        list.Add(new VncClientInfo
        {
            Id = "Custom",
            DisplayName = "Custom Executable",
            IsInstalled = true,
            DefaultArgsTemplate = "{host}:{port}"
        });

        return list;
    }

    private static readonly char[] DisallowedChars = ['&', '|', ';', '`', '$', '<', '>', '"', '\'', '\r', '\n'];

    public static Process Launch(string host, int port, string? password = null, AppSettings? settings = null)
    {
        var vncPort = port > 0 ? port : 5900;
        if (vncPort < 1 || vncPort > 65535)
        {
            vncPort = 5900;
        }

        if (string.IsNullOrWhiteSpace(host) || host.IndexOfAny(DisallowedChars) >= 0)
        {
            throw new ArgumentException("Host contains invalid characters or is empty.", nameof(host));
        }

        var safeHost = host.Trim();
        var address = $"{safeHost}:{vncPort}";
        var clientType = settings?.VncClientType ?? "Auto";
        LogEngine.Instance.Info("Protocol.VNC", $"Launching external VNC viewer for {address} (Type: {clientType})");

        try
        {
            // 1. Custom executable
            if (clientType == "Custom")
            {
                var customPath = settings?.CustomVncClientPath;
                if (string.IsNullOrWhiteSpace(customPath) || !File.Exists(customPath))
                {
                    throw new FileNotFoundException($"Custom VNC client executable was not found. Please verify the path in Settings.");
                }

                var argsTemplate = string.IsNullOrWhiteSpace(settings?.CustomVncClientArgs)
                    ? "{host}:{port}"
                    : settings.CustomVncClientArgs;

                var safePassword = (password ?? string.Empty).Replace("\"", "\\\"");
                var args = argsTemplate
                    .Replace("{host}", safeHost, StringComparison.OrdinalIgnoreCase)
                    .Replace("{port}", vncPort.ToString(), StringComparison.OrdinalIgnoreCase)
                    .Replace("{address}", address, StringComparison.OrdinalIgnoreCase)
                    .Replace("{password}", safePassword, StringComparison.OrdinalIgnoreCase);

                LogEngine.Instance.Debug("Protocol.VNC", $"Starting custom VNC client: {customPath} (Args masked)");
                return ProcessLauncher(new ProcessStartInfo
                {
                    FileName = customPath,
                    Arguments = args,
                    UseShellExecute = true
                }) ?? throw new InvalidOperationException($"Failed to start custom VNC client: {customPath}");
            }

            // 2. Specific known viewer
            var specific = KnownViewers.FirstOrDefault(k => string.Equals(k.Id, clientType, StringComparison.OrdinalIgnoreCase));
            if (specific.Id != null)
            {
                var foundPath = specific.Paths.FirstOrDefault(File.Exists);
                if (foundPath != null)
                {
                    LogEngine.Instance.Debug("Protocol.VNC", $"Starting {specific.DisplayName} at {foundPath} with target {address}");
                    return ProcessLauncher(new ProcessStartInfo
                    {
                        FileName = foundPath,
                        Arguments = address,
                        UseShellExecute = true
                    }) ?? throw new InvalidOperationException($"Failed to launch {specific.DisplayName}");
                }
            }

            // 3. Auto-detect any installed viewer
            foreach (var (_, displayName, paths, _) in KnownViewers)
            {
                var found = paths.FirstOrDefault(File.Exists);
                if (found != null)
                {
                    LogEngine.Instance.Debug("Protocol.VNC", $"Auto-detected {displayName} at {found}. Launching for {address}");
                    return ProcessLauncher(new ProcessStartInfo
                    {
                        FileName = found,
                        Arguments = address,
                        UseShellExecute = true
                    }) ?? throw new InvalidOperationException($"Failed to start {displayName}");
                }
            }

            throw new InvalidOperationException("No external VNC viewer was found on this system. Please configure an external viewer in Settings, or use the Built-in VNC Viewer.");
        }
        catch (Exception ex)
        {
            LogEngine.Instance.Error("Protocol.VNC", $"Error launching external VNC viewer for {address}: {ex.Message}", ex);
            throw;
        }
    }
}
