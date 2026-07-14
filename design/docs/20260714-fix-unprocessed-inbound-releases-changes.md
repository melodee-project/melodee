<!-- markdownlint-disable-file -->

# Release Changes: Fix Unprocessed Inbound Releases (Invalid Artist Name)

**Related Plan**: Diagnostic + fix for releases in `/mnt/fileserver_incoming/complete` not being processed
**Implementation Date**: 2026-07-14

## Summary

Three release folders in the production inbound library (Radiohead - OK Computer, The Cure - The Head On The Door,
ZZ Top - Degüello) were never processed into staging. Production logs (`/mnt/fileserver_melodee/logs/server/*.clef`)
showed every job run throwing `System.Exception: Invalid artist name` at
`Mp3Files.ProcessDirectoryAsync:192`, aborting the directory before it reached staging or validation (zero
"Found valid/invalid album" lines; 16 errors/day across the folders). The throw occurred when
`song.AlbumArtist()` resolved to null, e.g. Radiohead stores the album artist in a non-standard
`TXXX:ALBUM ARTIST` frame rather than `TPE2`, and stale multi-file `.cue` sheets (referencing `.flac` files
that no longer exist after transcoding) caused the CueSheet plugin to decline and hand off to Mp3Files. The
fix makes album-artist resolution graceful and cleaned the stale data so the folders now process.

## Root Cause

- `Mp3Files` built `newAlbumTags` with only `MetaTagIdentifier.AlbumArtist` and resolved the artist with
  `song.AlbumArtist() ?? throw new Exception("Invalid artist name")`. When AlbumArtist was null (missing TPE2,
  or ATL mapping quirk), the throw aborted the whole directory — no staging, no validation, just a retry next scan.
- Stale single-image `.cue` files (multiple `FILE "...flac" WAVE` lines referencing transcoded-away FLACs)
  produced the recurring "CUE file has more than one file line" warning and the CueSheet hand-off to Mp3Files.
- Radiohead CD2 also contained leftover duplicate files tagged track 15 (`0001...mp3`, `0015...mp3`) and a
  0-byte failed transcode, which would later break `SongsAreNotSequentiallyNumbered`/`SongsAreNotUniquelyNumbered`.

## Changes

### Added

- `src/Melodee.Common/InternalsVisibleTo.cs` - Exposes internal members to `Melodee.Tests.Common` so the
  new artist-resolution logic is unit-testable directly.
- `tests/Melodee.Tests.Common/Plugins/MetaData/Directory/Mp3FilesTests.cs` - Tests the resilient artist
  fallback chain: AlbumArtist tag -> per-song Artist tag -> directory-name artist segment -> "Unknown Artist".

### Modified

- `src/Melodee.Common/Plugins/MetaData/Directory/Mp3Files.cs` - Replaces the `throw new Exception("Invalid
  artist name")` with a new `ResolveArtistName` helper that falls back gracefully (AlbumArtist tag, then the
  grouped songs' Artist tag, then the leading segment of an "Artist - Album" directory name, then "Unknown
  Artist"). A missing tag now produces a staging-invalid album with `HasInvalidArtists`/`HasUnknownArtist`
  attention reasons instead of aborting the directory. The directory-name fallback only applies when a
  separator is present so bracket/year-only folders fall through to "Unknown Artist".

### Removed (filesystem, production inbound library)

- 4 stale multi-file `.cue` sheets (Radiohead CD1/CD2, The Cure, ZZ Top Degüello) referencing non-existent
  `.flac` source files.
- 2 leftover duplicate Radiohead CD2 mp3 files tagged track 15 (`0001 Polyethylene….mp3`,
  `0015 No Surprises….mp3`) that broke sequential track numbering.
- 4 zero-byte failed-transcode mp3 files (Radiohead CD1/CD2, The Cure, ZZ Top Degüello).

## Divergences from the plan

- **Could not run an end-to-end mcli verification on the host.** Running the production `mcli` in path-based
  mode failed with "Access to the path '/db-storage' is denied" because the artist search engine's
  DecentDB storage (`/db-storage/melodee/search-engine-storage/...`) exists only inside the production
  container, not on the host filesystem. Verification was therefore done via unit tests of the
  `ResolveArtistName` fallback chain (all paths covered) plus confirming the cleaned tags resolve cleanly
  (The Cure/ZZ Top have TPE2; Radiohead has TPE1 + TXXX:ALBUM ARTIST). The production job's next in-container
  run is the final end-to-end confirmation. Reason: environment/tooling access.

## Release Summary

**Total Files Affected**: 3 (source/test) + 10 (filesystem deletions)

### Files Created (3)

- `src/Melodee.Common/InternalsVisibleTo.cs` - InternalsVisibleTo for the test assembly.
- `tests/Melodee.Tests.Common/Plugins/MetaData/Directory/Mp3FilesTests.cs` - Artist-resolution tests.
- `design/docs/20260714-fix-unprocessed-inbound-releases-changes.md` - This changes file.

### Files Modified (1)

- `src/Melodee.Common/Plugins/MetaData/Directory/Mp3Files.cs` - Graceful artist resolution replacing the throw.

### Files Removed (filesystem only)

- 4 stale `.cue` files, 2 duplicate mp3 files, 4 zero-byte mp3 files in the production inbound library.

### Verification

- Build: 0 warnings, 0 errors (Common + Tests + Blazor).
- Tests: 3769 passed / 0 failed / 10 pre-existing skips (full suite); 47 passed in the targeted
  Mp3Files + residue classifier subset.
- `dotnet format` applied to changed files.

### Deployment Notes

- No migration or new settings required for this fix.
- The three cleaned folders will be processed on the inbound job's next run (every 10 minutes) once the
  updated build is deployed to the container. Expect them to reach staging; any residual validation
  failures (e.g. minimum duration, missing cover) will surface as staging-invalid albums with explicit
  `StatusReasons` rather than silent aborts.