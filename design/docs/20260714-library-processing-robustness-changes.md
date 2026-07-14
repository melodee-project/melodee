<!-- markdownlint-disable-file -->
# Release Changes: Library Processing Performance and Robustness

**Related Plan**: Library processing and media metadata discovery review
**Implementation Date**: 2026-07-14

## Summary

Addressed eight performance and robustness issues in the library processing and media
metadata discovery pipeline identified during a code review. Changes span the database-insert
stage, album discovery cache, tag reading, duplicate handling, extension classification,
file fingerprinting, and logging in hot paths.

## Changes

### Added

- src/Melodee.Common/Services/IFileSystemService.cs - Added GetDirectoryLastWriteTimeUtc interface method for directory-change detection.
- src/Melodee.Common/Services/FileSystemService.cs - Implemented GetDirectoryLastWriteTimeUtc using Directory.GetLastWriteTimeUtc.
- tests/Melodee.Tests.Common/Services/MockFileSystemService.cs - Implemented GetDirectoryLastWriteTimeUtc and added SetDirectoryLastWriteTime fluent helper plus Reset cleanup.
- tests/Melodee.Tests.Common/Utility/FileHelperTests.cs - Added case-insensitive extension detection tests for media, image, and metadata file types.
- tests/Melodee.Tests.Common/Services/Scanning/AlbumDiscoveryServiceTests.cs - Added cache invalidation test for directory modification and cache-hit test for unchanged directories.
- tests/Melodee.Tests.Common/Services/Scanning/OptimizedFileOperationsTests.cs - Added fingerprint change-detection test and lastProcessDate guard test.
- tests/Melodee.Tests.Common/Services/FileSystemServiceTests.cs - Added GetDirectoryLastWriteTimeUtc tests for updated, newly created, and non-existent directories.
- tests/Melodee.Tests.Common/Plugins/MetaData/Directory/Mp3FilesTests.cs - Added HandleDuplicates tests for locked/open and deletable duplicate files verifying no-throw behavior.
- design/docs/20260714-library-processing-robustness-changes.md - This changes file.
- docs/pages/changelog.md - Added Unreleased changelog entries for all eight fixes.

### Modified

- src/Melodee.Common/Jobs/LibraryInsertJob.cs - Replaced unbounded Task.WhenAll + Task.Run with bounded Parallel.ForEachAsync (max 4 / ProcessorCount) in LoadAlbumsInParallelAsync.
- src/Melodee.Common/Plugins/MetaData/Song/AtlMetaTag.cs - Delete orphaned embedded image file when validation fails; removed Trace.WriteLine and unused System.Diagnostics import.
- src/Melodee.Common/Services/Scanning/AlbumDiscoveryService.cs - Cache now stores and compares the directory's actual LastWriteTimeUtc for immediate invalidation on external modification; silent catch replaced with debug-logged catch.
- src/Melodee.Common/Metadata/AudioTags/AudioTagManager.cs - Silent catch blocks in AllMediaFilesForDirectoryAsync and NeedsConversionToMp3Async now log exceptions at debug level via Serilog.
- src/Melodee.Common/Plugins/MetaData/Directory/Mp3Files.cs - HandleDuplicates guards File.Delete in try/catch, only prunes in-memory list on success; file-level duplicate delete also guarded; Trace.WriteLine replaced with logger calls; removed unused System.Diagnostics import.
- src/Melodee.Common/Utility/FileHelper.cs - Converted media, image, and metadata extension lists from IEnumerable<string> to HashSet<string> with OrdinalIgnoreCase comparer for O(1) lookups.
- src/Melodee.Common/Services/Scanning/OptimizedFileOperations.cs - Renamed FileHashCache field to FileFingerprintCache and updated XML doc comments to accurately describe fingerprint semantics.
- src/Melodee.Common/Services/Scanning/DirectoryProcessorToStagingService.cs - Replaced all Trace.WriteLine calls with structured LogAndRaiseEvent debug logging.

### Removed

- (none)

## Release Summary

**Total Files Affected**: 16

### Files Created (2)

- design/docs/20260714-library-processing-robustness-changes.md - Implementation tracking document.
- (changelog updated, not created)

### Files Modified (14)

- src/Melodee.Common/Jobs/LibraryInsertJob.cs - Bounded concurrency in batch album loading.
- src/Melodee.Common/Plugins/MetaData/Song/AtlMetaTag.cs - Orphan image cleanup, Trace removal.
- src/Melodee.Common/Services/Scanning/AlbumDiscoveryService.cs - Cache invalidation fix, logged catch.
- src/Melodee.Common/Metadata/AudioTags/AudioTagManager.cs - Logged catches replacing silent catches.
- src/Melodee.Common/Plugins/MetaData/Directory/Mp3Files.cs - Duplicate delete guard, Trace removal.
- src/Melodee.Common/Utility/FileHelper.cs - HashSet extension lookups.
- src/Melodee.Common/Services/Scanning/OptimizedFileOperations.cs - Fingerprint cache rename and doc fix.
- src/Melodee.Common/Services/Scanning/DirectoryProcessorToStagingService.cs - Trace.WriteLine removal.
- src/Melodee.Common/Services/IFileSystemService.cs - New interface method.
- src/Melodee.Common/Services/FileSystemService.cs - New method implementation.
- tests/Melodee.Tests.Common/Services/MockFileSystemService.cs - Mock support for new method.
- tests/Melodee.Tests.Common/Utility/FileHelperTests.cs - Extension detection tests.
- tests/Melodee.Tests.Common/Services/Scanning/AlbumDiscoveryServiceTests.cs - Cache invalidation tests.
- tests/Melodee.Tests.Common/Services/Scanning/OptimizedFileOperationsTests.cs - Fingerprint tests.
- tests/Melodee.Tests.Common/Services/FileSystemServiceTests.cs - Directory write-time tests.
- tests/Melodee.Tests.Common/Plugins/MetaData/Directory/Mp3FilesTests.cs - HandleDuplicates tests.
- docs/pages/changelog.md - Changelog entries.

### Dependencies & Infrastructure

- **New Dependencies**: None
- **Updated Dependencies**: None
- **Infrastructure Changes**: None
- **Configuration Updates**: None

### Deployment Notes

No deployment changes required. All fixes are code-level improvements within the existing
library processing pipeline. No database migrations, no configuration changes, and no new
external dependencies.