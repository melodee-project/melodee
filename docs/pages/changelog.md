---
title: Changelog
description: Release history and notable changes for Melodee.
permalink: /changelog/
tags:
  - release
  - changelog
---

# Changelog

All notable changes to Melodee will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Types of Changes

- **Added** — New features, endpoints, UI pages, or configuration options.
- **Changed** — Modifications to existing functionality or behavior.
- **Deprecated** — Features that will be removed in a future release.
- **Removed** — Features removed in this release.
- **Fixed** — Bug fixes and error corrections.
- **Security** — Vulnerability patches and security hardening.

---

## [Unreleased]

## [2.2.0] - 2026-07-11

### Added

- Admin health checks now recognize DecentDB error 8 and automatically open a
  migration dialog with the matching prebuilt release, configured source and
  destination paths, copyable `decentdb-migrate` commands, verification steps,
  safe replacement scripts, and links to the official DecentDB guidance.
- Media Artist export/import: export all search-engine artist data (artists, albums,
  aliases) as JSON via the `/admin/mediaartists` page; import JSON exports with optional
  overwrite and full preview of artist/album/alias counts.
- ArtistSearch database auto-creation in Blazor Doctor: when the `.ddb` file is missing
  but the parent directory exists, the health check now creates it via EF Core migrations
  instead of reporting an error.

### Changed

- Replaced the hand-rolled raw-SQL ArtistSearch initial migration with a proper
  model-driven EF Core migration that uses `CREATE TABLE IF NOT EXISTS` for idempotent
  application on both fresh and existing databases.
- Added a second `SyncMusicBrainzUuidColumns` migration as a no-op to satisfy EF Core's
  migration chain. DecentDB reports `Guid`/`byte[]` columns as `UUID` in the EF model but
  stores them as `BLOB`, and `ALTER COLUMN TYPE` to `UUID` is unsupported by DecentDB
  (only `INT64`, `FLOAT64`, `TEXT`, `BOOL`). The no-op migration records itself as applied
  without attempting the unsupported `ALTER`, keeping `MigrateAsync` stable.
- Updated the DecentDB providers to `2.16.1`, SkiaSharp managed and Linux native
  assets to `4.150.0`, Radzen to `11.1.3`, and related runtime, UI, test, and
  tooling dependencies.
- Chart editing, user, user-group, and podcast detail routes now bind public
  GUID API keys directly instead of internal numeric IDs or custom string
  parsing.
- Enabled solution-wide XML documentation output so build-time unused-using
  analysis runs consistently, and corrected malformed API documentation and
  formatter configuration.
- Updated the documentation site's default Release, version menu, and search
  scope to `2.2.0`; future version bumps now synchronize these values
  automatically.
- Audited the complete public documentation set against the current source,
  deployment files, CLI help, and API routes; corrected installation,
  configuration, operations, feature-status, and compatibility guidance, and
  refreshed site navigation, metadata, links, and release pages.

### Fixed

- Resolved EF Core "pending model changes" error for `ArtistSearchEngineServiceDbContext`
  by regenerating the model snapshot to match the `IsLocked` `INTEGER` column type
  configured in the DbContext.
- Resolved DecentDB `ALTER COLUMN TYPE supports only INT64, FLOAT64, TEXT, and BOOL` crash
  during ArtistSearch migration by making the `SyncMusicBrainzUuidColumns` migration a no-op.
- ArtistSearch database is now auto-created on first use when the `.ddb` file is absent,
  eliminating the "database is empty or not initialized" error that previously required
  manual intervention.
- Paged ArtistSearch queries now retrieve the requested page before counting
  results, preventing DecentDB positional parameters from carrying across
  commands and causing missing-parameter failures.
- Chart edit navigation now uses the chart API key, and missing chart album
  matching is accent-insensitive.
- Aligned the SkiaSharp managed and Linux native packages and moved image
  drawing to the supported sampling API, preventing native `119.0` and managed
  `150.0` incompatibility failures during image processing.
- Solution Debug and CI analyzer builds now complete without dependency,
  obsolete API, or XML documentation warnings.
