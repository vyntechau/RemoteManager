# Security Policy

The Vyntech team takes security vulnerabilities seriously. We appreciate the responsible disclosure of security issues and are committed to addressing them quickly.

## Supported Versions

| Version | Supported          |
| ------- | ------------------ |
| 1.0.x   | :white_check_mark: |
| < 1.0   | :x:                |

## Reporting a Vulnerability

**Please do NOT report security vulnerabilities through public GitHub issues.**

If you believe you have found a security vulnerability in RemoteManager:

1. Send an email to **[security@vyntech.com.au](mailto:security@vyntech.com.au)**.
2. Include details about the vulnerability, including:
   - A description of the issue and its potential impact.
   - Exact steps or minimal proof-of-concept to reproduce the behavior.
   - Operating system version (e.g., Windows 10 22H2, Windows 11 23H2).
   - RemoteManager version / commit SHA.
3. We will acknowledge receipt of your report within **48 hours** and provide regular progress updates as we investigate and develop a patch.
4. Once a fix has been released, we will publicly credit you in the release notes (unless you request to remain anonymous).

## Security Mindset

RemoteManager runs with a **local-first, zero-trust architecture**:
- Sensitive credentials and passwords are encrypted using Windows DPAPI (`DataProtectionScope.CurrentUser`) tied to your Windows profile.
- All connections and settings are stored locally in an embedded SQLite database (`%LocalAppData%\RemoteManager\remotemanager.db`).
- Web sessions are strictly isolated into distinct user data profiles (`%LocalAppData%\RemoteManager\WebProfiles\{ConnectionId}`).
- No telemetry, analytics, or remote network requests are made by RemoteManager without explicit user action.
