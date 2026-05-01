---
title: "Melodee 2.0.0 Released"
date: 2026-05-01
badges:
  - type: info
    tag: release
---

We're thrilled to announce the release of Melodee 2.0.0! This is a major milestone that brings powerful automation, a smoother first-time setup, security hardening, and a move to .NET 10.

<!--more-->

## What's New in 2.0.0

### Event Scripting
Automate your library workflows with JavaScript-powered event scripts using the built-in Monaco code editor.

- Write scripts that run on directory processing events
- Context-aware code completion in the admin script editor
- Deny actions with path validation and dry-run support
- Validation and orchestration through the CLI

### Onboarding Wizard
New installations now include a guided setup wizard to get Melodee running in minutes.

- Step-by-step administrator account setup
- Automated health checks via the integrated Doctor service
- Checklist-based configuration tracking
- Admin dashboard warnings for incomplete setup

### .NET 10 & DecentDB
Melodee has been upgraded to .NET 10 for improved performance, and the database layer has moved from SQLite to DecentDB for better scalability and maintainability.

- .NET 10 runtime support
- DecentDB.EntityFrameworkCore integration
- Cancellation support throughout MusicBrainz imports
- Enhanced query performance and streaming imports

### Playlist Import & Export
Manage playlists more flexibly with standard file format support.

- Import M3U and M3U8 playlists
- Export playlists to M3U format
- Seamless UI integration for playlist management

### Security Hardening
A comprehensive security audit and remediation effort across the entire codebase.

- Rate limiting with custom rejection handling for API requests
- Strict CORS policies and secure key generation
- Centralized file path guarding for destructive operations
- SSRF and resource exhaustion protections
- Cache invalidation with concurrency safety
- Correlation ID logging for observability

### Docker Publishing
Official Docker images are now published automatically on every release via GitHub Actions.

- Multi-arch container images pushed to GHCR
- Simplified deployment for self-hosted users

### Additional Improvements

- **Artist alias lookup** - Improved artist matching via alias database schema
- **Bulk artist management** - Directory diagnosis and bulk operations in the admin UI
- **Dashboard enhancements** - Admin checks, health warnings, and loading states
- **MusicBrainz streaming importer** - Generic streaming methods with cancellation support
- **Artist listing improvements** - Album counts and better pagination
- **Track gap calculation** - Enhanced logic for identifying missing tracks

## Upgrading

To upgrade to 2.0.0, follow the [upgrade guide](/upgrade/).

Database migrations will run automatically on startup. Note the migration from SQLite to DecentDB — review the migration notes if you are upgrading from an existing SQLite installation.

## Documentation

- [Event Scripting Guide](/event-scripting/)
- [Onboarding Wizard](/onboarding/)
- [Upgrade Guide](/upgrade/)
- [Docker Deployment](/docker/)
- [Security Overview](/security/)

## Thank You

Thanks to everyone who contributed to this release through bug reports, feature requests, security audits, and pull requests!

Questions or feedback? Join our [Discord community](https://discord.gg/bfMnEUrvbp) or open an issue on [GitHub](https://github.com/melodee-project/melodee/issues).