- Version bump automation now promotes release notes without moving the fresh
  Unreleased heading above the changelog's Jekyll front matter.
- Remote `mcli` commands now pass the normalized server origin to the HTTP
  client, preventing `/api/v1` from being appended twice to request URLs.
- ArtistSearch initialization now uses migrations for relational databases and
  schema creation for non-relational providers, preventing OpenSubsonic
  `getTopSongs` failures in in-memory hosts.

### Security

- Pinned `Microsoft.OpenApi` to patched version `2.7.5`, preventing crafted
  circular schema references from terminating the process during OpenAPI
  document parsing.
- Password-reset, SMTP, login, migration, profile-lookup, and blacklist logs no
  longer persist email/user identifiers, reset URLs, template subjects,
  configured SMTP hosts, tokens, or raw exception payloads. Reset links also
  require a credential-free absolute HTTP(S) base URL without a query or
  fragment.
- Startup configuration diagnostics now redact passwords, tokens, API keys,
  connection strings, credential-bearing URLs, and unknown environment values
  by default while preserving sanitized operational metadata.
- Both setup utilities now generate independent database and authentication
  secrets without printing them and use one atomic writer with overwrite,
  symlink, non-regular-file, and failure-cleanup protection. Secret files use
  owner-only `0600` permissions on POSIX and the containing directory's ACL on
  Windows.
- Python maintenance tooling now parses GitHub Link headers with bounded linear
  work and no longer prints demo passwords, generated API/public keys,
  encrypted passwords, or secret-bearing exception payloads. The code-scanning
  exporter also confines requests, pagination links, redirects, and downloads
  to HTTPS on the exact configured GitHub API origin.
- The destructive incoming cleanup tool now confines all reads and mutations to
  a canonical cleanup root beneath an explicit trusted boundary, rejects
  symlink/parent escapes, preflights ZIP members against Zip Slip, and prevents
  SFV filenames from traversing outside the media root. Live cleanup uses pinned
  no-follow descriptors and atomic no-replace quarantine moves on Linux and
  fails closed when those primitives are unavailable. Extracted directories
  and files are created privately as `0700` and `0600`. The boundary defaults to
  the current working directory; use `--trusted-boundary` for an explicitly
  authorized absolute media tree.
- Consolidated GitHub Actions, C#, JavaScript/TypeScript, and Python under the
  advanced CodeQL workflow, removed repository-wide rule exclusions, and
  replaced the invalid sanitizer summary with an auto-discovered,
  flow-specific barrier model. Source changes fix the six original C# alerts;
  the Python setup alert is handled by a hardened necessary persistence boundary
  and narrow documented suppression. Fresh local verification reduced C# from
  eight findings to zero and reports zero Actions and JavaScript/TypeScript
  findings. Fresh local-threat-model Python analysis reports zero findings and
  no SARIF warning/error notifications across the complete 45-query default
  suite, while the 52-query security-extended suite also reports zero findings.
- Pinned all external actions across the six CI workflows to verified commits
  (and the Gitleaks container to an immutable digest), and validated release
  image digests before constructing multi-architecture manifests.
- Updated the runtime image to the official .NET 10 Ubuntu 26.04 variant and
  current FFmpeg 8 packages, removed unused vulnerable inherited tooling, and
  made the application process PID 1 under the unprivileged `melodee` account.
  Trivy 0.72.0 validation reduced raw image findings from 416 to 140, with no
  critical, high, fixable, .NET-package, or application-package findings among
  the remainder. The 140 remaining entries represent 41 CVEs: 124 medium and 16
  low, a reduction of 276 findings (66.3%).
- Corrected Trivy SARIF severity filtering so GitHub code scanning receives the
  intended critical/high policy while CI retains a complete all-severity JSON
  report for review.
- Completed security verification includes a zero-warning solution build, 5,885
  passing .NET tests (34 skipped), zero vulnerable NuGet dependencies, and
  successful Jekyll, GitHub Actions, YAML, shell, and real PostgreSQL container
  checks. The production integration runs non-root as PID 1, reaches healthy
  status, and keeps configured secret values out of logs.
