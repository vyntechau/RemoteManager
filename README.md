# RemoteManager

<p align="center">
  <strong>Part of the VynTech Ecosystem</strong><br>
  A modern, open-source, native Windows connection & credential manager for RDP, SSH, VNC, and Web sessions with zero credential collisions.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white" alt=".NET 8" />
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011-0078D6?logo=windows&logoColor=white" alt="Windows" />
  <img src="https://img.shields.io/badge/UI-Fluent%20WPF--UI-0078D4" alt="WPF-UI" />
  <img src="https://img.shields.io/badge/Installer-WiX%20Toolset%20v5-FF8800" alt="WiX Toolset" />
  <img src="https://img.shields.io/badge/Security-Windows%20DPAPI-success" alt="DPAPI" />
  <img src="https://img.shields.io/badge/License-MIT-blue.svg" alt="License" />
</p>

---

## 💡 The Problem: Windows RDP Credential Overwrite

If you manage servers or workstations using standard Windows Remote Desktop (`mstsc.exe`), you've likely encountered this frustrating limitation:

Windows Credential Manager indexes saved credentials strictly by the target IP or hostname:
```text
TERMSRV/192.168.1.50
```

Because Credential Manager only stores **a single credential per target key**, checking "Remember me" for a second account (e.g. `Domain\Auditor`) **permanently overwrites** the saved password of your previous account (e.g. `Domain\Administrator`) on that IP.

---

## ⚡ The Solution: RemoteManager

**RemoteManager** is purpose-built to solve this exact problem:

* **Zero Credential Collisions**: Store dozens of unique accounts (`Admin`, `User1`, `ServiceAccount`) pointing to the exact same host or IP without them ever conflicting.
* **Embedded Tabbed Sessions**: Run concurrent RDP, VNC, and Web sessions side-by-side in tabs, with integrated SSH console launch.
* **Flexible Display Modes**:
  * **In-App Tabs**: Seamless multi-tasking across embedded protocols.
  * **Borderless Fullscreen (`F11`)**: Auto-hiding floating top bar (just like native MSTSC / VMware) to minimize, restore, or disconnect.
  * **Detached Windows**: Pop out any tab into an independent window with one click, and dock it back whenever you want.
  * **External Launcher**: Direct launch into native `mstsc.exe` with dynamically injected isolated credentials and automatic teardown.
* **System Theme by Default**: Natively adapts to your Windows light or dark mode in real time.

---

## 🌐 Supported Protocols

| Protocol | Engine | Capabilities |
| :--- | :--- | :--- |
| **RDP** | Native ActiveX (`AxMsRdpClient`) + Isolated Launcher | Full credentials injection, SmartSizing, audio redirection, clipboard sharing, multi-monitor |
| **SSH** | Windows Terminal (`wt.exe`) / OpenSSH | Automatic terminal console launch with sanitized user and port parameters |
| **VNC** | Built-in RFB Viewer + External Viewer Bridge | Embedded tabbed viewer or external bridge (UltraVNC, TightVNC, TigerVNC, RealVNC) |
| **Web** | Microsoft WebView2 (Chromium) | **Isolated profile per connection** — cookies, sessions, and storage never leak between accounts |

---

## 🔒 Security & Privacy Architecture

RemoteManager is built with a **strict local-first, zero-trust mindset**:

1. **Windows DPAPI Encryption**:
   - All passwords and sensitive tokens are encrypted using Windows Data Protection API (`System.Security.Cryptography.ProtectedData`).
   - The cryptographic key is tied directly to your logged-in Windows user account (`DataProtectionScope.CurrentUser`) combined with internal cryptographic entropy.
   - Passwords are **never written to disk in plain text**.
   - *Note: Because DPAPI derives its master key from your Windows profile, credentials are machine- and user-specific.*
2. **100% Local Storage**:
   - All connections and encrypted credentials reside in an embedded SQLite database:
     ```text
     %LocalAppData%\RemoteManager\remotemanager.db
     ```
3. **Isolated Web Profiles**:
   - Each Web session tab runs in an independent Chromium `UserDataFolder` (`%LocalAppData%\RemoteManager\WebProfiles\{ConnectionId}`).
   - Logging into multiple accounts on cloud consoles (AWS, Azure, Proxmox, vCenter, routers) will never cross-pollinate cookies or sessions.
   - Profile cache directories are cleaned up automatically when connections are deleted.
4. **Zero Telemetry & Cloud-Free**:
   - No external API calls, no third-party accounts, and no data tracking.

### 🛡️ Vulnerability Reporting & Security Policy
For security matters and responsible disclosure, please refer to our [Security Policy](SECURITY.md) or reach out directly to:

📧 **[security@vyntech.com.au](mailto:security@vyntech.com.au)**

---

## 📚 Documentation Center

For comprehensive setup guides, architecture overviews, protocol configurations, and FAQs, visit our document center:

📖 **[VynTech Document Center](https://www.vyntech.com.au/documents)**

---

## 🏛️ Project Architecture

```text
RemoteManager/
├── .github/
│   └── workflows/
│       └── build-and-release.yml   # Automated CI/CD (Test, build EXE/MSI, create Releases)
│
├── packaging/
│   ├── Package.wxs                 # WiX Toolset v5 Windows Installer definition
│   └── license.rtf                 # License file bundled in MSI installer
│
├── src/
│   ├── RemoteManager.Core/         # Domain entities, enums, and service contracts
│   │   ├── Models/                 # ConnectionItem, Credential, AppSettings, ProtocolType
│   │   └── Interfaces/             # IDatabaseService, IEncryptionService, IProtocolSession
│   │
│   ├── RemoteManager.Data/         # SQLite storage and DPAPI cryptographic services
│   │   ├── SqliteDatabaseService.cs
│   │   └── Security/DpapiEncryptionService.cs
│   │
│   ├── RemoteManager.Protocols/    # Multi-protocol session execution engines
│   │   ├── Rdp/                    # RdpAxClient (ActiveX), RdpHostControl, RdpIsolatedLauncher
│   │   ├── Ssh/                    # SshSessionHandler, SshClientDetector
│   │   ├── Vnc/                    # VncSessionHandler, VncHostControl, VncSharpCore engine
│   │   └── Web/                    # WebView2SessionControl (isolated profiles)
│   │
│   └── RemoteManager.App/          # WPF-UI Fluent Windows 11 presentation layer
│       ├── Views/                  # MainWindow, ConnectionsView, SettingsView, LogsView
│       └── ViewModels/             # MainViewModel, ConnectionsViewModel, LogsViewModel
│
├── tests/
│   └── RemoteManager.Tests/        # Automated unit and integration tests (xUnit)
│
├── Makefile                        # Cross-platform build & packaging automation
├── build.ps1                       # Native PowerShell packaging script (EXE & MSI)
└── dotnet-tools.json               # WiX Toolset v5 tool manifest
```

---

## 🚀 Getting Started

### Prerequisites
- Windows 10 (Build 19041+) or Windows 11
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or higher

### Option 1: Run via Command Line
```powershell
# Clone the repository
git clone git@github.com:vyntechau/RemoteManager.git
cd RemoteManager

# Build and run
dotnet run --project src/RemoteManager.App
```

### Option 2: Run the Compiled Binary
After building, double-click:
```text
src\RemoteManager.App\bin\Debug\net8.0-windows\RemoteManager.App.exe
```

### Run Automated Tests
```powershell
dotnet test
```

---

## 📦 Building & Packaging (EXE & MSI)

RemoteManager provides full build automation to produce both **portable single-file executables** and an **MSI Windows Installer** using **WiX Toolset v5**.

### Release Artifacts
When built, release artifacts are generated into the `artifacts/` folder:
- **`RemoteManager-v<version>-win-x64-portable.zip`**: Self-contained single-file bundle that runs instantly on Windows 10/11 without requiring .NET installation or administrator rights.
- **`RemoteManager-v<version>-win-x64-Setup.msi`**: Standard Windows Installer package:
  - Installs to `C:\Program Files\Vyntech\Remote Manager\`
  - Creates Start Menu & Desktop shortcuts with the Vyntech application icon
  - Full integration with Windows **Installed apps / Add or remove programs** (ARP)
  - Supports silent enterprise rollout:
    ```cmd
    msiexec /i RemoteManager-v1.0.0-win-x64-Setup.msi /qn
    ```
- **`checksums.txt`**: SHA-256 integrity verification hashes for all built assets.

---

### Option 1: Using `make`
Standard cross-environment build commands:
```bash
# Display help and all available targets
make help

# Run all 31 unit tests
make test

# Publish self-contained portable EXE and create portable ZIP
make exe

# Build Windows Installer (.msi) using WiX v5
make msi

# Full pipeline: clean, test, build portable ZIP, build MSI, and generate SHA-256 checksums
make all

# Clean build artifacts, temporary obj/bin directories, and WiX cache
make clean
```

### Option 2: Using PowerShell
Native Windows automation script:
```powershell
# Build everything (Tests, Portable ZIP, MSI installer, and SHA-256 checksums)
.\build.ps1 -Target All -Version 1.0.0

# Build single-file portable EXE & ZIP only
.\build.ps1 -Target Exe

# Build Windows Installer MSI only (auto-generates version from git tag or parameter)
.\build.ps1 -Target Msi -Version 1.0.0

# Run automated unit test suite
.\build.ps1 -Target Test

# Clean build artifacts and staging files
.\build.ps1 -Target Clean
```

---

## 🤖 Continuous Integration & Automated Releases (GitHub Actions)

An automated production CI/CD workflow is included in [`.github/workflows/build-and-release.yml`](.github/workflows/build-and-release.yml):

- **Pull Requests & Pushes to `main`**:
  - Restores NuGet dependencies and WiX v5 toolset
  - Runs all unit tests (`dotnet test`)
  - Compiles and publishes self-contained win-x64 binaries
  - Creates portable ZIP, compiles the MSI installer, and generates SHA-256 checksums
  - Uploads artifacts directly to the workflow run (accessible via the **Actions** tab on GitHub)

- **Publishing a Release**:
  - Pushing a version tag automatically generates and publishes a GitHub Release with the MSI installer, portable ZIP, checksums, and auto-generated release notes:
    ```bash
    git tag v1.0.0
    git push origin v1.0.0
    ```
  - Manual trigger is also available via **Run workflow** on the GitHub Actions tab (`workflow_dispatch`), allowing you to override version numbers and publish releases on demand.

## 🤝 Contributing & Community

Contributions are warmly welcome! Please see our [Contributing Guide](CONTRIBUTING.md) for details on setting up your environment, coding conventions, and submitting pull requests. All participants are expected to adhere to our [Code of Conduct](CODE_OF_CONDUCT.md).

---

## 🌌 VynTech Ecosystem

**RemoteManager** is an open-source project by **[VynTech](https://github.com/vyntechau)**.

VynTech focuses on crafting high-performance, secure, and developer-first tools for cloud engineering, systems administration, and automated infrastructure environments.

---

## 📄 License & Attributions

- RemoteManager is licensed under the **[MIT License](LICENSE)** — free for personal and commercial use.
- Third-party open-source libraries and notices are documented in **[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)**.
