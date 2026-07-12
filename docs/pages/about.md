---
title: About
description: Melodee's purpose, architecture, companion clients, support channels, and license.
permalink: /about/
tags:
  - about
  - architecture
  - community
---

# About

Melodee is an open-source, self-hosted system for managing and streaming large music libraries. It combines a staged media-ingestion workflow, a Blazor Server web application, PostgreSQL metadata storage, generated DecentDB search stores, and OpenSubsonic, Jellyfin-compatible, and native REST interfaces.

## Project goals

Melodee is designed to:

- turn inconsistent incoming releases into a reviewed Storage library;
- retain operator control over metadata, artwork, validation, and promotion;
- serve music to the built-in browser player and compatible external clients;
- support repeatable container deployment, backup, upgrade, and diagnostics;
- scale through paged queries, background jobs, incremental work, and direct streaming where possible.

“Designed for large libraries” is an architectural goal, not a fixed capacity guarantee. Actual limits depend on PostgreSQL, filesystem latency, media format, concurrent users, transcoding, and host resources. See [Hardware & Performance](/hardware/) for measurement-based sizing guidance.

## Server components

| Component | Responsibility | Technology |
|---|---|---|
| Melodee.Blazor | Web UI, authentication, APIs, streaming, and scheduled-job host | .NET 10, Blazor Server, Radzen |
| Melodee.Common | Domain models, ingestion, metadata, services, persistence, and plug-ins | .NET class library, EF Core |
| Melodee.Cli (`mcli`) | Local maintenance and a small remote command subset | .NET console application |
| PostgreSQL | Primary users, catalog, settings, playlists, requests, and history database | PostgreSQL 17 in the supplied Compose deployment |
| DecentDB files | Generated MusicBrainz and artist-search data | DecentDB |

The primary application database is PostgreSQL. DecentDB does not replace it; see [DecentDB Usage & Migration](/decentdb/).

## Interfaces

- The **native API** provides JWT-authenticated, versioned JSON routes under `/api/v1`.
- The **OpenSubsonic API** provides compatibility routes under `/rest`.
- The **Jellyfin-compatible API** implements a documented subset for music clients.
- The **Blazor UI** covers administration, curation, browsing, playback, and operational status.
- **mcli** provides local database/filesystem operations and a limited remote JWT mode.

Compatibility is endpoint-specific. Consult [API Overview](/apis/), the [OpenSubsonic matrix](/opensubsonic-matrix/), and the [Jellyfin guide](/api-jellyfin/) rather than assuming full protocol or client parity.

## Companion clients

- [MeloAmp](https://github.com/melodee-project/meloamp) is the Electron/React desktop client. Its repository documents current Linux, Windows, and macOS targets, features, packages, and releases.
- [Melodee Player](https://github.com/melodee-project/melodee-player) is the Kotlin/Compose Android and Android Auto client. Its repository documents current Android requirements and automotive capabilities.

These projects have independent release cycles. Use each repository's README and Releases page as the source of truth for installation and current client features.

Third-party OpenSubsonic and Jellyfin clients may also connect to the implemented compatibility surfaces. Their behavior varies by the routes and authentication patterns they use.

## Typical uses

- A private household or homelab music server
- A staged workflow for cleaning and organizing incoming releases
- Multiple Storage roots across local disks or NAS mounts
- A server for desktop, mobile, and automotive clients
- A music catalog exposed to trusted integrations through the native API

Features marked **Preview** in their guides are not security or compatibility guarantees. In particular, review the current boundaries for [Party Mode](/party-mode/), [Shares](/shares/), [User Device Profiles](/user-device-profiles/), and [Event Scripting](/scripting/).

## Support and contributions

- Use this documentation for installation, configuration, operation, and API behavior.
- Open a [GitHub issue](https://github.com/melodee-project/melodee/issues) for a reproducible bug or documentation defect.
- Use [GitHub Discussions](https://github.com/melodee-project/melodee/discussions) for design and deployment discussion.
- Join the [Discord community](https://discord.gg/bfMnEUrvbp) for community conversation.

Contributions are welcome. Review the repository's contributing guide, code of conduct, and local `AGENTS.md` before changing code.

## License

Melodee is distributed under the MIT License. See the repository's `LICENSE` file for the authoritative terms.

