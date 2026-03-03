# Security Policy

## Supported Versions

| Version | Supported |
|---------|-----------|
| 1.0.x   | Yes       |

## Reporting a Vulnerability

If you discover a security vulnerability in SPAN Finder, please report it responsibly:

1. **Do NOT** open a public GitHub issue for security vulnerabilities
2. Report via [GitHub Security Advisories](https://github.com/LumiBearStudio/SpanFinder/security/advisories/new)
3. Include a detailed description of the vulnerability and steps to reproduce

We will acknowledge your report within **48 hours** and work to release a fix as soon as possible.

## Scope

The following are in scope:

- Local privilege escalation
- Arbitrary code execution through file operations
- Credential leakage (FTP/SFTP stored credentials)
- Path traversal vulnerabilities

The following are **out of scope**:

- Issues requiring physical access to the machine
- Denial of service on the local application
- Issues in third-party dependencies (report to the upstream project)