- All 109 Python script tests pass with resource warnings treated as errors,
  including 57 focused cleanup filesystem, ZIP, SFV, shutdown, and race
  regressions.

## [2.1.4] - 2026-06-16

### Added

- Added public DecentDB usage and migration documentation covering Melodee's
  generated search databases and rebuild steps for unsupported file-format
  errors.

### Fixed

- Admin dashboard and login health warnings now open-check MusicBrainz and
  ArtistSearch DecentDB files and link to the migration guide when unsupported
  DecentDB file-format versions are detected.
- Manual Library Inbound processing now bypasses the inbound root timestamp
  shortcut so admin-triggered and force-mode scans inspect waiting releases.
- Library Inbound scan selection now treats media folders without
  `melodee.json` as unprocessed even when preserved file timestamps predate
  the previous scan, while still avoiding churn for old folders that already
  have Melodee metadata.

## [2.1.3] - 2026-06-15

### Added

- Added MusicBrainz DecentDB index warm-up after Blazor startup and after
  successful MusicBrainz database promotion, using native .NET queries against
  the request-path indexed lookup shapes.
- Added an internal DecentDB package-upgrade validation gate and runbook for
  repeatable DDB-002/DDB-003 checks against checkpointed MusicBrainz data.

### Changed

- Upgraded `DecentDB.AdoNet`, `DecentDB.EntityFrameworkCore`, and
  `DecentDB.EntityFrameworkCore.NodaTime` to `2.13.1`.
- MusicBrainz query probes now keep the broad ordered first-row existence
  measurement opt-in via `--include-row-existence` so default validation stays
  focused on request-safe indexed lookups.
- Replaced the legacy IdSharp metadata fallback with Melodee's native ID3 tag
  reader, removing the obsolete transitive image-processing dependency path.
- Added explicit private `MessagePack` references for NBomber consumers so the
  test and benchmark graphs resolve the non-vulnerable package version.

### Fixed

- Validated DecentDB `2.13.1` NuGet packages against the checkpointed real
  MusicBrainz query probe, completing DDB-002 and DDB-003 with `IndexSeek`
  plans and sub-millisecond warm indexed equality timings.
- MusicBrainz DecentDB startup warm-up no longer runs the alias-by-artist
  bounded query shape that DecentDB `2.13.1` rejects with a missing-parameter
  error.

## [2.1.2] - 2026-06-10

### Added

- Added DecentDB MusicBrainz import and query probes for real-file performance diagnostics,
  including JSON phase timings, row counts, memory samples, WAL growth, SQL shape,
  DecentDB `EXPLAIN` output, and cold/warm lookup timings.
- Added internal DecentDB search strategy and provider enhancement notes covering ADO.NET
  maintenance APIs, WAL visibility, query diagnostics, large indexed string equality, and
  large-text search guidance.

### Changed

- Upgraded `DecentDB.AdoNet`, `DecentDB.EntityFrameworkCore`, and
  `DecentDB.EntityFrameworkCore.NodaTime` to `2.13.0`.
- Recorded DecentDB `2.13.0` real-file MusicBrainz validation. The package
  provides indexed equality `EXPLAIN` plans for the tested query shapes, but
  DDB-002 and DDB-003 remain provider follow-up because checkpointed large
  `Artist` table equality probes still time out.
- Validated a local DecentDB worktree fix for DDB-002 and DDB-003 through
  direct local binding binaries; the items remain pending until a published
  DecentDB NuGet package reproduces the real-file probe results.
- MusicBrainz DecentDB imports now return materialized row counts and keep final full-table
  verification counts opt-in, avoiding redundant full-table counts during normal imports.
- Local artist cache lookups now use staged exact identifier, normalized name, and normalized
  alias queries, with database-side paging for artist list requests.
- Local artist aliases now use a normalized lookup table that is backfilled on startup for
  existing cache data and synchronized when cached artists change.

### Fixed

