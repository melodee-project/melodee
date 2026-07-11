---
title: "Melodee 2.2.0 Released"
description: Melodee 2.2.0 adds guided DecentDB migration, media-artist data transfer, dependency alignment, and release-quality fixes.
date: 2026-07-11
tags:
  - release
  - 2.2.0
badges:
  - type: info
    tag: release
---

Melodee 2.2.0 improves DecentDB compatibility and recovery, media-artist data portability, image-processing stability, API-key routing, build quality, and public documentation.

<!--more-->

## Guided DecentDB migration

Doctor now recognizes DecentDB error 8, **Unsupported database format**, and opens a migration dialog tailored to the affected MusicBrainz or Artist Search file. It shows:

- the detected and required DecentDB versions;
- the configured source and safe destination paths;
- a copyable `decentdb-migrate` command;
- verification and replacement steps;
- links to matching prebuilt DecentDB releases and the official migration guide.

The primary application database remains PostgreSQL. The migration applies only to generated DecentDB search files. See [DecentDB Usage & Migration](/decentdb/).

## Search data and migration reliability

- Artist Search can initialize a missing database through the correct EF Core migration/schema path.
- Artist Search migrations now match the current model and avoid DecentDB's unsupported UUID column-type alteration.
- A compatibility no-op keeps the migration chain stable for existing files.
- Paged Artist Search reads avoid carrying positional parameters between the page query and result count.
- MusicBrainz and Artist Search startup checks provide clearer recovery guidance.

## Media-artist import and export

Administrators can export artist, album, and alias search-engine data as JSON and preview/import it with optional overwrite behavior from the Media Artists administration page.

## Runtime and build quality

- DecentDB providers were updated to 2.16.1.
- Managed and Linux-native SkiaSharp packages were aligned at 4.150.0, fixing native 119.0 versus managed 150.0 startup failures.
- Radzen and related dependencies were updated.
- Chart, user, user-group, and podcast routes now bind public GUID API keys consistently.
- Remote `mcli` commands no longer duplicate `/api/v1` in request URLs.
- Solution-wide documentation/analyzer builds complete without warnings.
- `Microsoft.OpenApi` is pinned to a patched release.
- Password-reset and adjacent SMTP/authentication logs no longer contain user
  identifiers, reset configuration, tokens, or raw exception payloads. Both
  unattended setup paths create generated deployment secrets atomically with
  owner-only `0600` permissions on POSIX (or the containing directory's ACL on
  Windows).
- Startup diagnostics now redact connection strings, passwords, tokens, API
  keys, credential-bearing URLs, and unknown environment values by default.
- Python maintenance utilities now use bounded Link-header parsing and omit
  demo credentials, generated key material, and sensitive exception payloads.
- Incoming cleanup now enforces canonical trusted-root containment, symlink and
  Zip Slip defenses, and traversal-safe SFV processing before destructive work.
- The production runtime now uses the official .NET 10 Ubuntu 26.04 base with
  FFmpeg 8, removes unused vulnerable inherited tools, and runs the application
  as the unprivileged PID 1 process.
- CodeQL now covers GitHub Actions, C#, JavaScript/TypeScript, and Python
  through one hardened advanced workflow. Trivy uploads only critical/high
  SARIF findings while retaining its complete report as a CI artifact.
- All external actions in the six CI workflows are pinned to verified commits;
  the Gitleaks container is pinned to an immutable digest.

## Documentation and release automation

The public documentation now defaults to the 2.2.0 release. The version-bump script also updates the documentation release menu, search scope, and current release marker for future releases.

The full documentation corpus was reviewed against the 2.2.0 source. Preview boundaries, API routes, CLI commands, container paths, job schedules, settings, and backup/upgrade procedures now distinguish implemented behavior from planned or partially wired features.

## Upgrade

Back up PostgreSQL and persistent files, pin the `2.2.0` image, and follow the [Upgrade Guide](/upgrade/). If Doctor reports DecentDB error 8 after startup, follow its generated dialog before replacing any search database.

Questions or feedback are welcome on [GitHub](https://github.com/melodee-project/melodee/issues) and [Discord](https://discord.gg/bfMnEUrvbp).
