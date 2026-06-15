---
title: "Melodee 2.1.0 Released"
date: 2026-05-24
badges:
  - type: info
    tag: release
---

Melodee 2.1.0 is here! This release focuses on performance, developer experience, and security hardening — making your server faster to load, easier to maintain, and safer to run.

<!--more-->

## What's New in 2.1.0

### SkiaSharp Image Processing
All image processing has migrated to `SkiaSharp`, replacing the legacy library with a more performant and actively maintained alternative.

- New `IImageProcessor` abstraction for decode, encode, resize, format detection, and hash generation
- Clean dependency injection throughout services, Blazor components, CLI commands, and tests
- Conditional native asset inclusion (`SkiaSharp.NativeAssets.Linux` on Linux) — no extra runtime dependencies needed

### Dashboard Performance
Page loads are faster and feel more responsive thanks to targeted optimizations.

- Dashboard data loading now occurs in `OnAfterRenderAsync` — skeleton placeholders render immediately instead of blocking the initial page paint
- `DoctorService.NeedsAttentionAsync` fast path uses lightweight file-existence checks, cutting dashboard first-render time by ~5 seconds

### Bulk Delete Performance
Deleting large collections is now significantly faster.

- `SongService`, `AlbumService`, and `ArtistService` batch-load entities in a single query instead of executing N+1 queries per item
- Dramatic speed improvements for cleaning up large libraries

### Party Mode Optimization
Party Mode has been refactored for direct in-process performance.

- `PartyModeService` now calls domain services (`PartySessionService`, `PartyQueueService`, `PartyPlaybackService`, `PartySessionEndpointRegistryService`) directly via dependency injection instead of making HTTP requests to the same application
- Eliminates ~20 HTTP round-trips per user interaction (create, join, leave, queue, playback, endpoints)
- User identity resolved through `IAuthService` rather than cookie authentication

### Developer Experience
Building and maintaining Melodee is now easier for contributors.

- Squashed 55 EF Core database migrations into a single `InitialBaseline` migration — smaller repository, faster CI builds, no fragile migration chains
- Quartz job scheduling extracted into a reusable helper, shrinking `Program.cs` from 1,046 to ~860 lines
- `.kilo/` project configuration with slash commands (`/build`, `/test`, `/test-mql`, `/lint`, `/migrate`, `/coverage`) and a `melodee-developer` agent for consistent workflows
- CodeQL analysis optimized — JavaScript/TypeScript scanning dropped, saving 5–10 minutes per CI run

### Security Hardening

- Password reset endpoint no longer exposes reset tokens in API responses. Tokens are now only returned in development mode; in production, the endpoint returns a generic message and relies on email delivery.

### Additional Improvements

- Min-width set on album detail action column for layout stability
- Removed unnecessary `EnsureArtistAliasTableAsync` call in MusicBrainz repository
- NuGet dependency updates across the project

## Upgrading

To upgrade to 2.1.0, follow the [upgrade guide](/upgrade/).

Database migrations will run automatically on startup. If you were on the latest 2.0.x migration, the squashed `InitialBaseline` migration is a no-op — existing databases are unaffected.

## Documentation

- [Upgrade Guide](/upgrade/)
- [Docker Deployment](/docker/)
- [Security Overview](/security/)
- [Event Scripting Guide](/event-scripting/)

## Thank You

Thanks to everyone who contributed to this release through bug reports, feature requests, security audits, and pull requests!

Questions or feedback? Join our [Discord community](https://discord.gg/bfMnEUrvbp) or open an issue on [GitHub](https://github.com/melodee-project/melodee/issues).
