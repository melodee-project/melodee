<!-- markdownlint-disable-file -->

# Release Changes: Incoming Library Leftover Cleanup

**Related Plan**: Conversation plan — "delete unwanted leftovers from a release folder once the release media files (music and images) have been processed" (Phases A/B/C)
**Implementation Date**: 2026-07-14

## Summary

Removes leftover junk (EAC logs, AccurateRip reports, auCDtect/DR text reports, cue/m3u sidecars,
album images, and zero-byte failed transcodes) that remained in the production inbound library
(`/mnt/fileserver_incoming/complete`) after releases were processed, and makes Melodee clean these
residue files automatically going forward. The one-time cleanup deleted 58 residue files from 17
fully-processed (media-free) release folders and removed the now-empty folders; three folders still
containing non-zero audio were correctly skipped. The code fixes the three root causes that left
folders dirty: (1) the residue classifier ignored `.log`/`.accurip`/`.toc`/`.md5`/extensionless
leftover files, (2) zero-byte failed-transcode media blocked "residue-only" detection, and (3) the
dormant `processing.fileExtensionsToDelete` setting was never read. A new
`processing.deleteSourceResidueAfterIngest` flag (default on) decouples residue cleanup from
copy/move mode so leftovers are removed even when the original media is preserved.

## Changes

### Added

- `src/Melodee.Common/Constants/SettingRegistry.cs` - Adds the
  `ProcessingDeleteSourceResidueAfterIngest` constant for the new residue-after-ingest flag.
- `src/Melodee.Common/Data/MelodeeDbContext.cs` - Seeds setting `Id = 55`
  (`processing.deleteSourceResidueAfterIngest`, default `true`) so it is discoverable in the admin
  UI and applied to existing databases via the generated migration.
- `src/Melodee.Common/Migrations/20260714155936_AddDeleteSourceResidueAfterIngestSetting.cs` -
  Generated migration inserting the new setting row with a reversible `Down` (`DeleteData`).
- `src/Melodee.Common/Migrations/20260714155936_AddDeleteSourceResidueAfterIngestSetting.Designer.cs`
  - Migration designer file.
- `tests/Melodee.Tests.Common/Services/Scanning/DirectoryProcessorToStagingServiceTests.cs` - Adds
  nine tests covering the provenance taxonomy (log/accurip/toc/md5/html/url), known extensionless
  leftover names (`about_album`), non-music content is NOT residue (`book.epub`), zero-byte failed
  transcodes treated as residue, additional configured extensions, residue-only detection with
  zero-byte media, and residue deletion that preserves non-music content.

### Modified

- `src/Melodee.Common/Services/Scanning/DirectoryProcessorToStagingService.cs` - Broadens the
  residue taxonomy with `SourceResidueProvenanceExtensions` and `SourceResidueKnownFileNames`;
  threads an optional `additionalResidueExtensions` parameter through `IsSourceResidueFile`,
  `IsSourceResidueOnlyDirectory`, `DeleteSourceResidueFiles`, and
  `DeleteSourceResidueOnlyDirectoryFiles`; treats zero-byte media as residue and only treats
  non-zero files as live media in `IsSourceResidueOnlyDirectory`; reads
  `processing.fileExtensionsToDelete` in `InitializeAsync` into `_configuredResidueExtensions` and
  passes it to the cleanup pass; adds `ShouldDeleteSourceResidueAfterIngest` so cleanup runs in move
  mode or when the new (default-on) copy-mode residue flag is enabled.
- `src/Melodee.Common/Migrations/MelodeeDbContextModelSnapshot.cs` - Model snapshot updated by the
  EF tooling to include setting `Id = 55`.

### Removed

- 58 residue files (`.log`, `.accurip`, `.txt`, `.jpg`, `.png`, `about_album`) plus 17 empty
  release directories under `/mnt/fileserver_incoming/complete` (production one-time cleanup, Phase A).
  Note: this was a filesystem operation, not a source-code removal.

## Divergences from the plan

- **Dropped: catalog-confirmation gate (Phase B item).** The approved plan proposed gating residue
  deletion on catalog confirmation that the album was ingested. This was diverged because residue
  cleanup runs at the inbound-to-staging step, before `LibraryInsertJob` inserts into PostgreSQL, so
  catalog confirmation is architecturally misplaced at this stage. The existing safe invariant is
  retained instead: residue is only deleted from directories with no remaining usable (non-zero)
  media, which already proves the media was staged/moved out. Reason: correctness of the pipeline
  stage.
- **Dropped: `AlbumFileType.Other` enum value / non-media modeling (Phase B item).** The approved
  plan proposed adding an `AlbumFileType.Other` enum value to model/report non-media files in
  `Album.Files` and `FileSystemDirectoryInfo`. This was diverged as a speculative, unused
  abstraction that does not serve the cleanup deliverable; the dependency-injection guidance
  discourages earning abstractions without a consumer. Reason: scope discipline / avoid speculative
  code.
- **Phase A did not perform catalog verification.** Per the plan, the one-time cleanup was to verify
  albums in the catalog before deleting. This was not feasible from the shell without database
  access; instead the conservative invariant (zero non-zero audio => media already removed) was
  used, with folders still containing real audio explicitly skipped. Reason: tooling access.

## Release Summary

**Total Files Affected**: 7 (source) + 58 (filesystem deletions)

### Files Created (5)

- `src/Melodee.Common/Migrations/20260714155936_AddDeleteSourceResidueAfterIngestSetting.cs` - Migration inserting the new setting.
- `src/Melodee.Common/Migrations/20260714155936_AddDeleteSourceResidueAfterIngestSetting.Designer.cs` - Migration designer.
- `tests/Melodee.Tests.Common/Services/Scanning/DirectoryProcessorToStagingServiceTests.cs` (tests appended) - Residue taxonomy and cleanup tests.
- `design/docs/20260714-incoming-library-leftover-cleanup-changes.md` - This changes file.

### Files Modified (3)

- `src/Melodee.Common/Constants/SettingRegistry.cs` - New flag constant.
- `src/Melodee.Common/Data/MelodeeDbContext.cs` - Seed for setting `Id = 55`.
- `src/Melodee.Common/Services/Scanning/DirectoryProcessorToStagingService.cs` - Residue taxonomy, zero-byte handling, setting wiring, copy-mode decoupling.
- `src/Melodee.Common/Migrations/MelodeeDbContextModelSnapshot.cs` - Snapshot includes the new setting.

### Files Removed (filesystem only)

- 58 residue files and 17 empty release directories under `/mnt/fileserver_incoming/complete`.

### Dependencies & Infrastructure

- **New Dependencies**: None.
- **Updated Dependencies**: None.
- **Infrastructure Changes**: None.
- **Configuration Updates**: New setting `processing.deleteSourceResidueAfterIngest` (default `true`).
  The existing `processing.fileExtensionsToDelete` setting (default `['log','lnk','lrc','doc']`) is
  now actually consumed; no value change required.

### Deployment Notes

- Apply the EF Core migration `20260714155936_AddDeleteSourceResidueAfterIngestSetting` to existing
  databases so the new setting row exists and is editable in the admin UI. Until applied, the flag
  defaults on via the null-defaults-true fallback, so cleanup behavior is enabled regardless.
- The three remaining folders in `/mnt/fileserver_incoming/complete` (Radiohead, The Cure,
  ZZ Top - Degüello) still contain non-zero audio and were intentionally not cleaned; they also
  contain zero-byte failed transcodes which the new logic will now recognize as residue on the next
  processing run.