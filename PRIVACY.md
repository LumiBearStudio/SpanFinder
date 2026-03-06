# SPAN Finder — Privacy Policy

**Last Updated: March 6, 2026**

## Overview

SPAN Finder ("the App") is a file explorer application for Windows developed by LumiBear Studio. We are committed to protecting your privacy. This policy explains what data we collect and how we use it.

## Data We Collect

### Crash Reports (Sentry)

The App uses [Sentry](https://sentry.io) for automated crash reporting. When the App crashes or encounters an unhandled error, the following data may be sent:

- **Error details**: Exception type, message, and stack trace
- **Device info**: OS version, CPU architecture, memory usage
- **App info**: App version, runtime version

Crash reports are used solely to identify and fix bugs. They do **not** include:

- File names, folder names, or file contents
- User account information
- Browsing history or navigation paths
- Any personally identifiable information (PII)

### Local Settings

The App stores user preferences (theme, language, recent folders, favorites, etc.) locally on your device using Windows `ApplicationData.LocalSettings`. This data is never transmitted to any server.

## Data We Do NOT Collect

- No personal information (name, email, address)
- No file system contents or file metadata
- No usage analytics or telemetry
- No location data
- No advertising identifiers
- No data shared with third parties for marketing

## Network Access

The App requires internet access only for:

- **Crash reporting** (Sentry) — automatic error reports
- **FTP/SFTP connections** — only when explicitly configured by the user
- **NuGet package restore** — during development builds only

## Data Storage and Retention

- Crash reports are retained on Sentry servers for 90 days, then automatically deleted.
- All user settings are stored locally and remain under your control.

## Children's Privacy

The App does not knowingly collect any data from children under the age of 13.

## Your Rights

Since we do not collect personal data, there is no personal data to access, modify, or delete. You can disable crash reporting by disconnecting from the internet while using the App.

## Open Source

SPAN Finder uses open-source libraries. See [LICENSES.md](https://github.com/LumiBearStudio/SpanFinder/blob/main/LICENSES.md) for details.

## Contact

If you have questions about this privacy policy:

- **GitHub Issues**: [https://github.com/LumiBearStudio/SpanFinder/issues](https://github.com/LumiBearStudio/SpanFinder/issues)

## Changes to This Policy

We may update this policy from time to time. Changes will be posted to this repository with an updated revision date.
