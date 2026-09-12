using System.IO;

namespace RemoteManager.Protocols.Ssh;

public static class SshClientDetector
{
    public static List<SshClientInfo> DetectAvailableClients()
    {
        var list = new List<SshClientInfo>
        {
            new()
            {
                Id = "Auto",
                DisplayName = "Auto-Detect (Windows Terminal / OpenSSH)",
                IsInstalled = true,
                DefaultArgsTemplate = "{user}@{host} -p {port}"
            }
        };

        // 1. Windows Terminal
        var wtPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Microsoft\WindowsApps\wt.exe");
        var wtFound = File.Exists(wtPath) || FindOnPath("wt.exe") != null;
        list.Add(new SshClientInfo
        {
            Id = "WindowsTerminal",
            DisplayName = "Windows Terminal (wt.exe)",
            ExecutablePath = File.Exists(wtPath) ? wtPath : FindOnPath("wt.exe"),
            IsInstalled = wtFound,
            DefaultArgsTemplate = "--title \"SSH: {user}@{host}\" ssh -p {port} {user}@{host}"
        });

        // 2. Windows Built-in OpenSSH
        var openSshPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            @"System32\OpenSSH\ssh.exe");
        var openSshFound = File.Exists(openSshPath) || FindOnPath("ssh.exe") != null;
        list.Add(new SshClientInfo
        {
            Id = "OpenSSH",
            DisplayName = "Windows OpenSSH (ssh.exe)",
            ExecutablePath = File.Exists(openSshPath) ? openSshPath : FindOnPath("ssh.exe"),
            IsInstalled = openSshFound,
            DefaultArgsTemplate = "-p {port} {user}@{host}"
        });

        // 3. PuTTY
        var puttyPaths = new[]
        {
            @"C:\Program Files\PuTTY\putty.exe",
            @"C:\Program Files (x86)\PuTTY\putty.exe"
        };
        var puttyPath = puttyPaths.FirstOrDefault(File.Exists) ?? FindOnPath("putty.exe");
        list.Add(new SshClientInfo
        {
            Id = "PuTTY",
            DisplayName = "PuTTY",
            ExecutablePath = puttyPath,
            IsInstalled = !string.IsNullOrEmpty(puttyPath),
            DefaultArgsTemplate = "-ssh {host} -P {port} -l {user}"
        });

        // 4. KiTTY
        var kittyPaths = new[]
        {
            @"C:\Program Files\KiTTY\kitty.exe",
            @"C:\Program Files (x86)\KiTTY\kitty.exe"
        };
        var kittyPath = kittyPaths.FirstOrDefault(File.Exists) ?? FindOnPath("kitty.exe");
        list.Add(new SshClientInfo
        {
            Id = "KiTTY",
            DisplayName = "KiTTY",
            ExecutablePath = kittyPath,
            IsInstalled = !string.IsNullOrEmpty(kittyPath),
            DefaultArgsTemplate = "-ssh {host} -P {port} -l {user}"
        });

        // 5. Git Bash
        var gitBashPaths = new[]
        {
            @"C:\Program Files\Git\git-bash.exe",
            @"C:\Program Files (x86)\Git\git-bash.exe"
        };
        var gitBashPath = gitBashPaths.FirstOrDefault(File.Exists) ?? FindOnPath("git-bash.exe");
        list.Add(new SshClientInfo
        {
            Id = "GitBash",
            DisplayName = "Git Bash",
            ExecutablePath = gitBashPath,
            IsInstalled = !string.IsNullOrEmpty(gitBashPath),
            DefaultArgsTemplate = "-c \"ssh -p {port} {user}@{host}\""
        });

        // 6. MobaXterm
        var mobaPaths = new[]
        {
            @"C:\Program Files (x86)\Mobatek\MobaXterm\MobaXterm.exe",
            @"C:\Program Files\Mobatek\MobaXterm\MobaXterm.exe"
        };
        var mobaPath = mobaPaths.FirstOrDefault(File.Exists) ?? FindOnPath("MobaXterm.exe");
        list.Add(new SshClientInfo
        {
            Id = "MobaXterm",
            DisplayName = "MobaXterm",
            ExecutablePath = mobaPath,
            IsInstalled = !string.IsNullOrEmpty(mobaPath),
            DefaultArgsTemplate = "-newtab \"ssh -p {port} {user}@{host}\""
        });

        // 7. Bitvise SSH Client
        var bitvisePaths = new[]
        {
            @"C:\Program Files (x86)\Bitvise SSH Client\BvSsh.exe",
            @"C:\Program Files\Bitvise SSH Client\BvSsh.exe"
        };
        var bitvisePath = bitvisePaths.FirstOrDefault(File.Exists) ?? FindOnPath("BvSsh.exe");
        list.Add(new SshClientInfo
        {
            Id = "Bitvise",
            DisplayName = "Bitvise SSH Client",
            ExecutablePath = bitvisePath,
            IsInstalled = !string.IsNullOrEmpty(bitvisePath),
            DefaultArgsTemplate = "-host={host} -port={port} -user={user}"
        });

        // 8. Custom Executable Option
        list.Add(new SshClientInfo
        {
            Id = "Custom",
            DisplayName = "Custom Client Executable...",
            IsInstalled = true,
            DefaultArgsTemplate = "{user}@{host} -p {port}"
        });

        return list;
    }

    internal static string? FindOnPath(string exeName, string? pathEnv = null)
    {
        pathEnv ??= Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return null;

        var paths = pathEnv.Split(Path.PathSeparator);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            var fullPath = Path.Combine(path.Trim(), exeName);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }
        return null;
    }
}