- Release editing no longer triggers EasyMDE's Font Awesome CDN stylesheet load,
  preventing Content Security Policy violations in the browser console.
- The Blazor shell now loads the EasyMDE script only once.
- Admin dashboard doctor checks no longer emit Entity Framework warnings for
  unordered row-limiting probes.
- Exact MusicBrainz ID lookups now apply deterministic ordering before row limiting.
- DecentDB improvement tracking now separates completed Melodee changes from provider
  enhancement candidates.
- DecentDB improvement tracking now distinguishes the DecentDB `2.13.0`
  planner/provider fix from the remaining large-file runtime/storage follow-up.
- MusicBrainz database imports now checkpoint through the DecentDB
  `DecentDBMaintenance.CheckpointAsync(...)` API instead of an external process.

## [2.1.1] - 2026-05-25

### Changed

- Updated the docs release dropdown so the latest documentation track points to `2.1.0`, while patch releases continue to use the `2.1.x` application version line.
- Replaced the top-navbar `Documentation` link with `News` so release posts are easier to find from the docs site.
- Expanded `mcli library scan` storage-transfer reporting to separate ready albums, newly moved albums, albums merged with existing storage, duplicate-prefixed staging directories, failed metadata loads, and albums left in staging with their validation reason counts.
- Added `mcli library scan` performance reporting for artist lookup cache behavior, conversion time, copy time, revalidation skips, and DecentDB artist-search persistence retry counts.
- `mcli library scan` now shows live progress messages and item counts for inbound processing, staging revalidation, storage transfer, and database insert work instead of leaving active steps at an apparent 0%.
- `mcli library scan` now suppresses ATL library stack traces during progress rendering and reports non-fatal inbound processing errors as scan warnings instead of letting raw exception text corrupt the TUI.
- Artist search database read/open errors, including non-retryable DecentDB provider failures, are now counted in full-scan performance output and reported as scan warnings.
- `mcli doctor` now validates DecentDB files by checking file presence, opening the configured database, inspecting expected schema tables, and running read queries instead of relying on shallow connection checks.
- Bounded concurrent media conversions during inbound processing to reduce CPU and disk saturation on large batch scans.
- Staging artist revalidation now uses a staging-local `.melodee-revalidation.ddb` retry state database so repeated scans defer recently failed albums instead of re-querying every invalid staged release on every run; the state database is recreated automatically if missing or corrupt.
- Staging artist revalidation now logs the retry state database path and row counts, and `mcli library scan` reports artist lookup attempts and no-match counts for revalidation work.
- MusicBrainz DecentDB artist searches now report phase timings and use a compact release/alias loading path for one-result ingestion lookups.

### Fixed

