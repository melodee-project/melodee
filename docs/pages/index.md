---
layout: page
title: Melodee
description: Self-hosted music management, curation, and streaming with OpenSubsonic, Jellyfin-compatible, and native APIs.
permalink: /
tags:
  - overview
  - self-hosted
  - music
---

# Melodee Music System

Melodee is a self-hosted music-management and streaming server. It processes incoming audio through an Inbound → Staging → Storage workflow, stores the primary catalog in PostgreSQL, and serves the result through its web UI and multiple API surfaces.

## Try the demo

The project demo is at [demo.melodee.org](https://demo.melodee.org):

```text
Username: demo
Password: Mel0deeR0cks!
```

The demo contains sample music and resets periodically. Availability and writable features may be limited.

## How media becomes playable

1. Drop a release directory into Inbound.
2. The ingestion job reads supported audio tags, applies configured cleanup and validation, and creates Melodee metadata.
3. Review the processed album in Staging.
4. Approve it for promotion into a Storage library.
5. Run library insertion so PostgreSQL and generated search data include the new catalog records.
6. Browse or stream through the web player or a compatible client.

See [Libraries](/libraries/) and [Background Jobs](/jobs/) for the exact jobs and default schedules.

## Highlights

- Staged ingestion, validation, metadata editing, artwork, and promotion
- Multiple Storage roots
- Direct streaming and configured FFmpeg transcoding
- PostgreSQL primary persistence and generated DecentDB search databases
- Blazor Server administration and browser playback
- OpenSubsonic compatibility with a route-by-route [support matrix](/opensubsonic-matrix/)
- A documented [Jellyfin-compatible subset](/api-jellyfin/)
- JWT-authenticated [native API](/api/)
- Local operations and limited remote commands through [mcli](/cli/)
- [Podcasts](/podcasts/), [playlists](/playlists/), [charts](/charts/), requests, and Last.fm scrobbling
- Custom [themes](/theming/) and [page blocks](/custom-blocks/)
- Quartz background jobs, Doctor diagnostics, backup, and upgrade workflows

Some newer surfaces are explicitly previews. Read their limitations before deployment:

- [Party Mode](/party-mode/)
- [Shares](/shares/)
- [Event Scripting](/scripting/)
- [User Device Profiles](/user-device-profiles/)

[Jukebox](/jukebox/) requires an MPV or MPD executable that is not included in the standard image.

## Start here

- [Quick Start](/quickstart/) — first container deployment
- [Installation](/installing/) — supported deployment paths
- [Configuration](/configuration/) — initial and ongoing settings
- [Upgrade](/upgrade/) — version changes and verification
- [Backup & Recovery](/backup/) — coordinated PostgreSQL and file backups
- [API Overview](/apis/) — choose the correct interface
- [About](/about/) — architecture, companion clients, and support
- [Changelog](/changelog/) — release history

## Contribute

Report bugs and documentation gaps in the [Melodee GitHub repository](https://github.com/melodee-project/melodee). Focused documentation pull requests are welcome.
