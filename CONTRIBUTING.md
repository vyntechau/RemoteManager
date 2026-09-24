# Contributing to RemoteManager

Thank you for your interest in contributing to **RemoteManager**! RemoteManager is an open-source project by [VynTech](https://github.com/vyntechau).

---

## 🛠️ Development Setup

### Prerequisites
- **Operating System**: Windows 10 (Build 19041+) or Windows 11
- **.NET SDK**: [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or higher
- **IDE**: Visual Studio 2022 (with *.NET desktop development* workload) or Visual Studio Code / JetBrains Rider

### Getting the Code
```powershell
git clone https://github.com/vyntechau/RemoteManager.git
cd RemoteManager
```

### Building the Project
```powershell
# Restore dependencies and build
dotnet build

# Run the app locally
dotnet run --project src/RemoteManager.App
```

### Running Tests
All automated unit and integration tests must pass before submitting a pull request:
```powershell
dotnet test
```

### Packaging (Portable ZIP & MSI)
```powershell
# Build both Portable ZIP and Windows Installer MSI
.\build.ps1 -Target All -Version 1.2.0
```

---

## 🌿 Branching & Git Workflow

1. Fork the repository on GitHub.
2. Create a feature or fix branch from `main`:
   ```bash
   git checkout -b feature/my-cool-feature
   # or
   git checkout -b fix/issue-description
   ```
3. Commit your changes using descriptive commit messages:
   ```bash
   git commit -m "feat(protocols): add auto-reconnect option for RDP"
   ```
4. Push your branch and open a Pull Request against `main`.

---

## 📋 Coding Conventions & Guidelines

- **Target Framework**: .NET 8.0 Windows (`net8.0-windows`).
- **UI Architecture**: MVVM using `CommunityToolkit.Mvvm` and `WPF-UI` controls.
- **Security First**:
  - Never log passwords or decrypted credentials.
  - Always validate external process arguments to prevent command/argument injection.
  - Always use parameterized queries for SQLite database interactions.
- **Asynchronous Code**: Prefer `async/await` for database, network, and file I/O operations to keep the UI thread responsive.

---

## 🤝 Code of Conduct

Please note that this project is governed by our [Code of Conduct](CODE_OF_CONDUCT.md). By participating, you are expected to uphold these standards.