- Prevent inbound processing from deleting release directories through directory event scripts; releases now remain available for conversion, normalization, validation, and staging according to the documented ingestion pipeline.
- Preserve copied cover images in staged album metadata after inbound processing renames images to Melodee's normalized `i-##-Type.jpg` filenames.
- Revalidation during the full scan workflow now bypasses same-run negative artist lookup cache entries, allowing albums to become valid when artist metadata is discovered later in the ingestion pass.
- iTunes artist matches now count as trusted artist identities alongside Spotify and MusicBrainz matches, allowing iTunes-only artist lookups to validate staged albums and participate in storage insert matching.
- Staging revalidation now clears stale invalid-artist statuses when an album already contains a trusted artist identity, and iTunes-only artist IDs can be used for storage directory naming.
- Dashboard loading now updates the layout spinner through the layout notification event and clears the global spinner when leaving the page.
- Inbound move-mode processing now removes source sidecar metadata files such as `.sfv`, `.nfo`, `.m3u`, `.cue`, and Blackbeard provenance after albums are staged, including metadata-only directories left by earlier runs.
- Inbound staging now tracks media files converted during processing, so NFO-derived albums that start as FLAC are staged from the converted MP3 files instead of leaving converted songs behind in `inbound`.
- Storage-transfer chaining now treats albums merged into existing storage directories as handled work, so the next ingestion step can continue after merge-only batches.
- Full-scan artist lookup work now shares one run-scoped cache across inbound processing and staging revalidation, including forced revalidation lookups.
- Artist search persistence now retries transient DecentDB transaction conflicts while surfacing non-retryable DecentDB provider errors without retrying them.
- Compound release artists such as `Artist One feat. Artist Two` now get conservative fallback artist lookups when the fallback candidate has trusted identity data and matching release evidence.
- Staging revalidation now skips albums whose artist metadata is blank or obviously unsearchable instead of repeatedly calling external artist providers.
- Forced staging revalidation no longer expands compound artist names into multiple fallback provider searches, preventing `mcli library scan` from appearing hung on batches of invalid collaboration artists.
- Move-mode inbound cleanup now removes source residue files such as release artwork and `.txt` notes only after media files are gone, allowing empty inbound release directories to be removed without deleting unprocessed media.
- Inbound processing now defers directories whose files are still changing instead of partially staging releases while the source copy is still in progress.
- Media conversion now accepts a valid generated MP3 when ffmpeg produced usable output but ATL reports an unexpected format label, preventing converted tracks from being stranded in inbound.
- External artist provider searches now honor bounded requested result limits instead of requesting unbounded provider result sets during forced lookups.
- iTunes artist searches now deserialize large Apple artist, collection, AMG, and genre identifiers without failing, and artist image searches correctly send the requested result limit.
- NFO parsing now ignores malformed track lines and missing artist metadata without emitting parser stack traces.
- Inbound staging now reports missing staged files once per album when skipping tag updates instead of generating repeated per-song update warnings.

## [2.1.0] - 2026-05-24

### Changed

- **Replaced the legacy image processing library with `SkiaSharp`** for all image processing operations.
  A new `IImageProcessor` abstraction centralizes decode, encode, resize, format
  identification, and average-hash computation. `ImageHasher`, `ImageConvertor`, and
  `ImageValidator` now receive `IImageProcessor` via dependency injection rather than
  using static library calls. All services, Blazor components, CLI commands, and test
  constructors were updated consistently. SkiaSharp native assets are included
  conditionally (`SkiaSharp.NativeAssets.Linux` on Linux) so builds work across
  platforms without extra runtime dependencies.
- Set min-width on album detail action column for layout stability
- Remove unnecessary EnsureArtistAliasTableAsync call in MusicBrainz repository
- Dashboard data loading moved from `OnInitializedAsync` to `OnAfterRenderAsync` so skeleton placeholders render immediately instead of blocking the initial page render.
- Bulk delete operations in `SongService`, `AlbumService`, and `ArtistService` now batch-load entities in a single query instead of executing N+1 queries per item, significantly improving performance for large deletions.
- Quartz job scheduling extracted into `QuartzSchedulerExtensions.ScheduleJobIfConfigured<TJob>` helper method, reducing `Program.cs` from 1,046 to ~860 lines and eliminating ~150 lines of repetitive scheduling code.
- **Squashed 55 EF Core migrations into a single `InitialBaseline` migration.** The
  migration history (107 files spanning Feb 2025 – Jan 2026) has been consolidated into
  one baseline file that generates the complete current schema. This reduces repository
  size, speeds up CI builds, and eliminates fragile migration chains. Existing databases
  that have already applied the latest migration are unaffected; new setups will apply
  only the single baseline.
- Added `.kilo/` project configuration with slash commands (`/build`, `/test`, `/test-mql`, `/lint`, `/migrate`, `/coverage`) and a project-aware `melodee-developer` agent for consistent developer workflows.
- Dropped JavaScript/TypeScript from CodeQL analysis — the repository contains only minimal JS files (jQuery, lunr.js in docs site), and scanning them wasted ~5–10 minutes per CI run with no security value.
- **Refactored `PartyModeService` to call domain services directly instead of making HTTP
  requests to the same application.** Replaced `HttpClient` with `PartySessionService`,
  `PartyQueueService`, `PartyPlaybackService`, and `PartySessionEndpointRegistryService`
  via dependency injection. User identity resolved through `IAuthService.CurrentUser`
  rather than cookie auth. Eliminates ~20 HTTP round-trips per user interaction (create,
  join, leave, queue, playback, endpoints) in party mode Blazor components.

