---
title: Changelog
permalink: /changelog/
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

## [2.1.1] - 2026-05-25

### Changed

- Updated the docs release dropdown so the latest documentation track points to `2.1.0`, while patch releases continue to use the `2.1.x` application version line.
- Replaced the top-navbar `Documentation` link with `News` so release posts are easier to find from the docs site.
- Expanded `mcli library scan` storage-transfer reporting to separate ready albums, newly moved albums, albums merged with existing storage, duplicate-prefixed staging directories, failed metadata loads, and albums left in staging with their validation reason counts.

### Fixed

- Prevent inbound processing from deleting release directories through directory event scripts; releases now remain available for conversion, normalization, validation, and staging according to the documented ingestion pipeline.
- Preserve copied cover images in staged album metadata after inbound processing renames images to Melodee's normalized `i-##-Type.jpg` filenames.
- Revalidation during the full scan workflow now bypasses same-run negative artist lookup cache entries, allowing albums to become valid when artist metadata is discovered later in the ingestion pass.
- iTunes artist matches now count as trusted artist identities alongside Spotify and MusicBrainz matches, allowing iTunes-only artist lookups to validate staged albums and participate in storage insert matching.
- Staging revalidation now clears stale invalid-artist statuses when an album already contains a trusted artist identity, and iTunes-only artist IDs can be used for storage directory naming.
- Dashboard loading now updates the layout spinner through the layout notification event and clears the global spinner when leaving the page.
- Storage-transfer chaining now treats albums merged into existing storage directories as handled work, so the next ingestion step can continue after merge-only batches.

## [2.1.0] - 2026-05-24

### Changed

- **Replaced `SixLabors.ImageSharp` with `SkiaSharp`** for all image processing operations. A new `IImageProcessor` abstraction centralizes decode, encode, resize, format identification, and average-hash computation. `ImageHasher`, `ImageConvertor`, and `ImageValidator` now receive `IImageProcessor` via dependency injection rather than using static library calls. All services, Blazor components, CLI commands, and test constructors were updated consistently. SkiaSharp native assets are included conditionally (`SkiaSharp.NativeAssets.Linux` on Linux) so builds work across platforms without extra runtime dependencies.
- Set min-width on album detail action column for layout stability
- Remove unnecessary EnsureArtistAliasTableAsync call in MusicBrainz repository
- Dashboard data loading moved from `OnInitializedAsync` to `OnAfterRenderAsync` so skeleton placeholders render immediately instead of blocking the initial page render.
- Bulk delete operations in `SongService`, `AlbumService`, and `ArtistService` now batch-load entities in a single query instead of executing N+1 queries per item, significantly improving performance for large deletions.
- Quartz job scheduling extracted into `QuartzSchedulerExtensions.ScheduleJobIfConfigured<TJob>` helper method, reducing `Program.cs` from 1,046 to ~860 lines and eliminating ~150 lines of repetitive scheduling code.
- **Squashed 55 EF Core migrations into a single `InitialBaseline` migration.** The migration history (107 files spanning Feb 2025 – Jan 2026) has been consolidated into one baseline file that generates the complete current schema. This reduces repository size, speeds up CI builds, and eliminates fragile migration chains. Existing databases that have already applied the latest migration are unaffected; new setups will apply only the single baseline.
- Added `.kilo/` project configuration with slash commands (`/build`, `/test`, `/test-mql`, `/lint`, `/migrate`, `/coverage`) and a project-aware `melodee-developer` agent for consistent developer workflows.
- Dropped JavaScript/TypeScript from CodeQL analysis — the repository contains only minimal JS files (jQuery, lunr.js in docs site), and scanning them wasted ~5–10 minutes per CI run with no security value.
- **Refactored `PartyModeService` to call domain services directly instead of making HTTP requests to the same application.** Replaced `HttpClient` with `PartySessionService`, `PartyQueueService`, `PartyPlaybackService`, and `PartySessionEndpointRegistryService` via dependency injection. User identity resolved through `IAuthService.CurrentUser` rather than cookie auth. Eliminates ~20 HTTP round-trips per user interaction (create, join, leave, queue, playback, endpoints) in party mode Blazor components.

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
- Scrobbling support (Last.fm and compatible services).
- User sharing and playlist management.
- Custom theming with Radzen theme support and custom CSS overrides.
- Multi-library support (Inbound, Staging, Storage).
- Background job scheduling with Quartz.NET.
- Doctor diagnostics for server health checks.
- Onboarding wizard for first-time setup.
- Request system for user-submitted metadata corrections.
- Radio station management.
- Chart generation and display.
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
- Migrated from SQLite to [DecentDB](https://github.com/sphildreth/decentdb)
- Centralized configuration via `IMelodeeConfigurationFactory` with environment variable overrides.

### Fixed

- Various stability and performance improvements across scan pipeline and API endpoints.

### Security

- SSRF validation for podcast and external URL fetching.
- Secret redaction in configuration exports and logs.
- CSRF protection via antiforgery tokens.
- HSTS and security headers middleware.
