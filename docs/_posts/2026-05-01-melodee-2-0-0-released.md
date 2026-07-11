---
title: "Melodee 2.0.0 Released"
description: Historical release notes for Melodee 2.0.0 and its .NET 10, onboarding, automation, and security work.
date: 2026-05-01
tags:
  - release
  - 2.0.0
badges:
  - type: info
    tag: release
---

Melodee 2.0.0 moved the server to .NET 10 and established the current Blazor, PostgreSQL, container, API, and operational baseline.

<!--more-->

## Highlights

### Event scripting

Administrators gained Jint-based JavaScript settings, an editor and test surface, an Inbound directory-processing hook, and selected Blazor page gates. Current scripts fail open; review the [Event Scripting](/scripting/) guide for the exact enforcement boundary.

### Onboarding and Doctor

New installations gained a guided administrator, path, security, branding, and verification workflow plus Doctor checks for missing or invalid setup.

### .NET 10 and generated search stores

The application moved to .NET 10. PostgreSQL remained the primary application database. DecentDB providers were introduced for generated MusicBrainz and artist-search data; they did not replace PostgreSQL.

### Playlist portability

Regular playlists gained M3U/M3U8 import and M3U export, including retention of unmatched import references.

### Security and operations

- API and authentication rate limiting
- Explicit CORS configuration
- Filesystem path guards around destructive operations
- SSRF and response-size protections for external fetches
- Correlation IDs and safer secret handling
- Published multi-architecture container images

## Upgrade

Follow the [Upgrade Guide](/upgrade/). PostgreSQL migrations run during startup; generated DecentDB files have a separate compatibility and [migration procedure](/decentdb/).

## Documentation

- [Quick Start](/quickstart/)
- [Installation](/installing/)
- [Configuration](/configuration/)
- [Event Scripting](/scripting/)
- [Backup & Recovery](/backup/)

Questions or feedback are welcome on [GitHub](https://github.com/melodee-project/melodee/issues) and [Discord](https://discord.gg/bfMnEUrvbp).