### Security

- Password reset endpoint no longer exposes reset tokens in API responses. Tokens are now only returned in development mode; in production, the endpoint returns a generic message and relies on email delivery.
---

## [2.0.1] - 2026-05-01

### Added

- Dashboard now loads progressively with skeleton placeholders; each data section renders independently as its query completes instead of blocking the entire page.
- Inline setting editor on the Onboarding verification step — failed configuration checks (e.g., `system.baseUrl`) now show a text input and Save button so admins can fix settings without navigating away.
- Inline setting editor on the Onboarding blocking page — same inline fix capability when redirected for missing configuration.
- `Disabled` parameter on the `ThemeSelector` component for use in read‑only profile forms.
- Theme-aware skeleton loading placeholders — dark gray on dark themes, light gray on light themes.
- Serilog timing instrumentation on `DoctorService.NeedsAttentionAsync` for diagnosing slow health checks.

### Changed

- `DoctorService.NeedsAttentionAsync` fast path now uses lightweight file-existence checks for MusicBrainz and ArtistSearch databases instead of full DB probes, reducing dashboard first-render time by ~5 seconds.
- Dashboard header spinner (`MainLayoutProxyService.ShowSpinner`) now reflects the actual loading state of all dashboard sections.

### Fixed

- Dashboard `OnInitializedAsync` no longer runs twice during prerender/interactive transition, eliminating duplicate database queries on page load.
- Profile page crash caused by missing `Disabled` parameter on `ThemeSelector`.
- Light theme (`theme-default`) sidebar and panel menu now use a white background instead of falling back to dark styles.
- Onboarding blocking page now allows admins to enter and save missing setting values inline.

---

## [2.0.0] - 2026-05-01

> **v2.0.0** marks the current major release line of Melodee, built on .NET 10 with Blazor Server UI, OpenSubsonic-compatible API, and a native Melodee REST API.

### Added

- Blazor Server administrative UI with Radzen component library.
- OpenSubsonic-compatible API for third-party client support.
- Native Melodee REST API with versioned endpoints (`/api/v1/`).
- Party Mode for collaborative queue management.
- Jukebox playback mode for server-side audio playback.
- Podcast discovery, subscription, and playback.
- Event scripting engine for custom automation.
- MQL (Melodee Query Language) for advanced search and filtering.
- Last.fm scrobbling support.
- User sharing and playlist management.
- Custom theming with Radzen theme support and custom CSS overrides.
- Multi-library support (Inbound, Staging, Storage).
- Background job scheduling with Quartz.NET.
- Doctor diagnostics for server health checks.
- Onboarding wizard for first-time setup.
- Request system for user-submitted metadata corrections.
- Radio station management.
- Chart import, album linking, and display.
- User device profiles.
- Multi-language localization (en-US, de-DE, es-ES, fr-FR, it-IT, ja-JP, pt-BR, ru-RU, zh-CN, ar-SA).
- Docker multi-arch images (linux/amd64, linux/arm64) via GitHub Container Registry.
- Scalar OpenAPI documentation UI.
- Rate limiting for API and authentication endpoints.
- JWT and cookie-based authentication.
- CORS policy configuration.
- ETag and response compression support.
- Custom block system for page customization via Markdown/HTML.

### Changed

- Migrated to .NET 10 runtime.
- Continued using PostgreSQL as the primary application database and introduced
  [DecentDB](https://github.com/sphildreth/decentdb) for generated MusicBrainz
  and artist-search data stores.
- Centralized configuration via `IMelodeeConfigurationFactory` with environment variable overrides.

### Fixed

- Various stability and performance improvements across scan pipeline and API endpoints.

### Security

- SSRF validation for podcast and external URL fetching.
- Secret redaction in configuration exports and logs.
- CSRF protection via antiforgery tokens.
- HSTS and security headers middleware.
