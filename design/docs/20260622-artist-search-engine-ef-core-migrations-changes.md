<!-- markdownlint-disable-file -->
# Release Changes: EF Core Migrations for Artist Search Engine

**Related Plan**: EF Core Migration Implementation for ArtistSearchEngineServiceDbContext
**Implementation Date**: 2026-06-22

## Summary

Migrated the Artist Search Engine DecentDB database from using `EnsureCreatedAsync()` (which only creates schema on first run) to proper EF Core migrations via `MigrateAsync()`. This enables schema versioning, proper migration tracking, and eliminates hand-rolled idempotent DDL blocks that were maintained manually in `ArtistSearchEngineService.cs`.

## Changes

### Added

- `src/Melodee.Common/Data/ArtistSearchEngineServiceDbContextFactory.cs` - Design-time DbContext factory for `dotnet ef` tooling to generate migrations
- `src/Melodee.Common/Migrations.ArtistSearchEngine/ArtistSearchEngine/20260622141948_InitialArtistSearchEngineSchema.cs` - Baseline migration with raw SQL matching the existing production schema exactly
- `src/Melodee.Common/Migrations/ArtistSearchEngine/20260622141948_InitialArtistSearchEngineSchema.Designer.cs` - EF Core migration metadata/snapshot
- `tests/Melodee.Tests.Common/Data/ArtistSearchEngineServiceDbContextMigrationTests.cs` - 3 comprehensive tests validating migration behavior on fresh files, existing schema, and production schema copy

### Modified

- `src/Melodee.Blazor/Program.cs` - Added `MigrationsAssembly("Melodee.Common")` to DecentDB options for ArtistSearchEngineServiceDbContext so EF Core finds the dedicated migration folder
- `src/Melodee.Common/Services/SearchEngines/ArtistSearchEngineService.cs` - Replaced `EnsureCreatedAsync()` + 3 hand-rolled DDL blocks (`EnsureHousekeepingIndexesAsync`, `EnsureLocalArtistAliasLookupAsync`, `BackfillLocalArtistAliasLookupAsync`) with single `MigrateAsync()` call; kept `BackfillLocalArtistAliasLookupAsync` as a data-seed step
- `src/Melodee.Blazor/Services/DoctorService.cs` - Simplified `ProbeArtistSearchDatabaseAsync` to just verify connectivity without running DDL
- `src/Melodee.Cli/Command/DoctorCommand.cs` - Changed new database creation from `EnsureCreatedAsync()` to `MigrateAsync()`

### Removed

- Hand-rolled `EnsureHousekeepingIndexesAsync` method (index creation now in migration)
- Hand-rolled `EnsureLocalArtistAliasLookupAsync` method (table + index creation now in migration)
- Raw `CREATE INDEX IF NOT EXISTS` and `CREATE TABLE IF NOT EXISTS` blocks from startup path

## Release Summary

**Total Files Affected**: 8

### Files Created (4)

- `src/Melodee.Common/Data/ArtistSearchEngineServiceDbContextFactory.cs` - Design-time factory for migration generation
- `src/Melodee.Common/Migrations/ArtistSearchEngine/20260622141948_InitialArtistSearchEngineSchema.cs` - Baseline migration with idempotent raw SQL matching production DecentDB schema
- `src/Melodee.Common/Migrations/ArtistSearchEngine/20260622141948_InitialArtistSearchEngineSchema.Designer.cs` - Migration metadata
- `tests/Melodee.Tests.Common/Data/ArtistSearchEngineServiceDbContextMigrationTests.cs` - Migration tests

### Files Modified (4)

- `src/Melodee.Blazor/Program.cs` - MigrationsAssembly configuration
- `src/Melodee.Common/Services/SearchEngines/ArtistSearchEngineService.cs` - Switched to MigrateAsync, removed hand-rolled DDL
- `src/Melodee.Blazor/Services/DoctorService.cs` - Simplified health probe
- `src/Melodee.Cli/Command/DoctorCommand.cs` - Switched to MigrateAsync for new database creation

### Dependencies & Infrastructure

- **New Dependencies**: None (uses existing `DecentDB.EntityFrameworkCore` and `DecentDB.EntityFrameworkCore.NodaTime`)
- **Updated Dependencies**: None
- **Infrastructure Changes**: Created dedicated migration folder `src/Melodee.Common/Migrations.ArtistSearchEngine/` for isolated migration tracking
- **Configuration Updates**: `MigrationsAssembly("Melodee.Common")` on DbContext options to locate migration history table

### Deployment Notes

1. **Production file compatibility**: The baseline migration uses raw SQL that exactly matches the existing production `.ddb` schema (verified via DecentDB CLI dump). Running `MigrateAsync()` against the existing file is a no-op — it inserts the `__EFMigrationsHistory` row and leaves all tables/indexes/data intact.

2. **New environments**: Fresh `.ddb` files will have the full schema created by the migration's `CREATE TABLE IF NOT EXISTS` / `CREATE INDEX IF NOT EXISTS` statements.

3. **Future schema changes**: New schema modifications should be done by modifying the entity models, then running `dotnet ef migrations add <Name> --project src/Melodee.Common --startup-project src/Melodee.Blazor --context ArtistSearchEngineServiceDbContext --output-dir Migrations.ArtistSearchEngine` to generate proper incremental migrations.

4. **Test fixture**: The production schema test requires setting `MELODEE_TEST_ARTIST_SEARCH_ENGINE_DDB` environment variable to a DecentDB file matching the production schema (not committed due to size).

## Local Development Baselining Fix

**Problem**: Local `.ddb` files created with the old `EnsureCreatedAsync()` path lacked the `__EFMigrationsHistory` table. When `MigrateAsync()` ran, it tried to read the history table (failed), then compared the model snapshot to the database and reported "pending changes" — even though the schema matched.

**Root cause**: The model snapshot (in `Designer.cs`) had `BOOLEAN` for `IsLocked` while the database had `INT64` (from the old `EnsureCreatedAsync()`). EF Core's model validator flagged this as a schema mismatch.

**Fix applied**:
1. Added explicit value converter for `Artist.IsLocked` in `ArtistSearchEngineServiceDbContext.OnModelCreating()` mapping `bool?` → `int?` (0/1) stored as `INTEGER`
2. Updated the baseline migration's `Designer.cs` model snapshot to match the database: `IsLocked` now uses `INTEGER` with the value converter (instead of `BOOLEAN`)
3. **No additional migration was created** — the baseline migration's model snapshot was manually corrected to match the physical database schema, so EF Core's model validator sees zero differences

**Result**: Existing local `.ddb` files with historical data work unchanged. `MigrateAsync()` sees the baseline history row, finds zero pending migrations, and proceeds without errors.